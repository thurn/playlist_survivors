"""The Rootwarden (ps-8z4.13), Boss Rush boss 4: the vendor's treant rebuilt as a few simple faceted shapes, every
edge it designs drawn, around what names it: a stump of a trunk with a scowling face and a jagged maw, three thick
boughs from its broken top, and four root legs whose feet reach out in roots diving into the floor.

The trailer's drawing of the vendor mesh (2,961 triangles) is a round mass of small bark plates, knots, leaves and
twigs: a closed silhouette, but inside it every facet is a sliver, so at game size it is a scribble with no dark
inside, and the face (the treant's best feature: slanted eyes and a jack-o'-lantern grin) is lost in the noise, as is
the crown of broken branches over it. Only the four ribbed root legs read. What changes:

- The trunk: one ten-sided stump, fluted as bark (every other corner sunk) round a broad flat front, flaring to the
  roots and tapering to a broken top that leans back, so the face turns up toward the game camera. It splits at the
  mouth: the lower body on the pelvis and jaw, the head above it rigid on the head bone, a wedge of a maw open at the
  front between them. Its normals lean up 0.4, so its big facets turned away stay dark.
- The face: a jagged grin of four fangs hanging from the head and three rising from the jaw, interleaved; two angry
  slanted eyes, each a slot bulging to a ridge so its facets catch the line; a heavy brow of two wedges and a knot of
  a nose.
- The top: three splinters standing from the stump's broken rim and three thick boughs, two of them forking, all
  four-sided prisms tapering to points. A crown of many twigs drew as hairline scratches at game size (both blind
  critiques).
- The arms: short gnarled branches through the vendor's joints, each ending in one thick claw.
- The legs: four prisms from inside the trunk through the vendor's joints to the ground, each foot reaching out in
  one thick root that dives into the floor. Two or three thin roots a foot cluttered the outline as claws (the second
  blind critique). Dropped: the leaves round the waist and the bark's knots and plates.

Every shape draws its polygons' outlines, never their fan diagonals. The trunk, arms and legs are skinned by transfer
from the vendor's nearest vertices over their own bones (never across limbs or sides); the head and its face, crown
and boughs rigid on the head bone, the lower fangs on the jaw, each claw and ground root on its hand or ankle.
Joints are read from the vendor's vertices weighted to both bones.

Source: Polygonal Creatures Pack's Polygonal Treant Green.prefab (sha256
f28ecb6df55454211bfeb32eef0885d999e8c0da8159c739390e065d4bef31f6) and Polygonal Treant.FBX
(94dab0d328fe3f4c4371b3d5aa5f04562762930b5fcf16b38a35f90298cbd4dc): the prefab and its clips are in manifest.json,
the mesh in the prefab's dependency closure. No Blender: numpy only. Bake: `just champions-tool monster bake
rootwarden`; review: `just champions-tool monster review rootwarden` (and `--look trailer`).
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


LEAN = 0.4  # the trunk's normals lean up this much, so its big facets turned away stay dark

# The trunk's section: ten corners round it, (x, z) in metres before each ring's scale, from the front's right corner
# round the back to its left; a broad flat front for the face, the rest fluted (every other corner sunk) as bark.
ANGLES = np.radians([40, 72, 104, 136, 166])
FLUTE = np.array([1.0, 0.92, 1.0, 0.92, 1.0])
_right = np.column_stack([np.sin(ANGLES) * FLUTE, np.cos(ANGLES) * FLUTE * 0.95])
SECTION = np.vstack([_right, (_right * (-1, 1))[::-1]])
TOP_Y = 3.75  # the stump's broken top
TOP_SCALE, TOP_BACK = 0.72, 0.25  # the top ring's scale and how far it sits back: the face leans back to the camera
MOUTH_Y = 2.0  # where head and jaw meet at the back
MOUTH_OPEN = (0.28, 0.28)  # how far the head's front lifts and the jaw's front drops: the maw's gape


def section(y, scale: float, back: float = 0.0, front_dy: float = 0.0) -> np.ndarray:
    """A trunk ring at height y, its front corners raised by front_dy and the rest blended toward the back."""
    pts = np.column_stack([SECTION[:, 0] * scale, np.full(len(SECTION), y, float), SECTION[:, 1] * scale - back])
    pts[:, 1] += front_dy * np.clip((SECTION[:, 1] + 0.45) / 1.4, 0, 1)
    return pts


def front(y: float) -> tuple[np.ndarray, np.ndarray]:
    """A point on the head's front face at height y (x = 0), and that face's outward normal."""
    lo = section(MOUTH_Y, 0.95, 0, MOUTH_OPEN[0])[0] * (0, 1, 1)
    hi = section(TOP_Y, TOP_SCALE, TOP_BACK)[0] * (0, 1, 1)
    t = (y - lo[1]) / (hi[1] - lo[1])
    p = lo + (hi - lo) * t
    up = unit(hi - lo)
    return np.array([0.0, p[1], p[2]]), unit(np.cross(X, up))


def trunk(b: Builder) -> None:
    """The stump: a lower body from the roots to the mouth on the pelvis and jaw, and the head above it, one tapering
    octagon leaning back; the maw a wedge open at the front between them."""
    lower = [section(0.45, 1.25), section(MOUTH_Y, 0.95, 0, -MOUTH_OPEN[1])]
    ids = b.loft(lower, ("RigPelvis", "RigJaw"))
    b.lean_up([i for r in ids for i in r], LEAN)
    upper = [section(MOUTH_Y, 0.95, 0, MOUTH_OPEN[0]), section(TOP_Y, TOP_SCALE, TOP_BACK)]
    ids = b.loft(upper, "RigHead")
    b.lean_up([i for r in ids for i in r], LEAN)


def teeth(b: Builder) -> None:
    """A jagged grin: fangs hanging from the head's front rim and rising from the jaw's, interleaved."""
    top = section(MOUTH_Y, 0.95, 0, MOUTH_OPEN[0])[0]
    low = section(MOUTH_Y, 0.95, 0, -MOUTH_OPEN[1])[0]
    for x in (-0.42, -0.14, 0.14, 0.42):
        y, z = top[1], top[2]
        tri = [(x - 0.13, y + 0.01, z - 0.02), (x + 0.13, y + 0.01, z - 0.02), (x, y + 0.01, z - 0.2),
               (x, y - 0.36, z - 0.06)]
        b.shape(tri, [[0, 1, 3], [1, 2, 3], [2, 0, 3], [0, 2, 1]], "RigHead")
    for x in (-0.28, 0.0, 0.28):
        y, z = low[1], low[2]
        tri = [(x - 0.12, y - 0.01, z - 0.02), (x + 0.12, y - 0.01, z - 0.02), (x, y - 0.01, z - 0.2),
               (x, y + 0.32, z - 0.06)]
        b.shape(tri, [[0, 1, 3], [1, 2, 3], [2, 0, 3], [0, 2, 1]], "RigJaw")


def face(b: Builder) -> None:
    """Angry slanted eyes, each a slot bulging to a ridge from the face, under a heavy brow of two wedges meeting over
    a knot of a nose."""
    for side in (1, -1):
        # the eye: a slanted slot, inner corner low, bulging to a ridge so its facets catch the line
        outline = [(0.08, 2.86), (0.54, 3.06), (0.5, 3.3), (0.1, 3.1)]
        pts = []
        for x, y in outline:
            p, n = front(y)
            pts.append(p + X * x * side + n * 0.02)
        _, n = front(3.08)
        ridge = [pts[0] * 0.75 + pts[2] * 0.25 + n * 0.12, pts[0] * 0.25 + pts[2] * 0.75 + n * 0.12]
        b.shape(pts + ridge, [[0, 1, 5, 4], [1, 2, 5], [2, 3, 4, 5], [3, 0, 4], [3, 2, 1, 0]], "RigHead",
                inside=pts[0] * 0.5 + pts[2] * 0.5 - n * 0.1)
        # the brow: a wedge from the nose's root up and out over the eye
        brow = []
        for x, y in ((0.04, 3.2), (0.56, 3.46)):
            p, n = front(y)
            brow.append(p + X * x * side)
        inner, outer = brow
        _, n = front(3.3)
        ridge_in, ridge_out = inner + n * 0.2 + Y * 0.02, outer + n * 0.14 + Y * 0.04
        low_in, low_out = inner - Y * 0.1, outer - Y * 0.08
        b.shape([inner, outer, ridge_out, ridge_in, low_in, low_out],
                [[0, 1, 2, 3], [3, 2, 5, 4], [0, 4, 5, 1], [0, 3, 4], [1, 5, 2]], "RigHead")
    # the nose: a knot between and below the eyes
    p, n = front(2.88)
    knot = [p + X * 0.1 + Y * 0.12, p - X * 0.1 + Y * 0.12, p - Y * 0.1, p + n * 0.18]
    b.shape(knot, [[0, 1, 3], [1, 2, 3], [2, 0, 3], [0, 2, 1]], "RigHead")


# The broken top's splinters: for each of the top ring's edges, how high its splinter stands (0: none).
SPLINTERS = (0.55, 0.0, 0.0, 0.7, 0.0, 0.0, 0.0, 0.6, 0.0, 0.0)


def crown(b: Builder) -> None:
    """The stump's broken top: a splinter standing up and out from most of its top edges, ragged in height."""
    top = section(TOP_Y, TOP_SCALE, TOP_BACK)
    c = top.mean(axis=0)
    for k, h in enumerate(SPLINTERS):
        if not h:
            continue
        a, e = top[k], top[(k + 1) % len(top)]
        mid = (a + e) / 2
        apex = c + (mid - c) * 1.1 + Y * h
        back = c + (mid - c) * 0.45
        b.shape([a, e, apex, back], [[0, 1, 2], [1, 3, 2], [3, 0, 2], [0, 3, 1]], "RigHead")


