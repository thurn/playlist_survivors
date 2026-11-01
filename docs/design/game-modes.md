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

- **Timing**: a press heard at h, taken at F, winds up at once and lands at
  `max(h + 0.08 s, F + a tick)`. Presses' impacts are at least 0.2 s apart (the swing). A press that
  would wait more than 0.1 s for the swing before it is dropped, so mashing swings at most five
  times a second, and a press mid-swing never queues.
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
  press heavy.
- **Amount** (the sword's rank 2 on, Duplicator and the like) adds follow-up strikes spread over
  the swing; they never delay the next press, whose impact needs only 0.2 s after the last press's.
- Free presses never slam on a section change.

The other weapons (wand, bible, lightning, garlic, fire trail) keep firing on the beat by
themselves, aimed from wherever the hero walks.

### Scripted and recorded play

- `--walk "right@9-11,z@10-10.05"` holds keys over song-time spans, Z included.
- Targets are recorded in `inputs.bin` beside the keys and replay exactly.
- `--bot` refuses the free mode: the bots steer a grid hero and leave the sword to the beat.

## Open questions for play-testing

- Is 70 ms the right full-grade window, and 0.5 the right floor? Both are constants in
  `BeatGrade`.
- Should an early press's impact wait for the beat it was graded on, so the hit lands on it?
  Today every impact comes 80 ms after the press.
- Does the walk need a pulse on the beat to read as rhythmic, now that the hero glides?
- Click-to-move without path finding: is pressing against a wall acceptable?
