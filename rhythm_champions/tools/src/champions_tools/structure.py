"""`champions structure bake KIND` and `champions structure review KIND` (plan.md, Rendering > The edge look), and the
structure recipes they run.

A kind is one of the eight in Assets/Champions/Editor/Structures/structures.json (its Synty prefab, height, pieces). A
bake writes it into the ignored Assets/Baked/Structures/KIND/ for both looks: `--look trailer`'s always from the Synty
model, the default's from the kind's recipe, tools/recipes/structure/KIND.py, when there is one, else from the model as
structures.json tunes it. With a recipe, the recipe builds its mesh into KIND/recipe/ first, skipped while the recipe,
this module and the kind's entries haven't changed; then Unity bakes both looks (StructureBaker keeps its own key). A
review bakes if needed, then renders the sheets into rhythm_champions/evidence/structures/; it never passes or fails.

A structure recipe is a Python module whose docstring says what it draws and why, with

    def build(kind: Kind) -> Mesh: ...

It designs the structure from scratch, numpy at hand: a low-poly model of simple faceted shapes in the structure's own
space (metres, y up from the ground, the footprint's middle at the origin, lying along x as Content/structures.json's
footprint does), standing exactly `kind.height_m` tall. A `Builder` collects its convex polygons over shared corners,
and every edge of the design draws (design.md, The aim): a polygon's outline wherever it meets another polygon at an
angle or nothing at all, never the diagonals that cut a polygon into triangles. `Builder.line` draws an edge between
two polygons in one plane (a printed outline), `Builder.hush` quiets one. Shapes: `box`, `prism`, `loft`, `slab`, and
`face` and `panel` for flat faces with inset doors and windows. `Builder.mesh(max_edge, pieces)` cuts big facets into
triangles no longer than that along edges that never draw, so the shatter can cut chunks of a size from them, and may
give the default look's shatter its own piece count (structures.json's stays `--look trailer`'s).
"""

from __future__ import annotations

import importlib.util
import json
import math
import shutil
import sys
import time
from collections.abc import Iterable, Sequence
from dataclasses import dataclass
from itertools import pairwise
from pathlib import Path
from types import ModuleType

import numpy as np

from . import paths, setup, unity
from .monster import digest

TASKS = "Champions.EditorTools.Structures.StructureTasks"
MODELS = "Assets/Champions/Editor/Structures/structures.json"
CONTENT = "Content/structures.json"
FLAT_DEG = 1.0  # two polygons meeting at less than this are one plane: the edge between them doesn't draw


class StructureError(Exception):
    pass


@dataclass
class Kind:
    """A structure kind as a recipe needs it: its height (the mesh's top), its sim footprint (a box `size_m` along x and
    z, or a disc `radius_m`) and the pieces it shatters into."""

    id: str
    height_m: float
    pieces: int
    size_m: tuple[float, float] | None = None
    radius_m: float | None = None


@dataclass
class Mesh:
    """A recipe's mesh: triangles over corners in the structure's space, which of each triangle's edges draw (`edges`
    (m, 3), 1 for the edge opposite each corner), and how many pieces it shatters into (the kind's when None)."""

    positions: np.ndarray  # (n, 3) metres
    triangles: np.ndarray  # (m, 3) into positions
    edges: np.ndarray  # (m, 3) 0 or 1
    pieces: int | None = None

    def check(self, kind: Kind) -> None:
        n = len(self.positions)
        problems = []
        if self.positions.shape != (n, 3) or n == 0 or not np.isfinite(self.positions).all():
            problems.append(f"positions {self.positions.shape}")
        if self.triangles.ndim != 2 or self.triangles.shape[1] != 3 or len(self.triangles) == 0:
            problems.append(f"triangles {self.triangles.shape}")
        elif self.triangles.min() < 0 or self.triangles.max() >= n:
            problems.append("triangles index past the corners")
        if self.edges.shape != self.triangles.shape:
            problems.append(f"edges {self.edges.shape} need one flag per triangle corner {self.triangles.shape}")
        if self.pieces is not None and not 2 <= self.pieces <= 64:
            problems.append(f"pieces {self.pieces} is not between 2 and 64")
        if not problems:
            top = float(self.positions[:, 1].max())
            if abs(top - kind.height_m) > 0.01 * kind.height_m:
                problems.append(f"it stands {top:.2f} m tall, not the kind's {kind.height_m} m")
        if problems:
            raise StructureError(f"{kind.id}: the recipe's mesh is malformed: " + "; ".join(problems))

    def write(self, folder: Path) -> None:
        """The arrays StructureBaker reads from KIND/recipe/."""
        if folder.exists():
            shutil.rmtree(folder)
        folder.mkdir(parents=True)
        self.positions.astype("<f4").tofile(folder / "positions.f32")
        self.triangles.astype("<i4").tofile(folder / "triangles.i32")
        (self.edges != 0).astype(np.uint8).tofile(folder / "edges.u8")
        meta = {"vertices": len(self.positions), "triangles": len(self.triangles), "pieces": self.pieces or 0}
        (folder / "mesh.json").write_text(json.dumps(meta) + "\n", encoding="utf-8")


