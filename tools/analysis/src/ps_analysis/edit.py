"""Named, reviewable corrections applied to a song map.

An edit list is a JSON array of operations applied in order. Beat indices
always refer to the beat list *as it is when that operation runs* (so an
`insertBeat` shifts the indices of every later beat for later operations).
Bar numbers likewise follow the current beats, meter and first downbeat.

Beat-grid operations (energy is recomputed afterwards when audio is given):

- `{"op": "shift", "beats": [a, b], "byMs": ms}`: move beats a..b (inclusive).
- `{"op": "insertBeat", "after": i, "time"?: s}`: insert a beat after beat i,
  at `time` or halfway to beat i+1.
- `{"op": "deleteBeat", "index": i}`.
- `{"op": "deleteBeats", "from": a, "to": b}`: delete beats a..b (inclusive).
- `{"op": "moveBeat", "index": i, "time": s}`: set one beat's time.
- `{"op": "respace", "from": a, "to": b, "count"?: n}`: keep beats a and b and
  space the beats between them evenly. With `count`, there are n gaps between
  them afterwards (inserting or removing beats); otherwise the beat count is
  unchanged.
- `{"op": "subdivide", "from": a, "to": b, "parts": n}`: split every gap
  between beats a and b into n equal gaps (fixes half-tempo octave errors).
- `{"op": "spliceTracker", "tracker": name, "fromTime": s0, "toTime": s1}`:
  replace the beats in [s0, s1] with the named tracker's beats from the
  analysis file (needs `--analysis`).
- `{"op": "smooth", "from": a, "to": b, "window": w}`: replace beats a..b by a
  centred moving linear fit over ±w beats (removes jitter while following
  tempo drift; the end beats a and b are kept).
- `{"op": "fitOnsets", "from": a, "to": b, "windowMs"?: 45, "maxChange"?: 0.075}`:
  move beats a..b onto nearby onset peaks (within `windowMs`) where that keeps
  every gap within `maxChange` of the previous one (ignored inside marked
  tempo changes). A dynamic programme picks, for every beat, its current time
  or a time at or near a peak, minimising the number of beats more than 25 ms
  from a peak plus small displacement and smoothness penalties. Beats a-2,
  a-1, b+1 and b+2 stay fixed and are included in the smoothness constraint.
- `{"op": "extend", "until": s}`: append beats at the last gap while they are
  at or before time s.
- `{"op": "setFirstDownbeat", "index": i}`.

Structure operations:

- `{"op": "setMeter", "meter": [{"fromBar": 1, "beatsPerBar": 4}, ...]}`.
- `{"op": "setSection", "label": l, "bars": [a, b], "feel": f, "key"?: k}`:
  create or replace the section with this label (anchors are kept on
  replace); sections are kept sorted by first bar.
- `{"op": "removeSection", "label": l}`.
- `{"op": "setPhrases", "phrases": [[a, b], ...]}`.
- `{"op": "setTempoChanges", "tempoChanges": [{"bars": [a, b]}, ...]}`.

Accent and anchor operations (`snapMs` snaps the time to the nearest onset
peak within that many milliseconds; it needs audio):

- `{"op": "addAccent", "time": s, "strength": x, "label"?: l, "snapMs"?: ms}`.
- `{"op": "removeAccent", "time": s, "toleranceMs"?: ms}`.
- `{"op": "setAccentLabel", "time": s, "label": l, "toleranceMs"?: ms}`.
- `{"op": "clearAccents"}`.
- `{"op": "addAnchor", "time": s, "strength": x, "snapMs"?: ms}`: added to the
  section containing the time, which must be free.
- `{"op": "clearAnchors", "label"?: l}`.
"""

from __future__ import annotations

import copy
from collections.abc import Callable
from typing import Any

import numpy as np

from . import songmap as sm
from .audio import Onsets


class EditError(Exception):
    pass


class Context:
    """Optional inputs some operations need."""

    def __init__(
        self,
        onsets: Onsets | None = None,
        trackers: dict[str, list[float]] | None = None,
    ):
        self.onsets = onsets
        self.trackers = trackers or {}

    def snap(self, t: float, snap_ms: float | None) -> float:
        if snap_ms is None:
            return t
        if self.onsets is None:
            raise EditError("snapMs needs the audio (onset peaks)")
        pt, off = self.onsets.nearest_peak(t)
        if abs(off) * 1000 > snap_ms:
            raise EditError(
                f"no onset peak within {snap_ms} ms of {t:.3f} s (nearest {pt:.3f} s)"
            )
        return pt


