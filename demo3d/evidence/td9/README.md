# TD9: the arena — evidence

All measurements on Cygnus (RTX 4070 Ti, i7-12700F, DDR4-2133), IL2CPP player, 1920×1080
windowed, 6 job workers, through TD7's harness (`demo3d sweep`). TD8's session ran beside every
sweep, so every row is marked noisy; repeats of the same run agree within 0.01 to 0.05 ms (S0)
and about 0.1 to 0.6 ms (S3 at 1,200), and the tables below give each point's median of its
three attempts. S0 runs at about 1,000 fps, where 25 to 30 % of frames read no GPU time
(TD7 saw the same at 1,000 fps); those frames are excluded, as the harness does, and that alone
marks a run noisy.

- `s0/`: S0 rows (`rows.jsonl`), the harness's `report.md`, `table.md` (every run).
- `aa/`: the AA cost runs (S3, S1) and S6's run, with `table.md`; the AA capture crops.
- `stills/`: the arena from the layout's six shot positions at 1×, E0 and E2 (`ArenaStills`,
  960 px), and the forest edge and courtyard at 0.5× and 4×. `stills/shade/`: heroes on the
  path at E0 (realtime cascades) and E2 (static shadow map), and the rock under the tree, which
  takes the static map at E2.
- `summarize.py` (the tables), `aa_captures.py` (the capture pairs).

## S0 at rung E0, every density (GPU / main / render p99, ms)

E0 is the stock baseline: the kit's prefabs unpacked as plain GameObjects with converted URP Lit
materials, the moon and 9 lights realtime (8 flickering flames, 1 glow), every piece a shadow
caster, no baked lighting. The camera tours the six shot positions, 3 s each, at the point's
view width. Post: bloom, neutral tonemapping, colour adjustments, the night LUT, vignette, SSAO.

| density | kit renderers | LOD0 triangles | 40 m, MSAA 4× | 40 m, TAA | 55 m, MSAA 4× | 55 m, TAA |
|---|---|---|---|---|---|---|
| 0.5× | 976 | 0.33 M | 0.39 / 0.96 / 0.55 | 0.35 / 0.90 / 0.51 | 0.41 / 1.43 / 0.75 | 0.36 / 0.95 / 0.53 |
| 1× | 1,418 | 0.52 M | 0.40 / 0.90 / 0.53 | 0.36 / 0.95 / 0.52 | 0.43 / 0.98 / 0.57 | 0.38 / 0.98 / 0.54 |
| 2× | 2,086 | 0.77 M | 0.42 / 1.00 / 0.54 | 0.37 / 0.99 / 0.52 | 0.46 / 1.03 / 0.59 | 0.40 / 1.04 / 0.58 |
| 4× | 2,775 | 1.07 M | 0.43 / 1.00 / 0.55 | 0.38 / 0.97 / 0.54 | 0.50 / 1.12 / 0.65 | 0.43 / 1.15 / 0.64 |

Every point is far inside the environment's ceiling (GPU p99 ≤ 1.5 ms, main and render ≤ 1.7 ms).
Doubling the decoration from 1× to 2× costs 0.02 to 0.03 ms of GPU p99; 1× to 4× costs 0.03 to
0.07 ms. (The 0.5× point at 55 m with MSAA ran beside a 92 % CPU burst on one attempt; its main
1.43 is that median.)

At rung E2 (lightmaps, the static set out of the cascades, the static shadow map), for TD14:

| density | 40 m, MSAA 4× | 40 m, TAA | 55 m, MSAA 4× | 55 m, TAA |
|---|---|---|---|---|
| 1× | 0.39 / 0.85 / 0.52 | 0.35 / 0.85 / 0.52 | 0.44 / 0.96 / 0.59 | 0.36 / 0.93 / 0.55 |
| 2× | 0.40 / 0.89 / 0.53 | 0.35 / 0.89 / 0.51 | 0.45 / 0.92 / 0.56 | 0.37 / 0.91 / 0.55 |

