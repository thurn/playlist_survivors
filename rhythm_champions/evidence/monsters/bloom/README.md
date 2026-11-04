# Monster: bloom

Written by `champions monster review bloom` and `champions monster review bloom --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/bloom.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, breath, smash, fireball, stomp, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, breath, smash, fireball, stomp, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 720/720/720 in the default look (the trailer's 2600/1686/896), 39 shards, 100 bones, standing 3.958 m.

## The recipe

The Bloom (ps-8z4.11), Boss Rush boss 3: the vendor's carnivorous plant rebuilt as a few simple faceted shapes,
every edge it designs drawn, around what names it: a flytrap of two toothed lobes, a ruff of big petals behind it,
a stem and eight roots.

The trailer's drawing of the vendor mesh (2,600 triangles) reads at once as a mouth on legs, but breaks the look: the
trap's upper lobe is a smooth dome whose rim lights as a soft gradient, with short stitches floating on its face; the
teeth are dozens of thin spikes that smear into a hatched band at game size; the roots and leaves are hatched with
chevrons. From the game's camera, in front and above, the trap faces the viewer, the ruff of leaves stands behind it
and the roots splay round. What changes:

- The trap: each lobe a clamshell whose ribs fan from a short hinge bar over a crown to the lip, as a flytrap's do,
  rigid on its jaw bone; one big three-sided tooth on each lip edge, splayed out over the rim so the toothed mouth
  shows from above (fewer, larger teeth: the first blind critique). Each lobe's mouth is a shallow funnel of its own,
  only its rim drawn, so the lip's fold doesn't average the shell's normals with the mouth's into a rim-lit glow. The
  upper lobe's hinge sits above its lip, so its back slopes gently rather than catching the rim edge-on. A small
  hinge knob joins the lobes to the stem on the head bone.
- The ruff: five big kite petals fanned round the back of the trap from the stem's top, a midrib splitting each in two
  facets, thin membranes whose outlines are bevelled (the dragon's wings), their normals leant halfway up so they stay
  dark. They replace the vendor's six curled leaves there (the theme's petals), short enough that the trap, not the
  ruff, leads (the second blind critique: two starbursts of equal weight read as an urchin).
- The stem: one five-sided prism through the neck's joints; the bulb a low eight-sided dome; the three leaves at the
  bulb kept as petals, the mid-stem leaves dropped as clutter under the trap.
- The roots: each one thin four-sided prism, a ridge on top, through every other bone to its point (thinner than the
  vendor's, so the body stands out in a crowd: the first blind critique), the chevrons gone.

The stem, bulb and roots lean their normals up 0.6 and the trap 0.4, so big facets turned away stay dark (leant
further, a lobe's shard turned over in the shatter drew solid). Every shape draws its polygons' outlines, never their
fan diagonals; of the petals' bevels only the outline draws. Shapes are skinned by transfer from the vendor's nearest
vertices over their own bones (never across roots), the lobes, knob and petals rigid. Joints are read from the
vendor's vertices weighted to both bones.

Source: Forest Creatures Pack's Carnivorous Plant-Red.prefab (sha256
29776245832e80d013091e867d34f8b54ae5a839f4ef6ba607ac4dd7d2ea38cb) and Carnivorous Plant.FBX
(8eba906925a91f84ee9208f4d28b045658a28b78323df7bfa6be303a7ef7cff7): the prefab and its clips are in manifest.json,
the mesh in the prefab's dependency closure. No Blender: numpy only. Bake: `just champions-tool monster bake bloom`;
review: `just champions-tool monster review bloom` (and `--look trailer`).

## Notes

ps-8z4.11 (Boss Rush boss 3, act 1, "Rotating spirals"), by the monster-mesh-design skill. The default look draws the
recipe's mesh: both blind critiques judged it closer to the look aimed for (closed facets, dark inside, a crisp
outline), and the vendor's drawing the quicker creature read; the recipe then took the critiques' notes toward that
read (a mouth that leads, fewer and bigger teeth, a smaller ruff). `--look trailer` is unchanged (the vendor mesh).

The source was registered by the Bloom's fight bead (ps-8z4.10), which landed first: the Red prefab and its clips
in manifest.json, the `bloom` type in monsters.json, its cast row in enemies.json and its colour in palette.json
(`enemies.accent.violet`). This bead used the same entries.

Its clips (monsters.json) are the boss animator's roles, so the fight's moves play them: breath (BreathAttack: the
spore arms), smash (Bite), fireball (SpellCast: the spore spiral), stomp (BreathAttackSurround: the pulse), walk (Walk
Forward WO Root: drawing its roots to its spot) and hit, the attacks' impacts at 0.6 s, the head's lunge. Measured here from the baked bone
texture (the midpoint of the two lobes' front lips) for comparison: the lunge peaks at 0.6 s in BreathAttack and
Bite, 0.567 in SpellCast and 0.433 in BreathAttackSurround; Bite's lobes snap shut fastest at 0.733. Left to the
fight bead to tune.

### The trailer's drawing of the vendor mesh

Not in the trailer; watched on these sheets. The vendor model (2,600 triangles) is a carnivorous plant: a flytrap
head of two lobes ringed with dozens of fangs, on an S-curved stem with leaves behind the head and at its foot, over
a bulb with eight long thorny roots splayed on the ground (the clips curl them like legs). From the game's camera the
trap faces the viewer, the ruff of leaves behind it. In the edge look:

- it reads at once as a mouth on legs (both critiques), the dark dome and the fang ring the clearest shapes;
- but the upper lobe is a smooth dome whose rim lights as a soft gradient, with short stitches floating on its face;
- the fangs smear into a hatched band at game size, and into a scribbled band in the crowd;
- the roots and leaves carry chevrons and fine grids that read as hatching, and the shatter is curved slivers.

### What was tried

Each by `champions monster review bloom`, with a numpy preview of the drawn edges at rest before it:

- The lobes first as stacked horizontal rings round a dome: from the camera a ringed barrel, a target from above.
  Rebuilt as clamshells whose ribs fan from the hinge to the lip, as a flytrap's do; then from a short hinge bar, as
  the ribs met in an umbrella's point from behind.
- The lobes' open mouths as one flat face sharing the lip with the shell: the lip folds nearly 180 degrees, so the
  smoothed normals there averaged to nothing and the lip glowed. The mouth became a shallow funnel of its own, its
  spokes hushed.
- The upper lobe's back sloped steeply down to a hinge below its lip and lit edge-on as a band at idle; with its hinge
  above the lip it faces the camera and stays dark.
- The vendor's thirteen leaves first as kites and fans on their own axes: a tangle of spiky stars behind the head.
  Replaced by a designed ruff of six, then five shorter petals (the second critique), and the three at the bulb.
- Normals leant up 0.4, then 0.9 (darkest, but a lobe's shard turned over lit solid in the shatter, and so did teeth
  given fixed normals), settled at 0.6 for the stem, bulb and roots and 0.4 for the trap.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game and near poses with the shatter, and the crowd),
labelled A and B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh A.) "A is closer to the style. B reads better as a creature, at game size and in the crowd." Of the new
   mesh: "the star crown and the spiked drum read as a flower or a sea urchin, not a mouth" (done: bigger single
   teeth splayed over the rim, a smaller ruff); solid wedges in the open-mouth pose and at leg joints (partly: the
   lip's fold and the upper lobe's back fixed; the Bite pose's open mouth still lights); legs as bright as the body,
   a lattice in the crowd (done: thinner roots); "the many small spikes on the drum rim become sawtooth fuzz" (done:
   one big tooth an edge). Of the vendor's: a smooth dome with floating stitches, fangs as a hatched band, chevron
   scratches.
2. (New mesh B, after the first round.) "B is closer to the target style, but A reads better as a creature at
   in-game size and in the crowd ... It is properly faceted: closed polygons, a dark interior and a crisp outline."
   Asked: the star crown and the spiky body are "two starbursts of equal weight" (done: five shorter petals); spike
   tips and the attack pose's mouth glow solid (teeth leant, not fixed; the Bite pose's mouth stays); legs too thin
   (done: thickened between the two critiques' asks). Of the vendor's: the dome "reads as a shaded sphere", the teeth
   ring "too dense", the leaves scratchy.

### What still looks weak

- It still reads less instantly as a mouth on legs than the vendor's dark dome: from the game camera the trap is a
  faceted shell with a toothed rim, and the ruff and the splayed roots compete with it.
- In Bite the trap gapes toward the camera and the upper lobe's inside and back light in the rim (the vendor's do too).
- A few root segments light as short bars where a clip bends them sharply.
- The shatter's last frames keep a few lit facets on the lobes' shards.
