# The crowd's floor: no empty stretches through the whole run (ps-w9p.12)

Operator feedback after ps-w9p.4's denser opening, playing the grid mode: "still feels pretty empty at times".
ps-w9p.4 raised only minutes 0 to 2; this measures the whole 30 minutes and fixes what empties the screen.

## The run

The harness, [Floor](Floor/Program.cs), plays the run's level as the sweeps do (`BotRun`'s 60 fps frames, each
entry 60 ms after its stamp, seed 1, the headless start, the game camera's footprint, the arsenal from the
starting sword) for the positioning bot on the grid, to the victory card. For each second of run clock's playing
frames it writes the alive crowd and the part of it on screen (inside the footprint round the hero), lowest and
highest, the loudness level and the hero's level; and every stretch with fewer than 15 on screen for longer than
2 s. Its streams are entry dumps (native's `ps entries`) laid end to end with 2 s gaps:

```sh
E=~/Library/Caches/playlist-survivors/entries
dotnet run --project game/evidence/floor/Floor -c Release -- game/evidence/floor after-playlist $E/9a7c4bf2….jsonl $E/9a7c4bf2….jsonl
dotnet run --project game/evidence/floor/Floor -c Release -- game/evidence/floor after-house $E/77ee3d25….jsonl …  # 40 times
dotnet run --project game/evidence/floor/Floor -c Release -- game/evidence/floor after-aria $E/286bac83….jsonl …   # 9 times
```

- **playlist**: the survivors test playlist's continuous dump (`native/playlists/survivors-30.m3u`: house,
  hip-hop, accelerando, drum and bass and the piano aria), the sweeps' continuous case, twice.
- **house**: house-128 looped (a loud song: 96% of its ticks loud).
- **aria**: cc-kimiko-ishizaka-aria looped (the quiet one: 7% quiet and 29% medium ticks, and long stretches with
  no reliable beat, where enemies hold).

`before-*.csv` ran on staging 8b1495d6, `after-*.csv` on this change, 2026-11-01; `*-empty.csv` lists the
stretches.

| stream | stretches under 15 on screen for > 2 s, past minute 0 | their seconds | longest | fewest alive past minute 0 | end | hero's level at 30:00 |
|---|---|---|---|---|---|---|
| playlist, before | 46 | 329.5 | 24.9 s (17:29) | 24 | victory, health 1.000 | 30 |
| playlist, after | 1 | 4.0 | 4.0 s (24:17) | 35 | victory, health 1.000 | 30 |
| house, before | 55 | 351.8 | 24.1 s (17:42) | 12 | victory, health 1.000 | 42 |
| house, after | 0 | 0 | 1.4 s (0:35) | 21 | victory, health 1.000 | 37 |
| aria, before | 35 | 251.4 | 26.3 s (11:23) | 37 | victory, health 1.000 | 28 |
| aria, after | 1 | 2.8 | 2.8 s (6:20) | 28 | victory, health 1.000 | 27 |

On screen, the mean over each minute's playing frames (in brackets, seconds of that minute in stretches):

```text
playlist before  0:22(13s) 1:20(23s) 2:40(4s) 3:15(40s) 4:18(27s) 5:35(17s) 6:30(4s) 7:64(7s) 8:30(19s) 9:17(27s) 10:48(9s) 11:89(15s) 12:46(7s) 13:44(2s) 14:54(8s) 15:60 16:65 17:32(25s) 18:19(19s) 19:46(6s) 20:83(10s) 21:55(8s) 22:44(3s) 23:46(10s) 24:54(26s) 25:58(3s) 26:156 27:110(8s) 28:144 29:93(3s)
playlist after   0:30(6s) 1:43 2:68 3:62 4:57 5:77 6:90 7:83 8:57 9:65 10:72 11:119 12:98 13:91 14:118 15:138 16:121 17:71 18:65 19:83 20:128 21:100 22:140 23:125 24:119(4s) 25:114 26:122 27:163 28:188 29:130
house before     0:20(23s) 1:30(19s) 2:35(7s) 3:21(18s) 4:12(40s) 5:24(21s) 6:12(41s) 7:28(7s) 8:22(29s) 9:22(26s) 10:31(9s) 11:102(11s) 12:34(17s) 13:99 14:95 15:111 16:134 17:16(50s) 18:19(23s) 19:53(3s) 20:128 21:121 22:127 23:81(6s) 24:104(7s) 25:55(16s) 26:146 27:147 28:175 29:187
house after      0:34 1:51 2:60 3:62 4:64 5:76 6:65 7:68 8:66 9:71 10:80 11:131 12:67 13:115 14:125 15:156 16:132 17:70 18:69 19:80 20:135 21:150 22:133 23:154 24:137 25:147 26:139 27:148 28:156 29:183
aria before      0:26(35s) 1:16(25s) 2:10(40s) 3:19(13s) 4:20(13s) 5:28(24s) 6:28(7s) 7:68 8:37(13s) 9:46(10s) 10:49(4s) 11:27(33s) 12:58(9s) 13:91 14:105 15:71 16:43(14s) 17:54(14s) 18:38(3s) 19:63 20:91 21:58(7s) 22:48(4s) 23:96 24:104(7s) 25:56(9s) 26:88 27:113 28:101 29:129
aria after       0:38(2s) 1:40 2:65 3:63 4:60 5:66 6:55(3s) 7:66 8:75 9:65 10:85 11:134 12:92 13:104 14:125 15:120 16:110 17:74 18:58 19:85 20:134 21:127 22:131 23:143 24:127 25:123 26:162 27:124 28:154 29:145
```

