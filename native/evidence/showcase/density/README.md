# Showcase density (ps-8sb.2)

`--showcase` now plays demo2d's crowd curve: one schedule row per section of *Action*
(songs/action/songmap.json), each row's crowd **on screen** demo2d's `onScreen` x 400
(demo2d/Assets/Demo2D/Data/action-level.json), and the camera widening with demo2d's `viewWidthM`
against its 30 m base.

```sh
cd native
cargo run --release -p app -- --showcase --headless       # records logs/sessions/<id>
cargo run --release -p app -- --showcase-density logs/sessions/<id>   # these numbers and stills
```

`--showcase-density` plays the session's recorded music again, offscreen and as fast as it can,
on the showcase level as it is now (`--level` for another), with the virtual player at the keys as
the app's showcase has it, five times with five player seeds. It counts the enemies inside the
camera's view every frame (frames frozen on cards or a chest left out, and each section's first
quarter, at most 2 s), writes a still from the middle of each section (seed 1) and times the
frame's work through the final peak (seed 1). Its output for session 1793384906 is `density.json`
and the twelve stills here.

## How the level does it

- **Rows on the song clock** (`songS`): a row begins at its section's song time (the song map's
  time less the run's start, song 7.0 s), so the level-ups' freezes never shift the curve.
  Without `songS`, rows stay per minute on the run clock, as in every other level.
- **A crowd on screen, not alive** (`minimum`, song-clock rows only): the spawner keeps a crowd in
  an arena (the view and a band a quarter of its width round it, where fresh spawns land just out
  of view), steered every tick by the view's count against the row's minimum: an integral term
  (the arena's size per enemy wanted on screen, 1x to 4x) and a proportional push. Each row's
  crowd leads its section by up to 2 s (a quarter of the row at most), since spawns take that long
  to walk in. Ordinary enemies left outside the arena, out of sight, leave the field, so the crowd
  falls with the music too.
- **The cap** (`survivors.cap`, default `SURVIVOR_CAP` = 300): the showcase allows 1000 alive; it
  holds about 400 on screen at the final peak with about 950 alive.
- **The view** (`widen`, eased over 2 s from each row's start): 30, 32, 34, 36 and 40.5 m against
  demo2d's 30 m base, i.e. 1.0 to 1.35 times native's own view. The showcase's loudness camera has
  one zoom (1.2) at every level, so the framing follows demo2d's widths, not native's loudness.
- **The mixes**, mapped onto native kinds (recorded in levels/action-showcase.yaml): grunts to
  vs-skeleton, vs-zombie, vs-bat2/3, vs-ghost and vs-mudman1; casters to vs-xlbat (native has no
  ranged survivors kind); elites to vs-mudman2, vs-xlmantis, vs-werewolf, vs-xlmummy; demo2d's boss
  devil to vs-boss-xlmantis at the final peak.

## Crowd per section (session 1793384906)

On screen and alive are seed 1's (the stills' run); the ratio's mean and range are over the five
seeds.

| Section | Song s | demo2d on screen | Native on screen | Ratio | Ratio, 5 seeds: mean (range) | Alive |
|---|---|---|---|---|---|---|
| intro | 0.0 | 3 | 13 | 4.05 | 3.31 (2.04-4.05) | 62 |
| groove | 12.4 | 60 | 49 | 0.81 | 0.84 (0.78-0.99) | 153 |
| break-1 | 34.1 | 60 | 61 | 1.02 | 0.91 (0.59-1.02) | 271 |
| drop-1 | 37.2 | 160 | 138 | 0.86 | 0.90 (0.86-0.96) | 531 |
| breakdown | 74.3 | 120 | 143 | 1.19 | 1.07 (0.78-1.19) | 493 |
| groove-2 | 86.7 | 200 | 214 | 1.07 | 1.08 (1.05-1.12) | 658 |
| build | 111.5 | 200 | 210 | 1.05 | 1.05 (1.01-1.11) | 571 |
| break-2 | 120.8 | 160 | 176 | 1.10 | 1.14 (1.06-1.34) | 614 |
| drop-2 | 123.9 | 320 | 335 | 1.05 | 1.04 (1.01-1.05) | 889 |
| **final-peak** | 161.0 | **384** | **408** | 1.06 | 1.03 (0.92-1.06) | 948 |
| outro | 185.8 | 200 | 215 | 1.07 | 1.18 (1.07-1.43) | 553 |
| ending | 195.1 | 100 | 60 | 0.60 | 0.55 (0.43-0.60) | 313 |

(demo2d's final peak asks for 0.96 of 400 and averages 399 on screen; native's averages 408.)

Over the five seeds, seven sections average within 10% of demo2d (break-1, drop-1, breakdown,
groove-2, build, drop-2, the final peak), break-2 within 14% and the groove 16% low. Three miss:

- **intro**: 3 on screen is a handful (13 here); the groove's crowd starts 1.35 s early, inside the
  intro's short (5.4 s from the run's start) counted window.
- **outro** (+18%, 1.07 to 1.43): the final peak's crowd is still on screen. Native takes only out-of-sight
  enemies off the field, so a crowd on screen falls only as fast as the hero kills it, and the
  final peak's ~400 do not thin to 200 in 9 s.
- **ending** (3.5 s, -45%, 0.43 to 0.60): the crowd falls from the outro's 200 to the ending's 100
  only as fast as the hero kills, and the counts swing with whatever is on screen as the song
  ends. demo2d clears its ending with slams; native's finale blast clears it 3 s after
  the song.

## Frame time at the final peak

`--showcase-density`'s timing of every unfrozen final-peak frame (seed 1: 2065 frames, ~950 alive,
~400 on screen, measured while a live showcase ran beside it), each frame's sim and evaluation, scene and HUD build, and encode at 1920 x 1080,
in this thread's CPU time:

| | p50 | p95 | p99 | max |
|---|---|---|---|---|
| frame work | 1.73 ms | 3.41 ms | 3.85 ms | 5.52 ms |
| engine (sim, evaluate) | 0.55 | 2.16 | 2.48 | 3.61 |
| scene and HUD | 0.65 | 0.79 | 0.86 | 1.15 |
| encode | 0.47 | 0.67 | 0.78 | 3.59 |

A 60 fps frame has 16.7 ms: the worst final-peak frame used a third of it, and nothing needed
fixing (a pass on a quiet machine, before a last small fix, measured p99 2.7 ms, max 3.5). The
live headless runs of `--showcase` (Delayed listening, their rollbacks included; each played the
whole song, exit 0, finale played) logged frame work p95 1.6-2.2 ms, p99 1.8-2.6 ms through the
final peak at 920-1000 alive (the higher figures from a run beside a build).

## Known gaps

- The hero levels every 3-4 s at this crowd (39 level-ups by run 111 s, song 169 s, each a ~1.5 s
  freeze on the cards): about a third of the song is spent frozen. Compressing and flattening the
  progression is ps-8sb.3's; noted there.
- `--showcase-density` plays the virtual player again rather than the recorded keys (the level
  differs from the recording's), so it measures this level on that music, not the recorded run
  frame for frame.
