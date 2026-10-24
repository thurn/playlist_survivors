"""Bands, brightness and timbre for the belief stream (docs/design/reactive-feel.md, "Bands,
brightness and timbre (E1)").

Everything is causal and streaming: a frame's spectrum is the window of samples ending at the
frame's time, every normaliser is a decaying running maximum or a statistic over a bounded past,
and each value is a function of frames at or before its own time. (It is computed in numpy
batches, but nothing reads ahead.)

- Tick bands `{kick, snare, hat, bass}` (0-1): each band's level in dB is read, as `energy` is,
  against its own running maximum, which falls 0.2 dB/s and never below -60 dB, over a 30 dB
  range. Bass is that level (sustained). Kick, snare and hat are transient: the rise of the
  level, held within that range, against a decaying running maximum of the rises. Named for
  their role: a chiptune's noise channel lands in hat and snare.
- An onset's `band`: the band with the largest flux at its detection frame, relative to that
  band's running maximum.
- Tick `brightness` (0-1): the spectral centroid (of the magnitude spectrum, in octaves) over
  the last 0.5 s, standardised against the past 30 s and squashed.
- `timbre` entries every second over the past 10 s: brightness (the centroid on a fixed octave
  scale), flatness (noisy against tonal, from the magnitude spectrum over 100 Hz-8 kHz),
  percussive (the energy share of a causal median-filter harmonic-percussive split) and density
  (onsets per second).
"""

from __future__ import annotations

import time
from collections import deque
from dataclasses import dataclass
from typing import Any

import numpy as np

SR = 44100
FPS = 100
HOP = SR // FPS
#: The causal frame's window (samples ending at the frame's time).
N_FFT = 2048
BAND_HZ: dict[str, list[tuple[float, float]]] = {
    "kick": [(40.0, 120.0)],
    "bass": [(60.0, 250.0)],
    "snare": [(150.0, 400.0), (2000.0, 5000.0)],
    "hat": [(6000.0, 16000.0)],
}
BANDS = ("kick", "snare", "hat", "bass")
TRANSIENT = ("kick", "snare", "hat")
#: Band levels, as `energy`: dB of full scale (a full-scale sine in the band reads about 0), read
#: against a running maximum that falls DECAY_DB_PER_S and never below FLOOR_DB, over RANGE_DB.
DECAY_DB_PER_S = 0.2
RANGE_DB = 30.0
FLOOR_DB = -60.0
#: A full-scale sine's peak bin power under the Hann window, so band power reads in dB of full scale.
FULL_SCALE = (N_FFT / 4) ** 2
#: The transient bands' rises are read against a running maximum that halves over PEAK_HALF_S
#: and never falls under FLUX_FLOOR_DB, so a few dB of wobble in an empty band stays small.
PEAK_HALF_S = 8.0
FLUX_FLOOR_DB = 6.0
#: Flatness: the magnitude spectrum over this band, each bin floored this far under the frame's peak.
FLAT_HZ = (100.0, 8000.0)
FLAT_FLOOR_DB = 80.0
#: Timbre brightness: the centroid in octaves above this (Hz), squashed with this scale (octaves).
TIMBRE_BRIGHT_HZ = 1500.0
TIMBRE_BRIGHT_OCTAVES = 0.75
BRIGHT_WINDOW_S = 0.5
BRIGHT_STATS_S = 30.0
TIMBRE_EVERY_S = 1.0
TIMBRE_PAST_S = 10.0
#: The causal harmonic-percussive split: a median over this many past frames (harmonic, along
#: time) and this many neighbouring bins (percussive, along frequency).
HPSS_TIME = 17
HPSS_FREQ = 17

GROUP_VERSION = 2


@dataclass
class Spectra:
    """The causal power spectrogram: frame n covers the N_FFT samples ending at sample n * HOP."""

    power: np.ndarray  # frames x bins
    freqs: np.ndarray


