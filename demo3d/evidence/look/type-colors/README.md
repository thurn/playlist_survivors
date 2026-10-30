# Look set: a signature colour per monster type (ps-e8r.8, 2026-10-29)

The horde was mostly grey steel and bone, so even in same-type clumps
(ps-e8r.9) it read as grey confetti. Every monster type now has its own hue,
as in DRG:S, so each clump is a mass of one colour.

Rendered by the look harness (IL2CPP player, Apple M5 Max, Metal), from one
build at staging `96c46047` plus this bead:

| folder / file | what |
|---|---|
| [before](before/look.md) | the same build with `--type-dye 0`, which turns the dye off and leaves the old crowd shading exactly (its numbers match [hero-pool](../hero-pool/look.md)) |
| [after](after/look.md) | the type colours, measured against `before` ([after/contact-sheet.jpg](after/contact-sheet.jpg): before left, after right) |
| [contact-sheet-baseline.jpg](contact-sheet-baseline.jpg) | the epic's [baseline](../baseline/README.md) left, after right |
| [roster.jpg](roster.jpg), [roster-undyed.jpg](roster-undyed.jpg) | the roster style still: every drawn type in a row under the night look, dyed and undyed |

## What changed

- **roster.json** gives every monster type a `hue` (HSV degrees), and
  `typeColors` sets the saturation (0.8) and the dye (0.85). The crowd baker
  copies them into `CrowdType` (`Hue`, `HueSaturation`, `Dye`, `HueRotate`).
- **CrowdSkinned.shader** (`TypeDyed`) dyes the low-saturation albedo
  (steel, bone, cloth: saturation below 0.4, fading out by 0.8) toward the
  type's hue at the albedo's own luminance. Saturated swatches (skin, eyes)
  and the brightest highlights keep most of their colour, so faces and metal
  glints stay readable. `BoneTextureCrowd` sets `_Demo3DTypeColor` per type on
  its main-draw property blocks. The hero has no hue, so it is unchanged.
- **`hueRotate`** first turns a whole type's albedo round the luma axis (YIQ).
  It is for the two types whose saturated green hide sat on the goblins' and
  slimes' hues: the dragon (+150, green to magenta) and the big orc (+60,
  green to cyan-teal, its orange stripes to magenta).
- **Player flags for tuning:** `--type-dye d`, `--type-saturation s` and
  `--hue-rotate-<id> deg` override the roster. `--classic-monster-colors`
  turns the dye and the hue rotation off, for the palette before this pass
  (`--type-dye 0` alone still turns the dragon and the big orc).
- **The crowd bench's `--scene roster`** lines up every drawn type under the
  arena layout's night look. In batch mode the bench now renders offscreen,
  so its stills need no window:

  ```
  "Playlist Survivors 3D Demo" -batchmode -force-metal --crowd-bench --scene roster \
    --stills DIR --still-widths 20 --time 0 --still-width 960 [--type-dye 0]
  ```

- **The look harness** reports the monster pixels' mean chroma (`monster C*ab`)
  in the readability table and in look.json (`monsterChroma`). With a
  baseline that has it, the harness also prints the ratio.

## The hues

| type | role | hue | colour on screen |
|---|---|---:|---|
| skeleton | fodder (the biggest mass) | 20 | rust orange |
| mushroom | fodder | 60 | ochre-yellow (its own) |
| goblin | fodder | 100 | toxic green |
| slime | fodder | 140 | emerald (its own) |
| bat | fodder | 260 | violet |
| demon | fodder | 340 | crimson (its own) |
| bigork | elite | 180 | cyan-teal (rotated +60) |
| troll | elite | 220 | icy azure |
| dragon | boss | 300 | magenta (rotated +150) |
| evilgod | rejected boss, not drawn | 300 | |

Fodder runs the warm half of the wheel, against the warm stage. The rare
elites and the boss take the cool and hot hues, which stand out there.

