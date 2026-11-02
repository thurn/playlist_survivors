"""The look harness's stats and readability, on synthetic stills with known L*, chroma and contrast."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

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


def test_cool_share_counts_saturated_cyan_to_violet_only() -> None:
    img = frame(GREY_50, 10, 10)
    img[0:2] = (40, 200, 255)  # cyan-azure, hue about 197
    img[2:3] = (140, 60, 255)  # violet, hue about 265
    img[3:4] = (255, 120, 30)  # warm orange: not cool
    img[4:5] = (60, 62, 70)  # a dull navy grey: cool hue, but under the chroma bar
    assert look.cool_share(img) == pytest.approx(0.3)
    assert look.still_stats(img)["cool"] == pytest.approx(0.3)


def test_effects_are_brightest_when_their_p98_beats_the_rest() -> None:
    measured = frame(GREY_DARK, 100, 100)
    measured[:, :20] = GREY_50  # a lit ground strip, not an effect
    horde = measured.copy()
    horde[40:60, 40:60] = 255  # a hot effect core
    e = look.effect_stats(horde, measured)
    assert e["share"] == pytest.approx(0.04)
    assert e["p98"] == pytest.approx(100) and e["restP98"] == pytest.approx(50.0, abs=0.1)


def test_contact_sheet_has_a_column_per_set_and_skips_stills_a_set_lacks(tmp_path: Path):
    sets = [tmp_path / n for n in ("a", "b", "c")]
    for i, d in enumerate(sets):
        d.mkdir()
        for n in ("song-060", "path-horde"):
            if not (n == "path-horde" and i == 2):
                Image.new("RGB", (look.JPEG_W, look.JPEG_H), (40 * i, 0, 0)).save(d / f"{n}.jpg")
    dst = tmp_path / "sheet.jpg"
    look.contact_sheet(["song-060", "path-horde"], sets, dst, ["a", "b", "c"], scale=4)
    with Image.open(dst) as im:
        assert im.size == (3 * (look.JPEG_W // 4), 18 + look.JPEG_H // 4)
        assert b"ps-render" in im.info["comment"]


def test_the_horde_against_structures_needs_enough_structure_pixels_near_it() -> None:
    img, monsters = horde_on_ground(GREY_50, GREY_DARK)
    structures = np.zeros(monsters.shape, dtype=bool)
    r = look.structure_contrast(img, monsters, structures)
    assert r["setPx"] == 0 and "dL" not in r
    # A wall beside the horde, far darker than it.
    structures[30:70, 62:80] = True
    img[structures] = 0
    r = look.structure_contrast(img, monsters, structures)
    assert r["ringPx"] >= look.STRUCTURE_MIN_PX
    assert r["dL"] == pytest.approx(50.0, abs=0.2) and r["lStructure"] == pytest.approx(0.0, abs=0.1)
    # A wall beyond the ring isn't near the horde.
    far = np.zeros(monsters.shape, dtype=bool)
    far[:, 95:] = True
    assert "dL" not in look.structure_contrast(img, monsters, far)


def test_type_readability_classes_monster_pixels_by_their_signature_hue() -> None:
    img = frame(GREY_50, 100, 120)
    img[:, 60:] = (40, 160, 40)  # the right half's ground is green: a goblin's own hue
    mask = np.zeros(img.shape[:2], dtype=bool)
    img[35:65, 15:45] = (200, 90, 40)  # an orange skeleton on grey
    img[35:65, 75:105] = (60, 200, 30)  # a green goblin on green
    img[2:12, 2:12] = (30, 30, 30)  # an unsaturated monster pixel: dark side, classed as nothing
    mask[35:65, 15:45] = mask[35:65, 75:105] = mask[2:12, 2:12] = True
    hues = {"skeleton": 20.0, "goblin": 100.0, "troll": 220.0}
    t = look.type_readability(img, mask, hues)
    assert set(t) == {"skeleton", "goblin"}  # no troll pixels: no row
    assert t["skeleton"]["px"] == t["goblin"]["px"] == 900
    assert t["skeleton"]["share"] == pytest.approx(900 / 1900)
    assert t["skeleton"]["lRing"] == pytest.approx(50.0, abs=0.1)
    assert t["skeleton"]["dL"] == pytest.approx(look.lstar(np.array([200, 90, 40], dtype=np.uint8)) - 50.0, abs=0.1)
    assert t["skeleton"]["hueMatch"] == 0  # grey ground: nothing in its hue
    assert t["goblin"]["hueMatch"] == 1  # green ground all round it


# --- The neon look's metrics (ps-wnj.2, ps-wnj.21) ---

ELECTRIC = {"name": "electric", "void": {"floor": "#05060a"}, "hero": {"body": "#f4f1ff", "blade": "#ffffff"},
            "enemies": ["#ff2e88", "#28f0ff", "#9dff3a"], "shatter": {"gem": "#ffe9a8"},
            "zones": [{"name": "cobalt", "tint": "#1d3cff"}]}
CYAN, MAGENTA, BLUE = (40, 240, 255), (255, 46, 136), (40, 60, 255)  # enemies.1, enemies.0, near the zone tint
ORANGE, GEM = (255, 60, 0), (255, 233, 168)  # ORANGE: 30+ HSV degrees from every role; GEM: shatter.gem
RULES = {"presets": ["edge", "solid"], "pairings": {"electric": "solid"}, "rules": {
    "floor_p50_max": {"value": 8},
    "frame_p25_max": {"value": 6, "unless": "floor_p50"},
    "black_share_min": {"value": 0.30, "solid": 0.25},
    "bright_share_max": {"edge": 0.22, "solid": 0.32},
    "clipped_impact_max": {"value": 0.12, "metric": "clipped", "on": "impact"},
    "key60_median_min": {"value": 0.62, "on": "cut"},
    "swarm_separation_min": {"value": 13}}}


def crowd(outline: bool, spacing: int = 24, r: int = 16, w: int = 1920, h: int = 1080) -> np.ndarray:
    """A near-black frame with a packed crowd of overlapping cyan discs, flat or each with a dark rim."""
    img = np.full((h, w, 3), 6, dtype=np.uint8)
    yy, xx = np.mgrid[-r:r + 1, -r:r + 1]
    d = np.hypot(yy, xx)
    for i, cy in enumerate(range(300, 780, spacing)):
        for cx in range(500 + (spacing // 2) * (i % 2), 1400, spacing):
            patch = img[cy - r:cy + r + 1, cx - r:cx + r + 1]
            patch[d <= r] = CYAN
            if outline:
                patch[(d <= r) & (d > r - 3)] = (4, 30, 34)
    return img


def top_down(w: int, h: int, metres: float = 20.0) -> dict:
    """A view json for an orthographic camera looking straight down on the ground at y 0, `metres` wide, the hero's
    feet at the origin: the still's centre."""
    sx, sz = 2 / metres, 2 / (metres * h / w)
    return {"viewProj": [sx, 0, 0, 0, 0, 0, -sz, 0, 0, -0.01, 0, 0, 0, 0, 0, 1], "hero": [0, 0, 0]}


