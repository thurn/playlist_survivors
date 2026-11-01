# Every moment on its frame (ps-msz.8.5)

The game's promise is that every moving thing lands on the music ([unity-game.md, Time and
Sync](../../../docs/design/unity-game.md#time-and-sync)). M2 and M3 proved each presenter alone; this
audits them together, at the run's peak and over an early stretch, and measures the key rules' late steps.

## The on-the-beat audit

`game beat NAME --file F` (demo3d/tools beat.py) runs the player windowless at fixed time with
`--moments-out`: each presenter marks a key moment on the frame it first shows it, with the moment's own
song time (game/Assets/Game/Client/Loop/Moments.cs), and `--events-out` keeps every sim event with the
frameSong of the frame it settled in. A moment is on its frame when that frame is the first at or after its
time (`prev < t <= song`). The marks sit where each presenter shows the moment: the hero's hop setting off
(press or held stride) and landing (HeroMotion), the blade's flare on each impact, each monster's footplant
as its pose reaches it (MonsterTrack), each death seeding its GPU shatter (MonsterShatter), structure breaks
and regrowths (StructureShatter), every particle effect as the GPU emits it (EffectSystem.Update: the
sword's slashes, death bursts, hit flashes, the weapons' casts and hits, chest bursts), the beat gem's ring
landing and each card's flip coming to rest.

On jarvis (Apple silicon), IL2CPP, from the worktree's root:

```sh
F=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
P="--start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@18,regrow@24,finale@32"
uv run --project demo3d/tools game beat peak --file $F --seconds 40 --player-args "$P"            # 10 s
uv run --project demo3d/tools game beat early --file $F --seconds 90 --player-args "--run --bot positioning"  # 14 s
uv run --project demo3d/tools game beat peak-sounds --sounds --file $F --seconds 40 --player-args "$P"  # 43 s, real time
uv run --project demo3d/tools game beat ci --file public/reactive/house-128/house-128.ogg --seconds 20 --backend mono --no-evidence \
  --player-args "--start-minute 25 --bot positioning --debug evolve:bloody-tear@3,evolve:soul-eater@3,break@8,regrow@12,finale@15"  # slow CI's, 6 s
```

The peak is minute 25 with the three evolutions, a break, a regrowth and the finale's blast at 32.4 s (292
deaths at once); the early stretch is the run's first 87 s from its start, with four level-ups. Results:
[peak.json](peak.json), [early.json](early.json), [peak-sounds.json](peak-sounds.json).

