"""`demo3d setup` (or `demo2d`, `stylelab` or `game setup`): prepares a fresh worktree's project in one command.

It checks the machine's sources, makes sure the sync record exists (empty until the sync tool
first fills it), clones the vendor folder, the baked folders and a warm Library from the
per-machine cache (rebuilding only what is stale), installs the worktree's pre-push guard, and
opens the project once in batch mode, which also verifies the Unity license.
"""

from __future__ import annotations

import fnmatch
import hashlib
import json
import os
import re
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


ROSTERS_FILE = ".rosters"  # in the vendor folder: the roster sets synced into it (a dotfile, so Unity skips it)


def synced_rosters(wt: paths.Worktree) -> tuple[str, ...]:
    """The roster sets the worktree's vendor folder holds (`setup --roster`), sorted."""
    f = wt.vendor / ROSTERS_FILE
    return tuple(sorted(f.read_text(encoding="utf-8").split())) if f.exists() else ()


def manifest_key(wt: paths.Worktree, rosters: tuple[str, ...] | None = None) -> str:
    """The vendor cache's key: the manifest, the sync tool's version, and the roster sets synced
    with it (default: the vendor folder's), so a set's vendor folder and Library are never a
    default worktree's exact entry (a set's Library may still be its nearest, as any may). The
    kit packages missing on this machine (sync.absent_kits) count too, so a pack that arrives
    later syncs a new folder."""
    from . import sync

    manifest = wt.project / "manifest.json"
    sets = tuple(sorted(rosters)) if rosters is not None else synced_rosters(wt)
    absent = sync.absent_kits(json.loads(manifest.read_text(encoding="utf-8")), paths.load_roots(), sets)
    extra = "".join(f"+{r}" for r in sets) + "".join(f"-{a}" for a in absent)
    return hashlib.sha256((file_hash(wt.root, manifest) + sync.SYNC_VERSION + extra).encode()).hexdigest()[:16]


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


# The game's free sound packs are the repository's own CC0 and CC-BY samples
# (assets-src/sfx-catalogue.md), cloned into a gitignored folder so game/ never tracks audio. Not
# into Baked/: the guard may record a Baked/ file as derived (derived.py), which would then fail
# the tracked originals.
FREE_SOUNDS = Path("public") / "assets" / "sfx"


def free_sounds(wt: paths.Worktree) -> Path:
    return wt.project / "Assets" / "FreeSounds"


# The game plays the player's music, not the song.
PY_BAKERS = {"game": []}
DEFAULT_PY_BAKERS = [PyBaker("Audio", _song_inputs, _song_build, version="4")]


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
    missing = [f"{name}: {roots.get(name) or '(not mapped)'}" for name in needed
               if not roots.get(name) or not Path(roots[name]).exists()]
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


def build_vendor(wt: paths.Worktree, roots: dict[str, str], out: Path,
                 rosters: tuple[str, ...] = ()) -> None:
    manifest = json.loads((wt.project / "manifest.json").read_text(encoding="utf-8"))
    out.mkdir(parents=True, exist_ok=True)
    if manifest.get("assets"):
        from . import sync

        sync.sync(manifest, roots, out, paths.record_path(), rosters)
    if rosters:
        (out / ROSTERS_FILE).write_text(" ".join(rosters) + "\n", encoding="utf-8")


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
    """The exact Library for these packages and vendor files, else the packages' others: those
    holding a build's shader cache first, newest first."""
    base = cache / "library" / lib_key
    exact = base / vendor_key
    if exact.exists():
        return [exact]
    others = sorted((p for p in base.glob("*") if p.is_dir() and not p.name.endswith(".tmp")),
                    key=lambda p: (built_marker(p).exists(), p.stat().st_mtime), reverse=True)
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


# A build that compiles more variants than this (new keywords, such as a post effect turned on)
# refreshes the saved Library, so later worktrees don't compile them again.
REFRESH_VARIANTS = 100


