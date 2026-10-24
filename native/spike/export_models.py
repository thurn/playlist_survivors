"""Exports what the spike's analyzer needs from madmom, and golden data to check it.

Run with the analysis tool's environment (from the repository root):

    tools/analysis/.venv/bin/python native/spike/export_models.py native/spike/models

Writes `models.bin` (every tensor) and `golden.bin` (a 12 s excerpt of
house-128: PCM, online features, activations and forward states; a 20 s window's
offline activations and beats). Both are one file each: a little-endian u32
header length, a JSON header naming each tensor's dtype (f32, f64 or i32), shape and
byte offset, then the data. Phase 1's `export-models` replaces this.
"""

from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/analysis/src"))

from madmom.audio.signal import FramedSignal, Signal  # noqa: E402
from madmom.audio.spectrogram import _diff_frames  # noqa: E402
from madmom.audio.filters import LogarithmicFilterbank  # noqa: E402
from madmom.audio.stft import fft_frequencies  # noqa: E402
from madmom.features.beats import DBNBeatTrackingProcessor, RNNBeatProcessor  # noqa: E402
from madmom.ml.nn import NeuralNetwork  # noqa: E402
from madmom.models import BEATS_BLSTM, BEATS_LSTM  # noqa: E402

from ps_analysis import stream  # noqa: E402

SR = 44100
FPS = 100
HOP = SR / FPS


class Writer:
    def __init__(self) -> None:
        self.tensors: dict[str, dict] = {}
        self.chunks: list[bytes] = []
        self.offset = 0

    def add(self, name: str, a, dtype: str = "f32") -> None:
        arr = np.ascontiguousarray(
            a, dtype={"f32": "<f4", "f64": "<f8", "i32": "<i4"}[dtype]
        )
        data = arr.tobytes()
        self.tensors[name] = {
            "dtype": dtype,
            "shape": list(arr.shape),
            "offset": self.offset,
        }
        self.chunks.append(data)
        self.offset += len(data)

    def scalar(self, name: str, v: float) -> None:
        self.add(name, np.asarray([v]))

    def write(self, path: Path) -> None:
        header = json.dumps(self.tensors).encode()
        with open(path, "wb") as f:
            f.write(struct.pack("<I", len(header)))
            f.write(header)
            for c in self.chunks:
                f.write(c)


def lstm(w: Writer, prefix: str, layer) -> None:
    for gate in ("input_gate", "forget_gate", "cell", "output_gate"):
        g = getattr(layer, gate)
        w.add(f"{prefix}.{gate}.w", g.weights)
        w.add(f"{prefix}.{gate}.r", g.recurrent_weights)
        w.add(f"{prefix}.{gate}.b", g.bias)
        if g.peephole_weights is not None:
            w.add(f"{prefix}.{gate}.p", g.peephole_weights)


def nets(w: Writer, prefix: str, files) -> None:
    for i, f in enumerate(files):
        nn = NeuralNetwork.load(f)
        for j, layer in enumerate(nn.layers):
            p = f"{prefix}.{i}.{j}"
            name = type(layer).__name__
            if name == "LSTMLayer":
                lstm(w, p, layer)
            elif name == "BidirectionalLayer":
                lstm(w, p + ".fwd", layer.fwd_layer)
                lstm(w, p + ".bwd", layer.bwd_layer)
            elif name == "FeedForwardLayer":
                w.add(p + ".ff.w", layer.weights)
                w.add(p + ".ff.b", layer.bias)
            else:
                raise SystemExit(f"unexpected layer {name}")


def front_end(w: Writer, prefix: str, frame_size: int, bands: int) -> None:
    freqs = fft_frequencies(frame_size // 2, SR)
    fb = LogarithmicFilterbank(
        freqs, num_bands=bands, fmin=30, fmax=17000, norm_filters=True
    )
    w.add(f"{prefix}.{frame_size}.filterbank", np.asarray(fb))
    w.add(f"{prefix}.{frame_size}.window", np.hanning(frame_size))
    w.add(
        f"{prefix}.{frame_size}.diff_frames",
        [_diff_frames(0.5, HOP, frame_size)],
        "i32",
    )


def dbn(w: Writer, prefix: str, d: DBNBeatTrackingProcessor) -> None:
    tm = d.tm
    w.add(f"{prefix}.indptr", tm.pointers, "i32")
    w.add(f"{prefix}.prev", tm.states, "i32")
    w.add(f"{prefix}.prob", tm.probabilities, "f64")
    w.add(f"{prefix}.log_prob", tm.log_probabilities, "f64")
    w.add(f"{prefix}.om_pointers", d.om.pointers, "i32")
    w.add(f"{prefix}.positions", d.st.state_positions)
    w.add(f"{prefix}.intervals", d.st.state_intervals, "i32")
    w.scalar(f"{prefix}.observation_lambda", float(d.om.observation_lambda))


def main() -> None:
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    p = stream.Params()

    w = Writer()
    front_end(w, "online", 2048, 12)
    for fs in (1024, 2048, 4096):
        front_end(w, "offline", fs, 6)
    nets(w, "online", BEATS_LSTM)
    nets(w, "offline", BEATS_BLSTM)
    on = DBNBeatTrackingProcessor(
        fps=FPS, min_bpm=p.min_bpm, max_bpm=p.max_bpm, num_tempi=p.num_tempi, online=True
    )
    off = DBNBeatTrackingProcessor(fps=FPS, min_bpm=p.min_bpm, max_bpm=p.max_bpm)
    dbn(w, "dbn_online", on)
    dbn(w, "dbn_offline", off)
    w.write(out / "models.bin")

    # Golden data on house-128.
    root = Path(__file__).resolve().parents[2]
    y, _ = stream.load_audio(root / "public/reactive/house-128/house-128.ogg")
    g = Writer()
    ex = y[: 12 * SR]
    g.add("online.pcm", ex)
    sig = Signal(ex, sample_rate=SR, num_channels=1)
    rnn = RNNBeatProcessor(online=True, origin="online", fps=FPS)
    feats = rnn.processors[0](sig)
    g.add("online.features", feats)
    act = np.asarray(rnn(sig))
    g.add("online.activations", act)
    on.hmm.reset()
    fwd = on.hmm.forward(act, reset=False)
    g.add("online.states", np.argmax(fwd, axis=1), "i32")
    beliefs = stream.forward_beliefs(act, p)
    committed = stream.commit_beats(beliefs, lambda n: 0.0, p)
    g.add("online.commit_t", [b["t"] for b in committed])
    g.add("online.commit_known", [b["knownAt"] for b in committed])
    g.add("online.commit_period", [b["period"] for b in committed])
    # Frames as madmom cuts them, to pin the framing origin.
    fr = FramedSignal(sig, frame_size=2048, hop_size=HOP, origin="online")
    g.add("online.frame5", fr[5])

    win = y[10 * SR : 30 * SR]
    g.add("offline.pcm", win)
    rnn_off = RNNBeatProcessor(fps=FPS)
    sig = Signal(win, sample_rate=SR, num_channels=1)
    g.add("offline.features", rnn_off.processors[0](sig))
    act_off = np.asarray(rnn_off(sig))
    g.add("offline.activations", act_off)
    g.add("offline.beats", off(act_off))
    g.write(out / "golden.bin")
    print("wrote", out / "models.bin", out / "golden.bin")


if __name__ == "__main__":
    main()
