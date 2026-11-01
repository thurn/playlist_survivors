# Game Modes

`--mode NAME` picks a way of playing the Unity game (`game/`, `just game`). Modes exist to
prototype gameplay that differs substantially from the designed game
([unity-game.md](unity-game.md)) without forking it: each is a named bundle of rule choices,
recorded in sessions and replayed faithfully.

```sh
just game --mode free --file "$PWD/public/reactive/house-128/house-128.ogg"
```

Filed as bead ps-y21 (2026-11-01). Native (`native/`) has no modes.

## The modes

| Name | Hero | Sword | Bots |
|---|---|---|---|
| `grid` (default) | hops a 4-direction grid on the stride steps ([Hero movement on the grid](unity-game.md#hero-movement-on-the-grid)) | strikes on every beat by itself ([The sword](unity-game.md#the-sword)) | yes |
| `free` | walks freely: 8 ways on the arrows, or to a clicked point | strikes on Z, its size graded by the beat | no |

Everything else (the horde, the other weapons, structures, the run) is the same in every mode.

## How modes are built

A mode is a row of `ModeRules` in `GameModes.All` (`game/Assets/Game/Sim/GameModes.cs`). Each
field is one system's choice:

- `Movement` (`HeroMovement.Grid` or `Free`): which `IHeroControl` the sim builds, the grid's
  `Hero` or the `FreeHero`.
- `Sword` (`SwordTrigger.Beat` or `Press`): whether the `Sword` plans strikes from the beats or
  waits for the attack key.
- `Bots`: whether `--bot` can play it.
- Derived: `Pointer` (clicks set targets) follows the movement.

The mode travels as data, never as flags read in place:

- `RunInputs.Mode` (from `--mode`) builds `SimConfig.Mode`; a session's `inputs.bin` (version 3)
  stores it, and `run.json` names it. A replay plays its session's mode, whatever the flags say.
- `Simulation.Rules` exposes the rules to the client, which reads them where it builds its input
  (`KeyInput` binds Z only for a press sword; `PointerInput` exists only for a pointer mode) and
  its presentation (`HeroMotion` draws `HeroGlide` events as a walk, `HeroStep` events as hops).

### Adding a mode

1. Add a `GameMode` value at the end (sessions store the number) and a row in `GameModes.All`.
2. A rule no mode has yet is a new `ModeRules` field whose default is today's behaviour, read
   where its system is built. Prefer a new implementation behind an existing seam
   (`IHeroControl`, the sword's trigger) to `if (mode == ...)` checks scattered through systems.
3. New player input is a new sim input recorded like keys (as `Simulation.Target` was added),
   so sessions replay it.
4. Tests: the mode's rules in the sim tests (`FreeModeTests` is the pattern), and a session of it
   replaying to its hash (`SessionTests.AFreeModeSessionReplaysToItsHash`).

## The free mode

### Walking

- **Arrows** (or WASD) steer, eight ways; diagonals as fast as the rest; opposite keys cancel.
- **Pointer**: a left click sends the hero straight to the ground point under the cursor; holding
  the button keeps sending the point under the cursor (whenever it moves 5 cm, the camera's drift
  included), so the hero follows it. An arrow press drops the target; held arrows win over it.
- **Speed**: a tile (1.5 m) per stride step, the grid hero's pace with a key held: 6 m/s at
  120 BPM. It follows the tempo, as enemy speeds do
  ([Speeds follow the tempo](unity-game.md#speeds-follow-the-tempo)).
- **Walls**: the hero is a box 0.45 m either side of its centre that never enters a blocked tile
  or leaves the arena. It moves one axis then the other, so it slides along walls. There is no
  path finding: a target behind a wall leaves the hero pressed against it.
- **Heard time**: like the grid's press strides ([Key presses](unity-game.md#key-presses)), a key
  change or target heard at h and taken by the sim at F re-walks the hero's path from h (up to
  64 ticks back, never across a freeze), so the client shows the turn on the key-down frame.
- Frozen (cards, chests, pause, music stopped) or fallen, the hero stands; a click while frozen
  is dropped, and the cards let go of the keys and the target.

The sim keeps the chord of the tick ahead in `HeroState` and emits a `HeroGlide` event (start,
position, velocity) whenever the velocity changes. `HeroMotion` draws the latest glide at the
song time, cycling the run clip once per 3 m walked, with no hop.

### Attacking on the beat

The sword never strikes by itself. Z (the sim's `Key.Attack`) swings it:

- **Timing: react now, land on the grid** (ps-w9p.1, Hi-Fi Rush's answer). Landing kill sounds and impact
  FX on the beat is much of why the trailer works, but the knight must answer Z at once. So a press heard
  at h winds up from h, in heard time like the walk's keys, so the knight is in the attack on the key-down
  frame, and its impact lands on the **first eighth** of the beat (a beat or half beat, from the beats
  revealed then) at least **0.12 s** (`Sword.GridWindupS`) after h. The blows, deaths, kill sounds,
  shatters and structure breaks all follow the impact, so they land on the grid too.
  - A press in time therefore hits an eighth later: one on the beat lands on the half beat after it,
    one on the half beat on the next beat. 0.12 s keeps that true for every full-grade press up to about
    158 BPM, and leaves the slash clips a visible wind-up even at their fastest retime; at 120 BPM a press
    in time waits 0.18-0.32 s, about the clips' whole wind-up.
  - The clip is retimed to fill the wait (`HeroClips.Fit`): from its first frame at the press when the wait
    fits 0.75x-1.33x; a shorter wait (a press just before an eighth) enters it part-way, still in the
    wind-up; a longer one (a slow tempo, a late press) **coils**, playing the first 60% of the way to the
    impact slowly, then the strike at 0.75x, rather than starting late with nothing on screen.
  - With no reliable beat at the press there is no grid: the impact is `max(h + 0.08 s, F + a tick)`, as
    before.
- **The swing**: presses' main impacts are at least 0.2 s apart, on the next eighth that leaves that much
  (so faster than 150 BPM, where an eighth is shorter than the swing, a press skips one). A press that would
  wait more than 0.1 s past its own landing for the swing before it is dropped: on the grid, one that would
  land on the eighth the pending swing already has. That swing is on screen, so a press is never swallowed
  while the knight shows nothing. A press during the swing before winds up from that swing's impact, which
  it keeps.
- **Grade** (`BeatGrade`): the press, in heard time, against the nearest beat or half beat of
  its beat interval. Within 70 ms it is full; from there it falls linearly to 0.5 at a quarter
  beat off, the furthest a press can be from that grid. At 120 BPM a press is full for about
  half of all moments, and only the eighths' exact midpoints reach 0.5.
- **No beat, full grade**: with no reliable beat at the press (no beat ahead, below the gate, the
  music unclear), every press is full.
- **Size**: the grade scales the strike's reach and its damage.
- **Heavy**: a full-grade press measured from a beat (not a half beat) that is accented, as the
  grid's heavy strikes ask (a strong onset or accent forecast within 50 ms), is a heavy spin
  with its cleave wave, which breaks structures. Once per beat. F6's forced accent makes the next
  press heavy. The spin lands an eighth after the accent, like any press in time, and its cleave's
  bands follow on the grid: the first on the eighth after the impact, the rest a sixteenth apart.
- **Amount** (the sword's rank 2 on, Duplicator and the like) adds follow-up strikes spread evenly
  to the next eighth (rank 2's on the sixteenth; over the swing with no beat); they never delay the
  next press, whose impact needs only 0.2 s after the last press's.
- Free presses never slam on a section change.

The other weapons (wand, bible, lightning, garlic, fire trail) keep firing on the beat by
themselves, aimed from wherever the hero walks.

### Swinging on the move

The player attacks almost always while walking, so a swing on the walk must read as one (ps-w9p.2). Standing,
the knight turns its whole body to the swing, as the grid hero does. Walking (`HeroMotion`, from the glides):

- **The legs keep the walk.** They face the travel, run forward and carry the hips: the torso layer (the
  humanoid mask's Body, hips and spine together) takes the run's hips and spine, not the attack's, so the
  attack's own hip and leg motion never fights the run cycle. The head, arms and blade carry the attack.
- **The upper body twists to the aim.** `HeroPose.Twist` is spread evenly over the spine, chest and upper chest
  after the graph evaluates, so the chest, arms and blade face the strike. It eases in over the first 60% of
  the wind-up (`TwistLead`), so the torso leads and the blade arc reads in the strike's direction, and unwinds
  over 0.25 s after the follow-through (`TwistOut`). A swing that enters before the one before has unwound
  carries its twist on, crossfading from that aim to its own, so a chain of swings never lets go between them.
- **Up to 120 degrees** (`TwistMax`) the twist alone reaches the aim.
- **Past it the whole body turns.** The twist unwinds linearly to none at 180 degrees, and the legs turn by the
  rest, so a strike straight behind faces the knight round, whichever side it is on (no flip across 180).
  Legs off the travel blend from the forward run (by the cosine of their angle off it) to a backpedal (the
  run clip played backward, by minus that cosine), through the attack's own footwork, which has the legs and
  hips square to the travel. The Synty packs have only forward run and walk clips (A_Run_F, A_Walk_F), so
  there is no true strafe.
- **Blending with standing**: the walk's weight (up over 0.03 s, down over 0.1 s) blends the walking legs and
  twist with the standing hero's whole-body turn, so stopping mid-swing hands over smoothly.
- The pose stays a function of the events and the song time, with no state machine. The grid hero's hops are
  unchanged: it hops a tile at a time and turns whole to its swings.

### Scripted and recorded play

- `--walk "right@9-11,z@10-10.05"` holds keys over song-time spans, Z included.
- Targets are recorded in `inputs.bin` beside the keys and replay exactly.
- `--bot` refuses the free mode: the bots steer a grid hero and leave the sword to the beat.

## Open questions for play-testing

- Is 70 ms the right full-grade window, and 0.5 the right floor? Both are constants in
  `BeatGrade`.
- Does the eighth's wait from press to impact (about 0.25 s at 120 BPM) read as weighty or as laggy,
  and does the coil read as anticipation or as a stall at slow tempos? (Closed: an early press's impact
  now waits for the grid, ps-w9p.1.)
- Should a heavy land on the accent itself (a press an eighth before it) rather than an eighth after a
  press on it? Today the grade and the heavy are both judged on the press.
- Does the walk need a pulse on the beat to read as rhythmic, now that the hero glides?
- Does a 120-degree twist read as athletic or contorted, and does the turn round for a strike behind read as a
  pivot or a snap? Do the legs read as skating while square to the walk, where the attack's footwork stands in
  for a strafe clip?
- Click-to-move without path finding: is pressing against a wall acceptable?
