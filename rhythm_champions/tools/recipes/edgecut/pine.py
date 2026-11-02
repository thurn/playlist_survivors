"""The pines' edge cut (ps-6y2.15): Synty's Fantasy Kingdom pine cluster 01 (1,220 triangles: ten cones of needles on
their trunks) as a stand of pines.

- Size: 8 m to the tallest tip (the vendor's 19 m), the stand 4 m across.
- Body (the fill): a hull over each cone and over each trunk below it.
- Lines: each cone's rim and its sharpest slopes (its hull's edges past 50 degrees).
- Shards: each cone two pieces, each trunk one.
"""

from champions_tools.edgecut import Cut, Model, Source

MODEL = Model(id="pine", prefab="5d42ad4d961622744969e55de6f5270b", heightM=8.0)
ROLE = "structure"


def cut(src: Source) -> Cut:
    c = Cut(src)
    above = src.where(src.centroids[:, 1] > 0.0)
    splits = {}
    for i, sel in enumerate(src.components(above)):
        if len(sel) >= 60:
            part = f"cone{i}"
            c.hull(sel, part, points=10)
            c.hull_edges(part, min_angle=50)
            splits[part] = 2
        elif len(sel) >= 6:
            part = f"trunk{i}"
            c.hull(sel, part, points=6)
            splits[part] = 1
    c.recentre()
    c.break_into(splits)
    return c
