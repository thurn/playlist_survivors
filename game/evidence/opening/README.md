# The opening: a crowd from the first seconds (ps-w9p.4)

Operator feedback from a free-mode playlist run (epic ps-w9p): "monster density is way too low at the
start; nothing happens for a long time". The opening is schedule.json's, so the fix is for every mode.

| minute | before (native's) | after |
|---|---|---|
| 0 | bat3 every 1 s, minimum 15 | bat3 and bat1 every 0.5 s, minimum 40 |
| 1 | zombie and bat1 every 1 s, minimum 30 | zombie and bat1 every 0.5 s, minimum 60 |
| 2 | bat1-3 every 0.5 s, minimum 50 | bat1-3 every 0.5 s, minimum 80 |

Minutes 3 on, the cap of 300, the loudness scale (0.6 quiet, 1.3 loud) and the spawn ring are unchanged.

## The run

The harness, [Opening](Opening/Program.cs), plays the run's level as the sweeps do (`BotRun`'s 60 fps
frames, each entry 60 ms after its stamp, seed 1, the headless start, the game camera's footprint) over the
continuous dump of the survivors test playlist (`native/playlists/survivors-30.m3u`, house-128 first),
for the positioning bot (kiting the crowd on the grid) and the idle bot (standing, the sword on the beat).
For each second of run clock's playing frames it writes the alive crowd and the part of it on screen
(inside the footprint round the hero), lowest and highest:

```sh
dotnet run --project game/evidence/opening/Opening -c Release -- \
  ~/Library/Caches/playlist-survivors/entries/9a7c4bf2….jsonl game/evidence/opening after
```

[before.csv](before.csv) ran on the schedule before this change, [after.csv](after.csv) on the new one,
2026-11-01. House-128 is loud, so minute 0's minimum is 52 after (20 before).

| positioning bot | 0-5 s | 5-15 s | 15-30 s | 30-45 s | 45-60 s | 60-90 s | 90-120 s |
|---|---|---|---|---|---|---|---|
| before: alive / on screen | 3-24 / 0-22 | 6-23 / 0-20 | 7-22 / 0-20 | 3-23 / 0-13 | 9-21 / 0-18 | 30-51 / 0-26 | 25-53 / 0-37 |
| after: alive / on screen | 16-68 / 0-59 | 34-63 / 2-48 | 28-62 / 0-47 | 27-59 / 0-43 | 33-58 / 3-44 | 68-91 / 1-64 | 59-89 / 0-61 |

| | first on screen | on screen, mean, minute 0 / 1 | seconds of minute 0 with none on screen | longest below the minute's minimum | kills, minute 0 / 1 |
|---|---|---|---|---|---|
| positioning, before | 1.34 s | 7.9 / 11.5 | 8 | 0.85 s (of 15) | 280 / 258 |
| positioning, after | 1.32 s | 21.6 / 20.0 | 3 | 0.43 s (of 40) | 662 / 497 |
| idle, before | 1.39 s | 12.7 / 47.4 | 1 | 0.85 s (of 15) | 162 / 73 |
| idle, after | 1.32 s | 36.0 / 91.3 | 1 | 0.30 s (of 40) | 501 / 171 |

- **On screen within 2 s**: the first wave lands at 0 s just outside the view and walks in by 1.3 s, as
  before, but as two clumps of 26 (one of 20 before). The second counted with none on screen is second 0.
- **Never below the new minimum for longer than a wave**: the longest stretch below 40 in minute 0 or 60
  in minute 1 is 0.43 s, inside the 0.5 s wave interval: a clump the sword clears is replaced on the next
  wave. Before, the crowd sat at its 15-20 in minute 0, and one sword pass cleared all of it.
- **The kiting bot still outruns the crowd now and then**: it walks a tile a stride step away from the
  crowd, faster than enemies that travel only on main beats, so the screen empties briefly (2 s of minute 0
  after, 7 before) until it meets the next clump. A player walking into the crowd, as free mode's does, has
  it in view.
- **Nothing dying**: the cap fills at about 1:05 of run clock instead of 2:18 (ScheduleTests: 278 in
  minute 0, 22 in minute 1). The idle bot's sword keeps the crowd near the minimum in minute 0; it falls
  at 1:58 (1:59 before).

## The later run

- **The bots' sweeps** (`uv run --project demo3d/tools game sweep --assert --force`, game/evidence/sweeps)
  all pass on the new schedule: the four positioning runs win (lowest health 0.876, 0.924, 1.000, 1.000),
  the idle bot falls at 2:05, as before. Their levels at minute 5 rise from 10 to 11 against the design's 10
  (`startLevels`, within the 20% band); minutes 10 to 30 stay at 14-15, 17-21, 22-24, 24-26 and 30-32.
- **The 30-minute schedule run** ([schedule/Run30](../schedule/Run30/Program.cs)) on the same dump still
  ends in victory at 30:00, never above 300 alive, with all 20 bosses and 14 chests; it spawned 16547
  enemies against the old schedule's 14957 on the same dump, 1150 of the difference in minutes 0 to 2.
- The peak and its performance targets are the cap's (unity-game.md, Performance targets): unchanged.
