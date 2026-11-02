# The neon look toned down, in game (ps-wnj.24, 2026-11-01)

The operator on the first neon look: too overwhelming, the grid drowning
everything and the structural crystals flashing far too bright. The look is
now calm by default, and its loudest effects fire only in a special moment
([neon-look.md, Restraint and special moments](../../../../docs/archive/design/neon-look.md#restraint-and-special-moments);
the style guide's [Restraint](../../../../docs/archive/design/neon-look-style.md#restraint)).

Before is an IL2CPP Mac player of 6610faff (the tree this change starts
from), after one of this change, both windowless:

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look restraint-after-edge --at 20,30,40,50,58,70 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look restraint-after-break-edge --at 63.98 --frames 16 --strip 8 \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/restraint-after-edge \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --preset edge
```

(and so for solid/arcade, hologram/phosphor, prism/spectrum).

## Stills and strips

- `game-020.jpg`, `game-040.jpg`, `game-058.jpg`: minute 25 with 300 alive,
  each preset's before (left) and after (right). The grid recedes behind the
  fight. The props' resting edges drop from near white toward the gem. A
  prop struck by a blow no longer flashes solid white: at 40 s the ring and
  the obelisks that glinted white before show their edges after.
- `break-PRESET.jpg`: F7's break of every structure within 15 m (64 s, 16
  frames at 60 fps), before above after. Before, the break whited out the
  frame: the post's flash, every structure and every struck monster at
  white, the grid lit across the screen. After, the break is still the
  frame's biggest moment on its impact frame (a special moment: the break's
  ring lights `_LookSpecial`), but in the gem's cream and the monsters' own
  hues, not a white-out, and the grid's ripple is narrower.

## The grid lands at one L* in every preset

The p99 L* of an empty floor patch (lower right of the 20 s still), the
grid's lines:

| preset | before | after |
| --- | --- | --- |
| edge | 27.5 | 9.2 |
| solid | 8.6 | 8.2 |
| hologram (pure-void, half) | 4.2 | 3.5 |
| prism | 6.4 | 8.0 |

A first pass halved the lines' gain: edge fell to 2.4 and the other presets
to about 1, because the post's void toe is steep and works on a pixel's
brightest channel. One linear luminance in every palette put edge at 16,
solid at 13, prism at 2. So the post now publishes its toe (`_LookToe`) and
the floor sets the lines' colour back through it, to L* 12 (`_LineL`).

## Metrics (steady stills; full tables in metrics/)

| measure | before | after |
| --- | --- | --- |
| stills with a flash (near-white region over 0.3% of the frame) | 4 of 6 in every preset | 2 of 6 (edge, solid), 3 of 6 (hologram, prism) |
| largest near-white region, worst still per preset | 1.1-2.3% of the frame | 0.4-0.7% |
| white share (`white_max` 2%) | up to 3.3%, broken in edge, solid, hologram | up to 1.0%, passing |
| background light (`background_light_max` 0.15) | 0.02-0.15, hologram's 40 s breaks it | 0.02-0.14 |
| swarm separation | edge 11.4-18.7 | edge 16.1-22.6; the others level |
| halo | 6.9-24.9 | 4.8-24.7 (bloom and halo cut; `halo_min` failed before too) |

The restraint rules (`flash_frames_max`, `background_light_max`) pass in
every preset after; before breaks `background_light_max` once. The rules
still failing after (`halo_min`, `swarm_separation_min`) failed before too,
in the same presets: they are the post's and the monster looks'.

## The tier keeps the timing

- `restraint-PRESET.json`: `game beat` (game/evidence/beat/README.md's peak
  command plus `--look PRESET`): 2399 frames, 14317 moments, 0 off their
  frame in every preset.
- `perf-PRESET/`: `game perf --only peak,break --player-args "--look PRESET"`,
  GPU-complete 1080p frames in ms, no frame near 33 ms:

| preset | peak probe p50/p99/max | break probe p50/p99/max |
| --- | --- | --- |
| edge | 3.18 / 5.76 / 5.76 | 4.31 / 5.61 / 5.61 |
| solid | 2.96 / 4.63 / 4.63 | 6.39 / 7.30 / 7.30 |
| hologram | 6.60 / 7.36 / 7.36 | 5.29 / 6.23 / 6.23 |
| prism | 3.46 / 11.73 / 11.73 | 3.14 / 4.95 / 4.95 |

Prism's break had a 38.7 ms first frame (shader warm-up on the first draw,
as ps-wnj.8 and ps-wnj.6 saw; the build's traced warm-up is ps-wnj.18's).

The operator's check is in game/evidence/operator-pending.md (ps-wnj.24).
