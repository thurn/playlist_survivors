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

## Task Notes

Each task's status, commits, run command and a few lines summarizing its
evidence, linking `demo2d/evidence/<key>/`. A task writes only in its own
entry.

### PX0

Not started.

### PX1

Not started.

### PX2

Not started.

### PX3

Not started.

### PX4

Not started.

### PX5

Not started.

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
