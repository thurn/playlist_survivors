# Look set: env-blossom, a cherry-blossom temple at dusk (ps-bop.3.9, 2026-10-30)

The blossom environment (epic ps-bop): the mid-value pink environment, the
arena's area plan in POLYGON Samurai's temple grounds, with Elven Realm's
white statues and lamp posts. Pale white-stone fighting ground under pink
cherry canopies, petal-pink edge bands, warm lanterns, a rose dusk. Rendered
by the look harness (IL2CPP player, Apple M5 Max, Metal) at staging
`073fa639` plus this bead, with `--player-args "--environment blossom"`. The
numbers are in [look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | [final](../final/README.md) (the forest) left, blossom right, shot for shot |
| `song-*.jpg`, `<shot>-horde.jpg`, `<shot>-empty.jpg` | the set's stills |
| [reference/](reference/) | Samurai's own demo scene (`Demo.unity`) from our camera under the blossom's lighting (TD15's reference stills) |

Run it again with:

```
uv run --project demo3d/tools demo3d look env-blossom --baseline final --player-args "--environment blossom"
```

## The kit

The layout is
[environment-blossom.json](../../../Assets/Demo3D/Choreography/Data/environment-blossom.json).
It keeps the arena's areas, goals and shots, and swaps every prop group and
every landmark's pieces.

- **Two packs, 61 pieces in the manifest:** Samurai 52 (the four cherry
  trees, bamboo stalks and groves, bushes, flowers, tulips and plants, rocks,
  boulders and zen rocks, stepping stones and a stone circle, stone and zen
  lanterns, stone braziers, the guardian lions, a gong, flags, pots, a rice
  basket, a bonsai, a bamboo fence, the stone wall block, the shrine's base
  and roof, the torii) and Elven Realm 9 (six statues, their plinth and two
  lamp posts). FootprintMeasurer names Samurai's `samurai/`.
- **No custom shaders.** Every Samurai piece and every Elven piece used is
  the Standard shader on one atlas, which the baker's converter turns into
  URP Lit; nothing needed a Shader Graph port. Two Elven pieces surveyed and
  left out had custom shaders: the fountain (its water) and the low brick
  wall (the triplanar rock shader on a brick material).
- **Surveyed and left out:** about 100 more candidates, rendered one by one
  from the game camera (a temporary editor script, not committed). The
  modular temples, houses and castle bases need several pieces stacked and
  show flat platforms from above; the big pagoda's measured footprint is only
  its doorstep, so monsters would walk through it; the bamboo fences read as
  sticks from our camera and Elven's walls are 0.2 m planes, invisible end
  on, so neither works as the courtyard; the paper lanterns and the bell hang
  from a pivot at their top.
- **Bamboo is costly.** A bamboo grove is 3-4.5 k triangles against a cherry
  tree's 0.5-2.7 k; at a quarter of the forest, the first bake had 1.9 M
  triangles. Groves are now accents (5% of the forest), the stalks in the edge
  bands.

Every piece's footprint is measured (footprints.json). The landmarks and
structures keep their places and their navigable shape:

| area | forest | blossom |
|---|---|---|
| glowing-tree | the glowing elven tree | the great cherry tree (Samurai's widest, 8.4 m tall) with a pink glow light and a 7 m pool, two stone lanterns, a zen lantern, an Elven statue |
| crystal-ring | six tombstones in a ring | six tall stone lanterns (2.5 m), each with a warm glow and pool |
| graveyard | tombs, statue, fence, candles | a shrine (the small temple base and its roof, 7.4 m), two guardian lions facing the goal, two lit lanterns, an offering bowl, a gong, flags, a bonsai, a cherry tree |
| ruins-gate | ruin walls with a gap | a low wall of stone blocks (1.3 m) with the gap, lit lanterns either side of it, a lion at the west end, an Elven statue on its plinth at the east |
| courtyard | ruin walls, four gaps | the same stone blocks and four 5 m gaps, Elven statues on plinths at the corners |
| brazier-glade | four braziers, the pyre, a statue | four stone braziers (the flames: warm, flickering), a gong, an Elven statue on its plinth |
| opening-ruin | ruin and gargoyle | a torii (scaled 1.5, 9.7 m wide), two red lions before it, a lit lantern, a cherry tree |
| fairy-ring and other dressing | flowers and stepping stones | Samurai's flowers and stepping stones; stepping stones for the path, a ring of them with a stone circle in the courtyard |
| rune circles | violet | faint rose and warm petal rings |

The forest fill is cherry trees (the three smaller of the four, 3.6-6.6 m)
with bamboo groves as accents and boulders as its rocks; the great cherry tree
is the landmark's alone, since a glow light goes with every placement of a
piece. The edge bands hold bushes, bamboo stalks, stone and zen lanterns, zen
rocks, rocks, pots, bonsai and short bamboo fences, with Elven statues and
lamp posts sparingly; flowers, tulips and plants as the soft layer. Fighting
ground carries flowers and the odd stepping stone, at about half the forest's
clutter.

- **The courtyard's walls are a row of blocks.** The stone block's measured
  footprint is a circle (it is nearly square), so a wall run lays one every
  3.5 m with a 0.9 m slot between them, narrower than any monster.
- **The torii can't stand in a gap.** Its two posts measure as one bar
  between them, which would close the ruin wall's gap, so it marks the
  opening instead, where monsters walk round it.
- The scene has 2,248 placements against the forest's 2,582, and twice its
  triangles (1.62 M against 0.80 M): the cherry trees and the remaining
  bamboo.

## The palette

Role by role, with where each colour lives. sRGB unless noted.

| role | colour | where |
|---|---|---|
| **The temple ground** | | |
| fighting ground | pale warm white stone (0.86, 0.82, 0.81), path (0.82, 0.77, 0.76); L* 61-72 rendered, empty | `ground.clearing`, `path` |
| edge bands | petal pink (0.82, 0.62, 0.7) | `ground.edge` |
| forest floor | shaded mauve-pink (0.7, 0.5, 0.58); with the trees, L* 25-47 rendered | `ground.forest` |
| key light on the set | rose white (1.0, 0.88, 0.86), intensity 0.7, the arena's 42° | `lighting.moon` |
| skylight | mauve trilight (0.7, 0.58, 0.66) / (0.64, 0.5, 0.56) / (0.5, 0.38, 0.42) | `lighting.ambient` |
| fog, mist | rose haze (0.66, 0.5, 0.6); mist (0.85, 0.66, 0.76) | `lighting.fog`, `atmosphere.mist` |
| frame edges | vignette 0.3, plum (0.22, 0.1, 0.18) | `lighting.vignette` |
| grade | shadows mauve (1.02, 0.95, 1.05), midtones neutral, highlights a touch warm (1.0, 0.98, 0.98); exposure 0.42, saturation 22 | `lighting.post` |
| kit | Samurai at 1.15 (its stone read dark grey on the white ground), Elven at 0.7 (its statues are near white) | `lighting.kitValue` |
| **Pink** (the dominant colour) | | |
| cherry canopies | the atlas's pink at mid value, the forest and the landmarks | Samurai's trees |
| great cherry tree's glow | pink (1.0, 0.55, 0.75), a 7 m pool | `lighting.glow` |
| petal rings | rose (1.0, 0.45, 0.6) at the opening and the courtyard, warm at the ring, linear | `atmosphere.runes` |
| **Warm accents** | | |
| lanterns | warm (1.0, 0.68, 0.38) glow and pools: the tall stone lanterns and the zen lanterns | `lighting.glow` |
| flames | four flickering stone braziers, orange (1.0, 0.52, 0.22) (4 realtime lights) | `lighting.flames` |
| hero's pool | warm white (1.0, 0.86, 0.76), intensity 6 (the forest's 40, on a light ground) | `lighting.hero` |
| **Cool accents** | | |
| Elven lamp posts | lilac white (0.82, 0.78, 1.0) glow and pools | `lighting.glow` |
| bamboo, bushes, roofs | the atlas's greens and the shrine's green tiles | Samurai's atlas |
| **The horde** (decision 4: never recoloured) | | |
| its key | white (`keyColor`), 1.5x the key on the set, fill 0.15 | `lighting.crowd` |
| rim | cool (0.32, 0.3, 0.42) | `lighting.crowd.rim` |
| type hues | unchanged | roster.json |
| **Effects** | unchanged: white → orange → red | effects.json |

## Findings

**What it took.** The palette swap's blossom put a flat pink ground at the
horde's own value. The first build here did the same with a mid-value pink
stone (albedo 0.72, L* 53-56 empty): every shot failed, at dL* -3 to +2.
The horde's own shadows darken the ground around it by 15 L*, so a ring at
mid value meets a horde at mid value. The fighting ground had to go nearly as
pale as the desert's sand (L* 61-72 empty), and the bead's darker paths were
the wrong way round: a dark stage under the hero's pool would sit below a
frame border of mid-value canopies, failing the stage bar. So the pink moved
off fighting ground, into the canopies, the edge bands and the forest floor,
and the horde reads darker on white stone.

**Readability passes in four of six shots**, with the horde darker than the
stone:

| shot | L* monster | L* ring | dL* | against the empty stone |
|---|---:|---:|---:|---:|
| opening | 36.7 | 54.4 | -17.7 | -31.5 |
| path | 35.9 | 55.1 | -19.2 | -31.4 |
| glowing-tree | 36.3 | 53.6 | -17.3 | -28.6 |
| graveyard | 33.5 | 48.3 | **-14.8** | -34.2 |
| courtyard | 36.4 | 54.8 | -18.4 | -23.9 |
| forest-edge | 34.1 | 47.2 | **-13.2** | -30.3 |

- **The two failures are where the horde meets the pink.** In the graveyard
  and the forest edge, a third of the horde stands in the edge bands and
  under the cherry trees, whose canopies and petal ground sit at L* 40-50,
  the horde's own value, and whose shade darkens the forest floor (the
  graveyard's outer ground is L* 26). Against the empty stone every shot
  clears the floor by 7-17; the ring is what drags.
- **The margin is the narrowest of any environment** (|dL*| 13-19, against
  the neon's 19-25 and the desert's 18-32): white stone is not as pale as
  snow or sand, since it has to sit under pink rather than be the colour.
- **What didn't help:** a softer dusk (key 0.55 with more skylight) lowered
  the stone more than the horde (every shot fell by 1-2); paler edge bands,
  a paler forest floor, lighter canopies (Samurai at 1.15) and a darker horde
  (its key 1.5, from 2.2) each won 1-3 L*.
- **The monsters keep most of their chroma:** mean C*ab 47-49, 0.83-0.89x
  the forest's, between the desert's (0.91-1.0x) and the neon's (0.67-0.78x).
  The darker key costs some; the warm rose bounce from the set keeps the rest.

**Which type hues fail, and why.** By hue, against the blossom:

- **The demon (crimson, 340) sinks into the pink.** Under the canopies and
  on the petal-pink edge bands a demon clump is a darker pink on pink
  (`forest-edge-horde.jpg`, left); on the white stone it reads. Its red death
  bursts, too, land on pink ground.
- **The dragon (magenta, 300) and the bat (violet, 260) read**, against the
  bead's expectation: on pale stone they are the darkest, most saturated
  things in the frame (`song-175.jpg`). They would fail on the canopies, but
  neither flies over them in the shots.
- **The greens flatter, as the bead expected:** the goblins (100) and the
  slimes (140) are the clearest masses in every still, complementary to the
  pink; the ochre mushrooms (60) and the troll (220) read on stone and pink
  alike.
- **The skeleton (rust, 20) is near the flowers' hue.** Samurai's flowers on
  the fighting ground are red-orange; at our camera they are small, but they
  are the same family as the skeletons and the fire. The clutter was halved
  for that.

**Effects stay the brightest values in all 11 stills**, but on a light
ground by thin margins: in the beauty shots their L* p98 is 88-98 against the
rest's 82-88, and in the song stills they lead by 9-17 L* except song-175
(88.0 against 87.5) and the opening shot (88.1 against 87.5). The rest's top
is the lit white stone, so a paler ground (0.9 albedo) tied the opening's
effects; 0.86 is the palest that keeps them on top. Fire does not read by
hue on the warm, pink set; its white cores and bloom carry it, as on the
desert's sand.

**The stage passes everywhere:** the white stone under the hero's pool is L*
72-88 against a border of 40-64, dL* 17.5-41. The song-025 still is the
closest (17.5), where the border is mostly stone too.

**The cool share is 0.2-6.5%** of a song still and 2.0-3.9% of a beauty
shot, the lowest of the environments with the desert: the lilac lamp posts,
the bats, trolls and Big Orks, and the horde's rim. Pink and magenta sit
outside the measure's cyan-to-violet band.

**Every shot shows a landmark or edge decoration** (the empty stills):

- opening: the fairy ring of flowers and stepping stones inside the rose
  petal ring;
- path: the ring of tall lanterns and their warm pools, the stepping stones,
  Elven lamp posts and cherry trees at the edge;
- glowing-tree: the great cherry tree in its pink pool, the lanterns;
- graveyard: the shrine, its lions and lanterns, the gong, the wall of
  blocks, cherry trees;
- courtyard: the walls of blocks, the statues, the stepping-stone ring;
- forest-edge: the cherry trees, bamboo, a zen rock, lanterns.

No canopy covers fighting ground except the landmarks': the scatter keeps the
trees' measured canopies off it, and the great cherry tree, like the forest's
glowing tree, spreads over the camp's edge, where the canopy fade covers it
near the hero.

**Against the reference.** Samurai's demo scene from our camera is a
village on green grass: temples and gates on stone terraces, walls and
fences, a zen garden, a torii, dense bamboo and a few cherry trees, some of
its trees autumn orange. It is denser and greener than our arena, which is
open fighting ground by design; our edge bands come close to its garden
edges. The demo's own ground is grass, not stone: the white ground is ours,
for readability, and the pink is the cherries', which the demo uses as
accents rather than as the forest.

## S0 at the authored density (decision 8)

The Mac has no bench sweep, so this is the Mac's windowless stand-in for S0,
as in [env-neon](../env-neon/README.md#s0-at-the-authored-density-decision-8):

- the empty arena at the authored density (E2, 2x): `--scene S3 --beauty-empty`;
- the hero held at the opening and the forest-edge shots, at a 40 m view;
- `--gpu-probe-at` at 12 times, each time three 1080p MSAA4 frames timed
  through GPU completion (CPU submission and GPU in series);
- four runs per environment, alternating, 144 frames per row, the same player.

**The machine was heavily loaded** (load average 29-31: other sessions were
baking and rendering), and one run in each column spread to 5.4 ms. These
are not Cygnus numbers and not comparable to the 1.5 ms ceiling.

| environment | placements | triangles | realtime lights | opening run medians | forest-edge run medians | opening p50 / p90 | forest-edge p50 / p90 |
|---|---:|---:|---:|---|---|---:|---:|
| blossom | 2,248 | 1,621,053 | 4 | 2.55, 3.30, 4.21, 5.39 ms | 2.10, 2.83, 2.61, 3.01 ms | 3.68 / 10.48 ms | 2.67 / 3.85 ms |
| forest | 2,582 | 804,264 | 8 | 3.67, 3.38, 5.58, 3.30 ms | 2.99, 2.24, 3.22, 2.13 ms | 3.69 / 6.76 ms | 2.56 / 3.53 ms |

- **The blossom costs what the forest does,** within the probe's noise
  (the medians match; the opening's p90 is one contended run), on twice the
  triangles and 13% fewer placements, half the realtime lights. Its glow
  lights (the lanterns, the lamp posts, the great tree) are baked, and their
  pools are one merged mesh of additive cards. The triangles are the
  re-check to watch on Cygnus, as with the desert's 2.4 M.
- **CPU:** a batch-mode frame (`--stats`, no drawing, 100 s of the song from
  40 s) is 0.6 ms median and 1.5 ms p90 in the blossom, 0.7 and 1.2 ms in the
  forest; the timeline's evaluation and the crowd are 0.15 and 0.14 ms median
  in the blossom, 0.18 and 0.16 ms in the forest. The render thread's
  submission is inside the GPU probe's times. The operator's Cygnus re-check
  (plan.md, Pending on the operator) is where the real S0 GPU p99 and main-
  and render-thread times come from.

The windowless smoke check plays the whole song in the blossom:
`--environment blossom --fixed-time --seconds 199` exits 0 on
`Arena-blossom-E2-2x` (2,248 kit renderers, 4 realtime lights), "done at song
time 199.00".

## Self-review

- The horde reads against the ground at a glance: on the white stone, yes,
  darker on light; where it stands in the pink edge bands and under the
  canopies, by hue more than by value (|dL*| 13-15 in two shots).
- Every shot shows a landmark or edge decoration: yes (above).
- No canopy hides fighting ground: yes, apart from the great cherry tree's
  fringe, which fades near the hero.
- Effect colours stand apart from the set: by value and bloom, thinly; by hue
  they share the flowers' and the lanterns' warm family.
- Nothing floats, clips or repeats in an obvious pattern: the walls' blocks
  repeat with even slots between them, and the stepping stones and flowers
  scatter in the same few shapes. The Elven statues stand on their plinths.
