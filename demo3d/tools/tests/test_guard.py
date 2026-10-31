"""The vendor guard's failure cases, proven on synthetic fixtures in a scratch repository."""

from __future__ import annotations

import hashlib
import json
import os
import struct
import subprocess
import zlib
from pathlib import Path

import pytest

from demo3d_tools import guard, paths, setup

GUID = "0123456789abcdef0123456789abcdef"
VENDOR_TEXT = b"vendor material: secret shader source\r\nline two\r\n"


def png(tag: bool) -> bytes:
    def chunk(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))

    ihdr = struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)
    out = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
    if tag:
        out += chunk(b"tEXt", b"ps-render\x00capture tool")
    out += chunk(b"IDAT", zlib.compress(b"\x00\xff\x00\x00")) + chunk(b"IEND", b"")
    return out


def jpeg(tag: bool) -> bytes:
    body = b"\xff\xd8"
    if tag:
        text = b"ps-render capture tool"
        body += b"\xff\xfe" + struct.pack(">H", len(text) + 2) + text
    return body + b"\xff\xda\x00\x02" + b"\x00" * 16 + b"\xff\xd9"


def git(repo: Path, *args: str, stdin: str | None = None) -> str:
    env = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t",
           "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"}
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True, text=True,
                          input=stdin, env=env).stdout


@pytest.fixture
def repo(tmp_path: Path) -> Path:
    r = tmp_path / "repo"
    r.mkdir()
    git(r, "init", "-q", "-b", "main")
    git(r, "config", "core.autocrlf", "false")
    (r / ".gitattributes").write_text("* text=auto eol=lf\n")
    (r / "demo3d").mkdir()
    (r / "demo3d" / "Code.cs").write_text("class C {}\n")
    (r / "README.md").write_text("hello\n")
    (r / "empty.txt").write_text("")
    git(r, "add", "-A")
    git(r, "commit", "-qm", "base")
    return r


def no_vendor_roots(tmp_path: Path) -> dict[str, str]:
    """Roots that exist nowhere, so a guard run never reads this machine's vendor folders."""
    return {name: str(tmp_path / "no-vendor" / name) for name in ("synty", *paths.ASSET_FOLDERS)}


@pytest.fixture
def record(tmp_path: Path) -> Path:
    path = tmp_path / "sync-record.json"
    (tmp_path / "roots.json").write_text(json.dumps(no_vendor_roots(tmp_path)))  # the hook's cache
    lf = VENDOR_TEXT.replace(b"\r\n", b"\n")
    path.write_text(json.dumps({"version": 1, "files": [
        {"path": "Assets/Vendor/Hovl/Slash.prefab.meta", "origin": "Hovl", "guid": GUID,
         "sha256": hashlib.sha256(b"guid only").hexdigest(),
         "sha256Lf": hashlib.sha256(b"guid only").hexdigest()},
        {"path": "Assets/Vendor/Hovl/Slash.shader", "origin": "Hovl",
         "sha256": hashlib.sha256(VENDOR_TEXT).hexdigest(),
         "sha256Lf": hashlib.sha256(lf).hexdigest()},
        {"path": "Assets/Vendor/Synty/empty.txt", "origin": "Synty",
         "sha256": hashlib.sha256(b"").hexdigest(), "sha256Lf": hashlib.sha256(b"").hexdigest()},
    ]}))
    return path


def add(repo: Path, rel: str, data: bytes, force: bool = True) -> None:
    p = repo / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(data)
    git(repo, "add", *(["-f"] if force else []), rel)


def check_index(repo: Path, record: Path | None) -> guard.Result:
    return guard.run("index", repo, None, None, record or repo / "no-such-record.json")


def failures(res: guard.Result) -> dict[str, str]:
    return {p: r for p, r in res.failures}


def reasons(res: guard.Result, rel: str) -> list[str]:
    return [r for p, r in res.failures if p == rel]


def test_clean_tree_passes_with_an_empty_record(repo: Path, tmp_path: Path) -> None:
    empty = tmp_path / "empty-record.json"
    empty.write_text(json.dumps({"version": 1, "files": []}))
    res = check_index(repo, empty)
    assert res.ok, res.failures
    assert res.checked == 4


@pytest.mark.parametrize("rel", [
    "demo3d/Assets/Vendor/Synty/Knight.prefab",
    "demo3d/Assets/vendor/Synty/Knight.prefab",  # Windows paths are case-insensitive
    "demo3d/Assets/Baked/BoneTextures/goblin.json",
    "demo3d/Assets/Vendor.meta",
    "demo2d/Assets/Vendor/Skeleton/skeleton_png/skeleton-run_3.png",
    "demo2d/Assets/vendor/JMO Assets/CFXR Hit.prefab",
    "demo2d/Assets/Baked/Atlases/skeleton.json",
    "demo2d/Assets/Baked.meta",
    "Demo2D/Assets/Vendor/x.txt",  # macOS paths are case-insensitive too
    "game/Assets/Vendor/Synty/Knight.prefab",
    "game/Assets/Baked/BoneTextures/goblin.json",
    "game/Assets/Baked.meta",
    "Game/Assets/vendor/x.txt",
])
def test_vendor_and_baked_paths_fail(repo: Path, record: Path, rel: str) -> None:
    add(repo, rel, b"text\n")
    assert "inside the vendor or baked folder" in reasons(check_index(repo, record), rel)


