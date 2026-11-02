"""The goblin (ps-6y2.35): the vendor's armoured goblin rebuilt as a few simple faceted shapes, every edge it designs
drawn, around what names a goblin: big ears, a long nose, a hunch.

The trailer's drawing of the vendor mesh is a filled magenta knot at game size: 12,457 triangles of plate, straps,
the belt's rows, the skirt's flaps, spiked boots and gloves, a caged helmet and the teeth light so many creases that
the lines merge into a fill (the operator's dense-swarm.jpg), and its 9 cm ears hide behind the pauldrons, so
nothing names a goblin. What changes:

- The head: 1.45 times the vendor's, sunk between the shoulders and thrust forward, the hunch. A jutting jaw, a ring
  at the brow and a skull closing under a ridge from forehead to nape (an apex's spokes read as an umbrella from the
  camera); a long nose; two slanted eye slits, a scowl, on the forehead's planes, which face up toward the camera.
- The ears: broad leaves reaching twice as far out as the vendor's (0.31 m from the middle), drooping at the tips,
  facing up so the camera sees each one whole from every side, folded along one crease and drawn by their outline.
  Rigid on the head: the clips lift the vendor's ear bones, which turned long ears into a rabbit's.
- The body: one six-sided shape with a flat front from the crotch to a humped back, the chest pushed forward, with a
  belt round it and a loincloth hanging front and back from under the belt.
- The limbs: each arm and each leg one four-sided prism through the vendor's joints, so the outline closes at the
  elbow and knee: thin arms with bracers flaring to the wrist and mitten hands; legs with a boot flaring at the top,
  a diamond guard on the knee and wedge feet.
- The sword: a broad blade of diamond section with a crossguard, a grip and a pommel, on the vendor sword's axis and
  length (its thin blade drew a hairline). The shield: a thin plank board with an arched top, 85% of the vendor's,
  on its plane but held out from the hand and leant 25 degrees further out at the top, so its face shows from above
  rather than its edge.
- The big plates' tops (ears, shield, loincloth) have vertex normals leaning up, so a plate turned across the view
  stays dark rather than lighting whole in the fresnel rim; only their tops, so a shard turned over never shows a
  face whose normals point the other way (it drew solid).
- Dropped: the pauldrons and collar (boxes on the shoulders that made it a little mech), the caged helmet, the teeth,
  eyeballs and earrings, the skirt's flaps, straps, spikes and the shield's boss (louder than the creature).

Each shape draws its polygons' outlines, never their fan diagonals, and a plate only its top. 436 triangles at every
LOD, where the vendor's are 12,457, 8,878 and 4,480. Each shape is skinned by transfer from the vendor's nearest
vertices over its own bones (never across limbs or sides); the head and ears rigid on the head bone, the sword and
the shield on the weapon bones that carry the vendor's (R_w, L_w), both carried, so the shatter flings them.

Source: Magic Pig Games' Goblins LP.prefab (sha256 2a36906f05a8a301a13d8595bec436a6bb6f5abbfbbbd4eb3d3afe2d52c84175)
and Goblins LP.fbx (a46dcba399602bc28e681dc81b003d1319c58014cb3c594f4cef1dddbf92b6b5), as manifest.json lists them.
No Blender: numpy only. Bake: `just champions-tool monster bake goblin`; review: `just champions-tool monster review
goblin` (and `--look trailer`).
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


LEAN = 0.8  # the big plates' vertex normals (ears, shield, loincloth): mostly up

HEAD_PIVOT = np.array([0.0, 1.02, 0.0])  # the neck's top, about which the head is enlarged
HEAD_SCALE = 1.45
HEAD_SHIFT = np.array([0.0, -0.035, 0.035])  # sunk between the shoulders and thrust forward: the hunch


def big(points) -> np.ndarray:
    """Head points enlarged about the neck, then sunk and thrust forward."""
    return HEAD_PIVOT + (np.asarray(points, np.float64) - HEAD_PIVOT) * HEAD_SCALE + HEAD_SHIFT


def head(b: Builder) -> None:
    """A big bald head of a few planes: a jutting jaw, a ring at the brow, and a skull closing under a ridge from the
    forehead to the nape (no apex); a long nose; two slanted eye slits on the forehead's two planes either side of
    its front corner, which face up toward the camera."""
    jaw = np.array([(0.0, 1.035, 0.115), (0.062, 1.04, 0.06), (0.06, 1.05, -0.05), (0.0, 1.05, -0.07),
                    (-0.06, 1.05, -0.05), (-0.062, 1.04, 0.06)])
    brow = np.array([(0.0, 1.11, 0.1), (0.074, 1.11, 0.045), (0.07, 1.115, -0.055), (0.0, 1.115, -0.085),
                     (-0.07, 1.115, -0.055), (-0.074, 1.11, 0.045)])
    pts = big(np.vstack([jaw, brow, [(0, 1.19, 0.0), (0, 1.185, -0.05)]]))
    c, f, k = 6, 12, 13  # the brow ring; the ridge's front and back
    faces = [[i, (i + 1) % 6, c + (i + 1) % 6, c + i] for i in range(6)] + [list(range(6))]
    faces += [[c + 5, c, f], [c, c + 1, f], [c + 1, c + 2, k, f], [c + 2, c + 3, k], [c + 3, c + 4, k],
              [c + 4, c + 5, f, k]]
    b.shape(pts, faces, "head", inside=big((0, 1.09, 0.0)))
    for side in (1, 5):  # the forehead's planes right and left of the front corner
        p0, p1, top = pts[c], pts[c + side], pts[f]
        normal = unit(np.cross(p1 - p0, top - p0))
        if normal @ (p0 - big((0, 1.09, 0.0))) < 0:
            normal = -normal
        centre = 0.42 * p0 + 0.38 * p1 + 0.2 * top + normal * 0.006
        across = unit(p1 - p0)
        up = unit(np.cross(normal, across))
        up = up if up[1] > 0 else -up
        outline = [centre - across * 0.03 - up * 0.012, centre + across * 0.052 + up * 0.006,
                   centre + across * 0.03 + up * 0.026]  # the inner corner low: a scowl
        b.plate(outline, normal, 0.006, "head")
    nose = big([(0.0, 1.115, 0.096), (0.028, 1.08, 0.1), (-0.028, 1.08, 0.1), (0.0, 1.05, 0.2)])
    ids = b.shape(nose, [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], "head")
    for i, j in ((0, 1), (1, 2), (2, 0)):  # its base lies on the face
        b.hush(ids[i], ids[j])


def ears(b: Builder) -> None:
    """Long broad ears out to the sides, drooping at the tips: leaves facing up, so the camera sees each one whole,
    folded along one crease from root to tip."""
    for s in (-1, 1):
        root_f, root_b = big((s * 0.066, 1.14, 0.03)), big((s * 0.066, 1.085, -0.045))
        front, tip, back = (s * 0.19, 1.15, 0.03), (s * 0.31, 1.07, -0.02), (s * 0.17, 1.075, -0.09)
        crease = (root_f + root_b) / 2 + (0, 0.014, 0)
        leaf = np.array([root_f, front, tip, back, root_b, crease])
        under = leaf[:5] - (0, 0.012, 0)
        pts = np.vstack([leaf, under])
        faces = [[5, 0, 1, 2], [5, 2, 3, 4], [10, 9, 8, 7, 6]]
        faces += [[i, i + 1, 7 + i, 6 + i] for i in range(4)] + [[4, 5, 0, 6, 10]]
        ids = b.shape(pts, faces, "head", inside=(s * 0.18, 1.08, -0.02))
        b.lean_up(ids[:6], LEAN)
        for i in range(5):  # the underside and the rim's short sides stay dark: one leaf, one crease
            b.hush(ids[6 + i], ids[6 + (i + 1) % 5])
            b.hush(ids[i], ids[6 + i])
        b.hush(ids[5], ids[0])
        b.hush(ids[5], ids[4])


def torso(b: Builder) -> None:
    """The body in one shape from the crotch to the shoulders, six-sided with a flat front: wide hips, a waist, the
    chest pushed forward, and the top tilted up behind into a hump."""
    crotch = flat((0, 0.53, 0.005), (0.08, 0.07), 6, np.pi / 6)
    hips = flat((0, 0.635, 0.0), (0.122, 0.115), 6, np.pi / 6)
    waist = flat((0, 0.70, 0.0), (0.108, 0.103), 6, np.pi / 6)
    chest = flat((0, 0.84, 0.025), (0.145, 0.13), 6, np.pi / 6)
    top = flat((0, 0.965, -0.02), (0.13, 0.1), 6, np.pi / 6)
    top[:, 1] -= 0.06 * (top[:, 2] + 0.02) / 0.1
    b.loft([crotch, hips, waist, chest, top], ("pelvis", "sp1", "sp2", "sp3"))


def belt_skirt(b: Builder) -> None:
    """A belt round the body, and a loincloth hanging from under it front and back."""
    top = flat((0, 0.715, 0.0), (0.124, 0.12), 6, np.pi / 6)
    bottom = flat((0, 0.64, 0.0), (0.136, 0.13), 6, np.pi / 6)
    b.loft([top, bottom], ("pelvis", "sp1"))
    skirt = tuple(n for n in b.source.bone_names if n.startswith("Skirt_jnt")) + ("pelvis",)
    for s in (-1, 1):  # front and back
        z0, z1 = s * 0.112, s * 0.15
        board = [(-0.075, 0.69, z0), (0.075, 0.69, z0), (0.055, 0.42, z1), (-0.055, 0.42, z1)]
        b.plate(board, (0, 0, s), 0.015, skirt, lean=LEAN)


def arms(b: Builder) -> None:
    """Each arm one prism, tapering to the elbow and flaring in a bracer to the wrist, and a big mitten of a hand."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = [f"{n}_{side}_jnt" for n in ("shoulder", "upperArm", "elbow", "hand")]
        shoulder, elbow, wrist = (np.array(p) for p in ((s * 0.12, 0.93, -0.035), (s * 0.214, 0.73, -0.031),
                                                         (s * 0.332, 0.62, 0.036)))
        cuff = wrist + (wrist - elbow) * 0.05
        limb(b, [shoulder, elbow, elbow + (wrist - elbow) * 0.25, cuff], [0.05, 0.036, 0.05, 0.062], bones)
        hand = wrist + unit(wrist - elbow) * 0.115
        limb(b, [cuff, hand], [0.05, 0.03], bones[3:])


