# Bright solid emissive on the crystal cut (ps-wnj.11, 2026-11-01)

The solid preset's monster look, `solid-emissive`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)), on
the crystal cut's horde, shards and dragon. Before and after come from one
Mono Mac build: `--look solid` draws the look; `--look solid --monster-look
refractive-gem` (an option not built yet) draws the crystal stand-in, v2's
crystal as ps-wnj.22 carried it into the package, so only the monsters differ
(both on `arcade`, `punchy-bloom` and the environments as they are).

```sh
APP="demo3d/Builds/mac/mono/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --capture-stills DIR --stills-format png \
  --capture-mask --stills-at 4,12,20,28,36,44,52,55.95,56.3 --seconds 57 --look solid
uv run --project demo3d/tools demo3d look-metrics DIR --preset solid --impact 55.95,56.3 \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/arcade.json
uv run --project demo3d/tools demo3d filmstrip monster-solid-after --at 55.6 --frames 24 --every 3 --warmup 3 --backend mono \
  --player-args "--trailer crystal --look solid" --baseline monster-solid-before --evidence look/monster-solid
```

## Stills and filmstrip

- `crystal-T.jpg`: the crystal above, the solid look below. Every type is in
  the cut's clumps: skeletons and slimes (violet, `enemies[0]`), demons and
  bats (magenta, `[1]`), goblins (aqua, `[2]`), mushrooms (cyan, `[3]`),
  bigorks and trolls (hot orange and amber, `[4..5]`), and the dragon in
  `boss.body` red where the crystal drew a pale gem. 55.95 s is the breath's
  impact frame, 56.3 s its shards.
- `monster-solid-after-vs-monster-solid-before.jpg`: the breath's mass kill
  (55.6 to 56.75 s, every third frame), the crystal above. The kill breaks on
  its frame (55.90 s) in both; the solid shards flash `shatter.core` on that
  frame and are back in their hues, outlined, on the next.

## Metrics (arcade, solid thresholds; the two impact frames included)

| rule | crystal | solid |
| --- | --- | --- |
| rules broken, all nine stills | 56 | 30 |
| palette conformance >= 0.85 | 0.50-0.89, 7 fail | 0.88-0.99 (0.80 on the breath's white impact frame) |
| monster form >= 20 | 2-11, all fail | 33-62 |
| swarm separation >= 13 | 5.8-18.2, 5 of 8 measured fail | 10.5-16.4, 4 of 9 fail |
| readability >= 17 | 58-76 | 37-47 |
| bright share <= 0.32 | 0.19-0.47, 4 fail | 0.05-0.26 |

[metrics-before.md](metrics-before.md), [metrics-after.md](metrics-after.md).
v2's own re-render, measured on its own colours
([metrics/masked](../metrics/masked/metrics.md)), had separation 3.0-15.7
and form 10-18 at the same moments.

- **Conformance and form pass**: every body draws in its palette slot, with
  a light from above and a dark band inside its silhouette.
- **Swarm separation is provisional at trailer distance**, as the edge look's
  is ([monster-edge](../monster-edge/README.md): 6.7-11.5). The shots score
  10.5-16.4; 4, 28, 44 and 52 s (10.5-12.5) fall short of 13. The cut's bodies
  are 35-60 px in radius under a steep camera; in game, at about twice that,
  it passes at 14.3-20.7
  ([game evidence](../../../../game/evidence/look/monster-solid/README.md)).
  An earlier take with the game's broad falloff on every body did no better
  here (12.1-16.8 on fewer tiles) and darkened whole bodies: their sides face
  the camera at about 60 degrees, the post's void toe crushes what falls under
  its knee, and the bodies fell to L* p50 10-17 with readability 10-15. The silhouette band therefore keeps a constant width in pixels, and
  only bodies large on screen take the broad falloff.
- **The rest belong to other axes**: halo (1-13) is the post's; hero margin is
  the hero treatment's (ps-wnj.9); key60 follows the cut's type mix and
  staging (ps-wnj.17); the blue structure crystal and rune circles that cost
  conformance are the world kit's (ps-wnj.8).
