# Structures in the edge look

Rendered on jarvis (M5 Max), 2026-11-02, in the palette's first zone (cobalt) unless named. Art is judged by eye
(design.md, Judgment over rules); the numbers below are diagnostics. ps-6y2.30 put the eight Synty kinds in the edge
look; ps-6y2.43 replaced seven of them in the default look with meshes designed for it.

```sh
just champions-tool structure review all        # the sheets and review.json here (runs the recipes first)
just champions-tool stills --scene structures   # and --look trailer; --scene level
```

## What draws

- **`--look trailer`** draws each kind's Synty prefab (`Assets/Champions/Editor/Structures/structures.json`,
  ps-6y2.15's picks), combined at its height on its footprint's middle, its foundation dropped, made a style mesh as
  the monsters' are (`StyleMesh.Unweld`: its creases over 24 degrees draw) and cut into chunks outlined along their
  cracks (`MonsterShardBaker`).
- **The default look** draws a designed low-poly mesh for the cottage, hut, tower, wall, fence, pine stand and round
  tree (ps-6y2.43): each kind's recipe, `tools/recipes/structure/KIND.py`, builds it from simple faceted shapes with
  `champions_tools.structure` (boxes, prisms, lofts, slabs, wall faces with inset doors and windows), keeping the
  kind's height and footprint, and **every edge of the design draws**: each convex polygon's outline wherever no other
  polygon continues it in its plane, never the diagonals that cut it into triangles. Each recipe's docstring says what
  it draws and why. The rock keeps its Synty model at 8 degrees (below).
- `StructureEdge.shader` (ps-6y2.30): the monsters' pixel-wide lines (`MonsterLines.hlsl`) in the zone's `edge` colour at
  three times its value, flaring as the beat wave passes, and as wide a line where the structure meets the ground, over
  a flat fill at half the zone's `fill`. A hit flashes the lines; the fill lifts a little.
- A break (`StructureShatter`, ps-6y2.15's PhysX shatter; `StructureEdgeShard.shader`): every chunk launched on the
  impact frame, thrown along the blow, hottest on the break's frame and cooling over 0.6 s; they clatter down and sink
  after 2 s. A designed mesh's chunks are cut from big facets (long edges are halved along lines that never draw, so
  the cutter has triangles of a size to work with), in more pieces than the model's (36 for the cottage and hut, 40
  for the tower), and each chunk's broken side draws only its outline, where it broke: one dark polygon per cut face.
  Every edge of the inside faces drew as a fan of spokes to the chunk's apex, a starburst, to both of the first two
  critiques below; the model kinds keep ps-6y2.30's every edge.
- The rock (8 degrees): a boulder of a few big facets, closed over 8 degrees without the slivers of a near-flat face's
  triangulation at 1. Both ps-6y2.30 critiques passed it as it is.

