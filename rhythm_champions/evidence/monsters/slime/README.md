# Monster: slime

Written by `champions monster review slime` and `champions monster review slime --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/slime.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 88/88/88 in the default look (the trailer's 1428/767/384), 27 shards, 8 bones, standing 0.911 m.

## The recipe

The slime (ps-6y2.32): the vendor's smooth dome rebuilt as a low-poly blob that looks up with one big eye.

The trailer's drawing of the vendor slime has no facets: its dome's 470 even triangles never crease past 24 degrees,
so it draws as a soft rim (a bubble, an egg or one of the game's rings) with the eye socket's fan of creases as
radiating lashes (a sun icon at game size), and nothing says creature unless a slime faces the camera. What changes:

- The body: eight sides, every edge drawn (a true wireframe), 1.5 m across and 0.9 m tall. It sags: widest low down,
  over a base tucked under it, so its lower outline is a bulge and no upright band reads as a pot or a jelly mould;
  it narrows to a shoulder ring and closes under a crown, a face in front and a lopsided corner behind, rather than
  an apex, whose spokes read as an umbrella or a spider web from the game camera. It is 10% longer behind, where the
  vendor's body drags.
- The face: one quiet panel on the top front, tilted 55 degrees up, so the game camera sees the eye from every
  side and not only when a slime faces it. The eye is an almond lens 0.53 m across (a third of the body, more than
  twice the vendor's eye), drawn by its rim alone, with a raised slit pupil whose sides fill it in at game size. It
  is printed on the face: each of its vertices blends the face's corners' weights, so it follows the face's hop,
  drag and squint without sinking or sliding.
- Vertex normals lean up, so the big facets turned across the view stay dark rather than lighting whole in the
  fresnel rim, and the lines carry the outline.
- Dropped: the brain and veins inside the dome, which the dark body hides in every view, the vendor's eyeball and
  its lids, and the bottom: tucked under the bulge, it is out of every view, and its fan only drew spokes in the
  shatter.

88 triangles at every LOD, where the vendor's are 1,428, 767 and 384, skinned to the vendor's body bones (root,
drag, top and eye region) from its nearest vertices, so the slime moves with the vendor rig and clips.

Source: Magic Pig Games' Slime_LP.prefab (sha256 b288527883abb23512cb8f31d5206d11f025527683d52b657ae63ad7bf1ac92d)
and Slime_LP.fbx (41162a2653ba20e6a65565640b3c51a475df2c69387b9bb5f113c66ad4830d2e), as manifest.json lists them.
No Blender: numpy only. Bake: `just champions-tool monster bake slime`; review: `just champions-tool monster review
slime` (and `--look trailer`).

## Notes

ps-6y2.32, by the monster-mesh-design skill. The default look draws the recipe's mesh: five blind critiques judged it
the better read at the in-game size and in the crowd. `--look trailer` is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

The vendor slime is a smooth dome (1.43 m across, 0.9 m tall, 470 even triangles) with a dragging back, a small
eyeball on its own bone in a socket of lids, and a brain and veins inside that the dark body always hides. No crease
on the dome passes 24 degrees, so the trailer's drawing has no facets:

- the outline is the fresnel rim, a soft band that thickens and breaks into short blotches where the dome's even
  triangles turn across the view: a bubble, an egg, or one of the game's rings;
- the only inner lines are the eye socket's fan of creases, drawn as radiating lashes (a sun icon at game size, or
  legs: "ticks" in a crowd), and one stray arc on the crown that reads as a highlight;
- in the crowd the bodies separate cleanly as eggs or jellybeans, and only those facing the camera show an eye;
- its shatter: zigzag crack strokes inside a round rim that stays standing after the break.

What names it: a soft round blob on the ground, one eye, the stretch into an egg as it moves (the vendor's drag and
top bones, which the new mesh keeps).

### What was tried

Each by `champions monster review slime --out <scratch>`, judged at game size and in the crowd:

- A dome of rings and an apex (ten sides, three rings): a 3D wireframe, but from the game camera the apex's spokes
  and the rings read as an umbrella, a wheel or a spider web, a range ring with spokes. Every apex tried since (a
  gumdrop's peak, a drop's tip) did the same.
- The meridians' pole turned forward onto the eye: lanterns or seed pods, and in a crowd a lattice.
- The vendor dome decimated in Blender (40 symmetric triangles): the cleanest shatter, but "a twenty-sided die or a
  crystal".
- A flared skirt with drips (asked for by three critiques): from above, the skirt, waist and shoulder rings nest
  into concentric polygons (a rose, a web) and the crowd turns back into a net. The base is tucked under the bulge
  instead, so the bottom tier is out of view.
- A soft peak over the crown, and rings made slightly uneven: the peak's three lines read as a gem's cut, and the
  unevenness doesn't show at game size.
- Big facets turned across the view lit whole in the fresnel rim (the shader weighs the facet's normal 60%): the
  vertex normals lean up, which keeps every facet dark.
- The eye: a lens on its own bone stuck out as a nut, or sank, when the body moved apart from the vendor's eye bone;
  skinned vertex by vertex it stretched into a sliver in the walk. It is now printed on its face panel by the
  corners' weights, standing 5 cm off it so the panel's fold never covers it.

### Blind critiques

Fresh agents given only the sheets (far, game and near poses with the shatter, and the crowd), labelled at random,
no docs, no recipe, no numbers, asked the skill's question with the slime named.

1. Rings and a ridge top, the eye low on the front (new mesh A): "A is in the right style but looks like a gem; B
   looks soft but isn't in the style", and "at game size A reads more as a creature; in the crowd B separates
   better". Of the eye: "a small hexagonal nut that sticks out sideways ... in the crowd the bodies read as grenades
   or acorns". Done: the eye moved up onto the top front, larger.
2. Four at once, the trailer's among them: the ridged mesh ranked first and the trailer's second ("a hazy,
   stair-stepped rim, a floating highlight arc and an eyelash starburst: scratches with no facets"). Of the best:
   "the bottom ring of tall upright panels ... look like a jelly mold or a lampshade", "too many lines at game
   size", "the eye sits too low". Done: the base tucked under the bulge, the eye on a face tilted up.
3. The tucked base and the high eye (new mesh B): "B is the right base ... It reads as a creature at game size and
   in the crowd much better than A. But it looks like a gem, a geodesic dome, a soccer ball or a turtle shell", and
   "the hexagonal socket with nested rings reads as a bolt head or nozzle". Done: the face as one quiet panel and an
   almond eye.
4. The almond eye with a brow (new mesh B): "B is the right direction and reads better at game size and in the
   crowd. A doesn't reach the target style at all." Of the eye: "reads as a rose, a spiral or a cut gem. The
   offset, skewed octagon rings don't close into clean loops ... drop the second ring". Done: the brow dropped,
   leaving two closed shapes, the almond and the slit.
5. The mesh as delivered but for the last two changes (new mesh A): "A is the one in the style. It is a closed
   low-poly shell, its edges meet into whole facets, it is dark inside, and the outline holds at every size and in
   the crowd ... A reads better at game size and in the crowd: every body is clear and countable. But neither reads
   as a slime. A looks like a cut gem or a turtle shell." It asked for an eye of 35-40% of the body's width and
   said "the base's triangle fan shows as spokes through the body" in the shatter. Done: the eye from 31% to 35%
   of the width, the bottom dropped. Not done: its gumdrop with a peak and uneven rings (see What was tried).

### Still weak

- It reads as a creature (every critique) but more as a faceted gem or shell than as goo: the cost of a low-poly
  blob drawn by every edge. Its eye, its sag and its motion carry the slime.
- The hop: idle, walk and attack differ little at game size. The vendor's clips stretch it into an egg in the walk
  and squint the eye in the attack; a squash on the beat's landing would be the crowd's or a clip's work, not the
  mesh's.
- Inner lines are as bright as the outline (the shader's gains, the same in both looks), so touching bodies in a
  dense crowd can still merge into a net.
- The shards: open slivers and V's from the shatter's crack outlines, and one seen edge-on reads as a bright slab,
  in both looks (the shard baker, not the mesh).
