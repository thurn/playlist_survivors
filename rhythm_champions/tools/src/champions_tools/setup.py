"""`champions setup`: prepares a fresh worktree's project in one command.

It checks the machine's sources, syncs the vendor folder from the manifest (once per manifest, into
the per-machine cache, then cloned), restores the baked folder and a warm Library from the cache
(each keyed by its inputs), builds the music plugin, installs the clone's pre-push guard, refreshes
the guard's sync record, and opens the project once in batch mode, which also verifies the Unity
license. `ensure` runs whichever of these a worktree still needs before a build.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import time
from collections.abc import Callable
from pathlib import Path

from . import clone, paths, plugin, sync, unity


class SetupError(Exception):
    pass


def file_hash(root: Path, *files: Path) -> str:
    """A hash of files' names (relative to `root`, so every worktree agrees) and contents."""
    h = hashlib.sha256()
    for f in files:
        h.update(f.relative_to(root).as_posix().encode())
        h.update(f.read_bytes().replace(b"\r\n", b"\n") if f.exists() else b"<missing>")
    return h.hexdigest()[:16]


def manifest_key(wt: paths.Worktree) -> str:
    """The vendor cache's key: the manifest and the sync tool's version."""
    return hashlib.sha256((file_hash(wt.root, wt.project / "manifest.json") + sync.SYNC_VERSION)
                          .encode()).hexdigest()[:16]


def library_key(wt: paths.Worktree) -> str:
    """The Library cache's key: the Unity version and the packages (the vendor files key the
    entries under it)."""
    p = wt.project
    return file_hash(wt.root, p / "ProjectSettings" / "ProjectVersion.txt",
                     p / "Packages" / "manifest.json", p / "Packages" / "packages-lock.json")


# What the bakers read (plan.md, Build Policy: the baked cache): the edge-cut recipes and the code
# that cuts them, the editor code that bakes, the crowd and the structures whose assets it writes,
# and the vendor manifest. A worktree whose Assets/Baked/ was made from the same inputs copies it from the cache
# instead of baking again.
BAKE_INPUTS = ("tools/recipes", "tools/src/champions_tools/edgecut", "Assets/Champions/Editor", "Assets/Champions/Render/Crowd",
               "Assets/Champions/Render/Structures")
BAKED_KEY = ".baked-key"  # in Assets/Baked/: the inputs key it was baked from


def baked_dir(wt: paths.Worktree) -> Path:
    return wt.project / "Assets" / "Baked"


def baked_key(wt: paths.Worktree) -> str:
    files = [f for d in BAKE_INPUTS for f in sorted((wt.project / d).rglob("*"))
             if f.is_file() and "__pycache__" not in f.parts]
    return file_hash(wt.root, *files, wt.project / "manifest.json")


def restore_baked(wt: paths.Worktree) -> bool:
    """Clones the cached Assets/Baked/ for the current inputs, if the cache has it and the
    worktree's isn't already that one; whether it cloned."""
    key = baked_key(wt)
    marker = baked_dir(wt) / BAKED_KEY
    if marker.exists() and marker.read_text(encoding="utf-8").strip() == key:
        return False
    src = paths.cache_root() / "baked" / key
    if not src.is_dir():
        return False
    clone.clone_tree(src, baked_dir(wt))
    return True


def save_baked(wt: paths.Worktree) -> Path | None:
    """After a bake: saves Assets/Baked/ as the cache's entry for its inputs (once)."""
    if not baked_dir(wt).is_dir() or not any(baked_dir(wt).iterdir()):
        return None
    key = baked_key(wt)
    (baked_dir(wt) / BAKED_KEY).write_text(key + "\n", encoding="utf-8")
    dest = paths.cache_root() / "baked" / key
    if dest.exists():
        return None
    tmp = dest.with_name(f"{dest.name}.{os.getpid()}.tmp")
    clone.clone_tree(baked_dir(wt), tmp)
    tmp.rename(dest)
    return dest


