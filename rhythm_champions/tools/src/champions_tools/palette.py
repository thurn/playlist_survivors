"""The palette (rhythm_champions/Content/palette.json) and the rules it keeps (docs/champions/style.md, The Palette).

Colour science, shared with the look metrics (look.py): CIE L* and C*ab (CIELAB, D65) and HSV hue of 8-bit sRGB, as
the archived look harness measured them. Every `#rrggbb` string in the palette is a role, named by its path
(`enemies.key.cyan`, `zones.teal.fill`); `types` maps each cast type to the role that colours it.

`problems(palette, cast)` lists every rule the palette breaks, as text; the cast is Content/enemies.json's, whose
`slot` names each type's family.
"""

from __future__ import annotations

import json
import re
from itertools import combinations
from pathlib import Path

import numpy as np

FAMILIES = ("key", "accent", "counter")  # the enemy families, in the order the style guide names them
SLOT_GAP = (14.0, 43.0)  # a family's two members: apart enough to tell, close enough for one 60-degree window
FAMILY_GAP = 50.0  # members of different families at least this far apart
REACH = 40.0  # the boss within this of a counter member; the first zone within this of the key's members
ARC = (40.0, 185.0)  # the yellow-green arc (exclusive): only the warnings, enemy shots and the warm zone live in it
YELLOW = (50.0, 70.0)  # electric yellow: warnings and enemy shots
ROLE_GAP = 20.0  # warnings and enemy shots this far from every enemy and boss hue (palette conformance's tolerance)
ZONE_FAMILIES = {"cool": (180.0, 250.0), "violet": (255.0, 300.0), "warm": (0.0, 60.0)}
ZONE_ORDER = ("cool", "cool", "violet", "warm")  # two cool (the first is the key's), a violet, a dim warm
ZONE_GAP = 25.0  # zones' edge hues pairwise apart, so four zones are four floors
NEUTRAL_C = 15.0  # under this C*ab a colour has no hue to speak of: white, grey, the void
NEAR_WHITE = (85.0, 30.0)  # L* at least, C*ab at most: white, which is the hero's alone
# The value ladder (style.md): (L* min, L* max, C*ab min, C*ab max) for each role pattern.
VALUES = (
    ("void.*", 0, 5, 0, 8),
    ("hero.body", 95, 100, 0, 8),
    ("hero.edge", 90, 100, 0, NEUTRAL_C),
    ("hero.break", 90, 100, 0, 30),
    ("hero.hurt", 50, 82, 60, 200),
    ("enemies.*.*", 50, 82, 35, 200),
    ("boss.*", 45, 82, 60, 200),
    ("warning", 85, 95, 70, 200),
    ("enemyShot", 85, 95, 70, 200),
    ("wall", 45, 70, 0, 5),
    ("zones.*.line", 10, 20, 0, 200),
    ("zones.*.pulse", 25, 35, 0, 200),
    ("zones.*.fill", 10, 25, 0, 200),
    ("zones.*.edge", 40, 60, 0, 200),
)
PURE_WHITE = ("hero.blade", "hero.trail")
SLOT_FAMILY = {"keyLight": "key", "keyDeep": "key", "accent": "accent", "counter": "counter", "boss": "boss"}


# --- Colour science ----------------------------------------------------------------------------

def _linear(rgb: np.ndarray) -> np.ndarray:
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def _f(t: np.ndarray) -> np.ndarray:
    return np.where(t > 216 / 24389, np.cbrt(t), (24389 / 27 * t + 16) / 116)


def luminance(rgb: np.ndarray) -> np.ndarray:
    """Linear relative luminance (0-1) of 8-bit sRGB pixels."""
    return _linear(rgb) @ np.array([0.2126, 0.7152, 0.0722])


def lstar(rgb: np.ndarray) -> np.ndarray:
    """CIE L* (0-100) of 8-bit sRGB pixels."""
    return 116 * _f(luminance(rgb)) - 16


def lab(rgb: np.ndarray) -> np.ndarray:
    """CIELAB (D65) of 8-bit sRGB pixels: L*, a*, b* on the last axis."""
    m = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]])
    f = _f(_linear(rgb) @ m.T / np.array([0.95047, 1.0, 1.08883]))
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


def chroma(rgb: np.ndarray) -> np.ndarray:
    """C*ab of 8-bit sRGB pixels (D65)."""
    c = lab(rgb)
    return np.hypot(c[..., 1], c[..., 2])


def hue(rgb: np.ndarray) -> np.ndarray:
    """HSV hue (degrees, 0-360) of 8-bit sRGB pixels; 0 for greys."""
    c = np.asarray(rgb, dtype=np.float64)
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    mx, mn = c.max(axis=-1), c.min(axis=-1)
    d = np.where(mx > mn, mx - mn, 1.0)
    h = np.where(mx == r, (g - b) / d % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4))
    return np.where(mx > mn, h * 60.0, 0.0)


def hue_gap(h: np.ndarray | float, target: float) -> np.ndarray | float:
    """The angle (degrees, 0-180) between HSV hues."""
    return np.abs((np.asarray(h) - target + 180.0) % 360.0 - 180.0)


