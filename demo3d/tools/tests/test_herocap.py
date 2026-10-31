"""herocap's recipes (herocap/recipes/*.json): dot and line faces only, and one per native atlas."""

from __future__ import annotations

import json
from pathlib import Path

import yaml

from demo3d_tools import herocap, paths

ROOT = paths.git_root(Path(__file__).parent)
CLIPS = {c["name"] for c in herocap.load_clips(ROOT)["clips"]}


def recipe(**face: str) -> dict:
    return {"atlas": "orc", "parts": {"armor": "FantasyHeroes.Vikings.Armor.VikingRoughArmor1"},
            "face": {"eyes": "Common.Undead.Eyes.GlowingEyes", **face}, "clips": ["walk"]}


def test_every_recipe_has_a_dot_and_line_face_and_known_clips() -> None:
    assert herocap.check_recipes(ROOT) == []


def test_a_banned_face_part_fails() -> None:
    assert herocap.check_recipe("orc", recipe(), CLIPS) == []
    for part, banned in [("eyes", "Common.Basic.Eyes.Male"), ("eyes", "Common.Basic.Eyes.Female"),
                         ("eyes", "Common.Emoji.Eyes.Happy"), ("eyes", "Common.Undead.Eyes.ZombieEyes3"),
                         ("eyes", ""), ("mouth", "Common.Emoji.Mouth.XD"), ("mouth", "Common.Basic.Mouth.Zombie"),
                         ("eyebrows", "Common.Emoji.Eyebrows.Angry")]:
        problems = herocap.check_recipe("orc", recipe(**{part: banned}), CLIPS)
        assert len(problems) == 1 and f"face {part}" in problems[0], (part, banned, problems)


def test_unknown_clips_and_keys_fail() -> None:
    bad = {**recipe(), "clips": ["moonwalk"], "colour": "red"}
    problems = herocap.check_recipe("orc", bad, CLIPS)
    assert any("unknown clip 'moonwalk'" in p for p in problems)
    assert any("unknown keys ['colour']" in p for p in problems)


def test_every_atlas_of_the_survivors_run_has_a_recipe() -> None:
    enemies = yaml.safe_load((ROOT / "content" / "survivors-enemies.yaml").read_text(encoding="utf-8"))
    atlases = {e["atlas"] for e in enemies["enemies"]} | {"hero", "goblin"}  # goblin: native's fallback look
    assert atlases <= set(herocap.load_recipes(ROOT))


def test_the_budget_counts_padded_frames_and_flash_twins() -> None:
    m = {"clips": [{"name": "walk", "frames": 20, "w": 96, "h": 100, "loop": True}]}
    b = herocap.budget({"orc": m})
    assert b["frames"] == 20 and b["pixels"] == 20 * 100 * 104
    assert (b["pages"], b["pagesWithFlash"]) == (1, 1)
    assert json.loads(json.dumps(b))["atlases"]["orc"]["clips"] == 1