def compiled_variants(log: Path) -> int:
    """The shader variants a build's log says it compiled (cache misses), 0 without a log."""
    if not log.exists():
        return 0
    text = log.read_text(encoding="utf-8", errors="replace")
    return sum(int(n) for n in re.findall(r"compiled (\d+) variants", text))


def save_built_library(wt: paths.Worktree, compiled: int = 0) -> Path | None:
    """After a player build, saves the Library once per packages and vendor files.

    The Library setup saves holds only the import; the first player build compiles every shader
    variant the baked scenes use (20+ minutes on the Mac, ps-e8r.9), and its ShaderCache then
    warms every later worktree. A later build that compiled more than REFRESH_VARIANTS saves it
    again. Returns where it saved, or None if the cache already has it.
    """
    entry = paths.cache_root() / "library" / library_key(wt) / manifest_key(wt)
    if built_marker(entry).exists() and compiled <= REFRESH_VARIANTS:
        return None
    dest = save_library(wt)
    built_marker(dest).write_text(time.strftime("%Y-%m-%dT%H:%M:%S") + "\n", encoding="utf-8")
    return dest


BAKED_INPUTS = ".bakeall-inputs"  # in Assets/Baked/: each baker's inputs key when it last ran
BAKED_RAN = ".bakeall-ran"  # Bakers.Run appends each baker's name as it finishes

# Runtime C# that an asset can serialize: a baked asset made before such a type changed lacks
# its new fields (a CrowdLibrary baked before StyleShaders existed has none).
_SERIALIZED = re.compile(rb"ScriptableObject|\[Serializable\]|\[System\.Serializable\]")
# Runtime C# a baker calls, which no asset serializes (the SoundBank lists ShatterSounds' names).
_BAKER_RUNTIME = {"Runtime/Playback/ShatterSounds.cs"}


def bake_inputs(wt: paths.Worktree) -> list[Path]:
    """What demo3d's Unity bakers read from git: everything under Assets/Demo3D (their code,
    data, shaders and settings) except the tests and runtime C# no asset serializes, the shared
    choreography package, the vendor manifest and the song map."""
    files: list[Path] = []
    demo = wt.project / "Assets" / "Demo3D"
    package = wt.root / "unity-packages" / "com.playlistsurvivors.choreography"
    for root in (demo, package):
        for f in sorted(root.rglob("*")):
            if not f.is_file() or "Tests" in f.relative_to(root).parts:
                continue
            if (root == demo and f.suffix == ".cs" and f.relative_to(demo).parts[0] == "Runtime"
                    and f.relative_to(demo).as_posix() not in _BAKER_RUNTIME
                    and not _SERIALIZED.search(f.read_bytes())):
                continue
            files.append(f)
    return files + [wt.project / "manifest.json", wt.root / "songs" / "action" / "songmap.json"]


def _demo(*globs: str) -> tuple[str, ...]:
    return tuple("demo3d/Assets/Demo3D/" + g for g in globs)


# The bake inputs each baker (Bakers.cs's registry) reads, as globs over repo-relative paths
# (`*` crosses folders; a trailing `*` takes the .meta too). An input no baker claims (the
# choreography package, Editor/Bakers.cs, the shared Editor/Assets helpers, the vendor manifest)
# makes every baker stale, so a new folder errs toward baking too much.
BAKER_INPUTS: dict[str, tuple[str, ...]] = {
    "Arena": _demo("Editor/Arena/*", "Runtime/Arena/*", "Choreography/Data/*-layout.json*",
                   "Choreography/Data/environment-*", "Choreography/Data/footprints.json*"),
    "StructureShards": _demo("Editor/Structures/*", "Editor/Crowd/CrowdShardBaker.cs*",
                             "Runtime/Arena/StructureShards.cs*", "Choreography/Data/*-layout.json*",
                             "Choreography/Data/environment-*", "Choreography/Data/footprints.json*"),
    "crowd": _demo("Editor/Crowd/*", "Runtime/Crowd/*", "Runtime/Arena/Shaders/Structures/*",
                   "Data/clip-annotations.json*", "Data/roster*.json*"),
    "effects": _demo("Editor/Effects/*", "Runtime/Effects/*", "Data/effects.json*"),
    "Sounds": _demo("Editor/Assets/SoundBaker.cs*", "Runtime/Playback/SoundBank.cs*",
                    "Runtime/Playback/ShatterSounds.cs*"),
    "Roster": _demo("Editor/Assets/RosterBaker.cs*", "Runtime/Playback/RosterAssets.cs*",
                    "Data/roster*.json*"),
    "Playback": _demo("Editor/Assets/PlaybackBaker.cs*", "Runtime/Playback/*", "Choreography/Data/*",
                      "Data/clip-annotations.json*") + ("songs/action/songmap.json",),
}


