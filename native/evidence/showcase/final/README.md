# Showcase growth and finish (ps-8sb.3)

The showcase's progression, compressed for a 3:19 run, and a pass over the whole song's crowd.

```sh
cd native
cargo run --release -p app -- --showcase --headless                    # records logs/sessions/<id>
cargo run --release -p app -- --showcase-arsenal logs/sessions/<id>    # arsenal.json, these stills
cargo run --release -p app -- --showcase-density logs/sessions/<id>    # density.json
```

`--showcase-arsenal` replays the session's music on the showcase level with the virtual player, as
`--showcase-density` does (five player seeds). For each section it records the level reached, the
level-ups, the chests opened, the song time frozen on cards or a chest, and the arsenal at its end.
It draws the middle of drop-1, groove-2, drop-2 and the final peak (seed 1). These are from
session 1793392222, a `--showcase --headless` run of the final level: exit 0, the finale played
(run over at 198 s), health 1.00 at the stills and the finale, 25 level-ups, three chests (song
20.5, 92.1 and 129.7 s).

## The problem

At demo2d's crowd (up to ~1000 alive) native's hero levels every 3 to 4 s. Before this bead the same
song ran 39 level-ups and 66 s frozen on cards (a third of the song), 17 s of it in drop-1, and
reached the final peak at level 40 with four maxed weapons and no evolution (the Mad Forest's
chests evolve only at minutes 10 to 25).

## What changed

- **`xpScale` per schedule row** (new, sim): the share of each gem's experience the hero takes while
  the row plays (1 when absent; every other level is unchanged). The showcase takes 0.5 in the
  intro, 0.2 to 0.3 in the grooves and the drops, 0.7 in the breakdown, the build and break-2,
  and 0 from the final peak on.
- **`evolves` on a treasure** (new, sim): its chest may evolve a weapon whatever its minute.
- **Three tier-3 chests** (five items each) on vs-bat4 at the starts of the groove, the breakdown
  and groove-2; the last two evolve. The hero takes a chest only when clear of the crowd, so each
  lands a section or so after its boss: one at the start (on drop-1's doorstep), the other two by
  drop-2. (Chests spawned at drop-1 and drop-2 landed as late as the outro in live runs, and a
  final-peak chest would freeze the climax for two bars, so they moved earlier.)
- **Crowd retuned for the stronger hero**: the cap rises from 1000 to 1200 (the final peak sat at
  the cap, 336 on screen of 384); the breakdown asks 150 on screen and the outro 225 (demo2d's 120
  and 200 measured about 85 and 150 once the hero had a maxed Sword and three evolutions).

## Growth per section (seed 1)

| Section | Song s | Level | Level-ups | Chests | Frozen s | Gained |
|---|---|---|---|---|---|---|
| intro | 0.0 | 1 | 0 | 0 | 0.0 | Sword Spin |
| groove | 12.4 | 5 | 4 | 1 | 8.4 | Magic Wand, Empty Tome, Spinach, Hollow Heart; the chest: Sword 4, Wand 3 |
| break-1 | 34.1 | 5 | 0 | 0 | 0.0 | |
| drop-1 | 37.2 | 10 | 5 | 0 | 8.2 | Sword 8, Garlic |
| breakdown | 74.3 | 15 | 5 | 0 | 6.1 | Garlic 4, Pummarola |
| groove-2 | 86.7 | 18 | 3 | 1 | 8.2 | **Bloody Tear**, Wand 7, Garlic 8 |
| build | 111.5 | 20 | 2 | 1 | 4.9 | **Holy Wand**, **Soul Eater**, passives 2 |
| break-2 | 120.8 | 21 | 1 | 0 | 1.5 | Hollow Heart 3 |
| drop-2 | 123.9 | 26 | 5 | 0 | 8.7 | King Bible 3, Lightning Ring 2 |
| **final-peak** | 161.0 | 26 | **0** | **0** | **0.0** | (plays Bloody Tear, Holy Wand, Soul Eater, King Bible 3, Lightning Ring 2; Empty Tome 2, Spinach 2, Hollow Heart 3, Pummarola 2) |
| outro | 185.8 | 26 | 0 | 0 | 0.0 | |
| ending | 195.1 | 26 | 0 | 0 | 0.0 | |

Over the five seeds: 24 to 25 level-ups, three chests every time (the last by song 122), 45.4 to
46.0 s frozen (against 66 s before), level 25 to 26 at the final peak, and the same late arsenal:
three evolutions beside King Bible 2-3 and Lightning Ring 2. Nothing freezes from the final peak on.

| Still | Moment |
|---|---|
| `04-drop-1.png` | drop-1's middle, level 8 |
| `06-groove-2.png` | groove-2's middle, level 16 |
| `09-drop-2.png` | drop-2's middle, level 23, three evolutions |
| `10-final-peak.png` | the final peak's middle, level 26, ~400 on screen |

## The whole song's crowd (density.json)

`--showcase-density` on the same session: on screen against demo2d, the mean over five seeds.

| Section | demo2d | Seed 1 on screen | Ratio, 5 seeds: mean (range) | Alive |
|---|---|---|---|---|
| intro | 3 | 20 | 5.64 (4.44-6.34) | 62 |
| groove | 60 | 52 | 0.86 (0.82-0.89) | 179 |
| break-1 | 60 | 51 | 0.95 (0.85-1.11) | 273 |
| drop-1 | 160 | 161 | 1.00 (0.98-1.01) | 501 |
| breakdown | 120 | 120 | 1.05 (0.83-1.44) | 464 |
| groove-2 | 200 | 187 | 0.96 (0.94-1.01) | 658 |
| build | 200 | 206 | 0.96 (0.79-1.03) | 706 |
| break-2 | 160 | 195 | 1.14 (0.49-1.54) | 534 |
| drop-2 | 320 | 339 | 1.05 (1.01-1.06) | 1045 |
| **final-peak** | 384 | 347 | **0.96 (0.90-1.06)** | 1179 |
| outro | 200 | 230 | 1.06 (0.85-1.15) | 635 |
| ending | 100 | 114 | 0.88 (0.47-1.14) | 383 |

Every section but the intro is within 15% of demo2d on average (the ending was 45% low, the outro
18% high before). The intro's handful (3) is about 20: the groove's crowd and its chest's bat lead
into the intro's short counted window. The short breaks (break-1, break-2: 3 s each) and the
ending swing most between seeds.

## Frame time

The final peak's frames (seed 1, 2733 frames, ~1180 alive, ~350-400 on screen), each frame's sim
and evaluation, scene and HUD build, and encode at 1920 x 1080 in this thread's CPU time:
frame work p50 2.07 ms, p95 4.05, p99 4.56, max 5.37 (60 fps allows 16.7 ms). The live headless
run logged frame work p95 at most 2.5 ms and p99 at most 3.4 ms over the whole song, Delayed
listening's rollbacks included.

## Known gaps

- About 45 s of the song is still frozen on cards and chests, mostly in the grooves, the drops and
  the breakdown. Fewer would mean a thinner arsenal or a change to native's card timing (each
  level-up holds about 1.5 s: three cards revealed on beats, the pick a beat later).
- A chest lands only once the hero is clear of the crowd, so when each lands varies by run (the
  last between song 104 and 130 here).
