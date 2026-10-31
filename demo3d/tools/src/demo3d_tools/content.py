"""`game content [--check]`: the one-shot conversion of native's run content into the game's JSON.

Reads native's YAML (content/, levels/survivors.yaml) and the numbers native's Rust sim keeps in
code (native/crates/sim/src/survivors/ and engine/), and writes game/Content/*.json in metres
(docs/design/unity-game.md, Content Data and Units). The JSON is then the game's own: once it is
edited by hand, this converter is history and `--check` (which fails on any difference) goes with
it. game/Content/README.md maps every native role and weapon to its game entry.

Units: 27 px per metre. Health, power, experience and knockback keep Vampire Survivors units;
times are seconds.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import yaml

PX_PER_M = 27.0
# native/crates/sim/src/survivors/stats.rs: one VS world unit in native pixels, chosen so the
# hero's VS speed (0.825 U/s x Mad Forest's 1.1) is native's 140 px/s.
VS_UNIT_PX = 140.0 / (0.825 * 1.1)
HERO_SPEED_PX = 0.825 * 1.1 * VS_UNIT_PX
# The game's own ids drop native's "vs-" prefix.
PREFIX = "vs-"

# demo3d's nine monster models and their crystal gem colours (sRGB), from
# demo3d/Assets/Demo3D/Runtime/Crowd/Styles/StyleLook.cs.
MODELS = {
    "bat": "#a77bff", "slime": "#ff5cbf", "demon": "#ff3d3d", "skeleton": "#8be9ff",
    "goblin": "#6dff8a", "bigork": "#3dffc8", "troll": "#4d8bff", "mushroom": "#ffb23d",
    "dragon": "#ffd23d",
}
# The cast (unity-game.md, Enemies): game role -> (model, scale, crystal). A role keeps its
# model's gem colour, except where two roles would otherwise look the same: the XL bat keeps
# native's dark tint against the boss bats, and the XL mummy a pale troll against the werewolf
# boss, both trolls at 2x.
CAST = {
    "bat1": ("bat", 1, None), "bat2": ("bat", 1, None), "bat3": ("bat", 1, None),
    "bat4": ("bat", 2, None), "bat5": ("bat", 2, None), "xlbat": ("bat", 2, "#5a3dcc"),
    "bat-swarm": ("bat", 1, None),
    "zombie": ("slime", 1, None),
    "ghost": ("demon", 1, None), "ghost-swarm": ("demon", 1, None),
    "skeleton": ("skeleton", 1, None),
    "mudman1": ("goblin", 1, None), "mudman2": ("bigork", 1, None),
    "werewolf": ("troll", 1, None),
    "xlmantis": ("bigork", 2, None), "xlmummy": ("troll", 2, "#b8c8ff"),
    "flower": ("mushroom", 1, None), "xlflower": ("mushroom", 2, None),
    "boss-xlmantis": ("dragon", 1, None), "boss-werewolf": ("troll", 2, None),
    "boss-xlmummy": ("bigork", 3, None), "boss-xlflower": ("mushroom", 3, None),
    "minotaur": ("troll", 2.5, None),
    "goblin": ("goblin", 1, None), "skeleton-ember": ("skeleton", 1, None), "bee": ("demon", 1, None),
}
# Native's set pieces and its signature boss use classic roles (content/enemies.yaml) whose
# survivors stats come from EnemyStats::for_art (stats.rs): speed, power, xp, knockback.
CLASSIC = {
    "goblin": (100, 10, 1, 0.8), "skeleton-ember": (100, 10, 2, 1), "bee": (700, 5, 1, 1),
    "minotaur": (160, 20, 50, 0),
}
EVENT_KINDS = {"BAT_SWARM": ("bat-swarm", "bat-swarm"), "GHOST_SWARM": ("ghost-swarm", "ghost-swarm"),
               "FLOWER_WALL": ("flower-wall", "flower")}
# native's survivors/weapons.rs WeaponId order: the inventory's tie-break order.
WEAPON_ORDER = ["sword", "bloody-tear", "garlic", "soul-eater", "magic-wand", "holy-wand",
                "king-bible", "unholy-vespers", "lightning-ring", "thunder-loop",
                "chain-lightning", "fire-trail"]
# PassiveId's order (the same tie-break) and each passive's per-rank stat change (offers.rs).
PASSIVES = [
    ("hollow-heart", 5, {"maxHp": 0.2}), ("empty-tome", 5, {"cooldown": -0.08}),
    ("spellbinder", 5, {"duration": 0.1}), ("duplicator", 2, {"amount": 1}),
    ("pummarola", 5, {"recovery": 0.2}), ("spinach", 5, {"might": 0.1}),
    ("magnet", 5, {"magnet": 0.3}), ("armour", 5, {"armor": 1}),
]
# offers.rs: the order "any offerable" scans (BASE_WEAPONS, then PASSIVES).
ANY_ORDER = ["sword", "garlic", "magic-wand", "king-bible", "lightning-ring", "chain-lightning",
             "fire-trail", "spinach", "hollow-heart", "pummarola", "duplicator", "empty-tome",
             "spellbinder", "magnet", "armour"]
RANK_SECONDS = {"intervalMs": "intervalS", "repeatMs": "repeatS", "durationMs": "durationS",
                "hitDelayMs": "hitDelayS"}
RANK_FIELDS = ["power", "amount", "intervalS", "repeatS", "area", "speed", "durationS", "pierce",
               "hitDelayS", "knockback", "critChance", "critMultiplier", "critEnabled",
               "intervalDependsOnDuration"]


def num(x: float) -> float | int:
    """A number as the JSON shows it: rounded past float noise, integral values without '.0'."""
    x = round(float(x), 9)
    return int(x) if x.is_integer() else x


def metres(px: float) -> float | int:
    return num(round(px / PX_PER_M, 4))


def vs_metres(units: float) -> float | int:
    return metres(units * VS_UNIT_PX)


def game_id(native: str) -> str:
    return native.removeprefix(PREFIX)


def ref_speed(vs_speed: float) -> float | int:
    """stats.rs enemy_speed: 0.231 U/s x 1.1 x speed/100, in metres per second."""
    return metres(0.231 * 1.1 * (vs_speed / 100.0) * VS_UNIT_PX)


def load(root: Path, rel: str) -> dict:
    return yaml.safe_load((root / rel).read_text())


def classic_gait(e: dict) -> str:
    """foes.rs's survivors cadence: every other main beat when the gait covers two or more of its
    times per step (gait.rs gait_ground), else every main beat. Classic gaits' eighth-note steps
    don't exist on the game's beats; the signature minotaur steps as a boss, every beat."""
    pattern = (e.get("gait") or {}).get("pattern", "x")
    if e["id"] == "minotaur" or len(pattern) / pattern.count("x") < 2:
        return "x"
    return "x."


def enemies(root: Path) -> dict:
    roles = []
    for e in load(root, "content/survivors-enemies.yaml")["enemies"]:
        s = e["survivors"]
        roles.append({
            "id": game_id(e["id"]), "refSpeedMps": ref_speed(s["speed"]), "health": num(e["health"]),
            "power": num(s["power"]), "xp": num(s["xp"]), "radiusM": metres(e["radius"]),
            "knockback": num(s["knockback"]), "maxKnockback": num(s["maxKnockback"]),
            "levelScaled": s["levelScaled"], "brute": s["brute"],
            "gait": {"pattern": e["gait"]["pattern"]},
        })
    classic = {e["id"]: e for e in load(root, "content/enemies.yaml")["enemies"]}
    for cid, (speed, power, xp, knockback) in CLASSIC.items():
        e = classic[cid]
        roles.append({
            "id": cid, "refSpeedMps": ref_speed(speed), "health": num(e["health"]),
            "power": num(power), "xp": num(xp), "radiusM": metres(e["radius"]),
            "knockback": num(knockback), "maxKnockback": 1 if cid == "minotaur" else 3,
            "levelScaled": False, "brute": False,
            "gait": {"pattern": classic_gait(e)},
        })
    return {"roles": roles}


def cast(ids: list[str]) -> dict:
    missing = sorted(set(ids) ^ set(CAST))
    if missing:
        raise SystemExit(f"content: the cast and the roles differ: {missing}")
    return {
        "models": [{"id": m, "gem": c} for m, c in MODELS.items()],
        "cast": [{"role": r, "model": CAST[r][0], "scale": num(CAST[r][1]),
                  "crystal": CAST[r][2] or MODELS[CAST[r][0]]} for r in ids],
    }


def shape(wid: str) -> dict:
    """The weapon's geometry before Area (engine/weapons.rs, combat.rs, bible.rs, missiles.rs)."""
    if wid in ("sword", "bloody-tear"):
        # In zen mode at the groove's top tier (survivors' only mode) the heavy strike reaches
        # 130/100 of the light one (hero.yaml's heavy and light hit shapes) and its cleave wave
        # 380 - 220 px past it, in 25 px bands, at 1.0 of the light damage.
        return {"kind": "sword", "radiusM": metres(190), "heavyReachScale": 1.3, "heavyDamage": 2.5,
                "waveBeyondM": metres(380 - 220), "waveBandM": metres(25), "waveDamage": 1}
    if wid in ("magic-wand", "holy-wand"):
        return {"kind": "missile", "radiusM": vs_metres(0.08), "speedMps": vs_metres(1.65)}
    if wid in ("garlic", "soul-eater"):
        return {"kind": "aura", "radiusM": vs_metres(0.32)}
    if wid in ("king-bible", "unholy-vespers"):
        # The orbit's radius grows with Area up to 4.5x; BIBLE_OMEGA (bible.rs) times speed. A
        # blade's body is 0.08 U x (1 + 0.3 x Area), not 0.08 U x Area (bible.rs BibleCycle::size).
        return {"kind": "orbit", "radiusM": vs_metres(0.08), "bladeAreaSlope": 0.3,
                "orbitRadiusM": vs_metres(0.5), "orbitAreaCap": 4.5, "radPerS": num(35 * 0.0625)}
    if wid in ("lightning-ring", "thunder-loop"):
        return {"kind": "strike", "radiusM": vs_metres(0.08)}
    if wid == "chain-lightning":
        # combat.rs: the first jump reaches 260 px x Area, every later one 140 px x Area; each jump
        # hits a 16 px x Area circle (content/weapons.yaml's hitShape), a sixteenth after the last.
        return {"kind": "chain", "radiusM": metres(260), "jumpRangeM": metres(140),
                "hitRadiusM": metres(16), "jumpBeats": 0.25}
    if wid == "fire-trail":
        # combat.rs: hits 18 px apart along the hero's path (content/weapons.yaml's spacing),
        # skipping its latest 0.12 s.
        return {"kind": "trail", "radiusM": metres(20), "spacingM": metres(18), "skipRecentS": 0.12}
    raise SystemExit(f"content: no shape for weapon {wid}")


