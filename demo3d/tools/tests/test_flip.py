"""FLIP scoring of A/B capture folders, on small synthetic stills."""

from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image

from demo3d_tools import flip


def still(path: Path, shift: int = 0, spot: bool = False) -> None:
    img = np.zeros((64, 96, 3), dtype=np.uint8)
    img[:, :, 0] = np.linspace(0, 255, 96, dtype=np.uint8)[None, :]
    img[20:40, 30 + shift:50 + shift, :] = 220
    if spot:
        img[5:8, 5:8, :] = 255
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(img).save(path)


def test_identical_stills_score_zero(tmp_path: Path) -> None:
    still(tmp_path / "a" / "still-001.png")
    still(tmp_path / "b" / "still-001.png")
    r = flip.score_dirs(tmp_path / "a", tmp_path / "b")
    assert r["mean"] == 0 and r["p99"] == 0


def test_a_shift_scores_more_than_a_tiny_spot_and_p99_catches_the_spot(tmp_path: Path) -> None:
    for name in ("still-001.png", "still-002.png"):
        still(tmp_path / "base" / name)
        still(tmp_path / "spot" / name, spot=True)
        still(tmp_path / "shift" / name, shift=6)
    spot = flip.score_dirs(tmp_path / "base", tmp_path / "spot")
    shift = flip.score_dirs(tmp_path / "base", tmp_path / "shift")
    assert 0 < spot["mean"] < shift["mean"]
    assert spot["p99"] > 0  # a localized artifact shows in the 99th percentile


def test_thresholds_sit_a_third_of_the_way_to_the_visible_anchor() -> None:
    lines = flip.thresholds({"mean": 0.01, "p99": 0.1}, {"mean": 0.04, "p99": 0.4})
    assert abs(lines["mean"] - 0.02) < 1e-12 and abs(lines["p99"] - 0.2) < 1e-12
    assert flip.verdict({"mean": 0.02, "p99": 0.2}, lines)
    assert not flip.verdict({"mean": 0.021, "p99": 0.1}, lines)


def test_unpaired_stills_are_reported(tmp_path: Path) -> None:
    still(tmp_path / "a" / "still-001.png")
    still(tmp_path / "a" / "still-002.png")
    still(tmp_path / "b" / "still-001.png")
    assert flip.score_dirs(tmp_path / "a", tmp_path / "b")["unpaired"] == ["still-002.png"]
