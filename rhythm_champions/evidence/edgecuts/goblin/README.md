# Edge cut: goblin

Written by `champions edgecut review goblin` from the recipe [tools/recipes/edgecut/goblin.py](../../../tools/recipes/edgecut/goblin.py) (role fodder).

The goblin's edge cut (ps-6y2.10): Magic Pig's Goblins LP (12,457 triangles in its prefab's active renderers, 83
entries) redrawn as a few dozen lines over a body of convex parts.

- Body: one hull per part, corners budgeted by how much of the outline the part makes at game scale; the big ears
  their own parts. The helmet, eyes, teeth and rings lie inside their part's hull. The shield is a flat disc.
- Lines: the outline-making creases (lines.choose), chains shorter than 6% of the height dropped; the head's and
  ears' kept, the torso's capped; the shield an empty circle; the sword two strokes (pommel to tip, the guard).
- Shards: a piece per part, the chest, the pelvis and the shield split in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 65% | 65.7% (poses 67%, 68%, 62%) | ok |
| line segments <= 120 | 32 | ok |
| body triangles 300-800 | 314 | ok |

22 shard pieces; the bone texture holds 58 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density, three poses to a row (idle, walk, attack), each the original then the cut, and a last row of the shatter: the first pose broken by a blow away from the camera, then 0.08, 0.2, 0.4, 0.8 and 1.4 s after (as many as the row holds). Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (37.3 px tall): [far.jpg](far.jpg)
- game (75.3 px tall): [game.jpg](game.jpg)
- near (150.7 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 52.9% | 55.8% | 278 | 29 |
| far | walk | 50.5% | 54.7% | 285 | 31 |
| far | attack | 44.7% | 49.8% | 235 | 35 |
| game | idle | 67.1% | 71.2% | 978 | 56 |
| game | walk | 67.8% | 70.3% | 968 | 62 |
| game | attack | 62.3% | 67.1% | 820 | 71 |
| near | idle | 81.4% | 83.9% | 3637 | 112 |
| near | walk | 81.0% | 82.7% | 3512 | 123 |
| near | attack | 78.0% | 80.5% | 3104 | 144 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 34 | 4 |
| chest | 32 | 2 |
| belly | 16 | 0 |
| pelvis | 26 | 0 |
| pauldron_l | 14 | 2 |
| pauldron_r | 14 | 2 |
| upperarm_l | 12 | 0 |
| upperarm_r | 10 | 0 |
| forearm_l | 12 | 2 |
| forearm_r | 14 | 2 |
| hand_l | 16 | 0 |
| hand_r | 14 | 0 |
| thigh_l | 10 | 0 |
| thigh_r | 12 | 0 |
| calf_l | 16 | 0 |
| calf_r | 14 | 0 |
| foot_l | 16 | 0 |
| foot_r | 16 | 0 |
| shield | 16 | 12 |
| ears | 0 | 4 |
| sword | 0 | 2 |
