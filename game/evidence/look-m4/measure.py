"""The look set's numbers (ps-msz.8.3), with the look tools' measures (demo3d_tools.look).

  uv run --project demo3d/tools python game/evidence/look-m4/measure.py stats DIR|JPG...
      one row per still-*.jpg in each DIR (960 px evidence stills), or per JPG given: still_stats, the
      median hue of the coloured pixels (C*ab > 10) and, from DIR/readability.json when there is one,
      the horde's dL*.
  uv run --project demo3d/tools python game/evidence/look-m4/measure.py light BEFORE AFTER [...]
      a strike's new light, from consecutive frames (BEFORE its impact, AFTER it): the pixels whose L*
      rose by more than RISE, their count and L* p50, p98 and max, and AFTER's clipped share.
  uv run --project demo3d/tools python game/evidence/look-m4/measure.py pairs OUT BEFORE AFTER [...]
      a before/after sheet: each pair side by side (480 px each), labelled, saved with the ps-render tag.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from demo3d_tools.filmstrip import save_tagged
from demo3d_tools.look import chroma, hue, lstar, still_stats

RISE = 10.0


def rgb(path: Path) -> np.ndarray:
    with Image.open(path) as im:
        return np.asarray(im.convert("RGB"))


def stats(dirs: list[str]) -> None:
    print("| still | L* p50 | L* p98 | contrast | chroma | near-black | clipped | clipped blob | hue | dL* |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for d in map(Path, dirs):
        read = {}
        if (d / "readability.json").is_file():
            read = json.loads((d / "readability.json").read_text())
        for still in sorted(d.glob("still-*.jpg")) if d.is_dir() else [d]:
            px = rgb(still)
            s = still_stats(px)
            coloured = chroma(px) > 10
            h = float(np.median(hue(px)[coloured])) if coloured.any() else float("nan")
            r = read.get(still.stem, {}) if isinstance(read, dict) else {}
            dl = f"{r['dL']:+.1f}" if "dL" in r else ""
            print(f"| {still.parent.name}/{still.stem} | {s['p50']:.1f} | {s['p98']:.1f} | {s['contrast']:.1f} | "
                  f"{s['chroma']:.1f} | {100 * s['nearBlack']:.0f}% | {100 * s['clipped']:.2f}% | "
                  f"{100 * s['clippedBlob']:.2f}% | {h:.0f}° | {dl} |")


def light(pairs: list[str]) -> None:
    print("| frames | new-light px | L* p50 | L* p98 | L* max | clipped |")
    print("|---|---|---|---|---|---|")
    for before, after in zip(pairs[::2], pairs[1::2]):
        a, b = rgb(Path(before)), rgb(Path(after))
        la, lb = lstar(a), lstar(b)
        new = lb - la > RISE
        clipped = float((b >= 250).all(axis=-1).mean())
        if not new.any():
            print(f"| {Path(after).name} | 0 | | | | {100 * clipped:.2f}% |")
            continue
        p50, p98 = np.percentile(lb[new], [50, 98])
        print(f"| {Path(before).name} → {Path(after).name} | {int(new.sum())} | {p50:.1f} | {p98:.1f} | "
              f"{lb[new].max():.1f} | {100 * clipped:.2f}% |")


def pairs(args: list[str]) -> None:
    out, files = Path(args[0]), [Path(f) for f in args[1:]]
    w, h = 480, 270
    sheet = Image.new("RGB", (2 * w, h * (len(files) // 2)))
    for i, f in enumerate(files):
        with Image.open(f) as im:
            tile = im.convert("RGB").resize((w, h), Image.LANCZOS)
        ImageDraw.Draw(tile).text((6, 4), f"{'after' if i % 2 else 'before'} {f.parent.name}/{f.stem}", fill=(255, 255, 255))
        sheet.paste(tile, ((i % 2) * w, (i // 2) * h))
    save_tagged(sheet, out, f"look-m4 {out.stem}")


if __name__ == "__main__":
    {"stats": stats, "light": light, "pairs": pairs}[sys.argv[1]](sys.argv[2:])
