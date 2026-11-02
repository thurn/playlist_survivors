"""The music plugin as build output (plan.md, What Carries Over: rhythm_champions/plugin/).

Setup and `champions build` build the plugin's Cargo workspace and copy `libps_plugin.dylib` into
Assets/Plugins/macOS/, where a player build picks it up, and the analyzer's models into
Assets/StreamingAssets/ps/, where a player finds them. Both folders are gitignored. Cargo makes it
a no-op when nothing changed, and the copies are rewritten only when their content differs, so an
unchanged plugin never looks like a project change to the build stamp. Until the workspace exists
there is nothing to build.
"""

from __future__ import annotations

import filecmp
import json
import shutil
import subprocess
import time
from pathlib import Path

from . import paths

DYLIB = "libps_plugin.dylib"
PLUGIN_DIR = Path("Assets/Plugins/macOS")
MODELS_DIR = Path("Assets/StreamingAssets/ps")


class PluginError(Exception):
    pass


def install(wt: paths.Worktree) -> Path | None:
    """Builds the plugin and copies it and the models into the project; returns the copy, or
    None while there is no plugin workspace."""
    workspace = wt.project / "plugin"
    if not (workspace / "Cargo.toml").exists():
        return None
    started = time.monotonic()
    # Cargo names the artifact itself, wherever CARGO_TARGET_DIR puts it (slow CI's is shared).
    run = subprocess.run(["cargo", "build", "--locked", "--release", "-p", "plugin",
                          "--message-format=json-render-diagnostics"],
                         cwd=workspace, check=False, stdout=subprocess.PIPE, text=True)
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
    for model in sorted((workspace / "models").glob("*.bin")):
        _copy(model, wt.project / MODELS_DIR / model.name)
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
