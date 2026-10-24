"""The belief stream's accent forecasts (stage 5): repeating strong hits, forecast a cycle ahead.

When strong onsets have landed within 50 ms of the same position in a 2-, 4-
or 8-beat cycle of committed beats 3 cycles in a row, the next one is forecast
on the committed beat at that position. It is known at the later of that
beat's `knownAt` and the onsets that confirmed the pattern, and it is
forecast only if that leaves a swing's wind-up plus 0.35 s before the beat.
A forecast with no strong onset within 50 ms of it breaks the run, so the
pattern needs 3 more confirming cycles. `confidence` is the share of the last
4 cycles with a strong onset at the position.

Forecasts read only committed beats and onsets, each by its `knownAt`, so the
stream of the audio cut at X holds exactly the forecasts known by X
(`tests/test_stream.py` proves it).

See docs/design/reactive-audio.md, "Accents (stage 5)".
"""

from __future__ import annotations

from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from .stream import Params


def hit_times(
    beats: list[dict[str, Any]], onsets: list[dict[str, Any]], window_s: float
) -> list[float | None]:
    """For each committed beat, when a strong onset within the window of it is first known
    (no earlier than the beat itself), or None if none lands there."""
    strong = [o for o in onsets if o["strong"]]
    out: list[float | None] = []
    for b in beats:
        known = [
            o["knownAt"] for o in strong if abs(o["t"] - b["t"]) <= window_s + 1e-9
        ]
        out.append(max(min(known), b["knownAt"]) if known else None)
    return out


def forecasts(
    beats: list[dict[str, Any]], onsets: list[dict[str, Any]], p: Params
) -> list[dict[str, Any]]:
    """Accent forecasts, one per forecast beat in time order (the earliest known, then the most
    confident, then the shortest cycle, where several cycles forecast the same beat)."""
    hit = hit_times(beats, onsets, p.forecast_window_s)
    best: dict[int, dict[str, Any]] = {}
    for cycle in p.forecast_cycles:
        for i in range(len(beats) - cycle):
            run = [i - k * cycle for k in range(p.forecast_confirm)]
            if run[-1] < 0:
                continue
            times = [hit[k] for k in run]
            if any(t is None for t in times):
                continue
            j = i + cycle
            known = max(beats[j]["knownAt"], *(t for t in times if t is not None))
            if beats[j]["t"] - known < p.forecast_min_lead_s - 1e-9:
                continue
            recent = [i - k * cycle for k in range(p.forecast_history)]
            confidence = sum(1 for k in recent if k >= 0 and hit[k] is not None) / len(
                recent
            )
            entry = {
                "t": beats[j]["t"],
                "knownAt": known,
                "cycle": cycle,
                "confidence": confidence,
            }
            prev = best.get(j)
            rank = (known, -confidence, cycle)
            if prev is None or rank < (
                prev["knownAt"],
                -prev["confidence"],
                prev["cycle"],
            ):
                best[j] = entry
    return [best[j] for j in sorted(best)]
