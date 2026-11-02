# The neon action cut (ps-wnj.26, 2026-11-01)

The action cut on the look package, re-staged in the void world
([trailer.md, The Neon Action Cut](../../../../docs/archive/tech-demo-3d/trailer.md#the-neon-action-cut);
[neon-look.md, The Trailer](../../../../docs/archive/design/neon-look.md#the-trailer)).
`--trailer` with any look flag plays `trailer-action-neon.json`; plain
`--trailer` is still the Synty cut.

```sh
# the timeline: checks
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --name action-neon --checks
# IL2CPP Mac player of this change, windowless, per preset (edge, solid, hologram, prism), and plain (no --look)
PlaylistSurvivors3D.app/Contents/MacOS/Playlist\ Survivors\ 3D\ Demo -batchmode -force-metal --fixed-time \
  --trailer --look PRESET --capture-stills DIR --stills-format png --capture-mask \
  --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET --out metrics-PRESET
# the drop's cut and the dragon's sweep (trailer seconds)
uv run --project demo3d/tools demo3d filmstrip action-neon-drop-solid --at 2.43 --frames 12 --every 3 --player-args "--trailer --look solid" --evidence look/action-neon
uv run --project demo3d/tools demo3d filmstrip action-neon-dragon-solid --at 55.6 --frames 12 --every 3 --player-args "--trailer --look solid" --evidence look/action-neon
# the videos: captures/trailer-action-PRESET.mp4 (60.5 s each, mixed)
just trailer-looks action
```

## Files

- `stills-PRESET.jpg`: one or two windowless stills per shot in each preset
  (1.6 s the duel; 4 and 12 s shot 2's charge; 20 s shot 3's lava streams;
  28 and 36 s shot 4's vortex; 44 s shot 5's charge; 47.2 s shot 6; 50.2
  and 52 s shot 7's streams; 56.3 s the dragon). Every shot sits square to
  the one grid on the void's open ground; under `hued-zones` (solid, prism)
  each takes its zone's tint.
- `plain-vs-neon.jpg`: the same moments, the plain action cut (Synty, its
  old headings) above the neon cut in solid. Each shot keeps its moment: the
  formations' shapes, the vortex holes, the dragon's ring; the neon cut turns
  each into yaw 0.
- `action-neon-drop-solid.jpg`, `action-neon-drop-edge.jpg`: the drop's cut
  (81.1, 2.481 s) every third frame: the slam's rings and the impulse wipe
  run out from the hero across the floor in the incoming zone's colour.
- `action-neon-dragon-solid.jpg`: the dragon's sweep landing (115.3, 55.90
  s): its telegraph closes, the impact frame is the special-moment flash,
  gone within two frames.
- `metrics-PRESET/`: the style guide's rules on the stills.

## What the metrics say

| rule | edge | solid | hologram | prism |
| --- | --- | --- | --- | --- |
| shot floor range <= 6 | 0.96 | 0.43 | 1.02 | 1.17 |
| cut key60 median >= 0.62 | 0.67 | 0.60 | 0.56 | 0.38 |
| flash frames <= 0.5 | 0.00 | 0.00 | 0.00 | 0.00 |

One world across the shots in every preset, and no steady still flashes.
The stills and captures are of staging with ps-wnj.9's hero looks, so the
hero's margin passes in every still. The rules still broken are the crystal
cut's (restage/README.md): halo, swarm separation (the monster looks),
prism's key (its spectrum palette), and `bright_share_min` in the sparsest
stills (the duel; 52 s in hologram and prism).

## Checks

- `trailer --name action-neon --checks`: ok. View widths 20, 44, 47, 42, 36,
  34, 36, 34 m; the worst bar under dotnet is 0.87 of its target (shot 2, bar
  87), within the 15% tolerance.
- `dotnet test` (Demo3D.Choreography.Tests): the trailer tests pass, the new
  `TheNeonActionCutIsTheActionCutInTheVoid` included (every check, and each
  shot the action cut's).
- Unity EditMode (`demo3d test`): 533 of 536 pass, 3 skipped, none fail; the
  new test passes under Unity's floats too.
- **The plain action cut is unchanged.** Plain `--trailer` stills from this
  change against a player built at its base (069d154f, this change's player
  code and trailer file set aside): the masks byte-identical, the stills
  within the GPU's run-to-run noise. Over the eleven stills 23,781 pixels
  differ, 3 of them by more than 8 of 255 (at most 16); two runs of the base
  player differ in 24,156 pixels, none by more than 8.
