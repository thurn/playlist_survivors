"""Song map validation: structure plus the acceptance thresholds.

Thresholds (docs/design/tech-demo.md, "Acceptance thresholds"):

- Onset alignment (pulsed sections): at least 90% of beats are within 25 ms of
  an onset peak, counting only beats with a peak within 80 ms.
- Smoothness (pulsed sections): each gap between consecutive beats differs
  from the previous gap by at most 8%, except inside a marked tempo change.
- Structure: sections cover bars 1..lastBar without gaps or overlaps (bar
  ranges, so they start on downbeats by construction).
- Anchors: every free section has at least one anchor per 8 seconds (no gap
  longer than 8 s between the section start, its anchors and its end) and
  each anchor is within 30 ms of an onset peak.
- Accents: every accent is within 20 ms of an onset peak.

`check_map` is pure: it takes the onset peak times, so tests can use synthetic
data. `run_check` loads the audio and computes them.
"""

from __future__ import annotations

import itertools
from dataclasses import dataclass, field
from typing import Any

import numpy as np

from . import songmap as sm

ALIGN_MS = 25.0
ALIGN_WINDOW_MS = 80.0
ALIGN_MIN_FRACTION = 0.90
SMOOTH_MAX = 0.08
ANCHOR_GAP_S = 8.0
ANCHOR_MS = 30.0
ACCENT_MS = 20.0


@dataclass
class CheckResult:
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    sections: list[dict[str, Any]] = field(default_factory=list)
    accents: dict[str, Any] = field(default_factory=dict)

    @property
    def ok(self) -> bool:
        return not self.errors

    def to_json(self) -> dict[str, Any]:
        return {
            "ok": self.ok,
            "errors": self.errors,
            "warnings": self.warnings,
            "sections": self.sections,
            "accents": self.accents,
        }


def _nearest_offsets(peaks: np.ndarray, times: np.ndarray) -> np.ndarray:
    """Signed offset (time - nearest peak) in seconds for each time."""
    if len(peaks) == 0:
        return np.full(len(times), np.inf)
    k = np.searchsorted(peaks, times)
    lo = np.clip(k - 1, 0, len(peaks) - 1)
    hi = np.clip(k, 0, len(peaks) - 1)
    d_lo = times - peaks[lo]
    d_hi = times - peaks[hi]
    return np.where(np.abs(d_lo) <= np.abs(d_hi), d_lo, d_hi)


