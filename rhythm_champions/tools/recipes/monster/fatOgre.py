"""The Fat Ogre (ps-6y2.39): the vendor's fat ogre rebuilt whole as a few simple faceted shapes, every line chosen: a
big gut, a small head sunk between massive shoulders, long heavy arms, stumpy legs and a spiked mace.

The trailer's drawing of the vendor mesh is a colouring-book contour of a smooth sculpt (ps-6y2.29's critique): its
18,897 triangles of muscle, belly folds and fat rolls crease near 24 degrees, so its lines break into dashes over the
body and the face and its facets never close; a haze of rim fills the shoulders and the gut; the mace head is a knot
of rope and stone; and its shatter throws curled, rounded scraps. What names it reads anyway, as an ogre at once: the
gut, the strap across the chest, the bald head slung low with its grimace, the mace. So nothing of the vendor mesh is
kept; every part is new, skinned to the vendor bones:

- The body: one ten-sided shape from under the gut to the neck, the gut wider and further forward than the vendor's
  and sagging over the crotch, the hunched back and the shoulders sloping up to the neck above it. Drawn by its long
  lines and two rings only: the fold under the chest, in front (the gut's top; its bottom is the outline), and a
  collar round the neck, which closes the long lines over the hump (without it they ended in mid-air, rays round the
  head). More rings read as a barrel, a turtle shell or a stack of tyres; the gut's widest ring as a crate's bottom.
- A broad strap from the right shoulder across the chest and the gut to the left hip and round the back, where a
  tilted plane cuts the body: two lines, the vendor's harness simplified to what names it.
- The head: a skull a fifth larger than the vendor's, sunk between the shoulders, a dark dome behind a scowling brow
  with one crest across it from side to side (more lines on the pate read as a visor, a pyramid or a helmet, none
  as a hole framed by the collar); a face sloping back 32 degrees from the muzzle to
  the brow, so it shows from in front and not from behind, with two slanted eye slits and a nose; an underbite jaw
  wider than the skull, jutting past the muzzle, with two tusks; big drooping ears, leaves facing up, which show from
  every side.
- The arms: one six-sided loft each through the vendor's joints to a big fist, heavy at the shoulder and the forearm,
  lines round it only at the elbow and the wrist. The legs: one loft each, a trouser hem flaring at the shin, and big
  wedge feet.
- The mace: on the vendor's axis and length, a six-sided shaft thickening to a barrel of a head with six fat studs,
  three round it at each of two places, their feet hushed (thin spikes read as scratches, one pointing at the
  camera as a target).
- Vertex normals lean up (0.4, the arms 0.55, the ears' tops 0.8), so big facets turned across the view stay dark
  instead of lighting whole in the rim; not further, and not the pate's: a shard turned over then shows a face whose
  normals point away, which the rim draws solid.

Each shape draws its polygons' outlines, never their fans' diagonals: 968 triangles at every LOD, where the vendor's
are 18,897, 9,786 and 4,990. Each shape is skinned by transfer from the vendor's nearest vertices over its own bones
(the body and the strap over the pelvis, spine, belly, neck and clavicles; each limb over its own side's), the head
and ears rigid on the head bone, the jaw on its own (Ponytail1), and the mace on the weapon bone that carries the
vendor's (MORGENSTERN_TRUE), carried, so the shatter flings it.

Source: Heroic Fantasy Creatures' FatOgre.prefab (sha256
b78fae5ec223865b6d86edc3ed7da49459f04d45b7cdf303479e1868e5c20bf1, as manifest.json lists it) and its mesh,
SK_FatOgre.FBX (1d523c97663fd3ed5471d350a18852c9c7e32e79da0868bf2e44c6774376d5df). No Blender: numpy only.
Bake: `just champions-tool monster bake fatOgre`; review: `just champions-tool monster review fatOgre` (and
`--look trailer`).
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

X, Y, Z = np.eye(3)
B = "FAT_OGRE_ "


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv), the first at u turned by phase."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre, np.float64) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


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

    def loft(self, rings, skin, carried: bool = False, soft_rings=(), soft_sides=()) -> list[list[int]]:
        """A shape lofted through rings of equal count (a ring may be one point, an apex), capped at both ends; each
        side face is wound outward from the axis between its two rings' centres. The edges round the rings in
        soft_rings, and those along it from the ring corners in soft_sides, don't draw. Returns each ring's ids."""
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
        out = [ids[o:o + len(r)] for o, r in zip(offsets, rings)]
        for i in soft_rings:
            r = out[i]
            for k in range(len(r)):
                self.hush(r[k], r[(k + 1) % len(r)])
        for k in soft_sides:
            for r0, r1 in pairwise(out):
                if len(r0) > 1 and len(r1) > 1:
                    self.hush(r0[k], r1[k])
        return out

    def plate(self, outline, normal, depth: float, skin) -> None:
        """A thin plate: the convex outline (n, 3) on top, the same `depth` behind along -normal; only the top
        outline draws, so it reads as one shape rather than a doubled rim."""
        top = np.asarray(outline, np.float64)
        low, _ = self.loft([top - unit(normal) * depth, top], skin)
        for i in range(len(top)):
            self.hush(low[i], low[(i + 1) % len(top)])
            self.hush(low[i], low[i] + len(top))

    def lean_up(self, ids, lean: float) -> None:
        """These points' normals lean up by `lean` (see normals)."""
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
        """Smooth vertex normals over each shape, as Unity would compute them, each leant up by its point's `lean`
        unless it faces down: a big facet turned across the view then stays dark rather than lighting whole in the
        fresnel rim, which weighs the vertex normal against the facet's. Not far: a shard turned over would show a
        face whose vertex normals point away from the camera, which the rim draws solid."""
        pts = np.array(self.points)
        p = pts[triangles]
        face = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        n = np.zeros_like(pts)
        for k in range(3):
            np.add.at(n, triangles[:, k], face)
        n /= np.linalg.norm(n, axis=1, keepdims=True)
        lean = np.where(n[:, 1] < -0.4, 0.0, np.array(self.lean))[:, None]
        n = n * (1 - lean) + Y * lean
        return (n / np.linalg.norm(n, axis=1, keepdims=True)).astype(np.float32)

    def skin(self) -> tuple[np.ndarray, np.ndarray]:
        """Each point's four bones: one bone with weight 1, or the weights of the vendor vertices nearest it (not
        carried, and led by one of the shape's bones), blended by inverse distance and kept to the shape's bones."""
        s = self.source
        live = ~s.carried
        vp = s.positions[live].astype(np.float64)
        vb, vw = s.bones[live], s.weights[live].astype(np.float64)
        lead = vb[np.arange(len(vb)), np.argmax(vw, axis=1)]
        bones = np.zeros((len(self.points), 4), np.int32)
        weights = np.zeros((len(self.points), 4), np.float32)
        for i, (p, skin) in enumerate(zip(self.points, self.skins)):
            if isinstance(skin, str):
                bones[i, 0], weights[i, 0] = s.bone(skin), 1
                continue
            allowed = np.zeros(len(s.bone_names), bool)
            allowed[[s.bone(n) for n in skin]] = True
            near = allowed[lead]
            d = np.linalg.norm(vp[near] - p, axis=1)
            dense = np.zeros(len(s.bone_names))
            for v in np.argsort(d)[:8]:
                np.add.at(dense, vb[near][v], vw[near][v] / (d[v] + 0.03))
            dense[~allowed] = 0
            top = np.argsort(-dense)[:4]
            bones[i], weights[i] = top, dense[top] / dense[top].sum()
        return bones, weights


