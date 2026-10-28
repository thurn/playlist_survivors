# 3D Tech Demo: Fidelity and Performance

## The Fidelity Floor

The demo's premise is that fighting one monster feels the same as fighting a
horde. That becomes a rule: what each visible monster gets never degrades with
the number of monsters.

Every visible monster keeps:

- **Full skeletal animation.** Locomotion is phase-matched to distance
  travelled, so feet never slide. Clips crossfade, turns are smooth, and every
  monster animates every frame, with no reduced update rate for crowds. This
  rules out per-vertex animation textures, which cannot blend clips.
- **A real cast shadow** in the sun's shadow map, not a blob decal.
- **Its own reactions.** Every hit plays a flinch, a flash, knockback and a
  hit effect. Every death plays a death animation and a death effect. A
  **tick**, a repeat hit from a lingering effect, plays only the flash: no
  flinch, knockback or particles. A tick that kills plays the full death
  (see [Effect design rules for a horde](effects.md#effect-design-rules-for-a-horde)).
- **Dynamic light** from nearby effects.

A **cut** is any change that makes a monster cheaper to draw. A cut is
allowed only when an A/B capture shows no visible difference at 1080p from
the game camera (see [Fidelity A/B Checks](benchmarks.md#fidelity-ab-checks)). Candidate
cuts:

- a decimated mesh for a monster below a screen-height threshold;
- a simplified mesh that draws only its shadow;
- clips baked at 30 Hz instead of the default 60 Hz, and interpolated;
- effect lights clustered on cells larger than the default 1.5 m;
- coincident identical effect spawns merged;
- an effect's large soft glow quad dropped, leaving bloom to draw the halo.

The decimated meshes are **LOD1** and **LOD2**: each monster's mesh reduced at
bake time to about half and about a quarter of its triangles, by a
decimation tool TD11 chooses. LOD0 is the original. Examples of what the rule
permits and forbids:

```
allowed    LOD1 (half the triangles) below 60 px tall, if its A/B passes
allowed    shadows drawn from LOD2, if its A/B passes
forbidden  walking monsters animate at 15 Hz when more than 2,000 are visible
forbidden  blob shadows for monsters more than 20 m from the hero
forbidden  skip hit effects when more than 50 hits land on one beat
```

An **on-screen monster** is a monster instance whose bounds intersect the
camera frustum in that frame, alive or dying. The report counts alive and
dying monsters separately and headlines their sum.

## Performance Budget

The target is 60 fps on an RTX 4060 Laptop, and the client may use 75% of
each frame: 12.5 ms of 16.7 ms. All measurement happens on Cygnus, so the
target budget is translated into pass lines for that machine.

The translation factors are approximate:

- **GPU: Cygnus's RTX 4070 Ti is about 2.5× an RTX 4060 Laptop** (Steel Nomad
  about 5,040 against 2,025).
- **CPU: one i7-12700F performance core is about one performance core** of a
  typical 4060 laptop's processor (an i7-13620H or Ryzen 7 7840HS class
  part), within about 10%. This is an estimate from single-thread benchmark
  scores, and the report states it as an assumption.
- **An uncertainty margin of 1.2×** applies to both, for drivers, thermals
  and the spread of laptop power limits.
- **Unity's job workers are capped at 6.** A 4060 laptop typically has 6 to 8
  performance cores, shared with the main thread, render thread and the OS.
  The 12700F has 8, plus 4 efficiency cores.

```
                  target (RTX 4060 Laptop)   Cygnus pass line
GPU frame         12.5 ms                    4.2 ms   (÷ 2.5 ÷ 1.2)
main thread       12.5 ms                   10.4 ms   (÷ 1.0 ÷ 1.2)
render thread     12.5 ms                   10.4 ms   (÷ 1.0 ÷ 1.2)
job workers       6 to 8 cores               capped at 6
```

The GPU line is "240 fps on Cygnus": 4.2 ms is about 240 fps at 1920×1080.

The **pass rule**: a configuration passes when, over a 20 s steady-state
window after 5 s of warm-up:

- p99 GPU frame time is at most 4.2 ms;
- p99 main-thread and render-thread CPU times are at most 10.4 ms each;
- no single frame exceeds twice its line;
- no particle spawn was rejected, and the light dropped by the effect-light
  pool stayed within its limit (see [Effects](effects.md#effects));
- the machine stayed quiet and cool throughout (see
  [Benchmark Harness](benchmarks.md#benchmark-harness)).

The demo's full song has no steady state. Its run passes when every 20 s
window, starting every 5 s, meets the same lines, and no frame anywhere
exceeds twice its line.

The environment has a ceiling of its own: the empty arena, with lighting and
post-processing, gets at most 1.5 ms of GPU p99 on Cygnus, about 4.5 ms of
the target's 12.5 ms (see [Environment frontier](environment.md#environment-frontier)).

The reserve is checked too, not just assumed. A real game's sim layer must
fit in the 4.2 ms of target CPU the client leaves free. That gives the sim its
own line on Cygnus: **3.5 ms per tick** (4.2 ÷ 1.0 ÷ 1.2), measured by
[Sim tick timing](benchmarks.md#sim-tick-timing).

### Where Cygnus flatters the target

Cygnus's GPU is the target's architecture, so the old Mac caveats are gone:
NVIDIA GPUs shade every fragment that passes the depth test and blend through
memory, on Cygnus as on the target. What remains is a difference of size, not
of kind, and the harness counts the costs that scale worst.

- **Memory bandwidth.** An RTX 4060 Laptop has 256 GB/s, and the 4070 Ti
  504 GB/s: about 2.0×, against 2.5× in compute. Bandwidth-bound passes
  (shadows, transparent effects, post-processing) therefore translate worse
  than the compute ratio.
- **L2 cache.** 48 MB against 32 MB. The larger cache hides some of the
  bandwidth gap on Cygnus, which the bandwidth treatment below and the margin
  cover.
- **Power.** A desktop card at full power against a laptop part whose power
  limit varies by model. The laptop's Steel Nomad score is a typical one, and
  the report says that a low-power 4060 Laptop will fall short of it.
- **Memory speed for the CPU.** Cygnus's DDR4 runs at 2133 MHz, slower than
  a typical 4060 laptop's memory. XMP would raise it, but Cygnus froze at
  startup with XMP on, so it stays off (see
  [Setting up Cygnus](windows.md#setting-up-cygnus)). Its CPU times are
  therefore pessimistic, which understates the frontier where the CPU limits
  it. The harness records the memory speed in every result row.
- **Overdraw counters stay.** Opaque depth complexity and transparent
  fragments per pixel no longer correct for a different GPU design, but they
  still explain what limits a point (see [Measurements](benchmarks.md#measurements)).

A **frontier point** is the largest on-screen count that passes the pass rule
on Cygnus at one combination of the other sweep axes, found by the
[sweep's search](benchmarks.md#the-sweep). Every point gets a **compute estimate** of the
target's GPU time: Cygnus's GPU time × 2.5 × 1.2.

The compute ratio flatters bandwidth-heavy points. So two points also get a
**per-pass estimate**: the envelope point (see [The report](benchmarks.md#the-report)) and
the heavy-load point at the envelope's view width.

- **The capture.** A PIX timing capture at the found count gives each render
  pass's GPU time. Shadow, transparent and post-processing passes are scaled
  by 2.0 × 1.2, and the other passes by 2.5 × 1.2. Their sum is then scaled
  by the run's p99-to-mean GPU time ratio.
- **The step-down.** If the estimate exceeds 12.5 ms, the count steps down by
  5%, and the pass rule and the capture repeat.
- **Other points are not captured.** The report flags those whose overdraw
  counters are high (see [Measurements](benchmarks.md#measurements)).

A frontier point can also be limited by space rather than time. Monsters keep
apart (see [The choreographer](choreography.md#the-choreographer)), so a view holds at most
its **packing capacity**: its visible ground times the roster's densest
packing, about 2.4 monsters per m² for 0.4 m radii. The search's
ceiling is the smaller of 12,000 and that capacity. A point that reaches it
is reported as capacity-bound.

## Engine and Rendering Setup

The project uses Unity 6.6 (6000.6.7f1, installed on Cygnus during
[setup](windows.md#setting-up-cygnus)) with URP. URP's
Forward+ path is required: it shades many unshadowed point lights per pixel,
and it is what the GPU Resident Drawer needs.

The default settings that every measurement uses unless a sweep axis changes
them:

```
renderingPath:  Forward+          hdr: true      renderScale: 1.0
depthPriming:   forced            (SSAO's depth prepass primes the forward pass)
antiAliasing:   MSAA 4x  or  TAA  (TD9 picks; MSAA 4x until then)
sunShadows:     soft, 2 cascades, 2048 px, fitted to the view
effectLights:   point, unshadowed, from a pool (see Effect lights)
post:           bloom, neutral tonemapping, color grading, SSAO, vignette
off:            motion blur, depth of field, film grain, upscaling
```

- **Static environment** rendering climbs its own measured ladder, starting
  with the GPU Resident Drawer (see
  [Environment rendering](environment.md#environment-rendering)).
- **Graphics API:** Direct3D 12 only. The player is forced to it, and every
  result row records the API in use.
- **Scripting backend:** IL2CPP for the benchmark and showcase builds, since
  a shipped game would use it. Mono is allowed for development builds while
  iterating. TD1 records both build times.
- **Benchmark runs** turn vsync off and leave the frame rate uncapped, so frame
  time measures throughput. The showcase build caps at the display rate.
- **The Windows x64 player** is the demo and the benchmark.
