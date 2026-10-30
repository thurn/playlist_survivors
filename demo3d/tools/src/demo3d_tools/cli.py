"""`demo3d <command>`: the 3D tech demo's tools (run with `uv run --project demo3d/tools demo3d`).

`demo2d <command>` (`uv run --project demo3d/tools demo2d`) runs the same tools on the 2D demo's
project, `demo2d/`: setup, guard, check, unity, test, build, library, sync and manifest. The
guard is the same either way; it checks the whole repository. `stylelab <command>` does the same
for the shader style prototype, `stylelab/` (docs/tech-demo-3d/stylelab.md).

Commands:
  setup               prepare this worktree (vendor, baked, warm Library, guard hook, license)
  guard               the vendor guard (--tree REV, --index, --range A..B, --pre-push REMOTE)
  check               every check before a push: guard, dotnet test, cold Unity compile and
                      EditMode tests, and with --player an IL2CPP player build
  unity METHOD ...    run a batch-mode task method (warm editor if running, else cold)
  test                EditMode tests (warm editor if running, else cold; --no-skips fails on
                      a skipped test, as CI does)
  build               build the player (--platform windows|mac, --backend il2cpp|mono, --out DIR)
  editor start|stop|status
                      the warm editor for this worktree
  library save        save this worktree's Library as the warm one for its packages
  sync                fill a vendor folder from the manifest (--record-only for the Mac)
  manifest find|add   list source assets by glob, or add them to the manifest
  mix                 the offline mix of a video capture (the song plus the timeline's sounds)
  flip A B            FLIP scores of two capture folders' stills (calibrate: the A/B thresholds)
  sweep SPEC...       a benchmark sweep in the windowed player (one process per spec), then its
                      report: takes the machine lock first; --out DIR, --backend, --build,
                      --screen WxH and --workers N (to test refusals), --no-lock
  report DIR          regenerate a sweep folder's report.md from its rows
  look SLUG           the look pass's set on the Mac, windowless: song stills, TD15's beauty shots,
                      their stats and |dL*| readability (--baseline SLUG for a contact sheet)
  pix SPEC            a PIX GPU capture at one point (--point n=..,viewWidthM=..,load=..,aa=..) and
                      its per-pass GPU summary
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import time
from pathlib import Path

from . import guard, paths, setup, unity


def cmd_setup(a: argparse.Namespace) -> int:
    try:
        return setup.setup(paths.worktree(name=a.project), song=a.song, run_unity=not a.no_unity,
                           fresh_library=a.fresh_library, rosters=tuple(a.roster or ()))
    except setup.SetupError as e:
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


def dotnet_test(wt: paths.Worktree) -> bool:
    proj = wt.project / "dotnet" / f"{wt.namespace}.Choreography.Tests"
    if not proj.exists():
        print(f"check: dotnet test skipped ({wt.name} has no engine-free tests)")
        return True
    started = time.monotonic()
    code = subprocess.run(["dotnet", "test", str(proj), "--nologo", "-v", "q"], check=False).returncode
    print(f"check: dotnet test {'passed' if code == 0 else 'FAILED'} in {time.monotonic() - started:.1f} s")
    return code == 0


def cmd_check(a: argparse.Namespace) -> int:
    wt = paths.worktree(name=a.project)
    results: list[tuple[str, bool]] = []
    res = guard.run("tree", wt.root, "HEAD", None, None)
    res.report()
    idx = guard.run("index", wt.root, None, None, None)
    if not idx.ok:
        idx.report()
    results.append(("vendor guard", res.ok and idx.ok))
    results.append(("dotnet test", dotnet_test(wt)))
    if unity.editor_stop(wt):
        print("check: stopped the warm editor; Unity checks run cold before a push")
    summary = unity.run_tests(wt, [], cold=True)
    print(f"check: Unity compile and EditMode tests: {summary.passed}/{summary.total} passed, "
          f"{summary.failed} failed\n{summary.failures}")
    results.append(("unity editmode (cold)", summary.ok))
    if a.player:
        platform = "windows" if paths.is_windows() else "mac"
        code = unity.run_method(wt, "BuildPlayer", ["-platform", platform, "-backend", "il2cpp"], cold=True)
        results.append((f"il2cpp player build ({platform})", code == 0))
    print("check: " + ", ".join(f"{n} {'ok' if ok else 'FAILED'}" for n, ok in results))
    return 0 if all(ok for _, ok in results) else 1


def cmd_unity(a: argparse.Namespace) -> int:
    wt = paths.worktree(name=a.project)
    code = unity.run_method(wt, a.method, a.args, cold=a.cold)
    if code == 0 and a.method == "BakeAll" and not a.args and wt.name == "demo3d":
        setup.record_baked(wt)
    return code


def cmd_test(a: argparse.Namespace) -> int:
    s = unity.run_tests(paths.worktree(name=a.project), a.assembly or [], cold=a.cold)
    print(f"tests: {s.passed}/{s.total} passed, {s.failed} failed, {s.skipped} skipped\n{s.failures}")
    if a.no_skips and s.skipped:
        print(f"tests: FAILED, since --no-skips and these were skipped:\n{s.skips}")
        return 1
    return 0 if s.ok else 1


def cmd_build(a: argparse.Namespace) -> int:
    platform = a.platform or ("windows" if paths.is_windows() else "mac")
    args = ["-platform", platform, "-backend", a.backend]
    if a.out:
        args += ["-buildOut", a.out]
    if a.development:
        args.append("-development")
    for s in a.scene or []:
        args += ["-scene", s]
    wt = paths.worktree(name=a.project)
    # A player built before BakeAll starts, but hangs on its first missing baked asset; one built
    # on a stale bake throws on the first field the bake predates.
    if not setup.baked(wt):
        print("build: this worktree isn't baked, or its bakers' inputs changed; running BakeAll first")
        code = unity.run_method(wt, "BakeAll", [], cold=a.cold)
        if code != 0:
            return code
        setup.record_baked(wt)
    code = unity.run_method(wt, "BuildPlayer", args, cold=a.cold)
    if code == 0 and not a.scene and not unity.warm_server(wt):
        compiled = setup.compiled_variants(wt.logs / "BuildPlayer.log")
        dest = setup.save_built_library(wt, compiled)
        if dest:
            print(f"build: compiled {compiled} shader variants; saved the Library, with its "
                  f"shader cache, to {dest}")
    return code


def cmd_editor(a: argparse.Namespace) -> int:
    wt = paths.worktree(name=a.project)
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


def cmd_library(a: argparse.Namespace) -> int:
    wt = paths.worktree(name=a.project)
    if unity.warm_server(wt):
        print("library: stop the warm editor first", file=sys.stderr)
        return 1
    print(f"library: saved to {setup.save_library(wt)}")
    return 0


def cmd_sync(a: argparse.Namespace) -> int:
    from . import sync

    return sync.main(a)


def cmd_manifest(a: argparse.Namespace) -> int:
    from . import sync

    wt = paths.worktree(name=a.project)
    cache = paths.cache_root()
    sources = sync.Sources(paths.load_roots(create=True), cache,
                           sync.HashCache(cache / "source-hashes.json"))
    try:
        if a.action == "find":
            for p in sync.find(a.source, a.patterns[0], sources):
                print(p)
            return 0
        n = sync.manifest_add(wt.project / "manifest.json", a.source, a.patterns, a.use or "",
                              sources, a.roster)
        print(f"manifest: {n} new entries")
        return 0
    except sync.SyncError as e:
        print(f"manifest: {e}", file=sys.stderr)
        return 2
    finally:
        sources.hashes.save()
def cmd_sweep(a: argparse.Namespace) -> int:
    from . import sweep

    return sweep.cmd_sweep(a)


def cmd_report(a: argparse.Namespace) -> int:
    from . import report

    return report.cmd_report(a)


def cmd_pix(a: argparse.Namespace) -> int:
    from . import pix

    return pix.cmd_pix(a)


def cmd_look(a: argparse.Namespace) -> int:
    from . import look

    return look.cmd_look(a)


def cmd_look_sheet(a: argparse.Namespace) -> int:
    from . import look

    return look.cmd_look_sheet(a)


def cmd_mix(a: argparse.Namespace) -> int:
    from . import mix

    return mix.main(a.args)


def cmd_flip(a: argparse.Namespace) -> int:
    from . import flip

    return flip.main(a.args)


def build_parser(project: str = "demo3d") -> argparse.ArgumentParser:
    """The commands for `project`; the sweep, PIX, capture and warm-editor tools are demo3d's."""
    p = argparse.ArgumentParser(prog=project, description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.set_defaults(project=project)
    sub = p.add_subparsers(dest="command", required=True)

    s = sub.add_parser("setup")
    s.add_argument("--song", help="the Action MP3, if not at the machine's default path")
    s.add_argument("--no-unity", action="store_true", help="skip opening the project")
    s.add_argument("--fresh-library", action="store_true", help="replace an existing Library")
    s.add_argument("--roster", action="append", choices=paths.ROSTERS,
                   help="also sync a roster set's packs (demo3d; needs the more_assets root)")
    s.set_defaults(func=cmd_setup)

    g = sub.add_parser("guard")
    g.add_argument("--tree", metavar="REV")
    g.add_argument("--index", action="store_true")
    g.add_argument("--range", metavar="A..B")
    g.add_argument("--pre-push", metavar="REMOTE")
    g.add_argument("url", nargs="?", help="the remote's URL (pre-push hooks pass it; unused)")
    g.add_argument("--record", help="the sync record (default: the machine's cache)")
    g.set_defaults(func=cmd_guard)

    c = sub.add_parser("check")
    c.add_argument("--player", action="store_true", help="also build this machine's IL2CPP player")
    c.set_defaults(func=cmd_check)

    u = sub.add_parser("unity")
    u.add_argument("method")
    u.add_argument("args", nargs=argparse.REMAINDER)
    u.add_argument("--cold", action="store_true")
    u.set_defaults(func=cmd_unity)

    t = sub.add_parser("test")
    t.add_argument("--assembly", action="append")
    t.add_argument("--cold", action="store_true")
    t.add_argument("--no-skips", action="store_true",
                   help="fail if any test is skipped (a missing setup step or bake, in CI)")
    t.set_defaults(func=cmd_test)

    b = sub.add_parser("build")
    b.add_argument("--platform", choices=["windows", "mac"],
                   help="the player's platform (default: this machine's)")
    b.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    b.add_argument("--out")
    b.add_argument("--scene", action="append")
    b.add_argument("--development", action="store_true")
    b.add_argument("--cold", action="store_true")
    b.set_defaults(func=cmd_build)

    lib = sub.add_parser("library")
    lib.add_argument("action", choices=["save"])
    lib.set_defaults(func=cmd_library)

    m = sub.add_parser("manifest", help="find source assets, or add them to the manifest")
    m.add_argument("action", choices=["find", "add"])
    m.add_argument("source", help="synty:<package file>, unity_assets, pixel_assets or more_assets")
    m.add_argument("patterns", nargs="+", help="glob(s) over Unity paths (Assets/...)")
    m.add_argument("--use", help="what the demo uses it for")
    m.add_argument("--roster", choices=paths.ROSTERS,
                   help="the roster set the entries belong to (required for more_assets)")
    m.set_defaults(func=cmd_manifest)

    y = sub.add_parser("sync")
    y.add_argument("--record-only", action="store_true")
    y.add_argument("--out")
    y.set_defaults(func=cmd_sync)
    if project != "demo3d":
        return p

    e = sub.add_parser("editor")
    e.add_argument("action", choices=["start", "stop", "status"])
    e.set_defaults(func=cmd_editor)

    sw = sub.add_parser("sweep")
    sw.add_argument("spec", nargs="+", help="sweep specs (JSON), one player process each")
    sw.add_argument("--out", help="the sweep folder (default demo3d/Logs/bench/<time>)")
    sw.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    sw.add_argument("--build", action="store_true", help="rebuild the player first")
    sw.add_argument("--player", help="a player exe to use instead of Builds/<backend>")
    sw.add_argument("--screen", help="launch at WxH instead of the spec's size (refusal test)")
    sw.add_argument("--workers", type=int, help="launch with N job workers instead of the spec's")
    sw.add_argument("--no-lock", action="store_true", help="let the player take the lock itself")
    sw.add_argument("--no-foreground", action="store_true",
                    help="don't require the foreground (record focus loss, not noise)")
    sw.set_defaults(func=cmd_sweep)

    rp = sub.add_parser("report")
    rp.add_argument("folder")
    rp.set_defaults(func=cmd_report)

    px = sub.add_parser("pix")
    px.add_argument("spec")
    px.add_argument("--point", required=True, help="n=..,viewWidthM=..,load=..,aa=..")
    px.add_argument("--out", required=True)
    px.add_argument("--frames", type=int, default=3)
    px.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    px.add_argument("--player")
    px.set_defaults(func=cmd_pix)

    mx = sub.add_parser("mix", help="the video capture's offline mix: the song plus the timeline's sounds")
    mx.add_argument("args", nargs=argparse.REMAINDER)
    mx.set_defaults(func=cmd_mix)

    fl = sub.add_parser("flip", help="FLIP scores of A/B capture pairs (or calibrate the thresholds)")
    fl.add_argument("args", nargs=argparse.REMAINDER)
    fl.set_defaults(func=cmd_flip)

    lk = sub.add_parser("look", help="the look pass's stills, stats and readability (Mac, windowless)")
    lk.add_argument("slug", help="the set's name: captures/look/SLUG and demo3d/evidence/look/SLUG")
    lk.add_argument("--baseline", help="a committed set to compare against (contact sheet, deltas)")
    lk.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    lk.add_argument("--build", action="store_true", help="rebuild the player first (after a change)")
    lk.add_argument("--workers", type=int, default=4, help="player processes at a time")
    lk.add_argument("--measure-only", action="store_true", help="re-measure the kept captures")
    lk.add_argument("--song-only", action="store_true")
    lk.add_argument("--beauty-only", action="store_true")
    lk.add_argument("--trial", action="store_true", help="print the tables, write nothing to evidence")
    lk.add_argument("--player-args", default="", help="extra player flags, e.g. '--crowd-fill 1.5'")
    lk.set_defaults(func=cmd_look)

    ls = sub.add_parser("look-sheet", help="one contact sheet across committed look sets")
    ls.add_argument("out", help="the JPEG, under demo3d/evidence/look/ (e.g. ../styles/all/song.jpg)")
    ls.add_argument("sets", nargs="+", help="look set slugs, one column each")
    ls.add_argument("--stills", required=True, help="comma-separated still names, one row each")
    ls.add_argument("--scale", type=int, default=4, help="each still at 1/scale of the 960 px JPEGs")
    ls.add_argument("--labels", help="comma-separated column labels (default: each set's folder name)")
    ls.set_defaults(func=cmd_look_sheet)
    return p


def main(argv: list[str] | None = None, project: str = "demo3d") -> None:
    if shutil.which("git") is None:
        sys.exit(f"{project}: git is not on PATH")
    argv = sys.argv[1:] if argv is None else argv
    # These parse their own options (argparse's REMAINDER won't take a leading --option).
    if project == "demo3d" and argv and argv[0] in ("mix", "flip"):
        from . import flip, mix

        sys.exit((mix if argv[0] == "mix" else flip).main(argv[1:]))
    args = build_parser(project).parse_args(argv)
    sys.exit(args.func(args))


def main2d() -> None:
    """`demo2d <command>`: the same tools on the 2D demo's project."""
    main(project="demo2d")


def main_stylelab() -> None:
    """`stylelab <command>`: the same tools on the shader style prototype's project."""
    main(project="stylelab")
