"""Scores a live spike run against the track's reference beats.

    tools/analysis/.venv/bin/python native/spike/align.py native/logs house-128

Finds where the track starts in the analyzer's captured stream (cross-correlation
with the file), then reports, against the reference beats:

- committed-beat error in stream time (the analysis alone);
- each click's offset at the ear: its actual render time plus L_out, against the
  music beat's tap time plus L_tap, using the loopback-measured tap - render offset;
- the flash: the beat error plus the measured frame-target offset.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

SR = 44100


def summary(name: str, v) -> None:
    v = np.asarray(v, dtype=float)
    if len(v) == 0:
        print(f"{name}: none")
        return
    a = np.abs(v)
    print(
        f"{name}: n={len(v)} median {np.median(v):+.1f} ms, |e| median {np.median(a):.1f} "
        f"p95 {np.percentile(a, 95):.1f} max {a.max():.1f}"
    )


def main() -> None:
    logs = Path(sys.argv[1])
    track = sys.argv[2]
    root = Path(__file__).resolve().parents[2]
    stream = np.fromfile(logs / "app-stream.f32", dtype="<f4")
    y, sr = sf.read(str(logs / f"{track}.wav"), dtype="float32", always_2d=True)
    y = y.mean(axis=1)
    assert sr == SR
    run = json.loads((logs / "app-run.json").read_text())
    ref = np.asarray(json.loads((root / f"public/reactive/{track}/reference.json").read_text())["beats"])

    # stream[n] ~ g * y[n - lag]
    n = 1 << int(np.ceil(np.log2(len(stream) + len(y))))
    xc = np.fft.irfft(np.fft.rfft(stream, n) * np.conj(np.fft.rfft(y, n)), n)
    lag = int(np.argmax(xc[: len(stream)]))
    off = lag / SR
    print(f"track starts at stream time {off:.4f} s")

    beats = run["beats"]
    t = np.asarray([b["t"] for b in beats])
    true_s = ref + off
    idx = np.searchsorted(true_s, t)
    near = np.where(
        (idx > 0) & ((idx == len(true_s)) | (np.abs(t - true_s[np.maximum(idx - 1, 0)]) < np.abs(t - true_s[np.minimum(idx, len(true_s) - 1)]))),
        np.maximum(idx - 1, 0),
        np.minimum(idx, len(true_s) - 1),
    )
    err = (t - true_s[near]) * 1e3
    settled = t > off + 5.0
    summary("committed beat - reference (stream time, after 5 s)", err[settled])

    # Stream time -> tap host time through the per-buffer anchors.
    anchors = np.asarray(run["anchors"])
    ratio = 48000 / SR

    def host_of_stream(s: float) -> float:
        i = s * SR * ratio
        k = np.searchsorted(anchors[:, 0], i, side="right") - 1
        k = max(k, 0)
        return anchors[k, 1] + (i - anchors[k, 0]) / 48000

    heard_to_beat = {round(b["heard"] - run["lOut"], 6): b["t"] for b in beats}
    for name, tap_minus_render in (("tap", 0.0230), ("mic", 0.0223)):
        offs = []
        for at, actual in run["played"]:
            bt = heard_to_beat.get(round(at, 6))
            if bt is None or bt < off + 5.0:
                continue
            j = int(np.argmin(np.abs(true_s - bt)))
            music_tap = host_of_stream(true_s[j])
            # ear: click at actual + L_out; music at tap + L_out - (tap - render)
            offs.append((actual - (music_tap - tap_minus_render)) * 1e3)
        summary(f"click at the ear - music beat (tap - render from the {name} loopback)", offs)
    flash = np.asarray(run["flashOffsetMs"])
    print(f"flash frame target - heard time: median {np.median(flash):.1f} ms (add the beat error above)")


if __name__ == "__main__":
    main()
