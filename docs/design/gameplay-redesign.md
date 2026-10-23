# Gameplay Redesign: Survive the Horde, Power Up on the Beat

## Introduction

Playlist Survivors is a browser rhythm game set to Offenbach's *Orpheus in the
Underworld* overture: a slow prelude with no steady beat, then the Infernal
Galop at 175–230 beats per minute. The player moves a hero with the arrow keys
or WASD through a horde of up to about 500 monsters, while the hero's sword and
other weapons attack by themselves.

The song's sections, which the rest of this document refers to by name:

```text
section          time        beats per minute
prelude          0:00-0:43   no steady beat
galop vamp       0:43-0:54   177
galop opening    0:54-1:10   175
theme A          1:10-1:20   184
theme A repeat   1:20-1:31   186
kick line        1:31-1:41   190
bridge           1:41-1:45   185
climax           1:45-2:05   191
coda             2:05-2:24   231, ending on the final chord at 2:19.6
```

A few terms come up throughout:

- **Meter:** the prelude is in 4/4 and the galop in 2/4, so a galop bar is two
  beats, about 0.65 s.
- **Song time:** seconds into the music. Every time in this document is song
  time.
- **Timing grid:** the points a section's rhythm is judged against, normally
  its beats (a section may use eighth notes or bars instead). Each point is a
  **grid time**. "On the grid" means on a grid time or one of its
  subdivisions.
- **Level script:** the file that describes the song's sections, enemy
  rosters and events in bars and beats.

There are three ways to see a run:

- **Play:** the keyboard drives the game live.
- **Watch the demo:** a recorded run by a **virtual player**, a deterministic
  program that presses keys the way a human would.
- **Watch my run:** the end card's exact replay of the player's own run.

The fantasy is a dance-fight with a horde, like a well-choreographed fight
scene. The hero moves and attacks to the beat, and so do the enemies. Much of
that is presentation: strikes land on the beat however the player presses.
Capturing that feeling matters more than anything else in the game.

The target is the feel of a late Vampire Survivors run, around minute 20:

- **Movement is the core skill.** Health is hard to recover, so avoiding
  contact is the main loop.
- **Positioning for attacks matters,** but less than avoiding hits.
- **Wide area attacks keep space** around the hero. Even a sword swing clears
  a broad arc rather than poking.
- **The reward loop** is collecting experience gems, levelling up, grabbing
  pickups and surviving situations that look overwhelming.
- **Standing still is allowed.**

Rhythm sits on top of that. Pressing on the beat makes attacks bigger, and late
in the song that size is what keeps the horde off the hero. Pressing at random
keeps a player alive early in the song, but not late.

### How play mode works today

Today's rules, which the problems below refer to:

- **Steps:** in a section with a steady beat, each press moves the hero one
  44 px step in 110 ms. Holding a key does nothing after the first step. In
  the prelude, held keys walk at 80 px/s.
- **Ratings:** each press is rated by its distance from the nearest beat:
  Perfect within 45 ms, Great within 90 ms, Good within 140 ms.
- **Swings armed by presses:** a press **arms** the sword, which then swings
  on the next beat at least 90 ms away. A bar without presses gets one idle
  swing. In the prelude the sword swings by itself on every beat.
- **One rated press per beat:** only the first press for a beat is rated and
  arms a swing. An extra press is allowed if it is a well-timed off-beat or a
  quick double tap; any other extra press **stumbles**: the hero doesn't step,
  the next swing is lost, and the groove halves.
- **The groove:** a meter that moves part of the way toward each rated
  press's accuracy and adds up to 20% damage.
- **Accents:** a Great press one beat before a big marked chord arms a
  **cleave**, a heavy swing with a shockwave.
- **Health:** 5,000. Each touching enemy bites at most every 600 ms. A
  **clean press**, Great or better, heals 15, and from the fourth clean press
  in a row (a **streak**) each heals 30.
- **Gems and level-ups:** gems fly to the hero on the next downbeat, and three
  level-ups at fixed moments each grant a weapon.

### What is wrong today

Today's play mode works against that feel. These measurements come from the
play-mode checks (`npm run check:play`) and from playtest logs. The **moderate
player** below is the virtual player that stands in for a moderately competent
human.

- **Speed is press rate.** Pressing on every beat at 185 beats per minute
  moves the hero about 135 px/s, and pressing once a bar about 68 px/s.
  Goblins run at 112 px/s, orcs 105, bats 165 and bees 180. Pressing faster
  than the beat is the only way to outrun the horde, and a slower song makes
  the gap wider.
- **Mashing nearly ties.** A virtual player pressing at random four times a
  second gets about 95% of the moderate player's kills and ends with 71–82%
  health. Its first press in each beat arms a swing, so it swings on every
  beat, just like an accurate player.
- **Presses that do nothing feel bad.** An earlier rule ignored a second press
  for the same beat, 36 times in one playtest log. In the latest log, 84 of
  165 extra presses stumbled (out of 472 presses in sections with a steady
  beat). Both read as dropped input.
- **Players press eighth notes.** In the same log, extra presses came a median
  176 ms after the beat's press, which is an eighth note at the galop's tempo.
  This is good, musical play.
- **Healing is the real defence.** The moderate player takes 2,650–3,050
  damage, many hundreds of bites, and heals it back, staying above about 90%.
- **The horde can't be avoided.** The **spawn director**, which keeps the
  population on the level's target curve, refills it on every beat from just
  outside the view, from every side, up to 500 enemies.
- **Attacks are short.** A light swing reaches 100 px, in a view 700–1,070 px
  wide depending on the camera's zoom.
- **Gems collect themselves,** so nothing pulls the hero across the field.
- **The run starts at the beginning.** The hero starts with only a sword and
  gains three weapons: an early-game arc, not a late run.

### The proposal in brief

The redesign keeps the music-locked presentation and moves survival onto
movement and space:

- **Fixed-speed strides.** The hero moves at one speed, whatever the tempo or
  press rate, in **strides**: quick eased hops like today's steps. A press
  strides at once, and a held key strides on every beat, or on every eighth
  note when the beat is slow.
- **Weapons fire by themselves on the beat.** Presses no longer arm swings.
- **The groove meter sets a tier.** On-beat presses fill a meter whose
  **tier**, 0 to 3, grows the area of every attack. **Stray presses**, extra
  presses that are neither on the beat nor a well-timed off-beat, drain it,
  and so does silence.
- **A small health pool.** About 100 health, a short invulnerability after each
  bite, and healing only from pickups and level-ups.
- **A readable horde.** A thinner steady stream, plus **set pieces**, scripted
  spawns timed to the music: closing rings, stampedes, a dancing kick line,
  diving bee wedges, archers and the boss.
- **Gems to walk to.** They stay where enemies die, and the radius that pulls
  them in grows with the tier.
- **Level-ups as a walk.** Each level-up raises three upgrade orbs around the
  hero, and walking into one takes it.
- **Rhythm decides the late game.** Mashing or holding keys leaves the hero at
  tier 0, which survives the early sections and falls in the climax.
- **One set of rules.** Watch the demo is re-recorded under the same rules.

### Decisions

These decisions were settled before this document was written:

- **Movement:** a fixed speed. Over any stretch of time the hero covers at most
  the distance of constant-speed walking plus one stride. Every press is an
  instant footfall; extra presses split strides instead of adding speed.
- **Power:** every weapon fires on its own beat whatever the player presses. A
  forgiving groove meter sets visible tiers.
