"""The Hive Queen (ps-8z4.9), Boss Rush boss 2: the vendor's giant bee rebuilt as a few simple faceted shapes, every
edge it designs drawn, around what names it: two big veined wings, a long striped abdomen curled under her with its
sting, long elbowed antennae, big compound eyes and a crown.

The trailer's drawing of the vendor mesh (1,858 triangles) is half right. The abdomen is already a clean banded shape
with a pointed tip, and the head a faceted dome. But the wings are curved organic outlines with no inner edges: flat
to the camera they are faint hairlines, and through the flap they light whole in the fresnel rim as solid pink blades;
the six legs, the mandibles and the antennae are smooth curves that knot into scratches under the head; the thorax and
head are finely gridded balls that go muddy at game size. The flight clips pitch the bee so the game camera sees the
abdomen's back on top, the thorax, then the face below it. What changes:

- The wings: one big forewing a side, a thin membrane on the vendor's wing bones in the vendor's outline, its veins
  splitting it into five cells as an insect's do, and its outline closed by two narrow bevels (the dragon's). Its
  normals are the direction the wing bones turn toward the sky over every clip (the bone texture), so it stays dark
  through the flap.
- The abdomen: six-sided rings round the vendor's curl, a ridge along the back, each ring a stripe, and a four-sided
  sting to the vendor's tip. The thorax: one six-sided egg of four rings. Both lean their normals up 0.4, so their big
  facets turned away stay dark.
- The head: 1.3 times the vendor's and pushed forward, so the face clears the legs from the game's camera: an
  inverted rounded triangle of three six-sided rings, a big faceted compound eye bulging on each upper side, and a
  five-pointed crown on top: she is the queen.
- The antennae: thin three-sided prisms along the vendor's feelers, ending in a club.
- The legs: the front and back pairs, each one four-sided prism through the vendor's joints. Dropped: the middle pair
  and the mandibles, which knotted into a bright tangle under the head (the first blind critique).

Every shape draws its polygons' outlines, never their fan diagonals; of the wings' bevels only the outline draws.
Each is skinned by transfer from the vendor's nearest vertices over its own bones (never across limbs or sides); the
head, eyes and crown rigid on the head bone. Joints are read from the vendor's vertices weighted to both bones.

Source: Polygonal Creatures Pack's Polygonal Giant Bee Blue.prefab (sha256
b587b298eebd05c813d4d4936f36487262041ebea7011c06933f374662f85ced) and Polygonal Giant Bee.FBX
(03864eee03f89f17ca3e44231df3ad899851e4952fb52e9cfe0f7c753f0a1da2): the prefab and its clips are in manifest.json,
the mesh in the prefab's dependency closure. No Blender: numpy
only. Bake: `just champions-tool monster bake hiveQueen`; review: `just champions-tool monster review hiveQueen` (and
`--look trailer`).
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



LEAN = 0.4  # the abdomen's and thorax's normals lean up this much, so their big facets turned away stay dark


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


# The abdomen curls under the body at rest, round an axis along x through CURL (y, z); each station is an angle round
# it (degrees from +z toward +y), its half width across x and half depth along the curl's radius.
CURL = np.array([0.8, -0.3])
CURL_R = 0.93
ABDOMEN = [(84, 0.2, 0.17, 0.84), (104, 0.55, 0.36, CURL_R), (132, 0.8, 0.47, CURL_R), (166, 0.86, 0.5, CURL_R),
           (202, 0.8, 0.48, CURL_R), (236, 0.62, 0.4, CURL_R), (266, 0.4, 0.28, 0.97), (292, 0.2, 0.15, 1.02)]
ABDOMEN_X = 0.12


def abdomen(b: Builder) -> None:
    """The queen's long abdomen, curled under her: six-sided rings round the curl, a ridge along the back, each ring a
    stripe; and the sting, a long cone to the vendor's tip."""
    s = b.source
    rings = []
    for deg, half_w, half_d, r in ABDOMEN:
        t = np.radians(deg)
        out = np.array([0.0, np.sin(t), np.cos(t)])  # away from the curl's axis
        centre = np.array([ABDOMEN_X, CURL[0], CURL[1]]) + out * r
        rings.append(ring(centre, out, X, (half_d, half_w), 6))
    ids = b.loft(rings, ("RigTail1", "RigTail2", "RigTail3", "RigTail4"))
    b.lean_up([i for r in ids for i in r], LEAN)
    last = rings[-1].mean(axis=0)
    sting = tip(s, "RigTail4", joint(s, "RigTail3", "RigTail4"))
    d = unit(sting - last)
    u = unit(X - (X @ d) * d)
    limb(b, [last, last + d * 0.12, sting], [0.11, 0.08, 0], "RigTail4", sides=4, ref=u)