def check_sources(roots: dict[str, str], needed: tuple[str, ...]) -> None:
    missing = [f"{name}: {roots.get(name) or '(not mapped)'}" for name in needed
               if not roots.get(name) or not Path(roots[name]).exists()]
    if missing:
        raise SetupError(
            "missing vendor source(s):\n  " + "\n  ".join(missing)
            + f"\nfix the path in {paths.cache_root() / 'roots.json'}, or restore the source"
        )


def install_hook(wt: paths.Worktree) -> Path:
    """Copies the tracked pre-push hook into the clone's shared hooks folder.

    Every checkout of the clone (the main checkout and every worktree, set up or not) then runs
    it. A core.hooksPath anywhere would make git skip that folder, so setup refuses one.
    """
    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", *args], cwd=wt.root, capture_output=True, text=True,
                              check=False)

    hooks_path = git("config", "--get", "core.hooksPath").stdout.strip()
    if hooks_path:
        raise SetupError(f"core.hooksPath is set ({hooks_path}), so git would skip the shared "
                         "pre-push guard; unset it")
    common = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir").stdout.strip())
    hook = common / "hooks" / "pre-push"
    hook.parent.mkdir(parents=True, exist_ok=True)
    data = (wt.project / "tools" / "hooks" / "pre-push").read_bytes().replace(b"\r\n", b"\n")
    if not hook.exists() or hook.read_bytes() != data:
        tmp = hook.with_name("pre-push.tmp")
        tmp.write_bytes(data)
        tmp.chmod(0o755)
        tmp.replace(hook)
    return hook


def _cached(cache_dir: Path, build: Callable[[Path], None]) -> tuple[Path, bool]:
    """The cache entry, building it (atomically) if missing; returns (path, built)."""
    if cache_dir.exists():
        return cache_dir, False
    tmp = cache_dir.with_name(f"{cache_dir.name}.{os.getpid()}.tmp")
    clone.remove_tree(tmp)
    build(tmp)
    tmp.rename(cache_dir)
    return cache_dir, True


def build_vendor(wt: paths.Worktree, roots: dict[str, str], out: Path) -> None:
    manifest = json.loads((wt.project / "manifest.json").read_text(encoding="utf-8"))
    out.mkdir(parents=True, exist_ok=True)
    if manifest.get("assets"):
        sync.sync(manifest, roots, out, paths.record_path())


VENDOR_STAMP = "vendor-manifest.json"  # in Library/: the manifest the vendor folder was synced from


def sync_vendor(wt: paths.Worktree, roots: dict[str, str]) -> tuple[int, bool, str]:
    """Clones the vendor folder for the manifest from the cache, syncing it there first if
    missing; (files, synced, key)."""
    key = manifest_key(wt)
    try:
        src, built = _cached(paths.cache_root() / "vendor" / key, lambda out: build_vendor(wt, roots, out))
    except sync.SyncError as e:
        raise SetupError(str(e)) from e
    n = clone.clone_tree(src, wt.vendor)
    return n, built, key


def library_candidates(cache: Path, lib_key: str, vendor_key: str) -> list[Path]:
    """The exact Library for these packages and vendor files, else the packages' others: those
    holding a build's shader cache first, newest first."""
    base = cache / "library" / lib_key
    exact = base / vendor_key
    if exact.exists():
        return [exact]
    return sorted((p for p in base.glob("*") if p.is_dir() and not p.name.endswith(".tmp")),
                  key=lambda p: (built_marker(p).exists(), p.stat().st_mtime), reverse=True)


def save_library(wt: paths.Worktree) -> Path:
    """Saves the worktree's Library, Library/Bee with it (plan.md, Build Policy), as the cache's
    entry for its packages and vendor files. The warm editor's folder stays behind."""
    dest = paths.cache_root() / "library" / library_key(wt) / manifest_key(wt)
    tmp = dest.with_name(f"{dest.name}.{os.getpid()}.tmp")
    clone.clone_tree(wt.library, tmp)
    clone.remove_tree(tmp / f"{paths.NAMESPACE}Warm")
    clone.remove_tree(dest)
    tmp.rename(dest)
    built_marker(dest).unlink(missing_ok=True)
    return dest


