# TD12: the frontier report

The demo's answer to its question (docs/tech-demo-3d/README.md): **on an RTX 4060 Laptop-class
machine, about 500 fully animated monsters on screen at once, in a 30 m view, with the demo's
effect load, at full fidelity and 60 fps** (the envelope, below). Every target number here is
**inferred** from Cygnus (RTX 4070 Ti, i7-12700F, DDR4-2133) through the factors listed under The
envelope; nothing was measured on a laptop.

**Corrected by TD13: the envelope is 425, not 500.** The full song, with the climax held at the
envelope, failed the song rule at 500 (the climax's 20 s windows at GPU p99 4.34 to 4.54 ms) and at
475 (4.22 to 4.50 ms), passed once and failed once at 450 (worst windows 4.176 and 4.208 ms), and
passed twice at 425 (worst 3.90 and 3.86 ms). S3 at 500 holds its line here (3.74 ms); the song's
climax costs more at the same count, with the boss, the laser and the arena around the courtyard
field, while its particles match drop-2's. Everything below is TD12's measurement as it was;
`demo3d/evidence/td13/` has the song runs and plan.md's TD13 entry the steps.

All runs: Cygnus, the IL2CPP release player (`demo3d build --backend il2cpp`, td/td12), 1920×1080
windowed, Direct3D 12, 6 job workers, High performance, driver 610.88, through TD7's harness
(`demo3d sweep`, with the foundations review's GPU-frame fix). The scene: the finished arena (rung
E2, TD14's final rung; TD15's art pass at the authored density 2×) with TD11's accepted cuts as
defaults (shadow proxy from LOD2, 30 Hz clips), TD5's bone-texture crowd, TD8's effects and
128-light pool, TD9's MSAA 4×. Each folder here holds a sweep's `rows.jsonl` (one row per run),
`frontier.jsonl`, the harness's `report.md` (every run), `bench.log` and machine facts;
`table.md` is `summarize.py`'s table of every frontier point. Specs: `demo3d/sweeps/td12/`.

## The frontier (S3, full combat, MSAA 4×)

The largest requested on-screen count that passes the pass rule (GPU p99 ≤ 4.2 ms, main and
render p99 ≤ 10.4 ms, no frame over twice its line, no spawn rejected, light drop within its
limit) at each view width and effect load, found by the harness's fitted search at 5 %
resolution. "On screen" is what the deciding run measured (alive + dying, each monster's bounds
against the frustum); compute estimate = GPU p99 × 2.5 × 1.2 (performance.md). Folder `s3/`
(and `heavy55/`).

| load | view | frontier N | on screen | limit | GPU / main / render p99 (ms) | compute est. | fill count | fills | sim tick p99 |
|---|---|---|---|---|---|---|---|---|---|
| **demo** | **30 m** | **591** | **600** | GPU | 4.17 / 3.81 / 0.79 | **12.52 ms** | 664 | no | 1.70 ms |
| demo | 40 m | 730 | 750 | GPU | 4.20 / 4.22 / 0.86 | 12.59 ms | 1,180 | no | 1.88 ms |
| demo | 55 m | 839 | 863 | GPU | 4.15 / 4.69 / 1.03 | 12.45 ms | 2,232 | no | 1.78 ms |
| light | 30 m | 750 | 748 | GPU | 4.08 / 4.50 / 0.83 | 12.23 ms | 664 | yes | 2.10 ms |
| light | 40 m | 862 | 881 | GPU | 4.13 / 4.89 / 0.89 | 12.39 ms | 1,180 | no | 2.29 ms |
| light | 55 m | 1,070 | 1,094 | GPU | 4.07 / 5.91 / 0.94 | 12.21 ms | 2,232 | no | 2.70 ms |
| heavy | 30 m | 417 | 435 | GPU | 4.04 / 2.70 / 0.79 | 12.13 ms | 664 | no | 0.97 ms |
| heavy | 40 m | 577 | 597 | GPU | 4.07 / 3.15 / 0.74 | 12.21 ms | 1,180 | no | 1.04 ms |
| heavy | 55 m | 735 ¹ | 770 | GPU | 4.14 / 3.79 / 0.91 | 12.43 ms | 2,232 | no | 1.00 ms |

¹ The full sweep found 1,017 here, on a lighter load than heavy (see What's noisy): its noise
repeats fell later in the stress timeline, where heavy load at 55 m draws about half the
particles (8 to 10k against 19k), and the search takes the median attempt. Re-run with no
repeats, so every run measures the timeline's 3 to 23 s (`heavy55/`): 735.

- **Every point is GPU-limited.** The CPU lines are far off: main thread p99 2.7 to 5.9 ms
  against 10.4, render 0.7 to 1.0 ms. Each point's compute estimate is 12.1 to 12.6 ms against
  the target's 12.5 ms, as it must be at the GPU line (4.2 × 3.0 = 12.6).
- **The effect load moves the frontier most:** at 30 m, light 750, demo 591, heavy 417. Wider
  views gain far fewer monsters than their ground (30 to 55 m: +42 % at demo load for 3.4× the
  ground).
- **Only light load at 30 m fills its screen** at one monster per m². No width fills it at demo
  load.
- **Overdraw:** heavy load is flagged at 30 and 40 m (mean transparent fragments per pixel 5.3 and
  7.4, over 5; 3.7 at 55 m; beat peaks 5.3 to 8.2, under 12); demo and light stay at 0.5 to 2.0
  (peaks 1.5 to 6.2). No run rejected a spawn; the light pool's dropped share (p99) stayed at most
  1.9 % in any run, under its 2 % limit, with at most 112 of its 128 lights used.

## The envelope

The rule (benchmarks.md, The report): at demo load with the chosen anti-aliasing (MSAA 4×), the
widest measured view whose frontier count is at least its fill count; if none qualifies, 30 m;
the count is that width's frontier rounded down to 100.

```
frontier (demo load, MSAA 4x):  30 m → 591    40 m → 730    55 m → 839
fill counts:                    30 m → 664    40 m → 1,180  55 m → 2,232
envelope:                       30 m, 500 on screen   (no width fills its screen, so 30 m)
```

**Envelope: 30 m, 500 on screen** (TD13 lowered it to 425 in 5% steps: the song's climax fails at
500, 475 and 450; see the correction at the top). `Choreography/Data/action-level.json` carries it
(`"envelope": { "onScreen": 500, "viewWidthM": 30 }`); its sections' widths were scaled by 0.75
(the zoom arc of 30 to 40 m under the placeholder becomes 22.5 to 30 m), so no section's view is
wider than 30 m.

**Held at the envelope** (`envelope/`: S3, demo, 30 m, 500 requested, three runs): 513 to 518 on
screen, GPU p99 3.60 to 3.77 ms (median 3.74), **compute estimate 10.8 to 11.3 ms (median 11.2)**
against 12.5; main 2.7 to 2.8 ms, render 0.73 to 0.78 ms; sim tick p99 1.38 ms; 13 to 14k
particles. The rounding to 100 leaves 15 % of the frontier unused (591 → 500): TD13's room for the
climax's boss, laser and denser spells.

**What it rests on** (performance.md; every target figure is inferred through these):

| factor | value | source / status |
|---|---|---|
| GPU ratio, Cygnus to the RTX 4060 Laptop | 2.5× (Steel Nomad 5,040 / 2,025) | the design's figure; TD7 found published laptop medians of 2,267 (UL) and 2,236 (Notebookcheck), a ratio of 2.22×: see the alternative line below |
| CPU ratio, one P-core to a typical 4060 laptop's | 1.0× | an assumption from single-thread scores (i7-13620H / Ryzen 7 7840HS class), within about 10 % |
| uncertainty margin | 1.2× on both | drivers, thermals, laptop power limits |
| frame budget | 75 % of 16.7 ms = 12.5 ms | the rest is the sim's reserve |
| pass lines on Cygnus | GPU 4.2 ms, main and render 10.4 ms (p99); sim tick 3.5 ms | 12.5 / 2.5 / 1.2; 12.5 / 1.0 / 1.2; 4.2 / 1.0 / 1.2 |
| job workers | capped at 6 | a 4060 laptop has 6 to 8 P-cores |
| memory | DDR4-2133 on Cygnus | slower than a typical laptop's: CPU times pessimistic (they never limit here) |
| fighting density | 1 monster per m² of visible ground | the fill counts (computed ground 664 / 1,180 / 2,232 m²) |
| the laptop | a typical, full-power RTX 4060 Laptop | a low-power one (Notebookcheck's minimum Steel Nomad is 1,829) falls short |

**With the published Steel Nomad median instead (the alternative GPU line, 4.69 ms;** `gpu469/`,
the 30 m demo point searched again with `lines.gpuMs` 4.69): **655** (661 on screen), still just
under the 30 m fill count of 664, so the rule gives **30 m, 600**. The design's 4.2 ms line is
about 11 % conservative against the published median; the level script keeps the design's 500.
For the other points the 4.69 ms count is only fitted from each point's own runs (`table.md`'s
last column), an inference that overestimated the one searched value by 13 % (741 against 655).

### Per-pass estimates (PIX): not obtained

`demo3d pix` at the envelope point (S3, demo, 30 m, n 591, MSAA 4×) and at the heavy-load point at
its width (heavy, 30 m, n 417) now **captures** (145 MB, and 130 MB with `--frames 1`; kept in
`D:\wt\td12\demo3d\Logs\pix\`, gitignored), but PIX's replay, which times each pass, stops at
`E_PIX_FEATURE_REQUIRES_DEVELOPER_MODE`: Developer Mode is off on Cygnus (an administrator
setting; Pending on the operator). Logs: `pix/`.

- Found on the way: `demo3d pix` had never written a capture. Under pixtool the programmatic
  capture's file name isn't used: pixtool holds the capture and closes the player once it has it.
  The command now ends `programmatic-capture --open save-capture <dir>\capture.wpix` (pix.py and
  its test). The heavy point's 3-frame capture lost the player twice ("process under capture was
  terminated"); one frame worked.
- **So the compute estimate stands everywhere.** It bounds the per-pass estimate from above: the
  per-pass method scales bandwidth-heavy passes (shadows, transparents, post) by 2.0 × 1.2 instead
  of 2.5 × 1.2 and rescales the sum to p99, so it lands between GPU p99 × 2.4 and × 3.0. At the
  envelope point (591) that is 10.0 to 12.5 ms; at the envelope itself (500), 9.0 to 11.2 ms; at
  the heavy 30 m point (417), 9.7 to 12.1 ms. No step-down is needed at 500 by either estimate.
- performance.md says bandwidth-bound passes "translate worse than the compute ratio", but its own
  factors make them translate better (2.0× against 2.5×: the laptop's bandwidth is a larger share
  of Cygnus's than its compute). On either reading the compute estimate is the conservative one.

## The other anti-aliasing mode (TAA), at the envelope point only

`taa/`: the 30 m demo-load search with TAA reached the harness's ceiling (the packing capacity,
1,593 requested) without failing: **at least 932 on screen** (the stress scene puts no more on a
30 m screen) at GPU p99 3.67 ms (compute estimate 11.0 ms). With TAA the 30 m point fills its
screen. At that count the sim tick reaches 7.97 ms (below).

Matched counts (`aa-ab-s3/`, `aa-ab-s1/`: 591 requested, 30 m, demo, MSAA and TAA interleaved
twice, three attempts each):

| scene | MSAA 4× GPU p99 | TAA GPU p99 | TAA saves |
|---|---|---|---|
| S3 (crowd, hero, effects, lights) | 4.10 to 4.15 ms | 2.52 to 2.58 ms | 1.57 ms (38 %) |
| S1 (the crowd walking, no effects) | 2.66 to 2.79 ms | 2.20 to 2.36 ms | 0.50 ms (18 %) |

TD9 measured TAA saving 11 to 13 % (S1 and S3 at 800 and 1,200, 40 m, before TD11's cuts and
TD15's art pass). Today most of MSAA's cost in S3 sits with the effects: about 1.1 of its 1.6 ms
appears only when effects draw (by subtraction; no per-pass capture). TD9 kept MSAA 4× because TAA
left the horde's moving edges and the bolts stepped (its captures; Pending on the operator), so
the envelope stays at MSAA. Two leads, not measured: the effects pass under MSAA (blending at 4
samples, or the depth input it asks for) may cost more than it must, which would raise the MSAA
frontier; and TAA's look would need judging again before anyone moved the envelope to it.

## Walls (S6) and the sim

**S6 at the envelope holds.** `s6/` (S3 at demo load with the hero holding the walled courtyard,
the horde funnelling through its four gaps; 30 m, MSAA 4×):

| S6 | on screen | GPU p99 | main p99 | sim tick p99 (max) | pass |
|---|---|---|---|---|---|
| **500 requested (the envelope)** | 453 to 480 | 3.16 to 3.37 ms | 2.80 to 2.97 ms | **2.70 ms** (5.17) | **3 of 3** |
| 591 requested (the S3 frontier) | 513 to 566 | 3.48 to 3.79 ms | 3.27 to 3.39 ms | 3.25 ms (6.02) | 3 of 3 |
| S6's own frontier: 873 requested | 627 (median attempt; 627 to 727) | 4.19 ms | 4.25 ms | 4.62 ms (7.90) | GPU-limited |

The courtyard rations the screen (873 requested holds about 630 on screen), and at the gaps the
overdraw rises (transparent fragments per pixel 2.2 to 3.1 at 500, 5.5 to 5.9 at 873).

**The sim fits its 3.5 ms reserve at the envelope:** its tick p99 is 1.38 ms in S3 at 500 and 2.70
ms in S6 at 500, and 0.97 to 2.70 ms at every S3 frontier point (the table above). It is the plain
C# choreographer on one thread at load, a conservative stand-in (benchmarks.md, Sim tick timing).
Where it stops fitting:

- S6 crosses 3.5 ms between 591 (3.25 ms) and 846 requested (4.58 ms): **S6's own frontier (873,
  4.62 ms) is past the sim's line**, so among walls the sim, not the GPU, would limit first, at
  about 600 to 650 requested.
- S3 crosses it between 846 (2.56 ms) and 1,294 requested (4.93 ms) at demo load; the TAA point's
  1,593 reads 7.97 ms. A TAA build that filled its 30 m screen would need a faster sim (Burst,
  jobs), not a faster GPU.

## Against the stock baseline

`baseline/`: the same S3 point (demo, 40 m, MSAA 4×, same arena and effects) drawn by TD5's
spec-literal stock arm (`crowd: baseline`: a GameObject, Animator and SkinnedMeshRenderer per
monster, CrossFade on clip changes, Unity's GPU skinning):

| crowd path | 40 m frontier | on screen | limit | GPU / main p99 at the frontier |
|---|---|---|---|---|
| bone-texture path (TD5, with TD11's cuts) | 730 | 750 | GPU | 4.20 / 4.22 ms |
| stock Unity (baseline) | 362 | 377 | main thread | 3.77 / 10.06 ms |

The bespoke path reaches **2.0×** stock Unity's count. Stock Unity is main-thread-bound (its
Animators and renderers take 10 ms at about 370) and its GPU time is already near the line there,
so a faster CPU would not take it much further. (The baseline ignores tints and blends, biases in
its favour; TD5's notes.)

## The environment (TD14 and TD15, carried here)

The frontier above is measured in the finished scene at the authored density (2×), so the count
already pays for the environment. From TD14's and TD15's evidence (`evidence/td14/r3/`,
`evidence/td15/s0-headroom/`), S0 (the empty arena with lighting and post) at E2, GPU / main /
render p99 at 40 m and 55 m:

- **The density curve:** 0.5× 0.45 / 0.91 / 0.59 and 0.47 / 0.99 / 0.64 ms; 1× 0.46 / 1.04 / 0.69
  and 0.51 / 1.11 / 0.73; 2× 0.47 / 0.91 / 0.61 and 0.55 / 1.01 / 0.67; 4× 0.48 / 0.93 / 0.60 and
  0.59 / 1.10 / 0.70. At the authored 2× with the art pass's final dressing: 0.49 / 0.91 / 0.60 and
  0.56 / 1.03 / 0.67, against the ceiling's 1.5 / 1.7 / 1.7 ms.
- **The richness-to-monsters trade:** doubling the decoration costs 0.01 to 0.04 ms of S0's GPU p99
  (0.03 to 0.12 ms of target GPU) and no monsters within the search's 5 % (S3's 40 m frontier 716 at
  1×, 721 at 2×); 0.5× to 4× costs 0.03 to 0.12 ms, 7 to 27 monsters at the frontier's slope. The
  environment is not what limits the count.
- **The final rung against E0 (stock):** S0 GPU +0.02 / +0.04 ms, main −0.12 / −0.14 ms (inside
  E2's own spread, so no win), draws 747 → 326 at 40 m and no kit shadow casters; S3's 40 m
  frontier 716 against E0's 723 (equal within 5 %), and E2 4 to 6 % GPU-slower than E0 in S3 at
  matched counts (the static shadow map's taps on every monster pixel). E2 is kept for the static
  shade, not for time.

TD14's frontier (716 at 40 m, 1×, TD11's defaults) and this one (730 at 40 m, 2×) agree within the
search's 5 %.

## The accepted cuts (TD11)

Every frontier here runs with TD11's defaults. A cut passes at mean FLIP ≤ 0.03042 and p99 FLIP ≤
0.3233 (invisible anchor 0.01608 / 0.1907, visible 0.05911 / 0.5884; S3 at 600, 40 m, demo load;
`evidence/td11/thresholds.json`):

| cut | mean / p99 FLIP | A/B | GPU saved (p50 / p99) |
|---|---|---|---|
| shadow proxy from LOD2 | 0.02524 / 0.2329 | pass | with 30 Hz clips: 0.50 / 0.53 ms in S3 at 600, 1.35 / 1.37 ms in S1 at 1,200 |
| 30 Hz clips | 0.00995 / 0.1385 | pass | none measurable; halves the bone textures |
| **both (the defaults)** | **0.02923 / 0.2408** | **pass** | as the first row |

Rejected: LOD at 60/30 px (passes alone, fails beside the shadow proxy and saves less), at 90/45
and 120/60 px; light cells of 2 and 3 m; merging (saves nothing); dropping glow quads. The
operator's look at the A/B stills and videos is pending (TD11's row).

## What's noisy

- **370 of 372 runs are marked noisy**, and every frontier point rests on noisy runs. Most (307)
  only because up to 12 % of a run's frames had no GPU time (FrameTimingManager never delivered
  one within its 64-frame window; over 0.5 % marks a run noisy, the foundations review's rule).
  Those frames are excluded, not zeroed: if the missing frames are the slow ones, GPU p99 reads
  low. 63 runs also saw other processes over 10 % of the CPU (another session's tests and builds
  in `D:\wt\demo3d`). The window held the foreground in every run (`requireForeground` on; no focus
  loss, no occlusion); GPU 45 to 79 °C, throttle reasons only `power_cap`; memory 2133 MHz; 6
  workers in every row.
- **Each noisy run is repeated twice and the search takes the median attempt.** Repeats at one N
  agree within about 0.1 to 0.3 ms of GPU p99 at most points, so a point's frontier is good to
  about the search's 5 %.
- **The stress timelines aren't perfectly steady.** S6's first 20 s (the opening fight) carry
  about a quarter more effects than later (its first attempts read 0.2 to 0.6 ms more GPU); heavy
  load at 55 m stops the hero's melee after about 15 s, so later windows draw half the particles
  (`dotnet run --project demo3d/dotnet/Demo3D.Choreography.Cli -c Release -- stress S3 --n 1017
  --view 55 --load heavy --seconds 120`: 1,053 hit sparks in the first 10 s, none after 20 s). The
  55 m heavy point was re-run (above). S6's frontier (873) uses the median attempt, mostly the later
  window; on first attempts alone it would sit near 740. The envelope's S6 runs pass in every
  attempt.
- **Sim tick timing** is one load-time build per N on the player's main thread, before the frame
  loop: no contention with frames. Tick maxima (5 to 8 ms at these counts) are single ticks
  (flow-field rebuild slices, garbage collection); the line is on p99.

## What this does not answer

- **A real laptop.** Every target number is inferred through the factors above, not measured on a
  4060 Laptop; laptop power limits and thermals, and AMD or Intel GPUs, are unmeasured. A low-power
  4060 Laptop will fall short of the typical one assumed.
- **The per-pass split.** No PIX timing (Developer Mode), so the bandwidth-heavy passes' share, and
  whether a per-pass estimate would allow more than 500, is unknown; the compute estimate is the
  conservative one.
- **TAA beyond the envelope point.** TAA was measured only at 30 m; whether it would move the
  envelope to a wider view is not measured, and TD9 rejected it on looks.
- **The climax.** The final peak adds the boss, the laser and denser spells to the demo load; heavy
  load's 30 m frontier is 417, under 500, so a climax as heavy as S3's heavy preset would not hold
  the envelope. TD13 tunes it, and steps the envelope down if it must.
- **Hosting the real sim, latency, macOS.** The choreographer's tick timing shows a single-threaded
  C# sim like it fits the reserve at the envelope; it says nothing about the Rust sim in Unity, nor
  about input and audio latency, nor macOS (README, What This Does Not Answer).
- **Memory.** The release player has no GPU memory counter; VRAM on an 8 GB laptop part is
  unmeasured.

## Run commands

From the worktree's root, after `demo3d setup`, `demo3d unity BakeAll` and
`demo3d build --backend il2cpp` (each `demo3d sweep` holds the machine lock):

```
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s3-msaa4-demo.json demo3d/sweeps/td12/s3-msaa4-light.json demo3d/sweeps/td12/s3-msaa4-heavy.json --out <dir>
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s3-msaa4-heavy-55m-first-window.json --out <dir>
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s3-envelope-fixed.json --out <dir>
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s3-taa-30m.json --out <dir>
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/aa-ab-s3-30m.json --out <dir>    (and aa-ab-s1-30m.json)
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s3-gpu469-30m.json --out <dir>
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s3-baseline-40m.json --out <dir>
uv run --project demo3d/tools demo3d sweep demo3d/sweeps/td12/s6-envelope.json demo3d/sweeps/td12/s6-frontier-30m.json --out <dir>
uv run --project demo3d/tools demo3d pix demo3d/sweeps/td12/s3-msaa4-demo.json --point n=591,viewWidthM=30,load=demo,aa=msaa4 --out <dir>
uv run --project demo3d/tools demo3d pix demo3d/sweeps/td12/s3-msaa4-heavy.json --point n=417,viewWidthM=30,load=heavy,aa=msaa4 --frames 1 --out <dir>
uv run --project demo3d/tools python demo3d/evidence/td12/summarize.py demo3d/evidence/td12/s3 demo3d/evidence/td12/heavy55 demo3d/evidence/td12/taa demo3d/evidence/td12/gpu469 demo3d/evidence/td12/baseline demo3d/evidence/td12/s6 --out demo3d/evidence/td12/table.md
```

The demo at the envelope, from `demo3d/`: `Builds\il2cpp\PlaylistSurvivors3D.exe -screen-width
1920 -screen-height 1080 -screen-fullscreen 0` (the level script's envelope is the default).
