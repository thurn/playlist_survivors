"""`game beat NAME --file F`: the on-the-beat audit (docs/design/unity-game.md, Time and Sync).

The game's promise is that every moving thing lands on the music: each key moment shows on the
first frame at or after its song time. The audit runs the game's player windowless at fixed time
(batch mode, `-force-metal`, so the crowd and the effects run as in a still) with `--moments-out`,
where each presenter marks a moment on the frame it first shows it (game/Assets/Game/Client/Loop/
Moments.cs), and `--events-out`, every sim event as it settled. Then, from those files alone:

- **on its frame**: every moment's frame is the first at or after its time, `prev < t <= song`
  (frames are exactly 1/60 s apart at fixed time): the hero's hops setting off and landing, the
  blade's flare on each impact, the monsters' footfalls, deaths seeding their shatter, structure
  breaks and regrowths, every particle effect (the sword's slashes, death bursts, hit flashes, the
  weapons' casts and hits), the beat gem's ring landing and the cards' flips. Late is a frame after
  that, early a frame before its time.
- **coverage**: how many of the sim's events of each kind in the run's frames were shown (the
  footfalls are a sample: only monsters on a slot are posed).
- **settled ahead**: the margin by which each event settled before its moment, against the frame
  that dispatched it; a moment that settled after its frame had passed can only show late.

`--sounds` audits the sounds instead, on a real-time headless run (the only kind that schedules
them): the player's `--sound-out` with the sound probe, as `game spike --sounds` runs it, and
`--events-out`. Every scheduled sound's song time must be its event's (a swing's impact, a hit's
or a death's time, a cast's, a chest's tick), apart from those snapped to the grid by design (the
hurt and the gem chimes on the sixteenth, a shatter's tinkle as its shards land); its DSP time must
be its song time's on the music's anchor; the mix measurement (spike.py) finds each probe click on
the music; and the lead (the run's summary) holds at the output latency plus the margin. Dropped
sounds are counted, not failed: which sounds drop at the peak is ps-msz.8.6's.

The numbers go to game/evidence/beat/NAME.json (`--evidence DIR` for another folder,
`--no-evidence` for slow CI); the player's files stay in the gitignored captures/game/beat/NAME/.
It exits 1 when a moment is off its frame, a sound is off its event or its anchor, a probe click is
off the music, the lead strays, or the player fails or throws.
"""

from __future__ import annotations

import argparse
import bisect
import csv
import json
import math
import re
import shlex
import shutil
import subprocess
import sys
import time
from collections import Counter, defaultdict
from pathlib import Path

from . import paths, setup

APP_EXE = "PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
EPS = 1e-9
# A sound's song time against its event's: both are the same double, printed round-trip (C#'s R) and
# by Unity's JsonUtility.
SOUND_EPS = 1e-6
# The lead's p5 and p95 against the output latency plus the margin (ms): the slice measured 0.0.
LEAD_TOLERANCE_MS = 2.0

# The moments, and the sim events each one shows (for coverage).
MOMENTS = ["HeroPress", "HeroStride", "HeroLand", "Blade", "Footfall", "Death", "Break", "Regrow",
           "Effect", "GemRing", "CardFlip"]

# The sounds snapped to the grid by design (unity-game.md, Sound Timing), not at an event's time.
SNAPPED = {"hurt", "chime", "shatter-tinkle"}
# Each sound's event: (event kind, the field holding its time).
SOUND_EVENTS = {
    "swing": [("Attack", "d1")], "swing-heavy": [("Attack", "d1")], "slam": [("Attack", "d1")],
    "garlic-pulse": [("Attack", "d1")], "fire-trail-pulse": [("Attack", "d1")], "wand-fire": [("Attack", "t")],
    "hit": [("Hit", "t")], "hit-heavy": [("Hit", "t")], "garlic-hit": [("Hit", "t")],
    "fire-trail-hit": [("Hit", "t")], "wand-hit": [("Hit", "t")], "bible-hit": [("Hit", "t")],
    "lightning-hit": [("Hit", "t")],
    "death": [("Death", "t")],
    "shatter-body": [("Death", "t"), ("StructureBreak", "t"), ("Regrowth", "d0")],
    "shatter-glass": [("Death", "t"), ("StructureBreak", "t"), ("Regrowth", "d0")],
    "bible-fire": [("Orbit", "d0")],
    "lightning-strike": [("Bolt", "t")], "lightning-jump": [("Bolt", "t")],
    "chest-open": [("Chest", "t")], "chest-tick": [("Chest", "t")], "chest-land": [("Chest", "t")],
    "evolve": [("Chest", "t"), ("Card", "t"), ("Projectile", "t"), ("Attack", "t")],
}


