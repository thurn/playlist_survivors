# The bots' sweeps: four 30-minute .NET runs and the idle bot (ps-msz.6.13, ps-msz.8.4, ps-6bl)

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

[summary.json](summary.json), retaken 2026-10-31 for ps-6bl on staging 6bc54258 with the bot dodging every telegraph
(below): ps-msz.8.4's tuning (the run's pacing tuned to `startLevels`, the late crowd's rosters mixed, the waves'
short pass taking turns round the roster, chests landing only where the hero can walk), the later sim changes
(ps-msz.8.6's blast through `Horde.Damage`, ps-msz.8.5's late steps, ps-msz.8.7's `RunRecap`) and the sword's
half-time pulse above 150 BPM (ps-at1, ps-avv). Every gate passes:

| case | run clock (s) | song (s) | started (s) | end | lowest health | hits taken | level at 5, 10, 15, 20, 25, 30 min | chests | evolutions | frozen (s) |
|---|---|---|---|---|---|---|---|---|---|---|
| cached-original-positioning | 1800 | 2013.1 | 3.8 | victory | 0.948 | 3 | 10, 14, 18, 23, 26, 34 | 14 | 3 | 206 |
| cached-reverse-positioning | 1800 | 2184.2 | 182.1 | victory | 0.868 | 1 | 10, 14, 21, 23, 28, 33 | 12 | 3 | 199 |
| cached-rotate-positioning | 1800 | 1995.7 | 3.8 | victory | 1.000 | 3 | 10, 14, 18, 23, 25, 30 | 15 | 3 | 188 |
| continuous-original-positioning | 1800 | 2081.9 | 3.8 | victory | 1.000 | 0 | 10, 14, 19, 23, 27, 33 | 17 | 3 | 274 |
| cached-original-idle | 125.2 | 142.7 | 3.8 | fell | 0 | 7 | - | 0 | 0 | 14 |

- **The gates** (unity-game.md, Tools, Checks and Evidence): each positioning run reaches its
  victory card with its health above 25% at every frame; the idle bot falls before minute 10, here
  at 2:05 of run clock, its sword alone against the first minutes' crowd. They are asserted.
- **Pacing, reported only**: the design's levels (progression.json's `startLevels`) are 10, 15, 19,
  23, 27 and 30 at minutes 5 to 30; every run is within 20% of each (the sweep marks a level outside
  it with `!`); ps-msz.8.4's own take stood at 10 to 11 at 5:00 and 31 to 34 at 30:00, this one at 10 and 30 to 34. Before ps-msz.8.4 the runs stood at 14 to 17 at 5:00, 33 to 36 at 15:00 and 57 to 65
  at 30:00 on native's curve; the game's steeper curve (1.9 x level^2.8) slows them. Fewer levels
  mean fewer top-rank weapons, so three evolutions where there were four or five.
- **Boss chests**: each run took minutes 10, 15, 20 and 25's chests, each 0.7 to 4.0 s after it fell
  (every chest: 0.5 to 5.7 s); summary.json gives each chest's drop and take on the run clock. The
  delay is the crowd clearing round the hero (2 m) and the bot's walk; chests come late only when
  their bosses live long, as the minute-5 brute xlmantis does (dropped at 8:36 to 12:31: the bot
  outruns it). Before the chest fix, one run's minute 20 chest fell on a walled-in tile at
  (74, 41) and was never taken, the bot standing beside it from 23:46 to the end.
- **The late crowd, reported only**: from minute 20 to 30, no role held more than 48% of the crowd
  at any frame with 250 or more alive (each minute's largest share and role are in summary.json's
  `lateCrowd`), where minutes 21 to 27 were 82% to 99% flowers and minute 20 all bat swarm before;
  each minute's peak showed four to eight crystal colours (cast.json's per role). Seeds 2 and 3,
  run the same way by hand, peaked at 50% and won all eight runs within the pacing band.
- **The positioning bot is rarely touched**: none to three hits in 30 minutes, never below 86% health,
  and seeds 1 to 4 of all four cases, run by hand, win with none to four hits, never below 86% (before
  ps-6bl, 73 hits over the same sixteen, three of them below 25%, seed 3's original order fallen), as native's own sweep keeps full health on three of its four. A grid hero
  striding a tile every stride step outruns the crowd, which travels only on main beats. That is the
  run's difficulty as it stands, not asserted either way.
- **Standing still under the minotaur**: the rotated order once fell at 2:51, standing for 14 s on
  one tile while the signature minotaur (725 health at level 9) slammed the mark under the hero,
  which is where the hero stood as it telegraphed. Two changes keep it moving: the bot forgets a gem
  its route comes no nearer to, which it had stood for (ps-b3v), and it steps off any telegraphed
  slam whose blow would reach it before the blow lands (ps-msz.6.14).
- **Dodging every telegraph** (ps-6bl): the sword's half-time pulse above 150 BPM (ps-at1) took the
  reversed order to 0.139 health, three lunges of the minute-10 mantis in 1.4 s at 10:10: a far
  crowd's pull walked the bot into the boss (the crowd's weights count every enemy alike). Closing
  that gap uncovered the next, each failing the gate in turn: the bot stood against a wall its escape
  ran into while the minotaur lunged, then stood in a boss's charge line for three charges; and seed 3
  fell walking back toward a chest into slams it had just stepped clear of. The bot now flees a noticed lunge and a charge whose line reaches it, slides
  along a wall in danger, and routes round a slam's blow until it lands.
- **Frozen time** (188 to 274 s): each level-up's cards (about 2.1 s each, 62 to 73 s a run, half
  what they took at native's pace), each chest's roulette (36 to 54 s), the music stopping (70 to
  138 s) and the waits for a resume's beat (11 to 15 s). On the continuous stream the music stops at every track change, the analyzer
  confirming each song's end into the quiet. The piano aria's stop used to last 56 s, its beat
  turning reliable only 56 s in: since ps-msz.6.14 a stop lets go once the music has played 8 s
  without a lock, which took the continuous run's song from 2354 s to 2124 s. On cached tracks the
  gaps carry no ticks at all and only some endings stop it. The reversed order opens on the aria
  (300 s long), whose beat first holds reliable for the start's 2 s at 182 s, so its run starts
  there and needs 2280 s of the playlist's 2294: hence the second pass.

The bots' rules are pinned by game/Assets/Game/Tests/Sim/Bots/BotTests.cs (the escape, the gems
and the chests' pull, routing round a structure and out of a pocket, off a slam's mark, round a slam's blow, away
from a lunge, along a wall in danger, one stride a step, the cards,
the idle bot standing, and a run's determinism).
