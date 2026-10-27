# Finale cancellation repair

Release01a17e49 at8163363 crashed in `blast_band` during the random-choice
30-minute case; all20 positioning cases passed. Exact retained release output
is release-log.jsonl. The source defect is independent of minimum difficulty.

When a card menu cancels a planned future finale, queued/held blast events were
removed but bands waiting for unknown beat slots remained in `deferred`.
Ingesting those beats could deliver the cancelled blast with no finale plan.
Cancellation now removes those deferred bands and finale landmarks too.

regression-before.txt fails on the actual planned unknown-band ownership;
regression-after.txt covers cancellation, a confirmed menu resume and completion
with a new finale. random-pick.json/stderr retain the exact failing CLI case
rerun on the repair, with ordinary bot behavior and30-minute deadline. No
balance, health or AFK-gate changes. Final fast/gate and cold review recorded
in the bead. Playable: `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed` from native/ (also `--legacy`).

Observed repair result: ordinary random-pick seed1 completed1800.0 run seconds,
minimum HP .969859,249.61 wall seconds. Final check-fast passed79s on this
cold worktree; cold warden had no actionable findings.
