"""The sync spike's measurement, on a synthetic mix whose clicks sit at known offsets from the music."""

from __future__ import annotations

from pathlib import Path

import numpy as np

from demo3d_tools import spike

RATE = 48000


def synthetic(offsets_ms: list[float], lead_frames: int = 3000, seconds: float = 30.0):
    """A noisy 'song' with a thump every 0.5 s, mixed from frame `lead_frames` on, plus a click per
    beat at its song time plus its offset; and the run's report as the player would write it."""
    rng = np.random.default_rng(7)
    n = int(seconds * RATE)
    song = 0.05 * rng.standard_normal(n).astype(np.float32)
    beats = np.arange(1.0, seconds - 5, 0.5)
    for b in beats:  # a decaying thump on each beat, so the music has onsets
        i = int(b * RATE)
        song[i:i + 2000] += 0.3 * np.exp(-np.arange(2000) / 300.0).astype(np.float32)
    ref = np.stack([song, song], axis=1)
    mix = np.zeros((lead_frames + n, 2), dtype=np.float32)
    mix[lead_frames:] = ref
    lead = lead_frames / RATE  # the DSP time (from the capture's start) of song time 0
    for k, b in enumerate(beats):
        at = lead_frames + round((b + offsets_ms[k % len(offsets_ms)] / 1000) * RATE)
        mix[at:at + 100] += 0.6 * np.cos(2 * np.pi * 2000 * np.arange(100) / RATE)[:, None]
    # The audio thread's clock: tick = DSP time + 10 s. Each frame's frameSong is 2 ms ahead of
    # the song heard at its light.
    frames = [{"tick": 10 + i / 60, "display": 1 / 60,
               "frameSong": (i / 60 + 1 / 60) - lead - 0.038 + 0.002} for i in range(60, 600)]
    report = {
        "outputRate": RATE, "dspBufferLength": 256, "dspBuffers": 4, "outputLatency": 0.038,
        "deviceLatency": 0.0167, "dspBuffering": 0.0213, "anchorDsp": lead, "anchorSong": 0.0,
        "captureStartDsp": 0.0, "stalls": 0, "gaps": 0, "error": "",
        "clickSong": list(beats), "clickDsp": [lead + b for b in beats],  # scheduled on the beats
        "bufferTick": [10 + i * 256 / RATE for i in range(len(mix) // 256)],
        "bufferDsp": [i * 256 / RATE for i in range(len(mix) // 256)],
        "frames": frames,
    }
    return report, mix, ref


def test_clicks_on_the_beat_measure_zero_with_no_drift() -> None:
    report, mix, ref = synthetic([0.0])
    s = spike.measure(report, mix, ref)
    assert s["ok"], s
    assert s["clicks_found"] == s["clicks_scheduled"]
    assert abs(s["music_to_click_ms"]["median"]) < 0.03 and abs(s["drift_ms_per_min"]) < 0.01
    assert s["music_lag_frames"]["min"] == s["music_lag_frames"]["max"] == 3000


def test_late_clicks_and_a_misplaced_music_are_measured_against_the_music_itself() -> None:
    # The clicks sound 5 ms after their beats, though the report schedules them on the beats.
    report, mix, ref = synthetic([5.0, 5.0, 6.0])
    s = spike.measure(report, mix, ref)
    assert not s["ok"]
    assert abs(s["music_to_click_ms"]["median"] - 5.0) < 0.05
    assert abs(s["music_to_click_ms"]["max"] - 6.0) < 0.05
    assert abs(s["click_to_schedule_ms"]["median"] - 5.0) < 0.05


def test_frame_to_heard_compares_frame_song_with_the_audio_threads_clock() -> None:
    report, mix, ref = synthetic([0.0])
    s = spike.measure(report, mix, ref)
    assert abs(s["frame_to_heard_ms"]["median"] - 2.0) < 0.01
    assert s["frame_song_backwards"] == 0


def test_a_playlist_puts_each_track_where_the_analyzer_timeline_does(tmp_path: Path, monkeypatch) -> None:
    lengths = {"a.ogg": 44100, "b.ogg": 22050}
    monkeypatch.setattr(spike, "afconvert", lambda path, rate, channels: np.ones(
        (int(lengths[path.name] * rate / 44100), channels), dtype=np.float32))
    (tmp_path / "list.m3u").write_text("#EXTM3U\na.ogg\n#PS-GAP:0.5\nb.ogg\na.ogg\n")
    out = spike.decode(tmp_path / "list.m3u", 48000)
    # a: 0-1 s; 0.5 s gap; b: 1.5-2 s; the default 2 s gap; a: 4-5 s.
    assert len(out) == 5 * 48000
    on = np.nonzero(out[:, 0])[0]
    assert on[0] == 0 and out[48000, 0] == 0 and out[72000, 0] == 1 and out[96000, 0] == 0
    assert out[4 * 48000, 0] == 1 and on[-1] == 5 * 48000 - 1
