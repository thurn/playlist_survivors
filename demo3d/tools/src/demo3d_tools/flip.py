"""`demo3d flip`: NVIDIA FLIP scores for A/B capture pairs (benchmarks.md, Fidelity A/B Checks).

Pairs the stills of two capture folders by file name and scores each pair with FLIP (LDR,
sRGB): the frame's mean error, and the 99th percentile per pixel, which catches small localized
artifacts. A run's scores are the mean of the frames' means and the largest of their p99s.

    demo3d flip A_DIR B_DIR [--out scores.json]
    demo3d flip calibrate --base A_DIR --invisible B_DIR --visible C_DIR [--out thresholds.json]

A cut passes when both its scores are at or below the invisible anchor's plus a third of the gap
to the visible anchor's (TD11 calibrates the anchors).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

IMAGE_SUFFIXES = (".png", ".jpg", ".jpeg")


def load(path: Path):
    import numpy as np
    from PIL import Image

    with Image.open(path) as im:
        return np.asarray(im.convert("RGB"), dtype=np.float32) / 255.0


def score_pair(a: Path, b: Path) -> dict:
    import flip_evaluator
    import numpy as np

    error, _mean, _params = flip_evaluator.evaluate(load(a), load(b), "LDR", applyMagma=False)
    error = np.asarray(error)
    return {"frame": a.name, "mean": float(error.mean()), "p99": float(np.percentile(error, 99))}


def score_dirs(a_dir: Path, b_dir: Path) -> dict:
    a_files = {p.name: p for p in sorted(a_dir.iterdir()) if p.suffix.lower() in IMAGE_SUFFIXES}
    b_files = {p.name: p for p in sorted(b_dir.iterdir()) if p.suffix.lower() in IMAGE_SUFFIXES}
    common = sorted(set(a_files) & set(b_files))
    if not common:
        raise SystemExit(f"flip: no stills in common between {a_dir} and {b_dir}")
    missing = sorted(set(a_files) ^ set(b_files))
    frames = [score_pair(a_files[n], b_files[n]) for n in common]
    return {
        "a": str(a_dir), "b": str(b_dir), "frames": frames, "unpaired": missing,
        "mean": sum(f["mean"] for f in frames) / len(frames),
        "p99": max(f["p99"] for f in frames),
    }


def thresholds(invisible: dict, visible: dict) -> dict:
    """Pass lines: the invisible anchor plus a third of the gap to the visible one."""
    return {
        "mean": invisible["mean"] + (visible["mean"] - invisible["mean"]) / 3,
        "p99": invisible["p99"] + (visible["p99"] - invisible["p99"]) / 3,
        "invisible": {"mean": invisible["mean"], "p99": invisible["p99"]},
        "visible": {"mean": visible["mean"], "p99": visible["p99"]},
    }


def verdict(scores: dict, lines: dict) -> bool:
    return scores["mean"] <= lines["mean"] and scores["p99"] <= lines["p99"]


def main(argv: list[str]) -> int:
    if argv and argv[0] == "calibrate":
        p = argparse.ArgumentParser(prog="demo3d flip calibrate")
        p.add_argument("--base", required=True)
        p.add_argument("--invisible", required=True)
        p.add_argument("--visible", required=True)
        p.add_argument("--out")
        a = p.parse_args(argv[1:])
        inv = score_dirs(Path(a.base), Path(a.invisible))
        vis = score_dirs(Path(a.base), Path(a.visible))
        result = thresholds(inv, vis)
        print(f"flip: invisible anchor mean {inv['mean']:.5f} p99 {inv['p99']:.4f}; visible anchor "
              f"mean {vis['mean']:.5f} p99 {vis['p99']:.4f}")
        print(f"flip: a cut passes at mean <= {result['mean']:.5f} and p99 <= {result['p99']:.4f}")
    else:
        p = argparse.ArgumentParser(prog="demo3d flip")
        p.add_argument("a")
        p.add_argument("b")
        p.add_argument("--out")
        p.add_argument("--thresholds", help="a calibrate --out file, to print a verdict")
        a = p.parse_args(argv)
        result = score_dirs(Path(a.a), Path(a.b))
        line = f"flip: {len(result['frames'])} frames, mean {result['mean']:.5f}, p99 {result['p99']:.4f}"
        if a.thresholds:
            lines = json.loads(Path(a.thresholds).read_text(encoding="utf-8"))
            result["pass"] = verdict(result, lines)
            line += f" (pass <= {lines['mean']:.5f} / {lines['p99']:.4f}): {'PASS' if result['pass'] else 'FAIL'}"
        print(line)
    if a.out:
        Path(a.out).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