def built_marker(entry: Path) -> Path:
    """Beside a cached Library: present once the entry holds a player build's shader cache."""
    return entry.with_name(entry.name + ".built")


# A build that compiles more variants than this (new keywords, a new shader) refreshes the saved
# Library, so later worktrees don't compile them again.
REFRESH_VARIANTS = 50


def compiled_variants(log: Path) -> int:
    """The shader variants a build's log says it compiled (cache misses), 0 without a log."""
    if not log.exists():
        return 0
    text = log.read_text(encoding="utf-8", errors="replace")
    return sum(int(n) for n in re.findall(r"compiled (\d+) variants", text))


def save_built_library(wt: paths.Worktree, compiled: int = 0) -> Path | None:
    """After a player build, saves the Library once per packages and vendor files, with the
    build's shader cache, which warms every later worktree; again after a build that compiled
    more than REFRESH_VARIANTS. Returns where it saved, or None if the cache already has it."""
    entry = paths.cache_root() / "library" / library_key(wt) / manifest_key(wt)
    if built_marker(entry).exists() and compiled <= REFRESH_VARIANTS:
        return None
    dest = save_library(wt)
    built_marker(dest).write_text(time.strftime("%Y-%m-%dT%H:%M:%S") + "\n", encoding="utf-8")
    return dest


BUILD_STAMP = ".build-inputs.json"  # in the player's folder: what the last `build` made it from


def player_dir(wt: paths.Worktree, backend: str, out: str | None = None) -> Path:
    """Where BuildPlayer writes: its `-buildOut`, else Builds/mac/<backend>."""
    return wt.project / out if out else wt.project / "Builds" / "mac" / backend


APP = "RhythmChampions.app"
APP_EXE = f"{APP}/Contents/MacOS/Rhythm Champions"


def _unity_ignores(name: str) -> bool:
    return name.startswith(".") or name.endswith("~")


def project_files(wt: paths.Worktree, known: dict[str, list]) -> dict[str, list]:
    """Every file a player build reads (the project's Assets, Packages and ProjectSettings), as
    [size, mtime_ns, digest]. A file whose size and mtime match `known` keeps its digest unread,
    so the walk costs a stat per file and a file Unity rewrote unchanged still matches."""
    files: dict[str, list] = {}
    for top in (wt.project / d for d in ("Assets", "Packages", "ProjectSettings")):
        for dirpath, dirs, names in os.walk(top, followlinks=True):
            dirs[:] = [d for d in dirs if not _unity_ignores(d)]
            for n in names:
                if _unity_ignores(n):
                    continue
                f = Path(dirpath, n)
                r, st = f.relative_to(wt.root).as_posix(), f.stat()
                old = known.get(r)
                if old and old[:2] == [st.st_size, st.st_mtime_ns]:
                    files[r] = old
                else:
                    with open(f, "rb") as fh:
                        files[r] = [st.st_size, st.st_mtime_ns, hashlib.file_digest(fh, "sha256").hexdigest()[:16]]
    return files


