# SW5 — the five evolutions

All five evolved identities are enabled. `source-audit.json` proves that the
weapon table changes only the five implementation flags: every numerical
field and evolution pair is identical to base
`12db4a0a34ad38ed77672862799165510c28767b`. The source/content checks cover
45 ranks / 450 numerical fields against `docs/survivors/appendix-feel.md`.
No offer table, easy knob, binary asset or third-party source changed.

| Evolution | Eligibility | Runtime |
|---|---|---|
| Bloody Tear | Sword 8 + Hollow Heart owned | source P4/N2/Area1.3 spin, source .1×Luck critical chance / ×2, 8 HP on a critical overlap at most once per active second |
| Soul Eater | Garlic 8 + Pummarola owned | source P2/Area3/1 s aura; per-contact healing adaptation and actual-recovery growth below |
| Holy Wand | Magic Wand 8 + Empty Tome owned | source P3/N4/500 ms, speed3.3 U/s and pierce2; Tome5 gives300 ms |
| Unholy Vespers | King Bible 8 + Spellbinder owned | source P3/N4/Area1.75/Speed1.5, 3 s duration / 3 s recast, per-blade target reset1.7 s; continuous musical orbit |
| Thunder Loop | Lightning Ring 8 + Duplicator owned | source P6.5/N6/Area4/4.5 s volley; each actual first strike returns to its original point after a1.5 s charge, snapped forward to the grid |

The existing sword tempo normalization, homing adaptation, source aura/body
radii and musical orbit-speed tolerance remain. Source generic duration is
not a Wand lifetime, a Lightning damage window or Garlic's target repeat.

## Explicit adaptations and ownership

Soul Eater follows the design's **per-hit healing** adaptation. Every actual
aura contact heals one source Little Heart amount, `1 × (1 + Recovery)`,
capped by missing HP. With required Pummarola1 this is1.2 HP, including a
nonlethal contact. This deliberately replaces the source kill-only heart
chance `0.15 × (PPower /7)`; it is not claimed as source drop behavior. At
crowd density, healing scales with actual contacted targets. SW7's bots must
measure that whole-run result; this bead makes no survival/balance claim.