def thorax(b: Builder) -> None:
    """The thorax: one faceted egg from the neck to the waist, six-sided rings across z, a corner up (the back's
    ridge)."""
    rings = [ring((0.0, 1.86, z), Y, X, (ry, rx), 6) for z, rx, ry in
             ((0.86, 0.22, 0.2), (0.5, 0.52, 0.5), (0.0, 0.42, 0.4), (-0.2, 0.18, 0.18))]
    ids = b.loft(rings, ("RigBody",))
    b.lean_up([i for r in ids for i in r], LEAN)


HEAD_X = -0.07
HEAD_PIVOT = np.array([HEAD_X, 1.82, 0.77])  # the neck, about which the head is grown and pushed forward
HEAD_SCALE = 1.3
HEAD_SHIFT = np.array([0.0, 0.05, 0.12])


def hd(points) -> np.ndarray:
    """Head points grown about the neck and pushed forward, so the face clears the legs from the game's camera."""
    return HEAD_PIVOT + HEAD_SHIFT + (np.asarray(points, np.float64) - HEAD_PIVOT) * HEAD_SCALE


def head(b: Builder) -> list[np.ndarray]:
    """The head: an inverted rounded triangle from the front, wide at the eyes and narrowing to the jaws, lofted from
    the neck to the face; a big compound eye on each upper side; returns the antennae's roots."""
    def section(z, w, top, cheek, chin, cw):
        x = HEAD_X
        return np.array([(x - w * 0.62, top, z), (x + w * 0.62, top, z), (x + w, cheek, z), (x + cw, chin, z),
                         (x - cw, chin, z), (x - w, cheek, z)])

    rings = [hd(section(0.8, 0.3, 2.0, 1.78, 1.55, 0.12)), hd(section(1.1, 0.5, 2.14, 1.8, 1.42, 0.2)),
             hd(section(1.46, 0.3, 1.94, 1.68, 1.42, 0.1))]
    ids = b.loft(rings, "RigHead")
    pts = np.array(b.points)
    for side in (1, -1):  # an eye on each upper side face: between the top corner and the cheek of rings 1 and 2
        i0, i1 = (1, 2) if side > 0 else (0, 5)
        q = [pts[ids[1][i0]], pts[ids[2][i0]], pts[ids[2][i1]], pts[ids[1][i1]]]
        c = np.mean(q, axis=0)
        normal = unit(np.cross(q[1] - q[0], q[3] - q[0]))
        if normal @ (c - hd([HEAD_X, 1.78, 1.1])) < 0:
            normal = -normal
        bulge = c + normal * 0.1
        outline = [c + (p - c) * 0.82 + normal * 0.03 for p in q]
        eye = np.vstack([outline, [bulge]])
        b.shape(eye, [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [3, 2, 1, 0]], "RigHead", inside=c - normal * 0.05)
    top = rings[2]
    return [top[1] * 0.7 + top[0] * 0.3 + (0, -0.05, 0), top[0] * 0.7 + top[1] * 0.3 + (0, -0.05, 0)]


def crown(b: Builder) -> None:
    """The queen's crown: a five-sided band on the crown of the head behind the antennae, and a pyramid of a spike up
    from each top edge (thin plates drew as open triangles, the second blind critique)."""
    c = hd([HEAD_X, 2.08, 1.1])
    lower = flat(c - (0, 0.04, 0), (0.24, 0.22), 5)
    upper = flat(c + (0, 0.14, 0), (0.28, 0.26), 5)
    b.loft([lower, upper], "RigHead")
    for k in range(5):
        a, e = upper[k], upper[(k + 1) % 5]
        apex = c + ((a + e) / 2 - c) * 1.15 + (0, 0.36, 0)
        back = c + ((a + e) / 2 - c) * 0.6 + (0, 0.1, 0)  # inside the band: each spike a closed pyramid
        b.shape([a, e, apex, back], [[0, 1, 2], [1, 3, 2], [3, 0, 2], [0, 3, 1]], "RigHead")


