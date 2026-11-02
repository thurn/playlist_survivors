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
- `full`: the 30-minute fixed-time File run with the bot, for the CPU profile (about 4 minutes);
- `firsts`: the same run with the probe at each kind of moment's first sighting (`--gpu-probe-firsts`: the
  first heavy strike, each role's first death, each weapon's first hit, the first break, chest and level-up,
  each zone's first change...), its `first_ms` the frame that first draws it: a first-use hitch's cost;
- `firsts-rest`: the same probe at minute 25 on what that run never shows: Unholy Vespers, Thunder Loop, Chain
  Lightning and the fire trail, and the stampede, kick line and wedge.

Each scenario's CSV and probe JSON and the player's log stay in the gitignored captures/game/perf/NAME/;
the summary (per scenario: the probe's frame p50/p99/max, the main thread's p50/p99/max by part over
the scenario's window of song time, the main thread's CPU time, each frame over 16.7 ms with its CPU
time and top part, allocations and collections, and the sim's step p99 at 285 or more alive) goes to
captures/game/perf/summary.json, and with `--evidence DIR` beside the probes' JSON into
game/evidence/DIR/. It exits 1 if a player fails (a non-zero exit, an exception in its log, or a missing
profile or probe point). With `--ablate` the probe also draws each counted frame's neighbours with one
pass turned off (`--gpu-probe-ablate`), and the summary has each pass's saving. `--trace` builds and
runs a development player (game/Builds/mac/trace; Unity traces only in one) that traces the graphics
states it draws into the build's warm-up (`--graphics-states-out`, game/Assets/StreamingAssets/GraphicsStates/,
gitignored; its captures go to captures/game/perf-trace/): the next `game build` ships them, and its
player warms them up at boot, so no first sighting compiles a shader mid-run. `--no-warmup` measures
without the warm-up. `--player-args` adds flags to every scenario's player (a look preset: `--look hologram`).

`--looks edge,solid,hologram,prism` runs the scenarios once per look preset (`--look NAME` after `--player-args`), each
look's captures under captures/game/perf/NAME/ and its summary and probes in game/evidence/DIR/NAME/, with `--trace`
tracing every look into the one warm-up (a trace adds to the file's states). Every summary has the budget's verdict
(unity-game.md, Performance targets: no frame over 33 ms on the Mac): `over_budget` lists each counted probe frame, each
first sighting's first frame and each profiled frame of the main thread over it whose own work (its CPU time) is too (not
a timed point's warm-up frame, which re-creates the hand-drawn frame's pooled targets: 18-34 ms at every point, the run's
quiet first minute too), and `--budget` exits 1 when one is. A main-thread frame over it on little CPU time waited on the
machine (other players, builds), not on its own work (ps-msz.8.9): it goes to `over_budget_waits`.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import shlex
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
# What the full run's bot never reaches (ps-msz.8.9): the other two evolutions and the last two weapons, and the set
# pieces its songs never cue.
REST_ARSENAL = "sword:8,unholy-vespers:1,thunder-loop:1,chain-lightning:8,fire-trail:8"
REST_SET_PIECES = "stampede@8,kick@16,wedge@24"
# The sim's step is measured against the target at 300 alive: frames this close to the cap count.
PEAK_ALIVE = 285
# With --ablate a probe point draws GpuProbe.AblateRounds rounds of its ten frame kinds (250 frames,
# 4.2 s of song at fixed time): its points are spaced this far apart and the run goes on this long after.
ABLATE_S = 4.5
# The traced graphics states the player warms up at boot (Client/GraphicsStates.cs), in the gitignored
# StreamingAssets the build copies: per machine, made by `game perf --trace`.
TRACE = "Assets/StreamingAssets/GraphicsStates/Metal.graphicsstate"
TRACE_PLAYER = "Builds/mac/trace"
# The columns that aren't parts of the frame's work.
NOT_PARTS = {"frame_ms", "wait_ms", "probe_ms", "cpu_ms"}
# The budget: no frame over 33 ms on the Mac (unity-game.md, Performance targets).
BUDGET_MS = 1000 / 30


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
    # The probe at each first sighting too (`--gpu-probe-firsts`).
    firsts: bool = False


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
    Scenario("firsts", "the 30-minute run with the probe at each first sighting", ["--run", "--bot", "positioning"],
             [], (0, math.inf), None, file=PLAYLIST, firsts=True),
    Scenario("firsts-rest", "the weapons and set pieces the full run never shows, with the probe at each first sighting",
             [*PEAK, "--arsenal", REST_ARSENAL, "--debug", REST_SET_PIECES], [], (0, math.inf), 45, firsts=True),
]


