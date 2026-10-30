# Look set: env-lava, a basalt hellscape lit by lava (ps-bop.3.5, 2026-10-30)

The lava environment (epic ps-bop): very dark and warm, the arena's area
plan in POLYGON Dungeon Realms' hell kit and Dark Fantasy's basalt and burnt
pyres, on near-black basalt cut by glowing lava. Rendered by the look harness
(IL2CPP player, Apple M5 Max, Metal) at staging `2c6f4712` plus this bead,
with `--player-args "--environment lava"`. The numbers are in
[look.md](look.md).

| file | what |
|---|---|
| [contact-sheet.jpg](contact-sheet.jpg) | [final](../final/README.md) (the forest) left, lava right, shot for shot |
| `song-*.jpg`, `<shot>-horde.jpg`, `<shot>-empty.jpg` | the set's stills |
| [reference/](reference/) | Dungeon Realms' own hell demo scene (`Demo_Hell_01`) from our camera under the lava's lighting (TD15's reference stills) |

Run it again with:

```
uv run --project demo3d/tools demo3d look env-lava --baseline final --player-args "--environment lava"
```

## The kit

The layout is
[environment-lava.json](../../../Assets/Demo3D/Choreography/Data/environment-lava.json).
It keeps the arena's areas, goals and shots, and swaps every prop group and
every landmark's pieces.

- **Two packs, 82 pieces in the manifest:** Dungeon Realms 48 (its hell
  set: black rocks, spikes, spires and obelisks, the horned giant skull,
  skeletal hands, dead trees and brambles, rubble) and Dark Fantasy 34 (basalt
  columns and cliffs, a dead tree, burnt pyres and stakes, burnt bodies,
  bones and skulls, tombstones, the demon statue and ward). Both packs use the
  Standard shader on their atlases (Dungeon Realms' with an emission map, which
  gives the obelisk its cyan cracks), so the baker's converter turns them into
  URP Lit. No custom shader is used, so nothing needed a Shader Graph port.
  FootprintMeasurer names Dungeon Realms' pieces `realms/`.
- **The lava is ours, not the pack's.** Dungeon Realms has lava pieces
  (`SM_Env_Lava_Plane_01`, a flat 7 m emissive square; `Lava_Flow_01` and
  `Lava_Line_01`, small flow and drip pieces). Its own hell scene lays them as
  thin straight channels between packed cliffs (see the reference stills).
  On our open arena a square plane reads as a tile, not a river. So the
  ground carries the lava instead: an optional `ground.lava` in the layout,
  from which ArenaBaker generates an emission map for the ground material
  (`ArenaBaker.GroundLava`, tested by `LavaRunsOnlyPastFightingGround`):
  - **rivers:** iso-lines of warped noise about 28 m between bends, up to 3.6 m wide, hot
    yellow mid-stream and red-orange at the banks. They start 1.5 m past
    fighting ground and widen over the next 6 m, so they wind through the edge
    bands and the forest, never onto fighting ground;
  - **a glow** on the basalt beside them (1.6 m falloff);
  - **cracks** in the crust (warped cells about 2.6 m across) glowing near
    the rivers and fading over 7 m. On fighting ground only the cooled crust's
    dim cracks show, at a tenth of the river's red and in patches about 15 m across, so
    the fighting ground isn't one tiling;
  - its emission is baked (BakedEmissive), so the lava lights the kit around
    it through the probes. It costs nothing at runtime beyond the ground's
    emission map (2048², about 11 cm a texel).
- **The braziers and the pyre are the arena's own** (Dark Fantasy), with
  their flames and the candles' (8 realtime lights).

Every piece's footprint is measured (footprints.json). The landmarks and
structures keep their places and their navigable shape:

| area | forest | lava |
|---|---|---|
| glowing-tree | the glowing elven tree | Dungeon Realms' rune obelisk (`SM_Env_Obelisk_Glow_01`, 9.4 m) with its cyan cracks, a cyan glow light and pool, a smaller obelisk and two hell rocks |
| crystal-ring | six tombstones in a ring | six black hell spikes (3-4.4 m) |
| graveyard | tombs, statue, fence, candles | a burning ground: the tombs and tombstones, a burnt body on a stake, a giant skeletal hand reaching out of the ground, burnt stakes for the fence, bones, skulls, a dead tree, the candles |
| ruins-gate | ruin walls with a gap | the same ruin walls (darkened) with the gap, basalt columns at each end |
| courtyard | ruin walls, four gaps | the same walls and four 5 m gaps, black obelisks at the corners |
| brazier-glade | four braziers, the pyre, a statue | the braziers and the pyre, the red demon statue (`SM_Prop_Demon_01`) and a demon ward |
| opening-ruin | ruin and gargoyle | the horned giant skull (`SM_Env_Skull_Huge_01`), a skeletal hand and a spike |
| fairy-ring and other dressing | flowers and stepping stones | skulls, bone piles and broken flagstones, the same candles |
| rune circles | violet | hot orange, like summoning circles |

The forest fill is basalt columns, black spires, obelisks and pillars (scaled
0.55-0.9x: Dark Fantasy's basalt runs 6-18 m and the hell pillars 16-19 m),
hell spikes and dead trees, with basalt cliffs and black rock mounds as its
rocks. The edge bands hold hell rocks, darkened grey boulders, short basalt
stacks, dead brambles, burnt stakes, burnt rubble and bodies, skull piles and
the odd demon ward. Clearings carry small rocks, rubble, skulls and bones.

Two deviations from the forest's density rules, both so the lava shows:

- **the forest is 5.5 m apart** (the forest's 4.5 m), so the rivers glow
  between the spires. The scene has 1,854 placements against the forest's
  2,582, and about the same triangles (0.81 M against 0.80 M);
- **the edge bands are a little denser and larger** (2.4 m, 0.9-1.5x against
  2.6 m, 0.8-1.2x), since the hell rocks are smaller than the forest's bushes
  and, on black, a sparse band read as empty ground.

The pack's big pieces (the giant golem's 20-30 m parts, the 13 m hell rings,
the 18-21 m bone ribs, the 28 m cliffs) are not used: each is larger than a
landmark's place in the plan or would arch over fighting ground. Its crystals
read as a flat yellow under the cyan glow, and its tan dwarven walls and
broken statues belong to another place.

## The palette

Role by role, with where each colour lives. sRGB unless noted.

| role | colour | where |
|---|---|---|
| **Basalt and lava** | | |
| fighting ground | near-black basalt (0.16, 0.14, 0.125), path (0.18, 0.155, 0.135); L* 3.6-8.7 rendered, empty | `ground.clearing`, `path` |
| edge bands, forest floor | black (0.06, 0.05, 0.045), (0.04, 0.035, 0.032); L* 2-3.5 rendered | `ground.edge`, `forest` |
| lava | hot (1.0, 0.72, 0.30) mid-stream, red-orange (1.0, 0.24, 0.02) at the banks, the glow and the cracks; HDR x2.2 | `ground.lava` |
| key light on the set | dim warm (1.0, 0.62, 0.46), intensity 0.7, the arena's 42° | `lighting.moon` |
| shadows | ember: trilight (0.05, 0.025, 0.022) / (0.045, 0.022, 0.016) / (0.09, 0.035, 0.012), the warmest from below | `lighting.ambient` |
| fog, mist | smoke red (0.07, 0.025, 0.016); ash (0.24, 0.11, 0.07) | `lighting.fog`, `atmosphere.mist` |
| frame edges | vignette 0.3, blood black (0.05, 0.006, 0) | `lighting.vignette` |
| grade | shadows a touch warm (1.04, 0.97, 0.95), midtones neutral, highlights hot (1.03, 0.98, 0.90); the arena's exposure, contrast and bloom | `lighting.post` |
| kit | Dungeon Realms at 0.5, Dark Fantasy at 0.55 (the forest's 0.7) | `lighting.kitValue` |
| **The horde** (decision 4: never recoloured) | | |
| its key | white (`keyColor`), 3.2x the key on the set (the forest's 3x), the arena's 0.5 fill | `lighting.crowd` |
| rim | warm neutral (0.32, 0.22, 0.18) | `lighting.crowd.rim` |
| type hues | unchanged | roster.json |
| **Warm accents** | | |
| hero's pool | peach (1.0, 0.80, 0.62), intensity 150 (the forest's 40) | `lighting.hero` |
| flames | orange: the braziers, the pyre, candles (8 lights) | the arena's `lighting.flames` |
| rune circles | hot orange (1.0, 0.28-0.32, 0.05), linear, intensity 0.1-0.12 | `atmosphere.runes` |
| **Cool accents** | | |
| obelisk glow | cyan (0.3, 0.85, 1.0), intensity 8, 14 m, a 6.5 m pool | `lighting.glow` |
| obelisk cracks | cyan, the pack's own emission | Dungeon Realms' atlas |
| **Effects** | unchanged: white → orange → red | effects.json |

## Findings

**Readability passes in every shot**, with the horde lighter than the basalt,
and by the widest margins of any environment so far:

| shot | L* monster | L* ring | dL* | against the empty basalt |
|---|---:|---:|---:|---:|
| opening | 34.2 | 3.4 | 30.9 | 30.5 |
| path | 29.6 | 3.2 | 26.4 | 25.9 |
| glowing-tree | 32.8 | 3.8 | 29.0 | 27.8 |
| graveyard | 30.8 | 3.0 | 27.8 | 27.5 |
| courtyard | 30.4 | 2.6 | 27.8 | 27.8 |
| forest-edge | 30.3 | 3.0 | 27.2 | 26.6 |

- **The basalt makes it easy.** The ring around the horde sits at L* 2.6-3.8,
  so a horde at L* 30-34 clears the floor by 9-14. The palette swap's lava
  (a red moon) failed because it turned the horde red. Here the horde's key
  stays white, and the colour comes from the ground and the emission.
- **But the monsters' dark sides merge with the ground.** Their L* p10 is
  3.6-6.6, against the forest's mid-value ground where the shadowed sides
  still read as shape. On black, a monster reads by its lit top and its hue;
  its silhouette's shadowed half is lost. The warm rim (0.32, 0.22, 0.18) is
  too weak to draw it.
- **The monsters lose some chroma:** mean C*ab 46-51, 0.82-0.89x the
  forest's. The dim set gives the horde less bounce than the forest's warm
  ground did.
- **The key's multiple matters here, unlike in the light environments.** At
  4x the key, the ochre mushrooms' yellow caps became the brightest
  non-effect pixels in song-100 (above the effects). At 3.2x the horde is
  still 26-31 L* above the ring.

**Which type hues fail, and why.** None falls below the floor as a mass,
since every type is far lighter than the basalt. By hue, against the glow:

- **The demon (crimson, 340) is the weak type,** as the bead expected. Its
  crimson is the death bursts' red. In the drops, where demons die in
  masses, the demons and their bursts merge into one red cloud
  (`song-145.jpg`). On the basalt alone they read well.
- **The skeleton (rust, 20) collides with the lava, not the ground.** It is
  the lava's bank colour. Where skeletons walk the edge bands beside a river,
  they read as orange on orange (`path-horde.jpg`, top left). On the dim
  cracked basalt they are the frame's strongest warm mass (`song-025.jpg`).
- **The greens, the violet bat and the cool types win.** The goblins and
  slimes (100, 140), the bats (260), the Big Orks (180) and the trolls (220)
  are the cleanest masses in every still, because nothing on the set but the
  obelisk is green, blue or violet.
- **The ochre mushroom (60)** is the brightest type on black, its caps near
  the effects' cores (above).

**Effects stay the brightest values in 10 of 11 stills.** In the beauty shots
their L* p98 is 86-99 against the rest's 74-83. In the song stills the margin
is thin: 0.4-5 L* in the drops and groove-2, and one fail:

- **song-100: effects 86.9, the rest 87.3.** The rest's brightest pixels are
  the ochre mushrooms' lit caps (2.8% of the frame above L* 85), not the
  lava.
- **Effects are dimmer on black than on the forest's ground.** Their L* p98
  in the song stills is 82-97 against the forest's 87-100. Additive glows add
  light to what is under them, and black adds nothing. The white-hot cores
  that topped the forest's frames are smaller here.
- **The lava never tops the effects:** at its first intensity (3) the
  rivers' hot cores reached the effects' values. At 2.2 the hot cores sit
  under them and bloom as yellow-orange, with nothing clipped (0.0% in every
  still).
- **Fire competes with lava by hue.** The hits' orange and red sit on an
  orange-and-red set. They read by value and size, as yellow-white blooms, as
  in the desert.

**The stage.** At the forest's pool (40) on basalt this black the stage
failed every still (dL* 4-5). Lifting the fighting ground's albedo (0.085 →
0.16, still L* 3.6-8.7 empty) and the pool (40 → 150) gives a lit stage of
L* 24-34 against a border of 2-17: dL* 17-31, every still passing the bar.
The pool is the only light on the fighting ground, so the stage reads
strongly.

**The cool share is 0.3-2.6%** of a song still and 2.2-3.8% of a beauty
shot, well under the 15% cap. The obelisk's cyan pool and cracks and the
cool types are the only cool on screen. The ambient fireflies (the effect
runtime's, shared by every environment) add a few cool sparks.

**Every shot shows a landmark or edge decoration** (the empty stills):

- opening: the fairy ring of skulls, bones and candles inside the orange rune circle;
- path: a lava river and a dead tree at the top left, the spike ring, the obelisk's glow;
- glowing-tree: the rune obelisk in its cyan pool, lava at the top;
- graveyard: the tombs and candles, the gate's walls at the top, the skeletal hand and a river at the right;
- courtyard: the walls, their obelisks and the rune circle;
- forest-edge: a lava river, basalt and a dead tree at the left, the obelisk.

No canopy covers fighting ground: the dead trees' and spires' measured
canopies keep off it, and the canopy fade covers the dead trees at the edges.

**Against the reference.** Dungeon Realms' hell scene, seen from our camera,
is a canyon: black rock cliffs packed shoulder to shoulder, thin straight
lava channels between them, almost no open ground. Its dressing is ours (the
same black rocks and spikes). Our arena is open fighting ground by design,
so it is far sparser than the reference, and our lava is wider and winds.
The reference's packed cliffs at our edges would hide the lava that makes
the place.

## S0 at the authored density (decision 8)

The Mac has no bench sweep, so this is the Mac's windowless stand-in for S0,
as in [env-desert](../env-desert/README.md#s0-at-the-authored-density-decision-8):

- the empty arena at the authored density (E2, 2x): `--scene S3 --beauty-empty`;
- the hero held at the opening and the forest-edge shots, at a 40 m view;
- `--gpu-probe-at` at 12 times, each time three 1080p MSAA4 frames timed
  through GPU completion (CPU submission and GPU in series);
- two runs per environment, 72 frames per row, the same player.

**The machine was loaded** (load average 27-47: other sessions were baking
and rendering), so the run-to-run medians spread by 0.5-0.8 ms. These are
not Cygnus numbers and not comparable to the 1.5 ms ceiling.

| environment | placements | triangles | opening run medians | forest-edge run medians | opening p50 / p90 | forest-edge p50 / p90 |
|---|---:|---:|---|---|---:|---:|
| lava | 1,854 | 805,829 | 1.71, 2.30 ms | 2.03, 2.79 ms | 2.15 / 2.82 ms | 2.39 / 4.75 ms |
| forest | 2,582 | 804,264 | 2.14, 1.65 ms | 2.33, 3.16 ms | 1.96 / 3.23 ms | 2.84 / 4.27 ms |

- **The lava costs what the forest does,** within the probe's noise: the
  same triangles on 28% fewer placements, the same 8 realtime lights. The
  ground's emission map is one more texture sample on one draw.
- **CPU:** a batch-mode frame (`--stats`, no drawing, 60 s of the song) is
  0.3 ms median and 0.5 ms p90 in both the lava and the forest. The render thread's submission is inside the GPU
  probe's times. The operator's Cygnus re-check (plan.md, Pending on the
  operator) is where the real S0 GPU p99 and main- and render-thread times
  come from.

The windowless smoke check plays the whole song in the lava:
`--environment lava --fixed-time --seconds 199` exits 0 on
`Arena-lava-E2-2x` (1,859 kit renderers, 8 realtime lights).

## Self-review

- The horde reads against the ground at a glance: yes, lighter on black,
  |dL*| 26-31; its shadowed sides merge with the basalt.
- Every shot shows a landmark or edge decoration: yes (above).
- No canopy hides fighting ground: yes.
- Effect colours stand apart from the set: by value and bloom; by hue the
  fire is on its own ground. One song still (song-100) has the mushrooms'
  caps 0.4 L* above the effects.
- Nothing floats, clips or repeats in an obvious pattern: the cooled cracks
  come in patches, so the fighting ground isn't one cellular tiling. The
  rivers' bends repeat at about 28 m, which a wide view can show.
