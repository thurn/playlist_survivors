# Dark core, lit edges on the crystal cut (ps-wnj.10, 2026-11-01)

The edge preset's monster look, `dark-core-edges`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)), on
the crystal cut's horde, shards and dragon. Before and after come from one
IL2CPP Mac build: `--look edge` draws the look; `--look edge --monster-look
refractive-gem` (an option not built yet) draws the crystal stand-in, so only
the monsters differ (both on neon-bloom and the environments as they are).

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --capture-stills DIR --stills-format png \
  --capture-mask --stills-at 4,12,20,28,36,44,52,55.95,56.3 --seconds 57 --look edge
uv run --project demo3d/tools demo3d look-metrics DIR \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --preset edge --impact 55.95,56.3
uv run --project demo3d/tools demo3d filmstrip monster-edge-after --at 55.6 --frames 24 --every 3 --warmup 3.0 \
  --player-args "--trailer crystal --look edge" --baseline monster-edge-before --evidence look/monster-edge
```

## Stills and filmstrip

- `crystal-T.jpg`: the crystal above, the edge look below. Every type is in
  the cut's clumps: skeletons and slimes (cyan, `enemies[0]`), demons and bats
  (cobalt, `[1]`), goblins (magenta, `[2]`), mushrooms (violet, `[3]`),
  bigorks and trolls (amber, `[4..5]`), and the dragon in `boss.body`, a red
  lined silhouette with its core lit, where the crystal drew a flat yellow
  shape. 55.95 s is the breath's impact frame, 56.3 s its shards.
- `monster-edge-after-vs-monster-edge-before.jpg`: the breath's mass kill
  (55.6 to 56.75 s, every third frame), before and after. The kill breaks on
  its frame (55.90 s) in both; the edge shards flash `shatter.core` on that
  frame and keep lit crack outlines as they fly.

## Metrics (ion, edge thresholds; the two impact frames included)

| rule | crystal | edge |
| --- | --- | --- |
| rules broken, all nine stills | 68 | 39 |
| palette conformance >= 0.85 | 0.47-0.94, 8 fail | 0.99-1.00 (0.78 on the breath's white impact frame) |
| monster form >= 30 | 4-12, all fail | 36-51 |
| swarm separation >= 13 | 2.8-9.1, all 7 measured fail | 4.8-11.5, all 9 fail |
| floor p50 <= 8, black share >= 0.30 | 4 and 4 fail | pass (black 0.27 on the impact frame) |
| bright share <= 0.22 | 0.18-0.48, 7 fail | 0.06-0.37, 2 fail (the impact frame and its shards) |
| readability >= 17 | 47-63 | 30-50 |

- **Conformance and form pass**: every body draws in its palette slot, its
  light in rim, lines and core, its interior void.
- **Swarm separation is provisional at trailer distance.** It rose from
  2.8-9.1 to 6.7-11.5 on the shots (the impact frame's white aside), short of
  13. The cut's monsters are 20-40 px tall, so the seam metric's box smoothing
  takes in a whole body's rim; in game, at about twice the size, it is 16.5-17
  ([game evidence](../../../../game/evidence/look/monster-edge/README.md)).
  Lines on triangles only a few pixels across keep 35% of their light, which
  raised separation and readability here without tangling close-up bodies.
- **The rest belong to other axes**: halo (7.8-16.9) is the post's; hero
  margin, which fails in both, is the hero treatment's (ps-wnj.9); key60
  (0.27-0.76) follows the cut's type mix and staging (ps-wnj.17); the lit
  environments' floors are the void world's (ps-wnj.8).
