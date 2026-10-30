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
    px = tmp_path / "px" / "Assets" / "Skeleton"
    (px / "skeleton_png").mkdir(parents=True)
    (px / "Skeleton.prefab").write_text("m_Sprite: {guid: " + "1" * 32 + "}\n")
    (px / "Skeleton.prefab.meta").write_text(f"fileFormatVersion: 2\nguid: {'2' * 32}\n")
    (px / "skeleton_png" / "skeleton-run_0.png").write_bytes(b"\x89PNG\r\n\x1a\nframe")
    (px / "skeleton_png" / "skeleton-run_0.png.meta").write_text(
        f"fileFormatVersion: 2\nguid: {'1' * 32}\n")
    (tmp_path / "px" / "Library").mkdir()  # the asset project's own folders aren't sources
    (tmp_path / "px" / "Library" / "cache.bin").write_bytes(b"\x00library")
    return {"synty": str(synty), "unity_assets": str(tmp_path / "ua"),
            "pixel_assets": str(tmp_path / "px")}


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
    assert n == 16  # 4 package assets, 2 + 2 asset-folder files, each with its .meta
    data = json.loads(record.read_text())
    assert all(f["sha256"] and f["sha256Lf"] for f in data["files"])
    mat = next(f for f in data["files"] if f["path"] == "Assets/Pack/Materials/Thing.mat")
    assert mat["sha256Lf"] != mat["sha256"]  # the CRLF material's folded hash
    rec = guard.Record.load(record)
    assert rec is not None and PREFAB in rec.guids and SCRIPT in rec.guids
    assert rec.guids["1" * 32] == "pixel_assets"  # the 2D packs' frames
    assert not any("Library" in f["path"] for f in data["files"])


def test_pixel_assets_entries_sync_with_meta_and_dependencies(roots, tmp_path: Path) -> None:
    prefab = Path(roots["pixel_assets"]) / "Assets/Skeleton/Skeleton.prefab"
    e = {"source": "pixel_assets", "path": "Assets/Skeleton/Skeleton.prefab",
         "sourceSha256": hashlib.sha256(prefab.read_bytes()).hexdigest(), "guid": "2" * 32}
    out = tmp_path / "Vendor"
    sync.copy_items(sync.resolve({"assets": [e]}, sources(roots, tmp_path)), out)
    assert (out / "Skeleton/Skeleton.prefab.meta").exists()
    assert (out / "Skeleton/skeleton_png/skeleton-run_0.png").exists()


