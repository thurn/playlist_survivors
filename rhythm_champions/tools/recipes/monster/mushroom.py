"""The mushroom (ps-6y2.36): a toadstool rebuilt from a few simple shapes, a face on the front of its cap, two large
spots behind, walking on four thick roots.

The trailer's drawing of the vendor mesh has no facets. Its cap, a broad dome, rarely creases past 24 degrees, so it is
a wobbly fresnel outline with a glassy band along its far edge; its raised spots crease into zigzag scratches ("Z",
"7", "?"), read as graffiti or a sleeper's "Zzz"; and its root feet are a fuzz of short strokes in front, which stays
standing through the shatter. From the game camera, 60 degrees down, the cap hides the stalk and most of the legs, so
at game size it reads as a brain, broccoli or a jellyfish. So the whole figure is new, every line chosen:

- The cap: a dome of eight gores from the rim up to a flat crown, each folding halfway without a line there, so each
  gore is one closed panel. A lip under the rim gives the cap an edge the game camera sees in front, where the cap
  overhangs. The three gores in front are one quiet panel for the face, so no gore frames it.
- The face: two slanted almond eyes and a wide grin, each one closed shape, high on that panel, so the game camera sees
  them from most sides. The cap alone read as an object (a dome, a sea mine, a soccer ball); with a face it reads as a
  creature, and one that faces the hero as a monster that shoots should.
- The spots: two, large and faceted with uneven sides, behind, so the crown stays quiet. Small spots in every facet
  read as portholes or a soccer ball's patches.
- The stalk: a squat six-sided bulb, whose front shows under the lip. A stalk up into the cap is hidden by the cap in
  every view, and in the shatter it stood as a cluster of long spikes.
- The legs: four thick roots, each a four-sided prism with a ridge along its top, from the bulb out to the rim, bent
  at the knee and out along the ground to a blunt end, drawn by their long edges and their ends; rings at their joints
  read as scratches across them. The vendor's leg is a short shin under the body and a long root of a foot along the
  ground, so each root rises from its shin at the bulb and is its foot from the knee out.
- The face and spots are printed on the cap: each outline is carried along rays from the cap's centre onto its facets,
  raised a little, with a bevel down into the cap so the outline is drawn from both sides, as the cap's own edges
  are (drawn from one side only, it was a faint hairline).
- Every triangle on its own corners. The cap's normals lean up, so the fresnel rim outlines the cap rather than
  lighting its facets; the legs and stalk have their facets' normals.
- Dropped: the vendor's spots and its root toes.

310 triangles at every LOD, where the vendor's are 1,638, 958 and 512. The cap, its face and spots are rigid on the
cap's bone, the stalk on the pelvis, and each root's rings ride its own leg's shin and foot.

Source: Magic Pig Games' Mushroom_LP.prefab (sha256 a80eb3799721fc0fe5b540f2db856c9e4d60cb5aeee6a635244387e171057bd9)
and Mushroom_LP.fbx (259710e41bf8251ebcc249d8caa1f2024acdc4e6cadf5f9e5515dfc002e33a90), as manifest.json lists them.
No Blender: numpy only. Bake: `just champions-tool monster bake mushroom`; review: `just champions-tool monster review
mushroom` (and `--look trailer`).
"""

from __future__ import annotations

import math

import numpy as np

from champions_tools.monster import Mesh, Source

SIDES = 8  # the cap's gores; one faces forward, holding the face
CAP = [(0.585, 0.445, True), (0.64, 0.46, True), (0.78, 0.37, False), (0.88, 0.17, True)]  # (height, radius, ring
# drawn): the lip under the rim, which the game camera sees in front as the cap's edge, the rim, the fold, the crown
UNDER = (0.61, 0.13)  # the underside's inner ring, round the stalk
CAP_CENTRE = np.array([0.0, 0.6, 0.0])  # the face and the spots are printed along rays from here
STALK = [(0.02, 0.21), (0.36, 0.16)]  # (height, radius): a squat bulb, whose front shows under the lip; a stalk up
# into the cap would be hidden by it in every view, and it stood in the shatter as a cluster of long spikes
STALK_SIDES = 6
NORMAL_LEAN = 0.1  # a vertex normal's outward share against up

SPOTS = [(150, 36, 0.3, 0.3), (230, 52, 0.26, 1.1)]  # (azimuth, elevation, size, turn): two, large, behind, so the
# face has the front and the crown stays quiet
SPOT_RISE = 0.02
EYES = (31, 47, 0.18, 0.11, 0.35)  # (azimuth either side, elevation, width, height, slant)
MOUTH = (31, 0.3, 0.1)  # (elevation, width, depth)

