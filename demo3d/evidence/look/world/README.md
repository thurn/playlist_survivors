# The void world kit on the crystal cut (ps-wnj.8, 2026-11-01)

The crystal cut's environments drawn in the look's world kit
([neon-look.md, The Axes > World](../../../../docs/design/neon-look.md#world)):
each environment's Synty set, mist and flame lights go and the grid floor
(ps-wnj.7) takes the ground's place; a crystal kit piece stands where each of
its `Structures` pieces stood (36, 30, ... per environment, about 55 pieces),
in the preset's structure look; the formations draw in that look's featured
material and break as before. The shots are not re-staged yet (ps-wnj.17):
hordes, paths and framing are the Synty environments'.

```sh
# IL2CPP Mac player of this change, windowless
PlaylistSurvivors3D.app/Contents/MacOS/Playlist\ Survivors\ 3D\ Demo -batchmode -force-metal --fixed-time \
  --trailer crystal --capture-stills DIR --stills-format png --capture-mask \
  --stills-at 4,12,20,28,36,44,52 --seconds 53 --look edge   # solid, hologram, prism; before: --look edge --world today --structure-look today
uv run --project demo3d/tools demo3d look-metrics DIR --palette PALETTE.json --preset PRESET --out metrics-PRESET
dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- trailer --name crystal --checks
```

- `crystal-PRESET.jpg`: each moment's before (the post stack and palette of
  the preset, the Synty environments, today's formations) above the
  preset's world. Every world option draws: `void-grid` (edge), `hued-zones`
  (solid, prism: each shot takes the palette's next zone tint on its cut) and
  `pure-void` (hologram).
- `formation-break.jpg`: a formation's break (shot 2, 9.40-9.55 s), cropped,
  before above edge: whole until the blow, its shards flashing white on the
  impact frame, then the burst. The timing is the same frame for frame: the
  break is still demo3d's `StructureShatter`, only the formation's material
  changed.
- `metrics-PRESET/`: the style guide's rules on the seven stills with their ID
  renders and view json.

## The formations keep their glow

A first pass drew the formations in the props' own edge-glass: dim outlines,
no brighter than the props, losing the white-gold glow viewers liked most.
They now draw in `StructureLook.Featured` (faces at least half the gem, rim
and edges brighter), the style guide's crystal gem on the hero's rung, so they
tower over the props in every preset. A first pass also stood props on the
environments' landmarks; their wide footprints buried the formations in
crystal (300+ pieces a shot), so only the `Structures` pieces get props.

## What the world moves

| rule | before | edge | solid | hologram | prism |
| --- | --- | --- | --- | --- | --- |
| floor p50 <= 8 | 2.9-12.5, 2 fail | 1.4-9.3, 1 fail | 0.9-6.1 | 1.2-7.8 | 1.2-6.9 |
| floor p90 <= 30 | 13-23 | 11-22 | 8-17 | 10-20 | 9-20 |
| black share | 14-50% | 27-61% | 35-66% | 29-62% | 31-64% |
| shot floor range <= 6 | 9.65 | 7.86 | 5.21 | 6.68 | 5.75 |

The lit floors of the lava and finale shots (20 and 36 s) are gone: the floor
rules that failed there pass but for edge's 20 s still (9.3, the dense lava
crowd's halo between bodies), and the shots' floors sit closer together.
Bright share, halo, key, form and separation stay the stand-in monsters'
(ps-wnj.10-.13).

## Checks

- `trailer --name crystal --checks`: ok (every crystal breaks on screen, open
  lane, liveness): the timeline is unchanged.
- The action cut ignores the look (`DemoMain: the look flags are for the crystal
  cut; the action cut keeps its look`): no VoidArena, its formations-free
  `StructureShatter` path and Synty environments as before.
