"""`champions monster bake TYPE` and `champions monster review TYPE [--look trailer]` (plan.md, Rendering > The edge look).

A type is one of the ten in Assets/Champions/Editor/Monsters/monsters.json (its vendor prefab, height, clips). A bake
writes it into the ignored Assets/Baked/Monsters/TYPE/ for both looks: `--look trailer`'s always from the vendor mesh,
the default's from the type's recipe, tools/recipes/monster/TYPE.py, when there is one, else from the vendor mesh too.
With a recipe there are three steps, each skipped while its inputs haven't changed: Unity exports the vendor mesh
(TYPE/source/), the recipe builds the new mesh from it (TYPE/recipe/), and Unity bakes both looks (MonsterBaker keeps
its own key). Without one, the Unity bake alone. A review bakes if needed, then renders the type's sheets into
rhythm_champions/evidence/monsters/TYPE/ with a README; it never passes or fails anything.

A recipe is a Python module whose docstring says what it changes and why (the review's README quotes it), with

    def build(source: Source) -> Mesh: ...

`Source` is the vendor mesh as the game draws it: every renderer combined in the model's root space, metres at the
type's height, its rest pose, each vertex skinned to four of the source's bones. The recipe returns a `Mesh` skinned to
the same bones (their indices into `source.bones`), so it moves with the vendor rig and clips. Numpy is at hand, and
Blender 5.2 LTS headless through `blender()`: `source.save(path)` and `Mesh.load(path)` trade .npz files with a Blender
script. By default every crease of the mesh sharper than 24 degrees is a line on screen, as the trailer drew the vendor
meshes; a designed low-poly mesh may choose its own: `mesh.edge_angle = 1` draws every edge but a flat quad's diagonal
(a true wireframe), or `mesh.edges` flags each triangle's edges. A mesh that chooses its edges is not decimated for
distance: its topology is the drawing.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path
from types import ModuleType

import numpy as np

from . import paths, setup, unity

TASKS = "Champions.EditorTools.Monsters.MonsterTasks"
BLENDER = Path("/Applications/Blender.app/Contents/MacOS/Blender")
MODELS = "Assets/Champions/Editor/Monsters/monsters.json"
LOOKS = ("default", "trailer")
# What the export reads besides the type's entry: the code that spawns and combines the vendor model.
EXPORT_CODE = ("Assets/Champions/Editor/Monsters/MonsterBaker.cs", "Assets/Champions/Editor/Monsters/MonsterModel.cs",
               "Assets/Champions/Editor/Monsters/Fist.cs", "manifest.json")


class MonsterError(Exception):
    pass


@dataclass
class Part:
    """One renderer of the vendor model: its name and vertex range; rigid when it rides one bone (a held weapon); carried
    when it is a weapon or shield (the shatter flings its chunks with the body)."""

    name: str
    first: int
    count: int
    rigid: bool
    carried: bool


@dataclass
class Source:
    """The type's vendor mesh as the game draws it (MonsterBaker.Export): numpy arrays over its vertices and triangles."""

    id: str
    height_m: float
    positions: np.ndarray  # (n, 3) float32, root space, metres
    normals: np.ndarray  # (n, 3) float32
    triangles: np.ndarray  # (m, 3) int32
    bones: np.ndarray  # (n, 4) int32, into bone_names
    weights: np.ndarray  # (n, 4) float32, summing to 1
    carried: np.ndarray  # (n,) bool: of a weapon or shield
    bone_names: list[str]
    bone_paths: list[str]
    parts: list[Part]

    @classmethod
    def read(cls, folder: Path) -> Source:
        meta = json.loads((folder / "source.json").read_text(encoding="utf-8"))
        n, m = meta["vertices"], meta["triangles"]

        def array(name: str, dtype: type, width: int, count: int) -> np.ndarray:
            return np.fromfile(folder / name, dtype=dtype).reshape(count, width)

        return cls(
            id=meta["id"], height_m=meta["heightM"],
            positions=array("positions.f32", np.float32, 3, n), normals=array("normals.f32", np.float32, 3, n),
            triangles=array("triangles.i32", np.int32, 3, m), bones=array("bones.i32", np.int32, 4, n),
            weights=array("weights.f32", np.float32, 4, n),
            carried=np.fromfile(folder / "carried.u8", dtype=np.uint8).astype(bool),
            bone_names=[b["name"] for b in meta["bones"]], bone_paths=[b["path"] for b in meta["bones"]],
            parts=[Part(p["name"], p["first"], p["count"], p["rigid"], p["carried"]) for p in meta["parts"]],
        )

    def part(self, name: str) -> Part:
        for p in self.parts:
            if p.name == name:
                return p
        raise MonsterError(f"{self.id}: no part {name!r} (parts: {', '.join(p.name for p in self.parts)})")

    def bone(self, name: str) -> int:
        """A bone's index by its name."""
        try:
            return self.bone_names.index(name)
        except ValueError:
            raise MonsterError(f"{self.id}: no bone {name!r}") from None

    def mesh(self) -> Mesh:
        """The source as a recipe's mesh, unchanged (a recipe's starting point)."""
        return Mesh(self.positions.copy(), self.triangles.copy(), self.bones.copy(), self.weights.copy(), self.normals.copy(),
                    self.carried.copy())

    def save(self, path: Path) -> None:
        """The arrays and bone names in one .npz, for a Blender script."""
        np.savez(path, positions=self.positions, normals=self.normals, triangles=self.triangles, bones=self.bones,
                 weights=self.weights, carried=self.carried, bone_names=np.array(self.bone_names))


