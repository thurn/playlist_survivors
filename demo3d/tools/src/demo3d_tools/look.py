"""`demo3d look SLUG [--baseline SLUG]`: the look pass's stills and numbers on the Mac (ps-e8r.1).

Renders the look set with the windowless batch-mode player (mac.md), in fixed time, one player
process per capture, a few at a time. It builds the player first (BakeAll too if the bake is missing or
stale) when there is none, or with --build after a change:
- song stills at SONG_TIMES (one each in groove, drop-1, groove-2, drop-2 and the final peak),
  each from WARMUP_S before it, and the same frame with `--no-effects` (song-<t>-measure);
- TD15's six beauty shots (arena-layout.json's `shots`), three captures each as in
  evidence/td15/beauty.py: the horde; the same frame with `--no-effects --capture-mask` (the
  measured still and its ID render of the monsters); and `--beauty-empty`. All three run with
  `--no-post-feel`, the post stack at its steady state (ps-e8r.18), so a beat's pulse or an
  accent's flash never moves the readability measure or the effect and stage numbers between beads.

Full PNGs go to the gitignored captures/look/SLUG/. Small ps-render-tagged JPEGs (960 px), look.md
and, given --baseline, a before/after contact sheet go to demo3d/evidence/look/SLUG/.

look.md has, for every still: CIE L* p2/p50/p98, the RMS contrast (the standard deviation of L*),
the mean chroma C*ab, the near-black share (L* < 10), the clipped share (every channel >= 250) and
the largest connected clipped region's share (a blown-out blob, not scattered hot pixels).
It adds the cool accents' share (ps-e8r.10: HSV hue in COOL_HUE, C*ab above COOL_CHROMA) and, for every
still, the effects (the pixels whose L* the effects move by more than EFFECT_DL, against the no-effects
still) with the other pixels' L* p98, since effects should be the brightest values. For beauty shots it
adds the monster pixels' L* p10/p90 and mean chroma and readability.

Readability is TD15's measure, made absolute (the look epic's Decision 3): in the measured still,
the monster pixels (the ID render) against the ground in a ring 3-10 px around them, in L*. dL* is
the median monster L* less the median ring L*; the floor is |dL*| >= FLOOR_DL, so a dark horde on a
light ground reads as well as a light horde on a dark one.

The horde against the structures (epic ps-suy, Decision 4): the ID render also marks the arena's structures
(ArenaRuntime.Structures, the pieces `--structure-style` restyles) in blue. In the measured still, the monster
pixels against the structure pixels in a ring STRUCTURE_RING px around them: dL* is the median monster L* less
their median, reported when the ring holds at least STRUCTURE_MIN_PX structure pixels. A self-lit set must not
match the horde it frames. `--evidence DIR` writes a set's evidence under demo3d/evidence/DIR instead of
look/SLUG (the structure styles' sets: structures/looks/<environment>-<style>).

Readability by type (ps-bop.4, the environments' comparison): the ID render marks every monster alike, so in the
measured still each colourful monster pixel (C*ab above TYPE_CHROMA) is classed by the roster.json signature hue
nearest its HSV hue, and each type's pixels are measured against a ring around them as readability's are: dL*,
and how much of that ring lies in the type's own hue (a hue collision's background). It is a colour estimate:
a type lit or graded into a neighbour's 40-degree slot counts as the neighbour.

The stage (ps-e8r.5's lit pool around the hero) is measured in every still that has the player's
view-<t>.json (its camera's view-projection and the hero's feet): the median L* of the ground within
STAGE_R_M of the hero (each pixel's view ray met with the ground plane at the hero's feet, less the
monsters of the ID render and the hero's own STAGE_HERO_M) less the median L* of the frame's outer
STAGE_BORDER border. The bead's bar is STAGE_FLOOR.

The ground (ps-e8r.6) is measured in the empty beauty shots, from the same view-<t>.json: each ground
pixel is classed by ArenaLayout.FightingDistance as fighting ground (inside a clearing or path) or
outer (the edge bands and forest). look.md has each class's median L* and the fighting ground's
local detail, the mean standard deviation of L* in GROUND_WINDOW px windows.

`look-metrics DIR --palette P.json` (ps-wnj.2, ps-wnj.21) measures any directory of stills, demo3d's, game's or frames
cut from a capture, by the style guide's rules (docs/design/neon-look-style.md, Rules and Thresholds), on stills scaled
to 960 px wide: value (floor, black, bright, clipped, white shares), the halo round emitters, the colour key and hue
families, palette conformance, the hero's margin, monster form, swarm separation (boundary contrast in dense one-type
clumps) and readability, and per cut the key's median and the shots' floor spread. A still's ID render (demo3d's
mask-T.png, game's still-T-mask.png) and view json (view-T.json) give the mask rules; a video frame has the frame-level
ones and frame L* p25 for the floor. The rules are keyed as the guide's table in docs/design/neon-look-thresholds.json,
per preset where it differs (--preset, else the palette's pairing, else the loosest); --impact marks impact frames.
It writes metrics.md, metrics.json and a swarm overlay per still with --out and exits 1 when a still or the cut breaks
a rule. The v2 calibration is in demo3d/evidence/look/metrics.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import shutil
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from . import paths

SONG_TIMES = (25.0, 60.0, 100.0, 145.0, 175.0)  # groove, drop-1, groove-2, drop-2, final peak
WARMUP_S = 5.0  # fixed-time seconds before each still: effects, the camera's feel and particles settle
SHOT_WARMUP_S = 3.0  # beauty.py's
FLOOR_DL = 17.0  # TD15's floor, now on |dL*|
STRUCTURE_RING = (3, 24)  # ps-suy.1: structure pixels this many px around the monsters
STRUCTURE_MIN_PX = 200  # fewer structure pixels in the ring: no measure
EFFECT_DL = 5.0  # an effect pixel: the effects move its L* by more than this
STAGE_R_M, STAGE_HERO_M = 6.0, 1.2  # the stage's ground: within 6 m of the hero, past its own body
STAGE_BORDER = 0.15  # the frame's outer 15% on every side
STAGE_FLOOR = 15.0  # ps-e8r.5: the stage's median L* above the border's
GROUND_WINDOW = 16  # ps-e8r.6: the ground's local detail, the standard deviation of L* in 16 px windows
COOL_HUE, COOL_CHROMA = (180.0, 280.0), 20.0  # ps-e8r.10: a cool accent pixel, HSV hue (degrees) and C*ab
COOL_CAP = 0.15  # ps-e8r.10: cool accents cover at most this share of a song still
TYPE_CHROMA = 20.0  # ps-bop.4: a monster pixel colourful enough to class by hue, and a ring pixel in a hue
TYPE_RING = (3, 10)  # readability's ring, around one type's pixels
TYPE_HUE_MATCH = 20.0  # degrees: half the roster's 40-degree slots
TYPE_MIN_PX = 500  # fewer type (or ring) pixels in a shot: no row
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


def lab(rgb: np.ndarray) -> np.ndarray:
    """CIELAB (D65) of 8-bit sRGB pixels: L*, a*, b* on the last axis."""
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    f = _f(_linear(rgb) @ m.T / np.array([0.95047, 1.0, 1.08883]))
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def chroma(rgb: np.ndarray) -> np.ndarray:
    """C*ab of 8-bit sRGB pixels (D65)."""
    c = lab(rgb)
    return np.hypot(c[..., 1], c[..., 2])


def lab_hue(c: np.ndarray) -> np.ndarray:
    """CIELAB hue h_ab (degrees, 0-360) of lab() values; noise for greys, so gate it on chroma."""
    return np.degrees(np.arctan2(c[..., 2], c[..., 1])) % 360.0


def hue(rgb: np.ndarray) -> np.ndarray:
    """HSV hue (degrees, 0-360) of 8-bit sRGB pixels; 0 for greys."""
    c = rgb.astype(np.float64)
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    mx, mn = c.max(axis=-1), c.min(axis=-1)
    d = np.where(mx > mn, mx - mn, 1.0)
    h = np.where(mx == r, (g - b) / d % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4))
    return np.where(mx > mn, h * 60.0, 0.0)


def cool_share(rgb: np.ndarray) -> float:
    """The share of the frame in the cool accents' hues (ps-e8r.10): HSV hue in COOL_HUE, C*ab above COOL_CHROMA."""
    h = hue(rgb)
    return float(((h >= COOL_HUE[0]) & (h <= COOL_HUE[1]) & (chroma(rgb) > COOL_CHROMA)).mean())


