"""`export-models`: madmom's models and golden data for the native analyzer (docs/design/native-app.md, F3).

Writes to `native/models/`:

- `madmom.bin`: the online and offline beat ensembles, the online and offline
  DBNs' state spaces and transition models for the stream's parameters (CSR
  arrays; `num_tempi` unset models every whole-frame interval), the front ends'
  filterbanks, windows and difference frames, the onsets' parameters, and
  librosa's chroma, mel and DCT matrices that `structure.py`'s features use.
- `golden/<excerpt>.bin`: for stage parity, Python's own outputs on a 12 s
  excerpt: decoded PCM, frames as madmom cuts them, online features,
  activations, most likely states and beliefs, ticks (evidence, energy, level,
  key), committed beats, the onset function and onsets, the offline decode of
  the excerpt, and the structure features per tick.
- `golden/orpheus-galop-hindsight.bin`: 30 s of the Galop's hindsight entries
  (20 s windows sliding past 20 s) and committed beats with and without the
  phase check.
- `manifest.json`: the versions, the stream's parameters and the excerpts.

Each `.bin` is a little-endian u32 header length, a JSON header naming each
tensor's dtype (f32, f64 or i32), shape and byte offset, then the data. The
DBNs' probabilities are f64, as madmom computes them, so the Rust port can
match its decode exactly.
"""

from __future__ import annotations

import json
import struct
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np

from . import stream as st
from . import structure

DTYPES = {"f32": "<f4", "f64": "<f8", "i32": "<i4"}
HOP = st.SR / st.FPS
#: librosa's MFCC count in `structure.features` and its mel bands (librosa's default).
N_MFCC = 13
N_MELS = 128


class Writer:
    """Tensors for one file, in the order they are added."""

    def __init__(self) -> None:
        self.tensors: dict[str, dict[str, Any]] = {}
        self.chunks: list[bytes] = []
        self.offset = 0

    def add(self, name: str, a: Any, dtype: str = "f32") -> None:
        if name in self.tensors:
            raise ValueError(f"duplicate tensor {name}")
        arr = np.ascontiguousarray(a, dtype=DTYPES[dtype])
        data = arr.tobytes()
        self.tensors[name] = {
            "dtype": dtype,
            "shape": list(arr.shape),
            "offset": self.offset,
        }
        self.chunks.append(data)
        self.offset += len(data)

    def write(self, path: Path) -> None:
        header = json.dumps(self.tensors, separators=(",", ":")).encode()
        path.parent.mkdir(parents=True, exist_ok=True)
        with open(path, "wb") as f:
            f.write(struct.pack("<I", len(header)))
            f.write(header)
            f.writelines(self.chunks)


def read(path: Path) -> dict[str, np.ndarray]:
    """Every tensor of a file, by name."""
    b = Path(path).read_bytes()
    n = struct.unpack("<I", b[:4])[0]
    header = json.loads(b[4 : 4 + n])
    data = b[4 + n :]
    out = {}
    for name, e in header.items():
        dt = np.dtype(DTYPES[e["dtype"]])
        count = int(np.prod(e["shape"])) if e["shape"] else 1
        out[name] = np.frombuffer(
            data, dtype=dt, count=count, offset=e["offset"]
        ).reshape(e["shape"])
    return out


# --- models ------------------------------------------------------------------


def _lstm(w: Writer, prefix: str, layer: Any) -> None:
    for gate in ("input_gate", "forget_gate", "cell", "output_gate"):
        g = getattr(layer, gate)
        w.add(f"{prefix}.{gate}.w", g.weights)
        w.add(f"{prefix}.{gate}.r", g.recurrent_weights)
        w.add(f"{prefix}.{gate}.b", g.bias)
        if g.peephole_weights is not None:
            w.add(f"{prefix}.{gate}.p", g.peephole_weights)


def _nets(w: Writer, prefix: str, files: list[str]) -> None:
    from madmom.ml.nn import NeuralNetwork

    for i, f in enumerate(files):
        nn = NeuralNetwork.load(f)
        for j, layer in enumerate(nn.layers):
            p = f"{prefix}.{i}.{j}"
            name = type(layer).__name__
            if name == "LSTMLayer":
                _lstm(w, p, layer)
            elif name == "BidirectionalLayer":
                _lstm(w, p + ".fwd", layer.fwd_layer)
                _lstm(w, p + ".bwd", layer.bwd_layer)
            elif name == "FeedForwardLayer":
                w.add(p + ".ff.w", layer.weights)
                w.add(p + ".ff.b", layer.bias)
            else:
                raise SystemExit(f"unexpected layer {name} in {f}")


