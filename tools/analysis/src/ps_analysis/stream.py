"""`stream`: the belief stream, what a causal analysis knows of a track over time.

Every entry is stamped with the song time it becomes known (`knownAt`), and
nothing in an entry depends on audio after that time: the analysis of the
audio cut at X equals the full analysis's entries known by X (minus a 50 ms
margin), which `tests/test_stream.py` proves. Nothing may normalize over the
whole file.

- **Beats** (committed): madmom's online beat model (a unidirectional LSTM,
  each activation frame's window ending at its own time) and the forward
  algorithm of its beat-tracking network give a believed grid at every
  activation frame. The next beat is committed, and never revised, once its
  forecast falls within 1 period plus 0.35 s (the design's fallback horizon),
  or at the latest when the beat before it arrives; one that a sudden belief
  change puts less than 0.1 s ahead is skipped. The
  forecast extrapolates with the period smoothed over the past 2 s, and each
  beat time includes `biasS`.
- **Ticks** every 50 ms: `evidence` (the mean peak activation within ±30 ms of
  the last 4 believed beats) and `energy` (loudness over the last 0.5 s against
  a running maximum that falls by 0.2 dB a second, 0 at 30 dB below it).
- **Onsets:** online superflux onsets, with `strong` when the strength is at
  least the 98th percentile of the onsets of the past 30 s.
- **Reference** (optional): beats and sections for the virtual players and the
  checks, never for the simulation.

See docs/design/reactive-audio.md, "The Belief Stream".
"""

from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np

#: Bumped whenever the stream's output changes, so stale shipped streams fail
#: `check:reactive`.
TOOL_VERSION = 3

SR = 44100
FPS = 100
#: Activation frames per tick.
TICK_FRAMES = 5


@dataclass(frozen=True)
class Params:
    min_bpm: float = 55
    max_bpm: float = 215
    #: Tempo states of the tracker: None models every whole-frame beat interval,
    #: so a forecast two periods ahead is not off by the gaps of a log spacing.
    num_tempi: int | None = None
    #: The forecast period is the mean of the most likely states' periods over
    #: this many past seconds, counting only frames within `period_band` of the
    #: current period. A state's period is a whole number of frames, and the
    #: tracker alternates between the two around the true one; forecasting two
    #: periods ahead from either is off by up to two frames.
    period_window_s: float = 2.0
    period_band: float = 0.1
    #: The design's fallback horizon (1 period plus 0.35 s, not 2): at 2 periods
    #: only 54% of the Galop's pulsed-section beats fell within 45 ms of the song
    #: map, under the 79% that triggers the fallback (see the design's appendix).
    commit_periods: float = 1.0
    commit_extra_s: float = 0.35
    commit_min_lead_s: float = 0.1
    gate: float = 0.2
    #: Calibration of the believed grid: the committed beats' median lateness on
    #: the steady synthetic tracks (house-128 15 ms, dnb-174 11 ms,
    #: pop-arrangement 10 ms with no bias).
    bias_s: float = -0.013
    evidence_beats: int = 4
    evidence_half_s: float = 0.03
    energy_window_s: float = 0.5
    energy_decay_db_per_s: float = 0.2
    energy_range_db: float = 30.0
    #: The running maximum never falls below this, so leading silence reads 0.
    energy_floor_db: float = -60.0
    onset_threshold: float = 1.5
    #: Onset times are the detection frame plus this (detection lags hits ~10 ms).
    onset_bias_s: float = -0.010
    strong_percentile: float = 98.0
    strong_window_s: float = 30.0
    #: Hindsight grids: every `hindsight_every_s`, madmom's offline model decodes the
    #: last `hindsight_window_s` of audio; each entry keeps the beats of its last
    #: `hindsight_keep_s`, the offline decode's with `hindsight_bias_s`.
    hindsight_every_s: float = 1.0
    hindsight_window_s: float = 20.0
    hindsight_keep_s: float = 8.0
    hindsight_bias_s: float = 0.0

    def to_json(self) -> dict[str, Any]:
        return {
            "fps": FPS,
            "minBpm": self.min_bpm,
            "maxBpm": self.max_bpm,
            "numTempi": self.num_tempi,
            "period": {"windowS": self.period_window_s, "band": self.period_band},
            "commit": {
                "periods": self.commit_periods,
                "extraS": self.commit_extra_s,
                "minLeadS": self.commit_min_lead_s,
            },
            "gate": self.gate,
            "tickS": TICK_FRAMES / FPS,
            "evidence": {"beats": self.evidence_beats, "halfS": self.evidence_half_s},
            "energy": {
                "windowS": self.energy_window_s,
                "decayDbPerS": self.energy_decay_db_per_s,
                "rangeDb": self.energy_range_db,
                "floorDb": self.energy_floor_db,
            },
            "onsets": {
                "threshold": self.onset_threshold,
                "biasS": self.onset_bias_s,
                "strongPercentile": self.strong_percentile,
                "strongWindowS": self.strong_window_s,
            },
            "hindsight": {
                "everyS": self.hindsight_every_s,
                "windowS": self.hindsight_window_s,
                "keepS": self.hindsight_keep_s,
                "biasS": self.hindsight_bias_s,
            },
            "biasS": self.bias_s,
            "toolVersion": TOOL_VERSION,
        }


