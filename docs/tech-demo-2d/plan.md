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
| Are PX3's clip annotations right? Each attack's and slash's impact on the blow, each hit's and death's end where it settles or rests, the slam's on the landing, and the walks' strides plausible for their gait (two are fallbacks: the lava beast and the boss have no planted foot). | Look over `demo2d/evidence/px3/annotations-*.jpg` (impact framed red, end blue); the numbers and methods are in `demo2d/Assets/Demo2D/Data/clip-annotations.json`. To change a clip or a proposal, edit the entry's `clip` and re-run `demo2d unity AnnotateClips`, or note the frame here. | PX3 | none: after PX10, an answer that needs a change becomes a bead |
| Does the song play on the click? Every step, blow, flinch and death landing on the beat, with no sprite popping, sliding or flickering between facings, and the scroll and zoom smooth. | After `demo2d build`, from the worktree's root: `demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/"Playlist Survivors 2D Demo" --click --overlay` (add `--start-at 160` for the climax). Note any section or second that drifts. | PX5 | none: after PX10 (PX8's listening check below covers the same run with sound) |
| Does the fight sound right, and does every blow land on its click? In the first minute (0 to 60 s) and the climax (160 to 199 s): swings, hits, deaths, parries and spells on the beat and not too loud against the song; the spins' and slams' hit-stop and the knockback hops reading as weight; the struck crowd's warm flash (PX7's white flash is gone) and the hero's cool parry gleam; the boss entering from the right at about 165 s, its hellfire parried, and its fall on the last slam. | After `demo2d build`, from the worktree's root: `"$APP" --click` and `"$APP" --click --start-at 160` (`APP` as in PX8's notes), then once without `--click` for the mix. `--no-sound` drops the effect sounds; `--no-feel` the punch and shake. Note any second that drifts or a sound that is wrong. | PX8 | none: after PX10, an answer that needs a change becomes a bead |
| Do PX9's beauty shots pass the checklist (one focal point, the hero readable, effects carrying light, no flat or muddy ground, no tiling repetition visible)? This also answers PX3's readability question (each type recognizable at game size, the hero findable in a crowd) and PX7's night question (too dark, too blue, the shafts too geometric, the downbeat rings too strong?), whose rows it replaces. | Look over the six stills in `demo2d/evidence/px9/` with its README's table (`before/` has PX8's look at three of the same moments), and `readability.txt`; or watch: `"$APP"` (`APP` as in PX8's notes; `--start-at 160` for the climax, `--no-night`, `--no-post` or `--no-effects` to compare). Note any shot that fails a point, and why. The values are in `night-lighting.json`, `effects.json` and `forest-tiles.json`. | PX9 | none: after PX10, an answer that needs a change becomes a bead |
| Does the whole demo hold up as a watch? The bundled app, start to end, once: the arc (a handful of skeletons, waves following the song's energy with room to breathe, about 400 at the final peak with the camera eased out, the horde cleared on the ending's accents), every moment reading as a finished game's, no hitch, pop or stutter (the song check found none above 25 ms windowless), and the sound in balance with the song. | After `demo2d build` (or the README's [Running the Demo](README.md#running-the-demo) commands), from the worktree's root: `open demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app`; it plays the song once at 1920×1080 and quits. Note any second that looks wrong, stalls or sounds wrong, and why. | PX10 | none: an answer that needs a change becomes a bead |

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

Done (bead ps-tfn.3.1). Commits: the arena commit `feat(px6): …` and this notes commit (SHAs on
the bead). `demo2d test` 84 of 84; dotnet 27 of 27, 6 of them PX6's. [Evidence](../../demo2d/evidence/px6/README.md).

- **Run:** from a worktree's root (the player's paths absolute):

  ```
  uv run --project demo3d/tools demo2d setup
  uv run --project demo3d/tools demo2d build     # bakes PlaybackData (now with the arena's textures and tiles), then the player
  APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
  "$APP" --click --overlay                       # the song in the arena
  uv run --project demo3d/tools demo2d unity ArenaStills -out "$PWD/demo2d/evidence/px6"   # overview and shots
  ```

- **The data:** `Data/arena-layout.json` (demo3d's ArenaLayout plus `ground`, `cliffs` and
  `water` with `bridges`; its note documents them) replaces `open-ground-layout.json` as the level
  script's layout. `Data/forest-props.json` is both the footprint table and the sprite catalogue:
  99 props (7 swaying trees, 3 dead trees, 2 stumps, 2 fallen branches, the great dead tree, 84
  plants), their rectangles and feet measured from the sheets' alpha. `forest-tiles.json` gains the
  plateau, channel and bridge blocks.
- **The code:** `ForestArena` and `ArenaTiles` (engine-free, `Demo2D.Choreography`) parse the
  layout, generate the placement list (the core's scatter, minus anything in water or on a cliff,
  plus a blocking polygon per cliff and per stretch of channel between bridges) and compose the
  Tilemap layers. `ArenaGround` builds the Tilemaps, `ArenaProps` the props (sway, culling, canopy
  fade), and `SpriteCrowd` ranks the visible props with the characters. `ForestTiles.CornerTiles`
  now delegates to `TileSheet.CornerTiles`.
- **Choices:**
  - **Generated at load, not baked.** The README planned an editor bake into `Baked/`; the arena is
    instead composed from its JSON when the player loads (a few ms), as the sim's placements are, so
    the drawn arena can't go stale against the navigated one (the Mason's note on the bead). Only the
    vendor textures and the sheet's tiles are indexed in PlaybackData, which every build re-bakes.
  - **Cliffs and water are rectangles** (README updated): the sheet's plateau block has only a south
    face and its channel block no corners, so a capsule or a bend couldn't be drawn.
  - **Ground materials** use the `clear-on-*` sets (inner terrain transparent), so each material
    is an overlay layer with soft edges over the base grass; diagonal corner patterns fill. Water keeps
    base grass within a tile, matching the channel block's own banks.
  - **The canopy fade** fades a whole prop at least 2.5 m tall to 45% by how deep a character
    standing behind it is inside its sprite (above its base), a function of the positions, so seeks
    match.
  - **The route:** camp glade, old stump, moss clearing, over the bridge, past the ledge's foot, into
    the great clearing, spaced to the hero's real pace so the stream and cliff stay out of the
    climax's view (when they were in it the final peak fell to 223 to 248 on screen).
