"""Measures the sample offset of decoded files against the song map's reference decode (TD0).

Usage (from the repo root; audio stays in gitignored paths):
    uv run --project tools/analysis python demo3d/evidence/td0/align.py \
        public/audio/action.flac tools/analysis/work/action.ogg [more files ...]

Prints frames and per-window lags; writes tools/analysis/work/align.json.
"""
import json, sys
import numpy as np, soundfile as sf
from scipy import signal

ref_path = sys.argv[1]
others = sys.argv[2:]
ref, sr = sf.read(ref_path, dtype='float64', always_2d=True)
ref = ref.mean(axis=1)
MAXLAG = 4000
WINDOWS = [(1.0, 11.0), (60.0, 70.0), (125.0, 135.0), (185.0, 195.0)]


def lag_of(x, y, t0, t1):
    """Lag (samples) such that y[n + lag] ~ x[n] over [t0, t1)."""
    a, b = int(t0 * sr), int(t1 * sr)
    xs = x[a:b]
    ys = y[a - MAXLAG:b + MAXLAG]
    c = signal.correlate(ys, xs, mode='valid', method='fft')  # length 2*MAXLAG+1
    k = int(np.argmax(c))
    # parabolic sub-sample refinement
    if 0 < k < len(c) - 1:
        y0, y1, y2 = c[k - 1], c[k], c[k + 1]
        frac = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2)
    else:
        frac = 0.0
    lag = k - MAXLAG + frac
    li = int(round(lag))
    seg = y[a + li:b + li]
    resid = 10 * np.log10(np.mean((seg - xs) ** 2) / np.mean(xs ** 2))
    return lag, resid


out = {"reference": ref_path, "sampleRate": sr, "referenceFrames": len(ref), "files": {}}
for p in others:
    y, sr2 = sf.read(p, dtype='float64', always_2d=True)
    y = y.mean(axis=1)
    assert sr2 == sr, (p, sr2)
    res = []
    for t0, t1 in WINDOWS:
        lag, resid = lag_of(ref, y, t0, t1)
        res.append({"window": [t0, t1], "lagSamples": round(lag, 3), "lagMs": round(1000 * lag / sr, 4), "residualDb": round(resid, 1)})
    out["files"][p] = {"frames": len(y), "frameDelta": len(y) - len(ref), "windows": res}
    print(p, 'frames', len(y), 'delta', len(y) - len(ref))
    for r in res:
        print('   ', r)
json.dump(out, open('tools/analysis/work/align.json', 'w'), indent=1)
