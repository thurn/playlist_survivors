# 3D Tech Demo: Environment

## The Environment

The environment is where the demo spends its extra frame budget. It should
look like a scene from a finished game: a moonlit forest glade, built from
Synty's classic POLYGON packs, with open ground for the fighting and rich
decoration around it. Its rendering gets the same treatment as the crowd: a
stock baseline, a measured ladder of optimizations, cuts gated by A/B checks,
and a density axis that shows what richness costs in monsters.

### Art direction

- **Mood.** An enchanted forest at night. Cool blue moonlight; warm pools of
  light from braziers, candles and pyres; cyan and violet magic from glowing
  trees, crystals and runes; low mist in the hollows.
- **Value structure.** The ground is dark and low in contrast. Monsters, the
  hero and effects carry the brightest values and the most saturated colors,
  so the horde reads at a glance even at a 55 m view.
- **Color separation.** The environment keeps to cool blues, greens and
  violets. Combat effects are warm and white-hot, so they never get lost in
  the set.
- **Composition.** Open clearings 30–60 m across, linked by paths and ringed
  by forest. Each clearing has one landmark the eye can rest on: a glowing
  elven tree, a ruined shrine, a stone circle, the walled courtyard or a
  graveyard.
- **No empty screens.** Every camera position the level script reaches shows
  some edge decoration or a landmark, never only bare ground.

### The kit

The scene uses classic POLYGON packs. Their foliage is almost all solid
geometry, and their materials mostly use the Standard shader, which Unity's
converter turns into URP Lit:

```
pack              what it provides
Fantasy Kingdom   trees (33), bushes and plants (75), rocks (90)
Adventure         trees (48) with solid canopies, on one small atlas
Elven Realm       glowing trees and canopies, ruins, statues, lamps, vines
Dark Fantasy      graveyard, tombs, ruins, candles, pyres, dead trees
```

