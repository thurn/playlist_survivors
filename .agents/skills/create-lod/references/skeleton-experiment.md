# Skeleton experiment: evidence and reusable entry points

Read this when adapting the skill to the Unity crowd renderer. Paths below are
repository-relative entry points, not a guarantee that today's baker is unchanged.

## Lessons grounded in the experiment

- LOD0 had 9,310 triangles / 19,217 vertices. Whole-source reduction A could not
  reach the approximate 1,200 target while retaining required anatomy. The useful
  retained A had 2,143 triangles / 4,008 vertices. Early lower-count successes
  erased small connected surfaces or damaged atlas mapping.
- The original B had 1,264 triangles / 3,792 vertices and crude box armour. Comparing
  its appearance with A did not isolate strategy: A had much more geometry.
- B matched was generated independently from LOD0 with 2,003 triangles / 4,211
  vertices. It simplified authored shells per region and selectively reconstructed
  bones/details. The main shield and boss already cost only 76 triangles, so
  retaining those and rebuilding six tiny rivets was better than reducing the
  important shield outline to fund the rivets. Shoulder shells also needed their
  own contour budget. Allocation, rather than a different simplifier, was decisive.
- Both A and matched B used meshoptimizer. Matched B's median paired synchronized
  rendering cost was 1.013 × A on Mac Metal over 36 paired blocks, with noisy
  individual ratios. At the sampled game sizes, silhouette overlap was about 92%
  versus 88% and foreground RGB error about 20% lower. This supported the hybrid
  for this skeleton's aggressive LOD, not a universal speed tie, pixel threshold,
  artistic acceptance or a Windows GPU-budget claim.

Read the [final comparison](../../../../demo3d/evidence/skeleton-lod/cost-match.md)
for protocols, raw-output locations, exact commands, caveats and small stills.
The [initial experiment](../../../../demo3d/evidence/skeleton-lod/README.md)
documents failed attempts and the earlier preview/zoom fix. Use the final
cost-matched assessment for strategy conclusions; the earlier low B is a
different candidate.

## Inspect the current consuming code

The demo routes are under `demo3d/Assets/Demo3D/`; the game has its own routes under
`game/Assets/Game/`. Start with the consumer the user actually named:

| Purpose | Demo | Game |
|---|---|---|
| Combining/export/bake | `Editor/Crowd/CrowdBaker.cs` | `Editor/Crowd/CrowdBaker.cs` |
| Type/clip/binding data | `Runtime/Crowd/CrowdLibrary.cs` | `Client/Crowd/CrowdLibrary.cs` |
| Runtime palette/indirect rendering | `Runtime/Crowd/BoneTextureCrowd.cs` | `Client/Crowd/BoneTextureCrowd.cs` |

In the experiment's TD5 combined streams, **UV1 contains palette indices and UV2
contains weights**, with four influences. Palette entries identify transform plus
bind matrix, not just a joint name. UV0 supplies the atlas/material appearance.
The 120-entry palette and 60 Hz bone textures were reused unchanged by A and B.
Read the actual mesh/shader/baker contract before adapting those conventions to
another asset or project. Static meshes do not need animation-specific machinery.

## Reuse mechanics, adapt recipes

Under `demo3d/tools/recipes/`:

- `skeleton_lod.py`: explicit export, generation, scene, capture, details, build,
  test, cost and quality routes; wrapper restores temporary Unity settings.
- `skeleton_lod_a.py`: pinned meshoptimizer attribute-aware/permissive reduction,
  selective discontinuity protection and required-surface coverage.
- `skeleton_lod_b.py`: source-region-limited transfer and the original proxy.
- `skeleton_lod_b_match.py`: per-component shell budgets, selective reconstruction
  and full-stream compaction for the useful hybrid.
- `skeleton_lod_cost_report.py`: validates raw rows/counts and produces aggregate
  data and tagged small stills.
- `setup_skeleton_lod.sh`: historical pinned local Blender/meshoptimizer setup.
  Inspect before executing; reuse a compatible installed tool where appropriate.

Under `demo3d/Assets/Demo3D/`:

- `Editor/Crowd/SkeletonLodExperiment.cs`: source export, candidate import and
  animated-bound validation.
- `Editor/Crowd/SkeletonLodPreviewBuilder.cs` and
  `Runtime/Crowd/SkeletonLodPreview.cs`: isolated library/scene and synchronized
  interactive/offscreen preview through the crowd renderer.
- `Runtime/Crowd/SkeletonLodCostBenchmark.cs`: actual-frame controlled timing with
  equal visibility and own-mesh shadows; reported time includes synchronization.
- `Editor/Crowd/SkeletonLodQualityAssessment.cs`: aligned lit/mask pose comparisons.

These tools are skeleton-specific, including region names, allocations, fixed
paths, source settings and benchmark scenarios. Do not present `b-match` or its
budget-scale setting as a generic source-and-triangle-count API. Adapt the recipe
and importer deliberately for the requested asset; recompute actual output counts
and rerun coverage, binding and render checks. Do not carry over the skeleton's
budgets, camera distances, number of clips or evidence ratios as acceptance rules.

The experiment used meshoptimizer v0.25 at revision
`6daea4695c48338363b08022d2fb15deaef6ac09` (MIT) and Blender 4.5.3 LTS
(GPL-3.0-or-later). Record the actual tools used by a new recipe. The historical
Mac Mono/Metal preview tested local appearance and controlled rendering cost;
platform/backend conclusions require measurements on the relevant target.
