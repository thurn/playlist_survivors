# Monster: troll

Written by `champions monster review troll` and `champions monster review troll --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/troll.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 673/673/673 in the default look (the trailer's 5220/2686/1344), 33 shards, 50 bones, standing 2.45 m.

## The recipe

The troll (ps-6y2.38): the vendor's hunched brute and its spiked club rebuilt whole as a few simple faceted volumes.

The trailer's drawing of the vendor mesh reads by its bulk and its club, and up close as a man in armour: the box of
its brow, its small eyes, ears and tusks and a necklace of teeth knot into a round helmet with a grille at the top of
the figure, and the wraps round its arms and legs, the belt of ropes and pouches and the club's iron bands and grain
draw parallel hatching. At game size it is a dense gold scribble, never dark inside, and its shatter flies as curls.
Blind critiques called it a diver, a knight or a mech. What names the troll (the huge hunched bulk, the head slung
low and forward, the very long heavy arms, the tusks and the great spiked club) is in it but buried. So nothing of
the vendor mesh is kept; every part is new, skinned to the vendor bones, and every edge that draws is chosen here (a
polygon's edges, never its triangles' diagonals, and only some of those):

- The trunk: hips, a gut and a hump of shoulders rising behind the neck to a small crest, so the head hangs low in
  front of it. Behind, a few big planes: a spine ridge from the small of the back up to the crest, one broad plane
  each side of it running down to the shoulder, and the lower back under one line across it; in front,
  the belly's edges up to a line across the chest, and one across the gut. Lines over the shoulder blades, round the
  hump and up its front tiled it into a tortoise's shell, and rings round a dome drew a target.
- The head: 1.45 times the vendor's, sunk 0.26 m and thrust 0.38 m forward, clear of the shoulders: the hunch the
  clips (the big ork's) don't have. One wedge from a narrow nape to a wide jaw, an ape's from above or behind, a line
  round its crown, a heavy brow dipping in a scowl, slanted eyes, a blunt nose and two short thick tusks up beside
  the snout. From behind, the 60-degree camera sees the face above the skull: ears, and tusks reaching past the
  snout's top, read there as horns, a crown or a cat's ears, so the ears are dropped and the tusks stay inside the
  snout's outline.
- The arms: very long and heavy, each one six-sided prism from the shoulder to the wrist, tapering to the elbow and
  swelling in the forearm, a line round it only at the elbow and the wrist; a big fist of one block with one line
  round it at the knuckles.
- The legs: short and thick, each one six-sided prism from the hip to the ankle with a line round it only there, and
  a wide flat foot joined to it (with its back drawn, a loose box). A belt under the gut.
- Dropped: the wraps, the necklace, the ropes and pouches, the ears, the toes, and the loincloth (an empty slab
  under the belly at game size).
- The club, on the vendor's axis, a little shorter: a thick six-sided head with four big spikes swept a little toward
  its end (radial, the one pointing at the camera drew an X), a thick shaft nearly parallel down to the fist
  (tapering to the grip, its long lines met there like a searchlight's beam), a grip and a pommel. Carried, so it
  shatters with the body.

Every triangle has its own corners and its facet's normal leant along its shape toward its root, as the big ork's
are (up the trunk, the head and the legs; toward the shoulder along each arm, which is up while it hangs): the rim no
longer lights the long facets of the trunk and the hanging arms as bands, and a face turned against the lean keeps
its own. The mesh flags its edges, so it is drawn as designed at every distance rather than decimated: 673 triangles
at every LOD, where the vendor's are 5,220, 2,686 and 1,344.

Beside the big ork, its sibling in the counter family, it reads apart by shape: the ork stands upright, horned, a
strap and a pauldron on it and a long notched cleaver in its hand; the troll is a bare boulder of a back with its
head low in front of it and a spiked club.

Source: Synty's Character_BR_Troll_01.prefab and SM_Wep_Troll_01.prefab from POLYGON Fantasy Rivals (package sha256
f5b2197465eb4fcd0ce06cec1faa2790523a8a1ae8a669c1ef9a0b0316919e6c), as manifest.json lists them. Only its rig is used,
and its rest pose and skin weights to place and skin the shapes: each shape takes the weights of the vendor's nearest
vertices over its own bones (never across limbs or sides); the head is rigid on the head bone, the club on the
weapon's. No Blender: numpy only. Bake: `just champions-tool monster bake troll`; review: `just champions-tool monster
review troll` (and `--look trailer`).

## Notes

ps-6y2.38, by the monster-mesh-design skill. The default look draws the recipe's mesh: six blind critiques, each
preferring it to the trailer's drawing at the in-game size and in the crowd. `--look trailer` is unchanged (the
vendor mesh; its sheets byte-identical across every render).

### The trailer's drawing of the vendor mesh

Watched in the trailer (`captures/trailer-action-edge.mp4`, the amber clubbers among the swarms at 14-24 s; the
brute with the club at the left of `docs/champions/reference/opening-duel.jpg`) and on these sheets (`trailer-*.jpg`).
The vendor troll is a 2.6 m hunched brute with very long arms and a gut, a flat box of a brow over small eyes, small
pointed ears and tusks, a necklace of teeth, wraps round its forearms and ankles, a belt of ropes and pouches, bare
feet with toes, and a 2 m club with iron bands and spikes: 5,220 triangles with the club. So:

- at game size it reads by its bulk and its club, but every wrap, rope, band and the club's grain draws parallel
  hatching; it is never dark inside, and its head (brow, eyes, ears, tusks and necklace together) is a round helmet
  with a grille at the top of the figure;
- in the crowd the trolls are a field of gold hatching; up close, an armoured man in a helmet more than a troll;
- the shatter throws curled scraps.

What reads: the hunched bulk, the long arms and the club. What names it: those, the head slung low, and the tusks.
Its sibling in the counter family, the big ork (ps-6y2.37, its new mesh on staging), stands upright, horned, with a
strap, a pauldron and a notched cleaver; the troll is told apart by its hump, its bare bulk and its spiked club.

### What was tried

Each by `champions monster review troll --out <scratch>` (about 30 s), judged at game size, in the crowd and up close,
with a numpy previewer of the drawn edges at rest (front, side, back, the game camera at several yaws) before each:

- Every polygon of an eight-sided trunk drawn: from above, the hump's rings and its lines converging on the neck drew
  a spider's web, and the head was lost on top of it.
- Only a few long lines on a twelve-sided trunk, no rings: the trunk turned into lit glass, the rim glowing in
  patches round the hips and the gut where no line was.
- Seven corners, every line drawn, a flat lid on the hump: clean, but a box on its back, then a tortoise's shell
  (critique 1); a kite of shoulder-blade lines (the demon's) still left a dozen cells with the lid round it
  (critique 3). The back is two big planes either side of a spine ridge running over a small crest to the neck.
  With the crest's outline hushed too, the ridge stood up from an open outline like an antenna, so the crest stays
  small and outlined.
- The clips are the big ork's upright ones, so the hunch is in the mesh: the hump rises behind the neck and the head
  is sunk and thrust forward. Under the hump the head was a lump or a fist (critiques 2, 4): it is now 1.45 times the
  vendor's and forward enough to clear the shoulders, with its outline closed at the nape, the crown and the snout.
- From behind, the 60-degree camera shows the face above the skull, so whatever reaches past the snout's top stands
  up over the head: leaf ears, swept-back ears and ears out to the sides read as a crown, horns or a hedgehog; long
  tusks, forward tusks and tusks splayed out read as horns or a cat's ears (critiques 1, 5). The ears are dropped
  and the tusks are short and thick, inside the snout's outline.
- A head of a skull and a separate long jaw: from behind, a tall box or chimney. One wedge from a narrow nape to a
  wide jaw reads as an ape's head. A beak of a nose looked owlish (critique 4): it is blunt.
- A loincloth hanging from the belt: an empty slab under the gut at game size (critiques 1, 2). Dropped.
- The club's shaft tapering to the grip: its six long lines met there like a searchlight's beam (critique 2). It is
  thick and nearly parallel down to the fist.
- Radial spikes: the one facing the camera drew an X (critique 3); swept 35 degrees toward the club's end they lay
  along its head and vanished; swept 20 degrees they read.
- Smooth normals leant up (the goblin's) against flat facet normals leant along each shape toward its root (the big
  ork's): the latter keep the trunk and the hanging arms dark without a crescent on the hump.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near with the shatter, and the crowd), labelled A and
B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh B, seven corners and a lid.) "B is clearly the better drawing. It reads as a low-poly model at game
   size, in the crowd and in the shatter. A doesn't. It reads as a hatched illustration, not a wireframe." Of the
   trailer's: "an armoured robot, a diver in a helmet or a mech"; of the new: "a hunched ogre or gorilla-troll with a
   spiked club. Facets close, the silhouette closes and the inside stays dark." Asked: the back "reads as a turtle
   shell", "the head is lost", the loincloth "floats as a separate rectangle", the legs "thin", the spikes
   "hair-thin". Done: a kite on the back, the head bigger and forward, thicker legs, thicker spikes.
2. (New mesh B.) "B reads as a low-poly faceted model, and it is clearly the better creature at game size and in the
   crowd." Of the trailer's: "an armored knight or a diving-suit robot ... Fix: replace it. Don't try to clean it up."
   Of the new: "a hunched troll, or a gorilla with a club". Asked: the head "reads as a fist" under the hump, the club
   shaft "a beam of light", "an empty rectangle hangs under the idle body", the hump wider than the shoulders. Done:
   the head 1.3 times the vendor's with a scowling brow and slanted eyes, a thick shaft, the loincloth dropped, the
   hump narrower, flat normals leant along each shape.
3. (New mesh A.) "A is clearly better. It's the only one that reads as retro neon wireframe. It's built from closed
   prisms over dark bodies, and both the silhouette and the shatter shards are closed." Of the new: "a hunched,
   knuckle-walking ogre or gorilla-troll with a spiked mace"; of the trailer's: "an armoured deep-sea diver or
   mech-suited soldier carrying a bat or bazooka". Asked: the hump's "about 12 irregular cells", the head "a cube",
   detached fists, fins and an X on the mace. Done: the back as two planes and a spine ridge, a heavier brow, fists
   with a line at the knuckles, the spikes swept and squatter.
4. (New mesh B.) "B is clearly better at game size and in the crowd. Each B is a distinct dark lump with a club; the
   A crowd blurs into an orange tangle." Of the trailer's: "discard it and don't salvage it." Asked: the crown of the
   head "a smooth dark cap with no edges", the face "owl-like", tiny hands and feet. Done: a line round the crown, the
   head out further, a blunt nose, bigger tusks, fists and feet, the legs one prism.
5. (New mesh B.) "B is clearly better at game size, in the crowd and in the death shatter ... closed, dark facets
   inside a closed outline, and the club stays clean at every size." Of the trailer's: "don't tune A; rebuild it the
   way B was built." Asked: the head "disappears" and the tusks look "like cat ears in the crowd", the back "crazy
   paving", the feet "loose boxes". Done: the head 1.45 times the vendor's and clear of the shoulders, the tusks inside
   the snout's outline, the belly's edges stopped at the chest's line, the feet joined to the legs.
6. (New mesh B: the delivered mesh, but for one line.) "B is the look: closed faceted volumes, dark bodies and a
   closed silhouette in idle and walk. It reads as a hulking troll, ogre or gorilla with a spiked mace. B is clearly
   better at game size and in the crowd." Of the trailer's: "It reads as a contour sketch, not a faceted model ...
   the whole thing looks like an armoured soldier, diver or mech carrying a club." Asked: "The hump reads as the
   head. The dome with a split down its middle looks like a small skull on top"; the hips and the flank under the
   raised arm "dissolve into loose vertical strokes" in rear views and the attack; the trunk's shards "large open
   hairline shapes"; the mark on the mace. Done: the ridge across the crest dropped, so the crest is one outlined
   dome. The rest is below.

### Still weak

- The attack: the vendor's leaping clip twists the trunk across the view, so the flank under the raised arm thins
  to short strokes and the hump shrinks behind the arm (critiques 3 to 6).
- From behind, the hips and thighs have few lines: each leg is one prism with a line round it only at the ankle.
  A line at the knee drew a stack of boxes (critique 4); without it they are "dangling lines" (critique 6).
- The shatter: the baker cuts the big dark trunk into large pieces whose crack outlines fly as thin open shapes
  beside the limbs' closed chunks, and a few thin wedges light solid as they turn (critiques 3, 4, 6).
- A spike facing the camera draws a small V or X on the mace's head (critiques 3, 4, 6).
- The face is small at game size: it reads up close and when the troll faces the camera; from above, the head is a
  wedge with a brow line. Critiques 4 to 6 asked for it larger still; it is already 1.45 times the vendor's.
- The faceted hump reads to most critics as a rock troll's or a stone golem's (critiques 3 to 5): a troll, but
  stony.

### Cost

673 triangles at every LOD (a mesh that flags its edges isn't decimated), where the vendor's are 5,220, 2,686 and
1,344: an eighth of the vendor's LOD0, half its LOD2.
