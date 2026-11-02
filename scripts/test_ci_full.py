"""Tests for scripts/ci-full's lanes, step times and budget, on its guard lane with a stand-in `uv`:
`uv run --no-project python -m unittest scripts/test_ci_full.py`."""

from __future__ import annotations

import os
import re
import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("ci-full")
# Each of the guard lane's two steps runs `uv`, which this one stands in for: it sleeps FAKE_SLEEP
# seconds and exits FAKE_STATUS.
FAKE_UV = '#!/bin/sh\necho "uv $*"\nsleep "${FAKE_SLEEP:-0}"\nexit "${FAKE_STATUS:-0}"\n'


def ci_full(**env: str) -> subprocess.CompletedProcess[str]:
    with tempfile.TemporaryDirectory() as d:
        uv = Path(d) / "uv"
        uv.write_text(FAKE_UV)
        uv.chmod(0o755)
        environ = {k: v for k, v in os.environ.items() if k != "CI_BUDGET_S"}
        environ.update(env, PATH=f"{d}{os.pathsep}{os.environ['PATH']}")
        return subprocess.run(
            ["sh", str(SCRIPT), "guard"],  # sh: Windows runs no shebang
            check=False,
            capture_output=True,
            text=True,
            env=environ,
        )


class CiFull(unittest.TestCase):
    def test_a_passing_run_logs_each_steps_seconds(self) -> None:
        r = ci_full()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertRegex(r.stdout, r"ci-full guard tree: \d+ s\n")
        self.assertRegex(r.stdout, r"ci-full: guard \d+ s \(tree \d+ s, range \d+ s\)\n")
        self.assertRegex(r.stdout, r"ci-full: \d+ s, budget 300 s\n")

    def test_a_run_over_its_budget_fails(self) -> None:
        # Two steps of at least a second each: at least 2 s, over a budget of 1 s however fast the machine.
        r = ci_full(CI_BUDGET_S="1", FAKE_SLEEP="1")
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self.assertIn("=== ci-full guard: passed", r.stdout)
        self.assertRegex(r.stdout, r"ci-full: \d+ s, budget 1 s\nci-full: FAILED: over the 1 s budget")

    def test_a_failing_step_fails_its_lane_and_the_run(self) -> None:
        r = ci_full(FAKE_STATUS="3")
        self.assertEqual(r.returncode, 1, r.stdout + r.stderr)
        self.assertIn("ci-full guard tree: FAILED (exit 3)", r.stdout)
        self.assertNotIn("guard --range", r.stdout)  # the lane stops at its first failure
        self.assertTrue(r.stdout.rstrip().endswith("budget 300 s"), r.stdout)
        self.assertIsNotNone(re.search(r"^ci-full: failed: guard$", r.stdout, re.MULTILINE), r.stdout)


if __name__ == "__main__":
    unittest.main()
