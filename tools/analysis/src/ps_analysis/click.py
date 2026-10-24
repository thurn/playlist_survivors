"""`click`: the excerpt mixed with a click on every beat, for the human gate.

Ordinary beats get a short 1 kHz click, downbeats a louder 1.6 kHz click, and
the first beat of each section a louder 2.4 kHz click, so a listener can hear
both the beat grid and the section boundaries.
"""

from __future__ import annotations

import numpy as np

from . import songmap as sm


def _click(sr: int, freq: float, amp: float, ms: float = 35.0) -> np.ndarray:
    n = int(sr * ms / 1000)
    t = np.arange(n) / sr
    return (amp * np.sin(2 * np.pi * freq * t) * np.exp(-t * 1000 / (ms / 4))).astype(
        np.float32
    )


def render_click(
    m: sm.SongMap, y: np.ndarray, sr: int, music_gain: float = 0.6
) -> np.ndarray:
    idx = sm.index_song(m)
    downbeats = {idx.bar_start[b] for b in range(1, idx.bar_count + 1)}
    sec_starts = {
        idx.bar_start[s["bars"][0]]
        for s in m["sections"]
        if s["bars"][0] <= idx.bar_count
    }
    return render_click_at(y, sr, m["beats"], downbeats, sec_starts, music_gain)


def render_click_at(
    y: np.ndarray,
    sr: int,
    beats: list[float],
    downbeats: set[int],
    section_starts: set[int],
    music_gain: float = 0.6,
) -> np.ndarray:
    """The click over `y` (mono, or frames x channels): beats by time, accents by beat index."""
    peak = float(np.max(np.abs(y))) or 1.0
    out = (y / peak * music_gain).astype(np.float32)
    clicks = {
        "beat": _click(sr, 1000, 0.35),
        "down": _click(sr, 1600, 0.7),
        "section": _click(sr, 2400, 0.9, 60),
    }
    for i, t in enumerate(beats):
        kind = (
            "section" if i in section_starts else "down" if i in downbeats else "beat"
        )
        c = clicks[kind]
        s = round(t * sr)
        if s < 0 or s >= len(out):
            continue
        e = min(len(out), s + len(c))
        out[s:e] += c[: e - s] if out.ndim == 1 else c[: e - s, None]
    return np.clip(out, -1, 1)
