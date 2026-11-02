# The analyzer's models and golden data

Copied unchanged from the frozen `native/models/` (docs/champions/plan.md, What Carries Over).
They were written by the frozen analysis tool's `ps-analysis export-models`
(`tools/analysis/src/ps_analysis/models.py`, docs/archive/design/native-app.md "Models"); never
edit them by hand. madmom and the stream's parameters are frozen with that tool, so these don't
change; if they ever must, export again there and copy the files here.

- `madmom.bin`: madmom's online and offline beat ensembles, both DBNs' state spaces and
  transition models (CSR, f64 probabilities), the front ends' filterbanks, windows and
  difference frames, the onsets' parameters, and librosa's chroma, mel and DCT matrices.
  `champions setup` copies it into the Unity project's `Assets/StreamingAssets/ps/`, where a
  player finds it.
- `golden/<excerpt>.bin`: Python's output at each stage on a 12 s excerpt (levels and keys as
  codes into `manifest.json`'s `codes`), for the analysis crate's stage parity tests.
- `golden/orpheus-galop-hindsight.bin`: 30 s of the Galop's hindsight entries (20 s windows
  sliding past 20 s) and committed beats with and without the phase check.
- `manifest.json`: the versions, the stream's parameters and the excerpts.

Format: a little-endian u32 header length, a JSON header `{name: {dtype, shape, offset}}`
(dtype f32, f64 or i32), then the little-endian data.

madmom's models are CC BY-NC-SA 4.0: Rhythm Champions is free and non-commercial, and the plugin
loads them only on the player's own machine.
