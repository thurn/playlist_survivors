"""The troll (ps-6y2.38): the vendor's hunched brute and its spiked club rebuilt whole as a few simple faceted volumes.

The trailer's drawing of the vendor mesh reads by its bulk and its club, and up close as a man in armour: the box of
its brow, its small eyes, ears and tusks and a necklace of teeth knot into a round helmet with a grille at the top of
the figure, and the wraps round its arms and legs, the belt of ropes and pouches and the club's iron bands and grain
draw parallel hatching. At game size it is a dense gold scribble, never dark inside, and its shatter flies as curls.
Blind critiques called it a diver, a knight or a mech. What names the troll (the huge hunched bulk, the head slung
low and forward, the very long heavy arms, the tusks and the great spiked club) is in it but buried. So nothing of
the vendor mesh is kept; every part is new, skinned to the vendor bones, and every edge that draws is chosen here (a
polygon's edges, never its triangles' diagonals, and only some of those):

- The trunk: hips, a gut and a hump of shoulders rising behind the neck to a small crest, so the head hangs low in
  front of it. Behind, a few big planes: a spine ridge from the small of the back up to the crest, one broad plane
  each side of it running down to the shoulder, and the lower back under one line across it; in front,
  the belly's edges up to a line across the chest, and one across the gut. Lines over the shoulder blades, round the
  hump and up its front tiled it into a tortoise's shell, and rings round a dome drew a target.
- The head: 1.45 times the vendor's, sunk 0.26 m and thrust 0.38 m forward, clear of the shoulders: the hunch the
  clips (the big ork's) don't have. One wedge from a narrow nape to a wide jaw, an ape's from above or behind, a line
  round its crown, a heavy brow dipping in a scowl, slanted eyes, a blunt nose and two short thick tusks up beside
  the snout. From behind, the 60-degree camera sees the face above the skull: ears, and tusks reaching past the
  snout's top, read there as horns, a crown or a cat's ears, so the ears are dropped and the tusks stay inside the
  snout's outline.
- The arms: very long and heavy, each one six-sided prism from the shoulder to the wrist, tapering to the elbow and
  swelling in the forearm, a line round it only at the elbow and the wrist; a big fist of one block with one line
  round it at the knuckles.
- The legs: short and thick, each one six-sided prism from the hip to the ankle with a line round it only there, and
  a wide flat foot joined to it (with its back drawn, a loose box). A belt under the gut.
- Dropped: the wraps, the necklace, the ropes and pouches, the ears, the toes, and the loincloth (an empty slab
  under the belly at game size).
- The club, on the vendor's axis, a little shorter: a thick six-sided head with four big spikes swept a little toward
  its end (radial, the one pointing at the camera drew an X), a thick shaft nearly parallel down to the fist
  (tapering to the grip, its long lines met there like a searchlight's beam), a grip and a pommel. Carried, so it
  shatters with the body.

Every triangle has its own corners and its facet's normal leant along its shape toward its root, as the big ork's
are (up the trunk, the head and the legs; toward the shoulder along each arm, which is up while it hangs): the rim no
longer lights the long facets of the trunk and the hanging arms as bands, and a face turned against the lean keeps
its own. The mesh flags its edges, so it is drawn as designed at every distance rather than decimated: 673 triangles
at every LOD, where the vendor's are 5,220, 2,686 and 1,344.

Beside the big ork, its sibling in the counter family, it reads apart by shape: the ork stands upright, horned, a
strap and a pauldron on it and a long notched cleaver in its hand; the troll is a bare boulder of a back with its
head low in front of it and a spiked club.

Source: Synty's Character_BR_Troll_01.prefab and SM_Wep_Troll_01.prefab from POLYGON Fantasy Rivals (package sha256
f5b2197465eb4fcd0ce06cec1faa2790523a8a1ae8a669c1ef9a0b0316919e6c), as manifest.json lists them. Only its rig is used,
and its rest pose and skin weights to place and skin the shapes: each shape takes the weights of the vendor's nearest
vertices over its own bones (never across limbs or sides); the head is rigid on the head bone, the club on the
weapon's. No Blender: numpy only. Bake: `just champions-tool monster bake troll`; review: `just champions-tool monster
review troll` (and `--look trailer`).
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
LEAN = 0.8  # how far the shading normals lean (Builder.mesh)


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


TRUNK = ("Hips", "Spine_01", "Spine_02", "Spine_03", "Clavicle_L", "Clavicle_R")
# A trunk ring's eight corners, round from the right side (0): the right belly edge, the front's middle, the left belly
# edge, the left side (4), the left shoulder blade, the spine (6), the right shoulder blade.
DRAWN = {0, 1, 3, 4, 6}  # the lines along it: the sides, the belly's edges and the spine
FRONT = ((0, 1), (1, 2), (2, 3), (3, 4))  # a ring's edges over the front, from side to side
BACK = ((4, 5), (5, 6), (6, 7), (7, 0))


def section(y, zc, rx, front, back, lat):
    """A ring of the trunk at height y, x by rx, forward to zc + front and back to zc - back, its shoulder blades'
    corners `lat` degrees below the sides."""
    t = np.radians([0, 50, 90, 130, 180, 180 + lat, 270, 360 - lat])
    c, s = np.cos(t), np.sin(t)
    z = np.where(s > 0, front * s, back * s)
    return np.stack([rx * c, np.full(len(t), y), zc + z], axis=1)


def torso(b: Builder) -> None:
    """The trunk from the crotch to a hump of shoulders: wide hips, a gut, and the hump's top tilted up behind, so the
    head hangs low in front of it. Behind, a few big planes: a spine ridge from the small of the back up to the
    hump's small crest, a broad plane each side of it out to the side, and the lower back under a line across it
    (lines over the shoulder blades and round the hump tiled it into a tortoise's shell; with no line round the
    crest, the ridge stood up from an open outline like an antenna, and a ridge across the crest drew a small skull
    on top of the hump). In front, the belly's edges up to a line across the chest, and one across the gut."""
    top = section(2.36, -0.17, 0.22, 0.1, 0.2, 55)
    top[:, 1] += 0.14 * (-(top[:, 2] + 0.17) / 0.2)  # higher behind: the hump
    rings = [section(0.98, -0.02, 0.22, 0.14, 0.16, 80),
             section(1.1, -0.02, 0.41, 0.3, 0.3, 75),
             section(1.32, 0.0, 0.48, 0.4, 0.32, 62),
             section(1.62, -0.02, 0.43, 0.33, 0.35, 42),
             section(2.04, -0.08, 0.45, 0.3, 0.44, 22),
             top]
    ids = b.loft(rings, TRUNK, soft_sides=set(range(8)) - DRAWN, lean=UP)
    for i, r in enumerate(ids):
        across = {1: FRONT + BACK, 3: FRONT + BACK, 4: BACK}.get(i, ())
        for j, k in across:
            b.hush(r[j], r[k])
    for k in (1, 3):  # the belly's edges end at the chest's line: above it they tiled the hump's front
        b.hush(ids[4][k], ids[5][k])


