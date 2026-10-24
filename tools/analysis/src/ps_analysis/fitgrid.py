"""`fit-grid`: a corpus track's fitted reference (docs/design/reactive-feel.md, "Ground Truth").

madmom's offline whole-song beat and downbeat decode, a least-squares constant grid
through the decoded beats (or up to four constant pieces where the tempo changes, or
`fit: "failed"` with the raw decode), the metrical level a player most likely taps,
the meter voted from the downbeat decode, approximate sections from a Laplacian
segmentation snapped to downbeats, and accents: the offline strong onsets on grid
points. Whole-song analysis is allowed here: only the virtual players, the metrics and
the raters read references.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import numpy as np

SR = 44100
FPS = 100
#: Candidate metrical levels, as multiples of the decoded beat period: the decode's
#: own, double time, half time, and two-thirds time (a triplet feel).
LEVELS = (1.0, 0.5, 2.0, 1.5)
#: Each level's phase offsets, in decoded periods (a two-thirds grid can start on any
#: of three beats: 0, 1 or 2 periods in, the last being half its own period).
OFFSETS = {1.0: (0,), 0.5: (0,), 2.0: (0, 1), 1.5: (0, 1, 2)}
#: The level a player most likely taps has a period in this range (80 to 150 bpm).
TAP_PERIOD = (0.4, 0.75)
#: A fit is accepted with residuals of at most this median and 90th percentile (s),
#: and this share of decoded beats within WITHIN_S of the grid.
MEDIAN_S = 0.008
P90_S = 0.020
WITHIN_S = 0.045
WITHIN_SHARE = 0.9
MAX_PIECES = 4
#: The shortest piece of a piecewise fit (decoded beats).
MIN_PIECE_BEATS = 16
#: A meter is marked low confidence under this share of downbeats agreeing.
METER_AGREEMENT = 0.7
#: An onset this close to a grid point supports it (and a strong one is an accent).
ONSET_SNAP_S = 0.03
#: Strong onsets: above this percentile of onset strength.
STRONG_PERCENTILE = 98.0
#: Sections shorter than this many bars merge into a neighbour.
MIN_SECTION_BARS = 8
#: The two-thirds level replaces a plain one only with this much more onset strength on
#: its grid points. (TAP_PERIOD admits at most one plain level.)
LEVEL_MARGIN = 1.1
#: The phase refinement: the fine onset envelope's hop and window (samples at SR), and
#: the largest shift it tries (s).
FINE_HOP = 32
FINE_FFT = 256
PHASE_SEARCH_S = 0.02


@dataclass
class Line:
    """A constant grid through some decoded beats: t = phase + period * n."""

    phase: float
    period: float
    #: The decoded beats' grid indices and residuals (s).
    n: np.ndarray
    residuals: np.ndarray

    def stats(self) -> dict[str, float]:
        a = np.abs(self.residuals)
        return {
            "median": float(np.median(a)),
            "p90": float(np.percentile(a, 90)),
            "within": float(np.mean(a <= WITHIN_S)),
        }

    def accepted(self) -> bool:
        s = self.stats()
        return (
            s["median"] <= MEDIAN_S + 1e-12
            and s["p90"] <= P90_S + 1e-12
            and s["within"] >= WITHIN_SHARE - 1e-12
        )


def beat_indices(beats: np.ndarray) -> np.ndarray:
    """Grid indices for decoded beats: each gap counts as its rounded number of local periods."""
    ioi = np.diff(beats)
    n = np.zeros(len(beats), dtype=float)
    for i, gap in enumerate(ioi):
        local = np.median(ioi[max(0, i - 4) : i + 5])
        # (A stray extra beat, under half a period on, shares its index rather than shifting the rest.)
        n[i + 1] = n[i] + round(gap / local)
    return n


def fit_line(beats: np.ndarray, n: np.ndarray | None = None) -> Line:
    """The least-squares constant grid through `beats` (at least two)."""
    n = beat_indices(beats) if n is None else n - n[0]
    a = np.stack([np.ones_like(n), n], axis=1)
    (phase, period), *_ = np.linalg.lstsq(a, beats, rcond=None)
    return Line(float(phase), float(period), n, beats - (phase + period * n))


def _segment_costs(n: np.ndarray, b: np.ndarray):
    """Least-squares error of a line through beats [i, j), from prefix sums, in O(1)."""
    c = np.concatenate([[0.0], np.cumsum(np.ones_like(n))])
    sx = np.concatenate([[0.0], np.cumsum(n)])
    sy = np.concatenate([[0.0], np.cumsum(b)])
    sxx = np.concatenate([[0.0], np.cumsum(n * n)])
    sxy = np.concatenate([[0.0], np.cumsum(n * b)])
    syy = np.concatenate([[0.0], np.cumsum(b * b)])

    def cost(i: int, j: int) -> float:
        k = c[j] - c[i]
        x, y = sx[j] - sx[i], sy[j] - sy[i]
        xx, xy, yy = sxx[j] - sxx[i], sxy[j] - sxy[i], syy[j] - syy[i]
        den = k * xx - x * x
        if den <= 0:
            return float("inf")
        slope = (k * xy - x * y) / den
        icpt = (y - slope * x) / k
        return float(
            yy
            + slope * slope * xx
            + k * icpt * icpt
            - 2 * slope * xy
            - 2 * icpt * y
            + 2 * slope * icpt * x
        )

    return cost


def fit_pieces(beats: np.ndarray, pieces: int) -> list[tuple[int, int]] | None:
    """The split of `beats` into `pieces` runs (start, end) minimising the total squared error."""
    nb = len(beats)
    if nb < pieces * MIN_PIECE_BEATS:
        return None
    n = beat_indices(beats)
    cost = _segment_costs(n, beats)
    inf = float("inf")
    best = np.full((pieces + 1, nb + 1), inf)
    back = np.zeros((pieces + 1, nb + 1), dtype=int)
    best[0, 0] = 0.0
    for k in range(1, pieces + 1):
        for j in range(k * MIN_PIECE_BEATS, nb + 1):
            for i in range((k - 1) * MIN_PIECE_BEATS, j - MIN_PIECE_BEATS + 1):
                if best[k - 1, i] == inf:
                    continue
                v = best[k - 1, i] + cost(i, j)
                if v < best[k, j]:
                    best[k, j] = v
                    back[k, j] = i
    if best[pieces, nb] == inf:
        return None
    runs = []
    j = nb
    for k in range(pieces, 0, -1):
        i = int(back[k, j])
        runs.append((i, j))
        j = i
    return runs[::-1]


@dataclass
class Fit:
    kind: str  # "constant", "piecewise" or "failed"
    #: The constant lines (one, or one per piece) and each piece's decoded beat range.
    lines: list[Line]
    runs: list[tuple[int, int]]

    def stats(self) -> dict[str, float]:
        r = np.concatenate([ln.residuals for ln in self.lines])
        return Line(0, 0, np.zeros(0), r).stats()


def fit_beats(beats: np.ndarray) -> Fit:
    """A constant grid, else up to MAX_PIECES pieces each within the limits, else failed."""
    line = fit_line(beats)
    if line.accepted():
        return Fit("constant", [line], [(0, len(beats))])
    for k in range(2, MAX_PIECES + 1):
        runs = fit_pieces(beats, k)
        if runs is None:
            break
        lines = [fit_line(beats[i:j]) for i, j in runs]
        if all(ln.accepted() for ln in lines):
            return Fit("piecewise", lines, runs)
    return Fit("failed", [line], [(0, len(beats))])


#: A piece's grid reaches this far past its first and last decoded beats (s): the phase
#: refinement may move its line, and the decode is rounded to frames.
EDGE_S = PHASE_SEARCH_S + 0.5 / FPS


def base_grid(beats: np.ndarray, fit: Fit) -> np.ndarray:
    """The decode's level as one continuous grid: each piece's line over its span, joined without duplicates."""
    out: list[float] = []
    for ln, (i, j) in zip(fit.lines, fit.runs, strict=True):
        lo, hi = beats[i] - EDGE_S, beats[j - 1] + EDGE_S
        k0 = int(np.ceil((lo - ln.phase) / ln.period))
        k1 = int(np.floor((hi - ln.phase) / ln.period))
        pts = [ln.phase + ln.period * k for k in range(k0, k1 + 1)]
        if out:
            pts = [t for t in pts if t > out[-1] + 0.5 * ln.period]
        out.extend(pts)
    return np.asarray(out)


def grid_points(beats: np.ndarray, fit: Fit, level: float, offset: int) -> np.ndarray:
    """
    The grid at a metrical level: every `level` beats of the decode's continuous grid from `offset`
    beats in, by its global beat index, so a level keeps its phase across a piecewise join (a
    fractional position falls between two grid beats).
    """
    base = base_grid(beats, fit)
    if len(base) < 2:
        return base
    pos = np.arange(offset, len(base) - 1 + 1e-9, level)
    i = np.minimum(np.floor(pos).astype(int), len(base) - 2)
    return base[i] + (pos - i) * (base[i + 1] - base[i])


@dataclass
class Envelope:
    """An onset strength envelope: frame times and values (normalised to a peak of 1)."""

    t: np.ndarray
    v: np.ndarray

    def at(self, points: np.ndarray, within: float) -> np.ndarray:
        """The envelope's peak within `within` of each point."""
        step = float(self.t[1] - self.t[0])
        w = max(0, round(within / step))
        k = np.clip(
            np.round((points - self.t[0]) / step).astype(int), 0, len(self.v) - 1
        )
        out = np.zeros(len(points))
        for d in range(-w, w + 1):
            out = np.maximum(out, self.v[np.clip(k + d, 0, len(self.v) - 1)])
        return out


