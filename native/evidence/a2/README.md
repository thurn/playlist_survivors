# A2: the DBN forward step and committed beats

Measured by `hv-t8o8.7` on 2026-10-24 on macOS arm64 (`cargo test -p analysis --test stage_parity_a2
-- --nocapture`, native/crates/analysis/tests/stage_parity_a2.rs).

```text
house-128: 1200 frames, 29 committed beats, all bit-equal
orpheus-galop: 1200 frames, 28 committed beats, all bit-equal
orpheus-galop 30 s: 3000 frames, 89 committed beats, all bit-equal
house-128 from PCM: 0 of 1200 states differ; 29 of 29 committed beats
orpheus-galop from PCM: 0 of 1200 states differ; 28 of 28 committed beats
```

- Fed Python's own activations, the online DBN's most likely state at every frame, the believed
  beat's age (`since`), the smoothed periods, the tick evidence and the committed beats (their
  times, knownAt, periods and evidence) are bit-equal to Python's on both 12 s golden excerpts and
  on the 30 s Galop hindsight excerpt (3000 frames, periods flipping between 0.28 and 1.07 s, past
  the evidence window, with the believed last beat moving backwards at each flip; its
  `commits_unphased`, the commit rule without A4's phase check).
- Fed from PCM through A1's front end and ensemble, whose activations differ from Python's at
  float32 rounding, no state differs on the 12 s excerpts and the committed beats are still
  bit-equal: the forward step's near-ties never flipped there. That is not guaranteed in general;
  the corpus parity (A7) measures it across every track.
- The bit-equality rests on following madmom and numpy exactly (the forward sums in pointer order
  in float64, the observation densities as numpy computes them for float32 activations, the first
  maximum, and `np.mean`'s pairwise summation, checked against np.sum at every branch), and on
  the platform's float32 `log` and float64 `exp`: on macOS arm64 numpy and Rust both call the
  system's, and the golden data is generated there.
- The commit invariant holds: each beat is known by the time the one before it falls, and the
  beats strictly increase.
- Not covered here: the hindsight phase check (A4), and Python's last tick, at the frame one past
  the stream's end, which reads the last frame (A3, with the ticks; A3 also owns holding each
  tick's evidence for the commits, which the test harness does here).
