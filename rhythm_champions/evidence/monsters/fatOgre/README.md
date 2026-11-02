# Monster: fatOgre

Written by `champions monster review fatOgre` and `champions monster review fatOgre --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/fatOgre.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, charge, smash, stomp, throw, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, charge, smash, stomp, throw, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 968/968/968 in the default look (the trailer's 18897/9786/4990), 40 shards, 56 bones, standing 4.838 m.

## The recipe

The Fat Ogre (ps-6y2.39): the vendor's fat ogre rebuilt whole as a few simple faceted shapes, every line chosen: a
big gut, a small head sunk between massive shoulders, long heavy arms, stumpy legs and a spiked mace.

The trailer's drawing of the vendor mesh is a colouring-book contour of a smooth sculpt (ps-6y2.29's critique): its
18,897 triangles of muscle, belly folds and fat rolls crease near 24 degrees, so its lines break into dashes over the
body and the face and its facets never close; a haze of rim fills the shoulders and the gut; the mace head is a knot
of rope and stone; and its shatter throws curled, rounded scraps. What names it reads anyway, as an ogre at once: the
gut, the strap across the chest, the bald head slung low with its grimace, the mace. So nothing of the vendor mesh is
kept; every part is new, skinned to the vendor bones:

- The body: one ten-sided shape from under the gut to the neck, the gut wider and further forward than the vendor's
  and sagging over the crotch, the hunched back and the shoulders sloping up to the neck above it. Drawn by its long
  lines and two rings only: the fold under the chest, in front (the gut's top; its bottom is the outline), and a
  collar round the neck, which closes the long lines over the hump (without it they ended in mid-air, rays round the
  head). More rings read as a barrel, a turtle shell or a stack of tyres; the gut's widest ring as a crate's bottom.
- A broad strap from the right shoulder across the chest and the gut to the left hip and round the back, where a
  tilted plane cuts the body: two lines, the vendor's harness simplified to what names it.
- The head: a skull a fifth larger than the vendor's, sunk between the shoulders, a dark dome behind a scowling brow
  with one crest across it from side to side (more lines on the pate read as a visor, a pyramid or a helmet, none
  as a hole framed by the collar); a face sloping back 32 degrees from the muzzle to
  the brow, so it shows from in front and not from behind, with two slanted eye slits and a nose; an underbite jaw
  wider than the skull, jutting past the muzzle, with two tusks; big drooping ears, leaves facing up, which show from
  every side.
- The arms: one six-sided loft each through the vendor's joints to a big fist, heavy at the shoulder and the forearm,
  lines round it only at the elbow and the wrist. The legs: one loft each, a trouser hem flaring at the shin, and big
  wedge feet.
- The mace: on the vendor's axis and length, a six-sided shaft thickening to a barrel of a head with six fat studs,
  three round it at each of two places, their feet hushed (thin spikes read as scratches, one pointing at the
  camera as a target).
- Vertex normals lean up (0.4, the arms 0.55, the ears' tops 0.8), so big facets turned across the view stay dark
  instead of lighting whole in the rim; not further, and not the pate's: a shard turned over then shows a face whose
  normals point away, which the rim draws solid.

Each shape draws its polygons' outlines, never their fans' diagonals: 968 triangles at every LOD, where the vendor's
are 18,897, 9,786 and 4,990. Each shape is skinned by transfer from the vendor's nearest vertices over its own bones
(the body and the strap over the pelvis, spine, belly, neck and clavicles; each limb over its own side's), the head
and ears rigid on the head bone, the jaw on its own (Ponytail1), and the mace on the weapon bone that carries the
vendor's (MORGENSTERN_TRUE), carried, so the shatter flings it.

Source: Heroic Fantasy Creatures' FatOgre.prefab (sha256
b78fae5ec223865b6d86edc3ed7da49459f04d45b7cdf303479e1868e5c20bf1, as manifest.json lists it) and its mesh,
SK_FatOgre.FBX (1d523c97663fd3ed5471d350a18852c9c7e32e79da0868bf2e44c6774376d5df). No Blender: numpy only.
Bake: `just champions-tool monster bake fatOgre`; review: `just champions-tool monster review fatOgre` (and
`--look trailer`).

## Notes

ps-6y2.39, by the monster-mesh-design skill. The default look draws the recipe's mesh: all seven blind critiques
judged it the look aimed for (closed facets, a closed silhouette, dark inside) and the trailer's drawing a contour
sketch of a smooth sculpt ("comic ink", "a pencil sketch"). The creature is closer: the first five read the trailer's
drawing as the clearer ogre (its gut, strap, bald head and grimace) and the new mesh as a brute, troll or golem; the
last two, after the changes they asked for, called the new mesh "the look and the better creature" and "about as
well as B at game size and clearly better in the crowd". `--look trailer` is unchanged (the vendor mesh).

- [duel.jpg](duel.jpg): the boss in its duel (`champions stills --scene duel-ogre --scene duel-ogre-death`), cropped
  round it: `--look trailer` above, the default below; the duel's opening, the smash, the stomp, the charge and its
  death a quarter second after the break.

### The trailer's drawing of the vendor mesh

Not in the trailer itself, whose brutes are the big orks and trolls (blocky armoured segments, which ps-6y2.29's
critique judged far better); watched on these sheets, in the duel stills and in ps-6y2.29's `ogre.jpg`. The vendor
ogre is a 5 m sculpt of 18,897 triangles: a huge gut, massive shoulders and traps with the bald head sunk between
them, a grimacing open mouth with teeth, a harness of straps over both shoulders to a ring on the chest and the back,
ragged trousers, bare feet with toes, and a morning star of a log bound in rope with a stone spike. In the edge look:

- its muscle curves, belly folds and fat rolls crease near 24 degrees, so the lines break into dashes over the body
  and the face, and no facet closes: a colouring-book contour, a "comic-book bodybuilder" in the crowd;
- a haze of rim fills the shoulders and the gut, and at game size the figure smudges into an orange blob with a dark
  face hole;
- the mace head is a knot of rope loops; the shatter throws curled, rounded scraps like burnt paper.

What reads, and names the type: the gut, the harness and its ring, the small bald head slung low, the heavy arms and
the mace.

### What was tried

Each by `champions monster review fatOgre --out <scratch>`, with a numpy z-buffer preview of the drawn edges at rest
over the vendor's silhouette before it:

- A first figure of the vendor's proportions (an eight-sided body, every ring and long line drawn) read as a fridge:
  a gut and a chest as wide as each other. The gut went wider and further forward than the chest, ten-sided.
- Every edge of the body drawn read as a grid, a turtle shell or a pumpkin. Lines round the back went first: then
  the back's long lines ended in mid-air at the hump, rays round the head in the charge and the smash; a collar round
  the neck closes them. Then the rings in front went, one by one, as critiques read them as a barrel, a beehive and
  stacked tyres; the last, the gut's widest, read as a crate's bottom edge over skirt panels, and the line that stays
  is the fold under the chest, the gut's top.
- The big facets of the gut, the shoulders and the arms lit whole in the rim. Vertex normals leaning up (0.6) kept
  them dark but drew turned-over shards solid in the shatter; 0.4 (the arms 0.55) keeps both. The pate, leant, flew
  off as a solid slab: it isn't.
- The head took the most rounds. A ring-lofted cage, a block with a pentagon roof, a face sloping up toward the camera
  (55 degrees showed it from behind too, over the pate; 32 shows it from in front only), a ridge front to back (a
  visor slit), a pate of a few facets (a pyramid, a gem), a broad hexagonal head (a helmet with a visor), a quiet
  dome (a black hole, or a hole framed by the collar from above). It ends a little larger than the vendor's, a dark
  dome behind a scowling brow with one crest across it from side to side, an underbite jaw wider than the skull,
  fat short tusks and big drooping ears facing up. Long tusks poked above the head from behind as a cat's ears (the
  jaw's front is higher on screen than the pate from behind); thin ones read as whiskers.
- The strap: one sash from the right shoulder to the left hip; then the vendor's yoke, a strap over each shoulder to
  a round plate on the chest and the back (the plate read as a keyhole, the straps were lost behind the head); then
  the sash again, which the critics asked for back.
- The mace: thin needles for spikes read as scratches, and the end spike, pointing at the camera, as a target; six
  fat studs on a barrel of a head, their feet hushed.
- The fists were separate boxes ("a kit of boxes with gaps"); each arm is now one loft to the knuckles.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near poses with the shatter, and the crowd), labelled
A and B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New A.) "A is clearly the better drawing of the style ... A is a true low-poly wireframe. The edges join into
   closed facets, the inside is dark and the silhouette is closed at every size." But "B reads better as a specific
   creature, because the big belly, harness and bald head say 'ogre'"; A "also suggests a stone golem". Asked: fewer,
   longer panels on the torso and "one diagonal strap"; a head a quarter larger with an underbite and no slit; fat
   studs on the mace; bigger feet. Done: the back's rings hushed, a sash, the head 1.22 times larger without its
   ridge line, six fat studs on a barrel head, bigger feet and thicker shins.
2. (New A.) "A is better at every size, and it's the only one in the target style. B breaks into scratches." Asked:
   cut most of the rings ("a turtle shell or beehive"); a head that isn't "a can or top hat" from behind ("add ogre
   features that show in silhouette: a heavy brow, ears or tusks sticking out, and a head sunk lower"); larger face
   marks; lean the rim-lit slivers on the shoulders and arms. Done: the shoulder ring hushed, the back of the head
   quiet, the head sunk 12 cm, big ears, larger eyes, the arms' normals leant further.
3. (New B.) "A matches the aimed look poorly and B matches it well ... It wins clearly in the crowd." But "a hunched
   golem or armoured brute than an ogre". Asked: split the crown, "one flat, unlit polygon", into facets; "keep one
   belt line" on the belly; lean the club and the brow; the crown flew off "as a solid, lit hexagonal slab". Done:
   the pate in a few facets, one ring on the gut, the club leant, the pate's normals left alone.
4. (New A.) "A is the better drawing in this style. B reads more clearly as an ogre." The faceted crown read as "a
   pyramid ... a hood, a gem or a diamond on the chest"; it saw no gut and no strap. Done: a broad head; the vendor's
   yoke of two straps and a chest plate; the gut further forward; each fist joined to its arm.
5. (New B.) "B is the right style. A reads better as a creature": the broad head read as "a helmet ... a visor", the
   torso as "a crate. It has a flat bottom edge and rectangular skirt panels", the plate as a keyhole; "add the gut
   ... and a diagonal strap. Those two features are why A reads as an ogre." Done: the sash back, the plate gone, a
   rounder gut sagging further, its widest ring hushed for the fold above it, a smaller skull with a quiet pate, a
   wider jaw and tusks.
6. (New B.) "B is the look and the better creature. A is an ink sketch of a creature, not a wireframe model." Asked:
   the head read as a hole framed by the collar from above ("replace the ring with a ridge line across the skull");
   the back "gridded like a shell"; thin tusks as whiskers; brick feet. Done: a crest across the pate, two fewer long
   lines on the back, fatter and shorter tusks, the feet narrowed to the toes.
7. (The mesh as delivered, new A.) "I'd go with A. It reads as a creature about as well as B at game size and
   clearly better in the crowd ... A looks more like a stone golem or gorilla, B like a fat bald ogre with a chest
   strap." Its asks are what still looks weak, below.

### Still weak

- The species. Every critique read a hunched brute with a spiked club; the trailer's sculpt says "fat ogre" through
  its anatomy, and the new mesh reads as an ogre, a troll, a golem or a gorilla. A low-poly gut seen from 60 degrees
  above is a barrel unless its lines say otherwise, and each set of lines tried on it read as hoops, a shell or a
  crate.
- The head is mostly its pate from this camera: a dark dome that reads as a helmet or a lid to some (critique 7), and
  its face (brow, eyes, jaw, tusks) is small at game size. The operator's eye should settle it (operator-pending).
- The sash and the fold under the chest can read together as a belt and a hoop (critique 7).
- In the charge, the gut's long lines hang below the body with no line across their ends: the gut's bottom is drawn
  by the outline alone.
- Some side facets still glow in the rim (the hump in the stomp, the outer arms), and a few shards draw solid in the
  shatter's late frames; the trailer's drawing has both, larger.
- The mace stays one piece through the shatter (one rigid carried part, as the goblin's sword).

### Cost

968 triangles at every LOD (a mesh that flags its edges isn't decimated), where the vendor's are 18,897, 9,786 and
4,990; 40 shards, as before. One Fat Ogre is on screen at a time.
