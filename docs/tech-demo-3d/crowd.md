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
