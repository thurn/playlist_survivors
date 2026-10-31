"""The one-shot content converter's units and layout (native's YAML to game/Content/*.json).

Whether the conversion still reproduces the committed JSON is `game content --check`, run by hand:
the JSON is the game's own once edited, and native keeps tuning its YAML, so no check gates on it.
"""

from __future__ import annotations

import json

import pytest

from demo3d_tools import content


def test_units_follow_27_px_per_metre() -> None:
    assert content.metres(190) == 7.037  # native's sword reach
    assert content.metres(content.HERO_SPEED_PX) == 5.1852  # native's 140 px/s
    # VS's enemy/hero speed ratio, 0.231 / 0.825 x speed / 100, holds at the hero's speed.
    assert content.ref_speed(100) / 5.1852 == pytest.approx(0.28, abs=1e-4)
    assert content.vs_metres(1) == pytest.approx(content.VS_UNIT_PX / 27, abs=1e-4)


def test_numbers_drop_float_noise() -> None:
    assert content.num(1 + 0.1 + 0.1) == 1.2
    assert content.num(1300 / 1000 - 0.3) == 1 and isinstance(content.num(1.0), int)


def test_rows_stay_on_one_line() -> None:
    text = content.fmt({"rows": [{"id": "a" * 60, "n": [1, 2], "g": {"p": "x"}, "m": 1.5}]})
    assert text.splitlines()[2].strip().startswith('{"id": "aaaa')
    assert json.loads(text)["rows"][0]["g"] == {"p": "x"}
