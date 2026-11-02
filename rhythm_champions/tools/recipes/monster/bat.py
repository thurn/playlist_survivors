"""The bat (ps-6y2.33): rebuilt from a few simple shapes, its wings membranes whose fingers and scalloped edges are
its lines.

The trailer's drawing of the vendor mesh reads as a bat in a crowd by its finger-spoked, scalloped wings, but it draws
as an X-ray sketch rather than a faceted model: every finger bone two or three hairlines, the body, head, teeth and
feet a knot of short strokes (at game size a bright smudge), the ears, pointing up and back, foreshortened to stubs
from the game's camera, and the wings' curved membranes washed with the fresnel rim. A 0.3 m swarmer is a few dozen
pixels at game size, so its wings' shape is nearly all it has. So the whole figure is new, every line chosen (a true
wireframe of its topology), 182 triangles at every LOD where the vendor's are 3,092, 1,662 and 832:

- The wings: each a thin plate between the vendor's bones (a top and a bottom layer, a rim between), drawing its
  leading edge from the shoulder to the wrist, a stubby claw at the wrist, three fingers from the wrist to their tips
  and a fold from the elbow, and a trailing edge that sweeps in a curve between each pair of points; no line inside a
  panel. Skinned by transfer from the vendor's membrane (its four nearest vertices of the arm, hand and body bones on
  its side), so it flaps with the vendor's clips. Never the legs: a membrane that followed them drew two spikes when
  they hang.
- The body: a long spindle of diamond rings, broader than the vendor's, the wings' roots inside its shoulders and its
  sides above them, drawn by its four long edges (its rings crossed the ridge in an X).
- The head: a double wedge pointed at the snout, holding the body's neck, its back on the neck's bone so a bite
  that stretches the neck doesn't leave it floating. The ears: tall three-sided spikes, up and back as the vendor's.
  Both a quarter larger than the vendor's: smaller, they were a knot too small to read as a head at game size.
- Dropped: the tail membrane (its outline closed into a ring over the head in the hovering pose), the legs and feet
  (hairline spikes at game size, too thin to draw solid), the eyes and the teeth.
- Every triangle on its own corners with its facet's normal: normals smoothed across a wing's fold lit the panel
  facing the camera as a glowing wedge under the fresnel rim.

Tried and dropped: a wing split into four times the triangles (it bent more smoothly, and its panels glowed all over
and its shards broke into scratches), deeper scallops (the wings read as zigzag bars), ears tilted out (a crown below
the head), and legs.

Source: Magic Pig Games' Bats LP.prefab (sha256 dcb6e090ea21acaad3efd3cf9264c2fedd7f543b5a767cecb7c5a38c6dd39611) and
Bats LP.fbx (e06900ac2ca26a8d9a4af69398069851eb8821d6af68f3bcad4594b57e0160ec), as manifest.json lists them. No
Blender: numpy only. Bake: `just champions-tool monster bake bat`; review: `just champions-tool monster review bat`
(and `--look trailer`).
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

# The wing's landmarks on the right side, (x, z) in the rest pose, where the wings lie spread flat (x out, z toward the
# head), measured from the vendor bones: the shoulder S, the wrist W, the thumb's claw TH rising from the leading edge
# at Wa, each finger's knuckle K and tip T (FT the third's), the elbow E and the strut from it to T4 (the membrane's
# fold, where the vendor's legs held it), and the hip H, where the trailing edge runs into the body.
WING = {
    "S": (0.06, 0.05), "Wa": (0.28, 0.055), "W": (0.36, 0.05), "TH": (0.38, 0.15),
    "K1": (0.583, -0.001), "T1": (0.738, -0.2),
    "K2": (0.551, -0.11), "T2": (0.681, -0.318),
    "K3": (0.445, -0.19), "FT": (0.503, -0.393),
    "E": (0.16, -0.04), "T4": (0.27, -0.3), "H": (0.045, -0.15),
}


def scallop(a: str, b: str, n: int, depth: float) -> dict[str, tuple[float, float]]:
    """The trailing edge between two landmarks: n points along it, bowed in toward the wrist by up to `depth` of the
    way there, so the edge sweeps in a curve between the finger tips, as a bat's membrane does. Named a1, a2..."""
    points = {}
    for i in range(1, n + 1):
        t = i / (n + 1)
        p = np.add(np.multiply(WING[a], 1 - t), np.multiply(WING[b], t))
        points[f"{a}{i}"] = tuple(p + depth * 4 * t * (1 - t) * (np.asarray(WING["W"]) - p))
    return points


