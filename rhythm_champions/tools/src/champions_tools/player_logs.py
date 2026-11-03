"""Persistent player logs and the standalone macOS app launcher (stdlib only).

Copied into built apps: keep this module independent of champions_tools and Python >=3.9.
"""
from __future__ import annotations

import hashlib
import json
import os
import plistlib
import shutil
import subprocess
import sys
import time
import uuid
from pathlib import Path


def checkout_id(root: Path) -> str:
    # resolve symlinks, retain case: case-sensitive APFS checkouts must remain distinct.
    return hashlib.sha256(os.fsencode(str(root.resolve()))).hexdigest()[:24]


def log_root() -> Path:
    if os.environ.get("CHAMPIONS_CACHE"):
        cache = Path(os.environ["CHAMPIONS_CACHE"])
    elif os.environ.get("PS_CACHE"):
        cache = Path(os.environ["PS_CACHE"]) / "champions"
    elif sys.platform == "darwin":
        cache = Path.home() / "Library/Caches/playlist-survivors/champions"
    elif sys.platform == "win32":
        cache = Path(os.environ["LOCALAPPDATA"]) / "playlist-survivors/champions"
    else:
        cache = Path.home() / ".cache/playlist-survivors/champions"
    return cache.resolve() / "player-logs"


def kind(flags: list[str]) -> str:
    if "--replay" in flags:
        return "replay"
    if any(f in flags for f in ("--capture-stills", "--bench-out", "--capture")):
        return "capture"
    if "-batchmode" in flags or "--headless" in flags or os.environ.get("CI"):
        return "test"
    return "play"


def explicit_log(flags: list[str]) -> bool:
    return any(f.lower() == "-logfile" for f in flags)


def latest(link: Path, target: Path) -> None:
    link.parent.mkdir(parents=True, exist_ok=True)
    temporary = link.with_name(link.name + "." + uuid.uuid4().hex)
    temporary.symlink_to(target)
    temporary.replace(link)


def prune(folder: Path, keep: int = 100, now: float | None = None, pattern: str = "*") -> None:
    """Only completed immutable runs; crashes without a completion marker are preserved.

    Keep at most 100 completed runs per checkout/kind (1000 globally), none older than 30 days.
    Active runs are deliberately not bounded or inferred dead from a reused PID.
    """
    now = time.time() if now is None else now
    completed = []
    for run in folder.glob(pattern):
        if run.is_symlink() or not run.is_dir():
            continue
        try:
            completed.append(((run / "complete.json").stat().st_mtime, run))
        except FileNotFoundError:
            pass
    for i, (stamp, run) in enumerate(sorted(completed, reverse=True)):
        if i >= keep or now - stamp > 30 * 86400:
            shutil.rmtree(run, ignore_errors=True)


def allocate(identity: str, flags: list[str]) -> Path:
    root = log_root()
    root.mkdir(parents=True, exist_ok=True)
    prune(root, keep=1000, pattern="*/*/*")
    folder = root / identity / kind(flags)
    folder.mkdir(parents=True, exist_ok=True)
    prune(folder)
    run = folder / (time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + uuid.uuid4().hex)
    run.mkdir(mode=0o700)
    (run / "run.json").write_text(json.dumps({"kind": kind(flags), "ci": bool(os.environ.get("CI")), "checkout": identity,
                                              "started_utc": time.time()}) + "\n")
    latest(folder / "latest", run)
    return run


def complete(run: Path, code: int) -> None:
    (run / "complete.json").write_text(json.dumps({"exit_code": code}) + "\n")
    prune(run.parent)
    prune(log_root(), keep=1000, pattern="*/*/*")


def install(app: Path, root: Path) -> None:
    """Serialize first-time packaging when concurrent CLI launches share an older app."""
    import fcntl  # only the macOS packaging path; standalone launching needs no lock

    locks = log_root().parent / "app-launcher-locks"
    locks.mkdir(parents=True, exist_ok=True)
    with (locks / (checkout_id(app) + ".lock")).open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        _install(app, root)


def _install(app: Path, root: Path) -> None:
    """Route Finder/open launches before the native engine starts; retain its binary name."""
    contents = app / "Contents"
    plist = contents / "Info.plist"
    with plist.open("rb") as stream:
        info = plistlib.load(stream)
    native = info["CFBundleExecutable"]
    config = contents / "Resources/champions-launch.json"
    if native == "champions-launch":
        native = json.loads(config.read_text())["native"]
    resources = config.parent
    resources.mkdir(parents=True, exist_ok=True)
    source = Path(__file__).read_bytes()
    config_text = json.dumps({"native": native, "checkout": checkout_id(root)}) + "\n"
    script = resources / "champions-player-logs.py"
    if script.exists() and script.read_bytes() == source and config.read_text() == config_text:
        return
    script.write_bytes(source)
    config.write_text(config_text)
    launcher = contents / "MacOS/champions-launch"
    launcher.write_text('#!/bin/sh\nexec /usr/bin/python3 "$(dirname "$0")/../Resources/champions-player-logs.py" "$@"\n')
    launcher.chmod(0o755)
    info["CFBundleExecutable"] = launcher.name
    with plist.open("wb") as stream:
        plistlib.dump(info, stream)
    # Changing Info.plist invalidates Unity's ad hoc signature.
    subprocess.run(["/usr/bin/codesign", "--force", "--deep", "--sign", "-", str(app)], check=True,
                   stdout=subprocess.DEVNULL)


def main() -> int:
    resources = Path(__file__).resolve().parent
    config = json.loads((resources / "champions-launch.json").read_text())
    flags = sys.argv[1:]
    run = None
    if "--champions-run-dir" in flags:
        i = flags.index("--champions-run-dir")
        run = Path(flags[i + 1])
        del flags[i:i + 2]
    elif not explicit_log(flags):
        run = allocate(config["checkout"], flags)
        flags = ["-logFile", str(run / "engine.log"), *flags]
    if run:
        print(f"play: engine log {run / 'engine.log'}", flush=True)
    code = subprocess.run([str(resources.parent / "MacOS" / config["native"]), *flags], check=False).returncode
    if run:
        complete(run, code)
    return code


if __name__ == "__main__":
    sys.exit(main())
