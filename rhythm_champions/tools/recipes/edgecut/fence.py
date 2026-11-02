"""The fence's edge cut (ps-6y2.15): Synty's Fantasy Kingdom fence 01 (284 triangles: two posts, two rails and their
pegs, 2.4 m long), one length of a fence run.

- Size: 1.1 m over the ground (the vendor's), its posts' feet below it dropped.
- Body (the fill): a thin hull over each post and each rail; the pegs go.
- Lines: one stroke along each rail and each post, through its middle: at game scale a rail is a few pixels thick,
  and a box's twelve edges there would be a smear.
- Shards: each rail in six pieces, each post in four.
"""

import numpy as np

from champions_tools.edgecut import Cut, Model, Source

MODEL = Model(id="fence", prefab="8b5272035dfd90243984731c03b5b100", heightM=1.1)
ROLE = "structure"


def cut(src: Source) -> Cut:
    c = Cut(src)
    entry = 0
    pieces = [p for p in src.components(src.where(src.centroids[:, 1] > 0.0)) if len(p) >= 20]
    splits = {}
    for i, sel in enumerate(pieces):
        pts = src.positions[src.vertices_of(sel)]
        lo, hi = pts.min(0), pts.max(0)
        rail = np.ptp(pts[:, 0]) > np.ptp(pts[:, 1])
        part = f"rail{i}" if rail else f"post{i}"
        c.hull(sel, part, points=8)
        mid = 0.5 * (lo + hi)
        ends = ([lo[0], mid[1], mid[2]], [hi[0], mid[1], mid[2]]) if rail else ([mid[0], max(lo[1], 0.0), mid[2]], [mid[0], hi[1], mid[2]])
        c.stroke([np.array(e) for e in ends], entry, part)
        splits[part] = 6 if rail else 4
    c.recentre()
    c.break_into(splits)
    return c
