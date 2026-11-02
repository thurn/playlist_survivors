# Edge cut: slime

Written by `champions edgecut review slime` from the recipe [tools/recipes/edgecut/slime.py](../../../tools/recipes/edgecut/slime.py) (role fodder).

The slime's edge cut (ps-6y2.9): Magic Pig's Slime LP (1,428 triangles: the jelly with a brain, veins and an eye
inside, 8 entries) redrawn as its outline and its eye over one dark blob.

- Body: one hull over the jelly, so the blob stays closed as it stretches and drags itself along; the brain and the
  veins inside it go. The hull is pulled in further than the default, under the rim. The eye is a flat disc on the
  eye's bone.
- Lines: the outline-making creases of the jelly (lines.choose), which at the game camera's pitch are its rim on
  the ground, simplified finely enough that its two chains overlap at the front without a gap; the eye an empty
  circle facing out of the blob.
- Clips: the vendor's stab and cannonball attacks are blend shapes (a spike, a ball) that the crowd's bones can't
  play, so the attack is the spit (bones only); the walk is a crawl, the hop on each beat the sim's step.
- Shards: the blob in twenty pieces, the eye one more.

## Thresholds (plan.md, Rendering > Edge cuts)

| Threshold | Measured | |
| --- | --- | --- |
| black share at game scale >= 65% | 89.3% (poses 89%, 90%, 89%) | ok |
| line segments <= 120 | 34 | ok |
| body triangles 300-800 | 328 | ok |

21 shard pieces; the bone texture holds 7 entries.

## Sheets

The edge crowd draws the cut at the game camera's angle (60 degrees down, a 30 degree field, the monster turned 160 degrees) with the pipeline's post, beside the vendor original in flat grey. Each sheet is one camera distance, the same for every model (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far, near half), in square cells twice the larger of the monster's height and its reach at a 1080-row frame's pixel density: idle, walk and attack, each the original then the cut. Game scale is the old game camera's medium width; the world bead's camera may differ.

- far (28 px tall): [far.jpg](far.jpg)
- game (56.5 px tall): [game.jpg](game.jpg)
- near (113 px tall): [near.jpg](near.jpg)

## Black share per cell (the cut's silhouette darker than L* 10)

| Size | Pose | Black share | Without post | Silhouette px | On screen px |
| --- | --- | --- | --- | --- | --- |
| far | idle | 80.3% | 80.3% | 569 | 27 |
| far | walk | 79.6% | 79.6% | 633 | 30 |
| far | attack | 78.2% | 78.3% | 559 | 26 |
| game | idle | 89.1% | 89.1% | 2117 | 51 |
| game | walk | 89.8% | 89.8% | 2287 | 57 |
| game | attack | 89.0% | 89.0% | 2083 | 51 |
| near | idle | 94.4% | 94.4% | 7917 | 100 |
| near | walk | 94.7% | 94.7% | 8596 | 112 |
| near | attack | 94.2% | 94.2% | 7826 | 98 |

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| body | 312 | 24 |
| eye | 16 | 10 |
