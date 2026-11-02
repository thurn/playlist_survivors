# Wireframe hologram on the crystal cut (ps-wnj.12, 2026-11-01)

The hologram preset's monster look, `wireframe`
([neon-look.md, Monsters](../../../../docs/archive/design/neon-look.md#monsters)), on
the crystal cut's horde, shards and dragon: lines in each type's palette slot
on the model's feature edges, a thin rim and a faint fill added over the void
world, front surfaces only. Before and after come from one IL2CPP Mac build:
`--look hologram` draws the look; `--look hologram --monster-look
refractive-gem` (an option not built yet) draws the crystal stand-in, v2's
monsters, so only the monsters differ (both on the hologram preset's
scanlines post and wireframe structures).

```sh
APP="$PWD/demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --capture-stills "$PWD/DIR" --stills-format png \
  --capture-mask --stills-at 4,12,20,28,36,44,52,55.95,56.3 --seconds 57 --look hologram
uv run --project demo3d/tools demo3d look-metrics DIR --preset hologram --impact 55.95,56.3 \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/phosphor.json
uv run --project demo3d/tools demo3d filmstrip monster-hologram-after --at 55.6 --frames 24 --every 3 --warmup 3.0 \
  --player-args "--trailer crystal --look hologram" --baseline monster-hologram-before --evidence look/monster-hologram
```

(The player resolves relative paths against its own folder: give it absolute ones.)

## Stills and filmstrip

- `crystal-T.jpg`: the crystal above, the hologram below. Every type is in
  the cut's clumps, in phosphor's slots: skeletons and slimes green
  (`enemies[0]`), demons and bats teal (`[1]`), goblins amber (`[2]`),
  mushrooms orange (`[3]`), bigorks and trolls pink and violet (`[4..5]`),
  and the dragon a pink wire silhouette in `boss.body` (56.3 s), where the
  crystal drew a flat yellow shape. 55.95 s is the breath's impact frame,
  56.3 s its shards.
- `monster-hologram-after-vs-monster-hologram-before.jpg`: the breath's mass
  kill (55.6 to 56.75 s, every third frame), before and after. The kill
  breaks on its frame (55.90 s) in both; the hologram's dead flash
  `shatter.core` on that frame and fly as wire fragments outlined where they
  broke, their lines fading over 0.6 s.

## Metrics (phosphor, hologram thresholds; the two impact frames included)

| rule | crystal | wireframe |
| --- | --- | --- |
| rules broken, all nine stills | 53 | 36 |
| palette conformance >= 0.85 | 0.71-0.94, 7 fail | 0.86-0.94 (0.78 on the breath's white impact frame) |
| monster form >= 30 | 7-20, all fail | 35-46 (29.7 at 20 s, 29.4 on the impact frame) |
| swarm separation >= 13 | 1.0-15.9, 5 of 8 measured fail | 8.2-12.4, all 9 fail |
| bright share <= 0.22 | 0.16-0.45, 6 fail | 0.04-0.20 (0.31 on the impact frame) |
| floor p50, black share >= 0.30 | 1.2-7.8, 3 fail | 0.3-2.6, pass |
| readability >= 17 | 48-63 | 30-51 |

- **Conformance and form pass**: every body draws in its slot, its light in
  its lines and rim, and the crowd no longer lights the floor (bright share
  and black share now pass).
- **Swarm separation is provisional at trailer distance**, as it is for the
  edge look ([monster-edge](../monster-edge/README.md): 6.7-11.5). The cut's
  monsters are 20-40 px tall, so the seam metric's box smoothing takes in a
  whole body's lines. (The crystal passed on three stills, 13.6-15.9, and
  failed down to 1.0 on the others.) In game, at about twice the size,
  the hologram measures 11.7-13.5
  ([game evidence](../../../../game/evidence/look/monster-hologram/README.md)).
- **The rest belong to other axes**: halo is the post's; hero margin, which
  fails in both, is the hero treatment's (ps-wnj.9); key60 follows the cut's
  type mix and staging (ps-wnj.17).
