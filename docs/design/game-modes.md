# Game Modes

`--mode NAME` picks a way of playing the Unity game (`game/`, `just game`). Modes exist to
prototype gameplay that differs substantially from the designed game
([unity-game.md](unity-game.md)) without forking it: each is a named bundle of rule choices,
recorded in sessions and replayed faithfully.

```sh
just game --mode free --file "$PWD/public/reactive/house-128/house-128.ogg"
```

Filed as bead ps-y21 (2026-11-01); the zen mode as ps-myz (2026-11-01), its finishers (`--finishers`) as ps-qbd
(2026-11-01) and its dash (`--dash`) as ps-zz4 (2026-11-01). Native (`native/`) has no modes.

## The modes

| Name | Hero | Sword | Bots |
|---|---|---|---|
| `grid` (default) | hops a 4-direction grid on the stride steps ([Hero movement on the grid](unity-game.md#hero-movement-on-the-grid)) | strikes on every beat by itself ([The sword](unity-game.md#the-sword)) | yes |
| `free` | walks freely: 8 ways on the arrows, or to a clicked point | strikes on Z, its size graded by the beat | no |
| `zen` | walks freely, as `free` | strikes on the beat by itself, kept to the beat grid; its power follows the loudness | no |

Everything else (the horde, the other weapons, structures, the run) is the same in every mode, but in `zen` the horde
reels to the music ([The zen mode](#the-zen-mode)). The opening's
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
- `BeatGrid`: whether a beat sword keeps every strike on the beat grid (half beats on slow songs, a pulse's
  subdivisions on the beats it spans).
- `Loudness`: whether a beat sword's reach, damage and strike count follow the tick's loudness level.
- `Reels`: whether the horde reels to the music (heavy blows throw further, a zone change shoves and staggers, the
  crowd bobs on the kick).
- `Finishers`: whether `--finishers` may play it ([Finishers](#finishers)); only zen does, and the launch refuses the
  flag elsewhere. `--dash` ([The dash](#the-dash)) needs no field of its own: it needs `Movement` `Free` (zen's, or
  free's), and the launch and the sim refuse it with a grid hero.
- Derived: `Pointer` (clicks set targets) follows the movement.

The mode travels as data, never as flags read in place:

- `RunInputs.Mode` (from `--mode`) builds `SimConfig.Mode`; a session's `inputs.bin` (version 3)
  stores it, and `run.json` names it. A replay plays its session's mode, whatever the flags say. `--finishers` travels
  the same way (`RunInputs.Finishers`, `SimConfig.Finishers`, `inputs.bin` version 4, `run.json`'s `finishers`), and
  so does `--dash` (`RunInputs.Dash`, `SimConfig.Dash`, `inputs.bin` version 5, `run.json`'s `dash`).
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

## The zen mode

`--mode zen` is a plain survivors game that moves to the music: the hero walks freely, exactly as in the free mode
([Walking](#walking)), and the sword strikes by itself on the beat while it walks. Nothing is judged: no Z key, no
grade, no timing to get right. The music shapes the fight instead.

```sh
just game --mode zen --file "$PWD/public/reactive/house-128/house-128.ogg"
```

### The sword keeps to the beat

Slashes landing on the beat matter more than anything else here for game feel (the user, 2026-11-01). Every slash
impact lands on the tracker's committed beats, on a grid chosen by the beat's period:

| Tracked tempo | Strikes on | Slashes apart |
|---|---|---|
| under 80 BPM (a period over 0.75 s, `Sword.HalfBeatsAbove`) | every half beat, each a slash of its own | 0.375-0.75 s |
| 80 to 150 BPM | every beat (the grid mode's rule) | 0.4-0.75 s |
| over 150 BPM (a period under 0.4 s, `Sword.HalfTimeBelow`) | every other beat (the grid mode's half-time pulse) | under 0.8 s (0.69 s at 174 BPM) |

- Everything else is the grid mode's sword ([The sword](unity-game.md#the-sword)): heavy spins on accents, a slam on a
  zone change's downbeat, Amount's follow-ups, the aim at the crowd, wind-ups planned ahead from the lookahead.
- **On the beats, not an estimate.** A strike after its beat's first (a half beat, a follow-up, a half-time pulse's
  subdivision) lands a share of the actual beat interval it falls in. The grid mode spreads them evenly over an
  estimated interval, which a tracked beat's jitter sets 5-10 ms off the skipped beat. Committed beats are revealed
  about 0.76 s ahead (median), often after a strike is planned, so a strike planned on the estimate moves onto the
  beats at its wind-up when they are known by then.
- With no beat known ahead, the sword keeps the grid mode's free pulse (every 1.35 s at rank 1), off any grid, so the
  hero still fights through a beatless intro.
- Real songs under 80 BPM are mostly tracked at double time (house-128 slowed to 70 BPM tracks at 141), so they
  strike on the beat the tracker hears, every 0.43 s; the half-beat rule catches the ones tracked slow.

### Loudness drives power

The tick's loudness level at a strike beat, known when its strikes are planned:

| Level | Reach and damage | Strikes |
|---|---|---|
| quiet | 0.75x (`Sword.QuietScale`) | as ever |
| medium | 1x | as ever |
| loud | 1.25x (`Sword.LoudScale`) | one more per beat (or half beat), on its eighth (sixteenth) |

- The slash is drawn at the attack event's reach, so a quiet passage swings visibly smaller.
- On a loud beat an accent may be heavy though the strike beat before was heavy too, so a drop's run of accents spins
  every time.

### The horde reels

`Horde.Reels`:

- **Heavy blows throw further**: a heavy strike's and its cleave's knockback, and the role's cap on it, times 2.5
  (`Horde.ReelKnockScale`): 2.8 m for most roles, from 1.1 m.
- **A zone change staggers the horde**: on the zone change's downbeat every enemy within a view's width of the hero
  (but a set piece's) is shoved straight back (a blow of knockback power 3, `Horde.StaggerPower`) and staggered for
  the beat after, neither noticing the hero nor stepping; pursuit resumes the beat after that. The sword's slam lands
  on the same downbeat.
- **The crowd bobs on the kick** (presentation, `CrowdView.Kick`, from the look's fast lane): every monster swells
  up to 14% and hops 12% of its height with the kick's envelope.

## Finishers

`--finishers` (zen only) adds one sparse, optional rhythm verb on top of zen's unjudged walking and striking: every
phrase, a ring closes on the hero toward the phrase's downbeat, and Space in its window fires the screen's biggest
move, a slam that clears the view. A miss costs nothing. So the rhythm sits on a single big, well-timed moment every
8-15 s, not on the frequent verbs (grid hops looked jerky in 3D, free mode's Z flailed), and every moment still looks
good.

```sh
just game --mode zen --finishers --file "$PWD/public/reactive/house-128/house-128.ogg"
```

The sim's `Finishers` (`game/Assets/Game/Sim/Hero/Finishers.cs`) and the sword's `Sword.Finisher`; the client's
`FinisherRings` and the look's `FeelMoments`.

### Cadence

- **Phrases** count beats from the beat's latest lock, as bars do: a ring every 16 beats (four bars of four), or every
  32 where 16 would come under 7 s apart (`Finishers.PhraseMinS`: over 137 BPM). So rings come 7-14 s apart from 69 BPM
  up: 13.7 s at 70 BPM, 7.5 s at 128, 11 s at 174 (eight bars).
- **Causal.** The target downbeat is predicted from the beats revealed now: the latest's time plus its beats on at the
  mean of the latest four beat intervals (its own time once it is revealed). Committed beats are revealed only about
  0.76 s ahead, less than a telegraph, so a ring always starts on a prediction. It never uses accents or section
  changes, which a song the game hasn't heard doesn't know ahead.
- **Retimes.** Until its window closes the ring takes, each tick, the beat nearest the downbeat it has promised; when
  that moves it (by more than 1 ms, or at all once the beat is revealed), a `Retime` event carries the new downbeat
  and the ring eases onto it over 0.15 s. A steady beat keeps the phrase's beat (house-128: 10 ms at most). A tracker
  that drops or doubles beats moves the ring by less than a beat, to the beat nearest its promise, off the phrase
  count (dnb-174, whose committed beats flip between 0.34 and 0.69 s apart: 165-177 ms).

### The ring

- It closes over the 4 beats before the downbeat (2 under 96 BPM, where 4 would last over 2.5 s): 1.88 s at 128 BPM.
  Its arrival is the beat.
- Drawn in the world, never as HUD text: a band of crystal light on the ground at the hero's feet, closing from 4.5 m
  to 0.9 m and brightening as it closes, over a faint ring where it will land, flaring through its window
  (`FinisherRings`, on the sword's crystal-wave shader).
- A downbeat found with less than three quarters of its telegraph left (after a hold, a lock, the beat turning
  reliable, or a prediction jumping) gets no ring.
- **No rings** while the world is frozen (cards, chests, pause, the music stopped), the hero has fallen, or the beat
  isn't reliable (the gate) when it would start. A ring in flight when a hold starts is dropped.

### The press

- **Space**, the dash's key too (ps-zz4; zen needs no stomp). With both flags on, a press in a ring's window is the
  finisher, not a dash: the sim hands Space to the finishers first, and only a press they don't take goes on.
- **Judged in heard time** against the ring's downbeat: a press heard within 100 ms of it (`Finishers.WindowS`,
  BeatGrade's 70 ms full window and a little more) fires. A live key reaches the sim a lead after it was heard (about
  90 ms on speakers, 0.2-0.35 s on Bluetooth), so the ring stays open for presses heard in its window for 0.5 s after
  it (`Sword.PressBackS`) before it ends as a miss.
- **A press outside the window**, or none, does nothing: no penalty, no meter, and the ring goes on taking a press in
  its window. (So mashing Space hits every ring; see the open questions.)

### The slam

- **At once, in heard time**, as free mode's presses: one heavy sword strike that winds up from the press and lands
  1/30 s after it (`Sword.PressImpactS`), resolved against the world as it was then, its events stamped at their own
  times ([Attacking on the beat](#attacking-on-the-beat)). The beat's own strikes go on around it.
- **It clears the view**: every enemy within 16 m of the hero (`Sword.FinisherReachM`; the view's nearer half-width is
  18 m) takes 10 times the sword's power (`Sword.FinisherDamage` times a heavy's 2.5), which kills the ordinary crowd
  of the first ten minutes; the rest are thrown (a heavy blow's knockback, 2.5x in zen). Its cleave wave rolls on 5.9 m
  beyond, and it breaks the structures it and its wave reach. Deaths land on the impact frame, no build-up.
- **Presented** as the sword's slam: the knight's slam clip on the press (the press's hit-stop, held 50 ms at 0.25x),
  the slam's shock sized to the reach and its white-hot ring out to 16 m, then the cleave wave; and the look's
  strongest moment (`FeelMoments.Kind.Finisher`): a full flash, twice a structure break's punch and the breaks' cap of
  shake (0.4 m). The ring goes on the impact.

Evidence: [game/evidence/zen-finishers](../../game/evidence/zen-finishers/README.md).

## The dash

`--dash` (a free hero's: zen is the target, free mode takes it too, untuned) adds a second sparse, judged verb next to
the finishers: a dash on Space. The frequent verbs stay unjudged; dodging is the genre's real skill, and a lunge reads
well as one action, on the beat or off it.

```sh
just game --mode zen --dash --file "$PWD/public/reactive/house-128/house-128.ogg"
```

The sim's `Dash` (`game/Assets/Game/Sim/Hero/Dash.cs`), the free hero's `FreeHero.Dash` and the sword's
`Sword.Lunge` and `Sword.SpinAfter`; the client's `HeroMotion` (the pose), `FeelMoments` and `SoundPresenter`.

### The key

**Space**, which never stomps with `--dash` (zen needs no stomp); Enter still answers cards, and Space still answers
them while they are open. With `--finishers` too, a press in a ring's window is the finisher and not a dash; any other
press dashes.

### On and off the beat

Binary, not graded, so "on the beat" reads clearly, and never a dead press:

- **On the beat**: heard within 70 ms of a beat (BeatGrade's full window, `Dash.WindowS`; half beats don't count;
  `BeatGrade.OnBeat`). A long lunge, 4.5 m (three tiles) over 0.15 s (`Dash.LongM`, `LongS`). Each enemy whose body
  comes within 1.2 m of the hero on the way (`Dash.ReachM`) is struck once, for a heavy strike's damage (the sword's
  power x 2.5, `WeaponHit.Dash`), and thrown aside of the path to its own side (the rank's knockback, 2.5x in zen
  as heavy blows are). **The sword's next beat strike landing after it is a heavy spin** (`Sword.SpinAfter`): lunge
  in on the beat, land, spin out on the next. Once per dash; a zone change's slam still wins. (On the attack key, in
  free mode, the next press is heavy.)
- **Off the beat**: a short hop, 1.5 m (a tile) over 0.1 s (`Dash.ShortM`, `ShortS`), striking nothing, no spin.
- **No reliable beat** (no beat ahead, under the gate, unclear music): every dash is the lunge, as BeatGrade grades
  every press full there.
- **Either way the hero can't be hurt** from the dash's start to 0.1 s after it lands (`Dash.AfterS`,
  `HeroHealth.Shield`).
- Nothing else: no groove meter, nothing punishes not dashing.

### Moving

- **Direction**: the held arrows (eight ways); with none, toward the pointer's target; else the walk's latest
  direction; standing, the facing (`FreeHero.DashDirection`). A target stays set: the hero walks on to it after.
- **Walls**: the free walk's box rules (moved one axis then the other), so a dash stops at a blocked tile or the
  arena's edge, sliding along it as the walk does.
- **At once, in heard time**, as free mode's presses: the dash starts when its key was heard (never before a hold nor
  more than 0.5 s back), the hero's path from then walked again with the dash, so the drawn knight leaves its spot on
  the key-down frame; the lunge's blows the sim has already passed resolve as it takes the key, each at its own time
  against the world as it was then ([Attacking on the beat](#attacking-on-the-beat)). A hit the hero took in between
  stands (nothing simulated is revised).

### Rate

At most one dash per zen strike period: a beat, two on the half-time pulse (over 150 BPM), half of one on the beat
grid's slow songs (under 80 BPM), less twice the window, so two presses each on its own beat both dash (0.36 s at
128 BPM's 0.469 s, 0.55 s at 174's pulse). A press sooner waits for the pulse and dashes then, judged by when it was
heard (so mashing gets hops), the latest such press replacing an earlier (a finisher's press too, so no dash follows
the slam): mashing never runs faster than the music's pulse, and no press shows nothing. Frozen (cards, chests, pause, the music stopped) or fallen, nothing dashes, and a
press waiting is dropped.

### Presented

In-world only (trailer style):

- **The pose** (`HeroMotion`, from a `HeroDash` event and the glides the sim settles): from the dash's start the
  knight faces it at once, untwisted, its legs held in a long stride rather than cycling at the dash's 30 m/s, lifted
  in a low arc: the lunge skims (0.1 m) with its blade alight, the hop jumps (0.3 m). Over 0.12 s after it lands the
  walk's (and a swing's twist) take back over. There is no lunge clip in the Synty packs, and no velocity trail yet
  (ps-wnj.15's VFX would carry one).
- **The landing**: a lunge lands with a camera punch (0.04), a little flash and shake (`FeelMoments.Kind.Dash`), in
  place of a hit-stop; its struck enemies flash and are thrown as any blow's.
- **Sound**: the lunge whooshes with the heavy swing from its start and lands on the slam's thud, quieter and higher
  (0.45); the hop whooshes with the light swing, quieter. A dash the sim takes late sounds as soon as the mixer can,
  as a press's swing does.

Evidence: [game/evidence/zen-dash](../../game/evidence/zen-dash/README.md).

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
- Zen: does a slash on every beat read as dancing or as a metronome? Is the loud passages' extra eighth strike
  power or clutter? Does the kick bob read as the horde grooving or as jitter, and the zone change's stagger as a
  beat drop or a hiccup? Most songs are loud nearly throughout (house-128's ticks are 96% loud), so is loudness's
  power-up noticeable, or should it follow energy instead of the three levels?
- Finishers: does a ring closing over four beats read as an invitation, and its arrival as the beat? Is 100 ms either
  side generous enough with live latency, or too generous? A press outside the window costs nothing, so mashing Space
  hits every ring: should an early press spend the ring (as osu! counts a miss), or is "never punished" the point? Is
  a slam every 7.5 s at 128 BPM too often for the screen's biggest move, and 13.7 s at 70 BPM too rare? On a tracker
  that flips between full and half time (dnb-174), rings come irregularly (6.5-29 s apart) and move up to half a
  beat: is that acceptable, or should phrases count in time rather than in committed beats?
- Dash: does on-the-beat-or-not read with only two outcomes, or does 70 ms on beats alone feel stingy (half beats
  don't count, so most mashed presses hop)? Is the lunge's spin on the next beat felt as the player's move, or lost
  among the sword's own spins? Does the held stride read as a lunge without a clip of its own, and the hop as a hop?
  Is a dash per beat too often for a dodge, or should it ride a longer cooldown? In zen the beat strikes clear 7-8.8 m
  round the hero, so the lunge's 1.2 m blows rarely find anyone (none in the evidence's runs): should the lunge reach
  wider or further, or is it a dodge first and its blows a bonus for the crowds that do close in?
