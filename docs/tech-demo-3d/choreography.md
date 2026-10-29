# 3D Tech Demo: Choreography

## The Choreography Layer

The choreography layer stands in for a game's simulation. It is plain C#
(netstandard2.1, `System.Numerics` for vectors, no Unity types), compiled by
Unity through an assembly definition with `noEngineReferences: true`, and also
built and tested by `dotnet test` outside Unity. Since the 2D demo's PX0 it is
a local Unity package both demos share,
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
60 times a second:

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
```

A `Tick` is a repeat hit from a lingering effect, such as the laser's sweep
or a ring's afterburn. The client plays it as a shader flash only, with no
clip, knockback or effect event.

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
- **Spells are independent** of combos and of each other. Each fires on every
  occurrence of its position.

TD12 measures the envelope and writes it into the level script. Until then
it holds a placeholder.

What the simulation does on each grid step:

- **The hero either strides or attacks on each beat.** They attack whenever
  monsters are within reach, and otherwise stride. During an attack the
  hero's motion follows the combo clip's own root motion instead. Walls push
  the hero out like any monster.
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
  position would need a retiming speed above 1.33, the action moves to the
  following eligible position. At fast tempos the hero therefore acts on
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
- **A monster within reach attacks.** It schedules an impact on the next beat
  its windup fits. The hero parries any blow that would land, with a parry
  effect and no damage; the hero is a perfect player.
- **At each impact time,** the hit shape (an arc, a ring or a line) is tested
  against monster positions at that instant. The result is `Hit` events with
  short `Linear` knockback segments, or `Death` events once a monster's hit
  points run out.
- **Kill rate follows the section's load preset.** The song holds each
  section's deaths and hits per second with the same per-bar feedback as the
  stress timelines, so its climax kills as fast as the frontier's measured
  points. The climax adds the boss, the laser and denser spells on top.
- **Spawns refill toward the section's target count.** They come in on beats,
  a few at a time, at a ring 2 m beyond the view's edge, on open cells the
  flow field can reach. While the hero heads for a goal, spawns are weighted
  toward the direction of travel. A monster counts toward the target from
  spawn until its death starts, so a dying monster is replaced at once.

The choreographer also owns the camera's **framing**: where the view is
centred and how wide it is. It runs the follow spring and zoom easing
described in [Camera](camera-and-sound.md#camera) and records them as the framing track. That is
how it knows which monsters are on screen and where the view's edge is. The
client adds punch (at most 2% of the view width) and shake (at most 0.25 m)
on top, which the 2 m spawn margin covers.

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
- **A wall distance field.** Precomputed once per layout: each cell's distance
  to the nearest blocked cell. After each gait step, a monster closer to a
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
  nothing to draw, but the sim moves them every tick;
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
- **The choreographer only schedules impacts that fit** within the speed
  limits, because it knows every clip's annotation.
- **Two animation slots** play per monster: the current clip and the one it
  is leaving. Locomotion counts as a clip. A change crossfades over 0.12 s.
  If a third change starts mid-crossfade, the oldest clip is dropped.

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
