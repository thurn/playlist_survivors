"""`herocap bake`: packs herocap's captured frames into a native character atlas in the cache.

native draws `--art heroeditor` from `<cache>/heroeditor/assets/` (docs/design/heroeditor-native.md):
a characters.json in native's atlas format (native/crates/render/src/atlas.rs) and its pages,
`heroeditor-<n>.png`. native lays these sprites over its LPC ones, so an atlas missing here keeps
its LPC art.

- **Names.** Each atlas gets native's animation names, each one of its captured clips (`HERO`,
  `ENEMY`): the hero's swings, an enemy's walk, its death fall (`death`) and its dancers' `kick`
  and `kick-alt`, the two halves of the arms-up `victory` loop, so two beats play it whole. The
  thorn plants are rooted: their walk is the idle clip. Only what native draws is packed.
- **Size.** HeroEditor frames are hi-res and drawn smooth at the LPC size: each animation carries
  a `density`, frame pixels per world pixel, so a character stands as tall as the LPC sprite it
  replaces (its walk, or the thorn plants' prop). The bat, bee and slime aren't human-shaped:
  their stand-ins keep the recipe's scale against the hero instead.
- **Side view.** Frames face right (`side`); native mirrors them for left.
- **Pages.** Frames are shelf-packed into 4096-wide pages with 2 px of padding, their colour
  bled into the transparent texels around them so smooth sampling draws no dark fringe. There are
  no white hit-flash twins: native flashes these as silhouettes.

Never in the tree: the atlas is HeroEditor-derived, and the guard's derived record covers the
cache.
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

import numpy as np
from PIL import Image

from . import paths

PAGE = 4096
PAD = 2
# native's names for the hero's captured clips (native/crates/sim HERO_ANIMS). The
# hero's beat step is native's `jump`: a run cycle reads as a stride, where HeroEditor's Jump clip
# holds a pose.
HERO = {"idle": "idle", "walk": "walk", "run": "run", "jump": "run", "slash": "slash",
        "backslash": "jab", "halfslash": "slash", "thrust": "jab", "spellcast": "cast",
        "slash-oversize": "slash2h", "hurt": "hurt"}
# An enemy's: its walk, death fall and dance, and the hurt kept for a hit's frames.
ENEMY = {"walk": "walk", "hurt": "hurt", "death": ("death-back", "death-front"),
         "kick": ("victory", 0), "kick-alt": ("victory", 1)}
ROOTED = ("thorn-plant-",)
# An LPC sprite shorter than this (world px above its feet) is a blob, not a person.
HUMAN_MIN_PX = 40
ALPHA_SOLID = 32


def add_parser(sub: argparse._SubParsersAction) -> None:
    b = sub.add_parser("bake", help="pack the captured frames into native's character atlas in "
                       "the cache's heroeditor/assets/")
    b.add_argument("--out", help="the capture's folder (default: the cache's heroeditor/)")
    b.set_defaults(func=cmd_bake)


def height_above_feet(img: Image.Image, anchor_y: float) -> float:
    """World (or frame) pixels from the feet to the top of the solid part of `img`."""
    box = img.getchannel("A").point(lambda a: 255 if a > ALPHA_SOLID else 0).getbbox()
    return anchor_y - box[1] if box else 0.0


def lpc_heights(assets: Path) -> dict[str, float]:
    """How tall each LPC character stands (its first walk frame facing right), and each thorn
    plant's prop."""
    chars = json.loads((assets / "characters.json").read_text(encoding="utf-8"))
    pages = [Image.open(assets / p).convert("RGBA") for p in chars["pages"]]
    out = {}
    for sid, s in chars["sprites"].items():
        a = s["animations"].get("walk") or next(iter(s["animations"].values()))
        f = a["frames"]["right"][0]
        crop = pages[f["page"]].crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"]))
        out[sid] = height_above_feet(crop, a["anchorY"])
    world = json.loads((assets / "world.json").read_text(encoding="utf-8"))
    wpages = [Image.open(assets / p).convert("RGBA") for p in world["pages"]]
    for pid, prop in world["props"].items():
        if pid.startswith(ROOTED):
            v = prop["variants"][0]
            r = v["rect"]
            crop = wpages[r["page"]].crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))
            out[pid] = height_above_feet(crop, v["anchorY"])
    return out


def names(atlas: str, clips: dict[str, dict]) -> dict[str, tuple[str, int | None]]:
    """native's animation name → (captured clip, which half or None), for one atlas."""
    table = HERO if atlas == "hero" else ENEMY
    out = {}
    for name, src in table.items():
        half = None
        if isinstance(src, tuple) and isinstance(src[1], int):
            src, half = src
        options = src if isinstance(src, tuple) else (src,)
        clip = next((c for c in options if c in clips), None)
        if clip is None:
            raise SystemExit(f"bake: {atlas} has no {' or '.join(options)} clip for native's {name}")
        out[name] = (clip, half)
    if atlas.startswith(ROOTED):
        out["walk"] = ("idle", None)
    return out


