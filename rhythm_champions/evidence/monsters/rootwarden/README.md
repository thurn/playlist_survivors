# Monster: rootwarden

Written by `champions monster review rootwarden` and `champions monster review rootwarden --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/rootwarden.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, smash, stomp, throw, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, smash, stomp, throw, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 422/422/422 in the default look (the trailer's 2961/1535/768), 40 shards, 20 bones, standing 4.818 m.

## The recipe

The Rootwarden (ps-8z4.13), Boss Rush boss 4: the vendor's treant rebuilt as a few simple faceted shapes, every
edge it designs drawn, around what names it: a stump of a trunk with a scowling face and a jagged maw, three thick
boughs from its broken top, and four root legs whose feet reach out in roots diving into the floor.

The trailer's drawing of the vendor mesh (2,961 triangles) is a round mass of small bark plates, knots, leaves and
twigs: a closed silhouette, but inside it every facet is a sliver, so at game size it is a scribble with no dark
inside, and the face (the treant's best feature: slanted eyes and a jack-o'-lantern grin) is lost in the noise, as is
the crown of broken branches over it. Only the four ribbed root legs read. What changes:

- The trunk: one ten-sided stump, fluted as bark (every other corner sunk) round a broad flat front, flaring to the
  roots and tapering to a broken top that leans back, so the face turns up toward the game camera. It splits at the
  mouth: the lower body on the pelvis and jaw, the head above it rigid on the head bone, a wedge of a maw open at the
  front between them. Its normals lean up 0.4, so its big facets turned away stay dark.
- The face: a jagged grin of four fangs hanging from the head and three rising from the jaw, interleaved; two angry
  slanted eyes, each a slot bulging to a ridge so its facets catch the line; a heavy brow of two wedges and a knot of
  a nose.
- The top: three splinters standing from the stump's broken rim and three thick boughs, two of them forking, all
  four-sided prisms tapering to points. A crown of many twigs drew as hairline scratches at game size (both blind
  critiques).
- The arms: short gnarled branches through the vendor's joints, each ending in one thick claw.
- The legs: four prisms from inside the trunk through the vendor's joints to the ground, each foot reaching out in
  one thick root that dives into the floor. Two or three thin roots a foot cluttered the outline as claws (the second
  blind critique). Dropped: the leaves round the waist and the bark's knots and plates.

Every shape draws its polygons' outlines, never their fan diagonals. The trunk, arms and legs are skinned by transfer
from the vendor's nearest vertices over their own bones (never across limbs or sides); the head and its face, crown
and boughs rigid on the head bone, the lower fangs on the jaw, each claw and ground root on its hand or ankle.
Joints are read from the vendor's vertices weighted to both bones.

Source: Polygonal Creatures Pack's Polygonal Treant Green.prefab (sha256
f28ecb6df55454211bfeb32eef0885d999e8c0da8159c739390e065d4bef31f6) and Polygonal Treant.FBX
(94dab0d328fe3f4c4371b3d5aa5f04562762930b5fcf16b38a35f90298cbd4dc): the prefab and its clips are in manifest.json,
the mesh in the prefab's dependency closure. No Blender: numpy only. Bake: `just champions-tool monster bake
rootwarden`; review: `just champions-tool monster review rootwarden` (and `--look trailer`).

## Notes

ps-8z4.13 (Boss Rush boss 4, act 1, "Echo eruptions"), by the monster-mesh-design skill. The default look draws the
recipe's mesh: both blind critiques judged it closer to the look aimed for and the clearer creature at game size and
in the crowd. `--look trailer` is unchanged (the vendor mesh). The source's registration (the Green prefab, its Root
and Projectile Seed prefabs and the clips in manifest.json, the `rootwarden` type in monsters.json, its cast row in
enemies.json and `enemies.accent.violet` in palette.json) is the fight bead's (ps-8z4.12), copied entry for entry so
whichever lands first carries it: roles idle, walk, smash (Root Attack), stomp, throw (Projectile Up Attack), attack
(Projectile Attack), roar (Cast Spell) and hit; the sheets show idle, walk, smash, stomp and throw at their impacts.
My own measure from the baked bone texture agrees: Root Attack bows the head lowest and furthest forward at 0.167 s,
and Stomp Attack's front feet land at 0.53 to 0.6 s.

### The trailer's drawing of the vendor mesh

Not in the trailer; watched on these sheets. The vendor model (2,961 triangles) is a treant: a round stump of a trunk
whose upper half is its head, with slanted eyes and a jack-o'-lantern grin over a jaw, a crown of broken branches, a
collar of leaves round its waist, stubby arms and four ribbed root legs. In the edge look:

- the silhouette is closed and round, but inside it every facet is a sliver of bark, knot, leaf or twig, so at game
  size it is a scribble with no dark inside;
- the face, its best feature, is lost in that noise, and the crown of branches is a ragged ring of small chunks;
- from above (the smash and stomp bows) it is a ring of rubble round a dark disc with a few strokes in it;
- only the four ribbed root legs read as shapes.

### What was tried

Each by `champions monster review rootwarden`, with a numpy hidden-line preview of the drawn edges at rest before it:

- An octagonal trunk with a flat front: it read as a crate or a bin. Ten sides fluted as bark (every other corner
  sunk), flaring to the roots and narrowing to a top set back 0.25 m, read as a stump, the face turned up to the
  camera.
- The mouth split where the vendor's head and jaw bones meet, open as a wedge at the front so the fangs interleave
  without crossing; the head's bottom and the jaw's top close it, dark inside.
- Eyes as flat plates were thin lines at game size; slots bulging to a ridge catch the line on four facets and read.
  (A first version's face normal pointed into the trunk and buried the eyes and brows: fixed.)
- Branches: a crown of thin forked twigs and three roots a foot drew as hairline scratches and claws (both
  critiques); now three thick boughs (two forking), three splinters, one claw an arm and one thick root a foot.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near poses with the shatter, and the crowd),
labelled A and B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh A.) "A is closer to the target style and reads better at in-game size and in the crowd ... The trunk is a
   clean faceted box with a dark inside, the zigzag mouth reads, and the main legs are solid prisms. It falls apart
   in the extremities." Of the trailer's: "a mosaic of patches ... At game size it's a cyan scribble with no dark
   interior", the face "lost in the noise". Asked: fatter, fewer spikes (done: boughs 0.28 m at the base, half as
   many points); the stray splinters at the sides (done: one claw an arm); a readable top in the bowed pose (partly:
   fewer, bigger splinters and boughs over a plain cap).
2. (New mesh B.) "B reads better, both at the in-game size and in the crowd. It looks like a low-poly 3D model: a
   boxy trunk, legs and cone spikes, with dark faces and a face that still reads." Asked: still fewer, fatter spikes
   (done: one root a foot, three splinters); the bowed poses lose the face (not changed: seen from straight above the
   face is under the top, as it should be); one claw per limb (done).

### What still looks weak

- In the smash and stomp bows the camera sees the top of the stump: a plain cap with splinters and boughs. It reads as
  a tree from above, not as a face.
- Next to the trailer's round mass it is lighter and spikier; check that it holds its own as a boss alone in its
  hollow at the eased-out boss camera.
- The lower body's side facets turned away still catch a soft gradient of rim light in the violet.
