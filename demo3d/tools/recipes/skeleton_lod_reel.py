"""Label/assemble the ignored Mac player PNG reel; no optional ffmpeg text filters needed."""
from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fps", type=int, default=15)
    args = parser.parse_args()
    directory = ROOT / "demo3d/captures/skeleton-lod-reel"
    frames = sorted(directory.glob("*-Original.png"))
    if not frames:
        parser.error("render the player reel first")
    output = ROOT / "demo3d/captures/skeleton-lod-comparison.mp4"
    font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 20)
    command = ["ffmpeg", "-y", "-v", "error", "-f", "rawvideo", "-pixel_format", "rgb24",
               "-video_size", "1920x400", "-framerate", str(args.fps), "-i", "-", "-an",
               "-c:v", "libx264", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(output)]
    with subprocess.Popen(command, stdin=subprocess.PIPE) as encoder:
        try:
            for path in frames:
                prefix = path.name.split("-")[0]
                image = Image.new("RGB", (1920, 400), "#15202b")
                draw = ImageDraw.Draw(image)
                for i, (variant, triangles) in enumerate((("Original", 9310), ("A", 2143), ("B", 1264))):
                    with Image.open(directory / f"{prefix}-{variant}.png") as source:
                        image.paste(source.resize((640, 360), Image.Resampling.LANCZOS), (i * 640, 40))
                    draw.text((i * 640 + 12, 10), f"{variant}: {triangles:,} triangles", font=font, fill="white")
                encoder.stdin.write(image.tobytes())
        finally:
            encoder.stdin.close()
        if encoder.wait() != 0:
            raise RuntimeError("ffmpeg failed")
    print(f"{len(frames)} frames / {args.fps} fps / {len(frames) / args.fps:.3f}s: {output}")


if __name__ == "__main__":
    main()
