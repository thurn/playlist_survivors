"""Choosing a model's lines (design.md, The Look > Edge cuts; plan.md, Rendering > Edge cuts, step 5).

Candidates come from a smoothed proxy of the source, in its pose: its edges that the game camera sees on the silhouette
from many headings (the creases that shape the outline), its sharp creases and its open rims. They chain into polylines,
simplified, each scored by its length and salience; edges the body hides from every view are dropped. The recipe's
keep and drop lists and its count pick the lines, and each gets an importance from its score, so the renderer fades
the least important first when the monster is small on screen.
"""

from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from .source import Selection, Source


def view_directions(pitches=(50.0, 60.0, 70.0), headings: int = 24) -> np.ndarray:
    """Unit directions from the camera toward the model: looking down at each pitch, from every heading."""
    out = []
    for pitch in np.radians(pitches):
        for yaw in np.linspace(0, 2 * np.pi, headings, endpoint=False):
            out.append([np.cos(pitch) * np.sin(yaw), -np.sin(pitch), np.cos(pitch) * np.cos(yaw)])
    return np.array(out)


@dataclass
class Chain:
    """One chosen polyline: welded vertex ids along it (after simplification), its part, score and length."""

    vertices: list[int]
    part: str
    score: float
    length: float
    importance: float = 0.0

    @property
    def segments(self) -> int:
        return len(self.vertices) - 1


@dataclass
class Proxy:
    """The selection welded by position: vertices (raw, and smoothed in the source's pose), faces, edges with their
    faces, and the source vertex each welded vertex stands for (for its skin)."""

    raw: np.ndarray
    smooth: np.ndarray
    source_vertex: np.ndarray
    faces: np.ndarray
    face_part: np.ndarray
    edges: np.ndarray
    edge_faces: list[list[int]] = field(default_factory=list)


def weld(src: Source, sel: Selection, face_part: np.ndarray, tolerance: float, smooth_iterations: int) -> Proxy:
    tris = src.triangles[sel.mask]
    parts = face_part[sel.mask]
    used = np.unique(tris)
    key = np.round(src.positions[used] / tolerance).astype(np.int64)
    _, first, welded = np.unique(key, axis=0, return_index=True, return_inverse=True)
    remap = np.full(len(src.positions), -1, np.int64)
    remap[used] = welded.reshape(-1)
    faces = remap[tris]
    ok = (faces[:, 0] != faces[:, 1]) & (faces[:, 1] != faces[:, 2]) & (faces[:, 0] != faces[:, 2])
    faces, parts = faces[ok], parts[ok]
    raw = src.positions[used[first]]
    posed = src.posed[used[first]]
    pairs = np.sort(np.concatenate([faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]]), axis=1)
    edges, inverse = np.unique(pairs, axis=0, return_inverse=True)
    inverse = inverse.reshape(-1)
    face_of = np.tile(np.arange(len(faces)), 3)
    edge_faces: list[list[int]] = [[] for _ in range(len(edges))]
    for e, f in zip(inverse, face_of, strict=True):
        edge_faces[e].append(int(f))
    smooth = taubin(posed, edges, smooth_iterations)
    return Proxy(raw, smooth, used[first], faces, parts, edges, edge_faces)


def taubin(points: np.ndarray, edges: np.ndarray, iterations: int, lam: float = 0.5, mu: float = -0.53) -> np.ndarray:
    """Taubin smoothing over the mesh's edges: removes small detail without shrinking the whole."""
    p = points.copy()
    n = len(p)
    degree = np.bincount(edges.reshape(-1), minlength=n).astype(np.float64)
    degree[degree == 0] = 1
    for _ in range(iterations):
        for factor in (lam, mu):
            acc = np.zeros_like(p)
            np.add.at(acc, edges[:, 0], p[edges[:, 1]])
            np.add.at(acc, edges[:, 1], p[edges[:, 0]])
            p = p + factor * (acc / degree[:, None] - p)
    return p


def face_normals(points: np.ndarray, faces: np.ndarray) -> np.ndarray:
    n = np.cross(points[faces[:, 1]] - points[faces[:, 0]], points[faces[:, 2]] - points[faces[:, 0]])
    length = np.linalg.norm(n, axis=1, keepdims=True)
    return n / np.maximum(length, 1e-12)


def salience(proxy: Proxy, views: np.ndarray, crease_deg: float, rim: float) -> np.ndarray:
    """Per edge: the share of views it is on the proxy's silhouette from, plus its crease's strength beyond
    `crease_deg` (half weight); an open rim (one face) scores `rim`."""
    normals = face_normals(proxy.smooth, proxy.faces)
    sal = np.zeros(len(proxy.edges))
    two = [i for i, f in enumerate(proxy.edge_faces) if len(f) >= 2]
    one = [i for i, f in enumerate(proxy.edge_faces) if len(f) == 1]
    if two:
        f = np.array([proxy.edge_faces[i][:2] for i in two])
        n1, n2 = normals[f[:, 0]], normals[f[:, 1]]
        d1, d2 = n1 @ views.T, n2 @ views.T
        silhouette = (np.sign(d1) != np.sign(d2)).mean(1)
        angle = np.degrees(np.arccos(np.clip((n1 * n2).sum(1), -1, 1)))
        crease = np.clip((angle - crease_deg) / (180 - crease_deg), 0, 1)
        sal[two] = silhouette + 0.5 * crease
    sal[one] = rim
    return sal


def hidden(points: np.ndarray, hulls: list[tuple[np.ndarray, np.ndarray]], depth: float) -> np.ndarray:
    """Whether each point lies deeper than `depth` inside any of the convex hulls (inward planes n·p <= d)."""
    out = np.zeros(len(points), bool)
    for normals, offsets in hulls:
        inside = (points @ normals.T - offsets).max(1) < -depth
        out |= inside
    return out


