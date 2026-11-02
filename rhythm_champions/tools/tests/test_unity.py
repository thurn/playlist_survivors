"""Cold Unity runs end when Unity exits, whatever its package manager server does; results; the Accelerator."""

from __future__ import annotations

import socket
import sys
import time
from pathlib import Path

import pytest

from champions_tools import paths, unity

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

FAKE_UPM = "import time\ntime.sleep(600)\n"


@pytest.mark.skipif(sys.platform == "win32", reason="the orphaned server is a Mac problem")
def test_a_lingering_package_manager_does_not_hold_the_run(
        monkeypatch: pytest.MonkeyPatch, tmp_path: Path, capsys: pytest.CaptureFixture) -> None:
    (tmp_path / "unity.py").write_text(FAKE_UNITY)
    (tmp_path / "UnityPackageManager").write_text(FAKE_UPM)
    monkeypatch.setattr(unity, "base_args", lambda wt: [sys.executable, str(tmp_path / "unity.py")])
    wt = paths.Worktree(tmp_path / "wt")

    started = time.monotonic()
    assert unity.run_cold(wt, [], "fake") == 0
    assert time.monotonic() - started < 300  # a hang guard: the server would hold the pipe 600 s

    assert "fake unity: done" in (wt.logs / "fake.log").read_text()
    upm = int((tmp_path / "upm.pid").read_text())
    deadline = time.monotonic() + 60
    while unity.pid_alive(upm) and time.monotonic() < deadline:
        time.sleep(0.05)
    assert not unity.pid_alive(upm)


# Unity's NUnit results, trimmed: one pass, one failure and one Assert.Ignore.
RESULTS = """\
<test-run total="3" passed="1" failed="1" skipped="1">
  <test-suite type="TestFixture" fullname="Champions.Tests.BootTests" result="Failed">
    <test-case fullname="Champions.Tests.BootTests.Passes" result="Passed" />
    <test-case fullname="Champions.Tests.BootTests.Fails" result="Failed">
      <failure><message><![CDATA[expected 1 but was 2]]></message></failure>
    </test-case>
    <test-case fullname="Champions.Tests.BootTests.ASeek" result="Skipped" label="Ignored">
      <reason><message><![CDATA[Assets/Baked isn't baked: run champions build]]></message></reason>
    </test-case>
  </test-suite>
</test-run>
"""


def test_results_name_each_failure_and_skip_with_its_reason(tmp_path: Path) -> None:
    xml = tmp_path / "results.xml"
    xml.write_text(RESULTS)
    s = unity.parse_nunit(xml)
    assert (s.total, s.passed, s.failed, s.skipped) == (3, 1, 1, 1)
    assert s.failures == "Champions.Tests.BootTests.Fails: expected 1 but was 2"
    assert s.skips == ("Champions.Tests.BootTests.ASeek: "
                       "Assets/Baked isn't baked: run champions build")


def test_launches_use_the_accelerator_only_when_one_answers(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    wt = paths.Worktree(tmp_path)
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen()
        endpoint = f"127.0.0.1:{listener.getsockname()[1]}"
        monkeypatch.setenv("UNITY_ACCELERATOR", endpoint)
        args = unity.base_args(wt)
        assert args[args.index("-cacheServerEndpoint") + 1] == endpoint
        assert "-EnableCacheServer" in args
    monkeypatch.setenv("UNITY_ACCELERATOR", endpoint)  # closed now
    assert "-EnableCacheServer" not in unity.base_args(wt)
