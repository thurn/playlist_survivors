# The hero looks, in game (ps-wnj.9, 2026-11-01)

The knight and the crystal blade in a treatment per preset, each echoing its
monsters on the style guide's hero light rung, so the hero is the brightest
single thing on screen without growing or an outline
([neon-look.md, Hero](../../../../docs/design/neon-look.md#hero)). Crystal
cut: [demo3d/evidence/look/hero](../../../../demo3d/evidence/look/hero/README.md).

An IL2CPP Mac player of this change, windowless, at the trailer's framing
(`--view-width 36`) from minute 25 (about 300 alive):

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
# the clean horde: the starting sword, no offer cards
uv run --project demo3d/tools game look hero-edge-f --at 20,30,50,58 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --look edge --view-width 36 --no-cards"
# with evolved weapons (restraint's set)
uv run --project demo3d/tools game look hero-edge-fe --at 20,40,58 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5 --look edge --view-width 36 --no-cards"
uv run --project demo3d/tools demo3d look-metrics captures/game/look/hero-edge-f \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --preset edge
```

(and so for solid/arcade, hologram/phosphor, prism/spectrum, and `--look
classic`, the knight before the epic, against ion). `--readability` now also
writes each still's view json (the camera and the hero's feet), which the
hero margin needs.

## Stills

Crops round the hero, 2x, one row per look (classic, edge, solid, hologram,
prism):

- `hero-clean.jpg`: the clean horde at 20, 30, 50 and 58 s; 58 s is the
  knight inside a dense clump.
- `hero-evolved.jpg`: with evolved weapons at 20, 40 and 58 s: the hero
  inside its own weapons' crystals and rings.

Classic's Synty knight, lit by the arena, is a small orange figure the swarm
swallows. Under each look it is a white figure you find at a glance: edge a
knight of white lines round a dark core with a glowing chest, solid a shaded
white figure, hologram a body of light, prism a white gem with sparkling
facets.

## How readability was judged

1. **The style guide's rule**, `hero_margin_min`: the hero's L* p90 (a
   capsule from its feet to 1.8 m, off the monsters) at least 5 over the
   monsters' (metrics/, column "hero margin"):

   | look | clean horde | evolved weapons |
   | --- | --- | --- |
   | classic | -7.7 to 22.8 | -11.0 to 27.9 |
   | edge | 8.9-19.3 | 6.0-23.2 |
   | solid | 12.5-21.5 | 16.4-17.1 |
   | hologram | 24.6-33.4 | 22.7-37.0 |
   | prism | 11.7-20.6 | 8.6-21.0 |

   Every still of every look passes; classic fails twice.
2. **The hero's own pixels** (the capsule up to 1.6 m, under the HUD's beat
   disc, off the monsters, lit): on the hero rung, not a white blob. The
   first tuning put 35-60% of the knight at L* 97 or more, so every look
   read as the same white shape; the gains came down until each look's
   grammar showed:

   | look | L* p50 | L* p90 | share L* >= 97 |
   | --- | --- | --- | --- |
   | classic | 32-66 | 60-94 | 0.01-0.06 |
   | edge | 64-93 | 98.5-99.2 | 0.14-0.26 |
   | solid | 90-97 | 99.6-99.7 | 0.37-0.51 |
   | hologram | 43-93 | 96.5-97.2 | 0.08-0.12 |
   | prism | 57-95 | 99.3-99.7 | 0.19-0.41 |

   Solid is the brightest by design: `hero.body` is L* 96-100, and its
   monsters reach high on the crystal cut (demo3d's README).
3. **By eye** on the crops: the knight is found first in every frame, and
   each look is told apart at this size.

The evolved scene's margins also count the weapons' white crystals near the
hero, so the clean horde is the fairer read of the knight itself.

The operator's check is in game/evidence/operator-pending.md (ps-wnj.9).
