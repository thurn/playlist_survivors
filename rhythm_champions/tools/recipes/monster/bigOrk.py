"""The big ork (ps-6y2.37): the vendor's armoured brute and its greatsword rebuilt whole as a few simple faceted volumes.

The trailer's drawing of the vendor mesh reads by its bulk, a hunched brute with long arms and a long sword, and up
close as an armoured warrior with every plate outlined. But its 6,112-triangle body lights so many creases that it is
never dark inside: bands round the arms and legs, a studded belt with a round buckle drawn as a spider's web, fang
charms, wrappings and a pauldron of small plates. At game size it is a striped orange clump and in a crowd the figures
tangle together; its 700-triangle sword hangs edge-on as a jagged streak whose outline breaks into dashes. What names
the big ork (the hunched bulk, the long heavy arms, the head slung low with tusks and pointed ears, one pauldron, the
strap across the chest and the greatsword) is in it but buried. So nothing of the vendor mesh is kept; every part is
new, skinned to the vendor bones, and every edge that draws is chosen here (each polygon's edges, never its
triangles' diagonals, and only some of those):

- The trunk: a barrel from the crotch to a thick neck, a gut over the belt and high shoulders, drawn by eight long
  lines and no line round it; a belt; a strap over the left shoulder and across the chest and back to the right hip.
- The head, a quarter larger than the vendor's skull and slung low between the shoulders: one wedge from the back of
  the skull to a boar's snout with one line across it at the brow dipping in a scowl, slanted eyes, a jaw wider than
  the snout jutting past it, two thick tusks hooking out of its corners and pointed ears flat as leaves. Its first
  builds read as a helmet's visor from above (a crest, and a straight line round the brow), then as a blank dome.
- Arms and legs: six-sided rings blended between the bones at each joint, heavy upper arms and forearms, one line
  round each limb at the elbow or knee; big fists, each one block of palm and fingers and a thumb; blocky boots.
  The vendor's bracers, greaves, knee pads and slender fingers are dropped (cuffs, plates and banded fists made it
  a robot's).
- One pauldron of three lames over the left shoulder, laid flat (upright, it read as a backpack); a loincloth.
- The greatsword: a cleaver with two notches in its back, a crossguard and a grip, a sixth shorter and narrower
  than the vendor's; turned a quarter on its grip so its flat faces the camera as it hangs (edge-on it drew as a
  bright bar), its flats unfolded (a folded flat caught the rim and glowed). Carried: it shatters with the body.

Every triangle has its own corners and its facet's normal leant up its shape toward its root (up the trunk and the
legs, up each arm toward the shoulder): the rim mixes the normal with the facet's own, and unleant, the long facets
of the round trunk and the hanging arms lit whole as gradient bands. A plate's underside keeps its own normal. The
mesh flags its edges, so it is drawn as designed at every distance rather than decimated: 1,060 triangles at every
LOD, where the vendor's are 6,812, 3,455 and 1,728.

Source: Synty's Character_BR_BigOrk_01.prefab and SM_Wep_Slayer_01.prefab from POLYGON Fantasy Rivals (package sha256
f5b2197465eb4fcd0ce06cec1faa2790523a8a1ae8a669c1ef9a0b0316919e6c), as manifest.json lists them. Only its rig is used,
and its rest pose and skin weights to place the shapes. No Blender: numpy only. Bake: `just champions-tool monster
bake bigOrk`; review: `just champions-tool monster review bigOrk` (and `--look trailer`).
"""

from __future__ import annotations

import numpy as np

from champions_tools.monster import Mesh, Source

Skin = dict[str, float]
SWORD = "SM_Wep_Slayer_01"
UP = (0.0, 1.0, 0.0)  # a lean up the trunk, the legs and the head
LEAN = 0.8  # how far the shading normals lean (Builder.mesh)


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


