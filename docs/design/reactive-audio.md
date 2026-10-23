# Reactive Mode: Play to Music the Game Has Not Heard Yet

## Introduction

Playlist Survivors knows its song completely before the first frame. A song
map, built offline and corrected by hand, fixes every beat, bar, section,
phrase and accent. A level script then places the horde, the set pieces and
the level-ups on those musical moments.

This document proposes **reactive mode**: the same game played to music it
has not analysed in advance. The long-term goal is to play along to any audio
on the computer, such as a streaming app or a video. In that setting the game
learns about the music only as it plays, and nothing reveals the future.

This document covers **stage 1**, a playable prototype that answers the open
design questions without the plumbing of capturing system audio:

- The music still comes from a file, but the game may only use what a
  **causal** beat tracker would know at each moment.
- An offline step writes that knowledge out as a **belief stream**. Every
  entry is stamped with the time it became known, and checks prove that the
  game never reads an entry early.
- Press judging, the groove, effects and the horde are reworked so they need
  only what the stream reveals.

Terms used throughout:

- **Song time:** seconds into the audio, as everywhere else in this game.
- **Causal:** computed only from audio up to the current moment.
- **Known-at time (`knownAt`):** the song time at which a belief-stream entry
  becomes available to the game. An entry is never visible before it.
- **Committed beat:** a beat time the tracker forecasts, made known well ahead
  of the beat and never revised. Strikes and gameplay sounds are placed on
  committed beats.
- **Hindsight grid:** beat times re-decoded a little later from a longer
  stretch of audio with a stronger model. Presses are judged against these.
- **Evidence:** a 0-to-1 measure of how strongly the audio's onsets line up
  with the beats the tracker believes in. Low evidence means the beat is
  unclear: a breakdown, a free passage, or the tracker lost.
- **The gate:** evidence 0.2. Below it, nothing is judged and percussive
  gameplay sounds are muted.
- **Reading:** one interpretation of a hindsight grid's tempo level and
  phase: as tracked, off-beat, double tempo, or half tempo on either beat.
- **Settle delay:** how long after a press its authoritative judgment and
  groove change arrive: 2 s.
- **Judged time:** song time after the first 16 s at which the evidence is at
  or above the gate. The groove checks measure shares of judged time.

