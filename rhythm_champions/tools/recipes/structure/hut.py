"""The hut (ps-6y2.43): Synty's cabin on stilts rebuilt as a round hut under a wide cone roof, every edge drawn.

Drawn at its creases, the vendor hut's roof slabs break into open cracks and its deck planks and balusters into barcode
hatching (both ps-6y2.30 critiques). Blind critiques here could not tell a gabled cabin from the cottage ("both are blue
gabled boxes"), read a deck and its rail as hatching and a pallet, a hipped roof on corner posts as a gazebo, and an
A-frame as "a tent or wedge", featureless from its slopes. The third asked for "a squat six-sided drum, a low wide cone
roof with overhanging eaves, and an outlined door", which reads from every heading and apart from both the cottage and
the tower. What draws now:

- A six-sided drum on a low six-sided platform as wide as the sim's footprint (4.46 m square), a door inset in one
  face and windows in two others, each 0.12 m deep with its jambs and back drawn.
- A roof in two pitches, its eaves a little out past the drum (further, they hid the walls and door from the game
  camera and it read as a lampshade): a shallow skirt to a ring, then steeper to a smoke vent, a little six-sided box,
  to the kind's 4.6 m. One closed solid, flat underneath, so each eave, hip and ring is one edge; the vent keeps the
  hips from meeting in a point.
- Dropped: the stilts, deck, planks, balusters, barrel and shingles.

Long edges are halved along lines that never draw, and it shatters into 36 pieces (20 for the vendor's). Numpy only, no
vendor input. Bake: `just champions-tool structure bake hut`; review: `just champions-tool structure review all`.
"""

from __future__ import annotations

import math

import numpy as np

from champions_tools.structure import Builder, Kind, Mesh, face, loft, polygon, ring

SIDES = 6
PHASE = 30.0  # a face, not a corner, toward -z (face 1)
SILL_R, SILL_Y = 2.45, 0.2  # the sill, a low platform over the sim's footprint: its corner radius and top
DRUM_R, DRUM_Y = 1.8, 2.2  # the drum's corner radius and its walls' top
EAVE_R, EAVE_Y = 2.2, 2.05  # the roof's eaves: their corner radius and height
BREAK_R, BREAK_Y = 1.35, 2.85  # where the skirt turns steeper
VENT_R, VENT_Y = 0.36, 4.3  # the smoke vent: its corner radius and foot
DOOR = (0.95, 1.5)
WINDOW = (0.6, 0.55, 0.75)  # width, height and sill height over the sill
PIECES = 36  # the shatter's chunks


def hexagon(r: float, y: float) -> list[np.ndarray]:
    return ring(polygon(0, 0, r, SIDES, PHASE), y)


def build(kind: Kind) -> Mesh:
    b = Builder()
    loft(b, [hexagon(SILL_R, 0.0), hexagon(SILL_R, SILL_Y), hexagon(DRUM_R, SILL_Y)], top=False)
    corners = polygon(0, 0, DRUM_R, SIDES, PHASE)
    w = 2 * DRUM_R * math.sin(math.pi / SIDES)
    door = (w / 2 - DOOR[0] / 2, 0.0, w / 2 + DOOR[0] / 2, DOOR[1])
    window = (w / 2 - WINDOW[0] / 2, WINDOW[2], w / 2 + WINDOW[0] / 2, WINDOW[2] + WINDOW[1])
    openings = {1: [door], 3: [window], 5: [window]}
    for i in range(SIDES):
        right = np.array([corners[i][0], SILL_Y, corners[i][1]])
        left = np.array([corners[(i + 1) % SIDES][0], SILL_Y, corners[(i + 1) % SIDES][1]])
        face(b, left, (right - left) / w, (0, 1, 0), w, DRUM_Y - SILL_Y, openings.get(i, []), depth=0.12)
    loft(b, [hexagon(EAVE_R, EAVE_Y), hexagon(BREAK_R, BREAK_Y), hexagon(VENT_R, VENT_Y), hexagon(VENT_R, kind.height_m)],
         top=True, bottom=True)
    return b.mesh(max_edge=1.2, pieces=PIECES)
