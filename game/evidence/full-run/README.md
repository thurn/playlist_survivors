# The full run: a 30-minute File playlist to the victory card (ps-msz.6.14)

The run from its start to the victory card, headless in the IL2CPP player on the Mac (jarvis),
2026-10-31, with the positioning bot playing (`--bot positioning`, the sweeps' bot in place of the
keyboard) over the survivors test playlist played twice:

```sh
uv run --project demo3d/tools game build --platform mac --backend il2cpp
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile player.log --file "$PWD/native/playlists/survivors-30-twice.m3u" --headless --run \
  --bot positioning --events-out events.csv --loop-out loop.csv --sessions sessions
python3 game/evidence/full-run/pacing.py events.csv > pacing.json   # the numbers below
```

Exit 0 after 35 minutes of wall clock. The player's summary:

```text
GameLoop: victory; 125615 frames to song 2114.234 s, 132198 entries (0 late), ..., hash 5A48798D10A3D173;
run Ended from song 3.780 s, run clock 1800.000 s, 81 freezes (16 music stopped), 0 run controls;
lead (ms) median 90.3, p5..p95 90.3..91.7, min..max 90.3..104.7 (output latency 38.0 + margin 52.3);
frameSong backwards 0, held 0 frames; frames at the frontier 0; sim step (ms) median 0.022, p99 0.609,
max 2.076; enemies alive 113, 12964 killed, 158 heavy strikes, 160 structures broken, 155 regrown,
80 zone changes, 143 set pieces; sounds 43089 scheduled, 2325 dropped
```

## Pacing

| | measured | the design (Manual QA, progression.json) |
|---|---|---|
| start | song 3.78 s (2 s of clear beat) | headless: after 2 s of clear beat |
| level at 5:00 | 16 | about 10 |
| level at 10, 15, 20, 25 minutes | 22, 33, 41, 43 | 15, 19, 23, 27 (`startLevels`) |
| final level | 54 | 30 |
| boss chests (run clock taken, its minute) | 1:59 (1), 10:44 (9, tier 2), 11:11 (5), 11:14 (10), 14:11 (14), 16:32 (15), 18:22 (18), 23:23 (22, tier 3), 24:19 (23), 25:27 (20), 25:27 (25, tier 3), 27:13 (27, tier 2) | at minutes 10, 15, 20 and 25 |
| evolutions | Bloody Tear 11:14, Soul Eater 16:32, Holy Wand and Unholy Vespers 25:27 | evolutions |
| peak | 300 alive (the cap), first at 7:31; 276 to 300 through minute 15 | 300 at the peak |
| frozen | 302 s of song: cards 130, chests 33, music stopped 139 | |
| finale | the blast at song 2106.22 s, the victory card 3 s later, at 30:00 of run clock | 30:00 |

- **Levels run ahead of the design's pacing**, as in the sweeps (game/evidence/sweeps: 14 to 17 at
  5:00): the bot gathers nearly every gem. Tuning the run is M4's (unity-game.md, Pickups,
  level-ups and chests).
- **Every boss chest of minutes 10, 15, 20 and 25 was taken**, but late when the field was busy:
  the bot walks to a chest only when nothing is within its safety reach, and a chest opens only
  with no enemy body within 2 m. Minute 20's waited until 25:27, beside minute 25's.
- **The music stopped 16 times**, at each track change (the analyzer confirms each song's end into
  the 2 s gaps), 7 to 13 s each. The piano aria's stop let go after 9.3 s (275.17 to 284.44), on
  the free pulse: before this bead's change it held 56 s, until the aria's beat turned reliable
  (unity-game.md, The Run).
- **One pass of the playlist was enough here** (song 2109 of its 2294 s), but the sweeps' reversed
  order needs 2268 s, so the full run plays `survivors-30-twice.m3u`.

## Errors and hitches

- **No error or exception during the run.** The log's `ERROR: Shader ... not supported on this
  GPU` lines (46 of them, lines 20 to 110) all come as the headless player (no graphics device)
  loads its shaders, before the run starts.
- **The sim kept its lead on every frame**: 90.3 to 104.7 ms, never at the frontier, the sim's step
  2.1 ms at most.
- **Frame pacing** (loop.csv's frameSong): median 16.7 ms, p99 18.6 ms, p99.9 20.6 ms; 24 frames
  over 25 ms and 5 over 33 ms in 125,615, the longest 131 ms at song 1150.8 s. None lines up with
  a sim event or a long sim step, so they are pauses of the headless process on a shared machine
  (the garbage collector or the OS), not the run's work. The windowed frame budget is M4's.
- **The replay**: the session (5598 key changes) replays in .NET to the same 463634 events with a
  different hash, as a session recorded in IL2CPP should (unity-game.md, Sessions and Replay).
  In the IL2CPP player that recorded it (`game replay DIR --player`, 35 minutes with its recorded
  music) it replays to the same hash, 5A48798D10A3D173: it matches.

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
- **The bot steps off telegraphed slams** (`SurvivorBot`): without it the sweeps' rotated run fell
  at 2:51, four of the signature minotaur's slams landing on a bot standing on a gem.
- **`--bot positioning|idle`** in the player (`Client/Loop/BotInput.cs`).
- **The crowd's slots** (`CrowdView.SlotsPerType`, 375 to 600): see the peak above.

Operator rows for the Manual QA checks this needs a window or speakers for (the full File run,
tempo bands, the band boundary, beatless passages, song gaps and the mass kill) are in
[operator-pending.md](../operator-pending.md); Watch my run and the presses were already there.
