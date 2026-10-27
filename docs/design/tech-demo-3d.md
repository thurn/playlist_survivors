# 3D Tech Demo: Technical Design and Implementation Plan

## Introduction

Playlist Survivors is a planned rhythm game with fantasy action-RPG elements:
the horde combat of Vampire Survivors with the beat-locked movement of Crypt
of the NecroDancer. This document specifies a **3D tech demo**: a
non-interactive Unity build in which a hero carves through a horde of fully
animated 3D monsters to *Action* by Alessia Labate & Conrad Taylor (NCS
Release), as if a perfect player were controlling the hero to the beat.

The demo answers one question that the game's design depends on:

> On a mid-range gaming laptop, how many animated monsters and effects can be
> on screen at once while every monster still looks and reacts the way a
> single monster does?

The demo scouts a larger decision: whether the game's client should move from
the native Rust app ([native-app.md](native-app.md)) to Unity. So the report
gives what stock Unity does as well as what bespoke systems reach. What else
that decision needs is listed in
[What This Does Not Answer](#what-this-does-not-answer).

It produces three things:

- **A frontier report.** For each camera zoom and effect load, the largest
  number of on-screen monsters that fits the frame budget, what limits it, and
  a recommended envelope for game design. It also shows how rich the
  environment can be, and what that richness costs in monsters.
- **The demo build.** A macOS app that plays the full 3:19 song, with the horde
  peaking at that envelope in the climax.
- **A capture.** A 1080p60 video of the demo for sharing.

The demo is a prototype that establishes capabilities. None of its code is
meant to ship. Wherever a choice does not change the measured answer, the
simplest option wins.

### Decisions

These were settled with the user on 2026-10-26 in a design interview, and
revised the same day in a review interview. The target song was changed to
*Action* at the user's request on 2026-10-27. Executors treat these decisions
as fixed.
Every other choice is the executor's, recorded with its reasons in the bead's
notes.

1. **A standalone showcase.** The demo does not render the game's Rust sim.
   It has its own choreography and content.
2. **Unity is only a visual client.** A plain C# choreography layer decides
   everything that happens, and Unity reflects it. Nothing uses Unity physics,
   animation events or Animator state machines to decide what happens.
3. **The target is an RTX 4060 Laptop-class Windows laptop** at 1920×1080,
   60 fps, on default settings.
   - It is the most common laptop GPU on Steam in September 2026: 3.5% of all
     GPUs, ahead of the RTX 5060 Laptop (2.5%), RTX 3060 Laptop (1.7%) and
     RTX 4050 Laptop (1.6%).
   - The equivalent Mac floor is an M3 Pro or M4 Pro class machine.
   - Base M-series MacBook Airs, at roughly a third of a 4060 Laptop's GPU,
     are not targeted.
   - No Windows player is built or run.
4. **Validation happens on this M5 Max only.** Budgets are translated from
   published benchmarks; no target device is bought. Every target-hardware
   number in the report is labeled as inferred and shows its factors.
5. **The client gets 75% of the target frame:** 12.5 ms of 16.7 ms. The rest
   is reserved for the real game's sim layer, HUD, audio and input. The
   headline count is measured against this budget. It is the count of the
   **envelope**, the recommended view width and on-screen count (defined in
   [The report](#the-report)), and the demo's climax runs at exactly that
   envelope.
6. **Engine: Unity 6.6 (6000.6.7f1) with URP,** plus bespoke GPU-driven systems
   for the crowd and the particles.
7. **The project lives in this repository.** Paid vendor assets, and anything
   derived from them, are never committed, because the repository is public
   (see [Vendor Assets and Licensing](#vendor-assets-and-licensing)).
8. **The game camera.** A fixed high three-quarter perspective view follows
   the hero, with beat-synced punch, shake and slow zooms. "How many" means on
   screen at once. View width is a measured axis: 30, 40 and 55 m.
9. **The fidelity floor.** Every visible monster keeps full skeletal
   animation, a real cast shadow, its own hit and death reactions and effects,
   and dynamic light from nearby effects, at any horde size (see
   [The Fidelity Floor](#the-fidelity-floor)).
10. **Crowd animation runs on our own bone-texture GPU path,** measured
    against stock Unity as a baseline. DOTS with Latios Kinemation was
    dropped: it is unconfirmed on Unity 6.6 and not clearly faster.
11. **Effects run on GPU particles from the start.** An editor converter
    carries the chosen Hovl Studio and KriptoFX prefabs into the GPU runtime.
    TD8 opens with a spike that picks it: our own compute-shader runtime, or
    VFX Graph driven by a few template graphs (see
    [Choosing the GPU runtime](#choosing-the-gpu-runtime)).
12. **A night forest glade built from classic Synty POLYGON packs,** with open
    clearings for the fighting and rich decoration around them, baked
    moonlight, emissive glow and effect lights that dominate (see
    [The Environment](#the-environment)).
    - **It must look good.** It's art-directed, and checked from fixed beauty
      shots.
    - **Its rendering is optimized as deliberately as the crowd's,** with its
      own stock baseline, ladder of techniques and density sweep. The report
      gives the trade between scene richness and monster count.
13. **The song is *Action* by Alessia Labate & Conrad Taylor (NCS Release),**
    using the user-supplied MP3, 198.968 s (about 3:19, measured with
    `ffprobe`). The source on this machine is
    `/Users/dthurn/Downloads/Alessia Labate, Conrad Taylor - Action [NCS Release].mp3`.
    The full recording is the timing reference; TD0 maps its sections and
    accents before setting the horde's build, releases and final climax.
14. **The hero fights with sword combos and beat spells.**
15. **Native 1080p by default.** MSAA 4× and TAA are compared. Upscaling is
    reported only as extra headroom, never in the headline count.
16. **Benchmark sweeps may open a window** on the screen of the **operator**,
    the developer who runs the demo and reviews its evidence. Routine checks
    stay windowless.
17. **A new epic in the project's issue tracker, run in parallel with the
    survivors run** (`hv-n7fn`), with plan keys `TD0` to `TD15` (see
    [Implementation Plan](#implementation-plan)).

## Related Information

- [AGENTS.md](../../AGENTS.md): the repo's working rules, beads, reviews,
  evidence, and the vendor-asset rule this demo adds. It describes how every
  change is delivered:
  - **Tollgate** is the repo's local delivery tool. Each change is submitted
    from its own git worktree as a candidate.
  - **The gate stage** (`scripts/ci-gate`, about 60 s) must pass before
    Tollgate promotes a candidate to the local `staging` branch.
  - **The release stage** (`scripts/ci-full`) later validates `staging` and
    pushes remote master.
  - **`scripts/check-fast`** is the quick check every change runs before it is
    submitted.
- [tech-demo.md](tech-demo.md): the earlier 2D browser tech demo. Its song-map
  workflow builds this demo's song map. The workflow uses the commands of the
  repo's Python beat-analysis tool (`crop`, `analyze`, `report`, `edit`,
  `check` and `click`). Its effect catalogue
  is a reference for juice: the feedback effects (particles, flashes, shake,
  hit-stop, impact sounds) that make actions feel physical.
- [beat-steps.md](beat-steps.md): why the 2D demo replaced choreography
  planned backwards from kills with a forward simulation. This demo's
  choreographer is a forward simulation for the same reason.
- [survivors-run.md](survivors-run.md): the parallel epic. Its Vampire
  Survivors numbers (speeds, kill pacing) are a reference for plausible horde
  behavior.
- [native-app.md](native-app.md): the native Rust app, its windowless-testing
  rule and its evidence conventions.
- Steam Hardware & Software Survey, September 2026:
  [video cards](https://store.steampowered.com/hwsurvey/videocard/) and the
  [overview](https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam)
  (Windows 95%, macOS 1.9%, 1920×1080 on 48% of primary displays).
- [Notebookcheck's M5 Pro and M5 Max GPU analysis](https://www.notebookcheck.net/Apple-M5-Pro-M5-Max-GPU-Analysis-M5-Max-GPU-on-par-with-the-GeForce-RTX-5070-and-faster-than-Strix-Halo.1246060.0.html):
  the M5 Max 40-core GPU scores 3,924 in 3DMark Steel Nomad, against about
  2,025 for an RTX 4060 Laptop.
- [NVIDIA FLIP](https://github.com/NVlabs/flip): the image-difference metric
  behind the fidelity A/B checks.
- Asset sources on this machine, both read-only:
  - `~/synty`: Synty POLYGON and ANIMATION packs as `.unitypackage.gz` files.
  - `~/unity_assets/UnityAssets`: a Unity 6.6 project with Hovl Studio,
    KriptoFX, Magic Pig Games (Infinity PBR), sound packs and other packs
    already imported.

## What the Viewer Sees

The demo is a single 3:19 shot of one battle, with no input and no HUD. It
should feel like watching an expert play a finished game.

- **The arena.** A moonlit enchanted forest: open glades ringed by trees,
  glowing elven trees and crystals, ruins, a graveyard and braziers, under
  cool blue moonlight. Effect light carries the fighting.
- **The hero.** A Synty knight with sword and shield. They stride on beats from
  clearing to clearing and carve through the horde with sword combos whose
  blows land on beats, spinning on accents and slamming on the downbeats that
  open phrases. Spells fire on the musical grid: rings on downbeats, bolts on
  eighth notes, lightning on accents and a laser sweep in the climax.
- **The horde.** Skeletons, goblins, slimes, bats, mushrooms and demons from
  Magic Pig Games' low-poly line, with Synty Fantasy Rivals brutes as elites
  and a boss at the climax. They advance in beat-synced gaits, attack on
  beats, flinch when hit and die visibly.
- **The arc.** The opening has a handful of monsters. Horde density and
  spell intensity follow *Action*'s mapped energy and accents, with room to
  breathe in lower-energy sections. The final high-energy section holds the
  measured envelope on screen, and the ending clears the horde on mapped
  accents.

An illustrative outline follows. These are choreography roles, not confirmed
song sections or timestamps; TD0 sets their boundaries from the supplied MP3.

```
opening              12 skeletons      30 m view, hero strides
early build          ~15% of envelope  first rings on downbeats
later build          ~50% of envelope  zoom eases out
final energy peak    100% of envelope  laser sweeps, boss arrives
ending               the horde dies   slams on mapped accents
```

## System Overview

The demo has four parts, and data flows one way through them.

- **Song map:** the recording's beats, downbeats, sections, accents and
  loudness. It is built once from the supplied MP3 with the beat-analysis
  tool and committed as JSON under `songs/action/`. The audio stays local;
  the old public-domain recording and its song map are not used.
- **Choreography layer:** plain C# with no UnityEngine dependency. At load it
  runs a deterministic forward simulation over the whole song. The result is
  the **timeline**: every entity's motion as segments, the camera's framing,
  and every action, effect and sound as events stamped in song seconds. Its
  entities are the hero, monsters and projectiles.
- **Visual client:** Unity. Each frame it evaluates the timeline at the
  current song time and draws the result: the crowd, the hero, effects,
  lights, camera and sound.
- **Benchmark harness:** drives the same client with synthetic timelines and
  records frame timings and cost counters. Its scenes are the **stress
  scenes**, listed in [Stress scenes](#stress-scenes).

The client's frame loop shows the split of responsibilities:

```
at load:    timeline = Choreographer.Run(songMap, levelScript, seed)
each frame: t = songClock.Now()
            states = Evaluate(timeline, t)        // Burst jobs, per entity
            crowd.Upload(states)
            effects.Spawn(timeline.EffectsDue(t)); lights.Update(t)
            camera.Update(timeline.Framing(t))    // plus punch and shake
            sound.Schedule(timeline.SoundsDue(t))
```

Three properties follow from this structure:

- **The client never decides gameplay.** Every entity's state and the
  camera's framing are functions of the timeline and the song time, so a
  seek shows the right horde at once. Stateful presentation (particles in
  flight, local hit-stop) settles within about a second of a seek.
- **Lookahead is unlimited.** An attack can start winding up before the beat
  its blow lands on.
- **The choreographer's cost is paid at load.** The benchmark measures only
  the client; the 25% reserve stands in for a real sim layer. The
  choreographer times each of its ticks at load, which checks that a sim
  like it fits in that reserve (see [Sim tick timing](#sim-tick-timing)).

## The Fidelity Floor

The demo's premise is that fighting one monster feels the same as fighting a
horde. That becomes a rule: what each visible monster gets never degrades with
the number of monsters.

Every visible monster keeps:

- **Full skeletal animation.** Locomotion is phase-matched to distance
  travelled, so feet never slide. Clips crossfade, turns are smooth, and every
  monster animates every frame, with no reduced update rate for crowds. This
  rules out per-vertex animation textures, which cannot blend clips.
- **A real cast shadow** in the sun's shadow map, not a blob decal.
- **Its own reactions.** Every hit plays a flinch, a flash, knockback and a
  hit effect. Every death plays a death animation and a death effect. A
  **tick**, a repeat hit from a lingering effect, plays only the flash: no
  flinch, knockback or particles. A tick that kills plays the full death
  (see [Effect design rules for a horde](#effect-design-rules-for-a-horde)).
- **Dynamic light** from nearby effects.

A **cut** is any change that makes a monster cheaper to draw. A cut is
allowed only when an A/B capture shows no visible difference at 1080p from
the game camera (see [Fidelity A/B Checks](#fidelity-ab-checks)). Candidate
cuts:

- a decimated mesh for a monster below a screen-height threshold;
- a simplified mesh that draws only its shadow;
- clips baked at 30 Hz instead of the default 60 Hz, and interpolated;
- effect lights clustered on cells larger than the default 1.5 m;
- coincident identical effect spawns merged;
- an effect's large soft glow quad dropped, leaving bloom to draw the halo.

The decimated meshes are **LOD1** and **LOD2**: each monster's mesh reduced at
bake time to about half and about a quarter of its triangles, by a
decimation tool TD11 chooses. LOD0 is the original. Examples of what the rule
permits and forbids:

```
allowed    LOD1 (half the triangles) below 60 px tall, if its A/B passes
allowed    shadows drawn from LOD2, if its A/B passes
forbidden  walking monsters animate at 15 Hz when more than 2,000 are visible
forbidden  blob shadows for monsters more than 20 m from the hero
forbidden  skip hit effects when more than 50 hits land on one beat
```

An **on-screen monster** is a monster instance whose bounds intersect the
camera frustum in that frame, alive or dying. The report counts alive and
dying monsters separately and headlines their sum.

## Performance Budget

The target is 60 fps on an RTX 4060 Laptop, and the client may use 75% of
each frame: 12.5 ms of 16.7 ms. All development happens on an M5 Max, so the
target budget is translated into pass lines for this machine.

The translation factors are approximate:

- **GPU: the M5 Max is about 1.9× an RTX 4060 Laptop** (Steel Nomad 3,924
  against 2,025).
- **CPU: one M5 Max performance core is about 1.7× one performance core** of
  a typical 4060 laptop's processor (an i7-13620H or Ryzen 7 7840HS class
  part). This is an estimate from single-thread benchmark scores, and the
  report states it as an assumption.
- **An uncertainty margin of 1.2×** applies to both, for drivers, thermals and
  Windows.
- **Unity's job workers are capped at 6.** A 4060 laptop typically has 6 to 8
  performance cores, shared with the main thread, render thread and the OS.

```
                  target (RTX 4060 Laptop)   M5 Max pass line
GPU frame         12.5 ms                    5.5 ms   (÷ 1.9 ÷ 1.2)
main thread       12.5 ms                    6.0 ms   (÷ 1.7 ÷ 1.2)
render thread     12.5 ms                    6.0 ms   (÷ 1.7 ÷ 1.2)
job workers       6 to 8 cores               capped at 6
```

The GPU line is the "180 fps on this Mac" rule of thumb: 5.5 ms is about
180 fps.

The **pass rule**: a configuration passes when, over a 20 s steady-state
window after 5 s of warm-up:

- p99 GPU frame time is at most 5.5 ms;
- p99 main-thread and render-thread CPU times are at most 6.0 ms each;
- no single frame exceeds twice its line;
- no particle spawn was rejected, and the light dropped by the effect-light
  pool stayed within its limit (see [Effects](#effects));
- the machine stayed quiet and cool throughout (see
  [Benchmark Harness](#benchmark-harness)).

The demo's full song has no steady state. Its run passes when every 20 s
window, starting every 5 s, meets the same lines, and no frame anywhere
exceeds twice its line.

The environment has a ceiling of its own: the empty arena, with lighting and
post-processing, gets at most 2.0 ms of GPU p99 on the M5 Max, about 4.5 ms of
the target's 12.5 ms (see [Environment frontier](#environment-frontier)).

The reserve is checked too, not just assumed. A real game's sim layer must
fit in the 4.2 ms of target CPU the client leaves free. That gives the sim its
own line on the M5 Max: **2.0 ms per tick** (4.2 ÷ 1.7 ÷ 1.2), measured by
[Sim tick timing](#sim-tick-timing).

### Where the Mac flatters the target

The M5 Max is not simply a faster 4060. Three architectural differences make
some costs nearly free on the Mac but expensive on the target, so frame time
alone understates them. The harness counts these costs directly.

- **Hidden-surface removal.** Apple GPUs are tile-based deferred renderers.
  Each opaque pixel is shaded once, whatever the draw order. NVIDIA GPUs shade
  every fragment that passes the depth test when it is drawn, so opaque
  overdraw costs there.
  - **Depth priming closes most of the gap.** SSAO needs a depth prepass, so
    URP runs one, and forced depth priming makes the forward pass shade each
    opaque pixel once on any GPU.
  - **The prepass itself still overdraws.** Its fragments are cheap, but its
    vertex work for skinned monsters is not. The crowd is drawn roughly front
    to back to help it (see [Drawing the crowd](#drawing-the-crowd)).
  - **The harness reports opaque depth complexity:** the mean number of
    prepass fragments per covered pixel that pass the depth test in the
    client's draw order. A debug render counts it on any GPU.
- **On-tile blending.** Apple GPUs blend transparent fragments in on-chip
  memory, while NVIDIA blends through memory. Particle overdraw costs more on
  the target. The harness reports transparent fragments per pixel.
- **Memory bandwidth.** An RTX 4060 Laptop has 256 GB/s. The M5 Max has more
  than twice that, about 2.1–2.4× by published figures; TD7 records the
  figure it uses. Bandwidth-bound passes therefore translate worse than the
  1.9× compute ratio.
- **Faster job workers.** Six M5 Max workers each run about 1.7× faster than
  the target's cores. The client completes its jobs inside the frame, before
  rendering, so job latency lands in main-thread time and the CPU lines cover
  it.

A **frontier point** is the largest on-screen count that passes the pass rule
on the M5 Max at one combination of the other sweep axes, found by the
[sweep's search](#the-sweep). Every point gets a **compute estimate** of the
target's GPU time: the M5 Max GPU time × 1.9 × 1.2.

The compute ratio flatters bandwidth-heavy points. So two points also get a
**per-pass estimate**: the envelope point (see [The report](#the-report)) and
the heavy-load point at the envelope's view width.

- **The trace.** A Metal System Trace at the found count. Shadow, transparent
  and post-processing passes are scaled by the bandwidth ratio × 1.2, and the
  other passes by 1.9 × 1.2. Their sum is then scaled by the run's
  p99-to-mean GPU time ratio.
- **The step-down.** If the estimate exceeds 12.5 ms, the count steps down by
  5%, and the pass rule and the trace repeat.
- **Other points are not traced.** The report flags those whose overdraw
  counters suggest the Mac flatters them (see [Measurements](#measurements)).

A frontier point can also be limited by space rather than time. Monsters keep
apart (see [The choreographer](#the-choreographer)), so a view holds at most
its **packing capacity**: its visible ground times the roster's densest
packing, about 2.4 monsters per m² for 0.4 m radii. The search's
ceiling is the smaller of 12,000 and that capacity. A point that reaches it
is reported as capacity-bound.

## Engine and Rendering Setup

The project uses Unity 6.6 (6000.6.7f1, already installed) with URP. URP's
Forward+ path is required: it shades many unshadowed point lights per pixel,
and it is what the GPU Resident Drawer needs.

The default settings that every measurement uses unless a sweep axis changes
them:

```
renderingPath:  Forward+          hdr: true      renderScale: 1.0
depthPriming:   forced            (SSAO's depth prepass primes the forward pass)
antiAliasing:   MSAA 4x  or  TAA  (TD9 picks; MSAA 4x until then)
sunShadows:     soft, 2 cascades, 2048 px, fitted to the view
effectLights:   point, unshadowed, from a pool (see Effect lights)
post:           bloom, neutral tonemapping, color grading, SSAO, vignette
off:            motion blur, depth of field, film grain, upscaling
```

- **Static environment** rendering climbs its own measured ladder, starting
  with the GPU Resident Drawer (see
  [Environment rendering](#environment-rendering)).
- **Graphics API:** Metal. No Windows player is built.
- **Benchmark runs** turn vsync off and leave the frame rate uncapped, so frame
  time measures throughput. The showcase build caps at the display rate.
- **The macOS player** is the demo and the benchmark.

## The Choreography Layer

The choreography layer stands in for a game's simulation. It is plain C#
(netstandard2.1, `System.Numerics` for vectors, no Unity types), compiled by
Unity through an assembly definition with `noEngineReferences: true`, and also
built and tested by `dotnet test` outside Unity.

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
  give a byte-identical JSON dump on the same runtime. Mono in Unity and .NET
  under `dotnet test` may differ in the last bits of floating point, so each
  runtime is checked against itself.

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
described in [Camera](#camera) and records them as the framing track. That is
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
[Stress scenes](#stress-scenes)) is one stress script plus which systems the
client draws.

A stress script fixes:

- **N**, the on-screen count, alive and dying. Refills hold each bar's average
  within 5% of N. N never exceeds the view's packing capacity (see
  [Performance Budget](#performance-budget)).
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
  biters, moves fastest. Contact sheets of the proposals go to the bead's
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
- **The video's sound is mixed offline.** The capture writes 1080p frames in
  fixed-time mode. A mixing step then renders the recording plus every
  `SoundEvent` in the timeline at its song time, and muxes the result with
  the frames (ffmpeg). The events are already aggregated (see
  [Sound](#sound)), so the mix matches what the live demo plays.

## The Crowd

The crowd is the core of the measurement. Thousands of skinned monsters must
meet the fidelity floor, and stock Unity, an Animator plus a
SkinnedMeshRenderer per monster, tops out in the hundreds, mostly on CPU.
Our own GPU-driven bone-texture path draws the crowd, and stock Unity is
measured as the baseline.

### Roster

The horde is drawn from palette-textured low-poly packs that share one look:

- **Fodder:** six Magic Pig Games low-poly monsters (Skeleton Warrior,
  Goblin, Slime, Bat, Mushroom Monster, Demon). Each ships its own rig and 12
  to 20 clips, covering walk, attacks, hit and death.
- **Elites:** two Synty POLYGON Fantasy Rivals characters (Big Ork, Troll).
  They use Synty's humanoid rig, animated with Synty's locomotion and combat
  animation packs.
- **Boss:** one large creature (the Magic Pig Dragon or the Fantasy Rivals Evil
  God), chosen by TD2 from a style still: a render of the candidates side by
  side under the demo's lighting.

That is nine mesh types and eight rigs. TD2 may swap types for style or clip
coverage.

### Common requirements

The bone-texture path must meet these requirements. The stock baseline meets
the same ones wherever stock Unity can, so the comparison is of
implementations, not features.

- **Per-entity state comes from the timeline.** A Burst job evaluates each
  monster's segment, easing, action state and clip times each frame.
- **Animation.** Two clips per monster crossfade, with frame interpolation.
  The walk phase follows distance, and turns are smooth.
- **Shadows.** Every monster casts into the sun's cascades, including
  monsters just off screen whose shadows reach into view.
- **Draw order.** Roughly front to back, where the path allows it, for the
  target's early depth rejection.
- **Lighting.** Forward+ shading with the sun, ambient and every effect light.
- **Reactions.** Hit flash, knockback, death clip, then a 1-beat hold and a
  1-beat dissolve with an emissive edge.
- **Variation.** Per-instance tint from the type's own color sets, plus small
  scale jitter.
- **Weapons.** Weapons ride their hand bone rigidly.
- **Motion vectors.** Every pass that TAA needs, including skinned motion
  vectors from the previous frame's pose.

### Bone-texture path

Our own path keeps animation entirely on the GPU and needs no DOTS.

- **Baking.** The bone-texture baker, an editor tool, samples each clip on its
  rig at 60 Hz (30 Hz is a cut). It writes each bone's skinning matrix (the
  bone's world matrix times its bind pose) as three float4 rows into one
  floating-point texture per rig. A small table gives each clip's first row,
  frame count, loop flag and annotations.
- **Size.** About 60 bones × 3 texels × 16 bytes is 2.9 KB per frame. Thirty
  clips of 2 s at 60 Hz come to about 10 MB per rig, or 80 MB for the roster.
- **Instance data.** 64 bytes per monster (the struct below): position and
  yaw, both animation slots' clips and times, the blend weight, scale, hit
  flash, dissolve, tint and flags. Burst jobs write it straight into a
  `GraphicsBuffer` through `LockBufferForWrite`. Slots are stable per entity,
  and the buffer is double-buffered, so the previous frame's copy supplies
  motion vectors.
- **Two skinning variants, both measured:**
  - **Per-vertex sampling:** the vertex shader samples 4 bones × 2 clips × 2
    frames, which is 48 texel loads per vertex, in every pass.
  - **Palette pass:** a compute pass blends each visible monster's bones once
    into a palette buffer (about 14 MB per frame for 5,000 monsters). Every
    pass then reads just 4 matrices per vertex. The previous frame's palette
    is kept for motion vectors.
- **Drawing** uses `Graphics.RenderMeshIndirect` with a custom URP shader that
  has forward, depth, shadow-caster and motion-vector passes (see
  [Drawing the crowd](#drawing-the-crowd)).

```hlsl
struct MonsterInstance {
    float3 position; float yaw;
    uint clipA; float timeA; uint clipB; float timeB;
    float blend; float scale; float hitFlash; float dissolve;
    uint tint; uint flags; float2 pad;
};
```

### Stock baseline

The baseline shows what Unity does out of the box, so the report can say how
far the bespoke paths move the frontier.

- **One GameObject per monster,** with an Animator driven by `CrossFade` calls
  from timeline events.
- **A SkinnedMeshRenderer per monster,** with Unity's compute skinning and
  shadows on.
- **Everything else is shared:** the same scene, lighting and timeline as the
  bone-texture path.

### Against the baseline

TD5 compares the bone-texture path with stock Unity early, before effects and
polish depend on the crowd.

- **Scenes.** The crowd-only and crowd-with-combat stress scenes (no
  effects), at 2,000 on-screen monsters and a 40 m view. Each arm then also
  gets a search for its own maximum.
- **Arms.** Both bone-texture skinning variants and the stock baseline, all
  under the same pass rule.
- **Recorded per arm:** max on-screen count, the frame breakdown at 2,000,
  and implementation notes (lines of code, workarounds, any fidelity-floor
  item it struggled with).
- **The variant.** The skinning variant with the larger S2 maximum at 40 m
  survives. The other's code is deleted; its measurements stay in the
  evidence.
- **The baseline stays** as a harness option, so the final report can
  compare against it.

### Drawing the crowd

The bone-texture path draws the crowd in a fixed way that keeps CPU cost
flat and helps the target's GPU.

- **Culling runs in a compute pass.** Each instance's bounding sphere is
  tested against the camera frustum extended toward the sun by the longest
  shadow a monster can cast (height ÷ tan(sun elevation)). Monsters just off
  screen therefore still cast their shadows into view.
- **Depth bins give rough front-to-back order.** The same pass sorts visible
  instances into 8 bins by camera distance and writes one indirect draw
  command per bin. Commands run near to far within each call, which cuts the
  depth prepass's overdraw. Order across types is not sorted; depth priming
  keeps the forward pass's cost independent of it.
- **LOD is chosen by projected height,** with hysteresis and a 0.2 s dithered
  crossfade. Each LOD threshold is a cut gated by an A/B check.
- **Shadows can use a proxy.** The main draw does not cast shadows; a
  separate shadow-only draw uses the shadow-proxy LOD, so the proxy can
  differ from what the camera sees.
- **Draw count stays flat.** The CPU issues one `RenderMeshIndirect` call per
  type and LOD for the main draw, and one per type for its shadow proxy:
  36 calls a frame for nine types and three LODs. URP runs each call in
  every pass its shader has (depth, motion vectors, forward, each shadow
  cascade). Each call holds its 8 depth-bin commands. That makes a few
  hundred GPU draws in all, whatever the number of monsters.

## The Hero

The hero is one character, so stock Unity animation is fine for them, but
their clip timing still comes from the timeline.

- **Model.** A Synty knight with sword and shield, from POLYGON Knights or
  Fantasy Characters, animated with Synty's Sword Combat, Base Locomotion and
  Idles packs.
- **Animation.** A `PlayableGraph` driven by song time plays the hero's clips,
  with no Animator state machine. Each combo clip starts as its retiming
  dictates, and locomotion phase follows distance.
- **Combos.** Slashes left and right, a thrust, a spin on accents and an
  overhead slam on phrase-opening downbeats. Each impact spawns its slash
  effect aligned to the swing.
- **Spells.** Fired from the section's spell set: rings, bolts, lightning, and
  a laser in the climax. They grow larger and denser as the song builds.
- **Parries.** Blows that would land are parried with a shield flash and
  sparks. The hero never takes damage.

## Effects

Effects are half of the main question. A stock Hovl hit effect is a prefab of
3 to 8 Shuriken particle systems. At the stress peak, 1,000 or more live
systems would be needed, and Unity's per-system cost lands on the main thread
long before particle counts matter. So effects run on GPU particles from the
start. An editor converter carries the chosen vendor effects into the GPU
runtime, which a spike picks first (see
[Choosing the GPU runtime](#choosing-the-gpu-runtime)).

### How many effects

The player causes nearly every effect, but most effect instances land on
enemies. Survivors-genre enemies rarely emit effects of their own. Here
monsters fight only in melee, so their share is a small dust puff per attack,
plus the hero's parry sparks. Hits and deaths, though, scale with the horde
and its kill rate.

- **Player weapons.** A late-game Vampire Survivors build spawns about 50–60
  weapon effects a second and keeps 60–80 alive, plus about 10 that persist.
  That's six evolved weapons with +3 Amount, from the repo's VS data in
  `docs/survivors/`. The hero's spells here are fewer and larger, and each
  combo blow adds a slash, so tens are alive at once.
- **Enemy feedback** at an envelope of 2,600 on screen and demo load (130
  deaths and 260 hits a second):

```
              rate      lifetime   alive at once
hit sparks    260/s     ~0.35 s    ~90
death bursts  130/s     ~0.8 s     ~105
all effects                        ~250-300   (heavy load: ~800-1,000)
```

**Beats make effects bursty.** Every impact lands on the beat, so one beat's
hits and deaths spawn in a single frame. At 140 bpm a beat is 0.43 s:

- at demo load, about 110 sparks and 55 death bursts spawn in one frame;
- at heavy load, about 450 effects spawn in one frame;
- each effect is at its largest and brightest at birth, so the beat frame is
  also the overdraw peak.

**Particles are cheap; overdraw is not.**

- **Particle counts.** About 20 particles per spark, 60 per death and a few
  hundred per spell give 15,000–30,000 live particles at demo load and about
  100,000 at heavy. Simulating 100,000 on a 4060-class GPU takes well under
  0.5 ms.
- **The cost of a layer.** One full-screen layer of HDR additive blending at
  1080p moves about 33 MB: 2 million pixels, each read and written at 8
  bytes. That is about 0.13 ms of an RTX 4060 Laptop's 256 GB/s, or 0.2–0.3
  ms with texture reads.
- **The overdraw budget.** If effects get about 3 ms of the client's 12.5 ms,
  that allows a mean of about 5 full-screen layers, and 10–15 on beat frames.
- **Glow quads eat it.** A Hovl hit effect often carries a soft glow quad a
  few metres across. A hundred at once make one or two full-screen layers on
  their own.

These are estimates, which TD8 measures. The harness flags any point whose
mean transparent fragments per pixel exceed 5, or whose beat-frame peak
exceeds 12 (see [Measurements](#measurements)). The Mac blends on tile and
hides most of this cost, so these counters matter more than its times here.

### Effect design rules for a horde

Three rules keep effects affordable without breaking the fidelity floor.

- **Bloom makes the glow.** Post-process bloom costs the same for one effect
  or a thousand, while a glow quad costs per effect. Converted effects keep
  small, HDR-bright cores and drop their large soft glow quads, so bloom draws
  the halo. Each dropped glow layer is a cut, gated by an A/B check.
- **Feedback is tiered.** A **tick**, a repeat hit from a lingering effect
  such as the laser's sweep or a ring's afterburn, gets the monster's shader
  flash and nothing else: no particles, flinch or knockback, so a swept crowd
  doesn't jitter in place. Weapon impacts get sparks, deaths get bursts, and
  accents and spells get the large effects. Vampire Survivors tiers its
  feedback the same way.
- **The beat frame is the budget.** The p99 pass rule measures it. Spawning
  must cost almost nothing per effect on the CPU: all of a frame's spawns go
  up in one GPU buffer, never as one object per effect.

### The effect list

About twenty effects cover the demo, chosen by TD8 from the vendor packs:

- **Hovl Studio:** sword slashes (Sword slash VFX), hit sparks (Magic hits),
  rings (AOE Magic spells), bolts and impacts (AAA Projectiles), the laser
  (3D Lasers Pack), ground circles (Magic circles) and parry flashes.
- **KriptoFX:** death bursts and explosions, where they read better than
  Hovl's.
- **Ambient:** embers and drifting wisps over the arena.
- **Monsters:** a small dust puff on each attack.

### The converter

The converter is an editor script that reads each chosen prefab through
Unity's public `ParticleSystem` and `ParticleSystemRenderer` APIs. It writes
an effect definition for our own runtime, or a parameter set for the VFX
Graph templates, whichever the spike picks. Its outputs are vendor-derived,
so they live only in the gitignored baked folder.

**The converter covers only what the chosen effects use.** It is not a
general Shuriken converter.

- **Inventory first.** Before writing the converter, TD8 runs an editor
  script over the chosen prefabs and all their child systems. It lists every
  module enabled, and every shape, render mode, curve mode and vertex stream
  in use, with the prefabs that use each. The table goes to TD8's evidence.
- **Build to the inventory.** The converter implements exactly the entries in
  that table. Anything else fails loudly, as below.
- **Swap before extending.** When one effect is the only user of a costly
  module, such as ribbons or sub-emitters, choose another effect that reads the
  same before implementing the module, unless no candidate reads as well.
  Record each swap in TD8's notes.

The modules the chosen effects might need, as a guide to what the inventory
will contain:

- **`main`:** duration, looping, start delay, lifetime, speed, 3D size and
  rotation, color, gravity, simulation space and max particles.
- **`emission`:** rate over time, rate over distance and bursts.
- **`shape`:** sphere, hemisphere, cone, circle, box, edge and donut, with
  arc, transform and direction randomization.
- **Over-lifetime modules:** velocity, limit velocity, force, color, size and
  rotation; also color and size by speed.
- **`noise`:** approximated with curl noise. It cannot match exactly. Every
  converted effect therefore gets an A/B still against the stock prefab, for
  review. A conversion is not a cut and has no threshold.
- **`textureSheetAnimation`, `trails` (ribbons), `subEmitters`** (birth and
  death), and `customData` with the renderer's active vertex streams (Hovl's
  shaders read dissolve and UV offsets from them).
- **`lights`:** converted into the effect's light cue.
- **`renderer`:** billboard, stretched, horizontal, vertical and mesh modes,
  the material, sorting fudge, and minimum and maximum particle size.

Curves and gradients in every mode (constant, two constants, curve, two
curves, gradient, two gradients, random color) are baked into 64-sample rows
of one lookup texture per definition.

Unsupported modules make the converter fail loudly. Collision, triggers,
external forces and lifetime by emitter speed are examples. It lists each one
in use per prefab:

```
error: hovl/Magic hits/Hit_Electric: unsupported modules in use
  collision (world, bounce 0.3) on child 'Sparks'
  fix: drop the module (cosmetic), or choose another effect
```

The runtime's draw shaders cover what the chosen effects' materials do:
additive, alpha blend, soft particles, dissolve and distortion. Distortion
samples URP's opaque texture.

- **Written from scratch.** They are our own code, written from each
  material's observed behavior (blend mode, inputs, vertex streams), so they
  are tracked.
- **Never copied.** No Hovl shader source is copied into them. A shader that
  must reuse vendor code counts as derived and lives in the baked folder.

### Choosing the GPU runtime

Two GPU runtimes could run the converted effects, and TD8 picks one with a
spike before building either out. The vendor packs contain no VFX Graph
assets. There are no `.vfx` files; Hovl and KriptoFX use Shuriken with 78
hand-written shaders and 26 Shader Graphs. So effects are rebuilt whichever
runtime wins, and the converter stays the same; only its output changes.

**Our own compute runtime** (see [The runtime](#the-runtime)):

- **One pool for every effect type,** so one simulation dispatch and one
  global back-to-front sort serve them all.
- **Exact control** over the beat-frame spawn path.
- **All code,** so agents own it end to end.
- **But it builds everything itself:** sorting, ribbons, sub-emitters and
  soft particles.

**VFX Graph driven by template graphs:**

- **Mature features.** Unity's own GPU particle system brings simulation,
  sorting within a system, ribbon strips, and sub-emitters through GPU events.
  It also has collision with the depth buffer (sparks bouncing off the
  ground), lit outputs and Shader Graph outputs.
- **One system per effect type, not one per hit.** Each frame the client
  fills a `GraphicsBuffer` with that type's spawn requests and sets a spawn
  count. The graph's initialize step reads each new particle's position, seed
  and scale from the buffer.
- **Templates.** About 5 to 8 generic graphs cover the effects: a billboard
  burst, stretched sparks, a ribbon trail, a mesh particle and a ground ring.
  Each exposes its curves as baked textures, and its flipbook and timing as
  properties. Every converted effect is one or more template instances with
  its own parameter set.
- **Built once, by hand.** Graphs can only be authored in the node editor.
  There's no supported API for building them in code, and `.vfx` files are
  fragile to edit as text. So someone builds the templates once: the operator
  in the editor (about an afternoon), or an agent carefully editing copies of
  the templates that ship with the VFX Graph package. After that, agents work
  only in data.
- **Sorting stops at the system.** Alpha-blended effects sort only within
  their own system, so overlapping smoke from two effect types can sort
  wrongly. Additive sparks, most of Hovl's look, are unaffected.

**The spike** builds one billboard-burst effect both ways. It runs both on
stress scene S4 (effects only) at heavy load, about 450 spawns on each beat
frame, and fills in a table like this:

```
spike: billboard burst, S4 heavy, 40 m, N 2,600
                    gpu p99   main p99   beat-frame spawn cost   authoring
own runtime         …         …          …                       code only
VFX Graph template  …         …          …                       one graph
```

- **Decision rule.** VFX Graph wins if its GPU and main-thread p99 times are
  within 10% of ours and its template was built or edited without
  hand-repairing the asset. Otherwise our runtime wins.
- **Recorded in TD8's notes:** both rows, the template's authoring route, and
  any VFX Graph feature that turned out to disable its batching.

### The runtime

Whichever runtime wins must meet the requirements below. The mechanisms
describe our own runtime; VFX Graph meets them through the spawn buffers and
templates described above. The runtime simulates and draws every instance of
an effect together, so its cost follows particles, not prefabs.

- **One pool.** A single GPU particle pool, shared by every emitter
  definition, holds state buffers, a dead list and an alive list. Each
  particle carries its definition's index. The pool is sized at twice its
  peak live count under the heavy load at the largest N swept. Capacity costs
  only memory, so an overflow signals a sizing bug.
- **Spawning.** Each frame the CPU uploads one buffer of spawn requests: the
  effects due this frame from the timeline, plus continuous emitters on
  projectiles and attached effects. Each request carries its definition,
  transform, seed and sub-frame time offset.
- **Compute kernels** emit, simulate (curves through the lookup texture),
  spawn sub-emitters and compact the alive list. Alpha-blended particles are
  then sorted back to front together, across effect types.
- **Drawing.** Indirect procedural draws (quads, ribbons and meshes) are
  issued from a URP renderer feature after opaques.
- **Randomness** comes from hashing the spawn seed and particle index, so
  fixed-time captures reproduce exactly.
- **Budgets are hard.** A spawn that would overflow the pool is rejected and
  counted. Any rejection fails the pass rule, because it would hide a hit or
  death effect.
- **Merging is a cut.** Identical spawns of one definition in the same frame,
  with centers within 8 px on screen, merge into one with combined density.
  It is allowed only after an A/B check.

### Effect lights

Effect lights are what make the dusk arena come alive, and the fourth item of
the fidelity floor.

- **Cues.** Each effect definition may carry a light cue: color, range and an
  intensity curve over lifetime. It comes from the prefab's lights module, or
  is authored where the prefab has none.
- **Clustering is always on.** Each frame, active cues are clustered on a
  1.5 m world grid into one light per cell: intensities summed, the position
  a weighted centroid, the range the largest. Effect lights reach several
  metres, so lights within 1.5 m merge without visible change. Larger cells
  are a cut, gated by an A/B check.
- **The pool.** Pooled URP point lights without shadows take the strongest
  clusters by **screen contribution**: intensity times the screen area of the
  light's range sphere.
- **Culling.** Clusters whose range sphere misses the view are culled, not
  dropped.
- **Pool size.** A quality setting that costs GPU time, so it is fixed before
  the first sweep with lights, and stays fixed. It starts at 128. TD8
  measures 64, 128 and 256 against the Forward+ visible-light limit of URP
  17.6, which TD8 confirms, and records its choice. S1 and S2 have no effect
  lights, so earlier sweeps do not depend on it.
- **Drops.** Visible clusters beyond the pool are dropped, weakest first, and
  their share of the frame's visible cue intensity is recorded. A run passes
  only if the p99 of that share is at most 2%. So a dense beat can drop its
  faintest lights, but a horde never goes dark.

## Camera

The camera is a game camera, so the measured counts apply to a playable
view. Its framing has to fit hundreds of monsters at once, which is the
premise of the genre.

- **Geometry.** A perspective view at 55° pitch with a 30° vertical field of
  view, following the hero. Its height is set by the **view width**: the
  width of ground visible across the middle of the screen.
- **Follow.** A critically damped spring with a 0.4 s time constant.
- **Zoom.** Changes ease over 2 bars at section boundaries.
- **Who computes what.** The choreographer runs the follow and zoom and stores
  them as the timeline's framing track (see
  [The choreographer](#the-choreographer)). The client adds punch and shake
  on top.
- **Punch.** A 2% zoom-in over a sixteenth note on accents, released over a
  beat.
- **Shake** grows sublinearly with the number of hits, so 100 hits on one beat
  never shake the screen 100 times harder. It offsets the camera in world
  metres:

```
amplitude = min(0.25, 0.08 × strongestImpact + 0.02 × log2(1 + hitsThisBeat))
```

Here `strongestImpact` is the beat's largest action strength, from 0 to 1.

The camera's distance and height follow from the view width at 16:9:

```
hfov     = 2 × atan(tan(15°) × 16 / 9)          ≈ 50.9°
distance = viewWidth / (2 × tan(hfov / 2))      ≈ viewWidth / 0.953
height   = distance × sin(55°)
```

How the three measured view widths frame the horde (approximate; TD4 measures
monster heights):

```
view width  camera height  visible ground  at 1-2 per m²   monster height
30 m        26 m           ~690 m²         ~700-1,400      ~65-95 px
40 m        34 m           ~1,220 m²       ~1,200-2,400    ~50-70 px
55 m        47 m           ~2,300 m²       ~2,300-4,600    ~35-50 px
```

Monsters off screen cost nothing to draw, except that their shadows reach in.

## The Environment

The environment is where the demo spends its extra frame budget. It should
look like a scene from a finished game: a moonlit forest glade, built from
Synty's classic POLYGON packs, with open ground for the fighting and rich
decoration around it. Its rendering gets the same treatment as the crowd: a
stock baseline, a measured ladder of optimizations, cuts gated by A/B checks,
and a density axis that shows what richness costs in monsters.

### Art direction

- **Mood.** An enchanted forest at night. Cool blue moonlight; warm pools of
  light from braziers, candles and pyres; cyan and violet magic from glowing
  trees, crystals and runes; low mist in the hollows.
- **Value structure.** The ground is dark and low in contrast. Monsters, the
  hero and effects carry the brightest values and the most saturated colors,
  so the horde reads at a glance even at a 55 m view.
- **Color separation.** The environment keeps to cool blues, greens and
  violets. Combat effects are warm and white-hot, so they never get lost in
  the set.
- **Composition.** Open clearings 30–60 m across, linked by paths and ringed
  by forest. Each clearing has one landmark the eye can rest on: a glowing
  elven tree, a ruined shrine, a stone circle, the walled courtyard or a
  graveyard.
- **No empty screens.** Every camera position the level script reaches shows
  some edge decoration or a landmark, never only bare ground.

### The kit

The scene uses classic POLYGON packs. Their foliage is almost all solid
geometry, and their materials mostly use the Standard shader, which Unity's
converter turns into URP Lit:

```
pack              what it provides
Fantasy Kingdom   trees (33), bushes and plants (75), rocks (90)
Adventure         trees (48) with solid canopies, on one small atlas
Elven Realm       glowing trees and canopies, ruins, statues, lamps, vines
Dark Fantasy      graveyard, tombs, ruins, candles, pyres, dead trees
```

- **No Nature Biomes packs.** Their cutout leaf cards shade the same pixels
  many times over, and their vegetation shaders are built-in-pipeline only
  (see [Alternatives Considered](#alternatives-considered)).
- **TD2 confirms the mix** with a style still: the roster and the kit
  together under night lighting, from the game camera.

### Layout

The layout is authored as tracked parameters and generated. The choreography
layer turns the parameters into the placement list (see
[Navigation around structures](#navigation-around-structures)), and an editor
script builds the scene from that list. So every wall the camera shows is a
wall the monsters walk around.

- **Areas.** The parameters name areas on the 160 × 160 m ground, each with a
  type: clearing, path, edge band, forest, landmark or structure. A clearing,
  landmark or structure may also have a name, which the level script uses as
  a section's goal.
- **Density by type:**
  - **Clearings and paths** are fighting ground. They carry only sparse clutter
    under 0.3 m tall (flowers, moss, scattered stones), and nothing blocks.
  - **Edge bands,** 3–6 m wide, carry bushes, rocks, fallen logs and vines,
    and partly block.
  - **Forest** is dense trees and rocks, and blocks. Canopies never overhang
    fighting ground.
  - **Landmarks and structures** are placed explicitly, not scattered.
  Structures include broken ruin walls and the walled courtyard with 3 or 4
  gaps that the level script and stress scene S6 use.
- **Seeded scatter** fills each area by its type's rules: spacing, scale and
  rotation jitter, and slope limits.
- **A density multiplier** scales edge bands and forest only, for the density
  sweep (see [Environment frontier](#environment-frontier)). Clearings stay
  clear at every density.

```json
{ "seed": 7, "size": [160, 160], "decorationDensity": 1.0,
  "areas": [
    { "type": "clearing", "name": "opening", "center": [0, 0], "radius": 28 },
    { "type": "landmark", "name": "glowing-tree", "at": [6, -4],
      "prefab": "elven/SM_Env_Tree_Glowing_01" },
    { "type": "structure", "name": "courtyard", "kind": "courtyard",
      "center": [-52, 30], "gaps": 4 },
    { "type": "forest", "ring": [68, 80] } ] }
```

### Lighting the night

- **Moonlight.** One directional light, cool blue, at 25° to 35° elevation.
- **Baked static light.** Lightmaps, baked in batch mode, hold the moon's
  light and shadows on the static set, plus the static light of braziers,
  lamps and glowing plants.
- **A static shadow map for monsters.** URP has no cached shadow maps. So at
  load, the client renders the static set's depth once from the moon's
  direction over the whole arena (4096² over 160 m, about 4 cm per texel).
  The crowd and hero shaders sample it, so monsters walk through tree shade,
  and the realtime cascades hold only the monsters and the hero.
- **Emissive glow.** Glowing trees, crystals, runes and flames use emissive
  materials, which bloom picks up for nearly nothing.
- **Few realtime environment lights.** At most 8 flickering flame lights,
  counted in S0 and separate from the effect-light pool.
- **Atmosphere.** Height-tinted distance fog, a few low mist cards in hollows
  (few, since they are transparent overdraw), and fireflies as ambient
  particles on the effect runtime.
- **Ambient for monsters.** Thousands of monsters cannot each sample light
  probes cheaply, so they take ambient from Unity's global ambient probe.
  Per-pixel Adaptive Probe Volume sampling is measured as an option.
- **Night grading.** A color-grading lookup table built for the night palette,
  on top of the post-processing in
  [Engine and Rendering Setup](#engine-and-rendering-setup).

### Environment rendering

The kit's prefabs, placed as plain GameObjects, cost about what Synty's own
demo scenes do: every prop its own draw call and its own shadow caster. The
environment's rendering is therefore optimized as deliberately as the crowd,
up a measured ladder. Every rung is measured on S0 at the authored density and
at twice it, and against S3 at the envelope, because the environment and the
crowd share the GPU.

```
rung  technique                                          targets
E0    stock: GameObjects, realtime shadows from all      the baseline
E1    GPU Resident Drawer instancing (+ its GPU          CPU draw submission
      occlusion culling, measured on and off)
E2    baked static lighting and the static shadow map;   shadow-pass geometry
      the static set leaves the realtime cascades
E3    decimated LODs for small props (a cut)             vertices and triangles
E4    impostors for the forest's interior (a cut)        the densest geometry
E5    capped textures and shared atlases                 memory and bandwidth
E6    the static set in or out of the depth prepass      prepass vertices vs
                                                         overdraw
```

- **E0 is the baseline.** It is the kit placed as plain GameObjects, with
  realtime shadows from everything: what dropping Synty's demo scene into URP
  would cost. The report compares the final rung against it, as the crowd's
  report compares against stock Unity.
- **Each rung is kept only if it helps.** A rung stays when it lowers S0's GPU
  or CPU time, or raises the envelope's monster count, without failing an A/B
  check. Otherwise it is recorded and dropped.
- **Cuts follow the fidelity rules.** E3's LODs and E4's impostors are cuts,
  gated by the same A/B checks as the crowd's, on the beauty shots (see
  [Making it look good](#making-it-look-good)), in stills and in motion.
- **Vendor tools help.** Amplify Impostors (for E4) and Mesh Combine Studio
  are already in the vendor asset project. Merging clustered small props with
  Mesh Combine Studio changes nothing visible, so it is not a cut; it is
  measured within E1, against instancing alone.
- **Texture caps are applied by our code.** An import override caps
  environment textures, never an edit to vendor files. The cap is the
  smallest size whose beauty-shot A/B passes, likely 1024 px, or 2048 for the
  most shared atlas.
- **Canopy fade.** Tree materials use a Shader Graph variant of URP Lit that
  dithers out canopy pixels within a few metres, on screen, of the hero, so
  the hero is never hidden. The layout keeps canopies off fighting ground; the
  fade covers forest edges.
- **Only what the kit uses is ported.** Elven Realm's triplanar rock shader is
  the one custom shader the kit needs. It is rewritten in Shader Graph, which
  makes it compatible with the GPU Resident Drawer.

### Environment frontier

The crowd's frontier asks how many monsters fit; the environment's asks how
rich the scene can be, and what that richness costs in monsters.

- **The density sweep** runs S0 at decoration densities 0.5×, 1×, 2× and 4×,
  and S3 at the envelope at 1× and 2×.
- **The report gives the curve:** S0's GPU and CPU times against density, and
  the envelope's monster count at each density tested. For example: "doubling
  the decoration costs 0.6 ms of target GPU, or about 350 monsters."
- **The ceiling.** S0, the empty arena with lighting and post-processing,
  must stay at or under 2.0 ms of GPU p99 on the M5 Max, about 4.5 ms of the
  target's 12.5 ms. Its main-thread and render-thread times must each stay
  under 1.0 ms. The authored density is the richest that fits the ceiling.
- **The headline count pays for the scene.** TD12's frontier is measured in
  the finished scene at the authored density, so the monster count already
  includes the environment's cost.

### Making it look good

Agents can't judge beauty by feel, so the scene is built against references
and checked from fixed shots.

- **Reference stills.** Each pack's own art-directed demo scene (Elven
  Realm's demo, Dark Fantasy's graveyard) is opened in batch mode and rendered
  from our camera's angle and height. Those stills set the bar for density
  and dressing.
- **Beauty shots.** Six fixed camera positions, named in the layout
  parameters: the opening clearing, a path between clearings, the
  glowing-tree landmark, the graveyard, the courtyard and a forest edge. Each
  is rendered at 1080p in fixed-time mode, once empty and once with a horde at
  the envelope count.
- **A self-review checklist,** recorded with each set of shots:
  - the horde reads against the ground at a glance;
  - every shot shows a landmark or edge decoration;
  - no canopy hides fighting ground;
  - effect colors stand apart from the set;
  - nothing floats, clips or repeats in an obvious pattern.
- **A readability measure.** For the horde shots, an ID render masks monster
  pixels, and the harness reports the luminance contrast between monster
  pixels and the ground around them. TD15 sets a floor from the first layout
  that passes the checklist, and later changes must not fall below it.
- **The operator's eye.** Each set of beauty shots goes to
  `operator-pending.md` for review. Feedback arrives later as new beads, and
  no bead waits for it.

## Sound

Sound is not part of the measured question, but the demo needs it to feel
right. It must stay as punchy at 100 hits a beat as at one.

- **Music.** The supplied *Action* MP3 is converted to streamed Ogg Vorbis
  and started with `PlayScheduled`. Setup accepts the local source path
  from Decision 13 (or an explicit replacement path), converts it into the
  gitignored baked folder, and copies the tracked `songs/action/` song map
  there. The map and playback use the same time origin; TD0 fixes the
  conversion command and checks that it preserves beat alignment, including
  any encoder delay. Neither the source MP3 nor any conversion or crop of it
  is committed: `.gitignore` excludes audio under `songs/action/` and
  `public/audio/action*`, and the vendor guard fails on it (see
  [The vendor guard](#the-vendor-guard)). TD0 records the NCS
  usage terms and required credits for the demo build and shared capture;
  the previous recording's public-domain status does not apply.
- **Effects** come from the vendor sound packs: Magic Spells SFX Bundle, RPG
  Magic Sound Effects Pack 3 and Universal Sound FX.
- **Aggregation happens in the choreography layer,** so the timeline's
  `SoundEvent`s are final: the live client plays them, and the video's offline
  mix renders the same list. Per beat and per sound category, at most 3
  voices play. Their gain grows with log2 of the number of hits, and
  variants are chosen by seed.
- **Scheduling.** The client schedules each sound event with `PlayScheduled`
  at its DSP time once it comes within 150 ms, using the timeline's
  lookahead. A pool of 32 voices covers the demo.

## Benchmark Harness

The harness turns the question into numbers. It runs stress scenes through the
real client, sweeps the axes, and finds the largest on-screen count that
passes at each point.

### Stress scenes

Each scene isolates or combines costs, so the report can say what limits a
point. Until TD9 builds the arena, scenes run in the **bench scene**: flat
ground under the default sun, ambient and post-processing. From TD9 on they
run in the arena, and TD12's sweep uses the arena.

```
S0  empty arena          environment, lighting and post; density 0.5x to 4x
S1  crowd walking        N monsters on beat gaits, no actions or effects
S2  crowd in combat      S1 + hero combos, hits, deaths, refills; no effects
S3  full combat          S2 + effects and lights at a load preset
S4  effects only         S3's effect stream with the monsters hidden
S5  worst beats          S3 at heavy load, a worst beat every 4 bars
S6  walls and chokes     S3 at demo load around the arena's walled courtyard
song                     the demo's own timeline
```

S6 needs the arena's structures, so it runs from TD9 on. The hero fights
inside the walled courtyard, and the horde funnels in through its gaps. That
tests:

- navigation cost under congestion, from the sim's tick timing;
- local density spikes at the gaps, for drawing and overdraw;
- wall shadows across the horde;
- whether jams look acceptable, which the operator reviews from captures.

### Sim tick timing

The client's measurements never include the choreographer, which runs at
load. But the choreographer stands in for a game's sim, and it already ticks
at 60 Hz over the whole timeline. So the player times every tick of that
load-time run, and the reserve becomes a measured number.

- **What is timed.** Each tick's wall time on the main thread, in the player
  build. A tick simulates everything a real game would: the on-screen horde,
  the off-screen reserve, navigation, separation, hits and spawns. Stress
  timelines hold N steady, so their ticks measure the sim at that N.
- **Warm-up.** The first 5 s of ticks are excluded, as frames are.
- **The sim's own line.** The tick's p99 time must stay at most 2.0 ms on the
  M5 Max (see [Performance Budget](#performance-budget)).
- **It is a conservative stand-in.** The choreographer is plain C# on one
  thread, without Burst or jobs. Passing means a real sim fits with room to
  spare. Failing means a real sim would need those optimizations, not that
  the design fails.
- **What it leaves out.** The ticks run before the frame loop, so contention
  with the client's frame (caches, the garbage collector) is not measured. A
  single-threaded tick loop that allocates nothing keeps that small.
  Determinism is covered by the byte-identical dump test, not here.

```json
{ "scene": "S6", "n": 2600, "offScreen": 1300,
  "simTickP99Ms": 1.42, "simTickMaxMs": 1.88, "flowRebuildSlices": 26 }
```

### The sweep

A sweep is a JSON spec of axes. For each combination of the other axes, the
harness searches for the largest N that passes.

```json
{ "scene": "S3", "seconds": 20, "warmup": 5, "probeSeconds": 8,
  "n": { "search": [100, 12000], "resolution": 0.05 },
  "viewWidthM": [30, 40, 55], "load": ["light", "demo", "heavy"],
  "aa": ["msaa4", "taa"], "jobWorkers": 6 }
```

The frontier is the resulting table of maximum N per view width, load and
anti-aliasing mode.

Sweeps hold Tollgate paused and sit on the plan's longest path, so the
search spends as few runs as it can:

- **One process per scene.** The player loads a scene once and changes the
  other axes in place. For a new N it rebuilds the stress timeline (timing
  its ticks, as at load) and resizes the crowd, then re-warms for 3 s. The
  first point gets the full warm-up.
- **A fitted search.** Each resource's frame time grows close to linearly
  with N. The harness measures three short probes spread over the search
  range and fits a line per resource: GPU, main thread and render thread.
  The lowest line's crossing of its pass line is the candidate N, and that
  resource is the point's limit.
- **Confirmed at full length.** A full run at the candidate N must pass and
  one 5% above it must fail. If either disagrees with the fit, the harness
  bisects between them as before.
- **Seeded from neighbors.** Probes start around the nearest point already
  measured, since adjacent points' frontiers are close.
- **5% resolution.** The envelope rounds down to the nearest 100 anyway.

A point costs about four runs instead of a bisection's seven, and no run
pays for a launch or a scene load.

### Measurements

Each run records per-frame timings and counters. Their summary is one result
row per run.

- **Timings** from Unity's `FrameTimingManager`: main-thread CPU,
  render-thread CPU and GPU frame time.
- **Counters** from our own systems: visible instances by type and LOD,
  skinned vertices, shadow casters, live particles, effect spawns per frame
  (and the largest in one frame), spawns rejected, light cues, lights used,
  and the share of light dropped. Also the sim's tick times from the run's
  load-time choreography, and its flow-field rebuild slices (see
  [Sim tick timing](#sim-tick-timing)).
- **Overdraw** from a debug replacement render that counts opaque depth
  complexity and transparent fragments per pixel. It runs on every 60th
  frame and on the frame after each downbeat, which catches the beat-frame
  peak. Those frames are excluded from timing.
- **Flags.** The report flags a point whose mean transparent fragments per
  pixel exceed 5, or whose beat-frame peak exceeds 12 (see
  [How many effects](#how-many-effects)).
- **Machine state:** thermal state, power source and energy mode, job worker
  count, and the backbuffer size.

```json
{ "scene": "S3", "n": 2400, "viewWidthM": 40, "load": "demo", "aa": "taa",
  "gpuP99Ms": 5.31, "mainP99Ms": 4.12, "renderP99Ms": 3.05, "maxFrameMs": 7.9,
  "onScreenAlive": 2231, "onScreenDying": 169, "particles": 26400,
  "spawnsMaxPerFrame": 171, "spawnsRejected": 0,
  "lightsUsed": 117, "lightDropShareP99": 0.004,
  "opaqueDepth": 1.21, "transparentPerPixel": 3.1, "transparentBeatPeak": 8.6,
  "thermal": "nominal", "power": "ac/high", "pass": true }
```

At the envelope point and the heavy-load point at its width (see
[Performance Budget](#performance-budget)), the harness also records a 10 s
Metal System Trace and keeps its per-pass GPU summary, not the raw trace:

```
xcrun xctrace record --template 'Metal System Trace' --time-limit 10s \
  --output <run>.trace --launch -- <app> --bench <point.json>
```

### Running a sweep

Sweeps run the real macOS player in a window, which benchmark beads may open
on the operator's screen.

```
PlaylistSurvivors3D.app/Contents/MacOS/PlaylistSurvivors3D \
  -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
  -job-worker-count 6 --bench sweep.json --out <evidence dir>
```

- **Exactly 1920×1080 pixels.** The camera must render into a 1920×1080-pixel
  backbuffer. On a Retina display a window's size in points is half its size
  in pixels, so TD7 finds the flags or window size that give exactly 1080p.
  The command above is illustrative. The harness asserts the backbuffer size
  at startup and refuses to run otherwise.
- **The worker cap.** Unity's `-job-worker-count` sets it, and the harness
  verifies `JobsUtility.JobWorkerCount` at startup.
- **No frame cap.** Vsync is off and the frame rate uncapped.

### Quiet machine

The machine is shared with Tollgate's gate and release stages and with other
agent sessions, and their load would corrupt measurements. Parallel epics
keep it busy, so a sweep can't wait for a machine that is idle on its own.
It quiets what it can, waits a bounded time for the rest, and reports what
it couldn't quiet.

- **The lock.** A sweep holds a machine-wide lock (an exclusive `flock` on a
  file in the per-machine cache). It waits for the lock, not around it, so
  two sweeps never overlap.
- **Tollgate pauses.** The sweep runs `tg pause`, then waits for any gate or
  release run in progress to finish. It runs `tg resume` on exit, a crash
  included, through a shell trap. A marker beside the lock file records the
  pause, so the next sweep resumes one that a killed sweep left behind.
- **Other sessions can't be paused.** Agent sessions on other epics keep
  building. The sweep waits up to 10 minutes for other processes to use less
  than 10% of total CPU over 5 s, then runs anyway.
- **Hard refusals.** A run never starts unless the Mac is on AC power, not in
  Low Power Mode, and its thermal state is nominal.
- **During a run,** it samples thermal state and other processes' CPU every
  second. Any violation marks the run noisy.
- **The window too.** macOS throttles a window that is covered or loses
  focus, so a run during which the player's window is occluded or loses key
  focus is marked noisy.
- **A noisy run** is repeated up to twice and then reported as noisy, never
  silently kept. The report counts the frontier points that rest on noisy
  runs.

### Scheduling sweeps

The heavy sweeps take hours of window time: TD5's baseline comparison,
TD14's density sweep and TD12's full sweep. They cost least when nothing
else runs.

- **The operator can schedule them.** Running those beads overnight, or
  while the operator is away, with other epics' executors stopped, avoids
  the waits and the repeated noisy runs.
- **Nothing waits for it.** No bead pauses for a quiet window. A sweep run
  among other work only takes longer, and reports what it couldn't quiet.

### The report

The report is generated from result rows and written to the evidence of the
bead that ran the sweep, with a short summary in that bead's notes below. It
gives:

- the frontier tables, with each point's limiting resource (GPU, main thread
  or render thread), its compute estimate, and the per-pass estimate where
  it was traced;
- the sim's tick timing at every frontier point and in S6;
- the baseline comparison: stock Unity's maximum against the chosen path's;
- the envelope, defined below, with every factor and assumption it rests on;
- the environment's density curve, what richness costs in monsters, and its
  final rendering against the stock baseline, rung E0;
- every accepted cut, with its A/B result.

The **envelope** is the recommended pair of a view width and an on-screen
count for game design. It is picked from the frontier at the demo load with
the chosen anti-aliasing, by one rule: **the widest view the hardware can
fill at fighting density.**

- **Fill count.** A view width's fill count is its visible ground at one
  monster per m² (690, 1,220 and 2,300 for 30, 40 and 55 m).
- **Envelope view width.** The widest measured width whose frontier count is
  at least its fill count. If no width qualifies, it is 30 m.
- **Envelope count.** The frontier count at that width, rounded down to the
  nearest 100.

```
frontier (demo load):  30 m → 1,600 (capacity-bound)  40 m → 2,600  55 m → 1,700
fill counts:           30 m →   690   40 m → 1,220   55 m → 2,300
envelope:              40 m, 2,600 on screen   (55 m cannot fill its screen)
```

The envelope's count is the headline count. The level script's per-section
counts are fractions of it, and no section's view is wider than its width.

## Fidelity A/B Checks

A cut is accepted by evidence, not by eye at the moment. Each cut gets a
deterministic capture pair and an image-difference score.

- **Capture.** Fixed-time mode renders 12 frames spread over 20 s at
  1920×1080, once without the cut (A) and once with it (B). The scene is S3
  at demo load and a 40 m view, with N at 2,000 or the current 40 m
  frontier, whichever is lower.
- **Score.** NVIDIA FLIP per frame pair: the mean error, and the 99th
  percentile per pixel, which catches small localized artifacts.
- **Thresholds.** TD11 calibrates them against two anchors that work on any
  crowd path:
  - **Invisible by construction:** song time offset by 1 ms.
  - **Plainly visible:** every monster's pose time rounded down to 1/15 s, so
    the crowd animates at 15 Hz.
  - A cut passes when both its scores sit at or below the invisible anchor's
    plus a third of the gap to the visible anchor's.
- **The tools.** TD4 builds the capture tool, which renders fixed-time stills
  and video pairs; TD8 and TD9 use it before any cut exists. TD11 adds the
  FLIP scoring and calibrates the thresholds. Their stills and videos carry
  the `ps-render` tag (see [The vendor guard](#the-vendor-guard)).
- **Motion.** Some cuts show only in motion, such as LOD popping. Each A/B also
  keeps a 2 s fixed-time video pair.
- **Review.** The operator reviews the stills and video pairs later, through
  `operator-pending.md`. No bead waits for that review.

```
cut: lod1-below-60px   scene S1, n 3000, 40 m
  mean FLIP 0.0041 (pass ≤ 0.0060)   p99 FLIP 0.071 (pass ≤ 0.090)   PASS
```

## Vendor Assets and Licensing

The demo is built from paid packs, and the repository is public on GitHub.
Synty's license and the Unity Asset Store license allow the assets in a game,
including a distributed build. They forbid redistributing the assets
themselves, and a public repository is redistribution.

### What never reaches git

- **Vendor files:** any file from `~/synty` or `~/unity_assets`. That covers
  meshes, textures, clips, prefabs, materials, shaders, sounds, music, scenes
  and their `.meta` files.
- **Derived files:** anything generated from vendor files. That covers bone
  textures, converted effect definitions, decimated meshes, impostors (flat
  pictures standing in for distant meshes), generated scenes, lightmaps, and
  edited copies of vendor prefabs, materials or scenes.
- **Builds:** the built apps and Unity's `Library`, which hold vendor content
  in compiled form.
- **The song's audio:** the *Action* MP3 and every conversion or crop of it.
  NCS audio is not ours to redistribute. The repo's other songs commit their
  public-domain source audio beside their song maps; this one never does.
  Only its song map, edit list and report summary are tracked.

Vendor files live only in the gitignored `demo3d/Assets/Vendor/` and derived
files only in the gitignored `demo3d/Assets/Baked/`. Tracked files are our own
code, shaders, parameters, annotations and tracked JSON.

Renders of the demo are fine to commit: stills in evidence, kept small. The
licenses allow screenshots of a game made with the assets. Videos stay in the
gitignored `captures/`.

### Curation and sync

Only the assets the demo uses enter the project, which keeps imports and the
`Library` small.

- **The manifest** is tracked JSON listing each vendor asset the demo uses:
  its source pack, path, GUID and purpose.
- **The sync tool** fills the vendor folder from the manifest:
  - From a Synty `.unitypackage.gz` (a gzipped tar of per-GUID folders, each
    holding `asset`, `asset.meta` and `pathname`), it extracts the listed
    GUIDs.
  - From `~/unity_assets/UnityAssets`, it copies the listed files with their
    `.meta` files.
  - Either way it follows `guid:` references in the copied YAML (prefab to
    material to texture or shader) to include everything the listed assets
    need.
- **GUIDs stay the vendor's,** so our tracked YAML can reference vendor
  assets by GUID. A reference is not vendor content.
- **A sync record** (gitignored) lists every synced file's GUID and SHA-256.
  The guard checks against it.

```json
{ "pack": "~/synty/POLYGON_Fantasy_Rivals_Unity_2021_3_v1_3_1.unitypackage.gz",
  "path": "Assets/PolygonFantasyRivals/Models/Characters.fbx",
  "guid": "…", "use": "elites: Big Ork, Troll" }
```

### The vendor guard

A leak to the public repository cannot be undone, so a guard checks every
change at three points: `scripts/check-fast`, the gate and the release stage.
The release stage is the last line of defense, because it moves remote
master. The guard fails when any tracked file:

- is inside the vendor or baked folders;
- is binary (by extension or a NUL byte) anywhere under `demo3d/`. The one
  exception is a PNG or JPEG under the demo's evidence that carries the
  capture tool's `ps-render` text tag, which marks it as our render;
- has a `.meta` GUID that appears in the sync record;
- has a SHA-256 that appears in the sync record;
- is audio (by extension or content) under `songs/action/`, or under
  `public/audio/` with a name starting `action`. `.gitignore` already
  excludes both, so this catches only a force-add or a renamed path.

More rules:

- **Our own images carry the tag.** Every tool that writes images (the
  capture tool, the A/B tool, the clip annotator's contact sheets, the report)
  adds the `ps-render` tag.
- **One record per machine.** The sync record lives in the per-machine cache
  (see [Unity Work by Agents](#unity-work-by-agents)). Every worktree and
  Tollgate's own checkouts read the same file.
- **It starts empty.** The setup command creates an empty record until the
  sync tool (TD2) first fills it. An empty record is valid.
- **A missing record fails safe.** If the record file is missing, the GUID and
  hash checks cannot run. The guard then fails any change that touches
  `demo3d/`, and only warns for changes elsewhere.
- **The guard has its own tests,** with synthetic fixtures: a fake record, a
  fake vendor file, a tagged render. TD1 proves every failure case with them
  before any vendor file exists.

```
vendor guard: FAIL
  demo3d/Assets/Art/Goblin_Color.png   binary file under demo3d/
  demo3d/Assets/Fx/Slash.prefab.meta   GUID 9f3c… is a vendor asset (Hovl)
```

AGENTS.md carries the rule for every agent: never commit vendor or derived
files, never force-add an ignored path, and never bypass the guard.

## Unity Work by Agents

Agents implement this demo, and they cannot use the Unity editor's interface.
Everything is therefore scripted, runs in batch mode, and fits the repo's
worktree-per-change workflow.

- **Batch mode for everything:** baking, scene generation, lightmapping,
  tests and builds.

  ```
  Unity -batchmode -projectPath "$WORKTREE/demo3d" \
    -executeMethod Demo3D.Tasks.BakeAll -quit -logFile -
  Unity -batchmode -projectPath "$WORKTREE/demo3d" \
    -runTests -testPlatform EditMode -testResults results.xml -logFile -
  ```

- **Scenes, prefabs and materials are generated** by editor scripts from
  tracked parameters. Agents never hand-edit vendor YAML, and edit our own
  Unity YAML only for small settings files.
- **The choreography layer also builds and tests with `dotnet test`** (SDK 10
  is installed), through an SDK-style project over the same source files.
  That takes seconds, without Unity.
- **One setup command prepares a worktree.**
  - A per-machine cache holds the vendor folder, the baked folder and a warm
    `Library` (under `~/Library/Caches/playlist-survivors/demo3d/`), keyed by
    the manifest's hash and the generators' input hash.
  - It clones all three into the worktree with APFS clones (`cp -c`), which
    are instant.
  - It rebakes only what is stale, and verifies the Unity license.

  ```
  $ demo3d setup        # in a fresh worktree; the command name is TD1's choice
  cloned Vendor (412 files), Baked (manifest 3f2a…), Library (warm)
  rebaking: bone textures for 1 changed clip annotation
  ```
- **A warm editor for the inner loop.** A cold batch-mode launch pays for the
  editor's startup and the asset database's check on every compile or test
  run, which adds up over the dozens of iterations a bead makes.
  - A task command starts one long-running batch-mode editor per worktree.
    It takes commands over a Unix socket in the worktree: refresh and
    compile, run EditMode tests, run a task method. Its listener restarts
    after each domain reload.
  - The task entry points use it when it is running and fall back to a cold
    launch otherwise. It exits after 30 idle minutes.
  - It holds the worktree's project lock, so every Unity step in that
    worktree goes through it while it runs.
  - **Before submitting,** a bead runs its Unity checks once cold, since a
    long-lived editor can carry stale static state across reloads. The gate
    and release stages always launch cold.
  - TD1 spikes it and records the cold and warm loop times. If it isn't
    reliable, cold launches stay, and the notes say why.
- **Projects are locked per path.** The operator's editor may have the main
  checkout's project open, and Unity locks a project per path. Agents only
  ever open their own worktree's project.
- **On-screen windows.** Only benchmark sweeps open a window. Everything else
  runs in batch mode and opens none.

## Automated Checks

Checks follow the repo's stages. They are path-gated on `demo3d/`, except the
vendor guard, which runs on every change.

- **`scripts/check-fast`:** the vendor guard, and `dotnet test` of the
  choreography layer when it changed.
- **The gate (`scripts/ci-gate`):** the vendor guard and `dotnet test`, plus a
  Unity batch-mode compile and EditMode tests. The Unity steps stay in the
  gate only if TD1 measures them within the gate's budget on a warm
  `Library`; otherwise they move to the release stage. A slow gate holds up
  every candidate in the repository, the survivors run's included, so when
  in doubt they move.
- **The release stage (`scripts/ci-full`, a new `ci-full-demo3d` step):** the
  vendor guard, the Unity import and compile, EditMode tests and the macOS
  player build.
- **Unity steps set up first.** Before any Unity step, the gate and release
  stages run the setup command in their own checkout. It clones the vendor
  folder, baked folder and warm `Library` from the per-machine cache, exactly
  as in a worktree.
- **No benchmarks** run in the gate or release stage. Benchmarks need a quiet
  machine and a window.

The choreography layer's tests:

- the same inputs give a byte-identical timeline dump;
- every impact lies within 1 ms of the grid (accents in free sections);
- every gait step starts on a beat, outside free sections;
- every hero stride outside an attack heads toward the section's goal until
  the hero reaches it;
- each section's measured deaths per second stay within 10% of its load
  preset;
- no two monsters overlap by more than a quarter of their radius;
- every hit and death has its clip and effect events;
- spawns appear outside the view at that section's view width;
- no monster or hero ever stands closer to a wall than its radius, beyond a
  small tolerance;
- on a test layout with a courtyard and gaps, every monster with a path
  reaches the hero, none stays stuck for more than 4 beats, and blocked
  monsters mark time in place instead of jittering;
- the flow field matches a reference shortest-path search on small test
  layouts;
- the song's timeline builds in under 3 s.

## What This Does Not Answer

The demo scouts a move of the game's client to Unity, but it answers only how
many monsters and effects fit. A move also needs these, which the demo leaves
open and nobody should read its report as settling:

- **Input and audio latency.** The native app spent much of its epic on the
  latency chain: the audio clock, the display link, and presses landing on
  beats. The demo takes no input, so Unity's DSP buffer, input and present
  latency on macOS go unmeasured.
- **Hosting the Rust sim.** The sim tick timing shows that a single-threaded
  C# sim like the choreographer fits the reserve. It says nothing about what
  the real Rust sim costs in Unity, or whether it would run as a native
  plugin or be ported to C#.
- **Windows.** No Windows player is built or run. Direct3D 12 shader
  compatibility and Windows driver behavior are untested, and every target
  number is inferred from the Mac.

## Alternatives Considered

The interviews weighed these and rejected them for the reasons given.

- **Rendering the game's Rust sim.** The numbers would transfer directly, but
  the demo would inherit the survivors run's content, pacing and 300-monster
  cap. The demo instead uses a forward choreographer of its own.
- **A custom Rust and wgpu engine.** It fits the repo's workflow best. But for
  this workload the gain over Unity's bypassable overhead is about 1 to 3 ms
  of CPU, while it costs a renderer, an animation system and a port of every
  effect.
- **Unreal Engine 5.** Niagara and Mass are strong, but it has a heavy baseline
  on laptop GPUs, weaker Mac support, binary assets agents cannot edit, and
  every Unity effect would need remaking.
- **Bevy.** The asset-conversion cost of a custom engine, without the control.
- **Per-vertex animation textures.** The cheapest crowd technique, but they
  cannot blend clips, which the fidelity floor requires.
- **DOTS with Latios Kinemation as a second crowd arm.** An open-source ECS
  animation layer proven at crowd scale. But Latios is confirmed on Unity 6.0
  and not on 6.6, where Entities is tied to the editor version, so the arm
  might have needed its own project on an older editor. It was not clearly
  faster than a bone-texture path, and it cost a bead and a bake-off.
- **Live-sim mode.** A second copy of the choreographer ticking beside the
  client every frame, checked against the timeline at sampled ticks. It
  would measure contention with the frame, but timing the load-time run gives
  the tick cost without a second copy, an 8.0 ms main-thread line or the
  sync logic.
- **A Windows compile check.** A Mono Windows player built from the Mac to
  compile the Direct3D 12 shaders, never run. Dropped with Windows from the
  demo's scope.
- **A trace at every frontier point.** Bandwidth-corrects every table cell,
  but needs a parsed Metal System Trace per point. Only the envelope and its
  heavy-load point are traced; overdraw counters flag the rest.
- **VFX Graph as the authoring tool for every effect.** It has GPU effects
  with editor features for free, but there is no supported API to build
  graphs in code, and its asset files are fragile to edit as text. Agents
  could not author twenty effects that way. VFX Graph as a runtime, driven by
  a few templates and converted data, remains a candidate in TD8's spike.
- **Stock effect prefabs, pooled.** Simple, but per-system overhead caps the
  horde long before the GPU does.
- **A path per monster** (Unity's NavMesh agents, or A* each). Every agent
  costs a GameObject or its own search, and its cost grows with path length
  and congestion. A flow field costs the same for 100 monsters or 5,000.
- **Velocity-based local avoidance** (RVO or ORCA). Smoother passing in
  sparse crowds, but heavier per monster, and a horde mostly converges on one
  target, where separation plus marching in place reads well.
- **Synty's Nature Biomes packs for the forest.** The lushest look in the
  library, but their foliage is cutout leaf cards, which shade the same
  pixels many times over and redraw into the shadow maps. Their vegetation
  shaders are built-in-pipeline only, and their demo scenes are about 19,000
  unbatched prefab instances. The classic POLYGON packs give a forest of
  solid geometry at a fraction of the cost.
- **Dropping a pack's demo scene in as the map.** Fast, but those scenes are
  dressed for eye-level vistas, and their layout wouldn't fit the clearings,
  navigation and placement list. They serve as reference stills instead.
- **Buying a target laptop.** It would make the answer measured instead of
  inferred, but was declined. The report labels inferred numbers.
- **Offscreen-only benchmarks.** They never disturb the operator, but they
  never measure present and compositor cost.
- **Upscaling by default.** About 30% to 40% more GPU headroom, but it smears
  small, fast sparks, which are much of the demo's juice.

## Implementation Plan

The work is a new epic in Hive, the project's issue tracker: `hv-50g9`,
with TD1 to TD15 filed as `hv-50g9.1` to `hv-50g9.15`, and TD0 (the song
map, split out of TD3 so it can start at once) as `hv-50g9.16`. Its work items
are **beads**. Each bead is implemented by an executor session (an agent
session working one bead) and delivered as one Tollgate candidate, under the
rules in AGENTS.md. The epic runs in parallel with `hv-n7fn`.

- **Plan keys.** Beads use the keys below.
- **Evidence** goes in `demo3d/evidence/<key>/` (lowercase).
- **Summaries** go in that key's subsection of [Bead Notes](#bead-notes).

### The beads

Each bead lists what it builds and when it is done.

- **TD0, the song map.**
  - Builds: the *Action* song map under `songs/action/`, through the song-map
    workflow in [tech-demo.md](tech-demo.md#building-the-song-map), using the
    full supplied MP3 rather than the earlier demo's source or crop. No score
    exists, so the expected form comes from the recording itself: its energy
    curve, spectrogram strips and onsets.
  - Hand-checks beats, downbeats, section transitions and accents, and
    identifies the final energy peak and the ending, without assuming a tempo
    ramp or free closing chords.
  - Records the source's identity (path, duration, SHA-256), the NCS usage
    terms and the required credits in `songs/action/source.json`.
  - Fixes the MP3-to-Ogg conversion command that setup uses (see
    [Sound](#sound)), and checks that the converted audio keeps the map's
    beat alignment, encoder delay included.
  - Needs no Unity and no other bead, so it starts at once.
  - Tracked output follows `songs/orpheus-galop/`: the song map, edit list,
    form notes and report summary. The audio stays out of git (see
    [What never reaches git](#what-never-reaches-git)). Report images stay in
    the analysis tool's gitignored work folder, since they carry no
    `ps-render` tag.
  - Done when: the map passes the analysis tool's `check`, and alignment
    against the converted audio is recorded.
- **TD1, foundations.**
  - Builds: the Unity project; `.gitignore` coverage; the vendor guard in
    check-fast, the gate and the release stage; the worktree setup command
    and its cache; batch-mode task entry points; the
    [warm editor](#unity-work-by-agents); the `dotnet` project for the
    choreography layer.
  - Spikes, recorded in its notes:
    - Unity's batch-mode compile and test time on a warm `Library`, which
      decides gate placement, and the same loop through the warm editor;
    - a windowed 1080p player reporting `FrameTimingManager` GPU times on
      Metal;
    - the C# version Unity 6.6 accepts.
  - Done when: one command sets up a fresh worktree; the guard's tests catch
    every failure case with synthetic fixtures; and an empty scene builds for
    macOS. The [vendor guard drill](#vendor-guard-drill) runs once TD2 and
    TD4 exist.
- **TD2, assets.**
  - Builds: the manifest, the sync tool and its record; the clip annotator
    and its contact sheets.
  - Chooses: the roster, the hero, the effect candidates and the sounds, and
    the kit's pieces from the classic packs in [The kit](#the-kit).
  - Done when: every roster type's walk, attack, hit and death clips and
    every hero combo and locomotion clip are annotated, and a style still
    shows the roster and the kit together under night lighting.
- **TD3, choreography on open ground.**
  - Builds, from TD0's song map: the timeline types (with `Tick`), the choreographer with its
    framing track, kill-rate feedback, free sections and sound aggregation,
    the level-script and stress-script formats with the off-screen reserve,
    the sim tick timing, and the layer's tests.
  - Without walls, the hero walks straight toward each section's goal, which
    a test layout names; TD6 adds the flow fields.
  - Uses placeholder clip annotations: a tracked JSON file of plausible
    impact times, ends and strides per placeholder roster type. So it runs in
    parallel with TD2. TD4 switches to TD2's real annotations.
  - Done when: the song's timeline and every stress script's timeline build
    and pass the layer's tests on open ground, with a placeholder envelope.
- **TD4, client playback.**
  - Builds: the client's timeline evaluation (segments, action states,
    retiming and the two animation slots), the song clock, fixed-time mode,
    the bench scene, the development flags listed in
    [Manual QA](#manual-qa), and the stock baseline crowd and a simply drawn
    hero, all driven by TD3's timelines with TD2's annotations.
  - Also builds the capture tool: fixed-time stills and video pairs, with
    the `ps-render` tag.
  - TD7 builds the harness alongside it. Whichever of TD4 and TD7 lands
    second registers TD4's stress scenes and the sim tick timing with the
    sweep runner.
  - Done when: the [first assembled check](#first-assembled-check) passes.
- **TD5, bone-texture crowd.**
  - Builds: the bone-texture baker, the instance pipeline, both skinning
    variants, culling, depth bins, the shadow extension and motion vectors.
  - Starts once TD2 lands, on a test scene that cycles the roster's clips at
    random phases. Its hookup to TD4's timeline evaluation waits for TD4.
  - Compares both variants with the stock baseline, as in
    [Against the baseline](#against-the-baseline), using TD7's harness, and
    deletes the slower variant. This is the first sweep of a real crowd.
  - Done when: it draws S1 and S2 within the fidelity floor, and the
    comparison is in its notes.
- **TD6, navigation.**
  - Builds: the placement-list generator (areas, scatter rules, structures
    and the density multiplier) and footprint annotations for TD2's kit
    pieces; the obstacle map, the wall distance field, the time-sliced flow
    field toward the hero, the hero's per-section goal fields, and marching
    in place.
  - Done when: the navigation tests in
    [Automated Checks](#automated-checks) pass on the courtyard test layout,
    and the song and stress timelines still pass with walls.
- **TD7, harness.**
  - Builds: the windowed benchmark player and the sweep runner with its
    fitted search, result rows, the quiet-machine lock with its Tollgate
    pause, the overdraw counters and the Metal System Trace summary.
  - Starts once TD1 lands, in parallel with TD2 and TD3. Until TD4's stress
    scenes exist, it runs on a **synthetic load**: N instances of one stock
    mesh, GPU-instanced and moving, which is enough to exercise the search,
    the timings and the lock.
  - Adds a line to AGENTS.md's 3D tech demo rules: a sweep pauses Tollgate,
    so a paused queue during a sweep is expected, not a tooling failure.
  - Done when: a sweep of the synthetic load produces result rows and a
    first frontier, and the
    [sweeps and machine lock](#sweeps-and-the-machine-lock) checks pass.
- **TD8, effects.**
  - Opens with the runtime spike in
    [Choosing the GPU runtime](#choosing-the-gpu-runtime), and records its
    decision. If VFX Graph wins and its templates need the operator, those
    templates go to `operator-pending.md`, and TD8 continues on the parts
    that don't need them.
  - Builds: the module inventory of the chosen prefabs, then the converter,
    scoped to that inventory (see [The converter](#the-converter)); the
    chosen runtime, light cues, clustering and the light pool. Converts the
    effect list under the design rules for a horde.
  - Done when: the spike's table and decision and the module inventory are
    in its notes and evidence; every listed
    effect runs on the runtime with an A/B still against its stock prefab;
    and the light pool's size is fixed.
- **TD9, the arena.**
  - Builds: the arena's layout parameters, with named goal areas for the
    level script; the scene generated from TD6's placement list, with the
    ruin walls, the walled courtyard and the density multiplier; the kit's
    conversion to URP Lit;
    the night lighting, lightmaps and static shadow map; and the
    post-processing stack. S6 becomes runnable.
  - Starts once TD4, TD6 and TD7 land, lighting TD4's stock crowd. Its crowd
    steps wait for TD5: the static shadow map in the crowd shader, and the
    anti-aliasing decision, since TAA leans on the crowd's motion vectors.
  - Decides MSAA against TAA. Both are full quality, so this is not a cut:
    the choice weighs cost against capture pairs of monster edges and spark
    trails, which also go to the operator for review.
  - Done when: the arena renders at every density, S0 is measured at rung E0,
    and the anti-aliasing choice is recorded with its captures and its
    cost.
- **TD10, hero and presentation.**
  - Builds: the hero's playable graph, combos, spells, parries, local
    hit-stop, the client's punch and shake, and sound scheduling.
  - Done when: the song's first minute plays with every blow on its click.
- **TD11, cuts.**
  - Adds FLIP scoring to TD4's capture tool and calibrates the A/B
    thresholds. Then tries LOD thresholds, shadow proxies,
    the 30 Hz clip rate, larger light-cluster cells, effect merging, and
    dropping glow quads in favor of bloom.
  - Done when: each cut is accepted or rejected, with its scores.
- **TD14, environment rendering.**
  - Climbs the ladder in [Environment rendering](#environment-rendering),
    rung by rung, and runs the density sweep. Rungs E3 and E4 are cuts and
    use TD11's A/B thresholds; the other rungs can start as soon as TD9
    lands.
  - Done when: every rung is measured, kept or dropped with its numbers; the
    density curve is in its evidence; and S0 at the authored density fits the
    environment's ceiling.
- **TD15, the art pass.**
  - Builds: the reference stills from the packs' demo scenes, the six beauty
    shots, the readability measure and its floor, and the landmarks and
    dressing that meet [the art direction](#art-direction).
  - Starts once TD8 and TD9 land, dressing the scene at TD9's density. Its
    last step waits for TD14: spending the headroom TD14 frees, the richest
    scene that keeps S0 within its ceiling.
  - Done when: every beauty shot passes the checklist and the readability
    floor, S0 fits the ceiling, and the shots are in its evidence and in
    `operator-pending.md`.
- **TD12, the frontier.**
  - Runs the full sweep with every point's compute estimate, at TD9's
    anti-aliasing mode; the other mode is measured only at the envelope
    point, for the report. Then it runs the traces
    and per-pass estimates at the envelope point and its heavy-load point.
    Writes the report and the envelope, and puts the envelope into the level
    script.
  - Also runs S6 at the envelope, and reports the sim's tick timing at every
    frontier point and at S6. The report states whether the envelope holds
    among walls and whether the sim fits its reserve. If S6 fails at the
    envelope, the report gives S6's own frontier and what limits it.
  - Also carries TD14's environment results into the report: the density
    curve, the richness-to-monsters trade, and the final rung against E0.
  - Done when: the report is in its evidence and its summary in its notes.
- **TD13, the demo.**
  - Tunes the level script so the climax holds the envelope and the full song
    passes. The song's climax adds the boss, the laser and denser spells to
    the demo load. If it fails, TD13 thins the climax's spells first and the
    boss's effects second, and never lowers the count. If it still fails, the
    envelope was wrong: TD13 lowers it in 5% steps and corrects TD12's report.
  - Builds the offline mixer and records the video capture with its mix. Adds
    the operator's viewing check to `operator-pending.md`.
  - Done when: the song run passes and the video is in `captures/`.

### Order and parallelism

```
TD0, TD1    first, in parallel (TD0 needs no Unity)
TD2         after TD1
TD3         after TD0 and TD1, in parallel with TD2 (it uses placeholder
            clip annotations)
TD7         after TD1, in parallel with TD2 and TD3 (on a synthetic load)
TD4         after TD2 and TD3
TD5         after TD2; its timeline hookup after TD4, its baseline
            comparison after TD7
TD6         after TD2 and TD3 (it needs the kit's footprints)
TD8         after TD4 and TD7 (its spike runs S4 on the harness)
TD9         after TD4, TD6 and TD7 (it generates the arena from the
            placement list and measures S0 on the harness); its crowd
            steps after TD5
TD10        after TD5 and TD8
TD11        after TD5, TD8 and TD9
TD14        after TD9; its cut rungs (E3, E4) after TD11
TD15        after TD8 and TD9; its headroom step after TD14
TD12        after TD10, TD11, TD14 and TD15
TD13        after TD12
```

- **The graph is kept wide.** A bead starts as soon as its first steps can,
  so two executors rarely wait and a third has work most of the time.
- **A step that waits.** Where a later step of a bead needs another bead
  ("its crowd steps after TD5"), the bead starts without it. If the other
  bead is not on `staging` when that step comes up, the executor lands what
  is done as its own candidate, adds a dependency edge on the other bead,
  and finishes the step after it lands.

### Reviews

- **One cold review per milestone.** A fresh read-only agent runs the repo's
  warden review skill. Whichever of the milestone's beads finishes last
  requests it, once all their commits are on `staging`, on the commits its
  beads recorded in their notes:
  - foundations: TD0 to TD4, TD6 and TD7;
  - the crowd: TD5;
  - the scene: TD9, TD14 and TD15;
  - the demo's systems: TD8, TD10 and TD11;
  - the answer: TD12 and TD13.
- **A per-bead review for the vendor guard** (TD1, and any later change to
  it), because a leak cannot be undone.
- **AGENTS.md's 300-line trigger** does not apply to this epic.
- **Self-review before a review** follows AGENTS.md's checklist, recorded in
  the bead's notes.

### Working rules

- **Playable after every bead.** Each bead from TD4 on keeps the demo
  runnable and writes its run command in its notes.
- **Commit SHAs.** Each bead records its commit SHA in its notes, so a
  milestone's review covers exactly its beads' commits.
- **No approval pauses.** Checks only the operator can make go to
  `operator-pending.md`.
- **Prototype rules.** Prefer the simplest option wherever it does not change
  a measurement.

### A possible Windows test machine

The operator has a Windows desktop with an i7-12700F and an RTX 4070 Ti. It
could serve as a spot check of the inferred numbers, though Decisions 3 and 4
rule out a Windows player for now. Its GPU is the target's architecture
(Ada), so the Mac's tile-based caveats drop away, but it is about 2.5× an RTX
4060 Laptop (Steel Nomad about 5,040 against 2,025, or 1.3× the M5 Max), with
only 2.0× its bandwidth (504 GB/s). With the same 1.2× margin, the 12.5 ms
target maps to a **GPU pass line of about 4.2 ms, roughly 240 fps at
1920×1080**. That is this machine's equivalent of the Mac's 180 fps, or 5.0
ms (200 fps) without the margin. One 12700F performance core is roughly a
13620H core (within about 10%, unmeasured here), so its main-thread and
render-thread lines are about 10 ms, and its sim line about 3.3 ms per tick.
Job workers stay capped at 6, of its 8 performance cores. Bandwidth-bound
passes will translate worse than the 2.5× compute ratio, as on the Mac.

## Bead Notes

Each bead summarizes its measurements here in a few lines, in its own
subsection, linking `demo3d/evidence/<key>/`. Beads never add sections
elsewhere in this document; parallel beads would collide there.

### TD0

Not started.

### TD1

Not started.

### TD2

Not started.

### TD3

Not started.

### TD4

Not started.

### TD5

Not started.

### TD6

Not started.

### TD7

Not started.

### TD8

Not started.

### TD9

Not started.

### TD10

Not started.

### TD11

Not started.

### TD12

Not started.

### TD13

Not started.

### TD14

Not started.

### TD15

Not started.

## Manual QA

Manual QA watches the demo and its tools the way the operator will use them.
The development build's command-line flags put it into each state needed
here, since the demo itself takes no input:

```
--overlay           on-screen counts, frame times against the pass lines
--click             mix a click track: every beat, louder on downbeats
--start-at <s>      seek the song; --seconds <n> to exit after n seconds
--fixed-time        deterministic 1/60 s frames, sound muted
--scene <S0..S6|song> --n <count> --view-width <m> --load <preset> --aa <mode>
--freeze-at <s>     hold one frame for inspection
--show-lod          tint monsters by LOD; --overdraw for a heat map
--show-nav          draw the flow field and blocked cells over the ground
```

### First assembled check

Run this as soon as TD4 has landed, before the GPU crowd or effects
exist. It exercises the main integration boundary: the choreographer's
timeline driving the client's crowd and hero on the audio clock. TD4's stock
baseline crowd handles a few hundred monsters, which is enough.

- Launch the song with `--click --overlay --seconds 60` and about 300
  monsters.
- **Expect** every monster's gait step, every hit flinch and every death to
  land on a click, with no visible drift over the minute.
- **Expect** feet that do not slide, and crossfades without pops.
- **Expect** the overlay's counts to match the level script's targets.
- Seek with `--start-at 45`. **Expect** the horde's state at 0:45 to appear
  immediately, with the same positions as a run from the start.
- Seek into the mapped ending. **Expect** hero slams on its curated accents.
  If TD0 identifies a free section, seek there too: **expect** monsters to
  advance smoothly instead of stepping to beats.
- Seek into a laser sweep, once TD10 has added it. **Expect** swept monsters
  to flash without flinching or sliding back.

### Vendor guard drill

- In a scratch worktree, force-add a file from the vendor folder and run
  `scripts/check-fast`. **Expect** a failure naming the file.
- Copy a vendor PNG into a tracked folder under `demo3d/`. **Expect** the
  binary-file failure.
- Copy a vendor prefab with its `.meta` into a tracked folder. **Expect** the
  GUID failure.
- Commit a capture-tool still under the evidence. **Expect** a pass.

### The full demo

- Run the showcase build windowed at 1080p with `--overlay`.
- **Expect** the full *Action* recording (about 3:19), with the arc from a
  handful of monsters to the envelope count at the mapped final energy
  peak, then the ending's accents clearing the screen.
- **Expect** the hero to travel between the level script's goal areas,
  fighting on the way, and every screen to show a landmark or edge
  decoration.
- **Expect** the overlay's rolling 20 s p99 times to stay under the pass lines
  all the way through, with no frame over twice its line.
- **Expect** every hit and death to be visible, with no effect missing in the
  densest beats and no lights flickering out.
- Watch the frames right after the densest downbeats, where a few hundred
  effects spawn at once. **Expect** no stutter on the beat, and the overlay's
  largest spawn count per frame to match the stress estimates (about 150 at
  demo load).
- Compare an early fight against one monster with the climax by eye. **Expect**
  each monster to look and react the same.
- Listen at the densest beats. **Expect** weight, not clipping or a wall of
  identical sounds.

### Sweeps and the machine lock

- Run a small sweep (one scene, two points). **Expect** result rows with
  timings, counters, machine state and a pass flag.
- Start a heavy build in another terminal, then run the sweep. **Expect** a
  bounded wait, then runs marked noisy, repeated, and counted in the report.
- Watch `tg status` during a sweep. **Expect** Tollgate paused while it runs
  and resumed when it ends.
- Kill a sweep with `kill -9`, then start another. **Expect** the new sweep
  to report and resume the pause the killed one left.
- Unplug the power. **Expect** a refusal.
- Run three sweeps back to back. **Expect** any thermal throttling reported,
  never hidden.
- Launch with a different window size. **Expect** a refusal naming the
  backbuffer size.
- Check every result row. **Expect** `jobWorkers: 6`.

### Walls and the sim

- Run S6 windowed with `--overlay`, about a minute.
- **Expect** the horde to flow around the courtyard walls and through its
  gaps, and never to pass through a wall.
- **Expect** jams at the gaps to read as monsters marking time on the beat,
  not jittering or vibrating against each other.
- **Expect** elites and the boss to fit through every gap.
- Check the result rows of S6 and one S3 frontier point. **Expect** the
  sim's tick p99 at or under 2.0 ms.

### The scene

- Flip through the six beauty shots, empty and with the horde. **Expect** a
  scene that looks finished: a landmark or edge decoration in every shot,
  readable monsters, and effects that stand out from the set.
- Run S0 windowed with `--overlay` at 1× and 2× density. **Expect** S0's GPU
  p99 under 2.0 ms at the authored density.
- Walk the hero along a forest edge (`--start-at` a section that does).
  **Expect** canopies near the hero to dither out, and never to hide it.
- Look for floating props, clipping, and obvious repeating patterns in the
  scatter. **Expect** none.
- Compare the final scene with rung E0 in the report. **Expect** the same
  look at a fraction of the cost.

### A/B review

- For each accepted cut, flip between its A and B stills at 100% zoom, and
  watch its video pair.
- **Expect** no visible difference. Any visible one goes back to its bead as a
  failed cut.
- For each converted effect, compare its still with the stock prefab's.
- **Expect** the same shape, color and timing.

### Edge cases

- **A missing vendor cache:** run setup in a fresh worktree with the cache
  folder renamed. **Expect** a clear error naming the missing source.
