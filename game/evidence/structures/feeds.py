"""Writes the structures' synthetic event feeds (README.md): `python3 game/evidence/structures/feeds.py`,
checks a player run's `--structures-out` CSV against a feed's breaks: `feeds.py check FEED.jsonl RUN.csv`,
and summarises a bench run's CSV: `feeds.py bench RUN.csv`.

Both feeds are in the frame loop's `--events` format (ScriptedEvents), on 128 BPM beats from 1 s,
in the game's arena (game/Content/arena.json), whose sites are indexed as in its `structures`.

- moments.jsonl: the hero strides north from its start (0.75, -2.25) to (0.75, 3.75) and breaks the
  small site 7 with a heavy spin on beat 8 (4.75 s, a frame); strides west to (-6.75, 3.75) and
  takes the large site 6 (9 tiles: three breaking hits) with heavy spins on beats 20 and 22 (glints
  toward the break, off the frame grid), a light slash on the eighth after beat 21 (a glint that
  can't break) and the break on beat 24 (12.25 s, a frame); strides two tiles back east, and site 7
  regrows on beat 32 (16.0 s), the hero 3.2 m from it, as the sim would allow.
- bench.jsonl: every site breaks alone, one every 4 s from 1 s (their shards sink within 3.5 s), so
  each break's physics stands alone; then all regrow at 153 s and all break at once at 157 s, a
  bound far past any real moment.
"""

from __future__ import annotations

import csv
import json
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ARENA = HERE.parents[1] / "Content" / "arena.json"
BPM = 128
FIRST_BEAT = 1.0
WINDUP = 0.32
STEP_S = 0.11
HEAVY_THROW = 4.63  # FeelMoments.HeavyThrowM: the sim's heavy launch (m)
SLASH_L, SLASH_R, FOLLOW_UP, SPIN, SLAM = range(5)  # HeroAttackKind
UP, DOWN, LEFT, RIGHT = range(4)  # Direction
HEAVY_STRIKE, CLEAVE = 1, 2  # StructureHit (Strike = 0)


def beat(k: float) -> float:
    return FIRST_BEAT + k * 60.0 / BPM


def arena() -> dict:
    return json.loads(ARENA.read_text(encoding="utf-8"))


def tile_box(a: dict, tile: list[int]) -> tuple[float, float, float, float]:
    ox, oy = a["origin"]
    m = a["tileM"]
    x0, y0 = ox + tile[0] * m, oy + tile[1] * m
    return x0, y0, x0 + m, y0 + m


def nearest(a: dict, site: int, x: float, y: float) -> tuple[float, float]:
    """The site's point nearest (x, y), as the sim's Structures.Nearest."""
    best, at = math.inf, (x, y)
    for t in a["structures"][site]["tiles"]:
        x0, y0, x1, y1 = tile_box(a, t)
        px, py = min(max(x, x0), x1), min(max(y, y0), y1)
        d = (px - x) ** 2 + (py - y) ** 2
        if d < best:
            best, at = d, (px, py)
    return at


def centre(a: dict, site: int) -> tuple[float, float]:
    boxes = [tile_box(a, t) for t in a["structures"][site]["tiles"]]
    return (sum(b[0] + b[2] for b in boxes) / (2 * len(boxes)), sum(b[1] + b[3] for b in boxes) / (2 * len(boxes)))


def hit(a: dict, site: int, hx: float, hy: float) -> tuple[float, float, float, float]:
    """A hit from (hx, hy): the point and the unit direction from the hero through it."""
    px, py = nearest(a, site, hx, hy)
    dx, dy = px - hx, py - hy
    n = math.hypot(dx, dy) or 1.0
    return px, py, dx / n, dy / n


def strike(t: float, kind: int, heavy: bool, aim: tuple[float, float]) -> dict:
    return {"kind": "Attack", "t": t - WINDUP, "i": [-1, kind, int(heavy)],
            "d": [t - WINDUP, t, aim[0], aim[1], 7.04 * (1.3 if heavy else 1)]}


