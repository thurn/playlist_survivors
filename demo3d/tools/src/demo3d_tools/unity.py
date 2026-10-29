"""Runs Unity in batch mode on a worktree's own project, cold or through the warm editor.

Every launch names its project (Unity reopens its last project otherwise). A cold launch
streams the log to the console with `-logFile -`. The warm editor (WarmEditor.cs) holds the
project's lock while it runs, so every Unity step in that worktree goes through it then.
"""

from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path

from . import paths

TASKS = "Demo3D.EditorTools.Tasks"


def method_name(name: str) -> str:
    return name if "." in name else f"{TASKS}.{name}"


def base_args(wt: paths.Worktree) -> list[str]:
    return [str(paths.unity_exe()), "-batchmode", "-projectPath", str(wt.project)]


def run_cold(wt: paths.Worktree, extra: list[str], log_name: str, quiet: bool = False) -> int:
    """One cold batch-mode launch, its log streamed to the console and saved under Logs/."""
    wt.logs.mkdir(parents=True, exist_ok=True)
    log = wt.logs / f"{log_name}.log"
    cmd = base_args(wt) + extra + ["-logFile", "-"]
    started = time.monotonic()
    with open(log, "w", encoding="utf-8", errors="replace") as out:
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        assert proc.stdout
        for raw in proc.stdout:
            line = raw.decode("utf-8", "replace")
            out.write(line)
            if not quiet:
                sys.stdout.write(line)
        code = proc.wait()
    print(f"unity: {log_name} exited {code} after {time.monotonic() - started:.1f} s (log {log})")
    return code


# The warm editor.


@dataclass
class Server:
    port: int
    token: str
    pid: int


def _server_file(wt: paths.Worktree) -> Path:
    return wt.library / "Demo3DWarm" / "server.json"


