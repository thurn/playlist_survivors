"""Tests for native/scripts/crates: run with `uv run --no-project python -m unittest native/scripts/test_crates.py`."""

from __future__ import annotations

import importlib.machinery
import importlib.util
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

_path = Path(__file__).with_name("crates")
_loader = importlib.machinery.SourceFileLoader("crates", str(_path))
_spec = importlib.util.spec_from_loader("crates", _loader)
crates = importlib.util.module_from_spec(_spec)
_loader.exec_module(crates)

FORBID = "//! doc\n#![forbid(unsafe_code)]\n"


def meta(
    edges: dict[str, list[str]],
    members: list[str],
    dev: dict[str, list[str]] | None = None,
) -> dict:
    """Synthetic `cargo metadata`: package ids are their names; members live in crates/<name>."""
    names = set(edges) | {d for ds in edges.values() for d in ds}
    dev = dev or {}
    return {
        "packages": [
            {"id": n, "name": n, "manifest_path": f"/w/native/crates/{n}/Cargo.toml"}
            for n in sorted(names)
        ],
        "workspace_members": members,
        "resolve": {
            "nodes": [
                {
                    "id": n,
                    "deps": [
                        {"pkg": d, "dep_kinds": [{"kind": None}]}
                        for d in edges.get(n, [])
                    ]
                    + [
                        {"pkg": d, "dep_kinds": [{"kind": "dev"}]}
                        for d in dev.get(n, [])
                    ],
                }
                for n in sorted(names)
            ]
        },
    }


WORKSPACE = {
    "stream": [],
    "analysis": ["stream"],
    "sim": ["stream"],
    "eval": ["sim"],
    "players": ["sim"],
    "audio": ["objc2"],
    "render": ["eval", "wgpu"],
    "app": ["audio", "render", "analysis"],
    "cli": ["analysis", "players"],
}
MEMBERS = list(WORKSPACE)
SOURCES = {c: FORBID for c in crates.PURE}


class Purity(unittest.TestCase):
    def test_the_planned_graph_is_pure(self) -> None:
        self.assertEqual(crates.purity_errors(meta(WORKSPACE, MEMBERS), SOURCES), [])

    def test_a_transitive_impure_dependency_fails(self) -> None:
        g = dict(WORKSPACE, players=["sim", "helper"], helper=["winit"])
        errors = crates.purity_errors(meta(g, MEMBERS), SOURCES)
        self.assertEqual(errors, ["players: depends on winit"])

    def test_depending_on_an_impure_crate_fails(self) -> None:
        g = dict(WORKSPACE, eval=["sim", "render"])
        errors = crates.purity_errors(meta(g, MEMBERS), SOURCES)
        self.assertEqual(errors, ["eval: depends on render, wgpu"])

    def test_dev_dependencies_do_not_count(self) -> None:
        m = meta(WORKSPACE, MEMBERS, dev={"sim": ["audio"]})
        self.assertEqual(crates.purity_errors(m, SOURCES), [])

    def test_a_pure_crate_without_forbid_unsafe_fails(self) -> None:
        errors = crates.purity_errors(
            meta(WORKSPACE, MEMBERS), dict(SOURCES, stream="//! doc\n")
        )
        self.assertEqual(errors, ["stream: a target lacks #![forbid(unsafe_code)]"])

    def test_forbid_unsafe_in_a_comment_does_not_count(self) -> None:
        commented = "//! doc\n// #![forbid(unsafe_code)]\n"
        errors = crates.purity_errors(
            meta(WORKSPACE, MEMBERS), dict(SOURCES, sim=[FORBID, commented])
        )
        self.assertEqual(errors, ["sim: a target lacks #![forbid(unsafe_code)]"])


class RootFiles(unittest.TestCase):
    def test_only_library_and_binary_targets_must_forbid_unsafe(self) -> None:
        m = meta(WORKSPACE, MEMBERS)
        for p in m["packages"]:
            p["targets"] = [
                {"kind": ["lib"], "src_path": f"/w/{p['name']}/src/lib.rs"},
                {"kind": ["test"], "src_path": f"/w/{p['name']}/tests/t.rs"},
                {"kind": ["bench"], "src_path": f"/w/{p['name']}/benches/b.rs"},
            ]
        roots = crates.root_files(m)
        self.assertEqual(sorted(roots), sorted(crates.PURE))
        self.assertEqual(roots["sim"], ["/w/sim/src/lib.rs"])


