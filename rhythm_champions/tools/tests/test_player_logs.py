"""Launch real subprocesses through the standalone app launcher, without Unity or timing assumptions."""
import json
import os
import plistlib
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from champions_tools import cli, paths, player_logs


def test_checkout_and_launch_isolation_survives_checkout_removal(tmp_path):
    first = tmp_path / "Work Tree"
    second = tmp_path / "work tree"
    first.mkdir()
    second.mkdir(exist_ok=True)  # distinct on case-sensitive volumes, aliases on normal APFS
    ids = [player_logs.checkout_id(first), player_logs.checkout_id(tmp_path / "other")]
    assert ids[0] != ids[1]
    alias = tmp_path / "alias"
    alias.symlink_to(first, target_is_directory=True)
    assert player_logs.checkout_id(alias) == ids[0]
    with ThreadPoolExecutor() as pool:
        runs = list(pool.map(lambda i: player_logs.allocate(i, []), ids * 4))
    assert len(set(runs)) == 8
    for run in runs:
        (run / "engine.log").write_text("startup\nshutdown\n")
        player_logs.complete(run, 0)
    first.rmdir()
    assert all((run / "engine.log").read_text() == "startup\nshutdown\n" for run in runs)


def test_pruning_never_deletes_active_or_interrupted_runs(tmp_path):
    folder = tmp_path / "runs"
    folder.mkdir()
    for i in range(105):
        run = folder / str(i)
        run.mkdir()
        marker = run / "complete.json"
        marker.write_text("{}")
        os.utime(marker, (100 + i, 100 + i))
    active = folder / "active"
    active.mkdir()
    (active / "engine.log").write_text("still running")
    # Neither an active run nor a crash can be inferred dead from age/PID.
    player_logs.prune(folder, now=1000)
    assert len(list(folder.iterdir())) == 101
    player_logs.prune(folder, now=40 * 86400)
    assert list(folder.iterdir()) == [active]


def test_launch_kinds_and_explicit_stdout_paths(tmp_path):
    assert [player_logs.kind(f) for f in ([], ["-batchmode"], ["--replay"], ["--capture-stills"])] == [
        "play", "test", "replay", "capture"]
    assert player_logs.explicit_log(["-logFile", "-"])
    assert cli.absolute_file_flags(["-logFile", "-"], tmp_path) == ["-logFile", "-"]
    assert cli.absolute_file_flags(["-logFile", "a b.log"], tmp_path) == ["-logFile", str(tmp_path / "a b.log")]


def test_packaged_app_startup_shutdown_override_and_repeated_launches(tmp_path, monkeypatch):
    app = tmp_path / "My Game.app"
    macos = app / "Contents/MacOS"
    macos.mkdir(parents=True)
    plist = app / "Contents/Info.plist"
    plist.write_bytes(plistlib.dumps({"CFBundleExecutable": "Native Game"}))
    native = macos / "Native Game"
    native.write_text(f'''#!{sys.executable}
import pathlib, sys
flags = sys.argv[1:]
log = flags[flags.index('-logFile') + 1]
if log == '-':
    print('startup; shutdown')
else:
    pathlib.Path(log).write_text('startup; shutdown')
''')
    native.chmod(0o755)
    original_run = subprocess.run
    signatures = []
    monkeypatch.setattr(player_logs.subprocess, "run", lambda cmd, **kw: signatures.append(cmd))
    with ThreadPoolExecutor() as pool:
        list(pool.map(lambda _: player_logs.install(app, tmp_path / "checkout"), range(3)))
    assert len(signatures) == 1  # unchanged install retains signature
    monkeypatch.setattr(player_logs.subprocess, "run", original_run)
    assert plistlib.loads(plist.read_bytes())["CFBundleExecutable"] == "champions-launch"
    script = app / "Contents/Resources/champions-player-logs.py"
    with ThreadPoolExecutor() as pool:
        outputs = list(pool.map(lambda _: subprocess.run([sys.executable, str(script)],
                            check=True, capture_output=True, text=True).stdout, range(3)))
    logs = [Path(out.strip().removeprefix("play: engine log ")) for out in outputs]
    assert len(set(logs)) == 3
    assert all(log.read_text() == 'startup; shutdown' for log in logs)
    assert all(json.loads((log.parent / "complete.json").read_text())["exit_code"] == 0 for log in logs)
    before = set(player_logs.log_root().glob("*/*/*/run.json"))
    stdout = subprocess.run([sys.executable, str(script), "-logFile", "-"], check=True,
                            capture_output=True, text=True)
    assert stdout.stdout == 'startup; shutdown\n'
    external = tmp_path / "explicit log.txt"
    subprocess.run([sys.executable, str(script), "-logFile", str(external)], check=True)
    assert external.read_text() == 'startup; shutdown'
    assert set(player_logs.log_root().glob("*/*/*/run.json")) == before


def test_headless_alias_and_explicit_destination(tmp_path, monkeypatch):
    wt = paths.Worktree(tmp_path / "checkout")
    wt.root.mkdir()
    calls = []

    def run(cmd, **kw):
        calls.append(cmd)
        dest = cmd[cmd.index("-logFile") + 1]
        Path(dest).write_text("Replay: done\n")
        return type("Result", (), {"returncode": 0})()

    monkeypatch.setattr(cli.subprocess, "run", run)
    app = wt.project / "Builds/Game.app"
    cli.headless(wt, app, ["--replay", "session"])
    first = (wt.logs / "player.log").resolve()
    cli.headless(wt, app, ["--replay", "session"])
    second = (wt.logs / "player.log").resolve()
    assert first != second
    assert first.read_text() == second.read_text() == "Replay: done\n"
    external = tmp_path / "external.log"
    cli.headless(wt, app, ["-logFile", str(external)])
    assert calls[-1].count("-logFile") == 1
    assert (wt.logs / "player.log").resolve() == second


def test_new_launch_prunes_expired_removed_checkout_but_preserves_unfinished():
    old = player_logs.allocate("removed-checkout", [])
    unfinished = player_logs.allocate("removed-checkout", [])
    (old / "complete.json").write_text('{}')
    os.utime(old / "complete.json", (1, 1))
    player_logs.allocate("new-checkout", [])
    assert not old.exists()
    assert unfinished.exists()
