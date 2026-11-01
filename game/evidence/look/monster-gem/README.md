# Refractive gem in game (ps-wnj.13, 2026-11-01)

The prism preset's monster look, `refractive-gem`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)),
which replaced the old crystal (MonsterCrystal, MonsterShard) as the stand-in
for an option without a look; the crystal now draws only `--look classic`. Clear gem showing the void and grid behind it
through its facets, split into the type's palette hue and its two neighbours,
with narrow reflected bands that move between facets as the view turns, a rim
and fine lines in the slot's hue and a core that flares on the beat. Shards
keep the gem, their fresh faces lit.

Before is the same commit's tree before the change, both IL2CPP Mac builds:
`--look prism` drew the crystal stand-in then (refractive-gem was not built),
so only the monsters differ.

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look gem-after --at 20,45,64.1 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look prism"
uv run --project demo3d/tools game look gem-after-early --at 20,40 --readability \
  --player-args "--file $SONG --start-minute 6 --bot positioning --debug $EV --look prism"
uv run --project demo3d/tools game look-metrics captures/game/look/gem-after \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/spectrum.json --preset prism
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look prism" --evidence look/monster-gem/perf
```

## Stills

`game-T.jpg`: the crystal above, the gem below, minute 25 with 300 alive. 20 s
is a clump of elite trolls (`enemies[5]`, orange) under lightning and constant
hits; 45 s a mushroom clump (`enemies[3]`, rose) and a troll's shards; 64.1 s a
structure break among trolls, mushrooms and bigorks. `game-early-T.jpg`: minute
6, the gold elites at a distance and their shards.

## Metrics (spectrum, prism thresholds)

| rule | crystal | gem |
| --- | --- | --- |
| swarm separation >= 13 | 5.4 (one dense tile; none at 20 and 64.1 s) | 13.2, 13.5, 13.6 (13-48 dense tiles) |
| monster form >= 30 | 17.5-22.7, all fail | 43.0-49.4 |
| palette conformance >= 0.75 | 0.90-0.92 | 0.89-0.92 |
| bright share <= 0.28 | 0.06-0.14 | 0.11-0.25 |
| white <= 0.02 | 0.003-0.009 | 0.004-0.019 |
| halo >= 15 | 11.8-32.5, 1 fail | 12.5-14.9, all fail |
| readability >= 17 | 40-45 | 46-62 |
| minute 6: conformance | 0.30, 0.59 (teal and green bodies) | 0.72, 0.76 |

- **Clumps read as individuals.** Each gem's rim and fine lines are lit and
  the void seen through it is dark, so touching trolls or mushrooms show a
  seam. The crystal's dense clump measured 5.4.
- **Form from view angle.** Facet bands, rim and refraction move with the
  view, so a body's L* spans 43-49 (the crystal's 18-23).
- **Constant hits keep their hue.** A hit lights the gem half `shatter.core`
  and half its slot's hue, as the crystal's did; a white flash put the troll
  clump at 4% white.
- **Halo** is the post's (prism-bloom), measured round emitters: the fine
  lines leave that ring mostly dark, as with the edge look. ps-wnj.19 runs
  every preset's rules on the finished looks.
- **Minute 6's conformance** is the weapons and pickups (green crystals,
  yellow hexagons): its monsters are the gold elites in their slot. The
  crystal drew them teal.

## Rings

`slam/gem-slam.jpg`: ps-wnj.7's slam recipe (game/evidence/look/impulse) with
`--look prism`. As each ring passes a living gem it hops and its bands, rim
and lines take the ring's light: the troll clump on the left lights as the
slam's ring reaches it.

## Perf: in budget

| | peak p50/p99/max | break | finale | first frames |
| --- | --- | --- | --- | --- |
| edge look ([monster-edge](../monster-edge/README.md)) | 4.92 / 6.63 / 6.63 | 5.72 / 9.91 / 9.91 | 6.53 / 7.87 / 7.87 | 29-38 |
| gem, prism preset | 3.51 / 7.15 / 7.15 | 4.45 / 6.17 / 6.17 | 6.07 / 12.05 / 12.05 | 32-110 |

GPU-complete 1080p frames, ms. The gem reads the camera's opaque texture
(three taps a pixel), which `CrowdView` turns on whenever it draws. Nothing
comes near 33 ms after each scenario's first frames, where the new shaders
compile on first draw (ps-wnj.18 takes the traced warm-up for every preset).
The first peak run stopped on "the plugin's analysis stayed at 0.000 s for
10 s" before a frame, the music plugin under load; its rerun is
`perf/gpu-probe-peak.json`.
