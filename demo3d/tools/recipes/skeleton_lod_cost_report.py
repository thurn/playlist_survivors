"""Validate matched-cost/quality evidence; write aggregate summaries and tagged small stills."""
from __future__ import annotations

import hashlib
import json
import math
import statistics as stats
from collections import defaultdict
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, PngImagePlugin

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "demo3d"
RAW = PROJECT / "captures/skeleton-lod-cost"
CAP = PROJECT / "captures/skeleton-lod-matched"
OUT = PROJECT / "evidence/skeleton-lod"
BAKED = PROJECT / "Assets/Baked/SkeletonLod"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save(image, name):
    metadata = PngImagePlugin.PngInfo()
    metadata.add_text("ps-render", "Skeleton LOD cost-matched Unity/Metal captures, scaled/labelled only")
    image.save(OUT / name, pnginfo=metadata)


def main():
    costs = json.loads((RAW / "results.json").read_text())
    quality = json.loads((RAW / "quality.json").read_text())
    recipe = json.loads((BAKED / "B-matched-recipe.json").read_text())
    meshes = {"A": json.loads((BAKED / "A.json").read_text()),
              "B": json.loads((BAKED / "B-matched.json").read_text())}
    counts = {k: {"triangles": sum(len(s) for s in m["submeshes"]) // 3,
                  "vertices": len(m["positions"])} for k, m in meshes.items()}
    assert len(costs["Rows"]) == 108
    grouped = defaultdict(dict)
    seen = set()
    for r in costs["Rows"]:
        key = (r["Instances"], r["ViewWidth"], r["Pose"], r["Block"])
        assert (*key, r["Variant"]) not in seen
        seen.add((*key, r["Variant"]))
        assert r["Instances"] in (1024, 4096) and r["ViewWidth"] in (30, 55)
        assert r["Pose"] in ("walk", "attack", "crossfade") and r["Block"] in (0, 1, 2)
        assert r["Visible"] == r["ShadowCasters"] == r["Instances"] and r["Draws"] == 2
        assert len(r["Milliseconds"]) == 24 and all(math.isfinite(t) and t > 0 for t in r["Milliseconds"])
        assert abs(stats.median(r["Milliseconds"]) - r["MedianMs"]) < 1e-8
        if r["Variant"] in counts:
            assert r["Triangles"] == counts[r["Variant"]]["triangles"]
            assert r["Vertices"] == counts[r["Variant"]]["vertices"]
        grouped[key][r["Variant"]] = r["MedianMs"]
    assert len(grouped) == 36 and all(set(v) == {"Original", "A", "B"} for v in grouped.values())
    ratios = [v["B"] / v["A"] for v in grouped.values()]
    table = []
    for n in (1024, 4096):
        for width in (30, 55):
            for pose in ("walk", "attack", "crossfade"):
                rows = [v for k, v in grouped.items() if k[:3] == (n, width, pose)]
                table.append({"instances": n, "widthM": width, "pose": pose,
                              "aMedianMs": stats.median(v["A"] for v in rows),
                              "bMedianMs": stats.median(v["B"] for v in rows),
                              "medianPairedRatio": stats.median(v["B"] / v["A"] for v in rows)})
    assert len(quality["Rows"]) == 540
    q = defaultdict(dict)
    for r in quality["Rows"]:
        key = (r["Width"], r["Yaw"], r["Clip"], r["Phase"])
        assert r["Variant"] not in q[key]
        assert 0 < r["SilhouetteIoU"] <= 1 and 0 <= r["ForegroundRgbMae"] <= 1
        q[key][r["Variant"]] = r
    assert len(q) == 270 and all(set(v) == {"A", "B"} for v in q.values())
    quality_table = []
    for width in (30, 40, 55):
        row = {"widthM": width}
        for variant in ("A", "B"):
            values = [r for r in quality["Rows"] if r["Width"] == width and r["Variant"] == variant]
            assert len(values) == 90
            row[variant] = {"meanSilhouetteIoU": stats.mean(r["SilhouetteIoU"] for r in values),
                            "meanForegroundRgbMae": stats.mean(r["ForegroundRgbMae"] for r in values)}
        quality_table.append(row)
    summary = {"counts": counts, "budgetScale": recipe["budgetScale"],
               "method": costs["Method"], "device": costs["Device"], "api": costs["Api"],
               "pairedBlocks": len(ratios), "medianPairedBOverA": stats.median(ratios),
               "individualPairedBlockRange": [min(ratios), max(ratios)],
               "costConfigurations": table, "qualityAtGameSizes": quality_table,
               "bSilhouetteWinsOf270": sum(v["B"]["SilhouetteIoU"] > v["A"]["SilhouetteIoU"] for v in q.values()),
               "bColourWinsOf270": sum(v["B"]["ForegroundRgbMae"] < v["A"]["ForegroundRgbMae"] for v in q.values()),
               "hashes": {name: digest(BAKED / name) for name in ("source.json", "A.json", "B-matched.json", "B-matched-recipe.json")},
               "rawCostSha256": digest(RAW / "results.json"), "rawQualitySha256": digest(RAW / "quality.json")}
    (OUT / "cost-match-summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    rows = ["| Instances | View | Pose | A median ms | B median ms | Paired B/A |",
            "|---:|---:|---|---:|---:|---:|"]
    rows += [f"| {r['instances']:,} | {r['widthM']} m | {r['pose']} | {r['aMedianMs']:.3f} | {r['bMedianMs']:.3f} | {r['medianPairedRatio']:.3f} |" for r in table]
    (BAKED / "cost-table.md").write_text("\n".join(rows) + "\n")
    font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 20)
    image = Image.new("RGB", (1920, 410), "#15202b")
    draw = ImageDraw.Draw(image)
    for i, (variant, label) in enumerate((("Original", "Original: 9,310 triangles"), ("A", f"A: {counts['A']['triangles']:,} triangles / {counts['A']['vertices']:,} vertices"), ("B", f"B matched: {counts['B']['triangles']:,} triangles / {counts['B']['vertices']:,} vertices"))):
        with Image.open(CAP / f"idle-55deg-5m-{variant}.png") as frame:
            image.paste(frame.resize((640, 360), Image.Resampling.LANCZOS), (i * 640, 45))
        draw.text((i * 640 + 10, 12), label, font=font, fill="white")
    save(image, "cost-matched-inspection.png")
    image = Image.new("RGB", (960, 660), "#15202b")
    draw = ImageDraw.Draw(image)
    for row, width in enumerate((30, 55)):
        for i, (variant, label) in enumerate((("Original", "Original"), ("A", "A"), ("B", "B matched"))):
            with Image.open(CAP / f"idle-55deg-{width}m-{variant}.png") as frame:
                image.paste(frame.crop((800, 355, 1120, 635)), (i * 320, row * 330 + 45))
            draw.text((i * 320 + 10, row * 330 + 10), f"{label}: {width} m, native pixels", font=font, fill="white")
    save(image, "cost-matched-native-pixels.png")
    print(json.dumps({"counts": counts, "medianBOverA": stats.median(ratios), "quality": quality_table}, indent=2))


if __name__ == "__main__":
    main()
