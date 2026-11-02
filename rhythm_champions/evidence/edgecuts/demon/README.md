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
| black share at game scale >= 65% | 75.6% (poses 73%, 76%, 77%) | ok |
| line segments <= 120 | 44 | ok |
| body triangles 300-800 | 308 | ok |

22 shard pieces; the bone texture holds 46 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density, three poses to a row (idle, walk, attack), each the original then the cut, and a last row of the shatter: the first pose broken by a blow away from the camera, then 0.08, 0.2, 0.4, 0.8 and 1.4 s after (as many as the row holds). Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (59.1 px tall): [far.jpg](far.jpg)
- game (119.3 px tall): [game.jpg](game.jpg)
- near (238.6 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 57.0% | 62.9% | 456 | 29 |
| far | walk | 58.4% | 63.5% | 425 | 30 |
| far | attack | 60.1% | 62.0% | 476 | 33 |
| game | idle | 73.4% | 77.5% | 1704 | 58 |
| game | walk | 76.0% | 78.9% | 1608 | 61 |
| game | attack | 77.3% | 79.1% | 1764 | 66 |
| near | idle | 86.0% | 87.7% | 6367 | 116 |
| near | walk | 86.6% | 88.5% | 6174 | 121 |
| near | attack | 88.0% | 88.8% | 6705 | 136 |

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
