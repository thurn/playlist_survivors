# The timing log (ps-sba)

Every recorded run writes `timing.csv` into its session folder, one row per frame: its interval,
the main thread's CPU time, the loop's and the sim's work, Unity's frame timing, frameSong, the
lead, the hero and the frame's share of the run's counts. `game log` summarises it
([the design](../../../docs/archive/design/unity-game.md#sessions-and-replay)). Measured on the Mac
(jarvis) on 2026-10-31 with an IL2CPP player built from this bead's tree.

## A real-time run at the peak

45 s of go-cart at minute 25 with three evolutions and a structure break at 30 s. Batch mode with
Metal, real time (60 fps paced, muted), recorded with `--sessions`:

```sh
uv run --project demo3d/tools game build
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -force-metal \
  -logFile "$PWD/captures/game/timing/player.log" \
  --file "$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg" \
  --sessions "$PWD/captures/game/timing/sessions" --start-minute 25 --bot positioning \
  --debug "evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@30" --seconds 45
uv run --project demo3d/tools game log captures/game/timing/sessions/<time>
```

The session holds `timing.csv` beside `audio.wav`, `entries.jsonl`, `inputs.bin`, `meta.json` and
`run.json`: 2683 rows, 360 KB for 45 s (about 30 MB for a 30-minute run at 120 fps, a tenth of its
`audio.wav`). [log.txt](log.txt) is `game log`'s summary: intervals p50/p95/p99 16.7/17.5/17.6 ms,
the one hitch the 590 ms boot frame before the music, the main thread at 1.4 ms (p50), the sim at
0.3 ms (p99) with 300 alive, 0 late entries, 0 dropped sounds, 145 key changes from the bot (the
session records 145), and a table per second of song.

`render_ms` and `gpu_ms` are -1 on most of a batch-mode player's frames (Unity times only those
it draws). They are filled on a windowed run, which agents don't open (see the operator list).

## No garbage, no measurable cost

The same run with `--profile-out`, with the log and without (no `--sessions`): the allocation
census differs only in gameplay (one run's effects cue array grew once more, and the structure break's
shards), with nothing tagged to the loop or engine that the log would make: no strings, char
buffers or frame timings. The main thread's median frame was 1.31 ms with the log and 1.60 ms without:
run-to-run noise, not the log's cost.

## Replayed with the output flags

What the sim did frame by frame comes from replaying the session, which takes the `--*-out` flags:

```sh
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -nographics \
  -logFile "$PWD/captures/game/timing/replay.log" --replay captures/game/timing/sessions/<time> \
  --headless --fixed-time --loop-out captures/game/timing/replay-loop.csv \
  --moments-out captures/game/timing/replay-moments.csv
```

It matched the recorded hash (`26A0BCFF72F97708`, 16946 events) and wrote 2664 frames of
`--loop-out` and 452 moments.

## The Warden's re-run (ps-087)

`cpu_ms` was measured from the end of one frame's Update to the end of the next, so the hitch a
heavy Update caused showed that Update's loop time beside CPU time that left it out. It now runs
from one Update's start to the next, the interval's span. Re-run as above for 25 s with the break
at 15 s, from the reviewed tree with that fix: 1488 rows, intervals p50/p95/p99 16.7/17.6/17.6 ms,
the main thread at 1.5 ms (p50), the boot frame's 623 ms interval with its 121 ms of CPU as the
first hitch, 0 late entries, 0 dropped sounds, and `gpu_ms` on 46 of its frames (`render_ms` on none).
