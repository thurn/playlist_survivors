# 3D Tech Demo: Choreography

## The Choreography Layer

The choreography layer stands in for a game's simulation. It is plain C#
(netstandard2.1, `System.Numerics` for vectors, no Unity types), compiled by
Unity through an assembly definition with `noEngineReferences: true`, and also
built and tested by `dotnet test` outside Unity. It is a local Unity package
both demos share,
`unity-packages/com.playlistsurvivors.choreography/` (namespace
`PS.Choreography`); demo3d's data and the code that loads it from a checkout
stay in `demo3d/Assets/Demo3D/Choreography/`.

The code stays within C# 9, the version Unity's compiler is documented to
accept (TD1 confirms it for 6.6). So the timeline's types are plain
`readonly struct`s, not C# 10 record structs.

### Timeline contract

The timeline is the only interface between the choreography layer and the
client. It lives in memory after load. A debug command dumps it to JSON for
tests and inspection.

Each entity is the hero, a monster of one roster type, or a projectile. The
timeline's entity table says which, and when it exists:

```csharp
public enum EntityKind { Hero, Monster, Projectile }
public readonly struct EntityInfo {
    public readonly int Id, Type;          // Type: roster or projectile index
    public readonly EntityKind Kind;
    public readonly double Spawn, Despawn; // song seconds
    public readonly uint Seed;             // tint and scale jitter
}
```

An entity's motion is a list of segments, sorted by start time. A segment
moves it from one ground position and facing to another, with an easing
curve:

```csharp
public enum Ease { Pulse, Linear }       // Pulse: one gait step
public readonly struct MotionSegment {
    public readonly int Entity;
    public readonly double T0, T1;       // song seconds
    public readonly Vector2 From, To;    // ground plane, metres
    public readonly float FacingFrom, FacingTo;  // radians
    public readonly Ease Ease;
}
```

A `Pulse` segment is one gait step. Its progress lunges early and settles
before the next beat, the gait feel of the repo's 2D game. With `u` running
from 0 to 1 over the segment:

```
progress(u) = 1 - (1 - u)^3      // fast after the beat, settled by the next
```

