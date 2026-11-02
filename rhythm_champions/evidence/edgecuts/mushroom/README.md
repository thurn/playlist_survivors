# Edge cut: mushroom

Written by `champions edgecut review mushroom` from the recipe [tools/recipes/edgecut/mushroom.py](../../../tools/recipes/edgecut/mushroom.py) (role fodder).

The mushroom's edge cut (ps-6y2.11): Magic Pig's Mushroom Monster LP (1,638 triangles, 15 entries: a broad cap on a
short stem and four stubby legs) redrawn as a ring round a dark cap, the design's read for the ranged mushroom.

- Body: one hull per part; the cap's finely cornered, since from the game camera's height it is nearly the whole
  silhouette.
- Lines: the cap an empty ring round its rim (none of its own creases); the outline-making creases of the stem, body,
  legs and feet (lines.choose), which peek out under the rim as the legs step.
- Clips: the spore shot is the cap's lunge (MushAttack01): it rears back and snaps forward, and the spore leaves on
  the snap (game/'s roster's impact).
- Shards: a piece per part, the cap in eight, the body and the stem in two.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 65% | 84.8% (poses 85%, 85%, 85%) | ok |
| line segments <= 120 | 56 | ok |
| body triangles 300-800 | 334 | ok |

20 shard pieces; the bone texture holds 15 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (31.1 px tall): [far.jpg](far.jpg)
- game (62.8 px tall): [game.jpg](game.jpg)
- near (125.6 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 74.2% | 74.3% | 720 | 31 |
| far | walk | 74.4% | 74.7% | 727 | 32 |
| far | attack | 74.6% | 74.8% | 606 | 28 |
| game | idle | 85.1% | 85.1% | 2625 | 60 |
| game | walk | 84.6% | 84.7% | 2650 | 61 |
| game | attack | 84.6% | 84.6% | 2222 | 54 |
| near | idle | 91.9% | 92.0% | 9891 | 116 |
| near | walk | 91.8% | 91.8% | 9997 | 119 |
| near | attack | 92.0% | 92.0% | 8409 | 104 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| cap | 136 | 16 |
| stem | 18 | 0 |
| body | 36 | 1 |
| leg1 | 14 | 2 |
| foot1 | 24 | 8 |
| leg2 | 12 | 1 |
| foot2 | 22 | 8 |
| leg3 | 16 | 4 |
| foot3 | 16 | 8 |
| leg4 | 16 | 0 |
| foot4 | 24 | 8 |
