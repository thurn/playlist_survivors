"""Contact sheets of a shot's top-down frames (the trailer CLI's --frames), as ps-render-tagged JPEGs.

    uv run --project demo3d/tools python -I demo3d/evidence/trailer/kiting/sheet.py OUT.jpg LABEL FRAME.png...

Four frames across, each 320 px wide, in the order given.
"""

import sys

from PIL import Image

WIDTH, COLUMNS = 320, 4

out, label, frames = sys.argv[1], sys.argv[2], sys.argv[3:]
images = []
for f in frames:
    img = Image.open(f).convert("RGB")
    images.append(img.resize((WIDTH, round(img.height * WIDTH / img.width))))
rows = (len(images) + COLUMNS - 1) // COLUMNS
height = max(i.height for i in images)
sheet = Image.new("RGB", (COLUMNS * WIDTH, rows * height), (10, 10, 12))
for k, img in enumerate(images):
    sheet.paste(img, ((k % COLUMNS) * WIDTH, (k // COLUMNS) * height))
sheet.save(out, "JPEG", quality=84, comment=f"ps-render trailer kiting {label}".encode())
