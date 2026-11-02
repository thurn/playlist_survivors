# Swarm separation in tiles at the bodies' size (ps-wnj.30, 2026-11-02)

The style guide's swarm separation and monster form measured in tiles grown
with the monsters' height on screen
([neon-look-style.md, Rules and Thresholds](../../../../docs/design/neon-look-style.md#rules-and-thresholds),
the notes under `swarm_separation_min`). Before, both used a fixed 32 px
tile, calibrated on the crystal cut's fodder (32-58 px tall at the metric's
960 px, median 45); a game ork stands 115 px and a troll 230, so a dense
game tile was one body's interior, not a clump
([separation](../separation/README.md)).

What changed:

- **The view json says how tall the bodies are.** The look package's
  `StillBodies` collects each living monster's feet, height and enemy slot
  as the crowd is presented (game CrowdView, demo3d DemoMain) while stills
  are kept; the view json gains `bodyM` (the height of the body the median
  monster pixel belongs to: a median weighted by height squared, so a few
  trolls outweigh many mushrooms) and `bodies` (the same per enemy slot).
- **look.py scales the tiles from it** (`body_px`, `scaled_tile`,
  `swarm_tiles`): a height in metres stood at the hero's feet, in pixels;
  each hue's swarm tile is 32 x its bodies' px / 45, never under 32; the
  form tile the same from `bodyM`. Grown tiles step by half a tile; the
  percentile of seam depth a tile takes rises with it (p90 at 32 px, p97 at
  107: seams keep their pixel width round bigger bodies). Tiles reach the
  frame's far edges. A still without the fields keeps today's 32 px tiles.
- **Speckle is filled first** (`despeckle`): a valley in an 8-connected
  patch under 24 px takes the brightest L* within 2 px, so dark specks on
  5-12% of a flat clump score 1-8 (box-averaged alone, 8% scored 17.2);
  on these stills it moves separation by 0-0.5.

## Stills and metrics

IL2CPP Mac player of this change on staging a610bf6f, windowless, on jarvis,
ps-wnj.18's recipe:

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look ps30-edge --at 20,30,40,50,58,70 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/ps30-edge --preset edge \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json \
  --out game/evidence/look/body-tiles/metrics-edge
```

(and so for solid/arcade, hologram/phosphor, prism/spectrum).
`metrics-PRESET/`: every rule on every still (metrics.md with the new
`body px` and `tile` columns, metrics.json) and each still's swarm overlay
(every dense tile, at its size, labelled with its contrast; red under 13).
Before is the same stills measured by staging's look.py.

| preset | separation before | after | dense tiles before | after | form after |
| --- | --- | --- | --- | --- | --- |
| edge | 16.0-22.1 | 15.4-33.6 | 228 | 32 | 56.9-67.9 |
| solid | 12.0-17.7 (58 s breaks) | 16.0-29.2 | 277 | 24 | 37.9-51.2 |
| hologram | 9.3-13.6 (4 break) | 14.9-23.4 | 329 | 55 | 37.2-53.8 |
| prism | 10.4-16.0 (4 break) | 22.1-26.3 | 204 | 28 | 48.7-56.9 |

**Every still of every preset passes `swarm_separation_min` and
`monster_form_min`.** The one broken rule is hologram's background light at
58 s (0.162 against 0.15), as before this change
([separation](../separation/README.md)).

Body heights on screen (`body px`, from `bodyM`): 113-235 px; per hue,
mushrooms 30-63 (tiles 32-45), orks 113-119 (80-85), trolls 224-235
(159-167).

**Dense tiles are few now**, and several stills have none, so the rule
skips them: edge 50 s, solid 58 s, hologram 50 s, prism 40, 50 and 58 s.
That is what the game's crowd is at a body's scale: its orks and trolls in
the line looks are lit lines over a dark core, which never cover half a
body-sized tile, and at 58 s in hologram the 32 px tiles that broke were
inside single trolls (one cut by the frame's edge). The clumps the rule
still finds are clumps: solid's troll mass at 20 s (four 167 px tiles,
27-30: its outlines part it) and its mushroom clump at 40 s (seven 43 px
tiles, 6-22, median 16.0). In 32 px tiles a tile inside one of those
mushroom caps scored 0 and hologram's hatched mushroom stems 10-11: single
bodies again, since a cap is about as wide as the mushroom is tall.
