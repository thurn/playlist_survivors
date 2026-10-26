# SH1 cold warden review

Fresh read-only same-host warden reviewed source tree `400b23ce24ddce8bb4d42d17627c9b7568422f6a` against `089c08f5f3cc71b70a6f5724c8e7227fffb6d68c`. Required because compatibility hash encoding is part of the rollback runner. Self-review checklist recorded in native bead notes before dispatch. No confirmation review.

Both P2 findings confirmed and fixed:

1. Watch my run supplied the live beat deque, whose old beats were discarded. It now retains beats from the replay engine’s recorded, rebased stream as arrivals are fed. A regression exercises real `session::rebase_append`, late/pre-run beat rejection and repeated updates without duplication.
2. Labels were presented on logical Hit before grid-aligned EnemyImpact. They now present exactly once from EnemyImpact, so a level-up freeze cannot expire the number before the reaction. A regression sends a logical hit, waits five seconds, then sends its impact and verifies the label’s birth/value.

Reviewer found no additional actionable atlas-binding, sampler, bitmap-placement or compatibility-encoding issue. Critical gameplay flag remains the explicitly documented SW5 handoff.
