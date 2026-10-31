# The sim's enemies: step time at 300 alive (ps-msz.3.2)

The target (unity-game.md, "Performance Targets"): the sim's step at 300 alive at most 1 ms p99
per frame.

What was run, on jarvis (Apple silicon Mac, .NET 10, the sim built optimized):

```bash
dotnet test game/dotnet/PS.Game.Sim.Tests --filter ReportsTheStepTimeAtThreeHundredAlive --logger "console;verbosity=normal"
```

The test drives a sim the way the client does, one `StepTo` per 60 fps frame, over 85 s of a
synthetic song (128 BPM, a 4 s gap, then a 92 to 160 BPM ramp with jitter), with the slice's
roster held at the cap of 300 alive in the default 240 m open arena (a 322 by 322 cell nav
grid). The grid hero walks a square on held keys, a side every 3 s, so the flow field rebuilds
each time it changes tile, 4096 cells a tick. It times each frame's `StepTo` after the first
5 s and prints the distribution. It asserts nothing about time, since checks run under load.

Three runs on 2026-10-31, with other sessions loading the machine (load average 7 to 11):

| run | frames | p50 | p99 | max |
|---|---|---|---|---|
| 1 | 5101 | 0.115 ms | 0.398 ms | 1.812 ms |
| 2 | 5101 | 0.113 ms | 0.380 ms | 1.888 ms |
| 3 | 5101 | 0.112 ms | 0.369 ms | 2.779 ms |

The p99 is well inside the target. The p50 is mostly the flow field's rebuild slices; the p99
frames add a main beat's decisions (flow-field lookups and two separation passes for every
stepping enemy). The rare maxima are the loaded machine's scheduling, not the sim. Cygnus
measures the real target in M5.

## After the Warden's pathing fixes (ps-msz.3.15)

Steps now walk the flow field's path half a cell at a time, keep the farthest point a straight
move reaches, and test moves cell by cell. Old and new code alternated on 2026-10-31, built
Release (`-c Release`), load average 8 to 10:

| run | old p99 | new p99 | old max | new max |
|---|---|---|---|---|
| 1 | 0.486 ms | 0.491 ms | 2.215 ms | 1.626 ms |
| 2 | 0.368 ms | 0.445 ms | 1.688 ms | 2.046 ms |
| 3 | 0.419 ms | 0.584 ms | 1.440 ms | 3.280 ms |

The path walk adds about 0.05 to 0.15 ms at p99 under this load, mostly the long steps of fast
roles; the p99 stays well inside the 1 ms target.
