"""The game's own commands (`game look`), beside the commands it shares with the demos.

`game look SLUG [--at T1,T2,...]`: windowless stills of the game's player on the Mac. It runs the
player in batch mode and fixed time (docs/tech-demo-3d/mac.md: a batch-mode player draws a camera
by hand only for the frames that keep a still), building it first when there is none, or with
--build after a change. The full 1920×1080 stills and the player's log stay in the gitignored
captures/game/look/SLUG/; 960 px JPEGs with the ps-render tag the vendor guard accepts go to
game/evidence/look/SLUG/.
"""

from __future__ import annotations

import argparse
import shlex
import shutil
import subprocess
import sys
import time

from PIL import Image

from . import paths, setup

APP_EXE = "PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
EVIDENCE_W = 960


def add_parsers(sub: argparse._SubParsersAction) -> None:
    lk = sub.add_parser("look", help="windowless stills of the player (Mac), into game/evidence/look/SLUG")
    lk.add_argument("slug", help="the set's name: captures/game/look/SLUG and game/evidence/look/SLUG")
    lk.add_argument("--at", default="1", help="comma-separated still times in seconds (default 1)")
    lk.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    lk.add_argument("--build", action="store_true", help="rebuild the player first (after a change)")
    lk.add_argument("--player-args", default="", help="extra player flags")
    lk.set_defaults(func=cmd_look)


def cmd_look(a: argparse.Namespace) -> int:
    if sys.platform != "darwin":
        print("look: the game's windowless stills run on the Mac", file=sys.stderr)
        return 2
    wt = paths.worktree(name="game")
    times = sorted(float(t) for t in a.at.split(","))
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
           "--fixed-time", "--capture-stills", str(captures), "--stills-at", a.at,
           "--seconds", f"{times[-1] + 5:g}", *shlex.split(a.player_args)]
    started = time.monotonic()
    code = subprocess.run(cmd, check=False, cwd=wt.root, timeout=600, stdout=subprocess.DEVNULL).returncode
    stills = sorted(captures.glob("still-*.jpg"))
    if code != 0 or len(stills) != len(times):
        print(f"look: the player exited {code} with {len(stills)} of {len(times)} stills "
              f"(see {captures / 'player.log'})", file=sys.stderr)
        return 1
    evidence = wt.project / "evidence" / "look" / a.slug
    evidence.mkdir(parents=True, exist_ok=True)
    for still in stills:
        with Image.open(still) as im:
            small = im.convert("RGB").resize((EVIDENCE_W, round(im.height * EVIDENCE_W / im.width)), Image.LANCZOS)
        small.save(evidence / still.name, "JPEG", quality=88,
                   comment=f"ps-render game look {a.slug} {still.stem}".encode())
    print(f"look: {len(stills)} stills in {time.monotonic() - started:.1f} s -> "
          f"{evidence.relative_to(wt.root)} (full size in {captures.relative_to(wt.root)})")
    return 0
