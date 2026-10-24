# A4: hindsight grids and the phase check

Measured by `hv-t8o8.9` on 2026-10-24 on macOS arm64 (Mac17,6, 18 cores; `cargo test -p analysis
--test stage_parity_a4 -- --nocapture --test-threads=1`, native/crates/analysis/tests/stage_parity_a4.rs).

```text
test hindsight_entries_match_python ... hindsight: 30 entries bit-equal; decode ms (window up to 20 s): min 15, median 260, p90 327, max 328
test offline_activations_and_beats_match_python ... house-128: offline activations worst 0.121 of tolerance (frame 249) from Python's features, 0.130 from Rust's
house-128: 26 offline beats bit-equal
orpheus-galop: offline activations worst 0.116 of tolerance (frame 567) from Python's features, 0.084 from Rust's
orpheus-galop: 35 offline beats bit-equal
test offline_features_match_python ... house-128: offline spectra worst 0.517 of tolerance (value 35281)
orpheus-galop: offline spectra worst 0.517 of tolerance (value 7135)
test the_analyzer_from_pcm_commits_pythons_phased_beats ... analyzer from PCM: 30 hindsight entries and 90 phased beats bit-equal
test the_phase_check_commits_pythons_beats ... phase check: 90 committed beats bit-equal, 143 frames shifted
test the_worker_reports_its_stalls ... worker at D = 0: 29 stalls, 5.606933746s in all
test with_a_delay_entries_count_from_their_window_ends_plus_d ... D = 0.67: 91 committed beats as the oracle gives them (128 frames shifted); 14 differ from D = 0
```

Each stage fed Python's own upstream, with D = 0:

- **The offline front end** (1024, 2048 and 4096-sample frames centred on `n * 441`, 266 features):
  the log spectra within 0.52 of their tolerance; the positive differences checked exactly as
  `max(now - back, 0)` on both sides.
- **The BiLSTM ensemble:** activations within 0.13 of their tolerance, fed Python's features or
  Rust's.
- **The offline DBN** (Viterbi in float64 log space, the first best predecessor, the peak in each
  run of beat states): the beats bit-equal to Python's, from Python's activations and from Rust's.
- **The hindsight entries** of the 30 s Galop excerpt (30 windows, 20 s once the audio allows),
  decoded from PCM: every window's offline and believed beats bit-equal.
- **The phase check:** fed Python's activations and hindsight entries, all 90 committed beats
  (time, knownAt, period, evidence) bit-equal, with the grid shifted at 143 frames.
- **The whole analyzer from PCM** (D = 0): the 30 hindsight entries and the 90 phase-checked beats
  bit-equal; the worker thread gives the inline results exactly, at D = 0 and at D = 0.67 s.
- **An awkward stream end:** the excerpt cut to 29.997 s (neither whole seconds nor a multiple of
  441 samples) gives Python's 29 entries to the bit (`tests/fixtures/a4-galop-29997.json`, from
  `stream.hindsight_entries`).
- **Viterbi edges:** a 5-state model with tied transitions decodes as madmom's
  `HiddenMarkovModel` and `process_offline` do, on activations with exact 1.0 and 0.0 values,
  beat runs at both window edges and all zeros (`tests/fixtures/a4-tiny-dbn.*`).
- **D > 0:** with the live D = 0.67 s every entry is usable 0.67 s after its window ends, and the
  analyzer's commits from PCM equal the commit rule fed Python's entries shifted by D: 91
  committed beats, 14 of them different from D = 0's on this excerpt (the delay's cost; A7's
  corpus parity measures it across the corpus).
- **Stalls:** `Analyzer::hindsight_stalls` counts the times the analyzer waited for the worker,
  and how long (at D = 0 it waits for every decode, as expected; live, D = 0.67 s leaves room).

The phase check's complex arithmetic follows numpy's (checked against numpy on 5000 random cases
before porting, and pinned by a unit test to numpy's bits for the unit vectors, the means of 3, 7
and 40, and their abs and angle): `2j * pi * x / period` divides by the period as a complex (Smith's method, so
multiplying by its reciprocal), the complex mean is numpy's pairwise sum of (re, im) pairs times
`1 / n`, and `abs` and `angle` are the C library's `hypot` and `atan2` (Python's own
`math.hypot` differs).

**Decode time** (one window's offline features, BiLSTM ensemble and Viterbi decode; measured
with `cargo test --release ... hindsight_entries -- --test-threads=1`, load average about 3): a full 20 s window 273-283 ms (p90 273-281 ms,
three runs), shorter windows less (median over the 30 windows 219-224 ms). That is well inside
`D = 670 ms`, set in phase 0 from the spike's decode times.

Gate cost: `native/scripts/check-fast` for the analysis crate takes 34 s warm, mostly the inline
hindsight decodes in the A3 and A4 tests.

The bit-equality of the offline beats also rests on numpy's float32 `log` being the system's
`logf` (true on macOS arm64, where the golden data is made).

Not covered: the real `D` across the corpus (A7's corpus parity reports its cost), and the app's
worker thread wiring (C3/C4 drive the analyzer; this bead gives it the worker mode).
