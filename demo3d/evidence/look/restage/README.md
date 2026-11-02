# The crystal cut re-staged in the void (ps-wnj.17, 2026-11-01)

The crystal cut's shots moved from their Synty environments onto the void's
open ground, with designed transitions and one camera grammar
([trailer.md, The Crystal Cut](../../../../docs/tech-demo-3d/trailer.md#the-crystal-cut);
[neon-look.md, The Trailer](../../../../docs/design/neon-look.md#the-trailer)).

```sh
# the timeline: checks, and the view sheets' top-down frames (every 4 beats)
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --name crystal --checks
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --name crystal --frames DIR --every 4
# IL2CPP Mac player of this change, windowless, per preset (edge, solid, hologram, prism)
PlaylistSurvivors3D.app/Contents/MacOS/Playlist\ Survivors\ 3D\ Demo -batchmode -force-metal --fixed-time \
  --trailer crystal --look PRESET --capture-stills DIR --stills-format png --capture-mask \
  --stills-at 1.6,4,12,20,28,36,44,47.2,50.2,52,56.3 --seconds 57
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET --out metrics-PRESET
# the transitions (trailer seconds)
uv run --project demo3d/tools demo3d filmstrip restage-flash-solid --at 2.465 --frames 8 --every 1 --player-args "--trailer crystal --look solid"
uv run --project demo3d/tools demo3d filmstrip restage-drop-solid --at 2.43 --frames 12 --every 3 --player-args "--trailer crystal --look solid"
uv run --project demo3d/tools demo3d filmstrip restage-soft-solid --at 45.80 --frames 12 --every 3 --player-args "--trailer crystal --look solid"
```

## Why re-stage

ps-wnj.8 hid each environment's Synty set, but the shots still simulated on
their layouts: the horde parted round trees, landmarks and walls that were no
longer drawn, leaving big black holes in the swarm, and some frames reached
the arena's edge. See its stills at 12, 28 and 44 s
([world/crystal-edge.jpg](../world/crystal-edge.jpg): those moments are in
`stills-PRESET.jpg` here too, so the two compare directly).

## Files

- `views-shotN.jpg`: each shot's simulation from above every 4 beats from its
  cut, before (its Synty layout: the forest's trees and rocks, the
  structures, the black past the arena's edge, the camera's turned
  footprint) above after (the void's open ground, one heading). The yellow
  outline is the camera's view, the white dot the hero, the yellow circles the
  layout's named goals (the void's one is its camp); the formations aren't
  drawn (the stills show them). The hordes keep their shapes; nothing
  but the formations parts them, and no frame reaches the edge.
- `stills-PRESET.jpg`: one or two windowless stills per shot (1.6 s is the
  duel; then 4 and 12 s shot 2, 20 s shot 3, 28 and 36 s shot 4, 44 s shot 5,
  47.2 s shot 6, 50.2 and 52 s shot 7, 56.3 s the finale), in each preset.
  Every shot sits square to the one grid; under `hued-zones` (solid, prism)
  each takes its zone's tint.
- `restage-flash-solid.jpg`: every frame through the drop's cut (81.1, 2.481
  s): the cut frame floods with the incoming zone's hue (arcade's magenta, the
  palette flash) and it decays within an eighth note, under the cut's slam.
- `restage-drop-solid.jpg`, `restage-drop-edge.jpg`: the same cut every third
  frame: the impulse wipe's ring runs out from the hero across the floor in
  the zone's colour (edge: one key, `grid.pulse`), past the slam's own ring.
- `restage-soft-solid.jpg`: a cut within a section (109.1, 45.836 s): a hard
  cut with a soft wipe, no flash.
- `metrics-PRESET/`: the style guide's rules on the stills with their ID
  renders and view json.

## What the metrics say

| rule | ps-wnj.8 (Synty layouts) | re-staged |
| --- | --- | --- |
| shot floor range <= 6 | edge 7.86, solid 5.21, hologram 6.68, prism 5.75 | 3.59, 1.33, 2.82, 2.66 |
| cut key60 median >= 0.62 | 0.58, 0.58, 0.55, 0.44 | 0.74, 0.65, 0.63, 0.37 |
| floor p50 <= 8 | 0.9-9.3, edge fails once | 0.0-3.6, all pass |
| floor p90 <= 30 | 8-22 | 0.3-14 |

The shots' floors now sit together in every preset ("one world across the
shots, not stitched parts"), and the cut keys in edge, solid and hologram.
What still breaks belongs to other beads: the hero's margin (every still: the
hero is a tiny figure, ps-wnj.9), halo and swarm separation (the monster
looks), prism's key (its spectrum palette's hues). The duel's still (1.6 s)
breaks `bright_share_min`: three elites alone in the void.

## Checks

- `trailer --name crystal --checks`: ok, the camera grammar's new check among
  them. Every formation smashes at the old cut's moment (4.80 to 57.45 s).
- `dotnet test` (Demo3D.Choreography.Tests): the trailer tests pass, the new
  `AShotPlaysOnItsOwnLayout` and `AVoidShotKeepsTheCameraGrammar` included.
- Unity EditMode (`demo3d test`): 522 of 528 pass, 6 skipped, none fail. A
  first run failed shot 5's first bar under Unity only (251 of 300 on screen
  at 36 m, 269 under dotnet), so shot 5 frames 38 m, and shot 2 44 m for its
  margin (trailer.md, The camera grammar).
- The action cut's checks are unchanged (`trailer --name action --checks`: ok).
