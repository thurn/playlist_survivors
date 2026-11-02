"""The skeleton (ps-6y2.31): the vendor's armour kept, the parts that drew scribble rebuilt as simple faceted shapes.

The trailer's drawing of the vendor mesh already reads in a crowd by its helmet, its shield's circle and its sword,
and its armour (cuirass and breastplate, pauldrons, tassets, greaves, boots, bracers, shield) is low-poly plate
whose creases close into facets. So the hand is light: those parts stay as the vendor made them, drawn by their
24-degree creases as in the trailer. What changes:

- The sword: a broad diamond-section blade with a ridge, a crossguard, a grip and a pommel on the vendor sword's
  axis and length. The vendor's thin blade drew one hairline that read as a spear or a needle.
- The helmet: a faceted helm (a quiet cap in an octagon, a brow ring, cheek and neck guards, a nasal). The vendor's
  rounded helmet lit only some of its creases, in lines that stopped mid-face and a dashed crest.
- The skull: a block with a face plane, hexagonal eye sockets, a triangle nose and a jaw, where the vendor's skull
  and sixteen teeth drew a knot of tiny strokes in the helmet's opening.
- The bare bones: four-sided prisms for the thighs, shins, upper arms and forearms, diamonds at the knees and elbows,
  a six-sided neck, a wedge of a pelvis and mitten hands, where the vendor's finely modelled bones and forty finger
  bones drew short strokes at every joint.
- The waist: one faceted band for the vendor's rounded belt and the plate under it, which drew broken strokes.
- The shield: the vendor's, pressed to 40% of its depth. Its domed face and deep rim caught the fresnel rim facet by
  facet and drew a lit band down its edge; thin, the rim is an outline and the face one dark plane.
- Dropped: what is hidden inside the armour in every view (toes and feet in the boots, the spine, ribs and
  collarbones under the cuirass), and straps, cuffs and second layers of plate that only doubled a line beside them
  (bracer and boot straps, wrist cuffs, the inner tassets, the lower pauldrons).

Every edge's flag is computed here with the trailer's rule (creases over 24 degrees, open edges), so the mesh is
drawn as designed at every distance rather than decimated: 2,524 triangles at every LOD, where the vendor's are
9,310, 4,734 and 2,367. The new shapes are rigid to one vendor bone each, so the mesh moves with the vendor rig and
clips; the sword stays carried, so the shatter flings it.

Source: Magic Pig Games' SkeletonWarriorLP.prefab (sha256
a722c1da9a55f88a871f956f53a4838bf294cb1af5298047afb2a469904f6c94) and SkeletonWarriorLP.fbx
(de8f13673aade336a5d2ef08ed843e2966d2148d0ce00bc5138bed2198d8644a), as manifest.json lists them. No Blender:
numpy only. Bake: `just champions-tool monster bake skeleton`; review: `just champions-tool monster review skeleton`
(and `--look trailer`).
"""

from __future__ import annotations

import numpy as np

from champions_tools.monster import Mesh, Source

# Hidden inside the armour in every view of the rest pose: the toes and feet in the boots, the spine, ribs and
# collarbones under the cuirass and pauldrons.
HIDDEN = ([f"pCube{i}" for i in range(58, 74)] + [f"pCube{i}" for i in range(87, 103)]
          + [f"pCube{i}" for i in range(75, 85)] + ["pCube3", "pCube8", "pCube17", "pCube103", "pCube104", "pCube125",
                                                    "pCube137"])