def rgb(hex_colour: str) -> tuple[int, int, int]:
    return int(hex_colour[1:3], 16), int(hex_colour[3:5], 16), int(hex_colour[5:7], 16)


def describe(colour: tuple[int, int, int]) -> dict[str, float]:
    """One colour's HSV hue, L* and C*ab."""
    px = np.array(colour, dtype=np.uint8)
    return {"hue": float(hue(px)), "L": float(lstar(px)), "C": float(chroma(px))}


# --- The palette -------------------------------------------------------------------------------

def default_path() -> Path:
    return Path(__file__).resolve().parents[3] / "Content" / "palette.json"


def load(path: Path | None = None) -> dict:
    return json.loads((path or default_path()).read_text(encoding="utf-8"))


def roles(palette: dict) -> dict[str, tuple[int, int, int]]:
    """Every colour in the palette by its role's path: `void.floor`, `enemies.key.cyan`, `zones.teal.fill` (a list
    item with a `name` takes its name). Any `#rrggbb` string is a role; `types` names roles, and holds none."""
    out: dict[str, tuple[int, int, int]] = {}

    def walk(v: object, path: str) -> None:
        if isinstance(v, str) and re.fullmatch(r"#[0-9a-fA-F]{6}", v):
            out[path] = rgb(v)
        elif isinstance(v, dict):
            for k, x in v.items():
                walk(x, f"{path}.{k}" if path else k)
        elif isinstance(v, list):
            for i, x in enumerate(v):
                walk(x, f"{path}.{x['name'] if isinstance(x, dict) and 'name' in x else i}")

    walk({k: v for k, v in palette.items() if k != "types"}, "")
    return out


def family_members(palette: dict) -> dict[str, dict[str, tuple[int, int, int]]]:
    """The enemy families' members by role path."""
    rs = roles(palette)
    return {f: {k: v for k, v in rs.items() if k.startswith(f"enemies.{f}.")} for f in FAMILIES}


def _matches(path: str, pattern: str) -> bool:
    return re.fullmatch(pattern.replace(".", r"\.").replace("*", "[^.]+"), path) is not None


