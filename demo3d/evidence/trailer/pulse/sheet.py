"""The beat pulse's sheet (README.md beside this): from a `demo3d filmstrip` run of every frame, one
row per beat, columns at frame offsets from the beat, cropped, and the crop's mean luma per frame
with the beats marked, as a ps-render tagged JPEG; prints each beat's luma jump.

uv run --project demo3d/tools python demo3d/evidence/trailer/pulse/sheet.py \
  RUN_DIR FIRST_S BEAT0_S NBEATS x,y,w,h OUT.jpg LABEL
"""
import sys
from pathlib import Path

import numpy as np
from demo3d_tools.filmstrip import save_tagged
from PIL import Image, ImageDraw

BEAT = 0.387097
OFFSETS = [-3, 0, 2, 4, 6, 9, 13, 18]  # frames from the first frame at or after the beat

run, first, beat0, nbeats = Path(sys.argv[1]), float(sys.argv[2]), float(sys.argv[3]), int(sys.argv[4])
x, y, w, h = (int(v) for v in sys.argv[5].split(","))
out, label = Path(sys.argv[6]), sys.argv[7]
frames = sorted(run.glob("frame-*.png"))
times = [first + k / 60 for k in range(len(frames))]


def frame_at(t):
    k = int(np.ceil((t - first) * 60 - 1e-6))
    return k


tile_w = 300
tile_h = round(h * tile_w / w)
pad, head, left = 4, 22, 70
sheet = Image.new("RGB", (left + len(OFFSETS) * (tile_w + pad), head + nbeats * (tile_h + pad) + 200), (16, 16, 18))
d = ImageDraw.Draw(sheet)
for j, o in enumerate(OFFSETS):
    d.text((left + j * (tile_w + pad) + 4, 4), f"{o:+d} f ({o / 60 * 1000:+.0f} ms)" if o else "impact (+0 f)", fill=(230, 230, 230))
lum = []
for k, f in enumerate(frames):
    im = np.asarray(Image.open(f).convert("L").crop((x, y, x + w, y + h)), dtype=np.float32)
    lum.append(float(im.mean()))
for r in range(nbeats):
    tb = beat0 + r * BEAT
    kb = frame_at(tb)
    yy = head + r * (tile_h + pad)
    down = r % 4 == 0
    d.text((4, yy + 4), f"{'DOWN' if down else 'beat'} {r % 4 + 1}\n{tb:.3f}s", fill=(255, 210, 90) if down else (200, 200, 200))
    for j, o in enumerate(OFFSETS):
        k = kb + o
        if not 0 <= k < len(frames):
            continue
        tile = Image.open(frames[k]).convert("RGB").crop((x, y, x + w, y + h)).resize((tile_w, tile_h), Image.LANCZOS)
        sheet.paste(tile, (left + j * (tile_w + pad), yy))
# luminance trace
gy0 = head + nbeats * (tile_h + pad) + 20
gh, gw = 160, sheet.width - left - 10
lo, hi = min(lum), max(lum)
pts = [(left + (times[k] - first) / (times[-1] - first) * gw, gy0 + gh - (lum[k] - lo) / max(hi - lo, 1e-6) * gh) for k in range(len(lum))]
for r in range(int((times[-1] - beat0) / BEAT) + 1):
    tb = beat0 + r * BEAT
    if tb < first or tb > times[-1]:
        continue
    xx = left + (tb - first) / (times[-1] - first) * gw
    d.line([(xx, gy0), (xx, gy0 + gh)], fill=(255, 210, 90) if r % 4 == 0 else (90, 90, 100), width=2 if r % 4 == 0 else 1)
d.line(pts, fill=(120, 220, 255), width=2)
d.text((4, gy0), "crop\nluma", fill=(200, 200, 200))
d.text((left, gy0 + gh + 4), f"{label}: the crop's mean luma per frame (range {lo:.1f}-{hi:.1f}); gold lines downbeats, grey beats", fill=(200, 200, 200))
save_tagged(sheet, out, label)
print(out, sheet.size)
# Each beat's impact against 3 frames before it, and 12 frames after against the same.
for r in range(nbeats):
    k = frame_at(beat0 + r * BEAT)
    if 3 <= k and k + 12 < len(lum):
        print(f"beat {r + 1}: impact x{lum[k] / lum[k - 3]:.2f}, +12 f x{lum[k + 12] / lum[k - 3]:.2f}")
