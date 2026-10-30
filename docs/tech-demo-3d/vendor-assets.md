# 3D Tech Demo: Vendor Assets

## Vendor Assets and Licensing

The demo is built from paid packs, and the repository is public on GitHub.
Synty's license and the Unity Asset Store license allow the assets in a game,
including a distributed build. They forbid redistributing the assets
themselves, and a public repository is redistribution.

### What never reaches git

- **Vendor files:** any file from `~/synty` or `~/unity_assets`. That covers
  meshes, textures, clips, prefabs, materials, shaders, sounds, music, scenes
  and their `.meta` files.
- **Derived files:** anything generated from vendor files. That covers bone
  textures, converted effect definitions, decimated meshes, impostors (flat
  pictures standing in for distant meshes), generated scenes, lightmaps, and
  edited copies of vendor prefabs, materials or scenes.
- **Builds:** the built apps and Unity's `Library`, which hold vendor content
  in compiled form.
- **The song's audio:** the *Action* MP3 and every conversion or crop of it.
  NCS audio is not ours to redistribute. The repo's other songs commit their
  public-domain source audio beside their song maps; this one never does.
  Only its song map, edit list and report summary are tracked.

Vendor files live only in the gitignored `demo3d/Assets/Vendor/` and derived
files only in the gitignored `demo3d/Assets/Baked/`. Tracked files are our own
code, shaders, parameters, annotations and tracked JSON.

Renders of the demo are fine to commit: stills in evidence, kept small. The
licenses allow screenshots of a game made with the assets. Videos stay in the
gitignored `captures/`.

### Curation and sync

Only the assets the demo uses enter the project, which keeps imports and the
`Library` small.

- **The manifest** is tracked JSON listing each vendor asset the demo uses:
  its source, path, GUID and purpose. A source is named by a root (`synty` or
  `unity_assets`) and a path under it, never by a machine's path, and carries
  the source file's SHA-256. The sync tool refuses a source whose hash
  differs, so both machines always sync, and build their records, from
  identical files.
