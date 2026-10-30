"""`demo3d filmstrip SLUG --at T`: consecutive windowless frames through a song time (epic ps-2iq, Decision 7).

Agents never open a player window, so motion is judged from frame sequences: `--frames` consecutive
fixed-time stills (1/60 s apart, or every `--every`th frame) from song time T, rendered by the
batch-mode player (mac.md), which draws by hand only the frames that keep a still. One player
draws them all by default (96 frames held about 1 GB). A longer run can be split over several
players (`--chunk` frames each, `--workers` at a time), each starting at the same `--start-at` (T
less `--warmup`), so they simulate the same frames (the crystal shatter's physics is deterministic,
ps-2iq.1); but some of a frame's pixels depend on how many frames its player has drawn (by up to
about 20 of 255, in the build before this tool too), so a split run has small seams.

Outputs, all under the gitignored captures/filmstrip/SLUG/:
- the full PNG of every frame (frame-NNN.png, in each chunk's folder too);
- strip.jpg: the frames in a grid, `--columns` wide, each `--crop`ped (1080p pixels from the top
  left) and labelled with its song time;
- SLUG.mp4 at `--fps` (60 / `--every` is real time), cropped the same way.
`--evidence DIR` also copies strip.jpg, with the ps-render tag the vendor guard accepts, to
demo3d/evidence/DIR/SLUG.jpg. `--baseline SLUG2` builds versus.jpg: SLUG2's frames over this run's,
row by row (before above after), for the same times. `--check SLUG2` compares every frame with
SLUG2's (the same times) and exits 1 unless they match pixel for pixel.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from . import paths
from .look import APP

FPS = 60  # the fixed-time frame rate (SongClock)
TILE_W = 480  # a strip tile's width before --scale
MAX_EVIDENCE_W = 2400  # a committed strip stays small (the guard's limit is 4 MiB)


@dataclass
class Chunk:
    index: int
    times: list[float]
    out: Path


def frame_times(at: float, frames: int, every: int, start: float) -> list[float]:
    """Song times of the frames: the fixed-time frames (start + k/60) from the first at or after `at`."""
    first = int(np.ceil((at - start) * FPS - 1e-6))
    return [start + (first + k * every) / FPS for k in range(frames)]


def chunks(times: list[float], size: int, root: Path) -> list[Chunk]:
    return [Chunk(i, times[o:o + size], root / f"chunk-{i:02d}") for i, o in enumerate(range(0, len(times), size))]


def parse_crop(spec: str | None) -> tuple[int, int, int, int] | None:
    if not spec:
        return None
    x, y, w, h = (int(v) for v in spec.split(","))
    return x, y, w, h


def run_chunk(exe: Path, cwd: Path, chunk: Chunk, start: float, extra: list[str]) -> float:
    if chunk.out.exists():
        shutil.rmtree(chunk.out)
    chunk.out.mkdir(parents=True)
    stills = ",".join(f"{t:.6f}" for t in chunk.times)
    cmd = [str(exe), "-batchmode", "-force-metal", "-logFile", str(chunk.out / "player.log"), "--fixed-time",
           "--start-at", f"{start:.6f}", *extra, "--stills-at", stills, "--capture-stills", str(chunk.out),
           "--stills-format", "png"]
    started = time.monotonic()
    code = subprocess.run(cmd, check=False, cwd=cwd, timeout=1800, stdout=subprocess.DEVNULL).returncode
    got = len(list(chunk.out.glob("still-*.png")))
    if code != 0 or got != len(chunk.times):
        raise SystemExit(f"filmstrip: chunk {chunk.index} exited {code} with {got} of {len(chunk.times)} frames "
                         f"(see {chunk.out / 'player.log'})")
    return time.monotonic() - started


def collect(root: Path, chunk_list: list[Chunk]) -> list[Path]:
    """Every chunk's stills, renamed into one sequence frame-000.png, ... in the run's folder."""
    frames: list[Path] = []
    for c in chunk_list:
        for src in sorted(c.out.glob("still-*.png")):
            dst = root / f"frame-{len(frames):03d}.png"
            shutil.copyfile(src, dst)
            frames.append(dst)
    return frames


def tile(path: Path, crop: tuple[int, int, int, int] | None, width: int) -> Image.Image:
    with Image.open(path) as im:
        im = im.convert("RGB")
        if crop:
            x, y, w, h = crop
            im = im.crop((x, y, x + w, y + h))
        return im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)


def strip(frames: list[Path], times: list[float], crop, columns: int, width: int, rows_from: list[list[Path]] | None = None,
          labels: list[str] | None = None) -> Image.Image:
    """The frames in a grid, `columns` wide, each labelled with its song time; with `rows_from`,
    one band per frame row holding each sequence's row in turn (before above after)."""
    seqs = rows_from or [frames]
    first = tile(frames[0], crop, width)
    w, h, pad = first.width, first.height, 16
    rows = (len(frames) + columns - 1) // columns
    band = len(seqs) * (h + pad)
    sheet = Image.new("RGB", (columns * w, rows * band), (16, 16, 16))
    draw = ImageDraw.Draw(sheet)
    for i in range(len(frames)):
        r, c = divmod(i, columns)
        for s, seq in enumerate(seqs):
            y = r * band + s * (h + pad)
            name = f"{labels[s]} " if labels else ""
            draw.text((c * w + 4, y + 2), f"{name}t={times[i]:.3f} (+{i})", fill=(220, 220, 220))
            sheet.paste(tile(seq[i], crop, width), (c * w, y + pad))
    return sheet