def section(y, zc, hx, front, back, n: int = 8, phase: float = 0.0, tilt: float = 0.0) -> np.ndarray:
    """A ring round the body: x by hx, z from zc by `front` ahead and `back` behind; the first point on the right side
    (+x) turned by phase toward the front; y rises by `tilt` per metre back (a sagging belly's ring is low in front)."""
    t = phase + 2 * np.pi * np.arange(n) / n
    s = np.sin(t)
    z = zc + np.where(s > 0, front * s, back * s)
    return np.stack([hx * np.cos(t), y - tilt * (z - zc), z], axis=1)


LEAN = 0.4  # the body's vertex normals toward up (Builder.normals)
ARM_LEAN = 0.55  # the arms' long side facets more
EAR_LEAN = 0.8  # the ears' tops, thin plates that face up

TORSO = tuple(B + n for n in ("Pelvis", "Spine", "Spine1")) + ("FAT_OGRE_SAGGING_BELLY",)
TOP = TORSO[1:3] + (B + "Neck", B + "L Clavicle", B + "R Clavicle")


def torso(b: Builder) -> list[int]:
    """The body in one ten-sided shape from under the gut to the neck: a gut wider and further forward than the
    vendor's, sagging over the crotch, the chest, the hunched back and the shoulders sloping up to the neck. Drawn by
    its long lines and by two rings: the fold under the chest, in front only, the gut's top (its bottom is the
    outline), and the collar round the neck, which closes the long lines over the hump. Returns its polygons."""
    n = 10
    ph = np.pi / 10
    gut = [
        section(1.45, 0.12, 0.42, 0.62, 0.55, n, ph, tilt=0.3),
        section(1.68, 0.12, 0.86, 1.12, 0.92, n, ph, tilt=0.2),
        section(2.0, 0.12, 1.12, 1.42, 1.0, n, ph, tilt=0.1),
        section(2.4, 0.08, 1.18, 1.46, 1.02, n, ph),
        section(2.85, -0.04, 1.06, 1.12, 1.0, n, ph),
    ]
    chest = [
        section(3.2, -0.12, 0.98, 0.98, 1.04, n, ph),
        section(3.62, -0.22, 1.0, 0.9, 0.98, n, ph),
        section(4.0, -0.32, 0.9, 0.62, 0.86, n, ph),
        section(4.36, -0.4, 0.46, 0.3, 0.42, n, ph),
    ]
    rings = gut + chest
    first = len(b.polys)
    ids = b.loft(rings, TORSO + TOP, soft_rings=(0, 1, 2, 3, 5, 6, 7), soft_sides=(5, 6, 8, 9))
    fold = ids[4]
    for k in range(n):  # the fold under the chest draws in front only: no hoop round the back
        if b.points[fold[k]][2] + b.points[fold[(k + 1) % n]][2] < 2 * rings[4][:, 2].mean():
            b.hush(fold[k], fold[(k + 1) % n])
    return list(range(first, len(b.polys)))


