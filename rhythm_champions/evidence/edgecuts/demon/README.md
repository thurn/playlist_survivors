# Edge cut: demon

Written by `champions edgecut review demon` from the recipe [tools/recipes/edgecut/demon.py](../../../tools/recipes/edgecut/demon.py) (role fodder).

The demon's edge cut (ps-6y2.10): Magic Pig's Demons LP (5,992 triangles in its prefab's active renderers, 59
entries) redrawn as a few dozen lines over a body of convex parts.

- Body: one hull per part, corners budgeted by how much of the outline the part makes at game scale (the chest,
  head and long arms most). The armour lies inside its part's hull.
- Lines: the outline-making creases of the long arms and a few of the chest, pelvis and calves (lines.choose),
  chains shorter than 6% of the height dropped. The demon's name is its horns and claws: the two tall pairs of
  horns on the crown are strokes along their spines (the small face horns go), and the fingers have no body, only a
  stroke from the palm to each of the three long claws (the thumb goes).
- Shards: a piece per part, the chest in three, the belly, the pelvis and the forearms in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 65% | 79.1% (poses 78%, 79%, 80%) | ok |
| line segments <= 120 | 44 | ok |
| body triangles 300-800 | 308 | ok |

22 shard pieces; the bone texture holds 46 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one size on screen, the monster's standing height in pixels at 1080 rows, in cells twice that square and at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (56 px): [far.jpg](far.jpg)
- game (113 px): [game.jpg](game.jpg)
- near (226 px): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 61.8% | 63.7% | 408 | 28 |
| far | walk | 65.7% | 66.5% | 388 | 29 |
| far | attack | 63.7% | 65.4% | 433 | 30 |
| game | idle | 78.3% | 79.2% | 1528 | 55 |
| game | walk | 79.3% | 79.7% | 1458 | 58 |
| game | attack | 79.8% | 79.9% | 1590 | 63 |
| near | idle | 87.6% | 88.0% | 5724 | 110 |
| near | walk | 88.7% | 89.0% | 5545 | 114 |
| near | attack | 88.9% | 89.0% | 6023 | 128 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 30 | 0 |
| chest | 36 | 4 |
| belly | 20 | 0 |
| pelvis | 24 | 2 |
| upperarm_l | 18 | 5 |
| upperarm_r | 18 | 5 |
| forearm_l | 20 | 5 |
| forearm_r | 20 | 5 |
| hand_l | 20 | 0 |
| hand_r | 20 | 0 |
| thigh_l | 12 | 0 |
| thigh_r | 10 | 0 |
| calf_l | 14 | 2 |
| calf_r | 16 | 2 |
| foot_l | 16 | 0 |
| foot_r | 14 | 0 |
| horns | 0 | 8 |
| claws_l | 0 | 3 |
| claws_r | 0 | 3 |
