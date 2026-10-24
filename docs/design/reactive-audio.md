# Reactive Mode: Play to Music the Game Has Not Heard Yet

## Introduction

Playlist Survivors knows its song completely before the first frame. A song
map, built offline and corrected by hand, fixes every beat, bar, section,
phrase and accent. A level script then places the horde, the set pieces and
the level-ups on those musical moments.

This document plans **reactive mode**: a new game mode played to music the
game has not analysed in advance. The long-term goal is to play along to any
audio on the computer, such as a streaming app or a video. In that setting the
game learns about the music only as it plays, and nothing reveals the future.

The plan is a staged roadmap:

- **Play mode stays exactly as it is,** as the baseline to compare against.
- **Reactive mode is a separate mode** that grows stage by stage towards the
  full play-mode experience: the beat sync, the groove, the aesthetic
  flourishes, the scene changes and, as far as they can transfer, the set
  pieces.
- **Every stage ends playable,** and a parity report measures how close each
  stage gets to the authored experience.
- **The Galop is the main test track.** Its hand-made song map is ground truth
  for beats, sections and accents, and its slow prelude, quiet vamp and
  speeding coda show the real-world problems. Synthetic tracks with exact beats
  serve as clean controls.

Until a later stage adds live analysis, the music comes from a file, but the
game may only use what a **causal** analysis would know at each moment. An
offline step writes that knowledge out as a **belief stream**. Every entry is
stamped with the time it became known, and checks prove that the game never
reads an entry early.

Terms used throughout:

- **Song time:** seconds into the audio, as everywhere else in this game.
- **Causal:** computed only from audio up to the current moment.
- **Known-at time (`knownAt`):** the song time at which a belief-stream entry
  becomes available to the game. An entry is never visible before it.
- **Committed beat:** a beat time the tracker forecasts, made known well ahead
  of the beat and never revised. Strikes and gameplay sounds are placed on
  committed beats.
- **Hindsight grid:** beat times re-decoded a little later from a longer
  stretch of audio with a stronger model. Presses are judged against these
  from stage 2.
- **Evidence:** a 0-to-1 measure of how strongly the audio's onsets line up
  with the beats the tracker believes in. Low evidence means the beat is
  unclear: a breakdown, a free passage, or the tracker lost.
- **The gate:** evidence 0.2. Below it, nothing is judged and percussive
  gameplay sounds are muted.
- **Reading:** one interpretation of a hindsight grid's tempo level and phase:
  as tracked, off-beat, double tempo, or half tempo on either beat.
- **Settle delay:** from stage 2, how long after a press its authoritative
  judgment and groove change arrive: 2 s.
- **Judged time:** song time after the first 16 s at which the evidence is at
  or above the gate. The groove checks measure shares of judged time.
- **Structural event:** a detected change in the music, such as a new section
  or a jump in loudness, known some seconds after it happens (stage 4).
- **Authored run** and **reactive run:** the same virtual player playing the
  same track in play mode and in reactive mode.

The plan rests on experiments summarized in the
[evidence appendix](#appendix-evidence).

### The plan in brief

| Stage | Playable result | What it adds |
|---|---|---|
| 1. Foundation | The Galop and the test tracks play in reactive mode | Belief stream, reactive song map, a basic director, strikes and sounds on committed beats, instant judging, the gate, the causality check, the parity report |
| 2. Fair judging | The groove follows your playing, even where the tracker's tempo is wrong | Hindsight grids, a groove that settles 2 s late, readings chosen from presses |
| 3. Flourish | The world pulses with the music and with you | Evidence-faded pulses, accent flashes, energy lighting, a press-reactive world |
| 4. Structure | Scenes change when the music does | Detected section changes and loudness jumps drive zone waves, rosters, camera, palette and level-ups; the key colours the gem chimes |
| 5. Accents | Heavy swings and cleaves return | Forecast repeating accents, cleaves on detected accents |
| 6. Set pieces | Rings, stampedes, volleys, formations and the boss return, triggered by the music | Set pieces on structural events, a reactive finale |
| 7. Parity | Reactive Galop plays like a finished level | The difficulty curve, healing, grades, Watch the demo, a final parity pass |

### Decisions

These choices were made with the user while designing. The executor treats
them as settled.

- **Play mode is preserved unchanged** for comparison. Reactive mode is a new
  mode in the existing web build that reuses the simulation, renderer, HUD,
  content and assets.
- **The goal is the play-mode experience,** including aesthetic flourish. Where
  something cannot transfer exactly, reactive mode finds the nearest version
  the music allows, such as set pieces triggered by detected structure.
- **Reactive mode stays playable throughout.** From bead `hv-s4pt.3`, which
  first makes it playable, every bead leaves it playable on the Galop, which a
  headless smoke test in the gate checks.
- **The Galop and the other classical tracks are test material.** They show the
  real-world problems; synthetic tracks are clean controls.
- **Player presses are input to the beat,** not just judged against it, for
  both mechanics and effects.
- **Judgment waits** for hindsight; effects do not. The settle delay is 2 s,
  a choice inside the measured plateau of the stronger model's accuracy (see
  the appendix).
- **Someone who consistently presses on the off-beat is accepted** as keeping
  the beat. The guard cannot tell that player from one who hears the backbeat
  as the beat, and either is musical.
- **The game never delays the music.** Muting the source application and
  replaying its audio late would buy real lookahead, but the user rejected it
  as too risky. No stage captures audio in a way that changes what the player
  hears.

### Goals

- A player can choose a track in reactive mode and play it from start to end
  after every stage.
- Every simulation decision, sound and rendered frame depends only on stream
  entries known at the time, enforced by an automated check.
- A parity report shows, for the Galop, how the reactive experience compares
  with the authored one, row by row, and each stage moves its rows closer.
- Gates guard correctness: causality, determinism, playability, play mode
  unchanged, and the few behaviours the experiments support. Everything else is
  a measured **target**, printed by the checks and recorded per stage, because
  "how close can we get" is the question this plan exists to answer.
- On steady beat-driven tracks, an honest player's groove behaves close to play
  mode's, while mashers stay at tier 0.
- The user can add their own audio files locally with one command.
- Play mode does not change: the golden demo hashes, `check:play` and
  `check:seeds` pass unchanged throughout.

### Non-goals

