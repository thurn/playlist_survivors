---
name: create-lod
description: Create a high-quality mesh LOD from a source asset and target triangle count, preserving appearance and animation while validating quality at game scale and measuring rendering cost. Use for static or skinned asset reduction, replacement meshes, and LOD strategy comparisons.
---

# Create a high-quality LOD

Produce a reproducible candidate from the original asset, a comparison through the
actual renderer, and an honest assessment of the requested budget. Prioritize the
game's cinematic appearance. A smaller mesh is useful only at screen sizes where
its shape, motion and shading hold up.

Follow the current project AGENTS.md for worktrees, checks and delivery. This skill
does not authorize production LOD/default changes, interactive app launches or
external uploads beyond the user's task. Follow the
[vendor asset rules](../../../docs/tech-demo-3d/vendor-assets.md): keep vendor and
derived meshes, textures, exports, Blender files, generated scenes and builds in
the relevant ignored project directories. Track original tooling, recipes,
summaries and permitted small evidence stills; use the existing derived-record
machinery and asset guard.

## Establish the comparison

- Resolve the source asset and target triangles. Determine whether the number is
  a hard runtime cap or an approximate goal; use the user's wording and constraints.
  If unspecified, treat it as an approximate goal and report the actual count.
- Inspect the current consuming renderer and baker before exporting. Find the
  actual active combined LOD0, coordinate/root space, rig/bind conventions,
  materials, texture atlas and animation table. Do not mistake all prefab
  renderers, inactive variants or a posed snapshot for the source.
- Record source identity/hash, triangles, referenced vertices, submeshes/materials,
  skin influences/palette entries, clips, bounds and region provenance. Inspect
  connected surfaces and open boundaries separately from coincident UV/normal
  seam splits. Position-only connectivity is diagnostic, not permission to weld.
- Establish intended camera/projection, resolution, projected character size and
  near/far usage from the consuming game. Use a known good source render as the
  reference. If screen-size requirements are missing, test several representative
  sizes and report them rather than inventing a production switching threshold.

## Allocate geometry by visual importance

Prefer lighter attribute-aware source reduction for nearer LODs. For aggressive
budgets, consider a **part-aware hybrid**: retain useful authored shells, simplify
parts independently and rebuild selected expensive small details. Start every
independent strategy from LOD0, with separate recipes/outputs.

1. Partition by meaningful surfaces: body region, side, equipment, articulated
   piece, atlas/material boundary and, where needed, individual digit or rivet.
   Identify silhouette landmarks, facial openings, negative spaces, joint bends,
   contact points and distinctive colour features.
2. Write a per-part triangle allocation summing to the target. Spend according to
   projected importance and motion, not in proportion to source triangle counts.
   Retain an already cheap shield outline or armour shell when rebuilding it
   would cost more or damage its identity. Reserve enough geometry for joints
   and required small components before distributing the remaining budget.
3. Use attribute-aware simplification with selective seam/deformation protection.
   Avoid globally locking every border: many are shading/UV splits. Conversely,
   permissive whole-mesh reduction can silently erase fingers, ribs, vertebrae,
   armour or thin surfaces. Audit required components after every reduction;
   protect them individually or allocate/reconstruct them separately. One retained
   triangle prevents disappearance but does not establish useful shape or motion.
4. Reconstruct selectively where reduction is inefficient: tapered low-sided
   limbs, tiny fasteners or simpler independent pieces can help. Fit them to the
   original anatomy/rig. Broad box armour and whole-body voxel fusion can destroy
   the style and internal openings. Watertightness and all-quad topology are not
   goals for these rendering LODs.
5. Sweep a small recorded set of budgets/errors and inspect the worst defects.
   Reallocate triangles from details to damaged important shapes before increasing
   the global budget. Remove a component only deliberately, documenting its
   screen-size justification; never conceal failure by dropping body parts.

If the target still fails quality, keep the best target-budget attempt and explain
the failure. Also keep the nearest useful higher-budget candidate when practical.
A hard cap remains a cap: do not label an over-budget candidate compliant. A
successful result for one asset does not establish a roster-wide recipe.

## Preserve appearance, binding and streams

- Keep source root space, scale, material mapping and the actual rig binding.
  Palette identity can include both a transform and its bind matrix; equal bone
  names do not imply interchangeable entries. Reuse original animation sampling
  for a geometry comparison.
- Never interpolate bone/palette indices numerically. When transferring or
  blending skinning, accumulate weights by discrete palette identity, retain the
  supported influence count, clamp and normalize. Use rigid binding only where
  original deformation supports it.
