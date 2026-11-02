"""The neon look's reference board (ps-wnj.1): v2 crystal-cut frames measured against the style guide.

    uv run --project demo3d/tools python demo3d/evidence/look/style/board.py [captures/trailer-crystal-v2.silent.mp4]

Pulls BOARD frames from the v2 capture with ffmpeg, measures each with the guide's frame-level rules
(docs/archive/design/neon-look-style.md, Rules and Thresholds), writes board.jpg (ps-render tagged) and
prints the rules table that README.md quotes. The measures and thresholds are look-metrics' own
(demo3d_tools.look.frame_measures, docs/archive/design/neon-look-thresholds.json). Mask-based rules (floor, monster
form, hero brightest) need the players' ID render and view json, so look-metrics measures those. On a
finished video the frame's L* p25 (the darkest quarter must be void) stands in for the floor rule.
"""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from demo3d_tools.filmstrip import save_tagged
from demo3d_tools.look import NEON_W as W, check_rules, frame_measures, load_rules

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]

BOARD = [  # (time s, what the frame shows)
    (0.5, "opening: the darkest shot, but a lit pool and a textured floor"),
    (4.0, "formations: the loved white-gold crystal, in a soup of hues"),
    (20.0, "the drop: same-type clumps fuse into flat colour fields (the blob)"),
    (36.0, "lava: an amber-lit floor; the world, not the emitters, is bright"),
    (44.0, "dragon ring: every type's hue at once; no key"),
    (54.0, "breath: the cone, the dragon and the floor share one orange"),
]

# The board's cells: (label, metric, format), each judged by the thresholds file's steady rules on that metric,
# every preset's ceiling where they differ (look-metrics with no preset).
CELLS = [("key", "key60", ".2f"), ("fam", "families", "d"), ("black", "black_share", ".0%"),
         ("bright", "bright_share", ".0%"), ("clip", "clipped", ".1%"), ("white", "white", ".1%"),
         ("halo", "halo", ".1f"), ("p25", "frame_p25", ".1f")]
RULES = load_rules(json.loads((ROOT / "docs" / "archive" / "design" / "neon-look-thresholds.json").read_text(encoding="utf-8")), None)


def verdicts(m: dict) -> dict[str, bool]:
    broken = check_rules(m, RULES)
    failing = {r["metric"] for r in RULES if r["key"] in broken}
    return {metric: metric not in failing for _, metric, _ in CELLS}


def limits(metric: str) -> str:
    return ", ".join(f"{'>=' if r['op'] == 'min' else '<='} {r['value']:g}" for r in RULES
                     if r["metric"] == metric and r["on"] == "steady")


def frame(video: Path, t: float, tmp: Path) -> np.ndarray:
    out = tmp / f"f-{t:06.2f}.png"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t}", "-i", str(video), "-frames:v", "1", str(out)], check=True)
    im = Image.open(out).convert("RGB")
    return np.asarray(im.resize((W, round(im.height * W / im.width)), Image.LANCZOS))


def font(size: int) -> ImageFont.ImageFont:
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/System/Library/Fonts/Helvetica.ttc"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def tile(rgb: np.ndarray, t: float, note: str, m: dict[str, float], ok: dict[str, bool]) -> Image.Image:
    im = Image.fromarray(rgb)
    band = 96
    out = Image.new("RGB", (im.width, im.height + band), (16, 16, 20))
    out.paste(im, (0, 0))
    d = ImageDraw.Draw(out)
    d.text((10, im.height + 6), f"v2 t={t:.1f}s  {note}", fill=(235, 235, 240), font=font(19))
    cells = [(label, format(m[metric], fmt) if metric in m else "", ok[metric]) for label, metric, fmt in CELLS]
    x = 10
    for name, val, good in cells:
        d.text((x, im.height + 40), name, fill=(150, 150, 160), font=font(15))
        d.text((x, im.height + 60), val, fill=(120, 230, 140) if good else (255, 90, 90), font=font(22))
        x += 118
    return out


def main() -> int:
    video = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "captures" / "trailer-crystal-v2.silent.mp4"
    rows, tiles = [], []
    with tempfile.TemporaryDirectory() as tmp:
        for t, note in BOARD:
            rgb = frame(video, t, Path(tmp))
            m = frame_measures(rgb)
            ok = verdicts(m)
            tiles.append(tile(rgb, t, note, m, ok))
            rows.append((t, note, m, ok))
    cols = 2
    tw, th = tiles[0].size
    sheet = Image.new("RGB", (tw * cols, th * ((len(tiles) + cols - 1) // cols)), (0, 0, 0))
    for i, im in enumerate(tiles):
        sheet.paste(im, ((i % cols) * tw, (i // cols) * th))
    save_tagged(sheet, HERE / "board.jpg", "neon-look style board v2")
    mark = lambda good: "" if good else " ✗"
    lines = ["| t (s) | frame | " + " | ".join(f"{metric} ({limits(metric)})" for _, metric, _ in CELLS) + " |",
             "|---:|---|" + "---:|" * len(CELLS)]
    for t, note, m, ok in rows:
        lines.append(f"| {t:.1f} | {note} | " + " | ".join(
            (format(m[metric], fmt) if metric in m else "") + mark(ok[metric]) for _, metric, fmt in CELLS) + " |")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
