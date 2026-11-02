# The hero looks, on the crystal cut (ps-wnj.9, 2026-11-01)

The same hero looks as [game's](../../../../game/evidence/look/hero/README.md)
on the crystal cut's knight
([neon-look.md, Hero](../../../../docs/design/neon-look.md#hero)), at the
cut's own framing (shots of 36-44 m) inside its hordes.

An IL2CPP Mac player of this change:

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --capture-stills "$PWD/DIR" --stills-format png \
  --capture-mask --stills-at 12,20,36,44,52 --seconds 53 --look edge
uv run --project demo3d/tools demo3d look-metrics DIR \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --preset edge
```

(and so for solid/arcade, hologram/phosphor, prism/spectrum, and `--look
classic` against ion). Tables in metrics/.

## Stills

`hero-crystal-cut.jpg`: crops round the hero, 2.5x, at 12, 20, 36, 44 and
52 s, one row per look (classic, edge, solid, hologram, prism). Classic's
lit knight is a small orange figure on the Synty ground; under each look it
is a white figure in the void, brighter than anything round it, with each
look's grammar (edge's lines and chest core, solid's shaded body,
hologram's light, prism's sparkling facets) plain at this size.

## How readability was judged

As in game: the style guide's `hero_margin_min` (at least 5), the hero's own
pixels, and by eye.

| look | hero margin | hero L* p90 | share L* >= 97 |
| --- | --- | --- | --- |
| classic | -28.1 to 8.5 | 69-80 | 0.00-0.03 |
| edge | 24.4-38.0 | 97.9-99.2 | 0.13-0.19 |
| solid | 5.9-11.8 | 99.6-99.7 | 0.25-0.47 |
| hologram | 33.1-43.8 | 95.6-96.9 | 0.06-0.09 |
| prism | 30.4-41.9 | 97.8-99.7 | 0.13-0.23 |

Every still of every look passes; classic fails four of five.

- **Levelling.** With game's gains as they were, the cut's knight came out
  far darker (edge -6 to 13, solid -50 to -41): game's baked arena adds 0.8
  stops of scene exposure that the cut doesn't. The hero now takes the
  scene's exposure out (`_LookToe.w`, `HeroLevel` in HeroLook.hlsl), so it
  lands on the same rung in both players.
- **Solid** clears its monsters by the least: the cut's solid monsters reach
  a body L* p90 of about 95, over the enemy rung (45-82). The hero's hot spot
  (0.3, was 0.08) lifts its top to white to keep the margin; at 0.08 it was
  -2.8 to 3.9. A calmer solid monster on the cut is ps-wnj.19's (commented
  there).
