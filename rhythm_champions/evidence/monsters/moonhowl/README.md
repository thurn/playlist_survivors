# Monster: moonhowl

Written by `champions monster review moonhowl` and `champions monster review moonhowl --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/moonhowl.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, smash, attack, throw, tail, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, smash, attack, throw, tail, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 750/750/750 in the default look (the trailer's 14912/7674/3837), 35 shards, 54 bones, standing 2.912 m.

## The recipe

Moonhowl (ps-8z4.15), Boss Rush boss 5, act 1's finale: the vendor's werewolf rebuilt whole as a few simple
faceted volumes, every edge it designs drawn, around what names it: a hunched wolfman with a wolf's head (a long
snout, a jaw that opens with the howl and the bites, tall pointed ears), hackles over its hump, long arms ending in
three long talons, digitigrade legs on paws, and a bushy tail to the ground.

The trailer's drawing of the vendor mesh (14,912 triangles) reads as a hunched long-armed beast at game size, but
its fur tufts and sculpted muscles draw short broken dashes all over it: the back and shoulders glow as lit patches,
the head is a small knot at the top of the figure, and in a crowd the werewolves merge into a field of dashes. Blind
critiques called it a ghoul or a zombie. So nothing of the vendor mesh is kept; every part is new, skinned to the
vendor bones, and every edge that draws is chosen here (a polygon's edges, never its triangles' diagonals, and only
some of those):

- The trunk: one eight-sided loft bent forward from narrow hips through a lean waist to a deep chest and a broad hump
  of shoulders, higher behind. Long lines along it (the sides, the belly's edges and the spine) and the shoulder
  blades' lines over the hump only, so the back has mass from the 60-degree camera (with the spine alone it was an
  empty gap between the arms) without breaking into slivers (a ring round the chest as well did).
- The head: 1.4 times the vendor's (it was lost over the hump), a skull wide behind narrowing to a long tapered
  snout, a slanted eye under the brow on each side, a nose at the tip, and the lower jaw a wedge of its own on the vendor's jaw bone, so the howl and the bites open it. The ears are tall flat
  pyramids standing up and out from the back of the skull: from behind they are a wolf's ears, as they should be.
- Hackles: three broad flat spikes round the back of the neck and three down the spine over the hump, each a closed
  pyramid (thin ones read as stray strokes), swept back. More round the neck, and fangs, knotted the head at game
  size.
- The arms: long and heavy, each one six-sided prism from the shoulder through the elbow to the wrist, a line round
  it only at the elbow; a palm block and three thick hooked talons, each a tapering four-sided prism through its
  finger's joints to a point past the vendor's claw (four thin ones, the thumb's too, were hairlines at game size).
- The legs: digitigrade, each one six-sided prism from the hip through the knee and the hock to the ankle; a wedge of
  a paw with three short claws.
- The tail: a six-sided spindle swelling through its middle to a point on the ground behind.

Every triangle has its own corners. The trunk, legs, hackles and tail lean their normals a little (0.4) along their
shape toward its root, which keeps their long facets dark in the rim. The head, hands and arms keep their own normals:
the clips raise the claws and tip the head back to howl, and normals leant up in the rest pose then pointed away from
the camera and the rim drew the raised hand and the howling head solid. The mesh flags its edges, so it is drawn as
designed at every distance rather than decimated: 750 triangles at every LOD, where the vendor's are 14,912.

Source: Heroic Fantasy Creatures Full Pack Vol 1's Werewolf.prefab (sha256
2c74a5dc348ad85314b946f8edba76230cf857b3ada7074215e4c1cdd0201490) and SK_Werewolf.FBX
(47a958af7bb693eb149c1e0da9d881e595eca5107e8f1d76f8933e3d3ab5ad6b), as manifest.json lists them. Only its rig is
used, and its rest pose and skin weights to place and skin the shapes: each shape takes the weights of the vendor's
nearest vertices over its own bones (never across limbs or sides); the head is rigid on the head bone, the jaw on
the jaw bone. No Blender: numpy only. Bake: `just champions-tool monster bake moonhowl`; review: `just champions-tool
monster review moonhowl` (and `--look trailer`).

## Notes

ps-8z4.15, by the monster-mesh-design skill. The default look draws the recipe's mesh: two blind critiques, each
preferring it to the trailer's drawing at the in-game size and in the crowd. `--look trailer` is unchanged (the
vendor mesh).

The source's registration (the Werewolf prefab and its clips in manifest.json, the `moonhowl` type in monsters.json,
its boss cast row in enemies.json and its blue in palette.json) is copied entry for entry from the fight bead
(ps-8z4.14), which registered it first, so both rebases see the same hunks; the fight's sound bank entries are its own.

### The trailer's drawing of the vendor mesh

On these sheets (`trailer-*.jpg`). The vendor werewolf is a 3.6 m hunched wolfman, arms reaching forward to four-clawed
hands, digitigrade legs, a tail to the ground, a long snout with a jaw of its own and tall ears: 14,912 triangles of
sculpted muscle and fur tufts. So:

- at game size it reads as a hunched long-armed beast, but every tuft and muscle draws short broken dashes: it is never
  dark inside, the back and shoulders glow as lit patches, and the head, the first thing that names it, is a small
  bright knot at the top of the figure;
- in the crowd the werewolves are a field of dashes running into each other;
- the shatter throws curled scribbles that fade to dust.

What reads: the hunch, the long arms and the claws. What names it: those, the wolf's head with its snout and ears, the
hackles and the tail. It has no sibling in the boss roster's blue; among the hunched humanoids it is told apart from
the troll (a bare hump, a spiked club) and the big ork (upright, a cleaver) by its wolf's head and ears, its hackles,
its talons and its tail.

### What was tried

Each by `champions monster review moonhowl --out <scratch>` (about 20 s), judged at game size, in the crowd and up
close, with a numpy previewer of the drawn edges at rest (the game camera from the front, side and back, then front
and side) before each:

- The first build leant every shape's normals up their limbs (the troll's 0.8): in the smash and the throw the raised
  hand glowed solid, and in the howl the head, tipped back toward the camera, drew as a lit hood. Normals leant up in
  the rest pose point away from the camera once a clip turns the part over. The head, the jaw, the arms and the hands
  keep their own normals; the rest lean 0.4.
- A thin palm block lit whole edge-on when the claws rose: it is thicker.
- The head at the vendor's size was lost over the hump (critique 1): 1.4 times, a little forward. The ears scaled with
  it stood up like a jackal's: they are shorter.
- The spine line alone left the back an empty gap between the strong arms from above (critique 1). The shoulder
  blades' lines over the hump give it mass; a ring round the chest as well broke it into slivers (critique 2).
- A ruff of five spikes round the neck, and two fangs, knotted the head at game size (critique 2): three round the neck,
  no fangs. Thin spikes read as stray strokes (critique 1): each is a broad closed pyramid.
- Four talons, the thumb's too, were hairlines at game size (critiques 1, 2): three thick ones.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near with the shatter, and the crowd), labelled A and
B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh B.) "B: matches the style. It's built from simple prisms ... The edges join into closed facets over a dark
   body, and the silhouette is clean." Of the trailer's: "a smooth sculpted mesh drawn with crease lines ... At far and
   game size those dashes merge into a blue speckled haze", "a fleshy zombie or ghoul"; of the new: "a spiked armored
   ape or wolf-beast". Asked: a bigger head, closed spikes, mass on the back, thicker claws, a less jumbled shatter.
   Done: the head 1.4 times, broad closed spikes, the shoulder blades' lines, thicker talons; the shatter is the shared
   shatter's, not the mesh's.
2. (New mesh A.) "A is clearly the better one. It's the only one that reads as the target style ... In the crowd it
   looks like a pack of low-poly synthwave werewolves." Of the trailer's: "each pose becomes a noisy blue blob with a
   brighter knot at the head", "a skinny, hairy ghoul or ape". Asked: the snout, ears and spikes knot at game size,
   slivers on the back, hairline claws, seams at the elbows and wrists, open slivers in the shatter's last frame. Done:
   fewer neck spikes and no fangs, the chest ring dropped, three thick talons. Kept: the soft rings at the elbow and
   forearm (one prism per limb: drawn, rings at each joint read as a kit of boxes, the goblin's lesson).

### What still looks weak

- The long arms are the brightest thing on the figure: their sides catch the rim as bright bars when they swing across
  the view, more than the body does.
- In the howl the snout points at the camera, so the head reads as a faceted cone with ears rather than a profile; the
  pose is short and the moon disc and the sound carry it.
- The shatter's middle frame is a dense jumble of shards (the shared shatter, as for every type).
