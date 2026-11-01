"""The neon look's reference board (ps-wnj.1): v2 crystal-cut frames measured against the style guide.

    uv run --project demo3d/tools python demo3d/evidence/look/style/board.py [captures/trailer-crystal-v2.silent.mp4]

Pulls BOARD frames from the v2 capture with ffmpeg, measures each with the guide's frame-level rules
(docs/design/neon-look-style.md, Rules and Thresholds), writes board.jpg (ps-render tagged) and
prints the rules table that README.md quotes. Mask-based rules (floor, monster form, swarm
separation, hero brightest) need the players' ID render and view json; the look metrics bead (ps-wnj.2) measures those. On a finished
video the frame's L* p25 (the darkest quarter must be void) stands in for the floor rule.
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

from demo3d_tools.filmstrip import save_tagged
from demo3d_tools.look import chroma, hue, lstar

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
W = 960  # the guide measures at 960 px wide: its ring widths are in these pixels

BOARD = [  # (time s, what the frame shows)
    (0.5, "opening: the darkest shot, but a lit pool and a textured floor"),
    (4.0, "formations: the loved white-gold crystal, in a soup of hues"),
    (20.0, "the drop: same-type clumps fuse into flat colour fields (the blob)"),
    (36.0, "lava: an amber-lit floor; the world, not the emitters, is bright"),
    (44.0, "dragon ring: every type's hue at once; no key"),
    (54.0, "breath: the cone, the dragon and the floor share one orange"),
]

# The guide's frame-level thresholds (Rules and Thresholds); every preset's ceiling where they differ.
T = {"key60": 0.55, "families": 4, "black": 0.30, "bright_max": 0.32, "clipped": 0.02, "white": 0.02,
     "halo": 15.0, "p25": 6.0}


def dilate(mask: np.ndarray, r: int) -> np.ndarray:
    return np.asarray(Image.fromarray(mask.astype(np.uint8) * 255).filter(ImageFilter.MaxFilter(2 * r + 1))) > 0


def hue_windows(h: np.ndarray) -> tuple[float, int]:
    """The key (the largest share of colourful pixels in one 60-degree window) and the families
    (greedy 60-degree windows each holding at least 8% of them)."""
    hist = np.histogram(h, bins=36, range=(0, 360))[0].astype(float)
    hist /= max(hist.sum(), 1.0)
    win = lambda x: np.array([sum(x[(i + k) % 36] for k in range(6)) for i in range(36)])
    key = float(win(hist).max())
    rest, fams = hist.copy(), 0
    while True:
        w = win(rest)
        i = int(w.argmax())
        if w[i] < 0.08:
            return key, fams
        fams += 1
        for k in range(6):
            rest[(i + k) % 36] = 0


def measure(rgb: np.ndarray) -> dict[str, float]:
    L, C, H = lstar(rgb), chroma(rgb), hue(rgb)
    colourful = (C >= 30) & (L >= 20)
    key, fams = hue_windows(H[colourful])
    emit = L >= 70
    ring = dilate(emit, 10) & ~dilate(emit, 3) & (L < 70)
    p25 = float(np.percentile(L, 25))
    return {
        "key60": key, "families": fams,
        "black": float((L < 10).mean()), "bright": float((L >= 50).mean()),
        "clipped": float((rgb.min(axis=-1) >= 250).mean()), "white": float(((C < 15) & (L >= 85)).mean()),
        "halo": float(np.median(L[ring]) - p25) if ring.any() else 0.0,
        "p25": p25,
    }


def verdicts(m: dict[str, float]) -> dict[str, bool]:
    return {
        "key60": m["key60"] >= T["key60"], "families": m["families"] <= T["families"],
        "black": m["black"] >= T["black"], "bright": m["bright"] <= T["bright_max"],
        "clipped": m["clipped"] <= T["clipped"], "white": m["white"] <= T["white"],
        "halo": m["halo"] >= T["halo"], "p25": m["p25"] <= T["p25"],
    }


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
    cells = [("key", f"{m['key60']:.2f}", ok["key60"]), ("fam", f"{m['families']}", ok["families"]),
             ("black", f"{m['black']:.0%}", ok["black"]), ("bright", f"{m['bright']:.0%}", ok["bright"]),
             ("clip", f"{m['clipped']:.1%}", ok["clipped"]), ("white", f"{m['white']:.1%}", ok["white"]),
             ("halo", f"{m['halo']:.0f}", ok["halo"]), ("p25", f"{m['p25']:.1f}", ok["p25"])]
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
            m = measure(rgb)
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
    lines = [
        "| t (s) | frame | key60 (>= 0.55) | families (<= 4) | near-black (>= 30%) | bright (<= 32%) "
        "| clipped (<= 2%) | white (<= 2%) | halo dL* (>= 15) | L* p25 (<= 6) |",
        "|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for t, note, m, ok in rows:
        lines.append(
            f"| {t:.1f} | {note} | {m['key60']:.2f}{mark(ok['key60'])} | {m['families']}{mark(ok['families'])} "
            f"| {m['black']:.0%}{mark(ok['black'])} | {m['bright']:.0%}{mark(ok['bright'])} "
            f"| {m['clipped']:.1%}{mark(ok['clipped'])} | {m['white']:.1%}{mark(ok['white'])} "
            f"| {m['halo']:.1f}{mark(ok['halo'])} | {m['p25']:.1f}{mark(ok['p25'])} |")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
