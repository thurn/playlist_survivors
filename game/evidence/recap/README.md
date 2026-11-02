# The end card from the sim's recap, and presenter faults in the summary (ps-msz.8.7)

The end card's numbers are now the sim's `RunRecap` (`SimState.Recap`): kills, and each weapon's
damage and time held, tallied as the run plays and final from its end
([the design](../../../docs/archive/design/unity-game.md#hud)). The event dispatch contains a presenter's
throws, and the run's summary always reports `presenter faults N`
([Architecture](../../../docs/archive/design/unity-game.md#architecture)).

## The end card at the peak

```sh
uv run --project demo3d/tools game look endcard --at 9,15 --evidence recap \
  --player-args "--file native/playlists/survivors-30-twice.m3u --run --bot positioning --start-minute 25 --debug finale@10"
```

- `still-009.000.jpg`: 25:05 of a run started at minute 25, the crowd drawn at the content's cap.
- `still-015.000.jpg`: the victory card. KILLS 325 matches the live counter top right and the
  summary's `325 killed`. Each weapon is held 0:06.2, from the start minute (25:00) to the
  finale (25:06): the start minute's jump of the run clock isn't time held. The summary gave
  `presenter faults 0`, and the log had no exception.

## A headless run and its replay

```sh
"game/Builds/mac/il2cpp/PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors" -batchmode -logFile run.log \
  --file native/playlists/survivors-30-twice.m3u --headless --fixed-time --run --bot positioning \
  --start-minute 25 --seconds 60 --sessions SESSIONS
uv run --project demo3d/tools game replay SESSIONS/<session> --player
```

60 s of song from minute 25 at 294 alive: 794 killed, `presenter faults 0`, hash 84259259261C372A;
the player's replay matches it (35067 events). `spike.exceptions()` on the log finds nothing; on a
summary with `presenter faults 1` or more it reports them, so `game spike`, `game replay --record`
and `game perf` fail.
