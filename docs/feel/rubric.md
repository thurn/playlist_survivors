# Reactive Feel Rubric

The rubric raters score each track's feel dossier against
([docs/design/reactive-feel.md](../design/reactive-feel.md), "The Rubric"). It
has ten dimensions: four for mechanics (M) and six for aesthetics (A). Each is
scored from 1 to 5. The anchors below describe 1, 3 and 5; a 2 or a 4 sits
between them.

Score what the dossier shows, not what the design intends. You never hear the
music: the timeline's reference lane (beats, downbeats, sections, accents) and
its stream lane (evidence, energy, level) stand in for it.

## The dossier

Each track's folder in `captures/feel/round-<n>/<id>/` holds:

- `metrics.md`: the measured numbers, paired with dimensions below. A metric
  marked n/a needs a later bead; score from the pictures alone there.
- `timeline.png`: the whole song in lanes. The reference lane is the ground
  truth: where the beats, bars and sections really are.
- `section-<k>.png`: one bar of a section, a row per beat of the bar, a column
  per beat phase (0, 0.25, 0.5, 0.75). Phase 0 is the beat itself.
- `phrase.png`: beat 1 of 8 consecutive bars of the loudest section.
- `moments.png`: stills 0.25 s after the identity, section starts, set pieces
  and the finale.
- `honest-vs-masher.png`: the same moments for the honest player (left) and a
  player mashing four presses a beat (right).

## Mechanics

### M1: Beat lock

Do strikes, sounds and the beat display land on the music's beat?

- **1:** strikes and sounds are visibly off the beat: phase-0 frames show no
  strike while later phases do, or the timeline's strikes drift from the
  reference beats.
- **3:** mostly on the beat, but drifting or locked to the off-beat in places,
  or slow to lock on at the start.
- **5:** locked everywhere the music has a beat: strikes on phase 0 in every
  section sheet, and the timeline's strikes line up with the reference beats.

Paired metrics: beat F-measure (strict and at any level), lock-in, sound sync.

### M2: Reward legibility

Can a viewer see that following the beat makes the player stronger?

- **1:** the honest player and the masher look alike: same strike sizes, same
  colours, same effects.
- **3:** the honest player is somewhat stronger: bigger or brighter strikes,
  but you have to look for it.
- **5:** following the beat obviously grows the attacks: the honest column of
  `honest-vs-masher.png` is clearly bigger, brighter and more varied in every
  row.

Paired metrics: honest tier 2+ and masher tier 0 shares, damage ratio, strike
size by tier.

### M3: Fair judging

Is the player judged when the beat is clear, and left alone when it isn't?

- **1:** clear beats go unjudged, or presses are misjudged where the beat is
  plain (the timeline's evidence is high but the groove falls).
- **3:** occasional misreads: a stretch judged that shouldn't be, or a clear
  stretch left unjudged.
- **5:** judged when the beat is clear, quiet when it isn't: the groove rises
  through clear stretches and holds through unclear ones.

Paired metrics: judged share of beat-driven time, honest presses settled
Perfect or Great.

### M4: Pacing fit

Does the run's arc fit the song's shape at its length?

- **1:** the arc ignores the song: nothing happens for most of it, set pieces
  pile up or are missing, or it ends wrong (a finale in the middle, none at the
  end).
- **3:** the arc partly matches: escalation and set pieces arrive, but early,
  late or unevenly for this song.
- **5:** escalation, set pieces and the finale match the song's shape at its
  length: big events in big sections, the finale at the end.

Paired metrics: set pieces, level-ups, zone waves, the share of the song
before each first, false finales, survival.

## Aesthetics

### A1: Coverage

How much of the world moves with the music?

- **1:** only a few elements react: the hero pulses, the rest is static or
  random.
- **3:** about half react: enemies, lights and effects move with the beat in
  some sheets, not others.
- **5:** nearly everything moves with the music: enemies, lights, particles,
  camera, grading and the HUD all change across a section sheet's phases.

Paired metric: coverage, n of 12 element classes reacting to the beat.

### A2: Visual sync

Are the visual pulses on the beat, crisp, and on the bars and accents?

- **1:** pulses are off-beat or mushy: the brightest or biggest frame of a row
  is not phase 0, or every phase looks the same.
- **3:** mostly on the beat, but soft or late in places.
- **5:** crisp on beats, bars and accents: phase 0 of each row stands out,
  beat 1 of a bar more than the others, and accents (orange on the timeline)
  get a visible hit.

Paired metric: visual sync (peaks within a frame of a beat).

### A3: Song identity

Does this track look and feel like its own game? Compare it with the other
tracks in your batch.

- **1:** indistinguishable from other songs: the same zones, colours and
  motion as the rest of the batch.
- **3:** some differences: a different zone order or tint, but the same feel.
- **5:** looks built for this song: its colours, motion and events
  fit its character (the timeline's energy and level) and differ from other
  songs'.

Paired metrics: identity (the zones seen, the mean grading); the identity
family arrives with a later bead.

### A4: Structure response

Does the game change when the music changes, and hold when it doesn't?

- **1:** ignores section changes, or changes at random times.
- **3:** catches some section changes, misses or invents others.
- **5:** changes when the music does (zone waves, set pieces or colour within
  a few seconds of each reference section start in the timeline and the
  moments sheet) and holds steady within sections.

Paired metrics: reference section starts followed by a visible change, stray
changes.

### A5: Intensity hierarchy

Are the big moments the biggest, without saturating?

- **1:** flat or noisy: everything is equally busy, or quiet bars are busier
  than loud ones.
- **3:** some contrast between quiet and loud sections.
- **5:** big moments clearly biggest, quiet ones calm, and never saturated
  (the screen never turns to a wash of effects).

Paired metrics: hierarchy (the correlation per bar of loudness and visual
intensity), the share of frames at the response budget's cap (the particle cap
until a later bead adds the budget).

### A6: Breakdowns and silence

How does the game behave where the beat drops out or the music goes quiet?

- **1:** garbage in the quiet: percussive sounds or beat-locked motion with no
  beat, a drained groove, sudden jolts.
- **3:** acceptable: not much wrong, but not much right either.
- **5:** graceful: effects fade, motion follows the player, the groove holds,
  and the beat returns cleanly.

Paired metrics: breakdowns (percussive sounds below the gate, press
following, intensity drop, groove drains).

## Calibration

Each round's first batch includes the authored Galop's play-mode dossier
(`anchor-orpheus-galop`) as an anchor. It should score about 5 on M1, M2, A2
and A4. If a rater scores it 3 or less on any of those, that rater's batch is
re-rated before triage.

## Scores

Each rater writes `scores.json` for its batch: each track's ten scores, and up
to three issues, each with its dimension, a song time, the evidence (a file and
row or column), what is wrong, and the area likely responsible. The rater
brief (`docs/feel/rater-brief.md`) gives the exact format.
