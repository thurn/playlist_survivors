"""`demo3d trailer`'s player watchdog, on stand-in players that write a log like the real one."""

from __future__ import annotations

import sys
from pathlib import Path

from demo3d_tools import trailer


def player(tmp_path: Path, body: str) -> list[str]:
    script = tmp_path / "player.py"
    script.write_text("import sys, time\nlog = open(sys.argv[1], 'w', buffering=1)\n" + body)
    return [sys.executable, str(script), str(tmp_path / "player.log")]


def test_a_player_that_finishes_returns_its_exit_code_and_shows_progress(tmp_path: Path, capsys) -> None:
    cmd = player(tmp_path, "for n in (60, 120):\n    log.write(f'CaptureTool: {n} frames\\n'); time.sleep(1.2)\n"
                           "sys.exit(3)\n")
    assert trailer.run_player(cmd, tmp_path, tmp_path / "player.log", stall_s=5) == 3
    assert "120 frames (2.0 s of video)" in capsys.readouterr().out


def test_a_player_whose_log_stops_growing_is_stopped(tmp_path: Path, capsys) -> None:
    cmd = player(tmp_path, "log.write('DemoMain: loading\\n')\ntime.sleep(60)\n")
    assert trailer.run_player(cmd, tmp_path, tmp_path / "player.log", stall_s=2) == 124
    assert "it is stuck" in capsys.readouterr().err
