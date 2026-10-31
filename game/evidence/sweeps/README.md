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

[summary.json](summary.json), from 2026-10-31 after ps-msz.6.14 (a beatless track's thaw after a stop, and
the bot's step off a telegraphed slam), every gate passing:

| case | run clock (s) | song (s) | started (s) | end | lowest health | hits taken | level at 5:00 | level | chests (first, song s) | evolutions | frozen (s) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| cached-original-positioning | 1800 | 2042.6 | 3.8 | victory | 1.000 | 0 | 17 | 51 | 13 (561) | 4 | 236 |
| cached-reverse-positioning | 1800 | 2268.3 | 182.1 | victory | 1.000 | 1 | 15 | 61 | 12 (621) | 4 | 283 |
| cached-rotate-positioning | 1800 | 2059.7 | 3.8 | victory | 0.868 | 1 | 14 | 57 | 14 (64) | 5 | 253 |
| continuous-original-positioning | 1800 | 2091.3 | 3.8 | victory | 0.868 | 1 | 16 | 43 | 12 (477) | 4 | 284 |
| cached-original-idle | 126.4 | 143.9 | 3.8 | fell | 0 | 7 | - | 1 | 0 | 0 | 14 |

- **The gates** (unity-game.md, Tools, Checks and Evidence): each positioning run reaches its
  victory card with its health above 25% at every frame; the idle bot falls before minute 10, here
  at 2:06 of run clock, its sword alone against the first minutes' crowd. They are asserted.
- **Pacing, reported only**: the level at minute 5 is 14 to 17 (native's sweep: 15 to 17; the
  design's 10), the final level 43 to 61 (native's 74 to 79), and 12 to 14 chests. Every chest's
  song time is in summary.json.
- **The positioning bot is almost never touched**: two runs take no hit in 30 minutes and the
  others one each, a slam (24 of its 144 health). A grid hero striding a tile every stride step
  outruns the crowd, which travels only on main beats. That is the run's difficulty as it stands,
  not asserted either way. Before the bot stepped off slams, the rotated run fell at 2:51: standing
  on a gem with nothing within its 3.3 m safety reach, it took four of the signature minotaur's
  slams, which land where the hero stood, in 7 s.
- **Frozen time** (236 to 284 s): each level-up's cards (about 2.4 s each, 90 to 143 s a run),
  each chest's roulette (32 to 42 s), the music stopping (70 to 138 s) and the waits for a resume's
  beat (13 to 22 s). On the continuous stream the music stops at every track change, the analyzer
  confirming each song's end into the quiet; each stop now lasts 7 to 13 s. The piano aria's used
  to last 56 s, its beat turning reliable only 56 s in: since ps-msz.6.14 a stop lets go once the
  music has played 8 s without a lock, which took the continuous run's song from 2382 s to 2091 s.
  On cached tracks the gaps carry no ticks at all and only some endings stop it. The reversed
  order opens on the aria (300 s long), whose beat first holds reliable for the start's 2 s at
  182 s, so its run starts there and needs 2268 s of the playlist's 2294: hence the second pass.

The bots' rules are pinned by game/Assets/Game/Tests/Sim/Bots/BotTests.cs (the escape, the gems
and the chests' pull, routing round a structure and out of a pocket, off a slam's mark, one stride a step, the cards,
the idle bot standing, and a run's determinism).
