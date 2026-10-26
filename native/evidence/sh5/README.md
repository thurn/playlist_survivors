# SH5 — Magic Wand

Magic Wand is now implemented and offered by the existing scripted table.
No offer preference or easy balance knob changed, and no asset bake was needed.

## Source rules and deliberate adaptations

The kept source is `docs/survivors/appendix-feel.md`, Magic Wand table and runtime
notes. Rank 1/rank 8 power is 1/3, Amount 1/4, raw interval 1.2/1.0 s, pierce 1/2,
repeat 100 ms, radius 8 legacy px × Area and speed 1.65 U/s before Speed. The common
conversion gives radius 12.341598 app px and speed 254.545455 px/s. Might, Area,
Amount, Speed and Cooldown modifiers apply; the generic 1,000 ms duration is not a
lifespan. Pierce is spent on surviving targets; a lethal overlap preserves it.
Per-projectile target memory uses the source 1,000 ms hit delay.

The burst cycle uses SF5's unsnapped interval and first-shot eighth snap. The
remaining projectiles keep source 100 ms spacing from that actual first shot.
Treating each Amount projectile as a separate reserved sixteenth would cap
70 BPM throughput at 4.67/s while rank 8+Amount2/Cooldown0.6 requires 10/s, creating
a backlog. Interval-cycle collisions retain SF5's own sixteenth staggering.
Secondary WandBullet events preserve raw remaining delay through freezes.

Homing is a demo adaptation, as the source appendix explicitly permits. A
projectile locks the nearest hittable enemy at spawn (arena ID breaks ties),
turns at most 12 rad/s and reacquires after death or remembered contact. Swept
circle/segment collision applies near targets first, independent of spawn order,
and stops a spent head at actual contact until the same sixteenth as its impact
spark. View +96 px and 2,400 px travel culling, plus a 12-second active-world-time guard for zero-speed modifiers, bound homing life without substituting
the generic duration. These are explicit gameplay/presentation choices, not a
claim that VS continuously homes or uses that cull distance.

## Kept combat measurements

`wand-tests.txt` contains seven real-sim tests:

- Both source ranks apply actual damage 1/3 to the expected 1/4 target contacts;
  rank 8 burst fires at 0, 0.1, 0.2, 0.3 s, including without a beat grid.
- A controlled long integration step crosses two targets and orders near before far even with reversed arena IDs;
  lethal contacts preserve pierce and a single rank 8 missile stops after two
  surviving targets. The test explicitly isolates one missile from its burst.
- Homing turns through 180 degrees, respects per-target memory and stat bonuses.
- Movement/sampling holds during freeze, and culling is finite while a harmless
  projectile remains alive past the unused one-second generic duration. A zero-speed regression also proves finite cleanup.
- A spent head holds before its impact sixteenth and disappears on that grid.
- At rank 8 with Amount+2/Cooldown0.6, 40 s emits 398/400/400 bullets at 70/128/174 BPM,
  versus 400 source-rate bullets, with 4/2/2 pending burst bullets. Boundary delay
  is bounded, rather than a growing serialization deficit. A mid-burst pause
  resumes at 2.5 s and retains the remaining 50 ms, then 100 ms (2.55/2.65s).

`table-and-offer-tests.txt` checks all 45 ranks/450 numeric fields against the
kept source appendix and confirms the current first offer contains Garlic,
Magic Wand and Sword while unavailable items still skip. These controlled
measurements do not establish whole-run balance or survival.

## Causal view, sound and rollback

Missiles, target memory, serial IDs and queued burst/end events live in cloned
simulation state. New MissilePose records feed at most ten samples per active
projectile in LiveView; the renderer reads those trails instead of simulating
its own projectile. Existing record encodings remain unchanged, and the new
records exist only when projectiles exist. Typed pulse/contact projections keep
exact burst onsets and actual grid contacts. Normal/accent clips are already
baked licensed samples and use SH2's budgets, retraction and non-ducking mix.

`live-and-session-tests.txt`:17 live / 3 session tests pass. The real-XP late-key
test now acquires Wand and settles to the same records/inventory as on-time
confirmation. Cause assertions tie both weapons' pulses/contacts and bounded
missile trails to actual journal records. `sound-test.txt` decodes all four
Wand cues, checks non-sixteenth onsets and cancels pending voices on retraction.
`survive-driver-tests.txt`/`player-tests.txt` retain four driver checks (including
future-input prefix safety) and all player tests. `sword-capture-regression.txt`
and `garlic-capture-regression.txt` keep the earlier visual modes working.

`headless.txt`/`replay.txt` keep a 35-second normal-damage File run: 173 sounds,
zero late skips/drops, 55 kills/full HP and 36,147 matching settled records. That
short stationary run acquired Garlic, not Wand; Wand is covered by the real-XP
runner tests and the separate visual fixture. `bench.txt` passes default CPU
frame-work p99 at 1.24 ms (4 ms limit), but that default fixture does not acquire
Wand. No dense-Wand GPU/performance or listening verdict is claimed.

## Offscreen presentation

`normal.png`, `accent.png` and the silent six-second `homing-and-impact.mp4`
were visually inspected. `--wand-check` injects rank 8 Wand into the initial
loadout, uses lead 0 and synthetic 128 BPM committed beats/forecast, hides Sword FX
and places eight normal-damage skeletons with health 1,000/XP 0 in memory. The
streaks, curves, sparks and damage labels come from actual simulation records.
This controlled visual setup is not progression, rollback, balance or survival
evidence. Operator feel/mix remains a row in operator-pending; later milestone
sweeps own integrated tuning and density budgets.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 35 --pick-first
cargo run --release -p app -- --headless --wand-check /tmp/wand-check
```

SW1 can extend the isolated weapon delivery without touching Wand's physics.
SW5 must enable Holy Wand, route its typed pulse/contact IDs and palette and
choose how evolving affects pending base bursts; speed 2 in its source row
already gives 3.3 U/s through the shared missile scale. Cold runner review found no actionable issues (see `review.md`). Source SHA and
final fast-check are recorded after completion in bead notes.
