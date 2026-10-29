# Survivors Run: the 30-Minute Demo

> **Restoration follow-up (2026-10-27):**
> [Restore the Musical Power Fantasy](feel-restoration.md) supersedes the
> conflicting combat, enemy movement, HUD, audio and choreography decisions
> below. VS is a baseline for balance and density, not a requirement to remove
> the game's musical identity. This document retains the implementation
> history and the run/progression systems the restoration keeps; the follow-up
> is a plan, not a claim that restoration has already landed.

## Introduction

The native app plays a reactive run to whatever music is on the Mac
([native-app.md](native-app.md)). Today a run is a few minutes long. It has
three weapons, three level-ups taken by walking into orbs, and a director that
grows the horde with the music's loudness.

This plan turns it into **a full 30-minute run modelled directly on Vampire
Survivors (VS)**:

- VS's movement speeds, damage formula, enemies, experience curve, levels and
  weapon progression;
- VS's pick-1-of-3 level-ups, with the music still playing while the cards are
  open;
- VS's Mad Forest minute-by-minute schedule, ending in a victory at 30:00.

The goal is **game feel first**. We copy VS's numbers so that the only new
thing is the music: the hero strides on the beat, every enemy moves and attacks
on the beat, and every weapon fires on the grid. Creativity with the rules
comes after this demo, not in it.

The demo is played mostly in **zen mode** (nothing is judged) with **Delayed
listening** (the music is heard 3 s late, so beats are known ahead). It must be
easy: a zen player survives the 30 minutes with basic positioning.

Content is deliberately small:

- 5 weapons, each with one evolution;
- 6 passives;
- about 10 enemy roles;
- a scripted offer table.

Most of the effort goes into making that small set look and sound stunning,
because the demo's job is to sell the fantasy in its first minutes.

