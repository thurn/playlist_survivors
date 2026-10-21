import copy

import numpy as np
from conftest import make_map

from ps_analysis.check import check_map, format_text


def peaks_at(beats, offset=0.0):
    return np.array(beats) + offset


def test_clean_grid_passes(grid_map) -> None:
    res = check_map(grid_map, peaks_at(grid_map["beats"]), duration=100.0)
    assert res.ok, res.errors
    st = res.sections[0]
    assert st["onsetAlignment"]["fraction"] == 1.0
    assert st["smoothness"]["maxChange"] == 0.0
    assert st["bpm"] == 120.0
    assert "check: OK" in format_text(res)


def test_onset_alignment_threshold(grid_map) -> None:
    peaks = peaks_at(grid_map["beats"])
    # Section a has 8 beats; move 1 peak 40 ms away: 7/8 = 87.5% < 90%.
    peaks[2] += 0.040
    res = check_map(grid_map, peaks, 100.0)
    assert not res.ok
    assert any(
        "section a: onset alignment 87.5%" in e and "beat 2 (bar 2 beat 1" in e
        for e in res.errors
    )
    # A peak more than 80 ms away does not count at all.
    peaks[2] += 0.100
    res = check_map(grid_map, peaks, 100.0)
    assert res.ok, res.errors
    assert res.sections[0]["onsetAlignment"]["counted"] == 7


def test_smoothness_and_tempo_change(grid_map) -> None:
    m = copy.deepcopy(grid_map)
    m["beats"][10] += 0.05  # gaps 0.55 then 0.45: +10% then -18%
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert not res.ok
    assert any(
        "section b: beat 9 (bar 5 beat 2" in e and "+10.0%" in e for e in res.errors
    )
    assert any(
        "section b: beat 10 (bar 6 beat 1" in e and "-18.2%" in e for e in res.errors
    )
    m["tempoChanges"] = [{"bars": [6, 6]}]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert res.ok, res.errors


def test_free_sections_skip_beat_checks_but_need_anchors() -> None:
    beats = [0.5 + i for i in range(20)]  # 10 bars of 2/4, 1 s per beat
    beats[3] += 0.3
    m = make_map(
        beats,
        sections=[
            {
                "label": "prelude",
                "bars": [1, 6],
                "feel": "free",
                "anchors": [{"time": 4.0, "strength": 0.5}],
            },
            {"label": "rest", "bars": [7, 10], "feel": "pulsed"},
        ],
    )
    peaks = np.array([*beats[12:], 4.01, 9.0])
    res = check_map(m, peaks, 100.0)
    # Prelude spans 0.5..12.5 s; the anchors at 4.0 leave 8.5 s up to the end.
    assert any("section prelude: 8.50 s without an anchor" in e for e in res.errors)
    m["sections"][0]["anchors"].append({"time": 9.0, "strength": 0.5})
    res = check_map(m, peaks, 100.0)
    assert res.ok, res.errors
    m["sections"][0]["anchors"].append({"time": 10.0, "strength": 0.5})
    res = check_map(m, peaks, 100.0)
    assert any(
        "anchor at 10.000 s is 1000 ms from the nearest onset peak" in e
        for e in res.errors
    )


def test_anchor_in_pulsed_section_is_an_error(grid_map) -> None:
    grid_map["sections"][1]["anchors"] = [{"time": 6.0, "strength": 1}]
    res = check_map(grid_map, peaks_at(grid_map["beats"]), 100.0)
    assert any("anchors are only allowed in free sections" in e for e in res.errors)


def test_accents_must_sit_on_peaks(grid_map) -> None:
    grid_map["accents"] = [
        {"time": 2.0, "strength": 1.0, "label": "tutti-chord"},
        {"time": 3.03, "strength": 0.5},
    ]
    res = check_map(grid_map, peaks_at(grid_map["beats"]), 100.0)
    assert [e for e in res.errors if "accent" in e] == [
        "accent at 3.030 s (unlabelled) is 30 ms from the nearest onset peak (limit 20 ms)"
    ]
    assert res.accents["labels"] == {"tutti-chord": 1}


def test_section_cover_gaps_and_overlaps(grid_map) -> None:
    m = copy.deepcopy(grid_map)
    m["sections"][1]["bars"] = [6, 8]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert any(
        "section b: starts at bar 6, expected bar 5 (gap)" in e for e in res.errors
    )
    m["sections"][1]["bars"] = [4, 8]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert any("(overlap)" in e for e in res.errors)
    m["sections"][1]["bars"] = [5, 7]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert any("sections end at bar 7 but the song has 8 bars" in e for e in res.errors)


def test_structural_errors(grid_map) -> None:
    m = copy.deepcopy(grid_map)
    m["beats"][5] = m["beats"][4]
    m["energy"] = m["energy"][:-1]
    res = check_map(m, peaks_at(grid_map["beats"]), 100.0)
    assert any("not strictly increasing at beat 5" in e for e in res.errors)
    assert any("energy has 15 values but there are 16 beats" in e for e in res.errors)

    m = copy.deepcopy(grid_map)
    m["phrases"] = [[1, 2], [3, 6]]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert res.errors == ["phrase [3, 6] is not inside a single section"]

    m = copy.deepcopy(grid_map)
    m["phrases"] = [[1, 3], [3, 4]]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert any("overlaps" in e for e in res.errors)

    m = copy.deepcopy(grid_map)
    m["version"] = 2
    del m["accents"]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert "version must be 1, got 2" in res.errors
    assert "missing field 'accents'" in res.errors

    m = copy.deepcopy(grid_map)
    m["beats"].append(101.0)
    m["energy"].append(0.5)
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert any("after the end of the file" in e for e in res.errors)


def test_incomplete_last_bar_warns(grid_map) -> None:
    m = copy.deepcopy(grid_map)
    m["beats"] = m["beats"][:-1]
    m["energy"] = m["energy"][:-1]
    res = check_map(m, peaks_at(m["beats"]), 100.0)
    assert res.ok
    assert any("last bar 8 is incomplete" in w for w in res.warnings)
