"""`demo3d look SLUG [--baseline SLUG]`: the look pass's stills and numbers on the Mac (ps-e8r.1).

Renders the look set with the windowless batch-mode player (mac.md), in fixed time, one player
process per capture, a few at a time. It builds the player first (BakeAll too if the worktree isn't
baked) when there is none, or with --build after a change:
- song stills at SONG_TIMES (one each in groove, drop-1, groove-2, drop-2 and the final peak),
  each from WARMUP_S before it;
- TD15's six beauty shots (arena-layout.json's `shots`), three captures each as in
  evidence/td15/beauty.py: the horde; the same frame with `--no-effects --capture-mask` (the
  measured still and its ID render of the monsters); and `--beauty-empty`.

Full PNGs go to the gitignored captures/look/SLUG/. Small ps-render-tagged JPEGs (960 px), look.md
and, given --baseline, a before/after contact sheet go to demo3d/evidence/look/SLUG/.

look.md has, for every still: CIE L* p2/p50/p98, the RMS contrast (the standard deviation of L*),
the mean chroma C*ab, the near-black share (L* < 10) and the clipped share (every channel >= 250).
For beauty shots it adds the monster pixels' L* p10/p90, the effects (the pixels whose L* the
effects move by more than EFFECT_DL, horde still against measured still) and readability.

Readability is TD15's measure, made absolute (the look epic's Decision 3): in the measured still,
the monster pixels (the ID render) against the ground in a ring 3-10 px around them, in L*. dL* is
the median monster L* less the median ring L*; the floor is |dL*| >= FLOOR_DL, so a dark horde on a
light ground reads as well as a light horde on a dark one.

The stage (ps-e8r.5's lit pool around the hero) is measured in every still that has the player's
view-<t>.json (its camera's view-projection and the hero's feet): the median L* of the ground within
STAGE_R_M of the hero (each pixel's view ray met with the ground plane at the hero's feet, less the
monsters of the ID render and the hero's own STAGE_HERO_M) less the median L* of the frame's outer
STAGE_BORDER border. The bead's bar is STAGE_FLOOR.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from . import paths

SONG_TIMES = (25.0, 60.0, 100.0, 145.0, 175.0)  # groove, drop-1, groove-2, drop-2, final peak
WARMUP_S = 5.0  # fixed-time seconds before each still: effects, the camera's feel and particles settle
SHOT_WARMUP_S = 3.0  # beauty.py's
FLOOR_DL = 17.0  # TD15's floor, now on |dL*|
EFFECT_DL = 5.0  # an effect pixel: the effects move its L* by more than this
STAGE_R_M, STAGE_HERO_M = 6.0, 1.2  # the stage's ground: within 6 m of the hero, past its own body
STAGE_BORDER = 0.15  # the frame's outer 15% on every side
STAGE_FLOOR = 15.0  # ps-e8r.5: the stage's median L* above the border's
JPEG_W, JPEG_H = 960, 540
APP = "PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"


# --- Stats -------------------------------------------------------------------------------------

def _linear(rgb: np.ndarray) -> np.ndarray:
    c = rgb.astype(np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _f(t: np.ndarray) -> np.ndarray:
    return np.where(t > 216 / 24389, np.cbrt(t), (24389 / 27 * t + 16) / 116)


def lstar(rgb: np.ndarray) -> np.ndarray:
    """CIE L* (0-100) of 8-bit sRGB pixels."""
    y = _linear(rgb) @ np.array([0.2126, 0.7152, 0.0722])
    return 116 * _f(y) - 16


def chroma(rgb: np.ndarray) -> np.ndarray:
    """C*ab of 8-bit sRGB pixels (D65)."""
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    f = _f(_linear(rgb) @ m.T / np.array([0.95047, 1.0, 1.08883]))
    return np.hypot(500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2]))


def still_stats(rgb: np.ndarray) -> dict:
    """The whole frame's look stats."""
    lum = lstar(rgb)
    p2, p50, p98 = np.percentile(lum, [2, 50, 98])
    return {"p2": float(p2), "p50": float(p50), "p98": float(p98), "contrast": float(lum.std()),
            "chroma": float(chroma(rgb).mean()), "nearBlack": float((lum < 10).mean()),
            "clipped": float((rgb >= 250).all(axis=-1).mean())}


def dilate(mask: np.ndarray, r: int) -> np.ndarray:
    """A square dilation by r pixels (separable: rows, then columns)."""
    out = mask.copy()
    for axis in (0, 1):
        src, n = out.copy(), mask.shape[axis]
        for d in range(1, r + 1):
            lo, hi = [slice(None)] * 2, [slice(None)] * 2
            lo[axis], hi[axis] = slice(0, n - d), slice(d, n)
            out[tuple(lo)] |= src[tuple(hi)]
            out[tuple(hi)] |= src[tuple(lo)]
    return out


