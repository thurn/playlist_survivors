# Edge cut: pine (a structure)

Written by `champions edgecut review pine` from the recipe [tools/recipes/edgecut/pine.py](../../../tools/recipes/edgecut/pine.py).

The pines' edge cut (ps-6y2.15): Synty's Fantasy Kingdom pine cluster 01 (1,220 triangles: ten cones of needles on
their trunks) as a stand of pines.

- Size: 8 m to the tallest tip (the vendor's 19 m), the stand 4 m across.
- Body (the fill): a hull over each cone and over each trunk below it.
- Lines: each cone's rim and its sharpest slopes (its hull's edges past 50 degrees).
- Shards: each cone two pieces, each trunk one.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 107 | ok |
| body triangles 12-800 | 212 | ok |

30 shard pieces. 3.975 by 4.113 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 7.952 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| cone0 | 16 | 10 |
| cone1 | 16 | 10 |
| cone2 | 12 | 10 |
| cone3 | 14 | 12 |
| cone4 | 14 | 13 |
| cone5 | 10 | 11 |
| cone6 | 12 | 10 |
| cone7 | 16 | 9 |
| cone8 | 12 | 11 |
| cone9 | 12 | 11 |
| trunk10 | 8 | 0 |
| trunk11 | 8 | 0 |
| trunk12 | 8 | 0 |
| trunk13 | 8 | 0 |
| trunk14 | 8 | 0 |
| trunk15 | 8 | 0 |
| trunk16 | 6 | 0 |
| trunk17 | 8 | 0 |
| trunk18 | 8 | 0 |
| trunk19 | 8 | 0 |
