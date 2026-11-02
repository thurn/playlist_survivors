# The game on every look preset (ps-wnj.18, 2026-11-01)

The game runs in each of the neon look's four presets (`--look edge`, `solid`,
`hologram`, `prism`; [neon-look.md, The Game](../../../../docs/design/neon-look.md#the-game)):
in the frame budget, every key moment on its frame, free mode's heard-time
strikes drawn. `just game --look prism` (and the per-axis flags) plays one;
[SETUP.md](../../../../SETUP.md#launch-options) lists them.

IL2CPP Mac players of this change, windowless, on jarvis (M5 Max), from the
worktree's root.

## Perf: in budget in every preset

```sh
# once per machine (and after a look's shaders change): trace every preset's graphics states into the build's warm-up
uv run --project demo3d/tools game perf --trace --looks edge,solid,hologram,prism --only peak,break,finale,early,firsts-rest,firsts
uv run --project demo3d/tools game build
# the standard scenarios once per preset (about 25 minutes)
uv run --project demo3d/tools game perf --looks edge,solid,hologram,prism --evidence look/presets/perf
```

`game perf --looks` is new: the scenarios once per preset, each preset's
summary and probes in `perf/PRESET/`. Every summary now carries the budget's
verdict, `over_budget` (unity-game.md, Performance targets: no frame over
33 ms on the Mac): each counted probe frame, each first sighting's first frame
and each main-thread frame of the profile over 33.3 ms whose own work (its CPU
time) is too; `--budget` exits 1 on one. A main-thread frame over it on little
CPU time waited on the machine, not on its own work (ps-msz.8.9's reading of
`cpu_ms`), and goes to `over_budget_waits`. Nothing in any preset is over the
budget.

On the rebased tree (staging d8dd782d, ps-wnj.19's retuned looks and post),
GPU-complete 1080p frames with MSAA 4x (the probe, p50 / p99 / max in ms) and
the 30-minute run's main thread (p99 / max in ms, and the most CPU time any frame took):

| preset | peak | break | finale | early | 30-minute run, main |
| --- | --- | --- | --- | --- | --- |
| edge | 3.06 / 10.29 / 10.29 | 2.49 / 5.62 / 5.62 | 3.21 / 5.31 / 5.31 | 3.31 / 11.47 / 11.47 | 1.73 / 8.0, CPU ≤ 4.7 (first take: a 42.2 ms frame on 4.0 ms of CPU) |
| solid | 3.62 / 5.71 / 5.71 | 6.18 / 6.76 / 6.76 | 6.63 / 10.40 / 10.40 | 3.28 / 6.75 / 6.75 | 1.45 / 10.0, CPU ≤ 3.9 |
| hologram | 3.52 / 4.81 / 4.81 | 6.59 / 7.87 / 7.87 | 2.94 / 6.21 / 6.21 | 2.91 / 7.15 / 7.15 | 1.58 / 4.6, CPU ≤ 3.7 |
| prism | 3.48 / 11.48 / 11.48 | 5.55 / 13.11 / 13.11 | 5.67 / 7.98 / 7.98 | 3.25 / 6.03 / 6.03 | 2.02 / 11.7, CPU ≤ 3.8 (first take: a 67.8 ms frame on 1.9 ms of CPU) |

The first take ran while ps-wnj.28 rendered crystal-cut captures on the same
machine: edge's and prism's 30-minute runs each had one main-thread frame
over 33 ms with almost no CPU time of its own (`over_budget_waits`), and the
probe's tails rose a little everywhere. Re-run (`game perf --looks edge,prism
--only full`, `full-rerun` in their summaries), neither came back.

- **First sightings** (`firsts`: the 30-minute run probed at each kind of
  moment's first sight, 98 points; `firsts-rest`: the weapons and set pieces
  that run never shows, 27): the frame that first draws each is at most 23.4
  ms (edge), 25.3 (solid), 28.4 (hologram) and 25.9 (prism). These include
  the probe's own warm-up, about 20 ms at any point. ps-wnj.19's shader
  changes needed no new trace.
- **The sim's step** at 285 or more alive: p99 0.21-0.41 ms (0.21-0.29 on a
  quiet machine, before the rebase).
- **Allocations** 20-24 B a frame over the full run, as before the epic.

The same run before the rebase, on a quiet machine, had every probe p99
under 13.6 ms and no main-thread frame over 12.7 ms (its summaries are in
this change's history).

**What broke the budget: first sightings compiling the looks' shaders.**
The build's graphics-state warm-up (unity-game.md, ps-msz.8.9) is traced per
machine, and no trace on jarvis held the neon looks' states: prism's break
had a 38.7 ms first frame in ps-wnj.24's evidence. The trace now covers
every preset, each traced into the one file (a trace adds to it): 44
variants after edge, 52 after solid, 61 after hologram and 70 in 94 states
after prism, the looks' own monster, hero, shard, grid and post shaders.
`--look classic` isn't traced: its states are the night's, a parity check,
not a preset a player picks.

**A timed point's first frame is not a frame shown.** Each probe point's
first hand-drawn frame re-creates the frame's pooled targets: 18.8 to 34.0 ms
at every point, the run's quiet first minute too (solid's finale point at
68.4 s before the rebase: 33.97 ms, while the main thread's slowest frame there took 5.0 ms).
It was never counted in the probe's stats, and the verdict leaves it out too;
a first sighting's first frame counts, since that is what a first use costs.

## Beat: every moment on its frame, in every preset

```sh
F=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
P="--start-minute 25 --bot positioning --debug evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5,break@18,regrow@24,finale@32"
uv run --project demo3d/tools game beat peak-edge --file $F --seconds 40 --evidence look/presets/beat --player-args "$P --look edge"
H=public/reactive/house-128/house-128.ogg
Z="z@9.3-9.32,z@9.8-9.82,z@10.3-10.32,z@10.8-10.82,z@11.3-11.32,z@11.8-11.82,z@12.3-12.32,z@13.1-13.12,z@13.9-13.92,z@14.6-14.62"
FREE="--mode free --run --start-minute 2 --no-cards --walk left@9-13,right@13.5-15,$Z --debug accent@10.75"
uv run --project demo3d/tools game beat free-edge --file $H --seconds 16 --evidence look/presets/beat --player-args "$FREE --look edge"
uv run --project demo3d/tools game beat heard-edge --file $H --seconds 16 --evidence look/presets/beat --player-args "$FREE --walk-lead 0.3 --look edge"
```

(and so for solid, hologram, prism; the free runs under classic too).

| audit | frames | moments | off their frame | blades | deaths |
| --- | --- | --- | --- | --- | --- |
| peak, each preset | 2399 | 15199 | 0 | 109 of 109 | 652 of 652 |
| free, each preset | 959 | 4854 | 0 | 10 of 10 | 175 of 175 |
| free, classic | 959 | 4596 | 0 | 10 of 10 | 175 of 175 |
| heard (`--walk-lead 0.3`), each preset | 959 | 4982 | 260 late, every one settled past its frame | 10 of 10 | 180 of 180 |
| heard, classic | 959 | 4699 | 184 late, likewise | 10 of 10 | 180 of 180 |

The presets differ from classic only in their effects (552 against 294:
the vfx options' slashes, flourishes and warps) and impulse rings; every
preset's run is the same sim, frame for frame.

**Free mode's heard-time strikes.** A live press is heard on the screen's
time and reaches the sim the lead later (about 0.09 s on speakers, 0.3 s on
Bluetooth), so its blow, kills and shatter settle past their frames and are
drawn on the frame they arrive ([press-latency](../../press-latency/README.md)).
A fixed-time run's own lead is about a frame, so its scripted presses
settled ahead of their impacts (the attacks' least margin 68 ms). The new
`--walk-lead S` has each scripted key reach the sim S seconds after its time,
heard at its time, as a live key does (`LoopTests.AScriptedKeyWithALeadReachesTheSimLateAndStrikesInHeardTime`).
With Bluetooth's 0.3 s lead every press's blow settles 0.22 s past its
impact (`settled past their frame`: 10 attacks, 65 deaths, 70 hits, 3
breaks), so the audit counts those moments late, as it must: no presenter
can draw a moment before the sim knows it. What the heard path needs is that
each is drawn on the frame its event arrives, and in every preset it is:
all 10 blades and all 65 late deaths are drawn on the frame that dispatched
their event (matched against `events.csv`'s frame song), and every death of
the run shatters (180 of 180). The presets' extra late moments over classic
are their effects and rings, which ride the same events.

## Stills and metrics

```sh
SONG=public/reactive/cc-kevin-macleod-go-cart/cc-kevin-macleod-go-cart.ogg
EV=evolve:bloody-tear@5,evolve:soul-eater@5,evolve:holy-wand@5
uv run --project demo3d/tools game look ps18-edge --at 20,30,40,50,58,70 --readability \
  --player-args "--file $SONG --start-minute 25 --bot positioning --debug $EV,break@64 --look edge"
uv run --project demo3d/tools game look-metrics captures/game/look/ps18-edge --preset edge \
  --palette unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json --out game/evidence/look/presets/metrics-edge
```

(and so for solid/arcade, hologram/phosphor, prism/spectrum).

`stills-PRESET.jpg`: minute 25 with 300 alive and three evolved weapons, at
the game's own camera (the music's width, wider than ps-wnj.9's 36 m), at
20, 30, 40, 50, 58 and 70 s. `metrics-PRESET/`: the style guide's metrics
per still (metrics.md, metrics.json, the swarm overlays) and each still's
readability against its ID render (readability.json).

| preset | readability | conformance | form | hero margin | swarm separation | background light | white |
| --- | --- | --- | --- | --- | --- | --- | --- |
| edge | 31.9-51.3 | 0.97-0.99 | 50.1-60.2 | 5.9-24.7 | 16.1-22.2 | 0.03-0.12 | 0.3-0.9% |
| solid | 62.1-68.5 | 0.99-1.00 | 31.8-45.2 | 16.8-20.7 | 12.0-17.7 | 0.02-0.07 | 0.4-1.0% |
| hologram | 34.2-52.0 | 0.95-0.98 | 29.8-47.5 | 21.5-36.5 | 8.1-13.4 | 0.04-0.16 | 0.4-1.0% |
| prism | 47.3-60.1 | 0.95-0.98 | 45.0-53.9 | 7.4-21.5 | 9.6-15.6 | 0.03-0.10 | 0.3-1.3% |

**Edge passes every rule in every still.** In every preset the set's key
(median key60 0.66-0.85), floor range and flash share pass, and so do
readability, conformance, the hero margin, the value and white shares in
every still. The halo is judged on impact frames only since ps-wnj.19
(`halo_impact_min`): steady stills measure 4.9-25.0, calm by design
(ps-wnj.24). Two fixes here:

- **The edge knight's lines** are 1.8 px, from 1.4 (`HeroEdge.shader`): the
  shader was tuned at ps-wnj.9's 36 m framing, and at the game's wider
  framing the knight mid-swing at 40 s sat only 3.1 L* over the amber
  swarm (`hero_margin_min` 5); now 5.9. Brighter glass (0.08) passed too
  (7.7) but fills the dark core edge is named for.
- **The hero margin needs the hero in sight** (look.py, `hero_visible`): at
  30 s the knight stands inside a clump, and the crowd's ID render, which
  marks monsters behind the knight too, left 38 px (2.4%) of its capsule:
  -24 to -46 in every preset, a measure of the shards in the gaps. Under 5%
  clear, the margin isn't measured ([neon-look-style.md](../../../../docs/design/neon-look-style.md#rules-and-thresholds)).

**Still breaking, the monster looks' (ps-wnj.28):** swarm separation (13)
in hologram (5 of 6 stills, 8.1-11.4), prism (4, 9.6-12.7) and solid (once,
12.0): dense clumps of trolls and bigorks. Hologram also misses two rules by
a hair: background light at 58 s (0.163 against 0.15: its dim wires leave
the weapons' light a bigger share) and monster form at 70 s (29.8 against
30). ps-wnj.28 is tuning the monster looks for separation in every preset
now (it has these numbers), and ps-wnj.18 leaves them alone.

## Zones

Under a world option the zones tint the floor's lines and the fog with the
palette's zone hues (`VoidWorld`, eased over two bars from the change's
downbeat): the run's five zones take a palette's four hues in turn, so lava,
the victory zone, returns to the first zone's hue (an operator row asks
whether that reads as a return).

The operator's checks (a free-mode playlist run in each preset; the look
package on Windows) are in [operator-pending.md](../../operator-pending.md).