def hit(origin, direction, triangles: np.ndarray):
    """Where a ray from `origin` first leaves through `triangles` (k, 3, 3) (Moller-Trumbore), or None."""
    e1, e2 = triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]
    p = np.cross(direction, e2)
    det = (e1 * p).sum(axis=1)
    ok = np.abs(det) > 1e-12
    inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
    tv = origin - triangles[:, 0]
    u = (tv * p).sum(axis=1) * inv
    q = np.cross(tv, e1)
    v = (q @ direction) * inv
    t = (q * e2).sum(axis=1) * inv
    good = ok & (u >= -1e-9) & (v >= -1e-9) & (u + v <= 1 + 1e-9) & (t > 0)  # an edge or a corner counts
    return origin + direction * t[good].max() if good.any() else None


def strap(b: Builder, torso_polys: list[int]) -> None:
    """A broad strap from the right shoulder across the chest and the gut to the left hip and round the back, where a
    tilted plane cuts the body: two lines on it, the vendor's harness simplified to what names it."""
    pts = np.array(b.points)
    tris = np.array([pts[[poly[0], poly[i], poly[i + 1]]] for poly in (b.polys[k] for k in torso_polys)
                     for i in range(1, len(poly) - 1)])
    slope, y0 = 0.86, 3.5  # the plane: y = y0 + slope * x, over the right shoulder by the neck to the left hip
    half = 0.17  # half the strap's height up the body
    n = 28

    def surface(y, d):
        return hit(np.array([0.0, y, -0.2]), d, tris)

    sections = []
    for phi in 2 * np.pi * np.arange(n) / n:
        d = np.array([np.cos(phi), 0.0, np.sin(phi)])
        lo, hi = 1.6, 4.35  # the height where the body meets the plane: below it, the body is out past it
        for _ in range(30):
            y = (lo + hi) / 2
            h = surface(y, d)
            if h is None or y > y0 + slope * h[0]:
                hi = y
            else:
                lo = y
        sections.append([surface(y + dy, d) + d * lift for dy, lift in ((-half, -0.02), (-half, 0.04),
                                                                        (half, 0.04), (half, -0.02))])
    flat = np.array(sections).reshape(-1, 3)  # point 4j + k: corner k of section j, a ring of them round the body
    faces, inside = [], []
    for j in range(n):
        a, c = 4 * j, 4 * ((j + 1) % n)
        for k in range(4):
            faces.append([a + k, a + (k + 1) % 4, c + (k + 1) % 4, c + k])
            inside.append((flat[a:a + 4].mean(axis=0) + flat[c:c + 4].mean(axis=0)) / 2)
    ids = b.shape(flat, faces, TORSO + TOP, inside=np.array(inside))
    for j in range(n):  # only its face's two edges draw
        a, c = 4 * j, 4 * ((j + 1) % n)
        for k in range(4):
            b.hush(ids[a + k], ids[a + (k + 1) % 4])
            if k in (0, 3):
                b.hush(ids[a + k], ids[c + k])


