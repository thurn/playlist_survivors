"""`champions <command>`: Rhythm Champions' tools (`just champions-tool <command>`, or
`uv run --project rhythm_champions/tools champions <command>`).

Commands:
  setup               prepare this worktree: vendor files, baked cache, warm Library, the plugin,
                      the pre-push guard, the sync record, the Unity license
  build               set up if needed, then build the Mac player (--backend mono|il2cpp, default
                      mono; --out DIR, --development, --scene PATH); nothing if nothing changed
                      (--force); --clean-cache rebuilds with a clean build cache
  play [FLAGS]        play the last build at 1080p (--windowed for a window) with player flags;
                      --headless runs it windowless in batch mode and waits for it to exit
                      (player flags: --file PATH or --app NAME, the music, played 3 s late;
                      --seconds N, --fixed-time, --capture-stills DIR --stills-at T,..)
  test                EditMode tests (warm editor if running, else cold; --no-skips fails on a
                      skipped test)
  editor [start|stop|status]
                      the warm editor for this worktree, which build, test and unity then use
  check               every check before a push: the guard, the EditMode tests (cold) and the
                      variant budget; or only those named (--guard, --tests, --variants)
  monster bake TYPE   bake a monster type (or `all`) for both looks: `--look trailer`'s from the vendor mesh,
                      the default's from tools/recipes/monster/TYPE.py if it exists (--force bakes again)
  monster review TYPE the type's review sheets and README in evidence/monsters/TYPE/ (--look trailer for the
                      trailer's; always a side-by-side of both; --out DIR)
  structure bake KIND bake a structure kind (or `all`) for both looks: `--look trailer`'s the Synty model
                      unsimplified, the default's as Editor/Structures/structures.json tunes it (--force)
  structure review KIND
                      the kind's (or `all`'s) review sheets into evidence/structures/ (--out DIR)
  stills              fixed-time stills of the scripted scenes in one headless launch of the last
                      build, with their type-ID passes and views (--scene NAME, repeatable; --out DIR,
                      default captures/stills; --look trailer for the trailer's monsters)
  look [STILLS]       the look metrics (docs/champions/style.md) on stills (default captures/stills):
                      every threshold key per still, the palette's rules; --out DIR writes metrics.md,
                      metrics.json and evidence JPEGs; exits 1 on a broken rule
  unity METHOD ...    run a batch-mode task method (warm editor if running, else cold)
  guard               the vendor guard (--tree REV, --index, --range A..B, --pre-push REMOTE)
  sync                fill the vendor folder from the manifest (--record-only: just the record)
  manifest find|add   list source assets by glob, or add them to the manifest
  library save        save this worktree's Library as the warm one for its packages (--built: after a
                      player build, with its shader cache)
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

from . import guard, paths, plugin, setup, unity

VARIANTS_REPORT = "shader-variants.json"  # ShaderVariantBudget.ReportName, beside the player


def cmd_setup(a: argparse.Namespace) -> int:
    try:
        return setup.setup(paths.worktree(), run_unity=not a.no_unity, fresh_library=a.fresh_library)
    except (setup.SetupError, plugin.PluginError) as e:
        print(f"setup: {e}", file=sys.stderr)
        return 2


def cmd_guard(a: argparse.Namespace) -> int:
    repo = paths.git_root()
    record = Path(a.record) if a.record else None
    if a.pre_push:
        res = guard.run("pre-push", repo, None, a.pre_push, record, sys.stdin.read().splitlines())
    elif a.range:
        res = guard.run("range", repo, a.range, None, record)
    elif a.index:
        res = guard.run("index", repo, None, None, record)
    else:
        res = guard.run("tree", repo, a.tree or "HEAD", None, record)
    res.report(sys.stderr if a.pre_push else sys.stdout)
    return 0 if res.ok else 1


def build(wt: paths.Worktree, backend: str = "mono", out: str | None = None, development: bool = False,
          scenes: list[str] | None = None, cold: bool = False, force: bool = False,
          clean_cache: bool = False) -> int:
    try:
        code = setup.ensure(wt)
    except (setup.SetupError, plugin.PluginError) as e:
        print(f"build: {e}", file=sys.stderr)
        return 2
    if code != 0:
        return code
    args = ["-platform", "mac", "-backend", backend]
    if out:
        args += ["-buildOut", out]
    if development:
        args.append("-development")
    for s in scenes or []:
        args += ["-scene", s]
    folder = setup.player_dir(wt, backend, out)
    if not force and not clean_cache and setup.player_current(wt, folder, args):
        shown = folder.relative_to(wt.project) if folder.is_relative_to(wt.project) else folder
        print(f"build: nothing changed since the last build into {shown}; not rebuilding (--force to)")
        return 0
    from . import monster

    try:
        monster.prepare_all(wt)  # the recipes' meshes, which the player build's bake takes
    except monster.MonsterError as e:
        print(f"build: {e}", file=sys.stderr)
        return 2
    setup.forget_build(folder)
    code = unity.run_method(wt, "BuildPlayer", args + (["-cleanBuildCache"] if clean_cache else []), cold=cold)
    if code != 0:
        return code
    setup.record_build(wt, folder, args)
    saved = setup.save_baked(wt)
    if saved:
        print(f"build: saved Assets/Baked to the cache ({saved})")
    if not scenes and not unity.warm_server(wt):
        compiled = setup.compiled_variants(wt.logs / "BuildPlayer.log")
        dest = setup.save_built_library(wt, compiled)
        if dest:
            print(f"build: compiled {compiled} shader variants; saved the Library, with its shader cache, to {dest}")
    return 0


def cmd_build(a: argparse.Namespace) -> int:
    return build(paths.worktree(), a.backend, a.out, a.development, a.scene, a.cold, a.force, a.clean_cache)


PATH_FLAGS = ("--file", "--capture-stills", "--bench-out", "--record", "--replay")


def absolute_file_flags(flags: list[str], cwd: Path) -> list[str]:
    """The player flags with their paths (PATH_FLAGS; `--replay`'s comma-separated) made absolute against `cwd`: the
    player runs from elsewhere."""
    out = list(flags)
    for i, flag in enumerate(out[:-1]):
        if flag in PATH_FLAGS:
            out[i + 1] = ",".join(str((cwd / p).resolve()) for p in out[i + 1].split(",")) if flag == "--replay" \
                else str((cwd / out[i + 1]).resolve())
    return out


def headless(wt: paths.Worktree, app: Path, player_flags: list[str], log_name: str = "player.log") -> int:
    """Runs the player windowless in batch mode with the player flags and waits for it; its exit code."""
    wt.logs.mkdir(parents=True, exist_ok=True)
    log = wt.logs / log_name
    # Batch mode draws with the Null device unless told otherwise, and so does a player given
    # `--headless`, which Unity reads too: stills need Metal. The game counts batch mode as headless.
    cmd = [str(app.parent / setup.APP_EXE), "-batchmode", "-force-metal", "-logFile", str(log), *player_flags]
    started = time.monotonic()
    code = subprocess.run(cmd, check=False, cwd=wt.root, stdout=subprocess.DEVNULL).returncode
    print(f"play: the player exited {code} after {time.monotonic() - started:.1f} s (log {log.relative_to(wt.root)})")
    return code


def cmd_play(a: argparse.Namespace, player_flags: list[str]) -> int:
    wt = paths.worktree()
    player_flags = absolute_file_flags(player_flags, Path.cwd())
    app = setup.player_dir(wt, a.backend) / setup.APP
    if not app.is_dir():
        print(f"play: no {a.backend} player at {app}; run `just champions-build`", file=sys.stderr)
        return 2
    if a.headless:
        return headless(wt, app, player_flags)
    flags = ["-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0" if a.windowed else "1"]
    print(f"play: {app.relative_to(wt.root)}")
    return subprocess.run(["open", "-n", str(app), "--args", *flags, *player_flags], check=False).returncode


def cmd_test(a: argparse.Namespace) -> int:
    s = unity.run_tests(paths.worktree(), a.assembly or [], cold=a.cold)
    print(f"tests: {s.passed}/{s.total} passed, {s.failed} failed, {s.skipped} skipped\n{s.failures}")
    if a.no_skips and s.skipped:
        print(f"tests: FAILED, since --no-skips and these were skipped:\n{s.skips}")
        return 1
    return 0 if s.ok else 1


def cmd_editor(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    if a.action == "start":
        started = time.monotonic()
        s = unity.editor_start(wt)
        print(f"editor: running (pid {s.pid}, port {s.port}) after {time.monotonic() - started:.1f} s")
    elif a.action == "stop":
        print("editor: stopped" if unity.editor_stop(wt) else "editor: not running")
    else:
        s = unity.warm_server(wt)
        print(f"editor: running (pid {s.pid}, port {s.port})" if s else "editor: not running")
    return 0


def check_variants(wt: paths.Worktree, build_first: bool) -> bool:
    """The variant budget (plan.md, Rendering > Pipeline) on the Mono player: built first with a
    clean build cache, so every shader's variants are counted afresh."""
    report = setup.player_dir(wt, "mono") / VARIANTS_REPORT
    if build_first:
        report.unlink(missing_ok=True)  # a build that fails before counting leaves none to misread
        if build(wt, "mono", cold=True, clean_cache=True) != 0:
            print("check: the variant budget's build FAILED (over budget, or see Logs/BuildPlayer.log)")
            if report.exists():
                print_variants(json.loads(report.read_text(encoding="utf-8")))
            return False
    if not report.exists():
        print(f"check: no variant report at {report}; build the Mono player first")
        return False
    r = json.loads(report.read_text(encoding="utf-8"))
    print_variants(r)
    return r["total"] <= r["budget"]


def print_variants(r: dict) -> None:
    ok = r["total"] <= r["budget"]
    print(f"check: {r['total']} compiled shader variants, budget {r['budget']}: {'ok' if ok else 'OVER BUDGET'}"
          f"{'' if r.get('fresh') else ' (counted on an incremental build)'}")
    shaders: dict[str, int] = {}
    for s in r["snippets"]:
        shaders[s["shader"]] = shaders.get(s["shader"], 0) + s["variants"]
    for name, n in sorted(shaders.items(), key=lambda kv: -kv[1])[:5]:
        print(f"  {n:4d}  {name}")


def cmd_check(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    every = not (a.guard or a.tests or a.variants)
    results: list[tuple[str, bool]] = []
    if every or a.guard:
        res = guard.run("tree", wt.root, "HEAD", None, None)
        res.report()
        idx = guard.run("index", wt.root, None, None, None)
        if not idx.ok:
            idx.report()
        results.append(("vendor guard", res.ok and idx.ok))
    if (every or a.tests or a.variants) and unity.editor_stop(wt):
        print("check: stopped the warm editor; Unity checks run cold")
    if every or a.tests:
        summary = unity.run_tests(wt, [], cold=True)
        print(f"check: EditMode tests: {summary.passed}/{summary.total} passed, {summary.failed} failed\n{summary.failures}")
        results.append(("editmode tests", summary.ok))
    if every or a.variants:
        results.append(("variant budget", check_variants(wt, build_first=not a.no_build)))
    print("check: " + ", ".join(f"{n} {'ok' if ok else 'FAILED'}" for n, ok in results))
    return 0 if all(ok for _, ok in results) else 1


def cmd_stills(a: argparse.Namespace) -> int:
    from . import stills

    return stills.cmd_stills(a)


def cmd_monster(a: argparse.Namespace) -> int:
    from . import monster

    return monster.cmd_monster(a)


STRUCTURE_TASKS = "Champions.EditorTools.Structures.StructureTasks"


def cmd_structure(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    if a.action == "bake":
        return unity.run_method(wt, f"{STRUCTURE_TASKS}.Bake", ["-kind", a.kind] + (["-force"] if a.force else []))
    out = Path(a.out).resolve() if a.out else wt.project / "evidence" / "structures"
    return unity.run_method(wt, f"{STRUCTURE_TASKS}.Review", ["-kind", a.kind, "-out", str(out)])


def cmd_look(a: argparse.Namespace) -> int:
    from . import look

    return look.cmd_look(a)


def cmd_unity(a: argparse.Namespace) -> int:
    return unity.run_method(paths.worktree(), a.method, a.args, cold=a.cold)


def cmd_library(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    if unity.warm_server(wt):
        print("library: stop the warm editor first", file=sys.stderr)
        return 1
    dest = setup.save_library(wt)
    if a.built:
        setup.built_marker(dest).write_text(time.strftime("%Y-%m-%dT%H:%M:%S") + "\n", encoding="utf-8")
    print(f"library: saved to {dest}{', with a build' if a.built else ''}")
    return 0


def cmd_sync(a: argparse.Namespace) -> int:
    from . import sync

    return sync.main(a)


def cmd_manifest(a: argparse.Namespace) -> int:
    from . import sync

    wt = paths.worktree()
    cache = paths.cache_root()
    sources = sync.Sources(paths.load_roots(create=True), cache, sync.HashCache(cache / "source-hashes.json"))
    try:
        if a.action == "find":
            for p in sync.find(a.source, a.patterns[0], sources):
                print(p)
            return 0
        n = sync.manifest_add(wt.project / "manifest.json", a.source, a.patterns, a.use or "", sources)
        print(f"manifest: {n} new entries")
        return 0
    except sync.SyncError as e:
        print(f"manifest: {e}", file=sys.stderr)
        return 2
    finally:
        sources.hashes.save()


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="champions", description=__doc__, allow_abbrev=False,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="command", required=True)

    s = sub.add_parser("setup")
    s.add_argument("--no-unity", action="store_true", help="skip opening the project")
    s.add_argument("--fresh-library", action="store_true", help="replace an existing Library")
    s.set_defaults(func=cmd_setup)

    b = sub.add_parser("build")
    b.add_argument("--backend", choices=["mono", "il2cpp"], default="mono")
    b.add_argument("--out")
    b.add_argument("--scene", action="append")
    b.add_argument("--development", action="store_true")
    b.add_argument("--cold", action="store_true")
    b.add_argument("--force", action="store_true", help="build even if nothing changed since the last build")
    b.add_argument("--clean-cache", action="store_true", help="build with a clean build cache")
    b.set_defaults(func=cmd_build)

    pl = sub.add_parser("play", allow_abbrev=False, help="play the last build; unknown flags go to the player")
    pl.add_argument("--backend", choices=["mono", "il2cpp"], default="mono")
    pl.add_argument("--headless", action="store_true", help="windowless, in batch mode; waits for the player to exit")
    pl.add_argument("--windowed", action="store_true", help="in a 1080p window rather than fullscreen")
    pl.set_defaults(func=cmd_play, player_flags=True)

    t = sub.add_parser("test")
    t.add_argument("--assembly", action="append")
    t.add_argument("--cold", action="store_true")
    t.add_argument("--no-skips", action="store_true", help="fail if any test is skipped")
    t.set_defaults(func=cmd_test)

    e = sub.add_parser("editor")
    e.add_argument("action", nargs="?", choices=["start", "stop", "status"], default="start")
    e.set_defaults(func=cmd_editor)

    c = sub.add_parser("check")
    c.add_argument("--guard", action="store_true", help="the vendor guard over HEAD and the index")
    c.add_argument("--tests", action="store_true", help="the EditMode tests, cold")
    c.add_argument("--variants", action="store_true",
                   help="the shader variant budget: a Mono build with a clean build cache, and its count")
    c.add_argument("--no-build", action="store_true", help="--variants: read the last Mono build's report")
    c.set_defaults(func=cmd_check)

    st = sub.add_parser("stills", help="fixed-time stills of the scripted scenes, with their ID passes")
    st.add_argument("--scene", action="append", help="a scene to play (repeatable; default all)")
    st.add_argument("--out", help="the stills folder (default captures/stills)")
    st.add_argument("--backend", choices=["mono", "il2cpp"], default="mono")
    st.add_argument("--look", choices=["default", "trailer"], default="default",
                    help="the monsters' drawing: trailer for the neon edge trailer's")
    st.set_defaults(func=cmd_stills)

    mo = sub.add_parser("monster", help="bake a monster type, or review one (monster review TYPE)")
    mo.add_argument("action", choices=["bake", "review"])
    mo.add_argument("type", help="a type in Assets/Champions/Editor/Monsters/monsters.json, or all")
    mo.add_argument("--look", choices=["default", "trailer"], default="default", help="review: the look the sheets draw")
    mo.add_argument("--out", help="review: the folder for the sheets (default evidence/monsters/TYPE)")
    mo.add_argument("--force", action="store_true", help="bake again even if nothing changed")
    mo.set_defaults(func=cmd_monster)

    sr = sub.add_parser("structure", help="bake a structure kind, or review one (structure review KIND)")
    sr.add_argument("action", choices=["bake", "review"])
    sr.add_argument("kind", help="a kind in Assets/Champions/Editor/Structures/structures.json, or all")
    sr.add_argument("--out", help="review: the folder for the sheets (default evidence/structures)")
    sr.add_argument("--force", action="store_true", help="bake again even if nothing changed")
    sr.set_defaults(func=cmd_structure)

    lk = sub.add_parser("look", help="a diagnostic: where stills stray from the style's guide values (never fails)")
    lk.add_argument("stills", nargs="?", help="a folder of stills (default captures/stills)")
    lk.add_argument("--palette", help="the palette (default rhythm_champions/Content/palette.json)")
    lk.add_argument("--thresholds", help="the guide values (default docs/champions/style-thresholds.json)")
    lk.add_argument("--out", help="the evidence folder: metrics.md, metrics.json and a JPEG per still")
    lk.add_argument("--jobs", type=int, choices=range(1, 9), default=1, metavar="1..8",
                    help="parallel image workers (default 1; CI uses 4)")
    lk.add_argument("--impact", action="append", help="a glob of still names that are impact frames")
    lk.add_argument("--title", help="metrics.md's title (default the folder's name)")
    lk.set_defaults(func=cmd_look)

    u = sub.add_parser("unity")
    u.add_argument("method")
    u.add_argument("args", nargs=argparse.REMAINDER)
    u.add_argument("--cold", action="store_true")
    u.set_defaults(func=cmd_unity)

    g = sub.add_parser("guard")
    g.add_argument("--tree", metavar="REV")
    g.add_argument("--index", action="store_true")
    g.add_argument("--range", metavar="A..B")
    g.add_argument("--pre-push", metavar="REMOTE")
    g.add_argument("url", nargs="?", help="the remote's URL (pre-push hooks pass it; unused)")
    g.add_argument("--record", help="the sync record (default: the machine's cache)")
    g.set_defaults(func=cmd_guard)

    y = sub.add_parser("sync")
    y.add_argument("--record-only", action="store_true")
    y.add_argument("--out")
    y.set_defaults(func=cmd_sync)

    m = sub.add_parser("manifest", help="find source assets, or add them to the manifest")
    m.add_argument("action", choices=["find", "add"])
    m.add_argument("source", help="synty:<package file>, unity_assets or more_assets")
    m.add_argument("patterns", nargs="+", help="glob(s) over Unity paths (Assets/...)")
    m.add_argument("--use", help="what the game uses it for")
    m.set_defaults(func=cmd_manifest)

    lib = sub.add_parser("library")
    lib.add_argument("action", choices=["save"])
    lib.add_argument("--built", action="store_true",
                     help="it holds a player build's shader cache (saved after `build`, as slow CI does)")
    lib.set_defaults(func=cmd_library)
    return p


def main(argv: list[str] | None = None) -> None:
    if shutil.which("git") is None:
        sys.exit("champions: git is not on PATH")
    argv = sys.argv[1:] if argv is None else argv
    args, extra = build_parser().parse_known_args(argv)
    if getattr(args, "player_flags", False):
        sys.exit(args.func(args, extra))
    if extra:
        build_parser().error(f"unrecognized arguments: {' '.join(extra)}")
    sys.exit(args.func(args))
