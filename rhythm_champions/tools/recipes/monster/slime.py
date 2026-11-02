"""The slime (ps-6y2.32): the vendor's smooth dome rebuilt as a low-poly blob that looks up with one big eye.

The trailer's drawing of the vendor slime has no facets: its dome's 470 even triangles never crease past 24 degrees,
so it draws as a soft rim (a bubble, an egg or one of the game's rings) with the eye socket's fan of creases as
radiating lashes (a sun icon at game size), and nothing says creature unless a slime faces the camera. What changes:

- The body: eight sides, every edge drawn (a true wireframe), 1.5 m across and 0.9 m tall. It sags: widest low down,
  over a base tucked under it, so its lower outline is a bulge and no upright band reads as a pot or a jelly mould;
  it narrows to a shoulder ring and closes under a crown, a face in front and a lopsided corner behind, rather than
  an apex, whose spokes read as an umbrella or a spider web from the game camera. It is 10% longer behind, where the
  vendor's body drags.
- The face: one quiet panel on the top front, tilted 55 degrees up, so the game camera sees the eye from every
  side and not only when a slime faces it. The eye is an almond lens 0.53 m across (a third of the body, more than
  twice the vendor's eye), drawn by its rim alone, with a raised slit pupil whose sides fill it in at game size. It
  is printed on the face: each of its vertices blends the face's corners' weights, so it follows the face's hop,
  drag and squint without sinking or sliding.
- Vertex normals lean up, so the big facets turned across the view stay dark rather than lighting whole in the
  fresnel rim, and the lines carry the outline.
- Dropped: the brain and veins inside the dome, which the dark body hides in every view, the vendor's eyeball and
  its lids, and the bottom: tucked under the bulge, it is out of every view, and its fan only drew spokes in the
  shatter.

88 triangles at every LOD, where the vendor's are 1,428, 767 and 384, skinned to the vendor's body bones (root,
drag, top and eye region) from its nearest vertices, so the slime moves with the vendor rig and clips.

Source: Magic Pig Games' Slime_LP.prefab (sha256 b288527883abb23512cb8f31d5206d11f025527683d52b657ae63ad7bf1ac92d)
and Slime_LP.fbx (41162a2653ba20e6a65565640b3c51a475df2c69387b9bb5f113c66ad4830d2e), as manifest.json lists them.
No Blender: numpy only. Bake: `just champions-tool monster bake slime`; review: `just champions-tool monster review
slime` (and `--look trailer`).
"""

from __future__ import annotations

import math
from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

SIDES = 8  # a facet column faces forward, under the eye
AXIS = (0.0, -0.24)  # the vendor dome's axis (x, z); its eye looks along +z
RINGS = [(0.0, 0.62), (0.21, 0.78), (0.59, 0.58)]  # (height, radius): the base tucked under, the bulge, the shoulder
DRAG = 0.10  # rings this much longer straight behind
CROWN_FRONT = (0.90, 0.10, 0.26)  # the face's top edge: height, forward of the axis, half its width
CROWN_BACK = (0.81, -0.30, 0.08)  # the crown's back corner: height, forward of the axis, to the right
NORMAL_LEAN = 0.35  # a vertex normal's outward share against up

EYE_RADIUS = 0.28
EYE_ASPECT = 0.62  # its height to its width: an almond
EYE_RISE, SLIT_RISE = 0.05, 0.03  # the lens's face over the body's, the slit's over the lens's
BODY_BONES = ("slime_rig_root", "slime_rig_drag", "slime_rig_top", "slime_eye_region")


class Builder:
    """Convex polygons over shared vertices; each polygon's outline draws unless the edge is hushed."""

    def __init__(self) -> None:
        self.points: list[np.ndarray] = []
        self.eye: list[bool] = []
        self.polys: list[list[int]] = []
        self.quiet: set[tuple[int, int]] = set()

    def vert(self, p, eye: bool = False) -> int:
        self.points.append(np.asarray(p, dtype=np.float64))
        self.eye.append(eye)
        return len(self.points) - 1

    def poly(self, *idx: int) -> None:
        self.polys.append(list(idx))

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
        return np.array(tris, dtype=np.int32), np.array(flags, dtype=np.uint8)


def body(b: Builder) -> list[int]:
    """The base, bulge and shoulder rings and the crown over them, open underneath; the face's four corners."""
    def point(y: float, r: float, theta: float) -> np.ndarray:
        r *= 1 + DRAG * max(0.0, -math.cos(theta))
        return np.array([AXIS[0] + r * math.sin(theta), y, AXIS[1] + r * math.cos(theta)])

    rings = [[b.vert(point(y, r, (j + 0.5) * 2 * math.pi / SIDES)) for j in range(SIDES)] for y, r in RINGS]
    for lo, hi in pairwise(rings):
        for j in range(SIDES):
            k = (j + 1) % SIDES
            b.poly(lo[j], lo[k], hi[k], hi[j])
    # no base: it is tucked under the bulge out of every view, and its fan only drew spokes in the shatter
    s = rings[-1]  # s[0] front right, round to s[7] front left
    y, z, w = CROWN_FRONT
    fl = b.vert((AXIS[0] - w, y, AXIS[1] + z))
    fr = b.vert((AXIS[0] + w, y, AXIS[1] + z))
    y, z, x = CROWN_BACK
    back = b.vert((AXIS[0] + x, y, AXIS[1] + z))
    b.poly(s[7], s[0], fr, fl)  # the face, one quiet panel with the triangles beside it
    b.hush(s[0], fr)
    b.hush(s[7], fl)
    b.poly(s[0], s[1], fr)
    b.poly(s[1], s[2], back, fr)
    for j in range(2, 5):
        b.poly(s[j], s[j + 1], back)
    b.poly(s[5], s[6], fl, back)
    b.poly(s[6], s[7], fl)
    b.poly(fr, back, fl)  # the crown
    return [s[7], s[0], fr, fl]


