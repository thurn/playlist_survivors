"""The look harness's stats and readability, on synthetic stills with known L*, chroma and contrast."""

from __future__ import annotations

import numpy as np
import pytest

from demo3d_tools import look

# sRGB greys and their CIE L*: 119 is L* 50.0; 19 is L* 5.9 (TD15's darkest ring); 255 is 100.
GREY_50, GREY_DARK = 119, 19


def frame(value, h: int = 60, w: int = 80) -> np.ndarray:
    return np.full((h, w, 3), value, dtype=np.uint8)


def test_lstar_and_chroma_of_known_colours() -> None:
    px = np.array([[0, 0, 0], [255, 255, 255], [GREY_50] * 3, [255, 0, 0]], dtype=np.uint8)
    assert look.lstar(px) == pytest.approx([0, 100, 50.0, 53.2], abs=0.1)
    assert look.chroma(px) == pytest.approx([0, 0, 0, 104.6], abs=0.6)  # sRGB red: L* 53.2, C*ab 104.6


def test_a_half_black_half_white_frame() -> None:
    img = frame(0)
    img[:, 40:] = 255
    s = look.still_stats(img)
    assert (s["p2"], s["p50"], s["p98"]) == pytest.approx((0, 50, 100))
    assert s["contrast"] == pytest.approx(50)  # the standard deviation of L*
    assert s["nearBlack"] == pytest.approx(0.5)
    assert s["clipped"] == pytest.approx(0.5)
    assert s["chroma"] == pytest.approx(0, abs=0.01)  # white is D65 to the matrices' rounding


def test_chroma_is_the_frames_mean_and_clipping_needs_every_channel() -> None:
    img = frame(GREY_50)
    img[:30] = (255, 0, 0)
    s = look.still_stats(img)
    assert s["chroma"] == pytest.approx(104.6 / 2, abs=0.3)
    assert s["clipped"] == 0  # pure red has a channel at 0
    assert s["contrast"] == pytest.approx(abs(53.2 - 50.0) / 2, abs=0.1)


def test_dilate_is_a_square() -> None:
    m = np.zeros((21, 21), dtype=bool)
    m[10, 10] = True
    d = look.dilate(m, 3)
    assert d.sum() == 49 and d[7:14, 7:14].all()


def horde_on_ground(monster: int, ground: int) -> tuple[np.ndarray, np.ndarray]:
    img = frame(ground, 100, 100)
    mask = np.zeros((100, 100), dtype=bool)
    mask[40:60, 40:60] = True
    img[mask] = monster
    return img, mask


def test_readability_is_absolute() -> None:
    light, mask = horde_on_ground(GREY_50, GREY_DARK)
    r = look.readability(light, mask, frame(GREY_DARK, 100, 100))
    assert r["dL"] == pytest.approx(50.0 - 5.9, abs=0.2) and r["pass"]
    assert r["dLbg"] == pytest.approx(r["dL"])
    assert r["monsterP10"] == pytest.approx(50.0, abs=0.1)
    # A dark horde on a light ground reads as well: negative dL*, and it passes the floor.
    dark, mask = horde_on_ground(GREY_DARK, GREY_50)
    r = look.readability(dark, mask, frame(GREY_50, 100, 100))
    assert r["dL"] == pytest.approx(-(50.0 - 5.9), abs=0.2) and r["pass"]
    # A horde close to its ground fails.
    close, mask = horde_on_ground(GREY_50, 100)
    assert not look.readability(close, mask, close)["pass"]


def test_readability_without_monsters() -> None:
    img = frame(GREY_50)
    assert "error" in look.readability(img, np.zeros(img.shape[:2], dtype=bool), img)


def test_effect_stats_count_only_the_pixels_effects_change() -> None:
    measured = frame(GREY_DARK)
    horde = measured.copy()
    horde[:15] = 255  # a quarter of the frame blown out by an effect
    horde[15:30] = GREY_DARK + 1  # a change below the threshold is not an effect
    e = look.effect_stats(horde, measured)
    assert e["share"] == pytest.approx(0.25)
    assert e["p50"] == pytest.approx(100) and e["clipped"] == 1
    assert look.effect_stats(measured, measured)["share"] == 0
