"""The palette (Content/palette.json) against style.md's rules: families, the key, the arc, values, white and grey."""

import copy

import pytest

from champions_tools import palette as pal


def test_the_palette_keeps_every_rule():
    assert pal.problems(pal.load(), pal.load_cast()) == []


def test_every_cast_type_has_its_colour():
    palette = pal.load()
    roles = pal.roles(palette)
    assert {t["id"]: palette["types"][t["id"]] in roles for t in pal.load_cast()} == {
        t["id"]: True for t in pal.load_cast()}


def _set(palette: dict, path: str, value: object) -> None:
    *head, last = path.split(".")
    node = palette
    for k in head:
        node = node[int(k)] if isinstance(node, list) else node[k]
    if value is None:
        del node[last]
    else:
        node[int(last) if isinstance(node, list) else last] = value


@pytest.mark.parametrize(("path", "value", "rule"), [
    ("boss.body", "#30d030", "boss:"),  # a green boss: outside the counter's reach
    ("enemies.key.blue", "#eef0ff", "white:"),  # a white monster: white is the hero's
    ("warning", "#f5e900", "hazard:"),  # an amber warning: not electric yellow, and on the troll's hue
    ("enemies.accent.magenta", "#6a5cff", "families:"),  # an accent beside the key
    ("zones.0.line", "#35135d", "key:"),  # a violet first zone: the floor no longer keys with the crowd
    ("enemies.counter.amber", "#ffd27a", "values:"),  # an elite on the hero's rung
    ("zones.1.fill", "#3a3a3a", "grey:"),  # a grey fill: grey is a wall's
    ("enemies.accent.violet", "#9cff2e", "arc:"),  # a lime monster in the warnings' arc
    ("types.skeleton", "enemies.accent.magenta", "types:"),  # a key type in the accent's colour
    ("types.dragon", None, "types:"),  # a type with no colour
])
def test_a_broken_rule_is_named(path, value, rule):
    palette = copy.deepcopy(pal.load())
    _set(palette, path, value)
    found = pal.problems(palette, pal.load_cast())
    assert any(p.startswith(rule) for p in found), found
