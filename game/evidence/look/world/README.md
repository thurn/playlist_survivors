# The void world kit in game (ps-wnj.8, 2026-11-01)

The game's arena drawn in the look's world kit
([neon-look.md, The Axes > World](../../../../docs/design/neon-look.md#world)):
under a world option the Synty set, the mist and the flames' lights go and the
grid floor (ps-wnj.7) takes the ground's place; under a structure look each of
the 38 structure sites stands as crystal kit pieces on its tiles in the build's
`arena.json` (77 pieces), in the palette's `shatter.gem`, breaking into their
own shards. The layout, footprints and routes are the bake's and the sim's,
unchanged.

## Stills

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look world-edge --at 20,45,64.1 --readability --evidence look/world/stills-edge \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
# the arena alone, four of the layout's shots, 40 m wide; before: --look edge --world today --structure-look today
uv run --project demo3d/tools game look world-arena-edge --at 0.5,1.5,2.5,4.5 \
  --player-args "--arena --arena-tour 1 --view-width 40 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/world-edge --palette PALETTE.json --preset edge --out metrics-edge
```

- `arena.jpg`: the arena alone, before (the baked Synty night with its crystal
  swap) above each preset: `edge` (void-grid, edge-glass), `solid`
  (hued-zones, solid-glass), `hologram` (pure-void, wireframe), `prism`
  (hued-zones, refractive-gem). Every option draws; the pieces fit their
  sites (obelisks on single tiles, shards in pairs, rings and walls along
  lines, formations with outlying shards on the large sites). `pure-void`'s
  faint grid barely shows at this framing (the floor's tuning, ps-wnj.7).
- `stills-PRESET/`: minute 25 with 300 alive, the third still after the
  scripted break. The monsters are still the stand-in look (ps-wnj.10-.13).
- `metrics-PRESET/`: the style guide's rules. What the world owns passes in
  every preset: frame p25 0.3-0.8 (rule 6), black share 57-77%, clipped
  under 0.3%, white under 1.6%. The rules that fail are the stand-in
  monsters': bright share (their coverage), halo, key, form and separation.

## Breaks pay off on the impact frame

`telemetry/` is the same run under `edge` with `regrow@70` and
`--structures-out` (`captures/game/look/world-telemetry/structures.csv`).
Each of its seven breaks, on its first frame:

| break at (s) | shards | flying on the impact frame | moved on it | all flying |
| --- | --- | --- | --- | --- |
| 16.20 | 28 | 5 | 9.5 cm | +3 frames |
| 45.43 | 14 | 5 | 26 cm | +2 frames |
| 61.08 | 28 | 23 | 50 cm | +2 frames |
| 65.00 | 10 | 4 | 15 cm | +3 frames |

(and so for the others): the nearest shards launch on the strike, the whole
site within 50 ms, nothing before it. All seven regrew, their eruptions
throwing their bursts (70.47 s: 17 shards). Physics peaks at 0.40 ms in a
frame and the shadow redraw at 0.43 ms.

## Perf: in budget

```sh
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look edge" --evidence look/world/perf-edge
```

| preset | peak probe p50/p99/max | break | finale |
| --- | --- | --- | --- |
| edge | 5.44 / 9.68 / 9.68 | 5.15 / 10.61 / 10.61 | 4.57 / 9.69 / 9.69 |
| prism (refracts) | 6.00 / 7.14 / 7.14 | 3.48 / 10.65 / 10.65 | 3.85 / 7.66 / 7.66 |

GPU-complete 1080p frames in ms; no frame near 33 ms. Edge's peak had one
124 ms first frame (the props' shader compiling on its first draw; the other
scenarios' first frames are 31-43 ms, as ps-wnj.6's): the build's traced
warm-up is ps-wnj.18's.
