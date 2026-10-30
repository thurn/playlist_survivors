# PX1: foundations (2026-10-29)

The `demo2d/` Unity project, the demo tools serving it, the vendor guard covering it, and the
spikes PX10 needs. Every run was on the operator's Mac (M5 Max, Unity 6000.6.7f1, Metal),
in batch mode, from the worktree's root. The player takes absolute paths: a Mac player's
working directory is the folder holding the `.app`, so relative paths land in `Builds/`.

## One command sets up a worktree

`uv run --project demo3d/tools demo2d setup`

| Run | Library | Unity's open | Total |
|---|---|---|---|
| first, in this worktree | none: the first import builds it, then setup saves it to the cache | 36.7 s | 46.0 s |
| a fresh `git worktree add` of the commit | cloned from the cache in 2.7 s | 22.6 s | 28.5 s |

Both verified the Unity license. Setup also rebuilt the machine's sync record with the new
`pixel_assets` root: 427,093 vendor files across the roots, in 6.8 s (3.1 s and 1.3 s once
cached). The fresh worktree's checkout stayed clean.

## Compile, test and build times (the spikes)

| Step | Command | Time |
|---|---|---|
| Cold compile (no Library: import and compile) | `demo2d setup` (first run) | 36.7 s |
| Warm compile (one script changed) | `demo2d unity Noop` | 7.9 s |
| Cold EditMode tests (no Library) | `demo2d test` | 31.3 s, 23 of 23 passed |
| Warm EditMode tests | `demo2d test` | 13.1 to 17.8 s, 23 of 23 passed |
| IL2CPP player, first | `demo2d build` | 60.8 s in BuildPipeline, 68.3 s in all |
| IL2CPP player, rebuild | `demo2d build` | 16.4 s, 22.7 s in all |
| Mono player | `demo2d build --backend mono` | 13.9 s, 21.5 s in all |

The 23 tests are PX4's 18 choreography tests (`Demo2D.Choreography.Tests`, now running in
Unity too) and PX1's 5 (`Demo2D.EditMode.Tests`: the configuration and the render tag). The
IL2CPP build's reported 981 MiB is mostly its `_BackUpThisFolder_ButDontShipItWithYourGame`
symbols (905 MB); the app is 77 MB.

## A windowless still

`"$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo" -batchmode -force-metal -logFile "$PWD/demo2d/Logs/px1-stills.log" --capture-stills "$PWD/demo2d/evidence/px1" --stills-at 2 --seconds 2.5`

[still-002.000.jpg](still-002.000.jpg): the empty scene's generated test card (no vendor art)
at the base view, 30 × 16.9 m at 32 grid pixels a metre, drawn by URP's 2D renderer under the
dim cool global light and three point lights. The first still came out unlit: a sprite
renderer made at runtime in a player gets the unlit default material, so the scene now names
URP's `Sprite-Lit-Default` (PX5's crowd will set its own material anyway).

## Frame timing in a batch-mode player

`"$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo" -batchmode -force-metal -logFile "$PWD/demo2d/Logs/px1-timing.log" --frame-timing "$PWD/captures/px1-timing/batch-400.json" --spike-sprites 400 --warmup 2 --seconds 10`

The player draws the main camera by hand into a 1920×1080 target every frame (a batch-mode
player's loop draws nothing), reads back one pixel to wait for the GPU, and records
FrameTimingManager, the "Frame Time" profiler counters and memory. With 400 and 4,000 moving
lit sprites ([400](frame-timing-batch-400.json), [4,000](frame-timing-batch-4000.json)):

| | 400 sprites | 4,000 sprites |
|---|---|---|
| frames in 10 s | 9,168 | 3,377 |
| frames with CPU main-thread time | 9,168 (p99 3.5 ms) | 3,377 (p99 5.6 ms) |
| frames with a GPU time (FrameTimingManager) | 286 (3%) | 106 (3%) |
| frames with a GPU time (the counter) | 286 | 106 |
| draw to GPU readback, every frame | p50 0.58, p99 3.4 ms | p50 2.3, p99 5.3 ms |
| render-thread time | always 0 | always 0 |
| system memory, start to end | 534 to 532 MiB | 557 to 555 MiB |

- **CPU times: yes, every frame.** FrameTimingManager and the counters agree.
- **GPU times: not reliably.** Both report a GPU time on only 3% of hand-drawn frames,
  whatever the load, so they can't give a per-frame 99th percentile.
- **What PX10 can use instead:** the draw-to-readback time. Waiting on a one-pixel readback
  after each draw makes the main thread's frame time include the GPU's work, so the main
  thread's time is the whole frame's cost, measured every frame. It is conservative: CPU and
  GPU don't overlap as they do in a windowed player. Metal command-buffer timing would need a
  native plugin; not needed given this.
- **Memory stays flat** over 9,168 hand-drawn frames with the 2D renderer, unlike demo3d's
  URP 17.6 3D renderer, which grew about 90 MB a frame (demo3d's TD4). A whole-song
  batch-mode run (about 12,000 frames at 60 fps) is feasible.
- **A windowed comparison** (does a presenting player report GPU times every frame?) was the
  operator's to run; PX10 deleted the spike, and the question with it.
