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
| black share at game scale >= 65% | 77.9% (poses 79%, 78%, 76%) | ok |
| line segments <= 120 | 32 | ok |
| body triangles 300-800 | 314 | ok |

22 shard pieces; the bone texture holds 58 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one size on screen, the monster's standing height in pixels at 1080 rows, in cells twice that square and at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (56 px): [far.jpg](far.jpg)
- game (113 px): [game.jpg](game.jpg)
- near (226 px): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 64.6% | 65.7% | 537 | 41 |
| far | walk | 64.3% | 64.3% | 538 | 45 |
| far | attack | 59.5% | 59.9% | 449 | 51 |
| game | idle | 79.1% | 79.6% | 1969 | 82 |
| game | walk | 78.5% | 78.9% | 1907 | 89 |
| game | attack | 76.2% | 76.7% | 1674 | 104 |
| near | idle | 88.5% | 88.7% | 7426 | 165 |
| near | walk | 87.9% | 88.2% | 7139 | 180 |
| near | attack | 86.4% | 86.5% | 6391 | 213 |

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
