"""`champions build` and `champions check --variants` against a stand-in for Unity: what they
rebuild, what they cache, and what they report."""

from __future__ import annotations

import json
import shutil
from pathlib import Path

import pytest

from champions_tools import cli, paths, setup, unity


@pytest.fixture
def wt(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> paths.Worktree:
    root = tmp_path / "wt"
    p = root / paths.PROJECT
    (p / "ProjectSettings").mkdir(parents=True)
    (p / "Packages").mkdir()
    (p / "ProjectSettings" / "ProjectVersion.txt").write_text("6000.6.7f1\n")
    (p / "Packages" / "manifest.json").write_text("{}\n")
    (p / "Packages" / "packages-lock.json").write_text("{}\n")
    (p / "manifest.json").write_text('{"version": 1, "assets": []}\n')
    (p / "Assets" / "Vendor").mkdir(parents=True)
    (p / "Library" / "ShaderCache").mkdir(parents=True)
    (p / "Library" / "ShaderCache" / "variant.bin").write_bytes(b"compiled")
    (p / "Library" / setup.VENDOR_STAMP).write_bytes((p / "manifest.json").read_bytes())
    w = paths.Worktree(root)
    monkeypatch.setattr(paths, "worktree", lambda start=None: w)
    monkeypatch.setattr(unity, "warm_server", lambda wt: None)
    monkeypatch.setattr(unity, "editor_stop", lambda wt: False)
    return w


def fake_unity(ran: list[list[str]], fail: bool = False, variants: int = 200, bakes: bool = False):
    """A run_method that builds a player as Tasks.BuildPlayer does: the app, and beside it the
    variant report, failing (as ShaderVariantBudget does) over the budget."""

    def run_method(wt, name, args, cold=False):
        ran.append([name, *args])
        if fail:
            return 1
        folder = wt.project / "Builds" / "mac" / args[args.index("-backend") + 1]
        (folder / setup.APP).mkdir(parents=True, exist_ok=True)
        (folder / setup.APP / "Info.plist").write_text("built\n")
        (folder / cli.VARIANTS_REPORT).write_text(json.dumps({
            "budget": 1000, "total": variants, "fresh": "-cleanBuildCache" in args,
            "snippets": [{"shader": "Hidden/Universal/CoreBlit", "pass": "Nearest", "stage": "fragment",
                          "variants": variants}]}))
        if bakes:
            (setup.baked_dir(wt) / "Effects" / "Resources").mkdir(parents=True, exist_ok=True)
            (setup.baked_dir(wt) / "Effects" / "Resources" / "Effects.asset").write_text("baked\n")
        return 0 if variants <= 1000 else 1

    return run_method


def run(monkeypatch: pytest.MonkeyPatch, *argv: str, **fake) -> tuple[int, list[list[str]]]:
    ran: list[list[str]] = []
    monkeypatch.setattr(unity, "run_method", fake_unity(ran, **fake))
    monkeypatch.setattr(cli.player_logs, "install", lambda app, root: None)
    a = cli.build_parser().parse_args(list(argv))
    return a.func(a), ran


def builds(ran: list[list[str]]) -> list[str]:
    return [" ".join(r[0:1] + [x for x in r[1:] if x in ("-development", "-cleanBuildCache")]) for r in ran]


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)


def test_a_changed_project_file_rebuilds_but_a_touched_one_does_not(monkeypatch, wt) -> None:
    settings = wt.project / "ProjectSettings" / "ProjectSettings.asset"
    write(settings, "backend: 0\n")
    code, ran = run(monkeypatch, "build")
    assert (code, builds(ran)) == (0, ["BuildPlayer"])
    assert ran[0][ran[0].index("-backend") + 1] == "mono"  # the default backend
    write(settings, "backend: 0\n")  # Unity rewrote it unchanged
    assert run(monkeypatch, "build")[1] == []
    write(settings, "backend: 1\n")
    assert builds(run(monkeypatch, "build")[1]) == ["BuildPlayer"]
    assert builds(run(monkeypatch, "build", "--development")[1]) == ["BuildPlayer -development"]
    assert run(monkeypatch, "build", "--development")[1] == []


def test_a_deleted_player_is_rebuilt_and_a_failed_build_claims_nothing(monkeypatch, wt) -> None:
    run(monkeypatch, "build")
    shutil.rmtree(wt.project / "Builds" / "mac" / "mono" / setup.APP)
    assert builds(run(monkeypatch, "build")[1]) == ["BuildPlayer"]
    assert run(monkeypatch, "build", "--force", fail=True)[0] == 1
    assert builds(run(monkeypatch, "build")[1]) == ["BuildPlayer"]


def test_a_manifest_changed_since_the_last_sync_syncs_the_vendor_folder_first(monkeypatch, wt) -> None:
    manifest = wt.project / "manifest.json"
    manifest.write_text('{"version": 1, "assets": [], "note": "changed"}\n')
    assert run(monkeypatch, "build")[0] == 0
    assert (wt.library / setup.VENDOR_STAMP).read_bytes() == manifest.read_bytes()
    assert (paths.cache_root() / "vendor" / setup.manifest_key(wt)).is_dir()


