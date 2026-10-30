# Look set: env-desert, sun-bleached ruins at golden hour (ps-bop.3.3, 2026-10-30)

The desert environment (epic ps-bop): light and warm, the arena's area plan
in POLYGON Ancient Egypt's palms, limestone, obelisks, pillars and statues,
Ancient Empire's marble rubble and Western Frontier's cacti, desert trees and
adobe walls. Rendered by the look harness (IL2CPP player, Apple M5 Max,
Metal) at staging `d1d80269` plus this bead, with
`--player-args "--environment desert"`. The numbers are in [look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | [final](../final/README.md) (the forest) left, desert right, shot for shot |
| `song-*.jpg`, `<shot>-horde.jpg`, `<shot>-empty.jpg` | the set's stills |
| [reference/](reference/) | Ancient Egypt's own demo scene from our camera under the desert's lighting (TD15's reference stills) |

Run it again with:

```
uv run --project demo3d/tools demo3d look env-desert --baseline final --player-args "--environment desert"
```

## The kit

The layout is
[environment-desert.json](../../../Assets/Demo3D/Choreography/Data/environment-desert.json).
It keeps the arena's areas, goals and shots, and swaps every prop group and
every landmark's pieces.

- **Three packs, 119 pieces in the manifest:** Ancient Egypt 73, Western
  Frontier 30 and Ancient Empire 16. All three use the Standard shader, each
  on one atlas (plus Egypt's hieroglyph and mural materials on its obelisk
  and columns, and Empire's statue material), so the baker's converter turns
  them into URP Lit as it does the forest's packs. No custom shader is used, so nothing needed a Shader
  Graph port. Their FX materials (legacy particles) aren't used.
- **Ancient Egypt sits under `Assets/Synty/`,** so FootprintMeasurer now
  names a pack by the folder after `Synty` (`egypt/`). Without that, every
  Synty-folder piece would be `synty/...`. Ancient Empire's pieces are
  `empire/` and Western Frontier's `western/`. Two packs' pieces of the same
  name (both have `SM_Prop_Brazier_01`) keep apart.
- **Dark Fantasy's candles** (already synced) are the small flames on the
  graveyard and the fairy ring, as in snow. Egypt's braziers carry the big
  flames: the tall `SM_Prop_Brazier_03`, with its emissive coals insert.
  Western Frontier's campfire replaces the pyre.

Every piece's footprint is measured (footprints.json). The landmarks and
structures keep their places and their navigable shape:

| area | forest | desert |
|---|---|---|
| glowing-tree | the glowing elven tree | an obelisk (`SM_Bld_Obelisk_Medium_01`, 8.8 m at 1.2x) with a cyan glow light and pool, a limestone block, a broken column, a small statue |
| crystal-ring | six tombstones in a ring | six painted Egyptian columns, four broken and two whole |
| graveyard | tombs, statue, fence, candles | a royal tomb: two sarcophagi, three pharaoh statues, the black jackal (`SM_Prop_Statue_10`), a dead tree, an adobe wall with a column, candles, jars |
| ruins-gate | ruin walls with a gap | limestone and adobe walls with the gap, a broken giant column at one end and a 10.8 m whole one at the other |
| courtyard | ruin walls, four gaps | stacked limestone blocks and adobe walls, the same four 5 m gaps, columns at the corners |
| brazier-glade | four braziers, the pyre, a statue | four tall Egyptian braziers with glowing coals, a campfire, a statue, pots |
| opening-ruin | ruin and gargoyle | a ruined temple platform (`SM_Prop_Ruins_Base_02`) and a sphinx |
| fairy-ring and other dressing | flowers and stepping stones | canopic jars, broken statue pieces and desert grass, rubble, the same candles |

The forest fill is palms (both packs), acacia-like desert trees, dead trees
and tall cacti, scaled 0.6-0.85x: Egypt's palms are 10-15 m tall at 1x, and
their canopies, seen from our 55° camera, overhung the fighting ground at
full size. Western mesas (`SM_Env_RockTall`), Empire cliffs and Egypt rock
walls are the forest's rocks. The edge bands hold dead bushes, shrubs,
cacti, red sandstone boulders, limestone blocks, marble rubble and a few camp
props (pots, jars, baskets, a cart, a broken wagon, a skull pole). Clearings
carry pale rubble, small rocks and broken statue pieces.

The packs' big pieces (Egypt's 42 m sphinx, its 19.5 m obelisk, the dunes
and Western's hills, 33-63 m across) are not used: each is larger than a
landmark's place in the plan and would cover the fighting ground.

## The palette

Role by role, with where each colour lives. sRGB unless noted.

| role | colour | where |
|---|---|---|
| **Sand and shade** | | |
| fighting ground | pale bleached sand (0.93, 0.84, 0.68), path (0.89, 0.79, 0.63); L* 74-84 rendered | `ground.clearing`, `path` |
| edge bands, forest floor | warmer sand (0.84, 0.72, 0.55), (0.78, 0.65, 0.48) | `ground.edge`, `forest` |
| shadows | pale blue: a bright sky fill, trilight (0.80, 0.86, 1.0) / (0.80, 0.74, 0.66) / (0.74, 0.62, 0.48) | `lighting.ambient` |
| key light (the sun) | golden (1.0, 0.80, 0.58), intensity 0.64, 38° up (the arena's 42°) | `lighting.moon` |
| fog, mist | warm haze (0.80, 0.66, 0.50); dust (0.86, 0.74, 0.58) | `lighting.fog`, `atmosphere.mist` |
| frame edges | vignette 0.32, burnt umber (0.26, 0.14, 0.06) | `lighting.vignette` |
| grade | shadows a touch cool (0.93, 0.97, 1.06), midtones neutral, highlights golden (1.03, 0.99, 0.92); exposure 0.45, saturation 22 | `lighting.post` |
| kit | Egypt's limestone and painted columns at 0.85, Empire's marble 0.8, Western 0.9 | `lighting.kitValue` |
| **The horde** (decision 4: never recoloured) | | |
| its key | white (`keyColor`), 1.6x the sun (the forest's 3x), no extra ambient fill (the forest's 0.5) | `lighting.crowd` |
| rim | cool (0.30, 0.36, 0.50) | `lighting.crowd.rim` |
| type hues | unchanged | roster.json |
| **Warm accents** | | |
| hero's pool | peach (1.0, 0.84, 0.66), intensity 4 (the forest's 40) | `lighting.hero` |
| flames | orange: the four braziers, the campfire, candles (8 lights) | `lighting.flames` |
| **Cool accents** | | |
| obelisk glow | cyan (0.3, 0.85, 1.0), intensity 8, 14 m, a 6.5 m pool | `lighting.glow` |
| rune circles | cyan-blue (0.2-0.25, 0.65-0.75, 1.0), linear, intensity 0.1 | `atmosphere.runes` |
| **Effects** | unchanged: white → orange → red | effects.json |

## Findings

**Readability passes in every shot**, with the horde darker than the sand:

| shot | L* monster | L* ring | dL* | against the empty sand |
|---|---:|---:|---:|---:|
| opening | 42.0 | 73.8 | -31.8 | -36.1 |
| path | 39.6 | 64.6 | -24.9 | -33.3 |
| glowing-tree | 40.1 | 66.9 | -26.8 | -31.8 |
| graveyard | 36.7 | 54.7 | -18.0 | -30.9 |
| courtyard | 39.6 | 64.2 | -24.6 | -30.0 |
| forest-edge | 38.7 | 63.5 | -24.7 | -34.2 |

**What it took.** The palette swap's desert put a mid-value ground (L* 52)
under a horde at 43. Here the sand is pale enough that its rendered value
reaches L* 74-84, and the horde sits at 37-42. That gap needed four changes,
measured one at a time:

- **The sun's shadows.** The first lighting (a sun 32° up at 0.9, a dimmer
  sky, palms at full size) failed every shot (dL* -0.3 to -8.2). Long palm
  shadows lay across the clearings, and the horde's own shadows filled the
  ring around it: the ring sat 10-30 L* under the empty sand.
- **The sky fill does the work.** Golden hour's light here is mostly the blue
  sky, as snow's blue hour is. The sun is dimmed to 0.64 and the trilight
  ambient is raised to near-white blue, so shadows on the sand turn pale
  blue instead of brown. That lifted the ring from 37-50 to 58-74.
- **The sand itself is bleached** (0.93, 0.84, 0.68), close to the snow's
  value but warm, with the edge and forest floor only a step darker. A
  darker edge band, as in the forest, put the horde in the forest's shade
  against dark sand.
- **The horde gets no extra ambient fill** (0 against the forest's 0.5) and
  a 1.6x key. Its value is set mostly by the sky fill, so the key's multiple
  hardly moves it (1.9 → 1.6 changed its L* by 1). The fill was what kept
  the graveyard shot under the floor.
- **The sun is 38° up,** not lower. At 50° every shot passed too, but the
  scene went flat and white. Moving the sun from 40° to 50° moved the
  graveyard's dL* by only 0.7, so 38° keeps golden hour's long shadows at
  almost no cost.
- **The graveyard is the closest shot** (-18.0). Much of its horde stands
  among the palms east of the tomb, in their shade, with the darkest ring of
  any shot (L* 54.7).
- **The monsters keep their chroma:** mean C*ab 51-55, 0.91-1.0x the
  forest's.

**Which type hues fail, and why.** None falls below the floor as a mass,
since every type reads darker than the sand. By hue:

- **The skeleton (rust, 20) is the weak type.** It is the sand's own hue. On
  the pale sand of the groove sections it still reads, by value. In the
  drops the post feel warms and saturates the grade, so the sand turns
  golden-orange and the skeletons read as darker sand (`song-060.jpg`,
  `song-175.jpg`). Their shields and spears carry them.
- **The mushroom (ochre, 60)** is close to the sand too, but its yellow,
  spotted caps are lighter and more saturated than the sand. It reads
  better than the skeleton.
- **The cool types win.** The troll (azure, 220), the Big Ork (cyan, 180)
  and the bat (violet, 260) are the strongest masses in every still, because
  nothing on the set is cool except the shadows and the obelisk's glow. The
  goblin and slime greens and the demon's crimson read well. The dragon's
  magenta stands out at the final peak.

**Effects stay the brightest values** in every still: their L* p98 is
91.6-99.1 against the rest's 88.5-95.9. The margin is thin where the sand is
brightest, in the opening shot (97.1 against 94.5) and at the drop
(`song-060`, 91.6 against 91.0). Fire sits on warm ground: the hits read as
yellow-white blooms by value and size, not as orange against the set.
Nothing clips (0.0% in every still).

**The hero's pool.** Kept to intensity 4, against the forest's 40. On the
pale sand a stronger pool clips. The pool alone did not light a stage on
the sand, which is as bright at the frame's edge as under the hero: three
song stills failed the stage bar (dL* 7.9-14.2) at the first vignette
(0.26). A darker, warmer vignette (0.32, burnt umber) gives the stage its
contrast (dL* 15.2-25.4 in the song stills, 31.9-45.9 in the beauty shots).
At 0.38 the stage was stronger but the vignette darkened the horde at the
frame's edges (monster chroma 57 → 51).

**The cool share is 0-2.5%** of a song still and 1.7-3.5% of a beauty
shot, well under the 15% cap. The shadows' blue is too pale to count
(C*ab < 20). So the cool accents are the important ones here, as the bead
expected:

- the obelisk's cyan pool reads clearly on the sand;
- the rune circles show as pale cyan rings, reading as white on the lit
  sand and cyan in shade. Additive light on a light ground can only
  lighten, so they read by line, not colour;
- the trolls and Big Orks are the frame's real cool accent.

**Every shot shows a landmark or edge decoration** (the empty stills):

- opening: the fairy ring of jars and statue pieces, the sphinx and the temple platform at the top right;
- path: the column ring and the palms;
- glowing-tree: the obelisk;
- graveyard: the tomb, and the gate's limestone wall at the top;
- courtyard: the limestone and adobe walls and their columns;
- forest-edge: the palms and the obelisk.

No canopy covers fighting ground. The layout keeps the trees' measured
canopy radius off it, and the palms are scaled down so their crowns, seen
from our camera, stay over the edge bands. The canopy fade covers the rest.

**Against the reference.** Ancient Egypt's demo scene, seen from our camera,
is a walled town: limestone buildings with painted friezes, palms, carpets
and market props, on deeply saturated golden sand. Our arena is open
fighting ground by design, so it is sparser than the reference's streets.
Its dressing (palms, painted columns, limestone, statues, pots) is the
reference's. Its sand is the reference's hue at a much lighter value and
lower chroma. The reference's saturated orange sand would put the ground at
the skeletons' hue and near the horde's value, which is what the palette
swap's desert measured.

## S0 at the authored density (decision 8)

The Mac has no bench sweep, so this is the Mac's windowless stand-in for S0,
as in [env-snow](../env-snow/README.md#s0-at-the-authored-density-decision-8),
run for three environments with the same player:

- the empty arena at the authored density (E2, 2x): `--scene S3 --beauty-empty`;
- the hero held at the opening and the forest-edge shots, at a 40 m view;
- `--gpu-probe-at` at 12 times, each time three 1080p MSAA4 frames timed
  through GPU completion (CPU submission and GPU in series);
- four runs, 144 frames per row.

**The machine was loaded:** other sessions were baking and rendering, with a
load average of 16-41. The run-to-run medians spread by 2-5 ms, more than any
difference between the environments. So the table gives each run's median,
and the least-contended run is the fairest number. These are not Cygnus
numbers and not comparable to the 1.5 ms ceiling.

| environment | placements | triangles | opening run medians | forest-edge run medians | least contended |
|---|---:|---:|---|---|---|
| desert | 2,836 | 2,382,594 | 3.33, 6.43, 2.35, 6.04 ms | 3.72, 5.89, 2.74, 6.64 ms | 2.35 / 2.74 ms |
| forest | 2,582 | 804,264 | 3.38, 2.67, 2.32, 6.38 ms | 4.07, 2.35, 2.50, 7.45 ms | 2.32 / 2.35 ms |
| snow | 2,491 | 1,153,380 | 2.99, 2.43, 4.30, 6.20 ms | 2.57, 2.60, 7.57, 6.38 ms | 2.43 / 2.57 ms |

- **The desert has three times the forest's triangles** (2.38 M against
  0.80 M) for 10% more placements (not broken down per piece). At the
  least-contended runs that costs about 0.0-0.4 ms on the Mac's probe,
  within its noise.
- **CPU:** a batch-mode frame (`--stats`, no drawing) is 0.2 ms median in
  all three environments. The render thread's submission is inside the GPU
  probe's times. The operator's Cygnus re-check (plan.md, Pending on the
  operator) is where the real S0 GPU p99 and main- and render-thread times
  come from.

The windowless smoke check plays the whole song in the desert:
`--environment desert --fixed-time --seconds 199` exits 0 on
`Arena-desert-E2-2x` (2,853 kit renderers, 8 realtime lights).

## Self-review

- The horde reads against the ground at a glance: yes, darker on light,
  |dL*| 18-32.
- Every shot shows a landmark or edge decoration: yes (above).
- No canopy hides fighting ground: yes.
- Effect colours stand apart from the set: by value and bloom; by hue the
  fire is on its own ground, so it reads as yellow-white light.
- Nothing floats, clips or repeats in an obvious pattern: the ground's
  generated cracks read as dried mud on the sand (detail 2.5-3.5), and the
  red sandstone boulders and pebbles of the edge bands repeat in small
  clusters.
