import argparse
from pathlib import Path

from demo3d_tools import timing

HEADER = ("frame,stepped,real_s,interval_ms,cpu_ms,loop_ms,step_ms,sim_ms,render_ms,gpu_ms,present_wait_ms,frame_song,lead_ms,"
          "phase,frozen,alive,hero_x,hero_y," + ",".join(timing.COUNTS))


def row(frame: int, real: float, interval: float, song: float | None, *, cpu: float = 5, step: float = 2, alive: int = 10,
        kills: int = 0, late: int = 0, dropped: int = 0, gc: int = 0) -> str:
    counts = {k: 0 for k in timing.COUNTS} | {"kills": kills, "late": late, "dropped": dropped, "gc": gc}
    stepped = song is not None
    s = "nan" if song is None else f"{song:.4f}"
    return (f"{frame},{int(stepped)},{real:.4f},{interval:.3f},{cpu:.3f},1.000,{step if stepped else 0:.3f},0.500,3.000,-1.000,"
            f"0.000,{s},{'nan' if song is None else '73.50'},{'playing' if stepped else 'waiting'},0,{alive if stepped else 0},"
            f"{'nan' if song is None else '1.000'},{'nan' if song is None else '2.000'},"
            + ",".join(str(counts[k]) for k in timing.COUNTS))


def session(dir: Path) -> Path:
    """Two frames before the music's anchor, then a second of 60 fps song with one 50 ms frame at song 0.5 s."""
    lines = [HEADER, row(1, 0.0, 16.7, None), row(2, 0.0167, 16.7, None)]
    real = 0.0334
    for i in range(60):
        song = i / 60
        lines.append(row(3 + i, real, 16.7 if i != 31 else 50.0, song, step=12 if i == 30 else 2, cpu=40 if i == 31 else 5,
                         kills=1 if i % 20 == 0 else 0, dropped=1 if i == 45 else 0))
        real += 0.0167 if i != 31 else 0.05
    lines.append(row(63, real, 16.7, 1.0, kills=2, late=1, gc=1))
    dir.mkdir(parents=True)
    (dir / timing.FILE).write_text("\n".join(lines) + "\n")
    return dir


def test_a_hitch_is_shown_at_the_frame_that_took_it_with_the_counts_and_a_table_per_second(tmp_path: Path) -> None:
    lines = timing.summarise(timing.read(session(tmp_path / "20261031-120000") / timing.FILE))
    text = "\n".join(lines)
    assert "63 frames" in lines[0] and "song 0.0 to 1.0 s" in lines[0]
    assert "over 33.3 ms 1" in text
    assert "5 kills" in text and "1 late" in text and "1 dropped" in text and "1 collections" in text
    hitch = lines[lines.index(next(line for line in lines if line.startswith("hitches"))) + 2]
    # The 50 ms interval arrives on song 31/60's row: the frame before it, song 0.5 s, took it, with its 12 ms step.
    assert hitch.split()[:2] == ["0.500", "50.0"]
    assert hitch.split()[2] == "40.0" and hitch.split()[4] == "12.0"
    # Second 0's 60 frames (the last row has no frame after it to time it), its worst interval and kills.
    table = lines[next(i for i, line in enumerate(lines) if line.startswith("per second")) + 1:]
    assert [line.split()[:3] for line in table] == [["0", "60", "50.0"]]
    assert table[0].split()[6] == "3"


def test_a_span_of_song_and_no_table_narrow_the_summary(tmp_path: Path) -> None:
    rows = timing.read(session(tmp_path / "s") / timing.FILE)
    text = "\n".join(timing.summarise(rows, start=0.6, end=1.0, table=False))
    assert "over 33.3 ms 0" in text and "hitches" not in text and "per second" not in text
    assert timing.summarise(rows, start=5, end=6) == ["no frames in that span"]


def test_log_takes_the_newest_session_with_a_timing_log(tmp_path: Path, monkeypatch, capsys) -> None:
    session(tmp_path / "20261031-120000")
    (tmp_path / "20261031-130000").mkdir()  # newer, but recorded without one
    monkeypatch.setattr(timing, "sessions_root", lambda: tmp_path)
    assert timing.cmd_log(argparse.Namespace(session=None, start=None, end=None, hitch=timing.HITCH_MS, no_table=True)) == 0
    assert "20261031-120000" in capsys.readouterr().out
    assert timing.cmd_log(argparse.Namespace(session=str(tmp_path / "20261031-130000"), start=None, end=None,
                                             hitch=timing.HITCH_MS, no_table=True)) == 1