WING |= (scallop("T1", "T2", 1, 0.15) | scallop("T2", "FT", 2, 0.16) | scallop("FT", "T4", 2, 0.14)
         | scallop("T4", "H", 1, 0.12))
WING_TRIANGLES = [
    ("Wa", "TH", "W"),
    ("W", "K1", "K2"), ("K1", "T1", "T11"), ("K1", "T11", "K2"), ("K2", "T11", "T2"),
    ("W", "K2", "K3"), ("K2", "T2", "T21"), ("K2", "T21", "K3"), ("K3", "T21", "T22"), ("K3", "T22", "FT"),
    ("S", "Wa", "E"), ("Wa", "W", "E"), ("W", "K3", "E"), ("E", "K3", "FT1"), ("K3", "FT", "FT1"),
    ("E", "FT1", "FT2"), ("E", "FT2", "T4"), ("E", "T4", "T41"), ("E", "T41", "H"), ("S", "E", "H"),
]
# The lines: the leading edge and the thumb's claw, the three fingers and the fold, and the scalloped trailing edge.
TRAILING = ["T1", "T11", "T2", "T21", "T22", "FT", "FT1", "FT2", "T4", "T41", "H"]
WING_LINES = [
    ("S", "Wa"), ("Wa", "TH"), ("TH", "W"), ("W", "K1"), ("K1", "T1"), ("W", "K2"), ("K2", "T2"), ("W", "K3"),
    ("K3", "FT"), ("E", "T4"), *pairwise(TRAILING),
]
# Skinned to the arm, the hand and the body, never the legs: the legs hang below the membrane in some poses, and a
# membrane that followed them drew two spikes.
WING_BONES = ["Collarbone", "Arm1", "Arm2", "ArmPalm", "Thumb1", "Thumb2", "Wing1_", "Wing2_", "Flap", "Ribcage",
              "Spine", "Pelvis"]

BODY_BONES = ["Pelvis", "Spine", "Ribcage", "Neck", "Tail1"]


