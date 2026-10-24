from __future__ import annotations

import itertools
import json
import math
from pathlib import Path
from typing import Any

import numpy as np
import pytest

from ps_analysis import accents as acc
from ps_analysis import stream as st
from ps_analysis import structure as stc
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
        if isinstance(v, (bool, str)):
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
    for field in (
        "beats",
        "ticks",
        "onsets",
        "hindsight",
        "forecasts",
        "changes",
        "jumps",
        "ends",
    ):
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
        "structure",
        "accents",
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
    assert set(back["ticks"][0]) == {"t", "evidence", "energy", "level"}
    assert set(back["ticks"][-1]) == {"t", "evidence", "energy", "level", "key"}
    assert set(back["onsets"][0]) == {"t", "knownAt", "strength", "strong"}
    beats = [b["t"] for b in back["beats"]]
    assert all(b > a for a, b in itertools.pairwise(beats))
    assert all(b["knownAt"] < b["t"] for b in back["beats"])
    assert [k["t"] for k in back["ticks"][:3]] == [0.05, 0.1, 0.15]
    assert back["reference"] == ref
    assert set(back["structure"]) == {"changes", "jumps", "ends"}
    assert set(back["accents"]) == {"forecast"}
    assert params["accents"] == {
        "cycles": [2, 4, 8],
        "confirm": 3,
        "windowS": 0.05,
        "minLeadS": 0.54,
        "history": 4,
    }
    assert set(params["structure"]) == {"change", "jump", "level", "key", "end"}
    assert params["structure"]["change"]["k"] == 0.5
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


# --- structure (stage 4) ---------------------------------------------------------

HOP = st.SR * st.TICK_FRAMES // st.FPS


@pytest.fixture(scope="module")
def mix() -> np.ndarray:
    return synth.track("causality-mix")[0]


def form(y: np.ndarray, p: st.Params | None = None) -> dict[str, list[dict[str, Any]]]:
    """The structure entries of the audio, with each tick's level and key as entries known at the tick."""
    p = p or st.Params()
    n = math.floor(len(y) / st.SR * st.FPS / st.TICK_FRAMES + 1e-9)
    times = np.arange(1, n + 1) * st.TICK_FRAMES / st.FPS
    f = stc.structure(y, times, st.energies(y, times, p), p, HOP, st.SR)
    ticks = [
        {"t": float(t), "level": lv, **({"key": k} if k else {})}
        for t, lv, k in zip(times, f["levels"], f["keys"], strict=True)
    ]
    return {
        "changes": f["changes"],
        "jumps": f["jumps"],
        "ends": f["ends"],
        "ticks": ticks,
    }


@pytest.fixture(scope="module")
def mix_form(mix: np.ndarray) -> dict[str, list[dict[str, Any]]]:
    return form(mix)


@pytest.mark.parametrize(
    ("cut", "margin"),
    [
        (18.0, MARGIN_S),
        (22.63, MARGIN_S),
        (31.1, MARGIN_S),
        (41.12, MARGIN_S),
        (44.1, MARGIN_S),
    ]
    + [(17.987654, 0.0), (24.811111, 0.0), (43.2, 0.0)],
)
def test_structure_is_causal(
    mix: np.ndarray, mix_form: dict[str, Any], cut: float, margin: float
) -> None:
    """On causality-mix, whose every structure field fires: the cut audio's entries equal the full's known by X."""
    part = form(mix[: round(cut * synth.SR)])
    for field in ("changes", "jumps", "ends", "ticks"):
        want = known_by(mix_form[field], cut, margin)
        got = known_by(part[field], cut, margin)
        assert len(got) == len(want), f"{field} at {cut}: {len(got)} vs {len(want)}"
        for i, (g, w) in enumerate(zip(got, want, strict=True)):
            assert_same(g, w, f"{field}[{i}] at {cut}")


def test_structure_is_causal_at_every_entry_it_knows(
    mix: np.ndarray, mix_form: dict[str, Any]
) -> None:
    """Cut exactly at each entry's knownAt, with no margin: an entry stamped even a tick before
    the audio it needs is missing or different in the cut audio's structure."""
    cuts = sorted(
        {e["knownAt"] for k in ("changes", "jumps", "ends") for e in mix_form[k]}
    )
    assert len(cuts) >= 8
    for cut in cuts:
        part = form(mix[: round(cut * synth.SR)])
        for field in ("changes", "jumps", "ends", "ticks"):
            want = known_by(mix_form[field], cut, 0.0)
            got = known_by(part[field], cut, 0.0)
            assert len(got) == len(want), f"{field} at {cut}: {len(got)} vs {len(want)}"
            for i, (g, w) in enumerate(zip(got, want, strict=True)):
                assert_same(g, w, f"{field}[{i}] at {cut}")


