# SF3 cold warden review

Fresh read-only Codex subagent `sf3_warden`, no inherited conversation, using
the warden skill. Complete diff against
`e23cff8fbcc0bb6fdc28966a156945b769c2e55c`, including untracked sources.
No window opened, no edits by reviewer, no confirmation review requested.
Self-review pass recorded in the bead before dispatch.

All three findings were confirmed and fixed:

1. **P1: historical zone events after resume moved the clock backward.**
   Reviewer reproduced pause 1.1, change known at 2, resume 5.5, then clock
   5.5 → 3.0. Sections learned in the pause now replan onto a future downbeat;
   several historical changes resolving there present the latest one once.
   Zone fallback clocks/checks preserve their remaining time and cannot queue
   a historical beat. Tests cover the change and a 69.4 s freeze crossing a
   fallback deadline. This is deliberate: music decides the current zone;
   menus do not replay all intermediate musical transitions.
2. **P2: music_pending stayed set after music recovered under another freeze.**
   It now clears with the MusicStopped reason, even while Paused/card remains.
   A regression pauses across quiet 2–8, loud 8–13, quiet 13–20 and confirms
   unpausing at 18.1 cannot advance the world during silence.
3. **P2: free pulses moved the hero during victory's final pose.**
   Pulse work is suppressed after blasting. A held direction with beats ending
   at the blast downbeat now preserves position through the beatless epilogue.

While checking monotonicity, the test also exposed past held strides from a
newly bounded interval whose second beat arrived late. Survivors skips those
missed steps (free pulses already own gaps), rather than running time backward.
Steady test fixtures now honor the analyzer contract: beats known by the
previous beat. The real File session's 95 beats all met that contract.
