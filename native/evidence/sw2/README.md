# SW2 — Lightning Ring

Lightning Ring is implemented and enabled in the unchanged scripted table.
Volleys start on beats; their strikes land on successive sixteenths. Each
strike chooses a currently visible enemy, applies circular area damage at that
actual position, and drives a sky bolt, flash and exact-onset sound. No asset
bake, external image, offer retune or easy-knob change was needed.

## Source rules and explicit adaptations

The authority is `docs/survivors/appendix-feel.md`, Lightning Ring table/runtime
notes. `source-audit.json` keeps the inspected projectile source SHA256 and
selected facts; no raw extracted source/data is committed. Rank 1/8 have power
1.5/6.5, Amount 2/6, Area 1/4 and a 4.5 s raw interval. The source body is
8 × Area legacy px, giving native radii 12.341597796 / 49.366391185 px. Might,
Area, Amount and Cooldown use the existing finalized stats; source KB 5 uses
the common recoil path. Generic Duration, Speed and target hit delay do not
turn the volley into a lingering damage field or delay successive projectiles.

The design replaces the source 50 ms repeats/short descent with grid landings.
The first strike lands on the cast beat, and the next is scheduled only when
the previous one lands. Without a known bounding beat pair it uses the existing
free sixteenth; a newly known grid can defer that provisional event to its
first committed sixteenth. No future beat or target is read in advance.

Target choice is with replacement, independently per strike. The deterministic
key is run seed, volley serial and strike index. Eligible centers are currently
hittable/alive and inside the current camera view. A cast with no visible
target makes no strike bolt/cue, but later strikes can acquire an enemy entering
view. Area victims use their actual current positions, including lunges, and
kills free the alive population immediately. Accent is refreshed at landing
from known forecasts, changes look/sound only, and does not change damage.

## Kept simulation proof

`lightning-tests.txt` contains five actual-sim tests:

- Rank 1/8 land 2/6 strikes with source damage/radius; two nearby enemies both
  receive each circular hit. At 120 BPM onsets are 0, .125, .25, .375, .5, .625.
- Six-strike volleys follow exact sixteenths at 70, 128 and 174 BPM. A bounding
  beat learned at .1 s replaces a provisional later fallback: actual strikes
  are 0, .0875 (still no known pair), .3, .45, .6, .75. Nothing is backdated.
- Offscreen-only targets produce no strike; an enemy entering after cast is
  selected at .125 s; moving the target between strikes changes x from 120
  to 300, proving positions are selected at actual landing.
- A fractional .203 s pause keeps pending strikes: 0, .125, 2.625, 2.75, 2.875,
  3.0 after resume. A non-default fixture applies Amount 8, damage 9.75,
  Area 6/radius 74.049586777; 1 ms generic duration and Speed 0 lose no strikes.
  Raw Cooldown 2.7 s snaps the next volley to the 3.0 s beat.
- A fixed 20 s/three-target fixture emits 30 strikes. Seed 2026 selects target
  counts 12/9/9; frame cuts produce identical strikes, while seed 2027 changes
  order. These counts demonstrate that fixture, not general distribution or
  combat balance.

`source-and-content-tests.txt` retains all 45 combat ranks / 450 numerical
fields against the kept appendix and the unchanged legacy content subset.
These are source numerical contracts, not bit-exact TS behavior.

## Causal presentation and sound

PendingLightning lives in the snapshot-owned event queue, with snapshot-owned
volley/strike counters. One pending event per live volley preserves every
ordinal through freeze/resume; there is no frame-owned burst scheduler.
`LightningStrike { id, weapon, song, x, y, radius, accent }` enters the journal
as an actual moment. Scene drawing never chooses targets. It reuses the chain
bolt's exact jitter/core tessellation, adds a short ground ring and a restrained
world flash, and keeps at most 64 short-lived entries. Legacy chain drawing
calls the same extracted helper with its previous parameters.

Typed fire pulses derive from actual strike moments, not empty cast attempts.
Contact cues follow the existing EnemyImpact reaction grid. Generic legacy
Lightning cues are suppressed in this projection. SH2's category budgets and
non-ducking mix still own all clips. `sound-test.txt` decodes the four normal/
accent fire/contact cues, checks exact onsets and cancels retracted voices.

`live-tests.txt` keeps 18 live and 3 session cases. The bounded high-XP,
damage-exempt card fixture acquires Lightning naturally through the actual
choices: 22 picks, including two positioned Lightning moments, settle to the
same 8,571 records with .4 s speculation and 30 ms late confirmations. The
200-XP bat fixture is not pacing or survival evidence. Separate tests require
actual strike/contact records for the projected sounds and bounded causal
projectile trails for the other weapons.

## Captures and playable check

`normal.png`, `accent.png` and `sky-strikes.mp4` are a labelled diagnostic setup:
injected rank-8 Lightning, lead zero, sword FX hidden, eight skeletons at
HP 1000/XP 0 and a synthetic 128 BPM known beat/forecast stream. No hero damage
exemption is set. Two real volley attempts produce six actual positioned
strikes, one accented; attempts without an eligible target are consumed quietly.
The silent capture covers song 4.4–5.8 s: 43 frames at 30 fps, 1280×720, 1.433333 s.
`capture.txt` and `media-info.json` retain the setup/counts. This is drawing
and strike evidence, not natural acquisition, pacing, survival or dense GPU data.

`headless.txt` is a 35-second normal-damage File run: 252 sounds, no late skips/
drops, 96 kills and full HP. It acquires earlier weapons; Lightning itself is
covered by actual-card tests and the visual fixture. `replay.txt` matches all
37,142 live settled records, with a longer replay tail. Listening/feel remain
an operator row, not a pause.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 35 --pick-first
cargo run --release -p app -- --headless --lightning-check /tmp/lightning-check
```

SW5's Thunder Loop must repeat at the original strike point after its source
1.5 s charge, resnapped to the grid, without rerolling the target or extending
the generic duration. The exported positioned moment already supports its
weapon ID; split/reuse the current AoE application when adding that repeat,
and extend the typed contact filter and palette. SW7 owns measured balance
and the W milestone review; no balance threshold is claimed here.
