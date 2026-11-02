"""`champions look [STILLS]`: the look's diagnostics (docs/champions/style.md, Diagnostics) on a directory of
stills: how they stray from the guide values (docs/champions/style-thresholds.json), and how the palette
(Content/palette.json) strays from style.md's rules. A diagnostic, never a gate: it finds stills worth a second look
and decides nothing (design.md, Judgment over rules), so stills that stray from every guide value still exit 0.

Ported by review from the frozen look harness (demo3d/tools/src/demo3d_tools/look.py, `look-metrics`): the same pixel
classes on stills scaled to NEON_W, the value rules (floor, bright, clipped and white shares, the background's light,
flashes), the colour key and families, palette conformance, the hero's margin, monster form, swarm separation and
readability; per cut the key's median, the shots' floor spread and the share of stills that flash. What changed:

- The ID render is the stills' type-ID pass (`champions stills`, Champions.Render.IdPass): each pixel's code names
  what covers it (a monster type, the hero, a structure, an effect), and the view json's `ids` table names each code.
  So the hero's pixels are exact, the floor leaves out everything drawn, and a type's clump is its own pixels.
- New: monster white, the share of the monsters' silhouettes gone white.
- The hero's margin and readability judge the lines, not the bodies: a monster is lit lines over a dark body, so its
  median is the body's.
- Gone: the frame's and each type's black share, which made empty space a target (style.md, Diagnostics).
- One look, so no presets; no song, beauty-shot or stage captures (stills come from `champions stills`).

A still without an ID pass (a frame cut from a video) gets the frame rules alone, and frame L* p25 for the floor.
Stills are `still-NAME.png` with `id-NAME.png` and `view-NAME.json` beside them; any other PNG or JPEG is a frame.
With --out, metrics.md, metrics.json and a small ps-render-tagged JPEG per still (dense swarm tiles outlined) go to
DIR, the evidence folder. Exits 2 only when there is nothing to measure or an input can't be read (a still, its ID
pass or view, the palette or the thresholds file).
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import sys
from collections.abc import Callable
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from . import palette as pal
from . import paths
from .palette import chroma, hue, hue_gap, lstar, luminance

NEON_W = 960  # the rules measure stills at 960 px wide: their ring widths and tiles are in these pixels
COLOURFUL_C, COLOURFUL_L = 30.0, 20.0  # a colourful pixel: the key and the families count these
BRIGHT_L, EMITTER_L = 50.0, 70.0  # emitted light (there is no sun); an emitter
WHITE_C, WHITE_L = 15.0, 85.0  # a white pixel
LIT_L = 20.0  # a lit pixel of a silhouette: its lines and their glow, not its dark body
HALO_RING = (3, 10)  # px round the emitters whose median L* is the halo
READ_RING = (3, 10)  # px round the monsters: the ground readability compares their lines with
WINDOW_BINS, FAMILY_MIN = 6, 0.08  # a family: a 60-degree window (six 10-degree bins) holding 8% of the colourful pixels
CONFORM_L, CONFORM_C = 40.0, 30.0  # a pixel palette conformance judges
HUE_TOL = 20.0  # HSV degrees: a judged pixel this near a role's hue conforms
FORM_TILE, FORM_MIN = 32, 0.25  # monster form per tile (px), in tiles a monster body covers this share of
# The crystal cut's fodder's height on screen (px at NEON_W), on which FORM_TILE and SWARM_TILE were calibrated:
# taller bodies measure in tiles grown as much.
BODY_REF_PX = 45.0
HERO_MIN_PX = 30  # fewer of the hero's pixels in sight (at NEON_W): no margin
SWARM_C, SWARM_L = 30.0, 25.0  # a monster pixel candidate: coloured and lit
SWARM_TILE = 32  # px: wider than one monster at the trailer's framing (bodies BODY_REF_PX tall)
SWARM_DENSE, SWARM_CLOSE = 0.5, 1  # a dense tile: one type's clump covers half of it, gaps of up to 2 px closed
SWARM_HULL = 2  # inside a type's own silhouettes, gaps of up to 4 px close too: the dark between lines is a seam
SWARM_REACH = 4  # px: a seam is darker than the clump on both sides within this reach
SWARM_SMOOTH = 1  # px: L* box-averaged over this radius first, so speckle (shards, noise) is no seam
# Speckle filled before that: a valley this deep in L* whose 8-connected patch is under this many pixels takes the
# brightest L* within SWARM_SPECK_FILL px.
SWARM_SPECK_DL, SWARM_SPECK_PX, SWARM_SPECK_FILL = 6.0, 24, 2
SWARM_PCT = 90  # a tile's boundary contrast: this percentile of its clump's seam depth (in SWARM_TILE tiles)
FLASH_L, FLASH_AREA = 90.0, 0.003  # a flash: a connected region of L* >= 90 covering 0.3% of the frame or more
DRAWN = ("hero", "structure", "effect")  # the ID pass's roles that are no monster
JPEG_W = 960


def thresholds_path() -> Path:
    return paths.worktree().root / "docs" / "champions" / "style-thresholds.json"


def stills_dir(wt: paths.Worktree) -> Path:
    """Where `champions stills` keeps its stills, and `champions look` looks by default (ignored)."""
    return wt.root / "captures" / "stills"


# --- Masks and filters ---------------------------------------------------------------------------

def neon_size(img: np.ndarray, nearest: bool = False) -> np.ndarray:
    """An image at NEON_W wide (Lanczos; nearest for an ID pass, whose codes must not blend)."""
    h, w = img.shape[:2]
    if w == NEON_W:
        return img
    return np.asarray(Image.fromarray(img).resize((NEON_W, round(h * NEON_W / w)),
                                                  Image.NEAREST if nearest else Image.LANCZOS))


def _region_runs(mask: np.ndarray) -> tuple[list[tuple[int, int, int, int]], list[int], list[int]]:
    """The mask's runs along each row joined into 8-connected regions (union-find over the runs): the runs as (row,
    start, end exclusive, run id), each run id's region (its root id) and each root's pixel count."""
    parent: list[int] = []
    size: list[int] = []

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    every: list[tuple[int, int, int, int]] = []
    prev: list[tuple[int, int, int]] = []  # (start, end exclusive, run id) of the row above
    for y, row in enumerate(mask):
        edges = np.flatnonzero(np.diff(np.concatenate(([0], row.view(np.int8), [0]))))
        runs = []
        j = 0
        for a, b in zip(edges[::2], edges[1::2], strict=True):
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
            every.append((y, int(a), int(b), rid))
        prev = runs
    return every, [find(i) for i in range(len(parent))], size


def largest_region(mask: np.ndarray) -> int:
    """The pixel count of the mask's largest 8-connected region."""
    _, root, size = _region_runs(mask)
    return max((size[r] for r in set(root)), default=0)


