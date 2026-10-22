"""`crop`: cut the excerpt from the source recording and write it as FLAC.

Crop points are rounded to whole samples; the reported `cropStart`/`cropEnd`
are the exact sample positions in seconds. A short fade-in and fade-out avoid
clicks at the cut points. An excerpt can also join several segments of the
source to skip repeats; the crop info then lists them under `segments`.
"""

from __future__ import annotations

import hashlib
from itertools import pairwise
from pathlib import Path
from typing import Any

import numpy as np
import soundfile as sf


def sha256(path: str | Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def crop(
    source: str | Path,
    out: str | Path,
    start: float | None = None,
    end: float | None = None,
    mono: bool = True,
    fade_in_ms: float = 5.0,
    fade_out_ms: float = 300.0,
    segments: list[tuple[float, float]] | None = None,
    crossfade_ms: float = 30.0,
) -> dict[str, Any]:
    """Cut `start`–`end`, or join several `segments` in order.

    Each join is an equal-power crossfade of `crossfade_ms` centred on the
    cut, so the excerpt's time at a join is the sum of the earlier segments'
    lengths. Put cuts just before a beat, where the music repeats.
    """
    if segments is None:
        if start is None or end is None:
            raise ValueError("crop needs start and end, or segments")
        segments = [(start, end)]
    y, sr = sf.read(str(source), dtype="float32", always_2d=True)
    bounds: list[tuple[int, int]] = []
    for a, b in segments:
        s0, s1 = round(a * sr), round(b * sr)
        if not (0 <= s0 < s1 <= len(y)):
            raise ValueError(
                f"crop {a}–{b} s is outside the source (0–{len(y) / sr:.3f} s)"
            )
        if bounds and s0 < bounds[-1][1]:
            raise ValueError(f"segment {a}–{b} s starts before the previous one ends")
        bounds.append((s0, s1))
    half = int(sr * crossfade_ms / 2000) if len(bounds) > 1 else 0
    for (a0, a1), (b0, b1) in pairwise(bounds):
        if a1 + half > len(y) or b0 < half or min(a1 - a0, b1 - b0) <= 2 * half:
            raise ValueError(
                f"the join at {a1 / sr:.3f} -> {b0 / sr:.3f} s is too close to an edge to crossfade"
            )
    ramp = np.linspace(0, np.pi / 2, 2 * half, dtype=np.float32)[:, None]
    parts: list[np.ndarray] = []
    for k, (s0, s1) in enumerate(bounds):
        if k > 0:
            p1 = bounds[k - 1][1]
            parts.append(
                y[p1 - half : p1 + half] * np.cos(ramp)
                + y[s0 - half : s0 + half] * np.sin(ramp)
            )
        lo = s0 + (half if k > 0 else 0)
        hi = s1 - (half if k < len(bounds) - 1 else 0)
        parts.append(y[lo:hi])
    seg = np.concatenate(parts)
    if mono:
        seg = seg.mean(axis=1, keepdims=True)
    n_in = int(sr * fade_in_ms / 1000)
    n_out = int(sr * fade_out_ms / 1000)
    if n_in:
        seg[:n_in] *= np.linspace(0, 1, n_in, dtype=np.float32)[:, None]
    if n_out:
        seg[-n_out:] *= np.linspace(1, 0, n_out, dtype=np.float32)[:, None]
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(out), seg, sr, format="FLAC", subtype="PCM_16")
    info: dict[str, Any] = {
        "file": Path(out).name,
        "cropStart": round(bounds[0][0] / sr, 6),
        "cropEnd": round(bounds[-1][1] / sr, 6),
        "sampleRate": sr,
        "duration": round(len(seg) / sr, 6),
        "channels": seg.shape[1],
        "sha256": sha256(out),
    }
    if len(bounds) > 1:
        info["segments"] = [[round(s0 / sr, 6), round(s1 / sr, 6)] for s0, s1 in bounds]
        info["crossfadeMs"] = crossfade_ms
    return info
