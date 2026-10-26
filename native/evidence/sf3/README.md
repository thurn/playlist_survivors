# SF3 — freeze, silence and keys

The freeze is independent sim state and a zero world-time warp. The run clock
subtracts accumulated frozen song time from song time (rather than summing
frame deltas). Finite queued actions retain their remaining run time and snap
to a known grid after resume; repeating beat/stride work is regenerated. Chain
anchors and swing plans are rebased. No uncommitted grid is extrapolated.

Controls: Escape/focus loss pause, Escape/click resume on the next known beat,
Backspace ends. Enter and card-Space use Confirm; movement keys held at open
need release. Interaction hooks for SF7/SH10 carry selection/confirmation state;
those beads add the actual cards, timing eligibility and chest resolution.

## Kept checks

From `native/`:

- `cargo test -p sim --test survivors -- --nocapture` — 15 passing cases,
  including streaming=batch, held keys, nested reasons, chained interactions,
  free pulse, victory replanning and the known-evidence silence boundary.
- `cargo test -p sim a_queued_fire -- --nocapture` — remaining-time fire is
  held through the pause; it fires at 4.0 s, not during or in a catch-up burst.
- `cargo test -p eval --test live late_confirm -- --nocapture` — 3,861 identical
  settled records, hash `4f798c6e571ffcd8`, two key rollbacks, deepest 420 ms across freeze opening.
- `cargo test -p eval --test session unknown -- --nocapture` — unknown key
  names fail replay parsing rather than becoming Space.
- `cargo test -p app watchdog -- --nocapture` — eight seconds of silent
  buffers keep capture alive; actual capture/main-thread stalls still trip.
- `cargo clippy -p sim -p eval -p players -p app -p cli --all-targets -- -D warnings`.
- `taskpolicy -b cargo test -p sim --test causality -- --ignored` — full
  pre-existing causality sweep; retained separately in `causality.txt`.

## Real File-mode gap

Fixture `/tmp/sf3-gap.wav`: first 16 seconds of house-128, eight seconds of
zero PCM, then that same excerpt. No generated audio is committed.

```
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions \
  target/release/playlist-survivors --file /tmp/sf3-gap.wav --delayed --headless --seconds 46
 target/release/ps replay /Users/dthurn/playlist_survivors/native/logs/sessions/1792991737
```

`gap-run.txt` shows MUSIC STOPPED at run 17.900 s, song 17.900 s;
RESUMED at the same run time, song 25.400 s. There were no late/dropped
sounds or late keys; live stream arrivals caused 34 rollbacks (5.8 s total
re-simulation in the last timing report). `gap-replay.txt` matches all 98,692 live settled records
(101,943 total replay records including its later speculative tail), 482 kills,
full HP. These are temporary director enemies, not SF6's VS schedule and not a
survival/pacing measurement. The gap check is headless File mode: real System
capture permission, speaker timing, focus/click feel and visual bob are pending
operator checks, not inferred from it.

Cold review: [findings and dispositions](review.md), all three fixed with regressions.