def head_ring(y, front, back, w_front, w_side, z_side) -> np.ndarray:
    """A ring round the head with a broad front panel: front corners at +-w_front, sides at +-w_side (z_side), the
    back narrower; listed from the front right round by the right side and the back to the front left."""
    return np.array([(w_front, y, front), (w_side, y, z_side), (w_side * 0.9, y, (z_side + back) / 2),
                     (w_front * 0.7, y, back), (-w_front * 0.7, y, back), (-w_side * 0.9, y, (z_side + back) / 2),
                     (-w_side, y, z_side), (-w_front, y, front)])


HEAD_PIVOT = np.array([0.0, 4.4, -0.05])
HEAD_SCALE = np.array([1.2, 1.12, 1.08])
HEAD_SHIFT = np.array([0.0, -0.12, 0.04])  # sunk between the shoulders


def big(points) -> np.ndarray:
    return HEAD_PIVOT + (np.asarray(points, np.float64) - HEAD_PIVOT) * HEAD_SCALE + HEAD_SHIFT


def head(b: Builder) -> set[int]:
    """A low skull whose face slopes back from the muzzle to the brow, so it looks up toward the camera. Returns the
    pate's points, whose normals stay as they are: leant up, the pate flew off in the shatter as a solid slab."""
    hb = B + "Head"
    jaw = B + "Ponytail1"
    base = head_ring(4.42, 0.4, -0.46, 0.22, 0.32, 0.05)
    muzzle = head_ring(4.54, 0.48, -0.55, 0.24, 0.38, 0.1)
    brow = head_ring(4.8, 0.32, -0.58, 0.26, 0.38, 0.02)
    brow[[0, 7], 1] += 0.05  # the brow's outer corners up, its middle down: a scowl
    crest = np.array([(-0.2, 4.98, -0.14), (0.2, 4.98, -0.14)])  # across the pate, side to side
    pts = big(np.vstack([base, muzzle, brow, crest]))
    left, right, c = 24, 25, 16
    faces = [[r * 8 + i, r * 8 + (i + 1) % 8, (r + 1) * 8 + (i + 1) % 8, (r + 1) * 8 + i] for r in (0, 1)
             for i in range(8)]
    faces.append(list(range(8)))
    faces += [[c + 6, c + 7, c + 0, c + 1, right, left], [right, left, c + 5, c + 4, c + 3, c + 2],
              [c + 1, c + 2, right], [c + 5, c + 6, left]]
    ids = b.shape(pts, faces, hb, inside=(0, 4.7, -0.1))
    for i in range(8):
        b.hush(ids[8 + i], ids[8 + (i + 1) % 8])  # the muzzle's ring stays quiet
    b.hush(ids[right], ids[c + 2])  # the pate: a crest across it from side to side, quiet behind
    b.hush(ids[left], ids[c + 5])
    for i in range(1, 6):  # and the back of the head: no can from behind
        for r in (0, c):
            b.hush(ids[r + i], ids[r + i + 1])
        if i > 1:
            b.hush(ids[i], ids[8 + i])
            b.hush(ids[8 + i], ids[c + i])
    face = [pts[8 + 7], pts[8 + 0], pts[c + 0], pts[c + 7]]  # left low, right low, right high, left high
    normal = unit(np.cross(face[1] - face[0], face[3] - face[0]))

    def on(u, v):
        return face[0] + (face[1] - face[0]) * u + (face[3] - face[0]) * v + normal * 0.012

    for sx in (-1, 1):  # slanted eye slits on the face, inner corners low
        eye = [on(0.5 + sx * 0.06, 0.52), on(0.5 + sx * 0.46, 0.84), on(0.5 + sx * 0.3, 0.96)]
        b.plate(eye, normal, 0.012, hb)
    # the nose: a wedge on the face
    top, low = on(0.5, 0.55), on(0.5, 0.18)
    nose = [top, low + (0.08, 0, 0), low - (0.08, 0, 0), (top + low) / 2 + normal * 0.12]
    nid = b.shape(nose, [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], hb)
    for i, j in ((0, 1), (1, 2), (2, 0)):
        b.hush(nid[i], nid[j])
    # the jaw: an underbite, wider than the skull at the jowls and jutting past the muzzle
    lo = head_ring(4.22, 0.6, -0.2, 0.28, 0.48, 0.18)
    hi = head_ring(4.47, 0.68, -0.2, 0.32, 0.5, 0.2)
    b.loft([big(lo), big(hi)], jaw)
    for s in (-1, 1):  # two tusks from its front corners, up and out past the lip
        foot = np.array([s * 0.27, 4.44, 0.6])
        b.loft([big(ring(foot, X, Z, (0.11, 0.1), 4, np.pi / 4)), big(foot + (s * 0.07, 0.18, 0.02))], jaw)
    return set(ids[c:])


