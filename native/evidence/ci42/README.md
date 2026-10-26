# CI42 — native density release repair

Release `01a17a3d-bc74-7701-93dc-5ec4eb8374d8` at `411488ca` failed
`ci-full-native`: density frame p99 **4.89 ms**, versus the unchanged **4 ms**
bar. The following two release steps were skipped. `release-native-failure.jsonl`
keeps the native log. A local baseline reproduced **4.75 ms**.

The final production change is only the due-event queue shortcut. `pop_due`
previously drained every simultaneous event and reinserted all but one, for
every selection. A scan now proves when the heap has already selected the
best due kind/sequence; only a better key inside the time tolerance invokes
the unchanged full-drain fallback. No order, damage, schedule, offer, health,
loadout, listening or benchmark requirement changes.

`source.json` and `runtime.patch` identify the reviewed source. The bead notes
record the delivered SHA and candidate after staging.

## Focused evidence

- `density-queue.txt`: all five rank-8 base weapons, six applied max passives,
  300 live foes and 400 simulation gems; 7,080 verified frames of 7,200,
  1080p/120 Hz, lead 0.2 and 5–35 ms late keys. Frame p99 **2.87 ms**,
  engine 2.277, scene 0.598, encode 0.444 ms p99; 1,776 rollbacks. Component
  quantiles do not add up to the combined quantile.
- `queue-tests.txt`: **8,575 selections** match the old full-drain algorithm,
  including exact timestamps, ±0.5 ps, mixed kinds/phases and events inserted
  during dispatch.
- `replay-comparison.json`: actual house stream through the production density
  Engine for 30 s, nondefault lead 0.23, keys25 ms late. Baseline and optimized
  runs produce the same **609,236 records**, hash **b7af20589d509af1**, 897
  rollbacks, 300 live foes and 400 gems. This is a scoped comparison with the
  committed Rust source, not a frozen gameplay oracle. `replay-probe.rs` is
  the diagnostic copied temporarily into cli/examples and removed afterwards.

## Commands

From the worktree's `native/`:

```sh
cargo test -p sim due_queue -- --nocapture
cargo build --release -p app
target/release/playlist-survivors --bench-density --check
```

`replay-probe.rs` was built as cli's temporary `ci42_probe` example, once
with the committed module from 411488ca and once with the final module. The
source and output hashes are kept. The probe's wall times are diagnostic,
not a controlled speedup claim. It uses real committed stream input and all
implemented base weapons and applied passives.

From the repository: `scripts/ci-full native`, then `scripts/check-fast`.
Routine play check: from native, `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first
--seconds 30`. No window or listening/feel claim.

## Investigation history (not final acceptance)

The failed intermediate measurements and two sampling profiles are kept.
Position caching, broad-phase indexing, pool hints, shared impact metadata,
inlining and a contact deadline did not resolve the p99 failure and were
removed. `intermediate-optimization.patch` also contains temporary phase
instrumentation; none remains in production. `phase-profile.txt` aggregates
step phases; `frame-phase-profile.txt` localizes slow frames to event dispatch.
Those instrumented runs add diagnostic overhead and are not acceptance runs.
`density-unchanged-repeat.txt` is an accidental unchanged-source repeat after
a local edit path typo; it is not represented as evidence for another change.
The final fix follows the measured event-queue bottleneck, not those attempts.

## Headless play

`headless-play-result.json` and `headless-play.txt` keep the exact command and
exit 0: 30 app seconds, 24 run seconds, normal level-up/card selection and
resume, 106 sounds started, 0 skipped late, 0 dropped, 2 speculative cancellations.
Session 1793054494 is in the main checkout's ignored session log directory.
Headless operation was muted with no window; these counts do not establish
sound quality or feel.

## Final validation and review

`scripts/ci-full native` passed in **497.843 s**, including the workspace's
ignored release tests, eight-track corpus parity, runner check and both
release app benchmarks. The final density frame p99 is **2.66 ms** (engine
2.147, scene 0.609, encode 0.393 ms p99); max 5.19 ms, 1,776 rollbacks. These are
quantiles of the unchanged workload, not a maximum-frame guarantee.
`density-full-native.txt` extracts the final output; the entire run and status
are kept in `ci-full-native.txt` and `ci-full-native-result.json`.

One fresh read-only warden review of the due-event ordering change found no
actionable issues (`warden-review.txt`); no confirmation review was run.
The final fast check and Tollgate gate certify the submitted integration;
their outcomes and full commit SHA are recorded in the native bead notes.
