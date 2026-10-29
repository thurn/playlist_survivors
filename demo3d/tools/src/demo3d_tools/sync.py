"""The sync tool: fills the gitignored vendor folder from the tracked manifest.

Manifest entries name a source root and a path, never a machine's path (vendor-assets.md,
Curation and sync):

    { "source": "synty:POLYGON_Fantasy_Rivals_Unity_2021_3_v1_3_1.unitypackage.gz",
      "path": "Assets/PolygonFantasyRivals/Models/Characters.fbx",
      "sourceSha256": "...", "guid": "...", "use": "elites" }
    { "source": "unity_assets", "path": "Assets/Hovl Studio/.../Slash.prefab", ... }

- A Synty `.unitypackage.gz` (a gzipped tar of per-GUID folders holding `asset`, `asset.meta`
  and `pathname`) is unpacked once into the per-machine cache, keyed by its SHA-256.
- From `unity_assets`, files are copied with their `.meta`.
- `sourceSha256` is the package's hash for Synty, and the file's own hash for unity_assets. A
  source whose hash differs is refused, so both machines sync from identical files.
- `guid:` references in the copied YAML and `.meta` files are followed within the same root
  (prefab to material to texture or shader). Scripts and scenes are never followed: vendor
  code doesn't enter the project, and a prefab's script becomes a missing script.
- Files land in the vendor folder at their path without the leading `Assets/`.

The sync record lists every file under every source root with its GUID and SHA-256, not just
the synced ones, so the guard knows every vendor file this machine has. `--record-only` builds
it without copying anything (the Mac's guard at integration).
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import re
import shutil
import sys
import tarfile
import time
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

from . import paths

# Bump when resolve, follow or copy_items change what a manifest syncs to: setup keys its
# vendor cache by the manifest and this, so worktrees never reuse a folder synced by old logic.
SYNC_VERSION = "1"

# Unity's built-in resources (default materials, the Standard shader): never in a source.
BUILTIN_GUIDS = frozenset({"0000000000000000e000000000000000", "0000000000000000f000000000000000"})

GUID_REF = re.compile(rb"guid:\s*([0-9a-fA-F]{32})")
META_GUID = re.compile(rb"^guid:\s*([0-9a-fA-F]{32})", re.MULTILINE)
NEVER_FOLLOW = frozenset({".cs", ".dll", ".js", ".asmdef", ".asmref", ".rsp", ".unity", ".pdb"})
YAML_LIKE = frozenset({
    ".prefab", ".mat", ".asset", ".controller", ".overridecontroller", ".mask", ".anim",
    ".shadergraph", ".shadersubgraph", ".vfx", ".playable", ".preset", ".physicmaterial",
    ".terrainlayer", ".lighting", ".spriteatlas", ".mixer", ".flare", ".rendertexture",
    ".cubemap", ".guiskin", ".fontsettings", ".signal",
})
SKIP_NAMES = frozenset({".DS_Store", "Thumbs.db"})


class SyncError(Exception):
    pass


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def lf_hash(data: bytes) -> str | None:
    return hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest() if b"\r\n" in data else None


class HashCache:
    """SHA-256 of large source files, remembered by path, size and mtime."""

    def __init__(self, path: Path):
        self.path = path
        self.data: dict[str, list] = {}
        if path.exists():
            try:
                self.data = json.loads(path.read_text(encoding="utf-8"))
            except ValueError:
                self.data = {}

    def get(self, file: Path) -> str:
        st = file.stat()
        key = str(file.resolve())
        hit = self.data.get(key)
        if hit and hit[0] == st.st_size and hit[1] == st.st_mtime_ns:
            return hit[2]
        h = sha256_file(file)
        self.data[key] = [st.st_size, st.st_mtime_ns, h]
        return h

    def save(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.path.write_text(json.dumps(self.data), encoding="utf-8")


# Sources: each maps GUIDs to files it can provide.


@dataclass
class Item:
    """One vendor asset: where its bytes are, and where it goes in the project."""

    path: str  # "Assets/..." as Unity names it
    guid: str
    asset: Path | None  # None for a folder
    meta: Path | None


@dataclass
class Source:
    name: str  # "synty:<file>" or "unity_assets"
    sha256: str | None  # the package's hash (Synty)
    by_guid: dict[str, Item] = field(default_factory=dict)
    by_path: dict[str, Item] = field(default_factory=dict)

    def add(self, item: Item) -> None:
        self.by_guid[item.guid] = item
        self.by_path[item.path.lower()] = item


def unpack_synty(pkg: Path, sha: str, cache: Path) -> Path:
    """Unpacks a .unitypackage.gz once into cache/packages/<sha>/; returns that folder."""
    out = cache / "packages" / sha[:16]
    if (out / ".complete").exists():
        return out
    tmp = out.with_name(out.name + ".tmp")
    shutil.rmtree(tmp, ignore_errors=True)
    tmp.mkdir(parents=True)
    with gzip.open(pkg, "rb") as gz, tarfile.open(fileobj=gz, mode="r|") as tar:
        for member in tar:
            if not member.isfile():
                continue
            parts = PurePosixPath(member.name).parts
            parts = [p for p in parts if p not in (".", "")]
            if len(parts) != 2 or parts[1] not in ("asset", "asset.meta", "pathname"):
                continue
            guid, leaf = parts
            if not re.fullmatch(r"[0-9a-fA-F]{32}", guid):
                continue
            dest = tmp / guid.lower()
            dest.mkdir(exist_ok=True)
            src = tar.extractfile(member)
            assert src is not None
            with open(dest / leaf, "wb") as f:
                shutil.copyfileobj(src, f)
    (tmp / ".complete").write_text(sha + "\n")
    shutil.rmtree(out, ignore_errors=True)
    tmp.rename(out)
    return out


def load_synty(pkg: Path, sha: str, cache: Path) -> Source:
    folder = unpack_synty(pkg, sha, cache)
    src = Source(f"synty:{pkg.name}", sha)
    for d in folder.iterdir():
        if not d.is_dir():
            continue
        pn = d / "pathname"
        if not pn.exists():
            continue
        path = pn.read_text(encoding="utf-8", errors="replace").splitlines()[0].strip()
        asset = d / "asset"
        meta = d / "asset.meta"
        src.add(Item(path, d.name, asset if asset.exists() else None, meta if meta.exists() else None))
    return src


def load_unity_assets(root: Path) -> Source:
    src = Source("unity_assets", None)
    assets = root / "Assets"
    for dirpath, dirs, files in os.walk(assets):
        dirs[:] = [d for d in dirs if d != "__MACOSX"]
        for f in files:
            if not f.endswith(".meta") or f in SKIP_NAMES or f.startswith("._"):
                continue
            meta = Path(dirpath) / f
            m = META_GUID.search(meta.read_bytes()[:4096])
            if not m:
                continue
            target = meta.with_name(f[: -len(".meta")])
            rel = "Assets/" + target.relative_to(assets).as_posix()
            src.add(Item(rel, m.group(1).decode().lower(),
                         target if target.is_file() else None, meta))
    return src


@dataclass
class Sources:
    roots: dict[str, str]
    cache: Path
    hashes: HashCache
    loaded: dict[str, Source] = field(default_factory=dict)

    def get(self, name: str) -> Source:
        if name in self.loaded:
            return self.loaded[name]
        if name.startswith("synty:"):
            pkg = Path(self.roots["synty"]) / name.split(":", 1)[1]
            if not pkg.exists():
                raise SyncError(f"missing source {name}: {pkg}")
            src = load_synty(pkg, self.hashes.get(pkg), self.cache)
        elif name == "unity_assets":
            root = Path(self.roots["unity_assets"])
            if not (root / "Assets").exists():
                raise SyncError(f"missing source unity_assets: {root}")
            src = load_unity_assets(root)
        else:
            raise SyncError(f"unknown source root in {name!r}")
        self.loaded[name] = src
        return src


def follow(item: Item) -> set[str]:
    """GUIDs an item's asset and meta refer to."""
    refs: set[str] = set()
    for f in (item.asset, item.meta):
        if f is None:
            continue
        ext = PurePosixPath(item.path).suffix.lower()
        if f is item.asset and ext not in YAML_LIKE:
            continue
        data = f.read_bytes()
        if f is item.asset and b"\x00" in data[:8192]:
            continue  # binary-serialized asset
        refs.update(m.group(1).decode().lower() for m in GUID_REF.finditer(data))
    refs.discard(item.guid)
    return refs


