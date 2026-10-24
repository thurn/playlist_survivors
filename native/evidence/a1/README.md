# A1: the online front end and LSTM ensemble

Measured by `hv-t8o8.6` on 2026-10-24 (`cargo test -p analysis --test stage_parity_a1 -- --nocapture`, native/crates/analysis/tests/stage_parity_a1.rs).

## Stage parity against Python's golden data (native/models/golden/)

Fed Python's decoded PCM in uneven blocks (1, 7, 441, 512, 4096, 333 samples, repeating).
Errors as a share of the tolerance (1e-5 relative, 1e-7 absolute floor):

```text
house-128: spectra worst 0.516 of tolerance (frame 72, band 76), activations worst 0.267 (frame 931)
orpheus-galop: spectra worst 0.517 of tolerance (frame 4, band 80), activations worst 0.189 (frame 630)
```

- Frames 0 and 60 are cut exactly as madmom cuts them (bit-equal raw samples); each excerpt has
  Python's 1200 frames, and streams cut short (len - 1, len - 200, 599 * 441 + 1, 599 * 441
  samples) have madmom's ceil(len / 441) frames, each within tolerance of Python's.
- The positive differences are checked exactly, on both sides: each is `max(now - back, 0)` of its
  frame's spectrum and the one `diff_frames` back, in float32 (zero before then), in Rust's
  features and in Python's. So difference parity follows from spectrum parity; their own
  relative tolerance would be too tight, since a difference of two near-equal spectrum values
  inherits their float32 rounding (house-128, frame 761, band 1: spectra 1 and 2 ulps off, the
  0.016 difference 4.8e-7 off, 2.9 times its own relative tolerance).
- The stream is independent of block sizes (bit-equal activations fed whole or in blocks).
- Parity is measured, not exact by construction: numpy's float32 BLAS sums accumulate in float32
  in their own order, the port's in float64.
- What this does not show: parity on audio beyond the two 12 s excerpts, or on 48 kHz capture
  (resampled live, built and measured in C4).

## Builds and tests

Fresh worktree, warm cargo registry, load average about 5:

- Cold `cargo test --workspace --no-run`: 8.7 s; cold `cargo build --workspace --release`: 6.0 s (realfft and serde are the only dependencies so far).
- Warm, three runs: `cargo test --workspace` 0.31, 0.32, 0.31 s; `cargo nextest run --workspace` 0.26, 0.25, 0.25 s.
- `native/scripts/check-fast` (analysis changed): 0.8 s warm.
- The gate target directory's contention needs two concurrent native gate runs; none ran during A1, so it stays on the watch list.
