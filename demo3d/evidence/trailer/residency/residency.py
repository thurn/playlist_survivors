"""ps-hgs.1's numbers: four environments resident in one player (trailer.md, Environments in one player).

From the worktree's root, after `demo3d build` and the captures the README lists:

    uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py compare SET BASE
    uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py final SET
    uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py timing PROBE.json...
    uv run --project demo3d/tools python demo3d/evidence/trailer/residency/residency.py jpegs DIR

- `compare`: two look sets kept in captures/look/ (`demo3d look SLUG --trial`), still by still: the frame's
  L* p50 and mean in each, their differences, and the mean per-pixel |dL*|. Exits 1 if a median or mean
  differs by more than 1 L* (the look harness's pass line).
- `final`: a kept look set's L* p2/p50/p98 against evidence/look/final/look.json's; exits 1 past 1 L* at p50.
- `timing`: `--gpu-probe-at` outputs (BatchGpuProbe's per-frame `frames`), each frame's draw and switch
  times, and each switch frame against its neighbours' mean.
- `jpegs`: every still-*.png in DIR as a tagged 960 px JPEG in this folder (the look harness's small_jpeg).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools.look import load_rgb, lstar, small_jpeg  # noqa: E402

HERE = Path(__file__).resolve().parent
LOOK = ROOT / "captures" / "look"
PASS_DL = 1.0


def still(d: Path) -> Path:
    return next(d.glob("still-*.png"))


def names(d: Path) -> list[str]:
    return sorted(p.name for p in d.iterdir() if p.is_dir() and any(p.glob("still-*.png")))


def cmd_compare(a: argparse.Namespace) -> int:
    rows, worst = [], 0.0
    print(f"| still | L* p50 {a.set} | {a.base} | dp50 | L* mean {a.set} | {a.base} | dmean | mean px abs dL* |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")
    for n in names(LOOK / a.set):
        if not (LOOK / a.base / n).is_dir():
            continue
        x, y = lstar(load_rgb(still(LOOK / a.set / n))), lstar(load_rgb(still(LOOK / a.base / n)))
        px, py = float(np.median(x)), float(np.median(y))
        mx, my = float(x.mean()), float(y.mean())
        worst = max(worst, abs(px - py), abs(mx - my))
        rows.append(n)
        print(f"| {n} | {px:.2f} | {py:.2f} | {px - py:+.2f} | {mx:.2f} | {my:.2f} | {mx - my:+.2f} | {float(np.abs(x - y).mean()):.2f} |")
    print(f"\n{len(rows)} stills; the largest median or mean difference is {worst:.2f} L* (pass: <= {PASS_DL:g})")
    return 0 if rows and worst <= PASS_DL else 1


def cmd_final(a: argparse.Namespace) -> int:
    final = json.loads((ROOT / "demo3d" / "evidence" / "look" / "final" / "look.json").read_text(encoding="utf-8"))["stats"]
    worst = 0.0
    print(f"| still | p2 | final | p50 | final | dp50 | p98 | final |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|")
    for n, f in final.items():
        d = LOOK / a.set / (n if (LOOK / a.set / n).is_dir() else f"{n}-horde")
        if not d.is_dir():
            continue
        p2, p50, p98 = np.percentile(lstar(load_rgb(still(d))), [2, 50, 98])
        worst = max(worst, abs(p50 - f["p50"]))
        print(f"| {n} | {p2:.2f} | {f['p2']:.2f} | {p50:.2f} | {f['p50']:.2f} | {p50 - f['p50']:+.2f} | {p98:.2f} | {f['p98']:.2f} |")
    print(f"\nthe largest p50 difference is {worst:.2f} L* (pass: <= {PASS_DL:g})")
    return 0 if worst <= PASS_DL else 1


def cmd_timing(a: argparse.Namespace) -> int:
    worst = -1e9
    for path in a.probes:
        points = json.loads(Path(path).read_text(encoding="utf-8"))["points"]
        print(f"\n{path}:\n\n| at | song time | draw ms | switch ms | frame ms |\n|---:|---:|---:|---:|---:|")
        for p in points:
            frames = [(t, ms, sw, ms + sw) for t, ms, sw in p["frames"]]
            for t, ms, sw, total in frames:
                print(f"| {p['at']:.3f} | {t:.4f} | {ms:.2f} | {sw:.2f} | {total:.2f} |")
            for i, (t, _, sw, total) in enumerate(frames):
                others = [f[3] for j, f in enumerate(frames) if j != i]
                if sw > 0 and others:
                    over = total - sum(others) / len(others)
                    worst = max(worst, over)
                    print(f"| | switch at {t:.4f} | | | {over:+.2f} vs neighbours |")
    if worst > -1e9:
        print(f"\nthe slowest switch frame is {worst:+.2f} ms against its neighbours' mean (pass: <= 2)")
    return 0 if worst <= 2 else 1


def cmd_jpegs(a: argparse.Namespace) -> int:
    for p in sorted(Path(a.dir).glob("still-*.png")):
        dst = HERE / (a.prefix + p.stem.removeprefix("still-") + ".jpg")
        small_jpeg(p, dst, f"{a.prefix}{p.stem}")
        print(dst.relative_to(ROOT))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("compare")
    c.add_argument("set")
    c.add_argument("base")
    c.set_defaults(fn=cmd_compare)
    f = sub.add_parser("final")
    f.add_argument("set")
    f.set_defaults(fn=cmd_final)
    t = sub.add_parser("timing")
    t.add_argument("probes", nargs="+")
    t.set_defaults(fn=cmd_timing)
    j = sub.add_parser("jpegs")
    j.add_argument("dir")
    j.add_argument("--prefix", default="")
    j.set_defaults(fn=cmd_jpegs)
    a = ap.parse_args()
    return a.fn(a)


if __name__ == "__main__":
    sys.exit(main())
