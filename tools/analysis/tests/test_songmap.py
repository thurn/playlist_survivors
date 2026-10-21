import pytest
from conftest import make_map

from ps_analysis import songmap as sm


def test_example_conversions_from_design() -> None:
    # The worked example in docs/design/tech-demo.md, "Musical positions".
    beats = [0.41, 1.52, 2.60, 3.71]
    assert sm.beat_index_at(beats, 2.0) == pytest.approx(1.4444, abs=1e-4)
    assert sm.time_of_beat_index(beats, 2.5) == pytest.approx(3.155)


def test_extrapolation_before_and_after() -> None:
    beats = [1.0, 1.5, 2.0]
    assert sm.beat_index_at(beats, 0.0) == pytest.approx(-2.0)
    assert sm.beat_index_at(beats, 3.0) == pytest.approx(4.0)
    assert sm.time_of_beat_index(beats, -1) == pytest.approx(0.5)
    assert sm.time_of_beat_index(beats, 3) == pytest.approx(2.5)


def test_index_song_with_meter_change_and_pickup() -> None:
    # One pickup beat, then two 4/4 bars, then 2/4 from bar 3.
    beats = [float(i) for i in range(14)]
    m = make_map(
        beats,
        meter=[{"fromBar": 1, "beatsPerBar": 4}, {"fromBar": 3, "beatsPerBar": 2}],
        first_downbeat=1,
    )
    idx = sm.index_song(m)
    assert idx.bar_start[1:] == [1, 5, 9, 11, 13]
    assert idx.beats_per_bar[1:] == [4, 4, 2, 2, 2]
    assert idx.bar_count == 5
    assert not idx.last_bar_complete()  # bar 5 has only beat 13
    assert sm.bar_of_beat_index(idx, 0) == 0
    assert sm.bar_of_beat_index(idx, 1) == 1
    assert sm.bar_of_beat_index(idx, 8.9) == 2
    assert sm.bar_of_beat_index(idx, 9) == 3
    assert sm.position_of_beat(idx, 10) == (3, 2)
    assert sm.position_of_beat(idx, 0) == (0, 4)
    assert sm.time_of(idx, 3, 1) == 9.0
    assert sm.time_of(idx, 2, 2.5) == 6.5
    # Beyond the last bar: extrapolate with the last bar's meter.
    assert sm.beat_index_of(idx, 7, 1) == 13 + 2 * 2
    # Before bar 1: extrapolate with bar 1's meter.
    assert sm.beat_index_of(idx, 0, 1) == 1 - 4


def test_section_lookup(grid_map) -> None:
    idx = sm.index_song(grid_map)
    assert sm.section_times(idx, grid_map["sections"][1]) == (5.0, 9.0)
    assert sm.section_at_time(idx, 4.99)["label"] == "a"
    assert sm.section_at_time(idx, 5.0)["label"] == "b"
    assert sm.section_at_time(idx, 0.0)["label"] == "a"
    assert sm.section_at_time(idx, 50.0)["label"] == "b"
    assert idx.beat_range(5, 8) == (8, 16)


def test_save_roundtrip(tmp_path, grid_map) -> None:
    grid_map["accents"] = [{"time": 2.123456, "strength": 0.5, "label": "tutti-chord"}]
    p = tmp_path / "m.json"
    sm.save(grid_map, p)
    back = sm.load(p)
    assert back["beats"] == grid_map["beats"]
    assert back["accents"][0]["time"] == 2.1235
    assert back["sections"] == grid_map["sections"]
