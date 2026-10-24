"""`delayed-payoff`: Delayed listening's payoff, measured offline (docs/design/native-app.md, E0).

Delayed listening plays the music `M` seconds late, so committed beats can come
from hindsight grids instead of forecasts. This replays E1's commit rule over a
committed stream's hindsight entries, with no audio:

- An entry whose window ends at `T` (its `knownAt`) is usable at `T + D` and is
  stamped `k = T + D - M + A`. Its grid's beats with `k + H <= t < k + every + H`
  that come at least half a period after the last committed beat are committed,
  known at `k`. `H` is the commit horizon (1 period plus 0.35 s) at the grid's
  local period, `every` the entries' cadence.
- **Grids:** the entry's offline decode (`offline`), the causal tracker's
  believed beats (`online`), or those shifted onto the offline decode's phase by
  the stream's phase check (`phased`).
- **Fallback:** where the grid has no beat (fewer than 2 beats, a gap of more
  than 1.5 periods, or nothing within a period of the band's end), the band's
  beats there are Direct listening's committed beats, stamped `M - A` before the
  position that committed them (a forecast needs no decode, so not `D`). Past the
  last entry, Direct's beats continue, so the stream never stalls.

Python's hindsight entries carry no decode delay; `D` models it. Accuracy is the
stream's own (`stream.accuracy`, beats at t >= 4 s within 45 ms of the
reference), with recall (the share of reference beats with a committed beat
within 45 ms) beside it, since a grid at half tempo can be precise and miss half
the beats.
"""

from __future__ import annotations

import dataclasses
import glob
import itertools
import json
import math
from pathlib import Path
from typing import Any

import numpy as np

from . import stream as st

#: The hindsight decode's delay and the capture -> arrival target, from S0 (the design's Targets).
D_S = 0.67
A_S = 0.048
MS = (1.5, 2.0, 2.5, 3.0, 4.0, 5.0)
GRIDS = ("online", "offline", "phased")
AFTER_S = 4.0
WITHIN_S = 0.045


def local_period(beats: list[float]) -> float | None:
    """The median spacing of a grid's beats, or None with fewer than 2."""
    return float(np.median(np.diff(beats))) if len(beats) >= 2 else None


def grid_beats(
    entries: list[dict[str, Any]], i: int, grid: str, p: st.Params
) -> list[float]:
    """Entry i's beats on the chosen grid."""
    e = entries[i]
    if grid == "offline":
        return sorted(e["offline"])
    online = sorted(e["online"])
    if grid == "online":
        return online
    per = local_period(online)
    if per is None:
        return online
    shift = st.phase_shift(entries, i, online[-1], per, p)
    return [b + shift for b in online]


def params_of(doc: dict[str, Any]) -> st.Params:
    """The stream's own commit horizon and phase check (the defaults where it has none)."""
    q = doc.get("params", {})
    p = st.Params()
    commit = q.get("commit", {})
    phase = q.get("phase", {})
    keys = {
        "commit_periods": commit.get("periods"),
        "commit_extra_s": commit.get("extraS"),
        "phase_settle_s": phase.get("settleS"),
        "phase_min_beats": phase.get("minBeats"),
        "phase_min_r": phase.get("minR"),
        "phase_min_shift": phase.get("minShift"),
        "phase_level_tol": phase.get("levelTol"),
        "phase_level_share": phase.get("levelShare"),
        "phase_persist": phase.get("persist"),
        "phase_agree": phase.get("agree"),
    }
    return dataclasses.replace(p, **{k: v for k, v in keys.items() if v is not None})


def uncovered(
    beats: list[float], per: float, lo: float, hi: float
) -> list[tuple[float, float]]:
    """The parts of the band [lo, hi) where a grid has no beat: all of it with fewer than 2
    beats, else every gap of more than 1.5 periods (the band's ends count as beats half a
    period outside it), less half a period at each grid beat (a beat there is the grid's)."""
    if len(beats) < 2:
        return [(lo, hi)]
    near = [b for b in beats if lo - 0.5 * per < b < hi + 0.5 * per]
    edges = [lo - 0.5 * per, *near, hi + 0.5 * per]
    return [
        (x + 0.5 * per, y - 0.5 * per)
        for x, y in itertools.pairwise(edges)
        if y - x > 1.5 * per + 1e-9
    ]