def onset_envelope(y: np.ndarray, sr: int, hop: int, n_fft: int) -> Envelope:
    import librosa

    # (A short window has room for fewer mel bands.)
    v = librosa.onset.onset_strength(
        y=y, sr=sr, hop_length=hop, n_fft=n_fft, n_mels=min(128, n_fft // 8)
    )
    v = v / (float(np.max(v)) or 1.0)
    return Envelope(librosa.frames_to_time(np.arange(len(v)), sr=sr, hop_length=hop), v)


def refine_phases(beats: np.ndarray, fit: Fit, env: Envelope) -> None:
    """Shifts each line's phase (within PHASE_SEARCH_S) to put its grid on the attacks: the decode is frame-quantised."""
    shifts = np.arange(-PHASE_SEARCH_S, PHASE_SEARCH_S + 1e-9, 0.0005)
    for k, ln in enumerate(fit.lines):
        single = Fit(fit.kind, [ln], [fit.runs[k]])
        pts = grid_points(beats, single, 1.0, 0)
        scores = [float(np.sum(env.at(pts + d, 0.0))) for d in shifts]
        ln.phase += float(shifts[int(np.argmax(scores))])


def hit_share(points: np.ndarray, targets: np.ndarray, within: float) -> float:
    """The share of targets within `within` of a point (1 with no targets)."""
    if len(targets) == 0:
        return 1.0
    if len(points) == 0:
        return 0.0
    i = np.clip(np.searchsorted(points, targets), 1, len(points) - 1)
    d = np.minimum(np.abs(points[i - 1] - targets), np.abs(points[i] - targets))
    return float(np.mean(d <= within))


def choose_level(
    beats: np.ndarray, fit: Fit, env: Envelope, downbeats: np.ndarray
) -> tuple[float, int, np.ndarray]:
    """
    The level whose period lies in TAP_PERIOD (else the level nearest to it), by the onset strength
    on its grid points, and its phase offset: the one on the most decoded downbeats (a tap lands on
    the bar, though a backbeat may be louder), then the strongest.
    """
    period = float(np.median([ln.period for ln in fit.lines]))
    lo, hi = TAP_PERIOD

    def gap(m: float) -> float:
        p = period * m
        return 0.0 if lo <= p <= hi else min(abs(p - lo), abs(p - hi))

    nearest = min(gap(m) for m in LEVELS)
    candidates = [m for m in LEVELS if gap(m) <= nearest + 1e-9]
    # A triplet feel needs evidence: the two-thirds level must beat the nearest plain level too.
    if candidates == [1.5]:
        candidates.insert(0, min((1.0, 0.5, 2.0), key=gap))
    best: tuple[float, float, int, np.ndarray] | None = None
    for m in candidates:
        options = []
        for off in OFFSETS[m]:
            pts = grid_points(beats, fit, m, off)
            strength = float(np.mean(env.at(pts, ONSET_SNAP_S))) if len(pts) else 0.0
            options.append(
                (hit_share(pts, downbeats, 0.1 * period), strength, off, pts)
            )
        hit, strength, off, pts = max(options, key=lambda o: (round(o[0], 2), o[1]))
        # (A triplet feel's grid also carries the bars: decoded downbeats land on it.)
        if m == 1.5 and (len(downbeats) == 0 or hit < METER_AGREEMENT):
            continue
        score = strength * (0.5 + 0.5 * hit)
        # (A plain level stays unless the two-thirds one clearly beats it.)
        if best is not None and m == 1.5:
            score /= LEVEL_MARGIN
        if best is None or score > best[0]:
            best = (score, m, off, pts)
    assert best is not None
    return best[1], best[2], best[3]


def fit_meter(grid: np.ndarray, downbeats: np.ndarray) -> tuple[int, int, float]:
    """The meter (2, 3 or 4 grid beats), the downbeat's grid offset and the share of decoded downbeats agreeing."""
    if len(grid) < 2 or len(downbeats) == 0:
        return 4, 0, 0.0
    i = np.clip(np.searchsorted(grid, downbeats), 1, len(grid) - 1)
    idx = np.where(
        np.abs(grid[i - 1] - downbeats) <= np.abs(grid[i] - downbeats), i - 1, i
    )
    step = float(np.median(np.diff(grid)))
    idx = idx[np.abs(grid[idx] - downbeats) <= 0.35 * step]
    idx = np.unique(idx)
    if len(idx) == 0:
        return 4, 0, 0.0
    votes = {2: 0, 3: 0, 4: 0}
    for g in np.diff(idx):
        for m in (4, 3, 2):
            if g == m or g == 2 * m:
                votes[m] += 1
                break
    meter = max((4, 3, 2), key=lambda m: votes[m])
    phases = np.bincount(idx % meter, minlength=meter)
    offset = int(np.argmax(phases))
    # (Over every decoded downbeat: those far from the grid disagree.)
    return meter, offset, float(phases[offset] / len(downbeats))


def sections_of(
    y: np.ndarray, sr: int, grid: np.ndarray, downbeats: np.ndarray, end: float
) -> list[dict[str, Any]]:
    """Laplacian segmentation of a beat-synchronous recurrence matrix, boundaries snapped to downbeats."""
    import librosa
    import scipy.cluster.vq
    import scipy.linalg
    import scipy.ndimage
    import scipy.sparse.csgraph

    if len(grid) < 16 or len(downbeats) < 2:
        return [
            {
                "label": "all",
                "s0": _t(grid[0]) if len(grid) else 0.0,
                "s1": _t(end),
                "approximate": True,
            }
        ]
    y22 = librosa.resample(y, orig_sr=sr, target_sr=22050)
    hop = 512
    cqt = librosa.amplitude_to_db(
        np.abs(
            librosa.cqt(y22, sr=22050, hop_length=hop, n_bins=72, bins_per_octave=12)
        ),
        ref=np.max,
    )
    frames = librosa.time_to_frames(grid, sr=22050, hop_length=hop)
    frames = np.unique(np.clip(frames, 0, cqt.shape[1] - 1))
    # (sync's segments start at each of these frames, a leading one at 0 if needed.)
    starts_f = librosa.util.fix_frames(frames, x_min=0, x_max=cqt.shape[1])[:-1]
    csync = librosa.util.sync(cqt, frames, aggregate=np.median)
    rec = librosa.segment.recurrence_matrix(csync, width=3, mode="affinity", sym=True)
    rf = librosa.segment.timelag_filter(scipy.ndimage.median_filter)(rec, size=(1, 7))
    mfcc = librosa.feature.mfcc(y=y22, sr=22050, hop_length=hop)
    msync = librosa.util.sync(mfcc, frames)
    path = np.sum(np.diff(msync, axis=1) ** 2, axis=0)
    sigma = np.median(path) or 1.0
    path_sim = np.exp(-path / sigma)
    r_path = np.diag(path_sim, k=1) + np.diag(path_sim, k=-1)
    deg_path = np.sum(r_path, axis=1)
    deg_rec = np.sum(rf, axis=1)
    mu = deg_path.dot(deg_path + deg_rec) / (np.sum((deg_path + deg_rec) ** 2) or 1.0)
    a = mu * rf + (1 - mu) * r_path
    lap = scipy.sparse.csgraph.laplacian(a, normed=True)
    _, evecs = scipy.linalg.eigh(lap)
    evecs = scipy.ndimage.median_filter(evecs, size=(9, 1))
    k = int(np.clip(round((end - grid[0]) / 30) + 2, 3, 8))
    k = min(k, evecs.shape[1])
    cnorm = np.cumsum(evecs**2, axis=1) ** 0.5
    x = evecs[:, :k] / (cnorm[:, k - 1 : k] + 1e-9)
    _, labels = scipy.cluster.vq.kmeans2(x, k, rng=1858, minit="++")
    # Boundaries where the cluster changes, as times of the synced frames, snapped to downbeats.
    times = librosa.frames_to_time(starts_f, sr=22050, hop_length=hop)
    starts = [times[i] for i in range(1, len(labels)) if labels[i] != labels[i - 1]]
    bar = float(np.median(np.diff(downbeats)))
    snapped: list[float] = []
    for t in starts:
        d = downbeats[int(np.argmin(np.abs(downbeats - t)))]
        if (
            d - downbeats[0] < MIN_SECTION_BARS * bar - 1e-6
            or end - d < MIN_SECTION_BARS * bar
        ):
            continue
        if snapped and d - snapped[-1] < MIN_SECTION_BARS * bar - 1e-6:
            continue
        snapped.append(float(d))
    bounds = [float(downbeats[0]), *snapped, end]
    return [
        {
            "label": f"section {i + 1}",
            "s0": _t(bounds[i]),
            "s1": _t(bounds[i + 1]),
            "approximate": True,
        }
        for i in range(len(bounds) - 1)
    ]


def onsets_of(y: np.ndarray, sr: int) -> tuple[np.ndarray, np.ndarray]:
    """Offline onset times and strengths over the whole track."""
    import librosa

    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=441)
    frames = librosa.onset.onset_detect(onset_envelope=env, sr=sr, hop_length=441)
    return librosa.frames_to_time(frames, sr=sr, hop_length=441), env[frames]


