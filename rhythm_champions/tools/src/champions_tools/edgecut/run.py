"""`champions edgecut <model>` and `champions edgecut review <model>` (plan.md, Rendering > Edge cuts).

A bake runs three steps, each skipped when its inputs haven't changed (keys in Assets/Baked/EdgeCuts/<model>/):
Unity exports the vendor model the recipe names (source.json), the recipe cuts it (cut.json), and Unity bakes the
cut into the edge crowd's assets and library (a hero's with its rig, posed on the CPU; a structure's, a model without
clips, into the structure field's). A
review bakes if needed, then renders the cut's sheets beside the original into
rhythm_champions/evidence/edgecuts/<model>/ with a README of its numbers against the thresholds.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import sys
import time
from pathlib import Path
from types import ModuleType

from .. import palette, paths, plugin, setup, unity
from .source import Source

TASKS = "Champions.EditorTools.EdgeCuts.EdgeCutTasks"
# plan.md, Rendering > Edge cuts: black share at game scale, line segments and body triangles by role.
THRESHOLDS = {
    "fodder": {"black_share": 0.65, "segments": 120, "triangles": (300, 800)},
    "elite": {"black_share": 0.55, "segments": 220, "triangles": (300, 800)},
    "boss": {"black_share": 0.55, "segments": 450, "triangles": (300, 1500)},
    "hero": {"black_share": 0.55, "segments": 220, "triangles": (300, 800)},
    # Structures have a coloured fill, not a black body: no black share; a rock or a fence needs few triangles.
    "structure": {"segments": 160, "triangles": (12, 800)},
}
# What each step reads besides the recipe's model, relative to the project.
EXPORT_CODE = ("Assets/Champions/Editor/EdgeCuts/EdgeCutSource.cs", "Assets/Champions/Editor/EdgeCuts/EdgeCutModel.cs",
               "Assets/Champions/Editor/EdgeCuts/Fist.cs", "manifest.json")
CUT_CODE = ("tools/src/champions_tools/edgecut",)
BAKE_CODE = ("Assets/Champions/Editor/EdgeCuts/EdgeCutBaker.cs", "Assets/Champions/Render/Crowd/EdgeCutType.cs")
# A hero's bake also keeps its rig, posed on the CPU (EdgeRig).
RIG_CODE = ("Assets/Champions/Editor/EdgeCuts/EdgeRigBaker.cs", "Assets/Champions/Render/Crowd/EdgeRig.cs",
            "Assets/Champions/Render/Crowd/RigPose.cs")
STRUCTURE_BAKE_CODE = ("Assets/Champions/Editor/EdgeCuts/StructureBaker.cs", "Assets/Champions/Render/Structures/StructureType.cs")


class EdgeCutError(Exception):
    pass


def recipes_dir(wt: paths.Worktree) -> Path:
    return wt.project / "tools" / "recipes" / "edgecut"


def baked_dir(wt: paths.Worktree, model: str) -> Path:
    return wt.project / "Assets" / "Baked" / "EdgeCuts" / model


def evidence_dir(wt: paths.Worktree, model: str) -> Path:
    return wt.project / "evidence" / "edgecuts" / model


def load_recipe(wt: paths.Worktree, model: str) -> ModuleType:
    path = recipes_dir(wt) / f"{model}.py"
    if not path.exists():
        known = sorted(p.stem for p in recipes_dir(wt).glob("*.py"))
        raise EdgeCutError(f"no recipe {path.relative_to(wt.root)} (recipes: {', '.join(known) or 'none'})")
    spec = importlib.util.spec_from_file_location(f"edgecut_recipe_{model}", path)
    module = importlib.util.module_from_spec(spec)
    writes, sys.dont_write_bytecode = sys.dont_write_bytecode, True  # no __pycache__ among the recipes
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    if getattr(module, "MODEL", None) is None or module.MODEL.id != model or not callable(getattr(module, "cut", None)):
        raise EdgeCutError(f"{path.name} needs MODEL (id {model!r}) and cut(source)")
    return module


def digest(wt: paths.Worktree, *parts: str | Path) -> str:
    """A hash of text parts and of files (or every file under folders) given as project paths."""
    h = hashlib.sha256()
    for part in parts:
        if isinstance(part, Path):
            files = sorted(f for f in part.rglob("*") if f.is_file() and "__pycache__" not in f.parts) if part.is_dir() else [part]
            for f in files:
                h.update(f.relative_to(wt.project).as_posix().encode())
                h.update(f.read_bytes().replace(b"\r\n", b"\n") if f.exists() else b"<missing>")
        else:
            h.update(part.encode())
    return h.hexdigest()[:16]


def _current(marker: Path, key: str, *outputs: Path) -> bool:
    return marker.exists() and marker.read_text(encoding="utf-8").strip() == key and all(o.exists() for o in outputs)


def _unity(wt: paths.Worktree, task: str, *args: str) -> None:
    code = unity.run_method(wt, f"{TASKS}.{task}", list(args))
    if code != 0:
        raise EdgeCutError(f"Unity's {task} failed (exit {code}); see Logs/")


def bake(wt: paths.Worktree, model: str, force: bool = False) -> dict:
    """Bakes the model's cut, running only the steps whose inputs changed (all with `force`); its summary."""
    recipe = load_recipe(wt, model)
    out = baked_dir(wt, model)
    out.mkdir(parents=True, exist_ok=True)
    model_json = json.dumps(recipe.MODEL.json(), indent=2) + "\n"
    (out / "model.json").write_text(model_json, encoding="utf-8")
    p = wt.project
    export_key = digest(wt, model_json, *(p / f for f in EXPORT_CODE))
    cut_key = digest(wt, export_key, p / "tools" / "recipes" / "edgecut" / f"{model}.py", *(p / f for f in CUT_CODE))
    rig = thresholds(recipe)["role"] == "hero"
    code = STRUCTURE_BAKE_CODE if recipe.MODEL.static else BAKE_CODE + (RIG_CODE if rig else ())
    bake_key = digest(wt, cut_key, *(p / f for f in code))
    steps = []
    if force or not _current(out / "source.key", export_key, out / "source.json"):
        started = time.monotonic()
        _unity(wt, "Export", "-model", model)
        (out / "source.key").write_text(export_key + "\n", encoding="utf-8")
        steps.append(f"exported in {time.monotonic() - started:.1f} s")
    if force or not _current(out / "cut.key", cut_key, out / "cut.json"):
        started = time.monotonic()
        cut = recipe.cut(Source(out / "source.json"))
        cut.write(out / "cut.json")
        (out / "cut.key").write_text(cut_key + "\n", encoding="utf-8")
        steps.append(f"cut in {time.monotonic() - started:.1f} s")
    if force or not _current(out / "bake.key", bake_key, out / f"{model}.asset"):
        started = time.monotonic()
        _unity(wt, "Bake", "-model", model, *(("-rig",) if rig else ()))
        (out / "bake.key").write_text(bake_key + "\n", encoding="utf-8")
        steps.append(f"baked in {time.monotonic() - started:.1f} s")
    if steps:
        setup.save_baked(wt)
    summary = json.loads((out / "cut.json").read_text(encoding="utf-8"))
    summary["steps"] = steps or ["nothing changed"]
    return summary


