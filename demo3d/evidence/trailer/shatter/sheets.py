"""sheets.py DUMP_DIR STILLS_DIR OUT_DIR OFFSETS (run from the repo root; see README.md): one strip per crystal break: frames at offsets (in 60 fps frames) from the impact
frame, cropped round the hero and the formation, labelled; plus the list of breaks."""
import json, sys, glob, math
from pathlib import Path
from PIL import Image, ImageDraw
sys.path.insert(0, 'demo3d/tools/src')
from demo3d_tools.filmstrip import save_tagged

dump, stills, out = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
offs = [int(x) for x in sys.argv[4].split(',')]
tr = json.load(open('demo3d/Assets/Demo3D/Choreography/Data/trailer-crystal.json'))
TW, TH = 400, 260        # tile
CW, CH = 1040, 676       # crop from the 1920x1080 frame (same aspect as the tile)

def project(vp, p):
    x = vp[0]*p[0] + vp[1]*p[1] + vp[2]*p[2] + vp[3]
    y = vp[4]*p[0] + vp[5]*p[1] + vp[6]*p[2] + vp[7]
    w = vp[12]*p[0] + vp[13]*p[1] + vp[14]*p[2] + vp[15]
    return ((x/w + 1) * 0.5 * 1920, (1 - (y/w + 1) * 0.5) * 1080)

rows = []
k = 0
for f in sorted(glob.glob(dump + '/shot-*.json'), key=lambda x: int(x.split('-')[-1].split('.')[0])):
    n = int(f.split('-')[-1].split('.')[0])
    d = json.load(open(f))
    crystals = {c['placement']: c for c in d.get('crystals', [])}
    for b in d.get('breaks', []):
        c = crystals.get(b['placement'])
        if not c: continue
        k += 1
        tau = b['time'] - 121.4
        f0 = math.ceil(tau * 60 - 1e-6)
        times = [(f0 + o) / 60 for o in offs]
        vp = json.load(open(stills / f'view-{times[offs.index(0)]:07.3f}.json'))
        m = vp['viewProj']; hero = vp['hero']
        a = project(m, (c['at'][0], 0, c['at'][1])); top = project(m, (c['at'][0], c['heightM'], c['at'][1]))
        h = project(m, (hero[0], 0, hero[2]))
        cx = (min(a[0], top[0], h[0]) + max(a[0], top[0], h[0])) / 2
        cy = (min(a[1], top[1], h[1]) + max(a[1], top[1], h[1])) / 2
        x0 = int(max(0, min(1920 - CW, cx - CW / 2))); y0 = int(max(0, min(1080 - CH, cy - CH / 2)))
        sheet = Image.new('RGB', (TW * len(times), TH + 18), (16, 16, 16))
        draw = ImageDraw.Draw(sheet)
        for i, (t, o) in enumerate(zip(times, offs)):
            im = Image.open(stills / f'still-{t:07.3f}.jpg').convert('RGB').crop((x0, y0, x0 + CW, y0 + CH)).resize((TW, TH), Image.LANCZOS)
            sheet.paste(im, (i * TW, 18))
            label = 'impact' if o == 0 else f'{o:+d} f'
            draw.text((i * TW + 4, 3), f'{label}  t={t:.3f}', fill=(255, 220, 120) if o == 0 else (220, 220, 220))
        name = f'break-{k:02d}.jpg'
        save_tagged(sheet, out / name, f'crystal break {k}')
        rows.append((k, n, tr['shots'][n - 1]['environment'], tau, b['target'], name))
for r in rows: print(r)