def chains(proxy: Proxy, chosen: np.ndarray, max_turn_deg: float) -> list[list[int]]:
    """Walks the chosen edges into polylines, continuing through a vertex shared by exactly two chosen edges when
    the line turns there by less than `max_turn_deg`."""
    edges = proxy.edges[chosen]
    at: dict[int, list[int]] = {}
    for i, (a, b) in enumerate(edges):
        at.setdefault(int(a), []).append(i)
        at.setdefault(int(b), []).append(i)
    cos_max = np.cos(np.radians(max_turn_deg))

    def continues(v: int, came: int) -> int | None:
        nxt = [e for e in at[v] if e != came]
        if len(at[v]) != 2 or not nxt:
            return None
        e2 = nxt[0]
        a = proxy.raw[v] - proxy.raw[other(came, v)]
        b = proxy.raw[other(e2, v)] - proxy.raw[v]
        cos = a @ b / max(np.linalg.norm(a) * np.linalg.norm(b), 1e-12)
        return e2 if cos >= cos_max else None

    def other(e: int, v: int) -> int:
        a, b = edges[e]
        return int(b if a == v else a)

    used = np.zeros(len(edges), bool)
    out = []
    for start in range(len(edges)):
        if used[start]:
            continue
        used[start] = True
        line = [int(edges[start][0]), int(edges[start][1])]
        for end in (1, 0):  # grow forward from the last vertex, then backward from the first
            e = start
            while True:
                v = line[-1] if end == 1 else line[0]
                nxt = continues(v, e)
                if nxt is None or used[nxt]:
                    break
                used[nxt] = True
                w = other(nxt, v)
                if end == 1:
                    line.append(w)
                else:
                    line.insert(0, w)
                e = nxt
                if w == line[0 if end == 1 else -1]:
                    break  # closed loop
        out.append(line)
    return out


def simplify(points: np.ndarray, line: list[int], tolerance: float) -> list[int]:
    """Douglas-Peucker over the polyline's vertices."""
    if len(line) <= 2:
        return line
    p = points[line]
    a, b = p[0], p[-1]
    ab = b - a
    denom = ab @ ab
    if denom < 1e-12:
        dist = np.linalg.norm(p - a, axis=1)
    else:
        t = np.clip((p - a) @ ab / denom, 0, 1)
        dist = np.linalg.norm(p - (a + t[:, None] * ab), axis=1)
    i = int(dist.argmax())
    if dist[i] <= tolerance:
        return [line[0], line[-1]]
    left = simplify(points, line[: i + 1], tolerance)
    right = simplify(points, line[i:], tolerance)
    return left[:-1] + right


def choose(src: Source, sel: Selection, face_part: np.ndarray, part_names: list[str], count: int,
           keep: tuple[str, ...] = (), drop: tuple[str, ...] = (), caps: dict[str, int] | None = None,
           hulls: list[tuple[np.ndarray, np.ndarray]] | None = None, smooth_iterations: int = 4,
           crease_deg: float = 40.0, rim: float = 0.35, min_salience: float = 0.3, max_turn_deg: float = 40.0,
           min_length: float = 0.035, tolerance: float = 0.012, hidden_depth: float = 0.02) -> tuple[Proxy, list[Chain]]:
    """The model's chosen polylines (lengths and tolerances as shares of the model's height). `face_part` labels
    each source triangle with an index into `part_names`; `keep` parts' chains are taken first, `drop` parts' never;
    `caps` limits a part's segments."""
    h = src.height
    proxy = weld(src, sel, face_part, 1e-4 * h, smooth_iterations)
    sal = salience(proxy, view_directions(), crease_deg, rim)
    a, b = proxy.raw[proxy.edges[:, 0]], proxy.raw[proxy.edges[:, 1]]
    length = np.linalg.norm(b - a, axis=1)
    chosen = sal >= min_salience
    if hulls:
        chosen &= ~hidden(0.5 * (a + b), hulls, hidden_depth * h)
    edge_part = np.array([np.bincount(proxy.face_part[f], minlength=len(part_names)).argmax() if f else 0
                          for f in proxy.edge_faces])
    score_of = {}
    for i in np.flatnonzero(chosen):
        e = proxy.edges[i]
        score_of[(int(e[0]), int(e[1]))] = (length[i] * sal[i], int(edge_part[i]))
    lines = []
    for raw in chains(proxy, chosen, max_turn_deg):
        steps = [score_of[tuple(sorted((raw[k], raw[k + 1])))] for k in range(len(raw) - 1)]
        score = sum(s for s, _ in steps)
        part = part_names[np.bincount([p for _, p in steps], minlength=len(part_names)).argmax()]
        span = sum(np.linalg.norm(proxy.raw[raw[k + 1]] - proxy.raw[raw[k]]) for k in range(len(raw) - 1))
        if span < min_length * h or part in drop:
            continue
        lines.append(Chain(simplify(proxy.raw, raw, tolerance * h), part, score, span))
    lines.sort(key=lambda c: (c.part not in keep, -c.score))
    picked, total, per_part = [], 0, {}
    for c in lines:
        cap = (caps or {}).get(c.part)
        if total + c.segments > count or (cap is not None and per_part.get(c.part, 0) + c.segments > cap):
            continue
        picked.append(c)
        total += c.segments
        per_part[c.part] = per_part.get(c.part, 0) + c.segments
    if picked:
        top = max(c.score for c in picked)
        for c in picked:
            c.importance = float(np.clip(np.sqrt(c.score / top), 0.15, 1.0))
            if c.part in keep:
                c.importance = max(c.importance, 0.6)
    return proxy, picked