**Decided with the user on 2026-10-25** in a design interview. The
[Decisions](#decisions) below are fixed for executors.

### Terms

Terms from [native-app.md](native-app.md) keep their meanings: session clock,
heard time, song time, Delayed listening, zen mode, stride, stomp, gait,
`knownAt`, committed beat.

New terms:

- **Run clock:** seconds of the run, 0:00 to 30:00. It advances with song time
  except while the world is frozen. The VS schedule reads it.
- **Freeze (pseudo-pause):** world time stops while song time keeps going.
  Nothing moves, spawns, cools down or deals damage, but enemies and the hero
  still bob in place on the beat. A level-up, a chest and the music stopping
  each freeze the world.
- **Grid step:** an eighth note (light weapons) or a beat (heavy weapons) of
  the committed beat grid. (Strides move on *stride steps*, the existing
  `stride_steps`, a different grid.)
- **Free pulse:** the stand-in grid when no beat is known (song gaps, beatless
  intros): the stride's `freePeriodS`.
- **Offer table:** the scripted list of the three cards offered at each level.
- **VS units:** VS's own data units. Damage and enemy HP are internal
  (shown ×10); player HP and contact damage are not scaled
  ([progression.md, Units](../survivors/progression.md#units)).
- **Plan keys:** `SF1`–`SF11` (foundations), `SH1`–`SH11` (the hook),
  `SW1`–`SW7` (the whole run), `SP1`–`SP5` (polish). Evidence goes in
  `native/evidence/<key>/`, lowercase, with a short summary in that key's
  subsection of [Bead notes](#bead-notes).

## Decisions

These were settled with the user. Executors treat them as fixed. Every other
choice is the executor's, recorded with its reasons in the bead's notes.

1. **The run is 30:00 of run clock across songs.**
   - A song change never ends the run.
   - Music stopping for more than 5 s freezes the world until it returns.
   - At 30:00 the run ends in a victory finale; it ends earlier only on death.
   - There is no Reaper.
2. **Movement keeps zen's beat strides.**
   - Holding a direction strides the hero on every stride step at a fixed
     average speed. Space stomps (stands still on the beat).
   - Speeds are VS's ratios: hero, enemies, Wings and pickup radius, all
     relative to VS's player base speed.
3. **Weapons use VS cooldowns snapped to the grid.**
   - A weapon fires on the first grid step after its VS interval
     (× Cooldown) has elapsed, on an unsnapped schedule, so snapping never
     loses throughput.
   - With no beat, it fires on the raw interval.
   - A shot on an accent gets a heavier look and sound but the same damage.
4. **The starting weapon is the omnidirectional sword spin.**
   - It spins on every beat, with damage scaled by tempo so that its damage per
     second matches VS's Whip at every level.
   - Amount adds spins on subdivisions. Its evolution is Bloody Tear.
   - Its radius starts at about 80 px and grows with levels and Area.
   - Accent beats get the heavy spin's look.
5. **Five weapons, six passives, VS's numbers.**
   - Weapons: Sword spin (Whip), Magic Wand, King Bible, Lightning Ring and
     Garlic, each with its VS evolution.
   - Passives: Hollow Heart, Empty Tome, Spellbinder, Duplicator, Pummarola and
     Spinach. Those are VS's 6 + 6 slots.
   - Offers come from a scripted table keyed by level.
   - Evolutions come from boss chests at minutes 10, 15, 20 and 25.
6. **Enemies are Mad Forest's roles, cast with LPC art** (two new pieces: a
   brute and thorn plants).
   - Contact damage as in VS, but delivered by a beat-synced lunge.
   - No ranged enemies.
   - At most 300 alive.
7. **The level-up screen is a freeze.**
   - The world and the run clock stop. The beat goes on: enemies bob in place,
     and cards flip in on beats.
   - Arrows highlight a card and Space or Enter confirms.
   - Keys held when the screen opens are ignored until released. There is no
     timeout.
   - Chests open with a roulette that spins for 2 bars and lands on a downbeat.
8. **Easy is measured.**
   - The schedule is Mad Forest's, with enemy contact damage ×0.5 and floor
     chickens.
   - A positioning bot survives the full run above 25% HP on a 30-minute test
     playlist.
   - An AFK bot dies before minute 10.
9. **The look is "neon over pixel".**
   - Characters and ground are LPC pixel art. Weapons and effects are HD,
     additive and glowing, made in-house as procedural and shader effects
     driven by the beat.
   - Sources: Kenney's CC0 Particle Pack and the LPC pixel impacts already
     baked. (The interview named LPC Revised's `FX/`, but it holds only
     splashes and water.)
   - No paid packs and no AI-generated art.
10. **Sound sits under someone else's music.**
    - The existing packs, plus Kenney's other CC0 sets and a few CC0
      showpieces. (The interview named freesound.org; its full-quality
      downloads need a logged-in account, so showpieces come from
      OpenGameArt's CC0 and CC-BY sounds unless an executor has one.)
    - Sounds are percussive and short.
    - Gem pickups climb a pentatonic chain.
    - Each weapon has fire, hit and evolved sounds.
    - Voices are budgeted, and the music is never ducked.
11. **The HUD is VS's.**
    - The XP bar on top, the timer, kills, slot icons, and an HP bar under the
      hero.
    - Damage numbers are shown ×10.
    - Fonts are Kenney's CC0 pixel fonts. Icons are game-icons.net glyphs
      (CC-BY 3.0) drawn neon.
    - A victory card shows VS's damage recap.
12. **The clock decides content; the music decides presentation.**
    - The VS schedule decides who spawns and when.
    - Zones, camera and lighting follow the music.
    - The one exception: VS's swarm events land on the first big loudness jump
      inside their scheduled minute.
    - Non-VS set pieces are retired from this mode.
    - It lives in a new level file, `levels/survivors.yaml`. The reactive level
      and legacy mode stay playable, frozen.
13. **Build order:** foundations, then the first five minutes polished to hook
    quality, then the whole 30 minutes, then a polish pass.
14. **No approval or playtest pauses of any kind.**
    - Checks only the operator can make go in
      [`operator-pending.md`](../../native/evidence/operator-pending.md).
    - The operator's feedback arrives as `velocity` beads whenever it comes.
15. **Process:**
    - A new epic, `hv-n7fn`, inherits the native epic's working rules
      (prototype, not specification; playable after every bead; headless
      checks; a deterministic, causal sim).
    - One cold warden review per milestone, run by the milestone's last bead
      on the milestone's commits.
    - Per-bead reviews only for the freeze's determinism (SF3) and the
      rollback runner.
    - Each bead records its commit SHA in its notes, so a milestone's
      review covers exactly its beads' commits.

## Goals

- A 30-minute run, start to victory card, on live music in Delayed listening,
  across a playlist's song changes and gaps.
- VS's progression felt as VS players know it:
  - kill pacing (one or two hits for early enemies, more later);
  - level pacing (about level 10 by minute 5);
  - weapon growth and evolutions, with bosses and chests on schedule.
- Every moving thing is musical:
  - hero strides, enemy gaits and lunges, weapon shots, hits, deaths and
    pickups land on the grid;
  - the level-up screen keeps the beat.
- The first five minutes look and sound good enough to be the demo's hook.
- A zen player with basic positioning reliably survives, proven by bots.
- The sim stays deterministic and causal. A 30-minute session replays bit for
  bit.

## Non-goals

- Build diversity: more weapons, passives, characters, stages, Arcanas, the
  gold shop, meta progression, Curse, Luck-driven loot, rerolls, skips or
  banishes.
- New rules beyond VS (that comes after this demo), and new judged play.
  Legacy mode gets no work.
- An endless session past 30:00, multiple stages, or a stage select.
- Ranged enemies, projectiles aimed at the hero, or bullet-hell dodging.
- Paid asset packs or AI-generated art.
- Track identity, per-song grades, or reading the playlist's length.
- Pausing the music. The app can't pause someone else's player, and the
  freeze is its stand-in.

## Related Information

- [docs/survivors/progression.md](../survivors/progression.md): VS's
  formulas and tables. The appendices cover
  [damage vs health](../survivors/appendix-damage-vs-health.md) (Mad Forest's
  schedule and enemies) and
  [XP and leveling](../survivors/appendix-xp-and-leveling.md).
- [native-app.md](native-app.md): the app's architecture, zen mode, Delayed
  listening, the runner, sessions and replay, and the workflow this epic
  inherits.
- [gameplay-redesign.md](gameplay-redesign.md): strides, stomps and the hero's
  movement model.
- [beat-steps.md](beat-steps.md): enemy gaits on the beat.
- VS itself: installed at
  `~/Library/Application Support/Steam/steamapps/common/Vampire Survivors`
  (Steam build 1.16.111). A full extraction, made for docs/survivors/, is kept
  outside the repo in `~/Documents/vs-extract/` (the JSON text assets in
  `out/`, the decompiled runtime in `rt/`, loaders in `scripts/`; see its
  README). SF1 reads the numbers the docs lack from it, never committing it.
- Code:
  - movement: `native/crates/sim/src/engine/hero.rs` (zen strides at
    `zen_step`), `strides.rs`;
  - enemies: `engine/foes.rs` (spawning, gaits);
  - weapons: `engine/combat.rs`;
  - gems and level-ups: `engine/pickups.rs` and `upgrades.rs`;
  - the director: `reactive/director.rs`;
  - the runner and snapshots: `eval/src/live.rs`;
  - the app's effects and sounds: `app/src/slice/{fx,lane,tier,gems,sounds,hud,scene}.rs`;
  - drawing: `render/src/{sprites,proc,post,font}.rs`.
- Content: `content/*.yaml`, `levels/reactive.yaml` (today's level),
  `assets-src/` (baker inputs and `licences.yaml`), `public/assets/` (the baked
  atlases and SFX), `public/reactive/<track>/` (corpus audio and streams).

## Design

### The run

**Start.** Enter or Space on the listening screen starts a run, as today. The
run clock starts at 0:00 on the first beat after the press, and the hero drops
in on that downbeat.

**The run clock.**
- It advances with song time while the world isn't frozen, and stops while it
  is.
- It drives the schedule, chest tiers and the victory.
- A run therefore takes 30 minutes plus time in menus, roughly 3 minutes over
  about 60 level-ups.

**Songs and gaps.** The run ignores song boundaries. Today a confirmed end
(about 3 s of quiet after an end) ends the run (`engine/plan.rs:530`). In
survivors mode it doesn't:

- **Short gaps** (no committed beat ahead, music below the gate for under 5 s
  of heard time):
  - The hero strides on the free pulse.
  - Enemies keep walking toward the hero at their VS speed, but smoothly
    (gaits need a beat). Their bob slows to the free pulse.
  - Weapons fire on their raw intervals.
  - **The free pulse** is new (`free_period_s` is read nowhere yet,
    `strides.rs:80`). It's phased from the last committed beat, at
    `freePeriodS`, until the next committed beat.
- **The music stopping** (below the gate for 5 s, or the analyzer's end
  confirmed):
  - The world freezes, with a "MUSIC STOPPED" banner. The run clock stops.
  - When the beat locks in again, the world resumes on the downbeat of the
    second bar after lock: one bar to hear the tempo, then go.
- **When it starts:** the freeze starts on the first stride step after the
  5 s rule's evidence is known (its `knownAt`). With Delayed listening's
  lookahead that is about 2 s into the heard gap. Direct listening is about 5
  s in. Nothing already settled changes.
- **Not a live-run hazard:** the Delayed watchdog (`app/src/slice/watchdog.rs`)
  watches capture stalls, not silence. SF3 checks that a silent gap in System
  mode doesn't trip it.

**Victory.**
- At run clock 30:00 the finale fires on the next downbeat: the finale blast
  clears the screen and the hero holds a pose.
- Three seconds later the victory card shows (a new `End::Victory`,
  `scene.rs:983`).
- Death before 30:00 shows YOU FELL with the same stats.

**Pausing and ending by hand.** A 30-minute run shouldn't die to a stray
click:
- **Esc and focus loss freeze the world** with a PAUSED banner, using the same
  freeze. Esc or a click resumes on the next beat.
- **Backspace** still ends the run.
- **An output device change, F2 or a source switch** still end it: the
  latency changes, and the mapping from heard time doesn't survive that.

**Sessions.** A 30-minute run must record and replay:

- "Watch my run"'s 900 s audio cap (`RUN_AUDIO_MAX_S`) rises to cover 40
  minutes. 40 minutes of `run_audio` as f32 is about 423 MB in RAM
  (`app/src/slice/mod.rs:1341`), so it's kept as i16 or read back from the
  session's recorded audio instead;
- the session's audio, stream and keys stay within the 20-session rolling cap.

### Movement

Zen strides stay exactly as they work today (`hero.rs`, `zen_step`). What
changes is speed:

- **The hero's average speed is VS's player base speed, converted** by one
  scale factor `px_per_vs_unit`. The same factor converts every enemy's VS
  speed, so the speed ratios match VS's exactly. SF1 extracts the base speed.
- **The scale factor is chosen so the hero's speed is close to today's 140
  px/s.** That keeps strides, the camera and the field's density in the range
  that already plays well. The executor records the factor and its reasons.
- **Wings** isn't in this demo, so move speed never changes. The pickup radius
  (Attractorb's stat) uses VS's base radius times the same factor.
- **While frozen,** held keys don't move the hero. The hero idles to the beat.

### Damage and stats

The hero has VS's stat block, with Antonio as the character: 120 Max HP and 1
Armor. Every stat combines as VS's does
([progression.md, How stats combine](../survivors/progression.md#how-stats-combine)):

```text
damage per hit = weapon power × Might
interval       = weapon interval × Cooldown        (then snapped, below)
amount         = weapon amount + Amount
area           = weapon area × Area
duration       = weapon duration × Duration
damage taken   = max(1, enemy power × 0.5 − Armor)  (the easy knob)
Max HP         = 120 × 1.2 per Hollow Heart level   (compounding)
regen          = 0.2 HP/s per Pummarola level
```

Antonio's own level bonus (+10% Might every 10 levels) is left out, so the
build's growth comes only from the demo's items.

The stats a demo item touches are Might, Cooldown, Amount, Area, Duration,
Max HP, Recovery and Armor. Others (Luck, Greed, Curse, Growth from items)
exist as fields at their base value, so VS formulas carry over verbatim. VS's
clamps apply.

**After a hit** the hero has VS's invulnerability window (SF1 extracts it;
today's is 0.5 s).

**Knockback** follows each weapon's VS knockback against each enemy's VS
resistance. It's applied on the next sixteenth so the horde recoils on the
grid.

### Weapons fire on the grid

```text
ready_k   = ready_(k-1) + interval × Cooldown     (unsnapped: never from fire_(k-1))
fire_k    = first grid step at or after ready_k
            (eighth note for light weapons, beat for heavy ones;
             with no known grid step ahead, fire_k = ready_k)
```

- **The schedule is unsnapped,** so snapping delays each shot by less than
  one grid step but never accumulates. Snapping from the last *shot* instead
  would lose up to 42% (Garlic at level 8, 1.0 s, on 70 BPM beats fires every
  1.71 s).
- **An interval shorter than a grid step** puts several shots on one step,
  staggered on sixteenths. Holy Wand at 0.3 s against 0.43 s eighths fires
  one or two per eighth.
- **Throughput matches VS exactly over time** at any tempo. Cooldown passives
  still matter continuously. SF5 tests it at 70, 128 and 174 BPM.
- The grid is the committed beat grid. It's known before heard time in
  Delayed listening, and at least one beat ahead in Direct listening. A shot
  is decided only from entries already known (causality).
- **While frozen,** the schedule is held and resumes from where it was in run
  time (see [Determinism and causality](#determinism-and-causality)).
- **An accent shot:** if `fire_at` lands on a forecast accent, the shot's
  `Moment` carries `accent: true`. The app draws and plays the heavy variant.
  Damage never depends on it.

### The roster

Numbers are VS's level tables (`WEAPON_DATA`), copied as data: numeric fields
add per level, other fields replace (`DataHelper.UpgradeJsonData`). SF1 puts
the full tables for these five weapons and their evolutions in `docs/survivors/`.

| Weapon | VS base | Fires | Look | Evolution (with) |
|---|---|---|---|---|
| **Sword spin** (start) | Whip | every beat (see below) | a 360° slash arc and afterimage trail; the oversize slash and a shock ring on accents | **Bloody Tear** (Hollow Heart): crits and heals on hit |
| **Garlic** | Garlic | heavy: on the beat | an aura ring that pulses on the kick, with a heat-haze distortion | **Soul Eater** (Pummarola): bigger, heals per hit |
| **Magic Wand** | Magic Wand | light: on the eighth | homing streaks at the nearest enemy, with sparks on hit | **Holy Wand** (Empty Tome): a rapid stream |
| **King Bible** | King Bible | its cycle restarts on a beat | orbiting blades (today's art) with afterimages, phase-locked to the beat | **Unholy Vespers** (Spellbinder): never stops orbiting |
| **Lightning Ring** | Lightning Ring | heavy: on the beat, strikes staggered on sixteenths | bolts from the sky on random enemies (reusing chain-lightning's VFX) with a flash | **Thunder Loop** (Duplicator): strikes twice |

**The sword spin** is the one weapon that doesn't use cooldown snapping:

```text
spins per beat   = amount (Whip amount + Amount): spin 1 on the beat,
                   the others on equal subdivisions of the beat
damage per spin  = Whip power(level) × Might × (beat length / 1.35 s)
radius           = 80 px × Whip area(level) × Area
```

- Damage per second is then `power × amount / 1.35 s × Might`, VS's Whip's,
  at any tempo.
- With no beat it spins on the free pulse, with damage scaled by the free
  pulse's period.
- **Hits to kill vary with tempo; time to kill doesn't.** A level-1 spin deals
  0.26 at 174 BPM, 0.35 at 128 and 0.63 at 70, so minute 0's 0.5-HP bats take
  two spins above 89 BPM, and skeletons (1.5 HP) take 3–6. Time to kill is
  VS's Whip's or better: two spins at 128 BPM take 0.94 s, against the Whip's
  one hit per 1.35 s. That's what Decision 4 bought. The bar for SH3 is time
  to kill, not hits.
- The radius's 80 px is a starting point. SH3 tunes it so the early game
  feels as vulnerable as VS's: enemies reach the hero's personal space before
  they die.
- Today's tier-3 spin (190 px from the start, `tiers.rs`) and the per-accent
  cleave go away in survivors mode.

**King Bible** keeps VS's damage, duration and cooldown. Its angular speed is
phase-locked so the blades pass the cardinal points on beats, as long as that
speed is within ±15% of VS's. Otherwise it keeps VS's speed. Each enemy uses
VS's per-weapon hit cooldown.

**Removed in survivors mode:** today's chain lightning, fire trail and the
light/heavy beat swings with their groove scaling. The finale blast stays for
the victory. The reactive level keeps all of them.

### Passives

| Passive | Per level | Levels | Evolution partner of |
|---|---|---|---|
| Spinach | +10% Might | 5 | none |
| Hollow Heart | +20% Max HP (compounding; heals the added amount) | 5 | Sword spin → Bloody Tear |
| Pummarola | +0.2 HP/s | 5 | Garlic → Soul Eater |
| Empty Tome | −8% Cooldown | 5 | Magic Wand → Holy Wand |
| Spellbinder | +10% Duration | 5 | King Bible → Unholy Vespers |
| Duplicator | +1 Amount | 2 | Lightning Ring → Thunder Loop |

Today's `max-health`, `magnet`, `might` and `armour` passives aren't offered
in survivors mode.

### Experience and level-ups

Copied from [the XP appendix](../survivors/appendix-xp-and-leveling.md):

- `total(L) = factor(L) × L²`, with factor 5, 6.5 and 8 from levels 1, 20 and
  40.
- +100% Growth while at levels 20 and 40.
- Gem value is `floor((rand + 0.5) × enemy xp)`, and no gem drops when that
  is 0.
- Gem colour by value: blue 1–2, green 3–9, red 10+.
- At most 400 gems on the ground. Past that, new XP goes to the gem farthest
  from the hero.

All randomness comes from the sim's seeded RNG.

Reaching a level's XP freezes the world at once (VS's behaviour) and opens the
card screen. Today's "wait for the next section" rule (`levelUpWaitS`) is gone
in survivors mode.

Several level-ups at once, or a chest followed by level-ups, chain: each opens
in turn while the world stays frozen.

### The offer table

The offers are scripted, keyed by the level being reached, in a new content
file `content/offers.yaml`:

```yaml
# Each level lists three cards; each card is a preference list: the first
# entry still offerable (owned and below max, or not owned with a free slot)
# is shown. "any" falls back to the fallback rule.
offers:
  2:  [[garlic], [magic-wand], [sword]]
  3:  [[magic-wand, garlic], [sword], [spinach]]
  4:  [[sword], [garlic, magic-wand], [hollow-heart]]
  5:  [[garlic], [sword], [magic-wand]]
  6:  [[spinach], [sword], [garlic]]
  7:  [[sword], [hollow-heart], [magic-wand]]
  8:  [[hollow-heart], [garlic], [spinach]]
  # ... written out to level 70 by SF7, column 1 following the pacing table
fallback: [lowest-level weapon owned, lowest-level passive owned, any offerable]
```

- **Card 1 is the intended path.** The bots pick card 1, so the table's first
  column is the run the pacing reports measure. The other two cards are
  designed so any pick stays viable.
- **A card names an item that isn't built yet?** It's skipped, so the table can
  be written in full in SF7 while weapons land in later beads.
- **Everything maxed:** the card heals 30 HP (VS offers gold or a floor
  chicken there).

**Pacing targets the table is written against** (VS's XP curve with Mad
Forest's kill volume; SF9's bots report them). These are estimates and an
upper bound: killing everything spawned gives about level 5 at 2:00 and 14 at
5:00. They're never a gate. When the bots' measured pace differs, the offer
table is rewritten to the measured pace (and the chests' expected evolutions
with it), with the numbers in the notes.

| By minute | Level about | Owned |
|---|---|---|
| 2 | 5 | Sword 2+, Garlic, Magic Wand |
| 5 | 10 | + Spinach, Hollow Heart |
| 8 | 15 | + King Bible, Pummarola |
| 10 | 18 | Sword 8 with Hollow Heart (chest: Bloody Tear) |
| 12 | 22 | + Lightning Ring, Empty Tome |
| 15 | 28 | Garlic 8 with Pummarola (chest: Soul Eater) |
| 20 | 38 | Magic Wand 8 with Empty Tome (chest: Holy Wand); + Spellbinder, Duplicator |
| 25 | 48 | King Bible 8 and Lightning Ring 8 (chest: Unholy Vespers, Thunder Loop) |
| 30 | 55–65 | everything at max |

**The card screen** (app; the sim only sees keys):

- **Before the cards:** the world dims and desaturates slightly behind them.
- **Cards** flip in on three successive beats (or free-pulse steps), each with
  a hit sound on the grid.
- **Highlighting:** ← ↑ → highlight left, middle and right; Space or Enter
  confirms. Card 1 is highlighted at open. Keys held at open are ignored until
  released.
- **When a confirm counts:** the sim ignores confirms until the third card's
  beat. It computes that beat from the grid as the app does, so a confirm
  never picks a card the player hasn't seen.
- **Speculation:** the app opens the screen from speculated state, as soon as
  the runner reaches the level-up. In the rare case a rollback retracts the
  level-up (a late key changed the outcome), the screen closes with no pick.
- **Each card shows** an icon, the name, "NEW!" or "LV 3 → 4", one plain line
  of what changes ("+1 bolt", "−8% cooldown"), and a small mark when it
  completes an evolution pair.
- **On confirm:** the card punches, the others fall away, and the world
  resumes on the next beat.

### Enemies

Mad Forest's roles ([damage appendix, The enemies](../survivors/appendix-damage-vs-health.md#the-enemies)),
with VS HP, xp and speed (converted as in [Movement](#movement)) and VS
contact damage × 0.5:

| VS role | Ours | Art | Gait |
|---|---|---|---|
| BAT1–3 (small bats) | bat | existing | flutter on eighths |
| ZOMBIE | slime | existing | hop on every beat |
| GHOST (fast) | bee | existing | dart on sixteenths |
| SKELETON | skeleton | existing | march on the beat |
| MUDMAN1, MUDMAN2 | orc, orc-elite | existing | heavy step on beats 1 and 3 |
| WEREWOLF | orc-ember | existing | lunge forward on offbeats |
| XLBAT (giant bat) | bat at 2× scale, dark tint | existing | slow wingbeat per beat |
| XLMUMMY, XLMANTIS | **brute** | **new**: Source of Tales `skeleton_boss` (walk, slash, spellcast, hurt) | stomp on the downbeat with a dust shock ring |
| FLOWER (ring walls), XLFLOWER | **thorn plants** | **new**: LPC Revised `Terrain/plants_*.png`, animated in code | sway on the bar, close in on downbeats |
| Bosses (minutes 10/15/20/25) | minotaur, plus scaled and tinted brute and orc-elite variants | existing and new | charge on the bar, slam on accents |

**Attacks on the beat, VS's contact model.**

- An enemy within reach (touching, plus its lunge distance) winds up on the
  offbeat and lunges on the beat.
- The bite lands at the lunge's peak if the hero is still in contact, then the
  hero's invulnerability starts.
- No enemy bites more than once per beat, and an enemy out of reach never
  bites.

This keeps VS's rule (damage only from touching) while every attack lands on
the grid.

**Everything else that moves is musical:**

- walk cycles keyed to the gait;
- hit flash and knockback on the next sixteenth;
- deaths on the grid, with the death burst by kind;
- the whole horde squashing on the downbeat (today's fast lane, `lane.rs`).

**While frozen,** enemies hold their positions and bob in place on their
gaits, with no translation.

**Spawning** keeps today's placement (just off-screen, mostly ahead of the
hero, `foes.rs:189`) and its recycling of far enemies (`foes.rs:661`).

### The schedule

`levels/survivors.yaml` holds one entry per minute, 0 to 30, transcribed from
[Mad Forest's schedule](../survivors/appendix-damage-vs-health.md#the-mad-forest-schedule)
with the roles mapped as above. Each entry has `enemies`, `minimum`,
`frequency`, `bosses`, `events` and `treasure`.

The spawner is VS's (`Stage.HandleSpawning`):

- On every tick it spawns one of each roster enemy.
- While fewer than `minimum` are alive, it spawns whole rosters until they
  are.
- At most 300 are alive.
- Bosses spawn once at the minute's start. A minute omitting a field carries
  it over.
- Ticks and minutes read the run clock. The spawns themselves are snapped to
  the next grid step, so enemies arrive on the music.

Enemy HP follows VS:

```text
normal:        maxHp
level-scaled:  maxHp × player level     (bosses, flower walls, boss bats)
```

(Mad Forest has no stage or time HP multiplier, and the demo has no Curse.)

**The easy knobs,** in the level file:

- `contactDamage: 0.5`;
- floor chickens: brutes drop one 5% of the time and bosses always do;
- the "everything maxed" card heals 30.

SW7 may retune these, and the offer table, and nothing else, with the bots'
numbers in its notes.

The director's loudness-driven population, section-stepped rosters and set
pieces aren't used by this level.

### Bosses, chests and evolutions

- **Bosses** spawn on Mad Forest's minutes, with level-scaled HP and VS speed
  and damage:
  - the big four at minutes 10, 15, 20 and 25 (BOSS_XLMANTIS, BOSS_WEREWOLF,
    BOSS_XLMUMMY and BOSS_XLFLOWER roles);
  - the smaller level-scaled boss bats (BAT4, BAT5) from minute 1, as giant
    bats;
  - minute 5's XLMANTIS and minute 8's XLBAT as their roles.
  - SH10 brings the boss bats and chests into the first five minutes. SW4 adds
    the big four.
- **Boss entrance** (SP2):
  - a warning on screen two bars ahead, and a horn on the bar;
  - the camera eases out;
  - the boss lands on a downbeat.
- **Chests:** a boss drops one when VS's data says it carries one (SF1
  extracts which). Walking onto it freezes the world for the roulette:
  - It spins through item icons for 2 bars, with ticks on the eighths.
  - It lands on the downbeat with a beam and a burst.
  - The chest's tier sets how many items it holds (1, 3 or 5), from VS's
    `treasure.chances` for Mad Forest's minute.
  - Except the big four's chests, whose tiers `levels/survivors.yaml`
    scripts so the expected path happens: at least tier 1 at minutes 10, 15
    and 20, and tier 3 (5 items) at minute 25.
- **Each chest item:**
  - evolves a weapon that qualifies (weapon at level 8 and its partner
    passive owned), else
  - levels an owned weapon or passive (the offer table's fallback order), else
  - heals.
- **The expected path** evolves the sword at 10, Garlic at 15, Magic Wand at
  20, and King Bible and Lightning Ring at 25 (a tier-3 chest). The chest
  contents are deterministic from the sim's RNG.

### Swarm events

Mad Forest's events (bat swarms, ghost swarms, flower-wall rings) keep their
minutes. Within its minute, each fires on the **first loudness jump** (today's
`sectionRule`: a jump of at least 6 dB) known after the minute starts. If no
jump arrives by the minute's last 10 s, it fires then.

- A swarm crosses the screen in formation, keyed to its gait.
- A flower wall closes as a ring around the hero, using today's ring set
  piece (`pieces.rs`) with thorn plants.

Other set pieces (stampede, kick line, bee wedge, archer volleys, the
90-second minotaur) aren't used by this level.

### Presentation follows the music

- **Zones:**
  - today's five zone themes (`content/zones.yaml`) sweep in on detected
    sections, as today;
  - with no section for 4 minutes, the next zone sweeps in anyway, so a
    30-minute run tours all five;
  - the victory always plays in `infernal-grove`.
- **Camera and lighting:** zoom and lead by loudness (`director.camera`). The
  ambient pulse, bloom and light intensity follow the beat and accents.
- **The fast lane** stays (`lane.rs`): pulses, accent punches, onset bursts and
  the horde's squash and flinch.
- **The groove and tier look** go away in survivors mode, since zen pins them
  at the top. The hero's aura becomes a function of build strength (owned
  evolutions).

### The look: neon over pixel

Characters, enemies, props and ground stay LPC pixel art, nearest-sampled.
Every weapon, hit, death, pickup and UI accent is HD, additive and glowing,
pushed through the bloom and shockwave passes (`render/src/post.rs`) and pulsed
by the beat.

**Where effects come from:**

1. **In-house procedural and shader effects** (`render::proc`, new WGSL),
   driven by beat parameters (phase within the beat, accent, loudness):
   - the sword's arc trail;
   - Garlic's pulse ring and haze;
   - lightning bolts;
   - the wand's streaks;
   - the blades' afterimages;
   - evolution auras, shock rings and the chest beam.
2. **Kenney Particle Pack (CC0)** supplies the raw textures procedural effects
   layer on: sparks, smoke, flares, magic circles and slashes. It's added to
   the baker's `sources.json` and the effects atlas.
3. **The pixel impacts already baked** from `lpc-explosions` and
   `lpc-magic-pack` (`tools/baker/sources.json`) supply the pixel layer of hits
   and deaths. (LPC Revised's `FX/` holds only splashes and water, so it adds
   nothing here.)

**Polish budget, in order:**

1. the five weapons and their evolutions;
2. hits and deaths;
3. the level-up and chest moments;
4. boss entrances and the victory;
5. zone sweeps.

**Readability at 300 enemies and up to 400 gems:**

- Effects have a density budget. Per-hit effects shrink and merge as hits per
  frame grow.
- Damage numbers merge per enemy per beat.
- The hero, their HP bar and the gems always stay readable over effects.

### Sound

**Sources:**
- the existing packs (`assets-src/sounds.json`);
- Kenney's other CC0 sets: Sci-Fi Sounds, Digital Audio, and Casino Audio for
  the roulette (Impact and Interface Sounds are already in use);
- a few hand-picked CC0 or CC-BY clips from OpenGameArt for the showpieces
  (evolution, boss horn, chest open, victory). freesound.org's full-quality
  downloads need a logged-in account.

All go through `sounds.json` and `licences.yaml`.

**Rules:**
- **Percussive and short.** Sounds are transients, whooshes and crunches,
  because they sit under someone else's music in an unknown key. Sustained
  tones are kept to the gem chain and the level-up chime.
- **The gem chain:** each pickup within 0.3 s of the last steps one note up a
  major pentatonic scale (two octaves, then it holds). A pause resets it. It's
  VS's most addictive sound, and pentatonic clashes least with arbitrary
  music.
- **Per weapon:** a fire sound and a hit sound, a heavier variant for accent
  shots, and an evolved set.
- **On the grid:** sounds keep today's scheduling (`sounds.rs`: the next
  sixteenth at least `NEVER_LATE` after known, multi-kills summed). Delayed
  listening knows shots ahead, so they land exactly on their grid step.
- **Budget:**
  - a voice cap per category (weapons, hits, kills, pickups, UI);
  - kills summed into one multi-kill per sixteenth;
  - the effects bus sits at a fixed margin under the music's followed level.
  - The music itself is never ducked.

### HUD and UI

- **Layout (VS's):**
  - the XP bar full width along the top, with the level at its right;
  - the run timer top centre;
  - kills top right;
  - owned weapon and passive icons with level pips top left;
  - a small HP bar under the hero.
- **Removed in survivors mode:** the beat track and the groove meter.
- **Damage numbers:** ×10 like VS (a power-1 hit shows 10). They're small,
  pop on the hit, drift up and merge per enemy per beat. Bloody Tear's crits
  are bigger and gold.
- **Font:** Kenney's pixel fonts (CC0), baked to a glyph atlas by the baker.
  It replaces the 5×7 one-quad-per-pixel font (`render/src/font.rs`) for the
  game's text. The debug overlay may keep the old one.
- **Icons:** [game-icons.net](https://game-icons.net) (CC-BY 3.0), rasterized
  by the baker and drawn as glowing glyphs tinted per item.
- **Banners:** "MUSIC STOPPED", the boss warning, "VICTORY" and "YOU FELL".
- **The victory card:**
  - time, kills and level;
  - VS's per-weapon recap: damage, time held, DPS.
  - "Watch my run" stays (W on the end card).

### Determinism and causality

The survivors rules stay pure functions of the stream and the keys:

- **The run clock and freezes** are sim state. A freeze is entered at a sim
  time decided only from known entries and state:
  - a level's XP reached;
  - a chest touched;
  - the music stopped (gate evidence and the end rule, at their `knownAt`).

  It ends on a key event (a card or chest confirmed) or on the beat-lock rule
  (committed beats at their `knownAt`).
- **How the freeze fits the sim's clocks.** The sim already has world time
  `w`, warped by breaks, hitstop stops, dips and rate steps
  (`engine/mod.rs:880`, `:1416`, `:1489`). Its event queue is keyed on song time
  (`QKey`, `mod.rs:268`). So:
  - **The freeze has its own flag and is a warp of rate 0.** It does not
    reuse hitstop's `stop_start`/`stop_end`. A zen press during a stop ends
    the stop (`hero.rs:389`), and reusing them would let the first highlight
    key unfreeze the world.
  - **The run clock is its own state.** It advances by song time while not
    frozen. In survivors mode hitstop, dips and rate steps are off, so world
    time and the run clock move together outside freezes.
  - **Song-keyed world events are held while frozen:** weapon fires,
    appearances, waves, automatic events and spawner ticks. On resume, each
    is re-planned from its run-clock ready time onto the grid after the
    resume beat.
  - **Keys during a freeze** go only to the card state. They never reach
    `zen_taps`, `zen_stomp` or held strides, so a highlight or a Space
    confirm can't stride or stomp the hero after the resume.
- **Card picks are key events** in the sim's key queue:
  - Left, Up and Right highlight, and a new `InputKey::Confirm` (Enter, or
    Space while a card screen is open) picks.
  - Today's `InputKey` has only the four directions and Space
    (`movement.rs:23`), and session replay maps any unknown key name to Space
    without a warning (`eval/src/session.rs:371`). SF3 makes that parse
    exhaustive and failing, and updates the app's key array and the players
    crate.
  - Picks roll back like any late key and replay exactly.
- **The offer table, chest contents and gem values** come from the content and
  the sim's seeded RNG.
- **Snapshots stay cheap over 30 minutes.** Every 30 Hz snapshot deep-clones
  the state (`eval/src/live.rs:836`), and much of it grows with run length:
  - pools that only grow: `foes`, `swing_pool`, `waves` and `chains`
    (`foes.rs:124`, `combat.rs:251`, `combat.rs:375`, `combat.rs:803`);
  - state derived from the stream: two `SongMap`s (`st.song`,
    `st.sch.song`), `sch.grid_times`, `grid_slots` and the stride steps;
    `Judging`'s six vectors per 20 Hz tick (`reactive/judging.rs:17`), about
    42,000 ticks in 35 minutes; `breaks`, `strong_onsets`, `forecasts`,
    `punches` and `foe_samples`.

  SF10 bounds both kinds: free lists or compaction for the pools; shared
  immutable chunks (`Rc`/`Arc`) or trimming before the oldest snapshot for
  the derived state. Snapshot cost then tracks what's alive, not the run's
  length.

## Assets

| Need | Source | Licence | Notes |
|---|---|---|---|
| Hero, existing enemies, minotaur | already baked (ULPC, Source of Tales) | per `licences.yaml` | gaits and lunges are code |
| Brute | Source of Tales `sprites/monsters/skeleton_boss_*.png` + `skeleton_boss.xml` | CC-BY-SA 3.0 (dual GPL; we take CC-BY-SA) | on disk in `~/Documents/lpc/source-of-tales`; fallback: the ULPC generator's `zombie` or `muscular` body dressed as a mummy |
| Thorn plants | LPC Revised `Terrain/plants_*.png` | OGA-BY 3.0 | already listed in `licences.yaml`; animated by code (sway, scale punch) |
| Pixel impacts | already baked from `lpc-explosions`, `lpc-magic-pack` | per `licences.yaml` | nothing new |
| Glow textures | Kenney Particle Pack | CC0 | `sources.json` download |
| SFX | existing packs; Kenney Sci-Fi Sounds, Digital Audio, Casino Audio; OpenGameArt showpieces | CC0, CC-BY | `sounds.json`; audition before relying on a description |
| Fonts | Kenney Fonts (pixel faces) | CC0 | baked to a glyph atlas |
| Icons | game-icons.net SVGs | CC-BY 3.0 (credit each author) | rasterized by the baker; credits in `credits.json` |

Every new source goes through the baker and `licences.yaml`. A bake fails on
an unlisted or disallowed licence, as today. Beads touching the baker run
`npm ci` first (AGENTS.md).

**One intake, then serialized bakes.**
- SF11 brings every new third-party source in at once: sources, licences,
  credits, and the sprites and textures baked.
- Each bake rewrites the committed binary atlases (`public/assets/*-0.png`),
  which can't be merged. So every later bead that changes the baker's
  output serializes after the previous one: SF11 → SH1 → SH2 → SW3 → SP1.

## Checks

### The 30-minute test playlist

A committed playlist file `native/playlists/survivors-30.m3u` lists corpus
tracks from `public/reactive/` that add up to at least 33 minutes:

- tempos from about 70 to 175 BPM, with the beatless and accelerating tracks
  included (`accelerando-100-140`, `cc-kimiko-ishizaka-aria`);
- gaps of 2 s between tracks, plus one 8 s gap to exercise the music-stopped
  freeze.

The playlist names tracks, and the tools concatenate them.

- **File mode plays a playlist** (`--playlist <m3u>`): tracks in order, with
  the gaps as silence, as **one concatenated source** through the same
  capture-free path as `--file`. It never goes through the per-track
  `switch()`, which ends runs. The operator and executors can play a whole
  run without System mode.
- **Fast-forward** (`ps survive`) builds the run's stream from each track's
  stream, offset by its start, with gaps as no entries. It runs the run
  through `eval::live::Engine` headless at full speed with a bot's keys, so a
  run has a settled hash. (`players::drive` runs a bare `Sim`,
  `players/src/lib.rs:22`, which has none.)
  - **Streams:** by default each track's Rust-analysed stream with Delayed
    listening's stamps, cached locally per track (keyed by its audio and the
    analyzer's version).
  - **A committed fixture** holds the first 3 minutes of the playlist's
    stream, for the gate, which can't analyse audio in its budget.
  - Per-track streams don't model the analyzer's state across a track change,
    so the release stage also runs one sweep on a fully analysed
    concatenation.
- **Session files:** `ps survive --record <dir>` writes the run's stream and
  keys in the session format, with no audio, so `ps replay` can check a full
  30-minute run settles to the same hash without a 33-minute real-time
  recording.

### Bots

New virtual players in the `players` crate:

- **Positioning bot.**
  - Each grid step it strides away from the crowd-weighted centre of enemies
    within reach, plus a pull toward the nearest gem when no enemy is within
    a safety radius. It never stomps.
  - It picks card 1 on every screen, confirming one beat after the cards
    land.
  - It's deterministic for a seed.
- **AFK bot.** It never moves and confirms card 1.
- **Random-pick bot.** The positioning bot, but it picks a random card from
  the seed. Its results are evidence only, not a gate.

`ps survive --playlist <m3u> --bot <name> --seed <n> [--minutes <m>]` reports:

- survival time;
- lowest HP as a share of max, and HP over time;
- level by minute;
- each weapon's level at each chest;
- evolutions taken;
- peak enemies alive;
- the run's settled hash.

**Gating criteria (SW7 asserts them; earlier milestones report them).**
Only survival gates:

- **Positioning bot:** survives 30:00 with lowest HP above 25% of max, on one
  seed in each of the 3 playlist orders and on the continuously analysed
  source. (5 seeds each until ps-qtj.1 cut the release sweep to about 3
  minutes; run more by hand with `ps survive --seed`.)
- **AFK bot:** dies before minute 10 (report-only, from hand-run `ps survive
  --bot afk` reports; the release sweep no longer runs it).

**Pacing (report-only, never a gate).** Every sweep reports these against the
targets, and they guide the offer table, but no bead fails on them:
  - level at minute 5 (target 8–12);
  - each intended-path weapon's level at its chest (target 8);
  - level at 30:00 (target 50 or more).

**Where they run:**

- **The gate:** a 3-minute positioning-bot smoke run on one seed, on the
  committed fixture stream, asserting survival. Level ≥ 3 is printed as an XP
  sanity report; the operator’s only-survival-gates instruction keeps all
  level-at-time numbers report-only. SF9 measures it
  within the gate's budget.
- **The release stage:** the sweep, as `ci-full`'s `survivors` step: four
  30-minute positioning runs in parallel, about 3 minutes, in a lane beside
  the native tests.
  - **Budget:** 20 minutes, a hang's bound.
  - **QoS:** utility, like the corpus parity step (AGENTS.md: the background
    class keeps to the efficiency cores and is too slow for a release step).
  - It's skipped while its inputs (the sim, content, levels and the playlist)
    are unchanged.

### Scale

SF10 measures, with the evidence in `native/evidence/sf10/`:

- frame work p99 at 300 enemies, 400 gems and full weapon effects;
- the engine's step cost;
- rollback re-simulation CPU at minute-25 density;
- snapshot size at minutes 1, 15 and 30.

Today's 4 ms frame-work bar holds (a release-stage check,
`native/evidence/g5/runner.md`), and snapshot cost doesn't grow with run
length. SP5 re-measures with the final effects. Since 2026-10-27 the density
bench (`--bench-density`) is a measurement to run by hand, not a release-stage
gate: the operator judges game performance by play, not by blocking landings.

### Determinism and causality

- **K1's checks extend to survivors mode:**
  - causality by perturbation and truncation on the test playlist's first 3
    minutes;
  - streaming equals batch;
  - a freeze opened and closed by keys replays identically, with rollback
    across a freeze.
- **Session replay:** `ps replay` of a `ps survive --record` session (30
  minutes, positioning bot) settles to the run's hash. It's a release-stage
  check from SF9, run inside the sweep's step on its base-order run.

### Operator checks

Each milestone's last bead adds a row to `operator-pending.md` with the exact
command to play (File mode with the playlist, and System mode with Delayed
listening). Nothing waits on those rows.

## Risks

- **VS's numbers assume continuous movement and continuous contact.**
  Beat-quantized lunges bite at most once per beat. At slow tempos, contact
  damage rate drops below VS's. Mitigation: the bots measure it, and SW7 may
  retune `contactDamage` only, with numbers.
- **The schedule's density at 300 enemies may be unreadable** with HD effects
  on top. Mitigation: the effect density budget, and SP5's sweep at minute-25
  density.
- **Long sessions:** 30+ minutes of audio, stream and keys per session, and
  snapshot cost over a long run. Mitigation: SF10 lands before any H bead's
  content multiplies the state.
- **Song gaps in System mode** depend on the analyzer noticing silence and
  re-locking tempo. A playlist with crossfades may never stop. That's fine:
  the beat just changes. A gapless tempo change may glitch gaits for a bar.
  Mitigation: the test playlist includes accelerating and beatless tracks.
- **The tempo-scaled sword spin is weak per hit at fast tempos** (0.26 power
  at 174 BPM). Minute 0's 0.5-HP bats take two spins above 89 BPM, and
  skeletons take up to six. Time to kill still matches VS's Whip. Mitigation:
  SH3 measures time to kill at 70, 128 and 174 BPM. If fast tempos feel weak
  in the operator's play, that is a `velocity` question about Decision 4, not
  a silent formula change.
- **Copying VS's numbers verbatim** is for the developer's own build. The
  numbers are documented as tables. Raw game data files are never committed.

## Alternatives Considered

- **Weapons on pure musical intervals** (every beat, every bar): rejected.
  Throughput would scale 2.4× between 70 and 170 BPM within one run.
- **Smooth VS gliding with only beat animation:** rejected. Striding on the
  beat is the fantasy, and zen already makes it effortless.
- **A random VS-style offer pool:** rejected for the demo. A script makes the
  hook reliable and the bots' pacing checks meaningful.
- **Run length tied to the playlist:** rejected. We can't read the
  playlist's length, and VS's schedule is by the minute.
- **Paid itch.io FX packs or AI art:** rejected. Licences outside the
  manifest, and coherence.
- **Music-driven content (loudness-scaled hordes, section rosters):**
  retired for this mode. Balance would vary by track, and VS's schedule is the
  reference.

## Implementation Plan

Tracked by epic `hv-n7fn`. Each bead delivers through its own Tollgate
worktree and passes the gate stage (AGENTS.md). Plan keys map to bead IDs in
the table.

| Key | Bead | Work | Needs |
|---|---|---|---|
| **F: Foundations** | | | |
| SF1 | `hv-n7fn.1` | Document VS's missing numbers in `docs/survivors/`, read from `~/Documents/vs-extract/`: player base speed, pickup radius, i-frames, knockback and resistances, full tables for the 5 weapons, 5 evolutions and 6 passives, which bosses carry chests, Mad Forest's events and treasure chances, floor chicken rules | none |
| SF2 | `hv-n7fn.2` | `levels/survivors.yaml` (loadout: the sword alone) and survivors mode (the app's default with zen), the run clock, song changes not ending the run, `End::Victory` at 30:00, the 40-minute session and replay audio | none |
| SF3 | `hv-n7fn.3` | The freeze: its own flag at warp rate 0, held and re-planned world events, the music-stopped freeze and beat-lock resume, the free pulse, Esc and focus loss as PAUSED, `InputKey::Confirm` and exhaustive replay parsing, keys routed to the card state; determinism and rollback tests (cold review) | SF2 |
| SF4 | `hv-n7fn.4` | VS stats, damage and contact formulas, i-frames, knockback, the XP curve, gem values, colours and the 400 cap; hitstop, dips and rate steps off in survivors mode | SF1, SF3 |
| SF5 | `hv-n7fn.5` | The weapon framework: VS level tables as data, the unsnapped cooldown schedule on grid steps, several shots per step, accent moments, slots; the sword spin on it with tempo-scaled Whip damage; the old swings, cleave and groove scaling off in survivors mode | SF1, SF4 |
| SF6 | `hv-n7fn.6` | The VS spawner and all 31 minutes of the schedule transcribed, enemy roles with VS stats on existing art (stand-ins for the brute and plants), level-scaled HP, the 300 cap, the easy knobs; the director's population and set pieces off for this level | SF1, SF5 |
| SF7 | `hv-n7fn.7` | `content/offers.yaml` to level 70, the offer rules and fallback, level-up freezes and chaining, confirm after the third card's beat, a plain card overlay | SF3, SF4, SF5 |
| SF8 | `hv-n7fn.8` | File mode `--playlist` as one source, `native/playlists/survivors-30.m3u`, per-track stream cache, the committed 3-minute fixture stream, `ps survive`'s fast-forward runner through `Engine`, `--record` session files | SF2 |
| SF9 | `hv-n7fn.9` | Positioning, AFK and random-pick bots; `ps survive` reports; the gate's 3-minute smoke; the release stage's sweep and 30-minute replay steps (survival report-only) | SF6, SF7, SF8 |
| SF10 | `hv-n7fn.10` | Scale and soak: bounded pools and stream-derived state, snapshot cost flat over 30 minutes, 300 enemies and 400 gems benched, rollback CPU at density; F's warden review | SF5, SF6, SF9 |
| SF11 | `hv-n7fn.11` | Asset intake: Kenney Particle Pack, Kenney Sci-Fi, Digital and Casino sounds, Kenney pixel fonts, game-icons.net SVGs, Source of Tales' `skeleton_boss`, the plant sheets; licences, credits, a first bake | none |
| **H: The hook (minutes 0–5)** | | | |
| SH1 | `hv-n7fn.12` | The HUD in VS's layout, the font atlas and neon icon glyphs drawn, damage numbers ×10, the HP bar under the hero; the beat track and groove meter off in survivors mode | SF4, SF5, SF11 |
| SH2 | `hv-n7fn.13` | The sound foundation: the survivors palette, voice budgets per category, the effects bus under the music, the pentatonic gem chain | SF4, SH1 |
| SH3 | `hv-n7fn.14` | The sword spin polished: arc trail and afterimage shader, the accent heavy spin, time to kill measured at 70/128/174 BPM, the radius tuned, its sounds | SF5, SH2 |
| SH4 | `hv-n7fn.15` | Garlic: VS table, beat pulse, aura and haze shader, knockback, sounds | SF5, SH2, SH3 |
| SH5 | `hv-n7fn.16` | Magic Wand: VS table, homing and pierce, streaks and sparks, sounds | SF5, SH2, SH4 |
| SH6 | `hv-n7fn.17` | Early enemies on the beat: bat, slime, bee and skeleton gaits, the offbeat wind-up and on-beat lunge bite, hit flash and knockback on sixteenths, deaths on the grid | SF6 |
| SH7 | `hv-n7fn.18` | The card screen polished: dim and desaturate, cards flipping on beats, highlight and confirm, the world bobbing behind, level-up burst and chime | SF3, SF7, SH1 |
| SH8 | `hv-n7fn.19` | Gems and pickups: VS gem colours and sizes, magnet radius, floor chickens, the gem field at the 400 cap | SF4, SH2 |
| SH9 | `hv-n7fn.20` | The six passives: stats, offers, icons, card text | SF4, SF7, SH1 |
| SH10 | `hv-n7fn.21` | Boss bats and minute 5's and 8's bosses, chests from VS's data, tiers, the 2-bar roulette freeze, item resolution (evolutions fall back to levels until SW5) | SF6, SF7, SH6, SH7 |
| SH11 | `hv-n7fn.22` | The hook pass: minutes 0–5 tuned end to end (the drop-in, schedule, offers 2–12, chests), stills and a capture in evidence, the operator row; H's warden review | SH1–SH10, SF9, SF10 |
| **W: The whole run** | | | |
| SW1 | `hv-n7fn.23` | King Bible: VS table, the beat-locked orbit, afterimages, sounds | SF5, SH2, SH5 |
| SW2 | `hv-n7fn.24` | Lightning Ring: VS table, sky bolts staggered on sixteenths, flash, sounds | SF5, SH2, SW1 |
| SW3 | `hv-n7fn.25` | The rest of the roster: orc, orc-elite, orc-ember, giant bat, the brute and thorn plants (baked in SF11), each with gait and lunge; SF6's stand-ins replaced | SH6, SH2 |
| SW4 | `hv-n7fn.26` | The big four bosses at 10/15/20/25, level-scaled, charging on the bar and slamming on accents; their scripted chest tiers | SW3, SH10 |
| SW5 | `hv-n7fn.27` | The five evolutions: Bloody Tear, Soul Eater, Holy Wand, Unholy Vespers, Thunder Loop, with their logic and base look, granted by chests | SH3, SH4, SH5, SH9, SH10, SW1, SW2 |
| SW6 | `hv-n7fn.28` | Swarm events: bat and ghost swarms and flower-wall rings, on the first loudness jump in their minute | SF6, SW3 |
| SW7 | `hv-n7fn.29` | The 30 minutes checked end to end and the victory finale; the balance sweep against the survival gates, pacing reported (easy knobs and offers only), the release step asserting; W's warden review | SW1–SW6, SF9 |
| **P: Polish** | | | |
| SP1 | `hv-n7fn.30` | Evolution showpieces: each evolution's transformation moment and upgraded visuals, and showpiece sounds from OpenGameArt | SW7, SW3 |
| SP2 | `hv-n7fn.31` | Boss entrances, warnings and deaths; the chest showpiece (beam, burst, roulette polish) | SW7 |
| SP3 | `hv-n7fn.32` | Zones across the run (the 4-minute fallback, infernal victory), camera and lighting by loudness, swarm-on-drop look, the aura by build strength | SW7 |
| SP4 | `hv-n7fn.33` | The victory card and per-weapon damage recap; YOU FELL with the same stats | SW7 |
| SP5 | `hv-n7fn.34` | The final sweep: hits, deaths and readability at minute-25 density, the effects' density budget, the scale re-bench; P's warden review | SP1–SP4 |

### Order and parallelism

- **Two executors fit:** one on the sim side (`sim`, `eval`, `players`, `cli`)
  and one on the app side (`app`, `render`, `audio`, the baker).
  - **Sim side:** SF2 → SF3 → SF4 → SF5 → SF6 → SF7, then SF9 and SF10. They
    share the step loop and `SimState` in `engine/mod.rs`, so they run in that
    order.
  - **App side meanwhile:** SF1 (docs), SF11 (assets), SF8 (playlists), then H's
    app beads as their needs land.
- **Weapons serialize** SH3 → SH4 → SH5 → SW1 → SW2. They share the weapon
  dispatch in `combat.rs` and the weapon drawing in `tier.rs`.
- **Bakes serialize** SF11 → SH1 → SH2 → SW3 → SP1 (the binary atlases).
- **Ready-bead order:** `ci-fix`, then `velocity` (the operator's feedback),
  then this table's order.
- **The app stays playable** after every candidate. Every bead writes its run
  command in its notes. A whole run plays with:

  ```sh
  cargo run --release -p app -- --playlist native/playlists/survivors-30.m3u
  ```

  (from `native/`, once SF8 lands; `--headless --seconds N` for checks).
- **Reviews:**
  - one cold warden review per milestone, run by its last bead (SF10, SH11,
    SW7, SP5) on the commits its beads recorded;
  - a per-bead review only for SF3 (the freeze's determinism) and any change
    to the rollback runner.
- **No stops for the user.** Feel questions go to the operator's play, as
  `operator-pending.md` rows and later `velocity` beads, never to a pause.

## Bead Notes

Each bead summarizes its measurements here in a few lines, in its own
subsection, linking `native/evidence/<key>/`. Beads never add sections
elsewhere in this document. Parallel beads would collide there.

### Tooling — epic-scoped waits (hv-n7fn.44)

[Kept evidence](../../native/evidence/tooling-scoped-await/README.md) records
Hive scoped-wait fix `f8f8d000b7078a8e90fea6394a4144d549d849fe` and its test-only
observation prerequisite `bc96e7878b85a9222cd42fd004304c7424bd3cab`.
Both passed their own Hive cold reviews, full gates and exact-commit Linux
Portability runs. The live source reload and scoped probe ignore unrelated ready
epics while preserving claims, prerequisites and the bound project/stream.
Use `executor await --parent hv-n7fn --include-label ci-fix --include-label velocity`;
its default deadline/interval remain. No game code or extra Survivors review
changes; this evidence commit SHA is recorded in the native bead notes.

### Tooling — capacity interruption recovery (hv-n7fn.37)

[Kept native records](../../native/evidence/tooling-capacity-recovery/README.md)
cover two monitor-reported provider-capacity interruptions. SF11 and SH6 resumed,
collected surviving checks and delivered; their exact promoted certificates,
staging ancestry and retained evidence hashes are verified. No new occurrence
or reproduced provider outage is claimed. AGENTS.md records checking ownership,
handles and candidates before replacement work. No game behavior changes; this
bead's source SHA is in its native notes.

### Tooling — tracked configuration staging (hv-n7fn.39)

[Kept reproduction](../../native/evidence/tooling-config-staging/README.md) shows
Git 2.54.0 returns an ignored-parent error after staging the tracked config;
`git add -u -- .tollgate/config.toml` succeeds and leaves runtime files ignored.
The standalone fixture needs no Tollgate process. AGENTS.md now records that
command and inspecting the index after an error. No tool defect, exclude change
or game change is inferred. This bead's commit SHA is in its native notes.


### Tooling — pristine worktree refresh (hv-n7fn.40)

[Kept evidence](../../native/evidence/tooling-pristine-update/README.md) records
cause fix `hv-mm03` in Tollgate, source/landed `114f93d2a5d56d804ea0da60bffd241c7c856ac5`.
A zero-source-commit `tg update` error now explains `git merge --ff-only staging`
and preserving saved work. Real Git tests prove unchanged branch/index/stash and
a usable refresh path; all 32 adapter tests, clippy and Tollgate certification pass.
The exact release was built (216.738 s), installed and restarted; binary hashes,
healthy doctors and an installed-service probe are retained. This bead changes
no game behavior. Its own evidence commit SHA is in the native bead notes.



### CI42

The native release density check failed at 4.89 ms p99. The repair skips
rebuilding a simultaneous event batch when its heap head already has the
correct due order; the original tolerance fallback remains. The focused
300-foe/400-gem, five-weapon applied-passive full native check gives **2.66 ms p99** under
the unchanged 4 ms bar. 8,575 queue selections match the prior algorithm;
a 30 s real-stream density replay matches 609,236 records exactly at lead 0.23
and 25 ms late input. The [CI42 evidence](../../native/evidence/ci42/README.md)
keeps failures, source scope, commands and final checks. No gameplay knobs
or benchmark workload change. SP5 still measures final evolved effects.

### SF1

The [feel appendix](../survivors/appendix-feel.md) records movement units and
ratios, pickup/invulnerability values, all 45 weapon combat rank rows, six
passives, enemy knockback/speeds, 21 Mad Forest event entries, treasure by
minute, chicken rules and gem attraction. Runtime-specific behavior and demo
adaptations are explicit: 120 ms damage lockout, Bloody Tear critical healing,
Soul Eater kill-heart drops, and Bible's interval/duration distinction. Source
hashes and numeric audits are in [SF1 evidence](../../native/evidence/sf1/README.md).
No game data files or behavior changes are introduced.

### SF1-swarm

The event-only BATSWARM/GHOSTSWARM combat rows omitted from the damage
appendix are now curated in `docs/survivors/appendix-feel.md`: HP 0.1/1,
contact power 1/5, XP 1/1.5, speed 700, fixed direction, no level scaling.
[SF1-swarm evidence](../../native/evidence/sf1-swarm/README.md) keeps source
hashes and selected numeric facts. No raw data or runtime source is committed;
SW6 consumes the documented values. No game/app behavior changes.

### SF2

Survivors is now the app's default level with the sword alone. Its own run
clock ignores song ends and ends on the next known downbeat after 30:00,
followed by a three-second finale and Victory. A short override reached Victory
headlessly in Delayed mode and replayed all 4,135 settled records identically;
a default run stayed alive past an eight-second file's end. Legacy and explicit
reactive launches still work. Replay retains the exact level source. Forty
minutes of retained i16 audio uses 211,680,000 bytes; short-session metadata
projects to about 50 MB more, below the 5 GiB store cap (sizing, not a soak).
See [SF2 evidence](../../native/evidence/sf2/README.md) for commands and limits.

### SF3

The freeze now holds world and run time while music continues. Escape/focus
pause and known-evidence silence share its state; card/chest hooks route keys
without leaking strides or stomps. The 8 s File-mode gap held run time at
17.900 s and resumed on the documented downbeat; replay matched all 98,692
settled records. Streaming and batch match, and a late Confirm matches 3,861
settled records after a 420 ms rollback across opening. Free-pulse movement,
held-key release, nested freezes and victory replanning have regression tests.
Cold review found three issues, all fixed with regressions: past zone events,
repeated silence inside another pause, and movement during the victory pose.
See [SF3 evidence](../../native/evidence/sf3/README.md); System capture and
visual/feel checks remain in operator-pending. Actual offers and chest timing
belong to SF7/SH10.

### SF4

Antonio's 120 HP/1 Armor, final-stat clamps, compounding Hollow Heart,
120 ms damage lockout and contact power ×0.5 before Armor are wired in.
One scale (154.26997245 px/U) gives hero 140 px/s, BAT1 54.88 and magnet 46.28.
Cumulative XP keeps the source's half-point thresholds and +1 Growth at 20/40;
earned levels wait for SF7's cards. Zero-value gems do not drop; 400 ground
gems merge into the farthest from the hero and retain their value/color.
Nine formula/integrated tests cover these rules, including fixed-step recovery
across a freeze and next-sixteenth recoil. A short headless Delayed run replayed
all 21,307 settled records identically (36 kills/full HP); prototype enemy HP/population
remain until SF6, so this is not a survival or pacing measurement.
See [SF4 evidence](../../native/evidence/sf4/README.md).

### SF5

All 45 weapon combat ranks are curated as data, checked against all 450 fields
in SF1's appendix. Unsnapped run deadlines retain throughput while shots align
to committed grid steps; collisions stagger on sixteenths. Twelve 600 s cases
at 70/128/174 BPM stay within 0.1167% of VS. The starting sword now spins on
beats/subdivisions (free pulse in gaps), with 80 px base radius and tempo-scaled
Whip damage; actual target hits over 60 s match DPS at base and non-default
stats. Accents change look/sound only. Old swings, cleaves and groove scaling
are off in survivors. Streaming/frame-cut/freeze recordings match, as does a
420 ms Confirm rollback. A headless run replays 21,149 settled records exactly;
[SF5 evidence](../../native/evidence/sf5/README.md) includes the offscreen still,
commands and limits. Other weapons enable their delivery in their planned beads.

### SF6

All 31 Mad Forest minutes and 20 source-stat roles are in the level/content,
with the 300-alive cap, whole-roster refill, grid arrivals, level-scaled bosses
and fixed chicken knobs. Brutes/plants use existing art stand-ins. Synthetic
source/cap/HP checks and headless File evidence are kept in
[SF6 evidence](../../native/evidence/sf6/README.md). Spawn-only smoke results
are explicitly separate from SF9's survival gates; normal early check players
can still die with the rank-1 sword before SF7 adds offers. The final spawn
smoke reached 309 s, observed every minute 0–4 roster, and peaked at 300 alive.
A frame-dependent landing order exposed by replay is fixed; the dense
off-grid-pause test matches 118,817 records between execution strategies. The
final File session replays all 3,781,538 settled records exactly; check-fast
passed in 49 s.

### SF7

The scripted preferences cover levels 2–70, skip unbuilt weapons, and choose
owned low-rank upgrades before new eligible items; all-maxed offers heal the
level's 30 HP knob. XP opens a rollbackable three-card freeze immediately;
chains stay frozen, held confirms cannot pick twice, and the third reveal
is the sim's confirm gate. The overlay is plain text/highlight on the existing
font; SH1/SH7 own icons, fonts and polish. Normal-damage File evidence replays
all 79,798 settled records; a real-XP regression applies nine picks once
across nine rollbacks (deepest 420 ms). See [SF7 evidence](../../native/evidence/sf7/README.md).

### SF8

File mode accepts the 38:14 silent-padded M3U as one source; a normal-damage
120 s headless check crosses the first track boundary and resumes. Rust
per-track streams are content/version cached, with a committed 180 song-second
fixture and a reusable Engine/Player driver for SF9. Audio-free sessions
replay their settled hash. The final held stand-in dies at 1,098.023 run seconds
(level 10, peak 300), so those numbers are not a survival gate. A separately
labelled damage-exempt runner soak exercises the full clock/replay path.
The playlist check also repaired a false MUSIC STOPPED hold: known energy
establishes silence, while beat confidence still gates re-lock. See
[SF8 evidence](../../native/evidence/sf8/README.md) for interfaces, commands,
raw results and the per-track/gap approximation.

### SF9

The pure players crate supplies positioning, AFK and seeded random-pick bots.
Positioning holds at most two adjacent arrows per stride, flees a nearby
crowd-weighted centre and pursues gems only outside its safety radius. Every
bot releases movement during freezes and waits one heard beat after card3
before its single pick. Random picks are stable by seed and level when a screen
reopens. No game stats, offers or easy knobs change. The three-minute Rust
fixture smoke passes in 1.16 s: 169.121 run seconds after choices, full HP,
level 3 and peak 300. The level is a report, not a gate. All three bots replay
their keys and ignore the uncommitted input tail. The separate 20-minute
release step reports normal-damage runs across five seeds and three orders,
plus a continuously analysed source; SW7 enables only the survival assertions.
The complete matrix takes 18m 47s: 17/20 positioning runs finish, 13/20 also
stay above 25% HP; all 20 AFK runs die before 10:00. Seven failed survival
criteria remain reports; no values are retuned. A separate release step checks
the full 30-minute recorded-session replay without spending the matrix budget.
The normal-damage reverse-order recording replays all 29,528,212 records
exactly at minimum HP 75%; its replay uses 4.67 GiB peak footprint, an explicit
before-SF10 baseline. Final fast check passes in 27 s.
See [SF9 evidence](../../native/evidence/sf9/README.md).

### SF10

Live pools retire objects behind monotonic IDs; stream histories share pages,
scratch geometry stays outside snapshots, and displayed-prefix caches bound
rollback/view work. F's cold review found three issues, all fixed: finite
beatless pickup timing, no-grid victory, and replay moment retention. Final
integration also rejects retired missile targets, restores shared vacuum
handling and culls distant Bible collision candidates before the unchanged
contact test.

With all five rank-8 base weapons and six applied max passives, 300 foes/400
gems give **3.38 ms frame-work p99** at 1080p. Snapshot copying is
262,865 / 273,063 / 264,560 bytes at minutes 1/15/30 (30/1 = 1.006×); the
source-scoped age data precedes only the stateless collision optimization.
The normal 30-minute recording replays all 31,399,652 records exactly. The
41-case matrix takes 276.9 s; seven AFK cases remain report-only SW7 findings.
No knobs or offers change. File/System feel checks are operator-pending.
Source SHAs, commands, raw outputs and corrected historical fixture scope are
in [SF10 evidence](../../native/evidence/sf10/README.md). SP5 remeasures final
evolved effects; Tollgate certifies integration with later commits.

### SF11

The intake adds the brute's real walk/attack/death art, four spiky plant
variants, nine Kenney particle textures, two pixel-font faces (95 ASCII
glyphs each), all sixteen item icons and fifteen new CC0 SFX. Each icon's
author is credited, and font/icon baseline and tinting interfaces are in
[SF11 evidence](../../native/evidence/sf11/README.md). A kept raster audit
confirms all thirteen existing sprites, 57 effects and 22 props are unchanged.
Nothing new is wired into gameplay; SFX listening is in operator-pending for
SH2 and the consuming presentation beads.

### Tooling: level-only checks (`hv-n7fn.36`)

Survivors-only level edits now select the native section and every affected
crate. The selectors cover YAML custom levels, including moved/deleted paths,
while unrelated files still skip native checks. Seventeen selector/purity tests
pass, including the real POSIX section selector and Cargo-metadata probes.
See [tooling evidence](../../native/evidence/tooling-level-checks/README.md).

### Tooling: cold continuous preparation (`hv-n7fn.41`)

The continuous Rust analyzer prepares its independent hindsight models in
four bounded jobs, reducing in model order. Live and per-track defaults stay
inline. Replay implementation lives outside the analysis cache fingerprint;
conversion, analyzer/model and audio inputs remain covered. Upstream stage
checks and a live-delay PCM comparison pass; measurements and the original
timeout/profile are in [repair evidence](../../native/evidence/tooling-cold-preparation/README.md).
The cold 41-case matrix completes in 670.8 s, including 576.5 s continuous
preparation, within the unchanged 20-minute budget. All positioning cases pass;
six AFK cases remain report-only SW7 findings. No easy knobs or offers change.

### SH1

Survivors now draws the Kenney pixel font, neon item icons and rank pips in
six weapon/six passive slots, XP/level, run timer, kills and HP under the hero.
Damage labels use applied Fate damage ×10, merge per enemy/beat and cap at
128. Minute-zero and full-slot offscreen captures are kept in
[SH1 evidence](../../native/evidence/sh1/README.md); the latter is a synthetic
layout/critical-style fixture, not gameplay progression. SW5 owns wiring real
critical status. A pre-change recording and a fresh 20-second normal-damage
File run replay the same hashes; presentation metadata preserves the old hash
encoding. Readability during play remains an operator row.

### SH2

The survivors palette uses existing baked samples, with duration-aware caps
[weapons 4, hits 6, kills 2, pickups 3, UI 3]. Pickup records feed a speculative
C6–C8 major pentatonic chain with a 0.3 s reset, preserving settled hash bytes.
The effects ceiling sits 12 dB below followed music RMS; the music bus never
ducks, and reference/calibration clicks remain audible without music. A silent
recording check reaches minute 25 with 300 enemies: 8,541 starts, no late
skips or mixer drops, category peaks [4,3,2,3,0]; a dense-input regression
fills the other categories. This is a damage-exempt capacity check, not a
survival or listening verdict. Review fixes cover calibration and a loudness
step-down. See [SH2 evidence](../../native/evidence/sh2/README.md), including
operator audition and downstream palette interfaces.

### SH3

The sword now draws a procedural sweep with fading afterimages; accents add a
wider gold spin and shock ring. Fire keeps its actual subdivision, while hit
sounds project from existing grid-aligned reactions without new hash records.
At 70/128/174 BPM, rank-one bat full-cycle TTK is 0.857/0.938/0.690 s and
skeleton TTK 2.571/2.344/2.069 s, below the analytical Whip reference of
1.350/2.700 s. The stationary test includes one initial attack period; it is not
VS runtime parity. Radius alone changes from 80 to 48 px: a 96-case approach
probe shows closer kills/contact exposure, not a whole-run balance verdict.
[SH3 evidence](../../native/evidence/sh3/README.md) keeps the raw tables, stills,
silent capture, replay and benchmark scheduling comparison. Listening and
personal-space feel remain an operator row; SH11/SW7 own integrated balance.

### SH4

Garlic is offered by the existing level 2 preference and follows the hero as a
persistent aura. Each target is hit once per beat-snapped interval incarnation;
rank 1/rank 8 use source power 0.5/1.5, raw interval 1.3/1.0 s and converted radius
49.366/98.733px. Surviving eligible hits increase knockback susceptibility by 0.3;
its own knockback remains 0, per the runtime source. The ring kick-pulses, brightens
on accents and adds local heat haze underneath the HUD. Sound cues project from
existing shot/contact records. [SH4 evidence](../../native/evidence/sh4/README.md)
keeps source-table/collision tests, rollback and fresh normal File replay, stills
and a silent synthetic presentation capture. Crowd balance and listening remain
later sweeps/operator rows; no offer preference or easy knob was retuned.

### SH5

Magic Wand is offered by the existing table and uses source power/Amount/pierce
at ranks 1/8, swept near-first collisions and bounded homing. Each burst starts
on the eighth grid after its unsnapped cooldown, then keeps source 100 ms repeats;
separately reserving every projectile on sixteenths would throttle slow-tempo
maxed builds. At 70/128/174 BPM, rank 8+Amount2/Cooldown0.6 emits 398/400/400 bullets
in 40 s versus 400 source-rate bullets, with only 4/2/2 pending. Raw remaining burst
delays hold through freeze. Causal projectile records drive ten-sample glow
trails; contact heads/sparks disappear on their grid reaction. [SH5 evidence](../../native/evidence/sh5/README.md)
keeps the source tests, real-XP rollback equality and explicitly labeled rank 8
stills/silent capture. The normal short File run acquires Garlic; Wand is
covered separately. Integrated balance, dense GPU timing and listening remain
later sweeps/operator checks; no offer preference or easy knob was retuned.

### SH6

Early bats, slime, bee and skeleton now step and animate on their assigned
subdivisions. Contact attacks wind up offbeat, attempt one bite on the beat's
peak and recheck contact; freezes require fresh notice. Flashes, recoil and
visible deaths land on sixteenths, preserving immediate logical combat/report
counts. Source VS stats and the easy knobs are unchanged. The minute-two
headless still has 143 enemies at run 157.414 s (damage-exempt presentation
only). A separate normal-damage File run replays all 79,862 settled records
exactly, with 55 kills and full health; its duration is not a survival verdict.
Five focused regressions cover six gaits, finite startup motion, contact,
freeze, grid invalidation and an uncommitted-tail poison check; Live density
rollback matches 69,420 records. See [SH6 evidence](../../native/evidence/sh6/README.md).

### SH7

The card screen now dims/desaturates the world, lands three icon/text flips
on the sim’s reveal beats, marks owned evolution pairs, highlights selection
and punches the chosen card while the others fall away. Pending sound cues
share SH2’s UI budget and cancel on retraction; the sim’s keys, offers and
next-beat resume are unchanged. The kept 66-frame offscreen sequence holds
the run clock at 1.658875 s through open/confirm and observes resume at song
3.325 s. Its rapid-XP/durable-enemy setup is an in-memory presentation fixture,
not pacing or survival evidence. See [SH7 evidence](../../native/evidence/sh7/README.md)
for stills, the silent capture and tests. Feel and listening remain an operator row.

### SH8

Gems now show the source blue/green/red value bands, bounded value-scaled glyphs
and compact beat glows below actors. At 61.708 px combined magnet/body overlap,
snapshot-owned motion gives the source 60 Hz outward kick and accelerating
pull; physical collection queues XP/chime on the next sixteenth. Floor food
has a pixel sprite, actual grid-heal feedback and 300 ms pickup immunity; the
level's existing drops and fixed 30 HP heal are unchanged. Tests cover geometry,
freeze, capped healing, immunity, the 400 cap and causal journal timestamps.
A normal-damage fixture reached run 1:00 at full HP with 291 kills/137 gems;
a separate 400-gem layout checks drawing hierarchy. A 60-second File check
replayed all 73,345 live settled records identically. See
[SH8 evidence](../../native/evidence/sh8/README.md); listening/feel remain an
operator row. Run from `native/` with `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed`.

### SH9

The six passives already flowed through SF4/SF7's typed stats, offers and UI.
SH9 verifies all 27 ranks through real revealed-card confirmations against the
source totals: Might 1.5, Max HP 298.5984, Recovery 1 HP/s, Cooldown 0.6,
Duration 1.5 and Amount +2 at their caps. Hollow Heart heals only the added
maximum; all other stats stay fixed and extra ranks are rejected. Two card
lines clarify that heal and Duplicator's projectile/spin effect. Two labelled
synthetic stills show all six cards, HUD icons/pips and true pair marks; no
offer, stat or easy knob changed. See
[SH9 evidence](../../native/evidence/sh9/README.md). Run from `native/` with
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.

### SH10

Scheduled bosses charge on native bars with contact-only damage and an accent
landing squash. Origin-owned treasure survives cap delays/recycling; source
rolls or scripted tiers give 1/3/5 items. The snapshot-owned roulette freezes
world/run time, ticks for two stable bars and grants sequential evolution,
owned-level or heal rewards on its landing downbeat. Evolutions stay disabled
until SW5 and limited to origins 10/15/20/25. A real-kill replay matches 8,741
records at lead 0.23/late 25ms. 87 sim tests, 19 live tests, a 460-frame silent
capture and the minute 1 headless playlist smoke are kept in the
[SH10 evidence](../../native/evidence/sh10/README.md); density p99 is 3.04ms.
No easy knob or offer changes. File/System feel checks remain operator-pending.
Run from `native/` with `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 90`.

### SH11

All 20 normal-damage positioning prefixes survive five run minutes, minimum
HP 72.92%, in the 41-case sweep (163.686s wall). Level at 5 varies 1–12;
10/20 reach the report-only 8–12 target. All 20 AFK prefixes remain alive at
5:00; the full before-10 verdict belongs to SW7. The [SH11 evidence](../../native/evidence/sh11/README.md)
keeps raw reports, source-scoped actual-play recordings, first-five stills,
a silent offscreen montage and H's indexed cold review/dispositions. The one
confirmed review issue is fixed: rollback-corrected cards and pair badges
refresh even when reveal beats stay unchanged, without a phantom confirm. Offers
and easy knobs remain SW7's work under AGENTS. File/System feel and music
listening are operator-pending. Run from `native/` with `cargo run --release
-p app -- --playlist playlists/survivors-30.m3u --headless --delayed
--press-beats --pick-first --seconds 30`.

### SW1

King Bible now emits source-duration cycles with evenly spaced orbiting blades,
Area-capped radius, per-blade periodic 1.7 s hit memory and KB 5. Cycles restart
on beats; a compatible cardinal lattice within ±15% of source angular speed
locks that incarnation, otherwise source speed stays. Snapshots own motion and
memory; shared bounded projectile records drive blade art/afterimages, cleanup
and exact fire/contact cues. Rank 1/8 tests retain damage 1/3, Amount 1/4,
duration 3/4 s and raw cycle 6/7 s. Real high-XP card choices acquire Bible and
settle 8,676 records identically with delayed confirms; this is not balance
proof. Kept normal/accent stills and silent clip are a labelled synthetic
no-enemy fixture. See [SW1 evidence](../../native/evidence/sw1/README.md), including
phase/tempo limits and source audit. Run from `native/` with
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.

### SW2

Lightning Ring now casts source-cadence volleys with 2–6 random visible-target
strikes on successive sixteenths and source circular AoE. Target/position choice
happens at each actual landing, with deterministic per-volley/index keys;
free subdivisions can be replaced causally by newly committed grids. Pending
ordinals survive freezes. Actual strike moments drive reused chain/sky bolts,
short flashes and typed sounds; empty attempts make no phantom strike cue.
Tests cover source ranks/radii, three tempos, late grid knowledge, visibility,
move-after-cast, fractional pause, non-default stats and deterministic frame
cuts. The actual-card rollback fixture retains 8,571 identical records with
22 delayed-confirm choices. Labelled synthetic normal/accent stills and a
short silent clip are in [SW2 evidence](../../native/evidence/sw2/README.md).
No offer/easy knob changed. Run from `native/` with
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.

### SW3

Eight remaining base roles now use their final art and gaits: heavy mudmen,
offbeat werewolf, dark giant bat, downbeat skeleton brutes and bar-swaying
thorn plants. Existing SH6 lunges remain; combat stats, radii, source scale
and spawn extents are preserved. No bake was needed: SF11 assets pass checks.
The GPU alpha-mask regression keeps plant hit flashes white and transparent.
[SW3 evidence](../../native/evidence/sw3/README.md) keeps an audit of all 20
roles, eight labelled approach stills and eight silent actual-gait clips
(52 frames each at 24 fps), plus source, lunge, render and replay outputs.
Captures disable damage/weapons and make no survival/balance claim. A normal
35 s File check reaches the real offer freeze and matches all 12,571 settled
replay records. SW4/SW6 reuse the art roles; SW7 owns the milestone review.
Run from `native/` with `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed`.
### SW4

The four late bosses use minotaur, crimson orc-elite and enlarged ochre/violet
brute variants, with two-beat walk cycles and SH10's bar charge/accent-only
squash. Source HP, speed, contact power, XP and resistance stay unchanged.
Minutes 10/15/20/25 script chest tiers 1/1/1/3, retaining source chance rows.
Twelve actual spawn/drop cases at player levels 1/7/35 check minute boundaries,
HP scaling and one physical scripted-tier chest. [SW4 evidence](../../native/evidence/sw4/README.md)
keeps source contracts and four inspected stills/silent clips. Captures inject
each holder at minute 0, normal source HP/XP, hero-damage exemption and
Might0/Area0; they make no late-run survival claim. SW7 reviews W, and the
operator row carries feel. Run from `native/`: `cargo run --release -p app
-- --playlist playlists/survivors-30.m3u --headless --delayed --press-beats
--pick-first --seconds 30`.

### SW5

The five evolutions now replace max-rank base slots through eligible late
chests. Source tables/pairs are numerically unchanged. Bloody Tear carries
actual critical damage/gold labels and its shared 8 HP / active-second heal;
Soul Eater applies the documented per-contact healing adaptation and grows
from actual recovered HP, adding its bonus after Might scales base power. Holy Wand uses source rapid bursts, Vespers bridges
grid gaps without stopping its orbit, and Thunder Loop returns to each
original strike point after its 1.5 s charge. Snapshot/freeze/causality tests
cover all five together; actual tier-3 roulette tests cover five sequential
replacements and early-origin exclusion. [SW5 evidence](../../native/evidence/sw5/README.md)
keeps source audits, raw checks and eleven labelled stills, including actual
combat criticals (forced Luck 10) and Thunder's fixed-point return. The demo's
Soul Eater healing scales with contacted density; SW7 measures that result.
SP1 retains the transformation/showpiece pass. No offer/easy knob changed.
Run from `native/` with `cargo run --release -p app -- --playlist
playlists/survivors-30.m3u --delayed`.
### SW6

Mad Forest's source event attempts now unlock on the first known ≥6 dB jump
inside their run minute, or at its last-ten-second fallback. Source repeats,
chance and expiry use the held run clock; grid admissions, fixed-heading
bat/ghost gaits, boss-preserving 300 cap and source-speed bar-step flower
walls are snapshot-owned. SF1-swarm supplies both event roles' source combat
rows. Six event regressions and all 20 live tests pass; the actual event replay
matches 96,846 records at lead 0.23 with 25ms-late keys. A normal positioning
prefix reaches 5:00 at level 10, minimum HP 92.08%, 850 kills (pacing only).
[SW6 evidence](../../native/evidence/sw6/README.md) keeps choices, raw outputs,
source scopes and three silent offscreen captures. SW7 owns the full sweep
and W review; real-music readability remains operator-pending. Run from
`native/`: `cargo run --release -p app -- --playlist playlists/survivors-30.m3u
--headless --delayed --press-beats --pick-first --seconds 30`.

### SW7

The [whole-run evidence](../../native/evidence/sw7/README.md) keeps the final
41-case sweep: all 20 positioning cases survive 30:00 with at least 82.986% HP;
all 20 AFK cases die in 225.383–329.030 s. Release now asserts survival only.
Pacing remains report-only: 18/20 reach level 8–12 by five minutes, levels are
15–20 at 30:00 and 24–40 after the victory clear. Seven origin-10 chests have
Sword 8; Garlic is 7/7/8 at three origin-15 chests; no origin-20/25 chest is
collected. The measured offer path prioritizes Sword 8 at player level 13,
then Garlic 8 at 21, with Wand first and Garlic at level 6. Easy knobs and VS
stats stay unchanged. Seven Bloody Tear/one Soul Eater runs do not establish
all five natural evolution paths or the original level-50 pacing target.

Full normal replay matches 28,645,747 records/hash `edd2c4598be943e1`;
local CI passes in 727.429 s with the source fingerprint unchanged. Base-five
300-foe/400-gem frame work p99 is 2.59 ms (max 5.82 ms); SP5 owns evolved
density. W’s cold review found one confirmed P2: Holy Wand repeats now divide
by projectile Speed (50 ms normally), with speed/pause regressions. Chest
reports retain physical identity/origin/tier across interrupted freezes.
The final 30-app-second headless smoke/replay passes; File/System feel stays
in operator-pending. From root after build: `native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u --headless --delayed --press-beats --pick-first --seconds 30`.
Commit and staging OIDs are recorded in hv-n7fn.29’s native notes.

### SP1

Presentation-only evolution showpieces add actual-reward, downbeat transformation
bursts, five distinct evolved effects and six short CC0 OpenGameArt transients.
Stable chest identity, owned inventory and pause clocks prevent early, repeated
or withdrawn celebrations; forecast state only supplies existing sound lead.
The ordinary weapons, simulation and weapon statistics are unchanged. The source
and landed commit SHAs are recorded in hv-n7fn.30's notes.

[Evidence](../../native/evidence/sp1/README.md) keeps five silent injected-loadout
clips (48 s/1440 frames), 16 inspected stills, CC0 provenance and sound timing,
61 app/5 content tests, clippy, a full local check (1075.223 s, unchanged source
fingerprint), and the headless run/replay. All 41 survival-matrix gameplay hashes
match SW7: 20 positioning passes/minimum HP 82.986111%, 20 AFK deaths before
329.030 s; pacing remains report-only. Base-five dense p99 is 2.95 ms; this does
not prove evolved density. The typed capture trigger is presentation evidence,
not natural evolution acquisition. SP5 owns P's cold review and evolved density;
subjective listening and feel remain in operator-pending.

Run from the root after building: `native/target/release/playlist-survivors
--evolutions-check native/evidence/sp1/captures`; normal play check:
`native/target/release/playlist-survivors --playlist native/playlists/survivors-30.m3u
--headless --delayed --press-beats --pick-first --seconds 30`.

### SP2

Boss notices and their short original horn begin two native bars before the
scheduled entrance on a steady committed grid. Camera zoom eases out through
the shared sim/view camera; bosses land visibly on the next downbeat along
their seeded approach ray. Held bosses keep the notice/zoom until every
announced arrival fits; freezes hold the camera and reanchor admission.
Source stats, attack AI and chest/reward rules stay unchanged. No known grid
keeps raw admission; tempo changes/freezes/cap holds can lengthen notice.
Typed, bounded cues drive landing/death rings and source-timed sounds. Death
presentation follows the actual grid reaction, including its existing chest
freeze hold. Physical chests gain a beacon/motes, settling roulette motion
and animated reward beam/burst. No asset bake.

[Evidence](../../native/evidence/sp2/README.md) keeps actual timing/cap and
late-key replay checks, decoded sound onset/retraction checks, and a labelled
silent real boss kill/drop/pickup/tier3 fixture. The empty-field synthetic
capture establishes presentation rather than dense survival or human mix.
SP1 retains evolution art/bake ownership; SP3 composes the authoritative
camera multiplier, SP5 owns density readability/performance. Play from
`native/`: `cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.
Commit/staging OIDs and cold-review disposition are in hv-n7fn.31's notes.

### SP3

The existing section sweeps now have their specified240-active-second
fallback, including the configured stand-in pulse without a beat. Pause
preserves the remainder. Victory starts an infernal-grove sweep and later
queued zones cannot replace it. Existing director zoom/lead and SP2's
boss multiplier remain authoritative; ambient/prop light and bloom follow
energy, beat and accents. Known positive loudness jumps add a short drop
pulse and at most64 trails on actual swarm actors. The hero's aura reads
owned evolution count0–5 from Inventory, replacing pinned tier styling;
base-only builds keep a small locating glow. Unknown tick/key/onset tails
cannot affect the current camera or lane.

[Evidence](../../native/evidence/sp3/README.md) keeps actual grid/free-pulse
four-minute tours, pause/finale/causality checks, per-zone stills and a sweep,
plus separately labelled inventory-only aura poses and synthetic source-swarm
captures. These establish presentation, not dense real-run performance or
human feel. No asset bake, source-stat/AI/offer/easy-knob retune or analyzer
change. SP5 owns the milestone cold review and density budget. Play from
`native/`: `cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.
Source/staging SHAs are recorded in hv-n7fn.32's native notes.

### SP4

VICTORY and YOU FELL now share the completed run's active time, kills,
level and per-weapon damage/time-held/DPS table. Existing logical hit values
and inventory changes drive the bounded recap in the rollback view; visual
hit echoes and the finale never double-count weapon damage. Rank upgrades
keep time, evolution closes the base interval, and lingering base attacks
retain identity. Menus/music freezes add no held time. Damage/DPS use the
same×10 human-facing scale as combat labels; held time shows tenths.

The first fatal Health record captures an immutable Summary in the rollback
view; confirmation delay cannot inflate time or damage. The completed Summary
survives Watch-my-run and an early replay stop, while replay retains its original
wall/song span. New runs
clear it. [Evidence](../../native/evidence/sp4/README.md) keeps an independent
actual-Sim applied-fate comparison, real late-offer rollback checks, and
labelled actual shortened-victory/forced-fatal-contact card stills. Fixture
loadouts/actors do not establish natural acquisition or balance. Eye/ear/W
feel remains operator-pending. One per-bead cold review covers the changed
rollback projection; SP5 also reviews the milestone. Play from `native/`:
`cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.
Source/staging SHAs and review disposition are recorded in hv-n7fn.33's notes.

### SP5

Final-evolution density uses the actual five evolutions and six applied max
passives, with300 minute25-roster foes and up to400 gems. Per-hit recipes
merge spatially/time-wise and shrink with recent hit density; the hero and
gem cores draw above additive effects, with HP/HUD and modal showpieces on
top. Damage remains per-enemy/per-beat; crowded labels share screen space.

An ephemeral ordered spatial broad phase avoids repeated Bible/Wand scans
without changing contact predicates, target ties or recording. Four actual
before/after runs match exactly at nondefault lead/late keys. Normal-priority
1080p frame p99 is 3.18ms stationary and 3.21ms moving with actual attraction/
pickups, below 4ms; slower background-QoS numbers are separately scoped.
[Evidence](../../native/evidence/sp5/README.md) keeps raw output, final-density
stills, snapshot/rollback remeasurement and P's review disposition. Synthetic
loadouts/HP/gems establish density, not balance or natural acquisition.
Operator eye/ear/System checks remain pending, with no pause. Play from
`native/`: `cargo run --release -p app -- --playlist playlists/survivors-30.m3u --delayed`.
Final source/staging SHAs and review findings go in hv-n7fn.34's native notes.

## Beyond This Plan

Named so this plan doesn't block them:

- Creative rules beyond VS: weapons that respond to the music's structure,
  accents that matter, judged-play bonuses in zen.
- More weapons, characters and stages, and a random offer pool.
- The endless session across a whole listening session.
- Tonal SFX tuned to the song's key, once the analyzer detects key.