def readability(measured: np.ndarray, mask: np.ndarray, empty: np.ndarray) -> dict:
    """TD15's measure on the measured still, its ID mask (bool) and the empty still."""
    n = int(mask.sum())
    ring = dilate(mask, 10) & ~dilate(mask, 3)
    if n == 0 or not ring.any():
        return {"monsterPx": n, "error": "no monster pixels"}
    lh, ch = lstar(measured), chroma(measured)
    lm, lg = float(np.median(lh[mask])), float(np.median(lh[ring]))
    p10, p90 = np.percentile(lh[mask], [10, 90])
    return {"monsterPx": n, "monsterShare": n / mask.size, "lMonster": lm, "lRing": lg,
            "dL": lm - lg, "dLbg": lm - float(np.median(lstar(empty)[mask])),
            "dC": float(np.median(ch[mask])) - float(np.median(ch[ring])),
            "monsterP10": float(p10), "monsterP90": float(p90),
            "pass": abs(lm - lg) >= FLOOR_DL}


def ground_distance(view: dict, w: int, h: int) -> np.ndarray:
    """Each pixel's distance (m) from the hero's feet along the ground plane through them; inf off it."""
    inv = np.linalg.inv(np.array(view["viewProj"], dtype=np.float64).reshape(4, 4))
    hero = np.array(view["hero"], dtype=np.float64)
    x = (np.arange(w) + 0.5) / w * 2 - 1
    y = 1 - (np.arange(h) + 0.5) / h * 2  # the stills' first row is the top: clip-space y = +1
    xx, yy = np.meshgrid(x, y)
    def unproject(z: float) -> np.ndarray:
        p = np.stack([xx, yy, np.full_like(xx, z), np.ones_like(xx)], axis=-1) @ inv.T
        return p[..., :3] / p[..., 3:]
    near, far = unproject(-1.0), unproject(1.0)
    d = far - near
    with np.errstate(divide="ignore", invalid="ignore"):
        k = (hero[1] - near[..., 1]) / d[..., 1]
    hit = near + k[..., None] * d
    dist = np.hypot(hit[..., 0] - hero[0], hit[..., 2] - hero[2])
    return np.where((k > 0) & np.isfinite(k), dist, np.inf)


def stage(rgb: np.ndarray, view: dict, monsters: np.ndarray | None) -> dict:
    """The lit stage: the ground's median L* within STAGE_R_M of the hero against the frame's border."""
    h, w = rgb.shape[:2]
    dist = ground_distance(view, w, h)
    ground = (dist <= STAGE_R_M) & (dist > STAGE_HERO_M)
    if monsters is not None:
        ground &= ~dilate(monsters, 2)
    border = np.ones((h, w), dtype=bool)
    bh, bw = int(round(h * STAGE_BORDER)), int(round(w * STAGE_BORDER))
    border[bh:h - bh, bw:w - bw] = False
    if not ground.any():
        return {"groundPx": 0, "error": "no ground near the hero"}
    lum = lstar(rgb)
    lg, lb = float(np.median(lum[ground])), float(np.median(lum[border]))
    return {"groundPx": int(ground.sum()), "lGround": lg, "lBorder": lb, "dL": lg - lb, "pass": lg - lb >= STAGE_FLOOR}


def effect_stats(horde: np.ndarray, measured: np.ndarray) -> dict:
    """The effects: the pixels whose L* they move by more than EFFECT_DL (horde vs no-effects still)."""
    lh = lstar(horde)
    fx = np.abs(lh - lstar(measured)) > EFFECT_DL
    if not fx.any():
        return {"share": 0.0}
    p50, p98 = np.percentile(lh[fx], [50, 98])
    return {"share": float(fx.mean()), "p50": float(p50), "p98": float(p98),
            "chroma": float(chroma(horde[fx]).mean()),
            "clipped": float((horde[fx] >= 250).all(axis=-1).mean())}


# --- Capture -----------------------------------------------------------------------------------

@dataclass
class Capture:
    name: str  # e.g. song-060, opening-horde
    args: list[str]
    out: Path = field(default=Path())


def player(wt: paths.Worktree, backend: str) -> Path:
    return wt.project / "Builds" / "mac" / backend / APP


