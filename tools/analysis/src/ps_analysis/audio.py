"""Audio features shared by the analysis commands.

The onset-strength curve and its peaks are the reference that `check` measures
beats, anchors and accents against, so every command computes them the same way
(`onset_features`).
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import ndimage, signal

#: Onset-strength hop: 256 samples is 5.8 ms at 44.1 kHz.
HOP = 256
N_FFT = 1024


def load_mono(path: str | Path) -> tuple[np.ndarray, int]:
    y, sr = sf.read(str(path), dtype="float32", always_2d=True)
    return y.mean(axis=1), int(sr)


@dataclass
class Onsets:
    #: Frame times in seconds.
    times: np.ndarray
    #: Onset strength per frame (librosa spectral flux on a mel spectrogram).
    env: np.ndarray
    #: Peak times (sub-frame, parabolic interpolation).
    peak_times: np.ndarray
    #: Peak heights divided by the excerpt's 99.5th percentile, clipped to 0–1.
    peak_strengths: np.ndarray
    #: Peak heights divided by the local (3 s) 95th percentile.
    peak_local: np.ndarray

    def nearest_peak(self, t: float) -> tuple[float, float]:
        """(peak time, signed offset t - peak) of the peak nearest to t."""
        pt = self.peak_times
        if len(pt) == 0:
            return float("nan"), float("inf")
        k = int(np.searchsorted(pt, t))
        cands = [j for j in (k - 1, k) if 0 <= j < len(pt)]
        j = min(cands, key=lambda j: abs(pt[j] - t))
        return float(pt[j]), float(t - pt[j])


def onset_envelope(y: np.ndarray, sr: int) -> tuple[np.ndarray, np.ndarray]:
    import librosa

    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP, n_fft=N_FFT)
    times = librosa.frames_to_time(np.arange(len(env)), sr=sr, hop_length=HOP)
    return times, env


def pick_peaks(times: np.ndarray, env: np.ndarray, sr: int) -> Onsets:
    """Local maxima that stand out from their neighbourhood.

    A frame is a peak when no higher peak lies within 50 ms, its prominence is
    at least 30% of the local 95th percentile (3 s window), and it is at least
    1.3 times the local mean (0.5 s window). The thresholds are relative so
    that quiet passages still have peaks, and strict enough that a galop at
    185 BPM yields roughly one peak per eighth note rather than one every few
    frames (which would make the alignment check meaningless).
    """
    fps = sr / HOP
    local_hi = ndimage.percentile_filter(env, 95, size=max(3, int(3.0 * fps))) + 1e-6
    local_mean = ndimage.uniform_filter1d(env, size=max(3, int(0.5 * fps)))
    idx, _props = signal.find_peaks(
        env,
        distance=max(1, round(0.05 * fps)),
        prominence=0.3 * local_hi,
    )
    keep = env[idx] > local_mean[idx] * 1.3
    idx = idx[keep]
    # Parabolic interpolation for sub-frame peak times.
    pt = []
    for i in idx:
        if 0 < i < len(env) - 1:
            a, b, c = env[i - 1], env[i], env[i + 1]
            den = a - 2 * b + c
            off = 0.5 * (a - c) / den if den != 0 else 0.0
            off = float(np.clip(off, -0.5, 0.5))
        else:
            off = 0.0
        pt.append(times[i] + off / fps)
    glob = np.percentile(env, 99.5) + 1e-9
    return Onsets(
        times=times,
        env=env,
        peak_times=np.array(pt, dtype=float),
        peak_strengths=np.clip(env[idx] / glob, 0, 1).astype(float),
        peak_local=(env[idx] / local_hi[idx]).astype(float),
    )


def onset_features(y: np.ndarray, sr: int) -> Onsets:
    times, env = onset_envelope(y, sr)
    return pick_peaks(times, env, sr)


def rms_db(y: np.ndarray, sr: int, hop: int = 512) -> tuple[np.ndarray, np.ndarray]:
    import librosa

    r = librosa.feature.rms(y=y, frame_length=2048, hop_length=hop)[0]
    t = librosa.frames_to_time(np.arange(len(r)), sr=sr, hop_length=hop)
    return t, 20 * np.log10(r + 1e-6)


def _norm(x: np.ndarray, lo_pct: float = 2, hi_pct: float = 98) -> np.ndarray:
    lo, hi = np.percentile(x, lo_pct), np.percentile(x, hi_pct)
    return np.clip((x - lo) / (hi - lo + 1e-9), 0, 1)


def beat_energy(
    y: np.ndarray, sr: int, beats: list[float], onsets: Onsets
) -> list[float]:
    """0–1 energy per beat: loudness (70%) plus onset density (30%), smoothed."""
    t_rms, db = rms_db(y, sr)
    duration = len(y) / sr
    b = np.asarray(beats, dtype=float)
    ends = np.append(b[1:], b[-1] + (b[-1] - b[-2]) if len(b) > 1 else b[-1] + 0.5)
    loud = np.zeros(len(b))
    dens = np.zeros(len(b))
    for i, (s, e) in enumerate(zip(b, ends)):
        s2, e2 = max(0.0, s), min(duration, max(e, s + 0.05))
        m = (t_rms >= s2) & (t_rms < e2)
        loud[i] = 10 * np.log10(np.mean(10 ** (db[m] / 10))) if m.any() else -120
        mo = (onsets.times >= s2) & (onsets.times < e2)
        dens[i] = onsets.env[mo].mean() if mo.any() else 0.0
    x = 0.7 * _norm(loud) + 0.3 * _norm(dens)
    x = np.convolve(np.pad(x, 1, mode="edge"), [0.25, 0.5, 0.25], mode="valid")
    return [float(v) for v in _norm(x, 1, 99)]


def accent_candidates(
    y: np.ndarray, sr: int, onsets: Onsets, count: int = 60, min_gap: float = 0.25
) -> list[dict[str, float]]:
    """Strong, loud onset peaks, strongest first, at least `min_gap` apart.

    Score = 0.5 × global peak strength + 0.3 × loudness jump (0–12 dB)
    + 0.2 × loudness after the onset (relative to the excerpt).
    """
    t_rms, db = rms_db(y, sr, hop=256)
    level = _norm(db, 5, 99.5)

    def mean_db(a: float, b: float) -> float:
        m = (t_rms >= a) & (t_rms < b)
        return float(db[m].mean()) if m.any() else -120.0

    def mean_level(a: float, b: float) -> float:
        m = (t_rms >= a) & (t_rms < b)
        return float(level[m].mean()) if m.any() else 0.0

    scored = []
    duration = len(y) / sr
    for t, s in zip(onsets.peak_times, onsets.peak_strengths):
        if t < 0.1 or t > duration - 0.1:
            continue  # the cut points themselves are not musical hits
        jump = mean_db(t + 0.02, t + 0.12) - mean_db(t - 0.15, t - 0.03)
        score = (
            0.5 * s
            + 0.3 * float(np.clip(jump / 12, 0, 1))
            + 0.2 * mean_level(t, t + 0.15)
        )
        scored.append((score, float(t), float(jump)))
    scored.sort(reverse=True)
    picked: list[tuple[float, float, float]] = []
    for sc, t, jump in scored:
        if all(abs(t - p[1]) >= min_gap for p in picked):
            picked.append((sc, t, jump))
        if len(picked) >= count:
            break
    top = picked[0][0] if picked else 1.0
    return [
        {"time": t, "strength": round(sc / top, 3), "jumpDb": round(jump, 1)}
        for sc, t, jump in picked
    ]