def test_palette_roles_are_every_colour_by_its_path() -> None:
    roles = look.palette_roles(ELECTRIC)
    assert roles["void.floor"] == (5, 6, 10) and roles["hero.blade"] == (255, 255, 255)
    assert roles["enemies.1"] == (0x28, 0xF0, 0xFF)
    assert roles["zones.cobalt.tint"] == (0x1D, 0x3C, 0xFF)
    assert "name" not in roles and len(roles) == 8


def test_the_key_is_the_best_60_degree_window_and_families_are_greedy_windows() -> None:
    assert look.hue_windows(np.full(10, 186.0)) == (1.0, 1)
    assert look.hue_windows(np.r_[np.full(50, 186.0), np.full(50, 334.0)]) == (0.5, 2)
    key, families = look.hue_windows(np.r_[np.full(50, 186.0), np.full(45, 334.0), np.full(5, 90.0)])
    assert key == pytest.approx(0.5) and families == 2  # 5% is under a family's 8%
    assert look.hue_windows(np.arange(0, 360, 0.5)) == (pytest.approx(1 / 6), 6)  # a rainbow: six windows
    assert look.hue_windows(np.r_[np.full(50, 355.0), np.full(50, 15.0)])[0] == 1.0  # windows wrap round red


def test_frame_measures_follow_the_guides_pixel_classes() -> None:
    img = frame(GREY_DARK, 100, 100)  # L* 5.9: near black
    img[30:70, 30:70] = 100  # L* 42.4: a glow round the emitter, not bright
    img[40:60, 40:60] = 255  # an emitter: bright, white and clipped
    img[:10, :50] = MAGENTA  # colourful and bright (L* 57.7), under the emitter rung
    img[:10, 50:] = BLUE  # colourful, not bright
    m = look.frame_measures(img)
    assert m["frame_p25"] == pytest.approx(5.9, abs=0.1)
    assert m["black_share"] == pytest.approx(0.74)
    assert m["bright_share"] == pytest.approx(0.09)
    assert m["clipped"] == m["clipped_blob"] == m["white"] == pytest.approx(0.04)
    assert m["halo"] == pytest.approx(42.4 - 5.9, abs=0.1)  # the ring 3-10 px out lies wholly in the glow
    assert (m["key60"], m["families"]) == (0.5, 2)  # magenta and blue, 100 degrees apart
    flat = look.frame_measures(frame(GREY_DARK))
    assert "halo" not in flat and "key60" not in flat  # no emitter, no colour


