# The run: its start, freezes and the music stopping (ps-msz.6.1)

A headless File run over the survivors test playlist, `native/playlists/survivors-30.m3u`
(house-128, a 2 s gap, hiphop-92-swing, an 8 s gap, accelerando-100-140, ...), in real time on
the Mono player:

```sh
uv run --project demo3d/tools game build --platform mac --backend mono
"game/Builds/mac/mono/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile player.log --file "$PWD/native/playlists/survivors-30.m3u" --headless --seconds 175 \
  --events-out events.csv
```

[freezes.csv](freezes.csv) keeps the run's `RunStart` and `Freeze` events (`i0` the reason, 3
music stopped; `i1` 1 at a freeze's start, 0 at the resume), in song time:

| event | song time (s) | why |
| --- | --- | --- |
| start | 3.780 | the first beat 2 s after the beat turned reliable (headless: `AfterClearBeat`) |
| music stopped | 61.675 | house-128 falls silent at about 60.6 s (its RMS, 8 kHz mono: 0.063 in 60–61 s, 0.0 after); with the 2 s gap, 3 s of quiet around the analyzer's confirmed end |
| resume | 77.380 | hiphop-92-swing's beat turns reliable only at 74.2 s (below the gate before); the lock beat 74.78, downbeats 76.08 and 77.38 |
| music stopped | 126.350 | the 8 s gap after hiphop-92-swing (127 s): 3 s of quiet with its confirmed end |
| resume | 139.690 | accelerando reliable at 137.25 s; the lock beat 137.30, downbeats 138.50 and 139.69 |

Each resume lands on the second downbeat after the lock beat (the events' `Beat` rows carry the
downbeat bit), so one whole bar is heard first. The player's summary, at 175 s:

```text
run Playing from song 3.780 s, run clock 142.125 s, 2 freezes (2 music stopped), 0 run controls
```

The run clock is song time less the start and the two freezes: 174.872 - 3.780 - (77.380 - 61.675)
- (139.690 - 126.350) = 142.047 s at the last frame's frameSong; the summary's 142.125 is the
frontier's, about the 90 ms lead ahead. The .NET tests (game/Assets/Game/Tests/Sim/Run/RunTests.cs) pin the
rules: the run clock stopping in freezes and gaps, the second-downbeat resume, victory at
exactly 30:00, the ends, Easy's chickens and a replay of the run controls to its hash.

Not checked here, on the operator's list: Esc, focus loss and a click pausing and resuming with
a window, and Enter or Space starting the run.
