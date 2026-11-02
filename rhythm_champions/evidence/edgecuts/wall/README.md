# Edge cut: wall (a structure)

Written by `champions edgecut review wall` from the recipe [tools/recipes/edgecut/wall.py](../../../tools/recipes/edgecut/wall.py).

The wall's edge cut (ps-6y2.15): Synty's Fantasy Kingdom stone wall 01 (846 triangles, a run of stones 4.7 m long)
as one segment of a wall run, which the chunks' templates stand end to end.

- Size: 1.2 m tall (the vendor's 1.0 m), 5.6 m long.
- Body (the fill): one hull, a slab.
- Lines: the slab's edges.
- Shards: twenty pieces along it.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 15 | ok |
| body triangles 12-800 | 20 | ok |

20 shard pieces. 5.592 by 0.603 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 1.148 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| wall | 20 | 15 |