Soul Eater's source power bonus is `min(6, actual HP recovered since evolution
/600)`. The bonus is added after Might scales base power: `2 × Might + bonus`.
All actual recovery sources count, including regeneration and food;
overheal does not. A new aura incarnation captures the updated power. A
successful Soul Eater replacement clears the old Garlic aura and resets the
recovery epoch. Bloody Tear's heal clock uses active run time, survives
snapshots, and cannot advance while frozen. Full-health criticals still
consume that weapon's heal opportunity without creating recovered HP.

Vespers preserves source-duration overlaps, including Spellbinder and
Cooldown effects. When forward snapping would leave a gap after the last
active cycle, that final cycle bridges to the replacement; its tail then
fades while the replacement grows. This bounded bridge fulfills the design's
never-stops orbit without changing the raw recast clock. Freeze holds world
phase. New casts retain the existing ±15% cardinal-lattice speed rule.

Evolution replaces the base slot atomically through SH10's chest seam. It
requires the max base and partner, clears unissued parent casts and rejects
relearning the replaced base. Already emitted Wand bullets/bursts, Bible
cycles and Lightning ordinals finish as captured. Return strikes keep their
point after a target moves/dies, can hit a new occupant, and keep remaining
charge time through a freeze. Empty first attempts create no phantom return.

## Recording, presentation and sounds

Fate now carries the actual critical outcome and applied damage. Both logical
and grid EnemyImpact labels receive it; actual critical labels remain larger
and gold. New evolved-hit identities hash their applied damage and critical
state, so a replay cannot silently disagree about them. Old base-weapon label
metadata keeps its prior recording bytes; false critical values do not add a
new field to old serialized Fates. The live runner's speculation algorithm is
unchanged, but its recorder/projection changed and receives a cold review.

Typed fire/contact cues cover each evolved ID, with duplicate generic cues
suppressed. Thunder Loop sounds only actual positioned strikes, including
returns. Existing licensed clips and SH2 budgets/mix are reused; the real
palette decode/onset/retraction test exercises all four cues per new ID.
Distinct crimson, violet, gold, purple and pink base tints reuse the sword,
aura, missile, orbit and sky-bolt renderers. Their icons and full item names
appear in the HUD and roulette; SP1 owns transformation/showpiece polish.

## Kept proof

`evolution-tests.txt` passes eleven native integration tests:

- all five max-base/partner requirements, early-chest rejection, atomic slot
  replacement, repeated evolution and base reintroduction rejection;
- 21 guaranteed critical overlaps at120 BPM deal2.962962963 each, with only
  two shared8 HP heals in1.51 s across multiple targets;
- Soul Eater's radius, nonlethal1.2 HP contacts, per-incarnation memory,
  actual-recovery growth, no overheal growth and +6 power cap;
- Spinach5/Might1.5 gives4 damage after600 actual recovered HP and9 at
  the cap, confirming that Might never multiplies the recovery bonus;
- Holy Wand's source speed/pierce and194/196/196 bullets in10 s at
  70/128/174 BPM versus200 ideal source bullets (bounded startup/tail delay);
- Vespers' visible orbit throughout12 s at all three tempos, four raw3 s
  recasts, required Spellbinder1's3.3 s duration and bounded live cycles;
- seven original Thunder strikes and seven returns at+1.5 s (120 BPM),
  preserving points after moving the old target and striking a new occupant;
- fractional pauses preserve a Thunder return until3.875 s after a
  .2–2.5 s pause, and Holy Wand's remaining50 ms burst delay;
- the real five-item roulette grants five evolutions from origin25 and no
  evolution from origin5, with sequential rewards and five retained slots;
- all five effects, complete LogRecorder, heal counters and critical outcomes
  reproduce after a snapshot at.2 s, a fractional pause at.237 s and17 ms
  replay cuts through8 s;
- poisoning beats/energy whose knownAt is after1.6 s changes no output before
  that boundary.

`sim-tests.txt` passes98 native unit cases. `source-and-content-tests.txt`
passes source tables, old-content compatibility and offer rules.
`eval-unit-tests.txt` covers the new gameplay hash and actual-Fate labels;
`live-tests.txt` passes19 live and4 recorded-session cases, including SH10's
actual chest/late-key path. These do not claim that a normal player naturally
acquires all five evolutions in the first minutes. `sound-tests.txt` decodes
and reconciles each normal/accent fire/contact cue and cancels retractions.
`clippy.txt` covers app/sim/eval, all targets, warnings denied.

`headless.txt` is a normal-damage 35 s File run with actual first-card picks:
252 sounds, zero late skips/drops, 96 kills and full HP. `replay.txt` matches
all 37,142 live settled records (38,701 with its longer replay tail). It
acquires earlier weapons normally; the evolved weapons are covered by the
native actual-roulette/contact tests and labelled presentation fixtures.

## Stills and playable checks

Each evolution directory contains `normal.png` and `accent.png`; Thunder Loop
also has `return.png`. All11 labelled1280×720 stills are verified in
`media-info.json`. `captures.txt` records counts/setup. The fixtures use an
injected evolved loadout with its real partner, lead zero, synthetic128 BPM,
and hidden base-sword FX. Holy Wand/Thunder Loop use eight durable skeletons
(HP1000/XP0); Soul Eater/Vespers have no targets. Bloody Tear uses the same
durable targets and Luck10 to force **actual combat criticals**, not renderer
flags. No hero damage exemption is set. The stills are silent presentation
checks, not natural acquisition, pacing, listening or dense GPU performance.

From `native/`:

```sh
cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed
cargo run --release -p app -- --file ../public/reactive/house-128/house-128.ogg --headless --seconds 35 --pick-first
cargo run --release -p app -- --headless --evolutions-check /tmp/evolutions-check
cargo test -p sim evolution_ -- --nocapture
cargo test -p eval --test live --test session
cargo test -p app weapon_pulse_and_contact -- --nocapture
cargo clippy -p app -p sim -p eval --all-targets -- -D warnings
```

SP1 can build on actual `rec.evolve` / `Record::Weapon { evolved:true }` and
inventory transitions for transformations. Actual `HealRecord` source/amount
is available for healing showpieces; its only base feedback here is HP and
critical labels. Thunder's fixed-point pending return is snapshot-owned;
publishing a charge presentation moment remains part of SP1's showpiece work.
SW7 owns the whole-run measurements and milestone review.
