"""The vendor guard: no vendor file, derived file or NCS audio may leave the machine.

It checks blobs as git stores them, so it sees exactly what a push or a commit would carry.
It covers both demos' Unity projects, demo3d/ and demo2d/, alike. A file fails when it:

- is inside either demo's vendor or baked folders;
- is binary (by extension or a NUL byte) under a demo's folder or the Unity package they share
  (unity-packages/, code only), unless it is a PNG or JPEG under that demo's evidence/ that
  carries the `ps-render` text tag;
- is a .meta whose GUID is in the sync record;
- has a SHA-256 that is in the sync record (raw, or with CRLF folded to LF, since git stores
  text with LF);
- is audio (by extension or content) under songs/action/, or under public/audio/ with a name
  starting `action`.

A missing sync record fails any change under a demo's folder and only warns elsewhere
(vendor-assets.md, The vendor guard). An unreadable or malformed record fails everything, and so
does a stale record: one that lacks a GUID either demo's tracked manifest lists, or a pack in
one of the machine's asset folders (a top-level folder of `unity_assets` or `pixel_assets` whose
`.meta` GUID it lacks, such as a pack copied in, or a root added, since the last sync).

Besides the sync record, the guard reads the derived record: the hashes and GUIDs of every file
in the demos' Assets/Baked/ and Assets/Vendor/ (where Unity may rewrite a vendor file on import)
of every worktree of this clone, now or since the record began, and of the song's source. It
refreshes the record before each check (derived.py). Derived files that are
edited copies with fresh GUIDs and new bytes can't be recognized; the binary rule and the
gitignored folders are the defense there.
"""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
from collections.abc import Iterable, Iterator
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath

from . import paths

PS_RENDER = b"ps-render"
MAX_RENDER_BYTES = 4 << 20  # evidence stills are kept small
RECORD_KEYS = frozenset({"version", "files"})
EMPTY_SHA256 = hashlib.sha256(b"").hexdigest()
ZERO_SHA = "0" * 40

