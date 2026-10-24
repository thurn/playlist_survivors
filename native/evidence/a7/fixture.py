"""Writes native/crates/analysis/tests/fixtures/a7-bands.json: Python's bands group (tick bands
and brightness, onset bands, timbre entries) from stream.build_stream on the golden excerpts' PCM,
with the tick and onset frames it read.

    uv run --project tools/analysis python native/evidence/a7/fixture.py
"""

import json
import struct
from pathlib import Path

import numpy as np

from ps_analysis import stream as st

ROOT = Path(__file__).resolve().parents[3]


def bits(x: float) -> int:
    return int(np.float64(x).view(np.uint64))


def golden_pcm(name: str) -> np.ndarray:
    b = (ROOT / f"native/models/golden/{name}.bin").read_bytes()
    n = struct.unpack("<I", b[:4])[0]
    e = json.loads(b[4 : 4 + n])["pcm"]
    return np.frombuffer(
        b[4 + n :], np.float32, count=e["shape"][0], offset=e["offset"]
    ).copy()


out = {"generated": "tools/analysis: stream.build_stream on golden PCM", "excerpts": {}}
for name in ("house-128", "orpheus-galop", "orpheus-galop-hindsight"):
    s = st.build_stream(golden_pcm(name))
    out["excerpts"][name] = {
        "ticks": [
            [bits(t["bands"][b]) for b in ("kick", "snare", "hat", "bass")]
            + [bits(t["brightness"])]
            for t in s["ticks"]
        ],
        "onsets": [[bits(o["knownAt"]), o["band"]] for o in s["onsets"]],
        "timbre": [
            [bits(e[k]) for k in ("knownAt", "brightness", "flatness", "percussive", "density")]
            for e in s["timbre"]
        ],
    }
    print(name, len(s["ticks"]), "ticks", len(s["onsets"]), "onsets", len(s["timbre"]), "timbre")
dest = ROOT / "native/crates/analysis/tests/fixtures/a7-bands.json"
dest.write_text(json.dumps(out, separators=(",", ":")))