def plan(root: Path, shots: list[dict], song: bool = True, beauty: bool = True) -> list[Capture]:
    caps: list[Capture] = []
    if song:
        for t in SONG_TIMES:
            caps.append(Capture(f"song-{t:03.0f}", ["--start-at", f"{t - WARMUP_S:.3f}", "--stills-at", f"{t:.3f}",
                                                    "--capture-mask"]))  # the mask keeps monsters out of the stage's ground
    if beauty:
        for s in shots:
            x, y = s["at"]
            t = float(s.get("t", 20.0))
            common = ["--scene", "S3", "--load", s.get("load", "demo"), "--n", str(s.get("n", 2000)),
                      "--view-width", str(s["viewWidthM"]), "--hold-at", f"{x},{y}",
                      "--start-at", f"{t - SHOT_WARMUP_S:.3f}", "--stills-at", f"{t:.3f}"]
            caps.append(Capture(f"{s['name']}-horde", common))
            caps.append(Capture(f"{s['name']}-measure", common + ["--no-effects", "--capture-mask"]))
            caps.append(Capture(f"{s['name']}-empty", common + ["--beauty-empty"]))
    for c in caps:
        c.out = root / c.name
    return caps


def run_capture(exe: Path, cwd: Path, cap: Capture, extra: list[str]) -> float:
    if cap.out.exists():
        shutil.rmtree(cap.out)
    cap.out.mkdir(parents=True)
    cmd = [str(exe), "-batchmode", "-force-metal", "-logFile", str(cap.out / "player.log"), "--fixed-time",
           *cap.args, *extra, "--capture-stills", str(cap.out), "--stills-format", "png"]
    started = time.monotonic()
    code = subprocess.run(cmd, check=False, cwd=cwd, timeout=1200, stdout=subprocess.DEVNULL).returncode
    took = time.monotonic() - started
    if code != 0 or not list(cap.out.glob("still-*.png")):
        raise SystemExit(f"look: {cap.name} exited {code} with no still (see {cap.out / 'player.log'})")
    print(f"look: {cap.name} in {took:.0f} s", flush=True)
    return took


# --- Output ------------------------------------------------------------------------------------

def load_rgb(path: Path) -> np.ndarray:
    with Image.open(path) as im:
        return np.asarray(im.convert("RGB"))


