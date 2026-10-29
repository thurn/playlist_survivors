# 2D Pixel Demo

The design for a Unity 2D pixel-art demo: the 3D tech demo's fight, redrawn
as a pixel-art Vampire Survivors-style battle, to *Action*. It is split into
two documents:

- **This page:** the goal, the decisions, what the viewer sees, and how it is
  built.
- [Implementation Plan](plan.md): the tasks PX0 to PX10, their order, status
  and notes, and what is pending on the operator.

## Introduction

Playlist Survivors is a horde survivor rhythm game: the horde combat of
Vampire Survivors with the beat-locked movement of Crypt of the NecroDancer.
The [3D tech demo](../tech-demo-3d/README.md) asked how many animated
monsters fit on a laptop GPU. This demo asks nothing. It exists to **sell the
idea of the game**: a non-interactive Unity build in which a pixel-art knight
carves through a pixel-art horde to the song, as if a perfect player were
holding the controls. It should look like "the game that doesn't need a
trailer": every moment cinematic.

It produces:

- **The demo build:** a Mac app (Metal, IL2CPP) that plays the full 3:19
  song at 1920×1080, 60 fps.
- **Beauty stills** in its evidence, for review and sharing.

There is no video capture (the operator dropped it on 2026-10-29), no
benchmark sweep and no frontier report. Wherever a choice doesn't change how
the demo looks or sounds, the simplest option wins. None of its code is meant
to ship.

### Decisions

Settled with the operator on 2026-10-29 in a design interview. Agents treat
them as fixed; every other choice is the implementing agent's, recorded with
its reasons in the task's notes.

1. **A showcase, built and checked on the Mac.** No sweeps, no frontier, no
   Cygnus. The bar is that it looks great and holds 60 fps at 1080p on the
   operator's Mac (Apple silicon, Metal).
2. **A new Unity project, `demo2d/`, sharing demo3d's choreography core.**
   The same song map, forward simulation and timeline contract decide the
   fight; Unity only reflects it, as in the 3D demo (its Decision 2).
3. **The core becomes a local Unity package** both projects reference
   (`unity-packages/com.playlistsurvivors.choreography/`, namespace
   `PS.Choreography`). Each demo keeps its own data, catalog and clip
   library. demo3d must play the song identically after the move.
4. **One pixel grid.** The world renders at 960×540 into a pixel-perfect
   target, upscaled 2× to 1080p. Character frames are downsampled at bake
   time to their game size, so tiles, sprites and effects share one crisp
   grid. Camera zoom and punch go through a smooth upscale pass, so they are
   not limited to integer steps.
5. **The hero is `knight_male`:** sword and shield, two slash skills with
   built-in arcs, defensive animations. Sword combos on beats, CFXR spells on
   the musical grid, as in the 3D demo.
6. **The roster is the undead core plus brutes,** all Pixel Cattle Games
   packs from `~/unity_assets/Assets`:
   - grunts: Skeleton, Ghoul (`hunger_zombie`), Zombie Warrior, Flame Imp,
     Ice Skull, Mask;
   - casters, which fire bolts: Skeleton Mage, Blood Mage;
   - elites: Ogre, Cyclops, Werewolf, Lava Beast;
   - the boss: Boss Devil Man.
7. **Night, lit with URP's 2D renderer.** A dim, cool global light (the
   moon), glowing spots, and short point lights on every hit, spell and
   projectile, with bloom and a colour grade.
8. **The arena is generated from a committed layout JSON,** in demo3d's
   ArenaLayout format plus a 2D extension, into Unity Tilemaps built from
   `RPGW_AncientForest`. The sim reads the same JSON, so trees, cliffs and
   water are real obstacles.
9. **About 400 monsters on screen at the climax,** with the camera eased out
   to about 1.35× for the final energy peak. It has its own level script,
   derived from demo3d's `action-level.json`.
