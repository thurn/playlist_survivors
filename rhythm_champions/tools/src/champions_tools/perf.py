"""`champions perf`: the quality tiers' performance at 1,000 alive (plan.md, Performance). Never in CI.

It builds the IL2CPP player (Master C++, as a release ships), then plays the performance peak once per tier
(`--perf-peak`, Champions.Client.Bench.PerfPeak: level 1 held at the horde's cap against the maximum Storm build, kills
shattering and dropping pickups, the weapons' effects and flights in the air) headless at 1080p, its frames pipelined
as the game runs them. The player times each frame on the CPU: the frame, the sim's step, the presenters and the
camera's submission. The GPU's time comes from a Metal System Trace (`xcrun xctrace`, Xcode's) recorded for a few
seconds once the peak measures: each frame's GPU work is the player's command buffers up to the end of the one holding
its camera's last pass and the 1-pixel readback the peak asks for after it. A frame during which another process's
work (the window server's compositing, mostly) shared the GPU is counted apart, since sharing stretches every pass in
it. Without Xcode the GPU goes unmeasured and the frame times stand.

The budgets (plan.md, Performance): on this machine, the low tier's GPU p99 at most 1.5 ms over the frames the GPU
was the game's alone and its sim p99 at most 1 ms (the proxy for an M1 Air class Mac, assuming an 8x gap, until one
is measured), and every tier's frame p99 at most 1/60 s. Each tier's numbers go to OUT/TIER.json, all of them with the
verdicts to OUT/perf.json (OUT defaults to rhythm_champions/evidence/perf); exits 1 when a budget breaks.
"""

from __future__ import annotations

import argparse
import bisect
import itertools
import json
import platform
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

from . import paths, player_logs, setup

TIERS = ("low", "medium", "high")
MEASURING = "PerfPeak: measuring"  # PerfPeak.MeasuringLog
LOW_GPU_P99_MS = 1.5
LOW_SIM_P99_MS = 1.0
FRAME_P99_MS = 1000 / 60
TRACE_SECONDS = 4
PLAYER_PROCESS = "Rhythm Champions"


@dataclass
class Interval:
    start: int  # ns
    end: int
    buffer: str  # the command buffer's id
    channel: str  # Vertex, Fragment or Compute (blits too)
    ours: bool


def read_intervals(xml: Path, process: str = PLAYER_PROCESS) -> list[Interval]:
    """The top-level GPU intervals of an `xctrace export` of the metal-gpu-intervals table: each encoder's work on a
    channel, with its command buffer, and whether `process` submitted it. The export writes a repeated value once with
    an id and refers to it after (ref)."""
    seen: dict[str, ET.Element] = {}
    columns: list[str] = []
    out: list[Interval] = []
    for _, el in ET.iterparse(xml, events=("end",)):
        if el.tag == "schema":
            columns = [c.findtext("mnemonic") or "" for c in el.findall("col")]
            continue
        if el.tag != "row":
            continue
        values: dict[str, ET.Element] = {}
        for name, cell in zip(columns, el):
            for sub in cell.iter():
                if "id" in sub.attrib:
                    seen[sub.attrib["id"]] = sub
            values[name] = seen[cell.attrib["ref"]] if "ref" in cell.attrib else cell
        el.clear()
        if values["event-depth"].text != "0":
            continue
        start = int(values["start"].text or 0)
        owner = values["process"].attrib.get("fmt", "")
        out.append(Interval(start, start + int(values["duration"].text or 0), values["cmdbuffer-id"].attrib.get("fmt", ""),
                            values["channel-name"].text or "", owner.startswith(process)))
    return out


def union(spans: list[tuple[int, int]]) -> list[tuple[int, int]]:
    merged: list[tuple[int, int]] = []
    for a, b in sorted(spans):
        if merged and a <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(merged[-1][1], b))
        else:
            merged.append((a, b))
    return merged


def frame_ends(ours: list[Interval]) -> list[int]:
    """Where the player's frames end: each command buffer with render work whose last encoder is a blit, the frame's
    readback after its camera's last pass. (Unity sometimes commits a camera's passes in two command buffers: the
    first ends on a pass.) Upload and readback command buffers of their own have no render work."""
    buffers: dict[str, list[Interval]] = {}
    for i in ours:
        buffers.setdefault(i.buffer, []).append(i)
    return sorted(max(i.end for i in b) for b in buffers.values()
                  if any(i.channel != "Compute" for i in b) and max(b, key=lambda i: (i.start, i.end)).channel == "Compute")


