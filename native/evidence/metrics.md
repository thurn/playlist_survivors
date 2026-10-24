# Budgets

The native app epic's check budgets ([plan](../../docs/design/native-app.md#introspection)).
`scripts/friction` reads this table: a row whose Key is a backticked friction-record field path
is a budget, and the same field over 1.5 times its budget in 3 consecutive records fires a
trigger. A retrospective may revise a budget; record the reason under Revisions.

| Budget | Key | Seconds | Baseline | Measured by |
|---|---|---|---|---|
| Fast local check (`scripts/check-fast`) | `localValidationS.fast` | 60 | 25 s on a `src/sim` diff, 3 s on a leaf file, 27–29 s when the toolchain's configuration changes (W1) | each bead's record |
| Gate stage (`scripts/ci-gate`) | `gateS` | 60 | 44 s TS section, 59 s TS and analysis, 1 s docs or native only (W1); before W1 the gate was the full suite, 276–397 s | each bead's record, checked against Tollgate's history at retrospectives |
| Release stage (`scripts/ci-full`) | `releaseS` | 1800 | 293–325 s: gate step 44, `ci-full-ts` 241, `ci-full-analysis` 40 (W1) | Tollgate's release runs (`tg release status --json`), at retrospectives |
| Cold worktree build | `coldBuildS` | 60 | 28–34 s, cold `cargo build --release -p spike` in a fresh worktree (S0) | beads that build native crates in a fresh worktree |

Per-crate focused tests (`cargo test -p <crate>`) get a row each, keyed
`localValidationS.test-<crate>`, as F4 and the crates' beads add them.

## Revisions

- 2026-10-24, W2: first budgets, from W1's and S0's measurements.
