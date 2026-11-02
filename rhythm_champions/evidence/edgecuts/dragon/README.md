# Edge cut: dragon

Written by `champions edgecut review dragon` from the recipe [tools/recipes/edgecut/dragon.py](../../../tools/recipes/edgecut/dragon.py) (role boss).

The dragon's edge cut (ps-6y2.12): Magic Pig's Dragon LP (5,444 triangles without its saddle, 108 entries)
redrawn as a big dark shape with a few strong strokes (docs/champions/reference/dragon-duel.jpg). The boss stands
3.39 m with a 12 m wingspan: about 215 px tall and 750 px from wing tip to wing tip at game scale.

- Body: one hull per part, the neck and tail in short runs of bones so they bend; each wing's membrane a hull per
  finger, so the scallops of its trailing edge stay dark between them. The saddle goes.
- Lines: the outline-making creases of the head, neck, body, tail and legs (lines.choose), chains shorter than 8% of
  the height dropped, and one stroke down the back over its spines from the neck to the tail's tip. The wings are a
  few spars: per wing the leading edge from the shoulder over the wrist to the first finger's tip and the other
  three fingers from the wrist, strokes along their bones that bend as the wings beat. The membranes' trailing edges
  stay unlit.
- Clips: the flying clips are authored on the ground, with the takeoff rising 4.43 m by itself and the landing
  falling as far, so fly and land are raised that much (liftM): takeoff, fly and land play in order at the dragon's
  ground position, the dive landing on the marked spot at 1.27 s. The breath's fire starts at 0.4 s; the tail sweep
  crosses the front at 1.15 s; the fireball leaves on Attack 2's snap after it rears, at 1.8 s; hit is Break's
  stagger (bosses take no other hit reaction).
- Shards: 40 pieces, the most a cut has: a piece per part, the head, the neck's base, chest, belly, pelvis and the
  tail's base split in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 55% | 82.2% (poses 80%, 78%, 75%, 79%, 85%, 90%, 88%) | ok |
| line segments <= 450 | 146 | ok |
| body triangles 300-1500 | 524 | ok |

40 shard pieces; the bone texture holds 91 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density, three poses to a row (idle, walk, breath, tail, fireball, fly, land), each the original then the cut, and a last row of the shatter: the first pose broken by a blow away from the camera, then 0.08, 0.2, 0.4, 0.8 and 1.4 s after (as many as the row holds). Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (105.5 px tall): [far.jpg](far.jpg)
- game (212.8 px tall, saved at 1/2 size): [game.jpg](game.jpg)
- near (425.6 px tall, saved at 1/3 size): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 74.2% | 79.5% | 9550 | 161 |
| far | walk | 73.0% | 78.3% | 7665 | 157 |
| far | breath | 71.0% | 76.7% | 8361 | 187 |
| far | tail | 75.4% | 80.8% | 10077 | 156 |
| far | fireball | 79.4% | 83.5% | 11202 | 190 |
| far | fly | 86.0% | 87.8% | 8553 | 130 |
| far | land | 83.0% | 86.4% | 11851 | 225 |
| game | idle | 80.1% | 87.1% | 37358 | 322 |
| game | walk | 77.8% | 85.8% | 29915 | 311 |
| game | breath | 75.3% | 84.3% | 32526 | 373 |
| game | tail | 79.2% | 87.3% | 39460 | 315 |
| game | fireball | 85.0% | 89.2% | 44658 | 384 |
| game | fly | 89.8% | 92.7% | 36203 | 269 |
| game | land | 87.9% | 91.9% | 47475 | 457 |
| near | idle | 66.0% | 89.2% | 144818 | 636 |
| near | walk | 66.0% | 88.1% | 115259 | 604 |
| near | breath | 61.3% | 86.7% | 125937 | 739 |
| near | tail | 68.9% | 89.3% | 151316 | 628 |
| near | fireball | 75.8% | 91.2% | 176760 | 776 |
| near | fly | 83.9% | 94.2% | 166945 | 589 |
| near | land | 82.9% | 93.6% | 191085 | 927 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| head | 30 | 10 |
| neck_a | 12 | 8 |
| neck_b | 14 | 11 |
| chest | 28 | 4 |
| belly | 22 | 2 |
| pelvis | 20 | 12 |
| tail_a | 10 | 1 |
| tail_b | 12 | 28 |
| tail_c | 12 | 1 |
| tail_d | 10 | 3 |
| thigh_l | 16 | 4 |
| thigh_r | 14 | 4 |
| shin_l | 12 | 5 |
| shin_r | 12 | 5 |
| foot_l | 16 | 4 |
| foot_r | 16 | 4 |
| arm_l | 8 | 1 |
| arm_r | 12 | 1 |
| forearm_l | 10 | 1 |
| forearm_r | 10 | 1 |
| hand_l | 16 | 4 |
| hand_r | 16 | 4 |
| wingarm_l | 12 | 0 |
| wingarm_r | 12 | 0 |
| wingfore_l | 12 | 0 |
| wingfore_r | 10 | 0 |
| membrane1_l | 20 | 5 |
| membrane1_r | 20 | 5 |
| membrane2_l | 18 | 3 |
| membrane2_r | 18 | 3 |
| membrane3_l | 18 | 3 |
| membrane3_r | 20 | 3 |
| membrane4_l | 16 | 3 |
| membrane4_r | 20 | 3 |
