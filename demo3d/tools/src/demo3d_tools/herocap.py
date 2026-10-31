"""herocap's own commands (`herocap capture`, `herocap evidence`), beside those it shares with the demos.

herocap/ is a small Unity project that bakes HeroEditor characters (Hippo's skeletal 2D kit, an
Asset Store pack in the `more_assets` root) into transparent PNG frames for native
(docs/design/heroeditor-native.md). Its recipes are tracked JSON, herocap/recipes/<atlas>.json,
one per native atlas plus the hero; herocap/clips.json names the clips (an upper- and a
lower-body HeroEditor clip each) and the capture's rate and scale.

`herocap capture [--recipe ATLAS ...] [--clip NAME ...] [--fps N]`: checks the recipes, then runs
the batch-mode Capture task (Capture.cs). It writes, under paths.heroeditor_cache():

- frames/<atlas>/<clip>_<n>.png, every frame of a clip cropped to one size, facing right;
- frames/<atlas>/clips.json, each clip's frame count, rate, size, feet anchor, impact frame and
  whether it loops;
- faces/<atlas>.png, a head close-up for the face lineup.

Never in the tree: they are HeroEditor-derived, and the guard's derived record covers them.

`herocap evidence`: small tagged renders of the capture in native/evidence/heroeditor/capture/: a
contact sheet per recipe, the face lineup, and budget.json (frames, pixels and 4096 pages).
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw
from PIL.PngImagePlugin import PngInfo

from . import paths, unity

# Faces are dots and lines (the epic's decision 7): the hero's two dots, short line and thin flat
# brows; the undead's two soft glows and a blank or no mouth. Everything else is banned, by name:
# the default Male and Female eyes, the Emoji blobs, the big ZombieEyes, open or toothy mouths.
FACE_PARTS = {
    "eyes": frozenset({"Common.Emoji.Eyes.Blank", "Common.Undead.Eyes.GlowingEyes"}),
    "mouth": frozenset({"Common.Expressions.Mouth.Normal", "Common.Emoji.Mouth.Blank", ""}),
    "eyebrows": frozenset({"Common.Expressions.Eyebrows.Bored", ""}),
}
PART_KEYS = frozenset({"head", "body", "ears", "hair", "beard", "helmet", "glasses", "mask", "cape",
                       "back", "shield", "armor", "primary", "secondary"})
RECIPE_KEYS = frozenset({"atlas", "role", "scale", "weapon", "skin", "parts", "face", "bodyScale", "clips"})
WEAPONS = frozenset({"Melee1H", "Melee2H", "MeleePaired"})
PAGE = 4096
EVIDENCE = Path("native") / "evidence" / "heroeditor" / "capture"
SHEET_BG = (58, 62, 74, 255)
SHEET_FRAMES = 12  # a contact sheet row shows at most this many of a clip's frames, evenly spaced


def project(root: Path) -> Path:
    return root / "herocap"


def load_clips(root: Path) -> dict:
    return json.loads((project(root) / "clips.json").read_text(encoding="utf-8"))


def load_recipes(root: Path) -> dict[str, dict]:
    """Every recipe, by its file's name (the native atlas it replaces)."""
    folder = project(root) / "recipes"
    return {p.stem: json.loads(p.read_text(encoding="utf-8")) for p in sorted(folder.glob("*.json"))}


def check_recipe(name: str, recipe: dict, clip_names: set[str]) -> list[str]:
    """Every problem with one recipe: unknown keys or clips, and any face part that isn't a dot or
    a line."""
    problems = []
    unknown = set(recipe) - RECIPE_KEYS
    if unknown:
        problems.append(f"{name}: unknown keys {sorted(unknown)}")
    if recipe.get("atlas") != name:
        problems.append(f"{name}: atlas {recipe.get('atlas')!r} differs from the file's name")
    if recipe.get("weapon", "Melee1H") not in WEAPONS:
        problems.append(f"{name}: weapon {recipe.get('weapon')!r} is not one of {sorted(WEAPONS)}")
    unknown = set(recipe.get("parts", {})) - PART_KEYS
    if unknown:
        problems.append(f"{name}: unknown parts {sorted(unknown)}")
    face = recipe.get("face", {})
    for part, allowed in FACE_PARTS.items():
        used = face.get(part) or ""
        if used not in allowed:
            problems.append(f"{name}: face {part} {used or '(none)'} is not a dot or line face part "
                            f"(one of {', '.join(sorted(a or '(none)' for a in allowed))})")
    if not recipe.get("clips"):
        problems.append(f"{name}: no clips")
    for clip in recipe.get("clips", []):
        if clip not in clip_names:
            problems.append(f"{name}: unknown clip {clip!r} (herocap/clips.json)")
    return problems