def thresholds(recipe: ModuleType) -> dict:
    role = getattr(recipe, "ROLE", "fodder")
    if role not in THRESHOLDS:
        raise EdgeCutError(f"{recipe.MODEL.id}: unknown ROLE {role!r} (one of {', '.join(THRESHOLDS)})")
    return {"role": role, **THRESHOLDS[role]}


def judge(review: dict, limits: dict) -> list[tuple[str, str, bool]]:
    """Each threshold as (what, measured, passed): the mean black share over the game-scale poses (not a
    structure's), the segment count and the body's triangles."""
    lo, hi = limits["triangles"]
    verdicts = []
    if "black_share" in limits:
        game = [c["blackShare"] for c in review["cells"] if c["size"] == "game"]
        share = sum(game) / len(game)
        verdicts.append((f"black share at game scale >= {limits['black_share']:.0%}",
                         f"{share:.1%} (poses {', '.join(f'{s:.0%}' for s in game)})", share >= limits["black_share"]))
    return verdicts + [
        (f"line segments <= {limits['segments']}", str(review["segments"]), review["segments"] <= limits["segments"]),
        (f"body triangles {lo}-{hi}", str(review["triangles"]), lo <= review["triangles"] <= hi),
    ]


def review(wt: paths.Worktree, model: str, out: Path | None = None, force: bool = False) -> bool:
    """Bakes if needed, renders the review into `out` (the evidence folder by default) and writes its README;
    whether every threshold passed."""
    recipe = load_recipe(wt, model)
    summary = bake(wt, model, force)
    out = out or evidence_dir(wt, model)
    out.mkdir(parents=True, exist_ok=True)
    limits = thresholds(recipe)
    # The hero is drawn in its own white (design.md, The Look > Principles), which blooms more than a monster's colour.
    white = palette.roles(palette.load())["hero.body"]
    tint = ("-tint", "".join(f"{c:02x}" for c in white)) if limits["role"] == "hero" else ()
    _unity(wt, "Review", "-model", model, "-out", str(out.resolve()), *tint)
    result = json.loads((out / "review.json").read_text(encoding="utf-8"))
    verdicts = judge(result, limits)
    write = structure_readme if recipe.MODEL.static else readme
    (out / "README.md").write_text(write(recipe, summary, result, limits, verdicts), encoding="utf-8")
    for what, measured, ok in verdicts:
        print(f"edgecut review: {model}: {what}: {measured} {'ok' if ok else 'FAILED'}")
    return all(ok for _, _, ok in verdicts)


