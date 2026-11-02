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
| black share at game scale >= 65% | 78.2% (poses 77%, 78%, 79%) | ok |
| line segments <= 120 | 44 | ok |
| body triangles 300-800 | 308 | ok |

22 shard pieces; the bone texture holds 46 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (59.1 px tall): [far.jpg](far.jpg)
- game (119.3 px tall): [game.jpg](game.jpg)
- near (238.6 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 60.4% | 61.5% | 455 | 30 |
| far | walk | 61.8% | 64.1% | 429 | 31 |
| far | attack | 63.5% | 64.1% | 471 | 32 |
| game | idle | 77.1% | 77.4% | 1705 | 58 |
| game | walk | 78.2% | 78.8% | 1609 | 61 |
| game | attack | 79.2% | 79.4% | 1755 | 66 |
| near | idle | 87.5% | 87.7% | 6352 | 116 |
| near | walk | 87.9% | 88.3% | 6167 | 121 |
| near | attack | 88.7% | 88.7% | 6685 | 136 |

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