def pid_alive(pid: int) -> bool:
    if paths.is_windows():
        import ctypes

        kernel32 = ctypes.windll.kernel32
        handle = kernel32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            return False
        code = ctypes.c_ulong()
        ok = kernel32.GetExitCodeProcess(handle, ctypes.byref(code))
        kernel32.CloseHandle(handle)
        return bool(ok) and code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def warm_server(wt: paths.Worktree) -> Server | None:
    f = _server_file(wt)
    if not f.exists():
        return None
    try:
        s = json.loads(f.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    server = Server(int(s["port"]), s["token"], int(s["pid"]))
    return server if pid_alive(server.pid) else None


def _send(server: Server, cmd: str, args: list[str]) -> str:
    job = uuid.uuid4().hex
    msg = json.dumps({"token": server.token, "id": job, "cmd": cmd, "args": "\t".join(args)})
    deadline = time.monotonic() + 120
    while True:  # the listener is down for a moment during a domain reload
        try:
            with socket.create_connection(("127.0.0.1", server.port), timeout=10) as sock:
                sock.sendall(msg.encode() + b"\n")
                reply = json.loads(sock.makefile("rb").readline() or b"{}")
            break
        except (ConnectionError, TimeoutError, OSError):
            if time.monotonic() > deadline or not pid_alive(server.pid):
                raise
            time.sleep(0.5)
    if reply.get("accepted") != job:
        raise RuntimeError(f"warm editor refused the job: {reply}")
    return job


def warm_job(wt: paths.Worktree, server: Server, cmd: str, args: list[str], timeout: float) -> dict:
    job = _send(server, cmd, args)
    result = wt.library / "Demo3DWarm" / "jobs" / f"{job}.json"
    deadline = time.monotonic() + timeout
    while not result.exists():
        if not pid_alive(server.pid):
            raise RuntimeError("the warm editor exited during the job (see Logs/warm-editor.log)")
        if time.monotonic() > deadline:
            raise TimeoutError(f"warm editor job {cmd} timed out after {timeout:.0f} s")
        time.sleep(0.1)
    for _ in range(50):
        try:
            return json.loads(result.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            time.sleep(0.05)
    raise RuntimeError(f"unreadable job result {result}")


def editor_start(wt: paths.Worktree, wait: float = 600) -> Server:
    existing = warm_server(wt)
    if existing:
        return existing
    _server_file(wt).unlink(missing_ok=True)
    wt.logs.mkdir(parents=True, exist_ok=True)
    cmd = base_args(wt) + [
        "-executeMethod", "Demo3D.EditorTools.WarmEditor.Serve",
        "-logFile", str(wt.logs / "warm-editor.log"),
    ]
    flags = 0
    if paths.is_windows():
        flags = subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS
    proc = subprocess.Popen(cmd, creationflags=flags, stdin=subprocess.DEVNULL,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + wait
    while time.monotonic() < deadline:
        server = warm_server(wt)
        if server:
            return server
        if proc.poll() is not None:
            raise RuntimeError(f"the warm editor exited with {proc.returncode} (Logs/warm-editor.log)")
        time.sleep(0.25)
    raise TimeoutError("the warm editor didn't start (Logs/warm-editor.log)")


def editor_stop(wt: paths.Worktree) -> bool:
    server = warm_server(wt)
    if not server:
        return False
    try:
        _send(server, "quit", [])
    except OSError:
        pass
    deadline = time.monotonic() + 60
    while pid_alive(server.pid) and time.monotonic() < deadline:
        time.sleep(0.25)
    return True


# Commands that use the warm editor when it runs and a cold launch otherwise.


def run_method(wt: paths.Worktree, name: str, args: list[str], cold: bool = False,
               timeout: float = 3600) -> int:
    method = method_name(name)
    server = None if cold else warm_server(wt)
    if server:
        started = time.monotonic()
        refresh = warm_job(wt, server, "refresh", [], timeout)
        if not refresh["ok"]:
            print(f"unity (warm): compile failed:\n{refresh['detail']}")
            return 1
        res = warm_job(wt, server, "exec", [method, *args], timeout)
        print(f"unity (warm): {method} {'ok' if res['ok'] else 'FAILED'} after "
              f"{time.monotonic() - started:.1f} s\n{res['detail']}")
        return 0 if res["ok"] else 1
    return run_cold(wt, ["-executeMethod", method, *args, "-quit"], name.split(".")[-1])


@dataclass
class TestSummary:
    total: int
    passed: int
    failed: int
    skipped: int
    failures: str

    @property
    def ok(self) -> bool:
        return self.failed == 0 and self.total > 0


def parse_nunit(xml_path: Path) -> TestSummary:
    root = ET.parse(xml_path).getroot()
    failures = []
    for case in root.iter("test-case"):
        if case.get("result") == "Failed":
            msg = case.findtext("failure/message") or ""
            failures.append(f"{case.get('fullname')}: {msg.strip()}")
    return TestSummary(
        int(root.get("total", 0)), int(root.get("passed", 0)), int(root.get("failed", 0)),
        int(root.get("skipped", 0)), "\n".join(failures),
    )


def run_tests(wt: paths.Worktree, assemblies: list[str], cold: bool = False) -> TestSummary:
    server = None if cold else warm_server(wt)
    if server:
        started = time.monotonic()
        refresh = warm_job(wt, server, "refresh", [], 1800)
        if not refresh["ok"]:
            return TestSummary(0, 0, 1, 0, f"compile failed: {refresh['detail']}")
        res = warm_job(wt, server, "test", assemblies, 1800)
        summary = parse_nunit(Path(res["detail"]["xml"]))
        print(f"unity (warm): EditMode tests after {time.monotonic() - started:.1f} s")
        return summary
    xml = wt.logs / "editmode-results.xml"
    xml.unlink(missing_ok=True)
    extra = ["-runTests", "-testPlatform", "EditMode", "-testResults", str(xml)]
    if assemblies:
        extra += ["-assemblyNames", ";".join(assemblies)]
    code = run_cold(wt, extra, "editmode-tests", quiet=True)
    if not xml.exists():
        return TestSummary(0, 0, 1, 0, f"no results (exit {code}): compile errors? see Logs/editmode-tests.log")
    return parse_nunit(xml)
