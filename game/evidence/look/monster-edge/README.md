# Dark core, lit edges in game (ps-wnj.10, 2026-11-01)

The edge preset's monster look, `dark-core-edges`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)):
near-black glass lit only by the type's palette slot in a fresnel rim, in
lines on the model's feature edges (the crowd baker's, UV5) and in a small
core that flares on the beat. Shards keep the lines and outline their cracks.

Before and after come from one IL2CPP Mac build: `--look edge` draws the
look; `--look edge --monster-look refractive-gem` (an option not built yet)
draws the crystal stand-in, so only the monsters differ.

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look edge-after --at 20,45,64.1 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/edge-after \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --preset edge
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look edge" --evidence look/monster-edge/perf
```

## Stills

`game-T.jpg`: the crystal above, the edge look below, minute 25 with 300
alive. 20 s is a clump of elite trolls (amber, `enemies[5]`) under
lightning; 45 s a mushroom clump (violet, `enemies[3]`) and a troll's
shards; 64.1 s a structure break among trolls, mushrooms and bigorks.

## Metrics (ion, edge thresholds)

| rule | crystal | edge |
| --- | --- | --- |
| swarm separation >= 13 | 6.5, 7.6 (20 s: no dense tile) | 16.8, 16.5, 17.0 |
| palette conformance >= 0.85 | 0.91-0.97 | 0.97-0.99 |
| monster form >= 30 | 20-24, all fail | 47-57 |
| key60 >= 0.55 | 0.46-0.76, 1 fail | 0.63-0.95 |
| bright share <= 0.22 | 0.10-0.23, 1 fail | 0.08-0.22 (0.223 at 20 s) |
| halo >= 15 | 11-26, 1 fail | 8.7-13.8, all fail |
| readability >= 17 | 46-57 | 36-56 |

- **Clumps read as individuals.** Each body's rim and outline are lit and
  its interior is void, so two touching mushrooms or trolls show a dark
  valley between bright edges.
- **Form from view angle.** The rim and the lines toward the silhouette carry
  the light, so a body's L* spans 47-57.
- **Halo** is the post's (neon-bloom), measured 3-10 px round emitters: thin
  lines over dark glass leave that ring mostly dark. It was already failing
  with the crystal on two of three stills; ps-wnj.19 runs every preset's
  rules on the finished looks.
- The 20 s still's bright share (0.223) is lightning and a troll clump in its
  hit flash.

## Rings

`slam/edge-slam.jpg`: ps-wnj.7's slam recipe (game/evidence/look/impulse,
`--at 16.17 --frames 12 --strip 4`) with `--look edge`. As each impulse
ring passes a living monster it hops (Impulse.hlsl's `ImpulseCrowd`) and its
rim and lines take the ring's light: the troll clump on the left lights as
the slam's ring reaches it, the mushrooms on the right as the hit's does.

## Perf: in budget

| | peak p50/p99/max | break | finale | first frames |
| --- | --- | --- | --- | --- |
| crystal ([post](../post/README.md), edge) | 5.15 / 8.59 / 8.59 | 4.09 / 7.25 / 7.25 | 5.72 / 10.05 / 10.05 | 30-49 |
| edge look, rings included | 4.92 / 6.63 / 6.63 | 5.72 / 9.91 / 9.91 | 6.53 / 7.87 / 7.87 | 29-38 |

GPU-complete 1080p frames, ms. The look is opaque glass with no refraction
(no opaque-texture reads); nothing comes near 33 ms after each scenario's
first frames, where the new shaders compile on first draw (ps-wnj.18 takes
the traced warm-up for every preset).