@dataclass
class Mesh:
    """
    A recipe's mesh: triangles over vertices skinned to the source's bones; normals optional (Unity computes them);
    carried optional, true for a weapon's or shield's vertices, whose chunks the shatter flings with the body. Which
    edges draw: `edges` (m, 3), 1 for the edge opposite each corner of each triangle; else every crease sharper than
    `edge_angle` degrees (24, the vendor meshes'; 1 draws every edge but a flat quad's diagonal).
    """

    positions: np.ndarray  # (n, 3), root space, metres at the type's height
    triangles: np.ndarray  # (m, 3) into positions
    bones: np.ndarray  # (n, 4) into the source's bones
    weights: np.ndarray  # (n, 4), summing to 1
    normals: np.ndarray | None = field(default=None)
    carried: np.ndarray | None = field(default=None)  # (n,) bool
    edges: np.ndarray | None = field(default=None)  # (m, 3) 0 or 1
    edge_angle: float = 24.0

    @classmethod
    def load(cls, path: Path) -> Mesh:
        """A mesh a Blender script saved with numpy.savez (the arrays named as the fields)."""
        a = np.load(path)
        return cls(a["positions"], a["triangles"], a["bones"], a["weights"], a.get("normals"), a.get("carried"), a.get("edges"))

    def check(self, bones: int) -> None:
        n = len(self.positions)
        problems = []
        if self.positions.shape != (n, 3) or n == 0:
            problems.append(f"positions {self.positions.shape}")
        if self.triangles.ndim != 2 or self.triangles.shape[1] != 3 or len(self.triangles) == 0:
            problems.append(f"triangles {self.triangles.shape}")
        elif self.triangles.min() < 0 or self.triangles.max() >= n:
            problems.append("triangles index past the vertices")
        if self.bones.shape != (n, 4) or self.weights.shape != (n, 4):
            problems.append(f"bones {self.bones.shape} and weights {self.weights.shape} need (n, 4)")
        elif self.bones.min() < 0 or self.bones.max() >= bones:
            problems.append(f"bones index past the source's {bones}")
        elif not np.allclose(self.weights.sum(axis=1), 1, atol=1e-3):
            problems.append("weights don't sum to 1")
        if self.normals is not None and self.normals.shape != (n, 3):
            problems.append(f"normals {self.normals.shape}")
        if self.carried is not None and self.carried.shape != (n,):
            problems.append(f"carried {self.carried.shape}")
        if self.edges is not None and self.edges.shape != self.triangles.shape:
            problems.append(f"edges {self.edges.shape} need one flag per triangle corner {self.triangles.shape}")
        if not 0 < self.edge_angle < 180:
            problems.append(f"edge_angle {self.edge_angle} is not between 0 and 180 degrees")
        if problems:
            raise MonsterError("the recipe's mesh is malformed: " + "; ".join(problems))

    def write(self, folder: Path) -> None:
        """The arrays MonsterBaker reads from TYPE/recipe/."""
        if folder.exists():
            shutil.rmtree(folder)
        folder.mkdir(parents=True)
        self.positions.astype("<f4").tofile(folder / "positions.f32")
        self.triangles.astype("<i4").tofile(folder / "triangles.i32")
        self.bones.astype("<i4").tofile(folder / "bones.i32")
        self.weights.astype("<f4").tofile(folder / "weights.f32")
        if self.normals is not None:
            self.normals.astype("<f4").tofile(folder / "normals.f32")
        if self.carried is not None:
            self.carried.astype(np.uint8).tofile(folder / "carried.u8")
        if self.edges is not None:
            (self.edges != 0).astype(np.uint8).tofile(folder / "edges.u8")
        meta = {"vertices": len(self.positions), "triangles": len(self.triangles), "edgeAngleDeg": self.edge_angle}
        (folder / "mesh.json").write_text(json.dumps(meta) + "\n", encoding="utf-8")


