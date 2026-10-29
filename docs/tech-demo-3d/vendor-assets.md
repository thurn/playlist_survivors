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
  `demo3d/`, and only warns for changes elsewhere.
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
    in each worktree's `Assets/Baked/` and of the song's source. The guard
    adds to it before each check and never removes from it. An edited copy
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
