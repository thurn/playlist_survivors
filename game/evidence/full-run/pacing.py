"""Pacing of a full run from the player's --events-out CSV: run clock, levels, chests, evolutions, freezes."""
import csv, json, sys
from pathlib import Path
root = Path(__file__).resolve().parents[3]
W = json.load(open(root / 'game/Content/weapons.json'))['weapons']
P = json.load(open(root / 'game/Content/passives.json'))['passives']
REASONS = ['level-up', 'chest', 'pause', 'music stopped']
rows = list(csv.DictReader(open(sys.argv[1])))
start = next(float(r['t']) for r in rows if r['kind'] == 'RunStart')
held, frozen_from, intervals, frozen_by = set(), None, [], {r: 0.0 for r in REASONS}
first_reason = None
for r in rows:
    if r['kind'] != 'Freeze': continue
    t, reason, starts = float(r['t']), int(r['i0']), r['i1'] == '1'
    if starts:
        if not held: frozen_from, first_reason = t, reason
        held.add(reason)
    else:
        held.discard(reason)
        if not held and frozen_from is not None:
            intervals.append((frozen_from, t)); frozen_by[REASONS[first_reason]] += t - frozen_from; frozen_from = None
def clock(t):
    """The run clock at song time t: playing time, stopping at 30:00 (the finale's wait and the card don't count)."""
    c = t - start
    for a, b in intervals:
        if a < t: c -= min(t, b) - a
    return min(c, 1800.0)
def card(bits):
    code = bits & 63
    return 'heal' if code == 0 else W[code - 1]['id'] if code < 32 else P[code - 32]['id']
level_at, level = {}, 1
chests, evolutions, end = [], [], None
for r in rows:
    k, t = r['kind'], float(r['t'])
    if k == 'LevelUp': level = int(r['i0']); level_at[int(clock(t) // 60)] = level
    elif k == 'Chest':
        phase, tier = int(r['i1']) & 15, int(r['i1']) >> 4
        if phase == 0: chests.append({'song': round(t, 2), 'clock': round(clock(t), 1), 'minute': int(r['i2']), 'tier': tier})
        elif phase == 3:
            item = card(int(r['i2']))
            if any(w['id'] == item and w.get('evolutionOf') for w in W): evolutions.append((item, round(clock(t), 1)))
    elif k == 'RunEnd': end = (t, clock(t), r['i0'])
    elif k == 'Finale': finale = (t, clock(t))
lv, by_minute = 1, []
for m in range(31):
    lv = max([v for mm, v in level_at.items() if mm < m] + [lv]) if m else 1
    by_minute.append(lv)
print(json.dumps({'start': start, 'end_song': end[0], 'end_clock': round(end[1], 2), 'end': end[2], 'finale_song': finale[0],
    'final_level': level, 'level_at_minute': {m: by_minute[m] for m in (5, 10, 15, 20, 25, 30)},
    'chests': chests, 'evolutions': evolutions, 'frozen_s': {k: round(v, 1) for k, v in frozen_by.items()},
    'freezes': len(intervals)}, indent=1))
