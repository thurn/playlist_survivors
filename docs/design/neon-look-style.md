# The Neon Look's Style Guide

## Introduction

This is the rulebook for the neon crystal look (epic ps-wnj,
[neon-look.md](neon-look.md)). Every bead that draws something in the look
package reads it first. It answers three questions:

- **What are we copying?** A study of Geometry Wars 3 and a few neighbours,
  with numbers measured from their stills.
- **What does that mean for each axis in 3D?** Monsters, world, post, VFX,
  motion, hero, transitions.
- **How do we know we got there?** Rules with measurable forms and
  thresholds. The look metrics bead (ps-wnj.2) turns them into checks; the
  palettes (`unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/`) hold the
  colours.

The short version:

- **Light comes from things, not from the world.** The floor is void; edges,
  rims, trails, sparks and the grid emit.
- **Each frame has a colour key.** One hue family holds most of the colour;
  at most four families in a frame, plus white.
- **White is earned.** Impact frames, sparks and the blade's flare only.
- **A swarm is many lit things with dark between them**, not a coloured
  field.

## Related Information

- [The neon crystal look](neon-look.md): the epic's design. Its
  **Decisions**, **Palettes** and **Checks and Evidence** sections are the
  frame for this guide.
- [Reference board](../../demo3d/evidence/look/style/README.md): v2 crystal
  cut frames measured against these rules (`board.jpg`, `board.py`).
- [Look harness](../../demo3d/tools/src/demo3d_tools/look.py): `demo3d look`,
  where L*, chroma, readability and (from ps-wnj.2) these rules are measured.
- [Trailer mode](../tech-demo-3d/trailer.md): the crystal cut and its
  **Rules for Every Frame**.
- [Palette swaps](../../demo3d/evidence/look/palettes.md): the old lesson
  that a coloured key light erases type hues, and light grounds fail on value.

## The Study

### Sources

