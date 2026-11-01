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
| Structure shatter | `Arena/StructureShatter.cs`, `StructureShards.cs` | `Client/Structures/StructureShatter.cs`, `StructureShards.cs` |
| Crystal formations | `Arena/CrystalFormations.cs` | none |
| Effects runtime | `Runtime/Effects/Effect*.cs`, `EffectSim.compute`, `EffectParticles.shader` | `Client/Effects/` forks |
| Post and grade | `Arena/NightLook.cs`, `Playback/PostFeel.cs`, `TrailerLook.cs`, `LensHitFeature.cs` | `Client/Arena/NightLook.cs`, `Client/Look/PostFeel.cs` |
| Beat globals | `Crowd/Styles/StyleBeat.cs` | `Client/Look/BeatGlobals.cs` |
| Bakers' unweld step | `Editor/Crowd/CrowdBaker.cs` (`Unweld`) | `Editor/Crowd/CrowdBaker.cs` |

Paths are under `demo3d/Assets/Demo3D/` and `game/Assets/Game/`. Where the
forks have diverged (game's shards on compute, its regrowth, its heard-time
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
(`_LookHeroBody`), and the enemy hues and zone tints as linear arrays
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
