"""The pine stand (ps-6y2.43): Synty's stand of ten pines redrawn as five faceted ones on faceted trunks.

The vendor pines already read as stacked faceted cones (both ps-6y2.30 critiques passed them), but each trunk is a
hairline that the high camera stretches into "a volley of rockets". What draws now: five trees spaced so each crown
keeps its own outline, their tiers wide and squat (nine, and six narrower ones, read as "a crystal cluster" and
"missiles"), each a six-sided trunk 0.3 m across rising into three stacked six-sided cones, each narrower than the one
under it and turned a twelfth so their edges don't line up, the tallest to the kind's 8 m. Numpy only, no vendor input.
Bake: `just champions-tool structure bake pine`; review: `just champions-tool structure review all`.
"""

from __future__ import annotations

import numpy as np

from champions_tools.structure import Builder, Kind, Mesh, loft, polygon, ring

SIDES = 6
TRUNK_R = 0.15
# The trees: (x, z, crown bottom, top, crown radius, turn in degrees); the tallest stands the kind's height.
TREES = [(-1.1, 0.8, 2.2, 5.8, 0.95, 10), (-0.9, -1.0, 2.7, 6.5, 0.98, 35), (0.2, 0.05, 3.8, 8.0, 1.02, 0),
         (1.15, 0.95, 2.0, 5.5, 0.9, 45), (1.0, -1.1, 2.4, 6.1, 0.92, 30)]
TIERS, TIER, SHRINK = 3, 0.4, 0.24  # stacked cones a crown, each this share of its height, each this much narrower


def tree(b: Builder, x: float, z: float, bottom: float, top: float, radius: float, turn: float) -> None:
    """A faceted trunk up into a crown of stacked six-sided cones, each wider at its foot than the one above."""
    loft(b, [ring(polygon(x, z, TRUNK_R, SIDES, turn), 0.0), ring(polygon(x, z, TRUNK_R * 0.8, SIDES, turn), bottom + 0.3)],
         top=False)
    tier = (top - bottom) * TIER
    for t in range(TIERS):
        y0 = bottom + (top - bottom - tier) * t / (TIERS - 1)
        y1 = y0 + tier
        foot = [b.vert(p) for p in ring(polygon(x, z, radius * (1.0 - SHRINK * t), SIDES, turn + 30 * t), y0)]
        tip = b.vert((x, y1, z))
        for i in range(SIDES):
            a, c = b.points[foot[i]], b.points[foot[(i + 1) % SIDES]]
            mid = (a + c) / 2
            b.poly([foot[i], tip, foot[(i + 1) % SIDES]], facing=mid - np.array([x, mid[1], z]))
        b.poly(list(reversed(foot)), facing=(0, -1, 0))


def build(kind: Kind) -> Mesh:
    b = Builder()
    for t in TREES:
        tree(b, *t)
    return b.mesh(max_edge=1.2)
