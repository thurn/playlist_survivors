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
    action cut and its Synty-environment looks stay untouched as the
    "before" reference.
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
option without a look draws `MonsterLooks.Crystal`, today's crystal. The game
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
globals only demo3d sets, so the game's look is unchanged. Where the forks
diverged the crystal cut took the game's: GPU shards for PhysX, LODs, the
half-tinted hit flash ([crowd.md](../tech-demo-3d/crowd.md#monster-styles)). The
action cut and the other Synty styles keep demo3d's own crowd (Decision 11).

Where the forks have diverged (game's shards on compute, its regrowth, its heard-time
strikes), the package takes the game's version: it is the one that has to
hold a frame budget.

What stays in each project: playback and the choreographer (demo3d), the sim,
presenters and HUD (game), and the trailer's shot system. demo3d's action cut
keeps its own copies of anything it needs that the package changes, so the
action cut's look does not move.

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
--beat-ripple                    # steps ripple outward (off in every preset)
```

The crystal cut's file (`trailer-crystal.json`) names no look. The capture
recipe takes one or many:

```sh
just trailer --out captures/trailer-neon-edge.mp4 -- --trailer crystal --look edge
just trailer-looks                # all four presets, one capture each
```

`trailer-looks` is a new recipe; the exact shape is the executor's.

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
  so for any trailer but the action cut; the action cut and the song outside
  a trailer ignore the look flags and say so.

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
- **`VoidWorld`** is the air: the camera clears to `void.floor`, the
  distance fades into `void.fog` (exponential squared, gone past about
  150 m), `Hide` takes the vendor set away, and under `hued-zones` a zone
  eases the floor's lines and the fog's hue to the palette's next zone tint.
- **game**: `VoidArena` hides the Synty set under Static, the mist and the
  flames' lights under a world option, and under a structure look stands
  `CrystalProps` on each site's tiles from the build's own `arena.json`
  (the kit pieces hidden), re-rendering the static shadow map from what
  stands. `StructurePresenter` turns the sim's events into the shatter's
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
  liked); under `hued-zones` each shot takes the next zone tint on its cut.
  Re-staging the shots for the void is ps-wnj.17.

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
  each Synty set stays until the world kit (ps-wnj.8).
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
  deaths, the `BreakMoments`). The action cut has neither.

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
  grain and dirt; the action cut and the song keep `NightLook`,
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

### Hero

The hero is the brightest single thing on screen in every preset, in the
palette's `hero` role, readable at the trailer's 32–42 m framing without
growing (ps-b5z.1's rule) or an outline (removed on the operator's review).
The crystal blade's flare stays. Each preset picks a hero treatment
matching its monsters.

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
- demo3d's action cut is unchanged (its checks pass; its stills match those
  taken before the epic).
- Nothing vendor-derived is committed.
- trailer.md, unity-game.md, effects.md and this document describe what
  shipped; the operator-pending rows are filed.

## Open Questions

None blocking. Executors decide, and record in bead notes:

- each preset's world, post, VFX and hero choices, against the style guide;
- whether the impulse field is a texture, a ring list in a buffer, or both;
- how the swarm-separation metric isolates monsters (an ID pass or edges);
- the transitions' exact grammar.
