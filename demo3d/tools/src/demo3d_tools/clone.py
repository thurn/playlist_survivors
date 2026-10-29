"""Copies trees by cloning blocks where the filesystem can.

On a Windows Dev Drive (ReFS), CopyFile2, which Python's shutil.copy2 calls, clones blocks when
source and destination are on the same volume: 1 GiB in about 11 ms on Cygnus (TD1's spike).
On macOS, `cp -c` clones on APFS. Anywhere else this falls back to plain copies, and says so.
"""

from __future__ import annotations

import os
import shutil
import stat
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path


def volume_fs(path: Path) -> str:
    """The filesystem's name for the volume holding `path` ("ReFS", "NTFS", "apfs", ...)."""
    p = path
    while not p.exists():
        p = p.parent
    if sys.platform == "win32":
        import ctypes

        root = os.path.splitdrive(str(p.resolve()))[0] + "\\"
        name = ctypes.create_unicode_buffer(64)
        ok = ctypes.windll.kernel32.GetVolumeInformationW(
            ctypes.c_wchar_p(root), None, 0, None, None, None, name, 64
        )
        return name.value if ok else "unknown"
    if sys.platform == "darwin":
        out = subprocess.run(["stat", "-f", "%T", str(p)], capture_output=True, text=True, check=False)
        return out.stdout.strip() or "unknown"
    return "unknown"


def same_volume(a: Path, b: Path) -> bool:
    def existing(p: Path) -> Path:
        while not p.exists():
            p = p.parent
        return p

    return os.stat(existing(a)).st_dev == os.stat(existing(b)).st_dev


def clones_blocks(src: Path, dst: Path) -> bool:
    if not same_volume(src, dst):
        return False
    fs = volume_fs(src).lower()
    return fs == "refs" or fs == "apfs"


def _force_remove(func, path, _exc):
    os.chmod(path, stat.S_IWRITE)
    func(path)


def remove_tree(path: Path) -> None:
    if path.is_symlink() or path.is_file():
        path.unlink()
    elif path.exists():
        shutil.rmtree(path, onexc=_force_remove)


def clone_tree(src: Path, dst: Path, workers: int = 16) -> int:
    """Replaces `dst` with a copy of `src`; returns the number of files."""
    remove_tree(dst)
    dst.parent.mkdir(parents=True, exist_ok=True)
    if sys.platform == "darwin":
        subprocess.run(["cp", "-cR", str(src), str(dst)], check=True)
        return sum(len(files) for _, _, files in os.walk(dst))
    files: list[tuple[str, str]] = []
    for dirpath, _dirs, names in os.walk(src):
        rel = os.path.relpath(dirpath, src)
        target = dst if rel == "." else dst / rel
        target.mkdir(parents=True, exist_ok=True)
        for n in names:
            files.append((os.path.join(dirpath, n), str(target / n)))
    with ThreadPoolExecutor(max_workers=workers) as pool:
        list(pool.map(lambda pair: shutil.copy2(*pair), files, chunksize=64))
    return len(files)
