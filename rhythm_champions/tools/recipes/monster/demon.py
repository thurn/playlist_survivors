"""The demon (ps-6y2.34): the vendor's horned, armoured brute rebuilt whole as a few simple faceted volumes.

The trailer's drawing of the vendor mesh reads as a hunched blue thing with a ragged outline: its dense, uneven
topology lights so many creases that the body is a lattice of tiny facets with no dark inside, a blob at game size,
and its four pairs of small horns, spiked armour, teeth and slender fingers fray the outline into a fringe. What
names the demon (the horns, the claws, the spikes, the hunched brute's back, the beast's legs) is in it but lost.
So nothing of the vendor mesh is kept; every part is new, skinned to the vendor bones, and every edge that draws is
chosen here (each polygon's edges, never its triangles' diagonals, and only some of those):

- The trunk: a chest over a narrow waist, broad shoulders sloping to a bull's neck (on a thin one the lines
  over the shoulders converged into lit wedges). The hunched demon is mostly seen
  from above, down its back, so the back is drawn as a muscled kite: the spine's ridge and a line from each side of
  the neck out over the shoulder blade and in to the waist, the sides and front, and no line round it (a flat top
  at the shoulders drew a lid on a barrel, and lines round it hoops). Its flanks round in narrow quiet facets, so
  no broad facet turns edge-on to the camera at the outline (broad ones lit up as patches there).
- The head: a wedge of a skull with a crest dropping into a V between rising brows (a scowl from above),
  cheekbones, a snout, slanted eye facets and a jaw on the jaw bone.
- The horns: one big pair in four segments, out to the sides and swept back along the skull, a V from above
  whether the head is up or pitched down; the vendor's four pairs drew a knot round the head.
- Arms and legs: six-sided rings blended between the bones at each joint, lines round them only at the elbow,
  wrist, knee and hock; heavy arms; beast's legs with a high hock and three thick clawed toes.
- The hands: a broad palm and three thick claws spread wide, each on its finger's bones so it curls with them,
  where the vendor's four slender fingers drew a bundle of hairlines.
- The armour, kept as what names it: a broad pauldron on each shoulder bridging the trunk and the arm, with one
  broad spike, and a belt; dropped, the chest plate, the bracers' and greaves' spikes, the cheek horns and the
  teeth. Few spikes, and broad: small ones turned to scratches at game size, and spikes down the spine to loose
  dashes on the back seen from above.

Every triangle has its own corners and its facet's normal, as the vendor's low-poly meshes are shaded. The mesh
flags its edges, so it is drawn as designed at every distance rather than decimated: 948 triangles at every LOD,
where the vendor's are 5,992, 3,070 and 1,536. Nothing is carried: the demon fights with its claws.

Source: Magic Pig Games' Demons_LP.prefab (sha256 3bc3d9c5819bb19c9352f6a6bbf4e03cff20da3de84f3542582abac37d19de13)
and Demons_LP.fbx (e3c7dfd3827edcc7948e42e097727077e7911d8119c2568b9dfe21be97ba10dd), as manifest.json lists them.
Only its rig is used, and its rest pose to place the shapes (by eye and by its skin weights). No Blender: numpy
only. Bake: `just champions-tool monster bake demon`; review: `just champions-tool monster review demon` (and
`--look trailer`).
"""

from __future__ import annotations

import numpy as np

from champions_tools.monster import Mesh, Source

Skin = dict[str, float]


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


