# Look set: env-snow, the frozen fjord at blue hour (ps-bop.3.1, 2026-10-30)

The snow environment (epic ps-bop): light and cool, the arena's area plan in
POLYGON Vikings' snow variants. Rendered by the look harness (IL2CPP player,
Apple M5 Max, Metal) at staging `6b3fe279` plus this bead, with
`--player-args "--environment snow"`. The numbers are in [look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | [final](../final/README.md) (the forest) left, snow right, shot for shot |
| `song-*.jpg`, `<shot>-horde.jpg`, `<shot>-empty.jpg` | the set's stills |
| [reference/](reference/) | Vikings' own demo scene from our camera under the snow's lighting (TD15's reference stills) |

Run it again with:

```
uv run --project demo3d/tools demo3d look env-snow --baseline final --player-args "--environment snow"
```

## The kit

The layout is
[environment-snow.json](../../../Assets/Demo3D/Choreography/Data/environment-snow.json).
It keeps the arena's areas, goals and shots, and swaps every prop group and
every landmark's pieces.

- **POLYGON Vikings, its `Snow_Variant` prefabs** (49 pieces in the
  manifest): snowy pines, rocks and drifts, standing stones, a ship burial,
  wooden fences, skull poles, glaciers and ice. Its materials use the
  Standard shader on one atlas, and the snow is modelled geometry on the same
  atlas. So the baker's converter turns them into URP Lit as it does the
  forest's packs, with no custom shader.
- **Dark Fantasy** (already synced): braziers, the pyre and candles, for the
  warm flame lights. Vikings has no flame prop: its torch stick has no fire.
- **Fantasy Kingdom's rocks** (already synced): dark rock chunks poking
  through the forest's and edge bands' snow.
- **Viking Realm is not used.** Its materials use Synty's own
  `VikingRealmShader` Shader Graph, which projects snow from above and has
  triplanar subgraphs. Its `_MainTex` also points at the classic Vikings
  atlas's GUID, not its own textures. Using it would mean porting that shader
  first, and the classic pack's snow variants already fill every role.
- **The Snow Kit is not used.** It is a ski resort: skis, snowboards, lifts,
  a snowmobile, modern fences and a snowman. None of it belongs on a fjord.

Every piece's footprint is measured (footprints.json). The landmarks and
structures keep their places and their navigable shape:

| area | forest | snow |
|---|---|---|
| glowing-tree | the glowing elven tree | an ice spire (`SM_Env_Glacier_03`) with a pale cyan glow light, ice chunks around it |
| crystal-ring | six tombstones in a ring | six standing stones (`SM_Prop_Rock_Totem_01_Snow`) |
| graveyard | tombs, statue, fence, candles | a ship burial: broken boat, stone statue, standing stone, skull poles, fence, candles |
| ruins-gate | ruin walls with a gap | wooden fence with the gap, the village gate and a standing stone at its ends |
| courtyard | ruin walls, four gaps | wooden fence, the same four 5 m gaps, posts at the corners |
| brazier-glade | four braziers, the pyre, a statue | the same braziers and pyre, a stone statue, skull poles |
| opening-ruin | ruin and gargoyle | an iceberg (`SM_Env_Iceberg_01`) and a standing stone |
| fairy-ring and other dressing | flowers and stepping stones | skull poles, stakes and stones, the same candles; snowy tufts and ice |

The forest fill is snowy pines scaled 1.4-2x (the pack's pines are 2.5-9 m
tall at scale 1), with icebergs and Kingdom rocks. The edge bands hold rocks,
small pines, logs, tall snowy grass and a few camp props (barrels, crates, a
fish rack, a wagon, stakes).

## The palette

Role by role, with where each colour lives. sRGB unless noted.

| role | colour | where |
|---|---|---|
| **Snow and its shade** | | |
| fighting ground | near-white snow (0.85, 0.86, 0.88), path (0.78, 0.79, 0.81); L* 72-80 rendered | `ground.clearing`, `path` |
| edge bands, forest floor | blue-grey snow (0.70, 0.73, 0.78), (0.56, 0.61, 0.70) | `ground.edge`, `forest` |
| shadows | pale blue: the sky fill, trilight (0.70, 0.76, 0.90) / (0.56, 0.61, 0.72) / (0.46, 0.49, 0.56) | `lighting.ambient` |
| key light | near-neutral (0.95, 0.96, 1.0), intensity 0.65, the arena's 42° | `lighting.moon` |
| fog, mist | blue (0.42, 0.52, 0.68); mist (0.72, 0.80, 0.92) | `lighting.fog`, `atmosphere.mist` |
| frame edges | vignette 0.3, navy (0.12, 0.16, 0.26) | `lighting.vignette` |
| grade | shadows a touch cool (0.92, 0.97, 1.06), midtones and highlights neutral (the night warms its midtones); exposure 0.45 | `lighting.post` |
| kit | Vikings' atlas at value 0.9: brown wood, grey stone, white snow caps | `lighting.kitValue` |
| **The horde** (decision 4: never recoloured) | | |
| its key | white (`keyColor`), 2.6x the moon (the forest's 3x), ambient fill 0.2 (0.5) | `lighting.crowd` |
| rim | cool (0.30, 0.36, 0.48) | `lighting.crowd.rim` |
| type hues | unchanged | roster.json |
| **Warm accents** | | |
| hero's pool | peach (1.0, 0.86, 0.72), intensity 6 (the forest's 40) | `lighting.hero` |
| flames | orange: braziers, the pyre, candles (8 lights) | `lighting.flames` (the arena's) |
| **Cool accents** | | |
| ice spire glow | pale cyan (0.45, 0.8, 1.0), intensity 6, 14 m | `lighting.glow` |
| rune circles | ice blue (0.3, 0.6, 1.0) and nearby, linear | `atmosphere.runes` |
| **Effects** | unchanged: white → orange → red | effects.json |

## Findings

**Readability passes in every shot**, with the horde darker than the snow:

| shot | L* monster | L* ring | dL* | against the empty snow |
|---|---:|---:|---:|---:|
| opening | 46.0 | 70.2 | -24.2 | -33.2 |
| path | 44.1 | 61.2 | -17.2 | -27.7 |
| glowing-tree | 46.1 | 65.1 | -18.9 | -27.4 |
| graveyard | 44.7 | 64.7 | -20.0 | -33.5 |
| courtyard | 43.8 | 61.9 | -18.0 | -26.8 |
| forest-edge | 41.2 | 64.5 | -23.3 | -31.7 |

- **The horde's own shadows darken its ring by about 10 L*.** Against the
  empty snow the horde reads 27-34 L* darker, but the ground right around
  the monsters is in their shade. The path shot is closest to the floor:
  its ring takes the edge band's tree shade as well.
- **The first lighting failed the floor in every shot** (dL* -9.7 to -15.6).
  That was a brighter key, a dimmer sky and lower contrast: the horde sat
  near L* 50, close to the shaded snow. Blue hour is mostly skylight, so the
  final lighting dims the key to 0.65, which also darkens the horde because
  its key is a multiple of the moon. It gives the sky fill the light, so the
  shadows on the snow turn pale, cuts the horde's ambient fill to 0.2 and
  its key to 2.6x, and keeps the arena's contrast.
- **The monsters keep most of their chroma:** mean C*ab 48-55, 0.89-0.98x
  the forest's.

**Which type hues fail, and why.** None falls below the floor as a mass, but
two read weakly:

- **The troll (azure, 220)** reads pale lavender-blue under the white key.
  That is the snow's own shade hue, at the lit snow's value, so a troll
  standing on shaded snow nearly disappears; only its outline and rim
  separate it. This is the worst type here (`song-145.jpg`, `song-175.jpg`).
- **The Big Ork (cyan, 180)** holds better than expected. Its teal is
  darker and more saturated than the snow, though it still reads as part of
  the cool field rather than as an accent.
- **The warm types win.** Skeleton orange, demon crimson, goblin green,
  mushroom yellow and the dragon's magenta read as strong masses, because
  nothing on the set is warm except the flames and the hero's pool.

**Effects stay the brightest values** in every still: their L* p98 is 95.6-99.6
against the rest's 91.0-97.9. The margin is thin where the hero's pool lights
the snow (song-100: 98.5 against 97.9). Their white cores no longer stand out
by value, only by bloom and colour: on the snow the hits read as orange-pink
halos, not white flashes. Clipping stays small (song stills 0.0-1.4%, the
largest clipped blob 1.1%).

**The hero's pool.** Kept to intensity 6, against the forest's 40. The ground
within 6 m of the hero is L* 80-91 against the frame border's 54-69, which
passes the stage bar (dL* 18-23) without clipping. At intensity 10 the pool
reached L* 94 and clipped 2.6% of a still. The pool is the only warm light on
the fighting ground, so it reads as warm by hue as much as by value.

**The cool share is 20-44%**, far over the forest's 15% cap for accents. The
snow's shaded areas (blue fill, blue edge bands and forest floor) are cool
hue at C*ab > 20, so here the cool share measures the set, not the accents.
The intended accents (the ice spire's glow, the rune circles) barely register:

- additive runes don't show on white ground;
- the glow light is weak against sky-lit snow.

On a light set, the only cool accent that would read is a dark, saturated one.

**Every shot shows a landmark or edge decoration** (the empty stills):

- opening: the ring of skull poles and stakes with its candles, a standing stone and the iceberg's edge;
- path: the standing-stone ring and the edge band's pines;
- glowing-tree: the ice spire;
- graveyard: the ship burial;
- courtyard: the fence;
- forest-edge: the pines and the spire.

No canopy covers fighting ground: the layout keeps the pines' measured canopy
radius off it, and the canopy fade covers the band edges.

**Against the reference.** Vikings' demo scene, seen from our camera, is
sparser than our edge bands and forest: open snow with stands of pines,
fences and camp props. Its ground is a Terrain whose snow didn't render in
URP, so the reference stills' blue is the fog's clear colour. Our density
follows the arena's rules, so it is denser than the reference; its dressing
(snowy pines in stands, logs, fences, a boat) is the reference's.

## S0 at the authored density (decision 8)

The Mac has no bench sweep (the bench player refuses to sweep off Windows),
so this is the Mac's windowless stand-in for S0, run for all three
environments with the same player:

- the empty arena at the authored density (E2, 2x): `--scene S3 --beauty-empty`,
  no monsters, ambient effects only;
- the hero held at the opening shot and at the forest-edge shot, at a 40 m view;
- `--gpu-probe-at` hand-draws 1080p MSAA4 frames and times each through GPU
  completion. That is CPU submission and GPU in series, an upper bound on a
  windowed frame;
- two runs of 12 probe points, 72 frames per row.

These are not Cygnus numbers and not comparable to the 1.5 ms ceiling.

| environment | placements | triangles | opening p50 / p90 | forest-edge p50 / p90 |
|---|---:|---:|---:|---:|
| snow | 2,491 | 1,153,380 | 2.67 / 4.75 ms | 2.71 / 4.47 ms |
| forest | 2,582 | 804,264 | 2.31 / 3.74 ms | 2.51 / 4.04 ms |
| crypt (minimal) | 2,508 | 1,025,798 | 2.70 / 4.30 ms | 2.60 / 4.52 ms |

- **Snow costs about 0.2-0.4 ms more than the forest** at the median, level
  with the crypt, and within the probe's run-to-run spread (the forest's two
  forest-edge runs differed by 0.4 ms). The scene has 43% more triangles than
  the forest's for about the same number of placements.
- **CPU:** a batch-mode frame (`--stats`, no drawing) is 0.2 ms in every
  environment. The render thread's submission is inside the GPU probe's
  times above. The operator's Cygnus re-check (plan.md, Pending on the
  operator) is where the real S0 GPU p99 and main- and render-thread times
  come from.

## Self-review

- The horde reads against the ground at a glance: yes, darker on light,
  |dL*| 17-24.
- Every shot shows a landmark or edge decoration: yes (above).
- No canopy hides fighting ground: yes.
- Effect colours stand apart from the set: by hue and bloom. By value they
  are barely above the lit snow.
- Nothing floats, clips or repeats in an obvious pattern: the forest's pines
  repeat at the frame's edge (six models), and the ground's generated noise
  shows as a faint crackle pattern on the snow (detail 2.8-3.5).