def _key(a: int, b: int) -> tuple[int, int]:
    return (a, b) if a < b else (b, a)


class Builder:
    """
    A structure's design: convex polygons over corners welded by position (so neighbouring shapes that share a corner
    share it). An edge of a polygon draws where no other polygon continues it in the same plane; `line` draws one
    anyway (a printed outline on a flat face), `hush` quiets one.
    """

    def __init__(self) -> None:
        self.points: list[np.ndarray] = []
        self._at: dict[tuple[int, int, int], int] = {}
        self.polys: list[list[int]] = []
        self.lines: set[tuple[int, int]] = set()
        self.quiet: set[tuple[int, int]] = set()

    def vert(self, p: Sequence[float] | np.ndarray) -> int:
        p = np.asarray(p, dtype=np.float64)
        key = tuple(round(float(c) * 1e4) for c in p)
        i = self._at.get(key)
        if i is None:
            i = self._at[key] = len(self.points)
            self.points.append(p)
        return i

    def poly(self, corners: Iterable[Sequence[float] | np.ndarray | int], facing: Sequence[float] | None = None) -> list[int]:
        """A convex polygon, its corners clockwise seen from outside (Unity's winding: the cross product of its first
        two sides points out), or turned so it faces `facing`; its corner indices."""
        idx = [c if isinstance(c, (int, np.integer)) else self.vert(c) for c in corners]
        idx = [int(i) for k, i in enumerate(idx) if i != idx[k - 1]]  # a corner given twice in a row is one
        if len(idx) < 3:
            raise StructureError(f"a polygon needs three distinct corners, has {len(idx)}")
        if facing is not None and np.dot(self.normal(idx), facing) < 0:
            idx.reverse()
        self.polys.append(idx)
        return idx

    def line(self, a: int, b: int) -> None:
        self.lines.add(_key(a, b))

    def hush(self, a: int, b: int) -> None:
        self.quiet.add(_key(a, b))

    def normal(self, poly: Sequence[int]) -> np.ndarray:
        """A polygon's unit normal (Newell's)."""
        p = np.array([self.points[i] for i in poly])
        q = np.roll(p, -1, axis=0)
        n = np.array([np.sum((p[:, 1] - q[:, 1]) * (p[:, 2] + q[:, 2])), np.sum((p[:, 2] - q[:, 2]) * (p[:, 0] + q[:, 0])),
                      np.sum((p[:, 0] - q[:, 0]) * (p[:, 1] + q[:, 1]))])
        length = np.linalg.norm(n)
        return n / length if length > 0 else n

    def drawn(self) -> set[tuple[int, int]]:
        """The edges that draw: each polygon's outline where no polygon in its plane continues it, with `line`'s and
        without `hush`'s."""
        uses: dict[tuple[int, int], list[int]] = {}
        for p, poly in enumerate(self.polys):
            for k in range(len(poly)):
                uses.setdefault(_key(poly[k], poly[(k + 1) % len(poly)]), []).append(p)
        normals = [self.normal(poly) for poly in self.polys]
        flat = math.cos(math.radians(FLAT_DEG))
        out = set()
        for e, ps in uses.items():
            if e in self.quiet:
                continue
            if len(ps) != 2 or abs(float(np.dot(normals[ps[0]], normals[ps[1]]))) < flat:
                out.add(e)
        return out | self.lines

    def mesh(self, max_edge: float | None = None, pieces: int | None = None) -> Mesh:
        """Each polygon as a fan of triangles, the drawn edges flagged; with `max_edge`, its triangles' edges longer
        than that halved (all the triangles on an edge at once, so no crack opens) along new edges that don't draw;
        `pieces`, the shatter's, where not the kind's."""
        drawn = self.drawn()
        tris, flags = [], []
        for poly in self.polys:
            n = self.normal(poly)
            poly = self._fan_start(poly)
            for i in range(1, len(poly) - 1):
                t = [poly[0], poly[i], poly[i + 1]]
                a, b, c = (self.points[j] for j in t)
                if np.dot(np.cross(b - a, c - a), n) < -1e-9:
                    raise StructureError(f"a polygon isn't convex (corners {poly})")
                tris.append(t)
                flags.append([int(_key(t[1], t[2]) in drawn), int(_key(t[2], t[0]) in drawn), int(_key(t[0], t[1]) in drawn)])
        points = list(self.points)
        if max_edge:
            tris, flags = _halve(points, tris, flags, max_edge)
        return Mesh(np.array(points, dtype=np.float64), np.array(tris, dtype=np.int32), np.array(flags, dtype=np.uint8), pieces)


    def _fan_start(self, poly: list[int]) -> list[int]:
        """The polygon from the corner whose fan's thinnest triangle is widest (no sliver from a corner on a straight
        side)."""
        if len(poly) == 3:
            return poly
        p = [self.points[i] for i in poly]

        def thinnest(s: int) -> float:
            q = p[s:] + p[:s]
            return min(float(np.linalg.norm(np.cross(q[i] - q[0], q[i + 1] - q[0]))) for i in range(1, len(q) - 1))

        best = max(range(len(poly)), key=thinnest)
        return poly[best:] + poly[:best]


