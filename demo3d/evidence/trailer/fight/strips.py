"""strips.py DUMP_DIR CUT (run from the repo root with `uv run --project demo3d/tools python`; see README.md):
prints the still times of shot 8's boss attacks for --stills-at, or with STILLS_DIR OUT_DIR builds one strip per
attack: 8 tiles, each a 1280 x 720 crop of the 1080p frame centred between the hero and the zone's origin (fixed
per attack, from its dodge's first still), labelled with the trailer second."""
import json, sys, math
from pathlib import Path
from PIL import Image, ImageDraw
sys.path.insert(0, 'demo3d/tools/src')
from demo3d_tools.filmstrip import save_tagged

START = 121.4
dump, cut = Path(sys.argv[1]), sys.argv[2]
attacks = json.load(open(dump / 'shot-8.json'))['bossAttacks']

def frames(a):
    # A still at song time t shows the frame after the last one before t, so each lands just past its instant.
    at = lambda t: round(math.ceil((t - START) * 1000) / 1000 + 0.001, 3)
    imp = at(a['impact'])
    return [('windup +0.05', at(a['windup'] + 0.05)), ('telegraph', at((a['windup'] + a['dodgeFrom']) / 2)),
            ('dodge', at(a['dodgeFrom'])), ('dodge +0.12', at(a['dodgeFrom'] + 0.117)), ('impact -1f', round(imp - 1 / 60, 3)),
            ('impact', imp), ('impact +3f', round(imp + 3 / 60, 3)), ('impact +12f', round(imp + 12 / 60, 3))]

if len(sys.argv) == 3:
    print(','.join(f'{t:.3f}' for a in attacks for _, t in frames(a)))
    sys.exit()
stills, out = Path(sys.argv[3]), Path(sys.argv[4])
TW, TH, CW, CH = 960, 540, 1280, 720

def project(vp, p):
    x = vp[0]*p[0] + vp[1]*p[1] + vp[2]*p[2] + vp[3]
    y = vp[4]*p[0] + vp[5]*p[1] + vp[6]*p[2] + vp[7]
    w = vp[12]*p[0] + vp[13]*p[1] + vp[14]*p[2] + vp[15]
    return ((x/w + 1) * 0.5 * 1920, (1 - (y/w + 1) * 0.5) * 1080)

for k, a in enumerate(attacks, 1):
    fs = frames(a)
    vp = json.load(open(stills / f'view-{fs[2][1]:07.3f}.json'))
    h = project(vp['viewProj'], (vp['hero'][0], 0, vp['hero'][2]))
    o = project(vp['viewProj'], (a['origin'][0], 0, a['origin'][1]))
    x0 = int(max(0, min(1920 - CW, (h[0] + o[0]) / 2 - CW / 2))); y0 = int(max(0, min(1080 - CH, (h[1] + o[1]) / 2 - CH / 2)))
    sheet = Image.new('RGB', (TW * 4, (TH + 24) * 2), (0, 0, 0))
    dr = ImageDraw.Draw(sheet)
    for i, (label, t) in enumerate(fs):
        im = Image.open(stills / f'still-{t:07.3f}.jpg').convert('RGB').crop((x0, y0, x0 + CW, y0 + CH)).resize((TW, TH), Image.LANCZOS)
        x, y = (i % 4) * TW, (i // 4) * (TH + 24)
        sheet.paste(im, (x, y + 24))
        dr.text((x + 6, y + 6), f'{label}  {t:.3f} s', fill=(255, 255, 255))
    save_tagged(sheet, out / f'{cut}-{k}-{a["kind"]}.jpg', f'{cut} boss attack {k}')
