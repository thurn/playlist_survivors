"""`game log [SESSION]`: a recorded session's timing log summarised (docs/design/unity-game.md, Sessions and Replay).

Every recorded run of the game's player writes `timing.csv` beside its audio, entries and inputs (TimingLog.cs):
a row per frame with its interval, the main thread's CPU time, the loop's and the sim's work, Unity's frame timing,
frameSong, the lead, the hero and the frame's share of the run's counts. This summarises the newest session that has
one, or SESSION: frame-time percentiles, each hitch (a frame interval over --hitch ms) with the song time of the
frame that took it, the lead, the counts, and a table per second of song unless --no-table, as native's `ps log`.

A row's interval and CPU time run from the previous row, so a hitch is shown at the previous frame: its song time and
its loop's, step's and sim's work, beside the interval and the CPU time that frame took.
"""

from __future__ import annotations

import argparse
import csv
import math
import os
import sys
from pathlib import Path

FILE = "timing.csv"
HITCH_MS = 1000 / 30
HITCHES_SHOWN = 20
COUNTS = ["events", "kills", "entries", "late", "keys", "sounds", "dropped", "cut", "effects", "rejected", "gc", "faults",
          "freezes", "stalls"]


def add_parser(sub: argparse._SubParsersAction) -> None:
    p = sub.add_parser("log", help="summarise a recorded session's timing log (the newest by default)")
    p.add_argument("session", nargs="?", help="the session's folder (default: the newest in the player's sessions)")
    p.add_argument("--from", dest="start", type=float, help="only frames from this song time (s)")
    p.add_argument("--to", dest="end", type=float, help="only frames up to this song time (s)")
    p.add_argument("--hitch", type=float, default=HITCH_MS, help=f"a hitch's least frame interval in ms (default {HITCH_MS:.1f})")
    p.add_argument("--no-table", action="store_true", help="leave out the table per second of song")
    p.set_defaults(func=cmd_log)


def sessions_root() -> Path:
    """The player's sessions folder (Application.persistentDataPath/sessions)."""
    if sys.platform == "win32":
        base = Path(os.environ.get("USERPROFILE", Path.home())) / "AppData" / "LocalLow"
    else:
        base = Path.home() / "Library" / "Application Support"
    return base / "Playlist Survivors" / "Playlist Survivors" / "sessions"


def newest(root: Path) -> Path | None:
    """The newest session in `root` with a timing log (session folders are named by their start time)."""
    found = sorted(p for p in root.iterdir() if (p / FILE).is_file()) if root.is_dir() else []
    return found[-1] if found else None


def read(path: Path) -> list[dict]:
    """The timing log's rows, numbers as floats (nan before the sim's first step) and `phase` as given.

    A run that didn't end cleanly (killed, crashed) can leave its last row cut off where the player's buffer filled:
    a last line without its line end is left out."""
    lines = path.read_text().splitlines(keepends=True)
    if lines and not lines[-1].endswith("\n"):
        lines.pop()
    return [{k: (v if k == "phase" else float(v)) for k, v in row.items()} for row in csv.DictReader(lines)]


def pct(sorted_values: list[float], p: float) -> float:
    """The nearest-rank percentile the player uses (RunTelemetry.Pct)."""
    return sorted_values[round(p * (len(sorted_values) - 1))]


def spread(values: list[float]) -> str:
    v = sorted(values)
    return f"p50/p95/p99/max {pct(v, 0.5):.1f}/{pct(v, 0.95):.1f}/{pct(v, 0.99):.1f}/{v[-1]:.1f}" if v else "none"


def known(rows: list[dict], key: str) -> list[float]:
    """The column's values where the player could measure them (-1: unknown)."""
    return [r[key] for r in rows if r[key] >= 0]


def ms(v: float) -> str:
    return f"{v:.1f}" if v >= 0 else "-"