ROOT = [(0.14, 0.15, 0.12), (0.27, 0.11, 0.1), (0.36, 0.05, 0.08), (0.47, 0.05, 0.05)]  # (out from the axis,
# height, half its thickness): the hip in the bulb, the knee, the ankle on the ground, the blunt end
LEGS = {  # leg chains: hip bone, shin bone, foot bone, the leg's direction (x, z)
    "01": ("MushLeg01", "Bone014", "Bone015", (1, 1)),
    "02": ("MushLeg02", "Bone005", "Bone006", (-1, 1)),
    "03": ("MushLeg03", "Bone008", "Bone009", (-1, -1)),
    "04": ("MushLeg04", "Bone011", "Bone012", (1, -1)),
}


class Figure:
    """The new mesh as it is built: convex shapes, each point skinned to vendor bones by weights given, their drawn edges
    flagged."""

    def __init__(self, source: Source):
        self.source = source
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.edges: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.weights: list[np.ndarray] = []
        self.normals: list[np.ndarray] = []
        self.count = 0

    def add(self, points, faces, lines, bones: str | list[dict[str, float]], normals=None) -> None:
        """A shape: convex polygons over `points`, wound outward from their mean; `lines` the pairs whose edges draw.
        `bones` names the one vendor bone it's rigid on, or gives each point its bones' weights. Without `normals`,
        each facet its own."""
        pts = np.asarray(points, np.float64)
        centre = pts.mean(axis=0)
        drawn = {frozenset(e) for e in lines}
        tris, flags = [], []
        for f in faces:
            for j in range(1, len(f) - 1):
                tri = [f[0], f[j], f[j + 1]]
                a, b, d = pts[tri]
                if np.dot(np.cross(b - a, d - a), (a + b + d) / 3 - centre) < 0:
                    tri = [tri[0], tri[2], tri[1]]
                tris.append(tri)
                flags.append([int(frozenset((tri[(k + 1) % 3], tri[(k + 2) % 3])) in drawn) for k in range(3)])
        if isinstance(bones, str):
            bones = [{bones: 1.0}] * len(pts)
        b = np.array([[self.source.bone(n) for n in list(d) + [next(iter(d))] * (4 - len(d))] for d in bones], np.int32)
        w = np.array([list(d.values()) + [0.0] * (4 - len(d)) for d in bones], np.float32)
        self.positions.append(pts)
        self.triangles.append(np.asarray(tris, np.int64) + self.count)
        self.edges.append(np.asarray(flags, np.uint8))
        self.bones.append(b)
        self.weights.append(w)
        self.normals.append(np.full((len(pts), 3), np.nan) if normals is None else np.asarray(normals, np.float64))
        self.count += len(pts)

    def mesh(self) -> Mesh:
        """The figure as a mesh, every triangle on its own corners: with its facet's normal, or the shape's own."""
        positions, triangles = np.vstack(self.positions), np.vstack(self.triangles)
        corners = triangles.reshape(-1)
        p = positions[triangles]
        facet = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        facet /= np.linalg.norm(facet, axis=1)[:, None]
        given = np.vstack(self.normals)[corners]
        normals = np.where(np.isnan(given), np.repeat(facet, 3, axis=0), given)
        return Mesh(positions[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    np.vstack(self.bones)[corners], np.vstack(self.weights)[corners],
                    normals=normals.astype(np.float32), edges=np.vstack(self.edges))


def ring(y: float, r: float, n: int) -> np.ndarray:
    """n points round the vertical axis at height y, a facet's middle facing forward (+z)."""
    t = (np.arange(n) + 0.5) * 2 * math.pi / n
    return np.c_[r * np.sin(t), np.full(n, y), r * np.cos(t)]


def leaning(points: np.ndarray) -> np.ndarray:
    """Normals leaning up from the axis: the fresnel rim then outlines the cap rather than lighting its facets."""
    out = points.copy()
    out[:, 1] = 0
    out /= np.maximum(np.linalg.norm(out, axis=1, keepdims=True), 1e-6)
    n = NORMAL_LEAN * out + np.array([0.0, 1.0, 0.0])
    return n / np.linalg.norm(n, axis=1, keepdims=True)


class Surface:
    """The cap's outer triangles, to print on: a point is carried along the ray from the cap's centre."""

    def __init__(self, triangles: np.ndarray):
        self.tris = triangles
        n = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
        n /= np.linalg.norm(n, axis=1, keepdims=True)
        n[np.einsum("ij,ij->i", n, triangles.mean(axis=1) - CAP_CENTRE) < 0] *= -1
        self.normals = n

    def hit(self, p: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Where the ray from the centre through p meets the cap, and the cap's normal there."""
        d = p - CAP_CENTRE
        a, b, c = self.tris[:, 0], self.tris[:, 1], self.tris[:, 2]
        e1, e2 = b - a, c - a
        h = np.cross(d, e2)
        det = np.einsum("ij,ij->i", e1, h)
        ok = np.abs(det) > 1e-12
        inv = np.where(ok, 1 / np.where(ok, det, 1), 0)
        s = CAP_CENTRE - a
        u = inv * np.einsum("ij,ij->i", s, h)
        q = np.cross(s, e1)
        v = inv * (q @ d)
        t = inv * np.einsum("ij,ij->i", e2, q)
        good = ok & (u >= -1e-6) & (v >= -1e-6) & (u + v <= 1 + 1e-6) & (t > 0)
        i = np.flatnonzero(good)[np.argmax(t[good])]
        return CAP_CENTRE + t[i] * d, self.normals[i]

    def print(self, f: Figure, azimuth: float, elevation: float, outline: np.ndarray, rise: float) -> None:
        """A closed shape printed on the cap `rise` off it, only its outline drawn: `outline` (k, 2) in metres across
        and up the cap round the point seen at `azimuth` (degrees from the front) and `elevation` (degrees up)."""
        az, el = math.radians(azimuth), math.radians(elevation)
        centre, normal = self.hit(CAP_CENTRE + np.array([math.sin(az) * math.cos(el), math.sin(el),
                                                         math.cos(az) * math.cos(el)]))
        across = np.array([math.cos(az), 0.0, -math.sin(az)])
        across -= normal * np.dot(across, normal)
        across /= np.linalg.norm(across)
        up = np.cross(normal, across)
        k = len(outline)
        pts = []
        for (x, y), lift in [((0.0, 0.0), rise)] + [(o, rise) for o in outline] + [(1.3 * o, -0.004) for o in outline]:
            p, n = self.hit(centre + x * across + y * up)
            pts.append(p + lift * n)
        # the lid a fan from its centre; a bevel from the lid's outline down into the cap, so the outline has a
        # triangle drawing it on each side, as every edge of the cap has
        faces = [[0, 1 + i, 1 + (i + 1) % k] for i in range(k)]
        faces += [[1 + i, 1 + k + i, 1 + k + (i + 1) % k, 1 + (i + 1) % k] for i in range(k)]
        lines = [(1 + i, 1 + (i + 1) % k) for i in range(k)]
        f.add(pts, faces, lines, "Bone003", normals=leaning(np.array(pts)))


def cap(f: Figure) -> Surface:
    """The cap: a dome of gores from the rim to the crown, each folding at a ring that isn't drawn, under a flat
    crown, the three in front one panel for the face; closed underneath round the stalk. Rigid on the cap's bone.
    Returns its outer surface, to print on."""
    n = SIDES
    pts = np.vstack([ring(y, r, n) for y, r, _ in CAP] + [ring(*UNDER, n)])
    gores = [[i * n + j, i * n + (j + 1) % n, (i + 1) * n + (j + 1) % n, (i + 1) * n + j]
             for i in range(len(CAP) - 1) for j in range(n)]
    crown = list(range((len(CAP) - 1) * n, len(CAP) * n))
    lines = [(face[j], face[(j + 1) % len(face)]) for face in gores + [crown] for j in range(len(face))]
    fold = {i * n + j for i, (_, _, drawn) in enumerate(CAP) if not drawn for j in range(n)}
    lines = [e for e in lines if not (e[0] in fold and e[1] in fold)]
    # the face: the front gore and the two beside it one quiet panel, so no gore frames it
    meridians = {i * n + j for i in range(len(CAP)) for j in (0, n - 1)}
    lines = [e for e in lines if not (e[0] in meridians and e[1] in meridians and abs(e[0] - e[1]) == n)]
    under = len(CAP) * n
    beneath = [[j, (j + 1) % n, under + (j + 1) % n, under + j] for j in range(n)] + [list(range(under, under + n))]
    f.add(pts, gores + [crown] + beneath, lines, "Bone003",
          normals=np.vstack([leaning(pts[:under]), np.tile([0.0, -1.0, 0.0], (n, 1))]))
    outer = [[face[0], face[j], face[j + 1]] for face in gores + [crown] for j in range(1, len(face) - 1)]
    return Surface(pts[np.array(outer)])


def oval(width: float, height: float, sides: int, turn: float = 0.0, point: float = 0.0) -> np.ndarray:
    """A closed outline: an ellipse of `sides`, its ends drawn to points by `point` (an almond)."""
    t = turn + 2 * math.pi * np.arange(sides) / sides
    x, y = np.cos(t), np.sin(t)
    y = y * (1 - point * np.abs(x) ** 2)
    return np.c_[width / 2 * x, height / 2 * y]


def spots(f: Figure, cap: Surface) -> None:
    """Faceted spots with uneven sides, so they read as a toadstool's spots rather than portholes."""
    for i, (azimuth, elevation, size, turn) in enumerate(SPOTS):
        sides = 6 - i % 2
        uneven = 1 + 0.18 * np.sin(np.arange(sides) * 2.4 + i)
        cap.print(f, azimuth, elevation, oval(size, size * 0.85, sides, turn) * uneven[:, None], SPOT_RISE)


def face(f: Figure, cap: Surface) -> None:
    """Two slanted almond eyes and a grin under them, high on the front of the cap, each one closed shape."""
    spread, elevation, width, height, slant = EYES
    for s in (-1, 1):
        turn = np.array([[math.cos(s * slant), math.sin(s * slant)], [-math.sin(s * slant), math.cos(s * slant)]])
        cap.print(f, s * spread, elevation, oval(width, height, 8, point=0.5) @ turn, 0.012)
    elevation, width, depth = MOUTH
    t = np.linspace(0, math.pi, 7)
    cap.print(f, 0, elevation, np.c_[width / 2 * np.cos(t), -depth * np.sin(t) + depth / 3], 0.012)


def stalk(f: Figure) -> None:
    """The stalk: a squat bulb on the ground, closed at both ends, rigid on the pelvis; under the lip its front shows."""
    n = STALK_SIDES
    pts = np.vstack([ring(y, r, n) for y, r in STALK])
    faces = [[i * n + j, i * n + (j + 1) % n, (i + 1) * n + (j + 1) % n, (i + 1) * n + j]
             for i in range(len(STALK) - 1) for j in range(n)]
    lines = [(face[j], face[(j + 1) % 4]) for face in faces for j in range(4)]
    faces += [list(range(n)), list(range((len(STALK) - 1) * n, len(STALK) * n))]
    f.add(pts, faces, lines, "MushroomPelvis")


def legs(f: Figure) -> None:
    """Each leg a root: a thick four-sided prism, a ridge along its top, from the bulb out to the cap's rim, bent at
    the knee, down to the ground and out to a blunt end."""
    for hip_bone, shin_bone, foot_bone, (sx, sz) in LEGS.values():
        d = np.array([sx, 0, sz]) / math.sqrt(2)
        side = np.array([-d[2], 0, d[0]])
        joints = [out * d + np.array([0, y, 0]) for out, y, _ in ROOT]
        rings = []
        for i, (c, (_, _, half)) in enumerate(zip(joints, ROOT)):
            along = joints[min(i + 1, 3)] - joints[max(i - 1, 0)]
            along /= np.linalg.norm(along)
            up = np.cross(along, side)
            up /= np.linalg.norm(up)
            rings.append(np.array([c + half * up, c + half * side, c - half * up, c - half * side]))
        pts = np.vstack(rings)
        faces = [[i * 4 + j, i * 4 + (j + 1) % 4, (i + 1) * 4 + (j + 1) % 4, (i + 1) * 4 + j]
                 for i in range(len(rings) - 1) for j in range(4)]
        faces += [[0, 1, 2, 3], [12, 13, 14, 15]]
        # its long edges and its end drawn, not the rings at its joints, which read as scratches across it
        lines = [(i * 4 + j, (i + 1) * 4 + j) for i in range(len(rings) - 1) for j in range(4)]
        lines += [(12 + j, 12 + (j + 1) % 4) for j in range(4)]
        # the vendor's leg is a short shin standing under the body and a long root of a foot along the ground: the
        # new leg rises from the shin at the bulb, and from the knee out it is the foot
        weights = ([{shin_bone: 0.6, hip_bone: 0.4}] * 4 + [{foot_bone: 0.7, shin_bone: 0.3}] * 4
                   + [{foot_bone: 1.0}] * 8)
        f.add(pts, faces, lines, weights)


def build(source: Source) -> Mesh:
    f = Figure(source)
    surface = cap(f)
    spots(f, surface)
    face(f, surface)
    stalk(f)
    legs(f)
    return f.mesh()