#: A failed fit is retried with the beat decode's tempo held within these factors of its median.
RETRY_TEMPO = (0.8, 1.25)


def decode(y: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """madmom's offline beats and decoded downbeats (times) over the whole track, and the beat activations."""
    from madmom.audio.signal import Signal
    from madmom.features.beats import DBNBeatTrackingProcessor, RNNBeatProcessor
    from madmom.features.downbeats import (
        DBNDownBeatTrackingProcessor,
        RNNDownBeatProcessor,
    )

    sig = Signal(y, sample_rate=SR, num_channels=1)
    act = RNNBeatProcessor(fps=FPS)(sig)
    beats = DBNBeatTrackingProcessor(fps=FPS, min_bpm=55, max_bpm=215)(act)
    db = DBNDownBeatTrackingProcessor(beats_per_bar=[2, 3, 4], fps=FPS)(
        RNNDownBeatProcessor(fps=FPS)(sig)
    )
    downbeats = db[db[:, 1] == 1, 0] if len(db) else np.zeros(0)
    return np.asarray(beats, dtype=float), np.asarray(downbeats, dtype=float), act


def narrow_decode(act: np.ndarray, beats: np.ndarray) -> np.ndarray:
    """The beats decoded again with the tempo held near the first decode's median (it may wander between levels)."""
    from madmom.features.beats import DBNBeatTrackingProcessor

    bpm = 60 / float(np.median(np.diff(beats)))
    lo, hi = RETRY_TEMPO
    dbn = DBNBeatTrackingProcessor(fps=FPS, min_bpm=bpm * lo, max_bpm=bpm * hi)
    return np.asarray(dbn(act), dtype=float)


def fit_reference(y: np.ndarray) -> dict[str, Any]:
    """The fitted reference of a track (mono samples at SR)."""
    beats, decoded_downbeats, act = decode(y)
    end = len(y) / SR
    onsets, strength = onsets_of(y, SR)
    env = onset_envelope(y, SR, 441, 2048)
    if len(beats) < 8:
        return {
            "source": "fitted",
            "fit": "failed",
            "reason": "too few decoded beats",
            "beats": [_t(b) for b in beats],
        }
    fit = fit_beats(beats)
    retried = False
    if fit.kind == "failed":
        again = narrow_decode(act, beats)
        if len(again) >= 8 and (fit2 := fit_beats(again)).kind != "failed":
            beats, fit, retried = again, fit2, True
    stats = fit.stats()
    residual = {
        "median": round(stats["median"] * 1000, 2),
        "p90": round(stats["p90"] * 1000, 2),
    }
    if fit.kind == "failed":
        return {
            "source": "fitted",
            "fit": "failed",
            "reason": "residuals over the limits as one grid, in up to 4 pieces, and decoded again with the tempo held near its median",
            "residualMs": residual,
            "within45": round(stats["within"], 4),
            "beats": [_t(b) for b in beats],
        }
    refine_phases(beats, fit, onset_envelope(y, SR, FINE_HOP, FINE_FFT))
    level, _, grid = choose_level(beats, fit, env, decoded_downbeats)
    meter, phase, agreement = fit_meter(grid, decoded_downbeats)
    downbeats = grid[phase::meter]
    strong = (
        onsets[strength >= np.percentile(strength, STRONG_PERCENTILE)]
        if len(onsets)
        else onsets
    )
    accents = []
    for t in strong:
        g = grid[int(np.argmin(np.abs(grid - t)))]
        if abs(g - t) <= ONSET_SNAP_S and (not accents or _t(g) > accents[-1]["t"]):
            accents.append({"t": _t(g), "label": "onset"})
    periods = [ln.period * level for ln in fit.lines]
    longest = max(range(len(fit.lines)), key=lambda i: fit.runs[i][1] - fit.runs[i][0])
    ref: dict[str, Any] = {
        "source": "fitted",
        "fit": fit.kind,
        "bpm": round(60 / periods[longest], 2),
        "residualMs": residual,
        "within45": round(stats["within"], 4),
        "level": level,
        "beats": [_t(t) for t in grid],
        "meter": meter,
        "meterConfidence": "high" if agreement >= METER_AGREEMENT else "low",
        "meterAgreement": round(agreement, 3),
        "downbeats": [_t(t) for t in downbeats],
        "sections": sections_of(y, SR, grid, downbeats, end),
        "accents": accents,
    }
    if retried:
        ref["decode"] = "narrow tempo"
    if fit.kind == "piecewise":
        ref["pieces"] = [
            {"s0": _t(beats[i]), "s1": _t(beats[j - 1]), "bpm": round(60 / p, 2)}
            for (i, j), p in zip(fit.runs, periods, strict=True)
        ]
    return ref


def _t(t: float) -> float:
    """A time rounded to 0.1 ms, as the stream file has them."""
    return round(float(t), 4)


def summary(ref: dict[str, Any]) -> dict[str, Any]:
    """The fit's headline numbers, for the corpus table."""
    out = {
        k: ref[k]
        for k in (
            "fit",
            "bpm",
            "residualMs",
            "within45",
            "level",
            "meter",
            "meterConfidence",
            "decode",
        )
        if k in ref
    }
    out["sections"] = len(ref.get("sections", []))
    if "pieces" in ref:
        out["pieces"] = len(ref["pieces"])
    return out
