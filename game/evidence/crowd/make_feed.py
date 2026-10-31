"""Writes captures/game/crowd/horde-300.jsonl: a synthetic event feed of 300 crystal monsters for the crowd's stills
(ps-msz.3.8; ScriptedEvents' JSON lines, SimEvent.cs's slots), played over house-128.

The beats are house-128's (its committed beat entries in native/fixtures/entries/house-128-20s.jsonl,
the plugin's analysis of public/reactive/house-128/house-128.ogg), so with `--file` the music, the
sim's beats, the look's pulses and the facets' sparkle land with the footplants. Same-type clumps
(unity-game.md, Enemies) spawn at 0.5 s on rings 4.5 to 16 m around the hero at the origin, covering
every model of the cast. Each monster steps toward the hero on main beats (elites every other beat),
its move window the min(100 ms, 20% of the beat) before it; within 3.6 m it lunges instead (notice
half a beat ahead, contact on the beat). From 4 s the sword strikes on every beat: the nearest few
are hit and knocked back, and at 8 s a dozen die. Deterministic (seeded).

Beside it, captures/game/crowd/row-5.jsonl: five models in a row 4 m north of the hero, each stepping
0.6 m east on every beat, for a close filmstrip of the footplants.

    python3 game/evidence/crowd/make_feed.py
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
FIXTURE = ROOT / "native" / "fixtures" / "entries" / "house-128-20s.jsonl"
OUT = ROOT / "captures" / "game" / "crowd" / "horde-300.jsonl"  # gitignored
ROW_OUT = OUT.with_name("row-5.jsonl")
ROW = ["skeleton", "mudman1", "ghost", "werewolf", "zombie"]  # skeleton, goblin, demon, troll, slime
FIRST_BEAT, END = 0.9, 12.0
SPAWN = 0.5
ROLES = ["bat1", "bat2", "bat3", "bat4", "bat5", "zombie", "ghost", "skeleton", "mudman1", "mudman2",
         "werewolf", "xlbat", "xlmantis", "xlmummy", "xlflower", "flower", "boss-xlmantis", "boss-werewolf",
         "boss-xlmummy", "boss-xlflower", "bat-swarm", "ghost-swarm", "goblin", "skeleton-ember", "bee",
         "minotaur"]  # game/Content/enemies.json's order: the sim's role indices
# Clumps: role, count, step (m), every Nth beat.
CLUMPS = [("bat1", 56, 0.9, 1), ("zombie", 46, 0.5, 1), ("ghost", 40, 0.8, 1), ("skeleton", 40, 0.6, 1),
          ("mudman1", 40, 0.6, 1), ("flower", 30, 0.3, 2), ("mudman2", 14, 0.9, 2), ("werewolf", 14, 0.9, 2),
          ("xlbat", 6, 1.2, 1), ("xlmummy", 6, 1.0, 2), ("xlflower", 4, 0.5, 2), ("boss-xlmantis", 1, 1.0, 2),
          ("bat-swarm", 3, 0.9, 1)]
REACH = 3.6  # within this of the hero a monster lunges rather than steps
# No step ends closer than a monster's own stop, which grows with where it spawned, so the horde
# closes in as a disc rather than piling onto one ring (the sim's separation, roughly).
STOP, STOP_GROWTH = 2.6, 0.45
STRIKE_FROM, STRIKE_REACH = 4.0, 4.2
DEATHS_FROM, DEATHS = 8.0, 12


def event(kind: str, t: float, i: list[int], d: list[float]) -> str:
    return json.dumps({"kind": kind, "t": round(t, 6), "i": i, "d": [round(x, 4) for x in d]})


def beats() -> list[float]:
    """house-128's committed beats (entry kind 1) from FIRST_BEAT to END."""
    times = []
    for line in FIXTURE.read_text(encoding="utf-8").splitlines():
        e = json.loads(line)
        if e.get("kind") == 1 and FIRST_BEAT <= e["t"] <= END:
            times.append(round(e["t"], 6))
    return times


