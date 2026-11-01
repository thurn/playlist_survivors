# The run's performance on the Mac (ps-msz.8.2, ps-msz.8.9)

The game's first frame numbers: a windowless frame probe and a per-frame CPU profile in the player,
and one command over the run's standard scenarios. Taken on jarvis (Apple M5 Max, Metal), the IL2CPP
player built from this bead's tree, 2026-10-31. Mac numbers only guide M4's work; Cygnus measures
against the targets in M5 (docs/design/unity-game.md, Performance targets).

```sh
uv run --project demo3d/tools game perf --evidence perf   # builds if needed; about 5 minutes
uv run --project demo3d/tools game perf --only peak,break # a subset
```

It writes [summary.json](summary.json) and each scenario's probe (`gpu-probe-<scenario>.json`) here;
the per-frame profiles (`profile.csv`, 16 MB for the full run), their allocation census
(`profile-alloc.csv`) and the players' logs stay in `captures/game/perf/<scenario>/`. It exits 1 if a
player fails. summary.json is ps-msz.8.9's take; ps-msz.8.2's baseline is the table below.

## What it measures

- **The frame probe** (`--gpu-probe-at T1,T2,... --gpu-probe-out FILE.json`, `Client/GpuProbe.cs`,
  demo3d's BatchGpuProbe ported): a batch-mode player draws no frames, so at each song time it
  hand-draws the game camera for nine consecutive frames into a 1920x1080 target with the game's MSAA
  4x, and times each draw through GPU completion (a one-pixel readback waited on). The first frame is
  a warm-up and isn't counted. A probe frame is the frame's CPU submission and GPU work in series: an
  upper bound on a windowed frame, which pipelines them. Not in it: the HUD's UI panel (drawn into its
  own target). Metal has no GPU timer in Unity, so the GPU's share can't be split out.
