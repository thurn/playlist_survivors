"""The Treasure Goblin (ps-8z4.7), Boss Rush boss 1: the vendor's chibi goblin rebuilt as a few simple faceted
shapes, every edge it designs drawn, around what names it: the loot sack on its back, a big head with long ears, a
long nose and a jutting jaw.

The trailer's drawing of the vendor mesh (1,762 triangles) is half right. The sack is already a low-poly polyhedron
whose creases close into facets, with a sword stuck through it, its tie and a strap over the shoulder: the best thing
on the sheet, and it names the type. The goblin is not: its head is a smooth ball whose features crease only in
places, so eyes, brows and mouth draw as floating dashes and no facet closes; the hands' fingers, the clawed feet and
the bracers knot into scribbles at game size. What changes:

- Kept as the vendor made them, drawn by their own 24-degree creases (flagged here): the sack, its tie, the sword
  through it and the strap (everything the bag bone leads), and the dagger in the right hand.
- The head: 0.88 of the vendor's, angular, six-sided rings from a jutting chin and a wide jaw to a narrow crown
  closing under a ridge from brow to nape (no apex, which reads as an umbrella from above), a corner to the front. A
  round head of eight-sided rings read as a second sack in every critique ("two sacks stacked up"). An almond eye on each forehead plane either
  side of that corner, their inner corners low (sly), raised as plates so they draw closed; a long nose drooping
  from the middle of the face. The idle clip bows the head, so the camera sees the forehead and crown.
- The ears: long broad leaves straight out to the sides, drooping at the tips, facing up, folded once, thick at the
  root and thin at the tip so they read as wedges, not cards. Rigid on the head; their tops' normals lean up.
- The body: one six-sided pot-bellied shape from the crotch to the neck, with a belt round it.
- The limbs: each arm one four-sided prism through the vendor's joints, thin to the elbow and flaring into a big
  bracer, and a broad mitten of a hand; each leg one short prism, and a pointed shoe curling up at the toe.
- Dropped: the fingers, claws, the rope's dangling end on the chest and the earring.

Every new shape draws its polygons' outlines, never their fan diagonals, and a plate only its top. Each is skinned by
transfer from the vendor's nearest vertices over its own bones (never across limbs or sides); the head, eyes, nose
and ears rigid on the head bone. The kept parts keep their vendor weights.

Source: Forest Creatures Pack's Treasure Goblin - Blue.prefab (sha256
299d32142eb35816e3cc20081da60041af39527419de13fa3a9750715ae22f90) and Treasure Goblin.FBX
(70dd16b9a17684f5706becf911a33761a42243128ae7bd4739dceec243240178), as manifest.json lists them. No Blender: numpy
only. Bake: `just champions-tool monster bake treasureGoblin`; review: `just champions-tool monster review
treasureGoblin` (and `--look trailer`).
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

    def plate(self, outline, normal, depth: float, skin, carried: bool = False, lean: float = 0.0) -> None:
        """A thin plate: the convex outline (n, 3) on top, the same `depth` behind along -normal; only the top
        outline draws, so it reads as one shape rather than a doubled rim. Its top's normals lean up by `lean`."""
        top = np.asarray(outline, np.float64)
        bottom = top - unit(normal) * depth
        low, high = self.loft([bottom, top], skin, carried)
        self.lean_up(high, lean)
        for i in range(len(top)):
            self.hush(low[i], low[(i + 1) % len(top)])
            self.hush(low[i], high[i])

    def lean_up(self, ids, lean: float) -> None:
        """These points' normals lean up by `lean` (see normals): only a plate's top, whose face looks up, so every
        face's normals stay on its own side when a shard turns it over."""
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


LEAN = 0.8  # the ears' tops: mostly up

KEPT_BONES = ("RigBag",)  # the vendor's sack, its tie, the sword through it and the strap: kept as they are
SIDES = {-1: "L", 1: "R"}  # the vendor's left limbs are at -x


