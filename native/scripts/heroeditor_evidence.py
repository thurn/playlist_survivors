"""Paired stills and filmstrips of native's two looks, LPC and `--art heroeditor` (epic ps-4c8).

    uv run --project demo3d/tools python native/scripts/heroeditor_evidence.py pair OUT.png --at 40
    uv run --project demo3d/tools python native/scripts/heroeditor_evidence.py strip OUT.png --at 30

Both drive the release app's `--still` (built first: `cargo build --release -p app` in native/),
headless, on a track's committed stream with a stride on each beat and the first card picked at
each level-up, at 1920 x 1080:

- `pair`: the same moment in both looks, LPC on the left.
- `strip`: `--bars` bars from the first beat at or after `--at`, `--per-beat` frames a beat, one
  row a beat, cropped around the screen's centre (the camera keeps the hero near it) or
  `--shift`ed and `--lift`ed from it, the LPC
  look above and HeroEditor below for each beat. Each still re-simulates the run from the start,
  so every frame is the same run at another time.

Images (JPEG or PNG, by OUT's suffix) are tagged ps-render for the vendor guard: the HeroEditor look draws HeroEditor-derived
frames, fine in evidence as small tagged renders (docs/archive/tech-demo-3d/vendor-assets.md).
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from PIL import Image, ImageDraw
from PIL.PngImagePlugin import PngInfo

NATIVE = Path(__file__).resolve().parent.parent
APP = NATIVE / "target" / "release" / "playlist-survivors"
LOOKS = ("lpc", "heroeditor")
W, H = 1920, 1080


def still(path: Path, track: str, at: float, art: str) -> Path:
    cmd = [str(APP), "--still", str(path), "--still-track", track, "--still-at", f"{at:.4f}",
           "--pick-first", "--headless", "--art", art]
    r = subprocess.run(cmd, capture_output=True, text=True, check=False)
    if r.returncode != 0:
        sys.exit(f"still failed ({' '.join(cmd)}):\n{r.stderr[-2000:]}")
    return path


def save(img: Image.Image, dst: Path, label: str) -> None:
    """A JPEG or PNG (by `dst`'s suffix) with the ps-render tag the vendor guard accepts."""
    tag = f"ps-render native heroeditor evidence {label}"
    dst.parent.mkdir(parents=True, exist_ok=True)
    if dst.suffix.lower() in (".jpg", ".jpeg"):
        img.convert("RGB").save(dst, "JPEG", quality=88, comment=tag.encode())
        return
    info = PngInfo()
    info.add_text("ps-render", tag.removeprefix("ps-render "))
    img.convert("RGB").save(dst, "PNG", pnginfo=info, optimize=True)


def labelled(img: Image.Image, text: str) -> Image.Image:
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 8 + 7 * len(text), 18), fill=(0, 0, 0))
    d.text((4, 3), text, fill=(255, 255, 255))
    return img


def cmd_pair(a: argparse.Namespace, tmp: Path) -> None:
    with ThreadPoolExecutor(2) as pool:
        shots = list(pool.map(lambda art: still(tmp / f"{art}.png", a.track, a.at, art), LOOKS))
    scale = a.width / W
    w, h = round(W * scale), round(H * scale)
    out = Image.new("RGB", (2 * w + 8, h), (0, 0, 0))
    for k, (art, p) in enumerate(zip(LOOKS, shots)):
        img = Image.open(p).convert("RGB").resize((w, h), Image.LANCZOS)
        out.paste(labelled(img, f"{art}  {a.track} {a.at:.1f} s"), (k * (w + 8), 0))
    save(out, Path(a.out), f"pair {a.track} {a.at}")


def beats(track: str) -> list[float]:
    stream = json.loads((NATIVE.parent / "public" / "reactive" / track / "stream.json").read_text())
    log = stream.get("log", stream)
    return [b["t"] for b in log["beats"]]


def cmd_strip(a: argparse.Namespace, tmp: Path) -> None:
    bs = beats(a.track)
    first = next(i for i, t in enumerate(bs) if t >= a.at)
    n = 4 * a.bars
    times = []
    for i in range(first, first + n):
        step = (bs[i + 1] - bs[i]) / a.per_beat
        times += [bs[i] + k * step for k in range(a.per_beat)]
    jobs = [(art, k, t) for art in LOOKS for k, t in enumerate(times)]
    with ThreadPoolExecutor(a.workers) as pool:
        paths = list(pool.map(lambda j: still(tmp / f"{j[0]}-{j[1]:03d}.png", a.track, j[2], j[0]), jobs))
    cw, ch = a.crop
    x0, y0 = (W - cw) // 2 + a.shift, (H - ch) // 2 + a.lift
    box = (x0, y0, x0 + cw, y0 + ch)
    tw, th = round(cw * a.scale), round(ch * a.scale)
    rows = 2 * n
    sheet = Image.new("RGB", (a.per_beat * (tw + 2), rows * (th + 2)), (0, 0, 0))
    for (art, k, t), p in zip(jobs, paths):
        beat, sub = divmod(k, a.per_beat)
        row = 2 * beat + LOOKS.index(art)
        tile = Image.open(p).convert("RGB").crop(box).resize((tw, th), Image.LANCZOS)
        label = f"{art} b{beat + 1}.{sub}" if sub == 0 else f"{t:.2f}"
        sheet.paste(labelled(tile, label), (sub * (tw + 2), row * (th + 2)))
    save(sheet, Path(a.out), f"strip {a.track} {a.at}")


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    pa = sub.add_parser("pair")
    pa.add_argument("out")
    pa.add_argument("--at", type=float, required=True)
    pa.add_argument("--width", type=int, default=960, help="each look's width in the pair")
    st = sub.add_parser("strip")
    st.add_argument("out")
    st.add_argument("--at", type=float, required=True)
    st.add_argument("--bars", type=int, default=2)
    st.add_argument("--per-beat", type=int, default=8)
    st.add_argument("--crop", type=int, nargs=2, default=(480, 360), metavar=("W", "H"))
    st.add_argument("--lift", type=int, default=-20, help="move the crop down by this (px)")
    st.add_argument("--shift", type=int, default=0, help="move the crop right by this (px)")
    st.add_argument("--scale", type=float, default=0.5)
    st.add_argument("--workers", type=int, default=6)
    for s in (pa, st):
        s.add_argument("--track", default="house-128")
    a = p.parse_args()
    if not APP.exists():
        sys.exit(f"{APP} is missing: cargo build --release -p app in native/")
    with tempfile.TemporaryDirectory() as tmp:
        {"pair": cmd_pair, "strip": cmd_strip}[a.cmd](a, Path(tmp))
    print(f"wrote {a.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