- Geometry Wars 3: Dimensions Evolved's five store screenshots
  ([Steam](https://store.steampowered.com/app/310790/Geometry_Wars_3_Dimensions_Evolved/)),
  measured below. They are not committed (copyright); fetch them again from
  the store's `appdetails` API to re-measure.
- Bizarre Creations on the series' look
  ([Game Developer](https://www.gamedeveloper.com/game-platforms/the-color-and-the-shape-bizarre-creations-on-i-geowars-i-sensible-aesthetic)):
  the grid as a quiet second layer, flat backgrounds so attention stays on
  the play, and in Retro Evolved 2 a grid whose colour shifts toward the
  enemies over it.
- Lucid Games on Dimensions' 3D stages
  ([GamesBeat](https://gamesbeat.com/dimensions-is-more-than-just-geometry-wars-3s-subtitle/)):
  the grid bent onto curved 3D shapes.
- The Tuts+ "neon vector shooter" remakes of the grid
  ([warping grid](https://code.tutsplus.com/make-a-neon-vector-shooter-in-xna-the-warping-grid--gamedev-9904t))
  and bloom
  ([bloom](https://code.tutsplus.com/make-a-neon-vector-shooter-in-xna-bloom-and-black-holes--gamedev-9877t)):
  a mass-spring lattice; a low bloom threshold with the glow *more*
  saturated than its source.
- Reviews on clutter: players learn to read the flashing screen
  ([Hooked Gamers](https://hookedgamers.com/pc/geometry_wars_3_dimensions/review/article-1381-2.html));
  deaths as neon fireworks
  ([GameZone](https://gamezone.com/?p=106625)).
- Housemarque: Resogun's voxel destruction, physics kept short of realism
  for readability, a neon look settled on after several retro styles
  ([Gamingbolt](https://gamingbolt.com/resogun-dev-diary-from-voxel-murder-to-resolution-gunning-in-1080p),
  [Wikipedia](https://en.wikipedia.org/wiki/Resogun)); Nex Machina's voxel
  shockwaves and darker "cablepunk" take
  ([Engadget](https://www.engadget.com/2016-12-03-nex-machina-housemarque-resogun-sequel-interview-preview.html)).

### Geometry Wars 3, measured

Each still was scaled to 960 px wide, its HUD strip cut, and measured with
look.py's L*, C*ab and HSV hue. "Key" is the share of colourful pixels
(C*ab >= 30, L* >= 20) inside the best 60-degree hue window. v2 is our
crystal cut (`captures/trailer-crystal-v2.mp4`), one frame every 2 s.

| measure | GW3 median (range) | v2 median (range) |
| --- | ---: | ---: |
| L* p10 / p25 / p50 | 4.5 / 10.6 / 25.8 | 2.0 / 5.4 / 17.1 |
| L* p98 | 96 (91-100) | 96 (74-98) |
| near-black share (L* < 10) | 24% (20-30) | 39% (18-69) |
| bright share (L* >= 50) | 24% (13-33) | 24% (7-41) |
| clipped share (all channels >= 250) | 1.1% (0.5-8.2) | 0.0% (0-1.0) |
| white share (C* < 15, L* >= 85) | 2.3% (0.9-10) | 0.1% (0-2.1) |
| key: colour in one 60-degree window | **0.70 (0.56-0.83)** | **0.47 (0.32-0.94)** |
| halo: ring L* round emitters less p25 | 18 (17-26) | 13 (5-30) |

What the numbers and the stills say:

- **GW3 is not black; it is keyed.** Its backgrounds are dark nebula and a
  tinted grid surface (p50 L* 26), but about 70% of all colour sits in one
  hue window (usually blue-cyan, violet-red in one stage). v2 is darker than
  GW3 yet reads as noisier, because its colour is split across five to eight
  families. **The key, not darkness, is the biggest measured gap.**
- **Value is a ladder.** Background and grid fill sit low (L* 5-30), enemy
  outlines and particles mid-high (50-85), and only cores, flares and
  explosions reach white. Clipping is ~1% normally and spikes to ~8% on a
  big explosion.
- **Glow is wide and saturated.** Emitters carry soft halos that lift their
  surroundings ~18 L* above the background; the core whitens while the halo
  stays coloured (the remake's bloom saturation 1.5 over a base of 1). Big
  events throw horizontal anamorphic streaks.
- **Hundreds stay readable** because each enemy is a small, bright-edged icon
  with a darker interior and dark gaps around it; one type keeps one hue and
  one silhouette; a dense pack of one type (the violet pinwheels) still reads
  as many outlines. Behaviour and shape carry identity as much as hue does.
- **The grid is the world.** Thin lines with every third line thicker, cells
  faintly tinted, the lattice shoved by every explosion and pulled by black
  holes, and (Retro Evolved 2 on) tinted toward the enemies over it. It shows
  every event, so the background is never dead and never busy.
- **Deaths are fireworks in the enemy's own hue** plus a grid shove, all on
  the death frame: no dying animation.
- **Clutter is the known cost.** Reviewers note the screen gets crowded at
  peaks. Our trailer is watched, not learned, so we keep GW's grammar but
  less of its density of effects.

### Other references

- **Retro Evolved 2**: the post pass added to the whole game at 60 fps; the
  grid's colour follows the enemies. Our impulse field and zone tints are
  the same idea.
- **Resogun and Nex Machina**: destruction leaves persistent, physically
  bouncing debris in the enemy's colour on a dark stage. It supports our
  shards staying on the floor for a beat after the burst (not twinkling).
- **Tron-style edge light**: a black world whose objects exist only as
  lit edges. It is the `edge` preset's ancestor (stylelab's Spectrum is the
  in-repo precedent).

## What v2 Does

The [reference board](../../demo3d/evidence/look/style/README.md) measures
six v2 frames. The patterns:

- **No key** (0.34-0.53 in five of six): every type has its own hue
  (roster.json's nine 40-degree slots), so a full frame is a hue soup.
- **The blob**: one type in one flat, saturated colour with no internal
  value fills a region solidly; neighbours share edges of equal L*.
- **The world is lit**: lava and dragon shots light an amber floor (L* p25 8-9,
  bright share 33-36%). The brightest thing on screen is the ground, not an
  emitter.
- **Little glow**: halos 7-12 L* in the dense shots. Emitters are flat
  paint, not light.
- **What works**: the white-gold formations and their shatter (keep the gem
  colour), the near-black opening shot's value, low clipping.

## The Value Ladder

Every element sits on one rung. A bead that puts something on a higher rung
than its role says is wrong, even if it looks good alone.

| rung | L* | what lives here |
| --- | --- | --- |
| void | 0-5 | floor, fog, monster cores (edge), empty space |
| ground light | 10-35 | grid lines, zone tints, hologram fill, distant glow |
| enemy light | 45-82 | edges, rims, solid bodies, enemy trails, shard faces |
| hero light | 85-98 | hero body, crystal gem, telegraphs at their peak |
| earned white | 98-100, clipped | impact frames, sparks, blade flare core, death pop |

Consequences:

- **The floor never rises above ground light** except under a passing
  impulse ring or a beat breath, and both decay within a beat and a half.
- **No light pools.** No spot or point light paints the floor around the
  hero; the hero reads by its own brightness and the grid by its emission.
- **A monster never outshines the hero** (hero rule below).

## The Hue Budget

### Families and the key

A **family** is a 60-degree HSV hue window. Every palette has three enemy
families and a zone tint list:

- **Key** (`enemies[0..1]`): the most numerous fodder. It sits within 40
  degrees of the palette's first zone and its grid, so crowd, floor and grid
  share one hue and the frame keys naturally.
- **Accent** (`enemies[2..3]`): the remaining fodder, far enough from the
  key (>= 50 degrees) to separate.
- **Counter** (`enemies[4..5]`): the elites, at least 50 degrees from both.
- **Boss** (`boss.body`, `boss.telegraph`): inside one of those three
  families (within 40 degrees of a member), so a boss shot adds no family.
- **Hero, gem, spark, core**: near white (C* <= 30), so they spend no family.

A frame therefore holds at most four families (the zone's key plus three
enemy families) plus white. Zones other than the first change the floor's
key per shot; the enemies keep their families.

### Type to slot

Types share slots and are told apart by silhouette and size, as GW tells
types apart by shape. Pairs in one slot never look alike:

| slot | family | types |
| --- | --- | --- |
| `enemies[0]` | key, light | skeleton (1.8 m walker), slime (0.9 m blob) |
| `enemies[1]` | key, deep | demon (1.9 m walker), bat (0.3 m flyer) |
| `enemies[2]` | accent | goblin |
| `enemies[3]` | accent | mushroom |
| `enemies[4]` | counter | bigork (elite) |
| `enemies[5]` | counter | troll (elite) |
| `boss.body` | boss | dragon (and evilgod if it returns) |

The crowd bead (ps-wnj.4) owns where this mapping lives (the roster, the
preset or the package); this table is its starting value.

### Palette conformance

Everything drawn takes its colour from a role. Bloom may whiten a core and
mix two neighbouring hues, but a bright, colourful pixel far from every role
is a bug: a vendor texture leaking through, a hard-coded colour, a grade.

## The Axes in 3D

### Monsters

What GW does with icons we do with silhouettes:

- **Bright edge, darker interior, dark gap.** Every look draws a body whose
  boundary is lighter (edge, rim, outline-backed fill) or darker (solid's
  outline) than its neighbours' boundary, so two touching monsters of one
  type show a seam. This is the swarm-separation rule.
- **Form from view angle.** Fresnel, rim and refraction vary across a body,
  so a monster's L* spans at least 30 (18 for solid). This is the monster
  form rule, the answer to "flat".
- **Small hot cores.** A beat flares a core or the edges, not the whole body;
  a beat's whole-crowd flash lifts the bright share past its cap.
- **Per look:**
  - `edge` (dark-core-edges): void core (L* <= 8), the slot's hue only in
    the rim and feature edges (L* 55-80), a small core that flares on the
    beat. Bright share stays low; separation comes free.
  - `solid` (bright-solid): the slot's hue as emission at L* 50-78 (never
    the hero's rung), with a 2-3 px dark outline in screen space and a
    darker falloff toward the silhouette so a clump shows seams (as built,
    ps-wnj.11: a 2.5 px outline and a band about 3 px wide inside the
    silhouette; 2 px lost too much depth to the bloom).
  - `hologram` (wireframe): feature edges or all edges at L* 60-85, fill at
    ground light (L* 15-30, additive, under 25% opacity), scanlines in the
    post rather than per monster.
  - `prism` (refractive gem): refracts the void and grid behind, so the
    body is mostly dark with bright facet highlights; dispersion fringes use
    the slot's family and its neighbours, not a full rainbow.
- **Shards keep the look**: an edge monster shatters into edge-lit shards,
  a hologram into wire fragments, and every look's shards may flash the
  `shatter.core` white on the impact frame only.

### World

- **Floor**: `void.floor` (L* about 1), glossy enough to catch the grid's
  and emitters' reflections at low strength; no texture, no albedo detail.
- **Grid**: `grid.line` dimmed to L* 8-18 (ps-wnj.24: it drowned the swarm
  at 18-30; [Restraint](#restraint)) with every fourth line about twice as
  bright or thick (GW's every-third, scaled for our 35 m frame), spacing
  near 1.5-2 m so a monster stands on about one cell. Lines fade out with
  distance into `void.fog` so the far floor has no moiré.
- **Pulse**: `grid.pulse` is the colour a line takes under an impulse ring
  and on the beat's breath. The beat breath lifts lines only, not the cells.
- **Zones**: a zone tint replaces `grid.line`'s hue (keeping its L*) and
  tints the fog; it never lights the floor.
- **Crystal props**: formations and props are the only set dressing, in
  `shatter.gem` (the white-gold people loved) with edges at the hero's rung
  at most. They sit on the hero light rung only while whole and lit by a
  blow; their resting faces stay at enemy light so the hero still wins.

### Post

- **Tonemap**: filmic or ACES; never neutral clipping of saturated
  emitters. A saturated edge goes toward white at its core only.
- **Bloom**: a low threshold (emission over about L* 50 contributes), a
  tight core layer plus a wide soft halo layer, glow saturation above the
  base (GW's 1.5 over 1). Target: the halo rule (ring L* 15+ over the frame's
  p25) without lifting the floor rule.
- **Streaks**: optional horizontal anamorphic streaks on earned white only
  (impact, blade flare); never on monsters.
- **Grade**: the palette's; no per-shot grade. Vignette darkens toward the
  void, never toward a colour.
- **Retro** (hologram): scanlines at under 15% darkening and chromatic
  aberration under 2 px at 1080p edge, so edges stay crisp at the centre.
- **Feel**: beat pulses and impact flashes from PostFeel, coloured by the
  palette (`spark`, `shatter.core`), on the impact frame, decaying within
  0.25 s.

### VFX

- **Every effect's colour is a role**: slash and spin ribbons white at the
  core into `hero.trail`; enemy streaks in the enemy's slot; death pops in
  the dying type's slot with a `spark` core; structure bursts in
  `shatter.gem` with a `shatter.core` flash.
- **Effects are the brightest values in their region** (look.py's effects
  measure), on the earned-white rung only at their first frame.
- **Deaths are GW fireworks on the impact frame**: a pop, an expanding ring
  that writes an impulse, the shatter. No anticipation, no twinkle; shards
  then rest dimly on the floor and fade.
- **Spawn warp**: converging lines in the type's slot, at most half a beat,
  ground or enemy light only (no white), never overlapping a death.
- **Sparks**: `spark` white, under 0.15 s, few and crisp.

### Motion

- **Beat steps land exactly on the beat's frame** (the existing beat checks);
  `--beat-ripple` alone offsets them.
- **Springs**: overshoot at most ~15% of a move, settled within one beat, so
  a step reads as a step, not a wobble.
- **Swarm individuality**: phase and scale variance small enough that a
  type's silhouette stays one silhouette (scale within +-8%, tilt within
  +-6 degrees).
- **Trails and smears**: under 0.2 s of life; smears only on blows and
  lunges, so trails mark speed, not everything.
- **The impulse ring** crosses a 35 m frame in about one beat and fades in
  about one and a half; a beat's breath is one ring from the hero or a
  floor-wide lift of the lines, never both at once.

### Hero

- **The brightest thing on screen in every preset**: the hero's L* p90 at
  least 5 over the monsters' (outside impact frames). `hero.body` is near
  white so it spends no family.
- No growth and no outline (ps-b5z.1 and the operator's review); the hero
  wins on value and on its trail, not on size.
- Each preset's treatment echoes its monsters (an edge hero for `edge`, a
  wire hero for `hologram`) but on the hero light rung.

### Transitions

- A cut is one gesture in the shared grammar: a white or `grid.pulse` flash
  on the beat (earned white for at most two frames), a whip, or an impulse
  wipe across the grid. The palette and post do not change at a cut; only
  the zone tint may.

## Restraint

The operator on the first neon stills (2026-11-01): too overwhelming; the
grid drowned everything and the structural crystals flashed far too bright.
The look is **calm by default** (ps-wnj.24; the tier is
[neon-look.md, Restraint and special moments](neon-look.md#restraint-and-special-moments)):

- **A contrast hierarchy in every frame**: hero and blade first, enemies
  second, the world (grid, props, fog) last. The world sits on the ground
  light rung or under it; a prop's resting edges at enemy light, never at
  the hero's.
- **The grid is a quiet background**: lines at L* 8-18 (the bottom of
  ground light), thin, fading with distance, breathing only faintly on the
  beat. Only a big impact (a special moment, a mass kill) visibly bends and
  lights it; a light strike or a single death barely touches it.
- **Flashes are for special moments**: structure breaks and formation
  smashes, slams and finishers, drops, the trailer's designed flash cuts.
  There the post flashes, every hit lights toward white and the floor
  ripples, all on the impact frame. Elsewhere a hit lights a monster or a
  prop by a third, and the post's flash is under a third of a special
  one's.
- **Even a special moment is not a white-out**: `clipped_impact_max` still
  holds, and the flash is gone within a quarter second.

Measured by three rules below: `floor_p99_max` (the grid's lines),
`background_light_max` (the background's share of the frame's light) and
`flash_frames_max` (the share of steady stills, outside special moments, with
a near-white region over 0.3% of the frame).

## Rules and Thresholds

`look-metrics` (ps-wnj.2, ps-wnj.21; `demo3d/tools/src/demo3d_tools/look.py`)
implements these on windowless stills scaled to **960 px wide** (ring widths
are in those pixels), keyed in
[neon-look-thresholds.json](neon-look-thresholds.json) exactly as below.
Pixel classes, from look.py's `lstar`, `chroma` and `hue`:

- **colourful**: C*ab >= 30 and L* >= 20;
- **bright** (emitted light; there is no sun): L* >= 50; **emitter**:
  L* >= 70;
- **clipped**: every channel >= 250; **white**: C*ab < 15 and L* >= 85;
- **floor**: ground-plane pixels not covered by monsters, hero or structures
  (the ID render and view json look.py's stage measure uses);
- **hero**: a screen capsule from the view json's hero feet to 1.8 m above
  them, 0.5 m wide (the ID render marks only monsters and structures);
- **impact frames**: an impact frame and the next one (`--impact` names
  them).

Value and hue rules run on steady-state frames (outside impact frames); an
impact frame is judged by `clipped_impact_max` alone. Rules that need the ID
render or the view json (floor, hero, monster form, readability) are skipped
on a still without them, such as a video frame, which gets `frame_p25_max`
for the floor instead. A preset's value applies with `--preset` or when the
palette is that preset's (ion, arcade, phosphor, spectrum); with neither, the
loosest preset's value applies.

| key | rule | threshold | v2 | GW3 |
| --- | --- | --- | ---: | ---: |
| `floor_p50_max` | floor L* p50 | <= 8 | 3.8 (1.4-26) | |
| `floor_p90_max` | floor L* p90, rings and breaths included | <= 30 | 30 (10-69) | |
| `floor_p99_max` | floor L* p99: the grid's lines (ps-wnj.24) | <= 20; prism 26 | | |
| `background_light_max` | share of the frame's linear luminance off the monsters, structures and hero, under L* 70 (ps-wnj.24) | <= 0.15 | | |
| `frame_p25_max` | frame L* p25 (video fallback for the floor) | <= 6 | 5.4 (1-16) | 10.6 |
| `black_share_min` | share with L* < 10 | >= 0.30; solid 0.25 | 0.39 | 0.24 |
| `bright_share_max` | emitted-light share, L* >= 50 | edge 0.22, solid 0.32, hologram 0.22, prism 0.28 | 0.24 | 0.24 |
| `bright_share_min` | emitted-light share | >= 0.04 | 0.24 | 0.24 |
| `clipped_max` | clipped share | <= 0.02 | 0.000 | 0.011 |
| `clipped_impact_max` | clipped share on impact frames | <= 0.12 | | 0.082 |
| `clipped_blob_max` | largest connected clipped region, of the frame | <= 0.005 | | |
| `white_max` | white share | <= 0.02 | 0.001 | 0.023 |
| `halo_impact_min` | median L* in a 3-10 px ring round emitters (ring pixels under L* 70) less the frame's L* p25, on impact frames (ps-wnj.19; was `halo_min`, every frame) | >= 15 | 13 | 18 |
| `families_max` | greedy 60-degree hue windows each holding >= 8% of colourful pixels | <= 4 | 3 (1-4) | 3 (2-3) |
| `key60_min` | colourful share in the best 60-degree window, every frame | >= 0.55 | 0.47 | 0.70 |
| `key60_median_min` | the same, median over a cut or a set | >= 0.62 | 0.47 | 0.70 |
| `palette_conformance_min` | share of pixels with L* >= 40 and C* >= 30 whose hue is within 20 degrees of a palette role or zone tint | >= 0.85; prism 0.75 | 0.98 (own colours) | |
| `shot_floor_range_max` | spread of the shots' floor p50 (frame p25 without a floor) across a cut | <= 6 | 24 (floor), 16 (frame p25) | |
| `flash_frames_max` | share of a cut's or set's steady stills whose largest L* >= 90 region covers 0.3% of the frame or more (ps-wnj.24) | <= 0.5 | | |
| `hero_margin_min` | hero L* p90 less the monsters' L* p90 | >= 5 | -4 (-30-12) | |
| `monster_form_min` | monster pixels' L* p90 less p10 in each 32 px tile they cover a quarter of, the tiles' median | >= 30; solid 20 | 14 (10-18) | |
| `swarm_separation_min` | boundary contrast between adjacent same-type monsters in dense regions, dL*: seam depth in each dense 32 px clump tile, the tiles' median | >= 13 | 9.4 (3-16) | |
| `readability_min` | look.py's \|dL*\| monsters against their ring (TD15) | >= 17 | 48 (33-68) | |

v2's mask columns come from the crystal cut re-rendered with its ID render
and view json (`demo3d/evidence/look/metrics/masked`); the rest from the
study above.

Notes:

- **The key is the rule v2 fails hardest** and GW3 passes in every still.
  `families_max` is the doc's ceiling, not a discriminator: v2's soup often
  merges into three or four wide windows.
- **Calibrated on v2** (ps-wnj.21, [evidence](../../demo3d/evidence/look/metrics/README.md)):
  - `halo_min` stays 15. GW3's stills (17-26) pass with margin; v2's
    dark-floor frames, where emitters are flat paint, fail (4-12). The v2
    frames that pass do so because a lit floor lifts the ring, and they fail
    the floor rules instead. 17 would sit on GW3's lowest still.
  - **The halo is an impact-frame rule** since ps-wnj.19
    (`halo_impact_min`, [evidence](../../demo3d/evidence/look/trailer/README.md)).
    After the restraint the crystal cut's steady stills measured 0.3-12 in
    every preset; at five times edge's calm halo they reached only 6-10
    while the grid's floor p99 broke (20-27 against 20), and solid's broke
    its hero margin too. The halo now flares in a special moment
    (`halo.special`), where the impact frame measures 16-26: emitters glow
    when it counts, and a steady frame stays calm.
  - `monster_form_min` is measured per tile, a monster's own span: over a
    whole crowd p90 less p10 mixes types of different L* (v2 scores 35-84
    that way, though its bodies are flat). Per tile v2 scores 10-18. 30 is
    about twice that; solid rises from 18 to 20 so that every v2 frame fails
    it, since v2's bright crystal bodies already are the solid look viewers
    called flat.
  - `swarm_separation_min` is a contrast in L*, as this table asks, not
    ps-wnj.2's share of edge pixels. Seam depth: how far a pixel's L* (box
    averaged over 3x3, so speckle is no seam) sits under the clump within
    4 px on both sides across rows or columns; a tile's contrast is its
    clump's 90th percentile. v2's dense-clump median is 8.4 with masks (4.5
    on video frames, where shards in enemy hues join the clumps); 1.5x is
    12.5, so 13. The blob frames (20, 28, 32, 56 s) fail with and without
    masks; v2's three least dense crowds pass (8, 24, 36 s). A crowd of
    outlined discs scores 40, the same discs flat 0.
- **The restraint rules** (ps-wnj.24, [Restraint](#restraint)) are calibrated
  on stills of the look before and after its toning down, from players built
  at 6610faff and after
  ([game](../../game/evidence/look/restraint/README.md),
  [crystal cut](../../demo3d/evidence/look/restraint/README.md)).
  `floor_p99_max`: the crystal cut's grid lines measured L* 19-30 before
  (edge 25, prism 30) and 10-13 after; prism's gems halo onto the floor, so
  its own ceiling is 26. `flash_frames_max`: two thirds of the game's steady
  stills flashed before in every preset, a third to a half after; a hit's
  glint on a structure was the largest. `background_light_max` is a guard
  more than a discriminator: the game's stills measured at most 0.15 before
  and 0.13 after, the crystal cut's 0.05, while a lit Synty floor carries far
  more. The game's stills have no view json, so `floor_p99` runs on the
  crystal cut only.
- **Timing rules stay where they are**: beat steps (`game beat`, the trailer
  checks), impact-frame payoffs (death and break checks) and the rules in
  Motion above, measured from event logs, not pixels.

## The Palettes

Four palettes, one per preset, in
`unity-packages/com.playlistsurvivors.look/Runtime/Resources/Look/Palettes/`, in neon-look.md's
format (a `note` field added). All share the role structure above; they
differ in key so the four presets are four distinct looks rather than one
look four ways.

| palette | preset | key (grid, zone 1, `enemies[0..1]`) | accent `[2..3]` | counter `[4..5]` | boss |
| --- | --- | --- | --- | --- | --- |
| `ion` | `edge` | cobalt-cyan 188-232 | magenta-violet 285-326 | amber 25-39 | red 4 (counter) |
| `arcade` | `solid` | violet-magenta 262-313 | aqua 163-191 | hot orange 16-36 | red 354 (counter) |
| `phosphor` | `hologram` | phosphor green 144-171 | amber 24-44 | pink-violet 290-328 | red 348 (counter) |
| `spectrum` | `prism` | ice blue 198-234 | rose-violet 287-330 | gold 27-42 | rose 349 (accent) |

Hues are HSV degrees. Shared values:

- `void.floor` L* about 1, `void.fog` L* 2-4, both a whisper of the key hue.
- `grid.line` L* 19-27 in the key hue; `grid.pulse` L* 50-90.
- `hero.body` L* 96-100, C* <= 8; `hero.blade` white; `hero.trail` C* <= 30.
- `shatter.gem` the white-gold formations (L* 93-98, C* 9-27);
  `shatter.core` and `spark` white.
- Enemies L* 53-89, C* 35-106; in `arcade` (solid bodies) every enemy is at
  L* 78 or under, so the hero keeps its rung.
- Within a family the two slots are 14-43 degrees apart; families at least
  53 degrees apart; the boss within 22 degrees of its family.

Why these keys:

- `ion` is GW3's own blue-cyan key, the canonical neon read for the darkest
  preset; amber elites stand out as the only warm light.
- `arcade` keys violet so `solid`'s bright bodies sit on a cool, dark stage;
  aqua and hot orange are its complements.
- `phosphor` is a vector monitor's green, the hologram's natural home;
  amber and pink keep it from being monochrome.
- `spectrum` keys pale ice so refraction and dispersion read as light
  through glass; gold counters echo the formations' gem.

A preset may name another palette (`--palette`): the roles are the same, so
any look draws in any palette. The four pairings above are the presets'
starting choices.

## Reference Board

`demo3d/evidence/look/style/` holds the board: six v2 frames annotated with
the frame-level rules (`board.jpg`), the table (`README.md`) and the script
that makes both (`board.py`). Re-run it on any capture to compare:

```sh
uv run --project demo3d/tools python demo3d/evidence/look/style/board.py captures/<capture>.silent.mp4
```
