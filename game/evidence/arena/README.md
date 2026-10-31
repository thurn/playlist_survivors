# The arena (ps-msz.3.6)

The 240 m bounded night arena ([unity-game.md, The Arena](../../../docs/design/unity-game.md#the-arena)):
generated in batch mode from game/Content/arena-layout.json, lit like the reference look, its crystal
structures in hues across the spectrum. Windowless stills from the game camera's framing (55° pitch,
30° field of view, 30 m across the ground), beside the 3D demo's reference command.

## How they were made

```sh
uv run --project demo3d/tools game setup
uv run --project demo3d/tools game unity BakeAll        # the arena's scene and content/arena.json
uv run --project demo3d/tools game look arena --evidence arena --at 1,3,5,7,9,11,13,15 \
  --player-args "--arena --arena-tour 2"                # IL2CPP player, fixed time, batch mode
```

The reference stills are demo3d's player (the main checkout's build of 2026-10-30) with the reference
command's flags, `--monster-style crystal --palette neon --structure-style crystal` (`--structure-breaks
walls` is refused by the stress scene), in stress scene S3 at its beauty shots, empty (`--beauty-empty`)
and with the horde, at t = 20 s, `--fixed-time --no-post-feel`; and the song at 25 s.

## The arena's stills

The tour holds each of the layout's `shots` for 2 s, from the hero's start:

| still | shot | what it shows |
|---|---|---|
| [1 s](still-001.000.jpg) | heart, the hero's start | walls and ruins in green, yellow, magenta, violet and teal around the rune circle and the hero's cyan pool |
| [3 s](still-003.000.jpg) | the courtyard | its four walls in red, the hero's light inside |
| [5 s](still-005.000.jpg) | the glowing tree | the west glade's landmark (kit, baked glow) and a green crystal ruin |
| [7 s](still-007.000.jpg) | the graveyard | candles' flame lights, teal crystal walls |
| [9 s](still-009.000.jpg) | the brazier ring | four braziers and a pyre, flickering, near the east wall |
| [11 s](still-011.000.jpg) | the south gate | a blue ruin wall with its gap |
| [13 s](still-013.000.jpg) | the north keep | yellow ruins |
| [15 s](still-015.000.jpg) | a forest edge | the impassable forest bounding a clearing |

The reference: [courtyard](reference/courtyard-empty.jpg) ([horde](reference/courtyard-horde.jpg)),
[gate](reference/gate-empty.jpg) ([horde](reference/gate-horde.jpg)),
[glowing tree](reference/glowing-tree-empty.jpg) ([horde](reference/glowing-tree-horde.jpg)),
[opening](reference/opening-empty.jpg) ([horde](reference/opening-horde.jpg)), [song at 25 s](reference/song-025.jpg).

## Against the reference's lighting

Full-size stills, CIE L* and C*ab over the whole frame (demo3d_tools.look's `still_stats`), and the
median HSV hue (the ground's, which fills most of the frame):

| set | L* p50 | L* p98 | contrast | chroma | near-black (L* < 10) | hue |
|---|---|---|---|---|---|---|
| reference, empty shots (4) | 3.4 - 8.0 | 28 - 58 | 7.5 - 13.7 | 23 - 26 | 57 - 79% | 243 - 257° |
| the arena's tour (8) | 4.5 - 5.9 | 26 - 63 | 6.7 - 16.4 | 19 - 26 | 64 - 75% | 243 - 257° |

The same dark violet world: the ground's value and hue, the cyan pool of the hero's light, the magenta
mist and the rune circles match. The differences are the design's:

- **Rainbow crystal**: the reference's structures are one sapphire; the game gives each site its own
  hue by its angle around the arena's centre (CrystalStructures.Hue).
- **Dark forest**: the reference's `--structure-style crystal` also turns trees and landmarks to crystal;
  the game's crystal is its breakable structure sites only, the forest and landmarks stay the dark kit.
- The forest-edge shot (15 s) is darker and less chromatic: mostly trees and the edge band.

## Numbers

- **Bake**: 5,359 placements, 5,361 renderers, 1.65 M triangles, 11 converted materials, 8 flame
  lights; 24 s, of which the ground's lightmap (one 2048² map at 5 texels per metre over 300 m) 20 s.
- **Level data** (game/Content/arena.json): 160 × 160 tiles of 1.5 m; 7,743 open (17,400 m²),
  17,692 blocked, 38 structure sites (8 large, from 6 tiles). Unity's bake and the .NET export write
  byte-identical files (ArenaLevelTests checks the committed one against the export).
- **Load** (IL2CPP, Apple silicon): the static shadow map, 4096² over the arena (8.7 × 6.2 cm texels),
  of 5,361 casters in 13 ms; 1,922 light probes; 92 structure renderers swapped to crystal (3
  materials).