10. **Effects draw in the pixel grid.** CFXR effects draw into the 960×540
    target with the sprites. Bloom, screen distortion and the grade run at
    1080p after the upscale. An effect may opt into a full-resolution layer
    when pixelation ruins it, with the reason recorded.
11. **No HUD,** like the 3D demo: the scene only.
12. **No video capture.** Stills are the evidence; the operator watches the
    app.
13. **The song is *Action*,** with demo3d's song map (`songs/action/`) and
    the same local audio rules.
14. **The plan is markdown,** with plan keys `PX0` to `PX10`, evidence in
    `demo2d/evidence/<key>/`, landing through Enact with no beads, as the 3D
    demo now does.

## Related Information

- [3D tech demo](../tech-demo-3d/README.md): the structure this demo copies.
  The most relevant parts are [Choreography](../tech-demo-3d/choreography.md)
  (the layer this demo shares), [Vendor Assets](../tech-demo-3d/vendor-assets.md)
  (the guard this demo extends), [Running on the Mac](../tech-demo-3d/mac.md)
  (the setup, build and windowless-still commands this demo mirrors) and
  [Camera and Sound](../tech-demo-3d/camera-and-sound.md) (sound aggregation
  and scheduling, reused as is).
- [tech-demo.md](../design/tech-demo.md): the earlier 2D *browser* tech
  demo and its effect catalogue, a reference for juice. Not to be confused
  with this one.
- [AGENTS.md](../../AGENTS.md): the vendor-asset rule, Enact delivery and
  the windowless-testing rule.
- Asset sources, read-only, in `~/unity_assets/Assets` (a Unity 6000.6.7f1
  project, built-in pipeline):
  - Pixel Cattle Games character packs, one folder each (`Skeleton/`,
    `knight_male/`, …): Spine-exported PNG frames (`<name>_png/`), an
    Animator prefab with `.anim` clips, and the Spine 3.6 project;
  - `RPGW_AncientForest/`: the `MainLev` tile sheet (32 px tiles, sliced),
    the `Decorative` sheet (trees, bushes, stumps, logs), and seven swaying
    trees with clips;
  - `JMO Assets/Cartoon FX Remaster/`: 244 CFXR particle prefabs (impacts,
    explosions, fire, electric, liquids, text, screen distortion) with
    built-in and URP shader variants.
- The sound packs demo3d already uses, from `~/unity_assets/UnityAssets`
  (Magic Spells SFX Bundle, RPG Magic Sound Effects Pack 3, Universal Sound
  FX).

## What the Viewer Sees

A single 3:19 shot of one battle, top-down, with no input and no HUD. It
should feel like watching an expert play a finished pixel-art game.

- **The arena.** The Ancient Forest at night: mossy clearings and dirt paths
  through dense woods, a cliff ledge, a stream with a stone bridge, old
  stumps and fallen logs. Trees sway. Fireflies and shafts of moonlight
  glow; effect light carries the fight.
- **The hero.** The knight strides on beats from clearing to clearing and
  carves through the horde with sword combos whose blows land on beats.
  Spells fire on the musical grid: rings on downbeats, bolts on eighth notes,
  lightning on accents, and a beam sweep in the climax.
- **The horde.** Skeletons and ghouls first, then imps, ice skulls, masks and
  zombie warriors in waves, casters lobbing bolts, brutes as elites, and the
  Boss Devil Man at the climax. They step on beats, attack on beats, flinch
  when hit (a white flash and a knockback hop), and die with their own death
  animation and a CFXR burst.
- **The arc.** The same as the 3D demo's, from demo3d's level script: a
  handful of skeletons in the intro; density and spell intensity following
  *Action*'s mapped energy, with room to breathe; about 400 on screen in the
  final energy peak with the camera eased out; the horde cleared on mapped
  accents at the end.

### The look

- **Perspective.** Vampire Survivors' convention: a top-down tile ground with
  side-view characters. Characters face left or right (a horizontal flip) and
  sort by their feet's y. Each casts a soft pixel-blob shadow.