@pytest.mark.parametrize("cut", [17.987654, 24.811111, 31.1, 43.2])
def test_feature_frames_of_cut_audio_are_a_prefix(mix: np.ndarray, cut: float) -> None:
    """Each tick's features read only the audio before the tick."""
    n = math.floor(len(mix) / st.SR * st.FPS / st.TICK_FRAMES + 1e-9)
    m = math.floor(cut * st.FPS / st.TICK_FRAMES + 1e-9)
    full = stc.features(mix, n, HOP, st.SR)
    part = stc.features(mix[: round(cut * synth.SR)], m, HOP, st.SR)
    for a, b in zip(part, full, strict=True):
        np.testing.assert_allclose(a, b[:m], atol=1e-9)


def test_causality_mix_exercises_every_structure_field(
    mix_form: dict[str, Any],
) -> None:
    f = mix_form
    assert len(f["changes"]) >= 2
    assert any(j["db"] > 0 for j in f["jumps"]) and any(j["db"] < 0 for j in f["jumps"])
    # The final chord rings out into silence: a candidate ending after it.
    assert any(e["t"] > 40 for e in f["ends"])
    assert {k["level"] for k in f["ticks"]} == {"quiet", "medium", "loud"}
    assert len({k.get("key") for k in f["ticks"]}) > 2


def test_changes_are_known_after_the_next_window_and_the_peak_window(
    mix_form: dict[str, Any],
) -> None:
    p = st.Params()
    for c in mix_form["changes"]:
        lead = c["knownAt"] - c["t"]
        assert lead == pytest.approx(p.change_next_s + p.change_peak_s, abs=0.1)
        assert c["strength"] > 0


def test_the_structure_parameters_are_configurable(mix: np.ndarray) -> None:
    """A compressed setting (for the check's short track) detects changes sooner."""
    p = st.Params(
        change_past_s=4, change_next_s=1, change_peak_s=2, key_after_s=3, end_after_s=3
    )
    f = form(mix[: 20 * synth.SR], p)
    assert next(k["t"] for k in f["ticks"] if "key" in k) == pytest.approx(3.0)
    assert f["changes"]
    assert all(
        c["knownAt"] - c["t"] == pytest.approx(3.0, abs=0.1) for c in f["changes"]
    )
    # (The default setting knows the quiet stretch's change at 16.5 s only 6 s later.)
    assert form(mix[: 20 * synth.SR])["changes"] == []


def sine(segments: list[tuple[float, float]], freq: float = 440.0) -> np.ndarray:
    """A sine in (seconds, dB) segments."""
    parts = []
    for secs, db in segments:
        t = np.arange(round(secs * st.SR)) / st.SR
        parts.append(10 ** (db / 20) * np.sqrt(2) * np.sin(2 * np.pi * freq * t))
    return np.concatenate(parts)


def test_a_loudness_jump_is_reported_at_the_end_of_its_second() -> None:
    p = st.Params()
    f = form(sine([(10.0, -30.0), (8.0, -18.0)]), p)
    (j,) = f["jumps"]
    # The rise of 12 dB at 10 s: 6 dB is reached once about half of the last second is louder.
    assert 10.3 < j["knownAt"] < 10.8
    assert j["t"] == pytest.approx(j["knownAt"] - 1.0)
    assert j["db"] >= 6


def test_jumps_need_the_full_seven_seconds_and_are_four_seconds_apart() -> None:
    f = form(
        sine([(3.0, -40.0), (5.0, -20.0), (3.0, -40.0), (3.0, -20.0), (8.0, -40.0)])
    )
    times = [j["knownAt"] for j in f["jumps"]]
    assert all(t >= 7.0 - 1e-9 for t in times)
    assert all(b - a > 4.0 for a, b in itertools.pairwise(times))


def test_an_ending_is_20_db_below_the_loudest_second_held_for_1_5_s() -> None:
    f = form(sine([(12.0, -20.0), (4.0, -45.0)]))
    (e,) = f["ends"]
    assert e["t"] == pytest.approx(12.0, abs=0.1)
    assert e["knownAt"] == pytest.approx(e["t"] + 1.5)
    # Not before 10 s of audio, and not for a drop of less than 20 dB.
    assert form(sine([(5.0, -20.0), (4.0, -45.0)]))["ends"] == []
    assert form(sine([(12.0, -20.0), (4.0, -35.0)]))["ends"] == []


def test_levels_follow_the_energy_with_hysteresis() -> None:
    p = st.Params()
    e = np.array([0.5, 0.85, 0.75, 0.69, 0.45, 0.39, 0.45, 0.5, 0.79, 0.8])
    assert stc.levels(e, p) == [
        "medium",
        "loud",
        "loud",
        "medium",
        "medium",
        "quiet",
        "quiet",
        "medium",
        "medium",
        "loud",
    ]


def test_keys_match_the_major_and_minor_profiles() -> None:
    chord = lambda *pcs: np.array([1.0 if i in pcs else 0.1 for i in range(12)])
    assert stc._key_name(chord(0, 4, 7, 2, 5, 9, 11)) == "C major"
    assert stc._key_name(chord(9, 0, 4, 2, 11)) == "A minor"
    assert stc._key_name(chord(2, 6, 9)) == "D major"


