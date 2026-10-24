"""The library arithmetic the native analyzer reproduces to the bit, probed.

Each entry runs numpy, scipy, madmom or Python itself on inputs that tell plausible
implementations apart (chunk boundaries, spikes, ties, exact boundaries) and records the
result's bits. The native crate pins every entry (native/crates/analysis/src/pinned.rs, from
native/crates/analysis/tests/fixtures/library-arithmetic.json, which this module writes), and
tests/test_probe.py fails when a library upgrade changes one. The behaviours are described in
native/crates/analysis/LIBRARY-ARITHMETIC.md.

    uv run --project tools/analysis python -m ps_analysis.probe [OUT.json]
"""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

import numpy as np

FIXTURE = (
    Path(__file__).resolve().parents[4]
    / "native/crates/analysis/tests/fixtures/library-arithmetic.json"
)


def _f64(x: float) -> int:
    return int(np.float64(x).view(np.uint64))


def _f32(x: float) -> int:
    return int(np.float32(x).view(np.uint32))


def _values64(n: int) -> np.ndarray:
    return (np.array([((i * 7919) % 1_000_003) / 1_000_003.0 for i in range(n)])) ** 2


def _values32(n: int) -> np.ndarray:
    return np.array(
        [((i * 7919) % 1_000_003) / 1_000_003.0 for i in range(n)]
    ).astype(np.float32)


def sums() -> dict[str, Any]:
    """np.sum: pairwise in blocks of up to 128 with eight accumulators, halved above that,
    within buffered chunks of 8192 elements added in order; np.mean is the sum over n."""
    v, v32 = _values64(30000), _values32(20000)
    n64 = [5, 8, 9, 17, 64, 128, 129, 200, 300, 8192, 8193, 16385, 22050, 30000]
    n32 = [5, 1024, 8200, 20000]
    return {
        "f64": [[n, _f64(np.sum(v[:n]))] for n in n64],
        "f32": [[n, _f32(np.sum(v32[:n]))] for n in n32],
        "mean": [[n, _f64(np.mean(v[:n]))] for n in [3, 200, 22050]],
    }


def percentiles() -> list[Any]:
    """np.percentile (linear): index (n - 1) * q / 100, interpolated from the upper value when
    the weight is at least a half."""
    lists = [
        [3.0],
        [1.0, 2.0],
        [0.0, 10.0],
        [float(((i * 37) % 101) * 0.25 + (i % 7)) for i in range(37)],
    ]
    cases = [(0, 98.0), (1, 0.0), (1, 100.0), (2, 25.0), (3, 98.0), (3, 50.0), (3, 2.0)]
    return [
        [i, q, _f64(np.percentile(lists[i], q))] for i, q in cases
    ] + [["lists", lists]]


def strong_flags() -> dict[str, Any]:
    """detect_onsets' strong rule: at least the 98th percentile of the strengths known in the
    past 30 s, an onset exactly 30 s back included."""
    times = [i * 0.5 for i in range(150)] + [104.5, 104.75]
    strengths = [float((i * 7) % 23 + ((i * 13) % 5) * 0.5) for i in range(len(times))]
    strengths[149], strengths[150] = 20.0, 10.0
    known: list[tuple[float, float]] = []
    flags = []
    for t, s in zip(times, strengths, strict=True):
        known.append((t, s))
        recent = [x for k, x in known if k >= t - 30.0]
        flags.append(int(s >= float(np.percentile(recent, 98.0))))
    return {"times": times, "strengths": strengths, "flags": flags}


def uniform_filter() -> dict[str, Any]:
    """scipy.ndimage.uniform_filter (size 11, origin 5, constant) over madmom's onset buffer
    (three zeros in front): a float64 running sum stepped by new minus oldest, so a spike's
    rounding stays in it."""
    import scipy.ndimage as nd

    values = [0.5, 1e9, 3.25, 0.0, 7.0, 1e20, 2.0, 0.125, 9.0, 4.0]
    values += [1.0, 0.0, 6.5, 3.0, 2.5, 1e-3, 8.0, 0.75, 5.0, 1.5]
    buf = np.insert(np.array(values, np.float32), 0, np.zeros(3), axis=0)
    out = nd.uniform_filter(buf, 11, mode="constant", origin=5)[3:]
    return {"values": values, "means": [_f32(x) for x in out]}


def superflux() -> list[int]:
    """madmom's superflux: a 3-bin maximum filter (scipy, reflect at the edges) of the frame
    before, positive differences, np.sum over each frame in float32."""
    import scipy.ndimage as nd

    spec = np.array(
        [
            [(((f * 1031 + b * 7919) % 997) / 997.0) ** 3 * 50 for b in range(1024)]
            for f in range(4)
        ]
    ).astype(np.float32)
    spec[2, :3] = [60, 0, 5]
    spec[2, -3:] = [1, 70, 2]
    mx = nd.maximum_filter(spec, size=(1, 3))
    diff = np.zeros_like(spec)
    diff[1:] = spec[1:] - mx[:-1]
    np.maximum(diff, 0, out=diff)
    return [_f32(x) for x in np.sum(diff, axis=1)]


def combine_events() -> dict[str, Any]:
    """madmom.utils.combine_events ('left'): an event within delta (plus 1e-12) of the last kept
    one is dropped; onsets 30 ms apart as frame / 100 combine."""
    from madmom.utils import combine_events as combine

    frames = [64, 65, 66, 67, 83, 86, 99, 102, 115]
    events = np.array(frames) / 100.0
    kept = combine(events, 0.03, "left")
    return {"frames": frames, "kept": [_f64(x) for x in kept]}