# --- audio -----------------------------------------------------------------


def load_audio(path: str | Path) -> tuple[np.ndarray, int]:
    """Mono float32 samples resampled to 44.1 kHz, and the file's own sample rate.

    Resampling (only for files not at 44.1 kHz) uses a symmetric filter, which
    hears about 2 ms ahead: an accepted lookahead well under one 10 ms frame.
    """
    import soundfile as sf

    try:
        y, sr = sf.read(str(path), dtype="float32", always_2d=True)
        y = y.mean(axis=1)
    except sf.LibsndfileError:
        import librosa

        y, sr = librosa.load(str(path), sr=None, mono=True)
    sr = int(sr)
    if sr != SR:
        import librosa

        y = librosa.resample(y, orig_sr=sr, target_sr=SR)
    return np.ascontiguousarray(y, dtype=np.float32), sr


# --- the tracker -------------------------------------------------------------


def activations(y: np.ndarray) -> np.ndarray:
    """Online beat activations, one per 10 ms frame; frame n hears audio up to n / FPS."""
    from madmom.audio.signal import Signal
    from madmom.features.beats import RNNBeatProcessor

    sig = Signal(y, sample_rate=SR, num_channels=1)
    return np.asarray(RNNBeatProcessor(online=True, origin="online", fps=FPS)(sig))


@dataclass
class Beliefs:
    """The believed grid at every activation frame (unbiased model times)."""

    #: Seconds since the last believed beat.
    since: np.ndarray
    #: Believed beat period in seconds.
    period: np.ndarray

    def last_beat(self, n: int) -> float:
        return n / FPS - float(self.since[n])


def forward_beliefs(act: np.ndarray, p: Params, chunk: int = 1000) -> Beliefs:
    """The most likely tracker state at each frame, by the forward algorithm alone."""
    from madmom.features.beats import DBNBeatTrackingProcessor

    dbn = DBNBeatTrackingProcessor(
        fps=FPS,
        min_bpm=p.min_bpm,
        max_bpm=p.max_bpm,
        num_tempi=p.num_tempi,
        online=True,
    )
    pos = dbn.st.state_positions
    itv = dbn.st.state_intervals.astype(float)
    n = len(act)
    since = np.empty(n)
    period = np.empty(n)
    dbn.hmm.reset()
    for a in range(0, n, chunk):
        fwd = dbn.hmm.forward(act[a : a + chunk], reset=False)
        s = np.argmax(fwd, axis=1)
        since[a : a + len(s)] = pos[s] * itv[s] / FPS
        period[a : a + len(s)] = itv[s] / FPS
    return Beliefs(since, smooth_periods(period, p))


def smooth_periods(period: np.ndarray, p: Params) -> np.ndarray:
    """Each frame's mean period over the past window, within the band of its own."""
    w = max(1, round(p.period_window_s * FPS))
    out = np.empty(len(period))
    for n in range(len(period)):
        past = period[max(0, n - w + 1) : n + 1]
        near = past[np.abs(past - period[n]) <= p.period_band * period[n]]
        out[n] = near.mean()
    return out


