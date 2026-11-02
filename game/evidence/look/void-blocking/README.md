# The void world's blocked ground in game (ps-wnj.23, 2026-11-01)

The void world (ps-wnj.8) hid the arena's whole Synty set, but only the 38
structure sites stood again as crystal: the other 17,692 tiles `arena.json`
blocks (the forest edge's mass round the field, the glowing tree, the
graveyard, the keep, rocks and stumps) stayed solid and invisible, so the
knight hit walls nobody could see. Now, under every world option
([neon-look.md, The Axes > World](../../../../docs/archive/design/neon-look.md#world)):

- **A kerb**: the grid floor draws a bright line along the walkable side of
  every edge walkable ground shares with blocked ground (the lines' hue
  halfway to `grid.pulse`, zone-tinted, levelled to L* 38 after the post in
  every option, over the quiet grid's L* 12 and `pure-void`'s 6).
- **No grid beyond it**: over blocked ground and outside the level the lines
  fade to a tenth, so the gridded floor is exactly where the knight can walk.
- **Low crystal hedges** on every blocked tile beside walkable ground
  (diagonals too): 2,020 tiles under 790 pieces (`CrystalKit.Wall` along the
  longest straight runs, at most five tiles, a `CrystalKit.Shard` on a lone
  tile), merged into one mesh per 16-tile chunk, no shadows, in the structure
  look's new terrain material: 60% of its light (lit edges at least 0.3, under
  every look's props) in `grid.pulse` (the zone's
  hue under `hued-zones`), so the world's walls read with the floor, apart
  from the gold structures that break and well under the monsters.

Structures' tiles stay gridded (they open when broken) and carry no kerb.
`--look classic` and `--world classic` are untouched (no world option, so
nothing is made).

## Stills

```sh
# each preset's arena tour, the layout's eight shots 40 m wide (classic: --look classic)
uv run --project demo3d/tools game look vb-arena-edge --build --at 0.5,1.5,2.5,3.5,4.5,5.5,6.5,7.5 \
  --player-args "--arena --arena-tour 1 --view-width 40 --look edge"
# play at minute 25 with the horde
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look vb-play-edge --at 20,45 \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV --look edge"
```

- `arena.jpg`: the glowing tree, the graveyard and the forest edge in
  classic (the Synty set whose dressing the sim blocks) above each preset:
  the kerb outlines the same ground the trees, gravestones and fences
  covered. The before is [world/arena.jpg](../world/arena.jpg): the void
  with nothing where the tree stood (its third column).
- `tour-PRESET.jpg`: the whole tour per preset (and classic, unchanged).
- `play.jpg`: minute 25 with 300 alive in each preset: the hedges sit dim
  behind the kerb and read apart from the horde; under `hued-zones` (solid,
  prism) they share the zone's hue with some monsters, far darker
  (operator-pending row).

## The check

`Game.Tests.VoidArenaTests` on the game's own `arena.json`, under each world
option (`void-grid`, `hued-zones`, `pure-void`): the floor's mask is blocked
on exactly the blocked tiles; every blocked tile beside walkable ground has
a piece's vertex standing above 0.25 m on it; no vertex above 0.25 m stands
more than 0.15 m onto a walkable tile; the floor takes the mask; and
`classic` (look or world) makes nothing. Dropping the lone tiles' shards
fails it.

## Perf: in budget

```sh
uv run --project demo3d/tools game perf --only peak --player-args "--look edge" --evidence look/void-blocking/perf-edge
```

| preset | peak probe p50/p99/max (ms) | main p99 (ms) |
| --- | --- | --- |
| edge | 2.99 / 5.38 / 5.38 | 1.52 |
| prism (refracts) | 3.83 / 7.44 / 7.44 | 2.03 |

GPU-complete 1080p frames with 300 alive, on top of the toned-down look
(ps-wnj.24), nowhere near 33 ms (ps-wnj.8's edge peak was 5.44 / 9.68).
