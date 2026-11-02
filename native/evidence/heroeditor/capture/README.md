# HeroEditor capture (ps-4c8.1)

herocap's first capture of every recipe: the HeroEditor knight and the undead
roster, as transparent PNG frames in the machine's cache
(docs/archive/design/heroeditor-native.md). These are small tagged renders of it; the
frames themselves never enter the tree.

- `<atlas>.png`: a contact sheet per recipe, one row per clip, up to 12 of its
  frames evenly spaced at half size, the impact frame boxed in amber.
- `faces.png`: the face lineup, an idle head close-up per recipe. Every face is
  dots and lines (decision 7): the hero's two dots, short line and thin brows;
  the undead's two soft glows, tinted per role, and no mouth.
- `budget.json`: frames and padded pixels per atlas, and the 4096² pages they
  need.

## Commands

From a clean machine with `~/unity_assets` (the `more_assets` root):

```sh
just herocap                       # herocap setup if needed, then capture every recipe (~25 s)
just herocap --recipe orc --clip walk   # one recipe's one clip
just herocap-tool evidence         # this folder: contact sheets, faces.png, budget.json
```

Output: `~/Library/Caches/playlist-survivors/demo3d/heroeditor/`, with
`frames/<atlas>/<clip>_<n>.png` and `clips.json` per atlas, and
`faces/<atlas>.png`.

## Recipes

Skin tints multiply HeroEditor's white body. Undead eyes are
`Common.Undead.Eyes.GlowingEyes` tinted as listed; the hero's are
`Common.Emoji.Eyes.Blank` with an `Expressions/Normal` mouth and `Bored` brows.

| atlas | role | scale | gear | skin | eyes | clips | frames |
|---|---|---:|---|---|---|---|---:|
| bat | small hooded wraith with a cape (flyer: native's hover), violet glow | 0.7 | DwellerHood, AssassinArmor, AssassinDagger, VampireCape | #8C9AA8 | glow #C07CFF | idle, walk, hurt, death-back, slash, victory | 121 |
| bee | pale hooded ghost, fast, pale yellow glow | 0.8 | HermitHood, Hermit | #C8D0D8 | glow #FFF3A0 | idle, walk, hurt, death-front, jab, victory | 121 |
| brute | big revenant knight with a two-handed hammer, red glow | 1.4 | DarkKnight, TitanHammer | #8C9AA8 | glow #FF3B3B | idle, walk, hurt, death-back, slash2h, victory | 126 |
| goblin | the fallback: a zombie peasant | 1.0 | PeasantClothing, AspenStake | #8FA38A | glow #A0FFB0 | idle, walk, hurt, death-back, slash, victory | 121 |
| hero | a sword-and-shield knight, open face, short hair | 1.0 | KnightArmor, EliteKnightSword, BlueKnightShield | #EBC3A0 | dots | idle, walk, run, jump, slash, jab, slash2h, cast, hurt, death-back | 186 |
| minotaur | the boss: a death lord in Thrones armour with a two-handed sword, crimson glow | 1.6 | DarkMountain, MountainSword | #7D8C8A | glow #FF1A4A | idle, walk, hurt, death-back, slash2h, jab2h, victory | 147 |
| orc-elite | iron-helmed Viking elite with a shield, amber glow | 1.05 | VikingIronHelm2, VikingFurArmor, VikingSword1, IronShield1 | #8C9AA8 | glow #FFB43C | idle, walk, hurt, death-back, slash, victory | 121 |
| orc-ember | burning SandLords warrior, ember-tinted, orange glow | 1.0 | SandWarriorHelm1, SandWarrior1, SandWarriorSword1 | #B07A5A | glow #FF7A2A | idle, walk, hurt, death-back, slash, victory | 121 |
| orc | zombie Viking in rough armour with an axe, green glow | 1.0 | VikingRoughArmor1, VikingAxe1 | #8FA38A | glow #7CFF6B | idle, walk, hurt, death-back, slash, victory | 121 |
| skeleton | ragged zombie militiaman with a short blade, cyan glow | 1.0 | Slave, Machete | #8FA38A | glow #5FF3FF | idle, walk, hurt, death-back, slash, victory | 121 |
| slime | hunched swamp ghoul in SwampLords rags, unarmed, green glow | 0.9 | SwampHealer1 | #7E9A6E | glow #7CFF6B | idle, walk, hurt, death-front, jab, victory | 121 |
| thorn-plant-autumn | rooted shaman, hooded and masked, with a staff, yellow-green glow | 1.0 | DruidHat, ShamanArmor, SwampMage2Staff, ClothMask | #A08A5E | glow #B4FF4A | idle, walk, hurt, death-front, cast, victory | 121 |
| thorn-plant-summer | rooted shaman, hooded and masked, with a staff, green glow | 1.0 | DruidHat, ShamanArmor, SwampMage1Staff, ClothMask | #7E9A6E | glow #7CFF6B | idle, walk, hurt, death-front, cast, victory | 121 |

These are the survivors run's atlases (content/survivors-enemies.yaml) plus
the hero and goblin, native's fallback look. The Can-Can roster's eyeball,
skeleton-archer and skeleton-ember atlases have no recipe: the survivors run
doesn't use them.

`victory` (arms raised, a 1 s loop) is the short rhythmic upper-body clip for
native's dancers' `kick` and `kick-alt`; the atlas bead picks the split.

## Frame budget

At 30 fps and 96 px for the hero standing (twice the LPC hero's 48), at 2x
supersampling:

- 1669 frames, 20.8 megapixels with 2 px padding.
- **2 pages** of 4096² for the frames, **3** with native's white hit-flash
  twins (10% packing allowance).
- Native allows 8 pages (render/src/sprites.rs, MAX_PAGES); the swapped-out LPC
  character page frees one. 45 fps would need about 5 pages with flash twins
  and 60 fps about 6, so 30 fps fits with room; more frames per clip is the
  feel pass's call.

## What it showed

- Deterministic sampling works: both Animator layers played at one normalized
  time and evaluated by hand give clean, repeatable frames, and the whole
  roster captures in about 25 s.
- HeroEditor's sprite bounds include their transparent margins, so the scale
  is measured from a render (the hero idle, 96 px), and the feet anchor is the
  Shadow sprite's position.
- `Character.FromJson` resets a part's colour to white without a tint, so the
  hero's skin is tinted explicitly.
- The knight's sword and shield hang in front of his legs in HeroEditor's idle
  and walk, so his steps read less than the horde's: for the feel pass.