def evidence_at(
    act: np.ndarray, last: float, period: float, t: float, p: Params
) -> float:
    """Mean peak activation within ±halfS of the last `evidence_beats` believed beats heard in full by t.

    A beat less than halfS ago is not yet heard in full, so the beat before
    the oldest takes its place.
    """
    vals: list[float] = []
    for j in range(p.evidence_beats + 1):
        b = last - j * period
        if b < 0 or len(vals) == p.evidence_beats:
            break
        if b + p.evidence_half_s > t:
            continue
        lo = max(0, round((b - p.evidence_half_s) * FPS))
        hi = min(len(act) - 1, round((b + p.evidence_half_s) * FPS))
        if hi >= lo:
            vals.append(float(act[lo : hi + 1].max()))
    return float(np.mean(vals)) if vals else 0.0


def commit_beats(
    beliefs: Beliefs, evidence_of_frame: Any, p: Params
) -> list[dict[str, float]]:
    """Committed beats: each forecast fixed once within the horizon, never revised.

    At each frame the next beat after the last committed one (at least half a
    period after it, and at least `minLeadS` ahead) is committed while its
    forecast lies within `periods` periods plus `extraS` of now, and in any case
    by the last frame at or before the last committed beat: when a committed
    beat arrives, the next is always known, so the game may read the length of
    the beat it is in. `evidence_of_frame(n)` gives the evidence known at frame n.
    """
    out: list[dict[str, float]] = []
    c = -math.inf
    for n in range(len(beliefs.since)):
        t = n / FPS
        last = beliefs.last_beat(n) + p.bias_s
        per = float(beliefs.period[n])
        horizon = p.commit_periods * per + p.commit_extra_s
        while True:
            k = math.ceil(
                (max(c + 0.5 * per, t + p.commit_min_lead_s) - last) / per - 1e-9
            )
            cand = last + k * per
            # The last frame at or before the last committed beat commits the next one regardless.
            due = c - 1 / FPS < t <= c + 1e-9
            if cand - t > horizon + 1e-9 and not due:
                break
            out.append(
                {
                    "t": cand,
                    "knownAt": t,
                    "period": per,
                    "evidence": evidence_of_frame(n),
                }
            )
            c = cand
    return out


# --- ticks -------------------------------------------------------------------


def loudness_db(y: np.ndarray, t: float, window_s: float) -> float:
    """Mean-square loudness of the samples in (t - window, t], in dB."""
    hi = min(len(y), round(t * SR))
    lo = max(0, round((t - window_s) * SR))
    if hi <= lo:
        return -120.0
    seg = y[lo:hi].astype(np.float64)
    return float(10 * np.log10(np.mean(seg * seg) + 1e-12))


def energies(y: np.ndarray, times: np.ndarray, p: Params) -> np.ndarray:
    """Loudness against a running maximum that decays by decayDbPerS, mapped to 0-1."""
    out = np.empty(len(times))
    ref = p.energy_floor_db
    prev_t = 0.0
    for i, t in enumerate(times):
        level = loudness_db(y, float(t), p.energy_window_s)
        ref = max(
            level,
            ref - p.energy_decay_db_per_s * (float(t) - prev_t),
            p.energy_floor_db,
        )
        prev_t = float(t)
        out[i] = min(
            1.0, max(0.0, (level - ref + p.energy_range_db) / p.energy_range_db)
        )
    return out


# --- onsets ------------------------------------------------------------------


def detect_onsets(y: np.ndarray, p: Params) -> list[dict[str, Any]]:
    """Online superflux onsets with their strengths and strong flags."""
    from madmom.audio.signal import Signal
    from madmom.features.onsets import OnsetPeakPickingProcessor, SpectralOnsetProcessor

    sig = Signal(y, sample_rate=SR, num_channels=1)
    odf = np.asarray(
        SpectralOnsetProcessor(onset_method="superflux", fps=FPS, origin="online")(sig)
    )
    picker = OnsetPeakPickingProcessor(
        online=True,
        fps=FPS,
        threshold=p.onset_threshold,
        pre_max=0.03,
        pre_avg=0.1,
        combine=0.03,
    )
    times = np.asarray(picker(odf), dtype=float)
    out: list[dict[str, Any]] = []
    known: list[tuple[float, float]] = []
    for kt in times:
        n = round(kt * FPS)
        known_at = n / FPS
        strength = float(odf[min(n, len(odf) - 1)])
        known.append((known_at, strength))
        recent = [s for k, s in known if k >= known_at - p.strong_window_s]
        strong = strength >= float(np.percentile(recent, p.strong_percentile))
        out.append(
            {
                "t": known_at + p.onset_bias_s,
                "knownAt": known_at,
                "strength": strength,
                "strong": strong,
            }
        )
    return out