- **The CPU profile** (`--profile-out FILE.csv`, `Client/Loop/FrameProfile.cs`): every frame's main
  thread from just after Unity's time update to the end of its player loop, split into the sim's
  step, the events' dispatch to every presenter, each presenter's Present by what it draws (`crowd`
  without the GPU `shatter`, `structures` without their PhysX `physics` steps, `effects`, `weapons`,
  `pickups`, `hero`, `look` (camera and post), `hud`, `sound`), the rest of GameLoop's update (`loop`)
  and everything else (`engine`: Unity's player loop, animation, the UI panels, other scripts). Each
  row also has the frame's managed allocation and the collections since the previous frame.
- **Left out of the frame**: `wait_ms`, a fixed-time frame rendering its music and waiting on the
  plugin's analysis (live, the audio thread renders and a frame never waits), and `probe_ms`, the
  probe's draw and its file, with its allocation. So the full run, which takes 4 minutes at fixed
  time, reports the frames' own work (it waited 84 s in all).
- **Allocations**: IL2CPP doesn't implement `GC.GetAllocatedBytesForCurrentThread`, so the player
  counts the main thread's bytes from IL2CPP's allocation profiler hook, in native code
  (`Client/Loop/AllocationCounter.c`, compiled into GameAssembly). A managed hook crashed the player
  (exit -4) whenever a job thread attached while it was on: attaching allocates, and the hook's
  reverse P/Invoke attaches first, so it re-entered itself until the stack ran out.
- **In batch mode the main thread doesn't render**: `frame_ms` is the frame's update work. A
  windowed frame adds its render submission, which the probe's frame contains.
- **The frame's CPU time** (`cpu_ms`, ps-msz.8.9): the main thread's own CPU time in the frame
  (`CLOCK_THREAD_CPUTIME_ID`, the wait and probe left out as above; -1 on Windows, whose thread times
  move in 16 ms ticks). A frame whose `frame_ms` is far over its `cpu_ms` waited on the OS, not on its
  work: the summary lists every frame over 16.7 ms with both (`slow`), and p99 and p99.9 of each.
- **The allocation census** (`profile-alloc.csv`, ps-msz.8.9): the main thread's allocations from the
  first profiled frame to the run's end by type and by the part of the frame that made them (`engine`,
  `loop`, `sim`, `probe`, or a presenter's `Present`/`OnEvent`), with each type's count, bytes and
  largest, from the same native hook (the type's name from IL2CPP).
- **First sightings** (`--gpu-probe-firsts`, ps-msz.8.9): the probe also draws three frames at the run's
  first frame and at the first sighting of each kind of moment, by its events: each weapon's first
  strike, hit, projectile, orbit, bolt and burn, a heavy strike, each role's first spawn, death and
  leave, each enemy attack, each zone's first change, each set piece, the first break, regrowth, glint,
  cleave, gem, chest phase, level-up and card, and the finale. Its first frame (`first_ms`) is the one
  that first draws the moment: in batch mode nothing else draws, so a shader variant's compile or a
  pool's fill lands there.

## The scenarios

All on cc-kevin-macleod-go-cart with the positioning bot, at fixed time, seed 1 (full flags in
summary.json):

| scenario | flags | probe at (s) | profile window (s) |
|---|---|---|---|
| peak | `--start-minute 25 --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5` | 40, 50, 60 | 30 to 62 |
| break | the peak's, plus `break@64` | 64.05, 64.4, 65 | 64 to 66 |
| finale | the peak's, plus `finale@68` (the blast at 68.30 s: 296 alive to 4) | 68.4, 68.7, 69.2 | 68 to 71 |
| early | `--run` (the run's first minute) | 45, 60, 75 | 20 to 80 |
| full | `--file native/playlists/survivors-30-twice.m3u --run`, to the victory card | none | the whole run |
| firsts | the full run's, with `--gpu-probe-firsts` | each first sighting | the whole run |
| firsts-rest | the peak's with `--arsenal sword:8,unholy-vespers:1,thunder-loop:1,chain-lightning:8,fire-trail:8 --debug stampede@8,kick@16,wedge@24`, `--gpu-probe-firsts`: what the full run never shows | each first sighting | 0 to 45 |

## The baseline (ps-msz.8.2)

ps-msz.8.2's take (load average 22 to 37: other agents' builds shared the machine). Times in
ms: probe frame p50/p99/max over its 24 counted frames; main thread p50/p99/max over the window.

| scenario | probe frame | main thread | over 16.7 / 33 ms | alloc B/frame mean, p99, max | GCs | sim step p99 at 285+ alive (frames) | alive median, max |
|---|---|---|---|---|---|---|---|
| peak | 6.96 / 7.71 / 7.71 | 0.84 / 1.59 / 2.19 | 0 / 0 | 1281, 3982, 316588 | 1 | 0.31 (704) | 268, 300 |
| break | 5.05 / 9.63 / 9.63 | 0.93 / 1.66 / 1.70 | 0 / 0 | 647, 2884, 3376 | 0 | 0.24 (121) | 300, 300 |
| finale | 4.26 / 8.31 / 8.31 | 0.67 / 1.65 / 1.74 | 0 / 0 | 2075, 4590, 227710 | 1 | 0.22 (18) | 4, 297 |
| early | 4.16 / 7.35 / 7.35 | 0.43 / 1.12 / 2.08 | 0 / 0 | 1115, 2664, 524808 | 2 | (never 285 alive) | 23, 48 |
| full (127,795 frames) | | 0.88 / 2.66 / 22.17 | 4 / 0 | 1125, 3150, 16777746 | 38 | 0.41 (51,277) | 214, 300 |

The peak's main thread by part (p50 / p99 / max, ms):

| part | p50 | p99 | max |
|---|---|---|---|
| engine (Unity's loop outside GameLoop) | 0.415 | 0.760 | 1.349 |
| sim | 0.015 | 0.298 | 0.481 |
| physics (structure shards) | 0.000 | 0.319 | 1.386 |
| crowd | 0.099 | 0.223 | 0.352 |
| effects | 0.043 | 0.110 | 0.226 |
| hud | 0.040 | 0.087 | 0.207 |
| hero | 0.036 | 0.062 | 0.093 |
| shatter | 0.020 | 0.061 | 0.144 |
| events | 0.000 | 0.051 | 0.289 |
| weapons, structures, look, pickups, loop, sound | under 0.035 each | | |

Two earlier takes of the same build (before the probe's own allocation was left out, which changes
only the allocation columns) at load 20 and 14: the peak's probe p50/p99 5.57/12.09 and 5.19/7.18 ms,
its main thread 1.25/2.70 and 0.67/1.51 ms; the full run's main thread p99 2.90 and 1.98 ms with
3 and 1 frames over 33 ms. The machine's load moves the upper percentiles by up to 2x; the
allocations were the same to the byte in every take.

## The top costs at the peak

1. **Drawing the frame**: the probe's 7.0 ms median (7.7 ms p99) of CPU submission and GPU in series,
   about eight times the main thread's update. Everything that draws (the arena and its shadows, the
   crowd's 300 instanced monsters, the effects, post and bloom at 1080p with MSAA 4x) is in it.
2. **Unity's own player loop** (`engine`): 0.42 ms median, half the update, with the hero's animator,
   the UI panels' layout and the transforms in it.
3. **The crowd's Present**: 0.10 ms median, 0.22 ms p99 (posing 300 monsters into their instances).
   The structures' PhysX steps are the bursts beside it: nothing most frames, 0.32 ms p99 and up to
   1.4 ms while shards fly; at a break, 0.20 ms on average over its two seconds.

## Against the targets

- **60 fps on the Mac**: met with room. At the peak the update's p99 (1.6 ms) plus the probe frame's
  p99 (7.7 ms) is 9.3 ms, an upper bound, against 16.7 ms.
- **The sim's step at 300 alive, 1 ms p99**: 0.31 ms at the peak, 0.41 ms over the full run's 51,277
  frames with 285 or more alive.
- **No frame over 33 ms in the 30-minute run** (Cygnus's target): none in this take; 4 over 16.7 ms,
  the run's first frame (startup) and three of PhysX shards at song 1980 s. The earlier takes' slow
  frames (12 over 16.7 ms in the loaded one) fell at different song times each take, and none
  repeated: the machine's load, not the run's work. The windowed frame budget is ps-msz.8.9's.
- **Cygnus's 4.2 ms GPU p99 and 10.4 ms main and render p99** are for context only: the probe's frame
  is CPU and GPU in series on an M5 Max, not a GPU time, and Cygnus's RTX 4060 is a different GPU.

## For the performance beads (ps-msz.8.8, ps-msz.8.9)

- **Allocation every frame**: at least 458 B in every frame, from the start, and 0.6 to 2.1 KB on
  average across the peak's scenarios. A run's managed garbage comes to 38 collections over 30 minutes, none slower than a
  4.2 ms frame.
- **Periodic bursts**: 130 to 660 KB in single frames every few seconds at the peak (14.0, 16.6, 18.2,
  35.2, 37.0 and 56.6 s of song in the peak's take).
- **Power-of-two allocations over the run**: 4.2, 8.4 and 16.8 MB in single frames at 478, 958 and
  1973 s, 10.3 MB at 1998 s: lists that double as the run goes on. By their sizes and times most
  likely the sim's belief log (`BeliefLog`), which keeps every entry (128 B each, 133,000 over the run).
- **The first frame** takes 22 to 43 ms (and 1.5 MB of allocation): startup, before the music.

# The run's peak at 60 fps on the Mac (ps-msz.8.8)

The peak measured again on the polished look (ps-msz.8.3), its frame split into CPU and GPU, each pass's
cost measured, and the one cut that saved GPU time without changing a pixel's worth of the look. Jarvis
(M5 Max, Metal), the IL2CPP player, 2026-10-31. Other sessions' Unity builds kept the load average at 20
to 70 through every take, and their players shared the GPU: read every upper percentile as a loaded
machine's, and compare only takes run side by side.

```sh
uv run --project demo3d/tools game perf --only peak,break,finale --evidence perf/peak-m4
uv run --project demo3d/tools game perf --only peak,finale --ablate --evidence perf/peak-m4/ablate
```

## What the probe now measures

- **The CPU's share**: each probe frame also times the camera's Render call before its wait on the GPU
  (`cpuP50Ms`/`cpuP99Ms`, each frame's fourth number). At the peak it is 2.3 ms of a 6 ms frame.
- **Each pass's cost** (`--gpu-probe-ablate`, `game perf --ablate`): at each probe time, 25 rounds of the
  full frame and the frame with one pass off (SSAO, the whole post stack, bloom, the main light's shadows,
  the opaque texture the crystal refracts, MSAA, the crowd and its shards, the effects' particles, the
  effects' lights), one after another, so a pass is timed beside full frames under the same load. The
  summary's `pairedSavedMs` is the median of each frame without the pass against the full frame drawn
  just before it: a loaded machine's bursts move it least. `game perf --ablate` keeps only points 4.5 s
  apart (an ablated point takes 4.2 s of song).
- **One pass off in every frame** (`--gpu-probe-without PASS,...`): for a moment too short for the cycle,
  compared with a run that keeps the pass, since a fixed-time run reaches the same frames each time.
- **The GPU's own time**: Instruments' Metal System Trace on a probe run
  (`xcrun xctrace record --template 'Metal System Trace' --launch -- <player> <the scenario's flags>`,
  then `xcrun xctrace export` of `metal-gpu-intervals`): the player's encoders, their union per probe
  frame. The probe's frames are told apart by their last blit's encoder index, which differs by pass set
  (27 full, 23 without SSAO, 10 without post, 11 without bloom, 26 without shadows, 25 without the
  opaque texture, 28 without MSAA).

## Where the frame goes at the peak

- **The GPU is not the bottleneck.** A full probe frame keeps the GPU busy 0.8 to 6 ms (median 3.5 to
  4.7 ms in the traced takes, lowest when no other process held the GPU; eight frames at 40 s traced
  alone, 2.1 to 3.6 ms). The rest of the probe's 6 ms is the CPU's 2.3 ms of submission and the GPU
  waiting on it between passes: the probe draws CPU then GPU in series, a windowed frame overlaps them.
- **No pass dominates.** Each pass's saving at the peak, from [ablate/summary.json](peak-m4/ablate/summary.json)
  (paired median, ms; the earlier takes agree within about 0.5 ms):

| pass off | peak | finale (68.4 s, after the blast) | of which CPU (peak) |
|---|---|---|---|
| the crowd and its shards (300 crystal monsters) | 1.51 | 1.70 | 0.53 |
| the main light's shadows | 0.68 | 0.66 | 0.21 |
| SSAO (half resolution, after this bead's cut) | 0.45 | 0.35 | 0.15 |
| MSAA 4x | 0.45 | 0.0 | 0.0 |
| the post stack (bloom 0.28 of it) | 0.33 | 0.61 | 0.19 |
| the effects' lights | 0.18 | 0.24 | 0.0 |
| the effects' particles | 0.13 | (noise) | 0.05 |
| the crystal's opaque texture | (noise) | (noise) | 0.11 |

- **The top three costs at the peak**: (1) the frame's CPU submission, 2.3 ms p50 (URP's culling and
  render graph, the crowd's 0.5 ms of indirect draws by type, LOD and depth bin among it); (2) the crowd's
  draws on the GPU, about 1 ms more (refracting crystal over a full crowd); (3) the main light's
  shadows, 0.7 ms.
- **The main thread**: at the peak 1.42 ms p50 and 2.90 ms p99 ([summary.json](peak-m4/summary.json)),
  Unity's own loop the largest part (1.38 ms p99), then the structures' physics (0.52), the crowd (0.49)
  and the sim (0.39). Main plus render at the peak, update p99 plus the probe's CPU p99, is at most
  2.90 + 5.66 = 8.6 ms against Cygnus's 10.4 ms. Every part measured 1.3 to 1.5x the baseline above,
  uniformly: the machine's load, not new work.
- **The sim's step at 300 alive**: 0.39 ms p99 over 1,872 frames at 285 or more alive (break 0.36,
  finale 0.30), against 1 ms.

## The cut: SSAO at half resolution

URP's SSAO ran at full 1080p: its AO pass and three bilateral blur passes over every pixel, behind a
depth-normals prepass. `Downsample` on (`Settings/URP-Game_Renderer.asset`) runs the AO and its blurs at
half resolution, a quarter of the pixels; the prepass is unchanged. In the traced takes SSAO's GPU cost
was 0.44 and 0.72 ms at full resolution and within the noise (0.04 to 0.67 ms) at half.

Before and after, the same build otherwise, run side by side three times each (72 probe frames a
scenario; load 20 to 35):

| scenario | before: probe p50 / p90 / p99 / max | after | frames over 16.7 ms, before / after | CPU share p50, before / after |
|---|---|---|---|---|
| peak | 6.22 / 8.91 / 14.93 / 17.84 | 6.41 / 8.01 / 9.90 / 12.56 | 1 / 0 | 2.30 / 2.23 |
| break | 5.34 / 8.10 / 9.51 / 11.27 | 5.40 / 7.07 / 8.78 / 10.62 | 0 / 0 | 2.38 / 2.41 |
| finale | 5.92 / 7.50 / 16.96 / 20.51 | 6.25 / 8.78 / 13.10 / 13.76 | 2 / 0 | 2.52 / 2.52 |

The medians are the same within the load's noise (the GPU is mostly waiting); the tails are shorter.
The look doesn't change: the eight peak stills (the five zones, the forced accent's heavy strike, the
break and the finale's blast) read the same dL* to the tenth (+42.2 to +66.3, all passing), and the
pixels differ by 0.07 to 0.19 of 255 on average, 2 to 3 at the 99th percentile (JPEG noise). Three of
them before and after: [before-after.jpg](peak-m4/before-after.jpg) ([before/](peak-m4/before/),
[after/](peak-m4/after/), each with its readability.json).

Tried and not kept: SSAO off entirely in the asset (`m_Active: 0`) blacks out the camera, since the
build then strips SSAO's shaders while the feature is still listed and URP fails to set up its renderer.
SSAO is the look's, and half resolution keeps it.

## Against the targets

- **60 fps at the peak on the Mac: met.** The after take ([summary.json](peak-m4/summary.json)): probe
  p99 9.03 ms at the peak and 6.79 ms at the break, with the main thread's p99 2.90 and 3.50 ms. A probe
  frame is CPU and GPU in series, an upper bound on a windowed frame.
- **The finale's first frames are the run's heaviest**: in every take the frames just after the blast
  (68.30 s, about 300 monsters shattering at once) are the slowest, 8 to 20 ms against 6 ms, decaying over
  six frames, with the CPU's share near 4.5 ms against 2.3. The evidence take's p99 there is 20.11 ms, one
  frame of 24 (the side-by-side takes above: 13.10 ms over 72). Turning single passes off in those frames
  (`--gpu-probe-without`) left the spike within the load's noise (8.9 to 18.4 ms with no pass off), so
  the cost is spread over the CPU's submission of the shards, the crowd and the effects. It is
  ps-msz.9.2's to measure on Cygnus, windowed, against the targets.
- **A one-off 144 ms probe frame** at 60.03 s in the first take of the peak never repeated in the
  fourteen takes after it: a GPU shared with other sessions' players.

# No frame over 33 ms on the Mac (ps-msz.8.9)

The doc's target is no frame over 33 ms in a 30-minute run on Cygnus. On the Mac this bead removed the
steady allocations, sized the sim's storage for a run, moved the arena's load out of the run's frames,
and warmed up every graphics state and compute kernel the run uses at boot. The take in summary.json
(this bead's tree, before ps-msz.8.8's half-resolution SSAO landed beside it, which only shortens the GPU's share),
on jarvis at a load average of 9 to 17 (times in ms; main thread p50 / p99 / p99.9 / max, the
analysis wait left out; CPU max the main thread's own CPU time in its longest frame):

| scenario | probe frame p50 / p99 / max | main thread | CPU max | over 16.7 / 33 ms | alloc B/frame mean, p99, max (frames allocating) | GCs | sim step p99 at 285+ |
|---|---|---|---|---|---|---|---|
| peak | 5.09 / 18.70 / 18.70 | 1.21 / 2.42 / 6.97 / 24.14 | 3.65 | 2 / 0 | 36, 480, 21688 (208 of 1921) | 0 | 0.35 |
| break | 5.25 / 6.90 / 6.90 | 1.69 / 2.95 / 5.04 / 5.04 | 2.85 | 0 / 0 | 184, 2698, 17906 (15 of 121) | 0 | 0.39 |
| finale | 5.26 / 8.35 / 8.35 | 1.46 / 3.93 / 6.61 / 6.61 | 3.46 | 0 / 0 | 813, 6592, 74916 (7 of 181) | 1 | 0.25 |
| early | 5.30 / 7.03 / 7.03 | 0.72 / 2.20 / 4.96 / 9.92 | 3.07 | 0 / 0 | 72, 1568, 30560 (293 of 3601) | 0 | |
| full (124,827 frames) | | 0.94 / 2.70 / 4.21 / 11.98 | 7.99 | 0 / 0 | 18, 76, 131104 (8019) | 2 | 0.47 |
| firsts | 6.29 / 14.20 / 28.50 | 0.78 / 1.82 / 2.27 / 6.28 | 6.24 | 0 / 0 | 19, 76, 131104 | 7 | 0.36 |
| firsts-rest | 4.71 / 7.01 / 7.63 | 0.69 / 1.29 / 1.68 / 3.26 | 3.26 | 0 / 0 | 253, 3136, 176134 (515 of 2699) | 2 | 0.27 |

Against ps-msz.8.2's baseline above: the full run's longest frame 22.17 to 11.98 ms (4 frames over
16.7 ms to none), its allocation 1125 to 18 B a frame and its collections 38 to 2; the peak's
allocation 1281 B a frame (at least 458 B in every frame) to 36 B, its p99 3982 B to 480 B and its
median to 0, with no collection. The peak's two frames over 16.7 ms (at 56.70 s) had 1.4 and 1.9 ms
of CPU.

**Wall time and CPU time.** jarvis shares its cores with other agents' builds, so a frame's wall
clock can stretch while the OS runs something else, and a frame can also block (on a file, a lock or
the GPU's compiler) without spending CPU. `cpu_ms` shows both: a frame far over its CPU time waited.
A wait that repeats at the same moment in every take is the run's (the compute kernels' compiles below
were); one at a different moment each take is the machine's. After the fixes no frame's own CPU work
went over 8 ms in any take, and no frame over 33 ms repeated.

## Allocations

The census of a full run before the fixes named the garbage (bytes over the 30 minutes):

| source | bytes | fix |
|---|---|---|
| the sim's stream lists doubling: hindsight grid beats (69,273 a run), ticks (41,660), their known-at times, the accepted entries | 53 MB, 45 doublings, the largest 15.7 MB | sized for 40 minutes of song in the run's level (`SimConfig.HindsightCapacity`, `SparseCapacity`, `TickCapacity`, `BeatCapacity`, `PressCapacity` in `RunLevel.Config`): about 35 MB at the start |
| the HUD's texts every frame: level, kills, clock (and the clock's boxed numbers) | 26.8 MB | made once each and set only on a change (`HudText.Level`, `Clock`, `Name`, `Mark`) |
| the damage numbers: a capturing lambda every frame, a string per merged hit | 19 MB | a loop; each shown amount's text made once, only for the numbers drawn |
| the effects' counter readback: a new delegate every frame | 16 MB | made once |
| the bot's gem searches (lambdas) | 4.7 MB | loops |
| the wand's bolts and their legs | 5.9 MB | pooled |
| the zone's palette parsed from the layout's JSON on its first change | 1.4 MB | every zone's palette parsed at load |
| the fog's camera callback re-subscribed through a zone's ease | 1.7 MB | made once |
| a chest's shard batches at its first roulette | 1.6 MB | every colour's at load |
| the level-up cards and the arsenal's slots rebuilt as new UI trees | 2.5 MB | built once, filled in |
| a break's shard sets and materials (`Array.Find`, `sharedMaterials`) | 0.2 MB | a loop, a reused list |

What allocates still: the HUD's kill counter (a string per change, 270 KB a run), the run's summary
at its end (2 MB of percentile copies, after play), the structures' shard pool filling at its first
breaks, and the effects' cue list growing once at the finale's blast (53 KB).

## First-use hitches

Each first sighting probed (102 points in the full run, 32 in firsts-rest) on a build's first launch:
the game's Metal shader cache (`$(getconf DARWIN_USER_CACHE_DIR)/ca.thurn.playlist-survivors.game/com.apple.metal`)
removed before each take ([firsts-cold.json](firsts-cold.json)). macOS compiles a pipeline the first
time it's used and keeps it, so these stalls come on the first launch of each build (changed shaders
compile again) and not after: the warm takes above have none.

| on a cold launch | without a warm-up | graphics states only | both (shipped) |
|---|---|---|---|
| the run's first drawn frame (probe) | 3515 ms | 25 | 24 |
| zone 1's first change (probe) | 666 | 3.9 | 2.6 |
| the first spawn, bat3 (probe) | 563 | 8.4 | 2.7 |
| the first sword hit, death and gem (probe) | 287 | 6.6 | 3.1 |
| the first ghost's death / bat4's death / zone 2 (probe) | 275 / 265 / 243 | 6.5 / 7.0 / 8.3 | 4.7 / 3.2 / 2.6 |
| the worst of the full run's 102 first sightings (probe) | 3515 | 26.9 | 26.3 |
| the worst of firsts-rest's 32 (probe) | 2265 | 25.6 | 25.6 |
| the longest main-thread frame, full run | 171 | 113 | 4.1 |
| the longest main-thread frame, firsts-rest | 169 | 168 | 3.0 |

Two warm-ups at boot, both in `Client/GraphicsStates.cs`:

- **Graphics states.** A traced `GraphicsStateCollection`: `game perf --trace` builds a development
  player (Unity traces only in one) and runs the scenarios with `--graphics-states-out` into
  `game/Assets/StreamingAssets/GraphicsStates/Metal.graphicsstate` (gitignored, made per machine),
  which the next `game build` ships. The boot warms its 76 variants in 87 states before the title or the
  run: 3.3 to 4.5 s on a cold launch, near nothing warm. firsts-rest's weapons and set pieces added no
  state to the full run's trace.
- **Compute kernels.** A trace holds no compute, and the first dispatch of each of the 17 kernels (the
  effects' simulation and sort, the crowd's cull and palette, the shatter's) stalled its frame on the
  compiler with almost no CPU of its own, at the same moments in every cold take: the first frame
  (effects, 111 to 164 ms), the first spawn (crowd, 84 to 130), the first emitter burst (effects, 89 to
  151), the first death (shatter, 54 to 76). Each system now hands its kernels to
  `GraphicsStates.Prepare` as it loads, and `ComputeShader.IsSupported` builds them: 371 to 421 ms at
  load on a cold launch.

`--no-warmup` measures without both. One cold take with both warm-ups at a load average near 15 had
first sightings up to 83 ms (a boss's spawn) and a 64 ms main-thread frame with 1.8 ms of CPU, at
moments no other take repeated; the next, at 8, had none over 26.3 and 4.1 ms.

**The process's first drawn frame.** Each timed scenario's first probe point is the first frame that
player draws at all, with its uploads (24 to 37 ms, the finale's drawn on the blast with 296
shatters); in a window that cost is the load frame's, before the run.

## The arena's load

The arena's scene finishes loading in the frame after the loop starts, and its setup (the 4096² static
shadow map took 23 ms of it) ran in the same frame as the sim's first step: 36 to 48 ms of CPU in
the run's first frame. The loop now steps the sim and draws its presenters from the frame after the
arena's load (that frame still takes the music's entries and the keys), so no frame of the run
carries it; the sim's frames are its own, so the run is the same.

## Spikes

None left over 33 ms on the Mac: a structure break's frames 5.0 ms at most and the finale's blast
frame 6.6 ms; in the full run, with its 134 breaks, 126 regrowths, 77 zone changes and 131 set
pieces, no frame over 12.0 ms wall or 8.0 ms of CPU; and the probe's frames at the break and the
finale 6.9 and 8.4 ms p99.

## For Cygnus (M5)

The graphics-state file is per graphics API: Cygnus needs its own trace (Direct3D 12 or Vulkan), from
a development player there (`game perf` is the Mac's today); the compute kernels' preparation is
the same code there. `cpu_ms` reads -1 on Windows. Mac numbers only guide this; the target is Cygnus's.

# The Warden's re-run (ps-msz.8.10)

`game perf` (all seven scenarios) re-run on jarvis, 2026-10-31, at a load average of 18 to 26, from a fresh
worktree after `game perf --trace` and `game build`. Times in ms as in ps-msz.8.9's table above:

| scenario | probe frame p50 / p99 / max | main thread p50 / p99 / p99.9 / max | CPU max | over 16.7 / 33 ms | alloc B/frame mean, p99, max | GCs | sim step p99 at 285+ |
|---|---|---|---|---|---|---|---|
| peak | 6.23 / 9.52 / 9.52 | 0.77 / 1.38 / 1.94 / 2.83 | 2.77 | 0 / 0 | 36, 480, 21688 | 1 | 0.23 |
| break | 6.43 / 10.84 / 10.84 | 1.13 / 1.83 / 1.85 / 1.85 | 1.77 | 0 / 0 | 184, 2698, 17906 | 1 | 0.25 |
| finale | 5.01 / 14.70 / 14.70 | 0.92 / 1.75 / 2.65 / 2.65 | 2.58 | 0 / 0 | 813, 6592, 74916 | 1 | 0.20 |
| early | 3.51 / 5.82 / 5.82 | 0.45 / 1.10 / 1.58 / 1.89 | 1.84 | 0 / 0 | 72, 1568, 30560 | 0 | |
| full | | 0.67 / 1.37 / 1.75 / 5.30 | 4.02 | 0 / 0 | 18, 76, 131104 | 2 | 0.24 |
| firsts | 6.19 / 11.56 / 17.24 | 0.79 / 2.23 / 4.11 / 24.23 | 5.38 | 1 / 0 | 19, 76, 131104 | 8 | 0.38 |
| firsts-rest | 6.41 / 11.82 / 12.33 | 1.32 / 2.66 / 4.36 / 5.84 | 4.04 | 0 / 0 | 251, 3136, 175526 | 2 | 0.44 |

- **Reproduced.** Every scenario's allocations match ps-msz.8.9's take to the byte (firsts-rest's within 2 B
  a frame); the timings fall within the load's spread; no frame over 33 ms. The one frame over 16.7 ms
  (firsts, 1869 s, 24.2 ms) had 2.7 ms of CPU: the machine's load. The first sightings' worst probe frame
  was 21.5 ms (26.3 in ps-msz.8.9's).
- **The probe's own garbage moved the collections.** The probe's allocations are left out of each frame's
  `alloc_bytes`, but the collections they bring on are not: the peak's and the break's one collection each
  came just after a probe point (ps-msz.8.9's take had none), and firsts had 8 against the full run's 2.
  The census (`profile-alloc.csv`, part `probe`) put firsts' probe at 24.7 MB against the frames' 4 MB: a
  capture of the camera made on every frame of a probed run (`GpuProbe.Frame`'s lambdas, now in a method of
  their own), and the JSON rebuilt with every point after each point (each point's line is now made once).
  After: the probe 5.9 MB in firsts, which has **3 collections**, and the peak and the break none.
- A probe draw that throws now restores the camera's target and the passes it turned off.
