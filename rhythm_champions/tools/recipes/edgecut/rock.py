"""The rock's edge cut (ps-6y2.15): Synty's Fantasy Kingdom rock 01 (70 triangles, half of it below the ground) as a
low boulder.

- Size: 1.3 m over the ground (the vendor's is a pebble 0.3 m across), so it stands as a boulder 3 m across.
- Body (the fill): one hull over what stands above the ground.
- Lines: the hull's sharpest edges, the boulder's facets.
- Shards: eight pieces.
"""

from champions_tools.edgecut import Cut, Model, Source

MODEL = Model(id="rock", prefab="818fbf9ffe287c5489125f1d37e02de5", heightM=1.3)
ROLE = "structure"


def cut(src: Source) -> Cut:
    c = Cut(src)
    c.hull(src.where(src.centroids[:, 1] > -0.1), "rock", points=18)
    c.hull_edges("rock", min_angle=30)
    c.recentre()
    c.break_into({"rock": 8})
    return c