def _build_stamp(out: Path) -> dict:
    try:
        stamp = json.loads((out / BUILD_STAMP).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return stamp if isinstance(stamp, dict) else {}


def _write_build_stamp(out: Path, args: list[str], files: dict[str, list], outputs: list[str]) -> None:
    stamp = {"args": args, "outputs": outputs, "files": files}
    (out / BUILD_STAMP).write_text(json.dumps(stamp, separators=(",", ":")) + "\n", encoding="utf-8")


def player_current(wt: paths.Worktree, out: Path, args: list[str]) -> bool:
    """Whether the player in `out` was built with these flags from the project as it is now, and
    every file that build wrote is still there."""
    stamp = _build_stamp(out)
    outputs = stamp.get("outputs") or []
    if stamp.get("args") != args or not outputs or not all((out / o).exists() for o in outputs):
        return False
    files = project_files(wt, stamp.get("files", {}))
    if {r: v[2] for r, v in files.items()} != {r: v[2] for r, v in stamp["files"].items()}:
        return False
    if files != stamp["files"]:  # touched but unchanged: keep the new mtimes, so they aren't read again
        _write_build_stamp(out, args, files, outputs)
    return True


def forget_build(out: Path) -> None:
    """Before any build into `out`: one that fails partway leaves no stamp claiming the player."""
    (out / BUILD_STAMP).unlink(missing_ok=True)


def record_build(wt: paths.Worktree, out: Path, args: list[str]) -> None:
    """After a successful build: the flags and project files it was made from (read after the
    build, which also imports and puts back the assets URP rewrites)."""
    files = project_files(wt, _build_stamp(out).get("files", {}))
    outputs = sorted(e.name for e in out.iterdir() if e.name != BUILD_STAMP)
    _write_build_stamp(out, args, files, outputs)


def refresh_record(roots: dict[str, str]) -> int:
    """The guard's sync record of every vendor file under every root on the machine."""
    cache = paths.cache_root()
    hashes = sync.HashCache(cache / "source-hashes.json")
    try:
        return sync.build_record(sync.Sources(roots, cache, hashes), paths.record_path())
    finally:
        hashes.save()


def needs_setup(wt: paths.Worktree) -> bool:
    return not wt.library.is_dir() or not wt.vendor.is_dir()


def ensure(wt: paths.Worktree) -> int:
    """Before a build: a full setup for a worktree that never had one, a vendor sync when the
    manifest changed since its last sync, the baked cache when its inputs changed; the plugin."""
    if needs_setup(wt):
        print("setup: this worktree isn't set up yet; setting it up first")
        return setup(wt)
    stamp = wt.library / VENDOR_STAMP
    manifest = wt.project / "manifest.json"
    if not stamp.exists() or stamp.read_bytes() != manifest.read_bytes():
        roots = paths.load_roots(create=True)
        n, synced, key = sync_vendor(wt, roots)
        stamp.write_bytes(manifest.read_bytes())
        print(f"setup: the manifest changed since the last sync; Vendor {'synced' if synced else 'cloned'} "
              f"({n} files, manifest {key})")
    if restore_baked(wt):
        print("setup: Baked cloned from the cache for the current bake inputs")
    plugin.install(wt)
    return 0


def setup(wt: paths.Worktree, run_unity: bool = True, fresh_library: bool = False) -> int:
    started = time.monotonic()
    cache = paths.cache_root()
    cache.mkdir(parents=True, exist_ok=True)
    roots = paths.load_roots(create=True)
    check_sources(roots, paths.PROJECT_ROOTS[paths.PROJECT])
    mode = "block clones" if clone.clones_blocks(cache, wt.project) else "plain copies (slow)"
    print(f"setup: cache {cache} ({clone.volume_fs(cache)}), {mode}")

    t = time.monotonic()
    n, synced, vkey = sync_vendor(wt, roots)
    print(f"setup: Vendor {'synced' if synced else 'cloned'} ({n} files, manifest {vkey}) "
          f"in {time.monotonic() - t:.2f} s")
    if restore_baked(wt):
        print(f"setup: Baked cloned from the cache ({baked_key(wt)})")
    plugin.install(wt)

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
    t = time.monotonic()
    count = refresh_record(roots)
    print(f"setup: sync record of {count} vendor files in {time.monotonic() - t:.1f} s")

    if run_unity:
        if unity.warm_server(wt):
            print("setup: the warm editor holds the project; skipped the batch-mode check")
        else:
            code = unity.run_cold(wt, ["-executeMethod", unity.method_name("Noop"), "-quit"],
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
                print(f"setup: saved the warm Library to {save_library(wt)}")
    wt.library.mkdir(parents=True, exist_ok=True)
    (wt.library / VENDOR_STAMP).write_bytes((wt.project / "manifest.json").read_bytes())
    print(f"setup: done in {time.monotonic() - started:.1f} s ({'warm' if warm else 'cold'} Library)")
    return 0