class Builder:
    """The new mesh: shapes of convex polygons, each polygon's edges drawn (not its triangles' diagonals), each point
    skinned to one bone or blended between two at a joint; a shape may be carried (the sword)."""

    def __init__(self, source: Source):
        self.source = source
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.edges: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.weights: list[np.ndarray] = []
        self.carried: list[np.ndarray] = []
        self.leans: list[np.ndarray] = []
        self.count = 0

    def add(self, points, faces, skins, soft=(), carried=False, lean=(0, 0, 0)) -> None:
        """points (n, 3); faces, each (indices, a point inside the shape behind it): a convex polygon wound outward
        from that point; skins, one per point ({bone: weight}); soft, pairs of points whose edge doesn't draw; lean,
        a vector its shading normals lean along (see mesh)."""
        pts = np.asarray(points, np.float64)
        soft = {frozenset(e) for e in soft}
        lean = np.asarray(lean, np.float64)
        tris, flags, leans = [], [], []
        for f, inside in faces:
            f = list(f)
            q = pts[f]
            normal = np.cross(q[1:-1] - q[0], q[2:] - q[0]).sum(axis=0)
            if normal @ (q.mean(axis=0) - np.asarray(inside, np.float64)) < 0:
                f.reverse()
                normal = -normal
            # A face turned against the lean (a plate's underside) keeps its own normal: leant, it drew solid when its
            # shard turned over.
            facing = normal @ lean >= -0.5 * np.linalg.norm(normal) * np.linalg.norm(lean)
            m = len(f)
            for k in range(1, m - 1):
                tris.append([f[0], f[k], f[k + 1]])
                leans.append(lean if facing else np.zeros(3))
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
        self.carried.append(np.full(len(pts), carried))
        self.leans.append(np.asarray(leans, np.float64).reshape(-1, 3))
        self.count += len(pts)

    def loft(self, rings, skins, soft_rings=(), soft_sides=(), carried=False, caps=(True, True), lean=(0, 0, 0)) -> None:
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
        for (i, other), cap in zip(((0, 1), (len(rings) - 1, len(rings) - 2)), caps):
            if cap and len(rings[i]) > 1:  # a cap, wound away from the ring beyond it
                faces.append((list(range(first[i], first[i] + k)), centres[other]))
        skin = [skins[i] for i, r in enumerate(rings) for _ in r]
        self.add(pts, faces, skin, soft, carried, lean)

    def mesh(self) -> Mesh:
        """Every triangle its own corners, their normal its facet's leaning along its shape's lean: up a limb toward
        its root, which is up in the world while it hangs. The rim mixes the normal with the facet's own, so the
        long facets turned across the view on a body or a hanging limb stay dark rather than glowing whole."""
        positions = np.vstack(self.positions)
        triangles = np.vstack(self.triangles)
        corners = triangles.reshape(-1)
        p = positions[triangles]
        normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        normal += LEAN * np.vstack(self.leans)
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        return Mesh(positions[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    np.vstack(self.bones)[corners], np.vstack(self.weights)[corners],
                    np.repeat(normal, 3, axis=0).astype(np.float32), np.concatenate(self.carried)[corners],
                    edges=np.vstack(self.edges))


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


def trunk_section(y, zc, rx, front, back, n=16):
    """A ring of the trunk: n corners from the right side (0) over the front (90), the left and the back."""
    t = 2 * np.pi * np.arange(n) / n
    c, sn = np.cos(t), np.sin(t)
    z = np.where(sn > 0, front * sn, back * sn)
    return np.stack([rx * c, np.full(n, y), zc + z], axis=1)


# The trunk's rings from the crotch to the neck: height, centre (z), half width, depth in front and behind, skin.
TRUNK = [(0.9, -0.02, 0.22, 0.18, 0.18, one("Hips")),
         (1.02, 0.0, 0.34, 0.3, 0.24, one("Hips")),
         (1.2, 0.02, 0.38, 0.36, 0.27, blend("Hips", "Spine_01")),
         (1.4, 0.0, 0.37, 0.31, 0.3, blend("Spine_01", "Spine_02")),
         (1.6, -0.02, 0.4, 0.3, 0.34, blend("Spine_02", "Spine_03")),
         (1.8, -0.04, 0.4, 0.22, 0.3, one("Spine_03")),
         (2.0, -0.08, 0.3, 0.12, 0.22, blend("Spine_03", "Neck")),
         (2.08, -0.07, 0.14, 0.08, 0.12, one("Neck"))]


def torso(b: Builder) -> None:
    """The trunk drawn by eight long lines and no line round it (rings round a trunk draw hoops from above), and a
    belt round the hips drawn by its rims and four lines across."""
    rings = [trunk_section(*r[:5]) for r in TRUNK]
    drawn = {0, 2, 4, 6, 8, 10, 12, 14}
    b.loft(rings, [r[5] for r in TRUNK], soft_rings=range(1, len(TRUNK) - 1), soft_sides=set(range(16)) - drawn,
           lean=UP)
    belt = [trunk_section(1.0, 0.0, 0.36, 0.32, 0.26), trunk_section(1.18, 0.02, 0.4, 0.38, 0.29)]
    b.loft(belt, [one("Hips")] * 2, soft_sides=set(range(16)) - {0, 4, 8, 12}, lean=UP)


def trunk_point(y: float, t: float, out: float):
    """The trunk's surface at height y and angle t round it (as trunk_section's), `out` metres proud of it, with
    the skin of the rings either side blended by height."""
    ys = [r[0] for r in TRUNK]
    i = int(np.clip(np.searchsorted(ys, y) - 1, 0, len(TRUNK) - 2))
    f = float(np.clip((y - ys[i]) / (ys[i + 1] - ys[i]), 0, 1))
    zc, rx, front, back = (np.array(TRUNK[i][1:5]) * (1 - f) + np.array(TRUNK[i + 1][1:5]) * f)
    sn = np.sin(t)
    p = np.array([(rx + out) * np.cos(t), y, zc + (front + out if sn > 0 else back + out) * sn])
    skin: Skin = {}
    for k, w in ((i, 1 - f), (i + 1, f)):
        for bone, bw in TRUNK[k][5].items():
            skin[bone] = skin.get(bone, 0) + bw * w
    return p, skin


def strap(b: Builder) -> None:
    """A band over the left shoulder and across the chest and back to the right hip, its plane tilted between
    them: one diagonal across the round trunk, front and back."""
    n, half = 16, 0.05
    tops, bottoms, skins = [], [], []
    for t in 2 * np.pi * np.arange(n) / n:
        y = 1.5
        for _ in range(20):  # the height where the tilted plane crosses the surface at this angle
            p, _ = trunk_point(y, t, 0.0)
            y = float(np.clip(1.59 - 1.3 * p[0], 0.95, 2.02))
        top, skin = trunk_point(min(y + half, 2.04), t, 0.025)
        bottom, _ = trunk_point(y - half, t, 0.025)
        tops.append(top)
        bottoms.append(bottom)
        skins.append(skin)
    pts = np.vstack([tops, bottoms])
    faces = [([j, (j + 1) % n, n + (j + 1) % n, n + j], (pts[j] + pts[n + (j + 1) % n]) / 2 * [0.5, 1, 0.5])
             for j in range(n)]
    soft = [(j, n + j) for j in range(n)]
    b.add(pts, faces, skins + skins, soft, lean=UP)


def head(b: Builder) -> None:
    """A big head, a quarter larger than drawn here about the neck's joint, so its features break its outline from
    above: one wedge from the back of the skull over the brow to a boar's snout, drawn along its sides, with one line
    across it at the brow that dips in a scowl (a straight line round it read as a helmet's visor, and with none it
    was a blank dome); slanted eyes under the brow; a jaw wider than the snout jutting past it, two thick tusks hooking
    up and out from its corners; pointed ears out and back, flat as leaves (a cone read as a horn), standing up
    past the outline as the hunch pitches the head forward (out to the sides, they sank into the shoulders)."""
    pivot = np.array([0.0, 2.03, 0.02])

    def big(points):
        return pivot + (np.asarray(points, np.float64) - pivot) * 1.25

    def section(z, crest, top, wt, wm, ym, bottom, wb):
        return big([(0, crest, z), (wt, top, z), (wm, ym, z), (wb, bottom, z), (-wb, bottom, z), (-wm, ym, z),
                    (-wt, top, z)])

    wedge = [section(-0.17, 2.13, 2.12, 0.08, 0.15, 2.05, 1.97, 0.11),
             section(0.0, 2.21, 2.2, 0.11, 0.18, 2.1, 1.97, 0.14),
             section(0.17, 2.15, 2.22, 0.12, 0.18, 2.1, 1.98, 0.13),
             section(0.3, 2.1, 2.09, 0.07, 0.12, 2.04, 1.97, 0.1),
             section(0.42, 2.06, 2.05, 0.05, 0.08, 2.02, 1.98, 0.07)]
    b.loft(wedge, [one("Head")] * 5, soft_rings=(1, 3), soft_sides=(0, 1, 6), lean=UP)
    jaw = [big([(-0.2, 2.0, 0.0), (0.2, 2.0, 0.0), (0.18, 1.85, 0.02), (-0.18, 1.85, 0.02)]),
           big([(-0.21, 2.0, 0.45), (0.21, 2.0, 0.45), (0.16, 1.89, 0.43), (-0.16, 1.89, 0.43)])]
    b.loft(jaw, [one("Head")] * 2, lean=UP)
    for s in (-1, 1):
        tusk = big(np.array([(0.16, 1.98, 0.42), (0.22, 2.08, 0.46), (0.28, 2.19, 0.45), (0.31, 2.27, 0.38)])
                   * [s, 1, 1])
        b.loft(limb_rings(tusk, [0.075, 0.065, 0.045, 0], 4, (0, 0, 1), np.pi / 4), [one("Head")] * 4,
               soft_rings=(1, 2))
        leaf = big(np.array([(0.14, 2.17, 0.07), (0.42, 2.16, -0.18), (0.14, 2.05, -0.12)]) * [s, 1, 1])
        normal = unit(np.cross(leaf[1] - leaf[0], leaf[2] - leaf[0]))
        b.loft([leaf + normal * 0.015, leaf - normal * 0.015], [one("Head")] * 2, lean=UP)
        eye = big(np.array([(0.035, 2.12, 0.25), (0.13, 2.155, 0.225), (0.09, 2.105, 0.24)]) * [s, 1, 1])
        b.loft([eye, eye + unit([0.3 * s, 0.4, 1.0]) * 0.015], [one("Head")] * 2)


def arm(b: Builder, s: int, side: str) -> None:
    """Shoulder to wrist in six-sided rings blended between the bones at each joint: a heavy upper arm, a bulging
    forearm."""
    path = np.array([(0.3, 1.86, -0.05), (0.45, 1.84, -0.06), (0.7, 1.8, -0.06), (0.96, 1.76, -0.05),
                     (1.15, 1.78, -0.05), (1.42, 1.83, -0.05)]) * [s, 1, 1]
    radii = [0.16, 0.19, 0.17, 0.14, 0.17, 0.12]
    skins = [one(f"Clavicle_{side}"), blend(f"Clavicle_{side}", f"Shoulder_{side}", 0.8), one(f"Shoulder_{side}"),
             blend(f"Shoulder_{side}", f"Elbow_{side}"), one(f"Elbow_{side}"), blend(f"Elbow_{side}", f"Hand_{side}")]
    # One line round it, at the elbow: with one at the wrist too it was chopped into blocks, and with none its long
    # lines were a ladder. Open at both ends, inside the trunk and the fist.
    b.loft(limb_rings(path, radii, 6, (0, 1, 0)), skins, soft_rings=(0, 1, 2, 4, 5), caps=(False, False),
           lean=(-s, 0, 0))


def hand(b: Builder, s: int, side: str) -> None:
    """A big fist: one block of palm and fingers, its finger joints on both fingers' bones so it curls with them, and
    a thick thumb; separate fingers drew as scratches."""
    r = "" if side == "L" else " 1"
    wrist = np.array([1.42, 1.83, -0.03])

    def big(points):
        return (wrist + (np.asarray(points, np.float64) - wrist) * 1.25) * [s, 1, 1]

    # One block from the wrist to the fingertips, drawn round only at its ends: a palm and a finger block, each
    # closed, banded the forearm into a stack of bricks.
    fist = [[(1.42, 1.9, 0.07), (1.42, 1.9, -0.15), (1.42, 1.76, -0.15), (1.42, 1.76, 0.07)]]
    fist += [[(x, 1.885 - k, 0.1), (x, 1.885 - k, -0.16), (x, 1.765 - k, -0.16), (x, 1.765 - k, 0.1)]
             for x, k in ((1.6, 0.0), (1.73, 0.025), (1.86, 0.05))]
    skins = [one(f"Hand_{side}"), blend(f"IndexFinger_01{r}", f"Finger_01{r}"),
             blend(f"IndexFinger_02{r}", f"Finger_02{r}"), blend(f"IndexFinger_04{r}", f"Finger_04{r}")]
    b.loft([big(q) for q in fist], skins, soft_rings=(1, 2), lean=(-s, 0, 0))
    thumb = big([(1.5, 1.79, 0.06), (1.6, 1.74, 0.14), (1.71, 1.68, 0.18)])
    b.loft(limb_rings(thumb, [0.065, 0.058, 0.048], 4, (0, 1, 0), np.pi / 4),
           [one(f"Thumb_01{r}"), one(f"Thumb_02{r}"), one(f"Thumb_03{r}")], soft_rings=(1,))


def leg(b: Builder, s: int, side: str) -> None:
    """A thick thigh and shin in six-sided rings, a line round them at the knee, and a blocky boot drawn round only
    at its heel and toe."""
    path = np.array([(0.13, 1.02, -0.05), (0.2, 0.8, -0.1), (0.21, 0.56, -0.08), (0.22, 0.4, -0.08),
                     (0.24, 0.2, -0.07)]) * [s, 1, 1]
    radii = [0.2, 0.19, 0.15, 0.16, 0.12]
    skins = [blend("Hips", f"UpperLeg_{side}", 0.7), one(f"UpperLeg_{side}"), blend(f"UpperLeg_{side}", f"LowerLeg_{side}"),
             one(f"LowerLeg_{side}"), blend(f"LowerLeg_{side}", f"Ankle_{side}")]
    # Open at both ends, inside the trunk and the boot: a ring at the ankle banded the shin above the boot's.
    b.loft(limb_rings(path, radii, 6, (0, 0, 1)), skins, soft_rings=(0, 1, 3, 4), caps=(False, False), lean=UP)
    boot = [np.array([(0.13, 0.0, -0.18), (0.35, 0.0, -0.18), (0.35, 0.22, -0.17), (0.13, 0.22, -0.17)]),
            np.array([(0.12, 0.0, 0.05), (0.36, 0.0, 0.05), (0.35, 0.2, 0.03), (0.13, 0.2, 0.03)]),
            np.array([(0.15, 0.0, 0.29), (0.33, 0.0, 0.29), (0.32, 0.09, 0.27), (0.16, 0.09, 0.27)])]
    b.loft([k * [s, 1, 1] for k in boot], [one(f"Ankle_{side}"), blend(f"Ankle_{side}", f"Ball_{side}"), one(f"Ball_{side}")],
           soft_rings=(1,))


def pauldron(b: Builder) -> None:
    """Three lames over the left shoulder, each a curved plate from the front over the top to the back, stepping
    down the arm: the top one on the clavicle, the lowest on the upper arm."""
    lames = [(-0.18, -0.46, 1.86, 0.24, 0.27, one("Clavicle_L")),
             (-0.4, -0.62, 1.83, 0.26, 0.24, blend("Clavicle_L", "Shoulder_L")),
             (-0.56, -0.74, 1.8, 0.23, 0.2, one("Shoulder_L"))]
    th = 0.03
    for x_in, x_out, yc, r_in, r_out, skin in lames:
        sections = []
        for t in np.radians([20, 55, 90, 125, 160]):
            d = np.array([0.0, 0.65 * np.sin(t), np.cos(t)])  # flattened over the shoulder
            c_in, c_out = np.array([x_in, yc, -0.05]), np.array([x_out, yc, -0.05])
            sections.append([c_in + d * r_in, c_out + d * r_out, c_out + d * (r_out - th), c_in + d * (r_in - th)])
        b.loft(sections, [skin] * len(sections), soft_rings=range(1, len(sections) - 1), lean=UP)


def apron(b: Builder) -> None:
    """A loincloth hanging from the belt between the legs, its lower corners following the thighs."""
    front = np.array([(-0.17, 1.0, 0.33), (0.17, 1.0, 0.33), (0.13, 0.62, 0.27), (0.0, 0.55, 0.27), (-0.13, 0.62, 0.27)])
    back = front + [0, 0, -0.03]
    skins = [one("Hips"), one("Hips"), blend("Hips", "UpperLeg_R"), {"Hips": 0.5, "UpperLeg_L": 0.25, "UpperLeg_R": 0.25},
             blend("Hips", "UpperLeg_L")]
    pts = np.vstack([front, back])
    faces = [([0, 1, 2, 3, 4], front.mean(0) - [0, 0, 0.1]), ([5, 6, 7, 8, 9], back.mean(0) + [0, 0, 0.1])]
    c = pts.mean(0)
    for j in range(5):
        faces.append(([j, (j + 1) % 5, 5 + (j + 1) % 5, 5 + j], c))
    b.add(pts, faces, skins + skins, lean=UP)


def sword(b: Builder) -> None:
    """The greatsword, in its own frame: along it from the centre of the vendor's sword (a), across its flat (w),
    through it (c)."""
    centre = np.array([1.87, 1.832, 0.502])
    a = unit([0.433, 0.095, 0.896])
    c = unit([0.027, 0.993, -0.118])
    w = unit(np.cross(c, a))
    w, c = c, -w  # turned on its grip: the flat faces the camera as the sword hangs (edge-on it drew as a bright bar)

    def at(t, u, v):
        return centre + t * a + u * w + v * c

    skin = one(SWORD)
    # The grip ends just past the fist: a pommel above it drew a knot of short strokes as the sword hangs.
    grip = [ring(at(t, 0, 0), w, c, (0.035, 0.035), 4, np.pi / 4) for t in (-0.74, -0.36)]
    b.loft(grip, [skin] * 2, carried=True)
    guard = [np.array([at(-0.38, u, v) for u, v in ((-0.24, 0.05), (0.2, 0.05), (0.2, -0.05), (-0.24, -0.05))]),
             np.array([at(-0.28, u, v) for u, v in ((-0.28, 0.04), (0.24, 0.04), (0.24, -0.04), (-0.28, -0.04))])]
    b.loft(guard, [skin] * 2, carried=True)
    # The blade, a flat plate thinning to its edge: the back (w +) notched, the edge (w -), a clipped tip, a sixth
    # shorter and narrower than the vendor's so it doesn't outweigh the body. Its flats stay flat: folded along the
    # middle, the half turned away caught the rim and glowed.
    stations = [(-0.28, 0.1, -0.13), (0.08, 0.1, -0.135), (0.2, 0.02, -0.14), (0.28, 0.11, -0.145),
                (0.5, 0.12, -0.15), (0.62, 0.04, -0.155), (0.7, 0.13, -0.16), (0.93, 0.135, -0.16)]
    th = 0.035
    rings = [np.array([at(t, back, th), at(t, edge, 0.3 * th), at(t, edge, -0.3 * th), at(t, back, -th)])
             for t, back, edge in stations]
    rings.append(at(1.2, -0.1, 0))
    b.loft(rings, [skin] * len(rings), soft_rings=range(1, len(rings) - 1), carried=True)


def build(source: Source) -> Mesh:
    b = Builder(source)
    torso(b)
    strap(b)
    head(b)
    pauldron(b)
    apron(b)
    sword(b)
    for s, side in ((-1, "L"), (1, "R")):
        arm(b, s, side)
        hand(b, s, side)
        leg(b, s, side)
    return b.mesh()
