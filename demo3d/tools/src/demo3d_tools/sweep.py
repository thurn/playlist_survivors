"""`demo3d sweep`: a benchmark sweep in the windowed player, then its report (benchmarks.md).

The player does the measuring, the search, the refusals and the noise marking (BenchRunner.cs).
This side only:

- takes the machine lock *before* the player opens its window, so a second sweep waits without
  putting a window over the first one's (the player is told with `--lock-held`);
- puts the player in a Job object that kills it when this process dies, so a sweep killed with
  `taskkill /F` (of either process) leaves no player behind holding the GPU, and the lock, whose
  handle Windows closes with the process, frees at once;
- stops this worktree's warm editor, which idles at about 6% of Cygnus's CPU;
- launches the player at exactly 1920x1080 windowed on Direct3D 12 with 6 job workers, and
  streams its bench.log;
- writes report.md from the rows (report.py).
"""

from __future__ import annotations

import ctypes
import json
import os
import subprocess
import sys
import time
from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path

from . import paths, report, unity

EXE = "PlaylistSurvivors3D.exe"
EXIT_NAMES = {0: "done", 2: "bad arguments", 3: "refused", 4: "failed"}


def lock_path() -> Path:
    return paths.cache_root() / "bench.lock"


class LockHeld(Exception):
    pass


