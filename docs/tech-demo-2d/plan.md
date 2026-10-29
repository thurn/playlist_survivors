# 2D Pixel Demo: Implementation Plan

The demo's work is eleven tasks, `PX0` to `PX10`, on the operator's Mac.
This file is the plan's only tracker: each task's status, notes and commits
live in its entry under [Task Notes](#task-notes). The design is in
[README.md](README.md).

- **Plan keys** name tasks, commits' scopes (`feat(demo2d): …`,
  `refactor(choreography): …`) and evidence directories.
- **Evidence** goes in `demo2d/evidence/<key>/` (lowercase). Stills carry
  the `ps-render` tag.
- **Summaries** go in that key's entry under [Task Notes](#task-notes).
- **Delivery** is Enact's, with no beads: a worktree per task (or per
  session), `enact push` to `staging`.

## Tasks

Each task lists what it builds and when it is done.

- **PX0, the shared choreography package.**
  - Moves demo3d's engine-free core from `demo3d/Assets/Demo3D/Choreography/`
    into `unity-packages/com.playlistsurvivors.choreography/` (a local UPM
    package, `file:` dependency in demo3d's manifest), with the namespace
    `PS.Choreography`. demo3d's data (roster, footprints, layouts, level and
    stress scripts) stays in demo3d. Anything in the core that names a
    demo3d type or file becomes a parameter or moves out.
  - Points `demo3d/dotnet/` at the package's sources, and updates
    `scripts/check-fast`'s path filter and `scripts/ci-full demo3d`.
  - Done when: demo3d's EditMode and `dotnet` tests pass; the song's timeline
    JSON dump is byte-identical before and after (the diff is in the notes);
    BakeAll, a Mac build and windowless stills at 45, 170 and 196 s match the
    old build; and mac.md's commands still work as written.
- **PX1, foundations.**
  - Builds `demo2d/`: a Unity 6000.6.7f1 project with URP's 2D renderer,
    Metal, IL2CPP for the Mac, referencing the PX0 package. It has a
    `Demo2D.Runtime` assembly, an `Editor` assembly with batch-mode task
    entry points, and EditMode tests.
  - Generalizes `demo3d/tools` so one tool serves both projects (a
    `--project demo2d` option or a `demo2d` entry point; PX1 picks). It
    covers `setup` with its warm `Library` cache, `unity <Task>`, `build`,
    `test`, `check`, `sync` and `guard`.
  - Extends the vendor guard: the `pixel_assets` root for
    `~/unity_assets/Assets` in `roots.json` and the sync record; `demo2d/`
    under the binary, GUID, hash and derived-record rules; `.gitignore`
    lines mirroring demo3d's. New synthetic-fixture tests cover each case
    under `demo2d/`.
  - Spikes, recorded in its notes: cold and warm batch-mode compile and test
    times; IL2CPP and Mono build times; and whether a batch-mode Metal player
    that draws its camera by hand reports per-frame CPU and GPU times
    (`FrameTimingManager`, or Metal command-buffer timing), which PX10's
    performance check needs.
  - Done when: one command sets up a fresh worktree; an empty 2D scene
    builds a Mac player and writes a windowless still; the guard's new
    tests pass and `scripts/check-fast` runs them; the spikes are recorded;
    and the guard's own review is done (see [Reviews](#reviews)).