- **Fixed on the way: the hero never moved.** Since PX3's measured annotations (no root motion for
  knight_male's slashes and bash), a hero within reach of the horde attacked in place all song: on
  open ground too, it never left the camp. The core's hero combos take an optional `advanceM` (the
  least the hero moves toward an unreached goal over the clip; demo3d sets none, and its dump is still
  `09945e3b…`), and demo2d's roster sets 0.5 m on the slashes and 0.8 m on the bash. The hero now
  reaches the stump at 19 s, the moss clearing at 54 s, crosses the bridge in the build and reaches the
  great clearing at 147 s. The cyclops weight went from 0.2 to 0.35 (build) and 0.3 (final peak):
  elites are a few per song, and the arena's run spawned none.
- **For PX7 onward:** props are lit with `Sprite-Lit-Default`; the umbrella plants are candidates
  for glowing plants. Sorting orders: ground layers from -1100, shadows -1, characters and props
  ranked from 0. Plateau tops are bare (PX9 can dress them; scatter skips terrain).
- **Self-review:** re-read the diff against `origin/staging`. The core change is the opt-in
  `advanceM`; demo3d's dotnet tests (129) and dump are unchanged. New tracked files are our code,
  JSON, `.meta`s Unity wrote and tagged stills; Unity's ProjectSettings rewrites were discarded.

### PX7

