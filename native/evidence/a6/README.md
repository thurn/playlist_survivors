# A6: accent forecasts

Measured by `hv-t8o8.11` on 2026-10-24 on macOS arm64 (Mac17,6; `cargo test -p analysis
--release --test stage_parity_a6 -- --include-ignored --nocapture`,
native/crates/analysis/tests/stage_parity_a6.rs; fixture from [fixture.py](fixture.py)).

```text
every-beat: 58 forecasts bit-equal
fourth-with-gap: 20 forecasts bit-equal
late-onsets: 42 forecasts bit-equal
window-edges: 29 forecasts bit-equal
62 streams: 75 forecasts on 24 of them, bit-equal
accented pulse: 17 forecasts bit-equal
```

- **Given Python's upstream:** every stream under `public/reactive` (62), its stored beats and
  onsets fed to the Rust stage a frame (`knownAt`) at a time, gives exactly the forecasts Python's
  `accents.forecasts` computes from the same lists all at once (75 forecasts on 24 streams), each
  taken at its own `knownAt`, never later.
- **The rule's edges**, on synthetic beats and onsets: all three cycles completing together (the
  tie-break: earliest known, then most confident, then shortest cycle), a missing hit breaking
  the runs, strong onsets exactly at the 50 ms window (counted) and 1 ms past it (not), weak
  onsets (never counted), and onsets known 0.3 s late, so they stamp the forecasts and the 2-beat
  cycle misses the 0.54 s least lead while the 4-beat cycle forecasts.
- **The whole analyzer** from PCM (release stage): a 40 s pulse with every 4th beat accented
  gives Python's `build_stream` forecasts to the bit (17), since its phase-checked beats and
  onsets already are (A3, A4). The Galop's 30 s golden excerpt has no forecast, so it isn't used.
- **Streaming:** Python stamps a forecast with the latest `knownAt` it read, so it becomes
  computable exactly when its last input arrives; each beat or strong onset marks the target
  beats it could complete, and the stage decides them once a frame's inputs are in. Memory is the
  last 41 beats (the 8-beat cycle's 4-cycle history) and a second of strong onsets.
- `accents.py` makes no library call (comparisons, `min`, `max` and a count over 4), so there is
  nothing to probe (AGENTS.md's inventory step).

Not covered: a forecast on real music through the analyzer from PCM (A7's corpus parity runs
every track).
