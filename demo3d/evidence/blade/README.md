# The hero's crystal blade

Stills of `HeroBlade` (crowd.md, The Hero) at its defaults, from the Mac's
IL2CPP player, windowless and in fixed time (2026-10-30), after the grip
pass: the grip from the hand's bones, the 1.15 m blade, the shield at the
character's scale and the sword idle's arms over the walk.

- `game-view-100s.jpg`: the song's own camera at 100.2 s, the forest.
- `close-up-60s.jpg`: `--view-width 7` at 60 s: the blade raised out of
  the fist, the shield on the forearm.
- `combo-strip-60s.jpg`: `--view-width 8`, 60 to 61.8 s every 0.2 s,
  left to right then the second row: a combo's swings.
- `walk-hold-200s.jpg`: `--view-width 8` at 200.5 s, walking at the song's
  end: the sword idle's ready stance over the walk's legs.
- `neon-60s.jpg`: `--environment neon` at 60 s.

To reproduce (from a worktree's root, after `demo3d build`):

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile "$PWD/demo3d/Logs/blade.log" \
  --fixed-time --view-width 7 --capture-stills "$PWD/captures/blade" --stills-at 60 --start-at 56 --seconds 5
```

`--view-width` changes the timeline (the horde is placed for the wider
view), so the hero's moves at a time differ between widths. Add
`--blade-tune pitch=10,palm=0.03,length=0.9` (keys in crowd.md) to judge a
change without a rebuild. Paths must be absolute: the player doesn't resolve
them against the shell's directory.