# Three boughs grow from the top, two of them forking once (points in the rest frame, metres).
BOUGHS = [
    [(-0.3, 3.55, -0.4), (-0.8, 4.2, -0.55), (-1.5, 4.6, -0.35)],
    [(0.35, 3.55, -0.3), (0.85, 4.1, -0.4), (1.55, 4.4, -0.1)],
    [(0.0, 3.55, -0.65), (0.05, 4.3, -1.0), (0.0, 4.8, -1.4)],
]
FORKS = [[(-0.8, 4.2, -0.55), (-0.85, 4.85, -0.9)], [(0.85, 4.1, -0.4), (1.0, 4.7, -0.8)]]


def branches(b: Builder) -> None:
    """Three thick boughs from the stump's top, two forking, four-sided prisms tapering to points: the warden is a
    tree. Fewer and fatter than a crown of twigs, which drew as hairline scratches at game size (the first blind
    critique)."""
    for bough in BOUGHS:
        limb(b, bough, [0.28, 0.17, 0.0], "RigHead", sides=4)
    for fork in FORKS:
        limb(b, fork, [0.13, 0.0], "RigHead", sides=4)


def arms(b: Builder) -> None:
    """Short gnarled branch arms from the trunk's sides, each ending in one thick claw."""
    s = b.source
    for side, sign in (("R", 1), ("L", -1)):
        a1, a2 = f"Rig{side}Arm1", f"Rig{side}Arm2"
        shoulder, elbow = joint(s, "RigPelvis", a1), joint(s, a1, a2)
        hand = tip(s, a2, elbow)
        root = shoulder * (0.7, 1, 1)
        wrist = elbow + (hand - elbow) * 1.1 - Y * 0.25
        limb(b, [root, elbow, wrist], [0.24, 0.18, 0.14], ("RigPelvis", a1, a2), sides=4)
        d = unit(wrist - elbow)
        f = unit(d + Y * 0.1)
        limb(b, [wrist, wrist + f * 0.7], [0.15, 0.0], a2, sides=4)


