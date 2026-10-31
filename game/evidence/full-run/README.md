# The full run: a 30-minute File playlist to the victory card (ps-msz.6.14)

The run from its start to the victory card, headless in the IL2CPP player on the Mac (jarvis),
2026-10-31, built from this bead's tree on staging after ps-b3v, with the positioning bot playing
(`--bot positioning`, the sweeps' bot in place of the keyboard) over the survivors test playlist
played twice:

```sh
uv run --project demo3d/tools game build --platform mac --backend il2cpp
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile player.log --file "$PWD/native/playlists/survivors-30-twice.m3u" --headless --run \
  --bot positioning --events-out events.csv --loop-out loop.csv --sessions sessions
python3 game/evidence/full-run/pacing.py events.csv > pacing.json   # the numbers below
```

Exit 0 after 35 minutes of wall clock (in real time; at fixed time it now takes 4 minutes: see
[Retaking it quickly](#retaking-it-quickly-ps-msz75)). The player's summary:

```text
GameLoop: victory; 125138 frames to song 2121.971 s, 132618 entries (0 late), ..., hash F3C4252CC0E9CD13;
run Ended from song 3.780 s, run clock 1800.000 s, 83 freezes (16 music stopped), 0 run controls;
lead (ms) median 90.8, p5..p95 90.3..91.8, min..max 90.3..97.3 (output latency 38.0 + margin 52.3);
frameSong backwards 0, held 0 frames; frames at the frontier 0; sim step (ms) median 0.021, p99 0.351,
max 2.495; enemies alive 116, 13991 killed, 157 heavy strikes, 111 structures broken, 104 regrown,
80 zone changes, 133 set pieces; sounds 44072 scheduled, 2220 dropped
```

An earlier run of the same command on this bead's tree before ps-b3v also won (song 2109 s, level 54,
four evolutions, hash 5A48798D10A3D173, its player replay matching).

## Pacing

[pacing.json](pacing.json):

| | measured | the design (Manual QA, progression.json) |
|---|---|---|
| start | song 3.78 s (2 s of clear beat) | headless: after 2 s of clear beat |
| level at 5:00 | 15 | about 10 |
| level at 10, 15, 20, 25 minutes | 22, 33, 44, 45 | 15, 19, 23, 27 (`startLevels`) |
| final level | 55 | 30 |
| boss chests (run clock taken, its minute) | 3:32 (3, tier 2), 8:15 (7), 10:15 (5), 10:35 (10), 13:46 (12), 14:21 (14), 16:07 (15), 18:59 (18), 24:23 (22), 24:44 (23), 25:25 (20), 25:28 (25, tier 3), 27:54 (27) | at minutes 10, 15, 20 and 25 |
| evolutions | Bloody Tear 10:35, Soul Eater 16:07, Holy Wand 25:25, Unholy Vespers and Thunder Loop 25:28: all five | evolutions |
| peak | 300 alive (the cap), first at 11:00; 121 to 300 from minute 15 on | 300 at the peak |
| frozen | 310 s of song: cards 145, chests 33, music stopped 132 | |
| finale | the blast at song 2113.96 s, the victory card 3 s later, at 30:00 of run clock | 30:00 |

- **Levels run ahead of the design's pacing**, as in the sweeps (game/evidence/sweeps: 14 to 17 at
  5:00): the bot gathers nearly every gem. Tuning the run is M4's (unity-game.md, Pickups,
  level-ups and chests).
- **Every boss chest of minutes 10, 15, 20 and 25 was taken**, but late when the field was busy:
  the bot walks to a chest only when nothing is within its safety reach, and a chest opens only
  with no enemy body within 2 m. Minute 20's waited until 25:25, beside minute 25's.
- **The music stopped 16 times**, at each track change (the analyzer confirms each song's end into
  the 2 s gaps), 7 to 13 s each. The piano aria's stop let go after 9.3 s (275.17 to 284.44), on
  the free pulse: before this bead's change it held 56 s, until the aria's beat turned reliable
  (unity-game.md, The Run).
- **One pass of the playlist was enough here** (song 2117 of its 2294 s), but the sweeps' reversed
  order needs 2280 s, so the full run plays `survivors-30-twice.m3u`.

