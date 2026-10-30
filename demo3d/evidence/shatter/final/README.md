# Crystal shatter: the final pass (ps-2iq.4)

Everything on at once: the shards, cracks and motion ([../motion](../motion/README.md)) with the
burst, flash and glass break ([../burst](../burst/README.md)), on the foundation's baked shards
([../foundation](../foundation/README.md)). The design is in
[crowd.md](../../../../docs/tech-demo-3d/crowd.md#monster-styles) and
[effects.md](../../../../docs/tech-demo-3d/effects.md#the-look). Every strip was rendered by the
Mac's batch-mode IL2CPP player in fixed time on 2026-10-30, every 2nd 1/60 s frame, **with** the
effect library.

[contact-sheet.jpg](contact-sheet.jpg) has eight frames of each strip below: the hold, the cracks,
the pop, the flight and the settled pile.

| file | what | command (`demo3d filmstrip ...`) |
|---|---|---|
| [skeleton.jpg](skeleton.jpg) | fodder: the lone skeleton at the showcase preset, hold 41.233, release 41.617, to its pile 1.5 s later | `skeleton --at 41.2 --frames 60 --every 2 --columns 6 --scale 0.8 --crop 760,0,400,300 --player-args "--monster-style crystal --envelope 24 --view-width 12" --evidence shatter/final` |
| [troll.jpg](troll.jpg) | an elite: a troll, hold 163.733, release 164.117, among other deaths | `troll --at 163.7 --frames 60 --every 2 --columns 6 --scale 0.8 --crop 1090,300,640,540 --player-args "<the same>" --evidence shatter/final` |
| [crowd.jpg](crowd.jpg) | a crowded beat at a 48 cap: seven skeletons released on 197.25 and seven goblins on 197.483, under the hero's final slam | `crowd --at 196.95 --frames 40 --every 2 --columns 4 --scale 1.25 --crop 0,140,1920,940 --player-args "--monster-style crystal --envelope 48 --view-width 12" --evidence shatter/final` |
| [before-after.jpg](before-after.jpg) | the same skeleton: each band's top row is the old triangle shatter (a build of 97a17230, the commit before the epic), its bottom row is today's | `vs-before --app <that build> --at 41.5 --frames 32 --every 2 --columns 8 --scale 0.6 --crop 760,0,400,300 --player-args "<the preset>"`, then `vs-after ... --baseline vs-before --evidence shatter/final` (renamed) |

The mp4s are in the gitignored `captures/filmstrip/<slug>/`.

## What the final pass changed

- **A crowded beat's flashes washed the view.** Each release's 0.3 s flash is a light cue in the gem
  colour. Seven goblins on 197.483 summed into a lime wash over the whole frame for about 0.1 s.
  Over the 1080p frame, the mean of G − (R + B)/2 rose from 3 to 34 levels. Now the releases on
  one beat share their flash: each one's light is 1/√n of n (`EffectDriver.SetShatters`, the
  spawn's new `LightGain`). A crowded beat still flashes harder than a lone death (√7 = 2.6
  times), but the peak excess is now 17 levels, fading over about 0.15 s. A lone death is unchanged.
- **Checked and left alone.**
  - The troll's beat washes orange-pink on 164.13 to 164.2 s. The same frames under
    `--monster-style painted` wash just as much, and `--light-pool 0` removes it, so that wash is
    the stock hit effects' lights, not the shatter's.
  - The hero's swirl crosses the skeleton during its hold. That is the hero's own attack and it
    clears before the pop.
  - The burst's glints and dust stay small next to the shards, as the burst bead tuned them.
  - The timing is unchanged: the pop lands on the release beat and the 4-frame hit-stop holds.

## Checks

- **The shards are deterministic; the effects never were.** With `--no-effects`, two runs of 30
  frames through the death match pixel for pixel. With effects, two runs differ in a few
  thousand scattered pixels by at most 6 of 255. That happens under `--monster-style painted`
  too, and in the build before the epic (97a17230, painted, 0 of 30 frames match), so the GPU
  effect runtime has never been pixel-exact run to run. The shards and their placement are.
- **Without the flag**, a look set against [look/final](../../look/final/look.md) is within 0.11 L*
  on every still's p2, p50, p98, contrast and chroma (largest: song-025 p50).
- **The full song** at its own density under `--monster-style crystal`, windowless in fixed time:
  no errors. 2,336 deaths, 9,660 effect spawns, at most 12,051 shards live. It took 91 s of wall
  time for 200 s of song, against 10 s for the same run in the build before the epic. The
  measured cost is in [crowd.md](../../../../docs/tech-demo-3d/crowd.md#monster-styles) (the
  crystal shatter, Cost).
- demo3d's EditMode tests: 295 of 298 passed, 3 skipped.

The judgement with the music in a window is the operator's: plan.md, Pending on the operator,
"Judge the crystal shatter with the music".
