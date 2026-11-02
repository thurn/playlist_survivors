"""An edge cut as a recipe builds it (design.md, The Look > Edge cuts; plan.md, Rendering > Edge cuts): a dark body
of closed parts (convex hulls of the source's parts, discs where a primitive replaces one), lines (chosen feature
lines of the source, circles and strokes) and the shard pieces both break into. Every vertex keeps a skin of 4
entries; primitives are rigid on the bone they replace. `write` exports it as cut.json/.bytes for the baker."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass, field
from itertools import pairwise
from pathlib import Path

import numpy as np
from scipy.spatial import ConvexHull

from . import arrays, lines
from .source import Selection, Source

VERSION = 1


def fibonacci_sphere(n: int) -> np.ndarray:
    i = np.arange(n) + 0.5
    z = 1 - 2 * i / n
    r = np.sqrt(1 - z * z)
    a = np.pi * (1 + 5**0.5) * i
    return np.stack([r * np.cos(a), z, r * np.sin(a)], 1)


def support_points(points: np.ndarray, count: int) -> np.ndarray:
    """Indices of up to `count` of the points that are furthest along evenly spread directions: the corners of a
    simplified convex hull."""
    centred = points - points.mean(0)
    best = np.array([], np.int64)
    for n in range(count, 40 * count, max(1, count // 2)):
        picks = np.unique((centred @ fibonacci_sphere(n).T).argmax(0))
        if len(picks) > count:
            break
        best = picks
    return best


def plane_basis(normal: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    helper = np.array([0.0, 1.0, 0.0]) if abs(normal[1]) < 0.9 else np.array([1.0, 0.0, 0.0])
    u = np.cross(normal, helper)
    u /= np.linalg.norm(u)
    return u, np.cross(normal, u)


@dataclass
class Cut:
    src: Source
    body_positions: list[np.ndarray] = field(default_factory=list)
    body_bones: list[np.ndarray] = field(default_factory=list)
    body_weights: list[np.ndarray] = field(default_factory=list)
    body_triangles: list[np.ndarray] = field(default_factory=list)
    body_part: list[str] = field(default_factory=list)  # per triangle
    line_positions: list[np.ndarray] = field(default_factory=list)
    line_bones: list[np.ndarray] = field(default_factory=list)
    line_weights: list[np.ndarray] = field(default_factory=list)
    segments: list[tuple[int, int]] = field(default_factory=list)
    segment_importance: list[float] = field(default_factory=list)
    segment_part: list[str] = field(default_factory=list)
    hulls: list[tuple[np.ndarray, np.ndarray]] = field(default_factory=list)  # (inward normals, offsets) per hull
    piece_of_triangle: np.ndarray | None = None
    piece_of_segment: np.ndarray | None = None
    pieces: list[dict] = field(default_factory=list)

    @property
    def triangle_count(self) -> int:
        return sum(len(t) for t in self.body_triangles)

    @property
    def segment_count(self) -> int:
        return len(self.segments)

    def _body(self, positions, bones, weights, triangles, part: str) -> None:
        base = sum(len(p) for p in self.body_positions)
        self.body_positions.append(np.asarray(positions, np.float64))
        self.body_bones.append(np.asarray(bones, np.int64))
        self.body_weights.append(np.asarray(weights, np.float64))
        self.body_triangles.append(np.asarray(triangles, np.int64) + base)
        self.body_part += [part] * len(triangles)

    def _line_vertex(self, position, bones, weights) -> int:
        self.line_positions.append(np.asarray(position, np.float64))
        self.line_bones.append(np.asarray(bones, np.int64))
        self.line_weights.append(np.asarray(weights, np.float64))
        return len(self.line_positions) - 1

    def hull(self, sel: Selection, part: str, points: int = 14, inset: float = 0.006) -> None:
        """A closed convex body over the selection: the hull of its `points` most extreme vertices, each pulled
        `inset` (a share of the height) toward the centre so the lines on its surface stay in front of it. Its
        corners are source vertices and keep their skin."""
        v = self.src.vertices_of(sel)
        if len(v) < 4:
            raise ValueError(f"{self.src.model}: part {part} has {len(v)} vertices, too few for a hull")
        pts = self.src.positions[v]
        corners = v[support_points(pts, points)]
        p = self.src.positions[corners]
        centre = p.mean(0)
        hull = ConvexHull(p)
        tris = hull.simplices.copy()
        normals = np.cross(p[tris[:, 1]] - p[tris[:, 0]], p[tris[:, 2]] - p[tris[:, 0]])
        outward = (normals * (p[tris[:, 0]] - centre)).sum(1) < 0
        tris[outward] = tris[outward][:, ::-1]
        used = np.unique(tris)
        remap = np.full(len(p), -1)
        remap[used] = np.arange(len(used))
        shrunk = p[used] + (centre - p[used]) * (inset * self.src.height / np.maximum(
            np.linalg.norm(centre - p[used], axis=1, keepdims=True), 1e-9))
        self._body(shrunk, self.src.bones[corners[used]], self.src.weights[corners[used]], remap[tris], part)
        eq = hull.equations  # outward normals n·x + d <= 0 inside
        self.hulls.append((eq[:, :3], -eq[:, 3]))

    def disc(self, centre, normal, radius: float, entry: int, part: str, sides: int = 16) -> None:
        """A flat disc body (drawn from both sides), rigid on `entry`."""
        u, w = plane_basis(np.asarray(normal, np.float64) / np.linalg.norm(normal))
        a = np.linspace(0, 2 * np.pi, sides, endpoint=False)
        ring = np.asarray(centre) + radius * (np.cos(a)[:, None] * u + np.sin(a)[:, None] * w)
        pos = np.vstack([np.asarray(centre)[None], ring])
        tris = np.array([[0, 1 + i, 1 + (i + 1) % sides] for i in range(sides)])
        self._body(pos, self._rigid_bones(entry, len(pos)), self._rigid_weights(len(pos)), tris, part)

    def circle(self, centre, normal, radius: float, entry: int, part: str, sides: int = 16,
               importance: float = 1.0) -> None:
        """A ring of `sides` segments, rigid on `entry` (a shield's circle)."""
        u, w = plane_basis(np.asarray(normal, np.float64) / np.linalg.norm(normal))
        a = np.linspace(0, 2 * np.pi, sides, endpoint=False)
        ids = [self._line_vertex(np.asarray(centre) + radius * (np.cos(t) * u + np.sin(t) * w), [entry, 0, 0, 0],
                                 [1, 0, 0, 0]) for t in a]
        for i in range(sides):
            self._segment(ids[i], ids[(i + 1) % sides], importance, part)

    def stroke(self, points, entry: int | Sequence[int], part: str, importance: float = 1.0) -> None:
        """A polyline through `points`, rigid on `entry` (a weapon's strokes), or each point rigid on its own entry
        (a wing's bones, the line bending at their joints)."""
        entries = [entry] * len(points) if isinstance(entry, int) else list(entry)
        if len(entries) != len(points):
            raise ValueError(f"{self.src.model}: stroke {part} has {len(points)} points and {len(entries)} entries")
        ids = [self._line_vertex(p, [e, 0, 0, 0], [1, 0, 0, 0]) for p, e in zip(points, entries, strict=True)]
        for a, b in pairwise(ids):
            self._segment(a, b, importance, part)

    def contours(self, sel: Selection, part: str, sides: int = 4, importance: float = 0.5) -> None:
        """Straight strokes along the selection's long axis at its surface, `sides` of them evenly round it, rigid on
        the entry holding most of its skin: from any heading about two lie near its outline. For a chunky low-poly
        limb, whose mesh has no long edge along it to choose."""
        pts = self.src.positions[self.src.vertices_of(sel)]
        centre = pts.mean(0)
        rel = pts - centre
        _, _, axes = np.linalg.svd(rel, full_matrices=False)
        along = rel @ axes[0]
        lo, hi = np.percentile(along, 4), np.percentile(along, 96)  # past the joints' stray vertices
        entry = self.src.dominant_entry(sel)
        for k in range(sides):
            a = 2 * np.pi * (k + 0.5) / sides
            d = np.cos(a) * axes[1] + np.sin(a) * axes[2]
            r = np.percentile(rel @ d, 92)
            self.stroke([centre + lo * axes[0] + r * d, centre + hi * axes[0] + r * d], entry, part, importance)

    def _segment(self, a: int, b: int, importance: float, part: str) -> None:
        self.segments.append((a, b))
        self.segment_importance.append(importance)
        self.segment_part.append(part)

    @staticmethod
    def _rigid_bones(entry: int, n: int) -> np.ndarray:
        return np.tile([entry, 0, 0, 0], (n, 1))

    @staticmethod
    def _rigid_weights(n: int) -> np.ndarray:
        return np.tile([1.0, 0, 0, 0], (n, 1))

    def feature_lines(self, parts: dict[str, Selection], count: int, **options) -> list[lines.Chain]:
        """The source's chosen feature lines over the named parts (lines.choose; call after the hulls, which hide
        what lies deep inside them), up to `count` segments, each skinned as its source vertex."""
        names = list(parts)
        face_part = np.zeros(self.src.triangle_count, np.int64)
        union = self.src.none()
        for i, name in enumerate(names):
            face_part[parts[name].mask] = i
            union = union | parts[name]
        proxy, picked = lines.choose(self.src, union, face_part, names, count, hulls=self.hulls, **options)
        for c in picked:
            ids = [self._line_vertex(proxy.raw[v], self.src.bones[proxy.source_vertex[v]],
                                     self.src.weights[proxy.source_vertex[v]]) for v in c.vertices]
            for a, b in pairwise(ids):
                self._segment(a, b, c.importance, c.part)
        return picked

    def break_into(self, splits: dict[str, int] | None = None) -> None:
        """The shard pieces (plan.md step 7): each part one piece, or `splits[part]` pieces by k-means over its
        triangles; every segment joins the piece of its part nearest its middle. Each piece turns about its
        centroid, posed by the entry holding most of its skin."""
        positions = np.vstack(self.body_positions)
        tris = np.vstack(self.body_triangles)
        centroid = positions[tris].mean(1)
        bones, weights = np.vstack(self.body_bones), np.vstack(self.body_weights)
        piece = np.zeros(len(tris), np.int64)
        part_pieces: dict[str, list[int]] = {}
        k = 0
        for part in dict.fromkeys(self.body_part):
            idx = np.flatnonzero(np.array(self.body_part) == part)
            n = min((splits or {}).get(part, 1), len(idx))
            labels = kmeans(centroid[idx], n) if n > 1 else np.zeros(len(idx), np.int64)
            for j in range(n):
                piece[idx[labels == j]] = k + j
            part_pieces[part] = list(range(k, k + n))
            k += n
        line_pos = np.array(self.line_positions) if self.line_positions else np.zeros((0, 3))
        seg_piece = np.zeros(len(self.segments), np.int64)
        piece_centres = np.array([centroid[piece == j].mean(0) for j in range(k)])
        for s, (a, b) in enumerate(self.segments):
            mid = 0.5 * (line_pos[a] + line_pos[b])
            candidates = part_pieces.get(self.segment_part[s], range(k))
            seg_piece[s] = min(candidates, key=lambda j: np.linalg.norm(piece_centres[j] - mid))
        self.pieces = []
        for j in range(k):
            verts = np.unique(tris[piece == j])
            pts = [positions[verts]]
            segs = np.flatnonzero(seg_piece == j)
            if len(segs):
                pts.append(line_pos[np.array(self.segments)[segs].reshape(-1)])
            allp = np.vstack(pts)
            pivot = allp.mean(0)
            score = np.zeros(len(self.src.entries))
            np.add.at(score, bones[verts].reshape(-1), weights[verts].reshape(-1))
            self.pieces.append({"pivot": pivot, "size": float(np.linalg.norm(allp - pivot, axis=1).max()),
                                "entry": int(score.argmax())})
        self.piece_of_triangle, self.piece_of_segment = piece, seg_piece

    def summary(self) -> dict:
        parts = {}
        for p in self.body_part:
            parts.setdefault(p, {"triangles": 0, "segments": 0})["triangles"] += 1
        for p in self.segment_part:
            parts.setdefault(p, {"triangles": 0, "segments": 0})["segments"] += 1
        return {"triangles": self.triangle_count, "segments": self.segment_count, "pieces": len(self.pieces),
                "parts": parts}

    def write(self, header: Path) -> None:
        """cut.json/.bytes: the body unwelded per piece (a vertex shared by two pieces is split), the lines'
        vertices and segments with importance and piece, and the pieces' pivots, sizes and entries."""
        if self.piece_of_triangle is None:
            self.break_into()
        positions = np.vstack(self.body_positions)
        bones, weights = np.vstack(self.body_bones), np.vstack(self.body_weights)
        tris = np.vstack(self.body_triangles)
        key = tris * len(self.pieces) + self.piece_of_triangle[:, None]
        uniq, inverse = np.unique(key.reshape(-1), return_inverse=True)
        source = uniq // len(self.pieces)
        piece_of_vertex = uniq % len(self.pieces)
        meta = {"version": VERSION, "model": self.src.model, "heightM": self.src.height, **self.summary()}
        arrays.write(header, meta, {
            "body_positions": positions[source],
            "body_bones": bones[source],
            "body_weights": weights[source],
            "body_piece": piece_of_vertex,
            "body_triangles": inverse.reshape(-1, 3),
            "line_positions": np.array(self.line_positions),
            "line_bones": np.array(self.line_bones),
            "line_weights": np.array(self.line_weights),
            "line_segments": np.array(self.segments, np.int64),
            "line_importance": np.array(self.segment_importance),
            "line_piece": self.piece_of_segment,
            "piece_pivots": np.array([p["pivot"] for p in self.pieces]),
            "piece_sizes": np.array([p["size"] for p in self.pieces]),
            "piece_entries": np.array([p["entry"] for p in self.pieces], np.int64),
        })


def kmeans(points: np.ndarray, k: int, iterations: int = 30) -> np.ndarray:
    """Deterministic k-means labels: seeded by the farthest-point rule from the first point."""
    centres = [points[0]]
    for _ in range(1, k):
        d = np.min([np.linalg.norm(points - c, axis=1) for c in centres], axis=0)
        centres.append(points[int(d.argmax())])
    c = np.array(centres)
    labels = np.zeros(len(points), np.int64)
    for _ in range(iterations):
        labels = np.linalg.norm(points[:, None] - c[None], axis=2).argmin(1)
        c = np.array([points[labels == j].mean(0) if (labels == j).any() else c[j] for j in range(k)])
    return labels