def add_parser(sub: argparse._SubParsersAction) -> None:
    bp = sub.add_parser("beat", help="the on-the-beat audit: every moment on its frame, every sound on its event")
    bp.add_argument("name", help="captures/game/beat/NAME and game/evidence/beat/NAME.json")
    bp.add_argument("--file", required=True, help="the audio file or playlist (paths from the repository root)")
    bp.add_argument("--seconds", type=float, default=40, help="song seconds to run")
    bp.add_argument("--player-args", default="", help="extra player flags, e.g. '--start-minute 25 --debug finale@30'")
    bp.add_argument("--sounds", action="store_true", help="audit the sounds on a real-time headless run instead")
    bp.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    bp.add_argument("--build", action="store_true", help="build the player first (a no-op if it is current)")
    bp.add_argument("--analyse-only", action="store_true", help="re-read the kept run")
    bp.add_argument("--no-evidence", action="store_true", help="print the numbers only (slow CI)")
    bp.add_argument("--evidence", default="beat", help="the evidence folder under game/evidence (default beat)")
    bp.set_defaults(func=cmd_beat)


def cmd_beat(a: argparse.Namespace) -> int:
    if sys.platform != "darwin":
        print("beat: the game's player runs on the Mac", file=sys.stderr)
        return 2
    wt = paths.worktree(name="game")
    captures = wt.root / "captures" / "game" / "beat" / a.name
    source = (wt.root / a.file).resolve()
    if not a.analyse_only:
        code = _run(wt, a, captures, source)
        if code != 0:
            return code
    events = read_events(captures / "events.csv")
    frames = read_frames(captures / "loop.csv")
    if a.sounds:
        summary = audit_sounds(json.loads((captures / "spike.json").read_text()), events,
                               (captures / "player.log").read_text(errors="replace"))
        from . import spike

        mix = spike.measure(json.loads((captures / "spike.json").read_text()), spike.read_wav(captures / "mix.wav"),
                            spike.decode(source, json.loads((captures / "spike.json").read_text())["outputRate"]))
        summary["mix"] = {k: mix[k] for k in ("ok", "clicks_scheduled", "clicks_apart", "clicks_found",
                                              "clicks_dropped", "music_to_click_ms", "drift_ms_per_min",
                                              "frame_to_heard_ms", "output_latency_ms") if k in mix}
        summary["ok"] = summary["ok"] and bool(mix.get("ok"))
    else:
        summary = audit_moments(read_moments(captures / "moments.csv"), events, frames)
    summary.update({"file": a.file, "seconds": a.seconds, "player_args": a.player_args, "backend": a.backend,
                    "sounds": a.sounds})
    print(sound_table(summary) if a.sounds else moment_table(summary))
    if a.no_evidence:
        return 0 if summary["ok"] else 1
    evidence = wt.project / "evidence" / a.evidence / f"{a.name}.json"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(summary, indent=2) + "\n")
    print(f"beat: -> {evidence.relative_to(wt.root)}")
    return 0 if summary["ok"] else 1


def _run(wt: paths.Worktree, a: argparse.Namespace, captures: Path, source: Path) -> int:
    from .spike import exceptions

    exe = setup.player_dir(wt, "mac", a.backend) / APP_EXE
    if a.build or not exe.exists():
        from . import cli

        code = cli.cmd_build(argparse.Namespace(
            project="game", platform="mac", backend=a.backend, out=None, development=False,
            scene=None, cold=False, force=False))
        if code != 0:
            return code
    shutil.rmtree(captures, ignore_errors=True)
    captures.mkdir(parents=True)
    log = captures / "player.log"
    outs = ["--events-out", str(captures / "events.csv"), "--loop-out", str(captures / "loop.csv")]
    if a.sounds:
        mode = ["--headless", "--sound-out", str(captures), "--sound-probe"]
        timeout = a.seconds + 300
    else:
        mode = ["-force-metal", "--fixed-time", "--moments-out", str(captures / "moments.csv")]
        timeout = 600 + a.seconds * 10
    cmd = [str(exe), "-batchmode", "-logFile", str(log), "--file", str(source), "--seconds", f"{a.seconds:g}",
           *mode, *outs, *shlex.split(a.player_args)]
    started = time.monotonic()
    code = subprocess.run(cmd, check=False, cwd=wt.root, timeout=timeout, stdout=subprocess.DEVNULL).returncode
    print(f"beat: the player exited {code} after {time.monotonic() - started:.1f} s (log {log})")
    thrown = exceptions(log)
    for line in thrown[:5]:
        print(f"beat: the player threw: {line}", file=sys.stderr)
    needed = ["events.csv", "loop.csv", "spike.json" if a.sounds else "moments.csv"]
    missing = [n for n in needed if not (captures / n).exists()]
    if code != 0 or thrown or missing:
        if missing:
            print(f"beat: the player left no {', '.join(missing)}", file=sys.stderr)
        return 1
    return 0


