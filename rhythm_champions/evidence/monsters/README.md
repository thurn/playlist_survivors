# Monsters in the trailer's edge look (ps-6y2.28)

The neon edge trailer's drawing (`captures/trailer-action-edge.mp4`, demo3d's edge preset), ported by review from the
frozen look package: the bone-texture crowd, `StyleMesh.FeatureEdges` (creases over 24 degrees), `MonsterEdge.hlsl`
with its body and shard shaders and gains, and the crack-outlined GPU shatter, for the ten types on their vendor meshes
(plan.md, Rendering > The edge look). Judged by eye; no number here passes or fails anything.

- Each type's folder holds its review sheets (`champions monster review TYPE`): far, game and near sizes in idle, walk
  and attack with the shatter under them, a crowd over the grid, and `compare.jpg` (`--look trailer` left, the default
  right; they are the same drawing until a monster mesh or a tuning changes the default).
- [look/palette-1080.jpg](look/palette-1080.jpg) (and a [smaller copy](look/palette.jpg)): the `palette` still scene,
  minute 5's horde drawn by the crowd in the player at 1080 rows under the trailer's grade (`champions stills --scene
  palette`; `--look trailer` renders it bit for bit the same). Beside it
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

## The trailer as it was captured

The trailer (`captures/trailer-action-edge.mp4`, finished 2026-11-01 17:34) was drawn by the look package at git
138684f2. Three changes landed in the package within hours after it, and the first port took the frozen head with them:
"capture-true line widths" (the trailer's shader measured lines in the pixels it rendered, and the capture rendered
2160 rows and scaled them to 1080, so its 1.25-pixel lines were half a 1080-row pixel; measured in 1080-row pixels
they came out twice as wide, and the vendor meshes' dense creases merged into fills), a dark outline hull, and a
brighter rim on bodies small on screen. The port is now the trailer's version: lines measured in pixels at 2160 rows
(MonsterLines.hlsl, so a frame drawn at 1080 rows shows them as the trailer's frames do), no hull, one rim gain.

## The trailer's grade

The trailer's look is its shaders and its post. Its neon-bloom preset graded the scene before URP's post: an exposure
of -0.1, a void toe that sank everything under a knee toward black (what made the glass cores black), a wide saturated
halo, and a filmic tonemap that rolls off the brightest channel and scales the others with it, so a levelled blue or
magenta keeps its hue; URP's tonemapper was off. The game's post had bloom and a Neutral tonemap, which washed those
hues to periwinkle and lavender and left the faint rim as a glassy fill. That stage is now the game's post too
(`Render/World/Grade.cs`, `Resources/World/Grade.shader`; Settings/Post.asset: tonemapping off, bloom 0.63, contrast and
saturation +10, a vignette into the void). The floor undoes the grade (World.hlsl's `Ungraded`), so the grid keeps the
light the world was calibrated to; the effects and the hero were bright enough to pass through it as they were
(`champions stills --scene world --scene gallery`).

## What differs from the trailer, on purpose

- **The beat.** The trailer lit every monster's core and lines at once on the beat; here the world's beat wave carries
  that flare and the hop out from the hero (design.md, The beat wave).
- **Weapons fly.** The trailer's kick gave the largest chunks half the smallest's push and slumped one in four, so a
  sword or spear hanging by the feet fell where it stood, upright. Chunks of what a monster carries (its weapon and
  shield: `carried` in Editor/Monsters/monsters.json, and every held weapon) are now flung like the struck part.
- Not ported: the trailer preset's lens dirt (a generated smudge texture on the bloom at 6) and film grain (0.08),
  nor its post's lift of exposure and bloom on each beat.
- No shadows, no fog (the game has neither); the review cells are crops of a 1080-row frame drawn at twice that and
  scaled down, as the trailer's capture was (`--supersample 2`).

## Recipes choose their edges

A recipe (tools/recipes/monster/TYPE.py) may choose which edges draw: `mesh.edge_angle = 1` draws every edge but a flat
quad's diagonal (a true wireframe of a designed low-poly mesh, design.md, The aim), or `mesh.edges` flags each
triangle's edges. The vendor meshes and `--look trailer` keep the trailer's 24-degree creases. A mesh that chooses its
edges isn't decimated for distance. `MonsterLookTests` bakes a recipe's tetrahedron with one triangle's edges flagged:
the default look draws exactly those, the trailer the vendor slime.

## Blind critiques

Fresh agents, given only images and no docs: trailer frames (A) beside this build's palette still and crowd cells (B),
and a troll's near sheet with its shatter. Asked whether B's monsters are drawn in A's look.

1. Before the grade (no toe, no filmic, unsupersampled review cells): **no**. "In A, every monster is a neon drawing:
   one unbroken glowing outline around the body, a few interior crease lines, and a near-black body inside. In B2-B5
   and B1, at the game camera, each monster is a lit 3D body with grey or tinted shading." Also: "the shards stay at
   full brightness and stop moving", and an empty cell in a different black.
2. With the toe inside the monster shaders, supersampled cells and the shatter over the grid: still **no** at the game
   camera, **yes** up close: "B6 at the closer camera does match A5: thin lines and a dark core." Its first point:
   "Fill instead of line. B3 (pink) is almost solid colour. B4 (blue) is a frosted fill." On colour: "B's pink leans
   lavender, and B4's blue is pale and washed out next to A's saturated blue." That was the Neutral tonemap: the
   operator saw the same (lit glassy facet patches, blotchy lines, shields as shaded discs), and the trailer's grade
   above is the fix.
3. With the trailer's grade as the game's post, but the frozen head's line widths, hull and rim: **no for most types**,
   asked directly whether each reads as a neon wireframe. "Only B6 matches A ... B2 (the shield knights) comes near. B3,
   B4, B5 and most of B1 do not. They are filled, bright shapes, where A shows line drawings"; "B5, orange ogres: No,
   and it's backwards. The body is bright orange and the 'edges' are dark cracks cut into it". Side by side at the same
   pixel scale, the trailer's big orks were thin amber wire over dark bellies and ours merged into bands; halving the
   lines matched them, which led to the capture's own version above.
4. With the trailer as it was captured: **the same family, nearly the same look**, and up close the same ("A5 against
   the B6 poses" nearly match). Asked directly about the wireframe, it found ours reads as one more often than the
   trailer's own crowds: B5's orks "yes, the best: chunky closed facets, dark inside, a closed silhouette", B2's
   skeletons "yes, the cleanest", where A's crowds were "mostly no ... the orks are tinted lit masses". What it still
   saw differ: A glows more ("A's lines are thick and heavily bloomed, over a faintly tinted glass body"). Measured on
   orks at the same pixel scale, the line widths match; the trailer's frames have about twice as many dim glow pixels
   around their lines. Two causes, both known: the trailer's beat lit every monster's core at once (`_SL_Pulse`, so
   in any frame near a beat every belly glows), where here the beat wave lights each as it passes (design.md, The beat
   wave); and its preset's lens dirt (6, on the bloom) and film grain, which aren't ported. What it found weak on our
   side belongs to the meshes: the bats' open, jagged wing outlines, the slime's soft rim with no facets, the
   goblins' interior edges merging, the big orks' blades as ragged streaks, the skeletons' hairline spears, and shards
   that read as open curls up close.

## Cost: 1,000 monsters on the M5 Max

`champions play --headless --fixed-time --crowd-bench 1000 --seconds 12 --bench-out ...` ([bench-1000.json](bench-1000.json)):
the eight mob types in equal shares walking toward the hero over the grid, 20 kills a second feeding the shatter, the
beat wave at 120 BPM, drawn by hand into 1080p and timed to the GPU's completion of each frame, alternating blocks with
and without the crowd, under the trailer's grade. With the crowd 5.6 ms p50 and 6.4 ms p99, without 1.0 and 2.1: about
4.6 ms of crowd per frame, an upper bound that includes the CPU's submission (1.2 ms; with the later outline hull it
was about 6.3). The vendor meshes
draw 1,400 to 19,000 triangles each at LOD0 (the game camera puts most of them there): far over
the low tier's 1.5 ms proxy budget, which ps-6y2.22 (performance) and the monster meshes have to bring down. Measured
while other sessions loaded the machine (load average 5 to 17).