HEAD_DROP = np.array([0.0, -0.26, 0.38])  # the head sunk and thrust forward from the vendor's: the hunch
HEAD_SCALE = 1.45  # about its middle
HEAD_MID = np.array([0.0, 2.4, 0.15])


def head(b: Builder) -> None:
    """A big low head in one wedge, narrow at the nape and wide at the jaw, an ape's seen from above or behind: the
    skull rising to a crown, a heavy brow over the face dipping in a scowl, the cheeks and a jutting jaw; slanted eyes
    and a big nose on the face, and two stubby tusks up from the jaw's corners, kept below the brow and close to the
    snout (tusks rising over the skull or splayed out read as horns, or a cat's ears from behind). Drawn: the brow,
    the cheeks, the jaw's edges, and its outline at the nape and the snout (with the snout's open it read as a fist
    or a lump under the hump)."""
    hb = "Head"

    def at(points):
        return HEAD_MID + (np.asarray(points, np.float64) - HEAD_MID) * HEAD_SCALE + HEAD_DROP

    def sec(z, crest, upper, cheek, jaw):
        """Seven corners round a section: the crest, then each side's upper corner, cheek and jaw, (x, y) each."""
        right = [upper, cheek, jaw]
        left = [(-x, y) for x, y in reversed(right)]
        return at([(x, y, z) for x, y in [(0, crest), *right, *left]])

    rings = [sec(-0.06, 2.4, (0.06, 2.38), (0.09, 2.32), (0.07, 2.26)),
             sec(0.1, 2.58, (0.13, 2.54), (0.18, 2.42), (0.16, 2.23)),
             sec(0.3, 2.49, (0.19, 2.53), (0.23, 2.4), (0.24, 2.2)),
             sec(0.31, 2.41, (0.13, 2.44), (0.22, 2.37), (0.24, 2.2)),
             sec(0.46, 2.38, (0.13, 2.36), (0.2, 2.31), (0.23, 2.2))]
    ids = b.loft(rings, hb, soft_rings=(3,), soft_sides=(0, 1, 6), lean=UP)
    for side in (1, 6):  # a slanted eye under the brow, on each half of the face
        face = [np.array(b.points[ids[i][k]]) for i, k in ((3, 0), (3, side), (4, 0), (4, side))]
        eye(b, face, hb)
    nose = at([(0.0, 2.45, 0.37), (0.09, 2.36, 0.42), (-0.09, 2.36, 0.42), (0.05, 2.33, 0.53), (-0.05, 2.33, 0.53)])
    b.shape(nose, [[0, 1, 3, 4, 2], [0, 1, 3], [0, 2, 4], [1, 2, 4, 3]], hb, lean=UP)
    for s in (-1, 1):
        path = at([(s * 0.15, 2.22, 0.47), (s * 0.15, 2.29, 0.49), (s * 0.15, 2.34, 0.48)])
        b.loft([ring(path[0], X, Z, (0.06, 0.05), 4, np.pi / 4), ring(path[1], X, Z, (0.042, 0.035), 4, np.pi / 4),
                path[2]], hb, lean=UP)