# --- hindsight grids -----------------------------------------------------------


def online_beats(beliefs: Beliefs, p: Params) -> list[tuple[float, int]]:
    """The causal tracker's believed beats as it went: (time with biasS, the frame it was believed at).

    A beat is believed when the most likely state passes it (the time since the
    last beat starts again).
    """
    since = beliefs.since
    return [
        (n / FPS - float(since[n]) + p.bias_s, n)
        for n in range(1, len(since))
        if since[n] < since[n - 1] - 1e-9
    ]


def hindsight_entries(
    y: np.ndarray, beliefs: Beliefs, p: Params
) -> list[dict[str, Any]]:
    """Every `everyS` at T: the offline decode of the audio from T - windowS to T, and the
    causal tracker's believed beats, each kept from T - keepS to T. Known at T."""
    from madmom.audio.signal import Signal
    from madmom.features.beats import DBNBeatTrackingProcessor, RNNBeatProcessor

    rnn = RNNBeatProcessor(fps=FPS)
    dbn = DBNBeatTrackingProcessor(fps=FPS, min_bpm=p.min_bpm, max_bpm=p.max_bpm)
    online = online_beats(beliefs, p)
    duration = len(y) / SR
    out: list[dict[str, Any]] = []
    k = 1
    while k * p.hindsight_every_s <= duration + 1e-9:
        t_known = round(k * p.hindsight_every_s, 6)
        a = max(0.0, t_known - p.hindsight_window_s)
        seg = y[round(a * SR) : round(t_known * SR)]
        offline: list[float] = []
        if len(seg) >= SR:
            act = rnn(Signal(seg, sample_rate=SR, num_channels=1))
            if len(act) > 100:
                offline = [float(b) + a + p.hindsight_bias_s for b in dbn(act)]
        lo = t_known - p.hindsight_keep_s
        frame = round(t_known * FPS)
        out.append(
            {
                "knownAt": t_known,
                "offline": [b for b in offline if lo <= b <= t_known],
                "online": [b for b, n in online if n <= frame and lo <= b <= t_known],
            }
        )
        k += 1
    return out


# --- the stream --------------------------------------------------------------


def build_stream(
    y: np.ndarray, p: Params | None = None
) -> dict[str, list[dict[str, Any]]]:
    """Beats, ticks and onsets of 44.1 kHz mono audio, unrounded."""
    p = p or Params()
    act = activations(y)
    beliefs = forward_beliefs(act, p)
    duration = len(y) / SR
    n_ticks = math.floor(duration * FPS / TICK_FRAMES + 1e-9)
    tick_frames = [TICK_FRAMES * k for k in range(1, n_ticks + 1)]
    tick_times = np.array([f / FPS for f in tick_frames])
    tick_evidence = [
        evidence_at(
            act,
            beliefs.last_beat(min(f, len(act) - 1)),
            float(beliefs.period[min(f, len(act) - 1)]),
            f / FPS,
            p,
        )
        for f in tick_frames
    ]

    def evidence_of_frame(n: int) -> float:
        k = n // TICK_FRAMES
        return tick_evidence[k - 1] if 1 <= k <= len(tick_evidence) else 0.0

    beats = commit_beats(beliefs, evidence_of_frame, p)
    energy = energies(y, tick_times, p)
    ticks = [
        {"t": float(t), "evidence": float(e), "energy": float(g)}
        for t, e, g in zip(tick_times, tick_evidence, energy)
    ]
    return {
        "beats": beats,
        "ticks": ticks,
        "onsets": detect_onsets(y, p),
        "hindsight": hindsight_entries(y, beliefs, p),
    }


# --- references ----------------------------------------------------------------


def songmap_reference(path: str | Path) -> dict[str, Any]:
    from . import songmap as sm

    m = sm.load(path)
    idx = sm.index_song(m)
    sections = []
    for sec in m["sections"]:
        s0, s1 = sm.section_times(idx, sec, m["audio"].get("duration"))
        sections.append(
            {
                "label": sec["label"],
                "s0": s0,
                "s1": s1,
                "feel": sec.get("feel", "pulsed"),
            }
        )
    return {
        "source": "songmap",
        "beats": [float(b) for b in m["beats"]],
        "sections": sections,
        "accents": [
            {"t": float(a["time"]), "label": a.get("label", "")}
            for a in m.get("accents", [])
        ],
    }