- **Source roots are per machine.** On Cygnus they are the copies under
  `D:\vendor` (see [Moving the assets](windows.md#moving-the-assets)); on
  the Mac, at integration, `~/synty` and `~/unity_assets/UnityAssets`. A small untracked config file in the
  per-machine cache maps each root to its folder.
- **The sync tool** fills the vendor folder from the manifest. It runs on
  both machines, so it is written in a language both run without Unity
  (Python under `uv`, as the repo's analysis tool is, or a .NET console
  tool; TD1 picks):
  - From a Synty `.unitypackage.gz` (a gzipped tar of per-GUID folders, each
    holding `asset`, `asset.meta` and `pathname`), it extracts the listed
    GUIDs.
  - From the `unity_assets` root, it copies the listed files with their
    `.meta` files.
  - Either way it follows `guid:` references in the copied YAML (prefab to
    material to texture or shader) to include everything the listed assets
    need.
- **GUIDs stay the vendor's,** so our tracked YAML can reference vendor
  assets by GUID. A reference is not vendor content.
- **A sync record** (gitignored) lists every synced file's GUID and SHA-256.
  The guard checks against it. The Mac never imports anything into Unity,
  but at integration it builds the same record from the same manifest and
  its own copies of the sources (a record-only run of the sync tool), so its
  gate can run the guard.

```json
{ "source": "synty:POLYGON_Fantasy_Rivals_Unity_2021_3_v1_3_1.unitypackage.gz",
  "path": "Assets/PolygonFantasyRivals/Models/Characters.fbx",
  "sourceSha256": "…", "guid": "…", "use": "elites: Big Ork, Troll" }
```

### The vendor guard

A leak to the public repository cannot be undone, so a guard checks every
change wherever it can leave the machine:

- **On Cygnus, before every push to the relay repository,** from a git
  pre-push hook. The relay is private, but everything pushed to it is bound
  for the public repository.
- **On the Mac, since integration:** `scripts/check-fast`, the gate (which
  rebuilds the Mac's record when the manifest changes) and the release
  stage's `ci-full-demo3d` step. The release stage is the last line of
  defense, because it moves remote master, and Tollgate's push of remote
  master runs the same pre-push hook.

The guard fails when any tracked file:

- is inside the vendor or baked folders;
- is binary (by extension or a NUL byte) anywhere under `demo3d/`. The one
  exception is a PNG or JPEG under the demo's evidence that carries the
  capture tool's `ps-render` text tag, which marks it as our render;
- has a `.meta` GUID that appears in the sync record;
- has a SHA-256 that appears in the sync record;
- is audio (by extension or content) under `songs/action/`, or under
  `public/audio/` with a name starting `action`. `.gitignore` already
  excludes both, so this catches only a force-add or a renamed path.

More rules:

- **Our own images carry the tag.** Every tool that writes images (the
  capture tool, the A/B tool, the clip annotator's contact sheets, the report)
  adds the `ps-render` tag.
- **One record per machine.** The sync record lives in the per-machine cache
  (see [Unity Work by Agents](windows.md#unity-work-by-agents)). On Cygnus,
  every task worktree reads the same file, and a manifest change rebuilds it
  in setup. At integration, the Mac's worktrees and Tollgate's own checkouts
  share the Mac's record, rebuilt in its gate.
- **It starts empty.** The setup command creates an empty record until the
  sync tool (TD2) first fills it. An empty record is valid.
- **A missing record fails safe.** If the record file is missing, the GUID and
  hash checks cannot run. The guard then fails any change that touches
  `demo3d/` or `demo2d/`, and only warns for changes elsewhere. A demo file
  whose path and contents are already on the remote's master (the local
  `<remote>/master` ref: `origin/master`, or the pushed remote's in a push) is
  not a change: it is already public, so checking it again guards nothing.
  This lets a machine without a record (the Mac, since the vendor packs live on
  Cygnus) pass its index and tree checks, while its range checks still fail
  any new demo file. Without that ref, every demo file counts as a change. The
  rule trusts the local ref, so a stale or hand-moved one would let its
  contents through. The guard's own code, `demo3d/tools/`, only warns without
  a record, as `docs/` does, so the Mac can still change the guard; its
  vendor-path, binary and audio checks still apply there.
- **A partial record counts as missing for its demo.** A record that lacks a
  demo's manifest GUIDs while one of that demo's source roots is absent from
  the machine (the Mac's record covers the 3D packs, but `pixel_assets`, with
  the 2D packs, lives on Cygnus) cannot be fixed by a sync there. The guard
  then treats that demo's files as under a missing record, by the rule above,
  and only warns for its unchanged files; the other demo keeps the record's
  full checks. With all of the demo's roots present, the record is stale.
- **The guard has its own tests,** with synthetic fixtures: a fake record, a
  fake vendor file, a tagged render. TD1 proves every failure case with them
  before any vendor file exists.
- **What TD1 built** (after the guard's review):
  - The record lists every file under every source root, not only the
    synced ones, each with its GUID, its SHA-256 and `sha256Lf` (the hash
    with CRLF folded to LF, since git stores text with LF). A record with an
    unknown version or an entry without both hashes fails every check, and
    so does one that lacks a GUID the tracked manifest lists.
  - A **derived record** beside it holds the hashes and GUIDs of every file
    in each worktree's `Assets/Baked/` and `Assets/Vendor/` (Unity rewrites
    some vendor files on import, such as demo2d's binary Spine clips, which
    it re-serializes as YAML; PX2) and of the song's source. Before each
    check the guard hashes those files in every worktree of the clone, so a
    file copied from a worktree whose guard never ran is caught too; it
    checks against them directly (a concurrent check may replace the record
    without them) and adds them to the record, which never forgets a removed
    worktree's files. An edited copy
    of a vendor file with new bytes and a fresh GUID can't be recognized;
    the binary rule and the gitignored folders are the defense there.
  - Song audio means any audio file under `songs/` or `public/` with a path
    segment starting `action`, plus any binary file under `songs/action/`.
  - A tagged render must end where its image ends and stay under 4 MB.
  - The hook lives in the clone's shared hooks folder, so every checkout
    runs it. A checkout without `demo3d/tools` (Tollgate pushes remote
    master from the Mac's main checkout, which may be on an older master)
    runs the guard from the first pushed commit that has them, and refuses
    any push that touches the demo's paths only when none has. Until
    integration the guard also refused demo files bound for the public
    repository; integration dropped that rule.

```
vendor guard: FAIL
  demo3d/Assets/Art/Goblin_Color.png   binary file under demo3d/
  demo3d/Assets/Fx/Slash.prefab.meta   GUID 9f3c… is a vendor asset (Hovl)
```

AGENTS.md carries the rule for every agent: never commit vendor or derived
files, never force-add an ignored path, and never bypass the guard.

### The 2D demo

Since PX1 the same guard and tools also cover the
[2D pixel demo](../tech-demo-2d/README.md#vendor-assets), `demo2d/`:

- **A third root, `pixel_assets`,** is the asset folder whose `Assets/`
  holds the 2D packs (`~/unity_assets/Assets` on the Mac; the root itself
  is `~/unity_assets`, as `unity_assets` is `~/unity_assets/UnityAssets`).
  A `roots.json` written before it gains it with its default. The sync
  record lists its files as it lists the others', so one record per machine
  knows both demos' vendor files.
- **`demo2d/` is under every rule `demo3d/` is under:** its `Assets/Vendor/`
  and `Assets/Baked/` are gitignored and refused; binaries under it fail
  unless they are tagged renders under `demo2d/evidence/`; a missing record
  fails changes under it; the derived record adds its `Assets/Baked/` and
  `Assets/Vendor/`; and
  the stale-record check reads `demo2d/manifest.json` too.
- **The shared Unity package is code only:** binaries under
  `unity-packages/`, which both demos import, fail as they do under a demo.
- **The record must know every pack on disk:** a top-level folder of
  `unity_assets` or `pixel_assets` whose `.meta` GUID the record lacks (a
  pack copied in, or a root added, since the last sync) fails every check
  until `demo2d sync --record-only` rebuilds the record. A new folder nested
  inside an existing pack is not noticed; setup's sync rebuilds the record
  per worktree.
- **`demo2d <command>`** (`uv run --project demo3d/tools demo2d`) runs the
  setup, sync, guard, Unity, test and build tools on `demo2d/`.

### The roster sets

Since ps-f8a.1, demo3d's [`--roster` sets](crowd.md#roster-sets) draw on a
fourth root:

- **`more_assets`** is the asset folder whose `Assets/` holds the MoreAssets
  packs (`~/unity_assets/MoreAssets/MoreAssets` on the Mac). Only the Mac
  maps it; Cygnus has no default for it, and a `roots.json` written before
  it gains the Mac's default.
- **Its manifest entries sync only on request.** Each one names its set
  (`"roster": "forest"` or `"heroic"`), and a `more_assets` entry without
  one is refused. `demo3d setup --roster <set>` syncs them beside the
  default's; without the flag they are
  skipped, so CI and a clean checkout never need the root. Asking for a set
  where the root is not mapped fails with that message. Add entries with
  `demo3d manifest add more_assets <glob> --roster <set>`.
- **The vendor cache keys by the sets too,** so a set's vendor folder and
  Library are never a default worktree's exact cache entry (a set's Library
  can still be the nearest one a new worktree starts from, costing only a
  reimport). The worktree's vendor folder records its sets in
  `Assets/Vendor/.rosters`.
- **The guard covers it where it is.** The record lists every file under
  the root, as for the other asset folders, and its packs must be in the
  record. Its manifest entries are never required in the record, so a
  record from a machine without the root still passes. A record built without the root (by a checkout older than
  ps-f8a.1) fails as stale until `demo3d sync --record-only` (or a setup)
  rebuilds it.
- **Derived files** go to `demo3d/Assets/Baked/Rosters/<set>/`, gitignored
  with the rest of `Assets/Baked/`.
