"""The Mac's differences from Cygnus in setup: its ffmpeg and its filesystem."""

from __future__ import annotations

import subprocess
from pathlib import Path

import pytest

from demo3d_tools import clone, setup

ENCODERS = """Encoders:
 V..... = Video
 ------
 A....D libopus              libopus Opus (codec opus)
 A..X.D vorbis               Vorbis
"""


def fake_run(stdout: str):
    def run(cmd, **kwargs):
        return subprocess.CompletedProcess(cmd, 0, stdout=stdout, stderr="")
    return run


def test_homebrews_ffmpeg_has_no_libvorbis(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(subprocess, "run", fake_run(ENCODERS))
    assert not setup.ffmpeg_has_libvorbis()  # the experimental `vorbis` encoder doesn't count
    with_lib = ENCODERS + " A....D libvorbis            libvorbis (codec vorbis)\n"
    monkeypatch.setattr(subprocess, "run", fake_run(with_lib))
    assert setup.ffmpeg_has_libvorbis()


def test_the_song_falls_back_to_wav(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    calls: list[list[str]] = []
    monkeypatch.setattr(setup, "ffmpeg_has_libvorbis", lambda: False)
    monkeypatch.setattr(subprocess, "run", lambda cmd, **kw: calls.append(cmd))
    wt = type("Wt", (), {"root": tmp_path})()
    setup._song_build(wt, {"song": "song.mp3"}, tmp_path / "out")
    assert calls[0][-1].endswith("action.wav")
    assert "pcm_s16le" in calls[0]


MOUNT = """/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)
/dev/disk3s5 on /System/Volumes/Data (apfs, local, journaled, nobrowse)
/dev/disk5s1 on /Volumes/Stick (msdos, local, nodev, nosuid, noowners)
"""


def test_mac_volume_is_the_longest_mount_point(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    monkeypatch.setattr(clone.sys, "platform", "darwin")
    monkeypatch.setattr(subprocess, "run", fake_run(MOUNT))
    monkeypatch.setattr(Path, "resolve", lambda self: self)
    monkeypatch.setattr(Path, "exists", lambda self: True)
    assert clone.volume_fs(Path("/Volumes/Stick/cache")) == "msdos"
    assert clone.volume_fs(Path("/Volumes/Sticky")) == "apfs"  # not under /Volumes/Stick
    assert clone.volume_fs(Path("/System/Volumes/Data/Users/x")) == "apfs"
