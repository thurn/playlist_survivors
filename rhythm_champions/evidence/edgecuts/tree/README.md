# Edge cut: tree (a structure)

Written by `champions edgecut review tree` from the recipe [tools/recipes/edgecut/tree.py](../../../tools/recipes/edgecut/tree.py).

The tree's edge cut (ps-6y2.15): Synty's Fantasy Kingdom round tree 01 (280 triangles: a trunk with a branch and
two blobs of leaves).

- Size: 5 m tall (the vendor's 6 m).
- Body (the fill): a thin prism for the trunk up into the leaves (the vendor's flared roots and branch go), a hull over
  each blob of leaves.
- Lines: the leaves' hulls' sharpest facet edges, and the trunk as a stroke up to the leaves.
- Shards: the leaves in twelve and five pieces, the trunk in three.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 37 | ok |
| body triangles 12-800 | 72 | ok |

20 shard pieces. 2.398 by 2.005 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 4.97 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| trunk | 20 | 1 |
| leaves | 32 | 19 |
| twig | 20 | 17 |
