# The effects runtime in the look package (ps-wnj.5)

The compute particle runtime (`EffectSystem`, `EffectLibrary`, `EffectLights`,
the sim, sort and draw shaders) and its converter moved into
`unity-packages/com.playlistsurvivors.look/`; both forks are gone
([neon-look.md, VFX](../../../../docs/design/neon-look.md#vfx),
[effects.md, The runtime](../../../../docs/tech-demo-3d/effects.md#the-runtime)).
A selection colour may now name a palette role, drawn in the applied look's
palette. Today's effects keep their literal colours, so nothing changes on
screen until the VFX beads restyle them.

Windowless fixed-time stills of Mono Mac players, on jarvis, 2026-11-01:
before from staging's tree at this change's base (2e004653), after from this
change. Per-pixel RGB differences, 0-255.

## The game plays as before

[before-after.jpg](before-after.jpg) (before above after) and
[strip-before-after.jpg](strip-before-after.jpg) (hits, deaths and Bloody
Tear's tinted slashes, 16.000 s + 5 frames): the run's peak on go-cart with
every evolution, zone changes, an accent, a break and the finale.

| Set | before vs after: largest, pixels over 8 | before vs a second before run |
| --- | --- | --- |
| 9 stills, 13 to 68.8 s | 2-12, 0-29 | 0-14, 0-97 |
| 12 frames from 16.000 s | 3-11, 0-34 | - |

```sh
GC="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
Z="change@20,change@30,change@40,change@50"
PA="--file $GC --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,evolve:unholy-vespers@5,evolve:thunder-loop@5,$Z,accent@56,break@64,finale@68"
L="uv run --project demo3d/tools game look"
$L fx-after --backend mono --at 13,16.1,19.5,22.4,28,42.2,56.65,64.05,68.8 --player-args "$PA"
$L fx-strip-after --backend mono --at 16.0 --frames 12 --strip 6 --crop 360,140,1200,800 --player-args "$PA"
```

## demo3d plays as before

[crystal-shatter-after-vs-crystal-shatter-before.jpg](../../../../demo3d/evidence/look/effects-package/crystal-shatter-after-vs-crystal-shatter-before.jpg)
(the courtyard run's shatter and burst, 3.2 s + 11 frames) and
[action-hits-after-vs-action-hits-before.jpg](../../../../demo3d/evidence/look/effects-package/action-hits-after-vs-action-hits-before.jpg)
(the action cut's hits, sparks and lights, 16 s + 11 frames), before above
after (`demo3d filmstrip --baseline`).

| Set | before vs after: largest, pixels over 8 | before vs a second before run |
| --- | --- | --- |
| crystal filmstrip, 12 frames | 2-9, 0-1 | - |
| action filmstrip, 12 frames | 3-20 | - |
| crystal stills 1.2-10 s | 2-4, 0 | 2-3, 0 |
| crystal stills 20-57.5 s | as between two before runs | 255, 100k-400k: the cut's runs diverge past 20 s |
| action stills 2-40 s | 3-27, 0-28 | - |

```sh
A="demo3d/Builds/mac/mono/PlaylistSurvivors3D.app/Contents/MacOS/Playlist Survivors 3D Demo"
"$A" -batchmode -force-metal --trailer crystal --fixed-time --capture-stills $PWD/captures/d3/crystal-after \
  --stills-at 1.2,3.3,3.45,10,20,33,41.3,45.1,52.85,57.5 --stills-format png --seconds 58
"$A" -batchmode -force-metal --trailer --fixed-time --capture-stills $PWD/captures/d3/action-after \
  --stills-at 2,9,16,24,32,40 --stills-format png --seconds 41
F="uv run --project demo3d/tools demo3d filmstrip"
$F crystal-shatter-after --at 3.2 --frames 12 --columns 6 --backend mono --player-args "--trailer crystal" --baseline crystal-shatter-before
$F action-hits-after --at 16 --frames 12 --columns 6 --backend mono --player-args=--trailer --baseline action-hits-before
```

## Effects respond to --palette

[palettes.jpg](palettes.jpg): a temporary selection, baked for this
evidence only and not committed, naming roles: the slash's and spin's heat
`{ "edge": "enemies.0", "mid": "hero.trail" }` and light `"hero.trail"`, the
hit flash's heat `enemies.0` to `enemies.1`. The heavy strike on house-128
(4.7 to 4.82 s, `--debug accent@4.6`) under `--palette ion`, `arcade` and
`phosphor`: the crescent and its light pool turn cyan, violet and green.
The package's `EffectPaletteTests` check the same without a bake: role slots
follow `ApplyPalette`, literal ones stay.

```sh
H="$PWD/public/reactive/house-128/house-128.ogg"
for p in ion arcade phosphor; do
  uv run --project demo3d/tools game look fx-palette-$p --backend mono --at 4.1 --frames 60 \
    --player-args "--file $H --debug accent@4.6 --palette $p"
done
```

## Perf

`game perf --only peak,break --backend mono`, before and after alternated
twice (Mono players, so for the A/B, not the budget). Every scenario is ok;
the after runs sit inside the spread of the two before runs:

| Run | peak main p50/p99 ms | peak probe p99 ms | break main p50/p99 ms | break probe p99 ms |
| --- | --- | --- | --- | --- |
| before 1 | 1.54 / 2.76 | 11.4 | 2.39 / 3.81 | 6.3 |
| after 1 | 1.88 / 3.82 | 13.8 | 2.51 / 3.96 | 7.0 |
| before 2 | 1.67 / 2.85 | 12.7 | 1.74 / 3.27 | 8.8 |
| after 2 | 1.18 / 2.02 | 8.9 | 2.03 / 3.79 | 6.5 |

All allocate 0 B a frame with 0 GCs.
