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

[summary.json](summary.json), from 2026-10-31 after ps-b3v (the bot forgets an out-of-reach gem) and
ps-msz.6.14 (a beatless track's thaw after a stop, and the bot's step off a telegraphed slam), every
gate passing:

| case | run clock (s) | song (s) | started (s) | end | lowest health | hits taken | level at 5:00 | level | chests (first, run clock s) | evolutions | frozen (s) |
|---|---|---|---|---|---|---|---|---|---|---|---|
| cached-original-positioning | 1800 | 2069.7 | 3.8 | victory | 0.860 | 1 | 17 | 65 | 11 (561) | 5 | 262 |
| cached-reverse-positioning | 1800 | 2279.6 | 182.1 | victory | 1.000 | 0 | 15 | 64 | 15 (517) | 4 | 294 |
| cached-rotate-positioning | 1800 | 2065.5 | 3.8 | victory | 0.868 | 1 | 14 | 57 | 14 (64) | 5 | 257 |
| continuous-original-positioning | 1800 | 2123.5 | 3.8 | victory | 0.969 | 1 | 16 | 57 | 12 (477) | 4 | 316 |
| cached-original-idle | 126.4 | 143.9 | 3.8 | fell | 0 | 7 | - | 1 | 0 | 0 | 14 |

- **The gates** (unity-game.md, Tools, Checks and Evidence): each positioning run reaches its
  victory card with its health above 25% at every frame; the idle bot falls before minute 10, here
  at 2:06 of run clock, its sword alone against the first minutes' crowd. They are asserted.
- **Pacing, reported only**: the level at minute 5 is 14 to 17 (native's sweep: 15 to 17; the
  design's 10), the final level 57 to 65 (native's 74 to 79), and 11 to 15 chests. Every chest's
  run clock time is in summary.json. Bosses and level-scaled roles take the hero's level in health
  (since ps-msz.6.12), so they die later and the final levels sit below native's.
- **The positioning bot is rarely touched**: one run takes no hit in 30 minutes and the others one
  each, never below 86% health, as native's own sweep keeps full health on three of its four. A
  grid hero striding a tile every stride step outruns the crowd, which travels only on main beats.
  That is the run's difficulty as it stands, not asserted either way.
- **Standing still under the minotaur**: the rotated order once fell at 2:51, standing for 14 s on
  one tile while the signature minotaur (725 health at level 9) slammed the mark under the hero,
  which is where the hero stood as it telegraphed. Two changes keep it moving: the bot forgets a gem
  its route comes no nearer to, which it had stood for (ps-b3v), and it steps off any telegraphed
  slam whose blow would reach it before the blow lands (ps-msz.6.14).
- **Frozen time** (257 to 316 s): each level-up's cards (about 2.4 s each, 121 to 140 s a run),
  each chest's roulette (32 to 45 s), the music stopping (70 to 130 s) and the waits for a resume's
  beat (19 to 26 s). On the continuous stream the music stops at every track change, the analyzer
  confirming each song's end into the quiet. The piano aria's stop used to last 56 s, its beat
  turning reliable only 56 s in: since ps-msz.6.14 a stop lets go once the music has played 8 s
  without a lock, which took the continuous run's song from 2354 s to 2124 s. On cached tracks the
  gaps carry no ticks at all and only some endings stop it. The reversed order opens on the aria
  (300 s long), whose beat first holds reliable for the start's 2 s at 182 s, so its run starts
  there and needs 2280 s of the playlist's 2294: hence the second pass.

The bots' rules are pinned by game/Assets/Game/Tests/Sim/Bots/BotTests.cs (the escape, the gems
and the chests' pull, routing round a structure and out of a pocket, off a slam's mark, one stride a step, the cards,
the idle bot standing, and a run's determinism).