**Draws and shadow casters** (the last counter frame of each run, so the range spans the tour's
shots; `kit casters` is our count of shadow-casting kit renderers whose bounds, swept 40 m toward
the moon, meet the view; Unity's is its own "Shadow Casters Count"):

| rung, density, view | kit in view | kit casters (ours / Unity's) | draw calls | set-pass calls |
|---|---|---|---|---|
| E0, 1×, 40 m | 61–94 | 422–530 / 241–307 | 433–525 | 18–25 |
| E0, 1×, 55 m | 135–177 | 478–642 / 307–385 | 585–716 | 17–19 |
| E0, 2×, 55 m | 148–273 | 610–949 / 399–586 | 703–1,070 | 17–20 |
| E0, 4×, 55 m | 251–324 | 980–1,239 / 684–803 | 1,339–1,384 | 20–24 |
| E2, 1×, 40 m | 89–94 | 0 / 0 | 186–195 | 19–24 |
| E2, 1×, 55 m | 152–164 | 0 / 0 | 330–382 | 16–20 |

The kit uses 8 converted materials (5 Lit atlases, the triplanar rock as plain Lit, the candles'
particle flame, the ground), so set-pass calls stay near 20; at E0 the shadow casters are what
multiply the draws (about 5× the visible pieces). E2's static map takes 1,418 casters at load:
5 ms in the player, 4096², texels 5.9 × 3.5 cm by axis.

**What it doesn't show.** The S0 scene is CPU-bound at about 1,000 fps: the GPU is idle most of
each frame, so its sub-0.5 ms times say the environment is cheap on this GPU, not where the
GPU's own limit is. The counters are one frame per run. E2's verdict (kept or dropped) is TD14's.

## Anti-aliasing: MSAA 4× against TAA

**Cost** (GPU p99, ms, median of three attempts; arena E2 at 1×, 40 m, demo load):

| scene, N | MSAA 4× | TAA | TAA saves |
|---|---|---|---|
| S0 (no crowd), 1× E0 | 0.40 | 0.36 | 0.04 (10 %) |
| S1, 800 | 4.06 | 3.62 | 0.45 (11 %) |
| S1, 1,200 | 5.73 | 5.04 | 0.69 (12 %) |
| S3, 800 | 4.11 | 3.57 | 0.53 (13 %) |
| S3, 1,200 | 5.64 | 5.02 | 0.61 (11 %) |

Main and render thread p99 are the same in both modes (4.2–6.1 and 0.64–0.76 ms).

**Captures** (`aa_captures.py`, fixed-time, the capture tool at the pipeline's sample count): S3,
1,200 at 40 m, song 12.5 and 13.5 s; the song's drop-1 at 50.5 and 51.5 s (combos, bolts, hit
flashes). Crops of the view's centre enlarged 3× in `aa/`; full stills and 2 s videos in
`captures/td9/` (gitignored: `s3-msaa4.mp4`, `s3-taa.mp4`, `song-msaa4.mp4`, `song-taa.mp4`).
With TAA the monsters' outlines (the slimes' silhouettes, swords, the hero's armour) step as
with no anti-aliasing at all, and the bolts' discs are jagged; with MSAA 4× they are smooth. In
this horde nearly every edge moves every frame (animation, knockback, the camera's follow), so
TAA's history is mostly rejected and it shows its jittered single sample.

**Decision: MSAA 4×.** TAA would buy 11–13 % of GPU time at the crowd's load (about 110 to 160
more monsters at 40 m by these runs' slope of 3.8–4.2 µs per monster), but on these captures it resolves the crowd's
edges no better than no anti-aliasing, and the horde is what the demo shows. Both are full
quality, so this is not a cut; TD12 measures TAA only at the envelope point, as planned. The
operator's review of the pairs is in Pending on the operator (TD12 consumes it). TD8's real
spark trails weren't on this branch: the projectiles and hit flashes stand in for them.