- Restrict nearest-surface appearance/skinning transfer to the correct region,
  side and articulated piece. Adjacent fingers, opposite limbs and equipment must
  not supply each other's weights. Preserve atlas islands and source face-corner
  UVs; interpolation across a palette/atlas boundary invents unintended colours.
  Check transfer distances and inspect stretches, not just weight validity.
- Compact unused vertices and remap **every** stream and index. Share vertices
  only when their full render/deformation attributes are compatible; position-only
  welding can bridge anatomy or destroy UV, normal, tangent and skin seams.
  Regenerate normals/tangents as needed without erasing intentional hard edges.
- Validate index ranges, finite attributes, discrete in-range palette entries,
  nonnegative normalized weights, submesh/material consistency and actual output
  counts. Recompute conservative animated bounds, covering clips, interpolation
  and crossfades according to the renderer's deformation model. A static rest-pose
  bound is insufficient for a rebuilt skinned mesh.

For this project's bone-texture conventions and reusable experiment code, read
[the skeleton reference](references/skeleton-experiment.md) when working on a
Unity crowd asset. Recheck the current consumer; do not assume the old demo and
the game still share identical code.

## Judge quality at game scale

Render source and candidates through the real material, skinning, culling and
shadow path. Match projection, camera, lighting, scale, yaw and animation state;
do not independently auto-fit each mesh. Disable unrelated automatic LOD,
animation-rate and shadow-proxy cuts while isolating geometry. Each candidate
casts its own shadow.

Inspect native pixels at intended screen sizes first, then enlarge head, joints,
armour, hands/feet and equipment to diagnose defects. Exercise every actual clip
at multiple phases, including endpoints, extremes, death/disassembly and recovery
when present, plus representative crossfades at 0, 0.5 and 1. Look for detached
pieces, collapsed bends, erased anatomy, wrong atlas colours, popping/culling and
shadow changes. A valid mesh and one attractive idle pose do not establish quality.

Registered silhouette masks and foreground colour error can help locate/rank
defects across many poses. Reject empty or clipped masks/crops. Compare to original
renders, not a second implementation of the candidate. Keep shadows separately
visible/scored; foreground colour error alone does not evaluate them. These
metrics support inspection, not a universal perceptual pass threshold. Label
human aesthetic judgment pending unless the user has supplied it.

When the task includes an interactive preview, provide synchronized side-by-side
and registered switching, game-size presets, close zoom and region focus. Clear
the presented screen every frame: cameras rendering only to textures do not clear
the backbuffer behind changing UI text. Reuse existing preview controls when
possible. Keep automated rendering offscreen with a graphics device; build for
the selected host explicitly and restore temporary project settings.

## Compare performance fairly when needed

Triangle equality is not cost equality. Record referenced vertices, material and
submission counts, skinning/palette work, shadow work and actual visibility too.
For strategy comparisons or a performance-equivalence claim, measure source and
candidates at the same crowd sizes, camera, resolution, animation/crossfades,
shader, sampling rate and shadow settings. Confirm equal visible/shadow instance
counts; equalize conservative culling bounds if their differences would bias the
geometry comparison.

Use the real rendering path in a normal player. Warm up, alternate/counterbalance
variant order, and retain multiple paired blocks and raw samples. Submit timed
renders on separate real player frames; repeatedly issuing renders within one
editor callback can change queued work and invalidate the comparison. Check GPU
completion if timing GPU work, propagate readback/process failures and drain
outstanding work before disposing resources. State exactly what the timer includes:
CPU submission, synchronized render latency and GPU-only time are different
measurements. Report variability and how ratios are aggregated. A screenshot,
draw-call count or triangle budget alone cannot prove a speed improvement. Scope
conclusions to the tested hardware/backend/workload.

## Deliver

Keep a recipe with source/output hashes, pinned tool versions/licenses, allocations,
transfer rules and exact generation/import/build commands. Make subprocess errors
fail the job; for Blender background scripts use `--python-exit-code 1` and check
exit status and expected outputs.

Report actual triangles/vertices/materials against the target, screen sizes and
poses inspected, worst remaining defects, relevant checks, and any measured cost
with its scope. Supply the requested usable asset/preview location and a clear
recommendation: candidate, intended LOD range, or why the budget is insufficient.
Leave production integration and switch thresholds within the user's authorized
scope. Follow current repository delivery rules; the historical experiment's
relay-only workflow and host exceptions are not reusable instructions.
