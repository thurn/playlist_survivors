# ps-6y2.8: the edge-cut pipeline and the edge crowd, proven on the skeleton

Measured on jarvis (M5 Max), 2026-11-02, with other beads' sessions building and testing on the same machine
(load average 4-5).

## The skeleton's cut

`champions edgecut review skeleton` (with the warm editor: export 2 s, cut 0.1 s, bake 1 s, review 1 s) wrote
[edgecuts/skeleton/](../edgecuts/skeleton/README.md): black share at game scale 67.6% (idle 67%, walk 72%,
attack 64%; the threshold, 65%, is on the poses' mean), 72 line segments (at most 120), a body of 322 triangles
(300-800), the shield an empty circle of 12 segments over a dark disc, 23 shard pieces, 62 of the rig's 120
entries in the bone texture.

What set the black share: bloom (at a line gain of 1.6 the post's glow lifted the body round every line over
L* 10, 22-44% at game scale; at 0.85, under the bloom threshold, it costs 1-2 points), line caps (additive caps
past each segment's end doubled the light at every polyline joint), chains shorter than 6% of the height, and
fewer feature lines (58 rather than 96). At 56 px tall a monster is mostly line (37-53%); at 226 px, 79-85% black.

## The edge crowd: 1,000 walking skeletons, shards on kill

`just champions-play --headless --edge-crowd 1000 --fixed-time --seconds 30 --bench-out ...` on the Mono player
(release): 1,000 skeletons alive in 1,058 slots (58 spares: each kill hides its slot and brings back the one that
has waited longest), 998 on screen, walking at their own phase and pace in a 33 x 29 m field at the game camera's
angle; 20 a second break into shards, 300 breaks per run.

The headless player draws the camera by hand into a 1080p target and times each frame from before the crowd's
passes to the GPU's completion (a 1-pixel readback), in alternating blocks of 30 frames with and without the
crowd. Metal reports no GPU recorder support (`SystemInfo.supportsGpuRecorder` false), so the crowd's GPU time
alone can't be read: the difference is its cost with the CPU's submission included, an upper bound.

| Run | Frame with the crowd p50 / p99 | Empty frame p50 / p99 | Difference p50 | CPU submission p50 (crowd, empty) |
| --- | --- | --- | --- | --- |
| [1](bench-1.json) | 1.42 / 4.66 ms | 0.64 / 3.83 ms | 0.79 ms | 0.46, 0.21 ms |
| [2](bench-2.json) | 1.05 / 2.25 ms | 0.68 / 1.70 ms | 0.37 ms | 0.55, 0.29 ms |
| [3](bench-3.json) | 1.74 / 4.94 ms | 0.82 / 4.01 ms | 0.92 ms | 0.57, 0.28 ms |

- The whole frame (URP, bloom, 1,000 bodies and lines, the culling, palette and shard passes) completes in
  1.0-1.7 ms p50 including its CPU submission; after submission the GPU needs 0.5-1.2 ms more with the crowd and
  0.4-0.5 ms without. The crowd adds at most 0.4-0.9 ms p50.
- The p99s, 4-5 ms with or without the crowd in runs 1 and 3, are the shared machine's noise (load average 5-7
  from other sessions' Unity builds), not the crowd's; run 2 caught a quieter moment: 2.25 ms with, 1.70 without.
- The proxy budget (plan.md, Performance: GPU p99 at most 1.5 ms at the low tier) needs a GPU-only reading: the
  operator-pending row asks for a windowed run, whose FrameTimingManager does read the GPU, and RC19's
  `champions perf` owns the budget.

Stills (shrunk to 960 px): [crowd-1000.jpg](crowd-1000.jpg), the field at t = 4 s; [shards-3.05.jpg](shards-3.05.jpg)
and [shards-3.30.jpg](shards-3.30.jpg), 40 alive close up: a kill flashing white on its break frame, its pieces
(lines and dark chunks) thrown away from the blow and up, tumbling, then sinking.

## The variant budget

`champions check --variants`: a clean-cache Mono build in 18 s, 235 compiled variants of the 300 budget (the
skeleton's 227 and 8 of the edge shaders: EdgeBody and EdgeLines, 2 per stage each, the crowd's and the shards'
variant). No keyword of URP's reaches them.

## Checks

- `champions test`: 25 of 25 EditMode tests, 3 of them new (EdgeCrowdTests: the culling pass draws only the living
  in view and blends the dying one's pose; a kill's piece starts at its posed pivot, flies along the blow and up,
  tumbles and sinks, and its death ends after 1.8 s; black share counts the silhouette under L* 10), on a synthetic
  one-bone cut, so they need no vendor files.
- The tools' pytest (11 new in test_edgecut.py: hulls closed and inside their part, lines on the creases within the
  count, keep/drop/caps, rigid circles and strokes, pieces, the written cut, the thresholds, the bake's step
  caching) and ruff.
- The guard: the review sheets and stills carry the `ps-render` tag; everything baked stays in `Assets/Baked/`.
