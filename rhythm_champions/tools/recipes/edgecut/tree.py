"""The tree's edge cut (ps-6y2.15): Synty's Fantasy Kingdom round tree 01 (280 triangles: a trunk with a branch and
two blobs of leaves).

- Size: 5 m tall (the vendor's 6 m).
- Body (the fill): a thin prism for the trunk up into the leaves (the vendor's flared roots and branch go), a hull over
  each blob of leaves.
- Lines: the leaves' hulls' sharpest facet edges, and the trunk as a stroke up to the leaves.
- Shards: the leaves in eight and four pieces, the trunk in two.
"""

import numpy as np

from champions_tools.edgecut import Cut, Model, Source, solids

MODEL = Model(id="tree", prefab="86a6bda8f78af6b449cd7570bc4efc6f", heightM=5.0)
ROLE = "structure"
CROWN = 2.2  # m: where the leaves begin


def cut(src: Source) -> Cut:
    c = Cut(src)
    _, leaves, twig = src.components()[:3]
    c.solid(solids.prism(0.05, 0.02, 0.2, 0.0, CROWN + 0.4, sides=6), "trunk")
    c.hull(leaves, "leaves", points=22)
    c.hull(twig, "twig", points=12)
    c.hull_edges("leaves", min_angle=40)
    c.hull_edges("twig", min_angle=40)
    c.stroke([np.array([0.05, 0.0, 0.02]), np.array([0.05, CROWN, 0.02])], 0, "trunk")
    c.recentre()
    c.break_into({"leaves": 8, "twig": 4, "trunk": 2})
    return c
