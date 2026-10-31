"""Where Magic Wand's and King Bible's shots, bolts and hits fall on the beat grid, from a player's --events-out CSV."""
import bisect, csv, sys
rows = list(csv.DictReader(open(sys.argv[1])))
beats = [float(r['t']) for r in rows if r['kind'] == 'Beat']
WAND, BIBLE = 4, 6
def phase(t, per):
    b = bisect.bisect_right(beats, t + 1e-9) - 1
    if b < 0 or b + 1 >= len(beats): return None
    return (t - beats[b]) / (beats[b + 1] - beats[b]) * per
def report(name, times, per, unit):
    on = off = 0
    for t in times:
        p = phase(t, per)
        if p is None: continue
        if abs(p - round(p)) < 1e-3: on += 1
        else: off += 1
    total = on + off
    print(f"| {name} | {total} | {on} ({100 * on / max(1, total):.1f}%) on {unit} |")
shots = [float(r['t']) for r in rows if r['kind'] == 'Attack' and int(r['i0']) == -1 and int(r['i2']) >> 4 == WAND]
launch = [float(r['t']) for r in rows if r['kind'] == 'Projectile' and int(r['i2']) & 3 == 0 and int(r['i2']) >> 4 == WAND]
hits = [float(r['t']) for r in rows if r['kind'] == 'Hit' and int(r['i0']) >= 0 and int(r['i2']) == WAND]
seen, rings = set(), []
for r in rows:
    if r['kind'] == 'Orbit' and int(r['i2']) >> 4 == BIBLE and r['i0'] not in seen:
        seen.add(r['i0']); rings.append(float(r['t']))
snapped = sum(1 for r in rows if r['kind'] == 'Orbit' and int(r['i2']) >> 4 == BIBLE and int(r['i2']) & 1)
bhits = [float(r['t']) for r in rows if r['kind'] == 'Hit' and int(r['i0']) >= 0 and int(r['i2']) == BIBLE]
print("| what | count | on the grid |\n| --- | --- | --- |")
report("Magic Wand shots", shots, 2, "eighths")
report("Magic Wand bolts (a burst's 0.1 s apart, as native's)", launch, 4, "sixteenths")
report("Magic Wand hits", hits, 4, "sixteenths")
report("King Bible rings", rings, 1, "beats")
print(f"\nKing Bible: {len(rings)} rings ({snapped} announcements snapped to the beat), {len(bhits)} hits; Magic Wand: {len(launch)} bolts.")
