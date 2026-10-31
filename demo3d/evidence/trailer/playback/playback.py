"""ps-hgs.6's evidence: the --trailer player mode's stills (trailer.md, Playback and cuts).

From the worktree's root, after `demo3d build` and the captures the README lists:

    uv run --project demo3d/tools python demo3d/evidence/trailer/playback/playback.py jpegs DIR PREFIX
    uv run --project demo3d/tools python demo3d/evidence/trailer/playback/playback.py sheet NAME PREFIX...

- `jpegs`: every still-*.png in DIR as a tagged 960 px JPEG in this folder, PREFIX-<trailer s>.jpg (the look
  harness's small_jpeg).
- `sheet`: the JPEGs in this folder starting with each PREFIX, as one tagged contact sheet NAME.jpg, four across,
  each labelled with its file name.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools.look import JPEG_H, JPEG_W, small_jpeg  # noqa: E402

HERE = Path(__file__).resolve().parent


def jpegs(a: argparse.Namespace) -> int:
    for p in sorted(Path(a.dir).glob("still-*.png")):
        dst = HERE / f"{a.prefix}-{p.stem[len('still-'):]}.jpg"
        small_jpeg(p, dst, f"trailer {dst.stem}")
        print(dst.relative_to(ROOT))
    return 0


def sheet(a: argparse.Namespace) -> int:
    files = [f for prefix in a.prefixes for f in sorted(HERE.glob(f"{prefix}-*.jpg"))]
    cols, w, h, pad = 4, JPEG_W // 3, JPEG_H // 3, 16
    rows = (len(files) + cols - 1) // cols
    out = Image.new("RGB", (cols * w, rows * (h + pad)), (16, 16, 16))
    draw = ImageDraw.Draw(out)
    for i, f in enumerate(files):
        x, y = (i % cols) * w, (i // cols) * (h + pad)
        draw.text((x + 4, y + 2), f.stem, fill=(220, 220, 220))
        with Image.open(f) as im:
            out.paste(im.convert("RGB").resize((w, h), Image.LANCZOS), (x, y + pad))
    dst = HERE / f"{a.name}.jpg"
    out.save(dst, "JPEG", quality=82, comment=b"ps-render look harness contact sheet")
    print(dst.relative_to(ROOT))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    j = sub.add_parser("jpegs")
    j.add_argument("dir")
    j.add_argument("prefix")
    s = sub.add_parser("sheet")
    s.add_argument("name")
    s.add_argument("prefixes", nargs="+")
    a = ap.parse_args()
    return {"jpegs": jpegs, "sheet": sheet}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