def add_parser(sub: argparse._SubParsersAction) -> None:
    p = sub.add_parser("perf", help="the run's frame probe and CPU profile over the standard scenarios (Mac, windowless)")
    p.add_argument("--only", help=f"comma-separated scenarios (default all: {', '.join(s.name for s in SCENARIOS)})")
    p.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    p.add_argument("--build", action="store_true", help="rebuild the player first (after a change)")
    p.add_argument("--evidence", help="also write the summary and the probes' JSON to game/evidence/DIR")
    p.add_argument("--ablate", action="store_true",
                   help="also time the probe's frames with each pass turned off (SSAO, post, shadows, ...)")
    p.add_argument("--trace", action="store_true",
                   help=f"trace the graphics states each scenario draws into the build's warm-up ({TRACE}), "
                        "in a development player of its own; `game build` after ships them")
    p.add_argument("--no-warmup", action="store_true", help="the player doesn't warm up the build's traced graphics states")
    p.add_argument("--player-args", default="", help="extra player flags for every scenario, e.g. '--look hologram'")
    p.add_argument("--looks", help="comma-separated look presets: the scenarios once under each (`--look NAME`)")
    p.add_argument("--budget", action="store_true", help=f"exit 1 when a frame goes over the budget ({BUDGET_MS:.1f} ms)")
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

    if a.trace and a.evidence:
        print("perf: --trace runs a development player, whose numbers aren't evidence", file=sys.stderr)
        return 2
    # Unity traces graphics states only in a development build: --trace has its own player, and its own captures.
    out = TRACE_PLAYER if a.trace else None
    exe = setup.player_dir(wt, "mac", a.backend, out) / APP_EXE
    if a.build or a.trace or not exe.exists():
        from . import cli

        code = cli.cmd_build(argparse.Namespace(
            project="game", platform="mac", backend=a.backend, out=out, development=a.trace,
            scene=None, cold=False, force=False))
        if code != 0:
            return code
    base = wt.root / "captures" / "game" / ("perf-trace" if a.trace else "perf")
    looks = a.looks.split(",") if a.looks else [None]
    failed, over, waited = [], [], []
    for look in looks:
        root = base / look if look else base
        summary = {}
        for scenario in (s for s in SCENARIOS if s.name in wanted):
            extra = ((["--graphics-states-out", str(wt.project / TRACE)] if a.trace else []) + (["--no-warmup"] if a.no_warmup else [])
                     + shlex.split(a.player_args) + (["--look", look] if look else []))
            result = run(exe, wt.root, root / scenario.name, scenario, extra, ablate=a.ablate)
            summary[scenario.name] = result
            name = f"{look}/{scenario.name}" if look else scenario.name
            if not result["ok"]:
                failed.append(name)
            if result["over_budget"]:
                over.append(name)
            if result["over_budget_waits"]:
                waited.append(name)
            print(report_line(name, result))
        root.mkdir(parents=True, exist_ok=True)
        text = json.dumps(summary, indent=2) + "\n"
        (root / "summary.json").write_text(text, encoding="utf-8")
        if a.evidence:
            evidence = wt.project / "evidence" / a.evidence / (look or "")
            evidence.mkdir(parents=True, exist_ok=True)
            (evidence / "summary.json").write_text(text, encoding="utf-8")
            for name in summary:
                probe = root / name / "gpu-probe.json"
                if probe.exists():
                    shutil.copy(probe, evidence / f"gpu-probe-{name}.json")
        print(f"perf: summary in {(root / 'summary.json').relative_to(wt.root)}")
    if over:
        print(f"perf: over the budget ({BUDGET_MS:.1f} ms): {', '.join(over)}", file=sys.stderr)
    if waited:
        print(f"perf: frames over the budget on little CPU time (the machine's waits): {', '.join(waited)}", file=sys.stderr)
    if failed:
        print(f"perf: failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 1 if a.budget and over else 0


def run(exe: Path, repo: Path, out: Path, s: Scenario, extra: list[str] | None = None, ablate: bool = False) -> dict:
    """One scenario in the player, at fixed time in batch mode; its summary, `ok` false on a failure."""
    from .spike import exceptions

    shutil.rmtree(out, ignore_errors=True)
    out.mkdir(parents=True)
    log, profile, probe = out / "player.log", out / "profile.csv", out / "gpu-probe.json"
    cmd = [str(exe), "-batchmode", "-force-metal", "-logFile", str(log), "--fixed-time",
           "--file", str((repo / s.file).resolve()), "--profile-out", str(profile), *s.args, *(extra or [])]
    points, seconds = s.probe, s.seconds
    if ablate and points:
        points = ablate_points(points)
        seconds = max(seconds, points[-1] + ABLATE_S) if seconds is not None else None
    if points:
        cmd += ["--gpu-probe-at", ",".join(f"{t:g}" for t in points)]
        if ablate:
            cmd.append("--gpu-probe-ablate")
    if s.firsts:
        cmd += ["--gpu-probe-firsts"]
    if points or s.firsts:
        cmd += ["--gpu-probe-out", str(probe)]
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
    if s.probe or s.firsts:
        if probe.exists():
            result["probe"] = probe_summary(json.loads(probe.read_text()))
            timed = sum(1 for p in result["probe"]["points"] if not p.get("first"))
            if timed != len(points):
                errors.append(f"{timed} of {len(points)} probe points")
        else:
            errors.append("no probe")
    result["over_budget"] = over_budget(result)
    result["over_budget_waits"] = over_budget(result, waits=True)
    result["ok"] = not errors
    if errors:
        result["errors"] = errors
        for e in errors:
            print(f"perf: {s.name}: {e} (log {log})", file=sys.stderr)
    return result


def over_budget(result: dict, waits: bool = False) -> list[dict]:
    """The scenario's frames over the budget: each probe point's slowest counted frame, a first sighting's first frame
    (what a first use costs; a timed point's first is the probe's warm-up), and each profiled main-thread frame whose CPU
    time is over it too; with `waits`, the main-thread frames over it on less CPU time instead (the machine's, not the
    frame's own work)."""
    slow = [s for s in result.get("cpu", {}).get("slow", []) if s["frame_ms"] > BUDGET_MS]
    if waits:
        return [{"main": s["song"], "frame_ms": s["frame_ms"], "cpu_ms": s["cpu_ms"]}
                for s in slow if s.get("cpu_ms", BUDGET_MS + 1) <= BUDGET_MS]
    over = []
    for p in result.get("probe", {}).get("points", []):
        for key in ("max_ms", "first_ms") if p.get("first") else ("max_ms",):
            if p.get(key) is not None and p[key] > BUDGET_MS:
                over.append({"probe": p.get("label", f"at {p['at']:g}"), key: p[key]})
    over += [{"main": s["song"], "frame_ms": s["frame_ms"], **({"cpu_ms": s["cpu_ms"]} if "cpu_ms" in s else {})}
             for s in slow if s.get("cpu_ms", BUDGET_MS + 1) > BUDGET_MS]
    return over


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
    return {"p50": round(pct(v, 0.5), 3), "p99": round(pct(v, 0.99), 3), "p99.9": round(pct(v, 0.999), 3),
            "max": round(v[-1], 3), "mean": round(sum(v) / len(v), 3)}


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
    cpu = [r["cpu_ms"] for r in frames if r.get("cpu_ms", -1) >= 0]
    alive = sorted(r["alive"] for r in frames)
    return {
        "frames": len(frames),
        "song": [round(frames[0]["frame_song"], 3), round(frames[-1]["frame_song"], 3)],
        "alive": {"median": int(pct(alive, 0.5)), "max": int(alive[-1])},
        "frame_ms": stats(frame_ms),
        "over_16_7ms": sum(ms > 1000 / 60 for ms in frame_ms),
        "over_33ms": sum(ms > 1000 / 30 for ms in frame_ms),
        # The main thread's CPU time (where the player could read it): a slow frame with little is the OS's, not the frame's.
        **({"cpu_ms": stats(cpu)} if cpu else {}),
        "slow": [{"song": round(r["frame_song"], 3), "frame_ms": round(r["frame_ms"], 2),
                  **({"cpu_ms": round(r["cpu_ms"], 2)} if r.get("cpu_ms", -1) >= 0 else {}),
                  "top": max(parts, key=lambda p: r[f"{p}_ms"]), "alloc_bytes": int(r["alloc_bytes"])}
                 for r in frames if r["frame_ms"] > 1000 / 60],
        "parts_ms": by_part,
        "top": sorted(parts, key=lambda p: by_part[p]["mean"], reverse=True)[:3],
        "wait_s": round(sum(r["wait_ms"] for r in frames) / 1000, 1),
        "alloc_bytes": {"mean": round(sum(alloc) / len(alloc)), "p99": int(pct(alloc, 0.99)), "max": int(alloc[-1]),
                        "frames": sum(b > 0 for b in alloc)},
        "gc_collections": int(sum(r["gc"] for r in frames)),
        "sim_at_peak": {"alive_from": PEAK_ALIVE, "frames": len(peak), "p99_ms": round(pct(peak, 0.99), 3) if peak else None},
    }


def probe_summary(probe: dict) -> dict:
    """The frame probe's counted frames over every point: p50, p99 and max in ms, and each point's, with its first
    (warm-up) frame's; a first sighting's point (`--gpu-probe-firsts`) has its label and `first` true."""
    ms = sorted(f[2] for p in probe["points"] for f in p["frames"])
    firsts = [p.get("firstMs") for p in probe["points"] if p.get("firstMs") is not None]
    return {
        "device": probe.get("graphicsDeviceName"), "size": probe.get("size"), "msaa": probe.get("msaa"),
        "frames": len(ms),
        "p50_ms": round(pct(ms, 0.5), 2) if ms else None,
        "p99_ms": round(pct(ms, 0.99), 2) if ms else None,
        "max_ms": round(ms[-1], 2) if ms else None,
        # The frame's CPU share (the Render call, before the wait for the GPU).
        "cpu_p50_ms": probe.get("cpuP50Ms"), "cpu_p99_ms": probe.get("cpuP99Ms"),
        "first_max_ms": round(max(firsts), 2) if firsts else None,
        "points": [{"at": p["at"], "alive": p["alive"], "median_ms": p["medianMs"], "max_ms": p["maxMs"],
                    **({"first_ms": p["firstMs"]} if "firstMs" in p else {}),
                    **({"label": p["label"], "first": not p["label"].startswith("at ")} if "label" in p else {})}
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
                     + (f" (CPU p50/p99 {probe['cpu_p50_ms']}/{probe['cpu_p99_ms']})" if probe.get("cpu_p50_ms") is not None else "")
                     + f", first frames max {probe['first_max_ms']} ms over {len(probe['points'])} points")
        if probe.get("ablations"):
            parts.append("saved without (paired) " + ", ".join(
                f"{x['name']} {x.get('pairedSavedMs', x['savedMs'])}" for x in probe["ablations"]) + " ms")
    if cpu.get("frames"):
        f = cpu["frame_ms"]
        parts.append(f"main p50/p99/max {f['p50']}/{f['p99']}/{f['max']} ms over {cpu['frames']} frames "
                     f"(top {', '.join(cpu['top'])}), alloc {cpu['alloc_bytes']['mean']} B/frame, "
                     f"{cpu['gc_collections']} GCs, alive max {cpu['alive']['max']}")
    return f"perf: {name}: {'ok' if r['ok'] else 'FAILED'} in {r['wall_s']} s; " + "; ".join(parts)
