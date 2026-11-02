# Monster: mushroom

Written by `champions monster review mushroom` and `champions monster review mushroom --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/mushroom.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 310/310/310 in the default look (the trailer's 1638/958/512), 27 shards, 15 bones, standing 0.926 m.

## The recipe

The mushroom (ps-6y2.36): a toadstool rebuilt from a few simple shapes, a face on the front of its cap, two large
spots behind, walking on four thick roots.

The trailer's drawing of the vendor mesh has no facets. Its cap, a broad dome, rarely creases past 24 degrees, so it is
a wobbly fresnel outline with a glassy band along its far edge; its raised spots crease into zigzag scratches ("Z",
"7", "?"), read as graffiti or a sleeper's "Zzz"; and its root feet are a fuzz of short strokes in front, which stays
standing through the shatter. From the game camera, 60 degrees down, the cap hides the stalk and most of the legs, so
at game size it reads as a brain, broccoli or a jellyfish. So the whole figure is new, every line chosen:

- The cap: a dome of eight gores from the rim up to a flat crown, each folding halfway without a line there, so each
  gore is one closed panel. A lip under the rim gives the cap an edge the game camera sees in front, where the cap
  overhangs. The three gores in front are one quiet panel for the face, so no gore frames it.
- The face: two slanted almond eyes and a wide grin, each one closed shape, high on that panel, so the game camera sees
  them from most sides. The cap alone read as an object (a dome, a sea mine, a soccer ball); with a face it reads as a
  creature, and one that faces the hero as a monster that shoots should.
- The spots: two, large and faceted with uneven sides, behind, so the crown stays quiet. Small spots in every facet
  read as portholes or a soccer ball's patches.
- The stalk: a squat six-sided bulb, whose front shows under the lip. A stalk up into the cap is hidden by the cap in
  every view, and in the shatter it stood as a cluster of long spikes.
- The legs: four thick roots, each a four-sided prism with a ridge along its top, from the bulb out to the rim, bent
  at the knee and out along the ground to a blunt end, drawn by their long edges and their ends; rings at their joints
  read as scratches across them. The vendor's leg is a short shin under the body and a long root of a foot along the
  ground, so each root rises from its shin at the bulb and is its foot from the knee out.
- The face and spots are printed on the cap: each outline is carried along rays from the cap's centre onto its facets,
  raised a little, with a bevel down into the cap so the outline is drawn from both sides, as the cap's own edges
  are (drawn from one side only, it was a faint hairline).
- Every triangle on its own corners. The cap's normals lean up, so the fresnel rim outlines the cap rather than
  lighting its facets; the legs and stalk have their facets' normals.
- Dropped: the vendor's spots and its root toes.

310 triangles at every LOD, where the vendor's are 1,638, 958 and 512. The cap, its face and spots are rigid on the
cap's bone, the stalk on the pelvis, and each root's rings ride its own leg's shin and foot.

Source: Magic Pig Games' Mushroom_LP.prefab (sha256 a80eb3799721fc0fe5b540f2db856c9e4d60cb5aeee6a635244387e171057bd9)
and Mushroom_LP.fbx (259710e41bf8251ebcc249d8caa1f2024acdc4e6cadf5f9e5515dfc002e33a90), as manifest.json lists them.
No Blender: numpy only. Bake: `just champions-tool monster bake mushroom`; review: `just champions-tool monster review
mushroom` (and `--look trailer`).

## Notes

ps-6y2.36, by the monster-mesh-design skill. The default look draws the recipe's mesh: all eight blind critiques
judged it the look aimed for (closed facets, a closed silhouette, dark inside) and the better creature at game size
and in the crowd, and the trailer's drawing a soft blob of scratches. What none settled is the species: the new mesh
reads as a grinning faceted creature on stubby legs (a rock golem, a geode, a walking soccer ball), with a mushroom
only as a second guess, while the trailer's drawing sometimes reads as a mushroom on roots and more often as a brain,
cauliflower or broccoli. That is the open question for the operator. `--look trailer` is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

The vendor mushroom is a red toadstool with raised white spots: a broad dome of a cap (0.96 m across, 0.37 m deep,
rigid on one bone) on a stalk about 0.4 m tall, standing on four short legs whose feet spread over the ground as roots
(1,340 of its 3,208 vertices are in the feet). From the game camera, 60 degrees down, the cap hides the stalk and the
legs but for the front feet: a thin stalk under the new cap showed no pixels over a ring of game-camera views, and
only a fat bulb shows, under the front rim. In the trailer's drawing (`trailer-*.jpg`, and the violet crowd in the
trailer itself at 0:40):

- the cap has no facets: it is the fresnel rim, a wobbly outline with a glassy band along its far top edge;
- the raised spots crease sharply, so each is a zigzag scratch: "Z", "7", "?", "*";
- the root feet are a fuzz of short strokes in front, which stays standing through the shatter like a stump;
- the shatter throws crumpled open scraps.

What names it: the cap and its spots, the walk on root legs, and, for a ranged monster that shoots spores, a front
that faces you.

### What was tried

Each by `champions monster review mushroom --out <scratch>`, judged at game size and in the crowd, most in blind
lineups:

- The vendor's proportions as a faceted dome (eight sides, two bands and a flat crown) with a small spot in every
  facet: a clean wireframe, but "a geodesic dome or soccer ball on little legs, maybe a pill bug or tortoise"; the
  spots "read as rivets or portholes".
- Umbrella gores, their spokes drawn to a small crown: "a sea urchin or pufferfish with a sun emblem", "sunbursts or
  ship's wheels".
- A smaller cap over a bulbous stalk on splayed legs: the only one of four read as a creature, but "a jellyfish, or a
  walking sea mine on spiky legs".
- A staggered (triangulated) dome: a geodesic ball. A flat-topped cap of one band: "a chunky armoured pod", "dice with
  legs". Six- and seven-sided caps: dice and soccer balls.
- Eyes low on the front band: "headlights". Eyes and a grin printed as flat shapes drew as faint hairlines: an outline
  on the cap is drawn by one triangle where the cap's own edges are drawn by two, and small triangles fade their lines
  (MonsterLines.hlsl). Printed with a bevel into the cap, both sides draw it.
- Long thin legs out past the rim, rigid ring by ring on the shin: they swung up beside the cap like arms in the idle;
  the vendor's leg is a short shin and a long foot along the ground, so the new roots ride the foot from the knee out.
- A stalk up into the cap: hidden in every game view, and in the shatter a ladder of long spikes left standing.

### Blind critiques

Fresh agents given only the sheets (far, game and near with the shatter, and the crowd), labelled at random, no docs,
no recipe, no numbers, asked the skill's question and, first, what each one looks like.

1. The faceted dome with a spot in every facet (new mesh B, beside the trailer's A): "A looks like a brain or a
   jellyfish with scribbles on it. B looks like a geodesic dome or soccer ball on little legs ... B hits the look.
   Every edge connects, every facet is closed". Of B: "It reads as an object, not a creature ... Give it a front",
   "The hexagon on every panel reads as rivets or portholes", "the feet look like X-ed boxes". Done: four variants for
   a lineup; the feet as simple pointed roots.
2. Four variants, the trailer's not among them: the smaller cap over a stalk "is the only drawing that reads as a
   creature at game size and in the crowd ... the splayed legs give each one a pose and a direction"; of it, "No
   face", "The legs are hairline spikes", "The dome is too busy". Done: a face; legs thicker; fewer spots.
3. Four at once, the trailer's among them: a dome with spots and no face was "a spotted mushroom cap, or a ladybug or
   turtle shell"; with a face, "a little turtle ... Second guess: a walking mushroom"; the flat-topped cap was "the
   clearest low-poly read" but "a chunky armoured pod"; the trailer's "broccoli, or a tree seen from above". Asked:
   the face higher, "the round spots on top read as portholes. They are the only curves on the model". Done: the dome
   kept, its spots faceted with uneven sides, the face moved up, normals leaning further up.
4. The gored dome with face and spots (new mesh A): "A reads clearly as low-poly 3D ... A is much better at game size
   and in the crowd"; A is "a faceted walking shell, like a beetle or tick on spiky legs. Second guess: a spotted
   mushroom cap or a ladybug", B (the trailer's) "a brain or cauliflower with 'Zzz' scribbled on it". Asked: thicker
   legs splayed to show from above, "keep a few large [spots] on the crown only", the face "bigger with fewer lines",
   and "if it's meant to be a mushroom, the cap needs an overhanging rim and a visible stalk". Done: four thick roots
   from a bulb, three large spots, a bigger face.
5. (New mesh B): "B reads better as a creature at game size and in the crowd, by a wide margin", but "a walking soccer
   ball with a grinning face"; the trailer's A was "a puffball mushroom, or a jellyfish". Of B: "A small shape floats
   inside each facet ... so the ball reads as a soccer ball or a ladybug", "The square panel around [the face] reads as
   a TV screen", "The legs are thin and splay sideways". Done: the three front gores one quiet panel for a bigger
   face, the spots fewer, the legs thicker and drawn by their long edges.
6. (New mesh B): "a faceted boulder or rock golem with a cartoon smiley face on stubby legs. Second guess: a walking
   mushroom cap or dumpling ... B, by a wide margin"; the trailer's "a lumpy brain or cauliflower". Asked: "Merge the
   top facets into fewer, larger ones. Tilt the face up", "a band where the cap meets the body", "The death shatter
   leaves a skeleton behind. A vertical ladder-like spine", "The eyes read as goggles". Done: the face higher; a lip
   under the rim; two spots, both behind; the eyes single closed shapes without pupils; the stalk a squat bulb.
7. (New mesh A): "a cute faceted ball creature with a smiley face on stubby legs ... Second guess: a rock golem or a
   mushroom-person ... A reads better as a creature at game size and in the crowd". Asked: "The death pile stays
   standing ... a tall cluster of spikes" (the stalk), "the pentagon pattern makes it a ball" from behind, "the legs
   are too slight". Done: the stalk squat and closed, the roots thicker, the normals leaning further up.
8. The mesh as delivered (new mesh B): "a grinning rock or geode creature on stubby legs. In the crowd, seen from above
   with the face hidden, it reads as a soccer ball or turtle shell"; the trailer's A "a fuzzy brain or cauliflower
   with scribbles on it. In the crowd it reads as mushrooms or jellyfish on root tangles ... B reads far better as a
   creature at game size and in the crowd." Its list: fewer, less regular facets on top; thicker wedge feet; solid
   shards in the shatter; doubled lines along the crown's far edge. Not done: see below.

### Still weak

- The species. From the game camera a cap hides whatever is under it, so the stalk, the overhang and the gills that
  say mushroom in profile are out of view; the cap, its spots and the face carry it, and the critics' first read is a
  creature (a rock golem, a geode, a soccer ball with a grin) with a mushroom as a second guess in half the
  critiques. The trailer's soft blob on a tangle of roots was called a mushroom about as often (once first, "a
  puffball mushroom"), and a brain, cauliflower or broccoli in every critique.
- From behind, the face hidden, the gores round a flat crown read as a soccer ball or a turtle shell: every
  arrangement of facets tried on a dome did (rings, triangles, gores, six to eight sides), and fewer, larger facets
  read as a box or a pod.
- The crown's far edge, seen at a grazing angle, draws as two lines close together.
- The shatter: a few shards seen edge-on draw as solid magenta slivers, as the other types' do in both looks (the
  shard shader's, not the mesh's); the leg pieces lie in a pile rather than flying.
- The three poses differ little at game size; that is the clips', not the mesh's.
