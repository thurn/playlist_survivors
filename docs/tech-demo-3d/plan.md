# 3D Tech Demo: Implementation Plan

The demo's work is sixteen tasks, `TD0` to `TD15`, run on Cygnus as
[Working on Cygnus](windows.md#how-the-run-works) describes. This file is the
plan's only tracker: each task's status, notes and commits live in its entry
under [Task Notes](#task-notes). (The tasks were Hive beads `hv-50g9.1` to
`hv-50g9.16` until 2026-10-28, closed as superseded by this file.)

- **Plan keys** name tasks, commits' scopes and evidence directories.
- **Evidence** goes in `demo3d/evidence/<key>/` (lowercase).
- **Summaries** go in that key's entry under [Task Notes](#task-notes).

## Tasks

Each task lists what it builds and when it is done.

- **TD0, the song map.**
  - Builds: the *Action* song map under `songs/action/`, through the song-map
    workflow in [tech-demo.md](../design/tech-demo.md#building-the-song-map), using the
    full supplied MP3 rather than the earlier demo's source or crop. No score
    exists, so the expected form comes from the recording itself: its energy
    curve, spectrogram strips and onsets.
  - Hand-checks beats, downbeats, section transitions and accents, and
    identifies the final energy peak and the ending, without assuming a tempo
    ramp or free closing chords.
  - Records the source's identity (path, duration, SHA-256), the NCS usage
    terms and the required credits in `songs/action/source.json`.
  - Fixes the MP3-to-Ogg conversion command that setup uses (see
    [Sound](camera-and-sound.md#sound)), and checks that the converted audio keeps the map's
    beat alignment, encoder delay included.
  - Needs no Unity and no other task, so it starts at once. It runs on
    Cygnus if the analysis tool installs there under `uv`; if it doesn't,
    TD0 is the one task done on the Mac, and its commits are pushed to the
    relay's `demo3d` all the same.
  - Tracked output follows `songs/orpheus-galop/`: the song map, edit list,
    form notes and report summary. The audio stays out of git (see
    [What never reaches git](vendor-assets.md#what-never-reaches-git)). Report images stay in
    the analysis tool's gitignored work folder, since they carry no
    `ps-render` tag.
  - Done when: the map passes the analysis tool's `check`; `source.json`
    records the source's path, duration and SHA-256, the NCS usage terms and
    the credits; the conversion command and its measured alignment (encoder
    delay included) are recorded; no audio file is tracked; and a
    click-track listening check is in
    [Pending on the operator](#pending-on-the-operator).
- **TD1, foundations.**
  - Opens with [What TD1 verifies](windows.md#what-td1-verifies), including
    the [placed assets](windows.md#moving-the-assets). If setup is
    incomplete, it stops, and the operator's missing steps go to
    [Pending on the operator](#pending-on-the-operator).
  - Creates the relay's `demo3d` branch from remote master (see
    [The relay repository](windows.md#the-relay-repository)).
  - Builds: the Unity project on Direct3D 12; the worktree setup command and
    its cache on the Dev Drive; batch-mode task entry points; the
    [warm editor](windows.md#unity-work-by-agents); `.gitignore` coverage;
    the vendor guard and its pre-push hook; the `dotnet` project for the
    choreography layer.
  - Writes the guard, its tests and the setup command to run on both
    machines (Python under `uv`, or .NET console tools), so that
    [integration](windows.md#integration) only wires them into the Mac's
    checks.
  - Spikes, recorded in its notes:
    - Unity's cold batch-mode compile and test time on a warm `Library`, and
      the same loop through the warm editor;
    - IL2CPP and Mono player build times;
    - a windowed player at exactly 1920×1080 pixels on Cygnus's 4K desktop,
      reporting `FrameTimingManager` GPU times on Direct3D 12;
    - the setup command's clone time on the Dev Drive;
    - the C# version Unity 6.6 accepts.
  - Done when: one command sets up a fresh worktree on Cygnus; the guard's
    tests catch every failure case with synthetic fixtures (a vendor or baked
    path, an untagged binary under `demo3d/`, a recorded GUID, a recorded
    SHA-256, the NCS song's audio under `songs/action/` or
    `public/audio/action*`, a missing record); an empty scene builds a
    Windows IL2CPP player in batch mode; the spikes are recorded; and the
    guard's own review is done (see [Reviews](#reviews)). The
    [vendor guard drill](qa.md#vendor-guard-drill) runs once TD2 and TD4
    exist.
- **TD2, assets.**
  - Builds: the manifest with its source hashes, the sync tool and its
    record, with a record-only mode for the Mac's guard at integration; the
    clip annotator and its contact sheets.
  - Chooses: the roster, the hero, the effect candidates and the sounds, and
    the kit's pieces from the classic packs in [The kit](environment.md#the-kit).
  - Done when: every roster type's walk, attack, hit and death clips and
    every hero combo and locomotion clip are annotated (impact, end, stride
    or root motion), and a style still shows the roster and the kit together
    under night lighting, from the game camera.
- **TD3, choreography on open ground.**
  - Builds, from TD0's song map: the timeline types (with `Tick`), the choreographer with its
    framing track, kill-rate feedback, free sections and sound aggregation,
    the level-script and stress-script formats with the off-screen reserve,
    the sim tick timing, and the layer's tests.
  - Without walls, the hero walks straight toward each section's goal, which
    a test layout names; TD6 adds the flow fields.
  - Uses placeholder clip annotations: a tracked JSON file of plausible
    impact times, ends and strides per placeholder roster type. So it runs in
    parallel with TD2. TD4 switches to TD2's real annotations.
  - Done when: the song's timeline and every stress script's timeline build
    and pass the layer's tests on open ground, with a placeholder envelope;
    the song's timeline builds in under 3 s; and tick timing is reported.
- **TD4, client playback.**
  - Builds: the client's timeline evaluation (segments, action states,
    retiming and the two animation slots), the song clock, fixed-time mode,
    the bench scene, the development flags listed in
    [Manual QA](qa.md#manual-qa), and the stock baseline crowd and a simply drawn
    hero, all driven by TD3's timelines with TD2's annotations.
  - Also builds the capture tool: fixed-time stills and video pairs, with
    the `ps-render` tag.
  - TD7 builds the harness alongside it. Whichever of TD4 and TD7 lands
    second registers TD4's stress scenes and the sim tick timing with the
    sweep runner.
  - Done when: the [first assembled check](qa.md#first-assembled-check)
    passes; the capture tool writes tagged stills and video pairs in
    fixed-time mode; and the [vendor guard drill](qa.md#vendor-guard-drill)
    passes, recorded in the notes.
- **TD5, bone-texture crowd.**
  - Builds: the bone-texture baker, the instance pipeline, both skinning
    variants, culling, depth bins, the shadow extension and motion vectors.
  - Starts once TD2 lands, on a test scene that cycles the roster's clips at
    random phases. Its hookup to TD4's timeline evaluation waits for TD4.
  - Compares both variants with the stock baseline, as in
    [Against the baseline](crowd.md#against-the-baseline), using TD7's harness, and
    deletes the slower variant. This is the first sweep of a real crowd.
  - Done when: it draws S1 and S2 within the fidelity floor; the comparison
    (the largest on-screen count, the frame breakdown at 2,000, and
    implementation notes per arm) is in its notes; only the faster skinning
    variant remains, with the stock baseline kept as a harness option; and
    the crowd milestone's review is done.
- **TD6, navigation.**
  - Builds: the placement-list generator (areas, scatter rules, structures
    and the density multiplier) and footprint annotations for TD2's kit
    pieces; the obstacle map, the wall distance field, the time-sliced flow
    field toward the hero, the hero's per-section goal fields, and marching
    in place.
  - Done when: the navigation tests in
    [Automated Checks](windows.md#automated-checks) pass on the courtyard
    test layout, and the song and stress timelines still pass with walls,
    with tick timing still reported.
- **TD7, harness.**
  - Builds: the windowed benchmark player and the sweep runner with its
    fitted search, result rows, the quiet-machine lock and refusals, the
    overdraw counters and the PIX capture summary.
  - Confirms the Steel Nomad scores and the bandwidth figures the budget
    uses, with sources.
  - Starts once TD1 lands, in parallel with TD2 and TD3. Until TD4's stress
    scenes exist, it runs on a **synthetic load**: N instances of one stock
    mesh, GPU-instanced and moving, which is enough to exercise the search,
    the timings and the lock.
  - Done when: a sweep of the synthetic load produces result rows and a
    first frontier, and the
    [sweeps and machine lock](qa.md#sweeps-and-the-machine-lock) checks pass.
- **TD8, effects.**
  - Opens with the runtime spike in
    [Choosing the GPU runtime](effects.md#choosing-the-gpu-runtime), and records its
    decision. If VFX Graph wins and its templates need the operator, those
    templates go to [Pending on the operator](#pending-on-the-operator), and
    TD8 continues on the parts that don't need them.
  - Builds: the module inventory of the chosen prefabs, then the converter,
    scoped to that inventory (see [The converter](effects.md#the-converter)); the
    chosen runtime, light cues, clustering and the light pool. Converts the
    effect list under the design rules for a horde.
  - Done when: the spike's table and decision and the module inventory are
    in its notes and evidence; every listed
    effect runs on the runtime with an A/B still against its stock prefab;
    and the light pool's size is fixed.
- **TD9, the arena.**
  - Builds: the arena's layout parameters, with named goal areas for the
    level script; the scene generated from TD6's placement list, with the
    ruin walls, the walled courtyard and the density multiplier; the kit's
    conversion to URP Lit;
    the night lighting, lightmaps and static shadow map; and the
    post-processing stack. S6 becomes runnable.
  - Starts once TD4, TD6 and TD7 land, lighting TD4's stock crowd. Its crowd
    steps wait for TD5: the static shadow map in the crowd shader, and the
    anti-aliasing decision, since TAA leans on the crowd's motion vectors.
  - Decides MSAA against TAA. Both are full quality, so this is not a cut:
    the choice weighs cost against capture pairs of monster edges and spark
    trails, which also go to the operator for review.
  - Done when: the arena renders at every density (0.5× to 4×), S0 is
    measured at rung E0,
    and the anti-aliasing choice is recorded with its captures and its
    cost.
- **TD10, hero and presentation.**
  - Builds: the hero's playable graph, combos, spells, parries, local
    hit-stop, the client's punch and shake, and sound scheduling.
  - Done when: the song's first minute plays with every blow on its click
    (`--click`); laser ticks flash without flinch or knockback; and the
    mapped ending's accents get a hero slam each.
- **TD11, cuts.**
  - Adds FLIP scoring to TD4's capture tool and calibrates the A/B
    thresholds. Then tries LOD thresholds, shadow proxies,
    the 30 Hz clip rate, larger light-cluster cells, effect merging, and
    dropping glow quads in favor of bloom.
  - Done when: the calibrated thresholds are recorded with both anchors'
    scores, and each cut is accepted or rejected with its scores, its stills
    and video pairs in [Pending on the operator](#pending-on-the-operator).
- **TD14, environment rendering.**
  - Climbs the ladder in [Environment rendering](environment.md#environment-rendering),
    rung by rung, and runs the density sweep. Rungs E3 and E4 are cuts and
    use TD11's A/B thresholds; the other rungs can start as soon as TD9
    lands.
  - Done when: every rung is measured, kept or dropped with its numbers; the
    density curve is in its evidence; and S0 at the authored density fits the
    environment's ceiling.
- **TD15, the art pass.**
  - Builds: the reference stills from the packs' demo scenes, the six beauty
    shots, the readability measure and its floor, and the landmarks and
    dressing that meet [the art direction](environment.md#art-direction).
  - Starts once TD8 and TD9 land, dressing the scene at TD9's density. Its
    last step waits for TD14: spending the headroom TD14 frees, the richest
    scene that keeps S0 within its ceiling.
  - Done when: every beauty shot passes the checklist and the readability
    floor, S0 fits the ceiling, the shots are in its evidence and in
    [Pending on the operator](#pending-on-the-operator), and the scene
    milestone's review is done.
- **TD12, the frontier.**
  - Runs the full sweep with every point's compute estimate, at TD9's
    anti-aliasing mode; the other mode is measured only at the envelope
    point, for the report. Then it runs the PIX captures
    and per-pass estimates at the envelope point and its heavy-load point.
    Writes the report and the envelope, and puts the envelope into the level
    script.
  - Also runs S6 at the envelope, and reports the sim's tick timing at every
    frontier point and at S6. The report states whether the envelope holds
    among walls and whether the sim fits its reserve. If S6 fails at the
    envelope, the report gives S6's own frontier and what limits it.
  - Also carries TD14's environment results into the report: the density
    curve, the richness-to-monsters trade, and the final rung against E0.
  - Done when: the report is in its evidence and its summary in its notes;
    the level script carries the envelope; and the report states whether the
    envelope holds among walls (S6's own frontier if not) and whether the sim
    fits its reserve.
- **TD13, the demo.**
  - Tunes the level script so the climax holds the envelope and the full song
    passes. The song's climax adds the boss, the laser and denser spells to
    the demo load. If it fails, TD13 thins the climax's spells first and the
    boss's effects second, and never lowers the count. If it still fails, the
    envelope was wrong: TD13 lowers it in 5% steps and corrects TD12's report.
  - Builds the offline mixer and records the video capture with its mix. Adds
    the operator's viewing check to
    [Pending on the operator](#pending-on-the-operator).
  - Done when: the song run passes; the video with its offline mix is in
    `captures/`; and the answer milestone's review is done. Then the demo
    is ready for [integration](windows.md#integration).

## Order and parallelism

```
TD0, TD1    first, in parallel (TD0 needs no Unity)
TD2         after TD1
TD3         after TD0 and TD1, in parallel with TD2 (it uses placeholder
            clip annotations)
TD7         after TD1, in parallel with TD2 and TD3 (on a synthetic load)
TD4         after TD2 and TD3
TD5         after TD2; its timeline hookup after TD4, its baseline
            comparison after TD7
TD6         after TD2 and TD3 (it needs the kit's footprints)
TD8         after TD4 and TD7 (its spike runs S4 on the harness)
TD9         after TD4, TD6 and TD7 (it generates the arena from the
            placement list and measures S0 on the harness); its crowd
            steps after TD5
TD10        after TD5 and TD8
TD11        after TD5, TD8 and TD9
TD14        after TD9; its cut rungs (E3, E4) after TD11
TD15        after TD8 and TD9; its headroom step after TD14
TD12        after TD10, TD11, TD14 and TD15
TD13        after TD12
```

- **The graph is kept wide.** A task starts as soon as its first steps can,
  so a second session has work most of the time.
- **A step that waits.** Where a later step of a task needs another task
  ("its crowd steps after TD5"), the task starts without it. If the other
  task isn't done when that step comes up, the task records the wait in its
  notes, does its other steps, and finishes that step after.

## Reviews

- **One cold review per milestone,** by a fresh read-only subagent on
  Cygnus, using the warden review skill if the session has it, over the
  milestone's commits as its tasks recorded them in their notes. The
  milestone's last task requests it, fixes what it confirms, and records
  what was fixed or dismissed in its notes:
  - foundations: TD0 to TD4, TD6 and TD7;
  - the crowd: TD5;
  - the scene: TD9, TD14 and TD15;
  - the demo's systems: TD8, TD10 and TD11;
  - the answer: TD12 and TD13.
- **A review for the vendor guard** (TD1, and any later change to it),
  because a leak can't be undone.
- **Self-review first.** Before a review, the task checks its own diff for
  what its consumers need, claims its evidence doesn't back, and edge cases,
  as AGENTS.md's checklist describes, and records the pass in its notes.
- **The milestones become the squashed commits** at
  [integration](windows.md#integration).

## Working rules

- **Playable after every task.** Each task from TD4 on keeps the demo
  runnable on Cygnus and writes its run command in its notes.
- **Commit SHAs.** Each task records its commits in its notes, so a
  milestone's review covers exactly its tasks' commits.
- **No approval pauses.** Checks only the operator can make go to
  [Pending on the operator](#pending-on-the-operator).
- **Prototype rules.** Prefer the simplest option wherever it doesn't change
  a measurement.

## Pending on the operator

Checks and steps only the operator can do. A task adds a row and moves on;
the task that consumes the result removes the row once it has used it.

| What | How | Added by | Consumed by |
|---|---|---|---|
| Finish Cygnus's setup | Set the display to never turn off (`powercfg /change monitor-timeout-ac 0`); reboot once and check D: comes back, or add a startup task that attaches `C:\DevDrive\DevDrive.vhdx`; confirm Settings, Accounts, Sign-in options, "If you've been away" is Never; optionally enable XMP; delete the start-up checkout's untracked `native/evidence/windows-spike/` and `native/spike-windows/` (in git since `73530c7`) and the `D:\wt\windows-spike` worktree | Cygnus setup (2026-10-28) | TD1 |

## Task Notes

Each task's status, commits, run command and a few lines summarizing its
measurements, linking `demo3d/evidence/<key>/`. A task writes only in its
own entry, so parallel tasks don't collide. Status is one of: Not started,
In progress, Done.

### TD0

Status: Not started.

### TD1

Status: Not started.

### TD2

Status: Not started.

### TD3

Status: Not started.

### TD4

Status: Not started.

### TD5

Status: Not started.

### TD6

Status: Not started.

### TD7

Status: Not started.

### TD8

Status: Not started.

### TD9

Status: Not started.

### TD10

Status: Not started.

### TD11

Status: Not started.

### TD12

Status: Not started.

### TD13

Status: Not started.

### TD14

Status: Not started.

### TD15

Status: Not started.