def ears(b: Builder) -> list[int]:
    """Big ogre's ears out to the sides and back, drooping: leaves facing up, so the camera sees each one whole from
    every side, folded along one crease from root to tip; rigid on the head. Returns the leaves' top points."""
    tops = []
    for s in (-1, 1):
        root_f, root_b = (s * 0.36, 4.76, 0.04), (s * 0.35, 4.66, -0.22)
        front, tip, back = (s * 0.52, 4.84, 0.0), (s * 0.66, 4.74, -0.26), (s * 0.48, 4.66, -0.36)
        leaf = big([root_f, front, tip, back, root_b])
        crease = (leaf[0] + leaf[4]) / 2 + (0, 0.02, 0)
        leaf = np.vstack([leaf, crease])
        under = leaf[:5] - (0, 0.03, 0)
        pts = np.vstack([leaf, under])
        faces = [[5, 0, 1, 2], [5, 2, 3, 4], [10, 9, 8, 7, 6]]
        faces += [[i, i + 1, 7 + i, 6 + i] for i in range(4)] + [[4, 5, 0, 6, 10]]
        ids = b.shape(pts, faces, B + "Head", inside=big((s * 0.46, 4.64, -0.16)))
        tops += ids[:6]
        for i in range(5):  # the underside and the rim's short sides stay dark: one leaf, one crease
            b.hush(ids[6 + i], ids[6 + (i + 1) % 5])
            b.hush(ids[i], ids[6 + i])
        b.hush(ids[5], ids[0])
        b.hush(ids[5], ids[4])
    return tops


def limb(b: Builder, joints, radii, skin, n: int = 6, soft_rings=(), ref=Z) -> list[int]:
    """One n-sided loft through `joints`, its radius (or pair of radii) at each, each ring square to the limb there
    and turned alike by `ref`, so the faces don't twist; skinned over `skin`. Returns its points."""
    joints = [np.asarray(j, np.float64) for j in joints]
    segments = [unit(c - a) for a, c in pairwise(joints)]
    rings = []
    for i, (j, r) in enumerate(zip(joints, radii)):
        d = unit(segments[max(i - 1, 0)] + segments[min(i, len(segments) - 1)])
        u = unit(ref - (ref @ d) * d)
        ru, rv = (r, r) if np.isscalar(r) else r
        rings.append(ring(j, u, np.cross(d, u), (ru, rv), n))
    return [i for r in b.loft(rings, skin, soft_rings=soft_rings) for i in r]


