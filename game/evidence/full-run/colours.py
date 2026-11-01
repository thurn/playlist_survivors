"""The crowd's gem colours and the clipping in a `game look --readability` set's stills (ps-msz.8.14).

  uv run --project demo3d/tools python game/evidence/full-run/colours.py captures/game/look/SLUG

For each full-size still-<t>.jpg with its ID render (still-<t>-mask.png): the monster pixels colourful enough
to class (C*ab above CHROMA) go to the nearest of cast.json's crystal colours by HSV hue, colours closer than
MERGE degrees taken as one (amber and gold, violet and indigo, blue and ice), and a colour counts when it holds
at least MIN_SHARE of them. It is a colour estimate, as the look tools' readability by type is: a zone's grade
or a hit's flash can move a crystal into its neighbour's hue. Also the frame's clipped share (every channel at
250 or more, look.still_stats).
"""
import json
import sys
from pathlib import Path

import numpy as np

from demo3d_tools.look import chroma, hue, load_rgb, still_stats

CHROMA = 20.0  # look.TYPE_CHROMA
MERGE = 15.0
MIN_SHARE = 0.03
CAST = Path(__file__).resolve().parents[2] / "Content" / "cast.json"


def hsv_hue(hex_colour: str) -> float:
    rgb = np.array([[[int(hex_colour[i:i + 2], 16) for i in (1, 3, 5)]]], dtype=np.uint8)
    return float(hue(rgb)[0, 0])


def colour_groups() -> list[tuple[str, float]]:
    """cast.json's crystal colours as (label, hue), merged within MERGE degrees; the label lists the roles' colours."""
    groups: list[tuple[list[str], float]] = []
    for c in sorted({r["crystal"] for r in json.loads(CAST.read_text(encoding="utf-8"))["cast"]}, key=hsv_hue):
        h = hsv_hue(c)
        if groups and abs((h - groups[-1][1] + 180) % 360 - 180) < MERGE:
            groups[-1][0].append(c)
        else:
            groups.append(([c], h))
    if len(groups) > 1 and abs((groups[0][1] - groups[-1][1] + 180) % 360 - 180) < MERGE:
        groups[0] = (groups[-1][0] + groups[0][0], groups[0][1])
        groups.pop()
    return [("/".join(cs), h) for cs, h in groups]


def main(folder: Path) -> None:
    groups = colour_groups()
    targets = np.array([h for _, h in groups])
    print("| still | clipped | colours (share of classed monster px) | count |")
    print("|---|---|---|---|")
    for still in sorted(folder.glob("still-*.jpg")):
        if still.stem.endswith("-mask"):
            continue
        px = load_rgb(still)
        mask_path = still.with_name(f"{still.stem}-mask.png")
        mask = load_rgb(mask_path).max(axis=2) > 127 if mask_path.exists() else np.zeros(px.shape[:2], bool)
        classed = mask & (chroma(px) > CHROMA)
        gaps = np.abs((hue(px)[classed][:, None] - targets[None, :] + 180) % 360 - 180)
        counts = np.bincount(gaps.argmin(axis=1), minlength=len(groups)) if classed.any() else np.zeros(len(groups))
        shares = counts / max(1, counts.sum())
        shown = [(groups[i][0], shares[i]) for i in np.argsort(-shares) if shares[i] >= MIN_SHARE]
        listed = ", ".join(f"{label} {100 * s:.0f}%" for label, s in shown)
        print(f"| {still.stem} | {100 * still_stats(px)['clipped']:.2f}% | {listed} | {len(shown)} |")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
