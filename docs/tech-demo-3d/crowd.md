# 3D Tech Demo: Crowd and Hero

## The Crowd

The crowd is the core of the measurement. Thousands of skinned monsters must
meet the fidelity floor, and stock Unity, an Animator plus a
SkinnedMeshRenderer per monster, tops out in the hundreds, mostly on CPU.
Our own GPU-driven bone-texture path draws the crowd, and stock Unity is
measured as the baseline.

### Roster

The horde is drawn from palette-textured low-poly packs that share one look:

- **Fodder:** six Magic Pig Games low-poly monsters (Skeleton Warrior,
  Goblin, Slime, Bat, Mushroom Monster, Demon). Each ships its own rig and 12
  to 20 clips, covering walk, attacks, hit and death.
- **Elites:** two Synty POLYGON Fantasy Rivals characters (Big Ork, Troll).
  They use Synty's humanoid rig, animated with Synty's locomotion and combat
  animation packs.
- **Boss:** one large creature (the Magic Pig Dragon or the Fantasy Rivals Evil
  God), chosen by TD2 from a style still: a render of the candidates side by
  side under the demo's lighting.

That is nine mesh types and eight rigs. TD2 may swap types for style or clip
coverage.

### Common requirements

The bone-texture path must meet these requirements. The stock baseline meets
the same ones wherever stock Unity can, so the comparison is of
implementations, not features.

- **Per-entity state comes from the timeline.** A Burst job evaluates each
  monster's segment, easing, action state and clip times each frame.
- **Animation.** Two clips per monster crossfade, with frame interpolation.
  The walk phase follows distance, and turns are smooth.
- **Shadows.** Every monster casts into the sun's cascades, including
  monsters just off screen whose shadows reach into view.
- **Draw order.** Roughly front to back, where the path allows it, for the
  target's early depth rejection.