# ---------------------------------------------------------------------------------------------
# Reading the player's files.

def _float(v: str) -> float:
    return float(v) if v not in ("", "NaN") else math.nan


def read_events(path: Path) -> list[dict]:
    rows = []
    with path.open(newline="") as f:
        for r in csv.DictReader(f):
            e = {"kind": r["kind"], "t": float(r["t"]), "i0": int(r["i0"]), "i1": int(r["i1"]), "i2": int(r["i2"]),
                 "frame_song": _float(r["frame_song"])}
            for k in ("d0", "d1", "d2", "d3", "d4", "d5"):
                e[k] = _float(r[k])
            rows.append(e)
    return rows


def read_moments(path: Path) -> list[dict]:
    with path.open(newline="") as f:
        return [{"frame": int(r["frame"]), "song": float(r["song"]), "prev": _float(r["prev"]), "kind": r["kind"],
                 "t": float(r["t"]), "id": int(r["id"]), "detail": r["detail"]} for r in csv.DictReader(f)]


def read_frames(path: Path) -> list[float]:
    with path.open(newline="") as f:
        return [float(r["frame_song"]) for r in csv.DictReader(f)]


# ---------------------------------------------------------------------------------------------
# The moments.

def verdict(m: dict) -> str:
    """A moment's frame against its time: on (the first frame at or after it), late or early."""
    if m["song"] < m["t"] - EPS:
        return "early"
    if not math.isnan(m["prev"]) and m["prev"] >= m["t"] - EPS:
        return "late"
    return "on"


def _sword(e: dict) -> bool:
    return e["kind"] == "Attack" and e["i0"] < 0 and (e["i2"] >> 1) & 7 == 0


def moment_of(e: dict) -> float:
    """When the key moment an event brings shows: an attack's impact, a step's footplant, a hero step's
    start (drawn from the press heard), a regrowth's or an orbit's start; else its time."""
    k = e["kind"]
    if k == "Attack":
        return e["d1"]
    if k == "EnemyStep":
        return e["d5"]
    if k == "HeroStep":
        return e["d4"]
    if k in ("Regrowth", "Orbit"):
        return e["d0"]
    return e["t"]


def expected(events: list[dict], lo: float, hi: float) -> dict[str, int]:
    """The sim's events each moment shows, with their moment in the frames' span (lo, hi]."""
    inside = lambda t: lo < t <= hi + EPS
    n: Counter = Counter()
    blades = set()
    for e in events:
        k = e["kind"]
        if k == "HeroStep" and not e["i1"] & 4:
            if inside(e["d4"]):
                n["HeroPress" if e["i1"] & 1 else "HeroStride"] += 1
            if inside(e["d5"]):
                n["HeroLand"] += 1  # an upper bound: a step cut short by the next never lands
        elif _sword(e) and inside(e["d1"]):
            blades.add(round(e["d1"], 9))
        elif k == "EnemyStep" and inside(e["d5"]):
            n["Footfall"] += 1
        elif k == "Death" and e["i0"] >= 0 and inside(e["t"]):
            n["Death"] += 1
        elif k == "StructureBreak" and inside(e["t"]):
            n["Break"] += 1
        elif k == "Regrowth" and inside(e["d0"]):
            n["Regrow"] += 1
        elif k == "Beat" and inside(e["t"]):
            n["GemRing"] += 1  # an upper bound: the ring lands only on a reliable beat, once the run shows
        elif k == "LevelUp" and inside(e["t"]):
            n["CardFlip"] += 3
    n["Blade"] = len(blades)
    return dict(n)


