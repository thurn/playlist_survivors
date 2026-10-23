# Beat Steps and a Simulated Playthrough: Implementation Plan

> **History.** This plan describes the tech demo's step gameplay in the forest
> corridor, which [the gameplay redesign](gameplay-redesign.md) replaced. The
> simulated playthrough itself (one engine, causality, determinism, recording
> and playback) still holds; the sections marked *Superseded* point to the
> rules that replaced them, and the forest corridor, its route and its pace
> checks are gone.

## Introduction

Playlist Survivors is meant to be a rhythm game. The player presses the arrow
keys or WASD to the beat of a song. The hero steps one pace per press and
swings a sword automatically, like a Vampire Survivors weapon, while a horde
closes in. Every sword strike, and every impact sound, lands exactly on the
beat.

The current tech demo does not work that way. It plays itself, and it was
built by planning the whole level backwards from the kills:

- The planner first decides that a slash on bar 12 kills five goblins.
- It then works out where each goblin must spawn and how it walks in.
- Each section has its own choreography style, called a **moveset**: gliding
  on a figure-eight path, hopping once per bar, lunging every beat, and so on.

That approach produced a polished, musically tight demo. It also produced
three problems:

- **The movement does not match the game.** The hero glides, hops, dashes
  and sweeps in ways four arrow keys cannot produce, and the attacks swing in
  scripted directions unrelated to where the enemies are.
- **The movement feels floaty.** The hero skates: path offsets slide the hero
  sideways between beats, every lunge springs back to the path over two
  beats, and the walk cycle follows the beat instead of the distance covered.
- **Nothing in it could happen in a real game.** Enemies walk to
  appointments chosen by the planner. The planner also uses future
  knowledge of the hero's attacks, for example to shorten a hit-stop so it
  ends before the next attack.

This document replaces the backwards planner with a **simulated
playthrough**: the game rules run forward in time, driven by key presses
from a **virtual player**, a deterministic program that plays the game the
way a highly skilled human would. The simulation is recorded, and the
existing renderer plays the recording back in sync with the music.

### Goals

- **Show what a highly skilled player experiences.** The demo still has no
  keyboard input, but every frame must be one that the real game could
  produce from some sequence of key presses.
- **One movement model.** In step sections, each on-beat press moves the hero
  one quick, firm step. In free sections, held keys walk the hero smoothly.
  Nothing else moves the hero.
- **Automatic, intelligent attacks.** The sword aims itself at the best
  target, like Vampire Survivors weapons. The player chooses where to stand,
  not where to swing.
- **Every impact on the beat.** Sword strikes and their impact sounds always
  land exactly on the musical grid, however early or late the player
  presses. Timing changes how hard the sword hits, never when it lands.
- **Reward without punishment.** Accurate timing earns up to about 20% more
  damage. Off-beat or sparse pressing still works and still earns some
  reward. The hero never takes damage.
- **Keep the music sync and the juice.** Hit-stop, slow motion, effects,
  sounds and the HUD still land on the music.

### The rule that keeps the demo honest

The simulation may use two kinds of knowledge, and must never use a third:

- **Allowed: the song.** The game owns the song map, so it may schedule
  anything from it in advance: spawn waves, formations, boss slams,
  level-ups, the finale blast and slow motion.
- **Allowed: the past and present.** Game rules react to the presses and
  game state seen so far.
- **Forbidden: future presses.** No game rule may read a press that has not
  happened yet. This includes hit-stop lengths, enemy movement, aiming,
  spawning and the camera.

The virtual player follows the same rule from the other side. It sees the
song map and the current game state, as a human player sees the screen and
knows the song. It never sees future game state.

