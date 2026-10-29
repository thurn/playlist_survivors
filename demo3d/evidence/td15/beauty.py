"""TD15's beauty shots and the readability measure (environment.md, Making it look good).

`uv run --project demo3d/tools python demo3d/evidence/td15/beauty.py [--shots a,b] [--measure-only]`
from the worktree's root, after `demo3d build --backend il2cpp`. It holds TD7's machine lock and
runs the IL2CPP player windowed, in fixed-time mode, three times per shot of arena-layout.json's
`shots`: S3 at demo load with the hero holding the shot's ground (`--hold-at`), its horde at the
shot's `n` and its view width, kept at the shot's `t` (the beauty shot); the same frame with
`--no-effects --capture-mask` (the measure's still, and the same frame again as an ID render of
the monsters, depth-tested against the set); and with `--beauty-empty` (no horde, no combat
effects: the arena, the hero and the ambient particles). Full PNGs go to the gitignored
captures/td15/; small tagged JPEGs (960 px) and readability.md go to demo3d/evidence/td15/.

The readability measure: in the measure's still (the horde without effects, so neither effects
drawn over monsters nor their light count), the monster pixels (the ID render, thresholded) against
the ground around them (a ring 3 to 10 px outside every monster, at 1080p, less any monster pixel).
Both in CIE L* (0 to 100) from the sRGB pixels: dL* is the median monster L* less the median ring
L*, so positive means the horde is brighter than its ground. dL*bg compares the monster pixels with
the same pixels of the empty still (the set they cover, without the fight's light), and dC* the
median chroma (C*ab) likewise against the ring. The ID render draws the set and the hero black with
their depth (the cold review's item 1), so a monster behind a canopy or wall isn't a monster pixel;
the ground, mist, runes, pools and particles are hidden in it. The floor is on dL*.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools import sweep  # noqa: E402

EXE = ROOT / "demo3d" / "Builds" / "il2cpp" / "PlaylistSurvivors3D.exe"
LAYOUT = ROOT / "demo3d" / "Assets" / "Demo3D" / "Choreography" / "Data" / "arena-layout.json"
CAPTURES = ROOT / "captures" / "td15"
EVIDENCE = ROOT / "demo3d" / "evidence" / "td15" / "shots"
TABLE = ROOT / "demo3d" / "evidence" / "td15" / "readability.md"
WARMUP_S = 3.0  # fixed-time frames before the still: effects, the clock and the ambient particles settle
# The floor on dL*: from the authored layout (2x, the cold review's depth-tested ID render, no effects in the
# measured still), its lowest shot less a margin (TD15's notes; it was 12 under the first, undepth-tested measure):
# later changes must not take any shot below it.
FLOOR_DL = 17.0


def shots() -> list[dict]:
    return json.loads(LAYOUT.read_text(encoding="utf-8"))["shots"]


def run(name: str, args: list[str], out: Path) -> None:
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    cmd = [str(EXE), "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0",
           "-logFile", str(out / "player.log"), *args, "--capture-stills", str(out), "--stills-format", "png"]
    print("beauty:", " ".join(cmd), flush=True)
    code = subprocess.run(cmd, cwd=ROOT / "demo3d", timeout=900).returncode
    if code != 0:
        raise SystemExit(f"beauty: {name} exited {code} (see {out / 'player.log'})")


def capture(shot: dict, root: Path, extra: list[str]) -> None:
    x, y = shot["at"]
    t = float(shot.get("t", 20.0))
    common = ["--scene", "S3", "--load", shot.get("load", "demo"), "--n", str(shot.get("n", 2000)),
              "--view-width", str(shot["viewWidthM"]), "--hold-at", f"{x},{y}",
              "--start-at", f"{t - WARMUP_S:.3f}", "--stills-at", f"{t:.3f}"]
    run(shot["name"] + " horde", common + extra, root / shot["name"] / "horde")
    run(shot["name"] + " measure", common + extra + ["--no-effects", "--capture-mask"], root / shot["name"] / "measure")
    run(shot["name"] + " empty", common + extra + ["--beauty-empty"], root / shot["name"] / "empty")


def lstar(rgb: np.ndarray) -> np.ndarray:
    """CIE L* of 8-bit sRGB pixels."""
    c = rgb.astype(np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    y = lin @ np.array([0.2126, 0.7152, 0.0722])
    return np.where(y > 216 / 24389, 116 * np.cbrt(y) - 16, y * 24389 / 27)


def chroma(rgb: np.ndarray) -> np.ndarray:
    """C*ab of 8-bit sRGB pixels (D65)."""
    c = rgb.astype(np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    xyz = lin @ m.T / np.array([0.95047, 1.0, 1.08883])
    f = np.where(xyz > 216 / 24389, np.cbrt(xyz), (24389 / 27 * xyz + 16) / 116)
    a = 500 * (f[..., 0] - f[..., 1])
    b = 200 * (f[..., 1] - f[..., 2])
    return np.hypot(a, b)


def dilate(mask: np.ndarray, r: int) -> np.ndarray:
    """A square dilation by r pixels."""
    out = mask.copy()
    h, w = mask.shape
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            ys, yd = (slice(dy, h), slice(0, h - dy)) if dy >= 0 else (slice(0, h + dy), slice(-dy, h))
            xs, xd = (slice(dx, w), slice(0, w - dx)) if dx >= 0 else (slice(0, w + dx), slice(-dx, w))
            out[yd, xd] |= mask[ys, xs]
    return out


def measure(shot: dict, root: Path) -> dict:
    d = root / shot["name"]
    still = next((d / "horde").glob("still-*.png"))
    measured = next((d / "measure").glob("still-*.png"))
    mask_png = next((d / "measure").glob("mask-*.png"))
    empty = next((d / "empty").glob("still-*.png"))
    horde = np.asarray(Image.open(measured).convert("RGB"))
    bare = np.asarray(Image.open(empty).convert("RGB"))
    mask = np.asarray(Image.open(mask_png).convert("L")) > 127
    ring = dilate(mask, 10) & ~dilate(mask, 3)
    lh, lb, ch = lstar(horde), lstar(bare), chroma(horde)
    n = int(mask.sum())
    if n == 0 or ring.sum() == 0:
        return {"name": shot["name"], "monsterPx": n, "error": "no monster pixels"}
    lm, lg = float(np.median(lh[mask])), float(np.median(lh[ring]))
    return {
        "name": shot["name"], "monsterPx": n, "monsterShare": n / mask.size,
        "lMonster": lm, "lRing": lg, "dL": lm - lg,
        "dLbg": lm - float(np.median(lb[mask])),
        "dC": float(np.median(ch[mask])) - float(np.median(ch[ring])),
        "brighterShare": float((lh[mask] > np.percentile(lh[ring], 75)).mean()),
        "still": still, "empty": empty,
    }


def small_jpeg(src: Path, dst: Path, label: str) -> None:
    """A 960 px JPEG with the ps-render tag (RenderTag's JPEG comment)."""
    im = Image.open(src).convert("RGB").resize((960, 540), Image.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.save(dst, "JPEG", quality=82, comment=f"ps-render TD15 beauty shot {label}".encode())


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--shots", help="comma-separated shot names (default: all)")
    p.add_argument("--measure-only", action="store_true", help="re-measure the kept captures")
    p.add_argument("--tag", default="", help="a trial: captures under captures/td15/<tag>/, nothing written to evidence")
    p.add_argument("--player-args", default="", help="extra player flags for a trial, e.g. '--crowd-fill 1.5'")
    a = p.parse_args()
    chosen = [s for s in shots() if not a.shots or s["name"] in a.shots.split(",")]
    root = CAPTURES / a.tag if a.tag else CAPTURES
    if not a.measure_only:
        root.mkdir(parents=True, exist_ok=True)
        with sweep.machine_lock("TD15 beauty shots"):
            for s in chosen:
                capture(s, root, a.player_args.split())
    rows = [measure(s, root) for s in chosen]
    lines = ["# TD15 readability", "",
             "Generated by `beauty.py`: the horde's monster pixels (ID render) against the ground in a ring 3-10 px",
             "around them, in CIE L*. dL*: median monster L* less median ring L*; dL*bg: against the same pixels",
             "of the empty still; dC*: median chroma, monsters less ring; brighter: monster pixels above the",
             "ring's 75th percentile. The ID render is depth-tested against the set; the measured still has no",
             "effects. Horde: S3 at demo load, n requested (capped by the view's packing), at the shot's t;",
             "1920x1080, MSAA 4x, rung E2 at the authored density.",
             f"Floor: dL* >= {FLOOR_DL:g} in every shot (re-derived after the review: the lowest shot, less a margin).", "",
             "| shot | monster px (share) | L* monster | L* ring | dL* | dL*bg | dC* | brighter | floor |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---|"]
    ok = True
    for r in rows:
        if "error" in r:
            lines.append(f"| {r['name']} | {r['monsterPx']} | | | | | | | {r['error']} |")
            ok = False
            continue
        passed = r["dL"] >= FLOOR_DL
        ok &= passed
        lines.append(f"| {r['name']} | {r['monsterPx']:,} ({r['monsterShare'] * 100:.1f}%) | {r['lMonster']:.1f} | "
                     f"{r['lRing']:.1f} | {r['dL']:.1f} | {r['dLbg']:.1f} | {r['dC']:.1f} | "
                     f"{r['brighterShare'] * 100:.0f}% | {'pass' if passed else 'FAIL'} |")
        if not a.tag:
            small_jpeg(r["still"], EVIDENCE / f"{r['name']}-horde.jpg", f"{r['name']} with the horde")
            small_jpeg(r["empty"], EVIDENCE / f"{r['name']}-empty.jpg", f"{r['name']} empty")
    if not a.tag:
        TABLE.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
