# The Neon Crystal Look: One Visual Language for the Trailer and the Game

## Introduction

People who watched the reworked crystal trailer
(`captures/trailer-crystal-v2.mp4`, demo3d's `--trailer crystal` cut) liked
its direction. The shatter, and above all the shattering crystal formations,
landed. The biggest gap is that it reads as **stitched-together parts, not one
designed whole**:

- each shot sits in a different Synty world (lava, snow, neon city, crypt)
  with its own lit ground and grade;
- the monsters are flat, saturated, low-detail bodies that read as "an older
  3D game";
- in a clump they merge into one coloured blob instead of a swarm;
- effects (gold slashes, white shards, orange fire, rune circles) come from
  different sources with no shared colour logic.

Selected viewer quotes (2026-11-01):

> Something about the bright colour low detail gives an older 3d game vibe.
> I like the death animations though.

> I'd probably go with crystal but try to add more of the retro-vibe flashy
> animations to add polish.

> In my mind I was picturing something akin to geometry wars, though that's 2d.

> I think kinda neon lighting effects to go with the neon enemies.

> When the enemies clump up they look like a blob more than a swarm?

> Something about the large number of low-poly entities makes it feel 'flat'.

This document (epic ps-wnj) plans a ground-up redesign around **one baseline**: a shared
palette, a shared post-processing stack, new procedural VFX and world assets,
and more fluid motion. It lives in a **new shared Unity package** that both
demo3d's crystal trailer and the Unity game (`game/`) use.

### The north star: neon crystal in the void

Geometry Wars 3's visual grammar, in 3D
([store page](https://store.steampowered.com/app/310790/Geometry_Wars_3_Dimensions_Evolved/)):

- **A near-black world.** Almost all light on screen is *emitted*: edges,
  rims, trails, sparks, the floor's grid. Nothing is lit flat by a sun.
- **Controlled, heavy bloom.** Emitters glow; the dark stays dark.
- **A reactive floor.** A light grid that ripples out from every blow and
  death and breathes on the beat, the 3D answer to GW's warping grid.
- **Crystal is the one material.** Monsters, structures and shards are all
  crystal, so the shatter people loved is native to the look rather than an
  effect on top of it.
- **Fantasy silhouettes stay.** Monsters keep their Synty shapes and
  animation; only how they are drawn changes.

### Everything aesthetic is a choice, bundled into presets

The operator wants every aesthetic axis selectable, to compare options rather
than commit early. Cohesion comes from **named look presets**:

- a preset is one data file naming a palette and one option per axis;
- every axis takes its colours *only* from the palette's semantic roles;
- `--look <name>` picks a preset; per-axis flags override it for experiments;
- each preset is captured as a whole trailer.

There is **no chosen default**. All four presets are first-class, and the
trailer is captured in each. A bare launch falls back to `edge` only so it
runs; that is not a decision, and every capture names its look.

## Related Information

- [3D Tech Demo: Trailer Mode](../tech-demo-3d/trailer.md): the trailer this
  redesigns. Especially **The crystal cut**, **Crystal formations** and
  **Rules for Every Frame**.
- [The Unity Game](unity-game.md): `game/`, which copies demo3d's crowd,
  shatter and effects today and will move onto the package.
- [Style Lab](../tech-demo-3d/stylelab.md): earlier monster treatments.
  Spectrum (black lacquer with a neon wireframe) and Hologram are starting
  points for two looks here.
- [Effects](../tech-demo-3d/effects.md): the compute particle runtime.
- [Crowd and Hero](../tech-demo-3d/crowd.md): the bone-texture crowd and
  roster.
- [Vendor assets](../tech-demo-3d/vendor-assets.md): what can never be
  committed.
- [Choreography](../tech-demo-3d/choreography.md): the precedent for a shared
  package, `unity-packages/com.playlistsurvivors.choreography/`.

## Decisions

Made with the operator on 2026-11-01. Executors decide everything else and
record their reasons in bead notes.

1. **A shared package.** New package
   `unity-packages/com.playlistsurvivors.look/`, referenced by `file:` from
   `demo3d/` and `game/` (the choreography package's pattern). It is
   `game/`'s first shared package.
2. **Proven in the trailer, adopted by the game in the same epic.** The
   crystal cut is where feedback comes from and where the capture and
   evidence pipeline lives. `game/` adopts every preset before the epic
   closes.
3. **Neon crystal void** is the north star (above).
4. **Every aesthetic axis is flaggable**, bundled into **named presets**.
   All presets are first-class; none is the default.
5. **Four monster looks**, each the heart of one preset:
   - **Dark core, lit edges**: near-black glass; the type's hue only in a
     fresnel rim, baked feature-edge lines and a small inner core that flares
     on the beat.
   - **Bright solid emissive**: saturated self-lit bodies with a thin dark
     outline so neighbours separate.
   - **Wireframe hologram**: lines plus a faint additive fill.
   - **Refractive gem**: today's refractive crystal rebuilt on the palette
     and post stack.
6. **Procedural assets.** New props, formations, the grid floor, trails and
   VFX are generated in code and shaders on the existing compute particle
   runtime, so they are original and committable. Synty monster and hero
   meshes stay as silhouettes; the baker derives edge data from them at build
   time and never commits it.
7. **Motion layers**: spring secondary motion, swarm individuality, light
   trails and smears, spawn and death flourishes.
8. **Beat steps stay exact.** Every monster's step lands on the beat's frame.
   A ripple (steps offset outward from the hero, up to ~1/16 note by distance)
   is an option behind a flag.
9. **The game's frame budget holds.** Every preset runs in `game/` within its
   current budget at the perf peak (`game perf`: no frame over 33 ms on the
   Mac, ~300 alive). The trailer may add only offline extras: supersampling,
   higher particle caps, heavier blur.
10. **Keep the cut, re-stage the world.** The crystal cut keeps its song
    window, hook/breathe/climax structure, formation shatters and dragon
    finale. Each shot is re-staged for the new world, and cuts become
    designed transitions.
11. **Replace crystal, keep action.** The crystal cut and `game/` move fully
    onto the package and their old crystal look code is deleted. demo3d's
    action cut gains an opt-in neon look (ps-wnj.26, operator request
    2026-11-01): with any look flag, `--trailer` plays the neon action cut
    (`trailer-action-neon.json`, [The Trailer](#the-trailer)). Plain
    `--trailer`, with no look flag, is the plain action cut: it and its
    Synty-environment looks stay untouched as the "before" reference.
12. **Style guide plus metrics** judge cohesion (see
    [Checks and Evidence](#checks-and-evidence)).
13. **Sound**: only the new effects get sounds (spawn warp, death pop, grid
    ripple), chosen from the existing free sound library and scheduled on the
    beat grid. A cohesive sound palette is a separate epic.
14. **Light run** (AGENTS.md): Executors and one final Warden.

## The Package

`com.playlistsurvivors.look` holds everything that makes the look, and nothing
about gameplay. It has no knowledge of the choreographer or the sim; each
project feeds it events through a small interface.

### What moves in

Today `game/` holds forked copies of demo3d's code ("copied and owned"). The
look-bearing pairs move into the package and both forks are deleted:

| Piece | demo3d today | game today |
| --- | --- | --- |
| Crowd renderer | `Runtime/Crowd/BoneTextureCrowd.cs`, `Crowd/Shaders/Crowd*.{shader,hlsl,compute}` | `Client/Crowd/BoneTextureCrowd.cs`, `MonsterInstance.cs` |
| Monster shatter | `Crowd/Styles/CrystalShatter.cs`, `CrystalShard.shader` | `Client/Crowd/MonsterShatter.cs`, `MonsterShard.shader`, `MonsterShards.compute` |
| Structure shatter | `Arena/StructureShatter.cs`, `StructureShards.cs` (kept: the action cut's) | `Client/Structures/StructureShatter.cs`, `StructureShards.cs` (moved in, ps-wnj.8) |
| Crystal formations | `Arena/CrystalFormations.cs` (moved in, ps-wnj.8) | none |
| Effects runtime | `Runtime/Effects/Effect*.cs`, `EffectSim.compute`, `EffectParticles.shader` | `Client/Effects/` forks |
| Post and grade | `Arena/NightLook.cs`, `Playback/PostFeel.cs`, `TrailerLook.cs`, `LensHitFeature.cs` | `Client/Arena/NightLook.cs`, `Client/Look/PostFeel.cs` |
| Beat globals | `Crowd/Styles/StyleBeat.cs` | `Client/Look/BeatGlobals.cs` |
| Bakers' unweld step | `Editor/Crowd/CrowdBaker.cs` (`Unweld`) | `Editor/Crowd/CrowdBaker.cs` |

Paths are under `demo3d/Assets/Demo3D/` and `game/Assets/Game/`.

**Moved so far (ps-wnj.4)**: the crowd renderer, its instance struct, slots,
culling, library format and shaders, the GPU monster shatter, the crystal
surface includes (CrystalCommon, CrystalSurface, StaticShadow) and the beat
globals are the package's (`Runtime/Crowd/`, namespace `PS.Look.Crowd`;
`Runtime/Shaders/`; `Runtime/Beat/BeatGlobals.cs`), as are the bakers' unweld
step (`StyleMesh.Unweld`) and shard cutter (`CrowdShardBaker`, in the package's
editor assembly `PS.Look.Editor`). Bodies and shards take their materials from
a **monster look** (`IMonsterLook`, registered by option in `MonsterLooks`); an
option without a look draws `MonsterLooks.StandIn`, the refractive gem since
ps-wnj.13 (today's crystal, MonsterCrystal and MonsterShard, is the `classic`
option's alone, `MonsterLooks.Classic`). The game
draws through the package and its forks are deleted; its `CrowdView` feeds the
crowd and its crowd baker (game's roster and cast) calls the package's steps.
demo3d's `StyleBeat` publishes through `BeatGlobals`. Players hook the package's
compute kernels' warm-up through `ComputeKernels.Prepare` (the game's GraphicsStates).

**The crystal cut on the package (ps-wnj.22)**: demo3d's crystal cut
(`--monster-style crystal`) draws its monsters and their shards with the
package's `BoneTextureCrowd` and `MonsterShatter` in `MonsterLooks.Active`, fed
by `LookCrowd` (its break schedule, bursts and sounds stay demo3d's), from a
package `CrowdLibrary` demo3d's crowd baker writes beside its own
(`LookCrowd.asset`: joined style meshes, 30 Hz bones, the package's shard
cutter). demo3d's crystal forks are deleted: `CrystalShatter`, `CrowdShards`,
`CrowdPose`, its `CrowdShardBaker` (its structure shard baker uses the
package's cutter) and the `Crystal` and `CrystalShard` style shaders (the mix's
slimes are hologram now). The horde's beat pulse (`_SL_Bounce`, `_SL_Throb`)
and the trailer's inner glow (`_SL_InnerGlow`) moved into the package's crowd as
globals only demo3d sets, so the game's look is unchanged (only the crystal,
classic's monster look since ps-wnj.13, reads the inner glow: the other looks
look the same in both players). Where the forks
diverged the crystal cut took the game's: GPU shards for PhysX, LODs, the
half-tinted hit flash ([crowd.md](../tech-demo-3d/crowd.md#monster-styles)). The
plain action cut and the other Synty styles keep demo3d's own crowd (Decision
11); the neon action cut draws through the package like the crystal cut.

Where the forks have diverged (game's shards on compute, its regrowth, its heard-time
strikes), the package takes the game's version: it is the one that has to
hold a frame budget.

What stays in each project: playback and the choreographer (demo3d), the sim,
presenters and HUD (game), and the trailer's shot system. demo3d's action cut
keeps its own copies of anything it needs that the package changes, so the
plain action cut's look does not move.

### Palettes

A palette is a JSON file of **semantic roles**, never of objects. Everything
drawn reads its colours from a role.

```json
{
  "name": "electric",
  "void":    { "floor": "#05060a", "fog": "#0a0c18" },
  "grid":    { "line": "#1d3cff", "pulse": "#5f7dff" },
  "hero":    { "body": "#f4f1ff", "blade": "#ffffff", "trail": "#9ff6ff" },
  "enemies": ["#ff2e88", "#28f0ff", "#9dff3a", "#ffb21e", "#b45bff", "#ff5a2e"],
  "boss":    { "body": "#ff3355", "telegraph": "#ff1030" },
  "shatter": { "gem": "#ffe9a8", "core": "#ffffff" },
  "spark":   "#ffffff",
  "zones":   [ { "name": "cobalt", "tint": "#1d3cff" }, { "name": "rose", "tint": "#ff2e88" } ]
}
```

Values above are only an example. The real palettes are `ion`, `arcade`, `phosphor` and
`spectrum` in the package's `Palettes/`, set by the
[style guide](neon-look-style.md#the-palettes), which also maps monster types onto the enemy
slots (`enemies[0..1]` the key family, `[2..3]` the accent, `[4..5]` the counter).
Rules the style guide enforces:

- **A hue budget per frame**: the hero's colour, at most three enemy hue
  families, and the shatter colour. Monster type blocks map onto enemy hue
  slots.
- **White is earned**: only impact frames, sparks and the blade's flare reach
  white.
- **The floor is near black** everywhere outside a ripple or a light pool.

In the package (ps-wnj.3), palettes are
`Runtime/Resources/Look/Palettes/<name>.json`, loaded in both players' builds
through Resources. `Palette` parses one: a top-level hex string is a role
(`spark`), a group's member is `group.member` (`hero.body`), and the doc's
roles above are required; a palette may add roles, and `name`, `description`
and `note` are not roles. `SetShaderGlobals` gives
every role to shaders as `_Look` plus its parts capitalized
(`_LookHeroBody`), a linear vector (ps-wnj.7: `Shader.SetGlobalColor` had
passed the sRGB values unconverted), and the enemy hues and zone tints as linear arrays
`_LookEnemies[8]` and `_LookZones[8]` with `_LookEnemyCount` and
`_LookZoneCount`. The style guide's four palettes are there: `ion` (edge),
`arcade` (solid), `phosphor` (hologram) and `spectrum` (prism).

### Look presets

A preset is a JSON file in the package (`Looks/<name>.json`):

```json
{
  "name": "edge",
  "palette": "ion",
  "monsters": "dark-core-edges",
  "structures": "edge-glass",
  "world": "void-grid",
  "post": "neon-bloom",
  "vfx": "light-streaks",
  "hero": "edge",
  "motion": { "springs": true, "swarm": true, "trails": true, "beatRipple": false }
}
```

The starting four presets, one per monster look. Each preset's other choices
are the executors' call against the style guide:

| Preset | Palette | Monsters | Likely world | Likely post |
| --- | --- | --- | --- | --- |
| `edge` | `ion` | dark core, lit edges | void grid | heavy bloom, filmic tonemap |
| `solid` | `arcade` | bright solid emissive | hued zones | punchy contrast, moderate bloom |
| `hologram` | `phosphor` | wireframe hologram | pure void grid | scanlines, chromatic aberration |
| `prism` | `spectrum` | refractive gem | hued zones | dispersion in the palette's hues, soft bloom |

### Flags

Both players take the same look flags:

```sh
--look edge                      # pick a preset
--monster-look wireframe         # override one axis
--world pure-void --post neon-bloom --palette ion
--motion springs,swarm           # exactly these motion layers (or none), in place of the preset's
--beat-ripple                    # steps ripple outward (off in every preset)
--look classic                   # the look before the epic, for parity checks
```

The crystal cut's file (`trailer-crystal.json`) names no look. The capture
recipe takes one or many:

```sh
just trailer --out captures/trailer-neon-edge.mp4 -- --trailer crystal --look edge
just trailer-looks                # all four presets, captures/trailer-crystal-PRESET.mp4
just trailer-looks action         # the neon action cut, captures/trailer-action-PRESET.mp4
```

`trailer-looks [CUT] [demo3d trailer flags] [-- player flags]` (ps-wnj.26,
ps-wnj.19) builds the player once and captures one cut in edge, solid,
hologram and prism in turn. `demo3d trailer`'s flags go to every capture
(`just trailer-looks --stand-in`, for agents: the mix and its checks with a
black picture), flags after `--` to every preset's player. It exits 1
naming the presets that failed. The windowed captures are the operator's
(game/evidence/operator-pending.md).

Three flags set a look's values for one run, to judge a change without a
rebuild (each fails on a key it doesn't know):

```sh
--post-tune halo.intensity=1.2,lens.chromatic=0.04   # the post option's values, as its file's groups
--monster-tune Edge=3,shards.Rim=1                   # the monster look's shader floats (body. or shards. for one)
--hero-tune blade.Glow=2                             # the hero's (body. or blade.)
```

How the package implements them (ps-wnj.3):

- Presets are `Runtime/Resources/Look/Looks/<name>.json`. A preset names its
  palette, a string for every axis and a `motion` object; an unknown key, a
  missing axis or an unknown motion layer fails at start.
- **The axis registry** is `LookAxes`: each axis (monsters, structures,
  world, post, vfx, hero, and motion's layers) is declared in its own file
  under `Runtime/Axes/` with the options built so far. The work that builds an
  option adds it to its axis's file and nothing else.
- An option or layer a preset or flag asks for that isn't built draws today's
  look: `ResolvedLook.Option(axis)` is null for it, and applying the look
  logs one warning listing everything not built yet.
- **`classic`** is the look before this epic, for parity comparisons:
  every axis takes the option `classic` (`LookAxis.Classic`), which is never
  built, so the axis draws today's look without a warning, and the `classic`
  preset names it on every axis with no motion layers. `--look classic` puts
  back the Synty arenas, the crystal monsters and structures and the night
  post (game's `NightLook` grade and LUT, restored for it; the crystal cut's
  environments' night volumes, shot grades and trailer dressing); an axis
  flag mixes one axis in (`--look edge --post classic`). Not pixel-exact: the
  crystal cut draws its monsters through the package's crowd (ps-wnj.22's
  differences: GPU shards, every death shattering, LODs, the half-tinted hit
  flash), and the palette's role globals now reach shaders linear. For exact
  parity, capture a build of the tree before the epic (`34950f68^`).
- `LookFlags` parses the flags once; game's `LaunchOptions` claims them so
  its unknown-flag check doesn't flag them. `ResolvedLook.Resolve` picks the
  preset (`edge` without `--look`), lays the flags over it, and `Apply` sets
  the palette's shader globals and logs the look:
  `Look: look edge, palette ion; monsters wireframe (--monster-look), ...`.
- `--palette` keeps its old meaning beside the new one: a name that is one of
  the package's palettes is the look's palette; any other name is still the
  arena's zone palette (game's and demo3d's `neon`, `lava`, ...), which the
  void world retires. Game's tests check no arena palette shares a look
  palette's name.
- `game/` resolves and applies the look at boot in every launch. demo3d does
  so for any trailer but the plain action cut: `--trailer` (the action cut)
  with any look flag plays the neon action cut in its place and says so; the
  song outside a trailer ignores the look flags and says so.

## The Axes

### Monsters

All four looks draw on the same instance buffer, skinning and beat globals.
What every look must do:

- **Separate neighbours.** Two same-type monsters touching must read as two.
  This is the "blob" feedback, and the swarm-separation metric measures it.
- **Show depth.** Rim, fresnel or refraction varies with view angle, so a
  crowd has form, not flat cut-outs. This is the "flat" feedback.
- **Pulse on the beat**, through the existing beat globals.
- **Shatter in their own look.** Shards keep the look's edges, emission or
  refraction.

The baker's unweld step already writes face normals and barycentrics. It adds
**feature-edge data**: a per-vertex mask of which triangle edges are real
silhouette or crease edges (dihedral angle over a threshold) rather than
internal triangulation, so edge lines trace the model's facets, not every
triangle. Wireframe can use all edges or feature edges as its option.

How the package implements it (ps-wnj.10):

- **Feature edges** (`StyleMesh.FeatureEdges`, run by `StyleMesh.Unweld` for
  every LOD in both crowd bakers): UV5 of the style mesh, the same on a
  triangle's three corners, component k 1 where the edge opposite corner k
  is a real edge. An edge is real when its two faces turn by more than
  `FeatureAngleDeg` (24), when it is open, or when more than two faces share
  it; corners weld by position (0.1 mm), so UV and material seams don't
  count. Corner k has barycentric k 1, so a shader draws a line where a
  flagged barycentric is small (`EdgeLine` in MonsterEdge.hlsl, a fixed width
  in pixels from the barycentric's screen derivative). Derived at bake time,
  gitignored with the rest of the bake.
- **Shards keep them** (`CrowdShardBaker`): a skin triangle keeps its style
  mesh's edges and adds the crack, the edges on its chunk's boundary; an
  inside face flags its edge on the skin. Every chunk is outlined where it
  broke.
- **Type to slot** (`MonsterSlots`): the style guide's table in code, so a
  look that draws in the palette's hues takes a type's `enemies[i]` (the
  dragon `boss.body`) rather than the role's gem on each instance.
- **Dark core, lit edges** (`EdgeLook`, option `dark-core-edges`, the edge
  preset's; shaders `Resources/Look/Shaders/MonsterEdge` and
  `MonsterEdgeShard` over `Runtime/Crowd/Shaders/MonsterEdge.hlsl`): glass in
  `void.floor`, each facet a shade apart; the type's slot hue, scaled to one
  linear luminance (`EdgeLook.Level`, so amber doesn't bloom to white beside
  cobalt), in a fresnel rim, in the feature lines (1.25 px, a little brighter
  toward the silhouette, dimmed to 35% on triangles only a few pixels across
  so fine detail isn't a tangle of wire) and in a small core round the clip's
  bounding-sphere centre, seen wherever the view ray passes near it. `_SL_Pulse` (the beat globals both players feed)
  flares the core and lifts the lines on a living monster; a hit adds
  `shatter.core` to the lines, rim and core. Shards outline their cracks,
  hottest on the break's frame and fading over 0.6 s, and the set flashes
  `shatter.core` for the hit flash's 0.12 s. Drawn where the crystal draws
  (the transparent queue, ZWrite on, no blend): opaque on screen, outside the
  renderers' forced depth prepass. Its gains are the shaders' property
  defaults, tuned on game's and the crystal cut's stills
  ([game](../../game/evidence/look/monster-edge/README.md),
  [crystal cut](../../demo3d/evidence/look/monster-edge/README.md)): swarm
  separation 16.5-17 in game (6.5-7.6 with the crystal) and 6.7-11.5 on the
  cut's smaller monsters (provisional; 2.8-9.1 before), conformance 0.97-1.00,
  monster form 36-57, game perf at the peak 4.9 / 6.6 ms p50 / max.
- **Rings** (the impulse field, `ImpulseCrowd`): a living edge monster hops
  as a ring passes its root and its rim and lines take the ring's light
  (`_Ring`), so a slam's ring visibly runs through the swarm.
- **Bright solid emissive** (ps-wnj.11; `SolidLook`, option `solid-emissive`,
  the solid preset's; shaders `Resources/Look/Shaders/MonsterSolid` and
  `MonsterSolidShard` over `Runtime/Crowd/Shaders/MonsterSolid.hlsl`): the
  type's slot hue (`MonsterSlots`, at the palette's own value: `arcade` holds
  every enemy at L* 78 or under) as emission, shaded for form by a soft light
  from above leaning toward the camera, each facet a step apart. Separation
  comes from two dark lines:
  - **The silhouette band**: a dark band about 3 px wide (`_FalloffPx`, at 1080
    rows) just inside each body's silhouette, which the post's void toe sinks
    to black. On a body r pixels in radius a band d pixels in from the edge is
    where the view-facing term nv < sqrt(2d / r), so the band keeps its width at
    any size; it is capped at nv 0.4, because under the trailer's steep camera a
    body's sides face the view at nv near 0.5 and would go dark. Bodies large on
    screen (a radius from 50 px, fully from 100: the game's close packs, which
    interpenetrate and hide each other's outline) also darken broadly toward
    their silhouette.
  - **The outline**: an inverted hull in URP's unlit pass beside the body,
    pushed out 2.5 px at 1080 rows (along the normal bent halfway toward the
    body's centre, so the low-poly creases don't crack it), in `void.floor`.
    The style guide asked for 1-2 px; after the bloom and the metric's 3 px
    smoothing a 2 px line kept too little of its depth, and 2.5 raised game
    separation from 13.0-14.7 to 13.8-15.8.

  `_SL_Pulse` lights a small hot spot where a living body faces the camera; a
  passing impulse ring (`ImpulseCrowd`) lifts the body and lights it in its own
  hue with a touch of the ring's colour, softly capped, since stacked rings sum
  far past white; a hit brightens the faces turned to the camera in the body's
  hue, the most frontal leaning to `shatter.core`, and the silhouette band
  stays dark. Shards keep the surface and the outline, fresh inside faces a
  little brighter, and flash `shatter.core` on the impact frame, cubed over the
  hit flash's 0.12 s so the white is nearly gone two trailer frames on. Drawn
  where the crystal draws, as the edge look is. Gains are the shaders'
  property defaults, tuned on game's and the crystal cut's stills
  ([game](../../game/evidence/look/monster-solid/README.md),
  [crystal cut](../../demo3d/evidence/look/monster-solid/README.md)): swarm
  separation 14.3-20.7 in game (3.4-6.0 with the crystal) and 10.5-16.4 on the
  cut's smaller monsters (provisional at trailer distance), conformance
  0.88-0.99, monster form 23-62, game perf at the peak 5.7 / 9.2 ms probe p50 /
  p99, level with the crystal.

How the hologram is built (ps-wnj.12):

- **Wireframe hologram** (`HologramLook`, option `wireframe`, the hologram
  preset's; shaders `Resources/Look/Shaders/MonsterHologram` and
  `MonsterHologramShard` over `Runtime/Crowd/Shaders/MonsterHologram.hlsl`,
  the lines from `MonsterLines.hlsl`, which the edge look shares): bodies of
  light. The type's slot hue (levelled as the edge look's) in 1.25 px lines
  on the feature edges, a thin fresnel rim and a faint fill (ground light,
  L* about 11 face-on to 25 at the silhouette), blended premultiplied: the
  light added, what is behind dimmed by a 25% veil, so the grid shows
  through. A depth-only pass (`SRPDefaultUnlit`, the same vertex function)
  runs before the light pass, so each draw adds its front surfaces alone:
  a clump shows the near monster's lines, never every back face's wire.
  `_SL_Pulse` lifts the lines and sends a band up the body from its feet; a
  hit flashes lines and rim toward `shatter.core`; rings light and lift it
  as they do the edge look. No shadow pass. Shards are line fragments: the
  feature and crack edges, hottest on the break's frame, little fill.
- **`wireframe-all`** draws the triangulation's edges too, at 40% of a
  feature edge's light: a mesh texture, the busier option (form and swarm
  separation fail on it in game).
- Measured ([game](../../game/evidence/look/monster-hologram/README.md),
  [crystal cut](../../demo3d/evidence/look/monster-hologram/README.md)):
  conformance 0.96-0.98 in game and 0.86-0.94 on the cut, monster form
  38-49 and 35-46, swarm separation 11.7-13.5 in game and 8.2-12.4 on the
  cut (provisional: a see-through body has no dark core, and six tunings
  all landed at 11-13.7), game perf at the peak 4.1 / 5.6 ms p50 / max.

How the package implements the refractive gem (ps-wnj.13):

- **Refractive gem** (`GemLook`, option `refractive-gem`, the prism preset's
  and `MonsterLooks.StandIn`, the look of an option not built yet; shaders
  `Resources/Look/Shaders/MonsterGem` and `MonsterGemShard` over
  `Runtime/Crowd/Shaders/MonsterGem.hlsl`, using MonsterEdge.hlsl's line and
  core): clear gem showing the void and grid behind it (the camera's opaque
  texture, three taps along each facet's bend) split into the type's slot
  hue and the slot's hue turned 28 degrees either way (`GemLook.FringeDeg`),
  all levelled to one luminance as the edge look's are, so the dispersion
  stays in the slot's family. A body is mostly dark: a narrow band of
  reflected light per facet that jumps between facets as the view turns,
  brightest at grazing angles, a fresnel rim and fine pale feature lines,
  a few facets of trapped light that sparkle on `_SL_Pulse`, and a small core
  that flares on it. A hit lights the bands, rim, lines and core half
  `shatter.core` and half the slot's hue, so a crowd under constant hits keeps
  its colour. A body under about 7% of the screen's half-height (`GemFar`:
  the crystal cut's horde, not the game's) keeps a glow in its slot's hue,
  where the bands and lines would average out to a dim outline. Rings hop it
  and light its bands, rim and lines. Shards keep the gem; their fresh inside
  faces and cracks are lit, hottest on the break's frame. The gem ignores
  the crystal cut's inner glow (`_SL_InnerGlow`), so it looks the same in
  both players. The old crystal (MonsterCrystal, MonsterShard, the shaders
  the library names) is no longer the stand-in: it draws only the monsters'
  `classic` option (`MonsterLooks.Classic`), for `--look classic`'s parity
  with the look before the epic. The game's `CrowdView` turns on the
  camera's opaque texture whenever the gem or the crystal draws
  (`MonsterLooks.Refracts`); demo3d's crystal cut always has it on. Game: swarm separation 13.2-13.6
  (5.4 with the crystal), form 43-49, conformance 0.89-0.92, perf at the peak
  3.5 / 7.2 ms; the crystal cut: form 31-46 (11-25), conformance 0.71-0.84
  (0.39-0.84), separation 5.4-11.9 (provisional at trailer distance)
  ([game](../../game/evidence/look/monster-gem/README.md),
  [crystal cut](../../demo3d/evidence/look/monster-gem/README.md)).

### World

World options, all from one procedural kit:

- **Void grid**: a dark glossy floor with an emissive grid.
- **Hued zones**: the same floor and grid, tinted per shot or per zone by the
  palette's `zones`. Variety comes from hue, not from different worlds.
- **Pure void**: black floor, faint grid, nothing else.

Set dressing is crystal only: the formations (moved from demo3d's
`CrystalFormations.cs`) and new procedural geometric crystal props (shards,
obelisks, rings, low walls) with sim footprints, as formations have now.
Synty environment meshes leave the crystal cut and `game/`'s arena.

`game/`'s arena keeps its layout (`game/Content/arena-layout.json`, the
structures and footprints the sim routes round). Only how it is drawn
changes.

How the package implements the kit (ps-wnj.8), in `Runtime/Crystal/` beside
the grid floor (`Runtime/World/GridFloor.cs`, ps-wnj.7):

- **`CrystalKit`** builds every piece in code with its shards, cut as it is
  built (a `StructureShards.Set`, its `Source` the whole): `Formation`
  (demo3d's spire clusters, the same geometry and seeds), `Shard` (one leaning
  spire), `Obelisk` (a square tapering pillar), `Ring` (a half-sunk standing
  ring of diamond-section segments, an arch over its line) and `Wall` (a low
  hedge of spires). Every face is fanned from its centre with uv2.x 0 on its
  real edges, so a look draws facet edges, not triangles. `Fill` fits pieces
  to a structure's tiles: an obelisk on one tile, a shard on each of two, a
  ring or wall along a line, else a formation at the shape's middle and shards
  on the tiles beyond it; nothing stands off its tiles.
- **`StructureLook`** is the structures axis: one material in
  `CrystalProp.shader` (self-lit, `shatter.gem`, the shatter's per-piece flash,
  glow and sink), set per option: `edge-glass` (near-black glass, rim and lit
  edges flaring on the beat), `solid-glass` (bright body falling off to the
  silhouette, dark facet lines), `wireframe` (lit edges over a void fill) and
  `refractive-gem` (the void and grid seen through the facets, fainter edges;
  it turns on the camera's opaque texture). Its `Featured` material, the
  same look with faces, rim and edges lit brighter, draws the shots'
  formations: the set pieces keep the white-gold glow people loved and
  outshine the props round them.
- **`CrystalProps`** stands a structure's pieces on its tiles in a look's
  material and hands `StructureShatter` its sites and their shards.
- **`StructureShatter`** is the game's (PhysX shards in their own scene, no
  crack front, regrowth and glints), moved in and freed of the sim:
  `Break`, `Regrow`, `Glint` and `Present(song)`, with `Broke` and `Regrew`
  for the caller's moments. A site's renderers take the crystal kit's shards
  or, without them, the game's baked shards of their meshes.
- **`CrystalFormations`** is demo3d's, moved in: formations from
  `CrystalKit.Formation` in the material it is given.
- **`BlockedGround`** marks a level's impassable ground (ps-wnj.23): the
  tiles it blocks with no structure on them, whose trees, rocks and forest
  edge leave with the vendor set. Every blocked tile beside walkable ground
  (diagonals too) carries a low hedge: the longest straight runs of such
  tiles take `CrystalKit.Wall` pieces at most five tiles long, a lone tile a
  `CrystalKit.Shard`, all within their tiles; they never break, so they merge
  into one mesh per 16-tile chunk with no shards or shadows. Its `Mask` (a
  texel a tile) goes to `GridFloor.SetBlocked`: the floor's lines fade to a
  tenth over blocked ground and outside the level, and a kerb (the lines' hue
  halfway to `grid.pulse`, at L* 38 after the post in every option, over the
  lines' 12) runs along the
  walkable side of every edge with blocked ground, so the gridded floor is
  the walkable floor and its outline is lit. The hedges draw in
  `StructureLook.Terrain`: the structure look at 60% light (lit edges at
  least 0.3, under every look's props) in `grid.pulse` (the zone's hue under `hued-zones`), so the
  world's walls read with the floor, apart from the gem of what breaks and
  well under the monsters
  ([evidence](../../game/evidence/look/void-blocking/README.md)).
- **`VoidWorld`** is the air: the camera clears to `void.floor`, the
  distance fades into `void.fog` (exponential squared, gone past about
  150 m), `Hide` takes the vendor set away, and under `hued-zones` a zone
  eases the floor's lines and the fog's hue to the palette's next zone tint.
- **game**: `VoidArena` hides the Synty set under Static, the mist and the
  flames' lights under a world option, and under a structure look stands
  `CrystalProps` on each site's tiles from the build's own `arena.json`
  (the kit pieces hidden), re-rendering the static shadow map from what
  stands. Under a world option it marks the rest of `arena.json`'s blocked
  tiles with `BlockedGround` (the hedges in the structure look's terrain, or
  `edge-glass`'s without one), so the hero meets no invisible wall; the
  check is `VoidArenaTests` (every blocked tile marked under each world
  option, nothing standing on walkable ground). `StructurePresenter` turns the sim's events into the shatter's
  calls. Zones tint the void world in place of the zone grades and the
  arena's zone relighting. Without a structure look the kit's sites turn
  crystal as before (`CrystalStructures`).
- **demo3d**: each of the crystal cut's environments gets a `VoidArena`:
  the whole Synty set, mist and flame lights go under a world option, and
  under a structure look a crystal piece stands on the tiles each of its
  `Structures` pieces covered (not the landmarks, whose wide footprints
  buried the formations in crystal), on the environment's resident layer.
  The formations draw in the structure look's `Featured` and still break through demo3d's own
  `StructureShatter` (the action cut's copy, with the formation burst people
  liked); under `hued-zones` each shot takes its zone's tint on its cut.
  Since the re-staging (ps-wnj.17) every crystal-cut shot plays on the
  void's open ground (`void-layout.json`) and its one host, the forest, is
  host-only: its set hides under every world option and no structure props
  stand (The Trailer, below).

### The impulse field

One shared GPU service the grid, the crowd and the post read:

- every blow, death, shatter and beat writes an **impulse**: a ring that
  expands from a point, with strength and colour;
- the **floor grid** bends and brightens as rings pass;
- **monsters** wobble and light up as a ring passes them (the swarm reacting);
- the **post** can add a faint screen-space ripple at big impacts.

This is the main thing that ties every element to every other: a blow is seen
in the floor, the crowd and the screen at once. Stylelab's beat wave is the
precedent.

In the package (ps-wnj.7):

- **The field** is `Runtime/Impulse/ImpulseField.cs`, a ring list (not a
  texture): each ring has a centre, a start in song time, strength, speed,
  reach, width and a palette colour. Players add rings as soon as they know
  them; `Publish(song)` hands shaders the 32 strongest alive at that time as
  globals (`Impulse.hlsl`: `_ImpulseRing`, `_ImpulseColor`, `_ImpulseCount`),
  so a ring sets out on the first frame that reaches its time and fixed-time
  frames reproduce. Same-instant rings of one kind within 3 m merge (a mass
  kill is a few strong rings, not hundreds).
- **Ring shapes** (`ImpulseField.Blow`, `Death`, `Shatter`, `Beat`) follow the
  style guide's pace: across a 35 m frame in about a beat, gone in about one
  and a half. A light strike's ring is faint, a heavy one strong, the slam's
  and a formation smash's strongest; a death's is a small ring in its gem;
  a break's a big one in `shatter.gem`.
- **The floor** is `Runtime/World/GridFloor.cs` and `GridFloor.shader`: a
  240 m patch kept under the camera's view, `void.floor` with a 1.5 m grid of
  `grid.line`, every fourth line twice as bright, lines thinner than a pixel
  dimming and fading out where a cell is under ~4 px (no moiré), and no scene
  fog (the arenas' night fog lifted the void). Rings shove
  the lines outward from their centres (the warp), lift the floor under the
  front and light the lines in `grid.pulse` and the ring's colour. On the beat
  the lines alone breathe up (`_SL_Pulse`, `_SL_BarPulse`). Every world option
  draws it: `void-grid` as set, `pure-void` with a faint grid, `hued-zones`
  with the lines in the first zone's hue (`SetZoneTint`). It replaces the
  arena's ground and its light decals (`Runes`, `GlowPools`); the rest of
  each Synty set stays until the world kit (ps-wnj.8). ps-wnj.24 made it a
  quiet background: its lines' level, widths, fade and ring knee are in
  [Restraint and special moments](#restraint-and-special-moments).
- **Beats**: the floor breathes floor-wide, so the players write no beat
  rings (the style guide: a breath is a ring from the hero or a floor-wide
  lift, never both). `ImpulseField.Beat` is there for a look that wants the
  ring instead.
- **Crowd hook**: `ImpulseCrowd(rootXZ, out hop, out light)` in
  `Impulse.hlsl`, for the monster looks to wobble and light up as a ring
  passes. No look calls it yet.
- **Post hook**: `_ImpulseStrongest` (centre, radius, strength now) is the
  strongest ring alive, for the post stack's screen ripple at big impacts.
- **The game** feeds it from `EffectsPresenter`'s cues (strikes, deaths,
  structure breaks, not regrowths) and marks each ring as an `Impulse` moment
  for `game beat`. **demo3d's crystal cut** loads each shot's rings at its cut
  (`ShotImpulses`: the hero's blows weighted by `BlowFeel`, the shattering
  deaths, the `BreakMoments`). The plain action cut has neither.

Evidence is in
[game/evidence/look/impulse](../../game/evidence/look/impulse/README.md).

### Post-processing stack

One stack built from the preset, replacing both projects' `NightLook` volumes
and the trailer's `TrailerLook` dressing. Options per stage:

- **Tonemap**: neutral, ACES, or a filmic curve that keeps saturated
  emitters from going white.
- **Bloom**: tiers by threshold, intensity and scatter; a wide soft halo
  layer for the neon read.
- **Colour**: the palette's grade; no per-shot grades that pull shots apart.
- **Lens**: chromatic aberration, vignette, grain, dirt, lens hits.
- **Retro**: optional scanlines and a CRT-ish soft mask.
- **Feel**: beat pulses and impact flashes, kept from today's `PostFeel`,
  reading colours from the palette.

The per-shot `TrailerGrade` goes away: every shot of a cut is graded by its
preset, so cuts no longer jump between looks.

How the package implements it (ps-wnj.6):

- **An option is a file**, `Runtime/Resources/Look/Post/<name>.json`
  (`PostOption`): `tonemap` (`neutral`, `aces` or `filmic`, with the filmic
  curve's `shoulder` and `whiten`), `void` (a toe's `knee` and `power`),
  `colour`, `bloom`, `halo`, `lens`
  (chromatic, vignette, grain, dirt), `retro` (scanlines, mask) and `feel`
  (the beat's exposure, bloom, vignette and bloom tint; the flash's exposure,
  aberration and saturation; the hero's hits that flash). Left-out values are
  off; an unknown key fails. The four built: `neon-bloom` (edge), `punchy-bloom`
  (solid), `scanlines` (hologram), `prism-bloom` (prism).
- **The look's scene pass** (`LookPostPass`, `Resources/Look/Shaders/LookPost.shader`)
  runs before URP's post on every game camera that has post on, enqueued by
  `PostStack.Use`, on the scene's light: the exposure (the option's plus
  `PostStack.SceneExposure`, a dim world's own: game's baked arena sets it),
  the void's toe (light under the knee, linear, of the brightest channel,
  sinks toward black by the power, the other channels scaled alike, so a lit
  floor falls to the void while emitters over the knee keep their value), the
  halo (a soft-knee threshold, the glow's saturation raised, a
  dual-filter pyramid from about 540 rows down six levels and back up), and
  the filmic curve (the brightest channel rolls off past the shoulder with the
  others scaled alike, so a saturated emitter keeps its hue; a core past white
  leans white, fully at 8x). A camera without post (an ID render) is left
  alone.
- **URP's half** is a volume profile (`PostStack.CreateProfile`) on what that
  pass drew: colour adjustments, bloom as the tight core layer with the lens
  dirt (`LensDirt`, moved in) on it, vignette toward `void.floor`, chromatic
  aberration, film grain (raised by a supersampling factor) and zeroed
  exposure and white balance. URP grades in LDR, as before: both players'
  builds strip the uber shader's HDR grading variants, so the tonemap can't
  move after URP's grade. Its tonemapper is off under `filmic`, URP's own
  under `neutral` and `aces`. The post feel writes its offsets over the
  profile's values through `PostBinding` (both projects' `PostFeel`s, whose
  volume bindings were forks), which also leans the bloom's tint toward
  `grid.pulse` on a beat and `spark` on a flash.
- **The retro pass**, after URP's post, draws the scanlines (360 to a frame's
  height) and the soft CRT mask, for an option with them.
- **Lens hits** stay demo3d's `TrailerLook` (its hits come from the
  choreography); `LensHitFeature` and its shader moved in.
- **demo3d's crystal cut** puts the stack's profile on every environment's
  night volume, with no shot, section or style grade and no `TrailerLook`
  grain and dirt; the plain action cut and the song keep `NightLook`,
  `TrailerGrade` and the trailer dressing unchanged. **game** builds its
  arena's volume from the stack and keeps `NightLook` for fog and ambient
  only; its zone grades still ride on top until the void world's zones.
- An option not built draws today's post in demo3d and
  `PostOption.Plain` in game, which has no post of its own left.

### VFX

All effects run on the moved compute particle runtime and take colours from
palette roles. Old vendor effects (the Hovl sword slash in
`game/Content/effects.json`) are replaced by procedural ones.

- **Slashes and spins**: ribbon trails of light along the blade's arc, white
  at the core into the hero's trail colour.
- **Light streaks**: short additive streaks behind anything fast: projectiles,
  lunging monsters, flying shards.
- **Death flourish**: on the impact frame, a light pop and an expanding ring
  on top of the shatter, writing an impulse.
- **Spawn warp**: monsters warp in with converging lines or an implosion
  before they step; it must not add build-up to a death.
- **Sparks**: crisp, short-lived, palette `spark`.
- **Structure breaks**: the formation shatter's burst, flash and debris in
  the preset's look, still bursting on the impact frame (the operator's rule:
  no crack-then-burst, no twinkling).

The runtime in the package (ps-wnj.5): `Runtime/Effects/` (`EffectSystem`,
`EffectLibrary`, `EffectLights`, the sim, sort and draw shaders) and
`Editor/Effects/EffectConverter.cs`, game's fork with demo3d's merge and
glow cuts and overdraw pass; both forks are gone, and each project bakes its
own selection with it. A selection colour may name a palette role
(`"edge": "shatter.gem"`, `"color": "boss.telegraph"`, an enemy slot
`"enemies.2"`), which `EffectSystem` draws in the applied look's palette
(`ApplyPalette`, at construction). Today's effects keep literal colours, so
they look as before in every preset until the VFX beads restyle them
([effects.md, The look](../tech-demo-3d/effects.md#the-look)).

The vfx axis's options (ps-wnj.15): **`light-streaks`** (edge, solid,
hologram) and **`prism-streaks`** (prism). Each project's selection names the
effects an option draws in place of the vendor ones (its `vfx` table,
[effects.md, Vfx variants](../tech-demo-3d/effects.md#the-look)), built from
the runtime's two new procedural kinds, arcs and light streaks, plus the
spark bursts:

- **Slashes and spins** are ribbons of light on the blade's arc (`arcs`): the
  head sweeps the arc in the first third of the life and the light trails
  behind it, narrowing, white at the core into `hero.trail`, with a thinner
  echo inside it. The game draws them where it drew the vendor crescent (its
  middle at -135 degrees, sweeping clockwise, turned onto each clip's arc and
  mirrored for a cut the other way); demo3d centres them on the blow and
  mirrors the right-to-left cuts (slashL, slash3). A spin sweeps a little past
  a full turn; demo3d's thrust is a lance of light streaking forward.
- **The slam** is a thin ring of light flat on the ground racing out to the
  reach (`grow`), a brief inner flash and sparks thrown up off it; demo3d's
  formation smashes take it too.
- **Light streaks and smears**: the crystal burst's chips are drawn as short
  streaks and a velocity smear shoots along the blow; a hit's chips streak
  with a smear (game) or are crisp short sparks white-hot into `hero.trail`
  (demo3d); demo3d's bolt is a glowing head with a streak behind it
  (`streaks`) and a monster's lunge streaks forward faintly in `grid.pulse`
  (the dust puff's place). The game's bolts already streak (WandView's
  afterimages).
- **`prism-streaks`** splits the cuts, the ring and demo3d's bolt into three
  softer layers in `enemies[0]`, `[2]` and `[4]` (the palette's key, accent
  and counter families), each a little inside and behind the last: dispersion
  in the palette's hues. Its bursts and sparks are `light-streaks'`.

**Restraint (ps-wnj.25, the operator's 2026-11-01 direction).** Every one of
these is modest every day: one thin ribbon at about half the brightness, a
single faint smear, dimmer chips and sparks, half the light. The bright
versions (a wider ribbon with a white core and its echo, the full smear,
twice the light) are each entry's `-big`, drawn only at a **special moment**
through the `vfx` table's `NAME@special` keys:

- the game: a heavy strike's slash or spin, the sword's slam (always), and a
  mass kill's bursts (8 or more deaths at one instant, `EffectsPresenter.MassKill`);
- demo3d's crystal cut: every slam's shock (the shots' cuts and the finale),
  a formation smash, and a heavy blow (its blow feel's weight 0.75 or more,
  `EffectDriver.HeavyBlow`).

ps-wnj.24 owns the special-moment tier for the rest of the look (the grid,
the structure flashes, the post stack); these are the VFX's moments.

Strikes still land on their frames: every variant spawns at its effect's own
time. Evidence: [game/evidence/look/vfx](../../game/evidence/look/vfx/README.md)
and [demo3d/evidence/look/vfx](../../demo3d/evidence/look/vfx/README.md).

The death flourish and the spawn warp (ps-wnj.16; the package's
`Effects/Flourishes.cs`, the selections' `death-pop`, `death-ring` and
`spawn-warp`) come with either vfx option and only with one: `--vfx classic`
and the plain action cut draw and sound neither. Both are tinted per spawn by the
monster's gem and sized by its body, and kept faint (the operator's restraint):

- **The death flourish**: on the death's impact frame, a soft pop a body wide on
  the body, gone in 0.1 s, and a thin ring of light flat on the ground racing out
  to 1.6 m (times the body) in 0.4 s, over the shatter and the death's impulse
  ring. The deaths of one instant within 3 m share one ring at their centre, its
  size their mean times the square root of their count (up to 2.5): a mass kill
  is a few larger rings, as its impulse rings merge, not a ring per body. Every
  body keeps its own pop.
- **The spawn warp**: from a monster's spawn, two thin rings on the ground
  collapse onto it faster and faster (`"ease": "in"` on an arc's grow) and a
  soft glow pops where they meet 0.14 s in, well before its first step (the
  next beat). It starts on the spawn's own frame (events settle only a lead
  ahead, too late to start it earlier), so the body stands from its spawn as
  the sim has it. Only spawns in view warp: the game's by the spawner's own
  footprint test (`Simulation.InView`; the ring of spawns past the view's edge
  never does, its set pieces' and swarms' do), the crystal cut's by the
  camera's ground footprint round the framing's focus at the spawn, turned by
  the shot's heading, after the shot's cut (`LookCrowd.Warps`). The
  choreography spawns every monster outside the view and walks it in, so the
  crystal cut has no warps until a re-staged shot spawns in view.
- **Sounds** (Decision 13), from the free samples (Kenney, CC0), each at its
  event's time and aggregated per beat as the deaths' (at most three voices per
  beat): a short bright pluck for each death's pop (`death-pop`: drop_001,
  pluck_001, pluck_002, pitched by the body), and a rising swell for each warp
  (`spawn-warp`: maximize_001 to 003), its attack on the spawn and its peak as
  the rings meet. game's in `game/Content/sounds.json`; demo3d's
  `FlourishSounds` (setup clones the free samples into `demo3d/Assets/FreeSounds/`
  too, and the offline mix finds them in `public/assets/sfx/`).

### Motion

- **Spring secondary motion**: per-instance squash and stretch, lean into
  travel, and overshoot-and-settle on every step, lunge and hit, on the GPU
  over the baked clips. The hero's hops get the same.
- **Swarm individuality**: jittered spacing, phase-offset idles, slight scale
  and tilt variance, a flocking wobble between steps. Beat steps still land
  on the beat's frame.
- **Beat ripple** (flag only): each monster's step delayed by its distance
  from the hero, up to ~1/16 note.
- **Trails and smears**: afterimage ribbons on the hero and fast monsters,
  velocity smears on blows.

How the package implements it (ps-wnj.14; `CrowdMotion`, the layers
`springs`, `swarm` and `beatRipple` in `Axes/Motion.cs`; evidence in
[game/evidence/look/motion](../../game/evidence/look/motion/README.md)):

- **The clocks.** Beside each type's instances a feeder writes each monster's
  `MonsterMotion` (`BoneTextureCrowd.Motion`, 48 bytes a slot): seconds from
  its latest step's plant (negative while the move runs before it), the move's
  length before the plant, the step's travel in the monster's own heights, the
  same for its latest kick (a lunge from its notice to its contact, a hit, a
  knock) with the kick's way times its strength, and a seed per life. The
  game's `MonsterTrack` keeps them from the sim's events (its steps move in
  the 100 ms before the beat); demo3d's `LookCrowd.MotionOf` reads them off
  the timeline (a gait step moves after its beat). A type with no clocks in a
  frame draws without the layers.
- **Springs** are closed-form damped springs in `SkinVertex`
  (CrowdCommon.hlsl's `CrowdMotion`), so every look, pass and LOD springs
  alike and fixed-time frames reproduce. Only the body's shape moves: a
  squash or stretch about the feet and a lean, a shear by height above them;
  the feet stay where the feeder put them, so a step plants on its beat's
  frame. A step stretches (up to 7% of the height) and leans into its travel
  (a shear of 0.2 at full size, a travel of 0.3 of the height) while it moves,
  drops into a 13% squash on the plant's own frame, rebounds and settles
  within about 0.4 s, the lean overshooting back; where the move follows the
  plant (the trailer) the plant kicks the lean instead. A lunge crouches and
  leans back through the first 60% of its wind-up, reaches into its contact
  (a shear of 0.3) and springs back; a hit (0.7 of a lunge) or a knock throws
  the body back on its own frame and squashes it, then settles. Dying bodies
  and shards take none (the shatter breaks on the impact frame as before).
- **Swarm**: each life is placed up to 0.2 m (times its cast scale) off the
  sim's spot, turned up to 0.12 rad off its facing and sized within 10% of its
  cast scale, on the CPU (`CrowdMotion.Place`) so the shatter seeds where the
  body drew; on the GPU it stands with a slight tilt of its own (a shear up to
  0.08) and wobbles slowly (0.45-0.8 Hz, a 0.025 lean and a 1.5% breath), each
  at its own phase. Idles were already phase-offset per monster. Separation on
  the horde feed's dense clumps: solid 13.4 to 13.7 mean (each still now
  passing 13), edge unchanged at 9.0 (its outlines carry it); game perf at the peak 3.5 / 6.0 ms probe
  p50 / p99.
- **The hero** takes the same step spring on its hops and dashes, on the CPU
  (`CrowdMotion.StepSpring`): game's `HeroMotion` and `HeroRig` scale and tilt
  the knight, demo3d's crystal cut its `StockCrowd` hero (`DemoMain.HeroSpring`;
  the plain action cut never sets it).
- **The ripple** (`--beat-ripple`, off in every preset) delays each step by
  its plant's distance from the hero, up to a sixteenth note (a quarter beat)
  at 24 m (`CrowdMotion.RippleDelay`): the game shifts the step event's times
  (the beat from the stride grid), demo3d evaluates a rippled monster's place,
  facing and walk that much earlier (the beat its gait step's length). Its
  actions, hits and breaks stay on time. With it on, `game beat` reports the
  delayed footfalls as off their frames, as it should.
- **Restrained by default** (the operator, 2026-11-01: the neon visuals
  overwhelmed; subtle secondary motion, flourishes for special moments):
  a louder first tuning (a 20% squash, a 0.3 lean) read better in strips but
  is not the default.
- `--motion LAYERS` (or `none`) sets the layers in place of the preset's, for
  before-and-after captures from one build. Every preset turns on springs and
  swarm; trails belong to the trails bead.

### Hero

The hero is the brightest single thing on screen in every preset, in the
palette's `hero` role, readable at the trailer's 32–42 m framing without
growing (ps-b5z.1's rule) or an outline (removed on the operator's review).
The crystal blade's flare stays. Each preset picks a hero treatment
matching its monsters.

How the package implements it (ps-wnj.9; evidence in
[game/evidence/look/hero](../../game/evidence/look/hero/README.md) and
[demo3d/evidence/look/hero](../../demo3d/evidence/look/hero/README.md)):

- **Four hero looks**, one per preset and named after it (`HeroLooks`,
  options `edge`, `solid`, `hologram`, `prism` in `Axes/Hero.cs`; `--hero-look`
  mixes one into any preset). One shader each,
  `Resources/Look/Shaders/HeroEdge`, `HeroSolid`, `HeroHologram` and `HeroGem`,
  over `Runtime/Hero/Shaders/HeroLook.hlsl`, draws both the knight and the
  blade, in `hero.body` (the blade `hero.blade`, `hero.trail` as a faint
  second colour):
  - **edge**: smoked glass a little lit, a fresnel rim and lines on the
    knight's feature edges, and a glowing core in its chest seen through
    the glass: a cut-crystal knight of white lines.
  - **solid**: self-lit `hero.body` shaded for form by a soft light from
    above, each facet a step apart, a hot spot facing the camera. No
    outline: it wins on value.
  - **hologram**: light added over the world (premultiplied, after a
    depth-only pass, as the monsters' hologram): lines, a rim and a fill far
    denser than a monster's, and a band rising up the body on the beat.
  - **prism**: a white gem: the void behind bent through each facet, light
    trapped in every facet, a band of reflected light per facet, fine
    lines, a few facets sparkling on the beat. The game turns on the
    camera's opaque texture for it (`HeroLook.Refracts`).
- **The knight's faceted meshes** come from the hero bake, since the vendor
  meshes aren't readable in a player: the package's `HeroFacetBaker` unwelds
  each of the knight's renderers' meshes as the crowd's style mesh
  (barycentrics in UV3, feature edges in UV5, bone weights and bind poses
  kept) and tags the renderer (`HeroFacets`). Game's `HeroBaker` and demo3d's
  `RosterBaker` (its hero) call it, into their gitignored Baked folders.
  `HeroLook.Dress` swaps the faceted mesh and the look's material in;
  classic (or a bake without facets) leaves the Synty knight.
- **The blade** takes the look's blade material in both players' `HeroBlade`
  (every facet edged, the spine lit at rest, the blow flare `_BladeFlare`
  over the whole blade past white, as before) and a motion-vector twin.
- **Levelled across the players.** Game's baked arena adds 0.8 stops of
  scene exposure (`PostStack.SceneExposure`) and demo3d's crystal cut none,
  so the same light read far darker on the cut. `PostStack` publishes the
  scene's exposure factor in `_LookToe.w` and the hero takes it out
  (`HeroLevel`, against the 0.8 stops it was tuned under).
- **Readability, judged** by the style guide's `hero_margin_min` (the hero's
  L* p90 at least 5 over the monsters') on dense stills at the trailer's
  framing (game at `--view-width 36`, the cut's 36-44 m shots), by the
  hero's own pixels (on the hero rung, L* p90 96-99.7, not a white blob),
  and by eye on crops. Game's stills now write the view json the margin
  needs (`Stills.HeroFeet`; `"floor": false`, since its ID render marks
  only the crowd). Margins: game 8.9-33 in the clean horde, 6-37 with
  evolved weapons (classic: -11 to 28, failing twice); the crystal cut 24-44 for edge,
  hologram and prism and 6-12 for solid (classic: -28 to 8.5). The cut's
  solid monsters reach L* p90 about 95, over the enemy rung, so the solid
  hero clears them only from white (its hot spot 0.3; ps-wnj.19 owns the
  monsters' side).
- `--hero-tune key=value,...` sets the hero shaders' float properties for a
  run (`body.` or `blade.` for one), to judge a change without a rebuild
  (`MaterialTune`, as `--monster-tune` does).

### Restraint and special moments

The operator on the first neon stills (2026-11-01): too overwhelming, "the
grid in the arena background kind of drowning everything out and the super
bright flashes on the structural crystals"; tone everything down and "save
these super flashy effects for special moments". So the look is **calm by
default**, with a contrast hierarchy of hero and blade first, enemies
second, the world last, and its loudest effects fire only in a **special
moment** (ps-wnj.24).

**The special moments**, the only ones that get the full flash:

- a **structure break** or a **formation smash** (every break the game's sim
  makes; every `BreakMoments` break of the crystal cut);
- a **slam or finisher**: a blow of weight 0.95 or more (game's finisher
  slam; the crystal cut's heaviest blows, its formation smashes among them);
- a **drop** (game's loudness jump), and the crystal cut's designed **flash
  cuts** (its transitions, unchanged).

Everything else is calm: light and heavy strikes, spins, deaths (a mass
kill too), accents, anchors, glints on structures, beats.

**How the tier is built**:

- **Rings say whether they are special.** `Impulse.Special`:
  `ImpulseField.Shatter` always, `ImpulseField.Blow` at a weight of
  `SpecialWeight` (0.95) or more, never a death or a beat. A calm ring is
  drawn at `CalmRing` (40%) of its strength (merging and `Strongest` keep
  its own), so the floor and the swarm react to calm blows only faintly.
- **`_LookSpecial`**: `ImpulseField.Publish` hands shaders an envelope of
  the special rings alive, 1 on a special moment's first frame, falling as
  (1 - u)² over `SpecialSeconds` (0.5 s). `LookFlash(calm)` in Impulse.hlsl
  is a flash's gain: `calm` outside special moments, 1 in one. Every look
  shader's flash goes through it: the monsters' hit flash (calm 0.35) and
  their shards' break flash (calm 0.5), the crystal props' hit flash and
  break glow (`_FlashCalm`, 0.3). So in a special moment everything on
  screen flashes in full together, and outside one nothing does.
- **The post's flash** (`feel.flash.calm`, 0.3 in every option): a calm
  moment's exposure, aberration and saturation spike is that share of its
  own. Game's `FeelMoments` treats breaks, finishers and drops as special;
  demo3d's `PostFeel` its structure breaks (accents, anchors and hits calm).
  The night feel (`--look classic`) and the plain action cut keep every flash
  (calm 1).

**What is calmer everywhere**, special moment or not:

- **The grid floor** is a quiet background: lines in `grid.line`'s hue
  that land at one L* after the post in every palette, zone and preset
  (`_LineL` 12, `pure-void` half that), thinner (10 mm minor, 20 mm major
  half-widths, were 12 and 25), the minor lines 35% of the major (were
  40%), fading to 30% between 30 and 110 m from the camera; the beat's
  breath under a fifth of before (0.06). The post's void toe is steep and
  works on a pixel's brightest channel, so a gain or a luminance can't level
  the lines: halving their light took edge's grid from L* 27 to 2.4, and one
  linear luminance put edge at 16, solid at 13 and prism at 2. So
  `PostStack` publishes the scene pass's toe and exposure (`_LookToe`) and
  the floor sets each line's colour back through them (`Levelled` in
  GridFloor.shader). A ring lights and shoves the lines only past a knee
  (`_RingKnee`, 0.25 of its drawn strength), so only big impacts (special
  ones, a mass kill) visibly bend it, at 1.2 (`_RingGain`, was 2.2) and
  with a 0.7 m warp.
- **The crystal props**: resting edges about half as bright (edge-glass
  0.6, wireframe 0.55, gem 0.35, solid-glass's body 0.4), the beat's flare
  on their edges about a third, the formations' lift over them smaller
  (faces 3x, at least 0.35 of the gem); a hit's flash and a break's glow
  peak at about two thirds of before even in a special moment.
- **The monsters**: the beat's lifts (lines, core, sparkle, band, hot spot)
  about half, the ring light 1.1 (was 1.5; solid's impulse light 0.45, was
  0.6), all as the shaders' property defaults.
- **The post**: bloom and halo intensities at 70% in `neon-bloom` and
  `punchy-bloom`, cut only 10-15% in `scanlines` and `prism-bloom` (thin
  emitters: at 70% the crystal cut's sparse shots fell under the
  bright-share floor), the beat's exposure, bloom and tint pulses about a
  third, the full flash's exposure 0.3, aberration 0.3 and saturation 15
  (were 0.5, 0.5, 25).

**The halo flares in a special moment** (ps-wnj.19): a post option's
`halo.special` grows its halo by that share of its own with `_LookSpecial`
(`LookPostPass`): 1.5 in `neon-bloom`, `scanlines` and `prism-bloom`, 7 in
`punchy-bloom`, whose solid bodies sit mostly under the halo's threshold.
Steady frames keep the calm halo, so the style guide's halo rule is an
impact-frame rule (`halo_impact_min`): at five times the calm halo the
crystal cut's steady stills still measured only 6-10 against 15 while the
grid's floor p99 broke, so a steady glow and a quiet floor can't both hold.

The style guide's [Restraint](neon-look-style.md#restraint) rules check it:
the grid's lines (floor L* p99), the background's share of the frame's
light and the share of steady stills that flash. Evidence:
[game](../../game/evidence/look/restraint/README.md),
[crystal cut](../../demo3d/evidence/look/restraint/README.md).

## The Trailer

The crystal cut keeps its shape (trailer.md, **The crystal cut**): *Action*,
~60 s, the drop's hook, three long shots, quickening cuts, formations
shattering in most shots, the dragon finale.

What changes:

- **Shots move into the void world.** Each shot's Synty environment becomes a
  zone of the kit. Hordes, formations, the hero's path and framing are
  re-staged for it; the checks (open lane, every formation breaks on screen,
  liveness) still pass.
- **Designed transitions.** Cuts become palette flashes, whips or impulse
  wipes on the beat, all in one grammar.
- **One camera grammar.** The same framing rules and moves across shots.
- **The dragon** draws in each preset's monster look, its telegraphs in the
  palette's `boss.telegraph`.

The trailer's rules hold (no idle crowds, blows on major beats, the hero
never surrounded, no UI or text, the game's own camera). A rule that blocks
the redesign may change: say so in trailer.md and update its check.

What shipped (ps-wnj.17; trailer.md, The Crystal Cut):

- **The void staging.** A trailer shot's new `layout` plays its simulation on
  a layout of its own: every crystal-cut shot plays on `void-layout.json`,
  open ground where only its formations block, so the horde never parts round
  anything the void doesn't draw (on the Synty layouts it parted round hidden
  trees and walls). Every shot is hosted by the forest's scene, whose whole set
  hides; a shot's `zone` names its zone (the old places neon, crypt, lava and
  forest become zones 0 to 3, each twice). Each shot was turned by its old
  heading and centred on the void, so it frames as before, and the fifteen
  formations smash at the old cut's moments.
- **Transitions**, all on the beat in the incoming zone's colour: a section's
  entry (the five accent cuts) is a palette flash, the frame flooding with
  the zone's hue through the colour filter with a modest exposure and bloom
  lift (the run's restraint: never the brightest moment), plus a strong
  impulse wipe, a fast wide ring from the hero across the floor
  (`ImpulseField.Wipe`); a cut within a section is a soft wipe alone. No whips:
  a camera move the game never makes.
- **One camera grammar**, checked (`one camera grammar`): one heading (yaw 0,
  the grid square to the frame in every shot), one framing per shot from a
  ladder by role (hook 20 m, long shots 42 to 44 m, quickening 38 to 36 m,
  finale 32 m), no zoom within a shot.
- The per-shot grades went with the environments they balanced. Evidence:
  [demo3d/evidence/look/restage](../../demo3d/evidence/look/restage/README.md).

**The neon action cut** (ps-wnj.26; trailer.md, The Neon Action Cut): the
action cut on the package, for the operator's request of 2026-11-01. It is a
trailer file of its own, `trailer-action-neon.json`, which `--trailer` plays
in place of `trailer-action.json` whenever a look flag is given (and
`--trailer action-neon` names directly). A separate file, because the void
staging changes every shot's ground, path, heading and framing; one file with
per-look overrides would have doubled every shot's fields. It is the action
cut re-staged the crystal cut's way: the same shots, beats, combos, spells,
duel, formations and dragon fight, each shot turned by its old heading into
yaw 0 and centred on the void, the four zones each twice, one framing per
shot (20, 44, 47, 42, 36, 34, 36, 34 m: the action cut's own widths, held
for the whole shot), the crystal cut's look block, and its transitions,
impulse field, special-moment tier and every look axis. It has no crystal
formations: the crystal cut is the formations' showcase, this the action
cut's horde and dragon. A test holds it to the action cut shot by shot
(`TheNeonActionCutIsTheActionCutInTheVoid`). Capture all four presets with
`just trailer-looks action`; evidence in
[demo3d/evidence/look/action-neon](../../demo3d/evidence/look/action-neon/README.md).

What shipped (ps-wnj.19): the cut in all four presets, `just trailer-looks`
(Flags), and the style guide's metrics on a still from every shot of every
preset, beside v2
([demo3d/evidence/look/trailer](../../demo3d/evidence/look/trailer/README.md)).
What the stills broke, and where it was fixed:

- **Halo** (every still of every preset, 0.3-12 against 15): the halo now
  flares in a special moment and the rule judges impact frames
  (Restraint and special moments); the impact frame measures 16-26.
- **The lines in a capture were half as wide.** The edge, hologram and gem
  looks' lines (and the hero's) were pixels of the frame drawn, which a 2x
  capture downsamples to half a pixel; they are now pixels at 1080 rows of
  it (`EdgeLine`, `_ScaledScreenParams`), so a capture draws what the live
  run and the game do.
- **Prism's key**: the lens's chromatic aberration (0.2) sprayed rainbow
  fringes over the frame's edges, breaking the key (0.29-0.50), families
  and palette conformance; at 0.04 its key is 0.38-0.92 (median 0.63) and
  conformance 0.88-1.
- **Bright share** in hologram's and prism's sparser shots (ps-wnj.24's
  open question): their monsters' resting light rose (hologram lines 3.0
  and rim 2.0, were 2.4 and 1.6; the gem's shine, rim and lines 1.5, 1.7,
  1.0, were 1.1, 1.3, 0.7), not the floor; both now clear 0.04 in every
  shot but the duel.
- **Solid's swarm separation**: its outline is 3.5 px (was 2.5); seven
  stills broke the rule, three do.
- **The spells drew vendor effects**: the cut's ring and lightning spells
  kept their Hovl prefabs under every vfx option (the lightning's magic
  circles broke edge's floor p99 at 50 s); each option now maps them to its
  slam ring and hit sparks.

Still breaking, filed as ps-wnj.28 (separation) and ps-wnj.29 (the rest): swarm separation (edge
in ten of eleven steady stills, where a finely cut type's lines fill its
body at the cut's distance; hologram and prism in five to seven), the key in shot 3
(20 s, every preset: three enemy families in one frame) and a few others,
bright share in the duel (1.6 s: three elites alone in the void), and
prism's floor p99 under its slam rings and formation shards (12 and 36 s).

## The Game

`game/` takes every package piece in place of its forks, and runs every
preset:

- `--look` and the axis flags on the player and `just game`;
- zones' looks become the preset's `zones` hues;
- `game perf` runs its standard scenarios under each preset, and every one
  holds the budget;
- the beat audit (`game beat`) still passes: nothing the package draws lands
  off its frame;
- free mode's heard-time strikes still show their blows and deaths when the
  sim resolves them.

Windows (Cygnus) comes after the Mac, as in ps-msz: an operator-pending row
checks the package's shaders build and run there.

**Shipped (ps-wnj.18)**, in
[game/evidence/look/presets](../../game/evidence/look/presets/README.md):
`game perf --looks edge,solid,hologram,prism` holds the budget in every
scenario of every preset, with the build's warm-up traced in all four;
`game beat` finds every moment on its frame at the peak and in free mode in
each, and `--walk-lead` plays free presses that resolve in heard time (their
blows and kills drawn when they arrive); zones ease the floor and fog
through the palette's four zone hues (the run's fifth zone, lava, returns to
the first). The edge knight's lines widened to 1.8 px for the game's framing.
The game's stills pass every rule but `halo_min` and
`swarm_separation_min` (and hologram's background light once), the post's
and the monster looks' rules ps-wnj.19 tunes for both players.

## Checks and Evidence

### The style guide

[neon-look-style.md](neon-look-style.md) (ps-wnj.1) holds:

- a study of Geometry Wars 3 (and any other references it finds useful):
  value structure, how light is used, hue budget, how it keeps 100s of
  entities readable, its grid and its effects;
- the palette roles' real values for each palette;
- the rules every axis follows, each with a measurable form where possible,
  such as "floor p50 L* under 8", "at most 2% of pixels clipped outside impact
  frames", "at most four hue families plus white in a frame".

### Look metrics

`demo3d/tools/src/demo3d_tools/look.py` already measures L*, chroma and
readability. `look-metrics DIR --palette P.json` (ps-wnj.2, ps-wnj.21) adds
every pixel rule of the style guide's
[Rules and Thresholds](neon-look-style.md#rules-and-thresholds), under the
guide's own keys:

- **Value structure**: the floor's L* p50 and p90 (from the ID render and the
  view json; the frame's L* p25 on a video frame), the near-black, bright,
  clipped and white shares, the largest clipped region.
- **Light and hue**: the halo round emitters, the colour key (the best
  60-degree window) and the hue families, palette conformance.
- **Form**: the hero's margin over the monsters, monster form (a body's own L*
  span, per 32 px tile) and readability.
- **Swarm separation**: in dense crowd regions, the contrast along boundaries
  between adjacent monsters of one type, in L*: in each 32 px tile that one
  enemy hue mostly covers, how far its seams sit under the bodies either side
  of them.
- **Per cut**: the key's median and the spread of the shots' floors.

It runs over a directory of stills: demo3d's or game's windowless stills,
with their ID renders and view json where the player wrote them, or frames
cut from a capture. It works under both `demo3d` and `game`, takes
`--preset` (else the palette's pairing) and `--impact` for impact frames, and
exits 1 when a still or the cut breaks a rule. The rules are in
`docs/design/neon-look-thresholds.json`.

Calibration on v2 is in
[demo3d/evidence/look/metrics](../../demo3d/evidence/look/metrics/README.md),
on its video frames and on the crystal cut re-rendered with ID renders. Every
v2 frame breaks rules: the key in 13 of 15 frames, swarm separation
in the blob frames, monster form in all of them, and the lit floors break the
value rules.

### Evidence per bead

Every bead leaves evidence under `demo3d/evidence/look/` or
`game/evidence/look/`: windowless stills and filmstrips per preset (or per
option of its axis), before-and-after against v2 where it applies, and the
metrics. Agents judge from these; nothing they run opens a window.

### Operator checks

Operator checks never gate a bead. Rows go to
`game/evidence/operator-pending.md`:

- the four whole-trailer captures, one per preset;
- a free-mode playlist run in each preset;
- the Windows run.

## Build Order

A thin foundation, then wide parallel tracks. A bead depends on another only
when it needs that bead's output (an API, a data format, moved code), never
merely because both touch the same files: `enact push` rebases, and
executors resolve conflicts.

**Wave 0, all at once:**

- **Style guide and palettes** (`neon-look-style.md`, palette JSON values).
- **Look metrics** in the tools (the palette format above is enough to
  start).
- **Package skeleton**: the package, both manifests, palette and preset
  loading, the flags, a placeholder palette and the four preset files.

**Wave 1, after the skeleton:**

- **Crowd and monster shatter into the package**, both forks deleted, both
  projects drawing through it (today's crystal look as the stand-in).
- **Effects runtime into the package**, forks deleted, colours from roles.
- **Post stack** from presets, replacing both `NightLook`s, `TrailerLook`'s
  dressing and per-shot grades.
- **Impulse field** and the reactive grid floor.
- **World kit**: void floor, zones, formations and structure shatter moved
  in, procedural crystal props.
- **Hero treatments** and the blade.

**Wave 2, after the crowd move (and the effects move for VFX):**

- **Dark core, lit edges** (with the baker's feature-edge data).
- **Bright solid emissive.**
- **Wireframe hologram.**
- **Refractive gem**, replacing the old crystal shaders.
- **Spring secondary motion, swarm individuality and the ripple flag.**
- **Slashes, trails, streaks and smears.**
- **Spawn warp and death flourish**, with their sounds.

**Wave 3:**

- **Re-stage the crystal cut** in the void world with designed transitions
  (needs the world kit; can start as soon as it lands).
- **The game on every preset**, with `game perf` per preset (needs the
  waves above).
- **Capture every preset** of the crystal cut and record the evidence.
- **Final Warden.**

## Whole-Run Acceptance

- Both projects reference `com.playlistsurvivors.look`; `game/`'s forks of
  the crowd, shatter, effects runtime and post are gone, and demo3d's crystal
  look code is gone.
- `--look edge|solid|hologram|prism` and the per-axis flags work in both
  players.
- The crystal cut captures through `just trailer` in all four presets; its
  trailer checks and capture checks pass in each.
- In every preset, stills and filmstrips pass the style guide's metrics:
  palette conformance, value structure, swarm separation.
- In every preset, a dense clump of one type reads as individuals, monsters
  show form, the floor ripples from blows, and every shot of the cut sits in
  one visual language.
- Breaks and deaths still pay off on the impact frame; beat steps still land
  on the beat frame unless `--beat-ripple` is passed.
- `game perf` holds the budget in every preset; `game beat` passes.
- demo3d's plain action cut (`--trailer`, no look flag) is unchanged (its
  checks pass; its stills match those taken before the epic).
- Nothing vendor-derived is committed.
- trailer.md, unity-game.md, effects.md and this document describe what
  shipped; the operator-pending rows are filed.

## Open Questions

None blocking. Executors decide, and record in bead notes:

- each preset's world, post, VFX and hero choices, against the style guide;
- whether the impulse field is a texture, a ring list in a buffer, or both;
- how the swarm-separation metric isolates monsters (an ID pass or edges);
- the transitions' exact grammar.
