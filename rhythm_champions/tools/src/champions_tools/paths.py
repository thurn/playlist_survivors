"""Where things live: the worktree, the Unity project and the per-machine cache.

The tools serve one Unity project, `rhythm_champions/`. The guard alone looks wider: it covers
every Unity project in the tree (PROJECTS), the frozen ones included, since the repository is
public.

The cache ($CHAMPIONS_CACHE, else $PS_CACHE/champions) holds what never reaches git (vendor files,
a warm Library) and the machine's config: `roots.json` maps each manifest source root to a folder,
and `sync-record.json` lists every vendor file under every root for the guard.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

UNITY_VERSION = "6000.6.7f1"
PROJECT = "rhythm_champions"
NAMESPACE = "Champions"
# Every Unity project in the tree, which the guard covers alike.
PROJECTS = (PROJECT, "game", "demo3d", "demo2d", "stylelab", "herocap")
# The synced source roots each project's manifest draws on (the guard's partial-record check).
PROJECT_ROOTS = {
    PROJECT: ("synty", "unity_assets", "more_assets"),
    "game": ("synty", "unity_assets"),
    "demo3d": ("synty", "unity_assets"),
    "demo2d": ("pixel_assets", "unity_assets"),
    "stylelab": ("synty", "unity_assets"),
    "herocap": ("more_assets",),
}
# The roots that are asset folders (an `Assets/` folder of packs, files beside their `.meta`).
ASSET_FOLDERS = ("unity_assets", "pixel_assets", "more_assets")
# Roots only the Mac maps: a manifest's entries from one need it listed in the manifest's
# `requires` (Rhythm Champions' and herocap's do), or a roster set (demo3d's, synced on request).
OPT_IN_ROOTS = ("more_assets",)
# The frozen demos' song (NCS, never tracked): the guard records its source's hash when mapped.
SONG_FILE = "Alessia Labate, Conrad Taylor - Action [NCS Release].mp3"


def is_windows() -> bool:
    return sys.platform == "win32"


def _machine_cache(name: str, env: str) -> Path:
    if os.environ.get(env):
        return Path(os.environ[env])
    if os.environ.get("PS_CACHE"):
        return Path(os.environ["PS_CACHE"]) / name
    if is_windows():
        if Path("D:/").exists():
            return Path("D:/cache/playlist-survivors") / name
        return Path(os.environ["LOCALAPPDATA"]) / "playlist-survivors" / name
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Caches" / "playlist-survivors" / name
    return Path.home() / ".cache" / "playlist-survivors" / name


def cache_root() -> Path:
    """The per-machine cache: $CHAMPIONS_CACHE, else $PS_CACHE/champions, else the platform's."""
    return _machine_cache("champions", "CHAMPIONS_CACHE")


def frozen_cache() -> Path:
    """The frozen projects' tools' cache (demo3d/tools): its derived record and HeroEditor
    frames, which the guard still reads."""
    return _machine_cache("demo3d", "DEMO3D_CACHE")


def record_path() -> Path:
    return cache_root() / "sync-record.json"


def default_roots() -> dict[str, str]:
    """Each machine's vendor sources. `unity_assets` holds the 3D packs (`.../UnityAssets/Assets`),
    `pixel_assets` the 2D packs, `more_assets` the Heroic Fantasy packs (the Fat Ogre): the Mac's
    only."""
    if is_windows():
        return {
            "synty": "D:/vendor/synty",
            "unity_assets": "D:/vendor/unity_assets/UnityAssets",
            "pixel_assets": "D:/vendor/unity_assets",
            "song": f"D:/vendor/songs/action/{SONG_FILE}",
        }
    home = Path.home()
    return {
        "synty": str(home / "synty"),
        "unity_assets": str(home / "unity_assets" / "UnityAssets"),
        "pixel_assets": str(home / "unity_assets"),
        "more_assets": str(home / "unity_assets" / "MoreAssets" / "MoreAssets"),
        "song": str(home / "Downloads" / SONG_FILE),
    }


def load_roots(create: bool = False) -> dict[str, str]:
    """The machine's roots.json, with defaults for any root it lacks; written back when `create`
    is set."""
    path = cache_root() / "roots.json"
    saved = json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}
    roots = {**default_roots(), **saved}
    if create and roots != saved:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(roots, indent=2) + "\n", encoding="utf-8")
    return roots


def unity_exe() -> Path:
    env = os.environ.get("UNITY_EXE")
    if env:
        return Path(env)
    if is_windows():
        return Path(f"C:/Program Files/Unity/Hub/Editor/{UNITY_VERSION}/Editor/Unity.exe")
    return Path(f"/Applications/Unity/Hub/Editor/{UNITY_VERSION}/Unity.app/Contents/MacOS/Unity")


@dataclass(frozen=True)
class Worktree:
    root: Path

    @property
    def project(self) -> Path:
        return self.root / PROJECT

    @property
    def vendor(self) -> Path:
        return self.project / "Assets" / "Vendor"

    @property
    def library(self) -> Path:
        return self.project / "Library"

    @property
    def logs(self) -> Path:
        return self.project / "Logs"


def git_root(start: Path | None = None) -> Path:
    out = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=start or Path.cwd(),
        check=True,
        capture_output=True,
        text=True,
    ).stdout.strip()
    return Path(out)


def worktree(start: Path | None = None) -> Worktree:
    return Worktree(git_root(start))
