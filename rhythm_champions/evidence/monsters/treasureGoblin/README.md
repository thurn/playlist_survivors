# Monster: treasureGoblin

Written by `champions monster review treasureGoblin` and `champions monster review treasureGoblin --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/treasureGoblin.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, run, slice, stab, cast, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, run, slice, stab, cast, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 644/644/644 in the default look (the trailer's 1762/895/448), 36 shards, 54 bones, standing 3.142 m.

## The recipe

The Treasure Goblin (ps-8z4.7), Boss Rush boss 1: the vendor's chibi goblin rebuilt as a few simple faceted
shapes, every edge it designs drawn, around what names it: the loot sack on its back, a big head with long ears, a
long nose and a jutting jaw.

The trailer's drawing of the vendor mesh (1,762 triangles) is half right. The sack is already a low-poly polyhedron
whose creases close into facets, with a sword stuck through it, its tie and a strap over the shoulder: the best thing
on the sheet, and it names the type. The goblin is not: its head is a smooth ball whose features crease only in
places, so eyes, brows and mouth draw as floating dashes and no facet closes; the hands' fingers, the clawed feet and
the bracers knot into scribbles at game size. What changes:

- Kept as the vendor made them, drawn by their own 24-degree creases (flagged here): the sack, its tie, the sword
  through it and the strap (everything the bag bone leads), and the dagger in the right hand.
- The head: 0.88 of the vendor's, angular, six-sided rings from a jutting chin and a wide jaw to a narrow crown
  closing under a ridge from brow to nape (no apex, which reads as an umbrella from above), a corner to the front. A
  round head of eight-sided rings read as a second sack in every critique ("two sacks stacked up"). An almond eye on each forehead plane either
  side of that corner, their inner corners low (sly), raised as plates so they draw closed; a long nose drooping
  from the middle of the face. The idle clip bows the head, so the camera sees the forehead and crown.
- The ears: long broad leaves straight out to the sides, drooping at the tips, facing up, folded once, thick at the
  root and thin at the tip so they read as wedges, not cards. Rigid on the head; their tops' normals lean up.
- The body: one six-sided pot-bellied shape from the crotch to the neck, with a belt round it.
- The limbs: each arm one four-sided prism through the vendor's joints, thin to the elbow and flaring into a big
  bracer, and a broad mitten of a hand; each leg one short prism, and a pointed shoe curling up at the toe.
- Dropped: the fingers, claws, the rope's dangling end on the chest and the earring.

Every new shape draws its polygons' outlines, never their fan diagonals, and a plate only its top. Each is skinned by
transfer from the vendor's nearest vertices over its own bones (never across limbs or sides); the head, eyes, nose
and ears rigid on the head bone. The kept parts keep their vendor weights.

Source: Forest Creatures Pack's Treasure Goblin - Blue.prefab (sha256
299d32142eb35816e3cc20081da60041af39527419de13fa3a9750715ae22f90) and Treasure Goblin.FBX
(70dd16b9a17684f5706becf911a33761a42243128ae7bd4739dceec243240178), as manifest.json lists them. No Blender: numpy
only. Bake: `just champions-tool monster bake treasureGoblin`; review: `just champions-tool monster review
treasureGoblin` (and `--look trailer`).

## Notes

ps-8z4.7 (Boss Rush boss 1, act 1, "The Chase"), by the monster-mesh-design skill. The default look draws the recipe's
mesh: all four blind critiques judged it the look aimed for (closed facets, a closed silhouette, dark inside) and the
clearer read at game size and in the crowd; the trailer's drawing of the goblin "a smooth dark ball with loose strokes
floating on it". `--look trailer` is unchanged (the vendor mesh). This bead also registered the source: the prefab and
its clips in manifest.json, the `treasureGoblin` type in monsters.json, its cast row in enemies.json (a boss, the
counter slot, which the Boss Rush shell reads only once a fight module lands) and its colour in palette.json
(`enemies.counter.amber`, the coin gold boss-rush.md asks for, never yellow).

Its clips (monsters.json): idle, walk and run (in place), slice, stab, cast and hit. Impacts are where the dagger's
tip moves fastest in the baked bone texture: SliceAttack 0.4 s of 1.0, StabAttack 0.25 of 0.833, SpellCast 0.42 (the
release) of 1.167; strides are estimated from the foot's travel while planted (walk about 1.9 m a cycle, run about 3).
Height 3 m: a small boss that flees, half the Fat Ogre's.

### The trailer's drawing of the vendor mesh

Not in the trailer; watched on these sheets. The vendor model is already low-poly (1,762 triangles): a chibi goblin
with a head a third of its height, long ears straight out, a long nose, a jutting grin, a pot belly, bracers, clawed
hands and feet, a dagger, and a big loot sack on its back with a sword stuck through it, a tie at its neck and a strap
over the shoulder. In the edge look:

- the sack is a clean low-poly polyhedron whose creases close into facets, the best thing on the sheet, and it names
  the type at every size and in the crowd;
- the head is a smooth ball whose features crease only in places: eyes, brows and the grin draw as floating dashes,
  and no facet on it closes; at game size it is a dark disc with scratches;
- the fingers, claws and bracers knot into scribbles under the head; the shatter throws open slivers and long sticks.

Drawing every edge of the vendor mesh instead (`edge_angle = 1`) was checked in a preview and dropped: the head and
body are small triangles, a dense net.

### What was tried

Each by `champions monster review treasureGoblin`, with a numpy preview of the drawn edges at rest before it:

- The sack, its tie, the sword and the strap kept as the vendor made them, drawn by their own 24-degree creases (the
  recipe flags them, so they draw the same at every distance); the dagger kept. Everything else rebuilt as in the
  goblin's recipe (ps-6y2.35): a head of rings, leaf ears rigid on the head, a lofted body, a prism per limb.
- Eyes: thin slanted slits on the forehead planes were hidden (the idle clip bows the head, so the camera sees the
  forehead and crown); on the crown's front triangles they drew as a fan of spokes beside the nose. Almond eyes raised
  as plates on the forehead planes, larger, read as two eyes either side of the nose.
- Ears: the first leaves, seen edge on, drew as thin spikes; broader, then longer (critique 1's ask to tell the head
  from the sack), then drooping further (pointing away from the camera, a long ear projected up as a horn), then
  thick at the root (critique 2: "flat cards").
- Feet: wedge boxes read as doors under the body; a pointed shoe curling up at the toe.
- The head: an eight-sided round head the vendor's size read as a second sack ("two sacks stacked up", critiques 1
  to 3); six-sided, angular, a narrower crown, a wider jutting jaw and 0.88 of the vendor's size.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near poses with the shatter, and the crowd),
labelled A and B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh A.) "A is better, both at in-game size and in the crowd ... every goblin reads as two clear masses, head
   + loot sack." Of the trailer's head: "a smooth round shell ... floating strokes for the brows, mouth and eye".
   Asked: merge the small parts under the body, and set the head apart from the sack, which "at far size reads as two
   balls". Done: pointed shoes, longer ears.
2. (New mesh B.) "B is the better wireframe and reads better as a boss at game size and in the crowd." Asked: bigger
   eyes, "the ears and spikes are big flat blades" to be given depth, a nose or jaw wedge. Done: larger raised eyes,
   ears thick at the root, a jutting jaw.
3. (New mesh A.) "A follows the style better. B reads better as a creature at a glance, but it does so by breaking
   the style." Asked: a head "smaller and more angular than the sack". Done: the six-sided head at 0.88.
4. (New mesh B.) "B is better ... B builds the creature as closed shapes too. A's creature breaks into scratches." Its
   remaining asks are below.

### What still looks weak

- The sack still outweighs the goblin at game size: critique 4 asks for the head a little bigger again, critique 3
  for it smaller; the angular head is the compromise. The sack names the type, so it stays the biggest shape.
- The eyes are raised plates inside the forehead facets, which critique 4 read at game size as "a little window or
  parallelogram". They read as eyes up close.
- The arms and legs hide under the head from the game camera, so the attack poses differ little from idle at game
  size; the dagger and the sword through the sack carry the motion.
- The shatter's first frame throws long sticks (the vendor sword and the strap); later frames are closed pieces.
