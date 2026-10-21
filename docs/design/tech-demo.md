# Playlist Survivors Tech Demo: Technical Design

## Introduction

Playlist Survivors is a planned rhythm game with fantasy action-RPG elements.
It mixes the horde combat of Survivors-like games (Vampire Survivors) with the
beat-locked movement of Crypt of the NecroDancer. This document specifies the
first **tech demo**: a non-interactive, browser-based level that plays itself
as if a perfect player were controlling the hero to the beat of a song.

The demo exists to answer one question: can large-scale combat be
choreographed to music so tightly that it feels like a dance routine? The
answer should be obvious within seconds of watching.

### Goals

- **Deep musical sync.** The hero moves and attacks on the beat. Monsters
  move and animate on the beat. Background and UI pulse with the music.
- **Choreography that follows the song's form.** Different song sections use
  different moves, like verses and choruses in a dance routine.
- **Calm passages still progress the action.** Slow, beatless music gets
  gliding, spinning, slow-motion combat instead of a pause.
- **A dramatic arc.** Heavier attacks land on the loudest accents. Tougher
  enemies and a boss arrive at the climax. The hero powers up over the song.
- **Juice everywhere.** Particles, explosions, slash effects, screenshake,
  hit-stop, flashes and impact sounds are mandatory, not polish.
- **A large horde.** Up to about 500 detailed monsters alive at once.
- **Data-driven levels.** Adding one of the five later songs should need new
  data, not new code paths.

### Fixed constraints

- **No user input** during the level. The only interaction is one click to
  start, which browsers require before audio can play.
- **Desktop browsers only.** The target is current Chrome on a mid-range
  laptop at 1920×1080 and 60 fps. Firefox and Safari are best-effort.
- **Liberated Pixel Cup (LPC) art.** Characters and terrain come from the LPC
  collections, plus a few compatible CC0 effect packs.
