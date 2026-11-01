# HeroEditor in Native: a Feel Test

## Introduction

A feel test, epic `ps-4c8`: native's survivors run played with HeroEditor
characters, Hippo's skeletal 2D character kit, as the hero and an undead horde,
on a painted ground at dusk, behind a flag. It answers one question for the
operator: **does HeroEditor's animation feel good in the game?**

It is a light run and a mockup: nothing here has to ship.

Why (the operator, 2026-10-31):

- native's feel is liked far more than demo2d's.
- demo2d feels "crappy and clunky", mostly from its animation: Pixel Cattle
  frames at 15 fps, retimed to beats, with no input. native's motion (sharp
  beat steps, hops, knockback, death launches, real input) is the feel to keep.
- HeroEditor's default eyes and mouths look too cartoony: faces are subtle dots
  and lines only.

Related:

- [native-app.md](native-app.md) and [survivors-run.md](survivors-run.md):
  the game this reskins.
- [vendor-assets.md](../tech-demo-3d/vendor-assets.md): the rules HeroEditor
  falls under, an Asset Store pack.
- [native/evidence/heroeditor/](../../native/evidence/heroeditor/): the
  capture's contact sheets and face lineup, the ground's stills, and the
  run's paired stills and filmstrips.

## Decisions

Settled with the operator on 2026-10-31; no human gates later.

1. **Reskin native, don't build a new demo.** native's rules, sim, motion,
   input, effects, HUD and sound stay. Everything new sits behind
   `--art heroeditor`. Without it, or on a machine without HeroEditor, native,
   every check and CI behave as before.
2. **Bake the rig, don't run it.** A HeroEditor character is about 25 sprite
   parts on a two-layer Animator. herocap, a small Unity project, captures each
   recipe and clip to transparent PNG frames at 30 fps or more, which become a
   native character atlas. native already indexes walk frames by beat
   fraction, so more frames means smoother motion.
3. **Hi-res and smooth, not pixel art.** Frames are captured at about twice the
   LPC sprites' size and drawn smooth-sampled at the same on-screen size, so
   sim radii, the sword's reach and density read unchanged.
4. **A painted procedural ground and a dusk grade** replace the LPC ground
   under the flag.
5. **The hero is a sword-and-shield knight:** Knights armour, an open face,
   short hair, a Knights sword and shield.
6. **The horde is an undead army of HeroEditor humans**, told apart by gear,
   scale, skin tint and eye-glow colour (the recipe table below).
7. **Faces are dots and lines.** The hero: `Emoji/Blank` eyes (two dots), an
   `Expressions/Normal` mouth (a short line) and `Expressions/Bored` brows. The
   undead: `Undead/GlowingEyes` tinted per role, and a blank mouth or none.
   Never the default Male or Female eyes, the Emoji blobs, the big ZombieEyes,
   or an open or toothy mouth. A test holds every recipe to it.
8. **Side view only.** HeroEditor draws only side-on: left is a mirror, and up
   and down keep the last horizontal facing.
9. **Vendor rules apply in full.** No HeroEditor file, captured frame or baked
   atlas ever reaches git. Frames and atlases live in the per-machine cache, so
   they outlive worktrees and the main checkout can play them. Tagged renders
   in evidence are fine.
10. **Agents judge from headless output** (`--headless --seconds N`, stills and
    clips), never an opened window. Feel is the operator's judgement, in
    native/evidence/operator-pending.md, never a gate.

## The Pipeline

```sh
just herocap                                  # set up if needed, capture every recipe, bake the atlas
just native -- --art heroeditor               # play it
uv run --project demo3d/tools herocap evidence  # contact sheets, faces, budget into evidence
```

`just herocap` is `herocap capture` then `herocap bake`; either runs alone through
`just herocap-tool`.

