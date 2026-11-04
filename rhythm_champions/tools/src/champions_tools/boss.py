"""`champions boss review ID [--motion]`: a Boss Rush boss's fight rendered headlessly (boss-rush.md, Fights; ps-8z4.3).

The built player plays the still scene `fight-ID` (Champions.Client.Scenes.FightScene): the boss's fight alone in an
empty world through the real run presentation, a hero with six weapons wandering the arena. It keeps a still every five
seconds (`t-05` ...) and the moments the sim decides: the entrance, the first attacks' telegraphs at 60% filled
(`attack-NN-MOVE`), each phase and the death. `--motion` adds `fight-ID-motion`: 30 frames a second from 4 to 12 s,
a clip's frames (`motion-NNNN`). Everything lands in rhythm_champions/evidence/boss-rush/ID/ by default, with a README
listing the stills; a new review keeps the README's hand-written Notes. Nothing here passes or fails a look.

ID is a boss id the sim registers (Champions.Sim.BossModules); the player refuses an unknown one and names the known.
To play a fight by hand, `champions play --fight ID` (`--fight-act N` for its act's telegraph minimums).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from . import paths, setup

KEPT = ("still-*", "id-*", "view-*")
NOTES = "## Notes"


def evidence_dir(wt: paths.Worktree, boss: str) -> Path:
    return wt.project / "evidence" / "boss-rush" / boss


def scenes(boss: str, motion: bool) -> list[str]:
    """The still scenes a review plays."""
    return [f"fight-{boss}"] + ([f"fight-{boss}-motion"] if motion else [])


def notes(readme: Path) -> str:
    """The README's hand-written notes (from its Notes heading on), which a new review keeps."""
    if not readme.exists():
        return ""
    text = readme.read_text(encoding="utf-8")
    at = text.find("\n" + NOTES + "\n")
    return text[at + 1:].rstrip() + "\n" if at >= 0 else ""


def write_readme(boss: str, stills: list[str], kept_notes: str) -> str:
    """The README for a review's stills (their file names, the scene prefix dropped)."""
    moments = [s for s in stills if "-motion-" not in s]
    frames = len(stills) - len(moments)
    lines = [
        f"# Boss Rush fight: {boss}",
        "",
        f"Written by `champions boss review {boss}` (boss-rush.md, Fights). The fight alone in an empty world, a hero",
        "with six weapons wandering the arena; the stills are for judging by eye, nothing here passes or fails.",
        "",
        "## Stills",
        "",
        *[f"- `{s}`" for s in moments],
    ]
    if frames:
        lines += ["", f"Plus {frames} motion frames (`fight-{boss}-motion`, 30 fps from 4 to 12 s)."]
    lines += ["", kept_notes.rstrip() if kept_notes else NOTES + "\n\n(None yet.)"]
    return "\n".join(lines) + "\n"


def cmd_boss(a: argparse.Namespace) -> int:
    """Plays the boss's review scenes into its evidence folder (emptied of earlier stills first); 2 without a player."""
    from .cli import headless

    wt = paths.worktree()
    app = setup.player_dir(wt, a.backend) / setup.APP
    if not app.is_dir():
        print(f"boss review: no {a.backend} player at {app}; run `just champions-build`", file=sys.stderr)
        return 2
    out = Path(a.out).resolve() if a.out else evidence_dir(wt, a.boss)
    out.mkdir(parents=True, exist_ok=True)
    for pattern in KEPT:
        for old in out.glob(pattern):
            old.unlink()
    code = headless(wt, app, ["--fixed-time", "--capture-stills", str(out), "--stills-scenes",
                              ",".join(scenes(a.boss, a.motion)), "--look", a.look], "boss-review.log")
    kept = sorted(p.name for p in out.glob("still-*"))
    readme = out / "README.md"
    readme.write_text(write_readme(a.boss, kept, notes(readme)), encoding="utf-8")
    print(f"boss review: {len(kept)} stills in {out}")
    return code if code != 0 else (0 if kept else 1)