def legs(b: Builder) -> None:
    """Each leg one prism from the hip, a boot flaring at its top, a diamond guard on the knee, and a big wedge of a
    foot."""
    for s in (-1, 1):
        side = "l" if s < 0 else "r"
        thigh, shin, foot, ball = f"thig_{side}", f"knee_{side}", f"foot_{side}", f"ball_{side}"
        hip, knee, ankle = (np.array(p) for p in ((s * 0.065, 0.62, -0.02), (s * 0.08, 0.316, -0.032),
                                                   (s * 0.094, 0.06, -0.05)))
        boot_top = knee + (ankle - knee) * 0.4
        cuff = boot_top + (knee - ankle) * 0.02
        limb(b, [hip, knee, cuff, boot_top, ankle], [0.066, 0.046, 0.044, 0.06, 0.046], (thigh, shin, foot, "pelvis"))
        # the foot: a wedge from the heel to the toe
        pts = [(s * 0.094 + dx, y, z) for dx in (-0.048, 0.048) for y, z in ((0.0, -0.08), (0.0, 0.15), (0.085, -0.06),
                                                                            (0.035, 0.11))]
        faces = [[0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]]
        b.shape(pts, faces, (foot, ball))
        # the knee's guard: a diamond plate in front of the knee
        c = knee + (0, 0.0, 0.045)
        plate = [c + (0, 0.06, 0), c + (s * 0.05, 0, 0.0), c + (0, -0.055, 0.0), c + (-s * 0.05, 0, 0),
                 c + (0, 0, 0.022)]
        b.shape(plate, [[0, 1, 4], [1, 2, 4], [2, 3, 4], [3, 0, 4], [0, 1, 2, 3]], (shin, thigh))


