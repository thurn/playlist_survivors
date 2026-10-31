"""`demo3d build` in a fresh worktree: it bakes first, and its first build warms the cache."""

from __future__ import annotations

import re
import shutil
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


def fake_unity(wt: paths.Worktree, ran: list[str], fail_build: bool = False):
    """A run_method that bakes as Bakers.Run does (recording each baker it ran) and builds a player."""

    def run_method(wt, name, args, cold=False):
        ran.append(name if "-only" not in args else
                   f"{name} " + ",".join(args[i + 1] for i, x in enumerate(args) if x == "-only"))
        if name in ("BakeAll", "BakeAndBuild"):
            only = [args[i + 1] for i, x in enumerate(args) if x == "-only"]
            wt.baked.mkdir(parents=True, exist_ok=True)
            (wt.baked / setup.BAKED_RAN).write_text("".join(n + "\n" for n in only or setup.BAKER_INPUTS))
        if name in ("BuildPlayer", "BakeAndBuild"):
            if fail_build:
                return 1
            app = wt.project / "Builds" / "mac" / args[args.index("-backend") + 1] / "Player.app"
            app.mkdir(parents=True, exist_ok=True)
            (app / "Info.plist").write_text("built\n")
        return 0

    return run_method


def build(monkeypatch: pytest.MonkeyPatch, *argv: str, fail: bool = False) -> tuple[int, list[str]]:
    ran: list[str] = []
    monkeypatch.setattr(unity, "run_method", fake_unity(paths.worktree(), ran, fail))
    a = cli.build_parser().parse_args(["build", "--platform", "mac", "--backend", "mono", *argv])
    return a.func(a), ran


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)


def test_an_unbaked_worktree_bakes_and_builds_in_one_launch(monkeypatch, wt) -> None:
    assert build(monkeypatch) == (0, ["BakeAndBuild"])
    assert build(monkeypatch) == (0, [])  # nothing changed: the player stands
    assert build(monkeypatch, "--force") == (0, ["BuildPlayer"])


def test_a_build_rebakes_only_the_bakers_whose_inputs_changed(monkeypatch, wt) -> None:
    demo = wt.project / "Assets" / "Demo3D"
    shaders = demo / "Runtime" / "Crowd" / "Shaders"
    write(shaders / "CrowdSkinned.shader", "v1\n")
    write(demo / "Choreography" / "Data" / "environment-neon.json", "{}\n")
    assert build(monkeypatch) == (0, ["BakeAndBuild"])
    assert build(monkeypatch) == (0, [])
    write(shaders / "Styles" / "FireBody.shader", "new\n")  # a shader the crowd library lists
    assert build(monkeypatch) == (0, ["BakeAndBuild crowd"])
    assert build(monkeypatch) == (0, [])
    write(demo / "Choreography" / "Data" / "environment-neon.json", '{"fog": 1}\n')
    assert build(monkeypatch) == (0, ["BakeAndBuild Arena,StructureShards,Playback"])


def test_an_input_no_baker_claims_rebakes_them_all(monkeypatch, wt) -> None:
    package = wt.root / "unity-packages" / "com.playlistsurvivors.choreography" / "Runtime"
    write(package / "Sim.cs", "v1\n")
    assert build(monkeypatch) == (0, ["BakeAndBuild"])
    write(package / "Sim.cs", "v2\n")
    assert build(monkeypatch) == (0, ["BakeAndBuild"])  # a full run: no -only


def test_a_changed_project_file_rebuilds_but_a_touched_one_does_not(monkeypatch, wt) -> None:
    settings = wt.project / "ProjectSettings" / "ProjectSettings.asset"
    write(settings, "backend: 0\n")
    assert build(monkeypatch) == (0, ["BakeAndBuild"])
    write(settings, "backend: 0\n")  # Unity rewrote it unchanged
    assert build(monkeypatch) == (0, [])
    write(settings, "backend: 1\n")
    assert build(monkeypatch) == (0, ["BuildPlayer"])
    assert build(monkeypatch, "--development") == (0, ["BuildPlayer"])  # other flags: another player
    assert build(monkeypatch, "--development") == (0, [])
    assert build(monkeypatch) == (0, ["BuildPlayer"])


def test_a_deleted_player_is_rebuilt(monkeypatch, wt) -> None:
    build(monkeypatch)
    shutil.rmtree(wt.project / "Builds" / "mac" / "mono" / "Player.app")
    assert build(monkeypatch) == (0, ["BuildPlayer"])


def test_a_bake_whose_build_fails_is_still_recorded(monkeypatch, wt) -> None:
    assert build(monkeypatch, fail=True) == (1, ["BakeAndBuild"])
    assert build(monkeypatch) == (0, ["BuildPlayer"])


def test_only_runtime_code_that_assets_serialize_or_bakers_call_is_a_bake_input(wt) -> None:
    runtime = wt.project / "Assets" / "Demo3D" / "Runtime"
    tests = wt.project / "Assets" / "Demo3D" / "Tests"
    write(runtime / "CrowdLibrary.cs", "class CrowdLibrary : ScriptableObject {}\n")
    write(runtime / "DemoMain.cs", "class DemoMain : MonoBehaviour {}\n")
    write(runtime / "Playback" / "ShatterSounds.cs", "static class ShatterSounds {}\n")
    write(tests / "SomeTests.cs", "class SomeTests {}\n")
    names = {f.name for f in setup.bake_inputs(wt)}
    assert {"CrowdLibrary.cs", "ShatterSounds.cs"} <= names
    assert not names & {"DemoMain.cs", "SomeTests.cs"}