HEAD_PIVOT = np.array([0.0, 1.95, 0.15])  # the neck's top, about which the head is shrunk
HEAD_SCALE = 0.88  # of the vendor's: smaller than the sack, so the two don't read as two sacks


def hd(points) -> np.ndarray:
    """Head points shrunk about the neck."""
    return HEAD_PIVOT + (np.asarray(points, np.float64) - HEAD_PIVOT) * HEAD_SCALE


CENTRE = hd((0.0, 2.45, 0.1))  # inside the head


def head(b: Builder) -> None:
    """A big angular head of six-sided rings, a corner to the front, from a jutting chin and a wide jaw to a narrow
    crown closing under a ridge from brow to nape (an apex's spokes read as an umbrella from above)."""
    n = 6

    def hex_ring(y, half_x, z_back, z_front):
        zc, rz = (z_front + z_back) / 2, (z_front - z_back) / 2
        return flat((0, y, zc), (half_x / np.sin(np.pi / 3), rz), n)  # half_x: the ring's half width

    rings = [hex_ring(1.93, 0.28, 0.3, 0.78), hex_ring(2.07, 0.46, -0.2, 0.82), hex_ring(2.33, 0.53, -0.45, 0.72),
             hex_ring(2.6, 0.55, -0.52, 0.7), hex_ring(2.83, 0.34, -0.38, 0.48)]
    pts = hd(np.vstack(rings + [np.array([(0, 2.98, 0.18), (0, 2.96, -0.18)])]))
    faces = []
    for r in range(len(rings) - 1):
        a, c = r * n, (r + 1) * n
        faces += [[a + k, a + (k + 1) % n, c + (k + 1) % n, c + k] for k in range(n)]
    faces.append(list(range(n)))
    top = (len(rings) - 1) * n
    f, k = len(rings) * n, len(rings) * n + 1  # the ridge's front and back
    # ring index 0 is straight ahead (+z), 1 and 2 to +x, 3 behind, 4 and 5 to -x
    faces += [[top + 5, top + 0, f], [top + 0, top + 1, f], [top + 1, top + 2, k, f], [top + 2, top + 3, k],
              [top + 3, top + 4, k], [top + 4, top + 5, f, k]]
    b.shape(pts, faces, "RigHead", inside=CENTRE)
    for side in (1, 5):  # an almond eye on each forehead plane either side of the front corner
        q0, q1, q2, q3 = pts[3 * n], pts[3 * n + side], pts[top + side], pts[top]
        normal = unit(np.cross(q1 - q0, q3 - q0))
        if normal @ (q0 - CENTRE) < 0:
            normal = -normal
        centre = 0.32 * q0 + 0.2 * q1 + 0.2 * q2 + 0.28 * q3 + normal * 0.03
        across = unit(q1 - q0)
        up = unit(np.cross(normal, across))
        up = up if up[1] > 0 else -up
        centre = centre + across * 0.05
        outline = [centre - across * 0.19 - up * 0.06, centre - up * 0.065, centre + across * 0.19 + up * 0.04,
                   centre + up * 0.095]  # the inner corner low: a sly scowl
        b.plate(outline, normal, 0.03, "RigHead")


def nose(b: Builder) -> None:
    """A long hooked nose from the middle of the face, drooping at its tip."""
    pts = hd([(0.0, 2.58, 0.7), (0.16, 2.36, 0.69), (-0.16, 2.36, 0.69), (0.0, 2.24, 1.08)])
    ids = b.shape(pts, [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], "RigHead")
    for i, j in ((0, 1), (1, 2), (2, 0)):
        b.hush(ids[i], ids[j])


