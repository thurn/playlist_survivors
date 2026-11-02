# ps-6y2.2: the music plugin and music

Measured on jarvis (M5 Max), 2026-11-02, with other beads' sessions running.

- `rhythm_champions/plugin` (the five crates copied from the frozen `native/`): `cargo fmt --check`,
  `cargo clippy --workspace --all-targets -- -D warnings` and `cargo test --workspace` pass; the
  tests take 27 s warm (the analysis crate's stage parity and causality tests dominate). Its four
  ignored release tests pass too (`cargo test --release -p analysis -- --ignored`, 37 s of tests);
  the fifth ignored test taps a real app.
- [play-house-128-20s.log](play-house-128-20s.log): `champions play --file
  public/reactive/house-128/house-128.ogg --headless --seconds 20`, its `Boot:` and `Music:` lines.
  The music plays 0.22 s after the session opens; the analyzer reads 2.96 to 2.98 s ahead of the
  speakers (M = 3 s less a block in flight), 2.976 s on average; 43 beats, each known 0.10 to
  1.31 s (median 0.78 s) before it is heard, none late; no stalls, no late entries, no held frames.
  The player exits 0 after 22.8 s.
- The same file under `--fixed-time --seconds 10`, driven by the frames: 10 s of song in 3.1 s,
  22 beats each known 0.25 to 1.31 s ahead, 0 late entries.
- `--app NoSuchApp`: after waiting 3 s for it, the player logs the apps producing audio now and
  exits 2.
- EditMode tests: 22 of 22 in 11 s, 15 of them the music's (bindings' layouts, apps and devices,
  the editor's reloadable copy, a File session committing beats ahead, a driven session analysed
  up to M past its render position, the clocks, and a fixed-time run of house-128's entries that
  knows each beat 0.25 s or more ahead whatever the entries' delivery).
- The variant budget is unchanged (no shaders added).