def walk(start: float, frm: tuple[float, float], dx: float, dy: float, n: int, facing: int, first: int) -> list[dict]:
    """n strides of a tile along (dx, dy) from `frm`, one per beat from `start`."""
    out, (x, y) = [], frm
    for k in range(n):
        t = start + k * 60.0 / BPM
        nx, ny = x + 1.5 * dx, y + 1.5 * dy
        out.append({"kind": "HeroStep", "t": t, "i": [first + k, 0, facing], "d": [x, y, nx, ny, t, t + STEP_S]})
        x, y = nx, ny
    return out


def moments() -> list[dict]:
    a = arena()
    ev = [{"kind": "HeroStep", "t": 0.5, "i": [0, 4, UP], "d": [0.75, -2.25, 0.75, -2.25, 0.5, 0.61]}]
    ev += walk(beat(2), (0.75, -2.25), 0, 1, 4, UP, 1)
    hx, hy = 0.75, 3.75
    px, py, dx, dy = hit(a, 7, hx, hy)
    ev.append(strike(beat(8), SPIN, True, (dx, dy)))
    ev.append({"kind": "StructureBreak", "t": beat(8), "i": [7, HEAVY_STRIKE], "d": [px, py, dx, dy, HEAVY_THROW]})
    ev += walk(beat(12), (hx, hy), -1, 0, 5, LEFT, 5)
    hx = -6.75
    px, py, dx, dy = hit(a, 6, hx, hy)
    for k, hits in ((20, 1), (22, 2)):
        ev.append(strike(beat(k), SPIN, True, (dx, dy)))
        ev.append({"kind": "Glint", "t": beat(k), "i": [6, HEAVY_STRIKE, hits], "d": [px, py, dx, dy]})
    ev.append(strike(beat(21.5), SLASH_R, False, (dx, dy)))
    ev.append({"kind": "Glint", "t": beat(21.5), "i": [6, 0, 0], "d": [px, py, dx, dy]})
    ev.append(strike(beat(24), SPIN, True, (dx, dy)))
    ev.append({"kind": "StructureBreak", "t": beat(24), "i": [6, HEAVY_STRIKE], "d": [px, py, dx, dy, HEAVY_THROW]})
    ev += walk(beat(26), (hx, hy), 1, 0, 2, RIGHT, 10)
    period = 60.0 / BPM
    ev.append({"kind": "Regrowth", "t": beat(32), "i": [7], "d": [beat(32), beat(32) + period]})
    return ev


def bench() -> list[dict]:
    a = arena()
    n = len(a["structures"])
    ev = []

    def brk(t: float, site: int) -> None:
        cx, cy = centre(a, site)
        # From 2 m south of the site's centre, as a heavy strike would come.
        px, py, dx, dy = hit(a, site, cx, cy - 2 - 1.5 * len(a["structures"][site]["tiles"]) ** 0.5)
        ev.append({"kind": "StructureBreak", "t": t, "i": [site, HEAVY_STRIKE], "d": [px, py, dx, dy, HEAVY_THROW]})

    for s in range(n):
        brk(1 + 4 * s, s)
    for s in range(n):
        ev.append({"kind": "Regrowth", "t": 153.0, "i": [s], "d": [153.0, 153.5]})
    for s in range(n):
        brk(157.0, s)
    return ev


def write(name: str, events: list[dict]) -> None:
    path = HERE / name
    with path.open("w", encoding="utf-8") as f:
        f.write(f"# {name[:-6]}: written by feeds.py\n")
        for e in sorted(events, key=lambda e: e["t"]):
            f.write(json.dumps(e) + "\n")
    print(f"{path.relative_to(HERE.parents[2])}: {len(events)} events")