def weapons(root: Path) -> dict:
    raw = {w["id"]: w for w in load(root, "content/survivors/weapons.yaml")["weapons"]}
    if sorted(raw) != sorted(WEAPON_ORDER):
        raise SystemExit(f"content: unexpected weapons {sorted(raw)}")
    out = []
    for wid in WEAPON_ORDER:
        w = raw[wid]
        value: dict = {}
        ranks = []
        # weapons.rs WeaponCatalog::parse: numeric fields add rank to rank, others replace.
        for patch in w["levels"]:
            for k, v in patch.items():
                is_num = isinstance(v, (int, float)) and not isinstance(v, bool)
                value[k] = value.get(k, 0) + v if is_num else v
            row = {}
            for k, v in value.items():
                key = RANK_SECONDS.get(k, k)
                row[key] = num(v / 1000) if k in RANK_SECONDS else (v if isinstance(v, bool) else num(v))
            ranks.append({k: row[k] for k in RANK_FIELDS})
        out.append({"id": wid, "grid": w["grid"], "evolutionOf": w["evolutionOf"],
                    "requires": w["requires"], "shape": shape(wid), "ranks": ranks})
    return {"weapons": out}


def passives() -> dict:
    return {"passives": [{"id": p, "maxRank": n, "perRank": d} for p, n, d in PASSIVES]}


