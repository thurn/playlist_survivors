# A7: remaining groups, corpus parity and the stream's causality

Measured by `hv-t8o8.12` on 2026-10-24 on macOS arm64 (Mac17,6).

## Bands, brightness and timbre

`cargo test -p analysis --release --test stage_parity_a7 -- --include-ignored --nocapture`
(native/crates/analysis/tests/stage_parity_a7.rs; fixture from [fixture.py](fixture.py),
Python's `stream.build_stream` on the golden excerpts' PCM):

```text
house-128: bands within 2.25e-7, brightness 2.46e-9, timbre 9.15e-10; 0 onset bands differ
orpheus-galop: bands within 4.19e-7, brightness 4.20e-8, timbre 2.11e-9; 0 onset bands differ
orpheus-galop-hindsight: bands within 8.66e-7, brightness 2.93e-8, timbre 2.98e-8; 0 onset bands differ
house-128: analyzer's bands, brightness and timbre within 2.25e-7; onset bands equal
orpheus-galop: analyzer's bands, brightness and timbre within 4.19e-7; onset bands equal
orpheus-galop-hindsight: analyzer's bands, brightness and timbre within 8.66e-7; onset bands equal
```

- Tick bands, tick brightness, onset bands and timbre entries (1,080 ticks, 544 onsets, 54
  entries over 54 s) agree with Python within 9e-7 (tolerance 1e-5, derived in the test), every
  onset's band is Python's, and every density is bit-equal; the same from the whole analyzer fed
  in blocks of 777 samples.
- Not to the bit: numpy 2.2 runs `rfft` on float32 frames in float32 (pocketfft's own), where
  Rust takes a float64 FFT and rounds it; everything after follows numpy's arithmetic (float32
  pairwise band sums, the float64 centroid and flatness, NEP 50's float32 comparisons with Python
  floats, scipy's exact medians, Python's `** 0.5`). numpy's array `exp`, `log`, `log10` and
  `log2` were checked equal to the C library's on 100000 values each before porting (the
  inventory step in AGENTS.md).
