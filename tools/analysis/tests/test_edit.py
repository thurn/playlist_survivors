import numpy as np
import pytest

from ps_analysis.audio import Onsets
from ps_analysis.edit import Context, EditError, apply_edits


def run(m, *edits, ctx=None):
    out, _ = apply_edits(m, list(edits), ctx)
    return out


def test_shift(grid_map) -> None:
    out = run(grid_map, {"op": "shift", "beats": [2, 3], "byMs": -20})
    assert out["beats"][1] == 1.5
    assert out["beats"][2] == pytest.approx(1.98)
    assert out["beats"][3] == pytest.approx(2.48)
    assert out["beats"][4] == 3.0
    assert grid_map["beats"][2] == 2.0  # the input is not modified


def test_insert_and_delete_beat(grid_map) -> None:
    out = run(grid_map, {"op": "insertBeat", "after": 0})
    assert out["beats"][:3] == [1.0, 1.25, 1.5]
    out = run(out, {"op": "deleteBeat", "index": 1})
    assert out["beats"] == grid_map["beats"]
    out = run(grid_map, {"op": "insertBeat", "after": 15})
    assert out["beats"][-1] == pytest.approx(9.0)
    out = run(grid_map, {"op": "insertBeat", "after": 3, "time": 2.6})
    assert out["beats"][4] == 2.6


def test_indices_follow_earlier_edits(grid_map) -> None:
    # After inserting at 0, old beat 5 is index 6.
    out = run(
        grid_map, {"op": "insertBeat", "after": 0}, {"op": "deleteBeat", "index": 6}
    )
    assert 3.5 not in out["beats"]
    assert len(out["beats"]) == 16


def test_delete_beats_and_move(grid_map) -> None:
    out = run(grid_map, {"op": "deleteBeats", "from": 2, "to": 4})
    assert out["beats"][:3] == [1.0, 1.5, 3.5]
    out = run(grid_map, {"op": "moveBeat", "index": 1, "time": 1.4})
    assert out["beats"][1] == 1.4


def test_respace_keeps_ends(grid_map) -> None:
    m = run(grid_map, {"op": "shift", "beats": [3, 3], "byMs": 70})
    out = run(m, {"op": "respace", "from": 1, "to": 5})
    assert out["beats"][1:6] == pytest.approx([1.5, 2.0, 2.5, 3.0, 3.5])


def test_respace_with_count_changes_beat_count(grid_map) -> None:
    out = run(grid_map, {"op": "respace", "from": 0, "to": 4, "count": 8})
    assert len(out["beats"]) == 20
    assert out["beats"][:9] == pytest.approx([1.0 + 0.25 * k for k in range(9)])
    assert out["beats"][9] == 3.5


def test_subdivide(grid_map) -> None:
    out = run(grid_map, {"op": "subdivide", "from": 0, "to": 2, "parts": 2})
    assert out["beats"][:6] == pytest.approx([1.0, 1.25, 1.5, 1.75, 2.0, 2.5])
    assert len(out["beats"]) == 18


def test_splice_tracker(grid_map) -> None:
    ctx = Context(trackers={"fast": [2.0, 2.25, 2.5, 2.75, 3.0]})
    out = run(
        grid_map,
        {"op": "spliceTracker", "tracker": "fast", "fromTime": 1.9, "toTime": 3.1},
        ctx=ctx,
    )
    assert out["beats"][:8] == [1.0, 1.5, 2.0, 2.25, 2.5, 2.75, 3.0, 3.5]
    with pytest.raises(EditError, match="not available"):
        run(
            grid_map,
            {"op": "spliceTracker", "tracker": "nope", "fromTime": 0, "toTime": 1},
        )


def test_smooth_removes_jitter(grid_map) -> None:
    m = run(grid_map, {"op": "shift", "beats": [5, 5], "byMs": 40})
    out = run(m, {"op": "smooth", "from": 0, "to": 15, "window": 3})
    assert np.max(np.abs(np.array(out["beats"]) - np.array(grid_map["beats"]))) < 0.02
    assert out["beats"][0] == 1.0
    assert out["beats"][15] == grid_map["beats"][15]


def test_extend(grid_map) -> None:
    out = run(grid_map, {"op": "extend", "until": 10.2})
    assert out["beats"][-3:] == pytest.approx([9.0, 9.5, 10.0])


def test_structure_ops(grid_map) -> None:
    out = run(
        grid_map,
        {"op": "setFirstDownbeat", "index": 1},
        {
            "op": "setMeter",
            "meter": [
                {"fromBar": 3, "beatsPerBar": 2},
                {"fromBar": 1, "beatsPerBar": 4},
            ],
        },
        {
            "op": "setSection",
            "label": "b",
            "bars": [3, 4],
            "feel": "pulsed",
            "key": "D major",
        },
        {"op": "setSection", "label": "a", "bars": [1, 2], "feel": "free"},
        {"op": "setPhrases", "phrases": [[3, 4], [1, 2]]},
        {"op": "setTempoChanges", "tempoChanges": [{"bars": [4, 4]}]},
    )
    assert out["firstDownbeatIndex"] == 1
    assert out["meter"][0]["fromBar"] == 1
    assert [s["label"] for s in out["sections"]] == ["a", "b"]
    assert out["sections"][1]["key"] == "D major"
    assert out["phrases"] == [[1, 2], [3, 4]]
    assert out["tempoChanges"] == [{"bars": [4, 4]}]
    out = run(out, {"op": "removeSection", "label": "b"})
    assert [s["label"] for s in out["sections"]] == ["a"]


