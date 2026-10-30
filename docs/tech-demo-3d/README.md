# 3D Tech Demo

The design and plan for a Unity tech demo, built and measured on Cygnus, the
operator's Windows desktop, as a standalone run (see
[Working on Cygnus](windows.md)). It is split into these documents:

- **This page:** the question, the decisions, and an overview.
- [Fidelity and Performance](performance.md): the fidelity floor, the frame
  budget and the engine setup.
- [Choreography](choreography.md): the C# layer that decides everything, and
  time and synchronization.
- [Crowd and Hero](crowd.md), [Effects](effects.md),
  [Environment](environment.md) and [Camera and Sound](camera-and-sound.md):
  what the client draws and plays.
- [Benchmarks and A/B Checks](benchmarks.md): the harness, the sweeps and the
  report.
- [Vendor Assets](vendor-assets.md): what never reaches git, and the guard.
- [Working on Cygnus](windows.md): how the run works, the machine, Unity work
  by agents, and the checks.
- [Running on the Mac](mac.md): setting up, building and watching the demo on
  the operator's Mac since integration.
- [Implementation Plan](plan.md): the tasks TD0 to TD15, their order, status
  and notes, and what is pending on the operator.
- [Manual QA](qa.md) and [Alternatives Considered](alternatives.md).
- [Style Lab](stylelab.md): a separate Unity project that shows the demo's
  horde in stylized shader treatments, to choose a look.