LEGS = (("Front1", "Front2", "FrontAnkle"), ("Back1", "Back2", "BackAnkle"))


def legs(b: Builder) -> None:
    """Four root legs, each one four-sided prism from inside the trunk through the vendor's joints, its foot splitting
    into a thick root along the ground."""
    s = b.source
    for side in "RL":
        for n1, n2, n3 in LEGS:
            c1, c2, c3 = (f"Rig{side}Leg{n}" for n in (n1, n2, n3))
            hip, knee, ankle = joint(s, "RigPelvis", c1), joint(s, c1, c2), joint(s, c2, c3)
            foot = tip(s, c3, ankle)
            foot = np.array([ankle[0] * 0.5 + foot[0] * 0.5, 0.0, ankle[2] * 0.5 + foot[2] * 0.5])
            root = hip * (0.6, 1, 0.6) + Y * 0.2
            limb(b, [root, knee, ankle, foot], [0.4, 0.3, 0.24, 0.3], ("RigPelvis", c1, c2, c3), sides=4)
            out = unit(foot * (1, 0, 1))
            f = unit(out + np.cross(Y, out) * 0.2)
            start = foot + Y * 0.12
            limb(b, [start, start + f * 0.7 - Y * 0.08, start + f * 1.4 - Y * 0.2], [0.22, 0.14, 0.0], c3, sides=4)


def build(source: Source) -> Mesh:
    b = Builder(source)
    trunk(b)
    teeth(b)
    face(b)
    crown(b)
    branches(b)
    arms(b)
    legs(b)
    triangles, edges = b.triangles()
    bones, weights = b.skin()
    return Mesh(np.array(b.points, np.float32), triangles, bones, weights, b.normals(triangles), np.array(b.carried),
                edges)