class Builder:
    """The new mesh: shapes of convex polygons, each polygon's edges drawn (not its triangles' diagonals), each point
    skinned to one bone or blended between two at a joint."""

    def __init__(self, source: Source):
        self.source = source
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.edges: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.weights: list[np.ndarray] = []
        self.count = 0

    def add(self, points, faces, skins, soft=()) -> None:
        """points (n, 3); faces, each (indices, a point inside the shape behind it): a convex polygon wound outward
        from that point; skins, one per point ({bone: weight}); soft, pairs of points whose edge doesn't draw."""
        pts = np.asarray(points, np.float64)
        soft = {frozenset(e) for e in soft}
        tris, flags = [], []
        for f, inside in faces:
            f = list(f)
            q = pts[f]
            normal = np.cross(q[1:-1] - q[0], q[2:] - q[0]).sum(axis=0)
            if normal @ (q.mean(axis=0) - np.asarray(inside, np.float64)) < 0:
                f.reverse()
            m = len(f)
            for k in range(1, m - 1):
                tris.append([f[0], f[k], f[k + 1]])
                drawn = [True, k + 1 == m - 1, k == 1]  # the edges opposite corners 0, k and k + 1
                pairs = [(f[k], f[k + 1]), (f[k + 1], f[0]), (f[0], f[k])]
                flags.append([d and frozenset(p) not in soft for d, p in zip(drawn, pairs)])
        bones = np.zeros((len(pts), 4), np.int32)
        weights = np.zeros((len(pts), 4), np.float32)
        for i, s in enumerate(skins):
            for k, (name, w) in enumerate(s.items()):
                bones[i, k], weights[i, k] = self.source.bone(name), w
        self.positions.append(pts)
        self.triangles.append(np.asarray(tris, np.int64) + self.count)
        self.edges.append(np.asarray(flags, np.uint8))
        self.bones.append(bones)
        self.weights.append(weights / weights.sum(axis=1, keepdims=True))
        self.count += len(pts)

    def loft(self, rings, skins, soft_rings=(), soft_sides=()) -> None:
        """A shape through rings of points (each (k, 3), all the same k, or a single point, an apex), wound the same
        way round; skins, one per ring; the edges round the rings in soft_rings, and those along the shape from the
        ring corners in soft_sides, don't draw."""
        rings = [np.atleast_2d(np.asarray(r, np.float64)) for r in rings]
        k = max(len(r) for r in rings)
        pts, first = [], []
        for r in rings:
            first.append(len(pts))
            pts.extend(r)
        centres = [r.mean(axis=0) for r in rings]
        faces, soft = [], []
        for i in range(len(rings) - 1):
            a, b = first[i], first[i + 1]
            na, nb = len(rings[i]), len(rings[i + 1])
            inside = (centres[i] + centres[i + 1]) / 2
            for j in range(k):
                if na == 1:
                    faces.append(([a, b + j, b + (j + 1) % k], inside))
                elif nb == 1:
                    faces.append(([a + j, a + (j + 1) % k, b], inside))
                else:
                    faces.append(([a + j, a + (j + 1) % k, b + (j + 1) % k, b + j], inside))
        for i in soft_rings:
            soft += [(first[i] + j, first[i] + (j + 1) % k) for j in range(k)]
        for j in soft_sides:
            soft += [(first[i] + j, first[i + 1] + j) for i in range(len(rings) - 1)]
        for i, other in ((0, 1), (len(rings) - 1, len(rings) - 2)):
            if len(rings[i]) > 1:  # a cap, wound away from the ring beyond it
                faces.append((list(range(first[i], first[i] + k)), centres[other]))
        skin = [skins[i] for i, r in enumerate(rings) for _ in r]
        self.add(pts, faces, skin, soft)

    def mesh(self) -> Mesh:
        """Every triangle its own corners with its facet's normal, as the vendor's low-poly meshes are shaded flat;
        nothing carried (the demon fights with its claws)."""
        positions = np.vstack(self.positions)
        triangles = np.vstack(self.triangles)
        corners = triangles.reshape(-1)
        p = positions[triangles]
        normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        return Mesh(positions[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    np.vstack(self.bones)[corners], np.vstack(self.weights)[corners],
                    np.repeat(normal, 3, axis=0).astype(np.float32), edges=np.vstack(self.edges))


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv)."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre, np.float64) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