1. **Vendor intake.** `herocap setup` syncs HeroEditor from the `more_assets`
   root (`~/unity_assets/MoreAssets/MoreAssets` on the Mac) into the gitignored
   `herocap/Assets/Vendor/`. `herocap/manifest.json` lists HeroEditor's code (no
   asmdefs, so all of it compiles into Assembly-CSharp), Human.prefab and the
   sprite and firearm collections; the controller, clips, materials, shaders
   and every part's sprites follow by GUID. The manifest `requires` that root,
   so its entries take no roster, unlike demo3d's roster sets, and the guard
   needs them in the machine's sync record. On a machine without the root,
   setup fails clearly and the guard treats herocap as uncovered.
2. **Recipes**, tracked JSON in `herocap/recipes/<atlas>.json`: the parts by
   HeroEditor sprite-collection id (an id may end `#RRGGBBAA`, a tint), a skin
   tint, the weapon type, one face, an overall scale and the clips. The capture
   turns a recipe into HeroEditor's own CharacterSerializer description and
   loads it with `Character.FromJson`.
3. **The clip table**, `herocap/clips.json`: each clip pairs an upper-body
   state with a lower-body one, as Human.controller's two layers do, and
   names the one whose length leads; plus the capture's rate (30 fps), the
   hero's idle height in pixels (96, twice the LPC hero's 48) and supersampling.
