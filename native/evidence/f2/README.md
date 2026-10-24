# F2: the oracle fixtures

Measured by `hv-t8o8.3` on 2026-10-24. Summary in the design's [Oracle fixtures](../../../docs/design/native-app.md#oracle-fixtures); the format in [native/oracle/FORMAT.md](../../oracle/FORMAT.md).

`scripts/oracle-export.ts` (instrumentation only) wrote `native/oracle/`
([FORMAT.md](../../oracle/FORMAT.md),
[README.md](../../oracle/README.md)) from the TS tree identical to
`ts-freeze`.

- **Runs:** 215: the 13 core tracks × 9 profiles (117) and the other 49
  shipped tracks × `reactive-moderate` and `mash4` (98); 1,479,355 frame
  hashes at 60 fps. 15 runs keep the full log (every core track with
  `reactive-moderate`, the Galop and `causality-mix` with `mash4`); the rest
  keep per-second hash chains. Exporting took 23 min on 10 jobs, and the truncation check 17 min more (about 20 s
  for a 65 s track, 60–180 s for the longest).
- **Size:** fixtures 29.5 MB, math vectors 0.2 MB, content 0.01 MB, stills
  26.4 MB (19 PNGs at 1920 × 1080). Full logs for every core profile would have
  been about 75 MB, and four Galop profiles 35 MB: both were trimmed to chains.
  The stills stay lossless PNG (lossless WebP saved only a quarter, and lossy
  compression would add artifacts at pixel-art edges, where the renderer's
  perceptual comparison looks).
- **The keys-only assertion** holds on every run: the exported keys-only run
  reproduces the player's keys, and the same keys run without frames give the
  identical recording, so taking frames never changes a run.
- **The TS sim depends on where it stops, in two reads.** The player's own run
  (its `decide` events are extra sim stops) differs from the keys-only run on
  10 of 215 runs: 9 by one or two sounds' pans, because `emit.ts:287-290` pans
  a sound by the camera at the sound's time, clamped to how far the camera
  track has been recorded when the sound is emitted; and one by a warp break
  landing on a different stop (`cc-hairy-larry…`, `reactive-half`, 141.53 s
  against 141.533 s). The oracle's reference is the keys-only run, whose stops
  the Rust sim shares in Replay. Live, arrival bursts change the stops, so
  these two reads will break B's "streaming equals batch" check unless the
  port pins them (B7 decides: for example, pan by the camera at the emission
  time); recorded per run as `playerRunDifference`.
- **Truncation on TS**, run as `check:reactive`'s perturbation check (its
  levels and options, `reactive-moderate` and `mash4`, its default cuts) but
  deleting every entry known after the cut, the song's length held:
  `causality-mix` fails 25 of 580 cuts and the Galop 25 of 1171; the negative
  control (reading the evidence a second ahead) fails at the first cut, as it
  must. Every failure traces to one of three reads, now live exceptions:
  `volleyGroup`'s look-ahead ("Archer groups' end": the spawn mix, archers'
  shots), `downbeatAtOrAfter` returning the time itself past the last bar
  (vacuum drops and level-ups fire at once), and set-piece candidates needing
  a later downbeat (a boss's slot going to a ring; the kick line's entrance)
  ("Events past the last committed beat").
- **Math functions the ported code calls:** `abs`, `atan2`, `ceil`, `cos`,
  `exp`, `floor`, `hypot`, `imul`, `log`, `log2`, `max`, `min`, `pow`,
  `round`, `sign`, `sin`, `sqrt`; 1000 V8 vectors each (Node 24.16, V8 13.6).
- **Content hash:** `99bee67636d7fdbf`.