def summarise(run: Path) -> None:
    """Each moment's physics cost from the bench's CSV: from a frame where breaks start (or, with no new
    break, shards appear: the regrowths' bursts) until the shards are gone."""
    rows = list(csv.DictReader(run.open(encoding="utf-8")))
    moments, cur, prev_breaks, prev_shards = [], None, 0, 0
    for r in rows:
        b, shards, steps = int(r["breaks"]), int(r["shards"]), int(r["steps"])
        ms, shadow = float(r["physics_ms"]), float(r["shadow_ms"])
        if b > prev_breaks or (shards > 0 and prev_shards == 0):
            cur = {"at": float(r["frame_song"]), "breaks": b - prev_breaks, "shards": 0, "frames": 0, "steps": 0,
                   "ms": 0.0, "max": 0.0, "shadow": shadow}
            moments.append(cur)
        if cur is not None:
            cur["shards"] = max(cur["shards"], shards)
            cur["frames"] += 1
            cur["steps"] += steps
            cur["ms"] += ms
            cur["max"] = max(cur["max"], ms)
            if shards == 0 and cur["frames"] > 1:
                cur = None
        prev_breaks, prev_shards = b, shards
    print("at_s,breaks,peak_shards,frames,steps,physics_ms_total,ms_per_step,max_frame_ms,shadow_redraw_ms")
    for m in moments:
        print(f"{m['at']:.3f},{m['breaks']},{m['shards']},{m['frames']},{m['steps']},{m['ms']:.2f},"
              f"{m['ms'] / max(1, m['steps']):.4f},{m['max']:.3f},{m['shadow']:.2f}")
    single = [m for m in moments if m["breaks"] == 1]
    if single:
        def med(xs: list[float]) -> float:
            return sorted(xs)[len(xs) // 2]
        print(f"# {len(single)} single breaks: physics per break median {med([m['ms'] for m in single]):.2f} ms "
              f"(max {max(m['ms'] for m in single):.2f}), worst frame median {med([m['max'] for m in single]):.3f} ms "
              f"(max {max(m['max'] for m in single):.3f}), peak shards median {med([m['shards'] for m in single])} "
              f"(max {max(m['shards'] for m in single)}); shadow redraw median {med([m['shadow'] for m in single]):.2f} ms "
              f"(max {max(m['shadow'] for m in single):.2f})")
    redraws = sorted(float(r["shadow_ms"]) for r in rows if float(r["shadow_ms"]) > 0)
    if redraws:
        print(f"# static shadow map redraws (breaks and whole regrowths): {len(redraws)}, "
              f"median {redraws[len(redraws) // 2]:.2f} ms, max {redraws[-1]:.2f} ms")


def check(feed: Path, run: Path) -> int:
    """Every break of the feed: nothing before its frame; on the first frame at or after it, the site's
    shards are out and some already moved; 50 ms later every one of them is in flight."""
    breaks = [json.loads(line)["t"] for line in feed.open(encoding="utf-8")
              if not line.startswith("#") and json.loads(line)["kind"] == "StructureBreak"]
    rows = [{k: float(v) for k, v in r.items()} for r in csv.DictReader(run.open(encoding="utf-8"))]
    failed = 0
    for k, t in enumerate(sorted(breaks), start=1):
        before = [r for r in rows if r["frame_song"] < t - 1e-9]
        at = next(r for r in rows if r["frame_song"] >= t - 1e-9)
        later = next(r for r in rows if r["frame_song"] >= t + 0.05 - 1e-9)
        ok = (before[-1]["breaks"] == k - 1 and at["breaks"] == k and at["flying"] > 0 and at["moved_m"] > 0
              and later["flying"] == later["shards"] > 0)
        failed += not ok
        print(f"{'ok' if ok else 'FAIL'} break {k} at {t:.4f}: frame {before[-1]['frame_song']:.4f} breaks "
              f"{before[-1]['breaks']:.0f}; impact frame {at['frame_song']:.4f}: {at['flying']:.0f} of {at['shards']:.0f} "
              f"shards flying, moved {at['moved_m']:.3f} m; {later['frame_song']:.4f}: {later['flying']:.0f} of "
              f"{later['shards']:.0f} flying")
    return 1 if failed else 0


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "bench":
        summarise(Path(sys.argv[2]))
    elif len(sys.argv) == 4 and sys.argv[1] == "check":
        sys.exit(check(Path(sys.argv[2]), Path(sys.argv[3])))
    else:
        write("moments.jsonl", moments())
        write("bench.jsonl", bench())
