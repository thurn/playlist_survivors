"""Tests for scripts/prune-cargo-target: `uv run --no-project python -m unittest scripts/test_prune_cargo_target.py`."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import time
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("prune-cargo-target")
DAY = 86400


class Target:
    """A Cargo workspace with one member, `app`, and a hand-made target/debug."""

    def __init__(self, root: Path) -> None:
        self.root = root
        (root / "Cargo.toml").write_text('[workspace]\nmembers = ["app"]\nresolver = "2"\n')
        (root / "app/src").mkdir(parents=True)
        (root / "app/Cargo.toml").write_text('[package]\nname = "app"\nversion = "0.0.0"\n')
        (root / "app/src/lib.rs").write_text("")
        self.profile = root / "target/debug"
        self.fingerprints: dict[str, int] = {}

    def unit(self, pkg: str, h: str, *, age_days: float, deps: tuple[str, ...] = ()) -> None:
        """A unit built age_days ago that depends on the units named deps, with its artifacts."""
        crate = pkg.replace("-", "_")
        fingerprint = int(h, 16) ^ 0x5A5A
        self.fingerprints[h] = fingerprint
        d = self.profile / ".fingerprint" / f"{pkg}-{h}"
        d.mkdir(parents=True)
        dep_list = [[0, "x", False, self.fingerprints[dep]] for dep in deps]
        (d / f"lib-{crate}").write_text(fingerprint.to_bytes(8, "little").hex())
        (d / f"lib-{crate}.json").write_text(json.dumps({"deps": dep_list}))
        (d / "invoked.timestamp").write_text("")
        artifacts = self.profile / "deps"
        artifacts.mkdir(exist_ok=True)
        files = [*d.iterdir(), artifacts / f"lib{crate}-{h}.rlib", artifacts / f"{crate}-{h}.d"]
        for f in files:
            f.touch()
        self.age(age_days, *files)

    def age(self, days: float, *paths: Path) -> None:
        t = time.time() - days * DAY
        for p in paths:
            os.utime(p, (t, t))

    def prune(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [str(SCRIPT), *args, str(self.root)], check=True, capture_output=True, text=True
        )

    def units(self) -> set[str]:
        return {p.name for p in (self.profile / ".fingerprint").iterdir()}

    def artifacts(self) -> set[str]:
        return {p.name for p in (self.profile / "deps").iterdir()}


class PruneCargoTarget(unittest.TestCase):
    def setUp(self) -> None:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.t = Target(Path(tmp.name))

    def test_keeps_old_dependencies_a_recent_build_uses(self) -> None:
        t = self.t
        t.unit("cfg-if", "1111111111111111", age_days=30)
        t.unit("serde", "2222222222222222", age_days=30, deps=("1111111111111111",))
        t.unit("app", "aaaaaaaaaaaaaaaa", age_days=0, deps=("2222222222222222",))
        t.prune()
        self.assertEqual(
            t.artifacts(),
            {
                "libcfg_if-1111111111111111.rlib",
                "cfg_if-1111111111111111.d",
                "libserde-2222222222222222.rlib",
                "serde-2222222222222222.d",
            },
        )

    def test_deletes_workspace_artifacts_but_keeps_their_fingerprints(self) -> None:
        t = self.t
        t.unit("app", "aaaaaaaaaaaaaaaa", age_days=0)
        session = t.profile / "incremental/app-0abc/s-1"
        session.mkdir(parents=True)
        (session / "dep-graph.bin").touch()
        t.prune()
        self.assertEqual(t.artifacts(), set())
        self.assertEqual(t.units(), {"app-aaaaaaaaaaaaaaaa"})
        self.assertEqual(list((t.profile / "incremental").iterdir()), [])

    def test_deletes_units_no_recent_build_uses(self) -> None:
        t = self.t
        t.unit("serde", "1111111111111111", age_days=30)  # an old version
        t.unit("app", "bbbbbbbbbbbbbbbb", age_days=30, deps=("1111111111111111",))
        t.unit("serde", "2222222222222222", age_days=1)
        t.unit("app", "aaaaaaaaaaaaaaaa", age_days=0, deps=("2222222222222222",))
        t.prune()
        self.assertEqual(t.units(), {"serde-2222222222222222", "app-aaaaaaaaaaaaaaaa"})
        self.assertEqual(
            t.artifacts(), {"libserde-2222222222222222.rlib", "serde-2222222222222222.d"}
        )

    def test_counts_the_window_back_from_the_newest_build(self) -> None:
        t = self.t
        t.unit("serde", "1111111111111111", age_days=40)
        t.unit("app", "aaaaaaaaaaaaaaaa", age_days=31)
        t.unit("libc", "2222222222222222", age_days=30)
        t.prune()
        self.assertEqual(t.units(), {"app-aaaaaaaaaaaaaaaa", "libc-2222222222222222"})

    def test_dry_run_deletes_nothing(self) -> None:
        t = self.t
        t.unit("app", "aaaaaaaaaaaaaaaa", age_days=0)
        before = t.artifacts()
        r = t.prune("--dry-run")
        self.assertEqual(t.artifacts(), before)
        self.assertIn("would delete 2 entries", r.stderr)


if __name__ == "__main__":
    unittest.main()
