"""The belief stream's structure (stage 4): what a causal analysis knows of a track's form.

All of it is computed per 50 ms tick from feature frames that end at their
tick (each frame holds the 4096 samples before it), with statistics from the
past only, so the stream of the audio cut at X holds exactly the entries known
by X (`tests/test_stream.py` proves it).

- **Changes:** novelty between the past 8 s and the next 2 s of features
  (chroma, MFCCs and loudness), each feature scaled by its spread over the past
  30 s. A change at `b` is declared when its novelty is the largest within
  ±4 s and above a running threshold (the past 60 s's median plus 0.5 times the
  gap between their 90th percentile and median), so it is known at `b + 6 s`.
- **Jumps:** the loudness of the last 1 s against the 6 s before it; a rise or
  fall of 6 dB or more, at most one every 4 s, reported at the end of that 1 s
  with `t` 1 s earlier.
- **Level** of each tick (`quiet`, `medium` or `loud`) from its energy with
  hysteresis.
- **Key** of each tick from the chroma of the past 20 s, matched against major
  and minor key profiles, from 10 s on.
- **Ends:** the loudness at least 20 dB below the loudest second of the past
  30 s for 1.5 s, after at least 10 s of audio: `t` where the quiet began.

See docs/design/reactive-audio.md, "Structure (stage 4)".
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

import numpy as np

if TYPE_CHECKING:
    from .stream import Params

#: Samples per feature frame (each ends at its tick).
N_FFT = 4096

#: Krumhansl-Kessler key profiles, from the tonic.
MAJOR = np.array(
    [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
)
MINOR = np.array(
    [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]
)
MAJOR_NAMES = ["C", "Db", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]
MINOR_NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "G#", "A", "Bb", "B"]


def frames(y: np.ndarray, n: int, hop: int) -> np.ndarray:
    """The n feature frames (N_FFT x n) ending at ticks 1..n: frame k holds y[k*hop - N_FFT : k*hop]."""
    pad = np.concatenate([np.zeros(N_FFT), np.asarray(y, dtype=np.float64)])
    idx = (np.arange(1, n + 1) * hop)[None, :] + np.arange(N_FFT)[:, None]
    # (Frames past the end of the audio, which only a tick at the very end could need, read zeros.)
    pad = np.concatenate([pad, np.zeros(max(0, int(idx.max()) + 1 - len(pad)))])
    return pad[idx]


def features(
    y: np.ndarray, n: int, hop: int, sr: int
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Per tick: chroma (n x 12), the novelty features (n x 25: chroma, MFCCs 1-12, loudness / 10) and loudness in dB."""
    import librosa

    fr = frames(y, n, hop)
    ldb = 20 * np.log10(np.sqrt(np.mean(fr * fr, axis=0)) + 1e-6)
    spec = np.abs(np.fft.rfft(fr * np.hanning(N_FFT + 1)[:-1, None], axis=0)) ** 2
    # (Fixed tuning and no top-dB clip: both would read the whole file.)
    chroma = librosa.feature.chroma_stft(S=spec, sr=sr, n_fft=N_FFT, tuning=0.0)
    mel = librosa.feature.melspectrogram(S=spec, sr=sr, n_fft=N_FFT)
    mfcc = librosa.feature.mfcc(
        S=librosa.power_to_db(mel, top_db=None), sr=sr, n_mfcc=13
    )
    feats = np.vstack([chroma, mfcc[1:], ldb[None, :] / 10]).T
    return chroma.T, feats, ldb


def _cumsum(a: np.ndarray) -> np.ndarray:
    return np.concatenate([np.zeros((1, *a.shape[1:])), np.cumsum(a, axis=0)])


def novelty(feats: np.ndarray, p: Params, fps: float) -> np.ndarray:
    """Novelty at each tick i between ticks [i - past, i) and [i, i + next), NaN where either is incomplete.

    Each feature is scaled by its standard deviation over the `statsS` before
    the next window ends (the mean cancels in the difference).
    """
    n, d = feats.shape
    past = round(p.change_past_s * fps)
    nxt = round(p.change_next_s * fps)
    stats = round(p.change_stats_s * fps)
    c1 = _cumsum(feats)
    c2 = _cumsum(feats * feats)
    out = np.full(n, np.nan)
    for i in range(past, n - nxt + 1):
        known = i + nxt
        lo = max(0, known - stats)
        m = known - lo
        mu = (c1[known] - c1[lo]) / m
        sd = np.sqrt(np.maximum((c2[known] - c2[lo]) / m - mu * mu, 0)) + 1e-6
        a = (c1[i] - c1[i - past]) / past
        b = (c1[known] - c1[i]) / nxt
        out[i] = float(np.linalg.norm((a - b) / sd) / np.sqrt(d))
    return out