def check_structure(
    m: sm.SongMap, duration: float | None = None
) -> tuple[list[str], list[str]]:
    errors: list[str] = []
    warnings: list[str] = []
    if m.get("version") != 1:
        errors.append(f"version must be 1, got {m.get('version')!r}")
    for key in (
        "song",
        "audio",
        "meter",
        "beats",
        "firstDownbeatIndex",
        "sections",
        "phrases",
        "tempoChanges",
        "energy",
        "accents",
    ):
        if key not in m:
            errors.append(f"missing field {key!r}")
    if errors:
        return errors, warnings
    audio = m["audio"]
    for key in ("file", "source", "cropStart", "cropEnd", "sampleRate", "duration"):
        if key not in audio:
            errors.append(f"audio: missing field {key!r}")
    beats = m["beats"]
    if len(beats) < 2:
        errors.append("need at least 2 beats")
        return errors, warnings
    for i in range(1, len(beats)):
        if not beats[i] > beats[i - 1]:
            errors.append(
                f"beats not strictly increasing at beat {i} ({beats[i - 1]:.4f} -> {beats[i]:.4f})"
            )
    if beats[0] < 0:
        errors.append(f"beat 0 is before the start of the file ({beats[0]:.4f} s)")
    dur = duration if duration is not None else audio.get("duration")
    if dur is not None and beats[-1] > dur:
        errors.append(
            f"last beat {beats[-1]:.4f} s is after the end of the file ({dur:.4f} s)"
        )
    if len(m["energy"]) != len(beats):
        errors.append(
            f"energy has {len(m['energy'])} values but there are {len(beats)} beats"
        )
    elif any(not (0 <= e <= 1) for e in m["energy"]):
        errors.append("energy values must be in 0..1")
    fdi = m["firstDownbeatIndex"]
    if not (isinstance(fdi, int) and 0 <= fdi < len(beats)):
        errors.append(f"firstDownbeatIndex {fdi!r} out of range")
        return errors, warnings
    meter = m["meter"]
    if not meter or min(r["fromBar"] for r in meter) != 1:
        errors.append("meter must have a region starting at bar 1")
        return errors, warnings
    from_bars = [r["fromBar"] for r in meter]
    if from_bars != sorted(set(from_bars)):
        errors.append("meter regions must be sorted by fromBar without duplicates")
    if any(r["beatsPerBar"] < 1 for r in meter):
        errors.append("beatsPerBar must be >= 1")
        return errors, warnings

    idx = sm.index_song(m)
    last = idx.bar_count
    if not idx.last_bar_complete():
        warnings.append(f"last bar {last} is incomplete (extend the beats to its end)")
    if any(r["fromBar"] > last for r in meter):
        errors.append(f"meter region starts after the last bar {last}")

    # Sections: contiguous cover of bars 1..last.
    secs = m["sections"]
    if not secs:
        errors.append("no sections")
        return errors, warnings
    labels = [s["label"] for s in secs]
    if len(set(labels)) != len(labels):
        errors.append(f"duplicate section labels: {labels}")
    expect = 1
    for s in secs:
        a, b = s["bars"]
        if s.get("feel") not in sm.FEELS:
            errors.append(f"section {s['label']}: feel must be one of {sm.FEELS}")
        if a > b:
            errors.append(f"section {s['label']}: bars {a}..{b} are reversed")
        if a != expect:
            kind = "gap" if a > expect else "overlap"
            errors.append(
                f"section {s['label']}: starts at bar {a}, expected bar {expect} ({kind})"
            )
        expect = b + 1
    if expect - 1 != last:
        errors.append(f"sections end at bar {expect - 1} but the song has {last} bars")

    # Phrases: sorted, non-overlapping, each inside one section.
    prev_end = 0
    for p in m["phrases"]:
        a, b = p
        if a > b:
            errors.append(f"phrase {p} is reversed")
        if a <= prev_end:
            errors.append(f"phrase {p} overlaps or precedes the previous phrase")
        prev_end = b
        if not any(s["bars"][0] <= a and b <= s["bars"][1] for s in secs):
            errors.append(f"phrase {p} is not inside a single section")

    for tc in m["tempoChanges"]:
        a, b = tc["bars"]
        if not (1 <= a <= b <= last):
            errors.append(f"tempo change {tc['bars']} is outside bars 1..{last}")

    # Anchors only in free sections, inside the section's time span.
    for s in secs:
        anchors = s.get("anchors") or []
        if anchors and s.get("feel") != "free":
            errors.append(
                f"section {s['label']}: anchors are only allowed in free sections"
            )
        if not anchors:
            continue
        if s["bars"][0] < 1 or s["bars"][1] > last:
            continue
        start, end = sm.section_times(idx, s, dur)
        for a in anchors:
            if not (start - 1e-6 <= a["time"] <= end + 1e-6):
                errors.append(
                    f"section {s['label']}: anchor at {a['time']:.3f} s is outside the section "
                    f"({start:.3f}–{end:.3f} s)"
                )
            if not (0 <= a["strength"] <= 1):
                errors.append(
                    f"section {s['label']}: anchor strength {a['strength']} not in 0..1"
                )

    for a in m["accents"]:
        if not (0 <= a["strength"] <= 1):
            errors.append(
                f"accent at {a['time']:.3f} s: strength {a['strength']} not in 0..1"
            )
        if dur is not None and not (0 <= a["time"] <= dur):
            errors.append(f"accent at {a['time']:.3f} s is outside the file")
    return errors, warnings


def _in_tempo_change(idx: sm.SongIndex, beat: int) -> bool:
    for tc in idx.map["tempoChanges"]:
        a, b = tc["bars"]
        if idx.bar_start[a] <= beat <= idx.bar_end_beat(b):
            return True
    return False


