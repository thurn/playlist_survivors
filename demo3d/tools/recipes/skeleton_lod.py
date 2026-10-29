"""Mac-only entry points. From repo root: uv run --directory demo3d/tools python recipes/skeleton_lod.py <step>."""
from __future__ import annotations

import argparse
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "demo3d"
RECIPES = Path(__file__).parent
UNITY = Path("/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity")


def unity(step, extra):
    methods = {"export": "SkeletonLodExperiment.Export", "scene": "SkeletonLodPreviewBuilder.Scene",
               "capture": "SkeletonLodPreviewBuilder.Capture", "details": "SkeletonLodPreviewBuilder.CaptureDetails",
               "quality": "SkeletonLodQualityAssessment.Run",
               "build": "SkeletonLodPreviewBuilder.BuildMac"}
    # Unity rewrites runtime resources in the global settings even on a read-only
    # import. Keep all temporary platform/build settings local and restore bytes.
    tracked = subprocess.check_output(["git", "ls-files", "demo3d/ProjectSettings", "demo3d/Assets/Demo3D/Settings", "demo3d/Assets/UniversalRenderPipelineGlobalSettings.asset"], cwd=ROOT, text=True).splitlines()
    settings = {ROOT / p: (ROOT / p).read_bytes() for p in tracked}
    logs = PROJECT / "Logs"
    logs.mkdir(exist_ok=True)
    cmd = [str(UNITY), "-batchmode", "-force-metal", "-buildTarget", "OSXUniversal", "-projectPath", str(PROJECT),
           "-logFile", str(logs / f"skeleton-{step}.log")]
    if step == "test":
        cmd += ["-runTests", "-testPlatform", "EditMode", "-testResults", str(logs / "skeleton-editmode.xml")]
    else:
        cmd += ["-executeMethod", "Demo3D.EditorTools.Crowd." + methods[step], "-quit"]
    print("Unity", step, "→", logs / f"skeleton-{step}.log", flush=True)
    try:
        subprocess.run(cmd + extra, cwd=PROJECT, check=True)
    finally:
        for path, contents in settings.items():
            path.write_bytes(contents)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("step", choices=["export", "a", "b", "b-match", "scene", "capture", "details", "quality", "cost", "build", "test"])
    args, extra = parser.parse_known_args()
    if args.step == "a":
        subprocess.run([str(PROJECT / "tools/.venv/bin/python"), str(RECIPES / "skeleton_lod_a.py")], cwd=ROOT, check=True)
    elif args.step in ("b", "b-match"):
        tools = Path(os.environ.get("SKELETON_LOD_TOOLS", Path.home() / ".local/share/skeleton-lod-tools"))
        subprocess.run([str(tools / "Blender-4.5.3.app/Contents/MacOS/Blender"), "--background", "--factory-startup",
                        "--python-exit-code", "1", "--python", str(RECIPES / ("skeleton_lod_b_match.py" if args.step == "b-match" else "skeleton_lod_b.py"))], cwd=ROOT, check=True)
    elif args.step == "cost":
        subprocess.run([str(PROJECT / "Builds/SkeletonLodPreview.app/Contents/MacOS/Playlist Survivors 3D Demo"),
                        "-batchmode", "-force-metal", "-logFile", str(PROJECT / "Logs/skeleton-cost.log"),
                        "--lod-cost", *extra], cwd=PROJECT, check=True)
    else:
        unity(args.step, extra)


if __name__ == "__main__":
    main()
