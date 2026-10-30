"""Where things live: the worktree, the Unity project and the per-machine cache.

One tool serves the demos' Unity projects, `demo3d/`, `demo2d/` and the style prototype
`stylelab/` (the `demo3d`, `demo2d` and `stylelab` commands). They share the per-machine cache, its roots and its sync record, so one guard knows
every vendor file on the machine.

The cache holds what never reaches git (vendor files, baked files, a warm Library) and the
machine's config: `roots.json` maps each manifest source root to a folder, and
`sync-record.json` lists every synced vendor file for the guard. On Cygnus the cache is on the
Dev Drive beside the worktrees, since ReFS block clones only work within one volume.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

UNITY_VERSION = "6000.6.7f1"
PROJECTS = ("demo3d", "demo2d", "stylelab")
# The source roots each project's setup needs (the song from the `song` root, the rest synced):
# demo2d syncs the 2D packs and reuses demo3d's sounds; stylelab reuses demo3d's roster.
PROJECT_ROOTS = {
    "demo3d": ("synty", "unity_assets", "song"),
    "demo2d": ("pixel_assets", "unity_assets", "song"),
    "stylelab": ("synty", "unity_assets", "song"),
}
# Each project's C# namespace prefix.
NAMESPACES = {"demo3d": "Demo3D", "demo2d": "Demo2D", "stylelab": "StyleLab"}
# The roots that are asset folders (an `Assets/` folder of packs, files beside their `.meta`).
ASSET_FOLDERS = ("unity_assets", "pixel_assets")
SONG_FILE = "Alessia Labate, Conrad Taylor - Action [NCS Release].mp3"


def is_windows() -> bool:
    return sys.platform == "win32"


def cache_root() -> Path:
    """The per-machine cache: $DEMO3D_CACHE, else the platform's default."""
    env = os.environ.get("DEMO3D_CACHE")
    if env:
        return Path(env)
    if is_windows():
        if Path("D:/").exists():
            return Path("D:/cache/playlist-survivors/demo3d")
        return Path(os.environ["LOCALAPPDATA"]) / "playlist-survivors" / "demo3d"
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Caches" / "playlist-survivors" / "demo3d"
    return Path.home() / ".cache" / "playlist-survivors" / "demo3d"


def record_path() -> Path:
    return cache_root() / "sync-record.json"


def default_roots() -> dict[str, str]:
    """Each machine's vendor sources (windows.md, Moving the assets).

    `unity_assets` and `pixel_assets` are asset folders holding an `Assets/` folder: the 3D
    packs in `~/unity_assets/UnityAssets/Assets`, the 2D packs (Pixel Cattle Games, the Ancient
    Forest, CFXR) in `~/unity_assets/Assets`.
    """
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
        "song": str(home / "Downloads" / SONG_FILE),
    }


def load_roots(create: bool = False) -> dict[str, str]:
    """The machine's roots.json, with defaults for any root it lacks (a roots.json written
    before demo2d has no `pixel_assets`); written back when `create` is set."""
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
    name: str = "demo3d"  # the Unity project: demo3d, demo2d or stylelab

    @property
    def project(self) -> Path:
        return self.root / self.name

    @property
    def namespace(self) -> str:
        """The project's C# namespace prefix: Demo3D, Demo2D or StyleLab."""
        return NAMESPACES[self.name]

    @property
    def vendor(self) -> Path:
        return self.project / "Assets" / "Vendor"

    @property
    def baked(self) -> Path:
        return self.project / "Assets" / "Baked"

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


def worktree(start: Path | None = None, name: str = "demo3d") -> Worktree:
    return Worktree(git_root(start), name)