def _onsets(peaks: list[float]) -> Onsets:
    p = np.array(peaks)
    return Onsets(np.array([0.0]), np.array([0.0]), p, np.ones_like(p), np.ones_like(p))


def test_accents_and_snapping(grid_map) -> None:
    ctx = Context(onsets=_onsets([2.01, 3.0]))
    out = run(
        grid_map,
        {
            "op": "addAccent",
            "time": 2.0,
            "strength": 0.9,
            "label": "cymbal",
            "snapMs": 20,
        },
        {"op": "addAccent", "time": 3.0, "strength": 0.5},
        {"op": "setAccentLabel", "time": 3.01, "label": "tutti-chord"},
        ctx=ctx,
    )
    assert out["accents"] == [
        {"time": 2.01, "strength": 0.9, "label": "cymbal"},
        {"time": 3.0, "strength": 0.5, "label": "tutti-chord"},
    ]
    out = run(out, {"op": "removeAccent", "time": 2.0})
    assert len(out["accents"]) == 1
    assert run(out, {"op": "clearAccents"})["accents"] == []
    with pytest.raises(EditError, match="no onset peak"):
        run(
            grid_map,
            {"op": "addAccent", "time": 2.5, "strength": 1, "snapMs": 20},
            ctx=ctx,
        )
    with pytest.raises(EditError, match="no accent"):
        run(out, {"op": "removeAccent", "time": 7.0})


def test_anchors_only_in_free_sections(grid_map) -> None:
    m = run(
        grid_map, {"op": "setSection", "label": "a", "bars": [1, 4], "feel": "free"}
    )
    out = run(
        m,
        {"op": "addAnchor", "time": 2.2, "strength": 0.7},
        {"op": "addAnchor", "time": 1.2, "strength": 0.5},
    )
    assert [a["time"] for a in out["sections"][0]["anchors"]] == [1.2, 2.2]
    # setSection keeps existing anchors.
    out2 = run(
        out,
        {
            "op": "setSection",
            "label": "a",
            "bars": [1, 4],
            "feel": "free",
            "key": "E major",
        },
    )
    assert len(out2["sections"][0]["anchors"]) == 2
    with pytest.raises(EditError, match="pulsed section"):
        run(m, {"op": "addAnchor", "time": 6.0, "strength": 0.7})
    assert "anchors" not in run(out, {"op": "clearAnchors"})["sections"][0]


def test_errors(grid_map) -> None:
    with pytest.raises(EditError, match="unknown op"):
        run(grid_map, {"op": "explode"})
    with pytest.raises(EditError, match="out of range"):
        run(grid_map, {"op": "deleteBeat", "index": 99})
    with pytest.raises(EditError, match="strictly increasing"):
        run(grid_map, {"op": "moveBeat", "index": 3, "time": 0.5})
    with pytest.raises(EditError, match="bad arguments"):
        run(grid_map, {"op": "shift", "beats": [1, 2]})


def test_reports_beat_changes(grid_map) -> None:
    _, changed = apply_edits(grid_map, [{"op": "setPhrases", "phrases": []}])
    assert not changed
    _, changed = apply_edits(grid_map, [{"op": "shift", "beats": [0, 0], "byMs": 1}])
    assert changed


def test_fit_onsets_moves_beats_onto_peaks_smoothly(grid_map) -> None:
    beats = grid_map["beats"]
    peaks = [t + 0.03 for t in beats]  # every onset 30 ms late: shift the whole grid
    peaks[8] = beats[8] - 0.04  # one stray early peak that would break smoothness
    ctx = Context(onsets=_onsets(sorted(peaks)))
    out = run(
        grid_map, {"op": "fitOnsets", "from": 2, "to": 13, "maxChange": 0.08}, ctx=ctx
    )
    b = np.array(out["beats"])
    assert b[0] == beats[0] and b[15] == beats[15]  # outside the range: fixed
    gaps = np.diff(b)
    assert np.all(np.abs(gaps[1:] / gaps[:-1] - 1) <= 0.08 + 1e-9)
    on_peak = [
        i for i in range(2, 14) if np.min(np.abs(np.array(peaks) - b[i])) <= 0.025
    ]
    assert len(on_peak) >= 10
    with pytest.raises(EditError, match="needs the audio"):
        run(grid_map, {"op": "fitOnsets", "from": 2, "to": 13})