def blender(script: Path, *args: str, timeout: float = 600) -> None:
    """Runs a Python script in Blender 5.2 LTS headless, its arguments after `--` (sys.argv); raises on failure."""
    if not BLENDER.exists():
        raise MonsterError(f"no Blender at {BLENDER} (Blender 5.2 LTS, plan.md Decision 7)")
    cmd = [str(BLENDER), "--background", "--factory-startup", "--python-exit-code", "1", "--python", str(script), "--", *args]
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, check=False)
    if res.returncode != 0:
        raise MonsterError(f"Blender failed (exit {res.returncode}) on {script}:\n{res.stdout[-2000:]}{res.stderr[-2000:]}")


def types(wt: paths.Worktree) -> list[dict]:
    return json.loads((wt.project / MODELS).read_text(encoding="utf-8"))["types"]


def model(wt: paths.Worktree, type_id: str) -> dict:
    for t in types(wt):
        if t["id"] == type_id:
            return t
    raise MonsterError(f"no type {type_id!r} in {MODELS} (types: {', '.join(t['id'] for t in types(wt))})")


def recipe_path(wt: paths.Worktree, type_id: str) -> Path:
    return wt.project / "tools" / "recipes" / "monster" / f"{type_id}.py"


def baked_dir(wt: paths.Worktree, type_id: str) -> Path:
    return wt.project / "Assets" / "Baked" / "Monsters" / type_id


def evidence_dir(wt: paths.Worktree, type_id: str) -> Path:
    return wt.project / "evidence" / "monsters" / type_id


def load_recipe(path: Path) -> ModuleType:
    spec = importlib.util.spec_from_file_location(f"monster_recipe_{path.stem}", path)
    module = importlib.util.module_from_spec(spec)
    writes, sys.dont_write_bytecode = sys.dont_write_bytecode, True  # no __pycache__ among the recipes
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    if not callable(getattr(module, "build", None)):
        raise MonsterError(f"{path.name} needs build(source) -> Mesh")
    return module


