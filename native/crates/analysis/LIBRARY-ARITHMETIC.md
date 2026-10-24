# Library arithmetic the analyzer reproduces

The analyzer matches Python's stream to the bit wherever Python's output is discrete, and within
float32 rounding elsewhere. That rests on reproducing what numpy, scipy, madmom and Python do,
which is often not what their documentation says or a natural port would do. Every behaviour
below was found by experiment and is pinned to the library's own bits: the probe
(`tools/analysis/src/ps_analysis/probe.py`) runs the library on inputs that tell plausible
variants apart and writes `tests/fixtures/library-arithmetic.json`; `src/pinned.rs` checks the
Rust against it, and `tools/analysis/tests/test_probe.py` fails when a library upgrade changes an
entry. A new behaviour gets a probe entry, a pinned test and a row here.

All of it holds on macOS arm64 (where the golden data and the fixture are made): numpy calls the
system's math library there, as Rust does. On x86 with AVX512, numpy's own vectorised `log`,
`exp` and friends can differ by an ulp.

| Behaviour | Library | Exactly | Telling input | Rust | Pinned by |
|---|---|---|---|---|---|
| Summation | numpy `np.add.reduce` (`np.sum`, `np.mean`) | Pairwise: a sequential sum under 8 values, eight accumulators per block of up to 128, halved recursively above; long arrays in buffered chunks of 8192 elements, added in order; the mean is the sum over n | Lengths 5–30000 across 128 and 8192 | `numeric::pairwise_sum`, `pairwise_sum_f32` | `sums_and_means_are_numpys` |
| float32 sums | numpy | The same in float32 accumulators (superflux's sum over 1024 bins) | 1024 to 20000 float32 values | `numeric::pairwise_sum_f32` | `sums_and_means_are_numpys` |
| Linear percentile | numpy `np.percentile` | Index `(n - 1) * q / 100`; `_lerp` from the upper value when the weight is at least a half | Lists of 1, 2 and 37 values at 0, 2, 25, 50, 98, 100 | `numeric::percentile_linear` | `percentiles_are_numpys` |
| Strong onsets | `stream.detect_onsets` | At least the 98th percentile of the strengths known in the past 30 s, an onset exactly 30 s back included | An onset 30 s after a strong one | `onsets::Strengths` | `strong_flags_are_detect_onsets` |
| Uniform filter | scipy `ndimage.uniform_filter` (constant) | A float64 running sum stepped by new minus oldest, divided by the size, cast to float32: a spike's rounding stays in the sum | 1e9 and 1e20 spikes | `onsets::RunningMean` | `the_running_mean_is_scipys_uniform_filter` |
| Superflux | madmom `superflux` with scipy `maximum_filter` | A 3-bin maximum filter of the frame `diff_frames` back (`reflect`: the edge bin repeats), positive differences, a float32 sum | Spikes in the edge bins | `onsets::Superflux` | `superflux_is_madmoms` |
| Combining onsets | madmom `utils.combine_events` (`left`) | Drop an event within `delta` of the last kept one, `delta` widened by 1e-12 (so onsets 30 ms apart as frame / 100 combine) | Frames 64 and 67 | `onsets::kept_after` | `combining_onsets_is_madmoms` |
| Complex phase | numpy | `2j * pi * x / period`: complex multiply, then Smith's division by `(period + 0j)` (times the reciprocal); `exp` gives `(cos, sin)`; the mean is a pairwise sum of (re, im) pairs times `1 / n`; `abs` is C `hypot` (not Python's `math.hypot`); `angle` is `atan2` | Means of 3, 7 and 40 | `hindsight::unit`, `complex_mean` | `the_phase_arithmetic_is_numpys` |
| Hann window | numpy `np.hanning` | `0.5 + 0.5 cos(pi n / (m - 1))` for `n = 1 - m, 3 - m, ...`, float64 | `m` = 8 and 2048 | `frontend::hanning` | `hanning_is_numpys` |
| Observation densities | madmom `RNNBeatTrackingObservationModel.log_densities` | `log((1 - a) / (lambda - 1))` and `log(a)` in float32 (numpy on float32 activations), stored as float64; the forward step takes their float64 `exp` | 0, 1e-7, 1 | `dbn::log_densities` | `log_densities_are_madmoms` |
| Rounding | Python `round` | Ties to even | 0.5, 1.5, 2.5, 1199.5 | `f64::round_ties_even` | `rounding_is_pythons` |

Behaviours checked through the golden data rather than the probe (each stage's parity test):

- **madmom's framing:** online frame `n` covers samples `n * 441 - 2047` to `n * 441` (origin
  1023), offline frames are centred on `n * 441`; a signal of `len` samples has `ceil(len / 441)`
  frames; zeros outside it (A1, A4).
- **madmom's STFT:** the float32 frame times the float64 window in float64, a float64 FFT, the
  first `size / 2` bins rounded to complex64, their float32 magnitudes (A1, A4).
- **madmom's LSTM:** a gate is `act((x W + b) + c p + h R)`; the input and forget gates peep at the
  previous cell state, the output gate at the new one; the ensemble is `sum / n` in float32 in
  network order (A1, A4).
- **madmom's HMM:** the forward step sums in pointer order in float64 and scales by `1 / sum`; the
  Viterbi decode keeps the first best predecessor (strict `>`) and the first maximum (A2, A4;
  ties and edges in `tests/fixtures/a4-tiny-dbn.*`).