- **Legally usable music.** The recording must be public domain or Creative
  Commons. See the [Music Source](#music-source) section below.
- **Asset licence policy.** CC0, CC-BY, OGA-BY and CC-BY-SA art is allowed.
  GPL-only and LGPL-only art is not. When an asset offers several licences,
  use the most permissive one.
- **Agent implementation.** Claude agents implement this design. Every
  important property must be checkable by an automated command or by a
  headless screenshot. A human reviews visuals and feel at defined gates.
- **No interactive developer tools.** There is no in-game scrubber, debug
  overlay or editor. Test hooks for automation are allowed.

### Proposed direction

The demo is a TypeScript web app that uses PixiJS v8 for rendering and the Web
Audio API for playback. The core idea is that **the whole level is planned in
advance and then played back**. Nothing is simulated live.

- An offline Python tool analyzes the recording. A Claude agent then corrects
  the result into a **song map**: a JSON file with beats, bars, sections,
  loudness and accents.
- A human-authored **level script** describes each song section at a high
  level, such as "galop theme, step movement, goblins, zoom out". It refers
  to music by bar number, never by seconds.
- A deterministic **planner** turns the song map and level script into a
  **plan**. The plan is a complete timeline of every hero action, enemy life,
  effect, sound and camera move.
- The planner works backwards from the hits. It decides that a slash on
  bar 12, beat 1 kills five goblins at specific points. It then works out
  where and when each goblin must spawn and how it walks in.
- At runtime, the audio clock gives the current **song time**. The renderer
  draws the plan's state at exactly that time. Picture and sound therefore
  cannot drift apart.

## Related Information

- [AGENTS.md](../../AGENTS.md): repository rules. Every change is committed
  immediately with a Conventional Commits message.
- `~/Documents/lpc/ulpc-generator`: the Universal LPC character generator.
  The hero and humanoid monsters are baked from its layered sheets. Read its
  `ARCHITECTURE.md` and the JSON files under `sheet_definitions` before
  writing the baker.
- `~/Documents/lpc/lpc-revised/Terrain`: OGA-BY forest terrain, trees,
  mushrooms and flowers in four seasons. This is the base of the forest.
- `~/Documents/lpc/source-of-tales`: CC-BY-SA monsters (slime, bat, bee,
  eyeball, beetle, snake), lava and fire tiles, and spell effects such as
  explosions, lightning and the fire lion.
- `~/Documents/lpc/lpc-tile-atlas`: CC-BY-SA lava, ruins and standing stones.
  Check each tile's attribution file, because some parts are GPL-only.
- [Orpheus in the Underworld overture, Musopen recording](https://commons.wikimedia.org/wiki/File:Offenbach_-_Orpheus_in_the_Underworld_-_Overture.ogg):
  the demo's development recording. See [Music Source](#music-source).
- [IMSLP: Orphée aux enfers](https://imslp.org/wiki/Orph%C3%A9e_aux_enfers_(Offenbach,_Jacques)):
  public-domain scores. The beat-map agent uses these to confirm bar counts
  and section boundaries.
- CC0 effect packs used for juice:
  - [LPC explosions](https://opengameart.org/content/lpc-explosions)
  - [Weapon Slash – Effect](https://opengameart.org/content/weapon-slash-effect)
  - [Spell Effects by StarsteelGaming](https://opengameart.org/content/spell-effects-by-starsteelgaming)
  - [Gore Blood Gibs Meat Chunks](https://opengameart.org/content/gore-blood-gibs-meat-chunks)
- Share-alike effect pack:
  [Extended LPC Magic Pack](https://opengameart.org/content/extended-lpc-magic-pack)
  (CC-BY-SA 3.0).
- Sound effect packs:
  - CC0: [Kenney Impact Sounds](https://kenney.nl/assets/impact-sounds),
    [Kenney RPG Audio](https://kenney.nl/assets/rpg-audio),
    [Kenney Interface Sounds](https://kenney.nl/assets/interface-sounds),
    [80 CC0 RPG SFX](https://opengameart.org/content/80-cc0-rpg-sfx) and
    [RPG Sound Pack](https://opengameart.org/content/rpg-sound-pack).
  - CC-BY:
    [Fantasy Sound Effects Library](https://opengameart.org/content/fantasy-sound-effects-library),
    used for the level-up fanfare.
  - Do not use the Sonniss GDC bundle. Its licence forbids redistributing the
    raw files, which a web game does.
- [PixiJS v8 guides](https://pixijs.com/8.x/guides): renderer, `ParticleContainer`,
  filters and render textures.
- [MDN: AudioContext.getOutputTimestamp](https://developer.mozilla.org/en-US/docs/Web/API/AudioContext/getOutputTimestamp):
  the basis of the audio clock.
- [madmom](https://madmom.readthedocs.io) and [librosa](https://librosa.org):
  beat, downbeat and onset analysis libraries.
- [Tiled map editor](https://www.mapeditor.org): used to build hand-made set
  pieces such as the finale shrine.

## Music Source

The first level uses the end of Offenbach's *Orpheus in the Underworld*
overture. It starts with about 30–40 seconds of the slow, lyrical passage and
then plays the complete Infernal Galop (the famous Can-Can). The excerpt is
about 2.5 minutes long.

The slow passage is there on purpose. It shows the calm, flowing style of
combat that the rest of the galop never needs.

The development recording is the Musopen recording on Wikimedia Commons.

- **Format:** 9:23 Ogg Vorbis, 44.1 kHz, mono, about 120 kbps.
- **Excerpt:** the galop starts at about 7:36 and the slow passage at about
  7:00. The beat-map agent chooses the exact crop points, starting on a phrase
  boundary.
- **Tempo:** the galop runs at roughly 180–190 quarter notes per minute in
  2/4 time. Analysis must confirm this.
- **Licence risk:** Commons tags it CC0. However, Musopen sourced it from
  1950s LP transfers and once disabled downloads after a DMCA notice. Treat
  the CC0 status as unverified.
- **Policy:** the recording is acceptable for a private tech demo. Replace it
  before any public release.

The recording must be easy to swap. Level scripts use bars and beats, never
seconds, so a new recording only needs a new song map.

```text
swap recording → re-run analysis → beat-map agent produces new song map
              → planner re-plans → level script unchanged
```

## What the Viewer Sees

This section describes the experience the system must produce. Later sections
explain the mechanics. The level runs through several **zones**, which are
stretches of the map with their own scenery, lighting and enemy types. The
hero travels through them as the song plays.

1. **Click to begin.** A dark screen shows the title and a "Click to begin"
   prompt. The click unlocks audio.
2. **Twilight prelude (slow passage).** The hero glides through a twilight
   forest in slow motion, sword spinning in wide arcs. Bats, slimes and
   floating eyeballs drift in and burst into soft sparkles. The title card
   (piece, composer, performer) fades over the scene.
3. **The curtain rises (galop opening chords).** Time snaps back to full
   speed with a hit-stop on the first chord. A level-up flash grants
   orbiting blades. The camera punches in and out.
4. **Galop theme.** The hero hops on every beat and slashes goblins and
   slimes in sharp, rhythmic arcs. Each kill sprays debris and drops
   experience gems that fly to the hero on the downbeats.
5. **Galop theme, repeat.** Dashes are added. Bees arrive in V-shaped
   formations. A second level-up adds chain lightning on downbeats.
6. **The kick line.** The scenery turns to an autumn mushroom grove. A line of
   skeletons does a synchronized can-can kick to the music. On the phrase's
   last accent, the hero cuts through the whole line. Skeleton archers fire
   volleys that the hero dodges on the beat, with "PERFECT" callouts.
7. **Descent.** The forest darkens to ember red and the ground cracks with
   lava. A third level-up adds a fire trail. The horde grows toward its
   peak.
8. **Lord of the Revel (climax).** A banner announces a huge minotaur boss.
   About 500 monsters are on screen. The boss winds up ground slams, and the
   hero dodges each one on the beat and counter-attacks on the next. The
   heaviest attacks land on the loudest orchestral hits.
9. **Finale.** On the second-to-last chord the boss dies, with a long
   hit-stop and slow motion. On the final chord, a radial blast clears every
   remaining monster. An end card shows kills and combo, then scrolls the
   credits.

## System Overview

The system has five offline or load-time stages and one runtime. Every stage
is deterministic: the same inputs and seed always produce the same output.

- **Audio analysis tool** (Python, offline): turns the recording into a draft
  song map plus diagnostic plots and statistics.
- **Beat-map agent** (a Claude subagent, offline): uses the analysis tool's
  commands to correct the draft into the final song map.
- **Asset baker** (Node, offline): composites LPC layers into sprite atlases
  and generates the credits list.
- **Map generator** (TypeScript, at load): builds the forest from the route,
  zone themes and hand-made set pieces.
- **Planner** (TypeScript, at load): turns the song map, level script and
  content definitions into the plan.
- **Runtime** (TypeScript in the browser): plays the audio, reads song time,
  evaluates the plan at that time, renders it and schedules sound effects.

The central rule is that the rendered frame is a pure function of song time.

```ts
// Called once per animation frame. No state carries between frames.
const s = audioClock.songTimeAtPresentation(frameTimestamp);
const frame = evaluatePlan(plan, s);   // positions, animations, effects
renderer.draw(frame);
```

This rule has several consequences:

- **No drift.** Audio and visuals cannot accumulate a timing error.
- **Free seeking.** Automated tests can render any moment directly. The
  headless capture and benchmark hooks depend on this.
- **Stateless effects.** Particles, screenshake and decals must be computed
  from event times and seeds, not stepped forward each frame. See the
  [Juice](#juice) section below.
- **Simulation lives in the planner.** Anything that needs step-by-step
  simulation, such as crowd movement or camera smoothing, runs there. The
  results are baked into the plan as samples.

## Song Map

The **song map** is a JSON description of one recording's musical structure.
It is the only place where seconds appear. Everything downstream refers to
bars and beats and converts through the song map.

### Contents

A song map contains:

- **Audio reference:** file name, crop start and end in the source file, and
  sample rate. All times in the map are seconds from the start of the cropped
  file.
- **Meter regions:** the time signature (for example 2/4 or 4/4) for each
  range of bars.
- **Beats:** one time per beat, strictly increasing, covering the whole
  excerpt.
- **Downbeats:** which beats start a bar, stored as the index of the first
  beat of bar 1 plus the meter regions.
- **Sections:** labelled bar ranges, each with a **pulse character**:
  - `pulsed` means the music has a clear beat that motion should hit.
  - `free` means the beat is weak or rubato and motion should flow.
- **Anchors:** in `free` sections, a short list of strong musical moments
  (phrase starts, swells, chord changes). The planner synchronizes visible
  events to anchors instead of to every beat.
- **Energy:** a 0–1 value per beat combining loudness and onset density,
  smoothed and normalized over the excerpt.
- **Accents:** the loudest or most striking hits, each with a time, a 0–1
  strength and an optional label such as `tutti-chord` or `cymbal`.

An abbreviated example:

```json
{
  "audio": { "file": "orpheus-galop.ogg", "cropStart": 418.2, "cropEnd": 562.9 },
  "meter": [{ "fromBar": 1, "beatsPerBar": 2, "beatUnit": 4 }],
  "beats": [0.412, 0.951, 1.533, 2.091],
  "firstDownbeatIndex": 0,
  "sections": [
    { "label": "prelude", "bars": [1, 16], "pulse": "free",
      "anchors": [0.412, 6.88, 13.1] },
    { "label": "galop-intro", "bars": [17, 24], "pulse": "pulsed" }
  ],
  "energy": [0.12, 0.14, 0.13, 0.15],
  "accents": [{ "time": 36.84, "strength": 1.0, "label": "tutti-chord" }]
}
```

### Musical position

The planner and runtime convert between seconds and a fractional **beat
position**: beat index plus the fraction of the way to the next beat. The
conversion is piecewise-linear interpolation over the beat list. This handles
tempo changes and rubato without any notion of a global tempo.

```ts
beatPosition(songMap, 1.24);            // → 1.53 (halfway-ish to beat 3)
timeOfBeat(songMap, 2.5);               // → 1.812
timeOfBar(songMap, { bar: 17, beat: 0 }); // → 34.07
```

Before the first beat and after the last beat, the conversion extrapolates
from the nearest two beats.

## Building the Song Map

The beat grid has to be right. A beat that is off by 30 ms is visible when 400
monsters stomp on it. Orchestral recordings speed up and slow down, and
automatic beat trackers often lock onto the wrong level (half or double tempo)
or drift during rubato. So a Claude subagent, the **beat-map agent**, builds
the song map using a purpose-built analysis tool.

### Analysis tool commands

The tool is a Python command-line program. It uses madmom for beats and
downbeats and librosa for onsets, loudness and spectrograms. Every command
writes machine-readable output, and the agent judges quality from statistics
and images because it cannot listen.

- **`analyze`:** runs several beat trackers and writes a draft song map. The
  trackers are madmom's DBN beat and downbeat trackers and librosa's
  `beat_track`. It also writes the energy curve and an accent candidate list.
- **`report`:** writes diagnostics for the current song map:
  - Spectrogram strips (8 seconds each) with beat and downbeat lines drawn
    over the onset-strength curve, as PNG images.
  - A plot of the gap between beats across the whole excerpt.
  - A table of beats where the trackers disagree by more than 40 ms.
  - Per-section statistics, described in the
    [Acceptance thresholds](#acceptance-thresholds) section below.
- **`edit`:** applies named corrections from a JSON list, so every change is
  reviewable and repeatable.
- **`check`:** validates the song map against the thresholds and exits
  non-zero on failure.
- **`click`:** renders a WAV of the excerpt with a click on each beat (accented
  on downbeats), for the human gate.
- **`crop`:** cuts the excerpt and transcodes it to Ogg Opus and AAC for the
  browser.

The `edit` operations are:

```json
[
  { "op": "shift", "beats": [210, 260], "byMs": -18 },
  { "op": "insertBeat", "after": 341 },
  { "op": "deleteBeat", "index": 402 },
  { "op": "setFirstDownbeat", "index": 1 },
  { "op": "respace", "from": 120, "to": 136 },
  { "op": "setSection", "label": "kick-line", "bars": [57, 72], "pulse": "pulsed" },
  { "op": "addAccent", "time": 88.31, "strength": 0.9, "label": "cymbal" }
]
```

The `respace` operation re-spaces beats evenly between two trusted beats. It
is the main fix for drift in rubato passages.

### Beat-map agent workflow

The agent's job is to produce a song map that passes `check` and matches the
score's structure.

1. Run `crop` and `analyze`.
2. Identify the form from the IMSLP score and general musical knowledge. For
   example: "slow passage of N bars, galop intro, theme A twice, theme B,
   return of A, coda." Write the expected bar count of each section.
3. Run `report`. Find tracker disagreements, octave errors (beats at half or
   double speed) and downbeat phase errors.
4. Apply `edit` operations, then repeat `report` until the numbers settle.
5. Confirm that section boundaries fall on downbeats and that bar counts match
   the score.
6. Curate accents. Keep the strongest 20–40 and label the obvious tutti hits.
7. Run `check` and `click`. Commit the song map, the edit list and the report
   summary.

For `free` sections, the beat grid only needs to be roughly right. The agent
should spend its effort on anchors instead: 1 to 3 per phrase, at clearly
audible onsets.

### Acceptance thresholds

`check` enforces these per section:

- **Onset alignment (`pulsed` sections only):** at least 90% of beats are
  within 25 ms of a peak in the onset-strength curve, where a peak exists
  within 80 ms.
- **Smoothness (`pulsed` sections only):** each gap between consecutive beats
  differs from its neighbour by at most 8%, except at a marked tempo change.
- **Structure:** sections cover the excerpt without gaps or overlaps and start
  on downbeats.
- **Anchors:** every `free` section has at least one anchor per 8 seconds.
  Each anchor is within 30 ms of an onset peak.
- **Accents:** every accent is within 20 ms of an onset peak.

## Song Time and World Time

The demo needs two clocks. Hit-stop (a brief freeze on a big impact) and slow
motion both change how fast the game world moves, but the music never pauses
or slows down.

- **Song time** (`s`) is seconds into the audio excerpt. It always advances
  at real-time speed.
- **World time** (`w`) is the clock that game motion runs on: walking,
  animation, particles and projectiles.
- The **time warp** is a function `w = W(s)` that maps song time to world
  time. It never decreases.

The warp is the integral of a playback rate that the planner chooses.

- **Rate 1** is normal play.
- **Rate 0.5** is half-speed slow motion, for example in the prelude.
- **Rate 0** is a hit-stop: world time freezes for 60–150 ms after a big
  impact.
- **Ramps:** rate changes between sections blend smoothly over one beat.

Hit-stops never need "catch-up" time. After a 100 ms hit-stop, world time
simply stays 100 ms behind song time for the rest of the song. This is
harmless because of the synchronization rule below.

### Synchronization rule

Every event that must line up with the music is placed by its song time.
Examples are a sword impact, a monster's stomp and a gem pickup. The planner
converts that song time to world time with `W` and schedules the event there.
Because of this, music-synced events land exactly on their beats however much
time warping happened earlier.

```ts
const sHit = timeOfBar(songMap, { bar: 33, beat: 1 }); // musical time
const wHit = warp.toWorld(sHit);                      // planned world time
hero.schedule({ move: "cleave", impactAt: wHit });
```

The warp has these constraints:

- **Hit-stops start at an impact.** Every hit-stop starts exactly at a planned
  impact.
- **Clearance.** A hit-stop ends at least 50 ms (song time) before the next
  synchronized event.
- **Bounded rate.** The rate stays between 0.25 and 1.0 outside hit-stops.

### World beat grid

The **world beat grid** is the song's beat list mapped through the warp:
`wBeat[i] = W(sBeat[i])`. Rhythmic animation in world time, such as a monster
taking one stride per beat, uses this grid. As a result:

- **Slow motion keeps the rhythm.** At rate 0.5 a beat spans half as much
  world time, so a one-stride-per-beat walk still steps on every beat but
  covers less ground per second. The world visibly slows while still dancing.
- **Hit-stop freezes rhythm too.** During a hit-stop the world beat grid
  stops, so walk cycles freeze. This is the desired look.
- **Song-time effects keep moving.** Effects that should ignore hit-stop
  declare that they run on song time instead. Examples are HUD pulses,
  background sway and screenshake.

## Level Script

The **level script** is a human-written YAML file, one per song. It describes
the level at the level of song sections. The planner fills in every beat. The
planner validates the script against a schema at load and reports each error
with its line number.

### Script contents

The script has four parts:

- **Header:** song id, random seed and title card text.
- **Zones:** the ordered list of forest zones, each referring to a zone theme.
  See the [Map Generation](#map-generation) section below.
- **Sections:** one entry per musical section, keyed by bar range and
  matching the song map's sections.
- **Events:** level-ups, set pieces, boss entrances and other one-off moments,
  each anchored to a bar and beat.

Each section entry sets:

- **`moveset`:** which hero choreography to use. See
  [Movesets](#movesets) below.
- **`zone`:** which zone the hero should be in by the section's start.
- **`timeScale`:** the base world-time rate (0.25–1.0).
- **`population`:** the target number of living enemies at the start and end
  of the section.
- **`roster`:** the enemy types that spawn and their weights.
- **`formations`:** optional formation waves. See [Formations](#formations)
  below.
- **`camera`:** zoom and lead distance.
- **`sfxPalette`:** which sound-effect set to use.
- **`intensity`:** a 0–1 multiplier on effect strength, multiplied with the
  song map's energy.

An abbreviated example (the bar numbers are illustrative; the song map
determines the real ones):

```yaml
song: orpheus-galop
seed: 1858
zones: [twilight-glade, galop-woods, mushroom-grove, ember-hollow, infernal-shrine]
sections:
  - bars: 1-16          # slow prelude
    moveset: spinning-glide
    zone: twilight-glade
    timeScale: 0.5
    population: [30, 60]
    roster: { bat: 3, slime: 2, eyeball: 1 }
    camera: { zoom: 1.2 }
    sfxPalette: soft-chimes
  - bars: 17-24         # galop opening chords
    moveset: herald-cleaves
    timeScale: 1.0
    population: [60, 120]
    roster: { slime: 2, goblin: 1 }
    camera: { zoom: 1.0, punchOnAccents: true }
    sfxPalette: crisp-steel
events:
  - at: { bar: 17, beat: 1 }
    levelUp: orbiting-blades
  - at: { bar: 57, beat: 1 }
    setPiece: skeleton-kick-line
```

## Content Definitions

Content is data shared by all levels: the hero's moves, movesets, enemy types,
weapons, zone themes, effect recipes and sound palettes. A level script refers
to content by id. Content files are YAML and are schema-validated in the same
way as the level script.

### Moves

A **move** is one hero action: an animation plus what it does to the world.
Each move defines:

- **Animation:** which baked animation plays, at what frame rate.
- **Impact frame:** the frame on which the hit lands. The planner starts the
  animation early so that this frame lands exactly on the beat. Wind-up
  happens before the beat and contact happens on it.
- **Hit shape:** the area the move damages: an arc, a circle, a line or a
  capsule along a dash path.
- **Weight:** light, medium or heavy. Weight scales the kill count, effects,
  hit-stop and sound.
- **Locomotion:** how far the hero travels during the move.

The initial move set:

- **slash, backslash and halfslash:** a 120° arc of radius 56 px. Light.
- **thrust:** a line 72 px long. Light.
- **spin:** a full circle of radius 64 px, made by cycling the slash animation
  through all four facing directions. Medium.
- **cleave:** an overhead heavy arc of radius 80 px with a hit-stop. Heavy.
- **dash-strike:** a lunge of 96–160 px whose capsule damages everything along
  the path. Medium.
- **nova:** a spellcast that hits a circle of radius 140 px. Heavy.
- **dodge:** a short dash out of a threat's path. It deals no damage and
  triggers the near-miss effect.

For example, the cleave move:

```yaml
id: cleave
animation: { name: slash-oversize, fps: 14 }
impactFrame: 3
hitShape: { kind: arc, radius: 80, degrees: 160 }
weight: heavy
hitStopMs: 110
```

### Movesets

A **moveset** is a section's choreography style. It has three parts:

- **Locomotion mode:**
  - `step` makes one hop per beat (or per half-bar), with a jump arc and a
    dust puff on landing.
  - `dash` makes quick lunges exactly on the beat, then settles.
  - `glide` follows a smooth curve whose speed swells with the energy curve.
- **Path shape:** the pattern traced around the route. For example
  `forward`, `zigzag`, `circle` or `figure-eight`.
- **Attack patterns:** which moves fire on which subdivisions of each bar.
  Each pattern is written as a grid string (one character per grid slot),
  plus rules for accents.

```yaml
id: galop-steps
locomotion: { mode: step, every: beat }
path: zigzag
grid: eighth             # in 2/4 a bar has 4 eighth-note slots
attacks:
  - pattern: "x.x."      # slash on each beat
    moves: [slash, backslash]   # alternate
  - onAccent: { minStrength: 0.7, move: cleave }
  - onSectionEnd: dash-strike
```

In `free` sections, grids are not used. The moveset instead lists continuous
moves (such as `spin`) that repeat at a set interval. It also lists the moves
to fire on anchors.

```yaml
id: spinning-glide
locomotion: { mode: glide, speed: 40 }
path: figure-eight
continuous: { move: spin, everyBeats: 2 }
onAnchor: { move: nova, minStrength: 0.5 }
```

### Enemy types

Each enemy type defines:

- **Sprite:** baked atlas animations for walk, attack, hurt and death.
- **Movement group:** which subdivision drives its motion and animation:
  - beat
  - eighth note
  - downbeat
  - half-bar

  See [Rhythm in Motion](#rhythm-in-motion) below.
- **Base speed and lifetime range:** used by the planner.
- **Size class:** regular, elite or boss. Elites and the boss take several
  planned hits before dying.
- **Attack behavior:** for example `none`, `archer` or `slam`.
- **Death recipe and colors:** which death effects to play and which debris
  colors to use (green goo for slimes, bone chips for skeletons).

The Can-Can roster:

| Enemy | Source art | Movement group | Notes |
|---|---|---|---|
| slime | source-of-tales | beat (hop) | squash and stretch on landing |
| bat | source-of-tales | eighth (flap) | swooping paths |
| eyeball | source-of-tales | half-bar (drift) | prelude only |
| bee | source-of-tales | eighth | V-formations |
| goblin | ULPC goblin head | beat (stride) | short sword |
| orc | ULPC orc head | downbeat (stomp) | elite variant: 3 hits |
| skeleton | ULPC skeleton body | beat | kick line, ember recolor |
| skeleton archer | ULPC skeleton body and bow | downbeat | fires volleys |
| minotaur boss | ULPC minotaur head, muscular body | half-bar | drawn at 2× scale |

### Weapons gained by levelling up

Level-ups add automatic weapons. Each weapon fires on its own subdivision, so
the arsenal builds up like layers of an orchestra.

- **Orbiting blades:** three blades orbiting at radius 72 px. One orbit takes
  exactly one bar. Each blade kills on eighth-note slots.
- **Chain lightning:** fires on downbeats. It jumps between 3–6 enemies,
  with jumps staggered by sixteenth notes.
- **Fire trail:** the hero's dash path burns for two beats. Enemies the
  planner routes through it die on the following beats.
- **Finale nova:** a one-off screen-clearing blast on the final chord.

Automatic weapon kills are planned in the same way as hero hits. Each firing
is a hit with a shape and a synchronized time.

## Planner

The planner is a pure TypeScript function. It runs in a web worker at load
and as a Node command for tests.

```ts
function plan(input: {
  songMap: SongMap;
  level: LevelScript;
  content: ContentLibrary;
  map: MapLayout;      // route, corridor and zones, from the map generator
  seed: number;
}): Plan;
```

Requirements:

- **Deterministic.** Use a seeded random generator, a stable sort and no
  `Date` or `Math.random`. The same inputs produce byte-identical output.
- **Fast.** Under 2 seconds for a 3-minute song on the reference laptop.
- **Cached.** Results are cached in IndexedDB keyed by a hash of the inputs.

The planner runs in a fixed sequence of stages. Each stage reads only the
outputs of earlier stages. The order matters:

- **Moves before warp.** Moves are chosen in song time before the time warp
  exists, because hit-stops depend on which moves are heavy.
- **Camera before kills.** The camera is planned before kills, because spawn
  positions depend on what the camera can see.

### Stage 1: Resolve musical time

- Convert every bar reference in the level script into song time through
  the song map.
- Build the list of **slots** (synchronized moments) for each section. In
  `pulsed` sections these are the moveset's grid positions. In `free` sections
  they are anchors plus continuous-move times.
- Attach each slot's energy and nearest accent strength.

### Stage 2: Choose actions and threats

This stage decides what happens on each slot, still in song time.

- **Pick moves:** for each slot, pick the move from the moveset's pattern.
  Accents override pattern moves when they match an accent rule.
- **Fire weapons:** add automatic weapon firings on their subdivisions, from
  each weapon's level-up onward.
- **Schedule threats:** add threats from the level script's events and the
  section roster. Enemies attack, but the hero never takes damage, so every
  threat is planned together with the hero's escape:
  - **Archer volley:** an archer releases on a beat, aimed at where the hero
    stands. On the beat before the arrow arrives, the hero dodges. The arrow
    passes 8–24 px from the hero's hurtbox and a "PERFECT" callout appears.
  - **Boss slam:** a glowing ground marker grows over two beats. The hero
    dodges on the slam beat, so the slam lands on empty ground with a
    shockwave. The hero counter-attacks with a cleave on the next beat.
- **Dodges replace attacks:** a dodge takes over its slot's planned attack.

### Stage 3: Build the time warp

- Start from each section's `timeScale`, ramping between sections over one
  beat.
- Add hit-stops. Every heavy move and every accent above strength 0.8 gets a
  hit-stop whose length scales with strength (60–150 ms), as long as the
  clearance rule allows it.
- Produce `W`, its inverse (defined except inside hit-stops) and the world
  beat grid.

### Stage 4: Plan hero motion

The hero's position over time comes from two parts:

- **Route progress:** the hero moves along the map route. The distance covered
  per section is chosen so that each section starts in its assigned zone. The
  map generator uses the same distances to lay out the route (see
  [Map Generation](#map-generation) below).
- **Local choreography:** the moveset's path shape adds an offset around the
  route position, staying within the combat corridor.

On top of that path:

- **Locomotion timing:** steps, dashes and dodges are timed to the world beat
  grid.
- **Animation timing:** each move's animation starts early so its impact
  frame lands on the slot.
- **Directions:** facing and attack directions alternate in a pattern (for
  example left, right, forward, forward) so the routine reads as a dance.
  Dodges move away from the incoming threat.
- **Output:** the hero's position, facing and current animation, baked at 60
  samples per world-second.

### Stage 5: Plan the camera

- The camera targets the hero's position plus a lead in the route direction.
- Zoom follows the level script's keyframes, with "punch" zooms (2–4% in,
  then out) on accents when enabled.
- A critically damped spring smooths the camera in world time. The result is
  baked at 60 samples per world-second.
- Screenshake is not baked here. It is added at evaluation from impact events
  (see [Juice](#juice) below).

### Stage 6: Assign kills (planning backwards)

This stage creates every enemy from its death. For each hit (a hero move or
weapon firing):

1. Choose a kill count. It scales with the section's population target, the
   slot's energy and the move's weight. For example, a heavy cleave at energy
   0.9 might take 9 kills and a light slash at energy 0.4 might take 2.
2. Choose kill positions inside the hit shape at the hit time. Use Poisson
   disk sampling so bodies don't overlap.
3. Create an enemy for each position, with a **fate**: death time, death
   position and the hit that kills it.
4. Elites and the boss get several fates, one per planned hit. Only the last
   fate kills.

Threat enemies get an extra constraint instead of, or as well as, a fate. An
archer must stand at its firing position on its release beat; the boss must
stand at its slam position on each slam.

Population is controlled through lifetimes. The number of living enemies is
roughly the kill rate multiplied by the average lifetime. So the planner sets
each enemy's lifetime to `targetPopulation / killRate` for the time of its
death, clamped to 3–30 seconds. In calm sections kills are rare, so enemies
live long and loiter near the hero. At the climax, kills come fast and the
crowd is dense.

```text
kill rate 20/s, target population 480 → lifetime ≈ 24 s
kill rate  2/s, target population  50 → lifetime ≈ 25 s (loiter)
kill rate 12/s, target population 120 → lifetime ≈ 10 s
```

Each enemy's spawn time is its death time minus its lifetime. Its spawn
position is just outside the camera's view at that moment, on the side it
will approach from.

### Stage 7: Simulate the crowd

A fixed-step crowd simulation (60 steps per world-second) moves every enemy
from its spawn point to its fate. It uses a spatial hash. Forces on each
enemy:

- **Arrival:** steer toward the next fate or threat position. The desired
  speed is the remaining distance divided by the remaining time, clamped to
  the type's maximum speed.
- **Separation:** push away from neighbors closer than 0.8 body widths.
- **Personal space:** stay at least 40 px from the hero, except during the
  final beat before the enemy's own fate.
- **Hit keep-out:** stay out of any hit shape that fires in the next beat
  unless that hit is fated to kill this enemy. A slash must never visibly pass
  through a monster that doesn't die.
- **Rhythmic speed:** maximum speed is multiplied by the enemy group's pulse.
  See [Rhythm in Motion](#rhythm-in-motion) below.

In the last 0.25 seconds before a fate, the enemy blends exactly onto its fate
position. Formation enemies are not simulated; they follow their formation
slots exactly. The output is position samples at 30 per world-second, which
the runtime interpolates with Catmull-Rom splines.

After simulation, a check measures how far each enemy was from its fate just
before the final blend. If it was more than 24 px away, the planner extends
that enemy's lifetime and re-simulates it.

### Stage 8: Emit effects, sounds and HUD data

For every planned event, the planner looks up the matching effect recipe and
sound palette entry and emits:

- **Effect events:** recipe id, world or song time, position, direction,
  intensity and seed.
- **Sound events:** sample id, song time, gain, stereo pan from screen
  position, and pitch. See [Sound](#sound) below.
- **Experience gems:** each death drops gems that fly to the hero, arriving
  on the next downbeat. Gem values are scaled so that the XP bar fills
  exactly at each level-up.
- **HUD counters:** kills, combo and XP stored as sorted event times, so the
  runtime can compute any count at time `s` with a binary search.

### Planner output checks

The planner runs these checks on its own output. They also run in the test
suite. Any failure is an error naming the time and entity involved.

- **Impacts on time:** every hit's impact is within 1 ms of its slot's song
  time, after mapping back through the warp.
- **Deaths covered:** every enemy dies inside the shape of the hit that
  kills it.
- **Off-screen spawns:** every enemy spawns outside the camera view plus a
  16 px margin, unless its formation uses a visible summoning circle.
- **No pass-through:** no regular enemy is inside a hit shape at the hit time
  unless that hit kills it.
- **Spacing:** no two enemies' centers are closer than 0.5 body widths
  (sampled 10 times per second), except formation members and enemies in their
  death frames.
- **Near misses stay near:** threats never touch the hero's hurtbox and pass
  within 24 px.
- **Population:** the living population stays within ±15% of the target curve
  and never exceeds 520.
- **Clean ending:** every enemy has died by the final chord.
- **Hit-stop clearance:** no hit-stop overlaps a synchronized event.
- **Sound budget:** no sixteenth-note slot exceeds the voice budget.
- **Determinism:** two runs with the same inputs produce the same plan hash.

## Plan Format and Evaluation

The **plan** is the planner's output: an immutable data structure designed so
that evaluating any time is cheap.

- **Time warp:** a breakpoint table for `W` and the world beat grid.
- **Hero track:** position, facing and animation samples in world time.
- **Enemy table:** for each enemy, its type, spawn and death times, the
  offset of its samples in a shared `Float32Array`, and its fates and hurt
  events.
- **Event lists:** effect, sound, gem, banner and level-up events, each
  sorted by start time.
- **Interval index:** for each event list, a bucket per 0.5 seconds listing
  the events alive in that bucket. This lets the runtime find active events
  quickly.
- **Camera track:** position and zoom samples.
- **HUD series:** sorted event times for kills, combo breaks and XP.

`evaluatePlan(plan, s)` computes the frame:

```ts
const w = plan.warp.toWorld(s);
const alive = plan.enemies.aliveAt(w);   // via interval index
for (const e of alive) {
  const p = sampleSpline(plan.enemySamples, e, w);
  const anim = enemyAnimation(e, w, plan.worldBeats); // beat-locked frame
  out.sprites.push({ p, anim, flash: flashAt(e, w), knockback: kbAt(e, w) });
}
out.effects = activeEffects(plan, s, w);  // stateless, see Juice
```

Knockback and hit flash are evaluated from hurt events as short decaying
offsets on top of the baked path. They never change the path, so the crowd
simulation does not need to account for them.

Evaluation must take under 4 ms at the busiest moment of the song.

## Runtime

The runtime loads everything, starts playback on the first click, then draws
`evaluatePlan(plan, s)` every animation frame.

### Loading

1. Load atlases, the song map, the level script and content.
2. Fetch and decode the audio into an `AudioBuffer`.
3. Generate the map and run the planner in a worker, or load the plan from
   the cache.
4. Show the title screen with "Click to begin".

The loading screen shows a progress bar. If the planner throws an error, show
the error on screen with the time and entity it names. Never start playback
with an invalid plan.

### Audio clock

Song time must match what the listener hears at the moment a frame appears.

- **Playback:** the decoded excerpt plays through one `AudioBufferSourceNode`,
  started at a known context time `t0`.
- **Each frame:** the runtime calls `getOutputTimestamp()`. That returns a
  context time paired with a `performance.now()` time for the audio currently
  leaving the speakers.
- **Extrapolation:** the runtime extrapolates from that pair to the frame's
  expected presentation time, which is the requestAnimationFrame timestamp
  plus one frame.
- **Smoothing:** the estimate is smoothed so it never runs backwards. It
  corrects by at most 2 ms per frame, unless the error exceeds 50 ms, in which
  case it jumps.

```ts
const { contextTime, performanceTime } = ctx.getOutputTimestamp();
const presentAt = rafTimestamp + frameDuration + config.avOffsetMs;
const raw = contextTime + (presentAt - performanceTime) / 1000 - t0;
songTime = smoother.update(raw); // monotonic, slewed
```

`config.avOffsetMs` defaults to 0. It can be set with a URL parameter for
hardware with unusual latency, such as Bluetooth headphones.

If the tab is hidden, the browser stops animation frames, but the audio keeps
playing. When the tab is visible again, the next frame simply evaluates the
current song time. When the audio ends, the end card stays up with the
credits.

### Rendering

The renderer is PixiJS v8 on WebGL2. The world is pixel art, so it is drawn
at native pixel size into an offscreen texture. That texture is then scaled
up to the window.

- **Base view:** 960×540 world pixels at zoom 1.0, shown at 2× on a 1080p
  screen.
- **Zoom range:** 0.7× (the climax, showing about 1370×770 world pixels) to
  1.4× (the prelude).
- **Smooth scaling:** non-integer zoom uses a sharp-bilinear upscale shader.
  This avoids shimmering pixels while still allowing smooth zooms.
- **Sub-pixel camera:** sprites snap to whole world pixels. The camera's
  fractional offset is applied in the upscale pass, so camera motion is
  smooth.

Layers, drawn back to front:

1. **Ground:** pre-rendered chunk textures of 512×512 world pixels.
2. **Ground decals:** blood, goo and scorch marks, plus boss slam markers.
3. **Shadows:** blob shadows under every character.
4. **Y-sorted world:** the hero, enemies and tall props such as trees, sorted
   by their foot position.
5. **Projectiles and slash arcs.**
6. **Additive effects:** sparks, glows and lightning.
7. **Lighting:** an ambient color multiplied over the scene, plus additive
   point lights from fires, attacks and lightning. Lights are drawn as radial
   sprites into a light texture.
8. **Post-processing:**
   - bloom
   - shockwave distortion
   - brief chromatic aberration on heavy hits
   - vignette pulse
   - a color grade per zone
9. **HUD:** drawn at screen resolution with a pixel font.

Performance rules:

- **Few textures:** pack all character frames into at most four 4096×4096
  atlases, so sprites batch into a few draw calls.
- **Baked hit flash:** bake a white-silhouette version of every frame, so a
  hit flash swaps the texture instead of using a per-sprite shader.
- **Cheap particles:** particles use `ParticleContainer`, with at most 6,000
  live at once.
- **Budgets:** at the busiest moment, CPU frame time under 8 ms and GPU frame
  time under 10 ms on the reference laptop.

## Rhythm in Motion

Every moving thing gets its rhythm from a few shared pulse functions of
musical position. All of them peak on the beat and decay before the next.

```ts
// phase: fraction of the way through the current beat, bar, etc. (0..1)
const pulse  = (phase: number) => Math.exp(-6 * phase);       // sharp hit
const swell  = (phase: number) => 0.5 - 0.5 * Math.cos(2 * Math.PI * phase);
const amount = (base: number, energy: number) => base * (0.4 + 0.6 * energy);
```

Enemies move in groups:

- **Speed:** each group's maximum speed is multiplied by
  `0.6 + 0.8 * pulse(phase)`, using the phase of its own subdivision. This
  makes the group surge on its subdivision.
- **Walk cycles:** each walk cycle is timed to the group's subdivision. A
  goblin takes one stride per beat, so its animation frame is computed from
  the world beat grid, not from elapsed time.
- **Group subdivisions:** slimes hop on beats, bats flap on eighth notes,
  orcs stomp on downbeats, and the boss lumbers on half-bars. The horde reads
  as an ensemble with several rhythms at once.
- **Free sections:** `free` sections replace `pulse` with `swell` over a
  two-beat period. Motion breathes instead of ticking.

The environment pulses too:

- **Vegetation:** trees and flowers sway with the `swell` of the bar.
- **Lighting:** light intensity rises 5–15% on each beat, scaled by energy.
- **Vignette:** the vignette tightens on downbeats.
- **HUD:** elements scale 3–6% on each beat. The beat-track heart beats.
- **Free sections:** the same effects become slow swells.

All environment and HUD pulses use song time, so they keep going during
hit-stops.

## Juice

Juice is mandatory. Every hit, death, level-up and threat triggers several
layered effects. **Effect recipes** are content data that describe the layers
for each event type. The amount of each layer scales with **intensity**: the
section's `intensity` × the song map's energy × the move's weight × the
accent strength (where present).

### Effect catalogue

Hits and deaths:

- **Hit flash:** the struck sprite shows its white silhouette for 2 frames.
- **Hit-stop:** world time freezes, as planned in the time warp.
- **Knockback:** a decaying offset away from the hero, with squash and
  stretch.
- **Slash arcs:** pixel-art arc sprites matching the move's hit shape, plus a
  bright additive core.
- **Sparks and debris:** bursts colored by enemy type. Examples: goo droplets
  for slimes, bone chips for skeletons, blood and gibs for humanoids.
- **Death:** a dissolve or burst into gibs, plus a small soul wisp that rises
  and fades.
- **Ground decals:** splats and scorch marks that fade over 20 seconds, with
  at most 400 at once.
- **Experience gems:** gems burst out on death, then fly to the hero in an
  arc on the next downbeat, with a sparkle on pickup.

Camera and screen:

- **Screenshake:** a trauma model. Each impact adds trauma, and the shake
  offset is trauma² times smooth noise.
- **Zoom punch:** on accents.
- **Shockwave distortion:** on heavy hits, boss slams and the finale nova.
- **Chromatic aberration:** briefly on hits with intensity above 0.8.
- **Flash frames:** a full-screen white flash at 20–40% opacity on the
  biggest accents.

Hero and weapons:

- **Dust puffs:** on every step landing.
- **Afterimages:** fading copies of the hero along dash paths.
- **Weapon effects:** orbiting blade trails, chain lightning bolts and fire
  trail flames.
- **Near-miss flourish:** a 150 ms slow-motion dip, a "PERFECT" callout and a
  ring of sparks.

Moments:

- **Level-up:** a radial light burst, a ring shockwave and a fanfare. Enemies
  near the hero are pushed back.
- **Boss entrance:** the screen dims, a banner slides in and the boss lands
  with a big shake.
- **Finale nova:** a screen-wide expanding ring kills every remaining enemy.
  It comes with a long hit-stop, a white flash, then slow-motion embers.

Ambience:

- **Fireflies** in the twilight glade.
- **Falling leaves** in the mushroom grove.
- **Rising embers and heat haze** in the infernal zones.

### Stateless effects

Effects never keep state between frames. Each active effect is computed from
its start time, seed and recipe. This keeps the frame a pure function of song
time. For example, a particle burst:

```ts
// Every particle's state is a closed-form function of its age.
function particle(burst: Burst, i: number, w: number) {
  const r = rng(burst.seed, i);
  const age = w - burst.start;
  const v = vecFromAngle(burst.dir + r.spread(), r.range(60, 180));
  return { x: burst.x + v.x * age, y: burst.y + v.y * age + 0.5 * GRAVITY * age * age,
           alpha: 1 - age / burst.life };
}
```

Each recipe layer declares whether it runs on world time (frozen by hit-stop)
or song time (keeps moving). Sparks and debris use world time, so they hang
in the air during a hit-stop. Screenshake, flashes and HUD effects use song
time.

### Effect art

- **Local packs:** slash arcs, explosions and spells come from the LPC and
  CC0 packs listed in [Related Information](#related-information).
- **Procedural shapes:** shockwave rings, glows and light blobs are
  generated as simple procedural textures.
- **Pixel style:** all effect sprites follow the pixel-art scale of the
  world. Only bloom, lights and distortion are smooth, modern layers on top.

## Sound

Sound effects must sit inside the music, not on top of it. Every sound is
scheduled with the Web Audio clock for the exact song time of its event,
which the plan already knows.

### Scheduling

- **Scheduler loop:** a loop runs every 25 ms. It schedules every sound event
  that falls within the next 150 ms of song time.
- **Start time:** each sound starts at context time `t0 + s` with
  `AudioBufferSourceNode.start()`.
- **Never late:** events already in the past (for example after the tab was
  hidden) are skipped, not played late.

### Palettes and budget

A **sound palette** is a named set of samples for each event type. Each
section's palette sets the tone. The demo uses three palettes:

- `soft-chimes` for the prelude
- `crisp-steel` for the galop
- `heavy-infernal` for the climax

The voice budget rules:

- **Per-slot budget:** at most 4 sound voices start in any sixteenth-note
  slot. Priority order: heavy hero hits, boss, near misses, weapons, kills.
- **Merged kills:** many kills in one slot become one layered "multi-kill"
  sound. Its gain grows with the logarithm of the kill count.
- **Variation:** pitch varies by up to ±1 semitone. In palettes that set a
  key, pitched sounds (bells, chimes, magic) are transposed to notes of the
  section's scale.
- **Normalization:** every sample is loudness-normalized at bake time.

### Mixing

- **Music bus:** the music gain dips 3–4 dB for 120 ms under heavy impacts.
  This is scheduled automation, not a sidechain.
- **Effects bus:** sound effects go through a bus compressor. Both buses go
  through a final limiter.
- **Default level:** sound effects sit about 6 dB below the music's peak
  loudness.

## HUD and Titles

The HUD tells viewers this is a rhythm game and a Survivors game at once.
Every element pulses with the music in song time.

- **Beat track:** a NecroDancer-style track at the bottom of the screen.
  - Markers for upcoming hero actions slide toward a beating heart in the
    center.
  - Heavy hits show as larger markers.
  - In `free` sections the markers become a flowing ribbon.
- **Combo counter:** counts consecutive on-beat actions. "PERFECT" callouts
  appear on accents and near misses.
- **XP bar:** fills from gem pickups and reaches full exactly at each
  level-up.
- **Kill counter:** total kills so far.
- **Song progress:** elapsed time and a thin progress line marked with
  section boundaries.
- **Weapon icons:** each weapon's icon flashes when that weapon fires.
- **Health bar:** the hero's health bar is always full.
- **Title card:** piece, composer and performer, shown during the prelude.
- **Section banners:** stylized banners such as "THE KICK LINE" and "LORD OF
  THE REVEL" at key transitions.
- **End card:** totals for kills, highest combo and "PERFECT" count, then a
  scrolling credits roll generated from the credits list.

Floating damage numbers are not shown.

## Assets and Licensing

All sprite art is baked offline into atlases. The runtime never composites
layers.

### Baking characters

The Universal LPC generator has no command-line export, so the baker
reimplements the parts it needs. It reads **character recipes**: small JSON
files listing body, head, clothing and weapon layers with color choices.

```json
{
  "id": "goblin",
  "bodyType": "male",
  "layers": [
    { "item": "body/body", "color": "green" },
    { "item": "head/heads/goblin/adult", "color": "green" },
    { "item": "legs/pants", "color": "brown" },
    { "item": "weapons/sword/dagger", "variant": "steel" }
  ],
  "animations": ["walk", "slash", "hurt", "idle"]
}
```

The baker:

- **Layers:** reads each item's definition from `sheet_definitions` and
  sorts layers by `zPos`.
- **Recoloring:** applies palette recoloring using the generator's palette
  definitions.
- **Oversized frames:** handles weapon animations with 128 px and 192 px
  frames, centering the 64 px body inside the larger frame.
- **Animation subset:** bakes only the listed animations, in the 4
  directions (up, left, down, right).
- **Color variants:** produces variants such as the ember recolor of
  skeletons and orcs for the infernal zones.
- **Hit flash frames:** produces the white-silhouette frame for each frame.
- **Packing:** packs everything into atlases with JSON metadata giving frame
  rectangles, anchors and impact frames.

Two LPC gaps need custom work:

- **Kick animation:** no LPC kick or dance animation exists, and the skeleton
  body has no jump or run animation. The baker builds a 3-frame kick from the
  skeleton's walk frames: stand, knee raised, leg extended. It does this by
  moving and redrawing the leg pixels with a scripted pixel edit. A human
  approves the result at a review gate. If the frames are not approved, the
  kick line instead alternates walk frames with a beat-synced bob plus squash
  and stretch.
- **Sword slash trails:** none exist in LPC. Slash arcs come from the CC0
  Weapon Slash pack plus procedurally generated additive arcs.

### Licence policy and credits

- **Allowed licences:** CC0, CC-BY, OGA-BY and CC-BY-SA.
- **Disallowed licences:** GPL-only and LGPL-only art is rejected. The baker
  fails the build if a recipe references it.
- **Least restrictive option:** when an asset lists several licences, the
  credits record the least restrictive one.
- **Share-alike output:** baked or modified art derived from CC-BY-SA sources
  is itself CC-BY-SA. The code keeps the repository's own licence.
- **Credits file:** the baker writes a machine-readable credits file listing
  every source file used with its authors, licence and URL. It draws on the
  generator's `CREDITS.csv`, each pack's credits file and the effect and
  sound pack pages.
- **On-screen credits:** the end card renders the credits file, including
  the recording's credit.

### Terrain and props

- **Forest base:** `lpc-revised` terrain, trees, mushrooms, wildflowers and
  plants (OGA-BY). Summer is used for the twilight glade and autumn for the
  mushroom grove.
- **Infernal look:** autumn tiles recolored to an ember palette, plus lava,
  lava rock, dead trees, braziers and ruins from the CC-BY-SA packs.
- **Light sources:** campfire and fireplace objects double as light sources.

## Map Generation

The map is a long strip of forest that the hero travels through over the
song. A seeded generator builds it from the route and zone themes. Hand-made
set pieces are stamped in at key points.

- **Route:** a smooth curve heading generally right and slightly up, made of
  gentle bends. Its length is the hero's total travel distance. That distance
  comes from a small **route schedule** computed from the song map and level
  script before map generation: how far the hero travels in each section.
- **Combat corridor:** a band 22 tiles wide around the route, kept clear of
  tall props. The planner keeps all combat inside it.
- **Zones:** each zone covers the stretch of route where the hero is during
  its sections. Zones blend into each other over 8 tiles by mixing tiles and
  props from both themes.
- **Zone themes:** a theme sets the terrain tileset, prop lists with
  densities, ambient light color, color grade and ambient particles.
- **Prop placement:** Poisson disk sampling outside the corridor, denser
  further out. Sparse small props (flowers, mushrooms, rocks) are allowed
  inside it.
- **Set pieces:** small maps built in Tiled and stamped at a given bar's
  route position. Examples are the ring of standing stones for the boss and
  the ruined shrine for the finale. A set piece can replace part of the
  corridor with its own walkable area.

```yaml
id: ember-hollow
terrain: { tileset: terrain_autumn, recolor: ember }
props:
  - { sprite: dead-tree, density: 0.04 }
  - { sprite: brazier, density: 0.01, light: { color: "#ff7a2e", radius: 96 } }
ambientLight: "#5a2a2a"
particles: rising-embers
```

Map generation runs before the planner. Both use the same route schedule, so
each section's zone is where the hero is during that section. Kill assignment
ignores small props inside the corridor.

## Automated Verification

Agents must be able to verify their own work. Besides unit tests, the project
provides these command-line checks:

- **Song map check:** the analysis tool's `check` command.
- **Plan check:** runs the planner in Node and all of its output checks, and
  prints a summary. The summary includes:
  - peak population
  - kills per section
  - total hit-stop time
  - longest stretch without a synchronized event
- **Determinism check:** plans twice and compares hashes.
- **Frame capture:** opens the demo in headless Chrome with a test hook. The
  hook replaces the audio clock with a virtual clock, renders exact song
  times and saves screenshots.

```bash
npm run capture -- --times 12.0,36.84,88.31,141.5 --out captures/
```

- **Benchmark:** plays the whole song in headless Chrome on the virtual clock
  at real-time speed. It records CPU and GPU frame times and fails if the 95th
  percentile exceeds 16.7 ms or any frame exceeds 33 ms.
- **Asset check:** confirms that every recipe's licence is allowed. Also
  confirms that every animation referenced in content exists in the atlases
  and that every credit has an author and URL.

The test hooks are enabled only by URL parameters such as `?capture`. They
have no on-screen interface.

## Can-Can Level Content

This section maps the viewer experience onto concrete level data. Bar numbers
come from the song map; the roles below are what the beat-map agent should
identify.

| Section role | Pulse | Moveset | Zone | Time scale | Population | New elements |
|---|---|---|---|---|---|---|
| Slow prelude | free | spinning-glide | twilight-glade | 0.5 | 30 → 60 | title card |
| Galop opening chords | pulsed | herald-cleaves | twilight-glade | 1.0 | 60 → 120 | level-up: orbiting blades |
| Theme A | pulsed | galop-steps | galop-woods | 1.0 | 120 → 250 | goblins |
| Theme A repeat | pulsed | galop-dashes | galop-woods | 1.0 | 250 → 320 | level-up: chain lightning, bee formations |
| Contrasting theme | pulsed | kick-line-dance | mushroom-grove | 1.0 | 320 → 350 | kick line, archers |
| Bridge to the return | pulsed | descent-steps | ember-hollow | 1.0 | 350 → 420 | level-up: fire trail |
| Theme A return (climax) | pulsed | revel-onslaught | infernal-shrine | 1.0 | 420 → 500 | boss entrance |
| Coda | pulsed | finale-frenzy | infernal-shrine | 1.0 → 0.4 at the end | 500 → 0 | boss death, finale nova |

### Signature moments

- **First chord:** a 150 ms hit-stop, a level-up and a zoom punch.
- **Kick line:** 12 ember-recolored skeletons enter as a straight formation
  line. They kick on each beat, alternating legs on alternating beats, for
  two phrases. On the phrase's final accent, the hero dash-strikes along the
  line and all 12 die in sequence on sixteenth notes.
- **Boss entrance:** the minotaur lands on a downbeat. The remaining enemies
  are knocked back and a banner slides in.
- **Boss fight:**
  - The boss takes 8 planned hits across the climax.
  - It performs 4 telegraphed slams, each dodged on the beat.
  - It dies on the second-to-last tutti chord, with a 150 ms hit-stop
    followed by a 0.4× slow-motion ramp.
- **Finale nova:** on the final chord, every remaining enemy dies within one
  beat as the ring passes over it.

### Formations

A **formation** is a group of enemies that move as a rigid shape instead of
through the crowd simulation. Each member follows a slot in the shape. Types:

- `line`
- `ring`
- `spiral`
- `v-wedge`
- `kick-line`

Each formation defines:

- **Entrance path:** how the group enters the screen.
- **Step rhythm:** the subdivision it moves on.
- **Optional dance animation:** for example the kick.
- **Release time:** when members either die in a planned hit or break off
  into the normal crowd.

```yaml
id: skeleton-kick-line
formation: kick-line
enemy: skeleton-ember
count: 12
spacing: 34
enterFrom: right
dance: { animation: kick, on: beat, alternateLegs: true }
release: { at: phraseEnd, by: dash-strike, staggerSubdivision: sixteenth }
```

## Future Songs

The design must handle the five later songs with only new data: a song map,
a level script, and possibly new movesets, enemies and zone themes. Each song
stresses something different:

- **William Tell Overture (finale):** a sustained fast gallop. It tests long
  dense sections and how stable the crowd simulation is at the population
  cap.
- **Beethoven's Fifth (first movement):** dramatic pauses and held notes
  (fermatas). It tests long hit-stop-like freezes, which should become slow
  `free` passages and not hit-stops longer than 150 ms.
- **In the Hall of the Mountain King:** a steady accelerating tempo. It tests
  beat-grid smoothness checks across a deliberate tempo ramp; the song map
  marks it as a tempo change.
- **Flight of the Bumblebee:** constant sixteenth notes. It tests the sound
  voice budget and whether subdivision-level effects are readable.
- **Ride of the Valkyries:** rubato passages with a weak beat. It tests `free`
  sections and anchors at length.

Code must not branch on song id. Any song-specific behavior belongs in
content data.

## Implementation Plan

Agents build the demo in the milestones below. Each milestone ends with
passing automated checks, a commit and, where marked, a human review gate.
Work on a milestone may start once the milestones it depends on pass.

1. **Project setup:**
   - Vite + TypeScript + PixiJS v8 app, ESLint, Vitest, Playwright and a
     Python project for the analysis tool.
   - Check: lint, type-check and test commands run clean.
2. **Song map for the overture:**
   - The analysis tool and the beat-map agent workflow produce the cropped
     excerpt and song map.
   - Check: `check` passes.
   - **Gate:** a human listens to the click track and confirms that the beats
     and section boundaries are right.
3. **Synced metronome runtime:**
   - The audio clock, the title screen and the HUD beat track, plus a
     full-screen flash on every beat.
   - Check: frame capture at known beat times shows the flash.
   - **Gate:** a human watches and confirms that the flash looks locked to
     the music. This is the early end-to-end integration check.
4. **Asset baking:**
   - The hero, all roster enemies, the ember variants, the kick frames and
     the hit flash frames, plus the credits file.
   - Check: the asset check passes.
   - **Gate:** a human reviews a contact sheet of every baked animation.
5. **Map generation:**
   - The route, zones, props and set pieces, with camera flythroughs
     captured as screenshots.
   - **Gate:** a human reviews the zone screenshots.
6. **Planner core:**
   - Time resolution, hero plan, kill assignment, crowd simulation, camera,
     and evaluation and rendering with plain sprites.
   - Check: the plan check and the determinism check pass.
7. **Time warp:**
   - Slow motion in the prelude, hit-stops and the world beat grid.
   - Check: the plan check includes hit-stop clearance.
8. **Juice:**
   - All effect recipes, lighting, post-processing, decals and gems.
   - Check: the benchmark passes.
   - **Gate:** a human reviews captures of the biggest moments.
9. **Sound:**
   - Sound palettes, scheduling, budget and the mix.
   - Check: the plan check's sound budget passes.
   - **Gate:** a human listens to the whole level.
10. **Set pieces and threats:**
    - Level-ups and weapons, formations and the kick line, archers, the
      boss and the finale nova.
    - Check: all plan checks pass.
11. **HUD, titles and credits; final polish and performance pass:**
    - **Gate:** the full Manual QA below.

## Manual QA

Manual QA is done by a human watching the demo in desktop Chrome at 1920×1080
with wired headphones or speakers. Bluetooth audio adds latency; use
`?avOffsetMs=` to compensate if needed.

### First check: the synced metronome

Before reviewing anything else, confirm the assembled audio-to-picture path:

- **Click to start.** Load the page and click "Click to begin".
- **Beat flash.** The first build shows a beat flash, then the HUD beat track.
  The flash and the heart must look locked to the music from the first beat
  to the last, with no gradual drift.
- **Phone camera test.** Film the screen and speaker with a phone's
  slow-motion camera on a loud accent. The flash should be within one frame
  (about 17 ms) of the sound.

### Full playthrough

Watch the whole level at least twice and confirm:

- **Prelude.** The prelude feels calm. Motion glides, the world moves in slow
  motion, and kills are soft. The title card is readable.
- **First chord.** The first galop chord snaps to full speed with an obvious
  hit-stop and level-up.
- **On the beat.** Every hero impact lands on the beat. Hits do not land just
  before or after it.
- **Ensemble rhythm.** Monster groups visibly move on different subdivisions,
  and the horde looks like it is dancing.
- **No ghost hits.** No slash passes through a monster that survives.
- **Off-screen spawns.** No monster pops into view without a spawn effect.
- **Kick line.** The skeleton kick line is recognizably a can-can, and the
  hero's dash-strike lands on the phrase's final accent.
- **Archers.** Archer arrows visibly miss by a hair, with a "PERFECT" callout.
- **Boss.** The boss's slams are telegraphed and dodged on the beat. The
  boss dies on the second-to-last chord.
- **Finale.** The finale nova clears every monster on the final chord.
- **Zones.** The scenery turns from twilight forest to infernal grove in step
  with the music's rising energy.
- **Sound.** Sound effects sound like part of the music. No impact flams
  against the beat, and there is no wall of noise at the climax.
- **HUD.** The XP bar fills exactly at each level-up. The kill counter and
  combo climb plausibly.
- **Credits.** The end card shows totals and the credits roll, including the
  recording credit and share-alike notices.

### Edge cases

- **Hidden tab.** Switch tabs for 10 seconds during the climax and come back.
  The picture must jump straight to the right moment and stay in sync, and no
  burst of late sounds plays.
- **Window resize.** Resize the window mid-playback. The view rescales without
  losing sync or blurring the pixel art.
- **Cached plan.** Reload the page. The second load uses the cached plan and
  produces the same playthrough.
- **Broken level script.** Introduce a deliberate error in the level script,
  such as a bar range outside the song. Loading must show a clear error and
  must not start playback.
- **Performance.** Run on the reference laptop with the browser's FPS meter
  on. The climax must hold 60 fps.

### Test hooks for QA

- `?capture&t=88.31` renders a single frozen frame at the given song time,
  without audio. Use it to inspect a specific moment.
- `?avOffsetMs=-40` shifts the picture relative to the audio, for unusual
  audio hardware.
- `?seed=1859` re-plans with a different seed. The level should look
  different but still pass every plan check.
