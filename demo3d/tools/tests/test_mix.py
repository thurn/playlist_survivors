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


def test_a_cue_ringing_into_a_cut_fades_out_over_the_cut_fade() -> None:
    song = np.zeros((mix.RATE, 2), dtype=np.float32)
    tone = np.ones((mix.RATE // 2, 2), dtype=np.float32)
    events = [{"time": 0.1, "clip": "t", "gain": 1.0, "fadeAt": 0.2}, {"time": 0.15, "clip": "t", "gain": 1.0}]
    out = mix.render(events, song, 0.0, {"t": tone}, limit=False, cut_fade_s=0.06)
    level = mix.EFFECTS_LEVEL
    assert np.isclose(out[round(0.19 * mix.RATE), 0], 2 * level)           # both, before the cut
    assert np.isclose(out[round(0.23 * mix.RATE), 0], level * 1.5)         # halfway down
    assert np.isclose(out[round(0.30 * mix.RATE), 0], level)               # only the cue with no fade
    assert np.isclose(out[round(0.62 * mix.RATE), 0], level)


def test_the_end_fade_takes_the_music_and_the_sounds_down_together() -> None:
    song = np.full((mix.RATE * 2, 2), 0.5, dtype=np.float32)
    tone = np.ones((mix.RATE * 2, 2), dtype=np.float32)
    out = mix.render([{"time": 10.0, "clip": "t", "gain": 1.0}], song, 10.0, {"t": tone}, limit=False,
                     fade_out={"at": 10.5, "seconds": 1.0})
    full = 0.5 * mix.MUSIC_LEVEL + mix.EFFECTS_LEVEL
    assert np.isclose(out[round(0.4 * mix.RATE), 0], full)
    assert np.isclose(out[round(1.0 * mix.RATE), 0], full / 2)
    assert not out[round(1.5 * mix.RATE) + 1:].any()


def test_the_trailer_check_passes_a_faded_mix_and_names_a_cue_left_ringing() -> None:
    from demo3d_tools import trailer

    song = np.full((mix.RATE * 3, 2), 0.3, dtype=np.float32)
    tone = np.ones((mix.RATE, 2), dtype=np.float32)
    dump = {"trailerStart": 0.0, "start": 0.0, "end": 2.5, "cutFadeS": 0.06, "fadeOut": {"at": 1.5, "seconds": 0.5},
            "sounds": [{"time": 0.2, "clip": "t", "gain": 1.0, "fadeAt": 0.5}, {"time": 1.0, "clip": "t", "gain": 0.5}]}
    raw = mix.render(dump["sounds"], song, 0.0, {"t": tone}, limit=False, cut_fade_s=0.06, fade_out=dump["fadeOut"])
    good = mix.Mix(dump, {"t": tone}, 0.0, song, raw, mix.soft_limit(raw))
    assert trailer.check(good) == []
    unfaded = mix.render(dump["sounds"], song, 0.0, {"t": tone}, limit=False)
    bad = mix.Mix(dump, {"t": tone}, 0.0, song, unfaded, mix.soft_limit(unfaded))
    problems = trailer.check(bad)
    assert any("still playing" in p for p in problems) and any("end fade" in p for p in problems)


def test_the_trailer_capture_has_a_frame_at_its_end() -> None:
    from demo3d_tools import trailer

    assert trailer.frames({"start": 121.4, "end": 121.4 + 1.0}) == 61
    assert trailer.frames({"start": 121.4, "end": 121.41}) == 2
