"""`demo3d build` in a fresh worktree: it bakes first, and its first build warms the cache."""

from __future__ import annotations

from pathlib import Path

import pytest

from demo3d_tools import cli, paths, setup, unity


@pytest.fixture
def wt(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> paths.Worktree:
    root = tmp_path / "wt"
    p = root / "demo3d"
    (p / "ProjectSettings").mkdir(parents=True)
    (p / "Packages").mkdir()
    (p / "ProjectSettings" / "ProjectVersion.txt").write_text("6000.6.7f1\n")
    (p / "Packages" / "manifest.json").write_text("{}\n")
    (p / "Packages" / "packages-lock.json").write_text("{}\n")
    (p / "manifest.json").write_text("{}\n")
    (p / "Library" / "ShaderCache").mkdir(parents=True)
    (p / "Library" / "ShaderCache" / "variant.bin").write_bytes(b"compiled")
    monkeypatch.setenv("DEMO3D_CACHE", str(tmp_path / "cache"))
    w = paths.Worktree(root)
    monkeypatch.setattr(paths, "worktree", lambda name="demo3d", start=None: w)
    monkeypatch.setattr(unity, "warm_server", lambda wt: None)
    return w


def build(monkeypatch: pytest.MonkeyPatch, *argv: str) -> tuple[int, list[str]]:
    ran: list[str] = []

    def run_method(wt, name, args, cold=False):
        ran.append(name)
        if name == "BakeAll":
            (wt.baked).mkdir(parents=True, exist_ok=True)
            (wt.baked / setup.BAKED_STAMP).write_text("now\n")
        return 0

    monkeypatch.setattr(unity, "run_method", run_method)
    a = cli.build_parser().parse_args(["build", "--backend", "mono", *argv])
    return a.func(a), ran


def test_an_unbaked_worktree_bakes_before_it_builds(monkeypatch, wt) -> None:
    assert build(monkeypatch) == (0, ["BakeAll", "BuildPlayer"])
    assert build(monkeypatch) == (0, ["BuildPlayer"])  # the stamp stays


def test_the_first_build_saves_the_library_once(monkeypatch, wt) -> None:
    build(monkeypatch)
    entry = paths.cache_root() / "library" / setup.library_key(wt) / setup.manifest_key(wt)
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"
    assert setup.built_marker(entry).exists()
    (wt.library / "ShaderCache" / "variant.bin").write_bytes(b"recompiled")
    build(monkeypatch)
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"


def test_setups_import_only_save_clears_the_built_marker(wt) -> None:
    entry = setup.save_built_library(wt)
    assert entry and setup.built_marker(entry).exists()
    setup.save_library(wt)
    assert not setup.built_marker(entry).exists()
    assert setup.save_built_library(wt) == entry


def test_demo2d_has_no_unity_bakers(tmp_path: Path) -> None:
    assert setup.baked(paths.Worktree(tmp_path, "demo2d"))
    assert not setup.baked(paths.Worktree(tmp_path, "demo3d"))