@pytest.mark.parametrize("rel,data", [
    ("demo3d/Assets/Art/Goblin_Color.png", png(tag=False)),
    ("demo3d/Assets/Art/Goblin.fbx", b"Kaydara FBX Binary"),
    ("demo3d/Assets/Data/table.json", b"{\"a\": 1}\x00\x01"),  # a NUL byte, whatever the name
    ("demo3d/evidence/td1/still.png", png(tag=False)),  # untagged, even under evidence
    ("demo3d/Assets/Art/render.png", png(tag=True)),  # tagged, but outside the evidence
])
def test_untagged_binaries_under_demo3d_fail(repo: Path, record: Path, rel: str, data: bytes) -> None:
    add(repo, rel, data)
    assert failures(check_index(repo, record))[rel] == "binary file under demo3d/"


@pytest.mark.parametrize("rel,data", [
    ("demo2d/Assets/Art/knight.png", png(tag=False)),
    ("demo2d/Assets/Demo2D/Data/atlas.bytes", b"atlas"),
    ("demo2d/Assets/Demo2D/Data/frames.json", b"[1]\x00"),
    ("demo2d/evidence/px1/still.jpg", jpeg(tag=False)),
    ("demo2d/Assets/Art/render.png", png(tag=True)),
    ("demo2d/evidence-old/still.png", png(tag=True)),  # only the evidence folder itself
])
def test_untagged_binaries_under_demo2d_fail(repo: Path, record: Path, rel: str, data: bytes) -> None:
    add(repo, rel, data)
    assert failures(check_index(repo, record))[rel] == "binary file under demo2d/"


def test_the_games_vendor_files_fail_as_the_demos_do(repo: Path, record: Path) -> None:
    add(repo, "game/Assets/Game/Art/Knight_Color.png", png(tag=False))
    add(repo, "game/Assets/FreeSounds/kenney/hit.ogg", b"OggS\x00\x02")
    add(repo, "game/Assets/Game/Fx/Slash.prefab.meta", f"fileFormatVersion: 2\nguid: {GUID}\n".encode())
    add(repo, "game/Assets/Game/Shaders/Slash.shader", VENDOR_TEXT)
    add(repo, "game/evidence/look/boot/still-001.000.jpg", jpeg(tag=True))
    add(repo, "game/Assets/Game/render.jpg", jpeg(tag=True))  # tagged, but outside the evidence
    fails = failures(check_index(repo, record))
    assert fails["game/Assets/Game/Art/Knight_Color.png"] == "binary file under game/"
    assert fails["game/Assets/FreeSounds/kenney/hit.ogg"] == "binary file under game/"
    assert fails["game/Assets/Game/Fx/Slash.prefab.meta"].startswith(f"GUID {GUID} is a vendor asset")
    assert "is a vendor file (Hovl)" in fails["game/Assets/Game/Shaders/Slash.shader"]
    assert fails["game/Assets/Game/render.jpg"] == "binary file under game/"
    assert "game/evidence/look/boot/still-001.000.jpg" not in fails


def test_missing_record_fails_changes_under_game(repo: Path) -> None:
    git(repo, "rm", "-q", "-r", "--cached", "demo3d")
    add(repo, "game/Assets/Game/Client/Boot.cs", b"class Boot {}\n")
    assert [p for p, _ in check_index(repo, None).failures] == ["(sync record)"]


def test_a_record_missing_a_game_manifest_guid_is_stale(repo: Path, record: Path) -> None:
    other = "b" * 32
    (repo / "game").mkdir()
    (repo / "game" / "manifest.json").write_text(json.dumps({"version": 1, "assets": [
        {"source": "unity_assets", "path": "Assets/Monsters/Bat.prefab", "guid": other},
    ]}))
    fails = check_index(repo, record).failures
    assert [p for p, _ in fails] == ["(sync record)"]
    assert other in fails[0][1] and "game sync" in fails[0][1]


def test_a_render_tagged_in_the_other_demos_evidence_still_needs_its_own_folder(
        repo: Path, record: Path) -> None:
    add(repo, "demo2d/evidence/px1/still.png", png(tag=True))
    add(repo, "demo3d/evidence/px1/still.jpg", jpeg(tag=True))
    assert check_index(repo, record).ok
    add(repo, "demo2dx/still.png", png(tag=False))  # not a demo's folder: not the binary rule
    assert check_index(repo, record).ok


def test_tagged_renders_under_evidence_pass(repo: Path, record: Path) -> None:
    add(repo, "demo3d/evidence/td4/still.png", png(tag=True))
    add(repo, "demo3d/evidence/td4/still.jpg", jpeg(tag=True))
    res = check_index(repo, record)
    assert res.ok, res.failures


def test_untagged_jpeg_under_evidence_fails(repo: Path, record: Path) -> None:
    add(repo, "demo3d/evidence/td4/still.jpg", jpeg(tag=False))
    assert not check_index(repo, record).ok


def test_binaries_outside_demo3d_are_not_the_binary_rule(repo: Path, record: Path) -> None:
    add(repo, "public/images/logo.png", png(tag=False))
    assert check_index(repo, record).ok


def test_binaries_in_the_shared_unity_package_fail(repo: Path, record: Path) -> None:
    """Both demos import unity-packages/, so an edited vendor sprite there is as bad as in a demo."""
    rel = "unity-packages/com.playlistsurvivors.choreography/Runtime/edited.png"
    add(repo, rel, png(tag=True))
    assert reasons(check_index(repo, record), rel) == ["binary file under unity-packages/"]


def test_recorded_guid_fails(repo: Path, record: Path) -> None:
    meta = f"fileFormatVersion: 2\nguid: {GUID}\nPrefabImporter:\n".encode()
    add(repo, "demo3d/Assets/Fx/Slash.prefab.meta", meta)
    reason = failures(check_index(repo, record))["demo3d/Assets/Fx/Slash.prefab.meta"]
    assert reason.startswith(f"GUID {GUID} is a vendor asset (Hovl)")


