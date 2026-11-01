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

Everything else (the horde, the other weapons, structures, the run) is the same in every mode. The opening's
denser crowd (ps-w9p.4, [Enemies](unity-game.md#enemies)) and the crowd's floor (ps-w9p.12: the crowd left behind
brought round ahead of a hero on the move, no minute's minimum below 80) are the spawner's and schedule.json's, so
every mode has them.

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

- **Timing: hit at once; the beat grades the payoff, never the timing** (ps-w9p.8). A press heard at h
  lands its main impact at `h + 1/30 s` (`Sword.PressImpactS`), beat or no beat: two 60 Hz frames after the
  key, so the key-down frame already shows the strike and the next its last sliver before the blow. The
  blows, deaths, kill sounds, shatters and structure breaks all follow the impact at once; nothing waits for
  the grid.
  - **In heard time** (ps-w9p.10). A live key reaches the sim about the lead after it was heard (outputLatency
    plus the margin: about 90 ms on the built-in speakers, 0.2-0.35 s on Bluetooth), past its impact. The sim
    resolves the press as it takes the key: each moment it has passed (wind-up, blows, a spin's first cleave
    bands) at its own time, against the world as it was then (where the walking hero was, where each enemy
    stood or stepped), the damage applied as it takes the key and the events stamped at their own times. Nothing is
    re-simulated: what happened in the lead stands (an enemy the blow killed may still have bitten), the
    design's no-rollback rule kept ([The sim lead](unity-game.md#the-sim-lead)). The client trails the sim by
    the lead, so the blow is drawn on time, and its sounds, already past the mixer, start as soon as it can
    ([Sound timing](unity-game.md#sound-timing)): key to blow seen in 35-51 ms whatever the output, to the
    swing heard in the output latency and up to 27 ms more
    ([game/evidence/press-latency](../../game/evidence/press-latency/README.md)). Before, impacts landed a tick
    after the sim took the key, a lead late, and play-testers found the swings "barely connected to the button
    press".
  - A scripted key (`--walk`) is heard as the sim takes it, so its impact is simply 1/30 s on. The pose reacts
    on the key-down frame, in heard time, and plays into the impact from there.
  - The knight's pose (`HeroMotion`, Presses): the press's wind-up starts at h, so the key-down frame is
    already in the strike. The clip plays up to 2.5x (`HeroClips.PressSpeed`), entered just short of its
    impact frame (a sliver of the wind-up: 0.08 s of the clip at 1/30 s), and follows through as fast (the
    first slash's 0.37 s follow-through in 0.15 s), so the next press never fights a long recovery. Every
    press's blow holds 50 ms at 0.25x then catches up (`PressSlow`, demo3d's `HoldThenCatchUp`), and the drawn
    knight turns to the strike at once rather than easing over 0.04 s (`HeroPose.TurnAtOnce`), so a walking
    swing's twist is on the aim at the impact.
  - **Tried and rejected: react now, land on the grid** (ps-w9p.1, Hi-Fi Rush's answer). The impact landed on
    the first eighth at least 0.12 s after the press (0.18-0.32 s at 120 BPM), the clip retimed to fill the
    wait, and a long wait coiled. In play-test the swings felt "awful and barely connected to the button
    press": a quarter second between Z and the blow reads as lag, however well the wind-up fills it, and the
    blow on the grid sounded on the beat only for presses that were already in time. The music shapes the
    reward instead.
- **The swing**: presses' main impacts are at least 0.15 s apart (`SwingS`, the slash's sped-up length). A
  press in the swing before's follow-through waits for it, at most 0.15 s (`SwingBufferS`), cutting the
  follow-through short into the next swing; a press while a waiting swing still winds up (before its impact)
  is dropped, and that swing is on screen, so a press is never swallowed while the knight shows nothing.
  Mashing swings at most about 6.7 times a second; presses a swing apart all hit at once.
- **Grade** (`BeatGrade`): the press, in heard time, against the nearest beat or half beat of
  its beat interval. Within 70 ms it is full; from there it falls linearly to 0.5 at a quarter
  beat off, the furthest a press can be from that grid. At 120 BPM a press is full for about
  half of all moments, and only the eighths' exact midpoints reach 0.5.
- **No beat, full grade**: with no reliable beat at the press (no beat ahead, below the gate, the
  music unclear), every press is full.
- **The payoff** follows the grade, which the attack event carries (`AttackEvent.Grade`):
  - reach and damage scale with it, and the slash is drawn at that reach, so an off-beat press is visibly smaller;
  - the slash's light: 1.75x the authored on the beat (`EffectCues.OnBeatLight`), times the grade off it;
  - the blade's flare: 1.25x on the beat (`HeroMotion.OnBeatFlare`), the grade off it (a heavy's 1.5x as ever);
  - the swing's sound: as loud as the grade, and on the beat a light press layers the heavy swing's whoosh under
    its own at 0.5 (`SoundPresenter.OnBeatAccent`).
- **Heavy**: a full-grade press measured from a beat (not a half beat) that is accented, as the
  grid's heavy strikes ask (a strong onset or accent forecast within 50 ms), is a heavy spin
  with its cleave wave, which breaks structures. Once per beat. F6's forced accent makes the next
  press heavy. The spin hits at once, on the accent, and its cleave's bands spread fast over the
  swing after it: the first half a swing (75 ms) after the impact, the rest a quarter swing apart.
- **Amount** (the sword's rank 2 on, Duplicator and the like) adds follow-up strikes spread evenly
  over the swing after the main impact (rank 2's 75 ms on), not to the grid; they never delay the
  next press, whose impact needs only 0.15 s after the last press's.
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
  carries its twist on, crossfading from that aim to its own, so a chain of swings never lets go between them;
  one entering after it has unwound eases in from the travel.
- **Up to 120 degrees** (`TwistMax`) the twist alone reaches the aim.
- **Past it the whole body turns.** The twist unwinds linearly to none at 180 degrees, and the legs turn by the
  rest, so a strike straight behind faces the knight round, whichever side it is on (no flip across 180).
  Legs off the travel blend from the forward run (by the cosine of their angle off it) to a backpedal (the
  run clip played backward, by minus that cosine), through the attack's own footwork, which has the legs and
  hips square to the travel. The Synty packs have only forward run and walk clips (A_Run_F, A_Walk_F), so
  there is no true strafe.
- **Turning the walk mid-swing**: the presenter eases the legs' facing and the upper body's apart (0.04 s
  each, `HeroTurn`), so the legs swing round to the new walk under a torso that stays on the strike, rather
  than the torso riding the legs' lag off the aim. A press's strike, too soon for the ease, takes both at once up to its impact (`HeroPose.TurnAtOnce`).
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
- Does a strike entered a sliver short of its impact read as a sword swing or as a twitch, the spin above
  all (its flourish is mostly skipped)? (Closed: the eighth's wait from press to impact read as lag,
  ps-w9p.1, and was dropped, ps-w9p.8; the live lead from key to blow, ps-w9p.10.)
- A press's sounds start as soon as the mixer allows, with their lead-in skipped: does the swing's whoosh,
  cut to its attack, still read as a swing on speakers? On Bluetooth the output latency itself (about
  0.25 s) stays between the key and the sound.
- Is the payoff's difference (reach, light, flare, the swing's accent) enough to make pressing on the beat
  worth it, against mashing at 6.7 swings a second?
- Does the walk need a pulse on the beat to read as rhythmic, now that the hero glides?
- Does a 120-degree twist read as athletic or contorted, and does the turn round for a strike behind read as a
  pivot or a snap? Do the legs read as skating while square to the walk, where the attack's footwork stands in
  for a strafe clip?
- Click-to-move without path finding: is pressing against a wall acceptable?
- Is the opening's crowd (40 alive from the first second, 52 on a loud song; ps-w9p.4) right for a level-1
  hero whose sword only swings on Z, or does it want to start fuller still, or gentler?