def offers(root: Path) -> dict:
    raw = load(root, "content/offers.yaml")
    return {
        "levels": [{"level": lv, "cards": cards} for lv, cards in sorted(raw["offers"].items())],
        "fallback": raw["fallback"],
        "anyOrder": ANY_ORDER,
    }


def progression(root: Path) -> dict:
    level = load(root, "levels/survivors.yaml")
    hero = level["hero"]["health"]
    return {
        "slots": {"weapons": 6, "passives": 6},
        # stats.rs: Antonio's base stats and VS's final-stat caps.
        "hero": {
            "speedMps": metres(HERO_SPEED_PX), "invulnerableS": num(hero["invulnerableMs"] / 1000),
            "stats": {"might": 1, "cooldown": 1, "amount": 0, "area": 1, "speed": 1, "duration": 1,
                      "maxHp": hero["max"], "recovery": 0, "armor": 1, "magnet": 1, "luck": 1,
                      "growth": 1},
            "caps": {"might": {"max": 10}, "cooldown": {"min": 0.1}, "amount": {"max": 10},
                     "area": {"max": 10}, "speed": {"max": 5}, "duration": {"max": 5},
                     "armor": {"max": 50}},
        },
        # progression.rs: total(L) = factor x L^2, the factor rising at levels 20 and 40; +100%
        # Growth while at levels 20 and 40.
        "experience": {"factors": [{"fromLevel": 1, "factor": 5}, {"fromLevel": 20, "factor": 6.5},
                                   {"fromLevel": 40, "factor": 8}],
                       "growthLevels": [20, 40], "growthBonus": 1},
        # stats.rs and survivors/pickups.rs.
        "gems": {"cap": 400, "pickupRadiusM": metres(120), "bodyRadiusM": vs_metres(0.1),
                 "colours": [{"from": 1, "colour": "blue"}, {"from": 3, "colour": "green"},
                             {"from": 10, "colour": "red"}]},
        # chests.rs: items by tier, and the order a chest checks evolutions in.
        "chests": {"itemsByTier": [1, 3, 5],
                   "evolutionOrder": ["sword", "garlic", "magic-wand", "king-bible", "lightning-ring"]},
        "maxedHeal": num(level["survivors"]["maxedHeal"]),
    }