def ears(b: Builder) -> None:
    """Long broad ears straight out to the sides, drooping at the tips: leaves facing up, folded once."""
    for s in (-1, 1):
        root_f, root_b = (s * 0.53, 2.62, 0.17), (s * 0.53, 2.52, -0.17)
        front, tip, back = (s * 0.98, 2.66, 0.22), (s * 1.28, 2.36, -0.04), (s * 0.9, 2.54, -0.24)
        crease = (s * 0.53, 2.6, 0.0)
        leaf = np.array([root_f, front, tip, back, root_b, crease], np.float64)
        under = leaf[:5] - np.array([(0, 0.13, 0), (0, 0.06, 0), (0, 0.02, 0), (0, 0.06, 0), (0, 0.13, 0)])
        pts = hd(np.vstack([leaf, under]))
        faces = [[5, 0, 1, 2], [5, 2, 3, 4], [10, 9, 8, 7, 6]]
        faces += [[i, i + 1, 7 + i, 6 + i] for i in range(4)] + [[4, 5, 0, 6, 10]]
        mid = pts.mean(axis=0)
        inside = [pts[f].mean(axis=0) - (0, 1, 0) for f in faces[:2]] + [pts[faces[2]].mean(axis=0) + (0, 1, 0)]
        inside += [mid] * (len(faces) - 3)
        ids = b.shape(pts, faces, "RigHead", inside=np.array(inside))
        b.lean_up(ids[:6], LEAN)
        for i in range(5):
            b.hush(ids[6 + i], ids[6 + (i + 1) % 5])
            b.hush(ids[i], ids[6 + i])
        b.hush(ids[5], ids[0])
        b.hush(ids[5], ids[4])


def torso(b: Builder) -> None:
    """A pot-bellied body from the crotch to the neck, six-sided, the belly pushed forward, and a belt round it."""
    crotch = flat((0, 0.8, 0.0), (0.22, 0.18), 6, np.pi / 6)
    hips = flat((0, 0.98, 0.0), (0.42, 0.29), 6, np.pi / 6)
    belly = flat((0, 1.36, 0.05), (0.46, 0.34), 6, np.pi / 6)
    chest = flat((0, 1.72, 0.0), (0.42, 0.28), 6, np.pi / 6)
    neck = flat((0, 1.97, 0.03), (0.24, 0.2), 6, np.pi / 6)
    b.loft([crotch, hips, belly, chest, neck], ("RigPelvis", "RigSpine1", "RigSpine2", "RigRibcage"))
    top = flat((0, 1.2, 0.03), (0.47, 0.335), 6, np.pi / 6)
    bottom = flat((0, 1.08, 0.015), (0.465, 0.325), 6, np.pi / 6)
    b.loft([top, bottom], ("RigPelvis", "RigSpine1"))


def limb(b: Builder, joints, radii, bones, sides: int = 4) -> list[list[int]]:
    """A prism through `joints`, a corner forward, its radius at each joint, each ring square to the limb there."""
    joints = [np.asarray(j, np.float64) for j in joints]
    segments = [unit(c - a) for a, c in pairwise(joints)]
    rings = []
    for i, (j, r) in enumerate(zip(joints, radii)):
        d = unit(segments[max(i - 1, 0)] + segments[min(i, len(segments) - 1)])
        u = unit(Z - (Z @ d) * d)
        rr = r if np.ndim(r) else (r, r)
        rings.append(ring(j, u, np.cross(d, u), rr, sides))
    return b.loft(rings, tuple(bones))


def arms(b: Builder) -> None:
    """Each arm one prism, thin to the elbow and flaring into a big bracer, and a broad mitten of a hand."""
    for s, side in SIDES.items():
        bones = [f"Rig{side}ArmUpperarm", f"Rig{side}ArmForearm", f"Rig{side}ArmPalm"]
        shoulder, elbow, wrist = (np.array(p) for p in ((s * 0.36, 1.75, 0.0), (s * 0.68, 1.73, 0.0),
                                                         (s * 1.06, 1.745, 0.0)))
        limb(b, [shoulder, elbow, elbow + (wrist - elbow) * 0.2, wrist], [0.1, 0.085, 0.13, 0.18], bones)
        tip = np.array((s * 1.6, 1.735, 0.0))
        limb(b, [wrist - (s * 0.02, 0, 0), wrist + (s * 0.12, 0, 0), tip], [(0.11, 0.07), (0.15, 0.08), (0.12, 0.05)],
             (f"Rig{side}ArmPalm", f"Rig{side}ArmMiddle1", f"Rig{side}ArmMiddle2"))