def region_sizes(mask: np.ndarray) -> np.ndarray:
    """Each set pixel's 8-connected region's pixel count (0 off the mask)."""
    runs, root, size = _region_runs(mask)
    out = np.zeros(mask.shape, dtype=np.int64)
    for y, a, b, rid in runs:
        out[y, a:b] = size[root[rid]]
    return out


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


def erode(mask: np.ndarray, r: int) -> np.ndarray:
    """A square erosion by r pixels (outside the frame counts as set)."""
    return ~dilate(~mask, r)


def close(mask: np.ndarray, r: int) -> np.ndarray:
    """Gaps of up to 2r px closed."""
    return erode(dilate(mask, r), r)


def box_max(lum: np.ndarray, r: int) -> np.ndarray:
    """Each pixel's largest L* over its (2r + 1)-square window (edges repeated; separable)."""
    out = lum
    for axis in (0, 1):
        n = out.shape[axis]
        p = np.pad(out, [(r, r) if a == axis else (0, 0) for a in (0, 1)], mode="edge")
        out = np.max([np.take(p, range(d, d + n), axis=axis) for d in range(2 * r + 1)], axis=0)
    return out


def box_mean(lum: np.ndarray, r: int) -> np.ndarray:
    """Each pixel's mean L* over its (2r + 1)-square window (edges repeated)."""
    h, w = lum.shape
    p = np.pad(lum, r, mode="edge")
    return sum(p[dy:dy + h, dx:dx + w] for dy in range(2 * r + 1) for dx in range(2 * r + 1)) / (2 * r + 1) ** 2


def seam_depth(lum: np.ndarray, reach: int = SWARM_REACH) -> np.ndarray:
    """How far each pixel's L* sits under the clump on both sides of it, across rows or columns: the brightest pixel
    within `reach` on its darker side, less its own L* (0 when it is not in a valley). A dark gap between two lit
    neighbours is deep, and so is a dark body between lit lines; a flat colour mass or a smooth falloff is 0."""
    h, w = lum.shape
    p = np.pad(lum, reach, mode="edge")
    depth = np.zeros_like(lum)
    for dy, dx in ((0, 1), (1, 0)):
        sides = [np.max([p[reach + s * k * dy:reach + s * k * dy + h, reach + s * k * dx:reach + s * k * dx + w]
                         for k in range(1, reach + 1)], axis=0) for s in (-1, 1)]
        np.maximum(depth, np.minimum(*sides) - lum, out=depth)
    return depth