def offline_reference(y: np.ndarray) -> dict[str, Any]:
    """madmom's offline whole-song decode: approximate, for tracks without a song map."""
    from madmom.audio.signal import Signal
    from madmom.features.beats import DBNBeatTrackingProcessor, RNNBeatProcessor

    act = RNNBeatProcessor(fps=FPS)(Signal(y, sample_rate=SR, num_channels=1))
    beats = DBNBeatTrackingProcessor(fps=FPS, min_bpm=55, max_bpm=215)(act)
    return {"source": "offline", "beats": [float(b) for b in beats]}


# --- accuracy ------------------------------------------------------------------


def accuracy(
    beats: list[float], ref: list[float], after: float = 0.0
) -> dict[str, Any]:
    """Committed beats at t >= after, within the reference's span, against the nearest reference beat."""
    r = np.asarray(ref, dtype=float)
    if len(r) < 2:
        return {"beats": 0}
    lo = max(after, r[0] - 0.5 * (r[1] - r[0]))
    hi = r[-1] + 0.5 * (r[-1] - r[-2])
    t = np.array([b for b in beats if lo <= b <= hi])
    if len(t) == 0:
        return {"beats": 0}
    i = np.clip(np.searchsorted(r, t), 1, len(r) - 1)
    e = np.where(np.abs(t - r[i - 1]) <= np.abs(t - r[i]), t - r[i - 1], t - r[i])
    mids = (r[1:] + r[:-1]) / 2
    j = np.clip(np.searchsorted(mids, t), 1, len(mids) - 1)
    em = np.minimum(np.abs(t - mids[j - 1]), np.abs(t - mids[j]))
    ae = np.abs(e)
    near = ae[ae <= 0.06]
    return {
        "beats": len(t),
        "within20": round(float((ae <= 0.020).mean()), 4),
        "within45": round(float((ae <= 0.045).mean()), 4),
        "offbeat45": round(float(((ae > 0.045) & (em <= 0.045)).mean()), 4),
        "elsewhere": round(float(((ae > 0.045) & (em > 0.045)).mean()), 4),
        "signedMedianMs": round(float(np.median(e[ae <= 0.06]) * 1000), 2)
        if len(near)
        else None,
    }


def statistics(stream: dict[str, Any], gate: float) -> dict[str, Any]:
    """Accuracy of the committed beats against the reference, overall and per section."""
    ref = stream.get("reference")
    beats = stream["beats"]
    stats: dict[str, Any] = {
        "beats": len(beats),
        "ticks": len(stream["ticks"]),
        "onsets": len(stream["onsets"]),
        "strongOnsets": sum(1 for o in stream["onsets"] if o["strong"]),
    }
    if not ref:
        return stats
    times = [b["t"] for b in beats]
    stats["afterFourSeconds"] = accuracy(times, ref["beats"], 4.0)
    sections = ref.get("sections", [])
    pulsed = [s for s in sections if s.get("feel", "pulsed") == "pulsed"]
    if pulsed and len(pulsed) < len(sections):
        inside = [b for b in times if any(s["s0"] <= b < s["s1"] for s in pulsed)]
        stats["pulsed"] = accuracy(inside, ref["beats"])
    per = {}
    for s in sections:
        sb = [b for b in beats if s["s0"] <= b["t"] < s["s1"]]
        row = accuracy([b["t"] for b in sb], ref["beats"])
        row["belowGate"] = (
            round(sum(1 for b in sb if b["evidence"] < gate) / len(sb), 4)
            if sb
            else None
        )
        per[s["label"]] = row
    if per:
        stats["sections"] = per
    return stats


# --- the file ------------------------------------------------------------------


def _r(x: float, digits: int = 4) -> float:
    v = round(float(x), digits)
    return 0.0 if v == 0 else v


