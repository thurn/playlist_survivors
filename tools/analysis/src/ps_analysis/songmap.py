"""Song map model: loading, saving and musical-position math.

Mirrors `src/core/songmap.ts`. All times are seconds from the start of the
cropped audio file. Beat indices are 0-based; bars and beats-within-bar are
1-based, as musicians count.
"""

from __future__ import annotations

import bisect
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any

SongMap = dict[str, Any]

FEELS = ("pulsed", "free")


def load(path: str | Path) -> SongMap:
    with open(path) as f:
        return json.load(f)


def _round(x: float, digits: int) -> float:
    return float(round(float(x), digits))


def normalize(m: SongMap) -> SongMap:
    """Round floats so the JSON stays small and diffs stay readable."""
    m["beats"] = [_round(b, 4) for b in m["beats"]]
    m["energy"] = [_round(e, 3) for e in m["energy"]]
    for a in m.get("accents", []):
        a["time"] = _round(a["time"], 4)
        a["strength"] = _round(a["strength"], 3)
    for sec in m.get("sections", []):
        for a in sec.get("anchors", []) or []:
            a["time"] = _round(a["time"], 4)
            a["strength"] = _round(a["strength"], 3)
    m["accents"] = sorted(m.get("accents", []), key=lambda a: a["time"])
    return m


def save(m: SongMap, path: str | Path) -> None:
    """Write a song map with one beat array per line block, compactly."""
    m = normalize(m)
    order = [
        "version",
        "song",
        "audio",
        "meter",
        "firstDownbeatIndex",
        "sections",
        "phrases",
        "tempoChanges",
        "accents",
        "beats",
        "energy",
    ]
    keys = [k for k in order if k in m] + [k for k in m if k not in order]
    lines = ["{"]
    for i, k in enumerate(keys):
        v = m[k]
        if k in ("beats", "energy"):
            chunks = [v[j : j + 16] for j in range(0, len(v), 16)]
            body = ",\n    ".join(", ".join(json.dumps(x) for x in c) for c in chunks)
            text = "[\n    " + body + "\n  ]" if v else "[]"
        elif k in ("sections", "accents", "phrases", "meter", "tempoChanges"):
            items = [json.dumps(x, separators=(", ", ": ")) for x in v]
            text = "[\n    " + ",\n    ".join(items) + "\n  ]" if items else "[]"
        else:
            text = json.dumps(v, indent=2).replace("\n", "\n  ")
        comma = "," if i < len(keys) - 1 else ""
        lines.append(f"  {json.dumps(k)}: {text}{comma}")
    lines.append("}")
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w") as f:
        f.write("\n".join(lines) + "\n")


@dataclass
class SongIndex:
    """Bar structure derived from a song map (same rules as `indexSong`)."""

    map: SongMap
    #: bar_start[bar] = beat index of beat 1 of that bar; index 0 unused.
    bar_start: list[int]
    beats_per_bar: list[int]
    bar_count: int

    def bar_end_beat(self, bar: int) -> int:
        """Exclusive end beat index of a bar (may exceed the beat list)."""
        return self.bar_start[bar] + self.beats_per_bar[bar]

    def last_bar_complete(self) -> bool:
        return self.bar_end_beat(self.bar_count) <= len(self.map["beats"])

    def beat_range(self, first_bar: int, last_bar: int) -> tuple[int, int]:
        """Beat index range [start, end) covered by bars first..last (inclusive)."""
        start = self.bar_start[first_bar]
        end = min(self.bar_end_beat(last_bar), len(self.map["beats"]))
        return start, end


def meter_at(meter: list[dict[str, int]], bar: int) -> int:
    ms = sorted(meter, key=lambda r: r["fromBar"])
    bpb = ms[0]["beatsPerBar"]
    for r in ms:
        if r["fromBar"] <= bar:
            bpb = r["beatsPerBar"]
    return bpb


