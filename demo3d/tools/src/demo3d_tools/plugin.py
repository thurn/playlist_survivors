"""The game's music plugin as build output (docs/design/unity-game.md, Building and Loading the Plugin).

`game plugin` builds native/crates/plugin with cargo and copies `libps_plugin.dylib` into
game/Assets/Plugins/macOS/, where a player build picks it up and the editor's loader hashes and
copies it, and the analyzer's models into game/Assets/StreamingAssets/ps/, where a player finds
them. Both folders are gitignored. `game setup` and `game build` run it first; cargo makes it a
no-op when nothing changed, and the copies are rewritten only when their content differs, so an
unchanged plugin never looks like a project change to the build stamp or the editor.
"""

from __future__ import annotations

import filecmp
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

from . import paths

DYLIB = "libps_plugin.dylib"
PLUGIN_DIR = Path("Assets/Plugins/macOS")
MODELS_DIR = Path("Assets/StreamingAssets/ps")


class PluginError(Exception):
    pass


def install(wt: paths.Worktree) -> Path:
    """Builds the plugin and copies it and the models into the game project; returns the copy."""
    if sys.platform != "darwin":
        raise PluginError("the plugin builds only on the Mac so far (Windows comes with M5)")
    native = wt.root / "native"
    started = time.monotonic()
    # Cargo names the artifact itself, wherever CARGO_TARGET_DIR puts it (slow CI's is shared).
    run = subprocess.run(["cargo", "build", "--locked", "--release", "-p", "plugin",
                          "--message-format=json-render-diagnostics"],
                         cwd=native, check=False, stdout=subprocess.PIPE, text=True)
    if run.returncode != 0:
        raise PluginError(f"cargo build -p plugin exited {run.returncode}")
    built = None
    for line in run.stdout.splitlines():
        msg = json.loads(line) if line.startswith("{") else {}
        if msg.get("reason") == "compiler-artifact":
            built = next((Path(f) for f in msg.get("filenames", []) if f.endswith(DYLIB)), built)
    if built is None:
        raise PluginError(f"cargo built no {DYLIB}")
    dest = wt.project / PLUGIN_DIR / DYLIB
    changed = _copy(built, dest)
    _copy(native / "models" / "madmom.bin", wt.project / MODELS_DIR / "madmom.bin")
    print(f"plugin: {'copied' if changed else 'unchanged'} {dest.relative_to(wt.root)} "
          f"in {time.monotonic() - started:.1f} s")
    return dest


def _copy(src: Path, dest: Path) -> bool:
    """Copies `src` over `dest` unless they already match; whether it copied."""
    if dest.exists() and filecmp.cmp(src, dest, shallow=False):
        return False
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".tmp")
    shutil.copy2(src, tmp)
    tmp.replace(dest)
    return True


def cmd_plugin(_a) -> int:
    try:
        install(paths.worktree(name="game"))
        return 0
    except PluginError as e:
        print(f"plugin: {e}", file=sys.stderr)
        return 1
