# The hero's crystal blade

Stills of `HeroBlade` (crowd.md, The Hero) at its defaults, from the Mac's
IL2CPP player, windowless and in fixed time (2026-10-30):

- `game-view-100s.jpg`: the song's own camera at 100.2 s, the forest. The
  broadsword it replaces drew about 1 cm long here, so the hero looked
  empty-handed.
- `close-up-60s.jpg`: `--view-width 7` at 60 s, mid-swing: the cut facets,
  the light down the spine, the crossguard's points and root shards.
- `neon-60s.jpg`: `--environment neon` at 60 s.

To reproduce (from a worktree's root, after `demo3d build`):

```
"demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo" \
  -batchmode -force-metal -logFile "$PWD/demo3d/Logs/blade.log" \
  --fixed-time --view-width 7 --capture-stills "$PWD/captures/blade" --stills-at 60 --start-at 56 --seconds 5
```

Add `--blade-tune glow=1.6,behind=0.3` (keys in crowd.md) to judge a change
without a rebuild. Paths must be absolute: the player doesn't resolve them
against the shell's directory.