def limb(b: Builder, joints, radii, bones) -> None:
    """A four-sided prism through `joints`, a corner forward, its radius at each joint, each ring square to the limb
    there (the mean of the segments either side); skinned over `bones`."""
    joints = [np.asarray(j, np.float64) for j in joints]
    segments = [unit(c - a) for a, c in pairwise(joints)]
    rings = []
    for i, (j, r) in enumerate(zip(joints, radii)):
        d = unit(segments[max(i - 1, 0)] + segments[min(i, len(segments) - 1)])
        u = unit(Z - (Z @ d) * d)
        rings.append(ring(j, u, np.cross(d, u), (r, r), 4))
    b.loft(rings, tuple(bones))


def sword(b: Builder, source: Source) -> None:
    """A broad blade, diamond in section, a crossguard, a grip and a pommel, on the vendor sword's axis and length,
    where the vendor's thin blade drew a hairline."""
    p = source.part("Sword1")
    q = source.positions[p.first:p.first + p.count].astype(np.float64)
    mid = q.mean(axis=0)
    _, _, axes = np.linalg.svd(q - mid)
    along = (q - mid) @ axes[0]
    if along.max() > -along.min():
        axes[0], along = -axes[0], -along
    tip_u = along.min()
    u, w, t = axes[0], unit(axes[1]), unit(np.cross(axes[0], axes[1]))
    bone = "R_w"

    def at(s, dw=0.0, dt=0.0):
        return mid + u * s + w * dw + t * dt

    def diamond(s, width, thick):
        return np.array([at(s, width / 2), at(s, 0, thick / 2), at(s, -width / 2), at(s, 0, -thick / 2)])

    guard, pommel = 0.0, along.max()  # the vendor's crossguard, and the grip's end
    b.loft([at(tip_u), diamond(tip_u + 0.12, 0.1, 0.035), diamond(guard - 0.005, 0.08, 0.035)], bone, carried=True)
    box = [at(guard + dx, dw, dt) for dx in (-0.005, 0.025) for dw, dt in ((0.085, 0.02), (0.085, -0.02),
                                                                          (-0.085, -0.02), (-0.085, 0.02))]
    b.shape(box, [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]], bone,
            carried=True)
    b.loft([diamond(guard + 0.025, 0.024, 0.024), diamond(pommel - 0.035, 0.022, 0.022),
            diamond(pommel - 0.02, 0.04, 0.04), at(pommel)], bone, carried=True)


