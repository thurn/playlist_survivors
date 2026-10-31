"""The game's own commands (`game look`, `game content`), beside those it shares with the demos.

`game look SLUG [--at T1,T2,...]`: windowless stills of the game's player on the Mac. It runs the
player in batch mode and fixed time (docs/tech-demo-3d/mac.md: a batch-mode player draws a camera
by hand only for the frames that keep a still), building it first when there is none, or with
--build after a change. The full 1920×1080 stills and the player's log stay in the gitignored
captures/game/look/SLUG/; 960 px JPEGs with the ps-render tag the vendor guard accepts go to
game/evidence/look/SLUG/, or game/evidence/DIR/ with `--evidence DIR` (a bead's own set). The
arena alone: `--player-args "--arena --arena-tour 2"` (ArenaStage). `--frames N` takes N
consecutive fixed-time frames from the first `--at` time instead, and `--strip COLUMNS` writes
them as one labelled filmstrip, SLUG.jpg, in place of separate stills (`--crop X,Y,W,H` in 1080p
pixels for a close look). `--readability` also draws
each still's ID render (the player's `--capture-mask`: the crowd alone, flat white) and measures
the horde against the ground around it, the look tools' readability (look.py: |dL*| >= 17
passes), into readability.json beside the stills; it exits 1 if a still fails.

`game content [--check]`: the one-shot conversion of native's run content into game/Content
(content.py).

`game plugin`: the music plugin built with cargo and copied into the project (plugin.py).

`game spike NAME --file F`: the sync spike's headless File run, measured from Unity's own mix
(spike.py).
"""

from __future__ import annotations

import argparse
import json
import shlex
import shutil
import subprocess
import sys
import time
from pathlib import Path

from PIL import Image

from . import look, paths, setup

APP_EXE = "PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
EVIDENCE_W = 960


def add_parsers(sub: argparse._SubParsersAction) -> None:
    lk = sub.add_parser("look", help="windowless stills of the player (Mac), into game/evidence/look/SLUG")
    lk.add_argument("slug", help="the set's name: captures/game/look/SLUG and game/evidence/look/SLUG")
    lk.add_argument("--at", default="1", help="comma-separated still times in seconds (default 1)")
    lk.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    lk.add_argument("--build", action="store_true", help="rebuild the player first (after a change)")
    lk.add_argument("--player-args", default="", help="extra player flags")
    lk.add_argument("--evidence", help="write the stills to game/evidence/DIR instead of look/SLUG")
    lk.add_argument("--frames", type=int, default=0, help="N consecutive fixed-time frames (1/60 s apart) from the first --at")
    lk.add_argument("--strip", type=int, default=0, metavar="COLUMNS",
                    help="one labelled filmstrip of the stills, COLUMNS wide, in place of separate stills")
    lk.add_argument("--crop", help="the strip's tiles cropped to X,Y,W,H (1080p pixels from the top left)")
    lk.add_argument("--readability", action="store_true",
                    help="measure the horde's readability on each still from its ID render (exits 1 on a failure)")
    lk.set_defaults(func=cmd_look)

    pl = sub.add_parser("plugin", help="build the music plugin and copy it into the project (setup and build run it)")
    pl.set_defaults(func=_cmd_plugin)

    from . import content, spike

    content.add_parser(sub)
    spike.add_parser(sub)


def _cmd_plugin(a: argparse.Namespace) -> int:
    from . import plugin

    return plugin.cmd_plugin(a)


def cmd_look(a: argparse.Namespace) -> int:
    if sys.platform != "darwin":
        print("look: the game's windowless stills run on the Mac", file=sys.stderr)
        return 2
    wt = paths.worktree(name="game")
    from . import filmstrip

    times = sorted(float(t) for t in a.at.split(","))
    if a.frames > 0:
        times = filmstrip.frame_times(times[0], a.frames, 1, 0.0)
    at = ",".join(f"{t:.6f}" for t in times)
    exe = setup.player_dir(wt, "mac", a.backend) / APP_EXE
    if a.build or not exe.exists():
        from . import cli

        code = cli.cmd_build(argparse.Namespace(
            project="game", platform="mac", backend=a.backend, out=None, development=False,
            scene=None, cold=False, force=False))
        if code != 0:
            return code
    captures = wt.root / "captures" / "game" / "look" / a.slug
    shutil.rmtree(captures, ignore_errors=True)
    captures.mkdir(parents=True)
    cmd = [str(exe), "-batchmode", "-force-metal", "-logFile", str(captures / "player.log"),
           "--fixed-time", "--capture-stills", str(captures), "--stills-at", at,
           "--seconds", f"{times[-1] + 5:g}", *shlex.split(a.player_args),
           *(["--capture-mask"] if a.readability else [])]
    started = time.monotonic()
    code = subprocess.run(cmd, check=False, cwd=wt.root, timeout=600, stdout=subprocess.DEVNULL).returncode
    stills = sorted(captures.glob("still-*.jpg"))
    if code != 0 or len(stills) != len(times):
        print(f"look: the player exited {code} with {len(stills)} of {len(times)} stills "
              f"(see {captures / 'player.log'})", file=sys.stderr)
        return 1
    evidence = wt.project / "evidence" / (a.evidence or f"look/{a.slug}")
    evidence.mkdir(parents=True, exist_ok=True)
    if a.strip > 0:
        sheet = filmstrip.strip(stills, times, filmstrip.parse_crop(a.crop), a.strip, filmstrip.TILE_W)
        filmstrip.save_tagged(sheet, evidence / f"{a.slug}.jpg", f"game look {a.slug}")
        print(f"look: a {len(stills)}-frame strip in {time.monotonic() - started:.1f} s -> "
              f"{(evidence / f'{a.slug}.jpg').relative_to(wt.root)} (full size in {captures.relative_to(wt.root)})")
        return 0
    for still in stills:
        with Image.open(still) as im:
            small = im.convert("RGB").resize((EVIDENCE_W, round(im.height * EVIDENCE_W / im.width)), Image.LANCZOS)
        small.save(evidence / still.name, "JPEG", quality=88,
                   comment=f"ps-render game look {a.slug} {still.stem}".encode())
    print(f"look: {len(stills)} stills in {time.monotonic() - started:.1f} s -> "
          f"{evidence.relative_to(wt.root)} (full size in {captures.relative_to(wt.root)})")
    return readability(stills, evidence) if a.readability else 0


def readability(stills: list[Path], evidence: Path) -> int:
    """The look tools' readability of each still against its ID render (still-<t>-mask.png), into
    readability.json; 1 if any still fails or lacks its mask."""
    results = {}
    for still in stills:
        mask_path = still.with_name(f"{still.stem}-mask.png")
        if not mask_path.exists():
            print(f"look: {still.name} has no ID render", file=sys.stderr)
            return 1
        measured = look.load_rgb(still)
        mask = look.load_rgb(mask_path).max(axis=2) > 127
        r = look.readability(measured, mask, measured)
        results[still.stem] = {k: (round(v, 3) if isinstance(v, float) else v) for k, v in r.items() if k != "dLbg"}
        verdict = "pass" if r.get("pass") else "FAIL"
        print(f"look: {still.stem} readability dL* {r.get('dL', float('nan')):+.1f} "
              f"(monsters {r.get('lMonster', float('nan')):.1f}, ground ring {r.get('lRing', float('nan')):.1f}, "
              f"{r['monsterPx']} px) {verdict}")
    (evidence / "readability.json").write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")
    return 0 if all(r.get("pass") for r in results.values()) else 1
