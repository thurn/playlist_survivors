# Look set: env-neon, a city at night (ps-bop.3.7, 2026-10-30)

The neon environment (epic ps-bop): very dark and cool, the arena's area
plan in POLYGON CyberCity's and SciFi City's streets, on a near-black wet
street lit by magenta and cyan signs, windows and pools. Rendered by the look
harness (IL2CPP player, Apple M5 Max, Metal) at staging `72da792e` plus this
bead, with `--player-args "--environment neon"`. The numbers are in
[look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | [final](../final/README.md) (the forest) left, neon right, shot for shot |
| `song-*.jpg`, `<shot>-horde.jpg`, `<shot>-empty.jpg` | the set's stills |
| [reference/](reference/) | SciFi City's own demo scene (`Demo.unity`) from our camera under the neon's lighting (TD15's reference stills) |

Run it again with:

```
uv run --project demo3d/tools demo3d look env-neon --baseline final --player-args "--environment neon"
```

## The kit

The layout is
[environment-neon.json](../../../Assets/Demo3D/Choreography/Data/environment-neon.json).
It keeps the arena's areas, goals and shots, and swaps every prop group and
every landmark's pieces.

- **Two packs, 145 pieces in the manifest:** CyberCity 80 (shopfront and
  window walls, crates, bins, skips, vending and claw machines, street
  consoles, hover lights, holo planters, glowing manholes, puddles, litter,
  junk piles, street-light pylons, poles, neon signs, the hologram cherry
  trees, the food truck, trailer and parked vehicles) and SciFi City 65 (its
  lit low-rise blocks, shacks, power units and food stall, pillars and power
  poles, trees, dumpsters, trash bags, AC units, ATMs, market stalls, lights
  and paper lanterns, billboards, ad panels, its neon martini, the taxi, cop
  car and other vehicles). Polygonal Aliens is not used: the two city packs
  fill every role. FootprintMeasurer names them `cyber/` and `scifi/`.
- **Their shaders.** Both packs' atlases, billboards, signs and walls use the
  Standard shader with emission maps, which the baker's converter turns into
  URP Lit. Two custom shaders are on pieces we use:
  - CyberCity's hologram (`SyntyStudios_Hologram_01`, an Amplify surface
    shader: its trees and planters). It is ported as our own
    `Demo3D/KitHologram`: the neon colour through the emission mask, scan
    lines scrolling up the piece in world height, additive, unlit, no depth
    write (the original faded alpha at 0.08 opacity was the same light on
    black). The converter maps its properties (`MaterialConverter.Hologram`).
    It is a hand-written URP shader, as the forest's ported triplanar rock
    shader is.
  - SciFi City's neon signs (`SyntyStudios/Polygon_UVScroll`) scroll a band
    texture through their tubes. The converter reads its `_Diffuse` and
    `_Emission` maps into URP Lit, so the sign glows in one frozen band
    colour. That is enough for the martini (magenta). The neon dragon froze
    on a dark band and read as grey tubes, so the courtyard carries a
    billboard instead.
- **Signs need height.** The packs' signs are modelled about their centre,
  for walls. A landmark's or structure's piece now takes `liftM` (ArenaLayout's
  `PieceSpec.LiftM`, carried on the placement, tested by
  `APiecesLiftRaisesItsPlacementOnly`), which puts the billboards on their
  base, the martini at head height and the courtyard's billboard on top of
  its north wall.
- **Emission needs a scale.** The atlases' emission (0.5-0.75 white) is
  below bloom's threshold on a dark set. `lighting.kitEmission` scales a
  converted material's emission by vendor name prefix, as `kitValue` does
  its base colour: the atlases 2.6-3x, the signs and billboards 2-2.4x, the
  hologram 0.2x (additive layers of foliage add up).
- **The wet street.** `ground.smoothness` (0.45; 0.12 elsewhere) gives the
  dark ground a sheen under the hero's pool and the glow lights, with the
  pack's puddles as dressing.

Every piece's footprint is measured (footprints.json). The landmarks and
structures keep their places and their navigable shape:

| area | forest | neon |
|---|---|---|
| glowing-tree | the glowing elven tree | a hologram cherry tree (6.6 m) and a smaller one, magenta, with a magenta glow light and a 7 m pool, two holo planters and a holo stand |
| crystal-ring | six tombstones in a ring | six street-light pylons (3.3 m), alternating cyan and magenta, each with its glow and pool |
| graveyard | tombs, statue, fence, candles | a night market: stall covers over tables of goods, market lights (an orange glow), paper lanterns hung at 2.4 m, the food trailer, a bench, the neon martini |
| ruins-gate | ruin walls with a gap | shopfront and window walls (3 m, their cyan trims and shop signs glowing) with the gap, a sci-fi pillar, a billboard on its base, an ad panel and a neon sign |
| courtyard | ruin walls, four gaps | the same shopfront walls and four 5 m gaps, pillars at the corners, a billboard on the north wall's roof, a neon sign and two ad panels on the walls |
| brazier-glade | four braziers, the pyre, a statue | a vending corner: four hover lights (the flames: two magenta, two cyan, flickering), a claw machine, two vending machines, a holo stand |
| opening-ruin | ruin and gargoyle | the food truck (with its cyan glow), a cart, a hover bike and a billboard |
| fairy-ring and other dressing | flowers and stepping stones | glowing manholes, puddles and floor lights; manholes for the stepping stones |
| rune circles | violet | magenta at the opening, cyan in the courtyard and the pylon ring: holo pads |

The forest fill is the city's low-rise: SciFi City's lit blocks (5-6 m, rows
of windows) at twice the weight of anything else, shacks, power units, the
food stall, parked vehicles, pillars, street and power poles, a few trees and
antennas, with greeble cubes, junk piles, dumpsters and skips as its rocks.
The edge bands hold crates, bins, trash bags, AC units, consoles, hydrants,
benches, barriers, drums and robot piles, with vending machines, hover
lights, holo planters, ad pillars, ATMs and charging stations as the lit
pieces; litter, rubbish, junk and robot parts as the soft layer; puddles,
paper and the odd glowing manhole as dressing. Clearings carry litter, cans,
rubble and puddles.

- **The forest is 5 m apart** (the forest's 4.5 m), since a city block is
  wider than a tree. The scene has 1,940 placements against the forest's
  2,582, and about the same triangles (0.79 M against 0.80 M).
- **Not used:** CyberCity's 15 m modular blocks and 50-60 m background towers
  (they would rise above the camera at our 35 m height and hide the fighting
  ground), SciFi City's modular section walls and the city wall (flat grey
  roofs from above), the cardboard boxes (their orange outshone everything
  cool), and the wall-mounted pieces that only face one way (the pack's
  street lamps and flat neon signs, invisible from behind).

## The palette

Role by role, with where each colour lives. sRGB unless noted.

| role | colour | where |
|---|---|---|
| **The street** | | |
| fighting ground | near-black blue asphalt (0.12, 0.13, 0.17), path (0.14, 0.15, 0.19), smoothness 0.45; L* 2-9 rendered, empty | `ground.clearing`, `path`, `smoothness` |
| edge bands, forest floor | black (0.045, 0.045, 0.06), (0.03, 0.03, 0.04); L* 1-3.3 rendered | `ground.edge`, `forest` |
| key light on the set | dim blue (0.6, 0.7, 1.0), intensity 0.5, the arena's 42° | `lighting.moon` |
| shadows | blue-violet trilight (0.025, 0.03, 0.07) / (0.02, 0.02, 0.05) / (0.03, 0.012, 0.04) | `lighting.ambient` |
| fog, mist | deep blue (0.015, 0.02, 0.06); violet haze (0.16, 0.1, 0.3) | `lighting.fog`, `atmosphere.mist` |
| frame edges | vignette 0.3, violet black (0.02, 0, 0.06) | `lighting.vignette` |
| grade | shadows cool (0.96, 0.95, 1.08), midtones neutral, highlights a touch cyan (0.97, 1.0, 1.05); the arena's exposure, contrast and bloom | `lighting.post` |
| kit | CyberCity and SciFi City at 0.55 (their buildings 0.6), walls and junk 0.55 | `lighting.kitValue` |
| **Neon** (the dominant colour) | | |
| signs, windows, trims | the packs' own emission: cyan trims and screens, white and warm windows, multicolour shop signs and billboards, x2-3 | `lighting.kitEmission` |
| cyan glow and pools | (0.25-0.3, 0.85-0.9, 1.0): half the blocks, pylons, vending machines, planters, hover lights, the food truck | `lighting.glow` |
| magenta glow and pools | (1.0, 0.25-0.3, 0.8-0.85): the hologram tree, the other blocks and pylons, vending and claw machines | `lighting.glow` |
| violet glow and pools | (0.55, 0.3, 1.0): the small blocks, ad pillars | `lighting.glow` |
| hologram trees | magenta, additive scan lines | CyberCity's hologram, `Demo3D/KitHologram` |
| flames | four flickering hover lights, magenta and cyan (4 realtime lights) | `lighting.flames` |
| holo pads | magenta (1.0, 0.2, 0.75) at the opening, cyan (0.2, 0.85, 1.0) in the courtyard and the ring, linear | `atmosphere.runes` |
| **The horde** (decision 4: never recoloured) | | |
| its key | white (`keyColor`), 3x the key on the set (the forest's), the arena's 0.5 fill | `lighting.crowd` |
| rim | cool (0.26, 0.3, 0.46) | `lighting.crowd.rim` |
| type hues | unchanged | roster.json |
| **Warm accents** | | |
| hero's pool | cool white (0.86, 0.9, 1.0), intensity 120 (the forest's 40) | `lighting.hero` |
| market | orange market lights and paper lanterns | the night market |
| **Effects** | unchanged: white → orange → red | effects.json |

## Findings

**Readability passes in every shot**, with the horde lighter than the street:

| shot | L* monster | L* ring | dL* | against the empty street |
|---|---:|---:|---:|---:|
| opening | 25.8 | 1.8 | 24.0 | 23.9 |
| path | 26.6 | 4.2 | 22.4 | 23.4 |
| glowing-tree | 28.6 | 3.9 | 24.7 | 25.1 |
| graveyard | 22.8 | 1.6 | 21.2 | 21.1 |
| courtyard | 22.8 | 1.5 | 21.3 | 21.2 |
| forest-edge | 25.8 | 6.4 | 19.4 | 23.4 |

- **The black street carries it,** as the lava's basalt did: the ring sits at
  L* 1.5-6.4, so a horde at L* 23-29 clears the floor by 2-8. The margin is
  narrower than the lava's (26-31): the cool key and rim give the horde less
  value than the lava's warm bounce, and the forest edge's glow pools and lit
  windows lift its ring to 6.4.
- **The monsters lose chroma:** mean C*ab 38-44, 0.67-0.78x the forest's, the
  most of any environment so far. Nothing on the set bounces colour into
  them; a neutral key on a near-black set leaves their dark sides black (L*
  p10 2.5-5), so each type reads by its lit top.
- **The palette swap's failure is gone.** Its violet moon pulled every type
  blue. Here the key stays white and the cool comes from the set, the fog and
  the emission, so the orange skeletons and the red demons stay warm.

**Which type hues fail, and why.** None falls below the floor as a mass. By
hue, against the neon:

- **The dragon (magenta, 300) and the demon (crimson, 340) sink into the
  magenta pools.** Inside the hologram tree's 7 m pool and the magenta
  pylons' and blocks' pools, a demon's crimson shifts to pink and the dragon
  matches the light (`glowing-tree-horde.jpg`, top left). On the black
  street between pools both read.
- **The bat (violet, 260) is the darkest type** on a black set: its violet
  is low in value, so a bat flock reads as dark purple specks
  (`opening-horde.jpg`), only as hue where a pool lights the ground behind
  it. It is the type the bead expected to suffer against the signs; it
  suffers against the dark more.
- **The Big Ork (cyan, 180) beside the cyan pools** loses its edge where it
  stands in one (`song-145.jpg`, top). Elsewhere it is the only cool
  monster, and reads.
- **The warm and green types win:** the skeleton (rust, 20), the ochre
  mushroom (60), the goblins and slimes (100, 140) are the clearest masses in
  every still, because nothing on the set is orange, yellow or green except
  the market's lanterns, a few CyberCity crates and the windows.

**Effects stay the brightest values in 10 of 11 stills.** In the beauty
shots their L* p98 is 85-100 against the rest's 73-82; in the song stills
they lead by 2-30 L*, except:

- **song-100: effects 84.1, the rest 89.1.** The rest's brightest pixels are
  the ochre mushrooms' lit caps under the hero's pool (1.9% of the frame
  above L* 89; mean colour a yellow-green), as in the lava, not the neon.
  The horde's key at 3x (from 3.2) held the gap at 5 L* (6.5 before) while
  the pool rose from 80 to 120; a dimmer pool would fail the stage.
- **Effects are dimmest where they matter least:** their song-still p98 is
  84-100 against the forest's 87-100, lowest in groove-2 (song-100, 84
  against 97). Additive glows add light to what is under them, and black
  adds nothing.