def run_recipe(path: Path, source_dir: Path, out: Path) -> Mesh:
    """Builds the recipe's mesh from the exported source and writes it to `out`."""
    src = Source.read(source_dir)
    mesh = load_recipe(path).build(src)
    if not isinstance(mesh, Mesh):
        raise MonsterError(f"{path.name}'s build returned {type(mesh).__name__}, not a Mesh")
    mesh.check(len(src.bone_names))
    mesh.write(out)
    return mesh


def digest(wt: paths.Worktree, *parts: str | Path) -> str:
    """A hash of text parts and of files given as paths."""
    h = hashlib.sha256()
    for part in parts:
        if isinstance(part, Path):
            h.update(part.relative_to(wt.project).as_posix().encode())
            h.update(part.read_bytes().replace(b"\r\n", b"\n") if part.exists() else b"<missing>")
        else:
            h.update(part.encode())
    return h.hexdigest()[:16]


def _current(marker: Path, key: str, *outputs: Path) -> bool:
    return marker.exists() and marker.read_text(encoding="utf-8").strip() == key and all(o.exists() for o in outputs)


def _unity(wt: paths.Worktree, task: str, *args: str) -> None:
    code = unity.run_method(wt, f"{TASKS}.{task}", list(args))
    if code != 0:
        raise MonsterError(f"Unity's {task} failed (exit {code}); see rhythm_champions/Logs/")


def prepare(wt: paths.Worktree, type_id: str, force: bool = False) -> list[str]:
    """
    The recipe's steps for one type: export the vendor mesh and run the recipe when their inputs changed (always with
    `force`); without a recipe, drops a mesh left by a recipe since deleted. What it did.
    """
    out = baked_dir(wt, type_id)
    recipe = recipe_path(wt, type_id)
    if not recipe.exists():
        if (out / "recipe").exists():
            shutil.rmtree(out / "recipe")
            return ["dropped the mesh of a deleted recipe"]
        return []
    p = wt.project
    export_key = digest(wt, json.dumps(model(wt, type_id), sort_keys=True), *(p / f for f in EXPORT_CODE))
    recipe_key = digest(wt, export_key, recipe, Path(__file__).resolve())
    steps = []
    out.mkdir(parents=True, exist_ok=True)
    if force or not _current(out / "source.key", export_key, out / "source" / "source.json"):
        started = time.monotonic()
        _unity(wt, "Export", "-type", type_id)
        (out / "source.key").write_text(export_key + "\n", encoding="utf-8")
        steps.append(f"exported in {time.monotonic() - started:.1f} s")
    if force or not _current(out / "recipe.key", recipe_key, out / "recipe" / "mesh.json"):
        started = time.monotonic()
        mesh = run_recipe(recipe, out / "source", out / "recipe")
        (out / "recipe.key").write_text(recipe_key + "\n", encoding="utf-8")
        steps.append(f"recipe built {len(mesh.triangles)} triangles in {time.monotonic() - started:.1f} s")
    return steps


def prepare_all(wt: paths.Worktree) -> None:
    """The recipe steps of every type with a recipe or a recipe's mesh (`champions build` runs them before the player's bake)."""
    ids = {p.stem for p in recipe_path(wt, "x").parent.glob("*.py")}
    ids |= {p.parent.name for p in baked_dir(wt, "x").parent.glob("*/recipe")}
    for type_id in sorted(ids):
        for step in prepare(wt, type_id):
            print(f"monster: {type_id}: {step}")


def bake(wt: paths.Worktree, type_id: str, force: bool = False) -> list[str]:
    """Bakes one type (or `all`) for both looks, running only the steps whose inputs changed; what it did."""
    ids = [t["id"] for t in types(wt)] if type_id == "all" else [model(wt, type_id)["id"]]
    steps = [f"{i}: {s}" for i in ids for s in prepare(wt, i, force)]
    started = time.monotonic()
    _unity(wt, "Bake", "-type", type_id, *(["-force"] if force else []))
    steps.append(f"Unity's bake in {time.monotonic() - started:.1f} s")
    saved = setup.save_baked(wt)
    if saved:
        steps.append(f"saved Assets/Baked to the cache ({saved.name})")
    return steps


