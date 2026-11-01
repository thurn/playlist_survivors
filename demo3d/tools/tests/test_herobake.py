"""herocap bake's pieces: native's names for the captured clips, the packing and the colour bleed."""

from __future__ import annotations

import numpy as np
import pytest

from demo3d_tools import herobake

ENEMY_CLIPS = {c: {} for c in ("idle", "walk", "hurt", "death-front", "jab", "victory")}


def test_an_enemy_gets_native_s_names_and_its_dance_is_victory_s_halves() -> None:
    names = herobake.names("slime", ENEMY_CLIPS)
    assert names == {"walk": ("walk", None), "hurt": ("hurt", None), "death": ("death-front", None),
                     "kick": ("victory", 0), "kick-alt": ("victory", 1)}
    # A thorn plant is rooted: it walks in place on its idle clip.
    assert herobake.names("thorn-plant-summer", ENEMY_CLIPS)["walk"] == ("idle", None)


def test_the_hero_s_beat_step_is_its_run_and_a_missing_clip_fails() -> None:
    clips = {c: {} for c in ("idle", "walk", "run", "slash", "jab", "slash2h", "cast", "hurt")}
    names = herobake.names("hero", clips)
    assert names["jump"] == ("run", None) and names["slash-oversize"] == ("slash2h", None)
    del clips["cast"]
    with pytest.raises(SystemExit, match="spellcast"):
        herobake.names("hero", clips)


def test_packing_keeps_boxes_apart_and_inside_pages() -> None:
    sizes = [(1000, 900), (3000, 900), (500, 300)] * 12
    spots, heights = herobake.pack(sizes)
    assert len(heights) > 1
    taken = [np.zeros((h, herobake.PAGE), bool) for h in heights]
    for (w, h), (page, x, y) in zip(sizes, spots):
        assert x + w <= herobake.PAGE and y + h <= heights[page]
        assert not taken[page][y:y + h, x:x + w].any()
        taken[page][y:y + h, x:x + w] = True


def test_bleed_colours_the_transparent_rim_and_keeps_alpha() -> None:
    img = np.zeros((7, 7, 4), np.uint8)
    img[3, 3] = (200, 100, 50, 255)
    out = herobake.bleed(img, steps=2)
    assert (out[..., 3] == img[..., 3]).all()
    assert tuple(out[3, 5, :3]) == (200, 100, 50)
    assert tuple(out[0, 0, :3]) == (0, 0, 0)  # (three texels out: beyond two steps)
