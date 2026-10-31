# Trailer: the --trailer player mode (ps-hgs.6)

`--trailer` now plays the trailer file's ten shots in the normal player
([trailer.md, Playback and cuts](../../../../docs/tech-demo-3d/trailer.md#playback-and-cuts)).
Each shot is its own simulation in its own resident environment, cut on the
beat. Everything here is from the IL2CPP player on the Mac (Apple M5 Max,
Metal), windowless at fixed time, built at staging `286eafb3` plus this bead.
The look is still the plain environments' (the per-shot grade, grain, dirt and
lens hits are ps-hgs.3's).

**Verdict:** all ten shots play without errors. Every cut lands on its beat in
its environment, opens on the hero's blow, and costs 0.1–0.3 ms. The three
flash cuts wash out near-white and clear within an eighth note. The picture
cuts to black on 99.3.

| file | what |
|---|---|
| `trailer-*.jpg` | the whole trailer, one run: each shot's first frame, frames either side of the flash cuts, and the end |
| `preroll3-*.jpg` | `--shot 3 --from-preroll`: the vortex forming before its cut |
| `startat27-*.jpg` | `--start-at 27` |
| `snow-*.jpg` | `--environment snow`: every shot pinned to the snow |
| `contact-sheet.jpg` | all of the above |
| [normal-run.md](normal-run.md) | a normal run (no `--trailer`) against staging's own build and against look/final |
| [playback.py](playback.py) | the JPEGs and the contact sheet |

## The runs

Still names are trailer seconds (song seconds less the file's `startS`,
121.40), rounded up to the next 60 fps frame:

```
P="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$P" -batchmode -force-metal -logFile $PWD/captures/trailer/playback/player.log --trailer --fixed-time \
  --capture-stills $PWD/captures/trailer/playback --stills-format png --seconds 33 \
  --stills-at 0,2.465,2.481,2.50,2.55,2.60,4.0,8.674,14.85,14.868,14.9,15.0,17.965,21.062,22.610,24.158,25.707,27.24,27.255,27.30,30.35,30.40,31.10,31.13
"$P" -batchmode -force-metal -logFile $PWD/captures/trailer/preroll/player.log --trailer --shot 3 --from-preroll \
  --capture-stills $PWD/captures/trailer/preroll --stills-format png --stills-at 2.49,6,8.66,8.7 --seconds 7
"$P" -batchmode -force-metal -logFile $PWD/captures/trailer/startat/player.log --trailer --start-at 27 \
  --capture-stills $PWD/captures/trailer/startat --stills-format png --stills-at 27,27.3 --seconds 0.5
"$P" -batchmode -force-metal -logFile $PWD/captures/trailer/snow/player.log --trailer --environment snow \
  --capture-stills $PWD/captures/trailer/snow --stills-format png --stills-at 4,16,29.5 --seconds 30
uv run --project demo3d/tools python demo3d/evidence/trailer/playback/playback.py jpegs captures/trailer/playback trailer
```

(The player resolves relative paths elsewhere, so pass absolute ones.)

## Every cut

| shot | trailer s | environment | still | what it shows |
|---|---|---|---|---|
| 1 | 0.000 | crypt | trailer-000.000 | the duel: three brutes closing on the hero |
| 2 | 2.481 | desert | trailer-002.481 | the drop's flash cut: near-white over the desert |
| 3 | 8.674 | snow | trailer-008.674 | the vortex, the hero's spin landing on the cut |
| 4 | 14.868 | forest | trailer-014.868 | bar 89's flash cut, the ring landing under it |
| 5 | 17.965 | crypt | trailer-017.965 | the elite charge, turned 180° |
| 6 | 21.062 | desert | trailer-021.062 | burst 1, the vortex's spin |
| 7 | 22.610 | snow | trailer-022.610 | burst 2, the charge, turned 90° |
| 8 | 24.158 | crypt | trailer-024.158 | burst 3, lightning on the streams |
| 9 | 25.707 | desert | trailer-025.707 | burst 4, the converge |
| 10 | 27.255 | forest | trailer-027.255 | the climax's flash cut |

The player's log names each cut and its cost: the environment switch plus
the shot's reset, and the cut frame's CPU draw beside the frame before's.

| shot | cut frame (trailer s) | switch and reset | draw | the frame before |
|---|---:|---:|---:|---:|
| 1 | 0.000 | 0.13 ms | 0.33 ms | (the first frame) |
| 2 | 2.483 | 0.14 ms | 1.21 ms | 0.09 ms |
| 3 | 8.683 | 0.27 ms | 1.42 ms | 0.37 ms |
| 4 | 14.883 | 0.13 ms | 0.81 ms | 0.92 ms |
| 5 | 17.967 | 0.13 ms | 0.74 ms | 0.37 ms |
| 6 | 21.067 | 0.12 ms | 0.77 ms | 0.37 ms |
| 7 | 22.617 | 0.12 ms | 0.67 ms | 0.48 ms |
| 8 | 24.167 | 0.12 ms | 0.68 ms | 0.43 ms |
| 9 | 25.717 | 0.12 ms | 0.64 ms | 0.37 ms |
| 10 | 27.267 | 0.11 ms | 1.57 ms | 0.37 ms |

A cut's frame draws at most about 1.2 ms more CPU than the frame before: the
new shot's effect driver re-spawns the effects still alive at the cut. These
are batch-mode CPU times; the GPU side of a switch is ps-hgs.1's timing.

## Flash cuts

The cuts that land on a song map accent flash: 81.1, 89.1 and 97.1, the
drop and the two re-entries. The flash is the post feel's (`PostFeel.CutFlash`):
+5 stops, −70 saturation and +4 bloom on the cut frame, decaying
quadratically over an eighth note (0.19 s). The song's own accent flash
runs on top, as in a normal run.

- **81.1:** trailer-002.465 (the crypt, the third brute falling), 002.481
  (white), 002.500, 002.550, 002.600 (the desert, cleared).
- **89.1:** trailer-014.850 (the snow), 014.868, 014.900, 015.000 (the forest
  and the ring).
- **97.1:** trailer-027.240 (the desert), 027.255, 027.300.

## The end

- trailer-030.350: the frame before the final slam on 99.1.
- trailer-030.400: the slam, the whole view dying.
- trailer-031.100: the aftermath's last frame. The horde is dying, mid death
  clip, which runs about 1.2 s, longer than the aftermath's 0.77 s (a note
  for the shot list's tuning, ps-hgs.7).
- trailer-031.130: black from 99.3. The music fades over the 1.5 s after.

## Isolating a shot

- **`--shot 3 --from-preroll`:** preroll3-002.490 is the first frame of the
  pre-roll, the vortex already spread round the hero. Then 006.000, 008.660
  just before the cut and 008.700 just after, its spin landing. Only the shot's
  own flash cuts and big hits flash, so the drop's flash cut, which falls on
  this pre-roll's first frame, doesn't.
- **`--start-at 27`:** opens 0.26 s before the climax, on shot 9's wind-up
  into it (startat27-027.000). The climax's flash cut follows (027.300).
- **`--environment snow`:** every shot plays in the snow with its own path
  and formation (snow-004.000, the drop's charge; 016.000, bar 89's converge;
  029.500, the climax).

## Flags

Each rejected flag exits 1 before the first frame, naming the conflict, for
example:

```
InvalidOperationException: DemoMain: --trailer conflicts with --intensity: the trailer carries its own tuning, built on its file's base overlay
InvalidOperationException: DemoMain: --trailer conflicts with --view-width: it controls the song's own timeline
ArgumentException: --shot: trailer 'action' has shots 1 to 10, not 11
ArgumentException: --trailer: unknown trailer 'nope' (one of action)
```

The same holds for `--scene`, `--envelope`, `--hold-at`, `--beauty-empty`,
`--structure-breaks`, `--resident` and `--switch-every`. `--from-preroll`
without `--shot`, and `--shot` without `--trailer`, fail too.
`--roster heroic --monster-style crystal` fails as in a normal run.