def _front_end(w: Writer, prefix: str, frame_size: int, bands: int) -> None:
    from madmom.audio.filters import LogarithmicFilterbank
    from madmom.audio.spectrogram import _diff_frames
    from madmom.audio.stft import fft_frequencies

    freqs = fft_frequencies(frame_size // 2, st.SR)
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


def _dbn(w: Writer, prefix: str, d: Any) -> None:
    tm = d.tm
    w.add(f"{prefix}.indptr", tm.pointers, "i32")
    w.add(f"{prefix}.prev", tm.states, "i32")
    w.add(f"{prefix}.prob", tm.probabilities, "f64")
    w.add(f"{prefix}.log_prob", tm.log_probabilities, "f64")
    w.add(f"{prefix}.om_pointers", d.om.pointers, "i32")
    w.add(f"{prefix}.positions", d.st.state_positions, "f64")
    w.add(f"{prefix}.intervals", d.st.state_intervals, "i32")
    w.add(f"{prefix}.observation_lambda", [float(d.om.observation_lambda)], "f64")


def dbns(p: st.Params) -> tuple[Any, Any]:
    """The online and offline beat trackers the stream uses (online: the forward beliefs;
    offline: each hindsight window's decode)."""
    from madmom.features.beats import DBNBeatTrackingProcessor

    online = DBNBeatTrackingProcessor(
        fps=st.FPS,
        min_bpm=p.min_bpm,
        max_bpm=p.max_bpm,
        num_tempi=p.num_tempi,
        online=True,
    )
    offline = DBNBeatTrackingProcessor(fps=st.FPS, min_bpm=p.min_bpm, max_bpm=p.max_bpm)
    return online, offline


def librosa_matrices() -> dict[str, np.ndarray]:
    """The chroma and mel filterbanks and the DCT behind `structure.features`, at its frame size."""
    import librosa
    import scipy.fft

    n_fft = structure.N_FFT
    return {
        "chroma": librosa.filters.chroma(sr=st.SR, n_fft=n_fft, tuning=0.0),
        "mel": librosa.filters.mel(sr=st.SR, n_fft=n_fft, n_mels=N_MELS),
        "dct": scipy.fft.dct(np.eye(N_MELS), type=2, norm="ortho", axis=0)[:N_MFCC],
    }


def models(p: st.Params) -> Writer:
    from madmom.models import BEATS_BLSTM, BEATS_LSTM

    w = Writer()
    _front_end(w, "online", 2048, 12)
    for fs in (1024, 2048, 4096):
        _front_end(w, "offline", fs, 6)
    _nets(w, "online", BEATS_LSTM)
    _nets(w, "offline", BEATS_BLSTM)
    online, offline = dbns(p)
    _dbn(w, "dbn_online", online)
    _dbn(w, "dbn_offline", offline)
    # Onsets: superflux on the online magnitude spectrogram (the online front end's window and
    # difference frames), its maximum filter over this many bins.
    w.add("onsets.diff_max_bins", [3], "i32")
    for name, m in librosa_matrices().items():
        # (librosa applies its DCT in f64, its filterbanks in f32.)
        w.add(f"librosa.{name}", m, "f64" if name == "dct" else "f32")
    return w


# --- golden data ---------------------------------------------------------------


@dataclass(frozen=True)
class Excerpt:
    name: str
    #: The audio, relative to the repository root.
    audio: str
    start_s: float
    seconds: float = 12.0


#: A steady synthetic track, and the Galop from its vamp (43.2-54.1 s) into the first
#: fortissimo tutti, where the online period flips between levels.
EXCERPTS = (
    Excerpt("house-128", "public/reactive/house-128/house-128.ogg", 0.0),
    Excerpt("orpheus-galop", "public/audio/orpheus-galop.flac", 44.0),
)
#: Hindsight needs more than a 20 s window, so its windows slide: 30 s of the Galop (60-90 s),
#: where the phase check moves the committed grid.
HINDSIGHT = Excerpt(
    "orpheus-galop-hindsight", "public/audio/orpheus-galop.flac", 60.0, 30.0
)
#: Integer codes for the golden data's levels and keys (-1: no key yet).
LEVELS = ("quiet", "medium", "loud")
KEYS = tuple(f"{n} major" for n in structure.MAJOR_NAMES) + tuple(
    f"{n} minor" for n in structure.MINOR_NAMES
)


@dataclass
class Ticks:
    times: np.ndarray
    evidence: list[float]
    energy: np.ndarray

    def evidence_of_frame(self, n: int) -> float:
        k = n // st.TICK_FRAMES
        return self.evidence[k - 1] if 1 <= k <= len(self.evidence) else 0.0


def ticks(y: np.ndarray, act: np.ndarray, beliefs: st.Beliefs, p: st.Params) -> Ticks:
    """Each tick's time, evidence and energy, as `stream.build_stream` computes them."""
    n_ticks = int(np.floor(len(y) / st.SR * st.FPS / st.TICK_FRAMES + 1e-9))
    tick_frames = [st.TICK_FRAMES * k for k in range(1, n_ticks + 1)]
    times = np.array([f / st.FPS for f in tick_frames])
    evidence = [
        st.evidence_at(
            act,
            beliefs.last_beat(min(f, len(act) - 1)),
            float(beliefs.period[min(f, len(act) - 1)]),
            f / st.FPS,
            p,
        )
        for f in tick_frames
    ]
    return Ticks(times, evidence, st.energies(y, times, p))


def _commits(g: Writer, prefix: str, beats: list[dict[str, float]]) -> None:
    for key in ("t", "knownAt", "period", "evidence"):
        g.add(f"{prefix}.{key}", [b[key] for b in beats], "f64")


def _ragged(g: Writer, name: str, lists: list[list[float]]) -> None:
    """A list of lists as its concatenation and CSR-style pointers (len + 1)."""
    g.add(name, [x for v in lists for x in v], "f64")
    g.add(f"{name}_ptr", np.cumsum([0, *[len(v) for v in lists]]), "i32")


def golden(y: np.ndarray, p: st.Params) -> Writer:
    """Python's own output at each stage, on mono 44.1 kHz samples."""
    from madmom.audio.signal import FramedSignal, Signal
    from madmom.features.beats import RNNBeatProcessor
    from madmom.features.onsets import SpectralOnsetProcessor

    g = Writer()
    g.add("pcm", y)
    sig = Signal(y, sample_rate=st.SR, num_channels=1)
    # Online: framing, features, activations, states and beliefs.
    fr = FramedSignal(sig, frame_size=2048, hop_size=HOP, origin="online")
    # (Frame 0 is mostly padding; frame 60 lies inside both excerpts' audio.)
    g.add("online.frame0", fr[0])
    g.add("online.frame60", fr[60])
    rnn = RNNBeatProcessor(online=True, origin="online", fps=st.FPS)
    g.add("online.features", rnn.processors[0](sig))
    act = np.asarray(rnn(sig))
    g.add("online.activations", act)
    online, offline = dbns(p)
    online.hmm.reset()
    g.add(
        "online.states", np.argmax(online.hmm.forward(act, reset=False), axis=1), "i32"
    )
    beliefs = st.forward_beliefs(act, p)
    g.add("online.since", beliefs.since, "f64")
    g.add("online.period", beliefs.period, "f64")
    # Ticks, then the committed beats with the evidence they carry (no hindsight phase check).
    tk = ticks(y, act, beliefs, p)
    g.add("ticks.t", tk.times, "f64")
    g.add("ticks.evidence", tk.evidence, "f64")
    g.add("ticks.energy", tk.energy, "f64")
    _commits(g, "commits", st.commit_beats(beliefs, tk.evidence_of_frame, p))
    # Onsets: the detection function and the picked onsets.
    odf = SpectralOnsetProcessor(onset_method="superflux", fps=st.FPS, origin="online")(
        sig
    )
    g.add("onsets.odf", np.asarray(odf))
    onsets = st.detect_onsets(y, p)
    for key in ("t", "knownAt", "strength"):
        g.add(f"onsets.{key}", [o[key] for o in onsets], "f64")
    g.add("onsets.strong", [int(o["strong"]) for o in onsets], "i32")
    # Offline: the whole excerpt as one hindsight window.
    rnn_off = RNNBeatProcessor(fps=st.FPS)
    g.add("offline.features", rnn_off.processors[0](sig))
    act_off = np.asarray(rnn_off(sig))
    g.add("offline.activations", act_off)
    g.add("offline.beats", offline(act_off), "f64")
    # Structure's features per tick (librosa's chroma and MFCCs, loudness).
    hop = st.SR * st.TICK_FRAMES // st.FPS
    chroma, feats, ldb = structure.features(y, len(tk.times), hop, st.SR)
    g.add("structure.chroma", chroma, "f64")
    g.add("structure.features", feats, "f64")
    g.add("structure.loudness_db", ldb, "f64")
    # Each tick's level (hysteresis on the energy) and key, as codes into LEVELS and KEYS.
    g.add(
        "ticks.level", [LEVELS.index(v) for v in structure.levels(tk.energy, p)], "i32"
    )
    keys = structure.keys(chroma, tk.times, p, st.SR / hop)
    g.add("ticks.key", [-1 if k is None else KEYS.index(k) for k in keys], "i32")
    return g


def hindsight_golden(y: np.ndarray, p: st.Params) -> Writer:
    """Hindsight entries (sliding 20 s windows past 20 s) and the committed beats with the phase
    check, with the online beliefs and ticks they are computed from."""
    g = Writer()
    g.add("pcm", y)
    act = st.activations(y)
    g.add("online.activations", act)
    beliefs = st.forward_beliefs(act, p)
    g.add("online.since", beliefs.since, "f64")
    g.add("online.period", beliefs.period, "f64")
    tk = ticks(y, act, beliefs, p)
    g.add("ticks.evidence", tk.evidence, "f64")
    entries = st.hindsight_entries(y, beliefs, p)
    g.add("hindsight.knownAt", [e["knownAt"] for e in entries], "f64")
    _ragged(g, "hindsight.offline", [e["offline"] for e in entries])
    _ragged(g, "hindsight.online", [e["online"] for e in entries])
    _commits(g, "commits", st.commit_beats(beliefs, tk.evidence_of_frame, p, entries))
    _commits(g, "commits_unphased", st.commit_beats(beliefs, tk.evidence_of_frame, p))
    return g


def excerpt_samples(root: Path, e: Excerpt) -> np.ndarray:
    y, _ = st.load_audio(root / e.audio)
    i = round(e.start_s * st.SR)
    return np.ascontiguousarray(y[i : i + round(e.seconds * st.SR)])


def manifest(p: st.Params, excerpts: tuple[Excerpt, ...]) -> dict[str, Any]:
    from importlib.metadata import version

    online, _ = dbns(p)
    return {
        "format": "u32 header length, JSON header {name: {dtype, shape, offset}}, little-endian data",
        "versions": {
            k: version(k) for k in ("madmom", "librosa", "numpy", "scipy", "soundfile")
        },
        "streamParams": p.to_json(),
        "models": "madmom.bin",
        "golden": {
            e.name: {
                "file": f"golden/{e.name}.bin",
                "audio": e.audio,
                "startS": e.start_s,
                "seconds": e.seconds,
            }
            for e in (*excerpts, HINDSIGHT)
        },
        "codes": {"level": list(LEVELS), "key": list(KEYS)},
        # Parameters that live in code rather than in the tensors.
        "dbn": {
            "correct": online.correct,
            "threshold": online.threshold,
            "transitionLambda": online.tm.transition_lambda,
            "observationLambda": int(online.om.observation_lambda),
        },
        "onsets": {
            "method": "superflux on the online magnitude spectrogram (2048 frames, no filterbank or log)",
            "diffMaxBins": 3,
            "peakPicking": "stream.detect_onsets: threshold onset_threshold, pre_max 0.03, pre_avg 0.1, combine 0.03, online",
        },
        "licence": "madmom's models: CC BY-NC-SA 4.0 (the app runs only on the developer's machine)",
    }


def export(
    root: Path, out: Path, excerpts: tuple[Excerpt, ...] = EXCERPTS
) -> dict[str, Any]:
    """Writes the models, the golden data and the manifest; returns a summary."""
    p = st.Params()
    m = models(p)
    m.write(out / "madmom.bin")
    files = {"madmom.bin": len(m.tensors)}
    for e in excerpts:
        g = golden(excerpt_samples(root, e), p)
        g.write(out / "golden" / f"{e.name}.bin")
        files[f"golden/{e.name}.bin"] = len(g.tensors)
    h = hindsight_golden(excerpt_samples(root, HINDSIGHT), p)
    h.write(out / "golden" / f"{HINDSIGHT.name}.bin")
    files[f"golden/{HINDSIGHT.name}.bin"] = len(h.tensors)
    (out / "manifest.json").write_text(
        json.dumps(manifest(p, excerpts), indent=1) + "\n"
    )
    return {
        "out": str(out),
        "tensors": files,
        "bytes": {f: (out / f).stat().st_size for f in files},
    }
