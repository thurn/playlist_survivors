"""TD5's interim measurement runner, until TD7's sweep runner lands in demo3d.

Runs the IL2CPP player's crowd bench (`--crowd-bench`) for TD5's plan, one process per run:
- holds the machine lock (bench.lock in the per-machine cache, the same file and sharing mode as
  TD7's MachineLock), so it never overlaps a TD7 sweep;
- before each run, waits up to a minute for the machine to be quiet: no other player process,
  GPU under 10% busy and other processes under 10% of total CPU over 5 s;
- during each run, samples other processes' CPU (total busy time minus the player's) and other
  players every second, and marks the run noisy past the same threshold; a noisy run is
  repeated once (a search not at all: it is many runs in one process) and kept marked;
- appends each row the player writes, plus `noisy`, `otherCpuPct` and `attempt`, to the output.

uv run --project demo3d/tools python demo3d/evidence/td5/bench.py OUT.jsonl breakdown|search|ablations|...
"""

from __future__ import annotations

import ctypes
import json
import os
import subprocess
import sys
import tempfile
import threading
import time
from ctypes import wintypes
from pathlib import Path

EXE = Path(__file__).resolve().parents[2] / "Builds" / "il2cpp" / "PlaylistSurvivors3D.exe"
CACHE = Path(os.environ.get("DEMO3D_CACHE", r"D:\cache\playlist-survivors\demo3d"))
BASE = ["-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0", "-job-worker-count", "6", "--crowd-bench"]
QUIET_PCT = 10.0

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = ctypes.c_void_p
k32.GetProcessTimes.argtypes = [ctypes.c_void_p] + [ctypes.c_void_p] * 4
k32.CloseHandle.argtypes = [ctypes.c_void_p]


def lock() -> int:
    path = CACHE / "bench.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    k32.CreateFileW.restype = ctypes.c_void_p
    k32.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
                                ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    waited = 0
    while True:
        h = k32.CreateFileW(str(path), 0xC0000000, 0, None, 4, 0x80, None)
        if h not in (None, ctypes.c_void_p(-1).value):
            info = f"pid {os.getpid()} since {time.strftime('%Y-%m-%d %H:%M:%S')}: td5 crowd bench\n"
            (CACHE / "bench.lock.holder").write_text(info, encoding="utf-8")
            print("bench: took the machine lock", flush=True)
            return h
        if waited % 60 == 0:
            holder = (CACHE / "bench.lock.holder").read_text(encoding="utf-8").strip() if (CACHE / "bench.lock.holder").exists() else "?"
            print(f"bench: waiting for the machine lock ({holder})", flush=True)
        time.sleep(1)
        waited += 1


def ft(f: wintypes.FILETIME) -> int:
    return (f.dwHighDateTime << 32) | f.dwLowDateTime


def system_times() -> tuple[int, int]:
    idle, kernel, user = wintypes.FILETIME(), wintypes.FILETIME(), wintypes.FILETIME()
    k32.GetSystemTimes(ctypes.byref(idle), ctypes.byref(kernel), ctypes.byref(user))
    total = ft(kernel) + ft(user)  # kernel includes idle
    return total - ft(idle), total


def process_busy(handle: int) -> int:
    c, e, k, u = (wintypes.FILETIME() for _ in range(4))
    k32.GetProcessTimes(ctypes.c_void_p(handle), ctypes.byref(c), ctypes.byref(e), ctypes.byref(k), ctypes.byref(u))
    return ft(k) + ft(u)


def other_players(own: int | None) -> int:
    # Any player build (TD7 renames its own, PlaylistSurvivors3D-TD7.exe).
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    pids = [int(line.split('","')[1]) for line in out.splitlines() if line.startswith('"PlaylistSurvivors3D')]
    return len([p for p in pids if p != own])


def gpu_busy() -> float:
    try:
        out = subprocess.run(["nvidia-smi", "--query-gpu=utilization.gpu", "--format=csv,noheader,nounits"],
                             capture_output=True, text=True, timeout=10).stdout
        return float(out.strip().splitlines()[0])
    except (OSError, ValueError, IndexError, subprocess.TimeoutExpired):
        return -1


def other_cpu_pct(seconds: float) -> float:
    b0, t0 = system_times()
    time.sleep(seconds)
    b1, t1 = system_times()
    return 100.0 * (b1 - b0) / max(1, t1 - t0)


def wait_quiet(limit: float = 60) -> bool:
    started = time.monotonic()
    while time.monotonic() - started < limit:
        if other_players(None) == 0 and gpu_busy() < QUIET_PCT and other_cpu_pct(5) < QUIET_PCT:
            return True
        time.sleep(5)
    print("bench: the machine stayed busy; running anyway (the run will be marked)", flush=True)
    return False