An automated **causality check** enforces the rule: re-simulating with all
presses after time T removed must reproduce the recording exactly up to T.
See the [Automated checks](#automated-checks) section below.

## Related Information

- [Tech demo design](tech-demo.md): the current demo's full design. This
  document supersedes these parts of it:
  - **Content definitions:** the Moves, Movesets and Enemy types sections.
  - **Planner:** Stages 2, 4, 6 and 7, and the planner output checks.
  - **Time:** the Hit-stop rules.
  - **HUD:** the combo counter and the beat-track markers.
  - **Level content:** the Can-Can moveset table.

  Everything else, including the song map, time warp, world beat grid, juice,
  sound mixing, assets and the renderer, still applies.
- [Interactive play](interactive.md): play mode, which runs these rules live
  with real key presses in an open field (the "later interactive build"
  below).
- [README](../../README.md): commands, URL parameters and the review gates.
- [QA report](../qa/qa-report.md): how each manual QA item of the current
  demo is checked. The new checks below extend it.
- [Crypt of the NecroDancer](https://braceyourselfgames.com/crypt-of-the-necrodancer/):
  the reference for one-step-per-beat movement and a beat track at the
  bottom of the screen.
- [Vampire Survivors](https://poncle.itch.io/vampire-survivors): the reference
  for automatic weapons, enemies that spawn around the player and chase, and
  a huge horde.

## Proposed Design

The game becomes a forward simulation with two parties: the game rules and
the virtual player. The simulation runs to the end of the song at load time,
in the existing planning web worker, and records everything it produces. The
runtime keeps drawing frames as a pure function of song time, now by playing
back the recording.

Precomputing the playthrough is honest because the simulation is causal.
Each step reads only presses that have already happened. A later interactive
build can run the same simulation live, one step per frame, with real key
presses in place of the virtual player. [Interactive play](interactive.md) is
that build.

The parts, in the order a frame of the game experiences them:

- **Song schedule:** game-owned events resolved from the level script and
  song map before the simulation starts.
- **Virtual player:** observes the current state and produces key presses.
- **Movement model:** turns presses and held keys into hero motion.
- **Timing and groove:** rates each press and keeps a running skill score.
- **Auto-aim and damage:** each swing picks its own direction and damages
  everything in its shape.
- **Enemy rules:** spawning, chasing, separation, archers, the boss and
  formations.
- **Recorder:** writes the result in the existing plan format, so the
  evaluator, renderer, effects and sounds keep working.

A press in a step section, from key to screen. The press moves the hero at
once, and the swing it arms strikes on the next beat:

```text
song 88.002  virtual player presses Right (8 ms before the beat at 88.010)
             rating: Perfect, accuracy 1.0, groove 0.93 -> 0.95
             hero starts a 44 px step right, finishing at 88.112
             the sword is armed to strike on the next beat, 88.334
song 88.147  wind-up starts; auto-aim scores 16 directions and picks
             205 degrees (9 enemies in the arc)
song 88.334  impact exactly on the beat, damage 1.0 x 1.19 = 1.19
             impact sound on the beat; 7 enemies die, 2 orc elites flash
```

## Movement Model

> **Superseded** by [Movement: Fixed-Speed
> Strides](gameplay-redesign.md#movement-fixed-speed-strides) in the gameplay
> redesign.

The movement model is the only thing that moves the hero. It is a pure
function of the input so far, so the same code can later read a real
keyboard.

There are two movement modes. The level script chooses one per section, and
the default comes from the song map's `feel` for that section:

- **Step movement** (`pulsed` sections): each press moves one step.
- **Free movement** (`free` sections, such as the slow prelude): held keys
  walk continuously.

### Step movement

> **Superseded** by [The stride
> budget](gameplay-redesign.md#the-stride-budget), [When strides
> start](gameplay-redesign.md#when-strides-start) and [Stride shape and
> direction](gameplay-redesign.md#stride-shape-and-direction) in the gameplay
> redesign.

A **step** is one quick move of fixed length in one of eight directions,
started by a key press. Between steps the hero stands completely still. This
removes the skating.

- **Length:** 44 world pixels, a little more than one 32 px terrain tile.
- **Duration:** 110 ms of world time, with a cubic ease-out, so the hero
  leaves fast and lands firmly.
- **Hop:** a 5 px jump arc, and a small dust puff on landing.
- **Starts on the press.** There is no wind-up before the press, because the
  game cannot know a press is coming.
- **Quiet:** a step makes no sharp sound, only a soft rustle well under the
  music. Its landing puff is visual. The only percussive sounds are the
  impacts, which are on the grid.
- **Diagonals:** the first key starts the step immediately. If a
  perpendicular key arrives within 40 ms, the rest of the step bends
  toward the diagonal and still covers 44 px in total. Bending only reacts
  to presses that have happened, so no future knowledge is needed.
- **Interrupted steps:** a press during a step starts a new step from the
  hero's current position.
- **Walls:** a step that would leave the combat corridor (the walkable band
  of the forest map) is shortened to stop at the edge.
- **Enemies do not block steps.** The hero passes through the crowd, and
  enemies are pushed aside by separation.
- **No presses means no movement.** The hero stands still and keeps
  swinging. See [Swings in each mode](#swings-in-each-mode) below.

The step's position over its duration, where `u` runs from 0 to 1:

```ts
const u = clamp((w - wPress) / STEP_WORLD_S, 0, 1);
const e = 1 - (1 - u) ** 3;                 // cubic ease-out
pos = start + dir * STEP_LENGTH * e;
z = 5 * Math.sin(Math.PI * u);              // hop arc
```

### Free movement

> **Superseded** by [Without a steady
> beat](gameplay-redesign.md#without-a-steady-beat) in the gameplay redesign.

In free movement, the hero walks while direction keys are held, like a
Vampire Survivors character.

- **Speed:** 80 world pixels per world-second, in eight directions.
- **No inertia:** full speed within 60 ms of a key going down, stopped within
  60 ms of the last key going up.
- **The walk cycle matches distance:** one full walk cycle per 64 px
  travelled, so the feet never slide.
- **Facing:** the hero faces the walk direction, except while a swing plays.
- **Presses are not rated** for timing in free movement.

### Switching modes

> **Superseded** by [Without a steady
> beat](gameplay-redesign.md#without-a-steady-beat) in the gameplay redesign.

A section boundary can switch the mode. The player needs warning before the
beat starts to matter.

- **Count-in:** during the last bar before a step section, the beat track
  fades in and four count marks pulse on the beats.
- **Entering step movement:** any walk in progress stops on the boundary.
  Held keys do nothing until they are pressed again.
- **Entering free movement:** the beat track becomes the flowing ribbon of
  the current demo, and held keys walk.

### Animation

The hero's animation follows directly from the movement model and swings:

- **Swinging:** the swing animation for its full length, timed so its
  impact frame shows at the strike time, facing the aim direction. A
  wind-up that overlaps a step reads as a jumping slash.
- **Stepping without a swing:** the jump animation over the step's duration.
- **Walking:** the walk cycle, advanced by distance.
- **Standing:** the idle animation, breathing on the beat.

## Timing, Groove and Damage

> **Superseded** by [Timing and the
> Groove](gameplay-redesign.md#timing-and-the-groove) in the gameplay
> redesign.

Accurate timing raises damage a little, never a lot. Nothing about timing
punishes the player. There is no "miss" rating, no damage taken and no
combo that resets.

### Rating a press

> **Superseded** by [Ratings](gameplay-redesign.md#ratings) and [Press
> classes](gameplay-redesign.md#press-classes) in the gameplay redesign.

Every press in step movement is rated against the nearest point on the
section's **timing grid**. That grid is the beat by default; a section may
use eighth notes or bars instead.

`delta` is the press time minus the nearest grid time, in song time. Its
size maps to an **accuracy** between 0.1 and 1.0:

```text
|delta| ms   rating     accuracy
0 - 35       Perfect    1.0
35 - 70      Great      1.0 -> 0.75 (linear)
70 - 120     Good       0.75 -> 0.4
120 and up   (none)     0.4 -> 0.1, reached at 250 ms or half a grid step
```

Any press, however late, still moves the hero at once and arms a swing.
The swing strikes on the grid either way. See
[When the sword strikes](#when-the-sword-strikes) below.

### The groove meter

> **Superseded** by [Press classes](gameplay-redesign.md#press-classes),
> [Drain](gameplay-redesign.md#drain) and [Tiers](gameplay-redesign.md#tiers)
> in the gameplay redesign.

The **groove meter** is a number from 0 to 1 that tracks recent timing
accuracy. It is the player's running skill score.

- **Each rated press** moves it 30% of the way toward that press's
  accuracy: `groove += 0.3 * (accuracy - groove)`.
- **Press rate does not matter.** Pressing perfectly once per bar keeps the
  meter as high as pressing perfectly on every beat. Pressing more often is
  rewarded separately, because every press arms a swing.
- **Idle decay:** with no presses in step movement, the meter halves every
  8 bars.
- **Frozen in free movement:** a quiet passage is a rest for the groove too.
- **Starts at 0** at the beginning of the song.

### Damage

> **Superseded** by [What a tier
> changes](gameplay-redesign.md#what-a-tier-changes) in the gameplay redesign.

All hero damage is multiplied by the groove:

```ts
const damageMultiplier = 1 + 0.2 * groove;  // 1.0 to 1.2
const damage = baseDamage * damageMultiplier;
```

The multiplier applies to the sword and to every automatic weapon.

Enemy health is set so the bonus shows up in play. A light swing deals 1.0
base damage. Most regular enemies have health 1.0 or less and die in one
swing at any groove. Tougher regulars have health just above 1.0. They die
to one swing only when the groove is high:

```text
enemy           health   light swings to kill
bat, bee        0.6      1
goblin          1.0      1
skeleton-ember  1.1      1 if groove >= 0.5, else 2
orc, orc-ember  1.15     1 if groove >= 0.75, else 2
orc-elite       3.0      3 at any groove, or a cleave plus 1
```

A skilled player therefore cuts through orc lines in one pass, while a
sloppier player needs a second swing. The difference is visible but never
blocks progress.

### Heavy swings on accents

> **Superseded** by [Accents and
> cleaves](gameplay-redesign.md#accents-and-cleaves) in the gameplay redesign.

An **accent** is a loud, marked moment in the song map, such as a tutti
chord. When a swing strikes on an accent, and the press that armed it was
rated Great or better, it becomes a **cleave**, a heavy swing. A skilled
player therefore presses one beat before each big chord, and the cleave
lands on the chord:

- **Shape:** an arc of radius 130 px and 200 degrees.
- **Damage:** 2.5 base damage.
- **Hit-stop:** heavy swings freeze the world briefly. See
  [Hit-stop](#hit-stop) below.
- **Shockwave:** the cleave sends a crescent-shaped wave outward along its
  aim. The wave travels from 130 px to 330 px in 25 px bands, one band per
  sixteenth note. Each band deals 1.0 base damage to the enemies in it.
- **Threshold:** each section sets the minimum accent strength, 0.7 by
  default. A section may also limit heavy swings to one accent label, as the
  galop opening does with `tutti-chord`.

The shockwave gives skilled players a big reward for hitting the loudest
moments. Enemies in a line die one after another on sixteenth notes.

## Swings and Auto-Aim

The sword is an automatic weapon. The player triggers it by pressing, or the
game triggers it on a timer, and the game always picks the direction.

### When the sword strikes

> **Superseded** by [Firing](gameplay-redesign.md#firing) in the gameplay
> redesign.

Every swing strikes exactly on a time from the song's grid. The game knows
those times in advance, because they come from the song, so every swing can
play a full wind-up and every impact sound can be scheduled on the beat.

- **A press arms the sword.** The armed swing strikes on the first grid time
  at least 90 ms after the press. For an on-time press, that is the next
  beat.
- **One swing per grid time.** A press whose strike time is already taken
  only steps.
- **Wind-up:** the swing animation starts so its impact frame lands on the
  strike time, about 190 ms before it. When the press comes later than
  that, the wind-up plays faster to fit, but never in less than 90 ms.
- **Hits resolve at the strike time,** against the enemies' positions then.

Pressing on beat 1 therefore steps on beat 1 and strikes on beat 2. Pressing
on every beat gives a step and a strike on every beat, a little dance of
step-and-slash. A late press still strikes on the beat: a casual player's
steps drift off the beat, but their sword never does.

```text
grid (beats)   88.010        88.334        88.658
press          88.002 Right  88.341 Up
step           88.002        88.341
strike                       88.334        88.658
```

### Swings in each mode

> **Superseded** by [Firing](gameplay-redesign.md#firing) and [What a tier
> changes](gameplay-redesign.md#what-a-tier-changes) in the gameplay redesign.

- **Step movement:** every press arms one swing, as above.
- **Idle swings in step movement:** if a bar ends with no swing in it, the
  sword swings by itself on the next downbeat. A player who stops pressing
  still fights.
- **Free movement:** the sword swings by itself every two beats. On song-map
  anchors of strength 0.5 or more, it cleaves instead. **Anchors** are the
  strong moments the song map marks in free-feel sections, where there is no
  steady beat.

Automatic swings strike on the grid in the same way, with a full wind-up.

### Choosing the direction

Auto-aim picks the direction that hits the most valuable targets. It reads
only the game state at the start of the wind-up, when the hero turns to
face the swing.

```ts
function aim(state: SimState, shape: ArcShape): number {
  let best = { score: 0, dir: state.hero.facingAngle };
  for (let k = 0; k < 16; k++) {
    const dir = (k / 16) * TAU;
    const score = sumOf(enemiesInArc(state, shape, dir), (e) => e.aimWeight);
    if (score > best.score) best = { score, dir };
  }
  return best.score > 0 ? best.dir : towardNearestEnemy(state, 2 * shape.radius) ?? best.dir;
}
```

- **Aim weights:** the boss counts 5, elites 2 and regular enemies 1.
- **Ties** go to the direction nearest the nearest enemy.
- **Nobody in reach:** aim at the nearest enemy within twice the reach.
- **Nobody near:** keep the current facing.

### Hits

A hit damages every enemy inside its shape at the hit time, as in a normal
action game.

- **Lethal hits:** the enemy plays its fall animation and death effect.
- **Non-lethal hits:** a white flash and a 10 px knockback away from the hero.
- **Light swing shape:** an arc of radius 100 px and 150 degrees.

## Enemy Rules

Enemies follow game rules that react to the hero. The song schedules when
things happen, and the game state decides where.

### Spawning

> **Superseded** by [The steady
> stream](gameplay-redesign.md#the-steady-stream) and [Set
> pieces](gameplay-redesign.md#set-pieces) in the gameplay redesign.

The **spawn director** keeps the living population near the level's target
curve. It knows the curve in advance because the level script defines it.

- **When:** on every beat (in song time), it computes the shortfall:
  `target population - living enemies`.
- **How many:** up to the shortfall, at most 12 per beat.
- **Which types:** drawn by weight from the section's roster, with a seeded
  random generator.
- **Where:** just outside the camera view. 60% of spawns come from the
  forward half (along the route ahead), the rest from anywhere around. A
  point outside the corridor is redrawn, up to 8 times.
- **On the beat:** each enemy enters on its movement group's subdivision.
  A **movement group** is the subdivision an enemy type moves on, such as
  every eighth note for bats or every bar for orcs.
- **Recycling:** an enemy more than 1.5 view widths behind the hero moves to
  a new spawn point ahead, as in Vampire Survivors. This does not count as
  a kill.

### Movement

Regular enemies chase the hero with the current demo's rhythmic motion:

- **Steering:** toward the hero, at the type's speed, multiplied by the
  movement group's surge on its subdivision.
- **Separation:** neighbors closer than 0.8 body widths push apart. A spatial
  hash keeps this fast.
- **Contact:** an enemy stops at the hero's radius plus its own plus 4 px.
  It plays its attack animation on its subdivision. Contact never damages
  the hero.
- **Walk cycles** advance by distance, like the hero's.

### Archers

> **Superseded** by [Health and
> Damage](gameplay-redesign.md#health-and-damage) in the gameplay redesign.

Archers keep their distance and shoot on scheduled beats.

- **Range:** an archer approaches to 220 px and stops.
- **Volleys** follow the level script's schedule, for example every two
  bars.
- **Telegraph:** the archer draws for one beat. It aims at the hero's
  position at the moment of release, not at a predicted position.
- **Flight:** the arrow flies for one beat in a straight line.
- **Near miss:** an arrow passing within 24 px of the hero without touching
  it triggers the near-miss juice: a short slow-motion dip, a whoosh and a
  "PERFECT" callout.
- **Hits:** an arrow that touches the hero shatters in a spark. There is no
  damage and no other penalty.

### The boss

The minotaur boss is a scripted encounter whose timing comes from the music.
Its movement and targeting come from the game state.

- **Entrance:** it lands on the downbeat the level script names, knocking
  nearby enemies back.
- **Movement:** it chases slowly on its two-bar movement group.
- **Slams:** on scheduled beats, a marker appears at the hero's current
  position and grows for two beats. The slam then lands on the marker.
  Leaving the marker earns a near miss.
- **Health:** 175 points, tuned so a skilled player kills it a few seconds
  before the closing chords. Its health is real: there is no floor holding
  it alive for a scripted moment. The killing hit plays the boss-kill effect
  and hit-stop. A player who hasn't worn it down sees it caught by the
  finale blast instead.
- **Slam window:** the level script's `slamsUntil` reference spreads the
  slams from four bars after the entrance to four bars before it.

### Formations

A **formation** is a group of enemies that enters as one rigid shape, such as
a wedge of bees or the kick line of dancing skeletons. Formations stay
game-scripted, because their timing comes from the music.

- **Entrance:** the formation enters at its scheduled time, from its side of
  the screen relative to the camera at that moment.
- **Rigid until release:** members follow their slots and dance on the beat.
  They can be hit and killed while in formation.
- **Release:** at the scheduled release, such as the final accent of a
  phrase, the surviving members become normal chasing enemies.
- **The kick line** now ends with the line charging the hero on its release
  accent. A skilled player meets it with an accent cleave, whose shockwave
  cuts down the skeletons one per sixteenth note.

### The finale blast

The finale blast stays a game-owned event on the final chord. A ring expands
from the hero across the screen in sixteenth-note bands and kills every
remaining enemy.

## Time, Hit-Stop and the Camera

Song time and world time keep their current meanings. **Song time** is
seconds into the audio. **World time** is the clock game motion runs on, and
it slows down or freezes for slow motion and hit-stop. The **time warp** maps
song time to world time.

The simulation advances in fixed steps of 1/120 s of song time. Each step
advances world time by the current rate times the step. The warp is built as
the simulation runs, so it records only what has already happened.

```text
rate = sectionTimeScale(s) * nearMissDip(s) * (inHitStop(s) ? 0 : 1)
w += rate * (1 / 120)
```

Presses fall between simulation steps. A press takes effect at its exact
time: its rating and step start use the press timestamp, not the step
boundary. Strikes, weapon hits and other impacts happen at their exact grid
times, also between steps.

### Hit-stop

A **hit-stop** freezes world time for a moment after a heavy impact. The
music keeps playing. The old rule shortened each hit-stop so it ended before
the next planned hero attack. That needs future knowledge, so it is
replaced:

- **Length:** `min(move.hitStopMs * (0.6 + 0.4 * accentStrength), 150 ms)`,
  skipped if under 40 ms.
- **A press ends the hit-stop early.** World time resumes at the press, and
  the press is processed normally.
- **No stacking:** a heavy impact during a hit-stop does not start another.
- **Weapons rest:** an automatic weapon firing that falls inside a hit-stop
  moves to the next slot of its subdivision.

### Camera

The camera follows the recorded hero with a critically damped spring, plus
the section's lead along the route. A spring only reacts to past motion, so
the camera is causal. The camera does not look at future hero positions.

## The Virtual Player

> **Superseded** by [Virtual Players](gameplay-redesign.md#virtual-players) in
> the gameplay redesign.

The virtual player is a deterministic program that plays like a highly
skilled human. It knows the song, as a practiced player would. Each time it
acts, it receives only a snapshot of the current game state:

```ts
interface Observation {
  s: number;                      // song time now
  hero: { x: number; y: number; stepping: boolean };
  enemies: { x: number; y: number; type: string; health: number }[];
  threats: { kind: "arrow" | "slam"; x: number; y: number; dir?: number; landsAt: number }[];
  pacePoint: { x: number; y: number };
}
```

It returns the presses and key releases it makes before its next decision.
It cannot see the future of the simulation.

### When it presses

- **Press pattern:** each section's `presses` pattern in the level script
  says which grid slots the skilled player presses. One character per
  slot: `x` presses and `.` rests. For example, `x.` presses once per 2/4
  bar and `xx` presses every beat.
- **Accents:** it always presses one grid step before an accent strong
  enough for a cleave, even on a rest slot, so the cleave strikes on the
  accent.
- **Strikes follow presses:** each press's swing strikes one grid step
  later. The pattern `.x` presses on beat 2 of a 2/4 bar, so its swings
  strike on downbeats.
- **Human timing:** each press lands at its grid time plus a seeded random
  error drawn from its skill profile. See
  [Skill profiles](#skill-profiles) below.
- **Reacting to threats:** it reacts to a threat only after it has been
  visible for 150 ms. If dodging needs a press on a rest slot, it adds one
  on the nearest beat.

### Where it steps

> **Superseded** by [Virtual Players](gameplay-redesign.md#virtual-players) in
> the gameplay redesign.

For each press, the virtual player scores the eight directions by where the
step would leave the hero, and picks the lowest cost:

```ts
cost =
    1.0 * distanceFromPacePoint(after)    // keep up with the route
  + 50  * insideThreat(after)              // leave slam markers and arrow lines
  + 2.0 * nearCorridorEdge(after)          // stay off the walls
  + 0.5 * enemiesWithin(after, 30)         // don't get buried
  - 0.8 * enemiesWithin(after, 100)        // but stay in the fight
  + 0.6 * reversesLastStep(dir)            // avoid back-and-forth jitter
  + noise(seed, pressIndex) * 0.2;         // a little variety
```

The **pace point** is where the route schedule says the hero should be now.
The **route schedule** is the hero's planned distance along the forest
route, from each section's `pace` in the level script. It is song-owned, so
the virtual player may know it. The weights above are starting values for
tuning, not fixed requirements.

In free movement, the virtual player holds the direction key toward the
pace point plus a slow weave across the corridor. It changes held keys at
most once per beat.

### Skill profiles

> **Superseded** by [Virtual Players](gameplay-redesign.md#virtual-players)
> and [The Difficulty Curve](gameplay-redesign.md#the-difficulty-curve) in the
> gameplay redesign.

A **skill profile** sets how the virtual player presses. The level script
names the default, and the `?player=` URL parameter overrides it for
review:

```text
profile   timing error            presses
perfect   0 ms                    the section's pattern
skilled   normal, sd 12 ms,       the section's pattern (the default)
          clamped to +/-40 ms
casual    normal, sd 45 ms,       downbeats only, plus accents
          clamped to +/-150 ms
```

`?player=` re-runs the simulation, like `?seed=`. The casual profile exists
so reviewers can see the reward spectrum. Signature moments, such as the
boss dying on its chord, are only required to work for the skilled and
perfect profiles.

## Content and Level Script

Moves, movesets and per-section choreography are replaced by hero settings,
enemy health and per-section movement settings.

### Hero settings

> **Superseded** by [Migration and
> Compatibility](gameplay-redesign.md#migration-and-compatibility) in the
> gameplay redesign.

A new hero content file holds the movement model, timing windows, groove and
swings:

```yaml
step: { length: 44, durationMs: 110, hop: 5, diagonalWindowMs: 40 }
walk: { speed: 80, accelMs: 60, strideCycle: 64 }
timing: { perfectMs: 35, greatMs: 70, goodMs: 120, floor: 0.1 }
groove: { follow: 0.3, idleHalfLifeBars: 8, maxBonus: 0.2 }
swings:
  light: { animations: [slash, backslash, halfslash], hitShape: { kind: arc, radius: 100, degrees: 150 }, damage: 1.0 }
  heavy: { animation: slash-oversize, hitShape: { kind: arc, radius: 130, degrees: 200 }, damage: 2.5, hitStopMs: 110, wave: { to: 330, band: 25, damage: 1.0 } }
aimWeights: { regular: 1, elite: 2, boss: 5 }
```

Light swings cycle through their three animations for variety.

### Moves that go away

These moves and movesets are removed, because no input can produce them:

- **Removed moves:** thrust, spin, dash-strike, sweep, burst and the dodge
  move. A dodge is now an ordinary step out of a threat.
- **Removed moveset features:** the glide and dash locomotion modes, path
  shapes, direction cycles, attack patterns and the spring-back after
  moves.
- **Kept, in a new role:** cleave becomes the heavy accent swing, and
  finisher becomes the boss-kill effect on whatever swing kills the boss.

### Enemies and weapons

- **Enemies** gain `health`, see the table in [Damage](#damage), and a
  `behavior`: `chase`, `archer` or `boss`. The planning-only `lifetime` range
  and the elite `hits` count are removed.
- **Weapons** gain a base `damage` per hit: orbiting blades 0.6 to each
  enemy a blade touches, chain lightning 1.2 per jump, fire trail 0.4 per
  beat, and the finale blast enough to kill anything. The weapons'
  planning-only `killsPerFiring` range is removed.
- **Weapons hit only on their subdivision.** Blades damage what they touch
  on each eighth note, not continuously, so weapon impact sounds are on the
  grid too.

### Level script

Each section replaces `moveset` with movement settings. Everything else,
including zones, roster, population and camera, stays.

```yaml
- bars: 54-69
  section: theme-a
  movement: steps          # or free; defaults from the song map's feel
  pace: 50                 # route speed, world px per world-second
  presses: "xx"            # skilled player: every beat of the 2/4 bar
  timingGrid: beat         # optional: eighth, beat or bar
  heavyOnAccents: { minStrength: 0.7 }
```

The skilled player's presses for the Can-Can level:

```text
section          movement  presses  why
prelude          free      -        slow, rubato; walking
galop-vamp       steps     .x       the quiet vamp already has the galop's pulse
galop-opening    steps     .x       once per bar; cleaves on tutti chords
theme-a          steps     xx       every beat (about 180 per minute)
theme-a-repeat   steps     xx
kick-line        steps     .x       once per bar while the line dances
bridge           steps     xx
climax           steps     xx
coda             steps     .x       about 115 per minute at 230 BPM
```

The loader checks that each section's pace is reachable. Forward progress
must be at most 80% of `step.length` times the press rate. Otherwise the
loader fails with a line-numbered error, as for other content errors:

```text
levels/orpheus-galop.yaml:47: section kick-line: pace 70 needs 1.6 steps per
second forward, but ".x" presses only 1.47 times per second
```

### Level-ups and experience

> **Superseded** by [Gems, Pickups and
> Level-Ups](gameplay-redesign.md#gems-pickups-and-level-ups) in the gameplay
> redesign.

Level-ups stay on the musical moments the level script names, such as the
start of a section. Experience must be earned honestly:

- **Gems:** every death drops gems, which fly to the hero.
- **The XP bar** fills from collected gems toward the next level-up's
  requirement. Requirements are tuned so the skilled player fills the bar a
  little before each scheduled level-up.
- **A full bar waits** and glows until the scheduled moment, then levels up.
- **A bar not yet full** levels up on the first downbeat after it fills.

## Recording and Playback

The **recording** is the simulation's output: everything the renderer needs
to draw any song time. It reuses the existing plan format, so the evaluator,
renderer, effect recipes, sound scheduling and HUD keep working.

- **Hero track:** position, height, facing, animation and frame, at 60
  samples per world-second.
- **Enemies:** spawn time, position samples at 30 per world-second, and the
  hits each one received, lethal or not.
- **Hits, effects, sounds and gems:** produced from simulation events by the
  existing effect and sound emission stage. The sound voice budget is
  enforced as sounds are emitted, which needs no future knowledge.
- **Time warp:** the breakpoints recorded during the simulation.
- **New fields:**
  - **Inputs:** every press and key release, with its time, keys, rating
    and accuracy.
  - **Groove series:** the groove meter's value over time.
- **Format version:** the plan format version is raised, so cached plans
  from the old planner are never loaded.

The planning pipeline becomes:

1. Resolve musical time and the song schedule.
2. Generate the map. The route length comes from the pace schedule in song
   time, plus a margin, because hit-stop lengths are not known yet.
3. Run the simulation with the virtual player.
4. Plan the camera from the recorded hero.
5. Emit effects, sounds, gems and HUD data.
6. Run the automated checks.

### Performance

- **Simulation time:** at most 3 seconds for the 3-minute song on the
  reference machine. The loading progress bar covers it, and the existing
  plan cache makes reloads instant.
- **Frame time:** unchanged, because the runtime still plays back a
  recording.
- **Determinism:** the same inputs, seed and profile produce a
  byte-identical recording.

## HUD

> **Superseded** by [HUD](gameplay-redesign.md#hud) in the gameplay redesign.

The HUD should make it obvious that a player is pressing keys to the beat.

- **Key display:** four arrow keys at the bottom left. Each lights up when
  pressed, tinted by the press's rating: gold for Perfect, white for Great,
  pale blue for Good, and plain for unrated presses.
- **Beat track:** beat ticks slide toward the beating heart. Future ticks
  come from the song, which the game knows. Presses appear on the track as
  they happen, colored by rating. The track no longer shows upcoming hero
  actions, because the game cannot know them.
- **Groove meter:** replaces the combo counter. A bar beside the heart
  shows the groove, with the damage bonus as a percentage, such as "+19%".
  The hero's sword trail brightens with the groove.
- **Callouts:** "PERFECT" on Perfect presses that land on accents, and on
  near misses. Ordinary presses show their rating only on the beat track
  and key display, to keep the screen readable at three presses a second.
- **End card:** kills, Perfect, Great and Good counts, average groove, then
  the credits roll.

## Automated Checks

> **Superseded** by [Automated Checks](gameplay-redesign.md#automated-checks)
> in the gameplay redesign.

The planner's output checks are replaced by simulation checks. They run when
the recording is built and in the test suite, and every failure names the
time and entity involved.

Checks that keep the demo on the music:

- **Impacts on the grid:** every sword strike, and its impact sound, lands
  within 1 ms of a grid time, for every skill profile. Weapon hits land on
  their subdivisions, shockwave bands on sixteenth notes, and slams and
  arrow arrivals on beats.
- **Sound budget:** no sixteenth-note slot exceeds the voice budget.

Checks that prove the demo is honest:

- **Causality:** for three cut times T, re-simulate with every press after T
  removed. The recording must match the full run exactly up to T. A cut
  stops the virtual player; the game keeps running.
- **Every hero movement has an input:** in step movement, hero displacement
  happens only within 110 ms (world time) after a press. In free movement,
  movement happens only while a key is held. No step exceeds 44 px.
- **No skating:** while walking, the walk cycle advances within 10% of the
  rate implied by the distance travelled.
- **Auto-aim is the rule:** re-running `aim` on the recorded state at every
  swing reproduces the recorded direction.
- **Damage adds up:** every enemy inside a hit shape at the hit time took
  that hit's damage. Every death follows hits whose damage totals at least
  the enemy's health.
- **Determinism:** two runs produce the same recording hash.

Checks that keep the demo looking right:

- **Off-screen spawns:** every non-formation enemy spawns fully outside the
  camera view plus a 16 px margin.
- **Population:** from the fourth bar of each section, the living population
  stays within 25% of the target curve. It never exceeds 520. The finale
  blast is exempt.
- **Spacing:** at most 1% of sampled enemy pairs are closer than 0.5 body
  widths. Formation members and dying enemies are exempt.
- **Clean ending:** every enemy has died by the end of the finale blast.
- **Pace:** the hero stays within 60 px ahead of and 140 px behind the pace
  point, measured along the route. This is required for the skilled and
  perfect profiles only.
- **Signature moments**, for the skilled and perfect profiles:
  - At least 90% of threats end as near misses.
  - Every kick-line skeleton dies within two beats of the release.

`check:plan`, `check:determinism` and `check:seeds` keep their names and
run these checks. `check:plan` also runs the casual profile, and reports
its kills and groove next to the skilled run's.

## Implementation Plan

The work lands as a sequence of commits. Each task passes `scripts/ci`, and
each is delivered through its own Tollgate worktree as described in
[AGENTS.md](../../AGENTS.md). Tasks marked **Gate** end with a human
review in the browser.

1. **Movement, timing and aim as pure modules.**
   - The step and walk motion, press rating, groove meter, damage
     multiplier and auto-aim functions, with unit tests.
   - The hero content file and its schema.
   - Nothing is wired into the planner yet.
2. **The simulation, end to end.** This is the integration point, and the
   largest task.
   - A fixed-step simulation with the spawn director, chasing enemies,
     separation, light swings, health and deaths.
   - The skilled virtual player in step movement, with the press patterns
     and pace following.
   - Hit-stop as specified, and the causal camera.
   - Recording into the plan format, including the new input and groove
     fields, and the raised format version.
   - The level script and content migration: the `moveset` to `movement`
     change, enemy `health` and `behavior`, and weapon `damage`. Removal of
     movesets, the removed moves, and Stages 4, 6 and 7 of the old planner.
     Stage 2's weapon firings and threat schedule move into the song
     schedule; its move choice is removed.
   - The simulation checks, replacing the old planner output checks. Free
     sections may use step movement until task 3.
   - **Gate:** watch theme A (`?start=1:10`) and confirm the assembled
     check in [Manual QA](#manual-qa).
3. **Free movement and mode switches.**
   - Walking with held keys, the distance-matched walk cycle and the
     virtual player's free-movement steering.
   - Automatic swings every two beats and cleaves on anchors.
   - Idle swings in step movement, and the count-in before step sections.
4. **Accents and scripted encounters.**
   - Heavy accent cleaves and the shockwave.
   - Archers, arrows and near misses, with the virtual player's 150 ms
     reaction.
   - The boss: slams, health and death on the chord.
   - Formations, including the kick line's release charge.
   - Level-ups from earned experience, and the finale blast.
   - The signature-moment checks.
5. **HUD and skill profiles.**
   - The key display, the new beat track, the groove meter, callouts and
     the end card.
   - The `perfect`, `skilled` and `casual` profiles and `?player=`.
   - The casual run added to `check:plan`.
   - **Gate:** compare `?player=skilled` with `?player=casual`.
6. **Tuning, QA tooling and documentation.**
   - Tune enemy speeds, spawn rates, health, the virtual player's weights
     and experience requirements until every check passes on all seeds.
   - Update `npm run qa`, the capture times, the end-to-end tests and the
     QA report for the new HUD and checks.
   - Update the README, and add notes to the superseded sections of
     [tech-demo.md](tech-demo.md) pointing here.
   - **Gate:** the full playthrough in [Manual QA](#manual-qa).

## Tuning Notes

> **Superseded** by [Starting values](gameplay-redesign.md#starting-values)
> and [The Difficulty Curve](gameplay-redesign.md#the-difficulty-curve) in the
> gameplay redesign.

The implementation follows this document, with these values and rules
settled during tuning (task 6). Each keeps every check passing on all seeds.

- **Spawning:** up to 120 enemies per beat, not 12. A cleave's shockwave can
  clear a few hundred enemies in half a second, and the horde must re-flood
  from off-screen within a beat to stay on the population curve. In free
  movement the director also runs on every sixteenth note, because its beats
  are slow. Each spawn's first recorded sample is its spawn spot.
- **Auto-aim:** the boss weighs 1000 and formation members 20 (a new weight),
  so the sword finds them in a crowd of hundreds. Aim counts enemies whose
  centre is in the arc, while the swing catches any body overlapping it, so a
  target that moves a little between the wind-up and the strike is still hit. A cleave aims with its
  shockwave's reach (330 px), not just its arc.
- **Cleave shockwave:** each band reaches everyone between the arc's edge and
  its own front, and hits each enemy at most once, so charging enemies cannot
  slip between bands. Bands keep their sixteenth-note slots; a band inside a
  hit-stop is skipped and the next one covers its ground.
- **Archers** of a volley share one post, shoulder to shoulder 20 px apart,
  so their arrows fly nearly parallel and one step can near-miss both. The
  virtual player dodges on the release beat; it judges a step by where it
  will have taken the hero when the threat lands.
- **Slams:** the marker is 28 px across the middle. Leaving it before the
  slam lands is a near miss, however far the hero went.
- **The boss** lands 110 px ahead of the hero. Slams it has marked but not
  landed are dropped when it dies.
- **The kick line** rides with the camera-smoothed hero, 170 px ahead along
  the route, 28 px between dancers. Its release is treated as an accent, so a
  skilled player meets it with a cleave, and the line charges at four times
  its walking speed.
- **The finale blast's** bands are growing discs: each kills everyone its
  front has reached.
- **Groove decay** starts two bars after the last press, so pressing once per
  bar never decays.
- **Experience:** 400, 1000 and 2000 gems for the three level-ups. The
  skilled player levels up on schedule; the casual player one to four
  seconds late.
- **The casual profile's** timing error is normal with an 120 ms deviation,
  clamped to ±250 ms. With the accuracy table above, the 45 ms of the
  starting value earns almost the skilled player's bonus (+18%); 120 ms
  gives about +13%, against the skilled +20%.
- **The galop vamp** (bars 15–30, 0:43.2–0:54.1) is its own pulsed section in
  the song map, not the end of the prelude: it already has the galop's steady
  pulse at about 176 BPM. The hero steps to it at full speed (time scale 1),
  once per bar, and the count-in moves to the last four beats of the rubato
  passage.
- **Kill sounds** in free movement group per beat, not per sixteenth note,
  and use the short single-kill sound, so the quiet prelude keeps its
  dynamics.

## Manual QA

> **Superseded** by [Manual QA](gameplay-redesign.md#manual-qa) in the
> gameplay redesign.

Run the dev server from a worktree (`npm run dev`, port from `npm run port`),
open it in desktop Chrome and click to begin. `?start=`, `?player=`,
`?seed=` and `?capture&t=` put the demo into each state below.

### First assembled check

Do this as soon as the simulation runs end to end, before broader review.

- **Open `?start=1:10`** (theme A, step movement, every-beat presses).
- **The hero stands still between steps.** Pause on two captures inside the
  same beat gap, such as `?capture&t=75.20` and `?capture&t=75.30` (between
  the beats at 75.02 and 75.37). The hero is in the same place in both.
- **Each step is quick and firm:** a short hop, a landing puff, then
  stillness. There is no sliding back afterwards.
- **Swings go toward the crowd.** When enemies crowd one side, the swings
  face that side, whatever direction the hero stepped.
- **Enemies chase and surround** the hero. Nobody walks to a fixed spot and
  waits.
- **Every sword impact is on the beat** to the ear, one beat after the step
  that armed it.

### Full playthrough

Play the whole song at `?player=skilled` with headphones.

- **Prelude:** the hero walks smoothly with matched footsteps, swings every
  two beats, and cleaves on strong anchors. The beat track shows the ribbon.
- **Count-in:** in the bar before the galop, the beat track fades in and
  counts four.
- **Galop opening:** one press per bar, with cleaves and hit-stops on the
  tutti chords.
- **Theme A:** a step and swing on every beat. Orcs die in one swing once
  the groove meter is high.
- **Kick line:** the line dances, then charges on the release accent, and
  the cleave's shockwave cuts down the skeletons in a ripple.
- **Archers:** arrows are telegraphed. The hero sidesteps, and near misses
  slow time briefly and show "PERFECT".
- **Boss:** slam markers appear under the hero, the hero steps off them,
  and the boss dies on the chord with the boss-kill effect.
- **Finale:** the blast clears the screen on the final chord, then the end
  card shows the rating counts and average groove.
- **Throughout:** the key display matches each step direction, and the
  groove bonus sits around +18% to +20%.

### The reward spectrum

- **Open `?player=casual`.** The game is visibly calmer: one press per bar,
  lower groove (around +12% to +15%; see [Tuning Notes](#tuning-notes)), and
  orcs often needing two swings.
- **Casual impacts stay on the beat.** The casual player's steps drift off
  the beat, but every sword impact and impact sound is still exactly on
  it.
- **The demo still progresses.** The casual run reaches the finale and the
  blast clears the screen. Its boss usually survives until the blast
  catches it.
- **Nothing punishes the player.** There is no miss rating, health loss or
  combo reset at any point.
- **Compare with `?player=perfect`:** every press is Perfect, and the
  groove bonus reaches +20%.

### Edge cases

- **A press during a hit-stop:** find a cleave followed quickly by a press.
  The freeze ends at the press, and the step starts immediately.
- **A diagonal step:** find a press where the key display shows two keys.
  The hero moves diagonally the same 44 px, with no visible kink.
- **The corridor edge:** a step toward the forest edge stops at the edge
  instead of entering the trees.
- **Other seeds:** `?seed=1859` plays through with the same feel, and every
  check passes.
- **Determinism:** reloading the same URL plays an identical run, loaded
  from the plan cache.