def summarise(rows: list[dict], start: float | None = None, end: float | None = None, hitch_ms: float = HITCH_MS,
              table: bool = True) -> list[str]:
    """The summary's lines for the rows whose frame (or, for a hitch, the frame before) has its song time in [start, end]."""
    def inside(r: dict) -> bool:
        song = r["frame_song"]
        if math.isnan(song):
            return start is None and end is None
        return (start is None or song >= start) and (end is None or song <= end)

    # Each row with the one before it: the interval and CPU time a row reports are the previous frame's.
    pairs = [(rows[i - 1], rows[i]) for i in range(1, len(rows)) if inside(rows[i - 1])]
    frames = [r for r in rows if inside(r)]
    if not frames:
        return ["no frames in that span"]
    stepped = [r for r in frames if r["stepped"] and not math.isnan(r["frame_song"])]
    intervals = [cur["interval_ms"] for _, cur in pairs]
    out = [f"{len(frames)} frames over {frames[-1]['real_s'] - frames[0]['real_s']:.1f} s"
           + (f", song {stepped[0]['frame_song']:.1f} to {stepped[-1]['frame_song']:.1f} s" if stepped else ", the sim never stepped")]
    if intervals:
        # Over 25 ms: a 60 Hz display's missed refresh (a frame's interval jitters around 16.7 ms).
        out.append(f"interval (ms) {spread(intervals)}; over 25 ms {sum(v > 25 for v in intervals)}, "
                   f"over {hitch_ms:.1f} ms {sum(v > hitch_ms for v in intervals)}")
    cpu = [cur["cpu_ms"] for _, cur in pairs if cur["cpu_ms"] >= 0]
    if cpu:
        out.append(f"main thread CPU (ms) {spread(cpu)}")
    out.append("work (ms): " + "; ".join(f"{name} {spread(known(rows_, key))}" for name, key, rows_ in [
        ("loop", "loop_ms", frames), ("step", "step_ms", stepped), ("sim", "sim_ms", stepped),
        ("render thread", "render_ms", frames), ("gpu", "gpu_ms", frames)] if known(rows_, key)))
    leads = sorted(r["lead_ms"] for r in stepped)
    if leads:
        out.append(f"lead (ms) median {pct(leads, 0.5):.1f}, p5..p95 {pct(leads, 0.05):.1f}..{pct(leads, 0.95):.1f}, "
                   f"min..max {leads[0]:.1f}..{leads[-1]:.1f}")
    totals = {k: int(sum(r[k] for r in frames)) for k in COUNTS}
    out.append(f"counts: {totals['kills']} kills, {totals['events']} sim events, {totals['entries']} entries ({totals['late']} late), "
               f"{totals['keys']} key changes, {totals['sounds']} sounds ({totals['dropped']} dropped, {totals['cut']} cut off), "
               f"{totals['effects']} effects ({totals['rejected']} rejected), {totals['gc']} collections, "
               f"{totals['faults']} presenter faults, {totals['freezes']} freezes, {totals['stalls']} music stalls")
    alive = [int(r["alive"]) for r in stepped]
    if alive:
        out.append(f"alive median {int(pct(sorted(alive), 0.5))}, max {max(alive)}")

    hitches = [(prev, cur) for prev, cur in pairs if cur["interval_ms"] > hitch_ms]
    if hitches:
        worst = sorted(hitches, key=lambda pc: pc[1]["interval_ms"], reverse=True)[:HITCHES_SHOWN]
        out.append(f"hitches over {hitch_ms:.1f} ms: {len(hitches)}"
                   + (f" (the worst {len(worst)})" if len(worst) < len(hitches) else "") + ", in song order:")
        out.append(f"  {'song s':>9} {'interval':>8} {'cpu':>6} {'loop':>6} {'step':>6} {'sim':>6} {'gpu':>6} {'alive':>5} {'gc':>3} "
                   f"{'events':>6} phase")
        for prev, cur in sorted(worst, key=lambda pc: pc[0]["real_s"]):
            song = "-" if math.isnan(prev["frame_song"]) else f"{prev['frame_song']:.3f}"
            out.append(f"  {song:>9} {cur['interval_ms']:8.1f} {ms(cur['cpu_ms']):>6} {prev['loop_ms']:6.1f} {prev['step_ms']:6.1f} "
                       f"{prev['sim_ms']:6.2f} {ms(cur['gpu_ms']):>6} {int(prev['alive']):5d} {int(cur['gc']):3d} "
                       f"{int(prev['events']):6d} {prev['phase']}{' (frozen)' if prev['frozen'] else ''}")
    if table and stepped:
        out.append("per second of song: frames, worst interval and CPU (ms), sim max (ms), alive, kills, late, dropped, gc")
        by_second: dict[int, list[tuple[dict, dict]]] = {}
        for prev, cur in pairs:
            if prev["stepped"] and not math.isnan(prev["frame_song"]):
                by_second.setdefault(math.floor(prev["frame_song"]), []).append((prev, cur))
        for s in sorted(by_second):
            ps = by_second[s]
            cpu_s = [cur["cpu_ms"] for _, cur in ps if cur["cpu_ms"] >= 0]
            out.append(f"  {s:5d} {len(ps):4d} {max(cur['interval_ms'] for _, cur in ps):6.1f} {ms(max(cpu_s) if cpu_s else -1):>6} "
                       f"{max(p['sim_ms'] for p, _ in ps):6.2f} {int(max(p['alive'] for p, _ in ps)):5d} "
                       f"{int(sum(p['kills'] for p, _ in ps)):4d} {int(sum(p['late'] for p, _ in ps)):3d} "
                       f"{int(sum(p['dropped'] for p, _ in ps)):3d} {int(sum(p['gc'] for p, _ in ps)):3d}")
    return out


def cmd_log(a: argparse.Namespace) -> int:
    if a.session:
        session = Path(a.session).expanduser()
    else:
        root = sessions_root()
        session = newest(root)
        if session is None:
            print(f"log: no session with a {FILE} in {root}", file=sys.stderr)
            return 1
    path = session / FILE
    if not path.is_file():
        print(f"log: {session} has no {FILE} (recorded before the timing log, or not a session)", file=sys.stderr)
        return 1
    print(f"timing: {path}")
    print("\n".join(summarise(read(path), a.start, a.end, a.hitch, not a.no_table)))
    return 0