| moment | peak: checked | on its frame | early: checked | on its frame |
|---|---|---|---|---|
| HeroPress (press strides setting off) | 53 | 53 | 110 | 110 |
| HeroStride (held strides setting off) | 49 | 49 | 144 | 144 |
| HeroLand (hops touching down) | 102 | 102 | 254 | 254 |
| Blade (flare on a sword impact) | 101 | 101 | 149 | 149 |
| Footfall (monster footplants, those posed) | 4706 of 5347 | 4706 | 3442 of 3533 | 3442 |
| Death (shatter seeded) | 292 | 292 | 282 | 282 |
| Break | 4 | 4 | 10 | 10 |
| Regrow | 2 | 2 | 6 | 6 |
| Effect (every particle effect) | 5226 | 5226 | 735 | 735 |
| GemRing (the ring landing on a reliable beat) | 49 | 49 | 132 | 132 |
| CardFlip | 0 (no level-up) | | 11 of 12 (the last offer's third card fell after the run) | 11 |

Every event settled at least 66.7 ms (four frames) before its moment (`settled_ahead`; held strides 66.7,
sword impacts 68.3 at the least, footplants 166.7), so nothing reached a presenter too late to show.
The footfalls are a sample: only monsters on a crowd slot are posed (CrowdView), and the rest of a step's
footplants fall on monsters off screen or past a cap. The bot's keys are heard at the sim's own time, so the
hero's moments here carry no lead; the lead's effect on keys is measured in .NET below.

### What it found, and the fixes

Before the fixes the peak had 242 of 10,584 moments a frame or two late:

| moment | late | cause |
|---|---|---|
| Effect: prism-hit 71, hit-flash 64, crystal-burst 8, slash 3, prism-cast 2, strike-sparks 1 | 149 | a rounding: n/60 falls short of the sim's tick at the same time |
| Footfall 34, HeroPress 14, Death 8, HeroLand 2, HeroStride 1 | 59 | the same rounding |
| Effect: garlic-pulse 29, prism-cast 5, structure-burst 1 | 34 | an ordering: views asked for effects after the effects had emitted the frame |

- **The rounding** (fixed time only, so every still, filmstrip and trailer frame): frame n shows song n/60,
  which falls a rounding short of a tick at the same time half the time (909/60 is 15.149999999999999 against
  the sim's 15.15), and the moments the sim times on that tick (deaths, hits, footplants, presses) showed a
  frame late. FrameClock's frameSong is now the frame's song time and a microsecond (`FrameClock.Reached`);
  the frontier, and so the sim and its hash, are unchanged. Test: LoopTests.AMomentOnAFramesTickShowsOnThatFrame.
- **The ordering**: the effects presented before the views that ask for effects as they present (the
  garlic's motes, Bible's ring casts, a chest's bursts), so those were emitted a frame late (the effect's
  clock still ran from its time, so the first frame showed it a frame old). The effects now present after
  every view (GameLoop).

[garlic-before.jpg](garlic-before.jpg) and [garlic-after.jpg](garlic-after.jpg) show Soul Eater's pulse at 7.32 s: its
motes ring the aura from 7.350 before, from 7.333 (the pulse's frame) after. [deaths-before.jpg](deaths-before.jpg) and
[deaths-after.jpg](deaths-after.jpg) show the deaths, hit flashes and garlic pulse at 15.150: before, they showed on
15.167; after, on 15.150.

```sh
PA="--file $PWD/$F $P"
uv run --project demo3d/tools game look garlic-after --evidence beat --at 7.3 --frames 4 --strip 4 --crop 560,240,800,600 --player-args "$PA"
uv run --project demo3d/tools game look deaths-after --evidence beat --at 15.1166667 --frames 4 --strip 4 --crop 360,140,1200,800 --player-args "$PA"
```

After those, one moment stayed late: **the gem's ring** landed 2.4 frames after the song's third beat (1.06 s),
on the frame the beat locked (1.10 s), with the gem's flash and the beat track's heart. A beat the lock came
after now shows as gone by (HudPresenter): no landing, no flash. The runs above are with every fix.

### The audit fails on a late moment (ps-msz.8.13)

The Warden checked the audit catches what it is for, in a scratch build (not committed) of slow CI's run
(`game beat ci` above): structure breaks, regrowths and glints taken a frame late in StructureShatter
(`_pending[0].T <= song - 1.0 / 60`), and the effects subscribed before the views again (GameLoop). It
failed (exit 1) with 39 of 6,219 moments off their frame: all 14 breaks (+1.2 to +1.5 frames) and 5
regrowths (+1.8), and 20 effects (garlic-pulse 12, structure-burst 6, prism-cast 2, +1.8 frames). The same
run without them: 0 off, ok.

It also checks coverage now: a moment shown once per sim event (HeroPress, HeroStride, Blade, Death, Break,
Regrow) shown fewer times than the sim made it fails, as does any kind the sim made that nothing showed
(footfalls, landings and gem rings are counted against an upper bound, so only none at all fails). Before,
a presenter that stopped showing or marking a moment passed as all on time.

## The sounds

`--sounds` runs the player in real time, headless, with the sound probe and `--sound-out` (as `game spike
--sounds` does) and `--events-out`: every scheduled sound's song time must be its event's, apart from those
the design snaps to the grid (the hurt and the gem chimes on the sixteenth, a shatter's tinkle as its shards
land), its DSP time its song time's on the music's anchor, and each probe click is found in Unity's own mix
against the music (spike.py's measurement).

At the peak: 945 sounds scheduled and 0 dropped; 725 on their event's song time (swings and slams on their
impacts, hits, deaths and shatters on theirs, the breaks' and regrowths' shatters on theirs, the garlic's pulses
and the lightning's strikes on theirs), 220 snapped by design, **0 off**; 0 off their anchor; the 438 probe clicks
apart from the rest all found in the mix, median +0.000 ms and p5 to p95 -0.021 to +0.000 ms from the music.
**The lead** held at the output latency plus the margin: median 91.4 ms, p5 to p95 90.3 to 92.1 (output latency
38.0 + margin 52.3; the audit allows 2 ms). A real-time run isn't frame-exact, so its counts vary a little from run
to run (an earlier one scheduled 888).

Since ps-msz.8.13 the probe has a 100 ms lead-in before its click (`SoundScheduler.ProbeLeadIn`, as long
as the longest real clip's), so it starts early by it as a clip with an onset does, or into it when the
mixer can't make all of it (ps-msz.8.6's trim). Retaken on mono: 934 scheduled, 703 of them into their
clip, 0 dropped, 0 off their event or anchor, all 350 probe clicks apart from the rest found on the music
(median -0.021 ms, p5 to p95 -0.021 to +0.000), lead 91.7 ms. Slow CI's `game spike ci --sounds`: 450 of
598 into their clip, 275 of 275 clicks found, within 0.021 ms. Unity's mixer honours `AudioSource.time`
under `PlayScheduled`, to the sample.

Reading the sounds' code found one off its event: a regrowth's eruption sound was asked for when its frame
showed it (GameLoop.Erupted), already past its song time, so the mixer couldn't make it and a windowed run
dropped it. It is now scheduled from the Regrowth event as it settles, a lead ahead, as a break's shatter is
(SoundPresenter; EffectsSoundTests.ARegrowthShattersAtItsStart...). Which sounds drop when the voices run out
at the peak is ps-msz.8.6's.

## Late steps

The key rules (unity-game.md, Key Presses; Sim/Hero/Hero.cs) take every key the lead after the player heard
it. Game.Sim's LateStepTests measures what the player sees against native's classic mode (a tap moves one tile
on its key-down frame, a turn turns at once) at 48 phases of the stride step, at the speakers' lead (90 ms) and
a Bluetooth lead (250 ms), at 100, 128 and 170 BPM. Keys reach the sim as the client gives them: at the start of
the first frame after they were heard (60 fps), at the previous frame's frontier, so a key's lag runs up to a
frame short of the lead with where in its frame it fell. Each run has sixteen earlier keys (Space).

```sh
dotnet test game/dotnet/PS.Game.Sim.Tests --filter LateStepTests --logger "console;verbosity=detailed"
```

Before (staging 06662887), the taps were fine, but:

| scenario | BPM | at once | placed | ghosts | latest (ms) |
|---|---|---|---|---|---|
| turn (Up held, Right pressed as Up is let go) | 100 / 128 / 170 | 96/96 | 93 / 93 / 95 of 96 | 3 / 3 / 1 | 0 |
| roll (Right pressed 30 ms before Up is let go) | 100 / 128 / 170 | 96/96 | 93 / 93 / 95 of 96 | 3 / 3 / 1 | 0 |
| double tap (Right, then Up 120 ms on) | 100 / 128 / 170, lead 90 | 19 / 25 / 17 of 48 | 48/48 | 0 | 286 / 222 / 336 |
| double tap | 100 / 128 / 170, lead 250 | 19 / 25 / 17 of 48 | 48/48 | 0 | 424 / 359 / 468 |

- **Ghost turns**: a turn heard just before a step, arriving a fraction of a frame later than the key before it
  (its lag longer by where in its frame it fell), found the step already judged with the old key held: an Up
  stride on it, then the Right press at once, so the hero hopped diagonally over two tiles. The held strides
  now wait for the longest lag of the last sixteen key changes, not the latest key's, and a press heard before
  the held stride on its step had set off takes that stride's place (the hero strides from where it stood when
  the press was heard), which closes the sub-frame window the longest lag leaves. A key heard exactly at a step's
  time counts for it however it arrives (the tick's judgement is now strict, as a key change's was).
- **Double taps**: one press stride per step made a quick second tap the other way (an L) wait for the next step
  no press took: up to 468 ms on a Bluetooth lead, in half the phases. Native's classic mode strides every press
  at once. A second press for a step a press took now strides at once when it turns (another way than that press)
  and the press's stride has drawn (110 ms): mashing one key, or faster than a stride, stays at the grid's pace
  (HeroTests.MashingStridesAtMostOncePerStep... passes unchanged).

After:

| scenario | BPM | lead (ms) | at once | latest (ms) | placed | ghosts | taken back | shortest stride (ms) | largest cut (ms) | held taken (ms) |
|---|---|---|---|---|---|---|---|---|---|---|
| tap | 100 | 90 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| turn | 100 | 90 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 104 | 83 |
| roll | 100 | 90 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 104 | 83 |
| double tap | 100 | 90 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| tap | 100 | 250 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| turn | 100 | 250 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 104 | 250 |
| roll | 100 | 250 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 104 | 250 |
| double tap | 100 | 250 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| tap | 128 | 90 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| turn | 128 | 90 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 105 | 90 |
| roll | 128 | 90 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 105 | 90 |
| double tap | 128 | 90 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| tap | 128 | 250 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| turn | 128 | 250 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 105 | 256 |
| roll | 128 | 250 | 96/96 | 0 | 96/96 | 0 | 0 | 110 | 105 | 256 |
| double tap | 128 | 250 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| tap | 170 | 90 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| turn | 170 | 90 | 96/96 | 0 | 96/96 | 0 | 1 | 110 | 103 | 90 |
| roll | 170 | 90 | 96/96 | 0 | 96/96 | 0 | 1 | 110 | 103 | 90 |
| double tap | 170 | 90 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| tap | 170 | 250 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |
| turn | 170 | 250 | 96/96 | 0 | 96/96 | 0 | 1 | 110 | 103 | 256 |
| roll | 170 | 250 | 96/96 | 0 | 96/96 | 0 | 1 | 110 | 103 | 256 |
| double tap | 170 | 250 | 48/48 | 0 | 48/48 | 0 | 0 | 110 | 0 |  |

Taps, turns, rolls and double taps all set off at once, drawn from their key-down, and end where classic mode
puts the hero. "Taken back" is the press replacing a held stride the sim had judged before it arrived: the
hero never kept it. A press's own stride is 110 ms at every phase, lead and tempo (none is compressed: every
grid period here is over 110 ms), and a press cuts short at most 105 ms of a held stride in progress, as in
classic mode, where a press interrupts the stride too.

**The cost**: a held stride is now taken the lead after its step (83 to 90 ms on the speakers, 250 to 256 on
Bluetooth, "held taken"), so it reaches the client about a display latency (one refresh, 16.7 ms) after its step,
where before it reached it 0 to 16.7 ms after. It draws as an on-time step does whose start fell just after the
frame before (the hop a sixth of the way up on its first frame), so it reads as a hop, not a jump; the takeoff is
at most a frame behind the beat. No frame can show a step on time and also know every key heard in the refresh
before it.

The sim's tests pass (323), and the bots' sweep with them (`game sweep --assert`: all four positioning runs win,
lowest health 0.70 to 1.00; the idle bot falls at 2:05). Sessions recorded before this change won't replay to their
hashes.
