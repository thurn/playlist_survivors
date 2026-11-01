# The run's performance baseline on the Mac (ps-msz.8.2)

The game's first frame numbers: a windowless frame probe and a per-frame CPU profile in the player,
and one command over the run's standard scenarios. Taken on jarvis (Apple M5 Max, Metal), the IL2CPP
player built from this bead's tree, 2026-10-31. Mac numbers only guide M4's work; Cygnus measures
against the targets in M5 (docs/design/unity-game.md, Performance targets).

```sh
uv run --project demo3d/tools game perf --evidence perf   # builds if needed; about 5 minutes
uv run --project demo3d/tools game perf --only peak,break # a subset
```

It writes [summary.json](summary.json) and each scenario's probe (`gpu-probe-<scenario>.json`) here;
the per-frame profiles (`profile.csv`, 16 MB for the full run) and the players' logs stay in
`captures/game/perf/<scenario>/`. It exits 1 if a player fails.

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

## The baseline

The take in summary.json (load average 22 to 37: other agents' builds shared the machine). Times in
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