def despeckle(lum: np.ndarray) -> np.ndarray:
    """L* with its speckle filled: every valley (raw seam_depth over SWARM_SPECK_DL) whose 8-connected patch is
    under SWARM_SPECK_PX pixels takes the brightest L* within SWARM_SPECK_FILL px. A shard's glint, noise or dark
    specks in a flat colour mass go; an outline, a gap or a line between bodies, which run on, stay."""
    valley = seam_depth(lum) > SWARM_SPECK_DL
    small = valley & (region_sizes(valley) < SWARM_SPECK_PX)
    return np.where(small, box_max(lum, SWARM_SPECK_FILL), lum) if small.any() else lum


def tile_starts(n: int, t: int, base: int = SWARM_TILE) -> list[int]:
    """Where t px tiles start along n px: every `base` px from 0 (every half tile, once a tile grows past twice
    `base`), and one more against the far edge when they leave a strip."""
    starts = list(range(0, n - t + 1, max(base, t // 2)))
    if starts and starts[-1] + t < n:
        starts.append(n - t)
    return starts


# --- The view: camera, heights, the ID table ------------------------------------------------------

def project(view: dict, points: np.ndarray, w: int, h: int) -> np.ndarray:
    """World points (n x 3) to pixel x, y (n x 2) in a w x h still, by the view json's view-projection (row-major)."""
    p = np.c_[points, np.ones(len(points))] @ np.array(view["viewProj"], dtype=np.float64).reshape(4, 4).T
    ndc = p[:, :2] / p[:, 3:]
    return np.c_[(ndc[:, 0] + 1) / 2 * w, (1 - ndc[:, 1]) / 2 * h]


def ground_points(view: dict, w: int, h: int) -> np.ndarray:
    """Which pixels see the ground plane through the hero's feet (their view ray meets it ahead of the camera)."""
    inv = np.linalg.inv(np.array(view["viewProj"], dtype=np.float64).reshape(4, 4))
    hero_y = float(view["hero"][1])
    x = (np.arange(w) + 0.5) / w * 2 - 1
    y = 1 - (np.arange(h) + 0.5) / h * 2  # the stills' first row is the top: clip-space y = +1
    xx, yy = np.meshgrid(x, y)

    def unproject(z: float) -> np.ndarray:
        p = np.stack([xx, yy, np.full_like(xx, z), np.ones_like(xx)], axis=-1) @ inv.T
        return p[..., :3] / p[..., 3:]

    near, far = unproject(-1.0), unproject(1.0)
    with np.errstate(divide="ignore", invalid="ignore"):
        k = (hero_y - near[..., 1]) / (far[..., 1] - near[..., 1])
    return (k > 0) & np.isfinite(k)


def body_px(view: dict | None, metres: float | None, w: int, h: int) -> float | None:
    """A body `metres` tall stood at the hero's feet, in pixels of a w x h still: its longest projection along the
    world's axes (a top-down camera sees a body's height short and its breadth whole). None without the view."""
    if view is None or not metres or "hero" not in view:
        return None
    feet, b = np.array(view["hero"], dtype=np.float64), float(metres)
    (fx, fy), *ends = project(view, np.array([feet, feet + (b, 0, 0), feet + (0, b, 0), feet + (0, 0, b)]), w, h)
    return float(max(np.hypot(x - fx, y - fy) for x, y in ends))


def scaled_tile(base: int, body: float | None) -> int:
    """A tile of `base` px (calibrated on bodies BODY_REF_PX tall) for bodies `body` px tall on screen: grown in
    proportion for taller bodies, never under `base`."""
    return base if body is None else max(base, round(base * body / BODY_REF_PX))


def id_table(view: dict | None) -> dict[int, dict]:
    """The view json's ID table, by code: each code's `type`, `role` (a cast role, or hero, structure, effect) and
    `heightM`."""
    return {int(k): v for k, v in ((view or {}).get("ids") or {}).items()}


def codes(table: dict[int, dict], present: np.ndarray, roles: tuple[str, ...] | None = None,
          monsters: bool = False) -> list[int]:
    """The table's codes in the ID pass (`present`: the codes seen), with one of `roles` or, `monsters`, none of
    DRAWN's."""
    return [c for c, v in table.items() if c in present
            and ((monsters and v.get("role") not in DRAWN) or (roles is not None and v.get("role") in roles))]


# --- The measures ---------------------------------------------------------------------------------

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
    """The frame-level measures of a still at NEON_W, which need no ID pass: the L* p25 (the floor's fallback), the
    bright, clipped and white shares, the largest clipped region's, the largest near-white region's (the flash), the
    halo round the emitters, the key and the families."""
    lum, cc = lstar(rgb), chroma(rgb)
    clipped = (rgb >= 250).all(axis=-1)
    colourful = (cc >= COLOURFUL_C) & (lum >= COLOURFUL_L)
    p25 = float(np.percentile(lum, 25))
    out = {"frame_p25": p25, "bright_share": float((lum >= BRIGHT_L).mean()), "clipped": float(clipped.mean()),
           "clipped_blob": largest_region(clipped) / lum.size,
           "white": float(((cc < WHITE_C) & (lum >= WHITE_L)).mean()),
           "flash": largest_region(lum >= FLASH_L) / lum.size}
    emit = lum >= EMITTER_L
    ring = dilate(emit, HALO_RING[1]) & ~dilate(emit, HALO_RING[0]) & ~emit
    if ring.any():
        out["halo"] = float(np.median(lum[ring])) - p25
    if colourful.any():
        out["key60"], out["families"] = hue_windows(hue(rgb)[colourful])
    return out


def palette_conformance(rgb: np.ndarray, roles: dict[str, tuple[int, int, int]]) -> dict:
    """The lit colourful pixels (L* >= CONFORM_L, C*ab >= CONFORM_C) against the palette: the share whose HSV hue is
    within HUE_TOL of a chromatic role's, and each role's share of them (the nearest)."""
    judged = (lstar(rgb) >= CONFORM_L) & (chroma(rgb) >= CONFORM_C)
    names = [k for k, v in roles.items() if chroma(np.array(v, dtype=np.uint8)) >= pal.NEUTRAL_C]
    if not judged.any() or not names:
        return {}
    h = hue(rgb)[judged]
    gaps = np.stack([hue_gap(h, float(hue(np.array(roles[k], dtype=np.uint8)))) for k in names])
    near = gaps.min(axis=0) <= HUE_TOL
    n = int(judged.sum())
    nearest, counts = np.unique(gaps.argmin(axis=0)[near], return_counts=True)
    return {"palette_conformance": float(near.mean()),
            "roles": {names[j]: int(c) / n for j, c in zip(nearest, counts, strict=True)}}


def swarm_separation(rgb: np.ndarray, ids: np.ndarray, table: dict[int, dict], view: dict | None) -> dict:
    """How well a dense clump of one type reads as individuals (the "blob"), as boundary contrast in L*, on a still
    at NEON_W with its ID pass. A type's clump is its lit coloured pixels (C*ab >= SWARM_C, L* >= SWARM_L), gaps of
    up to 2 * SWARM_CLOSE px closed, and up to 2 * SWARM_HULL px inside its own silhouettes. A tile (SWARM_TILE px,
    grown with the type's height on screen) is dense when the clump covers SWARM_DENSE of it: several monsters
    overlapping. Its boundary contrast is a high percentile of seam_depth (on L* despeckled, then box-averaged) over
    the clump: dark gaps and bodies between lit lines are deep; one flat colour mass is near 0. The percentile is
    SWARM_PCT in SWARM_TILE tiles, its top share shrinking as the tile grows, so seams keep their width in pixels.
    swarm_separation is the dense tiles' median, p10 their 10th percentile. A clump mostly dark between lit lines
    never covers the tile, so it is not measured: it cannot read as a colour blob."""
    lum = lstar(rgb)
    depth = seam_depth(box_mean(despeckle(lum), SWARM_SMOOTH))
    cand = (chroma(rgb) >= SWARM_C) & (lum >= SWARM_L)
    types = codes(table, np.unique(ids), monsters=True)
    monsters = np.isin(ids, types)
    found: list[dict] = []
    tiles: list[int] = []
    for code in types:
        own = ids == code
        mine = cand & own
        clump = (close(mine, SWARM_CLOSE) | (close(mine, SWARM_HULL) & own)) & dilate(monsters, SWARM_CLOSE)
        t = scaled_tile(SWARM_TILE, body_px(view, table[code].get("heightM"), lum.shape[1], lum.shape[0]))
        tiles.append(t)
        pct = 100 - (100 - SWARM_PCT) * SWARM_TILE / t
        acc = np.pad(clump.cumsum(axis=0).cumsum(axis=1), ((1, 0), (1, 0)))
        for y in tile_starts(lum.shape[0], t):
            for x in tile_starts(lum.shape[1], t):
                if acc[y + t, x + t] - acc[y, x + t] - acc[y + t, x] + acc[y, x] < SWARM_DENSE * t * t:
                    continue
                px = clump[y:y + t, x:x + t]
                found.append({"type": table[code]["type"], "box": [int(x), int(y), t],
                              "dL": float(np.percentile(depth[y:y + t, x:x + t][px], pct))})
    out: dict = {"dense_tiles": len(found), "tiles": found}
    if tiles:
        out["swarm_tile"] = max(tiles)
    if found:
        dl = np.array([f["dL"] for f in found])
        out.update({"swarm_separation": float(np.median(dl)), "swarm_separation_p10": float(np.percentile(dl, 10))})
    return out


def mask_rules(rgb: np.ndarray, ids: np.ndarray, table: dict[int, dict], view: dict | None) -> dict:
    """The rules that need the ID pass (at NEON_W, as the still): the share of the frame the monsters cover, monster
    form (the L* p90 less p10 of the monsters' body pixels in each tile they cover FORM_MIN of, the tiles' median),
    readability (the monsters' lines, their lit L* p90, against the median of the ground READ_RING px round them),
    monster white (the share of the monsters' pixels gone white), the hero's margin (the hero's lit L* p90 less the
    monsters'), the background's light (the share of the frame's linear luminance off everything the ID pass marks,
    under the emitter rung: the grid, halos and glow, which must not drown the swarm) and, with the view's ground
    plane, the floor's L* p50/p90/p99 (p99 is the grid's lines)."""
    lum = lstar(rgb)
    h, w = lum.shape
    present = np.unique(ids)
    monsters = np.isin(ids, codes(table, present, monsters=True))
    hero = np.isin(ids, codes(table, present, ("hero",)))
    drawn = ids > 0
    out: dict = {"monster_share": float(monsters.mean())}
    heights = {c: v.get("heightM") for c, v in table.items() if c in present and v.get("role") not in DRAWN}
    if heights:
        counts = {c: int((ids == c).sum()) for c in heights}
        order = sorted(heights, key=lambda c: heights[c] or 0)
        cum = np.cumsum([counts[c] for c in order])
        median = order[int(np.searchsorted(cum, cum[-1] / 2))]
        if (b := body_px(view, heights[median], w, h)) is not None:
            out["body_px"] = b
    body = erode(monsters, 1)
    t = scaled_tile(FORM_TILE, out.get("body_px"))
    spans = [np.subtract(*np.percentile(px, [90, 10]))
             for y in tile_starts(h, t, FORM_TILE) for x in tile_starts(w, t, FORM_TILE)
             if (px := lum[y:y + t, x:x + t][body[y:y + t, x:x + t]]).size >= FORM_MIN * t * t]
    if spans:
        out["monster_form"] = float(np.median(spans))
    if monsters.any():
        cc = chroma(rgb)
        out["monster_white"] = float(((cc < WHITE_C) & (lum >= WHITE_L))[monsters].mean())
        lit = monsters & (lum >= LIT_L)
        ring = dilate(monsters, READ_RING[1]) & ~dilate(drawn, READ_RING[0])
        if lit.any():
            out["monster_p90"] = float(np.percentile(lum[lit], 90))
            if ring.any():
                out["readability"] = abs(out["monster_p90"] - float(np.median(lum[ring])))
    hero_lit = hero & (lum >= LIT_L)
    if hero.sum() >= HERO_MIN_PX and hero_lit.any():
        out["hero_p90"] = float(np.percentile(lum[hero_lit], 90))
        if "monster_p90" in out:
            out["hero_margin"] = out["hero_p90"] - out["monster_p90"]
    light = luminance(rgb)
    background = ~dilate(drawn, 2) & (lum < EMITTER_L)
    if light.sum() > 0:
        out["background_light"] = float(light[background].sum() / light.sum())
    if view is not None and "viewProj" in view and "hero" in view:
        floor = ground_points(view, w, h) & ~dilate(drawn, 2)
        if floor.any():
            f50, f90, f99 = np.percentile(lum[floor], [50, 90, 99])
            out.update({"floor_p50": float(f50), "floor_p90": float(f90), "floor_p99": float(f99)})
    return out


def still_metrics(rgb: np.ndarray, palette: dict, ids: np.ndarray | None = None, view: dict | None = None) -> dict:
    """Every look metric of one still, flat by the thresholds' metric names, plus the swarm's tiles and each role's
    share of the judged colour for the evidence. `ids` is the still's ID pass (codes, its own size), named by the
    view json's `ids` table; without it, the frame rules alone."""
    rgb = neon_size(rgb)
    conf = palette_conformance(rgb, pal.roles(palette))
    flat = frame_measures(rgb) | {k: v for k, v in conf.items() if k != "roles"}
    tiles: list[dict] = []
    if ids is not None:
        table = id_table(view)
        small = neon_size(ids, nearest=True)
        swarm = swarm_separation(rgb, small, table, view)
        tiles = swarm.pop("tiles")
        flat |= swarm | mask_rules(rgb, small, table, view)
    return {"metrics": flat, "roles": conf.get("roles", {}), "tiles": tiles}


# --- The thresholds file ---------------------------------------------------------------------------

def load_rules(thresholds: dict) -> list[dict]:
    """The thresholds file's rules, keyed as style.md's table (`floor_p50_max`), each with its guide `value`. A key's
    suffix says min or max, the rest names the metric unless the rule's `metric` does; `on` is when it applies
    (`steady` frames by default, `impact` frames, or the whole `cut`), `unless` a metric whose presence skips it (a
    fallback), `unless_below` metrics under whose values it is skipped."""
    rules = []
    for key, r in thresholds["rules"].items():
        metric, _, op = key.rpartition("_")
        if op not in ("min", "max") or not metric:
            raise ValueError(f"rule {key!r}: a key ends in _min or _max")
        rules.append({"key": key, "metric": r.get("metric", metric), "op": op, "value": float(r["value"]),
                      "on": r.get("on", "steady"), "unless": r.get("unless"),
                      "unless_below": r.get("unless_below", {}), "why": r.get("why", "")})
    return rules


def load_thresholds(path: Path) -> tuple[list[dict], str]:
    """The thresholds file's rules and where their guide values come from."""
    thresholds = json.loads(path.read_text(encoding="utf-8"))
    return load_rules(thresholds), thresholds.get("source", str(path))


def applies(rule: dict, metrics: dict, on: str) -> bool:
    if rule["on"] != on or metrics.get(rule["metric"]) is None or (rule["unless"] and rule["unless"] in metrics):
        return False
    return not any(metrics.get(k) is not None and metrics[k] < below for k, below in rule["unless_below"].items())


def guide(r: dict) -> str:
    """A rule's guide value as text: `<= 8`, `>= 0.55`."""
    return f"{'>=' if r['op'] == 'min' else '<='} {_fmt(r['value'], r['metric'])}"


def strays(metrics: dict, rules: list[dict], on: str = "steady") -> dict[str, str]:
    """The rules applying `on` (steady, impact or cut) whose guide values the metrics stray from, by key, as text.
    A metric the still lacks (no dense clump, no ID pass) strays from nothing."""
    out = {}
    for r in rules:
        v = metrics.get(r["metric"])
        if applies(r, metrics, on) and (v < r["value"] if r["op"] == "min" else v > r["value"]):
            out[r["key"]] = f"{r['metric']} {_fmt(v, r['metric'])} strays from {guide(r)}"
    return out


def cut_metrics(stills: dict[str, dict]) -> dict:
    """The measures over a set's steady stills: the key's median, the spread of the stills' floor p50 (frame L* p25
    where a still has no floor), each still standing for its shot, and the share of them that flash."""
    keys = [m["key60"] for m in stills.values() if "key60" in m]
    floors = [m.get("floor_p50", m["frame_p25"]) for m in stills.values()]
    flashes = [m["flash"] > FLASH_AREA for m in stills.values() if "flash" in m]
    out = {"key60_median": float(np.median(keys))} if keys else {}
    if floors:
        out["shot_floor_range"] = float(max(floors) - min(floors))
    if flashes:
        out["flash_frames"] = float(np.mean(flashes))
    return out


# --- Output ----------------------------------------------------------------------------------------

def load_rgb(path: Path) -> np.ndarray:
    with Image.open(path) as im:
        return np.asarray(im.convert("RGB"))


def load_ids(path: Path) -> np.ndarray:
    """An ID pass's codes: its red channel."""
    with Image.open(path) as im:
        return np.asarray(im.convert("RGB"))[..., 0].copy()


def evidence_jpeg(rgb: np.ndarray, tiles: list[dict], floor: float | None, dst: Path, label: str) -> None:
    """The still at NEON_W with each dense swarm tile outlined and labelled by its boundary contrast (red under
    `floor`, green at or over it), as a JPEG with the ps-render tag the vendor guard asks of evidence."""
    im = Image.fromarray(rgb)
    draw = ImageDraw.Draw(im)
    font = ImageFont.load_default(size=max(9, round(im.width / 110)))
    for tile in tiles:
        x, y, size = tile["box"]
        col = (255, 255, 255) if floor is None else ((255, 50, 50) if tile["dL"] < floor else (60, 255, 90))
        draw.rectangle([x, y, x + size - 1, y + size - 1], outline=col, width=1)
        draw.text((x + 3, y + 2), f"{tile['dL']:.0f}", fill=col, font=font)
    dst.parent.mkdir(parents=True, exist_ok=True)
    im.resize((JPEG_W, round(im.height * JPEG_W / im.width)), Image.LANCZOS).save(
        dst, "JPEG", quality=85, comment=f"ps-render champions look {label}".encode())


SHARES = {"bright_share", "clipped", "clipped_blob", "white", "flash", "background_light", "palette_conformance",
          "monster_share", "monster_white", "flash_frames"}


def _fmt(v: float, metric: str) -> str:
    return f"{v * 100:.1f}%" if metric in SHARES else f"{v:.3g}"


def report(title: str, palette_note: str, rows: dict[str, dict], cut: dict, rules: list[dict], source: str) -> str:
    """metrics.md: the palette's check, then every threshold key with each still's value (a blank: the still lacks
    what the measure needs; n/a: the rule is for other frames), the cut's keys and where each still strays."""
    names = list(rows)
    lines = [f"# Look diagnostics: {title}", "",
             "A diagnostic, not a verdict: these numbers point at stills worth a second look and decide nothing.",
             "A value that strays from its guide on a good-looking frame is wrong about that frame",
             "(docs/champions/design.md, Judgment over rules).", "",
             "Generated by `champions look` (rhythm_champions/tools/src/champions_tools/look.py) against",
             "rhythm_champions/Content/palette.json and docs/champions/style-thresholds.json (docs/champions/style.md,",
             f"Diagnostics), on stills scaled to {NEON_W} px wide. L* and C*ab are CIELAB (D65), hues HSV. A blank",
             "cell: the still lacks what the measure needs (an ID pass, a view, an emitter, a dense clump); n/a: the",
             "rule judges other frames.", "",
             f"Thresholds: {source}", "", palette_note, "",
             "| rule | guide | " + " | ".join(f"{n}{' (impact)' if rows[n]['impact'] else ''}" for n in names) + " |",
             "|---|---|" + "---:|" * len(names)]

    def cell(name: str, r: dict, value: float | None) -> str:
        row = rows[name]
        on = "impact" if row["impact"] else "steady"
        if r["on"] != on:
            return "n/a"
        if value is None:
            return ""
        if not applies(r, row["metrics"], on):
            return f"{_fmt(value, r['metric'])} (not judged)"
        return _fmt(value, r["metric"]) + (" (strays)" if r["key"] in row["strays"] else "")

    for r in rules:
        if r["on"] != "cut":
            lines.append(f"| `{r['key']}` | {guide(r)} | "
                         + " | ".join(cell(n, r, rows[n]["metrics"].get(r["metric"])) for n in names) + " |")
    lines += ["", "| measure | " + " | ".join(names) + " |", "|---|" + "---:|" * len(names)]
    for metric in ("monster_share", "body_px", "swarm_tile", "dense_tiles", "swarm_separation_p10", "monster_p90",
                   "hero_p90", "frame_p25"):
        lines.append(f"| {metric} | " + " | ".join("" if (v := rows[n]["metrics"].get(metric)) is None
                                                    else _fmt(v, metric) for n in names) + " |")
    lines += ["", "The cut (its steady stills):", "", "| rule | guide | the cut |", "|---|---|---:|"]
    for r in rules:
        if r["on"] == "cut":
            v = cut["metrics"].get(r["metric"])
            lines.append(f"| `{r['key']}` | {guide(r)} | " + ("" if v is None else _fmt(v, r["metric"])
                         + (" (strays)" if r["key"] in cut["strays"] else "")) + " |")
    straying = {n: r["strays"] for n, r in rows.items() if r["strays"]} | ({"the cut": cut["strays"]} if cut["strays"] else {})
    lines += ["", "## Where the stills stray from the guide values", ""]
    lines += [f"- {n}: " + "; ".join(f"`{k}` {t}" for k, t in b.items()) for n, b in straying.items()] or ["Nowhere."]
    return "\n".join(lines) + "\n"


def find_stills(root: Path) -> list[Path]:
    """The PNG and JPEG stills under root, less the ID passes."""
    return sorted(p for p in root.rglob("*") if p.suffix.lower() in (".png", ".jpg", ".jpeg")
                  and not p.name.startswith("id-"))


def companions(still: Path) -> tuple[Path | None, Path | None]:
    """A still's ID pass and view json, when `champions stills` wrote them: id-NAME.png and view-NAME.json beside
    still-NAME.png."""
    name = still.stem.removeprefix("still-")
    ids, view = still.with_name(f"id-{name}.png"), still.with_name(f"view-{name}.json")
    return (ids if ids.exists() else None), (view if view.exists() else None)


def palette_check(path: Path) -> tuple[dict, list[str], str]:
    """The palette, where it strays from style.md's The Palette (test_palette.py holds it to them), and a line saying
    so."""
    palette = pal.load(path)
    found = pal.problems(palette, pal.load_cast(path.parent))
    note = (f"Palette `{palette.get('name')}` ({len(pal.roles(palette))} roles): "
            + ("keeps every rule of style.md's The Palette." if not found else
               f"strays from {len(found)} rules of style.md's The Palette:\n" + "\n".join(f"- {p}" for p in found)))
    return palette, found, note


class Unreadable(Exception):
    """An input `champions look` can't measure: a still, its ID pass or view, the palette or the thresholds file."""


def read[T](path: Path, load: Callable[[Path], T]) -> T:
    """`load(path)`, with any failure to read or parse the file raised as Unreadable (PIL's unidentified image is an
    OSError, a JSON error a ValueError, a missing field a KeyError)."""
    try:
        return load(path)
    except (OSError, ValueError, KeyError, TypeError) as e:
        raise Unreadable(f"{path}: {e}") from e


def load_still(still: Path) -> tuple[np.ndarray, np.ndarray | None, dict | None]:
    """A still with its ID pass and view json when it has them; an ID pass of another size is unreadable."""
    ids_path, view_path = companions(still)
    rgb = read(still, load_rgb)
    ids = read(ids_path, load_ids) if ids_path else None
    view = read(view_path, lambda q: json.loads(q.read_text(encoding="utf-8"))) if view_path else None
    if ids is not None and ids.shape != rgb.shape[:2]:
        raise Unreadable(f"{ids_path}: {ids.shape[1]}x{ids.shape[0]}, but its still is {rgb.shape[1]}x{rgb.shape[0]}")
    return rgb, ids, view


def cmd_look(a: argparse.Namespace) -> int:
    try:
        return look(a)
    except Unreadable as e:
        print(f"look: can't read {e}", file=sys.stderr)
        return 2


def look(a: argparse.Namespace) -> int:
    """Measure the stills and report where they stray: 0 however far they stray, 2 with nothing to measure."""
    wt = paths.worktree()
    root = Path(a.stills) if a.stills else stills_dir(wt)
    palette_path = Path(a.palette) if a.palette else wt.project / "Content" / "palette.json"
    thresholds_file = Path(a.thresholds) if a.thresholds else thresholds_path()
    stills = find_stills(root) if root.is_dir() else []
    if not stills:
        print(f"look: no stills under {root} (run `champions stills`)", file=sys.stderr)
        return 2
    palette, found, note = read(palette_path, palette_check)
    rules, source = read(thresholds_file, load_thresholds)
    floor = next((r["value"] for r in rules if r["metric"] == "swarm_separation"), None)
    out = Path(a.out) if a.out else None
    rows: dict[str, dict] = {}
    for p in stills:
        name = p.relative_to(root).with_suffix("").as_posix().removeprefix("still-")
        rgb, ids, view = load_still(p)
        m = still_metrics(rgb, palette, ids, view)
        impact = bool((view or {}).get("impact")) or any(fnmatch.fnmatch(name, g) for g in a.impact or [])
        rows[name] = {"metrics": m["metrics"], "roles": m["roles"], "impact": impact,
                      "strays": strays(m["metrics"], rules, "impact" if impact else "steady")}
        if out:
            evidence_jpeg(neon_size(rgb), m["tiles"], floor, out / f"{name.replace('/', '-')}.jpg", name)
    cm = cut_metrics({n: r["metrics"] for n, r in rows.items() if not r["impact"]})
    cut = {"metrics": cm, "strays": strays(cm, rules, "cut")}
    text = report(a.title or root.name, note, rows, cut, rules, source)
    if out:
        out.mkdir(parents=True, exist_ok=True)
        (out / "metrics.md").write_text(text, encoding="utf-8")
        (out / "metrics.json").write_text(json.dumps({"palette": palette.get("name"), "problems": found, "stills": rows,
                                                      "cut": cut}, indent=1) + "\n", encoding="utf-8")
    print(text)
    return 0
