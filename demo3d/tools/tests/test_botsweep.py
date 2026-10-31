"""`game sweep`'s playlist timeline and verdicts (botsweep.py)."""

from __future__ import annotations

from pathlib import Path

from demo3d_tools import botsweep


def test_the_playlist_keeps_native_gaps_and_orders(tmp_path: Path) -> None:
    text = "#EXTM3U\n# a comment\na.ogg\nb.ogg\n#PS-GAP:8\nc.ogg\nd.ogg\ne.ogg\nf.ogg\n"
    tracks = botsweep.parse_playlist(text, tmp_path)
    assert [t.path.name for t in tracks] == ["a.ogg", "b.ogg", "c.ogg", "d.ogg", "e.ogg", "f.ogg"]
    assert [t.gap for t in tracks] == [0, 2, 8, 2, 2, 2]
    # Native's orders move the tracks and leave the silences where they were.
    reverse = botsweep.ordered(tracks, "reverse")
    assert [t.path.name for t in reverse] == ["f.ogg", "e.ogg", "d.ogg", "c.ogg", "b.ogg", "a.ogg"]
    assert [t.gap for t in reverse] == [0, 2, 8, 2, 2, 2]
    rotate = botsweep.ordered(tracks, "rotate")
    assert [t.path.name for t in rotate] == ["c.ogg", "d.ogg", "e.ogg", "f.ogg", "a.ogg", "b.ogg"]


def report(**fields: object) -> dict:
    base = {"victory": True, "fallen": False, "runClock": 1800.0, "lowestHealthShare": 0.6}
    return {**base, **fields}


def test_the_verdicts_are_the_doc_s_gates() -> None:
    positioning = botsweep.Case("cached", "original", "positioning")
    assert botsweep.verdict(positioning, report())
    assert not botsweep.verdict(positioning, report(lowestHealthShare=0.25))
    assert not botsweep.verdict(positioning, report(victory=False, runClock=1700.0))
    assert not botsweep.verdict(positioning, report(fallen=True, victory=False))
    idle = botsweep.Case("cached", "original", "idle")
    assert botsweep.verdict(idle, report(fallen=True, victory=False, runClock=420.0))
    assert not botsweep.verdict(idle, report(fallen=True, victory=False, runClock=601.0))
    assert not botsweep.verdict(idle, report())