- [Trailer Mode](trailer.md): `--trailer`, a 30-second gameplay sizzle reel
  cut on the beat across the environments.

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
the native Rust app ([native-app.md](../design/native-app.md)) to Unity. So the report
gives what stock Unity does as well as what bespoke systems reach. What else
that decision needs is listed in
[What This Does Not Answer](#what-this-does-not-answer).

It produces three things:

- **A frontier report.** For each camera zoom and effect load, the largest
  number of on-screen monsters that fits the frame budget, what limits it, and
  a recommended envelope for game design. It also shows how rich the
  environment can be, and what that richness costs in monsters.
- **The demo build.** A Windows app that plays the full 3:19 song, with the horde
  peaking at that envelope in the climax.
- **A capture.** A 1080p60 video of the demo for sharing.

The demo is a prototype that establishes capabilities. None of its code is
meant to ship. Wherever a choice does not change the measured answer, the
simplest option wins.

### Decisions

These were settled with the user on 2026-10-26 in a design interview, and
revised the same day in a review interview. The target song was changed to
*Action* at the user's request on 2026-10-27. The same day, the user moved
development and measurement from the M5 Max Mac to **Cygnus**, their Windows
desktop (Decisions 3, 4, 16 and 18). On 2026-10-28 the user made the demo a
standalone run on Cygnus, outside the repo's issue tracker and delivery tool
(Decisions 17 and 18). Agents treat these decisions as fixed. Every other
choice is the implementing agent's, recorded with its reasons in the task's
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
   - The demo is a Windows x64 player on Direct3D 12. No macOS player is
     built or measured.
4. **Validation happens on Cygnus only:** an Intel Core i7-12700F with an
   NVIDIA RTX 4070 Ti (12 GB) and 32 GB of RAM, on Windows 11 Home 25H2.
   - Its GPU has the target's architecture (Ada Lovelace), so its costs scale
     to the target instead of hiding behind a different design, as the Mac's
     tile-based GPU did.
   - Budgets are translated from published benchmarks; no target device is
     bought. Every target-hardware number in the report is labeled as
     inferred and shows its factors.
5. **The client gets 75% of the target frame:** 12.5 ms of 16.7 ms. The rest
   is reserved for the real game's sim layer, HUD, audio and input. The
   headline count is measured against this budget. It is the count of the
   **envelope**, the recommended view width and on-screen count (defined in
   [The report](benchmarks.md#the-report)), and the demo's climax runs at exactly that
   envelope.
6. **Engine: Unity 6.6 (6000.6.7f1) with URP on Direct3D 12,** plus bespoke
   GPU-driven systems for the crowd and the particles.
7. **The project lives in this repository.** Paid vendor assets, and anything
   derived from them, are never committed, because the repository is public
   (see [Vendor Assets and Licensing](vendor-assets.md#vendor-assets-and-licensing)).
8. **The game camera.** A fixed high three-quarter perspective view follows
   the hero, with beat-synced punch, shake and slow zooms. "How many" means on
   screen at once. View width is a measured axis: 30, 40 and 55 m.
9. **The fidelity floor.** Every visible monster keeps full skeletal
   animation, a real cast shadow, its own hit and death reactions and effects,
   and dynamic light from nearby effects, at any horde size (see
   [The Fidelity Floor](performance.md#the-fidelity-floor)).
10. **Crowd animation runs on our own bone-texture GPU path,** measured
    against stock Unity as a baseline. DOTS with Latios Kinemation was
    dropped: it is unconfirmed on Unity 6.6 and not clearly faster.
11. **Effects run on GPU particles from the start.** An editor converter
    carries the chosen Hovl Studio and KriptoFX prefabs into the GPU runtime.
    TD8 opens with a spike that picks it: our own compute-shader runtime, or
    VFX Graph driven by a few template graphs (see
    [Choosing the GPU runtime](effects.md#choosing-the-gpu-runtime)).
12. **A night forest glade built from classic Synty POLYGON packs,** with open
    clearings for the fighting and rich decoration around them, baked
    moonlight, emissive glow and effect lights that dominate (see
    [The Environment](environment.md#the-environment)).
    - **It must look good.** It's art-directed, and checked from fixed beauty
      shots.
    - **Its rendering is optimized as deliberately as the crowd's,** with its
      own stock baseline, ladder of techniques and density sweep. The report
      gives the trade between scene richness and monster count.
13. **The song is *Action* by Alessia Labate & Conrad Taylor (NCS Release),**
    using the user-supplied MP3, 198.968 s (about 3:19, measured with
    `ffprobe`). On Cygnus it is
    `D:\vendor\songs\action\Alessia Labate, Conrad Taylor - Action [NCS Release].mp3`,
    copied from the Mac's `~/Downloads` (see
    [Moving the assets](windows.md#moving-the-assets)).
    The full recording is the timing reference; TD0 maps its sections and
    accents before setting the horde's build, releases and final climax.
14. **The hero fights with sword combos and beat spells.**
15. **Native 1080p by default.** MSAA 4× and TAA are compared. Upscaling is
    reported only as extra headroom, never in the headline count.
16. **Benchmark sweeps may take Cygnus's screen at any time.** Cygnus is
    mostly idle, and the **operator**, the developer who runs the demo and
    reviews its evidence, rarely uses it while agents work. Routine checks
    stay windowless.
17. **The plan is markdown,** with plan keys `TD0` to `TD15`: tasks, status
    and notes live in [the Implementation Plan](plan.md), not in Hive's beads.
18. **A standalone run on Cygnus.** Agents work on Cygnus directly, with no
    Mac orchestration, no beads and no Tollgate. They commit to the `demo3d`
    branch of a private relay repository. When the demo is finished, it comes
    back into remote master once, as a few squashed commits landed through
    Tollgate on the Mac after full validation (see
    [Working on Cygnus](windows.md)). The demo doesn't touch the rest of the
    repo's code or CI until then.

## Related Information

- [AGENTS.md](../../AGENTS.md): the repo's working rules, and the
  vendor-asset rule this demo adds. Its Tollgate delivery (worktree
  candidates, the gate and release stages) applies to the demo only at the
  final integration (see [Integration](windows.md#integration)).
- [tech-demo.md](../design/tech-demo.md): the earlier 2D browser tech demo. Its song-map
  workflow builds this demo's song map. The workflow uses the commands of the
  repo's Python beat-analysis tool (`crop`, `analyze`, `report`, `edit`,
  `check` and `click`). Its effect catalogue
  is a reference for juice: the feedback effects (particles, flashes, shake,
  hit-stop, impact sounds) that make actions feel physical.
- [beat-steps.md](../design/beat-steps.md): why the 2D demo replaced choreography
  planned backwards from kills with a forward simulation. This demo's
  choreographer is a forward simulation for the same reason.
- [survivors-run.md](../design/survivors-run.md): the finished survivors run. Its Vampire
  Survivors numbers (speeds, kill pacing) are a reference for plausible horde
  behavior.
- [native-app.md](../design/native-app.md): the native Rust app, its windowless-testing
  rule and its evidence conventions.
- Steam Hardware & Software Survey, September 2026:
  [video cards](https://store.steampowered.com/hwsurvey/videocard/) and the
  [overview](https://store.steampowered.com/hwsurvey/Steam-Hardware-Software-Survey-Welcome-to-Steam)
  (Windows 95%, macOS 1.9%, 1920×1080 on 48% of primary displays).
- 3DMark Steel Nomad scores: about 5,040 for an RTX 4070 Ti and about 2,025
  for an RTX 4060 Laptop. TD7 confirms both from a published comparison and
  records the source.
- [windows-delayed-spike.md](../design/windows-delayed-spike.md): the earlier Windows
  spike on Cygnus, and how that machine was first set up.
- [NVIDIA FLIP](https://github.com/NVlabs/flip): the image-difference metric
  behind the fidelity A/B checks.
- Asset sources, read-only. On Cygnus, the copies under `D:\vendor` (see
  [Moving the assets](windows.md#moving-the-assets)), made from the Mac's
  originals:
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
  scenes**, listed in [Stress scenes](benchmarks.md#stress-scenes).

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
  like it fits in that reserve (see [Sim tick timing](benchmarks.md#sim-tick-timing)).

## What This Does Not Answer

The demo scouts a move of the game's client to Unity, but it answers only how
many monsters and effects fit. A move also needs these, which the demo leaves
open and nobody should read its report as settling:

- **Input and audio latency.** The native app spent much of its epic on the
  latency chain: the audio clock, the display link, and presses landing on
  beats. The demo takes no input, so Unity's DSP buffer, input and present
  latency go unmeasured.
- **Hosting the Rust sim.** The sim tick timing shows that a single-threaded
  C# sim like the choreographer fits the reserve. It says nothing about what
  the real Rust sim costs in Unity, or whether it would run as a native
  plugin or be ported to C#.
- **A real laptop.** Every target number is inferred from a desktop of the
  same GPU architecture, not measured on a 4060 Laptop. Laptop power limits
  and thermals, and AMD or Intel GPUs, go unmeasured.
- **macOS.** No macOS player is built. Metal shader compatibility and Apple
  GPU behavior are untested, though macOS is under 2% of Steam's users.