# --- accent forecasts (stage 5) -----------------------------------------------------


def grid(n: int, lead: float = 0.85) -> list[dict[str, Any]]:
    """Committed beats every 0.5 s from 0.5 s, each known `lead` before it."""
    return [
        {"t": 0.5 + 0.5 * i, "knownAt": max(0.0, 0.5 + 0.5 * i - lead)}
        for i in range(n)
    ]


def hits_on(
    beats: list[dict[str, Any]], idx: list[int], late: float = 0.02
) -> list[dict[str, Any]]:
    """A strong onset 10 ms after each of the beats, known `late` after it."""
    return [
        {"t": beats[i]["t"] + 0.01, "knownAt": beats[i]["t"] + late, "strong": True}
        for i in idx
    ]


P = st.Params()


def test_a_repeating_hit_is_forecast_after_three_cycles() -> None:
    beats = grid(60)
    f = acc.forecasts(beats, hits_on(beats, list(range(0, 60, 8))), P)
    assert [(round((x["t"] - 0.5) / 0.5), x["cycle"]) for x in f] == [
        (24, 8),
        (32, 8),
        (40, 8),
        (48, 8),
        (56, 8),
    ]
    # The share of the last 4 cycles with the hit: the first has only 3 behind it.
    assert [x["confidence"] for x in f] == [0.75, 1.0, 1.0, 1.0, 1.0]
    # Known when the beat is (it was committed after the confirming hit was heard).
    assert all(x["knownAt"] == pytest.approx(x["t"] - 0.85) for x in f)


def test_a_failed_forecast_needs_three_more_cycles() -> None:
    beats = grid(80)
    idx = [i for i in range(0, 80, 8) if i != 32]
    f = acc.forecasts(beats, hits_on(beats, idx), P)
    # 32 is forecast and misses; 40, 48 and 56 confirm it again before 64.
    assert [round((x["t"] - 0.5) / 0.5) for x in f] == [24, 32, 64, 72]
    assert f[2]["confidence"] == 0.75


def test_a_forecast_is_known_when_both_its_beat_and_its_hits_are() -> None:
    # Beats known 2 s ahead: the confirming onsets are heard after the beat is committed.
    beats = grid(20, lead=2.0)
    f = acc.forecasts(beats, hits_on(beats, list(range(0, 20, 2))), P)
    assert f
    for x in f:
        i = round((x["t"] - 0.5) / 0.5)
        last = beats[i - x["cycle"]]["t"] + 0.02
        assert x["knownAt"] == pytest.approx(max(beats[i]["knownAt"], last))


def test_no_forecast_without_the_wind_up_and_0_35_s_ahead() -> None:
    beats = grid(60, lead=0.5)
    assert acc.forecasts(beats, hits_on(beats, list(range(0, 60, 8))), P) == []


def test_one_forecast_per_beat_the_earliest_known() -> None:
    beats = grid(40, lead=2.0)
    f = acc.forecasts(beats, hits_on(beats, list(range(40))), P)
    ts = [x["t"] for x in f]
    assert ts == sorted(set(ts))
    # With every beat a hit, each cycle forecasts each beat; the longest cycle's last
    # confirming hit is heard first, so it is the one kept once 3 of its cycles are in.
    for x in f:
        i = round((x["t"] - 0.5) / 0.5)
        longest = max(c for c in (2, 4, 8) if i - 3 * c >= 0)
        assert x["cycle"] == longest, i


def test_onsets_must_be_strong_and_within_50_ms() -> None:
    beats = grid(60)
    weak = [{**o, "strong": False} for o in hits_on(beats, list(range(0, 60, 8)))]
    assert acc.forecasts(beats, weak, P) == []
    far = [{**o, "t": o["t"] + 0.05} for o in hits_on(beats, list(range(0, 60, 8)))]
    assert acc.forecasts(beats, far, P) == []


def test_forecasts_read_only_entries_known_by_then() -> None:
    """Forecasts from the beats and onsets known by X are the full forecasts known by X."""
    rng = np.random.default_rng(5)
    beats = grid(120, lead=0.9)
    # Hits every 8 beats with gaps, every 4 beats in a stretch, and strong onsets off the beat.
    idx = [i for i in range(0, 120, 8) if rng.random() > 0.15]
    idx += list(range(60, 90, 4))
    onsets = hits_on(beats, sorted(set(idx)), late=0.03)
    onsets += [
        {"t": float(t), "knownAt": float(t) + 0.02, "strong": True}
        for t in rng.uniform(0, 60, 15)
    ]
    onsets.sort(key=lambda o: o["knownAt"])
    full = acc.forecasts(beats, onsets, P)
    assert len(full) >= 5
    for cut in sorted({x["knownAt"] for x in full} | {10.0, 33.33, 47.5}):
        part = acc.forecasts(known_by(beats, cut, 0.0), known_by(onsets, cut, 0.0), P)
        assert known_by(part, cut, 0.0) == known_by(full, cut, 0.0), cut