| Kind | Height | On the ground (x by z) | Sim footprint | Triangles (trailer) | Drawn segments (trailer's lines) | Pieces (trailer) |
| --- | --- | --- | --- | --- | --- | --- |
| cottage | 5 m | 3.56 x 4.45 m | box 3.6 x 4.4 | 741 (9,547) | 275 (8,289) | 36 (20) |
| hut | 4.6 m | 4.24 x 4.90 m | box 4.46 x 4.46 | 497 (7,224) | 210 (6,778) | 36 (20) |
| tower | 10 m | 3.33 x 3.33 m | disc r 1.7 | 908 (32,196) | 472 (31,780) | 40 (22) |
| wall | 1.2 m | 5.60 x 0.60 m | box 5.6 x 0.6 | 210 (846) | 150 (855) | 14 (14) |
| fence | 1.1 m | 2.42 x 0.30 m | box 2.42 x 0.3 | 182 (276) | 84 (214) | 10 (10) |
| pine | 8 m | 4.05 x 3.84 m | disc r 1.7 | 866 (1,220) | 438 (1,103) | 36 (36) |
| tree | 5 m | 3.06 x 2.22 m | disc r 0.7 | 224 (254) | 153 (240) | 14 (14) |
| rock | 1.3 m | 3.13 x 3.40 m | disc r 1.5 | 42 (42) | 54 (38) | 12 (12) |

Triangles and drawn segments are the default look's (a designed edge halved for the shatter counts as two segments);
the trailer's lines are its creases over 24 degrees. The Synty models' triangles are what `--look trailer` still draws.

## Sheets

- [field.jpg](field.jpg): every kind together at the game camera (a 1080-row frame), the default look;
  [field-trailer.jpg](field-trailer.jpg) the same in `--look trailer`.
- `KIND-game.jpg` at the game camera's distance: the default look from three headings, `--look trailer`'s row under
  it, then the default's shatter at 0, 0.08, 0.2, 0.4, 0.9 and 1.6 s; `KIND-near.jpg` the default at half the distance: [cottage](cottage-game.jpg) ([near](cottage-near.jpg)), [hut](hut-game.jpg)
  ([near](hut-near.jpg)), [tower](tower-game.jpg) ([near](tower-near.jpg)), [wall](wall-game.jpg)
  ([near](wall-near.jpg)), [fence](fence-game.jpg) ([near](fence-near.jpg)), [pine](pine-game.jpg)
  ([near](pine-near.jpg)), [tree](tree-game.jpg) ([near](tree-near.jpg)), [rock](rock-game.jpg) ([near](rock-near.jpg)).

## The field and a break, in play

The structures scene (`StructureScene`) plays level 1's structures through the real sim on seed 41 to a scripted
120 BPM song; the hero (a white ring) walks to a tree and at 6 s dashes through it with Blade Rush.

- [field-start-0.500.jpg](field-start-0.500.jpg) (and [`--look trailer`'s](trailer-field-start-0.500.jpg)),
  [field-wave-4.270.jpg](field-wave-4.270.jpg): the field round the hero, a beat ring passing.
- [break-impact.jpg](break-impact.jpg): the impact frame; [break-flying-6.250.jpg](break-flying-6.250.jpg),
  [break-landed-7.500.jpg](break-landed-7.500.jpg): the chunks flying, then down.

## Blind critiques of the designed meshes (ps-6y2.43)

Each by a fresh agent given only images, no docs and no recipe: the review sheets and field (drawing A the designed
meshes, B `--look trailer`'s Synty models, unlabelled), from the second on the structures scene's stills, the operator's
five synthwave references and the trailer's opening-duel frame; asked ps-6y2.30's questions: does each read as a retro
3D neon wireframe (closed facets and silhouette, dark inside) or as broken lines, scribble or hatching, at game size and
up close; does each read as what it is, beside the rock and apart from the monsters; does the break read; what to
change. Their full texts are on the bead. Every one judged A better than B for every kind.

### 1. The first designs

> The structures pass as clean low-poly wireframes and fail on identity and on the break. [...] **Cottage:** A is a
> closed, dark, low-poly box at both sizes. [...] B is shingle and plank scribble with wavy broken lines. A is much
> better. **Tower:** A is the cleanest of the five: big closed octagonal facets. [...] B is a fractal gear or
> snowflake. [...] **Hut:** reads as a shed or cabin, but you can't tell it from the cottage. **Tower:** reads as a
> lantern, gazebo or pagoda. The roof hides the shaft, so it has no height. **Wall:** reads as a row of crates or
> boxcars. [...] From 0.2 s the cottage, hut and tower shards show triangle-fan starbursts.

What changed: the tower's roof narrowed to the shaft and its shaft grew to 6 m with a course line and slits; the wall
became a battered body of two courses under merlons; the hut got a hipped roof and lost its deck's planks and
balusters; the cottage lost its crossed gable boards and got taller walls under short eaves, its windows showing. Its
first ask, matching the structures' lines to the rock's, is the look pass's (ps-6y2.23, commented there).

### 2. With the stills

> Drawing A is clearly the right direction. [...] A beats B for every kind. B is the vendor's model drawn as plank and
> shingle hatching. [...] **All kinds:** the lines are hairline-thin, dim, and nearly the same blue as the floor grid.
> [...] **Cottage:** each roof slope is split into three strips, so the roof looks corrugated, like a crate. The eaves
> and gable ends draw as 3-4 stacked parallel lines, which is hatching. [...] **Tower:** clean, but every band is a
> doubled ledge line. **Wall:** the cleanest of the set. [...] **Round tree:** four overlapping balls with every
> triangle drawn, the busiest object by far. [...] **Cut faces show spokes.** Their inside edges all draw, as
> starbursts.

What changed: a designed mesh's chunks draw only their crack on their broken side (one dark polygon per cut face),
in more pieces; the cottage's roof became one closed prism (each eave and rake one line); the tower's bands one line
each; the fence's caps flat and its rails end at the posts; six pines spaced apart instead of nine; the tree one
canopy. Not taken: dimming debris and a hue off the grid's (StructureShatter's and the look pass's, commented on
ps-6y2.23).

### 3. The third round

> Verdict: the tower, wall, fence and pine stand work. The hut and the round tree fail [...] **Hut:** [an A-frame]
> reads as a tent or wedge [...] **Round tree:** Clean, but it is an icosahedron on a stick: a d20, gem or lollipop.
> [...] **Cottage:** [...] from 60° the roof is one huge empty quad, half the object. [...] **Pine stand:** The best of
> the set: tiered cones, closed facets, a clear cluster. [...] Hut: replace the A-frame with a squat six-sided drum, a
> low wide cone roof with overhanging eaves, and an outlined door.

What changed: the hut became that drum under a two-pitch roof with a smoke vent; the tree a low canopy of three
lumps on a longer trunk; the cottage's slopes got a course line and a ridge cap; the wall's merlons went flush and its
joints regular; the fence's rails square beams. Not taken: shards with every edge drawn (the second critique's spokes)
and debris that clears by 2 s (StructureShatter's, ps-6y2.23).

### 4. The last round

> **Structure critique: drawing A beats B on every kind, but the hut, round tree, pine stand and the break need work.**
> A wins on every kind. It has closed facets, dark bodies and clean silhouettes. B is the vendor detail, and on every
> kind it turns into hatching or scribble [...] **Cottage:** The eaves, fascia and ridge are drawn as two or three
> near-parallel thin lines. **Tower:** The plinth and eave rings repeat that doubling. The windows are tiny and vanish
> at in-game size. **Fence:** [...] each rail is a pair of hairlines. **Round tree:** [...] Where its two canopy balls
> intersect, short stray stubs show. **Pine stand:** Close up it works. At in-game size the overlapping cones pile up
> into a knot of crossing lines. **Wall:** Clean. [...] All of them read as solid. [...] **Tower:** Reads as a tower,
> though a bit like a lighthouse. **Wall:** Reads as a stone wall, if a little like Lego. **Cottage:** [...] a box
> with a chimney. **Hut:** Reads as a gazebo, a lampshade or a hex nut. It also shares the tower's octagon-and-flared-
> cone form. **Round tree:** Reads as a 20-sided die on a stick. **Pine stand:** In play it reads as a cluster of
> crystals or missiles. [...] The impact frame is good [...] The cottage, hut, tower and rock break into flat,
> one-sided shards with saw-tooth edges. They read as torn paper or foil, not rubble. [...] The tree, pine and wall break
> into their own parts (cones, balls, trunks, blocks), and that reads far better.

What changed (the sheets and stills here are after these): the cottage lost its ridge cap (the third critique's ask,
its doubled lines this one's complaint), the tower's plinth became one sloped band and its slits 0.44 m wide, the
pines five wider, squatter trees, the fence's rails 0.16 m beams and the whole fence as deep as its footprint. Not
taken: a fifth hut and tree (the critiques pulled each way: the third asked for exactly this hut, the second and third
for a canopy of lumps; see Known), thick closed shards along designed seams with gravity and a fade, and outlines
brighter than inner edges (StructureShatter's and the look pass's, commented on ps-6y2.23).

## Known

- **Lines are thin and dim and share the grid's blue**: every critique's first complaint. The designs draw far fewer
  lines than the Synty models (the cottage 275 segments against 8,289), many one-sided at silhouettes and rims, under
  the same `StructureGains` (lines at three times the zone's `edge`); brighter, wider lines and a hue of the
  structures' own are the look pass's (ps-6y2.23, commented there), as is the chromatic flash's fringe on breaks.
- **The break**: the impact frame reads well in every critique; after it, a designed mesh's chunks are thin patches
  closed to an apex (`MonsterShardBaker`'s method for open meshes), so its cut faces draw either as fans of spokes
  (every edge, ps-6y2.30's rule) or, as now, as one outlined dark polygon each, which the last critique read as "torn
  paper or foil"; and debris neither dims nor clears before it sinks at 2 s. Cutting designed solids into closed
  prisms along their seams, and the debris's fade, are for the shatter's owner (noted on ps-6y2.23).
- **The hut and the round tree are the weakest identities.** The critiques read the hut's four designs as the
  cottage's twin, a gazebo, a tent and, last, a lampshade or the tower's cousin, and the tree's three as dice, a d20
  and a d20 with stubs where its lumps cross; each still judged them better drawn than the Synty models (whose tree is
  "an egg, a geode", "a cracked blob"). The operator's eye decides (operator-pending.md).
- The round tree's `defaultLook` (12 degrees) in structures.json goes unused while its recipe exists; it is kept for
  the model, should the recipe go.
- Variants 275/300 (`champions check --variants`); EditMode 112/112; tools 171.

## Earlier: ps-6y2.30's critiques of the Synty models

Each by a fresh agent given only the images (the stills and sheets of the Synty models in the edge look, the operator's
five synthwave references and the trailer's opening-duel frame), no docs.

### 1. The first material (ps-6y2.30)

Its verdict, condensed to its findings (the full text is on ps-6y2.30): the structures "don't read as retro neon
wireframe yet. Only the rock, the round tree and the pines get close, and all of them share one problem: a lit navy
body under a dim blue line."

> - **Cottage: fails.** It reads as shaded blue clay. The bevelled, lumpy roof slabs carry crease fragments: short
>   dashes and "crack" strokes that never close into facets.
> - **Hut: fails, a little less badly.** The deck planks are the best linework on any building, but the roof is the
>   same clay-and-cracks as the cottage.
> - **Tower: fails worst.** Hundreds of tile edges make a sawtooth fringe. Close up it's lacework, and at game scale
>   it's scribble.
> - **Wall: fails.** It looks like a strip of gravel. **Fence: half-succeeds close up**; at game scale it nearly
>   vanishes.
> - **Pine cluster: closest to success per tree.** The stacked cones have closed facets and clean outlines. But the
>   trunks are single unfaceted lines.
> - **Round tree: mostly succeeds.** **Rock: the best of the set.** Big facets, a closed outline, crisp lines.
> - They are distinct from the monsters, but for the wrong reason: they look like a different renderer. Monsters are
>   bright lines on black with bloom; structures are lit navy solids with weak, barely glowing lines.
> - The break: the impact frame works; in the flying frame many shards are "dark slabs with missing or partial edges".
>
> Its changes, in order: fix the material (a near-black, unshaded body; brighter lines with bloom; a hue apart from
> the floor's); designed low-poly meshes for the cottage, hut, tower, wall and fence, every edge drawn; a shatter of
> closed shards with every edge lined, fewer and bigger, hotter while fresh.

What changed in response: the fill went flat at half the zone's `fill` and the lines to three times its `edge`
(the material), the shards' inside faces draw every edge and fly hotter (2.5 times the lines on the break's frame),
in fewer pieces (10 to 22 a kind), and the cottage and hut went back to 24 degrees (45 broke their outlines once the
fill was dark). Not taken here: the hue (the zone's key is the design's: design.md, Zones, "Structures take the new
key") and the designed meshes (ps-6y2.43, above).

### 2. As landed (ps-6y2.30)

Its verdict on the structures as landed, condensed (the full text is on ps-6y2.30):

> - **Rock: passes, the best of the set.** It has big facets, a closed outline and a dark body, the same idea as the
>   low-poly mountain references. [...] the lower outline is missing: the side faces fall into darkness with no edge.
> - **Pine cluster: mostly passes.** The stacked faceted cones have clean closed facets, much like the palm reference.
>   But each trunk is a single hairline [...] the cluster looks like a volley of rockets.
> - **Round tree: passes on style, fails on identity.** The silhouette is closed and the body is dark, but [...] it
>   reads as an egg, a geode or a second rock.
> - **Cottage, hut, tower: fail.** These are detailed meshes with only their sharp edges drawn. The roofs break into
>   short open dashes that end in the middle of a face, like cracks. The hut's plank decks become barcode hatching.
>   The tower roof is hundreds of tiny tile boxes, a lace doily. The bodies are dark, which is right, but the lines
>   never close into large facets.
> - **Wall: fails.** Dozens of small stones turn into scribble. **Fence: fails.** The rails are double hairlines.
> - They separate from the monsters by hue and by brightness. That ranking is right: set dressing sitting under the
>   cast. The problem is the floor. Structures and grid share a hue, so the structures sink into the floor.
> - The break: "On the impact frame (04), the flash to bright cyan with bloom plus the grid ripple is the strongest
>   moment in the set. The angular rock-like shards [...] are good closed outlined pieces." But "the pine stacks fly
>   off as whole trees", and the red and green fringing doubles lines near the frame's edge.
>
> Its changes, in order: designed low-poly meshes for the cottage, hut, tower, wall and fence, every edge drawn (and
> faceted pine trunks, a lobed crown for the round tree); brighter resting lines and a hue of the structures' own;
> more, smaller shards thrown every way.

What changed in response: the line along the ground closes every outline at its base (the rock's), and the pine
stand breaks into 36 pieces, so its tiers come apart. Those stills and sheets were replaced by ps-6y2.43's.