def still_stats(rgb: np.ndarray) -> dict:
    """The whole frame's look stats."""
    lum = lstar(rgb)
    p2, p50, p98 = np.percentile(lum, [2, 50, 98])
    return {"p2": float(p2), "p50": float(p50), "p98": float(p98), "contrast": float(lum.std()),
            "chroma": float(chroma(rgb).mean()), "nearBlack": float((lum < 10).mean()), "cool": cool_share(rgb),
            "clipped": float((rgb >= 250).all(axis=-1).mean()),
            "clippedBlob": largest_region((rgb >= 250).all(axis=-1)) / lum.size}


def largest_region(mask: np.ndarray) -> int:
    """The pixel count of the mask's largest 8-connected region (union-find over each row's runs)."""
    parent: list[int] = []
    size: list[int] = []

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    prev: list[tuple[int, int, int]] = []  # (start, end exclusive, run id) of the row above
    for row in mask:
        edges = np.flatnonzero(np.diff(np.concatenate(([0], row.view(np.int8), [0]))))
        runs = []
        j = 0
        for a, b in zip(edges[::2], edges[1::2]):
            rid = len(parent)
            parent.append(rid)
            size.append(int(b - a))
            while j < len(prev) and prev[j][1] < a:  # runs above ending before a-1 can't touch this or later runs
                j += 1
            k = j
            while k < len(prev) and prev[k][0] <= b:  # touches diagonally: above start <= b, above end >= a
                ra, rb = find(prev[k][2]), find(rid)
                if ra != rb:
                    parent[rb] = ra
                    size[ra] += size[rb]
                k += 1
            runs.append((int(a), int(b), rid))
        prev = runs
    return max((size[i] for i in range(len(parent)) if parent[i] == i), default=0)


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


def readability(measured: np.ndarray, mask: np.ndarray, empty: np.ndarray | None = None) -> dict:
    """TD15's measure on the measured still, its ID mask (bool) and the empty still (dLbg, when given)."""
    n = int(mask.sum())
    ring = dilate(mask, 10) & ~dilate(mask, 3)
    if n == 0 or not ring.any():
        return {"monsterPx": n, "error": "no monster pixels"}
    lh, ch = lstar(measured), chroma(measured)
    lm, lg = float(np.median(lh[mask])), float(np.median(lh[ring]))
    p10, p90 = np.percentile(lh[mask], [10, 90])
    return {"monsterPx": n, "monsterShare": n / mask.size, "lMonster": lm, "lRing": lg,
            "dL": lm - lg, "dLbg": lm - float(np.median(lstar(empty)[mask])) if empty is not None else None,
            "dC": float(np.median(ch[mask])) - float(np.median(ch[ring])), "cMonster": float(ch[mask].mean()),
            "monsterP10": float(p10), "monsterP90": float(p90),
            "pass": abs(lm - lg) >= FLOOR_DL}


def roster_hues(project: Path) -> dict[str, float]:
    """Each monster type's signature hue (roster.json's `hue`, HSV degrees), less the rejected types."""
    roster = json.loads((project / "Assets" / "Demo3D" / "Data" / "roster.json").read_text(encoding="utf-8"))
    hues: dict[str, float] = {}
    for t in roster["types"]:
        if "hue" in t and not t.get("kind", "").startswith("rejected"):
            hues[t["id"]] = float(t["hue"])
    return hues


def _hue_gap(h: np.ndarray, target: float) -> np.ndarray:
    return np.abs((h - target + 180.0) % 360.0 - 180.0)


def type_readability(measured: np.ndarray, mask: np.ndarray, hues: dict[str, float]) -> dict[str, dict]:
    """Readability by monster type (ps-bop.4). The ID render marks every monster alike, so a monster pixel is
    classed by its colour: HSV hue nearest a type's signature hue, with C*ab above TYPE_CHROMA (dark sides and
    unsaturated highlights stay unclassed). Each type's pixels against the ground TYPE_RING px around them, away
    from every monster (as readability's ring): dL*, and the ring's share in the type's own hue (within
    TYPE_HUE_MATCH degrees, C*ab above TYPE_CHROMA), the background a hue collision would come from."""
    lh, ch, h = lstar(measured), chroma(measured), hue(measured)
    names, targets = list(hues), np.array(list(hues.values()))
    gaps = np.stack([_hue_gap(h, t) for t in targets])
    nearest = gaps.argmin(axis=0)
    coloured = mask & (ch > TYPE_CHROMA)
    away = ~dilate(mask, TYPE_RING[0])
    out: dict[str, dict] = {}
    for i, name in enumerate(names):
        px = coloured & (nearest == i)
        ring = dilate(px, TYPE_RING[1]) & away
        if px.sum() < TYPE_MIN_PX or ring.sum() < TYPE_MIN_PX:
            continue
        lm, lr = float(np.median(lh[px])), float(np.median(lh[ring]))
        out[name] = {"px": int(px.sum()), "share": float(px.sum() / mask.sum()), "lType": lm, "lRing": lr,
                     "dL": lm - lr, "cType": float(np.median(ch[px])),
                     "hueMatch": float(((gaps[i] <= TYPE_HUE_MATCH) & (ch > TYPE_CHROMA))[ring].mean())}
    return out


def structure_contrast(measured: np.ndarray, monsters: np.ndarray, structures: np.ndarray) -> dict:
    """The horde against the structures near it (ps-suy.1), in the measured still: monsters and structures are the ID
    render's masks (bool). Also the structures' share of the frame and their median L* overall."""
    lh = lstar(measured)
    out: dict = {"setShare": float(structures.mean()), "setPx": int(structures.sum())}
    if structures.any():
        out["lSet"] = float(np.median(lh[structures]))
    ring = dilate(monsters, STRUCTURE_RING[1]) & ~dilate(monsters, STRUCTURE_RING[0]) & structures
    out["ringPx"] = n = int(ring.sum())
    if n >= STRUCTURE_MIN_PX and monsters.any():
        lm, ls = float(np.median(lh[monsters])), float(np.median(lh[ring]))
        out.update({"lMonster": lm, "lStructure": ls, "dL": lm - ls})
    return out


def ground_points(view: dict, w: int, h: int) -> tuple[np.ndarray, np.ndarray]:
    """Each pixel's view ray met with the ground plane through the hero's feet: its x and z (m), nan off it."""
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
    on = (k > 0) & np.isfinite(k)
    return np.where(on, hit[..., 0], np.nan), np.where(on, hit[..., 2], np.nan)


def ground_distance(view: dict, w: int, h: int) -> np.ndarray:
    """Each pixel's distance (m) from the hero's feet along the ground plane through them; inf off it."""
    x, z = ground_points(view, w, h)
    hero = view["hero"]
    return np.nan_to_num(np.hypot(x - hero[0], z - hero[2]), nan=np.inf)