def bleed(rgba: np.ndarray, steps: int = PAD + 1) -> np.ndarray:
    """`rgba` with each transparent texel's colour taken from its solid neighbours, `steps`
    texels out, so smooth sampling at an edge mixes in the edge's colour, not black."""
    rgb = rgba[..., :3].astype(np.float32)
    known = rgba[..., 3] > 0
    for _ in range(steps):
        total = np.zeros_like(rgb)
        count = np.zeros(known.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            k = np.roll(known, (dy, dx), (0, 1))
            total += np.roll(rgb, (dy, dx), (0, 1)) * k[..., None]
            count += k
        grow = ~known & (count > 0)
        rgb[grow] = total[grow] / count[grow][:, None]
        known = known | grow
    out = rgba.copy()
    out[..., :3] = np.clip(rgb + 0.5, 0, 255).astype(np.uint8)
    return out


def pack(sizes: list[tuple[int, int]]) -> tuple[list[tuple[int, int, int]], list[int]]:
    """Shelf-packs padded boxes, tallest first: each box's (page, x, y) and each page's height."""
    order = sorted(range(len(sizes)), key=lambda i: (-sizes[i][1], -sizes[i][0]))
    spots: list[tuple[int, int, int]] = [(0, 0, 0)] * len(sizes)
    heights = [0]
    x = y = shelf = 0
    for i in order:
        w, h = sizes[i]
        if x + w > PAGE:
            x, y, shelf = 0, y + shelf, 0
        if y + h > PAGE:
            heights.append(0)
            x = y = shelf = 0
        spots[i] = (len(heights) - 1, x, y)
        x += w
        shelf = max(shelf, h)
        heights[-1] = max(heights[-1], y + h)
    return spots, heights


def bake(capture: Path, assets: Path) -> dict:
    """Packs `capture`/frames into `capture`/assets; returns the written characters.json."""
    frames_dir = capture / "frames"
    manifests = {p.parent.name: json.loads(p.read_text(encoding="utf-8"))
                 for p in sorted(frames_dir.glob("*/clips.json"))}
    if "hero" not in manifests:
        raise SystemExit(f"bake: no hero capture in {frames_dir}; run `herocap capture` first")
    lpc = lpc_heights(assets)

    def stands(atlas: str) -> float:
        m = manifests[atlas]
        idle = next(c for c in m["clips"] if c["name"] == "idle")
        img = Image.open(frames_dir / atlas / "idle_00.png").convert("RGBA")
        return height_above_feet(img, idle["anchorY"])

    hero_density = stands("hero") / lpc["hero"]
    images: dict[tuple[str, str, int], Image.Image] = {}
    sprites = {}
    for atlas, m in manifests.items():
        clips = {c["name"]: c for c in m["clips"]}
        if atlas not in lpc:
            print(f"bake: {atlas}: no LPC sprite to replace; skipped", file=sys.stderr)
            continue
        human = lpc[atlas] >= HUMAN_MIN_PX
        density = stands(atlas) / lpc[atlas] if human else hero_density
        anims = {}
        for name, (clip, half) in names(atlas, clips).items():
            c = clips[clip]
            idx = list(range(c["frames"]))
            if half is not None:
                mid = len(idx) // 2
                idx = idx[:mid] if half == 0 else idx[mid:]
            for n in idx:
                key = (atlas, clip, n)
                if key not in images:
                    images[key] = Image.open(frames_dir / atlas / f"{clip}_{n:02d}.png").convert("RGBA")
            impact = c.get("impact", -1)
            anims[name] = {
                "frameW": c["w"], "frameH": c["h"], "anchorX": c["anchorX"], "anchorY": c["anchorY"],
                "density": round(density, 4), "side": True,
                "impact": impact if impact >= 0 and half is None else None,
                "frames": {"right": [key for key in ((atlas, clip, n) for n in idx)]},
            }
        sprites[atlas] = {"id": atlas, "animations": anims}

    keys = list(images)
    spots, heights = pack([(images[k].width + 2 * PAD, images[k].height + 2 * PAD) for k in keys])
    pages = [np.zeros((h, PAGE, 4), np.uint8) for h in heights]
    regions = {}
    for k, (page, x, y) in zip(keys, spots):
        img = np.asarray(images[k])
        h, w = img.shape[:2]
        pages[page][y:y + h + 2 * PAD, x:x + w + 2 * PAD] = bleed(np.pad(img, ((PAD, PAD), (PAD, PAD), (0, 0))))
        regions[k] = {"page": page, "x": x + PAD, "y": y + PAD, "w": w, "h": h}
    for s in sprites.values():
        for a in s["animations"].values():
            a["frames"]["right"] = [regions[k] for k in a["frames"]["right"]]

    out = capture / "assets"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    names_ = [f"heroeditor-{i}.png" for i in range(len(pages))]
    for name, px in zip(names_, pages):
        Image.fromarray(px, "RGBA").save(out / name, optimize=False, compress_level=3)
    chars = {"pages": names_, "sprites": sprites}
    (out / "characters.json").write_text(json.dumps(chars, indent=1) + "\n", encoding="utf-8")
    return chars


def cmd_bake(a: argparse.Namespace) -> int:
    capture = Path(a.out) if a.out else paths.heroeditor_cache()
    chars = bake(capture, paths.git_root() / "public" / "assets")
    frames = sum(len(an["frames"]["right"]) for s in chars["sprites"].values()
                 for an in s["animations"].values())
    for sid, s in chars["sprites"].items():
        print(f"  {sid:20} density {s['animations']['walk']['density']:.3f}  "
              f"{', '.join(s['animations'])}")
    print(f"bake: {len(chars['sprites'])} sprites, {frames} frames, {len(chars['pages'])} pages "
          f"in {capture / 'assets'}")
    return 0