def main() -> None:
    rng = random.Random(3_8)
    total = sum(n for _, n, _, _ in CLUMPS)
    assert total == 300, total
    lines = [f"# {Path(__file__).name}: 300 crystal monsters in same-type clumps on house-128's beats (ps-msz.3.8)"]
    monsters = []
    sector = 0.0
    for role, n, step, every in CLUMPS:
        width = 2 * math.pi * n / total
        placed = 0
        while placed < n:
            a = sector + rng.uniform(0, width)
            r = rng.uniform(4.5, 16.0) if role != "boss-xlmantis" else 15.0
            x, y = r * math.sin(a), r * math.cos(a)
            if any((x - m["x"]) ** 2 + (y - m["y"]) ** 2 < 1.1 ** 2 for m in monsters):
                continue
            monsters.append({"id": len(monsters), "role": ROLES.index(role), "x": x, "y": y, "step": step,
                             "every": every, "phase": rng.randrange(every), "alive": True,
                             "stop": STOP + STOP_GROWTH * (r - 4.5)})
            placed += 1
        sector += width
    for m in monsters:
        lines.append(event("Spawn", SPAWN, [m["id"], m["role"]], [m["x"], m["y"]]))

    grid = beats()
    deaths_done = False
    for k, beat in enumerate(grid):
        period = grid[k + 1] - beat if k + 1 < len(grid) else beat - grid[k - 1]
        window = min(0.1, 0.2 * period)
        living = [m for m in monsters if m["alive"]]
        if beat >= STRIKE_FROM:
            # The hero's strike on the beat: the nearest few are hit and knocked straight back.
            near = sorted(living, key=lambda m: math.hypot(m["x"], m["y"]))[:6]
            for m in near:
                dist = math.hypot(m["x"], m["y"])
                if dist > STRIKE_REACH:
                    continue
                ux, uy = m["x"] / dist, m["y"] / dist
                lines.append(event("Hit", beat, [m["id"], 0], [10, m["x"], m["y"], ux, uy]))
                tx, ty = m["x"] + 1.85 * ux, m["y"] + 1.85 * uy
                lines.append(event("EnemyKnock", beat, [m["id"]], [m["x"], m["y"], tx, ty, beat, beat + 0.12]))
                m["x"], m["y"], m["knocked"] = tx, ty, k
        if beat >= DEATHS_FROM and not deaths_done:
            deaths_done = True
            for m in sorted(living, key=lambda m: math.hypot(m["x"], m["y"]))[:DEATHS]:
                d = math.hypot(m["x"], m["y"])
                lines.append(event("Death", beat, [m["id"], m["role"]], [m["x"], m["y"], m["x"] / d, m["y"] / d, 1.85]))
                m["alive"] = False
        for m in living:
            if not m["alive"] or (k + m["phase"]) % m["every"] or m.get("knocked") == k:
                continue
            dist = math.hypot(m["x"], m["y"])
            if dist <= REACH:
                notice = beat - period / 2
                lines.append(event("Attack", notice, [m["id"], 0, 0], [notice, beat, -m["x"] / dist, -m["y"] / dist, 0]))
                continue
            go = min(m["step"], dist - m["stop"])
            if go <= 0.05:
                continue
            tx, ty = m["x"] - go * m["x"] / dist, m["y"] - go * m["y"] / dist
            lines.append(event("EnemyStep", beat - window, [m["id"]], [m["x"], m["y"], tx, ty, beat - window, beat]))
            m["x"], m["y"] = tx, ty
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{OUT}: {len(lines) - 1} events")
    row(grid)


def row(grid: list[float]) -> None:
    lines = [f"# {Path(__file__).name}: five models stepping east on house-128's beats (ps-msz.3.8)"]
    xs = [-6.0 + 2.4 * i for i in range(len(ROW))]
    for i, role in enumerate(ROW):
        lines.append(event("Spawn", SPAWN, [i, ROLES.index(role)], [xs[i], 4.0]))
    for k, beat in enumerate(grid):
        period = grid[k + 1] - beat if k + 1 < len(grid) else beat - grid[k - 1]
        window = min(0.1, 0.2 * period)
        for i in range(len(ROW)):
            lines.append(event("EnemyStep", beat - window, [i], [xs[i], 4.0, xs[i] + 0.6, 4.0, beat - window, beat]))
            xs[i] += 0.6
    ROW_OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"{ROW_OUT}: {len(lines) - 1} events")


if __name__ == "__main__":
    main()
