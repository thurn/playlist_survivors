from __future__ import annotations

import itertools
import json
import math
from pathlib import Path
from typing import Any

import numpy as np
import pytest

from ps_analysis import stream as st
from ps_analysis import synth

EXCERPT_S = 12.0
MARGIN_S = 0.05


@pytest.fixture(scope="module")
def excerpt() -> np.ndarray:
    """12 s of pop-arrangement's chorus into its pads-only breakdown, so the tracker loses the beat."""
    y, ref = synth.track("pop-arrangement")
    start = next(s["s1"] for s in ref["sections"] if s["label"] == "chorus") - 6.0
    i = round(start * synth.SR)
    return y[i : i + int(EXCERPT_S * synth.SR)]


@pytest.fixture(scope="module")
def full(excerpt: np.ndarray) -> dict[str, list[dict[str, Any]]]:
    return st.build_stream(excerpt)


def known_by(
    entries: list[dict[str, Any]], x: float, margin: float = MARGIN_S
) -> list[dict[str, Any]]:
    return [
        e
        for e in entries
        if (e["knownAt"] if "knownAt" in e else e["t"]) <= x - margin + 1e-9
    ]


def assert_same(a: dict[str, Any], b: dict[str, Any], what: str) -> None:
    assert a.keys() == b.keys(), what
    for k, v in a.items():
        if isinstance(v, bool):
            assert v == b[k], f"{what}.{k}"
        elif isinstance(v, list):
            assert len(v) == len(b[k]), f"{what}.{k}: {len(v)} vs {len(b[k])} entries"
            for x, y in zip(v, b[k], strict=True):
                assert math.isclose(x, y, abs_tol=1e-6), f"{what}.{k}: {x} vs {y}"
        else:
            # 1 µs for times, 1e-6 for values.
            assert math.isclose(v, b[k], abs_tol=1e-6), f"{what}.{k}: {v} vs {b[k]}"


@pytest.mark.parametrize(
    ("cut", "margin"),
    [(3.0, MARGIN_S), (6.37, MARGIN_S), (8.5, MARGIN_S), (10.91, MARGIN_S)]
    # Cuts between frames with no margin: an entry stamped even a little
    # before the audio it needs would differ here.
    + [(4.123457, 0.0), (7.777731, 0.0), (9.005013, 0.0), (5.0, 0.0)],
)
def test_stream_is_causal(
    excerpt: np.ndarray, full: dict[str, Any], cut: float, margin: float
) -> None:
    """The stream of the audio cut at X holds exactly the full stream's entries known by X - margin."""
    part = st.build_stream(excerpt[: round(cut * synth.SR)])
    for field in ("beats", "ticks", "onsets", "hindsight"):
        want = known_by(full[field], cut, margin)
        got = known_by(part[field], cut, margin)
        assert len(got) == len(want), f"{field} at {cut}: {len(got)} vs {len(want)}"
        for i, (g, w) in enumerate(zip(got, want, strict=True)):
            assert_same(g, w, f"{field}[{i}] at {cut}")


def test_onsets_are_stamped_at_their_detection_frame(full: dict[str, Any]) -> None:
    p = st.Params()
    for o in full["onsets"]:
        assert o["knownAt"] * st.FPS == pytest.approx(round(o["knownAt"] * st.FPS))
        assert o["t"] == pytest.approx(o["knownAt"] + p.onset_bias_s)


def test_beat_evidence_is_the_latest_tick_at_its_commit(full: dict[str, Any]) -> None:
    ticks = {round(k["t"] * st.FPS): k["evidence"] for k in full["ticks"]}
    for b in full["beats"]:
        frame = round(b["knownAt"] * st.FPS)
        tick = frame - frame % st.TICK_FRAMES
        assert b["evidence"] == ticks.get(tick, 0.0)


def test_hindsight_entries_hold_the_last_8_s_of_both_decodes(
    full: dict[str, Any],
) -> None:
    h = full["hindsight"]
    assert len(h) == int(EXCERPT_S)
    assert any(e["offline"] for e in h) and any(e["online"] for e in h)
    for e in h:
        for kind in ("offline", "online"):
            assert all(
                e["knownAt"] - 8 - 1e-9 <= b <= e["knownAt"] + 1e-9 for b in e[kind]
            )
            assert e[kind] == sorted(e[kind])


def test_excerpt_exercises_every_field(full: dict[str, Any]) -> None:
    """The causality test is not vacuous: beats, strong onsets and both sides of the gate occur."""
    assert len(full["beats"]) > 15
    assert any(o["strong"] for o in full["onsets"])
    evidence = [k["evidence"] for k in full["ticks"]]
    assert max(evidence) > 0.2 and min(evidence[60:]) < 0.2
    assert len({round(k["energy"], 2) for k in full["ticks"]}) > 10


