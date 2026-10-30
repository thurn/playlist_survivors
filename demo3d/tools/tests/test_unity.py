"""Cold Unity runs end when Unity exits, whatever its package manager server does."""

from __future__ import annotations

import sys
import time
from pathlib import Path

import pytest

from demo3d_tools import paths, unity

# Stands in for Unity: starts a UnityPackageManager server in its own session that keeps the
# inherited stdout open and names its Unity as `-s <pid>`, as the real one does, then exits.
FAKE_UNITY = """\
import os, subprocess, sys
upm = os.path.join(os.path.dirname(sys.argv[0]), "UnityPackageManager")
server = subprocess.Popen([sys.executable, upm, "server", "-s", str(os.getpid())],
                          start_new_session=True)
open(os.path.join(os.path.dirname(sys.argv[0]), "upm.pid"), "w").write(str(server.pid))
print("fake unity: done", flush=True)
"""

FAKE_UPM = "import time\ntime.sleep(120)\n"


@pytest.mark.skipif(sys.platform == "win32", reason="the orphaned server is a Mac problem")
def test_a_lingering_package_manager_does_not_hold_the_run(
        monkeypatch: pytest.MonkeyPatch, tmp_path: Path, capsys: pytest.CaptureFixture) -> None:
    (tmp_path / "unity.py").write_text(FAKE_UNITY)
    (tmp_path / "UnityPackageManager").write_text(FAKE_UPM)
    monkeypatch.setattr(unity, "base_args", lambda wt: [sys.executable, str(tmp_path / "unity.py")])
    wt = paths.Worktree(tmp_path / "wt")

    started = time.monotonic()
    assert unity.run_cold(wt, [], "fake") == 0
    assert time.monotonic() - started < 10

    assert "fake unity: done" in (wt.logs / "fake.log").read_text()
    upm = int((tmp_path / "upm.pid").read_text())
    deadline = time.monotonic() + 5
    while unity.pid_alive(upm) and time.monotonic() < deadline:
        time.sleep(0.05)
    assert not unity.pid_alive(upm)


# Unity's NUnit results, trimmed: one pass, one failure and one Assert.Ignore.
RESULTS = """\
<test-run total="3" passed="1" failed="1" skipped="1">
  <test-suite type="TestFixture" fullname="Demo2D.Tests.PlaybackTests" result="Failed">
    <test-case fullname="Demo2D.Tests.PlaybackTests.Passes" result="Passed" />
    <test-case fullname="Demo2D.Tests.PlaybackTests.Fails" result="Failed">
      <failure><message><![CDATA[expected 1 but was 2]]></message></failure>
    </test-case>
    <test-case fullname="Demo2D.Tests.PlaybackTests.ASeek" result="Skipped" label="Ignored">
      <reason><message><![CDATA[PlaybackData isn't baked: run demo2d unity BakePlayback]]></message></reason>
    </test-case>
  </test-suite>
</test-run>
"""


def test_results_name_each_failure_and_skip_with_its_reason(tmp_path: Path) -> None:
    xml = tmp_path / "results.xml"
    xml.write_text(RESULTS)
    s = unity.parse_nunit(xml)
    assert (s.total, s.passed, s.failed, s.skipped) == (3, 1, 1, 1)
    assert s.failures == "Demo2D.Tests.PlaybackTests.Fails: expected 1 but was 2"
    assert s.skips == ("Demo2D.Tests.PlaybackTests.ASeek: "
                       "PlaybackData isn't baked: run demo2d unity BakePlayback")