def small_jpeg(src: Path, dst: Path, label: str) -> None:
    """A 960 px JPEG with the ps-render tag (RenderTag's JPEG comment), which the vendor guard accepts."""
    with Image.open(src) as im:
        small = im.convert("RGB").resize((JPEG_W, JPEG_H), Image.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    small.save(dst, "JPEG", quality=82, comment=f"ps-render look harness {label}".encode())


def contact_sheet(names: list[str], before: Path, after: Path, dst: Path, labels: tuple[str, str]) -> None:
    """Rows of before | after, at half the evidence JPEGs' size."""
    w, h, pad = JPEG_W // 2, JPEG_H // 2, 18
    rows = [n for n in names if (before / f"{n}.jpg").exists() and (after / f"{n}.jpg").exists()]
    sheet = Image.new("RGB", (2 * w, len(rows) * (pad + h)), (16, 16, 16))
    draw = ImageDraw.Draw(sheet)
    for i, n in enumerate(rows):
        y = i * (pad + h)
        for j, src in enumerate((before, after)):
            draw.text((j * w + 4, y + 3), f"{n}: {labels[j]}", fill=(220, 220, 220))
            with Image.open(src / f"{n}.jpg") as im:
                sheet.paste(im.convert("RGB").resize((w, h), Image.LANCZOS), (j * w, y + pad))
    sheet.save(dst, "JPEG", quality=85, comment=b"ps-render look harness contact sheet")


def _pct(v: float) -> str:
    return f"{v * 100:.1f}%"


def stats_row(name: str, s: dict) -> str:
    return (f"| {name} | {s['p2']:.1f} | {s['p50']:.1f} | {s['p98']:.1f} | {s['contrast']:.1f} | "
            f"{s['chroma']:.1f} | {_pct(s['nearBlack'])} | {_pct(s['clipped'])} |")


STATS_HEAD = ["| still | L* p2 | L* p50 | L* p98 | RMS contrast | mean C*ab | near-black | clipped |",
              "|---|---:|---:|---:|---:|---:|---:|---:|"]


def report(slug: str, song: dict[str, dict], shots: dict[str, dict], baseline: dict | None,
           stages: dict[str, dict] | None = None) -> str:
    lines = [f"# Look set: {slug}", "",
             "Generated by `demo3d look` (demo3d/tools/src/demo3d_tools/look.py). Stills: 1920x1080 PNGs,",
             "windowless batch-mode player in fixed time. L* is CIE L* (0-100) of the sRGB pixels; RMS contrast",
             "is the standard deviation of L*; near-black is L* < 10; clipped is every channel >= 250.", "",
             "## Song stills", "",
             f"Song mode, each from {WARMUP_S:g} s before its time: "
             + ", ".join(f"{t:g} s" for t in SONG_TIMES)
             + " (groove, drop-1, groove-2, drop-2, final peak).", "", *STATS_HEAD]
    lines += [stats_row(n, s) for n, s in song.items()]
    lines += ["", "## Beauty shots", "",
              "TD15's six shots (arena-layout.json `shots`): S3 at demo load holding the shot's ground, its horde",
              "at the shot's n and view width, at its t. Stats of the horde still (with effects).", "", *STATS_HEAD]
    lines += [stats_row(n, s["horde"]) for n, s in shots.items()]
    lines += ["", "## Readability", "",
              "In the measured still (the horde without effects): the monster pixels (the ID render, depth-tested",
              "against the set) against the ground in a ring 3-10 px around them. dL*: median monster L* less median",
              "ring L*; dL*bg: against the same pixels of the empty still; dC*: median chroma, monsters less ring;",
              f"monster p10/p90: the monster pixels' L*. Floor: |dL*| >= {FLOOR_DL:g} in every shot (epic ps-e8r,",
              "Decision 3).", "",
              "| shot | monster px (share) | L* monster | L* ring | dL* | dL*bg | dC* | monster p10 | p90 | floor |",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|---|"]
    for n, s in shots.items():
        r = s["read"]
        if "error" in r:
            lines.append(f"| {n} | {r['monsterPx']} | | | | | | | | {r['error']} |")
            continue
        lines.append(f"| {n} | {r['monsterPx']:,} ({_pct(r['monsterShare'])}) | {r['lMonster']:.1f} | "
                     f"{r['lRing']:.1f} | {r['dL']:.1f} | {r['dLbg']:.1f} | {r['dC']:.1f} | "
                     f"{r['monsterP10']:.1f} | {r['monsterP90']:.1f} | {'pass' if r['pass'] else 'FAIL'} |")
    lines += ["", "## Effects", "",
              f"The pixels whose L* the effects move by more than {EFFECT_DL:g} (the horde still against the",
              "measured still): their share of the frame, L* p50/p98 and mean C*ab in the horde still, and the",
              "share of them clipped.", "",
              "| shot | effect px | L* p50 | L* p98 | mean C*ab | clipped |", "|---|---:|---:|---:|---:|---:|"]
    for n, s in shots.items():
        e = s["fx"]
        if e["share"] == 0:
            lines.append(f"| {n} | 0 | | | | |")
        else:
            lines.append(f"| {n} | {_pct(e['share'])} | {e['p50']:.1f} | {e['p98']:.1f} | "
                         f"{e['chroma']:.1f} | {_pct(e['clipped'])} |")
    if stages:
        lines += ["", "## Stage", "",
                  f"The lit stage around the hero (ps-e8r.5): the median L* of the ground within {STAGE_R_M:g} m of the",
                  f"hero (past its own {STAGE_HERO_M:g} m, monsters masked out; beauty shots: the horde still) against",
                  f"the median L* of the frame's outer {STAGE_BORDER * 100:g}% border. Bar: dL* >= {STAGE_FLOOR:g} in every song still.",
                  "", "| still | ground px | L* ground | L* border | dL* | bar |", "|---|---:|---:|---:|---:|---|"]
        for n, st in stages.items():
            if "error" in st:
                lines.append(f"| {n} | 0 | | | | {st['error']} |")
                continue
            lines.append(f"| {n} | {st['groundPx']:,} | {st['lGround']:.1f} | {st['lBorder']:.1f} | {st['dL']:.1f} | "
                         f"{'pass' if st['pass'] else 'FAIL'} |")
    if baseline:
        lines += ["", f"## Against {baseline['slug']}", "",
                  "Song stills and beauty shots (horde stills); deltas are this set less the baseline.", "",
                  "| still | mean C*ab | delta | RMS contrast | delta | near-black | delta | |dL*| | delta |",
                  "|---|---:|---:|---:|---:|---:|---:|---:|---:|"]
        now = {**song, **{n: s["horde"] for n, s in shots.items()}}
        for n, s in now.items():
            b = baseline["stats"].get(n)
            if b is None:
                continue
            dl = shots[n]["read"].get("dL") if n in shots else None
            bdl = baseline["dL"].get(n)
            dl_cells = (f"{abs(dl):.1f} | {abs(dl) - abs(bdl):+.1f}" if dl is not None and bdl is not None
                        else " | ")
            lines.append(f"| {n} | {s['chroma']:.1f} | {s['chroma'] - b['chroma']:+.1f} | {s['contrast']:.1f} | "
                         f"{s['contrast'] - b['contrast']:+.1f} | {_pct(s['nearBlack'])} | "
                         f"{(s['nearBlack'] - b['nearBlack']) * 100:+.1f} | {dl_cells} |")
        lines += ["", "[contact-sheet.jpg](contact-sheet.jpg): the baseline left, this set right."]
    return "\n".join(lines) + "\n"


# --- Command -----------------------------------------------------------------------------------

def cmd_look(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    slug = a.slug
    captures = wt.root / "captures" / "look" / slug
    evidence = wt.project / "evidence" / "look" / slug
    shots = json.loads((wt.project / "Assets" / "Demo3D" / "Choreography" / "Data" / "arena-layout.json")
                       .read_text(encoding="utf-8"))["shots"]
    caps = plan(captures, shots, song=not a.beauty_only, beauty=not a.song_only)
    if not a.measure_only:
        exe = player(wt, a.backend)
        if a.build or not exe.exists():
            from .cli import cmd_build

            build = argparse.Namespace(project="demo3d", platform="mac", backend=a.backend, out=None,
                                       development=False, scene=None, cold=False)
            if cmd_build(build) != 0:
                print("look: the player build failed", file=sys.stderr)
                return 2
        extra = a.player_args.split()
        started = time.monotonic()
        with ThreadPoolExecutor(max_workers=a.workers) as pool:
            took = list(pool.map(lambda c: run_capture(exe, wt.project, c, extra), caps))
        print(f"look: {len(caps)} captures in {time.monotonic() - started:.0f} s "
              f"({sum(took):.0f} s of player time, {a.workers} at a time)", flush=True)

    def still(name: str) -> Path:
        return next((captures / name).glob("still-*.png"))

    song: dict[str, dict] = {}
    shot_rows: dict[str, dict] = {}
    def mask(name: str) -> np.ndarray | None:
        m = next((captures / name).glob("mask-*.png"), None)
        return load_rgb(m)[..., 0] > 127 if m else None

    def stage_of(name: str, monsters: np.ndarray | None) -> dict | None:
        v = next((captures / name).glob("view-*.json"), None)  # sets captured before ps-e8r.5 have none
        return stage(load_rgb(still(name)), json.loads(v.read_text(encoding="utf-8")), monsters) if v else None

    stages: dict[str, dict] = {}
    for c in caps:
        if c.name.startswith("song-"):
            song[c.name] = still_stats(load_rgb(still(c.name)))
            if (st := stage_of(c.name, mask(c.name))) is not None:
                stages[c.name] = st
    for s in shots if not a.song_only else []:
        n = s["name"]
        horde, measured, empty = (load_rgb(still(f"{n}-{k}")) for k in ("horde", "measure", "empty"))
        monsters = mask(f"{n}-measure")
        shot_rows[n] = {"horde": still_stats(horde), "read": readability(measured, monsters, empty),
                        "fx": effect_stats(horde, measured)}
        if (st := stage_of(f"{n}-horde", monsters)) is not None:
            stages[n] = st

    if a.trial:
        print(report(slug, song, shot_rows, None, stages))
        return 0
    evidence.mkdir(parents=True, exist_ok=True)
    names = list(song) + [f"{n}-{k}" for n in shot_rows for k in ("horde", "empty")]
    for n in names:
        small_jpeg(still(n), evidence / f"{n}.jpg", n)
    numbers = {"slug": slug, "stats": {**song, **{n: s["horde"] for n, s in shot_rows.items()}},
               "dL": {n: s["read"].get("dL") for n, s in shot_rows.items()},
               "stage": {n: st.get("dL") for n, st in stages.items()}}
    (evidence / "look.json").write_text(json.dumps(numbers, indent=1) + "\n", encoding="utf-8")
    base = None
    if a.baseline:
        bdir = wt.project / "evidence" / "look" / a.baseline
        base = json.loads((bdir / "look.json").read_text(encoding="utf-8"))
        contact_sheet(names, bdir, evidence, evidence / "contact-sheet.jpg", (a.baseline, slug))
    text = report(slug, song, shot_rows, base, stages)
    (evidence / "look.md").write_text(text, encoding="utf-8")
    print(text)
    return 0 if all(s["read"].get("pass") for s in shot_rows.values()) else 1
