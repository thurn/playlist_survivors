#!/usr/bin/env bash
# Optional one-time setup, macOS Apple Silicon. Pinned open-source tools only.
set -euo pipefail
lod_tools="${SKELETON_LOD_TOOLS:-$HOME/.local/share/skeleton-lod-tools}"
mkdir -p "$lod_tools"
if [[ ! -d "$lod_tools/meshoptimizer-v0.25" ]]; then
  git clone --depth 1 --branch v0.25 https://github.com/zeux/meshoptimizer.git "$lod_tools/meshoptimizer-v0.25"
fi
[[ "$(git -C "$lod_tools/meshoptimizer-v0.25" rev-parse HEAD)" == 6daea4695c48338363b08022d2fb15deaef6ac09 ]]
clang++ -O2 -std=c++11 -dynamiclib "$lod_tools"/meshoptimizer-v0.25/src/*.cpp -o "$lod_tools/libmeshoptimizer-v0.25.dylib"
if [[ ! -d "$lod_tools/Blender-4.5.3.app" ]]; then
  curl -fL https://download.blender.org/release/Blender4.5/blender-4.5.3-macos-arm64.dmg -o "$lod_tools/blender-4.5.3-macos-arm64.dmg"
  echo "73ea841053b55404bb3a71a9a22366f1f8821787fe5c899f8b55a7fff929d01b  $lod_tools/blender-4.5.3-macos-arm64.dmg" | shasum -a 256 -c -
  hdiutil attach "$lod_tools/blender-4.5.3-macos-arm64.dmg" -nobrowse -readonly -mountpoint "$lod_tools/blender-mount"
  trap 'hdiutil detach "$lod_tools/blender-mount"' EXIT
  ditto "$lod_tools/blender-mount/Blender.app" "$lod_tools/Blender-4.5.3.app"
  hdiutil detach "$lod_tools/blender-mount"
  trap - EXIT
fi
"$lod_tools/Blender-4.5.3.app/Contents/MacOS/Blender" --background --version | head -3
