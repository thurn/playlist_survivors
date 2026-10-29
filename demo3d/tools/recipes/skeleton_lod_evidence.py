"""Summaries and tagged small stills from the kept Unity/Metal captures (no mesh assets copied)."""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont, PngImagePlugin

ROOT = Path(__file__).resolve().parents[3]
BAKED = ROOT / "demo3d/Assets/Baked/SkeletonLod"
CAP = ROOT / "demo3d/captures/skeleton-lod-final"
EVIDENCE = ROOT / "demo3d/evidence/skeleton-lod"
IDS = ("Original", "A", "B")
SOURCE = json.loads((BAKED / "source.json").read_text())
FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 18)
SMALL = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 14)


def save(image, name):
    tag = PngImagePlugin.PngInfo()
    tag.add_text("ps-render", "Skeleton LOD Unity 6000.6.7f1 Metal RenderTexture comparison; labelled and scaled only")
    image.save(EVIDENCE / name, pnginfo=tag)


def projected_extent(data, width, pitch=55):
    text = (BAKED / "Bones.asset").read_text()
    raw = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]+)", text)[1])
    texels = np.frombuffer(raw, dtype="<f4").reshape(-1, len(SOURCE["palette"]) * 3, 4)
    clip = next(c for c in SOURCE["clips"] if c["Role"] == "idle")
    matrices = texels[clip["FirstRow"] + 24].reshape(-1, 3, 4).astype(float)
    p = np.column_stack((data["positions"], np.ones(len(data["positions"]))))
    bi, bw = np.array(data["boneIndices"], int), np.array(data["boneWeights"])
    skinned = sum(np.einsum("vij,vj->vi", matrices[bi[:, k]], p) * bw[:, k, None] for k in range(4))
    skinned[:, (0, 2)] *= -1  # 180-degree registered facing
    pitch = math.radians(pitch)
    forward, up = np.array([0, -math.sin(pitch), math.cos(pitch)]), np.array([0, math.cos(pitch), math.sin(pitch)])
    distance = width / (2 * math.tan(math.radians(15)) * (16 / 9))
    camera = np.array([0, .85 if width < 10 else 0, 0]) - forward * distance
    relative = skinned - camera
    y = ((relative @ up) / (relative @ forward)) / (2 * math.tan(math.radians(15)))
    return float(np.ptp(y) * 1080)


def main():
    EVIDENCE.mkdir(exist_ok=True)
    variants = [SOURCE] + [json.loads((BAKED / f"{v}.json").read_text()) for v in IDS[1:]]
    measurements = [{"widthM": width, "pitch": pitch,
                     "verticalPixels1080": {v: round(projected_extent(d, width, pitch), 2) for v, d in zip(IDS, variants)}}
                    for pitch in (55, 90) for width in (5, 30, 40, 55)]
    (BAKED / "projected-sizes.json").write_text(json.dumps(measurements, indent=2))
    print(json.dumps(measurements, indent=2))
    counts = [sum(len(s) for s in d["submeshes"]) // 3 for d in variants]
    image = Image.new("RGB", (1920, 420), "#15202b")
    draw = ImageDraw.Draw(image)
    for i, v in enumerate(IDS):
        frame = Image.open(CAP / f"idle-55deg-5m-{v}.png").resize((640, 360), Image.Resampling.LANCZOS)
        image.paste(frame, (i * 640, 50))
        draw.text((i * 640 + 16, 15), f"{v}: {counts[i]:,} triangles | 5 m inspection | idle 0.400 s", font=FONT, fill="white")
    save(image, "comparison-inspection.png")
    # Native-size crops: these character pixels are not enlarged or reduced.
    image = Image.new("RGB", (960, 680), "#15202b")
    draw = ImageDraw.Draw(image)
    for row, width in enumerate((30, 55)):
        for i, v in enumerate(IDS):
            frame = Image.open(CAP / f"idle-55deg-{width}m-{v}.png")
            image.paste(frame.crop((800, 355, 1120, 635)), (i * 320, row * 340 + 50))
            px = next(m for m in measurements if m["widthM"] == width and m["pitch"] == 55)["verticalPixels1080"][v]
            draw.text((i * 320 + 12, row * 340 + 8), f"{v} | {width} m | {px:.1f} px high", font=FONT, fill="white")
    save(image, "game-scale-native-pixels.png")
    semantic = ("idle", "walk", "attack", "attack2", "hit", "death")
    order = sorted(range(len(SOURCE["clips"])), key=lambda i: (semantic.index(SOURCE["clips"][i]["Role"]) if SOURCE["clips"][i]["Role"] in semantic else 100, SOURCE["clips"][i]["Name"]))
    for page in range(3):
        image = Image.new("RGB", (960, 1100), "#15202b")
        draw = ImageDraw.Draw(image)
        for row, ci in enumerate(order[page * 5:page * 5 + 5]):
            for i, v in enumerate(IDS):
                frame = Image.open(CAP / f"clip-{ci:02d}-0.50-{v}.png").resize((320, 180), Image.Resampling.LANCZOS)
                image.paste(frame, (i * 320, row * 220 + 30))
                draw.text((i * 320 + 8, row * 220 + 6), f"{v}: {SOURCE['clips'][ci]['Name']} (50%)", font=SMALL, fill="white")
        save(image, f"clips-{page + 1}.png")


if __name__ == "__main__":
    main()
