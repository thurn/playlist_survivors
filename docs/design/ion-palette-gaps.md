# The ion Palette's Gaps

## Introduction

`ion` is the `edge` preset's palette
([ion.json](../../unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/ion.json)).
An agent wrote it on 2026-11-01 for the style guide bead (ps-wnj.1), keyed
on Geometry Wars 3's blue-cyan. No one chose it by eye, and it has not been
reviewed as a palette since.

This doc lists what `ion` leaves out or doubles up, so a redesign can start
from the gaps rather than from scratch. It does not pick the new palette.

The short version:

- **The arc from 40 to 185 degrees is empty**: no yellow, lime or green
  anywhere in the palette's enemies or zones.
- **Every zone is cool or pink**: the floor's key can never go warm.
- **Two zones repeat other roles' hues**: teal is the key fodder's hue,
  cobalt the grid's.
- **The hero's trail sits in the key family**, saved only by being on the
  chroma cap.
- **Some roles have no colour**: pickups, the hero being hurt, any warning
  short of a boss.

## Related Information

- [The neon look's style guide](neon-look-style.md): the value ladder, the
  hue budget and **The Palettes**, which any redesign has to satisfy.
- [The neon crystal look](neon-look.md): the palette format and its roles.
- [neon-look-thresholds.json](neon-look-thresholds.json): the rules the
  look metrics check, `key60_min`, `families_max` and
  `palette_conformance_min` among them.
- The other palettes, for contrast: `arcade`, `phosphor` (keyed green) and
  `spectrum`, next to `ion.json`.

## ion as Measured

Hue is HSV degrees; L* and C* are CIELAB (D65), as look.py measures them.

| role | hex | hue | L* | C* |
| --- | --- | ---: | ---: | ---: |
| `enemies[0]` key, light (skeleton, slime) | `#1fe2ff` | 188 | 83 | 43 |
| `enemies[1]` key, deep (demon, bat) | `#3a86ff` | 217 | 57 | 69 |
| `enemies[2]` accent (goblin) | `#ff2fa6` | 326 | 58 | 83 |
| `enemies[3]` accent (mushroom) | `#d24dff` | 285 | 58 | 100 |
| `enemies[4]` counter (bigork) | `#ff7a1a` | 25 | 66 | 83 |
| `enemies[5]` counter (troll) | `#ffb01f` | 39 | 78 | 78 |
| `boss.body` | `#ff3b2e` | 4 | 57 | 89 |
| `boss.telegraph` | `#ff1a3a` | 352 | 54 | 91 |
| `hero.body` | `#eef4ff` | 219 | 96 | 6 |
| `hero.blade` | `#ffffff` | - | 100 | 0 |
| `hero.trail` | `#8ff3ff` | 186 | 90 | 30 |
| `grid.line` | `#1b2f8f` | 230 | 24 | 62 |
| `grid.pulse` | `#4f7cff` | 225 | 55 | 75 |
| zone `cobalt` | `#1d3cff` | 232 | 39 | 114 |
| zone `teal` | `#00b2c8` | 187 | 67 | 37 |
| zone `violet` | `#6a2cff` | 258 | 41 | 118 |
| zone `rose` | `#ff2e88` | 334 | 57 | 80 |
| `shatter.gem` | `#fff0cc` | 42 | 95 | 19 |

Every saturated role sits between 186 and 39 degrees, going round through
blue, violet and red (`shatter.gem` at 42 is near white). The other 147
degrees hold nothing.

## The Gaps

### 1. No yellow, lime or green

- The enemy hues jump from the troll's amber (39) to the key's cyan (188).
- **Yellow** (about 50-65): missing. The troll's `#ffb01f` reads as
  amber-gold, not the electric yellow of the neon canon.
- **Lime and chartreuse** (about 75-100): missing.
- **Green and spring green** (about 100-160): missing. Geometry Wars 3 has
  green enemies (its dodging squares); the style guide's study measured
  how keyed GW3's frames are, not which hues appear, so it never flagged
  this.
- **Why it matters**: the crowded 60% of the wheel holds six enemies, two
  boss colours and four zones, so neighbours sit close (the two accents are
  41 degrees apart, the boss 21-33 degrees from the bigork's orange). A
  family in the empty arc would separate more from everything.

### 2. No warm zone

- Zones: cobalt 232, teal 187, violet 258, rose 334.
- The floor's key can be cool or pink, never amber, gold or red, so a
  "fire" or "dawn" shot is impossible in `ion`.
- The style guide bans a *lit* amber floor (v2's lava shots, bright share
  33-36%), which is about value. A dim warm grid at L* 8-18 breaks no rule.
- Any warm zone must still key the frame: the guide wants the first zone,
  the grid and `enemies[0..1]` within 40 degrees. A warm zone other than the
  first is allowed; it changes the floor's key for that shot only.

### 3. Zones that repeat other roles

- `teal` (187) is the key fodder's hue (188). On a teal shot the skeletons
  and slimes lose their floor contrast in hue and keep only value.
- `cobalt` (232) is nearly the grid's own hue (230), so switching to it
  changes almost nothing.
- Four zone names give about two and a half distinct floors.

### 4. The hero's trail is in the key family

- `hero.trail` `#8ff3ff` is hue 186, against the key's 188.
- It counts as white only because its C* is 30, exactly the guide's cap for
  near-white roles. A touch more saturation and the hero's trail reads as
  a key enemy's streak, and starts spending the key family.
- The guide's hero rule is that the hero wins on value and its trail; a
  trail that shares a hue with the commonest enemy works against that.

### 5. Roles with no colour

The palette's roles are the trailer's needs. Things the game may draw have
none, and the guide counts a bright colour far from every role as a bug
(`palette_conformance_min`), so anything drawn for them gets hard-coded:

- **Pickups** (XP, items): none. The trailer shows none; `game/` may.
- **Hero hurt**: no flash or tint for the hero taking a hit.
- **Warnings below the boss**: `boss.telegraph` is the only danger colour;
  an elite's wind-up or a hazard has no role.

### 6. The key light is weak as colour

Not a gap in hue but in strength. The style guide notes (ps-wnj.29) that
the key's light slot, `#1fe2ff` at C* 43, renders at C* 17-28 under the
looks' shading, mostly under the metrics' colourful cut (C* 30). The most
numerous fodder therefore contributes little to the key it is meant to
carry, and the crystal cut's mixes were staged around it. Cyan can't go
much deeper at L* 83.

## What Is Not a Gap

- **Red fodder**: red is kept for the boss on purpose.
- **Indigo** (about 230-280): the violet zone at 258 covers it.
- **Darker enemy values**: every enemy is on the enemy light rung
  (L* 45-82) by rule; the troll at 78 and the key light at 83 sit at its
  top, and nothing needs to sit lower.

## Constraints on a Redesign

Whatever replaces `ion` has to keep the style guide's structure:

- **Roles**: the same JSON roles as today, so every look still draws in it.
- **Families**: three enemy families (key `[0..1]`, accent `[2..3]`,
  counter `[4..5]`), each a 60-degree window, at least 50 degrees apart;
  slots in one family 14-43 degrees apart.
- **Boss**: inside one of the three families (within 40 degrees of a
  member), so a boss shot adds none.
- **Key**: the first zone, the grid and `enemies[0..1]` within 40 degrees
  of each other; `key60_min` 0.55 per frame and 0.62 median over a cut.
- **Values**: enemies L* 53-89 and C* 35-106 as specified (they render
  lower under shading); `hero.body` L* 96-100 and C* <= 8; `hero.trail`
  C* <= 30; `grid.line` L* 19-27 (drawn dimmer); `void.*` L* 1-4.
- **Distinct presets**: the four palettes differ by key. `phosphor` is
  already the green-keyed palette, so `ion` should take green as an accent
  or counter, not as its key.
- **Swaps are visible**: a slot's colour is a monster type's colour in every
  `edge` shot. Changing a slot means re-running the crystal cut and the
  game's look evidence.

## Directions

Starting points for the redesign, not decisions:

- **Green for magenta-violet.** Green becomes the accent family (goblin,
  mushroom), giving cyan, green and amber. It fills gap 1's centre and
  separates the families widely; it loses the magenta that reads most
  "neon" and that `spectrum` and `phosphor` also use.
- **Yellow for the troll.** Move `enemies[5]` from 39 toward 50-55 so the
  counter family spans orange to real yellow. The boss keeps the warm red.
- **A dim warm zone.** Replace `cobalt` (the near-copy of the grid) with an
  amber or gold zone, fixing gaps 2 and 3 together.
- **Move the trail off the key.** Shift `hero.trail` toward plain white or
  a hue no enemy uses, keeping C* under 30.
- **Name the missing roles** in the palette format (pickup, hero hurt,
  warning) once `game/` needs them, so they are drawn from the palette.
- **Borrow from a public palette.** Synthwave '84 (MIT) has the cyan,
  pink, orange, yellow, red and green this needs, at neon saturation;
  Dracula (MIT) has the same families, calmer. Take hues from one, then
  fit values to the rules above. Pull hexes from the theme's own repository,
  and keep its licence notice; this repository is public.
