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
- `ab/<run>-still-<t>.jpg` (passing cuts, the defaults' set and the anchors): the frame with the largest mean FLIP, its centre (480 x 270 px from
  (720, 330)) at 100%: A (every cut off) left, B (the cut on) right. Small `ps-render` JPEGs;
  `invisible-*` and `visible-*` show the two anchors the same way.

The full PNG stills (12 per run) and the 2 s fixed-time videos (`<name>.mp4`, 14 to 16 s) are in
the gitignored `captures/td11/` of the TD11 review worktree (`D:\wt\td11-review\captures\td11\`);
the first pass's are in `D:\wt\td11\captures\td11\` while that worktree exists.

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

## Results (after the demo systems review, on `demo3d` at 2256e1d)

Re-run in full after the milestone review's findings 2 and 6: the invisible anchor had moved the
camera (punch and shake ran on the offset song time; fixed in 73f463c), and the evidence didn't
record the cut parameters. Every run now passes every parameter (`td11.py`'s `PARAMS` and `ARMS`)
and records the player's own `effective cuts [...]` line (in `args.json`, in each score file's
`args`, and in each cost run). Camera punch and shake stay on, alike in A, B and both anchors.
The arena is TD15's final scene. Its rune circle, mist cards, flame flicker and kit particles had
followed wall time (`_Time`, `Time.time`), which put the run-to-run floor at 0.0103 / 0.369; they
now follow `ArenaClock` (song time in the demo, with fixed particle seeds in fixed time), and the
floor is back to 0.00106 / 0.0374.

Thresholds (`thresholds.json`): a cut passes at **mean <= 0.03042 and p99 <= 0.3233**. Invisible
anchor 0.01608 / 0.1907 (the horde and its effects 1 ms later), visible anchor 0.05911 / 0.5884.

| Run (B) | effective cuts | mean FLIP | p99 FLIP | A/B | verdict |
|---|---|---|---|---|---|
| lod-60-30 | lod, lodPx 60,30 | 0.01886 | 0.3175 | pass | rejected: fails beside the shadow proxy, saves less |
| lod-90-45 | lod, lodPx 90,45 | 0.03599 | 0.4577 | fail | rejected |
| lod-120-60 | lod, lodPx 120,60 | 0.04493 | 0.5253 | fail | rejected (the old default) |
| **shadow-proxy-lod2** | shadow-proxy, shadowLod 2 | 0.02524 | 0.2329 | pass | **accepted** |
| shadow-proxy-lod1 | shadow-proxy, shadowLod 1 | 0.02549 | 0.2352 | pass | not taken: LOD2 is the smaller proxy for the same score |
| **clips30** | clips30 | 0.00995 | 0.1385 | pass | **accepted** (bone textures halved) |
| light-cell-3m | light-cell, lightCell 3 | 0.01235 | 0.4510 | fail | rejected |
| light-cell-2m | light-cell, lightCell 2 | 0.00877 | 0.3030 | pass | rejected: fails in the set |
| merge-8px | merge, mergePx 8 | 0.00108 | 0.0336 | pass | rejected: saves nothing (8 of 2,575 spawns merged) |
| drop-glow | drop-glow | 0.00587 | 0.3711 | fail | rejected |

Combinations (parameters as in their names):

| Set | mean | p99 | A/B |
|---|---|---|---|
| lod 60/30 + shadow-proxy + clips30 + light-cell 2 m | 0.04534 | 0.4198 | fail |
| lod 60/30 + shadow-proxy + clips30 | 0.04093 | 0.3541 | fail |
| lod 60/30 + shadow-proxy | 0.03826 | 0.3530 | fail |
| shadow-proxy + clips30 + light-cell 2 m | 0.03416 | 0.3380 | fail |
| **shadow-proxy (LOD2) + clips30: the defaults** | 0.02923 | 0.2408 | **pass** |
| lod 60/30 + clips30 | 0.02467 | 0.3191 | pass |

Costs of the two passing sets (`costs/`; GPU p50 / p99, every cut off minus the set, medians of 2
interleaved runs, explicit parameters in each run):

| Set | S3 at 600 | S1 at 1,200 |
|---|---|---|
| **shadow-proxy (LOD2) + clips30** | **+0.50 / +0.53 ms** (3.21 → 2.71 p50) | **+1.35 / +1.37 ms** (5.16 → 3.81 p50) |
| lod 60/30 + clips30 | +0.04 / +0.10 ms | +0.26 / +0.24 ms |

- **The defaults** are the shadow proxy from LOD2 and 30 Hz clips (`Cuts.Defaults`); the LOD
  thresholds' default is now 60 / 30 px for runs that turn LOD on.
- **What the costs don't show.** Every run was still marked noisy (other sessions' CPU), though
  main-thread times were near 3.4 ms (S3) and 5.8 ms (S1) this time; differences under about
  0.15 ms are noise. No frontier was re-run; TD12 measures it with the defaults.
- **Merge.** 8 of 2,575 spawns (0.3%) coincide within 8 px at 40 m (about 17 cm).
- **LOD meshes.** Unity 6.6's MeshLodUtility.GenerateMeshLods: LOD1 51 to 59% of LOD0's
  triangles, LOD2 25 to 31% (goblin 73% and 36%).
- **Superseded.** `costs/pre-review/` holds the first pass's costs (before the fixes and TD15's
  scene), with the parameters they ran with reconstructed from that build's defaults.