## Errors and hitches

- **No error or exception during the run.** The log's `ERROR: Shader ... not supported on this
  GPU` lines (46 of them, before line 111) all come as the headless player (no graphics device)
  loads its shaders, before the run starts.
- **The sim kept its lead on every frame**: 90.3 to 97.3 ms, never at the frontier, the sim's step
  2.5 ms at most.
- **Frame pacing** (loop.csv's frameSong): median 16.8 ms, p99 18.7 ms, p99.9 21.9 ms; 32 frames
  over 25 ms and 3 over 33 ms in 125,138, the longest 66 ms at song 1976.5 s. None lines up with a
  long sim step, so they are pauses of the headless process on a shared machine (the garbage
  collector or the OS), not the run's work. The windowed frame budget is M4's.
- **The replay**: in .NET the session (6628 key changes) diverges from the IL2CPP run (322,487
  events against 466,701), as a session replays only in the runtime that recorded it (unity-game.md,
  Sessions and Replay). In the IL2CPP player that recorded it (`game replay DIR --player`, 35 minutes
  with its recorded music) it replays to the same hash, F3C4252CC0E9CD13: it matches.

## The run's peak, at minute 25

Windowless stills at fixed time, from 25:00 on cc-kevin-macleod-go-cart with the bot playing and the
three evolutions given at 5 s (a hero built as `--start-minute 25` builds it, level 27):

```sh
uv run --project demo3d/tools game look full-run --evidence full-run --at 60,61.2,68.45,68.8,72 --readability \
  --player-args "--file $PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg --start-minute 25 \
  --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@64,finale@68"
```

| still | run clock | what it shows | readability dL* |
|---|---|---|---|
| [still-060.000.jpg](still-060.000.jpg) | 25:49 | the peak: 299 alive, Bloody Tear's crimson slashes and the white blade, Soul Eater's teal aura round the hero, the other weapons' gold shards and white streaks, a blue crystal boss | +40.7 |
| [still-061.200.jpg](still-061.200.jpg) | 25:51 | a structure break: the green crystal's shards (two broke at 61.07 s on a heavy strike) beside a heavy strike's cleave and the mushrooms shattering | +42.4 |
| [still-068.450.jpg](still-068.450.jpg) | 25:57 | the mass kill: the finale's blast at 68.37 s (a forced finale) breaks 292 monsters at once into orange glass | +45.8 |
| [still-068.800.jpg](still-068.800.jpg) | 25:57 | the glass settling, the gems they dropped among it | +41.1 |
| [still-072.000.jpg](still-072.000.jpg) | 25:57 | the victory card, each weapon's damage, time held and DPS | +81.1 |

Against the reference look: the world stays dark at night, the monsters read as crystal against it
(every still passes the readability floor), the blade is white, and every death breaks into glass on
its frame. One gap for M4: at minute 25 the schedule's crowd is all one role (297 of the 300 are the
crystal mushrooms), so the field reads orange, not rainbow; the rainbow comes only from the weapons
and the bosses.

**The bug this found** (fixed here, `CrowdView.SlotsPerType`): the first take of these stills drew no
monsters at all past 27.4 s of song and froze the HUD at 25:22 (readability dL* +2.8). From the
minute-25 crowd's first set piece the crowd threw `CrowdSlots: type 7 is over its 375 slots` on every
frame. Each life keeps its type's slot, and a freed slot stays Hidden for a frame, so a frame holds
last frame's slots beside this frame's new owners. When a set piece makes room (far monsters leave)
and its members spawn on the same beat, all mushrooms, that passes 375 with only about 297 alive; the
throw skipped the slots' end of frame, so none was freed again, and the HUD, presented after the
crowd, stopped too. The headless run never draws the crowd, so only stills show it. The slots now
hold twice the living (600), and `CrowdSlotsTests` replaces a whole crowd of one type on one beat.

## Changed by this bead

- **The beatless thaw** (`RunRules.BeatlessResumeS`): a music stop lets go once the music has
  played 8 s with no lock, resuming on the next beat or stride step.
- **The bot steps off telegraphed slams** (`SurvivorBot`): the sweeps' rotated run fell at 2:51
  under the signature minotaur's slams, the bot standing still (for a gem out of reach, as ps-b3v,
  landed alongside, found and fixed), and the minotaur's slam lands where the hero stood.
- **`--bot positioning|idle`** in the player (`Client/Loop/BotInput.cs`).
- **The crowd's slots** (`CrowdView.SlotsPerType`, 375 to 600): see the peak above.

Operator rows for the Manual QA checks this needs a window or speakers for (the full File run,
tempo bands, the band boundary, beatless passages, song gaps and the mass kill) are in
[operator-pending.md](../operator-pending.md); Watch my run and the presses were already there.

## Retaking it quickly (ps-msz.7.5)

Under `--fixed-time` the frames drive the music (docs/design/unity-game.md, Fixed time), so the full
run, its replay and late stills take minutes, not their song time. On jarvis (M-series Mac),
2026-10-31, IL2CPP player built from ps-msz.7.5's tree:

```sh
uv run --project demo3d/tools game build --platform mac --backend il2cpp
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile "$PWD/player.log" --file "$PWD/native/playlists/survivors-30-twice.m3u" --headless --fixed-time \
  --run --bot positioning --events-out "$PWD/events.csv" [--sessions "$PWD/sessions"]
