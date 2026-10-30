# Look sets: the palette swaps (ps-ocd, 2026-10-30)

The player's `--palette <name>` (arena-layout.json's `palettes`; [mac.md](../../../docs/tech-demo-3d/mac.md#palettes))
rendered against the [final](final/README.md) set, to see how the roster's type colours and the
effects read on other backgrounds. Each folder keeps its contact sheet (final left, the palette
right), `look.md` and `look.json`; the per-still JPEGs stay in `captures/look/palette-*`.

```
uv run --project demo3d/tools demo3d look palette-<name> --baseline final --player-args "--palette <name>"
```

## Readability: dL* per beauty shot (floor |dL*| >= 17)

| set | opening | path | glowing-tree | graveyard | courtyard | forest-edge |
|---|---:|---:|---:|---:|---:|---:|
| final (warm night) | 21.6 | 24.3 | 21.7 | 23.7 | 21.0 | 20.0 |
| snow | -23.3 | -8.8 | -17.8 | -12.6 | -24.7 | -14.4 |
| desert | -14.8 | -0.9 | -8.5 | -4.4 | -15.7 | -5.2 |
| lava | 19.4 | 16.9 | 15.6 | 17.2 | 18.3 | 14.5 |
| neon | 15.3 | 13.7 | 12.9 | 14.2 | 14.4 | 12.0 |
| blossom | -4.9 | 8.8 | 1.6 | 5.9 | -5.2 | 4.3 |
| crypt | 19.2 | 21.9 | 18.8 | 21.0 | 19.3 | 17.2 |

## What the stills show

- **Light grounds fail on value.** The horde's median L* is about 42-45, so any ground near
  L* 50 hides it: blossom (ring L* about 50) is the worst, desert next. Snow (ring about 75)
  reads where the horde is dense and dark but fails where the hero's pool brightens the ground.
  A light environment needs a darker horde, or a darker, cooler fighting ground than these.
- **A coloured key light erases the type hues.** The horde is keyed at 3x the moon, so lava's
  red moon turns every type red and neon's violet moon pulls them all toward blue-violet. Only
  the brightest types (yellow mushrooms, green goblins) survive. Background colour should come
  from the ground, fog and ambient, with a near-neutral key on the horde.
- **Warm grade on warm grounds blows out.** Desert's sand saturates to orange-yellow under the
  night LUT's warm midtones; blossom goes salmon. A per-palette grade would be needed.
- **Crypt, the neutral control, holds up** (17.2-21.9): the roster's hues read on grey as well
  as on the warm night, so the type colours are not leaning on the brown.
- **Effects stay the brightest everywhere** except where the snow and desert grounds clip.
- **What a palette leaves alone** shows in every sheet: the kit's baked albedo and bounce, the
  flames, the glowing tree's cyan and the rune circles keep the night's colours.