def test_recorded_guid_and_hash_fail_under_demo2d(repo: Path, record: Path) -> None:
    add(repo, "demo2d/Assets/Demo2D/Hit.prefab.meta", f"fileFormatVersion: 2\nguid: {GUID}\n".encode())
    add(repo, "demo2d/Assets/Demo2D/Shaders/Slash.shader", VENDOR_TEXT)
    fails = failures(check_index(repo, record))
    assert fails["demo2d/Assets/Demo2D/Hit.prefab.meta"].startswith(f"GUID {GUID}")
    assert "is a vendor file (Hovl)" in fails["demo2d/Assets/Demo2D/Shaders/Slash.shader"]


def test_a_reference_to_a_vendor_guid_is_not_vendor_content(repo: Path, record: Path) -> None:
    add(repo, "demo3d/Assets/Demo3D/Scene.unity", f"m_Material: {{guid: {GUID}}}\n".encode())
    assert check_index(repo, record).ok


def test_recorded_sha256_fails_anywhere_even_after_lf_normalization(repo: Path, record: Path) -> None:
    # git stores the CRLF vendor file with LF (eol=lf), so only the folded hash matches.
    add(repo, "docs/copied.shader", VENDOR_TEXT)
    blob = git(repo, "cat-file", "-p", ":docs/copied.shader").encode()
    assert b"\r\n" not in blob
    reason = failures(check_index(repo, record))["docs/copied.shader"]
    assert "is a vendor file (Hovl)" in reason


def test_empty_files_never_match_the_record(repo: Path, record: Path) -> None:
    assert check_index(repo, record).ok  # the base tree has empty.txt; the record has an empty file


@pytest.mark.parametrize("rel,data", [
    ("songs/action/action.mp3", b"ID3\x04\x00rest"),
    ("songs/action/notes.txt", b"ID3\x04\x00 renamed MP3"),  # by content
    ("songs/action/sub/crop.flac", b"fLaC\x00\x00"),
    ("songs/action/click.wav", b"RIFF\x00\x00\x00\x00WAVEfmt "),
    ("public/audio/action.ogg", b"OggS\x00\x02"),
    ("public/audio/Action-crop.m4a", b"\x00\x00\x00\x20ftypM4A "),
])
def test_ncs_audio_fails(repo: Path, record: Path, rel: str, data: bytes) -> None:
    add(repo, rel, data)
    assert failures(check_index(repo, record))[rel] == "audio of the NCS song (never tracked)"


def test_song_map_and_other_songs_pass(repo: Path, record: Path) -> None:
    add(repo, "songs/action/songmap.json", b"{\"beats\": [0.5]}\n")
    add(repo, "public/audio/orpheus-galop.flac", b"fLaC\x00\x00")
    assert check_index(repo, record).ok


def test_missing_record_fails_changes_under_demo3d(repo: Path) -> None:
    res = check_index(repo, None)
    assert not res.ok
    assert res.failures[0][0] == "(sync record)"


def test_missing_record_fails_changes_under_demo2d(repo: Path) -> None:
    git(repo, "rm", "-q", "-r", "--cached", "demo3d")
    add(repo, "demo2d/Assets/Demo2D/A.cs", b"class A {}\n")
    res = check_index(repo, None)
    assert [p for p, _ in res.failures] == ["(sync record)"]


def test_missing_record_only_warns_elsewhere(repo: Path, tmp_path: Path) -> None:
    git(repo, "rm", "-q", "-r", "--cached", "demo3d")
    res = check_index(repo, None)
    assert res.ok
    assert res.warnings and "sync record missing" in res.warnings[0]


def test_missing_record_passes_demo_files_the_remote_master_has(repo: Path) -> None:
    git(repo, "update-ref", "refs/remotes/origin/master", "HEAD")
    res = check_index(repo, None)
    assert res.ok
    assert res.warnings and "sync record missing" in res.warnings[0]
    tree = guard.run("tree", repo, "HEAD", None, repo / "no-such-record.json")
    assert tree.ok


def test_missing_record_fails_demo_changes_the_remote_master_lacks(repo: Path) -> None:
    git(repo, "update-ref", "refs/remotes/origin/master", "HEAD")
    add(repo, "demo3d/Code.cs", b"class C { int x; }\n")
    assert [p for p, _ in check_index(repo, None).failures] == ["(sync record)"]
    git(repo, "commit", "-qm", "change")
    no_record = repo / "no-such-record.json"
    for mode, rev in (("tree", "HEAD"), ("range", "origin/master..HEAD")):
        res = guard.run(mode, repo, rev, None, no_record)
        assert [p for p, _ in res.failures] == ["(sync record)"], mode


def test_missing_record_only_warns_for_the_guards_own_code(repo: Path) -> None:
    git(repo, "update-ref", "refs/remotes/origin/master", "HEAD")
    add(repo, "demo3d/tools/src/demo3d_tools/guard.py", b"# guard\n")
    res = check_index(repo, None)
    assert res.ok
    assert res.warnings and "sync record missing" in res.warnings[0]
    add(repo, "demo3d/tools/x.png", png(tag=False))
    assert list(failures(check_index(repo, None))) == ["demo3d/tools/x.png"]


def test_tree_mode_checks_a_commit(repo: Path, record: Path) -> None:
    add(repo, "demo3d/Assets/Vendor/x.txt", b"x\n")
    git(repo, "commit", "-qm", "leak")
    res = guard.run("tree", repo, "HEAD", None, record)
    assert "demo3d/Assets/Vendor/x.txt" in failures(res)