uv run --project demo3d/tools game replay sessions/<time> --player     # fixed time since ps-msz.7.5
```

(`-logFile` and the outputs want absolute paths: the player resolves a relative log path elsewhere.)

| check | wall clock | before (real time) | result |
|---|---|---|---|
| the full run, take 1 | 240 s | 35 min | victory at 30:00, song 2129.9 s, level 55, all five evolutions, hash 1BFD33F3E43B538E |
| the full run, take 2 | 237 s | | the same hash, and a byte-identical events.csv |
| the full run with `--sessions`, take 3 | 255 s | | the same hash (a cold-review agent ran beside it) |
| take 3's 30-minute session, `game replay --player` | 60 s | 35 min | matches, 1BFD33F3E43B538E (473,544 events) |
| a 120 s session recorded in real time (no `--fixed-time`), `game replay --player` | 7.8 s | 2 min | matches, 3355A08373B6F440 |
| a still at 24:57 of run clock (song 1748 s) on the real run, no `--start-minute` | 244 s | 29 min | [still-1748.000.jpg](still-1748.000.jpg), readability dL* +85.4 |

```sh
uv run --project demo3d/tools game look full-run-25 --evidence full-run --at 1748 --readability \
  --player-args "--file $PWD/native/playlists/survivors-30-twice.m3u --run --bot positioning"
```

- **The fixed-time run plays the run as the real-time one did**, on its own hash (fixed time steps the
  sim on frame time): victory at 30:00 of run clock, level 16 at 5:00 and 55 at the end, 14 chests and
  all five evolutions (Bloody Tear 10:38, Soul Eater 16:50, Holy Wand, Unholy Vespers and Thunder Loop
  25:26), 84 freezes (16 music stopped), 132,983 entries and none late. The player's summary now logs
  the seconds since launch beside the song time.
- **What limits it is the analysis.** Frames alone run 120 s of song in under 2 s. With the hindsight
  decode on one job, as live, 120 s took 32 s: each second of music decodes a 20 s window (its front
  end, eight networks and DBN), and the frames wait on it, as `ps entries` reads at about 5 times real
  time for the same reason. The driven session's 8 `hindsight_jobs` run the window's front end and
  networks in parallel: 120 s in 12 s, and the full run at about 9 times real time on average (the
  peak's frames cost more). Its entries are the same: the fixed-time run's 120 s hash, 627F7F968D16A8D6,
  was the same with one job and eight.
- **The still at 25:00** is the run's own state, not a `--start-minute` hero: level 46 with the cards
  of a level-up open, 10,923 kills, the field full of the crystal mushrooms. The crowd draws (dL* +85),
  so ps-msz.6.14's slot overflow is gone on a real run's minute 25 too.
- Slow CI now covers this path: `game replay --record` also replays its session at fixed time and runs
  the file twice at fixed time to one hash (about 10 s more).
