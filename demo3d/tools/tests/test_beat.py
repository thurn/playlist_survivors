"""The on-the-beat audit's reading of a run: moments against their frames, coverage, settling and sounds."""

from __future__ import annotations

import math

from demo3d_tools import beat

FRAME = 1 / 60


def moment(kind: str, t: float, frame: int, detail: str = "") -> dict:
    song = frame * FRAME + 1e-6
    return {"frame": frame, "song": song, "prev": song - FRAME if frame else math.nan, "kind": kind, "t": t,
            "id": 0, "detail": detail}


def event(kind: str, t: float, frame_song: float, i0: int = 0, i1: int = 0, i2: int = 0, **d: float) -> dict:
    e = {"kind": kind, "t": t, "i0": i0, "i1": i1, "i2": i2, "frame_song": frame_song}
    for k in ("d0", "d1", "d2", "d3", "d4", "d5"):
        e[k] = d.get(k, 0.0)
    return e


def test_a_moment_is_on_the_first_frame_at_or_after_its_time():
    assert beat.verdict(moment("Death", 10 * FRAME, 10)) == "on", "on a frame's own time"
    assert beat.verdict(moment("Death", 10.5 * FRAME, 11)) == "on"
    assert beat.verdict(moment("Death", 10.5 * FRAME, 12)) == "late"
    assert beat.verdict(moment("Death", 10 * FRAME, 11)) == "late", "the frame before had reached it"
    assert beat.verdict(moment("Death", 11.5 * FRAME, 11)) == "early"


def test_the_audit_counts_each_kind_and_fails_on_a_late_one():
    frames = [n * FRAME + 1e-6 for n in range(120)]
    events = [event("Death", 30.5 * FRAME, 25 * FRAME, i0=3), event("Death", 40.5 * FRAME, 30 * FRAME, i0=4),
              event("Death", 50 * FRAME, 40 * FRAME, i0=-1),  # the hero's fall: no shatter
              event("Attack", 20 * FRAME, 15 * FRAME, i0=-1, i2=0, d1=31 * FRAME),  # the sword's impact
              event("EnemyStep", 25 * FRAME, 20 * FRAME, d5=40.2 * FRAME)]
    moments = [moment("Death", 30.5 * FRAME, 31), moment("Death", 40.5 * FRAME, 41), moment("Blade", 31 * FRAME, 31),
               moment("Effect", 31 * FRAME, 31, "slash"), moment("Footfall", 40.2 * FRAME, 41)]
    s = beat.audit_moments(moments, events, frames)
    assert s["ok"], s
    assert s["kinds"]["Death"] == {"checked": 2, "on": 2, "late": 0, "early": 0, "events": 2}
    assert s["kinds"]["Blade"]["events"] == 1
    assert s["kinds"]["Effect"]["by_effect"] == {"slash": 1}
    assert s["settled_ahead"]["EnemyStep"]["past_their_frame"] == 0

    moments[1] = moment("Death", 40.5 * FRAME, 42)
    s = beat.audit_moments(moments, events, frames)
    assert not s["ok"] and s["off_frame"] == 1 and s["kinds"]["Death"]["late"] == 1


def test_the_audit_fails_on_a_moment_the_sim_made_that_nothing_showed():
    frames = [n * FRAME + 1e-6 for n in range(120)]
    events = [event("Death", 30.5 * FRAME, 25 * FRAME, i0=3), event("Death", 40.5 * FRAME, 30 * FRAME, i0=4),
              event("EnemyStep", 25 * FRAME, 20 * FRAME, d5=40.2 * FRAME),
              event("EnemyStep", 25 * FRAME, 20 * FRAME, d5=40.2 * FRAME)]
    shown = [moment("Death", 30.5 * FRAME, 31), moment("Death", 40.5 * FRAME, 41), moment("Footfall", 40.2 * FRAME, 41)]
    assert beat.audit_moments(shown, events, frames)["ok"], "a sample of the footfalls is enough"
    s = beat.audit_moments(shown[1:], events, frames)
    assert not s["ok"] and s["off_frame"] == 0 and s["unshown"] == {"Death": "1 of 2"}
    s = beat.audit_moments(shown[:2], events, frames)
    assert not s["ok"] and s["unshown"] == {"Footfall": "0 of 2"}


def test_an_event_settling_after_its_moment_passed_is_counted():
    frames = [n * FRAME + 1e-6 for n in range(60)]
    # A held stride drawn from frame 10's time, settling in frame 12: frame 11 had passed its start.
    s = beat.settle([event("HeroStep", 12 * FRAME, 12 * FRAME + 1e-6, d4=10 * FRAME)], frames)
    assert s["HeroStep"]["past_their_frame"] == 1
    s = beat.settle([event("HeroStep", 12 * FRAME, 12 * FRAME + 1e-6, d4=11.5 * FRAME)], frames)
    assert s["HeroStep"]["past_their_frame"] == 0


def test_sounds_are_matched_to_their_events_and_their_anchor():
    events = [event("Attack", 9.9, 9.8, i0=-1, d1=10.0), event("Hit", 10.0, 9.85), event("Death", 10.25, 10.1)]
    report = {
        "outputRate": 48000, "anchorDsp": 100.0, "anchorSong": 5.0,
        "clickKind": ["swing", "hit", "death", "shatter-body", "shatter-tinkle", "hurt"],
        "clickSong": [10.0, 10.0, 10.25, 10.25, 10.6, 10.125],
        "clickDsp": [105.0, 105.0, 105.25, 105.25, 105.6, math.nan],
    }
    log = "lead (ms) median 90.3, p5..p95 90.3..90.4, min..max 89.9..91.4 (output latency 38.3 + margin 52.0)"
    s = beat.audit_sounds(report, events, log)
    assert s["ok"], s
    assert (s["on_event"], s["snapped"], s["dropped"], s["off_event"]) == (4, 2, 1, 0)

    report["clickSong"][0] = 10.01  # a swing off its impact
    report["clickDsp"][2] = 105.26  # a death scheduled off its song time's anchor
    s = beat.audit_sounds(report, events, log)
    assert not s["ok"] and s["off_event"] == 1 and s["off_anchor"] == 2