def review(wt: paths.Worktree, type_id: str, look: str, out: Path | None = None, force: bool = False) -> Path:
    """Bakes if needed, renders the type's sheets in `look` into `out` (its evidence folder by default) with a README."""
    for step in bake(wt, type_id, force):
        print(f"monster bake: {type_id}: {step}")
    out = out or evidence_dir(wt, type_id)
    out.mkdir(parents=True, exist_ok=True)
    for old in out.glob(f"{look}-*.jpg"):
        old.unlink()
    _unity(wt, "Review", "-type", type_id, "-look", look, "-out", str(out.resolve()))
    result = json.loads((out / "review.json").read_text(encoding="utf-8"))
    recipe = recipe_path(wt, type_id)
    doc = (load_recipe(recipe).__doc__ or "").strip() if recipe.exists() else ""
    readme = out / "README.md"
    notes = _notes(readme)
    looks = [lk for lk in LOOKS if (out / f"{lk}-game.jpg").exists()]
    readme.write_text(write_readme(result, doc, notes, looks), encoding="utf-8")
    return out


NOTES = "## Notes"


def _notes(readme: Path) -> str:
    """The README's hand-written notes (from its Notes heading on), which a new review keeps."""
    if not readme.exists():
        return ""
    text = readme.read_text(encoding="utf-8")
    at = text.find("\n" + NOTES + "\n")
    return text[at + 1:].rstrip() + "\n" if at >= 0 else ""


def write_readme(result: dict, recipe_doc: str, notes: str, looks: list[str]) -> str:
    """The README for the sheets of `looks` in the folder, from the latest review's numbers."""
    t = result["type"]
    source = f"its recipe, tools/recipes/monster/{t}.py" if result["source"] == "recipe" else "the vendor mesh"
    cam = result["camera"]
    lines = [
        f"# Monster: {t}",
        "",
        f"Written by `champions monster review {t}` and `champions monster review {t} --look trailer` (plan.md,",
        "Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.",
        "",
        f"- The default look draws {source}; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.",
        (f"- The camera is the game's ({cam['pitchDeg']} degrees down, {cam['distanceM']} m, {cam['fovDeg']} degree field),"
         " each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post."),
    ]
    for look in looks:
        lines += [
            (f"- `{look}-far.jpg`, `{look}-game.jpg`, `{look}-near.jpg`: the {look} look at twice, once and half the game"
             f" camera's distance, the poses {', '.join(result['poses'])}, then the shatter"
             f" {', '.join(str(s) for s in result['shatterAt'])} s after a blow away from the camera."),
            f"- `{look}-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).",
        ]
    lines += [
        "- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.",
        "",
        ("Numbers (diagnostics only): "
         f"LOD triangles {'/'.join(str(n) for n in result['lodTriangles'])} in the {result['look']} look "
         f"(the trailer's {'/'.join(str(n) for n in result['trailerLodTriangles'])}), {result['shards']} shards, "
         f"{result['bones']} bones, standing {result['heightM']} m."),
    ]
    if recipe_doc:
        lines += ["", "## The recipe", "", recipe_doc]
    return "\n".join([*lines, ""]) + ("\n" + notes if notes else "")


def cmd_monster(a) -> int:
    wt = paths.worktree()
    try:
        if a.action == "bake":
            for step in bake(wt, a.type, a.force):
                print(f"monster bake: {step}")
            return 0
        if a.type == "all":
            for t in types(wt):
                print(f"monster review: {review(wt, t['id'], a.look, None, a.force)}")
            return 0
        print(f"monster review: {review(wt, a.type, a.look, Path(a.out).resolve() if a.out else None, a.force)}")
        return 0
    except MonsterError as e:
        print(f"monster {a.action}: {e}", file=sys.stderr)
        return 2