def gpu_frames(intervals: list[Interval]) -> list[tuple[float, bool]]:
    """Each whole frame's GPU time (ms: the union of the player's work after the previous frame's end, up to its own:
    frame_ends) and whether another process's work overlapped it."""
    ours = sorted((i for i in intervals if i.ours), key=lambda i: i.end)
    others = union([(i.start, i.end) for i in intervals if not i.ours])
    other_starts = [a for a, _ in others]
    ends = frame_ends(ours)
    frames: list[tuple[float, bool]] = []
    k = 0
    for lo, hi in itertools.pairwise(ends):
        while k < len(ours) and ours[k].end <= lo:
            k += 1
        spans = []
        while k < len(ours) and ours[k].end <= hi:
            spans.append((ours[k].start, ours[k].end))
            k += 1
        spans = union(spans)
        first = spans[0][0]
        # The others' merged spans are disjoint and in order: the last to start before the frame ends is the one
        # that could reach back into it.
        last = bisect.bisect_left(other_starts, hi) - 1
        shared = last >= 0 and others[last][1] > first
        frames.append((sum(b - a for a, b in spans) / 1e6, shared))
    return frames


def percentile(values: list[float], q: float) -> float | None:
    """Nearest rank, as the player's (PerfPeak.At)."""
    if not values:
        return None
    s = sorted(values)
    return s[min(len(s) - 1, int(q * len(s)))]


def gpu_summary(frames: list[tuple[float, bool]]) -> dict:
    alone = [ms for ms, shared in frames if not shared]
    every = [ms for ms, _ in frames]
    return {
        "frames": len(frames), "sharedShare": round(1 - len(alone) / len(frames), 3) if frames else None,
        "alone": {"frames": len(alone), "p50": percentile(alone, 0.5), "p99": percentile(alone, 0.99), "max": percentile(alone, 1)},
        "all": {"p50": percentile(every, 0.5), "p99": percentile(every, 0.99), "max": percentile(every, 1)},
    }


def xctrace() -> bool:
    return shutil.which("xcrun") is not None and subprocess.run(
        ["xcrun", "--find", "xctrace"], capture_output=True, check=False).returncode == 0


def trace_gpu(pid: int, work: Path) -> dict | str:
    """A Metal System Trace of process `pid` for TRACE_SECONDS: its GPU summary, or why there is none."""
    trace, xml = work / "peak.trace", work / "intervals.xml"
    rec = subprocess.run(["xcrun", "xctrace", "record", "--template", "Metal System Trace", "--attach", str(pid),
                          "--time-limit", f"{TRACE_SECONDS}s", "--output", str(trace), "--no-prompt"],
                         capture_output=True, text=True, check=False)
    if rec.returncode != 0 or not trace.exists():
        return f"xctrace record failed ({rec.returncode}): {(rec.stderr or rec.stdout).strip()[-300:]}"
    with xml.open("wb") as f:
        exp = subprocess.run(["xcrun", "xctrace", "export", "--input", str(trace), "--xpath",
                              '/trace-toc/run[@number="1"]/data/table[@schema="metal-gpu-intervals"]'],
                             stdout=f, stderr=subprocess.PIPE, check=False)
    if exp.returncode != 0:
        return f"xctrace export failed ({exp.returncode}): {exp.stderr.decode(errors='replace').strip()[-300:]}"
    frames = gpu_frames(read_intervals(xml))
    return gpu_summary(frames) if frames else "the trace held no whole frame of the player's"


def run_tier(wt: paths.Worktree, app: Path, tier: str, seconds: float, out: Path, trace: bool) -> dict:
    """Plays the peak at `tier` headless; its numbers, with the GPU's from a trace once it measures."""
    result = out / f"{tier}.json"
    result.unlink(missing_ok=True)
    flags = ["--perf-peak", "--tier", tier, "--fixed-time", "--mute", "--seconds", str(seconds), "--bench-out", str(result)]
    run = player_logs.allocate(player_logs.checkout_id(wt.root), flags)
    log = run / "engine.log"
    player_logs.latest(wt.logs / f"perf-{tier}.log", log)
    started = time.monotonic()
    player = subprocess.Popen([str(app.parent / setup.APP_EXE), "-batchmode", "-force-metal", "-logFile", str(log), *flags],
                              cwd=wt.root, stdout=subprocess.DEVNULL)
    gpu: dict | str = "not traced (--no-trace)" if not trace else "not traced: Xcode's xctrace is missing"
    if trace and xctrace():
        while player.poll() is None and not (log.exists() and MEASURING in log.read_text(errors="replace")):
            time.sleep(0.1)
        if player.poll() is None:
            with tempfile.TemporaryDirectory(prefix="champions-perf-") as work:
                gpu = trace_gpu(player.pid, Path(work))
        else:
            gpu = "not traced: the player ended before it measured"
    code = player.wait()
    player_logs.complete(run, code)
    print(f"perf: {tier}: the player exited {code} after {time.monotonic() - started:.1f} s (log {log})")
    if code != 0 or not result.exists():
        raise PerfError(f"the {tier} tier's peak wrote no timings (exit {code}; see {log})")
    numbers = json.loads(result.read_text(encoding="utf-8"))
    numbers["gpuMs"] = gpu
    numbers["machine"] = platform.machine()
    result.write_text(json.dumps(numbers, indent=2) + "\n", encoding="utf-8")
    return numbers


