"""The derived record: hashes and GUIDs of files made from vendor files or the song.

Bakers write derived files into each demo's gitignored Assets/Baked/, and Unity rewrites some
vendor files in Assets/Vendor/ when it imports them (demo2d's binary-serialized Spine clips become
YAML text). A copy of either elsewhere in the tree would pass the sync record's checks, since its
bytes are new. So before every check the guard hashes every file in the demo3d/ and demo2d/
Assets/Baked/ and Assets/Vendor/ of every worktree of this clone (a file copied from another
worktree is caught even if no guard ran there), and the song's source MP3. The guard checks
against those entries directly, and adds them to the per-machine derived record, which remembers
the files of worktrees since removed. It only ever adds.
A file in Baked/ whose bytes are a blob tracked at its worktree's HEAD is a copy of our own file
(setup copies the song map there), so it isn't recorded.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
from pathlib import Path

from . import paths

META_GUID = re.compile(rb"^guid:\s*([0-9a-fA-F]{32})", re.MULTILINE)


def _write_json(path: Path, data: object) -> None:
    """Writes atomically: checks in several worktrees run concurrently."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f"{path.name}.{os.getpid()}.tmp")
    tmp.write_text(json.dumps(data), encoding="utf-8")
    os.replace(tmp, path)


def _hashes(path: Path, cache: dict, key: str) -> tuple[str, str]:
    st = path.stat()
    hit = cache.get(key)
    if hit and hit["size"] == st.st_size and hit["mtime"] == st.st_mtime_ns:
        return hit["sha256"], hit["sha256Lf"]
    data = path.read_bytes()
    raw = hashlib.sha256(data).hexdigest()
    lf = hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest() if b"\r\n" in data else raw
    cache[key] = {"size": st.st_size, "mtime": st.st_mtime_ns, "sha256": raw, "sha256Lf": lf}
    return raw, lf


def worktrees(repo: Path) -> list[Path]:
    """This worktree first, then every other worktree of its clone that still exists."""
    out = subprocess.run(["git", "worktree", "list", "--porcelain"], cwd=repo,
                         capture_output=True, text=True, check=True).stdout
    found = [repo.resolve()]
    for line in out.splitlines():
        if line.startswith("worktree "):
            wt = Path(line.removeprefix("worktree ")).resolve()
            if wt not in found and wt.is_dir():
                found.append(wt)
    return found


_tracked_blobs: dict[Path, set[str]] = {}


def _is_tracked_copy(wt: Path, path: Path) -> bool:
    """True if the file's bytes are a blob tracked at its worktree's HEAD: a copy of our own
    file (the song map, say), not something derived from a vendor file or the song."""
    if wt not in _tracked_blobs:
        # A worktree being removed lists nothing, so its files are all recorded.
        out = subprocess.run(["git", "ls-tree", "-r", "HEAD"], cwd=wt, capture_output=True,
                             text=True, check=False).stdout
        _tracked_blobs[wt] = {line.split()[2] for line in out.splitlines() if line}
    if path.stat().st_size > 1 << 20 or path.suffix == ".meta":
        return False  # tracked text files are small; a .meta's GUID is Unity's own
    data = path.read_bytes().replace(b"\r\n", b"\n")  # git stores text with LF
    blob = hashlib.sha1(b"blob %d\x00" % len(data) + data).hexdigest()
    return blob in _tracked_blobs[wt]


def _files(repo: Path) -> list[tuple[Path | None, Path, str]]:
    """(worktree, file, origin) for every baked and vendor file in every worktree; the worktree
    only for baked files, the ones that may be copies of tracked files."""
    files = []
    for wt in worktrees(repo):
        where = "" if wt == repo.resolve() else f" (worktree {wt.name})"
        for demo in paths.PROJECTS:
            for folder in ("Baked", "Vendor"):
                top = wt / demo / "Assets" / folder
                for dirpath, _dirs, names in os.walk(top):
                    for n in names:
                        p = Path(dirpath) / n
                        rel = p.relative_to(top).as_posix()
                        files.append((wt if folder == "Baked" else None, p,
                                      f"{demo}/Assets/{folder}/{rel}{where}"))
    return files


def refresh(repo: Path, record: Path) -> list[dict]:
    """Every baked and vendor file in every worktree, and the song, as derived-record entries;
    adds the new ones to the derived record."""
    stat_cache_file = record.with_name("derived-hashes.json")
    try:
        old_cache = json.loads(stat_cache_file.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        old_cache = {}
    stat_cache: dict = {}  # only the files seen now, so removed worktrees drop out
    files = _files(repo)
    song = paths.load_roots().get("song")
    if song and Path(song).is_file():
        files.append((None, Path(song), "song source"))

    current: list[dict] = []
    for wt, p, origin in files:
        key = str(p.resolve())
        try:
            if wt is not None and _is_tracked_copy(wt, p):
                continue
            if key in old_cache:
                stat_cache[key] = old_cache[key]
            raw, lf = _hashes(p, stat_cache, key)
            m = META_GUID.search(p.read_bytes()[:4096]) if p.name.endswith(".meta") else None
        except OSError:
            continue  # removed while we looked (another worktree's sync or removal)
        entry = {"path": origin, "origin": "derived: " + origin, "sha256": raw, "sha256Lf": lf}
        if m:
            entry["guid"] = m.group(1).decode().lower()
        current.append(entry)

    existing: list[dict] = []
    if record.exists():
        existing = json.loads(record.read_text(encoding="utf-8")).get("files", [])
    known = {(e["sha256"], e.get("guid")) for e in existing}
    added = 0
    for e in current:
        if (e["sha256"], e.get("guid")) not in known:
            existing.append(e)
            known.add((e["sha256"], e.get("guid")))
            added += 1
    if added or not record.exists():
        _write_json(record, {"version": 1, "files": existing})
    if stat_cache != old_cache:
        _write_json(stat_cache_file, stat_cache)
    return current