def test_conformance_judges_lit_colourful_pixels_by_their_hue() -> None:
    img = frame(5, 100, 100)  # a near-black floor: not judged
    img[:40] = CYAN  # an enemy role
    img[40:60] = ORANGE  # off the palette: no role near its hue
    img[60:80] = 255  # white: no chroma, not judged
    img[80:90] = (60, 0, 0)  # a dark red: under L* 40, not judged
    c = look.palette_conformance(img, look.palette_roles(ELECTRIC))
    assert c["palette_conformance"] == pytest.approx(2 / 3)
    assert c["roles"] == {"enemies.1": pytest.approx(2 / 3)}
    assert look.palette_conformance(frame(5), look.palette_roles(ELECTRIC)) == {}


def test_a_flat_clump_reads_as_a_blob_and_an_outlined_one_as_a_swarm() -> None:
    roles = look.palette_roles(ELECTRIC)
    outlined = look.neon_size(crowd(True))
    blob, swarm = look.swarm_separation(look.neon_size(crowd(False)), roles), look.swarm_separation(outlined, roles)
    assert blob["dense_tiles"] > 50 and swarm["dense_tiles"] > 50
    assert blob["swarm_separation"] < 3 and swarm["swarm_separation"] > 26  # twice the guide's 13
    assert {t["enemy"] for t in swarm["tiles"]} == {"enemies.1"}  # all cyan
    # The ID render keeps the measure to monsters: a mask over half the crowd halves its dense tiles.
    half = np.zeros(outlined.shape[:2], dtype=bool)
    half[:, :outlined.shape[1] // 2] = True
    masked = look.swarm_separation(outlined, roles, half)
    assert 0.3 < masked["dense_tiles"] / swarm["dense_tiles"] < 0.7
    # A sparse crowd has no dense clump to judge; a formation in the gem's hue is no clump.
    assert look.swarm_separation(look.neon_size(crowd(False, spacing=48)), roles)["dense_tiles"] == 0
    gem = frame(6, 540, 960)
    gem[100:400, 200:700] = GEM
    assert look.swarm_separation(gem, roles)["dense_tiles"] == 0


def test_speckle_in_a_flat_clump_is_no_seam() -> None:
    """Black specks (shards, noise) on one in twenty pixels of a flat colour field (v2's red mass) do not make it read
    as individuals: the smoothing averages them away before the seams are found."""
    img = frame(6, 540, 960)
    img[100:440, 200:760] = CYAN
    rng = np.random.default_rng(1)
    specks = rng.random(img.shape[:2]) < 0.05
    img[specks & (img[..., 1] == 240)] = (4, 30, 34)
    s = look.swarm_separation(img, look.palette_roles(ELECTRIC))
    assert s["dense_tiles"] > 100 and s["swarm_separation"] < 13


def test_mask_rules_find_the_floor_the_hero_and_the_monsters_form() -> None:
    w = h = 320  # 20 m wide: 16 px a metre, the hero's 0.5 m radius 8 px round the centre
    img = frame(GREY_DARK, h, w)  # the floor, L* 5.9
    img[150:170, 150:170][np.hypot(*np.mgrid[-10:10, -10:10] + 0.5) <= 6] = 255  # the hero, inside its capsule
    monsters = np.zeros((h, w), dtype=bool)
    monsters[256:320, 0:64] = True  # a monster lit in 16 px stripes: form in every tile it covers
    img[256:320, 0:64][:, np.arange(64) % 32 < 16] = 220
    structures = np.zeros((h, w), dtype=bool)
    structures[0:64, 256:320] = True  # a lit structure: not floor
    img[0:64, 256:320] = 200
    m = look.mask_rules(img, monsters, structures, top_down(w, h))
    lit = float(look.lstar(np.array([220] * 3, dtype=np.uint8)))
    assert m["floor_p50"] == m["floor_p90"] == m["floor_p99"] == pytest.approx(5.9, abs=0.1)
    assert m["hero_margin"] == pytest.approx(100 - lit, abs=0.1)
    assert m["monster_form"] == pytest.approx(lit - 5.9, abs=0.1)
    # Game's view json marks its ID render as the crowd alone: the hero still, no floor.
    crowd_only = look.mask_rules(img, monsters, None, {**top_down(w, h), "floor": False})
    assert "floor_p50" not in crowd_only and crowd_only["hero_margin"] == pytest.approx(100 - lit, abs=0.1)
    # A clump standing round and behind the knight covers its capsule in the crowd's ID render: too little of the hero
    # is clear of it to measure.
    buried = monsters.copy()
    buried[140:180, 140:180] = True
    buried[160, 160] = False
    hidden = look.mask_rules(img, buried, None, {**top_down(w, h), "floor": False})
    assert hidden["hero_visible"] < look.HERO_VISIBLE_MIN and "hero_margin" not in hidden
    assert crowd_only["hero_visible"] > 0.9
    # A flat monster, with no view json: no floor or hero, and no form; it stands well off the ground.
    img[256:320, 0:64] = CYAN
    flat = look.mask_rules(img, monsters, None, None)
    assert flat["monster_form"] == pytest.approx(0, abs=0.1) and "floor_p50" not in flat and "hero_margin" not in flat
    assert flat["readability"] == pytest.approx(look.lstar(np.array(CYAN, dtype=np.uint8)) - 5.9, abs=0.1)


def test_rules_are_keyed_as_the_guide_and_pick_the_presets_values() -> None:
    def value(key: str, preset: str | None) -> float:
        return next(r["value"] for r in look.load_rules(RULES, preset) if r["key"] == key)

    assert value("black_share_min", "solid") == 0.25 and value("black_share_min", "edge") == 0.30
    assert value("bright_share_max", "edge") == 0.22
    assert value("bright_share_max", None) == 0.32 and value("black_share_min", None) == 0.30  # the loosest
    rules = look.load_rules(RULES, "edge")
    assert look.check_rules({"black_share": 0.27, "bright_share": 0.25, "swarm_separation": 14}, rules) == {
        "black_share_min": "black_share 0.27 < 0.3", "bright_share_max": "bright_share 0.25 > 0.22"}
    assert look.check_rules({"frame_p25": 9}, rules) == {"frame_p25_max": "frame_p25 9 > 6"}
    assert look.check_rules({"frame_p25": 9, "floor_p50": 4}, rules) == {}  # the floor, measured, replaces the fallback
    assert look.check_rules({"clipped": 0.2, "swarm_separation": 5}, rules, "impact") == {
        "clipped_impact_max": "clipped 0.2 > 0.12"}  # impact frames: only the impact rules
    assert look.check_rules({"key60_median": 0.5}, rules, "cut") == {"key60_median_min": "key60_median 0.5 < 0.62"}


def test_the_cuts_measures_span_its_stills() -> None:
    cut = look.cut_metrics({"a": {"key60": 0.5, "frame_p25": 2.0, "floor_p50": 3.0},
                            "b": {"key60": 0.7, "frame_p25": 9.0}, "c": {"key60": 0.9, "frame_p25": 1.0}})
    assert cut == {"key60_median": 0.7, "shot_floor_range": 8.0}  # b has no floor: its frame p25 stands in


def test_a_flash_is_a_large_near_white_region_and_flashing_stills_are_counted() -> None:
    img = frame(GREY_DARK, 100, 100)
    img[0:2, 0:2] = 255  # a spark: near white, tiny
    assert look.frame_measures(img)["flash"] == pytest.approx(4 / 10000)
    img[50:60, 50:60] = 255  # a structure flashing: 1% of the frame
    assert look.frame_measures(img)["flash"] == pytest.approx(0.01)
    cut = look.cut_metrics({"a": {"frame_p25": 1.0, "flash": 0.01}, "b": {"frame_p25": 1.0, "flash": 0.0004},
                            "c": {"frame_p25": 1.0, "flash": 0.0}, "d": {"frame_p25": 1.0, "flash": 0.0}})
    assert cut["flash_frames"] == 0.25


def test_the_background_light_is_the_frames_light_off_the_monsters_and_structures() -> None:
    w = h = 100
    img = frame(0, h, w)
    img[:, :50] = 60  # a lit floor, under the emitter rung: background
    monsters = np.zeros((h, w), dtype=bool)
    monsters[:, 70:] = True
    img[:, 70:] = 60  # monsters as bright as the floor, and as wide (30 px, with the 2 px dilation's 28 px of floor)
    m = look.mask_rules(img, monsters, None, None)
    assert m["background_light"] == pytest.approx(50 / 80)
    img[:, :50] = 255  # the same pixels at the emitter rung are an emitter (an effect, the hero), not background
    assert look.mask_rules(img, monsters, None, None)["background_light"] == 0


def test_look_metrics_writes_the_report_and_fails_on_a_broken_rule(tmp_path: Path) -> None:
    stills, out = tmp_path / "stills", tmp_path / "out"
    stills.mkdir()
    Image.fromarray(crowd(True)).save(stills / "swarm.png")
    Image.fromarray(crowd(False)).save(stills / "blob.png")
    Image.fromarray(crowd(True)).save(stills / "still-001.000.png")  # a game still with its ID render
    mask = np.zeros((1080, 1920, 3), dtype=np.uint8)
    mask[:, :960] = 255
    Image.fromarray(mask).save(stills / "still-001.000-mask.png")
    Image.fromarray(frame(0)).save(stills / "mask-0.png")  # demo3d's ID render: no still
    (tmp_path / "palette.json").write_text(json.dumps(ELECTRIC))
    (tmp_path / "rules.json").write_text(json.dumps(RULES))

    def run(src: Path, impact: list[str] | None = None, preset: str | None = None) -> int:
        return look.cmd_look_metrics(argparse.Namespace(stills=str(src), palette=str(tmp_path / "palette.json"),
                                                        thresholds=str(tmp_path / "rules.json"), out=str(out),
                                                        title=None, preset=preset, impact=impact))

    assert run(stills) == 1
    numbers = json.loads((out / "metrics.json").read_text())
    assert numbers["preset"] == "solid"  # the palette's pairing
    assert set(numbers["stills"]) == {"swarm", "blob", "still-001.000"}
    assert numbers["stills"]["swarm"]["broken"] == {}
    assert list(numbers["stills"]["blob"]["broken"]) == ["swarm_separation_min"]
    assert "readability" in numbers["stills"]["still-001.000"]["metrics"]  # its mask was read
    assert numbers["cut"] == {"metrics": {"key60_median": 1.0, "shot_floor_range": 0.0, "flash_frames": 0.0},
                              "broken": {}}  # one hue, no flash
    assert "| blob |" in (out / "metrics.md").read_text()
    assert (out / "blob-swarm.jpg").exists() and b"ps-render" in (out / "blob-swarm.jpg").read_bytes()[:400]
    assert run(stills, impact=["blob"]) == 0  # an impact frame: only the clipped share is judged
    assert json.loads((out / "metrics.json").read_text())["stills"]["blob"]["impact"] is True
    assert run(stills, preset="hologram") == 2
    assert run(tmp_path / "empty") == 2
