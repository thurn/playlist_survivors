# Monsters in the trailer's edge look (ps-6y2.28)

The neon edge trailer's drawing (`captures/trailer-action-edge.mp4`, demo3d's edge preset), ported by review from the
frozen look package: the bone-texture crowd, `StyleMesh.FeatureEdges` (creases over 24 degrees), `MonsterEdge.hlsl`
with its body and shard shaders and gains, and the crack-outlined GPU shatter, for the ten types on their vendor meshes
(plan.md, Rendering > The edge look). Judged by eye; no number here passes or fails anything.

- Each type's folder holds its review sheets (`champions monster review TYPE`): far, game and near sizes in idle, walk
  and attack with the shatter under them, a crowd over the grid, and `compare.jpg` (`--look trailer` left, the default
  right; they are the same drawing until a monster mesh or a tuning changes the default).
- [look/palette.jpg](look/palette.jpg): the `palette` still scene, minute 5's horde drawn by the crowd in the player
  (`champions stills --scene palette`; `--look trailer` renders it bit for bit the same). Beside it
  [trailer-18s](look/trailer-18s.jpg), [25s](look/trailer-25s.jpg), [45s](look/trailer-45s.jpg) and
  [52s](look/trailer-52s.jpg): frames of the trailer itself.

| Type | Sheets |
| --- | --- |
| skeleton | [game](skeleton/default-game.jpg), [crowd](skeleton/default-crowd.jpg), [compare](skeleton/compare.jpg) |
| slime | [game](slime/default-game.jpg), [crowd](slime/default-crowd.jpg), [compare](slime/compare.jpg) |
| bat | [game](bat/default-game.jpg), [crowd](bat/default-crowd.jpg), [compare](bat/compare.jpg) |
| demon | [game](demon/default-game.jpg), [crowd](demon/default-crowd.jpg), [compare](demon/compare.jpg) |
| goblin | [game](goblin/default-game.jpg), [crowd](goblin/default-crowd.jpg), [compare](goblin/compare.jpg) |
| mushroom | [game](mushroom/default-game.jpg), [crowd](mushroom/default-crowd.jpg), [compare](mushroom/compare.jpg) |
| bigOrk | [game](bigOrk/default-game.jpg), [crowd](bigOrk/default-crowd.jpg), [compare](bigOrk/compare.jpg) |
| troll | [game](troll/default-game.jpg), [crowd](troll/default-crowd.jpg), [compare](troll/compare.jpg) |
| fatOgre | [game](fatOgre/default-game.jpg), [crowd](fatOgre/default-crowd.jpg), [compare](fatOgre/compare.jpg) |
| dragon | [game](dragon/default-game.jpg), [crowd](dragon/default-crowd.jpg), [compare](dragon/compare.jpg) |

## What differs from the trailer, on purpose

- **The void toe.** The trailer's post (its neon-bloom preset) sank everything under a knee toward black, which is what
  made the glass cores black and the lines read as wire. The game's post has no toe, and adding one would crush the
  grid and the beat wave, so the monster shaders apply the trailer's toe to their own light (`EdgeToe`, knee 0.6, power
  3.2, pinned with the rest of `--look trailer`).
- **The beat.** The trailer lit every monster's core and lines at once on the beat; here the world's beat wave carries
  that flare and the hop out from the hero (design.md, The beat wave).
- **Weapons fly.** The trailer's kick gave the largest chunks half the smallest's push and slumped one in four, so a
  sword or spear hanging by the feet fell where it stood, upright. Chunks of what a monster carries (its weapon and
  shield: `carried` in Editor/Monsters/monsters.json, and every held weapon) are now flung like the struck part.
- No shadows, no fog (the game has neither); the review cells are crops of a 1080-row frame drawn at twice that and
  scaled down, as the trailer's capture was.

## Blind critiques

Two fresh agents, given only images and no docs: five trailer frames (A) beside this build's palette still and four
crowd cells (B), and a troll's near sheet with its shatter. Asked whether B's monsters are drawn in A's look.

The first (before the void toe; the review cells then rendered without supersampling) said **no**: "In A, every monster
is a neon drawing: one unbroken glowing outline around the body, a few interior crease lines, and a near-black body
inside. In B2-B5 and B1, at the game camera, each monster is a lit 3D body with grey or tinted shading." It also found
"the shards stay at full brightness and stop moving", and an empty cell drawn in a different black. In response: the
void toe (the bodies went black, as in the trailer), supersampled review cells, the shatter row drawn over the grid
(which hides the shards as they sink, as in play) and empty cells in the void's colour.

The second (as committed) still said **no** at the game camera, **yes** up close: "B6 at the closer camera does match A5:
thin lines and a dark core. So the look only survives up close and falls apart at game distance." Its first point:
"Fill instead of line. B3 (pink) is almost solid colour. B4 (blue) is a frosted fill." On colour: "B's pink leans
lavender, and B4's blue is pale and washed out next to A's saturated blue." On the shatter: "B6's death shatter looks
right: the fragments are line-drawn, it starts with a white flash and ends in specks."

What I found behind the remaining "no", side by side at the same pixel scale:

- **The tonemap.** The trailer's post tonemapped with a hue-preserving filmic curve (the brightest channel rolls off and
  the others scale with it). A levelled blue or magenta puts its strongest channel at 3 to 5 times white; the game's
  Neutral tonemap (and no tonemap at all, tried) clips or compresses each channel apart, so the hue drifts to pale
  periwinkle and lavender and the dense lines read as frosted fill. Porting the trailer's post (filmic, saturated halo,
  and the toe, which then leaves the monster shaders) changes the whole frame, so it is filed as its own bead.
- **The vendor meshes.** The goblins were filled pink shapes in the trailer too (design.md names them), and the demons'
  many small creases crowd at game scale. That is what the monster-mesh beads (ps-6y2.31-40) are for.
- Framing and density (the trailer's packed formations, its camera, its rings and debris) are the scenes', not the
  drawing's.

## Cost: 1,000 monsters on the M5 Max

`champions play --headless --fixed-time --crowd-bench 1000 --seconds 12 --bench-out ...` ([bench-1000.json](bench-1000.json)):
the eight mob types in equal shares walking toward the hero over the grid, 20 kills a second feeding the shatter, the
beat wave at 120 BPM, drawn by hand into 1080p and timed to the GPU's completion of each frame, alternating blocks with
and without the crowd. With the crowd 7.4 ms p50 and 8.1 ms p99, without 0.8 and 2.0 (a quieter run measured 6.5 and
0.7): about 6.5 ms of crowd per frame, an upper bound that includes the CPU's submission (1.3 ms). The vendor meshes
draw 1,400 to 19,000 triangles each at LOD0 (the game camera puts most of them there) plus the outline hull: far over
the low tier's 1.5 ms proxy budget, which ps-6y2.22 (performance) and the monster meshes have to bring down. Measured
while other sessions loaded the machine (load average 17).
