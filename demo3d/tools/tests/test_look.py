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


def test_the_clipped_blob_is_the_largest_connected_region() -> None:
    img = frame(GREY_50, 100, 100)
    img[10:20, 10:20] = 255  # 100 px
    img[50:60, 50:55] = 255  # 50 px, joined diagonally to the next
    img[60:70, 55:60] = 255  # 50 px
    img[::7, 90] = 255  # scattered hot pixels
    s = look.still_stats(img)
    assert s["clippedBlob"] == pytest.approx(100 / 10_000)
    assert look.largest_region(np.zeros((5, 5), dtype=bool)) == 0
    u = np.zeros((6, 7), dtype=bool)
    u[0:5, 0] = u[0:5, 6] = u[5, :] = True  # a U whose arms join only at the bottom row
    assert look.largest_region(u) == 5 + 5 + 7


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


def test_monster_chroma_is_the_monsters_mean() -> None:
    img, mask = horde_on_ground(GREY_50, GREY_DARK)
    img[40:50, 40:60] = [255, 0, 0]  # half the horde sRGB red (C*ab 104.6), half grey
    assert look.readability(img, mask, frame(GREY_DARK, 100, 100))["cMonster"] == pytest.approx(104.6 / 2, abs=0.3)


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


def view_looking_down(hero=(3.0, 0.0, -2.0), height=20.0, back=15.0, fov_deg=40.0, aspect=16 / 9) -> dict:
    """An OpenGL view-projection (Unity's camera.projectionMatrix * worldToCameraMatrix) aimed at the hero."""
    eye = np.array(hero) + np.array([0.0, height, -back])
    fwd = np.array(hero) - eye
    fwd /= np.linalg.norm(fwd)
    right = np.cross([0.0, 1.0, 0.0], fwd)
    right /= np.linalg.norm(right)
    up = np.cross(fwd, right)
    view = np.eye(4)
    view[:3, :3] = np.stack([right, up, -fwd])  # camera space looks down -z
    view[:3, 3] = -view[:3, :3] @ eye
    f, n, fa = 1 / np.tan(np.radians(fov_deg) / 2), 0.3, 500.0
    proj = np.array([[f / aspect, 0, 0, 0], [0, f, 0, 0],
                     [0, 0, (fa + n) / (n - fa), 2 * fa * n / (n - fa)], [0, 0, -1, 0]])
    return {"viewProj": (proj @ view).flatten().tolist(), "hero": list(hero)}


def test_the_ground_distance_is_zero_at_the_hero_and_grows_toward_the_top() -> None:
    v = view_looking_down()
    d = look.ground_distance(v, 160, 90)
    assert d[45, 80] == pytest.approx(0, abs=0.4)  # the camera aims at the hero's feet
    assert d[0, 80] > d[89, 80] > d[45, 80]  # the top of the frame is the far ground


def test_the_stage_compares_the_ground_near_the_hero_with_the_border() -> None:
    v = view_looking_down()
    d = look.ground_distance(v, 160, 90)
    img = np.repeat(np.where(d <= 6, GREY_50, GREY_DARK).astype(np.uint8)[..., None], 3, axis=-1)
    st = look.stage(img, v, None)
    assert st["lGround"] == pytest.approx(50.0, abs=0.1)
    assert st["lBorder"] == pytest.approx(5.9, abs=0.1)
    assert st["pass"]
    monsters = d <= 6  # a horde over all of it leaves no ground to measure
    assert "error" in look.stage(img, v, monsters)
    assert not look.stage(frame(GREY_50, 90, 160), v, None)["pass"]  # an evenly lit frame has no stage


def test_fighting_distance_matches_the_layouts_clearings_and_paths() -> None:
    areas = [{"type": "clearing", "center": [0, 0], "radius": 10},
             {"type": "path", "from": [20, 0], "to": [40, 0], "width": 4},
             {"type": "forest", "center": [0, 0], "radius": 100}]
    x, z = np.array([0.0, 12.0, 30.0, 30.0]), np.array([0.0, 0.0, 1.0, 5.0])
    assert look.fighting_distance(areas, x, z) == pytest.approx([-10, 2, -1, 3])


def test_the_ground_look_splits_fighting_ground_from_the_forest() -> None:
    v = view_looking_down(hero=(0.0, 0.0, 0.0))
    areas = [{"type": "clearing", "center": [0, 0], "radius": 6}]
    x, z = look.ground_points(v, 160, 90)
    inside = np.hypot(x, z) <= 6
    img = np.repeat(np.where(inside, GREY_50, GREY_DARK).astype(np.uint8)[..., None], 3, axis=-1)
    g = look.ground_look(img, v, areas)
    assert g["lFight"] == pytest.approx(50.0, abs=0.1) and g["lOuter"] == pytest.approx(5.9, abs=0.1)
    assert g["detail"] == pytest.approx(0, abs=0.01)  # a flat ground has no detail
    img[::2, ::2] = 255  # a fine texture raises it
    assert look.ground_look(img, v, areas)["detail"] > 10


def test_window_detail_ignores_windows_off_the_region() -> None:
    lum = np.zeros((32, 32))
    lum[:16, :16] = np.tile([0.0, 10.0], (16, 8))  # standard deviation 5
    region = np.zeros((32, 32), dtype=bool)
    region[:16, :20] = True  # one whole window, and part of another
    assert look.window_detail(lum, region) == pytest.approx(5)
    assert look.window_detail(lum, np.zeros_like(region)) is None