def limb_rings(points, radii, n: int, ref, phase: float = 0.0):
    """Rings of n points round a polyline, each square to it, oriented by `ref` alike so the faces don't twist; a
    radius of 0 is an apex point; a radius may be a pair (along ref, across)."""
    points = np.asarray(points, np.float64)
    rings = []
    for i, (c, r) in enumerate(zip(points, radii)):
        d = unit(points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)])
        u = unit(np.asarray(ref, np.float64) - (np.asarray(ref) @ d) * d)
        v = np.cross(d, u)
        ru, rv = (r, r) if np.isscalar(r) else r
        rings.append(c if ru == 0 else ring(c, u, v, (ru, rv), n, phase))
    return rings


def one(bone: str) -> Skin:
    return {bone: 1.0}


def blend(a: str, b: str, wb: float = 0.5) -> Skin:
    return {a: 1 - wb, b: wb}


def spike(b: Builder, base, tip, width: float, bone: str) -> None:
    """A three-sided pyramid from `base` (`width` its radius) to a point at `tip`, rigid to one bone."""
    b.loft(limb_rings([base, tip], [width, 0], 3, (0, 1, 0), np.pi / 2), [one(bone)] * 2)


def trunk_section(y, zc, rx, front, back, lat_deg):
    """A ring of the trunk and the indices of its corners whose lines along the trunk draw. Corners by angle round
    the trunk from the right side (0) over the front (90) and back: the sides, the front corners, the front, the
    spine and a line over each shoulder blade at `lat_deg` below the side draw; between them the trunk rounds in
    narrow quiet facets, so no broad facet turns edge-on to the camera at the outline (a broad flank facet lit up
    as a patch there). The back is squared off, broad and flat each side of the spine."""
    lat = -lat_deg
    right_back = [lat / 3, 2 * lat / 3, lat, (lat - 90) / 2]
    angles = ([0, 18, 36, 55, 90, 125, 144, 162, 180] + [180 - a for a in right_back] + [270]
              + [360 + a for a in reversed(right_back)])
    drawn = {0, 3, 4, 5, 8, 11, 13, 15}
    t = np.radians(angles)
    c, sn = np.cos(t), np.sin(t)
    # Squared off behind: |sin| ** 0.7 pushes the back out toward a flat plane.
    z = np.where(sn > 0, front * sn, -back * np.abs(sn) ** 0.7)
    return np.stack([rx * c, np.full(len(t), y), zc + z], axis=1), drawn


def torso(b: Builder) -> None:
    """The trunk: rings from the crotch to the base of the neck, a chest over a narrow waist and broad shoulders
    sloping up to a thick neck. Seen from above, as the hunched demon mostly is, its back is a muscled kite: the
    spine's ridge down the middle and a line from each side of the neck out over the shoulder blade and in to the
    waist. No line round it: a flat top at the shoulders drew a lid on a barrel, and lines round it hoops."""
    rings, drawn = zip(*[trunk_section(0.84, -0.03, 0.06, 0.05, 0.07, 80),
                         trunk_section(0.95, -0.03, 0.15, 0.10, 0.13, 80),
                         trunk_section(1.07, -0.03, 0.125, 0.085, 0.105, 78),
                         trunk_section(1.22, -0.02, 0.2, 0.125, 0.15, 62),
                         trunk_section(1.37, -0.02, 0.245, 0.15, 0.18, 42),
                         trunk_section(1.5, -0.035, 0.265, 0.125, 0.17, 28),
                         trunk_section(1.6, 0.0, 0.13, 0.09, 0.1, 55)])
    skins = [one("Hips"), one("Hips"), blend("Hips", "Spine"), blend("Spine", "Spine1"), blend("Spine1", "Spine2"),
             one("Spine2"), blend("Spine2", "Neck")]
    b.loft(rings, skins, soft_rings=range(1, 6), soft_sides=set(range(len(rings[0]))) - drawn[0])
    # A belt round the hips.
    belt, drawn = zip(trunk_section(1.04, -0.03, 0.15, 0.1, 0.125, 80),
                      trunk_section(0.95, -0.03, 0.17, 0.115, 0.145, 80))
    b.loft(belt, [one("Hips")] * 2, soft_sides=set(range(len(belt[0]))) - drawn[0])


