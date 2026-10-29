# TD8: effects — evidence

Everything here was measured on Cygnus (RTX 4070 Ti, i7-12700F, DDR4-2133) on 2026-10-28, IL2CPP
release players, 1920×1080 windowed, MSAA 4×. Every sweep run is marked noisy: TD9's session was
importing, building and sweeping at the same time (other processes 10–28% CPU), and the operator's
pending Windows Firewall prompt sat on the desktop. Treat the times as indicative, not frontier
grade; the comparisons were run back to back under the same conditions.

## Files

- `inventory.md`: the module inventory over every effect candidate (`demo3d unity EffectInventory`).
- `converter-report.md`: every candidate through the converter with no drops or skips
  (`demo3d unity EffectReport`): what converts as is and what fails, with the reason.
- `spike/`: the runtime spike's rows, bench logs and spawn-cost logs; `spike-burst-vfx-left-own-right.jpg`
  shows the spike burst on both arms (VFX Graph left, ours right) at four times.
- `ab/ab-<effect>.jpg`: the A/B stills, one per converted effect: the stock prefab on the left,
  our GPU runtime on the right, at 5%, 14%, 30% and 60% of the effect's life (2×2, read left to
  right, top to bottom), fixed time, from the game camera's pitch (`--effect-gallery`).
- `lightpool/`: the light pool sweeps (rows and logs).

## The runtime spike (effects.md, Choosing the GPU runtime)

S4 (effects only, monsters hidden), heavy load, 40 m, N 2,600 requested (the stress timeline holds
about 700–930 on screen at this N and view), every timeline effect spawned as one billboard burst
(20 additive billboards, 0.35–0.8 s), five copies per event so each beat frame spawns 450–550
bursts. Lights off on both arms. 20 s runs after 5 s warm-up, three attempts each.

```
spike: billboard burst, S4 heavy, 40 m, N 2,600   (alive particles: own ~10-11k, VFX ~63)
                    gpu p99        main p99      beat-frame spawn cost (p50 / p99)   authoring
own runtime         0.23 ms        0.96-0.99 ms  0.11-0.13 / 0.14-0.23 ms (~450-550)  code only
VFX Graph template  0.21-0.23 ms   0.82-1.74 ms  0.06-0.09 / 0.08-0.18 ms (~450-550)  Simple Burst edited as text
```

- **The VFX row does not carry the load.** The template is VFX Graph 17.6.0's Simple Burst,
  edited as text by `demo3d unity VfxSpikeTemplate` (Editor/Effects/VfxSpikeTemplate.cs): the
  burst's count, lifetime, speeds; capacity 65,536; bounds over the bench ground and culling off;
  gravity and drag out of Update; one added block, Set Position (Add, from the event's source).
  It imported with no repair, and one spawn renders like ours (the still). But the spawner fires
  one burst per frame however many OnPlay events arrive, so S4's beat frames produce one burst,
  not 450: about 63 particles alive against our 10–11k. Its GPU time is therefore not comparable.
- **The buffer route needs hand building.** Driving the template from a spawn GraphicsBuffer (the
  design's route) needs a Sample Graphics Buffer operator, an exposed GraphicsBuffer property and
  links from spawnIndex into Initialize's blocks: new operator, parameter and slot objects with
  cross-referenced ids in the .vfx YAML, which is an authoring job, not an edit of a sample. A
  Custom HLSL block reading a global buffer was considered and not tried.
- **Decision: our runtime wins**, by the rule's second clause: VFX Graph's template could not be
  built to the spike's load without hand-building the asset (and its measured row covers ~0.6% of
  the particles). The package was removed again; the arm's code (Runtime/VfxSpike, compiled only
  while com.unity.visualeffectgraph is present) and the template writer stay for a rerun. No VFX
  Graph feature was found that disabled batching, since one system served every spawn.
- The first VFX attempt (rows 1–3 of `spike/spike-ab/rows.jsonl`) drew nothing at all: Simple
  Burst's 7 m bounds culled the system. `spike-vfx2` is the corrected template.
- Our beat-frame spawn cost is the CPU time of `EffectDriver.Frame` plus `EffectSystem.Update`
  (spawn expansion, the light pool, one upload and the command buffer) on frames with 100 or more
  spawns.

## The light pool (effects.md, Effect lights)

S3 (crowd, hero, effects and lights), heavy load, 40 m, N 2,600 requested (690–910 on screen),
three 20 s runs per size, every run noisy (`lightpool/`). The pool's lights are unshadowed point
lights taking the strongest 1.5 m clusters by screen contribution.

```
pool   lights used (max)   drop share p99        gpu p99 (mean)            main p99
64     64 (saturated)      0.124-0.226  FAIL     5.36-6.90 (4.42-5.37)     2.9-4.8 ms
128    92-103              0                     5.93-7.63 (4.64-5.73)     3.1-6.8 ms
256    92-103              0                     5.80-7.39 (4.52-5.51)     6.8-8.0 ms
active cues 126-141 a frame; Forward+ visible additional light limit: 256 (logged at start)
```

- **URP 17.6's Forward+ limit is 256** visible additional lights on desktop
  (`ShaderOptions.k_MaxVisibleLightCountDesktop`, `MAX_VISIBLE_LIGHT_COUNT_DESKTOP`; the player
  logs `UniversalRenderPipeline.maxVisibleAdditionalLights` = 256 on Cygnus's D3D12). A pool of
  256 would leave no room for the arena's own lights (TD9's braziers and candles), which URP would
  then drop by its own ordering.
- **Fixed at 128.** 64 saturates and drops 12–23% of the visible cue intensity (the 2% rule
  fails); 128 and 256 never drop and use at most 103 lights; their GPU times differ by less than
  the run-to-run noise. 128 keeps half of Forward+'s budget for the environment.
- **What it doesn't show.** The stress timeline holds only 690–910 on screen at this request, so
  this is heavy load at under half the envelope's count. Cues grow with kills, and 1.5 m
  clustering grows sub-linearly; TD12's sweeps check the drop share at the envelope through the
  pass rule (a p99 above 2% fails the point).
- The GPU p99 of 5–7 ms fails the 4.2 ms line here, and transparent fragments per pixel run
  10–12 (beat peak 13–15), flagged over 5: heavy-load effects at full Hovl size, glow quads kept.
  That is TD11's cut list (glow quads, merging) and TD12's measurement, not a pool effect.

## The A/B stills

`ab/`: 20 effects, each against its stock prefab (left). The stock prefab is simulated with
Shuriken's own `Simulate` at a fixed seed, one loop, its vendor scripts disabled (they aren't
synced: Hovl's light fades and laser raycasts), and children whose vendor shader doesn't exist in
this project (Hovl's distortion, drawn magenta) hidden. Ours runs on the GPU runtime at the same
fixed times. Differences to look for: curl noise instead of Shuriken's noise, trails drawn as
stretched billboards, sword-slash shading, the laser's beam (the stock beam needs its script),
and colour where Hovl's graphs add effects we don't reproduce. `song-141.5s.jpg` is the song at
141.5 s (drop-2) with every effect on, from the capture tool.
