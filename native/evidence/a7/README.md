# A7: remaining groups, corpus parity and the stream's causality

Measured by `hv-t8o8.12` on 2026-10-24 on macOS arm64 (Mac17,6).

## Bands, brightness and timbre

`cargo test -p analysis --release --test stage_parity_a7 -- --include-ignored --nocapture`
(native/crates/analysis/tests/stage_parity_a7.rs; fixture from [fixture.py](fixture.py),
Python's `stream.build_stream` on the golden excerpts' PCM):

```text
galop-29997: bands within 8.66e-7, brightness 2.93e-8, timbre 2.98e-8; 0 onset bands differ
concat42: bands within 9.25e-7, brightness 1.21e-8, timbre 2.98e-8; 0 onset bands differ
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

## The stream's own causality

`cargo test -p analysis --release --test stream_causality -- --include-ignored --nocapture`
(native/crates/analysis/tests/stream_causality.rs):

```text
entries known by the last cut: beats 78, ticks 523, onsets 274, hindsight 26, changes 0, jumps 0, ends 0, forecasts 0, timbre 26
sections: 2 changes, 3 jumps, 1 ends; pulse: 16 forecasts
```

The analyzer's stream of audio cut at X holds exactly (bit for bit, in order) the full stream's
entries known by X less 0.05 s, in all nine groups (beats, ticks, onsets, hindsight, changes,
jumps, ends, forecasts, timbre), and with no margin at cuts between frames (4.123457 s, 9.005013
s, 22.5 s, 27.7 s, 33.333331 s), fed in uneven blocks: on the Galop's 30 s at eight cuts, on A5's
synthetic piece at four (its changes, jumps and end) and on A6's accented pulse at three (its
forecasts). The gate runs 6 s of the house excerpt at two cuts.

## Corpus comparison

`ps analysis-parity` (native/crates/cli/src/parity.rs) runs the analyzer over a track's audio,
from Python's own decoded samples (`ps_analysis.pcm`), in 512-sample blocks, twice: as Python runs
it (D = 0) and at the live hindsight delay (D = 0.67 s). It reports, per track and in aggregate,
committed-beat hit rates within 20, 45 and 70 ms of the reference beats (as `stream.py`'s
`accuracy`) for Rust and Python, the beat F-measure within 70 ms against Python's committed beats,
per-group summaries (tick energy, bands, keys, onsets matched within 10 ms, structure events within
0.1 s, forecasts, timbre), real-time factors per group and each group's arrival lag (audio time
from an entry's `knownAt` to its emission). Accuracy is judged statistically (the plan's revision
of 2026-10-24): about as accurate as Python's across the corpus.

On three tracks (house-128, the Galop, cc-kevin-macleod-go-cart; 420 s):

```text
| cc-kevin-macleod-go-cart | 213 | 411/411 | 0.890/0.890 | 0.890 | ...
| house-128 | 62 | 136/136 | 1.000/1.000 | 1.000 | ...
| orpheus-galop | 145 | 358/358 | 0.582/0.582 | 0.576 | ...
| **All** | 420 | | 0.782/0.782 | 0.780 | ...
```

At D = 0 the committed beats are as accurate as Python's on every track (the same counts and
hit rates); the live delay costs 0.6 points on the Galop and none on the others. Every onset is
matched within 10 ms, every key equal, structure counts equal, tick values within stream.json's
rounding (about 2e-5). Across the corpus ([corpus.md](corpus.md)): at D = 0, every one of the 61 tracks' committed
beats match Python's within 70 ms, 60 have exactly Python's hit rate within 45 ms and one is 0.8
points under (notjam-descend-gameplay, 0.960 against 0.968): 0.7708 in aggregate against 0.7710.
On 47 tracks at the live delay 10 tracks lose 0.5 to 3
points (the most: notjam-drag-and-dread-theme and notjam-klepto-linda-caverns-b), two gain a
little, and the rest are unchanged.

The release stage runs `--no-live` (the margins only) on a fixed sample of 8 tracks at utility
QoS when its inputs change: the whole corpus overran the step's 30 minutes on a loaded host.