def push_lines(repo: Path, remote_sha: str = guard.ZERO_SHA) -> list[str]:
    head = git(repo, "rev-parse", "HEAD").strip()
    return [f"refs/heads/main {head} refs/heads/main {remote_sha}"]


def test_pre_push_catches_a_leak_deleted_later_in_the_range(repo: Path, record: Path) -> None:
    base = git(repo, "rev-parse", "HEAD").strip()
    add(repo, "demo3d/Assets/Vendor/x.txt", b"x\n")
    git(repo, "commit", "-qm", "leak")
    git(repo, "rm", "-q", "demo3d/Assets/Vendor/x.txt")
    git(repo, "commit", "-qm", "hide it")
    res = guard.run("pre-push", repo, None, "relay", record, push_lines(repo, base))
    assert "demo3d/Assets/Vendor/x.txt" in failures(res)


def test_pre_push_of_a_new_branch_checks_commits_no_remote_has(repo: Path, record: Path) -> None:
    add(repo, "demo3d/Assets/Fx/a.png", png(tag=False))
    git(repo, "commit", "-qm", "binary")
    res = guard.run("pre-push", repo, None, "relay", record, push_lines(repo))
    assert "demo3d/Assets/Fx/a.png" in failures(res)


def test_pre_push_passes_a_clean_range_and_ignores_deletes(repo: Path, record: Path) -> None:
    base = git(repo, "rev-parse", "HEAD").strip()
    add(repo, "demo3d/More.cs", b"class D {}\n")
    git(repo, "commit", "-qm", "more")
    lines = push_lines(repo, base) + [f"(delete) {guard.ZERO_SHA} refs/heads/old {base}"]
    res = guard.run("pre-push", repo, None, "relay", record, lines)
    assert res.ok, res.failures


def test_pre_push_without_a_record_passes_only_what_the_remote_master_has(repo: Path) -> None:
    base = git(repo, "rev-parse", "HEAD").strip()
    git(repo, "update-ref", "refs/remotes/relay/master", base)
    add(repo, "README.md", b"changed\n")
    git(repo, "commit", "-qm", "docs")
    no_record = repo / "no-such-record.json"
    assert guard.run("pre-push", repo, None, "relay", no_record, push_lines(repo, base)).ok
    assert not guard.run("pre-push", repo, None, "origin", no_record, push_lines(repo, base)).ok
    add(repo, "demo3d/More.cs", b"class D {}\n")
    git(repo, "commit", "-qm", "more")
    res = guard.run("pre-push", repo, None, "relay", no_record, push_lines(repo, base))
    assert [p for p, _ in res.failures] == ["(sync record)"]


@pytest.mark.parametrize("leak", ["demo3d/Assets/Vendor/Knight.prefab",
                                  "demo2d/Assets/Vendor/Skeleton/skeleton.png"])
def test_hook_script_refuses_a_push_with_a_vendor_file(repo: Path, record: Path, tmp_path: Path,
                                                       leak: str) -> None:
    """The real pre-push hook, through git, against a bare remote: the push must not land."""
    tools = Path(__file__).resolve().parents[1]
    remote = tmp_path / "remote.git"
    git(tmp_path, "init", "-q", "--bare", str(remote))
    hooks = repo / "demo3d" / "tools" / "hooks"
    hooks.mkdir(parents=True)
    # The hook runs `uv run --project <root>/demo3d/tools`; point it at the real tools instead.
    script = (tools / "hooks" / "pre-push").read_text().replace(
        'tools="$root/demo3d/tools"', f'tools="{tools.as_posix()}"')
    (hooks / "pre-push").write_text(script)
    setup.install_hook(paths.Worktree(repo))  # into the shared hooks folder, as setup does
    git(repo, "remote", "add", "relay", str(remote))
    add(repo, leak, b"vendor\n")
    git(repo, "commit", "-qm", "leak")
    env = {**os.environ, "DEMO3D_CACHE": str(record.parent)}
    proc = subprocess.run(["git", "push", "relay", "main"], cwd=repo, capture_output=True, check=False,
                          text=True, env=env)
    assert proc.returncode != 0
    assert leak in proc.stderr
    assert git(remote, "branch", "--list").strip() == ""


def write_record(tmp_path: Path, raw: object) -> Path:
    p = tmp_path / "bad-record.json"
    p.write_text(json.dumps(raw))
    return p


@pytest.mark.parametrize("raw", [
    {"version": 2, "files": []},
    {"version": 1, "entries": []},
    {"version": 1, "files": [{"path": "x", "sha256": "ab"}]},  # no sha256Lf
    [],
])
def test_a_malformed_record_fails_everything(repo: Path, tmp_path: Path, raw: object) -> None:
    res = check_index(repo, write_record(tmp_path, raw))
    assert not res.ok
    assert res.failures[0][0] == "(guard)"


def test_an_unreadable_record_fails(repo: Path, tmp_path: Path) -> None:
    p = tmp_path / "torn.json"
    p.write_text('{"version": 1, "files": [')
    assert not check_index(repo, p).ok


def test_a_record_missing_a_manifest_guid_is_stale(repo: Path, record: Path) -> None:
    other = "f" * 32
    (repo / "demo3d" / "manifest.json").write_text(json.dumps({"version": 1, "assets": [
        {"source": "unity_assets", "path": "Assets/Hovl Studio/a.prefab", "guid": GUID},
        {"source": "unity_assets", "path": "Assets/Hovl Studio/b.prefab", "guid": other},
    ]}))
    fails = check_index(repo, record).failures
    assert [p for p, _ in fails] == ["(sync record)"]
    assert other in fails[0][1]


