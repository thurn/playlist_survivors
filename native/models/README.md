# Exported models and golden data

Written by `npm run analysis -- export-models` (tools/analysis/src/ps_analysis/models.py,
docs/design/native-app.md "Models"); never edit by hand. `tools/analysis/tests/test_models.py`
fails when these differ from a fresh export, so regenerate them whenever the stream's
parameters or madmom change.

- `madmom.bin`: madmom's online and offline beat ensembles, both DBNs' state spaces and
  transition models (CSR, f64 probabilities), the front ends' filterbanks, windows and
  difference frames, the onsets' parameters, and librosa's chroma, mel and DCT matrices.
- `golden/<excerpt>.bin`: Python's output at each stage on a 12 s excerpt (levels and keys as
  codes into `manifest.json`'s `codes`), for stage parity.
- `golden/orpheus-galop-hindsight.bin`: 30 s of the Galop's hindsight entries (20 s windows
  sliding past 20 s) and committed beats with and without the phase check, for A4. Its test
  checks its contents, not a re-export (30 decodes); rerun the export when the manifest's
  versions or parameters change.
- A5–A7 (structure changes, keys past 10 s, accent forecasts, bands, timbre) need longer
  excerpts than these; their beads add golden data as they need it.
- `manifest.json`: the versions, the stream's parameters and the excerpts.

Format: a little-endian u32 header length, a JSON header `{name: {dtype, shape, offset}}`
(dtype f32, f64 or i32), then the little-endian data.

madmom's models are CC BY-NC-SA 4.0: the app runs only on the developer's machine.
