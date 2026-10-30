# Showcase foundation (ps-8sb.1)

`--showcase` plays demo2d's *Action* (demo2d/Assets/Baked/Audio/action.wav, baked by
`uv run --project demo3d/tools demo2d setup`; `--file` overrides it, `PS_ACTION_WAV` moves it)
on `levels/action-showcase.yaml`, at native's own density for now, with nobody at the keys:

```sh
cd native
cargo run --release -p app -- --showcase --headless   # quits on its own once the run is over
cargo run --release -p app -- --showcase-stills logs/sessions/<id>   # these three stills
```

- **Level**: `levels/survivors.yaml` trimmed to the song (its first four minutes, `durationS: 240`)
  with two new survivors options: `endsWithSong` (the finale follows the song's confirmed end, as a
  reactive level's does, and the music-stopped freeze is off) and `heroStands` (a hit never takes the
  hero below a tenth of its health; hits still land, flash and sound).
- **Player**: `players::SurvivorBot` (positioning, eval's default bot) drives the hero and picks
  every level-up card one beat after the cards land; chests resolve on their own. It sees the game
  where the simulation has run to and presses there, so its keys never roll back.
- **Start and end**: the run starts by native's rule (2 s of clear music, on a beat: song 7.0 s; the
  intro before it is nearly silent). The finale lands about 3 s after the song's last note (the
  confirmed end), its epilogue plays, the end card shows for 3 s, and the app quits.

## Decision 3: Delayed listening

The showcase listens Delayed (3 s late, hindsight beats). Nobody plays, so the 3 s costs nothing,
and the same song, bot and level ran:

| | Direct | Delayed |
|---|---|---|
| rollbacks / re-simulated song time | 5848 / 1336 s | 198 / 35 s |
| frame work p95 (headless) | 1.29 ms | 0.25-0.58 ms |
| sounds cancelled by rollbacks | 133 | 100 |

Hindsight beats also put the beat grid right from the first bar.

## The run these stills are from (session 1793382981)

- `--showcase --headless`: exit 0, no errors, 213.7 s wall clock. Run from song 7.0 s, over at
  run 199 s (`Song`: the finale played), 11 level-ups (level 12), 1366 kills, health 1.00 at the end
  (native's density never reached the hero). In a trial where the bot pressed at the time heard
  instead, it was hit down to the floor and held there, 0.10, to the end (session 1793382329).
- Peak crowd 117-120 alive (the minute-2 bat swarm); native's cap and schedule, not yet demo2d's.

| Still | Moment |
|---|---|
| `cards.png` | the first level-up's cards, run 8.9 s (song 15.9 s) |
| `crowd.png` | the biggest crowd, run 183.9 s (song 190.8 s), 117 alive |
| `finale.png` | the finale's blast landing (10.0K on every foe), run 196.0 s (song 203.0 s) |

## Known gaps

- `--seconds 200` (the bead's acceptance) stops 3 s before the end: the song is 199 s long, a
  confirmed end needs 3 s of quiet after it, and Delayed listening hears everything 3 s late.
  Without `--seconds` the showcase quits on its own; `--seconds 220` is a safe cap.
- A Delayed showcase session replays with the same record count but a different settled hash
  (`ps replay` says DIFFERENT); Direct showcase sessions and Delayed sessions without the bot replay
  exactly. The stills are then a near replay, and the tool says so. Follow-up bead filed.
- The finale still shows native's "BOSS APPROACHING" banner over the blast.
