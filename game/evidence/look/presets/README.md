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
and each main-thread frame of the profile over 33.3 ms; `--budget` exits 1 on
one. Nothing in any preset is over it.

GPU-complete 1080p frames with MSAA 4x (the probe, p50 / p99 / max in ms) and
the main thread's frame (p99 / max):

| preset | peak | break | finale | early | main p99 / max over the 30-minute run |
| --- | --- | --- | --- | --- | --- |
| edge | 5.81 / 8.49 / 8.49 | 3.06 / 7.53 / 7.53 | 3.50 / 8.06 / 8.06 | 2.74 / 5.88 / 5.88 | 1.50 / 8.9 |
| solid | 2.90 / 11.82 / 11.82 | 3.35 / 7.17 / 7.17 | 3.97 / 7.79 / 7.79 | 3.78 / 8.42 / 8.42 | 1.48 / 7.0 |
| hologram | 2.85 / 5.79 / 5.79 | 3.22 / 13.58 / 13.58 | 2.99 / 12.10 / 12.10 | 4.22 / 6.96 / 6.96 | 1.58 / 7.1 |
| prism | 5.86 / 7.68 / 7.68 | 5.10 / 5.68 / 5.68 | 3.35 / 6.66 / 6.66 | 4.45 / 6.42 / 6.42 | 1.49 / 12.7 |

- **First sightings** (`firsts`: the 30-minute run probed at each kind of
  moment's first sight, 98 points; `firsts-rest`: the weapons and set pieces
  that run never shows, 27): the frame that first draws each is at most 23.7
  ms (edge), 24.7 (solid), 30.4 (hologram) and 26.4 (prism). These include
  the probe's own warm-up, about 20 ms at any point.
- **The sim's step** at 285 or more alive: p99 0.21-0.29 ms in every preset.
- **No main-thread frame over 33 ms** in any scenario (the full run's
  slowest 7.0-12.7 ms); allocations 20-24 B a frame over the full run.

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
68.4 s: 33.97 ms, while the main thread's slowest frame there took 5.0 ms).
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

| preset | readability | conformance | form | hero margin | swarm separation | halo | white |
| --- | --- | --- | --- | --- | --- | --- | --- |
| edge | 31.9-51.3 | 0.97-0.99 | 50.1-60.2 | 5.8-24.7 | 16.1-22.2 | 6.4-11.3 | 0.3-0.9% |
| solid | 62.6-68.7 | 0.99-1.00 | 27.6-41.0 | 16.5-20.7 | 11.2-15.3 | 5.0-8.8 | 0.4-1.0% |
| hologram | 33.2-49.5 | 0.95-0.98 | 31.2-47.7 | 20.1-37.3 | 8.6-13.6 | 6.5-25.6 | 0.3-1.0% |
| prism | 43.5-54.6 | 0.86-0.95 | 40.9-52.4 | 9.0-28.0 | 4.7-15.7 | 7.8-14.5 | 0.3-1.2% |

The set's key (median key60 0.65-0.85), floor range and flash share pass
in every preset, and readability, conformance, monster form, value and
white shares pass in every still. Two fixes here:

- **The edge knight's lines** are 1.8 px, from 1.4 (`HeroEdge.shader`): the
  shader was tuned at ps-wnj.9's 36 m framing, and at the game's wider
  framing the knight mid-swing at 40 s sat only 3.1 L* over the amber
  swarm (`hero_margin_min` 5); now 5.8. Brighter glass (0.08) passed too
  (7.7) but fills the dark core edge is named for.
- **The hero margin needs the hero in sight** (look.py, `hero_visible`): at
  30 s the knight stands inside a clump, and the crowd's ID render, which
  marks monsters behind the knight too, left 38 px (2.4%) of its capsule:
  -24 to -46 in every preset, a measure of the shards in the gaps. Under 5%
  clear, the margin isn't measured ([neon-look-style.md](../../../../docs/design/neon-look-style.md#rules-and-thresholds)).

**Still breaking, for ps-wnj.19 and the final Warden:** `halo_min` (15) in
22 of 24 stills, swarm separation (13) in 10 (hologram's and prism's dense
clumps of trolls and bigorks, solid twice), and hologram's 58 s background
light (0.165 against 0.15). They are the post's and the monster looks'
rules, shared with the crystal cut, which ps-wnj.19 is tuning for every
preset now; ps-wnj.18 leaves both alone. The halo rule was calibrated on
GW3 before the restraint pass (ps-wnj.24) turned the bloom down at the
operator's request, and the game's halo light on the floor counts against
`background_light_max`, so the two pull against each other here.

## Zones

Under a world option the zones tint the floor's lines and the fog with the
palette's zone hues (`VoidWorld`, eased over two bars from the change's
downbeat): the run's five zones take a palette's four hues in turn, so lava,
the victory zone, returns to the first zone's hue (an operator row asks
whether that reads as a return).

The operator's checks (a free-mode playlist run in each preset; the look
package on Windows) are in [operator-pending.md](../../operator-pending.md).