def beliefs(since: list[float], period: list[float]) -> st.Beliefs:
    return st.Beliefs(np.array(since), np.array(period))


def steady(period: float, phase: float, frames: int) -> st.Beliefs:
    """A tracker that believes in beats at phase + k * period from the first frame."""
    since = [((n / st.FPS) - phase) % period for n in range(frames)]
    return beliefs(since, [period] * frames)


NO_BIAS = st.Params(bias_s=0.0)


def test_commit_rule_commits_each_beat_once_within_the_horizon() -> None:
    b = steady(0.5, 0.25, 600)
    beats = st.commit_beats(b, lambda n: 0.7, NO_BIAS)
    times = [x["t"] for x in beats]
    assert times == pytest.approx([0.25 + 0.5 * k for k in range(len(times))])
    for x in beats:
        lead = x["t"] - x["knownAt"]
        assert lead <= 0.5 + 0.35 + 1e-9
        if x["knownAt"] > 0:
            assert lead > 0.5 + 0.35 - 0.01 - 1e-9
        assert x["period"] == 0.5 and x["evidence"] == 0.7


def test_commit_rule_uses_the_bias_and_horizon_parameters() -> None:
    b = steady(0.5, 0.25, 400)
    p = st.Params(bias_s=-0.01, commit_periods=2.0, commit_extra_s=0.35)
    beats = st.commit_beats(b, lambda n: 1.0, p)
    later = [x for x in beats if x["knownAt"] > 0]
    assert all(
        abs((x["t"] + 0.01 - 0.25) / 0.5 - round((x["t"] + 0.01 - 0.25) / 0.5)) < 1e-9
        for x in beats
    )
    assert all(x["t"] - x["knownAt"] == pytest.approx(1.35, abs=0.011) for x in later)


def test_committed_beats_are_never_revised_and_skip_late_ones() -> None:
    """A sudden phase jump never moves a committed beat; a beat it puts < 0.1 s ahead is skipped."""
    frames = 400
    first = steady(0.5, 0.0, 200)
    # From frame 200 (2.0 s) the tracker believes beats sit at 0.2 + k * 0.5.
    second = steady(0.5, 0.2, frames)
    b = beliefs(list(first.since) + list(second.since[200:]), [0.5] * frames)
    beats = st.commit_beats(b, lambda n: 1.0, NO_BIAS)
    times = [x["t"] for x in beats]
    assert all(b2 > a for a, b2 in itertools.pairwise(times))
    before = [x["t"] for x in beats if x["knownAt"] < 2.0]
    assert before == pytest.approx([0.5 * k for k in range(1, 6)])
    after = [x for x in beats if x["knownAt"] >= 2.0]
    # The first new beat is at least half a period after the last committed (2.5)
    # and at least 0.1 s ahead of its commit time.
    assert after[0]["t"] == pytest.approx(3.2)
    assert all(x["t"] - x["knownAt"] >= 0.1 - 1e-9 for x in beats)


def test_commit_rule_skips_a_beat_less_than_min_lead_ahead() -> None:
    # Until 1.0 s the next believed beat (2.0 s) lies beyond the 0.3 s horizon;
    # at 1.0 s the belief jumps to beats at 0.05 + k * 0.5, and 1.05 is too close. (2.05
    # is committed when 1.55 arrives, inside its horizon or not.)
    since = [n / st.FPS for n in range(100)] + [
        ((n / st.FPS) - 0.05) % 0.5 for n in range(100, 200)
    ]
    period = [2.0] * 100 + [0.5] * 100
    p = st.Params(bias_s=0.0, commit_periods=0.0, commit_extra_s=0.3)
    beats = st.commit_beats(beliefs(since, period), lambda n: 1.0, p)
    assert [(round(x["t"], 6), x["knownAt"]) for x in beats][:2] == [
        (1.55, 1.25),
        (2.05, 1.55),
    ]


def test_smoothed_period_averages_alternating_whole_frames_and_follows_jumps() -> None:
    raw = np.array([0.34, 0.35] * 150 + [0.5] * 100)
    out = st.smooth_periods(raw, st.Params())
    assert out[299] == pytest.approx(0.345)
    assert out[300] == 0.5


def tone(seconds: float, amp: float) -> np.ndarray:
    t = np.arange(int(seconds * st.SR)) / st.SR
    return (amp * np.sin(2 * np.pi * 220 * t)).astype(np.float32)


