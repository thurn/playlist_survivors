"""Sheets of the player's windowless trailer stills, as ps-render-tagged JPEGs.

    "demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" -batchmode \\
        -force-metal -logFile $PWD/captures/run.log --trailer --fixed-time --capture-stills $PWD/captures/DIR \\
        --stills-at T,T,... --seconds S
    uv run --project demo3d/tools python -I demo3d/evidence/trailer/choreography/stills.py OUT.jpg LABEL DIR [CROP]

Every still-*.jpg in DIR, in time order, labelled with its trailer second. With CROP (a square's side in
1080p pixels round the frame's centre, where the camera holds the hero), each tile is that square, five
across; without it, the whole frame, four across.
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw

out, label, folder = sys.argv[1], sys.argv[2], Path(sys.argv[3])
crop = int(sys.argv[4]) if len(sys.argv) > 4 else 0
files = sorted(folder.glob("still-*.jpg"))
cols, w, h = (5, 320, 320) if crop else (4, 480, 270)
pad = 14
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * (h + pad)), (16, 16, 16))
draw = ImageDraw.Draw(sheet)
for i, f in enumerate(files):
    x, y = (i % cols) * w, (i // cols) * (h + pad)
    draw.text((x + 4, y), f.stem[len("still-"):] + " s", fill=(220, 220, 220))
    with Image.open(f) as im:
        im = im.convert("RGB")
        if crop:
            cx, cy = im.width // 2, im.height // 2
            im = im.crop((cx - crop // 2, cy - crop // 2, cx + crop // 2, cy + crop // 2))
        sheet.paste(im.resize((w, h), Image.LANCZOS), (x, y + pad))
sheet.save(out, "JPEG", quality=82, comment=f"ps-render trailer choreography {label}".encode())
print(out)
