"""`champions stills`: fixed-time stills of the scripted scenes, in one launch of the built player.

The player (`--capture-stills DIR --stills-scenes NAMES`, Champions.Client.StillScenes) plays each scene in fixed time
and keeps, at each of the scene's times, still-SCENE-T.png (the frame as drawn, 1920x1080), id-SCENE-T.png (its
type-ID pass) and view-SCENE-T.json (the camera, the hero's feet, the ID table): what `champions look` measures. The
scenes grow as the game's systems land (plan.md, Look Metrics); `all` plays every scene the player knows.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import look, paths, setup

KEPT = ("still-*", "id-*", "view-*")


def cmd_stills(a: argparse.Namespace) -> int:
    """Plays the scenes into the stills folder (emptied of earlier stills first); 2 without a player."""
    from .cli import headless

    wt = paths.worktree()
    app = setup.player_dir(wt, a.backend) / setup.APP
    if not app.is_dir():
        print(f"stills: no {a.backend} player at {app}; run `just champions-build`", file=sys.stderr)
        return 2
    out = Path(a.out).resolve() if a.out else look.stills_dir(wt)
    out.mkdir(parents=True, exist_ok=True)
    for pattern in KEPT:
        for old in out.glob(pattern):
            old.unlink()
    code = headless(wt, app, ["--fixed-time", "--capture-stills", str(out),
                              "--stills-scenes", ",".join(a.scene or ["all"])], "stills.log")
    kept = sorted(out.glob("still-*"))
    print(f"stills: {len(kept)} stills in {out}" + "".join(f"\n  {p.name}" for p in kept))
    return code if code != 0 else (0 if kept else 1)