class PerfError(Exception):
    pass


def verdicts(tiers: dict[str, dict]) -> list[tuple[str, float | None, float, bool | None]]:
    """(budget, measured, limit, kept: None when unmeasured) for each budget the tiers ran."""
    out: list[tuple[str, float | None, float, bool | None]] = []
    low = tiers.get("low")
    if low:
        gpu = low["gpuMs"]["alone"]["p99"] if isinstance(low["gpuMs"], dict) else None
        out.append(("low tier GPU p99 (ms, the GPU the game's alone)", gpu, LOW_GPU_P99_MS, None if gpu is None else gpu <= LOW_GPU_P99_MS))
        sim = low["simMs"]["p99"]
        out.append(("low tier sim p99 (ms)", sim, LOW_SIM_P99_MS, sim <= LOW_SIM_P99_MS))
    for tier, n in tiers.items():
        frame = n["frameMs"]["p99"]
        out.append((f"{tier} tier frame p99 (ms, 60 fps)", frame, FRAME_P99_MS, frame <= FRAME_P99_MS))
    return out


def fmt(v: float | None) -> str:
    return "-" if v is None else f"{v:.3f}"


def cmd_perf(a: argparse.Namespace) -> int:
    from .cli import build

    wt = paths.worktree()
    if not a.no_build and build(wt, "il2cpp") != 0:
        print("perf: the IL2CPP build failed", file=sys.stderr)
        return 2
    app = setup.player_dir(wt, "il2cpp") / setup.APP
    if not app.is_dir():
        print(f"perf: no IL2CPP player at {app}", file=sys.stderr)
        return 2
    out = Path(a.out).resolve() if a.out else wt.project / "evidence" / "perf"
    out.mkdir(parents=True, exist_ok=True)
    tiers: dict[str, dict] = {}
    try:
        for tier in a.tier or TIERS:
            tiers[tier] = run_tier(wt, app, tier, a.seconds, out, not a.no_trace)
    except PerfError as e:
        print(f"perf: {e}", file=sys.stderr)
        return 2
    print(f"perf: {'tier':7} {'alive':>5} {'screen':>6} {'kills/s':>7} {'frame p50/p99':>14} {'sim p50/p99':>12} "
          f"{'GPU alone p50/p99':>18} {'shared':>6}")
    for tier, n in tiers.items():
        g = n["gpuMs"]
        gpu = f"{fmt(g['alone']['p50'])}/{fmt(g['alone']['p99'])}" if isinstance(g, dict) else "-"
        shared = f"{g['sharedShare']:.0%}" if isinstance(g, dict) and g["sharedShare"] is not None else "-"
        print(f"perf: {tier:7} {n['alive']['p50']:5.0f} {n['onScreen']['p50']:6.0f} {n['killsPerS']:7.1f} "
              f"{fmt(n['frameMs']['p50'])}/{fmt(n['frameMs']['p99']):>6} {fmt(n['simMs']['p50'])}/{fmt(n['simMs']['p99'])} "
              f"{gpu:>18} {shared:>6}")
        if not isinstance(g, dict):
            print(f"perf: {tier}: GPU {g}")
    budgets = verdicts(tiers)
    for name, measured, limit, kept in budgets:
        state = "unmeasured" if kept is None else "kept" if kept else "BROKEN"
        print(f"perf: {name}: {fmt(measured)} against {limit:.3f}: {state}")
    summary = {"tiers": tiers, "budgets": [{"budget": n, "measured": m, "limit": l, "kept": k} for n, m, l, k in budgets]}
    (out / "perf.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"perf: wrote {out.relative_to(wt.root) if out.is_relative_to(wt.root) else out}")
    return 1 if any(k is False for *_, k in budgets) else 0
