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
| Free sections (the prelude) | The song map's feel | The gate: unclear stretches are not judged (stage 1) and switch to free movement (stage 3) | 1, 3 |
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
  "ticks": [{ "t": 0.05, "evidence": 0.0, "energy": 0.0, "level": "quiet", "key": "D major" }],
  "onsets": [{ "t": 0.503, "knownAt": 0.51, "strength": 4.2, "strong": false }],
  "hindsight": [{ "knownAt": 1.0, "offline": [], "online": [] }],
  "structure": {
    "changes": [{ "t": 54.07, "knownAt": 60.1, "strength": 1.4 }],
    "jumps": [{ "t": 53.4, "knownAt": 54.4, "db": 6.4 }],
    "ends": [{ "t": 142.8, "knownAt": 144.2 }]
  },
  "accents": { "forecast": [{ "t": 65.03, "knownAt": 63.1, "cycle": 8, "confidence": 0.75 }] },
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
  the average groove over judged time (the settled groove from stage 2).
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

The presses that choose are every press in the window except partner presses
(a diagonal's or a rock's second key), whatever their class.

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
  d = score(c) − score(i) > 0.10   and   d > 3 × sqrt((h_c(1 − h_c) + h_i(1 − h_i)) / n)
where h are the candidates' hit shares and n the number of presses;
with fewer than 8 presses the incumbent stays
```

- **The chance term:** random presses hit any grid at about its chance rate, so
  their scores sit near 0 for every candidate. Without the term, a masher at 4
  presses a second picked the double-tempo grid and doubled its Perfect share
  (see the [appendix](#appendix-evidence)).
- **Leaving out `p`** stops a press from choosing the grid that rates it.
- **The significance test** stops the reading from wandering on noise. For 32
  random presses `3 × sqrt(...)` is about 0.34, far above their typical score
  differences, while an honest player's switch from a half-tempo grid to the
  double reading (d = 0.36, both hit shares near 0.95) clears it easily:
  `3 × sqrt(2 × 0.95 × 0.05 / 32) ≈ 0.16`.
- **The incumbent is found by matching beats** because the labels "half A" and
  "half B" depend on which beat opens the 8 s window, which changes as the
  window slides.
- **Knobs:** the window (8 s), the margin (0.10), the significance factor (3)
  and the 45 ms hit window may be tuned.
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

- **Unjudged presses:** a press whose evidence at `p` is below the gate is
  unjudged. It shows grey, changes nothing and does not count as raising the
  groove.
- **The drain clock stops:** the time since the latest raise counts only time
  whose evidence is at or above the gate. A stretch below the gate neither
  drains the groove nor brings the next drain closer, much as play mode's free
  sections freeze the groove.
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
- **Free movement when unclear:** after the evidence has stayed below the gate
  for 2 s, movement switches to play mode's free movement (held keys walk, the
  sword swings by itself every beat), as in the Galop's authored prelude. It
  switches back to strides after 2 s at or above the gate. Each switch takes
  effect on the next committed downbeat.

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

- `reactive-moderate`, `reactive-human` and `reactive-skilled` (stage 7) use
  play mode's `moderate`, `human` and `skilled` profiles unchanged (timing
  spread, bias, press rate and sloppiness), pressing on the reference beats.
  `reactive-skilled` also presses on each reference accent.
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