def check_recipes(root: Path) -> list[str]:
    clip_names = {c["name"] for c in load_clips(root)["clips"]}
    problems = []
    for name, recipe in load_recipes(root).items():
        problems += check_recipe(name, recipe, clip_names)
    return problems


def add_parsers(sub: argparse._SubParsersAction) -> None:
    c = sub.add_parser("capture", help="capture the recipes' clips to PNG frames in the machine's cache")
    c.add_argument("--recipe", action="append", help="only this atlas's recipe (repeatable)")
    c.add_argument("--clip", action="append", help="only this clip (repeatable)")
    c.add_argument("--fps", type=float, help="the capture rate (default: herocap/clips.json's)")
    c.add_argument("--out", help="the output folder (default: the cache's heroeditor/)")
    c.add_argument("--cold", action="store_true")
    c.set_defaults(func=cmd_capture)

    e = sub.add_parser("evidence", help="contact sheets, the face lineup and the frame budget into "
                       "native/evidence/heroeditor/capture/")
    e.add_argument("--out", help="the capture's folder (default: the cache's heroeditor/)")
    e.set_defaults(func=cmd_evidence)


def cmd_capture(a: argparse.Namespace) -> int:
    wt = paths.worktree(name="herocap")
    problems = check_recipes(wt.root)
    if problems:
        print("capture: the recipes have problems:\n  " + "\n  ".join(problems), file=sys.stderr)
        return 1
    out = Path(a.out) if a.out else paths.heroeditor_cache()
    args = ["-out", str(out.resolve())]
    for r in a.recipe or []:
        args += ["-recipe", r]
    for c in a.clip or []:
        args += ["-clip", c]
    if a.fps:
        args += ["-fps", str(a.fps)]
    code = unity.run_method(wt, "Capture", args, cold=a.cold)
    if code == 0:
        print(budget_table(read_manifests(out / "frames")))
    return code


def read_manifests(frames: Path) -> dict[str, dict]:
    return {p.parent.name: json.loads(p.read_text(encoding="utf-8"))
            for p in sorted(frames.glob("*/clips.json"))}


def budget(manifests: dict[str, dict], pad: int = 2) -> dict:
    """The frames' count and pixels per atlas, and the 4096 pages they need, with and without
    native's white hit-flash twins (each frame padded by `pad` on every side, plus a packing
    allowance of 10%)."""
    rows, frames, area = {}, 0, 0
    for atlas, m in manifests.items():
        n = sum(c["frames"] for c in m["clips"])
        px = sum(c["frames"] * (c["w"] + 2 * pad) * (c["h"] + 2 * pad) for c in m["clips"])
        rows[atlas] = {"clips": len(m["clips"]), "frames": n, "pixels": px}
        frames += n
        area += px
    pages = math.ceil(area * 1.1 / PAGE**2) if area else 0
    flash = math.ceil(2 * area * 1.1 / PAGE**2) if area else 0
    return {"atlases": rows, "frames": frames, "pixels": area, "pages": pages, "pagesWithFlash": flash}


def budget_table(manifests: dict[str, dict]) -> str:
    b = budget(manifests)
    lines = ["| atlas | clips | frames | megapixels |", "|---|---:|---:|---:|"]
    for atlas, r in b["atlases"].items():
        lines.append(f"| {atlas} | {r['clips']} | {r['frames']} | {r['pixels'] / 1e6:.2f} |")
    lines.append(f"| **all** | | **{b['frames']}** | **{b['pixels'] / 1e6:.2f}** |")
    lines.append(f"\n{b['pages']} pages of 4096² for the frames, {b['pagesWithFlash']} with their "
                 "hit-flash twins (2 px padding, 10% packing allowance).")
    return "\n".join(lines)