class MachineLock:
    """bench.lock in the per-machine cache, opened with no sharing (as BenchRunner's MachineLock)."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.handle: int | None = None

    def try_acquire(self, description: str) -> bool:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        if not paths.is_windows():
            raise RuntimeError("sweeps run on Windows (Cygnus)")
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        k32.CreateFileW.restype = ctypes.c_void_p
        k32.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p,
                                    ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
        h = k32.CreateFileW(str(self.path), 0xC0000000, 0, None, 4, 0x80, None)  # no sharing, open always
        if h in (None, ctypes.c_void_p(-1).value):
            err = ctypes.get_last_error()
            if err == 32:  # ERROR_SHARING_VIOLATION
                return False
            raise OSError(f"bench lock: can't open {self.path} (error {err})")
        self.handle = h
        info = f"pid {os.getpid()} since {time.strftime('%Y-%m-%d %H:%M:%S')}: {description}\n"
        written = ctypes.c_uint32()
        data = info.encode()
        k32.WriteFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32,
                                  ctypes.POINTER(ctypes.c_uint32), ctypes.c_void_p]
        k32.WriteFile(h, data, len(data), ctypes.byref(written), None)
        try:
            self.holder_path.write_text(info, encoding="utf-8")
        except OSError:
            pass
        return True

    @property
    def holder_path(self) -> Path:
        return self.path.with_name(self.path.name + ".holder")

    def holder(self) -> str:
        try:
            return self.holder_path.read_text(encoding="utf-8").strip()
        except OSError:
            return "unknown holder"

    def acquire(self, description: str, timeout: float | None = None, note_every: float = 30) -> None:
        """Waits for the lock (never around it); prints who holds it every `note_every` seconds."""
        started = time.monotonic()
        last = -note_every
        while not self.try_acquire(description):
            waited = time.monotonic() - started
            if timeout is not None and waited >= timeout:
                raise LockHeld(f"the machine lock is held ({self.holder()})")
            if waited - last >= note_every:
                print(f"sweep: waiting for the machine lock {self.path} ({self.holder()})", flush=True)
                last = waited
            time.sleep(1)

    def release(self) -> None:
        if self.handle is not None:
            ctypes.WinDLL("kernel32").CloseHandle(ctypes.c_void_p(self.handle))
            self.handle = None


class KillOnCloseJob:
    """A Job object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: its processes die with this one."""

    def __init__(self) -> None:
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        k32.CreateJobObjectW.restype = ctypes.c_void_p
        self.k32 = k32
        self.handle = k32.CreateJobObjectW(None, None)
        if not self.handle:
            raise OSError(f"CreateJobObject failed ({ctypes.get_last_error()})")

        class Basic(ctypes.Structure):
            _fields_ = [("PerProcessUserTimeLimit", ctypes.c_int64), ("PerJobUserTimeLimit", ctypes.c_int64),
                        ("LimitFlags", ctypes.c_uint32), ("MinimumWorkingSetSize", ctypes.c_size_t),
                        ("MaximumWorkingSetSize", ctypes.c_size_t), ("ActiveProcessLimit", ctypes.c_uint32),
                        ("Affinity", ctypes.c_size_t), ("PriorityClass", ctypes.c_uint32),
                        ("SchedulingClass", ctypes.c_uint32)]

        class Io(ctypes.Structure):
            _fields_ = [(n, ctypes.c_uint64) for n in ("r", "w", "o", "rb", "wb", "ob")]

        class Extended(ctypes.Structure):
            _fields_ = [("Basic", Basic), ("Io", Io), ("ProcessMemoryLimit", ctypes.c_size_t),
                        ("JobMemoryLimit", ctypes.c_size_t), ("PeakProcessMemoryUsed", ctypes.c_size_t),
                        ("PeakJobMemoryUsed", ctypes.c_size_t)]

        info = Extended()
        info.Basic.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        k32.SetInformationJobObject.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32]
        if not k32.SetInformationJobObject(self.handle, 9, ctypes.byref(info), ctypes.sizeof(info)):
            raise OSError(f"SetInformationJobObject failed ({ctypes.get_last_error()})")

    def assign(self, proc: subprocess.Popen) -> None:
        self.k32.AssignProcessToJobObject.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
        if not self.k32.AssignProcessToJobObject(self.handle, ctypes.c_void_p(int(proc._handle))):  # type: ignore[attr-defined]
            raise OSError(f"AssignProcessToJobObject failed ({ctypes.get_last_error()})")


def player_args(spec: Path, out: Path, screen: tuple[int, int], workers: int, extra: list[str]) -> list[str]:
    return ["-force-d3d12", "-screen-width", str(screen[0]), "-screen-height", str(screen[1]),
            "-screen-fullscreen", "0", "-job-worker-count", str(workers),
            "--bench", str(spec), "--out", str(out), "-logFile", str(out / "player.log"), *extra]


def run_player(exe: Path, args: list[str], out: Path, job: KillOnCloseJob | None) -> int:
    """Runs the player, echoing new bench.log lines, and returns its exit code."""
    log = out / "bench.log"
    start = log.stat().st_size if log.exists() else 0
    proc = subprocess.Popen([str(exe), *args], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                            stderr=subprocess.DEVNULL)
    if job:
        job.assign(proc)
    pos = start
    while True:
        code = proc.poll()
        if log.exists():
            with open(log, "rb") as f:
                f.seek(pos)
                chunk = f.read()
                pos += len(chunk)
            if chunk:
                sys.stdout.write(chunk.decode("utf-8", "replace"))
                sys.stdout.flush()
        if code is not None:
            return code
        time.sleep(0.5)


@contextmanager
def machine_lock(description: str, enabled: bool = True) -> Iterator[MachineLock | None]:
    if not enabled:
        yield None
        return
    lock = MachineLock(lock_path())
    lock.acquire(description)
    print(f"sweep: took the machine lock {lock.path}", flush=True)
    try:
        yield lock
    finally:
        lock.release()


def ensure_player(wt: paths.Worktree, backend: str, build: bool) -> Path:
    exe = wt.project / "Builds" / backend / EXE
    if build or not exe.exists():
        print(f"sweep: building the {backend} player", flush=True)
        if unity.run_method(wt, "BuildPlayer", ["-backend", backend]) != 0:
            raise RuntimeError("the player build failed")
    return exe


def cmd_sweep(a) -> int:
    wt = paths.worktree()
    specs = [Path(s).resolve() for s in a.spec]
    out = Path(a.out).resolve() if a.out else wt.logs / "bench" / time.strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True, exist_ok=True)
    exe = Path(a.player).resolve() if a.player else ensure_player(wt, a.backend, a.build)
    if unity.editor_stop(wt):
        print("sweep: stopped this worktree's warm editor (it idles at about 6% CPU)", flush=True)
    screen = tuple(int(x) for x in a.screen.lower().split("x")) if a.screen else None
    worst = 0
    with machine_lock(f"demo3d sweep {' '.join(s.name for s in specs)}", enabled=not a.no_lock):
        job = KillOnCloseJob()
        for spec in specs:
            body = json.loads(spec.read_text(encoding="utf-8"))
            if a.no_foreground:
                # A copy with quiet.requireForeground off, for when a dialog the harness may not
                # dismiss holds the foreground: focus loss is then recorded, not noise.
                body.setdefault("quiet", {})["requireForeground"] = False
                spec = out / f"{spec.stem}.no-foreground.json"
                spec.write_text(json.dumps(body, indent=2) + "\n", encoding="utf-8")
            size = screen or tuple(body.get("screen", [1920, 1080]))
            workers = a.workers or int(body.get("jobWorkers", 6))
            extra = [] if a.no_lock else ["--lock-held"]
            print(f"sweep: {exe.name} {spec.name} -> {out}", flush=True)
            code = run_player(exe, player_args(spec, out, size, workers, extra), out, job)
            print(f"sweep: the player exited {code} ({EXIT_NAMES.get(code, 'unknown')})", flush=True)
            if code == 3 and (out / "refusal.json").exists():
                print("sweep: REFUSED: " + json.loads((out / "refusal.json").read_text())["reason"], flush=True)
            worst = max(worst, code)
            if code != 0:
                break
    if (out / "rows.jsonl").exists():
        path = report.write(out)
        print(f"sweep: report {path}")
    return worst