def save_tagged(img: Image.Image, dst: Path, label: str) -> None:
    """A JPEG with the ps-render comment the vendor guard accepts, no wider than MAX_EVIDENCE_W."""
    if img.width > MAX_EVIDENCE_W:
        img = img.resize((MAX_EVIDENCE_W, round(img.height * MAX_EVIDENCE_W / img.width)), Image.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    img.save(dst, "JPEG", quality=84, comment=f"ps-render filmstrip {label}".encode())


def video(frames: list[Path], dst: Path, fps: int, crop) -> None:
    """The frames as an H.264 mp4 at `fps` (ffmpeg reads the numbered PNGs), cropped to even sizes."""
    vf = []
    if crop:
        x, y, w, h = crop
        vf.append(f"crop={w - w % 2}:{h - h % 2}:{x}:{y}")
    cmd = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-framerate", str(fps),
           "-i", str(frames[0].parent / "frame-%03d.png"), *(["-vf", ",".join(vf)] if vf else []),
           "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
           "-metadata", "comment=ps-render filmstrip", str(dst)]
    subprocess.run(cmd, check=True)


def compare(a: list[Path], b: list[Path]) -> list[tuple[int, int, int]]:
    """Per frame pair: (index, differing pixels, largest channel difference)."""
    out = []
    for i, (x, y) in enumerate(zip(a, b)):
        with Image.open(x) as ia, Image.open(y) as ib:
            pa, pb = np.asarray(ia.convert("RGB")).astype(np.int16), np.asarray(ib.convert("RGB")).astype(np.int16)
        d = np.abs(pa - pb)
        out.append((i, int((d.max(axis=-1) > 0).sum()), int(d.max())))
    return out