def test_a_roots_file_from_before_demo2d_gains_the_pixel_assets_root(
        tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    from demo3d_tools import paths

    monkeypatch.setenv("DEMO3D_CACHE", str(tmp_path))
    (tmp_path / "roots.json").write_text(json.dumps({"synty": "/s", "unity_assets": "/u"}))
    roots = paths.load_roots(create=True)
    assert roots["synty"] == "/s" and roots["pixel_assets"] == paths.default_roots()["pixel_assets"]
    assert json.loads((tmp_path / "roots.json").read_text())["pixel_assets"]


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


def more_assets(tmp_path: Path) -> tuple[str, dict]:
    """A tiny more_assets root (the roster sets' packs) and a forest entry for its prefab."""
    pack = tmp_path / "more" / "Assets" / "Forest Creatures Pack" / "Evil Mushroom"
    pack.mkdir(parents=True)
    (pack / "Mushroom.prefab").write_text("m_Material: {guid: " + "5" * 32 + "}\n")
    (pack / "Mushroom.prefab.meta").write_text(f"fileFormatVersion: 2\nguid: {'4' * 32}\n")
    (pack / "Mushroom.mat").write_text("m_Shader: {fileID: 46}\n")
    (pack / "Mushroom.mat.meta").write_text(f"fileFormatVersion: 2\nguid: {'5' * 32}\n")
    e = {"source": "more_assets", "path": "Assets/Forest Creatures Pack/Evil Mushroom/Mushroom.prefab",
         "sourceSha256": hashlib.sha256((pack / "Mushroom.prefab").read_bytes()).hexdigest(),
         "guid": "4" * 32, "use": "forest: mushroom", "roster": "forest"}
    return str(tmp_path / "more"), e


def test_roster_entries_sync_only_when_their_set_is_asked_for(roots, tmp_path: Path) -> None:
    root, forest = more_assets(tmp_path)
    manifest = {"assets": [entry(roots), forest]}
    # The default sync never touches the set's root, mapped or not.
    paths_default = [i.path for _s, i, _u in sync.resolve(manifest, sources(roots, tmp_path))]
    assert not any("Forest" in p for p in paths_default)
    out = tmp_path / "Vendor"
    sync.copy_items(sync.resolve(manifest, sources({**roots, "more_assets": root}, tmp_path),
                                 rosters=("forest",)), out)
    assert (out / "Forest Creatures Pack/Evil Mushroom/Mushroom.mat.meta").exists()
    assert (out / "Pack/Prefabs/Thing.prefab").exists()


def test_a_roster_set_fails_clearly_where_more_assets_is_not_mapped(roots, tmp_path: Path) -> None:
    _root, forest = more_assets(tmp_path)
    manifest = {"assets": [entry(roots), forest]}
    with pytest.raises(sync.SyncError, match="more_assets root is not mapped"):
        sync.resolve(manifest, sources(roots, tmp_path), rosters=("forest",))
    with pytest.raises(sync.SyncError, match="unknown roster"):
        sync.resolve(manifest, sources(roots, tmp_path), rosters=("swamp",))
    with pytest.raises(sync.SyncError, match="no entries for roster 'heroic'"):
        sync.resolve(manifest, sources(roots, tmp_path), rosters=("heroic",))


def test_more_assets_entries_must_name_a_roster(roots, tmp_path: Path) -> None:
    root, forest = more_assets(tmp_path)
    del forest["roster"]
    with pytest.raises(sync.SyncError, match="needs a roster"):
        sync.resolve({"assets": [forest]}, sources({**roots, "more_assets": root}, tmp_path))
    manifest = tmp_path / "manifest.json"
    manifest.write_text(json.dumps({"version": 1, "assets": []}))
    src = sources({**roots, "more_assets": root}, tmp_path)
    with pytest.raises(sync.SyncError, match="need --roster"):
        sync.manifest_add(manifest, "more_assets", ["Assets/Forest*/*/*.prefab"], "t", src)
    assert sync.manifest_add(manifest, "more_assets", ["Assets/Forest*/*/*.prefab"], "t", src,
                             "forest") == 1
    assert json.loads(manifest.read_text())["assets"][0]["roster"] == "forest"


def test_a_missing_kit_package_is_skipped_but_any_other_missing_package_fails(roots, tmp_path: Path) -> None:
    gone = "synty:POLYGON_Gone_Unity_2021_3_v1_0_0.unitypackage.gz"
    kit = [entry(roots, source=gone, use="kit: desert rocks"), entry(roots, source=gone, use="kit: desert palms")]
    manifest = {"assets": [entry(roots, use="kit: trees"), *kit]}
    absent = sync.absent_kits(manifest, roots)
    assert absent == (gone,)
    paths_synced = [i.path for _s, i, _u in sync.resolve(manifest, sources(roots, tmp_path), skip=absent)]
    assert paths_synced == ["Assets/Pack/Materials/Thing.mat", "Assets/Pack/Prefabs/Thing.prefab",
                            "Assets/Pack/Textures/Thing.png"]
    # A package with a non-kit entry (a roster's characters, an animation) is never skipped.
    manifest["assets"].append(entry(roots, source=gone, use="elites"))
    assert sync.absent_kits(manifest, roots) == ()
    with pytest.raises(sync.SyncError, match="missing source"):
        sync.resolve(manifest, sources(roots, tmp_path), skip=sync.absent_kits(manifest, roots))


def test_the_guard_never_needs_roster_entries_in_the_record(tmp_path: Path) -> None:
    (tmp_path / "demo3d").mkdir()
    (tmp_path / "demo3d" / "manifest.json").write_text(json.dumps({"assets": [
        {"source": "unity_assets", "path": "Assets/A.prefab", "guid": "a" * 32},
        {"source": "more_assets", "path": "Assets/B.prefab", "guid": "b" * 32, "roster": "forest"},
    ]}))
    # A record from a machine without more_assets (Cygnus's) can never hold them.
    assert set(guard.manifest_guids(tmp_path)) == {"a" * 32}