- **Groove:** on-beat presses fill it, and an off-beat eighth note rated
  Perfect adds a little. It drains after a bar without filling, and stray
  presses take a chunk off it.
- **Loadout:** the sword, orbiting blades (blades circling the hero) and chain
  lightning (a bolt that jumps between enemies) from the start. Upgrades add a
  fire trail (burning ground left behind the hero) and evolve the other
  weapons into stronger forms.
- **Tier effects:** mostly area (reach, arc, orbit, jumps), a little damage,
  and a full spin at tier 3.
- **Health:** 100, invulnerable for 0.5 s after a bite, healed by pickups and
  level-ups only. No rhythm healing.
- **Horde:** the same peak of about 500, much of it arriving in musical set
  pieces.
- **Gems:** they wait to be walked to, the pickup radius (how close a gem must
  be to fly in) grows with the tier, each pickup chimes on the grid in the
  section's key, and vacuum pickups pull every gem in.
- **Level-ups:** three, at today's moments, each offering three orbs from a
  pool of weapon upgrades and stackable passive bonuses.
- **Accents (big marked chords):** the chord's heavy impact is the same for
  everyone. A press on the chord launches a shockwave, which starts on the
  eighth note after the chord.
- **Standing still:** a stomp (Space, or two opposite keys together) keeps the
  groove without moving.
- **Curve:** tier 0 falls in the climax. A sparse, sloppy but honest player
  survives.
- **Prelude:** a calm walk with no beat to judge.
- **Demo:** rebuilt on these rules. The forest corridor and the old step rules
  are removed.
- **Damage and groove:** damage never changes the groove.

### Goals

The finished design is judged against these goals:

- **No dead presses.** Every key press moves or plants the hero on the next
  displayed frame.
- **Speed independent of input.** However the player presses, the hero covers
  at most its walking distance plus one stride.
- **Every gameplay sound on the grid.** No percussive sound starts at the
  moment of a press.
- **Rhythm buys space, and space buys survival.** Late in the song only a
  player at tier 1 or higher keeps the horde off.
- **The target curve.** Moderate play ends with 40–70% health, sparse sloppy
  play with 10–40%, and mashing or holding falls between 1:45 and 2:05.
- **One set of rules** for play, Watch the demo and Watch my run.
- **Causality kept.** No game rule reads a press before it happens, as today.

## Related Information

Read [beat-steps.md](beat-steps.md) and [interactive.md](interactive.md)
first if you need today's rules in full:

- [Beat steps and a simulated playthrough](beat-steps.md): today's rules. It
  covers step movement, timing and groove, auto-aim, swings armed by presses,
  enemies and the virtual player. This design replaces its movement, its
  groove and its swing arming. It keeps auto-aim, the brief freezes on heavy
  hits, the simulation loop and the recording.
- [Interactive play](interactive.md): play mode. It covers the live loop with
  rollback and speculation, cancellable sounds, the open field (endless ground
  generated around the hero), health and the stumble rule. This design
  replaces the play-mode rules (one step per beat, stumbles, rhythm healing,
  the 5,000 health pool) and keeps the live loop and the open field.
- [Tech demo design](tech-demo.md): the song map, time warp, juice, sound
  mixing and renderer. All of it still applies.
- [README](../../README.md): commands, URL parameters and checks.
- [QA report](../qa/qa-report.md): how each manual QA item is checked today.
- [AGENTS.md](../../AGENTS.md): how changes are delivered.
- Playtest evidence: play logs from real play, saved by the dev server in the
  main checkout's `logs/play/` (not in git) and summarized by
  `npm run log:play`. See [`src/live/log.ts`](../../src/live/log.ts) for what a
  log records.
- Code most affected:
  - [`src/sim/simulate.ts`](../../src/sim/simulate.ts): the simulation:
    swings, presses, bites, healing, gems and level-ups.
  - [`src/sim/movement.ts`](../../src/sim/movement.ts) and
    [`src/sim/timing.ts`](../../src/sim/timing.ts): steps, walking, ratings and
    the groove.
  - [`src/sim/player.ts`](../../src/sim/player.ts): the virtual players.
  - [`src/planner/play-checks.ts`](../../src/planner/play-checks.ts) and
    [`scripts/check-play.ts`](../../scripts/check-play.ts): the play-mode
    checks.
  - [`src/planner/emit.ts`](../../src/planner/emit.ts): sound emission,
    including the palettes that transpose samples into the section's key.
  - [`src/runtime/hud.ts`](../../src/runtime/hud.ts): the HUD.
- Content: [`content/hero.yaml`](../../content/hero.yaml),
  [`content/enemies.yaml`](../../content/enemies.yaml),
  [`content/weapons.yaml`](../../content/weapons.yaml),
  [`content/formations.yaml`](../../content/formations.yaml) and the level
  scripts in [`levels/`](../../levels/).