# Rebuilt below as a few simple shapes: the skull, jaw and teeth; the neck's vertebrae; the hands' bones and wraps;
# the pelvis; the sword; the helmet; the bones of the legs and arms; the belt and the plate under it.
REBUILT = (["pCube129", "pCube131"] + [f"polySurface{i}" for i in range(289, 305)]
           + ["pCube42", "pCube43", "pCube44", "pCube46"]
           + [f"pCube{i}" for i in range(18, 38)] + [f"pCube{i}" for i in range(105, 125)] + ["Hand_Wraps_lp"]
           + ["pCube55", "pCube130", "pCube136"] + ["Sword_LP", "lp_Helmet"]
           + ["pCube132", "pCube134", "pCube56", "pCube85", "pCube57", "pCube86", "pCube133", "pCube135"]
           + ["pCube126", "pCube138", "pCube127", "pCube128", "pCube139", "pCube140"] + ["Chest_2_lp", "Crotch_LP"])
# Straps, cuffs and second layers of plate that only add short strokes beside a line already drawn.
CLUTTER = ["Bracer_Straps_LP", "Bracers2_LP", "boots_straps_LP", "boot_straps_LP_Left", "Hips_Inner_lp",
           "Pauldrons_Lower_LP"]


class Builder:
    """Shapes added to the kept vendor parts: convex, wound outward, each vertex rigid to one bone."""

    def __init__(self, source: Source):
        self.source = source
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.carried: list[np.ndarray] = []
        self.count = 0

    def add(self, points, faces, bone: str | list[str], carried: bool = False) -> None:
        """A convex shape: points (n, 3) and faces (lists of point indices, convex polygons), each face wound
        outward from the shape's centre; `bone` one bone, or one per point."""
        pts = np.asarray(points, np.float64)
        centre = pts.mean(axis=0)
        tris = []
        for f in faces:
            for k in range(1, len(f) - 1):
                tri = [f[0], f[k], f[k + 1]]
                a, b, c = pts[tri]
                if np.dot(np.cross(b - a, c - a), (a + b + c) / 3 - centre) < 0:
                    tri = [tri[0], tri[2], tri[1]]
                tris.append(tri)
        names = [bone] * len(pts) if isinstance(bone, str) else bone
        self.positions.append(pts)
        self.triangles.append(np.asarray(tris, np.int64) + self.count)
        self.bones.append(np.array([self.source.bone(n) for n in names]))
        self.carried.append(np.full(len(pts), carried))
        self.count += len(pts)

    def prism(self, rings, faces_each: int, bone, carried: bool = False) -> None:
        """A shape lofted through rings of `faces_each` points each (a ring may be one point, an apex)."""
        pts, offsets = [], []
        for r in rings:
            offsets.append(len(pts))
            pts.extend(np.atleast_2d(r))
        faces = []
        for i in range(len(rings) - 1):
            a, b = offsets[i], offsets[i + 1]
            na, nb = len(np.atleast_2d(rings[i])), len(np.atleast_2d(rings[i + 1]))
            for k in range(faces_each):
                if na == 1:
                    faces.append([a, b + k, b + (k + 1) % nb])
                elif nb == 1:
                    faces.append([a + k, a + (k + 1) % na, b])
                else:
                    faces.append([a + k, a + (k + 1) % na, b + (k + 1) % nb, b + k])
        if len(np.atleast_2d(rings[0])) > 1:
            faces.append(list(range(offsets[0], offsets[0] + len(rings[0]))))
        if len(np.atleast_2d(rings[-1])) > 1:
            faces.append(list(range(offsets[-1], offsets[-1] + len(rings[-1]))))
        if isinstance(bone, list):  # one bone per ring
            bone = [bone[i] for i, r in enumerate(rings) for _ in np.atleast_2d(r)]
        self.add(pts, faces, bone, carried)


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv)."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def sword(b: Builder, source: Source) -> None:
    """A broad faceted blade with a ridge down its middle, a crossguard, a grip and a pommel, on the vendor sword's
    axis, plane and length, so it reads as a sword where the vendor's thin blade drew a hairline."""
    p = source.part("Sword_LP")
    q = source.positions[p.first:p.first + p.count].astype(np.float64)
    mid = q.mean(axis=0)
    _, _, axes = np.linalg.svd(q - mid)
    along = (q - mid) @ axes[0]
    if along.max() > -along.min():
        axes[0] = -axes[0]
        along = -along
    tip_u = along.min()  # the tip at the negative end, the pommel at the positive
    u, w, t = axes[0], unit(axes[1]), unit(np.cross(axes[0], axes[1]))
    bone = "ShieldBoneR"

    def at(s, dw=0.0, dt=0.0):
        return mid + u * s + w * dw + t * dt

    def diamond(s, width, thick):
        return np.array([at(s, width / 2), at(s, 0, thick / 2), at(s, -width / 2), at(s, 0, -thick / 2)])

    guard = 0.065
    b.prism([at(tip_u), diamond(tip_u + 0.26, 0.078, 0.034), diamond(guard, 0.09, 0.04)], 4, bone, carried=True)
    box = [at(guard + dx, dw, dt) for dx in (-0.004, 0.05) for dw, dt in ((0.14, 0.024), (0.14, -0.024),
                                                                         (-0.14, -0.024), (-0.14, 0.024))]
    b.add(box, [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]], bone, carried=True)
    grip = [diamond(guard + 0.05, 0.03, 0.03), diamond(0.35, 0.026, 0.026)]
    b.prism(grip, 4, bone, carried=True)
    b.prism([at(0.345), diamond(0.39, 0.06, 0.06), at(along.max())], 4, bone, carried=True)


