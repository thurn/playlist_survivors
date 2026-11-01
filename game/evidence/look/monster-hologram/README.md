# Wireframe hologram in game (ps-wnj.12, 2026-11-01)

The hologram preset's monster look, `wireframe`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)):
bodies of light. Lines in the type's palette slot on the model's feature
edges, a thin fresnel rim and a faint fill that thickens toward the
silhouette, added over the arena (a 25% veil dims what is behind). A
depth-only pass first draws each type's front surfaces alone, so a clump
shows the near monster's lines, not every back face's wire through it.
Shards are line fragments outlined where they broke. `wireframe-all` draws
every triangle edge too, at 40% of a feature edge's light.

Before and after come from one IL2CPP Mac build: `--look hologram` draws the
look; `--look hologram --monster-look refractive-gem` (an option not built
yet) draws the crystal stand-in, v2's monsters, so only the monsters differ.

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
P="--file $SONG --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@64"
uv run --project demo3d/tools game look hologram-after --at 20,45,64.1 --readability --player-args "$P --look hologram"
uv run --project demo3d/tools game look-metrics captures/game/look/hologram-after \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/phosphor.json --preset hologram
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look hologram" --evidence look/monster-hologram/perf
```

## Stills

- `game-T.jpg`: the crystal above, the hologram below, minute 25 with 300
  alive. 20 s is a clump of elite trolls (`enemies[5]`, violet in phosphor)
  under lightning; 45 s a mushroom clump (`enemies[3]`, orange) beside a
  troll's shards; 64.1 s a structure break among trolls, mushrooms and
  bigorks.
- `hologram-all/`: the same stills under `--monster-look wireframe-all`.
- `slam/hologram-slam.jpg`: ps-wnj.7's slam recipe
  ([impulse](../impulse/README.md), `--at 16.17 --frames 12 --strip 4`) with
  `--look hologram`. The orange clump on the right lights and hops as the
  slam's ring reaches it (ImpulseCrowd, as in the edge look).

## Metrics (phosphor, hologram thresholds)

| rule | crystal | wireframe | wireframe-all |
| --- | --- | --- | --- |
| palette conformance >= 0.85 | 0.28-0.68, all fail | 0.96-0.98 | 0.98-0.99 |
| monster form >= 30 | 13-15, all fail | 38-49 | 19-22, all fail |
| swarm separation >= 13 | 3.5, 5.5 (20 s: no dense tile) | 11.8, 13.5, 11.7 | 5.6-7.2 |
| bright share <= 0.22 | 0.11-0.25, 1 fail | 0.07-0.20 | 0.12-0.26, 2 fail |
| key60 >= 0.55 | 0.54-0.69, 2 fail | 0.47-0.86, 1 fail | 0.61-0.89 |
| halo >= 15 | 11-21, 1 fail | 12-24, 1 fail | 15-30 |
| readability >= 17 | 50-58 | 38-52 | 51-57 |

- **Conformance and form pass**: every body draws in its slot, its light in
  lines and rim toward the silhouette, its face-on surfaces nearly clear.
- **Swarm separation is provisional**: 11.7-13.5, short of 13 on two stills,
  up from 3.5-5.5. Six tunings (rim width and gain, fill, veil 0.25-0.6,
  line width 1.1-2 px) all landed at 11-13.7. A see-through body has no
  dark core to make a seam: the metric reads a big troll's interior, lines
  and bloom haze over the fill, where the edge look's interior is void. The
  preset's scanlines post costs 1-2 points (12.4-13.7 under neon-bloom).
  By eye, clumps read as individuals: each body is outlined and near bodies
  hide the far ones' wire. A pile of trolls against the frame's edge (the
  slam strip's left) is the noisiest case: a wall of wire.
- **wireframe-all is the busy option**: its triangulation turns a body to
  mesh texture, so form and separation fail; the preset keeps feature edges.
- key60 at 45 s and halo are the staging's and the post's, as with the edge
  look; ps-wnj.19 runs every preset's rules on the finished looks.

## Perf: in budget

| | peak p50/p99/max | break | finale | first frames |
| --- | --- | --- | --- | --- |
| edge look ([monster-edge](../monster-edge/README.md)) | 4.92 / 6.63 / 6.63 | 5.72 / 9.91 / 9.91 | 6.53 / 7.87 / 7.87 | 29-38 |
| wireframe | 4.12 / 5.64 / 5.64 | 3.01 / 6.47 / 6.47 | 4.32 / 6.48 / 6.48 | 29-34 |

GPU-complete 1080p frames, ms. Two passes per draw (depth, then the light),
no shadow pass, no opaque-texture reads. The first frames are the new
shaders compiling on first draw (ps-wnj.18 takes the traced warm-up).