def save_tagged(img: Image.Image, dst: Path, label: str) -> None:
    """A PNG with the ps-render text chunk the vendor guard accepts."""
    info = PngInfo()
    info.add_text("ps-render", f"herocap {label}")
    dst.parent.mkdir(parents=True, exist_ok=True)
    img.save(dst, "PNG", pnginfo=info, optimize=True)


def contact_sheet(folder: Path, manifest: dict, scale: float = 0.5) -> Image.Image:
    """One row per clip: up to SHEET_FRAMES of its frames, evenly spaced, at `scale`, each on the
    clip's feet line, with the clip's name, frame count and impact frame."""
    rows = []
    for clip in manifest["clips"]:
        n = clip["frames"]
        impact = clip["impact"] if clip.get("impact", -1) >= 0 else None  # -1: no strike
        picks = {round(i * (n - 1) / max(1, min(n, SHEET_FRAMES) - 1)) for i in range(min(n, SHEET_FRAMES))}
        picks = sorted(picks | ({impact} if impact is not None else set()))
        w, h = max(1, round(clip["w"] * scale)), max(1, round(clip["h"] * scale))
        row = Image.new("RGBA", (150 + len(picks) * (w + 6), h + 8), SHEET_BG)
        d = ImageDraw.Draw(row)
        d.text((6, 4), f"{clip['name']}\n{n} frames\n{'loop' if clip['loop'] else 'one-shot'}"
               + (f"\nimpact {impact}" if impact is not None else ""), fill=(230, 230, 230, 255))
        for i, f in enumerate(picks):
            frame = Image.open(folder / f"{clip['name']}_{f:02d}.png").convert("RGBA")
            frame = frame.resize((w, h), Image.LANCZOS)
            x = 150 + i * (w + 6)
            row.alpha_composite(frame, (x, 4))
            if f == impact:
                d.rectangle((x, 4, x + w - 1, 4 + h - 1), outline=(255, 200, 80, 255))
        rows.append(row)
    sheet = Image.new("RGBA", (max(r.width for r in rows), sum(r.height for r in rows)), SHEET_BG)
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y))
        y += r.height
    return sheet


def face_lineup(faces: Path, atlases: list[str], size: int = 160, per_row: int = 7) -> Image.Image:
    tiles = [(a, faces / f"{a}.png") for a in atlases if (faces / f"{a}.png").exists()]
    cols = min(per_row, len(tiles))
    rows = math.ceil(len(tiles) / cols)
    sheet = Image.new("RGBA", (cols * (size + 8) + 8, rows * (size + 28) + 8), SHEET_BG)
    d = ImageDraw.Draw(sheet)
    for i, (atlas, p) in enumerate(tiles):
        x, y = 8 + (i % cols) * (size + 8), 8 + (i // cols) * (size + 28)
        face = Image.open(p).convert("RGBA").resize((size, size), Image.LANCZOS)
        sheet.alpha_composite(face, (x, y))
        d.text((x + 2, y + size + 4), atlas, fill=(230, 230, 230, 255))
    return sheet


def cmd_evidence(a: argparse.Namespace) -> int:
    root = paths.git_root()
    out = Path(a.out) if a.out else paths.heroeditor_cache()
    manifests = read_manifests(out / "frames")
    if not manifests:
        print(f"evidence: no capture in {out / 'frames'}; run `herocap capture` first", file=sys.stderr)
        return 1
    dest = root / EVIDENCE
    for atlas, m in manifests.items():
        save_tagged(contact_sheet(out / "frames" / atlas, m).convert("RGB"), dest / f"{atlas}.png",
                    f"contact sheet {atlas}")
    save_tagged(face_lineup(out / "faces", list(manifests)).convert("RGB"), dest / "faces.png", "face lineup")
    b = budget(manifests)
    b["fps"] = {atlas: m["fps"] for atlas, m in manifests.items()}
    (dest / "budget.json").write_text(json.dumps(b, indent=2) + "\n", encoding="utf-8")
    print(budget_table(manifests))
    print(f"evidence: {len(manifests)} contact sheets, faces.png and budget.json in {dest.relative_to(root)}")
    return 0
