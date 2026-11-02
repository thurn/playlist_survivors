# Edge cut: bigOrk

Written by `champions edgecut review bigOrk` from the recipe [tools/recipes/edgecut/bigOrk.py](../../../tools/recipes/edgecut/bigOrk.py) (role elite).

The big ork's edge cut (ps-6y2.11): Synty's Big Ork (Fantasy Rivals, 6,112 triangles in its active skin, a humanoid
rig) with the Slayer greatsword in its right fist, redrawn as long lines and a few strokes over a body of convex parts.

- Pose: the line choice sees it in its sword idle, not its T-pose, so the outline it picks is the one the game shows.
- Body: one hull per part, corners budgeted by how much of the outline the part makes; the big ears are cut out of
  the head's hull (they would fill it out to their tips) and drawn as strokes instead.
- Lines: the long outline-making creases (lines.choose), chains shorter than 7% of the height dropped, capped per part
  (the belt, the pauldrons, the helmet and top-knot); four contour strokes along each arm and leg, which the
  chunky low-poly limbs give no long crease for; the tusks and the ears as strokes; the greatsword as its handle,
  guard and the blade's two edges meeting at the tip.
- Clips: Synty's sword idle and masculine walk; the attack is the heavy stab, its wind-up the charge's telegraph and
  its thrust (the impact) the charge itself.
- Shards: a piece per part, the chest and the pelvis split in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 55% | 70.2% (poses 69%, 72%, 69%) | ok |
| line segments <= 220 | 133 | ok |
| body triangles 300-800 | 356 | ok |

20 shard pieces; the bone texture holds 42 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (74.7 px tall): [far.jpg](far.jpg)
- game (150.7 px tall): [game.jpg](game.jpg)
- near (301.3 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 57.4% | 58.7% | 1624 | 80 |
| far | walk | 60.7% | 61.1% | 1548 | 89 |
| far | attack | 55.6% | 57.2% | 1481 | 65 |
| game | idle | 69.2% | 70.7% | 5845 | 161 |
| game | walk | 72.4% | 73.2% | 5543 | 178 |
| game | attack | 69.1% | 70.2% | 5511 | 136 |
| near | idle | 83.0% | 83.4% | 21611 | 321 |
| near | walk | 85.1% | 85.3% | 20639 | 348 |
| near | attack | 82.5% | 83.1% | 21051 | 291 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 34 | 14 |
| chest | 38 | 10 |
| belly | 20 | 6 |
| pelvis | 30 | 10 |
| shoulder_l | 16 | 8 |
| shoulder_r | 22 | 2 |
| upperarm_l | 14 | 8 |
| upperarm_r | 14 | 7 |
| forearm_l | 16 | 8 |
| forearm_r | 18 | 6 |
| hand_l | 16 | 2 |
| hand_r | 16 | 2 |
| thigh_l | 18 | 8 |
| thigh_r | 14 | 8 |
| calf_l | 20 | 8 |
| calf_r | 20 | 8 |
| foot_l | 16 | 3 |
| foot_r | 14 | 3 |
| ears | 0 | 4 |
| tusks | 0 | 2 |
| weapon | 0 | 6 |
