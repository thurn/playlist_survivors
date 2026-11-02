"""The wall's edge cut (ps-6y2.15): Synty's Fantasy Kingdom stone wall 01 (846 triangles, a run of stones 4.7 m long)
as one segment of a wall run, which the chunks' templates stand end to end.

- Size: 1.2 m tall (the vendor's 1.0 m), 5.6 m long.
- Body (the fill): one hull, a slab.
- Lines: the slab's edges.
- Shards: ten pieces along it.
"""

from champions_tools.edgecut import Cut, Model, Source

MODEL = Model(id="wall", prefab="56f5a6de40a6f8f4d9078f2e524efd73", heightM=1.2)
ROLE = "structure"


def cut(src: Source) -> Cut:
    c = Cut(src)
    c.hull(src.where(src.centroids[:, 1] > 0.0), "wall", points=12)
    c.hull_edges("wall", min_angle=40)
    c.recentre()
    c.break_into({"wall": 10})
    return c
