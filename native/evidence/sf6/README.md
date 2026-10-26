# SF6 — Mad Forest spawner

The level now contains all 31 source minutes, 20 distinct enemy roles, all 21
source events and the per-minute treasure data. The source row is the same
minute in `docs/survivors/appendix-damage-vs-health.md`; event/treasure fields
come from `appendix-feel.md`. These are curated numeric projections with our
art assignments, not raw extracted game files. The Reaper is omitted.

## Choices and boundaries

- Tick deadlines and minute boundaries use the held run clock. Arrivals use
  the first known beat/eighth at or after readiness; without a known future
  grid step they use the raw deadline. Later evidence cannot move an arrival
  into the past. Pending admissions regenerate their grid point after a freeze.
- Each tick admits one roster, then whole rosters to minimum. Only the hard
  300-alive cap can truncate a roster. Duplicate GHOST entries at minute 13
  are retained. A full field holds bosses, reserves the next free slot, and
  admits them on a fresh grid step. Regular ticks accumulate no population debt.
- Population fields inherit when omitted. Bosses, events and treasure are
  explicit per-minute arrays, matching the appendix's reset rules; an empty
  treasure array means no normal chest. Event chance absence and source zero
  both mean no chance gate. Events/chests execute in SW6/SH10.
- Scheduled bosses (including minute 5's XLMANTIS and minute 8's XLBAT), boss
  bats in ordinary rosters, and FLOWER scale HP by player level at admission.
  Recycling keeps remaining HP and the scheduled-boss reward flag.
- Source speed, power, XP and knockback are data. Contact remains the fixed
  0.5 multiplier with SF4's Armor/lockout. Fixed knobs are now in the level:
  brutes 5% chicken, scheduled bosses one chicken, maxed card heal 30 (SF7).
- Distinct `vs-*` role IDs allow BAT1/2/3 and normal/boss variants to share art
  without losing source stats. Brutes use orc-elite at 2×; plants use slime;
  boss/giant bats use bat at 2×. Final gaits/lunges and art belong to SH6/SW3/SW4.
- Music still chooses zones, camera and lighting. Survivor director population
  and section rosters are bypassed, and its set pieces remain retired.
- SH10 owns boss behavior, chest eligibility and carrying the source minute's
  treasure with a boss instance. This bead supplies the schedule and plain bosses.
  SF10 owns the long-run pools and snapshot bounds.

## Verification

`spawner-tests.txt` exercises every source row in isolation on a synthetic
0.5-second grid for a minute, checking each role appears, refill minimum,
300 cap and HP at player level 20. It also checks frame cuts/freeze, a full
field's deferred boss, recycled remaining HP, and the one-chicken reward.
This is not a physics soak or a survival test.

`source-tests.txt` compares all 31 schedule rows and all 20 roles' primary
stats directly with the documented source tables, and checks grid admission,
raw fallback, held admissions, minute inheritance and deferred-boss timing.
`freeze-tests.txt` is the existing 15-test survivor clock/freeze suite.
`late-confirm.txt` compares speculative and on-time recordings across a
420 ms rollback. `legacy-session-tests.txt` keeps the three stored legacy
session contracts. `causality-full.txt` is the ignored legacy every-cut sweep,
run under `/usr/sbin/taskpolicy -b` (63.32 s).

`headless-five-minutes.txt` preserves the normal tap-player attempt: it fell
at 233 run seconds. `headless-hold-five-minutes.txt` preserves the interim
straight-line check: its first run fell at 122 seconds. Neither is the SF9
positioning bot or a balance gate. No easy knob was changed to make these pass.

`--headless --spawn-smoke` is explicitly a **spawn-only** check: hold Right,
with hero damage disabled. The setting is saved as `noHeroDamage` in session
metadata and honored by `ps replay`/re-analysis and Watch. It requires headless
mode and cannot combine with the tap player. It proves roster arrival, not
survivability. Card picking and real survival bots belong to SF7/SF9.
`headless-spawn-five-minutes.txt` keeps an intermediate smoke before the final
full-field grid fix; `headless-prelanding-five-minutes.txt` exposed a replay mismatch from the
previous frame-dependent landing callback order. `replay-prelanding-five-minutes.txt`
keeps that failed comparison. Landings are now explicit survivor simulation stops;
the 8-second dense/off-grid-pause regression compares 118,817 records between
on-time and 200 ms speculative execution. `replay-landing-smoke.txt` keeps a
20-second real session replay (20,589 matching settled records).
`headless-final-five-minutes.txt` is the fresh final source run.

From the worktree root:

```sh
cargo build --release -p app -p cli --manifest-path native/Cargo.toml
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --file /tmp/sf6-five-minutes.wav --headless --delayed --spawn-smoke --seconds 315
```

The temporary mono 44.1 kHz WAV repeats `public/reactive/house-128/house-128.ogg`
(decoded as signed 16-bit PCM) to 330 seconds. It is not committed. All checks
were headless; no app window was opened. Operator-only role readability and
feel checks are in `native/evidence/operator-pending.md`.

## Final measurements

Final session: `1793001281`, 315 wall seconds in File/Delayed mode, one
run stopped normally after 309.129 run seconds. `noHeroDamage: true`; this is
a spawn-only result. The first observation per phase (10-second reporting)
is below; the source/grid tests verify exact timing. Scheduled-spawn counts
exclude far-enemy recycling. Peak observed alive count: 300.

| Minute | First observed run s | Scheduled spawns | Alive | Actual roles |
|---|---:|---:|---:|---|
| 0 | 4.192 | 18 | 17 | `{"vs-bat3": 17}` |
| 1 | 64.217 | 85 | 64 | `{"vs-bat1": 4, "vs-bat3": 54, "vs-bat4": 1, "vs-zombie": 5}` |
| 2 | 124.233 | 222 | 149 | `{"vs-bat1": 46, "vs-bat2": 8, "vs-bat3": 41, "vs-bat4": 1, "vs-zombie": 53}` |
| 3 | 184.258 | 534 | 300 | `{"vs-bat1": 85, "vs-bat2": 85, "vs-bat3": 76, "vs-bat4": 2, "vs-skeleton": 11, "vs-zombie": 41}` |
| 4 | 244.283 | 670 | 298 | `{"vs-bat1": 37, "vs-bat2": 40, "vs-bat3": 44, "vs-bat4": 2, "vs-ghost": 1, "vs-skeleton": 146, "vs-zombie": 28}` |

No late or dropped sounds in the final run (2,178 started at shutdown).
The 420 ms late-Confirm regression settled 7,679 identical records with
hash `77f8e4d7a8f8977d`. Legacy every-cut causality passed in 63.32 s.
Clippy is clean; 30 app and 5 CLI unit tests pass.

Replay command from the worktree root:

```sh
native/target/release/ps replay /Users/dthurn/playlist_survivors/native/logs/sessions/1793001281
```

Exact result is in `replay-final.txt`: all 3,781,538 settled live records match
(the replay also simulated the trailing unsettled period, for 3,800,931 total).
The run had 489 kills; HP 100% is a check flag result, not survival evidence.
Final `scripts/check-fast` passed in 49 s (native 47 s), including the full
selected workspace tests, purity, clippy and unused-dependency checks.

A separate selector defect is filed as tooling bead `hv-n7fn.36`: a
Survivors-only level edit currently selects no native checks. SF6 also changes
Rust, so its aggregate did select and pass all native crates. The tooling repair
has its own worktree/delivery and is not folded into this feature.
