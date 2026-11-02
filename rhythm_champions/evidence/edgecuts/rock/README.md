# Edge cut: rock (a structure)

Written by `champions edgecut review rock` from the recipe [tools/recipes/edgecut/rock.py](../../../tools/recipes/edgecut/rock.py).

The rock's edge cut (ps-6y2.15): Synty's Fantasy Kingdom rock 01 (70 triangles, half of it below the ground) as a
low boulder.

- Size: 1.3 m over the ground (the vendor's is a pebble 0.3 m across), so it stands as a boulder 3 m across.
- Body (the fill): one hull over what stands above the ground.
- Lines: the hull's sharpest edges, the boulder's facets.
- Shards: twenty pieces.

## Thresholds (plan.md, Rendering > Edge cuts; a structure has a fill, not a black body)

| Threshold | Measured | |
| --- | --- | --- |
| line segments <= 160 | 26 | ok |
| body triangles 12-800 | 28 | ok |

20 shard pieces. 3.116 by 3.317 m on the ground (x by z, its footprint's sizeM in Content/structures.json is the sim's), 1.295 m tall.

## Sheets

The structure field draws the cut at the game camera's angle (60 degrees down, a 30 degree field) with the pipeline's post, in the first zone's fill and edge, beside the vendor original in flat grey. Each sheet is one camera distance, the same as the monsters' (game scale stands a 1.8 m monster 113 px tall at 1080 rows; far is twice as far; no near sheet, wider than a sheet should be), at the pixel density of a 1080-row frame: the structure turned 20, 110, 235 degrees, each the original then the cut.

- far: [far.jpg](far.jpg)
- game: [game.jpg](game.jpg)

## Parts

| Part | Body triangles | Line segments |
| --- | --- | --- |
| rock | 28 | 26 |