- **Scale.** One tile (32 grid pixels) is one metre in the sim. A grunt is
  about 56 grid pixels tall (1.75 tiles), elites about 1.6×, the boss about
  3×. The base view is 30×17 m, close to the 3D demo's 22.5 to 30 m widths,
  so the level script's distances carry over.
- **Occlusion.** Trees in front of the fight fade where they cover the hero
  or the horde, as demo3d's canopy does.
- **Reactions.** Hit flash, knockback, hit-stop on the hero's big blows,
  and camera punch and shake on accents, all driven by the timeline.

## System Overview

The data flow is the 3D demo's, with a 2D client:

```
at load:    timeline = Choreographer.Run(songMap, levelScript2d, layout2d, catalog2d, seed)
each frame: t = songClock.Now()
            states = Evaluate(timeline, t)                 // per entity
            sprites.Upload(states)                        // instanced, y-sorted
            effects.Spawn(timeline.EffectsDue(t)); lights2d.Update(t)
            camera.Update(timeline.Framing(t))             // plus punch and shake
            sound.Schedule(timeline.SoundsDue(t))
            pixelTarget(960x540) -> smoothUpscale(zoom) -> bloom, grade -> 1080p
```

- **The shared core** (`PS.Choreography`): song map, beat grid, forward sim,
  navigation, spatial hash, timeline types, sound aggregation. Engine-free
  C# 9, tested by `dotnet test` and by both projects' EditMode tests.
- **Per-demo data:** demo2d's catalog (roster types, sizes, speeds, footprint
  radii), clip library (each clip's frame count, frame rate, impact frame,
  stride), level script, layout and effect list, all tracked JSON under
  `demo2d/Assets/Demo2D/Data/`.
- **The 2D client** (`demo2d/Assets/Demo2D/Runtime/`): the sprite crowd, the
  hero, the tilemap arena, CFXR effects, 2D lights, the camera and the
  pixel pipeline, the song clock and the sound scheduler.

### Sprites

Character frames are baked, not imported as is:

- **The baker** reads each roster pack's PNG frames (never Spine at
  runtime), trims them, and downsamples them to the type's game height with
  an area filter. Alpha is snapped to 0 or 1, and the frames are packed into
  one atlas per type with point filtering. The frames were rendered from
  Spine with smooth transforms, so the downsample is what puts them on the
  grid; the baker's contact sheets are where that's judged.
- **Clips** come from the frame names (`skeleton-run_3.png` is frame 3 of
  `run`), at the pack's frame rate (read from its `.anim` files).
- **Annotations** (impact frame for attacks, the stride per walk cycle, the
  end of hit and death) live in tracked JSON, proposed by the annotator from
  frame motion and checked from contact sheets, as demo3d's TD2 did.
- **Drawing.** One instanced quad per entity: atlas, frame, flip, flash and
  tint per instance, sorted by y. 400 monsters plus the hero and projectiles
  are a few draw calls.
- **Retiming.** As in demo3d: the client starts each attack clip so its
  impact frame lands on the timeline's impact time, and scales walk cycles to
  match the beat-step gait.

### The pixel pipeline

- The camera renders the world (tilemaps, sprites, effects, 2D lights) into
  a 960×540 point-filtered target at a whole-pixel camera position, plus a
  margin for punch and sub-pixel scroll.
