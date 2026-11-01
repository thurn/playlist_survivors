# Refractive gem on the crystal cut (ps-wnj.13, 2026-11-01)

The prism preset's monster look, `refractive-gem`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)), on
the crystal cut's horde, shards and dragon. Before is the tree before the
change, both IL2CPP Mac builds: `--look prism` drew the old crystal stand-in
then, so only the monsters differ (both in the prism preset's void world,
structures and post). The gem doesn't take the old crystal's inner glow
(`--style-tune glow`, 1.0 in trailer-crystal.json, which only the crystal,
now `--look classic`'s monster look, reads): it looks the same in both players.

```sh
APP="demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$APP" -batchmode -force-metal --fixed-time --trailer crystal --capture-stills "$PWD/DIR" --stills-format png \
  --capture-mask --stills-at 4,12,20,28,36,44,52,55.95,56.3 --seconds 57 --look prism
uv run --project demo3d/tools demo3d look-metrics DIR \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/spectrum.json --preset prism --impact 55.95,56.3
uv run --project demo3d/tools demo3d filmstrip gem-after --at 55.6 --frames 24 --every 3 --warmup 3.0 \
  --player-args "--trailer crystal --look prism" --baseline gem-before --evidence look/monster-gem
```

`--capture-stills` takes an absolute path: given a relative one, the player
wrote nothing.

## Stills and filmstrip

- `crystal-T.jpg`: the crystal above, the gem below. Every type is in the
  cut's clumps: skeletons and slimes (ice, `enemies[0]`), demons and bats
  (periwinkle, `[1]`), goblins (violet, `[2]`), mushrooms (rose, `[3]`),
  bigorks and trolls (gold and orange, `[4..5]`), and the dragon in
  `boss.body`, a red gem whose facets and core read through it, where the
  crystal drew a flat yellow shape. 55.95 s is the breath's impact frame,
  56.3 s its shards.
- `gem-after-vs-gem-before.jpg`: the breath's mass kill (55.6 to 56.75 s,
  every third frame), before and after. The kill breaks on its frame
  (55.90 s) in both; the gem's shards flash on that frame and fly in their
  slots' hues, fresh faces lit.

## Metrics (spectrum, prism thresholds; the two impact frames included)

| rule | crystal | gem |
| --- | --- | --- |
| rules broken, all nine stills | 54 | 44 |
| monster form >= 30 | 10.7-24.6, all fail | 31.3-45.7 |
| palette conformance >= 0.75 | 0.39-0.84, 8 fail | 0.71-0.84 (0.71 at 52 s; 0.69-0.74 on the impact frames) |
| floor p50 <= 8 | 1.2-7.7 | 0.3-2.9 |
| black share >= 0.30 | 0.26-0.64, 2 fail | 0.35-0.78 |
| bright share 0.04-0.28 | 0.14-0.47, 2 fail (the impact frames) | 0.04-0.36 (28 s at 0.038; the impact frame 0.36) |
| swarm separation >= 13 | 2.4-16.7 (3 of 4 measured fail) | 5.4-11.9, all 5 measured fail |
| readability >= 17 | 45-67 | 27-56 |

- **Form and conformance**: every body draws in its palette slot and shows
  its facets; the crystal's flat tints (green, teal, yellow) are gone.
- **Far bodies keep a glow.** The cut's monsters stand 20-60 px tall, where
  the gem's narrow bands and fine lines average out to a dim outline (bright
  share 1.3-2% on the opening shots without it). Under 7% of the screen's
  half-height a body adds a glow in its slot's hue, fading out by 14%; the
  game's monsters stand taller and get none (`GemFar` in MonsterGem.hlsl).
- **Swarm separation is provisional at trailer distance**, as it was for the
  edge look (6.7-11.5 there): the seam metric's box takes in a whole small
  body. In game, at larger sizes, the gem measures 13.2-13.6
  ([game evidence](../../../../game/evidence/look/monster-gem/README.md)).
- **The rest belong to other axes**: halo (4.4-19.4) is the post's; hero
  margin, failing in both, is the hero treatment's (ps-wnj.9); key60
  (0.24-0.43) and families (up to 6 on the impact frames) follow the cut's
  type mix and staging (ps-wnj.17), and the gem's fringes, which turn each
  slot's hue 28 degrees either way, widen each family a little.
