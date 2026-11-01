"""sheet.py STILLS_DIR OUT.jpg LABEL (run from the repo root with `uv run --project demo3d/tools python`; see README.md): one
row per still: the frame at half size and a 480 x 270 crop of the 1080p frame round the hero, at the frame's half
scale x 2 (so the hero shows at its 1080p size, doubled)."""
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw
sys.path.insert(0, 'demo3d/tools/src')
from demo3d_tools.filmstrip import save_tagged

stills, out, label = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3]

def project(vp, p):
    x = vp[0]*p[0] + vp[1]*p[1] + vp[2]*p[2] + vp[3]
    y = vp[4]*p[0] + vp[5]*p[1] + vp[6]*p[2] + vp[7]
    w = vp[12]*p[0] + vp[13]*p[1] + vp[14]*p[2] + vp[15]
    return ((x/w + 1) * 0.5 * 1920, (1 - (y/w + 1) * 0.5) * 1080)

rows = []
for f in sorted(stills.glob('still-*.jpg')):
    t = f.stem.split('-')[1]
    vp = json.load(open(stills / f'view-{t}.json'))
    hx, hy = project(vp['viewProj'], (vp['hero'][0], 0.9, vp['hero'][2]))
    im = Image.open(f).convert('RGB')
    x0 = int(max(0, min(1920 - 480, hx - 240))); y0 = int(max(0, min(1080 - 270, hy - 135)))
    row = Image.new('RGB', (960 + 960, 540 + 20))
    row.paste(im.resize((960, 540), Image.LANCZOS), (0, 20))
    row.paste(im.crop((x0, y0, x0 + 480, y0 + 270)).resize((960, 540), Image.LANCZOS), (960, 20))
    ImageDraw.Draw(row).text((6, 4), f'{label}  trailer {float(t):.1f} s', fill=(255, 255, 255))
    rows.append(row)
sheet = Image.new('RGB', (1920, 560 * len(rows)))
for i, r in enumerate(rows): sheet.paste(r, (0, 560 * i))
save_tagged(sheet, out, f'{label} views')
