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
  1-beat dissolve with an emissive edge.
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
spectrum, hologram, chrome, void and mix. An unknown name, or one not yet
ported, stops the player at start-up. baseline is the default, and it is
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
  LODs unwelded (`CrowdBaker.Unweld`, stylelab's StyleMesh), as `CrowdType.StyleMesh`,
  `StyleLod1` and `StyleLod2`, so every triangle has its own vertices. Bone data stays in
  UV1/UV2 and the position is already the rest pose in metres. The face normal is in the
  tangent, and barycentrics plus a random value per triangle are in UV3. Only a style whose
  `MonsterStyle.UsesStyleMesh` is true (crystal, spectrum, hologram) loads it: its shader defines
  `SL_STYLE_MESH`, which adds those streams to `SLAttributes` and `bary` to the varyings, and
  `SL_SkinFace` turns the face normal with the bones. The welded meshes and the default path are
  unchanged.
  - **Shatter** (crystal, spectrum) is `SL_ShatterDisplace` in `SL_DISPLACE`, so every pass
    moves the shards. A shard that has finished its flight collapses to a point, and such a
    style sets `SL_DISSOLVE` to `0.0` (no clip). Hologram's scan-out is its own `SL_DISSOLVE`.
  - **Transparent styles.** Crystal draws in Transparent-10 and refracts the camera's opaque
    texture. URP's asset keeps that texture off, and `MonsterStyle.NeedsOpaqueTexture` turns it
    on for the game camera only under crystal (DemoMain): one half-resolution copy of the
    colour target per frame. Hologram is additive in Transparent, so it needs no sort. It has
    no ShadowCaster, and URP skips the depth and motion passes in the transparent queue.
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
- **Ported so far:** painted (with its ink line), fire, chrome, void, crystal, spectrum and
  hologram.
- **The hero keeps its own shader** under every style.

## The Hero

The hero is one character, so stock Unity animation is fine for them, but
their clip timing still comes from the timeline.

- **Model.** A Synty knight with sword and shield, from POLYGON Knights or
  Fantasy Characters, animated with Synty's Sword Combat, Base Locomotion and
  Idles packs.
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
