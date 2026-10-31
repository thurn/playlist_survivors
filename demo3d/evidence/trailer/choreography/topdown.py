"""Top-down frames of a trailer shot's timeline, monsters coloured by type, as a ps-render-tagged sheet.

    dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --shot N --out-dir DIR
    uv run --project demo3d/tools python -I demo3d/evidence/trailer/choreography/topdown.py \\
        DIR/shot-N.json OUT.jpg LABEL YAW T...

Each frame is centred on the camera's focus at song time T and turned by the shot's heading YAW (its
yawDeg), so up is the camera's forward, as in the player's stills. The hero is the yellow dot, the
yellow circle the open-lane check's 6 m, and the hero's blow on that beat is marked. Four across.
"""

import json
import math
import sys

from PIL import Image, ImageDraw

COLOURS = {"skeleton": (235, 235, 235), "goblin": (60, 200, 60), "slime": (60, 220, 220), "bat": (165, 70, 225),
           "mushroom": (245, 145, 40), "demon": (230, 45, 45), "bigork": (150, 105, 45), "troll": (85, 85, 210),
           "boss": (255, 0, 255)}
TILE, COLUMNS, RING_M = 320, 4, 6.0

src, out, label, yaw = sys.argv[1], sys.argv[2], sys.argv[3], math.radians(float(sys.argv[4]))
times = [float(t) for t in sys.argv[5:]]
d = json.load(open(src))
types = d["types"]
segs = {}
for s in d["segments"]:
    segs.setdefault(s["entity"], []).append(s)
death = {}
for a in d["actions"]:
    if a["kind"] == "death":
        death.setdefault(a["entity"], a["impact"])
blows = [a["impact"] for a in d["actions"] if a["entity"] == 0 and a["kind"] == "attack"]
start = d["entities"][0]["spawn"]


def position(e, t):
    """Where entity e stands at t, linearly along its segment (exact at the beats, where steps start)."""
    seg = None
    for s in segs.get(e, []):
        if s["t0"] > t:
            break
        seg = s
    if seg is None:
        return None
    if t >= seg["t1"] or seg["t1"] <= seg["t0"]:
        return seg["to"]
    u = (t - seg["t0"]) / (seg["t1"] - seg["t0"])
    return [seg["from"][i] + (seg["to"][i] - seg["from"][i]) * u for i in (0, 1)]


rows = (len(times) + COLUMNS - 1) // COLUMNS
sheet = Image.new("RGB", (COLUMNS * TILE, rows * (TILE + 18)), (14, 14, 16))
draw = ImageDraw.Draw(sheet)
for k, t in enumerate(times):
    fx, fy, width = d["framing"][min(len(d["framing"]) - 1, max(0, round((t - start) * d["framingRate"])))]
    scale = TILE / (1.2 * width)
    ox, oy = (k % COLUMNS) * TILE, (k // COLUMNS) * (TILE + 18)

    def screen(p):
        # Turned by the heading (clockwise seen from above), so the camera's forward is up.
        x, y = p[0] - fx, p[1] - fy
        rx, ry = x * math.cos(yaw) - y * math.sin(yaw), x * math.sin(yaw) + y * math.cos(yaw)
        return ox + TILE / 2 + rx * scale, oy + TILE / 2 - ry * scale

    for e in d["entities"][1:]:
        if e["kind"] != "monster" or not e["spawn"] <= t < e["despawn"] or death.get(e["id"], 1e9) <= t:
            continue
        p = position(e["id"], t)
        if p is None:
            continue
        x, y = screen(p)
        r = 4.5 if types[e["type"]] in ("bigork", "troll", "boss") else 2.3
        draw.ellipse([x - r, y - r, x + r, y + r], fill=COLOURS.get(types[e["type"]], (255, 255, 0)))
    hx, hy = screen(position(0, t))
    draw.ellipse([hx - 5, hy - 5, hx + 5, hy + 5], fill=(255, 220, 0))
    ring = RING_M * scale
    draw.ellipse([hx - ring, hy - ring, hx + ring, hy + ring], outline=(255, 220, 0))
    blow = " blow" if any(abs(t - b) < 0.02 for b in blows) else ""
    draw.text((ox + 4, oy + TILE + 3), f"{t:.3f} s{blow}", fill=(220, 220, 220))
sheet.save(out, "JPEG", quality=84, comment=f"ps-render trailer choreography {label}".encode())
print(out)
