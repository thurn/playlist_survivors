"""The round tree (ps-6y2.43): Synty's leafy tree redrawn as a low faceted canopy on a long trunk, every edge drawn.

At 12 degrees the vendor tree closes into a ball of facets, but both ps-6y2.30 critiques read it as "an egg, a geode or a
second rock": its crown is one dense blob and its trunk hides under it. Here a crown of four balls was "the busiest
object by far ... a heap of dice", and one icosahedron "a d20, gem or lollipop". What draws now:

- A canopy wider than it is tall of three overlapping lumps, each a flattened icosahedron (twenty big facets) with
  its corners pulled in and out, the biggest on top to the kind's 5 m and two lower ones to the sides, so the outline
  is a canopy's, lumpy and asymmetric.
- A six-sided trunk 0.54 m across at the foot, tapering, up to the canopy's middle, long enough to show under the
  canopy from the game camera.

Numpy only, no vendor input. Bake: `just champions-tool structure bake tree`; review:
`just champions-tool structure review all`.
"""

from __future__ import annotations

import math

import numpy as np

from champions_tools.structure import Builder, Kind, Mesh, loft, polygon, ring

# The lumps: (middle x, z, half width, half height, turn in degrees, pulls of the upper and the lower ring's corners);
# the first one's top is the kind's height.
LUMPS = [((0.0, 0.0), 1.15, 0.85, 0, (1.15, 0.9, 1.1, 0.88, 1.05), (0.95, 1.1, 0.9, 1.08, 0.92)),
         ((0.82, 0.32), 0.85, 0.62, 25, (1.1, 0.92, 1.05, 0.9, 1.0), (1.0, 1.08, 0.9, 1.05, 0.95)),
         ((-0.7, -0.45), 0.8, 0.6, 50, (1.05, 0.9, 1.12, 0.92, 1.0), (0.92, 1.05, 0.95, 1.1, 0.9))]
LOW_Y = (3.9, 3.95)  # the two side lumps' middles
TRUNK = (0.27, 0.16, 3.9)  # its radius at the foot and at its top, inside the canopy


def lump(b: Builder, centre: np.ndarray, r: float, h: float, turn: float, upper, lower) -> None:
    """A flattened icosahedron standing on a corner, each corner of its two rings of five pulled by its factor."""
    lat = math.atan(0.5)

    def corner(i: int, up: bool) -> np.ndarray:
        a = math.radians(turn) + 2 * math.pi * i / 5 + (0 if up else math.pi / 5)
        pull = (upper if up else lower)[i]
        return centre + np.array([r * math.cos(lat) * pull * math.cos(a), h * (1 if up else -1) * math.sin(lat),
                                  -r * math.cos(lat) * pull * math.sin(a)])

    top, bottom = b.vert(centre + (0, h, 0)), b.vert(centre - (0, h, 0))
    hi = [b.vert(corner(i, True)) for i in range(5)]
    lo = [b.vert(corner(i, False)) for i in range(5)]
    for i in range(5):
        j = (i + 1) % 5
        for tri in ([top, hi[i], hi[j]], [hi[i], lo[i], hi[j]], [hi[j], lo[i], lo[j]], [bottom, lo[j], lo[i]]):
            b.poly(tri, facing=sum(b.points[k] for k in tri) / 3 - centre)


def build(kind: Kind) -> Mesh:
    b = Builder()
    loft(b, [ring(polygon(0, 0, TRUNK[0], 6, 0), 0.0), ring(polygon(0, 0, TRUNK[1], 6, 0), TRUNK[2])], top=False)
    for k, ((x, z), r, h, turn, upper, lower) in enumerate(LUMPS):
        y = kind.height_m - h if k == 0 else LOW_Y[k - 1]
        lump(b, np.array([x, y, z]), r, h, turn, upper, lower)
    return b.mesh(max_edge=1.0)
