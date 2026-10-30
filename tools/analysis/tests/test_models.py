from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np
import pytest

from ps_analysis import models, structure
from ps_analysis import stream as st

ROOT = Path(__file__).resolve().parents[3]
COMMITTED = ROOT / "native" / "models"


REGENERATE = "native/models is stale: run `npm run analysis -- export-models`"
# The golden data pin the arithmetic of the Mac's numpy and BLAS, for the native crate, which builds
# only on macOS; Windows' BLAS differs in the last bits (Cygnus's slow CI skips the native crate too).
macos_arithmetic = pytest.mark.skipif(
    sys.platform != "darwin", reason="pins macOS's BLAS arithmetic for the native crate"
)


def assert_same_tensors(a: dict[str, np.ndarray], b: dict[str, np.ndarray]) -> None:
    assert a.keys() == b.keys(), REGENERATE
    for name, x in a.items():
        assert x.shape == b[name].shape, f"{name}: {REGENERATE}"
        if x.dtype.kind == "i":
            assert np.array_equal(x, b[name]), f"{name}: {REGENERATE}"
        else:
            # (The design's stage-parity floor: another BLAS or decoder may differ in the last bits.)
            atol = 1e-7 if x.dtype == np.float32 else 1e-9
            assert np.allclose(x, b[name], rtol=1e-6, atol=atol), (
                f"{name}: {REGENERATE}"
            )


def test_tensors_round_trip(tmp_path: Path) -> None:
    w = models.Writer()
    w.add("a", np.arange(6).reshape(2, 3))
    w.add("b", [1.5, -2.25], "f64")
    w.add("c", [7], "i32")
    w.write(tmp_path / "t.bin")
    got = models.read(tmp_path / "t.bin")
    assert got["a"].dtype == np.float32 and got["a"].shape == (2, 3)
    assert np.array_equal(got["a"], np.arange(6).reshape(2, 3))
    assert got["b"].dtype == np.float64 and list(got["b"]) == [1.5, -2.25]
    assert got["c"].dtype == np.int32 and list(got["c"]) == [7]
    with pytest.raises(ValueError):
        w.add("a", [0.0])