def resolve(manifest: dict, sources: Sources,
            unresolved: dict[str, str] | None = None) -> list[tuple[Source, Item, str]]:
    """Every item to sync: the manifest's entries and their dependency closure. References no
    loaded source provides (package assets such as URP's shaders, or something missing) go into
    `unresolved` (GUID -> the first asset referring to it)."""
    chosen: dict[str, tuple[Source, Item, str]] = {}
    queue: list[tuple[Source, Item, str]] = []
    for entry in manifest.get("assets", []):
        src = sources.get(entry["source"])
        if src.sha256 is not None and entry.get("sourceSha256") != src.sha256:
            raise SyncError(f"{entry['source']}: SHA-256 {src.sha256} differs from the "
                            f"manifest's {entry.get('sourceSha256')}; refusing to sync")
        item = src.by_path.get(entry["path"].lower())
        if item is None:
            raise SyncError(f"{entry['source']}: no asset at {entry['path']}")
        if entry.get("guid") and entry["guid"].lower() != item.guid:
            raise SyncError(f"{entry['path']}: GUID {item.guid} differs from the manifest's {entry['guid']}")
        if src.sha256 is None:
            if item.asset is None:
                raise SyncError(f"{entry['path']}: a folder; list files")
            h = sources.hashes.get(item.asset)
            if entry.get("sourceSha256") != h:
                raise SyncError(f"{entry['path']}: SHA-256 {h} differs from the manifest's "
                                f"{entry.get('sourceSha256')}; refusing to sync")
        queue.append((src, item, entry.get("use", "")))
    siblings: dict[str, list[Source]] = {}
    while queue:
        src, item, use = queue.pop()
        if item.guid in chosen:
            continue
        chosen[item.guid] = (src, item, use)
        root = src.name.split(":")[0]
        if root not in siblings:
            siblings[root] = [s for n, s in sources.loaded.items() if n.split(":")[0] == root]
        for ref in follow(item):
            if ref in chosen:
                continue
            found = False
            for s in [src] + [x for x in siblings[root] if x is not src]:
                dep = s.by_guid.get(ref)
                if dep and dep.asset is not None:
                    if PurePosixPath(dep.path).suffix.lower() not in NEVER_FOLLOW:
                        queue.append((s, dep, f"dependency of {item.path}"))
                    found = True
                    break
            if not found and unresolved is not None and ref not in BUILTIN_GUIDS:
                unresolved.setdefault(ref, item.path)
    return sorted(chosen.values(), key=lambda t: t[1].path.lower())