def antennae(b: Builder, roots) -> None:
    """Long elbowed antennae along the vendor's feelers, thin three-sided prisms ending in a club."""
    s = b.source
    for side, root in zip("RL", roots):
        f = [f"Rig{side}Feeler{k}" for k in range(1, 9)]
        joints = [root] + [joint(s, f[k], f[k + 1]) for k in (0, 1, 2, 3, 4, 5)]
        end = led(s, f[6], f[7])
        end = end[np.argmax(np.linalg.norm(end - joints[-1], axis=1))]
        pts = joints + [end]
        radii = [0.05, 0.045, 0.04, 0.04, 0.045, 0.06, 0.03]
        limb(b, pts, radii, tuple(f[:7]))


def legs(b: Builder) -> None:
    """Four legs, the front and back pairs, each one four-sided prism through the vendor's joints to its foot. Six thin
    legs and the mandibles knotted into a bright tangle under the head at game size (the first blind critique)."""
    s = b.source
    for side in "RL":
        for leg in ("Front", "Back"):
            n = [f"Rig{side}{leg}Arm{k}" for k in (1, 2, 3)]
            root, knee, ankle = joint(s, "RigBody", n[0]), joint(s, n[0], n[1]), joint(s, n[1], n[2])
            foot = tip(s, n[2], ankle)
            limb(b, [root, knee, ankle, foot], [0.08, 0.07, 0.05, 0.0], tuple(n), sides=4)


def wing_frame(s: Source, side: str):
    """The wing's frame at rest: its hinge (the first and second wing bones' joint), its span toward the fourth and
    fifth's, and the chord across its membrane (toward the leading edge on both sides)."""
    w = [f"Rig{side}wing{k}" for k in range(1, 6)]
    o = joint(s, w[0], w[1])
    span = unit(joint(s, w[3], w[4]) - o)
    p = led(s, *w[1:])
    c = p.mean(axis=0)
    normal = np.linalg.svd(p - c)[2][2]
    normal = unit(normal - (normal @ span) * span)
    normal = normal if normal[1] > 0 else -normal
    chord = unit(np.cross(normal, span))
    if chord[2] < 0:
        chord = -chord
    return o, span, chord


# The forewing in its frame (along the span, across toward the leading edge, metres) from the right wing's vendor
# outline: its veins split it into cells as an insect's do.
WING = {"H": (-0.45, 0.05), "A": (0.6, 0.42), "B": (1.3, 0.62), "B2": (2.2, 0.6), "T": (3.05, 0.42),
        "T2": (3.12, 0.12), "C": (2.7, -0.5), "D": (2.0, -0.74), "E": (0.75, -0.42), "M1": (1.3, -0.05),
        "M2": (2.25, 0.0)}
WING_CELLS = [("H", "A", "B", "M1", "E"), ("B", "B2", "M2", "M1"), ("B2", "T", "T2", "M2"), ("M2", "T2", "C"),
              ("M1", "M2", "C", "D", "E")]


# Each wing's normal: the rest-frame direction its bones turn toward the sky, averaged over every frame of every clip
# (the baked bone texture); the membrane's own normal is up in about 90% of frames, but leant to these the wings stay
# dark through the flap rather than lighting as solid blades in the rim.
WING_UP = {"R": (0.25, 0.95, 0.17), "L": (-0.23, 0.96, 0.11)}


def wings(b: Builder) -> None:
    """One big forewing a side, a thin membrane on the vendor's wing bones whose veins draw its cells, its root on the
    thorax's top."""
    s = b.source
    for side in "RL":
        o, span, chord = wing_frame(s, side)
        names = list(WING)
        pts = [o + span * WING[n][0] + chord * WING[n][1] for n in names]
        cells = [[names.index(n) for n in c] for c in WING_CELLS]
        bones = tuple(f"Rig{side}wing{k}" for k in range(1, 6))
        b.sheet(pts, cells, bones, 0.03, 0.03, WING_UP[side])


def build(source: Source) -> Mesh:
    b = Builder(source)
    roots = head(b)
    crown(b)
    antennae(b, roots)
    thorax(b)
    abdomen(b)
    legs(b)
    wings(b)
    triangles, edges = b.triangles()
    bones, weights = b.skin()
    return Mesh(np.array(b.points, np.float32), triangles, bones, weights, b.normals(triangles), np.array(b.carried),
                edges)