def section_stats(
    idx: sm.SongIndex, sec: dict[str, Any], peaks: np.ndarray, duration: float | None
) -> tuple[dict[str, Any], list[str]]:
    """Per-section statistics and threshold failures."""
    m = idx.map
    beats = np.asarray(m["beats"], dtype=float)
    a, b = sec["bars"]
    start_beat, end_beat = idx.beat_range(a, b)
    t0, t1 = sm.section_times(idx, sec, duration)
    label = sec["label"]
    errors: list[str] = []
    st: dict[str, Any] = {
        "label": label,
        "bars": [a, b],
        "feel": sec["feel"],
        "key": sec.get("key"),
        "start": round(t0, 3),
        "end": round(t1, 3),
        "beats": [start_beat, end_beat - 1],
        "beatCount": end_beat - start_beat,
    }
    sb = beats[start_beat:end_beat]
    gaps = np.diff(beats[start_beat : min(end_beat + 1, len(beats))])
    if len(gaps):
        st["medianBeatMs"] = round(float(np.median(gaps)) * 1000, 1)
        st["bpm"] = round(60.0 / float(np.median(gaps)), 1)

    offs = _nearest_offsets(peaks, sb)
    near = np.abs(offs) <= ALIGN_WINDOW_MS / 1000
    aligned = np.abs(offs) <= ALIGN_MS / 1000
    counted = int(near.sum())
    frac = float(aligned.sum() / counted) if counted else 1.0
    st["onsetAlignment"] = {
        "counted": counted,
        "aligned": int(aligned.sum()),
        "fraction": round(frac, 3),
        "medianAbsOffsetMs": round(float(np.median(np.abs(offs[near]))) * 1000, 1)
        if counted
        else None,
        "meanOffsetMs": round(float(np.mean(offs[near])) * 1000, 1)
        if counted
        else None,
    }

    # Smoothness over consecutive gap pairs; a pair is centred on its shared beat.
    ratios = []
    worst = None
    bad = []
    for j in range(1, len(gaps)):
        i = start_beat + j  # shared beat between gap j-1 and gap j
        r = gaps[j] / gaps[j - 1] - 1.0
        exempt = (
            _in_tempo_change(idx, i - 1)
            or _in_tempo_change(idx, i)
            or _in_tempo_change(idx, i + 1)
        )
        if exempt:
            continue
        ratios.append(abs(r))
        if worst is None or abs(r) > abs(worst[1]):
            worst = (i, r)
        if abs(r) > SMOOTH_MAX:
            bad.append((i, r))
    st["smoothness"] = {
        "pairs": len(ratios),
        "maxChange": round(max(ratios), 4) if ratios else 0.0,
        "p95Change": round(float(np.percentile(ratios, 95)), 4) if ratios else 0.0,
        "violations": len(bad),
    }

    if sec["feel"] == "pulsed":
        if frac < ALIGN_MIN_FRACTION:
            worst_beats = sorted(
                [
                    (abs(o), start_beat + k)
                    for k, o in enumerate(offs)
                    if near[k] and not aligned[k]
                ],
                reverse=True,
            )[:5]
            desc = ", ".join(
                f"{sm.describe_beat(idx, i)} off by {o * 1000:.0f} ms"
                for o, i in worst_beats
            )
            errors.append(
                f"section {label}: onset alignment {frac:.1%} < {ALIGN_MIN_FRACTION:.0%} "
                f"({int(aligned.sum())}/{counted}); worst: {desc}"
            )
        for i, r in bad[:10]:
            errors.append(
                f"section {label}: {sm.describe_beat(idx, i)}: gap changes by {r:+.1%} "
                f"({gaps[i - start_beat - 1] * 1000:.0f} -> {gaps[i - start_beat] * 1000:.0f} ms), "
                f"limit {SMOOTH_MAX:.0%}"
            )
        if len(bad) > 10:
            errors.append(
                f"section {label}: ... and {len(bad) - 10} more smoothness violations"
            )
    else:
        anchors = sorted(sec.get("anchors") or [], key=lambda x: x["time"])
        times = [x["time"] for x in anchors]
        edges = [t0, *times, t1]
        max_gap = max(e2 - e1 for e1, e2 in itertools.pairwise(edges))
        a_offs = (
            _nearest_offsets(peaks, np.asarray(times, dtype=float))
            if times
            else np.array([])
        )
        st["anchors"] = {
            "count": len(anchors),
            "maxGapS": round(max_gap, 2),
            "maxOnsetOffsetMs": round(float(np.max(np.abs(a_offs))) * 1000, 1)
            if times
            else None,
        }
        if max_gap > ANCHOR_GAP_S + 1e-9:
            gi = int(np.argmax([e2 - e1 for e1, e2 in itertools.pairwise(edges)]))
            errors.append(
                f"section {label}: {max_gap:.2f} s without an anchor between {edges[gi]:.3f} and "
                f"{edges[gi + 1]:.3f} s (limit {ANCHOR_GAP_S:.0f} s)"
            )
        for t, o in zip(times, a_offs):
            if abs(o) > ANCHOR_MS / 1000:
                errors.append(
                    f"section {label}: anchor at {t:.3f} s is {abs(o) * 1000:.0f} ms from the nearest "
                    f"onset peak (limit {ANCHOR_MS:.0f} ms)"
                )
    return st, errors