**A deviation from the bead:** it suggested 4-6 hues by role. But its own
test, that types sharing a section differ by at least 35 degrees, needs nine:
the final peak holds eight types and the boss. So every type has its own
40-degree slot. The slime, mushroom and demon hues were chosen to match their
own saturated colour, which the dye leaves alone.
`TypeColorTests.TypesThatShareASectionDifferInHue` checks every section of
action-level.json, the boss included.

## Measures

The monster pixels' mean chroma C*ab, in the measured beauty stills (ID mask,
no effects):

| shot | before (dye 0) | after | ratio |
|---|---:|---:|---:|
| opening | 44.6 | 60.7 | 1.36x |
| path | 42.0 | 57.1 | 1.36x |
| glowing-tree | 42.3 | 57.7 | 1.36x |
| graveyard | 43.8 | 57.6 | 1.32x |
| courtyard | 42.7 | 58.9 | 1.38x |
| forest-edge | 42.5 | 56.2 | 1.32x |

The baseline's captures (and so its ID masks) aren't kept, so its monster chroma
is measured on the 960 px horde JPEGs (effects included) under this set's ID
masks, downscaled. The monster pixels are the same in every set, since the
simulation is deterministic (the baseline README shows identical monster
counts). The same measure on all three sets:

| shot | baseline | before | after | after / baseline | after / before |
|---|---:|---:|---:|---:|---:|
| opening | 23.7 | 49.3 | 63.3 | 2.67x | 1.28x |
| path | 25.9 | 48.6 | 61.5 | 2.37x | 1.27x |
| glowing-tree | 23.7 | 47.2 | 60.9 | 2.57x | 1.29x |
| graveyard | 22.0 | 45.9 | 58.6 | 2.66x | 1.28x |
| courtyard | 21.8 | 45.6 | 60.2 | 2.77x | 1.32x |
| forest-edge | 20.8 | 44.3 | 57.0 | 2.74x | 1.29x |

- **Bar (monster chroma >= 1.5x the baseline's):** met against the epic's
  baseline, at 2.4-2.8x. Against the set just before this bead, which the
  grade (ps-e8r.2) had already lifted, it is 1.32-1.38x.
- **The cap on the dye:** a full dye (`--type-dye 1 --type-saturation 0.9`)
  reached only 1.40x the before set. Most monster pixels are on the shaded
  side, where chroma stays low whatever the albedo. And the slimes, mushrooms
  and demons were saturated already.
- **Readability:** |dL*| 31.2-38.6 in every beauty shot (before: 31.7-39.5),
  above the floor of 17. The dye keeps each texel's luminance, so dL* moves by
  under 1.
- **Song stills:** mean chroma +3.0 to +4.2 against before. RMS contrast is
  -0.5 to -1.2, because bone-white skeletons became rust at the same
  luminance, which trims the p98 highlights.

## On the newer staging

Before pushing, this bead was rebased onto staging `7219c8ed`, where the warm
ground (ps-e8r.6) and the music-reactive post (ps-e8r.18) had landed. The
beauty shots were then re-rendered (trial run, not kept) with the dye on and
off:

| shot | |dL*| dye off | dye on | monster C*ab off | on |
|---|---:|---:|---:|---:|
| opening | 25.8 | 24.9 | 44.6 | 60.7 |
| path | 27.8 | 27.2 | 42.0 | 57.1 |
| glowing-tree | 25.4 | 24.7 | 42.2 | 57.5 |
| graveyard | 27.4 | 26.9 | 44.1 | 57.9 |
| courtyard | 25.2 | 24.4 | 42.9 | 59.0 |
| forest-edge | 23.0 | 22.5 | 42.3 | 55.9 |

Every shot still passes the floor (17). The lighter ground lowers |dL*| for
every set, and the dye costs it under 1 L*.

## Cost

No texture sample; the epic allowed one small lookup and none is needed.
`TypeDyed` adds about 20 ALU operations per crowd fragment, and the YIQ
rotation about 15 more, for the two rotated types only (a uniform branch).
There is one more vector per type on the property blocks. It's unmeasured on
Cygnus (the operator's S0/S3 frame-time re-check, epic Decision 5).
