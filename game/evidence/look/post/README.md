# The look's post stack in game (ps-wnj.6, 2026-11-01)

The game under each preset's post option
([neon-look.md, Post-processing stack](../../../../docs/design/neon-look.md#post-processing-stack)):
the arena's volume is the look's stack, its night grade gone, with the scene's
exposure (`lighting.exposure`, 0.8 stops) under the options' void toe.

## Stills

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look post-edge --at 20,45,64.1 --readability --evidence look/post/stills-edge \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/post-edge --palette PALETTE.json --preset edge
```

`stills-PRESET/`: minute 25 with the crowd at 300, the third still on a
structure break; `readability.json` beside them. `metrics-PRESET.*`: the
style guide's rules (game writes no view json, so the floor rules fall back to
the frame's p25).

| preset | frame p25 | black share | bright share | clipped, white | halo >= 15 | readability |
| --- | --- | --- | --- | --- | --- | --- |
| edge (neon-bloom) | 0.3-0.4 | 0.61-0.76 | 0.10-0.23, 1 over 0.22 | pass | 11-26, 1 fail | 46-56 |
| solid (punchy-bloom) | 0.3-0.4 | 0.62-0.75 | 0.12-0.28 | pass | 5-11, all fail | 55-66 |
| hologram (scanlines) | 0.2-0.4 | 0.61-0.75 | 0.09-0.21 | pass | 13-25, 1 fail | 43-53 |
| prism (prism-bloom) | 0.1-0.3 | 0.62-0.77 | 0.06-0.14 | pass | 12-35, 1 fail | 37-44 |

Under the void toe the baked arena falls almost to black, so the game already
reads as the void with its monsters and effects as the light; readability
(TD15) passes everywhere. Solid's halo is low by design (a tighter, later
glow for bright bodies). Form, separation and conformance are the monster
looks'.

## Perf: in budget in every preset

```sh
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look edge" --evidence look/post/perf-edge
```

| preset | peak probe p50/p99/max | break | finale | CPU frames over 16.7 / 33 ms |
| --- | --- | --- | --- | --- |
| edge | 5.15 / 8.59 / 8.59 | 4.09 / 7.25 / 7.25 | 5.72 / 10.05 / 10.05 | 0 / 0 |
| solid | 5.95 / 7.51 / 7.51 | 3.77 / 8.21 / 8.21 | 6.15 / 11.27 / 11.27 | 0 / 0 |
| hologram | 6.44 / 7.18 / 7.18 | 6.07 / 9.22 / 9.22 | 4.63 / 10.13 / 10.13 | 0 / 0 |
| prism | 6.93 / 7.81 / 7.81 | 6.19 / 7.01 / 7.01 | 6.48 / 11.11 / 11.11 | 0 / 0 |

GPU-complete 1080p frames with MSAA 4x, in ms; staging's
[perf](../../perf/README.md) had peak 5.09 / 18.7 / 18.7. No frame comes near
33 ms. Each scenario's first probe frame is 30-49 ms (staging's 30-37): the
first draw, where the look's new shader compiles. The build's traced warm-up
(`game perf --trace`, then `game build`) takes it in; ps-wnj.18 runs the game
on every preset. Perf ran on the build before the options' last `whiten`
and exposure values, which change uniforms, not passes.