def _halve(points: list[np.ndarray], tris: list[list[int]], flags: list[list[int]], max_edge: float):
    """Longest edges first, each edge over `max_edge` split at its middle in every triangle on it; a half of a drawn
    edge draws, the new edge to the opposite corner doesn't."""
    while True:
        uses: dict[tuple[int, int], list[tuple[int, int]]] = {}
        for t, tri in enumerate(tris):
            for k in range(3):
                uses.setdefault(_key(tri[(k + 1) % 3], tri[(k + 2) % 3]), []).append((t, k))
        long = sorted(((float(np.linalg.norm(points[a] - points[b])), (a, b)) for a, b in uses), reverse=True)
        long = [e for length, e in long if length > max_edge]
        if not long:
            return tris, flags
        touched: set[int] = set()
        for e in long:
            if any(t in touched for t, _ in uses[e]):
                continue
            m = len(points)
            points.append((points[e[0]] + points[e[1]]) / 2)
            for t, k in uses[e]:
                c, a, b = tris[t][k], tris[t][(k + 1) % 3], tris[t][(k + 2) % 3]
                f = flags[t]
                tris[t], flags[t] = [c, a, m], [f[k], 0, f[(k + 2) % 3]]
                tris.append([c, m, b])
                flags.append([f[k], f[(k + 1) % 3], 0])
                touched.update((t, len(tris) - 1))


# Shapes: the recipes' common volumes, each a closed set of polygons (open underneath where it stands on something).

def ring(outline: Sequence[Sequence[float]], y: float) -> list[np.ndarray]:
    """A horizontal ring of corners at height y from (x, z) points."""
    return [np.array([x, y, z], dtype=np.float64) for x, z in outline]


