# Monster: sandwyrm

Written by `champions monster review sandwyrm` and `champions monster review sandwyrm --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/sandwyrm.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, appear, smash, attack, stomp, cast, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, appear, smash, attack, stomp, cast, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 478/478/478 in the default look (the trailer's 2396/1216/639), 40 shards, 143 bones, standing 5.071 m.

## The recipe

The Sandwyrm (ps-8z4.18), Boss Rush boss 6, act 2's opener: the vendor's giant worm rebuilt whole as a few simple
faceted volumes, every edge it designs drawn, around what names it: a long segmented worm rearing out of the sand,
armour plates down its body, a row of spikes along its back, and a head that is all mouth, four jaws that close to a
point and open into a maw.

The trailer's drawing of the vendor mesh (2,396 triangles) is a thin worm whose head is a knot of small triangles
(teeth, tongues, a ridged carapace and serrated mandibles) and whose body is lined with small sawtooth spikes: at
game size the head blurs into noise and the spikes into a sawtooth fringe, though the long curling body reads. So
nothing of the vendor mesh is kept; every part is new, built in the body's bind pose and skinned to the vendor bones,
and every edge that draws is chosen here (a polygon's edges, never its triangles' diagonals):

- The body: one eight-sided loft from a point at the tail to the neck, a corner on the spine and one on the belly,
  in seven armour plates each over two of the vendor's segments. Each plate flares to a rim at its lower edge over
  the top of the plate below, as shingles that shed sand, so the body's outline steps at every plate and the rims
  draw round it; the plates' upper edges, tucked under the rim, don't (a doubled ring). Girth swells from the tail
  to the neck, fuller than the vendor's, so the tail keeps its outline to its point.
- Spikes: one broad swept-back pyramid on each plate's spine, larger toward the neck (the vendor's many small ones
  read as a sawtooth fringe; thin ones lit edge-on in the rim).
- The head, 1.15 times the vendor's: a plain collar on the neck (a flared lip was a thin band seen edge-on that
  filled solid in the rim), and the mouth, four jaws closing to a point as the vendor's do, each a thick curved
  petal on its own jaw chain, so the clips open them into a four-jawed maw: the top and bottom jaws short, the side
  mandibles longer hooks curving in with fangs down their inner faces. A ring of eight fangs round the throat
  inside them makes the open maw read as a mouth (without it, the head-down poses read as a claw or a fist).

The vendor's parts were bound separately, and some bone names appear twice with different bind poses (the spikes'
Worm_Bone002 is not the body's), so every shape is skinned by palette index to the body's entries only: the body's
chain, the head root and each jaw's chain, by transfer from the vendor's nearest body, teeth and carapace vertices
over the shape's own entries. The body's joints come from the baked bone texture (where each bone and its parent
move alike over every clip). The body and the collar lean their normals a little (0.4) toward the head, which is up
while it rears, keeping their long side facets dark in the rim; the jaws keep their own, as the clips swing them
toward the camera. The mesh flags its edges, so it is drawn as designed at every distance: 478 triangles at every
LOD, where the vendor's are 2,396.

The type's height (monsters.json heightM 14) is measured on the vendor's bind pose, which stands its parts apart; at
heightM 5 the worm reared only 1.7 m in its idle. At 14 it rears about 4.8 m, a boss's size, its tail a metre under
the floor.

Source: Infinity PBR's Giant_Worm_LP.prefab (sha256 3cb3552e1fd38da20a11583be3c6db3437a0fd37b12b839cd1d0f8f064016140)
and Giant_Worm_LP.fbx (c3fc83343c5689727ebced1608ba35e0e67bdf8fd16fcc8c333eeaf2b5102b89), as manifest.json lists
them. Only its rig is used, and its rest pose and skin weights to place and skin the shapes. No Blender: numpy only.
Bake: `just champions-tool monster bake sandwyrm`; review: `just champions-tool monster review sandwyrm` (and
`--look trailer`).

## Notes

ps-8z4.18, by the monster-mesh-design skill. The default look draws the recipe's mesh: two blind critiques, each
preferring it to the trailer's drawing at the in-game size and in the crowd. `--look trailer` is unchanged (the
vendor mesh).

This bead registered the source, so the fight bead (ps-8z4.17) need not: the Giant_Worm_LP prefab and FBX in
manifest.json (its clips are in the FBX), the `sandwyrm` type in monsters.json, its boss cast row in enemies.json
(slot accent, to be tuned by the fight) and its magenta in palette.json (the act's key is a dim amber, which an
orange or amber worm would melt into, and the telegraphs are orange). Its `heightM` is 14, not the worm's size: the
vendor's parts are bound apart, so the bind pose the height is measured on is far taller than the worm; at 14 it
rears about 4.8 m in its idle, a boss's size, with its tail a metre under the floor. Its clip roles: idle (Idle),
hidden, appear and disappear (Hidden, Appear, Disappear: under the floor, bursting up, sinking back), smash
(Attack01, the head slammed down, impact 0.833 s), attack (Attack02, a long lunge along the ground, 1.833 s), stomp
(Attack03, a weaving reach, 1.5 s), cast (Cast1, rearing up with the maw open to the sky, 1.167 s), roar (IdleBreak)
and hit (GotHitBody). Impacts are the mouth's lowest point or furthest reach, measured from the baked bone texture.

### The trailer's drawing of the vendor mesh

On these sheets (`trailer-*.jpg`). The vendor worm is a long tube of segments with a row of small spikes down its
back, a ridged carapace over its head, and a mouth of four jaws (short top and bottom jaws and two long serrated side
mandibles) round a throat of teeth and four tongues, 2,396 triangles. So:

- at game size the body reads as a curling worm, but the head is a knot of small triangles (teeth, tongues, ridges)
  that blurs into noise, and the spikes are a sawtooth fringe;
- in the crowd the S-curve and the open jaw read; the head stays scratchy;
- the shatter falls into open wire scraps and curls.

What names it: the long segmented body rearing from the sand, the spikes down its back, and the mouth, four jaws
that close to a point and open into a maw. It has no sibling in the roster.

### What was tried

Each by `champions monster review sandwyrm` (about a minute), judged at game size, in the crowd and up close, with a
numpy previewer of the drawn edges posed by the baked bone texture at rest and in the clips before each:

- The first build kept the vendor's head size and a thin body: the head was a small knot, and the body's sideways
  facets glowed solid in the rim. The body and collar lean their normals 0.4 toward the head; the head is 1.3 times.
- Critique 1 preferred it to the trailer's drawing as a creature in game and in the crowd, but saw many facets
  filling solid (the collar's flared lip and the spikes' sides, seen edge-on), the head as a cube or a fist, and the
  tail's outline fading. The collar lost its flare, the spikes are broader, the body fuller to its tail, and a ring
  of fangs round the throat makes the open maw read as a mouth.
- Critique 2 preferred it again ("big closed facets, a dark inside, crisp edges ... looks like synthwave models"; its
  shatter "solid, chunky shards"), but still read the head-down poses as a fist with fingers, and the body as a thin
  stalk under a huge head. The body is fuller again, the head 1.15 times, and the side mandibles shorter, curving in
  inside the head's outline.

### What still looks weak

- In the head-down poses (idle, smash, stomp) the game camera looks onto the back of the head with the body hidden
  below it, so the worm is a compact spiked head with hooked jaws, its length unseen; it reads as the worm once it
  lunges along the ground or rears up. The fight's camera and the burrowing will show more of it than these stills.
- Some spike and jaw faces still light in the rim along the silhouette, thin bright edges rather than patches.
- The plates' rims are hoops round the body; they name a segmented worm, but from above the stacked rims of the
  upright body can read as a stack of rings.
