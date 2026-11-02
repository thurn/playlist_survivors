# Monster: skeleton

Written by `champions monster review skeleton` and `champions monster review skeleton --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/skeleton.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 2524/2524/2524 in the default look (the trailer's 9310/4734/2367), 30 shards, 120 bones, standing 1.697 m.

## The recipe

The skeleton (ps-6y2.31): the vendor's armour kept, the parts that drew scribble rebuilt as simple faceted shapes.

The trailer's drawing of the vendor mesh already reads in a crowd by its helmet, its shield's circle and its sword,
and its armour (cuirass and breastplate, pauldrons, tassets, greaves, boots, bracers, shield) is low-poly plate
whose creases close into facets. So the hand is light: those parts stay as the vendor made them, drawn by their
24-degree creases as in the trailer. What changes:

- The sword: a broad diamond-section blade with a ridge, a crossguard, a grip and a pommel on the vendor sword's
  axis and length. The vendor's thin blade drew one hairline that read as a spear or a needle.
- The helmet: a faceted helm (a quiet cap in an octagon, a brow ring, cheek and neck guards, a nasal). The vendor's
  rounded helmet lit only some of its creases, in lines that stopped mid-face and a dashed crest.
- The skull: a block with a face plane, hexagonal eye sockets, a triangle nose and a jaw, where the vendor's skull
  and sixteen teeth drew a knot of tiny strokes in the helmet's opening.
- The bare bones: four-sided prisms for the thighs, shins, upper arms and forearms, diamonds at the knees and elbows,
  a six-sided neck, a wedge of a pelvis and mitten hands, where the vendor's finely modelled bones and forty finger
  bones drew short strokes at every joint.
- The waist: one faceted band for the vendor's rounded belt and the plate under it, which drew broken strokes.
- The shield: the vendor's, pressed to 40% of its depth. Its domed face and deep rim caught the fresnel rim facet by
  facet and drew a lit band down its edge; thin, the rim is an outline and the face one dark plane.
- Dropped: what is hidden inside the armour in every view (toes and feet in the boots, the spine, ribs and
  collarbones under the cuirass), and straps, cuffs and second layers of plate that only doubled a line beside them
  (bracer and boot straps, wrist cuffs, the inner tassets, the lower pauldrons).

Every edge's flag is computed here with the trailer's rule (creases over 24 degrees, open edges), so the mesh is
drawn as designed at every distance rather than decimated: 2,524 triangles at every LOD, where the vendor's are
9,310, 4,734 and 2,367. The new shapes are rigid to one vendor bone each, so the mesh moves with the vendor rig and
clips; the sword stays carried, so the shatter flings it.

Source: Magic Pig Games' SkeletonWarriorLP.prefab (sha256
a722c1da9a55f88a871f956f53a4838bf294cb1af5298047afb2a469904f6c94) and SkeletonWarriorLP.fbx
(de8f13673aade336a5d2ef08ed843e2966d2148d0ce00bc5138bed2198d8644a), as manifest.json lists them. No Blender:
numpy only. Bake: `just champions-tool monster bake skeleton`; review: `just champions-tool monster review skeleton`
(and `--look trailer`).

## Notes

ps-6y2.31, by the monster-mesh-design skill. The default look draws the recipe's mesh: three blind critiques judged
it the better read at the in-game size and in the crowd. `--look trailer` is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

Watched in the trailer (`captures/trailer-action-edge.mp4`, 16-36 s) and on these sheets: the skeleton already reads
in a crowd as a cyan knight by its shield's ten-sided circle with its rivets, its helmet and its long sword, and its
armour is low-poly plate whose creases close into facets. What drew scribble, at game size and in a crowd:

- the sword, a single hairline (a needle or a spear) with no guard;
- the helmet, a rounded dome whose 24-degree rule lit only some creases: a dashed crest and lines stopping mid-face;
- the face in the helmet's opening, the skull's and sixteen teeth's tiny creases in a knot;
- the bones: the knees' knobs, the forty finger bones, the pelvis between the tassets, all short strokes;
- the rounded belt in broken strokes, and straps, cuffs and doubled plates (inner tassets, lower pauldrons) beside
  lines already drawn;
- the shield's domed face and deep rim, lit facet by facet into a glowing band down its edge when seen obliquely.

Hidden in every view of the rest pose (counted over 24 views), and dropped at no cost: the toes and feet in the
boots, the spine, the ribs and the collarbones.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game, near poses, near shatter, crowd), labelled A and B at
random, no docs, no recipe, no numbers, asked the skill's question.

1. The sword, skull, hands and pelvis rebuilt, hidden parts and straps dropped (the new mesh was A): "A reads better,
   but not by much. The bodies are almost the same in A and B. The difference is in the sword and the shatter." Of
   the trailer's: "The sword is a single hairline. It has no guard ... In the crowd it looks like a needle or a
   spear." In both: "Too many interior lines. The small creases on the hands, belt, knees and arm straps are drawn
   as bright and thick as the outline", and "the sword hand is a smudge". Done: the knees, leg and arm bones, the
   belt and the helmet rebuilt; the inner tassets and lower pauldrons dropped.
2. Then (new mesh A): "A reads better, both at the in-game size and in the crowd ... The helmet is a clean faceted
   octagon, the limbs are closed boxes, the sword has a clear crossguard and the shield is an octagon with dark
   faces. In the crowd the gem-like heads make each knight readable at a glance." Of the trailer's: "the same model
   with more scratches. The helmet is a smooth dome with a seam down the middle instead of facets". In both: "The
   shield's rim ... shows as a glowing filled band on its right edge when idle and walking. It's a lit patch, not an
   edge." Done: the shield pressed to 40% of its depth; the band is gone. Its count of "about three fewer monsters"
   in the trailer's crowd is the review spacing the crowd by each mesh's measured radius, not culling.
3. The final mesh (new mesh B): "B reads better, at the in-game size and in the crowd. Both drawings hold a closed
   silhouette with a dark body, and they're nearly identical at a distance, but B is cleaner where they differ and
   its shatter is far better." Of the trailer's: "the skull face and dashed centre line inside the head are
   scratches, not facets", "the sword ... a single needle line", and the legs, sword arm and sword staying in one
   upright chunk in the shatter. In both: "the chest's horizontal plate bands, the leg seams and the tiny hands and
   feet turn into a tangle of lines". Tried: a breastplate of two facets meeting in a ridge for the banded one. At
   game size it was indistinguishable (the shield and arms cover the chest from the game camera) and up close its
   big facets caught the rim as a lit wedge, so the vendor's plate stays.

### Still weak

- The attack pose crouches behind the shield into a knot at game size, in both looks: the clip and the camera's
  angle, not the mesh.
- The shield edge-on is one bright streak (a disc's outline piled on itself), and its rivets are specks at game
  size; kept, as they name the shield up close.
- The shield's shards are thin open triangles and slivers, in both looks: the shatter's crack outlines on a thin
  plate.
- Interior lines are as bright as the outline (the shader's gains, the same in both looks).
- In the near shatter row the floor's grid lines kink into V spikes at the top of the frame, in both looks: the
  grid under the review's near camera, not the monster.

### Cost and the review sheets

2,524 triangles at every LOD (a mesh that flags its edges isn't decimated): fewer than the vendor's LOD0 and LOD1
(9,310 and 4,734), which the game camera mostly draws, and about its LOD2 (2,367). The review now saves sheets up to
4,800 pixels wide before shrinking them, so `*-near.jpg` keeps the near camera's detail (it was saved at half size,
no more detailed than the game sheet).
