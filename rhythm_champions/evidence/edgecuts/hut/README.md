# Edge cut: hut (a structure)

Written by `champions edgecut review hut` from the recipe [tools/recipes/edgecut/hut.py](../../../tools/recipes/edgecut/hut.py).

The hut's edge cut (ps-6y2.15): Synty's Fantasy Kingdom preset hut 01 (8,562 triangles, one mesh standing on its
corner, its foundation below the ground) redrawn as a cabin under a gabled roof on its raised deck.

- Size: 4.6 m from the ground to the ridge (the vendor's 6.7 m).
- Body (the fill): clean solids measured off the vendor mesh: the deck's slab (its stilts and rails go), the cabin's
  box at its back and the roof's gable over it, its ridge running across the hut.
- Lines: the solids' sharp edges, and the deck's front corner posts as strokes.
- Shards: the deck in eight pieces, the cabin in eight, the roof in eight.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 35 | ok |
| body triangles 12-800 | 32 | ok |

24 shard pieces. 4.462 by 4.812 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 4.483 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| deck | 12 | 14 |
| cabin | 12 | 12 |
| roof | 8 | 9 |