def test_the_first_build_saves_the_library_once_with_its_bee(monkeypatch, wt) -> None:
    (wt.library / "Bee" / "artifacts").mkdir(parents=True)
    (wt.library / "Bee" / "artifacts" / "Champions.Client.dll").write_bytes(b"compiled")
    (wt.library / f"{paths.NAMESPACE}Warm").mkdir()
    run(monkeypatch, "build")
    entry = paths.cache_root() / "library" / setup.library_key(wt) / setup.manifest_key(wt)
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"
    assert (entry / "Bee" / "artifacts" / "Champions.Client.dll").exists()
    assert not (entry / f"{paths.NAMESPACE}Warm").exists()
    assert setup.built_marker(entry).exists()
    (wt.library / "ShaderCache" / "variant.bin").write_bytes(b"recompiled")
    run(monkeypatch, "build", "--force")
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"


def test_a_build_that_compiles_many_variants_refreshes_the_library(monkeypatch, wt) -> None:
    run(monkeypatch, "build")
    entry = paths.cache_root() / "library" / setup.library_key(wt) / setup.manifest_key(wt)
    (wt.library / "ShaderCache" / "variant.bin").write_bytes(b"recompiled")
    pass_line = "  Pass Bloom (fp, metal) finished in 0.5 seconds. Local cache hits 1, compiled {} variants"
    write(wt.logs / "BuildPlayer.log", "\n".join(pass_line.format(n) for n in (30, 20)) + "\n")
    run(monkeypatch, "build", "--force")  # 50 variants: not enough to save again
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"
    write(wt.logs / "BuildPlayer.log", (wt.logs / "BuildPlayer.log").read_text() + pass_line.format(1) + "\n")
    run(monkeypatch, "build", "--force")
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"recompiled"


def test_setups_import_only_save_clears_the_built_marker(wt) -> None:
    entry = setup.save_built_library(wt)
    assert entry and setup.built_marker(entry).exists()
    setup.save_library(wt)
    assert not setup.built_marker(entry).exists()
    assert setup.save_built_library(wt) == entry


def test_slow_cis_save_after_a_build_refreshes_the_library_and_keeps_it_built(monkeypatch, wt) -> None:
    run(monkeypatch, "build")
    entry = paths.cache_root() / "library" / setup.library_key(wt) / setup.manifest_key(wt)
    (wt.library / "ShaderCache" / "variant.bin").write_bytes(b"recompiled")
    assert run(monkeypatch, "library", "save", "--built")[0] == 0
    assert (entry / "ShaderCache" / "variant.bin").read_bytes() == b"recompiled"
    assert setup.built_marker(entry).exists()
    assert setup.save_built_library(wt) is None  # the next build needn't save it again


def test_a_restored_library_keeps_bees_artifacts_but_not_its_graphs(wt) -> None:
    """Bee's graphs name the worktree that saved them, and Unity compiles with a graph before
    verifying it (ps-6y2.25)."""
    bee = wt.library / "Bee"
    graphs = ["200b0aP.dag", "200b0aP.dag.json", "200b0aP.dag.outputdata", "200b0aP.dag_fsmtime",
              "200b0aP-inputdata.json", "200b0aE.dag.payloads", "Player5d03ebd7.dag_derived"]
    kept = ["TundraBuildState.state", "tundra.digestcache", "artifacts/200b0aP.dag/Champions.Sim.dll",
            "PlayerScriptAssemblies/Champions.Sim.dll"]
    for name in graphs + kept:
        write(bee / name, "/Users/me/old-worktree\n")
    setup.save_library(wt)
    for vendor_key, exact in ((setup.manifest_key(wt), True), ("other-vendor-files", False)):
        restored = setup.restore_library(wt, vendor_key)
        assert restored and restored[1] is exact
        assert sorted(p.relative_to(bee).as_posix() for p in bee.rglob("*") if p.is_file()) == sorted(kept)
        assert (wt.library / "ShaderCache" / "variant.bin").read_bytes() == b"compiled"
    shutil.rmtree(paths.cache_root() / "library")
    assert setup.restore_library(wt, setup.manifest_key(wt)) is None


def test_setup_prefers_a_built_library_when_none_is_exact(tmp_path: Path) -> None:
    base = tmp_path / "library" / "pkgs"
    built, newer = base / "a", base / "b"
    for p in (built, newer):
        p.mkdir(parents=True)
    setup.built_marker(built).write_text("then\n")
    assert setup.library_candidates(tmp_path, "pkgs", "c") == [built, newer]
    assert setup.library_candidates(tmp_path, "pkgs", "b") == [newer]