def eye(b: Builder, face: list[int]) -> None:
    """An almond lens on the face (only its rim draws) and a raised slit pupil: two closed shapes."""
    corners = np.array([b.points[i] for i in face])
    side = np.array([1.0, 0.0, 0.0])
    up = (corners[2] + corners[3]) / 2 - (corners[0] + corners[1]) / 2
    up /= np.linalg.norm(up)
    normal = np.cross(side, up)
    centre = corners.mean(axis=0)
    lens = centre + EYE_RISE * normal

    def loop(c: np.ndarray, rx: float, ry: float, count: int = 8) -> list[int]:
        return [b.vert(c + rx * math.cos(a) * side + ry * math.sin(a) * up, eye=True)
                for a in (2 * math.pi * i / count for i in range(count))]

    base = loop(centre, EYE_RADIUS, EYE_RADIUS * EYE_ASPECT)
    rim = loop(lens, EYE_RADIUS * 0.94, EYE_RADIUS * EYE_ASPECT * 0.9)
    for i in range(8):
        j = (i + 1) % 8
        b.poly(base[i], base[j], rim[j], rim[i])
        b.hush(base[i], base[j])
        b.hush(base[i], rim[i])
    b.poly(*reversed(base))
    # the slit, a tall diamond: the lens's face joins each rim vertex to the slit's nearest corner, quietly
    tall = EYE_RADIUS * EYE_ASPECT * 0.8
    tips = [tall * up, -EYE_RADIUS * 0.13 * side, -tall * up, EYE_RADIUS * 0.13 * side]
    slit = [b.vert(lens + t, eye=True) for t in tips]
    for i in range(8):
        j = (i + 1) % 8
        ci = min(slit, key=lambda k: np.linalg.norm(b.points[k] - b.points[rim[i]]))
        cj = min(slit, key=lambda k: np.linalg.norm(b.points[k] - b.points[rim[j]]))
        if ci == cj:
            b.poly(rim[i], rim[j], ci)
        else:
            b.poly(rim[i], rim[j], cj, ci)
        b.hush(rim[i], ci)
        b.hush(rim[j], cj)
    raised = [b.vert(lens + SLIT_RISE * normal + 0.8 * t, eye=True) for t in tips]
    for i in range(4):
        j = (i + 1) % 4
        b.poly(slit[i], slit[j], raised[j], raised[i])
    b.poly(*raised)


def skin(source: Source, points: np.ndarray, eye: list[bool], face: list[int]) -> tuple[np.ndarray, np.ndarray]:
    """Each vertex's four bones. The body's blend the nearest vendor body vertices' weights by inverse distance, kept to
    the body's bones (no lid or eye bone pulls them). The eye is printed on the face: each of its vertices blends the
    face's four corners' weights by where it lies on the face, so it follows the face in every pose."""
    part = source.part("Slime:Slime")
    vp = source.positions[part.first:part.first + part.count].astype(np.float64)
    vb = source.bones[part.first:part.first + part.count]
    vw = source.weights[part.first:part.first + part.count].astype(np.float64)
    allowed = np.zeros(len(source.bone_names), dtype=bool)
    allowed[[source.bone(n) for n in BODY_BONES]] = True
    dense = np.zeros((len(points), len(source.bone_names)))
    for i, p in enumerate(points):
        if eye[i]:
            continue
        d = np.linalg.norm(vp - p, axis=1)
        for v in np.argsort(d)[:8]:
            np.add.at(dense[i], vb[v], vw[v] / (d[v] + 0.02))
        dense[i][~allowed] = 0
        dense[i] /= dense[i].sum()
    bl, br, tr, tl = (points[i] for i in face)
    up = (tl + tr) / 2 - (bl + br) / 2
    for i in np.flatnonzero(eye):
        v = np.clip(np.dot(points[i] - (bl + br) / 2, up) / np.dot(up, up), 0, 1)
        left, right = bl + v * (tl - bl), br + v * (tr - br)
        u = np.clip((points[i][0] - left[0]) / (right[0] - left[0]), 0, 1)
        dense[i] = ((1 - u) * (1 - v) * dense[face[0]] + u * (1 - v) * dense[face[1]] + u * v * dense[face[2]]
                    + (1 - u) * v * dense[face[3]])
    bones = np.argsort(-dense, axis=1)[:, :4].astype(np.int32)
    weights = np.take_along_axis(dense, bones, axis=1)
    return bones, (weights / weights.sum(axis=1, keepdims=True)).astype(np.float32)


def normals(points: np.ndarray) -> np.ndarray:
    """Leaning up: the rim weighs a facet's own normal more, but these keep a big facet turned across the view from
    lighting whole."""
    out = points - np.array([AXIS[0], 0.0, AXIS[1]])
    out[:, 1] = 0
    out /= np.maximum(np.linalg.norm(out, axis=1, keepdims=True), 1e-6)
    n = NORMAL_LEAN * out + np.array([0.0, 1.0, 0.0])
    return (n / np.linalg.norm(n, axis=1, keepdims=True)).astype(np.float32)


def build(source: Source) -> Mesh:
    b = Builder()
    face = body(b)
    eye(b, face)
    points = np.array(b.points)
    triangles, edges = b.triangles()
    bones, weights = skin(source, points, b.eye, face)
    mesh = Mesh(points.astype(np.float32), triangles, bones, weights, normals=normals(points))
    mesh.edges = edges
    return mesh
