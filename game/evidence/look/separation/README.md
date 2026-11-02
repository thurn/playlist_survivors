# The line looks' outline hulls in the game (ps-wnj.28, 2026-11-01)

The monster looks are shared, so ps-wnj.28's outline hulls (edge, hologram
and gem; [neon-look.md, How clumps part in the line looks](../../../../docs/design/neon-look.md#the-axes))
draw in the game too. Before is ps-wnj.18's [presets evidence](../presets/README.md)
(staging ad053d27); after, IL2CPP Mac players of this change, windowless,
on jarvis, from the worktree's root. The crystal cut, which the change was
tuned on: [demo3d/evidence/look/separation](../../../../demo3d/evidence/look/separation/README.md).

## Stills and metrics

ps-wnj.18's recipe:

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look ps28-edge --at 20,30,40,50,58,70 --readability \
  --evidence look/separation/stills-edge \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/ps28-edge --preset edge \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json \
  --out game/evidence/look/separation/metrics-edge
```

(and so for solid/arcade, hologram/phosphor, prism/spectrum).
`stills-PRESET/`: the six stills of edge, hologram and prism (solid's are
ps-wnj.18's: its shaders only moved their outline function).
`metrics-PRESET/`: every rule on every still, and the swarm overlay of each
still that breaks separation.

| preset | swarm separation before | after | other rules |
| --- | --- | --- | --- |
| edge | 16.1-22.2, passes | 16.0-22.1, passes | every rule passes, as before; the knight's margin at 40 s 6.8 (was 5.8) |
| solid | 12.0-17.7 (58 s breaks) | the same | unchanged |
| hologram | 8.1-13.4 (5 break) | 9.3-13.6 (4 break) | form at 70 s passes now (32.5, was 29.8); background light at 58 s still 0.161 (0.163) |
| prism | 9.6-15.6 (4 break) | 10.1-16.2 (3 break) | unchanged |

**Why the game breaks less than it did, but still breaks**: a game body is
85-150 px across at the metric's 960 px (its bodies two to three times
the crystal cut's on screen), wider than the 32 px tile the metric
calls a clump when one hue covers half of it. A dense tile there is one
body's interior, not a clump, and the seam measure reads its lines and fill.
The 4 px hull is thin on a body that size; 8 px moved hologram and prism by
about 1 and raised hologram's background light (two stills over 0.15), so
the hull stays 4 px in both players. Edge's rim boost follows the body's
size on screen (`_RimSmall` under 60 px in radius, the game's 2.0 by 90 px)
because the cut's 6.5 rim on the game's bodies took the knight's margin
over the amber swarm at 40 s to 5.0 (5.8 before, 6.8 after: the hull takes
some of the swarm's rim round him).

## Perf

The line looks draw one more pass, so their graphics states were traced
again and the standard scenarios run in each, as in ps-wnj.18:

```sh
uv run --project demo3d/tools game perf --trace --looks edge,solid,hologram,prism --only peak,break,finale,early,firsts-rest,firsts
uv run --project demo3d/tools game build
uv run --project demo3d/tools game perf --looks edge,hologram,prism --only peak,break,finale,early,firsts --evidence look/separation/perf
```

Nothing is over the budget (`over_budget` and `over_budget_waits` empty in
every scenario). GPU-complete 1080p frames, probe p50 / p99 / max in ms,
and the slowest first-sighting frame (`firsts`, 98 points, the probe's own
warm-up of about 20 ms included):

| preset | peak | break | finale | early | first sightings |
| --- | --- | --- | --- | --- | --- |
| edge | 4.77 / 7.25 / 7.25 | 4.57 / 9.50 / 9.50 | 3.05 / 5.44 / 5.44 | 2.28 / 6.42 / 6.42 | 30.8 |
| hologram | 3.83 / 7.39 / 7.39 | 4.38 / 9.21 / 9.21 | 4.90 / 9.59 / 9.59 | 3.67 / 6.98 / 6.98 | 31.3 |
| prism | 6.59 / 7.37 / 7.37 | 2.50 / 5.07 / 5.07 | 2.80 / 5.65 / 5.65 | 3.83 / 6.34 / 6.34 | 23.7 |

ps-wnj.18's before: peak 3.06-3.52 / 4.81-11.48 ms max, first sightings
23.4-28.4 ms. The main thread's slowest frame over the 30-minute run's
123,868 frames is 2.9-6.5 ms. The trace's own run, before the new passes'
states were warmed, drew the peak's first frames in 113-226 ms: a tree that
changes a look's shaders needs `game perf --trace` before its build.
