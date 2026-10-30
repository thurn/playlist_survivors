"""`demo3d setup` (or `demo2d setup`): prepares a fresh worktree's project in one command.

It checks the machine's sources, makes sure the sync record exists (empty until the sync tool
first fills it), clones the vendor folder, the baked folders and a warm Library from the
per-machine cache (rebuilding only what is stale), installs the worktree's pre-push guard, and
opens the project once in batch mode, which also verifies the Unity license.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import time
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from . import clone, paths, unity


def file_hash(root: Path, *files: Path) -> str:
    """A hash of files' names (relative to `root`, so every worktree agrees) and contents."""
    h = hashlib.sha256()
    for f in files:
        h.update(f.relative_to(root).as_posix().encode())
        h.update(f.read_bytes().replace(b"\r\n", b"\n") if f.exists() else b"<missing>")
    return h.hexdigest()[:16]


def manifest_key(wt: paths.Worktree) -> str:
    """The vendor cache's key: the manifest, and the sync tool's version."""
    from . import sync

    manifest = wt.project / "manifest.json"
    return hashlib.sha256((file_hash(wt.root, manifest) + sync.SYNC_VERSION).encode()).hexdigest()[:16]


def library_key(wt: paths.Worktree) -> str:
    p = wt.project
    return file_hash(
        wt.root,
        p / "ProjectSettings" / "ProjectVersion.txt",
        p / "Packages" / "manifest.json",
        p / "Packages" / "packages-lock.json",
    )


# Bakers that run without Unity, for either project. demo3d's Unity bakers (TD2, TD5) register
# in Bakers.cs and run through `demo3d unity BakeAll`.


@dataclass
class PyBaker:
    name: str
    inputs: Callable[[paths.Worktree, dict[str, str]], list[Path]]
    build: Callable[[paths.Worktree, dict[str, str], Path], None]
    version: str


# TD0 fixed this conversion (songs/action/source.json, "conversion"): the MP3 decoded with
# its encoder delay and padding trimmed, then encoded to Ogg Vorbis, so the Ogg's sample 0 is
# the song map's time 0. An ffmpeg without libvorbis (Homebrew's, on the Mac) writes the same
# decoded samples as WAV instead; Unity's importer encodes either to streamed Vorbis.
SONG_OGG_ARGS = ["-map_metadata", "-1", "-c:a", "libvorbis", "-q:a", "6"]
SONG_WAV_ARGS = ["-map_metadata", "-1", "-c:a", "pcm_s16le"]


def ffmpeg_has_libvorbis() -> bool:
    out = subprocess.run(["ffmpeg", "-hide_banner", "-encoders"], capture_output=True, text=True,
                         check=True).stdout
    return any(line.split()[1:2] == ["libvorbis"] for line in out.splitlines())


def _song_inputs(wt: paths.Worktree, roots: dict[str, str]) -> list[Path]:
    return [Path(roots["song"]), wt.root / "songs" / "action" / "songmap.json"]


