# Edge cut: tower (a structure)

Written by `champions edgecut review tower` from the recipe [tools/recipes/edgecut/tower.py](../../../tools/recipes/edgecut/tower.py).

The tower's edge cut (ps-6y2.15): Synty's Fantasy Kingdom preset tower 01 (33,078 triangles in two meshes) redrawn
as an eight-sided shaft under a wide square roof and its spire.

- Size: 10 m from the ground to the weathervane (the vendor's 21 m): the tallest structure, about five heroes.
- Body (the fill): clean solids measured off the vendor mesh (its stones, balconies, windows and shingles go): the
  shaft's octagonal prism to the eaves, the roof's square pyramid flaring out over it, and a thin spire; the windows'
  glass is a child the cut never shows.
- Lines: the shaft's rims and corners, the roof's eaves and hips, and the spire
  above the roof as a stroke to the vane.
- Shards: the shaft in sixteen pieces, the roof in ten, the spire one.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 33 | ok |
| body triangles 12-800 | 46 | ok |

23 shard pieces. 4.197 by 4.197 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 9.341 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| shaft | 28 | 24 |
| roof | 6 | 8 |
| spire | 12 | 1 |
