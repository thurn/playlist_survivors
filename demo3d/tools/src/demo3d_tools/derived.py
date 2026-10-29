"""The derived record: hashes and GUIDs of files made from vendor files or the song.

Bakers write derived files into each demo's gitignored Assets/Baked/. A copy of one elsewhere in
the tree would pass the sync record's checks, since its bytes and GUID are new. So before every
check the guard adds every file in this worktree's demo3d/ and demo2d/ Assets/Baked/ (and the
song's source MP3) to the per-machine derived record. It only ever adds: every worktree's baked
files stay recorded.
A file in Baked/ whose bytes are a blob tracked at HEAD is a copy of our own file (setup copies
the song map there), so it isn't recorded.
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


_tracked_blobs: dict[Path, set[str]] = {}


def _is_tracked_copy(repo: Path, path: Path) -> bool:
    """True if the file's bytes are a blob tracked at HEAD: a copy of our own file (the song
    map, say), not something derived from a vendor file or the song."""
    if repo not in _tracked_blobs:
        out = subprocess.run(["git", "ls-tree", "-r", "HEAD"], cwd=repo, capture_output=True,
                             text=True, check=True).stdout
        _tracked_blobs[repo] = {line.split()[2] for line in out.splitlines() if line}
    if path.stat().st_size > 1 << 20 or path.suffix == ".meta":
        return False  # tracked text files are small; a .meta's GUID is Unity's own
    data = path.read_bytes().replace(b"\r\n", b"\n")  # git stores text with LF
    blob = hashlib.sha1(b"blob %d\x00" % len(data) + data).hexdigest()
    return blob in _tracked_blobs[repo]


def refresh(repo: Path, record: Path) -> int:
    """Adds this worktree's baked files and the song to the derived record; returns new entries."""
    files: list[tuple[Path, str]] = []
    for demo in paths.PROJECTS:
        baked = repo / demo / "Assets" / "Baked"
        if not baked.is_dir():
            continue
        for dirpath, _dirs, names in os.walk(baked):
            for n in names:
                p = Path(dirpath) / n
                files.append((p, f"{demo}/Assets/Baked/" + p.relative_to(baked).as_posix()))
    files = [f for f in files if not _is_tracked_copy(repo, f[0])]
    song = paths.load_roots().get("song")
    if song and Path(song).is_file():
        files.append((Path(song), "song source"))

    existing: list[dict] = []
    if record.exists():
        existing = json.loads(record.read_text(encoding="utf-8")).get("files", [])
    known = {(e["sha256"], e.get("guid")) for e in existing}
    stat_cache_file = record.with_name("derived-hashes.json")
    try:
        stat_cache = json.loads(stat_cache_file.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        stat_cache = {}

    added = 0
    for p, origin in files:
        raw, lf = _hashes(p, stat_cache, str(p.resolve()))
        guid = None
        if p.name.endswith(".meta"):
            m = META_GUID.search(p.read_bytes()[:4096])
            guid = m.group(1).decode().lower() if m else None
        if (raw, guid) in known:
            continue
        entry = {"path": origin, "origin": "derived: " + origin, "sha256": raw, "sha256Lf": lf}
        if guid:
            entry["guid"] = guid
        existing.append(entry)
        known.add((raw, guid))
        added += 1
    if added or not record.exists():
        record.parent.mkdir(parents=True, exist_ok=True)
        tmp = record.with_name(f"{record.name}.{os.getpid()}.tmp")  # checks run concurrently
        tmp.write_text(json.dumps({"version": 1, "files": existing}), encoding="utf-8")
        os.replace(tmp, record)
    stat_cache_file.write_text(json.dumps(stat_cache), encoding="utf-8")
    return added
