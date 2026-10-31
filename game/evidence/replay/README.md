# Sessions and replay (ps-msz.6.10)

Every run with music is recorded as a session, and replays to its settled hash in the player and
in .NET ([the design](../../../docs/design/unity-game.md#sessions-and-replay)). Measured on the
Mac (jarvis) on 2026-10-31, with an IL2CPP player built from this bead's tree.

## A headless run, watched at once (IL2CPP)

25 s of house-128 in the slice, headless, with two debug commands, then replayed straight away
from its session (`--watch`) through the plugin's Replay source:

```sh
uv run --project demo3d/tools game build
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode \
  -logFile "$PWD/captures/game/replay/watch.log" --file "$PWD/public/reactive/house-128/house-128.ogg" \
  --headless --seconds 25 --watch --sessions "$PWD/captures/game/replay/sessions" --debug "break@12,change@15"
```

The player exited 0 after 53 s: the run, then its replay in real time with its recorded music.

| | Run | Replay |
|---|---|---|
| Frames to song | 1441 to 24.904 s | 1437 to 24.905 s |
| Entries (late) | 1702 (0) | 1702 |
| Debug commands | 2 | 2 (recorded) |
| Killed, structures broken, zone changes | 498, 15, 2 | 498, 15, 2 |
| Settled hash | `D2557B75AA1D9A4F` (11852 events) at song 24.983 s | `D2557B75AA1D9A4F` (11852 events): matches |

The session's folder holds `audio.wav` (4.9 MB: 27.9 s of the analyzer's 44.1 kHz mono input, 3 s
beyond the run's end, since the analysis runs `M` ahead), `entries.jsonl` (1702 records, 164 KB),
`meta.json` (the plugin's), `inputs.bin` (27 KB) and `run.json`:

```json
"runtime": "IL2CPP", "level": "slice", "arena": true, "start": "AfterClearBeat",
"end": 24.983333333333334, "hash": "D2557B75AA1D9A4F", "events": 11852,
"entries": 1702, "keys": 0, "commands": 2, "controls": 0
```

## The same session replayed again

```sh
uv run --project demo3d/tools game replay captures/game/replay/sessions/20261031-080028           # .NET
uv run --project demo3d/tools game replay captures/game/replay/sessions/20261031-080028 --player  # IL2CPP
```

- **.NET** (`game/dotnet/PS.Game.Replay`): `D2557B75AA1D9A4F` (11852 events), matches, in 0.1 s.
  The IL2CPP player's session replays bit for bit in .NET 10 too, though the design checks each
  runtime only against itself.
- **The player, headless**: `D2557B75AA1D9A4F`, matches; exit 0 after 28 s.

## A changed record is caught

A copy of the session with one committed beat (t = 10.34 s) known 1 s later in `inputs.bin`
(9.218 → 10.218): the player's replay settles `B559A68C80546B10` and exits 1 (DIFFERS); .NET
reports the same hash and exits 2, since the session was recorded in another runtime. Moving a
tick's `known_at` instead (entry 500) changed nothing the run used, and the hash held.

## In the tests

- `PS.Game.Sim.Tests.SessionTests` (.NET): a live run on the slice's level fed like the client
  (house-128's dump arriving a frame at a time, ten key changes, three debug commands, the start,
  a pause and a resume) is written to a session folder and replays from the folder alone to its
  hash, every event and every entry's record equal. An analyzer stall (nothing from 6 to 8 s, then
  the backlog at once, at 240 frames a second) gives entries known late, some at the very frontier
  they arrived at, and the session still replays exactly; known at stamp + B instead, the run
  differs. A replay supplied half its entries stops where the first missing one was taken.
- `Game.Tests.LoopTests.AReplayedRunDispatchesTheSameEventsAndStopsAtItsEnd` (EditMode): the
  client's driver replays a run on frames twice as long, dispatching the same events and stepping
  no further than the run's end.
- `native/crates/plugin/tests/replay.rs`: the Replay source gives back every recorded record
  exactly, plays the recorded audio sample for sample from song time 0 at 44.1 kHz on both
  channels, and ends with it; a missing or malformed session fails `ps_open`.

Not checked here: key presses in the player (a headless run has no keyboard; the .NET and EditMode
tests cover them), and watching a replay in a window with W, which is
[the operator's row](../operator-pending.md).
