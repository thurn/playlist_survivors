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
- **Calm passages still progress the action.** Slow music with a loose beat
  gets gliding, spinning, slow-motion combat instead of a pause.
- **A dramatic arc.** Heavier attacks land on the loudest accents. Tougher
  enemies and a boss arrive at the climax. The hero powers up over the song.
- **Juice everywhere.** **Juice** is game-design slang for feedback effects
  that make actions feel physical: particles, explosions, slash effects,
  screenshake, hit-stop, flashes and impact sounds. Juice is mandatory here,
  not polish.
- **A large horde.** Up to about 500 detailed monsters alive at once.
- **Data-driven levels.** Adding one of the five later songs should need new
  data, not new code paths.

### Fixed constraints

- **No user input** during the level. The only interaction is one click to
  start, which browsers require before audio can play.
- **Desktop browsers only.** The target is current Chrome on the **reference
  machine**, the Apple Silicon Mac used for development and review, at
  1920×1080 and 60 fps. Firefox and Safari are best-effort.
- **Liberated Pixel Cup (LPC) art.** Characters and terrain come from the LPC
  collections, plus a few compatible CC0 effect packs.
- **Legally usable music.** The recording must be public domain or Creative
  Commons. See the [Music Source](#music-source) section below.
- **Asset licence policy.** Allowed licences are CC0, CC-BY, CC-BY-SA and
  OGA-BY (OpenGameArt's attribution licence, which is CC-BY 3.0 minus its ban
  on DRM). GPL-only and LGPL-only art is not allowed. When an asset offers
  several licences, use the most permissive one.
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
  level, such as "galop theme, hopping movement, goblins, zoom out". It refers
  to music by bar and beat number, never by seconds.
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
- `~/Documents/lpc/ulpc-generator`: the Universal LPC (ULPC) character
  generator. The hero and humanoid monsters are baked from its layered
  sheets. Read its `ARCHITECTURE.md` and the JSON files under
  `sheet_definitions` before writing the baker.
- `~/Documents/lpc/lpc-revised/Terrain`: OGA-BY forest terrain, trees,
  mushrooms and flowers in four seasons. This is the base of the forest.
- `~/Documents/lpc/source-of-tales`: CC-BY-SA monsters (slime, bat, bee,
  eyeball), lava and fire tiles, and spell effects such as explosions,
  lightning and the fire lion.
- `~/Documents/lpc/lpc-tile-atlas`: CC-BY-SA lava, ruins and standing stones.
  Check each tile's attribution file, because some parts are GPL-only.
- [Orpheus in the Underworld overture, Musopen recording](https://commons.wikimedia.org/wiki/File:Offenbach_-_Orpheus_in_the_Underworld_-_Overture.ogg):
  the development recording. See [Music Source](#music-source).
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
- [PixiJS v8 guides](https://pixijs.com/8.x/guides): renderer,
  `ParticleContainer`, filters and render textures.
- [MDN: AudioContext.getOutputTimestamp](https://developer.mozilla.org/en-US/docs/Web/API/AudioContext/getOutputTimestamp):
  the basis of the audio clock.
- [madmom](https://madmom.readthedocs.io) and [librosa](https://librosa.org):
  beat, downbeat and onset analysis libraries.
- [Tiled map editor](https://www.mapeditor.org): used to build hand-made
  landmarks such as the finale shrine.

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
  2/4 time. At 185, a beat lasts about 324 ms, an eighth note 162 ms and a
  sixteenth note 81 ms. Analysis must confirm the tempo.
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
   speed with a hit-stop (a brief freeze of the game world) on the first
   chord. A level-up flash grants orbiting blades. Goblins rush in.
4. **Galop theme.** The hero hops on every beat and slashes goblins and
   slimes in sharp, rhythmic arcs. Each kill sprays debris and drops
   experience gems that fly to the hero on the downbeats.
5. **Galop theme, repeat.** Dashes are added. Bees arrive in V-shaped
   formations. A second level-up adds chain lightning on downbeats.
6. **The kick line.** The scenery turns to an autumn mushroom grove. A line of
   skeletons does a synchronized can-can kick to the music. On the phrase's
   final accent, the hero sweeps down the whole line, cutting each skeleton
   down on successive sixteenth notes. Skeleton archers fire volleys that the
   hero dodges on the beat, with "PERFECT" callouts.
7. **Descent.** The forest darkens to ember red and the ground cracks with
   lava. A third level-up adds a fire trail. The horde grows toward its
   peak.
8. **Lord of the Revel (climax).** A banner announces a huge minotaur boss.
   About 500 monsters are on screen. The boss winds up ground slams, and the
   hero dodges each one on the beat and counter-attacks on the next. The
   heaviest attacks land on the loudest orchestral hits.
9. **Finale.** On the second-to-last chord the boss dies, with a hit-stop and
   then slow motion. On the final chord, a radial blast clears every
   remaining monster. An end card shows totals, then scrolls the credits.

## System Overview

The system has five offline or load-time stages and one runtime.

- **Audio analysis tool** (Python, offline): turns the recording into a draft
  song map plus diagnostic plots and statistics.
- **Beat-map agent** (a Claude subagent, offline): uses the analysis tool's
  commands to correct the draft into the final song map, which is committed.
- **Asset baker** (Node, offline): composites LPC layers into sprite atlases
  and generates the credits list.
- **Planner** (TypeScript, at load): turns the song map, level script and
  content definitions into the plan.
- **Map generator** (TypeScript, at load): builds the forest from the route,
  zone themes and hand-made landmarks. The planner calls it partway through
  planning, once it knows how far the hero travels in each section.
- **Runtime** (TypeScript in the browser): plays the audio, reads song time,
  evaluates the plan at that time, renders it and schedules sound effects.

Every stage except the beat-map agent is deterministic: the same inputs and
seed always produce the same output. The agent's output is committed, so
everything after it is reproducible.

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

- **Audio reference:** file name, crop start and end in the source recording,
  and sample rate. All other times in the map are seconds from the start of
  the cropped file.
- **Meter regions:** the time signature for each range of bars. The excerpt
  starts in 4/4 for the slow passage and switches to 2/4 for the galop.
- **Beats:** one time per beat, strictly increasing, covering the whole
  excerpt.
- **Downbeats:** the first beat of each bar, located by `firstDownbeatIndex`
  (which beat is beat 1 of bar 1) plus the meter regions.
- **Sections:** labelled bar ranges, each with:
  - A **feel**. `pulsed` means the music has a clear beat that motion should
    hit. `free` means the beat is weak or rubato (the performer freely
    stretches the tempo), so motion should flow.
  - An optional musical key, such as `D major`, used to tune pitched sound
    effects.
- **Phrases:** the bar ranges of musical phrases (typically 4 or 8 bars),
  taken from the score. Anchors, kick-line timing and other choreography
  refer to phrases.
- **Anchors:** in `free` sections, a short list of strong musical moments
  (phrase starts, swells, chord changes), each with a time and a 0–1
  strength. The planner synchronizes visible events to anchors instead of to
  every beat.
- **Tempo changes:** bar ranges where the tempo deliberately speeds up or
  slows down. Smoothness checks are relaxed there.
- **Energy:** a 0–1 value per beat combining loudness and onset density,
  smoothed and normalized over the excerpt.
- **Accents:** the loudest or most striking hits, each with a time, a 0–1
  strength and an optional label. `tutti-chord` marks a hit played by the
  whole orchestra at once.

An abbreviated example:

```json
{
  "audio": { "file": "orpheus-galop.flac", "cropStart": 418.2, "cropEnd": 562.9 },
  "meter": [{ "fromBar": 1, "beatsPerBar": 4 }, { "fromBar": 9, "beatsPerBar": 2 }],
  "beats": [0.41, 1.52, 2.60, 3.71],
  "firstDownbeatIndex": 0,
  "sections": [
    { "label": "prelude", "bars": [1, 8], "feel": "free",
      "anchors": [{ "time": 0.41, "strength": 0.6 }, { "time": 9.02, "strength": 0.8 }] },
    { "label": "galop-opening", "bars": [9, 16], "feel": "pulsed", "key": "A major" }
  ],
  "phrases": [[1, 4], [5, 8], [9, 12], [13, 16]],
  "tempoChanges": [],
  "energy": [0.12, 0.14, 0.13, 0.15],
  "accents": [{ "time": 35.2, "strength": 1.0, "label": "tutti-chord" }]
}
```

### Musical positions

Musical references in level scripts and content use 1-based bars and beats,
as musicians count. `{ bar: 9, beat: 1 }` is the first beat of bar 9.

Code also uses a 0-based fractional **beat index**: the position in the beat
list plus the fraction of the way to the next beat. Conversion is
piecewise-linear interpolation over the beat list. This handles tempo changes
and rubato without any notion of a global tempo. Using the example beats
above:

```ts
beatIndexAt(songMap, 2.0);              // → 1.444 (between beats 1 and 2)
timeOfBeatIndex(songMap, 2.5);          // → 3.155
timeOf(songMap, { bar: 9, beat: 1 });   // → 35.2 (first beat of the galop)
```

Before the first beat and after the last beat, conversion extrapolates from
the nearest two beats.

Level scripts and content can also refer to musical moments by name instead
of by bar and beat. Each form resolves to one song time through the song map:

```yaml
at: { bar: 9, beat: 1 }                          # a bar and beat
at: { section: kick-line, phrase: 2, point: finalAccent }  # see below
at: { accent: tutti-chord, fromEnd: 2 }          # the second-to-last tutti chord
at: { section: coda, point: start }              # also: end
```

A phrase's `finalAccent` is the strongest accent in the phrase's last bar, or
the first beat of that bar if it has no accent. `phrase` counts from 1 within
the section.

## Building the Song Map

The beat grid has to be right. A beat that is off by 30 ms is visible when 400
monsters stomp on it. Orchestral recordings speed up and slow down, and
automatic beat trackers often lock onto the wrong level (half or double the
real tempo) or drift during rubato. So a Claude subagent, the **beat-map
agent**, builds the song map using a purpose-built analysis tool.

### Analysis tool commands

The tool is a Python command-line program. It uses madmom for beats and
downbeats and librosa for onsets, loudness and spectrograms. Every command
writes machine-readable output. The agent judges quality from statistics and
images, because it cannot listen.

- **`crop`:** cuts the excerpt from the source recording and writes it as
  FLAC. FLAC is lossless, plays in all target browsers and adds no encoder
  delay, so song-map times stay exact.
- **`analyze`:** runs several beat trackers and writes a draft song map. The
  trackers are madmom's recurrent-network beat and downbeat trackers (which
  use a dynamic Bayesian network to pick a consistent beat sequence) and
  librosa's `beat_track`. It also writes the energy curve and a list of
  accent candidates.
- **`report`:** writes diagnostics for the current song map:
  - Spectrogram strips (8 seconds each) with beat and downbeat lines drawn
    over the **onset-strength curve**, a per-frame measure of how much new
    sound starts at that moment. Written as PNG images.
  - A plot of the gap between consecutive beats across the whole excerpt.
  - A table of beats where the trackers disagree by more than 40 ms.
  - Per-section statistics, described in the
    [Acceptance thresholds](#acceptance-thresholds) section below.
- **`edit`:** applies named corrections from a JSON list, so every change is
  reviewable and repeatable.
- **`check`:** validates the song map against the thresholds and exits
  non-zero on failure.
- **`click`:** renders a WAV of the excerpt with a click on each beat (louder
  on downbeats), for the human review gate.

The `edit` operations are:

```json
[
  { "op": "shift", "beats": [210, 260], "byMs": -18 },
  { "op": "insertBeat", "after": 341 },
  { "op": "deleteBeat", "index": 402 },
  { "op": "setFirstDownbeat", "index": 1 },
  { "op": "respace", "from": 120, "to": 136 },
  { "op": "setSection", "label": "kick-line", "bars": [57, 72], "feel": "pulsed" },
  { "op": "addAccent", "time": 88.31, "strength": 0.9, "label": "cymbal" },
  { "op": "addAnchor", "time": 14.7, "strength": 0.7 }
]
```

The `respace` operation re-spaces beats evenly between two beats the agent
has confirmed against clear onsets. It is the main fix for drift in rubato
passages.

### Beat-map agent workflow

The agent's job is to produce a song map that passes `check` and matches the
score's structure.

1. Run `crop` and `analyze`.
2. Write down the expected form from the IMSLP score, with the expected bar
   count of each section. For example: "slow passage, galop opening chords,
   theme A, theme A repeat, contrasting theme, bridge, return of theme A,
   coda."
3. Run `report` and look for three kinds of error:
   - beats where the trackers disagree
   - octave errors, where beats run at half or double speed
   - phase errors, where bar lines land on the wrong beat
4. Apply `edit` operations, then repeat `report` until the numbers settle.
5. Confirm that section boundaries fall on downbeats and that bar counts match
   the score. Record the phrases from the score.
6. Curate accents. Keep the strongest 20–40 and label the obvious tutti
   chords.
7. Run `check` and `click`. Commit the song map, the edit list and the report
   summary.

For `free` sections, the beat grid only needs to be roughly right. The agent
should spend its effort on anchors instead: 1 to 3 per phrase, at clearly
visible onsets.

### Acceptance thresholds

`check` enforces these per section:

- **Onset alignment (`pulsed` sections only):** at least 90% of beats are
  within 25 ms of a peak in the onset-strength curve, counting only beats
  with a peak within 80 ms.
- **Smoothness (`pulsed` sections only):** each gap between consecutive beats
  differs from the previous gap by at most 8%, except inside a marked tempo
  change.
- **Structure:** sections cover the excerpt without gaps or overlaps and start
  on downbeats.
- **Anchors:** every `free` section has at least one anchor per 8 seconds.
  Each anchor is within 30 ms of an onset peak.
- **Accents:** every accent is within 20 ms of an onset peak.

## Song Time and World Time

The demo needs two clocks. Hit-stop and slow motion both change how fast the
game world moves, but the music never pauses or slows down.

- **Song time** (`s`) is seconds into the audio excerpt. It always advances
  at real-time speed.
- **World time** (`w`) is the clock that game motion runs on: walking,
  animation, particles and projectiles.
- The **time warp** is a function `w = W(s)` that maps song time to world
  time. It never decreases.

The warp is the running total of a playback rate that the planner chooses.

- **Rate 1** is normal play.
- **Rates from 0.25 to 1** are slow motion. The prelude runs at 0.5, and near
  misses dip briefly to 0.4.
- **Rate 0** is a hit-stop: world time freezes for 40–150 ms after a heavy
  impact. A hit-stop never lasts longer than 150 ms. Longer dramatic pauses
  use slow motion instead.
- **Ramps:** section-to-section rate changes blend smoothly over one beat.
  Near-miss dips are short: 40 ms down to 0.4, 70 ms held, 40 ms back up.
  Hit-stops start and end instantly.

Hit-stops never need "catch-up" time. After a 100 ms hit-stop, world time
simply stays 100 ms behind song time for the rest of the song. This is
harmless because of the synchronization rule below.

### Synchronization rule

Every planned event that must line up with the music is placed by its song
time. The planner converts that song time to world time with `W` and
schedules the event there. Because of this, synchronized events land exactly
on their beats however much time warping happened earlier.

```ts
const sHit = timeOf(songMap, { bar: 33, beat: 1 }); // musical time
const wHit = warp.toWorld(sHit);                    // planned world time
hero.schedule({ move: "cleave", impactAt: wHit });
```

The warp's inverse, `warp.toSong(w)`, is used by checks and the HUD. Inside a
hit-stop many song times share one world time; by convention `toSong`
returns the song time at which the hit-stop began.

### Hit-stop rules

Synchronized events come in two tiers, and only the first tier constrains
hit-stops:

- **Primary events** are hero impacts, dodges and **near misses** (the
  moment a threat, such as an arrow or a boss slam, reaches the spot the hero
  just left). Every hit-stop must end at least 50 ms (song time)
  before the next primary event.
- **Secondary events** are automatic weapon firings and the individual hits
  of a sweep or the finale blast. A secondary event that falls inside a
  hit-stop moves to the next **slot** (a musical moment where actions can
  happen; see [Stage 1](#stage-1-resolve-musical-time)) of its subdivision
  after the hit-stop ends. Weapons "rest" during the freeze.

The length of each hit-stop is:

```text
accentFactor = 0.6 + 0.4 × accentStrength      // accentStrength 0 if no accent
wanted = move.hitStopMs × accentFactor
length = min(wanted, gapToNextPrimaryEvent − 50 ms, 150 ms)
skip the hit-stop if length < 40 ms
```

At galop tempo the hero acts at most on every beat (324 ms apart), so a
150 ms hit-stop always fits.

An example warp table across a hit-stop and a slow-motion ramp. Each row is a
breakpoint; between rows, world time advances at the row's rate.

```text
song s    world w    rate   note
35.200    17.600     0.0    hit-stop on the first galop chord (prelude ran at 0.5)
35.310    17.600     1.0    110 ms freeze ends; full speed
88.310    70.600     0.0    cleave hit-stop
88.420    70.600     1.0
```

### World beat grid

The **world beat grid** is the song's beat list mapped through the warp:
`wBeat[i] = W(sBeat[i])`. Rhythmic animation in world time, such as a monster
taking one stride per beat, uses this grid. As a result:

- **Slow motion keeps the rhythm.** At rate 0.5, a beat spans half as much
  world time, so a one-stride-per-beat walk still steps on every beat but
  covers less ground per second. The world visibly slows while still dancing.
- **Hit-stop freezes rhythm too.** During a hit-stop the world beat grid
  stops, so walk cycles freeze. This is the desired look.
- **Song-time effects keep moving.** Effects that should ignore hit-stop
  declare that they run on song time instead. Examples are HUD pulses,
  background sway and screenshake.

## Rhythm in Motion

Every moving thing gets its rhythm from two shared shaping functions. Each
takes a phase (the fraction of the way through the current beat, bar or other
period, from 0 to 1) and peaks on the beat.

```ts
const beatHit = (phase: number) => Math.exp(-6 * phase);   // sharp, then decay
const beatSwell = (phase: number) => 0.5 + 0.5 * Math.cos(2 * Math.PI * phase);
const amount = (base: number, energy: number) => base * (0.4 + 0.6 * energy);
```

`beatHit` drives `pulsed` sections. `beatSwell` is a gentle rise and fall
with no sharp edge, and it drives `free` sections.

### Enemy movement groups

Each enemy type belongs to a **movement group**, the subdivision its motion
and animation follow. Different groups move on different subdivisions, so the
horde reads as an ensemble with several rhythms at once.

- `eighth`: bats and bees flap and surge on every eighth note.
- `beat`: slimes hop, and goblins and skeletons stride, on every beat.
- `bar`: orcs and archers stomp on every downbeat. Eyeballs drift once per
  bar in the slow prelude.
- `two-bar`: the boss lumbers once every two bars.

For each group:

- **Speed:** maximum speed is multiplied by `0.6 + 0.8 * beatHit(phase)`,
  using the phase of the group's own subdivision. The group surges on its
  subdivision and coasts between.
- **Animation:** walk cycles are timed from the world beat grid, not elapsed
  time. A goblin takes exactly one stride per beat.
- **Free sections:** each group uses `beatSwell` instead of `beatHit`, over
  its own period lengthened to at least two beats. Motion breathes instead of
  ticking, and the groups still move at different rates.

### Environment and HUD

- **Vegetation:** trees and flowers sway with `beatSwell` over each bar.
- **Lighting:** light intensity rises 5–15% on each beat, scaled by energy.
- **Vignette:** the screen-edge vignette tightens on downbeats.
- **HUD:** elements scale up 3–6% on each beat. The beat-track heart beats.
- **Free sections:** all of the above become slow swells.

All environment and HUD pulses use song time, so they keep going during
hit-stops.

## Content Definitions

Content is data shared by all levels: moves, movesets, enemy types, weapons,
formations, effect recipes, sound palettes and zone themes. Content files are
YAML, validated against schemas at load. A level script refers to content by
id.

All durations in content and in the planner are world seconds unless stated
otherwise. All distances are in **world pixels**, the native pixel size of
the LPC art.
Terrain tiles are 32 px. Character frames are 64 px, with bodies about 24 px
wide. At the default zoom, the screen shows 960×540 world pixels.

### Moves

A **move** is one hero action: an animation plus what it does to the world.
Each move defines:

- **Animation:** which baked animation plays, at what frame rate.
- **Impact frame:** the frame on which the hit lands. The planner starts the
  animation early so this frame lands exactly on its slot (its scheduled
  musical moment). Wind-up happens
  before the beat and contact happens on it.
- **Hit shape:** the area the move damages: an arc, a circle, a line or a
  capsule along a dash path. Its direction is any angle. The sprite uses the
  nearest of its four facing directions (up, left, down, right).
- **Weight:** light, medium or heavy. Weight scales kill counts, effects,
  hit-stop and sound.
- **Hit-stop:** `hitStopMs`, used only by heavy moves.
- **Travel:** how far the hero moves during the move. See
  [Stage 4](#stage-4-plan-hero-motion) for how travel combines with the
  hero's path.

The move list:

- **slash, backslash and halfslash:** a 120° arc of radius 56 px. Light.
- **thrust:** a line 72 px long. Light.
- **spin:** a full circle of radius 64 px, made by cycling the slash animation
  through all four facing directions. Medium.
- **cleave:** an overhead arc of radius 80 px. Heavy, 110 ms hit-stop.
- **dash-strike:** a lunge of 96–160 px whose capsule hits everything along
  the path. Medium.
- **sweep:** a long dash along a formation. It produces one small circular
  hit per target, timed to successive sixteenth notes as the hero passes each
  one. Medium.
- **burst:** a spellcast that hits a circle of radius 140 px. Heavy, 130 ms
  hit-stop.
- **finisher:** a cleave with a burst on impact, used for the boss's death
  blow. Heavy, 150 ms hit-stop.
- **dodge:** a short dash out of a threat's path. It deals no damage and
  triggers the near-miss effect.

For example:

```yaml
id: cleave
animation: { name: slash-oversize, fps: 14 }
impactFrame: 3
hitShape: { kind: arc, radius: 80, degrees: 160 }
weight: heavy
hitStopMs: 110
```

### Movesets

A **moveset** is a section's choreography style. It has four parts:

- **Locomotion mode:**
  - `step` makes one hop per beat or per bar, with a jump arc and a dust puff
    on landing.
  - `dash` makes quick lunges exactly on the beat, then settles.
  - `glide` follows a smooth curve whose speed swells with the energy curve.
- **Travel speed:** how fast the hero advances along the route, in world
  pixels per world-second.
- **Path shape:** the pattern traced around the route, such as `forward`,
  `zigzag`, `circle` or `figure-eight`.
- **Attack rules:** which moves fire on which slots.

Attack rules for `pulsed` sections use grid strings with one character per
grid slot. `x` fires a move and `.` rests. A pattern may span several bars,
separated by `|`, and repeats for the whole section.

```yaml
id: galop-steps
locomotion: { mode: step, every: beat }
travelSpeed: 90
path: zigzag
grid: eighth             # in 2/4, a bar has 4 eighth-note slots
attacks:
  - pattern: "x.x."      # a slash on each beat
    moves: [slash, backslash]   # alternate
  - onAccent: { minStrength: 0.7, move: cleave }   # replaces that slot's move
  - onSectionEnd: dash-strike                       # on the section's last beat
```

The rest of the rule vocabulary:

- **`extends`:** inherit another moveset's fields and rules, then override or
  add to them.
- **`onAccent` with `label`:** match only accents with that label, such as
  `tutti-chord`.
- **Accent matching:** an accent matches the nearest grid slot within 40 ms,
  even if the pattern rests there. Accents further off the grid trigger no
  move but still drive effects such as zoom punches.

```yaml
id: galop-dashes
extends: galop-steps
attacks:
  - pattern: "x...|...."      # dash-strike on beat 1 of every second bar
    moves: [dash-strike]
```

Grids are not used in `free` sections. A `free` moveset instead lists
continuous moves that repeat at a set interval, and moves to fire on anchors.

```yaml
id: spinning-glide
locomotion: { mode: glide }
travelSpeed: 40
path: figure-eight
continuous: { move: spin, everyBeats: 2 }
onAnchor: { move: burst, minStrength: 0.5 }
```

### Enemy types

Each enemy type defines:

- **Sprite:** baked atlas animations for walk, attack and hurt. LPC's hurt
  animation is a fall to the ground, so it doubles as the death animation,
  followed by the death effect.
- **Radius:** the body radius used for spacing. A **body width** is twice
  this.
- **Movement group:** see [Enemy movement groups](#enemy-movement-groups).
- **Speed and lifetime range:** used by the planner.
- **Size class:** `regular` dies in one hit. `elite` and `boss` take
  `hits` planned hits.
- **Attack behavior:** `none`, `archer` or `slam`.
- **Death:** which effect recipe plays and which debris colors to use.

```yaml
id: goblin
atlas: goblin
radius: 12
group: beat
speed: 70
lifetime: [4, 40]
size: regular
attack: none
death: { recipe: death-humanoid, debris: ["#5a8f3a", "#8b1a1a"] }
```

The Can-Can roster:

| Enemy | Source art | Movement group | Notes |
|---|---|---|---|
| slime | source-of-tales | beat (hop) | squash and stretch on landing |
| bat | source-of-tales | eighth (flap) | swooping paths |
| eyeball | source-of-tales | bar (drift) | prelude only |
| bee | source-of-tales | eighth | V-formations |
| goblin | ULPC goblin head | beat (stride) | short sword |
| orc | ULPC orc head | bar (stomp) | elite variant takes 3 hits |
| skeleton | ULPC skeleton body | beat | kick line, ember recolor |
| skeleton archer | ULPC skeleton body and bow | bar | fires volleys |
| minotaur boss | ULPC minotaur head, muscular body | two-bar | drawn at 2× scale |

### Weapons

Level-ups add automatic weapons. Each weapon fires on its own subdivision, so
the arsenal builds up like the layers of an orchestra. Every firing is a hit
with a shape and a slot, planned exactly like a hero move.

- **Orbiting blades:** three blades orbit at radius 72 px, one orbit per bar.
  Each blade's hit shape is a 14 px circle at its position.
- **Chain lightning:** fires on downbeats. It jumps between 3–6 enemies one
  sixteenth note apart. Each jump is a 16 px circle hit on its target.
- **Fire trail:** the ground along the hero's path over the last two beats
  burns. On each beat, the trail is a set of 20 px circle hits.
- **Finale blast:** a one-off ring that expands from the hero on the final
  chord. See [Signature moments](#signature-moments) below.

Each firing kills a small number of enemies, which may be zero. The weapon's
`killsPerFiring` range is interpolated by the slot's energy, and the planner
rounds it with a seeded random draw. For example, at energy 0.5 the blades
below kill 0.25 enemies per blade per firing on average, about 4.6 kills per
second at galop tempo.

```yaml
id: orbiting-blades
fires: { every: eighth }
orbit: { count: 3, radius: 72, periodBars: 1 }
hitShape: { kind: circle, radius: 14, at: blade }
killsPerFiring: [0, 0.5]     # per blade, at energy 0 and 1
weight: light
```

### Formations

A **formation** is a group of enemies that move as one rigid shape instead of
through the crowd simulation. Each member follows a slot in the shape. The
shapes are `line`, `ring`, `spiral`, `v-wedge` and `kick-line`.

A formation defines:

- **Entrance:** the side of the screen it enters from.
- **Step rhythm:** the subdivision it moves on.
- **Dance:** an optional animation, such as the kick.
- **Release:** when and how the formation ends. Members either die in a
  planned hit or break off into the normal crowd.

```yaml
id: skeleton-kick-line
shape: kick-line
enemy: skeleton-ember
count: 12
spacing: 34              # the line is about 374 px long
enterFrom: right
dance: { animation: kick, on: beat, alternateLegs: true }
release: { at: finalAccentOfPhrase, move: sweep, stagger: sixteenth }
```

### Effect recipes and sound palettes

An **effect recipe** is the list of visual layers played for one event type,
such as a light hit or a goblin death. See the [Juice](#juice) section below.
A **sound palette** is a named set of samples for each event type. See the
[Sound](#sound) section below.

### Zone themes

A **zone theme** sets a zone's terrain tileset, props, ambient light, color
grade and ambient particles. See the [Map Generation](#map-generation) section
below.

## Level Script

The **level script** is a human-written YAML file, one per song. It describes
the level one section at a time. The planner fills in every beat. The
planner validates the script against a schema at load and reports each error
with its line number.

### Script contents

The script has four parts:

- **Header:** song id, random seed and title card text.
- **Zones:** the ordered list of zones, each naming a zone theme.
- **Sections:** one entry per song-map section, keyed by bar range.
- **Events:** level-ups, formations, boss entrances and other one-off moments,
  each at a bar and beat.

Each section entry sets the following. Fields marked "inherits" default to
the previous section's value.

- **`moveset`:** the hero's choreography. Required.
- **`zone`:** the zone the hero is in during this section. Inherits.
- **`timeScale`:** the world-time rate, either one number or a
  `{ from, to }` ramp across the section. Default 1.0.
- **`population`:** the target number of living enemies, as
  `[start, end]` for the section.
- **`roster`:** the enemy types that spawn, with relative weights. Inherits.
- **`camera`:** `zoom`, plus `lead` (how far ahead of the hero, along the
  route, the camera aims) and `punchOnAccents`. Inherits.
- **`sfxPalette`:** the sound palette. Inherits.
- **`intensity`:** a 0–1 multiplier on effect strength. Default 1.0.

An abbreviated example (bar numbers are illustrative; the song map
determines the real ones):

```yaml
song: orpheus-galop
seed: 1858
title:
  piece: "Orpheus in the Underworld — Overture"
  composer: "Jacques Offenbach"
  performer: "Musopen archive recording"   # the source credits no performer
zones: [twilight-glade, galop-woods, mushroom-grove, ember-hollow, infernal-grove]
sections:
  - bars: 1-8           # slow prelude, 4/4
    moveset: spinning-glide
    zone: twilight-glade
    timeScale: 0.5
    population: [0, 60]
    roster: { bat: 3, slime: 2, eyeball: 1 }
    camera: { zoom: 1.4, lead: 40 }
    sfxPalette: soft-chimes
  - bars: 9-16          # galop opening chords, 2/4
    moveset: herald-cleaves
    population: [60, 120]
    roster: { slime: 2, goblin: 2 }
    camera: { zoom: 1.0, lead: 80, punchOnAccents: true }
    sfxPalette: crisp-steel
events:
  - at: { bar: 9, beat: 1 }
    levelUp: orbiting-blades
  - at: { bar: 57, beat: 1 }
    formation: skeleton-kick-line
```

Events use the musical reference forms from
[Musical positions](#musical-positions). The event types are:

- **`levelUp`:** grant a weapon.
- **`formation`:** start a formation; its own definition sets its release.
- **`threats`:** schedule enemy attacks across a range.
- **`boss`:** a boss entrance, with its hit count and slams.
- **`banner`:** show a section banner.
- **`titleCard`:** show the title card over a range.
- **`landmark`:** stamp a Tiled landmark at the hero's route position.
- **`timeScale`:** change the world-time rate from this moment, ramping over
  one beat. It overrides the section's `timeScale` until the next section.

```yaml
  - at: { section: prelude, point: start }
    titleCard: { until: { section: prelude, phrase: 2, point: finalAccent } }
  - at: { section: kick-line, point: start }
    threats: { kind: archer-volley, every: 2 bars, until: { section: kick-line, point: end } }
  - at: { section: climax, point: start }
    boss: { type: minotaur, hits: 8, slams: 4 }
    banner: "LORD OF THE REVEL"
    landmark: standing-stones
  - at: { accent: tutti-chord, fromEnd: 2 }
    timeScale: 0.4          # slow motion after the boss's death blow
```

## Map Generation

The map is a long strip of forest that the hero travels through over the
song. A seeded generator builds it during planning, between
[Stage 3](#stage-3-build-the-time-warp) and
[Stage 4](#stage-4-plan-hero-motion).

### Route schedule

The **route schedule** says how far along the route the hero is at every
moment. The planner computes it right after building the time warp, and both
the map generator and the hero planning stage use it.

```text
progress(w)  = ∫ travelSpeed of the current section's moveset, over world time
routeLength  = progress at the end of the song
zone extent  = the stretch of route covered while that zone's sections play
```

Because progress runs on world time, the hero stands still during hit-stops
and slows down in slow motion, and stays in step with the map.

### Layout

The generator lays out the route, a corridor for combat, the forest around
it, and landmarks.

- **Route:** a smooth curve of length `routeLength`, heading generally right
  and slightly up, made of gentle bends.
- **Combat corridor:** a band 22 tiles (704 px) wide around the route. The
  hero always stays in it, and every kill position is inside it. Only small
  props (flowers, mushrooms, rocks) are placed inside it, sparsely.
- **Forest:** tall props such as trees are placed outside the corridor by
  Poisson disk sampling, denser further out. Enemies have no collision with
  props. They spawn in the forest and emerge from between the trees, sorted
  by depth so canopies can hide them.
- **Zone blending:** zones blend over 8 tiles by mixing tiles and props from
  both themes.
- **Landmarks:** small maps built in Tiled and stamped at the route position
  of a given bar. Examples are the ring of standing stones for the boss and
  the ruined shrine for the finale. A landmark may widen the corridor with its
  own walkable area.

A zone theme:

```yaml
id: ember-hollow
terrain: { tileset: terrain_autumn, recolor: ember }
props:
  - { sprite: dead-tree, density: 0.04 }
  - { sprite: brazier, density: 0.01, light: { color: "#ff7a2e", radius: 96 } }
ambientLight: "#5a2a2a"
colorGrade: ember
particles: rising-embers
```

## Planner

The planner is a pure TypeScript function. It runs in a web worker at load,
and as a Node command for tests.

```ts
function plan(input: {
  songMap: SongMap;
  level: LevelScript;
  content: ContentLibrary;
  seed: number;        // the level script's seed unless ?seed= overrides it
}): Plan;              // includes the generated MapLayout
```

Requirements:

- **Deterministic.** Use a seeded random generator, stable sorts and no
  `Date` or `Math.random`. The same inputs produce byte-identical output.
- **Fast.** Under 2 seconds for a 3-minute song on the reference machine.
- **Cached.** Results are cached in IndexedDB, keyed by a hash of the inputs
  plus the build's version hash, so code changes never load a stale plan.

The planner runs in eight stages, calling the map generator between Stages 3
and 4. Each stage reads only the outputs of earlier stages. Three ordering
decisions matter:

- **Actions before warp.** Moves are chosen in song time before the time warp
  exists, because hit-stops depend on which moves are heavy.
- **Map after warp.** The map is generated after the warp, because the route
  length depends on how much world time passes.
- **Camera before kills.** The camera is planned before kills, because spawn
  positions depend on what the camera can see.

### Stage 1: Resolve musical time

This stage converts the level script's musical references into song time.

- Resolve every musical reference through the song map.
- Build each section's list of **slots**: the musical moments where actions
  can happen. In `pulsed` sections, slots are the moveset's grid positions.
  In `free` sections, slots are anchors plus the continuous-move times.
- Attach each slot's energy and the strength of any accent matched to it.

### Stage 2: Choose actions and threats

This stage decides what happens on each slot, still in song time. Nothing
here has a position yet; positions come in later stages.

- **Pick moves:** for each slot, take the move from the moveset's rules.
  Accent rules override pattern moves.
- **Fire weapons:** add automatic weapon firings on their subdivisions, from
  each weapon's level-up onward.
- **Schedule threats:** enemies attack, but the hero never takes damage, so
  each threat is scheduled together with the hero's escape. Each threat gets
  a **bearing**: the direction, relative to the route, it comes from.
  - **Archer volley:** an archer releases on a beat. On the beat before the
    arrow arrives, the hero dodges. The near miss is the arrow's arrival.
  - **Boss slam:** a glowing ground marker grows over two beats. The hero
    dodges on the slam beat, and the slam lands on empty ground. The hero
    counter-attacks with a cleave on the next beat.
  - **Dodges replace attacks:** a dodge takes over its slot's planned
    attack.
- **Assign multi-hit targets:** the boss's `hits` are its slam counters plus
  the heaviest hero moves while it is present, spread evenly. Its death blow
  becomes a `finisher`. Those hits also kill crowd enemies as normal.
- **Expand set moments:** a formation release becomes one hit per member,
  such as one sweep hit per kick-line skeleton on successive sixteenth notes.
  The finale blast becomes a series of ring hits on sixteenth notes across one
  beat, each a ring-shaped band further from the hero than the last.

### Stage 3: Build the time warp

This stage builds `W` from the chosen actions.

- Start from each section's `timeScale` and any `timeScale` events. Ramp
  between rates over one beat.
- Add a near-miss dip at each near miss.
- Add hit-stops for heavy moves, following the
  [Hit-stop rules](#hit-stop-rules).
- Move secondary events out of hit-stops.
- Produce `W`, its inverse, the world beat grid and the route schedule.

The planner then calls the map generator with the route schedule.

### Stage 4: Plan hero motion

The hero's position is the sum of three parts:

- **Route progress:** the route schedule gives the hero's distance along the
  route at every world time.
- **Path shape:** the moveset's path shape adds an offset around the route
  position.
- **Move travel:** dashes, hops, dodges and sweeps push the hero along their
  own paths. The resulting extra offset then relaxes back toward the path
  shape over two beats, like a spring.

The total offset is clamped to stay inside the combat corridor. On top of the
path:

- **Locomotion timing:** hops, dashes and dodges are timed to the world beat
  grid.
- **Animation timing:** each move's animation starts early so its impact
  frame lands on the slot.
- **Directions:** attack directions alternate in a pattern (for example left,
  right, ahead, ahead) so the routine reads as a dance. Dodges move sideways
  relative to the threat's bearing.
- **Sweeps:** a sweep is a straight dash whose length is the formation's
  length. Stage 6 places the formation along it.
- **Output:** position, facing and current animation, baked at 60 samples
  per world-second.

### Stage 5: Plan the camera

This stage produces the camera's path and zoom.

- The camera aims at the hero's position plus `lead` pixels ahead along the
  route.
- Zoom follows each section's `zoom`, easing between sections over one bar.
  If `punchOnAccents` is set, zoom also punches in 2–4% on accents.
- A critically damped spring smooths the camera in world time. The result is
  baked at 60 samples per world-second.
- Screenshake is not baked here. It is computed at evaluation from impact
  events (see [Juice](#juice) below).

### Stage 6: Assign kills (planning backwards)

This stage creates every enemy from its death. For each hit (a hero move,
weapon firing, sweep hit or ring hit), the planner:

1. Chooses a kill count:

   ```text
   kills = round(base[weight] × (targetPopulation / 250) × (0.5 + energy))
   base:   light 2, medium 4, heavy 7
   cap:    0.6 × hitShapeArea / (π × enemyRadius²)
   ```

   Weapons use `killsPerFiring`. Sweep hits kill exactly their one formation
   member. Ring hits share out the living population at the blast (see
   below).
2. Chooses kill positions inside the hit shape at the hit time, by Poisson
   disk sampling so bodies don't overlap.
3. Creates an enemy for each position, with a **fate**: the time, position
   and hit of its death.
4. Gives elites and the boss several fates, one per planned hit. Only the
   last fate is lethal. An elite's earlier fates are hero hits within its
   lifetime whose shapes can reach it.

Threat enemies also get **appointments**: a position they must stand at on a
given beat. An archer's firing spot lies along its threat's bearing, at a
standoff distance from where the hero is on the release beat. The boss's
slam position is next to where the hero stood before the dodge. Arrow paths
are computed from these, so that each arrow passes 8–24 px from the hero's
**hurtbox** (the area that would count as being hit: a 10 px circle centred
20 px above the hero's feet).

Formations are placed backwards in the same way. A kick line's members are
laid out along the hero's sweep path at release time. Their earlier dance
position and entrance path are worked out back from there.

Population is controlled through lifetimes. The number of living enemies is
roughly the kill rate multiplied by the average lifetime. So each enemy's
lifetime starts as:

```text
killRate = kills in the 4 world-seconds centred on its death ÷ 4
lifetime = clamp(targetPopulation ÷ killRate, typeLifetimeMin, typeLifetimeMax)
```

A balancing pass then compares the resulting population curve with the
target, in windows of one bar:

- **Too few enemies:** increase kill counts on hits in the window, up to each
  hit's cap.
- **Too many enemies:** decrease kill counts, down to 1 for hero moves and 0
  for weapons.
- **Repeat:** recompute lifetimes and repeat until the curve is within ±15%,
  for at most 10 rounds. After that, any remaining miss is a planner error.

At the finale blast, the ring hits kill every enemy alive at that moment. The
planner creates exactly the target population's worth of enemies with fates
on the ring hits, at positions spread across the bands.

Each enemy's spawn time is its death time minus its lifetime. It spawns just
outside the camera's view at that moment, on the side it will approach from.
"Outside" means the sprite's whole bounding box (2× for the boss) is at
least 16 px beyond the view's edge. A spawn time before the song starts is
moved to the song start, with the spawn still off-screen.

Some enemies are exempt from spawning off-screen. These are formations and
boss entrances whose entrance has a visible effect (a summoning circle, or
landing from above with a growing shadow).

Here is one hit traced through this stage, with illustrative numbers:

```text
slot: bar 33 beat 1, energy 0.8, accent 0.9 → move cleave (heavy)
kills = round(7 × (400 / 250) × (0.5 + 0.8)) = 15; cap = 0.6 × 8936 / 452 = 11 → 11
positions: 11 Poisson-disk points in the 160° arc, radius 80 px
killRate near bar 33 = 18/s → lifetime = 400 / 18 ≈ 22 s (goblin max 40)
spawn: 22 world-seconds earlier, off-screen on the side the arc faces
```

### Stage 7: Simulate the crowd

A fixed-step crowd simulation (60 steps per world-second) moves every enemy
from its spawn point to its fates and appointments. It uses a spatial hash.
Forces on each enemy:

- **Arrival:** steer toward the next fate or appointment. The desired speed
  is the remaining distance divided by the remaining time, clamped to the
  type's maximum speed.
- **Separation:** push away from neighbors closer than 0.8 body widths.
- **Personal space:** stay at least 40 px from the hero, except during the
  final beat before one of the enemy's own fates.
- **Hit keep-out:** stay out of any hit shape that fires in the next beat,
  unless the enemy has a fate (lethal or not) on that hit. A slash must never
  visibly pass through a monster it doesn't touch.
- **Rhythmic speed:** maximum speed follows the enemy's movement group (see
  [Enemy movement groups](#enemy-movement-groups)).

Further rules:

- **Final blend:** in the last 0.25 seconds before each fate or appointment,
  the enemy blends exactly onto its target.
- **Formations are kinematic:** formation members are not simulated; they
  follow their formation slots exactly.
- **Output:** position samples at 30 per world-second. The runtime
  interpolates them with Catmull-Rom splines.

If an enemy is more than 24 px from its target just before the final blend,
the planner gives it 30% more lifetime and re-runs the whole simulation. It
does this at most three times. After that, any remaining miss is a planner
error.

### Stage 8: Emit effects, sounds and HUD data

For every planned event, the planner looks up the matching effect recipe and
sound palette entry, and emits:

- **Effect events:** recipe id, start time, position, direction, intensity
  and seed.
- **Sound events:** sample id, song time, gain, stereo pan (from screen
  position) and pitch. See [Sound](#sound) below.
- **Experience gems:** each death drops gems. They arrive at the hero on the
  next downbeat that is not inside a hit-stop. Gem values are scaled so that
  the XP bar fills exactly at each level-up, and fills a final time when the
  finale blast's gems arrive, one beat after the blast. The end card follows.
- **HUD series:** kill, combo, "PERFECT" and XP times, stored sorted, so the
  runtime can count events up to any time with a binary search.

### Planner output checks

The planner runs these checks on its own output. They also run in the test
suite. Every failure names the time and entity involved.

- **Impacts on time:** every primary event lands within 1 ms of its slot's
  song time, mapped back through the warp.
- **Deaths covered:** every enemy dies inside the shape of the hit that
  kills it.
- **Off-screen spawns:** every non-exempt enemy's sprite spawns fully outside
  the camera view plus a 16 px margin.
- **No pass-through:** no enemy is inside a hit shape at the hit time unless
  it has a fate on that hit.
- **Spacing:** no two enemies' centres are closer than 0.5 body widths
  (sampled 10 times per second). Formation members and enemies in their death
  animation are exempt.
- **Near misses stay near:** threats never touch the hero's hurtbox and pass
  within 24 px of it.
- **Population:** the living population stays within ±15% of the target
  curve and never exceeds 520. The finale blast is exempt.
- **Clean ending:** every enemy has died by the end of the finale blast.
- **Hit-stop clearance:** every hit-stop follows the
  [Hit-stop rules](#hit-stop-rules).
- **Sound budget:** no sixteenth-note slot exceeds the voice budget.
- **Determinism:** two runs with the same inputs produce the same plan hash.

## Plan Format and Evaluation

The **plan** is the planner's output. It is immutable and is laid out so that
evaluating any moment is cheap. Its shape:

```ts
interface Plan {
  warp: WarpTable;              // breakpoints of (s, w, rate); toWorld / toSong
  worldBeats: Float64Array;     // world beat grid
  hero: HeroTrack;              // 60 Hz samples: x, y, facing, animation, frame
  enemies: EnemyRecord[];
  enemySamples: Float32Array;   // x, y pairs at 30 Hz world time
  events: { effects: EffectEvent[]; sounds: SoundEvent[]; gems: GemEvent[]; ui: UiEvent[] };
  activeIndex: IntervalIndex;   // per 0.5 s bucket: ids of events alive then
  camera: CameraTrack;          // 60 Hz samples: x, y, zoom
  hud: HudSeries;               // sorted times for kills, combo, PERFECT, XP
  map: MapLayout;               // route, corridor, zones, props, landmarks
}
interface EnemyRecord {
  type: string; spawnW: number; deathW: number;
  sampleOffset: number; sampleCount: number;
  fates: { w: number; x: number; y: number; hit: number; lethal: boolean }[];
}
```

`evaluatePlan(plan, s)` computes one frame:

```ts
const w = plan.warp.toWorld(s);
for (const e of aliveAt(plan, w)) {
  const p = sampleSpline(plan.enemySamples, e, w);
  const anim = enemyAnimation(e, w, plan.worldBeats); // beat-locked frame
  out.sprites.push({ p, anim, flash: flashAt(e, w), knockback: knockbackAt(e, w) });
}
out.effects = activeEffects(plan, s, w);  // stateless; see Juice
```

Knockback and hit flash come from each enemy's non-lethal fates. They are
short, decaying offsets on top of the baked path. They never change the path,
so the crowd simulation does not need to account for them.

Evaluation must take under 4 ms at the busiest moment of the song.

## Runtime

The runtime loads everything, starts playback on the first click, then draws
`evaluatePlan(plan, s)` every animation frame.

### Loading

1. Load atlases, the song map, the level script and content.
2. Fetch and decode the FLAC audio into an `AudioBuffer`.
3. Run the planner (which also generates the map) in a worker, or load the
   plan from the cache.
4. Show the title screen with "Click to begin".

The loading screen shows a progress bar. If schema validation or the planner
fails, the screen shows the error, including its line, time or entity. Never
start playback with an invalid plan.

### Audio clock

Song time must match what the listener hears at the moment a frame appears.

- **Playback:** the excerpt plays through one `AudioBufferSourceNode`,
  started at a known context time `t0`.
- **Each frame:** the runtime calls `getOutputTimestamp()`, which pairs a
  context time with the `performance.now()` time at which that audio leaves
  the speakers.
- **Extrapolation:** the runtime extrapolates from that pair to the frame's
  expected presentation time: the `requestAnimationFrame` timestamp plus one
  frame.
- **Fallback:** if `getOutputTimestamp` is unavailable, the runtime uses
  `currentTime − outputLatency` instead.
- **Smoothing:** normally the estimate never runs backwards and corrects by
  at most 2 ms per frame. If the error exceeds 50 ms, for example after an
  audio glitch, it jumps straight to the new value, backwards if necessary.

```ts
const { contextTime, performanceTime } = ctx.getOutputTimestamp();
const presentAt = rafTimestamp + frameDuration + config.avOffsetMs;
const raw = contextTime + (presentAt - performanceTime) / 1000 - t0;
songTime = smoother.update(raw); // monotonic, slewed
```

`config.avOffsetMs` defaults to 0. A URL parameter can set it for hardware
with unusual latency, such as Bluetooth headphones.

If the tab is hidden, the browser stops animation frames but audio keeps
playing. When the tab is visible again, the next frame simply evaluates the
current song time. When the audio ends, the end card stays up.

### Rendering

The renderer is PixiJS v8 on WebGL2. The world is pixel art, so it is drawn at
native pixel size into an offscreen texture, which is then scaled up to the
window.

- **Base view:** 960×540 world pixels at zoom 1.0, shown at 2× on a 1080p
  screen.
- **Zoom range:** 0.7× (the climax, showing about 1370×770 world pixels) to
  1.4× (the prelude, showing about 690×390).
- **Smooth scaling:** non-integer zoom uses a sharp-bilinear upscale. This
  shader scales each pixel up as a crisp block and blends only a one-pixel
  edge between blocks. It avoids both blur and shimmering while still
  allowing smooth zooms.
- **Sub-pixel camera:** sprites snap to whole world pixels. The camera's
  fractional offset is applied in the upscale pass, so camera motion stays
  smooth.

Layers, drawn back to front:

1. **Ground:** pre-rendered chunk textures of 512×512 world pixels.
2. **Ground decals:** blood, goo and scorch marks, plus boss slam markers.
3. **Shadows:** blob shadows under every character.
4. **Depth-sorted world:** the hero, enemies and tall props, sorted by the
   y position of their feet so nearer objects cover farther ones.
5. **Projectiles and slash arcs.**
6. **Additive effects:** sparks, glows and lightning.
7. **Lighting:** an ambient color multiplied over the scene, plus additive
   point lights from fires, attacks and lightning. Lights are drawn as radial
   sprites into a light texture.
8. **Post-processing:**
   - bloom
   - shockwave distortion
   - brief chromatic aberration on heavy hits
   - a vignette pulse
   - a color grade per zone
9. **HUD:** drawn at screen resolution with a pixel font.

Performance rules:

- **Few textures:** pack all character frames into at most four 4096×4096
  atlases, so sprites batch into a few draw calls.
- **Baked hit flash:** bake a white-silhouette version of every frame, so a
  hit flash swaps the texture instead of using a per-sprite shader.
- **Cheap particles:** particles use `ParticleContainer`, with at most 6,000
  live at once.
- **Budgets:** at the busiest moment on the reference machine, JavaScript
  frame time under 8 ms and GPU frame time under 10 ms.

## Juice

Juice is mandatory. Every hit, death, level-up and threat triggers several
layered effects.

### Effect recipes

**Effect recipes** are content data that list the layers for each event type.
Each layer gives:

- its kind: sprite, particle burst, decal, light, shake, flash or distortion
- its art
- its clock (`world`, frozen by hit-stop, or `song`, always moving)
- its parameters

A parameter written as a range `[low, high]` is interpolated by
**intensity**, the product of four factors, clamped to 0–1:

- the section's `intensity`
- the song map's energy at that beat
- the move's weight (light 0.4, medium 0.7, heavy 1.0)
- the accent strength (1.0 when there is no accent)

```yaml
id: hit-light
layers:
  - { kind: sprite, art: slash-arc-white, clock: world, life: 0.18, align: hitShape }
  - { kind: burst, art: spark, count: [6, 14], speed: [80, 200], life: 0.35, clock: world }
  - { kind: shake, trauma: [0.08, 0.2], clock: song }
  - { kind: light, color: "#fff2c0", radius: [40, 90], life: 0.12, clock: song }
```

### Effect catalogue

The catalogue below lists every effect the recipes draw on, grouped by what
triggers it.

Hits and deaths:

- **Hit flash:** the struck sprite shows its white silhouette for 50 ms.
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
  arc, arriving on the next downbeat with a sparkle.

Camera and screen:

- **Screenshake:** each impact adds **trauma**, an amount of shakiness from 0
  to 1 that decays over time. The shake offset is trauma squared times smooth
  noise.
- **Zoom punch:** on accents.
- **Shockwave distortion:** on heavy hits, boss slams and the finale blast.
- **Chromatic aberration:** briefly, on hits with intensity above 0.8.
- **Flash frames:** a full-screen white flash at 20–40% opacity on the
  biggest accents.

Hero and weapons:

- **Dust puffs:** on every hop landing.
- **Afterimages:** fading copies of the hero along dash paths.
- **Weapon effects:** orbiting blade trails, chain lightning bolts and fire
  trail flames.
- **Near-miss flourish:** the slow-motion dip, a "PERFECT" callout and a ring
  of sparks.

Moments:

- **Level-up:** a radial light burst, a ring shockwave and a fanfare. Enemies
  near the hero get knocked back.
- **Boss entrance:** the screen dims, a banner slides in and the boss lands
  with a big shake.
- **Finale blast:** a screen-wide expanding ring kills every remaining enemy,
  with a white flash, shockwave and slow-motion embers.

Ambience:

- **Fireflies** in the twilight glade.
- **Falling leaves** in the mushroom grove.
- **Rising embers and heat haze** in the infernal zones.

### Stateless effects

Effects never keep state between frames. Each active effect is computed from
its start time, seed and recipe, which keeps the frame a pure function of song
time. A particle burst, for example:

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

Screenshake is stateless in the same way. Trauma is summed over recent
impacts instead of being accumulated frame by frame:

```ts
const trauma = Math.min(1, recentImpacts(plan, s, 1.0)
  .reduce((t, i) => t + Math.max(0, i.trauma - DECAY_PER_S * (s - i.s)), 0));
const offset = scale(noise2(seedX, s * 30), noise2(seedY, s * 30), MAX_SHAKE * trauma ** 2);
```

Sparks and debris use world time, so they hang in the air during a hit-stop.
Screenshake, flashes and HUD effects use song time.

### Effect art

- **Local packs:** slash arcs, explosions and spells come from the LPC and
  CC0 packs listed in [Related Information](#related-information).
- **Procedural shapes:** shockwave rings, glows and light blobs are generated
  as simple procedural textures.
- **Pixel style:** all effect sprites follow the pixel-art scale of the
  world. Only bloom, lights and distortion are smooth, modern layers on top.

## Sound

Sound effects must sit inside the music, not on top of it. Every sound is
scheduled with the Web Audio clock for the exact song time of its event,
which the plan already knows.

### Scheduling

Sounds are scheduled slightly ahead of time so they start sample-accurately.

- **Scheduler loop:** a loop runs every 25 ms and schedules every sound event
  in the next 150 ms of song time.
- **Start time:** each sound starts at context time `t0 + s`, using
  `AudioBufferSourceNode.start()`.
- **Never late:** events already in the past, for example after the tab was
  hidden, are skipped rather than played late.

### Palettes and budget

Each section's palette sets the tone. The demo uses three:

- `soft-chimes` for the prelude
- `crisp-steel` for the galop
- `heavy-infernal` for the climax

```yaml
id: crisp-steel
tuned: false
sounds:
  hit-light:  [kenney/impactMetal_light_000.ogg, kenney/impactMetal_light_001.ogg]
  hit-heavy:  [rpg80/sword_heavy_01.ogg]
  kill-multi: [kenney/impactPunch_heavy_002.ogg]
  near-miss:  [rpgpack/swish_2.ogg]
```

Budget rules:

- **Per-slot budget:** at most 4 sound voices start in any sixteenth-note
  slot. Priority order: heavy hero hits, boss, near misses, weapons, kills.
- **Merged kills:** many kills in one slot become one layered "multi-kill"
  sound, whose gain grows with the logarithm of the kill count.
- **Variation:** pitch varies by up to ±1 semitone. In palettes marked
  `tuned`, pitched sounds (bells, chimes, magic) are instead transposed to
  notes in the section's key. Each pitched sample records its `rootNote` in
  the palette so it can be transposed.
- **Normalization:** every sample is loudness-normalized at bake time.

### Mixing

The mix keeps the music in front while letting big impacts punch through.

- **Music bus:** the music gain dips 3–4 dB for 120 ms under heavy impacts.
  The dip is scheduled gain automation, so no live level detection is needed.
- **Effects bus:** sound effects go through a bus compressor. Both buses go
  through a final limiter.
- **Default level:** sound effects sit about 6 dB below the music's peak
  loudness.

## HUD and Titles

The HUD tells viewers this is a rhythm game and a Survivors game at once.
Every element pulses with the music in song time.

- **Beat track:** a NecroDancer-style track at the bottom of the screen.
  - Markers for the hero's upcoming actions slide toward a beating heart in
    the centre.
  - Heavy hits show as larger markers.
  - In `free` sections the markers become a flowing ribbon.
- **Combo counter:** counts on-beat hero actions. Play is perfect, so the
  combo never breaks. It changes color at milestones (50, 100, 250).
- **"PERFECT" callouts:** on heavy moves that land on accents, and on near
  misses.
- **XP bar:** fills from gem pickups. It reaches full exactly at each
  level-up, and once more when the finale blast's gems arrive.
- **Kill counter:** total kills so far.
- **Song progress:** elapsed time and a thin progress line marked with
  section boundaries.
- **Weapon icons:** each weapon's icon flashes when that weapon fires.
- **Health bar:** the hero's health bar is always full.
- **Title card:** piece, composer and performer, shown during the prelude.
- **Section banners:** stylized banners such as "THE KICK LINE" and "LORD OF
  THE REVEL" at key transitions.
- **End card:** totals for kills, combo and "PERFECT" callouts, then a
  scrolling credits roll generated from the credits list.

Floating damage numbers are not shown.

## Assets and Licensing

All sprite art is baked offline into atlases. The runtime never composites
layers.

### Baking characters

The Universal LPC generator has no command-line export, so the baker
reimplements the parts it needs. Its input is **character recipes**: small
JSON files that list body, head, clothing and weapon layers with color
choices.

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
  "animations": ["walk", "slash", "hurt", "idle"]   // hurt doubles as death
}
```

What the baker does:

- **Layers:** reads each item's definition from the generator's
  `sheet_definitions`, and stacks layers in the order given by each layer's
  `zPos` value (lower values are drawn behind).
- **Recoloring:** applies palette swaps using the generator's palette
  definitions.
- **Oversized frames:** handles weapon animations whose frames are 128 px or
  192 px, centring the 64 px body inside the larger frame.
- **Animation subset:** bakes only the listed animations, in the four facing
  directions (up, left, down, right).
- **Color variants:** produces variants such as the ember recolor of
  skeletons and orcs for the infernal zones.
- **Hit flash frames:** produces a white-silhouette copy of every frame.
- **Packing:** packs everything into atlases, with JSON metadata giving frame
  rectangles, anchors and impact frames.

Two LPC gaps need custom work:

- **Kick animation:** no LPC kick or dance animation exists, and the skeleton
  body has no jump or run animation. The baker builds a 3-frame kick from the
  skeleton's walk frames: stand, knee raised, leg extended. It does this by
  moving and redrawing the leg pixels with a scripted pixel edit. A human
  approves the result at a review gate. If the frames are not approved, the
  kick line instead alternates walk frames with a bob on each beat, plus
  squash and stretch.
- **Sword slash trails:** LPC has none. Slash arcs come from the CC0 Weapon
  Slash pack plus procedurally generated additive arcs.

### Licence policy and credits

- **Allowed licences:** CC0, CC-BY, OGA-BY and CC-BY-SA.
- **Disallowed licences:** GPL-only and LGPL-only art is rejected. The baker
  fails if a recipe or zone theme refers to it.
- **Licence sources:** for generator assets, the licence of each file comes
  from the generator's `CREDITS.csv`. For every other pack, a hand-maintained
  licence manifest in the repository lists each source file or file pattern
  with its licence, authors and URL. Any asset missing from both is an error.
- **Least restrictive option:** when an asset lists several licences, the
  credits record the least restrictive one.
- **Share-alike output:** baked or modified art derived from CC-BY-SA sources
  is itself CC-BY-SA. The code keeps the repository's own licence.
- **Credits file:** the baker writes a machine-readable credits file listing
  every source file used, with authors, licence and URL. It draws on:
  - the generator's `CREDITS.csv`
  - each pack's credits file
  - the effect and sound pack pages
- **On-screen credits:** the end card renders the credits file, including
  the recording's credit.

### Terrain and props

- **Forest base:** `lpc-revised` terrain, trees, mushrooms, wildflowers and
  plants (OGA-BY). Summer art is used for the twilight glade and autumn art
  for the mushroom grove.
- **Infernal look:** autumn tiles recolored to an ember palette, plus lava,
  lava rock, dead trees, braziers and ruins from the CC-BY-SA packs.
- **Light sources:** campfire and fireplace objects double as light sources.

## Automated Verification

Agents must be able to verify their own work. Besides unit tests, the project
provides these command-line checks:

- **Song map check:** the analysis tool's `check` command.
- **Content check:** validates every content file and level script against
  its schema. A set of deliberately broken fixtures must each fail with a
  useful message.
- **Plan check:** runs the planner in Node with all of its output checks, and
  prints a summary:
  - peak population
  - kills per section
  - total hit-stop time
  - longest stretch without a primary event
- **Determinism check:** plans twice and compares hashes. Also generates the
  map twice and compares.
- **Frame capture:** opens the demo in headless Chrome with a test hook that
  replaces the audio clock with a virtual one, then saves screenshots at
  exact song times.

```bash
npm run capture -- --times 12.0,35.2,88.31,141.5 --out captures/
```

The capture hook is a small object on the page that the test driver calls:

```ts
window.__demo = {
  ready: Promise<void>,                    // assets loaded, plan ready
  renderAt(s: number): Promise<void>,      // evaluate and draw song time s
  stats(): { frameMs: number[]; alive: number; particles: number },
};
```

- **Benchmark:** plays the whole song on the virtual clock at real-time speed,
  in headed (visible-window) Chrome on the reference machine. It records
  JavaScript frame time with `performance` marks, and GPU time with
  `EXT_disjoint_timer_query_webgl2` where available. It fails if a budget from [Rendering](#rendering) is
  exceeded at the 95th percentile, or if any frame exceeds 33 ms.
- **Asset check:** confirms that every recipe's licence is allowed, that
  every animation referenced in content exists in the atlases, and that every
  credit has an author and a URL.

Test hooks are enabled only by URL parameters and have no on-screen
interface.

## Can-Can Level Content

This section maps the viewer experience onto concrete level data. The bar
numbers come from the song map; the section roles below are what the
beat-map agent should identify.

In the table, "Population" is the target number of living enemies at the
start and end of the section. "New" lists what first appears there.

| Section role | Feel | Moveset | Zone | Time scale | Population | New |
|---|---|---|---|---|---|---|
| Slow prelude | free | spinning-glide | twilight-glade | 0.5 | 0 → 60 | bats, slimes, eyeballs, title card |
| Galop opening chords | pulsed | herald-cleaves | twilight-glade | 1.0 | 60 → 120 | goblins, orbiting blades |
| Theme A | pulsed | galop-steps | galop-woods | 1.0 | 120 → 250 | orcs |
| Theme A repeat | pulsed | galop-dashes | galop-woods | 1.0 | 250 → 320 | chain lightning, bee formations |
| Contrasting theme | pulsed | kick-line-dance | mushroom-grove | 1.0 | 320 → 350 | kick line of ember skeletons, archers |
| Bridge | pulsed | descent-steps | ember-hollow | 1.0 | 350 → 420 | fire trail, ember orcs |
| Theme A return (climax) | pulsed | revel-onslaught | infernal-grove | 1.0 | 420 → 500 | boss |
| Coda | pulsed | finale-frenzy | infernal-grove | 1.0, then 0.4 after the boss dies | 500 → 450 | boss death, finale blast |

The movesets:

- **spinning-glide:** glide on a figure-eight path. Spin every two beats,
  burst on strong anchors.
- **herald-cleaves:** step once per bar. Cleave on every tutti chord, slash
  on the other downbeats.
- **galop-steps:** step every beat on a zigzag path. Slash and backslash
  alternate on beats, with cleave on accents.
- **galop-dashes:** galop-steps, plus a dash-strike on the first beat of
  every second bar.
- **kick-line-dance:** step every bar on a circle path while the line
  dances. Slash on beats against the crowd, then sweep on the formation's
  release.
- **descent-steps:** step every beat, moving forward. Thrust and slash
  alternate, with dash-strikes on accents.
- **revel-onslaught:** dash every beat on a figure-eight path around the
  boss. Slash on beats, cleave on accents, plus dodges and counters around
  boss slams.
- **finale-frenzy:** revel-onslaught at full intensity, ending in the finale
  blast.

### Signature moments

- **First galop chord:** a hit-stop, a level-up and a zoom punch.
- **Kick line:**
  - 12 ember-recolored skeletons enter as one line formation and kick on
    each beat, alternating legs, for two phrases.
  - On the second phrase's final accent, the hero sweeps along the line.
  - Each skeleton dies on its own sixteenth-note hit as the hero passes it,
    about one second for the whole line.
- **Boss entrance:** the minotaur lands on a downbeat with a growing shadow.
  Nearby enemies get knocked back and a banner slides in.
- **Boss fight:**
  - The boss takes 8 planned hits across the climax.
  - It performs 4 telegraphed slams, each dodged on the beat and followed by
    a counter-attack.
  - It dies on the second-to-last tutti chord from a `finisher` move. The
    death has a 150 ms hit-stop, then a `timeScale` event ramps to rate 0.4
    for the rest of the song.
- **Finale blast:**
  - On the final chord, a ring expands from the hero across the screen.
  - Every remaining enemy (about 450) dies as the ring reaches it, on
    sixteenth-note hits spread over one beat.
  - The population drops to zero.

## Future Songs

The design must handle the five later songs with only new data: a song map,
a level script, and possibly new movesets, enemies and zone themes. Each song
stresses something different:

- **William Tell Overture (finale):** a sustained fast gallop. It tests long
  dense sections and how stable the crowd simulation is at the population
  cap.
- **Beethoven's Fifth (first movement):** dramatic pauses on held notes. Each
  pause should become a short `free` passage with slow motion, not a long
  hit-stop.
- **In the Hall of the Mountain King:** a steady accelerating tempo, marked as
  a tempo change in the song map. It tests beat-grid checks across a
  deliberate ramp, and enemy speeds that rise with tempo.
- **Flight of the Bumblebee:** constant sixteenth notes. It tests the sound
  voice budget and whether effects on every subdivision stay readable.
- **Ride of the Valkyries:** rubato passages with a weak beat. It tests `free`
  sections and anchors at length.

Code must not branch on song id. Any song-specific behavior belongs in
content data.

## Implementation Plan

Agents build the demo in the milestones below. Each milestone ends with
passing automated checks and a commit. Some also have a human review gate,
marked **Gate**.

1. **Project setup.** No dependencies.
   - Vite + TypeScript + PixiJS v8 app, ESLint, Vitest, Playwright, plus a
     Python project for the analysis tool.
   - Check: lint, type-check and test commands run clean.
2. **Song map for the overture.** Depends on 1.
   - The analysis tool and the beat-map agent workflow, producing the FLAC
     excerpt and the song map.
   - Check: `check` passes.
   - **Gate:** a human listens to the click track and confirms the beats and
     section boundaries.
3. **Synced metronome runtime.** Depends on 2.
   - The audio clock, the title screen, the HUD beat track, and a
     full-screen flash on every beat, kept in later builds behind the
     `?metronome` URL parameter.
   - Check: frame captures at known beat times show the flash.
   - **Gate:** a human confirms that the flash looks locked to the music.
     This is the early end-to-end integration check.
4. **Content and level script loading.** Depends on 1.
   - Schemas, loaders, inheritance of section fields, and error reporting.
   - Check: the content check passes, including the broken fixtures.
5. **Asset baking.** Depends on 1.
   - The hero, every roster enemy, ember variants, the kick frames, hit
     flash frames and the credits file.
   - Check: the asset check passes.
   - **Gate:** a human reviews a contact sheet of every baked animation and
     approves or rejects the kick frames.
6. **Map generation.** Depends on 4 and 5.
   - The route schedule, route, zones, props and landmarks.
   - Check: the map determinism check passes, and frame captures show each
     zone.
   - **Gate:** a human reviews the zone captures.
7. **Planner core.** Depends on 2, 4 and 6.
   - Musical time, actions, hero motion, camera, kill assignment, crowd
     simulation, and evaluation and rendering with plain sprites.
   - Check: the plan check and determinism check pass.
8. **Time warp.** Depends on 7.
   - Slow motion, near-miss dips, hit-stops and the world beat grid.
   - Check: the plan check passes, including hit-stop clearance.
9. **Juice.** Depends on 5 and 8.
   - Every effect recipe, lighting, post-processing, decals and gems.
   - Check: the benchmark passes.
   - **Gate:** a human reviews captures of the biggest moments.
10. **Sound.** Depends on 7.
    - Sound palettes, scheduling, the voice budget and the mix.
    - Check: the plan check's sound budget passes.
    - **Gate:** a human listens to the whole level.
11. **Weapons, formations and threats.** Depends on 8.
    - Level-ups and weapons, formations and the kick line, archers, the boss
      and the finale blast.
    - Check: every plan check passes.
12. **HUD, titles, credits and final polish.** Depends on all the above.
    - Check: every automated check passes, and frame captures exist for the
      start of every section.
    - **Gate:** the full Manual QA below.

## Manual QA

A human runs this QA by watching the demo in desktop Chrome at 1920×1080,
with wired headphones or speakers. Bluetooth audio adds latency; use
`?avOffsetMs=` to compensate if needed.

### First check: the synced metronome

Before reviewing anything else, confirm the path from audio to picture.

- **Start.** Open the page with `?metronome` and click "Click to begin".
- **Beat flash.** The screen flashes on every beat and the HUD heart beats.
  Both must look locked to the music from the first beat to the last, with no
  gradual drift.
- **Slow-motion camera test.** Film the screen and speaker with a phone's
  slow-motion camera during a loud accent. The flash should be within one
  frame (about 17 ms) of the sound.

### Full playthrough

Watch the whole level at least twice and confirm:

- **Prelude.** The prelude feels calm. The hero glides, the world moves in
  slow motion, and kills are soft. The title card is readable.
- **First chord.** The first galop chord snaps to full speed with an obvious
  hit-stop and level-up.
- **On the beat.** Every hero impact lands on the beat, not just before or
  after it.
- **Ensemble rhythm.** Monster groups visibly move on different
  subdivisions, and the horde looks like it is dancing.
- **No ghost hits.** No slash passes through a monster it doesn't damage.
- **Spawning.** No monster pops into view without an entrance effect.
  Monsters emerge from the forest edges.
- **Kick line.** The skeleton kick line is recognizably a can-can. The
  hero's sweep starts on the phrase's final accent and cuts the skeletons
  down one after another.
- **Archers.** Archer arrows visibly miss by a hair, with a "PERFECT"
  callout.
- **Boss.** The boss's slams are telegraphed and dodged on the beat. The boss
  dies on the second-to-last chord.
- **Finale.** The finale blast clears every monster within a beat of the
  final chord.
- **Zones.** The scenery turns from twilight forest to infernal grove in step
  with the music's rising energy.
- **Sound.** Sound effects feel like part of the music. No impact lands just
  off the beat, and the climax does not become a wall of noise.
- **HUD.** The XP bar fills exactly at each level-up and on the final chord.
  The kill counter and combo climb steadily.
- **Credits.** The end card shows totals and the credits roll, including the
  recording credit and share-alike notices.

### Edge cases

- **Hidden tab.** Switch tabs for 10 seconds during the climax, then come
  back. The picture must jump straight to the right moment and stay in sync,
  with no burst of late sounds.
- **Window resize.** Resize the window mid-playback. The view rescales without
  losing sync or blurring the pixel art.
- **Cached plan.** Reload the page. The second load uses the cached plan and
  produces the same playthrough.
- **Broken level script.** Introduce a deliberate error in the level script,
  such as a bar range outside the song. Loading must show a clear error and
  must not start playback.
- **Performance.** Run on the reference machine with Chrome's FPS meter
  turned on. The climax must hold 60 fps.

### Test hooks for QA

- `?metronome` adds the beat flash and a click track over the music.
- `?capture&t=88.31` renders one frozen frame at the given song time,
  without audio, to inspect a specific moment.
- `?avOffsetMs=-40` shifts the picture relative to the audio, for unusual
  audio hardware.
- `?seed=1859` re-plans, and regenerates the map, with a different seed. The
  level should look different but still pass every plan check.