def eye(b: Builder, face, bone: str) -> None:
    """A thin slanted triangle proud of one half of the face, its outer corner high: a scowl. `face` is that half's
    corners: the brow's middle and outer corner, then the snout's."""
    brow, outer, snout, snout_outer = face
    normal = unit(np.cross(outer - brow, snout - brow))
    if normal[2] < 0:
        normal = -normal

    def on(across: float, down: float) -> np.ndarray:
        top, bottom = brow + (outer - brow) * across, snout + (snout_outer - snout) * across
        return top + (bottom - top) * down + normal * 0.01

    b.plate([on(0.3, 0.3), on(0.85, 0.12), on(0.75, 0.35)], normal, 0.01, bone)


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


def arms(b: Builder) -> None:
    """Long heavy arms, each one six-sided prism from inside the shoulder to the wrist, tapering to the elbow and
    swelling in the forearm, a line round it only at the elbow and the wrist; a big fist of one block. Their normals
    lean toward the shoulder, which is up while the arm hangs."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = [f"Clavicle_{side}", f"Shoulder_{side}", f"Elbow_{side}", f"Hand_{side}"]
        joints = [(s * 0.3, 2.02, -0.05), (s * 0.5, 2.0, -0.04), (s * 1.02, 1.96, -0.04), (s * 1.3, 1.97, -0.03),
                  (s * 1.58, 1.97, -0.03)]
        limb(b, joints, [0.2, 0.19, 0.15, 0.18, 0.13], bones, (-s, 0, 0), soft_rings=(1, 3))
        # the fist: a big block, one line round it at the knuckles
        fist = [(s * 1.56, 1.97, -0.02), (s * 1.74, 1.96, -0.01), (s * 1.93, 1.95, 0.0), (s * 2.04, 1.94, 0.0)]
        limb(b, fist, [(0.13, 0.15), (0.18, 0.21), (0.17, 0.2), (0.11, 0.14)], (f"Hand_{side}",), (-s, 0, 0),
             soft_rings=(1,))


def legs(b: Builder) -> None:
    """Short thick legs, each one six-sided prism from the hip through the knee and the calf to the ankle, and a wide
    flat foot."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = (f"UpperLeg_{side}", f"LowerLeg_{side}", f"Ankle_{side}", "Hips")
        joints = [(s * 0.24, 1.15, 0.0), (s * 0.24, 0.62, 0.0), (s * 0.26, 0.38, -0.08), (s * 0.27, 0.17, -0.06)]
        limb(b, joints, [0.25, 0.18, 0.19, 0.13], bones, UP, soft_rings=(1, 2))
        # the foot: a wide wedge from the heel to the toes
        pts = [(s * 0.27 + dx, y, z) for dx in (-0.16, 0.16) for y, z in ((0.0, -0.22), (0.0, 0.27), (0.21, -0.18),
                                                                         (0.09, 0.23))]
        faces = [[0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]]
        foot = b.shape(pts, faces, (f"Ankle_{side}", f"Ball_{side}"), lean=UP)
        for i, j in ((2, 6), (0, 2), (4, 6)):  # its back, under the ankle, joins the leg: drawn, it was a loose box
            b.hush(foot[i], foot[j])