def schedule(root: Path) -> dict:
    level = load(root, "levels/survivors.yaml")
    sv = level["survivors"]
    director = level["director"]
    formations = {f["id"]: f for f in load(root, "content/formations.yaml")["formations"]}
    sp = director["setPieces"]
    classic = {e["id"]: e for e in load(root, "content/enemies.yaml")["enemies"]}
    kick = formations[sp["kickLine"]["formation"]]
    wedge = formations[sp["wedge"]]
    minutes = []
    for m in sv["schedule"]:
        events = []
        for e in m["events"]:
            kind, role = EVENT_KINDS[e["kind"]]
            # spawning.rs: an absent chance and a zero chance both mean no gate.
            chance = e.get("chance") or None
            events.append({"kind": kind, "role": role, "delayS": num(e["delayMs"] / 1000),
                           "extraRepeats": e["extraRepeats"], "chance": chance,
                           "durationS": num(e["durationMs"] / 1000), "count": e["count"]})
        if len(m["treasure"]) > 1:
            raise SystemExit(f"content: minute {m['minute']} has several treasures")
        treasure = None
        for t in m["treasure"]:
            # chests.rs treasure_tier: chances[0] rolls for tier 3, then tier 2, then tier 1;
            # only minutes 10, 15, 20 and 25's chests may evolve a weapon.
            c = t["chances"]
            treasure = {"chances": {"tier3": num(c[0]), "tier2": num(c[1]), "tier1": num(c[2])},
                        "tier": t.get("tier"), "evolves": m["minute"] in (10, 15, 20, 25)}
        minutes.append({
            "minute": m["minute"], "minimum": m["minimum"], "intervalS": num(m["frequency"] / 1000),
            "enemies": [game_id(r) for r in m["enemies"]], "bosses": [game_id(r) for r in m["bosses"]],
            "events": events, "treasure": treasure,
        })
    boss = sp["boss"]
    finale = load(root, "content/weapons.yaml")["weapons"]
    blast = next(w for w in finale if w["id"] == sp["finale"]["blast"])
    rule = director["sectionRule"]
    return {
        "run": {"durationS": sv["durationS"], "cap": 300},
        "easy": {"contactDamage": num(sv["contactDamage"]),
                 "bruteChickenChance": num(sv["bruteChickenChance"]),
                 "bossChickens": num(sv["bossChickens"])},
        "minutes": minutes,
        # Each minute's swarm waits for its first loud jump, or fires in the minute's last 10 s.
        "swarms": {"jumpDb": rule["jumpDb"], "lateS": 10},
        # engine/musical.rs: one group at a time inside the cap, each kind from its run time.
        "setPieces": {
            "groupCap": 40, "gapS": 30, "lifeS": 48,
            "stampede": {"fromS": 120, "role": sp["stampede"]["enemy"], "count": sp["stampede"]["count"],
                         "rows": 2, "jumpDb": 6, "quietS": 8, "speedScale": 2.5},
            "kickLine": {"fromS": 180, "role": kick["enemy"], "count": kick["count"],
                         "spacingM": metres(kick["spacing"]), "mediumBeats": 16,
                         "entranceBeats": kick["entranceBeats"], "minDanceBeats": 8,
                         "maxDanceBeats": 32},
            "wedge": {"fromS": 240, "role": wedge["enemy"], "count": wedge["count"],
                      "spacingM": metres(wedge["spacing"]), "everyBeats": 16,
                      "entranceBeats": wedge["entranceBeats"],
                      "danceBeats": wedge["release"]["beats"]},
            "ring": {"fromS": 300, "role": sp["ring"]["enemy"], "count": sp["ring"]["count"],
                     "gaps": 3, "gapPlaces": 5, "everyBeats": 16, "entranceBeats": 4,
                     "closeToM": metres(120)},
        },
        # engine/signature.rs: the minotaur lands on a reliable beat from 90 s and slams every 8
        # beats (pieces.rs: its attackDamage, within SLAM_RADIUS of the hero's hurtbox).
        "signature": {"role": boss["type"], "fromS": boss["afterS"], "slamBeats": boss["slamBeats"],
                      "slamDamage": classic[boss["type"]]["attackDamage"],
                      "slamRadiusM": metres(28), "retreatS": boss["retreatS"]},
        "finale": {"radiusM": metres(blast["blast"]["radius"]), "damage": blast["damage"],
                   "beats": blast["blast"]["beats"], "victoryCardS": sp["finale"]["epilogueS"]},
        # The five zones cycle on section changes at least 8 s apart, else on the fallback: 60 s
        # through the first 5 minutes, then 240 s. The game's zones are demo3d's palettes, in
        # native's order (twilight glade, galop woods, mushroom grove, ember hollow, infernal grove).
        "zones": {"ids": ["neon", "blossom", "snow", "crypt", "lava"], "victory": "lava",
                  "jumpDb": rule["jumpDb"], "minGapS": rule["minGapS"],
                  "openingFallbackS": 60, "openingS": 300, "fallbackS": director["zoneFallbackS"]},
    }


