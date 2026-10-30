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
window, starting every 5 s after the same 5 s of warm-up and one more ending
at the run's last frame, meets the same lines (the drop share's limit
included), and no frame after the warm-up exceeds twice its line (TD13: the
first frames load, and are reported, not judged). The run is uncapped and
collects frames as the harness does; a window where under half the frames
have a GPU time fails, a GPU time over a second is counted as a garbage
reading, and the machine's samples are recorded with the verdict (a noisy
run is repeated, as the harness repeats one, not failed). The run must cover
the whole song.

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

### Overdrive's offline cost

`--intensity overdrive` (epic ps-5pd) is for a trailer recorded at fixed time on a powerful
machine, so it has no frame-time target: its limits are capacity, and the full song must play
windowless without errors. Measured on the Mac (Apple M5 Max) with the IL2CPP player, the
headline combination `--intensity overdrive --palette neon --monster-style crystal`
([evidence](../../demo3d/evidence/overdrive/showcase/README.md)):

- **It plays.** Windowless at fixed time the whole song reaches `done at song time 198.98` with
  no exceptions, no effect spawns rejected and no effect light dropped: 15,225 effect spawns
  (normal: about 9,500), 4,637 deaths broken into shards and at most 7,475 shards live (the
  feel's shard cap of 240 sets; normal crystal peaks at about 12,000). The player holds up to
  3,983 monsters active, under the crowd's 16,384 slots a type.
- **Simulating it** at fixed time takes 228 s of wall time for the 199 s song, about 19 ms a
  frame without drawing. The crystal shatter's PhysX is most of it: per section, the median
  frame and the shatter's share:

  | section | frame median / max (ms) | shatter (ms) | shards max | particles max |
  |---|---|---|---|---|
  | drop-1 | 18.3 / 108 | 16.2 | 7,260 | 21,452 |
  | groove-2 | 16.6 / 50 | 14.7 | 7,230 | 15,369 |
  | build | 15.8 / 36 | 12.3 | 6,917 | 12,930 |
  | drop-2 | 19.2 / 153 | 16.4 | 7,219 | 45,099 |
  | final-peak | 24.6 / 76 | 21.6 | 7,320 | 56,198 |

  Overdrive with the stock look (no shatter) runs its peaks at a median 1.2 to 2.2 ms a frame
  in real time, its timeline evaluation 0.7 to 1.6 ms of it; normal neon + crystal, 0.3 to
  5 ms.
- **Drawing it:** `--gpu-probe-at` (CPU submission and GPU in series, 1080p, MSAA 4×) gives
  62 to 91 ms a frame at drop-1's slam (49.6 s), drop-2 (136.3 s) and the final peak (173.5
  and 180 s), against 22 to 46 ms for normal neon + crystal at the same times. Expect a
  windowed fixed-time capture of the whole song to take roughly 20 minutes (11,940 frames at
  about 100 ms each).
- **Real time** is not a goal and doesn't hold: unpaced, neon + crystal's peaks ran at a median
  15 to 300 ms a frame, varying run to run, because the shatter's step grows with the frame. A
  real-time run then shatters fewer deaths (about 3,000 to 4,500). Watch it windowed for the
  feel, but record the trailer at fixed time.

### Resident environments' cost

The trailer holds crypt, desert, snow and forest resident in one player
([Environments in one player](trailer.md#environments-in-one-player), bead ps-hgs.1). Measured
on the Mac (Apple M5 Max) with the IL2CPP player, `--resident crypt,desert,snow,forest`
([evidence](../../demo3d/evidence/trailer/residency/README.md)):

- **A switch** costs 0.10 to 0.13 ms of CPU: two scenes' realtime lights and post volumes, the
  camera's culling mask and the new scene's globals. The first version deactivated and
  activated the scenes' roots, about 2,500 renderers each, for 1.1 to 1.9 ms.
- **Frames at a switch** draw no slower than their neighbours: over 12 switches in three
  windowless `--gpu-probe-at` runs at drop-2, the switch frame was at most 1.25 ms over its
  neighbours' mean (draw and switch together), and below it in 10 of 12. Those frames took 3.9
  to 7.6 ms; a single-environment run at the same times, 4.0 to 10.1 ms.
- **Loading** three more environments adds about 0.1 s to a windowless start (3.1 s against
  3.0 s): each sets up as it would alone, its 4096² static shadow map in 4 to 8 ms, then the
  four warm-up frames draw each once off-screen.
- **Memory:** four scenes, four lightmap sets and four 64 MiB static shadow maps, accepted for
  the trailer's powerful machine. Hidden scenes' particle systems and flame flickers still
  update, a small CPU cost a frame.

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
