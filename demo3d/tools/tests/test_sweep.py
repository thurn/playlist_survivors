"""The sweep orchestrator's lock, the report and the PIX summary (TD7)."""

from __future__ import annotations

import json
import subprocess
import sys
import time
from pathlib import Path

import pytest

from demo3d_tools import pix, report, sweep

windows = pytest.mark.skipif(sys.platform != "win32", reason="the machine lock is Windows-only")


@windows
def test_the_lock_is_exclusive_and_waits(tmp_path: Path) -> None:
    a = sweep.MachineLock(tmp_path / "bench.lock")
    b = sweep.MachineLock(tmp_path / "bench.lock")
    assert a.try_acquire("first")
    assert not b.try_acquire("second")
    assert "first" in b.holder()
    with pytest.raises(sweep.LockHeld):
        b.acquire("second", timeout=1.5)
    a.release()
    assert b.try_acquire("second")
    b.release()


@windows
def test_a_killed_holder_frees_the_lock_at_once(tmp_path: Path) -> None:
    path = tmp_path / "bench.lock"
    holder = subprocess.Popen([sys.executable, "-c", (
        "import sys, time; sys.path.insert(0, sys.argv[2]);"
        "from demo3d_tools import sweep; from pathlib import Path;"
        "l = sweep.MachineLock(Path(sys.argv[1])); assert l.try_acquire('child'); print('held', flush=True);"
        "time.sleep(60)"), str(path), str(Path(sweep.__file__).parents[1])], stdout=subprocess.PIPE, text=True)
    try:
        assert holder.stdout and holder.stdout.readline().strip() == "held"
        other = sweep.MachineLock(path)
        assert not other.try_acquire("parent")
        holder.kill()  # TerminateProcess, as taskkill /F
        holder.wait()
        started = time.monotonic()
        other.acquire("parent", timeout=5)
        assert time.monotonic() - started < 2
        other.release()
    finally:
        holder.kill()


def row(**kw) -> dict:
    base = {"sweep": "s1", "scene": "synthetic", "kind": "confirm", "attempt": 1, "n": 1000, "viewWidthM": 40,
            "load": "demo", "aa": "msaa4", "gpuP99Ms": 3.0, "gpuMaxMs": 3.5, "mainP99Ms": 1.0, "renderP99Ms": 0.5,
            "onScreenAlive": 1000, "opaqueDepth": 2.0, "transparentPerPixel": 1.0, "transparentBeatPeak": 1.1,
            "gpuTempC": 60, "gpuThrottle": "power_cap", "otherCpuPctMax": 4.0, "pass": True, "noisy": False,
            "noiseReasons": [], "jobWorkers": 6}
    base.update(kw)
    return base


def point(**kw) -> dict:
    base = {"sweep": "s1", "scene": "synthetic", "viewWidthM": 40, "load": "demo", "aa": "msaa4", "n": 2000,
            "limit": "gpu", "capacityBound": False, "belowRange": False, "ceiling": 2832, "capacity": 2832,
            "visibleGroundM2": 1180, "fitCandidate": 2000, "fitLimit": "gpu", "fitConfirmed": True, "seed": None,
            "probeRuns": 3, "fullRuns": 2, "hitRunCap": False, "noisyRuns": 0, "restsOnNoisy": False,
            "gpuP99Ms": 4.1, "mainP99Ms": 1.0, "renderP99Ms": 0.6, "computeEstimateMs": 12.3,
            "simTickP99Ms": None, "failN": 2100, "failWorst": "gpu"}
    base.update(kw)
    return base


def test_the_envelope_is_the_widest_view_that_fills_its_screen() -> None:
    pts = [point(viewWidthM=30, n=1593, visibleGroundM2=664, capacityBound=True),
           point(viewWidthM=40, n=2230, visibleGroundM2=1180),
           point(viewWidthM=55, n=1700, visibleGroundM2=2230)]
    assert report.envelope(pts) == (40, 2200)
    # Nothing fills: 30 m.
    pts = [point(viewWidthM=30, n=500, visibleGroundM2=664), point(viewWidthM=40, n=900, visibleGroundM2=1180)]
    assert report.envelope(pts) == (30, 500)


def test_the_report_counts_noisy_runs_and_flags_overdraw(tmp_path: Path) -> None:
    rows = [row(), row(attempt=2, noisy=True, noiseReasons=["other processes used 14.0% CPU over 5 s"]),
            row(n=2000, transparentPerPixel=6.0), row(**{"n": 2100, "pass": False})]
    (tmp_path / "rows.jsonl").write_text("\n".join(json.dumps(r) for r in rows) + "\n", encoding="utf-8")
    (tmp_path / "frontier.jsonl").write_text(json.dumps(point(restsOnNoisy=True, noisyRuns=1)) + "\n",
                                             encoding="utf-8")
    (tmp_path / "machine-s1.json").write_text(json.dumps({"gpu": "RTX", "memMHz": 2133}), encoding="utf-8")
    text = report.write(tmp_path).read_text(encoding="utf-8")
    assert "| 40 m | 2,000 | gpu | 4.10 ms |" in text
    assert "Frontier points resting on noisy runs: **1 of 1**." in text
    assert "Noisy runs: 1 of 4 (other processes' CPU: 1)." in text
    assert "exceed 5 or whose beat-frame peak exceeds 12: 1 of 4" in text
    assert "Job workers in every row: 6." in text


def test_pix_summary_keeps_top_level_passes_per_frame(tmp_path: Path) -> None:
    csv = tmp_path / "events.csv"
    csv.write_text("Global ID,Name,Event Depth,TOP to EOP Duration (ns)\n"
                   "1,Shadows,0,3000000\n2,DrawShadow,1,2900000\n3,Opaques,0,6000000\n"
                   "4,Shadows,0,3000000\n5,Opaques,0,6000000\n", encoding="utf-8")
    passes = pix.summarize(csv, frames=2)
    assert passes == [{"pass": "Opaques", "msPerFrame": 6.0}, {"pass": "Shadows", "msPerFrame": 3.0}]


def test_pix_command_lines_quote_the_player_arguments(tmp_path: Path) -> None:
    cmd = pix.capture_command(Path("D:/b/PlaylistSurvivors3D.exe"), Path("D:/s.json"), tmp_path,
                              "n=2000,viewWidthM=40,load=demo,aa=msaa4", 3)
    assert '--command-line="-force-d3d12 ' in cmd
    assert "--hold n:2000,viewWidthM:40,load:demo,aa:msaa4" in cmd
    assert cmd.count("=") == 1  # only the option's own: pixtool splits at the last "="
    assert cmd.endswith("programmatic-capture")
