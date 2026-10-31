# Look set: env-crypt, a grey stone crypt (ps-bop.3.11, 2026-10-30)

The crypt environment (epic ps-bop): the control, neutral grey with no hue.
The arena's area plan as a necropolis in POLYGON Dungeon's grey stone, with
POLYGON Knights' plinth tombs, gravestones and iron fences and Dark
Fantasy's dead trees, tombs, braziers and candles, on a grey stone ground at
the forest's own values. Only value separates things here, so it isolates
the roster's value range from its hues: a type that gets lost here has a
value problem, not a hue problem. Rendered by the look harness (IL2CPP
player, Apple M5 Max, Metal) at staging `2632ccf6` plus this bead, with
`--player-args "--environment crypt"`. The numbers are in [look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | [final](../final/README.md) (the forest) left, crypt right, shot for shot |
| `song-*.jpg`, `<shot>-horde.jpg`, `<shot>-empty.jpg` | the set's stills |
| [reference/](reference/) | POLYGON Dungeon's own demo scene (`Demo.unity`) from our camera under the crypt's lighting (TD15's reference stills) |

Run it again with:

```
uv run --project demo3d/tools demo3d look env-crypt --baseline final --player-args "--environment crypt"
```

## The kit

The layout is
[environment-crypt.json](../../../Assets/Demo3D/Choreography/Data/environment-crypt.json).
It keeps the arena's areas, goals and shots, and swaps every prop group and
every landmark's pieces. M1's minimal crypt (Dark Fantasy and Fantasy
Kingdom pieces already synced) is replaced whole.

- **Two new packs, 74 pieces in the manifest:** Dungeon 67 (round and
  square pillars, broken pillars and their rubble, balustrades and their
  posts, brick walls, statues, an altar, a stone throne, a lantern and a
  vessel, a mausoleum and a great gateway, rock piles, brick rubble,
  pebbles, bone piles, coffins, candles and candle stands, floor slabs) and
  Knights 7 (gravestones, the plinth tomb, iron fences, broken flagstones).
  Dark Fantasy's dead trees, tombs, tombstones, gargoyle, ruins, braziers,
  pyre and skull piles were already in the manifest. FootprintMeasurer names
  the packs `dungeon/` and `knights/`.
- **No custom shaders.** Every Dungeon piece is the Standard shader on one
  atlas (`Dungeon_Material_01`), every Knights piece likewise
  (`PolyKnights_Mat_01`); the baker's converter turns both into URP Lit.
  Dungeon's atlas has an emission map: the candles' flames glow yellow, and
  the pieces left out for it glow too (the rune pillars and rune tiles red
  and cyan, the pentagram red, the glowing orbs yellow).
- **Dungeon's stone is neutral grey,** which is why it carries the
  environment: the walls, pillars, statues and slabs have no visible hue in
  the stills. Knights' plinths, gravestones and fences are grey too.
- **Surveyed and left out:** about 120 more candidates, rendered one by one
  from the game camera (a temporary editor script, not committed): the full
  5 m dungeon walls (they would hide the horde behind them from 55°), the
  rubble mounds (a sand-coloured base), the hued pieces (the vases'
  terracotta, the chests' wood and paint, the Knights statue's orange, its
  trees' green canopies, its rock piles' sand), the rune and pentagram
  pieces (their emission is a colour), the ribcage (its emission makes it
  white).

Every piece's footprint is measured (footprints.json). The landmarks and
structures keep their places and their navigable shape:

| area | forest | crypt |
|---|---|---|
| glowing-tree | the glowing elven tree | a great hooded statue (Dungeon's 4.5 m statue at 1.3x) in a white glow light with a 6 m pool, two candle stands, a stone vessel, a tombstone, a ring of candles and bones |
| crystal-ring | six tombstones in a ring | six hooded statues facing in, candle clusters between them |
| graveyard | tombs, statue, fence, candles | a mausoleum (Dungeon's crypt entrance, 7 m), a coffin before it between two candle stands, Dark Fantasy's tombs, Knights gravestones and an iron fence, a dead tree |
| ruins-gate | ruin walls with a gap | balustrades and brick walls, two balustrade posts as the gateposts, a 7 m pillar at the west end and its broken twin at the east |
| courtyard | ruin walls, four gaps | the same balustrades and brick walls and four 5 m gaps, square pillars at the corners |
| brazier-glade | four braziers, the pyre, a statue | Dark Fantasy's four braziers and pyre (the flames: warm, flickering), Dungeon's altar and a statue |
| opening-ruin | ruin and gargoyle | a great ruined gateway (9 m), a gargoyle, a broken pillar, a fallen pillar |
| fairy-ring and other dressing | flowers and stepping stones | brick rubble, pebbles and bones in the opening's ring, broken flagstones, candles; a ring of floor slabs in the courtyard; a path of slabs |
| rune circles | violet | faint white |

The forest fill is dead trees, round and square pillars, the tall pillars,
plinth tombs, tombs, statues, rock piles and ruins; the edge bands hold
tombstones, gravestones, broken pillars and balustrade posts, short
balustrades, brick walls, coffins, fences, a lantern, a vessel, the odd
gargoyle and candle stand, with brick rubble, pebbles, rocks, bones and
candle clusters as the soft layer. Fighting ground carries brick rubble,
pebbles and the odd bone, and broken flagstones as dressing.

- **The ruins-gate's pieces are in world coordinates.** A ruin wall has no
  anchor, so its pieces' `at` is a ground position (environment.md, Adding
  an environment): the gateposts stand at x 12.4 and 19.6, either side of
  the gap at 13-19, leaving it 6 m.
- **No floor slabs in the scatter or under the hero.** Dungeon's 5 m slabs
  render darker than the stone ground at the kit's 0.7 value, so scattered
  over the clearings they made dark squares in an obvious grid, and a fully
  paved courtyard put the hero's stage under the bar (dL* 14 against the
  frame border). The courtyard keeps a ring of small slabs around bare stone,
  like the forest's stepping stones; the scatter keeps Knights' broken
  flagstones, which read as worn floor.
- **The mausoleums and the big plinth tombs are landmarks only.** In the
  forest fill they made the trailer's crypt shot 8 (a 3-stream converge at
  40 m) average 418 on screen against 500, under the trailer's 15%
  tolerance. Its count swings with any change to the crypt's blocking pieces
  (405-461 across the variants tried; M1's minimal crypt gave 461), so this
  is the layout that passes with a margin (456), not a rule; the trailer's
  formations beads (ps-hgs.5, ps-hgs.7) tune those shots.
- The scene has 2,463 placements against the forest's 2,582, and 1.74 times
  its triangles (1.40 M against 0.80 M): the pillars, statues and
  mausoleums.

## The palette

Role by role, with where each colour lives. sRGB unless noted.

| role | colour | where |
|---|---|---|
| **The crypt** (no hue) | | |
| fighting ground | grey stone (0.43), path (0.47): the forest's ground luminance (0.455) less its warm hero light and grade; L* 53-68 under the hero's pool, as the forest's 50-65 | `ground.clearing`, `path` |
| edge bands | dark grey (0.20) | `ground.edge` |
| forest floor | near-black grey (0.10) | `ground.forest` |
| key light on the set | white (1.0, 1.0, 1.0), intensity 1.0, the arena's 42° | `lighting.moon` |
| skylight | grey trilight (0.07) / (0.045) / (0.022) | `lighting.ambient` |
| fog, mist | grey (0.05); mist (0.36) | `lighting.fog`, `atmosphere.mist` |
| frame edges | vignette at the arena's 0.29, near-black grey (0.025) | `lighting.vignette` |
| grade | neutral: shadows, midtones and highlights (1, 1, 1); the arena's exposure, contrast and saturation | `lighting.post` |
| kit | Dungeon and Knights at 0.7, Dark Fantasy at the arena's 0.7 | `lighting.kitValue` |
| statue's glow | white (1.0, 1.0, 1.0), a 6 m pool | `lighting.glow` |
| rune circles | faint white (1.0, 1.0, 1.0) at 0.05 | `atmosphere.runes` |
| **Warm accents** (the only hue on the set) | | |
| candle stands | warm (1.0, 0.68, 0.38) glow and pools | `lighting.glow` |
| candles | Dungeon's emissive yellow flames, small | the atlas |
| flames | four flickering braziers and the pyre, orange (the arena's) (5 realtime lights) | `lighting.flames` |
| bones, coffins | the atlas's pale beige, small | Dungeon's and Dark Fantasy's atlases |
| hero's pool | white (1.0, 1.0, 1.0), intensity 32 (the forest's 40 warm white, at the same luminance on the ground) | `lighting.hero` |
| **Cool accents** | none on the set | |
| **The horde** (decision 4: never recoloured) | | |
| its key | white (`keyColor`), the arena's 3x the key on the set | `lighting.crowd` |
| rim | grey (0.27) | `lighting.crowd.rim` |
| type hues | unchanged | roster.json |
| **Effects** | unchanged: white → orange → red | effects.json |

## Findings

**What it took.** The crypt was built as the forest's value structure with
the hue taken out: the same ground luminance, the same key on the set and
on the horde, the same post stack with a neutral grade. The first build
used a white hero light at the forest's intensity, and the stage came out
6-8 L* brighter than the forest's (the forest's warm light and grade take
that much luminance off); a 0.43 ground and a hero light of 32 bring it to
the forest's numbers (song-060's stage 60.6 against the forest's 61.6).

**Readability passes in all six shots**, the horde lighter than the stone,
as in the forest:

| shot | L* monster | L* ring | dL* | forest dL* | against the empty stone |
|---|---:|---:|---:|---:|---:|
| opening | 48.5 | 22.9 | 25.7 | 21.6 | 11.3 |
| path | 46.5 | 21.2 | 25.3 | 24.3 | 9.4 |
| glowing-tree | 45.6 | 24.4 | 21.2 | 21.7 | 9.4 |
| graveyard | 47.8 | 20.4 | 27.4 | 23.7 | 12.1 |
| courtyard | 44.8 | 22.0 | 22.8 | 21.0 | 13.6 |
| forest-edge | 44.2 | 18.0 | 26.2 | 20.0 | 8.9 |

- **The roster's value range holds without its hues' help.** |dL*| 21-27,
  0-6 above the forest in every shot but the glowing tree (0.5 under). Hue
  was not carrying the forest's readability: the ring is the horde's own
  shadow on the stone, dark in both, and the monsters' key lifts them to L*
  44-49 against it. The crypt's ring is a little darker (no warm bounce),
  which is where its extra margin comes from.
- **Against the empty stone the margin is thin** (dL*bg 9-14, the forest's
  2.5-14): a monster in the open, away from the crowd's shadow, sits only
  about 10 L* above the lit grey. The neutral palette pass found the same
  (palettes.md, crypt 17.2-21.9 with the forest's kit).
- **Against the structures** the horde reads by 17.6-34 L* (the courtyard's
  grey walls the closest, 17.6, as they are the lightest kit).
- **The monsters keep all their chroma:** mean C*ab 54-59, 1.00-1.05x the
  forest's. With nothing coloured around them, they are the only colour in
  the frame (the mean C*ab of the song stills before the final peak falls
  to 9-19 from the forest's 38-43, all of it the horde and the effects).

**Which type hues fail, and why.** On grey only value separates, so this is
the roster's value ranking:

- **The bats (violet, 260) are the darkest type** and the one that sinks:
  over the black edge bands and the kit's shadows they read by saturation
  alone (`forest-edge-horde.jpg`, left). A value problem: the violet is dark
  at full saturation.
- **The demons (crimson, 340) are mid-dark** and read on the grey stone, but
  they share their value and hue with their own red death bursts, which
  swallow a demon clump in the drops (`song-145.jpg`). The skeletons (rust,
  20) are the same family as the fire, a step lighter.
- **The mushrooms (ochre, 60), goblins (100) and slimes (140) are the
  lightest,** the clearest masses in every still; the trolls (220) are light
  too. Nothing reads by hue against the set, as intended: there is no hue to
  read against.

**Effects stay the brightest values in all 11 stills**, but on mid-grey by a
thin margin in one: song-145 (75.1 against the rest's 74.7), where the
effects are the demons' red death bursts and the brightest rest is the lit
stone under the hero's pool. Red at full saturation tops out near L* 55, so a
red burst over grey stone at L* 60-70 darkens it rather than brightening it;
only the white-hot cores and bloom carry fire's value here. In an earlier
iteration of the same layout the margin was negative (73.4 against 75.3).
The forest's warm ground hides this, since the fire's hue stands apart from
it; on the grey control the effects are brightest by their cores alone.

**The stage passes everywhere:** the stone under the hero's pool is L*
53-68 against a border of 22-35, dL* 23-43.

**The cool share is 0.3-3.9%** of a song still and 3.2-4.3% of a beauty
shot, above the forest's 0.1-1.1%: on the grey set the bats, trolls, Big
Orks, the hero's spells and the horde's rim are the only cool pixels, and
none of them is the set's.

**Every shot shows a landmark or edge decoration** (the empty stills):

- opening: the ring of rubble, bones and candles inside the white rune ring,
  broken flagstones, the gargoyle at the edge (the gateway is out of frame,
  as the forest's ruin is);
- path: the ring of hooded statues, a round pillar, plinth tombs and dead
  trees at the edge, the slab path;
- glowing-tree: the great statue in its white pool, the candle stands;
- graveyard: the mausoleum, its coffin and candle stands, the balustrade of
  the ruins-gate behind;
- courtyard: the balustrades and brick walls, the corner pillars, the slab
  ring;
- forest-edge: the great statue and its candle stands, plinth tombs,
  tombstones, dead trees and rubble.

No canopy covers fighting ground: the dead trees' measured reach stays off it
like the forest's canopies, and the great statue overhangs only its own
landmark.

**Against the reference.** POLYGON Dungeon's demo scene, from our camera, is
an interior: tiled floors with sand-coloured grout, columns, walls and
ceilings, a few rubble mounds. Seen from above, the walls and ceilings cover
most of it (two of four framings were only roof or a sand cave, and are not
kept). Our crypt is outdoors by design (an open arena): it takes the scene's
grey stone, columns, statues and floor slabs and leaves its sand-coloured
mounds and grout, which would bring hue into the control.

## S0 at the authored density (decision 8)

The Mac has no bench sweep, so this is the Mac's windowless stand-in for S0,
as in [env-blossom](../env-blossom/README.md#s0-at-the-authored-density-decision-8):

- the empty arena at the authored density (E2, 2x): `--scene S3 --beauty-empty`;
- the hero held at the opening and the forest-edge shots, at a 40 m view;
- `--gpu-probe-at` at 12 times, each time three 1080p MSAA4 frames timed
  through GPU completion (CPU submission and GPU in series);
- four runs per environment, alternating, 144 frames per row, the same player.

The machine was lightly loaded this time (load average 4-7). These are not
Cygnus numbers and not comparable to the 1.5 ms ceiling.

| environment | placements | triangles | realtime lights | opening run medians | forest-edge run medians | opening p50 / p90 | forest-edge p50 / p90 |
|---|---:|---:|---:|---|---|---:|---:|
| crypt | 2,463 | 1,395,861 | 5 | 2.29, 1.50, 1.50, 1.49 ms | 3.24, 2.02, 1.65, 1.58 ms | 1.59 / 2.63 ms | 1.98 / 3.49 ms |
| forest | 2,582 | 804,264 | 8 | 1.62, 1.62, 1.58, 2.34 ms | 1.73, 1.67, 1.77, 1.72 ms | 1.66 / 2.69 ms | 1.69 / 2.72 ms |

- **The crypt costs what the forest does** at the opening, and about 0.3 ms
  more at the forest edge at the median, most of it its first run (3.24 ms;
  the last three, 1.58-2.02 ms, are level with the forest's). It has 1.74x
  the triangles on 5% fewer placements and five realtime lights to the
  forest's eight. The triangles are the re-check to watch on Cygnus, as
  with the blossom's 1.62 M and the desert's 2.4 M.
- **CPU:** a batch-mode frame (`--stats`, no drawing, 140 s of the song from
  40 s) is 0.4 ms median and 0.6 ms p90 in both; the timeline's evaluation
  and the crowd are 0.11 and 0.10 ms median in the crypt, 0.12 and 0.11 ms in
  the forest. The render thread's submission is inside the GPU probe's
  times. The operator's Cygnus re-check (plan.md, Pending on the operator) is
  where the real S0 GPU p99 and main- and render-thread times come from.

The windowless smoke check plays the whole song in the crypt:
`--environment crypt --fixed-time --seconds 199` exits 0 on
`Arena-crypt-E2-2x` (2,562 kit renderers, 5 realtime lights), "done at song
time 199.00".

## Self-review

- The horde reads against the ground at a glance: yes, lighter on grey,
  |dL*| 21-27, by value alone; the bats are the type to watch, dark on the
  dark edges.
- Every shot shows a landmark or edge decoration: yes (above).
- No canopy hides fighting ground: yes.
- Effect colours stand apart from the set: by hue entirely (the set has
  none); by value thinly where the bursts are red (song-145).
- Nothing floats, clips or repeats in an obvious pattern: the courtyard's
  slab ring and the path's slabs are regular by design; the balustrades
  repeat in 5 m lengths along the walls; the opening's rubble ring is the
  same few pieces round a circle.
