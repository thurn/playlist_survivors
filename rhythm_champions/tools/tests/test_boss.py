"""`champions boss review`: the scenes it plays and the README it keeps."""

from __future__ import annotations

from pathlib import Path

from champions_tools import boss
from champions_tools.cli import build_parser


def test_a_review_plays_the_fight_scene_and_its_motion_on_request() -> None:
    assert boss.scenes("fatOgre", False) == ["fight-fatOgre"]
    assert boss.scenes("fatOgre", True) == ["fight-fatOgre", "fight-fatOgre-motion"]
    a = build_parser().parse_args(["boss", "review", "fatOgre", "--motion"])
    assert (a.action, a.boss, a.motion, a.func.__name__) == ("review", "fatOgre", True, "cmd_boss")


def test_the_readme_lists_the_moments_counts_the_motion_frames_and_keeps_the_notes(tmp_path: Path) -> None:
    readme = tmp_path / "README.md"
    readme.write_text("# old\n\n## Notes\n\nThe stomp reads well.\n", encoding="utf-8")
    text = boss.write_readme("fatOgre", ["still-fight-fatOgre-entrance.png", "still-fight-fatOgre-motion-motion-0001.png"],
                             boss.notes(readme))
    assert "`still-fight-fatOgre-entrance.png`" in text
    assert "Plus 1 motion frames" in text
    assert text.rstrip().endswith("The stomp reads well.")
    assert "(None yet.)" in boss.write_readme("fatOgre", [], "")
