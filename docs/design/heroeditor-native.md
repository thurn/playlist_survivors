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
  capture's contact sheets and face lineup, and later the run's stills.

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
just herocap                                  # set up if needed, then capture every recipe
uv run --project demo3d/tools herocap evidence  # contact sheets, faces, budget into evidence
```

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

At 30 fps, 96 px for the hero standing, 2x supersampling: 1669 frames and
20.8 megapixels (2 px padding), **2 pages** of 4096², **3** with native's
hit-flash twins. Native allows 8 (MAX_PAGES), and the swapped-out LPC page
frees one; 45 fps would need about 5 with flash twins, 60 fps about 6.

## Later Beads

- `ps-4c8.2`: the painted ground and dusk grade.
- `ps-4c8.3`: the atlas bake, `--art heroeditor`, the animation mapping and the
  feel pass. It maps native's animation names onto the captured clips and
  draws each role at the size of the LPC sprite it replaces.
