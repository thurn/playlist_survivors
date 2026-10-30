"""The offline mix's sample placement and limiter, on synthetic signals."""

from __future__ import annotations

import numpy as np

from demo3d_tools import mix


def test_events_land_at_their_song_time_from_the_start_offset() -> None:
    song = np.zeros((mix.RATE * 2, 2), dtype=np.float32)
    click = np.ones((10, 2), dtype=np.float32)
    events = [{"time": 10.5, "clip": "c", "gain": 1.0}, {"time": 9.0, "clip": "c", "gain": 1.0},
              {"time": 10.0, "clip": "missing", "gain": 1.0}]
    out = mix.render(events, song, 10.0, {"c": click})
    hits = np.nonzero(out[:, 0])[0]
    assert hits.min() == mix.RATE // 2 and len(hits) == 10  # the 9 s event is before the video


def test_the_limiter_never_passes_one_and_leaves_quiet_audio_alone() -> None:
    x = np.array([[0.5, -0.5], [3.0, -3.0]], dtype=np.float32)
    y = mix.soft_limit(x)
    assert np.allclose(y[0], x[0])
    assert np.all(np.abs(y) <= mix.CEILING + 1e-6)


def test_a_pitched_event_plays_faster_and_shorter() -> None:
    song = np.zeros((1000, 2), dtype=np.float32)
    ramp = np.stack([np.linspace(0, 1, 100, dtype=np.float32)] * 2, axis=1)
    out = mix.render([{"time": 0.0, "clip": "r", "gain": 1.0, "pitch": 2.0}], song, 0.0, {"r": ramp}, limit=False)
    hits = np.nonzero(out[:, 0])[0]
    assert hits.max() == 49  # half as long
    assert np.isclose(out[10, 0], mix.EFFECTS_LEVEL * ramp[20, 0], atol=1e-6)