def complex_phase() -> dict[str, Any]:
    """The phase check's numpy complex arithmetic: exp(2j * pi * x / period) (the division as
    a complex: times the reciprocal), the complex mean (pairwise over pairs, times 1 / n), abs
    (C hypot) and angle (atan2)."""
    xs = [-3.7, -0.123456789, 0.0, 0.5, 1.75, 2.333333, 7.9]
    period = 0.4637
    z = np.exp(2j * np.pi * np.array(xs) / period)
    means = []
    for n in [3, 7]:
        m = z[:n].mean()
        means.append(
            [n, _f64(m.real), _f64(m.imag), _f64(abs(m)), _f64(np.angle(m))]
        )
    x40 = np.arange(40) * 0.37
    m40 = np.exp(2j * np.pi * x40 / period).mean()
    return {
        "xs": xs,
        "period": period,
        "units": [[_f64(v.real), _f64(v.imag)] for v in z],
        "means": means,
        "mean40": [_f64(m40.real), _f64(m40.imag)],
    }


def hanning() -> dict[str, Any]:
    """np.hanning(m): 0.5 + 0.5 cos(pi n / (m - 1)) for n = 1 - m, 3 - m, ..."""
    w8 = np.hanning(8)
    w = np.hanning(2048)
    idx = [0, 1, 512, 1023, 1024, 2047]
    return {
        "m8": [_f64(x) for x in w8],
        "m2048": [[i, _f64(w[i])] for i in idx],
    }


def log_densities() -> dict[str, Any]:
    """madmom's RNNBeatTrackingObservationModel.log_densities for float32 activations
    (lambda 16): log((1 - a) / 15) and log(a) in float32, stored as float64; the forward
    algorithm's densities are their exp in float64."""
    acts = np.array([1e-7, 0.01, 0.25, 0.5, 0.75, 0.999, 1.0, 0.0], np.float32)
    non = np.log((1.0 - acts) / (16 - 1)).astype(np.float64)
    beat = np.log(acts).astype(np.float64)
    return {
        "activations": [float(a) for a in acts],
        "non_beat": [_f64(x) for x in non],
        "beat": [_f64(x) for x in beat],
        "exp_non_beat": [_f64(np.exp(x)) for x in non],
        "exp_beat": [_f64(np.exp(x)) for x in beat],
    }


def axis_mean() -> dict[str, Any]:
    """np.mean(a, axis=0) over a C-ordered 2D array (structure's frame loudness): each column's
    values added row by row in order, not pairwise, over n; then 20 log10(sqrt + 1e-6)."""
    fr = _values64(4096 * 3).reshape(4096, 3) - 0.3
    mean = np.mean(fr * fr, axis=0)
    ldb = 20 * np.log10(np.sqrt(mean) + 1e-6)
    return {
        "rows": 4096,
        "cols": 3,
        "mean": [_f64(x) for x in mean],
        "loudness_db": [_f64(x) for x in ldb],
        "pairwise": [_f64(np.sum(fr[:, j] * fr[:, j]) / 4096) for j in range(3)],
    }


def correlation() -> dict[str, Any]:
    """structure's keys: np.corrcoef (cov's dot product is a BLAS dgemm, two FMA accumulators
    over alternate elements on Accelerate), the even-length np.median of the change threshold, and
    _key_name over chroma-like vectors."""
    from . import structure

    v = _values64(12 * 40).reshape(40, 12)
    cases = []
    for k in range(40):
        profile = structure.MAJOR if k % 2 == 0 else structure.MINOR
        r = (k * 5) % 12
        cases.append(
            [k, r, _f64(np.corrcoef(v[k], np.roll(profile, r))[0, 1])]
        )
    medians = [
        [n, _f64(np.median(_values64(n)))] for n in (20, 21, 600, 1199, 1200)
    ]
    return {
        "corrcoef": cases,
        "keys": [structure._key_name(v[k]) for k in range(40)],
        "medians": medians,
    }


def rounding() -> list[Any]:
    """Python's round(x): to the nearest integer, ties to even (frame times, window bounds)."""
    xs = [0.5, 1.5, 2.5, -0.5, -1.5, 3.4999999, 1199.5, 44100.5, 0.67 * 100, 1.3 * 44100]
    return [[x, round(x)] for x in xs]


def probe() -> dict[str, Any]:
    import madmom
    import scipy

    return {
        "versions": {
            "numpy": np.__version__,
            "scipy": scipy.__version__,
            "madmom": madmom.__version__,
        },
        "sums": sums(),
        "percentiles": percentiles(),
        "strong_flags": strong_flags(),
        "uniform_filter": uniform_filter(),
        "superflux": superflux(),
        "combine_events": combine_events(),
        "complex_phase": complex_phase(),
        "hanning": hanning(),
        "log_densities": log_densities(),
        "rounding": rounding(),
        "axis_mean": axis_mean(),
        "correlation": correlation(),
    }


def main(argv: list[str]) -> int:
    with np.errstate(divide="ignore"):
        out = probe()
    text = json.dumps(out, indent=1) + "\n"
    if argv:
        Path(argv[0]).write_text(text)
    else:
        sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