class Affected(unittest.TestCase):
    def test_a_crate_and_its_dependents(self) -> None:
        got = crates.affected(
            meta(WORKSPACE, MEMBERS), ["native/crates/sim/src/lib.rs"]
        )
        self.assertEqual(got, ["app", "cli", "eval", "players", "render", "sim"])

    def test_dev_dependents_are_affected(self) -> None:
        """cli's tests use eval: a change to eval can break them."""
        m = meta(WORKSPACE, MEMBERS, dev={"cli": ["eval"]})
        got = crates.affected(m, ["native/crates/eval/src/lib.rs"])
        self.assertEqual(got, ["app", "cli", "eval", "render"])

    def test_a_leaf_crate_alone(self) -> None:
        got = crates.affected(
            meta(WORKSPACE, MEMBERS), ["native/crates/cli/src/main.rs"]
        )
        self.assertEqual(got, ["cli"])

    def test_workspace_files_and_content_affect_everything(self) -> None:
        m = meta(WORKSPACE, MEMBERS)
        for path in (
            "native/Cargo.lock",
            "native/rust-toolchain.toml",
            "native/models/madmom.bin",
            "native/oracle/fixtures.json.gz",
            "content/enemies.yaml",
            "levels/reactive.yaml",
            "levels/survivors.yaml",
            "levels/play.yaml",
            "levels/custom/endless.yml",
            "public/reactive/house-128/stream.json",
        ):
            self.assertIsNone(crates.affected(m, [path]), path)

    def test_other_paths_affect_nothing(self) -> None:
        m = meta(WORKSPACE, MEMBERS)
        self.assertEqual(
            crates.affected(
                m,
                [
                    "docs/design/native-app.md",
                    "native/evidence/x.json",
                    "native/scripts/crates",
                    "levels/README.md",
                    "levels/survivors.yaml.bak",
                    "docs/levels/survivors.yaml",
                ],
            ),
            [],
        )


class NativeSection(unittest.TestCase):
    """Run the real POSIX section selector as the gate does, including absent/deleted paths."""

    def selected(self, paths: list[str], unknown: bool = False) -> bool:
        script = Path(__file__).resolve().parents[2] / "scripts/lib/changes.sh"
        with tempfile.NamedTemporaryFile(mode="w") as changed:
            changed.write("\n".join(paths) + "\n")
            changed.flush()
            result = subprocess.run(
                [
                    "/bin/sh",
                    "-c",
                    '. "$1"; changes_native',
                    "selector-test",
                    str(script),
                ],
                env=dict(
                    os.environ,
                    CHANGES_FILE=changed.name,
                    CHANGES_UNKNOWN=str(int(unknown)),
                ),
                capture_output=True,
                text=True,
                check=False,
            )
        self.assertIn(result.returncode, (0, 1), result.stderr)
        return result.returncode == 0

    def test_level_files_select_native_without_needing_to_exist(self) -> None:
        for path in (
            "levels/survivors.yaml",
            "levels/reactive.yaml",
            "levels/new.yml",
            "levels/custom/deleted.yaml",
        ):
            with self.subTest(path=path):
                self.assertTrue(self.selected([path]))

    def test_both_ends_of_a_level_rename_are_independently_selected(self) -> None:
        # changes_init uses --no-renames, so a move's old and new names both count.
        self.assertTrue(self.selected(["levels/survivors.yaml", "docs/retired.yaml"]))
        self.assertTrue(self.selected(["docs/draft.yaml", "levels/new.yaml"]))

    def test_unrelated_paths_do_not_select_native(self) -> None:
        for path in (
            "levels/README.md",
            "levels/survivors.yaml.bak",
            "docs/levels/x.yaml",
            "native/evidence/sf6/README.md",
            "songs/example/songmap.json",
        ):
            with self.subTest(path=path):
                self.assertFalse(self.selected([path]))

    def test_selector_changes_run_their_own_regressions(self) -> None:
        self.assertTrue(self.selected(["scripts/lib/changes.sh"]))

    def test_unknown_base_conservatively_selects_native(self) -> None:
        self.assertTrue(self.selected([], unknown=True))


if __name__ == "__main__":
    unittest.main()
