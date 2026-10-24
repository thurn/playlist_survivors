# A5: structure (changes, jumps, keys, ends)

Measured by `hv-t8o8.10` on 2026-10-24 on macOS arm64 (Mac17,6, Apple M5 Max, 18 cores; `cargo
test -p analysis --release --test stage_parity_a5 -- --include-ignored --nocapture
--test-threads=1`, native/crates/analysis/tests/stage_parity_a5.rs). Bit-equalities hold on this
platform (numpy's `cov` goes through Accelerate's BLAS here).

```text
test features_match_python ... house-128: 240 ticks, features within 3.88e-6 of their tolerance, 2189 of 8880 values bit-equal; chroma within 1.3e-15 relative, MFCCs within 5.0e-12 (1 + |value|)
orpheus-galop: 240 ticks, features within 5.65e-6 of their tolerance, 2128 of 8880 values bit-equal; chroma within 9.7e-16 relative, MFCCs within 4.0e-12 (1 + |value|)
test keys_from_pythons_features_match_to_the_bit ... ok
test keys_match_python ... ok
test params_and_keys_are_the_streams ... ok
test structure_matches_python_on_a_cut_piece ... synthetic29997: 2 changes, 0 bit-equal
synthetic29997: 2 jumps, 2 bit-equal
synthetic29997: 1 ends, 1 bit-equal
test structure_matches_python_on_a_synthetic_piece ... synthetic: 2 changes, 0 bit-equal
synthetic: 3 jumps, 3 bit-equal
synthetic: 1 ends, 1 bit-equal
test structure_matches_python_on_the_galop ... galop30: 0 changes, 0 bit-equal
galop30: 0 jumps, 0 bit-equal
galop30: 0 ends, 0 bit-equal
test structure_matches_python_past_every_window_it_keeps ... long: 8 changes, 0 bit-equal
long: 5 jumps, 5 bit-equal
long: 1 ends, 1 bit-equal
test the_analyzer_from_pcm_matches_python ... synthetic: 2 changes, 0 bit-equal
synthetic: 3 jumps, 3 bit-equal
synthetic: 1 ends, 1 bit-equal
synthetic29997: 2 changes, 0 bit-equal
synthetic29997: 2 jumps, 2 bit-equal
synthetic29997: 1 ends, 1 bit-equal
long: 8 changes, 0 bit-equal
long: 5 jumps, 5 bit-equal
long: 1 ends, 1 bit-equal
galop30: 0 changes, 0 bit-equal
galop30: 0 jumps, 0 bit-equal
galop30: 0 ends, 0 bit-equal
test ticks_from_heard_samples_match_ticks_from_frames ... synthetic from heard samples: 2 changes, 0 bit-equal
synthetic from heard samples: 3 jumps, 3 bit-equal
synthetic from heard samples: 1 ends, 1 bit-equal
test result: ok. 10 passed; 0 failed; 0 ignored; 0 measured; 0 filtered out; finished in 58.05s
```

- **Features** (`structure::StructureFeatures`), per 50 ms tick from the 4096 samples before it:
  the loudness in dB bit-equal to Python's on both golden excerpts; the chroma, MFCCs 1-12 and
  loudness / 10 within about 6e-6 of the plan's tolerance, and within a bound derived from the
  error's source, `1e-10 (1 + |value|)` (measured above: chroma within 1.3e-15 relative, MFCCs
  within 5e-12 (1 + |value|)). A quarter of the values are bit-equal; the rest differ in the last
  bits because librosa applies its filterbanks with BLAS (`einsum(optimize=True)`) and its DCT
  with an FFT (`scipy.fft.dct`), where Rust takes sequential dot products with the same matrices.
- **The matrices** are librosa's own, exported with the models (`librosa.chroma` and
  `librosa.mel` in float32 as librosa builds them, `librosa.dct` as scipy's orthonormal DCT-II of
  the identity, in `madmom.bin`), not recomputed from their definitions: nothing to keep in step
  with librosa's filterbank code, and the features differ only by summation order.
- **Keys:** fed Python's own chroma (the golden `structure.chroma`), every tick's key is Python's
  to the bit; from PCM too, on both golden excerpts, all 640 ticks of the synthetic piece (E minor,
  C major, A minor), the 3000 of the long piece and the Galop's 600 (D major). The correlation is
  numpy's `corrcoef` exactly: its `cov` takes a BLAS dot product, two fused multiply-add
  accumulators over alternate elements (found by experiment, identical on 20000 random cases, and
  pinned in LIBRARY-ARITHMETIC.md); a sequential dot product differs in the last bits in about
  two cases of three, which could flip a key at a near tie.
- **Changes, jumps, ends:** on a synthetic piece built to have every kind (saw chords that change
  at 12 s and 20 s, louder from 20 s and near silence from 26 s: jumps of +6.1, -8.3 and -28.7 dB;
  `tests/fixtures/a5-structure.json`, written by [fixture.py](fixture.py)), the changes' times
  and known-at times are bit-equal and their strengths within tolerance (novelty reads features
  that are not bit-equal); the jumps and the end are bit-equal. The same piece cut to 29.997 s
  (neither a whole number of ticks nor of hops; the last jump falls past the cut) gives Python's 2
  changes, 2 jumps and the end. A 150 s piece (a section every 20 s) runs past every window the
  stage keeps (the novelty ring of about 68 s, the threshold's 60 s prior, 30 s of feature
  statistics, the keys' 20 s): its 8 changes (one weak, strength 0.08, just above its threshold),
  5 jumps and the end are Python's.
- **Real music:** the Galop's 30 s runs the change rule on real music, finding no change, as
  Python does. The 12 s golden excerpts cannot decide a change at all (the last decidable tick is
  120, 6 s before the end, and novelty starts at tick 160), so a change found on real music is
  first compared in A7's corpus parity, where a near tie between a novelty value and its threshold
  would show as a flipped change.
- **Bounded memory:** every window statistic is a difference of running sums from the stream's
  start, as Python's `cumsum`, kept in rings sized by the windows, whatever the song's length.
- **The analyzer's path:** samples pushed in uneven blocks (13 to 1994 samples), each tick's frame
  cut from the stage's ring (`Structure::tick_at`), give what whole frames give (in the gate); the
  whole analyzer from PCM, fed in blocks of 1000, gives the same keys on its ticks and the same
  changes, jumps and ends on all four pieces (release stage).

Library behaviours, now pinned (LIBRARY-ARITHMETIC.md): numpy's `mean(axis=0)` on a C-ordered 2D
array adds each column row by row (`the_axis_mean_is_numpys`); `np.corrcoef`'s BLAS dot product
and `np.median`'s even-length mean (`the_key_correlation_is_numpys`). Not reproduced:
`np.linalg.norm` (a BLAS `ddot`, whose order did not match any simple accumulator pattern tried),
since novelty's inputs are not bit-equal anyway.

Cost: the gate's structure tests take about 0.4 s; the analyzer test is release-only (`#[ignore =
"release: ..."]`, it decodes hindsight windows: about 58 s for the four pieces). A tick's features
are one 4096-point real FFT and about 0.3 M multiply-adds of matrix products.
