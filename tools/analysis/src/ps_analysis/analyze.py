"""`analyze`: run several beat trackers and write a draft song map.

Trackers (all times in seconds from the start of the cropped file):

- `madmom`: RNN beat activations + DBN beat tracking over 55–215 BPM.
- `madmom-fast`: the same activations, DBN restricted to a fast tempo range
  (default 150–240 BPM). Useful when `madmom` locks onto half tempo.
- `madmom-downbeat`: RNN downbeat activations + DBN bar tracking (2 or 4 beats
  per bar). Its beats carry a position in the bar.
- `librosa`: `librosa.beat.beat_track` on the onset envelope.

The draft uses the `madmom` beats, one pulsed section covering every bar, the
energy curve and the 30 strongest accent candidates. The beat-map agent then
corrects it with `edit`.
"""

from __future__ import annotations

from typing import Any

import numpy as np

from . import audio as au


def run_trackers(
    y: np.ndarray, sr: int, fast_range: tuple[float, float] = (150, 240)
) -> dict[str, Any]:
    import librosa
    from madmom.audio.signal import Signal
    from madmom.features.beats import DBNBeatTrackingProcessor, RNNBeatProcessor
    from madmom.features.downbeats import (
        DBNDownBeatTrackingProcessor,
        RNNDownBeatProcessor,
    )

    sig = Signal(y.astype(np.float32), sample_rate=sr, num_channels=1)
    act = RNNBeatProcessor()(sig)
    madmom_beats = DBNBeatTrackingProcessor(fps=100, min_bpm=55, max_bpm=215)(act)
    fast_beats = DBNBeatTrackingProcessor(
        fps=100, min_bpm=fast_range[0], max_bpm=fast_range[1]
    )(act)
    dact = RNNDownBeatProcessor()(sig)
    down = DBNDownBeatTrackingProcessor(beats_per_bar=[2, 4], fps=100)(dact)
    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=512)
    _, lb = librosa.beat.beat_track(
        onset_envelope=env, sr=sr, hop_length=512, units="time"
    )
    return {
        "madmom": [float(t) for t in madmom_beats],
        "madmom-fast": [float(t) for t in fast_beats],
        "madmom-downbeat": [float(t) for t in down[:, 0]],
        "librosa": [float(t) for t in np.atleast_1d(lb)],
        "_downbeatPositions": [int(p) for p in down[:, 1]],
    }


def draft_map(
    y: np.ndarray,
    sr: int,
    trackers: dict[str, Any],
    onsets: au.Onsets,
    candidates: list[dict[str, float]],
    audio_info: dict[str, Any],
    song: str,
) -> dict[str, Any]:
    beats = list(trackers["madmom"])
    pos = trackers["_downbeatPositions"]
    bars = [p for p in pos if p > 0]
    bpb = int(max(pos)) if bars else 4
    m: dict[str, Any] = {
        "version": 1,
        "song": song,
        "audio": audio_info,
        "meter": [{"fromBar": 1, "beatsPerBar": bpb}],
        "beats": beats,
        "firstDownbeatIndex": 0,
        "sections": [],
        "phrases": [],
        "tempoChanges": [],
        "energy": au.beat_energy(y, sr, beats, onsets),
        "accents": [
            {"time": c["time"], "strength": c["strength"]} for c in candidates[:30]
        ],
    }
    from . import songmap as sm

    idx = sm.index_song(m)
    m["sections"] = [
        {"label": "all", "bars": [1, max(1, idx.bar_count)], "feel": "pulsed"}
    ]
    return m
