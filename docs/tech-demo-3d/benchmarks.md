# 3D Tech Demo: Benchmarks and A/B Checks

## Benchmark Harness

The harness turns the question into numbers. It runs stress scenes through the
real client, sweeps the axes, and finds the largest on-screen count that
passes at each point.

### Stress scenes

Each scene isolates or combines costs, so the report can say what limits a
point. Until TD9 builds the arena, scenes run in the **bench scene**: flat
ground under the default sun, ambient and post-processing. From TD9 on they
run in the arena, and TD12's sweep uses the arena.

```
S0  empty arena          environment, lighting and post; density 0.5x to 4x
S1  crowd walking        N monsters on beat gaits, no actions or effects
S2  crowd in combat      S1 + hero combos, hits, deaths, refills; no effects
S3  full combat          S2 + effects and lights at a load preset
S4  effects only         S3's effect stream with the monsters hidden
S5  worst beats          S3 at heavy load, a worst beat every 4 bars
S6  walls and chokes     S3 at demo load around the arena's walled courtyard
song                     the demo's own timeline
```

S6 needs the arena's structures, so it runs from TD9 on. The hero fights
inside the walled courtyard, and the horde funnels in through its gaps. That
tests:

- navigation cost under congestion, from the sim's tick timing;
- local density spikes at the gaps, for drawing and overdraw;
- wall shadows across the horde;
- whether jams look acceptable, which the operator reviews from captures.

### Sim tick timing

The client's measurements never include the choreographer, which runs at
load. But the choreographer stands in for a game's sim, and it already ticks
at 60 Hz over the whole timeline. So the player times every tick of that
load-time run, and the reserve becomes a measured number.

- **What is timed.** Each tick's wall time on the main thread, in the player
  build. A tick simulates everything a real game would: the on-screen horde,
  the off-screen reserve, navigation, separation, hits and spawns. Stress
  timelines hold N steady, so their ticks measure the sim at that N.
