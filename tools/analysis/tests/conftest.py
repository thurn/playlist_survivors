from __future__ import annotations

from typing import Any

import pytest


def make_map(
    beats: list[float],
    meter: list[dict[str, int]] | None = None,
    sections: list[dict[str, Any]] | None = None,
    first_downbeat: int = 0,
) -> dict[str, Any]:
    return {
        "version": 1,
        "song": "test",
        "audio": {
            "file": "test.flac",
            "source": "synthetic",
            "cropStart": 0.0,
            "cropEnd": 100.0,
            "sampleRate": 44100,
            "duration": 100.0,
        },
        "meter": meter or [{"fromBar": 1, "beatsPerBar": 2}],
        "beats": beats,
        "firstDownbeatIndex": first_downbeat,
        "sections": sections or [],
        "phrases": [],
        "tempoChanges": [],
        "energy": [0.5] * len(beats),
        "accents": [],
    }


@pytest.fixture
def grid_map() -> dict[str, Any]:
    """16 beats, 0.5 s apart from 1.0 s, 2/4: 8 bars in two pulsed sections."""
    beats = [1.0 + 0.5 * i for i in range(16)]
    return make_map(
        beats,
        sections=[
            {"label": "a", "bars": [1, 4], "feel": "pulsed"},
            {"label": "b", "bars": [5, 8], "feel": "pulsed"},
        ],
    )