def to_document(
    stream: dict[str, Any],
    audio_rel: str,
    duration: float,
    sample_rate: int,
    p: Params,
    reference: dict[str, Any] | None,
) -> dict[str, Any]:
    """The stream file's JSON, with times and values rounded to 0.1 ms and 1e-4."""
    doc: dict[str, Any] = {
        "version": 1,
        "audio": {
            "file": audio_rel,
            "duration": round(duration, 6),
            "sampleRate": sample_rate,
        },
        "params": p.to_json(),
        "beats": [
            {
                "t": _r(b["t"]),
                "knownAt": _r(b["knownAt"], 2),
                "period": _r(b["period"]),
                "evidence": _r(b["evidence"]),
            }
            for b in stream["beats"]
        ],
        "ticks": [
            {
                "t": _r(k["t"], 2),
                "evidence": _r(k["evidence"]),
                "energy": _r(k["energy"]),
            }
            for k in stream["ticks"]
        ],
        "onsets": [
            {
                "t": _r(o["t"]),
                "knownAt": _r(o["knownAt"], 2),
                "strength": _r(o["strength"]),
                "strong": o["strong"],
            }
            for o in stream["onsets"]
        ],
    }
    if "hindsight" in stream:
        doc["hindsight"] = [
            {
                "knownAt": _r(h["knownAt"], 2),
                "offline": [_r(b) for b in h["offline"]],
                "online": [_r(b) for b in h["online"]],
            }
            for h in stream["hindsight"]
        ]
    if reference:
        ref: dict[str, Any] = {
            "source": reference["source"],
            "beats": [_r(b) for b in reference["beats"]],
        }
        if "sections" in reference:
            ref["sections"] = [
                {**s, "s0": _r(s["s0"]), "s1": _r(s["s1"])}
                for s in reference["sections"]
            ]
        if "accents" in reference:
            ref["accents"] = [{**a, "t": _r(a["t"])} for a in reference["accents"]]
        doc["reference"] = ref
    return doc


def write_document(doc: dict[str, Any], out: str | Path) -> None:
    """One entry per line, so diffs of regenerated streams stay readable."""
    lines = ["{"]
    keys = list(doc)
    for i, key in enumerate(keys):
        comma = "," if i < len(keys) - 1 else ""
        value = doc[key]
        if isinstance(value, list):
            lines.append(f' "{key}": [')
            for j, item in enumerate(value):
                lines.append(
                    "  "
                    + json.dumps(item, separators=(",", ":"))
                    + ("," if j < len(value) - 1 else "")
                )
            lines.append(" ]" + comma)
        elif key == "reference":
            lines.append(f' "{key}": {{')
            sub = list(value)
            for j, sk in enumerate(sub):
                lines.append(
                    f'  "{sk}": '
                    + json.dumps(value[sk], separators=(",", ":"))
                    + ("," if j < len(sub) - 1 else "")
                )
            lines.append(" }" + comma)
        else:
            lines.append(
                f' "{key}": ' + json.dumps(value, separators=(",", ":")) + comma
            )
    lines.append("}")
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    Path(out).write_text("\n".join(lines) + "\n")


def relative_audio(audio: str | Path, out: str | Path) -> str:
    return Path(
        os.path.relpath(Path(audio).resolve(), Path(out).resolve().parent)
    ).as_posix()


@dataclass
class StreamJob:
    audio: Path
    out: Path
    reference: dict[str, Any] | None = None
    #: Compute an offline reference from the audio (when `reference` is None).
    offline_reference: bool = False
    #: Write the analysed samples here as FLAC and name it as the stream's
    #: audio, so the game plays exactly what was analysed (a compressed file's
    #: encoder delay may decode differently in the browser).
    audio_out: Path | None = None
    params: Params = field(default_factory=Params)


def run(job: StreamJob) -> dict[str, Any]:
    """Analyse the audio, write the stream file and return its statistics."""
    y, sr = load_audio(job.audio)
    stream: dict[str, Any] = build_stream(y, job.params)
    if job.reference is None and job.offline_reference:
        job.reference = offline_reference(y)
    audio = job.audio
    if job.audio_out is not None:
        import soundfile as sf

        job.audio_out.parent.mkdir(parents=True, exist_ok=True)
        sf.write(job.audio_out, y, SR, format="FLAC", subtype="PCM_24")
        audio, sr = job.audio_out, SR
    doc = to_document(
        stream,
        relative_audio(audio, job.out),
        len(y) / SR,
        sr,
        job.params,
        job.reference,
    )
    write_document(doc, job.out)
    if job.reference:
        stream["reference"] = job.reference
    return {"out": str(job.out), **statistics(stream, job.params.gate)}