def readme(recipe: ModuleType, summary: dict, result: dict, limits: dict, verdicts: list) -> str:
    m = recipe.MODEL
    cam = result["camera"]
    lines = [
        f"# Edge cut: {m.id}",
        "",
        (f"Written by `champions edgecut review {m.id}` from the recipe "
         f"[tools/recipes/edgecut/{m.id}.py](../../../tools/recipes/edgecut/{m.id}.py) (role {limits['role']})."),
        "",
        (recipe.__doc__ or "").strip(),
        "",
        "## Thresholds (plan.md, Rendering > Edge cuts)",
        "",
        "| Threshold | Measured | |",
        "| --- | --- | --- |",
        *[f"| {what} | {measured} | {'ok' if ok else 'FAILED'} |" for what, measured, ok in verdicts],
        "",
        f"{result['pieces']} shard pieces; the bone texture holds {result['entries']} entries.",
        "",
        "## Sheets",
        "",
        (f"The edge crowd draws the cut at the game camera's angle ({cam['pitchDeg']:g} degrees down, a "
        f"{cam['fovDeg']:g} degree field, the monster turned {cam['yawDeg']:g} degrees) with the pipeline's post, "
        "beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game "
        "scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells "
        "twice the larger of the monster's height and its reach at a 1080-row frame's pixel density, three poses to "
        f"a row ({', '.join(m.reviewPoses)}), each the original then the cut, and a last row of the shatter: the first pose "
        "broken by a blow away from the camera, then 0.08, 0.2, 0.4, 0.8 and 1.4 s after (as many as the row holds). "
        "Game scale is the old game camera's medium width; the world bead's camera may differ."),
        "",
    ]
    for size in dict.fromkeys(c["size"] for c in result["cells"]):
        px = next(c["heightPx"] for c in result["cells"] if c["size"] == size)
        scale = result.get("sheetScales", {}).get(size, 1)
        saved = f", saved at 1/{round(1 / scale)} size" if scale < 1 else ""
        lines += [f"- {size} ({px:g} px tall{saved}): [{size}.jpg]({size}.jpg)"]
    lines += [
        "",
        f"## Black share per cell (the cut's silhouette darker than L* {result['blackLStar']:g})",
        "",
        "| Size | Pose | Black share | Without post | Silhouette px | On screen px |",
        "| --- | --- | --- | --- | --- | --- |",
        *[f"| {c['size']} | {c['pose']} | {c['blackShare']:.1%} | {c['blackShareWithoutPost']:.1%} | {c['silhouettePx']} "
          f"| {c['screenHeightPx']} |" for c in result["cells"]],
        "",
        "## Parts",
        "",
        "| Part | Body triangles | Line segments |",
        "| --- | --- | --- |",
        *[f"| {name} | {p['triangles']} | {p['segments']} |" for name, p in summary["parts"].items()],
        "",
    ]
    return "\n".join(lines)