def belt(b: Builder) -> None:
    """A belt round the hips, under the gut (a loincloth hanging from it read as a loose empty slab)."""
    b.loft([section(1.06, -0.02, 0.43, 0.32, 0.32, 75), section(1.16, -0.02, 0.45, 0.35, 0.32, 75)], ("Hips",),
           lean=UP)


SPIKE_SWEEP = np.radians(20)


def club(b: Builder, source: Source) -> None:
    """The club on the vendor's axis and length, a little shorter at its head: a thick six-sided head with four big
    spikes, a thick shaft nearly parallel down to the fist (tapering to the grip, its long lines converged like a
    searchlight's beam), a grip through the fist and a pommel. Carried, so it shatters with the body."""
    p = source.part("SM_Wep_Troll_01")
    q = source.positions[p.first:p.first + p.count].astype(np.float64)
    mid = q.mean(axis=0)
    _, _, axes = np.linalg.svd(q - mid)
    along = (q - mid) @ axes[0]
    if along.max() < -along.min():
        axes[0], along = -axes[0], -along
    u = axes[0]  # toward the pommel
    w = unit(axes[1])
    bone = "SM_Wep_Troll_01"

    def at(s):
        return mid + u * s

    def hexa(s, r):
        return ring(at(s), w, np.cross(u, w), (r, r), 6)

    head, pommel = along.min() + 0.1, along.max()  # the vendor's fist holds it at 0.96
    rings = [hexa(head, 0.19), hexa(head + 0.08, 0.27), hexa(head + 0.42, 0.25), hexa(head + 0.55, 0.15),
             hexa(0.82, 0.12), hexa(0.84, 0.085), hexa(pommel - 0.06, 0.085)]
    b.loft(rings, bone, carried=True, soft_rings=(2,))
    b.loft([hexa(pommel - 0.08, 0.115), hexa(pommel, 0.1)], bone, carried=True)
    # four thick spikes round the head, swept toward its end (radial, the one pointing at the camera drew an X)
    c = at(head + 0.25)
    for k in range(4):
        t = np.pi / 4 + k * np.pi / 2
        out = unit(np.cos(t) * w + np.sin(t) * np.cross(u, w))
        base = c + out * 0.22
        tip = base + unit(out * np.cos(SPIKE_SWEEP) - u * np.sin(SPIKE_SWEEP)) * 0.22
        rings = [ring(base, u, np.cross(out, u), (0.13, 0.13), 4, np.pi / 4), tip]
        b.loft(rings, bone, carried=True)


def build(source: Source) -> Mesh:
    b = Builder(source)
    torso(b)
    head(b)
    arms(b)
    legs(b)
    belt(b)
    club(b, source)
    return b.mesh()
