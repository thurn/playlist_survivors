"""The filmstrip tool's frame plan, strip and frame comparison, on synthetic frames."""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from demo3d_tools import filmstrip


def test_frames_land_on_the_fixed_time_frames_from_the_start() -> None:
    # Players start at 26.0 and step 1/60 s: the first frame at or after 30.005 is 30 + 1/60.
    times = filmstrip.frame_times(30.005, 4, 2, 26.0)
    assert times == pytest.approx([30 + 1 / 60, 30 + 3 / 60, 30 + 5 / 60, 30 + 7 / 60])
    # A time already on a frame is kept, not rounded up a frame.
    assert filmstrip.frame_times(30.0, 1, 1, 26.0) == pytest.approx([30.0])
    # Still times are written to 6 places; each must pass SongClock's `t + 1e-6 >= still` on its own frame.
    for t in filmstrip.frame_times(41.2, 64, 1, 37.2):
        frame = round((t - 37.2) * 60)
        assert 37.2 + frame / 60 + 1e-6 >= float(f"{t:.6f}")
        assert 37.2 + (frame - 1) / 60 + 1e-6 < float(f"{t:.6f}")


def test_chunks_split_the_frames_in_order(tmp_path: Path) -> None:
    times = [float(i) for i in range(10)]
    plan = filmstrip.chunks(times, 4, tmp_path)
    assert [c.times for c in plan] == [[0, 1, 2, 3], [4, 5, 6, 7], [8, 9]]
    assert [c.out.name for c in plan] == ["chunk-00", "chunk-01", "chunk-02"]


def write(path: Path, value: int, w: int = 64, h: int = 36) -> Path:
    Image.fromarray(np.full((h, w, 3), value, dtype=np.uint8)).save(path)
    return path


def test_the_strip_is_a_grid_and_versus_stacks_each_row(tmp_path: Path) -> None:
    frames = [write(tmp_path / f"a{i}.png", 40 * i) for i in range(5)]
    before = [write(tmp_path / f"b{i}.png", 200) for i in range(5)]
    times = [1.0 + i / 60 for i in range(5)]
    sheet = filmstrip.strip(frames, times, None, 3, 32)
    assert sheet.size == (3 * 32, 2 * (18 + 16))  # 2 rows of 32x18 tiles, each under a 16 px label
    assert sheet.getpixel((32 + 16, 16 + 9)) == (40, 40, 40)  # frame 1: row 0, column 1
    vs = filmstrip.strip(frames, times, (0, 0, 32, 18), 3, 32, rows_from=[before, frames], labels=["b", "a"])
    assert vs.size == (3 * 32, 2 * 2 * (18 + 16))
    assert vs.getpixel((16, 16 + 9)) == (200, 200, 200)  # the band's first row is the baseline
    assert vs.getpixel((16, 34 + 16 + 9)) == (0, 0, 0)  # its second this run's frame 0


def test_compare_counts_the_differing_pixels(tmp_path: Path) -> None:
    a = [write(tmp_path / "a0.png", 10), write(tmp_path / "a1.png", 10)]
    b = [write(tmp_path / "b0.png", 10), tmp_path / "b1.png"]
    img = np.full((36, 64, 3), 10, dtype=np.uint8)
    img[0, :5] = (10, 13, 10)
    Image.fromarray(img).save(b[1])
    assert filmstrip.compare(a, b) == [(0, 0, 0), (1, 5, 3)]


def test_evidence_strips_carry_the_guards_tag_and_stay_small(tmp_path: Path) -> None:
    from demo3d_tools import guard

    big = Image.new("RGB", (5000, 600), (90, 120, 150))
    filmstrip.save_tagged(big, tmp_path / "s.jpg", "test")
    data = (tmp_path / "s.jpg").read_bytes()
    assert guard.jpeg_has_tag(data)
    with Image.open(tmp_path / "s.jpg") as im:
        assert im.width == filmstrip.MAX_EVIDENCE_W