## What emptied the screen

Before, the empty stretches came from three things:

- **The crowd left behind**: a grid hero strides at 5.2 m/s and the crowd walks 1.2 to 2.9 m/s, on main beats
  only, so a hero on the move outruns it. 44 of the 136 stretches (21 of the playlist's 46) had 200 to 300
  alive, out of sight behind the hero and short of native's recycling distance (1.5 view widths, 75 m): the
  screen was empty at the cap.
- **Native's low minimums**: minutes 3 to 6, 9, 10, 12, 14, 17 and 18 keep 10 to 60 alive (Vampire Survivors'
  curve), which the sword clears in moments: 55 of the stretches had fewer than 100 alive (31 of house's 55).
  Minute 17 (xlmummy only, minimum 20, every 1 s) held the longest on the playlist and house.
- **Set pieces counted as crowd**: a flower wall's 100 flowers (minutes 5, 10, 15 and 25) counted toward the
  minimum, so the waves stopped while the hero walked out of the wall and left it behind (17 s of an empty
  screen in minute 5 on house, once the view's floor below had fixed the rest).

Raising the minimums alone (tried: every minute to 100) cut the stretches by only a tenth to a quarter (42, 41
and 30): the crowd was there, off screen. Recycling sooner in every direction (as ps-w9p.4 tried) landed it on the ring behind the hero too.

## The change

- **The view's floor** (`schedule.json`'s `run.viewFloor`, 60; `Spawner`): while fewer than 60 are in view and the
  hero is on the move (its displacement over the last 1.5 s, decaying, longer than 1 m), enemies out of sight
  behind it, beyond the spawn ring, are brought round onto the ring ahead of it: one point within 0.8 rad of its
  heading a tick, the tick's enemies within 7 degrees of it, so they arrive as a clump the hero runs into. A hero
  heading into the arena's edge or a wall gets them anywhere walkable round it. Only enemies beyond the ring are
  taken, so one just brought round isn't taken again before it walks in (tried: taking any out of sight churned
  the crowd round the hero at an edge, none walking in). A standing hero needs none: the crowd walks to it.
- **The minimum counts the horde's own**: the waves' minimum no longer counts the music-driven events' scripted
  enemies (a flower wall, a swarm, a set piece), which come and go on their own.
- **No minute past the opening below 80** (before the loudness scale: 104 loud, 48 quiet): minutes 3, 4, 5, 6, 9,
  10, 12, 14, 17 and 18 go from 40, 30, 10, 20, 30, 10, 20, 20, 20 and 60 to 80. Rosters, intervals, bosses,
  swarms, flower walls, set pieces and the cap are unchanged. The quiet scale stays on: a quiet song keeps its
  thinner crowd and its skipped waves (unity-game.md: quiet passages breathe), and the aria still has no stretch.
- **The experience curve's factor 1.9 to 3.2** (progression.json): the denser crowd's kills brought the sweeps to
  levels 13 to 15 at 5:00 and 36 to 39 at 30:00 (the design's 10 and 30, a 20% band); 3.2 brings them back.

Also tried: a floor of 40 left 8 to 11 stretches a stream; a floor of 100 and a minimum of 60 did no better than
60 and 80 within the runs' noise (a few stretches either way).

## What's left

- The one stretch on the playlist (24:17 to 24:21, 4 to 15 on screen at the cap, a medium-loudness lull) and the
  aria's (6:20, 12 to 14 on screen) are walk-ins: after the sword clears the view, a clump on the ring takes 2 to
  3 s to step in. Minute 0's own (house's first 2 s, the playlist's 0:56 to 1:02) are ps-w9p.4's opening.
- The positioning bot kites: it walks away from the crowd. A player who walks into it sees more.

## The later run

- **The bots' sweeps** (`uv run --project demo3d/tools game sweep --assert --force`, game/evidence/sweeps) all
  pass: the four positioning runs win (lowest health 0.868, 1.000, 0.868, 1.000), the idle bot falls at 2:05 as
  before. Levels at minutes 5 to 30: 11-12, 15, 19-22, 23-24, 25-27, 30-31 against the design's 10, 15, 19, 23,
  27, 30, all within the 20% band (ps-w9p.4's: 11, 14-15, 17-21, 22-24, 24-26 and 30-32).
- **The 30-minute schedule run** ([schedule/Run30](../schedule/Run30/Program.cs), a hero standing, so no view
  floor) on the playlist's continuous dump still ends in victory at 30:00, never above 300 alive, with all 20
  bosses and 13 chests; it spawned 16778 enemies against 16547 before.
- Never above 300: the cap is unchanged and the floor only moves enemies; every run's most alive is 300.
- Cost: the floor counts the view (up to its 60) each tick while the hero moves, beside the recycling pass that
  already walks the horde each tick; the waves count the scripted enemies once a wave.