def audit_moments(moments: list[dict], events: list[dict], frames: list[float]) -> dict:
    by_kind: dict[str, list[dict]] = defaultdict(list)
    for m in moments:
        by_kind[m["kind"]].append(m)
    lo, hi = (frames[0], frames[-1]) if frames else (0.0, 0.0)
    want = expected(events, lo, hi)
    kinds = {}
    for kind in MOMENTS:
        ms = by_kind.get(kind, [])
        v = Counter(verdict(m) for m in ms)
        off = [m for m in ms if verdict(m) != "on"]
        entry = {"checked": len(ms), "on": v["on"], "late": v["late"], "early": v["early"]}
        if kind in want:
            entry["events"] = want[kind]
        if kind == "Effect":
            entry["by_effect"] = dict(sorted(Counter(m["detail"] for m in ms).items()))
            entry["off_by_effect"] = dict(sorted(Counter(f"{m['detail']} {verdict(m)}" for m in off).items()))
        if off:
            entry["worst"] = [{"t": round(m["t"], 6), "song": round(m["song"], 6), "frames_late":
                               round((m["song"] - m["t"]) * 60, 2), "id": m["id"], "detail": m["detail"]}
                              for m in sorted(off, key=lambda m: m["t"] - m["song"])[:5]]
        kinds[kind] = entry
    settled = settle(events, frames)
    checked = sum(k["checked"] for k in kinds.values())
    off = sum(k["late"] + k["early"] for k in kinds.values())
    shown = [k for k in MOMENTS if k != "HeroPress" and kinds[k]["checked"] == 0]
    return {
        "ok": off == 0 and checked > 0,
        "frames": len(frames),
        "span": [round(lo, 4), round(hi, 4)],
        "moments": checked,
        "off_frame": off,
        "kinds_never_shown": shown,
        "kinds": kinds,
        "settled_ahead": settled,
    }


