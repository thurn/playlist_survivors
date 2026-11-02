# Edge cut: skeleton

Written by `champions edgecut review skeleton` from the recipe [tools/recipes/edgecut/skeleton.py](../../../tools/recipes/edgecut/skeleton.py) (role fodder).

The skeleton's edge cut, the reference cut (ps-6y2.8): Magic Pig's Skeleton Warrior LP (9,310 triangles, 120
entries) redrawn as about seventy lines over a body of convex parts.

- Body: one hull per part, its corners budgeted by how much of the outline the part makes at game scale (the
  chest and head most, fingers nothing: a hand is one small hull). The shield is a flat disc.
- Lines: the outline-making creases of the armour and bones (lines.choose), chains shorter than 6% of the height
  dropped; the helmet's kept, the torso's capped, the hands' and feet's given little (the ribs lie under the
  breastplate and show none); the shield an empty circle; the sword two strokes (pommel to tip, the guard).
  Fewer lines than the 120 allowed: at game scale each line's light spreads over a pixel or two of the dark body
  round it, and the black share needs that body.
- Shards: a piece per part, the chest, the pelvis and the shield split in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 65% | 66.5% (poses 66%, 72%, 62%) | ok |
| line segments <= 120 | 72 | ok |
| body triangles 300-800 | 322 | ok |

23 shard pieces; the bone texture holds 61 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (56 px tall): [far.jpg](far.jpg)
- game (113 px tall): [game.jpg](game.jpg)
- near (226 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 44.2% | 49.3% | 635 | 41 |
| far | walk | 54.2% | 55.9% | 612 | 42 |
| far | attack | 36.4% | 42.8% | 418 | 34 |
| game | idle | 66.0% | 67.3% | 2323 | 83 |
| game | walk | 71.8% | 73.1% | 2296 | 85 |
| game | attack | 61.7% | 65.7% | 1557 | 67 |
| near | idle | 80.9% | 81.3% | 8734 | 169 |
| near | walk | 84.5% | 85.1% | 8697 | 170 |
| near | attack | 77.9% | 79.5% | 5950 | 136 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 26 | 6 |
| chest | 38 | 9 |
| ribs | 16 | 0 |
| spine | 10 | 0 |
| pelvis | 24 | 8 |
| pauldron_l | 18 | 2 |
| pauldron_r | 20 | 2 |
| upperarm_l | 12 | 2 |
| upperarm_r | 6 | 2 |
| forearm_l | 16 | 4 |
| forearm_r | 16 | 2 |
| hand_l | 12 | 0 |
| hand_r | 12 | 0 |
| thigh_l | 12 | 2 |
| thigh_r | 8 | 1 |
| calf_l | 14 | 4 |
| calf_r | 16 | 6 |
| foot_l | 16 | 4 |
| foot_r | 14 | 4 |
| shield | 16 | 12 |
| sword | 0 | 2 |