BINARY_EXTENSIONS = frozenset(
    """
    png jpg jpeg gif bmp tga tif tiff psd psb exr hdr dds ktx ktx2 webp ico svgz
    fbx obj blend blend1 dae 3ds max ma mb glb c4d skp abc usd usdz usdc
    wav mp3 ogg oga flac aif aiff m4a aac opus wma mid midi
    mp4 mov avi webm mkv wmv m4v
    ttf otf woff woff2
    zip gz tgz 7z rar tar xz bz2 zst unitypackage
    dll so dylib exe pdb lib a o obj bin bytes res resS resource unity3d bundle
    pdf npy npz pkl sqlite db
    """.split()
)
AUDIO_EXTENSIONS = frozenset(
    "wav mp3 ogg oga flac aif aiff m4a aac opus wma mp4 m4b caf wv ape mka webm".split()
)
IMAGE_TAG_EXTENSIONS = frozenset({"png", "jpg", "jpeg"})
# The folders whose binaries are refused: both demos, and the Unity package they share.
BINARY_FOLDERS = (*paths.PROJECTS, "unity-packages")
META_GUID = re.compile(rb"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)


class RecordError(Exception):
    pass


@dataclass
class Record:
    """The sync record (every vendor file's GUID and SHA-256), plus the derived record."""

    guids: dict[str, str] = field(default_factory=dict)  # guid -> where it came from
    hashes: dict[str, str] = field(default_factory=dict)  # sha256 -> where it came from

    def add_file(self, path: Path, label: str) -> None:
        try:
            raw = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as e:
            raise RecordError(f"{label} at {path} is unreadable: {e}") from e
        if not isinstance(raw, dict) or raw.get("version") != 1:
            raise RecordError(f"{label} at {path}: unknown version {raw.get('version') if isinstance(raw, dict) else raw!r}")
        extra = set(raw) - RECORD_KEYS
        if extra or not isinstance(raw.get("files"), list):
            raise RecordError(f"{label} at {path}: unexpected keys {sorted(extra)} or no file list")
        self.add_entries(raw["files"], f"{label} at {path}")

    def add_entries(self, files: list, label: str) -> None:
        for i, f in enumerate(files):
            if not isinstance(f, dict) or not f.get("sha256") or not f.get("sha256Lf"):
                raise RecordError(f"{label}: entry {i} lacks sha256 or sha256Lf")
            origin = f.get("origin") or f.get("path", "?")
            if f.get("guid"):
                self.guids[f["guid"].lower()] = origin
            for key in ("sha256", "sha256Lf"):
                h = f[key].lower()
                if h != EMPTY_SHA256:
                    self.hashes[h] = origin

    @classmethod
    def load(cls, path: Path, derived: Path | None = None) -> Record | None:
        if not path.exists():
            return None
        rec = cls()
        rec.add_file(path, "sync record")
        if derived is not None and derived.exists():
            rec.add_file(derived, "derived record")
        return rec


def sha256_forms(data: bytes) -> set[str]:
    """The blob's SHA-256, and the same with CRLF folded to LF."""
    forms = {hashlib.sha256(data).hexdigest()}
    if b"\r\n" in data:
        forms.add(hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest())
    forms.discard(EMPTY_SHA256)
    return forms


def png_has_tag(data: bytes) -> bool:
    """True if a PNG has a tEXt, zTXt or iTXt chunk keyed `ps-render` and ends at IEND."""
    if not data.startswith(b"\x89PNG\r\n\x1a\n"):
        return False
    pos = 8
    tagged = False
    while pos + 8 <= len(data):
        length = int.from_bytes(data[pos : pos + 4], "big")
        kind = data[pos + 4 : pos + 8]
        body = data[pos + 8 : pos + 8 + length]
        if kind in (b"tEXt", b"zTXt", b"iTXt") and body.split(b"\x00", 1)[0] == PS_RENDER:
            tagged = True
        pos += 12 + length
        if kind == b"IEND":
            return tagged and pos == len(data)  # nothing may ride along after the image
    return False


def jpeg_has_tag(data: bytes) -> bool:
    """True if a JPEG has a comment (COM) segment starting `ps-render` and ends at EOI."""
    if not data.startswith(b"\xff\xd8") or not data.endswith(b"\xff\xd9"):
        return False
    pos = 2
    while pos + 4 <= len(data) and data[pos] == 0xFF:
        marker = data[pos + 1]
        if marker in (0xD9, 0xDA):  # end of image, start of scan
            break
        length = int.from_bytes(data[pos + 2 : pos + 4], "big")
        if marker == 0xFE and data[pos + 4 : pos + 2 + length].startswith(PS_RENDER):
            return True
        pos += 2 + length
    return False


def has_render_tag(data: bytes) -> bool:
    return len(data) <= MAX_RENDER_BYTES and (png_has_tag(data) or jpeg_has_tag(data))


def looks_like_audio(data: bytes) -> bool:
    head = data[:12]
    return (
        head.startswith((b"ID3", b"OggS", b"fLaC", b"caff", b"wvpk", b"MAC ", b"\x1aE\xdf\xa3"))
        or (head[:4] == b"RIFF" and head[8:12] in (b"WAVE", b"AVI "))
        or (head[:4] == b"FORM" and head[8:12] in (b"AIFF", b"AIFC"))
        or head[4:8] == b"ftyp"
        or (len(head) >= 2 and head[0] == 0xFF and (head[1] & 0xE0) == 0xE0)  # MPEG/ADTS sync
    )


def extension(path: str) -> str:
    return PurePosixPath(path).suffix.lower().lstrip(".")


def in_vendor_or_baked(lower: str) -> bool:
    for demo in paths.PROJECTS:
        for folder in (f"{demo}/assets/vendor", f"{demo}/assets/baked"):
            if lower.startswith(folder + "/") or lower in (folder, folder + ".meta"):
                return True
    return False


def is_song_audio_path(lower: str) -> bool:
    """The song's folder, or any path under songs/ or public/ with a segment `action*`."""
    if lower.startswith("songs/action/"):
        return True
    parts = PurePosixPath(lower).parts
    return bool(parts) and parts[0] in ("songs", "public") and any(
        p.startswith("action") for p in parts[1:]
    )


def demo_of(lower: str) -> str | None:
    """The demo whose folder holds the path (demo3d or demo2d), if any."""
    head = lower.split("/", 1)[0]
    return head if head in paths.PROJECTS and "/" in lower else None


def under_demo(lower: str) -> bool:
    return demo_of(lower) is not None


def check_blob(path: str, data: bytes, record: Record | None) -> list[str]:
    """Every reason the file at `path` (repository-relative, `/`-separated) may not leave."""
    lower = path.lower()
    reasons: list[str] = []
    ext = extension(path)
    if in_vendor_or_baked(lower):
        reasons.append("inside the vendor or baked folder")
    demo = demo_of(lower)
    folder = lower.split("/", 1)[0] if "/" in lower else None
    if folder in BINARY_FOLDERS and (ext in BINARY_EXTENSIONS or b"\x00" in data):
        tagged = (
            demo is not None
            and lower.startswith(f"{demo}/evidence/")
            and ext in IMAGE_TAG_EXTENSIONS
            and has_render_tag(data)
        )
        if not tagged:
            reasons.append(f"binary file under {folder}/")
    if is_song_audio_path(lower) and (ext in AUDIO_EXTENSIONS or looks_like_audio(data)):
        reasons.append("audio of the NCS song (never tracked)")
    elif lower.startswith("songs/action/") and (ext in BINARY_EXTENSIONS or b"\x00" in data):
        reasons.append("binary file in the NCS song's folder (raw audio?)")
    if record is not None:
        if lower.endswith(".meta"):
            for m in META_GUID.finditer(data):
                guid = m.group(1).decode().lower()
                if guid in record.guids:
                    reasons.append(f"GUID {guid} is a vendor asset ({record.guids[guid]})")
        for h in sorted(sha256_forms(data)):
            if h in record.hashes:
                reasons.append(f"SHA-256 {h[:12]}… is a vendor file ({record.hashes[h]})")
                break
    return reasons


@dataclass
class Result:
    failures: list[tuple[str, str]] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    checked: int = 0

    @property
    def ok(self) -> bool:
        return not self.failures

    def report(self, out=sys.stdout) -> None:
        print(f"vendor guard: {'PASS' if self.ok else 'FAIL'} ({self.checked} files)", file=out)
        for w in self.warnings:
            print(f"  warning: {w}", file=out)
        width = max((len(p) for p, _ in self.failures), default=0)
        for p, reason in self.failures:
            print(f"  {p.ljust(width)}   {reason}", file=out)


# The guard's own code: a missing record only warns for it, so a machine without one (the Mac)
# can still change the guard. Its other checks (vendor paths, binaries, NCS audio) still apply.
OWN_TOOLS = "demo3d/tools/"


def check_blobs(blobs: Iterable[tuple[str, bytes]], record: Record | None, record_at: Path,
                changed: set[str] | None = None) -> Result:
    """`changed`, if given, limits the paths that fail a missing record to those it names."""
    res = Result()
    demo_touched = False
    for path, data in blobs:
        res.checked += 1
        demo_touched = demo_touched or (
            under_demo(path.lower()) and not path.lower().startswith(OWN_TOOLS)
            and (changed is None or path in changed))
        for reason in check_blob(path, data, record):
            res.failures.append((path, reason))
    if record is None:
        msg = f"sync record missing at {record_at}; the GUID and hash checks did not run"
        if demo_touched:
            res.failures.append(("(sync record)", msg + " (run `demo3d setup`)"))
        else:
            res.warnings.append(msg)
    return res


# Git enumeration: (path, blob sha) pairs, then their contents.


def _git(repo: Path, *args: str, stdin: bytes | None = None) -> bytes:
    return subprocess.run(
        ["git", *args], cwd=repo, input=stdin, check=True, capture_output=True
    ).stdout


def tree_entries(repo: Path, rev: str) -> list[tuple[str, str]]:
    out = _git(repo, "ls-tree", "-r", "-z", "--full-tree", rev)
    entries = []
    for item in out.split(b"\x00"):
        if not item:
            continue
        meta, path = item.split(b"\t", 1)
        _mode, kind, sha = meta.split()
        if kind == b"blob":
            entries.append((path.decode("utf-8", "surrogateescape"), sha.decode()))
    return entries


def index_entries(repo: Path) -> list[tuple[str, str]]:
    out = _git(repo, "ls-files", "-s", "-z")
    entries = []
    for item in out.split(b"\x00"):
        if not item:
            continue
        meta, path = item.split(b"\t", 1)
        mode, sha, _stage = meta.split()
        if mode != b"160000":  # submodules
            entries.append((path.decode("utf-8", "surrogateescape"), sha.decode()))
    return entries


def commit_entries(repo: Path, commits: Iterable[str]) -> list[tuple[str, str]]:
    """Every blob added or modified by the commits (merges against each parent)."""
    seen: set[tuple[str, str]] = set()
    for c in commits:
        out = _git(repo, "diff-tree", "-r", "-z", "-m", "--root", "--no-renames", "--no-commit-id", c)
        parts = out.split(b"\x00")
        i = 0
        while i < len(parts) - 1:
            meta = parts[i]
            if not meta.startswith(b":"):
                i += 1
                continue
            fields = meta[1:].split()
            new_mode, new_sha, status = fields[1], fields[3], fields[4]
            path = parts[i + 1].decode("utf-8", "surrogateescape")
            i += 2
            if status[:1] in (b"D",) or new_mode == b"160000":
                continue
            seen.add((path, new_sha.decode()))
    return sorted(seen)


def read_blobs(repo: Path, entries: list[tuple[str, str]]) -> Iterator[tuple[str, bytes]]:
    if not entries:
        return
    proc = subprocess.Popen(
        ["git", "cat-file", "--batch"], cwd=repo, stdin=subprocess.PIPE, stdout=subprocess.PIPE
    )
    assert proc.stdin and proc.stdout
    try:
        for path, sha in entries:
            proc.stdin.write(sha.encode() + b"\n")
            proc.stdin.flush()
            header = proc.stdout.readline().split()
            if len(header) < 3:
                raise RuntimeError(f"git cat-file failed for {path} ({sha})")
            size = int(header[2])
            data = proc.stdout.read(size)
            proc.stdout.read(1)  # the newline after each object
            yield path, data
    finally:
        proc.stdin.close()
        proc.wait()


def commits_to_push(repo: Path, remote: str, local_sha: str, remote_sha: str) -> list[str]:
    if remote_sha != ZERO_SHA:
        known = subprocess.run(
            ["git", "cat-file", "-e", f"{remote_sha}^{{commit}}"], cwd=repo, capture_output=True,
            check=False,
        ).returncode == 0
        if known:
            out = _git(repo, "rev-list", f"{remote_sha}..{local_sha}")
            return out.decode().split()
    out = _git(repo, "rev-list", local_sha, "--not", f"--remotes={remote}")
    return out.decode().split()


def vetted_entries(repo: Path, remote: str) -> set[tuple[str, str]] | None:
    """The remote master's files, which a missing record need not fail: None if there is none.

    Master moves only when slow CI passes, and its range check fails any demo change a machine
    without a record makes, so every demo file there was checked against some machine's record.
    """
    ref = f"refs/remotes/{remote}/master"
    known = subprocess.run(["git", "rev-parse", "--verify", "--quiet", f"{ref}^{{commit}}"],
                           cwd=repo, capture_output=True, check=False).returncode == 0
    return set(tree_entries(repo, ref)) if known else None


def demo_paths_only(entries: list[tuple[str, str]]) -> list[tuple[str, str]]:
    return [
        (p, s)
        for p, s in entries
        if under_demo(p.lower()) or is_song_audio_path(p.lower())
    ]


class PushError(Exception):
    pass


def tip_commit(repo: Path, sha: str) -> str:
    """The commit a pushed ref's tip names, peeling an annotated tag; refuses anything else."""
    kind = _git(repo, "cat-file", "-t", sha).decode().strip()
    if kind == "commit":
        return sha
    if kind == "tag":
        peeled = subprocess.run(["git", "rev-parse", "--verify", "--quiet", f"{sha}^{{commit}}"],
                                cwd=repo, capture_output=True, text=True, check=False)
        if peeled.returncode == 0:
            return peeled.stdout.strip()
    raise PushError(f"refusing to push {sha}: it names a {kind}, not a commit")


def pre_push_entries(repo: Path, remote: str, lines: Iterable[str]) -> list[tuple[str, str]]:
    """What a push carries: each new commit's blobs, plus the demo's paths at each tip."""
    entries: set[tuple[str, str]] = set()
    for line in lines:
        if not line.strip():
            continue
        fields = line.split()
        if len(fields) != 4:
            raise PushError(f"unexpected pre-push input line: {line!r}")
        _local_ref, local_sha, _remote_ref, remote_sha = fields
        if local_sha == ZERO_SHA:  # a delete carries nothing
            continue
        tip = tip_commit(repo, local_sha)
        entries.update(commit_entries(repo, commits_to_push(repo, remote, tip, remote_sha)))
        entries.update(demo_paths_only(tree_entries(repo, tip)))
    return sorted(entries)


def manifest_guids(repo: Path) -> dict[str, tuple[str, str]]:
    """Every GUID the demos' tracked manifests list: guid -> (demo, path)."""
    guids: dict[str, tuple[str, str]] = {}
    for demo in paths.PROJECTS:
        manifest = repo / demo / "manifest.json"
        if not manifest.exists():
            continue
        data = json.loads(manifest.read_text(encoding="utf-8"))
        for a in data.get("assets", []):
            if a.get("guid"):
                guids[a["guid"].lower()] = (demo, a.get("path", "?"))
    return guids


def unrecorded_packs(roots: dict[str, str], record: Record) -> list[tuple[str, str]]:
    """The packs in the machine's asset folders that the record lacks: (root, pack) pairs.

    Their files would pass the GUID and hash checks unseen, so each top-level `.meta` under a
    root's Assets/ must have its GUID in the record.
    """
    missing = []
    for name in paths.ASSET_FOLDERS:
        assets = Path(roots[name]) / "Assets" if roots.get(name) else None
        if assets is None or not assets.is_dir():
            continue
        for meta in sorted(assets.glob("*.meta")):
            try:
                m = None if meta.name.startswith("._") else META_GUID.search(meta.read_bytes()[:4096])
            except OSError:
                m = None
            if m and m.group(1).decode().lower() not in record.guids:
                missing.append((name, meta.name.removesuffix(".meta")))
    return missing


def run(mode: str, repo: Path, rev: str | None, remote: str | None, record_file: Path | None,
        stdin_lines: Iterable[str] = (), derived_file: Path | None = None, refresh: bool = True) -> Result:
    """Checks what `mode` selects: a tree, the index, a commit range, or a push."""
    record_at = record_file or paths.record_path()
    derived_at = derived_file or (record_at.parent / "derived-record.json")
    res = Result()
    try:
        current: list[dict] = []
        if refresh and record_file is None:
            from . import derived

            current = derived.refresh(repo, derived_at)
        record = Record.load(record_at, derived_at)
        if record is not None:  # another check may have replaced the file without them
            record.add_entries(current, "derived files")
        if mode == "tree":
            entries = tree_entries(repo, rev or "HEAD")
        elif mode == "index":
            entries = index_entries(repo)
        elif mode == "range":
            assert rev
            commits = _git(repo, "rev-list", rev).decode().split()
            entries = commit_entries(repo, commits)
        elif mode == "pre-push":
            entries = pre_push_entries(repo, remote or "origin", stdin_lines)
        else:
            raise ValueError(mode)
        changed = None
        if record is None:
            vetted = vetted_entries(repo, remote or "origin")
            if vetted is not None:
                changed = {p for p, s in entries if (p, s) not in vetted}
        res = check_blobs(read_blobs(repo, entries), record, record_at, changed)
    except (RecordError, PushError, subprocess.CalledProcessError, ValueError) as e:
        detail = e.stderr.decode(errors="replace").strip() if isinstance(
            e, subprocess.CalledProcessError) and e.stderr else str(e)
        res.failures.append(("(guard)", f"could not check: {detail}"))
        return res
    if record is not None:
        for guid, (demo, path) in manifest_guids(repo).items():
            if guid not in record.guids:
                res.failures.append(("(sync record)", (f"stale: {demo}'s manifest's {path} "
                                     f"({guid}) is not in it; run `{demo} sync`")))
        if record_file is None:  # the machine's own record, so its own roots
            missing: dict[str, list[str]] = {}
            for root, pack in unrecorded_packs(paths.load_roots(), record):
                missing.setdefault(root, []).append(pack)
            for root, packs in missing.items():
                shown = ", ".join(repr(p) for p in packs[:3]) + (", ..." if len(packs) > 3 else "")
                res.failures.append(("(sync record)", (f"stale: {len(packs)} of {root}'s packs "
                                     f"({shown}) are not in it; run `demo2d sync --record-only`")))
    return res