def loft(b: Builder, rings: Sequence[Sequence[np.ndarray]], top: bool = True, bottom: bool = False) -> list[list[int]]:
    """Rings of as many corners each (clockwise seen from above), joined by quads from the lowest up, capped at the top
    (and the bottom); each ring's corner indices."""
    idx = [[b.vert(p) for p in r] for r in rings]
    n = len(idx[0])
    for lo, hi in pairwise(idx):
        for j in range(n):
            k = (j + 1) % n
            b.poly([lo[j], lo[k], hi[k], hi[j]])
    if top:
        b.poly(idx[-1])
    if bottom:
        b.poly(list(reversed(idx[0])))
    return idx


def prism(b: Builder, outline: Sequence[Sequence[float]], y0: float, y1: float, top: bool = True,
          bottom: bool = False) -> list[list[int]]:
    """An upright prism over an (x, z) outline, clockwise seen from above."""
    return loft(b, [ring(outline, y0), ring(outline, y1)], top, bottom)


def rect(cx: float, cz: float, sx: float, sz: float, yaw_deg: float = 0.0) -> list[tuple[float, float]]:
    """A rectangle's (x, z) corners round its middle, sx along x and sz along z before the yaw, clockwise from above."""
    hx, hz = sx / 2, sz / 2
    return turn([(cx - hx, cz - hz), (cx - hx, cz + hz), (cx + hx, cz + hz), (cx + hx, cz - hz)], yaw_deg, (cx, cz))


def polygon(cx: float, cz: float, radius: float, sides: int, phase_deg: float = 0.0) -> list[tuple[float, float]]:
    """A regular polygon's (x, z) corners, clockwise from above, the first at `phase_deg` from +x toward -z."""
    out = []
    for i in range(sides):
        a = math.radians(phase_deg) + 2 * math.pi * i / sides
        out.append((cx + radius * math.cos(a), cz - radius * math.sin(a)))
    return out


def turn(outline: Sequence[Sequence[float]], yaw_deg: float, about: Sequence[float] = (0.0, 0.0)) -> list[tuple[float, float]]:
    """(x, z) points turned about a point, clockwise from above by `yaw_deg` (as Unity turns about y)."""
    a = math.radians(yaw_deg)
    c, s = math.cos(a), math.sin(a)
    return [(about[0] + (x - about[0]) * c + (z - about[1]) * s, about[1] - (x - about[0]) * s + (z - about[1]) * c)
            for x, z in outline]


def box(b: Builder, cx: float, cz: float, sx: float, sz: float, y0: float, y1: float, yaw_deg: float = 0.0,
        bottom: bool = False) -> list[list[int]]:
    """An upright box."""
    return prism(b, rect(cx, cz, sx, sz, yaw_deg), y0, y1, True, bottom)


def slab(b: Builder, corners: Sequence[Sequence[float]], thickness: float, down: Sequence[float] = (0, -1, 0)) -> list[int]:
    """A planar polygon (clockwise seen from its top side) made a slab: the face and a copy of it `thickness`
    under it along `down`, joined round its rim; the face's corner indices."""
    top = [np.asarray(c, dtype=np.float64) for c in corners]
    d = np.asarray(down, dtype=np.float64)
    d = d / np.linalg.norm(d) * thickness
    under = [c + d for c in top]
    idx = loft(b, [under, top], top=True, bottom=True)
    return idx[1]


def face(b: Builder, origin: Sequence[float], u: Sequence[float], v: Sequence[float], width: float, height: float,
         openings: Sequence[Sequence[float]] = (), depth: float = 0.1, gable: tuple[float, float] | None = None) -> None:
    """
    A flat rectangular face, `width` along the unit `u` (rightward seen from outside) and `height` along the unit `v`
    (up it) from its bottom-left corner `origin`, facing v x u, with `panel`'s openings. With `gable` (u, rise), a
    triangle over the face's top edge to an apex at u along it, rise over it, in the same plane.
    """
    outline = [(0.0, 0.0), (0.0, height), *([(gable[0], height + gable[1])] if gable else []), (width, height), (width, 0.0)]
    panel(b, origin, u, v, outline, openings, depth)


