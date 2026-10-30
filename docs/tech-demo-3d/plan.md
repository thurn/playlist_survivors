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
| Finish Cygnus's setup | Reboot once and check D: comes back, or add a startup task that attaches `C:\DevDrive\DevDrive.vhdx`; confirm Settings, Accounts, Sign-in options, "If you've been away" is Never; remove the `D:\wt\windows-spike` worktree (`git worktree remove D:\wt\windows-spike`). TD1 found the start-up checkout clean apart from ignored build output | Cygnus setup (2026-10-28) | TD7 (a locked desktop refuses sweeps) |
| Listen to *Action*'s click track | Render the click track locally from a checkout with `songs/action/` (it contains the NCS audio, so never commit or share it). On Cygnus, `export UV_PYTHON_PREFERENCE=managed` first. Then: `ffmpeg -i "D:\vendor\songs\action\Alessia Labate, Conrad Taylor - Action [NCS Release].mp3" -map 0:a -c:a flac public/audio/action.flac` and `uv run --project tools/analysis ps-analysis click songs/action/songmap.json --out tools/analysis/work/action-click.wav`. (`D:\wt\td0` already has the WAV while the worktree exists.) Listen on speakers or headphones for: the 1 kHz beat click sitting on every kick with no flam or drift from start to end; the louder 1.6 kHz downbeat click on each bar's first kick; the 2.4 kHz section clicks at the changes in `songs/action/form.md`; the clicks keeping time through the two kickless breaks (34.1–37.2 s, 120.8–123.9 s), with the drops landing on the section click (12.4, 37.2, 123.9 s); the early hit on beat 3 of bars 82, 84 and 86 (126.2, 129.3, 132.4 s), with the click on the kick, not the hit. Also note whether the "stabs" (36.4, 123.2, 195.9, 197.4 s) are vocals. Record any mismatch with its song time. | TD0 | TD3 |
| Review the clip annotations | Open `demo3d/evidence/td2/contact-*.jpg`: each row is a clip in 8 frames, with the proposed impact framed in red. Note any attack whose red frame isn't the blow, or any hit or death whose red frame isn't the reaction, with its type and role (`evidence/td2/README.md` lists them) | TD2 | TD4 (it switches to the real annotations) |
| Decide the NCS licence for a public demo build | NCS's policy covers the capture video with its credit, but sends games to its commercial licensing form (`songs/action/source.json`, `terms`). Decide whether the Windows demo build stays private, or apply through NCS's form before sharing it | TD0 (recorded by the TD1 session) | integration (the demo build; TD13 kept it private) |
| Answer the Windows Firewall prompt for `playlistsurvivors3d.exe` | On the desktop since 2026-10-27 23:13, raised by a TD7 development build (`D:\wt\td7\demo3d\Builds\il2cpp-dev`, since deleted); the bench needs no network, so denying it is fine. Until then every sweep refuses at the foreground check unless run with `demo3d sweep --no-foreground`. Afterwards an agent re-runs `demo3d sweep demo3d/sweeps/synthetic-frontier.json` for a frontier with the foreground, and the display-off check with the window composited (turn the display off, start a sweep, expect `display was off, now on` and normal runs) | TD7 | TD5 (its first sweep) |
| Turn on Developer Mode for PIX's replay | Settings, System, For developers, Developer Mode (administrator). Then an agent runs TD12's two captures, `uv run --project demo3d/tools demo3d pix demo3d/sweeps/td12/s3-msaa4-demo.json --point n=591,viewWidthM=30,load=demo,aa=msaa4 --out <dir>` (the 30 m frontier point; TD13 lowered the envelope to 425) and `... demo3d/sweeps/td12/s3-msaa4-heavy.json --point n=417,viewWidthM=30,load=heavy,aa=msaa4 --frames 1 --out <dir>` (its heavy-load point), checks one passes.md against PIX's UI, and adds the per-pass estimates to `demo3d/evidence/td12/report.md` (performance.md, the per-pass estimate). TD12 found both capture (since it fixed `demo3d pix`) and stop at `E_PIX_FEATURE_REQUIRES_DEVELOPER_MODE` | TD7 | integration (TD12 tried: the compute estimate stands meanwhile) |
| The locked-desktop refusal | Lock the desktop (Win+L) while an agent session (Remote Control) runs `uv run --project demo3d/tools demo3d sweep demo3d/sweeps/synthetic-small.json`; expect exit 3 and `refusal.json` saying `desktopLocked`; unlock | TD7 | integration (TD12's and TD13's runs didn't need it) |
| Watch the first assembled check | From `demo3d/`, windowed: `Builds\\il2cpp\\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -job-worker-count 6 --click --overlay --seconds 60` (the envelope, 425, at the climax; the first minute holds about 60 to 160), then `--start-at 45`, then `--start-at 196` (the ending's accents). Expect every gait step, hit flinch and death on a click with no drift over the minute; feet that don't slide and crossfades without pops; overlay counts near the targets; the horde at 0:45 appearing at once. Note the song times of anything off | TD4 | integration (TD13's song check covers the timing numbers, not the eyes) |
| Watch the bone-texture crowd in motion | After `demo3d unity BakeCrowd` and `demo3d build`, from `demo3d/`: `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --crowd-bench --arm bone --scene s2 --n 800 --view-width 30 --seconds 60` (windowed). Look for feet sliding on the walks, pops at crossfades (into and out of attacks, hits and deaths), the hit flash's strength, the dissolve and its edge, and weapons leaving hands. Stills: `demo3d/evidence/td5/crowd-*.jpg` | TD5 | TD4 (its timeline hookup replaces TD5's scenario) |
| Judge the crowd's look against the packs | Compare `demo3d/evidence/td5/crowd-bone-s2-30m-crop.jpg` with the vendor demo scenes: the slime is drawn opaque (its vendor material is transparent), the goblin, bat and mushroom use albedo baked from InfinityPBR's colour-ID shader, and tints are placeholder near-white sets. Note anything that reads wrong | TD5 | TD15 (the art pass) |
| Review TD9's anti-aliasing pairs | Flip between `demo3d/evidence/td9/aa/*-msaa4-*-crop.jpg` and the matching `*-taa-*` crops (3× enlargements of the view's centre), and watch `D:\wt\demo3d\captures\td9\s3-msaa4.mp4` against `s3-taa.mp4` and `song-msaa4.mp4` against `song-taa.mp4` (2 s, fixed-time; full stills beside them). TD9 chose MSAA 4×: TAA left the horde's edges stepped. Note any moment where TAA looks better (shimmer on thin swords, the bolts' trails) or MSAA worse. Regenerate with `uv run --project demo3d/tools python demo3d/evidence/td9/aa_captures.py`. TD12 measured the frontier at MSAA 4× and found TAA now saves 38 % of S3's GPU time at the envelope point (TD9: 11 to 13 %), so this look decides more than it did | TD9 | integration |
| Judge the arena's night look | Flip through `demo3d/evidence/td9/stills/arena-e2-1x-*.jpg` (the six shot positions, empty) and the E0 set beside them; watch the song in the arena (TD9's run command). Note anything too dark, too flat or off-palette, props that float or clip, and whether monsters read in the static shade | TD9 | TD15 (the art pass) |
| Watch S6 in the arena | From `demo3d/`, windowed: `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --scene S6 --n 425 --view-width 30 --load demo --overlay --seconds 60` (the envelope, 425 since TD13; S6 held TD12's 500 by the numbers). qa.md, Walls and the sim: the horde flows round the courtyard's walls and through its gaps, never through a wall; jams at the gaps read as marching in place on the beat | TD9 | integration |
| Listen to the hero's blows on the click | From `demo3d/`, windowed with speakers on: `Builds\\il2cpp\\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -job-worker-count 6 --click --overlay --seconds 60`. Expect every sword blow, hit flinch and death on a click, swings and impacts (not a wall of identical sounds) at the dense beats, punch on accents and shake that grows with the hits but never jars. Note the song time of anything off | TD10 | integration (TD13's video carries the offline mix, not the live scheduling) |
| Review TD8's effect A/B stills | Open `demo3d/evidence/td8/ab/ab-*.jpg` (each: the stock prefab left, our GPU runtime right, at four times of its life, 2×2) and `demo3d/evidence/td8/song-141.5s.jpg`. Note any effect whose read differs: colour, size, timing, a missing layer (README lists the known approximations: curl noise, trails as stretched billboards, sword-slash shading, the laser's beam, skipped distortion). To see one moving: from `demo3d/`, `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --effect-gallery <dir> --effects <name>` | TD8 | TD11 (cuts) |
| Review TD11's accepted cuts | Open `demo3d/evidence/td11/ab/*.jpg` at 100% (each: the centre of the frame with the largest FLIP, A with every cut off left, B right): `shadow-proxy-lod2-*` (crowd shadows drawn from LOD2), `clips30-*`, and `combo-shadow2-clips30-*` (both: the defaults); `invisible-*` and `visible-*` show the two anchors for scale. Full PNG stills: `D:\wt\td11-review\captures\td11\<name>\still-*.png` against `...\base\`. Watch `shadow-proxy-lod2.mp4`, `clips30.mp4` and `combo-shadow2-clips30.mp4` against `base.mp4` in the same folder (2 s, fixed time, S3 at 600, 40 m; gitignored, so keep them with the worktree's other captures). Note any visible difference: shadows that shift, crawl or speckle on the monsters (self-shadowing from the proxy), 30 Hz stepping in fast attacks. The other runs' videos sit beside them (`lod-60-30`, `combo-lod60-clips30` and the rejected ones) | TD11 | integration (TD12 and TD13 used them as accepted) |
| Judge TD15's beauty shots | Flip through `demo3d/evidence/td15/shots/` (each of the six shots empty and with the horde: S3 at demo load, 2,000 requested, held at the shot), with `demo3d/evidence/td15/reference/` (the packs' demo scenes through our camera) and `readability.md` beside them; `canopy/` shows the canopy fade off and on. To see one moving, from `demo3d/`: `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --scene S3 --load demo --n 2000 --view-width 40 --hold-at 44,40 --seconds 30` (the shots' points are in `arena-layout.json`, `shots`). Note anything too dark, too flat or off-palette; mist that reads as smudges; runes or light pools too graphic or too bright; the horde not reading at a glance; effects lost in the set; props that float, clip or repeat; a canopy that hides the hero | TD15 | integration (TD13's video shows the scene in motion) |
| Watch the demo | Play `D:\wt\demo3d\captures\td13\demo-mixed.mp4` (1080p60, 199 s, with its offline mix; gitignored, and the NCS song keeps it off any share until the licence row is settled) at full screen with sound. qa.md, The full demo: the arc from a handful of monsters to the envelope (425) at the final peak (161 to 186 s); the hero fighting through the arena; every hit and death visible, no effect missing on the densest beats and no light flickering out; the ending's two accent slams (195.9 and 197.4 s) clearing the screen (the last slam's merged lights wash the frame orange: too much?); weight, not clipping, at the densest beats. Note the song times of anything off. To watch it live instead, TD13's run command | TD13 | integration |
| Judge the triplanar rock port | Open `demo3d/evidence/td14/stills/rock-triplanar-e2-1x-forest-edge-crop.jpg` (the big rock, centre top) beside Elven Realm's own demo scene: TD14 ported `SyntyStudios_RockTriplanar` to `Demo3D/RockTriplanar` (rock-wall sides projected on the object's axes, grass laid over where it faces up). Note if the grass cover is too strong or too weak, the texture scale is off, or the rock reads flatter than the vendor's | TD14 | TD15 (the art pass) |
| Watch the demo on the Mac | In a worktree that ran [Running on the Mac](mac.md)'s commands, with speakers on: `open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --overlay` (the whole song, windowed). Expect what [The full demo](qa.md#the-full-demo) expects on Cygnus, with the song in time and no missing effects or black materials under Metal. Then a video, which exercises the Mac's ffmpeg pipe: add `--fixed-time --capture-video "$PWD/captures/mac/demo.mp4" --ffmpeg /opt/homebrew/bin/ffmpeg --seconds 20` (an app started by `open` runs from `/`, without Homebrew on its PATH) and check the file plays. Note anything that differs from Cygnus's video | Mac support (2026-10-29; [evidence](../../demo3d/evidence/mac/README.md)) | the next Mac change |
| Re-check S0 and S3 frame times after the look pass | Epic ps-e8r (the demo3d look pass: warm ground, the hero's light, the crowd's key light, type colours, hot effects, the music-reactive post stack) changed what the GPU draws, measured only on the Mac. On Cygnus, in a set-up worktree at master: `demo3d unity BakeAll`, `demo3d build --backend il2cpp`, then `demo3d sweep demo3d/sweeps/td15-s0-e2-1x.json --out <dir>` against the environment's 1.5 ms GPU p99 ceiling ([performance.md](performance.md)) and `demo3d sweep demo3d/sweeps/td12/s3-envelope-fixed.json --out <dir>` against the envelope (425 at 30 m). Each bead's expected cost is in its evidence README (demo3d/evidence/look/*/README.md, "Cost"). Record both against TD15's and TD13's numbers; a miss files a bead to trim the look | ps-e8r.10 (the look pass's palette bead) | the next performance task |
| Review the look pass | Flip through `demo3d/evidence/look/final/contact-sheet.jpg` (the baseline left, the shipped look right) and the stills beside it, with the palette table in its README. Then watch it moving, windowed: on the Mac, `open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0` (or Cygnus's run command). Note anything off-palette, too dark or too bright; the horde not reading on the warm ground; cool accents that take over the frame; effects that clip flat or get lost; post pulses that jar | ps-e8r.10 | the next look task |
| Pick a monster style | Flip through `demo3d/evidence/styles/all/` (`song.jpg` and `beauty.jpg`: a column per `--monster-style` name, baseline first) and the per-style sets beside it, with `all/README.md`'s tuning table. Then watch each treatment in motion with the song, windowed, on the Mac: `open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --monster-style <name>` for painted, fire, crystal, spectrum, hologram, chrome, void and mix (add `--start-at 37` for the first drop, `--style-tune gain=0.8,exposure=0.5` to try other values without a rebuild). Judge the beat wave and pulses against the music, whether hit flashes still read (the grade caps them under fire and hologram), the deaths (burn, shatter, melt, implode, scan-out), fire's flames and crystal under motion, whether effects still read as the brightest thing, and whether the horde reads on the ground. Pick one treatment (or the mix, or baseline) for the demo, and note any per-type swaps | ps-a54.4 | the next look task |
| Judge the roster sets with sound | Flip through `demo3d/evidence/rosters/compare/` (`song.jpg` and `beauty.jpg`: default, forest and heroic from the same song moments, a column each) with its README. Then watch each set with the song, windowed, with speakers on, in a Mac worktree that ran the set's commands in [crowd.md, Roster sets](crowd.md#roster-sets) (`demo3d setup --roster forest --roster heroic`, `demo3d unity BakeRoster -roster <set>`, `demo3d build`): `open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --overlay --frame-probe --roster forest`, then `--roster heroic`, then without the flag. Judge the feel with the music: the closer camera and the smaller crowd (150 on screen at 18 m) against the default's 425 at 30 m, whether the monsters read and fight on the beat, the sink-away deaths, whether effects still read as the brightest thing (the forest glow is capped), and which set (or the default) the demo should keep. Confirm the frame probe holds 60 fps in both (the agents' probes were windowless or a single run); judge forest's Demon Wolf running as its walk and the Wererat's sword, which leaves its hand in TakeDamage (`evidence/rosters/forest/README.md`), and heroic's one 18-24 ms first-draw frame per run (`evidence/rosters/heroic/README.md`) | ps-f8a.4 | the next roster or look task |
| Judge the snow environment | Flip through `demo3d/evidence/look/env-snow/contact-sheet.jpg` (the forest left, snow right, shot for shot) with its README's palette table and findings. Then watch it with the song, windowed, with speakers on, in a Mac worktree that ran [Running on the Mac](mac.md)'s commands: `open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --overlay --environment snow`. Judge whether it reads as a frozen fjord at blue hour, whether the darker horde on white snow is readable in motion, the trolls (azure) against the shaded snow, whether the hits still read as the hottest thing (by value they are barely above the lit snow), and whether the hero's pool is warm enough. Re-check S0 on Cygnus at the authored density (the README's numbers are the Mac's windowless probe) | ps-bop.3.1 | ps-bop.4 (the environments' comparison) |

## Task Notes

Each task's status, commits, run command and a few lines summarizing its
measurements, linking `demo3d/evidence/<key>/`. A task writes only in its
own entry, so parallel tasks don't collide. Status is one of: Not started,
In progress, Done.

### TD0

Status: Done (2026-10-27, on Cygnus).

- **Commits** (branch `td/td0`): the song map, source record, evidence and
  this entry in `529cea9`; this SHA recorded in a follow-up commit.
- **Tracked output.**
  - `songs/action/`: `songmap.json`, `edits.json`, `form.md`,
    `report-summary.md` and `source.json`.
  - `demo3d/evidence/td0/`: `alignment.json` (the measurement) and
    `align.py` (the script that makes it).
  - No audio is tracked. The map's audio (`public/audio/action.flac`), the
    Ogg and the click track are gitignored, checked with
    `git status --ignored`.
- **The map's time origin** is the MP3's first gapless sample. The map
  references `action.flac`, which ffmpeg decodes from the MP3 using its
  Info/LAME header:
  - encoder delay 576 plus decoder delay 529, so 1105 samples are dropped at
    the start;
  - 352 samples of padding are dropped at the end;
  - the result is 8774479 frames (198.967778 s), stored losslessly as 24-bit
    FLAC.
  The full MP3 is used, with no crop. The commands are in
  `songs/action/report-summary.md`.
- **The map.**
  - Tempo and grid: a constant 155.000 BPM. A least-squares fit of 483 strong
    onset peaks gives 155.0002 BPM with a residual spread of 3.2 ms and no
    drift. The map has 514 beats on one exact grid from 0.00982 s, and 129
    bars of 4/4 (the last has 2 beats).
  - No tempo changes, no ritardando and no closing chord: the evidence shows
    none.
  - Bar phase: every sub-bass entry and cut falls on the map's downbeats.
    madmom's downbeat tracker is off by one to two beats after each break,
    and was rejected.
- **Sections** (bars):
  - intro 1–8;
  - groove (the first drop) 9–22;
  - break-1 23–24 (free);
  - drop-1 25–48;
  - breakdown 49–56;
  - groove-2 57–72;
  - build 73–78;
  - break-2 79–80 (free);
  - drop-2 81–104;
  - final-peak 105–120 (the final energy peak, from 161.042 to 185.816 s);
  - outro 121–126;
  - ending 127–129 (free: no kick; the last hit is a stab at 197.446 s).
  The phrases are 8 bars. There are 26 accents: `drop` ×3, `re-entry` ×5
  and `stab` ×5.
- **`check`: OK.** Onset alignment is 93.7–100% in the pulsed sections, with
  0% gap change. The one warning, an incomplete last bar, is expected: the
  file ends 0.77 s into bar 129. `check` also passes against the converted
  Ogg.
- **The conversion command** (for the worktree setup command):
  `ffmpeg -nostdin -y -i "<source.mp3>" -map 0:a -map_metadata -1 -c:a libvorbis -q:a 6 "<baked>/action.ogg"`.
  - Measured alignment: decoded and cross-correlated against the map's FLAC
    in four 10 s windows, the offset is 0.000 samples (0.00 ms; |lag| below
    0.002 samples) with an identical frame count.
  - A decoder that ignores the gapless header plays 1105 samples (25.057 ms)
    late, also measured.
  - The measurement was made outside Unity; TD3's click check confirms the
    origin in Unity.
- **Choices, with reasons.**
  - A constant grid over the trackers' beats: the trackers are quantized to
    10 ms, which broke smoothness, and the fit shows no drift.
  - The breaks and the ending are `free` with anchors (9 in all): they have
    no kick. The arrivals after them carry `drop` accents.
  - Each drop is one section with 8-bar phrases. Bars 81–88 alone fail
    alignment, because a hit lands about 32 ms ahead of beat 3 in bars 82,
    84 and 86. The beat stays on the kick.
  - Accents on the beat sit exactly on their beat's time, so an accent on a
    section's first downbeat belongs to that section.
  - Keys are left out of the map: chroma is too weak in the drops.
- **NCS terms** (in `songs/action/source.json`). The credit block comes
  from https://ncs.io/Action and was read online. The usage policy allows
  free use in videos with that credit. It sends *games* to NCS's commercial
  licensing form, so a public demo build needs the operator's decision. The
  Terms page returned 404, so its wording on redistribution is unverified.
- **Tool problems on Windows.** The tool itself was not changed.
  - `tools/analysis` pins `python-preference = "only-system"`, and Cygnus has
    no Python 3.12. `uv run --project tools/analysis ...` failed with
    `error: No interpreter found for Python ==3.12.* in search path or registry`.
    Workaround: `UV_PYTHON_PREFERENCE=managed`, which downloads uv's
    cpython-3.12.16 to `%APPDATA%\uv\python`. It is needed on every `uv run`
    on Cygnus.
  - The first `uv sync` from Git Bash then failed (once) with
    `error: Missing expected target directory for Python minor version link at C:\Users\dthurn\AppData\Roaming\uv\python\cpython-3.12.16-windows-x86_64-none`,
    although the junction was intact. The same `uv sync` from PowerShell
    succeeded, and Git Bash has worked since.
  - `report` writes `summary.md` and `disagreements.md` with Python's
    default Windows encoding (cp1252) and CRLF, so en dashes come out as
    invalid UTF-8. These are work files only; the tracked
    `report-summary.md` was written as UTF-8.
  - The tool's tests were not run, since the tool is unchanged.

### TD1

Status: Done.

- **Commits:** `6cf1db5` (project, tools, guard, dotnet), `b0a1a39` (cache keys),
  `37957b2` (spikes), `4b6e8d4` (the guard review's fixes), and this entry's commit.
- **Run:** in a fresh worktree, `uv run --project demo3d/tools demo3d setup`; then
  `demo3d test`, `demo3d build`, `demo3d check --player` (see `demo3d/README.md`).
- **Setup verified** ([spikes.md](../../demo3d/evidence/td1/spikes.md)): Unity probe exit 0
  with licensing initialized; .NET SDK 10.0.403; uv 0.13.6; PIX 2603.25; D: is ReFS
  (`DevDrive`); the vendor sources in place; the relay takes and deletes a test branch; High
  performance plan; DDR4-2133. D: surviving a reboot and the sign-in setting stay with the
  operator (the row below).
- **Built:**
  - The Unity project `demo3d/` on Unity 6000.6.7f1, URP 17.6 Forward+ with forced depth
    priming, HDR, MSAA 4×, soft two-cascade 2048 px shadows; Direct3D 12 only, IL2CPP, linear
    color, frame timing stats on. `Demo3D.EditorTools.ProjectSetup` writes it all and three
    EditMode tests hold it.
  - Assemblies: `Demo3D.Choreography` (no engine references), `Demo3D.Runtime`,
    `Demo3D.EditorTools`, and two test assemblies. `demo3d/dotnet/` builds the choreography
    layer and its tests from the same files with `dotnet test`.
  - `demo3d` tools in Python under uv (`demo3d/tools/`): setup, the vendor guard, the Unity
    task runner, the warm editor's client, and the sync record (TD2 finishes the sync tool).
    Batch entry points are `Demo3D.EditorTools.Tasks` (Noop, Configure, BakeAll, BuildPlayer).
  - The warm editor (`WarmEditor.cs`): TCP on localhost with a per-session token; job
    results land in files, so a domain reload mid-job loses nothing.
  - The per-machine cache at `D:\cache\playlist-survivors\demo3d\`: `roots.json`, the sync
    and derived records, and the vendor, baked and Library entries keyed by input hashes.
- **Choices:**
  - Python under uv for the tools, like the analysis tool: it handles tar and gzip natively,
    and `shutil.copy2` block-clones on ReFS (CopyFile2), so no .NET console tool was needed.
  - Localhost TCP with a token for the warm editor, since .NET named pipes in Unity's Mono
    are less reliable across domain reloads; result files make reloads harmless.
  - The record lists every file under every source root (69,266), not only synced ones, so
    a vendor file outside the manifest is still caught.
  - The C# layer targets C# 9 over .NET Standard 2.1 (no records), as Unity compiles.
- **Spikes:** cold compile and test 7.0 to 7.4 s, warm 0.8 s unchanged and 3.2 to 4.3 s
  after a change; IL2CPP build 101 s, Mono 27 s; a windowed player at exactly 1920×1080 on the
  4K desktop reports FrameTimingManager GPU times on D3D12; setup clones 16,086 Library files
  in 2.7 s, and a fresh worktree is ready in 46 to 49 s (first open 43 s, Bee rebuilding its
  graph for the new path); C# 9.0; `-logFile -` streams on Windows.
- **The guard's review** (a fresh read-only subagent): 8 findings, all fixed in `4b6e8d4`.
  1. Only set-up worktrees ran the hook: it now lives in the clone's shared hooks folder,
     refuses demo changes from checkouts without the tools, and setup refuses a
     `core.hooksPath` that would bypass it. 2. A malformed or stale record passed: the record
     is validated, cross-checked against the manifest, and rebuilt by every setup.
     3. Song audio under other paths: any `action*` segment under `songs/` or `public/`, any
     binary in `songs/action/`, and the song's hash. 4. Derived files: the derived record
     (edited copies with fresh bytes stay a policy-only rule, now stated in vendor-assets.md).
     5. `sha256Lf` is now required and always written. 6. Renders with trailing data or over
     4 MB fail. 7. Tests added for the shared hook, the launcher without tools, merges, tags,
     unknown remote SHAs and fetched remotes. 8. Malformed hook input and pushed blob tags
     fail with a clear message. 52 tests pass.
- **Checks before push:** the guard over the full tree passes (4,609 files, 4 s); 52 guard
  tests; `dotnet test`; cold EditMode tests 4/4; IL2CPP build.
- **Waits:** the [vendor guard drill](qa.md#vendor-guard-drill) runs once TD2 and TD4 exist
  (TD4 records it).

### TD2

Status: Done.

- **Commits:** the TD2 commit that adds this entry (its SHA is in the next entry's commit
  list, TD4's), plus `sync.py` and the record producer in TD1's `4b6e8d4`.
- **Run:** `uv run --project demo3d/tools demo3d setup` (syncs and imports the vendor
  folder), then `demo3d unity AnnotateClips` and `demo3d unity StyleStills`; `demo3d manifest
  find|add <source> <glob> --use ...` edits the manifest. The selection commands are in the
  manifest itself: each entry's `use`.
- **Built:**
  - The manifest (`demo3d/manifest.json`, 259 entries with source hashes and GUIDs) and the
    sync tool: Synty packages unpack once into the cache keyed by SHA-256, `unity_assets`
    files copy with their `.meta`, `guid:` references are followed within a root (scripts and
    scenes never), a source whose hash differs is refused, and `--record-only` builds the
    record without copying (the Mac's guard at integration). The closure is 673 files, 304 MB.
  - `demo3d manifest find|add`, which fills GUIDs and hashes from the sources.
  - The roster (`Assets/Demo3D/Data/roster.json`): each type's prefab and clips by GUID, its
    height in the demo (the prefab is scaled to it) and separation radius.
  - The clip annotator (`ClipAnnotator.cs`): samples every clip at 60 Hz with the editor's
    animation sampling and proposes impact, end, stride and root motion, writing
    `Assets/Demo3D/Data/clip-annotations.json` and tagged contact sheets.
  - `RenderTag` (runtime): the `ps-render` tag on JPEGs (a COM segment) and PNGs (a tEXt
    chunk), for every tool that writes images.
  - The style stills (`StyleStill.cs`): roster and kit under night lighting from the game
    camera at 30 m and 40 m, and the boss candidates side by side.
- **Chosen** ([evidence](../../demo3d/evidence/td2/README.md)):
  - Fodder: Magic Pig Games' Skeleton Warrior, Goblin, Slime, Bat, Mushroom Monster and Demon,
    scaled to 0.3 to 1.9 m. Elites: Fantasy Rivals' Big Ork and Troll at 2.9 m with their
    weapons, on Synty's sword-combat and locomotion clips (Humanoid retargeting). Boss: the
    Magic Pig Dragon at 4.5 m, over the Evil God, because its winged, non-humanoid silhouette
    reads at a 55 m view and doesn't look like a big elite.
  - Hero: POLYGON Knights' Knight 01 Orange (warm, against the cool set) with the broadsword
    and shield. Combos: LightCombo01 A/B/C slashes, HeavyStab01 thrust, HeavyFlourish01 spin,
    LightLeaping01 slam, Parry F; walk and run from Base Locomotion.
  - Kit: Fantasy Kingdom trees and rocks, Adventure solid-canopy trees and logs, Elven Realm's
    glowing tree and canopy, bushes, ferns and flowers, Dark Fantasy ruin walls, graveyard,
    braziers, candles, pyres and statues (environment.md's mix). TD6 and TD9 add pieces.
  - Effect candidates for TD8: Hovl sword slashes, magic hits, AOE lightning and explosion,
    AAA projectiles (nova orange, fire, electro), 3D lasers, magic circles; KriptoFX
    explosions 1 to 6. Sounds: Universal Sound FX sword hits and clings, armor impacts and
    creature squeals; RPG Magic 3 fire, electric and generic impacts; two thunder takes.
- **Annotations:** every roster type's walk, attack, hit and death, and every hero combo and
  locomotion clip, has impact and end (or stride). Synty marks each attack with its own
  `_Hit_` clip, and the proposed impacts land 0.0 to 0.1 s after it (a test holds this).
  Fodder walks cover 0.9 to 2.0 m/s, the elites 2.1, the hero 1.4 walking and 2.5 running.
  The slam (LightLeaping01) travels 7.1 m of root motion: TD10 decides whether that leap
  stays.
- **Choices:**
  - Impact is the peak speed of the fastest end bone relative to the body (hands, weapons,
    head, jaw, tail; every skinned bone on the slime and mushroom, whose bones are unnamed).
    Hits and deaths take the reaction's first peak, and death's end is where the body rests.
  - Stride comes from the body's own travel when a clip moves (the Magic Pig walks do), else
    from the feet's fore-aft range; humanoid combos measure travel on their root-motion twins.
  - Renders before TD9 swap Standard materials for in-memory URP Lit copies; nothing derived
    is saved.
  - Newtonsoft JSON (Unity's own package) for the editor tools.
- **Tests:** EditMode 10/10 (roster assets resolve, nine types and a hero, every used clip
  annotated with ordered times, Synty impacts within the vendor's hit window, both render
  tags); the tools' 58 tests (6 for the sync tool on a synthetic package and project).
- **What it doesn't show:** the annotations are proposals from motion alone; whether each
  blow *looks* like it lands on its frame is for the operator's eye on the contact sheets
  (row below). The stills are lit and graded only roughly: TD9 and TD15 own the look.

### TD3

Status: Done (2026-10-27, on Cygnus).

- **Commits** (branch `td/td3`): the layer, scripts, data and CLI in `7c9a1c3`; the tests and
  the sim fixes they forced in `4f2860e`; Unity's importer blocks for the data metas in `908b5c9`;
  the 10% overlap margin and the fill column in `98095e0`; the evidence and this entry in
  `e9a191f`, and the check's summary in `ba1e9be`.
- **Run:** `dotnet test demo3d/dotnet/Demo3D.Choreography.Tests` (77 tests);
  `dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song` (or
  `stress S3 --n 2000 --view 40 --load heavy --seconds 30`, or `timing`), with `--out <file>`
  `[--from S --to S]` for the debug dump.
- **Built** (`demo3d/Assets/Demo3D/Choreography/`, all C# 9 with no UnityEngine):
  - the timeline types of choreography.md (`TimelineTypes.cs`) and `Timeline`, with per-entity
    segment and action ranges, `PositionAt`, `FramingAt`, the name tables and per-section stats;
  - the choreographer (`Choreographer.Run` for the song, `Choreographer.RunStress` for a stress
    script, both over `Sim`): 60 Hz ticks, the framing track, the kill budget and shape
    feedback, free sections, spells, worst beats, refills with the off-screen reserve, and
    sound aggregation;
  - the song map and beat grid, the level-script and stress-script formats with the load presets,
    the clip library with retiming (`ClipLibrary.Fit`), the roster and the open-ground test
    layout, the camera's view geometry, a JSON reader and writer, the debug dump
    (`TimelineJson`) and `SimTickTiming`;
  - data in `Choreography/Data/` (TextAssets for the client, files for dotnet): placeholder clip
    annotations in choreography.md's format (`guid` "placeholder"; roles in `use`, such as
    "skeleton attack" and "hero slashL", so TD2's file swaps in), the placeholder roster
    (`roster.json`), `open-ground-layout.json`, `action-level.json` with the placeholder envelope
    (2,000 on screen at 40 m) and `stress-s1.json` to `stress-s5.json`;
  - the CLI `demo3d/dotnet/Demo3D.Choreography.Cli`.
- **For TD4:** `ChoreographyData.FromJson(annotations, roster, layout)`, `SongMap.Parse`,
  `LevelScript.Parse`, then `Choreographer.Run`. Segments hold until the next segment's start
  (a later one may supersede an earlier one's end; past its own `T1` an entity rests at `To`).
  Ground `(X, Y)` is Unity's `(x, z)`; facings are yaws, `atan2(dx, dy)`; hit sparks and death
  bursts are attached to their monster. **For TD7:** `StressScript.With(n, viewWidthM, load,
  seconds)`, `Choreographer.RunStress`, then `timeline.TickTiming.Summarize()` or
  `WriteJsonFields(JsonOut)` (`simTickP50Ms`, `simTickP99Ms`, `simTickMaxMs`, `simTickMeanMs`,
  `simTicks`, `simWarmupS`, `flowRebuildSlices`) and `timeline.Sections[0].MeanOnScreen`.
- **Tests** (77 under dotnet; 80/80 in Unity's cold EditMode run with the project's own 3), on
  the song and on S1–S5: byte-identical dumps (song by FNV-1a over 81 M characters, S5 by string),
  impacts within 1 ms of the grid (accents in free sections), gait steps on beats outside free
  sections, monsters moving in Linear segments in free sections, hero strides straight at the goal
  until reached and inside it after, deaths/s per section within 10% of the preset (the song's
  sections; stress after 5 s), each stress bar's on-screen mean within 5% of N, no two living
  monsters overlapping by more than a quarter radius (every beat, +0.1 s and +0.2 s), clips and
  effects on every hit and death, spawns at least 1 m outside the section's view, at most three
  voices per beat and category, tick timing reported, framing and zoom, the 3 s song build, and
  the inputs (JSON, grid, retiming, view, scripts, roster).
- **Measurements** ([measurements.txt](../../demo3d/evidence/td3/measurements.txt),
  [tick-timing.md](../../demo3d/evidence/td3/tick-timing.md)):
  - The song builds in 1.5 s in Release, 1.0–1.4 s under `dotnet test`, 2.6 s unoptimized, and
    6.6–7.0 s in Unity's editor (Mono, not the budget; the player was not timed).
  - Tick timing after 5 s: the song p50 0.015 ms, p99 2.9–3.0 ms; stress p99 about 1.5 ms at
    N = 1000 and 3.2–4.2 ms at 2000, so under dotnet the 3.5 ms line holds to about N = 1,800.
  - The song: 12,145 entities and 547,198 segments; deaths/s 0.92–1.08 of each preset.
  - What it does not reach: the song's on-screen counts lag where the hero travels (final-peak
    70% of 2,000), and stress fill tops out near 1,900 (S1) and 1,500–1,600 (S3) at 40 m, so
    the tracked stress scripts run N = 1,200. Hits/s fall short of 2× deaths in dense sections.
- **Choices, with reasons:**
  - A free section's grid is its curated accents plus its anchors (merged within 30 ms), and
    counts as beat, eighth and accent positions, not downbeats: break-1 then has four points
    instead of two, and spells on beats still fire there.
  - Deaths come from a per-section kill budget at each impact (the preset's rate up to midway to
    the next killing impact, or the section's end), and victims are the struck monsters with the
    fewest hit points, then the nearest. Hit points only order victims; the per-bar feedback scales
    the hit shapes instead (toward deaths plus hits, at most 2.5×, or 4× while deaths run behind,
    and at most a quarter of the view's width). Feedback on hit points alone could not hold 10%
    on short sections.
  - On screen counts alive and dying monsters in the view's trapezoid, for the song as for the
    stress scripts; the reserve (0.5× the target) waits 2–6 m beyond the view's edge and is
    released nearest the edge first, by the deficit plus 2.5 s of measured deaths. Culled
    monsters take 1.3× steps. Reserve monsters 30 m beyond the view despawn silently.
  - The opening horde is placed at t = 0 on a fodder lattice inside the view (it is exempt from
    the spawn test); all later spawns come on beats, 2 m plus their radius and 0–4 m beyond the
    edge of the view at the wider of the current and the section's width, weighted toward an
    unreached goal.
  - The view is the exact trapezoid the camera sees, with a fixed yaw looking along ground +Y.
  - Separation is exact for steps on the same beat (the closest approach of the relative path),
    every neighbour is checked at the chosen fraction, and the sim keeps overlaps within 10% of a
    radius, which leaves room for knockback transients under the test's 25%. A blocked monster
    tries one sidestep a beat, alternating sides, and marks time below 20% of its step. The dying
    never block.
  - Segments are never cut short: a death adds a zero-length segment, and a knockback starts from
    where the monster is, ends past where its step was going, no later than that step would
    have, and is checked at its end and halfway. A hit on a beat costs that beat's step.
  - The hero strides only when no monster is within reach ahead (within 75° of the way to an
    unreached goal), and attacks toward the goal until reaching it: with any monster in reach
    counting, the hero never left the start. Strides are always Pulse segments and attacks' root
    motion Linear, so the tests tell them apart. At 155 bpm the hero acts on every second beat,
    as the retiming rule implies.
  - Spells accept "/k" (every k-th occurrence) so presets can say "lightning@beat/3" (about one
    a second). Projectiles fly at most one beat, so their launch is never before the decision;
    bolts and lightning pick on-screen monsters. The laser sweeps once round per bar and ticks on
    eighths; ticks never kill and carry no effect.
  - The boss enters outside the view at its section's start, is left out of the budget, and dies
    to the first hero combo that strikes it in its section's last bar or later. S5's worst
    attacks land on the downbeats of bars 4, 8 and so on, and its ring kills the nearest 30%,
    counted against the budget.
  - Sound: per beat and category at most 3 voices at the instants with the most requests, gain
    min(1, (0.35 + 0.15 log2 n)(0.7 + 0.3 × strongest)), variants by seed.
  - Every tick advances every monster; the spatial hash is rebuilt on the first tick that queries
    it, which keeps quiet ticks cheap without changing any result.
  - Fodder steps are 0.6–0.95 m a beat (about 1.5–2.5 m/s at 155 bpm): slower crowds could not
    feed the demo preset's kill rate.
  - The layer's dotnet project builds with `Optimize=true` in every configuration, so the 3 s
    test times what a player runs.
  - TD6's hooks: monsters step toward the hero and the hero toward the goal's centre in
    `Sim.MonsterDecide` and `Sim.Stride`, where the flow fields plug in, and `SimTickTiming`
    already carries `flowRebuildSlices` (0 here).
- **The click-track check** in [Pending on the operator](#pending-on-the-operator) is still
  unconsumed: TD3 proceeds on TD0's measured alignment, and the row stays for its listen.
- **Checks:** `dotnet test` 77/77; cold Unity compile and EditMode tests 80/80 (84 s); the vendor
  guard fails on `songs/action/songmap.json` with the branch's own tools: setup copies the song
  map into `Assets/Baked/`, and TD1's derived record then lists our own file. Relay `demo3d`
  fixes this in `2697609` (TD2), which `td/td3` does not have yet. Running this branch's setup
  re-added the song map's hash to the per-machine derived record, which fails every worktree's
  guard; that one entry was removed again (backup in the session's scratchpad). With relay's
  tools (`git archive relay/demo3d demo3d/tools` into a scratch folder, run from this worktree),
  `demo3d check` passes: `check: vendor guard ok, dotnet test ok, unity editmode (cold) ok`
  (guard PASS over 4,689 files; dotnet 77/77; Unity 80/80 in 87 s, the song building in 7.0 s on
  Mono, worst overlap 0.18 of a radius). Merging relay `demo3d` brings the fix.

### TD4

Status: Done.

- **Commits:** `c8f705b`, `cc29023`, `25e385f`, `adf8bb9`, `0278488`, `3d9e740`, and this
  entry's commit; the TD0, TD3, TD6, TD7 and TD5 merges (`d101c75`, `f4b3dcb`, `7b3b409`,
  `3ad8f1a`, `7b49139`) carry TD4's integration fixes.
- **Run** (from `demo3d/`, after `demo3d setup`, `demo3d unity BakeAll` and
  `demo3d build --backend il2cpp`): `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920
  -screen-height 1080 -screen-fullscreen 0 -job-worker-count 6 --overlay` plays the song; add
  `--click`, `--start-at <s>`, `--seconds <n>`, `--envelope <n>`, `--crowd bone|stock|baseline`,
  `--scene S1..S6 --n <N> --view-width <m> --load <preset>`, `--fixed-time`, `--freeze-at <s>`,
  `--aa msaa4|taa|off`, `--stats <csv>`, and captures with `--capture-stills DIR --stills-at s,s`
  or `--capture-video captures\x.mp4` (windowed only). Stress scenes for the harness:
  `demo3d sweep` with `"scene": "S1"` to `"S6"`.
- **Built:**
  - The client's timeline evaluation (`TimelineEvaluator`): positions and facings from
    segments; each clip action retimed so its blow lands on its impact (ClipLibrary.Fit, speed
    0.75 to 1.33, from the previous action's end); the walk's phase following distance; two
    slots with a 0.12 s crossfade; 50 ms hit-stop and hit flash; a death's one-beat hold and
    one-beat dissolve. It is a function of song time, so seeks are exact.
  - The song clock (`SongClock`: PlayScheduled half a second after load, a least-squares fit
    of dspTime over 2 s, the predicted present) and fixed-time mode.
  - The bench scene (`BenchEnvironment`), the game camera (`GameCameraRig`), the demo player
    (`DemoMain`), the overlay, the click track and the flags in qa.md (`DevFlags`;
    `--show-lod`, `--overdraw` and `--show-nav` are parsed for TD11, TD7 and TD6's views).
  - The stock baseline crowd (`StockCrowd`: vendor prefabs, an Animator per monster playing a
    graph whose clip slots follow the instance's clips and times exactly) and a simply drawn
    hero on it; `CrowdFactory` picks `bone` (TD5's path, the default), `stock` or `baseline`
    (TD5's spec-literal arm).
  - The baked runtime data (`RosterBaker`, `PlaybackBaker` into `Assets/Baked/`), the switch to
    TD2's measured annotations (`ChoreographyFiles.LoadData`; the annotator now writes TD3's
    format), and the stress scenes S1 to S6 registered with TD7's harness (`TimelineScene`),
    with the sim's tick timing in each rebuild.
  - The capture tool (`CaptureTool`, `PipeProcess`): fixed-time tagged stills and 1080p60
    video streamed into ffmpeg, as A/B pairs.
- **Measured** ([evidence](../../demo3d/evidence/td4/README.md)): the whole song plays in the
  IL2CPP player; on-screen counts reach 0.61 to 1.05 of the level targets per section (low in
  drop-2 and the final peak; the ending doesn't clear, 945 against 500: TD13); evaluation costs
  about 1 ms of main thread at 1,300 live monsters; the timeline builds in 2.0 to 2.2 s (IL2CPP).
  A seek to 0:45 gives a byte-identical still to a run from 0:40.
- **The first assembled check** (qa.md), as far as an agent can run it: every blow's clip is at
  its annotated impact at the impact (an EditMode test over a 300-monster S2 timeline); hits and
  deaths start their clips on the blow; seeks are exact; the overlay's counts come from the same
  evaluation the stats log. Watching it with the click (drift, sliding feet, crossfade pops) is
  the operator's (row below).
- **Integration fixes found here:** death clips started well before the blow because TD2
  annotated a hit's or death's impact at the reaction's peak; the annotator now takes the
  reaction's onset (a fifth of the peak speed). The warm editor hung on a save-scenes dialog
  before tests; it now resets to a clean scene before tests and after each task.
- **The vendor guard drill** passed all four Cygnus cases
  ([vendor-guard-drill.md](../../demo3d/evidence/td4/vendor-guard-drill.md)).
- **Choices:** playables at the timeline's exact times for the client's stock crowd, rather
  than CrossFade calls on game time, which would drift off the song (TD5's `baseline` keeps the
  CrossFade arm for the frontier's stock comparison); captures windowed (above); plain C#
  evaluation until profiled; `--envelope` to run the song at the stock crowd's few hundred.
- **Tests:** EditMode 164/164 cold (6 for the evaluator); dotnet 112/112.
- **The foundations milestone's review** (TD0 to TD4, TD6, TD7; a fresh read-only subagent
  over each task's commits): 7 findings, all fixed in the review commit.
  1. The client refitted each attack's clip with its own chain, so starts and speeds differed
     from the sim's (41 of 184 hero combos started their clip before the sim's root-motion
     segment; most monster attacks played at 0.75): attacks now carry the sim's fitted start and
     speed (`ActionEvent.ClipStart`, `ClipSpeed`; timeline format 2) and the client plays them;
     dotnet tests check the blow lands on the impact and that each hero combo's root-motion
     segment starts with its clip.
  2. The harness dropped frames without a GPU time from GPU p99 (20% of a passing probe's
     frames): frames are now committed only once their GPU time arrives (within the 64-frame
     window), a run with more than 0.5% still missing is marked noisy, and the report states the
     share. TD7's frontier rows predate this; TD12's sweep uses the fix.
  3. Stress scenes looped their timeline at 45 s without looping the downbeat check: the stress
     timelines now run 120 s and the downbeat check maps through the same loop.
  4. The song clock: it now subtracts the mixer's output latency, adds one refresh of display
     latency, starts a seek sample-exactly (`timeSamples`), keeps 2 s of samples at any frame
     rate, and never runs backwards. Whether picture and sound line up in Unity still needs the
     operator's ears (the click-track rows).
  5. Hit-stop jumped after its hold, and the heavy-blow slowdown was missing: the clip now holds
     (or runs at 0.6x on the hero's blows of strength 0.8 or more) for 50 ms, then catches up
     over 0.15 s with no jump (an EditMode test).
  6. Setup's vendor cache ignored the sync tool's own logic: its key now includes
     `sync.SYNC_VERSION`.
  7. Sync dropped unresolved references silently: it now lists them (20 today, package assets
     such as URP's shaders and vendor script references) in the cache's `sync-unresolved.txt`.
- **A flaky dotnet run:** one `demo3d check` reported dotnet test failed while TD8 and TD9 were
  importing; the push went ahead without gating on it (my mistake: that push carried `be77a63`,
  whose tree passes 112/112 on rerun). Repeated runs fail about one in six under load; the
  failing test isn't identified yet and is followed up in TD8's window.

### TD5

Status: Done (2026-10-28, on Cygnus). Its hookup to TD4's timeline evaluation waits for TD4.

- **Commits** (branch `td/td5`, from `070cd4c`): `4695c9b` (the baker, both skinning variants,
  GPU culling, the shaders, the stock arm, the bench player and the tests), `6afb8f4` (the
  per-vertex variant deleted; the buffer views), and this entry's commit (evidence and notes).
- **Run:** in a set-up worktree, `demo3d unity BakeCrowd` (writes only `Assets/Baked/Crowd/`; also
  the `crowd` baker of `demo3d unity BakeAll`), `demo3d build --backend il2cpp`, then from
  `demo3d/`: `Builds/il2cpp/PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080
  -screen-fullscreen 0 -job-worker-count 6 --crowd-bench --arm bone --scene s2 --n 2000
  --view-width 40` (`--arm stock` for the baseline; `--seconds`, `--warmup`, `--out rows.jsonl`,
  `--search --probes a,b,c`, `--stills DIR [--still-widths 30,40,55] [--crop] [--live
  --fixed-time]`, `--lights N`, `--aa taa`, `--show-motion`, `--show-normals`, `--sun-elevation`,
  `--ablate main|shadows|skin|extension`). The measured plan: `uv run --project demo3d/tools
  python demo3d/evidence/td5/bench.py OUT.jsonl breakdown search ablations extension stills`
  (it holds TD7's machine lock; it was run before the per-vertex arm was deleted).
- **Built:**
  - The baker (`Editor/Crowd/CrowdBaker.cs`): per drawn roster type (the nine; not the hero or
    the rejected boss), a combined mesh at demo scale (every active skin plus rigid attachments,
    weapons included; 4 bone indices in UV1, weights in UV2), one RGBAFloat texture of skinning
    matrices (bone world × bind pose relative to the root, 3 float4 rows per bone, every clip at
    60 Hz with both ends; a looping clip's horizontal root drift removed), the clip table (first
    row, frames, length, loop, TD2's impact, end and stride, and each clip's bounding sphere),
    crowd materials, and the stock arm's prefab (the same mesh on one SkinnedMeshRenderer, an
    AnimatorController with a state per clip). InfinityPBR's colour-ID shader (goblin, bat,
    mushroom) is evaluated per texel into a plain albedo. 17 MB of bone textures in all.
  - The crowd (`Runtime/Crowd/BoneTextureCrowd.cs`, `Runtime/Crowd/Shaders/`): `ICrowdRenderer`
    unchanged. `Instances()` returns the GPU buffer locked with `LockBufferForWrite` (write every
    field; a type not requested in a frame draws nothing; tint 0 means white); slots are stable
    and the buffers double-buffered for motion vectors. Per type and frame: a culling compute
    pass (the frustum, and the same sphere swept along the sun by its longest shadow; 8 depth
    bins, one indirect command each; LOD by projected height with 10% hysteresis and a 0.2 s
    dithered crossfade, LOD0 only until TD11; a shadow list for the proxy draw; on-screen
    counts), the palette pass, then flat `RenderMeshIndirect` calls: one main draw (no shadows)
    and one shadow-only draw per type and material, 20 in all. One HLSL shader with
    UniversalForward (Forward+ through URP's cluster loop, cascaded soft shadows, ambient from the
    global probe, tint, hit flash, dissolve with an emissive edge), DepthOnly, DepthNormals,
    ShadowCaster and MotionVectors (the previous instance and palette). `CrowdStaticShadow()` is
    TD9's hook.
  - The stock arm (`StockCrowd.cs` here; renamed `BaselineCrowd` at the merge, since TD4 has its
    own timeline-exact `StockCrowd`; `CrowdFactory`'s "baseline"), kept as the harness option: a GameObject per monster, an
    Animator crossfading (`CrossFadeInFixedTime`) when a monster's clip changes, one
    SkinnedMeshRenderer, a Burst transform job. GPU skinning is now on in `ProjectSetup`
    (`meshDeformation` GPU batched; it was CPU). TD4's stock crowd was not merged, so this is
    TD5's minimal one.
  - The test scene and windowed player (`Runtime/Crowd/Bench/`): flat ground, the default sun, the
    game camera; every monster walks its own circle (walk phase from distance, smooth turns); S2
    adds, per monster every 7 s, an attack, a hit (flash, flinch, knockback) or a death (clip,
    1-beat hold, 1-beat dissolve, then a refill that dissolves in); 10% more monsters stand just
    off screen. A Burst job writes the instances. It stands in until TD4's timeline evaluation
    and TD7's harness take over.
- **The comparison** (40 m; [evidence](../../demo3d/evidence/td5/README.md)):

  | Arm | S2 max: placed (on screen) | S1 max | Limit | At 2,000, S2: GPU / main / render p50 (p99), ms |
  |---|---|---|---|---|
  | Palette (kept) | 1,395 (1,295) | 1,232 (1,255) | GPU | 5.21 (5.78) / 0.68 (1.05) / 0.39 (0.61) |
  | Per-vertex (deleted) | 1,237 (1,142) | 1,196 (1,214) | GPU | 5.83 (6.42) / 0.85 (1.30) / 0.48 (0.74) |
  | Stock (option; BaselineCrowd) | 726 (651) | 748 (764) | main | 3.79 (7.72) / 23.89 (28.85) / 1.82 (2.55) |

  - **Palette wins** the rule (the larger S2 maximum at 40 m, +13%) and is 0.4 to 0.6 ms faster
    on the GPU at 2,000 in both scenes. Each maximum passed a full 20 s run and 5% more failed.
  - **No arm passes 2,000 on screen at 40 m**; the bone-texture path is GPU-bound near 1,300.
    Ablations show why: the rest pose with no skinning still costs 4.8 ms at 2,000 (skinning adds
    0.45 ms with the palette, 1.0 ms per-vertex). The roster averages 5,700 triangles a monster,
    drawn four times (depth prime, forward, two cascades), and shadows cost as much as the main
    draw. TD11's LODs and shadow proxy are what move the frontier; the mechanism is in place.
  - **Stock Unity** is cheaper on the GPU at 2,000 (it skins once a frame and draws plain
    vertices), but 2,000 Animators and renderers cost 23 ms of main thread.
  - **What it doesn't show:** every run was marked noisy (other sessions used 14% of the CPU on
    average against the 10% line); a breakdown run and its repeat differ by up to 0.10 ms of GPU
    p50 and the repetitions by up to 0.18 ms, and 16 of its 24 rows had another player running
    (TD4's batch captures), against the palette's 0.4 to 0.6 ms lead; no PIX per-pass split;
    30 and 55 m, TAA and the packing ceiling were not searched. TD12 re-measures on TD7's harness.
- **Implementation notes per arm:**
  - *Palette:* about 1,400 lines of runtime C# and HLSL shared with the per-vertex variant, plus
    about 90 of its own (the blend kernel; slot-indexed palettes with frame stamps, so a slot new
    to the view takes this frame's pose for its motion vectors). It struggled with nothing on the
    floor.
  - *Per-vertex:* about 15 lines of its own: 48 texel loads per vertex in every pass, and again
    for the previous pose in MotionVectors. Simplest, but every pass pays for the skinning.
  - *Stock:* 220 lines plus 70 in the baker. Workarounds: the vendor prefabs have up to 148 skins
    (the skeleton), so the arm uses the baker's one-skin prefab; URP Lit has no dissolve, so a
    dissolving monster hides at half-way; hit flash is a per-renderer property block (breaking
    batching while it flashes); Animator time follows the instance only at clip changes.
- **Choices, with reasons:**
  - The palette is indexed by stable slot and grown on demand, not sized to the capacity (12,000
    slots × 108 bones would be 62 MB per palette for the dragon alone).
  - Bounding spheres per clip, around the vertical axis so yaw needs no recompute; a crossfade
    takes a sphere holding both clips. Type-wide spheres (a death sprawls to 3.3 m on the
    skeleton) counted far too many monsters on screen.
  - The shadow-only draw holds every caster; its vertex shader skips skinning outside each
    cascade's box (no measurable gain here; kept, since it costs nothing).
  - The slime is drawn opaque (its vendor material is premultiplied transparent); the dragon keeps
    two materials; materials sharing a texture share a submesh.
  - Tints are placeholder near-white sets (TD2 defined no colour sets); scale jitter 0.9–1.1.
  - The bench is standalone (`--crowd-bench` on the empty scene), so it needs no tracked scene,
    and `bench.py` stands in for TD7's sweep runner, sharing its lock.
  - Shaders live in `Runtime/Crowd/Shaders/`, so no folder `.meta` collides with TD7's new
    `Assets/Demo3D/Shaders/`.
- **Fidelity floor:** every visible monster (and every shadow-casting one) is skinned every frame
  with a 2-clip, 2-frame crossfade; real casters in both cascades, including monsters just off
  screen (25 more at a 25° sun); hit flash, death clip, hold and dissolve with an emissive edge;
  Forward+ point lights (a 48-light still); ambient from the global probe; motion vectors with
  skinned motion (a still); depth bins near to far; weapons rigid on the hand bone; tint and
  scale jitter. Not yet: SSAO itself (the DepthNormals pass is verified; TD9 adds SSAO), the
  static shadow map (TD9), LOD1 and LOD2 meshes (TD11).
- **Tests:** EditMode 24/24, TD5's 14: the texture layout, the clip table against the roster and
  annotations, a sampled frame's matrices against the rig and the combined mesh against Unity's
  own `SkinnedMeshRenderer.BakeMesh` on a multi-skin MPG rig and a humanoid with a weapon (under
  1 mm), the colour-ID conversion, the baked library, the culling arithmetic, the scenario's
  placement and continuity, and the compute culling pass against the CPU.
- **Checks before push:** `demo3d check --player`: vendor guard (4,691 files and the index) ok, dotnet test ok, cold EditMode 24/24, IL2CPP player build ok.
- **Tool problems:** the warm editor blocked on the test runner's save-the-scene dialog after a
  task left a modified scene (`DisplayDialog: Scene(s) Have Been Modified ... This should not be
  called in batch mode`; the job never returned; once). The baker and TD5's tests now leave an
  empty scene. `BuildPlayer` with `-backend mono` saves ProjectSettings with Mono as the backend
  (its restore isn't saved), which fails `ProjectSetupTests` cold until `Configure` runs.
- **Self-review** (before the crowd milestone's cold review): downstream needs, done (TD4: the
  contract is unchanged and its semantics are in `BoneTextureCrowd`'s docs; TD9: the static
  shadow hook, verified motion vectors and DepthNormals, the cascade-split observation; TD11: the
  LOD and shadow-LOD mechanism, though `CrowdType` holds LOD0 only and the 60 Hz rate is a
  constant, `CrowdLibrary.ClipRate` and `CROWD_RATE`, that its 30 Hz cut must make per type; TD7
  and TD12: the stock arm and the bench can register as stress scenes); claims, done (every
  number is in `rows.jsonl` or `ablations.jsonl`, with what they don't show above); edge cases,
  done (a type with no instances draws nothing, capacity overflow and a second lock in a frame
  throw, a hidden or zero-scale slot is culled, a new or regrown slot has no previous pose, the
  first frame has none); tests against the source, done (Unity's own skinning, the CPU culling);
  concurrency, done (jobs complete before the buffers unlock; the stock transform job completes
  in Draw).
- **Review** (the crowd milestone's cold review, after the merge `7b49139`; fixes on `td/td5-review`
  in `5e83737` and the commit adding this bullet):
  1. Capacity: fixed by the integrating session (callers initialize with 16,384).
  2. Slots weren't stable (TimelineScene and DemoMain wrote by list position): fixed.
     `Runtime/Crowd/CrowdSlots.cs` keeps entity → slot per type with a free list; a freed slot is
     written Hidden for a frame before reuse, and the count passed to `Instances()` is the
     high-water mark. Both callers use it; StockCrowd and BaselineCrowd hide Hidden slots.
     Tests: `CrowdSlotsTests`.
  3. Frames in flight: fixed. The CPU-written instance buffers are a ring of `maxQueuedFrames + 2`
     (at least 4); palettes and stamps stay double-buffered, being GPU-only.
  4. `PreviousOf` took a Hidden or unscaled previous instance: fixed; such a slot (and one new
     this frame) uses the current instance and no previous palette.
  5. Claims: confirmed and restated here and in the evidence README (run-to-run up to 0.10 ms,
     repetitions up to 0.18 ms, 16 of 24 breakdown rows with another player running).
  6. The skin ablation ran the palette pass: fixed (`SkipPalette` under `--ablate skin`) and
     re-measured: 5.22 ms with skinning, 4.80 without, so palette skinning costs 0.42 ms in all
     (the blend pass is within noise); the README says what the original rows include.
  7. BaselineCrowd started clips at 0 and ignored ClipB, TimeB, Blend and Tint: it now starts with
     `PlayInFixedTime` at TimeA and restarts a slot's next owner. ClipB, TimeB and Blend stay
     ignored (the Animator crossfades on its own from the clip change) and Tint too (a property
     block per renderer breaks batching): biases in the baseline's favour on CPU, stated in its
     docs.
  8. The matrix test recomputed the baker's formula: added `ThePalettePassBlendsTheBakedFrames`,
     which reads the GPU palette back for a crossfade, a looping clip past its length and a
     non-looping clip past its end, against the bake's frames interpolated on the CPU (under
     1e-4). The BakeMesh comparison already tied the bake to Unity's own skinning.
  9. LOD clock and owners: fixed. `BoneTextureCrowd.Clock` (TimelineScene, DemoMain and the bench
     pass their timeline time); a crossfade ignores a clock that sought back; the culling pass
     marks a slot's LOD state unset when its previous frame was Hidden or it is new, so a new
     owner starts at its own LOD with no crossfade; `RenderParams.camera` is the drawing camera.
  10. BaselineCrowd's root leaked: fixed (it owns and destroys its root when given none). The
      bench's `--arm stock` rows in `evidence/td5/` are BaselineCrowd, not TD4's StockCrowd.
  - Also found: after the merge, DemoMain started beside `--crowd-bench` and quit the player at
    `--seconds`; it now stays out of the crowd bench.
  - Checks: `demo3d check --player`: vendor guard (4,917 files) ok, dotnet test ok, cold EditMode 169/169, IL2CPP build ok.

### TD6

Status: Done (2026-10-28, on Cygnus).

- **Commits** (branch `td/td6`): the footprint measurer and its data in `4c62ae1`; the layout
  parameters, placement list, obstacle map, flow fields and the sim's navigation in `6854258`; the
  navigation tests and the fixes they forced in `f5b0524`; sidesteps that fan queues out, and the
  timing tables with S6, in `12dc580`; Unity's metas in `c8835cc`; the evidence and this entry in
  `0c6c7da`, and this commit list in the next.
- **Run:** `dotnet test demo3d/dotnet/Demo3D.Choreography.Tests` (112 tests). The CLI
  (`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release --`): `placements
  [--layout arena|courtyard-test] [--density D] [--out list.json] [--png map.png [--field] [--px P]]`;
  `stress S6 [--png snap.png --at T --span M]`; `song [--png ... --at T] [--hero]`; `timing
  --repeat 2`; `field --layout L --sx X --sy Y --px X --py Y` (one cell's cost and heading).
  `uv run --project demo3d/tools demo3d unity MeasureFootprints` re-measures the footprints.
- **Built** (`demo3d/Assets/Demo3D/Choreography/`, C# 9, no UnityEngine):
  - `FootprintMeasurer` (editor): for every manifest entry whose `use` starts `kit`, the prefab's
    colliders (else meshes) cut 1 m above the ground; the hull's minimum-area rectangle is the
    footprint, or a circle round it when nearly square. Pieces under 0.5 m tall, under 0.15 m in
    equivalent radius, or soft (grass, flowers, ferns, small rocks, candles) do not block. Also each
    piece's height and canopy reach. `Data/footprints.json`: 85 pieces by GUID, 66 blocking.
  - `ArenaLayout` (`ArenaLayout.cs`; format in `Data/arena-layout.json`'s note): areas typed
    clearing, path, edge, forest (`fill`), landmark, structure; prop groups with weights; seeded
    jittered-grid scatter per type (spacing, scale, yaw, chance, `keepCanopyOff`, `maxSlopeDeg` kept
    for terrain); edge bands a seeded 3–6 m round all fighting ground; landmarks' explicit pieces;
    structures `courtyard` (size, yaw, 3 or 4 gaps of `gapM`, `closedSide`) and `ruin-wall` (from,
    to, gaps), laid from wall pieces end to end along their local X; named areas with `goal {at,
    radius}`; `shots`; `decorationDensity` scaling edge and forest spacing by 1/√density. `Generate`
    gives the `PlacementList`: pieces first, then path, clearing, forest and edge scatter, so the
    fighting ground's clutter is the same at every density.
  - `Data/arena-layout.json`, the 160 m arena: clearings camp (the start), glade, courtyard-field
    (holding the 16 m courtyard with four 5 m gaps), brazier-glade and opening (35 m, for S1–S5);
    a path; landmarks glowing-tree, crystal-ring (six standing stones), graveyard, brazier-glade and
    opening-ruin; the ruins-gate wall with a 6 m gate; forest everywhere else. 1,417 placements, 828
    blocking (2,088 and 1,353 at density 2). `Data/courtyard-test-layout.json` is the test layout.
  - `NavGrid` (`Navigation.cs`): blocking footprints as exact world shapes in 2 m buckets; the 0.5 m
    obstacle map (320 × 320) whose cells hold the exact clearance to the nearest footprint or the
    arena's edge, capped at 2.5 m (blocked under 0.25 m); an uncapped distance transform of the
    blocked cells; exact `Distance`, `PushOut`, `LineClear` and a conservative `Clear(a, b, r)`.
    About 20 ms per layout.
  - `FlowField`: 8-neighbour Dijkstra with 10/14 step costs and no corner cutting, by bucket queue
    (Dial's), so `Step(budget)` time-slices it with the same result; `Begin` from a point,
    `BeginArea` from a goal; `Direction` is the cost's gradient over four cells, or the way to the
    cheapest neighbour where one is unreached.
  - In `Sim` (only when the layout has placements; on open ground a run is TD3's): the field toward
    the hero (cells with over 0.4 m clearance, reaching 70 m by the way), built whole at load and
    rebuilt once the hero stands 1 m from its source, one rebuild at a time, sliced over the beat's
    ticks (`flowRebuildSlices`; new `flowRebuilds`); a goal field per named goal at load (0.55 m
    clearance); monsters and the hero head straight where `Clear` finds the line open, else down
    their field; steps slide along walls (four pushed-out quarter steps) and end with the full radius
    kept; knockbacks, spawns, the opening horde and the boss's entry respect walls and reachability;
    bolts need a clear line and monsters attack only across one; planning goes nearest first by the
    way to the hero; marching in place as before, a zero-length Pulse segment on the beat.
  - S6 (`Data/stress-s6.json`): S3 at demo load holding the courtyard. Stress scripts gain `layout`,
    `hold` (a named area) and `openingInnerM`; S1–S5 now say `"layout": "arena", "hold": "opening"`.
- **For TD9:** `PlacementList list = ChoreographyFiles.LoadPlacements(root, "arena", density)`, or from
  TextAssets `ArenaLayout.Parse(layoutJson).WithDensity(d).Generate(FootprintTable.Parse(footprintsJson))`.
  Instantiate `list.Props[p.Prop].Guid` at each `Placement`: ground `(X, Y)` is Unity's `(x, z)`,
  `Yaw` is radians with Unity's Euler y = `Yaw × 180/π`, `Scale` is uniform. `ArenaLayout.Areas`,
  `Classify(p)`, `FightingDistance(p)`, `EdgeWidth(p)`, `Goals()` and `Shots` give the ground's types
  (for materials), the goals and the camera positions; `list.ToJson()` is the dump. The format may
  gain fields; keep the existing ones' meaning. Refine the art freely, but keep every gap wider than
  the largest monster's diameter plus 1 m (`Validate` throws) and re-run the tests.
- **For TD4, TD5 and TD7:** the timeline format is unchanged. `ChoreographyFiles.LoadData(root)` now
  loads the arena with `footprints.json`; from strings, `ChoreographyData.FromJson(annotations,
  roster, layout, footprints)` (the 3-argument form still takes the open-ground layout). A
  zero-length Pulse segment is a monster marking time: play the walk in place. Tick timing adds
  `flowRebuilds`. S6's walls ration its screen: read `Sections[0].MeanOnScreen`, not N.
- **Tests** (112 under dotnet; 121/121 in Unity's cold EditMode run). New: every kit piece has a
  footprint; the placement list is deterministic, keeps blocking pieces and forest canopies off
  fighting ground, scales only edge and forest with density, and names every level-script goal; both
  courtyards have exactly their gaps, each wide enough, and no slits; the flow field equals an
  independent sorted-set Dijkstra on every cell of three small layouts at any slicing and from goal
  areas, and walking its headings arrives; nobody stands inside a wall in the song, S1–S6 and both
  courtyard runs (deepest 0.022 m inside a radius, tolerance 0.05 m); on the courtyard test layout
  every monster with a path reaches the hero (80, from behind the closed side, inside the U and
  beyond the long wall; the last at 19.5 s), none goes 4 beats without gaining 0.25 m by the
  reference search unless in a crowd, and in a dense run 84% of gait steps mark time, every one
  beside a neighbour, with 12 reversals in 35,444 steps and no zig-zags; S6 has monsters inside the
  walls on every beat. Changed: a hero stride heads straight at the goal along a clear line, else
  within 60° of its goal field's way downhill; S5's run mean within 20% (15% on open ground); S6's
  bars never overfill and its mean reaches 75% of N.
- **Measurements** ([tick-timing.md](../../demo3d/evidence/td6/tick-timing.md),
  [measurements.txt](../../demo3d/evidence/td6/measurements.txt), maps in
  [evidence/td6](../../demo3d/evidence/td6/README.md)):
  - The song builds in 1.87–2.13 s (best under `dotnet test`, and the CLI; TD3 1.6 s back to back)
    and in 9.1–9.5 s on Mono. Its ticks after 5 s: p50 0.015 ms, p99 4.4–4.8 ms, max 20–24 ms; 84
    flow rebuilds in 1,968 slices (about 120 ms in all). Each section's deaths/s stay within
    0.92–1.07 of its preset (build 0.92 and breakdown 0.94 the tightest).
  - Stress back to back with TD3: S1 at 2000 p99 2.9 ms (TD3 3.2), S3 at 2000 5.2 ms (TD3 4.4; 4.15 on
    open ground through TD6's code), S1 at 1000 1.6–2.3 ms (1.8). The full table ran under other
    sessions' load and reads higher (S3 at 1000 2.8 ms, S6 at 1000 4.3 ms).
  - S6 at N = 1,200 holds a mean of 1,018 on screen (85%; bars 939–1,139) at 0.98 of its kill rate:
    the courtyard's inside is the hero's kill zone, fed through four gaps, so the screen holds about
    1,000 to 1,100 at 40 m whatever N.
- **Choices, with reasons:**
  - Footprints are rectangles or circles from colliders cut at 1 m: walls and tombs need their
    length, trunks and rocks are round, and the cut leaves out canopies.
  - Exact shapes for wall checks, the grid only to gate them: a 0.5 m grid alone is off by up to a
    third of a fodder radius, more than the test's tolerance.
  - Straight headings by line of sight, not by comparing the field's cost with the straight
    distance: beside a gap the cost through it is within a few percent of straight, and monsters
    walked into the wall next to it.
  - Near walls (within 8 m by the distance transform) a blocked monster tries both sides and a wider
    80° sidestep, keeping to its last side until it marks time: one sidestep left queues filing
    through gaps in single file and two touching monsters locked, and alternating wide sidesteps
    zig-zagged. Away from walls, or with 5 neighbours touching, TD3's single sidestep stays: the
    extra tries doubled the tick cost in open crowds.
  - Rebuild on 1 m of hero movement, reaching 70 m: halves rebuilds and cells against a rebuild per
    0.5 m cell, and monsters near the hero head straight anyway.
  - The level script follows the arena: the final peak leaves the courtyard by its north gate for
    open ground (`courtyard-field`), and the outro holds ground there with lightning every second
    beat. Inside a 4-gap courtyard the peak held 0.75 of its kill rate; travelling to the
    brazier-glade the outro held 0.87 (its rings left nothing near the hero for its combos).
  - Holding ground, the hero attacks whatever its scaled hit shapes reach, and a lighter section
    starts its shapes at 1.5× at most: both stop the horde idling at the kill zone's edge.
  - S1–S5 hold the arena's 35 m opening, so the bench's timelines match TD9's scene; on TD4's flat
    bench they avoid trees that TD9 will draw.
- **What it does not show:** whether jams at the gaps look acceptable (that needs TD9's scene and
  TD4's playback; the task that captures S6 adds the operator's row); the sim's timing in the player
  (TD7); the art (TD9's: the wall `End` pieces are unused, since which end is broken is unknown).
  `NavGrid.PassableFor` caches per instance and is not thread-safe: build timelines on one thread.
- **Checks:** `demo3d check`: `vendor guard ok, dotnet test ok, unity editmode (cold) ok` (guard PASS
  over 4,756 files; dotnet 112/112; Unity 121/121 in 119 s).

### TD7

Status: Done (2026-10-27), with one caveat: the first frontier was measured while a Windows
Firewall prompt held the foreground (see QA below and
[Pending on the operator](#pending-on-the-operator)).

Commits on `td/td7` (from `demo3d` at `37957b2`): `87ce465`, `e315294`, `3ea6b78`, `141676c`,
`f6798c2`, `9398317`, `29c806a`, `a108886`, `1202b45`, `860f297`, and the commit that records
these notes.

Run command, from the worktree's root:

```
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/synthetic-frontier.json --out <dir>
```

It builds the IL2CPP player if `Builds/il2cpp` has none (`--build` to rebuild), takes the machine
lock, and launches `PlaylistSurvivors3D.exe -force-d3d12 -screen-width 1920 -screen-height 1080
-screen-fullscreen 0 -job-worker-count 6 --bench <spec> --out <dir> --lock-held`. The player
alone (without `--lock-held`, taking the lock itself) is the design's command. `demo3d report
<dir>` regenerates the report; evidence in [demo3d/evidence/td7/](../../demo3d/evidence/td7/).

**First frontier** (the synthetic load: 12,000-triangle capsules with glow quads, `load` demo as a
label, MSAA 4x; 2026-10-28 00:38 to 01:23, IL2CPP; [frontier/](../../demo3d/evidence/td7/frontier/)):

| view | frontier N | limit | GPU p99 | main / render p99 | compute est. | fill count | runs (probe+full) |
|---|---|---|---|---|---|---|---|
| 30 m | 1,593 (capacity-bound) | capacity | 3.64 ms | 1.51 / 0.86 ms | 10.9 ms | 664 | 3+1, fit confirmed |
| 40 m | 2,146 | GPU | 4.20 ms | 1.45 / 0.79 ms | 12.6 ms | 1,180 | 3+5, bisected |
| 55 m | 2,326 | GPU | 4.15 ms | 2.05 / 1.01 ms | 12.5 ms | 2,232 | 3+2, fit confirmed |

- The envelope rule gives 55 m, 2,300 on screen; for a synthetic load that only exercises the
  report.
- **Every one of the 51 runs was noisy**, so all three points rest on noisy runs: TD5's session
  used 13 to 55% of the CPU throughout (Unity imports, IL2CPP builds, its own crowd bench), and its
  stills player up to 23% of the GPU's 3D engine (one run read 27.8 ms GPU p99 under it). The
  window never held the foreground (the firewall prompt), and wasn't occluded except by a stray
  Microsoft Store window and TD5's player window. The small sweep at 22:42, when the machine was
  quieter, found 40 m at 2,230 (+4%).
- This frontier took each noisy run's last attempt. At 40 m that decided 2,146 on a third attempt
  at 4.199 ms after two at 4.27 and 4.26; the search now uses the median of an all-noisy run's
  attempts (`860f297`), which on the same rows gives 2,093.
- The synthetic load is GPU-bound: main and render thread p99 stay under 3 ms at every count, so
  the CPU lines first meet a real load with TD5's crowd.
- Overdraw at the frontier: opaque depth complexity 4.1, 3.4 and 2.4, transparent fragments per
  pixel 5.1 (flagged, over 5), 4.0 and 2.4 at 30, 40 and 55 m; the glow quads' beat peak equals
  their mean, as they don't vary with the beat.
- GPU 67 to 75 °C during runs, throttle reasons only `power_cap` (or none); no thermal reason.
  Zero GPU readings: 3 to 57 of about 5,000 timed frames per run. Every row says `jobWorkers: 6`
  and memory 2133 MHz.
- A point costs 4 to 8 full-length-equivalent runs as designed (3 short probes + 1 to 5 full
  runs); the noise repeats tripled that, and the bounded quiet waits (up to 10 minutes at the
  start, 60 s before a repeat) took about a third of the 45 minutes.

**What was built.**

- `Assets/Demo3D/Bench/` (engine-free, `Demo3D.Bench`): the sweep spec (benchmarks.md's format,
  plus `n.fixed`, `lines`, `quiet`, `sceneOptions`), the fitted search (`FrontierSearch`), the
  pass rule, statistics, the camera geometry (footprint, visible ground, packing capacity), result
  rows and machine facts (power plans, NVML throttle reasons, SMBIOS memory speed). 26 EditMode
  tests cover them (`BenchCoreTests`).
- `Runtime/Bench/`: the player (`BenchRunner`, `--bench <spec> --out <dir>`, plus `--hold` for one
  configuration and `--pix-capture`), the scene registry (`IBenchScene`, `BenchScenes`), the
  synthetic load, the bench rig (camera, sun, post), the overdraw counters, the machine lock, the
  noise sampler and the Win32/NVML calls.
- `tools/`: `demo3d sweep`, `demo3d report`, `demo3d pix`, with pytest coverage of the lock (a
  killed holder frees it at once), the report and the PIX summary.
- `sweeps/`: the frontier and small sweeps and the QA specs. ProjectSetup generates the bench
  materials under `Resources/Bench/`.

**Design choices.**

- **The player does the measuring, the search and the refusals; Python only orchestrates.** The
  design's command line is the player itself, so it must be complete alone: one process per
  scene, which loads once and changes axes in place (a new N rebuilds the crowd and re-warms 3 s;
  the first run warms 5 s). The search and row logic are pure C# so EditMode tests reach them.
- **The orchestrator takes the lock before the window opens.** A second sweep then waits without
  putting its window over the first one's. The player gets `--lock-held`; launched alone, it takes
  the lock itself. The lock is `bench.lock` in the per-machine cache opened with no sharing
  (`CreateFileW`, share mode 0), so Windows releases it with the process.
- **A Job object with kill-on-close holds the player.** Killing the orchestrator with `taskkill
  /F` kills the player too, so no orphaned window keeps the GPU while the lock is free.
- **The search.** Probes at 20/50/80% of the way to the ceiling, or 0.75/1/1.25× the nearest
  measured point's frontier (nearest: fewest differing axes, then log view-width distance); a
  least-squares line per resource (GPU, main, render p99); the lowest crossing is the candidate,
  floored and clamped to the ceiling. A full run at the candidate, then one at ⌈1.05×⌉; any
  disagreement bisects geometrically between the largest full-run pass and smallest full-run fail
  (probes are hints only), to 5%. A point's limit is the worst resource of its smallest failing
  full run, or `capacity`. At most 14 full runs per point.
- **The ceiling** is min(12,000, packing capacity), capacity being the footprint's area (the four
  frustum corner rays on the ground) × 2.4 per m². The computed visible ground is 664, 1,180 and
  2,230 m² at 30/40/55 m, 3 to 4% under camera-and-sound.md's rounded 690, 1,220 and 2,300; the
  report's fill counts use the computed values.
- **Frame timing.** FrameTimingManager's newest frames have no GPU time yet (they read 0), so each
  frame is held until 24 newer frames exist and only then committed; that cut zero GPU readings
  from 17% to 0 at 300 fps (5% remain at 1,000 fps, counted and excluded as TD1 suggested). Main
  thread time is `cpuMainThreadFrameTime`, which excludes its Present wait (reported separately).
  Run windows and counter frames are matched to frames by QueryPerformanceCounter timestamps,
  FrameTimingManager's clock (Mono's Stopwatch has another epoch; checked at startup).
- **Overdraw counters** render off the camera with a command buffer into a 2048² RGFloat target
  (1920×1080 viewport): every opaque fragment with no depth test (opaque depth complexity), the
  opaque depth, then transparent fragments that pass it. A mip chain averages it on the GPU and one
  texel is read back asynchronously. They run on every 60th frame and the frame after each
  downbeat (the synthetic load has a nominal 120 bpm); those frames and the next are excluded from
  timing. Scenes reissue their draws through `IOverdrawSink`, so TD5's crowd will need a variant of
  the counting shader that skins (noted for TD5).
- **Refusals** in order: backbuffer (`Screen` and `Display.main.renderingWidth/Height` must equal
  the spec's 1920×1080), `JobsUtility.JobWorkerCount` ≠ `jobWorkers`, not Direct3D 12, power plan
  not High/Ultimate (`PowerGetActiveScheme`), a locked desktop (`OpenInputDesktop` +
  `SwitchDesktop`), a display that doesn't come back within 15 s, a window that can't take the
  foreground, a GPU not under 50 °C with no thermal reason after up to 3 minutes of cooling. Each
  writes `refusal.json` and exits 3.
- **The display** is read from `GUID_CONSOLE_DISPLAY_STATE` notifications, woken by a zero-sum
  mouse nudge (`SendInput`) and held with `SetThreadExecutionState(ES_CONTINUOUS |
  ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` for the sweep's life.
- **Noise** is sampled every second on a background thread: other processes' CPU (system busy time
  less our own, `GetSystemTimes`/`GetProcessTimes`), its 5 s rolling mean against 10%; NVML's
  temperature and clock-event reasons (thermal, hardware slowdown and power brake are noise; the
  power cap is recorded); the foreground window; occlusion (Unity doesn't expose its swap chain's
  `DXGI_STATUS_OCCLUDED`, so any visible, uncloaked window above ours in the z-order that overlaps
  it counts); and the display state. A noisy run waits up to 60 s for quiet and repeats, up to
  twice, and its row says why. `pass` in a row is the timing rule alone; the report counts the
  points that rest on noisy runs.
- **NVML instead of spawning `nvidia-smi`** for temperature, throttle reasons, driver and name:
  the same library `nvidia-smi` uses, without a process launch every second counting as other
  processes' CPU. Memory speed comes from the SMBIOS table (`GetSystemFirmwareTable`, type 17's
  configured speed), since WMI isn't available to the player.
- **The synthetic load** is N procedural capsules (0.4 m radius, 1.8 m, `triangles` option, 12,000
  in the frontier sweep so the GPU limits inside the packing capacity), drawn with
  `RenderMeshInstanced` in chunks of 1,023 with shadows, each walking a 0.6 m circle at 1.2 m/s
  around an anchor on a jittered grid over the view's footprint, plus (option `glow`) an additive
  camera-facing quad each, which gives the transparent counter work. Flat 600 m ground, a soft-
  shadowed sun, shadow distance fitted to the view's farthest corner, bloom, neutral tonemapping,
  color adjustments and vignette (SSAO arrives with TD9's post stack). The `load` axis is recorded
  but means nothing to it.
- **The scene interface** (`IBenchScene`): `Load` once, `Configure(point)`, `Rebuild(n)` returning
  the sim's tick timing (null without a sim), `Tick(t)`, `DownbeatIn`, `CollectCounters`,
  `DrawOverdraw`, and an optional packing density. TD4 registers S0 to S6 and the song with
  `BenchScenes.Register(id, factory)`; the row fields for particles, lights, LOD, skinning and the
  sim stay null until a scene reports them.
- **PIX.** The player calls PIX's programmatic capture API (WinPixGpuCapturer.dll's
  `CaptureNextFrame`, which pix3.h's `PIXGpuCaptureNextFrames` wraps) after warming up at the held
  point, with pixtool loading the capturer. The exact commands (pixtool's `--command-line` must be
  quoted on the raw command line and can't contain `=`, which is why `--hold` also takes colons):

  ```
  "C:\Program Files\Microsoft PIX\2603.25\pixtool.exe" launch "<exe>" --command-line="-force-d3d12
    -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -job-worker-count 6 --bench <spec>
    --out <dir> -logFile <dir>\player.log --hold n:2000,viewWidthM:40,load:demo,aa:msaa4
    --pix-capture <dir>\capture.wpix --pix-frames 3" programmatic-capture
  "C:\Program Files\Microsoft PIX\2603.25\pixtool.exe" open-capture <dir>\capture.wpix
    save-event-list <dir>\events.csv --counters="*Duration*"
  ```

  The capture works (a 3-frame, 45 MB capture at 2,000 instances, 40 m). The event list doesn't:
  PIX's replay needs Windows' Developer Mode, an administrator setting
  (`E_PIX_FEATURE_REQUIRES_DEVELOPER_MODE`), so the per-pass summary (`demo3d pix`'s passes.md:
  top-level marker durations per frame) waits for the operator, and its CSV parser is tested only
  on a synthetic event list. Unity's own GPU markers were tried as a fallback: a release player
  exposes only `FrameTime.GPU`, and a development player opens a profiler socket (below).
- **Steel Nomad.** Confirmed for the RTX 4070 Ti (5,040) and both bandwidths (504 and 256 GB/s);
  not for the RTX 4060 Laptop, whose published medians are 2,267 (UL) and 2,236 (Notebookcheck),
  not 2,025, which matches the desktop RTX 4060 (2,040). That would make the ratio 2.22× and the
  GPU line 4.69 ms. The harness keeps 4.2 ms (about 11% conservative) until the design changes it
  ([sources.md](../../demo3d/evidence/td7/sources.md)).

**QA** ([Sweeps and the machine lock](qa.md#sweeps-and-the-machine-lock),
[qa-checks.md](../../demo3d/evidence/td7/qa-checks.md)):

- Small sweep (one scene, two points): **pass**; 21 rows with timings, counters, machine state and
  `pass`; 30 m 1,593 (capacity-bound), 40 m 2,230 (GPU).
- Heavy build beside a sweep: **pass**, with TD5's real builds and imports as the load rather than
  a staged one: bounded waits ("machine still busy after 61 s ...; running anyway", 200 s and
  600 s at sweep starts), noisy runs marked with their reasons, repeated twice, and counted in the
  report (12 of 21 in the small sweep, 51 of 51 in the frontier, "Frontier points resting on noisy
  runs: 3 of 3").
- Second sweep while one runs: **pass**; it printed the holder and waited, then started 3 s after
  the first one's player exited. TD5's crowd bench honoured the same lock.
- Killed with `taskkill /F`: **pass**; killing the orchestrator took the player with it (Job
  object) and the next sweep took the lock at once; a pytest repeats it.
- Balanced power plan: **pass**; `powercfg /setactive` needs no administrator rights, the refusal
  names "Balanced (381b4222-...)", and High performance was restored at once.
- Locked desktop: **pending on the operator** (an agent can't unlock it again).
- Display off: **wake passes** (off, then on 0.6 s after the sweep's start); "the sweep runs with its
  window composited" is **pending** the firewall prompt (the run then refused at the foreground).
- Three sweeps back to back: **partly**: not run as three dedicated sweeps (`sweeps/qa-thermal.json`), because TD5's crowd bench held the machine lock from 01:23 for longer than TD7 could wait. Throttle reasons are recorded in every row (`gpuThrottle`), thermal ones are noise, and the report lists every reason seen; the frontier's 51 consecutive runs over 45 minutes stayed at 67 to 75 °C with `power_cap` only. TD12's sweeps cover the back-to-back case.
- Different window size: **pass**; `--screen 1600x900` refused with "the backbuffer is 1600x900
  ...; the sweep needs 1920x1080". `--workers 4` refused likewise.
- `jobWorkers: 6` in every row: **pass** (all 72 kept rows).
- `demo3d check --player`: `check: vendor guard ok, dotnet test ok, unity editmode (cold) ok, il2cpp
  player build ok` (27/27 EditMode tests; pytest 37 passed).

**Not done.** The dedicated back-to-back thermal sweeps (above); a frontier with the foreground
(pending the firewall prompt); the PIX per-pass summary (pending Developer Mode).

**Lessons.**

- **Never launch a development player on Cygnus unsupervised.** Its profiler socket raised a
  Windows Firewall prompt ("Windows Firewall has blocked some features of
  playlistsurvivors3d.exe"), which holds the foreground until someone answers it, and answering
  it (even Cancel, which adds a block rule) changes a security setting, so only the operator may.
  Every later sweep refused at the foreground check until TD7 added `quiet.requireForeground`
  (`demo3d sweep --no-foreground`).
- **Other sessions dominate the noise.** Another session's idle warm editor alone uses 6 to 8% of
  Cygnus's CPU, against a 10% threshold, and its imports, builds and player windows (one occluded
  TD7's window) made most runs noisy. `demo3d sweep` stops its own worktree's warm editor.
- **Kill players by PID, never by image name.** Twice a TD7 sweep's player died with exit code 1,
  its log cut mid-line, a few seconds before TD5's crowd bench took the machine lock; something on
  TD5's side was apparently killing every `PlaylistSurvivors3D.exe`. TD7's frontier ran from a
  renamed copy of its build (`--player Builds/il2cpp-td7/PlaylistSurvivors3D-TD7.exe`, the exe
  and its `_Data` folder renamed). Sessions should wait for the lock and kill only their own PIDs.
- **A stray Microsoft Store window** (an agent running a bare `python`, which opens the Store
  through Windows' app alias) sat maximized over the player's window; TD7 minimized it. Use
  `uv run python`, never `python`, on Cygnus.

### TD8

Status: Done (2026-10-28, on Cygnus).

- **Commits** (branch `td/td8`, from `3d9e740`, with `demo3d` at `b03b45a` merged in `334fa0d`):
  `10dabb0` (runtime, converter, light pool, gallery, hookups), `4856155` (trails as stretched
  billboards, beams, orbital velocity, the spike burst, spawn-cost timing), `22207d1` (the VFX Graph
  arm, spawn sinks, EditMode tests, light pool specs), `246b70d` (the spike's decision, authored
  light cues, the VFX package removed again), `88ef4eb` (A/B stills, light pool sweeps), and this
  entry's commit.
- **Run:** in a set-up worktree, `demo3d unity BakeAll` (the `effects` baker writes
  `Assets/Baked/Effects/Resources/EffectLibrary.asset`; also `demo3d unity BakeEffects`), then
  `demo3d build`. The song with effects: from `demo3d/`, `Builds\il2cpp\PlaylistSurvivors3D.exe
  -screen-width 1920 -screen-height 1080 -screen-fullscreen 0` (`--no-effects`, `--light-pool N`,
  `--effect-pool N`; `--stats` adds particles, spawns, cues, lights and the drop share). The A/B
  stills: `... --effect-gallery <dir> [--effects a,b] [--gallery-night]` (windowed). The spike:
  `demo3d sweep demo3d/sweeps/td8-spike-own.json` (the VFX arm needs com.unity.visualeffectgraph
  17.6.0 back in Packages/manifest.json, `demo3d unity VfxSpikeTemplate`, and
  `sweeps/td8-spike-vfx.json`). The pool: `sweeps/td8-lightpool-{64,128,256}.json`. Evidence and
  every table: [demo3d/evidence/td8/](../../demo3d/evidence/td8/README.md).
- **The spike** (S4, heavy, 40 m, N 2,600 requested, ~700–930 on screen, 450–550 bursts on each
  beat frame, lights off):

  ```
                      gpu p99        main p99      beat-frame spawn cost (p50 / p99)   authoring
  own runtime         0.23 ms        0.96-0.99 ms  0.11-0.13 / 0.14-0.23 ms            code only
  VFX Graph template  0.21-0.23 ms   0.82-1.74 ms  0.06-0.09 / 0.08-0.18 ms            Simple Burst edited as text
  ```

  The VFX row carries ~63 live particles against our 10–11k: the template (VFX Graph 17.6.0's
  Simple Burst, edited as text by `VfxSpikeTemplate.cs`: counts, lifetimes, speeds, capacity,
  bounds, culling off, gravity and drag out, one Set Position (Add, from source) block in) imported
  without repair and draws a single spawn like ours, but its spawner fires one burst a frame
  however many OnPlay events arrive. The design's route, a spawn GraphicsBuffer read by spawnIndex,
  needs new operator, exposed-property and linked-slot objects in the .vfx: building the graph, not
  editing a sample. **Our runtime wins** by the rule's second clause. No VFX feature was seen to
  break batching (one system served every spawn). The package is out again; Runtime/VfxSpike
  compiles only while it is present.
- **Built:**
  - The converter (`Editor/Effects/EffectConverter.cs`), to the inventory
    ([inventory.md](../../demo3d/evidence/td8/inventory.md), regenerated over 52 candidates):
    main, emission (bursts with cycles and probability, rate over time and distance), shapes
    (sphere, hemisphere, cone, cone volume, circle, box, edge; arcs random and burst-spread;
    random and spherical direction; shape transform), velocity (with orbital and radial), limit
    velocity, inherit velocity, force, colour, size (axes), size by speed, rotation (three axes),
    noise (curl approximation), texture sheets (whole sheet), custom data, lights (a cue), every
    curve and gradient mode baked into 64-sample rows (one lookup atlas for all definitions, 14
    rows each, since one dispatch serves every type), renderers billboard, stretched, horizontal,
    vertical, mesh (meshes expanded to triangle lists), local alignment, min and max size, sorting
    fudge; LineRenderers as beams. Collision, sub-emitters, trails, triggers, external forces,
    colour by speed and the rest fail loudly per prefab and child unless the selection drops the
    module, skips the child or approximates trails; `converter-report.md` lists every candidate's
    result. Textures go into one 512² DXT5 sRGB array (55 slices). The selection is tracked
    (`Assets/Demo3D/Data/effects.json`, by manifest path); all output is in the gitignored
    `Assets/Baked/Effects/`.
  - The runtime (`Runtime/Effects/`): `EffectSystem` (one 262,144-particle pool for every type, a
    dead list and two alive lists; each frame's spawns are expanded on the CPU into emitter
    instances with their emission segments and go up in one upload; kernels Allocate (all of a
    spawn's particles or a counted rejection), Emit, Simulate, compact; a bitonic sort of the
    alpha-blended billboards across types, sized from the last readback; indirect procedural draws
    in buckets (alpha billboards sorted, alpha meshes, then additive) from a render pass after
    opaques, enqueued per camera like TD5's debug pass so the renderer asset is untouched;
    randomness hashed from spawn seed, emitter and particle index). `EffectParticles.shader`,
    `EffectSim.compute`, `EffectSort.compute` and `EffectCommon.hlsl` are our own, written from the
    materials' observed inputs (blend factors, texture, tint and emission, opacity, noise mask and
    its scroll speeds, soft-particle depth power).
  - `EffectLights`: cues, 1.5 m clustering, frustum culling, the pool by screen contribution, the
    dropped share; `EffectDriver`: timeline events due each frame through the Catalog mapping, plus
    the client's own bolt-in-flight, monster dust puffs and ambient emitters; `EffectGallery`.
  - Hookups: `DemoMain` (the song; the placeholder projectile spheres go when effects are on) and
    `TimelineScene` (S3–S6 draw effects and lights from the stress script's `draw` list; S4 hides
    the crowd). Counters: particles, effect spawns per frame and max, spawns rejected, mean active
    cues, lights used (max) and the drop share's p99; `PassRule` now fails a p99 drop share over 2%,
    and the overdraw counters count particles (`IOverdrawSink.Transparent(Action<CommandBuffer>)`).
  - Tests (`Tests/EditMode/EffectTests.cs`, 6): the spike burst's conversion, loud failure and
    drops, emission segments (bursts, cycles, rates, distance, determinism), clustering, drops and
    culling, and the runtime's shaders compile. EditMode 181/181.
- **The effect list** (20; Catalog name → effect → stock prefab):
  hit-spark → hit-spark / hit-spark-b by seed (Magic hits Hit 1 / Hit 2), elites → hit-heavy
  (Hit 3); death-burst → death-burst (AAA Projectiles Hit 25 orange explosion), at Scale ≥ 2.5 →
  death-burst-large (Cartoon explosion); parry-spark (Flash 2 electro); slash-trail (Sword Slash
  2); thrust-trail (Slash wave); spin-trail (Sword Slash 11); slam-shock (AOE Magic hit); ring
  (Magic circle sun); bolt-launch (Flash 16 fire); bolt-impact (Hit 16 fire); lightning (Lightning
  hit) with lightning-circle (Magic circle electro); laser (Laser beam 10 fire); and the client's
  bolt (Projectile 18 nova orange, along each bolt's flight), dust-puff (Cartoon smoke, cut to a
  0.25 s puff at a third of its size), ambient-embers and ambient-wisps (Magic Pig PComponent Fire
  Embers, Wisps Circular; six emitters over the view every 2 s). Glow quads are kept (TD11's cut).
- **Swaps and approximations, with reasons:**
  - Death bursts: KriptoFX Explosion1–6 → Hovl Hit 25 orange explosion and Cartoon explosion. The
    PC explosions' particles use KriptoFX's distortion shader, magenta in URP (stock and converted
    alike); the Mobile ones are a mesh, not particles.
  - Ring: Energy explosion → Magic circle sun. Its fire and smoke are birth sub-emitters (and two
    children have no mesh in the synced closure); the circle's curved trails are skipped (ribbons
    drawn as stretched billboards became straight spokes there).
  - Skipped children: Hovl's distortion layers (their shader isn't in the project: magenta in the
    stock prefab too). Dropped modules: collision on slam-shock, bolt-impact, laser and the large
    death burst (cosmetic). Lightning-circle's electric trails are stretched billboards over their
    particles (same seeds and births).
  - The laser's beam is its LineRenderer as a camera-facing strip of the event's length; its
    script (missing, like every Hovl script) normally sets the length and scrolls the texture.
  - Sword slashes multiply their shape and second texture and mask with the dissolve noise; the
    vendor graph's exact mix isn't reproduced.
  - Light cues authored where the prefab has no light: death bursts, bolt launch and impact,
    slashes, slam, lightning and laser (effects.json).
- **Light pool: fixed at 128.** URP 17.6 Forward+'s limit is 256 visible additional lights on
  desktop (logged at start). At S3 heavy, 64 saturates and drops 12–23% of visible cue intensity;
  128 and 256 never drop (at most 103 used) and cost the same within noise; 128 leaves half of
  Forward+ for the arena's lights. Only 690–910 were on screen in those runs, so TD12 rechecks
  the drop share at the envelope through the pass rule.
- **Measured but not TD8's to fix:** S3 heavy at that count runs 5–7 ms GPU p99 with 10–12
  transparent fragments per pixel (beat peak 13–15, flagged); about 23k particles alive, no
  rejections. Every run was noisy (TD9's session, and the firewall prompt on the desktop).
- **Self-review** (AGENTS.md's checklist):
  - Downstream needs: TD10 gets the laser's sweep (yaw rate one turn a bar, duration a bar, from
    the map) and `EffectSystem.Add` with velocity, yaw rate and duration; attached events are
    placed at the entity's position at the event time and don't follow it (deferred to TD10 for
    the hero's slashes). TD11: glow quads are separate children (`skip` in the selection drops
    one), `EffectLights.Cell` is the cluster size, merging would sit in `EffectSystem.Add`
    (not built). TD12: every counter above is in the rows, and the pass rule uses the drop share.
  - Claims: every number above is in `evidence/td8/` (rows, bench logs, spawn-cost logs); the
    spike's VFX row is labelled as not carrying the load; the pool's runs are below the envelope.
  - Edge cases: a seek or a loop wrap resets the pool and re-spawns 4 s of events; a frame over
    16,384 emitter instances counts the rest as rejected; the 65,536-entry instance ring would
    overwrite an instance still alive after about 50 s of heavy spawning (not guarded); the sort
    covers 1.5× the last read-back alpha count (any excess draws unsorted).
  - Tests against the source: the A/B stills against the stock prefabs; the converter's tests run
    a Shuriken system built in code.
  - Concurrency: GPU readbacks land on the main thread and are ignored after Dispose.
- **Not done:** sub-emitters and real ribbons (swapped or approximated instead); distortion; merging
  (TD11); attached effects that follow their entity (TD10). The A/B stills wait on the operator
  (row below).
- **Review** (the demo-systems milestone's cold review; fixes on `td/td8-review`):
  1. Alpha billboards past the sort size drew stale sort-list slots (the size came from a readback
     a few frames old, so a beat's jump in alpha count made smoke and sparks vanish): **fixed**.
     The material gets the size each frame used, and instances past it read the unsorted draw list,
     so the excess now really draws unsorted. Single-effect A/B stills are unchanged (their alpha
     counts stay under the 2,048 minimum sort); only dense beats in S3/S5 and the song change.
  2. (Not sent to TD8.)
  3. Rejections hidden (`Math.Max` of the CPU total and the GPU counter, the counter zeroed by
     resets, spawns half allocated per emitter): **fixed**. `RejectionLedger` keeps CPU and GPU
     counts apart; the GPU counter resets each frame and is summed per readback; readbacks issued
     before a reset are ignored; a spawn is rejected whole on the CPU (all its instances fit the
     frame's list or none) and on the GPU (Allocate runs once per spawn over its instances). Test:
     `RejectionsSumCpuAndGpuIgnoreStaleReadbacksAndExcuseResets`.
  4. A seek or loop wrap re-spawned 4 s of events whether alive or not: **fixed**. The look-back
     spawns only effects still alive (`EffectDriver.StillAlive`: spawn time plus lifetime, or the
     requested duration, past now), and a reset frame's overflow is excused from the pass rule and
     logged. Test: `SeeksRespawnOnlyEffectsStillAlive`. Smoke: S5 heavy (N 2,600, 40 m) ran 20 s
     with about 100k particles on beats, no rejections and no errors.

### TD9

Status: Done (2026-10-28, on Cygnus). No step waited: TD5 had landed.

- **Commits** (branch `td/td9`, from `demo3d` at `3d9e740`): `c6a2b4a` (the generator, the
  material conversion, the lighting, the static shadow map, the post stack, S0), `51c4a0e` (the demo
  and S1–S6 in the arena, the capture tool's MSAA, tests, sweep specs), `2798b09` (merge of
  `demo3d` at `b03b45a`), and this entry's commit (evidence and notes).
- **Run:**
  - Bake: `demo3d unity BakeArena` (also the `Arena` baker of `demo3d unity BakeAll`; `-e0 0.5,1,2,4
    -lit 1,2 -noLightmaps` choose; 3.5 min with lightmaps), then `demo3d build --backend il2cpp`.
    Stills: `demo3d unity Demo3D.EditorTools.Arena.ArenaStills.Run -rung e2|e0 -density 1 -out <dir
    relative to demo3d/> [-heroes] [-shots a,b]`.
  - **The demo** (the song in the arena), from `demo3d/`: `Builds\il2cpp\PlaylistSurvivors3D.exe
    -screen-width 1920 -screen-height 1080 -screen-fullscreen 0` (`--arena e0|e2|off`, `--density
    0.5|1|2|4`; default E2 at 1×). The full song ran to 198.98 s at 60 fps (vsync): 387 samples, one
    52 ms frame.
  - Sweeps: `demo3d sweep demo3d/sweeps/s0-e0-{0.5,1,2,4}x.json demo3d/sweeps/s0-e2-{1,2}x.json
    --out <dir>`; `td9-aa-s3.json`, `td9-aa-s1.json`, `s6-arena.json`. Tables: `uv run --project
    demo3d/tools python demo3d/evidence/td9/summarize.py <dir>`; captures: `aa_captures.py` beside it.
- **Built:**
  - `Editor/Arena/ArenaBaker.cs`: from `ArenaLayout.WithDensity(d).Generate(footprints)`, one scene
    per rung and density in `Assets/Baked/Arena/` (gitignored): the kit's prefabs instantiated and
    unpacked at each placement (colliders, vendor scripts, prefab lights stripped), grouped by area
    type; a 220 m ground mesh with an albedo generated from the layout's ground types (forest floor,
    edge band, clearing, path; our own design, written to Baked/) and Elven Realm's ground normals as
    a 3 m detail map; the moon; the flame and glow lights; the `ArenaIndex` the player reads. E0
    scenes at 0.5, 1, 2, 4× (976, 1,418, 2,086, 2,775 renderers; 0.33 to 1.07 M LOD0 triangles); E2
    scenes at 1× and 2× with lightmaps baked in batch mode (Progressive GPU, shadowmask, 6 texels/m
    on the ground only, a probe grid every 8 m for the kit; 66 s each).
  - `MaterialConverter` (same file): the kit's 8 materials saved under `Baked/Arena/Materials/`:
    Standard to URP Lit as Unity's converter maps it (albedo, colour, normal and metallic-gloss maps,
    emission, alpha clip), the candles' flame particles to URP's unlit additive particles, Elven
    Realm's triplanar rock to plain Lit in its rock colour (its Shader Graph port is TD14's). E2's
    kit gets `Demo3D/LitStaticShadow` twins (below).
  - The lighting (`lighting` in `arena-layout.json`, read by `ArenaLighting`): the moon at 30°
    elevation, cool blue (0.55, 0.66, 1.0), intensity 0.95, soft shadows; trilight ambient;
    exponential fog 0.011, the camera clearing to its colour; 8 flickering flame lights (4 braziers,
    the pyre, 3 graveyard candles; `FlameFlicker`) and the glowing tree's cyan light (baked at E2).
    Emissive kit materials bloom.
  - The static shadow map (`StaticShadowMap.cs`, `Shaders/StaticShadow.hlsl`): at load the static
    set's depth through each material's own ShadowCaster pass, 4096² from the moon over the arena's
    bounds (5.9 × 3.5 cm texels by axis; 5 ms in the player, the CPU's command submission, not
    GPU time), published as shader globals. The crowd
    shader (its TD5 hook, now the shared `Demo3DLightingPBR`), the hero (`Demo3D/LitStaticShadow`:
    URP Lit's passes with our forward pass; RosterBaker gives the hero's materials it) and E2's kit
    sample it with a 2×2 tent and take the min with the realtime shadow. At E0 it is off.
  - The post stack (`NightLook.cs`): bloom, neutral tonemapping, colour adjustments, a 32³ night LUT
    generated in code (shadows to blue-violet, warm saturated highlights kept, a gentle S-curve; no
    texture asset), vignette; SSAO as the renderer's feature (ProjectSetup: depth-normals, full
    resolution, medium samples, radius 0.3 m, intensity 1.2); shadowmask mode Shadowmask.
  - Runtime (`ArenaRuntime.cs`): loads the scene additively, makes it active, takes the moon, applies
    the rung; `RenderStats` reads Unity's draw, set-pass, triangle and shadow-caster counters (a
    release player has them). `ArenaScene.cs` is S0 (sceneOptions `rung`, `density`, `holdS`; the
    camera tours the six shot positions, 3 s each, at the point's view width). S1–S6
    (`TimelineScene`) load the arena when baked (`arena`, `density`); rows gain `environment`.
  - DemoMain loads the arena instead of the flat ground, removes the boot scene's camera and light
    (both had been drawing every frame), and fits the shadow distance to the view (the 50 m default
    cut off the top of a 40 m view). The capture tool now renders at the pipeline's MSAA count (with
    a 1-sample target URP drew MSAA captures without MSAA) and keeps enlarged crops
    (`--capture-crop x,y,w,h,scale`).
  - Layout refinements: the glowing tree moved to (−11, −39) and its shot to (−5, −46), so its canopy
    no longer fills the shot; `dotnet test` 112/112 after.
- **S0 at rung E0** ([evidence](../../demo3d/evidence/td9/README.md); GPU / main / render p99 ms,
  MSAA 4×, medians of 3): at 40 m, 0.5× 0.39 / 0.96 / 0.55, 1× 0.40 / 0.90 / 0.53, 2× 0.42 / 1.00 /
  0.54, 4× 0.43 / 1.00 / 0.55; at 55 m, 0.41 / 1.43 / 0.75, 0.43 / 0.98 / 0.57, 0.46 / 1.03 / 0.59,
  0.50 / 1.12 / 0.65. All far under the ceiling (1.5 / 1.7 / 1.7 ms); 1× to 4× costs 0.03–0.07 ms
  of GPU. At E0, 1×, 40 m a frame issues 433–525 draw calls with 18–25 set-passes, with 61–94 kit
  pieces in view and 241–307 shadow casters (Unity's count); at 4×, 55 m, 1,339–1,384 calls and
  684–803 casters. E2 at 1× takes the casters to 0 and the calls to 186–195 (40 m) for the same GPU
  time (0.39 ms). S0 runs CPU-bound near 1,000 fps, so these say the environment is cheap on this
  GPU, not where its limit is; every row was marked noisy (TD8's session, and the zero-GPU-time
  frames at 1,000 fps), though attempts agree within 0.05 ms.
- **S6 runs in the arena:** 1,200 at 40 m, demo load: 960 on screen alive, GPU p99 4.91–5.33 ms,
  main 5.9–6.1 ms, sim tick p99 5.0 ms.
- **Anti-aliasing: MSAA 4×.** TAA saves 0.45–0.69 ms of GPU p99 (11–13 %) on S1 and S3 at 800 and
  1,200 in the arena at 40 m (S3 at 1,200: 5.64 against 5.02 ms), and 0.04 ms on S0. In the
  fixed-time captures (S3 at 1,200, and the song's drop-1) TAA leaves the monsters' outlines and the
  bolts as stepped as no anti-aliasing: nearly every edge in the horde moves every frame, so its
  history is mostly rejected. MSAA's edges are smooth. The horde is the demo's subject, so MSAA 4×
  stays the default; TD12 measures TAA only at the envelope point. Crops in `evidence/td9/aa/`,
  videos in `D:\wt\demo3d\captures\td9\` (gitignored); the review is in Pending on the operator. TD8's
  spark trails weren't on this branch: projectiles and hit flashes stood in.
- **Choices, with reasons:**
  - E0 and E2 as separate scenes, not one scene with its lightmaps switched off: E0 must carry no
    baked data to be the stock baseline, and E0 at every density then needs no bake.
  - Only the ground is lightmapped; the kit (no UV2 on 74 of its 85 pieces, and vendor import
    settings stay untouched) takes probes, and at E2 the static shadow map instead of probe
    occlusion: without it a rock under a tree came out lit at E2 (`stills/shade/`).
  - Lights come from the layout parameters, not from the prefabs, so the cap of 8 flames is a layout
    rule.
  - The LUT is generated at runtime, so nothing derived or binary is stored for it.
  - E2 is the demo's and the stress scenes' default (the static map is TD9's to build); S0 defaults
    to E0. TD14 owns the rungs' verdicts.
- **Tests:** EditMode 182/182, TD9's 6 in `ArenaTests` (the moon's angle, colour and flame cap; the
  LUT's shape, cool shadows, warm highlights and monotonic greys; the shadow map's projection
  covering every arena corner, texels under 6.5 cm, depth order toward the moon; the ground's value
  structure; every density baked; each baked scene against its placement list: counts, sampled
  positions, no colliders or scripts, converted materials and shaders, E0's casters, one moon, the
  flame cap, E2's lighting data). `dotnet test` 112/112.
- **For TD14:** `--arena e0|e2` and S0's `rung` and `density` select rungs; E1 needs GPU Resident
  Drawer compatible materials (`Demo3D/LitStaticShadow` has no DOTS instancing variant yet); the
  kit's 8 materials keep set-passes near 20, so E1's gain is in draw submission. **For TD15:** the
  look is a first pass (`stills/`): the opening is bare, the glow light barely reaches the ground,
  no mist cards, fireflies or canopy fade yet. **For TD12:** S0 to S6 run in the arena; rows carry
  `environment`. **For TD11:** the capture tool's crops and MSAA-correct captures.
- **Self-review** (AGENTS.md's checklist): downstream needs, done (TD14's rung switches and density
  scenes, TD15's shots and stills tool, TD12's arena scenes and AA mode, TD11's capture fixes; noted
  above); claims, done (every number is in `evidence/td9/` rows, tables or logs, with what they don't
  show: CPU-bound S0, noisy rows, one-frame counters); edge cases, done (an arena not baked falls
  back to flat ground in the demo and refuses clearly in S1–S6; `arena: off`; LOD 1+ skipped in the
  map; particle renderers kept out of the static set; an existing index merged when baking a
  subset); tests against the source, done (scenes against the placement list; E0's and E2's stills
  agree on where the static set's shadows fall, on the ground and on a hero); concurrency, not
  applicable.
- **Tool problems:** the warm editor stopped answering after a compile error was fixed (its listener
  never came back; `editor stop` left it running; killed by PID). The first build after the lit
  scenes compiled 3,584 URP Lit variants (81 min; 85 s afterwards, from the cache), and `demo3d
  build`'s client timed out while the editor finished it. Two of TD9's player runs (a no-AA capture
  and a song run, about 06:47–06:53) ran without the machine lock beside TD8's sweep; later runs
  took the lock. One `dotnet test` run failed the 3 s song-build test under TD8's load (5.2 s) and
  passed on a rerun.

### TD10

Status: Done (the click-track listening check is the operator's; row below).

- **Commits:** `716d606`, `c021c3e` (feel, sounds, ending slams), the foundations review's
  `6b25c0a` (the sim's clip timing in the client, hit-stop catch-up and the hero's heavy-blow
  slowdown), and this entry's commit (flash tiers, notes).
- **Run** (from `demo3d/`, after `demo3d unity BakeAll` and `demo3d build --backend il2cpp`):
  `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080
  -screen-fullscreen 0 -job-worker-count 6 --click --overlay --seconds 60` (the first minute with
  the click); the full song without `--seconds`.
- **Built:**
  - The hero: TD4's playable graph on the hero prefab (TD9 gave it the static-shadow shader),
    clips started exactly where the sim fitted them, combos with their root-motion segments, and
    spells and parries from the timeline's events, drawn by TD8's effects.
  - Local hit-stop: a struck monster's clip holds 50 ms then catches up over 0.15 s; the hero's
    clip runs at 0.6x for 50 ms on blows of strength 0.8 or more, then catches up, so the next
    impact still lands on its beat (`TimelineEvaluator.HoldThenCatchUp`).
  - Tiered flashes: a hit flashes full; a tick (a lingering effect's repeat hit, such as the
    laser's) flashes at half strength for 80 ms with no clip, flinch or knockback; a parry is the
    hero's shield flash (0.6). The dragon under the laser no longer reads white.
  - The client's punch and shake (`CameraFeel`): a 2% zoom-in over a sixteenth on accents,
    released over a beat; shake min(0.25, 0.08 x strongest + 0.02 x log2(1 + hits)) metres per
    beat, decaying over it; both functions of song time.
  - Sound scheduling (`SoundScheduler`, `SoundBank` baked from TD2's sounds): the timeline's
    aggregated SoundEvents are scheduled with PlayScheduled once within 150 ms, on 32 voices.
  - The ending's accent slams: a plain combo now gives way when its recovery would leave no
    room for the windup of the next section's positioned combo, in a section whose combos are
    all positioned (the ending's `slam@accent`).
- **Checks:** every accent of the ending gets a hero slam, and ticks never knock a monster back
  (dotnet tests over the song); every blow's clip is at its impact at the impact (EditMode, TD4);
  ticks flash at most half strength (EditMode). Whether every blow *sounds* on its click in the
  first minute is the operator's ears (row below).
- **Choices:** attached effects (slash trails on the hero) spawn where their entity is at the
  event and don't follow it afterwards: a slash lasts about 0.3 s, over which the hero moves
  well under a metre. The hero keeps TD4's playable graph rather than a separate component; it is
  the same evaluation the crowd uses.
- **Not done here:** the ending still holds about 1,000 monsters against its 500 target, so it
  doesn't clear the screen: TD13 tunes the level script's ending.
- **The demo systems milestone's review** (TD8, TD10, TD11; a fresh read-only subagent): 8
  findings. Fixed here in `73f463c`: (2) TD11's invisible anchor moved the camera, since
  `--time-offset` fed the shake and punch: they now follow t minus the offset, and `--no-feel`
  turns both off; (5) the punch resized the shadow cascades every frame after an accent: shadows
  now fit the view before its punch; (7) close accents snapped the punch back: overlapping
  punches take the deepest; (8) late sounds were judged on the display's time: they're now skipped
  when past on the DSP clock. TD8's agent fixed (1) alpha billboards past the sort size, (3) hidden
  rejections and (4) seek re-spawns (TD8's entry, merged in `7584db7`); TD11's agent re-ran the
  calibration and the cuts for (2) and recorded their parameters for (6) (TD11's entry).
- **Stills:** [song-040.jpg](../../demo3d/evidence/td10/song-040.jpg),
  [song-172.jpg](../../demo3d/evidence/td10/song-172.jpg) (the final peak in the arena with effects).

### TD11

Status: Done (2026-10-28, on Cygnus); its A/B results re-run after the demo systems review (the
Review bullet below supersedes the first pass's thresholds, scores and defaults).

- **Commits** (branch `td/td11`, from `demo3d` at `df37614`): `c18dc83` (the cut switches, LOD and
  30 Hz bakes, the merge and glow cuts, the tooling), `768cd81` (merge of `demo3d` at `2a2df25`:
  TD10's flash tiers, TD13's mixer), and this entry's commit (evidence, notes, an edit-mode fix).
- **Run:** in a set-up worktree, `demo3d unity BakeAll` (the crowd baker now also writes each
  type's LOD1, LOD2 and 30 Hz bone texture; the effects baker marks glow quads), `demo3d build
  --backend il2cpp`, then the demo as before: it runs with the accepted cuts by default
  (`DemoMain: effective cuts [shadow-proxy,clips30] lodPx 60,30 shadowLod 2 lightCell 2 m mergePx 8` in the log). `--cuts none|all|default|name,-name` (names `lod`,
  `shadow-proxy`, `clips30`, `light-cell`, `merge`, `drop-glow`), `--lod-px 60,30`, `--shadow-lod
  1|2`, `--light-cell 2`, `--merge-px 8`; sweeps take the same as sceneOptions `cuts`, `lodPx`,
  `shadowLod`, `lightCell`, `mergePx`. Every step of the evidence: `uv run --project demo3d/tools
  python demo3d/evidence/td11/td11.py calibrate|cut|cost|frontier|table`
  ([evidence](../../demo3d/evidence/td11/README.md)).
- **A/B setup:** S3 at demo load, 40 m, through the demo client (`--scene S3 --n 600 --view-width
  40 --load demo`: the game camera, the hero, the arena at E2 1x, MSAA 4x; effects, lights and
  the time offset and pose rate all apply there), 12 PNG stills at 10.0 to 29.8 s (1.8 s apart, on
  the frame grid) and a 2 s video from 14 s, fixed time. N = 600: the 40 m frontier with every cut
  off is about 600 (below), under 2,000.
- **Thresholds, first pass** (superseded: Review below) ([thresholds.json](../../demo3d/evidence/td11/thresholds.json), the file TD14's
  E3 and E4 use): invisible anchor (`--time-offset 0.001`) mean 0.02336, p99 0.3867; visible anchor
  (`--pose-rate 15`) mean 0.04324, p99 0.5150; **a cut passes at mean <= 0.02999 and p99 <=
  0.4295**. The run-to-run floor (A against a second A) is 0.00098 / 0.0327. The 1 ms shift moves
  every particle, so the invisible anchor sits far above the floor.
- **The cuts, first pass** (superseded: Review below; GPU saved, A minus B, medians of 2 interleaved fixed-N runs; S3 at 600, S1 at
  1,200 for the crowd's cuts):

  | Cut | mean FLIP | p99 FLIP | A/B | S3 GPU p50 / p99 saved | S1 GPU p50 / p99 saved | Verdict |
  |---|---|---|---|---|---|---|
  | lod, 60 / 30 px | 0.01007 | 0.1977 | pass | +0.10 / +0.07 ms | +0.37 / +0.23 ms | superseded |
  | **lod, 120 / 60 px** | 0.02428 | 0.3667 | pass | **+0.53 / +0.67 ms** | **+0.99 / +0.88 ms** | **accepted** |
  | shadow-proxy, LOD2 (LOD1) | 0.03048 (0.03090) | 0.2957 (0.3011) | fail: mean | +0.23 / -0.73 ms | +1.14 / +1.13 ms | rejected |
  | **clips30** | 0.00669 | 0.0971 | pass | -0.08 / -0.08 ms | -0.12 / -0.14 ms | **accepted**: bone textures halved |
  | light-cell, 3 m | 0.01369 | 0.4726 | fail: p99 | +0.13 / +0.19 ms | | rejected |
  | light-cell, 2 m | 0.00964 | 0.3292 | pass alone | +0.07 / +0.14 ms | | rejected: fails combined |
  | merge, 8 px | 0.00099 | 0.0343 | pass | -0.01 / -0.04 ms | | rejected: saves nothing |
  | drop-glow | 0.00592 | 0.3734 | pass alone | +0.02 / +0.03 ms | | rejected: fails combined |
  | **lod 120/60 + clips30** | 0.02632 | 0.3682 | pass | | | **the defaults** |

  The passing cuts together fail: with light-cell 2 m and drop-glow added, 0.03477 / 0.5147 (lod
  at 60/30: 0.02386 / 0.4702); lod, clips30 and light-cell 2 m, 0.03142 / 0.4466. Both cuts that
  tip the sum save nothing measurable, so the defaults keep LOD at 120 / 60 and clips30 only.
- **Choices, with reasons:**
  - Decimation: Unity 6.6's own Mesh LOD generation (`MeshLodUtility.GenerateMeshLods`, which
    rewrites index buffers only, so bone indices and weights in UV1 and UV2 survive), copied into
    separate LOD1 and LOD2 meshes over the same vertices so the indirect draws stay one mesh per
    LOD. No third-party simplifier is available offline. LOD1 is 51 to 59% of LOD0's triangles
    and LOD2 25 to 31% (goblin 73% and 36%: its seams stop the simplifier).
  - LOD thresholds: 60 / 30 px (performance.md's example) passes with room and saves 0.1 to 0.4 ms;
    120 / 60 still passes and saves 0.5 to 1.0 ms (at 40 m the 1 to 2 m roster projects to roughly
    50 to 100 px, so more of it drops a level). Hysteresis (10%) and the 0.2 s dithered crossfade are TD5's.
  - clips30 saves no measurable GPU time (the palette pass blends each bone once a frame either
    way) but halves the bone textures' size (17 MB at 60 Hz), and passes far below the lines, so it
    stays on. The 30 Hz texture is baked beside the 60 Hz one; `CROWD_RATE` is a per-type
    `_ClipRate`.
  - Shadow proxy: any mesh other than the drawn one shifts every monster's self-shadowing, so LOD1
    fails like LOD2. It is the largest saving (about 1.1 ms in S1); a retry would pair it with a
    larger shadow normal bias, which also changes A.
  - Merge: at 40 m 8 px is about 17 cm; 21 of 2,734 spawns (0.8%) merged, and its O(n^2) per-frame
    pass costs CPU on beat frames for no gain. A merged spawn's density brightens additive
    particles k times and gives alpha the coverage of k layers.
  - Glow quads: the selection's `glow` list (one additive soft-point billboard of 1.5 m or more:
    hit-spark-b's Glow, death-burst's Flash, death-burst-large's ExplWave, slam-shock's
    Glow(Optional) and Flash, the bolt's Glow); the laser is left out (its child names repeat).
- **What it doesn't show:** every cost run was noisy (TD14's and TD15's sessions used 15 to 100%
  of the CPU, at times a quarter of the GPU), so main-thread times are contention and GPU
  differences under about 0.15 ms are noise. Neither frontier search is a frontier: every cut off
  stopped at 380 on main-thread contention (GPU p99 2.85 ms at 380, 3.78 at 495, 4.45 at 646,
  crossing 4.2 ms near 600, which set N); the defaults stopped at 180, main-bound (main p99 8 to
  34 ms). TD12 measures the frontier with the defaults. The captures and costs ran before the
  merge of TD10's flash tiers (A and every B on the same build). The operator's review of the
  stills and videos is pending (row above).
- **Tests:** EditMode 193/193 (TD11's 5 in `CutsTests`: cut specs; LODs share vertices and streams
  with fewer triangles; 30 Hz rows equal the 60 Hz rows at the same times; the glow quads marked;
  merge, drop-glow and the light cell on the CPU side). `demo3d check --player`: vendor guard,
  dotnet test, cold EditMode and the IL2CPP build ok.
- **Self-review** (AGENTS.md's checklist):
  - Downstream needs: TD12 runs with the defaults (lod 120/60, clips30), and a sweep can turn
    them off with sceneOptions `cuts: none`; TD14's E3 and E4 use `thresholds.json` and can capture
    with `td11.py capture <name> -- <flags>` and score with `td11.py cut`; TD5's crowd bench now
    also runs with the defaults.
  - Claims: every number is in `evidence/td11/` (thresholds, scores, costs with each run's row),
    with what the noisy costs and the contended frontiers don't show stated above and in the README.
  - Edge cases: a library without LODs or a 30 Hz texture falls back to LOD0 and 60 Hz with a
    warning; the last 30 Hz frame is the clip's end; a spawn behind the camera never merges; a
    seek or reset clears the merge queue; merged spawns keep every light cue.
  - Tests against the source: the 30 Hz rows against the 60 Hz bake (from Unity's own sampling),
    the LOD meshes against LOD0's streams; the A/B scores are against the uncut client.
  - Concurrency: not applicable (main-thread only).
- **Tool problems:** a cold `demo3d unity BakeAll` hung after Unity exited: the LicensingClient
  it launched inherited the output pipe (the wrapper was killed by PID). A build failed in
  ILPostProcessing under load (gRPC thread-pool starvation), then the warm editor's ILPP runner
  stayed broken until restarted, and the restart once crashed in UDS initialization; the next start
  worked. `EffectSystem.Dispose` called `Destroy` in edit mode (fixed). A `redefinition of 'k'` in
  my EffectSim edit made every kernel invalid in the player with only a log line (fixed).
- **Review** (the demo systems milestone's cold review, TD8, TD10 and TD11; branch
  `td/td11-review` from `demo3d` at `7584db7`, merged with `2256e1d` for TD15's final arena before
  any capture). Two findings on TD11, both fixed:
  - **2, the invisible anchor moved the camera:** `--time-offset` fed CameraFeel's punch and shake,
    so the anchor's scores followed beat phase and the pass line sat about 30 times the floor.
    Fixed in `73f463c` (TD10: punch and shake on t minus the offset). Punch and shake stay on, alike
    in A, B and both anchors. Recapturing on TD15's scene also showed its rune circle, mist cards,
    flame flicker and kit particles running on wall time (`_Time`, `Time.time`), which put the
    floor at 0.0103 / 0.369: they now run on `ArenaClock` (song time in the demo, fixed particle
    seeds in fixed time; `Runtime/Arena/ArenaClock.cs`), and the floor is 0.00106 / 0.0374.
  - **6, the evidence didn't record parameters:** the player logs the effective cuts and every
    parameter at startup (`Cuts.Describe()`: `effective cuts [...] lodPx ... shadowLod ... lightCell
    ... mergePx ...`); `td11.py` passes every parameter explicitly (`PARAMS`, `ARMS`) and writes
    the player's line into each capture's `args.json`, each score's `args` and each cost run; the
    first pass's costs moved to `costs/pre-review/` with their parameters reconstructed from that
    build's defaults.
  - **New thresholds** ([thresholds.json](../../demo3d/evidence/td11/thresholds.json), for TD14's
    E3 and E4): invisible anchor mean 0.01608, p99 0.1907; visible anchor 0.05911 / 0.5884; **a cut
    passes at mean <= 0.03042 and p99 <= 0.3233**.
  - **New scores** (every run against the same base, parameters in the names):

    | Run | mean | p99 | A/B | Verdict |
    |---|---|---|---|---|
    | lod 60/30 | 0.01886 | 0.3175 | pass | rejected: fails beside the shadow proxy, saves less |
    | lod 90/45, lod 120/60 | 0.03599, 0.04493 | 0.4577, 0.5253 | fail | rejected |
    | **shadow-proxy, LOD2** | 0.02524 | 0.2329 | pass | **accepted** |
    | shadow-proxy, LOD1 | 0.02549 | 0.2352 | pass | not taken (LOD2 is smaller) |
    | **clips30** | 0.00995 | 0.1385 | pass | **accepted** |
    | light-cell 3 m | 0.01235 | 0.4510 | fail | rejected |
    | light-cell 2 m | 0.00877 | 0.3030 | pass | rejected: fails in the set |
    | merge 8 px | 0.00108 | 0.0336 | pass | rejected: saves nothing (8 of 2,575 spawns merged) |
    | drop-glow | 0.00587 | 0.3711 | fail | rejected |
    | lod 60/30 + shadow-proxy (+ clips30, + light-cell 2 m) | 0.03826 (0.04093, 0.04534) | 0.3530 (0.3541, 0.4198) | fail | |
    | shadow-proxy + clips30 + light-cell 2 m | 0.03416 | 0.3380 | fail | |
    | **shadow-proxy (LOD2) + clips30** | 0.02923 | 0.2408 | pass | **the defaults** |
    | lod 60/30 + clips30 | 0.02467 | 0.3191 | pass | saves less |

  - **Costs of the two passing sets** (GPU p50 / p99 saved, medians of 2 interleaved runs, all noisy
    from other sessions' CPU; main thread near 3.4 and 5.8 ms): shadow-proxy + clips30 saves 0.50 /
    0.53 ms in S3 at 600 and 1.35 / 1.37 ms in S1 at 1,200; lod 60/30 + clips30 saves 0.04 / 0.10 and
    0.26 / 0.24 ms. **The defaults are now the shadow proxy from LOD2 and 30 Hz clips** (`Cuts.cs`;
    LOD at 120 / 60 px is no longer on, and LOD's default thresholds are 60 / 30 px when it is).
    No frontier was re-run; TD12 measures it with these defaults.
  - Evidence: [README](../../demo3d/evidence/td11/README.md) (Results), `scores/`, `costs/`, `ab/`;
    captures in `D:\wt\td11-review\captures\td11\`. Run: `td11.py calibrate`, `td11.py cut all`,
    `td11.py cut <name> --cuts <spec> --param key=value`, `td11.py cost <name> ...`, `td11.py table`.
  - Commits: `f68daca` (finding 6), `94e1194` (merge of `2256e1d`), and the commit adding this
    bullet (ArenaClock, the new defaults, evidence and notes). Tests: EditMode 199/199;
    `demo3d check --player` ok.

### TD12

Status: Done (2026-10-28, on Cygnus). The per-pass estimates wait for Developer Mode (Pending on
the operator); the compute estimate stands meanwhile.

- **Commits** (branch `td/td12`, from `demo3d` at `b1b4d73`): `16e1f11` (the envelope in the level
  script, the sweep specs, `demo3d pix` saving its capture, the two tests), and this entry's
  commit (the report, evidence and notes).
- **Run:** in a set-up worktree, `demo3d unity BakeAll` and `demo3d build --backend il2cpp`; the
  sweeps are `demo3d sweep demo3d/sweeps/td12/<spec>.json --out <dir>` (the full sweep is
  `s3-msaa4-demo.json s3-msaa4-light.json s3-msaa4-heavy.json` in one invocation; every command is
  in the report's Run commands); tables: `uv run --project demo3d/tools python
  demo3d/evidence/td12/summarize.py <dirs> --out table.md`. The demo now plays at the envelope by
  default (TD9's run command).
- **The frontier** ([report](../../demo3d/evidence/td12/report.md); S3 in the finished arena, E2 at
  2×, TD11's defaults, MSAA 4×; requested N, on screen in brackets; every point GPU-limited, compute
  estimate 12.1 to 12.6 ms):

  | load | 30 m | 40 m | 55 m |
  |---|---|---|---|
  | light | 750 (748) | 862 (881) | 1,070 (1,094) |
  | **demo** | **591 (600)** | 730 (750) | 839 (863) |
  | heavy | 417 (435) | 577 (597) | 735 (770), re-run in one window |

  Fill counts 664 / 1,180 / 2,232: only light load at 30 m fills its screen. Main thread p99 stays
  2.7 to 5.9 ms, render 0.7 to 1.0 ms.
- **The envelope: 30 m, 500 on screen** (**TD13 lowered it to 425**: the full song failed in the
  climax at 500, 475 and 450; TD13's entry and the report's correction) (no width fills its screen at demo load, so 30 m; 591
  rounded down). In `action-level.json`, its sections' widths scaled by 0.75 (22.5 to 30 m). Held
  at 500: GPU p99 3.74 ms, compute estimate 11.2 ms, sim tick 1.38 ms. At the published RTX 4060
  Laptop Steel Nomad median (GPU line 4.69 ms instead of 4.2, TD7's sources) the 30 m point
  searches to 655 (661 on screen, still under 664): 30 m, 600. The design's line is kept.
- **Per-pass (PIX):** not obtained. `demo3d pix` had never saved a capture (pixtool keeps it:
  fixed, `--open save-capture`); both captures now exist (the heavy one with `--frames 1`) and
  replay stops at `E_PIX_FEATURE_REQUIRES_DEVELOPER_MODE`. The per-pass estimate lies between GPU
  p99 × 2.4 and × 3.0, so the compute estimate is its upper bound: 9.0 to 11.2 ms at the envelope.
- **S6 at the envelope holds** (30 m, 500 requested, about 470 on screen: GPU p99 3.16 to 3.37 ms,
  3 of 3 pass). S6's own 30 m frontier: 873 requested (about 630 on screen), GPU-limited.
- **The sim fits its 3.5 ms reserve at the envelope:** tick p99 1.38 ms (S3 at 500), 2.70 ms (S6 at
  500), 0.97 to 2.70 ms at every S3 frontier point. It crosses the line in S6 between 591 (3.25)
  and 846 requested (4.58; S6's frontier reads 4.62), and in S3 between 846 and 1,294.
- **TAA** (the envelope point only): capacity-bound at 30 m (at least 932 on screen, GPU p99 3.67
  ms, sim tick 7.97 ms there). At matched counts TAA saves 38 % of S3's GPU time and 18 % of S1's
  (TD9: 11 to 13 %): most of MSAA's cost now comes with the effects. MSAA stays (TD9's look).
- **Against stock Unity** (40 m, demo, `crowd: baseline`): 362 (377 on screen), main-thread-bound
  at 10 ms, against the bone-texture path's 730: **2.0×**.
- **Environment** (TD14 and TD15, in the report): S0 at the authored 2× 0.49 / 0.56 ms GPU p99 at
  40 / 55 m against the 1.5 ms ceiling; doubling the decoration costs no monsters within 5 %; E2
  against E0 equal in monsters within 5 %, kept for the static shade.
- **What's noisy:** 370 of 372 runs, mostly for up to 12 % of frames without a GPU time; 63 also for
  other sessions' CPU. The foreground was held throughout (`requireForeground` on). Heavy load at
  55 m isn't steady in its stress timeline (the hero's melee stops after about 15 s), so its noise
  repeats measured half the particles and the sweep's 1,017 was re-run in one window (735). S6's
  opening is heavier than its later windows.
- **For TD13:** the envelope is 500 at 30 m. The song at it (`dotnet ... song`) holds a mean of 317
  on screen in the final peak (target 500) and 319 in drop-2 (400), so the climax doesn't reach the
  envelope yet. Heavy load's 30 m frontier (417) is under 500: a climax with heavy-preset effects
  won't hold it; spells first, as the plan says. TD15's `beauty.py` should run at `n` 500 and 30 m (425 since TD13 lowered the envelope).
- **Tests:** two changed with the envelope. `LevelScriptCoversEverySongSectionWithinTheEnvelope`
  no longer pins 2000 (a count rounded to 100 at a measured width). `FramingFollowsTheHeroAndEasesTheZoom`
  allowed 3 m of camera lag; at 30 m one slam's root motion (about 7 m in under a second) falls
  on the way to a goal and the 0.4 s spring trails it by 4.7 m at 173.4 s, so lag now stays under
  6 m and over 3 m in at most 1 % of samples. Clamping every width to 30 m instead failed the
  breakdown's kill rate (0.88); scaling the arc passes it. `demo3d check --player`: vendor guard
  (5,275 files) ok, dotnet 119/119, cold EditMode 203/203, IL2CPP build ok.
- **Self-review** (AGENTS.md's checklist):
  - Downstream needs: TD13 (the climax holding the envelope) gets the count, the width, the
    sections' clamped widths, the climax's current shortfall, the heavy-load frontier and the
    5 % step-down's baseline; integration gets the Developer Mode row with exact commands. Done.
  - Claims: every number is in `evidence/td12/` rows (372 runs), frontier files and logs, or quoted
    from TD11, TD14 and TD15's evidence with its source; the 4.69 ms counts other than 30 m are
    labelled fitted; the MSAA effects split is by subtraction. Done.
  - Edge cases: the envelope rule with no width qualifying (30 m fallback); capacity-bound TAA
    reported by on-screen count; requested against on-screen counts; non-steady stress windows
    (heavy at 55 m re-run, S6 noted); the zero-GPU-time frames' possible low bias. Done.
  - Tests against the source: `summarize.py`'s frontier numbers come from the harness's own
    frontier.jsonl and match its report.md; the 4.69 ms line was searched by the harness, not only
    fitted. Done.
  - Concurrency: not applicable (no threads added).
- **Tool problems:** `demo3d pix` wrote no capture (fixed); PIX's 3-frame capture of heavy load
  killed the player twice (one frame worked); `dotnet test`'s 3 s song-build test failed once under
  another session's load (4.3 s; passed on rerun); each build rewrites `URP-Demo.asset` and
  `UniversalRenderPipelineGlobalSettings.asset` (reverted, not committed). Another session's tests
  and builds ran in `D:\wt\demo3d` beside the sweeps.

### TD13

Status: Done (2026-10-29, on Cygnus), with the answer milestone's review recorded below. The
viewing and listening checks are the operator's (rows above).

- **Commits** (on `demo3d`): `f44309d` (the song check, the clearing ending), `2c7ddb0` (look-ahead
  releases, the route, the reserve), `8b84006` (a window test fix), `41912e3` (the song run
  uncapped, light clustering that coarsens, the climax's spells thinned, the envelope at 425 and
  TD12's correction), `229a3ee` (the breakdown holds ground), `b1626af` (evidence, notes, Pending
  rows), and this entry's commit (the review's fixes, the re-runs, the video re-captured). The merges
  `8b9d3b0` (TD12) and `7f74c94` (the relay's skeleton LOD work from another session) sit between.
- **Run** (from `demo3d/`, after `demo3d unity BakeAll` and `demo3d build --backend il2cpp`):
  `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width 1920 -screen-height 1080
  -screen-fullscreen 0 -job-worker-count 6 --overlay` (the whole song); the check adds
  `--song-check <out.json> --stats <out.csv>` (uncapped); the video, `--capture-video
  ..\captures\td13\demo.mp4 --dump-sounds ..\captures\td13\sounds.json`, then from the root
  `uv run --project demo3d/tools demo3d mix --events captures/td13/sounds.json --video
  captures/td13/demo.mp4 --out captures/td13/demo-mixed.mp4`.
- **The song passes at an envelope of 425** ([evidence](../../demo3d/evidence/td13/README.md)):
  two runs after the review's fixes, every window within the lines (worst GPU p99 4.022 and 4.026
  ms, the last window: the climax's last 7 s, the outro and the ending's clear; the climax's own
  windows at most 3.79 ms), main thread p99 under 3.6 ms, render under 1.3 ms, no frame after the
  warm-up over twice its line (largest GPU frame 4.72 ms), no rejected spawn, no window dropping
  light, the whole song covered. One run's machine was noisy by the harness's rule (other
  processes at 10.6% CPU); the other was quiet. 450 fails under the same measurement (4.30 and 4.27
  ms).
- **The steps, as the plan orders them:** (1) the climax's spells thinned to the demo load plus
  the laser (bolts on beats, not eighths; lightning every third beat, not every second): the
  climax still failed at 500 (4.34 to 4.54 ms), and its particles matched drop-2's (about 16,000);
  (2) the boss has no effects of its own beyond the shared hit sparks, so nothing to thin; (3) the
  envelope lowered in 5% steps: 475 failed, 450 passed once at 4.176 ms and failed once at 4.208
  ms, 425 passed twice; the review's re-runs confirm it (above). TD12's report carries the
  correction at its top: S3 holds 500 at 3.74 ms, but the song's climax costs more at the same
  count; what makes it so (the boss, the laser, the arena around the courtyard, the light
  clustering) isn't isolated.
- **The climax holds the envelope:** at TD12's 500 the final peak averaged 317 on screen. Three
  causes, three changes:
  - The route walked the hero through the walled courtyard in the climax, where the walls split
    the horde (about 215 on screen). The level script's goals now run ruins gate (groove-2),
    courtyard (build) and the courtyard field (drop-2); the breakdown and the final peak hold
    ground. The hero reaches none of those three goals at this density: drop-2 ends, and the final
    peak holds, at about (9.5, 16), against the courtyard's east wall
    ([choreography output](../../demo3d/evidence/td13/choreography/song-onscreen.txt)).
  - Monsters for a fuller section only started walking in at its start, so drops filled over 20 s.
    Releases and refills now look 4 s ahead to a section with a larger target (`Sim.TargetAhead`).
  - The sim holds about 94% of a target while 25 monsters a second die (dying ones count on
    screen), so the reserve is 1.5x and the final peak asks for 1.05 of the envelope.
  In the choreography at 425 the climax holds 426 on average and 452 at most (alive and dying); the
  player counted 413 and 428 (`final-1-stats.csv`).
- **The ending clears the horde:** the level script's `clear` makes the ending's accent slams kill
  the monsters on screen, nearest first, so each accent left takes as many and the last takes the
  rest and the off-screen band its drift would reveal; a clearing section releases and refills
  nothing, and its hero slams on every accent. Nothing alive is on screen from the last slam to the
  end (a dotnet test).
- **Built:** `SongWindows` (Demo3D.Bench, EditMode-tested), `SongRunCheck` (`--song-check`) and
  `FrameTimingCollector` (the harness's frame-commit policy, now shared by BenchRunner and the song
  check); `EffectLights` clusters again at twice the cell, up to 8x, while more clusters are visible
  than the pool holds, and holds a coarsened cell for 0.5 s (the ending's last slam: 335 cues
  against 128 lights dropped 6% before); the choreography CLI's `song --onscreen`.
- **The video:** `D:\wt\demo3d\captures\td13\demo-mixed.mp4` (gitignored; 1080p60, 199 s, re-captured
  with the review's build; the song plus every sound event through the offline mixer, `demo3d mix`;
  the NCS song and vendor sounds never leave the machine). The operator's viewing row is above.
- **Choices:**
  - The song check runs uncapped: at the display's 60 Hz the GPU clocked down and read 5 to 9 ms in
    the near-empty intro. Single frames in the first 5 s are reported, not judged (the first frame
    loads for 1.3 to 1.6 s on the main thread while the song plays), and frames without a GPU time
    mark the run noisy without failing it, as the harness does (7 to 8% here, with the harness's
    own policy); a window with under half its frames timed fails.
  - Light clusters merge instead of dropping: no light switches off on the densest beat, at the
    price of wider, less local light for that beat (the operator's viewing row asks about it).
  - The climax's fraction above 1.0 is a sim setting, not more than the envelope on screen: the
    measured count is what holds the envelope.
  - The breakdown holds ground: walking to the graveyard it killed 0.88 of its preset under Mono
    (EditMode) and 0.93 under .NET, against the test's 10%.
  - The stride test requires strides on a sparse song (an envelope of 100) and checks the song's
    own for direction only: at the envelope the hero almost never strides, since a monster is
    nearly always within reach ahead, and it cuts its way by its blows.
- **Not done here:** the hero reaches no goal after drop-1 at this density, so the tour of the arena
  is shorter than the level script reads (TD15's landmarks still pass through the frame). The NCS
  licence decides whether the Windows build is shared (row above).
- **Self-review** (AGENTS.md's checklist, before the review): downstream needs, done (integration
  needs the song run, the video and the rows; the envelope change is carried into TD12's report and
  entry); claims, done (numbers from `evidence/td13/`); edge cases, done (windows at the run's end,
  warm-up frames, frames without GPU time, a clearing section with no accent left, the light
  growth's cap); tests against the source, done (the ending's clear and the stride rules on the song
  timeline; the windows on synthetic frames with known answers); concurrency, not applicable. The
  review found what the claims and edge-case passes missed (below).
- **The answer milestone's review** (TD12 and TD13; a fresh read-only subagent, no warden skill in
  the session): 13 findings, none blocking in the sim and nothing that could commit vendor or NCS
  files. Fixed in this entry's commit:
  1. A run with no GPU times passed (empty p99 = 0): a window with under half its frames timed now
     fails (test).
  2. The song check committed frames by a looser rule than the harness (it lost 7 to 8% of GPU
     times against the harness's 2% in S3): `FrameTimingCollector` is the harness's policy, used by
     both; the re-runs above use it. The share without a GPU time stayed 7.8 to 7.9% in the song,
     so it comes from the frame rate, not the policy.
  3. Frames and song times were on different clocks (windows labelled up to 1.5 s early, drops
     judged in the wrong windows; a latent unsigned underflow): each frame is now placed at its own
     Update's song time through the QueryPerformanceCounter map the harness checks, frames before
     the first Update are dropped, and window times are song seconds.
  4. Whether the run's last seconds were judged depended on its length modulo 5 s: a window ending
     at the last frame is always added (test).
  5. The quiet-machine condition wasn't checked or stated, and verdicts didn't say what produced
     them: the harness's machine samples, the build and the command line are now in every verdict;
     a noisy run is reported, as the harness reports one, not failed.
  6. The rule as built differed from performance.md, and a garbage GPU reading (1.6e13 ms, seen in
     a warm-up) would have failed a run after the warm-up: performance.md's song paragraph now
     states the rule as built, and GPU times over a second are counted and treated as missing
     (test).
  8. Coarsened light cells had no hysteresis: a coarsened cell now holds 0.5 s (test). The merged
     clusters' look (summed intensity at the largest cue's range) is left to the operator's viewing
     row.
  9. Evidence claims: 500's range (4.34, not 4.45), 450-b's failing windows (2, not 3), "no light
     dropped" (now "no window dropping light"), the cause of the climax's cost (now "not isolated"),
     and the choreography numbers (now kept in `evidence/td13/choreography/`) are corrected.
  10. The stride test summed strides over the song and a sparse song: strides are now required on
     the sparse song and the song's own checked for direction; the claim that drop-2 reaches the
     field and the climax holds on open ground is corrected (it holds against the courtyard's
     wall).
  11. `summarize.py` judged "fills" by the requested count: it uses the on-screen count, and
     `table.md`'s S6 row reads "no" (627 on screen against 664; the envelope is unaffected).
  12. Stale rows: the PIX row's "envelope point", the S6 row's `--n 500` and the `beauty.py` note now
     say 425.
  13. A partial run (`--start-at`, `--seconds`) could pass: a run that doesn't cover the whole song
     now fails (`wholeSong`; judged by the run's Updates, since the first measured frame lands near
     1 s after the load).
  Dismissed: 7, the clearing ending assumes each remaining accent gets a slam. In this song both
  accents do and the test proves the screen clear; a level script whose ending accents sit closer
  than a slam would need the sweep, and none does.

### TD14

Status: Done (2026-10-28, on Cygnus). Every rung measured and kept or dropped; the final rung is
E2 (TD9's lighting) with the triplanar rock port. E3 and E4 didn't wait: TD11 landed during the
task, and their bounds (below) ruled them out without an A/B check.

- **Commits** (branch `td/td14`, from `demo3d` at `ede3af3`): `00ffc63` (the rung switches,
  the merged scenes, the texture cap's import override, DOTS instancing in LitStaticShadow, the
  triplanar rock, the sweep specs and table script), `5cf8b95` (merge of `demo3d` at `2a2df25`:
  TD8's effects, TD10, TD13), `dbea273` (the drawer's resources kept in the build), `646b27f`
  (merge of `demo3d` at `2256e1d`: TD15's art pass, TD11's cuts), `80f5b34` (E1 dropped from the
  project settings; round 2's specs), `2bb73db` (the E3 and E4 bounds), and this entry's commit
  (evidence and notes).
- **Run:** `demo3d unity BakeAll` (the arena baker now bakes E2 at 0.5–4× and merged variants at
  1× and 2×; `BakeArena -merged <densities>`), `demo3d build --backend il2cpp`. The demo is TD9's
  command and default (E2 at 1×). Rung switches, as sweep sceneOptions (`rung` for S0, `arena`
  for S1–S6) or demo flags: `merged: true` / `--merged`, `texCap: 1024` / `--tex-cap 1024`,
  `prepass: "out"` / `--prepass out`, bench-only `without: "small"|"forest"` / `--without`, and
  `grd: on|occlusion` / `--grd`, which needs `ProjectSetup.GpuResidentDrawer = true`, Configure
  and a rebuild (it logs an error otherwise). Sweeps: `demo3d sweep demo3d/sweeps/td14/<spec>.json
  --out <dir>` (one spec per invocation frees the machine lock between them; round 3's list is in
  `evidence/td14/README.md`); tables: `uv run --project demo3d/tools python
  demo3d/evidence/td14/summarize.py <dir>`. Stills: `demo3d unity
  Demo3D.EditorTools.Arena.ArenaStills.Run -rung e2 -density 1 -out evidence/td14/stills -task TD14
  -shots forest-edge,graveyard [-prepassOut] [-texCap 1024] [-merged] [-crop x,y,w,h,scale]`.
- **The ladder** ([evidence](../../demo3d/evidence/td14/README.md); GPU / main / render p99 ms,
  S0 at 1×, 40 m and 55 m; S3 at 2,000, 40 m, GPU p99, against its same-round reference; E1 on
  TD9's arena, the rest on TD15's):

  | rung | S0 1×, 40 m | S0 1×, 55 m | S0 2×, 40 / 55 m GPU | S3 at 2,000 | verdict |
  |---|---|---|---|---|---|
  | E0 stock | 0.44 / 1.16 / 0.68 | 0.47 / 1.25 / 0.74 | 0.46 / 0.51 | 7.62; frontier 723 | baseline |
  | E1 drawer on E0 (TD9's arena; E0 there 0.42 / 1.08, 0.46 / 2.13 main) | 0.44 / 0.99 / 0.62 | 0.45 / 0.94 / 0.60 | 0.48 / 0.85 (noisy) | 8.91 (E0 9.18) | helps E0's CPU only |
  | E1 + GPU occlusion | 0.49 / 1.24 / 1.01 | 0.51 / 1.25 / 0.97 | 0.50 / 0.53 | 9.41 | dropped |
  | E1 + merged small props | 0.44 / 0.96 / 0.59 | 0.46 / 0.92 / 0.59 | 0.46 / 0.48 | 9.52 | dropped |
  | E2 + E1 drawer (E2 there 0.41 / 0.85, 0.44 / 0.91) | 0.41 / 0.97 / 0.60 | 0.43 / 0.93 / 0.59 | 0.41 / 0.44 | 9.16 (E2 8.89) | E1 dropped |
  | **E2 lightmaps, static shadow map** | **0.46 / 1.04 / 0.69** | **0.51 / 1.11 / 0.73** | **0.47 / 0.55** | **8.04; frontier 716** | **kept (for the static shade; costs S3 GPU)** |
  | E3 bound: small props hidden | 0.43 / 0.95 / 0.61 | 0.70 / 1.72 / 0.96 (noisy) | 0.45 / 0.49 | 7.61 | dropped |
  | E4 bound: forest interior hidden | 0.44 / 1.03 / 0.66 | 0.46 / 1.03 / 0.64 | 0.45 / 0.47 | 7.82 | dropped |
  | E5 kit textures capped at 1,024 | 0.46 / 0.93 / 0.62 | 0.51 / 0.99 / 0.64 | 0.47 / 0.55 | 7.83 | dropped |
  | E6 static set out of the prepass | 0.45 / 1.00 / 0.63 | 0.48 / 0.96 / 0.63 | 0.45 / 0.51 | 7.89 | dropped |

- **The final rung against E0:** S0 GPU +0.02 / +0.04 ms (the kit's static-shadow taps and probe
  light), main −0.12 / −0.14 ms, render equal; draws 747 → 326 at 40 m (1,028 → 533 at 55 m) and
  no kit shadow casters; S3's 40 m frontier 716 against 723, equal within the search's 5 %.
  Restated after the scene milestone's review: the main-thread difference (1.04 against 1.16 ms)
  is inside E2's own 0.13–0.15 ms spread between attempts, so it is no win, and in S3 E2 is
  GPU-slower than E0 at every matched N (3.82 against 3.69 ms at 646, the search probes; 8.04 against 7.62 ms at
  2,000: about 4–6 %, the static map's taps on every monster pixel). **E2 is kept because its
  static shadow map is how monsters take the trees' static shade (TD9), at that S3 GPU cost**, not
  for time saved. (Not re-run: E0 and E2 were measured in the same round, not interleaved.)
- **The density curve** (E2; GPU / main / render p99 ms at 40 m and 55 m): 0.5× (1,419 kit
  renderers) 0.45 / 0.91 / 0.59 and 0.47 / 0.99 / 0.64; 1× (1,903) 0.46 / 1.04 / 0.69 and 0.51 /
  1.11 / 0.73; 2× (2,598) 0.47 / 0.91 / 0.61 and 0.55 / 1.01 / 0.67; 4× (3,271) 0.48 / 0.93 / 0.60
  and 0.59 / 1.10 / 0.70. S3's 40 m frontier: 716 at 1×, 721 at 2×. **The richness-to-monsters
  trade:** doubling the decoration costs 0.01–0.04 ms of S0's GPU p99 and no monsters within the
  frontier's 5 %; 0.5× to 4× costs 0.03–0.12 ms, 7 to 27 monsters at the frontier's slope (4.5 µs
  per monster).
- **S0 at the authored density fits the ceiling:** 0.46 / 0.51 ms of GPU p99 against 1.5 ms,
  main 1.04 / 1.11 and render 0.69 / 0.73 ms against 1.7 ms. 4× fits too (0.59 ms at 55 m), which
  is TD15's headroom: the ceiling doesn't limit decoration density up to 4×.
- **Choices, with reasons:**
  - E1 dropped although it works (the drawer took E0's draws from 524 to 112 and E0's main thread
    at 55 m from 2.13 to 0.94 ms): on top of E2, which already takes the casters that multiplied
    the draws, it saved nothing at 1×, 2× or 4×, and keeping it costs every lit build URP Lit's
    DOTS instancing variants (the first such build took 41 minutes of shader compiles, the next
    97). The code stays: LitStaticShadow and RockTriplanar keep DOTS instancing, ArenaPipeline
    switches the drawer per run, and `ProjectSetup.GpuResidentDrawer` brings the settings back.
    GPU occlusion culling cost 0.05 ms of GPU and 0.4 ms of render thread with nothing to cull in
    S0's open views.
  - Merging small props: our own merge (one mesh per 16 m cell, material, shadow mode and probe
    use; 688 props into 111 renderers at 1×), not Mesh Combine Studio, which isn't in the
    vendor project; the prototype rule prefers it, and it measures the same thing. It cut draws
    by about 20 % and no time, and a cell's one probe sample flattens the kit's indirect light.
  - E3 and E4 measured by their bounds: hiding the small props, or the forest's interior, after
    the static shadow map took them, bounds what any LOD or impostor could save. The bounds are
    0.02–0.08 ms of S0 GPU (one noisy 55 m run aside) and 0.2–0.4 ms of S3 at 2,000, inside S3's
    run-to-run spread; an LOD keeps the pieces' pixels, so it would save less. No candidate could
    clear the keep rule, so none was built and no A/B ran (TD11's thresholds were ready).
  - E5: the cap is an import override (KitTextureImport puts the four classic packs' textures in
    a mipmap limit group; ArenaPipeline sets its limit), so no vendor file or .meta changes. At
    1,024 (and 512) nothing moved; the atlases are already shared per pack (5 atlases plus two
    tiling ruin textures and the rock's), so there was no atlas to merge. The memory saving
    (about 70 to 18 MB by size) isn't measured: the release player has no Gfx memory counter.
  - E6: with MSAA 4× URP does no depth priming, so the prepass only feeds SSAO and the depth
    texture. Leaving the static set out halved E2's draws again for at most 0.04 ms and took SSAO
    contact shade off the kit (`stills/arena-e2-1x-prepassout-*`).
  - Elven Realm's triplanar rock is ported as hand-written HLSL (`Demo3D/RockTriplanar`), not
    Shader Graph: a graph can't be authored reliably in batch mode, and GPU Resident Drawer
    support, the reason environment.md gives, comes from its DOTS instancing. It reuses URP Lit's
    material layout (mapped in its header), so the SRP Batcher, URP Lit's shadow and depth passes
    and TD15's kit value apply; it lights through Demo3DLightingPBR, so at E2 it takes the static
    shadow map. Its look is in Pending on the operator.
  - The drawer's switch: a build keeps the drawer's compute resources only when a build pipeline
    asset has it on, so (with the setting on) the asset has it on and every player starts on a
    drawer-off copy (`ArenaPipeline.Boot`); a copy made at runtime from a drawer-off asset failed
    with a null reference in `InstanceDataSystem` (round 1's first run).
- **Tests:** EditMode 201/201 (TD14's: the rung switches' parsing and refusals, the kit textures'
  group, every scene's triangles against its placement list, merged or not, and the merged
  renderers; E2 at every density). `demo3d check --player` passes (below).
- **For TD15** (its headroom step): S0 at E2 stays within the ceiling up to 4× (0.59 / 1.10 /
  0.70 ms at 55 m) and S3's frontier doesn't move from 1× to 2×, so density is limited by the
  look, not the budget; rerun `td15-s0-e2-{1,2}x.json` and `s0-e2-4x.json` after raising it.
  **For TD12** (the report's environment section): the density curve, the trade, and the final
  rung against E0 above, with rows in `evidence/td14/r3/`; the frontier there is TD11's default
  cuts on TD15's arena (716 at 40 m, demo load).
- **Self-review** (AGENTS.md's checklist): downstream needs, done (TD15's headroom step and TD12's
  environment section read; both get their numbers and run commands above); claims, done (every
  number is in `evidence/td14/r{1,2,3}/` rows and tables, with what they don't show: CPU-bound S0,
  S3's run-to-run spread, E1 only on TD9's arena, bounds instead of built LODs and impostors, no
  memory counter); after the scene milestone's review: E1 was dropped on TD9's smaller arena (before
  TD15's 1,903-renderer dressing), so its verdict on today's scene is by extension; round 3's S0 E2
  4× 40 m row reads 0 draws and set-passes (a bad counter frame, not a measurement); `summarize.py`
  now keeps rounds and run kinds (fixed runs, search probes) apart, which it had merged, and the
  rounds' tables were regenerated; edge cases, done (merged scenes with multi-material pieces split per submesh
  and checked by the triangle test; a cell over 65,535 vertices gets 32-bit indices; `grd` asked
  for without the drawer in the build logs an error and runs without it; unknown switch values
  refuse; the texture group is a no-op at limit 0); tests against the source, done (scenes
  against the placement list, merged scenes against the unmerged triangle counts; the drawer's
  activity read from its own state, `grdActive` in rows, and its draw counts); concurrency, not
  applicable.
- **Tool problems:** `demo3d build`'s client timed out after 3,600 s while the editor compiled
  5,376 Lit variants (the build finished in the editor after 97 min, as TD9 saw); `demo3d unity
  BakeAll` after each merge failed until `demo3d setup` synced the newly listed vendor assets
  (TD8's effect prefabs, TD15's kit pieces); the first stills after a build rendered the kit in
  placeholder colours while the editor compiled shaders (rerun). TD11's and TD15's sweeps shared
  the machine lock; TD14's final round ran one spec per sweep so they could interleave.

### TD15

Status: Done (2026-10-28, on Cygnus). The headroom step ran after TD14 landed (authored density
2×), with the scene milestone's review fixed in the same round (Review, below).

- **Commits** (branch `td/td15`, from `demo3d` at `df37614`): `9f922cc` (the art pass, the beauty
  shots' player modes, the reference stills, tests), `0e59670` (merge of `demo3d` at `2a2df25`),
  `85590ed` (dressing after the scatter, the candle ring, canopy twins refreshed each bake),
  `3e85e94` (evidence and notes); the headroom round on `td/td15-headroom` (from `demo3d` at
  `7645849`, TD14 and TD11 merged): `456ef3b` (authored density 2× end to end and the review's fixes), and this entry's commit (evidence, notes, TD14's restated claims).
- **Run:** in a set-up worktree, `demo3d unity BakeAll` (the arena, its dressing and the fireflies),
  `demo3d build --backend il2cpp`; the demo as TD9's run command (the art pass is in the default E2
  scene). Beauty shots and the measure: `uv run --project demo3d/tools python
  demo3d/evidence/td15/beauty.py [--shots a,b] [--measure-only] [--tag t --player-args "..."]`
  (holds the machine lock; one shot by hand: `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width
  1920 -screen-height 1080 -screen-fullscreen 0 --scene S3 --load demo --n 2000 --view-width 40
  --hold-at 44,40 --start-at 17 --stills-at 20 --capture-stills <dir> --capture-mask`, and again with
  `--beauty-empty`). S0: `demo3d sweep demo3d/sweeps/td15-s0-e2-1x.json
  demo3d/sweeps/td15-s0-e2-2x.json --out <dir>`. Reference stills: sync a pack's demo scene with a
  temporary manifest entry (never committed), then `demo3d unity
  Demo3D.EditorTools.Arena.ReferenceStills.Run -scene Assets/Vendor/<pack>/Scenes/<scene>.unity
  -name <n> -out evidence/td15/reference`. The canopy check: `demo3d unity
  Demo3D.EditorTools.Arena.ArenaStills.Run -rung e2 -heroes -canopyCheck -18,-48 -out <dir>`.
  Evidence: [demo3d/evidence/td15/](../../demo3d/evidence/td15/README.md).
- **Built** (all tracked as layout parameters in `arena-layout.json`; everything generated lives in
  the gitignored `Assets/Baked/`):
  - Kit: 19 new pieces (Elven Realm's fallen leaves, pebbles, flower clumps, stepping stones, lamp
    posts, lantern, shrine, well, pedestal; Dark Fantasy's moss) with measured footprints; a
    `ground-dressing` scatter layer on clearings, paths and edge bands (flat, non-blocking);
    landmarks: the opening's ring (stepping stones, glowing flowers, eight candles), stepping stones
    down the path, flowers at the glowing tree's base, a stone circle in the courtyard. 1× has
    1,903 kit renderers (TD9: 1,418). The lamps, lantern, shrine, well and pedestal are in the
    kit but unplaced: they block, and any new blocker changes the song's navigation (Tests). All of it is `dressing`: generated after TD9's landmarks and scatter, keeping
    nothing clear, so it never displaces a TD9 piece (the flat pieces on fighting ground skip the
    overlap test, so the clearings stay the same at every density).
  - Atmosphere (`ArenaDressing`, our own shaders, one mesh and one draw each): 12 low mist cards in
    hollows and edges (`Demo3D/MistCard`: drifting value noise, elliptical edge, soft depth fade);
    3 rune circles on the opening, the courtyard and the tombstone ring (`Demo3D/RuneCircle`,
    additive, turning slowly, violet so they stay apart from the spells' blue-white rings); soft
    light pools under the glowing tree and the candles (`Demo3D/GroundGlow`), since the dark ground's
    lightmap barely shows a baked glow light. The glowing tree's light went from 2.5 to 10.
  - Fireflies: Elven Realm's `FX_Fireflies_01` on TD8's runtime (prewarm and flipped rotation
    dropped), with the wisps, nine emitters per 2 s over the view (`AmbientEffects`, shared by the
    demo's `EffectDriver` and now S0). TD8's warm embers left the ambient list (environment.md,
    Color separation: warm is for combat).
  - The canopy fade: at E2 the trees' materials become `Demo3D/LitCanopy` twins (LitStaticShadow's
    colour pass with its new `_CANOPY_FADE` variant, URP Lit's depth and depth-normal passes wrapped
    to clip the same pixels); tree pixels nearer the camera than the hero and within 3.2 m of it on
    screen dither out (`CanopyFade.hlsl`; the demo publishes the hero each frame). E0 stays stock.
  - Value: `kitValue` scales the converted kit materials' RGB (Elven 0.5, Dark Fantasy 0.7, the
    triplanar rock 0.6, brick and stucco 0.75): the packs' pale stone and lilac atlases had been
    the brightest things under the moon. `crowd` gives the horde extra ambient fill (2.5) and a cool
    rim light (0.6, 0.7, 0.95) in `CrowdSkinned`; `--crowd-fill` and `--crowd-rim` override them
    for tuning.
  - Beauty shots: the six `shots` carry the ground focus (and the hero's hold), view width, stress
    time and horde size; the player's `--hold-at x,y` (`StressScript.HoldAt`), `--beauty-empty`
    (no horde, no combat effects) and `--capture-mask` (the same frame again as the ID render:
    the crowd flat white on black, `mask-<t>.png`). The glowing-tree shot moved to (−6, −40) so
    the tree shows whole.
  - Reference stills (`ReferenceStills`): a pack's demo scene in batch mode through our camera, its
    materials as URP Lit in memory, under its own lights and under our night.
- **Readability** (`beauty.py`, [readability.md](../../demo3d/evidence/td15/readability.md)): dL\*,
  the median monster L\* less the median L\* of the ground in a 3–10 px ring around the monsters,
  in a still of the horde without effects (S3 at demo load, 2,000 requested, t = 20 s) and its
  ID render, which since the review draws the set and hero black with their depth, so monsters
  behind a canopy or wall drop out. At the authored 2×: opening 20.6, path 21.0, glowing tree
  18.8, graveyard 20.2, courtyard 19.4, forest edge 19.5. **Floor: dL\* ≥ 17 in every shot**
  (re-derived: the lowest shot, less a margin; the first, undepth-tested measure with effects had
  12 at 1×, its forest edge reading 13.1 because dark monsters on dark ground had been dropped as
  "hidden"). With the crowd's fill and rim off (TD9's crowd light) the opening had read 6.1 under
  the first measure: the horde's light made the difference, the ground stayed put. The evidence's
  ID renders include a few dozen pixels of the stand-in projectile spheres that `--no-effects`
  draws (negligible against 0.6–1.2 M monster pixels); the ID render leaves them out since `456ef3b`.
- **Checklist** (environment.md, Making it look good), every shot: a landmark or edge decoration
  in view; no canopy over fighting ground (the forest edge's canopies cover its edge band); the
  combat effects' warm bursts stand apart, though TD8's blue-white spell rings sit near the runes'
  hue; nothing floats or clips in the stills. The table per shot is in the evidence README.
- **S0 at the authored density** (E2, **2×**, MSAA 4×, TD14's final rung, with the fireflies; GPU /
  main / render p99, medians of 3; [s0-headroom/](../../demo3d/evidence/td15/s0-headroom/table-2x-final.md)):
  40 m 0.49 / 0.91 / 0.60 ms, 55 m 0.56 / 1.03 / 0.67 ms, against the ceiling's 1.5 / 1.7 / 1.7.
  The headroom sweep before the review's dressing fix, same build otherwise
  ([table.md](../../demo3d/evidence/td15/s0-headroom/table.md)): 1× 0.46 / 1.01 / 0.62 and 0.51 /
  1.01 / 0.64; 2× 0.47 / 0.92 / 0.61 and 0.55 / 1.06 / 0.70; 4× 0.49 / 1.00 / 0.63 and 0.60 / 1.10
  / 0.70. At 1× before TD14 (TD15's first round, `evidence/td15/s0/`): 0.46 / 0.89 / 0.59 and 0.50
  / 0.99 / 0.64. CPU-bound near 1,000 fps and marked noisy as TD9's were.
- **The headroom step: authored density 2×** (`decorationDensity: 2.0`). Every density to 4× fits
  the ceiling (TD14: 0.5× to 4× costs 0.01–0.04 ms and 7–27 monsters), so the look chose. Editor
  stills of the shots at 1×, 2× and 4× (E2): 2× fills the edge bands and forest edges with more
  bushes and trees while every clearing, path and the courtyard stay as they are (only edge and
  forest scale); 4× crowds the shots' edges with canopy and puts more of the edge bands in tree
  shade. The density is now one setting end to end: the layout's `decorationDensity` is what the
  choreography navigates and what the player loads by default (`ArenaOptions.AuthoredDensity`;
  `--density` and the `density` sceneOption now also set the timeline's navigation, the review's
  item 3). Raising it changed the song's navigation, so the song tests ran in dotnet and Unity: at
  1.5× and 2× Unity's Mono had the hero brushing a graveyard tombstone at 99 s
  (`NobodyStandsInsideAWall("song")`), at 3× two framing and stride tests failed, while dotnet
  passed every density. The graveyard goal's radius went from 5 to 4 m, which keeps the hero's held
  ground 1.4 m clear of the tombs; every song test then passes at 2× in both (dotnet 119/119,
  EditMode 203/203). The triplanar rock (TD14's port) looks right in the shots: mossy grey-green,
  lit by the moon and in the static shade like the kit; TD14's Pending row stays for the operator.
- **Choices, with reasons:**
  - Readability came from lighting the horde, not brightening the set: the art direction wants a
    dark, low-contrast ground, and the ring's L\* barely moved across trials.
  - Light pools as additive ground cards rather than stronger baked lights: at 6 texels/m on an
    albedo near 0.05 linear, a baked glow light needed intensities that bloomed the lamp posts' heads
    white before it showed on the ground (tried at the opening, before the lamps went).
  - Mist, runes and pools as merged meshes of our own shaders in the baked scene (one draw each,
    nothing per frame on the CPU), not effects: they are static set dressing.
  - The canopy fade only at E2 and up: E0 is the stock baseline.
  - Horde shots in S3 held at each shot's point, not the song: the song reaches only four of the
    six shot positions, and a fixed stress time gives repeatable frames for the measure.
  - Small edits in TD14's files (ArenaBaker: one hook and the kit value; LitStaticShadow: one
    keyword and one call; ArenaScene: the ambient particles) to keep its merge simple.
- **Self-review** (AGENTS.md's checklist):
  - Downstream needs: TD12's report gets S0 at the authored density with the art pass (the rows
    carry TD9's environment counters; the ambient particles run in S0 but their particle counts
    aren't in its rows), and S1–S6 run in the dressed arena with the crowd's fill and rim (no extra
    draws). TD13's capture gets the Pending row, the tracked shots, `beauty.py` (re-run with `n`
    set to TD12's envelope instead of the 2,000 placeholder) and the floor every later change must
    keep. TD14 gets `Demo3D/LitCanopy` (its depth passes carry DOTS instancing; its colour pass is
    LitStaticShadow's, so TD14's DOTS variant reaches it on merge), `dressing` pieces that its prop
    merging may combine, and the headroom step below. Done.
  - Claims: every number is in `evidence/td15/` (readability.md, s0/ rows, the stills) or in the
    bake log quoted (renderer counts); the readability trials are labelled as measured before the
    hidden-pixel exclusion; the README says what the measure does not show. Done.
  - Edge cases: a mask frame never lands in a video (`--capture-mask` is off with
    `--capture-video`) or in an empty run; the canopy fade is off until the demo publishes a hero
    and ignores pixels behind it; canopy twins refresh from their sources every bake (a warm editor
    had kept stale twins: found here, fixed, and tested); dressing on fighting ground is the same at
    every density; the ID render has no scene depth, so pixels the set hides are dropped. Done.
  - Tests against the source: the measure compares the player's own frame with its own ID render
    and empty twin; the shots' points are checked against the placement list's real footprints.
    Done.
  - Concurrency: not applicable (the capture's mask frame is sequential, on the main thread).
- **Review** (the scene milestone's cold review of TD9, TD14 and TD15; each checked against the
  code):
  1. The readability measure's "hidden" pixels (horde still equal to the empty still) also dropped
     dark monsters on dark ground and effects raised monster L\*: **fixed.** The ID render is
     depth-tested (LitStaticShadow and RockTriplanar draw black with depth under `_Demo3DIdMask`,
     `ArenaRuntime.SetIdMask` hides the ground, cards, particles and transparent kit), the measured
     still is a third run with `--no-effects`, the heuristic is gone, the floor was re-derived (17)
     and every shot re-scored.
  2. TD14's E2 "kept" main-thread win is inside E2's spread and E2 is GPU-slower than E0 in S3:
     **fixed (claims).** TD14's entry and evidence now keep E2 for the static shade, with its S3
     GPU cost (3.82 against 3.69 ms at 646, 8.04 against 7.62 at 2,000); no new runs.
  3. `--density` and the `density` sceneOption changed the scene but not the navigation:
     **fixed.** `ChoreographyData.FromJson(..., density)` builds the placement list at the drawn
     density (DemoMain from the loaded arena, TimelineScene from its options); the default density
     comes from the layout. Song tests run after raising it (above).
  4. Fighting-ground dressing placed with force could sit through walls, tombs, braziers and the
     pyre, and overlap itself: **fixed.** It now tests landmark and structure pieces, dressing already
     placed and keep-outs (`OverlapsFixed`, `InKeepOut`), still density-invariant; test
     `DressingStaysOffWallsAndKeepOuts`.
  5. `ArenaStills.Frame` left its shadow distance in the tracked URP asset: **fixed** (saved and
     restored in `finally`).
  6. The static shadow map's test checked only the CPU matrix: **fixed.** A GPU test renders one
     cube into a 512² map and checks the ground in LitStaticShadow dark in its shadow and lit
     opposite and beside it (projection, y-flip and reversed Z end to end); it renders one frame before publishing
     the map, since a cold editor sets the pipeline up on its first camera and the map published
     before that read as no shade (players render the map after the scene's first frames); the texel test now
     reports and bounds the ground-projected texel: about 5.5 × 6.6 cm over the 160 m box (5.9 ×
     7.0 cm on the arena's real bounds, by TD9's light-space sizes), not "about 4 cm". Fitting the
     ortho box's rotation to the arena: **dismissed** for now (at most about 15 % on one axis, and
     the shade already reads at the game camera's 3 cm per pixel).
  7. `ArenaClock` stepped nested particle systems once per ancestor and restarted at `now % 60`:
     **fixed** (each system stepped alone, `withChildren: false`; seeks simulate the full song time).
  8. Minor: round 3's S0 E2 4× 40 m row's 0 draws (a bad counter frame), TD14's summarize.py
     merging rounds and probe/full runs (now kept apart, tables regenerated), TD9's "5 ms" (CPU
     submission), E1 on TD9's smaller arena: **noted** in TD14's and TD9's entries. The canopy
     fade's cost is **unmeasured** in S0 (no hero there) and S1–S6 (the bench doesn't publish the
     hero): a branch and a few view-space ops per tree pixel near the hero, not isolated by any row.
     The `reference/*-own.jpg` stills (the vendors' demo scenes under their own lights) are
     **removed** from git (`git rm --cached`); `ReferenceStills` now writes them to the gitignored
     `captures/td15/reference-own/`, and only the under-our-night stills stay in evidence.
- **Tests:** dotnet 119/119 (TD15's: the beauty shots on open ground and the hold; dressing never
  moves an earlier piece, at 1x and 2x; dressing stays off walls and keep-outs); EditMode 203/203 at the
  authored 2× (TD15's: the static shadow map on the GPU, the ground texel, the kit value, canopy materials
  following their sources and the atmosphere's draws in every baked scene, the ambient spawner's
  determinism and seek). The first layout put its dressing in the middle of the scatter, which
  moved forest and edge pieces and so the song's navigation: two song tests then failed under
  Unity's Mono (`FramingFollowsTheHeroAndEasesTheZoom` at 173 s, `NoTwoMonstersOverlapByMoreThanAQuarterRadius`
  at 197.5 s) though dotnet passed them, and passed again with TD9's layout. Hence `dressing`: it is
  placed after everything else, so the blocking set the choreography navigates is exactly TD9's.
  Four lamp posts at the opening, far from the song's route, still broke both tests in Unity (the
  flow field is global): they went, and eight non-blocking candles took their place.
- **Tool notes:** `ArenaStills` writes the view's shadow distance into the URP asset, which the
  warm editor then saves (reverted before committing, as TD9's run had to). Changing
  LitStaticShadow's or CrowdSkinned's source recompiles their variants on the next build (11 minutes
  for the first build after, beside other worktrees' compiles).
  Another worktree's sweep held the machine lock, then a shader compile kept the machine busy, so
  my S0 sweep waited for quiet before measuring.

### Skeleton LOD Mac experiment (standalone, 2026-10-28)

- **Status:** Done. Explicit operator handoff overrides the Cygnus-only host
  rule for this experiment. Fresh relay worktree; no Hive or Tollgate.
- **Scope:** two independent Skeleton Warrior LOD experiments and a synchronized
  Metal/Mono Mac preview. Production LODs/defaults stay unchanged.
- **Evidence, recipes, commands and limitations:** [skeleton-lod comparison](../../demo3d/evidence/skeleton-lod/README.md).
- **Tool incident:** first Mac `uv run --directory demo3d/tools pytest -q` returned
  `1 failed, 67 passed, 2 skipped`: the synthetic hook in
  `test_launcher_without_tools_refuses_demo_changes_only` was not executable, so
  POSIX Git ignored it and the expected refusal never occurred. The test fixture
  now chmods its hook like the real installer. Rerun: 68 passed, 2 skipped.
  No change to guard policy. Blender's normal zero exit on Python exceptions was
  caught during recipe development; the reproducible route explicitly uses
  `--python-exit-code 1` and checks subprocess status.
- **Operator pending:** human visual acceptance of A/B and the interactive preview;
  no pause before completing the experiment. Windows performance/parity are outside
  this Mac task.

- **Implementation checkpoint:** `0850eedc419e475653817337eb7e4eacebdef5bf`.
  A retains 2,143 triangles / 4,008 vertices; the 1,200-triangle attempt failed the
  whole-body preservation requirement. B is an independent 1,264-triangle / 3,792-vertex
  proxy with visibly blockier armour. Both reuse the original binding/60 Hz clips.
- **Validation:** 119 .NET tests, 68 Python tests (2 Windows-only skips), 197 cold Unity
  tests (7 unrelated production-bake skips), full tools lint, check-fast and vendor
  guard passed. Mac arm64 Mono/Metal non-development build and final offscreen player
  capture passed. The full renderer exercise covers all 15 clips and preceding
  crossfades; 384 intended stills plus a 47.867 s reel are kept locally. No Cygnus,
  Windows/D3D12 or Windows IL2CPP checks were run.
- **Cold review:** two confirmed UI issues fixed (paused clip selection and clipped
  single-view controls); the new regression also caught target-texture disposal
  ordering, now corrected. Review and limitations are in the linked comparison.
- **Run:** `open demo3d/Builds/SkeletonLodPreview.app` in the Mac task worktree.
  Scene: `demo3d/Assets/Baked/SkeletonLod/SkeletonLodPreview.unity`; exact regeneration,
  build, offscreen capture, reel and controls are in the linked evidence README.
- **Additional tool incident:** ffmpeg's optional `drawtext` filter is absent on this
  Mac (`No such filter: 'drawtext'`, once). The reproducible assembler uses Pillow
  for labels and ffmpeg's available raw-video/H.264 path; the full reel passed.

### Skeleton LOD preview UI follow-up (2026-10-28)

- **Status:** Done; fresh Mac worktree from relay `2815a785`, standalone relay
  delivery, no Hive or Tollgate.
- **Operator report:** changing text painted repeatedly over itself in full screen;
  detail inspection also needed a closer zoom.
- **Fix:** a dedicated empty presentation camera clears the screen, and IMGUI
  paints an opaque background before its labels. Previously every camera rendered
  only to its model texture. Zoom minimum is now 0.5 m (formerly 2.5 m); wheel,
  +/- and logarithmic slider controls, matched head/torso/feet focus presets and
  a focus-height slider support detail inspection. Production meshes/defaults are
  unchanged. The full-screen player was reopened at a close upper-body view;
  read-only live inspection confirmed legible changing labels without overprint.
- **Checks:** 3 focused regressions (including old-pixel erasure and registered
  head/feet/minimum zoom), 199 cold Unity passes (7 unrelated bake skips), 119 .NET
  passes, 68 Python passes (2 Windows-only skips), tools lint and the normal Mac
  Mono/Metal build passed. Twelve matched detail renders were checked locally.
  Check-fast and vendor guard run before delivery. Windows-only checks not run.
- **Evidence and commands:** [preview follow-up](../../demo3d/evidence/skeleton-lod/README.md#preview-ui-follow-up-2026-10-28).
  No cold review trigger: under 300 lines of non-test/non-documentation code,
  no sim determinism/vendor guard change and no demo milestone boundary.
- **Capture note:** a temporary batch-player backbuffer screenshot probe returned
  black rather than the live UI. It was rejected, with no false UI-pass claim;
  the unshipped route was removed. Tests render to explicit textures, and the
  already operator-requested full-screen app was inspected after launch.

### Skeleton LOD cost-matched comparison (2026-10-28)

- **Implementation commit:** `3b59ec4`.
- **Status:** Done; fresh standalone Mac worktree from relay `9f3abb795ac1a9679e4b8ca8ac0fca58483a8f78`.
  No Hive, Tollgate or Cygnus; production defaults unchanged.
- **Operator request:** replace the unequal A/B comparison with B at comparable
  rendering cost and recommend the LOD strategy.
- **Result:** B matched has 2,003 triangles / 4,211 vertices, versus A's
  2,143 / 4,008. Median paired synchronized render latency is 1.013 × A over
  36 paired blocks (Mac Metal, 1,024/4,096 instances, main and own shadows).
  Across 270 game-size poses, B improves silhouette overlap in 267 and RGB error
  in 263; average overlap is about 92% versus 88%, colour error about 20% lower.
- **Recommendation:** part-aware hybrid for aggressive crowd LODs. Retain cheap
  important authored shells, allocate reductions per semantic part and reconstruct
  selected small anatomy/details. Lighter source reduction serves nearer LODs;
  keep LOD0 where visible damage remains. This is skeleton-specific pipeline
  evidence, not a universal threshold or Windows performance claim.
- **Evidence and exact commands:** [cost-matched comparison](../../demo3d/evidence/skeleton-lod/cost-match.md).
  Raw runs, generated meshes and the updated preview remain local and ignored.
  App: `demo3d/Builds/SkeletonLodPreview.app` in `.worktrees/skeleton-lod-cost-match`;
  scene: `demo3d/Assets/Baked/SkeletonLod/SkeletonLodPreview.unity`.
- **Validation:** 199 cold Unity passes (7 unrelated bake skips), 119 .NET passes,
  68 Python passes (2 Windows-only skips), tools lint, normal Mac Mono/Metal build,
  all-clip/crossfade/detail captures and validated cost/quality runs passed.
  Check-fast and vendor guard passed before delivery. Windows-only checks not run.
- **Review:** self-review recorded in evidence; fresh read-only same-host warden
  review found no actionable issues. The previously read skill disappeared on disk;
  its cached instructions were supplied and applied. Human aesthetic acceptance
  and target-device performance thresholds remain pending.
- **Run:** `open -n demo3d/Builds/SkeletonLodPreview.app --args -screen-fullscreen 1 --view-width 5`.
  The third pane is **B matched**; the earlier full-screen clearing and zoom
  controls are preserved.

### The climax's boss walks in (ps-tfn.9, 2026-10-29)

- **Status:** Done (bead ps-tfn.9, from the 2D demo's PX8). Commit SHA on the bead.
- **Problem:** the boss entered beyond the view's north edge at the final peak (161.0 s) and stood
  there to the song's end, one motion segment and no actions: the sim moves an off-screen monster
  only when it releases it to fill the view's deficit, nearest the edge first, and a full climax
  never reached it.
- **Fix:** the final peak sets PX8's opt-in `bossFrom` (choreography.md) to `opening`, the
  clearing to the hero's north-east. The boss enters on that side and always walks in. Tried on
  the song's timeline: `courtyard-field` (north) stops it near the frame's top edge, about 12 m
  past the focus; `brazier-glade` (north-west) is never struck before the outro; from `courtyard`
  (west) it is stuck behind the courtyard's walls. With `opening` it is in view from 163.1 s to
  the section's end, about 7 m up and right of the hero, is struck at 178.5 s and falls to one of the
  ending's slams (195.9 s) with the horde. Its reach and attack are unchanged: a 1.2 m melee boss
  can't get through about 430 monsters to the hero, so it never attacks; demo2d made its boss a
  caster for that, which demo3d's client would need a projectile effect for.
- **Song dump** (`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- song
  --out song.json`): SHA-256 `f5170f7184724ca94eee3438a1d342c07cba43958559a9c8e5bb2679fa84208c`
  before (the same-type clump commits had already moved it off PX8's `09945e3b…`), and
  `5f2bf73bf6a943f810c4e997c3d8495723a2ffa774916840a5b3cd3f35998337` (19,118,387 bytes) after,
  identical over two runs. Only the final peak onward changes.
- **Checks:** `dotnet test demo3d/dotnet/Demo3D.Choreography.Tests` 132 of 132, with the new
  `TheBossWalksIntoViewAndIsStruck` (in view in the section's first third and to its end, struck
  before the ending; it fails without `bossFrom`); `demo3d test` 220 of 223, 3 skipped (the
  skeleton-LOD preview bake); IL2CPP build. Windowless stills by [mac.md](mac.md)'s command at
  [170 s](../../demo3d/evidence/boss-entry/still-170.000.jpg) and
  [185 s](../../demo3d/evidence/boss-entry/still-185.000.jpg): the boss (the green dragon) is in
  frame to the hero's upper right in both.
