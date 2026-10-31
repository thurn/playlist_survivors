# The music-driven events (ps-msz.6.3)

The run's swarms, set pieces and signature minotaur (game/Assets/Game/Sim/Enemies/MusicEvents.cs,
unity-game.md's Music-Driven Events) and the zones' runtime lighting
(game/Assets/Game/Client/Arena/ZoneLighting.cs).

## Filmstrips

Windowless stills of the Mac player (Mono) in the run (`--run`) on house-128, each event forced
at 8 s by its debug command so it starts on the next main beat, whatever the run clock (the
natural ones wait for 90 s to 5 minutes of it and for their music):

```sh
uv run --project demo3d/tools game look SLUG --backend mono --at T1,...,T6 --strip 3 \
  --evidence music-events --player-args "--run --file public/reactive/house-128/house-128.ogg --debug NAME@8"
```

Most run in the run's level (`--run`: the schedule's minute-0 bats and the starting sword); the
ring, stampede and minotaur run in the cast's level (`--cast bat1`: no sword, no waves), because
the sword's heavy strikes cleave 15 m and shattered the ring and stampede as they came into view.
The world is pink because house-128's opening jump moved the zones on to blossom by 6.6 s.

- [zone-change.jpg](zone-change.jpg) (`change@8`), 8.4 to 11.4 s: the forced section change lands
  on the downbeat at 8.9 s and the arena eases from blossom to snow over the next two bars, the
  moon, ambient, fog, ground tint and mist with the post's grade, settled by 10.8 s.
- [stampede.jpg](stampede.jpg) (`stampede@8`), 9 to 17 s: forty goblins in two rows charge in from
  beyond the right edge at 12 s and cross to the left by 17 s, a gap in the middle; the crystal
  structures on the right break up the rows where the walkers stop short of them.
- [kick-line.jpg](kick-line.jpg) (`kick@8`), 8.6 to 14 s: twelve ember skeletons march in from
  ahead of the hero (facing down) over 8 beats, stand in a line across its heading, kick in turn,
  and the sword's heavy strike shatters the line at 12.6 s.
- [wedge.jpg](wedge.jpg) (`wedge@8`), 8.6 to 12.4 s: nine bees fly in as a V from ahead over 4
  beats, and the sword breaks them as they close.
- [ring.jpg](ring.jpg) (`ring@8`), 9 to 15 s: forty goblins hold beyond the view for 4 beats, then
  close on where the hero stood over 8 beats, three exits in the ring, closed by 15 s.
- [bats.jpg](bats.jpg) and [ghosts.jpg](ghosts.jpg) (`bats@8`, `ghosts@8`), 9.8 to 11.5 s: a
  flock of 50 bats, and of 20 ghosts, enters from beyond the left edge and flies through the hero,
  a step each main beat; the sword shatters the ones that reach it.
- [flowers.jpg](flowers.jpg) (`flowers@8`), 8.6 to 18.5 s: a hundred flowers ring the hero beyond
  the view and close to 9 m over 16 beats, a 60-degree exit at the top right.
- [minotaur.jpg](minotaur.jpg) (`minotaur@8`), 8.6 to 12.6 s: the minotaur (the troll at 2.5x, in
  blue crystal) lands just inside the view on the beat at 8.43 s, walks in, telegraphs two beats
  before its slam, and the slam lands on the 8th beat (12.18 s) at the mark under the standing
  hero: the hurt flash at 12.25 s.

## A 30-minute run

[Run30](Run30/Program.cs) drives the run's level (`RunLevel.Config`: the schedule, the events and
the arsenal from the starting sword) over the survivors-30 dump as the client does, in the game's arena, the hero
standing:

```sh
dotnet run --project game/evidence/music-events/Run30 -c Release -- DUMP.jsonl game/evidence/music-events
```

[events.csv](events.csv) has every set piece and zone change with its run clock, song time,
members, and its cause's time and known-at. The summary, from 2026-10-31:

```text
song 2172.2 s, run clock 1800.0 s, phase Ended, end Victory
set pieces: BatSwarm 49, GhostSwarm 50, FlowerWall 9, Ring 21, Signature 1
most alive 300 (cap 300), 4478 left out of sight, minotaur slams 13 (14 slam hits on the hero, its and the bosses')
```

- **The swarms land with their flocks**: 47 bat swarms of 50 and two of 49, every ghost swarm's 20
  and every flower wall's 100. Before the Warden's fix (ps-msz.6.16) the minotaur's group, once it
  had gone, took over the next swarm to reuse it and orphaned its flock, still scripted and holding
  cap slots for the rest of the run, so later swarms found no room: all 19 ghost swarms came as one
  ghost, and six of seven flower walls as one flower.
- **Never above 300**: the events make room under the cap (ordinary enemies far off leave), so the
  most alive in any frame is the cap.
- **The swarms** land on the first reliable main beat at or after their minute's jump where it has
  one (minute 7's at 526.15 s song, its jump at 524.50 s, known 1.45 s ahead; the dump's beats
  between were unreliable) and late in the minute otherwise; minute 13's ghosts and minute 20's and 29's
  bats come in their dozens of repeats, as the schedule has them.
- **The minotaur** lands once, at run clock 91.2 s (the first reliable main beat after 90 s),
  and slams on its 8th beats until its retreat 90 s later; slams on unreliable beats fizzle.
- **The set pieces are all rings** on this playlist: its analysis reads it loud most of the time
  (34,207 of 45,880 ticks loud, 8,136 medium), so the 2 s mean level stays above 1.5 and the
  kick line's 16 medium beats in a row and the wedge's quiet never come, nor 8 s of quiet before a
  jump. Native's rules read the same levels the same way; the tests pin the other three under
  their own music.

## Tests

game/Assets/Game/Tests/Sim/Enemies/MusicEventsTests.cs (.NET) pins the rules: a minute's swarm
lands on the beat of its first big jump (not a smaller one before it), its repeats every 5 s of
run clock, and late in the minute without one; the stampede charges in on a jump after quiet and
not after loud music; loud, quiet and medium passages bring the ring, the wedge and the kick line
from their start times, one at a time, the ring and wedge on 16-beat boundaries; the ring closes
to its radius; the minotaur lands on the beat at 90 s and slams on every 8th main beat at the mark
under the hero, hitting it while it stands there; nothing comes outside a run; every debug command
starts its event on the next main beat and nothing stays scripted or vanishes in sight; and an
event makes room from the farthest ordinary enemies. LookTests (EditMode) pins the zone lighting's
ease: the palette from the downbeat over two bars, and a change mid-ease starting from where it is.
