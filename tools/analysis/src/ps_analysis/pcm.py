"""Writes a track's analysed samples (stream.load_audio: mono float32 at 44.1 kHz, as the stream
was built from) as raw little-endian float32, for the native analyzer's corpus parity.

    uv run --project tools/analysis python -m ps_analysis.pcm AUDIO OUT.f32
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

import numpy as np

from . import stream


def write(audio: str | Path, out: str | Path) -> int:
    y, _ = stream.load_audio(audio)
    out = Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    # (A temporary name of this process's own, so two runs decoding one track don't collide.)
    tmp = out.with_name(f"{out.name}.{os.getpid()}.tmp")
    tmp.write_bytes(np.ascontiguousarray(y, dtype="<f4").tobytes())
    tmp.replace(out)
    return len(y)


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    print(write(argv[0], argv[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
