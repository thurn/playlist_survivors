"""ps-hgs.3's evidence: the trailer's look (trailer.md, Look), before and after, from windowless stills.

From the worktree's root, after `demo3d build` and the captures the README lists:

    uv run --project demo3d/tools python demo3d/evidence/trailer/look/look.py jpegs DIR PREFIX
    uv run --project demo3d/tools python demo3d/evidence/trailer/look/look.py sheet NAME TIMES PREFIX...
    uv run --project demo3d/tools python demo3d/evidence/trailer/look/look.py crops NAME X,Y,W,H TIME DIR...
    uv run --project demo3d/tools python demo3d/evidence/trailer/look/look.py stats DIR...

- `jpegs`: every still-*.png in DIR as a tagged 960 px JPEG in this folder, PREFIX-<trailer s>.jpg.
- `sheet`: one tagged contact sheet NAME.jpg, a row per trailer time in TIMES (comma-separated, as in the file
  names) and a column per PREFIX (before, after...), each cell labelled.
- `crops`: the full-size region X,Y,W,H (pixels from the top left) of each DIR's still at TIME, enlarged 3x side
  by side, as NAME.jpg (the supersampled edges).
- `stats`: each still-*.png's CIE L* p2/p50/p98 (the look harness's), one row per still, per DIR.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools.look import JPEG_H, JPEG_W, load_rgb, lstar, small_jpeg  # noqa: E402

HERE = Path(__file__).resolve().parent


def jpegs(a: argparse.Namespace) -> int:
    for p in sorted(Path(a.dir).glob("still-*.png")):
        dst = HERE / f"{a.prefix}-{p.stem[len('still-'):]}.jpg"
        small_jpeg(p, dst, f"trailer {dst.stem}")
        print(dst.relative_to(ROOT))
    return 0


def sheet(a: argparse.Namespace) -> int:
    times = a.times.split(",")
    w, h, pad = JPEG_W // 2, JPEG_H // 2, 16
    out = Image.new("RGB", (len(a.prefixes) * w, len(times) * (h + pad)), (16, 16, 16))
    draw = ImageDraw.Draw(out)
    for r, t in enumerate(times):
        for c, prefix in enumerate(a.prefixes):
            f = HERE / f"{prefix}-{t}.jpg"
            x, y = c * w, r * (h + pad)
            draw.text((x + 4, y + 2), f.stem, fill=(220, 220, 220))
            if f.exists():
                with Image.open(f) as im:
                    out.paste(im.convert("RGB").resize((w, h), Image.LANCZOS), (x, y + pad))
    dst = HERE / f"{a.name}.jpg"
    out.save(dst, "JPEG", quality=85, comment=b"ps-render trailer look contact sheet")
    print(dst.relative_to(ROOT))
    return 0


def crops(a: argparse.Namespace) -> int:
    x, y, w, h = (int(v) for v in a.region.split(","))
    k, pad = 3, 16
    out = Image.new("RGB", (len(a.dirs) * w * k, h * k + pad), (16, 16, 16))
    draw = ImageDraw.Draw(out)
    for i, d in enumerate(a.dirs):
        src = next(Path(d).glob(f"still-{a.time}.png"))
        with Image.open(src) as im:
            out.paste(im.convert("RGB").crop((x, y, x + w, y + h)).resize((w * k, h * k), Image.NEAREST), (i * w * k, pad))
        draw.text((i * w * k + 4, 2), Path(d).name, fill=(220, 220, 220))
    dst = HERE / f"{a.name}.jpg"
    out.save(dst, "JPEG", quality=92, comment=b"ps-render trailer look crops")
    print(dst.relative_to(ROOT))
    return 0


def stats(a: argparse.Namespace) -> int:
    for d in a.dirs:
        print(f"## {Path(d).name}")
        print("| still | L* p2 | p50 | p98 |")
        print("|---|---:|---:|---:|")
        for p in sorted(Path(d).glob("still-*.png")):
            v = lstar(load_rgb(p)).ravel()
            p2, p50, p98 = np.percentile(v, [2, 50, 98])
            print(f"| {p.stem[len('still-'):]} | {p2:.2f} | {p50:.2f} | {p98:.2f} |")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    j = sub.add_parser("jpegs")
    j.add_argument("dir")
    j.add_argument("prefix")
    s = sub.add_parser("sheet")
    s.add_argument("name")
    s.add_argument("times")
    s.add_argument("prefixes", nargs="+")
    c = sub.add_parser("crops")
    c.add_argument("name")
    c.add_argument("region")
    c.add_argument("time")
    c.add_argument("dirs", nargs="+")
    t = sub.add_parser("stats")
    t.add_argument("dirs", nargs="+")
    a = ap.parse_args()
    return {"jpegs": jpegs, "sheet": sheet, "crops": crops, "stats": stats}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