class Figure:
    """The new mesh as it is built: shapes skinned by transfer from the vendor vertices of the bones they replace."""

    def __init__(self, source: Source):
        self.source = source
        self.dominant = source.bones[np.arange(len(source.positions)), source.weights.argmax(axis=1)]
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.edges: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.weights: list[np.ndarray] = []
        self.count = 0

    def mesh(self) -> Mesh:
        """The figure as a mesh, every triangle with its own corners and its facet's normal: the fresnel rim then
        lights a facet by its own turn from the camera, where normals smoothed across a wing's fold lit the panel
        facing the camera as a glowing wedge."""
        positions, triangles = np.vstack(self.positions), np.vstack(self.triangles)
        corners = triangles.reshape(-1)
        p = positions[triangles]
        normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        normal /= np.linalg.norm(normal, axis=1)[:, None]
        return Mesh(positions[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    np.vstack(self.bones)[corners], np.vstack(self.weights)[corners],
                    normals=np.repeat(normal, 3, axis=0).astype(np.float32), edges=np.vstack(self.edges))

    def region(self, bones: list[str], side: int) -> tuple[np.ndarray, np.ndarray]:
        """The vendor bones whose names hold one of `bones` (a side's limbs only on that side; 0 for both sides), and
        the vendor vertices they lead on that side of the middle: a shape is skinned from these, to these bones only."""
        names = self.source.bone_names
        own = {1: ("BatR", "BatFlapR"), -1: ("BatL", "BatFlapL"), 0: ("BatR", "BatL", "BatFlapR", "BatFlapL")}[side]
        chosen = np.array([i for i, n in enumerate(names) if any(b in n for b in bones)
                           and (not n.startswith(("BatR", "BatL", "BatFlapR", "BatFlapL")) or n.startswith(own))])
        keep = np.isin(self.dominant, chosen)
        if side:
            keep &= self.source.positions[:, 0] * side > -0.005
        return np.nonzero(keep)[0], chosen

    def skin(self, points: np.ndarray, region: tuple[np.ndarray, np.ndarray], k: int = 4):
        """Bones and weights for `points` from their k nearest vendor vertices in `region`, by inverse distance, to the
        region's bones only, the four strongest kept."""
        among, allowed = region
        src = self.source
        pos = src.positions[among].astype(np.float64)
        bones = np.zeros((len(points), 4), np.int32)
        weights = np.zeros((len(points), 4), np.float32)
        for i, p in enumerate(points):
            d = np.linalg.norm(pos - p, axis=1)
            near = np.argsort(d)[:k]
            w = 1 / (d[near] + 1e-3)
            acc: dict[int, float] = {}
            for v, wv in zip(among[near], w / w.sum()):
                for b, bw in zip(src.bones[v], src.weights[v]):
                    if b in allowed:
                        acc[int(b)] = acc.get(int(b), 0.0) + float(wv * bw)
            top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
            total = sum(bw for _, bw in top)
            for j, (b, bw) in enumerate(top):
                bones[i, j], weights[i, j] = b, bw / total
        return bones, weights

    def add(self, points, faces, lines, region, orient: bool = True) -> None:
        """A shape: `faces` convex polygons over `points` (wound outward from the points' mean if `orient`, else as
        given), `lines` the pairs of points whose edges draw, skinned from `region`."""
        pts = np.asarray(points, np.float64)
        centre = pts.mean(axis=0)
        drawn = {frozenset(e) for e in lines}
        tris, flags = [], []
        for f in faces:
            for j in range(1, len(f) - 1):
                tri = [f[0], f[j], f[j + 1]]
                a, b, d = pts[tri]
                if orient and np.dot(np.cross(b - a, d - a), (a + b + d) / 3 - centre) < 0:
                    tri = [tri[0], tri[2], tri[1]]
                tris.append(tri)
                flags.append([int(frozenset((tri[(k + 1) % 3], tri[(k + 2) % 3])) in drawn) for k in range(3)])
        bones, weights = self.skin(pts, region)
        self.positions.append(pts)
        self.triangles.append(np.asarray(tris, np.int64) + self.count)
        self.edges.append(np.asarray(flags, np.uint8))
        self.bones.append(bones)
        self.weights.append(weights)
        self.count += len(pts)

    def solid(self, rings, region, ring_lines: bool = True) -> None:
        """A closed shape lofted through rings of equal size (a ring may be one point, an apex), every polygon edge a
        line (none of a quad's diagonals), a low-poly wireframe; without `ring_lines`, only the edges along it."""
        pts, offsets, sizes = [], [], []
        for r in rings:
            r = np.atleast_2d(r)
            offsets.append(len(pts))
            sizes.append(len(r))
            pts.extend(r)
        faces = []
        n = max(sizes)
        for i in range(len(rings) - 1):
            a, b, na, nb = offsets[i], offsets[i + 1], sizes[i], sizes[i + 1]
            for k in range(n):
                if na == 1:
                    faces.append([a, b + k, b + (k + 1) % nb])
                elif nb == 1:
                    faces.append([a + k, a + (k + 1) % na, b])
                else:
                    faces.append([a + k, a + (k + 1) % na, b + (k + 1) % nb, b + k])
        for i, (o, s) in enumerate(zip(offsets, sizes)):
            if s > 1 and i in (0, len(rings) - 1):
                faces.append(list(range(o, o + s)))
        lines = [(f[j], f[(j + 1) % len(f)]) for f in faces for j in range(len(f))]
        ring = np.repeat(np.arange(len(rings)), sizes)
        self.add(pts, faces, [e for e in lines if ring_lines or ring[e[0]] != ring[e[1]]], region)

    def membrane(self, plan: dict, triangles, lines, region, thickness: float = 0.008) -> None:
        """A thin plate over `plan`'s points ((x, z), y taken from the vendor's surface there): a top and a bottom
        layer, each drawing `lines`, closed by a rim round its outline, which draws nothing (seen edge-on, it is the
        outline's own width)."""
        names = list(plan)
        xz = np.array([plan[n] for n in names], np.float64)
        src = self.source.positions[region[0]].astype(np.float64)
        y = np.array([src[np.argsort(np.linalg.norm(src[:, [0, 2]] - p, axis=1))[:6], 1].mean() for p in xz])
        mid = np.c_[xz[:, 0], y, xz[:, 1]]
        index = {n: i for i, n in enumerate(names)}
        tris = [[index[a] for a in t] for t in triangles]
        drawn = [(index[a], index[b]) for a, b in lines]
        n = len(names)
        up = np.array([0, thickness / 2, 0])
        # Each layer wound to face out: the top up, the bottom down.
        top = [t if np.cross(mid[t[1]] - mid[t[0]], mid[t[2]] - mid[t[0]])[1] > 0 else [t[0], t[2], t[1]] for t in tris]
        bottom = [[t[0] + n, t[2] + n, t[1] + n] for t in top]
        faces = top + bottom
        lines_all = drawn + [(a + n, b + n) for a, b in drawn]
        # The rim: a quad on each outline edge (an edge of one triangle), outward of the top's winding.
        uses: dict[frozenset, list] = {}
        for t in top:
            for k in range(3):
                uses.setdefault(frozenset((t[k], t[(k + 1) % 3])), []).append((t[k], t[(k + 1) % 3]))
        for pairs in uses.values():
            if len(pairs) == 1:
                a, b = pairs[0]
                faces.append([b, a, a + n, b + n])
        self.add(np.vstack([mid + up, mid - up]), faces, lines_all, region, orient=False)


def wing(f: Figure, side: int) -> None:
    plan = {n: (side * x, z) for n, (x, z) in WING.items()}
    f.membrane(plan, WING_TRIANGLES, WING_LINES, f.region(WING_BONES, side))


def diamond(z: float, half_width: float, top: float, side: float, bottom: float) -> np.ndarray:
    """A ring of four at z: right and left at height `side`, top and bottom."""
    return np.array([(half_width, side, z), (0, top, z), (-half_width, side, z), (0, bottom, z)])


def body(f: Figure) -> None:
    """The body: a long spindle of diamond rings, the neck's (inside the head, which it holds), the chest's and the
    pelvis's, pointed at the tail; broader than the vendor's, so the wings' roots sit inside its shoulders, and its
    sides above the wings, so its outline draws over them from above. Drawn by its four long edges, a ridge down its
    back: its rings, drawn too, crossed the ridge in an X."""
    rings = [diamond(0.17, 0.034, 0.04, 0.012, -0.03), diamond(0.05, 0.085, 0.085, 0.03, -0.075),
             diamond(-0.11, 0.058, 0.058, 0.026, -0.05), np.array([(0, 0.012, -0.23)])]
    f.solid(rings, f.region(BODY_BONES, 0), ring_lines=False)


# The head and ears are a quarter larger than the vendor's, grown from the neck: smaller, they were a knot too small
# to read as a head at game size.
NECK = np.array([0.0, 0.01, 0.15])


def grown(points) -> np.ndarray:
    return NECK + 1.25 * (np.asarray(points, np.float64) - NECK)


def head(f: Figure) -> None:
    """The head: a double wedge, widest across the brow between the ears, pointed at the snout and, behind, at the
    neck, whose bone it follows there, so it stays joined to the body when the bite stretches the neck."""
    rings = [NECK[None], diamond(0.228, 0.076, 0.082, 0.028, -0.044), np.array([(0, -0.002, 0.318)])]
    f.solid([grown(r) for r in rings], f.region(["BatHead", "BatNeck"], 0))


def ears(f: Figure) -> None:
    """The ears: each a tall three-sided spike from the top of the head, up and back as the vendor's leaves point."""
    tip = np.array([0.15, 0.27, 0.16])
    base = np.array([(0.028, 0.076, 0.245), (0.084, 0.056, 0.235), (0.056, 0.074, 0.168)])
    for side, s in ((1, "R"), (-1, "L")):
        f.solid([grown(base * [side, 1, 1]), grown(tip[None] * [side, 1, 1])], f.region([f"BatEar{s}"], side))


def build(source: Source) -> Mesh:
    f = Figure(source)
    wing(f, 1)
    wing(f, -1)
    body(f)
    head(f)
    ears(f)
    return f.mesh()