def _song_build(wt: paths.Worktree, roots: dict[str, str], out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    songmap = wt.root / "songs" / "action" / "songmap.json"
    if songmap.exists():
        shutil.copy2(songmap, out / "action.songmap.json")
    args, name = (SONG_OGG_ARGS, "action.ogg") if ffmpeg_has_libvorbis() else (SONG_WAV_ARGS, "action.wav")
    cmd = ["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
           "-i", roots["song"], "-map", "0:a", *args, str(out / name)]
    subprocess.run(cmd, check=True)


PY_BAKERS = [PyBaker("Audio", _song_inputs, _song_build, version="4")]


def _source_key(wt: paths.Worktree, inputs: list[Path], version: str) -> str:
    # Large sources (the MP3) are keyed by size and mtime rather than read in full each setup;
    # the manifest's SHA-256 check guards their identity where it matters (vendor files).
    h = hashlib.sha256(version.encode())
    for f in inputs:
        name = f.relative_to(wt.root) if f.is_relative_to(wt.root) else f
        h.update(name.as_posix().encode())
        if not f.exists():
            h.update(b"<missing>")
        elif f.stat().st_size > 1 << 20:
            st = f.stat()
            h.update(f"{st.st_size}:{st.st_mtime_ns}".encode())
        else:
            h.update(f.read_bytes().replace(b"\r\n", b"\n"))
    return h.hexdigest()[:16]


class SetupError(Exception):
    pass


def check_sources(roots: dict[str, str], needed: tuple[str, ...]) -> None:
    missing = [f"{name}: {roots[name]}" for name in needed if not Path(roots[name]).exists()]
    if missing:
        raise SetupError(
            "missing vendor source(s):\n  " + "\n  ".join(missing)
            + f"\nfix the path in {paths.cache_root() / 'roots.json'}, or restore the source"
        )


def ensure_record() -> Path:
    record = paths.record_path()
    if not record.exists():
        record.parent.mkdir(parents=True, exist_ok=True)
        record.write_text(json.dumps({"version": 1, "files": []}, indent=2) + "\n", encoding="utf-8")
    return record


def install_hook(wt: paths.Worktree) -> Path:
    """Copies the tracked pre-push hook into the clone's shared hooks folder.

    Every checkout of the clone (the start-up checkout and every worktree, set up or not) then
    runs it. A core.hooksPath anywhere would make git skip that folder, so setup refuses one.
    """
    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", *args], cwd=wt.root, capture_output=True, text=True,
                              check=False)

    if git("config", "--get", "extensions.worktreeConfig").stdout.strip() == "true":
        git("config", "--worktree", "--unset", "core.hooksPath")  # TD1's first, per-worktree hook
    hooks_path = git("config", "--get", "core.hooksPath").stdout.strip()
    if hooks_path:
        raise SetupError(f"core.hooksPath is set ({hooks_path}), so git would skip the shared "
                         "pre-push guard; unset it")
    common = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir").stdout.strip())
    hook = common / "hooks" / "pre-push"
    hook.parent.mkdir(parents=True, exist_ok=True)
    data = (wt.root / "demo3d" / "tools" / "hooks" / "pre-push").read_bytes().replace(b"\r\n", b"\n")
    if not hook.exists() or hook.read_bytes() != data:
        tmp = hook.with_name("pre-push.tmp")
        tmp.write_bytes(data)
        tmp.chmod(0o755)
        tmp.replace(hook)
    return hook


def build_vendor(wt: paths.Worktree, roots: dict[str, str], out: Path) -> None:
    manifest = json.loads((wt.project / "manifest.json").read_text(encoding="utf-8"))
    out.mkdir(parents=True, exist_ok=True)
    if manifest.get("assets"):
        from . import sync

        sync.sync(manifest, roots, out, paths.record_path())


def _cached(cache_dir: Path, build: Callable[[Path], None]) -> tuple[Path, bool]:
    """The cache entry, building it (atomically) if missing; returns (path, built)."""
    if cache_dir.exists():
        return cache_dir, False
    tmp = cache_dir.with_name(cache_dir.name + ".tmp")
    clone.remove_tree(tmp)
    build(tmp)
    tmp.rename(cache_dir)
    return cache_dir, True


def library_candidates(cache: Path, lib_key: str, vendor_key: str) -> list[Path]:
    """The exact Library for these packages and vendor files, else the newest for the packages."""
    base = cache / "library" / lib_key
    exact = base / vendor_key
    if exact.exists():
        return [exact]
    others = sorted((p for p in base.glob("*") if p.is_dir() and not p.name.endswith(".tmp")),
                    key=lambda p: p.stat().st_mtime, reverse=True)
    return others


def save_library(wt: paths.Worktree) -> Path:
    cache = paths.cache_root()
    dest = cache / "library" / library_key(wt) / manifest_key(wt)
    tmp = dest.with_name(f"{dest.name}.{os.getpid()}.tmp")
    clone.clone_tree(wt.library, tmp)
    clone.remove_tree(tmp / f"{wt.namespace}Warm")
    clone.remove_tree(dest)
    tmp.rename(dest)
    built_marker(dest).unlink(missing_ok=True)
    return dest


def built_marker(entry: Path) -> Path:
    """Beside a cached Library: present once the entry holds a player build's shader cache."""
    return entry.with_name(entry.name + ".built")


def save_built_library(wt: paths.Worktree) -> Path | None:
    """After a player build, saves the Library once per packages and vendor files.

    The Library setup saves holds only the import; the first player build compiles every shader
    variant the baked scenes use (20+ minutes on the Mac, ps-e8r.9), and its ShaderCache then
    warms every later worktree. Returns where it saved, or None if the cache already has it.
    """
    entry = paths.cache_root() / "library" / library_key(wt) / manifest_key(wt)
    if built_marker(entry).exists():
        return None
    dest = save_library(wt)
    built_marker(dest).write_text(time.strftime("%Y-%m-%dT%H:%M:%S") + "\n", encoding="utf-8")
    return dest


