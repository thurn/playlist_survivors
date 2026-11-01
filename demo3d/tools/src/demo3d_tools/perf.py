"""`game perf`: the run's performance on the Mac, windowless (docs/design/unity-game.md, Performance targets).

It runs the built player (building it first when there is none, or with --build) in batch mode and
fixed time over the standard scenarios, each with the player's frame probe (`--gpu-probe-at`: timed
1080p frames with MSAA 4x through GPU completion, after a warm-up) and its CPU frame profile
(`--profile-out`: each frame's main-thread work by part, its allocation and collections, the
fixed-time frame's music rendering and analysis wait left out):

- `peak`: minute 25 (`--start-minute 25`) with Bloody Tear, Soul Eater and Holy Wand evolved at 5 s,
  the positioning bot playing, the crowd near its cap of 300;
- `break`: the same with a structure break at 64 s (`--debug break@64`);
- `finale`: the same with the finale's mass kill forced at 68 s (`--debug finale@68`);
- `early`: the run's first minute on the same song;
- `full`: the 30-minute fixed-time File run with the bot, for the CPU profile (about 4 minutes).

Each scenario's CSV and probe JSON and the player's log stay in the gitignored captures/game/perf/NAME/;
the summary (per scenario: the probe's frame p50/p99/max, the main thread's p50/p99/max by part over
the scenario's window of song time, allocations and collections, and the sim's step p99 at 285 or
more alive) goes to captures/game/perf/summary.json, and with `--evidence DIR` beside the probes'
JSON into game/evidence/DIR/. It exits 1 if a player fails (a non-zero exit, an exception in its log,
or a missing profile or probe point). With `--ablate` the probe also draws each counted frame's
neighbours with one pass turned off (`--gpu-probe-ablate`), and the summary has each pass's saving.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import shutil
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path

from . import paths, setup

SONG = "public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
PLAYLIST = "native/playlists/survivors-30-twice.m3u"
EVOLVED = "evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5"
PEAK = ["--start-minute", "25", "--bot", "positioning"]
# The sim's step is measured against the target at 300 alive: frames this close to the cap count.
PEAK_ALIVE = 285
# With --ablate a probe point draws GpuProbe.AblateRounds rounds of its ten frame kinds (250 frames,
# 4.2 s of song at fixed time): its points are spaced this far apart and the run goes on this long after.
ABLATE_S = 4.5
# The columns that aren't parts of the frame's work.
NOT_PARTS = {"frame_ms", "wait_ms", "probe_ms"}


@dataclass(frozen=True)
class Scenario:
    name: str
    help: str
    args: list[str]
    # Song times: the frame probe's, and the window the CPU stats cover.
    probe: list[float]
    window: tuple[float, float]
    # --seconds (song time at fixed time); None runs to the run's end.
    seconds: float | None
    file: str = SONG


SCENARIOS = [
    Scenario("peak", "minute 25, three evolutions, the crowd near 300", [*PEAK, "--debug", EVOLVED],
             [40, 50, 60], (30, 62), 62),
    Scenario("break", "the peak with a structure break at 64 s", [*PEAK, "--debug", EVOLVED + ",break@64"],
             [64.05, 64.4, 65], (64, 66), 66),
    Scenario("finale", "the peak with the finale's mass kill at 68 s", [*PEAK, "--debug", EVOLVED + ",finale@68"],
             [68.4, 68.7, 69.2], (68, 71), 71),
    Scenario("early", "the run's first minute", ["--run", "--bot", "positioning"], [45, 60, 75], (20, 80), 80),
    Scenario("full", "the 30-minute fixed-time File run (CPU profile only)", ["--run", "--bot", "positioning"],
             [], (0, math.inf), None, file=PLAYLIST),
]


def add_parser(sub: argparse._SubParsersAction) -> None:
    p = sub.add_parser("perf", help="the run's frame probe and CPU profile over the standard scenarios (Mac, windowless)")
    p.add_argument("--only", help=f"comma-separated scenarios (default all: {', '.join(s.name for s in SCENARIOS)})")
    p.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    p.add_argument("--build", action="store_true", help="rebuild the player first (after a change)")
    p.add_argument("--evidence", help="also write the summary and the probes' JSON to game/evidence/DIR")
    p.add_argument("--ablate", action="store_true",
                   help="also time the probe's frames with each pass turned off (SSAO, post, shadows, ...)")
    p.set_defaults(func=cmd_perf)


def cmd_perf(a: argparse.Namespace) -> int:
    if sys.platform != "darwin":
        print("perf: the game's windowless probe runs on the Mac", file=sys.stderr)
        return 2
    wanted = a.only.split(",") if a.only else [s.name for s in SCENARIOS]
    unknown = set(wanted) - {s.name for s in SCENARIOS}
    if unknown:
        print(f"perf: no scenario {', '.join(sorted(unknown))}", file=sys.stderr)
        return 2
    wt = paths.worktree(name="game")
    from .game import APP_EXE

    exe = setup.player_dir(wt, "mac", a.backend) / APP_EXE
    if a.build or not exe.exists():
        from . import cli

        code = cli.cmd_build(argparse.Namespace(
            project="game", platform="mac", backend=a.backend, out=None, development=False,
            scene=None, cold=False, force=False))
        if code != 0:
            return code
    root = wt.root / "captures" / "game" / "perf"
    summary, failed = {}, []
    for scenario in (s for s in SCENARIOS if s.name in wanted):
        result = run(exe, wt.root, root / scenario.name, scenario, ablate=a.ablate)
        summary[scenario.name] = result
        if not result["ok"]:
            failed.append(scenario.name)
        print(report_line(scenario.name, result))
    root.mkdir(parents=True, exist_ok=True)
    text = json.dumps(summary, indent=2) + "\n"
    (root / "summary.json").write_text(text, encoding="utf-8")
    if a.evidence:
        evidence = wt.project / "evidence" / a.evidence
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / "summary.json").write_text(text, encoding="utf-8")
        for name in summary:
            probe = root / name / "gpu-probe.json"
            if probe.exists():
                shutil.copy(probe, evidence / f"gpu-probe-{name}.json")
    print(f"perf: summary in {(root / 'summary.json').relative_to(wt.root)}")
    if failed:
        print(f"perf: failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


def run(exe: Path, repo: Path, out: Path, s: Scenario, ablate: bool = False) -> dict:
    """One scenario in the player, at fixed time in batch mode; its summary, `ok` false on a failure."""
    from .spike import exceptions

    shutil.rmtree(out, ignore_errors=True)
    out.mkdir(parents=True)
    log, profile, probe = out / "player.log", out / "profile.csv", out / "gpu-probe.json"
    cmd = [str(exe), "-batchmode", "-force-metal", "-logFile", str(log), "--fixed-time",
           "--file", str((repo / s.file).resolve()), "--profile-out", str(profile), *s.args]
    points, seconds = s.probe, s.seconds
    if ablate and points:
        points = ablate_points(points)
        seconds = max(seconds, points[-1] + ABLATE_S) if seconds is not None else None
    if points:
        cmd += ["--gpu-probe-at", ",".join(f"{t:g}" for t in points), "--gpu-probe-out", str(probe)]
        if ablate:
            cmd.append("--gpu-probe-ablate")
    if seconds is not None:
        cmd += ["--seconds", f"{seconds:g}"]
    started = time.monotonic()
    # (A hang guard: the full run takes about 4 minutes, a peak scenario under one.)
    try:
        code = subprocess.run(cmd, check=False, cwd=repo, stdout=subprocess.DEVNULL,
                              timeout=1800 if s.seconds is None else 600).returncode
    except subprocess.TimeoutExpired:
        code = -1
    # (The flags as given, paths from the repository root.)
    args = [arg.replace(f"{repo.resolve()}/", "") for arg in cmd[cmd.index("--fixed-time"):]]
    result: dict = {"help": s.help, "args": args, "exit": code,
                    "wall_s": round(time.monotonic() - started, 1)}
    errors = [f"the player exited {code}"] if code != 0 else []
    errors += [f"the player threw: {line}" for line in exceptions(log)[:5]]
    if profile.exists():
        result["cpu"] = summarise(read_profile(profile), s.window)
        if result["cpu"]["frames"] == 0:
            errors.append("no profiled frame in the window")
    else:
        errors.append("no profile")
    if s.probe:
        if probe.exists():
            result["probe"] = probe_summary(json.loads(probe.read_text()))
            if len(result["probe"]["points"]) != len(points):
                errors.append(f"{len(result['probe']['points'])} of {len(points)} probe points")
        else:
            errors.append("no probe")
    result["ok"] = not errors
    if errors:
        result["errors"] = errors
        for e in errors:
            print(f"perf: {s.name}: {e} (log {log})", file=sys.stderr)
    return result


def ablate_points(points: list[float]) -> list[float]:
    """The probe points an ablation run keeps: each at least ABLATE_S after the last one kept."""
    kept: list[float] = []
    for t in points:
        if not kept or t >= kept[-1] + ABLATE_S:
            kept.append(t)
    return kept


def read_profile(path: Path) -> list[dict[str, float]]:
    """The player's `--profile-out` CSV, each row's columns as numbers."""
    with path.open(newline="") as f:
        return [{k: float(v) for k, v in row.items()} for row in csv.DictReader(f)]


def pct(sorted_values: list[float], p: float) -> float:
    """The nearest-rank percentile the player uses (RunTelemetry.Pct)."""
    return sorted_values[round(p * (len(sorted_values) - 1))]


def stats(values: list[float]) -> dict[str, float]:
    v = sorted(values)
    return {"p50": round(pct(v, 0.5), 3), "p99": round(pct(v, 0.99), 3), "max": round(v[-1], 3),
            "mean": round(sum(v) / len(v), 3)}


def summarise(rows: list[dict[str, float]], window: tuple[float, float]) -> dict:
    """The CPU profile's frames whose song time is in `window`: the frame's own work (its music and
    analysis wait and the probe's draw already left out by the player) and each part's p50, p99, max
    and mean in ms, allocations and collections, and the sim's step p99 near 300 alive."""
    frames = [r for r in rows if window[0] <= r["frame_song"] <= window[1]]
    if not frames:
        return {"frames": 0}
    parts = [k[:-3] for k in frames[0] if k.endswith("_ms") and k not in NOT_PARTS]
    by_part = {p: stats([r[f"{p}_ms"] for r in frames]) for p in parts}
    alloc = sorted(r["alloc_bytes"] for r in frames)
    peak = sorted(r["sim_ms"] for r in frames if r["alive"] >= PEAK_ALIVE)
    frame_ms = [r["frame_ms"] for r in frames]
    alive = sorted(r["alive"] for r in frames)
    return {
        "frames": len(frames),
        "song": [round(frames[0]["frame_song"], 3), round(frames[-1]["frame_song"], 3)],
        "alive": {"median": int(pct(alive, 0.5)), "max": int(alive[-1])},
        "frame_ms": stats(frame_ms),
        "over_16_7ms": sum(ms > 1000 / 60 for ms in frame_ms),
        "over_33ms": sum(ms > 1000 / 30 for ms in frame_ms),
        "parts_ms": by_part,
        "top": sorted(parts, key=lambda p: by_part[p]["mean"], reverse=True)[:3],
        "wait_s": round(sum(r["wait_ms"] for r in frames) / 1000, 1),
        "alloc_bytes": {"mean": round(sum(alloc) / len(alloc)), "p99": int(pct(alloc, 0.99)), "max": int(alloc[-1]),
                        "frames": sum(b > 0 for b in alloc)},
        "gc_collections": int(sum(r["gc"] for r in frames)),
        "sim_at_peak": {"alive_from": PEAK_ALIVE, "frames": len(peak), "p99_ms": round(pct(peak, 0.99), 3) if peak else None},
    }


def probe_summary(probe: dict) -> dict:
    """The frame probe's counted frames over every point: p50, p99 and max in ms, and each point's."""
    ms = sorted(f[2] for p in probe["points"] for f in p["frames"])
    return {
        "device": probe.get("graphicsDeviceName"), "size": probe.get("size"), "msaa": probe.get("msaa"),
        "frames": len(ms),
        "p50_ms": round(pct(ms, 0.5), 2) if ms else None,
        "p99_ms": round(pct(ms, 0.99), 2) if ms else None,
        "max_ms": round(ms[-1], 2) if ms else None,
        # The frame's CPU share (the Render call, before the wait for the GPU).
        "cpu_p50_ms": probe.get("cpuP50Ms"), "cpu_p99_ms": probe.get("cpuP99Ms"),
        "points": [{"at": p["at"], "alive": p["alive"], "median_ms": p["medianMs"], "max_ms": p["maxMs"]}
                   for p in probe["points"]],
        **({"ablations": [ablation(x) for x in probe["ablations"]]} if "ablations" in probe else {}),
    }


def ablation(x: dict) -> dict:
    """One pass's ablation from the probe, its pairs (each frame without the pass beside the full frame
    drawn just before it) folded into their median saving, which a loaded machine's bursts move least."""
    out = {k: v for k, v in x.items() if k != "pairs"}
    if x.get("pairs"):
        out["pairedSavedMs"] = round(pct(sorted(full - without for full, without in x["pairs"]), 0.5), 2)
    return out


def report_line(name: str, r: dict) -> str:
    cpu, probe = r.get("cpu", {}), r.get("probe")
    parts = []
    if probe:
        parts.append(f"probe p50/p99/max {probe['p50_ms']}/{probe['p99_ms']}/{probe['max_ms']} ms"
                     + (f" (CPU p50/p99 {probe['cpu_p50_ms']}/{probe['cpu_p99_ms']})" if probe.get("cpu_p50_ms") is not None else ""))
        if probe.get("ablations"):
            parts.append("saved without (paired) " + ", ".join(
                f"{x['name']} {x.get('pairedSavedMs', x['savedMs'])}" for x in probe["ablations"]) + " ms")
    if cpu.get("frames"):
        f = cpu["frame_ms"]
        parts.append(f"main p50/p99/max {f['p50']}/{f['p99']}/{f['max']} ms over {cpu['frames']} frames "
                     f"(top {', '.join(cpu['top'])}), alloc {cpu['alloc_bytes']['mean']} B/frame, "
                     f"{cpu['gc_collections']} GCs, alive max {cpu['alive']['max']}")
    return f"perf: {name}: {'ok' if r['ok'] else 'FAILED'} in {r['wall_s']} s; " + "; ".join(parts)
