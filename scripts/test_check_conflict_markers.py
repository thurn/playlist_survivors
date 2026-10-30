"""Tests for scripts/check-conflict-markers: `uv run --no-project python -m unittest scripts/test_check_conflict_markers.py`."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("check-conflict-markers")


def check(*contents: bytes) -> subprocess.CompletedProcess[str]:
    with tempfile.TemporaryDirectory() as d:
        paths = []
        for i, c in enumerate(contents):
            p = Path(d) / f"f{i}"
            p.write_bytes(c)
            paths.append(str(p))
        return subprocess.run(
            ["sh", str(SCRIPT), *paths, str(Path(d) / "missing")],  # sh: Windows runs no shebang
            check=False,
            capture_output=True,
            text=True,
        )


class ConflictMarkers(unittest.TestCase):
    def test_clean_files_pass(self) -> None:
        r = check(b"# Title\n\nSome text.\n", b"a ======= b\n========\n")
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_each_marker_fails(self) -> None:
        for marker in (
            b"<<<<<<< HEAD",
            b"||||||| parent of 905ee94 (feat: x)",
            b"=======",
            b">>>>>>> 905ee94 (feat: x)",
        ):
            r = check(b"text\n" + marker + b"\nmore\n")
            self.assertEqual(r.returncode, 1, marker)
            self.assertIn(":2:", r.stderr)

    def test_binary_files_are_skipped(self) -> None:
        r = check(b"\x00\x01binary\n=======\n")
        self.assertEqual(r.returncode, 0, r.stderr)


if __name__ == "__main__":
    unittest.main()
