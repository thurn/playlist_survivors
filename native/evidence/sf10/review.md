# Milestone F cold warden review

Reviewer: fresh same-host read-only subagent `/root/sf10_warden`, using the warden skill, no inherited author conversation. Scope: the ten integrated F commits in `milestone-f-commits.json`, SF10 source `174294de64e04c629eff21c541beb4b24205410e` and its working diff. Interleaved SH6/SH3/tooling commits were context. The checklist was recorded in the native bead before requesting review. No source was edited by the reviewer and no review processes remain.

## Findings and disposition

1. **P1: beatless gem pickup can queue NaN and stall the clock. Confirmed and fixed.** `collect_gems` called the old extrapolating sixteenth helper with fewer than two committed beats. The reviewer's independent audible/no-beat input reached only run80.5875 after2200 song seconds. Survivors now use the committed grid when available and a finite free-pulse sixteenth otherwise. Vacuum cascades, chicken healing and victory gem flights share finite fallbacks. Legacy arithmetic is unchanged. Regression checks cover zero/one beat, ordinary/vacuum pickup, healing and no NaN queue times.
2. **P1: no-grid victory can wait indefinitely. Confirmed and fixed.** The finale previously returned until a downbeat appeared. Once duration is reached and the last known beat is older than its period with no future committed grid, victory commits the first stand-in pulse and finite raw blast bands. This avoids replacing a healthy grid in its normal commit window. A previous test requiring indefinite waiting for an uncommitted future beat was rewritten to compare full versus truncated future data: the free-pulse victory must be identical before that data is known. Zero/one/several-beat regressions also compare frame cuts with one long advance. This fallback is an executor choice: the fixed decision requires a30-minute ending, and the documented free pulse is the grid substitute in beatless music. The reviewer's separate full-length finale reproduction was stopped without a report; that finding came from code tracing, not a claimed measured run.
3. **P2: offline replay retains presentation moments. Confirmed and fixed.** `replay_run` now consumes/discards `view.moments` each frame, as the normal fast driver already did. Prefix tests assert both absent settled-log retention and an empty transient moment list after replay, against the kept-record hash oracle.

Self-check follow-through: a forced high-HP density foe survived the previous fixed1000-damage final band, contrary to its screen-clear contract. Survivors' final pass now makes remaining hits lethal; the legacy finale remains unchanged. A beatless high-HP regression proves the field clears, gem flights stay finite, and the epilogue finishes.

No finding was dismissed. The reviewer found no additional actionable issue in the inspected shared-page, separation-cache, displayed-prefix, identity-retirement or SF9 release paths. No confirmation review is requested, per AGENTS.md.

## Integration follow-through after the review

SH5 added homing missiles while SF10 was in flight. Its real-XP/late-key and
actual pulse/contact tests caught a missile querying a retired target.
`hittable` now rejects an absent ID, matching the prior dead-object result;
existing retargeting proceeds. All 17 live tests passed, with an explicit
retired-target assertion added to the identity regression.

SH8's physical gem branch returned before the shared vacuum check. The
zero/one-beat fixture caught its original far gem remaining forever. Common
vacuum handling is restored after the mode-specific gem collector. The test
keeps empty-field/vacuum, clock/heal and no-NaN assertions and now checks the
actual gem_arrive callback. No pickup values or attraction motion changed.

The density fixture initially maxed passive inventory ranks without applying
their stat deltas. Historical density outputs are corrected as diagnostic
only. The final setup calls the production stat-application method and asserts
its actual finalized effects before and after late-key rollback. Final full
native validation passes, including the unchanged 4 ms p99 bar at actual max
stats. These fixes and fixture checks do not introduce another rollback
algorithm; no confirmation review was requested under the milestone policy.

## Five-weapon scale integration

The final base includes SW1 Bible and SW2 Lightning Ring. The first all-five
max-weapon/applied-passive frame case failed at p99 4.17 ms. A profile showed
Bible swept-capsule queries among the hot paths. Conservative outward-padded
bounds now reject distant candidates before the hit-set and unchanged narrow
contact test. Degenerate, boundary and large-coordinate comparisons against
the real original predicate pass, as do 18 live tests. The old recording replays
exactly after optimization. Full native validation passes p99 3.38 ms with all
five weapons and all six real max-passive effects; no numerical knob, effect,
hitbox or rollback algorithm changed. The age dataset's state/clone code is
unchanged across this stateless optimization, with the exact source patch
and provenance retained under five-weapon-integration.