- **Lighting.** Forward+ shading with the sun, ambient and every effect light.
- **Reactions.** Hit flash, knockback, death clip, then a 1-beat hold and a
  1-beat dissolve with an emissive edge. A monster drawn by Crystal instead plays no death clip:
  it shatters into physical shards on the killing blow's frame, from whatever pose it is in
  ([The crystal shatter](#monster-styles)).
- **Variation.** Per-instance tint from the type's own color sets, plus small
  scale jitter.
- **Weapons.** Weapons ride their hand bone rigidly.
- **Motion vectors.** Every pass that TAA needs, including skinned motion
  vectors from the previous frame's pose.

### Bone-texture path

Our own path keeps animation entirely on the GPU and needs no DOTS.

- **Baking.** The bone-texture baker, an editor tool, samples each clip on its
  rig at 60 Hz (30 Hz is a cut). It writes each bone's skinning matrix (the
  bone's world matrix times its bind pose) as three float4 rows into one
  floating-point texture per rig. A small table gives each clip's first row,
  frame count, loop flag and annotations.
- **Size.** About 60 bones × 3 texels × 16 bytes is 2.9 KB per frame. Thirty
  clips of 2 s at 60 Hz come to about 10 MB per rig, or 80 MB for the roster.
- **Instance data.** 64 bytes per monster (the struct below): position and
  yaw, both animation slots' clips and times, the blend weight, scale, hit
  flash, dissolve, tint and flags. Burst jobs write it straight into a
  `GraphicsBuffer` through `LockBufferForWrite`. Slots are stable per entity,
  and the buffer is double-buffered, so the previous frame's copy supplies
  motion vectors.
- **Two skinning variants, both measured:**
  - **Per-vertex sampling:** the vertex shader samples 4 bones × 2 clips × 2
    frames, which is 48 texel loads per vertex, in every pass.
  - **Palette pass:** a compute pass blends each visible monster's bones once
    into a palette buffer (about 14 MB per frame for 5,000 monsters). Every
    pass then reads just 4 matrices per vertex. The previous frame's palette
    is kept for motion vectors.
- **Drawing** uses `Graphics.RenderMeshIndirect` with a custom URP shader that
  has forward, depth, shadow-caster and motion-vector passes (see
  [Drawing the crowd](#drawing-the-crowd)).

```hlsl
struct MonsterInstance {
    float3 position; float yaw;
    uint clipA; float timeA; uint clipB; float timeB;
    float blend; float scale; float hitFlash; float dissolve;
    uint tint; uint flags; float2 pad;
};
```

### Stock baseline

The baseline shows what Unity does out of the box, so the report can say how
far the bespoke paths move the frontier.

- **One GameObject per monster,** with an Animator driven by `CrossFade` calls
  from timeline events.
- **A SkinnedMeshRenderer per monster,** with Unity's compute skinning and
  shadows on.
- **Everything else is shared:** the same scene, lighting and timeline as the
  bone-texture path.

### Against the baseline

TD5 compares the bone-texture path with stock Unity early, before effects and
polish depend on the crowd.

- **Scenes.** The crowd-only and crowd-with-combat stress scenes (no
  effects), at 2,000 on-screen monsters and a 40 m view. Each arm then also
  gets a search for its own maximum.
- **Arms.** Both bone-texture skinning variants and the stock baseline, all
  under the same pass rule.
- **Recorded per arm:** max on-screen count, the frame breakdown at 2,000,
  and implementation notes (lines of code, workarounds, any fidelity-floor
  item it struggled with).
- **The variant.** The skinning variant with the larger S2 maximum at 40 m
  survives. The other's code is deleted; its measurements stay in the
  evidence.
- **The baseline stays** as a harness option, so the final report can
  compare against it.

### Drawing the crowd

The bone-texture path draws the crowd in a fixed way that keeps CPU cost
flat and helps the target's GPU.

- **Culling runs in a compute pass.** Each instance's bounding sphere is
  tested against the camera frustum extended toward the sun by the longest
  shadow a monster can cast (height ÷ tan(sun elevation)). Monsters just off
  screen therefore still cast their shadows into view.
- **Depth bins give rough front-to-back order.** The same pass sorts visible
  instances into 8 bins by camera distance and writes one indirect draw
  command per bin. Commands run near to far within each call, which cuts the
  depth prepass's overdraw. Order across types is not sorted; depth priming
  keeps the forward pass's cost independent of it.
- **LOD is chosen by projected height,** with hysteresis and a 0.2 s dithered
  crossfade. Each LOD threshold is a cut gated by an A/B check.
- **Shadows can use a proxy.** The main draw does not cast shadows; a
  separate shadow-only draw uses the shadow-proxy LOD, so the proxy can
  differ from what the camera sees.
- **Draw count stays flat.** The CPU issues one `RenderMeshIndirect` call per
  type and LOD for the main draw, and one per type for its shadow proxy:
  36 calls a frame for nine types and three LODs. URP runs each call in
  every pass its shader has (depth, motion vectors, forward, each shadow
  cascade). Each call holds its 8 depth-bin commands. That makes a few
  hundred GPU draws in all, whatever the number of monsters.

### Monster styles

`--monster-style <name>` draws the horde in one of the
[style lab's](stylelab.md) treatments, so they can be judged in the real
demo (epic ps-a54). The names are baseline, painted, fire, crystal,
spectrum, hologram, chrome, void and mix. An unknown name stops the player
at start-up. baseline is the default, and it is
the demo exactly as it was: the baked CrowdSkinned materials, the night's
post, and no beat globals. The look harness passes the flag through
`--player-args`. Performance is not a goal under the flag, and it is not
benchmarked.

Every style uses the pattern the painted treatment set (ps-a54.1):

- **Shaders live in `Runtime/Crowd/Shaders/Styles/`.** Each one is a copy of
  the lab's shader. Its fragment code is ported almost as it is.
  `StyleCommon.hlsl` is the lab's `StyleLabCommon.hlsl` on the crowd's
  skinning:
  - `SLAttributes` is the crowd's welded mesh, with bone data in
    TEXCOORD1/2. A style mesh's extras take TEXCOORD3 and later.
  - `SL_Skin` skins the vertex. It also gives the lab's per-monster values
    from the instance: hit and death (`hitFlash`, `dissolve`), a seed from
    the slot, and the centre (the feet). Read them in the fragment with
    `SL_HIT`, `SL_DEATH` and `SL_SEED`.
  - `SL_FRAGMENT_BEGIN` handles the LOD dither, the death clip and TD15's
    ID render.
  - The main light combines the realtime shadow with the arena's static
    shadow map, as the baseline's does.
  - The lab's beat ring (`_SL_RingGain`) is always 0 here, since demo3d's
    ground has none.
- **Passes are shared.** A style writes its forward pass. It gets
  ShadowCaster, DepthOnly, DepthNormals and MotionVectors by including
  `StylePasses.hlsl` in four short pass blocks (copy Painted's). Those
  passes are CrowdSkinned's, ported onto `SL_Skin`. Each style's CBUFFER and
  `StyleCommon.hlsl` sit in the SubShader's HLSLINCLUDE, so every pass sees
  the same skinning, death clip and LOD dither. A style that moves vertices
  (shatter, flames, melt) defines `SL_DISPLACE` before StyleCommon.
  Shadows, depth and motion vectors then follow the moved mesh, and the
  previous frame's pose is moved the same way. Death is the baseline's
  dissolve (`SL_Dissolve` calls `CrowdDissolve`). CrowdSkinned itself is
  untouched, so the default path stays as fast as it was.
- **Materials are made per style and type.** `MonsterStyle.Materials` makes
  one material per type and submesh, which `BoneTextureCrowd` draws in place
  of the baked ones. It copies the baked albedo and base colour, sets the
  type's `_HeightM`, and sets the type's palette from `StyleLook` (the lab's
  Looks.cs, keyed by roster id). A shader is found by name in the
  library's `StyleShaders`: CrowdBaker adds every shader in `Styles/`, so a
  new style needs `BakeCrowd`. The baker also records each type's albedo
  value range (`CrowdType.ValueRange`) for painted's colour strip.
- **Some styles draw the style mesh.** CrowdBaker also writes each type's three
  LODs unwelded (`CrowdBaker.Unweld`, stylelab's StyleMesh), so every triangle has its own
  vertices. Bone data stays in UV1/UV2 and the position is already the rest pose in metres.
  The face normal is in the tangent, and barycentrics plus a random value per triangle are in
  UV3. They are listed in their own asset, `CrowdStyleMeshes`, not in the library: the library
  loads on every run and would load every mesh it referenced. Only a style whose
  `MonsterStyle.UsesStyleMesh` is true for some type (crystal, spectrum, hologram, and the mix's
  slimes and bats) loads it: its shader defines
  `SL_STYLE_MESH`, which adds those streams to `SLAttributes` and `bary` to the varyings, and
  `SL_SkinFace` turns the face normal with the bones. The welded meshes and the default path are
  unchanged.
  - **Shatter** (crystal, spectrum) is `SL_ShatterDisplace` in `SL_DISPLACE`, so every pass
    moves the shards. A shard that has finished its flight collapses to a point, and such a
    style sets `SL_DISSOLVE` to `0.0` (no clip). Hologram's scan-out is its own `SL_DISSOLVE`.
    In the demo, crystal's deaths break into baked shards instead (below), so only spectrum
    still flies its triangles apart.
- **The crystal shatter** (epic ps-2iq) replaces crystal's triangle shatter wherever Crystal draws
  a type (`--monster-style crystal`, the mix's slimes):
  - **At the blow.** A crystal death breaks on the killing blow's impact frame, from whatever pose
    the monster is in (walking, attacking, flinching), with no death clip, hold or pause. Its death
    plays no clip (`TimelineEvaluator.BreaksAtImpact`, `ClipSchedule.ActiveAction`'s `skipDeath`),
    so up to the blow it shows exactly what it would have without dying; from the blow it is
    `State.Broken`, and DemoMain hides the instance and hands it to `CrystalShatter.Breaking`.
  - **Baked shards on the rest pose.** CrowdBaker gives `CrowdShardBaker` each type's LOD0 style
    mesh on its rest pose, which it cuts into 20 to 40 chunks of varied size: surface patches
    around weighted seeds, split into connected pieces and merged by contact, each closed by inside
    faces to an apex inside the body (the vendor meshes are rarely closed, so a Voronoi slice would
    have nothing to cap). Every corner keeps the style mesh's bones and weights (UV4, UV5), and an
    apex takes its patch's area-weighted blend. They go to `CrowdShards.asset` beside the style
    meshes, which only a crystal run loads. The rest pose because the pose at the blow varies: it is
    the pose every pose is skinned from, so the chunks can be skinned to any of them, and its limbs
    stand apart, so a chunk rarely spans two.
  - **The swap: posing the chunks on the CPU.** `CrystalShatter` takes a pooled set of the type's
    chunks and skins every corner with `CrowdPose`, a CPU mirror of the crowd's skinning: the bone
    texture's rows (30 Hz under the default cuts) around both slots' clip times, lerped and
    crossfaded as `CrowdPalette.compute` does, then the corner's weighted matrices and the
    instance's scale, yaw and position, as `SkinVertex` does. So the chunks land where the crowd
    drew the monster on that frame (an EditMode test checks the CPU's bones against the GPU's
    palette, and every chunk corner against the drawn mesh). Each chunk then becomes rigid: its
    frame turns with the bone that carries most of it, its pivot is its box's centre in that
    frame, and its mesh (its own per pooled set) is rewritten relative to them, with the inside
    faces' normals from their posed corners. Why on the CPU rather than in the shard shader: the
    colliders and the kick need the posed chunks on the CPU anyway, it happens once per death, and
    the shards stay plain MeshRenderers whose shadow and motion-vector passes URP draws unchanged.
    The bone textures stay readable for it. The shards flash white as the crowd's hit flash does
    (`_ShardFlash`, over 0.12 s).
  - **The break.** On the same frame every chunk becomes a PhysX rigidbody with a box collider
    fitted to it (85% of its box, as a chunk tapers to its apex; colliders approximate), kicked
    outward from where the blow landed (`CrystalShatter.HitPoint`: 0.6 of the height up, toward
    the hero), harder near it, along the killing blow (the hero to the monster) and a little up:
    2.8, 5.0 and 1.6 m/s for a 1.8 m monster. Each shard's share varies widely: a quarter slump
    where they stand (the legs fold under the body), one in eight flies far, and the largest
    chunks get about half the smallest's share. Small shards spin fastest. Gravity is 2.2 times
    Earth's, so they clack down, bounce once or twice, skitter and settle into a pile on a floor
    collider under y = 0, and sink after 1.2 s (over 0.6 s). They linger only that long so the
    piles never bury the hero (at 5 s they hid the hero under neon and cost most of the frame).
    The blow also brings the burst: a spray of bright chips and crystal dust along the blow with a flash of light in place of the death burst
    ([effects.md](effects.md#the-look)), and a layered glass break pitched by the type's size, on
    top of the death cry ([camera-and-sound.md](camera-and-sound.md#sound)). Both are worked out
    from the timeline ahead of time (`CrystalShatter.Breaks`, at each death's impact). Physics
    steps on the song clock in script mode at 120 Hz, so `--fixed-time` runs repeat exactly;
    deaths before a run's first frame are not reconstructed. Tuning and filmstrips:
    [shatter/impact](../../demo3d/evidence/shatter/impact/README.md).
  - **Motion vectors.** Each shard draws a second material on the same shader in the last opaque
    queue with only its MotionVectors pass enabled, so under `--aa taa` the shards reproject with
    their own motion (the crowd's crystal monsters still smear, as below).
  - **Cost** (the Mac's M5 Max, 2026-10-30; the starting point for the scale-up). Beauty came
    first. Each shard is a pooled GameObject with a PhysX rigidbody, a box collider and two
    materials (the surface and its motion-vector twin), and physics steps at 120 Hz. At the song's
    own density under `--monster-style crystal --palette neon` (windowless, real time, `--stats`,
    whose `shards` and `shatterMs` columns are the live shards and the shatter's time, including
    posing the chunks at each break): 6,350 deaths shattered, no errors. With the old 5 s linger
    and 1.2 s sink, at most 16,659 shards were live; with 1.2 s and 0.6 s, 10,919.

    | song time | live shards (mean), 5 s → 1.2 s | CPU frame (mean / max ms) | of it the shatter (mean ms) |
    |---|---:|---:|---:|
    | 60 | 5,220 → 1,548 | 198 / 542 → 0.6 / 1.4 | 197 → 0.1 |
    | 113 | 5,968 → 1,560 | 33 / 93 → 0.7 / 1.7 | 31 → 0.2 |
    | 145 | 9,029 → 2,818 | 130 / 390 → 4.7 / 27 | 128 → 3.9 |
    | 175 | 13,981 → 4,545 | 366 / 700 → 12 / 53 | 363 → 11 |
    | 197 | 7,012 → 4,201 | 252 / 463 → 15 / 106 | 249 → 14 |

    The numbers are the mean and max over the 4 s around each time (a sample every 0.5 s), with
    no frames drawn; the whole song's mean CPU frame fell from 127 to 2.9 ms. With the long linger
    the shards on the floor were most of the cost: a real-time run that falls behind steps physics
    up to 12 times a frame, so one slow stretch fed the next and the run never caught up. What is
    left is the climax's and the ending's mass kills, where tens of monsters break in one frame
    (posing each set skins every corner of its chunks, tens of thousands, on the CPU). Moving the
    posing and the physics off the main thread, or onto the GPU, is the scale-up epic's.
  - `--shatter-log` logs each break with its song time and place in the view, to find deaths for
    filmstrips (`demo3d filmstrip`, [mac.md](mac.md#filmstrips)).
  - **Transparent styles.** Crystal draws in Transparent-10 and refracts the camera's opaque
    texture. URP's asset keeps that texture off, and `MonsterStyle.NeedsOpaqueTexture` turns it
    on for the game camera only under crystal and mix (DemoMain): one half-resolution copy of the
    colour target per frame. Hologram is additive in Transparent, so it needs no sort. It has
    no ShadowCaster, so BoneTextureCrowd skips its shadow draws.
  - **No depth or motion passes run for them.** URP draws DepthOnly, DepthNormals and
    MotionVectors for the opaque queue only. Crystal writes depth in its forward pass, but its
    monsters are missing from the depth texture (SSAO) and get the camera's motion only. Under
    `--aa taa` (not the default) moving crystal monsters smear.
- **The beat comes from `StyleBeat`.** It sets `_SL_Time`, `_SL_Beat`,
  `_SL_Pulse`, `_SL_BarPulse`, `_SL_Energy` (the song map's value per beat),
  `_SL_HeroPos` and `_SL_WaveSpeed` every frame, from the song as heard.
- **Post comes from `MonsterStyle.Post`.** It is the lab's `PostLook`
  (tonemapper, exposure, contrast, saturation, bloom, chroma, vignette),
  written over the night volume before PostFeel reads its base values, so
  the beat and section offsets still ride on top. The night's LUT, vignette
  shape and white balance stay as they are. The look pass's crowd-only
  knobs (type dye, `crowd.shoulder`, key boost, crowd rim, flash) apply only
  to the baseline shader.
- **Shells are a second draw.** A style with a shell (fire's flames,
  painted's ink line) returns its material from `MonsterStyle.Shell`, one
  per type. `BoneTextureCrowd` draws it right after each submesh and LOD's
  main draw, with the same mesh, instance buffer, LOD block and args, so it
  costs no culling. Shell shaders are named `Demo3D/Styles/Shells/...`. They
  cast no shadow (the body does). The ink line is opaque, with its own depth
  and motion passes on its outward push (`SL_DISPLACE`). The flames are
  additive in the transparent queue, with no depth or motion vectors, so
  under TAA they reproject with whatever is behind them.
- **A style can change death.** The default is the baseline's dissolve. A
  style may define `SL_DISSOLVE(rest, death)` before StyleCommon, and every
  pass then clips with it: fire burns from the feet up, chrome melts into a
  puddle (its `SL_DISPLACE`) that dissolves in the last third, and void
  implodes to a point with no clip. `SL_DISPLACE` expands after
  StyleCommon's helpers, so it may use the noise, `SL_SeedOf(slot)`, the
  beat globals and the material's CBUFFER.
- **mix is a style per type**, the lab's showcase mix (`Treatments.StyleFor`):
  `MonsterStyle.StyleOf` gives painted skeletons, goblins and mushrooms; crystal slimes,
  spectrum bats and fire demons; chrome orks, void trolls and a fire dragon. Materials,
  shells and the style mesh follow each type's style; the post look is the mix's own.
- **Tuning.** Each style shader grades its forward colour with `SL_Grade`: a gain, then a
  soft limit on luminance (`MonsterStyle.Gain` and `Peak`, per type's style), which keeps the
  effects the brightest values over the self-lit treatments. `MonsterStyle.Post` is tuned for
  demo3d's arena rather than the lab's (the night's tonemapper, exposure and bloom threshold).
  The values and why are in
  [evidence/styles/all/README.md](../../demo3d/evidence/styles/all/README.md).
  `--style-tune key=value,...` overrides them for a run without a rebuild (keys: gain, peak,
  exposure, contrast, saturation, bloom, threshold, chroma, vignette, neutral), so a
  treatment can be judged with other values; it fails under baseline.
- **The hero keeps its own shader** under every style. The lab paints its knight, but
  demo3d's hero is drawn by `StockCrowd`, not the bone-texture crowd, so the style shaders'
  skinning does not apply to it.

### Roster sets

`--roster <set>` swaps the horde for one of two MoreAssets monster sets,
so the demo's feel can be judged with other monsters (epic ps-f8a). The
names are default (today's roster, the default), forest (the Dark and
Forest Creatures packs, cartoon) and heroic (HeroicFantasyCreaturesFullPackVol1,
realistic). An unknown name stops the player at start-up, and so does a
set beside a `--monster-style` other than baseline. The look harness passes
the flag through `--player-args`. Without it, the demo is unchanged.

- **Slots stay fixed.** A set keeps the nine roster ids (skeleton, goblin,
  slime, bat, mushroom, demon, bigork, troll, dragon) and points each at
  its own prefab and clips, so the choreography, the stress files and the
  timeline evaluator are the same. The set's file,
  `Data/roster-<set>.json` beside `Data/roster.json`, lists only the slots
  it replaces, with their own heights and radii; every other slot, and the
  hero (the Synty knight), stays the default's. `hide` names prefab
  children the baker removes, for a pack prefab that carries every weapon
  at once.
- **The level has a variant.** `Choreography/Data/action-level-<set>.json`
  is the song's script under a closer camera and fewer monsters: 150 on
  screen at 18 m to start, each section's width scaled to match.
- **The stock crowd draws it.** A set forces sceneOptions `crowd: stock`
  (`--crowd stock`; another renderer fails), with the prefabs' own
  materials: no `typeColors` dye, no hue rotation, no tint over their base
  colour, and the hit flash sets emission only while it lasts. URP Lit
  packs (heroic) keep their vendor materials; the Standard packs (forest)
  get URP Lit copies with their albedo and glow maps.
- **It bakes on request.** `demo3d setup --roster <set>` syncs the set's
  packs from the `more_assets` root (only the Mac maps it,
  [vendor-assets.md](vendor-assets.md#the-roster-sets)), and
  `demo3d unity BakeRoster -roster <set>` bakes its prefabs, converted
  materials and RosterAssets index into `Assets/Baked/Rosters/<set>/`.
  BakeAll, the default build and the checks never touch them; a player
  built without a set's bake stops at start-up with the commands to run.
- **Its clips are annotated per set.** `demo3d unity AnnotateClips -roster
  <set>` measures the slots the set's file lists and writes them to
  `clip-annotations.json`'s `rosters.<set>`; under `--roster <set>` the
  player puts each in place of the default entry with the same use (so clip
  indices and roles are unchanged), and a slot the set hasn't annotated
  keeps the default's. A default run of the annotator keeps the sets'.
- **A set's radii separate its monsters.** Under `--roster <set>` the
  choreography takes each slot's radius from the set's baked roster in place
  of `Choreography/Data/roster.json`'s (a harpy's wingspan, a rat's length);
  the default's are unchanged.
- **Annotation overrides.** A set's type can give `strideM` (a flyer whose
  feet don't step), a clip can give `impact` in seconds (read off its
  contact sheet where the peak speed falls in the wind-up), and
  `rootMotionClips` measures a walk's stride from its root-motion twin
  without playing it.
- **Heavy prefabs are made at load.** With a set, the stock crowd makes each
  type's slots at load (48 per fodder type, 8 per elite, 2 for the boss),
  since making a section's worth of several-mesh prefabs in one frame
  hitched by 25-40 ms on the Mac.
- **Pack particles are converted.** A set's bake swaps its prefabs' built-in
  particle materials (auras, trails) for URP Particles/Unlit copies with the
  same texture, tint and blend; the default roster's are untouched.
- **What ps-f8a.1 proved:** one creature per set, Evil Mushroom (forest's
  mushroom) and the heroic Goblin with sword and shield (heroic's goblin).
- **Forest fills all nine slots** (ps-f8a.2): Wererat Soldier, Treasure
  Goblin, Spiderling Venom, Forest Bat, Evil Mushroom and Demon Wolf, the
  Dark Treant and Forest Golem as elites and the Fantasy Dragon (on the
  ground) as the boss, each in one pack colourway, with their glow maps
  (a set's bake flags its converted materials emissive, or URP drops
  `_EMISSION` on save). The roster's `impactBone` names a clip's striking
  bones for the annotator where the fastest is a tail. Its level keeps 150
  on screen at 18 m, measured windowless at about 3x under the 60 fps
  budget ([evidence](../../demo3d/evidence/rosters/forest/README.md)).
- **Heroic fills all nine slots** (ps-f8a.3): Skeleton Knight, Goblin,
  Giant Rat, Harpy, Kobold and Ghoul, the Fat Ogre and Troll as elites and
  the Mountain Dragon as the boss, in the pack's own URP Lit materials, with
  texture memory capped at import (1K fodder, 2K elites and boss: 264 MB)
  ([evidence](../../demo3d/evidence/rosters/heroic/README.md)).
- **Each set has its own look** (ps-f8a.4). arena-layout.json's `rosters`
  gives each set a partial `lighting` object over the night's, as a palette
  does, read only by the runtime, so the baked kit is the night's. The post
  stack's base values (`lighting.post`: exposure, contrast, saturation,
  white-balance temperature, tonemapper and bloom) moved there from code,
  and the post feel's beat and section offsets still ride on top. The sets'
  materials take none of the bone crowd's fill, rim, key or shoulder, so
  their look comes from the grade and the lights:
  - forest: saturation 14 (the night's 30 made the packs' already
    saturated paint scream), contrast 22, bloom threshold 1.8 at 0.8
    intensity, so bloom stays on the glow and the effects. The roster
    file's `glow` scales a type's glow-map emission at bake: the wolf's
    spikes 0.5, the treant 0.6, the golem 0.7 and the dragon 0.6, while the
    small eyes keep the pack's 1.1.
  - heroic: a cooler moon (0.86, 0.9, 1.0 at 1.15), exposure 0.65, saturation
    14, contrast 32 and temperature -10, so the leather, bone and metal keep
    their detail instead of sinking into the warm ground's browns.
  - A `--palette` still applies over a set's look.
- **A set's corpses sink.** Stock URP Lit has no dissolve, and a
  dissolve-capable copy of URP Lit would be a fork of a large package
  shader to maintain against a vendor-materials-only epic, so the stock
  crowd sinks a set's corpse its whole height below the ground over the
  dissolve's beat (easing in), instead of scaling it away: the death clip
  plays out, the body lies still for a beat, then the ground takes it, with
  its materials untouched. The default stock crowd still scales away.
- **Running a set.** On the Mac, in a worktree:

  ```
  uv run --project demo3d/tools demo3d setup --roster forest --roster heroic
  uv run --project demo3d/tools demo3d unity BakeAll
  uv run --project demo3d/tools demo3d unity BakeRoster -roster forest
  uv run --project demo3d/tools demo3d unity BakeRoster -roster heroic
  uv run --project demo3d/tools demo3d build
  open -n demo3d/Builds/mac/il2cpp/PlaylistSurvivors3D.app --args -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 --sound --roster forest
  ```

  Name both sets in one `setup`: it syncs only the sets it is given. The
  look set of a set is `demo3d look <slug> --player-args "--roster <set>"
  --baseline final`; [evidence/rosters/compare](../../demo3d/evidence/rosters/compare/README.md)
  puts default, forest and heroic side by side.

## The Hero

The hero is one character, so stock Unity animation is fine for them, but
their clip timing still comes from the timeline.

- **Model.** A Synty knight with sword and shield, from POLYGON Knights or
  Fantasy Characters, animated with Synty's Sword Combat, Base Locomotion and
  Idles packs.
- **The crystal blade.** The knight's broadsword is replaced by a big
  white crystal blade (`HeroBlade`), under every `--monster-style`: a
  1.15 m faceted blade with a spine, a crossguard of crystal points,
  shards growing from its root and a pommel gem. Its mesh is made at
  start-up, so it is no vendor asset. The broadsword's renderer is turned
  off.
  - **The grip** comes from the right hand's bones in the bind pose: the
    handle runs through the fist across the knuckles (from the fused
    fingers' root to the index finger's), the blade leaving it on the
    thumb's side with its edge toward the knuckles, and the handle sits
    2 cm in from the knuckles toward the palm. That is how the Sword
    Combat clips hold a sword. The Synty broadsword's own axes are 90
    degrees off this hand's (its blade runs along the hand bone's Y, the
    fist along its Z), so a blade placed by the broadsword stuck out of
    the back of the hand and swung through the body.
  - **The scale.** The roster attaches weapons to the hand bone with no
    correction for the bone's scale, which drew the broadsword about 1 cm
    long and the shield as small: the hero looked empty-handed. The blade
    undoes the hand's scale, so its mesh is in metres, and `HeroBlade`
    gives the shield (`SM_Wep_Shield_01`, on the left hand, placed as Synty
    made it) the character's scale. The crowd's weapons are untouched.
  - **The arms while walking.** The hero's walk and run are Base
    Locomotion's unarmed clips (Sword Combat has none), whose swinging
    arms carried the blade behind the back and through the legs. So
    `StockCrowd.HoldArms` layers the sword idle's arms and fingers (an
    `AvatarMask` on an `AnimationLayerMixerPlayable`) over the walk and
    run, by their share of the blend, so the arms ease into and out of
    the combos with the crossfades; the hold breathes on the walk's phase.
    The hero walks about a third of the song and never plays the idle on
    its own.
  - **The look** is Crystal's (`CrystalCommon.hlsl`, in
    `Demo3D/Styles/CrystalBlade`): faceted gem refracting the scene, banded
    reflections, lit facet edges and glints, in white. A light inside,
    brightest along the spine, throbs on the style beat and flares on each
    blow (the timeline's slash, thrust, spin and slam effects, over
    0.22 s), past the bloom's threshold, so the blade glows.
  - **Every run** therefore turns on the camera's opaque texture (one
    half-resolution copy a frame) and publishes the style beat
    (`StyleBeat`), which only the style shaders read.
  - It casts a shadow, draws black in TD15's ID render, as the hero does,
    and has a motion-vector twin material, as the crystal shards do.
  - **The cut.** An eight-sided section (edges, a spine down each flat and
    a bevel either side) in 14 rings, every corner but the spine's nudged
    its own way, so each triangle tilts on its own and the blade reads as a
    mosaic of facets, as the crowd's crystal does. A plain diamond section
    showed two flat faces from above and read as a white bar.
  - **Tuning.** The blade passes a fifth of the scene behind it
    (`_Behind`, against a crystal monster's whole), so the hero's bright
    light pool doesn't wash its facets out, and its gem is a grey white
    that the light inside lifts to white. `--blade-tune key=value,...`
    overrides the material for a run (behind, glow, flare, gain, peak,
    refraction, facets, and the gem's r, g, b) and the grip (palm and back,
    the handle's offsets in metres; pitch, yaw and roll, a turn in
    degrees; length, the sword's scale). Stills:
    [evidence/blade](../../demo3d/evidence/blade/README.md).
- **Animation.** A `PlayableGraph` driven by song time plays the hero's clips,
  with no Animator state machine. Each combo clip starts as its retiming
  dictates, and locomotion phase follows distance.
- **Combos.** Slashes left and right, a thrust, a spin on accents and an
  overhead slam on phrase-opening downbeats. Each impact spawns its slash
  effect aligned to the swing.
- **Spells.** Fired from the section's spell set: rings, bolts, lightning, and
  a laser in the climax. They grow larger and denser as the song builds.
- **Parries.** Blows that would land are parried with a shield flash and
  sparks. The hero never takes damage.
