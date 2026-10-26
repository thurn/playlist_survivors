# SW1 — King Bible

King Bible is implemented and enabled in the unchanged offer table. Its orbit
uses the existing blade art with causal afterimages; normal/accent fire and
contact use existing licensed samples under SH2's voice budgets. No bake,
external image or easy-knob/offer retune was needed.

## Source behavior

The authority is `docs/survivors/appendix-feel.md`, King Bible table/runtime
notes. `source-audit.json` also keeps the inspected projectile source's SHA256
and the selected behavior facts; no extracted source or raw game data is added.
Rank 1/8 have power 1/3, Amount 1/4, active duration 3/4 s and raw cycle 6/7 s:
Bible waits Duration + (3 s × Cooldown), with Duration modifiers applied.
Orbit radius is 0.5 × min(4.5, Area) U, source angular speed is 2.1875 × Speed
rad/s, and each blade has its own target set cleared every 1.7 active seconds.
That reset is periodic from birth, not a 1.7-second delay after each contact.
Source KB 5 flows through the existing recoil path.

The body starts at zero scale and grows over 0.5 s to 1 + 0.3 × Area, applied to
8 legacy px. This demo uses a linear growth/shrink curve. Combat stops exactly
at Duration; a separate 0.5 s visual shrink removes its pose afterwards.
Might, Amount, Area, projectile Speed, Duration and Cooldown use the existing
finalized stat path. Stats are captured by the cycle's Shot, as with Wand.

## Beat lattice

A cycle casts on a committed beat (raw readiness without a known grid), with
blade zero on the right cardinal and the other blades evenly spaced. Using the
committed period at cast, choose the closest compatible angular speed, accepting
it only within ±15% of source speed. For counts dividing four, an integer number
of quarter-turns per beat is compatible. A slower quarter may span N beats if
4N is divisible by the blade count. Both neighboring reciprocal grids are
compared; simple rounding could miss an eligible slower grid. Otherwise keep
source speed, including an incompatible three-blade formation at ordinary tempos.
Fast integer steps may pass extra cardinals between beats.

The selected speed is held for that incarnation. World position, age and phase
hold through freezes; a subsequent cycle reanchors on its cast beat. Existing
blades are not teleported to a new phase for a menu or tempo change. This is a
per-cycle lock to the committed period, not continuous retiming to future beats.

`bible-tests.txt` covers source and musical rules, including same-step alive-slot
cleanup after a lethal contact:

- At rank 1/8, real sim contacts apply damage 1/3 through 1/4 blades, with 2/3
  periodic windows before duration ends. A pinned wide target isolates memory;
  spawn-growth contacts occur in the first half-second, then resets at 1.7/3.4 s.
  Late entry at 1.6 s permits another contact at 1.7 s, proving periodic reset.
- Actual pose radii and angular speed match the selected source/cardinal rules.
  Rank 1/8 cycles repeat at 6/7 s in the 120 BPM fixture.
- Non-default bonuses produce damage 4.5, Amount 6, duration 6 s and Area 15,
  with orbit capped at 2.25 U and source speed 10.5 rad/s. A fractional pause
  holds all samples and lifespan; cleanup follows the visual shrink.
- Phase tests cover 70/128/174 BPM and an explicit three-blade compatible
  lattice, exact ±15% boundaries, reciprocal-neighbor choice, zero speed and
  missing period. Rank 8 at 128 BPM chooses 3.351032164 vs source 3.5 rad/s.

`source-table-tests.txt` keeps all 45 combat ranks / 450 numeric values matched
to the kept appendix. These are source numerical contracts, not a TS oracle.

## Shared interfaces and causal checks

`BibleCycle` lives in snapshot-owned SimState, with per-blade memories and IDs
from the shared projectile allocator. Its swept substeps cannot cut across the
empty centre of a long angular move. `MissilePose`/`Record::Missile` carry actual
blade positions, tangent/rotation, scaled body radius and cleanup, reusing the
view's ten-sample limit. Wand drawing now filters its own weapon IDs; Bible
reads its own IDs. No renderer-side orbit simulation or new journal type exists.
Typed fire/contact projections preserve actual cycle onsets and impact grids;
old generic Bible fire cues are suppressed in the projection.

`live-tests.txt` keeps 18 live and 3 session checks. The enhanced high-XP,
damage-exempt progression fixture reaches Bible through real offers: 22 picks
settle to the same 8,676 records with 0.4 s speculation and 30 ms late confirms.
The kept blade motion spans a 1.7 s memory reset across rollback. Its 200-XP
bats are explicitly a bounded progression fixture, not pacing or survival data.
Separate cause assertions require actual Bible fire/contact records and bounded,
causal pose trails. `sound-test.txt` decodes four Bible normal/accent cues,
checks exact onsets/non-ducking and retracts pending voices.

## Presentation and playable check

`normal.png`, `accent.png` and `orbit-and-accent.mp4` use real rank-8 cycle
emission with an injected initial inventory, lead zero and sword FX hidden.
The stream is synthetic 128 BPM with one known accent; there are no enemies.
The silent offscreen clip has 247 frames at 30 fps, 1280×720 (8.233333 s),
covering the later normal/accent cycles; the full fixture emits three real
cycles, one accented. `capture.txt` records those conditions. This demonstrates
blade drawing and afterimages, not survival, natural acquisition or rollback.

`headless.txt` keeps a 35-second normal-damage File run: 252 sounds, no late
skips/drops, 96 kills and full HP. It acquires earlier weapons, so Bible itself
is covered by the actual-card tests and visual fixture. `replay.txt` matches all
37,142 live settled records; replay has a longer settled tail. No long-run
balance, dense-orbit GPU performance or listening verdict is claimed.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 35 --pick-first
cargo run --release -p app -- --headless --bible-check /tmp/bible-check
```

SW2 can use the typed weapon fire/contact sound projection without changing
orbit logic. SW5 must route Unholy Vespers into cycle launch, use its own false
`intervalDependsOnDuration` field and preserve separate memories for overlapping
cycles; the vector and shared IDs already permit overlap. Spellbinder changes
both active duration and Bible's raw cycle. Existing cycles retain their Shot
when rank/stats change, and later casts take new stats. SW7 owns the W review
and measured balance. Eye/ear/feel remain an operator row, without a pause.