Actions are events stamped with the moment of impact, not the moment the
animation starts. The client works backwards from the impact to start each
clip (see [Clip annotations and retiming](#clip-annotations-and-retiming)):

```csharp
public enum ActionKind { Spawn, Attack, Hit, Tick, Death, Parry }
public readonly struct ActionEvent {
    public readonly int Entity, Clip, Target;    // Target -1 for none
    public readonly ActionKind Kind;
    public readonly double Impact;               // on the beat grid
    public readonly float Strength;              // 0..1, accents near 1
}
```

Effects and sounds are also events. The camera's framing is a track sampled
60 times a second. Under a structure-breaks schedule, a break is one more
event per piece it takes out (see [Structure breaks](#structure-breaks)):

```csharp
public readonly struct EffectEvent {   // AttachTo -1 for world space
    public readonly int Effect, AttachTo; public readonly uint Seed;
    public readonly double Time; public readonly Vector3 Position;
    public readonly float Yaw, Scale;
}
public readonly struct SoundEvent { public readonly int Sound;
    public readonly double Time; public readonly float Gain; }
public readonly struct FramingSample { public readonly Vector2 Focus;
    public readonly float ViewWidth; }
public readonly struct BreakEvent {    // the dump's "breaks", only when any
    public readonly double Time;       // the bolt's impact, on the grid
    public readonly int Placement;     // index into the placement list
    public readonly string Target;     // "courtyard/south-west"
    public readonly int Projectile;    // the bolt's entity
    public readonly Vector2 Origin, Point; }  // where it flew from and struck
```

A `Tick` is a repeat hit from a lingering effect, such as the laser's sweep
or a ring's afterburn. The client plays it as a shader flash only, with no
clip, knockback or effect event. Under [one-hit kills](#one-hit-kills) the
sweep kills, so only the boss takes ticks.

The **beat grid** is the song map's beats and their halves and quarters.
Accents are the song map's curated accent list; a beat counts as an accent
when an accent lies within 30 ms of it. In a `free` section the grid is that
section's accents instead (see [Free sections](#free-sections)).

Rules every timeline obeys:

- **Stable entity ids.** An id is never reused within a timeline, so the
  client can keep per-entity slots across frames.
- **Every `Attack`, `Hit`, `Tick` and `Death` impact lies on the grid,**
  within 1 ms.
- **Every death has a death clip and a death effect event,** and every hit
  that is not a tick has a hit clip and a hit effect event.
- **Everything is deterministic.** The same song map, level script and seed
  give a byte-identical JSON dump on the same runtime and platform. IL2CPP
  or Mono in Unity, and .NET under `dotnet test` on the Mac (arm64) or on
  Cygnus (x64), may differ in the last bits of floating point, so each is
  checked against itself.

A short excerpt of the JSON dump, for one goblin's first steps and death:

```json
{ "segments": [
    { "entity": 41, "t0": 12.48, "t1": 12.91, "from": [18.2, 4.0],
      "to": [17.6, 3.5], "facing": [3.9, 3.9], "ease": "pulse" } ],
  "actions": [
    { "entity": 41, "kind": "death", "impact": 14.22, "clip": 3,
      "target": -1, "strength": 0.4 } ] }
```

### The choreographer

The choreographer is a deterministic forward simulation at 60 ticks per
second that runs over the whole song at load. Decisions are quantized to the
musical grid, and nothing plans backwards from a kill.

Its inputs:

- **The song map**, the beat-analysis tool's existing JSON format: `beats`
  (song seconds), `meter`, `firstDownbeatIndex`, `sections`, `phrases`,
  `accents` and an `energy` curve. Each section has a label, a bar range and
  a feel: `pulsed`, or `free` where the beats are only approximate.
- **A level script:** a small tracked JSON file giving, per song-map section,
  the target on-screen count as a fraction of the envelope count, the view
  width, the effect load preset (see [Stress timelines](#stress-timelines)),
  the hero's goal area, the monster mix, the hero's combo pattern and the
  spell set.
- **The clip annotations** (see
  [Clip annotations and retiming](#clip-annotations-and-retiming)), so it
  knows every clip's timing.
- **The arena's placement list:** every prop's type, position and rotation,
  with each type's footprint on the ground (see
  [Navigation around structures](#navigation-around-structures)).
- **A seed.**

For example, one section of the level script, with the envelope's two numbers
at its top:

```json
{ "envelope": { "onScreen": 2400, "viewWidthM": 40 },
  "sections": [
    { "section": "theme-4", "onScreen": 0.6, "viewWidthM": 40,
      "load": "demo", "goal": "glowing-tree",
      "mix": { "skeleton": 4, "goblin": 3, "bat": 2, "slime": 1 },
      "combo": ["slashL", "slashR", "spin@accent"],
      "spells": ["ring@downbeat", "bolt@eighth"] } ] }
```

Combo and spell entries are a clip or spell name, optionally followed by
`@` and a grid position: `beat` (the default), `eighth`, `downbeat`,
`phrase` (the downbeat that opens a phrase) or `accent`.

- **Plain combo entries cycle** in order, one per action.
- **A positioned entry preempts the cycle** on its position. When positions
  coincide, `phrase` beats `downbeat`, which beats `accent`, which beats
  `beat`.
- **An `@eighth` entry is a flurry** (overdrive's `slashR@eighth`): it
  strikes on the eighth notes between the beats whenever the clip fits, and a
  plain entry still owns the beat. A flurry never costs the next beat its
  blow: the hero skips a flurry that would leave the beat's combo no room.
- **Spells are independent** of combos and of each other. Each fires on every
  occurrence of its position.

TD12 measures the envelope and writes it into the level script. Until then
it holds a placeholder.

What the simulation does on each grid step:

- **The hero either strides or attacks on each beat.** They attack whenever
  monsters are within reach, and otherwise stride. During an attack the
  hero's motion follows the combo clip's own root motion instead. Walls push
  the hero out like any monster.
  - **A stepping hero** (the roster hero's `stepS`, demo2d's 110 ms, as the
    native game's hero steps) covers each stride in that long from the beat,
    eased out, then stands until the next beat, instead of gliding over the
    whole beat. A combo without root motion holds the hero through its windup
    and steps its advance (the combo's `advanceM`, the least the hero moves
    toward an unreached goal over the clip) from the impact, so the hero
    steps with the blow and still travels while fighting.
- **The hero heads for the section's goal.** Each section names a goal area
  from the layout parameters. Until the hero reaches it, each stride is one
  gait step along a flow field toward the goal, built once per section at
  load (see [Navigation around structures](#navigation-around-structures)).
  Once there, each stride heads for the densest arc of monsters within
  reach, staying inside the goal area, which keeps the horde wrapped around
  the hero.
- **Combo impacts land on the grid** given by the section's combo pattern:
  slashes on beats, spins on accents, slams on phrase-opening downbeats.
- **An action that cannot fit moves later.** If the next eligible grid
  position would need a retiming speed above 1.33 (for the hero's combos, the
  level script's `comboMaxSpeed`; see [Intensity overlays](#intensity-overlays)),
  the action moves to the following eligible position. At fast tempos the hero therefore acts on
  every second beat or less often, and every windup fits.
- **Spells fire from the section's spell set** at their grid positions.
  - **Projectiles** launch so they arrive on the grid: launch time is the
    impact minus the flight time. The launch itself is off the grid. Bolts
    only target monsters with a clear line from the hero.
  - **Beams such as the laser** tick everything along their sweep on each
    eighth note while they last. Rings, lightning and beams ignore walls.
- **Monsters take one gait step per beat** toward the hero, following a flow
  field around walls and structures (see
  [Navigation around structures](#navigation-around-structures)). A spatial
  hash keeps them apart, so they never overlap by more than a quarter of
  their radius.
- **The clear ring.** A level script's `clearRadiusM` (the song's is 2 m)
  keeps the horde out of a ring round the hero, as in a horde survivor game,
  where the hero's weapons kill whatever closes in. Monsters stop at the
  ring's edge instead of at contact, and step back out, still facing the hero,
  when the hero closes in. They never reach the hero to swing, so no melee
  attacks, parries or attack dust puffs crowd the hero, who stays in plain
  sight. The hero stops short of the edge (by 0.75 of the ring holding
  ground, 0.25 cutting a way toward a goal), hero-centred hit shapes never
  shrink below the edge, and monsters inside the ring die first. Casters
  still fire from their reach beyond the ring. Without it
  (0, the default, as in the stress scripts) monsters close to contact:
- **A monster within reach attacks.** It schedules an impact on the next beat
  its windup fits. The hero parries any blow that would land, with a parry
  effect and no damage; the hero is a perfect player.
  - **Casters** (a roster type with a `projectile`, demo2d's mages) attack
    from their longer reach. The blow releases the projectile on the beat,
    aimed where the hero will stand; it lands on the first eighth note it can
    reach at its speed, and the hero parries it there.
- **At each impact time,** the hit shape (an arc, a ring or a line) is tested
  against monster positions at that instant. The result is `Hit` events with
  short `Linear` knockback segments, or `Death` events once a monster's hit
  points run out.
- **Kill rate follows the section's load preset.** (demo3d's level scripts
  kill differently: see [One-hit kills](#one-hit-kills).) The song holds each
  section's deaths and hits per second with the same per-bar feedback as the
  stress timelines, so its climax kills as fast as the frontier's measured
  points. The climax adds the boss, the laser and denser spells on top.
- **The boss enters outside the view** at its section's start. By default it
  waits there to be released like any other monster, which a full climax
  never does. A level script's `bossFrom` names a layout goal: the boss
  enters in that goal's direction from the hero and always walks in,
  trampling the small fry in its way until it is in view (a full climax packs
  the whole view and would hold it at the edge), and casts only once in view,
  however long its reach. Both
  demos set it: demo3d's boss comes from the `opening`, to the hero's
  north-east. `bossFallsLast` (demo2d's) spares the boss from a clearing
  section's blows until its last, so it falls to the final slam.
- **Spawns refill toward the section's target count.** They come in on beats,
  a few at a time, at a ring 2 m beyond the view's edge, on open cells the
  flow field can reach. While the hero heads for a goal, spawns are weighted
  toward the direction of travel. A monster counts toward the target from
  spawn until its death starts, so a dying monster is replaced at once.

The choreographer also owns the camera's **framing**: where the view is
centred and how wide it is. It runs the follow spring and zoom easing
described in [Camera](camera-and-sound.md#camera) and records them as the framing track. That is
how it knows which monsters are on screen and where the view's edge is.
The view's shape follows the level script's `viewPitchDeg`: demo3d's 55°
trapezoid by default, or, at 90°, demo2d's top-down 16:9 rectangle. The
client adds punch (at most 2% of the view width) and shake (at most 0.25 m)
on top, which the 2 m spawn margin covers. An overlay's `feel` may raise both
(overdrive: up to 7% and 0.5 m; [Blow weight](camera-and-sound.md#blow-weight)).

### Intensity overlays

`--intensity <name>` (demo3d's player, and the dotnet CLI's `song`) plays the
song at another intensity without forking its level scripts. `normal`, the
default, plays the roster's level script as it is. Any other name merges
`Choreography/Data/intensity-NAME.json` over whichever level script the
roster picks before the choreographer runs. An unknown name fails, listing
the known ones. Only the choreography changes, so every `--palette`,
`--monster-style` and `--roster` works with every intensity.

The trailer's `overdrive` (ps-5pd) is the only overlay so far. Its file's
`note` gives its reasons, and `song --intensity overdrive` prints its numbers;
`--checks` adds the song's timeline checks, on any `--level` and intensity.
It sets its counts and views outright, so every roster set plays the same
overdrive (demo3d/evidence/overdrive/tuning/).

An overlay has three parts:

```json
{ "name": "overdrive",
  "multiply": { "envelope.onScreen": 3, "reserve": 1.5 },
  "comboMaxSpeed": 2.5,
  "loads": { "overdrive": { "deathsPerN": 0.06, "hitsPerDeath": 0.25, "spells": [] } },
  "sections": {
    "*": { "multiply": { "viewWidthM": 1.25 } },
    "drop-1": { "load": "overdrive", "spells": ["ring@phrase", "bolt@accent"] } } }
```

- **`multiply`** scales the base script's numbers, by dotted path. The number
  must be in the script, since the defaults live in the code.
- **Every other top-level field** is a global setting, merged over the
  script's. Objects merge member by member; anything else is replaced.
- **`sections`** overrides sections by label in the same way, with its own
  `multiply`. `"*"` applies to every section first. A label the script
  doesn't have fails.

At each level, the multipliers apply first, so a field that is both scaled
and set takes the set value.

**Tuning fields.** A level script (and so an overlay) may set these
top-level knobs. Each defaults to the constant the sim used before it
became a field, so a script without them runs exactly as before. Tests pin
the normal timelines' dump hashes for demo3d and demo2d.

| Field | Default | What it does |
|---|---|---|
| `comboMaxSpeed` | 1.33 | The hero's combo clips speed up at most this much to land on their grid point. Monsters keep 1.33. |
| `comboMinSpeed` | 0.75 | The hero's combo clips play at least this fast. The fit plays a clip as slowly as still lands it, so a slow follow-through can run past the next beat; raising this floor frees the hero for that beat. |
| `travelScaledReach` | false | A hero heading for a goal attacks whatever its scaled hit shapes reach, as it does when holding ground, instead of only what is within its unscaled reach. |
| `yieldToAccents` | false | The hero holds back a beat's blow (or a flurry's) that would leave no room for the windup of the section's next spin or slam on its accent or phrase. Without it, such a spin or slam moves later, and between back-to-back blows it never fits. |
| `maxShapeScale`, `hitsShapeScale` | 4, 2.5 | The kill-rate feedback's caps on the hit shapes' scale: while the kill budget runs behind, and otherwise. |
| `maxShapeViewFraction` | 0.25 | A scaled hit shape is at most this fraction of the view's width. |
| `hitPointScale` | 1 per rank | Multiplies the roster's hit points by rank, for example `{ "fodder": 0 }`. Every monster keeps at least 1. Struck monsters die fewest hit points first, while the load preset's kill budget decides how many die. |
| `refillMinPerBeat`, `refillDivisor` | 24, 6 | A beat's refill spawns at most max(min, the wanted total / divisor). |
| `releaseLeadS` | 2.5 | Releases add this many seconds of the expected deaths, to cover the walk in from the spawn band. |
| `loads` | none | The script's own load presets by name (`deathsPerN`, `hitsPerDeath`, `spells`), alongside `light`, `demo` and `heavy`. |
| `soundGain` | `{ "base": 0.35, "perDoubling": 0.15, "max": 1 }` | The aggregated sounds' gain for a beat's n requests of a category: min(max, (base + perDoubling × log2 n) × (0.7 + 0.3 × strongest)). Overdrive's lower curve leaves a 60-kill beat clearly louder than a 5-kill one, with headroom. |
| `oneHitKills` | false | Every monster but the boss dies to the first blow or spell that strikes it; see [One-hit kills](#one-hit-kills). |
| `heavyBlowKills` | 0 (off) | A hero blow that kills at least this many also asks for the ring's heavy impact, at a strength growing to 1 at twice as many, so a mass kill lands as one layered boom. |

**The client's feel.** An overlay's `feel` object is the client's, not the
sim's: it scales the game's own response to each of the hero's blows by its
kills, and a normal run, with no overlay, has none. Its fields are in
[Camera and Sound, Blow weight](camera-and-sound.md#blow-weight).

**The carnage table.** `TimelineReport` prints a second table after the
section table, with the numbers an intensity is tuned by. For each section it
gives the hero's swings per second, kills per swing (mean and peak), hero
kills, spell kills and their share, other kills (trampling and worst beats),
and the mean on screen against the target.

### Structure breaks

`--structure-breaks <name>` (demo3d's player, and the dotnet CLI's `song`)
lets the fight break the arena's structures (epic ps-suy). It merges
`Choreography/Data/structure-breaks-NAME.json` over the level script after
any intensity overlay, the same way. Without it nothing breaks, and the
timelines are byte-identical to before (the pinned hashes in IntensityTests
and demo2d's tests). An unknown name fails, listing the known ones. It works
with every `--roster`, `--intensity` and `--environment`, but its schedule is
placed on the default script's route.

A schedule may only add `breaks` to sections by label:

```json
{ "name": "walls",
  "sections": {
    "build": { "breaks": ["ruins-gate/1@phrase", "ruins-gate/2@phrase"] },
    "drop-2": { "breaks": ["courtyard/south-west@phrase",
                           "courtyard/east-north@phrase/2"] } } }
```

- **An entry is a target, `@` and a grid position,** as combos and spells
  are. `/k` picks the position's k-th occurrence in the section, the first by
  default. A position the section lacks fails.
- **A target is an area's name,** for every piece the area placed, or the
  area's name, `/` and one part of it. A courtyard's parts are its wall runs,
  named by side and end before its yaw (`south-west`, `south-east`,
  `east-south` ... `west-south`; a closed side is just `north`). A ruin
  wall's runs are numbered from its `from` end (`1`, `2` ...). An area's own
  pieces are `piece-1`, `piece-2` ... in its pieces list's order. The
  placement list records each wall piece's run (`Placement.Segment`).
- **A target fails loudly** if the resolved environment lacks it, if it blocks
  nothing, or if an earlier entry already broke one of its pieces.
- **Anything else a schedule sets fails,** so looks and tuning stay in their
  own flags.

**The break is the hero's spell.** On the decision before the bolt must
leave, the hero sends the roster's bolt (its first spell of shape `single`)
from where it stands to the target's nearest point. The bolt flies at the
projectile's speed and strikes on the break's grid point, like any bolt that
lands on the grid. A spell rather than a blow, because the route rarely puts
the hero within a blow's reach of the wall on that exact beat, and a bolt
reads from any side. The strike is a bolt impact effect and the bolt's sound
at the grid point's accent strength (at least 0.8). The timeline records one
`BreakEvent` per piece. The client shatters the pieces from them (ps-suy.3).

**The changing obstacle map.** A run with breaks navigates its own copy of
the layout's grid (`NavGrid.Clone`). At the strike:

- **The pieces' footprints leave the map at once.** The exact shapes stop
  counting for distances and push-outs. The capped wall distance field and
  every flow field's passable cells are recomputed in the cells those shapes
  could reach, a patch of a few hundred cells.
- **Room for every size still holds.** The widest clearance now found where
  the footprints stood must let the largest monster through with 0.5 m to
  spare each side, or the run fails.
- **Every whole-grid rebuild is time-sliced.** The field toward the hero
  rebuilds on the next beat over the new map, as when the hero moves. The
  uncapped distance field (`Open`) rebuilds a beat's share of its columns and
  rows per tick, keeping its old values, which only grow as footprints leave
  and so stay lower bounds. The goal fields of the current and later sections
  rebuild one after another, a beat's share of the grid per tick, each
  swapped in when done. No tick carries a whole field.
- **Debris never blocks.**

**The walls schedule** (`structure-breaks-walls.json`) breaks each wall next
to the hero as the route reaches it:

- **The build's first downbeat** opens both of the ruins gate's wall runs,
  with the hero in the gap.
- **drop-2's first downbeat** blows out the courtyard's south walls, with the
  hero at its south-east corner and the courtyard full of the horde.
- **drop-2's second phrase** breaks the east walls on either side of the east
  gap, which the new opening sends the hero through.

drop-1 breaks nothing: its hero holds the crystal ring, 24 m from the nearest
wall.

**The break table.** `song --structure-breaks walls` prints
`TimelineReport.BreakTable` after the carnage table. Each row is one break:

- its time, section and number of pieces;
- whether its point was in view, and how far the bolt flew;
- the monsters that stepped over its footprints in the 4 bars after it,
  against the horde alive at the break (on screen and in reserve) and against
  those within 10 m of it.

On the default script every break is in view and 4 to 17 monsters walk
through each opening. Most of the nearby horde dies to the drop's spells
first.

### One-hit kills

demo3d's three level scripts set `oneHitKills` (ps-7n4), as a horde survivor
game plays: the horde walks in and dies to the first thing that touches it.
Under the kill budget, a struck monster beyond the section's allotment took
a non-lethal `Hit` and knockback instead. So the song's opening struck its
lone skeleton for about 16 s before one died, and the groove dealt 14 hits a
second for 1.6 deaths. demo2d and the stress scripts keep the budget.

- **Every monster struck dies,** by a hero blow, a ring, a bolt, lightning
  or the laser's sweep, whatever its hit points. The boss is the exception:
  it keeps taking hits until its section's last bar, as before, and the
  laser's sweep only ticks it, never knocking it back.
- **The load preset steers how far the blows reach,** not who survives
  them. The bar feedback grows the hit shapes while deaths run behind the
  preset's rate and shrinks them otherwise, ignoring `hitsPerDeath`, since
  no hits come.
- **Spells top up.** A spell holds back while the section's deaths are
  within a quarter second's worth of the rate, so a ring's lump of kills
  doesn't overshoot the rate. The hero's blows reap most of every section;
  see [The drops cleave](#the-drops-cleave).
- **Sections kill what walks in.** The hero can't kill slower than the horde
  arrives. Quiet sections often run above their rate, as do the free breaks
  (which fill with the next drop's horde) and the sections after a bigger one
  (which kill its leftovers as they arrive). `SectionDeathRates` therefore
  holds only the sections that hold or grow the horde, from 90% of their rate
  to 1.5 times it.
- **The hero always has a slam ready for the ending.** Under one-hit kills
  the outro empties, so when nothing is in reach before a clearing section,
  the hero winds up for its first accent slam instead of striding past it.
  It commits only at the last decision before that windup, so it keeps
  striding and striking until then rather than standing for seconds.

demo3d's loads are `swarm-light` (0.075 deaths/s per monster on screen, 3x
`light`), `swarm` (0.15, 3x `demo`) and `swarm-build` (0.11). The build
travels to the courtyard and kills about 2.5x what it did. forest's and
heroic's closer view walks their small groove horde in faster, so their
groove has `swarm-groove` (0.15). Against the old budget, the song kills
3.0x as many monsters before the ending (2.6x with it, since the ending's
clearing blow finds fewer leftovers), and no monster but the boss takes a
non-lethal hit. Overdrive inherits it. Its own loads drop `hitsPerDeath`,
its build rises to 0.045 deaths/s per monster, and its groove plays
`swarm-light` on every roster set.

#### The drops cleave

With one-hit kills alone, the normal song's drops let spells take 85–95% of
their kills: the downbeat's hero-centred ring and the lightning emptied the
hero's 160° slash, so the hero swung 0.5–0.9 times a second and mostly at
nothing. The drops now play native's answer (ps-ffx;
[survivors-run.md](../design/survivors-run.md)'s omnidirectional sword spin,
with the oversize slash and shock ring on accents). The action script's drops
cleave; forest's and heroic's keep their slashes, since every-beat cleaves
empty their 150-monster screen faster than it refills.

- **Every swing is a 360° cleave.** `cleaveL` and `cleaveR` play the light
  combo's slashes with a ring for a hit shape, drawn with the spin's circular
  sword slash at the reach they cut.
- **The downbeat is the heavy cleave.** `cleave` plays the light combo's
  finisher (`slash3`) with the ring spell's 4.5 m reach and its boom, in
  place of that spell, so the downbeat's mass kill is the hero's: 40–60
  monsters in the drops, about 100 at the final peak.
- **The cleaves swing on the beat.** They have their own 0.32 s windup, so
  each may cut into the last one's follow-through as a hero with a windup
  does (no clip plays faster than 1.33x), and the downbeat's cleave always
  fits.
- **The hero carves while it travels** (`travelScaledReach`), and a cleave
  may reach 0.35 of the view (`maxShapeViewFraction`).
- **Spells strike away from the hero:** bolts on the beat (the peaks: the
  eighth) and lightning on accents, topping up behind the cleaves.

The drops that travel kill a little slower, as the horde reaches a moving
hero more slowly: groove-2 plays `swarm-travel` (0.14), the build
`swarm-build` (0.11). On seed 1 the hero takes 84–92% of each drop's kills
and swings 1.2–1.5 times a second (the build about 0.9). Two roster fields
make the cleaves: a combo's `clip` names another combo's clip, and its
`effectRadius` is the reach its effect is drawn at full size, so a combo
drawn with another's effect scales it to its own reach.

### Shot fields

A trailer shot ([Trailer Mode](trailer.md), "Shot simulations") is one run of
the choreographer over a short span, built from a level script with these
top-level fields. `Trailer.ShotScript` writes them from the trailer file;
any level script may set them. Each is opt-in, and its default runs the
song exactly as before (the pinned dump hashes prove it, overdrive's
included).

| Field | Default | What it does |
|---|---|---|
| `spanS` | the whole song | `[from, to]` in song seconds: the run simulates only this span. The timeline still runs in song seconds from 0, and its framing before the span holds the span's first sample. |
| `cut`, `final` | none | The bar.beats that `@cut` and `@final` entries fire on. Nothing but the final slam fires on its beat, nothing fires after it, and refill stops there. |
| `heroAt` | the layout's start | The hero's start: a named area or `[x, y]` metres. With a path and no `heroAt`, the path's first waypoint. |
| `heroPath` | none | `{ "via": [areas or metres], "stepM": 1.9, "loop": false }`, replacing goals. The hero strides `stepM` along it on every beat, whether it strikes or not, routed round structures by a flow field per waypoint. Its blows face along the path. Past a path's last waypoint, it runs on along the last leg. |
| `holdGround` | false | The duel's hero: each blow faces the nearest monster, and a beat without one is a short step to and fro across the hero's start, sideways to the nearest monster. |
| `heroStepS` | the roster's `stepS` | The hero's step time: each stride covers its ground in this long from the beat (the trailer's snap steps). |
| `killBudget` | true | False: every blow and spell kills all it strikes (the laser's sweep included), and spells never hold back for the rate. |
| `shapeScale` | the feedback | A fixed scale for the hero's and spells' hit shapes, with the kill-rate feedback off. |
| `yawDeg` | 0 | The camera's heading, clockwise seen from above. `ViewGeometry` turns its trapezoid by it, so spawns stay outside the turned view and the counts match it. The timeline carries it as `ViewYawDeg` (not in the dump). |
| `viewWidthToM` | none | The zoom eases from the section's width at the cut (or the span's start) to this at the span's end. |
| `boss` | none | The boss rule: `ahead`, `behind`, `left`, `right` (relative to the hero's travel at the cut) or a named area. The roster's boss enters there outside the view on the cut and walks in, trampling. It keeps beyond the shot's largest scaled hit shape plus 1 m, backing off when the hero closes in. No blow or spell touches it until the final slam, which kills it. Needs `final`. |
| `duel` | none | A fixed cast by type and count, with no refill and no opening horde. Its k-th member meets the k-th fixed combo entry: it starts a third of a turn from the last, as far out as it walks by that blow, so it arrives just inside its reach then. |

The combo and spell grid gains the trailer's positions: `cut`, `final`,
`anchor` (the next unused anchor of a free section, from the cut on, or the
span's start without one; the combo and the spells each count afresh) and an
explicit bar.beat such as `82.3`. Each fires on one grid point, resolved
before the run, and outranks the song's positions: final, then cut, then a
bar.beat, then anchor, phrase, downbeat, accent and beat. No other blow
costs a fixed entry its fit: the hero holds back one that would.

A hero with a path or holding ground acts on every beat: it swings whatever
is in reach (without waiting for a monster), and it strides on a beat whose
next blow would start after the next decision.

**The kiting refill** (a placeholder until the formations bead): a kiting
hero sweeps its view over whatever waits ahead, so the off-screen buffer
sets the count on screen. Each beat, the buffer integrates the on-screen
shortfall and leaks back toward 1.3 times the target. Refill spawns only on
the edges the hero runs toward, in a 6 m band, with no per-beat cap, and an
off-screen monster behind the hero past the spawn band leaves, since it never
catches up.

### Free sections

A section the song map marks `free` has no steady beat, only approximate
ones. This remains supported, but TD0 marks a section of *Action* as free
only if the supplied recording calls for it; the ending is not assumed to
be free. In a free section:

- **The grid is the section's accents.** Hero actions and spells land on
  accents only. A free ending gets hero slams on its curated accents.
- **Monsters move smoothly.** They advance in `Linear` segments instead of
  `Pulse` gait steps, so they don't step to beats that aren't there.
- **The beat tests skip it.** The test that every gait step starts on a beat
  exempts free sections; the test that every impact lies on the grid uses the
  accents there.

### Navigation around structures

The arena is mostly open, but some sections have walls and structures: ruins,
and a walled courtyard with gaps. Monsters must flow around them, never
through them, and thousands of monsters can't each search for a path. So the
choreographer navigates the way horde and RTS games do, at a cost that grows
only linearly with the number of monsters.

- **One placement list.** The choreography layer computes the arena's prop
  placement from tracked arena parameters: a seed, the prop types and the
  structures. Unity's scene generator instantiates props from the same list,
  so the scene and the obstacle map can never disagree.
- **Footprints** are tracked annotations, keyed by each prop's vendor GUID
  like the clip annotations: a circle or polygon on the ground. Small props
  such as grass and debris have none and don't block.
- **The obstacle map.** Footprints are rasterized onto a 0.5 m grid over the
  arena, about 320 × 320 cells for 160 × 160 m.
- **A wall distance field.** Precomputed once per layout (and patched when a
  structure breaks; see [Structure breaks](#structure-breaks)): each cell's
  distance to the nearest blocked cell. After each gait step, a monster closer to a
  wall than its radius is pushed out along the field's gradient, at the cost
  of one lookup per monster.
- **A flow field toward the hero.** A Dijkstra search with 8 neighbours runs
  from the hero's cell, over cells whose wall distance exceeds a typical
  monster radius. Each cell gets its cost to reach the hero. A monster steps
  toward its cell's lowest-cost neighbour, interpolated for smooth headings.
- **Rebuilds are rare and spread out.** The field rebuilds when the hero
  enters a new cell, at most once per beat. Each rebuild is time-sliced
  across that beat's ticks, so no single tick carries it. Its cost depends on
  the grid, not on the number of monsters.
- **The hero's own fields.** Each section's goal gets one more flow field,
  from the goal area outward, built once at load. The hero follows it the
  same way a monster follows the field toward the hero.
- **Marching in place.** In a jam, a gait step that would overlap a neighbour
  more than allowed is shortened. A monster whose step shrinks below 20% of a
  stride marks time on the beat instead, stepping in place. A packed doorway
  then reads as a crowd waiting its turn, in rhythm, instead of jittering.
- **Room for every size.** Every gap in a structure is at least as wide as
  the largest monster's diameter plus 1 m, so elites and the boss follow the
  same field.

Each tick's navigation work, in pseudocode:

```
per beat:  if hero entered a new cell: start a flow-field rebuild,
           spread over this beat's ticks
per tick:  advance the rebuild by one slice
           for each monster starting a gait step:
               dir = flowField.direction(cell)       // one lookup
               step = separate(dir, spatialHash)     // a few neighbours
               if step < 0.2 × stride: mark time in place
               pos = pushOutOfWalls(pos + step, distanceField)
```

### Stress timelines

The benchmark needs timelines that hold a chosen number of monsters on screen
at a chosen effect load for a long time. The same choreographer produces them
from a **stress script** instead of a level script, on a synthetic 4/4 grid
at 140 bpm instead of a song map. Each stress scene (see
[Stress scenes](benchmarks.md#stress-scenes)) is one stress script plus which systems the
client draws.

A stress script fixes:

- **N**, the on-screen count, alive and dying. Refills hold each bar's average
  within 5% of N. N never exceeds the view's packing capacity (see
  [Performance Budget](performance.md#performance-budget)).
- an **off-screen reserve** of 0.5 × N more monsters, walking in from beyond
  the view, as a real game keeps. The client culls them, so they cost almost
  nothing to draw, but the sim moves them every tick. A level script's
  `strandedS` (demo2d's 16 s) drops a monster left off screen and unreleased
  that long, so a horde the hero walked away from (in demo2d, beyond the
  stream it crosses only by its bridge) stops holding the reserve;
- the **view width**, held fixed;
- the **hero's ground,** with no goal: the hero holds the bench scene's
  centre (in S6, the courtyard) and fights in place;
- the **effect load**, a preset that scales with N, which the level script's
  sections also name:

```
load    deaths/s   hits/s       spells
light   N / 40     2 × deaths   a ring each bar
demo    N / 20     2 × deaths   ring each bar, bolt each beat, lightning ~1/s
heavy   N / 10     3 × deaths   twice demo's spell rates
```

- **Kill rates are held by feedback.** Each bar, the choreographer adjusts new
  monsters' hit points and the hero's hit shapes to keep measured deaths per
  second within 10% of the target.
- **Worst beats** (stress scene S5 only) are a stress pattern, not
  choreography. On one downbeat every 4 bars, every on-screen monster starts
  an attack at once, swinging at the air if out of reach, and a hero ring
  kills 30% of them.

### Clip annotations and retiming

An attack only feels right if its blow lands exactly on the beat. So every
attack, hit, death and combo clip has an annotated **impact time**: the
moment of the blow, in seconds from the clip's start.

- **The clip annotator proposes them.** This editor tool (TD2) samples each
  clip on its rig and picks the frame where the weapon hand, or the head for
  biters, moves fastest. Contact sheets of the proposals go to the task's
  evidence for review.
- **Annotations are tracked** as JSON keyed by the vendor asset GUID and clip
  name. They are our measurements, not vendor content.
- **Locomotion clips also get their stride length,** so the walk cycle's phase
  follows distance travelled.
- **Combo clips also get their root-motion distance,** so the hero's segments
  match the clip's own travel.

```json
{ "guid": "…", "clip": "Attack 1", "impact": 0.42, "end": 0.95,
  "rootMotionM": 0.0, "use": "skeleton attack" }
```

Retiming works backwards from the impact:

```
speed = clamp(annotatedImpact / timeAvailable, 0.75, 1.33)
start = impact - annotatedImpact / speed
```

- **`timeAvailable`** runs from when the entity's previous action reaches its
  annotated `end` (at its own speed) to the new impact.
- **A hero with a windup** (the roster hero's `windupS`, demo2d's 190 ms, the
  native game's sword swing; a combo's own `windupS` overrides it, and lets
  that combo cut into the last one's follow-through too) plays each
  combo from that long before its impact instead:
  `speed = max(1, annotatedImpact / windup)`, so the swing is quick and its
  blow lands on the beat. The next combo's windup may cut into the last one's
  follow-through, so the hero can swing on every beat.
- **The choreographer only schedules impacts that fit** within the speed
  limits, because it knows every clip's annotation.
- **Two animation slots** play per monster: the current clip and the one it
  is leaving. Locomotion counts as a clip. A change crossfades over 0.12 s.
  If a third change starts mid-crossfade, the oldest clip is dropped.
- **Both demos' clients share one schedule,** the package's `ClipSchedule`:
  each clip action's retimed start, end and speed, the clip playing at a song
  time, the walked distance, the hit-stop, the flashes and the death's fade.
  Each client keeps only its presentation (demo3d's slots, demo2d's frames).
  Where the clients' copies once differed, it chose:
  - **A killing blow shows the death, not the flinch.** The sim gives a hit
    and a death at one impact only when two blows land on one tick; the hit
    plays no clip.
  - **Clips that start together play in action order** (a stable sort).
  - **The hero's heavy blows slow to 0.6×** only when the client asks for it
    (demo3d does). demo2d's 15 fps frames can't show a slowdown, so it holds
    the impact frame through the hit-stop and catches up over 0.15 s.

## Time and Synchronization

Every visual is placed by song time, and song time comes from the audio
clock. Nothing in the client uses its own notion of game time.

- **The music** starts with `AudioSource.PlayScheduled` half a second after
  load, at a recorded DSP start time.
- **Song time** comes from a least-squares fit of `AudioSettings.dspTime`
  against `Time.realtimeSinceStartupAsDouble` over the last 2 s. The DSP clock
  advances in buffer-sized steps, and the fit smooths them.
- **Each frame is drawn at its predicted present:** the song time at which it
  will appear on screen, estimated as now plus one frame plus the display's
  reported latency.
- **Local hit-stop replaces global hit-stop.** The music never stops, so time
  never freezes. A struck monster holds its pose for 50 ms with a flash. On
  heavy blows (strength 0.8 or more), the hero's clip time slows to 0.6× for
  50 ms and then catches up, so the next impact still lands on its beat.
- **Fixed-time mode** advances song time by exactly 1/60 s per frame, whatever
  the real frame time, with sound muted. A/B captures, determinism checks and
  the video capture use it.
- **The video's sound is mixed offline.** The capture renders 1080p frames in
  fixed-time mode and streams them straight into ffmpeg's encoder, never as
  image files: 3:19 of 1080p60 PNGs would take tens of gigabytes of
  Cygnus's disk. A mixing step then renders the recording plus every
  `SoundEvent` in the timeline at its song time, and muxes the result with
  the video (ffmpeg). The events are already aggregated (see
  [Sound](camera-and-sound.md#sound)), so the mix matches what the live demo plays.