4. **The capture** (`herocap capture`, Capturer.cs, batch mode):
   - It builds an AnimatorController with Human.controller's layers and every
     state, but no transitions, behaviours or speed parameters, and turns the
     Animator's events off: no state moves on and no expression event changes
     the face. Every expression gets the recipe's face too.
   - For each frame it plays both layers at the same normalized time and
     evaluates them with `Animator.Update(0)`: deterministic, never timed by a
     coroutine (HeroEditor's SpriteSheetCapture).
   - It draws each frame with an orthographic camera at a fixed pixels per unit,
     twice, over black and over white, which recovers true alpha through
     HeroEditor's blending, at 2x and box-filtered down. The pixels per unit
     come from a render of the hero idle (a sprite's bounds include its
     transparent margins), so every recipe's size is its `scale` times the
     hero's. The feet are where HeroEditor's Shadow sprite sits; the shadow
     itself is off, since native draws its own.
   - A clip's frames are cropped to their union bounds, so they share one size,
     and a feet anchor; a pose that leaves the canvas fails the capture.
5. **The output**, under `<cache>/heroeditor/` (`paths.heroeditor_cache()`,
   `~/Library/Caches/playlist-survivors/demo3d/heroeditor` on the Mac):
   - `frames/<atlas>/<clip>_<n>.png`, facing right;
   - `frames/<atlas>/clips.json`: per clip its HeroEditor states, frame count,
     rate, length, loop or one-shot, size, feet anchor (from the top left), and
     impact frame (the clip's `Hit` event, or annotated in clips.json; -1 if
     none), plus the recipe's scale and the pixels per unit;
   - `faces/<atlas>.png`, an idle head close-up.

   The guard's derived record hashes everything there, so a copied frame is
   caught anywhere in the tree.
6. **The bake** (`herocap bake`, `demo3d_tools/herobake.py`) packs the frames
   native draws into `<cache>/heroeditor/assets/`: a characters.json in
   native's atlas format and its page, `heroeditor-0.png`. See
   [The Characters in Native](#the-characters-in-native).

## Recipes

One per atlas the survivors run uses (content/survivors-enemies.yaml), plus the
hero and goblin, native's fallback look; a test fails if one is missing. The
full table, with gear and tints, is in
[native/evidence/heroeditor/capture/README.md](../../native/evidence/heroeditor/capture/README.md#recipes).

| atlas | cast as | scale | glow |
|---|---|---:|---|
| hero | sword-and-shield knight, Knights armour, open face | 1.0 | (dots) |
| skeleton | ragged zombie militiaman, Slave rags, a machete | 1.0 | cyan |
| slime | hunched swamp ghoul, SwampLords rags, unarmed | 0.9 | green |
| bat | small hooded wraith with a cape | 0.7 | violet |
| bee | pale hooded ghost | 0.8 | pale yellow |
| orc | zombie Viking, rough armour, an axe | 1.0 | green |
| orc-elite | Viking elite, iron helm and shield | 1.05 | amber |
| orc-ember | burning SandLords warrior, ember skin | 1.0 | orange |
| brute | big revenant knight, a two-handed hammer | 1.4 | red |
| minotaur | death lord, Thrones armour, a two-handed sword | 1.6 | crimson |
| thorn-plant-summer, -autumn | rooted shaman, skull hood and cloth mask, a staff | 1.0 | green, yellow-green |
| goblin | zombie peasant with a stake | 1.0 | pale green |

Deviations from the epic's default casting, for readability: orc-elite wears
VikingIronHelm2 (the first iron helm hides the eyes), and the shamans wear a
skull hood and a cloth mask instead of a SwampLords mask, whose painted eyes
are big and cartoony.

Clips per role: idle, walk, hurt, a death (`death-back` or `death-front`), an
attack matched to the gear (`slash`, `jab` unarmed, `slash2h`, or `cast` for
the shamans' staffs), and `victory`, a 1 s arms-up loop for native's dancers'
`kick` and `kick-alt`. The hero has idle, walk, run, jump, slash, jab,
slash2h, cast, hurt and death-back.

## Frame Budget

At 30 fps, 96 px for the hero standing, 2x supersampling, the capture is 1669
frames and 20.8 megapixels (2 px padding), 2 pages of 4096². The bake packs
only the clips native draws (no enemy attacks or idles) and no hit-flash twins,
which native draws as silhouettes: 1104 frames on **1 page**. With native's six
own pages that is 7 of the 8 it binds (MAX_PAGES), so 60 fps frames (about 2
pages) would need MAX_PAGES raised or the LPC character page dropped.

## The Painted Ground

`--art heroeditor` (`just native -- --art heroeditor`) paints native's ground
and grades the scene as dusk (`ps-4c8.2`). It sets the art once for the process
(`slice/art.rs`), so every check drawing a scene honours it. `--art lpc`, the
default, is native as before: its stills are byte-identical with and without
the change.

- **The ground** (`render/src/ground.rs`) is one full-screen pass into the
  world texture, under the sprites, instead of the clear colour and the LPC
  tiles. Each pixel is a function of its world position, so it scrolls with
  the world and holds still as the camera moves, shakes and zooms (a test
  moves the camera and compares). It paints, in order:
  - broad tones of deep and light grass, brushed in long horizontal strokes,
    with short ragged dabs of lighter and darker grass;
  - moss patches, and clearings of earth with ragged rims;
  - worn paths about 30 world px wide, wandering along a warped noise field's
    middle (the width from the field's screen-space slope, so it holds at any
    zoom), with a darker trodden rim;
  - in some cells of a 72 px grid: a vector-like tuft of three to five tapered
    blades, a cluster of three flowers, or a pebble with its shadow and a
    light; none on the paths and fewer in the clearings.
- **Zones** keep their palettes (`slice/painted.rs`): the glade's cool green,
  the woods' fresh green, the grove's olive and ochre, the hollow's dry
  grey-brown and the infernal grove's scorched rock with ember paths. The
  transform wave paints the next zone's palette inside its ring, whose edge is
  drawn in soft glows instead of square blocks.
- **Props:** the LPC trees, bushes, flowers, tufts, rocks, braziers and ruins
  are cosmetic and hidden (with the braziers' lights). The landmarks' standing
  stones keep their gameplay meaning and stay as they are.
- **Dusk** goes over native's grade: a warm ambient, bright enough for flat
  colours to read and tinted a little by the zone's light; the zone's own
  colour grade at 40 %; violet shadows and warm highlights (split toning, new
  in the composite and zero by default); and a low sun's warm glaze from the
  top left. The beat, kick, snare and rise pulses, the bloom, the vignette,
  heat haze and the camera's zoom pulses are untouched, and the hero's and the
  effects' lights still add over the ambient.
- **Cost:** the soft noise reads a 256² grain texture, one filtered read per
  octave; only the paths use exact hashed noise, for clean edges. On the Mac
  (M5 Max) the pass adds about 0.7 to 1 ms of GPU per frame at a 960 x 640
  window's 1920 x 1280 pixels, and about 2 ms at 3456 x 2234. CPU frame work at
  the densest minute (`--bench-density`) is unchanged, and the encode is
  cheaper, with no tiles or props to draw.

Evidence (taken with LPC characters, before `ps-4c8.3`):
[native/evidence/heroeditor/ground/](../../native/evidence/heroeditor/ground/README.md).

## The Characters in Native

`--art heroeditor` draws every survivors role as its HeroEditor recipe
(`ps-4c8.3`). native's sim, timing, input and effects are untouched: only the
drawing changes, and `--art lpc`, the default, draws byte-identical frames.

- **The bake's names.** Each atlas gets native's animation names, each one of
  its clips:
  - the hero: `idle`, `slash` and `halfslash` (slash), `backslash` and
    `thrust` (jab), `slash-oversize` (slash2h), `spellcast` (cast), `hurt`,
    and `jump`, the beat step, as the run cycle (HeroEditor's Jump clip holds
    one pose);
  - an enemy: `walk`, `hurt`, `death` (death-back or death-front), and its
    dancers' `kick` and `kick-alt`, the two halves of the arms-up `victory`
    loop, so two beats play it whole;
  - the thorn plants are rooted shamans: their `walk` is the idle clip.
- **Loading.** `art::atlases()` loads native's atlases with the cache's
  characters laid over the LPC ones by sprite name (`Atlases::load_with`), so a
  sprite the bake lacks (the eyeball, skeleton archers) keeps its LPC art. With
  no bake in the cache it fails, naming `just herocap`. `PS_HEROEDITOR_ASSETS`
  points it elsewhere; otherwise it reads `DEMO3D_CACHE` or the Mac's default
  cache, as the tools write.
- **Size.** Each baked animation carries a `density`, frame pixels per world
  pixel, drawn smooth-sampled (pages named `heroeditor-*` use the linear
  sampler) at the LPC size: each character stands as tall as the LPC sprite it
  replaces (its walk, or the thorn plants' prop), so sim radii, reach and
  density read unchanged. The bat, bee and slime were blobs, not people: their
  stand-ins keep the recipe's scale against the hero instead. On a Retina
  screen at the camera's base zoom the hero's frames land about 1:1 on pixels.
- **Side view.** Animations marked `side` hold right-facing frames for every
  facing; native mirrors them about the anchor for left. An enemy moving up or
  down faces the hero. The hero faces the side its facing angle leans to
  (`HeroPose::dir`: its heading, or a swing's aim), so a swing aimed down and to
  the left swings left; only straight up or down keeps its last side.
- **Hit flash.** No white twins are baked: a flash draws the frame as a white
  silhouette (the sprite shader's alpha mask).
- **Timing.** native's sim times the hero's swings in its atlas's frames
  (`default_hero_anims`: a slash is 6 frames, the strike on frame 4). The sim
  now also reports each pose's frame unfloored (`Recorder::hero_art`, the
  live view's `HeroPose::phase`), and the scene maps that phase onto the
  HeroEditor clip piecewise: the wind-up onto the frames before the clip's
  impact frame, the follow-through onto those after, so the strike's frame
  lands on native's strike time and every frame between plays (a 16-frame
  slash shows about 9 frames in a 190 ms wind-up). Enemy walks keep native's
  beat-fraction indexing (20 frames a cycle instead of 8); a death plays the
  whole clip over native's 0.5 s fall from the killing blow, with no build-up,
  where LPC falls take six steps.
- **The swing's smear.** LPC slashes draw a white crescent in their frames;
  HeroEditor's have none. A slash (light or heavy) draws a smooth additive arc
  (`Sprite::arc`) along the facing side from above the hero's chest to below,
  from half a frame before the strike to a frame and a half after it.
- **Everything else** (hover, hops, squash and stretch, knockback, tints, the
  hero's light and outline, the brute's stomp) works on the HeroEditor sprites
  as on the LPC ones; the thorn plants keep their beat bounce.

Evidence, the whole-run acceptance and costs:
[native/evidence/heroeditor/README.md](../../native/evidence/heroeditor/README.md).