The proposal rests on an experiment summarized in the
[evidence appendix](#appendix-evidence).

### The proposal in brief

- **Two clocks for two jobs.** Effects must be scheduled ahead of the music,
  so they use committed beats forecast by a causal tracker. Judging can wait,
  so it uses hindsight grids from a stronger model and 2 s more audio.
- **The player decides the tempo level and phase.** The audio cannot settle
  whether a song runs at 90 or 180 beats per minute, or which pulse is the
  beat. The player's presses choose among the plausible readings. A guard
  makes sure random presses cannot exploit that choice.
- **The groove settles 2 s late.** A press shows a provisional rating
  instantly. Its authoritative rating, the groove change, the tier and weapon
  sizes follow 2 s later.
- **Nothing is judged when the beat is unclear.** Below the gate the groove
  neither rises nor drains, and percussive gameplay sounds are muted.
- **The world also follows the player.** Each press makes the world react,
  and when the music's beat is unclear, the world's pulse follows the
  player's presses instead.
- **A generic director replaces the level script.** Without song structure,
  the horde and level-ups follow elapsed time and loudness.
- **Causality is checked.** A check changes every stream entry after a cut
  (values, timings, counts and the song's length) and requires everything
  before the cut to stay identical, including sounds and rendered frames.

### Decisions

These choices were made with the user while designing. The executor treats
them as settled.

- **Stage 1 runs in the existing web build** as a new mode in this
  repository. It reuses the simulation, renderer, HUD, content and assets.
  A native shell comes in a later stage (see [Later Stages](#later-stages)).
- **Player presses are input to the beat,** not just judged against it, for
  both mechanics and effects.
- **Judgment waits** for hindsight; effects do not.
- **The settle delay is 2 s.** The experiment found the stronger model's
  accuracy already at its plateau with between 0.15 s and 2.15 s of
  hindsight. 2 s is a choice inside that plateau, not a measured minimum.
- **Someone who consistently presses on the off-beat is accepted** as
  keeping the beat. The guard cannot tell that player from one who hears the
  backbeat as the beat, and either is musical.
- **The game never delays the music.** Muting the source application and
  replaying its audio late would buy real lookahead, but the user rejected
  it as too risky. No stage of this plan captures audio in a way that
  changes what the player hears.
- **Stage 1 targets beat-driven music.** The classical songs stay with the
  authored levels. Reactive mode accepts them, but judges much of them as
  unclear.

### Goals

- A player can choose a track, play it in reactive mode from start to end,
  and see the late-settling groove, the gate and the press-reactive world
  working.
- Every simulation decision, sound and rendered frame depends only on stream
  entries known at the time, enforced by an automated check.
- On steady beat-driven tracks, an honest player's groove behaves close to
  play mode's: tier 2 or better most of the time, while mashers stay at
  tier 0.
- The user can add their own audio files locally with one command.
- Existing modes do not change: the golden demo hashes, `check:play` and
  `check:seeds` pass unchanged.

### Non-goals

- Capturing audio from other applications, or analysing audio in real time.
  Stage 1 computes the belief stream offline from a file.
- Set pieces (boss, formations, archer volleys, closing rings, stampedes),
  accent cleaves and the finale blast. They all need structure or accents
  known in advance.
- A tuned difficulty curve. The director only escalates.
- Fusing presses into the effect forecast. This document names it as a
  later experiment.
- Mobile or touch input.

## Related Information

- [README.md](../../README.md): how the game runs, the checks, URL
  parameters and the play log.
- [gameplay-redesign.md](gameplay-redesign.md): today's rules. This document
  reuses its terms and its press classes, ratings, drift, drain and tiers,
  and [Sound on the Grid](gameplay-redesign.md#sound-on-the-grid).
- [interactive.md](interactive.md): play mode's live simulation, rollback and
  sound scheduling.
- [tech-demo.md](tech-demo.md): the song map and the beat-map workflow that
  reactive mode replaces with a tracker.
- Code that this design touches:
  - [src/sim/schedule.ts](../../src/sim/schedule.ts): the song schedule. Its
    header calls it "everything the game may know in advance because it owns
    the song", which is exactly what reactive mode loses.
  - [src/sim/simulate.ts](../../src/sim/simulate.ts): the simulation, the
    meter (`meterChange`, `setTier`, `lastRaiseS`) and the beat events that
    drain it.
  - [src/sim/timing.ts](../../src/sim/timing.ts): `classifyPress` and its
    `PressMemo` (keyed by grid index), `driftOf` and the meter arithmetic.
  - [src/planner/context.ts](../../src/planner/context.ts) and
    [src/planner/sim-checks.ts](../../src/planner/sim-checks.ts): `Ctx`,
    `checkCausality` and `prefixDifference`.
  - [src/planner/emit.ts](../../src/planner/emit.ts) and
    [src/live/recording.ts](../../src/live/recording.ts): sound placement,
    the voice budget and the gem chime's key (`keyScale`).
  - [src/plan/evaluate.ts](../../src/plan/evaluate.ts) and
    [src/runtime/renderer.ts](../../src/runtime/renderer.ts): the beat phase
    and every beat-synced pulse.
  - [src/live/runner.ts](../../src/live/runner.ts): live play runs the
    simulation 0.2 s ahead of the audio.
  - [src/runtime/app.ts](../../src/runtime/app.ts),
    [title.ts](../../src/runtime/title.ts) and
    [play.ts](../../src/runtime/play.ts): mode selection and play flow.
  - [tools/analysis](../../tools/analysis): the Python analysis tool
    (madmom, librosa) that gains the stream and synth commands.
- The experiment's scripts and full report:
  `/Users/dthurn/playlist_survivors/tools/analysis/work/causal-beats/`
  (gitignored, in the main checkout only). Bead 1 moves `synth.py` from there
  into the analysis tool.
- Background: madmom's online beat model (a unidirectional LSTM) and its
  dynamic Bayesian network beat tracker, used here in forward-only mode.

## Design Overview

```text
audio file ──ps-analysis stream──► stream.json (every entry has a knownAt)
                                       │
       ┌───────────────────────────────┼─────────────────────────────┐
       ▼                               ▼                             ▼
committed beats                 hindsight grids               ticks, onsets
(forecast ~2 beats ahead)       (every 0.5 s, 8 s of beats)  (evidence, energy)
       │                               │                             │
reactive song map and          settle step, 2 s after         gate, director,
schedule: strikes, sounds,     each press: reading chosen     effects
beat events, visuals           from presses, groove, tier
```

The simulation still receives one `Ctx` and one `Schedule`, built before it
runs. Their beats are the committed beats, and `Ctx` gains a `reactive` field
holding the whole stream with its known-at times. That keeps the change small
and the simulation deterministic. It also means the code *could* read the
future, so causality is enforced by checks
([Causality](#causality)) rather than by the data structures. A live analyzer
in a later stage needs append-only structures instead.

### Rules that hold everywhere

- **No early reads.** A simulation decision at song time `s` depends only on
  stream entries with `knownAt <= s`. A rendered frame at song time `s` uses
  only entries known by `s`.
- **Committed beats are final.** They are never revised, so strikes and
  sounds placed on them never move.
- **Judgment is authoritative only when settled.** The provisional rating is
  feedback; only the settled rating changes the groove.
- **Existing modes are untouched.** Every reactive behaviour is switched on
  by the reactive level (`reactive: true`). New recorder and plan fields are
  absent from non-reactive runs, so the golden hashes do not move.

## The Belief Stream

A new analysis command writes one JSON file per track:

```sh
npm run analysis -- stream public/reactive/house-128/house-128.ogg \
  --out public/reactive/house-128/stream.json --reference house-128.beats.json
```

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

- **Commit horizon:** 2 beat periods plus 0.35 s, using the period believed
  at that activation frame. At 120 beats per minute that is 1.35 s.
- **Why so far ahead:** the simulation reads past the current beat in many
  places. Examples: a swing's wind-up and its heavy-or-light choice, the
  stride period, the off-beat midpoint, sixteenth slots, a downbeat on a
  2-beat bar, and the groove drain's bar length. Live play also runs the
  simulation 0.2 s ahead of the audio, and sounds need 0.06 s of notice.
- **Rule:** at each activation frame, the next beat after the last committed
  one (at least half a period after it) is committed once its forecast falls
  within the horizon. If a sudden belief change puts it less than 0.1 s ahead,
  it is skipped rather than committed late.
- **Entry:** `{ t, knownAt, period, evidence }`:
  - `knownAt` is the activation frame at which it was committed;
  - `evidence` is the evidence at `knownAt`;
  - times are strictly increasing.
- **Warm-up:** beats committed before the tracker locks carry low evidence,
  so they are muted and unjudged like any other unclear beat.

The experiment measured commit leads of only 0.25 s and 0.5 s. On steady
beat-driven music, going from 0.25 s to 0.5 s cost under a point. The 1.35 s
horizon is unmeasured, so a check gates its accuracy
([Automated Checks](#automated-checks)). If that gate fails, the executor
takes the fallback below without asking.

**Fallback if the horizon costs too much:** shorten the horizon to 1 period
plus 0.35 s. Then rewrite the reads that need the beat after next to
extrapolate it from the last two known beats instead:

- the groove drain's bar length;
- the off-beat midpoint after the next beat;
- the second beat of a 2-beat bar.

The perturbation check verifies the rewrites.

### Ticks, onsets and energy

- **Ticks** every 50 ms, each with `knownAt = t`:
  - `evidence`: the mean peak beat activation within ±30 ms of the last 4
    beats of the believed grid. The tracker's own state confidence stays near
    1 even in a breakdown, so it is useless here.
  - `energy`: loudness over the last 0.5 s, mapped to 0 to 1 between the 5th
    and 95th percentiles of the past 30 s.
- **Onsets:** causal spectral-flux onsets (madmom's superflux with online peak
  picking), each `{ t, knownAt, strength }`. The experiment measured
  detection 10 ms after the drum hit (90th percentile 14 ms).
- **"The evidence at time `x`"** means the evidence of the latest tick at or
  before `x`. "The energy at `x`" likewise.

### Hindsight grids

- **Every 0.5 s** at time `T`, madmom's offline beat model (bidirectional
  LSTM) runs on the audio from `T − 30 s` to `T`, and its offline beat tracker
  decodes that window.
- **Entry:** `{ knownAt: T, beats }`, keeping the beats from `T − 8 s` to `T`.
  That covers the 8 s window from which readings are chosen.
- **Why this model:** with between 0.15 s and 2.15 s of hindsight it matched
  97% of the beats on the beat-driven tracks, against 92% for the causal
  model. More waiting did not help. See the [appendix](#appendix-evidence).

### File format

```json
{
  "version": 1,
  "audio": { "file": "house-128.ogg", "duration": 62.5, "sampleRate": 44100 },
  "params": {
    "fps": 100, "minBpm": 55, "maxBpm": 215,
    "commit": { "periods": 2, "extraS": 0.35, "minLeadS": 0.1 },
    "gate": 0.2, "tickS": 0.05,
    "hindsight": { "everyS": 0.5, "windowS": 30, "keepS": 8 },
    "biasS": -0.010
  },
  "beats": [{ "t": 0.512, "knownAt": 0.0, "period": 0.469, "evidence": 0.0 }],
  "ticks": [{ "t": 0.05, "evidence": 0.0, "energy": 0.0 }],
  "onsets": [{ "t": 0.503, "knownAt": 0.51, "strength": 4.2 }],
  "hindsight": [{ "knownAt": 0.5, "beats": [] }],
  "reference": {
    "source": "exact",
    "beats": [0.5, 0.969],
    "sections": [{ "label": "breakdown-pads-only", "s0": 98.5, "s1": 114.5 }]
  }
}
```

- `reference` is optional. Synthetic tracks carry their exact beats and
  sections. Other tracks carry the offline whole-song decode
  (`"source": "offline"`) and no sections. Only the virtual players and the
  checks read it, never the simulation.
- The stream command prints accuracy statistics, but the checks compute their
  own from `beats` and `reference`.

### Causality of the stream itself

The command must never let a later sample influence an earlier entry. A
Python test proves it on a 12 s excerpt of a synthetic track: the stream of
the excerpt cut at `X` must equal the full excerpt's entries with
`knownAt <= X − 0.05`, to within 1 µs for times and 1e-6 for values. Anything
that normalizes over the whole file breaks that test, which is why energy
uses only the past 30 s.

## Tracks

- **Shipped:** five synthetic tracks with exact beats, generated by a new
  `ps-analysis synth` command (moved from the experiment's `synth.py`):
  - `house-128`: steady four-on-the-floor.
  - `dnb-174`: drum and bass at 174 beats per minute.
  - `hiphop-92-swing`: swung, syncopated.
  - `accelerando-100-140`: a steady start, a long accelerando and a steady
    end.
  - `pop-arrangement`: a chords-only intro, verse, chorus, a pads-only
    breakdown, chorus, then a hard cut from 120 to 96 beats per minute.
- **A check-only track:** `causality-mix`, about 24 s, generated by the same
  command: steady drums, a hard tempo cut, then a few seconds of pads only.
  It exercises short-lead commits and the gate in a run short enough to cut
  at every entry.
- **Where they live:** `public/reactive/<id>/` holds the audio (Ogg Vorbis, to
  keep the repository small) and `stream.json`. `public/reactive/index.json`
  lists each playable track's id, title and files; `causality-mix` is not
  listed.
- **Local tracks:** `npm run reactive:add -- <audio file> [--id <id>]` runs the
  stream command and copies the audio and stream into
  `public/reactive-local/<id>/`, which is gitignored. It also updates
  `public/reactive-local/index.json`. Local tracks get an offline reference
  so the virtual players can play them.
- **Loading:** the game fetches both indexes at runtime. A missing local
  index is not an error.

## The Reactive Song Map and Level

### The reactive song map

Built in the browser from a stream:

- `beats`: the committed beat times.
- `meter`: 2 beats to the bar throughout, counted from the first committed
  beat. Bars carry no musical meaning here; 2-beat bars keep every "next
  downbeat" read within 2 beats.
- `sections`: one pulsed section, `reactive`, covering every bar, with no
  `key`. Gem chimes then use the palette's unkeyed fallback (`keyScale`
  returns undefined).
- `phrases`, `accents`, `tempoChanges`: empty.
- `energy`: one value per committed beat, the energy at that beat's
  `knownAt`.
- `audio`: from the stream; `duration` is the audio's, used only for the
  run's end.

`Ctx.reactive` holds the stream itself: the committed beats with their
known-at times, the ticks, the onsets and the hindsight grids. Code that needs
"the first beat known by a time" reads it there.

### The reactive level

`levels/reactive.yaml` is a level script for that one section, plus a new
`director` block that only reactive levels may use:

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

- The numbers above are starting values. The executor may tune them; the
  checks gate behaviour, not these values.
- **Population:** the target at song time `s` is
  `min(max, (start + perMinute × s / 60) × scale)`, where `scale` maps the
  energy at `s` into `energyScale`. It replaces `targetPopulation`'s
  interpolation over the section, which reads the section's end.
- **Roster:** the entry with the largest `from` at or before `s`.
- **Level-ups:** each falls due at the first committed beat at or after its
  `at`, among the beats known when `at` arrives.
- **Zone waves:** every `zoneEvery` seconds, on the first known committed
  beat at or after that time, in the order of `zones`.
- **Off in reactive mode:** level-script events, accent punches, cleaves, set
  pieces, the blast, and time-scale changes (the base rate stays 1).
- **Structural rewrites:** two features schedule work from song-wide data at
  start-up, which the check cannot see because each queued event fires only
  at a known beat. In reactive mode they are rewritten as follows:
  - weapon fires are queued one beat at a time as each beat event fires,
    instead of to the end of the song at start-up (`scheduleFires`);
  - an upgrade orb's 2-bar deadline is counted in known committed beats as
    they arrive, with no fallback to `duration` (`riseOrbs`).
- **The run ends** when the audio ends. The end card shows the letter grade
  from the average settled groove over judged time.

## Judging Presses

### Provisional and settled ratings

Each press is judged twice:

- **Provisional, at the press:** `classifyPress` against the committed beats,
  with its own memory, so it never disturbs the settled judgment. It drives
  the instant feedback (the overhead rating and the press ripple) and the
  play log. It never changes the groove.
- **Settled, at the press time plus 2 s:** `classifyPress` against the chosen
  reading of the latest hindsight grid with `knownAt <= p + 2`, so every press
  is judged with between 1.5 s and 2 s of hindsight. This is the
  authoritative class and rating. It changes the groove as
  [Timing and the Groove](gameplay-redesign.md#timing-and-the-groove) says:
  same classes, windows, drift, values, tiers and hysteresis.
- **Before settling exists** (beads 2 and 3), reactive presses are judged
  instantly against the committed beats, as play mode judges them today.

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
  time within 70 ms of a registered beat is that beat; otherwise it is
  registered as a new one. 70 ms is under half the shortest beat at 215 beats
  per minute (279 ms).
- **Counted presses** are remembered by the beat's identity.
- **Off-beats** are remembered by the identity of the beat before them.
- **`classifyPress` gains an optional key function** mapping a grid index to
  the key its memory uses. Its default is the index itself, so play mode
  behaves and hashes exactly as before.
- Example: a press counted for the beat at 10.000 s under "as tracked" still
  blocks a second counted press for that beat after the reading changes to
  "half A", whose grid includes 10.004 s.

### Choosing the reading

Every hindsight entry yields five candidate grids from its beats `D`:

- **as tracked:** `D`
- **off-beat:** the midpoints of `D`
- **double:** `D` and its midpoints
- **half A** and **half B:** every other beat of `D`, starting with the first
  or the second

The reading used to settle a press `p` is chosen from the entry used for it
(time `T`) and the presses in `[T − 8 s, T]`, **leaving out `p` itself**:

```text
for each candidate c:
  hits   = share of those presses within 45 ms of a beat of c
  chance = min(1, 2 × 0.045 s × (beats of c per second))
  score  = hits − chance
the incumbent is the candidate whose beats best match the grid chosen for the
previous settle (most beats within 70 ms where the two overlap); at the first
settle, or with no overlap, it is "as tracked"
the best-scoring candidate is chosen if it beats the incumbent by more than
the margin (0.10); otherwise the incumbent is chosen
```

- **Why the incumbent is found by matching beats:** the labels "half A" and
  "half B" depend on which beat opens the 8 s window, which changes as the
  window slides. Matching the previous grid's beats keeps the same reading in
  force from one hindsight entry to the next.

- **The chance term:** random presses hit any grid at about its chance rate,
  so their scores sit near 0 for every candidate. Without the term, a masher
  at 4 presses a second picked the double-tempo grid and doubled its Perfect
  share (see the [appendix](#appendix-evidence)).
- **Leaving out `p` and the margin** stop a press from choosing the grid that
  rates it, and stop the reading from flipping on noise.
- **Knobs:** the window (8 s), the margin (0.10) and the 45 ms hit window may
  be tuned to pass the checks. The checks' thresholds may not.
- **No reading fits:** if the chosen grid has no beat within the Good window
  (140 ms) of the press, or the entry has no beats, the press settles
  unjudged.

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
  tracked stays.
  Either way each press is the counted press for its beat, as in play mode.
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
  classifies the press and applies the groove change.
- **Drain events:** each committed beat at `b` queues a drain event at `b + 2`,
  which applies today's drain rule as of time `b`.
- **The tier** follows the settled groove, so weapon sizes, damage, the pickup
  radius and the tier chimes lag the player's timing by 2 s.
- **Pending settles and drains live in the simulation state,** so snapshots,
  clones and rollback carry them.
- **Drift** comes from settled counted presses, as today.

### When the beat is unclear

- **Unjudged presses:** a press whose evidence at `p` is below the gate
  settles as **unjudged**. It shows grey, changes nothing and does not count
  as raising the groove.
- **The drain clock stops:** the time since the latest raise counts only time
  whose evidence is at or above the gate. A stretch below the gate neither
  drains the groove nor brings the next drain closer. Play mode's free
  sections freeze the groove in the same spirit.
- **Provisional feedback** for such a press is grey too.
- In the experiment, this gate muted 91% of the pads-only breakdown and 95% of
  a free orchestral prelude, while playing everything in steady beat-driven
  sections.

### What the player sees

- **The overhead rating** shows the provisional rating at once, as today.
- **The groove bar** shows the settled groove, so it moves 2 s after the
  presses that move it.
- **A correction mark:** when a settled rating differs from the provisional
  one by a class or more, a small flash on the groove bar shows the settled
  rating's colour.
- **The play log** records each press's provisional and settled class and
  rating, the reading chosen, the evidence and the settle time.
  `npm run log:play` prints the counts by settled class and how often the
  provisional and settled ratings agree.

## Effects

### Sounds on committed beats

- **Gameplay sounds** follow
  [Sound on the Grid](gameplay-redesign.md#sound-on-the-grid), with committed
  beats as the grid. A sub-beat slot belongs to the latest committed beat at
  or before it. With the commit horizon above, every slot a sound needs is
  known in time.
- **Muted when unclear:** a percussive gameplay sound whose slot belongs to a
  beat committed below the gate is not played.
- **Exempt:** hurt, level-up, tier chimes and the orb being taken play below
  the gate too, because they carry information. The sound check skips them.

### Beat-synced visuals

- **Today's pulses** (renderer beat and bar pulses, the hero aura, sway, gem
  and chicken bob, the HUD heart and the overhead beat gem) read the beat
  phase from the committed beats.
- **They fade with evidence:** the pulse amplitude is multiplied by the
  evidence at the frame's song time, mapped from 0 at the gate to 1 at twice
  the gate.

### Reactive accents

- **Strong onsets** (strength in the top 10% of the onsets in the past 30 s)
  give a short screen flash and a small camera punch, starting at the onset's
  `knownAt`, with no lead-in.
- **No accent sounds:** a reactive sound on a transient would land 20 to 50 ms
  late and audibly flam, so accents stay visual.

### Continuous effects

- **Energy** drives the ambient light level, the density of ambient particles
  and the population target's scale.
- These are smoothed over 0.5 s, so the analysis lag reads as smoothing.

### The world follows the player

Presses are the game's cleanest rhythmic signal: exact, instant and never
ambiguous about tempo. Three effects use them:

- **Press ripple:** every press sends a ring out from the hero, tinted by its
  provisional rating (grey when unjudged, red when stray).
- **Pulse source:** while the evidence is below the gate, the world's beat
  pulse follows the player's presses, with a pulse on each press decaying over
  250 ms. Above the gate it follows the committed beats, crossfading over
  0.5 s.
- **Agreement glow:** the ground light brightens with the share of the last
  2 s of presses whose provisional rating was Great or better.

Fusing presses into the beat forecast itself, so strikes follow a player who
keeps a steady beat through a breakdown, is a later experiment.

## Mode and Flow

- **Choosing it:** the title screen gains **Reactive (experimental)**, which
  opens a track list from the indexes. `?mode=reactive&track=<id>` skips the
  title screen's choice. `?player=` works in reactive mode with the reactive
  players.
- **Loading:** the game fetches the track's `stream.json`, builds the reactive
  song map, loads `levels/reactive.yaml` with the shipped content, and plays
  the track's audio through the existing audio engine.
- **Play flow:** count-ins, pause, Restart song, Quit to title, YOU FELL, the
  end card and Watch my run work as in play mode. Restart section restarts the
  song, since there is one section. `?start=` hands over after
  `reactive-moderate` plays up to it.
- **Best grade:** saved per track under its own key.
- **Live play:** the runner, rollback, snapshots and sound scheduling are
  unchanged.

## Virtual Players

Reactive players press against the stream's reference beats, as a human
hears the music, never against committed beats or hindsight grids:

- `reactive-moderate`: every beat, 30 ms standard deviation.
- `reactive-human`: every beat, 35 ms standard deviation and 35 ms late, like
  play mode's `human` player.
- `reactive-half`: every other reference beat, 30 ms standard deviation.
- `reactive-offbeat`: every reference off-beat, 30 ms standard deviation.
- `mash4` and `mash8`: random presses at 4 and 8 a second (existing players).

Steering reuses the existing roaming and threat avoidance. They need a
reference, so a track without one plays only with mashers.

## Causality

The simulation receives full arrays, so checks prove that nothing reads
beyond what is known.

### The perturbation check

For a cut at song time `C`, every stream entry with `knownAt > C` is
changed deterministically (seeded by the track and `C`):

- **Committed beats:**
  - every fifth is deleted;
  - an extra beat is inserted in every seventh gap;
  - the rest move by up to ±35% of their period.

  Every changed beat stays strictly increasing and later than both
  `max(knownAt, C) + 0.1 s` and the last unchanged beat plus a quarter period.
- **Ticks, onsets:** evidence, energy and onset strengths are replaced with
  seeded random values. Onset times move by up to ±50 ms, and every third
  onset is deleted.
- **Hindsight entries:** their beats move by up to ±35% of a period, and every
  fifth entry loses half its beats.
- **The song's length:** `audio.duration` becomes `C` plus a seeded value
  between 10 s and 60 s.

The run with the changed stream and the same presses must match the run with
the true stream before `C`. "Match" covers:

- the simulation: `prefixDifference`'s comparisons, plus the settled groove,
  the tier, tier chimes, gems, level-ups and zone waves;
- the sounds: the live recording's sound events (sample, slot and gain)
  before `C`;
- the frames: the evaluator's frame state (HUD values, beat phase, pulses
  and effects) at every 1/30 s before `C`.

The reactive comparisons live in a reactive variant of `prefixDifference`,
so play mode's own causality check in `check:seeds` stays exactly as it is.

### Cuts

- **`causality-mix`:** a cut just after every committed beat's `knownAt`,
  every hindsight entry's `knownAt`, every strong onset's `knownAt`, and every
  level-up and zone wave. The check runs it with a level override that
  compresses the director (level-ups at 5, 10 and 15 s, a zone wave every
  6 s) so they happen within the track.
- **Shipped tracks:** a cut every 10 s, in `npm run check:reactive -- --full`.

### Players and the hero

- **Presses:** the checks run `reactive-moderate` and `mash4`, recording the
  presses from the true run and feeding the same key events to the changed
  run.
- **Damage off:** check runs use a check-only simulation option that turns
  damage to the hero off, so the untuned director cannot end a run early.

### A negative control

A unit test runs the check against a simulation variant with a test-only
option that makes the spawn count read the committed beat three beats ahead.
The check must fail. This proves the check is not passing vacuously.

### Not covered in stage 1

The stream is precomputed, and the rule is `knownAt <= s` in simulation time.
Live play runs the simulation 0.2 s ahead of the audio, so a live analyzer
will have to deliver ticks, onsets and hindsight entries 0.2 s sooner, or
treat their arrival as a rollback. That is a later stage's problem.

## Automated Checks

`npm run check:reactive` runs everything below except the `--full` cuts. It
joins `scripts/ci` with a budget of 60 s on the developer's machine. It fails
on any of these:

- **Causality:** the perturbation check on `causality-mix` passes, and the
  negative control fails as it should.
- **Determinism:** the same stream and players simulated twice give the same
  plan hash.
- **Stream quality:** computed from `beats` and `reference`, after the first
  4 s:
  - on `house-128` and `dnb-174`, at least 95% of committed beats fall within
    45 ms of a reference beat;
  - the same figure is printed, not gated, for the other tracks.
- **Mechanics,** with damage off, on `house-128`, `dnb-174` and
  `pop-arrangement`, as shares of judged time:
  - `reactive-moderate` and `reactive-human` are at tier 2 or higher at least
    60% of the time;
  - `reactive-offbeat` is at tier 2 or higher at least 50% of the time;
  - `reactive-half` is at tier 2 or higher at least 40% of the time;
  - `mash4` and `mash8` are at tier 0 at least 80% of the time (play mode's
    rule for mashers);
  - no groove change happens less than 2 s after the press it belongs to.
- **Readings,** as unit tests of the settle step with synthetic grids:
  - a `reactive-moderate` press list against a hindsight grid at half tempo
    chooses "double";
  - against a grid at double tempo it chooses the half reading that holds
    the player's beats (0.95 − 0.18 = 0.77 against 0.95 − 0.36 = 0.59);
  - against a grid shifted by half a beat it chooses "off-beat";
  - in each case at least 85% of the presses settle Perfect or Great;
  - random presses at 4 and 8 a second keep "as tracked" at least 90% of the
    time.
- **The gate,** in `pop-arrangement`'s pads-only breakdown (from the
  reference's sections):
  - at least 80% of `reactive-moderate`'s presses settle unjudged;
  - the settled groove at the breakdown's end is at least its value at the
    start minus 0.1.
- **Sounds:** every percussive gameplay sound lies on a committed-beat slot of
  its subdivision within 1 ms, and none belongs to a beat committed below the
  gate, apart from the exempt sounds.
- **Unchanged modes:** `npm test` (with the golden hashes),
  `npm run check:play` and `npm run check:seeds` pass without changing any
  fixture.

`npm run check:analysis` gains the stream command's causality test and tests
for the synth command, the commit rule and the file format.

If a mechanics threshold proves unreachable, the executor tunes the reading
knobs, the gate's mapping and the director. It does not lower the threshold.

## Later Stages

Named here so stage 1 does not block them. Each needs its own design.

- **Live analysis:** a local Python process first, then a TypeScript or
  WebAssembly port of madmom's online model and the forward algorithm. The
  schedule becomes append-only, committed beats arrive as the music plays,
  and the 0.2 s runner lead is accounted for.
- **Browser capture:** a tab's audio through screen sharing in Chrome.
  Whole-system capture varies by operating system and browser version.
- **Native shell:** Tauri or Electron around the same web build, with a small
  native audio module: a Core Audio process tap on macOS, or WASAPI loopback
  on Windows. It only listens: it captures other applications' audio,
  excluding the game's own sounds, and never mutes, delays or replays that
  audio (see [Decisions](#decisions)).
- **Press fusion:** feeding the player's presses into the beat forecast.
- **Reactive set pieces:** rings, stampedes and a boss triggered by detected
  events such as a big onset after a quiet stretch.

## Alternatives Considered

- **Analyse the whole file first.** That is what the game does today. It
  cannot extend to audio from another application, which is the point.
- **Judge against committed beats only.** Simpler, but the hindsight model
  matched 97% of beats against the causal model's 92% on beat-driven tracks.
  The settle delay costs only a groove bar that runs 2 s behind.
- **Judge with a short re-decode of the causal model.** Measured worse than
  the forward belief: 6 s windows flip tempo level.
- **Accept the beat or the off-beat everywhere.** It rescued honest players,
  but a masher at 4 presses a second then landed within the Good window 98%
  of the time.
- **Build a streaming schedule now.** Needed for live analysis, but it would
  rewrite the schedule before the design questions are answered. The
  perturbation check gives stage 1 the same guarantee.
- **Delay the music for lookahead.** A native module could mute the source
  application and replay its audio a few hundred milliseconds late, giving
  the game real lookahead. Rejected by the user as too risky: it changes what
  the player hears and how other applications behave.
- **A separate game.** Stage 1 reuses nearly everything, so a new mode is far
  cheaper.

## Manual QA

Automated checks cover causality and the groove's numbers. These need a
person:

1. **Feel of the late groove:** play `house-128` with honest presses, then go
   sloppy for 4 s. The groove bar should react about 2 s later, and that
   should read as fair rather than broken.
2. **Off-beat:** play `house-128` pressing on the off-beats. After a few
   seconds the ratings should settle to Perfect.
3. **The breakdown:** in `pop-arrangement`'s pads-only breakdown, the strikes'
   sounds should fade out, the pulse should follow your presses, and the
   groove should hold.
4. **The tempo cut:** at `pop-arrangement`'s cut to 96, strikes may land on
   eighths for a few seconds. That should sound acceptable, not wrong.
5. **Reactive accents:** the flashes on strong hits should feel on time.
6. **Your own music:** add two or three of your own beat-driven tracks with
   `npm run reactive:add`, play them, and note where the beat goes wrong.

## Implementation Plan

Tracked by epic `hv-s4pt`, one child bead per step below (`hv-s4pt.1` to
`hv-s4pt.5`). Each bead delivers through its own Tollgate worktree and keeps
`scripts/ci` green.

1. **Belief stream and tracks** (Python):
   - the `stream` and `synth` commands (including `causality-mix`);
   - the stream causality test;
   - the shipped tracks and index;
   - `npm run reactive:add`.
2. **Reactive simulation and causality** (TypeScript):
   - `Ctx.reactive`, the reactive song map and level, and the director;
   - switching structure-bound features off, and the structural rewrites;
   - the reactive virtual players and the damage-off option;
   - `check:reactive` with the perturbation check (simulation, sounds and
     frames), the negative control, determinism and stream quality.

   Presses are judged instantly against committed beats.

   Needs 1.
3. **Reactive mode in the app:** the title entry and track list, URL
   parameters, runtime loading, play flow and best grade. README section.
   Needs 2.
4. **The late-settling groove:**
   - provisional and settled judging, beat identity, readings, settle and
     drain events, and the gate's drain clock;
   - the HUD correction mark and play log fields;
   - the mechanics, readings and gate checks.

   Needs 2 and 3.
5. **Reactive effects:**
   - gated sounds and fading pulses;
   - reactive accents and continuous effects;
   - the press ripple, pulse source and agreement glow;
   - the sound check.

   Needs 2. Serialized after 4, since both change the simulation's event
   handling, the live recording and the HUD.

## Appendix: Evidence

From the experiment in this design's conversation; the scripts and full
report are in `/Users/dthurn/playlist_survivors/tools/analysis/work/causal-beats/`.
The tracker is the one this design uses.

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

**The evidence gate at 0.2.** On orchestral tracks, beats elsewhere fell from
12–36% to 4–9%. The pads-only breakdown played 9% of its beats (all of them on
the beat). Steady beat-driven tracks played every beat.

**Hindsight.** % of reference beats matched within 45 ms. Grids were decoded
every 2 s, so each row covers a 2 s range of hindsight:

```text
hindsight        beat-driven, causal   beat-driven, offline   orpheus, offline
0.15–2.15 s            92                    97                    59
10–12 s                91                    97                    56
whole song              -                    98                    48 (all misses: half tempo)
```

**Readings chosen from presses, with the chance guard.** % Perfect against
the whole-song decode, beat-driven synthetic tracks, readings chosen per 8 s
block:

```text
player                      song map today   chosen reading
human, 25 ms deviation           94               92
human, every other beat          93               91
off-beat presser                  0               91
masher, 4 a second               18               25   (37 without the guard)
masher, 8 a second               18               22
```

**Limits of the evidence:**

- The beat-driven tracks were synthetic: clean drum machine, no vocals.
- Only one tracker family was tried.
- Activation frames are 10 ms.
- The 1.35 s commit horizon was not measured.
- The reading figures used whole-song decodes and 8 s blocks, not 2 s
  hindsight grids with rolling choice. The checks above, not the appendix,
  decide whether the design meets its goals.