def spectra(y: np.ndarray) -> Spectra:
    n_frames = len(y) // HOP + 1
    padded = np.concatenate([np.zeros(N_FFT, dtype=np.float32), y.astype(np.float32)])
    window = np.hanning(N_FFT).astype(np.float32)
    power = np.empty((n_frames, N_FFT // 2 + 1), dtype=np.float32)
    for start in range(0, n_frames, 512):
        idx = np.arange(start, min(n_frames, start + 512))
        # Frame n's window ends at sample n * HOP (exclusive), so it holds only audio before the frame.
        ends = idx * HOP + N_FFT
        frames = np.stack([padded[e - N_FFT : e] for e in ends]) * window
        power[idx] = np.abs(np.fft.rfft(frames, axis=1)) ** 2
    return Spectra(power, np.fft.rfftfreq(N_FFT, 1 / SR))


def band_energy(s: Spectra) -> dict[str, np.ndarray]:
    """Each band's power per frame."""
    out = {}
    for name, ranges in BAND_HZ.items():
        mask = np.zeros(len(s.freqs), dtype=bool)
        for lo, hi in ranges:
            mask |= (s.freqs >= lo) & (s.freqs < hi)
        out[name] = s.power[:, mask].sum(axis=1).astype(np.float64)
    return out


#: A band's rise is measured over this many frames: a low band's energy builds over several frames
#: of the window, so a one-frame difference undersells a kick.
RISE_FRAMES = 3


def band_levels(
    energy: dict[str, np.ndarray],
) -> dict[str, tuple[np.ndarray, np.ndarray]]:
    """Each band's level (dB of full scale) and its running maximum, which falls DECAY_DB_PER_S, never under FLOOR_DB."""
    fall = DECAY_DB_PER_S / FPS
    out = {}
    for name in BANDS:
        db = 10 * np.log10(energy[name] / FULL_SCALE + 1e-12)
        ref = np.empty(len(db))
        r = FLOOR_DB
        for i, v in enumerate(db):
            r = max(float(v), r - fall, FLOOR_DB)
            ref[i] = r
        out[name] = (db, ref)
    return out


def band_flux(
    levels: dict[str, tuple[np.ndarray, np.ndarray]],
) -> dict[str, np.ndarray]:
    """Each band's rise over the last RISE_FRAMES frames, of its level held within RANGE_DB of its running maximum."""
    out = {}
    for name in BANDS:
        db, ref = levels[name]
        held = np.maximum(db, ref - RANGE_DB)
        before = np.concatenate([np.full(RISE_FRAMES, held[0]), held])[: len(held)]
        out[name] = np.maximum(0.0, held - before)
    return out


def running_peak(x: np.ndarray, floor: float) -> np.ndarray:
    """A running maximum (including each value) that halves over PEAK_HALF_S, never under `floor`."""
    decay = 0.5 ** (1 / (PEAK_HALF_S * FPS))
    out = np.empty(len(x))
    peak = floor
    for i, v in enumerate(x):
        peak = max(float(v), peak * decay, floor)
        out[i] = peak
    return out


def tick_bands(
    levels: dict[str, tuple[np.ndarray, np.ndarray]],
    flux: dict[str, np.ndarray],
    tick_frames: list[int],
) -> list[dict[str, float]]:
    """Each tick's bands: the transient bands' largest rise in the tick against their running peak, bass's level."""
    rel = {
        name: flux[name] / running_peak(flux[name], FLUX_FLOOR_DB) for name in TRANSIENT
    }
    db, ref = levels["bass"]
    bass = np.clip((db - ref + RANGE_DB) / RANGE_DB, 0.0, 1.0)
    out = []
    prev = -1
    last = len(bass) - 1
    for f in tick_frames:
        # The frames after the previous tick up to this one (at least this one's).
        hi = min(f, last)
        lo = min(prev + 1, hi)
        tick = {
            name: float(np.clip(rel[name][lo : hi + 1].max(), 0.0, 1.0))
            for name in TRANSIENT
        }
        tick["bass"] = float(bass[hi])
        out.append({name: tick[name] for name in BANDS})
        prev = f
    return out


def onset_bands(flux: dict[str, np.ndarray], onset_frames: list[int]) -> list[str]:
    """Each onset's band: the largest flux at its frame, each band against its own running peak."""
    rel = {name: flux[name] / running_peak(flux[name], FLUX_FLOOR_DB) for name in BANDS}
    last = len(flux["kick"]) - 1
    return [max(BANDS, key=lambda b: rel[b][min(n, last)]) for n in onset_frames]


def centroids(s: Spectra) -> np.ndarray:
    """Each frame's centroid of the magnitude spectrum, in octaves above 20 Hz (0 for silence)."""
    mag = np.sqrt(s.power)
    total = mag.sum(axis=1)
    hz = np.where(
        total > 1e-6, (mag * s.freqs).sum(axis=1) / np.maximum(total, 1e-6), 20.0
    )
    return np.log2(np.maximum(hz, 20.0) / 20.0)


def brightness(cent: np.ndarray, tick_frames: list[int]) -> list[float]:
    """The mean centroid over the last BRIGHT_WINDOW_S, standardised against the past BRIGHT_STATS_S of those means, squashed to 0-1."""
    w = round(BRIGHT_WINDOW_S * FPS)
    csum = np.concatenate([[0.0], np.cumsum(cent)])
    last = len(cent) - 1
    past: deque[float] = deque()
    span = (
        round(BRIGHT_STATS_S * FPS / max(1, tick_frames[1] - tick_frames[0]))
        if len(tick_frames) > 1
        else 1
    )
    out = []
    for f in tick_frames:
        hi = min(f, last) + 1
        lo = max(0, hi - w)
        m = float((csum[hi] - csum[lo]) / max(1, hi - lo))
        past.append(m)
        if len(past) > span:
            past.popleft()
        mean = sum(past) / len(past)
        sd = (sum((x - mean) ** 2 for x in past) / len(past)) ** 0.5
        z = (m - mean) / sd if sd > 1e-9 else 0.0
        out.append(float(1 / (1 + np.exp(-z))))
    return out


def flatness(s: Spectra) -> np.ndarray:
    """Spectral flatness per frame: the geometric over the arithmetic mean of the magnitudes over FLAT_HZ, each floored FLAT_FLOOR_DB under the frame's peak; 0 for silence."""
    mask = (s.freqs >= FLAT_HZ[0]) & (s.freqs < FLAT_HZ[1])
    mag = np.sqrt(s.power[:, mask].astype(np.float64))
    peak = mag.max(axis=1, keepdims=True)
    m = np.maximum(mag, peak * 10 ** (-FLAT_FLOOR_DB / 20) + 1e-12)
    geo = np.exp(np.log(m).mean(axis=1))
    return np.where(peak[:, 0] > 1e-6, geo / m.mean(axis=1), 0.0)


def percussive_share(s: Spectra) -> np.ndarray:
    """Per frame, the share of energy the causal median split calls percussive: harmonic from the past HPSS_TIME frames' median per bin, percussive from the HPSS_FREQ bins' median within the frame."""
    from scipy.ndimage import median_filter

    p = s.power.astype(np.float32)
    # Along time, causally: the median of each bin over the frame and the HPSS_TIME - 1 frames before it.
    padded = np.concatenate([np.repeat(p[:1], HPSS_TIME - 1, axis=0), p])
    harm = median_filter(padded, size=(HPSS_TIME, 1), origin=((HPSS_TIME - 1) // 2, 0))[
        HPSS_TIME - 1 :
    ]
    perc = median_filter(p, size=(1, HPSS_FREQ))
    hp = harm.sum(axis=1)
    pp = perc.sum(axis=1)
    return np.where(hp + pp > 1e-10, pp / np.maximum(hp + pp, 1e-10), 0.0)


def timbre_entries(
    bright_frames: np.ndarray,
    flat: np.ndarray,
    perc: np.ndarray,
    onset_times: list[float],
    duration: float,
) -> list[dict[str, float]]:
    """Every TIMBRE_EVERY_S from TIMBRE_EVERY_S on, the past TIMBRE_PAST_S's means and onset density."""
    out = []
    k = 1
    last = len(flat) - 1
    while k * TIMBRE_EVERY_S <= duration + 1e-9:
        t = k * TIMBRE_EVERY_S
        hi = min(round(t * FPS), last) + 1
        lo = max(0, hi - round(TIMBRE_PAST_S * FPS))
        span = min(t, TIMBRE_PAST_S)
        out.append(
            {
                "knownAt": t,
                "brightness": float(bright_frames[lo:hi].mean()),
                "flatness": float(flat[lo:hi].mean()),
                "percussive": float(perc[lo:hi].mean()),
                "density": sum(
                    1 for o in onset_times if t - TIMBRE_PAST_S < o <= t + 1e-9
                )
                / span,
            }
        )
        k += 1
    return out


def analyse(
    y: np.ndarray,
    tick_frames: list[int],
    onset_frames: list[int],
    onset_known: list[float],
) -> dict[str, Any]:
    """The bands group: tick bands and brightness, onset bands, timbre entries, and each part's real-time factor."""
    duration = len(y) / SR
    t0 = time.perf_counter()
    s = spectra(y)
    levels = band_levels(band_energy(s))
    flux = band_flux(levels)
    t_spec = time.perf_counter() - t0
    t1 = time.perf_counter()
    bands = tick_bands(levels, flux, tick_frames)
    obands = onset_bands(flux, onset_frames)
    t_bands = time.perf_counter() - t1 + t_spec
    t2 = time.perf_counter()
    cent = centroids(s)
    bright = brightness(cent, tick_frames)
    t_bright = time.perf_counter() - t2 + t_spec
    t3 = time.perf_counter()
    # Brightness per frame for timbre: each frame's centroid on a fixed octave scale, so entries
    # compare across songs (the tick brightness is relative to the song's own past).
    bright_frames = 1 / (
        1 + np.exp(-(cent - np.log2(TIMBRE_BRIGHT_HZ / 20.0)) / TIMBRE_BRIGHT_OCTAVES)
    )
    timbre = timbre_entries(
        bright_frames, flatness(s), percussive_share(s), onset_known, duration
    )
    t_timbre = time.perf_counter() - t3 + t_spec
    return {
        "bands": bands,
        "brightness": bright,
        "onsetBands": obands,
        "timbre": timbre,
        "rtf": {
            k: round(v / max(duration, 1e-9), 4)
            for k, v in (
                ("bands", t_bands),
                ("brightness", t_bright),
                ("timbre", t_timbre),
            )
        },
    }
