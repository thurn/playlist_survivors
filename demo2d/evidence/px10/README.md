# PX10: the demo (2026-10-30)

Everything here ran on the operator's Mac (Apple silicon, Metal), windowless, from the IL2CPP
player in the bead's worktree:

```
uv run --project demo3d/tools demo2d build
APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
"$APP" -batchmode -force-metal --measure "$PWD/demo2d/evidence/px10/song.json"
"$APP" -batchmode -force-metal --capture-stills "$PWD/demo2d/evidence/px10" --stills-at 6,30,89.35,119.7,166,176,195.928
```

## The song check: pass

`--measure` draws every fixed-time frame of the song (0 to 199 s, 11,940 frames at 1/60 s) by
hand at 1920×1080. Each frame is timed from the start of its update to a one-pixel readback of
the finished image, so the time includes the GPU's work. The check passes when the 99th
percentile is under 16.7 ms and no second of song has a frame over 25 ms.

| Run | Pass | Mean | p50 | p99 | Worst frame | Seconds over 25 ms |
|---|---|---|---|---|---|---|
| [song.json](song.json) (the final build) | yes | 6.94 | 7.84 | 8.98 | 16.90 (0 s) | 0 |
| [song-run1.json](song-run1.json) | yes | 6.72 | 7.83 | 8.83 | 24.74 (0 s) | 0 |
| [song-run2.json](song-run2.json) | yes | 7.09 | 7.89 | 8.93 | 15.04 (0 s) | 0 |
| [song-run3.json](song-run3.json) | yes | 7.04 | 7.87 | 8.89 | 14.88 (0 s) | 0 |
| [song-before-prewarm.json](song-before-prewarm.json) | no | 5.39 | 4.77 | 8.91 | 45.35 (0 s), 42.59 (176 s) | 2 |
| [song-no-prewarm-a.json](song-no-prewarm-a.json) | no | 7.02 | 7.84 | 9.04 | 59.70 (0 s) | 1 |
| [song-no-prewarm-b.json](song-no-prewarm-b.json) | no | 7.09 | 7.88 | 9.00 | 51.78 (0 s) | 1 |

All times are in milliseconds.

- **Load:** at most 383 monsters on screen, 104 effects, about 3,030 particles and 28 effect
  lights. The worst second after the first is 10 to 15 ms, in the final peak (155 to 185 s).
- **The first runs failed on stalls, not on load.** The first frame (45 ms) and, in the very
  first run, the boss's first hellfire at 176 s (43 ms) were first uses: a pool growing, a
  shader drawn for the first time. The player now prewarms at load
  (`SongPlayback.Prewarm`): it makes every effect kind's whole pool and draws the start
  frame once with one of each kind in view. With it, the worst frame is 15 to 25 ms. Without
  it, in the same build session, the first frame took 52 to 60 ms. The 176 s stall didn't come
  back in later runs even without the prewarm; Metal keeps its compiled shaders between runs.
- **The mean moved, the prewarm didn't move it.** The first run's mean (5.4 ms) was lower than
  every later run's (6.7 to 7.1 ms), with or without the prewarm. That's the machine's state,
  not the change.
- **Nothing was thinned.** p99 is half the limit, so the level script, spells, effect lights
  and the horde (383 at the peak) stay as PX9 left them.

### CPU and GPU times

Each JSON also has FrameTimingManager's times:

- `ftmCpuMs`: the CPU frame time, reported on almost every frame. Its p99 is 9.1 to 9.3 ms,
  matching the readback-timed frames.
- `ftmGpuMs`: reported on only 373 frames, about 3% (PX1 found the same in batch mode). Its
  p99 is about 20 ms, but those samples can't be one frame's GPU work. Every frame waits for
  its own readback and averages about 7 ms from start to finish. A GPU frame averaging
  12.6 ms would hold every frame past that. In a batch-mode player, which presents nothing,
  the GPU frame seems to span the idle time between frames.

So the pass criterion uses the readback-timed frame, which covers the CPU's and the GPU's
work back to back. That overstates any frame where the two overlap. PX1's spike asked
whether a windowed player reports a GPU time every frame; the spike is now gone (below), and
the operator's viewing check covers the windowed app.

The JSON's `perSecond` rows hold each second's worst frame and peak counts. `every15thFrame`
samples the load, and `framesOverSecondLimit` lists every frame over 25 ms (none in the
passing runs).

## One measurement path

PX1's `FrameTimingSpike` (`--frame-timing`, on the boot scene's test card) and PX7's
`--measure` (the real song, readback time only) both timed frames. `--measure` now records
FrameTimingManager's times and the worst frame per second too. The spike, its `--frame-timing`
gate, and the test card's `--spike-sprites` walkers are deleted, as the Mason's M2 audit asked.

## The final beauty stills

The six PX9 moments plus the ending, from the final build. PX10 changed nothing that draws.
Against PX9's stills, the intro is identical, and the others differ in under 1% of their
pixels (mean difference 0.02 to 0.35 of 255), in small local spots.

| Still | Time | On screen |
|---|---|---|
| [Intro](still-006.000.jpg) | 6 s | 3 |
| [First wave](still-030.000.jpg) | 30 s | 61 |
| [Caster volley](still-089.350.jpg) | 89.35 s | 190 |
| [Elite kill](still-119.700.jpg) | 119.7 s | 122 |
| [Boss entry](still-166.000.jpg) | 166 s | 360 |
| [Climax](still-176.000.jpg) | 176 s | 361 |
| [The last slam](still-195.928.jpg) | 195.928 s | 154 (+159 dying) |

The last slam is new: the ending's final accent, the struck horde lit gold round the knight
and the boss falling at his right. PX9's checklist judgement holds for the six (its README).
The operator's judgement is pending (plan.md).

## Review (Warden, ps-tfn.5.2)

- **The first frame's stall was the timer's.** With the prewarm, the song's first frame still took
  13 to 17 ms in short runs (and 24.74 ms in [song-run1.json](song-run1.json), 0.26 ms under the
  limit), against about 2 ms for the frames after it with the same 3 on screen. It was the
  measurement's own first one-pixel readback. `--measure` now makes one untimed readback after
  the prewarm. The first second's worst frame is now 5.5 to 8.4 ms. In
  [song-review.json](song-review.json) the worst frame of the whole song is 10.32 ms (at 100 s),
  with mean 6.75 and p99 8.82.
- **The live mix clipped.** Mixed offline from `--dump-sounds` at the player's levels (the music at
  1.0, every effect sound at its full event gain, no limiter), the song went past full scale in 187
  of its 198 seconds, peaking at 3.25. The densest beats stack 9 sounds, 3 of them hits at gain 1.0.
  demo3d's offline mix (`demo3d mix`) sets the music at 0.8 and the effects at 0.45, with a soft
  limiter. The player now uses the same levels and limiter (`MixLimiter`, on the listener's object).
  Offline at those levels, the limiter's knee touches 4.5% of sample frames, and nothing passes 0.95.

