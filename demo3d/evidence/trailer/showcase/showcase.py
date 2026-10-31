"""ps-hgs.7's evidence: the trailer's shot list, as sheets of windowless stills (trailer.md, Checks and Evidence).

From the worktree's root, after `demo3d build` and the captures the README lists:

    uv run --project demo3d/tools python demo3d/evidence/trailer/showcase/showcase.py sheet NAME DIR
    uv run --project demo3d/tools python demo3d/evidence/trailer/showcase/showcase.py grid NAME DIR...
    uv run --project demo3d/tools python demo3d/evidence/trailer/showcase/showcase.py still NAME PNG

- `sheet`: every still-*.png in DIR as one tagged sheet NAME.jpg, four across, each labelled with its trailer second.
- `grid`: one row per DIR, labelled with the folder's name, each row its stills in time order (the spot-check grid).
- `still`: one still as a tagged 960 px JPEG NAME.jpg (the look harness's small_jpeg).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools.look import small_jpeg  # noqa: E402

HERE = Path(__file__).resolve().parent
TAG = b"ps-render trailer showcase sheet"


def stills(d: str) -> list[Path]:
    return sorted(Path(d).glob("still-*.png"))


def save(img: Image.Image, name: str) -> None:
    dst = HERE / f"{name}.jpg"
    img.save(dst, "JPEG", quality=82, comment=TAG)
    print(dst.relative_to(ROOT))


def sheet(a: argparse.Namespace) -> int:
    files = stills(a.dir)
    cols, w, h, pad = 4, 480, 270, 16
    rows = (len(files) + cols - 1) // cols
    out = Image.new("RGB", (cols * w, rows * (h + pad)), (16, 16, 16))
    draw = ImageDraw.Draw(out)
    for i, f in enumerate(files):
        x, y = (i % cols) * w, (i // cols) * (h + pad)
        draw.text((x + 4, y + 2), f.stem[len("still-"):], fill=(220, 220, 220))
        with Image.open(f) as im:
            out.paste(im.convert("RGB").resize((w, h), Image.LANCZOS), (x, y + pad))
    save(out, a.name)
    return 0


def grid(a: argparse.Namespace) -> int:
    rows = [(Path(d).name, stills(d)) for d in a.dirs]
    cols = max(len(fs) for _, fs in rows)
    label, w, h, pad = 130, 320, 180, 4
    out = Image.new("RGB", (label + cols * w, len(rows) * (h + pad)), (16, 16, 16))
    draw = ImageDraw.Draw(out)
    for r, (name, files) in enumerate(rows):
        y = r * (h + pad)
        draw.text((6, y + h // 2 - 6), name, fill=(220, 220, 220))
        for c, f in enumerate(files):
            with Image.open(f) as im:
                out.paste(im.convert("RGB").resize((w, h), Image.LANCZOS), (label + c * w, y))
            if r == 0:
                draw.text((label + c * w + 4, y + 2), f.stem[len("still-"):], fill=(255, 255, 255))
    save(out, a.name)
    return 0


def still(a: argparse.Namespace) -> int:
    dst = HERE / f"{a.name}.jpg"
    small_jpeg(Path(a.png), dst, f"trailer {a.name}")
    print(dst.relative_to(ROOT))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("sheet")
    s.add_argument("name")
    s.add_argument("dir")
    g = sub.add_parser("grid")
    g.add_argument("name")
    g.add_argument("dirs", nargs="+")
    o = sub.add_parser("still")
    o.add_argument("name")
    o.add_argument("png")
    a = ap.parse_args()
    return {"sheet": sheet, "grid": grid, "still": still}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
