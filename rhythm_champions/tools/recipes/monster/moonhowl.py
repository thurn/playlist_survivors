"""Moonhowl (ps-8z4.15), Boss Rush boss 5, act 1's finale: the vendor's werewolf rebuilt whole as a few simple
faceted volumes, every edge it designs drawn, around what names it: a hunched wolfman with a wolf's head (a long
snout, a jaw that opens with the howl and the bites, tall pointed ears), hackles over its hump, long arms ending in
three long talons, digitigrade legs on paws, and a bushy tail to the ground.

The trailer's drawing of the vendor mesh (14,912 triangles) reads as a hunched long-armed beast at game size, but
its fur tufts and sculpted muscles draw short broken dashes all over it: the back and shoulders glow as lit patches,
the head is a small knot at the top of the figure, and in a crowd the werewolves merge into a field of dashes. Blind
critiques called it a ghoul or a zombie. So nothing of the vendor mesh is kept; every part is new, skinned to the
vendor bones, and every edge that draws is chosen here (a polygon's edges, never its triangles' diagonals, and only
some of those):

- The trunk: one eight-sided loft bent forward from narrow hips through a lean waist to a deep chest and a broad hump
  of shoulders, higher behind. Long lines along it (the sides, the belly's edges and the spine) and the shoulder
  blades' lines over the hump only, so the back has mass from the 60-degree camera (with the spine alone it was an
  empty gap between the arms) without breaking into slivers (a ring round the chest as well did).
- The head: 1.4 times the vendor's (it was lost over the hump), a skull wide behind narrowing to a long tapered
  snout, a slanted eye under the brow on each side, a nose at the tip, and the lower jaw a wedge of its own on the vendor's jaw bone, so the howl and the bites open it. The ears are tall flat
  pyramids standing up and out from the back of the skull: from behind they are a wolf's ears, as they should be.
- Hackles: three broad flat spikes round the back of the neck and three down the spine over the hump, each a closed
  pyramid (thin ones read as stray strokes), swept back. More round the neck, and fangs, knotted the head at game
  size.
- The arms: long and heavy, each one six-sided prism from the shoulder through the elbow to the wrist, a line round
  it only at the elbow; a palm block and three thick hooked talons, each a tapering four-sided prism through its
  finger's joints to a point past the vendor's claw (four thin ones, the thumb's too, were hairlines at game size).
- The legs: digitigrade, each one six-sided prism from the hip through the knee and the hock to the ankle; a wedge of
  a paw with three short claws.
- The tail: a six-sided spindle swelling through its middle to a point on the ground behind.

Every triangle has its own corners. The trunk, legs, hackles and tail lean their normals a little (0.4) along their
shape toward its root, which keeps their long facets dark in the rim. The head, hands and arms keep their own normals:
the clips raise the claws and tip the head back to howl, and normals leant up in the rest pose then pointed away from
the camera and the rim drew the raised hand and the howling head solid. The mesh flags its edges, so it is drawn as
designed at every distance rather than decimated: 750 triangles at every LOD, where the vendor's are 14,912.

Source: Heroic Fantasy Creatures Full Pack Vol 1's Werewolf.prefab (sha256
2c74a5dc348ad85314b946f8edba76230cf857b3ada7074215e4c1cdd0201490) and SK_Werewolf.FBX
(47a958af7bb693eb149c1e0da9d881e595eca5107e8f1d76f8933e3d3ab5ad6b), as manifest.json lists them. Only its rig is
used, and its rest pose and skin weights to place and skin the shapes: each shape takes the weights of the vendor's
nearest vertices over its own bones (never across limbs or sides); the head is rigid on the head bone, the jaw on
the jaw bone. No Blender: numpy only. Bake: `just champions-tool monster bake moonhowl`; review: `just champions-tool
monster review moonhowl` (and `--look trailer`).
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

X, _, Z = np.eye(3)


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv), the first at u turned by phase."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre, np.float64) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


UP = (0.0, 1.0, 0.0)  # a lean up the trunk, the legs and the head
LEAN = 0.4  # how far the shading normals lean (Builder.mesh)


class Builder:
    """Closed shapes of convex polygons, each wound outward; every polygon's outline draws unless the edge is hushed.
    Each shape is skinned rigidly to one bone, or by transfer from the vendor vertices near it over a set of bones."""

    def __init__(self, source: Source):
        self.source = source
        self.points: list[np.ndarray] = []
        self.skins: list[object] = []
        self.carried: list[bool] = []
        self.polys: list[list[int]] = []
        self.leans: list[np.ndarray] = []
        self.quiet: set[tuple[int, int]] = set()

    def shape(self, points, faces, skin, carried: bool = False, inside=None, lean=(0, 0, 0)) -> list[int]:
        """A closed shape: points (n, 3), faces (convex polygons of point indices) wound outward from `inside` (the
        shape's centroid, or one point per face); `skin` a bone name or a tuple of bone names to transfer over; `lean`
        a vector its shading normals lean along (see mesh)."""
        pts = np.asarray(points, np.float64)
        first = len(self.points)
        for p in pts:
            self.points.append(p)
            self.skins.append(skin)
            self.carried.append(carried)
        centre = pts.mean(axis=0)
        for i, f in enumerate(faces):
            ref = centre if inside is None else np.asarray(inside[i] if np.ndim(inside) == 2 else inside)
            self.face([first + k for k in f], ref, lean)
        return list(range(first, first + len(pts)))

    def face(self, poly, inside, lean=(0, 0, 0)) -> None:
        """A convex polygon of existing points, wound outward from the point `inside`."""
        corners = np.array([self.points[k] for k in poly])
        normal = np.cross(corners, np.roll(corners, -1, axis=0)).sum(axis=0)  # Newell's, as wound
        if normal @ (corners.mean(axis=0) - np.asarray(inside, np.float64)) < 0:
            poly = list(reversed(poly))
        self.polys.append(list(poly))
        self.leans.append(np.asarray(lean, np.float64))

    def loft(self, rings, skin, carried: bool = False, soft_rings=(), soft_sides=(), lean=(0, 0, 0)) -> list[list[int]]:
        """A shape lofted through rings of equal count (a ring may be one point, an apex), capped at both ends; each
        side face is wound outward from the axis between its two rings' centres. The edges round the rings in
        `soft_rings` and those along the shape from the ring corners in `soft_sides` don't draw. Returns each ring's
        point indices."""
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
        ids = self.shape(pts, faces, skin, carried, np.array(inside), lean)
        out = [ids[o:o + len(r)] for o, r in zip(offsets, rings)]
        for i in soft_rings:
            for k in range(len(out[i])):
                self.hush(out[i][k], out[i][(k + 1) % len(out[i])])
        for k in soft_sides:
            for r0, r1 in pairwise(out):
                if len(r0) > 1 and len(r1) > 1:
                    self.hush(r0[k], r1[k])
        return out

    def plate(self, outline, normal, depth: float, skin) -> None:
        """A thin plate: the convex outline (n, 3) on top, the same `depth` behind along -normal; only the top
        outline draws, so it reads as one shape rather than a doubled rim. Only its top's normals lean up: a closed
        plate leant all round drew solid when its shard turned over."""
        top = np.asarray(outline, np.float64)
        bottom = top - unit(normal) * depth
        low, high = self.loft([bottom, top], skin)
        self.leans[-1] = np.asarray(UP)  # the top cap, the loft's last face
        for i in range(len(top)):
            self.hush(low[i], low[(i + 1) % len(top)])
            self.hush(low[i], high[i])

    def hush(self, a: int, b: int) -> None:
        self.quiet.add((min(a, b), max(a, b)))

    def mesh(self) -> Mesh:
        """Each polygon as a fan whose triangles draw only the polygon's outline; every triangle its own corners,
        their normal its facet's leaning along its shape's lean: up the trunk, up a limb toward its root, which is up
        in the world while it hangs. The rim mixes the normal with the facet's own, so the long facets turned across
        the view on the body or a hanging arm stay dark rather than glowing whole; a face turned against the lean
        keeps its own normal."""
        def key(a: int, b: int) -> tuple[int, int]:
            return min(a, b), max(a, b)

        pts = np.array(self.points)
        tris, flags, leans = [], [], []
        for poly, lean in zip(self.polys, self.leans):
            n = len(poly)
            outline = {key(poly[i], poly[(i + 1) % n]) for i in range(n)} - self.quiet
            for i in range(1, n - 1):
                t = (poly[0], poly[i], poly[i + 1])
                tris.append(t)
                flags.append([int(key(t[1], t[2]) in outline), int(key(t[2], t[0]) in outline),
                              int(key(t[0], t[1]) in outline)])
                leans.append(lean)
        tris, leans = np.array(tris), np.array(leans)
        p = pts[tris]
        normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        facing = np.einsum("ij,ij->i", normal, leans) >= -0.5 * np.linalg.norm(leans, axis=1)
        normal += LEAN * leans * facing[:, None]
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        bones, weights = self.skin()
        corners = tris.reshape(-1)
        return Mesh(pts[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    bones[corners], weights[corners], np.repeat(normal, 3, axis=0).astype(np.float32),
                    np.array(self.carried)[corners], np.array(flags, np.uint8))

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


def eye(b: Builder, face, bone_name: str) -> None:
    """A thin slanted triangle proud of one half of the face, its outer corner high: a scowl. `face` is that half's
    corners: the brow's middle and outer corner, then the snout's."""
    brow, outer, snout, snout_outer = face
    normal = unit(np.cross(outer - brow, snout - brow))
    if normal[2] < 0:
        normal = -normal

    def on(across: float, down: float) -> np.ndarray:
        top, bottom = brow + (outer - brow) * across, snout + (snout_outer - snout) * across
        return top + (bottom - top) * down + normal * 0.01

    b.plate([on(0.3, 0.3), on(0.85, 0.12), on(0.75, 0.35)], normal, 0.01, bone_name)


def limb(b: Builder, joints, radii, bones, lean, n=6, ref=Z, soft_rings=()) -> list[list[int]]:
    """One n-sided prism through all of a limb's joints (separate segments read as a kit of boxes), a corner toward
    `ref`, its radius (or radii) at each joint, each ring square to the mean of the segments either side; skinned
    over `bones`, its normals leant along `lean`."""
    joints = [np.asarray(j, np.float64) for j in joints]
    segments = [unit(c - a) for a, c in pairwise(joints)]
    rings = []
    for i, (j, r) in enumerate(zip(joints, radii)):
        d = unit(segments[max(i - 1, 0)] + segments[min(i, len(segments) - 1)])
        u = unit(ref - (ref @ d) * d)
        ru, rv = (r, r) if np.isscalar(r) else r
        rings.append(ring(j, u, np.cross(d, u), (ru, rv), n))
    return b.loft(rings, tuple(bones), soft_rings=soft_rings, lean=lean)




def bone(name: str) -> str:
    return "WEREWOLF_ " + name


TRUNK = tuple(bone(n) for n in ("Pelvis", "Spine", "Spine1", "Neck", "L Clavicle", "R Clavicle"))
# A trunk ring's eight corners, round from the right side (0): the right belly edge, the front's middle, the left belly
# edge, the left side (4), the left shoulder blade, the spine (6), the right shoulder blade.
DRAWN = {0, 1, 3, 4, 6}  # the lines along it: the sides, the belly's edges and the spine


def section(y, zc, rx, front, back, lat=50):
    """A ring of the trunk at height y, x by rx, forward to zc + front and back to zc - back, its shoulder blades'
    corners `lat` degrees below the sides."""
    t = np.radians([0, 50, 90, 130, 180, 180 + lat, 270, 360 - lat])
    c, s = np.cos(t), np.sin(t)
    z = np.where(s > 0, front * s, back * s)
    return np.stack([rx * c, np.full(len(t), y), zc + z], axis=1)


def torso(b: Builder) -> list[list[int]]:
    """The trunk, bent forward from narrow hips through a lean waist to a deep chest and a broad hump of shoulders:
    long lines along it (the sides, the belly's edges and the spine) and the shoulder blades' lines over the hump
    only (from above, the back was an empty gap between the arms)."""
    rings = [section(1.22, -0.46, 0.25, 0.2, 0.22),
             section(1.5, -0.45, 0.42, 0.36, 0.36),
             section(1.88, -0.36, 0.37, 0.3, 0.3),
             section(2.28, -0.18, 0.55, 0.48, 0.46),
             section(2.62, -0.08, 0.66, 0.5, 0.48),
             section(2.9, 0.06, 0.58, 0.48, 0.44),
             section(3.08, 0.22, 0.3, 0.3, 0.28)]
    rings[-1][:, 1] += 0.12 * (-(rings[-1][:, 2] - 0.22) / 0.28).clip(0)  # higher behind: the hump
    ids = b.loft(rings, TRUNK, soft_rings=(1, 2, 4, 5), soft_sides=set(range(8)) - DRAWN - {5, 7}, lean=UP)
    for r0, r1 in pairwise(ids[:4]):  # the shoulder blades' lines run only over the hump
        for k in (5, 7):
            b.hush(r0[k], r1[k])
    return ids


HEAD_SCALE = 1.4  # about its middle: the vendor's head is lost over the hump
HEAD_MID = np.array([0.0, 3.15, 0.6])
HEAD_SHIFT = np.array([0.0, 0.04, 0.12])


def at(points) -> np.ndarray:
    return HEAD_MID + (np.asarray(points, np.float64) - HEAD_MID) * HEAD_SCALE + HEAD_SHIFT


def head(b: Builder) -> None:
    """A wolf's head: a broad skull narrowing to a long snout, the brow over the eyes, a nose at its tip; tall pointed
    ears standing up and out from the back of the skull; a lower jaw of its own on the jaw bone, so the howl and the
    bites open it."""
    hb = bone("Head")

    def sec(z, crest, upper, cheek, jaw):
        right = [upper, cheek, jaw]
        left = [(-x, y) for x, y in reversed(right)]
        return at([(x, y, z) for x, y in [(0, crest), *right, *left]])

    rings = [sec(0.28, 3.3, (0.16, 3.28), (0.22, 3.14), (0.14, 3.0)),
             sec(0.48, 3.4, (0.2, 3.36), (0.26, 3.16), (0.18, 2.98)),
             sec(0.62, 3.33, (0.13, 3.3), (0.19, 3.12), (0.14, 2.99)),
             sec(0.96, 3.16, (0.055, 3.14), (0.085, 3.06), (0.065, 3.0))]
    ids = b.loft(rings, hb, soft_sides=(0,))
    for side in (1, 6):  # a slanted eye under the brow, on each half of the face
        face = [np.array(b.points[ids[i][k]]) for i, k in ((1, 0), (1, side), (2, 0), (2, side))]
        eye(b, face, hb)
    # the nose: a small block over the snout's tip
    nose = at([(0.0, 3.2, 0.9), (0.06, 3.17, 0.95), (-0.06, 3.17, 0.95), (0.0, 3.13, 1.0)])
    b.shape(nose, [[0, 1, 3], [0, 3, 2], [0, 2, 1], [1, 2, 3]], hb)
    for s in (-1, 1):
        # an ear: a tall flat pyramid standing up and out from the back of the skull, its open face forward
        base = at([(s * 0.08, 3.37, 0.44), (s * 0.26, 3.29, 0.46), (s * 0.18, 3.33, 0.28)])
        tip = at([(s * 0.32, 3.7, 0.25)])
        b.shape(np.vstack([base, tip]), [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], hb)
    jaw(b)


def jaw(b: Builder) -> None:
    """The lower jaw, a long wedge from under the cheeks to the chin, rigid on the jaw bone."""
    jb = bone("Queue de cheval 1")
    rings = [at([(0.15, 3.0, 0.5), (0.13, 2.86, 0.52), (-0.13, 2.86, 0.52), (-0.15, 3.0, 0.5)]),
             at([(0.07, 2.97, 0.92), (0.05, 2.88, 0.9), (-0.05, 2.88, 0.9), (-0.07, 2.97, 0.92)])]
    b.loft(rings, jb)


def mane(b: Builder) -> None:
    """Hackles: a ruff of big flat spikes round the back of the neck and a row of them down the spine over the hump,
    swept back, so the hunched back reads as a wolf's from the 60-degree camera."""
    spine = (bone("Spine1"), bone("Neck"), bone("Neck1"))
    tufts = [  # (root on the back, length, side tilt)
        ((0.0, 3.18, 0.0), 0.42, 0.0),
        ((0.22, 3.12, 0.05), 0.36, 0.5),
        ((-0.22, 3.12, 0.05), 0.36, -0.5),
        ((0.0, 2.94, -0.32), 0.34, 0.0),
        ((0.0, 2.62, -0.52), 0.3, 0.0),
        ((0.0, 2.3, -0.6), 0.24, 0.0),
    ]
    for root, length, tilt in tufts:
        root = np.asarray(root, np.float64)
        out = unit(np.array([tilt, 1.0, -1.1]))
        across = unit(np.cross(out, (0.0, 0.0, 1.0)) if abs(tilt) > 0.6 else np.cross(out, X))
        w = 0.19 * length / 0.36
        base = [root + across * w, root - across * w, root - out * 0.06 + np.cross(across, out) * 0.11]
        b.shape(np.vstack([base, root + out * length]), [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], spine, lean=UP)


def arms(b: Builder) -> None:
    """Long heavy arms reaching forward, each one six-sided prism from the shoulder through the elbow to the wrist,
    a big deltoid and a swelling forearm, a line round it only at the elbow; a palm block and three thick hooked
    talons, each one tapering prism to a point."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = [bone(f"{side} {n}") for n in ("Clavicle", "UpperArm", "Forearm", "Hand")]
        joints = [(s * 0.36, 2.84, -0.14), (s * 0.7, 2.8, -0.05), (s * 1.02, 2.5, 0.0), (s * 1.34, 2.16, 0.07),
                  (s * 1.5, 1.88, 0.42), (s * 1.66, 1.58, 0.76)]
        limb(b, joints, [0.22, 0.25, 0.2, 0.15, 0.17, 0.12], bones, (0, 0, 0), soft_rings=(1, 2, 4))
        hand(b, s, side)


# each finger's joints from the hand to the talon's tip (rest pose, the right hand; the left mirrors it)
FINGERS = {
    "1": [(1.69, 1.36, 1.15), (1.71, 1.3, 1.29), (1.7, 1.18, 1.4), (1.68, 0.9, 1.46)],
    "2": [(1.82, 1.35, 1.13), (1.86, 1.27, 1.26), (1.88, 1.16, 1.34), (1.86, 0.88, 1.38)],
    "3": [(1.93, 1.36, 1.05), (1.98, 1.3, 1.12), (2.01, 1.2, 1.18), (2.05, 0.96, 1.24)],
}


def hand(b: Builder, s: int, side: str) -> None:
    hb = bone(f"{side} Hand")
    m = np.array([s, 1, 1])
    wrist = np.array([1.66, 1.58, 0.76]) * m
    knuckles = np.array([1.76, 1.36, 1.1]) * m
    across = unit((np.array(FINGERS["3"][0]) - np.array(FINGERS["1"][0])) * m)
    d = unit(knuckles - wrist)
    up = unit(np.cross(across, d)) * (1 if s > 0 else -1)
    def box(c, w, t):
        return [c + across * w + up * t, c - across * w + up * t, c - across * w - up * t, c + across * w - up * t]
    b.loft([box(wrist, 0.13, 0.1), box(wrist + d * 0.12, 0.2, 0.13), box(knuckles, 0.22, 0.11)], (hb,))
    for f, joints in FINGERS.items():
        names = [f"{side} Finger{f}", f"{side} Finger{f}1", f"{side} Finger{f}2"]
        pts = [np.asarray(j) * m for j in joints]
        tip = pts[-1] + unit(pts[-1] - pts[-2]) * 0.16  # long talons
        r = 0.095
        rings = []
        for i, p in enumerate(pts[:-1]):
            dd = unit(pts[i + 1] - p)
            u = unit(up - (up @ dd) * dd)
            rings.append(ring(p, u, np.cross(dd, u), (r * (1 - 0.18 * i), r * (1 - 0.18 * i)), 4, np.pi / 4))
        b.loft(rings + [tip], (hb, *(bone(n) for n in names)), soft_rings=(1,))


def legs(b: Builder) -> None:
    """Digitigrade legs: a thick thigh to the knee, a lean shin back to the hock, the long rising foot down to the
    ball, each one six-sided prism through all its joints; a wedge of a paw with three short claws."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = tuple(bone(f"{side} {n}") for n in ("Thigh", "Calf", "HorseLink", "Foot")) + (bone("Pelvis"),)
        joints = [(s * 0.3, 1.5, -0.4), (s * 0.45, 1.15, -0.27), (s * 0.57, 0.83, -0.15), (s * 0.64, 0.49, -0.32),
                  (s * 0.7, 0.19, -0.2)]
        limb(b, joints, [0.26, 0.25, 0.16, 0.11, 0.1], bones, UP, soft_rings=(1, 3))
        fb = (bone(f"{side} Foot"), bone(f"{side} Toe0"))
        heel, ball = np.array([s * 0.7, 0.19, -0.2]), np.array([s * 0.8, 0.06, 0.24])
        pts = [heel + (s * dx, dy, dz) for dx, dy, dz in ((0.09, 0.06, 0.0), (-0.09, 0.06, 0.0), (-0.09, -0.12, -0.04),
                                                           (0.09, -0.12, -0.04))]
        pts += [ball + (s * dx, dy, dz) for dx, dy, dz in ((0.15, 0.05, 0.0), (-0.15, 0.05, 0.0), (-0.15, -0.06, 0.0),
                                                           (0.15, -0.06, 0.0))]
        b.shape(pts, [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]], fb, lean=UP)
        for dx in (-0.1, 0.0, 0.1):
            root = ball + (s * dx, 0.0, 0.0)
            b.loft([ring(root, X, (0, 1, 0), (0.035, 0.035), 3, np.pi / 2), root + (0, -0.05, 0.14)],
                   (bone(f"{side} Toe0"),), lean=UP)


TAIL = [(0.0, 1.55, -0.72), (0.0, 1.39, -0.9), (0.0, 1.08, -1.1), (0.0, 0.76, -1.34), (0.0, 0.4, -1.56)]


def tail(b: Builder) -> None:
    """A bushy tail hanging behind to the ground: a six-sided spindle swelling through its middle to a point."""
    bones = tuple(bone(n) for n in ("Pelvis", "Tail", "Tail1", "Tail2", "Tail3", "Tail4"))
    tip = np.array([0.0, 0.16, -1.8])
    pts = [np.asarray(p, np.float64) for p in TAIL] + [tip]
    rings = []
    for i, (p, r) in enumerate(zip(pts[:-1], (0.1, 0.16, 0.22, 0.21, 0.14))):
        d = unit(pts[i + 1] - p)
        rings.append(ring(p, X, np.cross(d, X), (r, r), 6))
    b.loft(rings + [tip], bones, soft_rings=(1, 3), lean=unit((0, 0.6, 0.8)))


def build(source: Source) -> Mesh:
    b = Builder(source)
    torso(b)
    head(b)
    mane(b)
    arms(b)
    legs(b)
    tail(b)
    return b.mesh()
