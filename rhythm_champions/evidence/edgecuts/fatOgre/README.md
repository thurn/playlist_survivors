# Edge cut: fatOgre

Written by `champions edgecut review fatOgre` from the recipe [tools/recipes/edgecut/fatOgre.py](../../../tools/recipes/edgecut/fatOgre.py) (role boss).

The Fat Ogre's edge cut (ps-6y2.12): Heroic Fantasy's Fat Ogre (17,211 body triangles, 55 bones, and its
morning star, 1,686) redrawn as big dark shapes and a few strong strokes. The mid-boss stands 5 m: about 315 px
tall at game scale, so its lines are a small share of it.

- Body: one hull per part, the belly and chest the largest; the eyes go, the fingers join the hand.
- Lines: the outline-making creases of the belly, chest, head and arms (lines.choose), chains shorter than 5% of the
  height dropped. Its skin is smooth, so an edge is on the silhouette from fewer headings than an armoured cut's
  (salience from 0.12, not 0.3), and lines deeper than 6 cm inside a hull, which the body hides anyway, are dropped
  before they spend the count. The club is its name: the shaft one stroke from the pommel through the head, the
  banded head a dark hull with two rings round it.
- Clips: the pack has no stomp and no throw. The smash is Attack3 (overhead, the club meeting the ground in front at
  0.53 s); the stomp's ring is Attack1, the club swept across the front from behind the right shoulder (crossing the
  front at 0.47 s); the boulder throw is Attack2's upward swing (fastest at 0.5 s); the charge is the run; the
  entrance roar is RoarTaunt. Bosses take no hit reaction (design.md, Bosses: no stagger).
- Shards: 40 pieces, the most a cut has (plan.md, Rendering > Edge cuts): the belly, chest and pelvis split most.
  The shards' throw grows with the type's height, so the Ogre's fly furthest of any monster's.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 55% | 89.5% (poses 90%, 90%, 90%, 89%, 88%, 89%) | ok |
| line segments <= 450 | 201 | ok |
| body triangles 300-1500 | 530 | ok |

40 shard pieces; the bone texture holds 49 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density, three poses to a row (idle, walk, charge, smash, stomp, throw), each the original then the cut, and a last row of the shatter: the first pose broken by a blow away from the camera, then 0.08, 0.2, 0.4, 0.8 and 1.4 s after (as many as the row holds). Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (155.6 px tall): [far.jpg](far.jpg)
- game (313.9 px tall): [game.jpg](game.jpg)
- near (627.8 px tall, saved at 1/2 size): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 81.7% | 83.0% | 8080 | 160 |
| far | walk | 81.2% | 82.9% | 7593 | 147 |
| far | charge | 79.5% | 81.7% | 7746 | 156 |
| far | smash | 80.7% | 81.3% | 7672 | 214 |
| far | stomp | 78.5% | 80.4% | 7367 | 178 |
| far | throw | 79.9% | 81.5% | 8912 | 146 |
| game | idle | 90.3% | 90.8% | 31939 | 323 |
| game | walk | 90.1% | 90.6% | 30209 | 296 |
| game | charge | 89.5% | 90.0% | 30641 | 317 |
| game | smash | 89.1% | 89.5% | 29978 | 436 |
| game | stomp | 88.4% | 89.2% | 29169 | 367 |
| game | throw | 89.5% | 89.9% | 36294 | 308 |
| near | idle | 91.8% | 93.3% | 126885 | 652 |
| near | walk | 91.9% | 93.3% | 121810 | 601 |
| near | charge | 91.1% | 92.8% | 122407 | 654 |
| near | smash | 91.0% | 92.5% | 119629 | 915 |
| near | stomp | 89.3% | 92.2% | 116014 | 765 |
| near | throw | 90.8% | 92.7% | 154282 | 677 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 34 | 9 |
| ponytail | 14 | 0 |
| chest | 58 | 23 |
| belly | 66 | 44 |
| pelvis | 32 | 16 |
| upperarm_l | 24 | 13 |
| upperarm_r | 26 | 13 |
| forearm_l | 26 | 9 |
| forearm_r | 28 | 9 |
| hand_l | 24 | 6 |
| hand_r | 26 | 6 |
| thigh_l | 26 | 8 |
| thigh_r | 26 | 8 |
| calf_l | 24 | 8 |
| calf_r | 24 | 8 |
| foot_l | 22 | 0 |
| foot_r | 24 | 0 |
| club | 26 | 21 |
