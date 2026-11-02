# Edge cut: bat

Written by `champions edgecut review bat` from the recipe [tools/recipes/edgecut/bat.py](../../../tools/recipes/edgecut/bat.py) (role fodder).

The bat's edge cut (ps-6y2.9): Magic Pig's Bats LP (2,570 body triangles, 74 entries) redrawn as its wings' bones
over a dark body of convex parts. A 0.3 m flyer: about 19 px tall at game scale and 66 px from wing tip to wing tip
when spread, so it is drawn with very few lines.

- Body: one hull per part, the wing membrane a hull per finger bone so the scallops of its trailing edge stay dark
  between the fingers; the eyes, teeth and inner ears (small separate meshes inside the head) go.
- Lines: no feature lines (the membrane's thin edges come out as scribble at this size). Each wing is one stroke
  along its bones, bending at the joints as the wing flaps: the leading edge from the shoulder over the wrist to the
  long finger's tip; the ears are a stroke each. The second finger and the trailing edge stay unlit (a stroke on the
  second finger took the black share at game scale from 72% to 64%): the dark membrane hides the floor's grid
  behind it.
- Shards: a piece per part.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 65% | 72.5% (poses 73%, 81%, 63%) | ok |
| line segments <= 120 | 14 | ok |
| body triangles 300-800 | 364 | ok |

31 shard pieces; the bone texture holds 58 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (9.3 px tall): [far.jpg](far.jpg)
- game (18.8 px tall): [game.jpg](game.jpg)
- near (37.7 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 57.0% | 58.2% | 165 | 21 |
| far | walk | 66.5% | 66.5% | 161 | 15 |
| far | attack | 39.6% | 40.6% | 101 | 12 |
| game | idle | 73.5% | 74.9% | 573 | 44 |
| game | walk | 80.8% | 81.1% | 636 | 33 |
| game | attack | 63.3% | 63.6% | 352 | 22 |
| near | idle | 85.5% | 86.0% | 2195 | 89 |
| near | walk | 90.1% | 90.1% | 2458 | 65 |
| near | attack | 80.3% | 80.5% | 1358 | 47 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| body | 26 | 0 |
| head | 22 | 0 |
| tail | 10 | 0 |
| ear_l | 6 | 2 |
| ear_r | 6 | 2 |
| arm_l | 12 | 0 |
| arm_r | 12 | 0 |
| forearm_l | 12 | 0 |
| forearm_r | 8 | 0 |
| thumb_l | 8 | 0 |
| thumb_r | 8 | 0 |
| flap_l | 14 | 0 |
| flap_r | 16 | 0 |
| leg_l | 16 | 0 |
| leg_r | 14 | 0 |
| finger1_1_l | 12 | 0 |
| finger1_1_r | 12 | 0 |
| finger1_2_l | 10 | 0 |
| finger1_2_r | 12 | 0 |
| finger1_3_l | 12 | 0 |
| finger1_3_r | 12 | 0 |
| finger1_4_l | 10 | 0 |
| finger1_4_r | 6 | 0 |
| finger2_1_l | 12 | 0 |
| finger2_1_r | 12 | 0 |
| finger2_2_l | 12 | 0 |
| finger2_2_r | 12 | 0 |
| finger2_3_l | 10 | 0 |
| finger2_3_r | 12 | 0 |
| finger2_4_l | 8 | 0 |
| finger2_4_r | 10 | 0 |
| wing_l | 0 | 5 |
| wing_r | 0 | 5 |