def legs(b: Builder) -> None:
    """Each leg one short prism from the hip, and a big wedge of a foot."""
    for s, side in SIDES.items():
        thigh, calf, foot, toes = (f"Rig{side}Leg{n}" for n in ("Thigh", "Calf", "Foot", "Toes1"))
        hip, knee, ankle = (np.array(p) for p in ((s * 0.2, 0.92, 0.0), (s * 0.246, 0.54, 0.0), (s * 0.244, 0.16, 0.0)))
        limb(b, [hip, knee, ankle], [0.17, 0.13, 0.12], (thigh, calf, "RigPelvis"))
        x = s * 0.244  # a pointed shoe: a heel box narrowing to a toe that curls up
        heel = [(x - 0.13, 0.0, -0.16), (x + 0.13, 0.0, -0.16), (x + 0.12, 0.22, -0.12), (x - 0.12, 0.22, -0.12)]
        ball = [(x - 0.13, 0.0, 0.2), (x + 0.13, 0.0, 0.2), (x + 0.09, 0.15, 0.17), (x - 0.09, 0.15, 0.17)]
        b.loft([heel, ball, (x, 0.08, 0.58)], (foot, toes))


def creases(positions: np.ndarray, triangles: np.ndarray, angle_deg: float = 24.0) -> np.ndarray:
    """Each triangle's edges that draw as StyleMesh.FeatureEdges flags them (welded at 0.1 mm; a crease past
    `angle_deg`, or an edge open or shared by more than two)."""
    _, ids = np.unique(np.round(positions.astype(np.float64) * 1e4).astype(np.int64), axis=0, return_inverse=True)
    corners = ids.reshape(-1)[triangles]
    p = positions.astype(np.float64)[triangles]
    normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
    length = np.linalg.norm(normal, axis=1)
    live = length > 1e-12
    normal = normal / np.where(live, length, 1)[:, None]
    sides: dict[tuple[int, int], list[int]] = {}
    for t in np.nonzero(live)[0]:
        for k in range(3):
            a, c = corners[t, (k + 1) % 3], corners[t, (k + 2) % 3]
            if a != c:
                sides.setdefault((min(a, c), max(a, c)), []).append(3 * t + k)
    flags = np.zeros(triangles.shape, np.uint8)
    cos = np.cos(np.radians(angle_deg))
    for faces in sides.values():
        if len(faces) != 2 or normal[faces[0] // 3] @ normal[faces[1] // 3] < cos:
            for f in faces:
                flags[f // 3, f % 3] = 1
    return flags


def kept(source: Source) -> np.ndarray:
    """The vendor triangles kept as they are: the sack and what it carries (led by the bag bone), and the dagger."""
    lead = source.bones[np.arange(len(source.bones)), source.weights.argmax(axis=1)]
    keep = np.isin(lead, [source.bone(n) for n in KEPT_BONES]) | source.carried
    return source.triangles[keep[source.triangles].all(axis=1)]


def build(source: Source) -> Mesh:
    b = Builder(source)
    head(b)
    nose(b)
    ears(b)
    torso(b)
    arms(b)
    legs(b)
    triangles, edges = b.triangles()
    bones, weights = b.skin()
    normals = b.normals(triangles)

    tris = kept(source)
    used = np.unique(tris)
    remap = np.full(len(source.positions), -1)
    remap[used] = np.arange(len(used))
    vt = remap[tris]
    vp = source.positions[used]
    n = len(used)
    return Mesh(np.vstack([vp, np.array(b.points)]).astype(np.float32),
                np.vstack([vt, triangles + n]).astype(np.int32),
                np.vstack([source.bones[used], bones]).astype(np.int32),
                np.vstack([source.weights[used], weights]).astype(np.float32),
                np.vstack([source.normals[used], normals]).astype(np.float32),
                np.concatenate([source.carried[used], np.array(b.carried)]),
                np.vstack([creases(vp, vt), edges]).astype(np.uint8))