def run_once(args: list[str], out: Path, log: Path) -> dict:
    quiet = True if "--stills" in args else wait_quiet()
    fd, name = tempfile.mkstemp(suffix=".jsonl")
    os.close(fd)
    tmp = Path(name)
    proc = subprocess.Popen([str(EXE), *BASE, "-logFile", str(log), "--out", str(tmp), *args],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    handle = k32.OpenProcess(0x1000, False, proc.pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    samples: list[float] = []
    players = 0

    def sample() -> None:
        nonlocal players
        b0, t0 = system_times()
        p0 = process_busy(handle)
        while proc.poll() is None:
            time.sleep(1)
            b1, t1 = system_times()
            p1 = process_busy(handle)
            samples.append(100.0 * max(0, (b1 - b0) - (p1 - p0)) / max(1, t1 - t0))
            b0, t0, p0 = b1, t1, p1
            players = max(players, other_players(proc.pid))

    t = threading.Thread(target=sample, daemon=True)
    t.start()
    proc.wait()
    t.join()
    k32.CloseHandle(ctypes.c_void_p(handle))
    rows = [json.loads(line) for line in tmp.read_text(encoding="utf-8").splitlines() if line.strip()]
    tmp.unlink()
    other = sum(samples) / len(samples) if samples else 0
    noisy = (not quiet) or players > 0 or other > QUIET_PCT
    for r in rows:
        r.update({"otherCpuPct": round(other, 1), "otherPlayers": players, "quietBefore": quiet, "noisy": noisy})
    return {"rows": rows, "noisy": noisy, "exit": proc.returncode}


def run(tag: str, args: list[str], out: Path) -> None:
    logs = out.parent / "logs"
    logs.mkdir(exist_ok=True)
    # A noisy run is repeated once (a search, which is many runs in one process, never).
    attempts = 1 if "--search" in args or "--stills" in args else 2
    for attempt in range(1, attempts + 1):
        res = run_once(args, out, logs / f"{tag}-{attempt}.log")
        with open(out, "a", encoding="utf-8") as f:
            for r in res["rows"]:
                r.update({"tag": tag, "attempt": attempt})
                f.write(json.dumps(r) + "\n")
        print(f"bench: {tag} attempt {attempt}: exit {res['exit']}, {'noisy' if res['noisy'] else 'quiet'}", flush=True)
        if not res["noisy"]:
            return


def plan(mode: str) -> list[tuple[str, list[str]]]:
    p: list[tuple[str, list[str]]] = []
    if mode == "breakdown":
        for rep in (1, 2):
            for scene in ("s1", "s2"):
                for arm in ("vertex", "palette", "stock"):
                    p.append((f"breakdown-{arm}-{scene}-{rep}", ["--arm", arm, "--scene", scene, "--n", "2000",
                                                                 "--view-width", "40", "--warmup", "5", "--seconds", "20"]))
    elif mode == "search":
        for scene in ("s2", "s1"):
            for arm, probes in (("vertex", "800,1400,2000"), ("palette", "800,1400,2000"), ("stock", "150,300,500")):
                p.append((f"search-{arm}-{scene}", ["--arm", arm, "--scene", scene, "--view-width", "40", "--search",
                                                    "--probes", probes]))
    elif mode == "ablations":
        for arm in ("vertex", "palette"):
            for ab in ("", "main", "shadows", "skin"):
                extra = ["--ablate", ab] if ab else []
                p.append((f"ablate-{arm}-{ab or 'none'}", ["--arm", arm, "--scene", "s2", "--n", "2000", "--view-width", "40",
                                                           "--warmup", "5", "--seconds", "20", *extra]))
    elif mode == "skin":
        # The review's re-run: skinning off with the palette pass skipped too (6afb8f4 on).
        for ab in ("", "skin"):
            extra = ["--ablate", ab] if ab else []
            p.append((f"skin-bone-{ab or 'none'}", ["--arm", "bone", "--scene", "s2", "--n", "2000", "--view-width", "40",
                                                   "--warmup", "5", "--seconds", "20", *extra]))
    elif mode == "stills":
        # Under the lock but not timed: no quiet wait matters, one attempt each.
        out = str(Path(__file__).resolve().parent)
        common = ["--arm", "bone", "--scene", "s2", "--stills", out]
        p.append(("stills-views", [*common, "--still-widths", "30,40,55", "--density", "1.6"]))
        p.append(("stills-detail", [*common, "--still-widths", "30", "--density", "0.7", "--crop"]))
        p.append(("stills-motion", [*common, "--still-widths", "30", "--density", "0.7", "--crop", "--live", "--fixed-time", "--show-motion"]))
        p.append(("stills-normals", [*common, "--still-widths", "30", "--density", "0.7", "--crop", "--show-normals"]))
        p.append(("stills-lights", [*common, "--still-widths", "30", "--density", "0.7", "--crop", "--lights", "48"]))
    elif mode == "extension":
        for ab in ("", "extension"):
            extra = ["--ablate", ab] if ab else []
            p.append((f"extension-{ab or 'on'}", ["--arm", "palette", "--scene", "s1", "--n", "2000", "--sun-elevation", "25",
                                                  "--sun-yaw", "0", "--warmup", "3", "--seconds", "5", *extra]))
    else:
        raise SystemExit(f"unknown mode {mode}")
    return p


def main() -> None:
    out = Path(sys.argv[1])
    modes = sys.argv[2:]
    h = lock()
    try:
        for mode in modes:
            for tag, args in plan(mode):
                run(tag, args, out)
    finally:
        k32.CloseHandle(ctypes.c_void_p(h))


if __name__ == "__main__":
    main()
