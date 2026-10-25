# E1: Delayed listening's committed beats, measured with the Rust analyzer

`hv-t8o8.31`, 2026-10-24. Command (from `native/`, about 54 minutes at background QoS on a shared host):

    taskpolicy -b target/release/ps analysis-parity --all --delayed-sweep --jobs 8 --out evidence/e1/sweep

Each of the 61 corpus tracks (`index.json`) is analysed once at the live hindsight delay
`D = 0.67 s`. Delayed listening's beats come from `analysis::delayed::replay` over that run's
entries, for every grid and `M` in {1.5, 2, 2.5, 3, 4, 5} s, with `A = 0.048 s`. The test
`streamed_beats_are_the_replay_of_direct_listening` (`crates/analysis/tests/delayed.rs`) checks
that the replay equals what the analyzer streams in Delayed listening. Direct is the same run's own
committed beats, so the comparison holds `D` and the analyzer fixed.

- [`sweep/delayed.md`](sweep/delayed.md): the table by category (within 45 ms of each track's
  reference after 4 s, mean of tracks, with recall in brackets), and the choice by E1's rule.
- `sweep/delayed.json`: the same, as data.
- `sweep/parity.json` and `sweep/parity.md`: per track, including each grid at each `M`.

## Result

| Category | Tracks | Direct | offline at M = 3 s | online at 3 s | phased at 3 s |
|---|---|---|---|---|---|
| all | 61 | 78.6 (84.3) | **85.9 (86.9)** | 74.8 (81.5) | 78.4 (84.9) |
| cc | 12 | 75.9 (75.9) | 85.6 (80.9) | 80.1 (82.8) | 78.7 (81.0) |
| classical | 5 | 62.8 (64.0) | 84.4 (78.9) | 77.4 (79.3) | 73.7 (75.9) |
| notjam | 39 | 80.9 (87.6) | 84.6 (88.7) | 72.0 (79.2) | 78.7 (85.8) |
| synthetic | 5 | 83.0 (98.7) | 98.9 (95.2) | 81.7 (98.6) | 80.2 (96.7) |

- **The grid is offline hindsight.** It is the best grid in aggregate at its best `M`, and the only
  one no worse than Direct on any category: online and phased trail Direct on notjam at every `M`.
- **`M = 3 s`.** Offline reaches 86.7% at 5 s. 3 s gives 85.9%, the smallest `M` within a point of
  that best (2.5 s gives 84.2%).
- **The lead check:** none of the 61 tracks has a beat known less than `H` ahead, at any grid or `M`.

This confirms E0's Python estimate, which measured 86.1% against 79.0% for offline at 3 s.

## What it doesn't show

- **The references:** 55 of the 61 are madmom's offline decode, or grids fitted through it, so the
  offline grid is partly graded against its own model. E0 found the same advantage on the
  independent references, the synthetic tracks and the Galop's song map.
- **Recall:** on some tracks the offline decode drops to half tempo. Its beats stay precise, but it
  misses half of them. Recall falls on cc-alex-beroza-good-day (94.9% to 56.9%), dnb-174 (100% to
  83.4%) and notjam-locrian-chipdoom (93.6% to 43.8%); notjam-locrian-chipdoom also loses accuracy
  (46.7% to 31.7%), and notjam-baroque-chips falls from 100% to 91.7% on both. Readings chosen from
  presses still decide the tempo octave, as in Direct listening.
- **Timing:** these are stream-accuracy numbers, not the feel of play. E2 brings the mode to the app.