def check_map(
    m: sm.SongMap, peaks: np.ndarray, duration: float | None = None
) -> CheckResult:
    """Validate a song map against onset peak times (seconds, sorted)."""
    res = CheckResult()
    peaks = np.sort(np.asarray(peaks, dtype=float))
    errs, warns = check_structure(m, duration)
    res.errors += errs
    res.warnings += warns
    if errs:
        return res
    idx = sm.index_song(m)
    dur = duration if duration is not None else m["audio"].get("duration")
    for sec in m["sections"]:
        st, errs = section_stats(idx, sec, peaks, dur)
        res.sections.append(st)
        res.errors += errs

    acc = m["accents"]
    a_times = np.asarray([a["time"] for a in acc], dtype=float)
    a_offs = _nearest_offsets(peaks, a_times) if len(acc) else np.array([])
    for a, o in zip(acc, a_offs):
        if abs(o) > ACCENT_MS / 1000:
            res.errors.append(
                f"accent at {a['time']:.3f} s ({a.get('label', 'unlabelled')}) is {abs(o) * 1000:.0f} ms "
                f"from the nearest onset peak (limit {ACCENT_MS:.0f} ms)"
            )
    labels: dict[str, int] = {}
    for a in acc:
        if a.get("label"):
            labels[a["label"]] = labels.get(a["label"], 0) + 1
    res.accents = {
        "count": len(acc),
        "labels": labels,
        "maxOnsetOffsetMs": round(float(np.max(np.abs(a_offs))) * 1000, 1)
        if len(acc)
        else None,
    }
    if not (20 <= len(acc) <= 40):
        res.warnings.append(
            f"{len(acc)} accents; the workflow keeps the strongest 20–40"
        )
    return res


def format_text(res: CheckResult) -> str:
    lines = []
    for st in res.sections:
        line = (
            f"{st['label']:<16} bars {st['bars'][0]:>3}-{st['bars'][1]:<3} {st['feel']:<6} "
            f"{st['start']:8.3f}-{st['end']:8.3f} s  {st.get('bpm', 0):6.1f} bpm"
        )
        if st["feel"] == "pulsed":
            oa = st["onsetAlignment"]
            sm_ = st["smoothness"]
            line += (
                f"  align {oa['fraction']:.1%} ({oa['aligned']}/{oa['counted']})"
                f"  max gap change {sm_['maxChange']:.1%}"
            )
        else:
            an = st["anchors"]
            line += f"  anchors {an['count']} max gap {an['maxGapS']:.2f} s"
        lines.append(line)
    if res.accents:
        lines.append(f"accents: {res.accents['count']} {res.accents['labels']}")
    for w in res.warnings:
        lines.append(f"WARNING: {w}")
    for e in res.errors:
        lines.append(f"FAIL: {e}")
    lines.append("check: OK" if res.ok else f"check: FAILED ({len(res.errors)} errors)")
    return "\n".join(lines)
