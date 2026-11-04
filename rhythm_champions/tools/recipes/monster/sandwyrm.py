"""The Sandwyrm (ps-8z4.18), Boss Rush boss 6, act 2's opener: the vendor's giant worm rebuilt whole as a few simple
faceted volumes, every edge it designs drawn, around what names it: a long segmented worm rearing out of the sand,
armour plates down its body, a row of spikes along its back, and a head that is all mouth, four jaws that close to a
point and open into a maw.

The trailer's drawing of the vendor mesh (2,396 triangles) is a thin worm whose head is a knot of small triangles
(teeth, tongues, a ridged carapace and serrated mandibles) and whose body is lined with small sawtooth spikes: at
game size the head blurs into noise and the spikes into a sawtooth fringe, though the long curling body reads. So
nothing of the vendor mesh is kept; every part is new, built in the body's bind pose and skinned to the vendor bones,
and every edge that draws is chosen here (a polygon's edges, never its triangles' diagonals):

- The body: one eight-sided loft from a point at the tail to the neck, a corner on the spine and one on the belly,
  in seven armour plates each over two of the vendor's segments. Each plate flares to a rim at its lower edge over
  the top of the plate below, as shingles that shed sand, so the body's outline steps at every plate and the rims
  draw round it; the plates' upper edges, tucked under the rim, don't (a doubled ring). Girth swells from the tail
  to the neck, fuller than the vendor's, so the tail keeps its outline to its point.
- Spikes: one broad swept-back pyramid on each plate's spine, larger toward the neck (the vendor's many small ones
  read as a sawtooth fringe; thin ones lit edge-on in the rim).
- The head, 1.15 times the vendor's: a plain collar on the neck (a flared lip was a thin band seen edge-on that
  filled solid in the rim), and the mouth, four jaws closing to a point as the vendor's do, each a thick curved
  petal on its own jaw chain, so the clips open them into a four-jawed maw: the top and bottom jaws short, the side
  mandibles longer hooks curving in with fangs down their inner faces. A ring of eight fangs round the throat
  inside them makes the open maw read as a mouth (without it, the head-down poses read as a claw or a fist).

The vendor's parts were bound separately, and some bone names appear twice with different bind poses (the spikes'
Worm_Bone002 is not the body's), so every shape is skinned by palette index to the body's entries only: the body's
chain, the head root and each jaw's chain, by transfer from the vendor's nearest body, teeth and carapace vertices
over the shape's own entries. The body's joints come from the baked bone texture (where each bone and its parent
move alike over every clip). The body and the collar lean their normals a little (0.4) toward the head, which is up
while it rears, keeping their long side facets dark in the rim; the jaws keep their own, as the clips swing them
toward the camera. The mesh flags its edges, so it is drawn as designed at every distance: 478 triangles at every
LOD, where the vendor's are 2,396.

The type's height (monsters.json heightM 14) is measured on the vendor's bind pose, which stands its parts apart; at
heightM 5 the worm reared only 1.7 m in its idle. At 14 it rears about 4.8 m, a boss's size, its tail a metre under
the floor.

Source: Infinity PBR's Giant_Worm_LP.prefab (sha256 3cb3552e1fd38da20a11583be3c6db3437a0fd37b12b839cd1d0f8f064016140)
and Giant_Worm_LP.fbx (c3fc83343c5689727ebced1608ba35e0e67bdf8fd16fcc8c333eeaf2b5102b89), as manifest.json lists
them. Only its rig is used, and its rest pose and skin weights to place and skin the shapes. No Blender: numpy only.
Bake: `just champions-tool monster bake sandwyrm`; review: `just champions-tool monster review sandwyrm` (and
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


# The vendor's palette entries by index: its parts were bound separately and some bone names appear more than once
# with different bind poses (the spikes' Worm_Bone002 is not the body's). Every shape here is built in the body's bind
# space (the worm standing straight up, mouth closed to a point at the top, its back toward -z) and skinned to the
# body's entries only.
BODY = tuple(range(127, 143))  # Worm_Bone001 (the tail) .. Worm_Bone016 (the neck)
HEAD = 67  # BottomMandible_Bone023, the head's root
JAWS = {  # each jaw's chain from the head out, and the direction it faces in the bind pose
    "top": ((2, 3, 4), -Z),  # TopMandible_Bone019..021, over the back
    "bottom": ((68, 69, 70), Z),  # BottomMandible_Bone024..026
    "left": ((75, 76, 77, 78), -X),  # LeftMandible_Bone033, 029, 030, 031
    "right": ((72, 73, 6, 7), X),  # RightMandible_Bone035, 036, 037, 038
}
CORE = ("Worm_body_lp", "teeth_lp", "Carapace_lp")  # the parts bound with the body's entries

# The body's joints in its bind pose (where each bone and its parent move alike over every clip, from the baked bone
# texture), tail to neck: (height, depth).
JOINTS = [(-7.588, 0.069), (-7.186, 0.091), (-6.745, 0.11), (-6.352, 0.122), (-5.903, 0.131), (-5.464, 0.139),
          (-5.071, 0.147), (-4.65, 0.156), (-4.211, 0.166), (-3.791, 0.177), (-3.37, 0.193), (-2.959, 0.195),
          (-2.548, 0.173), (-2.283, 0.148), (-1.884, 0.097)]
TAIL_TIP = (-8.1, 0.05)


class Builder:
    """Closed shapes of convex polygons, each wound outward; every polygon's outline draws unless the edge is hushed.
    Each shape is skinned by transfer from the vendor vertices near it over a set of palette entries."""

    def __init__(self, source: Source):
        self.source = source
        self.points: list[np.ndarray] = []
        self.skins: list[tuple[int, ...]] = []
        self.polys: list[list[int]] = []
        self.leans: list[np.ndarray] = []
        self.quiet: set[tuple[int, int]] = set()

    def shape(self, points, faces, skin, inside=None, lean=(0, 0, 0)) -> list[int]:
        """A closed shape: points (n, 3), faces (convex polygons of point indices) wound outward from `inside` (the
        shape's centroid, or one point per face); `skin` the palette entries it is skinned over; `lean` a vector its
        shading normals lean along (see mesh)."""
        pts = np.asarray(points, np.float64)
        first = len(self.points)
        for p in pts:
            self.points.append(p)
            self.skins.append(tuple(skin))
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

    def loft(self, rings, skin, soft_rings=(), soft_sides=(), lean=(0, 0, 0)) -> list[list[int]]:
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
        ids = self.shape(pts, faces, skin, np.array(inside), lean)
        out = [ids[o:o + len(r)] for o, r in zip(offsets, rings)]
        for i in soft_rings:
            for k in range(len(out[i])):
                self.hush(out[i][k], out[i][(k + 1) % len(out[i])])
        for k in soft_sides:
            for r0, r1 in pairwise(out):
                if len(r0) > 1 and len(r1) > 1:
                    self.hush(r0[k], r1[k])
        return out

    def hush(self, a: int, b: int) -> None:
        self.quiet.add((min(a, b), max(a, b)))

    def mesh(self) -> Mesh:
        """Each polygon as a fan whose triangles draw only the polygon's outline; every triangle its own corners,
        their normal its facet's leaning a little along its shape's lean. The rim mixes the normal with the facet's
        own, so the long facets turned across the view stay dark rather than glowing whole; a face turned against the
        lean keeps its own normal."""
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
                    np.zeros(len(corners), bool), np.array(flags, np.uint8))

    def skin(self) -> tuple[np.ndarray, np.ndarray]:
        """Each point's four bones: the weights of the core parts' vendor vertices nearest it, blended by inverse
        distance and kept to the shape's palette entries."""
        s = self.source
        core = np.zeros(len(s.positions), bool)
        for name in CORE:
            part = s.part(name)
            core[part.first:part.first + part.count] = True
        vp = s.positions[core].astype(np.float64)
        vb, vw = s.bones[core], s.weights[core].astype(np.float64)
        lead = vb[np.arange(len(vb)), np.argmax(vw, axis=1)]
        bones = np.zeros((len(self.points), 4), np.int32)
        weights = np.zeros((len(self.points), 4), np.float32)
        for i, (p, skin) in enumerate(zip(self.points, self.skins)):
            allowed = np.zeros(len(s.bone_names), bool)
            allowed[list(skin)] = True
            near = allowed[lead]
            d = np.linalg.norm(vp[near] - p, axis=1)
            dense = np.zeros(len(s.bone_names))
            for v in np.argsort(d)[:8]:
                np.add.at(dense, vb[near][v], vw[near][v] / (d[v] + 0.01))
            dense[~allowed] = 0
            top = np.argsort(-dense)[:4]
            bones[i], weights[i] = top, dense[top] / dense[top].sum()
        return bones, weights


LEAN = 0.4  # how far the shading normals lean (Builder.mesh)
SIDES = 8  # the body's cross-section, a corner on the spine (-z)


def section(y: float, z: float, r: float) -> np.ndarray:
    """A ring of the body at height y, its axis at depth z, a corner on the spine (-z) and one on the belly."""
    return ring((0.0, y, z), -Z, X, (r, r), SIDES)


def radius(y: float) -> float:
    """The body's girth: a point at the tail, swelling to the neck."""
    t = np.clip((y - TAIL_TIP[0]) / (JOINTS[-1][0] - TAIL_TIP[0]), 0, 1)
    return 0.66 * t ** 0.4 * (0.8 + 0.2 * t)


# The armour plates, tail to neck, each over two of the vendor's segments: (the joint at its lower edge, at its upper).
PLATES = [(0, 2), (2, 4), (4, 6), (6, 8), (8, 10), (10, 12), (12, 14)]
SHINGLE = 0.86  # a plate's upper edge, tucked under the plate above, against its flared lower rim


def body(b: Builder) -> list[tuple[np.ndarray, np.ndarray]]:
    """A worm of armour plates, each flaring to a rim at its lower edge over the top of the plate below, as shingles
    shed sand: one eight-sided loft from the tail's point to the neck, a step at every plate. The rims draw round it;
    the plates' upper edges, tucked under the rim, don't (a doubled ring). Returns each plate's spine line (lower,
    upper) for the spikes."""
    rings, soft, spines = [np.array([0.0, *TAIL_TIP])], [], []
    for lo, hi in PLATES:
        (y0, z0), (y1, z1) = JOINTS[lo], JOINTS[hi]
        rings.append(section(y0 + 0.02, z0, radius(y0) * 1.04))
        rings.append(section(y1, z1, radius(y1) * SHINGLE))
        soft.append(len(rings) - 1)
        spines.append((rings[-2][0], rings[-1][0]))
    b.loft(rings, BODY + (HEAD,), soft_rings=soft, lean=Y)
    return spines


def spikes(b: Builder, spines) -> None:
    """A swept-back spike on each plate's spine, a broad closed pyramid leaning toward the tail."""
    for i, (lo, hi) in enumerate(spines):
        size = 0.3 + 0.03 * i
        root = lo + (hi - lo) * 0.3
        up = unit(hi - lo)
        tip = root - Z * size - up * size * 0.6
        across = X * size * 0.7
        base = [root + across, root - across, root + up * size * 1.1]
        b.shape(np.vstack([base, tip]), [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], BODY, lean=Y)


HEAD_SCALE = 1.15  # about the neck: at the vendor's size the mouth was a knot at the end of the body


def head(b: Builder) -> None:
    """A collar flaring round the neck, and within it the mouth: four jaws closing to a point, the top and bottom
    short, the two side mandibles longer hooks curving in with fangs down their inner faces. Each jaw is skinned
    to its own chain, so the clips open them into a four-jawed maw."""
    yn, zn = JOINTS[-1]
    neck = np.array([0.0, yn, zn])
    first = len(b.points)
    r = radius(yn)
    collar = [section(yn + 0.02, zn, r * 1.02), section(-1.5, 0.06, r * 1.08), section(-1.28, 0.04, r * 0.96)]
    b.loft(collar, BODY[-3:] + (HEAD,), soft_rings=(2,), lean=Y)
    throat(b, np.array([0.0, -1.28, 0.04]), r * 0.62)
    for name, (chain, facing) in JAWS.items():
        if name in ("left", "right"):
            jaw(b, chain, facing, -1.3, r * 0.95, 1.05, [(0.5, 1.02, 0.75), (0.82, 0.6, 0.45)], 72, 3)
        else:
            jaw(b, chain, facing, -1.3, r * 0.95, 0.95, [(0.45, 0.95, 0.8)], 60, 2)
    for k in range(first, len(b.points)):
        b.points[k] = neck + (b.points[k] - neck) * HEAD_SCALE


def throat(b: Builder, centre, rad: float, n: int = 8) -> None:
    """A ring of fangs round the throat inside the jaws, each a closed pyramid pointing in and out of the mouth, so
    the open maw reads as a mouth rather than a claw."""
    for k in range(n):
        a = 2 * np.pi * (k + 0.5) / n
        out = np.cos(a) * -Z + np.sin(a) * X
        along = np.cross(Y, out)
        at = centre + out * rad
        size = 0.16
        base = [at + along * size * 0.55, at - along * size * 0.55, at - out * size * 0.2 - Y * size * 0.5]
        point = at - out * size * 1.1 + Y * size * 1.2
        b.shape(np.vstack([base, point]), [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], (HEAD,))


def jaw(b: Builder, chain, facing, base_y: float, rim: float, length: float, levels, span: float, fangs: int) -> None:
    """One jaw: a thick curved petal from the collar to a point at the mouth's axis through `levels` (the fraction of
    its length, its radius and its width as fractions of the base's), its outer face in two facets across."""
    f = unit(facing)
    across = unit(np.cross(Y, f))
    h = np.radians(span / 2)
    centre = np.array([0.0, base_y, 0.04])

    def section_at(t, rad, width):
        c = centre + Y * length * t
        outer = [c + (f * np.cos(a) + across * np.sin(a)) * rim * rad for a in (-h * width, 0, h * width)]
        inner = [c + (p - c) * 0.5 for p in outer]
        return outer + inner[::-1]

    rings = [section_at(0, 1.0, 1.0)] + [section_at(t, rad, w) for t, rad, w in levels]
    b.loft(rings + [centre + Y * length + f * 0.04], (HEAD,) + tuple(chain), soft_sides=(3,))
    for k in range(fangs):  # fangs down the inner face, pointing in toward the axis
        t = 0.3 + 0.45 * k / max(fangs - 1, 1)
        at = centre + Y * length * t + f * rim * 0.48
        size = 0.15 * (1.1 - 0.4 * t)
        base = [at + across * size * 0.5, at - across * size * 0.5, at - Y * size * 0.6]
        point = at - f * size * 1.5 + Y * size * 0.4
        b.shape(np.vstack([base, point]), [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], tuple(chain))


def build(source: Source) -> Mesh:
    b = Builder(source)
    spines = body(b)
    spikes(b, spines)
    head(b)
    return b.mesh()