def arms(b: Builder) -> list[int]:
    """Each arm one six-sided loft through the vendor's joints to a big fist, heavy at the shoulder and the forearm,
    lines round it only at the elbow and the wrist (separate fists read as a kit of boxes). Returns their points."""
    ids = []
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = tuple(f"{B}{side} {n}" for n in ("Clavicle", "UpperArm", "Forearm", "Hand"))
        shoulder = np.array([s * 0.95, 3.95, -0.32])
        deltoid = np.array([s * 1.2, 3.85, -0.32])
        elbow = np.array([s * 1.92, 3.41, -0.43])
        fore = np.array([s * 2.2, 3.28, -0.2])
        wrist = np.array([s * 2.5, 3.08, 0.05])
        d = unit(wrist - fore)
        fist, knuckles = wrist + d * 0.25, wrist + d * 0.48  # a big fist, the arm's end
        ids += limb(b, [shoulder, deltoid, elbow, fore, wrist, fist, knuckles],
                    [0.42, 0.45, 0.3, 0.36, 0.24, (0.3, 0.24), (0.2, 0.16)], bones, soft_rings=(1, 3, 5))
    return ids


def legs(b: Builder) -> None:
    """Each leg one six-sided loft from the hip, mostly under the gut, its trouser hem flaring at the shin, and a big
    wedge of a foot narrowing to the toes."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        bones = tuple(f"{B}{side} {n}" for n in ("Thigh", "Calf", "Foot"))
        hip = np.array([s * 0.44, 1.94, -0.3])
        knee = np.array([s * 0.62, 0.97, -0.08])
        hem = np.array([s * 0.68, 0.72, -0.08])
        ankle = np.array([s * 0.77, 0.32, -0.09])
        limb(b, [hip, knee, hem, hem + (0, -0.02, 0), ankle], [0.48, 0.44, 0.52, 0.36, 0.3], bones,
             soft_rings=(1,))
        foot = B + side + " Foot"
        toe = B + side + " Toe0"
        x = s * 0.8
        pts = [(x + dx * w, y, z) for dx in (-1, 1) for y, z, w in ((0.0, -0.45, 0.36), (0.0, 0.78, 0.28),
                                                                     (0.46, -0.32, 0.36), (0.2, 0.62, 0.28))]
        faces = [[0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]]
        b.shape(pts, faces, (foot, toe))


def club(b: Builder, source: Source) -> None:
    """The morning star on the vendor's axis and length: a six-sided shaft thickening from the butt, and a barrel of a
    head with six fat studs, three round it at each of two places, where the vendor's log bound in rope drew a knot."""
    p = source.part("MORGENSTERN_TRUE")
    q = source.positions[p.first:p.first + p.count].astype(np.float64)
    mid = q.mean(axis=0)
    _, _, axes = np.linalg.svd(q - mid)
    u = axes[0]
    along = (q - mid) @ u
    if along.max() < -along.min():
        u, along = -u, -along
    w = unit(np.cross(u, Y))
    t = np.cross(w, u)
    bone = "MORGENSTERN_TRUE"
    butt = along.max()

    def at(a):
        return mid + u * a

    shaft = [ring(at(a), w, t, (r, r), 6) for a, r in ((butt, 0.14), (0.3, 0.22), (0.1, 0.25))]
    b.loft(shaft, bone, carried=True)
    head_rings = [ring(at(a), w, t, (r, r), 8, np.pi / 8) for a, r in ((0.18, 0.3), (0.02, 0.5), (-0.52, 0.5),
                                                                         (-0.68, 0.3))]
    b.loft(head_rings, bone, carried=True)
    for station, phase in ((-0.08, 0.0), (-0.42, np.pi / 3)):  # six fat studs, three round at each of two places
        for k in range(3):
            ang = phase + k * 2 * np.pi / 3
            dirn = np.cos(ang) * w + np.sin(ang) * t
            base = at(station) + dirn * 0.44
            foot = b.loft([ring(base, u, np.cross(dirn, u), (0.19, 0.19), 4, np.pi / 4), base + dirn * 0.34], bone,
                          carried=True)[0]
            for i in range(4):
                b.hush(foot[i], foot[(i + 1) % 4])  # its foot lies in the head


def build(source: Source) -> Mesh:
    b = Builder(source)
    body = torso(b)
    strap(b, body)
    pate = head(b)
    ear_tops = ears(b)
    arm_ids = arms(b)
    legs(b)
    club(b, source)
    b.lean_up([i for i in range(len(b.points)) if i not in pate], LEAN)
    b.lean_up(arm_ids, ARM_LEAN)
    b.lean_up(ear_tops, EAR_LEAN)
    triangles, edges = b.triangles()
    bones, weights = b.skin()
    return Mesh(np.array(b.points, np.float32), triangles, bones, weights, b.normals(triangles), np.array(b.carried),
                edges)