def panel(b: Builder, origin: Sequence[float], u: Sequence[float], v: Sequence[float],
          outline: Sequence[Sequence[float]], openings: Sequence[Sequence[float]] = (), depth: float = 0.1) -> None:
    """
    A flat convex face in the plane through `origin` along the unit `u` (rightward seen from outside) and `v` (up it),
    facing v x u: `outline` its corners as (u, v). Each opening (u0, v0, u1, v1) is an inset `depth` deep, its jambs and
    its back drawn. The face is cut into strips at its corners' and openings' heights and round the openings, every
    strip sharing the corners on its edges with the strips over and under it, so no edge draws but the outline's and the
    openings'.
    """
    o, u, v = (np.asarray(x, dtype=np.float64) for x in (origin, u, v))
    n = np.cross(v, u)

    def at(a: float, c: float, d: float = 0.0) -> np.ndarray:
        return o + a * u + c * v - d * n

    pts = [(float(a), float(c)) for a, c in outline]

    def span(c: float) -> tuple[float, float]:
        """The outline's extent along u at height c."""
        xs = []
        for (a0, c0), (a1, c1) in zip(pts, pts[1:] + pts[:1], strict=False):
            if min(c0, c1) - 1e-9 <= c <= max(c0, c1) + 1e-9:
                xs += [a0, a1] if abs(c1 - c0) < 1e-9 else [a0 + (a1 - a0) * (c - c0) / (c1 - c0)]
        return min(xs), max(xs)

    levels = sorted({c for _, c in pts} | {y for w in openings for y in (w[1], w[3])})
    cells = []  # each (bottom left u, top left u, top right u, bottom right u, v under, v over)
    for va, vb in pairwise(levels):
        (la, ra), (lb, rb) = span(va), span(vb)
        across = [w for w in openings if w[1] <= va + 1e-9 and w[3] >= vb - 1e-9]
        cuts = sorted({x for w in across for x in (w[0], w[2])})
        bounds = [(la, lb), *((x, x) for x in cuts), (ra, rb)]
        for (a0, b0), (a1, b1) in pairwise(bounds):
            mid = (a0 + b0 + a1 + b1) / 4
            if not any(w[0] < mid < w[2] for w in across):
                cells.append((a0, b0, b1, a1, va, vb))
    on_level: dict[float, set[float]] = {}
    for a0, b0, b1, a1, va, vb in cells:
        on_level.setdefault(va, set()).update((a0, a1))
        on_level.setdefault(vb, set()).update((b0, b1))
    for a0, b0, b1, a1, va, vb in cells:
        top = sorted(x for x in on_level[vb] if b0 + 1e-9 < x < b1 - 1e-9)
        under = sorted((x for x in on_level[va] if a0 + 1e-9 < x < a1 - 1e-9), reverse=True)
        b.poly([at(a0, va), at(b0, vb), *(at(x, vb) for x in top), at(b1, vb), at(a1, va), *(at(x, va) for x in under)])
    for u0, v0, u1, v1 in openings:
        corners = [(u0, v0), (u0, v1), (u1, v1), (u1, v0)]
        for (a0, c0), (a1, c1) in zip(corners, corners[1:] + corners[:1], strict=False):
            mid = ((a0 + a1) / 2, (c0 + c1) / 2)
            inward = at((u0 + u1) / 2, (v0 + v1) / 2) - at(*mid)
            b.poly([at(a0, c0), at(a1, c1), at(a1, c1, depth), at(a0, c0, depth)], facing=inward)
        b.poly([at(a, c, depth) for a, c in corners])


# The recipe steps.

def models(wt: paths.Worktree) -> list[dict]:
    return json.loads((wt.project / MODELS).read_text(encoding="utf-8"))["kinds"]


def kind(wt: paths.Worktree, kind_id: str) -> Kind:
    model = next((m for m in models(wt) if m["id"] == kind_id), None)
    if model is None:
        raise StructureError(f"no kind {kind_id!r} in {MODELS} (kinds: {', '.join(m['id'] for m in models(wt))})")
    rules = json.loads((wt.project / CONTENT).read_text(encoding="utf-8"))["kinds"]
    rule = next((r for r in rules if r["id"] == kind_id), {})
    size = rule.get("sizeM")
    return Kind(kind_id, float(model["heightM"]), int(model.get("pieces", 24)), (float(size[0]), float(size[1])) if size else None,
                float(rule["radiusM"]) if "radiusM" in rule else None)