def problems(palette: dict, cast: list[dict]) -> list[str]:
    """Every rule of style.md's The Palette that the palette breaks, as text (empty when it keeps them all)."""
    out: list[str] = []
    rs = roles(palette)
    d = {k: describe(v) for k, v in rs.items()}

    # The shape: the groups the game draws from, every family with two members, four zones.
    for k in ("void.floor", "void.fog", "void.body", "hero.body", "hero.blade", "hero.trail", "hero.edge",
              "hero.hurt", "hero.break", "boss.body", "boss.telegraph", "warning", "enemyShot", "wall"):
        if k not in rs:
            out.append(f"shape: no {k}")
    fams = family_members(palette)
    for f, members in fams.items():
        if len(members) != 2:
            out.append(f"shape: the {f} family has {len(members)} members, not two")
    zones = palette.get("zones", [])
    if [z.get("family") for z in zones] != list(ZONE_ORDER):
        out.append(f"zones: families {[z.get('family') for z in zones]}, not {list(ZONE_ORDER)}")
    for z in zones:
        for part in ("line", "pulse", "fill", "edge"):
            if f"zones.{z.get('name')}.{part}" not in rs:
                out.append(f"shape: zone {z.get('name')} has no {part}")
    if out:
        return out

    # Families: each family's two members 14-43 degrees apart, members of different families 50 apart.
    for members in fams.values():
        a, b = members
        gap = float(hue_gap(d[a]["hue"], d[b]["hue"]))
        if not SLOT_GAP[0] <= gap <= SLOT_GAP[1]:
            out.append(f"families: {a} and {b} are {gap:.0f} degrees apart, not {SLOT_GAP[0]:.0f}-{SLOT_GAP[1]:.0f}")
    for f, g in combinations(FAMILIES, 2):
        for a in fams[f]:
            for b in fams[g]:
                gap = float(hue_gap(d[a]["hue"], d[b]["hue"]))
                if gap < FAMILY_GAP:
                    out.append(f"families: {a} and {b} are {gap:.0f} degrees apart, under {FAMILY_GAP:.0f}")

    # The boss in the counter's reach, so a boss shot adds no family.
    for k in ("boss.body", "boss.telegraph"):
        near = min(float(hue_gap(d[k]["hue"], d[c]["hue"])) for c in fams["counter"])
        if near > REACH:
            out.append(f"boss: {k} is {near:.0f} degrees from the counter family, over {REACH:.0f}")

    # The key: the first zone and the key's members within 40 degrees of each other.
    first = zones[0]["name"]
    keyed = [*fams["key"], *(f"zones.{first}.{p}" for p in ("line", "pulse", "fill", "edge"))]
    for a, b in combinations(keyed, 2):
        gap = float(hue_gap(d[a]["hue"], d[b]["hue"]))
        if gap > REACH:
            out.append(f"key: {a} and {b} are {gap:.0f} degrees apart, over {REACH:.0f}")

    # The yellow-green arc holds only electric yellow (warnings, enemy shots) and the warm zone; near white spends
    # no family, so Break's white-gold may sit there.
    warm = {z["name"] for z in zones if z["family"] == "warm"}
    for k, v in d.items():
        allowed = k in ("warning", "enemyShot") or (k.startswith("zones.") and k.split(".")[1] in warm)
        near_white = v["L"] >= NEAR_WHITE[0] and v["C"] <= NEAR_WHITE[1]
        if v["C"] >= NEUTRAL_C and not near_white and ARC[0] < v["hue"] < ARC[1] and not allowed:
            out.append(f"arc: {k} (hue {v['hue']:.0f}) is in the yellow-green arc, which is the warnings'")
    enemy_hues = [k for k in rs if k.startswith(("enemies.", "boss."))]
    for k in ("warning", "enemyShot"):
        if not YELLOW[0] <= d[k]["hue"] <= YELLOW[1]:
            out.append(f"yellow: {k} (hue {d[k]['hue']:.0f}) is not electric yellow ({YELLOW[0]:.0f}-{YELLOW[1]:.0f})")
        for e in enemy_hues:
            gap = float(hue_gap(d[k]["hue"], d[e]["hue"]))
            if gap < ROLE_GAP:
                out.append(f"yellow: {k} is {gap:.0f} degrees from {e}, under {ROLE_GAP:.0f}")

    # The value ladder.
    for k, v in d.items():
        for pattern, lmin, lmax, cmin, cmax in VALUES:
            if _matches(k, pattern) and not (lmin <= v["L"] <= lmax and cmin <= v["C"] <= cmax):
                out.append(f"values: {k} is L* {v['L']:.0f} C* {v['C']:.0f}, not L* {lmin}-{lmax} C* {cmin}-{cmax}")
    for k in PURE_WHITE:
        if rs[k] != (255, 255, 255):
            out.append(f"values: {k} is not pure white")

    # White is the hero's; grey is a wall's.
    for k, v in d.items():
        if v["L"] >= NEAR_WHITE[0] and v["C"] <= NEAR_WHITE[1] and not k.startswith("hero."):
            out.append(f"white: {k} is near white (L* {v['L']:.0f} C* {v['C']:.0f}), and white is the hero's")
        if v["C"] < NEUTRAL_C and 10 <= v["L"] < NEAR_WHITE[0] and k != "wall":
            out.append(f"grey: {k} is grey (L* {v['L']:.0f} C* {v['C']:.0f}), and grey is a wall's")

    # Zones: each in its family's hues, four distinct floors.
    for z in zones:
        lo, hi = ZONE_FAMILIES[z["family"]]
        for part in ("line", "pulse", "fill", "edge"):
            h = d[f"zones.{z['name']}.{part}"]["hue"]
            if not lo <= h <= hi:
                out.append(f"zones: {z['name']}.{part} (hue {h:.0f}) is outside {z['family']} ({lo:.0f}-{hi:.0f})")
    for a, b in combinations(zones, 2):
        gap = float(hue_gap(d[f"zones.{a['name']}.edge"]["hue"], d[f"zones.{b['name']}.edge"]["hue"]))
        if gap < ZONE_GAP:
            out.append(f"zones: {a['name']} and {b['name']} are {gap:.0f} degrees apart, under {ZONE_GAP:.0f}")

    # Types: every cast type coloured from its family; the key's light types take its lighter member.
    types = palette.get("types", {})
    key_light = max(fams["key"], key=lambda k: d[k]["L"])
    for t in cast:
        role = types.get(t["id"])
        family = SLOT_FAMILY.get(t["slot"])
        if role not in rs:
            out.append(f"types: {t['id']} has no colour ({role!r} is not a role)")
        elif family == "boss" and not role.startswith("boss."):
            out.append(f"types: {t['id']} is the boss's, not {role}")
        elif family != "boss" and not role.startswith(f"enemies.{family}."):
            out.append(f"types: {t['id']} is in the {family} family (its slot {t['slot']}), not {role}")
        elif t["slot"] in ("keyLight", "keyDeep") and (role == key_light) != (t["slot"] == "keyLight"):
            out.append(f"types: {t['id']} ({t['slot']}) takes {role}, the key's {'lighter' if role == key_light else 'deeper'}")
    for name in sorted(set(types) - {t["id"] for t in cast}):
        out.append(f"types: {name} is not in the cast")
    return out


def load_cast(content: Path | None = None) -> list[dict]:
    """Content/enemies.json's cast."""
    path = (content or default_path().parent) / "enemies.json"
    return json.loads(path.read_text(encoding="utf-8"))["cast"]


def table(palette: dict) -> str:
    """Every role's hex, HSV hue, L* and C*ab, as markdown."""
    lines = ["| role | hex | hue | L* | C* |", "|---|---|---:|---:|---:|"]
    for k, v in roles(palette).items():
        d = describe(v)
        lines.append(f"| `{k}` | `#{v[0]:02x}{v[1]:02x}{v[2]:02x}` | {d['hue']:.0f} | {d['L']:.0f} | {d['C']:.0f} |")
    return "\n".join(lines)
