"""`game sweep`: the bots' 30-minute runs in .NET (docs/archive/design/unity-game.md, Tools, Checks and
Evidence: bots and sweeps), on the pattern of native's survivors sweep (native/scripts/survivors_sweep.py).

The positioning bot plays four runs on seed 1, as native's sweep does: the survivors test playlist
(native/playlists/survivors-30.m3u) in its original, reversed and rotated orders over each track's
own entry dump, laid end to end with the playlist's gaps (native's cached tracks), and in its
original order over one dump of the whole playlist (native's continuous source). The idle bot plays
the original order's cached tracks once. Each run's stream is its playlist played twice, the second
time after a 2 s gap: the run's freezes (each level-up's cards, each chest's roulette, the music
stopping at track changes and holding until the next track's beat turns reliable, 56 s into the
piano aria) take 250 to 550 s of song time, so one pass of the 2294 s playlist can end before the run clock's
30 minutes, and the gates are the bots', not the playlist's length. The dumps are native's `ps entries`, cached by every
analyzer input; the runs are game/dotnet/PS.Game.Sweep, all five in parallel.

`--assert` gates them: each positioning run reaches its victory card with its health above 25% all
the way, and the idle bot falls before minute 10. The rest is reported, never asserted: the pacing
(the level every five minutes against progression.json's `startLevels`, and whether each is within
20% of it), each chest's drop and take, and the late crowd's mix (from minute 20 on, the largest
share one role holds of 250 or more alive, and the crystal colours at each minute's peak). A pass
with every input committed writes a stamp keyed by their hash, and a later sweep over the same
inputs skips (`--force` runs anyway), as slow CI's parity and survivors sections do. `--evidence` writes the summary to game/evidence/sweeps/summary.json.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

from . import paths

PLAYLIST = "native/playlists/survivors-30.m3u"
SWEEP = "game/dotnet/PS.Game.Sweep"
# What the runs depend on: the sim and the bots, the content and arena, the sweep, the dumps'
# analyzer and audio, and this file.
INPUTS = (
    "game/Assets/Game/Sim",
    "game/Content",
    "game/dotnet/Directory.Build.props",
    "game/dotnet/PS.Game.Sim",
    SWEEP,
    "demo3d/tools/src/demo3d_tools/botsweep.py",
    "native/crates/analysis",
    "native/crates/cli",
    "native/crates/stream",
    "native/models",
    "native/Cargo.toml",
    "native/Cargo.lock",
    "native/rust-toolchain.toml",
    "native/playlists",
    "public/reactive",
)
# Cold dumps are bounded analysis whose cost swings with the machine's load; the runs have their own
# hang bound.
PREPARE_BUDGET = 45 * 60
RUN_BUDGET = 15 * 60
HEALTH_FLOOR = 0.25
IDLE_FALLS_BY = 600
# The pacing's band round progression.json's startLevels, and the late crowd's minutes and largest share of one role.
PACING_BAND = 0.2
LATE_CROWD = range(20, 31)
CROWD_SHARE = 0.5


@dataclass(frozen=True)
class Track:
    path: Path
    gap: float


@dataclass(frozen=True)
class Case:
    mode: str  # "cached": a dump per track; "continuous": one dump of the playlist
    order: str
    bot: str
    seed: int = 1

    @property
    def name(self) -> str:
        return f"{self.mode}-{self.order}-{self.bot}-{self.seed}"


CASES = (
    Case("cached", "original", "positioning"),
    Case("cached", "reverse", "positioning"),
    Case("cached", "rotate", "positioning"),
    Case("continuous", "original", "positioning"),
    Case("cached", "original", "idle"),
)


def parse_playlist(text: str, base: Path) -> list[Track]:
    """Native's M3U (stream/src/playlist.rs): paths relative to the list, `#PS-GAP:S` the silence
    before the next track, 2 s by default and none before the first."""
    tracks: list[Track] = []
    gap = None
    for raw in text.lstrip("﻿").splitlines():
        line = raw.strip()
        if line.startswith("#PS-GAP:"):
            gap = float(line.removeprefix("#PS-GAP:"))
        elif line and not line.startswith("#"):
            tracks.append(Track((base / line).resolve(), gap if gap is not None else (2.0 if tracks else 0.0)))
            gap = None
    return tracks


def ordered(tracks: list[Track], order: str) -> list[Track]:
    """Native's playlist orders (cli/src/survive.rs): the tracks reversed or rotated left by a third,
    the gaps staying where they were."""
    paths_ = [t.path for t in tracks]
    if order == "reverse":
        paths_.reverse()
    elif order == "rotate":
        n = len(paths_) // 3
        paths_ = paths_[n:] + paths_[:n]
    elif order != "original":
        raise ValueError(f"unknown order {order}")
    return [Track(p, t.gap) for p, t in zip(paths_, tracks, strict=True)]


def verdict(case: Case, report: dict) -> bool:
    if case.bot == "positioning":
        return report["victory"] and not report["fallen"] and report["runClock"] >= 1800 and report["lowestHealthShare"] > HEALTH_FLOOR
    return report["fallen"] and report["runClock"] < IDLE_FALLS_BY


def pacing(levels: list[int], start_levels: list[int]) -> list[dict]:
    """The level every five minutes against the design's (progression.json's startLevels, from minute 0)."""
    rows = []
    for k, design in enumerate(start_levels[1:], 1):
        minute = 5 * k
        level = levels[minute] if minute < len(levels) else None
        rows.append({"minute": minute, "level": level, "design": design,
                     "within": level is not None and abs(level - design) <= PACING_BAND * design})
    return rows


def late_crowd(crowd: list[dict]) -> dict:
    """The late minutes' crowd: by minute, its peak, the largest share one role held of 250 or more alive, and the
    crystal colours at the peak; whether no role held more than half, and the fewest colours at a minute's peak
    (minute 30, the finale's instant, left out)."""
    minutes = [{"minute": m, "peak": crowd[m]["peak"], "topShare": crowd[m]["topShare"], "topRole": crowd[m].get("topRole"),
                "colours": crowd[m]["coloursAtPeak"]} for m in LATE_CROWD if m < len(crowd)]
    return {
        "minutes": minutes,
        "mixed": all(m["topShare"] <= CROWD_SHARE for m in minutes),
        "fewestColours": min((m["colours"] for m in minutes if m["minute"] < LATE_CROWD[-1]), default=None),
    }


def summary(case: Case, report: dict, start_levels: list[int]) -> dict:
    levels = report["levelByMinute"]
    return {
        "case": case.name,
        "passed": verdict(case, report),
        "runClock": round(report["runClock"], 3),
        "song": round(report["song"], 3),
        "startedAt": round(report["startedAt"], 3),
        "frozen": report["frozen"],
        "victory": report["victory"],
        "fallen": report["fallen"],
        "lowestHealthShare": round(report["lowestHealthShare"], 4),
        "heroHits": report["heroHits"],
        "levelAt5": levels[5] if len(levels) > 5 else None,
        "level": report["level"],
        "pacing": pacing(levels, start_levels) if case.bot == "positioning" else None,
        "chests": [{"minute": c["minute"], "dropped": c.get("droppedAt"), "taken": c["runClock"]} for c in report["chests"]],
        "lateCrowd": late_crowd(report["crowd"]) if case.bot == "positioning" else None,
        "evolutions": report["evolutions"],
        "peakEnemies": report["peakEnemies"],
        "kills": report["kills"],
        "hash": report["hash"],
        "wallSeconds": round(report["wallSeconds"], 1),
    }


def input_key(root: Path) -> str | None:
    """The inputs' hash, or None while any of them has uncommitted changes."""
    dirty = subprocess.run(["git", "status", "--porcelain", "--untracked-files=all", "--", *INPUTS],
                           cwd=root, check=True, capture_output=True).stdout
    if dirty:
        return None
    tree = subprocess.run(["git", "ls-tree", "-r", "HEAD", "--", *INPUTS], cwd=root, check=True, capture_output=True).stdout
    return hashlib.sha256(tree).hexdigest()


def cache_dir() -> Path:
    # scripts/lib/platform.sh exports PS_CACHE under ci-full.
    return Path(os.environ.get("PS_CACHE") or Path.home() / "Library/Caches/playlist-survivors")


def dump(root: Path, source: Path, deadline: float) -> Path:
    """The cached entry dump of an audio file or playlist (native's `ps entries`)."""
    out = subprocess.run(["cargo", "run", "--locked", "--release", "-q", "-p", "cli", "--", "entries", str(source)],
                         cwd=root / "native", check=True, capture_output=True, text=True,
                         timeout=max(1.0, deadline - time.monotonic()))
    return Path(json.loads(out.stdout.strip().splitlines()[-1])["cache"])


def run_case(dll: Path, root: Path, case: Case, tracks: list[tuple[Path, float]], out: Path, deadline: float) -> dict:
    report = out / f"{case.name}.json"
    # Through `dotnet`: the apphost finds no runtime installed outside its default places (Homebrew's).
    cmd = ["dotnet", str(dll), "--bot", case.bot, "--seed", str(case.seed), "--minutes", "30", "--out", str(report),
           "--content", str(root / "game" / "Content")]
    for path, gap in tracks:
        cmd += ["--track", str(path), "--gap", f"{gap:g}"]
    (out / f"{case.name}.command.json").write_text(json.dumps(cmd) + "\n")
    with (out / f"{case.name}.log").open("w") as log:
        subprocess.run(cmd, check=True, stdout=log, stderr=subprocess.STDOUT, timeout=max(1.0, deadline - time.monotonic()))
    return json.loads(report.read_text())


def add_parser(sub: argparse._SubParsersAction) -> None:
    s = sub.add_parser("sweep", help="the bots' 30-minute runs in .NET over entry dumps of the survivors playlist")
    s.add_argument("--assert", dest="check", action="store_true",
                   help="fail unless the positioning runs win above 25%% health and the idle bot falls before minute 10")
    s.add_argument("--force", action="store_true", help="run even when these inputs passed before")
    s.add_argument("--evidence", action="store_true", help="write the summary to game/evidence/sweeps/summary.json")
    s.set_defaults(func=cmd_sweep)


def cmd_sweep(a: argparse.Namespace) -> int:
    if sys.platform != "darwin":
        print("sweep: skipped, its entry dumps come from the native workspace, which builds only on macOS")
        return 0
    root = paths.worktree(name="game").root
    key = input_key(root)
    stamp = cache_dir() / "ci-stamps" / f"game-sweep-{key}"
    if key and a.check and not a.force and stamp.is_file():
        print(f"sweep: unchanged inputs {key[:12]}, passed at {stamp.read_text().strip()}; skipping")
        return 0
    try:
        return _sweep(root, a, key, stamp)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"sweep: {error}", file=sys.stderr)
        if isinstance(error, subprocess.CalledProcessError) and error.stderr:
            print(error.stderr[-4000:], file=sys.stderr)
        return 1


