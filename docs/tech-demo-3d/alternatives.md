# 3D Tech Demo: Alternatives Considered

## Alternatives Considered

The interviews weighed these and rejected them for the reasons given.

- **Rendering the game's Rust sim.** The numbers would transfer directly, but
  the demo would inherit the survivors run's content, pacing and 300-monster
  cap. The demo instead uses a forward choreographer of its own.
- **A custom Rust and wgpu engine.** It fits the repo's workflow best. But for
  this workload the gain over Unity's bypassable overhead is about 1 to 3 ms
  of CPU, while it costs a renderer, an animation system and a port of every
  effect.
- **Unreal Engine 5.** Niagara and Mass are strong, but it has a heavy baseline
  on laptop GPUs, weaker Mac support, binary assets agents cannot edit, and
  every Unity effect would need remaking.
- **Bevy.** The asset-conversion cost of a custom engine, without the control.
- **Per-vertex animation textures.** The cheapest crowd technique, but they
  cannot blend clips, which the fidelity floor requires.
- **DOTS with Latios Kinemation as a second crowd arm.** An open-source ECS
  animation layer proven at crowd scale. But Latios is confirmed on Unity 6.0
  and not on 6.6, where Entities is tied to the editor version, so the arm
  might have needed its own project on an older editor. It was not clearly
  faster than a bone-texture path, and it cost a bead and a bake-off.
- **Live-sim mode.** A second copy of the choreographer ticking beside the
  client every frame, checked against the timeline at sampled ticks. It
  would measure contention with the frame, but timing the load-time run gives
  the tick cost without a second copy, an 8.0 ms main-thread line or the
  sync logic.
- **Staying on the M5 Max.** The original plan, kept until 2026-10-27. One
  machine, with the repo's own tools. But its tile-based GPU hides the
  target's overdraw and blending costs, its numbers needed correction
  factors across GPU designs, and Direct3D 12 went untested. Cygnus has the
  target's GPU architecture and API.
- **Mac orchestration.** The plan from 2026-10-27 to 2026-10-28: beads,
  Hive and Tollgate stayed on the Mac, and a Mac executor briefed a worker
  session on Cygnus per bead and landed its branch through the relay. It
  worked for one handoff, but every task paid for a second session, remote
  status polling and a Tollgate candidate, for a project that touches no
  other code or CI. The standalone run defers all of that to one
  integration.
- **Executors on Cygnus reading the Mac's beads.** That needs Dolt exposed
  beyond localhost, `bd` and Hive on Windows, and a Mac session to land each
  branch anyway.
- **Porting Tollgate and Hive to Windows.** Tollgate is a macOS app. Porting
  it is a project of its own, and two Tollgates would contend for remote
  master.
- **Public branches as the transport.** The run would push to the public
  repository's branches. A mistake would publish vendor files before any of
  the Mac's guards ran. The private relay keeps everything unpublished until
  integration.
- **A trace at every frontier point.** Bandwidth-corrects every table cell,
  but needs a parsed GPU capture per point. Only the envelope and its
  heavy-load point are traced; overdraw counters flag the rest.
- **VFX Graph as the authoring tool for every effect.** It has GPU effects
  with editor features for free, but there is no supported API to build
  graphs in code, and its asset files are fragile to edit as text. Agents
  could not author twenty effects that way. VFX Graph as a runtime, driven by
  a few templates and converted data, remains a candidate in TD8's spike.
- **Stock effect prefabs, pooled.** Simple, but per-system overhead caps the
  horde long before the GPU does.
- **A path per monster** (Unity's NavMesh agents, or A* each). Every agent
  costs a GameObject or its own search, and its cost grows with path length
  and congestion. A flow field costs the same for 100 monsters or 5,000.
- **Velocity-based local avoidance** (RVO or ORCA). Smoother passing in
  sparse crowds, but heavier per monster, and a horde mostly converges on one
  target, where separation plus marching in place reads well.
- **Synty's Nature Biomes packs for the forest.** The lushest look in the
  library, but their foliage is cutout leaf cards, which shade the same
  pixels many times over and redraw into the shadow maps. Their vegetation
  shaders are built-in-pipeline only, and their demo scenes are about 19,000
  unbatched prefab instances. The classic POLYGON packs give a forest of
  solid geometry at a fraction of the cost.
- **Dropping a pack's demo scene in as the map.** Fast, but those scenes are
  dressed for eye-level vistas, and their layout wouldn't fit the clearings,
  navigation and placement list. They serve as reference stills instead.
- **Buying a target laptop.** It would make the answer measured instead of
  inferred, but was declined. The report labels inferred numbers.
- **Offscreen-only benchmarks.** They never disturb the operator, but they
  never measure present and compositor cost.
- **Upscaling by default.** About 30% to 40% more GPU headroom, but it smears
  small, fast sparks, which are much of the demo's juice.