def structure_readme(recipe: ModuleType, summary: dict, result: dict, limits: dict, verdicts: list) -> str:
    m = recipe.MODEL
    cam = result["camera"]
    lines = [
        f"# Edge cut: {m.id} (a structure)",
        "",
        (f"Written by `champions edgecut review {m.id}` from the recipe "
         f"[tools/recipes/edgecut/{m.id}.py](../../../tools/recipes/edgecut/{m.id}.py)."),
        "",
        (recipe.__doc__ or "").strip(),
        "",
        "## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)",
        "",
        "| Threshold | Measured | |",
        "| --- | --- | --- |",
        *[f"| {what} | {measured} | {'ok' if ok else 'FAILED'} |" for what, measured, ok in verdicts],
        "",
        (f"{result['pieces']} shard pieces. {summary['extentM'][0]:g} by {summary['extentM'][1]:g} m on the ground "
         f"(x by z, its footprint's sizeM in Content/structures.json is the sim's), {summary['topM']:g} m tall."),
        "",
        "## Sheets",
        "",
        (f"The structure field draws the cut at the game camera's angle ({cam['pitchDeg']:g} degrees down, a "
         f"{cam['fovDeg']:g} degree field) with the pipeline's post, in the first zone's fill and edge, beside the "
         "vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a "
         "1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at "
         "the pixel density of a 1080-row "
         f"frame: the structure turned {', '.join(f'{y:g}' for y in cam['headingsDeg'])} degrees, each the original then "
         "the cut."),
        "",
    ]
    for size in dict.fromkeys(c["size"] for c in result["cells"]):
        lines += [f"- {size}: [{size}.jpg]({size}.jpg)"]
    lines += [
        "",
        "## Parts",
        "",
        "| Part | Body triangles | Line segments |",
        "| --- | --- | --- |",
        *[f"| {name} | {p['triangles']} | {p['segments']} |" for name, p in summary["parts"].items()],
        "",
    ]
    return "\n".join(lines)


def main(words: list[str], force: bool, out: str | None) -> int:
    wt = paths.worktree()
    try:
        code = setup.ensure(wt)
        if code != 0:
            return code
        if words[0] == "review":
            if len(words) != 2:
                raise EdgeCutError("usage: champions edgecut review <model>")
            return 0 if review(wt, words[1], Path(out) if out else None, force) else 1
        if len(words) != 1:
            raise EdgeCutError("usage: champions edgecut <model> | champions edgecut review <model>")
        s = bake(wt, words[0], force)
        print(f"edgecut: {words[0]}: {s['triangles']} body triangles, {s['segments']} line segments, {s['pieces']} pieces "
              f"({'; '.join(s['steps'])})")
        return 0
    except (EdgeCutError, setup.SetupError, plugin.PluginError) as e:
        print(f"edgecut: {e}", file=sys.stderr)
        return 2
