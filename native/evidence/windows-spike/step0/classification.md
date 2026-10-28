# Step 0: Windows test failure classification

Source: `cargo-test.txt` (`cargo test -p stream -p analysis -p sim -p eval -p players -p audio --no-fail-fast`, Windows 11, repo at `a8c6a4e`, `core.autocrlf=false`). 17 failures in 5 binaries, two root causes: one path-separator bug (15 tests) and platform libm differences (2 tests). No CRLF problems, no missing fixtures.

## Summary

Totals: **309 passed, 17 failed, 17 ignored**. All 6 doc-test targets ran 0 tests.

| Crate | Test binary | Pass | Fail | Ignored |
|---|---|---|---|---|
| analysis | lib | 17 | **1** | 0 |
| analysis | tests/delayed.rs | 3 | 0 | 1 |
| analysis | tests/stage_parity_a1.rs | 7 | 0 | 0 |
| analysis | tests/stage_parity_a2.rs | 5 | 0 | 0 |
| analysis | tests/stage_parity_a3.rs | 5 | 0 | 2 |
| analysis | tests/stage_parity_a4.rs | 6 | 0 | 8 |
| analysis | tests/stage_parity_a5.rs | 9 | 0 | 1 |
| analysis | tests/stage_parity_a6.rs | 3 | 0 | 1 |
| analysis | tests/stage_parity_a7.rs | 2 | 0 | 1 |
| analysis | tests/stream_causality.rs | 1 | 0 | 2 |
| audio | lib | 18 | 0 | 0 |
| eval | lib | 7 | **1** | 0 |
| eval | tests/live.rs | 21 | 0 | 0 |
| eval | tests/session.rs | 4 | 0 | 0 |
| eval | tests/survive.rs | 0 | **4** | 0 |
| players | lib | 4 | 0 | 0 |
| players | tests/smoke.rs | 2 | 0 | 0 |
| sim | lib | 103 | **10** | 0 |
| sim | tests/causality.rs | 6 | 0 | 1 |
| sim | tests/content.rs | 5 | 0 | 0 |
| sim | tests/core.rs | 6 | 0 | 0 |
| sim | tests/jsmath.rs | 2 | **1** | 0 |
| sim | tests/live_run.rs | 2 | 0 | 0 |
| sim | tests/oracle_prefix.rs | 5 | 0 | 0 |
| sim | tests/schedule.rs | 8 | 0 | 0 |
| sim | tests/sim_modules.rs | 9 | 0 | 0 |
| sim | tests/survivor_offers.rs | 6 | 0 | 0 |
| sim | tests/survivor_spawning.rs | 10 | 0 | 0 |
| sim | tests/survivor_stats.rs | 5 | 0 | 0 |
| sim | tests/survivor_weapons.rs | 3 | 0 | 0 |
| sim | tests/survivors.rs | 19 | 0 | 0 |
| stream | lib | 2 | 0 | 0 |
| stream | tests/stream.rs | 4 | 0 | 0 |
| all 6 crates | doc-tests | 0 | 0 | 0 |

Ignored tests are the existing `release:`-gated ones, not Windows skips.

## Failures

### A. Path separators: `offers.yaml` detected with `rsplit('/')` (15 tests)