def fighting_distance(areas: list[dict], x: np.ndarray, z: np.ndarray) -> np.ndarray:
    """ArenaLayout.FightingDistance on arrays: the distance (m) outside the nearest clearing or path, negative inside."""
    best = np.full(np.shape(x), np.inf)
    for a in areas:
        if a.get("type") not in ("clearing", "path"):
            continue
        if "from" in a:  # a capsule: a path, or a clearing drawn as one
            (ax, az), (bx, bz) = a["from"], a["to"]
            dx, dz = bx - ax, bz - az
            t = np.clip(((x - ax) * dx + (z - az) * dz) / max(dx * dx + dz * dz, 1e-9), 0, 1)
            d = np.hypot(x - ax - t * dx, z - az - t * dz) - a.get("width", 6.0) / 2
        else:
            d = np.hypot(x - a["center"][0], z - a["center"][1]) - a["radius"]
        best = np.minimum(best, d)
    return best


def window_detail(lum: np.ndarray, region: np.ndarray, size: int = GROUND_WINDOW) -> float | None:
    """The mean standard deviation of L* over the size x size windows (a grid) lying wholly in region."""
    h, w = (lum.shape[0] // size) * size, (lum.shape[1] // size) * size
    tiles = lum[:h, :w].reshape(h // size, size, w // size, size)
    inside = region[:h, :w].reshape(h // size, size, w // size, size).all(axis=(1, 3))
    return float(tiles.std(axis=(1, 3))[inside].mean()) if inside.any() else None


def ground_look(rgb: np.ndarray, view: dict, areas: list[dict]) -> dict:
    """The ground in an empty still: fighting ground (clearings and paths) against the edge bands and forest."""
    h, w = rgb.shape[:2]
    x, z = ground_points(view, w, h)
    d = np.where(np.isnan(x), np.nan, fighting_distance(areas, np.nan_to_num(x), np.nan_to_num(z)))
    with np.errstate(invalid="ignore"):
        fight, outer = d <= 0, d > 0
    lum = lstar(rgb)
    out = {"fightPx": int(fight.sum()), "outerPx": int(outer.sum())}
    if fight.any():
        out["lFight"] = float(np.median(lum[fight]))
        out["detail"] = window_detail(lum, fight)
    if outer.any():
        out["lOuter"] = float(np.median(lum[outer]))
    return out


def stage(rgb: np.ndarray, view: dict, monsters: np.ndarray | None) -> dict:
    """The lit stage: the ground's median L* within STAGE_R_M of the hero against the frame's border."""
    h, w = rgb.shape[:2]
    dist = ground_distance(view, w, h)
    ground = (dist <= STAGE_R_M) & (dist > STAGE_HERO_M)
    if monsters is not None:
        ground &= ~dilate(monsters, 2)
    border = np.ones((h, w), dtype=bool)
    bh, bw = round(h * STAGE_BORDER), round(w * STAGE_BORDER)
    border[bh:h - bh, bw:w - bw] = False
    if not ground.any():
        return {"groundPx": 0, "error": "no ground near the hero"}
    lum = lstar(rgb)
    lg, lb = float(np.median(lum[ground])), float(np.median(lum[border]))
    return {"groundPx": int(ground.sum()), "lGround": lg, "lBorder": lb, "dL": lg - lb, "pass": lg - lb >= STAGE_FLOOR}


def effect_stats(horde: np.ndarray, measured: np.ndarray) -> dict:
    """The effects: the pixels whose L* they move by more than EFFECT_DL (horde vs no-effects still).
    restP98 is the other pixels' L* p98, in the same still: effects should be the brightest values (ps-e8r.10)."""
    lh = lstar(horde)
    fx = np.abs(lh - lstar(measured)) > EFFECT_DL
    if not fx.any():
        return {"share": 0.0}
    p50, p98 = np.percentile(lh[fx], [50, 98])
    return {"share": float(fx.mean()), "p50": float(p50), "p98": float(p98),
            "restP98": float(np.percentile(lh[~fx], 98)) if not fx.all() else None,
            "chroma": float(chroma(horde[fx]).mean()),
            "clipped": float((horde[fx] >= 250).all(axis=-1).mean())}


# --- The neon look's metrics (ps-wnj.2, ps-wnj.21) ----------------------------------------------
# docs/design/neon-look-style.md, Rules and Thresholds: its pixel classes, at its 960 px width.

NEON_W = 960  # the guide measures stills at 960 px wide: its ring widths are in these pixels
COLOURFUL_C, COLOURFUL_L = 30.0, 20.0  # a colourful pixel: the key and the families count these
BLACK_L, BRIGHT_L, EMITTER_L = 10.0, 50.0, 70.0  # near black; emitted light (there is no sun); an emitter
WHITE_C, WHITE_L = 15.0, 85.0  # a white pixel
HALO_RING = (3, 10)  # px round the emitters whose median L* is the halo
WINDOW_BINS, FAMILY_MIN = 6, 0.08  # a family: a 60-degree window (six 10-degree bins) holding 8% of the colourful pixels
CONFORM_L, CONFORM_C = 40.0, 30.0  # a pixel palette conformance judges
HUE_TOL = 20.0  # HSV degrees: a judged pixel this near a role's hue conforms
NEUTRAL_C = 15.0  # a role under this C*ab is neutral (white, grey, the void): no hue to conform to
FORM_TILE, FORM_MIN = 32, 0.25  # monster form per tile (px), in tiles a monster body covers this share of
HERO_H_M, HERO_R_M = 1.8, 0.5  # the hero's region above its feet in the view json: a capsule this tall and wide
SWARM_C, SWARM_L = 30.0, 25.0  # a monster pixel candidate: coloured and lit
SWARM_TILE = 32  # px: wider than one monster at the trailer's 32-42 m framing
SWARM_DENSE, SWARM_CLOSE = 0.5, 1  # a dense tile: one enemy hue covers half of it, gaps of up to 2 px closed
SWARM_REACH = 4  # px: a seam is darker than the clump on both sides within this reach
SWARM_SMOOTH = 1  # px: L* box-averaged over this radius first, so speckle (shards, noise) is no seam
SWARM_PCT = 90  # a tile's boundary contrast: this percentile of its clump's seam depth


def neon_size(rgb: np.ndarray) -> np.ndarray:
    """A still at the guide's NEON_W width (Lanczos), so every measure means the same at any capture size."""
    h, w = rgb.shape[:2]
    if w == NEON_W:
        return rgb
    return np.asarray(Image.fromarray(rgb).resize((NEON_W, round(h * NEON_W / w)), Image.LANCZOS))


def palette_roles(palette: dict) -> dict[str, tuple[int, int, int]]:
    """Every colour in a palette JSON (neon-look.md's format) by its role's path: `grid.line`, `enemies.0`,
    `zones.cobalt.tint`. Any `#rrggbb` string is a role, so the style guide can add roles freely."""
    roles: dict[str, tuple[int, int, int]] = {}

    def walk(v: object, path: str) -> None:
        if isinstance(v, str) and re.fullmatch(r"#[0-9a-fA-F]{6}", v):
            roles[path] = (int(v[1:3], 16), int(v[3:5], 16), int(v[5:7], 16))
        elif isinstance(v, dict):
            for k, x in v.items():
                walk(x, f"{path}.{k}" if path else k)
        elif isinstance(v, list):
            for i, x in enumerate(v):
                walk(x, f"{path}.{x['name'] if isinstance(x, dict) and 'name' in x else i}")

    walk(palette, "")
    return roles


def hue_windows(h: np.ndarray) -> tuple[float, int]:
    """The key (the largest share of the given HSV hues in one 60-degree window) and the families (greedy 60-degree
    windows, each taking its hues out of the rest, while one still holds FAMILY_MIN of them)."""
    hist = np.histogram(h, bins=36, range=(0, 360))[0].astype(float)
    hist /= max(hist.sum(), 1.0)

    def win(x: np.ndarray) -> np.ndarray:
        return sum(np.roll(x, -k) for k in range(WINDOW_BINS))

    key, rest, families = float(win(hist).max()), hist.copy(), 0
    while (w := win(rest)).max() >= FAMILY_MIN:
        i = int(w.argmax())
        rest[[(i + k) % 36 for k in range(WINDOW_BINS)]] = 0
        families += 1
    return key, families


def frame_measures(rgb: np.ndarray) -> dict:
    """The guide's frame-level measures of a still at NEON_W: the L* p25 (the video fallback for the floor), the
    near-black, bright, clipped, white shares, the largest clipped region's, the halo round the emitters, the key
    and the families. No mask needed, so they run on video frames too (the reference board's numbers)."""
    lum, cc = lstar(rgb), chroma(rgb)
    clipped = (rgb >= 250).all(axis=-1)
    colourful = (cc >= COLOURFUL_C) & (lum >= COLOURFUL_L)
    p25 = float(np.percentile(lum, 25))
    out = {"frame_p25": p25, "black_share": float((lum < BLACK_L).mean()),
           "bright_share": float((lum >= BRIGHT_L).mean()), "clipped": float(clipped.mean()),
           "clipped_blob": largest_region(clipped) / lum.size,
           "white": float(((cc < WHITE_C) & (lum >= WHITE_L)).mean())}
    emit = lum >= EMITTER_L
    ring = dilate(emit, HALO_RING[1]) & ~dilate(emit, HALO_RING[0]) & ~emit
    if ring.any():
        out["halo"] = float(np.median(lum[ring])) - p25
    if colourful.any():
        out["key60"], out["families"] = hue_windows(hue(rgb)[colourful])
    return out


def palette_conformance(rgb: np.ndarray, roles: dict[str, tuple[int, int, int]]) -> dict:
    """The lit colourful pixels (L* >= CONFORM_L, C*ab >= CONFORM_C) against the palette: the share whose HSV hue is
    within HUE_TOL of a chromatic role's (zone tints included), and each role's share of them (the nearest)."""
    judged = (lstar(rgb) >= CONFORM_L) & (chroma(rgb) >= CONFORM_C)
    names = [k for k, v in roles.items() if chroma(np.array(v, dtype=np.uint8)) >= NEUTRAL_C]
    if not judged.any() or not names:
        return {}
    gaps = np.stack([_hue_gap(hue(rgb)[judged], float(hue(np.array(roles[k], dtype=np.uint8)))) for k in names])
    near = gaps.min(axis=0) <= HUE_TOL
    n = int(judged.sum())
    nearest, counts = np.unique(gaps.argmin(axis=0)[near], return_counts=True)
    return {"palette_conformance": float(near.mean()), "roles": {names[j]: c / n for j, c in zip(nearest, counts)}}


def erode(mask: np.ndarray, r: int) -> np.ndarray:
    """A square erosion by r pixels (outside the frame counts as set)."""
    return ~dilate(~mask, r)


def box_mean(lum: np.ndarray, r: int) -> np.ndarray:
    """Each pixel's mean L* over its (2r + 1)-square window (edges repeated)."""
    h, w = lum.shape
    p = np.pad(lum, r, mode="edge")
    return sum(p[dy:dy + h, dx:dx + w] for dy in range(2 * r + 1) for dx in range(2 * r + 1)) / (2 * r + 1) ** 2


def seam_depth(lum: np.ndarray, reach: int = SWARM_REACH) -> np.ndarray:
    """How far each pixel's L* sits under the clump on both sides of it, across rows or columns: the brightest pixel
    within `reach` on its darker side, less its own L* (0 when it is not in a valley). A dark gap or outline between
    two lit neighbours is deep, and so is a dark interior between lit edges; a flat body or a smooth falloff is 0."""
    h, w = lum.shape
    p = np.pad(lum, reach, mode="edge")
    depth = np.zeros_like(lum)
    for dy, dx in ((0, 1), (1, 0)):
        sides = [np.max([p[reach + s * k * dy:reach + s * k * dy + h, reach + s * k * dx:reach + s * k * dx + w]
                         for k in range(1, reach + 1)], axis=0) for s in (-1, 1)]
        np.maximum(depth, np.minimum(*sides) - lum, out=depth)
    return depth


def enemy_clumps(rgb: np.ndarray, roles: dict[str, tuple[int, int, int]]) -> dict[str, np.ndarray]:
    """Each enemy role's candidate monster pixels (C*ab >= SWARM_C, L* >= SWARM_L, whose nearest chromatic role by
    HSV hue is that enemy, so a formation in the gem's hue is no clump), gaps of up to 2 * SWARM_CLOSE px closed:
    where one type's bodies, and the seams between them, lie."""
    names = [k for k, v in roles.items() if chroma(np.array(v, dtype=np.uint8)) >= NEUTRAL_C]
    nearest = np.stack([_hue_gap(hue(rgb), float(hue(np.array(roles[k], dtype=np.uint8)))) for k in names]).argmin(axis=0)
    cand = (chroma(rgb) >= SWARM_C) & (lstar(rgb) >= SWARM_L)
    return {k: erode(dilate(cand & (nearest == i), SWARM_CLOSE), SWARM_CLOSE)
            for i, k in enumerate(names) if k.startswith("enemies.")}


def swarm_separation(rgb: np.ndarray, roles: dict[str, tuple[int, int, int]], monsters: np.ndarray | None = None) -> dict:
    """How well a dense clump of one type reads as individuals (the "blob" feedback), as boundary contrast in L*,
    on a still at NEON_W. A tile (SWARM_TILE px) is dense when one enemy's clump (enemy_clumps, inside the ID
    render's monsters when the still has one) covers SWARM_DENSE of it: several monsters overlapping. Its boundary
    contrast is the SWARM_PCT percentile of seam_depth (on L* box-averaged over SWARM_SMOOTH) over that clump: dark
    gaps, outlines and lit edges between neighbours are deep; one flat colour mass, or one speckled with shards,
    is near 0. swarm_separation is the dense tiles' median, p10 their 10th percentile. A clump mostly dark between
    lit edges never covers the tile, so it is not measured: it cannot read as a colour blob. Boxes are in NEON_W
    pixels."""
    lum, t = lstar(rgb), SWARM_TILE
    depth = seam_depth(box_mean(lum, SWARM_SMOOTH))
    th, tw = lum.shape[0] // t, lum.shape[1] // t
    found: list[dict] = []
    for k, clump in enemy_clumps(rgb, roles).items():
        if monsters is not None:
            clump &= dilate(monsters, SWARM_CLOSE)
        cover = clump[:th * t, :tw * t].reshape(th, t, tw, t).sum(axis=(1, 3))
        for y, x in zip(*np.nonzero(cover >= SWARM_DENSE * t * t)):
            px = clump[y * t:(y + 1) * t, x * t:(x + 1) * t]
            found.append({"enemy": k, "box": [int(x * t), int(y * t), t],
                          "dL": float(np.percentile(depth[y * t:(y + 1) * t, x * t:(x + 1) * t][px], SWARM_PCT))})
    out: dict = {"dense_tiles": len(found), "tiles": found}
    if found:
        dl = np.array([f["dL"] for f in found])
        out.update({"swarm_separation": float(np.median(dl)), "swarm_separation_p10": float(np.percentile(dl, 10))})
    return out


def project(view: dict, points: np.ndarray, w: int, h: int) -> np.ndarray:
    """World points (n x 3) to pixel x, y (n x 2) in a w x h still, by the view json's view-projection."""
    p = np.c_[points, np.ones(len(points))] @ np.array(view["viewProj"], dtype=np.float64).reshape(4, 4).T
    ndc = p[:, :2] / p[:, 3:]
    return np.c_[(ndc[:, 0] + 1) / 2 * w, (1 - ndc[:, 1]) / 2 * h]


def hero_region(view: dict, w: int, h: int) -> np.ndarray:
    """The hero's pixels, roughly: a screen capsule from its feet (the view json's) to HERO_H_M above them, as wide
    as HERO_R_M at its feet. The monsters' ID render does not mark the hero, so this bounds where it can be."""
    feet = np.array(view["hero"], dtype=np.float64)
    (fx, fy), (hx, hy), *side = project(view, np.array([feet, feet + (0, HERO_H_M, 0), feet + (HERO_R_M, 0, 0),
                                                        feet + (0, 0, HERO_R_M)]), w, h)
    r = max(np.hypot(sx - fx, sy - fy) for sx, sy in side)
    yy, xx = np.mgrid[0:h, 0:w] + 0.5
    dx, dy = hx - fx, hy - fy
    t = np.clip(((xx - fx) * dx + (yy - fy) * dy) / max(dx * dx + dy * dy, 1e-9), 0, 1)
    return np.hypot(xx - fx - t * dx, yy - fy - t * dy) <= r


def mask_rules(rgb: np.ndarray, monsters: np.ndarray, structures: np.ndarray | None, view: dict | None) -> dict:
    """The guide's mask rules, from the ID render (monsters, structures; bool at the still's NEON_W size) and the
    view json: the floor's L* p50/p90 (ground-plane pixels clear of monsters, structures and the hero), the
    hero's margin (its L* p90 less the monsters'), monster form and readability (TD15's |dL*| against the
    ground round them). Monster form is a monster's own L* span, not the crowd's (whose types differ in L*): the
    L* p90 less p10 of the body pixels (a pixel in from the edge) in each FORM_TILE tile they cover FORM_MIN of,
    the tiles' median."""
    lum = lstar(rgb)
    h, w = lum.shape
    out: dict = {}
    body = erode(monsters, 1)
    t = FORM_TILE
    spans = [np.subtract(*np.percentile(px, [90, 10]))
             for y in range(0, h - t + 1, t) for x in range(0, w - t + 1, t)
             if (px := lum[y:y + t, x:x + t][body[y:y + t, x:x + t]]).size >= FORM_MIN * t * t]
    if spans:
        out["monster_form"] = float(np.median(spans))
    if monsters.any():
        read = readability(rgb, monsters)
        if "dL" in read:
            out["readability"] = abs(read["dL"])
    if view is None:
        return out
    region = hero_region(view, w, h)
    hero = region & ~dilate(monsters, 1)
    floor = np.isfinite(ground_points(view, w, h)[0]) & ~dilate(monsters, 2) & ~dilate(region, 2)
    if structures is not None:
        floor &= ~dilate(structures, 2)
    if floor.any():
        f50, f90 = np.percentile(lum[floor], [50, 90])
        out.update({"floor_p50": float(f50), "floor_p90": float(f90)})
    if hero.any() and body.any():
        out["hero_margin"] = float(np.percentile(lum[hero], 90) - np.percentile(lum[body], 90))
    return out


def neon_metrics(rgb: np.ndarray, palette: dict, mask: np.ndarray | None = None, view: dict | None = None) -> dict:
    """Every neon look metric of one still against a palette, flat by the thresholds' metric names, plus the swarm's
    tiles and each role's share for the evidence. `mask` is the ID render (RGB, any size): red or white monsters,
    blue structures; with `view`, the floor and the hero's margin. Without a mask (a video frame) the mask rules
    are absent and frame_p25 stands in for the floor."""
    rgb = neon_size(rgb)
    roles = palette_roles(palette)
    monsters = structures = None
    if mask is not None:
        m = np.asarray(Image.fromarray(mask).resize(rgb.shape[1::-1], Image.NEAREST))
        monsters, structures = m[..., 0] > 127, (m[..., 2] > 127) & (m[..., 0] <= 127)
    conf, swarm = palette_conformance(rgb, roles), swarm_separation(rgb, roles, monsters)
    flat = frame_measures(rgb) | {k: v for k, v in conf.items() if k != "roles"}
    flat |= {k: v for k, v in swarm.items() if k != "tiles"}
    if monsters is not None:
        flat |= mask_rules(rgb, monsters, structures, view)
    return {"metrics": flat, "roles": conf.get("roles", {}), "tiles": swarm["tiles"]}


# --- The thresholds file -------------------------------------------------------------------------

def load_rules(thresholds: dict, preset: str | None) -> list[dict]:
    """The thresholds file's rules, keyed as the guide's table (`floor_p50_max`), for one preset. A key's suffix
    says min or max, the rest names the metric unless the rule's `metric` does; `on` is when it applies (`steady`
    frames by default, `impact` frames, or the whole `cut`), `unless` a metric whose presence skips it (a
    fallback). The value is the preset's, else `value`, else the loosest preset's (with no preset given, the
    ceiling every preset must meet)."""
    presets = thresholds.get("presets", [])
    rules = []
    for key, r in thresholds["rules"].items():
        op = key.rsplit("_", 1)[1]
        if preset in r:
            value = r[preset]
        else:
            value = r["value"] if "value" in r else (max if op == "max" else min)(r[p] for p in presets if p in r)
        rules.append({"key": key, "metric": r.get("metric", key.rsplit("_", 1)[0]), "op": op, "value": value,
                      "on": r.get("on", "steady"), "unless": r.get("unless"), "why": r.get("why", "")})
    return rules


def check_rules(metrics: dict, rules: list[dict], on: str = "steady") -> dict[str, str]:
    """The rules applying `on` (steady, impact or cut) that the metrics break, by key, as text. A metric the still
    lacks (no dense clump, no mask) breaks nothing."""
    broken = {}
    for r in rules:
        v = metrics.get(r["metric"])
        if r["on"] != on or v is None or (r["unless"] and r["unless"] in metrics):
            continue
        if (v < r["value"]) if r["op"] == "min" else (v > r["value"]):
            broken[r["key"]] = f"{r['metric']} {v:.3g} {'<' if r['op'] == 'min' else '>'} {r['value']:g}"
    return broken


def cut_metrics(stills: dict[str, dict]) -> dict:
    """The per-cut measures over a directory's steady stills: the key's median and the spread of the stills' floor
    p50 (frame L* p25 where a still has no floor), each still standing for its shot."""
    keys = [m["key60"] for m in stills.values() if "key60" in m]
    floors = [m.get("floor_p50", m["frame_p25"]) for m in stills.values()]
    out = {"key60_median": float(np.median(keys))} if keys else {}
    if floors:
        out["shot_floor_range"] = float(max(floors) - min(floors))
    return out


def swarm_overlay(rgb: np.ndarray, tiles: list[dict], floor: float | None) -> Image.Image:
    """The still with each dense tile outlined and labelled by its boundary contrast: red under `floor`, green at or
    over it (white with no floor)."""
    im = Image.fromarray(rgb)
    draw = ImageDraw.Draw(im)
    font = ImageFont.load_default(size=max(9, round(im.width / 110)))
    for tile in tiles:
        x, y, size = tile["box"]
        v = tile["dL"]
        col = (255, 255, 255) if floor is None else ((255, 50, 50) if v < floor else (60, 255, 90))
        draw.rectangle([x, y, x + size - 1, y + size - 1], outline=col, width=1)
        draw.text((x + 3, y + 2), f"{v:.0f}", fill=col, font=font)
    return im


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
            common = ["--start-at", f"{t - WARMUP_S:.3f}", "--stills-at", f"{t:.3f}"]
            caps.append(Capture(f"song-{t:03.0f}", common + ["--capture-mask"]))  # the mask keeps monsters out of the stage's ground
            caps.append(Capture(f"song-{t:03.0f}-measure", common + ["--no-effects"]))  # the effects' pixels (ps-e8r.10)
    if beauty:
        for s in shots:
            x, y = s["at"]
            t = float(s.get("t", 20.0))
            common = ["--scene", "S3", "--load", s.get("load", "demo"), "--n", str(s.get("n", 2000)),
                      "--view-width", str(s["viewWidthM"]), "--hold-at", f"{x},{y}",
                      "--start-at", f"{t - SHOT_WARMUP_S:.3f}", "--stills-at", f"{t:.3f}", "--no-post-feel"]
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


def small_jpeg(src: Path | Image.Image, dst: Path, label: str) -> None:
    """A 960 px JPEG with the ps-render tag (RenderTag's JPEG comment), which the vendor guard accepts."""
    with (Image.open(src) if isinstance(src, Path) else src.copy()) as im:
        small = im.convert("RGB").resize((JPEG_W, round(im.height * JPEG_W / im.width)), Image.LANCZOS)
    dst.parent.mkdir(parents=True, exist_ok=True)
    small.save(dst, "JPEG", quality=82, comment=f"ps-render look harness {label}".encode())


def contact_sheet(names: list[str], sets: list[Path], dst: Path, labels: list[str], scale: int = 2) -> None:
    """A row per still with a column per set (before | after for two), at 1/scale of the evidence JPEGs' size."""
    w, h, pad = JPEG_W // scale, JPEG_H // scale, 18
    rows = [n for n in names if all((d / f"{n}.jpg").exists() for d in sets)]
    sheet = Image.new("RGB", (len(sets) * w, len(rows) * (pad + h)), (16, 16, 16))
    draw = ImageDraw.Draw(sheet)
    for i, n in enumerate(rows):
        y = i * (pad + h)
        for j, src in enumerate(sets):
            draw.text((j * w + 4, y + 3), f"{n}: {labels[j]}", fill=(220, 220, 220))
            with Image.open(src / f"{n}.jpg") as im:
                sheet.paste(im.convert("RGB").resize((w, h), Image.LANCZOS), (j * w, y + pad))
    sheet.save(dst, "JPEG", quality=85, comment=b"ps-render look harness contact sheet")


def _pct(v: float) -> str:
    return f"{v * 100:.1f}%"


def stats_row(name: str, s: dict) -> str:
    return (f"| {name} | {s['p2']:.1f} | {s['p50']:.1f} | {s['p98']:.1f} | {s['contrast']:.1f} | "
            f"{s['chroma']:.1f} | {_pct(s['nearBlack'])} | {_pct(s['clipped'])} | "
            f"{_pct(s['clippedBlob']) if 'clippedBlob' in s else ''} | {_pct(s['cool']) if 'cool' in s else ''} |")


STATS_HEAD = ["| still | L* p2 | L* p50 | L* p98 | RMS contrast | mean C*ab | near-black | clipped | clipped blob | cool |",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]


def report(slug: str, song: dict[str, dict], shots: dict[str, dict], baseline: dict | None,
           stages: dict[str, dict] | None = None, grounds: dict[str, dict] | None = None,
           song_fx: dict[str, dict] | None = None, structures: dict[str, dict] | None = None,
           types: dict[str, dict[str, dict]] | None = None, hues: dict[str, float] | None = None) -> str:
    lines = [f"# Look set: {slug}", "",
             "Generated by `demo3d look` (demo3d/tools/src/demo3d_tools/look.py). Stills: 1920x1080 PNGs,",
             "windowless batch-mode player in fixed time. L* is CIE L* (0-100) of the sRGB pixels; RMS contrast",
             "is the standard deviation of L*; near-black is L* < 10; clipped is every channel >= 250; clipped",
             "blob is the largest 8-connected region of clipped pixels; cool is the share in the cool accents' hues",
             f"(HSV hue {COOL_HUE[0]:g}-{COOL_HUE[1]:g} degrees, C*ab > {COOL_CHROMA:g}; at most {COOL_CAP * 100:g}% of a song",
             "still, ps-e8r.10).", "",
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
              "monster C*ab: the monster pixels' mean chroma (ps-e8r.8's type colours);",
              f"monster p10/p90: the monster pixels' L*. Floor: |dL*| >= {FLOOR_DL:g} in every shot (epic ps-e8r,",
              "Decision 3).", "",
              "| shot | monster px (share) | L* monster | L* ring | dL* | dL*bg | dC* | monster C*ab | monster p10 | p90 | floor |",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|"]
    for n, s in shots.items():
        r = s["read"]
        if "error" in r:
            lines.append(f"| {n} | {r['monsterPx']} | | | | | | | | | {r['error']} |")
            continue
        lines.append(f"| {n} | {r['monsterPx']:,} ({_pct(r['monsterShare'])}) | {r['lMonster']:.1f} | "
                     f"{r['lRing']:.1f} | {r['dL']:.1f} | {r['dLbg']:.1f} | {r['dC']:.1f} | {r['cMonster']:.1f} | "
                     f"{r['monsterP10']:.1f} | {r['monsterP90']:.1f} | {'pass' if r['pass'] else 'FAIL'} |")
    if structures:
        lines += ["", "## Horde against structures", "",
                  "In the measured still: the structures (the ID render's blue: ArenaRuntime.Structures, the pieces",
                  "`--structure-style` restyles), their share of the frame and median L*; and the monster pixels against",
                  f"the structure pixels {STRUCTURE_RING[0]}-{STRUCTURE_RING[1]} px around them (dL*: median monster L* less",
                  f"median structure L*), when that ring holds at least {STRUCTURE_MIN_PX} structure pixels (ps-suy.1).", "",
                  "| shot | structure share | L* structures | ring px | L* monster | L* structure ring | dL* |",
                  "|---|---:|---:|---:|---:|---:|---:|"]
        for n, st in structures.items():
            lset = f"{st['lSet']:.1f}" if "lSet" in st else ""
            if "dL" in st:
                cells = f"{st['lMonster']:.1f} | {st['lStructure']:.1f} | {st['dL']:.1f}"
            else:
                cells = " | | too few"
            lines.append(f"| {n} | {_pct(st['setShare'])} | {lset} | {st['ringPx']:,} | {cells} |")
    if types and hues:
        lines += ["", "## Types", "",
                  "Readability by monster type (ps-bop.4), in the measured stills. The ID render marks every monster",
                  f"alike, so a monster pixel with C*ab > {TYPE_CHROMA:g} is classed by the signature hue (roster.json) nearest",
                  "its HSV hue; a type's lit colour drifting into a neighbour's slot is counted as the neighbour. Each",
                  f"type's pixels against the ground {TYPE_RING[0]}-{TYPE_RING[1]} px around them and away from every monster: dL*",
                  "is their median L* less the ring's, pixel-weighted over the shots that hold the type; worst is the",
                  f"shot with the smallest |dL*|; ring in hue is the ring's share within {TYPE_HUE_MATCH:g} degrees of the type's hue",
                  f"at C*ab > {TYPE_CHROMA:g} (a hue collision's background). Share: of the monster pixels.", "",
                  "| type | hue | shots | share | L* type | L* ring | dL* | worst dL* | ring in hue | C*ab |",
                  "|---|---:|---:|---:|---:|---:|---:|---|---:|---:|"]
        for name, h in hues.items():
            rows = {n: t[name] for n, t in types.items() if name in t}
            if not rows:
                lines.append(f"| {name} | {h:g} | 0 | | | | | | | |")
                continue
            px = sum(r["px"] for r in rows.values())
            mean = {k: sum(r[k] * r["px"] for r in rows.values()) / px for k in ("share", "lType", "lRing", "dL", "hueMatch", "cType")}
            worst = min(rows, key=lambda n: abs(rows[n]["dL"]))
            lines.append(f"| {name} | {h:g} | {len(rows)} | {_pct(mean['share'])} | {mean['lType']:.1f} | "
                         f"{mean['lRing']:.1f} | {mean['dL']:.1f} | {rows[worst]['dL']:.1f} ({worst}) | "
                         f"{_pct(mean['hueMatch'])} | {mean['cType']:.1f} |")
    lines += ["", "## Effects", "",
              f"The pixels whose L* the effects move by more than {EFFECT_DL:g} (the horde still against the",
              "measured still): their share of the frame, L* p50/p98 and mean C*ab in the horde still, and the",
              "share of them clipped. Rest p98: the other pixels' L* p98 in the same still; brightest: the effects'",
              "p98 is above it (ps-e8r.10: effects are the brightest values on screen). Song stills measure against",
              "the same frame with `--no-effects`.", "",
              "| still | effect px | L* p50 | L* p98 | rest p98 | brightest | mean C*ab | clipped |",
              "|---|---:|---:|---:|---:|---|---:|---:|"]
    for n, e in [*(song_fx or {}).items(), *((n, s["fx"]) for n, s in shots.items())]:
        if e["share"] == 0:
            lines.append(f"| {n} | 0 | | | | | | |")
        else:
            rest = e.get("restP98")
            lines.append(f"| {n} | {_pct(e['share'])} | {e['p50']:.1f} | {e['p98']:.1f} | "
                         f"{'' if rest is None else f'{rest:.1f}'} | "
                         f"{'' if rest is None else ('yes' if e['p98'] > rest else 'NO')} | "
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
    if grounds:
        lines += ["", "## Ground", "",
                  "The ground in the empty beauty shots (ps-e8r.6): each pixel's view ray met with the ground plane,",
                  "classed by its distance to the layout's clearings and paths. Fighting ground: inside them; outer:",
                  "the edge bands and forest past them (props included). Detail: the mean standard deviation of L* in",
                  f"the {GROUND_WINDOW} px windows lying wholly on fighting ground.", "",
                  "| shot | fighting px | L* fighting | detail | outer px | L* outer |", "|---|---:|---:|---:|---:|---:|"]
        for n, g in grounds.items():
            cell = {k: f"{g[k]:.1f}" if g.get(k) is not None else "" for k in ("lFight", "detail", "lOuter")}
            lines.append(f"| {n} | {g['fightPx']:,} | {cell['lFight']} | {cell['detail']} | {g['outerPx']:,} | {cell['lOuter']} |")
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
        if (bc := baseline.get("monsterChroma")) and any(n in bc for n in shots):
            lines += ["", "The monster pixels' mean chroma in the measured stills:", "",
                      "| shot | monster C*ab | baseline | ratio |", "|---|---:|---:|---:|"]
            for n, s in shots.items():
                c, b = s["read"].get("cMonster"), bc.get(n)
                if c is not None and b:
                    lines.append(f"| {n} | {c:.1f} | {b:.1f} | {c / b:.2f}x |")
        lines += ["", "[contact-sheet.jpg](contact-sheet.jpg): the baseline left, this set right."]
    return "\n".join(lines) + "\n"


# --- Command -----------------------------------------------------------------------------------

def cmd_look(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    slug = a.slug
    captures = wt.root / "captures" / "look" / slug
    evidence = wt.project / "evidence" / (getattr(a, "evidence", None) or f"look/{slug}")
    layout = json.loads((wt.project / "Assets" / "Demo3D" / "Choreography" / "Data" / "arena-layout.json")
                        .read_text(encoding="utf-8"))
    shots, areas = layout["shots"], layout["areas"]
    caps = plan(captures, shots, song=not a.beauty_only, beauty=not a.song_only)
    if not a.measure_only:
        exe = player(wt, a.backend)
        if a.build or not exe.exists():
            from .cli import cmd_build

            build = argparse.Namespace(project="demo3d", platform="mac", backend=a.backend, out=None,
                                       development=False, scene=None, cold=False, force=False)
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

    def structure_mask(name: str) -> np.ndarray | None:  # the ID render's blue, under no monster
        m = next((captures / name).glob("mask-*.png"), None)
        if m is None:
            return None
        rgb = load_rgb(m)
        return (rgb[..., 2] > 127) & (rgb[..., 0] <= 127)

    def stage_of(name: str, monsters: np.ndarray | None) -> dict | None:
        v = next((captures / name).glob("view-*.json"), None)  # sets captured before ps-e8r.5 have none
        return stage(load_rgb(still(name)), json.loads(v.read_text(encoding="utf-8")), monsters) if v else None

    def ground_of(name: str) -> dict | None:
        v = next((captures / name).glob("view-*.json"), None)
        return ground_look(load_rgb(still(name)), json.loads(v.read_text(encoding="utf-8")), areas) if v else None

    stages: dict[str, dict] = {}
    grounds: dict[str, dict] = {}
    song_fx: dict[str, dict] = {}
    structs: dict[str, dict] = {}
    types: dict[str, dict[str, dict]] = {}
    hues = roster_hues(wt.project)
    for c in caps:
        if c.name.startswith("song-") and not c.name.endswith("-measure"):
            horde = load_rgb(still(c.name))
            song[c.name] = still_stats(horde)
            song_fx[c.name] = effect_stats(horde, load_rgb(still(f"{c.name}-measure")))
            if (st := stage_of(c.name, mask(c.name))) is not None:
                stages[c.name] = st
    for s in shots if not a.song_only else []:
        n = s["name"]
        horde, measured, empty = (load_rgb(still(f"{n}-{k}")) for k in ("horde", "measure", "empty"))
        monsters = mask(f"{n}-measure")
        shot_rows[n] = {"horde": still_stats(horde), "read": readability(measured, monsters, empty),
                        "fx": effect_stats(horde, measured)}
        if monsters is not None:
            types[n] = type_readability(measured, monsters, hues)
        if monsters is not None and (sm := structure_mask(f"{n}-measure")) is not None:
            structs[n] = structure_contrast(measured, monsters, sm)
        if (st := stage_of(f"{n}-horde", monsters)) is not None:
            stages[n] = st
        if (g := ground_of(f"{n}-empty")) is not None:
            grounds[n] = g

    if a.trial:
        print(report(slug, song, shot_rows, None, stages, grounds, song_fx, structs, types, hues))
        return 0
    evidence.mkdir(parents=True, exist_ok=True)
    names = list(song) + [f"{n}-{k}" for n in shot_rows for k in ("horde", "empty")]
    for n in names:
        small_jpeg(still(n), evidence / f"{n}.jpg", n)
    numbers = {"slug": slug, "stats": {**song, **{n: s["horde"] for n, s in shot_rows.items()}},
               "dL": {n: s["read"].get("dL") for n, s in shot_rows.items()},
               "monsterChroma": {n: s["read"].get("cMonster") for n, s in shot_rows.items()},
               "stage": {n: st.get("dL") for n, st in stages.items()}, "ground": grounds,
               "effects": {**song_fx, **{n: s["fx"] for n, s in shot_rows.items()}},
               "structures": structs, "types": types}
    (evidence / "look.json").write_text(json.dumps(numbers, indent=1) + "\n", encoding="utf-8")
    base = None
    if a.baseline:
        bdir = wt.project / "evidence" / (a.baseline if "/" in a.baseline else f"look/{a.baseline}")  # a path: under evidence/
        base = json.loads((bdir / "look.json").read_text(encoding="utf-8"))
        contact_sheet(names, [bdir, evidence], evidence / "contact-sheet.jpg", [a.baseline, slug])
    text = report(slug, song, shot_rows, base, stages, grounds, song_fx, structs, types, hues)
    (evidence / "look.md").write_text(text, encoding="utf-8")
    print(text)
    return 0 if all(s["read"].get("pass") for s in shot_rows.values()) else 1


def cmd_look_sheet(a: argparse.Namespace) -> int:
    """One contact sheet across committed look sets: a row per still, a column per set."""
    wt = paths.worktree()
    root = wt.project / "evidence" / "look"
    sets = [root / s for s in a.sets]
    missing = [str(d) for d in sets if not (d / "look.json").exists()]
    if missing:
        print(f"look-sheet: no look set at {', '.join(missing)}", file=sys.stderr)
        return 2
    dst = root / a.out
    dst.parent.mkdir(parents=True, exist_ok=True)
    labels = a.labels.split(",") if a.labels else [Path(s).name for s in a.sets]
    if len(labels) != len(sets):
        print(f"look-sheet: {len(labels)} labels for {len(sets)} sets", file=sys.stderr)
        return 2
    contact_sheet(a.stills.split(","), sets, dst, labels, a.scale)
    print(f"look-sheet: {dst}")
    return 0


NEON_COLUMNS = [  # metrics.md's columns: (heading, metric, format)
    ("floor p50", "floor_p50", ".1f"), ("floor p90", "floor_p90", ".1f"), ("frame p25", "frame_p25", ".1f"),
    ("black", "black_share", "%"), ("bright", "bright_share", "%"), ("clipped", "clipped", "%"),
    ("clip blob", "clipped_blob", "%"), ("white", "white", "%"), ("halo", "halo", ".1f"),
    ("families", "families", "d"), ("key60", "key60", ".2f"), ("conformance", "palette_conformance", "%"),
    ("hero margin", "hero_margin", ".1f"), ("form", "monster_form", ".1f"), ("dense", "dense_tiles", "d"),
    ("separation", "swarm_separation", ".1f"), ("p10", "swarm_separation_p10", ".1f"),
    ("readability", "readability", ".1f")]


def metrics_report(title: str, rows: dict[str, dict], cut: dict, preset: str | None, palette: Path,
                   thresholds: dict, rules: list[dict]) -> str:
    """metrics.md: the rules, a row per still of the neon look's metrics, the cut's and the rules each breaks."""
    lines = [f"# Look metrics: {title}", "",
             "Generated by `demo3d look-metrics` (demo3d/tools/src/demo3d_tools/look.py) against the palette",
             f"`{palette.name}`, preset `{preset or 'any (the loosest preset value)'}`, and the rules of",
             f"docs/design/neon-look-style.md (Rules and Thresholds), on stills scaled to {NEON_W} px wide. L* and",
             "C*ab are CIELAB (D65), hues HSV. Blank cells: the still lacks what the measure needs (an ID render, a",
             "view json, an emitter, a dense clump).", "",
             f"Rules ({thresholds.get('source', '')}):", ""]
    for r in rules:
        when = {"steady": "", "impact": " (impact frames)", "cut": " (the cut)"}[r["on"]]
        lines.append(f"- `{r['key']}` ({r['metric']} {'>=' if r['op'] == 'min' else '<='} {r['value']:g}{when}"
                     + (f", unless the still has {r['unless']}" if r["unless"] else "") + ")" + (f": {r['why']}" if r["why"] else ""))
    lines += ["", "| still | " + " | ".join(c[0] for c in NEON_COLUMNS) + " | rules |",
              "|---|" + "---:|" * len(NEON_COLUMNS) + "---|"]

    def num(m: dict, k: str, fmt: str) -> str:
        v = m.get(k)
        return "" if v is None else (_pct(v) if fmt == "%" else format(v, fmt))

    for name, row in rows.items():
        m, b = row["metrics"], row["broken"]
        lines.append(f"| {name}{' (impact)' if row['impact'] else ''} | " + " | ".join(num(m, k, f) for _, k, f in NEON_COLUMNS)
                     + f" | {'pass' if not b else 'FAIL ' + str(len(b))} |")
    lines += ["", "The cut: " + ", ".join(f"{k} {v:.2f}" for k, v in cut["metrics"].items())
              + (" (pass)." if not cut["broken"] else ".")]
    failing = {n: r["broken"] for n, r in rows.items() if r["broken"]} | ({"the cut": cut["broken"]} if cut["broken"] else {})
    if failing:
        lines += ["", "## Broken rules", ""] + [f"- {n}: " + "; ".join(f"`{k}` {t}" for k, t in b.items())
                                               for n, b in failing.items()]
    return "\n".join(lines) + "\n"


def find_stills(root: Path) -> list[Path]:
    """The PNG and JPEG stills under root, less the players' ID renders and this command's own overlays."""
    return sorted(p for p in root.rglob("*") if p.suffix.lower() in (".png", ".jpg", ".jpeg")
                  and not p.name.startswith(("mask-", "contact-sheet")) and not p.stem.endswith(("-swarm", "-mask")))


def companions(still: Path) -> tuple[Path | None, Path | None]:
    """A still's ID render and view json, if the player wrote them: demo3d's mask-T.png and view-T.json beside
    still-T.png, game's still-T-mask.png."""
    t = still.stem.removeprefix("still-")
    mask = next((m for m in (still.with_name(f"mask-{t}.png"), still.with_name(f"{still.stem}-mask.png")) if m.exists()), None)
    view = still.with_name(f"view-{t}.json")
    return mask, view if view.exists() else None


def cmd_look_metrics(a: argparse.Namespace) -> int:
    """The neon look's metrics over a directory of stills, against a palette and the style guide's thresholds: the
    table on stdout and, with --out, metrics.md, metrics.json and a swarm overlay per still. Exits 1 if a still or
    the cut breaks a rule, 2 if there is nothing to measure."""
    root, palette_path = Path(a.stills), Path(a.palette)
    thresholds_path = Path(a.thresholds) if a.thresholds else paths.worktree().root / "docs" / "design" / "neon-look-thresholds.json"
    stills = find_stills(root) if root.is_dir() else []
    if not stills:
        print(f"look-metrics: no stills under {root}", file=sys.stderr)
        return 2
    palette = json.loads(palette_path.read_text(encoding="utf-8"))
    thresholds = json.loads(thresholds_path.read_text(encoding="utf-8"))
    if not any(k.startswith("enemies.") for k in palette_roles(palette)):
        print(f"look-metrics: {palette_path} has no enemies", file=sys.stderr)
        return 2
    preset = a.preset or thresholds.get("pairings", {}).get(palette.get("name"))
    if preset is not None and preset not in thresholds.get("presets", []):
        print(f"look-metrics: no preset {preset} in {thresholds_path}", file=sys.stderr)
        return 2
    rules = load_rules(thresholds, preset)
    floor = next((r["value"] for r in rules if r["metric"] == "swarm_separation"), None)
    out = Path(a.out) if a.out else None
    rows: dict[str, dict] = {}
    for p in stills:
        name = p.relative_to(root).with_suffix("").as_posix()
        mask_path, view_path = companions(p)
        rgb = load_rgb(p)
        m = neon_metrics(rgb, palette, load_rgb(mask_path) if mask_path else None,
                         json.loads(view_path.read_text(encoding="utf-8")) if view_path else None)
        impact = any(fnmatch.fnmatch(name, g) for g in a.impact or [])
        rows[name] = {"metrics": m["metrics"], "roles": m["roles"], "impact": impact,
                      "broken": check_rules(m["metrics"], rules, "impact" if impact else "steady")}
        if out:
            small_jpeg(swarm_overlay(neon_size(rgb), m["tiles"], floor), out / f"{name.replace('/', '-')}-swarm.jpg",
                       f"look-metrics {name}")
    cm = cut_metrics({n: r["metrics"] for n, r in rows.items() if not r["impact"]})
    cut = {"metrics": cm, "broken": check_rules(cm, rules, "cut")}
    text = metrics_report(a.title or root.name, rows, cut, preset, palette_path, thresholds, rules)
    if out:
        out.mkdir(parents=True, exist_ok=True)
        (out / "metrics.md").write_text(text, encoding="utf-8")
        (out / "metrics.json").write_text(json.dumps({"palette": palette_path.name, "preset": preset, "thresholds": thresholds,
                                                      "stills": rows, "cut": cut}, indent=1) + "\n", encoding="utf-8")
    print(text)
    return 1 if cut["broken"] or any(r["broken"] for r in rows.values()) else 0
