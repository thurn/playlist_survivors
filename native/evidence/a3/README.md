# A3: ticks, energy, levels and onsets

Measured by `hv-t8o8.8` on 2026-10-24 on macOS arm64 (`cargo test -p analysis --test stage_parity_a3
-- --nocapture`, native/crates/analysis/tests/stage_parity_a3.rs).

```text
house-128: 98 onsets (6 strong) bit-equal
orpheus-galop: 134 onsets (10 strong) bit-equal
house-128: onset function worst 0.011 of the operands' tolerance (frame 895; 1.586 of its own relative tolerance)
house-128: 240 ticks bit-equal, levels [9, 1, 230]
orpheus-galop: onset function worst 0.005 of the operands' tolerance (frame 483; 0.090 of its own relative tolerance)
orpheus-galop: 240 ticks bit-equal, levels [0, 0, 240]
house-128 from PCM: 29 beats, 240 ticks (evidence worst 0.020 of tolerance), 98 onsets
orpheus-galop from PCM: 28 beats, 240 ticks (evidence worst 0.027 of tolerance), 134 onsets
```

Each stage fed Python's own upstream (activations and PCM for the ticks, Python's onset function
for the peak picking):

- **Ticks:** every tick's time, evidence, energy and level is bit-equal to Python's on both 12 s
  excerpts (240 ticks, the last at the frame one past the stream's end, which `Analyzer::finish`
  takes at the last frame's belief), and the 30 s Galop excerpt's 600 tick evidences too.
- **Onsets:** picked from Python's onset function, every onset's time, knownAt, strength and strong
  flag is bit-equal (98 and 134 onsets).
- **The onset function** from Rust's spectra is within 0.011 of its tolerance, where each frame
  takes the summed relative tolerance of the two magnitudes in every positive difference it adds
  (a sum of differences of near-equal magnitudes inherits their float32 rounding; on house-128
  frame 895 the error is 1.6 times the value's own relative tolerance). Python's spectra aren't in
  the golden data, so the operands are Rust's, within A1's tolerance of Python's. That budget is
  wide where terms nearly cancel, so the test also bounds the plain relative error (under 3; 1.6
  today), and a unit test pins superflux to Python's bits on synthetic spectra (madmom's maximum
  filter, differences and float32 sum, edge bins included).
- **The whole analyzer from PCM** (`Analyzer::push` in blocks, then `finish`): the committed beats,
  energies, levels, onset times, knownAt and strong flags are Python's to the bit; the evidence,
  reading Rust's activations, is within 0.03 of its tolerance and the strengths within the onset
  function's. The analyzer's output is identical fed whole or in blocks of 1, 333, 441 or 4096
  samples.

Matching to the bit took numpy's and scipy's exact arithmetic, each checked against the library:

- numpy reduces a long array in buffered chunks of 8192 elements, summing each pairwise and adding
  the chunks in order (the energy's 22050-sample mean of squares; checked against np.sum at chunk
  boundaries, float64 and float32);
- scipy's `uniform_filter` keeps a float64 running sum stepped by new minus oldest, divided by the
  size and cast to float32 (a unit test pins it to scipy's bits on values with 1e9 and 1e20 spikes,
  whose rounding stays in the sum; random values alone can't tell the variants apart);
- numpy's linear percentile interpolates with `_lerp`, from the upper value when the weight is at
  least a half (checked against np.percentile on 20000 random lists; a unit test pins the strong
  flags of 152 onsets to Python's, with one onset exactly 30 s after another, inside its window);
- madmom's `combine_events` widens its distance by 1e-12, so onsets exactly 30 ms apart combine.

Not covered: the tick keys (structure, A5), bands and brightness (A7), and parity beyond the golden
excerpts (A7's corpus parity).