def changes(
    nov: np.ndarray, times: np.ndarray, p: Params, fps: float, center_s: float
) -> list[dict[str, float]]:
    """Novelty peaks: the largest within ±peakS and above the running threshold.

    A change at tick i is known once the novelty through i + peak is, at the
    tick ending the next window after it.
    """
    n = len(nov)
    w = round(p.change_peak_s * fps)
    nxt = round(p.change_next_s * fps)
    hist = round(p.change_threshold_s * fps)
    out: list[dict[str, float]] = []
    for i in range(n):
        if np.isnan(nov[i]) or i + w + nxt > n:
            continue
        seg = nov[max(0, i - w) : i + w + 1]
        prior = nov[max(0, i - hist) : i]
        prior = prior[~np.isnan(prior)]
        if len(prior) < p.change_min_prior:
            continue
        med = float(np.median(prior))
        thr = med + p.change_k * (float(np.percentile(prior, 90)) - med)
        if nov[i] >= np.nanmax(seg) and nov[i] > thr:
            out.append(
                {
                    # The boundary: the middle of tick i's frame.
                    "t": float(times[i]) - center_s,
                    "knownAt": float(times[i + w + nxt - 1]),
                    "strength": float(nov[i]),
                }
            )
    return out


def _window_mean(c: np.ndarray, hi: int, length: int) -> float:
    lo = max(0, hi - length)
    return float((c[hi] - c[lo]) / (hi - lo))


def jumps(
    ldb: np.ndarray, times: np.ndarray, p: Params, fps: float
) -> list[dict[str, float]]:
    """Loudness jumps: the last `recentS` against the `beforeS` before it, at most one every `gapS`."""
    recent = round(p.jump_recent_s * fps)
    before = round(p.jump_before_s * fps)
    c = _cumsum(ldb)
    out: list[dict[str, float]] = []
    last = -np.inf
    for i in range(recent + before - 1, len(ldb)):
        end = i + 1
        now = _window_mean(c, end, recent)
        then = _window_mean(c, end - recent, before)
        db = now - then
        t = float(times[i])
        if abs(db) >= p.jump_db and t - last > p.jump_gap_s:
            out.append({"t": t - p.jump_recent_s, "knownAt": t, "db": db})
            last = t
    return out


def levels(energy: np.ndarray, p: Params) -> list[str]:
    """Each tick's level from its energy, with hysteresis (starting medium)."""
    out: list[str] = []
    level = "medium"
    for e in energy:
        if (
            level == "loud"
            and e < p.level_loud_out
            or level == "quiet"
            and e >= p.level_quiet_out
        ):
            level = "medium"
        if level == "medium":
            if e >= p.level_loud_in:
                level = "loud"
            elif e < p.level_quiet_in:
                level = "quiet"
        out.append(level)
    return out


def _key_name(v: np.ndarray) -> str:
    best = ("", -np.inf)
    for profile, names, mode in (
        (MAJOR, MAJOR_NAMES, "major"),
        (MINOR, MINOR_NAMES, "minor"),
    ):
        for r in range(12):
            score = float(np.corrcoef(v, np.roll(profile, r))[0, 1])
            if score > best[1] + 1e-12:
                best = (f"{names[r]} {mode}", score)
    return best[0]


def keys(
    chroma: np.ndarray, times: np.ndarray, p: Params, fps: float
) -> list[str | None]:
    """Each tick's key from the mean chroma of the past `windowS`, None before `afterS`."""
    win = round(p.key_window_s * fps)
    c = _cumsum(chroma)
    out: list[str | None] = []
    for i, t in enumerate(times):
        if t < p.key_after_s - 1e-9:
            out.append(None)
            continue
        lo = max(0, i + 1 - win)
        v = (c[i + 1] - c[lo]) / (i + 1 - lo)
        out.append(_key_name(v) if np.ptp(v) > 1e-9 else None)
    return out


def ends(
    ldb: np.ndarray, times: np.ndarray, p: Params, fps: float
) -> list[dict[str, float]]:
    """Candidate endings: `dropDb` below the loudest second of the past `windowS`, held `holdS`."""
    smooth = round(p.end_smooth_s * fps)
    win = round(p.end_window_s * fps)
    hold = round(p.end_hold_s * fps)
    c = _cumsum(ldb)
    second = np.array([_window_mean(c, i + 1, smooth) for i in range(len(ldb))])
    out: list[dict[str, float]] = []
    run = 0
    for i in range(len(ldb)):
        loudest = float(second[max(0, i - win) : i + 1].max())
        run = run + 1 if ldb[i] < loudest - p.end_drop_db else 0
        t = float(times[i])
        if run == hold and t >= p.end_after_s - 1e-9:
            out.append({"t": t - p.end_hold_s, "knownAt": t})
    return out


def structure(
    y: np.ndarray, times: np.ndarray, energy: np.ndarray, p: Params, hop: int, sr: int
) -> dict[str, Any]:
    """Changes, jumps and ends, and each tick's level and key."""
    fps = sr / hop
    chroma, feats, ldb = features(y, len(times), hop, sr)
    return {
        "changes": changes(novelty(feats, p, fps), times, p, fps, N_FFT / 2 / sr),
        "jumps": jumps(ldb, times, p, fps),
        "ends": ends(ldb, times, p, fps),
        "levels": levels(energy, p),
        "keys": keys(chroma, times, p, fps),
    }
