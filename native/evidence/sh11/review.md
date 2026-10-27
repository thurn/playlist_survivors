# Cold H milestone Warden review

Fresh same-host read-only agent `/root/sh11_warden`, no inherited author
conversation, used `/Users/dthurn/hive/skills/warden/SKILL.md`. Scope: the ten
indexed H integrated commits/current integration, with SF10/CI42/W1–W3 as
superseding context, and SH11's actual-recording capture. No files edited,
beads changed or app window opened. Required author checklist preceded review
in native bead notes and `self-review.md`. One review, no confirmation review.

## Finding and disposition

**P2 — refresh displayed cards after rollback preserves reveal times.**
`native/crates/app/src/slice/cards.rs`, original `same`/`Screen::update`:
offer identity compared only level/reveals; cached offer/inventory were not
refreshed. A late highlight changing an earlier chained pick can change the
next offer's eligible items/ranks while preserving reveal times. The display
then disagreed with the sim-confirmed grant and showed stale pair badges.

Confirmed against the code. Fixed by refreshing `Active.offer` and
`Active.inv` on every live offer update while preserving the reveal animation
identity, so a correction does not fabricate a confirm or restart the screen.
`rollback_replaces_cards_and_pair_inventory_at_the_same_reveal_times` uses the
real offer table and two eligible inventories at identical reveal times,
checks the corrected cards and inventory, unchanged animation start, and no
closing/confirm cue. It passes; all seven card-screen tests and focused
clippy pass (`review-cards-test.txt`, `review-cards-suite.txt`,
`clippy-reviewed.txt`). Game rules, offer table and stat numbers are unchanged.
The reviewer did not review the fix or run a confirmation review.

No additional actionable findings. SF10 beatless pickup/finale and retired
target repairs, and CI42 queue optimization, supersede earlier defects.
Independent reviewer command `cargo test --release -p sim --lib`: 87 pass,
0.65s tests / 7.01s build, reported by reviewer. Source contracts, freeze,
held keys, music/chests, sound cancellation/budgets, telemetry and
representative stills inspected.

## Limits retained

First-five-minute results do not establish final full-run survival or evolved
performance. The selected normal capture has no roulette; SH10 has its own
kill/touch/roulette proof. Musical fit, sound quality and human feel are
operator-pending. Pacing variation is report-only, with no retune outside SW7.
