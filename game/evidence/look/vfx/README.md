# Procedural slashes, spins, streaks and smears in game (ps-wnj.15, 2026-11-01)

The vfx axis's two options
([neon-look.md, VFX](../../../../docs/design/neon-look.md#vfx);
[effects.md, The look](../../../../docs/tech-demo-3d/effects.md#the-look)):
`light-streaks` (the edge, solid and hologram presets) and `prism-streaks`
(prism). Under either, the sword's slash, spin and slam are ribbons of light
built in code (the runtime's new arcs, no vendor prefab or texture) in the
palette's `hero.trail`, white at the core; the crystal burst's chips streak
and a velocity smear shoots along the blow; a hit's chips streak with a smear.
`prism-streaks` splits the cut, the spin and the slam's ring into three softer
layers in `enemies[0]`, `[2]` and `[4]`. `--vfx classic` draws the vendor
effects as before, so the strips below differ only in the vfx axis.

Windowless fixed-time Mono Mac player, on jarvis, `--look edge --vfx V`. From
the worktree's root:

```sh
F="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
P="--file $F --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@18,regrow@24,finale@32"
SONG="$PWD/public/reactive/house-128/house-128.ogg"
Z="z@9.3-9.32,z@9.8-9.82,z@10.3-10.32,z@10.8-10.82,z@11.3-11.32,z@11.8-11.82"
FREE="--mode free --run --start-minute 2 --no-cards --file $SONG --walk left@9-13,$Z,z@12.3-12.32 --debug accent@10.75"
L="uv run --project demo3d/tools game look"
for V in classic light-streaks prism-streaks; do
  $L slash-$V --backend mono --strip 4 --crop 560,240,800,600 --at 3.67 --frames 12 --evidence look/vfx/slash-$V --player-args "$P --look edge --vfx $V"
  $L slam-$V --backend mono --strip 4 --at 16.17 --frames 12 --evidence look/vfx/slam-$V --player-args "$P --look edge --vfx $V"
  $L spin-$V --backend mono --strip 4 --crop 460,140,1000,750 --at 10.833 --frames 12 --evidence look/vfx/spin-$V --player-args "$FREE --look edge --vfx $V"
  $L steady-$V --backend mono --at 10,13,25,28 --evidence look/vfx/steady-$V --player-args "$P --look edge --vfx $V"
  uv run --project demo3d/tools game look-metrics captures/game/look/steady-$V --preset edge \
    --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --out game/evidence/look/vfx/steady-$V
done
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look edge" --evidence look/vfx/perf-light-streaks
uv run --project demo3d/tools game perf --only peak,break,finale --player-args "--look prism" --evidence look/vfx/perf-prism-streaks
uv run --project demo3d/tools game beat peak --file public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg \
  --seconds 40 --evidence look/vfx/beat-light-streaks --player-args "--start-minute 25 --bot positioning --debug ... --look edge"
```

## The strips

Each is 12 consecutive frames (1/60 s apart) from the strike's impact.

- **Slash** (`slash-V/`): the peak run's light-combo slash landing at 3.670 s
  (reach 8.44 m). Classic draws Hovl's gold crescent; `light-streaks` a ribbon
  of light whose head sweeps the blade's arc clockwise in 0.036 s (about half
  of it lit on the impact frame, all of it on the next), the light trailing
  behind it and narrowing, with a thinner echo inside; `prism-streaks` three
  bands in the palette's key, accent and counter hues, one inside the other.
  The arc is the crescent's: its middle on the clip's arc, sized to the blade
  (0.75 of the cue's scale, the crescent's visible radius).
- **Spin** (`spin-V/`): free mode's heavy spin, heard-time, landing at
  10.833 s. The ribbon sweeps a little past a full turn in about 0.13 s from
  the impact frame and fades by 0.4 s.
- **Slam** (`slam-V/`): the peak run's slam at 16.200 s (frame +1). A thin
  ring of light flat on the ground races out to the reach in 0.45 s with a
  brief inner flash and sparks thrown up off it, over the strike's crystal
  waves and the impulse ring; `prism-streaks` three rings one behind the other.
- **Projectiles**: the game's bolts already streak (WandView's afterimages,
  ps-msz.8.3; visible in the slash strips, the violet Magic Wand bolt above
  the hero), so the vfx options leave them alone.

## Metrics (ion, edge thresholds)

The steady stills (10, 13, 25, 28 s: packs under area weapons, hits and
deaths) break the same rules under every option, none new: bright share and
white at 10 s, the halo at 13 s, white and the key at 28 s (Bloody Tear's
slashes and the hit flashes, before this change). White share at 10 s and
28 s: classic 3.8% and 5.1%, light-streaks 3.5% and 4.8%, prism-streaks
3.4% and 4.8%. Swarm separation at 10 s is 12.5 (classic), 13.0 and 12.6.
`steady-V/metrics.md` has every number.

## Perf and beat

`game perf` (IL2CPP, peak, break and finale): every scenario ok, no frame
over 33 ms after the first frames.

| preset (vfx) | peak probe p50/p99 | break | finale | main p99 max |
| --- | --- | --- | --- | --- |
| edge (light-streaks) | 3.12/7.04 ms | 2.47/3.59 | 4.51/8.01 | 2.0 ms |
| prism (prism-streaks) | 4.85/8.08 ms | 5.5/11.16 | 4.01/6.55 | 2.0 ms |

The edge run's first frames peaked at 233 ms over its three points (the
first run after the build, warming its shaders); the prism run's at 33.7 ms.

`game beat peak` under both presets: 14317 moments over 40 s, 0 off their
frame, 7311 effects among them on their frames (`beat-V/peak.json`).
