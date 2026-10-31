"""Writes the GPU shatter's synthetic event feeds (ps-msz.3.9; ScriptedEvents' JSON lines, SimEvent.cs's slots), played
over house-128 on its own beats (native/fixtures/entries/house-128-20s.jsonl), into the gitignored captures/game/shatter/:

- single.jsonl: four monsters 4 m north of the hero at the origin (skeleton, troll, demon and the 2x troll of xlmummy),
  stepping in place toward the hero on every beat, each dying alone in a different pose: the troll at the contact of its
  lunge (6.12 s), the skeleton on a step's footplant (7.06 s), xlmummy to a heavy blow (8.0 s, 4.63 m) and the demon
  mid-flinch (8.93 s).
- mass.jsonl: 300 monsters of every model in same-type clumps 3 to 15 m around the hero, stepping in on the beats,
  and on the beat at 8.0 s the 50 nearest die together to a heavy blow, flung outward.

    python3 game/evidence/shatter/make_feed.py
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
FIXTURE = ROOT / "native" / "fixtures" / "entries" / "house-128-20s.jsonl"
OUT = ROOT / "captures" / "game" / "shatter"  # gitignored
ROLES = ["bat1", "bat2", "bat3", "bat4", "bat5", "zombie", "ghost", "skeleton", "mudman1", "mudman2",
         "werewolf", "xlbat", "xlmantis", "xlmummy", "xlflower", "flower", "boss-xlmantis", "boss-werewolf",
         "boss-xlmummy", "boss-xlflower", "bat-swarm", "ghost-swarm", "goblin", "skeleton-ember", "bee",
         "minotaur"]  # game/Content/enemies.json's order: the sim's role indices
SPAWN, END = 0.5, 12.0
NORMAL, HEAVY = 1.85, 4.63  # the sword's launch distances (m)
MASS_AT, MASS_DEATHS = 8.0, 50
CLUMPS = [("bat1", 50), ("zombie", 40), ("ghost", 36), ("skeleton", 36), ("mudman1", 36), ("flower", 30),
          ("mudman2", 18), ("werewolf", 18), ("goblin", 18), ("xlbat", 6), ("xlmummy", 6), ("xlflower", 4),
          ("boss-xlmantis", 2)]


def event(kind: str, t: float, i: list[int], d: list[float]) -> str:
    return json.dumps({"kind": kind, "t": round(t, 6), "i": i, "d": [round(x, 4) for x in d]})


def beats() -> list[float]:
    times = []
    for line in FIXTURE.read_text(encoding="utf-8").splitlines():
        e = json.loads(line)
        if e.get("kind") == 1 and 0.9 <= e["t"] <= END:
            times.append(round(e["t"], 6))
    return times


def beat_at(grid: list[float], t: float) -> int:
    return min(range(len(grid)), key=lambda k: abs(grid[k] - t))


def death(m: dict, t: float, force: float) -> str:
    d = math.hypot(m["x"], m["y"])
    return event("Death", t, [m["id"], m["role"]], [m["x"], m["y"], m["x"] / d, m["y"] / d, force])


def single(grid: list[float]) -> list[str]:
    lines = [f"# {Path(__file__).name}: four monsters, each dying alone in its own pose (ps-msz.3.9)"]
    row = [("skeleton", -4.5), ("werewolf", -1.5), ("ghost", 1.5), ("xlmummy", 4.5)]
    ms = [{"id": i, "role": ROLES.index(r), "x": x, "y": 4.0, "alive": True} for i, (r, x) in enumerate(row)]
    for m in ms:
        lines.append(event("Spawn", SPAWN, [m["id"], m["role"]], [m["x"], m["y"]]))
    deaths = {1: (6.12, NORMAL), 0: (7.06, NORMAL), 3: (8.0, HEAVY), 2: (8.93, NORMAL)}
    for k, beat in enumerate(grid):
        period = grid[k + 1] - beat if k + 1 < len(grid) else beat - grid[k - 1]
        window = min(0.1, 0.2 * period)
        for m in ms:
            if not m["alive"]:
                continue
            dies, force = deaths[m["id"]]
            d = math.hypot(m["x"], m["y"])
            ux, uy = m["x"] / d, m["y"] / d
            if abs(beat - dies) < 1e-3:
                if m["id"] == 1:  # the troll lunges, and dies on its contact
                    notice = beat - period / 2
                    lines.append(event("Attack", notice, [m["id"], 0, 0], [notice, beat, -ux, -uy, 0]))
                if m["id"] == 2:  # the demon is struck a beat's quarter before, and dies mid-flinch
                    lines.append(event("Hit", beat - period / 4, [m["id"], 0], [10, m["x"], m["y"], ux, uy]))
                if m["id"] == 0:  # the skeleton dies on its step's footplant
                    tx, ty = m["x"] - 0.35 * ux, m["y"] - 0.35 * uy
                    lines.append(event("EnemyStep", beat - window, [m["id"]], [m["x"], m["y"], tx, ty, beat - window, beat]))
                    m["x"], m["y"] = tx, ty
                lines.append(event("Hit", beat, [m["id"], 1 if force == HEAVY else 0], [99, m["x"], m["y"], ux, uy]))
                lines.append(death(m, beat, force))
                m["alive"] = False
                continue
            # Otherwise a small step in and back, so every body is in motion.
            sign = -1 if k % 2 == 0 else 1
            tx, ty = m["x"] + sign * 0.25 * ux, m["y"] + sign * 0.25 * uy
            lines.append(event("EnemyStep", beat - window, [m["id"]], [m["x"], m["y"], tx, ty, beat - window, beat]))
            m["x"], m["y"] = tx, ty
    return lines


def mass(grid: list[float]) -> list[str]:
    rng = random.Random(3_9)
    total = sum(n for _, n in CLUMPS)
    assert total == 300, total
    lines = [f"# {Path(__file__).name}: 300 monsters, the 50 nearest dying together at {MASS_AT} s (ps-msz.3.9)"]
    ms: list[dict] = []
    sector = 0.0
    for role, n in CLUMPS:
        width = 2 * math.pi * n / total
        placed = 0
        while placed < n:
            a = sector + rng.uniform(0, width)
            r = rng.uniform(3.0, 15.0)
            x, y = r * math.sin(a), r * math.cos(a)
            if any((x - m["x"]) ** 2 + (y - m["y"]) ** 2 < 1.0 for m in ms):
                continue
            ms.append({"id": len(ms), "role": ROLES.index(role), "x": x, "y": y, "alive": True, "stop": 2.4 + 0.35 * (r - 3)})
            placed += 1
        sector += width
    for m in ms:
        lines.append(event("Spawn", SPAWN, [m["id"], m["role"]], [m["x"], m["y"]]))
    mass_beat = beat_at(grid, MASS_AT)
    for k, beat in enumerate(grid):
        period = grid[k + 1] - beat if k + 1 < len(grid) else beat - grid[k - 1]
        window = min(0.1, 0.2 * period)
        living = [m for m in ms if m["alive"]]
        if k == mass_beat:
            for m in sorted(living, key=lambda m: math.hypot(m["x"], m["y"]))[:MASS_DEATHS]:
                d = math.hypot(m["x"], m["y"])
                lines.append(event("Hit", beat, [m["id"], 1], [99, m["x"], m["y"], m["x"] / d, m["y"] / d]))
                lines.append(death(m, beat, HEAVY))
                m["alive"] = False
        for m in living:
            if not m["alive"]:
                continue
            d = math.hypot(m["x"], m["y"])
            go = min(0.6, d - m["stop"])
            if go <= 0.05:
                continue
            tx, ty = m["x"] - go * m["x"] / d, m["y"] - go * m["y"] / d
            lines.append(event("EnemyStep", beat - window, [m["id"]], [m["x"], m["y"], tx, ty, beat - window, beat]))
            m["x"], m["y"] = tx, ty
    return lines


def main() -> None:
    grid = beats()
    OUT.mkdir(parents=True, exist_ok=True)
    for name, lines in (("single", single(grid)), ("mass", mass(grid))):
        path = OUT / f"{name}.jsonl"
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"{path}: {len(lines) - 1} events")


if __name__ == "__main__":
    main()