def convert(root: Path) -> dict[str, str]:
    """Every content file's name and text."""
    enemy = enemies(root)
    files = {
        "enemies.json": enemy,
        "cast.json": cast([r["id"] for r in enemy["roles"]]),
        "weapons.json": weapons(root),
        "passives.json": passives(),
        "offers.json": offers(root),
        "progression.json": progression(root),
        "schedule.json": schedule(root),
    }
    return {name: fmt(data) + "\n" for name, data in files.items()}


def is_row(v: object) -> bool:
    """An object of scalars, scalar lists and objects of scalars: a role, a rank, an event."""
    def flat(x: object) -> bool:
        return not isinstance(x, (dict, list)) or (
            isinstance(x, list) and all(not isinstance(i, (dict, list)) for i in x))
    return isinstance(v, dict) and all(
        flat(x) or (isinstance(x, dict) and all(flat(i) for i in x.values())) for x in v.values())


def fmt(v: object, indent: int = 0, lead: int = 0, width: int = 100, row: bool = False) -> str:
    """JSON with each value on one line when it fits, and each row of a list on its own line.

    `lead` is what precedes the value on its line (its key)."""
    flat = json.dumps(v, separators=(", ", ": "))
    fits = indent + lead + len(flat) <= width or (row and is_row(v))
    if fits or not isinstance(v, (dict, list)) or not v:
        return flat
    pad = " " * (indent + 2)
    if isinstance(v, dict):
        body = [f"{pad}{json.dumps(k)}: {fmt(x, indent + 2, len(json.dumps(k)) + 2, width)}"
                for k, x in v.items()]
        return "{\n" + ",\n".join(body) + "\n" + " " * indent + "}"
    body = [pad + fmt(x, indent + 2, 0, width, row=True) for x in v]
    return "[\n" + ",\n".join(body) + "\n" + " " * indent + "]"


def add_parser(sub: argparse._SubParsersAction) -> None:
    c = sub.add_parser("content", help="convert native's run content into game/Content/*.json")
    c.add_argument("--check", action="store_true", help="fail if the committed JSON differs")
    c.set_defaults(func=cmd_content)


def cmd_content(a: argparse.Namespace) -> int:
    from . import paths

    root = paths.worktree(name="game").root
    out = root / "game" / "Content"
    files = convert(root)
    if a.check:
        stale = [n for n, text in files.items()
                 if not (out / n).exists() or (out / n).read_text() != text]
        if stale:
            print(f"content: game/Content differs from the conversion: {', '.join(stale)}",
                  file=sys.stderr)
            return 1
        print(f"content: {len(files)} files match the conversion")
        return 0
    out.mkdir(parents=True, exist_ok=True)
    for name, text in files.items():
        (out / name).write_text(text)
    print(f"content: wrote {len(files)} files to {out.relative_to(root)}")
    return 0