- **No Nature Biomes packs.** Their cutout leaf cards shade the same pixels
  many times over, and their vegetation shaders are built-in-pipeline only
  (see [Alternatives Considered](alternatives.md#alternatives-considered)).
- **TD2 confirms the mix** with a style still: the roster and the kit
  together under night lighting, from the game camera.

### Layout

The layout is authored as tracked parameters and generated. The choreography
layer turns the parameters into the placement list (see
[Navigation around structures](choreography.md#navigation-around-structures)), and an editor
script builds the scene from that list. So every wall the camera shows is a
wall the monsters walk around.

- **Areas.** The parameters name areas on the 160 × 160 m ground, each with a
  type: clearing, path, edge band, forest, landmark or structure. A clearing,
  landmark or structure may also have a name, which the level script uses as
  a section's goal.
- **Density by type:**
  - **Clearings and paths** are fighting ground. They carry only sparse clutter
    under 0.3 m tall (flowers, moss, scattered stones), and nothing blocks.
  - **Edge bands,** 3–6 m wide, carry bushes, rocks, fallen logs and vines,
    and partly block.
  - **Forest** is dense trees and rocks, and blocks. Canopies never overhang
    fighting ground.
  - **Landmarks and structures** are placed explicitly, not scattered.
  Structures include broken ruin walls and the walled courtyard with 3 or 4
  gaps that the level script and stress scene S6 use.
- **Seeded scatter** fills each area by its type's rules: spacing, scale and
  rotation jitter, and slope limits.
- **A density multiplier** scales edge bands and forest only, for the density
  sweep (see [Environment frontier](#environment-frontier)). Clearings stay
  clear at every density.

```json
{ "seed": 7, "size": [160, 160], "decorationDensity": 1.0,
  "areas": [
    { "type": "clearing", "name": "opening", "center": [0, 0], "radius": 28 },
    { "type": "landmark", "name": "glowing-tree", "at": [6, -4],
      "prefab": "elven/SM_Env_Tree_Glowing_01" },
    { "type": "structure", "name": "courtyard", "kind": "courtyard",
      "center": [-52, 30], "gaps": 4 },
    { "type": "forest", "ring": [68, 80] } ] }
```

### Lighting the night

- **Moonlight.** One directional light, cool blue, at 25° to 35° elevation.
- **Baked static light.** Lightmaps, baked in batch mode, hold the moon's
  light and shadows on the static set, plus the static light of braziers,
  lamps and glowing plants.
- **A static shadow map for monsters.** URP has no cached shadow maps. So at
  load, the client renders the static set's depth once from the moon's
  direction over the whole arena (4096² over 160 m, about 4 cm per texel).
  The crowd and hero shaders sample it, so monsters walk through tree shade,
  and the realtime cascades hold only the monsters and the hero.
- **Emissive glow.** Glowing trees, crystals, runes and flames use emissive
  materials, which bloom picks up for nearly nothing.
- **Few realtime environment lights.** At most 8 flickering flame lights,
  counted in S0 and separate from the effect-light pool.
- **Atmosphere.** Height-tinted distance fog, a few low mist cards in hollows
  (few, since they are transparent overdraw), and fireflies as ambient
  particles on the effect runtime.
- **Ambient for monsters.** Thousands of monsters cannot each sample light
  probes cheaply, so they take ambient from Unity's global ambient probe.
  Per-pixel Adaptive Probe Volume sampling is measured as an option.
- **Night grading.** A color-grading lookup table built for the night palette,
  on top of the post-processing in
  [Engine and Rendering Setup](performance.md#engine-and-rendering-setup).

### Environment rendering

The kit's prefabs, placed as plain GameObjects, cost about what Synty's own
demo scenes do: every prop its own draw call and its own shadow caster. The
environment's rendering is therefore optimized as deliberately as the crowd,
up a measured ladder. Every rung is measured on S0 at the authored density and
at twice it, and against S3 at the envelope, because the environment and the
crowd share the GPU.

```
rung  technique                                          targets
E0    stock: GameObjects, realtime shadows from all      the baseline
E1    GPU Resident Drawer instancing (+ its GPU          CPU draw submission
      occlusion culling, measured on and off)
E2    baked static lighting and the static shadow map;   shadow-pass geometry
      the static set leaves the realtime cascades
E3    decimated LODs for small props (a cut)             vertices and triangles
E4    impostors for the forest's interior (a cut)        the densest geometry
E5    capped textures and shared atlases                 memory and bandwidth
E6    the static set in or out of the depth prepass      prepass vertices vs
                                                         overdraw
```

- **E0 is the baseline.** It is the kit placed as plain GameObjects, with
  realtime shadows from everything: what dropping Synty's demo scene into URP
  would cost. The report compares the final rung against it, as the crowd's
  report compares against stock Unity.
- **Each rung is kept only if it helps.** A rung stays when it lowers S0's GPU
  or CPU time, or raises the envelope's monster count, without failing an A/B
  check. Otherwise it is recorded and dropped.
- **Cuts follow the fidelity rules.** E3's LODs and E4's impostors are cuts,
  gated by the same A/B checks as the crowd's, on the beauty shots (see
  [Making it look good](#making-it-look-good)), in stills and in motion.
- **Vendor tools help.** Amplify Impostors (for E4) and Mesh Combine Studio
  are already in the vendor asset project. Merging clustered small props with
  Mesh Combine Studio changes nothing visible, so it is not a cut; it is
  measured within E1, against instancing alone.
- **Texture caps are applied by our code.** An import override caps
  environment textures, never an edit to vendor files. The cap is the
  smallest size whose beauty-shot A/B passes, likely 1024 px, or 2048 for the
  most shared atlas.
- **Canopy fade.** Tree materials use a Shader Graph variant of URP Lit that
  dithers out canopy pixels within a few metres, on screen, of the hero, so
  the hero is never hidden. The layout keeps canopies off fighting ground; the
  fade covers forest edges.
- **Only what the kit uses is ported.** Elven Realm's triplanar rock shader is
  the one custom shader the kit needs. It is rewritten in Shader Graph, which
  makes it compatible with the GPU Resident Drawer.

### Environment frontier

The crowd's frontier asks how many monsters fit; the environment's asks how
rich the scene can be, and what that richness costs in monsters.

- **The density sweep** runs S0 at decoration densities 0.5×, 1×, 2× and 4×,
  and S3 at the envelope at 1× and 2×.
- **The report gives the curve:** S0's GPU and CPU times against density, and
  the envelope's monster count at each density tested. For example: "doubling
  the decoration costs 0.6 ms of target GPU, or about 350 monsters."
- **The ceiling.** S0, the empty arena with lighting and post-processing,
  must stay at or under 1.5 ms of GPU p99 on Cygnus, about 4.5 ms of the
  target's 12.5 ms. Its main-thread and render-thread times must each stay
  under 1.7 ms (about 2.0 ms of the target's). The authored density is the richest that fits the ceiling.
- **The headline count pays for the scene.** TD12's frontier is measured in
  the finished scene at the authored density, so the monster count already
  includes the environment's cost.

### Making it look good

Agents can't judge beauty by feel, so the scene is built against references
and checked from fixed shots.

- **Reference stills.** Each pack's own art-directed demo scene (Elven
  Realm's demo, Dark Fantasy's graveyard) is opened in batch mode and rendered
  from our camera's angle and height. Those stills set the bar for density
  and dressing.
- **Beauty shots.** Six fixed camera positions, named in the layout
  parameters: the opening clearing, a path between clearings, the
  glowing-tree landmark, the graveyard, the courtyard and a forest edge. Each
  is rendered at 1080p in fixed-time mode, once empty and once with a horde at
  the envelope count.
- **A self-review checklist,** recorded with each set of shots:
  - the horde reads against the ground at a glance;
  - every shot shows a landmark or edge decoration;
  - no canopy hides fighting ground;
  - effect colors stand apart from the set;
  - nothing floats, clips or repeats in an obvious pattern.
- **A readability measure.** For the horde shots, an ID render masks monster
  pixels, and the harness reports the luminance contrast between monster
  pixels and the ground around them. TD15 sets a floor from the first layout
  that passes the checklist, and later changes must not fall below it.
- **The operator's eye.** Each set of beauty shots goes to
  [Pending on the operator](plan.md#pending-on-the-operator) for review. Feedback arrives later as new tasks, and
  no task waits for it.
