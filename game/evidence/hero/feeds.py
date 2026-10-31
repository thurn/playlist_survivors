"""Writes the hero's synthetic event feeds (README.md): `python3 game/evidence/hero/feeds.py`, and
checks a player run's `--hero-out` CSV against one: `feeds.py check FEED.jsonl RUN.csv`.

For each tempo, hero-BPM.jsonl (attacks only) and hero-BPM-steps.jsonl (the same attacks over
stepping), in the frame loop's `--events` format (ScriptedEvents): Beat events, and the sim's
hero Attack events, each landing its impact on a beat or, for follow-ups, on the eighth after it,
winding up 0.32 s ahead or from the impact before. Beats start at 1 s. The steps stride on the
stride grid (the beat halved to at most 0.4 s), 110 ms each, a tile east and back from the
sim's open-arena start (0.75, 0.75), with a bump at the east end and a late stride on the way back.
Every feed first stomps facing east (stomp()).
"""

from __future__ import annotations

import csv
import json
import sys
from pathlib import Path

WINDUP = 0.32
TILE = 1.5
START = (0.75, 0.75)
FIRST_BEAT = 1.0
BEATS = 12
# HeroAttackKind and Direction (the sim's enums).
SLASH_L, SLASH_R, FOLLOW_UP, SPIN, SLAM = range(5)
UP, DOWN, LEFT, RIGHT = range(4)

# Per beat (from beat 1): the strike's kind, whether it is heavy, its aim, and a follow-up on the
# eighth after it. Aims turn the knight to face the camera, show its profile and its back.
PLAN = [
    (SLASH_L, False, (0.0, -1.0), False),
    (SLASH_R, False, (1.0, 0.0), False),
    (SLASH_L, False, (0.0, -1.0), True),
    (SPIN, True, (-1.0, 0.0), False),
    (SLASH_R, False, (0.7, -0.7), False),
    (SLASH_L, False, (0.0, -1.0), True),
    (SLAM, True, (0.0, -1.0), False),
    (SLASH_R, False, (-0.7, -0.7), False),
    (SLASH_L, False, (1.0, 0.0), False),
    (SPIN, True, (0.0, -1.0), False),
]


def beat_time(bpm: float, k: int) -> float:
    return FIRST_BEAT + k * 60.0 / bpm


def stride_period(bpm: float) -> float:
    p = 60.0 / bpm
    while p > 0.4 + 1e-9:
        p /= 2
    return p


