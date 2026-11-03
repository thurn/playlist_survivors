# Rhythm Champions: Style Guide

## Introduction

Combat references were updated on 2026-11-02. The
[previous version](../archive/champions-dash-combat-2026-11-02/style.md) is archived.
The November 3 combat-feel decisions in [design.md](design.md) and
[game-feel-plan.md](game-feel-plan.md) supersede older color roles.
The numeric palette table and diagnostics describe the existing renderer; new pickup and
weapon treatments still need visual evaluation.

This is the guide to how Rhythm Champions looks. Every bead that draws
something reads it first: the monsters, the crowd, the world, structures,
weapon effects, the hero, the UI and the look pass.

Everything here is guidance under aesthetic judgment
([design.md, Judgment over rules](design.md#judgment-over-rules)). The
reference is the neon edge trailer, which looks good; when this guide and a
better-looking frame disagree, the frame wins.

It answers three questions:

- **What does the look aim for?** The trailer's look, and the habits it
  keeps ([design.md, The Look](design.md#the-look)).
- **Which colours?** The palette, `rhythm_champions/Content/palette.json`,
  and why each role has its colour.
- **How do we judge?** By looking: stills from `champions stills`,
  side-by-sides with the trailer's drawing (`--look trailer`), a blind
  critique by an agent that hasn't read the docs, and the operator's eye.
  `champions look` measures the stills against the guide values in
  [style-thresholds.json](style-thresholds.json). It's a diagnostic for
  finding problems, never a gate.

The short version:

- **Retro 3D neon wireframe**, synthwave-style: low-poly models of simple
  shapes, drawn as glowing edges on black (design.md, The aim).
- **Start from the trailer.** Change it only where the change looks better.
- **Light comes from things.** The floor is void; lines, trails, sparks and
  the grid emit.
- **Each frame has a key.** One hue family holds most of a frame's colour:
  at most four environment/body families, plus white and the semantic
  player-yellow/XP-green roles.
- **White is the hero's; grey is a wall's; yellow is player empowerment.**
  Ordinary attacks stay white, enemy hazards orange/red, XP filled green.
- **No monster outshines the hero.**

It replaces the [archived neon look's guide](../archive/design/neon-look-style.md),
whose study of Geometry Wars 3 and whose calibrations it keeps.

## Related Information

- [The design](design.md): The Look (the trailer, changing the look,
  monster meshes, judgment over rules, the palette, restraint) is the source
  of the visual direction here.
- [The plan](plan.md): Look Metrics, Rendering > The edge look.
- [style-thresholds.json](style-thresholds.json): the guide values
  `champions look` reports against, keyed as the table in
  [Diagnostics](#diagnostics).
- [The monster-mesh-design skill](../../.agents/skills/monster-mesh-design/SKILL.md):
  how each monster's mesh is designed and judged.
- The palette: `rhythm_champions/Content/palette.json`; its rules are checked
  by `rhythm_champions/tools/tests/test_palette.py` (the tools'
  `palette.problems`), its reader is `Champions.Render.Palette`.
- The tools: `rhythm_champions/tools/src/champions_tools/look.py`
  (`champions look`), `stills.py` (`champions stills`), `palette.py`.
- History: [the ion palette's gaps](../archive/design/ion-palette-gaps.md),
  the starting point for this palette; the
  [archived thresholds](../archive/design/neon-look-thresholds.json) and their
  calibration notes in the archived guide's Rules and Thresholds.

## Habits and Their Diagnostics

The habits the trailer keeps (design.md, Judgment over rules), and the
diagnostics that can point at a frame worth a second look. A diagnostic that
disagrees with a good-looking frame is wrong about that frame.

| Habit | Diagnostics |
| --- | --- |
| Light comes from things, not from the world | `floor_p50_max`, `floor_p90_max`, `floor_p99_max`, `background_light_max` |
| A frame tends to have a colour key | `key60_min`, `key60_median_min`, `families_max`, `palette_conformance_min` |
| White is the hero's; grey means a wall | `white_max`, `monster_white_max`; the palette's white and grey rules |
| A swarm reads as many figures with dark gaps between them | `swarm_separation_min`, `monster_form_min`, `readability_min` |
| Point lights sparingly, for effect | Restraint: `flash_frames_max`, `clipped_max`, `halo_impact_min` |
| No monster is brighter than the hero | `hero_margin_min`; the value ladder |

## The Value Ladder

Every element has a rung. Something above its role's rung is usually a
mistake, so look at it again; the frame decides.

| Rung | L* | What lives here |
| --- | --- | --- |
| void | 0-5 | the floor, the fog, the monsters' glass cores, empty space |
| ground light | 10-35 | the grid (resting 10-20, under the beat wave up to 35), structure fills (about 12: half the zone's `fill`) |
| enemy light | 45-82 | monsters' lines, structure edges (about 80: three times the zone's `edge`), enemy effects, boss telegraphs |
| hero light | 85-98 | the hero's lines, warnings at their peak, enemy shots |
| clipped white | 98-100 | impact frames and the blade's core only |

Consequences:

- **The floor never rises above ground light**, not even under a downbeat's
  ring. The wave settles within half a beat.
- **No light pools**: no point light paints the floor round the hero.
- **Monsters stop at L* 82**, under the hero's rung, so the hero wins on value.

## The Hue Budget

Hues are HSV degrees, as the look metrics measure them.

- **A family** is a 60-degree window. There are three enemy families:
  - **key** (the most numerous fodder: skeleton, slime, demon, bat);
  - **accent** (goblin, mushroom);
  - **counter** (big ork, troll, Fat Ogre).
- **Each family has two members**, 14-43 degrees apart: two types sharing a
  member are told apart by silhouette and size, as Geometry Wars tells its
  enemies apart by shape.
- **Families stay at least 50 degrees apart**, member to member.
- **The boss** (the dragon, its telegraphs) sits within 40 degrees of a
  counter member, so a boss shot adds no family.
- **The key**: the first zone's colours and the key family within 40 degrees
  of each other, so crowd, floor and grid share one hue and the opening frames
  key naturally.
- **Yellow and green have distinct gameplay roles**: yellow belongs to the
  player rhythm aura and empowered attacks, green to filled animated XP.
  Enemy hazards use orange/red; enemy bodies retain the trailer families.
  Near white spends no family. The former Break accent is unused.
- **White and grey**: every near-white role (L* 85+, C* 30 or less) is the
  hero's; every grey role (C* under 15, L* 10-85) is the wall's.

The environment and enemy bodies keep the restrained family budget above.
Player yellow and pickup green are intentional semantic additions; do not
remove them to satisfy an old palette-family diagnostic.

## The Palette

`rhythm_champions/Content/palette.json`. Hazard roles migrated to orange/red in
ps-ueo.3; enemy-body and zone colors remain unchanged. Every value is the colour as drawn
on screen; a shader that tonemaps or blooms aims its output at it.

### Enemies and the boss

| Role | Types | Hex | Hue | L* | C* |
| --- | --- | --- | ---: | ---: | ---: |
| `enemies.key.cyan` | skeleton, slime | `#05c9ff` | 193 | 75 | 44 |
| `enemies.key.blue` | demon, bat | `#457dff` | 222 | 55 | 74 |
| `enemies.accent.magenta` | goblin | `#ff38b6` | 322 | 59 | 84 |
| `enemies.accent.violet` | mushroom | `#d52eff` | 288 | 55 | 110 |
| `enemies.counter.orange` | big ork, Fat Ogre | `#ff6524` | 18 | 62 | 84 |
| `enemies.counter.amber` | troll | `#ffa629` | 35 | 75 | 76 |
| `boss.body` | dragon | `#ff2e17` | 6 | 55 | 97 |
| `boss.telegraph` | the bosses' telegraphs | `#ff0011` | 356 | 53 | 102 |

`types` maps each cast type (Content/enemies.json's ids) to its role; its
family follows enemies.json's `slot`, and the key's light types take the
lighter member.

### Roles

| Role | Hex | Hue | L* | C* | Used for |
| --- | --- | ---: | ---: | ---: | --- |
| `void.floor` | `#030408` | 228 | 1 | 2 | the floor |
| `void.fog` | `#05070d` | 225 | 2 | 3 | distance fog |
| `void.body` | `#08090d` | 228 | 2 | 2 | the edge look's dark glass cores (monsters, hero) |
| `hero.body` | `#f4f8ff` | 218 | 97 | 4 | the hero's lines |
| `hero.blade` | `#ffffff` | - | 100 | 0 | the blade's core, slashes' cores, sparks, impact flashes |
| `hero.trail` | `#ffffff` | - | 100 | 0 | the hero's trail: pure white, no longer in the key family |
| `hero.edge` | `#e4eeff` | 218 | 94 | 9 | the faint white-blue edge of the hero's slashes |
| `hero.hurt` | `#ff4040` | 0 | 57 | 84 | the hero taking a hit: danger's red, in the counter's reach |
| `hero.break` | `#fff0c8` | 44 | 95 | 21 | legacy palette key, unused by the revised combat; no Break bar or stagger flash |
| `warning` | `#ff5018` | 15 | 59 | 91 | orange/red elite telegraphs with dark shoulders and hatching |
| `enemyShot` | `#ff3218` | 7 | 56 | 96 | airborne red-orange spores/shots |
| `wall` | `#8c8c8c` | 0 | 58 | 0 | unbreakable structures (reserved; level 1 has none) |

### Zones

The floor's key (design.md, Zones): two cool, a violet, a dim warm, in this
order. Each zone colours the grid at rest (`line`, L* 10-20) and under the
wave (`pulse`, up to 35), and the structures' dim fill (`fill`, L* 10-25) and
their edges (`edge`, L* 40-60).

| Zone | Family | line | pulse | fill | edge | Hue |
| --- | --- | --- | --- | --- | --- | ---: |
| `cobalt` (the first, the key's) | cool | `#001f67` | `#1443b3` | `#172d60` | `#3370ff` | 222 |
| `teal` | cool | `#00323c` | `#005769` | `#003742` | `#0092af` | 190 |
| `violet` | violet | `#35135d` | `#612c9d` | `#3c2557` | `#a04cff` | 268 |
| `ember` (the dragon's) | warm | `#421f00` | `#7d3a00` | `#4d2909` | `#c95e00` | 28 |

### Why these colours

- **The trailer's families**: cyan-blue key, magenta-violet
  accent, amber-orange counter, a red boss in the counter's reach. The
  dragon's fight, on the warm zone with red telegraphs, keys warm.
- **Electric yellow rewards rhythm.** One on-beat dash gives the full
  three-second yellow aura and visibly enlarged attacks. Enemy threats move
  to orange/red, using dark shoulders, shape and timing to separate them
  from amber bodies. Filled green animated XP reads apart from both.
- **The first zone is the grid's colour**, not a separate near-copy of it
  (ion's cobalt zone changed nothing). The teal zone shares the cyan fodder's
  hue on purpose: the frame keys, and the monsters stand off the dim floor by
  value (readability), not hue.
- **The cyan is as strong as cyan gets.** At L* 75 cyan cannot pass C* 44
  (ion's gap 6); its light comes from the line's value instead.
- **The hero's trail is pure white**, out of the key family (ion's gap 4).
- **Roles for everything drawn** (ion's gap 5): warning, enemy shot, hero
  hurt, structure fill and edge per zone, the bodies' void. Pickups must
  read apart from enemy shots by shape and motion; their new treatment is
  judged by eye in a full fight.

## Restraint

The look is calm by default (design.md, Restraint):

- **A contrast hierarchy in every frame**: the hero and its blade first,
  monsters second, the world (grid, structures) last.
- **The grid is a quiet background**: lines at the bottom of ground light,
  brightening only under the beat wave and never past ground light.
- **Special moments** are the only events that flash the frame, shake the
  camera or bend the whole grid: boss entrances and deaths, major weapon-form
  impacts, mass kills and structure breaks. Routine empowered attacks and
  dash-destination explosions stay local. Their
  flash is gone within a quarter second and never whites out the frame
  (`clipped_impact_max`).
- **Point lights** only for a slam or a boss entrance, a few at once.

Boss entrances visibly shatter ordinary enemies in the fight area. Preserve
existing drops and structures, and prevent outside horde refill. Review the
clear in motion and subsequent small, deliberately timed reinforcements. Required boss waves must remain visible beneath
six weapons; shrink or dim overlapping friendly effects before obscuring a
telegraph. Weapon forms need visibly different actions, and enlargement on a
timed dash should be clear without whitening the frame.

The default live hero keeps its posed, faceted drawing in a foreground depth
range, over opaque spectral weapons and retained bodies. A small soft contrast
pocket quiets light behind it; warning marks draw after that pocket. Damage
still colours its glass/core red while the identifying edges remain white.
Spectral Sentinels do not inherit this foreground treatment. The trailer hero
retains its pinned shader and depth behavior.

Elite warnings have one stable outer boundary, with a dark shoulder separating
it from amber bodies. Sparse ground-aligned hatching and a dim interior mark
the dangerous side within 5 m of the hero, fading out by 8 m. Timing brightens
the boundary toward impact; a routine enemy slam never becomes a solid bright disc. Yellow is reserved
for player empowerment; enemy warning boundaries and fills use orange/red.
Boss red marks keep their traveling fill/front and collision shapes are
unchanged. Judge overlaps in ordered motion as well as stills; none of these
layers promises a collision-free route through an invulnerable stress horde.

## The Edge Look

Monsters, the hero and structures are drawn as the trailer drew them
(design.md, The reference: the trailer; plan.md, Rendering > The edge look):
every crease sharper than 24 degrees lit in the type's colour over a dark
glass core (`void.body`), with a fresnel rim.

- **`--look trailer`** draws exactly what the trailer drew: the vendor
  meshes and `MonsterEdge.hlsl` as ported, unchanged. Every change to the
  look is compared against it.
- **Monster meshes**: each monster's mesh is redesigned for the look with
  the `monster-mesh-design` skill and judged by eye against the trailer's
  drawing, at game scale, in a crowd and up close.
- **What to look for**: does each figure read as a retro 3D neon wireframe
  (design.md, The aim)? That means a low-poly model of simple shapes, its
  lines connected into closed facets and a closed silhouette, dark inside.
  Or does it break into lit patches and scratches?
  Does each type read as its figure at game scale?
  Does a crowd read as many figures, or as a coloured mass (the trailer's
  goblins)? Does the shatter pay off? Is the hero still the brightest thing?
- **Structures** (ps-6y2.30, [evidence](../../rhythm_champions/evidence/structures/README.md))
  draw the same lines in the zone's `edge` colour, at three times its value,
  over a flat fill at half the zone's `fill`: dark enough that the lines carry
  the shape, still the zone's colour, so a structure reads as a solid apart
  from the monsters' black glass. The first look, a fill at the zone's full
  `fill` (shaded by slope) under lines at its `edge`, read to a blind critic
  as "lit navy solids with weak lines", another renderer. Their shards close
  with every edge lit and fly hot.

The edge cuts that this replaced, and their black-share threshold, were
deleted on 2026-11-02 (design.md, The Look).

## Measuring the Look

### `champions stills`

Renders the scripted scenes in one headless launch of the last build (plan.md,
Look Metrics), into `captures/stills/` (ignored):

- `still-SCENE-T.png`: the frame as drawn, 1920x1080, PNG (thin lines keep
  their colour; JPEG would smear it).
- `id-SCENE-T.png`: its type-ID pass.
- `view-SCENE-T.json`: the camera's view-projection (row-major, OpenGL clip
  space), the hero's feet, whether it is an impact frame, and the ID table.

Scenes join as the systems they show land. Today:

- **`palette`**: minute 5's horde from level1.json (its density and mix),
  each monster a stand-in at its type's size in its type's colour (a
  near-black body whose silhouette glows, `Champions/StandIn`), round the
  hero, on the void, under the real post. It measures the palette as drawn
  until the trailer's edge look replaces the stand-ins.
- **`world`**: the world's scripted song (the beat wave at three phases, a
  zone sweep, beatless play, an on-beat dash's ring).
- **`gallery`**: every weapon effect of the hero's, fired in turn at its feet
  on the void, a still each (`gallery-LABEL`) and each one's draw and compute
  cost in `gallery-costs.json`
  ([evidence](../../rhythm_champions/evidence/effects/README.md)). Its frames
  hold only the grid and one effect, so `background_light_max` is judged on the
  assembly's full frames, not on these.

### The type-ID pass

How a still says what covers each pixel (`Champions.Render.IdPass`):

- `Stills` draws the camera a second time with the global float
  `_ChampionsIdPass` at 1, post, MSAA and HDR off, into a linear target.
- **Every Champions shader honours it**: include
  `Assets/Champions/Render/Shaders/IdPass.hlsl` and start the fragment with
  `CHAMPIONS_ID_PASS(_ChampionsId)`, which writes `_ChampionsId / 255` in red.
  A global float, not a keyword: it costs no variants.
- **Every drawer sets `_ChampionsId`** on its material or property block,
  once per type: a cast type's code is its index in enemies.json's cast plus
  one (`IdPass.Monster`); the hero 250, structures 251, effects 252 (slashes,
  rings, telegraphs, sparks, spores). 0 is nothing.
- The crowd's shaders honour it: its presenter sets each type's code, and
  the shatter's pieces draw as effects.
- A drawer that queues its draws by hand (`Graphics.RenderMeshIndirect`, as
  the crowd does) sets `Stills.Requeue`: a hand-drawn camera spends the draws
  queued in a frame, so the ID pass, a frame's second drawing, needs them
  queued again.
- The view json's `ids` table names each code: its type, its role (a cast
  role, or hero, structure, effect) and its height in metres.

### `champions look`

```sh
just champions-tool stills
just champions-tool look                        # captures/stills
just champions-tool look DIR --out rhythm_champions/evidence/look/NAME
```

- Checks the palette first (style.md's rules, as test_palette.py does).
- Measures every still: a table with every threshold key as a row and each
  still's value (blank: the still lacks what the measure needs; n/a: the rule
  judges other frames; "not judged": a fallback or a sparse still; "strays":
  off its guide value), the cut's rules, and where each still strays.
- `--out` writes metrics.md, metrics.json and a small ps-render-tagged JPEG
  per still with the dense swarm tiles outlined.
- A report, not a verdict: nothing is accepted or rejected on its numbers.
  It exits 0 however far the stills stray, and non-zero only when it can't
  measure: no stills, or a still, ID pass, view, palette or thresholds file
  it can't read.
- A frame without an ID pass (cut from a video) gets the frame rules alone.

## Diagnostics

The guide values below came from the archived look's calibration. They help
find frames worth looking at again; they don't decide anything.

Stills are measured at 960 px wide (ring widths and tiles are in those
pixels). Pixel classes:

- **colourful**: C*ab 30+ and L* 20+; **bright**: L* 50+; **emitter**: L* 70+;
- **clipped**: every channel 250+; **white**: C*ab under 15 and L* 85+;
- **lit** (a silhouette's lines and their glow): L* 20+;
- **floor**: the ground plane's pixels clear of everything the ID pass marks;
- **impact frames**: a special moment's frame and the next (the view json's
  `impact`, or `--impact GLOB`).

Value and hue rules judge steady frames; an impact frame is judged by
`clipped_impact_max` and `halo_impact_min`.

| Key | Rule | Threshold |
| --- | --- | --- |
| `floor_p50_max` | floor L* p50 | <= 8 |
| `floor_p90_max` | floor L* p90, rings included | <= 30 |
| `floor_p99_max` | floor L* p99: the grid's lines | <= 20 |
| `background_light_max` | share of the frame's linear luminance off everything the ID pass marks, under L* 70 | <= 0.15 |
| `frame_p25_max` | frame L* p25 (the floor's fallback without an ID pass) | <= 6 |
| `bright_share_max` | share at L* 50+ | <= 0.22 |
| `bright_share_min` | the same, unless monsters cover under 10% of the frame | >= 0.04 |
| `clipped_max` | clipped share | <= 0.02 |
| `clipped_impact_max` | clipped share on impact frames | <= 0.12 |
| `clipped_blob_max` | largest connected clipped region, of the frame | <= 0.005 |
| `white_max` | white share | <= 0.02 |
| `monster_white_max` | white share of the monsters' silhouettes | <= 0.01 |
| `halo_impact_min` | median L* 3-10 px round emitters less the frame's p25, on impact frames | >= 15 |
| `families_max` | greedy 60-degree windows each holding 8%+ of colourful pixels | <= 4 |
| `key60_min` | colourful share in the best 60-degree window | >= 0.55 |
| `key60_median_min` | the same, median over the set | >= 0.62 |
| `palette_conformance_min` | share of pixels at L* 40+ and C* 30+ within 20 degrees of a role's hue | >= 0.85 |
| `shot_floor_range_max` | spread of the stills' floor p50 across the set | <= 6 |
| `flash_frames_max` | share of the set's steady stills with an L* 90+ region over 0.3% of the frame | <= 0.5 |
| `hero_margin_min` | the hero's lit L* p90 less the monsters' | >= 5 |
| `monster_form_min` | monster body pixels' L* p90 less p10 per tile, the tiles' median | >= 30 |
| `swarm_separation_min` | seam depth in dense one-type clumps, the tiles' median; not judged under four dense tiles | >= 13 |
| `readability_min` | the monsters' lit L* p90 against the ground 3-10 px round them | >= 17 |

What changed from the archived rules:

- **One look**, so no presets: the edge preset's values stand.
- **The type-ID pass** replaces the old mask (monsters red, structures blue,
  effects green) and the hero's capsule: the hero, the floor and each type's
  clump are exact.
- **New**: `monster_white_max` (white is the hero's; uncalibrated until the
  real crowd lands).
- **The hero's margin and readability judge the lines** (lit pixels), not
  whole silhouettes.
- **Gone**: the frame's and each type's black share, which turned "empty
  space" into a target and rewarded deleting the lines that make a monster
  readable.

## Calibration

The guide values come from the archived look's calibration (Geometry Wars
3's stills and the crystal cut, ps-wnj.21-31). The look pass (ps-6y2.23)
judges level 1's real stills by eye against the trailer and may move a guide
value that misjudges good frames, recording why in
`rhythm_champions/evidence/look/`.

Drawn unlit at their palette values under the post, the monsters' lines
render within a few L* of their roles (the cyan at L* 79 C* 43 against the
role's 75 and 44), so the palette's values hold on screen.


## Combat-feel readability (2026-11-03)

The permanent held sword, white knight body and broad ordinary white slash
must read as one action. Supporting spectral weapons cannot replace the held
sword. Coordinate body impact, VFX, sound and forceful shatter; judge ordered
motion against the local trailer, not only stills. Dash exit is a full-circle
body sweep every time, white normally and yellow/enlarged during the aura.
The aura appears fully on one qualifying dash, refreshes without stacking and
fades cleanly at expiry. Keep its light local and telegraphs visible.

XP uses a filled green readable shape with an animated shader, preserving
stored value and persistent collection behavior. Healing uses a different
shape; enemy shots remain orange/red and airborne. Judge green visibility
against the grid and yellow effects at actual camera scale.

A bottom-left red liquid/energy health orb replaces the thin player health
bar. Its fill boundary must remain clear near empty and through bloom;
compact health numbers may support it. Top-center horde time switches to
BOSS plus fight elapsed time; boss health and beat cue remain legible. Inspect
pause/card holds and low health in motion. Subjective hand-play remains an
operator-pending check, not an agent claim of fun.
