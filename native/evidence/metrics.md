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
| Cold worktree build | `coldBuildS` | 60 | 28–34 s, cold `cargo build --release -p spike` in a fresh worktree (S0, the spike's wgpu and objc2 tree; the F4 scaffold builds in under 2 s, so re-baseline once the crates carry their dependencies) | beads that build native crates in a fresh worktree |

Per-crate focused tests (`cargo test -p <crate>`) get a row each, keyed
`localValidationS.test-<crate>`, as F4 and the crates' beads add them.

## Test runner (F4)

`cargo nextest run` against `cargo test`, the whole workspace, warm, persistent target dir:

| Run | `cargo test --workspace` | `cargo nextest run --workspace` |
|---|---|---|
| F4 scaffold (no tests yet), three runs | 0.2, 0.2, 0.2 s | 0.2, 0.1, 0.1 s |

No measurable difference with no tests, so every stage keeps `cargo test`. R1 re-times both
once the first crates have tests (A1, B1) and adopts nextest for any stage it speeds up by at
least 10%.

## Gate target directory (F4)

Two gate slots (Tollgate's default repository concurrency) share one persistent
`CARGO_TARGET_DIR`. Cargo's build lock keeps that correct, but a second run waits for the first,
and each slot's worktree path leaves its own copy of the workspace crates, with nothing pruning
the directory. Watch the gate's native time as the crates grow: key the directory by slot, or
prune it, if the wait shows.

## Review churn (hv-t8o8.46)

Review findings cost 20, 20 and 15 minutes in ES, F3 and F4 (mean 18), about 8 findings per
reviewed bead. The self-review step in AGENTS.md targets them; the next retrospective compares
the `review-churn` minutes of the next 5 reviewed beads with that mean, aiming at half.

## Revisions

- 2026-10-24, W2: first budgets, from W1's and S0's measurements.