- **PX2, assets and their verification.**
  - Builds `demo2d/manifest.json` for the hero, the 13 roster packs, the
    AncientForest sheets and animated trees, the CFXR runtime (shaders,
    scripts, textures) and the effect candidates, and the demo3d sounds it
    reuses; then syncs them.
  - Runs every check in [Verifying the Assets](README.md#verifying-the-assets),
    with an EditMode test for each automatic one, and the vendor guard
    drill.
  - Chooses the effect list's candidates: two or three CFXR prefabs per
    effect kind, from a gallery still per kind under night lighting.
  - Done when: every check passes with its stills in evidence (a contact
    sheet per character, the tile test map, the CFXR gallery); the guard
    drill's output is in the notes; and nothing under `demo2d/` that git
    tracks is vendor or derived.
- **PX3, the sprite baker and clip library.**
  - Builds: the baker (trim, area downsample to each type's game height,
    alpha snap, atlas, point filtering) into `Baked/`; the clip library's
    JSON (frames, frame rate, pivot at the feet, impact frame, stride, end);
    the annotator, which proposes impacts and strides from frame motion,
    with its contact sheets.
  - Chooses each type's game height (grunts about 56 px, elites about 1.6×,
    the boss about 3×) from a lineup still, and checks readability at that
    size: each type recognizable, the hero always findable.
  - Done when: every roster and hero clip the choreography uses is baked and
    annotated; a lineup still shows every type at game size on the forest
    ground under night lighting; and a review of the annotations' contact
    sheets is in [Pending on the operator](#pending-on-the-operator).
- **PX4, 2D choreography data.**
  - Builds demo2d's catalog (types, sizes, speeds, footprints, attacks), the
    2D level script derived from `action-level.json` (same sections, goals
    renamed to the 2D layout's areas, the 2D roster's mix, a peak of about
    400 on screen with the 1.35× zoom-out), and the hero's combo and spell
    tables for knight_male's clips.
  - Uses an open-ground layout until PX6, as demo3d's TD3 did.
  - Adds to the core only what both demos can use (for example, facing
    quantized to left or right for a side-view client is a client concern
    and stays out of the core). Any core change keeps demo3d's timeline
    byte-identical, or records why it can't and re-checks demo3d.
  - Done when: the song's 2D timeline builds and passes the core's timeline
    tests; the per-section on-screen counts match the level script within
    demo3d's tolerance; and it builds in under 3 s.
- **PX5, client playback and the pixel pipeline.**
  - Builds: the song clock and fixed-time mode; timeline evaluation; the
    instanced sprite crowd (frame, flip, flash, tint, y-sort, blob shadows)
    and the hero; the 960×540 target, the smooth upscale with its zoom, and
    the camera's follow, punch and shake; the development flags (`--click`,
    `--start-at`, `--seconds`, `--fixed-time`, `--freeze-at`, `--overlay`,
    `--capture-stills`, `--stills-at`); windowless stills.
  - Decides how the climax's zoom-out keeps the grid's density (a larger
    target, or a softer upscale), from paired stills.
  - Done when: the song plays on open ground with every step, blow, flinch
    and death landing on the click (the operator's check goes to
    [Pending on the operator](#pending-on-the-operator)); a seek shows the
    same horde as a run from the start (a test on stills); and stills at
    the intro, the build and the climax are in evidence.
- **PX6, the arena.**
  - Builds: the 2D layout format (demo3d's areas plus ground materials,
    cliffs, water and bridges) and the arena layout, with named goals for
    the level script; the tilemap generator with autotiled edges; the
    scatter of Decorative props and swaying trees; footprints into the sim's
    obstacle map; the canopy fade for props in front of the fight.
  - Done when: the arena generates deterministically from the JSON; the
    song's timeline builds with walls and keeps the level script's counts;
    no monster or the hero stands in a tree, cliff or the stream (a test
    over the timeline); and an overview still and three ground-level stills
    are in evidence.
- **PX7, effects and night lighting.**
  - Builds: the effect library from PX2's candidates (one prefab per kind,
    scale, tint, sorting and light cue), pooled CFXR instances driven by the
    timeline's effect events, the `Light2D` pool, the global moonlight,
    moonlight shafts, fireflies, glowing plants, bloom and the colour grade.
    Effects draw in the pixel grid unless their entry opts out with a
    reason.
  - Measures particle and light cost at the climax's load on the Mac, and
    caps the pools so the climax stays within the frame (PX10 checks the
    whole frame).
  - Done when: every effect kind fires in the song; a still per kind and a
    night-look set (empty arena and with the horde) are in evidence; and
    the night-look judgement is in
    [Pending on the operator](#pending-on-the-operator).
- **PX8, hero and presentation.**
  - Builds: the hero's combos (slashes on beats, spins on accents, slams on
    phrase downbeats, a shield bash), spells on the grid (rings, bolts,
    lightning, the climax's beam sweep), hit-stop on big blows, knockback
    hops, death bursts, camera punch and shake on accents, the boss's entry
    and attacks, and sound scheduling with the reused packs.
  - Done when: the song's first minute and its climax play with every blow
    on its click; beam ticks flash monsters without knockback; the ending's
    accents each get a hero slam; and the listening check is in
    [Pending on the operator](#pending-on-the-operator).
- **PX9, the art pass.**
  - Builds: reference stills from the packs' own demo scenes (`Demo.unity`
    in AncientForest, CFXR's demo); six beauty shots from the game camera
    at fixed song times (intro, first wave, a caster volley, an elite kill,
    the boss's entry, the climax at full density); the readability measure
    (the hero findable and each monster's silhouette distinct at the
    climax's zoom); and the layout dressing and lighting that meet them.
  - Done when: every beauty shot passes the checklist (one focal point, the
    hero readable, effects carrying light, no flat or muddy ground, no
    tiling repetition visible), the shots are in evidence and in
    [Pending on the operator](#pending-on-the-operator), and the scene
    milestone's review is done.
- **PX10, the demo.**
  - Tunes the level script and effect density so the full song looks right
    and holds the frame: the windowless song check (using PX1's timing
    spike) reports per-frame CPU and GPU time at 1080p over the whole song,
    and passes when the 99th percentile stays under 16.7 ms and no second
    has a frame over 25 ms. If it fails, PX10 thins spells first, then
    effect lights, and never the horde below about 350 at the peak.
  - Bundles the app (`demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app`),
    writes the setup, bake, build and watch commands into README.md, and
    adds the operator's viewing check.
  - Done when: the song check passes with its numbers in evidence; the final
    beauty stills are in evidence; the viewing row is in
    [Pending on the operator](#pending-on-the-operator); and the demo
    milestone's review is done.

## Order and parallelism

```
PX0         first
PX1         after PX0 (demo2d references the package)
PX2         after PX1
PX3         after PX2
PX4         after PX0, in parallel with PX1 to PX3 (it uses placeholder clip
            annotations until PX3 lands, as demo3d's TD3 did)
PX5         after PX3 and PX4
PX6         after PX2 and PX4; its client steps after PX5
PX7         after PX5
PX8         after PX5 and PX7
PX9         after PX6, PX7 and PX8
PX10        after PX9
```

- **The graph is kept wide.** A task starts as soon as its first steps can.
- **A step that waits** is recorded in the task's notes, and finished once
  the other task lands.

## Reviews

- **One cold review per milestone,** by a fresh read-only subagent told to
  use the warden skill and not to edit files, over the milestone's commits
  as its tasks recorded them. The milestone's last task requests it, fixes
  what it confirms, and records what was fixed or dismissed:
  - the shared core: PX0 (it changes demo3d);
  - foundations: PX1 to PX5;
  - the scene: PX6, PX7 and PX9;
  - the demo: PX8 and PX10.
- **A review for the vendor guard** (PX1, and any later change to it),
  because a leak can't be undone.
- **Self-review first,** as AGENTS.md's checklist describes, recorded in
  the task's notes.

## Working rules

- **Playable after every task.** From PX5 on, each task keeps the app
  buildable and playable on the Mac, and writes its run command in its
  notes.
- **Windowless.** Agents drive Unity in batch mode in their own worktree's
  project, and check the player with `-batchmode -force-metal` and
  `--capture-stills`. Only the operator opens the app's window.
- **Commit SHAs.** Each task records its commits in its notes.
- **No approval pauses.** Checks only the operator can make go to
  [Pending on the operator](#pending-on-the-operator).
- **Never commit vendor or derived files,** never force-add an ignored path,
  never bypass the guard.

## Pending on the operator

Checks only the operator can do. A task adds a row and moves on; the task
that consumes the result removes the row.

| What | How | Added by | Consumed by |
|---|---|---|---|
| Does a windowed player report a GPU frame time every frame? (A batch-mode one reports it on 3% of frames.) | After `demo2d build`, from the worktree's root: `open -n demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --frame-timing "$PWD/captures/px1-timing/windowed-400.json" --spike-sprites 400 --seconds 10`; the window closes itself after 12 s. In the JSON, compare `ftm.gpuFrameTimeMs`'s `nonzero` with `frames`. | PX1 | PX10 |
| Are PX3's clip annotations right? Each attack's and slash's impact on the blow, each hit's and death's end where it settles or rests, the slam's on the landing, and the walks' strides plausible for their gait (two are fallbacks: the lava beast and the boss have no planted foot). | Look over `demo2d/evidence/px3/annotations-*.jpg` (impact framed red, end blue); the numbers and methods are in `demo2d/Assets/Demo2D/Data/clip-annotations.json`. To change a clip or a proposal, edit the entry's `clip` and re-run `demo2d unity AnnotateClips`, or note the frame here. | PX3 | PX8 (PX5 plays them as annotated; still unanswered) |
| Is every type recognizable at its game size, and the hero findable in a crowd? PX3 found each type distinct, but the hero findable only from his place and a warm light, and the ghoul, zombie warrior, ice skull and blood mage close in colour at night. | Look at `demo2d/evidence/px3/lineup.png` and `crowd.png` (2×, as at 1080p). | PX3 | PX9 |
| Does the song play on the click? Every step, blow, flinch and death landing on the beat, with no sprite popping, sliding or flickering between facings, and the scroll and zoom smooth. | After `demo2d build`, from the worktree's root: `demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/"Playlist Survivors 2D Demo" --click --overlay` (add `--start-at 160` for the climax). Note any section or second that drifts. | PX5 | PX8 |

## Task Notes

Each task's status, commits, run command and a few lines summarizing its
evidence, linking `demo2d/evidence/<key>/`. A task writes only in its own
entry.

### PX0

Done (bead ps-tfn.1.1). Commits: `2671f867` (the move), and this notes commit.
[Evidence](../../demo2d/evidence/px0/README.md).

- **The package:** `unity-packages/com.playlistsurvivors.choreography/`,
  `Runtime/PS.Choreography.asmdef` (namespace `PS.Choreography`), a `file:`
  dependency in demo3d's manifest. The moved files kept their `.meta` GUIDs.
- **What stayed in demo3d:** `Data/` (roster, footprints, layouts, level and
  stress scripts), and a small `Demo3D.Choreography` assembly beside it with
  `ChoreographyFiles` (loading demo3d's files from a checkout) and
  `TimingTables` (the evidence tables over S1 to S6, split out of
  `TimelineReport`). The footprint table's error no longer names demo3d's
  `MeasureFootprints` task.
- **dotnet:** `demo3d/dotnet/PS.Choreography/` builds the package's sources;
  `Demo3D.Choreography` builds the glue over it. `scripts/check-fast` runs the
  dotnet tests when the package changes; `scripts/ci-full demo3d` already ran
  the same test project.
- **Evidence:** the song's timeline dump is byte-identical before and after
  (SHA-256 `09945e3b…`); dotnet 121 passed; EditMode 211 of 214 passed, 3
  skipped; BakeAll and the IL2CPP build succeeded; the stills at 45, 170 and
  196 s differ from the old build's no more than the old build's differ
  between runs. mac.md's commands ran as written.
- **Self-review:** re-read the diff against `origin/staging`: only namespace
  lines change in the moved files besides the `TimelineReport` split, the
  `LayerInfo` comment and the footprint message.
- **Surprise:** `demo3d test` hung 35 minutes after its tests passed, held
  open by an orphaned UnityPackageManager server; filed as ps-tfn.7.
- **For PX1 onward:** reference the package from demo2d's manifest the same
  way (`file:../../unity-packages/com.playlistsurvivors.choreography`). Its
  tests still live in demo3d (`Tests/Choreography/`), since they run on
  demo3d's data.
- **Review** (Warden, bead ps-tfn.1.2): no problems confirmed, no code
  changed. Re-ran on the staging tip `3187ee6f`: dotnet 121 passed and the
  song's dump is still `09945e3b…`; nothing under the package or demo3d's
  Unity project changed since PX0, so its EditMode, build and stills
  evidence stands. Checked: the package names no demo3d type or file (only
  shared vocabulary such as the default `skeleton` mix); the old core
  asmdef's GUID moved to `PS.Choreography.asmdef`, and no asmdef references
  by GUID; check-fast's dotnet filter covers the package, and slow CI runs
  every section. Left alone: the package has no tests of its own (they run
  on demo3d's data, and fire on package changes), and the unused
  `changes_demo3d` in `scripts/lib/changes.sh` does not list the package.

### PX1

Done (bead ps-tfn.1.3). Commits: `c5cc4687` (the tools and the guard), `ff0a944a` (the
project), `51074dfe` (docstrings), and this notes commit. `demo2d check --player` passes
(the guard, dotnet, cold EditMode 23 of 23, the IL2CPP build). [Evidence](../../demo2d/evidence/px1/README.md).

- **Run:** from a worktree's root, with the player's paths absolute (a Mac player resolves
  relative ones against the `.app`'s folder):

  ```
  uv run --project demo3d/tools demo2d setup    # vendor, the song, a warm Library, the guard hook
  uv run --project demo3d/tools demo2d build    # Builds/mac/il2cpp/PlaylistSurvivors2D.app (--backend mono: faster)
  uv run --project demo3d/tools demo2d test     # EditMode tests
  "$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo" \
    -batchmode -force-metal -logFile "$PWD/demo2d/Logs/stills.log" \
    --capture-stills "$PWD/captures/px1" --stills-at 2 --seconds 2.5
  ```

- **The project:** `demo2d/`, Unity 6000.6.7f1, URP 17.6's 2D renderer (transparency sorted
  on the y axis, HDR, no MSAA), Metal only, IL2CPP, Apple silicon, linear, 1920×1080
  windowed, frame timing stats on. `ProjectSetup.Configure` (`demo2d unity Configure`) writes
  all of it and the empty scene; it is idempotent. Assemblies: `Demo2D.Runtime` (args, the
  render tag, windowless stills, the timing spike, the generated test card),
  `Demo2D.EditorTools` (`Tasks`: Noop, Configure, BuildPlayer; Mac only, and it restores the
  two URP assets a build rewrites), `Demo2D.EditMode.Tests`; PX4's `Demo2D.Choreography`
  assemblies now compile and pass in Unity too (23 of 23).
- **The tools:** a second entry point, `demo2d`, over `demo3d/tools` (chosen over a
  `--project` option so every command line names its project once, at the front). It runs
  setup, guard, check, unity, test, build, library, sync and manifest on `demo2d/`; sweep,
  report, pix, mix, flip and the warm editor stay demo3d's (demo2d has no warm editor; every
  Unity step runs cold). Both projects share the per-machine cache, `roots.json` and one sync
  record; Library and vendor caches are keyed by each project's own files, so they never
  collide. Setup checks only the roots its project needs.
- **The guard:** the new root `pixel_assets` is `~/unity_assets` (whose `Assets/` holds the
  2D packs), as `unity_assets` is `~/unity_assets/UnityAssets`; a `roots.json` without it gains
  the default. The record now lists every file under it. `demo2d/` falls under the vendor and
  baked folders, the binary rule (tagged renders only under `demo2d/evidence/`), the missing
  record rule, the derived record (its `Assets/Baked/`; labels now name the demo, as
  `demo3d/Assets/Baked/...`) and the stale check (`demo2d/manifest.json`, empty until PX2).
  The pre-push hook's no-tools fallback refuses `demo2d/` changes too. `.gitignore` mirrors
  demo3d's lines. 19 new test cases on synthetic fixtures (92 pass, 2 skip); `scripts/check-fast` runs them whenever
  `demo3d/tools` changes, and runs the guard itself on every push.
- **Spikes:** cold compile 36.7 s, warm 7.9 s; EditMode tests 31.3 s cold, 13 to 18 s warm;
  IL2CPP build 60.8 s first and 16.4 s after, Mono 13.9 s. A batch-mode Metal player drawing
  by hand reports CPU times every frame but GPU times on only 3% of frames (at 400 or 4,000
  sprites), so PX10 should time the whole frame with a one-pixel readback after each draw
  (the main thread then includes the GPU's work; conservative). Memory stays flat over 9,168
  hand-drawn frames, so a whole-song batch-mode run is feasible. The windowed comparison is in
  [Pending on the operator](#pending-on-the-operator).
- **The guard's review** is the separate Warden bead ps-tfn.1.4.
- **Review** (Warden, bead ps-tfn.1.4): two gaps fixed, in the commit `fix(px1): …`
  after `4333d425`.
  - **A record older than a root went unnoticed.** A machine whose record predates
    `pixel_assets` (Cygnus, once it has the 2D packs), or one that gains a pack after its last
    sync, let that pack's text files (prefabs, clips, materials, `.meta`s) through. Only the
    binary rule stood in the way, and only under a demo. Now every top-level pack `.meta` in
    the machine's asset folders must have its GUID in the record, or every check fails with
    one `stale` line per root. A folder nested inside a known pack still isn't noticed; setup
    rebuilds the record per worktree.
  - **Binaries in `unity-packages/`** (both demos import it; code only today) now fail the
    binary rule too. The executor flagged this.
  - **Fixtures pinned:** guard tests now give their temp cache a `roots.json`. Without one,
    `load_roots`'s new defaults pointed them at the real vendor folders.
  - **Drill**, in a scratch clone at `4333d425`, staged with `git add -f`; every case failed:
    a 2D pack's PNG and its `.meta` under `demo2d/Assets/Art/`, a prefab (and a CRLF copy),
    a `.meta` in `Vendor/`, a fresh binary under `Demo2D/` (upper case), and a PNG, `.mat`
    and `.anim` under `docs/` and `unity-packages/` (by hash). Against a record without
    `pixel_assets`, the new check fails it (42 packs). The `.gitignore` lines all match.
  - **Dismissed:** the derived record's relabelling (dedup is by hash and GUID, so nothing
    doubles); the stale-manifest failure on a machine without the 2D packs (that is the
    intended fail-closed); and the no-tools pre-push fallback skipping `unity-packages/`
    (every pushed tip since TD1 has the tools).
- **Surprises:** a sprite renderer created at runtime in a player gets the unlit sprite
  material, so the first still had no lighting; the scene's test card now names URP's
  `Sprite-Lit-Default`, which PX5's crowd should do too. The Mac player resolves relative
  paths against the folder holding the `.app`.
- **Self-review:** re-read the diff against `origin/staging`. demo3d's behaviour is unchanged
  except the derived record's labels, setup checking only demo3d's roots, and the shared
  record gaining `pixel_assets`; `demo3d setup --no-unity` still runs. Every `.meta` GUID in
  `demo2d/` is Unity's own, and the guard passes the tree and the index.

### PX2

Done (bead ps-tfn.1.5). Commits: `ce81bfb6` (the manifest, the checks and the stills' code),
`de587bc6` (the stills at the grid's size, and the evidence), `68de5e1e` (the guard fix), and
this notes commit. `demo2d check --player` passes (the guard, dotnet, cold EditMode 68 of
68 with 45 of them PX2's, the IL2CPP build with CFXR's scripts). [Evidence](../../demo2d/evidence/px2/README.md).

- **Run:** from a worktree's root:

  ```
  uv run --project demo3d/tools demo2d setup     # syncs demo2d/manifest.json into Assets/Vendor
  uv run --project demo3d/tools demo2d test      # Demo2D.Tests.AssetTests: the automatic checks
  uv run --project demo3d/tools demo2d unity AssetStills -out "$PWD/demo2d/evidence/px2"
  ```

- **The manifest:** 3,619 entries (about 7,400 files, 205 MB once synced): every frame and
  `.anim` of knight_male and the 13 roster packs; AncientForest's sliced `MainLev` and
  `Decorative` sheets and its 7 swaying trees; CFXR's shaders, their importer (CFXR's editor
  scripts: `.cfxrshader` files need it) and its runtime scripts; 37 CFXR effect candidates; and
  demo3d's 64 sound entries.
- **The checks** (`Demo2D.Tests.AssetTests`), one per automatic check in Verifying the Assets:
  every clip a complete, imported sequence; every clip the clip library names has its frames;
  the forest sheets at 32 px per unit, point filtered; every transition set's corner patterns;
  the tile test map rendering exactly as its tiles compose on the CPU (no seams, 3.4 M pixels);
  the trees' clips; each of the 37 CFXR prefabs free of missing scripts, meshes and broken or
  undrawn shaders, and visible under the moon and with no light; and the candidate list.
- **The code** (`Demo2D.EditorTools.Assets`): `CharacterPacks` (frames and clips by pack),
  `ForestTiles` (the sheet's catalogue, a corner autotiler, the test map), `EffectGallery`,
  `ContactSheets`, `AssetStills` (`Tasks.AssetStills`), `PixelShots` (render a world rectangle
  at one pixel per grid pixel and read it back), `PixelText` (a 3×5 label font).
- **The tile sheet,** in `Data/forest-tiles.json`: 14 transition sets, each two 5×5 corner
  templates (inner on outer and the reverse) that give every corner pattern but the two
  diagonals, so a corner autotiler draws any blob with convex and concave corners; their edge
  and fill variants mix without visible seams (the stills). Six sets have a transparent
  terrain, drawn over another ground. The cliffs, plateaus and cliffs over water are authored
  blocks (stamps) that PX6 must compose; the test map places them as authored.
- **Effect candidates,** in `Data/effect-candidates.json`, chosen from the galleries: 3 per
  kind, best first, with reasons, all used as authored. Five prefabs left the pool: four draw
  nothing to the 2D camera, and ScreenDistortion Ring draws a grey blur instead of distorting
  (the 2D renderer gives it no opaque texture), so the README's distortion prefabs are out
  unless PX7 provides one. CFXR has no true beam, and its slashes are spark bursts (knight_male's
  skills draw their own arcs). At native scale the effects are large (a hit spark is about 2 m).
- **Guard drill:** 89 cases in a scratch clone, every one refused; both controls pass
  (evidence's `guard-drill.txt`). Its first run found a gap, fixed in `68de5e1e`: Unity
  re-serializes the packs' binary Spine clips as YAML on import, so the copies in `Vendor/`
  matched no source hash and carried no GUID, and a copy under `Demo2D/Art/` passed. The derived
  record now records each demo's `Assets/Vendor/` as well as its `Assets/Baked/` (about 3 s on a
  first guard run, cached after). Its own review is Warden bead ps-tfn.1.10.
- **Guard review** (ps-tfn.1.10): the fix holds; the review's guard commit closed two gaps.
  A clip Unity rewrote in a worktree whose guard never ran passed when copied into another, since each
  guard recorded only its own worktree; the guard now hashes every worktree's `Baked/` and
  `Vendor/` (17,000 files on the Mac, under 1 s warm). And two concurrent checks each replaced the
  record without the other's entries, so a check could read back a record lacking its own
  rewritten files; it now checks against them in memory too, writes the record and its stat
  cache atomically, and prunes the stat cache to files that exist. A cold import in a fresh
  worktree rewrote 196 `.anim`s and 10 `.anim.meta`s (all keeping their vendor GUID) and nothing
  else (no prefab, material or texture); a warm Library rewrites nothing. The drill, re-run in a
  scratch clone of that commit on those rewrites: 89 refused, both controls pass. Left alone:
  edited copies with new bytes stay undetectable (as designed), and a separate clone's folders are
  not scanned (only this clone's worktrees).
- **Nothing tracked under `demo2d/` is vendor or derived:** the guard passes the tree and the
  index (5,566 files); the new tracked files are our code, JSON, `.meta`s Unity wrote, and
  tagged stills.
- **For PX3 onward:**
  - **The frames run at 15 fps,** not the `.anim`s' 30: every pack exported its frames from 0
    through the clip's end at 15 fps, so a clip of length L has ⌈15·L⌉ + 1 frames (all 196
    clips; `CharacterPacks.FrameRate`). The placeholder annotations' 30 fps is wrong by 2×.
  - **Each clip has its own canvas** (sizes differ between clips of a pack), so the baker should
    trim per clip and align by a fixed anchor, not by canvas.
  - **Edit-mode `AnimationClip.SampleAnimation` doesn't set a sprite;** `AssetStills.TreeSprite`
    reads the clip's sprite keys instead.
  - **Opening the project rewrites two ProjectSettings files** (`GraphicsSettings`' light
    intensity flags, `InputManager`'s debug buttons): Unity's own changes, discarded here.
  - Some CFXR meshes are an old serialization version that Unity warns about; they load, and the
    prefabs using them draw.
- **Self-review:** re-read the diff against `origin/staging`. The only change outside demo2d's
  data, code and evidence is the guard fix (`derived.py`, a test, and doc lines). demo3d is
  unchanged except that the guard now also records its `Assets/Vendor/`, catching any file Unity
  rewrote there too. Evidence is 2.7 MB, kept small by saving at the grid's size.

### PX3

Done (bead ps-tfn.1.6). Commits: `e01e5dd9` (the baker, annotator, stills and tests), `af2b3142`
(each clip's anchor in the library), and this notes commit. `demo2d test` passes (74 of 74, 6 of
them PX3's); the choreography's dotnet tests pass on the measured library (18 of 18).
[Evidence](../../demo2d/evidence/px3/README.md).

- **Run:** from a worktree's root, after `demo2d setup`:

  ```
  uv run --project demo3d/tools demo2d unity BakeSprites      # Assets/Baked/Characters: an atlas and its JSON per type (31 s)
  uv run --project demo3d/tools demo2d unity AnnotateClips -out "$PWD/demo2d/evidence/px3"   # also rewrites Data/clip-annotations.json
  uv run --project demo3d/tools demo2d unity SpriteStills -out "$PWD/demo2d/evidence/px3"    # lineup and crowd, from the bake
  ```

- **The baker** (`Demo2D.EditorTools.Assets.SpriteBaker`): one scale per type (its walk's first
  frame stands `gameHeightPx`); each clip anchored at the walk's feet by matching silhouettes
  (43 of 44; the knight's shield-up bash uses its own feet); an exact area downsample onto grid
  pixels placed around the feet, alpha snapped at half coverage; per-frame crops shelf-packed into
  one point-filtered, uncompressed atlas per type (448×360 to 960×956, the boss 1856×1900; 7.6 MB
  in all). `Baked/Characters/<type>.json` gives each clip's size and pivot and each frame's
  `[x, y, w, h, ox, oy]` (atlas rectangle, offset in the clip's frame). Setup doesn't bake, as
  with demo3d's Unity bakers: run `BakeSprites` before anything draws characters.
- **The clip library,** `Data/clip-annotations.json` (`ClipAnnotator`), the core's annotation
  format plus `frames`, `impactFrame`, `endFrame`, `pivotPx`, `sizePx`, `anchor` and `method`.
  Impacts and ends from the motion between frames (strikes at the first peak within 85% of the
  largest; the slam on landing; hits and deaths at the first large motion, ending as they settle);
  strides from the planted foot's slide over a cycle (0.34 to 1.78 m; the lava beast and the boss
  have none and take two gait steps); the hero's root motion from the feet's travel (0.25 m for the
  slam, 0 otherwise). It annotates the clips its own file names, so choosing another clip is an
  edit and a re-run; a test fails if the file drifts from a fresh annotation.
- **Choices:** the skeleton attacks with `skill_3` (`skill_1` lunges 2 m and ends lying in an
  explosion), the skeleton mage casts with `skill_2` (`skill_1` ends collapsed); the hero is 66 px
  (60 was hardly taller than the grunts); the other heights stand as PX4 set them.
- **Lineup and readability:** every type at game size on the forest ground at night, and the hero
  in a crowd of about 130 at the base view. Each type reads by silhouette and colour; the night pulls four
  grunts toward one dark teal; the hero is findable only from his place and a warm light, so PX7
  or PX9 should mark him with light. Both judgements are in
  [Pending on the operator](#pending-on-the-operator).
- **For PX5 onward:**
  - **Frames carry their own motion.** One anchor per clip, so a lunge or fall moves within the
    frames. The sim moves the hero `rootMotionM` over a combo too, so the client holds the
    sprite at the segment's start for combo clips (0.25 m, the slam's, is the only nonzero one).
  - **Times are frame starts:** `impact = impactFrame / 15`; a clip of n frames lasts n/15 s.
    Walk and idle cycles are n − 1 frames (the last repeats the first).
  - **The art faces right** in every pack: flip for left.
  - **Uses' clips:** the hero's spin reuses `skill_1` (same impact as slashL) and the bash is
    `hit_defensive` (a shield block, impact on frame 1); PX8 may pick others.
- **Self-review:** re-read the diff against `origin/staging`. Outside demo2d's editor code, tests,
  data and evidence, the only change is the hero's `gameHeightPx` (the sim ignores it). New
  tracked files are our code, JSON, `.meta`s Unity wrote and tagged stills; the guard passes the
  tree (5,569 files). Unity's rewrites of two ProjectSettings files were discarded, as in PX2.

### PX4

Done (bead ps-tfn.1.7). Commits: `0861154f` (the data and the core's additions), and this
notes commit. [Evidence](../../demo2d/evidence/px4/README.md).

- **The data,** under `demo2d/Assets/Demo2D/Data/`:
  - `roster.json`, the catalog: the 13 types' ranks, footprint radii, gait steps, reach and
    hit points, the hero's five combos, three projectiles and four spells. Each type also
    names its pack, frame prefix and game height (grunts 48 to 58 px, elites 88 to 92, the
    boss 168) for PX3's baker, which may revise them; the sim ignores those fields.
  - `placeholder-annotations.json`: one entry per clip the choreography uses, naming the
    pack's frame sequence and its frame count, timed at a placeholder 30 fps until PX3
    measures them. Monsters walk with `walk`, attack with `skill_1`, flinch with `hit_1`.
    The hero's slashes are knight_male's `skill_1` and `skill_2`, the spin reuses `skill_1`,
    the slam is `jump` and the shield bash (which replaces demo3d's thrust) `hit_defensive`;
    PX3 and PX8 may pick others. `ChoreographyFiles` prefers PX3's `clip-annotations.json`
    once it exists.
  - `open-ground-layout.json`: the arena's areas as goals (camp glade, old stump, moss
    clearing, stone bridge, cliff ledge, great clearing) on open ground, closer together
    than demo3d's so the hero reaches four of the six goals; PX6 places them in the arena.
  - `action-level.json`: demo3d's sections, loads and grids, the 2D roster's mix (grunts,
    then waves, casters from drop-1, elites from groove-2, the boss at the final peak), views
    from the base 30 m to the climax's 40.5 m, the beam sweep for the laser.
- **Core additions** (both demos can use them; demo3d's song dump is still `09945e3b…`):
  - **The view's pitch** (`viewPitchDeg` in the level script, `ViewGeometry`'s optional
    pitch, `Timeline.View`): at 90° the counted and spawned-around view is the top-down
    16:9 rectangle instead of demo3d's 55° trapezoid. The pitch is not in the dump, so
    demo3d's stays identical.
  - **Casters:** a monster type with a `projectile` attacks from its reach; the blow
    releases the projectile on the beat and it lands on the first eighth note it can reach,
    where the hero parries it (choreography.md, "A monster within reach attacks").
  - **`TimelineChecks`:** demo3d's generic timeline rules moved from its tests into the
    package, so both demos' tests run the same checks; plus the section-count check below.
    demo3d's tests now call them (122 pass).
- **"demo3d's tolerance"** for the section counts: `TimelineChecks.SectionsHoldTheirTargets`
  checks pulsed sections after the opening whose target is at least the previous section's,
  within 25%, what demo3d's own song holds (its `build` averages 77% of its target).
  demo2d's worst held section is `build` at 87%; the climax averages 399 at 40.5 m.
- **Facing** stays the core's yaw; quantizing it to left or right is PX5's.
- **Casters' bolts** that leave just before a free section fly until its next accent, up to
  1.6 s (about 20 of 1,610); PX8 may thin casters' attacks there if it reads badly.
- **Unity:** demo2d has no project until PX1, so its EditMode run waits for PX1 (the asmdefs
  and `.meta` files are in place). demo3d's EditMode run (cold Library,
  windowless): its choreography assembly 122 of 122; 83 other passes, 10 skipped for this
  worktree's missing bake, 0 failed.
- **Self-review:** re-read the diff against `origin/staging`. The core's changes are opt-in
  (default pitch 55°, no `projectile` on demo3d's types), and the moved checks keep their
  conditions; `Parry` is the old inline block unchanged.

### PX5

Done (bead ps-tfn.1.8). Commits: `7fb693de` (the client, its tests and the design's choices),
and this notes commit. `demo2d check --player` passes (the guard, dotnet, EditMode 78 of 78 with
4 of them PX5's, the IL2CPP build). [Evidence](../../demo2d/evidence/px5/README.md).

- **Run:** from a worktree's root (the player's paths absolute):

  ```
  uv run --project demo3d/tools demo2d setup     # vendor, the song
  uv run --project demo3d/tools demo2d build     # bakes the atlases if missing or stale, then PlaybackData, then the player
  APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
  "$APP" --click --overlay                       # the operator: the song with a click and the counts
  "$APP" -batchmode -force-metal -logFile "$PWD/demo2d/Logs/px5.log" \
    --capture-stills "$PWD/demo2d/evidence/px5" --stills-at 8,116,172.5
  ```

  Flags: `--click`, `--start-at <s>`, `--seconds <n>` (of song), `--fixed-time`, `--freeze-at
  <s>`, `--overlay` (song time, section, on-screen counts against the target, view, upscale,
  frame time), `--capture-stills <dir>` with `--stills-at s,...` (fixed time, 1920×1080 tagged
  JPEGs), `--zoom target|soft`, `--no-feel` (no punch or shake), and `--test-card` (PX1's
  scene, which the timing spike's `--frame-timing` still uses).
- **The client** (`Demo2D.Runtime.Playback`): `DemoMain` (starts itself in a player; the clock,
  music, click, stills, overlay), `SongPlayback` (the timeline from the baked `PlaybackData`, then
  any song time drawn on request, shared with the tests), `SpriteEvaluator` (the package's
  `ClipSchedule`, which demo3d's TimelineEvaluator shares, drawn as flipbook frames: retimed
  attacks, hits and deaths from their onset, walks by distance, the hero idling after a still
  beat, hit-stop, flash, the death's fade; engine-free in `Demo2D.Choreography` since ps-tfn.2.3,
  so its frame tests run under `dotnet test`), `SpriteCrowd`,
  `PixelPipeline`, `CharacterAtlas` (PX3's atlas JSON at runtime; SpriteBaker's `Sprite` now
  calls it). `SongClock`, `ClickTrack` and `CameraFeel` are demo3d's, copied (they live in
  demo3d's runtime assembly, which demo2d can't reference; CameraFeel's shake is now 2D).
  `PlaybackBaker` (`demo2d unity BakePlayback`, and every build) indexes the baked song,
  atlases, data, shaders and the open ground into `Assets/Baked/Playback/Resources`.
- **Choices:**
  - **Sprites are pooled SpriteRenderers**, not hand-instanced quads: the 2D renderer lights
    them with its `Light2D`s and draws every type's atlas in one back-to-front order; an
    instanced draw per atlas couldn't interleave types by y. Flash is one shared material per
    level (`Sprite-Lit-Flash.shader`, URP's lit sprite plus `_Flash`, 4 steps), so no sprite needs
    its own property block. Tint is a per-monster brightness jitter (0.88 to 1). Facing is a
    flip chosen per segment from its yaw with hysteresis (sideways past 0.25), so a walk
    straight up or down keeps its side. Positions snap to whole texels.
  - **The draw order is explicit:** each frame ranks the sprites by the feet's texel row, then
    entity id, into their sorting order. Snapped feet often share a row, and the renderer breaks
    ties by its registration order, which pooling changes: a seek drew overlaps differently.
    The cost: sprites of different atlases interleave, so they batch less (PX10 measures).
  - **The zoom-out keeps the grid with a larger target** (README updated), from paired stills:
    a fixed 960×540 target at 40.5 m drops and doubles texels. The target is sized once for
    the song's widest view (1328×761 with a 16-texel margin) and drawn whole every frame, about
    twice the base view's pixels; the upscale window and `_Scale` follow the view and punch.
  - **The present pass** is a second camera drawing one quad (layer 31) with
    `SharpUpscale.shader` over the screen, offset by the sub-texel remainder of the whole-texel
    world camera, so the scroll is smooth. PX7's bloom and grade go on that camera.
  - **Stills are drawn in DemoMain's LateUpdate** (execution order 10000), on a frame set to
    exactly the still's time: a `Light2D` refreshes its culling bounds in its own LateUpdate,
    so a still drawn earlier after a seek lit the hero from his old place. PX7's light pool
    inherits this.
  - **A killing blow shows the death,** not the flinch (the sim gives both the same impact).
  - **The open ground** is PX3's stills' grass with moss and dirt patches over 96×120 tiles
    (`SpriteStills.GroundMap`, now shared), baked as tile indices; PX6 replaces it.
  - **A warm light follows the hero** (radius 4.5 m), so he reads at night; PX7 owns lighting.
    Casters' bolts are a generated 7-pixel spark a metre up until PX7's effects.
- **Evidence:** stills at the intro (8 s), the build (116 s) and the final peak (172.5 s); the
  zoom pair; a seek's stills identical to a run from 0 to the pixel (from 100, 116 and 172.5 s,
  including a still on the player's first frame); the impact-frame test over every attack, hit
  and death in the song.
- **Surprise:** the IL2CPP player's timeline differs from the Mono player's (3,817 entities to
  3,803), as choreography.md allows (deterministic per runtime only). Compare stills within one
  build.
- **For PX6 onward:** `SongPlayback.BuildGround` is the open ground's only use; the arena's
  tilemaps replace it. Effects and their lights (PX7) draw on the world camera's layers; anything
  on layer 31 is the present quad's. Edit-mode tests run no LateUpdate, so `Light2D`s keep their
  first culling bounds there; check lights with the player's stills.
- **Self-review:** re-read the diff against `origin/staging`. Outside demo2d's runtime, editor
  code, tests and evidence: `SpriteStills.Ground` split into `GroundMap` (same seed and patch
  count, so PX3's stills are unchanged), SpriteBaker's `Sprite` delegating to `CharacterAtlas`,
  StillCapture's readback shared and installed only for the test card, and the build baking
  `PlaybackData` first. Unity's rewrites of two ProjectSettings files were discarded, as in PX2.
- **Review** (Warden, bead ps-tfn.1.9, the foundations milestone PX1 to PX5): one test bug and
  three small baker problems fixed, in the commit `fix(px5): …` after `68f27368`; one follow-up
  filed. `demo2d test` 78 of 78, twice, one of them with a cold shader cache.
  - **The seek test failed in a fresh worktree** (163,503 pixels on "a seek forward"; it passed
    once in five runs). The editor's first draw in a session samples atlases it hasn't uploaded
    yet, so whichever still came first showed other types' pixels in the right sprite shapes. The
    player's stills are right (PX5's evidence, and a seek's first frame there matches). The test
    now makes a throwaway draw first. Async shader compilation was ruled out.
  - **A stale bake drew silently.** The build re-baked the atlases only when one was missing, so a
    changed `gameHeightPx`, clip choice or baker kept the old frames. It now re-bakes when an atlas
    is older than `roster.json`, `clip-annotations.json` or `SpriteBaker.cs`, and the annotator
    leaves an unchanged library's file time alone, so that check doesn't re-bake every time.
  - **Culture-dependent text in baked and tracked files:** the atlas JSON's pivots (negative ones
    are invalid JSON where the minus is U+2212) and the library's `anchor` and `method` strings now
    format invariantly.
  - **Frames larger than a clip's first** lost the pixels past its size (latent: every pack's
    clips keep one canvas). The library test now also checks `rootMotionM` against a fresh
    annotation, the one value the client applies by hand.
  - **Filed:** ps-tfn.8. No Unity EditMode test of either demo runs in fast or slow CI (only the
    dotnet choreography tests do), so PX5's seek and impact tests run only when an agent remembers.
  - **Checked and left alone:** the evaluator's retiming (impact frames land on their impacts),
    facing hysteresis, the explicit y-sort, the death's fade (two beats, inside the sim's 1 s
    corpse linger at *Action*'s 155 bpm), the sharp upscale's filter, the target sizing and margin
    against the 0.25 m shake, the 90° view geometry (the exact 16:9 rectangle), casters' bolts and
    the moved timeline checks. The skeleton mage's clips sit 1 grid pixel above their feet (its
    lowest row snaps clear); `hero spin` reusing `skill_1` is PX3's recorded placeholder for PX8.
    The guard's changes had their own reviews (ps-tfn.1.4, ps-tfn.1.10).

### PX6

Not started.

### PX7

Not started.

### PX8

Not started.

### PX9

Not started.

### PX10

Not started.
