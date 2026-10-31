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
  and the combat effects (white, then orange, then red). Monsters keep their
  packs' classic colours, spawned in same-type clumps, so the horde reads as
  masses of colour. (Each type's signature hue in roster.json is opt-in,
  `--signature-monster-colors`.) Cool cyan and violet are the accents, kept
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

The moonlit forest is the default environment. Six others (epic ps-bop) keep
its area plan and change everything the eye reads as the place: the kit, the
ground, the lighting, the atmosphere and the grade. So the level script, the
stress scenes and the beauty shots work unchanged in each, and look sets
compare shot for shot. The player's `--environment <name>` loads one
([Running on the Mac](mac.md#environments)):

```
name      the place                          kit                                       fighting ground; the horde reads
forest    moonlit forest glade (default)     Fantasy Kingdom, Adventure, Elven Realm,  warm brown, mid; lighter
                                             Dark Fantasy
snow      frozen fjord at blue hour          Vikings' snow variants                    near-white snow; darker
desert    sun-bleached ruins, golden hour    Ancient Egypt, Western Frontier,          pale bleached sand; darker
                                             Ancient Empire
lava      basalt hellscape lit by lava       Dungeon Realms' hell kit, Dark Fantasy    near-black basalt, lava rivers; lighter
neon      city at night                      CyberCity, SciFi City                     near-black wet street, neon pools; lighter
blossom   cherry-blossom temple at dusk      Samurai, Elven Realm's statues and lamps  pale white stone, pink edges; darker
crypt     grey stone necropolis (control)    Dungeon, Knights, Dark Fantasy            grey stone at the forest's values; lighter
```

Each has a look set with its kit, palette table and findings
(`demo3d/evidence/look/env-<name>/README.md`; the forest's is
[final](../../demo3d/evidence/look/final/README.md)).
[environments.md](../../demo3d/evidence/look/environments.md) compares all
seven from one build: the roster reads above the floor everywhere but two
blossom shots, and its value spread, not its hues, is what fails per type.
Several environments can stay resident in one player and switch within a
frame, for the trailer ([Trailer Mode](trailer.md)).

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
  An optional `ground.lava` (the lava environment's) adds a generated
  emission map: rivers of lava winding through the edge bands and the
  forest, never onto fighting ground, with cracks glowing in the crust
  around them and, dimly, across fighting ground (`ArenaBaker.GroundLava`).
  Its glow is baked onto the kit through the probes. An optional
  `ground.smoothness` (0.12 by default) wets the neon environment's street.
- **The kit's own values.** `lighting.kitValue` scales the converted kit
  materials' base colours by vendor material name prefix, and
  `lighting.kitEmission` their emission (the neon city's signs and windows
  need several times the packs' own to bloom). A landmark's or structure's
  piece may set `liftM`, metres above the ground, for the signs whose pivot is
  their centre.
- **The horde is never recoloured.** `lighting.crowd.keyColor` gives the
  horde its own key colour at the moon's luminance, so a coloured moon on the
  set leaves the type hues alone. `lighting.post.grade` sets the grade's
  shadow, midtone and highlight tints (the night warms the midtones).
- **The ring around the horde is its own shadow, so the lighting picks the
  ground's value.** Under a dim ambient (the forest, the crypt) the
  monsters' shadows take a mid-value ground down to about L* 20, and the
  horde reads lighter. Under a bright sky fill (the snow, the desert, the
  blossom) the shadows stay pale, so the ground must be pale and the horde
  reads darker: a mid-value ground under a bright fill meets a mid-value
  horde, as the blossom's first pink stone (L* 55 empty) did at |dL*| 0-3.
  Its fighting ground is pale white stone, and its pink is in the canopies
  and edge bands
  ([env-blossom](../../demo3d/evidence/look/env-blossom/README.md)).
  A black ground (the lava, the neon) reads under any light.
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
([Running on the Mac](mac.md#environments)), and write its look set's README
with its kit, palette and findings. `crypt` is the control, POLYGON Dungeon's
grey stone at the forest's values with no hue
([env-crypt](../../demo3d/evidence/look/env-crypt/README.md)): what fails
there is the roster's, not a ground's.

- **A ruin wall's pieces are in world coordinates.** A landmark's and a
  courtyard's `pieces` are offsets from its anchor, but a ruin wall has only
  `from` and `to`, so its pieces' `at` is a ground position: the
  ruins-gate's gap runs from x 13 to 19, not 11 to 17 from `from`.

### Structure styles

`--structure-style <name>` (epic ps-suy) draws the arena's structures in one
of the horde's treatments ([Monster styles](crowd.md#monster-styles)):
`--structure-style crystal` makes the courtyard's walls, the landmarks and the
trees out of crystal, `fire` sets them burning, `hologram` turns them into
projections. The names are the monster styles' (painted, fire, crystal,
spectrum, hologram, chrome, void) and baseline, plus `match`, which takes
`--monster-style`'s style (mix, a style per type, has none for the set:
baseline). baseline is the default and leaves the arena exactly as it is. An
unknown name stops the player at start-up, listing the known ones. It is a
look only; breaking structures is a separate choreography flag. Performance is
not a goal under it, and it is not benchmarked.

- **What a structure is** (`ArenaRuntime.Structures`): every opaque kit
  renderer under Static/Structures and Static/Landmarks, plus any other whose
  bounds' largest side is at least `ArenaOptions.SmallPropM` (4 m: the trees).
  The ground, the mist, runes and glow pools, small props, merged small props
  and transparent kit (the candles' glow cards) are never styled. The player
  logs how many renderers it styled.
- **A runtime material swap.** `StructureStyle.Apply` runs when the arena is
  ready (`ArenaRuntime.OnReady`) and swaps each structure's materials for its
  style's structure shader, one material per kit material, with nothing
  rebaked. Each renderer gets its piece values in a MaterialPropertyBlock:
  `_SL_Piece` (its bounds' centre at their floor, and a seed), `_HeightM`, and
  `_SL_PieceFx` (hit flash and death, 0 while it stands, for breaks). Disposing
  the style puts the kit back.
- **The styles' own code.** Each style's forward code lives in
  `Runtime/Crowd/Shaders/Styles/<Style>Input.hlsl` and `<Style>Forward.hlsl`,
  which the crowd shader and its structure twin (`Runtime/Arena/Shaders/Structures/`,
  `Demo3D/Structures/<Style>`) both include. The twin defines `SL_STATIC` first,
  and `StyleCommon.hlsl` then swaps the skinned front end for a static mesh's:
  the same `SLVertex` and `SLVaryings` from the renderer's transform, the rest
  position in metres from the piece's base. The crowd's compiled output is
  unchanged. CrowdBaker lists the structure shaders in
  `CrowdLibrary.StructureShaders`, so a new one needs `BakeCrowd`.
- **The kit's lighting.** A structure is lit as the kit is: light probes
  (`SampleSH`), the moon with the static shadow map, and Forward+'s lights.
  Its shadows stay where the bake put them (the static shadow map and the
  shadowmask). Alpha clip, cull and the canopy fade follow the kit material it
  replaces, so styled trees keep their leaves' shapes and still dither out
  near the hero. Opaque styles keep the depth, depth-normals (SSAO) and shadow
  passes; a still structure needs no motion vectors.
- **Facets without a rebake.** Crystal, spectrum and hologram read a style
  mesh's barycentrics on the crowd. The kit's walls are a few large
  triangles, so structures get world-space facets instead (`SL_StaticFacets`):
  crystal's are cellular noise, cut across like a geode, each cell tilted its
  own way; hologram's and spectrum's are a square lattice in the surface's
  main plane (a projected grid, a synth's panel).
- **See-through structures stay dark.** A crystal or hologram structure
  shows what is behind it, and the ground is lighter than the kit's dark
  trees and walls, which cost the horde up to 3 L* against its ring. So a
  crystal structure passes half the light behind it (a thicker, darker gem),
  and a hologram structure draws its light over dim glass that takes 75% of
  what is behind away.
- **Fire's flames are a second renderer.** Each burning structure gets a child
  `Flames` renderer on its own mesh with `Demo3D/Structures/FireFlames` (the
  FireShell approach), additive, casting no shadow, its tongues sized to the
  piece. They clip by the leaves' alpha, and canopies burn lower.
- **Per-style looks for the set.** Structures are larger and dimmer than
  monsters, so each style sets its own colours and scales on the set
  (`StructureStyle.Look`): painted's colour strip is moonlit stone with violet
  shadows and teal runes on the beat wave, fire's cracks are a structure's
  size, crystal's gem a dark sapphire. Painted draws no ink line on
  structures: an inverted hull splits apart on the kit's hard edges.
- **The beat.** `StyleBeat` publishes its globals under either flag, so the
  set pulses with the music as the horde does.
- **Value structure.** Structures have their own `Gain` and `Peak`, below each
  style's horde peak, and the structure shaders also hold every channel under
  the bloom's knee (`SL_STATIC_CHANNEL_MAX`), so a self-lit wall blooms only
  as PostFeel's beat raises the bloom, and never out-glows the horde or the
  effects. `--structure-tune key=value,...` overrides them for a run (the same
  keys as `--style-tune`).
- **One post look.** Exactly one `PostLook` goes on the night volume: the
  monster style's if `--monster-style` sets one, otherwise the structure
  style's (the monster style's of the same name). Crystal structures turn on
  the camera's opaque texture, as crystal monsters do.
- **Readability.** The ID render marks the structures in blue (the kit
  through a rendering layer set for the ID frame only, the structure shaders
  themselves), and `demo3d look` reports the horde against the structure
  pixels near it beside its dL* against the ground. Every style keeps the
  horde's |dL*| >= 17. Stills and numbers:
  [evidence/structures/looks](../../demo3d/evidence/structures/looks/README.md).

### Breaks on screen

`--structure-breaks <name>` (epic ps-suy; the choreography half is
[Structure breaks](choreography.md#structure-breaks)) has the hero's bolt
strike scheduled walls on the grid, and the pieces leave the obstacle map at
that moment. The client breaks them on screen at the same song time
(`StructureShatter`), under any `--structure-style`, baseline included, and
with every other flag. Performance is not a goal under it.

- **Moments.** `BreakMoments` groups the timeline's break events into
  moments, the pieces one bolt takes out, each sized by the wall it opens
  (the square root of its pieces' width times height, from their
  footprints). Everything the moment does is known at load, so the live
  player and fixed-time captures agree.
- **Shards, baked.** The `StructureShards` baker (`Editor/Structures/`) cuts
  every kit mesh any environment's structure areas place into thick chunks,
  into `Assets/Baked/Arena/Resources/StructureShards.asset` (vendor-derived,
  gitignored), which only a player with the flag loads. It is the crystal
  shatter's method on a static mesh: the kit's few large triangles are split
  until no edge is longer than a third of a chunk's radius, seeds spread
  over the surface (about one per 1.2 m², 10 to 48), and each patch closes
  to an apex inside the piece, its inside faces marked (UV1.x). Chunks keep
  the kit's UVs and submeshes, so the piece's own materials draw them. A
  target whose meshes have no shards fails loudly at the first frame.
- **The break.** At the moment, the struck pieces' renderers (the kit's, and
  fire's flames) hide and their shards appear where they stood, in the
  piece's current materials: the kit's under baseline, the structure
  style's otherwise. The break spreads from the bolt's strike as a crack
  front (70 m/s): the strike's own shards fly on the impact frame, nothing
  builds up before it. Each shard is a rigidbody in the shatter's own
  physics scene, stepped in fixed 1/120 s steps on the song clock (apart
  from the crystal shatter's), kicked outward from the strike, along the
  bolt and up, the nearest hardest, one in five slumping and one in eight
  flying far, under 2.2 g. After 2.4 s they sink into the ground over 1 s.
  Debris never blocks: the horde walks the opening from the moment it opens.
- **Per style.** A shard drawn with a structure shader carries the piece's
  values, with `_SL_PieceFx.z` 1 and its rest pose (`_SL_Rest`, its
  object-to-world matrix at the break), so its patterns stay as they were in
  the wall while it flies; its inside faces read `SL_INSIDE`. Every styled
  shard flashes white as the front passes it, and glows (`_SL_PieceFx.w`,
  fading over 0.35 to 1.6 s by style): the glow eases the set's grade toward
  the horde's, without its channel limit, so a wall blooms at the moment it
  breaks and settles back under the horde.
  - Crystal's front is slower (22 m/s) and its white flash faint: each shard
    steps 8 cm out of the wall as the front passes, baring its inside faces,
    which glow with the gem's light, so the seams open and light across the
    wall from the strike, and 0.07 s later it flies.
  - Fire's wall chars and collapses: its shards slump more than they fly,
    are molten inside, and burn out from the piece's base (its death, from
    0.35 s over 1.8 s), with embers rising from the rubble.
  - Hologram's shards glitch and scan out from the top (the death, over
    0.9 s) as they drift down under half a g.
  - The other styles and baseline share the flash and the flight.
- **Feel.** Each moment flashes the post (PostFeel, as a strong accent:
  0.5 to 1.1 by size), punches the camera 3.5% and shakes its beat by 0.12
  to 0.4 m (past the beat's own cap). Its bursts follow the style (crystal
  chips tinted by the gem at the strike and each piece, fire's explosion and
  embers, hologram's heavy hit and a ring of lightning, a ground shock and a
  dust cloud for the rest), and its sound is layered from the bank's clips
  (stone: a heavy impact and a crunch; crystal: the shatter's ice break,
  glass spray and tinkle; fire and hologram their own), aggregated per beat
  as the choreography's sounds are: one voice per layer, louder with log2 of
  the pieces.
- **No ghost shadows.** At E2 the static set's shadows are baked twice, and
  both follow a break (`ArenaRuntime.Break`). The static shadow map renders
  again from the pieces still standing. The ground is the only lightmapped
  surface: its shadowmask texels whose ray to the moon passes through a
  broken piece's box take that map's visibility instead
  (`ShadowmaskPatch.shader` on a copy of the baked texture), and the kit's
  and the horde's lighting ignores the shadowmask there
  (`Demo3DUnderBroken` in StaticShadow.hlsl, up to 32 boxes). Without breaks
  nothing changes. A seek backwards puts every piece and shadow back.
- **Showcase:** [evidence/structures/breaks](../../demo3d/evidence/structures/breaks/README.md).

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
  the one custom shader the forest needs. It is rewritten in Shader Graph, which
  makes it compatible with the GPU Resident Drawer. The neon environment adds
  CyberCity's hologram (its trees and planters), ported as our own additive
  Demo3D/KitHologram with its scrolling scan lines; SciFi City's scrolling neon
  signs draw as URP Lit with their emission standing still.

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
