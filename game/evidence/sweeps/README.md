# The bots' sweeps: four 30-minute .NET runs and the idle bot (ps-msz.6.13)

The positioning bot (native's `SurvivorBot`, ported in `game/Assets/Game/Sim/Bots/SurvivorBot.cs`)
and the idle bot play the run's level (`RunLevel.Config`: the schedule, the music-driven events and
the arsenal from the starting sword, in the game's arena, seed 1, the headless start) in .NET over
native's entry dumps of the survivors test playlist, `native/playlists/survivors-30.m3u`, as the
client drives the sim (`BotRun`: 60 fps frames, each entry arriving 60 ms after its stamp):

```sh
uv run --project demo3d/tools game sweep --assert      # slow CI's game section; --evidence writes summary.json here
```

The cases follow native's sweep (native/scripts/survivors_sweep.py): the playlist in its original,
reversed and rotated orders over each track's own dump laid end to end with the playlist's gaps
(`cached`), and its original order over one dump of the whole playlist (`continuous`). Each stream
is the playlist played twice: one pass can end before the run clock's 30 minutes (see the frozen
time below). The five runs go in parallel; warm, the sweep takes about 8 s.

[summary.json](summary.json), from 2026-10-31, every gate passing:

| case | run clock (s) | song (s) | started (s) | end | lowest health | hits taken | level at 5:00 | level | chests (first, s) | evolutions | frozen (s) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| cached-original-positioning | 1800 | 2059.1 | 3.8 | victory | 1.000 | 0 | 16 | 59 | 14 (208) | 5 | 252 |
| cached-reverse-positioning | 1800 | 2333.9 | 182.1 | victory | 0.868 | 3 | 16 | 65 | 15 (198) | 5 | 349 |
| cached-rotate-positioning | 1800 | 2054.7 | 3.8 | victory | 0.833 | 2 | 14 | 54 | 14 (64) | 5 | 247 |
| continuous-original-positioning | 1800 | 2354.4 | 3.8 | victory | 0.833 | 1 | 16 | 56 | 14 (110) | 4 | 548 |
| cached-original-idle | 126.4 | 143.9 | 3.8 | fell | 0 | 7 | - | 1 | 0 | 0 | 14 |

- **The gates** (unity-game.md, Tools, Checks and Evidence): each positioning run reaches its
  victory card with its health above 25% at every frame; the idle bot falls before minute 10, here
  at 2:06 of run clock, its sword alone against the first minutes' crowd. They are asserted.
- **Pacing, reported only**: the level at minute 5 is 14 to 16 (native's sweep: 15 to 17), the
  final level 54 to 65 (native's 74 to 79), and 14 or 15 chests from minute 1 to 29. Every
  chest's run-clock time is in summary.json. Bosses and level-scaled roles take the hero's level in
  health (since ps-msz.6.12), so they die later and the final levels sit below native's.
- **The positioning bot is rarely touched**: one run takes no hit in 30 minutes and the others one
  to three, never below 83% health, as native's own sweep keeps full health on three of its four.
  A grid hero striding a tile every stride step outruns the crowd, which travels only on main
  beats. That is the run's difficulty as it stands, not asserted either way.
- **Out-of-reach gems**: the rotated order once fell at 2:51, standing for 14 s on one tile, its
  goal a gem it could not reach, while the signature minotaur (725 health at level 9) slammed the mark under the
  hero. The bot now forgets a gem its route comes no nearer to (ps-b3v).
- **Frozen time**: each level-up's cards (about 2.4 s each, 115 to 140 s a run), each chest's
  roulette (37 to 50 s), the music stopping (70 to 147 s on cached tracks) and the waits for a
  resume's beat (18 to 25 s). On the continuous stream the music stops at every track change, the
  analyzer confirming each song's end into the quiet, about 92 s a pass of the playlist: 56 s of it
  as the piano aria begins, whose beat turns reliable only 56 s in, so the world holds under MUSIC
  STOPPED while it plays. On cached tracks the gaps carry no ticks at all and only some endings stop
  it. The reversed order opens on the aria (300 s long), whose beat first holds reliable for the
  start's 2 s at 182 s, so its run starts there. Hence the second pass of the playlist.

The bots' rules are pinned by game/Assets/Game/Tests/Sim/Bots/BotTests.cs (the escape, the gems
and the chests' pull, routing round a structure and out of a pocket, one stride a step, the cards,
the idle bot standing, and a run's determinism).