def commit_delayed(
    doc: dict[str, Any], m: float, grid: str, d: float = D_S, a: float = A_S
) -> list[dict[str, Any]]:
    """The committed beats of Delayed listening at delay m on a grid, each with its stamp,
    period and source (the grid, or `fallback` for Direct's forecasts)."""
    p = params_of(doc)
    entries = doc.get("hindsight", [])
    every = doc.get("params", {}).get("hindsight", {}).get("everyS", 1.0)
    direct = doc["beats"]
    out: list[dict[str, Any]] = []
    c = -math.inf

    def direct_period(at: float) -> float:
        known = [b["period"] for b in direct if b["knownAt"] <= at + 1e-9]
        return known[-1] if known else 0.5

    def forecast(b: dict[str, Any]) -> dict[str, Any]:
        # (A forecast needs no decode: stamped M - A before the position that committed it.)
        return {
            "t": b["t"],
            "knownAt": b["knownAt"] - (m - a),
            "period": b["period"],
            "source": "fallback",
        }

    def commit(candidates: list[dict[str, Any]]) -> None:
        nonlocal c
        for b in sorted(candidates, key=lambda b: b["t"]):
            if b["t"] >= c + 0.5 * b["period"] - 1e-9:
                out.append(b)
                c = b["t"]

    hi = -math.inf
    for i, e in enumerate(entries):
        k = e["knownAt"] + d - m + a
        beats = grid_beats(entries, i, grid, p)
        per = local_period(beats) or direct_period(e["knownAt"] + d)
        h = p.commit_periods * per + p.commit_extra_s
        lo, hi = k + h, k + every + h
        gaps = uncovered(beats, per, lo, hi)
        commit(
            [
                {"t": b, "knownAt": k, "period": per, "source": grid}
                for b in beats
                if lo <= b < hi
            ]
            + [
                forecast(b)
                for b in direct
                if lo <= b["t"] < hi and any(x <= b["t"] <= y for x, y in gaps)
            ]
        )
    commit([forecast(b) for b in direct if b["t"] >= hi])
    return out


def recall(
    beats: list[float], ref: list[float], after: float = AFTER_S
) -> float | None:
    """The share of reference beats at t >= after with a committed beat within 45 ms."""
    r = np.asarray([x for x in ref if x >= after])
    if len(r) == 0:
        return None
    if not beats:
        return 0.0
    b = np.sort(np.asarray(beats))
    i = np.searchsorted(b, r)
    left = b[np.clip(i - 1, 0, len(b) - 1)]
    right = b[np.clip(i, 0, len(b) - 1)]
    near = np.minimum(np.abs(r - left), np.abs(r - right))
    return round(float((near <= WITHIN_S).mean()), 4)


def score(beats: list[dict[str, Any]], ref: list[float]) -> dict[str, Any]:
    times = [b["t"] for b in beats]
    acc = st.accuracy(times, ref, AFTER_S)
    row: dict[str, Any] = {
        "within45": acc.get("within45"),
        "recall": recall(times, ref),
        "beats": acc["beats"],
    }
    if beats and "source" in beats[0]:
        row["fallback"] = round(
            sum(1 for b in beats if b["source"] == "fallback") / len(beats), 4
        )
    return row


def track_row(doc: dict[str, Any], ms: tuple[float, ...] = MS) -> dict[str, Any]:
    """Direct listening's score and Delayed listening's at every grid and M."""
    ref = doc["reference"]["beats"]
    return {
        "direct": score(doc["beats"], ref),
        "delayed": {
            g: {str(m): score(commit_delayed(doc, m, g), ref) for m in ms}
            for g in GRIDS
        },
    }


def _mean(xs: list[float | None]) -> float | None:
    v = [x for x in xs if x is not None]
    return round(float(np.mean(v)), 4) if v else None


def summarise(
    rows: dict[str, dict[str, Any]], ms: tuple[float, ...] = MS
) -> dict[str, Any]:
    """Mean per-track accuracy and recall, overall and per category, for Direct and each grid and M."""

    def means(ids: list[str]) -> dict[str, Any]:
        out: dict[str, Any] = {
            "tracks": len(ids),
            "direct": {
                f: _mean([rows[i]["direct"][f] for i in ids])
                for f in ("within45", "recall")
            },
        }
        for g in GRIDS:
            out[g] = {
                str(m): {
                    f: _mean([rows[i]["delayed"][g][str(m)][f] for i in ids])
                    for f in ("within45", "recall", "fallback")
                }
                for m in ms
            }
        return out

    def by(key: str) -> dict[str, Any]:
        groups = sorted({r[key] for r in rows.values()})
        return {g: means([i for i, r in rows.items() if r[key] == g]) for g in groups}

    # (By reference source too: offline and fitted references come from madmom's offline
    # decode, the offline grid's own model, so only exact and songmap ones are independent.)
    return {
        "aggregate": means(list(rows)),
        "categories": by("category"),
        "references": by("reference"),
    }