def recipe_path(wt: paths.Worktree, kind_id: str) -> Path:
    return wt.project / "tools" / "recipes" / "structure" / f"{kind_id}.py"


def baked_dir(wt: paths.Worktree, kind_id: str) -> Path:
    return wt.project / "Assets" / "Baked" / "Structures" / kind_id


def load_recipe(path: Path) -> ModuleType:
    spec = importlib.util.spec_from_file_location(f"structure_recipe_{path.stem}", path)
    module = importlib.util.module_from_spec(spec)
    writes, sys.dont_write_bytecode = sys.dont_write_bytecode, True  # no __pycache__ among the recipes
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    if not callable(getattr(module, "build", None)):
        raise StructureError(f"{path.name} needs build(kind) -> Mesh")
    return module


def run_recipe(path: Path, k: Kind, out: Path) -> Mesh:
    """Builds the recipe's mesh for the kind and writes it to `out`."""
    mesh = load_recipe(path).build(k)
    if not isinstance(mesh, Mesh):
        raise StructureError(f"{path.name}'s build returned {type(mesh).__name__}, not a Mesh")
    mesh.check(k)
    mesh.write(out)
    return mesh


def prepare(wt: paths.Worktree, kind_id: str, force: bool = False) -> list[str]:
    """Runs the kind's recipe when its inputs changed (always with `force`); without a recipe, drops a mesh left by a
    recipe since deleted. What it did."""
    out = baked_dir(wt, kind_id)
    recipe = recipe_path(wt, kind_id)
    if not recipe.exists():
        if (out / "recipe").exists():
            shutil.rmtree(out / "recipe")
            return ["dropped the mesh of a deleted recipe"]
        return []
    k = kind(wt, kind_id)
    key = digest(wt, repr(k), recipe, Path(__file__).resolve())
    marker = out / "recipe.key"
    if not force and marker.exists() and marker.read_text(encoding="utf-8").strip() == key and (out / "recipe" / "mesh.json").exists():
        return []
    started = time.monotonic()
    mesh = run_recipe(recipe, k, out / "recipe")
    marker.write_text(key + "\n", encoding="utf-8")
    size = mesh.positions.max(axis=0) - mesh.positions.min(axis=0)
    return [(f"recipe built {len(mesh.triangles)} triangles, {int(mesh.edges.sum())} drawn triangle edges, "
             f"{size[0]:.2f} x {size[2]:.2f} m on the ground, in {time.monotonic() - started:.1f} s")]


def prepare_all(wt: paths.Worktree, force: bool = False) -> None:
    """The recipe step of every kind (`champions build` runs it before the player's bake); nothing in a project without
    structures."""
    if not (wt.project / MODELS).exists():
        return
    for m in models(wt):
        for step in prepare(wt, m["id"], force):
            print(f"structure: {m['id']}: {step}")


def cmd_structure(a) -> int:
    wt = paths.worktree()
    try:
        ids = [m["id"] for m in models(wt)] if a.kind == "all" else [kind(wt, a.kind).id]
        for kind_id in ids:
            for step in prepare(wt, kind_id, a.force):
                print(f"structure: {kind_id}: {step}")
    except StructureError as e:
        print(f"structure {a.action}: {e}", file=sys.stderr)
        return 2
    if a.action == "bake":
        code = unity.run_method(wt, f"{TASKS}.Bake", ["-kind", a.kind] + (["-force"] if a.force else []))
    else:
        out = Path(a.out).resolve() if a.out else wt.project / "evidence" / "structures"
        code = unity.run_method(wt, f"{TASKS}.Review", ["-kind", a.kind, "-out", str(out)])
    if code == 0:
        saved = setup.save_baked(wt)
        if saved:
            print(f"structure: saved Assets/Baked to the cache ({saved.name})")
    return code
