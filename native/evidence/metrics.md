# Budgets

The native app epic's check budgets ([plan](../../docs/design/native-app.md#introspection)).
`scripts/friction` reads this table: a row whose Key is a backticked friction-record field path
is a budget, and the same field over 1.5 times its budget in 3 consecutive records fires a
trigger. A retrospective may revise a budget; record the reason under Revisions.

| Budget | Key | Seconds | Baseline | Measured by |
|---|---|---|---|---|
| Fast local check (`scripts/check-fast`) | `localValidationS.fast` | 60 | 25 s on a `src/sim` diff, 3 s on a leaf file, 27–29 s when the toolchain's configuration changes (W1) | each bead's record |
| Gate stage (`scripts/ci-gate`) | `gateS` | 60 | 44 s TS section, 59 s TS and analysis, 1 s docs or native only (W1); before W1 the gate was the full suite, 276–397 s. Native section (`native/scripts/check-fast`, persistent target dir): 2.9 s cold, 0.8 s warm on the scaffold's empty crates (F4) | each bead's record, checked against Tollgate's history at retrospectives |
| Release stage (`scripts/ci-full`) | `releaseS` | 1800 | 293–325 s: gate step 44, `ci-full-ts` 241, `ci-full-analysis` 40 (W1); `ci-full-native` 3.2 s cold, 0.7 s warm on the scaffold (F4) | Tollgate's release runs (`tg release status --json`), at retrospectives |
| Cold worktree build | `coldBuildS` | 60 | 28–34 s, cold `cargo build --release -p spike` in a fresh worktree (S0, the spike's wgpu and objc2 tree; the F4 scaffold builds in under 2 s). A1 (realfft, serde): cold `cargo test --workspace --no-run` 8.7 s, cold release build 6.0 s ([a1/](a1/README.md)); re-baseline as render and audio add wgpu and objc2 | beads that build native crates in a fresh worktree |

Per-crate focused tests (`cargo test -p <crate>`) get a row each, keyed
`localValidationS.test-<crate>`, as F4 and the crates' beads add them.

## Test runner (F4)

`cargo nextest run` against `cargo test`, the whole workspace, warm, persistent target dir:

| Run | `cargo test --workspace` | `cargo nextest run --workspace` |
|---|---|---|
| F4 scaffold (no tests yet), three runs | 0.2, 0.2, 0.2 s | 0.2, 0.1, 0.1 s |
| A1 (the analysis crate's 7 tests), three runs | 0.31, 0.32, 0.31 s | 0.26, 0.25, 0.25 s |

No measurable difference with no tests, so every stage keeps `cargo test`. R1 ran before any
crate had tests, so A1 and B1 re-time both (and A1 re-baselines the cold build and the gate
target directory's contention), and R2 adopts nextest for any stage it speeds up by at least 10%.

## Gate target directory (F4)

Two gate slots (Tollgate's default repository concurrency) share one persistent
`CARGO_TARGET_DIR`. Cargo's build lock keeps that correct, but a second run waits for the first,
and each slot's worktree path leaves its own copy of the workspace crates, with nothing pruning
the directory. Watch the gate's native time as the crates grow: key the directory by slot, or
prune it, if the wait shows.

## Review churn (hv-t8o8.46)

Review findings cost 20, 20 and 15 minutes in ES, F3 and F4 (mean 18), about 8 findings per
reviewed bead. The self-review step in AGENTS.md targets them; the next retrospective compares
the `review-churn` minutes and the `review.findings` counts of the next 5 reviewed beads (records
written after the review, hv-t8o8.54) with that mean, aiming at half. The baseline's records carry
`review`: ES 6 findings, F3 10, F4 9, F2 11 (mean 9).

The step cut the counts a little in phase A (A1 8 findings and 20 min, A2 5 and 10, A3 5 and 15:
mean 6 and 15), but the review-churn trigger fired on those three: nearly every finding asked for
a parity test tied more tightly to the upstream, not a fix. hv-t8o8.56 adds a parity-bead
checklist to the step; the baseline for the next 3 reviewed parity beads is A1–A3's mean (6
findings, 15 min), aiming at half.

## The analysis crate's gate tests (hv-t8o8.59)

`native/scripts/check-fast` with the analysis crate changed (warm, a recompile of the crate):
33.6 and 35.3 s before, when every stage parity test ran in the gate (A4's inline hindsight
decodes, about 30 s); 2.8 and 3.1 s after the tests that decode hindsight windows moved to the
release stage (`#[ignore = "release: ..."]`, run by `scripts/ci-full native` with
`--include-ignored`), with a 4 s smoke test of the same path kept in the gate. The release stage's
analysis tests take 22 s (A4's from 26 to 13 s, its inline analyses cached once per binary).

## Batch jobs below Tollgate (hv-t8o8.55)

A gate (`scripts/ci-gate`, every section) took 43–44 s idle, and 64–67 s with a 10-job oracle
export running at normal priority. With the export under `nice -n 10` it took 64–68 s (no
change); under `taskpolicy -b` (background QoS) 43 s, as idle: 33% faster, so long batch jobs
run in the background class (`scripts/lib/batch.ts`, `ps_analysis/batch.py`). Raw numbers:
[batch-priority/](batch-priority/gate-under-export.txt).

## Skipping an unchanged TS section (hv-t8o8.51)

`scripts/ci-full ts` skips when a commit with the same TS inputs (every TS path's blob at HEAD,
node and npm) passed it before, keyed by their hash under
`~/Library/Caches/playlist-survivors/ci-stamps/`. Measured on the change's own commit: the first
run 414 s (host load about 20), the second 0.2 s. Release history before it: `ci-full-ts`
235–375 s on every run, 4 of 9 with no TS change. `PS_CI_FULL_FORCE=1` runs it regardless.

## Revisions

- 2026-10-24, hv-t8o8.59: the analysis crate's slow stage parity tests run in the release stage only.

- 2026-10-24, W2: first budgets, from W1's and S0's measurements.
