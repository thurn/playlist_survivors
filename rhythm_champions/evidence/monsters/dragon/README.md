# Monster: dragon

Written by `champions monster review dragon` and `champions monster review dragon --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/dragon.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, breath, tail, fireball, fly, land, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, breath, tail, fireball, fly, land, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 790/790/790 in the default look (the trailer's 5444/2815/1408), 35 shards, 108 bones, standing 2.949 m.

## The recipe

The dragon (ps-6y2.40): the boss rebuilt whole as a few simple faceted shapes on the vendor's bones.

The trailer's drawing of the vendor mesh reads beautifully gliding (long clean strokes for the wings and body), but on
the ground, where the duel keeps it, it isn't dark inside: wherever a wing turns edge-on to the camera (every folded
pose, the tail sweep, the landing) its curved membrane glows as a solid red band in the fresnel rim, the torso between
the wings is a knot of crossing lines, the head a nose-on box full of teeth, and the toes and claws scribble. What
names the dragon (the bat wings spoked by long fingers, the long neck and tail, the horned head) is in it, so every
part is new, skinned to the vendor bones, and every edge that draws is chosen here (each polygon's edges, never its
triangles' diagonals, and only some of those):

- The spine: one loft from the back of the head down the neck, over a broad deep chest and out along the tail, drawn
  by its ridge and its shoulders and quiet below; chevrons across its back, two up the neck, one over the shoulders
  and one over the hips, two down the tail (on every segment it read as a centipede, on every other one as a ladder,
  and with none on the trunk its ridge was an arrow's shaft); a flat arrowhead spade at the tail's tip (a bare point
  read as a spear).
- The head, nearly half again the vendor's skull: a long, low faceted wedge from broad cheeks and brow ridges to the
  nose, drawn round only at the brow and the nose (with parallel sides, or round every section, it read as a box with
  a pyramid of a snout); a jaw of its own whose back corners jut past the cheeks; two thick horns low on the back of
  the skull, swept back along it into the head's outline (up from the brows or out to the sides they stood off as
  daggers, or a fox's ears); slanted eyes under the brows.
- The wings: thin plates between the vendor's wing bones, the arm and first finger the leading edge, two more
  fingers spoking from the wrist (the vendor's second and fourth: with all four the folded wings drew as stacks of
  finger blades under a sawtooth of tips), a trailing edge bowed in broad scallops between their tips and back to the
  flank, the root on the body's shoulder line so the wing grows out of the body. Every landmark rides its own bone
  and each trailing-edge point the two fingers either side of it, so the edge stays taut as the wing folds (skinned
  from the vendor's membrane, those points rode one finger and zigzagged like lightning bolts). Two narrow bevels
  close each plate's outline, so an outline edge seen face-on has a lit face either side of its line, as a finger
  does, and draws as wide (on a rim square to the plate it drew at half the width and faded). A thick spar along the
  arm; no claw at the wrist (it drew a short bright bar).
- Shading normals for the membranes: by the bone each point rides, the direction in its rest frame that the vendor's
  clips turn up, toward the game's camera, in the most frames (measured from the baked bone texture). The rim then
  leaves a membrane seen nearly edge-on dark, where its facets' own normals lit it whole: the folded wings on the
  ground no longer glow. Every other shape leans its facets' normals up (the big ork's trick): unleant, the horns'
  bases and the arm spar lit as small solid chips.
- Legs lofted through the vendor's joints, thick, the hind ones with a muscled thigh, drawn only along them, each into
  a low wedge of a foot narrowing to a blunt point (separate toes and claws broke into scratches at game size, and
  thin legs with tall feet read as stakes).

The mesh flags its edges, so it is drawn as designed at every distance rather than decimated: 790 triangles at every
LOD, where the vendor's are 5,444, 2,815 and 1,408.

Source: Magic Pig Games' Dragon_LP.prefab (sha256 bb4269fa952bd422abdf9498f24a40ab958c52f0d78728ff64b52d7977264f52) and
Dragon_LP.fbx (daee8e51e18919ed78eb93f4a3ea8bbaa13e52837f07679d9541794ae64b7f32), as manifest.json lists them. Only its
rig is used, with its joints (each bone's pivot on its parent, solved from the baked clips) to place the shapes; the
tail rides the tail part's own bone entries. No Blender: numpy only. Bake: `just champions-tool monster bake dragon`;
review: `just champions-tool monster review dragon` (and `--look trailer`).

## Notes

ps-6y2.40, by the monster-mesh-design skill. The default look draws the recipe's mesh: seven blind critiques, each
judging it the wireframe look and the better read at the in-game size, and all but one in the crowd. `--look trailer`
is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

In the trailer itself (`captures/trailer-action-edge.mp4`, 52 to 60 s; `docs/champions/reference/dragon-live.jpg`)
the dragon mostly glides past side-on, wings spread, and reads beautifully: long clean strokes for the wing fingers
and the body. The game keeps it on the ground facing the hero, seen from the front and above, and there (these
sheets, `trailer-*.jpg`, and the duel stills in [ps-6y2.29](../../ps-6y2.29/README.md)) it isn't dark inside:

- wherever a wing turns edge-on to the camera it glows as a solid red band in the fresnel rim: one folded wing in
  idle, walk and breath, the spread wing in the tail sweep, the raised wings in the landing, and in flight near the
  camera (the dive); at the bottom of a flap both wings wrap the body as one red blob;
- the torso between the wings is a knot of crossing lines (wing roots, shoulder spikes, the saddle's straps);
- the head is a nose-on box with a grille of teeth under a crown of spikes; the toes and claws scribble at game
  size, and the tail ends in a starburst;
- its shards are small curled scraps.

What names it is all there: bat wings spoked by long fingers, the long neck and tail, the horned head. So every part
is rebuilt (the recipe above lists them), every edge that draws chosen, about a seventh of the vendor's triangles.

How the shapes were placed: the export has no bone positions, so each bone's pivot was solved from the baked bone
texture (the point a bone and its parent move alike over every frame of every clip, exact to a millimetre for all but
the wing fingers' roots), and the recipe's landmarks are those joints. The membranes' shading normals were chosen
the same way: for each finger, the rest-frame direction that the clips turn toward the sky (and so toward the
camera, always above) in the most frames; it faces up in about 95% of all frames. A numpy poser of the recipe's mesh
through the baked clips (lines over a z-buffer, with the rim estimated) checked each change in a second before a
Unity review.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game, near with the shatter, crowd), labelled A and B at
random, no docs, no recipe, no numbers, asked the skill's question.

1. A first rebuild (new mesh A): "A reads as a low-poly dragon, both at the in-game size and in the crowd. B
   doesn't." Against A: "The wings come apart": their outlines faded, leaving "rakes or lightning bolts with open
   spokes"; the downstroke "fills solid bright red"; "every neck, body and tail segment has the same chevron
   band. It reads as a centipede"; "the head is a plain box". Done: the wing outline drawn on two bevels (on a rim
   square to the plate it drew at half the fingers' width); every wing landmark skinned to its own bone (skinned from
   the vendor's membrane, the trailing edge rode one finger and zigzagged across the folded wing); chevrons only on
   the neck and tail; a broad chest; a bigger faceted head with long horns and cheek spikes.
2. (New mesh A): "A is clearly closer to the look and wins at game size and in the crowd." Against A: "the trunk is
   a thin spindle lost between two big jagged wing outlines", the head "a long rectangular box with no horns",
   the tail "a single thin spike ... a spear", "short bright bars at the wing wrists", "stick legs". Done: a wider,
   deeper trunk; the head a wedge with horns flaring out; a thicker tail with a spade; the arm's spar ending at the
   wrist; thicker legs with wedge feet; a normal per finger.
3. (New mesh A): "A is better on both counts". Against A: "the wings have no edge where they meet the body", the
   head "a cube with a pyramid snout", the neck and tail rings "a ladder", the claws "short bright bars". Done:
   the membrane's root moved onto the body's shoulder line; the head drawn round only at the brow and the nose; two
   chevrons each on the neck and tail; the wrist claws dropped.
4. (New mesh A): "A is better." Against A: each folded wing "a stack of separate finger blades with a sawtooth
   trailing edge", "one horn sticks out as a long, thin free spike", "one long spine line ... an arrow shaft".
   Done: three fingers instead of four (the middle one folded into one broad scalloped panel); a thick spar along
   the arm; horns swept back along the skull; a chevron over the shoulders and one over the hips.
5. (New mesh B): "B has closed facets, a dark body, a spine ridge, a wedge head with horns and an arrowhead tail.
   At game size it reads clearly as a dragon from above." Against B: the wings "read as jagged flames" (it asked for
   more finger spars, where the fourth asked for fewer: three stay), "a crowd thorn-burst" of spikes, "small solid
   chips" at the horn bases and wing roots, "legs read as pegs". Done: the cheek spikes dropped, the head larger, the
   horns' and arm spar's normals leant up, a muscled thigh.
6. (New mesh B): "B is the better drawing. It's the one that reads as the synthwave look and as a creature at game
   size and in the crowd." Against B: the downstroke's wings, "a horn stands as a separate dagger next to the head",
   "legs look like stakes", and from the side "pointed ear-horns make it read as a fox or wolf". Done: the horns set
   low on the back of the skull; a longer, flatter snout; thicker legs drawn only along them, into low wedge feet.
7. The final mesh (new mesh B): "B is the better drawing. It reads as a low-poly faceted model at every size and in
   the crowd", the trailer's "a wireframe of a dense mesh: lit inside, scratchy, and in places a red smear". Against
   B: the folded wings at the bottom of the wingbeat "light whole as two filled red panels, the only real patches in
   the set"; at game size the wings "read as jagged flames beside the body" and the open wings of the crowd cell
   "merge into a cracked-glass mesh"; the legs are still thin in flight and the horns small; the spade "could read as
   a second head". Left as they are: see below.

Also seen in the game's duel (`duel.jpg`: `champions stills --scene duel-dragon --scene duel-dragon-death`, the
trailer's look on the left): on the ground and in the dive the new wings stay outlined and dark where the trailer's
glow as solid bands, and its shards are larger closed pieces; but it is a lighter drawing than the trailer's dense
one, less of a blaze over the warm zone and the red telegraphs.

### Still weak

- The bottom of the wingbeat (`fly` here, the dive's flight in play): the wings wrap down round the body, and the
  fresnel rim lights their outer faces as two red panels in both looks. No one normal faces the camera there, and the
  rest of the beat is clean (the vendor's wings glow through most of it).
- A wing seen exactly edge-on is a stick (the landing's near wing, the tail sweep's far one): a thin plate has nothing
  else to show.
- The critiques split on the wings (four fingers drew as "a stack of finger blades", three as "flames" wanting more
  spars) and on the horns (unseen, then "a stray dagger"): three fingers and short horns along the skull are the
  middle. Several asked for more mass in the legs and the neck.
- The crowd cell packs a dozen 12 m wingspans into one frame, which play never does (one dragon, in its duel); there
  the open wings tangle in both looks.
- Membrane shards that tumble edge-on flash as solid slivers for a frame, as the trailer's do.

### Cost

790 triangles at every LOD (a mesh that flags its edges isn't decimated), where the vendor's are 5,444, 2,815 and
1,408; 35 shards (the vendor's 34). One dragon is drawn at a time.