| Test | Classification | Evidence |
|---|---|---|
| eval lib `scale::tests::density_setup_and_cached_view_survive_late_input_with_all_gems` | path separators | Panics at `eval/src/scale.rs:67:56`: `ContentError { file: "C:\\...\\crates\\eval\\../../..\\content\\offers.yaml", path: "offers", message: "unknown field \"offers\"" }` (plus `fallback`). |
| eval tests/survive.rs: `a_fast_run_replays_the_rust_fixture_exactly`, `all_survivor_bots_replay_their_real_keys_and_only_see_known_input`, `positioning_survives_the_three_minute_rust_fixture_smoke`, `future_stream_entries_cannot_change_a_driven_prefix` | path separators | All panic at `eval/tests/survive.rs:33:36` with the same `ContentError`. |
| sim lib `engine::spawning::tests::` `big_four_spawn_on_their_minutes_...`, `boss_showpiece_warns_two_bars_...`, `deferred_boss_death_keeps_identity_...`, `partial_boss_admission_keeps_notice_...` | path separators | All panic at `sim/src/engine/spawning.rs:158:86` with the same `ContentError`. |
| sim lib `engine::survivor_events::tests::` `beatless_and_one_beat_swarm_...`, `event_snapshot_and_pause_keep_...`, `jump_waits_for_its_knowledge_...`, `no_jump_falls_back_in_the_last_ten_seconds_...`, `source_roles_keep_fixed_crossing_heading_...`, `zero_chance_wall_is_unconditional_...` | path separators | All panic at `sim/src/engine/survivor_events.rs:388:86` with the same `ContentError`. |

Cause: these tests build `SourceFile.name` from a real OS path (`CARGO_MANIFEST_DIR.join("../../..")` then `read_dir`), which on Windows has `\` separators. `sim/src/content/load.rs:102` checks `f.name.rsplit('/').next() == Some("offers.yaml")`, so the last component comes out as `..\content\offers.yaml`, the offers special case is skipped, and the file goes to the generic content schema, which rejects its top-level `offers`/`fallback` keys.

Checks:
- `content/offers.yaml` is tracked and LF (`git ls-files --eol`: `i/lf w/lf`), so this isn't line endings or a missing file.
- `sim/tests/content.rs` passes because it names files `format!("content/{n}")` (forward slash).
- Reproduced with `cargo test -p eval --lib density_setup`.
- Same pattern, not hit by any failing test: `sim/src/content/level.rs:294` `level_name` (`file.rsplit('/')`). It would give a wrong level name for a backslash path, but these tests pass `"survivors.yaml"` or fail before reaching it.

### B. Float/libm: Windows CRT vs macOS/V8 math (2 tests)

| Test | Classification | Evidence |
|---|---|---|
| sim tests/jsmath.rs `jsmath_matches_the_extra_v8_vectors` | float/libm (`pow`) | `jsmath.rs:90:5`: `mismatches: [("pow", (9000, 5))]`. 5 of 9000 `pow` vectors are 1 ulp off, e.g. first `pow[1.904256034642458, -7.088781014084816] = 3f854d4915c300a7, V8 3f854d4915c300a8`. Also `pow[0.0016103033073483902, -1.0]`: `...69e5` vs V8 `...69e6`. `jsmath::pow` (`sim/src/jsmath/mod.rs:144`) falls through to `x.powf(y)`, the platform CRT `pow`. It is the only jsmath function that uses platform libm. Every other function, and all 17 functions in `oracle/math-vectors.jsonl.gz`, match bit for bit. |
| analysis lib `pinned::the_phase_arithmetic_is_numpys` | float/libm (`sin`/`cos`/`exp`) | `pinned.rs:194:5`: `mean40` left `[4576971188302315696, 4571225358172099744]`, right `[..., 4571225358172099751]`. Real part matches. Imag part is 0.003968471400814938 vs numpy 0.0039684714008149441 (7 ulps). `hindsight::unit` (`hindsight.rs:633-634`) uses std `exp`/`cos`/`sin`, i.e. the platform CRT. The 40 points `unit(i*0.37, period)` aren't individually pinned, and a 1-ulp `sin` difference (~1e-16) in one term, averaged over 40 and cancelling to ~4e-3, accounts for about 6-7 ulps of the result. The earlier per-point `units` and `means` asserts in the same test pass. Reproduced with `cargo test -p analysis --lib the_phase_arithmetic`. |

## Not observed

- CRLF/line endings: none. Content files are `i/lf w/lf` and `core.autocrlf=false`.
- Missing, gitignored or Mac-generated fixtures: none. Every fixture-reading binary found its files, including `oracle/math-vectors.jsonl.gz` and `tests/data/math-vectors-extra.jsonl.gz`.
