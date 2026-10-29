# PX6: the arena (2026-10-29)

The Ancient Forest arena: `Data/arena-layout.json` (demo3d's layout format plus ground materials,
a cliff ledge, a stream and its bridge) and `Data/forest-props.json` (the 2D kit), generated at load
by `ForestArena` and `ArenaTiles` for both the sim and the client. Every run was on the operator's
Mac, windowless (batch mode, Metal), from the worktree's root:

```
uv run --project demo3d/tools demo2d setup
uv run --project demo3d/tools demo2d unity ArenaStills -out "$PWD/demo2d/evidence/px6"   # overview and shots
uv run --project demo3d/tools demo2d build
APP="$PWD/demo2d/Builds/mac/il2cpp/PlaylistSurvivors2D.app/Contents/MacOS/Playlist Survivors 2D Demo"
"$APP" -batchmode -force-metal -logFile "$PWD/demo2d/Logs/px6.log" \
  --capture-stills "$PWD/demo2d/evidence/px6" --stills-at 8,116,134,172.5
```

## The arena without the horde (ArenaStills)

- [`overview.jpg`](overview.jpg): the whole 120 × 120 m arena in daylight at a quarter of the
  grid's size. The route runs north: the camp glade with the old stump, a dirt path to the moss
  clearing, the stream (a sunken channel 26 m long) and its stone bridge, the ledge's foot under the
  cliff, and the great clearing with the great dead tree. Forest everywhere else: swaying trees,
  dead trees, stumps and fallen branches, undergrowth on the edges.
- [`shot-stone-bridge.jpg`](shot-stone-bridge.jpg), [`shot-cliff-ledge.jpg`](shot-cliff-ledge.jpg),
  [`shot-great-clearing.jpg`](shot-great-clearing.jpg): the layout's shots at the grid's size under
  the moon (the 2D renderer's global light only; PX7 lights the night).

## Ground level, with the horde (the IL2CPP player)

1920×1080 from the game camera, with the player's counts:

- [`still-008.000.jpg`](still-008.000.jpg): the intro at the camp glade, 3 on screen (+1 dying).
- [`still-116.000.jpg`](still-116.000.jpg): the build, 169 on screen (+15 dying) at 34 m. The hero
  has just crossed the bridge; the horde behind him funnels over its deck, the rest still on the
  south bank.
- [`still-134.000.jpg`](still-134.000.jpg): drop-2, 236 on screen (+19 dying) at 36 m: past the
  ledge's foot, the cliff at top right, the stream and bridge below.
- [`still-172.500.jpg`](still-172.500.jpg): the final peak in the great clearing, the ledge at the
  frame's right edge.

## Checks

- `dotnet test demo2d/dotnet/Demo2D.Choreography.Tests`: 27 of 27, 6 of them PX6's `ArenaTests`:
  two generations give identical placements (2,090, 693 blocking), ground and tiles (218 distinct
  sheet tiles); scatter keeps out of the water, off the cliff and (when it blocks) off fighting
  ground; the terrain blocks where it's drawn and the bridge lets the hero over; **no one stands in
  a tree, the cliff or the stream** (851,859 samples at every beat and 0.1 and 0.2 s after it, the
  deepest 0.022 m inside a radius against the 0.05 m tolerance); the hero crosses only by the bridge
  and reaches the great clearing at 147.1 s, before the final peak (161 s).
- The level script's counts hold on the arena (`SectionsHoldTheirOnScreenTargets`): groove 62 of
  60, drop-1 165 of 160, groove-2 203 of 200, build 192 of 200, drop-2 282 of 320, the final peak
  392 of 384 at 40.5 m. The song builds in 0.6 s.
- `demo2d test`: 84 of 84 EditMode tests, including PX5's seek test (now with the swaying and
  fading props).
- demo3d: its 129 dotnet tests pass and its song dump is still
  `09945e3b0a2932713c6d4fd00743bc81bc3cf648241b9cd961a60c804e1cdadd` after the core's `advanceM`.