def settle(events: list[dict], frames: list[float]) -> dict:
    """Per event kind, how far ahead of its moment it settled: the moment against the frameSong of the
    frame that dispatched it (ms), and how many settled after the frame before had passed their moment."""
    out = {}
    by: dict[str, list[tuple[float, float]]] = defaultdict(list)
    for e in events:
        if not math.isnan(e["frame_song"]):
            by[e["kind"]].append((moment_of(e), e["frame_song"]))
    for kind, pairs in sorted(by.items()):
        margins = sorted((m - f) * 1000 for m, f in pairs)
        late = 0
        for m, f in pairs:
            i = bisect.bisect_left(frames, f - EPS)
            prev = frames[i - 1] if i > 0 else -math.inf
            if m <= prev + EPS:
                late += 1
        out[kind] = {"n": len(pairs), "min_ms": round(margins[0], 2), "median_ms": round(margins[len(margins) // 2], 2),
                     "past_their_frame": late}
    return out


def moment_table(s: dict) -> str:
    rows = ["| moment | checked | on its frame | late | early | sim events |", "|---|---|---|---|---|---|"]
    for kind in MOMENTS:
        k = s["kinds"][kind]
        rows.append(f"| {kind} | {k['checked']} | {k['on']} | {k['late']} | {k['early']} | {k.get('events', '')} |")
    for kind in MOMENTS:
        for w in s["kinds"][kind].get("worst", []):
            rows.append(f"beat: {kind} off its frame: t {w['t']:.4f} shown at {w['song']:.4f} "
                        f"({w['frames_late']:+.2f} frames) {w['detail']}")
    eff = s["kinds"]["Effect"].get("off_by_effect")
    if eff:
        rows.append(f"beat: effects off their frame: {eff}")
    hero = [k for k in ("HeroStep", "Attack", "EnemyStep", "Death") if k in s["settled_ahead"]]
    rows.append("settled ahead (ms, min/median): " + ", ".join(
        f"{k} {s['settled_ahead'][k]['min_ms']:.1f}/{s['settled_ahead'][k]['median_ms']:.1f}" for k in hero))
    past = {k: v["past_their_frame"] for k, v in s["settled_ahead"].items() if v["past_their_frame"]}
    if past:
        rows.append(f"settled past their frame (moments the presenters can't show on time): {past}")
    rows.append(f"\n{s['frames']} frames, song {s['span'][0]:.3f} to {s['span'][1]:.3f} s; {s['moments']} moments, "
                f"{s['off_frame']} off their frame; never shown: {', '.join(s['kinds_never_shown']) or 'none'}; "
                f"{'ok' if s['ok'] else 'NOT OK'}")
    return "\n".join(rows)


# ---------------------------------------------------------------------------------------------
# The sounds.

LEAD = re.compile(r"lead \(ms\) median ([\d.]+), p5\.\.p95 ([\d.]+)\.\.([\d.]+), min\.\.max ([\d.]+)\.\.([\d.]+) "
                  r"\(output latency ([\d.]+) \+ margin ([\d.]+)\)")


def audit_sounds(report: dict, events: list[dict], log: str) -> dict:
    times: dict[tuple[str, str], list[float]] = defaultdict(list)
    for e in events:
        for field in ("t", "d0", "d1"):
            times[(e["kind"], field)].append(e[field])
    for v in times.values():
        v.sort()

    def near(values: list[float], t: float) -> bool:
        i = bisect.bisect_left(values, t - SOUND_EPS)
        return i < len(values) and values[i] <= t + SOUND_EPS

    kinds = report.get("clickKind") or []
    songs, dsps = report["clickSong"], report["clickDsp"]
    per: dict[str, Counter] = defaultdict(Counter)
    off_event, off_anchor = [], []
    rate = report["outputRate"]
    for kind, song, dsp in zip(kinds, songs, dsps):
        c = per[kind]
        c["scheduled" if not math.isnan(dsp) else "dropped"] += 1
        if kind in SNAPPED:
            c["snapped"] += 1
        elif any(near(times.get(src, []), song) for src in SOUND_EVENTS.get(kind, [])):
            c["on_event"] += 1
        else:
            c["off_event"] += 1
            off_event.append({"kind": kind, "song": song})
        if not math.isnan(dsp):
            want = report["anchorDsp"] + (song - report["anchorSong"])
            if abs(dsp - want) * rate > 1:
                off_anchor.append({"kind": kind, "song": song, "samples": round((dsp - want) * rate, 1)})
    lead = None
    found = LEAD.search(log)
    if found:
        med, p5, p95, lo, hi, latency, margin = (float(x) for x in found.groups())
        lead = {"median_ms": med, "p5_ms": p5, "p95_ms": p95, "min_ms": lo, "max_ms": hi,
                "output_latency_ms": latency, "margin_ms": margin,
                "ok": max(abs(p5 - latency - margin), abs(p95 - latency - margin)) <= LEAD_TOLERANCE_MS}
    total = Counter()
    for c in per.values():
        total.update(c)
    ok = (len(kinds) == len(songs) and len(songs) > 0 and not off_event and not off_anchor
          and lead is not None and lead["ok"])
    return {
        "ok": bool(ok),
        "count": len(songs),
        "scheduled": total["scheduled"],
        "dropped": total["dropped"],
        "on_event": total["on_event"],
        "snapped": total["snapped"],
        "off_event": total["off_event"],
        "off_anchor": len(off_anchor),
        "by_kind": {k: dict(v) for k, v in sorted(per.items())},
        "off_event_first": off_event[:10],
        "off_anchor_first": off_anchor[:10],
        "lead": lead,
    }


def sound_table(s: dict) -> str:
    rows = ["| sound | scheduled | dropped | on its event | snapped | off its event |", "|---|---|---|---|---|---|"]
    for kind, c in s["by_kind"].items():
        rows.append(f"| {kind} | {c.get('scheduled', 0)} | {c.get('dropped', 0)} | {c.get('on_event', 0)} | "
                    f"{c.get('snapped', 0)} | {c.get('off_event', 0)} |")
    for o in s["off_event_first"]:
        rows.append(f"beat: {o['kind']} at {o['song']:.6f} matches no event")
    for o in s["off_anchor_first"]:
        rows.append(f"beat: {o['kind']} at {o['song']:.6f} scheduled {o['samples']:+.1f} samples off its anchor")
    lead = s["lead"]
    lead_s = "no lead in the log" if lead is None else (
        f"lead median {lead['median_ms']:.1f} ms, p5..p95 {lead['p5_ms']:.1f}..{lead['p95_ms']:.1f} "
        f"(output latency {lead['output_latency_ms']:.1f} + margin {lead['margin_ms']:.1f})")
    mix = s.get("mix", {})
    m2c = mix.get("music_to_click_ms", {})
    mix_s = (f"; probe clicks on the music: {mix.get('clicks_found', 0)}/{mix.get('clicks_apart', 0)} apart found, "
             f"median {m2c.get('median', math.nan):+.3f} ms, p5..p95 {m2c.get('p5', math.nan):+.3f}.."
             f"{m2c.get('p95', math.nan):+.3f}") if mix else ""
    rows.append(f"\n{s['count']} sounds: {s['scheduled']} scheduled, {s['dropped']} dropped; {s['on_event']} on their "
                f"event, {s['snapped']} snapped by design, {s['off_event']} off; {s['off_anchor']} off their anchor; "
                f"{lead_s}{mix_s}; {'ok' if s['ok'] else 'NOT OK'}")
    return "\n".join(rows)
