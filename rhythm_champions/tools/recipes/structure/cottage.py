"""The cottage (ps-6y2.43): Synty's timber house rebuilt as a few simple faceted shapes, every edge of them drawn.

Drawn at its creases, the vendor model's shingles, beams and planks never close into facets: its roof breaks into short
open dashes that end mid-face like cracks, its rails into hatching, and two blind critiques (ps-6y2.30) read it as
"shaded blue clay" and "a blueprint sketch". What draws now:

- A stone plinth a little wider than the walls, out to the porch in front, and the walls a box on it, within the sim's
  footprint (3.6 by 4.4 m); a door in the front wall and windows, big enough to read, in the back and both long sides,
  each an inset 0.1 m deep whose jambs and back draw, so it reads as an opening and not a printed square.
- A steep gable roof along z, one closed prism, so each eave and rake is one line (stepped courses with their
  thickness drawn read as "corrugated, like a crate" and their stacked edges as hatching), one course line across each
  slope, so neither slope is one empty quad (the third critique; a ridge cap drew as doubled lines to the fourth); a
  short overhang, so the walls and
  their windows show under the eaves from the game camera (a deep eave read from the side as a crate lid).
- A chimney standing on the roof, its foot cut to the roof's slope so its outline closes on the roof, and a capped top
  (the kind's 5 m).
- A porch: a little gable roof of the same kind on two stout posts over the door (thin posts read as a table).
- Dropped: the crossed boards at the gable peaks (loose stubs to the first critique), the shingles, beams, balusters
  and the side annex.

Long edges are halved along lines that never draw (`Builder.mesh(max_edge)`) so the shatter can cut chunks of a size
from big faces, and it shatters into 36 pieces (20 for the vendor's): smaller chunks show smaller fans of inside faces.
Numpy only, no vendor input: the proportions were measured from the vendor model by eye. Bake:
`just champions-tool structure bake cottage`; review: `just champions-tool structure review all`.
"""

from __future__ import annotations

import numpy as np

from champions_tools.structure import Builder, Kind, Mesh, box, face, loft

PLINTH = 0.35  # the stone base's top
WALL_X, WALL_Z0, WALL_Z1 = 1.5, -1.5, 1.9  # the walls' half width, their front and back
EAVE = 2.7  # the walls' top
ROOF_X, ROOF_Y, RIDGE = 1.78, 2.55, 4.45  # the eaves' reach and height, the ridge's
ROOF_Z0, ROOF_Z1 = -1.8, 2.2  # the roof's ends, past the gables
PORCH_Z, PORCH_X = -2.2, 0.75  # the porch's front and half width
PIECES = 36  # the shatter's chunks


def gable(b: Builder, cx: float, half: float, z0: float, z1: float, eave: float, ridge: float, course: float = 0.0) -> float:
    """A gable roof along z as one closed prism: two planes to the ridge over a flat underside at the eaves, a triangle
    at each end; so each eave and rake is one edge; with `course`, a line across each plane that far up it. Its slope."""
    l0, r0, t0 = (np.array([cx + dx, y, z0]) for dx, y in ((-half, eave), (half, eave), (0.0, ridge)))
    l1, r1, t1 = (p + (0, 0, z1 - z0) for p in (l0, r0, t0))
    slope = (ridge - eave) / half
    for side, (e0, e1) in ((-1, (l0, l1)), (1, (r0, r1))):
        up = (side * slope, 1, 0)
        if course:
            m0, m1 = e0 + course * (t0 - e0), e1 + course * (t1 - e1)
            b.poly([e0, e1, m1, m0], facing=up)
            b.poly([m0, m1, t1, t0], facing=up)
            b.line(b.vert(m0), b.vert(m1))
        else:
            b.poly([e0, e1, t1, t0], facing=up)
    b.poly([l0, r0, r1, l1], facing=(0, -1, 0))
    b.poly([l0, t0, r0], facing=(0, 0, -1))
    b.poly([l1, r1, t1], facing=(0, 0, 1))
    return slope


def build(kind: Kind) -> Mesh:
    b = Builder()
    # the plinth: a little wider than the walls, out to the porch in front
    box(b, 0, (PORCH_Z - 0.05 + WALL_Z1 + 0.15) / 2, 2 * WALL_X + 0.3, WALL_Z1 + 0.15 - PORCH_Z + 0.05, 0, PLINTH)
    h = EAVE - PLINTH
    w, d = 2 * WALL_X, WALL_Z1 - WALL_Z0
    face(b, (-WALL_X, PLINTH, WALL_Z0), (1, 0, 0), (0, 1, 0), w, h, [(w / 2 - 0.45, 0, w / 2 + 0.45, 1.6)])
    face(b, (WALL_X, PLINTH, WALL_Z1), (-1, 0, 0), (0, 1, 0), w, h, [(w / 2 - 0.45, 0.8, w / 2 + 0.45, 1.6)])
    wins = [(0.4, 0.75, 1.3, 1.6), (d - 1.3, 0.75, d - 0.4, 1.6)]
    face(b, (WALL_X, PLINTH, WALL_Z0), (0, 0, 1), (0, 1, 0), d, h, wins)
    face(b, (-WALL_X, PLINTH, WALL_Z1), (0, 0, -1), (0, 1, 0), d, h, wins)
    slope = gable(b, 0.0, ROOF_X, ROOF_Z0, ROOF_Z1, ROOF_Y, RIDGE, course=0.5)
    # the chimney, its foot cut to the roof's slope, and its cap to the kind's height
    cx, cz, cs, cap = 0.85, 1.1, 0.55, 0.2
    xs = (cx - cs / 2, cx + cs / 2)
    foot = [np.array([x, RIDGE - x * slope, z]) for x, z in ((xs[0], cz - cs / 2), (xs[0], cz + cs / 2), (xs[1], cz + cs / 2),
                                                               (xs[1], cz - cs / 2))]
    loft(b, [foot, [p * (1, 0, 1) + (0, kind.height_m - cap, 0) for p in foot]])
    box(b, cx, cz, cs + 0.16, cs + 0.16, kind.height_m - cap, kind.height_m)
    # the porch: a little gable over the door on two stout posts
    for side in (1, -1):
        box(b, side * (PORCH_X - 0.12), PORCH_Z + 0.14, 0.2, 0.2, PLINTH, 2.25)
    gable(b, 0.0, PORCH_X + 0.08, PORCH_Z, WALL_Z0, 2.25, 2.75)
    return b.mesh(max_edge=1.2, pieces=PIECES)
