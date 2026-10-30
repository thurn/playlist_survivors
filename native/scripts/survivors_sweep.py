#!/usr/bin/env -S uv run --quiet --script
# /// script
# requires-python = ">=3.11"
# ///
"""Release survival reports. Local jobs enter background QoS; --release uses utility.

Normal damage: the positioning player on one seed in each of 3 playlist orders, plus
the base order on a continuously analysed source, which starts alongside the cached
runs. --assert-survival gates them. Pacing remains a reported diagnostic, secondary to
game feel (operator direction 2026-10-27); the AFK and random-pick bots are for
hand-run `ps survive` reports. A complete 30-minute matrix also records its base-order
run and replays the session exactly while the other runs finish.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
INPUTS = (
    "native/crates/sim",
    "native/crates/players",
    "native/crates/eval",
    "native/crates/stream",
    "native/crates/analysis",
    "native/crates/cli",
    "native/Cargo.toml",
    "native/Cargo.lock",
    "native/rust-toolchain.toml",
    "native/models",
    "native/playlists",
    "native/fixtures/survivors-3m.json",
    "native/scripts/survivors_sweep.py",
    "scripts/ci-full",
    "content",
    "levels",
    "public/reactive",
    "public/audio",
)


@dataclass(frozen=True)
class Case:
    mode: str
    order: str
    bot: str
    seed: int

    @property
    def name(self) -> str:
        return f"{self.mode}-{self.order}-{self.bot}-{self.seed}"


def cases() -> list[Case]:
    out = [
        Case("cached", order, "positioning", 1)
        for order in ("original", "reverse", "rotate")
    ]
    out.append(Case("continuous", "original", "positioning", 1))
    return out


def verdict(data: dict) -> bool | None:
    """Gate active-player comfort; minimum difficulty remains diagnostic."""
    if data["noHeroDamage"] or data["requestedMinutes"] != 30:
        return None
    r = data["report"]
    if data["bot"] == "positioning":
        return (
            not r["fallen"]
            and r["done"]
            and r["survivalS"] >= 1800
            and r["lowestHpShare"] > 0.25
        )
    return None


def summary(case: Case, data: dict) -> dict:
    r = data["report"]
    levels = {row["minute"]: row["level"] for row in r["levelsByMinute"]}
    return {
        "case": case.name,
        "survivalGate": verdict(data),
        "survivalS": r["survivalS"],
        "lowestHpShare": r["lowestHpShare"],
        "fallen": r["fallen"],
        "done": r["done"],
        "levelAt5": levels.get(5),
        "levelAt30": levels.get(30),
        "lastLevel": r["level"],
        "weaponsAtChests": r["weaponsAtChests"],
        "evolutions": r["evolutions"],
        "peakEnemies": r["peakEnemies"],
        "settledHash": r["settledHash"],
        "runWallSeconds": data["runWallSeconds"],
    }


# Cold analysis is bounded work whose cost swings with the machine's other load (a cold
# continuous source took 14 minutes alone and over 20 beside Unity; ps-vno), so preparation
# has its own bound and the 20-minute hang bound covers only the simulations and replay.
PREPARE_BUDGET = 45 * 60
RUN_BUDGET = 20 * 60


def checked(
    command: list[str], deadline: float, **kwargs
) -> subprocess.CompletedProcess:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError("survivors sweep exhausted its time budget")
    capture = not ("stdout" in kwargs or "stderr" in kwargs)
    return subprocess.run(
        command,
        check=True,
        capture_output=capture,
        text=True,
        timeout=remaining,
        **kwargs,
    )


def enter_qos(release: bool) -> None:
    qos = "utility" if release else "background"
    if sys.platform == "darwin" and os.environ.get("PS_SURVIVORS_CLASS") != qos:
        mode = ["-c", "utility"] if release else ["-b"]
        env = dict(os.environ, PS_NATIVE_BATCH="1", PS_SURVIVORS_CLASS=qos)
        os.execve(
            "/usr/sbin/taskpolicy",
            ["taskpolicy", *mode, sys.executable, *sys.argv],
            env,
        )
    os.environ["PS_NATIVE_BATCH"] = "1"


def input_key(assert_survival: bool) -> str | None:
    dirty = subprocess.run(
        ["git", "status", "--porcelain", "--untracked-files=all", "--", *INPUTS],
        cwd=ROOT,
        check=True,
        capture_output=True,
    ).stdout
    if dirty:
        return None
    tree = subprocess.run(
        ["git", "ls-tree", "-r", "HEAD", "--", *INPUTS],
        cwd=ROOT,
        check=True,
        capture_output=True,
    ).stdout
    return hashlib.sha256(
        tree + f"assert-survival={assert_survival}".encode()
    ).hexdigest()


def run_case(
    binary: Path,
    case: Case,
    out: Path,
    minutes: float,
    deadline: float,
    record: str | None = None,
    soak: bool = False,
    jobs: int = 1,
    name: str | None = None,
) -> dict:
    name = name or case.name
    command = [
        str(binary),
        "survive",
        "--bot",
        case.bot,
        "--seed",
        str(case.seed),
        "--order",
        case.order,
        "--minutes",
        str(minutes),
        "--jobs",
        str(jobs),
    ]
    if case.mode == "continuous":
        command.append("--concatenated")
    if soak:
        command.append("--runner-soak")
    if record:
        command += ["--record", str(out / record)]
    (out / f"{name}.command.json").write_text(json.dumps(command) + "\n")
    output = out / f"{name}.json"
    with output.open("w") as stdout, (out / f"{name}.stderr").open("w") as stderr:
        checked(command, deadline, cwd=ROOT / "native", stdout=stdout, stderr=stderr)
    data = json.loads(output.read_text())
    if (
        data["bot"] != case.bot
        or data["seed"] != case.seed
        or data["noHeroDamage"] != soak
        or data["playlistOrder"] != case.order
        or data["streamMode"]
        != ("concatenated" if case.mode == "continuous" else "cached-tracks")
    ):
        raise ValueError(f"wrong actor/seed/damage mode in {name}")
    print(
        f"{name}: {data['report']['survivalS']:.3f}s, HP {data['report']['lowestHpShare']:.3f}, "
        f"level {data['report']['level']}, survival gate {verdict(data)}",
        flush=True,
    )
    return data


def replay_session(binary: Path, session: Path, data: dict, deadline: float) -> dict:
    """`ps replay` of a recorded run must settle to the run's own records and hash."""
    runs = json.loads((session / "meta.json").read_text())["runs"]
    if len(runs) != 1 or runs[0].get("noHeroDamage", False) != data["noHeroDamage"]:
        raise ValueError("recording/report damage modes differ")
    report = data["report"]
    if (
        runs[0]["settledHash"] != report["settledHash"]
        or runs[0]["settledRecords"] != report["settledRecords"]
    ):
        raise ValueError("recording/report settled prefixes differ")
    start = time.monotonic()
    replay = checked(
        [str(binary), "replay", str(session)], deadline, cwd=ROOT / "native"
    )
    (session.parent / "replay.log").write_text(replay.stdout + replay.stderr)
    print(
        f"survivors replay: {report['settledRecords']} records, hash "
        f"{report['settledHash']}, the same",
        flush=True,
    )
    return {
        "session": str(session),
        "settledRecords": report["settledRecords"],
        "settledHash": report["settledHash"],
        "wallSeconds": time.monotonic() - start,
    }