@pytest.mark.parametrize("rel,data", [
    ("public/reactive/action/action.ogg", b"OggS\x00\x02"),
    ("songs/action-remix/song.mp3", b"ID3\x04"),
    ("songs/action/samples.f32", b"\x00\x00\x80\x3f" * 8),  # raw PCM: no header, but NUL bytes
])
def test_more_ncs_audio_paths_fail(repo: Path, record: Path, rel: str, data: bytes) -> None:
    add(repo, rel, data)
    assert rel in failures(check_index(repo, record))


def test_a_record_missing_a_demo2d_manifest_guid_is_stale(repo: Path, record: Path) -> None:
    other = "a" * 32
    (repo / "demo2d").mkdir()
    (repo / "demo2d" / "manifest.json").write_text(json.dumps({"version": 1, "assets": [
        {"source": "pixel_assets", "path": "Assets/Skeleton/Skeleton.prefab", "guid": other},
    ]}))
    fails = check_index(repo, record).failures
    assert [p for p, _ in fails] == ["(sync record)"]
    assert other in fails[0][1] and "demo2d sync" in fails[0][1]


def test_a_render_with_data_after_the_image_fails(repo: Path, record: Path) -> None:
    add(repo, "demo3d/evidence/td4/still.png", png(tag=True) + b"vendor bytes riding along")
    add(repo, "demo3d/evidence/td4/still.jpg", jpeg(tag=True) + b"tail")
    assert set(failures(check_index(repo, record))) == {
        "demo3d/evidence/td4/still.png", "demo3d/evidence/td4/still.jpg"}


def test_an_oversized_render_fails(repo: Path, record: Path) -> None:
    small = png(tag=True)
    body = b"\x00" * guard.MAX_RENDER_BYTES
    filler = struct.pack(">I", len(body)) + b"zzZz" + body + b"\x00\x00\x00\x00"
    big = small[:-12] + filler + small[-12:]  # an ancillary chunk before IEND
    assert guard.png_has_tag(big)
    add(repo, "demo3d/evidence/td4/big.png", big)
    assert "demo3d/evidence/td4/big.png" in failures(check_index(repo, record))


def test_pre_push_refuses_malformed_input(repo: Path, record: Path) -> None:
    res = guard.run("pre-push", repo, None, "relay", record, ["refs/heads/main abc"])
    assert not res.ok and res.failures[0][0] == "(guard)"


def test_pre_push_refuses_a_tag_naming_a_blob(repo: Path, record: Path) -> None:
    blob = git(repo, "rev-parse", "HEAD:README.md").strip()
    res = guard.run("pre-push", repo, None, "relay", record,
                    [f"refs/tags/b {blob} refs/tags/b {guard.ZERO_SHA}"])
    assert not res.ok and "names a blob" in res.failures[0][1]


def test_pre_push_peels_an_annotated_tag(repo: Path, record: Path) -> None:
    add(repo, "demo3d/Assets/Vendor/t.txt", b"t\n")
    git(repo, "commit", "-qm", "leak")
    git(repo, "tag", "-a", "-m", "v", "v1")
    tag = git(repo, "rev-parse", "v1").strip()
    res = guard.run("pre-push", repo, None, "relay", record,
                    [f"refs/tags/v1 {tag} refs/tags/v1 {guard.ZERO_SHA}"])
    assert "demo3d/Assets/Vendor/t.txt" in failures(res)


def test_pre_push_with_an_unknown_remote_sha_checks_unpushed_history(repo: Path, record: Path) -> None:
    add(repo, "demo3d/Assets/Fx/x.fbx", b"FBX")
    git(repo, "commit", "-qm", "binary")
    res = guard.run("pre-push", repo, None, "relay", record, push_lines(repo, "1" * 40))
    assert "demo3d/Assets/Fx/x.fbx" in failures(res)


def test_pre_push_after_a_fetch_checks_only_new_commits(repo: Path, record: Path, tmp_path: Path) -> None:
    remote = tmp_path / "remote.git"
    git(tmp_path, "init", "-q", "--bare", str(remote))
    git(repo, "remote", "add", "relay", str(remote))
    git(repo, "push", "-q", "--no-verify", "relay", "main")
    add(repo, "docs/new.md", b"new\n")
    git(repo, "commit", "-qm", "new")
    res = guard.run("pre-push", repo, None, "relay", record, push_lines(repo))
    assert res.ok, res.failures
    assert res.checked == 2  # the new commit's blob, plus demo3d/Code.cs at the tip


def test_pre_push_catches_a_vendor_file_added_only_in_a_merge(repo: Path, record: Path) -> None:
    base = git(repo, "rev-parse", "HEAD").strip()
    git(repo, "checkout", "-q", "-b", "side")
    add(repo, "side.txt", b"side\n")
    git(repo, "commit", "-qm", "side")
    git(repo, "checkout", "-q", "main")
    git(repo, "merge", "-q", "--no-commit", "--no-ff", "side")
    add(repo, "docs/evil.shader", VENDOR_TEXT)
    git(repo, "commit", "-qm", "evil merge")
    git(repo, "rm", "-q", "docs/evil.shader")
    git(repo, "commit", "-qm", "and hide it")
    res = guard.run("pre-push", repo, None, "relay", record, push_lines(repo, base))
    assert "docs/evil.shader" in failures(res)


def test_pushing_demo_work_to_the_public_origin_passes(repo: Path, record: Path) -> None:
    """Since integration, demo work reaches the public repository through Tollgate."""
    base = git(repo, "rev-parse", "HEAD").strip()
    add(repo, "demo3d/More.cs", b"class D {}\n")
    git(repo, "commit", "-qm", "more")
    assert guard.run("pre-push", repo, None, "origin", record, push_lines(repo, base)).ok


