# Monster: goblin

Written by `champions monster review goblin` and `champions monster review goblin --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/goblin.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 436/436/436 in the default look (the trailer's 12457/8878/4480), 28 shards, 83 bones, standing 1.182 m.

## The recipe

The goblin (ps-6y2.35): the vendor's armoured goblin rebuilt as a few simple faceted shapes, every edge it designs
drawn, around what names a goblin: big ears, a long nose, a hunch.

The trailer's drawing of the vendor mesh is a filled magenta knot at game size: 12,457 triangles of plate, straps,
the belt's rows, the skirt's flaps, spiked boots and gloves, a caged helmet and the teeth light so many creases that
the lines merge into a fill (the operator's dense-swarm.jpg), and its 9 cm ears hide behind the pauldrons, so
nothing names a goblin. What changes:

- The head: 1.45 times the vendor's, sunk between the shoulders and thrust forward, the hunch. A jutting jaw, a ring
  at the brow and a skull closing under a ridge from forehead to nape (an apex's spokes read as an umbrella from the
  camera); a long nose; two slanted eye slits, a scowl, on the forehead's planes, which face up toward the camera.
- The ears: broad leaves reaching twice as far out as the vendor's (0.31 m from the middle), drooping at the tips,
  facing up so the camera sees each one whole from every side, folded along one crease and drawn by their outline.
  Rigid on the head: the clips lift the vendor's ear bones, which turned long ears into a rabbit's.
- The body: one six-sided shape with a flat front from the crotch to a humped back, the chest pushed forward, with a
  belt round it and a loincloth hanging front and back from under the belt.
- The limbs: each arm and each leg one four-sided prism through the vendor's joints, so the outline closes at the
  elbow and knee: thin arms with bracers flaring to the wrist and mitten hands; legs with a boot flaring at the top,
  a diamond guard on the knee and wedge feet.
- The sword: a broad blade of diamond section with a crossguard, a grip and a pommel, on the vendor sword's axis and
  length (its thin blade drew a hairline). The shield: a thin plank board with an arched top, 85% of the vendor's,
  on its plane but held out from the hand and leant 25 degrees further out at the top, so its face shows from above
  rather than its edge.
- The big plates' tops (ears, shield, loincloth) have vertex normals leaning up, so a plate turned across the view
  stays dark rather than lighting whole in the fresnel rim; only their tops, so a shard turned over never shows a
  face whose normals point the other way (it drew solid).
- Dropped: the pauldrons and collar (boxes on the shoulders that made it a little mech), the caged helmet, the teeth,
  eyeballs and earrings, the skirt's flaps, straps, spikes and the shield's boss (louder than the creature).

Each shape draws its polygons' outlines, never their fan diagonals, and a plate only its top. 436 triangles at every
LOD, where the vendor's are 12,457, 8,878 and 4,480. Each shape is skinned by transfer from the vendor's nearest
vertices over its own bones (never across limbs or sides); the head and ears rigid on the head bone, the sword and
the shield on the weapon bones that carry the vendor's (R_w, L_w), both carried, so the shatter flings them.

Source: Magic Pig Games' Goblins LP.prefab (sha256 2a36906f05a8a301a13d8595bec436a6bb6f5abbfbbbd4eb3d3afe2d52c84175)
and Goblins LP.fbx (a46dcba399602bc28e681dc81b003d1319c58014cb3c594f4cef1dddbf92b6b5), as manifest.json lists them.
No Blender: numpy only. Bake: `just champions-tool monster bake goblin`; review: `just champions-tool monster review
goblin` (and `--look trailer`).

## Notes

ps-6y2.35, by the monster-mesh-design skill. The default look draws the recipe's mesh: six blind critiques each
judged it the better read at the in-game size and in the crowd. `--look trailer` is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

Watched in the trailer (`captures/trailer-action-edge.mp4`, the magenta swarms at 8-50 s), in
`docs/champions/reference/dense-swarm.jpg` and on these sheets. The vendor goblin is a slight armoured humanoid
(1.2 m) with big pauldrons, a collar, a chest plate, a belt of rows, a skirt of cloth flaps, spiked boots and gloves,
a caged helmet, teeth and earrings, a long thin sword and a plank shield: 12,457 triangles, nearly all of them small
facets turned sharply against each other, so:

- at game size every crease draws and the lines merge into a filled magenta knot, a dome on legs; in the crowd the
  goblins are filled magenta figures, not line art (the operator's complaint), and the shield's planks a barcode;
- what names a goblin doesn't survive: its ears reach 9 cm and hide behind the pauldrons, its face is a grille of
  helmet cage and teeth;
- the sword is one hairline; the shatter throws curled, hatched scraps.

What does read: the hunched, big-shouldered posture, the sword and the shield.

### What was tried

Each by `champions monster review goblin --out <scratch>`, judged at game size and in the crowd:

- The whole figure from simple faceted shapes with every designed edge drawn: the first version (with the vendor's
  pauldrons as boxes, an eight-sided torso and a skirt) read as a little mech, the skirt as a lampshade, the ears as
  a robot's antenna fins. The pauldrons and the skirt went; the torso became six-sided.
- The ears on the vendor's ear bones: the clips lift them, so long ears stood up as rabbit's ears or a horn. They are
  rigid on the head, broad and drooping, facing up. A horizontal ear pointing away from the camera still projects up
  over the head when a goblin walks across the view; the broad leaf keeps it an ear rather than a spike.
- A box of a head (a hipped roof with one forehead plane carrying both eyes): cleaner, but faceless from the game
  camera, since the idle clip bows the head. The head keeps a front corner with an eye slit on each forehead plane.
- Vertex normals leaning up on whole plates fixed the shield lighting whole in the fresnel rim, but the shield's
  shard then drew solid when it turned over (its back face's normals pointed up while it faced down). Only the
  plates' tops lean.
- A pyramid boss on the shield knotted into a hot dot at game size; a flat diamond boss was "the most readable thing
  on screen". The board is plain.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near poses with the shatter, and the crowd),
labelled A and B at random (the new mesh B in the fourth), no docs, no recipe, no numbers, asked the skill's
question.

1. The first faceted figure with long ears: "A reads better, at game size and in the crowd. It is the only one that
   looks like a low-poly 3D model ... a goblin or imp foot soldier with huge pointed ears". Of the trailer's: "a dense
   pink scribble". Of the new: the ears "merge into one spike ... a pointed hat", "a stack of boxes with gaps", the
   sword "a hairline", "the head has no face". Done: the ears flattened into leaves drawn by their outline, hips joined
   to the body, thicker limbs, a broader blade, eyes.
2. "A is clearly better: at the in-game size and in the crowd it reads as a creature, and B reads as a smudge."
   Asked: close the outline, a face, and "the shield turns into a hot patch" (its pyramid boss). Done: one body from
   the crotch to the shoulders, a bigger head with a sloped forehead carrying the eyes, the boss flat.
3. "A is better at every size and in the crowd. B is not in the target style ... It is not worth fixing; build on A."
   Asked: a head of fewer planes with "one bold pair of eye slits", the tabard attached, ears with a fold, the
   shield's face shown rather than its edge, and B's "hunched, big-headed posture". Done: the head sunk and thrust
   forward, a humped back, the loincloth from under the belt, one crease per ear, the shield leant out 25 degrees.
4. (New mesh B.) "B reads much better as a creature at game size and in the crowd ... It looks like a goblin." Asked:
   fewer facets where the head meets the chest, the shield's emblem "louder than the creature", a shard drawn solid.
   Done: a flat-fronted torso, the nose's base hushed, the boss dropped, normals leaning only on the plates' tops.
5. "A, clearly ... every figure is a distinct little armoured goblin. The shatter throws closed 3D boxes and prisms."
   Asked: legs as "one tapered prism", a smaller shield whose edge-on slab "turns into a filled bright band". Done:
   each arm and leg one prism, the shield 85% of the vendor's and half as thick, held out from the hand.
6. The mesh as delivered: "A reads as the intended style and B doesn't. I prefer A at game size and in the crowd,
   though B's outline says 'creature' more strongly ... A's chunks are clean closed shapes and work well." Its asks
   are what still looks weak, below.

### Still weak

- It reads as a goblin soldier, but "a little like a robot" (critiques 1, 2, 3, 6): square-sectioned limbs and the
  boxy torso, where the trailer's rounded hunch "says creature more strongly". Rounder limbs (six sides) would cost
  lines at game size, which is what drowned the trailer's.
- The face is small at game size: the eyes and nose read up close and at game size only on goblins facing the
  camera; the ears carry the head.
- The shield's plain board is the biggest shape in a crowd and reads as a crate or a picture frame as often as a
  shield (critiques 5 and 6); critiques 4-6 asked for less shield, 2 and 5 for a boss.
- The sword stays one long piece through the shatter: it is one rigid carried part.
- The ear leaves are thin plates, so their shards are thin too (critique 5: "a spiky asterisk").

### Cost and the review sheets

436 triangles at every LOD (a mesh that flags its edges isn't decimated), where the vendor's are 12,457, 8,878 and
4,480: about a twentieth of the vendor's LOD0 and LOD1, which the game camera mostly draws.