def helmet(b: Builder) -> None:
    """A faceted helm: a low cap inside an octagon of eight facets, a ring at the brow, cheek guards and a neck guard
    below it round an open face, and a nasal down the middle. The vendor's rounded helmet lit only some of its
    creases, in lines that stopped mid-face. The cap's facets turn by under 20 degrees, so from above the head is
    an octagon in an octagon rather than eight strokes meeting in a knot at a point."""

    def at(k, y, rx, rz):
        t = 2 * np.pi * k / 8
        return (rx * np.sin(t), y, 0.035 + rz * np.cos(t))

    apex = [(0.0, 1.715, 0.03)]
    crown = [at(k, 1.685, 0.07, 0.092) for k in range(8)]
    brow = [at(k, 1.625, 0.1, 0.12) for k in range(8)]
    # The guards' ring, its front corners (the cheeks') brought forward round the jaw; none at the front, the face.
    guard = [at(k + (0.22 if k == 1 else -0.22 if k == 7 else 0), 1.5, 0.086, 0.106) for k in range(1, 8)]
    pts = apex + crown + brow + guard
    c, w, g = 1, 9, 17  # first crown, brow and guard point
    faces = [[0, c + k, c + (k + 1) % 8] for k in range(8)]
    faces += [[c + k, c + (k + 1) % 8, w + (k + 1) % 8, w + k] for k in range(8)]
    faces += [[w + k, w + k + 1, g + k, g + k - 1] for k in range(1, 7)]
    b.add(pts, faces, "Base HumanHead")
    front = 0.035 + 0.12  # the brow ring's front corner
    nasal = [(sx, y, front + dz + depth) for sx in (-0.008, 0.008) for y, dz in ((1.632, 0.001), (1.562, 0.006))
             for depth in (0.0, -0.01)]
    b.add(nasal, [[0, 1, 3, 2], [4, 5, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5], [0, 1, 5, 4], [2, 3, 7, 6]],
          "Base HumanHead")


