# Monster: bandagedKing

Written by `champions monster review bandagedKing` and `champions monster review bandagedKing --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/bandagedKing.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, smash, tail, attack, stomp, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, smash, tail, attack, stomp, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 674/674/674 in the default look (the trailer's 10964/5758/2878), 38 shards, 68 bones, standing 3.614 m.

## The recipe

The Bandaged King (ps-8z4.20), Boss Rush boss 7, act 2: the vendor's mummy rebuilt whole as a few simple faceted
volumes, every edge it designs drawn, around what names it: a tall gaunt figure wound in slanting bands of bandage,
crowned with a pharaoh's headcloth (the nemes) and false beard, a broad royal collar over its shoulders, and the two
long bandage whips it lashes with.

The trailer's drawing of the vendor mesh (10,964 triangles) reads as a gaunt zombie traced in short dashes: its
sculpted ribs, sinews and knuckles break the body into open loops that never close or go dark, the face is a scribble,
and nothing on it says king. Its whips, a flat ribbon on eight whip bones the clips coil into the hand at rest, draw as
one long open line. So nothing of the vendor mesh is kept; every part is new, skinned to the vendor bones, and every
edge that draws is chosen here (a polygon's outline, never its triangles' diagonals, and only some of those):

- The trunk: one eight-sided loft from the hips through a narrow waist to a deep chest and square shoulders, wound in
  three broad bands: the rings drawn round it slant one way and the next, and only its two sides run along it.
- The limbs: each one four-sided prism through all its joints (shoulder, elbow, wrist; hip, knee, ankle), every long
  line drawn so each wrap meets them, and one slanting ring round each segment, the next slanting the other way:
  bandage wound round it. Six-sided limbs with two of their lines drawn left the wraps as unconnected dashes in the
  crowd; two wraps a segment were a scribble. Mitten hands and wedge feet, broad enough to anchor the gait.
- The head: a narrow face proud of the hood, a ridge down the nose, two slanted eye slits; the nemes over it, a ridged
  roof from the brow band (across the forehead and temples only: round the back too, nested in the hem from above, it
  read as a donut) flaring behind the ears to a broad hem, with two lappets falling in front of the shoulders and a
  cobra rearing at the brow; a false beard on the jaw bone. A flat-topped nemes drew as a box helmet, and at 1.2 times
  it hid the body from the 60-degree camera; this one is the vendor's size.
- The collar: a shallow cone of a plate round the neck, deeper in front; its rim and one band inside draw.
- The whips: each a flat strap through the vendor's eight whip bones, rigid on each, narrowing to a short point, every
  other ring drawn so it reads as a closed ribbon of bandage panels (a thin strap read as a stray scratch). They vanish
  into the hand while the clips coil the whip bones there and lash out with the attacks.

The trunk, legs and collar lean their normals a little (0.4) up, which keeps their long facets dark in the rim; the
head and arms keep their own (the clips raise the arms and tip the head). The mesh flags its edges, so it is drawn as
designed at every distance rather than decimated: 674 triangles at every LOD.

Source: Heroic Fantasy Creatures Full Pack Vol 1's Mummy.prefab (sha256
aa452be5f8ed513402a7cd1b5bd5f319c44484f82cae22894d46b27a41a17ef8) and SK_Mummy.FBX
(6468762111a06fc11258b047bcf56f7350f23230e01eb2ea23fbaf3956320ea1), as manifest.json lists them. Only its rig is used,
and its rest pose and skin weights to place and skin the shapes: each shape takes the weights of the vendor's nearest
vertices over its own bones (never across limbs or sides); the face and nemes are rigid on the head bone (the lappets
bend with the neck), the beard on the jaw bone, each whip ring on its own whip bone. No Blender: numpy only. Bake: `just champions-tool monster bake
bandagedKing`; review: `just champions-tool monster review bandagedKing` (and `--look trailer`).

## Notes

ps-8z4.20, by the monster-mesh-design skill. The default look draws the recipe's mesh: two blind critiques, each
preferring it to the trailer's drawing at the in-game size and in the crowd. `--look trailer` is unchanged (the vendor
mesh).

This bead registered the source (the fight bead, ps-8z4.19, was running alongside and hadn't): the Mummy prefab,
SK_Mummy.FBX and sixteen of its clip FBXs in manifest.json (`champions manifest add more_assets ...`), the
`bandagedKing` type in monsters.json, its boss row in enemies.json (slot `keyLight`) and its cyan in palette.json
(lapis against act 2's amber sand). `heightM` is 5.0 because the vendor's rest pose includes the whips hanging below
the feet: at 5.0 the king stands 3.6 m (at 4.0, 2.75 m, smaller than Moonhowl). Clip roles and impacts, measured from
the whip tips and hands in the baked bone texture (the clips' tip reaching its lowest point in front):

- `smash`: BandageWhipAttack3Forward, both whips overhead to the ground about 7.5 m in front, impact 0.867 s.
- `tail`: BandageWhipAttack1Forward, the right whip swept round from right to left and behind, lowest at 0.533 s.
- `stomp`: BandageWhipAttack2Forward, the left whip swept level about 2.5 m up across the front, 0.333 s.
- `attack`: BandageWhip3HitComboForward, two sweeps (0.47, 0.93 s) then both whips slammed forward about 7.7 m,
  impact 2.0 s (the slam).
- `throw`: BandageWhip2HitComboForward, two sweeps, the first at 0.467 s.
- `roar`: Awakening (6.33 s: it lies on the ground until about 3.4 s, then rises), `hit`: GetHitLight1.
- `walk`: strideM 3.0 (the feet travel about 1.5 m a step).

The whips are the vendor's eight whip bones a side; every clip coils them into the hand at rest, so the whips exist
only while it lashes. The fight bead should keep its own registration where it differs.

### The trailer's drawing of the vendor mesh

On these sheets (`trailer-*.jpg`). The vendor mummy is a gaunt, slightly crouched humanoid with a bald round head, an
open jaw, and a flat bandage ribbon in each hand: 10,964 triangles of sculpted ribs, sinews and knuckles. So:

- at game size it reads as a thin human figure, but every rib and sinew draws a short broken dash: it is never dark
  inside, the face is a scribble, and nothing on it says mummy, let alone king;
- in the crowd the figures read as X-ray sketches of people;
- the whips draw as one long open line from the hand; the shatter throws curled crumbs.

What names it: the bandage (wrapping and whips) and, for the Bandaged King, a pharaoh's regalia, which the vendor
lacks. Nothing in the boss roster shares its cyan; among the humanoids it is told apart from Moonhowl (hunched, a
wolf's head) and the Rootwarden (a tree) by its upright wrapped figure, its pointed headcloth and its whips.

### What was tried

Each by `champions monster review bandagedKing --out <scratch>` (about 20 s), judged at game size, in the crowd and up
close, with a numpy previewer of the drawn edges at rest (the 60-degree camera from four sides, then front and side)
before each:

- The first nemes, 1.2 times the vendor's head with a flat top and a hem as wide as the shoulders, covered the body
  from the camera: a hooded jellyfish. At the vendor's size it still drew as a box helmet; flared to a hem behind the
  ears, with lappets in front of the shoulders and a ridge over the top, it reads as a pointed hood.
- Two wraps a limb segment made every limb a zigzag scribble in the crowd; one a segment reads as bandage.
- Six-sided limbs with two long lines drawn left the wraps as short unconnected dashes once the limb turned
  (critique 1): four-sided limbs with all four lines drawn close every wrap.
- A three-sided whip, then a thin flat strap with no rings, drew as one stray open line (critiques 1, 2): a wider
  strap with every other ring drawn reads as a closed ribbon.
- From above, the brow band round the back nested inside the hem as a donut (critique 1): it runs across the forehead
  and temples only. The face sat behind the hood's front from above (critique 2): it stands further forward.
- The hands and feet were thin stubs (critiques 1, 2): broader mittens and wedges.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near with the shatter, and the crowd), labelled A and
B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh A.) "A is much closer to the target style." Of the trailer's: "a smooth, sculpted human body traced in
   many short dashes ... an emaciated zombie or mummy"; of the new: "a hunched, hooded brute or ghoul", reading better
   in the crowd as "solid figures with clear heads and boots". Asked: unconnected creases on the torso and legs, an
   ambiguous head (a ring from above), thin arms, an open double-line whip. Done: four-sided limbs with every line
   drawn, the ridged nemes and partial brow band, thicker forearms and hands, ringed whips.
2. (New mesh B.) "B is the better drawing. It reads as a low-poly 3D model." Of the new: "a hooded, wrapped mummy or
   monk. The big pointed hood and the diagonal bands across the body read as wrappings"; of the trailer's: "a gaunt
   zombie or ghoul, almost a flayed anatomy figure". Asked: the whips still read as thin scratches, a faceless hood from
   above, tangled facets in the swing poses, stubby hands and feet, a short shatter. Done: wider ringed whips, the face
   brought forward, broader hands and feet. Kept: the swing poses (the clips cross the arms over the body) and the
   shatter (the shared shatter, as for every type).

### What still looks weak

- In the idle the clip bows the head, so from the game camera the head is mostly the nemes' roof; the face shows when
  it straightens to attack and walk.
- In the swings the arms cross the body and the facets overlap into a knot for a few frames.
- The whips are drawn by the clips: in the long lashes they bend at the whip bones into straight panels, a jointed
  strap rather than a curve.