def test_librosa_matrices_reproduce_structures_features() -> None:
    """The exported matrices give librosa's chroma, mel and MFCCs on a power spectrum."""
    import librosa

    m = models.librosa_matrices()
    rng = np.random.default_rng(1)
    spec = rng.random((structure.N_FFT // 2 + 1, 5)) ** 4
    mel = librosa.feature.melspectrogram(S=spec, sr=st.SR, n_fft=structure.N_FFT)
    assert np.allclose(m["mel"] @ spec, mel, rtol=1e-5)
    mfcc = librosa.feature.mfcc(
        S=librosa.power_to_db(mel, top_db=None), sr=st.SR, n_mfcc=models.N_MFCC
    )
    ours = m["dct"] @ librosa.power_to_db(m["mel"] @ spec, top_db=None)
    assert np.allclose(ours, mfcc, rtol=1e-5, atol=1e-4)
    chroma = librosa.feature.chroma_stft(
        S=spec, sr=st.SR, n_fft=structure.N_FFT, tuning=0.0
    )
    raw = m["chroma"] @ spec
    assert np.allclose(raw / np.abs(raw).max(axis=0), chroma, rtol=1e-5)


def test_committed_models_match_a_fresh_export(tmp_path: Path) -> None:
    """native/models/madmom.bin is what export-models writes for the stream's parameters."""
    w = models.models(st.Params())
    w.write(tmp_path / "madmom.bin")
    assert_same_tensors(
        models.read(COMMITTED / "madmom.bin"), models.read(tmp_path / "madmom.bin")
    )


@macos_arithmetic
@pytest.mark.parametrize("excerpt", models.EXCERPTS, ids=lambda e: e.name)
def test_committed_golden_data_match_a_fresh_export(
    excerpt: models.Excerpt, tmp_path: Path
) -> None:
    g = models.golden(models.excerpt_samples(ROOT, excerpt), st.Params())
    g.write(tmp_path / "g.bin")
    assert_same_tensors(
        models.read(COMMITTED / "golden" / f"{excerpt.name}.bin"),
        models.read(tmp_path / "g.bin"),
    )


def test_committed_hindsight_golden_holds_sliding_windows_and_phase_shifts() -> None:
    """(Re-exporting it takes 30 decodes, about 20 s, so its freshness rests on the manifest's
    versions and parameters, checked below, and on export-models being rerun with them.)"""
    h = models.read(COMMITTED / "golden" / f"{models.HINDSIGHT.name}.bin")
    # Windows slide past 20 s, and the phase check moves some committed beats.
    assert h["hindsight.knownAt"][-1] > 25
    assert len(h["hindsight.offline_ptr"]) == len(h["hindsight.knownAt"]) + 1
    phased, plain = h["commits.t"], h["commits_unphased.t"]
    assert len(phased) != len(plain) or not np.allclose(phased, plain)


def test_exported_front_ends_and_dbn_reproduce_madmom() -> None:
    """madmom.bin's tensors rebuild the golden online features, onset function and most likely
    states from the golden PCM, so they are what madmom used (not just what the exporter wrote)."""
    from madmom.audio.signal import FramedSignal, Signal
    from scipy.ndimage import maximum_filter
    from scipy.sparse import csr_matrix

    m = models.read(COMMITTED / "madmom.bin")
    g = models.read(COMMITTED / "golden" / "orpheus-galop.bin")
    sig = Signal(g["pcm"], sample_rate=st.SR, num_channels=1)
    fr = np.asarray(
        FramedSignal(sig, frame_size=2048, hop_size=models.HOP, origin="online")
    )
    assert np.array_equal(fr[60], g["online.frame60"])
    mag = np.abs(np.fft.rfft(fr * m["online.2048.window"], axis=1))[:, :1024]
    d = int(m["online.2048.diff_frames"][0])
    logf = np.log10(mag @ m["online.2048.filterbank"] + 1)
    diff = np.zeros_like(logf)
    diff[d:] = logf[d:] - logf[:-d]
    feats = np.hstack([logf, np.maximum(diff, 0)])
    assert np.allclose(feats, g["online.features"], rtol=1e-5, atol=1e-6)
    # Superflux: the difference from the earlier frame's maximum over neighbouring bins.
    bins = int(m["onsets.diff_max_bins"][0])
    mx = maximum_filter(mag, size=(1, bins))
    flux = np.zeros_like(mag)
    flux[d:] = mag[d:] - mx[:-d]
    odf = np.maximum(flux, 0).sum(axis=1)
    assert np.allclose(odf, g["onsets.odf"], rtol=1e-5, atol=1e-5)
    # The online DBN's forward algorithm on the golden activations.
    n = len(m["dbn_online.indptr"]) - 1
    trans = csr_matrix(
        (m["dbn_online.prob"], m["dbn_online.prev"], m["dbn_online.indptr"]),
        shape=(n, n),
    )
    lam = m["dbn_online.observation_lambda"][0]
    beat = m["dbn_online.om_pointers"] == 1
    fwd = np.full(n, 1.0 / n)
    states = []
    for a in g["online.activations"].astype(np.float64):
        fwd = (trans @ fwd) * np.where(beat, a, (1 - a) / (lam - 1))
        fwd /= fwd.sum()
        states.append(int(np.argmax(fwd)))
    assert np.array_equal(states, g["online.states"])


def test_golden_data_hold_each_stage() -> None:
    g = models.read(COMMITTED / "golden" / "house-128.bin")
    n = len(g["online.activations"])
    assert len(g["pcm"]) == 12 * st.SR
    assert n == 12 * st.FPS
    for key in ("online.states", "online.since", "online.period"):
        assert len(g[key]) == n, key
    assert g["online.features"].shape[0] == n
    assert len(g["ticks.t"]) == 12 * st.FPS // st.TICK_FRAMES
    assert g["structure.features"].shape == (len(g["ticks.t"]), 25)
    # A steady 128 bpm track: committed beats and offline beats about 0.469 s apart.
    assert np.allclose(np.median(np.diff(g["commits.t"])), 60 / 128, atol=0.01)
    assert np.allclose(np.median(np.diff(g["offline.beats"])), 60 / 128, atol=0.01)
    assert len(g["onsets.t"]) == len(g["onsets.strong"]) > 0
    assert set(g["ticks.level"]) <= {0, 1, 2}
    assert len(g["ticks.key"]) == len(g["ticks.t"])
    # Keys start after the stream's key_after_s.
    assert (g["ticks.key"][g["ticks.t"] < st.Params().key_after_s] == -1).all()


def test_manifest_names_every_file() -> None:
    m = json.loads((COMMITTED / "manifest.json").read_text())
    assert (COMMITTED / m["models"]).exists()
    for e in (*models.EXCERPTS, models.HINDSIGHT):
        assert (COMMITTED / m["golden"][e.name]["file"]).exists()
    assert m["codes"]["key"] == list(models.KEYS)
    from importlib.metadata import version

    assert m["versions"] == {k: version(k) for k in m["versions"]}, REGENERATE
    assert m["streamParams"] == st.Params().to_json()
