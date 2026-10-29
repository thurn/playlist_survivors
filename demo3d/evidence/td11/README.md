# TD11: cuts

Each candidate cut from performance.md (The Fidelity Floor) tried against the calibrated A/B
thresholds of benchmarks.md (Fidelity A/B Checks), with its cost on the harness. Summary and
verdicts: docs/tech-demo-3d/plan.md, TD11.

## Files

- `td11.py`: every step (captures, calibration, scores, costs, frontier, the table). From the
  worktree's root: `uv run --project demo3d/tools python demo3d/evidence/td11/td11.py <step>`.
- `thresholds.json`: the calibrated pass lines (`mean`, `p99`), both anchors' scores and frames,
  the run-to-run floor, and the capture setup. **TD14's E3 and E4 rungs use this file.**
- `scores/<cut>.json`: FLIP per frame against the base (A) run, the run's mean and p99, the pass
  lines and the verdict. `base-repeat.json` is the floor (base against a second base run).
- `costs/<scene>-<cut>.json`: fixed-N harness runs, every cut off against the cut on, interleaved
  (A B, B A), medians of 2 each; `costs/frontier-*.json`: 40 m S3 frontier searches.
- `ab/<cut>-still-<t>.jpg`: the frame with the largest mean FLIP, its centre (480 x 270 px from
  (720, 330)) at 100%: A (every cut off) left, B (the cut on) right. Small `ps-render` JPEGs;
  `invisible-*` and `visible-*` show the two anchors the same way.

