"""The tower's edge cut (ps-6y2.15): Synty's Fantasy Kingdom preset tower 01 (33,078 triangles in two meshes) redrawn
as an eight-sided shaft under a wide square roof and its spire.

- Size: 10 m from the ground to the weathervane (the vendor's 21 m): the tallest structure, about five heroes.
- Body (the fill): clean solids measured off the vendor mesh (its stones, balconies, windows and shingles go): the
  shaft's octagonal prism to the eaves, the roof's square pyramid flaring out over it, and a thin spire; the windows'
  glass is a child the cut never shows.
- Lines: the shaft's rims and corners, the roof's eaves and hips, and the spire
  above the roof as a stroke to the vane.
- Shards: the shaft in sixteen pieces, the roof in ten, the spire one.
"""

import numpy as np

from champions_tools.edgecut import Cut, Model, Source, solids

MODEL = Model(id="tower", prefab="ad046a662ca27474fbd4aef7604bdde6", heightM=10.0, hide=("SM_Bld_Preset_Tower_01_Glass",))
ROLE = "structure"
EAVES, APEX, VANE = 5.2, 8.2, 9.8  # m


def cut(src: Source) -> Cut:
    c = Cut(src)
    c.solid(solids.prism(0, 0, 1.52 / np.cos(np.pi / 8), 0.0, EAVES, sides=8, turn=np.pi / 8), "shaft")
    c.solid(solids.pyramid(0, 0, 2.14 * np.sqrt(2), EAVES, APEX, sides=4, turn=np.pi / 4), "roof")
    c.solid(solids.prism(0, 0, 0.12, APEX - 0.6, VANE - 0.4, sides=4), "spire")
    c.hull_edges("shaft", min_angle=40)
    c.hull_edges("roof", min_angle=25)
    c.stroke([np.array([0, APEX, 0]), np.array([0, VANE, 0])], 0, "spire", importance=0.5)
    c.recentre()
    c.break_into({"shaft": 16, "roof": 10})
    return c