def run(args: argparse.Namespace) -> None:
    enter_qos(args.release)
    start = time.monotonic()
    deadline = start + PREPARE_BUDGET
    selected = cases()
    if args.only is not None:
        selected = selected[: args.only]
    full = args.only is None and args.minutes == 30
    if args.assert_survival and not full:
        raise ValueError("survival assertions require the complete 30-minute matrix")
    key = input_key(args.assert_survival) if full else None
    # scripts/lib/platform.sh exports PS_CACHE under ci-full.
    home_cache = Path(
        os.environ.get("PS_CACHE") or Path.home() / "Library/Caches/playlist-survivors"
    )
    stamp = home_cache / "ci-stamps" / f"survivors-{key}"
    if (
        key
        and stamp.is_file()
        and (Path(stamp.read_text().strip()) / "summary.json").is_file()
        and not args.force
    ):
        print(
            f"survivors sweep: unchanged inputs {key}, passed at {stamp.read_text().strip()}"
        )
        return
    out = args.out or home_cache / "survivors" / f"{key or 'dirty'}-{time.time_ns()}"
    out.mkdir(parents=True, exist_ok=False)
    # Respect the worktree-specific Cargo target. Never borrow another checkout's target.
    built = checked(
        ["cargo", "build", "--locked", "--release", "-p", "cli"],
        deadline,
        cwd=ROOT / "native",
    )
    (out / "build.log").write_text(built.stdout + built.stderr)
    target = Path(os.environ.get("CARGO_TARGET_DIR", ROOT / "native/target"))
    if not target.is_absolute():
        target = ROOT / "native" / target
    binary = target / "release/ps"
    results = {}
    continuous = next((case for case in selected if case.mode == "continuous"), None)
    cached = [case for case in selected if case.mode == "cached"]
    normal = Case("cached", "original", "positioning", 1)
    # Preparation: the per-track caches (a short warmup run) and, alongside them, the
    # continuous source's full analysis (a short continuous run). Warm, both are cache reads.
    replay = None
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        prepared = None
        if continuous and args.jobs > 1:
            prepared = pool.submit(
                run_case,
                binary,
                continuous,
                out,
                0.01,
                deadline,
                name=f"{continuous.name}-prepare",
            )
        warmup = run_case(
            binary,
            Case("cached", "original", "afk", 0),
            out,
            0.01,
            deadline,
            jobs=max(1, args.jobs - bool(prepared)),
        )
        (out / "warmup.json").write_text(json.dumps(warmup, indent=2) + "\n")
        if prepared:
            prepared.result()
        elif continuous:
            run_case(
                binary,
                continuous,
                out,
                0.01,
                deadline,
                name=f"{continuous.name}-prepare",
            )
        print(
            f"survivors sweep: prepared in {time.monotonic() - start:.3f}s", flush=True
        )
        deadline = time.monotonic() + RUN_BUDGET
        pending = {}
        if continuous:
            future = pool.submit(
                run_case, binary, continuous, out, args.minutes, deadline
            )
            pending[future] = continuous
        for case in cached:
            future = pool.submit(
                run_case,
                binary,
                case,
                out,
                args.minutes,
                deadline,
                "normal-session" if full and case == normal else None,
            )
            pending[future] = case
        # Iterate rounds so the replay, submitted once its run is done, is also joined.
        while pending:
            future = next(as_completed(pending))
            case = pending.pop(future)
            if case is None:
                replay = future.result()
                continue
            results[case] = future.result()
            if full and case == normal and complete_run(results[case]):
                following = pool.submit(
                    replay_session,
                    binary,
                    out / "normal-session",
                    results[case],
                    deadline,
                )
                pending[following] = None
    rows = [summary(case, results[case]) for case in selected]
    report = {
        "assertSurvival": args.assert_survival,
        "pacingReportOnly": True,
        "wallSeconds": time.monotonic() - start,
        "cases": rows,
        "replay": replay,
    }
    (out / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    failures = [row["case"] for row in rows if row["survivalGate"] is False]
    print(
        f"survivors sweep: {len(rows)} reports, {len(failures)} survival failures "
        f"({'asserting' if args.assert_survival else 'report-only'}), "
        f"{report['wallSeconds']:.3f}s; {out}",
        flush=True,
    )
    if args.assert_survival and failures:
        raise ValueError("survival gates failed: " + ", ".join(failures))
    if key and input_key(args.assert_survival) == key:
        stamp.parent.mkdir(parents=True, exist_ok=True)
        stamp.write_text(str(out) + "\n")


def complete_run(data: dict) -> bool:
    r = data["report"]
    return r["done"] and not r["fallen"] and r["survivalS"] >= 1800


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jobs", type=int, choices=range(1, 17), default=6)
    parser.add_argument("--only", type=int, choices=range(1, len(cases()) + 1))
    parser.add_argument("--minutes", type=float, default=30)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--release", action="store_true")
    parser.add_argument("--assert-survival", action="store_true")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    if not 0 < args.minutes <= 30:
        parser.error("minutes must be positive and <= 30")
    try:
        run(args)
    except (OSError, ValueError, subprocess.SubprocessError, TimeoutError) as error:
        print(f"survivors sweep: {error}", file=sys.stderr)
        raise SystemExit(1) from error


if __name__ == "__main__":
    main()