BAKED_STAMP = ".bakeall"  # Bakers.RunStale writes it into Assets/Baked/ after a full BakeAll


def baked(wt: paths.Worktree) -> bool:
    """Whether a full `unity BakeAll` has run in this worktree (demo2d has no Unity bakers)."""
    return wt.name != "demo3d" or (wt.baked / BAKED_STAMP).exists()


def setup(wt: paths.Worktree, song: str | None = None, run_unity: bool = True,
          fresh_library: bool = False) -> int:
    started = time.monotonic()
    cache = paths.cache_root()
    cache.mkdir(parents=True, exist_ok=True)
    roots = paths.load_roots(create=True)
    if song:
        roots["song"] = song
    check_sources(roots, paths.PROJECT_ROOTS[wt.name])
    record = ensure_record()
    mode = "block clones" if clone.clones_blocks(cache, wt.project) else "plain copies (slow)"
    print(f"setup: cache {cache} ({clone.volume_fs(cache)}), {mode}; record {record}")

    t = time.monotonic()
    vkey = manifest_key(wt)
    src, built = _cached(cache / "vendor" / vkey, lambda out: build_vendor(wt, roots, out))
    n = clone.clone_tree(src, wt.vendor)
    print(f"setup: Vendor {'synced' if built else 'cloned'} ({n} files, manifest {vkey}) "
          f"in {time.monotonic() - t:.2f} s")

    t = time.monotonic()
    wt.baked.mkdir(parents=True, exist_ok=True)
    for b in PY_BAKERS:
        key = _source_key(wt, b.inputs(wt, roots), b.version)
        src, built = _cached(cache / "baked" / b.name / key, lambda out, b=b: b.build(wt, roots, out))
        n = clone.clone_tree(src, wt.baked / b.name)
        print(f"setup: Baked/{b.name} {'rebaked' if built else 'cloned'} ({n} files, {key})")
    print(f"setup: Baked in {time.monotonic() - t:.2f} s")

    t = time.monotonic()
    lkey = library_key(wt)
    warm = False
    if wt.library.exists() and not fresh_library:
        print("setup: Library kept (already present)")
        warm = True
    else:
        cands = library_candidates(cache, lkey, vkey)
        if cands:
            n = clone.clone_tree(cands[0], wt.library)
            exact = cands[0].name == vkey
            print(f"setup: Library cloned ({n} files, {'exact' if exact else 'nearest'}) "
                  f"in {time.monotonic() - t:.2f} s")
            warm = True
        else:
            clone.remove_tree(wt.library)
            print("setup: no warm Library for these packages; the first import builds one")

    hook = install_hook(wt)
    print(f"setup: pre-push guard installed for every checkout ({hook})")
    # The record covers every source root on the machine, not just this project's, so one guard
    # knows both demos' vendor files.
    from . import sync

    t = time.monotonic()
    hashes = sync.HashCache(cache / "source-hashes.json")
    count = sync.build_record(sync.Sources(roots, cache, hashes), record)
    hashes.save()
    print(f"setup: sync record of {count} vendor files in {time.monotonic() - t:.1f} s")

    if run_unity:
        if unity.warm_server(wt):
            print("setup: the warm editor holds the project; skipped the batch-mode check")
        else:
            code = unity.run_cold(wt, ["-executeMethod", unity.method_name(wt, "Noop"), "-quit"],
                                  "setup-noop", quiet=True)
            log = (wt.logs / "setup-noop.log").read_text(encoding="utf-8", errors="replace")
            # A fresh licensing client logs "Licensing is initialized"; one another editor
            # started only logs the connection. Unity refuses to open a project unlicensed.
            licensed = ("No valid Unity Editor license" not in log
                        and ("Licensing is initialized" in log
                             or "Successfully connected to LicensingClient" in log))
            print(f"setup: Unity license {'verified' if licensed else 'NOT verified'}; "
                  f"project opened with exit {code}")
            if code != 0 or not licensed:
                return 1
            if not (cache / "library" / lkey / vkey).exists():
                dest = save_library(wt)
                print(f"setup: saved the warm Library to {dest}")
    print(f"setup: done in {time.monotonic() - started:.1f} s ({'warm' if warm else 'cold'} Library)")
    return 0
