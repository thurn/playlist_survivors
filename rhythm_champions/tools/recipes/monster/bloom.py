"""The Bloom (ps-8z4.11), Boss Rush boss 3: the vendor's carnivorous plant rebuilt as a few simple faceted shapes,
every edge it designs drawn, around what names it: a flytrap of two toothed lobes, a ruff of big petals behind it,
a stem and eight roots.

The trailer's drawing of the vendor mesh (2,600 triangles) reads at once as a mouth on legs, but breaks the look: the
trap's upper lobe is a smooth dome whose rim lights as a soft gradient, with short stitches floating on its face; the
teeth are dozens of thin spikes that smear into a hatched band at game size; the roots and leaves are hatched with
chevrons. From the game's camera, in front and above, the trap faces the viewer, the ruff of leaves stands behind it
and the roots splay round. What changes:

- The trap: each lobe a clamshell whose ribs fan from a short hinge bar over a crown to the lip, as a flytrap's do,
  rigid on its jaw bone; one big three-sided tooth on each lip edge, splayed out over the rim so the toothed mouth
  shows from above (fewer, larger teeth: the first blind critique). Each lobe's mouth is a shallow funnel of its own,
  only its rim drawn, so the lip's fold doesn't average the shell's normals with the mouth's into a rim-lit glow. The
  upper lobe's hinge sits above its lip, so its back slopes gently rather than catching the rim edge-on. A small
  hinge knob joins the lobes to the stem on the head bone.
- The ruff: five big kite petals fanned round the back of the trap from the stem's top, a midrib splitting each in two
  facets, thin membranes whose outlines are bevelled (the dragon's wings), their normals leant halfway up so they stay
  dark. They replace the vendor's six curled leaves there (the theme's petals), short enough that the trap, not the
  ruff, leads (the second blind critique: two starbursts of equal weight read as an urchin).
- The stem: one five-sided prism through the neck's joints; the bulb a low eight-sided dome; the three leaves at the
  bulb kept as petals, the mid-stem leaves dropped as clutter under the trap.
- The roots: each one thin four-sided prism, a ridge on top, through every other bone to its point (thinner than the
  vendor's, so the body stands out in a crowd: the first blind critique), the chevrons gone.

The stem, bulb and roots lean their normals up 0.6 and the trap 0.4, so big facets turned away stay dark (leant
further, a lobe's shard turned over in the shatter drew solid). Every shape draws its polygons' outlines, never their
fan diagonals; of the petals' bevels only the outline draws. Shapes are skinned by transfer from the vendor's nearest
vertices over their own bones (never across roots), the lobes, knob and petals rigid. Joints are read from the
vendor's vertices weighted to both bones.

Source: Forest Creatures Pack's Carnivorous Plant-Red.prefab (sha256
29776245832e80d013091e867d34f8b54ae5a839f4ef6ba607ac4dd7d2ea38cb) and Carnivorous Plant.FBX
(8eba906925a91f84ee9208f4d28b045658a28b78323df7bfa6be303a7ef7cff7): the prefab and its clips are in manifest.json,
the mesh in the prefab's dependency closure. No Blender: numpy only. Bake: `just champions-tool monster bake bloom`;
review: `just champions-tool monster review bloom` (and `--look trailer`).
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

X, Y, Z = np.eye(3)


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv), the first at u turned by phase."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre, np.float64) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


def flat(centre, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """A horizontal ring: x by radii[0], z by radii[1], the first point straight ahead (+z) turned by phase."""
    return ring(centre, Z, X, (radii[1], radii[0]), n, phase)


class Builder:
    """Closed shapes of convex polygons, each wound outward; every polygon's outline draws unless the edge is hushed.
    Each shape is skinned rigidly to one bone, or by transfer from the vendor vertices near it over a set of bones."""

    def __init__(self, source: Source):
        self.source = source
        self.points: list[np.ndarray] = []
        self.skins: list[object] = []
        self.carried: list[bool] = []
        self.lean: list[float] = []
        self.polys: list[list[int]] = []
        self.quiet: set[tuple[int, int]] = set()
        self.fixed: dict[int, np.ndarray] = {}

    def shape(self, points, faces, skin, carried: bool = False, inside=None) -> list[int]:
        """A closed shape: points (n, 3), faces (convex polygons of point indices) wound outward from `inside` (the
        shape's centroid, or one point per face); `skin` a bone name or a tuple of bone names to transfer over."""
        pts = np.asarray(points, np.float64)
        first = len(self.points)
        for p in pts:
            self.points.append(p)
            self.skins.append(skin)
            self.carried.append(carried)
            self.lean.append(0.0)
        centre = pts.mean(axis=0)
        for i, f in enumerate(faces):
            corners = pts[list(f)]
            normal = np.cross(corners, np.roll(corners, -1, axis=0)).sum(axis=0)  # Newell's, as wound
            ref = centre if inside is None else np.asarray(inside[i] if np.ndim(inside) == 2 else inside)
            if normal @ (corners.mean(axis=0) - ref) < 0:
                f = list(reversed(f))
            self.polys.append([first + k for k in f])
        return list(range(first, first + len(pts)))

    def loft(self, rings, skin, carried: bool = False) -> list[list[int]]:
        """A shape lofted through rings of equal count (a ring may be one point, an apex); each side face is wound
        outward from the axis between its two rings' centres. Returns each ring's point indices."""
        rings = [np.atleast_2d(np.asarray(r, np.float64)) for r in rings]
        n = max(len(r) for r in rings)
        pts, offsets = [], []
        for r in rings:
            offsets.append(len(pts))
            pts.extend(r)
        faces, inside = [], []
        for i in range(len(rings) - 1):
            a, b = offsets[i], offsets[i + 1]
            na, nb = len(rings[i]), len(rings[i + 1])
            mid = (rings[i].mean(axis=0) + rings[i + 1].mean(axis=0)) / 2
            for k in range(n):
                if na == 1:
                    faces.append([a, b + k, b + (k + 1) % nb])
                elif nb == 1:
                    faces.append([a + k, a + (k + 1) % na, b])
                else:
                    faces.append([a + k, a + (k + 1) % na, b + (k + 1) % nb, b + k])
                inside.append(mid)
        axis = rings[-1].mean(axis=0) - rings[0].mean(axis=0)
        if len(rings[0]) > 2:
            faces.append(list(range(offsets[0], offsets[0] + len(rings[0]))))
            inside.append(rings[0].mean(axis=0) + axis * 0.5)
        if len(rings[-1]) > 2:
            faces.append(list(range(offsets[-1], offsets[-1] + len(rings[-1]))))
            inside.append(rings[-1].mean(axis=0) - axis * 0.5)
        ids = self.shape(pts, faces, skin, carried, np.array(inside))
        return [ids[o:o + len(r)] for o, r in zip(offsets, rings)]

    def lean_up(self, ids, lean: float) -> None:
        """These points' normals lean up by `lean` (see normals): a little only, so a shard turned over still shows
        faces whose normals point its own way."""
        for i in ids:
            self.lean[i] = lean

    def sheet(self, points, cells, skin, thickness: float, bevel: float, up) -> list[int]:
        """A thin membrane over convex cells (polygons of `points`), its top facing `up`: top and bottom layers, each
        drawing every cell's outline (its veins), and round the outline two narrow bevels meeting at a ridge `bevel` out
        from it, so the outline has a lit face on both sides of its line and draws as wide as a vein (the dragon's
        wings). Every point's normal is `up` (leant by the caller's choice from the clips), so a membrane turned across
        the view stays dark rather than lighting whole in the rim. Returns the top layer's point ids."""
        mid = np.asarray(points, np.float64)
        n = len(mid)
        up = unit(up)
        uses: dict[tuple[int, int], int] = {}
        for c in cells:
            for a, e in zip(c, np.roll(c, -1)):
                k = (min(a, e), max(a, e))
                uses[k] = uses.get(k, 0) + 1
        rim = [k for k, u in uses.items() if u == 1]
        out = {v: np.zeros(3) for k in rim for v in k}
        centre = mid.mean(axis=0)
        for a, e in rim:
            o = np.cross(mid[e] - mid[a], up)
            owner = next(c for c in cells if a in c and e in c)
            if o @ (mid[a] - mid[owner].mean(axis=0)) < 0:
                o = -o
            out[a] += unit(o)
            out[e] += unit(o)
        order = list(out)
        ridge = {v: 2 * n + i for i, v in enumerate(order)}
        pts = np.vstack([mid + up * thickness / 2, mid - up * thickness / 2,
                         [mid[v] + bevel * unit(out[v]) for v in order]])
        faces, inside = [], []
        for c in cells:
            faces.append(list(c))
            inside.append(mid[c].mean(axis=0) - up)
            faces.append([v + n for v in c])
            inside.append(mid[c].mean(axis=0) + up)
        for a, e in rim:
            faces.append([a, e, ridge[e], ridge[a]])
            inside.append(centre - up * 2)
            faces.append([a + n, e + n, ridge[e], ridge[a]])
            inside.append(centre + up * 2)
        ids = self.shape(pts, faces, skin, inside=np.array(inside))
        for a, e in rim:  # of each bevel only the outline draws, between it and the cell
            self.hush(ids[ridge[a]], ids[ridge[e]])
            self.hush(ids[a], ids[ridge[a]])
            self.hush(ids[e], ids[ridge[e]])
            self.hush(ids[a + n], ids[ridge[a]])
            self.hush(ids[e + n], ids[ridge[e]])
        for v in ids:
            self.fixed[v] = up
        return ids[:n]

    def hush(self, a: int, b: int) -> None:
        self.quiet.add((min(a, b), max(a, b)))

    def triangles(self) -> tuple[np.ndarray, np.ndarray]:
        """Each polygon as a fan, and each triangle's edge flags (1 for the edge opposite a corner)."""
        def key(a: int, b: int) -> tuple[int, int]:
            return min(a, b), max(a, b)

        tris, flags = [], []
        for poly in self.polys:
            n = len(poly)
            outline = {key(poly[i], poly[(i + 1) % n]) for i in range(n)} - self.quiet
            for i in range(1, n - 1):
                t = (poly[0], poly[i], poly[i + 1])
                tris.append(t)
                flags.append([int(key(t[1], t[2]) in outline), int(key(t[2], t[0]) in outline),
                              int(key(t[0], t[1]) in outline)])
        return np.array(tris, np.int32), np.array(flags, np.uint8)

    def normals(self, triangles: np.ndarray) -> np.ndarray:
        """Smooth vertex normals over each shape, as Unity would compute them, each leant up by its point's `lean`: a
        big plate turned across the view then stays dark rather than lighting whole in the fresnel rim, which
        weighs the vertex normal against the facet's."""
        pts = np.array(self.points)
        p = pts[triangles]
        face = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        n = np.zeros_like(pts)
        for k in range(3):
            np.add.at(n, triangles[:, k], face)
        n /= np.linalg.norm(n, axis=1, keepdims=True)
        lean = np.array(self.lean)[:, None]
        n = n * (1 - lean) + Y * lean
        for i, f in self.fixed.items():
            n[i] = f
        return (n / np.linalg.norm(n, axis=1, keepdims=True)).astype(np.float32)

    def skin(self) -> tuple[np.ndarray, np.ndarray]:
        """Each point's four bones: one bone with weight 1, or the weights of the vendor vertices nearest it (not
        carried), blended by inverse distance and kept to the shape's bones."""
        s = self.source
        live = ~s.carried
        vp = s.positions[live].astype(np.float64)
        vb, vw = s.bones[live], s.weights[live].astype(np.float64)
        bones = np.zeros((len(self.points), 4), np.int32)
        weights = np.zeros((len(self.points), 4), np.float32)
        for i, (p, skin) in enumerate(zip(self.points, self.skins)):
            if isinstance(skin, str):
                bones[i, 0], weights[i, 0] = s.bone(skin), 1
                continue
            allowed = np.zeros(len(s.bone_names), bool)
            allowed[[s.bone(n) for n in skin]] = True
            near = allowed[vb][np.arange(len(vb)), np.argmax(vw, axis=1)]  # vendor vertices led by an allowed bone
            d = np.linalg.norm(vp[near] - p, axis=1)
            dense = np.zeros(len(s.bone_names))
            for v in np.argsort(d)[:8]:
                np.add.at(dense, vb[near][v], vw[near][v] / (d[v] + 0.01))
            dense[~allowed] = 0
            top = np.argsort(-dense)[:4]
            bones[i], weights[i] = top, dense[top] / dense[top].sum()
        return bones, weights




def joint(s: Source, a: str, b: str) -> np.ndarray:
    """Where two bones meet at rest: the mean of the vendor vertices weighted to both."""
    ia, ib = s.bone(a), s.bone(b)
    w = s.weights * (s.weights > 0.2)
    on_a = (w * (s.bones == ia)).sum(axis=1) > 0
    on_b = (w * (s.bones == ib)).sum(axis=1) > 0
    both = on_a & on_b
    if not both.any():
        raise ValueError(f"no vertices shared by {a} and {b}")
    return s.positions[both].astype(np.float64).mean(axis=0)


def led(s: Source, *names: str) -> np.ndarray:
    """The rest positions of the vendor vertices each of these bones leads."""
    lead = s.bones[np.arange(len(s.bones)), s.weights.argmax(axis=1)]
    return s.positions[np.isin(lead, [s.bone(n) for n in names])].astype(np.float64)


def tip(s: Source, bone: str, base) -> np.ndarray:
    """The vertex of `bone` farthest from `base`: a limb's end."""
    p = led(s, bone)
    return p[np.argmax(np.linalg.norm(p - base, axis=1))]


def limb(b: Builder, joints, radii, skin, sides: int = 3, ref=Y) -> list[list[int]]:
    """A thin prism through `joints`, its radius at each joint (0: a point), each ring square to the limb there."""
    joints = [np.asarray(j, np.float64) for j in joints]
    segments = [unit(c - a) for a, c in pairwise(joints)]
    rings = []
    for i, (j, r) in enumerate(zip(joints, radii)):
        d = unit(segments[max(i - 1, 0)] + segments[min(i, len(segments) - 1)])
        u = unit(ref - (ref @ d) * d)
        rings.append(j if r == 0 else ring(j, u, np.cross(d, u), (r, r), sides))
    return b.loft(rings, skin)



LEAN = 0.6  # the stem's, bulb's and roots' normals lean up this much, so big facets turned away stay dark
TRAP_LEAN = 0.4  # the lobes' and teeth's: leant further, a lobe's shard turned over in the shatter drew solid

# The head's frame at rest: the vendor's flytrap faces forward and about 20 degrees to its right.
SIDE = unit((0.94, 0.0, -0.34))
FWD = unit((0.34, 0.0, 0.94))


def hp(s: float, f: float, y: float) -> np.ndarray:
    """A point in the head's frame: s across, f forward, y up (metres)."""
    return SIDE * s + FWD * f + Y * y


def oval(f: float, y: float, rs: float, rf: float, n: int) -> np.ndarray:
    """A level ring in the head's frame round (0, f, y), rs across and rf along, an edge (not a corner) in front."""
    t = np.pi / n + 2 * np.pi * np.arange(n) / n
    return np.array([hp(np.sin(a) * rs, f + np.cos(a) * rf, y) for a in t])


HINGE_F = -0.2  # how far forward the trap's hinge is, at the back of its lobes
HINGE_HALF = 0.3
RIBS = 9  # lip points round each lobe, from one side of the hinge round the front to the other
# Each lobe: its lip's centre forward, half width, half depth, the lip's height at the hinge and at the front, how far
# its crown bulges from the lip (up for the upper lobe, down for the lower), and its hinge's height: the upper lobe's
# above its lip, so its back slopes gently and faces the camera rather than catching the rim edge-on.
UPPER = {"f": 0.62, "rs": 1.0, "rf": 0.84, "back": 2.76, "front": 3.3, "bulge": 0.72, "hinge": 3.05}
LOWER = {"f": 0.62, "rs": 1.04, "rf": 0.9, "back": 2.6, "front": 2.3, "bulge": -0.95, "hinge": 2.5}
TOOTH = 0.62
MOUTH_DEPTH = 0.35
TOOTH_SPLAY = 1.4  # how far the teeth lean out over the rim (1: 45 degrees)


def lobe(b: Builder, spec: dict, bone: str) -> None:
    """One lobe of the trap, a clamshell: ribs fan from a short hinge bar over a crown to the lip, as a flytrap's do;
    the mouth a shallow funnel of its own sunk into it; a big tooth on each lip edge round the front, a closed
    three-sided spike splayed out over the rim. Rigid on its jaw bone, the shell's normals leant up."""
    hf, hy = HINGE_F, spec["hinge"]
    sign = np.sign(spec["bulge"])

    def lip_y(f: float) -> float:
        t = np.clip((f - hf) / (spec["f"] + spec["rf"] - hf), 0, 1)
        return spec["back"] + (spec["front"] - spec["back"]) * t

    angles = np.radians(np.linspace(-140, 140, RIBS))
    lip = [hp(np.sin(t) * spec["rs"], spec["f"] + np.cos(t) * spec["rf"], 0) for t in angles]
    lip = [p + Y * lip_y(p @ FWD) for p in lip]
    hinge = [hp(-HINGE_HALF, hf, hy), hp(HINGE_HALF, hf, hy)]  # a short hinge bar, so the ribs don't meet in a point
    crown = []
    for t, p in zip(angles, lip):  # each rib's crown: two thirds out from the hinge, raised by the bulge
        q = hp(0, hf, hy) + (p - hp(0, hf, hy)) * 0.62
        crown.append(q + Y * spec["bulge"] * (0.75 + 0.25 * np.cos(t)))
    pts = hinge + crown + lip
    n, half = RIBS, RIBS // 2
    c0, l0 = 2, 2 + RIBS
    faces = [[0, c0 + k, c0 + k + 1] for k in range(half)]
    faces.append([0, c0 + half, 1])
    faces += [[1, c0 + k, c0 + k + 1] for k in range(half, n - 1)]
    faces += [[c0 + k, l0 + k, l0 + k + 1, c0 + k + 1] for k in range(n - 1)]
    faces.append([0, c0, l0])
    faces.append([1, c0 + n - 1, l0 + n - 1])
    mouth = (hp(0, hf, hy) + np.mean(lip, axis=0)) / 2
    ids = b.shape(pts, faces, bone, inside=mouth - Y * sign * 0.3)
    b.lean_up(ids, TRAP_LEAN)
    # The mouth: a shallow funnel sunk into the lobe from its lip, a shape of its own so the lip's fold (nearly 180
    # degrees) doesn't average the shell's normals and the funnel's into a rim-lit glow; only its rim draws.
    rim = [pts[0]] + lip + [pts[1]]
    funnel = rim + [mouth + Y * sign * MOUTH_DEPTH]
    t = len(rim)
    fids = b.shape(funnel, [[k, (k + 1) % t, t] for k in range(t)], bone, inside=mouth + Y * sign * 2)
    for k in range(t):
        b.hush(fids[k], fids[t])
    centre = hp(0, spec["f"], 0) + Y * lip_y(spec["f"])
    for k in range(1, n - 2):  # a big tooth on each lip edge but the two by the hinge, splayed out over the rim as a
        l, r = lip[k], lip[k + 1]  # flytrap's are, so the toothed rim shows from above
        mid = (l + r) / 2
        out = unit((mid - centre) - ((mid - centre) @ Y) * Y)
        inner = mid - out * 0.16
        apex = mid + unit(out * TOOTH_SPLAY - Y * sign) * TOOTH
        ids = b.shape([l, r, inner, apex], [[0, 1, 3], [1, 2, 3], [2, 0, 3], [0, 2, 1]], bone)
        b.lean_up(ids, TRAP_LEAN)


def head(b: Builder) -> None:
    """The trap: two clamshell lobes on a hinge knob at the stem's top, the knob rigid on the head bone."""
    lobe(b, UPPER, "RigMouthUp")
    lobe(b, LOWER, "RigMouthLow")
    rings = [oval(-1.0, 2.7, 0.2, 0.18, 6), oval(-0.6, 2.7, 0.36, 0.3, 6), oval(-0.15, 2.68, 0.3, 0.2, 6)]
    ids = b.loft(rings, "RigHead")
    b.lean_up([i for r in ids for i in r], LEAN)


NECK = ["RigNeck1", "RigNeck2", "RigNeck3", "RigNeck4", "RigNeck5", "RigNeck6", "RigNeck7", "RigNeck8", "RigNeck9"]


def stem(b: Builder) -> None:
    """The stem: one five-sided prism from the bulb, back and up through the neck's joints to the hinge."""
    s = b.source
    joints = [joint(s, "RigPelvis", "RigNeck1")] + [joint(s, a, c) for a, c in pairwise(NECK)] + \
        [joint(s, "RigNeck9", "RigHead")]
    joints = [joints[0], joints[1], joints[3], joints[5], joints[7], joints[9]]
    radii = [0.24, 0.2, 0.18, 0.17, 0.17, 0.18]
    ids = limb(b, joints, radii, tuple(NECK + ["RigPelvis", "RigHead"]), sides=5)
    b.lean_up([i for r in ids for i in r], LEAN)


def bulb(b: Builder) -> None:
    """The root bulb the roots and stem grow from: a low faceted dome, eight-sided so each root leaves a face."""
    rings = [flat((0, 0.02, 0), (0.62, 0.62), 8, np.pi / 8), flat((0, 0.4, 0), (0.68, 0.68), 8, np.pi / 8),
             flat((0, 0.78, -0.1), (0.3, 0.3), 8, np.pi / 8)]
    ids = b.loft(rings, ("RigPelvis",))
    b.lean_up([i for r in ids for i in r], LEAN)


ROOTS = ["Front", "FrontRight", "MidRight", "BackRight", "Back", "BackLeft", "MidLeft", "FrontLeft"]
ROOT_RADII = [0.19, 0.17, 0.15, 0.12, 0.08, 0.0]  # from the bulb to the point


def roots(b: Builder) -> None:
    """Eight roots, each one thin four-sided prism, a ridge on top, through the centres of its even bones (its
    segments; the odd ones are its short joints) to its point."""
    s = b.source
    for r in ROOTS:
        n = [f"RigLeg{r}{k}" for k in range(1, 11)]
        start = joint(s, "RigPelvis", n[0])
        pts = [start] + [led(s, n[k - 1]).mean(axis=0) for k in (2, 4, 6, 8)] + [tip(s, n[9], start)]
        ids = limb(b, pts, ROOT_RADII, tuple(n), sides=4)
        b.lean_up([i for r in ids for i in r], LEAN)


# The ruff: big petals fanned round the back of the trap from the stem's top, each (degrees round from straight back,
# degrees up from level, length); and the vendor's three leaves at the bulb.
RUFF = [(-90, 30, 1.0), (-45, 38, 1.15), (0, 44, 1.2), (45, 38, 1.15), (90, 30, 1.0)]
PETAL_WIDTH = 0.36  # each side of the midrib, at its shoulder


def petal(b: Builder, base, axis, length: float, bone: str) -> None:
    """A kite of a petal from its base along `axis`: its midrib splits it in two facets, a thin membrane whose outline
    is bevelled (the sheet), its normal its own turned up and leant halfway to the sky so it stays dark."""
    axis = unit(axis)
    across = unit(np.cross(Y, axis)) if abs(axis @ Y) < 0.95 else X
    normal = unit(np.cross(axis, across))
    if normal[1] < 0:
        normal = -normal
    pts = [base, base + axis * length * 0.42 + across * PETAL_WIDTH, base + axis * length,
           base + axis * length * 0.42 - across * PETAL_WIDTH]
    b.sheet(pts, [[0, 1, 2], [0, 2, 3]], bone, 0.025, 0.025, unit(normal + Y))


def ruff(b: Builder) -> None:
    """The bloom's ruff: five petals fanned round the back of the trap, rigid on the stem's top bone."""
    base = joint(b.source, "RigNeck9", "RigHead") + Y * 0.05
    for deg, up, length in RUFF:
        a, e = np.radians(deg), np.radians(up)
        d = (-FWD * np.cos(a) + SIDE * np.sin(a)) * np.cos(e) + Y * np.sin(e)
        petal(b, base + d * 0.15, d, length, "RigNeck9")


def base_leaves(b: Builder) -> None:
    """The vendor's three leaves at the bulb (each a pair of faces there, on the stem's first bone) as petals."""
    s = b.source
    lead = s.bones[np.arange(len(s.bones)), s.weights.argmax(axis=1)]
    on = s.bone("RigNeck1")
    root = joint(s, "RigPelvis", "RigNeck1")
    seen = []
    for comp in components(s):
        if set(lead[comp]) != {on} or len(comp) > 40:
            continue
        p = s.positions[comp].astype(np.float64)
        if any(np.linalg.norm(p.mean(axis=0) - q) < 0.05 for q in seen):  # the pair's back face
            continue
        seen.append(p.mean(axis=0))
        base = p[np.argmin(np.linalg.norm(p - root, axis=1))]
        end = p[np.argmax(np.linalg.norm(p - base, axis=1))]
        petal(b, base, end - base, np.linalg.norm(end - base) * 1.2, "RigNeck1")


def components(s: Source) -> list[np.ndarray]:
    """The vendor mesh's pieces (vertex index arrays), welded by position."""
    _, weld = np.unique(np.round(s.positions, 4), axis=0, return_inverse=True)
    weld = weld.ravel()
    parent = np.arange(weld.max() + 1)

    def find(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for a, c, e in weld[s.triangles]:
        parent[find(a)] = find(c)
        parent[find(c)] = find(e)
    root = np.array([find(w) for w in weld])
    return [np.flatnonzero(root == r) for r in np.unique(root)]


def build(source: Source) -> Mesh:
    b = Builder(source)
    head(b)
    stem(b)
    bulb(b)
    roots(b)
    ruff(b)
    base_leaves(b)
    triangles, edges = b.triangles()
    bones, weights = b.skin()
    return Mesh(np.array(b.points, np.float32), triangles, bones, weights, b.normals(triangles), np.array(b.carried),
                edges)