def test_every_registered_baker_has_its_inputs_listed() -> None:
    editor = Path(__file__).resolve().parents[2] / "Assets" / "Demo3D" / "Editor"
    registered = {m for f in editor.rglob("*.cs")
                  for m in re.findall(r'Bakers\.Register\("([^"]+)"', f.read_text(encoding="utf-8"))}
    assert registered == setup.BAKER_INPUTS.keys()


def test_the_games_bakers_have_their_inputs_listed() -> None:
    tasks = Path(__file__).resolve().parents[3] / "game" / "Assets" / "Game" / "Editor" / "Tasks.cs"
    registered = set(re.findall(r'\("(\w+)", Bake\w+\)', tasks.read_text(encoding="utf-8")))
    assert registered == setup.GAME_BAKER_INPUTS.keys()


def test_the_game_bakes_its_arena_when_the_layout_changes(tmp_path: Path) -> None:
    wt = paths.Worktree(tmp_path, "game")
    (wt.project / "Content").mkdir(parents=True)
    layout = wt.project / "Content" / "arena-layout.json"
    layout.write_text("{}")
    assert setup.stale_bakers(wt) == ["Arena"]
    wt.baked.mkdir(parents=True)
    (wt.baked / setup.BAKED_RAN).write_text("Arena\n")
    assert setup.record_ran(wt) == ["Arena"]
    assert setup.baked(wt)
    layout.write_text('{"seed": 8}')
    assert setup.stale_bakers(wt) == ["Arena"]


def test_a_bakeall_through_the_tools_records_what_it_ran(monkeypatch, wt) -> None:
    monkeypatch.setattr(unity, "run_method", fake_unity(wt, []))
    assert not setup.baked(wt)
    a = cli.build_parser().parse_args(["unity", "BakeAll", "-only", "crowd"])
    assert a.func(a) == 0
    assert setup.stale_bakers(wt) == [n for n in setup.BAKER_INPUTS if n != "crowd"]
    a = cli.build_parser().parse_args(["unity", "BakeAll"])
    assert a.func(a) == 0
    assert setup.baked(wt)


def test_the_first_build_saves_the_library_once(monkeypatch, wt) -> None:
    build(monkeypatch)
    entry = paths.cache_root() / "library" / setup.library_key(wt) / setup.manifest_key(wt)
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"
    assert setup.built_marker(entry).exists()
    (wt.library / "ShaderCache" / "variant.bin").write_bytes(b"recompiled")
    build(monkeypatch)
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"


def test_a_build_that_compiles_many_variants_refreshes_the_library(monkeypatch, wt) -> None:
    build(monkeypatch)
    entry = paths.cache_root() / "library" / setup.library_key(wt) / setup.manifest_key(wt)
    (wt.library / "ShaderCache" / "variant.bin").write_bytes(b"recompiled")
    wt.logs.mkdir(parents=True, exist_ok=True)
    log = wt.logs / "BuildPlayer.log"
    pass_line = "  Pass Bloom (fp, metal) finished in 0.5 seconds. Local cache hits 1, compiled {} variants"
    log.write_text("\n".join(pass_line.format(n) for n in (60, 40)) + "\n")
    build(monkeypatch, "--force")  # 100 variants: not enough to save again
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"
    log.write_text(log.read_text() + pass_line.format(1) + "\n")
    build(monkeypatch, "--force")
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"recompiled"
    assert setup.built_marker(entry).exists()


def test_setups_import_only_save_clears_the_built_marker(wt) -> None:
    entry = setup.save_built_library(wt)
    assert entry and setup.built_marker(entry).exists()
    setup.save_library(wt)
    assert not setup.built_marker(entry).exists()
    assert setup.save_built_library(wt) == entry


def test_demo2d_has_no_unity_bakers(tmp_path: Path) -> None:
    assert setup.baked(paths.Worktree(tmp_path, "demo2d"))
    assert not setup.baked(paths.Worktree(tmp_path, "demo3d"))


def test_stylelab_is_a_project_of_its_own(tmp_path: Path) -> None:
    wt = paths.Worktree(tmp_path, "stylelab")
    assert wt.project == tmp_path / "stylelab"
    assert wt.namespace == "StyleLab"
    assert setup.baked(wt)  # its bake runs inside every build
    assert paths.Worktree(tmp_path, "demo2d").namespace == "Demo2D"


def test_setup_prefers_a_built_library_when_none_is_exact(tmp_path: Path) -> None:
    base = tmp_path / "library" / "pkgs"
    built, newer = base / "a", base / "b"
    for p in (built, newer):
        p.mkdir(parents=True)
    setup.built_marker(built).write_text("then\n")
    assert setup.library_candidates(tmp_path, "pkgs", "c") == [built, newer]
    assert setup.library_candidates(tmp_path, "pkgs", "b") == [newer]


def test_the_game_is_a_project_of_its_own(tmp_path: Path) -> None:
    wt = paths.Worktree(tmp_path, "game")
    assert wt.project == tmp_path / "game"
    assert wt.namespace == "Game"
    assert setup.PY_BAKERS["game"] == []  # it plays the player's music, so it never needs the song
    assert "song" not in paths.PROJECT_ROOTS["game"]
