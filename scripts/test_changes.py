"""Tests for scripts/lib/changes.sh's ts_inputs_hash: `uv run --no-project python -m unittest scripts/test_changes.py`."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

CHANGES = Path(__file__).resolve().parent / "lib" / "changes.sh"


def sh(cwd: Path, script: str) -> str:
    out = subprocess.run(
        ["sh", "-c", script], cwd=cwd, check=True, capture_output=True, text=True
    )
    return out.stdout.strip()


class TsInputsHash(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self._tmp.name)
        sh(
            self.repo,
            "git init -q && git config user.email t@t && git config user.name t",
        )
        for path in ("src/game.ts", "native/Cargo.toml", "docs/plan.md", "README.md"):
            (self.repo / path).parent.mkdir(parents=True, exist_ok=True)
            (self.repo / path).write_text(path + "\n")
        self.commit()

    def tearDown(self) -> None:
        self._tmp.cleanup()

    def commit(self) -> None:
        sh(self.repo, "git add -A && git commit -qm x")

    def hash(self) -> str:
        return sh(self.repo, f". '{CHANGES}' && ts_inputs_hash")

    def change(self, path: str) -> None:
        (self.repo / path).write_text("changed\n")
        self.commit()

    def test_only_ts_paths_change_the_hash(self) -> None:
        first = self.hash()
        self.assertRegex(first, "^[0-9a-f]{64}$")
        for path in ("native/Cargo.toml", "docs/plan.md", "README.md"):
            self.change(path)
            self.assertEqual(self.hash(), first, path)
        self.change("src/game.ts")
        self.assertNotEqual(self.hash(), first)

    def test_uncommitted_changes_give_no_hash(self) -> None:
        (self.repo / "src/game.ts").write_text("dirty\n")
        self.assertEqual(self.hash(), "")


if __name__ == "__main__":
    unittest.main()
