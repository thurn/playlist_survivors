import json
from pathlib import Path

import numpy as np
from PIL import Image

from demo3d_tools import game


def still(dir: Path, name: str, monster: int, ground: int) -> Path:
    """A 64x64 still: a grey ground with a 16x16 monster square in the middle, and its ID render."""
    rgb = np.full((64, 64, 3), ground, np.uint8)
    rgb[24:40, 24:40] = monster
    path = dir / f"{name}.png"
    Image.fromarray(rgb).save(path)
    mask = np.zeros((64, 64, 3), np.uint8)
    mask[24:40, 24:40] = 255
    Image.fromarray(mask).save(dir / f"{name}-mask.png")
    return path


def test_readability_passes_a_horde_that_stands_out_and_records_it(tmp_path: Path) -> None:
    stills = [still(tmp_path, "still-001.000", monster=200, ground=40), still(tmp_path, "still-002.000", monster=10, ground=120)]
    assert game.readability(stills, tmp_path) == 0
    results = json.loads((tmp_path / "readability.json").read_text())
    assert set(results) == {"still-001.000", "still-002.000"}
    assert results["still-001.000"]["dL"] > 17
    assert results["still-002.000"]["dL"] < -17  # a dark horde on a light ground reads too


def test_readability_fails_a_horde_close_to_the_ground_or_without_its_id_render(tmp_path: Path) -> None:
    assert game.readability([still(tmp_path, "still-001.000", monster=60, ground=50)], tmp_path) == 1
    lone = tmp_path / "still-002.000.png"
    Image.fromarray(np.zeros((8, 8, 3), np.uint8)).save(lone)
    assert game.readability([lone], tmp_path) == 1
