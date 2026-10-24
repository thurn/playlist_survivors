"""Writes native/crates/analysis/tests/fixtures/a5-structure.json: Python's structure.structure
(changes, jumps, ends and keys) on a synthetic piece built to have all of them, and on the
Galop's 30 s golden PCM, on the synthetic piece cut to 29.997 s (neither a whole number of
ticks nor of the analyzer's hops), and on a 150 s piece past every window the stage keeps.

    uv run --project tools/analysis python native/evidence/a5/fixture.py
"""

import json
import math
import struct
from pathlib import Path

import numpy as np

from ps_analysis import stream as st
from ps_analysis import structure

ROOT = Path(__file__).resolve().parents[3]
SR = 44100
# (from s, to s, amplitude, saw periods in samples): chords that change at 12 s and 20 s, a loud
# stretch from 20 s and near silence from 26 s.
SECTIONS = [
    (0, 12, 0.2, [169, 134, 112]),
    (12, 20, 0.2, [200, 169, 134, 100]),
    (20, 26, 0.6, [126, 100, 84]),
    (26, 32, 0.0005, [126, 100, 84]),
]


# A 150 s piece: a section every 20 s (chords, loudness), past the novelty's 68 s ring and the
# change threshold's 60 s prior, ending near silent.
LONG = [
    (0, 20, 0.2, [169, 134, 112]),
    (20, 40, 0.2, [200, 169, 134, 100]),
    (40, 60, 0.4, [126, 100, 84]),
    (60, 80, 0.2, [150, 119, 100]),
    (80, 100, 0.25, [169, 134, 112]),
    (100, 120, 0.6, [200, 159, 134]),
    (120, 140, 0.2, [126, 100, 84]),
    (140, 150, 0.0005, [126, 100, 84]),
]


def synth(sections=SECTIONS) -> np.ndarray:
    """Saw voices per section, summed in order in float64 (elementwise, as the Rust test sums
    each sample), then float32."""
    y = np.empty(SR * sections[-1][1], np.float32)
    for a, b, amp, periods in sections:
        n = np.arange(a * SR, b * SR)
        v = np.zeros(len(n))
        for p in periods:
            v = v + amp * ((n % p) / p * 2.0 - 1.0)
        y[a * SR : b * SR] = v
    return y


def golden_pcm() -> np.ndarray:
    b = (ROOT / "native/models/golden/orpheus-galop-hindsight.bin").read_bytes()
    n = struct.unpack("<I", b[:4])[0]
    e = json.loads(b[4 : 4 + n])["pcm"]
    return np.frombuffer(
        b[4 + n :], np.float32, count=e["shape"][0], offset=e["offset"]
    ).copy()


def bits(x: float) -> int:
    return int(np.float64(x).view(np.uint64))


def run(y: np.ndarray) -> dict:
    p = st.Params()
    n = math.floor(len(y) / SR * st.FPS / st.TICK_FRAMES + 1e-9)
    times = np.array([st.TICK_FRAMES * k / st.FPS for k in range(1, n + 1)])
    energy = st.energies(y, times, p)
    f = structure.structure(y, times, energy, p, SR * st.TICK_FRAMES // st.FPS, SR)
    return {
        "ticks": n,
        "changes": [
            [bits(c["t"]), bits(c["knownAt"]), bits(c["strength"])]
            for c in f["changes"]
        ],
        "jumps": [
            [bits(c["t"]), bits(c["knownAt"]), bits(c["db"])] for c in f["jumps"]
        ],
        "ends": [[bits(c["t"]), bits(c["knownAt"])] for c in f["ends"]],
        "keys": [k if k is not None else "" for k in f["keys"]],
    }


out = {
    "generated": "tools/analysis: structure.structure (with stream.energies) on the synthetic piece (sections, saw voices: amp * ((n % P) / P * 2 - 1) summed in order, float32) and on golden/orpheus-galop-hindsight.bin's pcm",
    "sections": SECTIONS,
    "synthetic": run(synth()),
    "galop30": run(golden_pcm()),
    "synthetic29997": run(synth()[: int(29.997 * SR)]),
    "long": {"sections": LONG, **run(synth(LONG))},
}
path = ROOT / "native/crates/analysis/tests/fixtures/a5-structure.json"
path.write_text(json.dumps(out))
for k in ("synthetic", "galop30", "synthetic29997", "long"):
    r = out[k]
    print(
        k,
        r["ticks"],
        "changes",
        len(r["changes"]),
        "jumps",
        len(r["jumps"]),
        "ends",
        len(r["ends"]),
    )