def test_energy_is_relative_to_a_slowly_falling_running_maximum() -> None:
    y = np.concatenate(
        [np.zeros(st.SR, dtype=np.float32), tone(5, 0.5), tone(20, 0.05)]
    )
    times = np.arange(1, int(len(y) / st.SR / 0.05) + 1) * 0.05
    e = st.energies(y, times, st.Params())
    at = lambda s: e[round(s / 0.05) - 1]
    assert at(0.5) == 0.0  # leading silence: the running maximum's floor
    assert at(5.5) == pytest.approx(1.0)
    # 20 dB quieter: (-20 + 30) / 30 at first, rising as the maximum falls 0.2 dB/s.
    assert at(7.0) == pytest.approx(10 / 30 + 0.2 * 0.5 / 30, abs=0.01)
    assert at(16.5) == pytest.approx(10 / 30 + 0.2 * 10 / 30, abs=0.01)
    assert at(26.0) > at(16.5)


def test_energy_of_a_loud_passage_stays_high() -> None:
    y = np.concatenate([tone(2, 0.05), tone(40, 0.5)])
    times = np.arange(1, int(len(y) / st.SR / 0.05) + 1) * 0.05
    e = st.energies(y, times, st.Params())
    assert min(e[int(3 / 0.05) :]) > 0.99


def test_evidence_is_the_mean_peak_activation_of_beats_heard_in_full() -> None:
    act = np.zeros(300)
    act[[100, 150, 200]] = [0.9, 0.6, 0.3]
    p = st.Params()
    # Last beat at 2.0 s, period 0.5: beats 2.0, 1.5, 1.0 and 0.5 (silent).
    assert st.evidence_at(act, 2.0, 0.5, 2.03, p) == pytest.approx(
        (0.3 + 0.6 + 0.9 + 0.0) / 4
    )
    # At 2.02 s the beat at 2.0 is not yet heard in full: 0.0 s takes its place.
    assert st.evidence_at(act, 2.0, 0.5, 2.02, p) == pytest.approx(
        (0.6 + 0.9 + 0.0 + 0.0) / 4
    )
    # Only beats from 0 s on count.
    assert st.evidence_at(act, 1.0, 0.5, 1.1, p) == pytest.approx((0.9 + 0.0 + 0.0) / 3)
    assert st.evidence_at(act, 0.0, 0.5, 0.01, p) == 0.0


@pytest.mark.parametrize("name", synth.TRACKS)
def test_synth_tracks_are_deterministic_with_exact_references(name: str) -> None:
    y, ref = synth.track(name)
    y2, ref2 = synth.track(name)
    assert np.array_equal(y, y2) and ref == ref2
    assert y.dtype == np.float32 and np.max(np.abs(y)) == pytest.approx(0.8, abs=1e-6)
    beats = ref["beats"]
    assert ref["source"] == "exact"
    assert all(b > a for a, b in itertools.pairwise(beats))
    secs = ref["sections"]
    assert secs[0]["s0"] == beats[0]
    assert all(a["s1"] == pytest.approx(b["s0"]) for a, b in itertools.pairwise(secs))
    assert len(y) / synth.SR > secs[-1]["s1"]


def test_synth_beat_timeline_has_exact_tempi() -> None:
    _, ref = synth.track("pop-arrangement")
    gaps = np.diff(ref["beats"])
    assert gaps[:255] == pytest.approx(0.5)
    assert gaps[256:] == pytest.approx(0.625)
    _, ref = synth.track("accelerando-100-140")
    gaps = np.diff(ref["beats"])
    assert gaps[0] == pytest.approx(0.6) and gaps[-1] == pytest.approx(60 / 140)


def test_causality_mix_has_every_ingredient() -> None:
    y, ref = synth.track("causality-mix")
    labels = [s["label"] for s in ref["sections"]]
    assert labels == [
        "steady-120",
        "quiet",
        "entrance",
        "cut-to-150",
        "pads-only",
        "drums",
        "final-chord",
    ]
    sec = {s["label"]: s for s in ref["sections"]}

    def db(a: float, b: float) -> float:
        seg = y[int(a * synth.SR) : int(b * synth.SR)].astype(np.float64)
        return 10 * np.log10(np.mean(seg * seg) + 1e-12)

    q, e = sec["quiet"], sec["entrance"]
    assert db(e["s0"], e["s0"] + 2) - db(q["s0"] + 1, q["s1"]) > 12
    end = sec["final-chord"]["s0"]
    assert db(len(y) / synth.SR - 1, len(y) / synth.SR) < db(end, end + 1) - 40
    assert 40 < len(y) / synth.SR < 50