TILT = 25  # degrees the shield leans out from the vendor's
SHIELD_SIZE = 0.85  # of the vendor's: the board's empty face outdrew the creature in a crowd


def shield(b: Builder, source: Source) -> None:
    """A plank shield: a flat board with an arched top on the vendor shield's plane, leant out at the top so its face
    shows from above rather than its edge."""
    p = source.part("Shield1")
    q = source.positions[p.first:p.first + p.count].astype(np.float64)
    mid = q.mean(axis=0)
    _, _, axes = np.linalg.svd(q - mid)
    up = axes[0] if axes[0][1] > 0 else -axes[0]
    normal = axes[2] if axes[2][0] < 0 else -axes[2]  # facing out from the body, to the left
    across = unit(np.cross(normal, up))
    h, a = (q - mid) @ up, (q - mid) @ across
    mid = mid + up * (h.max() + h.min()) / 2 + across * (a.max() + a.min()) / 2
    hh, ha = (h.max() - h.min()) / 2, (a.max() - a.min()) / 2
    tilt = np.radians(TILT)  # its top out from the body, so the face turns up toward the camera
    normal, up = normal * np.cos(tilt) + up * np.sin(tilt), up * np.cos(tilt) - normal * np.sin(tilt)
    hh, ha = hh * SHIELD_SIZE, ha * SHIELD_SIZE
    outline = [(-ha, -hh), (ha, -hh), (ha, hh * 0.55), (ha * 0.55, hh), (-ha * 0.55, hh), (-ha, hh * 0.55)]
    mid = mid + normal * 0.035  # out from the hand that holds it, so the hand stays behind the board
    depth = 0.0125  # each side of its middle: a thin board, whose edge never glows as a band
    front = [mid + across * x + up * y + normal * depth for x, y in outline]
    back = [mid + across * x + up * y - normal * depth for x, y in outline]
    b.lean_up(b.loft([np.array(back), np.array(front)], "L_w", carried=True)[1], LEAN)


def build(source: Source) -> Mesh:
    b = Builder(source)
    head(b)
    ears(b)
    torso(b)
    belt_skirt(b)
    arms(b)
    legs(b)
    sword(b, source)
    shield(b, source)
    triangles, edges = b.triangles()
    bones, weights = b.skin()
    return Mesh(np.array(b.points, np.float32), triangles, bones, weights, b.normals(triangles), np.array(b.carried),
                edges)
