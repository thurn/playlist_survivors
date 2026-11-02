# Monster: demon

Written by `champions monster review demon` and `champions monster review demon --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/demon.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 948/948/948 in the default look (the trailer's 5992/3070/1536), 29 shards, 59 bones, standing 1.491 m.

## The recipe

The demon (ps-6y2.34): the vendor's horned, armoured brute rebuilt whole as a few simple faceted volumes.

The trailer's drawing of the vendor mesh reads as a hunched blue thing with a ragged outline: its dense, uneven
topology lights so many creases that the body is a lattice of tiny facets with no dark inside, a blob at game size,
and its four pairs of small horns, spiked armour, teeth and slender fingers fray the outline into a fringe. What
names the demon (the horns, the claws, the spikes, the hunched brute's back, the beast's legs) is in it but lost.
So nothing of the vendor mesh is kept; every part is new, skinned to the vendor bones, and every edge that draws is
chosen here (each polygon's edges, never its triangles' diagonals, and only some of those):

- The trunk: a chest over a narrow waist, broad shoulders sloping to a bull's neck (on a thin one the lines
  over the shoulders converged into lit wedges). The hunched demon is mostly seen
  from above, down its back, so the back is drawn as a muscled kite: the spine's ridge and a line from each side of
  the neck out over the shoulder blade and in to the waist, the sides and front, and no line round it (a flat top
  at the shoulders drew a lid on a barrel, and lines round it hoops). Its flanks round in narrow quiet facets, so
  no broad facet turns edge-on to the camera at the outline (broad ones lit up as patches there).
- The head: a wedge of a skull with a crest dropping into a V between rising brows (a scowl from above),
  cheekbones, a snout, slanted eye facets and a jaw on the jaw bone.
- The horns: one big pair in four segments, out to the sides and swept back along the skull, a V from above
  whether the head is up or pitched down; the vendor's four pairs drew a knot round the head.
- Arms and legs: six-sided rings blended between the bones at each joint, lines round them only at the elbow,
  wrist, knee and hock; heavy arms; beast's legs with a high hock and three thick clawed toes.
- The hands: a broad palm and three thick claws spread wide, each on its finger's bones so it curls with them,
  where the vendor's four slender fingers drew a bundle of hairlines.
- The armour, kept as what names it: a broad pauldron on each shoulder bridging the trunk and the arm, with one
  broad spike, and a belt; dropped, the chest plate, the bracers' and greaves' spikes, the cheek horns and the
  teeth. Few spikes, and broad: small ones turned to scratches at game size, and spikes down the spine to loose
  dashes on the back seen from above.

Every triangle has its own corners and its facet's normal, as the vendor's low-poly meshes are shaded. The mesh
flags its edges, so it is drawn as designed at every distance rather than decimated: 948 triangles at every LOD,
where the vendor's are 5,992, 3,070 and 1,536. Nothing is carried: the demon fights with its claws.

Source: Magic Pig Games' Demons_LP.prefab (sha256 3bc3d9c5819bb19c9352f6a6bbf4e03cff20da3de84f3542582abac37d19de13)
and Demons_LP.fbx (e3c7dfd3827edcc7948e42e097727077e7911d8119c2568b9dfe21be97ba10dd), as manifest.json lists them.
Only its rig is used, and its rest pose to place the shapes (by eye and by its skin weights). No Blender: numpy
only. Bake: `just champions-tool monster bake demon`; review: `just champions-tool monster review demon` (and
`--look trailer`).

## Notes

ps-6y2.34, by the monster-mesh-design skill. The default look draws the recipe's mesh: seven blind critiques, each
judging it the better read at the in-game size and in the crowd. `--look trailer` is unchanged (the vendor mesh).

### The trailer's drawing of the vendor mesh

Watched in the trailer (`captures/trailer-action-edge.mp4`, the deep-blue crowd at 40 s) and on these sheets
(`trailer-*.jpg`): the demon reads as a hunched blue shape with long clawed arms and a few horn points, but not as a
wireframe. Its dense, uneven topology (5,992 triangles, every crease over 24 degrees lit) covers the body in small
irregular facets, so there is no dark inside: up close it is lace or crazed glass, at game size a mottled blob, at
far size a solid one. Its outline frays into a fringe of short strokes: four pairs of small horns (two up, two
curled round the jaw), spiked pauldrons, bracers and greaves, sixteen teeth and four slender fingers a hand. The
head is lost in the shoulders and shows only in the attack pose. What names it (the horns, the claws, the spikes,
the hunched brute's back, the beast's legs with a high hock) is all there, but buried.

So the vendor mesh goes entirely: the demon is rebuilt from about a dozen simple faceted volumes on the vendor's
bones (the recipe above lists them), every edge that draws chosen. It is a 1.9 m walker in the key's deep blue
beside the cyan skeleton: the skeleton reads by its helmet, round shield and sword, the demon by its horns, its
spiked shoulders, its claws and its hunched, kite-shaped back.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game, near with the shatter, crowd), labelled A and B at
random, no docs, no recipe, no numbers, asked the skill's question.

1. A first rebuild (new mesh B): "B is clearly better. At the in-game size and in the crowd it reads as a hunched
   horned creature with a dark body and lit edges. A reads as a blue scribble." Against B: limbs "long narrow
   boxes" that read "like a skinny insect", "a lit blue patch on the back hump", claws as "loose hairlines". Done:
   limbs thickened, three thick claws a hand where there were four, and the trunk's broad flank facets (one turned
   edge-on to the camera on the hunched back and lit whole) replaced by narrow ones.
2. (New mesh B): "B is the better drawing ... A reads as frost or lichen." Against B: still a glowing patch on the
   upper back; from above "the torso is an oval with a spine line and a row of tiny bright ticks, like a zipper or a
   ladder", and "the horns flatten into a horizontal bar that looks like a hat brim". Done: the shoulders' slope to
   the neck flattened, the horns swept back along the skull instead of up and out, so they keep a V from above,
   and the three thin dorsal spikes made two broad fins.
3. (New mesh B): "B is clearly better. It reads as a low-poly model with closed facets and dark bodies at every
   size." Against B: from above "the torso reads as a barrel ... like a trash can or jetpack" (the flat top was a
   lid), "the head is a round dome with a crossed grid", hairline horns and spikes. Done: the back drawn as a muscled
   kite (spine ridge and a line over each shoulder blade) with no line round it; the head a wedge skull drawn by its
   crest, brow and cheekbones, with slanted eye facets; horns, spikes and claws thickened.
4. (New mesh A): "A is the better drawing. It reads as a low-poly neon wireframe: large closed facets over a dark
   body, a horned head and a readable pose. B reads as crackled ice or lace." Against A: a lighter crescent on the
   back, "a bright dotted line down the spine" (the fins seen edge-on), thin limbs, small spikes turning to
   scratches, horns as smooth arcs. Done: the fins made broad spikes, the forearms and shins thickened, two forearm
   fins made one blade, the horns' four segments drawn.
5. (New mesh A): "A is better, at the in-game size and in the crowd. It is the only one that reads as low-poly
   wireframe." Against A: "the torso, shoulders and upper arms are thin separate prisms with black gaps", "too many
   spikes", the back spikes as glyphs on "a beetle shell". Its note on the trailer's: "B's only win is bulk". Done: a
   wider trunk, heavier upper arms and a broad pauldron bridging trunk and arm; the forearm blades and knee spikes
   dropped; the pauldron spike and the claws made chunkier.
6. (New mesh B): "At game size and in the crowd, B wins clearly. Each demon has a distinct silhouette: horned head,
   long arms, claws, dark body." Against B: lit slivers along the top of the back, the dorsal spikes "a small floating
   zigzag of loose strokes" from above that also "cross over the head". Done: the dorsal spikes dropped (three
   critiques had flagged them from above), and the neck made a bull's, so the lines over the shoulders don't
   converge into a point.
7. The final mesh (new mesh A): "A reads better. It's the only one of the two that looks like a low-poly model ...
   The bull head and horns read at all three sizes, and in the crowd you can make out each figure's pose and
   outline." Its verdict: "Keep A's mesh". Against A, kept as still weak below: a light-blue crescent along the
   hunched back's top at near size, the kite's lines meeting at the neck seen from above, claws thin at far size,
   open arcs in the shatter. Of the trailer's: "every triangle is drawn, so the surface becomes a cracked-ice net".

### Still weak

- A soft light-blue crescent along the top outline of the hunched back (idle and walk), and a soft glow behind the
  head as it rears to strike, at near size; a few pixels at game size. Its cause is open: it didn't change when the
  trunk's and spikes' shading normals were leaned away from the rim, when the core glow was turned off (in a
  scratch render), or when the lines over the shoulder blades were moved in from the outline. Worth a look with the
  look's gains (`EdgeGains.Default`) rather than the mesh.
- Seen from directly behind, the back is a kite of lines meeting at the neck: a few critiques read it as a beetle's
  shell or a shield. It reads as a muscled back and spine at game size.
- The claws (three a hand, three a foot) are thin at far size, where every claw on any type is a stroke.
- The shatter's crack outlines leave a few open arcs and slivers, and the legs fly as a cluster for a few frames:
  the shard baker's, in both looks, as on the skeleton.
- The crowd cell shows a monster or two fewer than the trailer's: the review spaces the crowd by each mesh's
  measured radius, and the new mesh's spikes and pauldrons are wider. Not culling.

### Cost

948 triangles at every LOD (a mesh that flags its edges isn't decimated), fewer than every vendor LOD (5,992, 3,070
and 1,536), so the 1,000-monster crowd draws less than with the vendor's demon. Every corner is skinned to one bone,
or blended between two at a joint; the shapes follow the vendor rig and clips. No vendor geometry is kept: the
recipe places its shapes from the vendor's rest pose and skin weights, and its outputs stay in the ignored
Assets/Baked/Monsters/demon/.