def baker_keys(wt: paths.Worktree) -> dict[str, str]:
    """Each baker's inputs key: a hash of the inputs it claims and those no baker claims."""
    digests = {f.relative_to(wt.root).as_posix(): file_hash(wt.root, f) for f in bake_inputs(wt)}
    claims = {name: {r for r in digests if any(fnmatch.fnmatchcase(r, g) for g in globs)}
              for name, globs in BAKER_INPUTS.items()}
    shared = digests.keys() - set().union(*claims.values())
    keys = {}
    for name, own in claims.items():
        h = hashlib.sha256()
        for r in sorted(shared | own):
            h.update(f"{r}\0{digests[r]}\n".encode())
        keys[name] = h.hexdigest()[:16]
    return keys


def _recorded_keys(wt: paths.Worktree) -> dict[str, str]:
    try:
        keys = json.loads((wt.baked / BAKED_INPUTS).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    return keys if isinstance(keys, dict) else {}  # an older single key: bake everything once


def stale_bakers(wt: paths.Worktree) -> list[str]:
    """The bakers whose inputs changed since the tools last saw them run, in BAKER_INPUTS order
    (demo2d and stylelab have no Unity bakers). A BakeAll run outside the tools records nothing,
    so the next build bakes once more."""
    if wt.name != "demo3d":
        return []
    recorded = _recorded_keys(wt)
    return [name for name, key in baker_keys(wt).items() if recorded.get(name) != key]


def baked(wt: paths.Worktree) -> bool:
    return not stale_bakers(wt)


def bake_args(wt: paths.Worktree, stale: list[str]) -> list[str]:
    """BakeAll's `-only` flags for the stale bakers (none when all are: a full run). Clears any
    record of bakers run outside the tools, so only this run's are recorded."""
    (wt.baked / BAKED_RAN).unlink(missing_ok=True)
    if set(stale) >= BAKER_INPUTS.keys():
        return []
    return [arg for name in stale for arg in ("-only", name)]


def record_ran(wt: paths.Worktree) -> list[str]:
    """After a BakeAll (even a failed one): records the inputs of each baker that finished."""
    ran_file = wt.baked / BAKED_RAN
    if not ran_file.exists():
        return []
    ran = [n for n in ran_file.read_text(encoding="utf-8").split() if n in BAKER_INPUTS]
    keys = baker_keys(wt)
    recorded = _recorded_keys(wt) | {n: keys[n] for n in ran}
    (wt.baked / BAKED_INPUTS).write_text(json.dumps(recorded, indent=1, sort_keys=True) + "\n",
                                         encoding="utf-8")
    ran_file.unlink()
    return ran


BUILD_STAMP = ".build-inputs.json"  # in the player's folder: what the last `build` made it from


def player_dir(wt: paths.Worktree, platform: str, backend: str, out: str | None = None) -> Path:
    """Where BuildPlayer writes (its `-buildOut`, else demo3d's Builds/<backend> on Windows, else
    Builds/mac/<backend>)."""
    if out:
        return wt.project / out
    if platform == "windows" and wt.name == "demo3d":
        return wt.project / "Builds" / backend
    return wt.project / "Builds" / "mac" / backend


def _unity_ignores(name: str) -> bool:
    return name.startswith(".") or name.endswith("~")


def project_files(wt: paths.Worktree, known: dict[str, list]) -> dict[str, list]:
    """Every file a player build reads (the project's Assets, Packages and ProjectSettings, and
    the shared packages), as [size, mtime_ns, digest]. A file whose size and mtime match `known`
    keeps its digest unread, so the walk costs a stat per file and a file Unity rewrote unchanged
    still matches."""
    files: dict[str, list] = {}
    tops = [wt.project / d for d in ("Assets", "Packages", "ProjectSettings")] + [wt.root / "unity-packages"]
    for top in tops:
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
    build, which also bakes, imports and puts back the assets URP rewrites)."""
    files = project_files(wt, _build_stamp(out).get("files", {}))
    outputs = sorted(e.name for e in out.iterdir() if e.name != BUILD_STAMP)
    _write_build_stamp(out, args, files, outputs)


def setup(wt: paths.Worktree, song: str | None = None, run_unity: bool = True,
          fresh_library: bool = False, rosters: tuple[str, ...] = ()) -> int:
    """`rosters` also syncs those roster sets' vendor files (demo3d's `--roster`), which need the
    `more_assets` root."""
    started = time.monotonic()
    cache = paths.cache_root()
    cache.mkdir(parents=True, exist_ok=True)
    roots = paths.load_roots(create=True)
    if song:
        roots["song"] = song
    rosters = tuple(sorted(set(rosters)))
    unknown = [r for r in rosters if r not in paths.ROSTERS]
    if unknown or (rosters and wt.name != "demo3d"):
        raise SetupError(f"--roster takes demo3d's sets ({', '.join(paths.ROSTERS)}), not "
                         f"{', '.join(unknown or rosters)}")
    check_sources(roots, paths.PROJECT_ROOTS[wt.name] + (paths.OPT_IN_ROOTS if rosters else ()))
    record = ensure_record()
    mode = "block clones" if clone.clones_blocks(cache, wt.project) else "plain copies (slow)"
    print(f"setup: cache {cache} ({clone.volume_fs(cache)}), {mode}; record {record}")

    t = time.monotonic()
    vkey = manifest_key(wt, rosters)
    from . import sync

    try:
        src, built = _cached(cache / "vendor" / vkey, lambda out: build_vendor(wt, roots, out, rosters))
    except sync.SyncError as e:
        raise SetupError(str(e)) from e
    n = clone.clone_tree(src, wt.vendor)
    print(f"setup: Vendor {'synced' if built else 'cloned'} ({n} files, manifest {vkey}"
          f"{', rosters ' + ' '.join(rosters) if rosters else ''}) "
          f"in {time.monotonic() - t:.2f} s")

    t = time.monotonic()
    wt.baked.mkdir(parents=True, exist_ok=True)
    for b in PY_BAKERS.get(wt.name, DEFAULT_PY_BAKERS):
        key = _source_key(wt, b.inputs(wt, roots), b.version)
        src, built = _cached(cache / "baked" / b.name / key, lambda out, b=b: b.build(wt, roots, out))
        n = clone.clone_tree(src, wt.baked / b.name)
        print(f"setup: Baked/{b.name} {'rebaked' if built else 'cloned'} ({n} files, {key})")
    print(f"setup: Baked in {time.monotonic() - t:.2f} s")
    if wt.name == "game":
        n = clone.clone_tree(wt.root / FREE_SOUNDS, free_sounds(wt))
        print(f"setup: FreeSounds cloned ({n} files) from {FREE_SOUNDS.as_posix()}")
        from . import plugin

        try:
            plugin.install(wt)
        except plugin.PluginError as e:
            raise SetupError(str(e)) from e

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
