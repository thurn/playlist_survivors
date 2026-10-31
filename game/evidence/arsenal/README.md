# The arsenal's core: gems, experience and the level-up (ps-msz.6.4)

## A headless run reaches level-ups and resumes on the next beat

A headless File run of the survivors test playlist on the Mono player, in real time:

```sh
uv run --project demo3d/tools game build --platform mac --backend mono
"game/Builds/mac/mono/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile player.log --file "$PWD/native/playlists/survivors-30.m3u" --headless --seconds 120 \
  --events-out events.csv
```

[headless-levelups.csv](headless-levelups.csv) keeps the first 25 s of its `GemFlight`, `Pickup`,
`LevelUp` (`i0` the level, `i1` the offer, `i2` the three cards packed), `Card` (`i0` the offer,
`i1` the card, `i2` 1 confirmed) and `Freeze` (`i0` 0 the level-up, `i1` 1 start / 0 resume) rows.
Nobody plays a headless run, so it takes each offer's selected card through `AutoCards`, a
recorded Enter half a second after the third card flips in.

| event | song time (s) | |
| --- | --- | --- |
| run start | 3.780 | headless: after 2 s of clear beat |
| level 2 | 13.973 | its offer opens and the world freezes |
| card taken | 15.042 | the first card, magic wand |
| resume | 15.500 | the next beat (house-128, 0.469 s a beat) |
| level 3 | 19.600 | |
| card taken | 20.675 | |
| resume | 21.120 | the next beat |

The player's summary at 120 s: `run Playing from song 3.780 s, run clock 97.518 s, 3 freezes
(1 music stopped)`, 1437 killed, 1071 gems dropped. The hero stands still, so only the 13 gems
that fell within its 4.4 m pickup radius flew in.

## Stills

Windowless fixed-time stills of the same playlist (`game look`), the slice's level:

```sh
uv run --project demo3d/tools game look arsenal-flight --build --evidence arsenal/flight \
  --at 12.2,12.4,12.6,12.8,13.0 --strip 5 --crop 610,290,700,500 \
  --player-args "--file $PWD/native/playlists/survivors-30.m3u"
uv run --project demo3d/tools game look arsenal-cards --evidence arsenal/cards \
  --at 11.5,13.4,14.3,15.0 --player-args "--file $PWD/native/playlists/survivors-30.m3u"
```

- [flight/arsenal-flight.jpg](flight/arsenal-flight.jpg): around the hero from 12.2 s, the gems
  that took off at 12.225 s kicked outward and pulled in to it (they arrive at 13.03 s, which
  levels the hero up).
- [cards](cards/): 11.5 s, crystal gems lying where the sword's kills fell; 13.4 s, level 2's first
  card flipped in over the frozen run; 14.3 s, all three (magic wand, garlic, sword 1 to 2), the
  first selected; 15.0 s, play again after the resume at 14.56 s.

Blue gems (values 1 and 2) read pale cyan under the night post's bloom; green (3+) and red (10+)
gems come with the run's tougher roles.

## The tests

game/Assets/Game/Tests/Sim/Arsenal/ArsenalTests.cs: native's offers at every level from 2 to 70
along two pick paths (from native's own `offer_table().cards`), the cards flipping in on beats and
resuming on the next beat, held keys, chained levels, each passive's stats, gems and Magnet, the
cap, the experience curve, and shots on the grid:

```text
70 BPM garlic rank 1, interval 1.300 s: 461 shots in 600 s, expected 461.5
70 BPM holy-wand rank 1, interval 0.300 s: 1999 shots in 600 s, expected 2000.0
128 BPM garlic rank 8, interval 0.600 s: 1000 shots in 600 s, expected 1000.0
174 BPM holy-wand rank 1, interval 0.300 s: 2000 shots in 600 s, expected 2000.0
```

Pacing, reported: the slice's horde (220 alive from the start) in an open arena, the hero walking
a square through its kills, reaches level 33 by 5:00 of run clock, far faster than native's run
(about level 10 by minute 5). The run's schedule (ps-msz.6.2) is what to measure that against.

Not checked here, on the operator's list: choosing and taking cards with the keys in a window.