- The upscale pass maps the target to 1080p at the current zoom (2× at the
  base view, down to about 1.48× at the climax's 1.35× zoom-out). Its
  filter is sharp-bilinear (texel-centred, antialiased only at the texel
  edges), so non-integer zooms stay crisp without shimmer. At 1.35×, the
  target grows to 1296×729 instead, so the climax keeps the grid's density.
  PX5 picks between these two from stills.
- Bloom, screen distortion (CFXR's distortion prefabs, if kept) and the
  colour grade run on the 1080p image.

### The arena

- **The 2D layout** reuses demo3d's `LayoutArea` shapes (clearings, paths,
  edges, forest fill, landmarks) and adds a ground material per area (grass,
  moss, dirt, stone path), cliffs (a capsule with a facing), water (a stream
  capsule) and bridges.
- **The generator** (an editor task, output in the gitignored `Baked/`)
  writes ground, cliff and water Tilemaps with autotiled edges from the
  `MainLev` sheet's blob sets. It scatters trees, bushes, stumps and logs
  from `Decorative` and the swaying trees by the layout's scatter rules, and
  emits each prop's footprint for the sim's obstacle map.
- **The sim's navigation** (flow fields, goals, walls) is demo3d's, fed by
  the 2D layout's obstacles.

### Effects and lights

- **CFXR prefabs run as stock Shuriken particle systems** under URP's 2D
  renderer, with CFXR's URP shader variants. At about 400 monsters and a
  bounded rate of effects per beat, stock particles are cheap on the Mac;
  PX7 measures it and pools instances.
- **An effect list** (tracked JSON) maps each timeline effect kind (slash
  arc, hit spark, death burst, ring, bolt, lightning, beam, boss slam) to a
  CFXR prefab, a scale, a tint, a sorting layer and a light cue (colour,
  radius, intensity curve) for a pooled `Light2D`.
- **Night lighting.** A global `Light2D` at low, cool intensity; freeform
  lights for moonlight shafts; point lights for fireflies and glowing plants;
  the light pool for effects. Characters and tiles are lit; effects are
  additive and unlit.

### Sound

Unchanged from the 3D demo: the song as streamed audio started with
`PlayScheduled`, effect sounds aggregated in the core (at most 3 voices per
beat and category, gain growing with log2 of the hits), scheduled at DSP
time within 150 ms.

## Vendor Assets

The 3D demo's rules apply in full ([Vendor Assets](../tech-demo-3d/vendor-assets.md)):
no vendor file, derived file (baked atlases, generated tilemaps and scenes,
edited prefabs), build or song audio ever reaches git. This demo adds:

- **A new source root, `pixel_assets`,** for `~/unity_assets/Assets` (the
  2D packs). It is separate from `unity_assets` (`~/unity_assets/UnityAssets`),
  so the sync record must cover it. Until it does, the guard can't recognize
  those files by hash or GUID.
- **Gitignored folders:** `demo2d/Assets/Vendor/`, `demo2d/Assets/Baked/`,
  `demo2d/Library/`, `Builds/`, `Logs/`, `Temp/`, `UserSettings/`, and the
  generated project files, mirroring demo3d's `.gitignore` lines.
- **The guard covers `demo2d/`** exactly as it covers `demo3d/`: the binary
  rule (with the `ps-render` tag exception for evidence stills), the record's
  GUIDs and hashes, and the derived record of `demo2d/Assets/Baked/` and
  `demo2d/Assets/Vendor/` (Unity re-serializes the packs' binary clips as
  YAML on import, so those copies match no source file; PX2).
- **A manifest,** `demo2d/manifest.json`, lists every vendor asset the demo
  uses, synced by the same tool.

Any change to the guard gets its own cold review (PX1).

## Verifying the Assets

Before anything is built on them, PX2 proves that each pack works in
`demo2d`, under URP's 2D renderer on Metal, and that none of it can reach
git:

- every roster type's and the hero's clips import with complete frame
  sequences and play at their frame rate (a contact sheet per type, and a
  test that every clip the clip library names has its frames);
- the `MainLev` and `Decorative` slices import at 32 px per unit with point
  filtering, and a test map using every autotile set renders without seams;
- the swaying trees' clips play;
- every CFXR prefab on the effect list compiles and renders under URP 2D,
  lit and unlit, with no pink (missing-shader) materials, checked
  automatically and by a gallery still;
- the vendor guard drill: force-staging a synced file, a baked atlas and a
  `.meta` from each pack fails the guard, in a scratch clone.
