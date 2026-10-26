# SF7 — Scripted offers and level-up freezes

`content/offers.yaml` covers levels 2–70 and labels the pacing row targeted by
each block. Levels 2–8 keep the design's example. With all weapons built,
card1 reaches Sword8/Hollow Heart by level18, Garlic8/Pummarola by28,
Wand8/Tome by38, and Bible8/Ring8 by48. The pure table path is maxed at67
without chest upgrades; its last three rows heal. These are inventory targets
at given levels, **not measured times or pacing gates**. Only SW7 retunes them.

## Choices and interfaces

- Each card tries its preferences, then lowest-rank owned weapon, lowest-rank
  owned passive, then any eligible item. Ties use stable typed IDs. New-item
  fallback prefers damage/sustain (Sword, other built weapons, Spinach, Hollow
  Heart, Pummarola, Duplicator, Tome, Spellbinder). An evolved weapon blocks
  reoffering its base. Unbuilt weapons are skipped; the six passives work now.
- Choices are distinct while at least three items are eligible. With fewer,
  remaining eligible choices repeat rather than offering a premature heal.
  When none remain, every card is the same one-time heal choice, using the
  level's `maxedHeal` (30), capped at missing HP.
- One gem can cross several XP thresholds. `Progress.pending_levels` queues
  them; each offer consumes one, and the world stays frozen throughout the
  chain. Hollow Heart compounds and heals exactly the gained maximum.
- `RunState.offer` is a Copy value: target level, three typed cards
  (item/before/after), and three absolute song-time reveal dates. The sim
  plans known future beats at opening, using 0.35-second free-pulse dates for
  an uncommitted tail. These dates stay stable for that screen; the renderer
  reads exactly the same dates. Confirms before the third date are ignored.
- Selected card and held-key protection stay in SF3's freeze state. Every
  choice mutation is rollbackable; the renderer keeps no modal/pick state.
  A retracted offer disappears from the projected RunState with no choice.
- After the last pick, resume uses a committed beat, or a free pulse if none
  is known. A newly committed beat can replace a still-pending free-pulse
  resume. Music-stopped and manual-pause reasons continue to hold the world.
- The existing root content loader validates this native-only table through
  its typed parser; the legacy library/reference hash excludes the table.
- The plain overlay uses the old font and simple rectangles. SH1/SH7 own
  font/icons, animation, audio and polish. Offscreen stills now use the held
  run/presentation clocks, matching the actual app while cards are open.

SF8 and SH1 were unclaimed when serialized after SF7: App options/frame input
and `slice/hud.rs` respectively. Native bead notes carry the handoff. The
headless `--pick-first` actor chooses only after reveal3, through normal
recorded keys, with normal damage. It is separate from the existing
`--spawn-smoke` damage exemption. Its retry token includes the offer level
and third reveal, so a retracted/reopened offer can be retried.

## Kept checks

- `offers-tests.txt`: preferences, unbuilt items, fallback, evolution/base
  exclusion, all-max heal, and the intended all-built inventory path.
- `card-tests.txt`: early confirm, held confirm, three chained choices,
  passive effect/added-max healing, beatless resume, new-grid relock,
  and exactly one 30 HP maxed-card heal.
- `offer-rollback.txt`: accelerated **test-only** bat HP/speed/XP generates
  actual combat/gem XP quickly. Nine picks each apply one rank; 5,601 records
  match on-time and 400 ms speculative execution with 30 ms late confirms,
  nine key rollbacks and deepest rollback420 ms. Restoring the sim/journal
  before XP retracts the projected offer and keeps the initial inventory.
- `live-tests.txt`, `survivor-suites.txt`, `legacy-sessions.txt`: existing
  engine, survivor freeze/clock, and stored legacy-session contracts.
- `cards.png` / `still.txt`: inspected offscreen view at 40 song seconds,
  with all three cards visible and one highlighted. No app window opened.
- `headless-cards.txt` / `replay-cards.txt`: normal-damage File/Delayed65s
  session `1793019906`. The offer opened at run24.957s; card1 was queued at
  song26.258 after third reveal26.250, and the world resumed at26.720.
  All79,798 settled live records replay exactly (81,388 with the replay tail),
  57kills, finalHP0.99, no late/dropped sounds. This is a short playability
  check, not the SF9 positioning/survival gate.

Commands from this worktree root:

```sh
cargo build --release -p app -p cli --manifest-path native/Cargo.toml
PS_SESSIONS=/Users/dthurn/playlist_survivors/native/logs/sessions native/target/release/playlist-survivors --file /tmp/sf6-five-minutes.wav --headless --delayed --press-beats --pick-first --seconds 65
native/target/release/ps replay /Users/dthurn/playlist_survivors/native/logs/sessions/1793019906
native/target/release/playlist-survivors --headless --still native/evidence/sf7/cards.png --still-at 40
```

The temporary WAV is SF6's 330-second repetition of the shipped house-128
track; see SF6's evidence for its construction. Operator-only checks are in
`native/evidence/operator-pending.md`.

Final validation on staging base `de2ea4f` (SF11 included): `scripts/check-fast`
passed in51s (native50s); clippy and all selected workspace tests/purity/unused
 dependency checks pass. The first aggregate caught a formatting lint in test
reporting; its corrected source was checked again. There were no CI-script,
workspace-configuration or shared-fixture changes requiring local ci-full.

Before the first feature commit, `tg update` correctly rejected a dirty tree,
then a clean branch with zero unique feature commits. The edits were safely
checkpointed, the clean feature branch fast-forwarded to staging, and the
checkpoint restored without conflicts. The final check ran after that refresh.
The temporary checkpoint is removed after the feature commit.

For a long spawn-only smoke after SF7, add `--pick-first` to SF6's
`--spawn-smoke` command; without a card actor, a headless run deliberately waits
at the first offer. Song time and run time diverge during reveals/choices, so
SF8/SF9 must report both rather than treating a playlist prefix as equal run time.