- **The neon never tops the effects:** with the effects removed, the frame's
  brightest pixels in the beauty shots (the windows, billboards and the
  pylons' screens among them) are L* 73-82 at p98, under the effects'
  85-100, and 0.0-0.6% of any still is clipped.
- **Fire reads by hue here, unlike the lava and the desert.** The hits'
  white, orange and red are the only warm light on a cool set, so they stand
  apart by hue as well as value: the bead's "warm effects the accent".

**The stage.** At a pool of 80 on a greyer asphalt (0.12, 0.12, 0.14),
song-025 failed (dL* 13.4) and three beauty shots were under the song
stills' bar.
At 120 on the bluer asphalt the lit stage is L* 25-50 against a border of
1-12: dL* 21-49, every still passing. The pool reads as a cool white spot on
the wet street.

**The cool share is 6.5-19.4%** of a song still and 12-17% of a beauty shot:
the highest of any environment, over the palette pass's 15% cap in three song
stills (song-025 19.4%, song-060 16.6%, song-100 15.9%). That cap was set for
the forest's warm night, where cool was the accent; here cool is meant to
dominate (the bead's colour target), so it is recorded, not fixed. The share
counts cyan to violet (HSV 180-280); the magenta pools and the hologram tree
are outside it, so the set's cool-and-magenta share is higher still. The
drops (song-145, song-175) fall to 6.5-7.4% because the horde and its effects
fill the frame.

**Every shot shows a landmark or edge decoration** (the empty stills):

- opening: the fairy ring of glowing manholes, floor lights and puddles inside the magenta holo pad;
- path: the manhole stepping stones, the pylon ring and its cyan pad, a lit block at the right;
- glowing-tree: the hologram trees in their magenta pool, the holo planters, a lit block;
- graveyard: the night market, its stalls, lanterns and trailer, a pylon;
- courtyard: the shopfront walls, their signs and billboard, the cyan pad;
- forest-edge: a taxi and a lit block at the left, the hologram tree's pool, the edge band's machines.

No canopy covers fighting ground: the blocks', vehicles' and trees' measured
canopies keep off it, and the canopy fade covers the SciFi trees at the
edges. The hologram trees draw no depth, so they never hide a monster.

**Against the reference.** SciFi City's demo scene, from our camera, is a
packed street: vehicles parked nose to tail, props along every kerb, low
buildings shoulder to shoulder, billboards on every wall. Our arena is open
fighting ground by design, so the fighting ground is far sparser; the edge
bands and the forest come close to its kerbs and blocks. CyberCity's demo
city is vertical (stacked floors, walkways, interiors), and from our camera
its framings show only roofs and floors, so its stills aren't kept.

## S0 at the authored density (decision 8)

The Mac has no bench sweep, so this is the Mac's windowless stand-in for S0,
as in [env-lava](../env-lava/README.md#s0-at-the-authored-density-decision-8):

- the empty arena at the authored density (E2, 2x): `--scene S3 --beauty-empty`;
- the hero held at the opening and the forest-edge shots, at a 40 m view;
- `--gpu-probe-at` at 12 times, each time three 1080p MSAA4 frames timed
  through GPU completion (CPU submission and GPU in series);
- four runs per environment, 96 frames per row, the same player.

**The machine was loaded** (load average 13-30: other sessions were baking
and rendering), and the first run of each pair spread up to 2.6 ms. These
are not Cygnus numbers and not comparable to the 1.5 ms ceiling.

| environment | placements | triangles | realtime lights | opening run medians | forest-edge run medians | opening p50 / p90 | forest-edge p50 / p90 |
|---|---:|---:|---:|---|---|---:|---:|
| neon | 1,940 | 794,287 | 4 | 2.96, 2.03, 2.13, 2.09 ms | 4.20, 2.93, 2.02, 1.93 ms | 2.33 / 3.39 ms | 2.42 / 4.79 ms |
| forest | 2,582 | 804,264 | 8 | 2.85, 2.50, 1.87, 1.83 ms | 4.65, 2.07, 2.21, 2.08 ms | 2.24 / 3.79 ms | 2.56 / 4.83 ms |

- **The neon costs what the forest does,** within the probe's noise: the
  same triangles on 25% fewer placements, half the realtime lights. Its glow
  lights (17 kinds of piece, one light per placed piece) are baked, and
  their pools are one merged mesh of additive cards. The hologram trees and
  planters are a handful of additive draws.
- **CPU:** a batch-mode frame (`--stats`, no drawing, 60 s of the song) is
  0.4 ms median and 0.5 ms p90 in the neon, 0.3 and 0.5 ms in the forest; the
  timeline's evaluation and the crowd are 0.11 ms median in both. The render
  thread's submission is inside the GPU probe's times. The operator's Cygnus
  re-check (plan.md, Pending on the operator) is where the real S0 GPU p99
  and main- and render-thread times come from.

The windowless smoke check plays the whole song in the neon:
`--environment neon --fixed-time --seconds 199` exits 0 on
`Arena-neon-E2-2x` (2,318 kit renderers, 4 realtime lights), "done at song
time 199.00".

## Self-review

- The horde reads against the ground at a glance: yes, lighter on black,
  |dL*| 19-25; its shadowed sides merge with the street, and it is the least
  saturated horde of the environments.
- Every shot shows a landmark or edge decoration: yes (above).
- No canopy hides fighting ground: yes.
- Effect colours stand apart from the set: yes, by hue (the only warm light)
  and by value; one song still (song-100) has the mushrooms' caps 5 L* above
  them.
- Nothing floats, clips or repeats in an obvious pattern: the paper lanterns
  hang in the air (they are the pack's hanging lanterns, without their
  strings), and the courtyard's billboard stands on the wall's roof. The lit
  blocks repeat their four window patterns through the forest.
