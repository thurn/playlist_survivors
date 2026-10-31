"""ps-hgs.4's evidence: the trailer capture's 2x render and its mix (trailer.md, Capture), all windowless.

From the worktree's root, after `demo3d build` and the runs the README lists:

    uv run --project demo3d/tools python demo3d/evidence/trailer/capture/capture.py crops X,Y,W,H 1X.png 2X.png
    uv run --project demo3d/tools python demo3d/evidence/trailer/capture/capture.py mix SOUNDS.json

- `crops`: the region X,Y,W,H (1080p pixels from the top left) of a 1x and a 2x still, enlarged 3x side by side,
  as supersample.jpg.
- `mix`: the stand-in's mix, re-rendered from its cue list as `demo3d trailer` mixed it, as mix.jpg: the whole
  trailer's waveform with its cuts and the end fade marked, then a row per cut, 40 ms before it to 100 ms after,
  of the sounds ringing into it (orange) over the mix (grey).
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools import mix, paths  # noqa: E402
from demo3d_tools.filmstrip import save_tagged  # noqa: E402

W = 1600
GREY, ORANGE, RED, BLUE = (150, 150, 150), (255, 150, 40), (220, 60, 60), (80, 140, 255)


def crops(region: str, one: str, two: str) -> None:
    x, y, w, h = (int(v) for v in region.split(","))
    tiles = [Image.open(p).convert("RGB").crop((x, y, x + w, y + h)).resize((w * 3, h * 3), Image.NEAREST)
             for p in (one, two)]
    sheet = Image.new("RGB", (w * 6 + 8, h * 3 + 24), (0, 0, 0))
    draw = ImageDraw.Draw(sheet)
    for i, (tile, label) in enumerate(zip(tiles, ("1x: drawn 1920x1080", "2x: drawn 3840x2160, kept at 1920x1080"))):
        sheet.paste(tile, (i * (w * 3 + 8), 24))
        draw.text((i * (w * 3 + 8) + 4, 6), label, fill=(255, 255, 255))
    save_tagged(sheet, HERE / "supersample.jpg", f"capture 1x vs 2x crop {region}")


def wave(draw: ImageDraw.ImageDraw, x: np.ndarray, box: tuple[int, int, int, int], color, scale: float) -> None:
    """A waveform's per-column peak in box (left, top, width, height), +-scale full height."""
    left, top, width, height = box
    mono = np.abs(x).max(axis=1)
    cols = np.array_split(mono, width)
    mid = top + height / 2
    for i, c in enumerate(cols):
        v = float(c.max(initial=0.0)) / scale * height / 2
        draw.line([(left + i, mid - v), (left + i, mid + v)], fill=color)


def mix_sheet(sounds_path: str) -> None:
    dump = json.loads(Path(sounds_path).read_text(encoding="utf-8"))
    sounds, start, base = dump["sounds"], dump["start"], dump["trailerStart"]
    files = mix.find_clips({e["clip"] for e in sounds if e["clip"]}, ROOT / "demo3d" / "Assets" / "Vendor")
    clips = {name: mix.decode(path) for name, path in files.items()}
    song = mix.decode(Path(paths.load_roots()["song"]), start, dump["end"] - start + 1 / 60)
    kw = {"cut_fade_s": dump["cutFadeS"], "fade_out": dump["fadeOut"]}
    out = mix.render(sounds, song, start, clips, **kw)
    cuts = sorted({e["fadeAt"] for e in sounds if "fadeAt" in e})
    row_h, top_h = 70, 160
    img = Image.new("RGB", (W, top_h + 20 + len(cuts) * (row_h + 4)), (16, 16, 16))
    draw = ImageDraw.Draw(img)
    wave(draw, out, (0, 0, W, top_h), GREY, 1.0)
    secs = len(out) / mix.RATE
    for c in cuts:
        draw.line([(W * (c - start) / secs, 0), (W * (c - start) / secs, top_h)], fill=ORANGE)
    at, fade = dump["fadeOut"]["at"], dump["fadeOut"]["seconds"]
    for t, color in ((at, RED), (at + fade, BLUE)):
        draw.line([(W * (t - start) / secs, 0), (W * (t - start) / secs, top_h)], fill=color)
    draw.text((4, 4), f"the mix, trailer 0-{secs:.1f} s: cuts (orange), the cut to black (red), silence (blue)",
              fill=(255, 255, 255))
    y = top_h + 20
    for c in cuts:
        a, b = round((c - 0.04 - start) * mix.RATE), round((c + 0.1 - start) * mix.RATE)
        ringing = mix.render([e for e in sounds if e.get("fadeAt") == c], np.zeros_like(song), start, clips,
                             limit=False, **kw)
        wave(draw, out[a:b], (0, y, W, row_h), GREY, 1.0)
        wave(draw, ringing[a:b], (0, y, W, row_h), ORANGE, 1.0)
        for t, color in ((c, ORANGE), (c + dump["cutFadeS"], BLUE)):
            px = W * (t - c + 0.04) / 0.14
            draw.line([(px, y), (px, y + row_h)], fill=color)
        draw.text((4, y + 2), f"cut at trailer {c - base:.3f} s, -40..+100 ms: ringing in orange, gone by +60 ms (blue)",
                  fill=(255, 255, 255))
        y += row_h + 4
    save_tagged(img, HERE / "mix.jpg", "capture stand-in mix waveform")


if __name__ == "__main__":
    if sys.argv[1] == "crops":
        crops(*sys.argv[2:5])
    elif sys.argv[1] == "mix":
        mix_sheet(sys.argv[2])
    else:
        sys.exit(__doc__)