def test_a_record_missing_a_pack_on_disk_is_stale(repo: Path, tmp_path: Path,
                                                   monkeypatch: pytest.MonkeyPatch) -> None:
    """A root added (or a pack copied in) since the last sync fails until the record has it."""
    cache = tmp_path / "cache"
    cache.mkdir()
    pixels = tmp_path / "pixels" / "Assets"
    (pixels / "Skeleton").mkdir(parents=True)
    (pixels / "Skeleton.meta").write_text(f"fileFormatVersion: 2\nguid: {'c' * 32}\n")
    (pixels / "Skeleton" / "run.anim").write_text("%YAML 1.1\nvendor clip\n")
    (pixels / "._Skeleton.meta").write_bytes(b"\x00\x05\x16\x07 AppleDouble")
    (cache / "roots.json").write_text(json.dumps(
        {**no_vendor_roots(tmp_path), "pixel_assets": str(pixels.parent)}))
    (cache / "sync-record.json").write_text(json.dumps({"version": 1, "files": []}))
    monkeypatch.setenv("DEMO3D_CACHE", str(cache))
    fails = guard.run("index", repo, None, None, None).failures
    assert [p for p, _ in fails] == ["(sync record)"]
    assert "1 of pixel_assets's packs ('Skeleton')" in fails[0][1]
    h = hashlib.sha256(b"x").hexdigest()
    (cache / "sync-record.json").write_text(json.dumps({"version": 1, "files": [
        {"path": "Assets/Skeleton.meta", "origin": "pixel_assets", "guid": "C" * 32,
         "sha256": h, "sha256Lf": h}]}))
    assert guard.run("index", repo, None, None, None).ok


def uncovered_cache(repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
                    pixels: bool) -> Path:
    """A machine record lacking demo2d's manifest GUIDs, with or without its pixel_assets root."""
    cache = tmp_path / "cache"
    cache.mkdir()
    roots = no_vendor_roots(tmp_path)
    for name in ("unity_assets", "pixel_assets") if pixels else ("unity_assets",):
        (Path(roots[name]) / "Assets").mkdir(parents=True)
    (cache / "roots.json").write_text(json.dumps(roots))
    (cache / "sync-record.json").write_text(json.dumps({"version": 1, "files": []}))
    monkeypatch.setenv("DEMO3D_CACHE", str(cache))
    add(repo, "demo2d/manifest.json", json.dumps({"version": 1, "assets": [
        {"source": "pixel_assets", "path": "Assets/Skeleton/Skeleton.prefab", "guid": "a" * 32},
    ]}).encode())
    add(repo, "demo2d/Code.cs", b"class E {}\n")
    git(repo, "commit", "-qm", "demo2d")
    git(repo, "update-ref", "refs/remotes/origin/master", "HEAD")
    return cache


