# 3D Tech Demo: Environment

## The Environment

The environment is where the demo spends its extra frame budget. It should
look like a scene from a finished game: a moonlit forest glade, built from
Synty's classic POLYGON packs, with open ground for the fighting and rich
decoration around it. Its rendering gets the same treatment as the crowd: a
stock baseline, a measured ladder of optimizations, cuts gated by A/B checks,
and a density axis that shows what richness costs in monsters.

### Art direction

- **Mood.** An enchanted forest at night, played as a lit stage (the look
  pass, epic ps-e8r, with Deep Rock Galactic: Survivor as the colour
  reference). A warm key light and a warm pool around the hero light a warm,
  mid-value, textured fighting ground; darkness stays at the frame's edges
  and in the forest. Braziers, candles and pyres add warm pools; low mist
  sits in the hollows.
- **Value structure.** Effects are the brightest values on screen, with
  white-hot cores. Then the key-lit horde and the lit stage around the hero
  (mid values); then the dark edge bands and forest. The horde must read
  against the ground by at least 17 L* either way, darker or lighter (see
  [Making it look good](#making-it-look-good)).
- **Color plan.** Warm dominates: the ground, the key light, the hero's pool
  and the combat effects (white, then orange, then red). Every monster type
  has one signature hue (roster.json), spawned in same-type clumps, so the
  horde reads as masses of colour. Cool cyan and violet are the accents, kept
  small: glowing trees, runes, magic, wisps and fireflies. The palette table,
  role by role, is in
  [evidence/look/final](../../demo3d/evidence/look/final/README.md).
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

- **The key light.** One directional light, warm-neutral, at 42° elevation
  (the layout's `lighting.moon`). The crowd shader boosts it on the horde
  alone (`crowd.key`) over a weak ambient fill, so monsters show form, with a
  faint cool rim.
- **The hero's light.** One unshadowed realtime point light follows the
  hero: the lit stage. A cool-tinted vignette darkens the frame's edges
  around it.
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
- **Night grading.** A color-grading lookup table built in code for the lit
  stage (shadows held near black, warm midtones, saturated colours keeping
  their hue), on top of the post-processing in
  [Engine and Rendering Setup](performance.md#engine-and-rendering-setup).
  The post stack follows the music: a pulse on every beat, flashes on
  accents, and a grade per section (cooler breakdowns, hotter drops).
- **Palette swaps.** The player's `--palette <name>` recolours the runtime's
  share of this (moon, ambient, fog, vignette, the hero's light, a ground
  tint, the mist) from arena-layout.json's `palettes`, to try the type
  colours against other backgrounds
  ([Running on the Mac](mac.md#palettes)). A recolour is not an environment (the kit keeps
  its night albedo), so [Environments](#environments) replace them for that test.

### Environments

The moonlit forest is the default environment. Others (epic ps-bop) keep its
area plan and change everything the eye reads as the place: the kit, the
ground, the lighting, the atmosphere and the grade. So the level script, the
stress scenes and the beauty shots work unchanged in each, and look sets
compare shot for shot.

- **One area plan.** An environment is
  `Choreography/Data/environment-<name>.json`, which `extends` the arena
  (arena-layout.json). The plan's keys come from the arena only: `name`,
  `size`, `decorationDensity`, `edgeWidthM`, `heroStart`, `areas` (with their
  goals) and `shots`. An environment that sets one fails to load.
- **Overrides merge.** Every other key merges over the arena's
  (`LayoutEnvironment.Resolve`, in the choreography package): objects key by
  key at any depth, arrays and values replace, and `null` removes a key. So a
  partial `lighting` keeps the arena's moon direction, the horde's lighting
  and the post feel. A prop group under `props` replaces whole.
  `pieces {area name: [pieces]}` replaces a landmark's or structure's own
  pieces at the same anchor.
- **The ground's colours** are the layout's `ground` (forest, edge,
  clearing, path, in sRGB); the baker generates the albedo from them.
- **The horde is never recoloured.** `lighting.crowd.keyColor` gives the
  horde its own key colour at the moon's luminance, so a coloured moon on the
  set leaves the type hues alone. `lighting.post.grade` sets the grade's
  shadow, midtone and highlight tints (the night warms the midtones).
- **Its own placement list.** The player and the stress scenes resolve the
  environment's layout before building the timeline, so monsters walk round
  its own blocking pieces.
- **Its own bake.** `BakeArena` (and `BakeAll`) bakes every environment:
  the forest's full ladder in `Assets/Baked/Arena/`, and each other
  environment at the authored density only (E0 and E2, not merged) in
  `Assets/Baked/Arena/<name>/`, as `Arena-<name>-E2-2x`. Each has its own
  converted materials, since `kitValue` may differ.
  `BakeArena -environment crypt` bakes just one.
- **Loading one.** The player's `--environment <name>` (or a sweep's
  `environment` sceneOptions key) loads its scene and lighting. An unknown
  name fails and lists the known ones. A known one that isn't baked fails too,
  where the forest would fall back to flat ground.

To add an environment, write `environment-<name>.json` from kit pieces that
are already synced and measured (footprints.json), then bake and render it
([Running on the Mac](mac.md#environments)). `crypt` is the minimal example:
Dark Fantasy's dead trees, ruins, tombs and statues and Fantasy Kingdom's
rocks on a grey stone ground, under a neutral moon and grade.

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
  pixels, and the harness reports dL*: the median L* of the monster pixels
  less that of the ground in a ring around them. The floor is absolute,
  |dL*| >= 17 (TD15's lowest shot) in every beauty shot, since a warm,
  mid-value ground can put a darker horde on a lighter ground; no change may
  fall below it. `demo3d look` renders the shots and measures it
  ([Running on the Mac](mac.md#the-look-set)).
- **The operator's eye.** Each set of beauty shots goes to
  [Pending on the operator](plan.md#pending-on-the-operator) for review. Feedback arrives later as new tasks, and
  no task waits for it.
