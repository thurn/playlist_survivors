# B5: judging, the settled groove and the gate against the oracle

Measured 2026-10-24 on arm64 macOS (rustc 1.98.1), hv-t8o8.17. B4 (hv-t8o8.16) ported the
judging code with the engine (`sim::reactive::{judging, settle}`, `engine/judge.rs`); B5 checks it
over whole runs and on the path no shipped stream takes.

## Whole runs

With the keys replayed, presses' judgments, the settled judgments and the groove and tier they
drive depend on the stream and the keys alone, not on set pieces, so `judging_matches_over_whole_runs`
(`crates/sim/tests/oracle_prefix.rs`) compares them over each full core run to the Rust run's end
(the song's end plus a second; TS plays on through the finale's epilogue, B6's): every `settle`,
`groove` and `tier` line, and each `input`'s judging fields (`s`, `kind`, `key`, `rating`,
`delta`, `class`, `extra`, `offbeat`, `unjudged`, `stomp`, `rock`, `bend`, `steps`; its world time
and stride, which B6's hit-stops move, aside).

| Run | Judging lines matched |
|---|---|
| `accelerando-100-140/reactive-moderate` | 565 |
| `causality-mix/mash4` | 974 |
| `causality-mix/reactive-moderate` | 549 |
| `cc-hairy-larry-and-the-flying-hungarians-ride-me-to-heaven/reactive-moderate` | 1268 |
| `cc-kevin-macleod-black-vortex/reactive-moderate` | 1331 |
| `cc-kimiko-ishizaka-aria/reactive-moderate` | 1683 |
| `dnb-174/reactive-moderate` | 706 |
| `hiphop-92-swing/reactive-moderate` | 364 |
| `house-128/reactive-moderate` | 517 |
| `notjam-breakbeat-chips/reactive-moderate` | 216 |
| `notjam-chill-menu/reactive-moderate` | 739 |
| `notjam-waltz-of-the-lich-king/reactive-moderate` | 587 |
| `orpheus-galop/mash4` | 1594 |
| `orpheus-galop/reactive-moderate` | 1457 |
| `pop-arrangement/reactive-moderate` | 1266 |

Every line matches on all 15 runs. This covers the provisional ratings, the settle step's readings
(`chooseReading`, the beat registry), the benefit of the doubt, the confidence-scaled penalties, the
lock-on grace, beatless credit, the drain 2 s after each beat and the tier changes.

## Without hindsight

Every shipped stream carries hindsight entries, so reactive judging before stage 2 (presses change
the groove when pressed; the drain runs on each beat) never runs on the oracle.
`gen-judging-vectors.mts` runs the TS simulation on three streams with their hindsight removed
(house-128 and cc-kimiko-ishizaka-aria with the moderate player's keys, the Galop with mash4's)
and `judging_without_hindsight_matches_ts` compares the groove, the tier and the inputs' judging
fields: all match.

## Running values

TS memoized `judging()` and the judged-time clock over the whole stream; here they grow tick by
tick (`sim::reactive::judging`). `extended_tick_by_tick_equals_extended_at_once` checks that
growing it one tick at a time, or in chunks, gives exactly what one extension over every tick
gives, on evidence that crosses the gate every way (short and long clear stretches, a lock-on after
a long gap, a one-tick wobble).

Not shown: the chain runs (the other profiles and tracks), whose hash chains include the emitted
categories (B7).