Done (bead ps-tfn.3.2). Commits: the effects commit `feat(px7): …` and this notes commit (SHAs on
the bead). `demo2d test` 89 of 89; dotnet 32 of 32, 5 of them PX7's. [Evidence](../../demo2d/evidence/px7/README.md).

- **Run:** from a worktree's root (the player's paths absolute):

  ```
  uv run --project demo3d/tools demo2d setup
  uv run --project demo3d/tools demo2d build     # PlaybackData now bakes the effect library, its prefabs and the night
  APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
  "$APP" --click --overlay                       # the song with effects and the night
  "$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px7/effects" --effect-stills
  uv run --project demo3d/tools demo2d unity EffectLibraryStills -out "$PWD/demo2d/evidence/px7/library"
  ```

  New flags: `--no-effects`, `--no-night` (no shafts, fireflies or plant lights), `--no-post`, `--empty`
  (the hero alone, unflashed: the arena's night), `--effect-stills` (with `--capture-stills`: a still per
  kind as it fires, listed in `effect-stills.txt`) and `--measure <json>` (fixed-time frames drawn at 1080p,
  each timed through a one-pixel readback, with the effect, particle and light counts).
- **The data:** `Data/effects.json`, the effect library: one CFXR prefab per timeline effect kind (all 12 of
  the core's `Catalog.Effects`, not only PX2's 8: hit, parry and death sparks, the five combo trails and
  shocks, ring, bolt launch and impact, lightning, laser) and one per projectile in flight (bolt, bone-bolt,
  blood-bolt), each with a scale (in proportion to the event's scale: rings and lightning carry their radius,
  combos the sim's shape scale, deaths the monster's radius), a tint, a layer (over the crowd, or on the
  ground), a lifetime and cap, an aim and a light cue. `Data/night-lighting.json`: the moon, the hero's light,
  nine shafts, the fireflies, the plants and the grade.
- **The code:** `EffectLibrary` (engine-free, `Demo2D.Choreography`, with its tests), `EffectLayer` (the pool),
  `LightPool`, `NightLighting`, `Glow.shader` (unlit additive HDR, for fireflies and the beam), and
  `EffectLibraryStills` (`demo2d unity EffectLibraryStills`). `SongPlayback.Frame` takes `still`; the world's
  pixel target is HDR and the present camera runs the post-processing.