def skull(b: Builder) -> None:
    """A low-poly skull in the helmet's opening: a block whose front is the face, two hexagonal eye sockets, a
    triangular nose, and the jaw on its own bone with the line of the teeth across it."""
    x, y, z = np.eye(3)
    outline = np.array([(-0.045, 1.675), (0.045, 1.675), (0.066, 1.64), (0.066, 1.578), (0.046, 1.548), (-0.046, 1.548),
                        (-0.066, 1.578), (-0.066, 1.64)])
    # The face is a smaller plane forward of the cheeks and under the brow, so it shows in the helmet's opening.
    face = outline.copy()
    face[[0, 1], 1], face[[2, 7], 1] = 1.626, 1.614
    rings = [np.c_[outline, np.full(len(outline), -0.05)], np.c_[outline, np.full(len(outline), 0.1)],
             np.c_[face * [0.72, 1], np.full(len(outline), 0.137)]]
    b.prism(rings, len(outline), "Base HumanHead")
    for side in (-1, 1):
        c = np.array([side * 0.031, 1.6, 0.133])
        b.prism([ring(c, x, y, (0.016, 0.013), 6, np.pi / 6), ring(c + z * 0.008, x, y, (0.013, 0.010), 6, np.pi / 6)],
                6, "Base HumanHead")
    nose = np.array([(-0.009, 1.578), (0.009, 1.578), (0.0, 1.562)])
    b.prism([np.c_[nose, np.full(3, 0.132)], np.c_[nose * [0.8, 1] + [0, 0.001], np.full(3, 0.141)]], 3,
            "Base HumanHead")
    profile = np.array([(1.546, 0.0), (1.546, 0.128), (1.531, 0.135), (1.500, 0.112), (1.497, 0.0)])
    b.prism([np.c_[np.full(5, sx), profile] for sx in (-0.044, 0.044)], 5, "Base HumanJaw")


def bone_shaft(b: Builder, a, c, radius: float, bones, sides: int = 4, end_radius: float | None = None) -> None:
    """A bone from a to c: a prism of `sides` faces with a corner forward (a ridge down its front), each end ring on
    its own bone, tapering to `end_radius` at c if given."""
    a, c = np.asarray(a, np.float64), np.asarray(c, np.float64)
    d = unit(c - a)
    forward = np.array([0.0, 0.0, 1.0])
    u = unit(forward - (forward @ d) * d)
    v = np.cross(d, u)
    rc = radius if end_radius is None else end_radius
    b.prism([ring(a, u, v, (radius, radius), sides), ring(c, u, v, (rc, rc), sides)], sides, bones)


def joint(b: Builder, centre, radius: float, bone: str) -> None:
    """A knee or an elbow: an octahedron, a small diamond between two bones."""
    c = np.asarray(centre, np.float64)
    pts = [c + s * radius * e for e in np.eye(3) for s in (1, -1)]
    faces = [[i, j, k] for i in (0, 1) for j in (2, 3) for k in (4, 5)]
    b.add(pts, faces, bone)


def limbs(b: Builder) -> None:
    """The bare bones between the armour, each a slim four-sided prism with a diamond at the knee and elbow: the
    vendor's bones were finely modelled, and at game scale their knobs drew a knot of short strokes at each joint."""
    for side, s in ((-1, "L"), (1, "R")):
        hip, knee, ankle = (side * 0.128, 0.9, 0.045), (side * 0.105, 0.455, -0.012), (side * 0.103, 0.07, -0.045)
        bone_shaft(b, hip, knee, 0.026, f"Base Human{s}Thigh1", end_radius=0.02)
        joint(b, knee, 0.032, f"Base Human{s}Calf")
        bone_shaft(b, knee, ankle, 0.02, f"Base Human{s}Calf", end_radius=0.016)
        shoulder, elbow, wrist = (side * 0.17, 1.37, -0.022), (side * 0.396, 1.215, 0.005), (side * 0.54, 1.09, 0.135)
        bone_shaft(b, shoulder, elbow, 0.022, f"Base Human{s}Upperarm1", end_radius=0.017)
        joint(b, elbow, 0.026, f"Base Human{s}Forearm1")
        bone_shaft(b, elbow, wrist, 0.017, f"Base Human{s}Forearm1", end_radius=0.014)


