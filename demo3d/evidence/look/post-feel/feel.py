"""ps-e8r.18's post feel stills: the post stack following the music, beside `demo3d look post-feel`.

`uv run --project demo3d/tools python demo3d/evidence/look/post-feel/feel.py [--measure-only]` from the
worktree's root, after `demo3d build`. Windowless batch-mode IL2CPP player in fixed time (mac.md), as
the look harness runs it. From the song map it picks:
- a beat in the middle of drop-2, away from accents: the frame on the beat and 0.6 of a beat later, and
  the on-beat frame again with `--no-feel` (the steady state);
- one still in the breakdown, two in break-free stretches of each drop and the final peak, all 0.6 of a
  beat after a downbeat away from accents, so the section's grade dominates;
- a strip of frames every 2/60 s from just before an isolated accent in drop-2 to just past its beat;
- the strip's accent frame rendered again in another process, and twice with `--no-effects`, compared
  pixel for pixel (the effects' GPU particles vary slightly between runs, with or without the feel).
Full PNGs go to the gitignored captures/look/post-feel-feel/; small tagged JPEGs, strip.jpg and feel.md
go next to this script.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools import look, paths

HERE = Path(__file__).resolve().parent
FPS = 60
WARMUP_S = 5.0


def frame_at(start: float, t: float) -> float:
    """The first fixed-time frame at or after t, for a run from start."""
    k = int(np.ceil((t - start) * FPS - 1e-6))
    return round(start + k / FPS, 6)


class Song:
    def __init__(self, path: Path):
        m = json.loads(path.read_text(encoding="utf-8"))
        self.beats = m["beats"]
        self.accents = [a["time"] for a in m["accents"]]
        for s in m["sections"]:
            self.accents += [a["time"] for a in s.get("anchors", [])]
        self.accents.sort()
        bpb = m["meter"][0]["beatsPerBar"] if m.get("meter") else 4
        self.sections = {}
        for s in m["sections"]:
            first = m["firstDownbeatIndex"] + (s["bars"][0] - 1) * bpb
            end = m["firstDownbeatIndex"] + s["bars"][1] * bpb
            self.sections[s["label"]] = (first, min(end, len(self.beats) - 1))
        self.bpb = bpb

    def beat_len(self, b: int) -> float:
        return self.beats[b + 1] - self.beats[b]

    def quiet(self, b: int) -> bool:
        """No accent from two beats before beat b to two after."""
        lo, hi = self.beats[max(0, b - 2)], self.beats[min(len(self.beats) - 1, b + 2)]
        return not any(lo <= a <= hi for a in self.accents)

    def quiet_downbeats(self, label: str, skip_bars: int = 2) -> list[int]:
        first, end = self.sections[label]
        return [b for b in range(first + skip_bars * self.bpb, end - 2, self.bpb) if self.quiet(b)]


def run(exe: Path, cwd: Path, out: Path, start: float, times: list[float], extra: list[str]) -> None:
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    cmd = [str(exe), "-batchmode", "-force-metal", "-logFile", str(out / "player.log"), "--fixed-time",
           "--start-at", f"{start:.3f}", "--stills-at", ",".join(f"{t:.6f}" for t in times),
           *extra, "--capture-stills", str(out), "--stills-format", "png"]
    code = subprocess.run(cmd, check=False, cwd=cwd, timeout=1800, stdout=subprocess.DEVNULL).returncode
    if code != 0 or len(list(out.glob("still-*.png"))) < len(times):
        raise SystemExit(f"feel: {out.name} exited {code} (see {out / 'player.log'})")
    print(f"feel: {out.name} done", flush=True)


def still(out: Path, t: float) -> Path:
    return out / f"still-{t:07.3f}.png"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--measure-only", action="store_true")
    ap.add_argument("--backend", default="il2cpp")
    a = ap.parse_args()
    wt = paths.worktree(ROOT)
    song = Song(wt.project / "Assets" / "Baked" / "Audio" / "action.songmap.json")
    captures = wt.root / "captures" / "look" / "post-feel-feel"

    # On and off a beat in drop-2.
    d2 = song.quiet_downbeats("drop-2")
    beat = d2[len(d2) // 2] + 1  # the bar's second beat
    bt = song.beats[beat]
    beat_start = round(bt - WARMUP_S)
    on, off = frame_at(beat_start, bt), frame_at(beat_start, bt + 0.6 * song.beat_len(beat))

    # The sections' grades: 0.6 of a beat after quiet downbeats.
    picks: list[tuple[str, int]] = []
    bd = song.quiet_downbeats("breakdown", 1)
    picks += [("breakdown-a", bd[len(bd) // 3]), ("breakdown-b", bd[2 * len(bd) // 3])]
    for label in ("drop-1", "drop-2", "final-peak"):
        q = song.quiet_downbeats(label)
        picks += [(f"{label}-a", q[len(q) // 3]), (f"{label}-b", q[2 * len(q) // 3])]
    section_runs = []
    for name, b in picks:
        t = song.beats[b] + 0.6 * song.beat_len(b)
        start = round(t - WARMUP_S)
        section_runs.append((name, start, frame_at(start, t)))

    # A strip around an isolated accent in drop-2.
    d2_first, d2_end = song.sections["drop-2"]
    lone = [x for x in song.accents if song.beats[d2_first] + 2 < x < song.beats[d2_end] - 2
            and not any(0 < y - x < 1.5 for y in song.accents) and not any(0 < x - y < 1.5 for y in song.accents)]
    acc = lone[len(lone) // 2]
    strip_start = round(acc - WARMUP_S)
    acc_frame = frame_at(strip_start, acc)
    strip = [round(acc_frame + k * 2 / FPS, 6) for k in range(-1, 13)]

    runs = {
        "beat": (beat_start, [on, off], []),
        "beat-nofeel": (beat_start, [on], ["--no-feel"]),
        "strip": (strip_start, strip, []),
        "strip-again": (strip_start, [acc_frame], []),
        "repro-a": (strip_start, [acc_frame], ["--no-effects"]),
        "repro-b": (strip_start, [acc_frame], ["--no-effects"]),
        **{n: (s, [t], []) for n, s, t in section_runs},
    }
    if not a.measure_only:
        exe = look.player(wt, a.backend)
        with ThreadPoolExecutor(max_workers=4) as pool:
            list(pool.map(lambda kv: run(exe, wt.project, captures / kv[0], kv[1][0], kv[1][1], kv[1][2]), runs.items()))

    def stats(name: str, t: float) -> dict:
        return look.still_stats(look.load_rgb(still(captures / name, t)))

    def row(label: str, t: float, s: dict) -> str:
        return f"| {label} | {t:.3f} | {s['p50']:.1f} | {s['p98']:.1f} | {s['chroma']:.1f} | {s['contrast']:.1f} |"

    lines = ["# Post feel stills (ps-e8r.18)", "",
             "Rendered by feel.py (IL2CPP player, fixed time). L* p50 and p98, mean chroma C*ab and RMS contrast",
             "(the standard deviation of L*) over the whole frame.", ""]
    head = ["| still | t (s) | L* p50 | L* p98 | chroma | RMS |", "|---|---|---|---|---|---|"]

    lines += ["## On and off a beat (drop-2)", "", *head]
    for label, name, t in (("on the beat", "beat", on), ("0.6 beat later", "beat", off), ("on the beat, --no-feel", "beat-nofeel", on)):
        lines.append(row(label, t, stats(name, t)))
        look.small_jpeg(still(captures / name, t), HERE / f"{name}-{'on' if t == on else 'off'}.jpg", f"post feel {name} {t:.3f}")

    lines += ["", "## Sections (0.6 beat after a quiet downbeat)", "", *head]
    chroma: dict[str, list[float]] = {}
    for name, _, t in section_runs:
        s = stats(name, t)
        chroma.setdefault(name.rsplit("-", 1)[0], []).append(s["chroma"])
        lines.append(row(name, t, s))
        look.small_jpeg(still(captures / name, t), HERE / f"{name}.jpg", f"post feel {name} {t:.3f}")
    worst_break = max(chroma["breakdown"])
    hot = {k: min(v) for k, v in chroma.items() if k != "breakdown"}
    lines += ["", f"Lowest drop or peak chroma {min(hot.values()):.1f} against the highest breakdown chroma {worst_break:.1f}: "
              + ("above." if min(hot.values()) > worst_break else "NOT above."), ""]

    lines += [f"## Strip around the accent at {acc:.3f} s (every 2 frames)", "", *head]
    thumbs = []
    for t in strip:
        lines.append(row(f"{(t - acc) * 1000:+.0f} ms", t, stats("strip", t)))
        with Image.open(still(captures / "strip", t)) as im:
            thumbs.append((t, im.convert("RGB").resize((320, 180), Image.LANCZOS)))
    cols = 7
    sheet = Image.new("RGB", (cols * 320, ((len(thumbs) + cols - 1) // cols) * 198), (16, 16, 16))
    draw = ImageDraw.Draw(sheet)
    for i, (t, im) in enumerate(thumbs):
        x, y = (i % cols) * 320, (i // cols) * 198
        sheet.paste(im, (x, y + 18))
        draw.text((x + 4, y + 3), f"{(t - acc) * 1000:+.0f} ms", fill=(230, 230, 230))
    sheet.save(HERE / "strip.jpg", "JPEG", quality=85, comment=b"ps-render post feel strip")

    def compare(x: str, y: str) -> str:
        d = np.abs(look.load_rgb(still(captures / x, acc_frame)).astype(int)
                   - look.load_rgb(still(captures / y, acc_frame)).astype(int)).max(axis=-1)
        return f"max channel difference {int(d.max())}, {(d > 0).mean() * 100:.4f}% of pixels differ, {(d > 2).mean() * 100:.4f}% by more than 2"

    lines += ["", "## Reproducibility", "",
              f"The accent's frame ({acc_frame:.3f} s, mid-flash) rendered by two player processes:", "",
              f"- with effects (the strip's run and another): {compare('strip', 'strip-again')};",
              f"- with `--no-effects`: {compare('repro-a', 'repro-b')}.", ""]
    (HERE / "feel.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