def index_song(m: SongMap) -> SongIndex:
    bar_start = [-1]
    bpb_list = [-1]
    beat = m["firstDownbeatIndex"]
    bar = 1
    n = len(m["beats"])
    while beat < n:
        bpb = meter_at(m["meter"], bar)
        bar_start.append(beat)
        bpb_list.append(bpb)
        beat += bpb
        bar += 1
    return SongIndex(m, bar_start, bpb_list, bar - 1)


def beat_index_at(beats: list[float], s: float) -> float:
    """Fractional 0-based beat index at time s (piecewise-linear, extrapolated)."""
    n = len(beats)
    if s <= beats[0]:
        return (s - beats[0]) / (beats[1] - beats[0])
    if s >= beats[-1]:
        return n - 1 + (s - beats[-1]) / (beats[-1] - beats[-2])
    lo = bisect.bisect_right(beats, s) - 1
    return lo + (s - beats[lo]) / (beats[lo + 1] - beats[lo])


def time_of_beat_index(beats: list[float], i: float) -> float:
    n = len(beats)
    if i <= 0:
        return beats[0] + i * (beats[1] - beats[0])
    if i >= n - 1:
        return beats[-1] + (i - (n - 1)) * (beats[-1] - beats[-2])
    k = math.floor(i)
    return beats[k] + (i - k) * (beats[k + 1] - beats[k])


def beat_index_of(idx: SongIndex, bar: int, beat: float = 1) -> float:
    if bar < 1:
        return (
            idx.map["firstDownbeatIndex"]
            + (bar - 1) * idx.beats_per_bar[1]
            + (beat - 1)
        )
    if bar > idx.bar_count:
        last = idx.bar_count
        return idx.bar_start[last] + (bar - last) * idx.beats_per_bar[last] + (beat - 1)
    return idx.bar_start[bar] + (beat - 1)


def time_of(idx: SongIndex, bar: int, beat: float = 1) -> float:
    return time_of_beat_index(idx.map["beats"], beat_index_of(idx, bar, beat))


def bar_of_beat_index(idx: SongIndex, i: float) -> int:
    """1-based bar containing beat index i (floored); 0 for pickup beats."""
    k = math.floor(i + 1e-9)
    if idx.bar_count == 0 or k < idx.bar_start[1]:
        return 0
    return bisect.bisect_right(idx.bar_start, k, 1, idx.bar_count + 1) - 1


def position_of_beat(idx: SongIndex, i: int) -> tuple[int, int]:
    """(bar, beat-in-bar) of an integer beat index; bar 0 for pickups."""
    bar = bar_of_beat_index(idx, i)
    if bar == 0:
        return 0, i - idx.map["firstDownbeatIndex"] + idx.beats_per_bar[1] + 1
    return bar, i - idx.bar_start[bar] + 1


def section_times(
    idx: SongIndex, sec: dict[str, Any], duration: float | None = None
) -> tuple[float, float]:
    """Start of a section's first bar and end of its last bar, in seconds.

    The end of the last bar of the song is clamped to `duration` if given.
    """
    a, b = sec["bars"]
    start = time_of(idx, a, 1)
    end = time_of(idx, b + 1, 1)
    if duration is not None and b >= idx.bar_count:
        end = min(end, duration)
    return start, end


def section_at_time(idx: SongIndex, s: float) -> dict[str, Any] | None:
    bar = bar_of_beat_index(idx, beat_index_at(idx.map["beats"], s))
    secs = idx.map["sections"]
    if not secs:
        return None
    if bar < secs[0]["bars"][0]:
        return secs[0]
    for sec in secs:
        if sec["bars"][0] <= bar <= sec["bars"][1]:
            return sec
    return secs[-1]


def section_by_label(m: SongMap, label: str) -> dict[str, Any] | None:
    for sec in m["sections"]:
        if sec["label"] == label:
            return sec
    return None


def describe_beat(idx: SongIndex, i: int) -> str:
    """Human-readable description of a beat for error messages."""
    beats = idx.map["beats"]
    bar, bb = position_of_beat(idx, i)
    t = beats[i] if 0 <= i < len(beats) else float("nan")
    return f"beat {i} (bar {bar} beat {bb}, {t:.3f} s)"
