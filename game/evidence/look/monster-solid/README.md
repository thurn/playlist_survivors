# Bright solid emissive in game (ps-wnj.11, 2026-11-01)

The solid preset's monster look, `solid-emissive`
([neon-look.md, Monsters](../../../../docs/design/neon-look.md#monsters)):
saturated self-lit bodies in the type's palette slot, a light from above for
form, a dark band just inside each silhouette, and a thin dark outline hull
round each body, so a pack of one type reads as a pack of individuals. Shards
keep the surface and the outline.

Before and after come from one IL2CPP Mac build: `--look solid` draws the
look; `--look solid --monster-look refractive-gem` (an option not built yet)
draws the crystal stand-in, so only the monsters differ (both on the solid
preset's palette, `arcade`, and post, `punchy-bloom`).

```sh
F="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
P="--file $F --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@18,regrow@24,finale@32"
uv run --project demo3d/tools game look solid-after --at 10,25,28,32.5,33 --readability --player-args "$P --look solid"
uv run --project demo3d/tools game look solid-finale-after --at 32.6 --frames 12 --strip 4 --player-args "$P --look solid"
D="--file $F --start-minute 25 --bot positioning --debug boss:boss-xlmantis@2,stampede@3"
uv run --project demo3d/tools game look solid-dragon-after --at 9,12 --readability --player-args "$D --look solid"
uv run --project demo3d/tools game look-metrics captures/game/look/solid-after --preset solid \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/arcade.json
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look solid" --evidence look/monster-solid/perf
uv run --project demo3d/tools game beat peak --file public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg \
  --seconds 40 --evidence look/monster-solid --player-args "--start-minute 25 --bot positioning --debug ... --look solid"
```

## Stills

`game-T.jpg`: the crystal above, the solid look below, minute 25 with 300
alive. Every type is in them: skeletons and slimes (violet, `enemies[0]`),
demons and bats (magenta, `[1]`), goblins (aqua, `[2]`), mushrooms (cyan,
`[3]`), bigorks (hot orange, `[4]`) and trolls with their large elites
(amber, `[5]`).

- 10 s and 28 s: packs under area weapons in their hit flash.
- 25 s: a clump of large elite trolls overlapping each other, bigorks beside
  them.
- 32.5 s: the finale's packs just before its mass kill; 33 s: their shards
  in flight.
- `game-dragon-12.jpg`: the dragon (`boss.body`, red) among a stampede.
- `finale-after.jpg` and `finale-before.jpg`: the finale's mass kill, 32.600
  s on, a frame apart. It breaks on its impact frame (+2, 32.633 s). The
  shards flash `shatter.core` there and settle into their hues within a few
  frames; the white over +3 to +5 is mostly the post's flash and the blast
  effect.

## Metrics (arcade, solid thresholds)

| rule | crystal | solid |
| --- | --- | --- |
| swarm separation >= 13 | 3.4-6.0, all fail | 14.3-20.7 (33 s, shards in flight: 9.1) |
| palette conformance >= 0.85 | 0.41-0.71, all fail | 0.98-0.99 (33 s: 0.89) |
| monster form >= 20 | 14-20, 4 fail | 23-36 |
| key60 >= 0.55 | 0.51-0.65, 1 fail | 0.59-0.80 |
| readability >= 17 | 52-73 | 59-73 |
| halo >= 15 | 2.8-11.5, all fail | 5.2-17.0, 3 fail |
| rules broken, five stills | 20 | 4 |

[metrics-before.md](metrics-before.md), [metrics-after.md](metrics-after.md).

- **Clumps read as individuals.** Each body's own edge is dark (a band about
  3 px wide inside its silhouette, plus a broad falloff on bodies large on
  screen) and the outline hull lays a 2.5 px dark line round it where it
  stands over a neighbour, so two touching trolls show a dark seam between two
  bright bodies.
- **Form.** The light from above and the band give a body an L* span of 23-36.
- **Bodies hold the guide's rung.** Outside a hit, body L* p50 is 65-71 and p90
  under 81 (25 and 32.5 s). A hit brightens the faces turned to the camera in the body's own
  hue; earlier takes lerped toward white and blew whole packs out (5% clipped,
  8% white at 28 s), as did the impulse rings' light, now softly capped and
  drawn in the body's hue.
- **Halo** is the post's (`punchy-bloom`): a stronger halo (1.6) lifted it to
  11-22 but filled the seams (separation 12.0-12.1 at 25 and 28 s), so the
  post is unchanged here; ps-wnj.19 judges every preset's rules on the
  finished looks.

## Perf: in budget

`game perf --only peak,break,finale` ([summary](perf/summary.json)), load
average 19-27 from other sessions:

| | probe p50 / p99 / max (ms) | main p99 / max (ms) | first frames max (ms) |
| --- | --- | --- | --- |
| peak | 5.68 / 9.20 / 9.20 | 1.49 / 4.66 | 39.0 |
| break | 4.96 / 8.30 / 8.30 | 2.52 / 2.62 | 49.5 |
| finale | 5.06 / 7.51 / 7.51 | 2.72 / 7.07 | 43.7 |

The same scenarios with the crystal stand-in on this build, a few minutes
earlier: peak 6.22 / 7.57, break 6.53 / 10.18, finale 5.70 / 10.30 (probe
p50 / p99). The outline hull doubles the crowd's vertex work, and the solid
body skips the crystal's opaque-texture refraction; they come out level. The
first frames, where the new shaders compile on first draw, match the crystal's
(45-59 ms, once 304 ms on a first run); ps-wnj.18 takes the traced warm-up for every preset.

## The beat audit passes, deaths break on their frame

`game beat peak` with `--look solid` ([peak.json](peak.json)): 2399 frames,
14317 moments, 0 off their frame, the 334 impulse rings included.