def belt(b: Builder) -> None:
    """The waist: one faceted band between the cuirass and the tassets, where the vendor's rounded belt drew broken
    strokes."""
    top = ring((0, 1.16, 0.012), (1, 0, 0), (0, 0, 1), (0.142, 0.118), 8, np.pi / 8)
    bottom = ring((0, 1.075, 0.016), (1, 0, 0), (0, 0, 1), (0.13, 0.108), 8, np.pi / 8)
    b.prism([top, bottom], 8, "Base HumanRibcage")


def hands(b: Builder) -> None:
    """Each hand a mitten: a block round the palm and the curled fingers."""
    for side, bone in ((-1, "Base HumanLPalm"), (1, "Base HumanRPalm")):
        x0, x1 = sorted((side * 0.535, side * 0.645))
        pts = [(xx, yy, zz) for xx in (x0, x1) for yy in (1.0, 1.09) for zz in (0.112, 0.236)]
        faces = [[0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]]
        b.add(pts, faces, bone)


def pelvis(b: Builder) -> None:
    """The pelvis between the tassets: one wedge, wide at the hips, narrow at the crotch."""
    top = ring((0, 1.0, 0.02), (1, 0, 0), (0, 0, 1), (0.125, 0.07), 6, np.pi / 6)
    bottom = ring((0, 0.85, 0.03), (1, 0, 0), (0, 0, 1), (0.05, 0.05), 6, np.pi / 6)
    b.prism([top, bottom], 6, "Base HumanPelvis")


def flat_shield(source: Source, thickness: float = 0.4) -> np.ndarray:
    """The source's positions with the shield pressed to `thickness` of its depth about its middle plane: its domed
    face and deep rim caught the fresnel rim facet by facet and drew a lit band down its edge; thin, the rim is an
    outline and the face one plane."""
    positions = source.positions.astype(np.float64)
    p = source.part("Shield_Main_LP")
    shield = positions[p.first:p.first + p.count]
    mid = shield.mean(axis=0)
    normal = np.linalg.svd(shield - mid)[2][2]  # the axis it is thinnest along
    depth = (shield - mid) @ normal
    positions[p.first:p.first + p.count] -= np.outer(depth * (1 - thickness), normal)
    return positions


def build(source: Source) -> Mesh:
    drop = set(HIDDEN) | set(REBUILT) | set(CLUTTER)
    keep_vertex = np.ones(len(source.positions), bool)
    for p in source.parts:
        if p.name in drop:
            keep_vertex[p.first:p.first + p.count] = False
    tris = source.triangles[keep_vertex[source.triangles].all(axis=1)]
    used = np.unique(tris)
    remap = np.full(len(source.positions), -1)
    remap[used] = np.arange(len(used))

    b = Builder(source)
    sword(b, source)
    helmet(b)
    skull(b)
    bone_shaft(b, (0, 1.44, 0.0), (0, 1.56, 0.012), 0.017, ["Base HumanNeck", "Base HumanNeck1"], sides=6)
    limbs(b)
    belt(b)
    hands(b)
    pelvis(b)

    n = len(used)
    positions = np.vstack([flat_shield(source)[used], *b.positions]).astype(np.float32)
    triangles = np.vstack([remap[tris], *(t + n for t in b.triangles)]).astype(np.int32)
    bones = np.zeros((len(positions), 4), np.int32)
    weights = np.zeros((len(positions), 4), np.float32)
    bones[:n], weights[:n] = source.bones[used], source.weights[used]
    bones[n:, 0] = np.concatenate(b.bones)
    weights[n:, 0] = 1
    carried = np.concatenate([source.carried[used], *b.carried])
    return Mesh(positions, triangles, bones, weights, carried=carried, edges=creases(positions, triangles))


def creases(positions: np.ndarray, triangles: np.ndarray, angle_deg: float = 24.0) -> np.ndarray:
    """Each triangle's edges that draw, as StyleMesh.FeatureEdges flags them (corners welded at 0.1 mm; an edge
    draws when its faces turn by more than `angle_deg`, or when it is open or shared by more than two). Flagged
    here, the mesh is drawn as it is at every distance rather than decimated."""
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