def _sweep(root: Path, a: argparse.Namespace, key: str | None, stamp: Path) -> int:
    started = time.monotonic()
    deadline = started + PREPARE_BUDGET
    playlist = root / PLAYLIST
    tracks = parse_playlist(playlist.read_text(), playlist.parent)
    out = root / "captures" / "game" / "sweeps" / time.strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True)
    unique = sorted({t.path for t in tracks})
    with ThreadPoolExecutor(max_workers=len(unique) + 2) as pool:
        build = pool.submit(subprocess.run, ["dotnet", "build", str(root / SWEEP), "-c", "Release", "--nologo", "-v", "q"],
                            check=True, capture_output=True, text=True)
        whole = pool.submit(dump, root, playlist, deadline)
        dumps = dict(zip(unique, pool.map(lambda p: dump(root, p, deadline), unique), strict=True))
        whole_dump = whole.result()
        build.result()
    print(f"sweep: {len(unique)} track dumps and the playlist's in {time.monotonic() - started:.1f} s", flush=True)
    dll = root / SWEEP / "bin" / "Release" / "net10.0" / "ps-game-sweep.dll"
    deadline = time.monotonic() + RUN_BUDGET

    def timeline(case: Case) -> list[tuple[Path, float]]:
        if case.mode == "continuous":
            once = [(whole_dump, 0.0)]
        else:
            once = [(dumps[t.path], t.gap) for t in ordered(tracks, case.order)]
        # Played twice, the second time after the default 2 s gap.
        return once + [(once[0][0], 2.0)] + once[1:]

    with ThreadPoolExecutor(max_workers=len(CASES)) as pool:
        reports = list(pool.map(lambda c: run_case(dll, root, c, timeline(c), out, deadline), CASES))
    start_levels = json.loads((root / "game" / "Content" / "progression.json").read_text())["startLevels"]
    rows = [summary(case, report, start_levels) for case, report in zip(CASES, reports, strict=True)]
    result = {"asserted": a.check, "pacingReportOnly": True, "wallSeconds": round(time.monotonic() - started, 1), "cases": rows}
    (out / "summary.json").write_text(json.dumps(result, indent=2) + "\n")
    for r in rows:
        print(f"  {r['case']}: run clock {r['runClock']:.1f} s at song {r['song']:.1f} s, "
              f"{'victory' if r['victory'] else 'fell' if r['fallen'] else 'stopped'}, lowest health {r['lowestHealthShare']:.3f}, "
              f"level {r['level']} (minute 5: {r['levelAt5']}), {len(r['chests'])} chests, {r['wallSeconds']} s: "
              f"{'pass' if r['passed'] else 'FAIL'}")
        if r["lateCrowd"]:
            levels = " ".join(f"{p['minute']}:{p['level']}/{p['design']}{'' if p['within'] else '!'}" for p in r["pacing"])
            crowd = r["lateCrowd"]
            share = max(m["topShare"] for m in crowd["minutes"])
            print(f"    levels by minute (design) {levels}; late crowd's largest role share {share:.2f}"
                  f"{'' if crowd['mixed'] else ' (over half)'}, fewest colours {crowd['fewestColours']}")
    if a.evidence:
        evidence = root / "game" / "evidence" / "sweeps"
        evidence.mkdir(parents=True, exist_ok=True)
        (evidence / "summary.json").write_text(json.dumps(result, indent=2) + "\n")
    failures = [r["case"] for r in rows if not r["passed"]]
    print(f"sweep: {len(rows)} runs, {len(failures)} failing{' (asserting)' if a.check else ''}, "
          f"{result['wallSeconds']} s; {out.relative_to(root)}")
    if a.check and failures:
        print("sweep: failed: " + ", ".join(failures), file=sys.stderr)
        return 1
    if a.check and key and input_key(root) == key:
        stamp.parent.mkdir(parents=True, exist_ok=True)
        stamp.write_text(f"{out}\n")
    return 0
