"""Writes native/crates/analysis/tests/fixtures/a6-forecasts.json: Python's accents.forecasts on
the committed beats and onsets of every stream under public/reactive (as stored, rounded), so the
Rust stage is compared with Python given Python's upstream.

    uv run --project tools/analysis python native/evidence/a6/fixture.py
"""

import json
from pathlib import Path

import numpy as np

from ps_analysis import accents
from ps_analysis import stream as st

ROOT = Path(__file__).resolve().parents[3]


def bits(x: float) -> int:
    return int(np.float64(x).view(np.uint64))


out = {
    "generated": "tools/analysis: accents.forecasts(beats, onsets, Params()) on each public/reactive/<track>/stream.json's stored beats and onsets",
    "tracks": {},
}
total = 0
for path in sorted((ROOT / "public/reactive").glob("*/stream.json")):
    doc = json.loads(path.read_text())
    fc = accents.forecasts(doc["beats"], doc["onsets"], st.Params())
    out["tracks"][path.parent.name] = [
        [bits(f["t"]), bits(f["knownAt"]), f["cycle"], bits(f["confidence"])]
        for f in fc
    ]
    total += len(fc)


def synthetic(name: str, period: float, n: int, hit, ahead: float = 1.2) -> None:
    """Beats every `period` s, each committed `ahead` periods early (knownAt on the 10 ms grid), and a
    strong onset wherever hit(k) gives (offset s, delay s from the beat to its knownAt)."""
    beats, onsets = [], []
    for k in range(n):
        t = round(0.3 + k * period, 3)
        beats.append(
            {
                "t": t,
                "knownAt": max(0.0, round(t - ahead * period, 2)),
                "period": period,
                "evidence": 1.0,
            }
        )
        h = hit(k)
        if h is not None:
            off, delay = h
            onsets.append(
                {
                    "t": round(t + off, 3),
                    "knownAt": round(t + off + delay, 2),
                    "strength": 10.0,
                    "strong": True,
                }
            )
        # A weak onset between beats, which never counts.
        onsets.append(
            {
                "t": round(t + period / 2, 3),
                "knownAt": round(t + period / 2 + 0.01, 2),
                "strength": 1.0,
                "strong": False,
            }
        )
    onsets.sort(key=lambda o: o["knownAt"])
    fc = accents.forecasts(beats, onsets, st.Params())
    out["synthetic"][name] = {
        "beats": beats,
        "onsets": onsets,
        "forecasts": [
            [bits(f["t"]), bits(f["knownAt"]), f["cycle"], bits(f["confidence"])]
            for f in fc
        ],
    }
    print(name, len(fc), "forecasts, cycles", sorted({f["cycle"] for f in fc}))


out["synthetic"] = {}
# Every beat: all three cycles complete together (ties broken by confidence, then cycle).
synthetic("every-beat", 0.5, 64, lambda k: (0.0, 0.01))
# Every 4th beat but one missing: runs break and restart.
synthetic(
    "fourth-with-gap",
    0.5,
    96,
    lambda k: (0.01, 0.01) if k % 4 == 0 and k != 40 else None,
)
# At the window's edges: 0.05 s off counts, 0.051 s doesn't.
synthetic(
    "window-edges", 0.5, 64, lambda k: (0.05, 0.01) if k % 2 == 0 else (-0.051, 0.01)
)
# Onsets known late (0.3 s after they sound), so they, not the beats, stamp the forecasts;
# at 0.4 s a beat the 2-beat cycle falls short of the least lead and the 4-beat cycle forecasts.
synthetic(
    "late-onsets", 0.4, 96, lambda k: (0.0, 0.3) if k % 2 == 0 else None, ahead=2.0
)


def accented(seconds: int) -> np.ndarray:
    """A 120 bpm pulse with every 4th beat accented, in plain float64 arithmetic (so the Rust
    test makes the same samples): a 441 Hz saw under a 50 ms linear decay from each beat, 0.8
    loud on accents and 0.2 otherwise, as float32."""
    sr = 44100
    n = np.arange(seconds * sr)
    k = n // 22050
    amp = np.where(k % 4 == 0, 0.8, 0.2)
    env = np.maximum(0.0, 1.0 - (n - k * 22050) / 2205.0)
    saw = (n % 100) / 100 * 2.0 - 1.0
    return (amp * env * saw).astype(np.float32)


# The whole stream (phase-checked beats, onsets, forecasts) of that pulse, for the analyzer.
pulse = st.build_stream(accented(40))
out["accented40"] = [
    [bits(f["t"]), bits(f["knownAt"]), f["cycle"], bits(f["confidence"])]
    for f in pulse["forecasts"]
]
print(
    "accented40",
    len(out["accented40"]),
    "forecasts from",
    len(pulse["beats"]),
    "beats and",
    sum(o["strong"] for o in pulse["onsets"]),
    "strong onsets",
)
dest = ROOT / "native/crates/analysis/tests/fixtures/a6-forecasts.json"
dest.write_text(json.dumps(out, separators=(",", ":")))
print(len(out["tracks"]), "tracks,", total, "forecasts")
