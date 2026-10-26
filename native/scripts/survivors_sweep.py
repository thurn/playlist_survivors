#!/usr/bin/env -S uv run --quiet --script
# /// script
# requires-python = ">=3.11"
# ///
"""Release survival reports. Local jobs enter background QoS; --release uses utility.

Normal damage: 5 seeds x 3 orders x positioning/AFK, plus one random-pick report.
The base-order continuous-analysis lane runs 5 seeds x positioning/AFK. It starts
alongside the cached lane, then reuses a full-source cache for its remaining cases.
Only SW7 enables --assert-survival. Pacing is always reported, never asserted.
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
        Case("cached", order, bot, seed)
        for order in ("original", "reverse", "rotate")
        for seed in range(1, 6)
        for bot in ("positioning", "afk")
    ]
    out.append(Case("cached", "original", "random-pick", 1))
    out += [
        Case("continuous", "original", bot, seed)
        for seed in range(1, 6)
        for bot in ("positioning", "afk")
    ]
    return out


def verdict(data: dict) -> bool | None:
    """Only survival thresholds; random-pick/diagnostic/short runs have no verdict."""
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
    if data["bot"] == "afk":
        return r["fallen"] and r["survivalS"] < 600
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


def checked(
    command: list[str], deadline: float, **kwargs
) -> subprocess.CompletedProcess:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError("survivors sweep exhausted its 20-minute budget")
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
) -> dict:
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
    (out / f"{case.name}.command.json").write_text(json.dumps(command) + "\n")
    output = out / f"{case.name}.json"
    with output.open("w") as stdout, (out / f"{case.name}.stderr").open("w") as stderr:
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
        raise ValueError(f"wrong actor/seed/damage mode in {case.name}")
    print(
        f"{case.name}: {data['report']['survivalS']:.3f}s, HP {data['report']['lowestHpShare']:.3f}, "
        f"level {data['report']['level']}, survival gate {verdict(data)}",
        flush=True,
    )
    return data


def run(args: argparse.Namespace) -> None:
    enter_qos(args.release)
    start = time.monotonic()
    deadline = start + 20 * 60
    selected = cases()
    if args.only is not None:
        selected = selected[: args.only]
    full = args.only is None and args.minutes == 30
    if args.assert_survival and not full:
        raise ValueError("survival assertions require the complete 30-minute matrix")
    key = input_key(args.assert_survival) if full else None
    home_cache = Path.home() / "Library/Caches/playlist-survivors"
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
    first_continuous = next(
        (case for case in selected if case.mode == "continuous"), None
    )
    cached = [case for case in selected if case.mode == "cached"]
    normal = Case("cached", "original", "positioning", 1)
    # One continuous analysis starts alongside cache preparation. Other continuous cases
    # wait for its atomically committed cache, avoiding duplicate full-source analysis.
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        pending = {}
        if first_continuous and args.jobs > 1:
            future = pool.submit(
                run_case, binary, first_continuous, out, args.minutes, deadline
            )
            pending[future] = first_continuous
        warmup = run_case(
            binary,
            Case("cached", "original", "afk", 0),
            out,
            0.01,
            deadline,
            jobs=max(1, args.jobs - bool(first_continuous)),
        )
        (out / "warmup.json").write_text(json.dumps(warmup, indent=2) + "\n")
        if first_continuous and args.jobs == 1:
            future = pool.submit(
                run_case, binary, first_continuous, out, args.minutes, deadline
            )
            pending[future] = first_continuous
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
        # Iterate rounds so newly submitted continuous-cache readers are also joined.
        while pending:
            future = next(as_completed(pending))
            case = pending.pop(future)
            results[case] = future.result()
            if case == first_continuous:
                for extra in selected:
                    if extra.mode == "continuous" and extra != first_continuous:
                        following = pool.submit(
                            run_case, binary, extra, out, args.minutes, deadline
                        )
                        pending[following] = extra
    rows = [summary(case, results[case]) for case in selected]
    # The first normal case keeps a session. Full 30:00 replay/diagnostic delivery
    # evidence is checked separately, outside this matrix's 20-minute release budget.
    report = {
        "assertSurvival": args.assert_survival,
        "pacingReportOnly": True,
        "wallSeconds": time.monotonic() - start,
        "cases": rows,
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


def matrix_run(home_cache: Path) -> tuple[Case, Path | None, dict] | None:
    """Only reuse reports from an exact current-input matrix, in a stable case order."""
    for asserting in (False, True):
        key = input_key(asserting)
        if key is None:
            continue
        stamp = home_cache / "ci-stamps" / f"survivors-{key}"
        if not stamp.is_file():
            continue
        folder = Path(stamp.read_text().strip())
        for case in cases():
            if case.bot != "positioning":
                continue
            report = folder / f"{case.name}.json"
            if report.is_file():
                data = json.loads(report.read_text())
                if not data["noHeroDamage"] and complete_run(data):
                    session = folder / "normal-session"
                    reuse = (
                        case == Case("cached", "original", "positioning", 1)
                        and (session / "meta.json").is_file()
                    )
                    return case, session if reuse else None, data
    return None


def run_replay(args: argparse.Namespace) -> None:
    enter_qos(args.release)
    start = time.monotonic()
    deadline = start + 20 * 60
    home_cache = Path.home() / "Library/Caches/playlist-survivors"
    key = input_key(False)
    if key:
        key = hashlib.sha256(
            (key + f"|replay={args.replay_case or 'auto'}").encode()
        ).hexdigest()
    stamp = home_cache / "ci-stamps" / f"survivors-replay-{key}"
    if (
        key
        and stamp.is_file()
        and (Path(stamp.read_text().strip()) / "summary.json").is_file()
        and not args.force
    ):
        print(
            f"survivors replay: unchanged inputs {key}, passed at {stamp.read_text().strip()}"
        )
        return
    out = (
        args.out
        or home_cache / "survivors-replay" / f"{key or 'dirty'}-{time.time_ns()}"
    )
    out.mkdir(parents=True, exist_ok=False)
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
    prepared = matrix_run(home_cache) if args.replay_case is None else None
    session = None
    if args.replay_case:
        case = next(c for c in cases() if c.name == args.replay_case)
        diagnostic = False
    elif prepared is not None:
        case, session, data = prepared
        diagnostic = False
    else:
        # Structural coverage before all weapons exist: excluded from balance verdicts.
        case = Case("cached", "original", "positioning", 1)
        diagnostic = True
    if session is None:
        data = run_case(
            binary,
            case,
            out,
            30,
            deadline,
            record="runner-session",
            soak=diagnostic,
            jobs=args.jobs,
        )
        session = out / "runner-session"
    if not complete_run(data):
        raise ValueError("the selected runner session did not finish 30:00")
    meta = json.loads((session / "meta.json").read_text())
    runs = meta["runs"]
    if len(runs) != 1 or runs[0].get("noHeroDamage", False) != data["noHeroDamage"]:
        raise ValueError("recording/report damage modes differ")
    if (
        runs[0]["settledHash"] != data["report"]["settledHash"]
        or runs[0]["settledRecords"] != data["report"]["settledRecords"]
    ):
        raise ValueError("recording/report settled prefixes differ")
    replay = checked(
        [str(binary), "replay", str(session)], deadline, cwd=ROOT / "native"
    )
    (out / "replay.log").write_text(replay.stdout + replay.stderr)
    (out / "source-report.json").write_text(json.dumps(data, indent=2) + "\n")
    report = {
        "runnerDiagnostic": data["noHeroDamage"],
        "survivalVerdict": None,
        "case": case.name,
        "session": str(session),
        "wallSeconds": time.monotonic() - start,
        "report": data["report"],
    }
    (out / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    print(
        f"survivors replay: {data['report']['settledRecords']} records, hash "
        f"{data['report']['settledHash']}, diagnostic={data['noHeroDamage']}, "
        f"{report['wallSeconds']:.3f}s; {out}",
        flush=True,
    )
    now_key = input_key(False)
    if key and now_key:
        current = hashlib.sha256(
            (now_key + f"|replay={args.replay_case or 'auto'}").encode()
        ).hexdigest()
        if current == key:
            stamp.parent.mkdir(parents=True, exist_ok=True)
            stamp.write_text(str(out) + "\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--jobs", type=int, choices=range(1, 17), default=6)
    parser.add_argument("--only", type=int, choices=range(1, 42))
    parser.add_argument("--minutes", type=float, default=30)
    parser.add_argument("--out", type=Path)
    parser.add_argument("--release", action="store_true")
    parser.add_argument("--assert-survival", action="store_true")
    parser.add_argument("--force", action="store_true")
    parser.add_argument("--replay-only", action="store_true")
    parser.add_argument(
        "--replay-case", choices=[c.name for c in cases() if c.bot == "positioning"]
    )
    args = parser.parse_args()
    if not 0 < args.minutes <= 30:
        parser.error("minutes must be positive and <= 30")
    if args.replay_case and not args.replay_only:
        parser.error("--replay-case needs --replay-only")
    if args.replay_only and (
        args.only is not None or args.minutes != 30 or args.assert_survival
    ):
        parser.error(
            "replay uses a complete 30-minute run and has no balance assertions"
        )
    try:
        if args.replay_only:
            run_replay(args)
        else:
            run(args)
    except (OSError, ValueError, subprocess.SubprocessError, TimeoutError) as error:
        print(f"survivors sweep: {error}", file=sys.stderr)
        raise SystemExit(1) from error


if __name__ == "__main__":
    main()