def vendor_rel(item_path: str) -> str:
    p = PurePosixPath(item_path)
    if p.parts and p.parts[0] == "Assets":
        p = PurePosixPath(*p.parts[1:])
    return p.as_posix()


def copy_items(items: list[tuple[Source, Item, str]], out: Path) -> int:
    n = 0
    for _src, item, _use in items:
        rel = vendor_rel(item.path)
        dest = out / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        if item.asset is not None:
            shutil.copy2(item.asset, dest)
            n += 1
        if item.meta is not None:
            shutil.copy2(item.meta, dest.with_name(dest.name + ".meta"))
    return n


# The record.


def record_entries(src: Source, root_label: str) -> list[dict]:
    out = []
    for item in src.by_guid.values():
        for f, suffix in ((item.asset, ""), (item.meta, ".meta")):
            if f is None:
                continue
            data = f.read_bytes()
            e = {"path": item.path + suffix, "origin": root_label, "guid": item.guid,
                 "sha256": hashlib.sha256(data).hexdigest()}
            e["sha256Lf"] = lf_hash(data) or e["sha256"]
            out.append(e)
    return out


def all_sources(roots: dict[str, str]) -> list[str]:
    names = []
    synty = Path(roots.get("synty", ""))
    if synty.is_dir():
        names += [f"synty:{p.name}" for p in sorted(synty.glob("*.unitypackage.gz"))]
    if (Path(roots.get("unity_assets", "")) / "Assets").is_dir():
        names.append("unity_assets")
    return names


def build_record(sources: Sources, record_path: Path) -> int:
    """Writes the record of every file under every source root; returns its entry count."""
    cache_file = sources.cache / "record-index.json"
    index: dict[str, dict] = {}
    if cache_file.exists():
        try:
            index = json.loads(cache_file.read_text(encoding="utf-8"))
        except ValueError:
            index = {}
    entries: list[dict] = []
    fresh: dict[str, dict] = {}
    for name in all_sources(sources.roots):
        if name.startswith("synty:"):
            pkg = Path(sources.roots["synty"]) / name.split(":", 1)[1]
            key = f"{name}:{sources.hashes.get(pkg)}"
        else:
            key = f"{name}:{_tree_stamp(Path(sources.roots['unity_assets']) / 'Assets')}"
        if key in index:
            fresh[key] = index[key]
        else:
            label = name.split(":", 1)[1].split("_Unity_")[0] if name.startswith("synty:") else name
            fresh[key] = {"files": record_entries(sources.get(name), label)}
        entries.extend(fresh[key]["files"])
    cache_file.write_text(json.dumps(fresh), encoding="utf-8")
    record_path.parent.mkdir(parents=True, exist_ok=True)
    tmp = record_path.with_name(f"{record_path.name}.{os.getpid()}.tmp")  # gates run concurrently
    tmp.write_text(json.dumps({"version": 1, "files": entries}), encoding="utf-8")
    os.replace(tmp, record_path)
    return len(entries)


