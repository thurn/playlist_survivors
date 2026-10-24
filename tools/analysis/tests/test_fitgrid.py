from __future__ import annotations

import numpy as np
import pytest

from ps_analysis import fitgrid as fg
from ps_analysis import synth


def jittered(times: np.ndarray, ms: float, seed: int = 7) -> np.ndarray:
    return times + np.random.default_rng(seed).uniform(-ms, ms, len(times)) / 1000


def test_a_steady_decode_fits_one_constant_grid() -> None:
    beats = jittered(1.2 + 0.5 * np.arange(120), 4)
    fit = fg.fit_beats(beats)
    assert fit.kind == "constant"
    (line,) = fit.lines
    assert line.period == pytest.approx(0.5, abs=1e-4)
    assert line.phase == pytest.approx(1.2, abs=2e-3)
    assert fit.stats()["median"] <= fg.MEDIAN_S
    # A missed beat leaves a gap of two periods, not a new tempo.
    gappy = np.delete(beats, [30, 31, 70])
    assert fg.fit_beats(gappy).kind == "constant"


def test_a_tempo_change_fits_in_pieces() -> None:
    a = 0.5 + 0.5 * np.arange(60)
    b = a[-1] + 0.42 * np.arange(1, 61)
    fit = fg.fit_beats(jittered(np.concatenate([a, b]), 3))
    assert fit.kind == "piecewise"
    assert [round(ln.period, 3) for ln in fit.lines] == [0.5, 0.42]
    assert abs(fit.runs[0][1] - 60) <= 1


def test_the_grid_keeps_its_ends_when_the_phase_moves() -> None:
    beats = 0.3 + 0.5 * np.arange(40)
    fit = fg.fit_beats(beats)
    for shift in (-0.015, 0.015):
        fit.lines[0].phase = 0.3 + shift
        grid = fg.grid_points(beats, fit, 1.0, 0)
        assert len(grid) == 40
        assert abs(grid[0] - beats[0]) <= 0.025 and abs(grid[-1] - beats[-1]) <= 0.025


def test_a_level_keeps_its_phase_across_a_piecewise_join() -> None:
    # 59 beats at 0.5 s, then 60 at 0.42 s: an odd split.
    a = 0.5 + 0.5 * np.arange(59)
    b = a[-1] + 0.42 * np.arange(1, 61)
    beats = np.concatenate([a, b])
    fit = fg.fit_beats(beats)
    assert fit.kind == "piecewise"
    base = fg.grid_points(beats, fit, 1.0, 0)
    assert len(base) == len(beats)
    assert fg.grid_points(beats, fit, 1.0, 0) == pytest.approx(beats, abs=1e-6)
    for off in (0, 1):
        half = fg.grid_points(beats, fit, 2.0, off)
        assert half == pytest.approx(beats[off::2], abs=1e-6)
    double = fg.grid_points(beats, fit, 0.5, 0)
    assert len(double) == 2 * len(beats) - 1
    assert double[1::2] == pytest.approx((beats[:-1] + beats[1:]) / 2, abs=1e-6)
    triple = fg.grid_points(beats, fit, 1.5, 0)
    assert triple[::2] == pytest.approx(beats[::3][: len(triple[::2])], abs=1e-6)


def test_an_unsteady_decode_fails() -> None:
    beats = np.cumsum(np.random.default_rng(3).uniform(0.3, 0.7, 150))
    assert fg.fit_beats(beats).kind == "failed"


def test_the_meter_is_voted_from_the_decoded_downbeats() -> None:
    grid = 0.5 * np.arange(60)
    # Downbeats every third grid beat from the second, one of them misplaced.
    downs = grid[1::3].copy()
    downs[5] = grid[17]
    meter, offset, agreement = fg.fit_meter(grid, downs)
    assert (meter, offset) == (3, 1)
    assert agreement == pytest.approx(19 / 20)
    # Decoded downbeats half a beat off the grid don't agree (they are not dropped from the count).
    assert fg.fit_meter(grid, np.concatenate([downs, grid[:20] + 0.25]))[
        2
    ] == pytest.approx(19 / 40)
    assert fg.fit_meter(grid, np.zeros(0)) == (4, 0, 0.0)


def test_the_level_a_player_taps_is_chosen() -> None:
    # A decode at 0.3 s (200 bpm): double the period (0.6 s) has every grid point on an onset; the
    # two-thirds level (0.45 s) has half of them between onsets.
    beats = 0.3 * np.arange(200)
    fit = fg.fit_beats(beats)
    t = np.arange(0, 60, 0.005)
    v = np.zeros_like(t)
    v[np.round(beats / 0.005).astype(int)] = 1.0
    level, offset, grid = fg.choose_level(beats, fit, fg.Envelope(t, v), np.zeros(0))
    assert (level, offset) == (2.0, 0)
    assert np.diff(grid) == pytest.approx(0.6)
    # Its phase follows the decoded downbeats (every other beat from the second).
    level, offset, grid = fg.choose_level(beats, fit, fg.Envelope(t, v), beats[1::4])
    assert (level, offset) == (2.0, 1)
    assert grid[0] == pytest.approx(0.3)


@pytest.mark.parametrize("name", ["house-128"])
def test_the_fit_recovers_a_synthetic_tracks_exact_beats(name: str) -> None:
    y, ref = synth.track(name)
    fitted = fg.fit_reference(y)
    assert (fitted["source"], fitted["fit"], fitted["level"]) == (
        "fitted",
        "constant",
        1.0,
    )
    assert fitted["meter"] == 4 and fitted["meterConfidence"] == "high"
    exact = np.asarray(ref["beats"])
    beats = np.asarray(fitted["beats"])
    beats = beats[(beats >= exact[0] - 0.05) & (beats <= exact[-1] + 0.05)]
    assert len(beats) == len(exact)
    i = np.clip(np.searchsorted(exact, beats), 1, len(exact) - 1)
    err = np.minimum(np.abs(exact[i - 1] - beats), np.abs(exact[i] - beats))
    assert err.max() <= 0.002
    # Downbeats are grid beats a bar apart; sections start on downbeats.
    downs = fitted["downbeats"]
    assert set(downs) <= set(fitted["beats"])
    assert np.diff(downs) == pytest.approx(4 * 60 / 128, abs=1e-3)
    assert all(s["s0"] in downs and s["approximate"] for s in fitted["sections"])
