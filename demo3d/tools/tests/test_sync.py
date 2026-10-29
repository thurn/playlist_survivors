"""The sync tool on synthetic sources: a tiny .unitypackage.gz and a tiny asset project."""

from __future__ import annotations

import gzip
import hashlib
import io
import json
import tarfile
from pathlib import Path

import pytest

from demo3d_tools import guard, sync

PREFAB, MAT, TEX, SCRIPT = ("a" * 32, "b" * 32, "c" * 32, "d" * 32)


def make_package(path: Path) -> None:
    entries = {
        PREFAB: ("Assets/Pack/Prefabs/Thing.prefab",
                 f"m_Materials:\n- {{fileID: 2100000, guid: {MAT}, type: 2}}\n"
                 f"m_Script: {{fileID: 11500000, guid: {SCRIPT}, type: 3}}\n".encode()),
        MAT: ("Assets/Pack/Materials/Thing.mat", f"m_Texture: {{guid: {TEX}}}\r\n".encode()),
        TEX: ("Assets/Pack/Textures/Thing.png", b"\x89PNG\r\n\x1a\nfake"),
        SCRIPT: ("Assets/Pack/Scripts/Thing.cs", b"class Thing {}\n"),
    }
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as tar:
        def add(name: str, data: bytes) -> None:
            info = tarfile.TarInfo(name)
            info.size = len(data)
            tar.addfile(info, io.BytesIO(data))
        for guid, (pathname, data) in entries.items():
            add(f"{guid}/pathname", pathname.encode() + b"\n00")
            add(f"{guid}/asset", data)
            add(f"{guid}/asset.meta", f"fileFormatVersion: 2\nguid: {guid}\n".encode())
    path.write_bytes(gzip.compress(buf.getvalue()))


@pytest.fixture
def roots(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> dict[str, str]:
    monkeypatch.setenv("DEMO3D_CACHE", str(tmp_path / "cache"))
    synty = tmp_path / "synty"
    synty.mkdir()
    make_package(synty / "POLYGON_Test_Unity_2021_3_v1_0_0.unitypackage.gz")
    ua = tmp_path / "ua" / "Assets" / "Hovl Studio" / "Fx"
    ua.mkdir(parents=True)
    (ua / "Slash.prefab").write_text("m_Shader: {guid: " + "e" * 32 + "}\n")
    (ua / "Slash.prefab.meta").write_text(f"fileFormatVersion: 2\nguid: {'f' * 32}\n")
    (ua / "Glow.shader").write_text("Shader \"Glow\" {}\n")
    (ua / "Glow.shader.meta").write_text(f"fileFormatVersion: 2\nguid: {'e' * 32}\n")
    return {"synty": str(synty), "unity_assets": str(tmp_path / "ua")}


def sources(roots: dict[str, str], tmp_path: Path) -> sync.Sources:
    cache = tmp_path / "cache"
    return sync.Sources(roots, cache, sync.HashCache(cache / "h.json"))


def entry(roots: dict[str, str], **over: str) -> dict:
    pkg = Path(roots["synty"]) / "POLYGON_Test_Unity_2021_3_v1_0_0.unitypackage.gz"
    e = {"source": f"synty:{pkg.name}", "path": "Assets/Pack/Prefabs/Thing.prefab",
         "sourceSha256": hashlib.sha256(pkg.read_bytes()).hexdigest(), "guid": PREFAB, "use": "t"}
    e.update(over)
    return e


def test_sync_follows_guid_references_but_not_scripts(roots, tmp_path: Path) -> None:
    out = tmp_path / "Vendor"
    items = sync.resolve({"assets": [entry(roots)]}, sources(roots, tmp_path))
    sync.copy_items(items, out)
    assert (out / "Pack/Prefabs/Thing.prefab").exists()
    assert (out / "Pack/Materials/Thing.mat").exists()
    assert (out / "Pack/Textures/Thing.png.meta").exists()
    assert not (out / "Pack/Scripts/Thing.cs").exists()


def test_sync_refuses_a_source_whose_hash_differs(roots, tmp_path: Path) -> None:
    with pytest.raises(sync.SyncError, match="refusing"):
        sync.resolve({"assets": [entry(roots, sourceSha256="0" * 64)]}, sources(roots, tmp_path))


def test_sync_refuses_a_guid_mismatch(roots, tmp_path: Path) -> None:
    with pytest.raises(sync.SyncError, match="GUID"):
        sync.resolve({"assets": [entry(roots, guid="9" * 32)]}, sources(roots, tmp_path))


def test_unity_assets_entries_copy_files_with_meta_and_dependencies(roots, tmp_path: Path) -> None:
    prefab = Path(roots["unity_assets"]) / "Assets/Hovl Studio/Fx/Slash.prefab"
    e = {"source": "unity_assets", "path": "Assets/Hovl Studio/Fx/Slash.prefab",
         "sourceSha256": hashlib.sha256(prefab.read_bytes()).hexdigest(), "guid": "f" * 32}
    out = tmp_path / "Vendor"
    sync.copy_items(sync.resolve({"assets": [e]}, sources(roots, tmp_path)), out)
    assert (out / "Hovl Studio/Fx/Slash.prefab.meta").exists()
    assert (out / "Hovl Studio/Fx/Glow.shader").exists()


def test_the_record_lists_every_source_file_and_the_guard_accepts_it(roots, tmp_path: Path) -> None:
    record = tmp_path / "cache" / "sync-record.json"
    n = sync.build_record(sources(roots, tmp_path), record)
    assert n == 12  # 4 package assets and 2 project files, each with its .meta
    data = json.loads(record.read_text())
    assert all(f["sha256"] and f["sha256Lf"] for f in data["files"])
    mat = next(f for f in data["files"] if f["path"] == "Assets/Pack/Materials/Thing.mat")
    assert mat["sha256Lf"] != mat["sha256"]  # the CRLF material's folded hash
    rec = guard.Record.load(record)
    assert rec is not None and PREFAB in rec.guids and SCRIPT in rec.guids


def test_manifest_add_fills_guid_and_hashes(roots, tmp_path: Path) -> None:
    manifest = tmp_path / "manifest.json"
    manifest.write_text(json.dumps({"version": 1, "assets": []}))
    src = sources(roots, tmp_path)
    pkg = "synty:POLYGON_Test_Unity_2021_3_v1_0_0.unitypackage.gz"
    assert sync.manifest_add(manifest, pkg, ["Assets/Pack/Prefabs/*.prefab"], "test", src) == 1
    assert sync.manifest_add(manifest, pkg, ["Assets/Pack/Prefabs/*.prefab"], "test", src) == 0
    a = json.loads(manifest.read_text())["assets"][0]
    assert a["guid"] == PREFAB and len(a["sourceSha256"]) == 64
    with pytest.raises(sync.SyncError, match="nothing matches"):
        sync.manifest_add(manifest, pkg, ["Assets/Nope/*"], "test", src)