- **Warm-up.** The first 5 s of ticks are excluded, as frames are.
- **The sim's own line.** The tick's p99 time must stay at most 3.5 ms on
  Cygnus (see [Performance Budget](performance.md#performance-budget)).
- **It is a conservative stand-in.** The choreographer is plain C# on one
  thread, without Burst or jobs. Passing means a real sim fits with room to
  spare. Failing means a real sim would need those optimizations, not that
  the design fails.
- **What it leaves out.** The ticks run before the frame loop, so contention
  with the client's frame (caches, the garbage collector) is not measured. A
  single-threaded tick loop that allocates nothing keeps that small.
  Determinism is covered by the byte-identical dump test, not here.

```json
{ "scene": "S6", "n": 2600, "offScreen": 1300,
  "simTickP99Ms": 1.42, "simTickMaxMs": 1.88, "flowRebuildSlices": 26 }
```

### The sweep

A sweep is a JSON spec of axes. For each combination of the other axes, the
harness searches for the largest N that passes.

```json
{ "scene": "S3", "seconds": 20, "warmup": 5, "probeSeconds": 8,
  "n": { "search": [100, 12000], "resolution": 0.05 },
  "viewWidthM": [30, 40, 55], "load": ["light", "demo", "heavy"],
  "aa": ["msaa4", "taa"], "jobWorkers": 6 }
```

The frontier is the resulting table of maximum N per view width, load and
anti-aliasing mode.

Sweeps hold Cygnus's screen and GPU and sit on the plan's longest path, so
the search spends as few runs as it can:

- **One process per scene.** The player loads a scene once and changes the
  other axes in place. For a new N it rebuilds the stress timeline (timing
  its ticks, as at load) and resizes the crowd, then re-warms for 3 s. The
  first point gets the full warm-up.
- **A fitted search.** Each resource's frame time grows close to linearly
  with N. The harness measures three short probes spread over the search
  range and fits a line per resource: GPU, main thread and render thread.
  The lowest line's crossing of its pass line is the candidate N, and that
  resource is the point's limit.
- **Confirmed at full length.** A full run at the candidate N must pass and
  one 5% above it must fail. If either disagrees with the fit, the harness
  bisects between them as before.
- **Seeded from neighbors.** Probes start around the nearest point already
  measured, since adjacent points' frontiers are close.
- **5% resolution.** The envelope rounds down to the nearest 100 anyway.

A point costs about four runs instead of a bisection's seven, and no run
pays for a launch or a scene load.

### Measurements

Each run records per-frame timings and counters. Their summary is one result
row per run.

- **Timings** from Unity's `FrameTimingManager`: main-thread CPU,
  render-thread CPU and GPU frame time.
- **Counters** from our own systems: visible instances by type and LOD,
  skinned vertices, shadow casters, live particles, effect spawns per frame
  (and the largest in one frame), spawns rejected, light cues, lights used,
  and the share of light dropped. Also the sim's tick times from the run's
  load-time choreography, and its flow-field rebuild slices (see
  [Sim tick timing](#sim-tick-timing)).
- **Overdraw** from a debug replacement render that counts opaque depth
  complexity and transparent fragments per pixel. It runs on every 60th
  frame and on the frame after each downbeat, which catches the beat-frame
  peak. Those frames are excluded from timing.
- **Flags.** The report flags a point whose mean transparent fragments per
  pixel exceed 5, or whose beat-frame peak exceeds 12 (see
  [How many effects](effects.md#how-many-effects)).
- **Machine state:** the graphics API, the GPU driver version, GPU
  temperature and NVIDIA's throttle reasons (from `nvidia-smi`), the Windows
  power plan, the memory speed, the job worker count and the backbuffer
  size.

```json
{ "scene": "S3", "n": 2400, "viewWidthM": 40, "load": "demo", "aa": "taa",
  "gpuP99Ms": 4.08, "mainP99Ms": 4.12, "renderP99Ms": 3.05, "maxFrameMs": 6.3,
  "onScreenAlive": 2231, "onScreenDying": 169, "particles": 26400,
  "spawnsMaxPerFrame": 171, "spawnsRejected": 0,
  "lightsUsed": 117, "lightDropShareP99": 0.004,
  "opaqueDepth": 1.21, "transparentPerPixel": 3.1, "transparentBeatPeak": 8.6,
  "api": "d3d12", "driver": "610.88", "gpuTempC": 64, "gpuThrottle": "none",
  "powerPlan": "high", "memMHz": 3200, "pass": true }
```

At the envelope point and the heavy-load point at its width (see
[Performance Budget](performance.md#performance-budget)), the harness also takes PIX timing
captures over a few seconds at the found count, and keeps their per-pass GPU
summary, not the raw captures. TD7 scripts the captures with PIX's
command-line tool, `pixtool`, or PIX's programmatic capture API, and records
the exact command in its notes.

### Running a sweep

Sweeps run the real Windows player in a window on Cygnus's desktop.

```
PlaylistSurvivors3D.exe -force-d3d12 -screen-width 1920 -screen-height 1080 \
  -screen-fullscreen 0 -job-worker-count 6 --bench sweep.json --out <evidence dir>
```

- **Exactly 1920×1080 pixels.** The camera must render into a 1920×1080-pixel
  backbuffer. Cygnus's only desktop display is a 3840×2160 monitor at 200%
  scaling, so a window of 1920×1080 pixels fills a quarter of it, and a
  window sized in scaled units would be twice that. TD7 finds the flags that
  give exactly 1080p. The command above is illustrative. The harness asserts
  the backbuffer size at startup and refuses to run otherwise.
- **The interactive desktop.** Agent sessions run in the Claude desktop app
  in the operator's logged-in session, so the player's window appears on the
  desktop. A locked desktop or a sleeping display stops composition, so the
  harness keeps the display awake during a sweep
  (`SetThreadExecutionState`) and refuses to start while the desktop is
  locked.
- **The worker cap.** Unity's `-job-worker-count` sets it, and the harness
  verifies `JobsUtility.JobWorkerCount` at startup.
- **No frame cap.** Vsync is off and the frame rate uncapped.

### Quiet machine

Cygnus is mostly idle, and Tollgate never runs there. The load that can
corrupt a measurement is other agent sessions on Cygnus (Unity imports,
IL2CPP builds), plus Windows' own background work: updates, Defender scans
and indexing. A sweep quiets what it can, waits a bounded time for the rest,
and reports what it couldn't quiet.

- **The lock.** A sweep holds a machine-wide lock: a file in the per-machine
  cache, opened for exclusive access for the sweep's whole life. Windows
  releases it if the process dies, so a killed sweep leaves nothing to clean
  up. A sweep waits for the lock, not around it, so two sweeps never overlap.
- **Other sessions can't be paused.** The sweep waits up to 10 minutes for
  other processes to use less than 10% of total CPU over 5 s, then runs
  anyway.
- **Hard refusals.** A run never starts unless the power plan is High
  performance (or Ultimate Performance), the desktop is unlocked, and the
  GPU is idle-cool: under 50 °C with no thermal throttle reason.
- **During a run,** it samples other processes' CPU and the GPU's throttle
  reasons every second. Any thermal slowdown, or other processes over the
  threshold, marks the run noisy. A power-cap reason under full load is
  normal for a desktop card and is recorded, not counted as noise.
- **The window too.** A run during which the player's window loses the
  foreground or is occluded (its swap chain reports occlusion) is marked
  noisy.
- **A noisy run** is repeated up to twice and then reported as noisy, never
  silently kept. The report counts the frontier points that rest on noisy
  runs.

### Scheduling sweeps

The heavy sweeps take hours of window time: TD5's baseline comparison,
TD14's density sweep and TD12's full sweep.

- **They run when their task reaches them.** Cygnus is mostly idle, so no
  task waits for a quiet window or for the operator.
- **At most two agent sessions on Cygnus at once.** A third would mostly add
  noisy runs and contention for the GPU and the disk.

### The report

The report is generated from result rows and written to the evidence of the
task that ran the sweep, with a short summary in that task's notes (see
[Task Notes](plan.md#task-notes)). It
gives:

- the frontier tables, with each point's limiting resource (GPU, main thread
  or render thread), its compute estimate, and the per-pass estimate where
  it was traced;
- the sim's tick timing at every frontier point and in S6;
- the baseline comparison: stock Unity's maximum against the chosen path's;
- the envelope, defined below, with every factor and assumption it rests on;
- the environment's density curve, what richness costs in monsters, and its
  final rendering against the stock baseline, rung E0;
- every accepted cut, with its A/B result.

The **envelope** is the recommended pair of a view width and an on-screen
count for game design. It is picked from the frontier at the demo load with
the chosen anti-aliasing, by one rule: **the widest view the hardware can
fill at fighting density.**

- **Fill count.** A view width's fill count is its visible ground at one
  monster per m² (690, 1,220 and 2,300 for 30, 40 and 55 m).
- **Envelope view width.** The widest measured width whose frontier count is
  at least its fill count. If no width qualifies, it is 30 m.
- **Envelope count.** The frontier count at that width, rounded down to the
  nearest 100.

```
frontier (demo load):  30 m → 1,600 (capacity-bound)  40 m → 2,600  55 m → 1,700
fill counts:           30 m →   690   40 m → 1,220   55 m → 2,300
envelope:              40 m, 2,600 on screen   (55 m cannot fill its screen)
```

The envelope's count is the headline count. The level script's per-section
counts are fractions of it, and no section's view is wider than its width.

## Fidelity A/B Checks

A cut is accepted by evidence, not by eye at the moment. Each cut gets a
deterministic capture pair and an image-difference score.

- **Capture.** Fixed-time mode renders 12 frames spread over 20 s at
  1920×1080, once without the cut (A) and once with it (B). The scene is S3
  at demo load and a 40 m view, with N at 2,000 or the current 40 m
  frontier, whichever is lower.
- **Score.** NVIDIA FLIP per frame pair: the mean error, and the 99th
  percentile per pixel, which catches small localized artifacts.
- **Thresholds.** TD11 calibrates them against two anchors that work on any
  crowd path:
  - **Invisible by construction:** song time offset by 1 ms.
  - **Plainly visible:** every monster's pose time rounded down to 1/15 s, so
    the crowd animates at 15 Hz.
  - A cut passes when both its scores sit at or below the invisible anchor's
    plus a third of the gap to the visible anchor's.
- **The tools.** TD4 builds the capture tool, which renders fixed-time stills
  and video pairs; TD8 and TD9 use it before any cut exists. TD11 adds the
  FLIP scoring and calibrates the thresholds. Their stills and videos carry
  the `ps-render` tag (see [The vendor guard](vendor-assets.md#the-vendor-guard)).
- **Motion.** Some cuts show only in motion, such as LOD popping. Each A/B also
  keeps a 2 s fixed-time video pair.
- **Review.** The operator reviews the stills and video pairs later, through
  [Pending on the operator](plan.md#pending-on-the-operator). No task waits for that review.

```
cut: lod1-below-60px   scene S1, n 3000, 40 m
  mean FLIP 0.0041 (pass ≤ 0.0060)   p99 FLIP 0.071 (pass ≤ 0.090)   PASS
```
