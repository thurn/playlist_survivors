# Monster: bigOrk

Written by `champions monster review bigOrk` and `champions monster review bigOrk --look trailer` (plan.md,
Rendering > The edge look). The sheets are for judging by eye; nothing here passes or fails.

- The default look draws its recipe, tools/recipes/monster/bigOrk.py; `--look trailer` draws the vendor mesh exactly as the neon edge trailer did.
- The camera is the game's (60 degrees down, 23 m, 40 degree field), each cell at a 1080-row frame's pixel density, the type in its palette colour with the game's post.
- `default-far.jpg`, `default-game.jpg`, `default-near.jpg`: the default look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `default-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `trailer-far.jpg`, `trailer-game.jpg`, `trailer-near.jpg`: the trailer look at twice, once and half the game camera's distance, the poses idle, walk, attack, then the shatter 0, 0.08, 0.2, 0.4, 0.9, 1.6 s after a blow away from the camera.
- `trailer-crowd.jpg`: a few dozen walking over the grid at the game camera (a 960 by 540 crop).
- `compare.jpg`: `--look trailer` on the left, the default on the right, the game-size poses over the crowd.

Numbers (diagnostics only): LOD triangles 1060/1060/1060 in the default look (the trailer's 6812/3455/1728), 32 shards, 50 bones, standing 2.205 m.

## The recipe

The big ork (ps-6y2.37): the vendor's armoured brute and its greatsword rebuilt whole as a few simple faceted volumes.

The trailer's drawing of the vendor mesh reads by its bulk, a hunched brute with long arms and a long sword, and up
close as an armoured warrior with every plate outlined. But its 6,112-triangle body lights so many creases that it is
never dark inside: bands round the arms and legs, a studded belt with a round buckle drawn as a spider's web, fang
charms, wrappings and a pauldron of small plates. At game size it is a striped orange clump and in a crowd the figures
tangle together; its 700-triangle sword hangs edge-on as a jagged streak whose outline breaks into dashes. What names
the big ork (the hunched bulk, the long heavy arms, the head slung low with tusks and pointed ears, one pauldron, the
strap across the chest and the greatsword) is in it but buried. So nothing of the vendor mesh is kept; every part is
new, skinned to the vendor bones, and every edge that draws is chosen here (each polygon's edges, never its
triangles' diagonals, and only some of those):

- The trunk: a barrel from the crotch to a thick neck, a gut over the belt and high shoulders, drawn by eight long
  lines and no line round it; a belt; a strap over the left shoulder and across the chest and back to the right hip.
- The head, a quarter larger than the vendor's skull and slung low between the shoulders: one wedge from the back of
  the skull to a boar's snout with one line across it at the brow dipping in a scowl, slanted eyes, a jaw wider than
  the snout jutting past it, two thick tusks hooking out of its corners and pointed ears flat as leaves. Its first
  builds read as a helmet's visor from above (a crest, and a straight line round the brow), then as a blank dome.
- Arms and legs: six-sided rings blended between the bones at each joint, heavy upper arms and forearms, one line
  round each limb at the elbow or knee; big fists, each one block of palm and fingers and a thumb; blocky boots.
  The vendor's bracers, greaves, knee pads and slender fingers are dropped (cuffs, plates and banded fists made it
  a robot's).
- One pauldron of three lames over the left shoulder, laid flat (upright, it read as a backpack); a loincloth.
- The greatsword: a cleaver with two notches in its back, a crossguard and a grip, a sixth shorter and narrower
  than the vendor's; turned a quarter on its grip so its flat faces the camera as it hangs (edge-on it drew as a
  bright bar), its flats unfolded (a folded flat caught the rim and glowed). Carried: it shatters with the body.

Every triangle has its own corners and its facet's normal leant up its shape toward its root (up the trunk and the
legs, up each arm toward the shoulder): the rim mixes the normal with the facet's own, and unleant, the long facets
of the round trunk and the hanging arms lit whole as gradient bands. A plate's underside keeps its own normal. The
mesh flags its edges, so it is drawn as designed at every distance rather than decimated: 1,060 triangles at every
LOD, where the vendor's are 6,812, 3,455 and 1,728.

Source: Synty's Character_BR_BigOrk_01.prefab and SM_Wep_Slayer_01.prefab from POLYGON Fantasy Rivals (package sha256
f5b2197465eb4fcd0ce06cec1faa2790523a8a1ae8a669c1ef9a0b0316919e6c), as manifest.json lists them. Only its rig is used,
and its rest pose and skin weights to place the shapes. No Blender: numpy only. Bake: `just champions-tool monster
bake bigOrk`; review: `just champions-tool monster review bigOrk` (and `--look trailer`).

## Notes

ps-6y2.37, by the monster-mesh-design skill. The default look draws the recipe's mesh: seven blind critiques, each
preferring it to the trailer's drawing at the in-game size and in the crowd. `--look trailer` is unchanged (the
vendor mesh).

### The trailer's drawing of the vendor mesh

Watched in the trailer (`captures/trailer-action-edge.mp4`, the orange crowd at 20-26 s; the orange brute at the
right of `docs/champions/reference/opening-duel.jpg`) and on these sheets (`trailer-*.jpg`): the big ork reads by its
bulk, a hunched brute with long arms and a long sword, and up close as an armoured warrior, every plate outlined. But
its 6,112-triangle body lights so many creases that it is never dark inside: bands round the arms and legs (bracers,
greaves, wrappings), the belt's studs, its round buckle drawn as a spider's web, fang charms, a strap and a pauldron of
small plates. At game size it is a striped orange clump, in the crowd the figures tangle together, and the sword (700
triangles) hangs edge-on as a long jagged streak whose outline breaks into dashes.

What names it: the hunched bulk, the long heavy arms, the head slung low with tusks and pointed ears, one heavy
pauldron on its left shoulder, the strap across the chest and the greatsword. The sibling troll shares its amber to
orange family; the ork's mark is the cleaver.

### Blind critiques

Fresh agents given only the sheets of both looks (far, game, near with the shatter, the near poses at native size,
crowd), labelled A and B at random, no docs, no recipe, no numbers, asked the skill's question.

1. (New mesh A, its first build): "A is clearly better at the in-game size and in the crowd ... It reads as an
   armoured knight or mech brute, not an ork." Of the trailer's: "an orange scribbled clump, and in the crowd the
   figures merge into noise". Against A: "The head is a helmet with a visor", "the rounded torso fills with orange
   gradient bands", hairline spikes, finger scratches. Done: an ork's head (a brow, a snout, a jaw wider than the
   skull, thick tusks, ears; the topknot, read as a plume, dropped), one fist block a hand, and every shape's normals
   leant up its limb, which darkened the trunk and the hanging arms; the bracers and knee plates dropped (cuffs).
2. (New mesh A): "A is clearly better, both at game size and in the crowd ... more like a mech or a helmeted golem
   than an orc." Against A: "From above it's a box helmet with a horizontal visor slot"; the pauldron "reads as a
   backpack"; "The sword over-glows ... a solid bright bar". Done: the separate brow bar dropped, the jaw pushed
   wider and further forward with the tusks hooking out past it, the lames laid flat, the blade's folded flats made
   flat and the blade turned on its grip so its flat faces the camera as it hangs: an outlined dark cleaver.
3. (New mesh B): "B is clearly better at game size and in the crowd ... a hunched brute (orc or ogre, slightly golem
   or gorilla) with long arms and a broken cleaver. The horns help in the crowd." Against B: the loincloth "a solid
   orange wedge", the head still "a dome with a box visor", the arms "stacks of ring segments". Done: the head made
   one wedge drawn along its sides only, the loincloth's normals leant, the rings at the arms' ends dropped.
4. (New mesh A): "A wins clearly, both at in-game size and in the crowd ... a hulking horned brute or armoured ogre
   with a cleaver." Of the trailer's: "up close, a tusked, snouted, armoured beast-man, like a boar-orc. At in-game
   size it's an orange bundle with a stick." Against A: "The head is a blank dark dome with horns. From above there's
   no jaw, tusks or brow"; arms "chopped into blocks". Done: the head a quarter larger with one line across it at the
   brow, dipping in a scowl; the elbow ring dropped; the fists a quarter larger. Ears out to the sides were tried and
   dropped: they sank into the shoulders' lines.
5. (New mesh B): "B is clearly better, both at the in-game size and in the crowd ... A horned orc or demon brute with
   a cleaver." Against B: limbs "drawn as parallel side lines without end caps, so they read as ladders"; "The sword
   is as big as the body". Done: the line round the elbow restored (one ring, between critique 4's blocks and this
   ladder), the blade a sixth shorter and narrower. A plate's underside now keeps its own normal, as the goblin's
   shield taught, so no shard of a leant plate draws solid when it turns over.
6. (New mesh B): "B is clearly better. It hits the look and A doesn't ... a hunched horned brute: an ogre, a
   minotaur, or an orc in a horned helmet." Against B: "Behind the sword hilt ... a pile of short crossing lines";
   one ear reads as "one tall spike"; "the zigzag notches turn into scratches at game size". Done: the pommel dropped
   and the grip ended just past the fist, the two notches cut deeper, the ears made broader leaves.
7. The final sheets bar the last pass below (new mesh A): "A is the retro 3D neon wireframe look and B is not. A
   reads better at game size and in the crowd ... It reads as a horned brute or troll, or a blocky rock golem.
   Nothing says 'ork': it has no tusks, no jaw, and the eyes disappear at game size." Of the trailer's: "an orange
   scribble with solid lit patches on the shoulders and around the belly ... The grille on the head reads as a
   knight's visor or a robot face". Against A: "Limbs are stacks of short boxes. Each forearm and shin has 3-4
   bands"; the attack's blade "lights as a solid brown-orange slab"; one ear seen as "a single unicorn spike". Done
   after it, checked by eye rather than critiqued again: the fist one block from the wrist to the fingertips drawn
   round only at its ends (a closed palm and a closed finger block banded the forearm), the legs open into the trunk
   and the boots, and the boots drawn round only at the heel and toe.

### Still weak

- The ork's identity at game size. Every critique preferred the new mesh, but read it as a horned brute (ork, ogre,
  troll, minotaur, golem) more than an ork: its tusks and jaw read up close and from the front, not from above at game
  size, and its ears, swept back, stand up as horns once the hunch pitches the head forward. Ears out to the sides
  sank into the shoulders' lines, and bigger tusks turn into a second pair of horns. The trailer's drawing reads no
  more as an ork (an armoured knight or mech, critiques 6 and 7), but the troll's mesh should keep apart from it by
  shape, and the demon's horns are its mark.
- In the attack, the blade and forearm pass in front of the belly and fill a dull red: the inner core's glow, seen
  through whatever is in front of the body (its sphere comes from the clip's bounds, which include the sword's
  reach), and the blade's flat turned part-way to the camera. Brief, and the same in both looks.
- Seen from the front as the ork faces the camera, the trunk hides behind the big dark head and arms (critique 5:
  "a dark hole between head and shoulders"); a ring round the shoulders was not tried, for the hoops it draws from
  above (the demon's lesson).
- A few shards draw as open crescents (the belt and the strap) or turn dull orange mid-burst, and the blade's tip
  lies apart from the burst for a few frames: the shard baker's, as on the other types.

### Cost

1,060 triangles at every LOD (a mesh that flags its edges isn't decimated), against the vendor's 6,812, 3,455 and
1,728, so the 1,000-monster crowd draws less than with the vendor's big ork. Every corner is skinned to one bone, or
blended between two at a joint (the strap's by height between the trunk's rings); the shapes follow the vendor rig
and clips, and the sword is carried, so it shatters with the body. It stands 2.205 m in the review's idle to the
vendor's 2.255 m (the head slung lower). No vendor geometry is kept: the recipe places its shapes from the vendor's
rest pose and skin weights, and its outputs stay in the ignored Assets/Baked/Monsters/bigOrk/.

The review's sheets now stay full size up to 5,600 px wide (MonsterReview.MaxSheetPx, 4,800 before): the big ork's
reach makes its cells wide, and its near sheets were saved at half size, no sharper than the game sheet.