def beats(bpm: float) -> list[dict]:
    p = 60.0 / bpm
    return [{"kind": "Beat", "t": beat_time(bpm, k), "i": [k, k // 2, int(k % 2 == 0) | (2 << 1)], "d": [p, 0.9]}
            for k in range(BEATS)]


def attacks(bpm: float) -> list[dict]:
    p = 60.0 / bpm
    out, last = [], float("-inf")

    def strike(kind: int, heavy: bool, aim: tuple[float, float], impact: float) -> None:
        nonlocal last
        windup = max(impact - WINDUP, last)
        out.append({"kind": "Attack", "t": windup, "i": [-1, kind, int(heavy)],
                    "d": [windup, impact, aim[0], aim[1], 7.04 * (1.3 if heavy else 1)]})
        last = impact

    for k, (kind, heavy, aim, follow) in enumerate(PLAN, start=1):
        strike(kind, heavy, aim, beat_time(bpm, k))
        if follow:
            strike(FOLLOW_UP, False, aim, beat_time(bpm, k) + p / 2)
    return out


def steps(bpm: float) -> list[dict]:
    period = stride_period(bpm)
    out, x = [], START[0]
    # East, a bump, west (late), east, west.
    moves = [1, 0, -1, 1, -1]
    t0 = beat_time(bpm, 1)
    n = 0
    while t0 + n * period < beat_time(bpm, len(PLAN)) and n < 64:
        move = moves[n % len(moves)]
        step = t0 + n * period
        late = n % len(moves) == 2
        start = step + 0.05 if late else step
        end = min(start + 0.11, step + period)
        bump = move == 0
        to = x + move * TILE
        facing = RIGHT if move > 0 or bump else LEFT
        flags = [n, int(late) | (2 if bump else 0), facing]
        out.append({"kind": "HeroStep", "t": start, "i": flags, "d": [x, START[1], to, START[1], start, end]})
        x = to
        n += 1
    return out


# The clip each attack kind plays (HeroMotion.ClipFor), and each clip's measured impact (hero-clips.json).
CLIPS = json.loads((Path(__file__).resolve().parents[2] / "Content" / "hero-clips.json").read_text(encoding="utf-8"))
IMPACT = {c["role"].lower(): c["impact"] for c in CLIPS["clips"] if "impact" in c}
NAMES = {"slashl": "SlashL", "slashr": "SlashR", "slash3": "Slash3", "spin": "Spin", "slam": "Slam"}


def clip_for(kind: int, previous: str | None) -> str:
    if kind == SLASH_R:
        return "slashr"
    if kind == SPIN:
        return "spin"
    if kind == SLAM:
        return "slam"
    if kind == FOLLOW_UP:
        return {"slashl": "slashr", "slashr": "slash3", "slash3": "slashr"}.get(previous or "", "slashl")
    return "slashl"


def slot(row: dict, clip: str) -> tuple[float, float] | None:
    """The time into `clip` in a frame's row and that slot's share of the attack, or None if neither slot plays it."""
    a_share = float(row["a_share"])
    if row["a_clip"] == NAMES[clip]:
        return float(row["a_time"]), a_share
    if row["b_clip"] == NAMES[clip] and a_share < 1:
        return float(row["b_time"]), 1 - a_share
    return None


def check(feed: Path, run: Path) -> int:
    """Each attack's impact frame (the first frame at or after its impact) shows its clip at or past
    the clip's measured impact, holding most of the attack (the next swing, winding up from this
    impact, may start crossfading in on it), the frame before short of it, and the blade's flare up
    from that frame."""
    rows = list(csv.DictReader(run.open(encoding="utf-8")))
    songs = [float(r["song"]) for r in rows]
    previous, bad, n = None, 0, 0
    for line in feed.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        e = json.loads(line)
        if e["kind"] != "Attack":
            continue
        impact, clip = e["d"][1], clip_for(e["i"][1], previous)
        previous = clip
        k = next((i for i, s in enumerate(songs) if s >= impact - 1e-9), None)
        if k is None or k == 0:
            continue
        at, before = rows[k], rows[k - 1]
        now, then = slot(at, clip), slot(before, clip)
        ok = (now is not None and now[0] >= IMPACT[clip] - 1e-4 and now[1] >= 0.5 and float(at["attack"]) > 0.99
              and (then is None or then[0] < IMPACT[clip]) and float(at["flare"]) > 0.9 and float(before["flare"]) < float(at["flare"]))
        n += 1
        bad += not ok
        print(f"{'ok ' if ok else 'BAD'} impact {impact:.4f} {NAMES[clip]:6} frame {songs[k]:.4f} (+{1000 * (songs[k] - impact):.1f} ms): "
              f"clip {then[0] if then else 0:.3f} -> {now[0] if now else -1:.3f} s (impact {IMPACT[clip]:.3f}, share {now[1] if now else 0:.2f}), "
              f"flare {float(before['flare']):.2f} -> {float(at['flare']):.2f}, lift {float(at['lift']):.3f} m")
    print(f"{n - bad}/{n} impacts on their frames")
    return 1 if bad or n == 0 else 0


def stomp() -> dict:
    """A stomp facing east at 0.5 s: the knight turns from the sim's default south without moving, so
    the camera's lead along its facing runs across the frame, which has the room."""
    return {"kind": "HeroStep", "t": 0.5, "i": [0, 4, RIGHT], "d": [START[0], START[1], START[0], START[1], 0.5, 0.61]}


def main() -> None:
    if len(sys.argv) == 4 and sys.argv[1] == "check":
        sys.exit(check(Path(sys.argv[2]), Path(sys.argv[3])))
    here = Path(__file__).resolve().parent
    for bpm in (100, 128, 150):
        base = beats(bpm) + attacks(bpm) + [stomp()]
        for name, events in ((f"hero-{bpm}", base), (f"hero-{bpm}-steps", base + steps(bpm))):
            events.sort(key=lambda e: e["t"])
            for e in events:
                e["t"] = round(e["t"], 9)
                e["d"] = [round(v, 9) for v in e["d"]]
            lines = [f"# {name}: written by feeds.py"] + [json.dumps(e) for e in events]
            (here / f"{name}.jsonl").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