def head(b: Builder) -> None:
    """A wedge of a skull: a crest down the crown that drops into a V between brows rising to the sides (a scowl,
    seen from above), ridges at the cheekbones, a snout; slanted eyes under the brow; a jaw under the snout. Its
    lines run along it and round it only at the brow: a line round the crown too drew a ball cage."""

    def section(z, yc, crest, wu, hu, wm, wb, hb):
        return np.array([(0, yc + crest, z), (wu, yc + hu, z), (wm, yc, z), (wb, yc - hb, z), (-wb, yc - hb, z),
                         (-wm, yc, z), (-wu, yc + hu, z)])

    rings = [section(-0.04, 1.68, 0.05, 0.04, 0.03, 0.065, 0.04, 0.05),
             section(0.04, 1.685, 0.085, 0.065, 0.06, 0.1, 0.06, 0.08),
             section(0.14, 1.68, 0.035, 0.085, 0.065, 0.09, 0.05, 0.06),
             section(0.235, 1.63, 0.02, 0.035, 0.015, 0.045, 0.03, 0.025)]
    b.loft(rings, [one("Head")] * 4, soft_rings=(1,))
    for s in (-1, 1):
        # A thin triangle proud of the face, its outer corner high: inner, outer, lower.
        eye = np.array([(0.022, 1.69, 0.2), (0.075, 1.72, 0.17), (0.05, 1.672, 0.19)]) * [s, 1, 1]
        out = np.array([0.35 * s, 0.25, 1.0])
        b.loft([eye, eye + unit(out) * 0.012], [one("Head")] * 2)
    jaw = [np.array([(-0.065, 1.615, 0.06), (0.065, 1.615, 0.06), (0.05, 1.565, 0.06), (-0.05, 1.565, 0.06)]),
           np.array([(-0.04, 1.6, 0.225), (0.04, 1.6, 0.225), (0.028, 1.55, 0.215), (-0.028, 1.55, 0.215)])]
    b.loft(jaw, [one("Jaw")] * 2)


def horns(b: Builder) -> None:
    """One big pair from the crown, out to the sides and swept back along the skull: the type's mark, a V seen from
    above whether the head is up or pitched down in the hunch (swept up and out, they flattened into a bar). The
    vendor's four smaller pairs drew a knot round the head."""
    for s in (-1, 1):
        path = np.array([(0.05, 1.75, 0.06), (0.12, 1.8, 0.02), (0.17, 1.86, -0.06), (0.19, 1.9, -0.16),
                         (0.17, 1.93, -0.27)]) * [s, 1, 1]
        b.loft(limb_rings(path, [0.05, 0.04, 0.028, 0.016, 0], 4, (0, 1, 0), np.pi / 4), [one("Head")] * 5)


def arm(b: Builder, s: int, side: str) -> None:
    """Shoulder to wrist in six-sided rings, lines round it at the elbow and wrist only, a heavy upper arm and
    forearm; a broad pauldron over the shoulder, bridging the trunk and the arm, with one broad spike."""
    path = np.array([(0.12, 1.47, -0.03), (0.2, 1.46, -0.04), (0.3, 1.36, -0.07), (0.41, 1.225, -0.11),
                     (0.47, 1.15, -0.08), (0.54, 1.045, -0.01)]) * [s, 1, 1]
    radii = [0.08, 0.105, 0.095, 0.07, 0.1, 0.055]
    skins = [one(f"{side}Shoulder"), blend(f"{side}Shoulder", f"{side}Arm", 0.8), one(f"{side}Arm"),
             blend(f"{side}Arm", f"{side}ForeArm"), one(f"{side}ForeArm"), blend(f"{side}ForeArm", f"{side}Hand")]
    b.loft(limb_rings(path, radii, 6, (0, 0, 1)), skins, soft_rings=(1, 2, 4))
    cap = [ring(np.array([0.28 * s, 1.47, -0.05]), (1, 0, 0), (0, 0, 1), (0.13, 0.12), 5, np.pi / 2),
           ring(np.array([0.26 * s, 1.55, -0.05]), (1, 0, 0), (0, 0, 1), (0.08, 0.075), 5, np.pi / 2)]
    b.loft(cap, [one(f"{side}Arm")] * 2)
    spike(b, np.array([0.27 * s, 1.55, -0.07]), np.array([0.33 * s, 1.68, -0.15]), 0.08, f"{side}Arm")