def test_a_record_without_a_demos_missing_root_passes_its_unchanged_files(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """The Mac's record covers the 3D packs, but the 2D packs live on Cygnus."""
    uncovered_cache(repo, tmp_path, monkeypatch, pixels=False)
    for mode, rev in (("index", None), ("tree", "HEAD")):
        res = guard.run(mode, repo, rev, None, None)
        assert res.ok, (mode, res.failures)
        assert any("lacks demo2d's packs (pixel_assets missing here)" in w for w in res.warnings)


def test_a_record_without_a_demos_missing_root_fails_its_changes(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    uncovered_cache(repo, tmp_path, monkeypatch, pixels=False)
    add(repo, "demo2d/More.cs", b"class F {}\n")
    fails = guard.run("index", repo, None, None, None).failures
    assert [p for p, _ in fails] == ["(sync record)"] and "demo2d/More.cs" in fails[0][1]
    add(repo, "demo2d/Assets/Fx/x.png", png(tag=False))
    git(repo, "commit", "-qm", "change")
    res = guard.run("range", repo, "origin/master..HEAD", None, None)
    assert set(failures(res)) == {"(sync record)", "demo2d/Assets/Fx/x.png"}
    add(repo, "demo3d/Change.cs", b"class G {}\n")  # demo3d is still covered by the record
    git(repo, "commit", "-qm", "demo3d")
    res = guard.run("range", repo, "HEAD~1..HEAD", None, None)
    assert res.ok, res.failures


def test_a_record_lacking_a_demo_whose_roots_are_here_is_still_stale(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    uncovered_cache(repo, tmp_path, monkeypatch, pixels=True)
    fails = guard.run("index", repo, None, None, None).failures
    assert [p for p, _ in fails] == ["(sync record)"] and "run `demo2d sync`" in fails[0][1]


def test_derived_files_are_recorded_and_caught_elsewhere(repo: Path, tmp_path: Path,
                                                         monkeypatch: pytest.MonkeyPatch) -> None:
    from demo3d_tools import derived

    cache = tmp_path / "cache"
    cache.mkdir()
    (cache / "sync-record.json").write_text(json.dumps({"version": 1, "files": []}))
    (cache / "roots.json").write_text(json.dumps(
        {**no_vendor_roots(tmp_path), "song": str(tmp_path / "song.mp3")}))
    (tmp_path / "song.mp3").write_bytes(b"ID3 the NCS song")
    monkeypatch.setenv("DEMO3D_CACHE", str(cache))
    baked = repo / "demo3d" / "Assets" / "Baked" / "Scenes"
    baked.mkdir(parents=True)
    (baked / "Arena.unity").write_text("%YAML 1.1\ngenerated scene from vendor prefabs\n")
    (baked / "Arena.unity.meta").write_text(f"fileFormatVersion: 2\nguid: {'e' * 32}\n")
    (baked / "readme-copy.md").write_bytes(b"hello\r\n")  # a copy of the tracked README.md
    assert len(derived.refresh(repo, cache / "derived-record.json")) == 3  # the copy isn't derived
    add(repo, "docs/arena-copy.unity", (baked / "Arena.unity").read_bytes())
    add(repo, "demo3d/Assets/Demo3D/Arena.unity.meta", (baked / "Arena.unity.meta").read_bytes())
    add(repo, "notes/song.bin", b"ID3 the NCS song")
    fails = failures(guard.run("index", repo, None, None, None))
    assert "derived: demo3d/Assets/Baked/Scenes/Arena.unity" in fails["docs/arena-copy.unity"]
    meta_reasons = [r for p, r in guard.run("index", repo, None, None, None).failures
                    if p == "demo3d/Assets/Demo3D/Arena.unity.meta"]
    assert any(r.startswith("GUID eeee") for r in meta_reasons)
    assert "song source" in fails["notes/song.bin"]
    derived.refresh(repo, cache / "derived-record.json")
    assert len(json.loads((cache / "derived-record.json").read_text())["files"]) == 3  # no duplicates


def test_demo2d_derived_files_are_recorded_and_caught_elsewhere(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from demo3d_tools import derived

    cache = tmp_path / "cache"
    cache.mkdir()
    (cache / "sync-record.json").write_text(json.dumps({"version": 1, "files": []}))
    (cache / "roots.json").write_text(json.dumps(
        {**no_vendor_roots(tmp_path), "song": str(tmp_path / "no-song.mp3")}))
    monkeypatch.setenv("DEMO3D_CACHE", str(cache))
    baked = repo / "demo2d" / "Assets" / "Baked" / "Atlases"
    baked.mkdir(parents=True)
    (baked / "skeleton.json").write_text('{"frames": ["run_0", "run_1"], "from": "vendor PNGs"}\n')
    (baked / "skeleton.json.meta").write_text(f"fileFormatVersion: 2\nguid: {'d' * 32}\n")
    assert len(derived.refresh(repo, cache / "derived-record.json")) == 2
    add(repo, "docs/frames.json", (baked / "skeleton.json").read_bytes())
    add(repo, "demo2d/Assets/Demo2D/Data/atlas.json.meta", (baked / "skeleton.json.meta").read_bytes())
    fails = failures(guard.run("index", repo, None, None, None))
    assert "derived: demo2d/Assets/Baked/Atlases/skeleton.json" in fails["docs/frames.json"]
    meta = reasons(guard.run("index", repo, None, None, None), "demo2d/Assets/Demo2D/Data/atlas.json.meta")
    assert any(r.startswith(f"GUID {'d' * 32}") for r in meta)


def test_the_games_derived_files_are_recorded_and_caught_elsewhere(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from demo3d_tools import derived

    cache = derived_cache(tmp_path, monkeypatch)
    baked = repo / "game" / "Assets" / "Baked" / "Crowd"
    baked.mkdir(parents=True)
    (baked / "bat.bones.asset").write_text("%YAML 1.1\nbones baked from the vendor bat\n")
    (baked / "bat.bones.asset.meta").write_text(f"fileFormatVersion: 2\nguid: {'c' * 32}\n")
    assert len(derived.refresh(repo, cache / "derived-record.json")) == 2
    add(repo, "docs/bat.asset", (baked / "bat.bones.asset").read_bytes())
    add(repo, "game/Assets/Game/Data/bat.asset.meta", (baked / "bat.bones.asset.meta").read_bytes())
    fails = failures(guard.run("index", repo, None, None, None))
    assert "derived: game/Assets/Baked/Crowd/bat.bones.asset" in fails["docs/bat.asset"]
    meta = reasons(guard.run("index", repo, None, None, None), "game/Assets/Game/Data/bat.asset.meta")
    assert any(r.startswith(f"GUID {'c' * 32}") for r in meta)


def derived_cache(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> Path:
    cache = tmp_path / "cache"
    cache.mkdir()
    (cache / "sync-record.json").write_text(json.dumps({"version": 1, "files": []}))
    (cache / "roots.json").write_text(json.dumps(
        {**no_vendor_roots(tmp_path), "song": str(tmp_path / "no-song.mp3")}))
    monkeypatch.setenv("DEMO3D_CACHE", str(cache))
    return cache


# Unity re-serializes demo2d's binary .anim clips as YAML on import: new bytes, no GUID.
REWRITTEN_CLIP = b"%YAML 1.1\nAnimationClip:\n  m_Name: death\n"


def test_vendor_files_unity_rewrote_are_caught_elsewhere(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    derived_cache(tmp_path, monkeypatch)
    clips = repo / "demo2d" / "Assets" / "Vendor" / "Skeleton" / "skeleton"
    clips.mkdir(parents=True)
    (clips / "death.anim").write_bytes(REWRITTEN_CLIP)
    add(repo, "demo2d/Assets/Demo2D/Art/death.anim", REWRITTEN_CLIP)
    fails = failures(guard.run("index", repo, None, None, None))
    assert "derived: demo2d/Assets/Vendor/Skeleton/skeleton/death.anim" in fails["demo2d/Assets/Demo2D/Art/death.anim"]


def test_another_worktrees_rewritten_vendor_files_are_caught(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Unity rewrote a clip in a worktree whose guard never ran; it is copied into this one."""
    derived_cache(tmp_path, monkeypatch)
    other = tmp_path / "other"
    git(repo, "worktree", "add", "-q", "--detach", str(other))
    clips = other / "demo2d" / "Assets" / "Vendor" / "Skeleton"
    clips.mkdir(parents=True)
    (clips / "death.anim").write_bytes(REWRITTEN_CLIP)
    add(repo, "demo2d/Assets/Demo2D/Art/death.anim", REWRITTEN_CLIP)
    assert "derived: demo2d/Assets/Vendor/Skeleton/death.anim (worktree other)" in failures(
        guard.run("index", repo, None, None, None))["demo2d/Assets/Demo2D/Art/death.anim"]
    # Removed since, the worktree's files stay in the record.
    git(repo, "worktree", "remove", "--force", str(other))
    assert "demo2d/Assets/Demo2D/Art/death.anim" in failures(guard.run("index", repo, None, None, None))


def test_derived_files_are_caught_when_a_concurrent_check_replaced_the_record(
        repo: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Two checks refresh at once: the last to write wins, and its record lacks the other's files."""
    from demo3d_tools import derived

    derived_cache(tmp_path, monkeypatch)
    monkeypatch.setattr(derived, "_write_json", lambda path, data: None)  # the other's write won
    baked = repo / "demo2d" / "Assets" / "Baked"
    baked.mkdir(parents=True)
    (baked / "atlas.json").write_text('{"frames": "from vendor PNGs"}\n')
    add(repo, "docs/atlas.json", (baked / "atlas.json").read_bytes())
    assert "docs/atlas.json" in failures(guard.run("index", repo, None, None, None))


def test_launcher_without_tools_refuses_demo_changes_only(repo: Path, tmp_path: Path) -> None:
    """A checkout without demo3d/tools (master) refuses pushes touching the demo's paths."""
    tools = Path(__file__).resolve().parents[1]
    remote = tmp_path / "remote.git"
    git(tmp_path, "init", "-q", "--bare", str(remote))
    git(repo, "remote", "add", "relay", str(remote))
    common = Path(git(repo, "rev-parse", "--path-format=absolute", "--git-common-dir").strip())
    (common / "hooks").mkdir(exist_ok=True)
    (common / "hooks" / "pre-push").write_bytes((tools / "hooks" / "pre-push").read_bytes())
    (common / "hooks" / "pre-push").chmod(0o755)  # POSIX Git ignores non-executable hooks
    git(repo, "push", "-q", "--no-verify", "relay", "main")
    add(repo, "docs/notes.md", b"fine\n")
    git(repo, "commit", "-qm", "docs")
    ok = subprocess.run(["git", "push", "relay", "main"], cwd=repo, capture_output=True,
                        text=True, check=False)
    assert ok.returncode == 0, ok.stderr
    for rel in ("demo3d/Assets/Demo3D/A.cs", "demo2d/Assets/Demo2D/A.cs"):
        before = git(repo, "rev-parse", "HEAD").strip()
        add(repo, rel, b"class A {}\n")
        git(repo, "commit", "-qm", "demo")
        bad = subprocess.run(["git", "push", "relay", "main"], cwd=repo, capture_output=True,
                             text=True, check=False)
        assert bad.returncode != 0, rel
        assert "no demo3d/tools" in bad.stderr
        git(repo, "reset", "-q", "--hard", before)


def test_launcher_without_tools_runs_the_guard_from_the_pushed_tip(repo: Path, record: Path,
                                                                   tmp_path: Path) -> None:
    """A checkout without demo3d/tools (Tollgate's, pushing remote master) runs the tip's guard."""
    import shutil

    tools = Path(__file__).resolve().parents[1]
    remote = tmp_path / "remote.git"
    git(tmp_path, "init", "-q", "--bare", str(remote))
    git(repo, "remote", "add", "origin", str(remote))
    common = Path(git(repo, "rev-parse", "--path-format=absolute", "--git-common-dir").strip())
    (common / "hooks").mkdir(exist_ok=True)
    (common / "hooks" / "pre-push").write_bytes((tools / "hooks" / "pre-push").read_bytes())
    (common / "hooks" / "pre-push").chmod(0o755)
    git(repo, "push", "-q", "--no-verify", "origin", "main")
    # The pushed branch carries the real tools; the checkout stays on main, which has none.
    git(repo, "checkout", "-q", "-b", "integrated")
    for rel in ("pyproject.toml", "uv.lock", "src", "hooks"):
        src = tools / rel
        dst = repo / "demo3d" / "tools" / rel
        if src.is_dir():
            shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__", "*.egg-info"))
        else:
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
    git(repo, "add", "demo3d/tools")
    git(repo, "commit", "-qm", "integrate the demo")
    git(repo, "checkout", "-q", "main")
    assert not (repo / "demo3d" / "tools").exists()
    env = {**os.environ, "DEMO3D_CACHE": str(record.parent)}
    ok = subprocess.run(["git", "push", "origin", "integrated:main"], cwd=repo,
                        capture_output=True, text=True, check=False, env=env)
    assert ok.returncode == 0, ok.stderr
    git(repo, "checkout", "-q", "integrated")
    add(repo, "demo3d/Assets/Vendor/Knight.prefab", b"vendor\n")
    git(repo, "commit", "-qm", "leak")
    git(repo, "checkout", "-q", "main")
    bad = subprocess.run(["git", "push", "origin", "integrated:main"], cwd=repo,
                         capture_output=True, text=True, check=False, env=env)
    assert bad.returncode != 0
    assert "demo3d/Assets/Vendor/Knight.prefab" in bad.stderr


def test_setup_refuses_a_hooks_path_that_would_skip_the_guard(repo: Path) -> None:
    hooks = repo / "demo3d" / "tools" / "hooks"
    hooks.mkdir(parents=True)
    (hooks / "pre-push").write_text("#!/bin/sh\n")
    git(repo, "config", "core.hooksPath", "elsewhere")
    with pytest.raises(setup.SetupError):
        setup.install_hook(paths.Worktree(repo))