def test_file_format(tmp_path: Path, excerpt: np.ndarray, full: dict[str, Any]) -> None:
    audio = tmp_path / "audio" / "x.ogg"
    audio.parent.mkdir()
    audio.write_bytes(b"")
    out = tmp_path / "reactive" / "x" / "stream.json"
    ref = {
        "source": "exact",
        "beats": [0.5, 1.0],
        "sections": [{"label": "all", "s0": 0.5, "s1": 1.5}],
    }
    doc = st.to_document(
        full, st.relative_audio(audio, out), EXCERPT_S, 48000, st.Params(), ref
    )
    st.write_document(doc, out)
    back = json.loads(out.read_text())
    assert back == doc
    assert list(back) == [
        "version",
        "audio",
        "params",
        "beats",
        "ticks",
        "onsets",
        "hindsight",
        "reference",
    ]
    assert back["version"] == 1
    assert back["audio"] == {
        "file": "../../audio/x.ogg",
        "duration": EXCERPT_S,
        "sampleRate": 48000,
    }
    params = back["params"]
    assert params["toolVersion"] == st.TOOL_VERSION
    assert params["commit"] == {"periods": 1.0, "extraS": 0.35, "minLeadS": 0.1}
    assert (
        params["gate"] == 0.2 and params["tickS"] == 0.05 and params["biasS"] == -0.013
    )
    assert set(back["beats"][0]) == {"t", "knownAt", "period", "evidence"}
    assert set(back["ticks"][0]) == {"t", "evidence", "energy"}
    assert set(back["onsets"][0]) == {"t", "knownAt", "strength", "strong"}
    beats = [b["t"] for b in back["beats"]]
    assert all(b > a for a, b in itertools.pairwise(beats))
    assert all(b["knownAt"] < b["t"] for b in back["beats"])
    assert [k["t"] for k in back["ticks"][:3]] == [0.05, 0.1, 0.15]
    assert back["reference"] == ref
    assert "structure" not in back
    h = back["hindsight"]
    assert [e["knownAt"] for e in h] == [float(k) for k in range(1, len(h) + 1)]
    assert set(h[0]) == {"knownAt", "offline", "online"}
    assert params["hindsight"] == {
        "everyS": 1.0,
        "windowS": 20.0,
        "keepS": 8.0,
        "biasS": 0.0,
    }
    # One entry per line keeps regenerated streams diffable.
    assert len(out.read_text().splitlines()) > len(beats) + len(back["ticks"])


def test_document_without_reference_omits_it(full: dict[str, Any]) -> None:
    doc = st.to_document(full, "a.ogg", EXCERPT_S, 44100, st.Params(), None)
    assert "reference" not in doc


def test_accuracy_counts_only_beats_within_the_reference_span() -> None:
    ref = [1.0, 1.5, 2.0, 2.5]
    acc = st.accuracy([0.2, 1.01, 1.75, 2.49, 9.0], ref)
    assert acc["beats"] == 3
    assert acc["within20"] == pytest.approx(2 / 3, abs=1e-4)
    assert acc["offbeat45"] == pytest.approx(1 / 3, abs=1e-4)


def test_run_can_play_the_analysed_samples(tmp_path: Path) -> None:
    """reactive:add's path: a 48 kHz file is analysed at 44.1 kHz, and those samples play."""
    import librosa
    import soundfile as sf

    y, _ = synth.track("house-128")
    src = tmp_path / "in.wav"
    sf.write(
        src,
        librosa.resample(y[: 6 * synth.SR], orig_sr=synth.SR, target_sr=48000),
        48000,
    )
    out = tmp_path / "local" / "x" / "stream.json"
    flac = tmp_path / "local" / "x" / "audio.flac"
    st.run(st.StreamJob(src, out, None, False, flac))
    doc = json.loads(out.read_text())
    assert doc["audio"]["file"] == "audio.flac"
    assert doc["audio"]["sampleRate"] == 44100
    played, sr = sf.read(flac)
    assert sr == 44100
    assert len(played) / sr == pytest.approx(doc["audio"]["duration"], abs=1e-6)


def test_the_next_beat_is_committed_by_the_time_a_beat_arrives() -> None:
    """Even when the horizon is shorter than a period, the next beat is known at the latest at the beat before it."""
    b = steady(0.5, 0.25, 600)
    beats = st.commit_beats(
        b, lambda n: 1.0, st.Params(bias_s=0.0, commit_periods=0.0, commit_extra_s=0.12)
    )
    assert len(beats) > 8
    for a, nxt in itertools.pairwise(beats):
        assert nxt["knownAt"] <= a["t"] + 1e-9


def test_the_excerpt_commits_each_next_beat_by_the_beat_before(
    full: dict[str, Any],
) -> None:
    beats = full["beats"]
    for a, nxt in itertools.pairwise(beats):
        assert nxt["knownAt"] <= a["t"] + 1e-9