def _beats(m: sm.SongMap) -> list[float]:
    return m["beats"]


def _check_index(m: sm.SongMap, i: int, name: str = "index") -> None:
    if not (0 <= i < len(m["beats"])):
        raise EditError(f"{name} {i} out of range 0..{len(m['beats']) - 1}")


def op_shift(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    a, b = e["beats"]
    _check_index(m, a)
    _check_index(m, b)
    d = e["byMs"] / 1000.0
    for i in range(a, b + 1):
        m["beats"][i] += d


def op_insert_beat(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    i = e["after"]
    _check_index(m, i)
    beats = _beats(m)
    if "time" in e:
        t = float(e["time"])
    elif i + 1 < len(beats):
        t = (beats[i] + beats[i + 1]) / 2
    else:
        t = beats[i] + (beats[i] - beats[i - 1])
    beats.insert(i + 1, t)


def op_delete_beat(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    i = e["index"]
    _check_index(m, i)
    del m["beats"][i]


def op_delete_beats(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    a, b = e["from"], e["to"]
    _check_index(m, a, "from")
    _check_index(m, b, "to")
    del m["beats"][a : b + 1]


def op_move_beat(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    i = e["index"]
    _check_index(m, i)
    m["beats"][i] = float(e["time"])


def op_respace(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    a, b = e["from"], e["to"]
    _check_index(m, a, "from")
    _check_index(m, b, "to")
    if b <= a:
        raise EditError("respace needs from < to")
    beats = _beats(m)
    n = int(e.get("count", b - a))
    if n < 1:
        raise EditError("respace count must be >= 1")
    t0, t1 = beats[a], beats[b]
    new = [t0 + (t1 - t0) * k / n for k in range(n + 1)]
    beats[a : b + 1] = new


def op_subdivide(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    a, b = e["from"], e["to"]
    _check_index(m, a, "from")
    _check_index(m, b, "to")
    parts = int(e["parts"])
    if parts < 2 or b <= a:
        raise EditError("subdivide needs parts >= 2 and from < to")
    beats = _beats(m)
    new: list[float] = []
    for i in range(a, b):
        for k in range(parts):
            new.append(beats[i] + (beats[i + 1] - beats[i]) * k / parts)
    new.append(beats[b])
    beats[a : b + 1] = new


def op_splice_tracker(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    name = e["tracker"]
    if name not in ctx.trackers:
        raise EditError(
            f"tracker {name!r} not available (pass --analysis); have {sorted(ctx.trackers)}"
        )
    s0, s1 = float(e["fromTime"]), float(e["toTime"])
    src = [t for t in ctx.trackers[name] if s0 <= t <= s1]
    keep_before = [t for t in m["beats"] if t < s0]
    keep_after = [t for t in m["beats"] if t > s1]
    m["beats"] = keep_before + src + keep_after


def op_smooth(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    a, b = e["from"], e["to"]
    _check_index(m, a, "from")
    _check_index(m, b, "to")
    w = int(e.get("window", 4))
    beats = np.asarray(m["beats"], dtype=float)
    out = beats.copy()
    for i in range(a + 1, b):
        lo, hi = max(a, i - w), min(b, i + w)
        x = np.arange(lo, hi + 1)
        coef = np.polyfit(x - i, beats[lo : hi + 1], 1)
        out[i] = coef[1]
    m["beats"] = [float(t) for t in out]


def _in_tempo_change(m: sm.SongMap, idx: sm.SongIndex, beat: int) -> bool:
    for tc in m["tempoChanges"]:
        a, b = tc["bars"]
        if a <= idx.bar_count and idx.bar_start[a] <= beat <= idx.bar_end_beat(
            min(b, idx.bar_count)
        ):
            return True
    return False


def op_fit_onsets(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    if ctx.onsets is None:
        raise EditError("fitOnsets needs the audio (onset peaks)")
    a, b = e["from"], e["to"]
    _check_index(m, a, "from")
    _check_index(m, b, "to")
    window = float(e.get("windowMs", 45)) / 1000
    max_change = float(e.get("maxChange", 0.075))
    align = 0.025
    peaks = ctx.onsets.peak_times
    beats = list(m["beats"])
    idx = sm.index_song(m)
    lo, hi = max(0, a - 2), min(len(beats) - 1, b + 2)

    def aligned(t: float) -> bool:
        _, off = ctx.onsets.nearest_peak(t)
        return abs(off) <= align

    cands: list[list[tuple[float, float]]] = []
    for i in range(lo, hi + 1):
        t = beats[i]
        if i < a or i > b:
            cands.append([(t, 0.0)])
            continue
        opts = {round(t, 5): 0.0 if aligned(t) else 1.0}
        for p in peaks[(peaks >= t - window) & (peaks <= t + window)]:
            for x in (p, p + float(np.clip(t - p, -0.02, 0.02))):
                opts.setdefault(round(float(x), 5), 0.0)
        cands.append([(x, c + 2.0 * abs(x - t)) for x, c in opts.items()])

    n = len(cands)
    # Second-order Viterbi over (previous candidate, current candidate).
    inf = float("inf")
    cost: dict[tuple[int, int], float] = {}
    back: list[dict[tuple[int, int], int]] = [{} for _ in range(n)]
    if n == 1:
        return
    for j0, (x0, c0) in enumerate(cands[0]):
        for j1, (x1, c1) in enumerate(cands[1]):
            if x1 > x0:
                cost[(j0, j1)] = c0 + c1
    for k in range(2, n):
        exempt = any(_in_tempo_change(m, idx, lo + k - d) for d in (0, 1, 2))
        new: dict[tuple[int, int], float] = {}
        for (jp, jc), cv in cost.items():
            xp, xc = cands[k - 2][jp][0], cands[k - 1][jc][0]
            g0 = xc - xp
            for jn, (xn, cn) in enumerate(cands[k]):
                g1 = xn - xc
                if g1 <= 0:
                    continue
                r = g1 / g0 - 1
                if abs(r) > max_change and not exempt:
                    continue
                v = cv + cn + (0.0 if exempt else 4.0 * r * r)
                if v < new.get((jc, jn), inf):
                    new[(jc, jn)] = v
                    back[k][(jc, jn)] = jp
        if not new:
            raise EditError(
                f"no smooth fit around beat {lo + k} ({beats[lo + k]:.3f} s); try a larger maxChange"
            )
        cost = new
    (jp, jc), _ = min(cost.items(), key=lambda kv: kv[1])
    path = [jc, jp]
    for k in range(n - 1, 1, -1):
        jpp = back[k][(path[-1], path[-2])]
        path.append(jpp)
    path.reverse()
    for k, j in enumerate(path):
        beats[lo + k] = cands[k][j][0]
    m["beats"] = beats


def op_extend(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    beats = _beats(m)
    gap = beats[-1] - beats[-2]
    until = float(e["until"])
    while beats[-1] + gap <= until + 1e-9:
        beats.append(beats[-1] + gap)


def op_set_first_downbeat(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    _check_index(m, e["index"])
    m["firstDownbeatIndex"] = int(e["index"])


def op_set_meter(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    m["meter"] = sorted(copy.deepcopy(e["meter"]), key=lambda r: r["fromBar"])


def op_set_section(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    if e.get("feel") not in sm.FEELS:
        raise EditError(f"feel must be one of {sm.FEELS}")
    old = sm.section_by_label(m, e["label"])
    sec: dict[str, Any] = {
        "label": e["label"],
        "bars": list(e["bars"]),
        "feel": e["feel"],
    }
    if e.get("key"):
        sec["key"] = e["key"]
    elif old and old.get("key"):
        sec["key"] = old["key"]
    if old and old.get("anchors"):
        sec["anchors"] = old["anchors"]
    m["sections"] = [s for s in m["sections"] if s["label"] != e["label"]] + [sec]
    m["sections"].sort(key=lambda s: s["bars"][0])


def op_remove_section(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    if not sm.section_by_label(m, e["label"]):
        raise EditError(f"no section {e['label']!r}")
    m["sections"] = [s for s in m["sections"] if s["label"] != e["label"]]


def op_set_phrases(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    m["phrases"] = sorted([list(p) for p in e["phrases"]])


def op_set_tempo_changes(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    m["tempoChanges"] = [{"bars": list(tc["bars"])} for tc in e["tempoChanges"]]


def _find_accent(m: sm.SongMap, t: float, tol_ms: float) -> int:
    best = None
    for k, a in enumerate(m["accents"]):
        d = abs(a["time"] - t)
        if d * 1000 <= tol_ms and (best is None or d < best[0]):
            best = (d, k)
    if best is None:
        raise EditError(f"no accent within {tol_ms} ms of {t:.3f} s")
    return best[1]


def op_add_accent(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    t = ctx.snap(float(e["time"]), e.get("snapMs"))
    acc: dict[str, Any] = {"time": t, "strength": float(e["strength"])}
    if e.get("label"):
        acc["label"] = e["label"]
    m["accents"].append(acc)
    m["accents"].sort(key=lambda a: a["time"])


def op_remove_accent(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    k = _find_accent(m, float(e["time"]), float(e.get("toleranceMs", 30)))
    del m["accents"][k]


def op_set_accent_label(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    k = _find_accent(m, float(e["time"]), float(e.get("toleranceMs", 30)))
    if e.get("label"):
        m["accents"][k]["label"] = e["label"]
    else:
        m["accents"][k].pop("label", None)


def op_clear_accents(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    m["accents"] = []


def op_add_anchor(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    t = ctx.snap(float(e["time"]), e.get("snapMs"))
    idx = sm.index_song(m)
    sec = sm.section_at_time(idx, t)
    if sec is None:
        raise EditError("no sections defined")
    if sec["feel"] != "free":
        raise EditError(f"anchor at {t:.3f} s falls in pulsed section {sec['label']!r}")
    sec.setdefault("anchors", []).append({"time": t, "strength": float(e["strength"])})
    sec["anchors"].sort(key=lambda a: a["time"])


def op_clear_anchors(m: sm.SongMap, e: dict[str, Any], ctx: Context) -> None:
    for s in m["sections"]:
        if e.get("label") in (None, s["label"]):
            s.pop("anchors", None)


OPS: dict[str, Callable[[sm.SongMap, dict[str, Any], Context], None]] = {
    "shift": op_shift,
    "insertBeat": op_insert_beat,
    "deleteBeat": op_delete_beat,
    "deleteBeats": op_delete_beats,
    "moveBeat": op_move_beat,
    "respace": op_respace,
    "subdivide": op_subdivide,
    "spliceTracker": op_splice_tracker,
    "smooth": op_smooth,
    "extend": op_extend,
    "fitOnsets": op_fit_onsets,
    "setFirstDownbeat": op_set_first_downbeat,
    "setMeter": op_set_meter,
    "setSection": op_set_section,
    "removeSection": op_remove_section,
    "setPhrases": op_set_phrases,
    "setTempoChanges": op_set_tempo_changes,
    "addAccent": op_add_accent,
    "removeAccent": op_remove_accent,
    "setAccentLabel": op_set_accent_label,
    "clearAccents": op_clear_accents,
    "addAnchor": op_add_anchor,
    "clearAnchors": op_clear_anchors,
}

BEAT_OPS = {
    "shift",
    "insertBeat",
    "deleteBeat",
    "deleteBeats",
    "moveBeat",
    "respace",
    "subdivide",
    "spliceTracker",
    "smooth",
    "extend",
    "fitOnsets",
}


def apply_edits(
    m: sm.SongMap, edits: list[dict[str, Any]], ctx: Context | None = None
) -> tuple[sm.SongMap, bool]:
    """Apply edits to a copy of m. Returns (new map, whether beats changed)."""
    ctx = ctx or Context()
    out = copy.deepcopy(m)
    beats_changed = False
    for n, e in enumerate(edits):
        op = e.get("op")
        if op not in OPS:
            raise EditError(f"edit {n}: unknown op {op!r}")
        try:
            OPS[op](out, e, ctx)
        except EditError as err:
            raise EditError(f"edit {n} ({op}): {err}") from None
        except (KeyError, TypeError, ValueError) as err:
            raise EditError(f"edit {n} ({op}): bad arguments: {err!r}") from None
        if op in BEAT_OPS:
            beats_changed = True
            b = out["beats"]
            for i in range(1, len(b)):
                if not b[i] > b[i - 1]:
                    raise EditError(
                        f"edit {n} ({op}): beats no longer strictly increasing at beat {i}"
                    )
    return out, beats_changed