def _tree_stamp(root: Path) -> str:
    h = hashlib.sha256()
    count = 0
    for dirpath, dirs, files in os.walk(root):
        dirs.sort()
        for f in sorted(files):
            st = os.stat(os.path.join(dirpath, f))
            h.update(f"{dirpath}/{f}:{st.st_size}:{st.st_mtime_ns}\n".encode())
            count += 1
    return f"{count}:{h.hexdigest()[:16]}"


def report_unresolved(unresolved: dict[str, str], cache: Path) -> None:
    """Lists references the sources didn't provide, so a broken dependency isn't silent."""
    if not unresolved:
        return
    listing = cache / "sync-unresolved.txt"
    listing.write_text("".join(f"{g}  referenced by {p}\n" for g, p in sorted(unresolved.items())),
                       encoding="utf-8")
    print(f"sync: {len(unresolved)} referenced GUIDs are in no source (package assets such as "
          f"URP's shaders, or missing files); see {listing}", file=sys.stderr)


def sync(manifest: dict, roots: dict[str, str], out: Path, record_path: Path) -> int:
    cache = paths.cache_root()
    sources = Sources(roots, cache, HashCache(cache / "source-hashes.json"))
    try:
        unresolved: dict[str, str] = {}
        items = resolve(manifest, sources, unresolved)
        report_unresolved(unresolved, cache)
        n = copy_items(items, out)
        build_record(sources, record_path)
    finally:
        sources.hashes.save()
    return n


def manifest_add(manifest_path: Path, source: str, patterns: list[str], use: str,
                 sources: Sources) -> int:
    """Adds every asset matching the glob patterns (fnmatch, case-insensitive) to the manifest."""
    import fnmatch

    src = sources.get(source)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assets = {(a["source"], a["path"].lower()): a for a in manifest.get("assets", [])}
    added = 0
    for pattern in patterns:
        pat = pattern.lower()
        hits = sorted((i for p, i in src.by_path.items() if fnmatch.fnmatchcase(p, pat)
                       and i.asset is not None), key=lambda i: i.path)
        if not hits:
            raise SyncError(f"{source}: nothing matches {pattern}")
        for item in hits:
            sha = src.sha256 if src.sha256 is not None else sources.hashes.get(item.asset)
            key = (source, item.path.lower())
            if key not in assets:
                added += 1
            assets[key] = {"source": source, "path": item.path, "sourceSha256": sha,
                           "guid": item.guid, "use": use}
    manifest["assets"] = sorted(assets.values(), key=lambda a: (a["source"], a["path"].lower()))
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
                             encoding="utf-8", newline="\n")
    return added


def find(source: str, pattern: str, sources: Sources) -> list[str]:
    import fnmatch

    src = sources.get(source)
    return sorted(i.path for p, i in src.by_path.items()
                  if fnmatch.fnmatchcase(p, pattern.lower()) and i.asset is not None)


def main(a: argparse.Namespace) -> int:
    wt = paths.worktree()
    manifest = json.loads((wt.project / "manifest.json").read_text(encoding="utf-8"))
    roots = paths.load_roots(create=True)
    cache = paths.cache_root()
    sources = Sources(roots, cache, HashCache(cache / "source-hashes.json"))
    started = time.monotonic()
    try:
        unresolved: dict[str, str] = {}
        items = resolve(manifest, sources, unresolved)
        report_unresolved(unresolved, cache)
        if not a.record_only:
            out = Path(a.out) if a.out else wt.vendor
            n = copy_items(items, out)
            print(f"sync: {n} files ({len(items)} assets) into {out}")
        else:
            print(f"sync: {len(items)} assets resolved (record only)")
        count = build_record(sources, paths.record_path())
        print(f"sync: record of {count} vendor files at {paths.record_path()} "
              f"in {time.monotonic() - started:.1f} s")
    except SyncError as e:
        print(f"sync: {e}", file=sys.stderr)
        return 2
    finally:
        sources.hashes.save()
    return 0
