# Monster: hiveQueen

Written by `champions monster review hiveQueen` and `champions monster review hiveQueen --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/hiveQueen.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, sting, claw, cast, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, sting, claw, cast, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 510/510/510 in the default look (the trailer's 1858/1237/639), 40 shards, 53 bones, standing 8.078 m.

## The recipe

The Hive Queen (ps-8z4.9), Boss Rush boss 2: the vendor's giant bee rebuilt as a few simple faceted shapes, every
edge it designs drawn, around what names it: two big veined wings, a long striped abdomen curled under her with its
sting, long elbowed antennae, big compound eyes and a crown.

The trailer's drawing of the vendor mesh (1,858 triangles) is half right. The abdomen is already a clean banded shape
with a pointed tip, and the head a faceted dome. But the wings are curved organic outlines with no inner edges: flat
to the camera they are faint hairlines, and through the flap they light whole in the fresnel rim as solid pink blades;
the six legs, the mandibles and the antennae are smooth curves that knot into scratches under the head; the thorax and
head are finely gridded balls that go muddy at game size. The flight clips pitch the bee so the game camera sees the
abdomen's back on top, the thorax, then the face below it. What changes:

- The wings: one big forewing a side, a thin membrane on the vendor's wing bones in the vendor's outline, its veins
  splitting it into five cells as an insect's do, and its outline closed by two narrow bevels (the dragon's). Its
  normals are the direction the wing bones turn toward the sky over every clip (the bone texture), so it stays dark
  through the flap.
- The abdomen: six-sided rings round the vendor's curl, a ridge along the back, each ring a stripe, and a four-sided
  sting to the vendor's tip. The thorax: one six-sided egg of four rings. Both lean their normals up 0.4, so their big
  facets turned away stay dark.
- The head: 1.3 times the vendor's and pushed forward, so the face clears the legs from the game's camera: an
  inverted rounded triangle of three six-sided rings, a big faceted compound eye bulging on each upper side, and a
  five-pointed crown on top: she is the queen.
- The antennae: thin three-sided prisms along the vendor's feelers, ending in a club.
- The legs: the front and back pairs, each one four-sided prism through the vendor's joints. Dropped: the middle pair
  and the mandibles, which knotted into a bright tangle under the head (the first blind critique).

Every shape draws its polygons' outlines, never their fan diagonals; of the wings' bevels only the outline draws.
Each is skinned by transfer from the vendor's nearest vertices over its own bones (never across limbs or sides); the
head, eyes and crown rigid on the head bone. Joints are read from the vendor's vertices weighted to both bones.

Source: Polygonal Creatures Pack's Polygonal Giant Bee Blue.prefab (sha256
b587b298eebd05c813d4d4936f36487262041ebea7011c06933f374662f85ced) and Polygonal Giant Bee.FBX
(03864eee03f89f17ca3e44231df3ad899851e4952fb52e9cfe0f7c753f0a1da2): the prefab and its clips are in manifest.json,
the mesh in the prefab's dependency closure. No Blender: numpy
only. Bake: `just champions-tool monster bake hiveQueen`; review: `just champions-tool monster review hiveQueen` (and
`--look trailer`).

## Notes

ps-8z4.9 (Boss Rush boss 2, act 1, "Swarm formations"), by the monster-mesh-design skill. The default look draws the
recipe's mesh: both blind critiques judged it the look aimed for and the clearer creature at game size and in the
crowd. `--look trailer` is unchanged (the vendor mesh). This bead also registered the source: the Blue prefab and all
ten of its clips in manifest.json, the `hiveQueen` type in monsters.json, its cast row in enemies.json (a boss, the
accent slot so its family matches its colour, health 735 from Content/bossrush.json; tune freely in the fight bead,
ps-8z4.8) and its colour in palette.json (`enemies.accent.magenta`: an act-1 neighbour of the amber goblin, never yellow).

Its clips (monsters.json): idle, walk (Fly Forward, in place; stride 3 m estimated), sting, claw, cast, defend and
hit. Impacts are where the striking part moves fastest in the baked bone texture: Sting Attack 0.217 s of 1.0 (the
sting's tip), Claw Attack 0.25 of 1.0 (the front feet), Cast Spell 0.583 of 1.167. Height 3.5 m at rest; every clip
flies the bee with its body about 2.3 to 4 m up and its flapping wing tips to about 8 m, so the review's "standing"
height is the wings'. The fight bead decides whether to lower it (no `liftM` is set).

### The trailer's drawing of the vendor mesh

Not in the trailer; watched on these sheets. The vendor model (1,858 triangles) is a giant bee: a big curled abdomen,
a round thorax, a faceted head with mandibles, long curving antennae, six thin legs and one big leaf wing a side. The
flight clips pitch it so the game camera, in front and above, sees the abdomen's back on top, the thorax below it and
the face at the bottom. In the edge look:

- the abdomen is a clean banded egg with a pointed tip, the best shape on the sheet (the second critique too);
- the wings are curved outlines with no inner edge: flat to the camera faint hairlines, and through the flap they
  light whole in the fresnel rim as solid pink blades (idle, sting and claw cells);
- the thorax and head are finely gridded balls that merge into a bright knot at game size, and the legs, mandibles
  and antennae are smooth strokes tangled under the head.

### What was tried

Each by `champions monster review hiveQueen`, with a numpy preview of the drawn edges at rest and posed by the bone
texture before it:

- Everything rebuilt as simple shapes skinned by transfer: a head of six-sided sections, the thorax and the abdomen
  lofted, prism legs and antennae, and the wings as veined membranes on the vendor's outline. With their normals the
  membrane's own, the wings still lit as blades in two of five poses; with the clips' mean sky direction they stay
  dark in all five.
- The head at the vendor's size sat behind the thorax and under the legs from the game camera, so the crown on top
  was hidden; 1.3 times larger and 0.12 m forward the face and the crown show.
- The abdomen's big facets turned away lit as a thick rim round its top; normals leant up 0.4 keep it dark.
- After the first critique: the mandibles and the middle pair of legs dropped, the other legs thicker four-sided
  prisms, the thorax and the head one ring fewer each. After the second: the crown's spikes closed pyramids instead of
  thin plates.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near poses with the shatter, and the crowd),
labelled A and B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh A.) "A reads better at game size and in the crowd ... It's a stack of simple solids: a faceted head, a
   thorax, a big gaster and flat hexagonal plates for wings." Of the trailer's: wings "faint hairlines ... or solid
   filled pink smears when seen edge-on". Asked: legs and mouthparts are "scratches" clumping into a "hot pink knot"
   (done: mandibles and the middle legs dropped, the rest prisms); fewer facets on head and thorax (done); wings
   edge-on in the flight pose (the clip's flap; not changed); a noisy shatter (the baker's).
2. (New mesh B.) "B is better, at the in-game size and in the crowd ... The wings are closed diamond and kite shapes
   ... The abdomen is a clean faceted egg, closed and dark inside." Asked: head, thorax and waist still pile into one
   knot at far size; "the head and mandibles are a cluster of thin open triangles" (done: the crown's spikes closed
   pyramids); legs and antennae as single strokes.

### What still looks weak

- At far size the head, thorax and waist still bunch into one knot under the abdomen: the flight clips stack them
  toward the camera, and the waist is the vendor's.
- The legs and antennae are thin prisms that draw as single strokes at game size.
- Mid-flap a wing seen edge-on is one bright line; that is the clip's flap, not the membrane lighting.
- The shatter's last frames are loose slivers (the baker's chunks of thin parts).
