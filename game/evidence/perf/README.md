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