def hand(b: Builder, s: int, side: str) -> None:
    """A big palm and three long, thick claws spread wide (the thumb's, the index's and the ring finger's), each on
    its finger's bones so it curls with them; the vendor's four slender fingers drew a bundle of hairlines."""
    palm = np.array([(0.54, 1.045, -0.01), (0.6, 0.975, 0.04)]) * [s, 1, 1]
    b.loft(limb_rings(palm, [(0.035, 0.05), (0.035, 0.07)], 4, (s, 0, 0), np.pi / 4), [one(f"{side}Hand")] * 2)
    fingers = {
        "Thumb": [(0.539, 1.005, 0.033), (0.539, 0.97, 0.08), (0.549, 0.932, 0.112), (0.589, 0.837, 0.158)],
        "Index": [(0.594, 0.975, 0.07), (0.618, 0.931, 0.112), (0.624, 0.908, 0.12), (0.623, 0.809, 0.128)],
        "Ring": [(0.63, 0.978, 0.005), (0.662, 0.935, 0.012), (0.665, 0.902, 0.01), (0.654, 0.823, -0.013)],
    }
    for name, pts in fingers.items():
        path = np.array(pts) * [s, 1, 1]
        bones = [f"{side}Hand{name}{i}" for i in (1, 2, 3, 3)]
        b.loft(limb_rings(path, [0.04, 0.03, 0.02, 0], 3, (s, 0, 0), np.pi / 2), [one(n) for n in bones],
               soft_rings=(1, 2))


def leg(b: Builder, s: int, side: str) -> None:
    """A beast's leg: thigh forward to the knee, shin back to a high hock, a long foot down to three clawed toes;
    six-sided rings blended at each joint, lines round it at the knee and the hock."""
    path = np.array([(0.09, 0.95, -0.03), (0.15, 0.78, 0.03), (0.216, 0.58, 0.09), (0.215, 0.45, 0.01),
                     (0.208, 0.31, -0.09), (0.225, 0.12, -0.04)]) * [s, 1, 1]
    radii = [0.12, 0.13, 0.09, 0.085, 0.06, 0.05]
    skins = [blend("Hips", f"{side}UpLeg", 0.7), one(f"{side}UpLeg"), blend(f"{side}UpLeg", f"{side}Leg"),
             one(f"{side}Leg"), blend(f"{side}Leg", f"{side}Foot"), blend(f"{side}Foot", f"{side}ToeBase")]
    b.loft(limb_rings(path, radii, 6, (0, 0, 1)), skins, soft_rings=(1, 3))
    toes = {"1": [(0.2, 0.09, 0.0), (0.18, 0.05, 0.1), (0.176, 0.012, 0.19)],
            "2": [(0.235, 0.09, 0.0), (0.244, 0.05, 0.1), (0.247, 0.01, 0.19)],
            "3": [(0.27, 0.09, -0.01), (0.3, 0.04, 0.09), (0.328, 0.01, 0.175)]}
    for n, pts in toes.items():
        path = np.array(pts) * [s, 1, 1]
        b.loft(limb_rings(path, [0.04, 0.028, 0], 3, (0, 1, 0), np.pi / 2),
               [one(f"{side}ToeBase"), one(f"{side}FootClaw{n}"), one(f"{side}FootClaw{n}")])


def build(source: Source) -> Mesh:
    b = Builder(source)
    torso(b)
    head(b)
    horns(b)
    for s, side in ((-1, "Left"), (1, "Right")):
        arm(b, s, side)
        hand(b, s, side)
        leg(b, s, side)
    return b.mesh()