- [Vampire Survivors](https://poncle.itch.io/vampire-survivors): the reference
  for the feel of a late run.
- [Crypt of the NecroDancer](https://braceyourselfgames.com/crypt-of-the-necrodancer/):
  the reference for one step per beat, which this design moves away from.

## Design Overview

The parts of the loop, each defined briefly here and fully in its own section:

- **Strides.** The hero moves in quick hops of up to one stride of ground,
  about 45 px in the galop, at a fixed average speed. See
  [Movement](#movement-fixed-speed-strides).
- **The groove.** Each press is rated Perfect, Great, Good or loose by how
  close it lands to the beat. The groove is a meter from 0 to 1 that rises with
  well-rated presses and falls with stray presses and silence, and its tier
  sets the size of every attack. See
  [Timing and the Groove](#timing-and-the-groove).
- **Attacks.** The sword swings by itself on every beat, and the other weapons
  fire on their own beats. See [Attacks](#attacks).
- **Health.** A small pool, bites that leave the hero briefly invulnerable, and
  scarce healing. See [Health and Damage](#health-and-damage).
- **The horde.** A steady stream plus set pieces. See [The Horde](#the-horde).
- **Gems and level-ups.** Gems that come within the hero's **pickup radius**,
  which grows with the tier, fly in. At each level-up the hero walks into one
  of three upgrades. See
  [Gems, Pickups and Level-Ups](#gems-pickups-and-level-ups).

[One Second of Play](#one-second-of-play), after those sections, traces all of
them together.

### Rules that hold everywhere

The rest of the design follows from these rules:

- **No dead presses.** Every key press strides, bends the stride in progress,
  or stomps, at once.
- **Timing never changes where the hero goes.** Stride lengths and speed never
  depend on ratings.
- **Timing changes power, and nothing else.** It moves the groove, and on an
  accent it decides whether a shockwave launches (see
  [Accents and cleaves](#accents-and-cleaves)).
- **Presses never arm swings.** Every weapon fires on its own schedule. An
  accent's shockwave is the one thing a press launches.
- **Every percussive gameplay sound is on the grid.** See
  [Sound on the Grid](#sound-on-the-grid).
- **Causality.** The game may schedule anything the song determines in
  advance, such as the grid, accents, set pieces and level-ups, but no rule
  reads a press before it happens. A press 50 ms after a chord can't change
  the impact that already landed on the chord, so it launches a wave that
  starts after the chord instead. Play mode keeps today's live loop, which
  runs a little ahead of the music and rolls back when a press arrives.
- **The groove measures rhythm only.** Damage, kills and pickups never change
  it.

## Movement: Fixed-Speed Strides

The hero moves at one fixed speed. Ground builds up as time passes, and each
stride spends what has built up. Pressing more often therefore makes strides
shorter, never the hero faster. A full stride at the galop's tempo is about as
long as today's 44 px step.

### The stride budget

Four quantities decide how far the hero may go:

- **Walk speed:** 140 px per second of world time, in every section, at any
  tempo and any press rate. That is faster than every enemy except bats (165
  px/s) and bees (180); the rest move at 68–120 px/s. **World time** is the
  clock game motion runs on: it slows for slow motion and stops during a
  freeze, while song time follows the music.
- **Stride period:** one step of the section's timing grid, halved while it is
  longer than 0.4 s, or 0.35 s in sections with no steady beat. A held key
  strides once per stride period (see
  [When strides start](#when-strides-start)).
- **One stride:** the walk speed times the stride period. That is 44–48 px
  through the galop, 36 px in the 230 beats-per-minute coda, and 49 px with no
  steady beat. At 78 beats per minute the period halves to an eighth note
  (385 ms), for a 54 px stride.
- **Stride budget:** the ground the hero may still cover. It refills at the
  walk speed, never above one stride, and every stride spends all of it.

```ts
// Every simulation step of dw seconds of world time:
budget = Math.min(oneStride, budget + walkSpeed * dw);

function startStride(dir: Vec, w: number): void {
  stride = { from: hero.pos, dir, length: budget, startW: w };
  budget = 0; // the stride takes 110 ms and lands `length` px away
}
```

What follows from the budget:

- **Distance is capped.** Over any time `t`, the hero covers at most
  `140 × t` plus one stride, so mashing can never beat holding.
- **A first press is a full stride.** After standing still for a stride period,
  a press moves a full stride: the same snappy step as today.
- **Quick presses split the ground.** A press soon after another moves only
  the ground that built up since: half a stride for an eighth note, a few
  pixels for a mashed press. It still hops and puffs dust, so it reads as a
  footfall.

At 185 beats per minute, with a 325 ms stride period and a 45 px stride:

```text
input                 strides per second   stride length   speed
hold a direction      3.1, on the beat     45 px           140 px/s
tap quarter notes     3.1, on the taps     45 px           140 px/s
tap eighth notes      6.2, on the taps     23 px           140 px/s
mash 8 per second     8, on the presses    about 18 px     140 px/s
```

### When strides start

A stride starts in exactly two situations:

- **On every direction key press,** at once, except a partner key: a
  perpendicular key within 40 ms of a press bends that press's stride toward
  the diagonal instead, and an opposite key within 40 ms turns it into a stomp
  (see [Standing still: stomps](#standing-still-stomps)).
- **On every step of the stride period while a direction key is held,** after
  the first stride. The steps are the grid times, plus the points half-way
  between them wherever the period is halved. A player who only holds a key
  strides on the beat, or on eighth notes when the beat is slow, and walks at
  the full walk speed either way.

Strides never start on release or on key repeat events:

- **Release:** releasing every key stops further strides; a stride in progress
  finishes.
- **Key repeat events** are ignored, as today.

A press between beats strides at once, and holding then snaps to the beat:

```text
theme A repeat, beats at 80.240, 80.562 and 80.884
80.400  press and hold Right: a full 45 px stride at once
80.562  still held: a stride on the beat, 23 px (the ground since 80.400)
80.884  still held: a stride on the beat, 45 px
```

In a slow section the steps fall on eighth notes, so holding still walks at
the full speed:

```text
a section at 78 beats per minute, beats 769 ms apart: the stride period
halves to 385 ms, and a held key strides on every eighth note
0.000  press and hold Right: a full 54 px stride at once
0.385  still held: a stride on the off-beat, 54 px
0.769  still held: a stride on the beat, 54 px; 140 px/s throughout
```

### Stride shape and direction

Every stride has the same shape; only its length and direction vary:

- **Shape:** 110 ms of world time, a cubic ease-out, a 5 px hop and a landing
  puff of dust, like today's step. Between strides the hero stands still.
- **Direction:** the combined direction of the held keys. Of two opposite held
  keys, the more recent wins.
- **Diagonals:** a perpendicular key within 40 ms bends the stride toward the
  diagonal without changing its length, as today.
- **Interruptions:** any other press during a stride stops it where it is. The
  ground it hadn't covered returns to the budget, and the new stride sets off
  from there.
- **Nothing blocks a stride.** The hero passes through enemies, and the open
  field has no walls.
- **Quiet:** a stride makes only a soft rustle well under the music.

### Without a steady beat

The **song map**, the analysed beats, sections and accents of the recording,
marks each section **pulsed** (with a steady beat) or **free** (without one,
like the prelude, whose tempo stretches and slows freely). In a free section:

- **Holding walks continuously** at the walk speed, with the walk cycle
  matched to the distance covered: today's walk, faster.
- **A tap from standing still** moves one stride (49 px).
- **Presses are not rated,** and the groove doesn't change.
- **At the start of a pulsed section,** a walk in progress becomes strides on
  the grid from the section's first grid time.

### Standing still: stomps

Vampire Survivors lets a player stand still for free. Here, standing still
without pressing drains the groove, so the player can **stomp**: press without
moving.

- **Input:** Space, or two opposite direction keys (Left and Right, or Up and
  Down) pressed within 40 ms of each other.
- **Judged like any press.** A stomp on the beat fills the groove, and an extra
  stomp is a stray press. A stomp made of two keys is timed by the first. See
  [Press classes](#press-classes) below.
- **Opposite keys rock.** The first key's stride turns into a rock step: over
  the rest of its 110 ms the hero comes back to where it began, and the ground
  goes back to the budget.
- **A stomp cancels the held strides of one grid step:** the next held stride
  after the stomp, or the next two wherever the stride period is halved.
  Holding a direction and stomping on every beat therefore never moves the
  hero. A new direction press still strides at once.
- **A stomp has no attack of its own:** no knockback and no damage. On an
  accent it counts like any press, so a Perfect stomp on the chord launches
  the shockwave.
- **Presentation:** both feet stamp with a ring of dust. There is no
  percussive sound, because a press can't be relied on to land on the grid.

```text
the first closing ring meets the hero on the chord at 62.300
(beats at 61.616, 61.958 and 62.300); the player waits inside it
61.606  Space, 10 ms early: Perfect; groove +0.08; the hero stamps
62.018  Left, then Right 25 ms later: one stomp, 60 ms late: Great; a
        rock step out and back
62.295  Space on the chord, 5 ms early: Perfect, so the heavy swing
        launches its shockwave
```

## Timing and the Groove

Timing never changes where the hero goes. It changes the **groove**, a meter
from 0 to 1 that tracks how well the player keeps the beat. The groove sets
the tier, 0 to 3, and the tier sets the size of every attack.

### Ratings

Each press in a pulsed section is rated by how far it lands from the nearest
grid time:

- **Delta:** a press's time minus the nearest grid time, after the player's
  calibration offset (the input delay measured on the title screen), as today.
- **Ratings,** with today's play-mode windows: Perfect within 45 ms, Great
  within 90 ms, Good within 140 ms, and **loose** beyond that.
- **The off-beat** of a grid time is the point half-way to the next one: the
  "and" of an eighth note on a beat grid.

### Press classes

Every press in a pulsed section falls into exactly one class, and a stomp
counts as one press. The first press that belongs to a grid time is that grid
time's **counted press**, and the other classes are judged against it. The
classes are checked in this order:

1. **Partner:** a perpendicular key within 40 ms of the previous press, which
   bends that stride, or the opposite key within 40 ms, which makes a stomp. It
   belongs to the previous press and isn't judged on its own.
2. **Off-beat press:** within 90 ms of the off-beat after a grid time whose
   counted press was Great or better, and the first such press for that
   off-beat. A Perfect one (within 45 ms) adds to the groove; a Great one is
   safe and changes nothing.
3. **Counted press:** the first press for its nearest grid time. Its rating
   changes the groove.
4. **Double tap:** a second press within 100 ms after the counted press, such
   as a key bounced twice or a slow diagonal. It changes nothing and shows
   grey.
5. **Stray press:** anything else. It still strides, because every press does,
   and takes a chunk off the groove. It shows red.

Six presses at 185 beats per minute, one of each kind, with the groove change
each one makes (the values are in [Starting values](#starting-values) below):

```text
beats at 10.000 and 10.325; the off-beat between them is at 10.1625
 9.992  Right  counted press for 10.000, 8 ms early: Perfect      +0.08
10.020  Up     partner, 28 ms later: the stride bends diagonally      0
10.070  Right  double tap, 78 ms after the counted press              0
10.120  Right  off-beat press, 43 ms early: Perfect               +0.03
10.150  Right  stray: 10.000's counted press and off-beat are taken -0.10
10.400  Right  counted press for 10.325, 75 ms late: Great        +0.02
```

### Drain

The groove falls on its own when the player stops keeping the beat:

- **Silence drains.** One bar after the latest press that raised the groove,
  it starts draining at an eighth per bar (full to empty in 8 bars), until the
  next press that raises it.
- **What drains:** standing still, holding keys without pressing, and pressing
  only loose or stray presses. A stomp on the beat raises the groove like any
  other press.
- **Free sections freeze it,** with no ratings and no drain.
- **The song starts at 0.**

### Tiers

The groove's value maps to a tier, and a player pressing well climbs quickly:

```text
groove   tier   from 0, with a Perfect press on every beat
< 0.30   0
0.30     1      after 4 beats
0.60     2      after 8 beats
0.85     3      after 11 beats
```

- **Hysteresis:** a tier drops only when the groove falls 0.05 below its
  threshold, so it doesn't flicker.
- **When it applies:** a tier change applies to every weapon firing that starts
  after it. The sword reads the tier when its wind-up starts.
- **Sound:** a rising chime for a tier-up and a soft falling one for a
  tier-down, on the next grid time.

### Starting values

These are changes to the groove on its 0-to-1 scale, tuned until the checks in
[Automated Checks](#automated-checks) pass:

```yaml
groove:
  counted: { perfect: 0.08, great: 0.02, good: -0.05, loose: -0.10 }
  offbeat: { perfect: 0.03, great: 0 }
  doubleTap: 0
  stray: -0.10
  drain: { afterBars: 1, emptyInBars: 8 }
  tiers: [0.3, 0.6, 0.85]
  hysteresis: 0.05
```

### Why loose timing costs a little

At 185 beats per minute a beat lasts 325 ms, so a press at a random moment
lands within 90 ms of a beat more than half the time. If every Great or Good
press added to the groove, pressing at random would fill it. With the values
above, a random press averages about zero:

```text
expected groove change per counted press, at 185 beats per minute
random timing: 28% Perfect, 28% Great, 31% Good, 14% loose
  0.28 x 0.08 + 0.28 x 0.02 - 0.31 x 0.05 - 0.14 x 0.10   = about 0.00
an honest press, 60 ms standard deviation                 = about +0.04
the moderate player, 30 ms standard deviation             = about +0.07
```

Mashing is worse than random timing, because it adds stray presses on top. At
four random presses a second, about a third of the beats also get a stray
press, so the groove falls steadily.

### The grade

The end card's letter grade still comes from the average groove over pulsed
sections. Its thresholds are retuned so the moderate player earns an A, the
casual player (a sparse, sloppy virtual player; see
[The Difficulty Curve](#the-difficulty-curve)) a B, and players who mash or
only hold keys a C.

## Attacks

Every weapon fires by itself on its own beat. Presses never arm swings; the
tier changes how big the attacks are, and a press on an accent can launch a
shockwave.

### Firing

Each weapon has its own schedule, taken from the song:

- **The sword** swings on every grid time in pulsed sections. Today's rules,
  where a press arms the next beat's swing and a bar without presses gets one
  idle swing, are removed.
- **Wind-up and aim:** the swing starts about 190 ms before its grid time and
  aims when it starts, choosing the direction that hits the most valuable
  enemies, as today.
- **Without a steady beat,** the sword swings on every beat of the song map,
  and swings heavy on strong anchors (strength 0.5 or more). **Anchors** are
  the strong moments the song map marks in free sections.
- **The other weapons** fire on their own subdivisions, as today: orbiting
  blades every eighth note, chain lightning every bar, the fire trail every
  beat.
- **Hit-stop,** a brief freeze of world time after a heavy impact while the
  music plays on, works as today, except for cleaves (see
  [Accents and cleaves](#accents-and-cleaves) below).

### The starting loadout

The run starts with the sword, orbiting blades and chain lightning, as a late
run would. Level-ups add the rest (see
[Level-ups and upgrade orbs](#level-ups-and-upgrade-orbs)):

- **Fire trail:** a new weapon, with today's definition.
- **Blades evolve:** two more blades and an orbit 20% wider.
- **Lightning evolves:** it fires every beat instead of every bar, with one
  more jump.

### What a tier changes

Tiers mostly grow area, because area is what keeps the horde away. Damage rises
only a little: most enemies die to one hit at any tier (their health is 0.6 to
1.15, and a light swing deals 1.0), so extra damage matters only for orc elites
(health 3.0) and the boss (175).

```text
tier  sword swing        blades           lightning          trail  damage
0     100 px, 150°       3, orbit 72 px   3 jumps, 110 px    20 px  x1.0
1     130 px, 180°       3, orbit 90 px   4 jumps, 125 px    26 px  x1.1
2     160 px, 240°       4, orbit 105 px  5 jumps, 140 px    32 px  x1.2
3     190 px, full spin  5, orbit 120 px  6 jumps, 160 px    38 px  x1.3
```

- **Tier 0 is today's sword.** All values are starting points for tuning.
- **Evolutions stack on top:** evolved blades have two more blades and a 20%
  wider orbit at every tier.
- **The pickup radius grows too:** 40, 65, 95 and 120 px (see [Gems](#gems)).
- **The tier is visible.** Each tier has its own weapon colour and trail, the
  hero gains a faint aura from tier 1, and at tier 3 the swing animation
  becomes a spin.

### Accents and cleaves

This section builds on the counted press and its rating (see
[Press classes](#press-classes)) and on the tier table above.

An **accent** is a loud moment marked in the song map, such as a tutti chord
(one the whole orchestra plays together), with a strength from 0 to 1. The
level script decides, section by section, which accents are eligible:
strength 0.7 or more by default, and only the tutti chords in the galop
opening. The kick line's release counts as an accent, as today.

- **The heavy impact is the same for everyone.** On an eligible accent the
  sword's swing is heavy whatever the player pressed: 130, 160, 190 or 220 px
  by tier, always 50° wider than that tier's light swing (200°, 230°, 290°, and
  a full spin at tier 3), with 2.5 base damage and the heavy impact sound
  exactly on the accent.
- **A press on the chord makes a cleave.** When the accent's counted press is
  Great or better, early or late, the swing also launches a **shockwave**: a
  crescent (a full ring at tier 3) that travels outward in 25 px bands, one per
  sixteenth note. It deals 1.0 base damage to every enemy it reaches, out to
  260, 300, 340 or 380 px by tier.
- **The wave starts on the "and".** Its first band is on the eighth note after
  the accent, then one band per sixteenth.
- **Never late.** A sound plays exactly on its slot only when it is known at
  least 60 ms ahead (see [Sound on the Grid](#sound-on-the-grid)). At 185
  beats per minute an eighth note is 162 ms, so even a press 90 ms late is in
  time for the first band. In the coda an eighth note is 130 ms, so a press
  more than 70 ms late starts the wave one sixteenth later instead.
- **Hit-stop only for early presses.** A hit-stop is silent, so it can't throw
  a sound off the grid. It happens only when the press came before the impact,
  with today's length: `min(110 ms × (0.6 + 0.4 × strength), 150 ms)`.
- **Telegraphs:** the **beat track** is the HUD strip where beat ticks slide
  toward a beating heart. Eligible accents show on it as large gold ticks, a
  gold ring marks the accent's own tick (today it marks the beat before), and
  the heart glows over the beat before.

```text
galop opening, a tutti chord at 56.780 (eighth 171 ms, sixteenth 86 ms)
56.590  the heavy wind-up starts, for every player
56.780  the heavy impact and its sound, on the chord
56.830  the player's press arrives 50 ms late: Great, so a cleave
56.951  the wave's first band, on the "and"
57.037  the second band, then one every sixteenth out to the tier's reach
```

## Health and Damage

Health works like a late Vampire Survivors run: a small pool, damage that can
be seen coming, and healing that is hard to find. The groove keeps the hero
alive by keeping space, never by healing.

- **Health:** 100.
- **Bites:** an enemy in contact, at today's contact distance, bites for its
  contact damage. The hero is then **invulnerable** for 0.5 s of world time:
  nothing hurts it, and its sprite blinks.
- **A swarm hurts at a capped rate:** one bite per half second, 4–20 health a
  second depending on who is biting. Surrounded by the galop's goblins and
  orcs, a hero at full health lasts about 6–12 seconds.
- **Attacks:** an arrow takes 15 (up from 12) and a boss slam 25.
  Invulnerability blocks both.
- **Healing:** a chicken heals 30 and a level-up 20. There is no rhythm healing
  and no regeneration.
- **The groove never changes** when the hero is hurt.
- **Falling:** at 0 the game stops on the YOU FELL menu, as today. Restarting a
  section restores the health the hero had at its start.

Contact damage keeps today's values:

```text
bat, bee, eyeball 2 · slime, skeleton archer 3 · goblin, skeleton 4 · orc 5
ember orc, ember skeleton 6 · orc elite 8 · minotaur 10
```

## The Horde

The peak stays at about 500 enemies on screen, the demo's spectacle. What
changes is how they arrive: a thinner steady stream, plus set pieces that the
player can read and route through.

### The steady stream

Between set pieces, enemies keep arriving as they do today, only fewer of
them:

- **Thinner:** the spawn director, which tops the population up on every beat,
  aims for 60% of the level script's population curve. Set pieces bring the
  rest.
- **Placement as today:** just outside the view, 60% of spawns from the half
  ahead of the hero's **heading**: its average direction of travel over the
  last two beats. While the hero stands still the heading keeps its last
  value, starting to the right. Enemies left far behind move ahead, as today.
- **Speeds:** every enemy except bats and bees stays slower than the hero,
  from the eyeball's 68 px/s to the skeleton archer's 120. Bats (165) and bees
  (180) are faster but die to any hit (health 0.6).

### Set pieces

Each set piece is scheduled from the song in the level script, and its
members are ordinary enemies that can be hit at any time.

- **Closing ring:**
  - **Shape:** enemies evenly spaced on a circle just outside the view,
    centred where the hero stood when it spawned, with two or three gaps of
    three missing places. Rings of more than 40 enemies form two rows.
  - **Timing:** it spawns four to eight bars before its target accent and
    closes at a steady speed, reaching 100 px from its centre (a tier-0
    swing's reach) on the accent. Then its members chase as usual.
  - **The hero's options:** the centre stays fixed, so the hero can leave
    through a gap, push through the line and take bites, or stay in the middle
    and cleave on the chord.
  - **Spacing:** ring members are exempt from the spacing check while in
    formation, as formation members are today.
- **Stampede:** a band of goblins at 2.5 times their speed enters from one side
  of the view and runs straight across in two or three rows with gaps,
  ignoring the hero. Members that leave the far side are recycled. The hero
  sidesteps through a gap or cuts a hole.
- **Kick line,** as today: 12 ember skeletons dance in a line for two phrases,
  then charge the hero on the release accent, where a cleave meets them.
- **Bee wedges,** as today: nine bees in a V dive in on phrase starts and break
  formation after six beats.
- **Archer volleys,** as today: during the kick line, archers draw for a beat
  and shoot every two bars. A sidestep earns a **near miss**: an attack that
  passes close without hitting, rewarded with a slow-motion dip and a
  "PERFECT" callout.
- **The boss,** as today: the minotaur lands on the climax's first downbeat,
  chases slowly and slams a marked spot under the hero. Leaving the marker
  earns a near miss.

The starting schedule, tuned in the level script:

```text
time     set piece                                          section
0:59.5   ring of 30 spawns; closes on the chord at 1:02.3   galop opening
1:21.5   bee wedge, and another at 1:25.4                   theme A repeat
1:30.5   kick line, with archer volleys every two bars      kick line
1:42.0   stampede of 40, after the level-up's orbs          bridge
1:44.6   the boss lands                                     climax
1:51.2   ring of 60 spawns; closes on the accent at 1:56.2  climax
2:04.6   stampede of 60                                     coda
2:08.8   ring of 80 spawns; closes on the accent at 2:13.0  coda
```

A ring in the level script, using today's musical references:

```yaml
- at: { accent: tutti-chord, fromEnd: 6 }          # 0:59.5
  ring:
    enemy: goblin
    count: 30
    gaps: 3
    closesOn: { accent: tutti-chord, fromEnd: 5 }   # 1:02.3
```

## Gems, Pickups and Level-Ups

Collecting is the main reason to move other than danger. Kills at the edge of
the hero's reach drop gems there, so collecting means stepping toward the
horde: the trade at the heart of Vampire Survivors.

### Gems

Experience gems are the main pickup:

- **They stay where they drop.** Every death drops gems on the spot, and they
  wait there. Today they fly to the hero on the next downbeat.
- **The pickup radius** is 40, 65, 95 or 120 px by tier, and each magnet bonus
  from a level-up widens it by 30% (see
  [The upgrade pool](#the-upgrade-pool)).
- **Collection is on the grid.** A gem inside the radius flies to the hero and
  is collected on the next sixteenth note at least 60 ms away. Its experience
  counts then.
- **A cap:** at most 300 gems lie on the field. Beyond that, a death adds its
  value to the nearest gem, which grows.
- **Gems stay put** when enemies are recycled or the hero walks far away.

### Gem chimes

Collecting plays a tune, so sweeping a gem field sounds like part of the
music:

- **One chime per sixteenth** with pickups, however many gems land on it.
- **Pitched in key:** each chime is the next note of the section's scale. The
  song map gives each section's key, and the tuned sound palettes (each
  section's set of effect samples) already transpose samples to it.
- **Runs climb:** consecutive sixteenths with pickups climb the scale, and a
  beat without pickups starts again from the root.
- **Budget:** chimes share the existing limit on sounds per sixteenth note.

```text
theme A, in G major, beats at 76.330 and 76.656: the hero strides into a
field of gems after a cleave
76.411  3 gems land: G
76.493  5 gems land: A
76.574  2 gems land: B
76.656 to 76.981: no pickups for a beat, so the next run starts on G
```

### Vacuum

A **vacuum** is the payoff for surviving a set piece: a pickup that collects
every gem in view.

- **When:** a vacuum drops on the first downbeat after each set piece ends:
  after a ring's accent, after the stampede leaves the view, and after the
  kick line's release.
- **Where:** 150 px from the hero along its heading. It stays until touched,
  within 24 px.
- **Effect:** every gem in view flies to the hero over one bar, collected on
  sixteenths, so a vacuum plays a long run of chimes.

```text
the first ring closes on the chord at 62.30 and the hero cleaves it
62.98  the next downbeat: a vacuum drops 150 px ahead of the hero
63.40  the hero touches it; 46 gems in view take off
63.49  to 64.09: they land on eight sixteenths, a run of chimes
```

### Chicken

Healing is rare, and it comes from pickups:

- **Drops:** each orc elite drops a roast **chicken**, the genre's floor
  pickup, with probability 0.5, and the boss drops two.
- **Collecting:** by touching it, within 24 px. The pickup radius doesn't pull
  it.
- **Effect:** it heals 30 on the next sixteenth, with its sound there.

### Level-ups and upgrade orbs

A level-up is a choice made by walking, so the music never stops:

- **Three level-ups,** at today's musical moments: the starts of the galop
  opening (0:54.1), of theme A's repeat (1:20.2) and of the bridge (1:40.7).
- **The experience bar** fills from gems. A full bar waits and glows until its
  moment, and a bar that isn't full levels up on the first downbeat after it
  fills, as today. Requirements are retuned so the moderate player fills each
  bar a little before its moment.
- **On the level-up downbeat,** three things happen:
  - a pulse pushes enemies within 160 px out to 160 px, as the boss's landing
    does;
  - the hero heals 20;
  - three **upgrade orbs** rise around the hero, 120 px away, one ahead along
    its heading and one 120° to each side, each labelled with its name and
    icon.
- **Walking into an orb,** within 24 px, takes it. The upgrade applies on the
  next grid time, with its sound there, and the other orbs sink away.
- **Two bars to choose.** If no orb is taken by then, the orb that rose ahead
  of the hero is taken.
- **No pause.** The world doesn't slow, and the music never stops.

```text
galop opening, beats every 342 ms from the level-up downbeat at 54.07
54.07  enemies within 160 px are pushed out to 160 px, the hero heals 20,
       and three orbs rise: Fire Trail ahead, Blades Evolve to the left,
       Might to the right
54.30  the player presses and holds Left: a full 48 px stride toward it
54.86  the third stride (15 px at 54.41, 48 px at 54.75) reaches the orb,
       and the other two sink away
55.09  on the next beat the blades evolve, with the upgrade's sound
```

### The upgrade pool

**Passives** are stat bonuses rather than weapons. The pool:

```text
weapon upgrades, each once            passives, each can be taken again
fire trail, a new weapon              max health +20, healing 20 at once
blades evolve: +2 blades, orbit +20%  magnet: pickup radius +30%
lightning evolves: every beat, and    might: damage +10%
  one more jump                       armour: bites take 1 less, at least 1
```

- **Offers:** each level-up offers three different items, at least one of them
  a weapon upgrade while any remain.
- **Seeded:** offers come from the run's seed and the level-up's index, so
  Watch my run replays them exactly.
- **Choices matter:** with three level-ups and seven items, every run skips
  some.

## One Second of Play

With the pieces above defined, here is one second of theme A at tier 2. The
beats fall at 72.100, 72.425 and 72.750, and a sixteenth note is 81 ms:

```text
72.092  tap Right, 8 ms early: Perfect; groove 0.55 -> 0.63, now tier 2
        a 45 px stride right starts at once and lands at 72.202
72.235  the sword winds up by itself, aimed at the densest arc
72.270  tap Up on the off-beat: a Perfect off-beat; groove 0.63 -> 0.66
        a 25 px stride up: only that much ground has built up
72.425  impact on the beat: a 160 px, 240° tier-2 swing kills 9; their
        gems stay where they died, 120-160 px out
72.600  a goblin bites: health 64 -> 60, invulnerable until 73.100
72.746  tap Right, 4 ms early: Perfect; the stride brings 4 gems inside
        the 95 px pickup radius, and they land with one chime at 72.831
```

## The Prelude

The prelude (0:00–0:43) has no steady beat. It becomes a calm walk that
teaches movement and collecting before the rhythm game starts:

- **Calm enemies,** as in today's play mode: slower, drifting in on a spiral.
- **Weapons as everywhere else:** the sword on every beat of the song map,
  heavy on strong anchors, with the blades and lightning on their beats.
- **Gems** from those kills teach walking to collect.
- **No groove:** presses still stride, but nothing is rated and the groove
  stays at 0.
- **The count-in stays:** four count marks pulse on the beat track over the
  last four beats before the galop.
- **Mashing works here,** by design.

## The Difficulty Curve

The tier is the player's power, and the song's pressure rises until tier 0 can
no longer hold space. The pressure comes from the stream's density, the set
pieces, enemy speeds and contact damage. The tier sizes set how much space
each tier holds.

What a tier-0 player, holding keys or mashing, should experience:

```text
section          time        a tier-0 player
prelude          0:00-0:43   safe
galop vamp       0:43-0:54   safe
galop opening    0:54-1:10   safe; the first ring can be walked out of
theme A, repeat  1:10-1:31   starts taking hits
kick line        1:31-1:41   bleeds health
bridge           1:41-1:45   bleeds health
climax           1:45-2:05   falls
```

The automated checks run the virtual players below. Each section of the level
script has a **press pattern**, the grid times a skilled player presses (such
as every beat, or once a bar), and most profiles follow it. Where each player
should end up:

```text
player     how it plays                                   end health
perfect    the section's press pattern, exact timing      90-100%
skilled    the pattern, 12 ms standard deviation          80-100%
eighths    every eighth note, 12 ms deviation             80-100%
moderate   85% of the pattern, 30 ms deviation, mistakes  40-70%
casual     downbeats only, 60 ms deviation                10-40%
holder     holds directions and never taps                falls 1:45-2:05
mashers    random presses, 2, 4 and 8 a second            fall 1:45-2:05
```

- **Press patterns** are today's, unchanged.
- **The casual profile changes:** its timing deviation drops from today's
  120 ms to 60 ms. At 185 beats per minute, 120 ms is close to random timing,
  and the casual profile stands for a sparse, sloppy but honest player.
- **Mashers and holders survive to the climax,** so pressing at random still
  works early in the song.

**As shipped (hv-dtz9):** the holder and mashers fall as the table says
(`check:play` allows 1:30–2:10, since their falls spread across seeds), and
every honest player survives. The honest players' end health is judged on its
average over the checked seeds, and reported against its band rather than
checked: every honest bot ends at 90–100% on average. At tier 2 and up the
horde hardly reaches the hero, so nearly all of an honest player's damage is
the minotaur's in the climax, and the heals that follow (its two chickens and
the bridge's level-up) give it back. Neither fewer heals (no level-up heal,
half-strength chickens, no boss chickens) nor a denser or faster horde, a
sturdier boss or smaller tiers brought the averages into the bands. Reaching
them needs a change to healing or damage, for example damage the groove can't
outrun at tier 3, which is a new decision for this design.

## Sound on the Grid

Gameplay sounds have to follow the music: an impact off the grid sounds wrong.
Every percussive gameplay sound plays on a grid time of its own subdivision,
never at the moment of a press.

```text
sound                         plays on
sword impact                  its grid time
heavy impact on an accent     the accent
shockwave bands               the eighth after the accent, then sixteenths
blades, lightning, trail      eighth notes, bars, beats
kills                         grouped per sixteenth (per beat if free)
gem chimes, vacuum, chicken   the next sixteenth
hurt                          the next sixteenth
tier change, orb taken        the next grid time
level-up                      its downbeat
near miss                     the threat's landing, already on a beat
strides and stomps            only a soft rustle, which is exempt
```

- **Never late.** A sound is scheduled exactly only when it is known at least
  60 ms before its slot. A sound known later moves to the next slot of its
  subdivision instead of starting late, and the play log records each move.
- **Free sections:** the song map still marks beats where there is no steady
  pulse, so every row uses those beats and their subdivisions there, and kills
  group per beat.
- **Checked.** Today's "impacts on the grid" check grows to cover every row.

## HUD

The HUD keeps today's layout and changes what it shows:

- **Groove meter:** the bar beside the heart shows the groove, with three tier
  marks and the tier's number. A tier-up flashes it. It replaces today's damage
  percentage, and the STREAK counter goes away with rhythm healing.
- **Key display:** counted presses in their rating's colour (gold for Perfect,
  white for Great, pale blue for Good, dim for loose), off-beat presses
  outlined, double taps grey, stray presses red, and stomps as both feet.
- **Health:** today's bar, flashing on bites and heals. The hero blinks while
  invulnerable.
- **Experience:** today's bar and level.
- **Weapons and passives:** the panel shows evolutions and how many of each
  passive the hero has.
- **Upgrade orbs** carry their labels in the world.
- **Pickup radius:** a faint ring for one beat after each tier change.
- **Telegraphs:** as in [Accents and cleaves](#accents-and-cleaves).
- **End card:** kills, the rating counts, stray presses, average groove, time
  at each tier, gems collected and the grade.

## Virtual Players

The automated checks and Watch the demo use virtual players. They see the song
and the current state, never the future, as today.

- **Steering** keeps today's costs (threats, crowding, and a **roam point**
  that wanders the field at the section's pace) and adds:
  - gems within a stride's reach, by value;
  - a chicken, when below 60% health;
  - the chosen orb during a level-up;
  - gaps in rings and stampedes;
  - the middle of a closing ring at tier 2 or higher, to cleave the chord.
- **Presses:** each profile presses the grid times its section's pattern marks,
  plus every eligible accent. The eighths profile also presses off-beats, and
  the casual profile presses downbeats only. When the steering wants to stay
  put on a pressed beat, the player stomps.
- **Orbs:** a fixed priority (blades evolve, lightning evolves, might, fire
  trail, magnet, max health, armour), except that below 50% health max health
  comes first.
- **Holder:** holds the direction its steering picks and changes it at most
  once a bar. Each change is a press at a random moment, so it averages about
  nothing.
- **Mashers:** press at random moments at a fixed rate, in the direction the
  steering picks.
- **URL:** `?player=` gains `holder`, `eighths`, `mash2`, `mash4` and `mash8`.

## Watch the Demo and Watch My Run

Both recorded modes run the same rules as play:

- **Watch the demo** becomes a recorded run of the skilled virtual player
  under these rules, in the open field: terrain generated in chunks around the
  hero, the ground re-theming in a wave at each zone change, and landmarks
  rising around the hero.
- **The prelude plays at full speed** with calm enemies, as play mode's does,
  instead of the demo's slow motion.
- **Removed:** the forest corridor world, its route and pace checks, and the
  old rules where presses arm swings.
- **One level script** drives both modes. Play mode's separate overrides fold
  into it.
- **Watch my run** is unchanged: it replays the recorded key presses through
  the same rules.
- **Tools keep working:** `?capture`, `?bench` and `?metronome` run against the
  new demo.

## Play Logs

Tuning needs evidence from real play, so the play log records the new rules'
decisions:

- **Per press:** its class, rating and delta; the stride's length and the
  budget left; the groove before and after; and the tier.
- **Events:** bites (the enemy, the damage, and how long the hero stays
  invulnerable), heals and their source, gem pickups per sixteenth, vacuums,
  orb offers and picks, and sounds moved to a later slot.
- **Summary** (`npm run log:play`): presses by class and time at each tier per
  section, bites and damage per section, gems collected against dropped, and
  the hero's speed over each second.
- **Re-rating:** `log:play` can re-rate any log's presses under the current
  groove rules, using grid times from the song map. Logs recorded under
  today's rules then serve as human references while the groove values are
  tuned.

## Migration and Compatibility

The change replaces rules rather than adding a mode, so stored data and
content move over once:

- **Plan format:** the **plan**, the recorded run that the renderer plays back,
  raises its format version, so cached plans from the old rules are never
  loaded.
- **Golden demo hashes,** the plan hashes that tests compare the demo
  against, are re-recorded once for this change. From then on they guard the
  new rules as they guard today's.
- **Hero content:** strides, the walk speed, timing windows, the groove values,
  tiers, the pickup radius and health replace today's step, walk, healing,
  streak, reach-by-accuracy and stumble settings:

```yaml
hero:
  walk: { speed: 140 }
  stride: { durationMs: 110, hop: 5, maxPeriodS: 0.4, freePeriodS: 0.35 }
  windows: { diagonalMs: 40, stompMs: 40, doubleTapMs: 100 }
  timing: { perfectMs: 45, greatMs: 90, goodMs: 140 }
  health: { max: 100, invulnerableMs: 500, levelUpHeal: 20 }
  pickupRadius: [40, 65, 95, 120]
```

- **Other content:** the archers' attack damage becomes 15, weapons gain
  per-tier values and evolved forms, passives become a new kind of content, and
  level events gain rings, stampedes, vacuums and level-up offers.
- **Rule switches** for one step per beat, health and telegraphs are removed:
  each behaviour is now always on or gone.
- **Saved data:** the best grade moves to a new storage key, because grades
  under the old rules aren't comparable. The calibration offset stays.
- **URL parameters:** `?mode=`, `?start=`, `?seed=` and `?player=` keep
  working, and `?mode=demo` plays the new demo.
- **Documents:** [beat-steps.md](beat-steps.md) and
  [interactive.md](interactive.md) stay as history. Once this design ships,
  their superseded sections point here, and the README's controls and checks
  are updated.

## Automated Checks

`check:play`, `check:plan`, `check:determinism` and `check:seeds` run these
rules on seeds 1858, 1859, 7 and 2024. Every failure names the time and the
entity involved, as today.

- **Kept:** causality, determinism, auto-aim, damage adds up, spacing, a clean
  ending, off-screen spawns for the steady stream, live play equal to replay,
  and an identical Watch my run.
- **Removed:** the corridor pace checks and one step per beat.
- **No dead presses:** every key press produces a stride, a bend of the stride
  in progress, a rock step or a stomp at its time.
- **The speed cap:** over every window of time, for every player, the hero's
  displacement is at most `140 × elapsed + one stride + 1 px`.
- **Holding keeps full speed:** a held key covers the walk speed, within 5%
  over each bar, in every pulsed section. A unit test checks the same on a
  grid slower than 150 beats per minute, where the stride period halves.
- **Every movement has an input:** strides start only at a key press, or at a
  step of the stride period while a key is held.
- **Sounds on the grid:** every row of the table in
  [Sound on the Grid](#sound-on-the-grid), within 1 ms.
- **The curve:** the holder and mashers fall as the table in
  [The Difficulty Curve](#the-difficulty-curve) says, and the honest players
  survive; their average end health is reported against its band (see the
  note there).
- **Groove:** after the galop vamp, the moderate player is at tier 2 or higher
  for at least 70% of pulsed time, and holders and mashers are at tier 0 for at
  least 80% of it. The eighths player's average groove is within 0.05 of the
  skilled player's.
- **Signature moments** for the moderate player:
  - at least 80% of eligible accents cleave;
  - every kick-line skeleton dies within two beats of the release;
  - at least 75% of threats end as near misses;
  - every level-up happens on its moment, and an orb is walked into at each;
  - at least 60% of the experience dropped is collected.
- **Population:** from the fourth bar of each section, the stream stays within
  25% of its target. Set pieces add to it, and the total never exceeds 520.

## Alternatives Considered

Each decision had plausible alternatives. These are the ones rejected, and
why:

- **Movement:**
  - *Today's steps, at most one per beat:* the smallest change, but playing on
    the beat stays slower than the horde, and the capped presses do nothing.
  - *One press, one step, uncapped:* mashing stays the fastest way to move.
  - *On-grid eighth notes add speed:* at 185 beats per minute the timing
    windows cover most of an eighth note, so a masher earns most of the bonus.
  - *Plain walking with rhythm on a separate key:* the cleanest survival feel,
    but rhythm stops being part of moving.
- **Power:**
  - *Presses arm swings, as today:* holding becomes very weak, and a masher
    still swings on every beat.
  - *A big swing only on a pressed beat, with no meter:* immediate, but there
    is no build-up and no tier-up moment.
- **Groove:**
  - *Each press pulls the meter toward its accuracy, as today:* holding plus
    one good tap a bar stays at the top.
  - *Tiers from streaks of clean beats:* easy to read, but one slip at 185–230
    beats per minute costs a tier.
- **Tier effects:** a new move per tier jumps too far between tiers to tune
  smoothly, and damage alone can't be seen on screen.
- **Health:** regeneration at a high groove brings back reliance on healing,
  and a big pool with smaller heals keeps damage a slow drain instead of
  readable hits.
- **Accents:** a setup press one beat early, as today, asks players to press
  before the big moment instead of on it. Counting only early presses misses
  the late presses a big chord pulls.
- **Standing still:** rocking between opposite keys needs no new input but
  can't hold an exact spot, and freezing the groove while standing lets a
  player park at tier 3.
- **Level-ups:** automatic upgrades take away the choice, and a slow-motion
  card menu stalls the action under the music and takes the arrow keys away
  from movement.
- **The demo:** keeping it frozen means keeping two movement models, two worlds
  and two swing systems, and retiring it leaves nothing to show someone who
  won't play.

## Manual QA

Run the dev server from a worktree (`npm run dev`, with the port from
`npm run port`), and open it in desktop Chrome with wired headphones. A new
debug overlay, `?debug=moves`, shows the hero's speed over the last second,
the stride budget, the groove and tier, the pickup radius as a ring, and the
last eight presses with their class and delta.

### First assembled check

Do this as soon as strides, the groove and the self-firing sword run end to
end, before broader tuning. Open `?mode=play&start=1:10&debug=moves` (theme
A) and spend two bars on each of these:

- **Hold Right.** The hero strides on every beat, about 45 px each, and the
  overlay reads about 140 px/s. The groove drains after a bar.
- **Tap quarter notes.** The same speed, with a footfall on every tap. The
  groove rises, and the tier steps up within about ten beats.
- **Tap eighth notes.** The same speed, with twice as many shorter strides.
  Off-beat presses show outlined, and the groove rises.
- **Mash.** The same speed, with a tiny stride on every press. Stray presses
  show red, and the groove falls to tier 0.
- **Stomp with Space on the beat.** The hero doesn't move, and the groove
  rises.
- **Throughout:** the sword swings on every beat at the tier's size, and every
  impact sounds on the beat.

### Full playthroughs

Whole runs check the difficulty curve. Each should end as described:

- **An honest run:** play the whole song on the beat. Expect to finish with
  roughly half health, at tier 2 or 3 through most of the galop.
- **A mashing run:** mash from the galop on. Expect to survive theme A, lose
  health in the kick line and bridge, and fall in the climax.
- **A holding run:** only hold keys. Expect the same fate as mashing.
- **Watch the bots:** `?mode=play&player=moderate`, then `casual`, `holder`,
  `mash4` and `eighths`, each against the targets in
  [The Difficulty Curve](#the-difficulty-curve).

### Set pieces and pickups

Each of these starts a little before its moment with `?start=`:

- **The ring at 1:02.3** (`?start=0:58`): walk out through a gap. Then restart
  the section, stay in the middle and press on the chord: the wave starts on
  the "and" and clears the ring, and a vacuum drops on the next downbeat.
- **The stampede at 1:42** (`?start=1:40`): sidestep through a gap.
- **Gems:** stride into a gem field and hear the chimes climb the scale; stop
  for a beat and hear the run start again. At tier 3, gems are pulled in from
  noticeably further away.
- **Chicken:** kill an orc elite in theme A's repeat and walk over its
  chicken.
- **Level-ups:** at 0:54.1, walk into an orb. At 1:20.2, take none, and check
  that the orb that rose ahead is taken after two bars. Collect too little
  before the bridge, and check that its level-up comes late, on the first
  downbeat after the bar fills.

### Edge cases

These trigger the rules most likely to go wrong:

- **A late press on a chord,** about 80 ms after it: the heavy impact is on
  the chord, and the wave starts on the "and". In the coda (`?start=2:05`), a
  press more than 70 ms late starts the wave a sixteenth later instead. No
  sound ever starts late.
- **An early press on a chord** brings a brief hit-stop; a late one doesn't.
- **A press during a hit-stop** ends it, as today, and strides at once.
- **A diagonal:** two perpendicular keys within 40 ms make one diagonal stride
  of the same length.
- **Opposite keys together:** a rock step out and back, counted as one stomp.
- **Space while holding a direction:** the held stride on the next grid time
  is skipped, so holding and stomping on every beat never moves the hero.
- **The tier threshold:** keep the groove hovering around 0.6, and check that
  the tier doesn't flicker.
- **YOU FELL:** fall, restart the section, and check that health and the world
  are restored.
- **Watch my run** after a run with level-ups: identical, including the
  offers.
- **Watch the demo:** the skilled bot plays the new rules in the open field.
- **Calibration:** with a known offset (Bluetooth headphones), calibrate, and
  check that Perfect ratings become reachable.
- **The play log:** press F8, run `npm run log:play` on the file, and check
  the press classes, the time at each tier and the bites per section.
