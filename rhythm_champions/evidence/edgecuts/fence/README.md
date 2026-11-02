# Edge cut: fence (a structure)

Written by `champions edgecut review fence` from the recipe [tools/recipes/edgecut/fence.py](../../../tools/recipes/edgecut/fence.py).

The fence's edge cut (ps-6y2.15): Synty's Fantasy Kingdom fence 01 (284 triangles: two posts, two rails and their
pegs, 2.4 m long), one length of a fence run.

- Size: 1.1 m over the ground (the vendor's), its posts' feet below it dropped.
- Body (the fill): a thin hull over each post and each rail; the pegs go.
- Lines: one stroke along each rail and each post, through its middle: at game scale a rail is a few pixels thick,
  and a box's twelve edges there would be a smear.
- Shards: each rail in six pieces, each post in four.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 8 | ok |
| body triangles 12-800 | 96 | ok |

36 shard pieces. 2.42 by 0.143 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 1.093 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| rail0 | 12 | 1 |
| rail1 | 12 | 1 |
| post2 | 12 | 1 |
| post3 | 12 | 1 |
| post4 | 12 | 1 |
| post5 | 12 | 1 |
| post6 | 12 | 1 |
| post7 | 12 | 1 |
