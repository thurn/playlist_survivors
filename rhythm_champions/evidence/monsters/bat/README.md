# Monster: bat

Written by `champions monster review bat` and `champions monster review bat --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/bat.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 182/182/182 in the default look (the trailer's 3092/1662/832), 27 shards, 74 bones, standing 0.753 m.

## The recipe

The bat (ps-6y2.33): rebuilt from a few simple shapes, its wings membranes whose fingers and scalloped edges are
its lines.

The trailer's drawing of the vendor mesh reads as a bat in a crowd by its finger-spoked, scalloped wings, but it draws
as an X-ray sketch rather than a faceted model: every finger bone two or three hairlines, the body, head, teeth and
feet a knot of short strokes (at game size a bright smudge), the ears, pointing up and back, foreshortened to stubs
from the game's camera, and the wings' curved membranes washed with the fresnel rim. A 0.3 m swarmer is a few dozen
pixels at game size, so its wings' shape is nearly all it has. So the whole figure is new, every line chosen (a true
wireframe of its topology), 182 triangles at every LOD where the vendor's are 3,092, 1,662 and 832:

- The wings: each a thin plate between the vendor's bones (a top and a bottom layer, a rim between), drawing its
  leading edge from the shoulder to the wrist, a stubby claw at the wrist, three fingers from the wrist to their tips
  and a fold from the elbow, and a trailing edge that sweeps in a curve between each pair of points; no line inside a
  panel. Skinned by transfer from the vendor's membrane (its four nearest vertices of the arm, hand and body bones on
  its side), so it flaps with the vendor's clips. Never the legs: a membrane that followed them drew two spikes when
  they hang.
- The body: a long spindle of diamond rings, broader than the vendor's, the wings' roots inside its shoulders and its
  sides above them, drawn by its four long edges (its rings crossed the ridge in an X).
- The head: a double wedge pointed at the snout, holding the body's neck, its back on the neck's bone so a bite
  that stretches the neck doesn't leave it floating. The ears: tall three-sided spikes, up and back as the vendor's.
  Both a quarter larger than the vendor's: smaller, they were a knot too small to read as a head at game size.
- Dropped: the tail membrane (its outline closed into a ring over the head in the hovering pose), the legs and feet
  (hairline spikes at game size, too thin to draw solid), the eyes and the teeth.
- Every triangle on its own corners with its facet's normal: normals smoothed across a wing's fold lit the panel
  facing the camera as a glowing wedge under the fresnel rim.

Tried and dropped: a wing split into four times the triangles (it bent more smoothly, and its panels glowed all over
and its shards broke into scratches), deeper scallops (the wings read as zigzag bars), ears tilted out (a crown below
the head), and legs.

Source: Magic Pig Games' Bats LP.prefab (sha256 dcb6e090ea21acaad3efd3cf9264c2fedd7f543b5a767cecb7c5a38c6dd39611) and
Bats LP.fbx (e06900ac2ca26a8d9a4af69398069851eb8821d6af68f3bcad4594b57e0160ec), as manifest.json lists them. No
Blender: numpy only. Bake: `just champions-tool monster bake bat`; review: `just champions-tool monster review bat`
(and `--look trailer`).

## Notes

ps-6y2.33, by the monster-mesh-design skill. The default look draws the recipe's mesh: every one of six blind critiques
judged it the look aimed for, closed facets over dark panels in a crisp silhouette, and the trailer's drawing an
X-ray sketch of scratches; on which reads more quickly as a bat at game size and in the crowd they split (two for the
new mesh, three for the trailer's, one each way), and that gap is the open question for the operator. `--look trailer`
is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

The trailer itself shows almost no bats (its crowds are skeletons, goblins and orks), so the reference is
`trailer-*.jpg`. The vendor bat reads in a crowd by its wings: finger bones spoking out from the wrist and a scalloped
trailing edge. But it draws as a sketch rather than a model:

- every finger and arm bone is two or three parallel hairlines, and the membranes between them are full of short ribs;
- the body, head, teeth and toes are a knot of short strokes, a bright smudge at game size;
- the ears point up and back, foreshortened to stubs from the game's camera; seen from the review's camera in front,
  the feet's clawed toes at the top of the figure read as the ears (critiques called them "clawed hands");
- the tail membrane closes into a ring over the head when it hovers;
- the curved membranes catch the fresnel rim in blue washes.

A 0.3 m swarmer is a few dozen pixels at game size, so its wings' shape is nearly all it has: the new mesh is built
around them.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game, near with the shatter, crowd), labelled A and B at random,
no docs, no recipe, no numbers, asked the skill's question.

1. First draft (wings as plates with fingers and shallow scallops, a small spindle body, a rounded tail membrane; the
   new mesh was A): "I prefer A, which reads as a retro neon wireframe ... At game size and in the crowd, A reads as a
   clean origami bat. B reads as a bat with a bright scribble for a body." Of A: "The body is too small and is not one
   shape", "The head barely reads", "An empty loop sits above the head" (the tail membrane, lifted over the head in
   the hover), "Some wing faces glow". Done: the tail membrane dropped; the body broader, its sides above the wings
   so it draws over them; the head a quarter larger; the wings freed from the legs (their inner edge followed the
   legs and drew two spikes).
2. (New mesh B): "B is the right style and the better base", but "In the crowd, A wins on species read": "The wings
   aren't bat wings ... straight trailing edges", "The head dominates", "There's no torso". Done: the scallops drawn
   deep, the ears smaller, the torso longer.
3. (New mesh A): "A is much closer to the target style" but "B reads better as a bat": the deep scallops made each
   wing "a leading-edge bar with a triangle at the tip ... a lightning bolt"; the head "hangs under the body, joined at
   a single point"; "a long thin line runs from the body up to a flying chunk" in the shatter. Done: the scallops
   rounded into curves of one to three points at half the depth; a neck ring joining the head; the tail shorter.
   Tried: every membrane triangle split in four (to bend more smoothly and break into smaller shards); its panels
   glowed all over and its shards broke into scratches, so it was dropped.
4. (New mesh B): "B is the style you're after; A isn't. At game size and in the crowd, B also reads better." Of B:
   "The head and body are a plain kite or diamond with an X drawn through it", "The inner wing is empty, with one
   sagging line", "Small Y-shaped hairline spikes at the wrist claws". Done: the body drawn by its four long edges
   only; the head a double wedge; the ears plain triangular spikes; a fold from the elbow to the trailing edge; stubbier
   claws.
5. (New mesh A): "A is the style ... It just isn't a bat yet" ("folded paper, like origami cranes"); "B is the anatomy.
   Its ears, head, fingers and scalloped membranes read as a bat at once". Of A: "There's no head", "The wing facets
   ignore the anatomy". Tried: legs as slim spikes ending in the claws, the cue the trailer's feet give; in the crowd
   they drew as faint dangling hairlines (too thin at game size to draw solid), so they were dropped. The ears turned
   back up and back as the vendor's (tilted out, they made a crown below the head).
6. (New mesh A): "A has the right construction, B is the better creature ... As the style, A wins easily." Of A: "No
   head", "In pose 3 the hanging leg and tail piece floats below the body as a separate paper dart" (the head in the
   bite), "A few panels are filled", "The shatter has a scratch". Done: the head and ears a quarter larger than the
   vendor's, the head's back on the neck's bone so the bite doesn't leave it floating.

Also done between critiques: every triangle on its own corners with its facet's normal. Normals smoothed across a
wing's fold lit the panel facing the camera as a glowing wedge under the fresnel rim; flat, the wings stay darker.

### Still weak

- The creature read: from the review's camera in front of the bat, its head points nose-on, a wedge and two spikes,
  and the hovering pose has no feet at the top of the figure as the vendor's has; critiques read the new bats as
  origami cranes as often as bats. The wings' scallops and spokes carry it from above and in the crowd.
- Wing panels seen nearly edge-on still glow under the fresnel rim (rim gain 2 at power 4, the same shader in both
  looks); a flat panel glows evenly where the vendor's curved one glows in bands.
- The shatter barely spreads, in both looks: the review stands every monster on the ground (a flyer's hover is in its
  position) and the throw scales with the type's 0.3 m height while its wings span 1.5 m, so the pieces land at once.
  In play a bat breaks at its 1.3 m hover and its pieces fall. Thin plates also break into a few thin slivers.

### Cost

182 triangles at every LOD (a mesh that flags its edges isn't decimated), where the vendor's are 3,092, 1,662 and
832.