def propose(summary: dict[str, Any], ms: tuple[float, ...] = MS) -> dict[str, Any]:
    """E1's rules: at each grid, M is the smallest within 1 point of that grid's best aggregate;
    the grid is the best in aggregate at its M that is no worse than Direct on any category.
    Delayed listening pays off when that grid beats Direct in aggregate."""
    agg = summary["aggregate"]
    direct = agg["direct"]["within45"]
    options = []
    for g in GRIDS:
        acc = {m: agg[g][str(m)]["within45"] for m in ms}
        best = max(acc.values())
        m = min(x for x in ms if acc[x] >= best - 0.01 - 1e-9)
        worse = [
            c
            for c, s in summary["categories"].items()
            if s[g][str(m)]["within45"] < s["direct"]["within45"] - 1e-9
        ]
        options.append(
            {
                "grid": g,
                "m": m,
                "within45": acc[m],
                "recall": agg[g][str(m)]["recall"],
                "worseCategories": worse,
            }
        )
    eligible = [o for o in options if not o["worseCategories"]]
    choice = max(eligible, key=lambda o: o["within45"]) if eligible else None
    return {
        "direct": {"within45": direct, "recall": agg["direct"]["recall"]},
        "options": options,
        "choice": choice,
        "pays": choice is not None and choice["within45"] > direct,
    }


def categories(index: Path | None) -> dict[str, str]:
    if index is None or not index.exists():
        return {}
    return {t["id"]: t["group"] for t in json.loads(index.read_text())["tracks"]}


def run(
    paths: list[Path], index: Path | None, ms: tuple[float, ...] = MS
) -> dict[str, Any]:
    """Every stream with hindsight entries and a reference, scored, summarised and proposed."""
    cat = categories(index)
    rows: dict[str, dict[str, Any]] = {}
    skipped: list[str] = []
    for path in sorted(paths):
        doc = json.loads(path.read_text())
        tid = path.parent.name
        if not doc.get("hindsight") or not doc.get("reference", {}).get("beats"):
            skipped.append(tid)
            continue
        rows[tid] = {
            "category": cat.get(tid, "unlisted"),
            "reference": doc["reference"]["source"],
            **track_row(doc, ms),
        }
    summary = summarise(rows, ms)
    return {
        "d": D_S,
        "a": A_S,
        "ms": list(ms),
        "skipped": skipped,
        "tracks": rows,
        **summary,
        "proposal": propose(summary, ms),
    }


def default_streams() -> tuple[list[Path], Path | None]:
    """public/reactive/*/stream.json and its index.json, above the working directory."""
    for d in [Path.cwd(), *Path.cwd().parents]:
        root = d / "public" / "reactive"
        if (root / "index.json").exists():
            return [
                Path(p) for p in glob.glob(str(root / "*" / "stream.json"))
            ], root / "index.json"
    raise SystemExit(
        "cannot find public/reactive/index.json above the working directory"
    )


def _pct(x: float | None) -> str:
    return "—" if x is None else f"{100 * x:.1f}"


def markdown(result: dict[str, Any]) -> str:
    """The tables for the design's appendix: per category and grid, then per track at the proposal."""
    ms = result["ms"]
    lines = [
        "| Category | Tracks | Direct | "
        + " | ".join(f"{g} {m:g}" for g in GRIDS for m in ms)
        + " |",
        "|---|---|---|" + "---|" * (len(GRIDS) * len(ms)),
    ]
    groups = [("all", result["aggregate"]), *result["categories"].items()]
    for name, s in groups:
        cells = [_pct(s[g][str(m)]["within45"]) for g in GRIDS for m in ms]
        lines.append(
            f"| {name} | {s['tracks']} | {_pct(s['direct']['within45'])} | "
            + " | ".join(cells)
            + " |"
        )
    choice = result["proposal"]["choice"]
    if choice:
        g, m = choice["grid"], str(choice["m"])
        lines += [
            "",
            "| Reference | Tracks | Direct | "
            + " | ".join(f"{g} {x:g}" for x in ms)
            + " |",
            "|---|---|---|" + "---|" * len(ms),
        ]
        for name, s in result["references"].items():
            cells = [_pct(s[g][str(x)]["within45"]) for x in ms]
            lines.append(
                f"| {name} | {s['tracks']} | {_pct(s['direct']['within45'])} | "
                + " | ".join(cells)
                + " |"
            )
        lines += [
            "",
            f"| Track | Category | Reference | Direct within 45 ms | Direct recall | {g} {choice['m']:g} within 45 ms | recall | fallback |",
            "|---|---|---|---|---|---|---|---|",
        ]
        for tid, r in result["tracks"].items():
            dl = r["delayed"][g][m]
            lines.append(
                f"| {tid} | {r['category']} | {r['reference']} | {_pct(r['direct']['within45'])} | "
                f"{_pct(r['direct']['recall'])} | {_pct(dl['within45'])} | {_pct(dl['recall'])} | "
                f"{_pct(dl.get('fallback'))} |"
            )
    return "\n".join(lines) + "\n"
