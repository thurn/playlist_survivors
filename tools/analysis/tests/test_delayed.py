from __future__ import annotations

import itertools
import json
from pathlib import Path
from typing import Any

import pytest

from ps_analysis import delayed as dl
from ps_analysis import stream as st
from ps_analysis.cli import main

PERIOD = 0.5
SECONDS = 40


def synthetic_stream() -> dict[str, Any]:
    """A steady 120 bpm track: Direct's committed beats 60 ms late, the offline decode exact,
    and the causal tracker's believed beats 0.4 periods off (which the phase check corrects)."""
    ref = [round(i * PERIOD, 6) for i in range(int(SECONDS / PERIOD) + 1)]
    beats = [
        {"t": t + 0.06, "knownAt": t + 0.06 - 0.85, "period": PERIOD, "evidence": 1.0}
        for t in ref[2:]
    ]
    hindsight = [
        {
            "knownAt": float(T),
            "offline": [t for t in ref if T - 8 <= t <= T],
            "online": [t + 0.4 * PERIOD for t in ref if T - 8 <= t <= T - 0.3],
        }
        for T in range(1, SECONDS + 1)
    ]
    return {
        "params": st.Params().to_json(),
        "beats": beats,
        "hindsight": hindsight,
        "reference": {"source": "exact", "beats": ref},
    }


def test_offline_grid_commits_the_decoded_beats() -> None:
    doc = synthetic_stream()
    beats = dl.commit_delayed(doc, 3.0, "offline")
    ref = set(doc["reference"]["beats"])
    assert all(b["t"] in ref for b in beats if b["source"] == "offline")
    # (Past the last entry, Direct's late beats continue.)
    assert beats[-1]["source"] == "fallback"
    row = dl.score(beats, doc["reference"]["beats"])
    assert row["within45"] > 0.95
    assert row["recall"] > 0.95
    assert row["fallback"] < 0.1
    # Direct listening's beats are all 60 ms late.
    assert dl.score(doc["beats"], doc["reference"]["beats"])["within45"] == 0.0


def test_phase_check_moves_the_online_grid_onto_the_offline_phase() -> None:
    doc = synthetic_stream()
    ref = doc["reference"]["beats"]
    assert dl.score(dl.commit_delayed(doc, 3.0, "online"), ref)["within45"] == 0.0
    assert dl.score(dl.commit_delayed(doc, 3.0, "phased"), ref)["within45"] > 0.9


def test_short_delays_fall_back_to_direct_forecasts() -> None:
    """At 1.5 s the bands lie past each entry's window, so Direct's beats fill them."""
    doc = synthetic_stream()
    row = dl.score(dl.commit_delayed(doc, 1.5, "offline"), doc["reference"]["beats"])
    assert row["fallback"] > 0.9
    assert row["within45"] < 0.1


def test_a_gap_inside_the_decoded_window_falls_back() -> None:
    """A stretch with no decoded beat, with beats after it in the same entry, still gets
    Direct's beats: here every entry misses the beats from 1.6 s to 0.45 s before its end,
    which is where each band lies at 3 s."""
    doc = synthetic_stream()
    for e in doc["hindsight"]:
        T = e["knownAt"]
        e["offline"] = [t for t in e["offline"] if not T - 1.6 <= t < T - 0.45]
    beats = [b for b in dl.commit_delayed(doc, 3.0, "offline") if 10 <= b["t"] < 35]
    assert len(beats) == 50
    assert all(b["source"] == "fallback" for b in beats)


def test_an_entry_with_one_beat_falls_back_at_directs_period() -> None:
    doc = synthetic_stream()
    for e in doc["hindsight"]:
        if 15 <= e["knownAt"] <= 18:
            e["offline"] = e["offline"][-1:]
    beats = dl.commit_delayed(doc, 3.0, "offline")
    stamps = {round(e["knownAt"] + dl.D_S - 3.0 + dl.A_S, 6) for e in doc["hindsight"]}
    assert any(b["source"] == "fallback" for b in beats if 14.0 <= b["t"] < 17.0)
    for b in beats:
        assert b["t"] - b["knownAt"] >= b["period"] + 0.35 - 1e-9
        if b["source"] == "offline":
            assert round(b["knownAt"], 6) in stamps


def test_params_come_from_the_stream() -> None:
    doc = synthetic_stream()
    doc["params"]["commit"]["extraS"] = 0.5
    doc["params"]["phase"]["persist"] = 3
    p = dl.params_of(doc)
    assert (p.commit_extra_s, p.phase_persist) == (0.5, 3)
    for b in dl.commit_delayed(doc, 3.0, "offline"):
        assert b["t"] - b["knownAt"] >= b["period"] + 0.5 - 1e-9


@pytest.mark.parametrize("grid", dl.GRIDS)
@pytest.mark.parametrize("m", dl.MS)
def test_every_beat_is_known_a_horizon_ahead_and_in_order(grid: str, m: float) -> None:
    beats = dl.commit_delayed(synthetic_stream(), m, grid)
    assert beats
    for b in beats:
        assert b["t"] - b["knownAt"] >= b["period"] + 0.35 - 1e-9
    times = [b["t"] for b in beats]
    assert all(y - x >= 0.5 * PERIOD - 1e-9 for x, y in itertools.pairwise(times))


def test_proposal_takes_the_smallest_m_within_a_point_and_no_worse_category() -> None:
    def cell(acc: float) -> dict[str, Any]:
        return {"within45": acc, "recall": acc, "fallback": 0.0}

    def group(direct: float, online: float, offline: list[float]) -> dict[str, Any]:
        return {
            "tracks": 1,
            "direct": {"within45": direct, "recall": direct},
            "online": {str(m): cell(online) for m in dl.MS},
            "offline": {str(m): cell(a) for m, a in zip(dl.MS, offline, strict=True)},
            "phased": {str(m): cell(0.5) for m in dl.MS},
        }

    summary = {
        "aggregate": group(0.80, 0.95, [0.80, 0.84, 0.878, 0.88, 0.885, 0.886]),
        "categories": {
            # The online grid is better in aggregate but worse than Direct here.
            "a": group(0.90, 0.85, [0.90, 0.91, 0.92, 0.93, 0.93, 0.93]),
        },
    }
    proposal = dl.propose(summary)
    assert proposal["choice"]["grid"] == "offline"
    # 0.878 is within a point of the best (0.886); 0.84 is not.
    assert proposal["choice"]["m"] == 2.5
    assert proposal["pays"]
    online = next(o for o in proposal["options"] if o["grid"] == "online")
    assert online["worseCategories"] == ["a"]


def test_command_scores_streams_and_writes_the_tables(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    path = tmp_path / "steady" / "stream.json"
    path.parent.mkdir()
    path.write_text(json.dumps(synthetic_stream()))
    index = tmp_path / "index.json"
    index.write_text(json.dumps({"tracks": [{"id": "steady", "group": "synthetic"}]}))
    md = tmp_path / "payoff.md"
    with pytest.raises(SystemExit) as done:
        main(
            ["delayed-payoff", str(path), "--index", str(index), "--markdown", str(md)]
        )
    assert done.value.code == 0
    out = json.loads(capsys.readouterr().out)
    assert out["tracks"] == 1
    assert out["proposal"]["pays"]
    assert out["proposal"]["choice"]["grid"] == "offline"
    assert "| synthetic | 1 | 0.0 |" in md.read_text()
    assert "| exact | 1 | 0.0 |" in md.read_text()
