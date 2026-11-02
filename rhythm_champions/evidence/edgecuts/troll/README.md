# Edge cut: troll

Written by `champions edgecut review troll` from the recipe [tools/recipes/edgecut/troll.py](../../../tools/recipes/edgecut/troll.py) (role elite).

The troll's edge cut (ps-6y2.11): Synty's Troll (Fantasy Rivals, 4,034 triangles in its active skin, a humanoid rig)
with its spiked club in its right fist, redrawn as long lines and a few strokes over a body of convex parts.

- Pose: the line choice sees it in its sword idle, not its T-pose, so the outline it picks is the one the game shows.
- Body: one hull per part, corners budgeted by how much of the outline the part makes. The club has none: a hull
  over its spikes would be a fat blob hiding its own outline.
- Lines: the long outline-making creases (lines.choose), chains shorter than 7% of the height dropped, capped per part;
  four contour strokes along each arm and leg, which the chunky low-poly limbs give no long crease for; the tusks as
  strokes; the club as its handle, its head's outline and a band round its widest.
- Clips: Synty's sword idle and masculine walk; the attack is the leaping slam, its wind-up and leap the slam's
  telegraph and its landing blow (the impact) the slam.
- Shards: a piece per part, the chest and the pelvis split in two; the club's strokes join the right hand's.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 55% | 71.1% (poses 70%, 72%, 71%) | ok |
| line segments <= 220 | 130 | ok |
| body triangles 300-800 | 362 | ok |

20 shard pieces; the bone texture holds 39 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (80.9 px tall): [far.jpg](far.jpg)
- game (163.2 px tall): [game.jpg](game.jpg)
- near (326.4 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 51.3% | 55.0% | 1855 | 69 |
| far | walk | 54.0% | 57.6% | 1791 | 76 |
| far | attack | 52.5% | 55.6% | 1945 | 84 |
| game | idle | 70.3% | 72.4% | 6678 | 138 |
| game | walk | 71.6% | 73.3% | 6477 | 150 |
| game | attack | 71.3% | 73.0% | 7009 | 170 |
| near | idle | 82.9% | 84.1% | 24540 | 273 |
| near | walk | 83.7% | 85.1% | 24029 | 298 |
| near | attack | 84.3% | 84.9% | 26243 | 340 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 34 | 14 |
| chest | 40 | 6 |
| belly | 20 | 6 |
| pelvis | 38 | 10 |
| shoulder_l | 14 | 1 |
| shoulder_r | 14 | 3 |
| upperarm_l | 16 | 8 |
| upperarm_r | 16 | 8 |
| forearm_l | 16 | 7 |
| forearm_r | 20 | 8 |
| hand_l | 16 | 2 |
| hand_r | 14 | 2 |
| thigh_l | 20 | 8 |
| thigh_r | 16 | 8 |
| calf_l | 20 | 8 |
| calf_r | 20 | 8 |
| foot_l | 14 | 3 |
| foot_r | 14 | 3 |
| tusks | 0 | 2 |
| club | 0 | 15 |