def test_a_bake_is_cached_by_its_inputs(monkeypatch, wt) -> None:
    baker = wt.project / "Assets" / "Champions" / "Editor" / "Effects" / "EffectBaker.cs"
    write(baker, "class EffectBaker {}\n")
    run(monkeypatch, "build", bakes=True)
    shutil.rmtree(setup.baked_dir(wt))
    assert setup.restore_baked(wt)
    assert (setup.baked_dir(wt) / "Effects" / "Resources" / "Effects.asset").read_text() == "baked\n"
    assert not setup.restore_baked(wt)  # already the cached bake
    write(baker, "class EffectBaker { }\n")
    shutil.rmtree(setup.baked_dir(wt))
    assert not setup.restore_baked(wt)  # new inputs: nothing cached for them


def test_a_later_bake_with_more_files_replaces_the_cached_one(monkeypatch, wt) -> None:
    baker = wt.project / "Assets" / "Champions" / "Editor" / "Effects" / "EffectBaker.cs"
    write(baker, "class EffectBaker {}\n")
    run(monkeypatch, "build", bakes=True)
    entry = paths.cache_root() / "baked" / setup.baked_key(wt)
    assert not (entry / "Monsters").exists()
    # A monster baked after the build (same inputs): the cache's entry must not keep it out.
    write(setup.baked_dir(wt) / "Monsters" / "skeleton.asset", "monster\n")
    assert setup.save_baked(wt) == entry
    assert (entry / "Monsters" / "skeleton.asset").read_text() == "monster\n"
    assert setup.save_baked(wt) is None  # nothing new: kept


def test_check_variants_builds_clean_and_reports_the_count(monkeypatch, wt, capsys) -> None:
    code, ran = run(monkeypatch, "check", "--variants", variants=227)
    out = capsys.readouterr().out
    assert code == 0
    assert builds(ran) == ["BuildPlayer -cleanBuildCache"]
    assert "227 compiled shader variants, budget 1000: ok" in out
    assert "Hidden/Universal/CoreBlit" in out
    code, ran = run(monkeypatch, "check", "--variants", variants=1001)
    assert code == 1
    assert "1001 compiled shader variants, budget 1000: OVER BUDGET" in capsys.readouterr().out


def test_play_needs_a_build_and_passes_unknown_flags_to_the_player(monkeypatch, wt) -> None:
    run(monkeypatch, "build")
    calls: list[list[str]] = []
    monkeypatch.setattr(cli.subprocess, "run", lambda cmd, **kw: calls.append(cmd) or type("R", (), {"returncode": 0}))
    with pytest.raises(SystemExit) as no_build:
        cli.main(["play", "--backend", "il2cpp", "--headless", "--seconds", "5"])
    assert no_build.value.code == 2 and calls == []
    with pytest.raises(SystemExit) as played:
        cli.main(["play", "--headless", "--seconds", "5", "--fixed-time"])
    assert played.value.code == 0
    exe, *args = calls[0]
    assert exe.endswith(setup.APP_EXE)
    assert args[:2] == ["-batchmode", "-force-metal"] and args[-4:] == ["--seconds", "5", "--fixed-time", "--mute"]
    assert "--headless" not in args  # Unity's player would draw with the Null device


def test_play_makes_the_players_paths_absolute(monkeypatch, wt, tmp_path) -> None:
    run(monkeypatch, "build")
    calls: list[list[str]] = []
    monkeypatch.setattr(cli.subprocess, "run", lambda cmd, **kw: calls.append(cmd) or type("R", (), {"returncode": 0}))
    monkeypatch.chdir(tmp_path)
    with pytest.raises(SystemExit):
        cli.main(["play", "--headless", "--file", "songs/a.ogg", "--seconds", "20"])
    assert calls[0][-5:] == ["--file", str((tmp_path / "songs/a.ogg").resolve()), "--seconds", "20", "--mute"]
    assert cli.absolute_file_flags(["--app", "Music"], tmp_path) == ["--app", "Music"]
    assert cli.absolute_file_flags(["--gallery", "--bench-out", "b.json"], tmp_path) == [
        "--gallery", "--bench-out", str((tmp_path / "b.json").resolve())]
    # --replay takes a comma-separated list of session folders; --record one folder.
    assert cli.absolute_file_flags(["--replay", "s/a,s/b", "--record", "r"], tmp_path) == [
        "--replay", f"{(tmp_path / 's/a').resolve()},{(tmp_path / 's/b').resolve()}", "--record", str((tmp_path / "r").resolve())]


def test_windowed_play_is_silent_unless_audio_requested(monkeypatch, wt) -> None:
    run(monkeypatch, "build")
    calls: list[list[str]] = []
    monkeypatch.setattr(cli.subprocess, "run", lambda cmd, **kw: calls.append(cmd) or type("R", (), {"returncode": 0}))
    for args in (["play", "--windowed"], ["play", "--windowed", "--audio"]):
        with pytest.raises(SystemExit) as played:
            cli.main(args)
        assert played.value.code == 0
    assert calls[0][-1] == "--mute"
    assert "--mute" not in calls[1]
    assert "--audio" not in calls[1]
