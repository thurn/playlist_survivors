# Edge cut: cottage (a structure)

Written by `champions edgecut review cottage` from the recipe [tools/recipes/edgecut/cottage.py](../../../tools/recipes/edgecut/cottage.py).

The cottage's edge cut (ps-6y2.15): Synty's Fantasy Kingdom preset house 03 (11,158 triangles, one mesh standing on
its corner with its foundation posts below the ground) redrawn as a box of walls under a gabled roof, its chimney and
its porch.

- Size: 5 m from the ground to the ridge (the vendor's 8 m), so it stands under three heroes tall and hides little
  of the field behind it at the game camera.
- Body (the fill): clean solids measured off the vendor mesh (its timbers, shingles and windows go): the walls' box,
  the roof's gable, overhanging the walls, the chimney's box through the roof, and the porch's deck and its little
  roof; the windows' glass is a child the cut never shows.
- Lines: the solids' sharp edges (corners, eaves, gable ends, ridge) and the porch's two posts as strokes.
- Shards: the walls in twelve pieces, the roof in ten, the chimney two, the porch's deck and roof one each.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 59 | ok |
| body triangles 12-800 | 56 | ok |

24 shard pieces. 4.097 by 4.637 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 4.922 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| walls | 12 | 12 |
| roof | 8 | 9 |
| chimney | 12 | 12 |
| porch | 12 | 14 |
| porch_roof | 12 | 12 |