def cmd_filmstrip(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    root = wt.root / "captures" / "filmstrip" / a.slug
    crop = parse_crop(a.crop)
    meta_path = root / "filmstrip.json"
    if a.measure_only:
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        times = meta["times"]
        frames = sorted(root.glob("frame-*.png"))
    else:
        if a.at is None:
            print("filmstrip: --at is required (or --measure-only)", file=sys.stderr)
            return 2
        start = a.at - a.warmup
        times = frame_times(a.at, a.frames, a.every, start)
        exe = Path(a.app) if a.app else wt.project / "Builds" / "mac" / a.backend / APP
        if not exe.exists():
            print(f"filmstrip: no player at {exe} (run `demo3d build`)", file=sys.stderr)
            return 2
        if root.exists():
            shutil.rmtree(root)
        root.mkdir(parents=True)
        plan = chunks(times, a.chunk, root)
        extra = a.player_args.split()
        started = time.monotonic()
        with ThreadPoolExecutor(max_workers=a.workers) as pool:
            took = list(pool.map(lambda c: run_chunk(exe, wt.project, c, start, extra), plan))
        frames = collect(root, plan)
        meta = {"slug": a.slug, "at": a.at, "start": start, "times": times, "playerArgs": a.player_args,
                "app": str(exe)}
        meta_path.write_text(json.dumps(meta, indent=1) + "\n", encoding="utf-8")
        print(f"filmstrip: {len(frames)} frames from {len(plan)} players in {time.monotonic() - started:.0f} s "
              f"({sum(took):.0f} s of player time)", flush=True)

    width = round(TILE_W * a.scale)
    sheet = strip(frames, times, crop, a.columns, width)
    sheet.save(root / "strip.jpg", "JPEG", quality=88)
    video(frames, root / f"{a.slug}.mp4", a.fps, crop)
    print(f"filmstrip: {root / 'strip.jpg'} and {root / (a.slug + '.mp4')}")
    if a.evidence:
        dst = wt.project / "evidence" / a.evidence / f"{a.slug}.jpg"
        save_tagged(sheet, dst, f"{a.slug} t={times[0]:.3f}+{len(times)}f {a.player_args}".strip())
        print(f"filmstrip: {dst}")
    code = 0
    if a.baseline:
        other = wt.root / "captures" / "filmstrip" / a.baseline
        base_frames = sorted(other.glob("frame-*.png"))
        if len(base_frames) != len(frames):
            print(f"filmstrip: {a.baseline} has {len(base_frames)} frames, not {len(frames)}", file=sys.stderr)
            return 2
        vs = strip(frames, times, crop, a.columns, width, rows_from=[base_frames, frames], labels=[a.baseline, a.slug])
        vs.save(root / "versus.jpg", "JPEG", quality=88)
        print(f"filmstrip: {root / 'versus.jpg'}")
        if a.evidence:
            dst = wt.project / "evidence" / a.evidence / f"{a.slug}-vs-{a.baseline}.jpg"
            save_tagged(vs, dst, f"{a.baseline} above {a.slug} t={times[0]:.3f}+{len(times)}f")
            print(f"filmstrip: {dst}")
    if a.check:
        other = sorted((wt.root / "captures" / "filmstrip" / a.check).glob("frame-*.png"))
        if len(other) != len(frames):
            print(f"filmstrip: {a.check} has {len(other)} frames, not {len(frames)}", file=sys.stderr)
            return 1
        diffs = [d for d in compare(frames, other) if d[1]]
        for i, px, mx in diffs:
            print(f"filmstrip: frame {i} (t={times[i]:.3f}) differs from {a.check}'s in {px} px (largest {mx})")
        print(f"filmstrip: {len(frames) - len(diffs)} of {len(frames)} frames match {a.check}'s pixel for pixel")
        code = 1 if diffs else 0
    return code


def add_parser(sub) -> None:
    fs = sub.add_parser("filmstrip", help="consecutive windowless frames through a song time: a strip and an mp4")
    fs.add_argument("slug", help="the run's name: captures/filmstrip/SLUG")
    fs.add_argument("--at", type=float, help="the first frame's song time (s)")
    fs.add_argument("--frames", type=int, default=48)
    fs.add_argument("--every", type=int, default=1, help="keep every Nth fixed-time frame (1: 1/60 s apart)")
    fs.add_argument("--warmup", type=float, default=4.0, help="fixed-time seconds the players run before --at")
    fs.add_argument("--player-args", default="", help="the player's flags, e.g. the showcase preset")
    fs.add_argument("--crop", help="x,y,w,h in 1080p pixels from the top left, for the strip and the mp4")
    fs.add_argument("--columns", type=int, default=8)
    fs.add_argument("--scale", type=float, default=1.0, help="tile width as a multiple of 480 px")
    fs.add_argument("--fps", type=int, default=15, help="the mp4's rate (60 / --every: real time)")
    fs.add_argument("--chunk", type=int, default=240, help="frames per player (split runs have small seams)")
    fs.add_argument("--workers", type=int, default=4, help="players at a time")
    fs.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    fs.add_argument("--app", help="another player's executable (e.g. a saved build, for a before strip)")
    fs.add_argument("--evidence", help="also save the strip as demo3d/evidence/DIR/SLUG.jpg")
    fs.add_argument("--baseline", help="a kept run with the same frames: versus.jpg, it above this one")
    fs.add_argument("--check", help="a kept run with the same frames: exit 1 unless every frame matches")
    fs.add_argument("--measure-only", action="store_true", help="reassemble the kept frames, run no player")
    fs.set_defaults(func=cmd_filmstrip)
