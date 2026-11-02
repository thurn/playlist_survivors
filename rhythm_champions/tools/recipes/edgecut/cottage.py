"""The cottage's edge cut (ps-6y2.15): Synty's Fantasy Kingdom preset house 03 (11,158 triangles, one mesh standing on
its corner with its foundation posts below the ground) redrawn as a box of walls under a gabled roof, its chimney and
its porch.

- Size: 5 m from the ground to the ridge (the vendor's 8 m), so it stands under three heroes tall and hides little
  of the field behind it at the game camera.
- Body (the fill): clean solids measured off the vendor mesh (its timbers, shingles and windows go): the walls' box,
  the roof's gable, overhanging the walls, the chimney's box through the roof, and the porch's deck and its little
  roof; the windows' glass is a child the cut never shows.
- Lines: the solids' sharp edges (corners, eaves, gable ends, ridge) and the porch's two posts as strokes.
- Shards: the walls in twelve pieces, the roof in ten, the chimney two, the porch's deck and roof one each.
"""

import numpy as np

from champions_tools.edgecut import Cut, Model, Source, solids

MODEL = Model(id="cottage", prefab="c004583408826104080451c4b7713ee5", heightM=5.0,
              hide=("SM_Bld_Preset_House_03_Glass_Optimized",))
ROLE = "structure"
EAVES, RIDGE = 2.6, 4.6  # m


def cut(src: Source) -> Cut:
    c = Cut(src)
    c.solid(solids.box(-0.35, 3.2, 0.0, EAVES, -3.2, 0.1), "walls")
    c.solid(solids.gable(-0.5, 3.64, -3.45, 0.3, EAVES, RIDGE, along="z", ridge_at=1.57), "roof")
    c.solid(solids.box(2.62, 3.1, 2.9, 4.95, -3.08, -2.55), "chimney")
    c.solid(solids.box(0.94, 3.14, 0.0, 0.35, 0.1, 1.22), "porch")
    c.solid(solids.box(0.9, 3.33, 2.3, 2.45, 0.1, 1.2), "porch_roof")
    for part in ("walls", "roof", "chimney", "porch", "porch_roof"):
        c.hull_edges(part, min_angle=30)
    for x in (1.02, 3.05):
        c.stroke([np.array([x, 0.35, 1.13]), np.array([x, 2.3, 1.13])], 0, "porch", importance=0.6)
    c.recentre()
    c.break_into({"walls": 12, "roof": 10, "chimney": 2})
    return c