The full PNG stills (12 per run) and the 2 s fixed-time videos (`<name>.mp4`, 14 to 16 s) are in
the gitignored `captures/td11/` of the TD11 worktree (`D:\wt\td11\captures\td11\`).

## Method

- **Scene.** S3 (full combat with effects and lights) at demo load and a 40 m view, through the
  demo client (`DemoMain`'s stress path: the game camera, the hero, the arena at E2 1x, MSAA 4x),
  N = 600 (min(2,000, the current 40 m frontier), below).
- **Capture.** Fixed-time mode, 1920 x 1080 PNG stills at 10.0, 11.8, ... 29.8 s (12 frames over
  19.8 s, each on the 1/60 s frame grid; the run starts at 8 s), and a 2 s video from 14 s.
  A is `--cuts none`; B turns one cut on.
- **Score.** `demo3d flip` (NVIDIA FLIP, LDR): per frame pair the mean error and the 99th
  percentile per pixel; a run's mean is the mean over frames, its p99 the largest frame's.
- **Anchors.** Invisible: `--time-offset 0.001` (song time 1 ms later). Visible: `--pose-rate 15`
  (every monster's pose time rounded down to 1/15 s). A cut passes when both its scores are at or
  below invisible + (visible - invisible) / 3.
- **Costs.** `demo3d sweep` fixed-N runs (20 s after a 5 s warm-up) of S3 at N 600 and, for the
  crowd's cuts, S1 at N 1,200 (the crowd alone near its 40 m frontier), in the arena; quiet wait
  capped at 30 s and no repeats, since the A/B interleaving is the noise control.

## Results

Thresholds (`thresholds.json`): a cut passes at **mean <= 0.02999 and p99 <= 0.4295**. Invisible
anchor 0.02336 / 0.3867, visible anchor 0.04324 / 0.5150; the run-to-run floor (base against a
second base run) 0.00098 / 0.0327. The invisible anchor scores well above the floor because a
1 ms shift moves every particle and spark: the lines are as tolerant of effect changes as of
crowd changes.

| Cut (B) | mean FLIP | p99 FLIP | A/B | S3 600, GPU p50 / p99 saved, ms | S1 1,200, GPU p50 / p99 saved, ms | Verdict |
|---|---|---|---|---|---|---|
| lod, 60 / 30 px | 0.01007 | 0.1977 | pass | +0.10 / +0.07 | +0.37 / +0.23 | superseded by 120 / 60 |
| **lod, 120 / 60 px** | 0.02428 | 0.3667 | pass | **+0.53 / +0.67** | **+0.99 / +0.88** | **accepted** |
| shadow-proxy (LOD2) | 0.03048 | 0.2957 | fail (mean) | +0.23 / -0.73 | +1.14 / +1.13 | rejected |
| shadow-proxy (LOD1) | 0.03090 | 0.3011 | fail (mean) | | | rejected |
| **clips30** | 0.00669 | 0.0971 | pass | -0.08 / -0.08 | -0.12 / -0.14 | **accepted** (bone textures about half) |
| light-cell, 3 m | 0.01369 | 0.4726 | fail (p99) | +0.13 / +0.19 | | rejected |
| light-cell, 2 m | 0.00964 | 0.3292 | pass alone | +0.07 / +0.14 | | rejected (fails combined) |
| merge, 8 px | 0.00099 | 0.0343 | pass | -0.01 / -0.04 | | rejected (saves nothing) |
| drop-glow | 0.00592 | 0.3734 | pass alone | +0.02 / +0.03 | | rejected (fails combined) |

Combinations against the same base:

| Set | mean | p99 | A/B |
|---|---|---|---|
| lod 120/60 + clips30 + light-cell 2 m + drop-glow | 0.03477 | 0.5147 | fail |
| lod 60/30 + clips30 + light-cell 2 m + drop-glow | 0.02386 | 0.4702 | fail |
| lod 120/60 + clips30 + light-cell 2 m | 0.03142 | 0.4466 | fail |
| **lod 120/60 + clips30 (the defaults)** | 0.02632 | 0.3682 | **pass** |

- **What the costs don't show.** Every cost run was marked noisy: other sessions (TD14, TD15)
  used 15 to 100% of the CPU and at times a quarter of the GPU, so main-thread times (3 to 28 ms)
  are contention, not the client. GPU differences under about 0.15 ms are within run-to-run
  noise: clips30's -0.1 ms, merge and drop-glow are noise, and light-cell's 0.07 to 0.19 ms are at its edge. Medians of 2 interleaved
  runs per arm.
- **Frontiers.** `frontier-none.json` (every cut off) stopped at 380 on main-thread contention;
  its GPU p99 (2.85 ms at 380, 3.78 at 495, 4.45 at 646) crosses the 4.2 ms line near 600,
  which sets N. `frontier-accepted.json` (the defaults) stopped at 180, main-bound by heavier
  contention (main p99 8 to 34 ms); its GPU p99 at 646 (4.49 ms) was taken while another
  editor used the GPU. Neither is a frontier; TD12 measures it.
- **Merge.** 21 of 2,734 spawns (0.8%) coincided within 8 px at 40 m (8 px is about 17 cm), so
  the cut changes almost nothing and saves nothing measurable.
- **Shadow proxy.** Any mesh other than the drawn one changes every monster's self-shadowing
  (the proxy's surface lies in front of or behind the drawn one), so LOD1 scores as badly as LOD2.
  It is the largest saving (about 1.1 ms in S1): a later try could pair it with a larger shadow
  normal bias, which would change the base too.
- **LOD meshes.** Unity 6.6's MeshLodUtility.GenerateMeshLods (no third-party decimator is
  available offline): LOD1 / LOD2 triangles per type: skeleton 9,310 → 4,735 / 2,368, goblin
  12,457 → 9,051 / 4,543 (its seams stop it at 73%), slime 1,428 → 767 / 383, bat 3,092 → 1,662 /
  832, mushroom 1,638 → 958 / 512, demon 5,992 → 3,070 / 1,536, bigork 6,812 → 3,456 / 1,727, troll
  5,220 → 2,687 / 1,344, dragon 5,996 → 3,072 / 1,535.
- **Base build.** The captures and costs ran on the code of `c18dc83` (with A as `--cuts none`) before the merge of TD10's
  flash tiers; the anchors and every B run share that build.
