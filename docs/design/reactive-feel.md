# Reactive Feel: Any Song, Its Own Music Video

## Introduction

[reactive-audio.md](reactive-audio.md) builds reactive mode: a mode that plays
to music the game has not analysed in advance, staged towards the authored
Galop. Its stages 1 to 6 have landed, and so has a fair-judging revision.
Stage 7, `hv-s4pt.11`, is tuning the reactive Galop's difficulty.

That plan measures everything against one song, the Galop. This plan runs
**alongside** it and asks a wider question: does reactive mode work on songs
nobody tuned it for?

The goal of reactive mode is that it can listen to *any* song playing on the
computer and build a complete rhythm game from it:

- **Mechanically,** the player gains power by following the beat, mostly
  through attacks that get bigger.
- **Aesthetically,** every element feels like a music video built for that
  song. The UI, particles, sounds, combat, movement, animations and the hero
  all move with the music and form one harmonious whole.

This plan gets there in two steps. First comes a **fast lane**
([The Fast Lane](#the-fast-lane)): one session of visible fixes using data the
stream already has, checked by rendering frames in seconds, with no rating
pipeline. Then comes a measured **improvement loop**, re-ranked by what the
fast lane shows:

1. **A corpus** of new songs: the CC0 NotJam Music Packs, plus about 12
   CC0/CC-BY tracks in other genres. Each track is either an **iteration**
   track, used to tune, or a **held-out** track, used only to test.
2. **Ground truth** for every track: a beat grid fitted to it and checked
   automatically.
3. **A feel dossier** per track: metrics, beat-phase contact sheets, structural
   stills and a timeline. **Rater subagents** score each dossier against a
   fixed rubric.
4. **Rounds:** rate the iteration set, triage the findings, fix the top
   issues, rate again. Every third round is a **milestone** that also scores
   the held-out set.
5. **Foundation work** the loop needs anyway:
   - richer music signals in the belief stream: bands, brightness, musical
     bars and phrases;
   - a signal bus and modulation matrix that bind music to every visual
     element;
   - a per-song identity;
   - sounds that follow the song;
   - pacing that works for songs of any length.

Terms used throughout. Terms from reactive-audio.md (belief stream,
`knownAt`, committed beat, evidence, the gate, judged time) keep their
meanings.

- **Corpus:** every track this plan measures, in three splits:
  - **iteration:** tracks used to tune;
  - **held-out:** tracks used only to test;
  - **regression:** the Galop, the classical tracks and the synthetic tracks
    already shipped.
- **Fitted grid:** a track's reference beats, bars and meter, fitted from a
  whole-song offline analysis ([Ground Truth](#ground-truth)).
- **Dossier:** the generated bundle a rater reads for one track
  ([The Dossier](#the-dossier)).
- **Rater:** a fresh, read-only subagent that scores dossiers against
  [the rubric](#the-rubric).
- **Round:** one pass of rate, triage and fix ([Rounds](#rounds)).
- **Milestone:** every third round, which also scores the held-out set.
- **Signal:** a named, causal, per-frame value derived from the stream, such
  as `kick` or `barPhase` ([The Signal Bus](#the-signal-bus)).
- **Binding:** one row of the modulation matrix: a signal driving one element
  parameter.
- **Identity:** a song's chosen look and sound: its palette family, zone
  order, motion style and sound family ([Song Identity](#song-identity)).
- **Reveal:** the moment a song's identity is committed, with a flourish.
- **Musical bar:** a bar of the song's detected meter (2, 3 or 4 beats). This
  is separate from the simulation's fixed 2-beat bars, which stay as they are.

### Where reactive mode stands

Gameplay as of master `505adf7`: the fair-judging revision (`hv-s4pt.12`)
and the beatless-credit revision (`hv-s4pt.13`). This plan builds on these
rules, not the ones before them:

- **No free movement.** Strides and the beat display stay on throughout.
  While the beat is unclear, presses stride unjudged and the groove holds.
  The beat track's ticks soften with the beat's clarity, down to 30%.
- **Judging pauses and resumes.** It stops at the first tick below the gate
  and resumes after 1 s of clear beat. The drain then waits its grace bar
  again.
- **Readings** come only from judged presses since judging resumed, restart
  from "as tracked" after an unclear stretch, and lean towards "as tracked".
- **The benefit of the doubt:** a settle never lowers a steady player's
  provisional rating.
- **Penalties and the drain scale with the tracker's confidence,** from none
  at the gate to all of it at evidence 0.55. A non-steady player's raises
  scale the same way, so random pressing gains nothing where the tracker is
  unsure.
- **Beatless credit** (`hv-s4pt.13`): each committed beat 2 s or more into a
  stretch without judging raises the groove by a Perfect's worth (0.08). A
  full meter takes about 13 beats, and the credit doesn't count as a raise for
  the drain.
- **The lock-on grace** (`hv-s4pt.13`): for the first 8 s of judged time after
  4 s or more without judging, a steady player loses nothing. Anyone else's
  settled penalties count half.
- **The grade** averages the meter as shown from 16 s on, judged or not, with
  beatless stretches filled by the credit.
- **A new gate: mashing never beats playing.** On every shipped track, with
  damage off, no masher and not the holder beats `reactive-moderate`'s average
  groove or its judged time at tier 2 or higher.
  - A side effect: the credit carries a masher into each judged stretch at a
    full meter.
  - So the Galop's stage 2 target, mashers at tier 0 for 80% of judged time,
    now misses (76% and 79%).
- **Rings close in world time.**
- **`npm run reactive:replay`** replays a play log's presses under the
  current rules: a fast way to test mechanics against real playtests.

Stage 7 (`hv-s4pt.11`) is in progress again, in `.worktrees/reactive-stage7`:

- It sweeps hero health and the director's population (`start`, `perMinute`,
  `max`) in `levels/reactive.yaml`.
- It edits `src/content/schema.ts`, `src/reactive/players.ts` and
  `src/sim/player.ts`.
- Its remaining work: reactive grade thresholds, Watch the demo, the parity
  pass and stage 7's targets.

Already reacting to the music, for the fast lane to build on:

- **Pulses on committed beats:** the hero's aura, props, gems, light, the
  vignette and the HUD.
- **Strong onsets:** a flash and a camera punch.
- **Forecast accents:** heavy strikes and their punches.
- **The loudness level:** camera, sound palette, population and calm.
- **Detected sections:** zone waves, rosters and banners.

Not reacting yet:

- **Enemies' bodies:** only formations dance.
- **Onsets in general:** non-strong onsets and onset strength drive nothing.
- **The key:** it colours only the gem chimes.
- **Per-song look:** none. Every song gets the same zones, palettes and
  constants.

### The plan in brief

| Phase | Beads | Can start | What it adds |
|---|---|---|---|
| 0. Fast lane | Q | Now, alongside `hv-s4pt.11` | Enemy beat motion, onset texture, key colour, a stand-in identity, legible reward; a mini-corpus of 6 tracks |
| A. Corpus and measurement | F1–F5 | After Q | NotJam corpus, fitted grids, CC shortlist, dossiers and metrics, rubric and round tooling |
| B. Baseline | R0 | After `.11` and F5 | Round 0 rates the engine as the fast lane left it |
| C. Music signals | E1, E2 | After F2 | Bands, brightness, timbre; causal meter, downbeats and phrases |
| D. Feel foundation | G1–G5 | After `.11` | Signal bus and matrix, identity, length-blind pacing, sounds, a song-sampling spike |
| E. Rounds | R1, R2, … and milestones | After phase D | Rater-driven fixes until the rubric plateaus |

Phase D's beads are seeded from known gaps. The fast lane's findings, then
Round 0's, re-rank and rescope them before they start
([Seeded work](#seeded-work)).

**How long:** the fast lane is one session, with no step waiting more than a
couple of minutes. Phases A to D are several days of beads, and each rating
round takes about an hour, because raters run one at a time. The fast lane
exists so that useful changes land before any of that.

### Decisions

These were settled with the user while designing. The executor treats them
as fixed.

- **A fast lane comes first** (bead Q). It makes code-only changes from data
  the stream already has, checked with rendered frames, before the corpus,
  the raters and the new analysis. Every other bead waits for it, so its
  findings can re-rank them.
- **Analysis is cheap enough to run in parallel.** The stream command runs at
  about 0.65× real time on one core, so the corpus regenerates in minutes
  across cores. No cache is needed ([Stream regeneration
  cost](#stream-regeneration-cost)).
- **Scope:** corpus, engine and feel. Live audio capture stays out of scope.
  This plan stays out of the director's difficulty, healing, grade and demo
  until stage 7 lands, then coordinates through serialization edges.
- **An improvement loop:** engine changes, then rater subagents scoring both
  aesthetic and mechanical feel, round after round.
- **Raters read a feel dossier:** metrics, contact sheets, stills and a
  timeline. They never get audio. The user gets a mixed-audio file.
- **Ground truth is a fitted grid, verified automatically.** A track whose fit
  fails is flagged, not guessed.
- **The split** is the one in [The Split](#the-split): 23 iteration and 16
  held-out NotJam tracks. Near-duplicates stay on one side.
- **The held-out set is scored only at milestones, as numbers only.** No
  change may cite a held-out track as its reason
  ([Held-out Protocol](#held-out-protocol)).
- **No approval points.** The user approved the whole plan up front
  (2026-10-23). Executors never stop to ask. Every choice this document once
  left to the user is now the executor's: the CC shortlist, the spike's
  verdict, and what to do when the loop overfits or plateaus. Each such
  choice is recorded with its reasons in the bead's notes.
- **Extra CC music:** about 12 CC0 or CC-BY tracks in genres NotJam lacks,
  chosen, verified and ingested by the executor.
- **The rubric** has 10 dimensions with written anchors and paired metrics,
  and is calibrated against the authored Galop.
- **Rounds are beads.** Raters run **one after another**, never in parallel,
  to protect the usage limit. There is one rater per batch, plus a second
  rater only for the anchor batch and disputed tracks.
- **Pacing is structural and blind to length.** The director's constants
  become musical units. Nothing reads `audio.duration` except the run's end.
- **New stream features must be causal and plausibly real-time.** The bar is
  "an optimised Rust rewrite could run this live". Each feature reports its
  real-time factor.
- **Aesthetics run through a signal bus and a data-driven modulation matrix,**
  not hand wiring.
- **Identity:** a neutral intro, a reveal once enough is known, then
  evolution within the chosen family.
- **The backlog:** Round 0 sets the baseline first. The seeded foundation work
  follows, re-ranked by Round 0's findings.
- **Mechanics:** corpus-wide targets, plus stage 7's Galop results as
  regression targets.
- **Sounds:** pitched to the detected key and mode, quantized to the beat, and
  ducked under the song. A timbre-matched SFX library picks sounds per song,
  and a time-boxed spike tries sampling the song's own drum hits.

### Goals

- Reactive mode plays every corpus track from start to end with no errors.
- On the iteration set, each rubric dimension's mean rises from Round 0, and
  each paired metric moves towards its target.
- The held-out set improves too, and its gap to the iteration set does not
  grow beyond [the overfitting rule](#held-out-protocol).
- Two different songs produce visibly and audibly different games.
- Following the beat visibly makes the player stronger on every track with a
  clear beat.
- Causality, determinism and play mode's golden hashes hold throughout, as in
  reactive-audio.md.

### Non-goals

- Live analysis and audio capture (see reactive-audio.md's
  [Beyond This Plan](reactive-audio.md#beyond-this-plan)).
- New art. Identities recolour, relight and reorder what exists; new zone art
  and enemy skins are [later work](#beyond-this-plan).
- Playlist runs spanning several songs (later work).
- Changing play mode in any way.

## Related Information

- [reactive-audio.md](reactive-audio.md): the reactive mode this plan
  improves. In particular:
  - [The Belief Stream](reactive-audio.md#the-belief-stream);
  - [Causality](reactive-audio.md#causality);
  - [Automated Checks](reactive-audio.md#automated-checks);
  - [Virtual Players](reactive-audio.md#virtual-players);
  - the measured stage results in its appendix.
- [gameplay-redesign.md](gameplay-redesign.md): press classes, ratings,
  tiers, weapon sizes and
  [Sound on the Grid](gameplay-redesign.md#sound-on-the-grid).
- [README.md](../../README.md): commands and URL parameters.
- Beads: epic `hv-s4pt` (reactive mode); this plan's epic is `hv-jtsz`.
- Code this plan touches:
  - [tools/analysis/src/ps_analysis/stream.py](../../tools/analysis/src/ps_analysis/stream.py),
    `structure.py`, `accents.py`, `click.py` and `cli.py`: the stream
    command, and the click track reused for spot checks.
  - [src/reactive/tracks.ts](../../src/reactive/tracks.ts): the track index
    schema (`TRACK_GROUPS`).
  - [src/reactive/director.ts](../../src/reactive/director.ts) and
    [src/reactive/setpieces.ts](../../src/reactive/setpieces.ts): the
    director's time constants that [pacing](#length-blind-pacing) rewrites.
  - [src/plan/evaluate.ts](../../src/plan/evaluate.ts) (`reactiveFx`,
    `pulsed`, `pulseScale`),
    [src/runtime/renderer.ts](../../src/runtime/renderer.ts) and
    [src/runtime/hud.ts](../../src/runtime/hud.ts): today's hand-wired
    reactive aesthetics, which [the matrix](#the-modulation-matrix) absorbs.
  - [src/planner/emit.ts](../../src/planner/emit.ts) and
    [src/live/recording.ts](../../src/live/recording.ts): sound palettes,
    `soundFade`, `keyScale`.
  - [levels/reactive.yaml](../../levels/reactive.yaml): the director block,
    gaining `look:` and musical-unit pacing.
  - [scripts/capture.ts](../../scripts/capture.ts),
    [scripts/qa.ts](../../scripts/qa.ts),
    [scripts/reactive-parity.ts](../../scripts/reactive-parity.ts) and
    [scripts/reactive-streams.ts](../../scripts/reactive-streams.ts): the
    tooling the dossier builds on.
- Source audio, all in `~/Downloads` on the developer's machine:
  - `NotJamMusicPack/Standalone/`: pack 1, CC0, 22 tracks.
  - `not-jam-music-pack-2-wav.zip`, folder `WAV/Standalone/`: pack 2, CC0,
    19 tracks.
  - `not-jam-music-pack-2-sfx.zip`: 42 CC0 sound effects.
  - The folder `NotJamMusicPack 2` is a copy of pack 1, not pack 2. Ignore it.

## The Fast Lane

One bead, Q, of about a session. It makes the changes that are visible in
seconds and need no new analysis, no corpus tooling and no raters.

### Mini-corpus

- **Six iteration tracks** chosen to span the corpus:
  - `BreakbeatChips`: slow breakbeat;
  - `KleptoLindaTitles`: fast;
  - `TypeCastTheme`: very wide dynamics;
  - `waltz_of_the_lich_king`: 3/4;
  - `DarkCavern_PhoneHome`: sparse and ambient;
  - `lazy_river_rally`: the fastest.
- **Plus the Galop,** which keeps its song map.
- **Added as local tracks** with the existing `npm run reactive:add`, which
  writes a stream with an offline reference to gitignored `reactive-local/`.
  - Run the six at once in parallel shells. That takes about 2 minutes.
  - These are iteration tracks, so they don't touch the held-out protocol.

### The feedback loop

- **Beat sheets:** a `--beat-sheet <t>` option for `npm run capture` that
  renders 16 frames, at phases 0, 0.25, 0.5 and 0.75 of 4 consecutive committed
  beats from `t`, tiled into one captioned image with `sharp`.
  - Each sheet takes about 20 to 30 s.
  - If capture does not yet work with `?mode=reactive&track=<id>`, making it
    work is the bead's first step.
- **The executor looks at the sheets itself,** comparing before and after. There
  is no rater pipeline in the fast lane.
- **Mechanics:** use `reactive:parity` and `reactive:replay` (on the user's
  newest play log) after any change that could touch the reward.

### The changes

Each change is small and hand-wired. G1 migrates them into the matrix later.

1. **Enemy beat motion:**
   - a squash and bob on committed beats, faded by evidence;
   - a flinch on onsets, scaled by strength;
   - phases staggered by enemy type, so the horde reads as dancing rather than
     vibrating.
2. **Onset texture:** every onset, not only strong ones, gives a small
   particle burst and light flicker, scaled by strength and faded by evidence.
   Strong onsets keep their flash.
3. **Key and mode colour:**
   - the light grading's hue follows the detected key, around the circle of
     fifths;
   - minor keys grade darker and cooler;
   - the colour changes only at detected section starts, so it never
     flickers.
4. **A stand-in identity:** at the first detected section start, or at 30 s,
   choose a zone-order rotation and a palette tint from a hash of the key,
   the mode and a tempo band. It uses no new stream data, and G2 replaces it.
5. **A legible reward:** compare `reactive-moderate` and `mash4` at the same
   moments in beat sheets. If the tiers don't read, scale strike trails and
   impact effects by tier in the renderer. The simulation's weapon sizes
   don't change.

### Constraints

- **No stream changes and no simulation changes.**
  - Stage 7 is active in `.worktrees/reactive-stage7`. It edits
    `src/sim/player.ts`, `src/reactive/players.ts` and
    `src/content/schema.ts`, and tunes the `hero` and `director` keys of
    `levels/reactive.yaml`. Stay out of all of those.
  - Put the fast lane's knobs in code constants, or in a new top-level key of
    `levels/reactive.yaml` (an additive edit).
  - Small textual collisions are rebased with `tg update`.
- **Causal:** every new frame effect reads only stream entries known at the
  frame's time. The existing perturbation check compares frames and enforces
  this.
- **Play mode unchanged:** golden hashes, `check:play` and `check:seeds`.
- **Deliverable:**
  - before and after beat sheets for three tracks, attached in the bead's
    notes;
  - a per-track list of what still looks wrong;
  - suggested re-ranking of this epic's beads, written into the epic's notes.

## The Corpus

### Sources

- **NotJam pack 1:** the 22 `Standalone/` tracks, CC0.
- **NotJam pack 2:** the 19 `WAV/Standalone/` tracks, CC0.
- **Excluded:**
  - every `_Loopable` file: these duplicate the standalone tracks;
  - `let_us_begin` (3 s) and `you_died` (8 s), which are jingles.
- **CC tracks:** about 12, picked by bead F3
  ([CC music](#cc-music)).

The NotJam tracks are all chiptune by one composer, mostly at a steady tempo
and mostly short. A rough profile (librosa; the tempo may be off by a factor
of two):

- **Tempo:** about 80 to 160 beats per minute.
- **Length:** 24 to 178 s, half of them under 60 s.
- **Edge cases worth having:**
  - a waltz;
  - sparse ambient pieces;
  - tracks with long silences or fades;
  - a few unsteady beat readings.

That narrowness is why the CC tracks exist.

### The split

The split keeps near-duplicates together:

- Caverns A/B;
- Mountain A/B;
- the battle pair;
- the lich-king pair.

It also balances tempo, density and dynamics across the two sides.

**Iteration (23):**

| Track | Pack | Rough profile |
|---|---|---|
| BreakbeatChips | 1 | ~83 bpm, breakbeat, 36 s |
| ChillMenu | 1 | ~89 bpm, 80 s |
| CriticalTheme | 1 | ~108 bpm, dense, 110 s |
| DarkCavern_PhoneHome | 1 | ~129 bpm, sparse ambient, 178 s |
| DragAndDreadTheme | 1 | ~112 bpm, dense, 76 s |
| KleptoLindaCavernsA, KleptoLindaCavernsB | 1 | ~144 bpm, variants, 103 s |
| KleptoLindaTitles | 1 | ~144 bpm, 45 s |
| MeltdownTheme | 1 | ~129 bpm, 150 s |
| PitcherPerfectTheme | 1 | ~144 bpm, 81 s |
| SwitchWithMeTheme | 1 | ~129 bpm, 91 s |
| TypeCastTheme | 1 | ~118 bpm, very wide dynamics, 61 s |
| UntitledTrack01 | 1 | ~103 bpm, 47 s |
| battle_commences, battle_ceases | 2 | ~103 bpm, pair, 69 s and 38 s |
| lazy_river_rally | 2 | ~162 bpm, 51 s |
| leading_the_charge | 2 | ~129 bpm, 111 s |
| lich_king_ascendant, waltz_of_the_lich_king | 2 | pair, includes a waltz, 49 s and 82 s |
| locrian_chipdoom | 2 | ~144 bpm, 137 s |
| rainy_foothills | 2 | ~89 bpm, unsteady reading, 45 s |
| safe_space | 2 | ~118 bpm, fades and silence, 38 s |
| the_wall | 2 | ~108 bpm, 51 s |

**Held-out (16):**

| Track | Pack |
|---|---|
| CrashingOut | 1 |
| DescendGameplay | 1 |
| HaroldParanormalInstigatorTheme | 1 |
| KleptoLindaCredits | 1 |
| KleptoLindaMountainA, KleptoLindaMountainB | 1 |
| SeeingDouble | 1 |
| TitleTheme_PhoneHome | 1 |
| VictoryLap | 1 |
| baroque_chips | 2 |
| crystal_caverns | 2 |
| grumpy_shopkeep | 2 |
| peak_performance | 2 |
| pool_party | 2 |
| really_long_arms | 2 |
| solemn_advance | 2 |

The held-out table deliberately lists no profiles. Executors should not study
them.

**Regression:** the shipped tracks keep their current roles:

- `orpheus-galop`, the main reference and the rubric's anchor;
- `william-tell-finale`, `mountain-king`, `bumblebee` and `beethoven-fifth`;
- the synthetic controls.

A round rates the Galop. The other regression tracks feed the metrics only.

### CC music

Bead F3 researches and picks about 12 tracks, verifying each licence on its
source page. Bead F3b downloads and ingests them. There is no approval step.

- **Licences:** CC0 or CC-BY only, because the repository is public under
  Apache 2.0. Every CC-BY track is credited in `corpus/CREDITS.md` and in the
  track index's `credit` field. Never NC, ND or SA.
- **Length:** 2 to 5 minutes each, so pacing meets real song lengths.
- **Genre slots,** one or two tracks each:
  - vocal pop with verse and chorus structure;
  - rock with live drums;
  - hip-hop, half-time feel;
  - house or EDM with builds and drops;
  - swing or jazz;
  - acoustic music with tempo drift;
  - beatless ambient;
  - odd meter (5/4 or 7/8);
  - metal.
- **The list,** recorded in F3's notes, gives for each track:
  - the source page;
  - the licence, with a link to it on that page;
  - the artist, title and length;
  - its genre slot;
  - why it was picked.
- **The split:** about half iteration, half held-out, balanced across the genre
  slots.
- **Downloading** happens in F3b. Each file goes into its own new, empty
  scratch directory before ingest. A track whose licence can't be confirmed on
  its source page is skipped and replaced.

The same list covers **extra SFX packs** for the
[SFX library](#the-sfx-library): CC0 or CC-BY sound packs, such as Kenney's
CC0 audio packs, chosen and verified the same way.

### Where the corpus lives

- **Audio:** `public/reactive/<id>/<id>.ogg`, Ogg Vorbis at quality 4.
  - The stream is computed from the shipped Ogg, as for the synthetic tracks,
    so encoder delay cannot shift it.
  - All of NotJam is about 46 minutes, roughly 40 to 50 MB.
- **Stream and reference:** `public/reactive/<id>/stream.json` and
  `reference.json`. A stream is about 50 KB gzipped per 2.5 minutes, so
  regenerating the whole corpus adds a few MB to history.
- **Ids:**
  - `notjam-<kebab-name>`, such as `notjam-breakbeat-chips`;
  - `cc-<kebab-artist>-<kebab-title>` for the CC tracks.
- **The manifest,** `corpus/manifest.yaml`, is the one source for split
  membership:

  ```yaml
  tracks:
    - id: notjam-klepto-linda-caverns-a
      source: NotJamMusicPack/Standalone/KleptoLindaCavernsA.wav
      title: "Klepto Linda: Caverns A"
      artist: NotJam
      licence: CC0-1.0
      split: iteration
      family: klepto-linda-caverns   # near-duplicates share a family and a split
  ```

- **The index:** `public/reactive/index.json`.
  - `TRACK_GROUPS` gains `notjam` and `cc`.
  - Each track gains optional `split`, `licence` and `credit` fields.
  - A unit test checks the index against the manifest, and checks that every
    family has a single split.
- **The title screen** lists the corpus under its groups.
  - Held-out tracks are playable: the music is public and the split is about
    tuning, not secrecy.
  - If the user reports how a held-out track felt, that report goes into the
    next milestone's notes, not a fix bead.
- **Ingest:** `npm run reactive:corpus-add -- --source ~/Downloads` (always from `corpus/manifest.yaml`)
  does the following:
  - encodes each missing track;
  - writes its stream;
  - writes its fitted grid (from F2 on);
  - updates the index.

  It is idempotent: a second run changes nothing.

### Stream regeneration cost

Every stream-changing bead regenerates every shipped stream.

- **Measured** on 2026-10-23: the stream command, hindsight included, took
  23 s for the 36 s `BreakbeatChips`. That is about 0.65× real time on one
  core.
  - reactive-audio.md's estimate of one to two minutes per minute of music
    does not hold for these tracks.
  - All of NotJam (about 46 minutes) is about 30 minutes in serial.
- **F1 adds `--jobs <n>`** to `reactive:streams` and `reactive:corpus-add`,
  defaulting to two thirds of the cores, longest tracks first. Measured on
  2026-10-23 over all 50 shipped streams (an 18-core M-series Mac): 601 s with
  the 6 performance cores, 358 s with 12 jobs. Under parallel load a track
  streams at about 1.1× real time, not 0.65×.
- **No cache.** Revisit only if a later group makes regeneration take longer
  than 15 minutes in parallel.

## Ground Truth

### The fitted grid

A new command fits each track's reference:

```sh
npm run analysis -- fit-grid public/reactive/notjam-victory-lap/notjam-victory-lap.ogg \
  --out public/reactive/notjam-victory-lap/reference.json --click captures/corpus/notjam-victory-lap-click.wav
```

1. **Decode offline:**
   - madmom's offline beat model and tracker over the whole file;
   - its downbeat model (`RNNDownBeatProcessor` with
     `DBNDownBeatTrackingProcessor`, `beats_per_bar=[2, 3, 4]`).

   Whole-song analysis is allowed here, because only the virtual players,
   the metrics and the raters read references.
2. **Fit a constant grid.** For each candidate metrical level of the decode
   (1×, 2×, ½×, and ⅔× for triplet feels), least-squares fit a period and a
   phase to the beats, and snap the onsets near each grid point.
3. **Choose the level** whose period is between 0.4 and 0.75 s (80 to
   150 bpm), breaking ties by onset support. This is the level a player most
   likely taps. The metrics also report results at any metrical level.
4. **Accept the fit** if the median absolute residual is at most 8 ms, the
   90th percentile at most 20 ms, and at least 90% of decoded beats fall
   within 45 ms of the grid. If not:
   - **Piecewise:** split at tempo changes found by residual drift, and fit
     each piece. Accept it under the same limits per piece, with at most 4
     pieces.
   - **Failed:** write the reference with `fit: "failed"` and the raw decode.
     The track is left out of beat metrics. The corpus report lists it with its
     click overlay. The executor may retry with other fit parameters, and
     records the outcome.
5. **Fit the meter:** downbeats come from the downbeat decode's phase, voted
   over the fitted grid. A meter with under 70% agreement is marked
   `meterConfidence: "low"`, and downbeat metrics skip it.
6. **Find approximate sections:** offline whole-song segmentation
   (Laplacian segmentation of a beat-synchronous recurrence matrix), snapped
   to the nearest fitted downbeat. These are marked `approximate: true` and
   used only for structure targets, with reactive-audio.md's 8 s tolerance.
7. **Write a click overlay:** `click.py`'s click over the track, with
   downbeats and section starts accented. It goes to gitignored
   `captures/corpus/` for optional spot checks by ear.

### The reference format

```json
{
  "source": "fitted",
  "fit": "constant",
  "bpm": 136.0,
  "residualMs": { "median": 3.1, "p90": 9.4 },
  "beats": [0.412, 0.853],
  "meter": 4,
  "meterConfidence": "high",
  "downbeats": [0.412, 2.176],
  "sections": [{ "s0": 0.412, "s1": 14.53, "approximate": true }],
  "accents": []
}
```

- The stream's `reference` field carries the same object, as the existing
  references do.
- `reference.json` stays beside the stream for the tools.
- Virtual players press on `beats`. `reactive-skilled` also presses on
  `accents`, which are the offline strong onsets that fall on grid points.
- The Galop keeps its song map as its reference.

## Music Signals

New stream fields, each a group with its own version, a causality test, and a
reported real-time factor ([Live-readiness](#live-readiness)).

### Bands, brightness and timbre (E1)

- **Band envelopes:** each tick gains `bands: { kick, snare, hat, bass }`, each
  from 0 to 1.
  - Each is the band energy of a fixed filterbank on the causal spectrogram:
    - kick: 40–120 Hz transients;
    - bass: 60–250 Hz sustained;
    - snare: 150–400 Hz plus 2–5 kHz noise;
    - hat: 6–16 kHz.
  - Each is normalised against its own decaying running maximum, as `energy`
    is.
  - They are named for their role, not their instrument: a chiptune's noise
    channel lands in `hat` and `snare`.
- **Band onsets:** each onset gains `band`, the band with the largest share of
  its spectral flux.
- **Brightness:** each tick gains `brightness`, the spectral centroid over the
  last 0.5 s, standardised against the past 30 s and squashed to 0–1.
- **Timbre:** every 1 s, a `timbre` entry
  `{ knownAt, brightness, flatness, percussive, density }` over the past 10 s:
  - `flatness`: spectral flatness, so noisy against tonal;
  - `percussive`: the share of energy in a causal median-filter
    harmonic–percussive split;
  - `density`: onsets per second.

  The [identity](#song-identity) and the [SFX library](#the-sfx-library) read
  these.

### Musical bars and phrases (E2)

The simulation keeps its 2-beat bars, because its "next downbeat" reads must
stay within the commit horizon (reactive-audio.md,
[Committed beats](reactive-audio.md#committed-beats)). Musical bars are a
separate, aesthetic layer.

- **Meter and downbeat:** for each committed beat, collect the low-band onset
  strength, the chroma change and the bass onset at that beat. Over the past
  16 bars, score the meters 2, 3 and 4 and every downbeat phase by how strongly
  these features peak on the candidate downbeats.
  - **Switching:** the incumbent is kept unless a challenger leads by a margin
    for 4 bars in a row. This mirrors the reading choice's hysteresis.
- **Entries:** `bars: [{ t, knownAt, meter, confidence }]`.
  - `t` is a committed beat chosen as a downbeat.
  - `knownAt` is the later of that beat's `knownAt` and the evidence deciding
    it.
  - A bar is committed only if `t >= knownAt + 0.1 s`, as for beats.
  - Below the gate, no bars are committed.
- **Phrases:** `phrases: [{ t, knownAt, bars, confidence }]`, on downbeats
  where an 8- or 4-bar phrase starts.
  - Evidence: chroma self-similarity at lags of 4 and 8 bars, plus structure
    changes that fall on a candidate phrase start.
  - The phrase length is held with the same hysteresis.
- **Targets on the iteration set** (from the fitted references):
  - meter correct on at least 80% of tracks with high meter confidence,
    including the waltz;
  - downbeats within 45 ms on at least 70% of committed bars after lock-in.

### Live-readiness

- Every new group extends the Python causality test in reactive-audio.md's
  [Causality of the stream itself](reactive-audio.md#causality-of-the-stream-itself).
- The stream command prints each group's **real-time factor**: analysis
  seconds per audio second.
  - The target is under 0.2× per new group, printed by `check:analysis` as a
    target, not a gate.
  - Hindsight's existing cost is grandfathered.
- **Streaming shape:** a new group's per-frame work and memory must be bounded
  regardless of song length. No whole-song passes and no growing buffers. The
  test is "an optimised Rust rewrite could run this live". A design that fails
  it is reworked into a streaming form, or dropped with the reason recorded.
- **No song-wide data in the game.** New game code reads only stream entries
  with `knownAt <= s`, and never `audio.duration`, except for the run's end
  ([Length-blind pacing](#length-blind-pacing)).

## The Signal Bus

`src/reactive/signals.ts` computes `musicSignals(ctx, s)`, a pure function of
the stream entries known at `s`. The evaluator samples it every frame, and the
simulation samples it at its own decision times.

| Signal | Range | From |
|---|---|---|
| `beatPhase`, `barPhase` | 0–1 | committed beats; the simulation's 2-beat bars |
| `musicalBarPhase`, `beatInBar` | 0–1, 1…meter | E2's bars; absent until locked |
| `phrasePhase`, `barInPhrase` | 0–1, 1…n | E2's phrases |
| `kick`, `snare`, `hat`, `bass` | 0–1 | E1's band envelopes, with attack and release smoothing |
| `onset` | 0–1 decay | the latest onset of any band, scaled by strength |
| `accent` | 0–1 decay | strong onsets and forecast accents |
| `loudness`, `level` | 0–1, quiet/medium/loud | ticks |
| `brightness` | 0–1 | E1 |
| `evidence`, `clear` | 0–1, boolean | ticks and the gate |
| `press`, `rating` | 0–1 decay, class | the player's presses (stage 3's press pulse) |
| `sectionIndex`, `sectionAge` | n, seconds | detected section starts |
| `key`, `mode` | pitch class, major/minor | ticks |
| `tier`, `groove` | n, 0–1 | the meter, settled |

- **Fading by evidence:** every rhythmic signal (phases, bands, onsets) carries
  a fade weight from `evidence`. Bindings use it unless they opt out. This
  generalises stage 3's `amp` and `mix`.
- **Press fallback:** when the beat is unclear, `beatPhase` follows the
  player's presses, as stage 3's press pulse does, so the world still moves
  with the player.

### The modulation matrix

`levels/reactive.yaml` gains a `look:` block of bindings, reactive levels
only:

```yaml
look:
  budget: { flash: 0.35, scale: 0.25 }    # caps on summed responses per frame
  bindings:
    - { signal: kick,  target: hero.aura.scale,        gain: 0.22, tier: beat,    attackS: 0.005, releaseS: 0.12 }
    - { signal: snare, target: enemies.squash,         gain: 0.15, tier: beat }
    - { signal: hat,   target: particles.ambient.emit, gain: 2.0,  tier: texture }
    - { signal: bass,  target: light.ambient,          gain: 0.3,  tier: texture, curve: sqrt }
    - { signal: musicalBarPhase, target: props.sway,   gain: 1.0,  tier: bar }
    - { signal: accent, target: post.flash,            gain: 0.6,  tier: accent }
    - { signal: brightness, target: light.hue,         gain: 0.15, tier: texture, smoothS: 2 }
```

- **Targets** form a typed registry (`src/reactive/look.ts`, zod-validated).
  Each element parameter names its default, its range and the renderer, HUD or
  audio code that reads it. The first registry covers the 12
  [coverage classes](#metrics):
  - hero body, hero aura, weapons and strikes;
  - enemies, props and world, light and ground;
  - particles, camera, HUD;
  - pickups, post-processing and sounds.
- **Tiers** set the intensity hierarchy: `accent` > `bar` > `beat` > `sub` >
  `texture`.
  - The `budget` caps summed flash and scale responses per frame.
  - When the cap binds, lower tiers yield first, so big moments read bigger
    and busy passages don't turn into noise.
- **Migration:** G1 moves stage 3's hand-wired constants (`0.4 + 0.6 *
  loudness`, `SECTION_PUNCH`, the press ripple and so on) into default
  bindings with the same values. A test samples the evaluator's frame state on
  the Galop before and after: equal within 1e-9 until a later bead changes a
  binding.
- **Play mode is untouched:** the matrix exists only when `reactive: true`.

### Song identity

- **The listening look:** from the first frame, a neutral, low-saturation look
  that already pulses on committed beats and presses.
- **The reveal:** on the first musical downbeat after 8 musical bars, at least
  75% of which had the beat clear.
  - The identity is chosen from what is known at that moment:
    - tempo;
    - key and mode;
    - the latest `timbre` entry;
    - loudness range so far;
    - density.
  - A song with no clear beat after 30 s reveals an **ambient** identity on
    the next detected section start, or at 45 s.
  - The reveal is a moment: a zone wave, a palette bloom and an accent-tier
    flash, all on that downbeat.
- **Families** live in `content/identities.yaml`. Each names:
  - a palette and its light grading;
  - a zone order drawn from the existing zones;
  - a motion style (bounce, glide or stomp: how bindings shape the hero and
    enemy motion, such as squash amounts and easing);
  - a particle style;
  - an SFX family;
  - a feature profile used to score the family against the song.

  The highest score wins. Ties are broken by a hash of the quantised features,
  so similar songs can still differ, but the same song always gets the same
  identity.
- **Evolution:** detected section starts can move to a variant inside the
  family: darker for minor or quiet sections, hotter for loud ones. The family
  never changes mid-song.
- **Recording:** the identity and each variant change are recorded at their
  decision times, so the perturbation check compares them.

### Sounds

- **Key and mode:** every pitched sound follows the detected key and mode:
  - chimes, level-ups, powerups;
  - cleave bands;
  - tier chimes.

  This extends `keyScale` from gem chimes, which today carry the key only.
- **On the grid:** percussive gameplay sounds stay on committed-beat slots, as
  reactive-audio.md requires.
- **Ducking:** game sound gain follows a binding on `loudness` and on the
  band its sample occupies, so the song leads and gameplay sounds sit in its
  gaps.
- **The NotJam SFX** become a `chip` sound family in the library below.

### The SFX library

- **Inventory:** `content/sfx-library.yaml` lists every sample with its role:
  strike, heavy, cleave, gem, level-up, tier, set piece or UI. The samples
  come from:
  - the existing sounds;
  - the 42 NotJam SFX;
  - the packs F3 picked.
- **Features:** a new `npm run analysis -- sfx-features` command writes each
  sample's brightness, attack time, pitch (f0, if pitched), flatness and
  duration into the library, offline.
- **Selection:** at the reveal, the game picks a sample for each role. The
  pick is the sample nearest to the song's timbre vector by cosine distance on
  standardised features, within the identity's SFX family. Section changes
  can re-pick within the family.
- **Check:** every role in every family has at least one sample, enforced by
  `check:content`.

### The song-sampling spike (G5)

A time-boxed experiment, one bead, two days at most:

- The stream gains `slices: [{ knownAt, t0, t1, band }]`. These are clean,
  isolated kick and snare hits from the past 10 s. Each is known at its end,
  plus the analysis time.
- In file mode the game slices the decoded buffer and uses the slices as
  strike sounds, pitched and filtered to sit under the song.
- **Output:** a mixed-audio file of three iteration tracks, for the record.
- **The executor decides** whether to keep it, from measurements:
  - slices land within 20 ms of their slots;
  - no clipping or clicks at slice edges;
  - sampled strikes sit at least 6 dB under the song's loudness.

  If it passes, its level flag defaults to on. Otherwise the flag defaults to
  off and the bead records what was learned.

### Length-blind pacing

Stage 6's director reads absolute seconds:

- `boss.afterS: 90`;
- `zoneFallbackS: 60`;
- `levelUpWaitS: 20`;
- `finale.windowS: 10`, which reads the file's length.

On a 40 s song the boss never comes. With live audio, the length is unknown.
G3 rewrites these in musical units:

| Today | Becomes |
|---|---|
| `boss.afterS: 90` | the first loud detected section start after at least 2 earlier structural changes and 32 committed bars |
| `zoneFallbackS: 60` | `zoneFallbackBars: 24` |
| `levelUpWaitS: 20` | `levelUpWaitBars: 8` |
| `population.perMinute` over elapsed time | growth per committed bar, scaled by level |
| set-piece `gapS: 6` | `gapBars: 3`, at least 4 s |
| `finale.windowS` (file length) | the first confirmed candidate ending |

- **A confirmed ending** is a candidate ending (reactive-audio.md's `ends`)
  that follows at least 16 bars with the beat clear, and whose quiet lasts
  3 s.
- **Encore:** if the beat comes back after a finale (evidence above the gate
  for 2 bars), the run continues. The horde resumes, and the next confirmed
  ending can bring another finale.
- **The run ends** when the audio ends, plus the epilogue if a finale is still
  playing. This is the only remaining read of the file's length, and a live
  build replaces it with a silence timeout.
- **Short songs** get a compressed but complete arc. A 40 s song can reach a
  section change, a set piece and its finale, even if the boss never comes.
- **The Galop regression:** stage 7's recorded Galop results (survival, the
  masher fall window, grades, set-piece times) are G3's regression targets.
  Stage 7 is retuning `population.start`, `perMinute` and `max` right now. G3
  converts its final values into per-bar growth, rather than the ones in this
  document.
  G3 holds them, or retunes knowingly and records the before and after in its
  notes.

## Measuring Feel

### The dossier

```sh
npm run reactive:dossier -- --track notjam-victory-lap --round 3
npm run reactive:dossier -- --split iteration --round 3     # every iteration track, one after another
npm run reactive:dossier -- --anchor --round 3              # the authored Galop, play mode
```

This writes `captures/feel/round-<n>/<id>/`, which is gitignored. All runs use
seed 1858 and damage off unless stated.

- **`metrics.json`** and **`metrics.md`:** the [metrics](#metrics) for this
  track.
- **`timeline.png`:** one chart over the song, with these lanes:
  - the reference beats, downbeats and approximate sections;
  - the stream's evidence, energy, level and bands;
  - the game's strikes, sounds by role, flashes, punches, zone waves, set
    pieces, level-ups, the reveal and variant changes, and the finale;
  - the tier and groove for `reactive-moderate` and `mash4`.
- **Section sheets:** for up to 4 reference sections (the longest and most
  distinct), one contact sheet each, 4 × 4 frames:
  - each row is one beat of a musical bar;
  - each column is a beat phase: 0, 0.25, 0.5 or 0.75;
  - frames are captioned with time, phase and section.
- **A phrase sheet:** beat 1 of 8 consecutive bars from the loudest section,
  to show phrase-level change.
- **An honest-against-masher sheet:** the same 4 moments from
  `reactive-moderate`'s and `mash4`'s runs, side by side, to show the reward.
- **A moments sheet:** up to 12 captioned stills, 0.25 s after each:
  - the reveal;
  - each detected section start and each reference section start;
  - each set piece;
  - the finale.
- **`frames.json`:** the evaluator's frame state sampled at 240 Hz. The
  evaluator is a pure function of song time, so any rate is free. The metrics
  read this file.
- **`mix.ogg`, for the user only:** the track with the live recording's sound
  events mixed over it. Raters never get audio.

`npm run capture` already renders exact song times in headless Chrome. F4
extends it to `?mode=reactive&track=<id>&player=<p>` if it doesn't already
work there.

### Metrics

`npm run reactive:corpus -- --split iteration` prints one row per track and
the split's means, and writes `captures/feel/corpus.json`. Each metric pairs
with a rubric dimension.

- **Beat (M1):**
  - committed-beat F-measure within 45 ms of the fitted grid, strictly and at
    any metrical level;
  - the longest run of correct beats as a share of the track;
  - lock-in time: the first 8 consecutive correct beats.
- **Bars (M1, A2):** meter correct, and downbeats within 45 ms (from E2).
- **Sound sync (M1):** the share of percussive sounds within 20 ms of a
  reference slot.
- **Reward (M2):**
  - with damage off, the share of judged time at tier 2 or higher for
    `reactive-moderate`, and at tier 0 for `mash4` and `mash8`;
  - the ratio of honest damage dealt to masher damage dealt;
  - mean strike size by tier.
- **Fairness (M3):**
  - the share of the track's clearly beat-driven time (by the reference's
    onset regularity) that is judged;
  - the share of honest presses settled Perfect or Great.
- **Pacing (M4):**
  - the number of set pieces, level-ups and zone waves;
  - the share of the song before the first of each;
  - false finales;
  - with damage on, whether the honest players survive.
- **Coverage (A1):** for each of the 12 element classes, whether its
  parameter "reacts": correlation of at least 0.3 with its bound signal, or a
  periodicity peak at the beat period, during clear stretches. Reported as n
  of 12.
- **Visual sync (A2):** for beat-tier and bar-tier outputs, the share of
  response peaks within 1 frame (1/60 s) of a reference beat or downbeat.
- **Identity (A3):**
  - the identity family;
  - mean frame colour histogram and motion statistics;
  - across a batch, the share of track pairs whose families differ, and the
    pairwise histogram distance.
- **Structure (A4):**
  - the share of reference section starts followed by a visible change
    within 8 s;
  - visible changes more than 8 s from any reference start.
- **Hierarchy (A5):**
  - the Spearman correlation per bar between reference loudness and visual
    intensity (mean flash, scale and particle response);
  - the share of frames at the response budget's cap.
- **Breakdowns (A6):** in stretches below the gate:
  - the percussive sounds played (should be none);
  - motion following the press fallback;
  - the visual intensity drop;
  - whether the groove holds or is credited, and is never drained.

### The rubric

`docs/feel/rubric.md` holds the full rubric. Each dimension is scored from 1
to 5 against written anchors for 1, 3 and 5. The short form:

| Dim | Name | 1 | 3 | 5 | Paired metrics |
|---|---|---|---|---|---|
| M1 | Beat lock | Strikes and sounds visibly off the beat | Mostly on, drifts in places | Locked everywhere the music has a beat | Beat F-measure, sound sync |
| M2 | Reward legibility | Masher and honest player look alike | Honest player somewhat stronger | Following the beat obviously grows the attacks | Tier shares, damage ratio, strike size |
| M3 | Fair judging | Clear beats go unjudged or misjudged | Occasional misreads | Judged when the beat is clear, quiet when it isn't | Judged share, settled ratings |
| M4 | Pacing fit | Arc ignores the song; nothing happens or it ends wrong | Arc partly matches | Escalation, set pieces and finale match the song's shape at its length | Pacing metrics |
| A1 | Coverage | Only a few elements react | About half react | Nearly everything moves with the music | Coverage n/12 |
| A2 | Visual sync | Pulses off-beat or mushy | Mostly on | Crisp on beats, bars and accents | Visual sync |
| A3 | Song identity | Indistinguishable from other songs | Some differences | Looks and sounds built for this song | Identity metrics |
| A4 | Structure response | Ignores section changes or changes at random | Catches some | Changes when the music does, holds when it doesn't | Structure metrics |
| A5 | Intensity hierarchy | Flat or noisy | Some contrast | Big moments clearly biggest, never saturated | Hierarchy metrics |
| A6 | Breakdowns and silence | Garbage motion or sounds in quiet | Acceptable | Graceful: fades, follows the player, returns cleanly | Breakdown metrics |

**Calibration:** each round's first batch includes the authored Galop's
play-mode dossier as an anchor. It should score about 5 on M1, M2, A2 and A4.
If it scores 3 or less on any of those, that rater's batch is re-rated before
triage.

### Raters

- **Who:** each rater is a fresh, read-only subagent of the executor's own
  host. It uses the session's model, is told not to edit files, and is given
  `docs/feel/rubric.md`. Never use `codex exec` or `claude -p` from the shell.
- **Batches:** about 5 tracks per batch, so a rater can judge identity across
  songs. About 6 or 7 batches per round.
- **Order:** raters run **one at a time**, never in parallel.
- **Output:** each rater writes `captures/feel/round-<n>/batch-<k>/scores.json`:

  ```json
  {
    "rater": "batch-2",
    "tracks": {
      "notjam-victory-lap": {
        "scores": { "M1": 4, "M2": 2, "A1": 3, "A3": 2 },
        "issues": [
          {
            "dim": "M2",
            "t": 41.5,
            "evidence": "honest-vs-masher sheet, row 2",
            "issue": "tier 3 strikes look the same size as tier 0",
            "area": "weapon scale binding"
          }
        ]
      }
    }
  }
  ```

  Each track gets its 10 scores and up to 3 issues.
- **A second rater** re-scores:
  - the anchor batch;
  - any track whose score contradicts its paired metric. That means a 4 or 5
    while the metric is in the round's bottom quartile, or a 1 or 2 while it
    is in the top quartile.

  The two raters' differences are reported as that round's rater noise.

### Rounds

A round is one bead. Its steps:

1. **Regenerate:** streams, if stale, then dossiers for the iteration set, the
   CC iteration tracks and the Galop anchor.
2. **Rate:** batch by batch, one rater at a time, then the second rater where
   needed.
3. **Aggregate:** `npm run reactive:feel -- aggregate --round <n>` does the
   following:
   - merges the scores;
   - computes the per-dimension means;
   - flags contradictions;
   - writes `docs/feel/round-<n>.json` (committed): scores, metrics, rater
     noise and the issue list.
4. **Triage:**
   - Cluster the issues across tracks.
   - Rank each cluster by tracks affected × severity × how far below 5 its
     dimension sits.
   - File the top 3 to 5 as fix beads under this plan's epic. Each fix bead
     carries:
     - the cluster's evidence (dossier paths, times, metrics);
     - a predicted metric movement;
     - its serialization edges.
5. **Record:** add a row to [Appendix: Rounds](#appendix-rounds): the
   dimension means, the key metrics and the fix beads filed.
6. **File the next round:** a bead blocked on this round's fix beads. Every
   third round, file a milestone first.

A round bead delivers only its committed summary through Tollgate. Dossiers
stay in gitignored `captures/`.

### Seeded work

The fast lane's findings come first: its notes on the epic re-rank and rescope
the seeded beads. Round 0 then runs before phase D. It also runs milestone 0: the held-out steps
on the same untouched engine, giving the overfitting rule its baseline. Its
triage doesn't file new fix beads.

- It maps its clusters onto the seeded beads (E1, E2, G1 to G5), recording
  the matching findings in each bead's notes.
- It may reorder or rescope them. For example, it might move G4 ahead of G2
  if sound complaints dominate.
- It files fix beads only for clusters no seeded bead covers.

### Held-out protocol

- **Milestone beads** run every third round. They follow the round's steps 1
  to 3 on the held-out NotJam and CC tracks, writing into
  `captures/heldout/milestone-<n>/` (gitignored).
- **Numbers only:** the aggregate step writes only numeric scores and metrics
  to `docs/feel/milestone-<n>.json`, with no issues and no prose.
  - Executors of fix and round beads must never open `captures/heldout/`, held-out
    dossiers or held-out rater output.
  - No fix bead may cite a held-out track.
- **The overfitting rule:** for each dimension, compare the iteration mean
  minus the held-out mean against the same gap at **milestone 0**, which R0
  runs on Round 0's untouched engine. The rule trips if:
  - any dimension's gap grows by 0.75 or more; or
  - the mean gap across dimensions grows by 0.4 or more.
- **When it trips,** the loop continues. The next round becomes a
  generalisation round:
  - Its triage finds which changes since the previous milestone are most
    track-specific, judged only from iteration-side evidence: tuning keyed to
    a few tracks, or metrics that rose on some tracks and fell on others.
  - It reverts or broadens those changes, then scores the held-out set again
    as an extra milestone.
- **No burning:** held-out tracks stay held out for the whole plan.

### Stopping

The loop ends when two consecutive rounds raise no dimension's iteration mean
by 0.2 or more and newly meet no metric target. A generalisation round
doesn't count towards the two.

- **On ending,** the last round bead runs a final milestone and publishes a
  short summary: the start against the end per dimension, the held-out
  results, and what remains weak.
- **Then** the epic is closed, with no approval step.

## Mechanics Targets

Printed by `reactive:corpus` as targets, not gates. The corpus is kept out
of `check:reactive`'s gates on purpose ([CI budget](#causality-determinism-and-checks)).

- **First priority, per track with damage off: mashing never beats
  playing.** No masher (`mash2`, `mash4`, `mash8`) and not the holder has a
  higher average groove, or more judged time at tier 2 or higher, than
  `reactive-moderate`. This is `hv-s4pt.13`'s gate, measured on the corpus.
- **Then, per track with damage off, over judged time:**
  - `reactive-moderate` at tier 2 or higher for at least 60%;
  - honest damage dealt at least 2× masher damage dealt;
  - `mash4` and `mash8` at tier 0 for at least 80%.
    - This is the lowest priority. Beatless credit carries mashers into judged
      stretches at a full meter, so it misses on tracks with many beatless
      stretches, as it does on the Galop.
- **Beatless-heavy tracks** give everyone the same credit, so their reward
  shows only in judged stretches. The reward metrics (M2) report judged time
  alone.
- **With damage on:** the honest players survive every track with at least
  30 s of clear beat.
- **The Galop:** stage 7's recorded results, as regression targets
  ([Length-blind pacing](#length-blind-pacing)).

## Causality, Determinism and Checks

Everything in reactive-audio.md's [Causality](reactive-audio.md#causality)
and [Automated Checks](reactive-audio.md#automated-checks) still holds.
This plan extends it:

- **Stream causality:** each new group (bands, timbre, bars, phrases, slices)
  joins the Python cut test.
- **The perturbation check** changes the new groups after a cut:
  - band and brightness values are replaced;
  - bar, phrase and slice entries move, are deleted and are inserted, as
    structure entries are;
  - timbre entries are replaced.

  The prefix comparison adds:
  - the signal bus's values;
  - every binding's output in the frame state;
  - the identity and variant decisions;
  - the SFX picks.
- **`causality-mix`** gains a meter change, a phrase boundary and a
  long-enough clear stretch for a reveal, so every new feature fires before a
  cut. The check's compressed level shortens the reveal to 2 bars and the
  pacing constants to match.
- **The negative control** gains a variant whose identity reads the timbre
  entry 1 s ahead. The check must fail.
- **Determinism:** identity ties hash quantised features; SFX picks are
  deterministic; plan hashes repeat.
- **Play mode is unchanged:** `npm test` with the golden hashes, `check:play`
  and `check:seeds` pass without changing any fixture.
- **CI budget, and keeping the corpus out of the gates:**
  - Today `check:reactive` runs every per-track gate over every track in
    `public/reactive/index.json` (`shippedTrackIds()`):
    - the end-to-end runs;
    - the mashing gate, which simulates 5 players per track;
    - the stream checks.
  - Listing 39 corpus tracks there would multiply CI time, which is already
    about 80 s for 10 tracks. It would also make held-out tracks gates, and a
    failure would force a fix that cites a held-out track.
  - So F1 splits the list:
    - `gatedTrackIds()`: the regression set's classical and synthetic groups;
      every per-track gate uses it;
    - `corpusTrackIds()`: the `notjam` and `cc` groups.
  - The fresh-streams gate covers both lists, since it's cheap. Apart from
    that, the only corpus check is a smoke run of one iteration track.
  - `reactive:corpus`, the dossiers and the rounds run outside CI. Held-out
    tracks go through `reactive:corpus` only with `--milestone`.
- **Index check:** a unit test checks the manifest, families, splits,
  licences and credits.

## Beyond This Plan

- **Playlist runs:** a run spanning several songs, with track changes as
  major structural events and pacing over the whole run.
- **New art per identity:** zone art, enemy skins and particle sprites made
  for identity families.
- **Live analysis and capture,** as in reactive-audio.md. This plan's
  live-readiness rules keep that path open.
- **Learned identities:** choosing families from embeddings of the audio
  rather than hand-written feature profiles.

## Alternatives Considered

- **Tune on the Galop only.** That is reactive-audio.md's scope, and it
  cannot show whether reactive mode generalises.
- **No held-out set.** It's cheaper, but with a rater loop tuned on the same
  songs it rates, overfitting would be invisible.
- **Absolute seconds, scaled by file length.** Simple for files, but no live
  build can know a song's length.
- **Hand wiring per element.** It was fast for stage 3, but it can't express
  identity, and it makes harmony a matter of luck.
- **Changing the simulation's 2-beat bars to musical bars.** Mechanics would
  then read downbeats that may lie beyond the commit horizon. Musical bars stay
  aesthetic, plus pacing counts.
- **Video for raters.** Subagents only see sampled frames anyway. Beat-phase
  contact sheets show synchrony more directly.
- **Parallel raters.** Faster, but rejected to protect the usage limit.

## Manual QA

For the user, never blocking a bead:

1. **After Q:** play two or three of the mini-corpus tracks. Does the horde
   move with the music? Do the songs look different from each other?
2. **After F2:** listen to two or three click overlays from
   `captures/corpus/`. Does the click sit on the beat and accent the bar?
3. **After G1:** play an iteration track. Does more of the world move with the
   kick, snare and hats?
4. **After G2:** play two contrasting iteration tracks. Do the reveals land on
   a downbeat? Do the two songs feel like different videos?
5. **After G3:** play a sub-60 s track and the Galop. Does each get a
   complete arc, and does the finale come at the end and not in a mid-song
   silence?
6. **After G4 and G5:** listen to `mix.ogg` for three tracks. Do the game
   sounds sit inside the song? Judge the spike's sampled strikes.
7. **At each milestone:** play one held-out track of your choice. Your
   impressions go into the milestone's notes.

## Implementation Plan

Tracked by epic `hv-jtsz`. Each bead delivers through its own Tollgate
worktree and keeps `scripts/ci` green.

| Key | Bead | Phase | Work | Needs |
|---|---|---|---|---|
| Q | `hv-jtsz.16` | 0 | The fast lane: mini-corpus, beat sheets, enemy beat motion, onset texture, key colour, stand-in identity, legible reward | — |
| F1 | `hv-jtsz.1` | A | Corpus manifest, NotJam ingest to Ogg, index groups and splits, `corpus/CREDITS.md`, `--jobs` parallel regeneration, gated against corpus track lists, NotJam streams | Q |
| F2 | `hv-jtsz.2` | A | `fit-grid`: fitted references, meter, approximate sections, click overlays; corpus beat-metric table | F1 (serialized: streams) |
| F3 | `hv-jtsz.3` | A | Pick about 12 CC tracks and extra SFX packs, licences verified | Q |
| F3b | `hv-jtsz.4` | A | Download and ingest F3's tracks with fitted references | F2, F3 |
| F4 | `hv-jtsz.5` | A | `reactive:dossier`, `reactive:corpus`, metrics, timeline, contact sheets, `mix.ogg`, reactive capture, the authored Galop anchor | F2 |
| F5 | `hv-jtsz.6` | A | `docs/feel/rubric.md`, rater brief, scores schema, `reactive:feel aggregate`, milestone numbers-only export | F4 |
| R0 | `hv-jtsz.7` | B | Round 0 baseline, milestone 0 (held-out baseline, numbers only) and seeded-work triage | F5, `hv-s4pt.11` |
| E1 | `hv-jtsz.8` | C | Bands, band onsets, brightness, timbre in the stream; causality tests; real-time factors | F2 (serialized: streams) |
| E2 | `hv-jtsz.9` | C | Causal meter, downbeats and phrases in the stream; targets | E1 (serialized: streams) |
| G1 | `hv-jtsz.10` | D | Signal bus, modulation matrix, target registry, budget and tiers; migration of stage 3's wiring with frame equality | R0, E1 |
| G2 | `hv-jtsz.11` | D | Identity: listening look, reveal, families, evolution; musical-bar and phrase signals | G1, E2 |
| G3 | `hv-jtsz.12` | D | Length-blind pacing, confirmed endings and encore; Galop regression | R0 |
| G4 | `hv-jtsz.13` | D | Key and mode pitching, ducking, SFX library and `sfx-features`, per-role picks, NotJam SFX | G2 |
| G5 | `hv-jtsz.14` | D | Song-sampling spike, time-boxed, kept or flagged off by measurement | G4 |
| R1 | `hv-jtsz.15` | E | Round 1 (later rounds and milestones are filed by the round before) | G1–G4 |

- **Serialization:**
  - The stream beads run in order: F1, F2, E1, E2, and F3b after F2. Each
    rewrites shipped streams.
  - G1 stays inside the evaluator, renderer, HUD and audio, and does not edit
    `src/sim/simulate.ts`. G3 stays inside the director, set pieces and the
    simulation's director reads. They may run in parallel, both after stage 7.
    If either has to cross into the other's files, add a serialization edge.
  - G2 and G4 follow G1 because they extend its matrix.
  - Every game-side bead (G1–G5 and fix beads) waits for `hv-s4pt.11`, which
    itself follows `hv-s4pt.12`.
- **The fast lane first:** Q runs now, alongside stage 7. Every other bead
  waits for it, through F1 and F3. Then F1, F3 and F2 run, F4 and F5 follow,
  and R0 runs once stage 7 has landed too.
- **Q is game-side but avoids `src/sim/`,** so it doesn't wait for stage 7.
- **Review:** the repository's rules apply. G1, G2, G3, E1 and E2 likely
  touch simulation determinism or exceed 300 lines, so each gets one cold
  warden review.

## Appendix: Rounds

One row per round and milestone, added by each round bead.

| Round | Date | M1 | M2 | M3 | M4 | A1 | A2 | A3 | A4 | A5 | A6 | Key metrics | Fix beads |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 2026-10-24 | 3.14 | 3.14 | 3.48 | 3.10 | 3.03 | 2.52 | 1.81 | 2.33 | 2.14 | 2.45 | beat F 0.73 (any 0.84), lock-in 11.3 s, sound sync 0.92, honest tier 2+ 97%, mash4 tier 0 86%, damage ×1.33, coverage 7.3/12, visual sync 0.41, hierarchy -0.09; rater noise ≤ 0.53 | hv-jtsz.17, hv-jtsz.18, hv-jtsz.19, hv-jtsz.20 (findings mapped onto E1, E2, G1–G4) |
| M0 | 2026-10-24 | 3.41 | 3.36 | 3.54 | 3.09 | 3.27 | 2.86 | 2.09 | 2.32 | 2.27 | 2.91 | held-out, numbers only (22 tracks): beat F 0.86 (any 0.93), honest tier 2+ 95%, mash4 tier 0 86%, damage ×1.41, coverage 7.6/12, visual sync 0.49; the overfitting rule's baseline | none (milestones file none) |