- Capturing audio from other applications, or analysing audio in real time.
  Every stage here computes the belief stream offline from a file; live
  analysis is a [later stage](#beyond-this-plan).
- Reproducing a specific song's authored moments exactly, such as the boss
  dying on the Galop's last tutti chord. Reactive mode aims for the same kind
  of moment, triggered by what the music does.
- Mobile or touch input.

## The Experience Map

Every part of today's play experience and where its reactive counterpart
comes from. "Exact" means it transfers unchanged; the stage column says when
it arrives.

| Play-mode element | Depends on | Reactive counterpart | Stage |
|---|---|---|---|
| Strides, stomps, diagonals | Presses, the grid | Exact, on committed beats | 1 |
| Sword and weapons firing on the beat | The grid | Exact, on committed beats | 1 |
| Sounds on the grid | The grid, sixteenths | Exact on committed beats; muted when the beat is unclear | 1 |
| Press classes and ratings | The grid | Instant on committed beats (stage 1), settled against hindsight grids (stage 2) | 1, 2 |
| Drift, calibration | Presses | Exact | 1 |
| Groove, tiers, weapon sizes | Ratings | Exact rules on settled ratings, 2 s later | 2 |
| Free sections (the prelude) | The song map's feel | The gate: unclear stretches are not judged (stage 1) and fill the meter with beatless credit (`hv-s4pt.13`); free movement (stage 3) was dropped by `hv-s4pt.12` | 1, 3 |
| Horde stream, population curve | Section ranges | Elapsed time and loudness (stage 1); loudness level and detected sections (stage 4) | 1, 4 |
| Rosters per section | Sections | Roster steps on detected sections | 4 |
| Beat pulses, aura, sway, bobs | Beats | Committed beats, faded by evidence | 3 |
| Camera zoom and lead per section | Sections | Per loudness level, changed on structural events | 4 |
| Camera punches on accents | Accents | Detected strong onsets, no lead-in | 3 |
| Effect intensity per section | Sections | Loudness | 3 |
| Zone transform waves | Section starts | Structural events, on the next committed downbeat | 4 |
| Sound palette per section | Sections | Per loudness level | 4 |
| Gem chime pitches | The section's key | A causally estimated key | 4 |
| Level-ups and orbs | Musical moments | XP plus a structural event when one comes soon enough | 4 |
| Heavy swings on accents | Accents known ahead | Accents forecast from repetition | 5 |
| Cleaves on a press | Accents | Forecast accents, or detected strong onsets with bands from the next eighth | 5 |
| Closing rings, stampedes | Accents, bars | Structural events and loudness jumps | 6 |
| Archer volleys | Phrases, bars | Every 2 bars of committed beats in loud stretches | 6 |
| Formations, the kick line | Phrases, final accents | Detected sections; release on the next strong accent | 6 |
| Boss and slams | Sections, the last tutti chords | Entry on a loud structural change late in the run; slams on committed downbeats | 6 |
| Finale blast, slow motion | The last tutti chords | A detected ending | 6 |
| Landmarks | Script times | With the boss and the finale | 6 |
| Section banners, title card | The song map | Track title card; a flourish on structural events | 4 |
| Difficulty curve, healing, grade | The authored level | Retuned against the reactive Galop | 7 |
| Watch the demo, Watch my run | The planner | Exact (Watch my run, stage 1); a recorded skilled run (stage 7) | 1, 7 |

## Related Information

- [README.md](../../README.md): how the game runs, the checks, URL parameters
  and the play log.
- [gameplay-redesign.md](gameplay-redesign.md): today's rules. This document
  reuses its terms and its press classes, ratings, drift, drain, tiers, set
  pieces and [Sound on the Grid](gameplay-redesign.md#sound-on-the-grid).
- [interactive.md](interactive.md): play mode's live simulation, rollback and
  sound scheduling.
- [tech-demo.md](tech-demo.md): the song map and the beat-map workflow that
  reactive mode replaces with causal analysis.
- [levels/orpheus-galop.yaml](../../levels/orpheus-galop.yaml): the authored
  level the parity report compares against.
- Code that this design touches:
  - [src/sim/schedule.ts](../../src/sim/schedule.ts): the song schedule. Its
    header calls it "everything the game may know in advance because it owns
    the song", which is exactly what reactive mode loses.
  - [src/sim/simulate.ts](../../src/sim/simulate.ts): the simulation, the
    meter (`meterChange`, `setTier`, `lastRaiseS`), the beat events that drain
    it, the spawn director and the set pieces.
  - [src/sim/timing.ts](../../src/sim/timing.ts): `classifyPress` and its
    `PressMemo` (keyed by grid index), `driftOf` and the meter arithmetic.
  - [src/planner/context.ts](../../src/planner/context.ts) and
    [src/planner/sim-checks.ts](../../src/planner/sim-checks.ts): `Ctx`,
    `checkCausality` and `prefixDifference`.
  - [src/planner/emit.ts](../../src/planner/emit.ts) and
    [src/live/recording.ts](../../src/live/recording.ts): sound placement, the
    voice budget and the gem chime's key (`keyScale`).
  - [src/plan/evaluate.ts](../../src/plan/evaluate.ts) and
    [src/runtime/renderer.ts](../../src/runtime/renderer.ts): the beat phase
    and every beat-synced pulse.
  - [src/live/runner.ts](../../src/live/runner.ts): live play runs the
    simulation 0.2 s ahead of the audio.
  - [src/runtime/app.ts](../../src/runtime/app.ts),
    [title.ts](../../src/runtime/title.ts) and
    [play.ts](../../src/runtime/play.ts): mode selection and play flow.
  - [tools/analysis](../../tools/analysis): the Python analysis tool (madmom,
    librosa) that gains the stream and synth commands.
- The experiments' scripts and full report:
  `/Users/dthurn/playlist_survivors/tools/analysis/work/causal-beats/`
  (gitignored, in the main checkout only). Bead 1 moves `synth.py` from there
  into the analysis tool.
- Background: madmom's online beat model (a unidirectional LSTM) and its
  dynamic Bayesian network beat tracker, used here in forward-only mode.

## Foundations

These hold for every stage.

### Design overview

```text
audio file ──ps-analysis stream──► stream.json (every entry has a knownAt)
                                       │
     ┌───────────────┬─────────────────┼─────────────────┬───────────────┐
     ▼               ▼                 ▼                 ▼               ▼
committed beats  hindsight grids  ticks, onsets     structure        accents
(~2 beats ahead) (every 1 s)       (evidence,        (changes, jumps, (forecast and
                                   energy)           level, key, end) detected)
     │               │                 │                 │               │
strikes, sounds, settled groove   gate, pulses,     scenes, rosters, heavy swings,
beat events      (stage 2)        lighting          set pieces       cleaves
```

The simulation still receives one `Ctx` and one `Schedule`, built before it
runs. Their beats are the committed beats, and `Ctx` gains a `reactive` field
holding the whole stream with its known-at times. That keeps the change small
and the simulation deterministic. It also means the code *could* read the
future, so causality is enforced by checks ([Causality](#causality)) rather
than by the data structures. A live analyzer needs append-only structures
instead.

### Rules that hold everywhere

- **No early reads.** A simulation decision at song time `s` depends only on
  stream entries with `knownAt <= s`. A rendered frame at song time `s` uses
  only entries known by `s`.
- **Committed beats are final.** They are never revised, so strikes and sounds
  placed on them never move.
- **Judgment is authoritative only when settled** (from stage 2). The
  provisional rating is feedback; only the settled rating changes the groove.
- **Play mode is untouched.** Every reactive behaviour is switched on by the
  reactive level (`reactive: true`). New recorder and plan fields are absent
  from non-reactive runs, so the golden hashes do not move.
- **Always playable.** From bead `hv-s4pt.3` on, every bead ends with reactive
  mode playable on the Galop.

## The Belief Stream

A new analysis command writes one JSON file per track:

```sh
npm run analysis -- stream public/audio/orpheus-galop.flac \
  --out public/reactive/orpheus-galop/stream.json \
  --songmap songs/orpheus-galop/songmap.json
```

Stage 1 builds the beats, ticks, onsets and the file format. Stage 2 adds
hindsight grids, stage 4 structure, and stage 5 accent forecasts.

### What the tracker computes

- **Activations:** madmom's online beat model
  (`RNNBeatProcessor(online=True, origin='online')`, 100 activation frames a
  second). Each activation frame's window ends at its own time, so it hears
  nothing later.
- **Belief:** the forward algorithm of madmom's beat tracking network
  (`DBNBeatTrackingProcessor`, 55 to 215 beats per minute), stepped one
  activation frame at a time. The most likely state at each frame gives the
  time since the last beat and the beat period. These define the **believed
  grid** at that frame: the last beat, and the beats one period apart before
  and after it.
- **Calibration:** one constant offset, `biasS`, measured on the synthetic
  tracks (about −10 ms in the experiment). It is applied to every beat time
  the stream reports. It is unrelated to the player's input calibration.

### Committed beats

A committed beat is the tracker's forecast of a beat, fixed once it falls
within the **commit horizon** and never revised:

- **Commit horizon:** 2 beat periods plus 0.35 s, using the period believed at
  that activation frame. At 120 beats per minute that is 1.35 s.
- **Why so far ahead:** the simulation reads past the current beat in many
  places. Examples: a swing's wind-up and its heavy-or-light choice, the stride
  period, the off-beat midpoint, sixteenth slots, a downbeat on a 2-beat bar,
  and the groove drain's bar length. Live play also runs the simulation 0.2 s
  ahead of the audio, and sounds need 0.06 s of notice.
- **Rule:** at each activation frame, the next beat after the last committed
  one (at least half a period after it) is committed once its forecast falls
  within the horizon. If a sudden belief change puts it less than 0.1 s ahead,
  it is skipped rather than committed late.
- **Entry:** `{ t, knownAt, period, evidence }`:
  - `knownAt` is the activation frame at which it was committed;
  - `evidence` is the evidence at `knownAt`;
  - times are strictly increasing.
- **Warm-up:** beats committed before the tracker locks carry low evidence, so
  they are muted and unjudged like any other unclear beat.
- **Phase check** (reactive-feel, `hv-jtsz.18`): the causal model can lock half
  or a quarter of a beat off, where its activations peak on off-beats. The
  offline model hears both sides of each beat and rarely does. So the committed
  grid takes the phase of the [hindsight grids](#hindsight-grids-stage-2) when:
  - the last two entries' offline beats, settled (at least 1 s before the
    entry's `knownAt`), agree on a phase at least 0.2 periods off the believed
    one;
  - each entry has at least 4 such beats, 90% of its intervals within 6% of
    the believed period (a decode at another tempo or level, or across a tempo
    change, says nothing about the phase).

  It is causal: a frame reads only entries known by then. On the iteration
  corpus this raised strict beat F from 0.73 to 0.84. It left the Galop's and
  the steady synthetic tracks' accuracy unchanged.

The experiment measured commit leads of only 0.25 s and 0.5 s. On steady
beat-driven music, going from 0.25 s to 0.5 s cost under a point. The 1.35 s
horizon is unmeasured, so a check gates its accuracy on the synthetic tracks
([Automated Checks](#automated-checks)). If that gate fails, the executor
takes the fallback below without asking.

**Fallback if the horizon costs too much:** shorten the horizon to 1 period
plus 0.35 s. Then rewrite the reads that need the beat after next to
extrapolate it from the last two known beats instead:

- the groove drain's bar length;
- the off-beat midpoint after the next beat;
- the second beat of a 2-beat bar;
- the boss slam's mark, 2 beats before it lands (stage 6);
- the archer volley's draw, 1 beat before its release (stage 6).

The perturbation check verifies the rewrites.

**When to take it:** if either
- the synthetic stream-quality gate fails, or
- the Galop's pulsed sections have fewer committed beats within 45 ms of a
  song-map beat than 79%, 5 points under the experiment's 84% at a 0.25 s lead.

If the Galop is still under 79% after the fallback, the executor records the
figure in the bead's notes and continues: the parity report tracks it.

**Taken in `hv-s4pt.1`.** At 2 periods plus 0.35 s, 54% of the Galop's
pulsed-section committed beats fell within 45 ms of a song-map beat, under
79%, so the stream commits at 1 period plus 0.35 s (67%, still under 79%:
recorded and tracked). The synthetic gate passed either way. `hv-s4pt.2`
therefore makes the extrapolation rewrites listed above, and has the stream
commit each next beat at the latest when the beat before it arrives, so the
beat the game is in always has its end known. See
[Measured: stage 1](#measured-stage-1) and the section after it.

### Ticks, onsets and energy

- **Ticks** every 50 ms, each with `knownAt = t`:
  - `evidence`: the mean peak beat activation within ±30 ms of the last 4
    beats of the believed grid. The tracker's own state confidence stays near
    1 even in a breakdown, so it is useless here.
  - `energy`: loudness `L` over the last 0.5 s against a slowly falling
    reference `R`, the running maximum of `L` that decays by 0.2 dB a second.
    `energy = clamp((L − R + 30 dB) / 30 dB, 0, 1)`: 1 at the loudest the music
    has recently been, 0 at 30 dB below it. A sustained loud passage, such as
    the Galop's 35 s climax, stays loud.
- **Onsets:** causal spectral-flux onsets (madmom's superflux with online peak
  picking), each `{ t, knownAt, strength }`. The experiment measured detection
  10 ms after a drum hit (90th percentile 14 ms).
- **Strong onsets:** an onset is strong when its strength is at least the 98th
  percentile of the onsets in the past 30 s. On the Galop that is about 33 a
  minute. Onsets are found in quiet passages too, but nothing uses those whose
  evidence is below the gate, apart from stage 3's flashes.
- **"The evidence at time `x`"** means the evidence of the latest tick at or
  before `x`. "The energy at `x`" likewise.

### Hindsight grids (stage 2)

- **Every 1 s** at time `T`, madmom's offline beat model (bidirectional LSTM)
  runs on the audio from `T − 20 s` to `T`, and its offline beat tracker
  decodes that window.
- **Entry:** `{ knownAt: T, offline, online }`, keeping beats from `T − 8 s`
  to `T`:
  - `offline`: the offline decode's beats;
  - `online`: the causal tracker's believed beats over the same span (its last
    beat each period, as believed at each activation frame up to `T`).

  The readings choose from both, because neither wins everywhere: on the
  beat-driven tracks the offline decode matched 97% of beats against the
  online model's 92%, but on the Galop the offline decode settles on half
  tempo and matched only 57–59% against the online model's 75–76%. See the
  [appendix](#appendix-evidence).
- **Runtime:** one offline decode per second of audio over 20 s, about one to
  two minutes of analysis per minute of music on the developer's machine.

### Structure (stage 4)

- **Changes:** novelty between the past 8 s and the next 2 s of features
  (chroma, MFCCs and loudness, standardized with statistics from the past
  30 s). A change at `b` is declared when its novelty is the largest within
  ±4 s and above a running threshold (the past 60 s's median plus 0.5 times
  the gap between their 90th percentile and median), so it is known at
  `b + 6 s`. Entry: `{ t: b, knownAt, strength }`.
- **Jumps:** the loudness of the last 1 s against the 6 s before it. A rise or
  fall of 6 dB or more, at most one every 4 s, is reported at the end of that
  1 s, with `t` 1 s earlier. Entry: `{ t, knownAt, db }`. On the Galop most
  were found within 1 s; the vamp's was 1.7 s off and known 2.7 s after the
  song map's start.
- **Level:** each tick gains `level`, one of `quiet`, `medium` or `loud`, from
  energy with hysteresis: enter `loud` at 0.8 or more (within 6 dB of the
  reference) and leave below 0.7; enter `quiet` below 0.4 and leave at 0.5 or
  more.
- **Key:** each tick gains `key`, such as `"D major"`, from chroma over the past
  20 s matched against major and minor key profiles. It is absent until 10 s of
  audio have passed.
- **End:** `{ t, knownAt }` when the loudness stays at least 20 dB below the
  loudest second of the past 30 s for 1.5 s, after at least 10 s of audio. `t`
  is where the quiet began, and `knownAt` is 1.5 s later. Several may occur;
  each is a candidate ending. On the Galop this rule found only the real
  ending, `t` 142.8 s and known at 144.2 s: 4.6 s after the last tutti chord,
  because the final chord rings on.
- On the Galop in the experiment, novelty found 4 of the 8 section starts from
  the vamp to the coda with no false alarms, and loudness jumps found the vamp,
  the opening, the kick line and the bridge. Six of the jumps' eight false
  alarms fell in the free prelude, so the game ignores structural events whose
  evidence is below the gate. See the [appendix](#appendix-evidence).

### Accents (stage 5)

- **Detected accents** are the strong onsets.
- **Forecast accents:** when strong onsets have landed within 50 ms of the same
  position in a 2-, 4- or 8-beat cycle of committed beats at least 3 cycles in
  a row, the next one is forecast on the committed beat at that position. The
  Galop's opening tutti chords repeat every 8 beats (54.07, 56.78, 59.53 and
  62.30 s).
  - **When it is known:** at the later of the beat's `knownAt` and the onset
    that confirms the pattern. It is forecast only if that leaves at least the
    swing's wind-up plus 0.35 s before the beat.
  - **Entry:** `{ t, knownAt, cycle, confidence }`, where `confidence` is the
    share of the last 4 cycles with a strong onset at that position.
  - **Failure:** a forecast with no strong onset within 50 ms of it stops the
    pattern until 3 more cycles confirm it.

### File format

```json
{
  "version": 1,
  "audio": { "file": "../../audio/orpheus-galop.flac", "duration": 144.6, "sampleRate": 44100 },
  "params": {
    "fps": 100, "minBpm": 55, "maxBpm": 215,
    "commit": { "periods": 1, "extraS": 0.35, "minLeadS": 0.1 },
    "gate": 0.2, "tickS": 0.05,
    "hindsight": { "everyS": 1.0, "windowS": 20, "keepS": 8 },
    "biasS": -0.013, "toolVersion": 1
  },
  "beats": [{ "t": 0.512, "knownAt": 0.0, "period": 0.469, "evidence": 0.0 }],
  "ticks": [{ "t": 0.05, "evidence": 0.0, "energy": 0.0, "level": "quiet", "key": "D major", "bands": { "kick": 0.0, "snare": 0.0, "hat": 0.0, "bass": 0.0 }, "brightness": 0.5 }],
  "onsets": [{ "t": 0.503, "knownAt": 0.51, "strength": 4.2, "strong": false, "band": "snare" }],
  "hindsight": [{ "knownAt": 1.0, "offline": [], "online": [] }],
  "structure": {
    "changes": [{ "t": 54.07, "knownAt": 60.1, "strength": 1.4 }],
    "jumps": [{ "t": 53.4, "knownAt": 54.4, "db": 6.4 }],
    "ends": [{ "t": 142.8, "knownAt": 144.2 }]
  },
  "accents": { "forecast": [{ "t": 65.03, "knownAt": 63.1, "cycle": 8, "confidence": 0.75 }] },
  "timbre": [{ "knownAt": 1.0, "brightness": 0.42, "flatness": 0.18, "percussive": 0.35, "density": 3.0 }],
  "reference": {
    "source": "songmap",
    "beats": [0.149, 0.54],
    "sections": [{ "label": "galop-vamp", "s0": 43.21, "s1": 54.07 }],
    "accents": [{ "t": 54.07, "label": "tutti-chord" }]
  }
}
```

- The audio path is relative to the stream file, so the Galop reuses
  `public/audio/orpheus-galop.flac` without a copy.
- `reference` is optional:
  - The Galop's comes from its song map (`"source": "songmap"`).
  - Synthetic tracks carry their exact beats and sections (`"exact"`).
  - Other tracks carry the offline whole-song beat decode (`"offline"`) and
    no sections.

  Only the virtual players, the parity report and the checks read it, never
  the simulation.
- The stream command prints accuracy statistics, but the checks compute their
  own from the stream and the reference.
- **Fields are keyed by presence,** not by version: a stage adds its fields, and
  the game reads the fields its stage needs. `version` stays 1.
- **Staleness:** `params.toolVersion` names the stream code's version, a
  constant the tool bumps whenever its output changes. `check:reactive` fails
  when a shipped stream's `toolVersion` differs from the tool's, so committed
  streams are regenerated with every change.
- **Synthetic streams** are computed from the shipped Ogg files, not the
  generated WAVs, so encoder delay cannot shift them.

### Causality of the stream itself

The command must never let a later sample influence an earlier entry. A Python
test proves it on a 12 s excerpt of a synthetic track: the stream of the
excerpt cut at `X` must equal the full excerpt's entries with
`knownAt <= X − 0.05`, to within 1 µs for times and 1e-6 for values. Each
stage that adds stream fields extends this test to them. Anything that
normalizes over the whole file breaks it, which is why energy, levels, keys
and novelty use only past statistics.

## Tracks

- **The Galop:** `orpheus-galop`, with its song map as reference.
- **Other classical tracks:** `william-tell-finale`, `mountain-king`,
  `bumblebee` and `beethoven-fifth`, from `public/audio/`, with offline
  references.
- **Synthetic controls,** with exact beats and sections, generated by a new
  `ps-analysis synth` command (moved from the experiment's `synth.py`):
  - `house-128`: steady four-on-the-floor.
  - `dnb-174`: drum and bass at 174 beats per minute.
  - `hiphop-92-swing`: swung, syncopated.
  - `accelerando-100-140`: a steady start, a long accelerando and a steady
    end.
  - `pop-arrangement`: a chords-only intro, verse, chorus, a pads-only
    breakdown, chorus, then a hard cut from 120 to 96 beats per minute.
- **A check-only track:** `causality-mix`, about 45 s, from the synth command,
  built so every feature fires in a short run:
  - steady drums with an accent every 8 beats;
  - a quiet stretch, then a loud entrance (a jump and a change);
  - a hard tempo cut;
  - a few seconds of pads only;
  - a final chord ringing out into silence.
- **Where they live:**
  - `public/reactive/<id>/stream.json` for every track;
  - the synthetic audio beside it as Ogg Vorbis, to keep the repository small;
  - `public/reactive/index.json`, listing each playable track's id, title and
    files (`causality-mix` is not listed).
- **Local tracks:** `npm run reactive:add -- <audio file> [--id <id>]` runs the
  stream command and copies the audio and stream into `reactive-local/<id>/` at
  the repository root, which is gitignored. It also updates
  `reactive-local/index.json`. The dev server serves that folder at
  `/reactive-local/` through a small middleware, so builds never include it.
  Local tracks get an offline reference so the virtual players can play them.
- **Loading:** the game fetches both indexes at runtime. A missing local index
  is not an error.

## The Reactive Song Map and Level

### The reactive song map

Built in the browser from a stream:

- `beats`: the committed beat times.
- `meter`: 2 beats to the bar throughout, counted from the first committed
  beat. Bars carry no musical meaning here; 2-beat bars keep every "next
  downbeat" read within 2 beats.
- `sections`: one pulsed section, `reactive`, covering every bar. It has no
  `key`, so gem chimes use the palette's unkeyed fallback (`keyScale` returns
  undefined) until stage 4 supplies a detected key.
- `phrases`, `accents`, `tempoChanges`: empty.
- `energy`: one value per committed beat, the energy at that beat's `knownAt`.
- `audio`: from the stream. `duration` is the audio's, used only for the run's
  end.

`Ctx.reactive` holds the stream itself, without `reference`: the committed
beats with their known-at times and every other field. Code that needs "the
first beat known by a time" reads it there. The reference goes only to the
virtual players, the parity report and the checks.

### The reactive level

`levels/reactive.yaml` is a level script for that one section, plus a
`director` block that only reactive levels may use. Each stage extends the
block. Stage 1's:

```yaml
song: reactive
seed: 2026
player: reactive-moderate
reactive: true
upgrades: { loadout: [orbiting-blades, chain-lightning], offer: [fire-trail] }
zones: [twilight-glade, galop-woods, mushroom-grove, ember-hollow, infernal-grove]
sections:
  - section: reactive
    bars: all
    presses: "xx"
    zone: twilight-glade
    camera: { zoom: 1.2, lead: 50, punchOnAccents: false }
    sfxPalette: crisp-steel
director:
  population: { start: 40, perMinute: 120, max: 480, energyScale: [0.7, 1.3] }
  roster:
    - { from: 0,   enemies: { slime: 2, bat: 1 } }
    - { from: 45,  enemies: { goblin: 3, slime: 2, bat: 1 } }
    - { from: 90,  enemies: { goblin: 3, orc: 2, skeleton: 1, orc-elite: 0.03 } }
    - { from: 150, enemies: { orc-ember: 3, skeleton-ember: 2, goblin: 2, orc-elite: 0.03 } }
  levelUps: [{ at: 30, xp: 100 }, { at: 90, xp: 470 }, { at: 150, xp: 800 }]
  zoneEvery: 40
```

- **Starting values only.** The numbers here and in later stages' blocks are
  starting values. The executor may tune them; the checks gate behaviour, not
  these values.
- **Population:** the target at song time `s` is
  `min(max, (start + perMinute × s / 60) × scale)`, where `scale` maps the
  energy at `s` into `energyScale`. It replaces `targetPopulation`'s
  interpolation over the section, which reads the section's end.
- **Roster:** the entry with the largest `from` at or before `s`.
- **Level-ups:** each falls due at the first committed beat at or after its
  `at`, among the beats known when `at` arrives.
- **Effective downbeat:** wherever this document says something happens "on
  the next committed downbeat" after an event known at `k`, it means the first
  committed beat on a bar's first beat with `t >= k + 0.35 s`, among the beats
  known at `k`. If none is known yet, it is the first such beat to be
  committed.
- **Zone waves:** every `zoneEvery` seconds, on the first known committed beat
  at or after that time, in the order of `zones`.
- **Off until a later stage brings it back:** level-script events, accent
  punches, cleaves, set pieces, the blast, and time-scale changes (the base
  rate stays 1).
- **Structural rewrites:** two features schedule work from song-wide data at
  start-up. The causality check would not object, because each queued event
  fires at a known beat, but a live analyzer cannot provide song-wide data, so
  reactive mode rewrites them now:
  - weapon fires are queued one beat at a time as each beat event fires,
    instead of to the end of the song at start-up (`scheduleFires`);
  - an upgrade orb's 2-bar deadline is counted in known committed beats as they
    arrive, with no fallback to `duration` (`riseOrbs`).
- **The run ends** when the audio ends, or from stage 6 when the finale's
  epilogue ends, whichever is later. The end card shows the letter grade from
  the average groove from 16 s on (the settled groove from stage 2, judged
  or not, `hv-s4pt.13`).
  Settles that would fall after the run's end are dropped.

## Stage 1: Foundation

**Playable result:** the Galop, the other classical tracks and the synthetic
controls play from start to end in reactive mode. Strikes, weapons and their
sounds follow the committed beats. Presses are judged instantly. The slow
prelude and other unclear stretches are not judged and play without
percussive gameplay sounds.

- **The belief stream:** beats, ticks and onsets, the file format, the stream
  causality test, every track above, and `reactive:add`.
- **The reactive song map and level,** and the stage 1 director above.
- **Judging:** `classifyPress` against the committed beats, instantly, as play
  mode judges against its grid today.
- **The gate:**
  - a press whose evidence at its time is below the gate is **unjudged**: it
    shows grey and changes nothing;
  - the drain clock stops below the gate (see
    [When the beat is unclear](#when-the-beat-is-unclear));
  - a percussive gameplay sound whose slot belongs to a beat committed below
    the gate is not played. A sub-beat slot belongs to the latest committed
    beat at or before it.
  - **Exempt sounds:** hurt, level-up, tier chimes and the orb being taken play
    below the gate too, because they carry information.
- **The mode in the app** ([Mode and Flow](#mode-and-flow)).
- **Virtual players** ([Virtual Players](#virtual-players)) and a check-only
  option that turns off damage to the hero.
- **The causality check** ([Causality](#causality)).
- **The parity report,** first rows ([The Parity Report](#the-parity-report)).

## Stage 2: Fair Judging

**Playable result:** presses are judged against what the music actually did,
2 s later. Where the tracker locks at the wrong tempo or on the off-beat, as it
does in the Galop's vamp and theme A repeat, the player's own presses steer
the judgment back.

### Provisional and settled ratings

Each press is judged twice:

- **Provisional, at the press:** `classifyPress` against the committed beats,
  with its own memory, so it never disturbs the settled judgment. It drives the
  instant feedback (the overhead rating, and from stage 3 the press ripple) and
  the play log. It never changes the groove.
- **Settled, at the press time plus 2 s:** `classifyPress` against the chosen
  reading of the latest hindsight entry with `knownAt <= p + 2`, so every press
  is judged with between 1 s and 2 s of hindsight. This is the authoritative
  class and rating. It changes the groove as
  [Timing and the Groove](gameplay-redesign.md#timing-and-the-groove) says:
  same classes, windows, drift, values, tiers and hysteresis.

Press times are the player's press times as today: the key event's song time
less the player's input calibration offset. The stream's `biasS` is already
inside the beat times.

### Beat identity

`PressMemo` remembers counted presses and taken off-beats by grid index. That
is safe today because the grid never changes. Each settle uses a different
hindsight entry and possibly a different reading, so index 5 names different
beats from one settle to the next. The settled memory therefore keys by
**beat identity**:

- **A beat registry** holds the beat times settled judging has seen. A grid
  time within the match tolerance of a registered beat is that beat; otherwise
  it is registered as a new one. The tolerance is 70 ms, or a quarter of the
  chosen grid's shortest spacing if that is less: a double reading at 215 beats
  per minute spaces its beats 139.5 ms apart.
- **Counted presses** are remembered by the beat's identity.
- **Off-beats** are remembered by the identity of the beat before them.
- **`classifyPress` gains an optional key function** mapping a grid index to the
  key its memory uses. Its default is the index itself, so play mode behaves
  and hashes exactly as before.
- **Example:** a press counted for the beat at 10.000 s under "as tracked"
  still blocks a second counted press for that beat after the reading changes
  to "half A", whose grid includes 10.004 s.
- **The registry, the settled memory and the incumbent reading live in the
  simulation state,** so snapshots, clones and rollback carry them.

### Choosing the reading

Every hindsight entry yields ten candidate grids: five from each of its two
beat lists `D` (`offline` and `online`):

- **as tracked:** `D`
- **off-beat:** the midpoints of `D`
- **double:** `D` and its midpoints
- **half A** and **half B:** every other beat of `D`, starting with the first or
  the second

The presses that choose are every judged press in the window except partner
presses (a diagonal's or a rock's second key), whatever their class. Since the
fair-judging revision (`hv-s4pt.12`):

- **Judged presses only:** presses made while unjudged, the beat unclear, do
  not choose. In two playtests the Galop's rubato prelude left a half-tempo
  reading that its presses had chosen.
- **A fresh start:** after an unclear stretch the reading starts again from
  offline "as tracked". Only presses since judging resumed choose it.

The reading used to settle a press `p` is chosen from the entry used for it
(time `T`) and the presses in `[T − 8 s, T]`, **leaving out `p` itself**:

```text
for each candidate c:
  hits   = share of those presses within 45 ms of a beat of c
  chance = min(1, 2 × 0.045 s × (beats of c per second))
  score  = hits − chance
the incumbent is the candidate whose beats best match the grid chosen for the
previous settle (most beats within the match tolerance where they overlap);
at the first settle, or with no overlap, it is offline "as tracked"
the best-scoring candidate c replaces the incumbent i only if
  d = score(c) − score(i) > 0.10   and   d > k × sqrt((h_c(1 − h_c) + h_i(1 − h_i)) / n)
with k = 1 when c reads the grid "as tracked" and k = 2 otherwise;
switching away from "as tracked" needs at least 12 presses
where h are the candidates' hit shares and n the number of presses;
with fewer than 8 presses the incumbent stays
```

- **The chance term:** random presses hit any grid at about its chance rate, so
  their scores sit near 0 for every candidate. Without the term, a masher at 4
  presses a second picked the double-tempo grid and doubled its Perfect share
  (see the [appendix](#appendix-evidence)).
- **Leaving out `p`** stops a press from choosing the grid that rates it.
- **The significance test** stops the reading from wandering on noise, and
  leans towards "as tracked": switching back to it takes one standard error,
  and switching away from it takes two.
  - The original factor of 3 kept a wrong incumbent in play for 6 s or more of
    a steady player's presses on the Galop. The right reading led by about 0.20
    against a required 0.38.
  - Switching away from "as tracked" also needs at least 12 presses: right after
    a restart, a handful of random presses can look like a reading.
- **The incumbent is found by matching beats** because the labels "half A" and
  "half B" depend on which beat opens the 8 s window, which changes as the
  window slides.
- **Knobs:** the window (8 s), the margin (0.10), the significance factors (2,
  and 1 back to "as tracked") and the hit window may be tuned.
- **The chosen grid goes to `classifyPress` unchanged,** so every class and
  rating works as in play mode: off-beats, loose counted presses and strays
  included. The press settles unjudged only if the chosen grid has no beat
  within one of its own periods of the press, or the entry has no beats.

Where readings matter, at 120 beats per minute (beat 0.5 s):

```text
the tracker locked at half tempo (grid beats 1 s apart), player on every beat
  as tracked  hits 0.50 (every other press)   chance 0.09   score 0.41
  double      hits 0.95                       chance 0.18   score 0.77  chosen
  without readings, every other press lands on the half grid's off-beat,
  where it earns nothing at best

the player presses on every off-beat, grid as tracked
  as tracked  hits 0.02    chance 0.18   score -0.16
  off-beat    hits 0.95    chance 0.18   score  0.77   chosen

the player presses every other beat, grid as tracked
  as tracked  hits 0.95    chance 0.18   score 0.77
  half A      hits 0.95    chance 0.09   score 0.86
  half A beats the incumbent (as tracked) by 0.09, under the margin: as
  tracked stays. Either way each press is the counted press for its beat.
```

**The double reading changes off-beat economics.** Under it, an eighth-note
player's in-between presses become counted presses (Perfect +0.08) rather than
off-beats (worth 0 in play mode's tuning). That is accepted: those presses are
on the music's eighth notes. The chance term keeps mashers from reaching the
double reading.

### The settled groove timeline

The meter, its drain and the tier run as today, but on events shifted by the
settle delay:

- **Settle events:** each press at `p` queues a settle event at `p + 2`, which
  classifies the press and applies the groove change. A raise sets the drain's
  latest-raise time to `p`, not `p + 2`, so the whole meter runs on the press's
  timeline shifted by 2 s.
- **Drain events:** each committed beat at `b` queues a drain event at `b + 2`,
  which applies today's drain rule as of time `b`.
- **The tier** follows the settled groove, so weapon sizes, damage, the pickup
  radius and the tier chimes lag the player's timing by 2 s.
- **Pending settles and drains live in the simulation state,** so snapshots,
  clones and rollback carry them.
- **Drift** comes from settled counted presses, as today.
- **After the run ends,** settles still pending are dropped.

### When the beat is unclear

These rules start in stage 1 and apply to the settled judgment from stage 2.

- **Unjudged presses:** a press is unjudged while the evidence is below the gate,
  and for 1 s after it returns (the fair-judging revision, `hv-s4pt.12`).
  - Judging stops at the first tick below the gate and resumes once the beat has
    been clear for 1 s. A wobble around the gate does not flip presses between
    judged and unjudged.
  - An unjudged press still strides, shows grey, changes nothing and does not
    count as raising the groove.
- **The drain clock stops:** the time since the latest raise counts only judged
  time. A stretch without judging neither drains the groove nor brings the next
  drain closer. When judging resumes, the drain waits its grace bar again.
- **Beatless credit** (`hv-s4pt.13`): when the song has no beat the game can
  hear, the player gets credit automatically.
  - Each committed beat that falls 2 s or more into a stretch without judging
    raises the groove by a Perfect's worth (0.08), on its drain event 2 s later.
    A full meter takes about 13 beats.
  - The first 2 s of a stretch earn nothing, so a flicker in the confidence
    gives nothing away.
  - A credit does not count as a raise for the drain.
  - **Why:** the playtests started every run at 0. The Galop's 16 s prelude has
    no beat to play to, and the meter then took about 20 s of the vamp to fill.
    Quiet stretches mid-song cost the same.
- **The lock-on grace** (`hv-s4pt.13`):
  - **When:** for the first 8 s of judged time after a lock-on. A lock-on is a
    run of judging that follows 4 s or more without judging.
  - **Flickers:** a shorter stretch without judging inside the 8 s neither ends
    the grace nor starts a new one.
  - **Steady players:** a steady player (as for the benefit of the doubt) loses
    nothing to settled penalties in the grace. While they have pressed within
    the last bar, they lose nothing to the drain either.
  - **Anyone else:** settled penalties count half their usual share, and the
    drain runs as usual.
  - **Why:** the tracker's first beats after it locks on are often off. In the
    third playtest they turned steady presses in the vamp into loose and stray
    ones.
- **The grade** averages the meter as shown from 16 s on, judged or not, with
  stretches without judging filled by the credit.
- **The benefit of the doubt** (`hv-s4pt.12`):
  - **Steady players:** a settle may not lower the provisional judgment of a
    press from a steady player. Steady means the press and the three judged
    presses before it were all provisionally Perfect or Great.
  - **Why:** a hindsight reading the game is unsure of could otherwise turn good
    playing into stray presses. A masher almost never strings four together, so
    its lucky ratings stay open to the settle.
- **Penalties scale with confidence** (`hv-s4pt.12`):
  - Every settled penalty, and the drain, counts in proportion to the
    evidence: none of it at the gate, all of it from 0.55.
  - A steady player's raises count in full. Anyone else's scale like a
    penalty, so pressing at random gains nothing where the tracker is unsure.
  - **Why:** in the Galop's kick line the tracker's beats ran 115–210 ms late
    with the evidence near 0.27. Both judgments rated a steady player's presses
    loose.
- In the experiment, this gate muted 91% of the pads-only breakdown and 95% of
  the Galop's free prelude, while playing everything in steady beat-driven
  sections.

### What the player sees

- **The overhead rating** shows the provisional rating at once, as today.
- **The groove bar** shows the settled groove, so it moves 2 s after the
  presses that move it.
- **A correction mark:** when a settled rating differs from the provisional one
  by a class or more, a small flash on the groove bar shows the settled
  rating's colour.
- **The play log** records each press's provisional and settled class and
  rating, the reading chosen, the evidence and the settle time.
  `npm run log:play` prints the counts by settled class and how often the
  provisional and settled ratings agree.

## Stage 3: Flourish

**Playable result:** the world breathes with the music and answers the
player. Pulses fade where the beat is unclear, strong hits flash, lighting
follows loudness, and every press ripples outward.

- **Beat-synced visuals:** today's pulses read the beat phase from the
  committed beats: renderer beat and bar pulses, the hero aura, sway, gem and
  chicken bob, the HUD heart and the overhead beat gem. Their amplitude is
  multiplied by the evidence at the frame's song time, mapped from 0 at the
  gate to 1 at twice the gate.
- **Enemy movement on the beat:** foe movement multipliers and idle frames
  follow the committed beats, as they follow the song map's beats today.
- **Accent flashes:** each strong onset gives a short screen flash and a small
  camera punch, starting at the onset's `knownAt` with no lead-in.
  - **No accent sounds:** a reactive sound on a transient would land 20 to
    50 ms late and audibly flam.
  - **Rate cap:** at most one flash every 0.5 s.
- **Loudness:** energy, smoothed over 0.5 s, drives the effect intensity (play
  mode's per-section `intensity`), the ambient light level and the density of
  ambient particles.
- **The world follows the player:**
  - **Press ripple:** every press sends a ring out from the hero, tinted by its
    provisional rating (grey when unjudged, red when stray).
  - **Pulse source:** while the evidence is below the gate, the world's beat
    pulse follows the player's presses, with a pulse on each press decaying
    over 250 ms. Above the gate it follows the committed beats, crossfading
    over 0.5 s.
  - **Agreement glow:** the ground light brightens with the share of the last
    2 s of presses whose provisional rating was Great or better.
- **Sound fades:** instead of cutting out at the gate, percussive gameplay
  sounds fade from full volume at twice the gate to silent at the gate, using
  the evidence of the beat their slot belongs to.
- **No free movement** (the fair-judging revision, `hv-s4pt.12`, superseding
  this stage's free movement while unclear):
  - Strides and the beat display stay on throughout.
  - While the beat is unclear, presses stride unjudged and the groove holds.
  - The beat track's ticks soften with the share of the last half second with
    the beat clear, down to 30% but never gone.
  - **Why:** in playtests the switch flipped controls and display on confidence
    dips the player could not hear, stretched by its 2 s rules.

## Stage 4: Structure

**Playable result:** scenes change when the music does. A big entrance sweeps
in a new zone, the roster and camera shift with the music's sections, quiet
passages calm the horde, and gem chimes follow the music's key.

### Detected sections

The director keeps a **detected section** counter, starting at 0:

- **A new section starts** on a change, or on a jump of 6 dB or more, whose
  evidence at `knownAt` is at or above the gate, provided the previous start's
  `knownAt` was at least 8 s earlier.
- **It takes effect on the effective downbeat** after the event's `knownAt`
  (changes are known about 6 s late, jumps about 1 s late). That downbeat is
  the section start's time for everything below, including the checks.

### What a new section does

- **Zone wave:** the next zone in `zones` sweeps out, replacing stage 1's timer.
  If no section has started for 60 s, a wave runs anyway.
- **Roster:** the roster steps to the next entry of a new `sections` list in the
  director, one entry per detected section, holding the last entry after the
  list ends.
- **Flourish:** a short banner flash and a camera punch mark the start.

### Loudness level

- **Population:** the target becomes
  `min(max, (start + perMinute × s / 60) × levelScale[level])`, with
  `levelScale` in the director (starting at quiet 0.6, medium 1.0, loud 1.3).
  It changes smoothly over 2 s.
- **Camera:** zoom and lead per level from the director, in the spirit of the
  Galop's quiet prelude (zoomed in, slow) and its climax (zoomed out).
- **Sound palette:** `soft-chimes` when quiet, `crisp-steel` when medium and
  `heavy-infernal` when loud, switching on the next committed downbeat after a
  level change that lasts 2 s.
- **Calm:** while quiet, enemies drift in slowly, as in the Galop's prelude
  (`calm` 0.5).

### Level-ups

A level-up falls due at the start of the next detected section once its XP is
reached, or 20 s after the XP is reached if no section starts in that time.
This replaces stage 1's fixed times.

### Key

Each gem chime uses the detected key at the moment it is scheduled, so chimes
are pitched in the music's key, as play mode's are in each section's authored
key.

## Stage 5: Accents

**Playable result:** heavy swings land on the music's big hits again, and a
press on a strong hit launches a cleave.

- **Heavy swings:** a strike whose committed beat carries a forecast accent
  with `confidence` at least 0.6 is heavy, as a strike on an authored accent
  is today. The decision is known at wind-up, because the forecast is.
- **Cleaves:**
  - **On a forecast accent:** the heavy strike leaves its pending cleave, and a
    counted press of Great or better launches it, exactly as today.
  - **On a detected accent that was not forecast:** a pending cleave is created
    at the onset's `knownAt`, as if a heavy strike had landed on the onset. A
    counted press of Great or better within play mode's window of the onset
    launches it, at the later of the press and the onset's `knownAt`.
- **Cleave sounds:** the shockwave bands start on the first eighth-note slot at
  least 0.06 s after the launch, then sixteenths, as
  [Sound on the Grid](gameplay-redesign.md#sound-on-the-grid) says for the
  eighth after the accent. For a late press that may be the eighth after the
  next.
- **No heavy impact on unforecast accents:** the heavy impact sound on the
  accent itself needs a forecast; an unforecast accent gets the flash and the
  bands only.
- **Accent punches:** camera punches on forecast accents get play mode's 50 ms
  lead-in.

## Stage 6: Set Pieces

**Playable result:** the horde's set pieces return, triggered by the music.
Rings close on big hits, stampedes break in on loud entrances, archers volley
in loud passages, a dance line performs and breaks, the boss arrives for the
loudest stretch, and the run ends with the finale.

Each set piece reuses play mode's implementation where it can. A set piece
starts on the effective downbeat after its trigger's `knownAt`, never within
6 s of another set piece's start. When two trigger together, the order is: the
boss, then a ring, a stampede, a formation and volleys.

**Open-ended set pieces.** Play mode fixes a ring's closing time and a
formation's release when it spawns, but reactive mode learns them later. So
ring members and formation slots hold their radius or position, and the close
or release starts when its trigger is known and lasts a fixed time. The
recorder's formation and ring records are written when the close or release
starts, not at entry.

- **Stampede:** on a rising jump of 6 dB or more after at least 8 s that were
  not loud.
- **Closing ring:** on a detected section start whose level is loud. Members
  hold their radius. The close starts at the `knownAt` of the first forecast or
  detected accent at least 3 s after the ring starts, or after 12 committed
  beats if none comes. It lasts until the next effective downbeat, so the ring
  closes on a beat a cleave can meet.
- **Archer volleys:** while the level is loud, a group of archers volleys every
  2 bars of committed beats, with play mode's timing: the draw 1 beat before the
  release and the arrival 1 beat after it.
- **Formations:**
  - **Bee wedge:** enters on a detected section start.
  - **Skeleton kick line:** enters when a medium-level stretch has lasted
    16 committed beats. Its entrance takes 8 committed beats, counted as they
    arrive, then it dances on the committed beats. It releases at the `knownAt`
    of the first strong onset after at least 8 dancing beats, or after
    32 beats.
- **The boss:**
  - **Entry:** on the first detected section start after 90 s whose level is
    loud. It drops in on the effective downbeat at least 0.6 s after the start's
    `knownAt`, with the standing-stones landmark.
  - **Slams:** on every other committed downbeat while the level is loud.
  - **Exit:** the finale's blast kills it. If it is still alive 90 s after
    entering, it retreats off screen with its exit animation.
- **The finale:** reactive mode's version of the blast on the last tutti chord.
  - **When:** on the first candidate ending whose `knownAt` falls within the
    last 10 s of the audio, or at the audio's end if none does. This is the
    only use of the file's length, which reactive mode learns at load. A live
    stage needs another rule.
  - **Once:** the finale fires at most once.
  - **What it does:** the finale blast, the ruined-shrine landmark, and a 0.4
    slow-motion swell over the next 1.5 s, starting at the finale.
  - **Its sounds** are exempt from the gate, like the level-up sounds.
  - **The epilogue:** the run continues after the audio ends until 3 s after
    the finale, so its bands, swell and landmark play out. The end card follows.

The authored Galop's moments land within a few seconds of the music in this
scheme, not exactly on it. The finale in particular comes about 4.6 s after
the last chord, 0.4 s before the audio ends, once the chord has rung out. It
plays as a short epilogue, not on the chord. The parity report measures each
gap.

## Stage 7: Parity

**Playable result:** the reactive Galop plays like a finished level. An honest
player survives a hard run, mashers fall, and the grade, Watch the demo and
Watch my run work as in play mode.

- **Difficulty curve:** tune the director, enemy damage and healing so that on
  the reactive Galop:
  - the honest reactive players (`reactive-moderate`, `reactive-human` and
    `reactive-skilled`) survive;
  - `holder` and the mashers fall between 1:30 and 2:10, as play mode's curve
    requires.
- **Healing:** chickens from elites and level-up heals as in play mode.
- **Grade:** letter-grade thresholds retuned so `reactive-moderate` earns an A
  on the Galop and the mashers a C.
- **Watch the demo:** a reactive demo, `?mode=reactive&track=<id>&player=reactive-skilled`,
  recorded and played back like play mode's demo, with a new `reactive-skilled`
  player (every beat, 20 ms standard deviation, accents cleaved).
- **The parity pass:** walk through every row of the parity report and the
  [Experience Map](#the-experience-map), close what can be closed, and record
  in this document what remains different and why.

### What remains different (`hv-s4pt.11`)

**Closed in stage 7:**

- The difficulty curve and its healing.
- The grade.
- Watch the demo.
- The parity report's survival row. It runs the same presses with damage on,
  since the rest of the report runs with damage off.

The tuning:

- **Health and horde.** The hero has 200 health, and the horde grows from
  nothing at 240 a minute.
- **Fewer chickens.** Orc elites make up 1% of a roster, so chickens are rare,
  as play mode intends.
- **A gentler boss.** It slams every 8 beats rather than every 4. Its slams
  landed on honest players and mashers alike, so they hid the difference the
  horde makes.

Each row below compares play mode with reactive mode on the Galop (parity
report, seed 1858):

| Row | Play mode | Reactive | Why it stays different |
|---|---|---|---|
| Strikes within 20 ms of a song-map beat | 100% | 34% | Strikes land on committed beats. The causal tracker's beats on the Galop are within 45 ms of the song map's 58% of the time (stage 1). Nothing here can close that without the tracker hearing ahead. |
| Gameplay sounds on their slot | 100% | 48% | The same cause. |
| Presses rated as the authored grid would | — | 53% | The settle rates against hindsight readings. The benefit of the doubt and beatless credit then let the meter disagree with the grid in the player's favour. |
| Tier 2 or higher, pulsed sections | 97% | 100% | Beatless credit and the benefit of the doubt (`hv-s4pt.12`, `.13`). |
| Average groove and grade | 0.96 (S) | 0.99 (A) | Reactive mode grades the meter as shown, beatless stretches included, so honest players sit near 1. Its thresholds differ (S 0.995, A 0.85, B 0.70). |
| Kills | 7,260 | 10,190 | The horde grows with elapsed time and loudness, not per section. The prelude and the vamp hold several times the authored population (532 and 523 kills, against 76 and 144). |
| Zone waves, false ones | 4, 0 | 6, 2 | Detected sections (stage 4): 4 of 8 song-map starts found, 1 false start in the galop and 1 in the prelude. |
| Heavy swings on accents | 22 of 23 | 0 of 1 | Only 2 accent forecasts on the Galop reach 0.6 confidence: its tutti chords do not repeat at a cycle the forecaster finds (stage 5). |
| Cleaves | 18 | 15 | 14 of the 15 are onset cleaves, on detected strong onsets, for want of forecasts. |
| Set pieces | rings 3, stampedes 2, volleys 1 run, bee wedges 2, kick line 1, boss 104 s, finale 139.6 s | rings 2, stampedes 0, volleys 4 runs, bee wedges 1, kick line 1, boss 108 s, finale 144.3 s | Triggered by the music, not by script (stage 6). No stampede: no loudness jump of 6 dB after 8 quiet seconds. |
| Lowest health, survival | 75%, lives | 71%, lives | Close. On seed 2026: 70% against 37%. |

**The Experience Map, row by row:**

- **Every row has its counterpart.** The difficulty, healing and grade row and
  the Watch the demo row are now closed.
- **Free movement in unclear stretches** (stage 3) was dropped by `hv-s4pt.12`.
  Beatless credit (`hv-s4pt.13`) takes its place.
- **Sounds on the grid, beat pulses, camera punches** and the other rows that
  ride on committed beats carry the tracker's error. The strikes and sounds
  rows above measure it.

## Mode and Flow

- **Choosing it:** the title screen gains **Reactive (experimental)**, which
  opens a track list from the indexes. `?mode=reactive&track=<id>` skips the
  title screen's choice. `?player=` works in reactive mode with the reactive
  players.
- **Loading:** the game fetches the track's `stream.json`, builds the reactive
  song map, loads `levels/reactive.yaml` with the shipped content, and plays
  the track's audio through the existing audio engine.
- **Play flow:** count-ins, pause, Restart song, Quit to title, YOU FELL, the end
  card and Watch my run work as in play mode. Restart section restarts the
  song, since there is one section. `?start=` hands over after
  `reactive-moderate` plays up to it.
- **Title card:** the track's title from the index.
- **Best grade:** saved per track under its own key.
- **Live play:** the runner, rollback, snapshots and sound scheduling are
  unchanged.

## Virtual Players

Reactive players press against the stream's reference beats, as a human hears
the music, never against committed beats or hindsight grids:

- `reactive-moderate` and `reactive-human` use play mode's `moderate` and
  `human` profiles unchanged (timing spread, bias, press rate and sloppiness),
  pressing on the reference beats.
- `reactive-skilled` (stage 7, the reactive demo's player) uses play mode's
  `skilled` profile with a 20 ms standard deviation (clamped at 60 ms). It
  presses every reference beat, so it presses on every accent that falls on a
  beat.
- `reactive-half`: `moderate`'s profile on every other reference beat.
- `reactive-offbeat`: `moderate`'s profile on every reference off-beat.
- `mash4`, `mash8` and `holder`: as in play mode.

Steering reuses the existing roaming and threat avoidance. On the Galop, the
reactive players press only in the song map's pulsed sections and roam in the
prelude, as play mode's players do.

## The Parity Report

`npm run reactive:parity -- --track orpheus-galop` runs the authored run and
the reactive run of the Galop with `reactive-moderate`'s reference presses.
The authored run uses play mode's level, `levels/orpheus-galop.play.yaml`, and
both runs use seed 1858 with damage off. It prints one row per measure, with
both values and the gap.

| Row | Authored | Reactive | Stage |
|---|---|---|---|
| Strikes within 20 ms of a song-map beat | ~100% | % | 1 |
| Gameplay sounds within 20 ms of their song-map slot | ~100% | % | 1 |
| Presses rated the same (authored vs reactive) | — | % | 1, 2 |
| Time at tier 2 or higher, pulsed sections | % | % | 1, 2 |
| Average groove and grade | | | 1, 2 |
| Kills, enemies alive per section | | | 1 |
| Zone waves | 4, at authored section starts | n, and how many within 8 s after a song-map section start | 4 |
| Detected section starts within 8 s after a song-map section start | — | n of 8 | 4 |
| Detected section starts in the prelude | — | n | 4 |
| False zone waves | 0 | n | 4 |
| Heavy swings on song-map accents | n | n | 5 |
| Cleaves launched | n | n | 5 |
| Set pieces, and each one's start time | list | list | 6 |
| Lowest health, survival | | | 7 |

- **Rows appear when their stage lands.** Rows from earlier stages keep
  printing, so a regression shows.
- **Not a gate.** The report is a measuring tool. Each stage's bead records its
  rows' values in its notes and in this document's appendix.
- **Any track with a reference can be compared.** Only the Galop has an
  authored level, so the authored column exists only for it.

## Causality

The simulation receives full arrays, so checks prove that nothing reads beyond
what is known.

### The perturbation check

For a cut at song time `C`, every stream entry with `knownAt > C` is changed
deterministically (seeded by the track and `C`):

- **Committed beats:**
  - every fifth is deleted;
  - an extra beat is inserted in every seventh gap;
  - the rest move by up to ±35% of their period.

  Every changed beat stays strictly increasing and later than both
  `max(knownAt, C) + 0.1 s` and the last unchanged beat plus a quarter period.
- **Every field of every changed entry** is changed, including `period`,
  `evidence` and `knownAt` (kept after `C`).
- **Ticks:** evidence, energy, level and key are replaced with seeded random
  values.
- **Onsets:** strengths and `strong` are replaced with seeded random values,
  times move by up to ±50 ms, and every third is deleted.
- **Hindsight entries:** their beats move by up to ±35% of a period, and every
  fifth entry loses half its beats.
- **Structure and accent entries:** times move by up to ±2 s (and stay after
  `C`), strengths and confidences are replaced, every third is deleted, and an
  extra one is inserted between every pair.
- **The song's length:** `audio.duration` becomes `C` plus a seeded value
  between 10 s and 60 s, for every cut. The finale's window (the last 10 s)
  is computed once from the true length and passed in as its own input, so it
  needs no exception.
- **Presses:** key events after `C` are dropped in the changed run, as play
  mode's causality check drops them, so a read of later presses also shows.
- **Rebuilt:** the check rebuilds the reactive song map, `Ctx` and `Schedule`
  from the changed stream; nothing is shared with the true run.

The run with the changed stream and the same presses must match the run with
the true stream before `C`. "Match" covers:

- **The simulation:** `prefixDifference`'s comparisons, plus:
  - the settled groove, the tier and tier chimes;
  - gems, level-ups and zone waves;
  - the director's detected section and level;
  - set pieces, accents, cleaves and heavy swings.
- **The sounds:** the live recording's sound events (sample, slot and gain)
  before `C`.
- **The frames:** the evaluator's frame state (HUD values, beat phase, pulses
  and effects) at every 1/30 s before `C`.

The reactive comparisons live in a reactive variant of `prefixDifference`, so
play mode's own causality check in `check:seeds` stays exactly as it is.

### Cuts

- **`causality-mix`:** a cut just after every `knownAt` of a committed beat,
  hindsight entry, strong onset, structural event or accent forecast, and after
  every level-up, zone wave and set piece.
- **A compressed director:** the check runs it with a level override that makes
  every rule constant small enough for the track: level-ups at 5, 10 and 15 s,
  the boss after 15 s, the kick line after 4 medium beats, short cooldowns, and
  20 s statistics windows. Every such constant is a director or stream
  parameter for this reason.
- **Proof that it exercised everything:** the check fails unless every feature
  class (each set piece, the finale, level-ups, zone waves, forecasts, cleaves
  and reading switches) fired before at least one cut.
- **The Galop and the shipped tracks:** a cut every 10 s, in
  `npm run check:reactive -- --full`. Every bead from stage 4 on runs it and
  records the result in its notes.

### Players and the hero

- **Presses:** the checks run `reactive-moderate` and `mash4`, recording the
  presses from the true run and feeding the same key events to the changed run.
- **Damage off:** check runs use the check-only option that turns damage to the
  hero off, so an untuned director cannot end a run early.

### A negative control

A unit test runs the check against a simulation variant with a test-only option
that makes the spawn count read a stream entry whose `knownAt` is after the
current time: the evidence of the tick 1 s ahead. The check must fail. This
proves the check is not passing vacuously.

### Not covered

The stream is precomputed, and the rule is `knownAt <= s` in simulation time.
Live play runs the simulation 0.2 s ahead of the audio, so a live analyzer will
have to deliver entries 0.2 s sooner, or treat their arrival as a rollback.
That is a later stage's problem.

## Automated Checks

`npm run check:reactive` runs in `scripts/ci`. It has two kinds of result:

- **Gates** fail the check. They guard correctness and the few behaviours the
  experiments support.
- **Targets** are printed with their values and the target beside them. Each
  stage's bead records them in its notes and in the
  [appendix](#appendix-evidence), and tunes the named knobs to approach them,
  but a missed target does not block delivery. The parity pass in stage 7 goes
  back over every missed target.

**Budget:** `check:reactive` should take under 60 s on the developer's
machine. If it does not, the executor trims in this order: cut only after every
second committed beat on `causality-mix`; then move the mechanics runs on
tracks other than `house-128` and the Galop to `--full`. The causality proof
and the play-mode gates are never trimmed.

### Gates

From stage 1:

- **Playable:** a headless smoke test loads the Galop's stream, builds the
  reactive song map and level, and runs the live runner through 10 s of song
  time with synthetic key presses, with no errors and with strikes, sounds and
  judged presses produced. (Playwright cannot run under Tollgate; an
  end-to-end test of `?mode=reactive&track=orpheus-galop` joins
  `npm run test:e2e` and `check:all` instead.)
- **Causality:** the perturbation check on `causality-mix` passes, every feature
  class fired before a cut, and the negative control fails as it should.
- **Determinism:** the same stream and players simulated twice give the same
  plan hash.
- **Fresh streams:** every shipped stream's `toolVersion` matches the tool's.
- **Stream quality:** on `house-128` and `dnb-174`, after the first 4 s, at least
  95% of committed beats fall within 45 ms of a reference beat. The experiment
  measured 98–99% at a 0.25 s lead.
- **The gate on the Galop:** at least 80% of the prelude's committed beats are
  below the gate. The experiment muted 95%.
- **Sounds:** every percussive gameplay sound lies on a committed-beat slot of
  its subdivision within 1 ms, and none belongs to a beat committed below the
  gate, apart from the exempt sounds.
- **Play mode unchanged:** `npm test` (with the golden hashes),
  `npm run check:play` and `npm run check:seeds` pass without changing any
  fixture.

From stage 2:

- **Timing:** no groove change happens less than 2 s after the press it belongs
  to.
- **Readings,** as unit tests of the settle step with synthetic grids and
  `reactive-moderate`'s presses:
  - against a hindsight grid at half tempo it chooses "double";
  - against a grid at double tempo it chooses the half reading that holds the
    player's beats (0.95 − 0.18 = 0.77 against 0.95 − 0.36 = 0.59);
  - against a grid shifted by half a beat it chooses "off-beat";
  - in each case at least 85% of the presses settle Perfect or Great.
- **On `house-128`,** with damage off, as shares of judged time:
  `reactive-moderate` is at tier 2 or higher at least 60% of the time, and
  `mash4` and `mash8` are at tier 0 at least 80% of the time. The experiment
  rated about 92% of an honest player's presses Perfect on these tracks, and
  mashers gained little.
- **Mashing never beats playing** (`hv-s4pt.13`): on every shipped track, with
  damage off, no masher (`mash2`, `mash4`, `mash8`) and not the holder has a
  higher average groove (the grade's) than `reactive-moderate`. None spends
  more of judged time at tier 2 or higher either. Mashing doing worse than
  playing is the second priority: measured, not gated.

### Targets

Stage 1:

- Committed-beat accuracy on every track, including the Galop's pulsed sections
  by section (the experiment's 84% overall at a 0.25 s lead is the reference).

Stage 2, with damage off, as shares of judged time:

- On `dnb-174` and `pop-arrangement`: `reactive-moderate` at tier 2 or higher at
  least 60%; `reactive-human` at least 50% (play mode's rule for `human`);
  `reactive-offbeat` at least 50%; `reactive-half` at least 40%; `mash4` and
  `mash8` at tier 0 at least 80%.
- On the Galop: `reactive-moderate` at tier 2 or higher at least 40% (play mode
  requires 70% of its moderate player), and `mash4` and `mash8` at tier 0 at
  least 80%.
- Random presses at 4 and 8 a second keep offline "as tracked" in at least 90%
  of settles.
- In `pop-arrangement`'s pads-only breakdown (from the reference's sections),
  at least 80% of `reactive-moderate`'s presses settle unjudged, and the settled
  groove at the breakdown's end is at least its value at the start minus 0.1.

Stage 3:

- A capture of the Galop's prelude and of `house-128`, attached to the bead's
  notes, showing the press ripple, the pulse source and free movement.

Stage 4, on the Galop:

- Detected section starts within 8 s after at least 4 of the 8 song-map section
  starts from the vamp to the coda.
- At most 3 detected section starts in the galop more than 8 s from any
  song-map section start, and at most 1 in the prelude.

Stage 5, on the Galop:

- At least 5 accents forecast with `confidence` at least 0.6, at least 70% of
  them within 50 ms of a strong onset.
- Heavy swings within 50 ms of song-map tutti chords: at least 3.

Stage 6, on the Galop:

- At least one stampede, one ring, one volley group and the boss occur.
- The finale starts within 6 s after the song map's last tutti chord, and no
  candidate ending occurs earlier in the galop.

Stage 7:

- On the Galop over two seeds, the honest reactive players survive, and
  `holder` and the mashers fall between 1:30 and 2:10.
- `reactive-moderate` earns an A and the mashers a C.

`npm run check:analysis` gains each stage's stream tests: causality, the synth
command, the commit rule, hindsight, structure and accent forecasts, and the
file format.

## Beyond This Plan

Named here so this plan does not block them. Each needs its own design.

- **Live analysis:** a local Python process first, then a TypeScript or
  WebAssembly port of madmom's online model and the forward algorithm. The
  schedule becomes append-only, committed beats arrive as the music plays, and
  the 0.2 s runner lead is accounted for.
- **Browser capture:** a tab's audio through screen sharing in Chrome.
  Whole-system capture varies by operating system and browser version.
- **Native shell:** Tauri or Electron around the same web build, with a small
  native audio module: a Core Audio process tap on macOS, or WASAPI loopback on
  Windows. It only listens: it captures other applications' audio, excluding
  the game's own sounds, and never mutes, delays or replays that audio (see
  [Decisions](#decisions)).
- **Generalising beyond the Galop:**
  [reactive-feel.md](reactive-feel.md) runs alongside stage 7. It adds a corpus
  of new songs with a held-out set, a rater-driven improvement loop, richer
  music signals, a signal bus and modulation matrix, per-song identity, and
  pacing that does not depend on the song's length. That pacing supersedes
  stage 6's absolute-second constants.
- **Press fusion:** feeding the player's presses into the beat forecast, so
  strikes follow a player who keeps a steady beat through a breakdown.

## Alternatives Considered

- **Analyse the whole file first.** That is what play mode does. It cannot
  extend to audio from another application, which is the point.
- **A shadow mode inside play mode,** showing what reactive judging would give
  beside the authored judging. Cheaper, but the user wants a separate mode that
  grows towards the whole experience.
- **Judge against committed beats only.** Simpler, but the hindsight model
  matched 97% of beats against the causal model's 92% on beat-driven tracks.
  The settle delay costs only a groove bar that runs 2 s behind.
- **Judge with a short re-decode of the causal model.** Measured worse than the
  forward belief: 6 s windows flip tempo level.
- **Accept the beat or the off-beat everywhere.** It rescued honest players, but
  a masher at 4 presses a second then landed within the Good window 98% of the
  time.
- **Build a streaming schedule now.** Needed for live analysis, but it would
  rewrite the schedule before the design questions are answered. The
  perturbation check gives this plan the same guarantee.
- **Delay the music for lookahead.** A native module could mute the source
  application and replay its audio a few hundred milliseconds late. Rejected by
  the user as too risky: it changes what the player hears and how other
  applications behave.
- **A separate game.** Reactive mode reuses nearly everything, so a new mode is
  far cheaper.

## Manual QA

Automated checks cover causality and the numbers. These are for the user to
try after each stage; they never block a bead.

1. **Stage 1, the Galop:** play the reactive Galop, then the authored one.
   Strikes and their sounds should follow the galop's beat. The prelude should
   play without percussive gameplay sounds. Note where the beat goes wrong:
   the vamp, theme A repeat, the coda.
2. **Stage 2, the late groove:** play `house-128` with honest presses, then go
   sloppy for 4 s. The groove bar should react about 2 s later, and that should
   read as fair rather than broken. In the Galop's vamp, keep pressing on the
   beat: the ratings should settle to Perfect even where the strikes run at
   half time.
3. **Stage 2, off-beat:** play `house-128` pressing on the off-beats. After a few
   seconds the ratings should settle to Perfect.
4. **Stage 3, the breakdown:** in `pop-arrangement`'s breakdown and the Galop's
   prelude, the pulse should follow your presses, and the sounds should fade.
5. **Stage 3, accent flashes:** the flashes on strong hits should feel on time.
6. **Stage 4, scenes:** zone waves in the reactive Galop should feel motivated
   by the music, such as at the galop's opening chords.
7. **Stage 5, accents:** heavy swings and cleaves on the Galop's tutti chords.
8. **Stage 6, set pieces:** the boss's arrival and the finale should feel like
   the music's climax and ending.
9. **Your own music,** at any stage: add two or three tracks with
   `npm run reactive:add`, play them, and note where the beat or the scenes go
   wrong.

## Implementation Plan

Tracked by epic `hv-s4pt`. Each bead delivers through its own Tollgate worktree,
keeps `scripts/ci` green, keeps reactive mode playable on the Galop from bead
`.3` onwards, adds its parity rows, and records its targets' values in its notes
and in the appendix. Manual QA items are for the user to try; they never block
a bead.

| Bead | Stage | Work | Needs |
|---|---|---|---|
| `hv-s4pt.1` | 1 | Belief stream (beats, ticks, onsets), synth command, every track, `reactive:add` | — |
| `hv-s4pt.2` | 1 | Reactive song map, level and director; instant judging; the gate; structural rewrites; virtual players; `check:reactive` with causality, determinism, stream quality and sounds | .1 |
| `hv-s4pt.3` | 1 | The mode in the app; parity report (stage 1 rows); the e2e test; README | .2 |
| `hv-s4pt.4` | 2 | Hindsight entries (offline and online beats) in the stream; settled judging, beat identity, readings with the significance test, settle and drain events; HUD and play log; stage 2 gates and targets | .3 |
| `hv-s4pt.5` | 3 | Flourish: faded pulses, enemy beat motion, accent flashes, loudness effects, press-reactive world, sound fades, free movement when unclear | .4 |
| `hv-s4pt.6` | 4 | Structure in the stream: changes, jumps, levels, key, ends; their causality tests | .4 |
| `hv-s4pt.7` | 4 | Structure in the game: detected sections, zone waves, rosters, level effects, level-ups, keyed chimes; stage 4 checks and parity rows | .5, .6 |
| `hv-s4pt.8` | 5 | Accent forecasts in the stream; their causality tests | .6 |
| `hv-s4pt.9` | 5 | Heavy swings and cleaves; stage 5 checks and parity rows | .7, .8 |
| `hv-s4pt.10` | 6 | Open-ended set pieces, the boss's exit, the finale and its epilogue; stage 6 targets and parity rows | .9 |
| `hv-s4pt.11` | 7 | Difficulty, healing, grade, reactive demo, the parity pass | .10 |

- **Serialization:**
  - The game-side beads (.2, .3, .4, .5, .7, .9, .10, .11) run one after another,
    because they share the simulation, the live recording and the HUD.
  - Every bead that changes the stream command (.1, .4, .6, .8) runs in that
    order, since each extends the same code and regenerates every shipped
    stream: `.6` waits for `.4`, and `.8` for `.6`.
- **Review:** the repository's rules apply. A bead over 300 lines of
  non-test, non-documentation code, or one touching simulation determinism, the
  golden hashes or the planner checks, gets one cold review.

## Appendix: Evidence

From the experiments in this design's conversation. The scripts and full report
are in `/Users/dthurn/playlist_survivors/tools/analysis/work/causal-beats/`.

**Committed beats at a 0.25 s lead.** Each row gives % within 20 ms / % within
45 ms / % on the off-beat / % elsewhere:

```text
house-128            98 / 98 /  0 /  2
dnb-174              97 / 99 /  0 /  1
hiphop-92-swing      45 / 53 / 46 /  1   (locked at double tempo)
accelerando          75 / 80 / 20 /  0
pop-arrangement      80 / 82 / 11 /  7   (most misses in the pads-only breakdown)
orpheus (pulsed)     53 / 84 /  9 /  8
```

By Galop section, % within 45 ms of a song-map beat:

```text
vamp 67 (half tempo), opening 98, theme A 84, theme A repeat 53 (off-beat),
kick line 88, bridge 100, climax 95, coda 66 (speeding up)
```

**The evidence gate at 0.2.** On orchestral tracks, beats elsewhere fell from
12–36% to 4–9%. The Galop's prelude was 95% muted. The pads-only breakdown
played 9% of its beats (all of them on the beat). Steady beat-driven tracks
played every beat.

**Hindsight.** % of reference beats matched within 45 ms. Grids were decoded
every 2 s, so each row covers a 2 s range of hindsight:

```text
hindsight        beat-driven, online   beat-driven, offline   orpheus, online   orpheus, offline
0.15–2.15 s            92                    97                   76                59
2–4 s                  92                    97                   75                57
10–12 s                91                    97                   67                56
whole song              -                    98                    -                48 (all misses: half tempo)
```

The two models fail differently, which is why the readings choose from both.

**Readings chosen from presses, with the chance guard.** % Perfect against the
whole-song decode, readings chosen per 8 s block. This differs from the design
in four ways: the whole-song decode rather than 20 s windows, fixed blocks that
included the press being rated, a plain 0.05 margin rather than the
significance test, and Perfect share rather than tier. So it supports the
approach, not the stage 2 thresholds.

```text
player                      beat-driven: today / chosen    Galop: today / chosen
human, 25 ms deviation              94 / 92                       95 / 88
human, every other beat             93 / 91                       91 / 87
off-beat presser                     0 / 91                        0 / 0
masher, 4 a second                  18 / 25  (37 unguarded)       28 / 20
masher, 8 a second                  18 / 22                       30 / 17
```

**Structure on the Galop.** The 8 section starts from the vamp to the coda:
vamp 43.2 s, opening 54.1, theme A 69.8, theme A repeat 80.2, kick line 90.5,
bridge 100.7, climax 104.5, coda 124.6.

```text
detector                          found (within 2 s)                      false   known after
novelty, 2 s ahead, ±4 s peak     opening, kick line, climax, coda           0      ~6 s
novelty, 4 s ahead, ±4 s peak     vamp, opening, kick line, bridge, coda     2      ~8 s
loudness jump of 6 dB             vamp, opening, kick line, bridge           8*     ~1 s
```

Novelty used the running threshold's `k` = 0.5. `*` Six of the jump detector's
eight false alarms fall in the free prelude (at −0.4, 4.8, 24.6, 29.0, 33.6 and
40.8 s), and the rest are real dynamic changes within sections. The vamp's jump
was 1.7 s off and known 2.7 s after the song map's start; the kick line was
found by a falling jump. Neither detector finds theme A's repeat, which is the
same music again. Results: `results_structure_sweep.txt` (the sweep) and
`results_ending.txt` beside the scripts.

**Accents on the Galop.** 30 curated accents, 8 of them tutti chords.

```text
strong onset rule                    curated found   tutti found   strong per minute
98th percentile of the past 30 s        11 / 30         4 / 8            33
95th percentile                         14 / 30         4 / 8            70
99th percentile                          7 / 30         2 / 8            23
```

**Limits of the evidence:**

- The beat-driven tracks were synthetic: clean drum machine, no vocals.
- Only one beat tracker family was tried.
- Activation frames are 10 ms.
- The 1.35 s commit horizon was not measured.
- Structure and accent detectors were tuned on the Galop alone.
- Accent forecasting from repetition was not tested.
- The ending rule was checked on the Galop's loudness alone (`ending.py`).
- The readings' significance test, the 20 s hindsight windows and the
  open-ended set pieces were not run.

The checks above, not this appendix, decide whether each stage meets its goals.

### Measured: stage 1

Recorded by `hv-s4pt.1` from the shipped streams (`toolVersion` 1), with the
commit horizon at the fallback's 1 period plus 0.35 s. (`toolVersion` 2, which
commits each next beat by the beat before it, moves these by at most 0.2
points: the Galop to 58.1% within 45 ms.) Each row gives, after
the first 4 s and within the reference's span, % of committed beats within
20 ms / within 45 ms / on the off-beat / elsewhere, then the share of all
committed beats below the gate. The four classical tracks other than the Galop
are scored against the offline decode.

```text
house-128            100.0 / 100.0 /  0.0 /  0.0    below gate  5%
dnb-174               99.4 / 100.0 /  0.0 /  0.0    below gate  3%
hiphop-92-swing       42.9 /  50.6 / 49.4 /  0.0    below gate 28%   (double tempo)
accelerando-100-140   76.3 /  79.8 / 20.2 /  0.0    below gate  4%
pop-arrangement       81.2 /  84.4 /  8.4 /  7.2    below gate 12%
orpheus-galop         29.1 /  57.9 /  7.5 / 34.6    below gate 30%   pulsed sections: 66.9% within 45 ms
william-tell-finale   43.2 /  77.8 /  6.4 / 15.8    below gate 29%
mountain-king         34.4 /  61.8 / 16.3 / 21.9    below gate 21%
bumblebee             49.2 /  76.0 /  2.2 / 21.8    below gate 34%
beethoven-fifth       25.9 /  47.5 /  5.0 / 47.5    below gate 69%
causality-mix         84.2 /  86.8 /  4.0 /  9.2    below gate 31%
```

The Galop by section, % within 45 ms (and % below the gate):

```text
prelude 8 (95 below the gate), vamp 69 (31), opening 93 (2), theme A 69 (19),
theme A repeat 24 (39; 24 on the off-beat), kick line 58 (21), bridge 100,
climax 81, coda 54 (29)
```

- **Gates this data bears on** (`hv-s4pt.2` computes its own in the check):
  stream quality on `house-128` and `dnb-174` 100%, against 95%; the Galop's
  prelude 95% below the gate, against 80%.
- **The horizon:** at 2 periods plus 0.35 s the Galop's pulsed sections were
  54% within 45 ms; at 1 period plus 0.35 s, 67%; at the experiment's 0.25 s
  lead, 83% (the experiment measured 84%). The loss comes from rubato and
  tempo changes over the longer extrapolation, not from timing noise: the
  bridge stays at 100% and the opening at 93%.
- **Forecast period:** the tracker's states have whole-frame beat intervals,
  and it alternates between the two around the true one (34 and 35 frames at
  174 beats per minute). Forecasting a period ahead from either is off by up
  to a frame per period, and commits fell more often on the longer one: at
  2 periods with no bias, `dnb-174`'s committed beats ran a median 32 ms late
  and 44% fell within 20 ms. The forecast uses the period averaged over the
  past 2 s (only frames within 10% of the current period, so a tempo change
  takes effect at once); with it and `biasS`, 99% fall within 20 ms.
- **`biasS`:** −13 ms, the committed beats' median lateness on the steady
  synthetic tracks without it (house-128 15 ms, dnb-174 11 ms,
  pop-arrangement 10 ms).
- **Energy:** the running maximum never falls below −60 dBFS, so leading
  silence reads 0 rather than full loudness.
- **Strong onsets** per minute: the Galop 24 (the experiment's rule gave about
  33), `house-128` 16, `pop-arrangement` 10. As the rule is worded, the window
  includes the onset itself, so the first onset of a track or after a silence
  is always strong.
- **Evidence** averages the last 4 believed beats heard in full: just after a
  beat, before its ±30 ms window has passed, the beat before the oldest takes
  its place.
- **Resampling:** files not at 44.1 kHz (`mountain-king` is 48 kHz) are
  resampled with a symmetric filter that hears about 2 ms ahead, an accepted
  lookahead well under one 10 ms frame. The stream causality test also cuts
  between frames with no margin.
- **Local tracks** (`reactive:add`) play the samples the analysis decoded,
  written as FLAC, so a compressed file's encoder delay cannot shift them.
  Synthetic tracks keep their exact reference beside their Ogg
  (`reference.json`), rendered with it.

### Measured: stage 1, the simulation and the check

Recorded by `hv-s4pt.2` from `npm run check:reactive` (11 s on the developer's
machine, against the 60 s budget; nothing trimmed):

- **Gates:** stream quality 100% on `house-128` (120 beats) and `dnb-174`
  (163); the Galop's prelude 95% below the gate (60 beats); causality on
  `causality-mix` 228 cuts passing for `reactive-moderate` and `mash4`, with
  level-ups and zone waves fired before a cut; the negative control caught at
  its first cut; determinism; fresh streams; sounds on committed beats (253 on
  `causality-mix`, 898 on the Galop); every shipped track end to end.
- **`--full`:** passes on all ten shipped tracks (12 to 34 cuts each, 97 s in
  all).
- **The horizon fallback's Galop figure:** 66.9% of the pulsed sections'
  committed beats within 45 ms (see the stage 1 measurements above).

How the check meets the design, where it had to choose:

- **Cuts** fall just before each committed beat's and strong onset's
  `knownAt` (1 ms), and just after each level-up and zone wave. A cut just
  before an entry's `knownAt` perturbs that entry, so it catches a read of the
  next beat to be committed made any time since the previous cut, which a cut
  just after the previous `knownAt` would miss. Frames are compared from the
  previous cut on.
- **Frames** are taken as live play draws them: the evaluator runs on the
  recording as it stands with the simulation 0.2 s ahead (the runner's
  speculation), so the changed run, stopped at `C`, has frames up to
  `C − 0.2 s`. A finished recording read afterwards would show records
  completed later, such as a gem that grew.
- **Sounds** are compared up to `C − 0.25 s`: the live recording closes a
  sixteenth-note slot half a slot plus 60 ms after its centre.
- **Records completed later** (an orb taken, a chicken eaten, a gem's value)
  are compared as they stood at `C`. The frame's section record is compared
  without its bounds, which no frame reads (the reactive section ends at the
  file's length).
- **The file's length** reaches the progress bar as its own input
  (`knownLength`), as the finale's window will, so perturbing the song's length
  changes nothing before `C`.

What the check found and the simulation now does:

- the HUD's beat lane shows a committed beat only from its `knownAt`
  (`beatsKnownAt` in the plan header);
- a zone wave's end is extrapolated from the last two committed beats;
- an automatic swing starts no earlier than its beat's `knownAt`.

The fallback's rewrites, made although the check passed without them: presses
are judged against the committed beats known at the press (one before any is
committed is unjudged), and the groove drain's bar length comes from the last
two committed beats.

The simulation, the emitter and the evaluator read the length of the beat they
are in, the next committed beat, wherever they interpolate beat positions
(beat phase, strides, sixteenth slots, the heading's span). The horizon keeps
that beat known almost always, but not always: after a sudden belief change,
the Galop's beat at 17.496 s had its successor committed only at 17.65 s, and
William Tell had two such gaps. The cold review of `hv-s4pt.2` caught this
with cuts just before those commits; `causality-mix` has no such gap and
`--full`'s 10 s cuts missed them. So the stream commits each next beat at the
latest when the beat before it arrives (`toolVersion` 2; accuracy unchanged:
the Galop's pulsed sections stay at 66.9%), and `check:reactive` gates that on
every shipped stream. The other reads the fallback names schedule an event at
a later beat, which reads nothing early: the off-beat midpoint (judging
considers only the off-beat after the beat at or before the press), the 2-beat
bar's second beat (downbeats only schedule events), and the boss and volleys
(stage 6).

### Measured: stage 1, the mode in the app

Recorded by `hv-s4pt.3`. `npm run reactive:parity -- --track orpheus-galop`
(seed 1858, `reactive-moderate`'s presses on the song map's beats in both runs,
damage off; the authored run is play mode's level):

```text
Row                                                             Authored   Reactive      Gap
Strikes within 20 ms of a reference beat (pulsed sections)        100.0%      34.1%   -65.9%
Gameplay sounds within 20 ms of their reference slot (pulsed)     100.0%      54.2%   -45.8%
Presses rated as the authored grid would (301 presses)                 —      44.5%
Time at tier 2 or higher (pulsed sections)                         96.5%      62.1%   -34.4%
Average groove and grade                                        0.96 (S)   0.69 (B)    -0.27
Kills                                                               7260       3421    -3839
Kills, enemies alive by section (authored / reactive):
  prelude 76, 14 / 172, 18        galop-vamp 144, 32 / 136, 45
  galop-opening 410, 57 / 323, 59 theme-a 432, 93 / 278, 76
  theme-a-repeat 778, 138 / 337, 86   kick-line 721, 173 / 292, 80
  bridge 268, 243 / 149, 99       climax 2225, 303 / 816, 114
  coda 2206, 250 / 906, 130
```

- **"Presses rated the same"** is measured on the reactive run's presses in the
  pulsed sections: its own class and rating (unjudged, an off-beat, Perfect ...)
  against the class and rating the song map's beats would give the same
  presses (`classifyPress` with its own memory and drift; an unjudged press
  always differs). The two runs' presses differ, so they are not paired.
  Enemies alive are counted in world time; the "grid" sounds (tier chimes, orbs)
  are measured on whole beats, the timing grid both levels use.
- **Difficulty for playability:** with the stage 1 director's starting values the
  hero fell in the Galop's prelude at 30 s, even with `reactive-moderate`: through
  long unclear stretches it is at tier 0 and, striding only on committed beats,
  cannot outpace the horde. So that every shipped track plays to the end,
  `levels/reactive.yaml` gives the hero 600 health and grows the horde from
  nothing (`start` 0, `perMinute` 80, `max` 360). `reactive-moderate` and
  `reactive-human` then survive every shipped track (the closest, Beethoven's
  Fifth, at 51% and 8%); stage 7 tunes the real curve. `check:reactive` gates
  `reactive-moderate`'s survival on every shipped track, and both players' runs
  passing play mode's plan checks that apply to a reactive plan (the `?player=`
  path's own check).
- **The smoke test** hands over at 50 s, where the Galop's beat is clear: the
  prelude's presses are unjudged by design, so 10 s from the start would make no
  judged presses.

### Measured: stage 2

Recorded by `hv-s4pt.4` (`toolVersion` 3, hindsight entries every second).

- **The hindsight grids** on the synthetic tracks, % of reference beats within
  45 ms of the entry's beats, over each entry's last 8 s: `house-128` offline
  100 / online 98.8; `dnb-174` 89.6 / 100; `pop-arrangement` 90.5 / 89.9. The
  offline decode needs no calibration (median error 0 to 1.4 ms):
  `hindsight.biasS` is 0. The stream takes about 0.8 s of analysis per second of
  audio.
- **Gates:** no groove change less than 2 s after its press (house-128's and the
  Galop's `reactive-moderate`, house-128's `mash8`); the readings' unit tests
  (half-tempo grid chooses double, double-tempo grid chooses the half holding the
  beats, shifted grid chooses off-beat, each with 85% or more of the presses
  settling Perfect or Great); on `house-128`, damage off, over judged time,
  `reactive-moderate` at tier 2 or higher 100% of the time, `mash4` and `mash8`
  at tier 0 100%. Causality on `causality-mix`: 318 cuts (cuts also before each
  hindsight entry), with reading switches among the features fired.
- **Targets** (damage off, judged time), all met but one:

```text
dnb-174          reactive-moderate 2+ 100%, reactive-human 2+ 100%, reactive-offbeat 2+ 100%,
                 reactive-half 2+ 100%, mash4 0 100%, mash8 0 100%
pop-arrangement  reactive-moderate 2+ 100%, reactive-human 2+ 94%, reactive-offbeat 2+ 94%,
                 reactive-half 2+ 82%, mash4 0 97%, mash8 0 100%
orpheus-galop    reactive-moderate 2+ 60% (target 40%), mash4 0 100%, mash8 0 100%
pop's breakdown  89% of reactive-moderate's presses settle unjudged; the settled groove
                 1.00 at its start, 1.00 at its end
random presses   keep offline "as tracked" in 66% of settles (mash4), 66% (mash8): missed
```

  The missed target measures the label, but the incumbent is found by its beats,
  not its label (as the design asks): when an offline re-decode moves its beats,
  the same beats may hold under the online list's reading, which no press chose.
  Random presses switched the reading in 2 of 1610 settles (`mash4`) and 2 of
  3180 (`mash8`).
- **Knobs tuned:**
  - The hit window is 70 ms, not 45 ms. At 120 beats a minute only about 14
    presses fall in the 8 s window, and `reactive-moderate`'s 30 ms spread lands
    only about 77% of them within 45 ms of the true grid, so against a
    double-tempo grid the half reading's lead (0.18) never cleared the
    significance test (about 0.3). With 70 ms the hit shares are about 0.98 and
    it clears easily; the significance factor stays 3.
  - The incumbent is the candidate agreeing best with the previous grid, its
    beats within the tolerance less those not: a grid holding the previous beats
    and as many more no longer ties with it. Of candidates agreeing about as well
    (within 10%), the previous source and reading is kept.
  - Densities for the chance term are per second of the entry's window, which is
    shorter than 8 s in the first seconds: without that, random presses switched
    `dnb-174` to double at 4 s.
- **Parity rows** (stage 2): presses rated as the authored grid would 61.4%
  (44.5% in stage 1); time at tier 2 or higher 79.3% (62.1%); average groove
  0.81, grade B (0.69, B).

### Measured: stage 3

Recorded by `hv-s4pt.5`.

- **The perturbation check with frames** passes on `causality-mix` (318 cuts):
  the frames compared include the pulse state, the accent flashes and camera
  punches, the press ripples and the agreement glow.
- **Captures** of the Galop's prelude (22.97 s, 23.75 s, 25.5 s) and of
  `house-128` (31.5 s, 31.62 s) with `reactive-moderate` show free walking and a
  grey ripple from an unjudged press in the prelude, and a rated ripple at tier 3
  on `house-128`. They are in the main checkout's `captures/reactive-stage3/`.
- **Free movement** on `reactive-moderate`'s runs: the Galop walks freely from
  3.8 s to 45.4 s (the prelude), and briefly in theme A's repeat, the kick line
  and the coda; `pop-arrangement` from 84.4 s to 100 s (the pads-only breakdown);
  `house-128` never.
- **Choices:** the crossfade between the press pulse and the beat pulse is the
  share of the last 0.5 s with the beat clear; the camera punch on an accent is
  drawn by the evaluator (the simulation's camera, which sets where enemies
  spawn, does not punch); effect intensity is 0.4 plus 0.6 times the loudness;
  ambient light scales by 0.8 plus 0.4 times it, and ambient particles by 0.4 plus
  0.9 times it; a press in free movement while the beat is unclear shows unjudged.
- **Free movement and the settled groove:** every press in free movement still
  settles 2 s later, as unjudged (changing nothing), so the hindsight readings
  keep seeing the player's presses through it; without that, the reading on
  resuming strides came from a nearly empty window and the stage 2 numbers fell
  (the Galop's `reactive-moderate` to 33.8% at tier 2 or higher). With it they
  hold: the Galop's `reactive-moderate` 70.3% at tier 2 or higher (target 40%),
  `pop-arrangement`'s `reactive-human` 94.1% (50%) and `reactive-moderate` 96.0%
  (60%), and 85.4% of the breakdown's 41 presses settle unjudged (80%).
  Free movement holds the drain's clock: back on strides, the drain waits its
  grace bar again.


### Measured: stage 4, structure in the stream

Recorded by `hv-s4pt.6` (stream `toolVersion` 4).

- **Implementation choices:**
  - Feature frames end at their tick: each holds the 4096 samples before it.
  - Chroma uses a fixed tuning, and the MFCCs' dB scale has no top-dB clip,
    since both would otherwise read the whole file.
  - A change's `t` is the middle of the boundary tick's frame.
  - Jumps need the full 7 s of history.
  - Levels start at medium.
  - Keys use the Krumhansl-Kessler profiles.
  - Every parameter is a stream parameter, and `ps-analysis stream --params`
    overrides them. `causality-mix` is generated with 20 s statistics windows
    for the change spread, the change threshold and the end window.
- **Changes on the Galop:** 9, at 10.4, 18.5, 29.8, 37.7, 43.5, 54.1, 90.8,
  101.4 and 134.6 s.
  - In the galop they fall on the vamp, the opening, the kick line and the
    bridge, with one false alarm at 134.6 s (10 s after the coda's start).
  - The experiment, with the same rule, found 4 with no false alarm: the
    opening, the kick line, the climax and the coda. The difference comes from
    the causal frames, the fixed tuning and the unclipped MFCCs.
- **Jumps on the Galop:** 11.
  - The vamp's is at 45.0 s, 1.8 s after the song map's start.
  - The opening's is at 53.5 s, the kick line's at 90.2 s and the bridge's at
    100.7 s.
  - Five fall in the prelude (6.0, 24.7, 29.1, 33.5 and 40.9 s), all known
    below the gate.
  - The vamp's and the kick line's jumps are known below the gate too (0.15
    and 0.05), so they start no section.
- **Candidate endings on the Galop:** two.
  - The real ending has `t` 142.8 s and is known at 144.3 s; the experiment
    found it at 144.2 s.
  - The real ending is known with the evidence at 0.03, below the gate. Stage
    6's finale must not gate candidate endings the way section starts are
    gated.
  - A second one, at 29.9 s in the free prelude, is below the gate. The
    experiment's centred frames did not find it.
- **Keys on the Galop:**
  - The prelude reads E major, as the song map says.
  - The galop reads A major from 46 s (the vamp) and D major from 61 s, with
    brief A major spells.
  - Theme A's G major is never reached: the 20 s window lags its 21 s.
- **Levels on the Galop:** 66% of ticks loud, 30% medium, 4% quiet.
- **Detected section starts** on the Galop (the stage 4 rule over the stream:
  gate, 8 s apart, effective downbeat): 36.6, 50.7, 58.9, 97.5, 107.8 and
  141.0 s.
  - They come from the changes at 29.8, 43.5, 90.8, 101.4 and 134.6 s and the
    jump at 57.5 s.
  - The prelude's start comes from the change at 29.8 s, known at 35.8 s with
    the evidence just above the gate (0.22).
  - The opening's jump at 53.5 s falls within 8 s of the vamp's start, so it
    starts nothing.
  - Within 8 s after 4 of the 8 song-map section starts from the vamp to the
    coda (target at least 4), exactly at the target.
    - Matching is one to one: the start at 107.8 s falls within 8 s after both
      the bridge (100.7 s) and the climax (104.5 s), but counts once.
  - 1 in the galop more than 8 s from every section start, before or after
    it (target at most 3).
  - 1 in the prelude (target at most 1).
- **Parity rows** (`npm run reactive:parity`), with stage 1's timer waves still
  in place:
  - Zone waves: authored 4 (4 on a section start), reactive 3 (0).
  - Detected section starts within 8 s after a section start: 4 of 8.
  - In free sections: 1.
  - In pulsed sections more than 8 s from every start: 1.
  - False zone waves: authored 0, reactive 3.
- **Every track's structure** (changes, jumps, candidate endings, detected
  starts):

  | Track | Changes | Jumps | Candidate endings | Detected starts |
  |---|---|---|---|---|
  | William Tell | 11 | 12 | none | 8 |
  | Mountain King | 11 | 9 | 150.2 s | 9 |
  | Bumblebee | 7 | 3 | none | 4 |
  | Beethoven | 10 | 18 | 23.6, 26.9 and 166.0 s | 5 |
  | house-128 | 5 | 2 | 60.5 s | 5 |
  | dnb-174 | 6 | 2 | none | 5 |
  | hiphop-92-swing | 9 | 2 | 60.7 s | 6 |
  | accelerando-100-140 | 8 | 2 | 73.5 s | 7 |
  | pop-arrangement | 9 | 5 | 168.7 s | 8 |
  | causality-mix | 2 | 7 | 16.5 and 42.5 s | 3 |

- **Causality:** the Python stream test covers every structure field on
  `causality-mix`, where each fires.
  - It cuts exactly at every structure entry's `knownAt` with no margin, plus
    8 fixed cuts.
  - A prefix check requires the feature frames of cut audio to equal the full
    audio's.
  - Injected leaks are caught: centred frames, a change stamped a tick early,
    and a jump stamped a tick early.
  - The perturbation check changes structure entries, levels and keys, and
    cuts before each structural event: 338 cuts on `causality-mix`.
  - `check:reactive --full` passes with no failing cut on any shipped track.
    The simulation does not read structure until the next bead, so these cuts
    prove nothing about structure yet.

### Measured: stage 4, scenes

Recorded by `hv-s4pt.7`.

- **The director's stage 4 block** replaces stage 1's `roster` (by time),
  `levelUps` (by time), `energyScale` and `zoneEvery`:
  - `sections`: six rosters on `levels/reactive.yaml`, one per detected
    section, holding the last.
  - `sectionRule`: jumps of 6 dB, starts at least 8 s apart.
  - `levelUps`: by experience only, with `levelUpWaitS` 20.
  - `zoneFallbackS` 60.
  - `smoothS` 2.
  - `population.levelScale`: quiet 0.6, medium 1.0, loud 1.3.
  - `camera`: quiet zoom 1.3 and lead 30, medium 1.2 and 50, loud 1.05 and
    60.
  - `palettes`: soft-chimes, crisp-steel and heavy-infernal, with
    `paletteHoldS` 2.
  - `calm`: quiet 0.5.
  - The check level compresses them: starts 4 s apart, level-ups 4 s after
    their experience, and a fallback wave after 12 s.
- **Choices:**
  - The population scale, the camera and the calm are each the mean of their
    level values over the last 2 s of ticks. That changes them smoothly and
    reads only the past.
  - The sound palette starts at medium.
  - A detected section's start shows a 1.6 s banner with the new zone's name
    and a camera punch of 0.035.
  - The negative control now scales the population by the evidence a second
    ahead.
  - Reactive level-ups have no fixed moment, so the plan's `levelUpsDue` is
    empty and the plan check on scheduled level-ups does not apply to them.
  - A full bar stays ready until it is granted. A level-up queued by the wait
    drops itself if a section start granted that level first, so each
    level-up happens once.
  - A fallback zone wave is skipped when a section start is already known by
    its beat.
  - The sound palette and the chime key are read at the time a sound is
    caused.
- **The Galop with `reactive-moderate`** (parity, seed 1858, damage off):
  - Zone waves: 6, 4 of them within 8 s after a song-map section start, and 2
    false: the prelude's start at 36.6 s and the coda's late start at 141.0 s.
  - Authored: 4, all on section starts.
  - Detected section starts within 8 s after 4 of the 8 song-map starts from
    the vamp to the coda. Targets: at least 4; at most 3 in the galop more
    than 8 s from every start (1); at most 1 in the prelude (1).
  - Kills: 3028.
  - Lowest health with damage on: 78%. Survival holds on every shipped
    track.
- **Captures** of the Galop's reactive run are in the main checkout's
  `captures/reactive-stage4/`: 20 s (quiet prelude, zoomed in), 50.8 s and
  51.4 s (the section start's wave and its "Mushroom Grove" banner), and 110 s
  (loud climax, zoomed out).
- **Causality:**
  - The perturbation check on `causality-mix` passes: 337 cuts, every feature
    class fired, including section starts.
  - `check:reactive --full` passes with no failing cut on every shipped
    track: the Galop 28, William Tell 32, Mountain King 30, Bumblebee 14,
    Beethoven 34, the synthetic tracks 12 to 32.

### Measured: stage 5, accent forecasts in the stream

Recorded by `hv-s4pt.8` (stream `toolVersion` 5).

- **Implementation:**
  - A committed beat is hit when a strong onset lies within 50 ms of it, as
    of the first such onset's `knownAt`.
  - Three hits in a row at one position of a 2-, 4- or 8-beat cycle forecast
    the next cycle's beat. It is known at the later of that beat's `knownAt`
    and the hits, and only with at least 0.54 s left: the swing's 0.19 s
    wind-up plus 0.35 s.
  - Where several cycles forecast the same beat, the one known first is kept
    (then the most confident, then the shortest cycle).
  - A miss breaks the run, so a failed forecast needs 3 more hits.
  - Every rule constant is a stream parameter. `causality-mix` takes strong
    onsets from the 95th percentile instead of the 98th, because its accents,
    every 4 s, outnumber the top 2% of its onsets. It now has 5 forecasts.
- **Target missed on the Galop:**
  - 2 forecasts at confidence 0.6 or more, against at least 5. They are 62.66
    s (confidence 0.75) and 65.39 s (1.0), both on the 8-beat cycle.
  - 1 of them (50%) lies within 50 ms of a strong onset, against at least 70%.
  - None falls on a song-map tutti chord.
  - The design's example, the opening tutti chords every 8 beats (54.07,
    56.78, 59.53 and 62.30 s), does not hold in committed beats. The chords
    fall on committed beats 75, 80, 88 and 96, because the tracker's beats in
    the vamp and the opening drift and skip. And the chord at 59.53 s is not
    a strong onset (strength 74.5, below the 98th percentile).
  - Heavy swings in stage 5 therefore rest mostly on the unforecast-accent
    path, which cleaves on detected strong onsets.
- **Other tracks:** forecasts at confidence 0.6 or more, and their share
  within 50 ms of a strong onset.

  | Track | Forecasts | Within 50 ms |
  |---|---|---|
  | Mountain King | 15 | 40% |
  | accelerando-100-140 | 3 | 33% |
  | William Tell | 2 | 0% |
  | pop-arrangement | 2 | 0% |
  | causality-mix | 5 | 20% |
  | Bumblebee, Beethoven, house-128, dnb-174, hiphop-92-swing | none | — |

  The low precision comes from committed-beat timing: a forecast sits on its
  committed beat, which is often more than 50 ms from the onset it predicts.
- **Causality:**
  - The Python test proves forecasts read only beats and onsets known by
    their `knownAt`, at cuts at every forecast's `knownAt`. Stamping a
    forecast before its beat is known fails it.
  - The full-stream causality test compares forecasts too.
  - The perturbation check changes forecasts after the cut (times ±2 s,
    cycles and confidences replaced, every third deleted, one inserted between
    pairs) and cuts before each: 361 cuts on `causality-mix`.
  - `check:reactive --full` passes on every shipped track.

### Measured: stage 5, heavy swings and cleaves

Recorded by `hv-s4pt.9`.

- **Implementation:**
  - A reactive swing is decided at its wind-up, not when it is armed. It is
    heavy when its slot is a committed beat in step movement carrying a
    forecast accent with confidence 0.6 or more, known by the wind-up.
  - The swing's accent is the forecast's confidence. Its animation and its
    camera punch (0.035, with play mode's 50 ms lead-in) are chosen there too.
  - A strong onset known with the beat clear, in step movement, and with no
    heavy swing within 50 ms of it, becomes a pending cleave at its `knownAt`,
    where the hero stands.
  - A counted press of Great or better within play mode's Great window (90
    ms) of the onset launches it: at the onset's `knownAt` if the press came
    first, or at the press.
  - One press launches at most one cleave, forecast or onset. A forecast
    cleave whose press came first launches at the strike.
  - These cleaves have no heavy impact and no hit-stop: the stage 3 accent
    flash and the bands only.
  - Every reactive cleave's bands start on the first eighth-note slot at
    least 0.06 s after the launch, then sixteenths.
- **The Galop with `reactive-moderate`** (seed 1858, damage off):
  - Heavy swings: 2, 1 of them within 50 ms of a song-map accent. Authored:
    23, 22 on accents.
  - Within 50 ms of a tutti chord: 0, against a target of at least 3
    (missed). This follows from stage 5's forecast miss: the stream forecasts
    only 2 accents on the Galop, neither on a tutti chord.
  - Cleaves launched: 16 in the parity run, against 18 authored (17 in
    `check:reactive`'s batch run). The perturbation check runs 361 cuts. Nearly all come from the unforecast path,
    on strong onsets.
- **Causality:**
  - The perturbation check passes on `causality-mix`, 362 cuts. Every feature
    class fired before a cut, now including forecasts (heavy swings) and
    cleaves.
  - The proof now counts forecast cleaves and onset cleaves separately.
  - Reactive cleave records carry their exact launch time, and the prefix
    comparison compares them as of it.
  - A cleave's bands are all queued at its launch. A band's slot may then
    interpolate a committed beat up to about 1.75 beats ahead that is not yet
    known at the launch. No band falls before that beat is known, by the rule
    that each next beat is committed by the beat before it, so the play is
    causal, as the precomputed automatic swings already are. A live analyzer
    would need the bands scheduled as their beats arrive.
  - `check:reactive --full` passes on every shipped track.
  - The sound check passes on `causality-mix` and the Galop.

### Measured: stage 6, set pieces and the finale

Recorded by `hv-s4pt.10`.

- **Implementation:**
  - `src/reactive/setpieces.ts` derives every set piece from the stream: its
    trigger, its start on the effective downbeat after it, the 6 s spacing,
    and the priority order (the boss, then a ring, a stampede, a formation and
    volleys).
  - The schedule takes them in play mode's terms, and the simulation reuses
    play mode's handlers.
  - A set piece starts only on stream entries known by its trigger. Its
    open-ended parts are read only from when they are known.
- **Choices beyond the design:**
  - Stampedes allow loud ticks in the quiet stretch before the jump, up to 5%
    of them, so a blip does not cancel one.
  - A volley group starts on the level turning loud with the beat clear, and
    re-arms only after 4 s without that. Each volley is decided at its draw.
  - Volleys, the least of the set pieces, wait the 6 s after any start, but
    their own start blocks none of the others: archers keep volleying under a
    later set piece.
  - Turned away by the spacing, volleys try again on the first downbeat the
    spacing allows, while the level is still loud with the beat clear.
  - The boss is dropped if the finale comes before it would drop in. One
    landing as the finale comes leaves with the blast.
  - A ring the finale has ended records no close.
  - From the finale on, every sound is exempt from the gate: the finale's own,
    and the epilogue's past the audio.
  - The kick line counts medium beats whatever the evidence, but needs the
    beat clear on the beat that triggers it. It comes at most once, as the
    boss does.
  - A bee wedge releases after its entrance and release beats, counted in
    committed beats as they arrive.
  - The boss's slams are marked one beat ahead, not two, so the landing's beat
    is committed when its marker appears.
  - The boss retreats by running off the field with its walk.
- **Rules a live run needs, found by the perturbation check:**
  - A ring's members get their velocity from the step behind, since the
    forward look-ahead read the close early.
  - A ring's close ends on the first downbeat known when it starts, or one bar
    of the latest committed period after it. Its pace depends on that end,
    which the effective downbeat could leave unknown.
  - Gems flying on sixteenths, and a vacuum's bar of them, are timed from the
    beats committed by their takeoff. Past the last committed beat they use
    its period. This was an older early read: the extended `causality-mix`
    exposed it.
  - The finale's swell joins the base rate only when the finale comes. Swings
    winding up for a slot near it would otherwise read the swell early.
  - The boss's bar ends at its retreat, the one end known when it lands.
  - The players' view of a closing ring follows its members, from the close's
    start.
  - The kick line's dance start is recorded when its entrance ends, its beat
    known by then.
  - The prefix comparison now also compares enemies' attacks and dances, as
    they stood at the cut.
  - In the prefix comparison, the boss bar's health series and a slam
    marker's predicted landing are compared as they stood at the cut.
- **`causality-mix` extended:** after its first 42 s, a medium stretch (the
  kick line), a faint quiet beat, a rise to medium (a stampede that does not
  fall on a loud section start), and a loud close (the boss). Then the final
  chord rings out at 62.1 s. The track is now 68 s long.
- **The check level** compresses the set pieces:
  - gaps of 2 s;
  - rings close from an accent 1 s in, or after 6 beats;
  - a stampede after 3 s that were not loud;
  - the kick line after 4 medium beats, dancing 4 to 8 beats;
  - the boss after 45 s, retreating after 4 s.
- **The Galop with `reactive-moderate`** (seed 1858, damage off), each set
  piece as it showed, against authored:

  | Set piece | Reactive (s) | Authored (s) | How it is timed |
  |---|---|---|---|
  | Rings | 54.7, 63.0 | 62.3, 116.2, 133.0 | the close |
  | Stampedes | none | 101.9, 124.6 | the start |
  | Volleys | 48.8, 108.7, 114.9 | 92.1 | the first arrow of each group |
  | Bee wedge | 43.9 | 84.8, 88.6 | the release |
  | Kick line | 104.6 | 100.0 | the release |
  | Boss | 107.8 | 104.0 | the entry |
  | Finale | 144.3 | 139.6 | the blast |

  - A third ring starts at 141.0 s, and the finale ends it before it
    closes.
  - Cleaves launched: 15.
- **Targets:**
  - Each set piece occurs: missed for the stampede. The only rising jump that
    qualifies is the bridge's at 100.65 s, known with the beat clear after a
    stretch that was not loud. It falls within 6 s of the kick line's start
    at 96.8 s.
  - The finale lands 4.72 s after the last tutti chord (target within 6 s).
  - No candidate ending comes earlier in the galop (the one at 29.9 s is in
    the prelude).
- **Causality:**
  - The perturbation check on `causality-mix` passes: 578 cuts, 0 failing.
    Every feature class fired before a cut: each set piece and the finale
    (rings, stampedes, volleys, bee wedges, kick lines, the boss, the finale)
    as well as the earlier stages' classes.
  - The finale's window comes from the audio's length, passed apart from the
    stream.
  - `check:reactive --full` passes with no failing cut on every shipped
    track: the Galop 28, William Tell 32, Mountain King 30, Bumblebee 14,
    Beethoven 34, the synthetic tracks 12 to 32.
  - `check:reactive` now takes about 80 s, 61 s of it for the causality check
    on the longer track.

### Measured: fair judging, after two playtests

Recorded by `hv-s4pt.12`.

**What the playtests showed.** The source was two playtests of the reactive
Galop (logs `2026-10-24T03-13-03-551Z` and `2026-10-24T03-19-24-149Z`).

- **Wrong readings.** The settle picked half-tempo readings in the vamp
  (46–54 s) and around the kick line (89–92 s), and an off-beat reading in the
  coda (131–136 s). Good presses settled as stray, loose or good: the coda
  crashed from 100% to 5%, and the vamp stayed at 0.
- **Free movement.** It also switched on confidence dips the player could not
  hear: 81.6–89.3 s, 92.5–94.7 s and 137.4–140.9 s, besides the prelude.
- **The tracker itself.** In the kick line its committed beats ran 115–210 ms
  behind the song map's, at evidence near 0.27, while the player pressed 25–90
  ms after the song map's beats.

**The revision.**

- No free movement.
- Judging resumes only after 1 s clear, with the drain's grace bar after an
  unclear stretch.
- Readings are chosen from judged presses since judging resumed, restarting
  from offline "as tracked".
- The reading choice leans towards "as tracked": significance factors 2 away
  from it, 1 back to it.
- The benefit of the doubt applies to steady players (4 provisionally Perfect or
  Great presses in a row), and only when the settle has a judgment of its own.
- Penalties, the drain and an unsteady player's raises scale with the
  evidence: none of them at the gate, all of them from 0.55.
- Switching away from "as tracked" needs at least 12 presses.

**Three findings along the way:**

- **Unconditional benefit of the doubt lifted the mashers.** On the Galop they
  stayed at tier 0 only 24–45% of judged time. Requiring a steady run restores
  it.
- **Full penalties from 0.65 let mash4 out of tier 0** on the Galop (78%). From
  0.55 every masher stays at or above 82%.
- **A reactive ring's members now close in world time,** frozen in hit-stops.
  Closing in song time moved them across a hit-stop, and the plan check failed
  on Beethoven's Fifth.

**The playtests replayed** (`npm run reactive:replay`), with each log's presses
under the new rules. Each cell is average groove, with the lowest in brackets.

| Run | Vamp 43–55 s, recorded | Vamp, replayed | Kick line 88–96 s, recorded | Kick line, replayed | Coda 130–138 s, recorded | Coda, replayed |
|---|---|---|---|---|---|---|
| First | 0.00 (0.00) | 0.02 (0.00) | 0.81 (0.60) | 0.84 (0.74) | ended before the coda | ended before the coda |
| Second | 0.01 (0.00) | 0.04 (0.00) | 0.81 (0.59) | 0.78 (0.65) | 0.62 (0.03) | 1.00 (0.99) |

- **Presses rated well at first but settled stray, loose or good:** 12 → 3
  and 20 → 7.
- **Average groove:** 0.45 → 0.42 and 0.51 → 0.59.
- **The vamp stays low**, lower still once an unsteady player's raises scale,
  for two reasons:
  - the evidence dips under the gate at 45.4–46.4 s and 48.0–48.8 s;
  - the tracker's beats there are 50–90 ms off, so steady presses rate Great
    (+0.02) rather than Perfect.
- **The recorded kick line hid its presses in free movement.** The replayed one
  judges them, softly.

**The players**, with damage off, as shares of judged time:

| Track | Player | Before | After |
|---|---|---|---|
| Galop | `reactive-moderate` at tier 2 or higher | 70.3% | 86.2% |
| Galop | `reactive-human` at tier 2 or higher | about 66% | 75% |
| Galop | mash4 at tier 0 | 100% | 97% |
| Galop | mash8 at tier 0 | 100% | 100% |
| pop-arrangement | mash4 at tier 0 | 99.7% | 87% |

- The house-128 gate holds at 100% for both mashers.
- **Judged time** in these shares and in the stage 2 gates is now the judged
  time the grade uses, so the second after each resume no longer counts.
- **pop-arrangement's mash4 reaches tier 2 or higher 9% of the time.** At 120
  beats per minute, 4 presses a second is exactly eighth notes, so after the
  breakdown its presses really do fit the double reading.
- **Random presses on offline "as tracked":** the stage 2 target of 90% stays
  missed, at 68% (66% before).
- **The review's ring finding:** a reactive ring's members now re-solve their
  pace each step for what is left of the close. They reach their inner radius
  by its end, after any hit-stop's pause.

### Measured: beatless credit and the lock-on grace

Recorded by `hv-s4pt.13`.

**What the third playtest showed** (log `2026-10-24T04-30-07-695Z`):

- **The prelude.** The meter sat at 0 through the Galop's 16 s prelude.
- **The vamp.** It then took about 20 s of the vamp to fill.
- **Unearned losses.** Quiet stretches mid-song lost groove the player could not
  have earned.

**The playtests replayed** (`npm run reactive:replay`). Each cell is average
groove, with the lowest in brackets. "Before" is the `hv-s4pt.12` rules. The
first run ended before the coda.

| Run | 0–16 s | 16–43 s | Vamp 43–55 s, before | Vamp 43–55 s, now | Kick line 88–96 s | Coda 130–138 s |
|---|---|---|---|---|---|---|
| First | 0.51 (0.00) | 1.00 (0.99) | 0.02 (0.00) | 1.00 (0.98) | 0.93 (0.85) | — |
| Second | 0.51 (0.00) | 1.00 (1.00) | 0.04 (0.00) | 1.00 (0.99) | 0.90 (0.79) | 1.00 (0.99) |
| Third | 0.51 (0.00) | 1.00 (0.99) | 0.01 (0.00), as recorded | 0.94 (0.81) | 0.95 (0.90) | 0.89 (0.66) |

- **Average groove over each run:** 0.90, 0.94 and 0.93. As recorded, it was
  0.45, 0.51 and 0.57.
- **Filling up.** The meter is half full 8 s in and full by 12 s, while the
  title card is still fading.
- **The third run's vamp** still dips to 0.81 at 46–53 s. Judging flickers back
  there with the tracker's beats a quarter beat off. The player's presses (every
  0.33 s, steady to ±20%) settle loose and stray, and they are not yet steady
  in the game's sense, so their penalties count half.

**The players**, with damage off. Each cell is average groove / share of judged
time at tier 2 or higher; the best masher per track is in bold.

| Track | `reactive-moderate` | mash2 | mash4 | mash8 | holder |
|---|---|---|---|---|---|
| Galop | 0.99 / 100% | 0.60 / 42% | 0.49 / 23% | 0.42 / 18% | **0.64 / 57%** |
| William Tell | 0.99 / 100% | 0.52 / 39% | **0.55 / 51%** | 0.32 / 23% | 0.52 / 43% |
| Mountain King | 0.99 / 100% | 0.23 / 11% | **0.27 / 14%** | 0.11 / 2% | 0.20 / 11% |
| Bumblebee | 1.00 / 100% | 0.47 / 27% | **0.78 / 97%** | 0.20 / 12% | 0.58 / 53% |
| Beethoven | 1.00 / 100% | 0.78 / 47% | 0.69 / 35% | 0.65 / 23% | **0.80 / 55%** |
| house-128 | 1.00 / 100% | 0.06 / 0% | **0.07 / 0%** | 0.00 / 0% | 0.01 / 0% |
| dnb-174 | 1.00 / 100% | 0.05 / 0% | **0.06 / 0%** | 0.01 / 0% | 0.02 / 0% |
| hiphop-92-swing | 0.97 / 100% | **0.10 / 0%** | 0.03 / 0% | 0.00 / 0% | 0.00 / 0% |
| accelerando | 0.97 / 100% | **0.09 / 0%** | 0.06 / 0% | 0.00 / 0% | 0.02 / 0% |
| pop-arrangement | 1.00 / 100% | 0.14 / 3% | **0.25 / 12%** | 0.08 / 2% | 0.12 / 4% |

- **The new gate holds on every track:** mashing never beats playing.
- **Mashers do worse on every track,** most narrowly on Bumblebee and
  Beethoven's Fifth. Both are mostly beatless (Beethoven's `reactive-moderate`
  has 270 of 369 presses unjudged), so everyone gets the same credit there.
  Bumblebee's mash4 spends nearly as much judged time at tier 2 or higher (97%)
  because its judged stretches are short, and it reaches them at a full meter.
- **The Galop's stage 2 target misses.** mash4 is at tier 0 76% of judged time
  and mash8 79% (target 80%; 97% and 100% before). The credit carries a masher
  into each judged stretch at a full meter.
- **Unchanged:** the house-128 gate holds at 100%, and pop-arrangement's mash4
  is at tier 0 84% (87% before).
- **End to end with damage on,** `reactive-moderate`'s grade-average groove is
  0.97–1.00 on every track, and its lowest health rises on the Galop from 58%
  to 71%.

**The review's findings, fixed:**

- **The grade first counted all time without judging as 1,** flickers included,
  while the meter showed something else. It now averages the meter as shown.
- **The drain grace trusted a stale steady run,** so a player could stop
  pressing and keep it. It now needs a press within the last bar.
- **Beatless credit** applies only in step sections, as the drain does.

**Four findings along the way:**

- **Credit for every unjudged beat let the holder tie playing.** On
  hiphop-92-swing the holder, which never presses, reached 1.00 / 100%. Two
  causes:
  - a confidence flicker earned credit;
  - the credit counted as a raise, which kept the drain away.
  Credit from 2 s into a stretch, and not counting as a raise, brings it to
  0.00 / 0%.
- **A grace after every short gap** let flickers restart it. Lock-ons after
  4 s or more without judging, with flickers inside the grace ignored, fix
  that.
- **A grace for everyone, penalties and drain alike,** lifted mash4 on
  Bumblebee to 0.92 / 100%, a tie with playing.
- **A grace for steady players only** left the third run's vamp at 0.48. The
  player's presses there were not steady in the game's sense, because the
  tracker's beats were off. Counting an unsteady player's penalties at half in
  the grace is the compromise:

| Unsteady player's penalties in the grace | 0 | 0.25 | 0.5 | 1 (none forgiven) |
|---|---|---|---|---|
| Third run's vamp, lowest | 1.00 | 0.91 | 0.81 | 0.48 |
| Bumblebee mash4, judged time at tier 2+ | 100% | 98% | 96% | 49% |

### Measured: stage 7, the difficulty curve and the grade

Recorded by `hv-s4pt.11`, after beatless credit (`hv-s4pt.13`).

**The Galop with damage on** (`check:reactive -- --full`). Each cell is the
lowest health for a run that lives, or when the hero fell, then the average
groove and grade.

| Player | Seed 1858 | Seed 2026 |
|---|---|---|
| `reactive-moderate` | 71%, 0.992 A | 37%, 0.991 A |
| `reactive-human` | 77%, 0.899 A | 50%, 0.964 A |
| `reactive-skilled` | 64%, 0.993 A | 55%, 0.993 A |
| holder | falls at 2:07, 0.571 C | falls at 2:06, 0.562 C |
| mash2 | falls at 1:55, 0.628 C | falls at 1:57, 0.603 C |
| mash4 | falls at 1:53, 0.505 C | falls at 2:00, 0.489 C |
| mash8 | falls at 1:52, 0.389 C | falls at 2:03, 0.420 C |

- **Both targets are met:**
  - the honest players live, and all 8 masher and holder runs fall between
    1:30 and 2:10;
  - `reactive-moderate` earns an A and the mashers a C.
- **No S.** `reactive-skilled` (0.993) also earns an A. An S takes a
  near-flawless 0.995, and separating 0.991 from 0.993 would be noise.
- **Four more seeds** (7, 42, 99, 314):
  - every honest run lives, at 38–76% lowest health;
  - 15 of the 16 masher and holder runs fall, 13 of them by 2:10 and the
    other 2 by 2:15;
  - mash4 lives on seed 42, at 15%.
- **Every shipped track, seed 2026:** every honest run lives, with
  `reactive-moderate` at 37–100% lowest health.
  - The holder and mash4 fall on 7 of the 10 tracks: Galop, William Tell,
    Mountain King, Beethoven's Fifth, house-128, accelerando and
    pop-arrangement.
  - They live on Bumblebee, dnb-174 and hiphop-92-swing, which are short or
    beatless.
- **Where the danger came from before the tuning:**
  - **The boss's slams.** With 300 health and slams every 4 beats, they took
    `reactive-moderate` from 96% to 31%: 8 of its 16 slams landed. Mashers
    did no worse.
  - **Chickens.** At 3% elites, 7–14 chickens healed mashers 30 each, so
    most of them lived.

**Tunings tried** (seeds 1858 and 2026):

| Health | Horde a minute | Slam every | Elites | Honest lowest | Masher and holder runs that fall (of 8) |
|---|---|---|---|---|---|
| 600 | 80 | 4 beats | 3% | 56–75% | 0 |
| 400 | 120 | 4 beats | 3% | 42–80% | 3 |
| 300 | 120 | 4 beats | 3% | 30–73% | 7, at 2:05–2:21 |
| 200 | 160 | 8 beats | 3% | 35–82% | 2 |
| 200 | 240 | 8 beats | 3% | 50–87% | 3 |
| 200 | 160 | 8 beats | 1% | 27–83% | 8, at 1:54–2:15 |
| **200** | **240** | **8 beats** | **1%** | **37–77%** | **8, at 1:52–2:07** |

The horde's cap (`max`) never bound: the population stays under 440 on the
Galop.
