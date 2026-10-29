# TD5: the bone-texture crowd

Measured on Cygnus (i7-12700F, RTX 4070 Ti, Windows 11, DDR4-2133) on 2026-10-28, with the
IL2CPP player at exactly 1920×1080 windowed on Direct3D 12, 6 job workers, MSAA 4×, URP Forward+
with forced depth priming and soft two-cascade 2048 px sun shadows (the project's defaults).

- `rows.jsonl`: every result row of the plan below (one per run; searches add a summary row).
- `ablations.jsonl`: the GPU split at 2,000 (main draw off, shadow draw off, skinning off).
- `bench.py`: the interim runner that made them (TD7's sweep runner had not landed in demo3d).
- `table.py`: turns the rows into the tables here.
- `crowd-*.jpg`: tagged stills from the bench player.

## How it was measured

TD7's harness was not in demo3d yet, so TD5 measured with its own windowed player mode
(`--crowd-bench`, `Runtime/Crowd/Bench/CrowdBench.cs`) and `bench.py`:

- **One process per run.** S1-like (walking) or S2-like (attacks, hits with flash and knockback,
  deaths with the 1-beat hold and dissolve, refills) at N placed on screen plus 10% just off
  screen, over flat ground with the default sun (50°), the game camera at 40 m.
- **Timing.** FrameTimingManager's GPU, main-thread and render-thread times for 20 s after 5 s of
  warm-up, zero GPU readings excluded (their count is in each row). The pass rule is
  performance.md's: p99 GPU ≤ 4.2 ms, p99 main and render ≤ 10.4 ms, no frame over twice its line.
- **Searches.** In one process: three 8 s probes fit a line per resource, the lowest crossing is
  the candidate, then full 20 s runs (3 s re-warm) confirm it passes and that 5% more fails,
  stepping or bisecting where they disagree (benchmarks.md, The sweep). The packing capacity
  ceiling is not applied, so overlapping monsters are allowed; each arm's maximum is its own.
- **Quiet machine.** `bench.py` holds TD7's machine lock (`bench.lock`), waits for no other player,
  an idle GPU and under 10% other CPU (a minute at most), samples other processes' CPU every
  second during the run, and repeats a noisy run once (`noisy`, `otherCpuPct` in each row).
- **Every row is marked noisy.** Two other agent sessions worked throughout (their editors and a
  TD4 batch player): other processes averaged 14% of the CPU (at most 22%) against the 10% line,
  and a run often began right after TD7's sweep released the lock. In the breakdown, a run and its
  repeat differ by up to 0.10 ms of GPU p50 (median 0.02 ms) and the two repetitions by up to 0.18
  ms (the stock arm; 0.16 ms per-vertex), and 16 of its 24 rows had another player process running
  (`otherPlayers`: TD4's batch captures, which use the GPU). The searches and ablations had none.
  The palette's lead (0.4 to 0.6 ms at 2,000, 13% at the S2 maximum) is larger than that spread,
  so the choice stands; the absolute maxima are worth re-measuring on a quiet machine with TD7's
  harness (TD12's sweep does).

## The comparison (crowd.md, Against the baseline)

**The palette variant survives:** the larger S2 maximum at 40 m, 1,395 placed (1,295 on screen)
against the per-vertex variant's 1,237 (1,142), both limited by the GPU. Stock Unity holds 726
(651 on screen), limited by the main thread. Each maximum passed a full 20 s run, and 5% more
failed one.

| Arm | S2 max at 40 m: placed (on screen) | S1 max at 40 m | Limit | Frame at 2,000, S2: GPU / main / render p50 (p99), ms |
|---|---|---|---|---|
| Palette (kept) | **1,395** (1,295) | 1,232 (1,255) | GPU | 5.21 (5.78) / 0.68 (1.05) / 0.39 (0.61) |
| Per-vertex (deleted) | 1,237 (1,142) | 1,196 (1,214) | GPU | 5.83 (6.42) / 0.85 (1.30) / 0.48 (0.74) |
| Stock (`--arm stock`, now BaselineCrowd) | 726 (651) | 748 (764) | main thread | 3.79 (7.72) / 23.89 (28.85) / 1.82 (2.55) |

"Placed" is the search's N (monsters placed inside the ground footprint; 10% more stand just off
screen); "on screen" is the mean count whose bounds meet the frustum (S2's dead and refilling
monsters are hidden part of the time).

### Frame breakdown at 2,000 placed, 40 m (median of 4 runs: 2 repetitions, each run twice)

| Scene | Arm | Runs | GPU p50 / p99 (ms) | Main p50 / p99 (ms) | Render p50 / p99 (ms) | On screen (alive + dying) | Shadow casters | Draw calls | Pass |
|---|---|---|---|---|---|---|---|---|---|
| S1 | palette | 4 | 5.75 / 6.32 | 0.67 / 1.03 | 0.40 / 0.60 | 2034 | 2070 | 20 | 0/4 |
| S1 | stock | 4 | 5.02 / 5.45 | 23.30 / 27.66 | 1.93 / 2.47 | 2034 | 2200 | 2200 | 0/4 |
| S1 | vertex | 4 | 6.19 / 6.81 | 0.75 / 1.26 | 0.44 / 0.72 | 2034 | 2070 | 20 | 0/4 |
| S2 | palette | 4 | 5.21 / 5.78 | 0.68 / 1.05 | 0.39 / 0.61 | 1845 | 1912 | 20 | 0/4 |
| S2 | stock | 4 | 3.79 / 7.72 | 23.89 / 28.85 | 1.82 / 2.55 | 1812 | 1986 | 1987 | 0/4 |
| S2 | vertex | 4 | 5.83 / 6.42 | 0.85 / 1.30 | 0.48 / 0.74 | 1845 | 1912 | 20 | 0/4 |

Draw calls: the bone-texture path issues 20 `RenderMeshIndirect` calls a frame (per type: one main
and one shadow call per material; the dragon has two materials), each holding 8 depth-bin
commands for the main draw; stock Unity draws one SkinnedMeshRenderer per visible monster.

### Searches (every run is in `rows.jsonl`)

| Scene | Arm | Max N (passes) | First failing N | Fitted candidate | Limit | On screen at max |
|---|---|---|---|---|---|---|
| S2 | vertex | 1237 | 1299 | 1237 | gpu | 1142 |
| S2 | palette | 1395 | 1465 | 1395 | gpu | 1295 |
| S2 | stock | 726 | 763 | 763 | main | 651 |
| S1 | vertex | 1196 | 1256 | 1196 | gpu | 1214 |
| S1 | palette | 1232 | 1294 | 1294 | gpu | 1255 |
| S1 | stock | 748 | 786 | 786 | main | 764 |

### Where the bone-texture path's GPU time goes (S2, 2,000, `ablations.jsonl`)

| Arm | All | Main draw off (shadows only) | Shadow draw off (main only) | Skinning off (rest pose) |
|---|---|---|---|---|
| Palette | 5.27 | 2.86 | 2.52 | 4.82 |
| Per-vertex | 5.82 | 3.16 | 2.77 | 4.78 |

GPU p50 in ms, two runs each (within 0.05 ms of each other here). In these rows the
"skinning off" ablation still ran the palette blend pass, so its 0.45 ms covers the draws'
palette reads, not the blend. The review's re-run with the pass skipped too (`skin-bone-*` rows
at the end of `ablations.jsonl`, the build after the review fixes): 5.22 ms with skinning, 4.80
ms without, so palette skinning in all costs 0.42 ms at 2,000 and the blend pass itself is
within noise.

- **Geometry, not skinning, is the cost.** The rest pose still costs 4.8 ms: skinning adds 0.45 ms
  (palette) or 1.0 ms (per-vertex). The roster averages about 5,700 triangles and 11,000 vertices
  a monster (the low-poly packs are flat-shaded, so vertices are hardly shared), drawn in four
  passes: the depth prepass (forced depth priming), the forward pass and two shadow cascades. At
  2,000 that is about 45 million triangles a frame.
- **Shadows cost as much as the main draw.** One draw holds every caster and URP repeats it for
  both cascades; the shadow pass skips skinning for monsters outside each cascade's box, which
  measured within noise. With the camera 42 m from the ground, the default 25% cascade split puts
  little of the view in cascade 0 (TD9 owns the split).
- **So the frontier moves with triangles.** LOD1 and LOD2 and a shadow proxy (TD11's cuts) are what
  move it; the mechanism (projected-height selection with hysteresis, the dithered crossfade, a
  separate shadow LOD) is built and runs with LOD0 only.
- **Stock Unity is cheaper on the GPU at 2,000** (5.0 ms in S1), since it skins each vertex once a
  frame in compute and draws plain vertices four times, but its main thread (23 ms: 2,000 Animators
  and renderers) fails the line threefold.

### The shadow extension

With a 25° sun (TD9's moon is 25° to 35°), S1 at 2,000 culls 2,061 shadow casters with the
sun-ward extension and 2,036 without it: 25 monsters just off screen whose shadows reach into
view, at no measurable cost (5.70 against 5.68 ms GPU p50). At the bench's 50° sun the sweep is
shorter than most monsters' bounding radius, so the extension adds about 1%.

## The bake (`demo3d unity BakeCrowd`, 25 s warm)

| Type | Vertices | Triangles | Bones (entries) | Frames (60 Hz) | Bone texture | Materials |
|---|---|---|---|---|---|---|
| skeleton | 19,217 | 9,310 | 120 | 656 | 360×656, 3.6 MB | 1 |
| goblin | 19,743 | 12,457 | 83 | 826 | 249×826, 3.1 MB | 1 (colour-ID baked) |
| slime | 2,648 | 1,428 | 8 | 1,221 | 24×1221, 0.4 MB | 1 (drawn opaque) |
| bat | 5,570 | 3,092 | 74 | 702 | 222×702, 2.4 MB | 1 (colour-ID baked) |
| mushroom | 3,208 | 1,638 | 15 | 736 | 45×736, 0.5 MB | 1 (colour-ID baked) |
| demon | 13,083 | 5,992 | 59 | 494 | 177×494, 1.3 MB | 1 |
| bigork | 13,855 | 6,812 | 50 | 514 | 150×514, 1.2 MB | 1 |
| troll | 11,095 | 5,220 | 50 | 522 | 150×522, 1.2 MB | 1 |
| dragon | 11,755 | 5,996 | 108 | 676 | 324×676, 3.3 MB | **2** (body, saddle) |

17 MB of bone textures for the roster (crowd.md estimated 80 MB). Entries are the distinct (bone,
bind pose) pairs of every active skin plus each rigid attachment (the elites' weapons ride their
hand bone as one entry). Only active renderers are combined: TD2's triangle counts include every
inactive variant mesh in the prefabs (56,296 for the Big Ork against the 6,812 drawn).

## Stills (tagged JPEGs)

- `crowd-bone-s2-30m.jpg` (0.7 monsters per m²), `crowd-bone-s2-40m.jpg`,
  `crowd-bone-s2-55m.jpg` (1.6 per m²): S2 at fixed time 9.3 s, 1280×720 from the 1080p frame.
  White monsters are mid hit flash; the glowing specks are dissolve edges.
- `crowd-bone-s2-30m-crop.jpg`: a 100% crop of the 30 m frame.
- `crowd-bone-s2-30m-motion-crop.jpg`: URP's motion vector texture in motion (`--show-motion
  --live --fixed-time`), mid-grey for none: each limb of each monster has its own vector, so the
  MotionVectors pass runs with skinned motion from the previous frame's palette.
- `crowd-bone-s2-30m-normals-crop.jpg`: the camera normals texture (`--show-normals`), written by
  the DepthNormals pass SSAO will use.
- `crowd-bone-s2-30m-lights-crop.jpg`: 48 unshadowed point lights through Forward+.
