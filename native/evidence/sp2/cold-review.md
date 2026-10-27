# Cold review — SP2

One fresh read-only same-host warden (`/root/sp2_warden`), base
`d68e2be149e50505373809c761404c7b86b406e3`. Required because the live
recorder/projection and rollback state change. No confirmation review.

1. P2: `boss_bar` ignored actual earlier committed downbeats and projected
   backward from the latest known beat. The reviewer reproduced a 130 ms
   early prediction on the kept SW7 stream. Confirmed. It now selects the
   first eligible known even-index timestamp, extrapolating only beyond the
   known prefix. Warning starts use the same actual bar selection. The
   irregular-grid regression uses real nonuniform timestamps, not a second
   copy of the algorithm.
2. P2: a deferred lethal `EnemyImpact` looked up the boss after pool retirement,
   losing the death cue/sound. Confirmed; the regenerated real-kill fixture
   also reproduced the missing cue. The actual damage path now captures
   the boss flag in the queued/held impact. A regression kills a marked boss,
   holds the actual impact through pause, trims its real pool entry, resumes
   and verifies one defeated cue from the original identity.

The review also found an existing integration assertion expecting an eighth
at 0.25 s. Updated it to the intentionally changed native downbeat at 1.0 s.
No source boss stats or attack logic changed. `review-fixes.txt` retains
focused verification, `live-tests.txt` checks all actual Engine regressions.

Before review: downstream SP3/SP4/SP5 interfaces reread; kept claims/timing
scoped to synthetic/empty-field or short normal smoke; unknown-tail, freezes,
partial cap admission and sample-time determinism checked. No new worker or
channel lifecycle. Human feel/audio and evolved density stay explicitly in
operator-pending/SP5. The source clock, actual fates and actual decoded
samples underpin tests and captures.
