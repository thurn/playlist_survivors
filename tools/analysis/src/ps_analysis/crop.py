"""`crop`: cut the excerpt from the source recording and write it as FLAC.

Crop points are rounded to whole samples; the reported `cropStart`/`cropEnd`
are the exact sample positions in seconds. A short fade-in and fade-out avoid
clicks at the cut points.
"""

from __future__ import annotations

import hashlib
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
    start: float,
    end: float,
    mono: bool = True,
    fade_in_ms: float = 5.0,
    fade_out_ms: float = 300.0,
) -> dict[str, Any]:
    y, sr = sf.read(str(source), dtype="float32", always_2d=True)
    s0 = round(start * sr)
    s1 = round(end * sr)
    if not (0 <= s0 < s1 <= len(y)):
        raise ValueError(
            f"crop {start}–{end} s is outside the source (0–{len(y) / sr:.3f} s)"
        )
    seg = y[s0:s1].copy()
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
    return {
        "file": Path(out).name,
        "cropStart": round(s0 / sr, 6),
        "cropEnd": round(s1 / sr, 6),
        "sampleRate": sr,
        "duration": round((s1 - s0) / sr, 6),
        "channels": seg.shape[1],
        "sha256": sha256(out),
    }
