"""Writes the synthetic event feeds (--events) for game/evidence/fx: the sim's sword events on
house-128's beats (native/fixtures/entries/house-128-20s.jsonl), shaped as the sim emits them
(Sim/Events/SimEvent.cs), around the hero's start in the open arena (0.75, 0.75).

- slice.jsonl: 40 enemies within the reach, slashes on the beats from 1.91 s with hits on five of
  them, then a heavy spin on the accent at 7.53 s that kills all 40 and sends a cleave wave
  through 30 more beyond the reach, which die band by band; a slam at 10.34 s.
- sounds.jsonl: the sync probe's feed, 2 to 19 s: a swing and a hit on every beat, a death on
  every fourth (its shatter's tinkle lands before the next beat, which has no strike), a hurt
  a little after the beat on the beats after a strike, and gem pickups; no two sounds of
  different instants within 50 ms, so the probe's clicks stay apart.

Run from the repository root: python3 game/evidence/fx/feeds.py
"""

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
HERO = (0.75, 0.75)
REACH, HEAVY_REACH, OUTER, BANDS = 7.04, 9.152, 15.07, 7
WINDUP = 0.32
STRIKE, HEAVY, CLEAVE = 0, 1, 2  # WeaponHit
SLASH_LEFT, SLASH_RIGHT, SPIN, SLAM = 0, 1, 3, 4  # HeroAttackKind


def beats() -> list[float]:
    lines = (ROOT / "native/fixtures/entries/house-128-20s.jsonl").read_text().splitlines()[1:]
    return [e["t"] for e in map(json.loads, lines) if e["kind"] == 1]


def ev(kind: str, t: float, i: list[int], d: list[float]) -> dict:
    return {"kind": kind, "t": round(t, 6), "i": i, "d": [round(x, 6) for x in d]}


def attack(impact: float, kind: int, heavy: bool, angle: float, reach: float) -> dict:
    dx, dy = math.cos(angle), math.sin(angle)
    return ev("Attack", impact - WINDUP, [-1, kind, int(heavy)], [impact - WINDUP, impact, dx, dy, reach])


def ring(first: int, n: int, radius: float, phase: float = 0.0) -> list[tuple[int, float, float, float]]:
    out = []
    for k in range(n):
        a = phase + 2 * math.pi * k / n
        out.append((first + k, HERO[0] + radius * math.cos(a), HERO[1] + radius * math.sin(a), a))
    return out


def slice_feed(b: list[float]) -> list[dict]:
    feed = []
    inner = ring(0, 16, 3.5) + ring(16, 24, 5.5, 0.13)
    outer = ring(40, 10, 10.0, 0.05) + ring(50, 10, 12.0, 0.2) + ring(60, 10, 14.2, 0.35)
    for e, x, y, _ in inner + outer:
        feed.append(ev("Spawn", 0.5, [e, e % 4, 0], [x, y]))
    accent = min(b, key=lambda t: abs(t - 7.53))
    period = b[b.index(accent) + 1] - accent
    side = 0
    for t in b:
        if t < 1.9 or t >= accent:
            continue
        angle = 0.7 * b.index(t)
        feed.append(attack(t, SLASH_LEFT + side, False, angle, REACH))
        side ^= 1
        for e, x, y, a in inner[(b.index(t) * 5) % 40:][:5]:
            feed.append(ev("Hit", t, [e, STRIKE], [1.0, x, y, math.cos(a), math.sin(a)]))
    feed.append(attack(accent, SPIN, True, 0.0, HEAVY_REACH))
    for e, x, y, a in inner:
        feed.append(ev("Hit", accent, [e, HEAVY], [30.0, x, y, math.cos(a), math.sin(a)]))
        feed.append(ev("Death", accent, [e, e % 4], [x, y, math.cos(a), math.sin(a), 4.63]))
    first, band_s = accent + period / 2, period / 4
    feed.append(ev("Cleave", accent, [0, BANDS], [HERO[0], HERO[1], HEAVY_REACH, OUTER, first, band_s]))
    width = (OUTER - HEAVY_REACH) / BANDS
    for e, x, y, a in outer:
        r = math.hypot(x - HERO[0], y - HERO[1])
        k = max(0, math.ceil((r - HEAVY_REACH) / width) - 1)
        t = first + k * band_s
        feed.append(ev("Hit", t, [e, CLEAVE], [30.0, x, y, math.cos(a), math.sin(a)]))
        feed.append(ev("Death", t, [e, e % 4], [x, y, math.cos(a), math.sin(a), 1.85]))
    slam = min(b, key=lambda t: abs(t - 10.34))
    feed.append(attack(slam, SLAM, True, math.pi / 2, HEAVY_REACH))
    return feed


def sounds_feed(b: list[float]) -> list[dict]:
    feed = []
    enemy, item = 0, 0
    for k, t in enumerate(b):
        if t < 2 or t > 19:
            continue
        if k % 4 == 1:
            continue  # the shatters of the beat before ring out
        feed.append(attack(t, SLASH_LEFT + k % 2, False, 0.5 * k, REACH))
        feed.append(ev("Hit", t, [enemy, STRIKE], [1.0, 3.0, 0.75, 1, 0]))
        if k % 4 == 0:
            feed.append(ev("Death", t, [enemy, 0], [3.0, 0.75, 1, 0, 1.85]))
            enemy += 1
        if k % 4 == 2:
            feed.append(ev("Hit", t + 0.18, [-1, 0], [10.0, HERO[0], HERO[1], 1, 0]))
        if k % 4 == 3:
            feed.append(ev("Pickup", t + 0.2, [item, 0], [HERO[0], HERO[1], 1.0]))
            item += 1
    return feed


def write(name: str, header: str, feed: list[dict]) -> None:
    feed.sort(key=lambda e: e["t"])
    text = f"# {header}\n" + "".join(json.dumps(e) + "\n" for e in feed)
    (OUT / name).write_text(text)
    print(f"{name}: {len(feed)} events")


if __name__ == "__main__":
    b = beats()
    write("slice.jsonl", "The sword on house-128 (feeds.py): slashes, a heavy spin killing 40 and its cleave wave through 30 more, a slam.",
          slice_feed(b))
    write("sounds.jsonl", "The sound probe's feed on house-128 (feeds.py): impact sounds on the beats, hurts on the sixteenth, chimes.",
          sounds_feed(b))
