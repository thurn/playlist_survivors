# The impulse field and the reactive grid floor (ps-wnj.7)

The look package's impulse field (`ImpulseField`, `Impulse.hlsl`) and grid floor (`GridFloor`,
`GridFloor.shader`) in both players
([neon-look.md, The impulse field](../../../../docs/archive/design/neon-look.md#the-impulse-field)). Blows,
deaths and breaks write rings that set out on their impact frames; the floor's grid is shoved, lifted
and lit as they pass, and between them it is steady and dark. Every world option draws the floor in
place of the arena's ground; the rest of the Synty sets stay until the world kit (ps-wnj.8).

Windowless fixed-time stills of IL2CPP (game) and Mono (demo3d) Mac players on jarvis, 2026-11-01,
in the fallback look (`edge`, palette `ion`), taken before the post stack (ps-wnj.6) landed, under
the arenas' night grades; crowds and effects are unchanged. Rebased onto the post stack,
[post-stack-slam.jpg](post-stack-slam.jpg) is the slam's ring at 16.3 s under `neon-bloom`: the void
black (frame L* p25 0.3 and 2.0 at 13 and 16.3 s), the ring and the shoved grid clear, and the peak
and finale still in budget (probe p99 9.9 and 9.4 ms, main max 2.3 and 6.4 ms, no frame over 33 ms).

## The game

The peak scenario (minute 25, three evolutions, a break at 18 s, the finale at 32 s), from the
worktree's root:

```sh
F="$PWD/public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg"
P="--file $F --start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@18,regrow@24,finale@32"
L="uv run --project demo3d/tools game look"
$L slam-strip --at 16.17 --frames 12 --strip 4 --evidence look/impulse/slam --player-args "$P"
$L finale-strip --at 32.6 --frames 12 --strip 4 --evidence look/impulse/finale --player-args "$P"
$L steady --at 10,13,25,28 --evidence look/impulse/steady --player-args "$P"
uv run --project demo3d/tools game look-metrics captures/game/look/steady --preset edge \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --out game/evidence/look/impulse/steady
```

- [The slam](slam/slam-strip.jpg), 16.183 s on: the sword's slam lands at 16.200 (frame +1) and its
  ring sets out from the hero on that frame, crossing the view in about ten frames with the grid
  shoved outward and lit behind it; the structure it breaks throws its own ring in the shatter's gem
  (top right).
- [The finale's mass kill](finale/finale-strip.jpg), 32.600 s on: 272 deaths at 32.633 (frame +2)
  merge into 47 rings, which run out past the shards as warped white arcs from frame +5.
- [Between impulses](steady/) the floor is the palette's near-black void with a quiet cobalt grid:
  frame L* p25 0.6-1.1 (rule: at most 6) and 53-73% near-black (at least 30%)
  ([metrics.md](steady/metrics.md)). The rules these stills still break are the key, the halo, swarm
  separation and conformance, which the monster looks and the post stack own.

Building this found that the palette's role globals (`_LookVoidFloor`, ...) reached shaders as the
sRGB values unconverted: the void drew at L* 10 instead of 1 and the grid four times too bright.
`Palette.SetShaderGlobals` now sets them as linear vectors (tested).

### The beat audit passes

`game beat peak` (game/evidence/beat/README.md's peak command, `--evidence look/impulse`):
2399 frames, 14317 moments, 0 off their frame ([peak.json](peak.json)). The rings are a moment kind
of their own, `Impulse`: 334 checked, 334 on their frame (each marked on the first frame that
publishes it, with its impact's time).

### Perf at the peak holds

`game perf --only peak,break,finale --evidence look/impulse/perf` ([summary.json](perf/summary.json)),
load average 8-15 from other sessions:

| Scenario | probe p99 (ms) | main p99 / max (ms) | main frames over 33 ms |
| --- | ---: | ---: | ---: |
| peak | 8.65 | 1.52 / 2.21 | 0 |
| break | 6.68 | 1.76 / 1.86 | 0 |
| finale | 12.75 | 2.35 / 6.76 | 0 |

Three earlier takes on this change matched (probe p99 7.0-11.5 ms, no frame over 33 ms). The probe's
cold first frame at each point was 28.6-42.6 ms (the committed baseline in game/evidence/perf:
29.9-36.5 ms), except once: the first run after a rebuild took 434 ms on the break's first probe frame,
a one-off pipeline compile that two more runs of the scenario didn't repeat (35.7, 31.1 ms).

## demo3d's crystal cut

Each shot's rings load at its cut (`ShotImpulses`): the hero's blows weighted by the blow feel, the
deaths that shatter and the formation smashes. The action cut has neither field nor floor.

```sh
F="uv run --project demo3d/tools demo3d filmstrip"
$F impulse-formation --at 4.75 --frames 12 --every 2 --columns 4 --warmup 2 --backend mono --player-args "--trailer crystal" --evidence look/impulse
$F impulse-breath --at 55.85 --frames 12 --every 2 --columns 4 --warmup 3 --backend mono --player-args "--trailer crystal" --evidence look/impulse
```

- [The formation smash](../../../../demo3d/evidence/look/impulse/impulse-formation.jpg) at trailer
  4.80 s (shot 2, crypt; every other frame from 4.75): its rings set out on the impact frame (+2) and
  the front crosses the floor through the horde.
- [The dragon's breath](../../../../demo3d/evidence/look/impulse/impulse-breath.jpg) at 55.90 s (shot
  8): the breath's mass kill and formation smash send lit, warped rings across the clearing from the
  impact frame (+2), while the grid away from it stays steady and dark.