- **Choices:**
  - **Effects follow the song time, exactly on stills.** The live set is a function of t (events whose
    lifetime covers it, in view, each kind's newest `max`). On a still every live instance is re-seeded from
    its event's seed and simulated from the event in 1/30 s steps following its anchor, so a seek draws the
    same particles (the seek test, now with effects, passes to the pixel). In live play a new instance is
    simulated to its age once and then plays on Unity's own particle update, which is free on the main
    thread; in the editor nothing plays.
  - **CFXR's scripts are stripped** from pooled instances (made under an inactive holder so they never wake):
    their clear behaviour destroys the instance and their lights and camera shake are 3D. Systems are set to
    hierarchy scaling, no play on awake and no stop action; the tint multiplies their start colour.
  - **Prefabs keep their own rotation.** Mesh particles lie in it (the fire ring is authored turned to face
    the camera); the first version reset it and drew the ring edge-on, i.e. not at all.
  - **The laser is a drawn beam.** CFXR has no beam, and its Electric Barrier, scaled to the 14 m sweep, drew
    short thick strokes at random angles. The beam is a jagged line (a hot core in a blue glow, re-jagged from
    the seed every 1/30 s) from the hero along the sweep, which turns once over the spell's bar (the sim's
    `bars × beats per bar × beat`); the barrier stays small as the crackle at his hand.
  - **Every effect draws in the pixel grid;** none opts out (`fullRes` is supported in the data, as a
    documented reason, but no full-resolution layer is drawn).
  - **Sorting:** effects over the crowd from order 30000 (fireflies at 29000), ground effects (the slam's
    shock) at -900, above the tiles (-1100) and under the shadows (-1); within a layer in event order.
  - **Lights:** each kind's cue (colour, radius, a linear attack and quadratic decay) goes to a pool that
    lights the brightest 28; fireflies and plants have pools of 24, placed only in view. The moon is
    `NightLighting`'s; the player removes the boot scene's.
  - **Bloom and grade on the present camera,** over an HDR pixel target: bloom threshold 1.25 (at 0.95 the
    hit flash's whites bloomed into one blob), a neutral tonemap, contrast, a cool white balance, a blue
    shadows lift and a vignette. The volume profile is made at runtime (URP's post-processing variants are
    not stripped).
  - **Fireflies** hover by the trunks of trees drawn by seed (a 3-texel cross with a small light, blinking),
    the plants that glow are the umbrella plants, and the shafts are freeform parallelograms on the clearings.
- **Measured** at the climax (160 to 176 s, everything on): mean 7.9 ms, p99 9.0 ms per 1080p frame
  including the GPU's work; effects add about 0.1 ms (0.2 ms at p99) with at most 110 instances, 3,540
  particles and 28 effect lights, the grade about 0.15 ms, the night's extras nothing measurable. The caps
  hold it there; PX10 measures the whole song.
- **For PX8 onward:**
  - **The hit flash dominates at night.** At its peak (the first 0.12 s after a blow) every struck monster is
    a white silhouette; a spin, a slam or the ending's clear turns the crowd into one white mass, and the
    hero turns white on every parry. PX8 owns the reactions (comment on its bead).
  - Rings fire on most downbeats and now read strongly (cyan, 9 m across); PX9 or PX10 may thin or soften
    them. Spin and slam are rare (5 and 3 in the song).
  - Effect stills: `--effect-stills` takes each kind's event nearest the focus; PX9's beauty shots should pick
    their own moments.
- **Self-review:** re-read the diff against `origin/staging`. Outside demo2d's runtime, editor code, data,
  tests and evidence: none (no core change; demo3d untouched). PlaybackData gains the effect data, prefabs
  and glow shader; `SpriteCrowd` gains `NoFlash` (only `--empty` sets it). New tracked files are our code,
  JSON, `.meta`s Unity wrote and tagged stills; the CFXR prefabs stay vendor (synced, referenced by the
  baked PlaybackData, never tracked). Unity's ProjectSettings rewrites were discarded.

### PX8

Done (bead ps-tfn.3.3). Commits: the presentation commit `feat(px8): …` and this notes commit (SHAs on
the bead). dotnet 37 of 37, 5 of them PX8's; demo3d's dotnet 129 of 129 and its song dump still
`09945e3b…`; `demo2d test` 94 of 94. [Evidence](../../demo2d/evidence/px8/README.md).

- **Run:** from a worktree's root (the player's paths absolute):

  ```
  uv run --project demo3d/tools demo2d setup
  uv run --project demo3d/tools demo2d build     # PlaybackData now bakes the sound bank
  APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
  "$APP" --click                                 # the song with its sounds and a click
  "$APP" --start-at 160                          # the climax: the boss, the beam, the ending's slams
  "$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px8" --stills-at 10,30,37.125,50,166,170,176,180,185,195.928,197.476
  ```

  New flags: `--no-sound` (no effect sounds; the music and click stay) and `--dump-sounds <json>` (the
  timeline's sound events with each one's clip and gain, demo3d's format).
- **What was already there** (PX4 to PX7 on the core): the combos on the grid (slashes on beats,
  spins on accents, slams on phrase downbeats, the shield bash), the spells (rings, bolts, lightning,
  the beam sweep), the sim's knockback slides, the beam's ticks (no knockback, a half flash), the
  ending's slams on every accent, monster hit-stop, punch and shake, and the death bursts. PX8
  checked them on the song and added what the 2D client lacked.
- **The boss never appeared** (in demo3d too): it entered off screen at the final peak and stood
  there all section, since the sim moves an off-screen monster only when it releases it to fill the
  view's deficit, nearest the edge first, and a full climax never reached it. The core gains an
  opt-in level-script field, `bossFrom` (a layout goal: the boss enters in its direction from the
  hero and always walks in; choreography.md); demo3d doesn't set it, so its dump is unchanged, and
  its own boss is filed as ps-tfn.9. demo2d's final peak sets `cliff-ledge`, the hero's own path in:
  north put the 5 m-tall boss's head out of frame where the crowd stopped it, and south was forest and
  stream. The boss is now a caster (reach 7.5 m, the `hellfire` projectile at 14 m/s, CFXR's large
  Fireball tinted magenta): a melee boss can't reach the hero through 400 monsters. It is in view
  from 164 s, casts on beats from 174.6 s (11 times, each parried where it lands), takes a lightning
  strike, and falls to the ending's last slam with the horde. Its attacks shake the camera as a
  full-strength blow.
- **The flash lights the sprite instead of whitening it** (PX7's concern): `Sprite-Lit-Flash` lerps
  toward the sprite's own colours times an HDR flash light, up to 0.85; warm (1.6, 1.38, 1.12) on
  monsters, a cool gleam (1.1, 1.35, 1.75) on the hero's parries. A spin's ring of struck skeletons
  reads as lit gold with every figure distinct, not a white mass; the brightest bone pixels bloom a
  little.
- **Hit-stop on the hero's big blows:** spins and slams (strength 0.8 and up) hold their impact
  frame for the core's 50 ms hit-stop and catch up over 0.15 s. demo3d slows its clip to 0.6×
  instead, which 15 fps frames can't show.
- **Knockback hops:** while the sim slides a struck monster back, its sprite lifts over its shadow
  in a 0.16 s arc, 0.16 m for grunts and a third of that for elites and the boss. Ticks don't hop.
- **Sound:** demo3d's SoundScheduler, copied (its assembly isn't demo2d's), with its clips from
  PlaybackData: demo3d's SoundBaker choices, one vendor clip per timeline sound name (24, all found
  in demo2d's manifest). 2,933 events over the song in the IL2CPP player.
- **Tests** (`PresentationTests`): the beam's 144 ticks flash at most half and push nothing; hits hop;
  the ending's accents are each a slam; the boss walks in within the section's first third, attacks
  at least 5 times with hellfire, and dies on the last accent; big blows hold their impact frame
  (5) where slashes move on (176). The casters' test counts the boss's hellfire.
  PX5's impact-frame test allowed 1 in 20 blows cut in by a later clip; the song already sat at
  4.98% under .NET, and Unity's Mono timeline crossed 5% once the boss walked in, so it allows 1 in
  16.
- **Left alone:** the hero's spin still reuses `skill_1` (knight_male has no spin clip; the blue spin
  trail carries it). The death bursts' smoke is dense at the climax; PX9 or PX10 may thin it.
- **Pending:** the listening check, with the first minute's and the climax's click, in
  [Pending on the operator](#pending-on-the-operator); PX5's click row and PX3's annotation row move
  to PX10, unanswered.
- **Self-review:** re-read the diff against `origin/staging`. The core change is opt-in (`bossFrom`)
  and demo3d's dump is byte-identical. Everything else is demo2d's client, data, tests and evidence;
  new tracked files are our code, `.meta`s Unity wrote and tagged stills; no ProjectSettings rewrite.

### PX9

Done (bead ps-tfn.3.4). Commits: the art-pass commit `feat(px9): …` and this notes commit (SHAs on
the bead). dotnet 38 of 38, 1 of them PX9's; `demo2d test` 95 of 95. [Evidence](../../demo2d/evidence/px9/README.md).

- **Run:** from a worktree's root (the player's paths absolute):

  ```
  uv run --project demo3d/tools demo2d setup     # now also syncs AncientForest's Demo.unity
  uv run --project demo3d/tools demo2d build
  APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
  "$APP" --click                                 # the song, as PX8
  "$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px9" --stills-at 6,30,89.35,119.7,166,176
  uv run --project demo3d/tools demo2d unity ReferenceStills -out "$PWD/demo2d/evidence/px9"
  uv run --project demo3d/tools demo2d unity Readability -out "$PWD/demo2d/evidence/px9"   # fails unless every type is distinct and the hero findable
  ```

- **References:** AncientForest's `Demo.unity` joins the manifest; `ReferenceStills` renders its camera
  and the whole scene (only the seven swaying trees: its two whole-sheet sprites point at sheets the demo
  doesn't sync). CFXR's demo scene uses built-in materials (pink under URP), so PX2's gallery stays the
  effects' reference.
- **The beauty shots** (6 s intro, 30 s first wave, 89.35 s caster volley with five bolts in view, 119.7 s
  an ogre's death, 166 s the boss walking in, 176 s the climax), each judged against the checklist in the
  evidence's table. The moments came from the timeline (bolts in view per 0.05 s, elite deaths in view), then
  from candidate stills around each.
- **What the first stills showed,** at PX8's build: the death smoke's white clouds buried the hero and most
  of a struck crowd; the hero couldn't be found at the climax; a strong cyan ring on most downbeats; one grass
  tile repeated over every clearing (the base layer drew a single tile); the shafts read as hard-edged
  parallelograms; the plateau's top a flat rectangle in the climax's view; the whole frame dim and blue.
- **The hero:** ranks as if he stood `HeroLeadM` (1.8 m) further south, so the grunts in front draw behind
  him (an elite's taller body still covers him), and four copies of his frame a texel off each way, in a flat
  warm colour (`Silhouette.shader`, in PlaybackData), rim him. Sorting orders now step by two to fit the rim.
- **The ground:** `forest-tiles.json` gains `baseSet` (the base grass varies over the plain tiles of the set
  whose outer terrain is that grass) and `details`, per transition set its outer terrain's 3×3 detail blocks
  from the sheet's ground fills, matched by mean colour. `ArenaTiles` gives a tile wholly of one ground a plain
  variant or, in value-noise patches (up to 70% where it peaks, one kind per patch), a detail tile;
  `ForestArena.ValueNoise` is the wobble's noise, shared. The plateau's top inside its rims (the block's own
  top is the base tile) varies the same way, and a `ledge-top` landmark of plants dresses it. Plants never
  block, and the song's timeline is unchanged: a hash of every entity's spawn, despawn and position every
  0.5 s matched before and after the dressing.
- **Effects and night** (values only): death smoke at 0.3 scale, 0.4 alpha, 0.7 s, at most 16; the ring dimmed
  to 0.45 to 0.75 of its colour, 0.8 s, at most 4, its light 0.8; bolts in flight 25 to 30% larger and lit
  brighter. Moon 0.32 (was 0.24) and less blue, the hero's light 5 m at 0.95, shafts at 0.1 with a 6 m
  falloff, exposure 0.25.
- **Readability measure** (`Readability`, `readability.txt`): per type the walk frame's lit colour (moon, and
  moon plus half the hero's light) in CIELAB and its alpha mask; distinct when the nearest type differs by ΔE
  ≥ 8 in both lights or overlaps ≤ 0.7. Every type passes on silhouette (largest overlap 0.59); by colour alone
  most don't (nearest ΔE 4.5 to 11 under the moon), confirming PX3. The hero's rim is ΔE 64 from the ground
  and 52 from the nearest type.
- **Pending:** the beauty shots' checklist, now one row in [Pending on the operator](#pending-on-the-operator)
  for PX10, replacing PX3's readability row and PX7's night row, which it answers.
- **Left alone:** the horde's shape (a tight disc of skeletons round the hero in the first wave) and the
  camera are the choreography's; the shafts are faint rather than reshaped; colour separation between grunts at
  night (silhouette carries them, and tinting types would fight the packs' art).
- **The scene milestone's review** (PX6, PX7, PX9) is the Warden bead ps-tfn.3.5.
- **Review** (Warden, ps-tfn.3.5, PX6 to PX9: the scene milestone and PX8): the code, tests, evidence
  and each task's Done-when were checked against the README. Fixed: the effect library's `fullRes`
  was parsed but nothing drew a full-resolution layer, so an entry setting it would still have drawn
  in the grid without a word; the library now rejects it until that layer exists, and the test that
  only checked its reason wasn't blank is gone. Dismissed, as deliberate and documented: the hero's
  1.8 m lead drawing him over a prop just in front of him; Readability's colour ΔE taken before the
  grade (relative, which is how it is used: silhouette carries the verdict); the client's beam sweep
  re-deriving the sim's duration from the roster JSON (it matches the sim's formula, and carrying it
  in the timeline would change demo3d's dump); SoundScheduler and CameraFeel copied from demo3d
  (they need Unity, which the engine-free package can't hold). The core's two opt-in changes
  (`advanceM`, `bossFrom`) leave demo3d's dump unchanged, re-run per commit; but PX8's note quoting
  `09945e3b…` is stale: `ea51af02` (demo3d's clear ring round the hero, outside this run) moved the
  dump to `71d7b6c7…` before PX8 landed, and PX8 kept that. Unity's EditMode tests still run only by
  hand (`demo2d test`, and the seek test only after `demo2d unity BakePlayback`); slow CI covering
  them is ps-tfn.8.
- **Self-review:** re-read the diff against `origin/staging`. No core change (demo3d untouched); demo2d's
  runtime gains the hero's rim and rank, its arena generator the detail tiles, its editor the two tasks; data
  edits are values and the ledge's pieces. New tracked files are our code, the shader, `.meta`s Unity wrote,
  tagged stills and `readability.txt`; the scene stays vendor (manifest only). Unity's ProjectSettings
  rewrites were discarded.

### PX10

Done (bead ps-tfn.5.1). Commits: `53d4cf85` (the song check, the prewarm, the run commands) and
this notes commit. dotnet 40 of 40; `demo2d test` 97 of 97. [Evidence](../../demo2d/evidence/px10/README.md).

- **Run:** the README's [Running the Demo](README.md#running-the-demo) (setup, bake, build, watch).
  The checks, from a worktree's root:

  ```
  uv run --project demo3d/tools demo2d build
  APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
  "$APP" -batchmode -force-metal --measure "$PWD/demo2d/evidence/px10/song.json"     # the song check
  "$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px10" --stills-at 6,30,89.35,119.7,166,176,195.928
  open demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app                              # the operator: watch
  ```

- **The song check passes.** All 11,940 frames are drawn at 1080p and timed to a one-pixel readback.
  Four runs of the final code: p99 8.8 to 9.0 ms (the limit is 16.7), worst frame 15 to 25 ms, always on
  the first frame, and no second over 25 ms. The peak has 383 on screen, 104 effects, about 3,030
  particles and 28 effect lights. The worst second after the first is 10 to 15 ms, in the final peak.
- **What failed first: stalls, not load.** The first whole-song run failed on two frames: 45 ms on the
  first frame and 43 ms at 176 s, in the final peak. Its p99 was already 8.9 ms. Both were first uses,
  a pool growing or a shader drawn for the first time. `SongPlayback.Prewarm` now runs at load:
  `EffectLayer.Prewarm` makes every kind's whole pool and shows one of each at the focus, and the
  start frame is drawn once. Without it, the same build session's runs still failed on the first frame
  (52 to 60 ms). The 176 s stall didn't recur in later runs, prewarmed or not; Metal keeps its compiled
  shaders between runs, so the prewarm is what a fresh machine needs. The prewarm costs nothing per
  frame: means of 7.0 to 7.1 ms with it and without it. The first run's lower 5.4 ms mean was the
  machine's state.
- **Nothing thinned.** At half the p99 limit, the spells, effect lights, level script and horde stay as
  PX9 left them. The effect schedule's dotnet counts weren't needed, since no cap changed.
- **One measurement path** (the Mason's M2 audit, on the bead): `--measure` gains FrameTimingManager's
  CPU and GPU times, each second's worst frame and peak counts, every frame over 25 ms and the verdict.
  `FrameTimingSpike`, its `--frame-timing` gate in `DemoMain.Runs` and `StillCapture`, and the test
  card's `--spike-sprites` walkers are deleted. PX5's note about `--test-card` serving the spike is now
  stale; the test card stays for the empty scene.
- **The GPU times.** FrameTimingManager reports a GPU time on 373 frames in batch mode (3%, as PX1
  found). Their p99 is about 20 ms, but that can't be one frame's GPU work: every frame waits for its
  own readback and finishes in about 7 ms on average. They seem to span the idle time between frames
  of a player that presents nothing. So the verdict uses the readback-timed frame, the CPU's and the
  GPU's work back to back, which is conservative. PX1's row asking whether a windowed player reports
  GPU time every frame is removed: it ran the deleted spike, and nothing depends on its answer now.
  The viewing check covers the windowed app.
- **Beauty stills:** PX9's six moments plus the last slam (195.928 s: the struck horde lit gold, the
  boss falling), from the final build. PX10 changed nothing that draws. Against PX9's stills, the intro
  is identical and the others differ in under 1% of their pixels.
- **The bundle:** `demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app` (IL2CPP, Metal), built by
  `demo2d build`. Builds stay out of git.
- **Pending:** the viewing check, a new row in [Pending on the operator](#pending-on-the-operator).
  The rows PX10 was to consume (PX3's annotations, PX5's click, PX8's listening, PX9's beauty shots)
  are still unanswered. They now say that an answer needing a change becomes a bead, since no task
  follows PX10.
- **The demo milestone's review** (PX8 and PX10) is the Warden bead ps-tfn.5.2.
- **Review** (Warden, ps-tfn.5.2, PX10 and how PX8 holds up over the whole song): the code, the
  evidence and the Done-when were checked against the README, on a fresh build. Fixed, with details
  in the [evidence](../../demo2d/evidence/px10/README.md#review-warden-ps-tfn52):
  - The song check's first frame (15 to 25 ms, once 0.26 ms under the limit) was the measurement's
    own first readback, not the game's. `--measure` now makes one untimed readback after the
    prewarm. The whole song's worst frame is 10.32 ms (`song-review.json`: p99 8.82, pass).
  - PX8's sound clipped in the live app: the music at 1.0 and each effect at its full event gain,
    with no limiter, went past full scale in 187 of 198 seconds (peak 3.25, offline from
    `--dump-sounds`). The player now mixes as demo3d's offline mix does: the music at 0.8, the
    effects at 0.45 of their gain, and a soft limiter on the final mix (`MixLimiter`, with an
    EditMode test). The viewing row's balance check now hears this mix.
  - The prewarm's spare render target is destroyed, not only released.

  Dismissed: the camera shake lands on about half the beats of the back half (0.1 m or more, mean
  0.11 m from 90 s on). It is the shared camera spec's formula, capped at 0.25 m, and the viewing
  check judges it. FrameTimingManager's batch-mode GPU times are rightly kept out of the verdict.
  PX5's stale `--test-card` note is history. `demo2d test` 98 of 98.
- **Self-review:** I re-read the diff against `origin/staging`. It touches only demo2d's runtime (the
  measurement, the prewarm, the spike's removal), the evidence, the README's run section and this
  plan. No core, data or demo3d change. New tracked files are our JSON and tagged stills. The guard
  passes the index. Unity's ProjectSettings rewrites were discarded.
