"""The tower (ps-6y2.43): Synty's watchtower rebuilt as an octagonal stone tower, every edge of it drawn.

From the game camera the vendor tower is mostly roof: a wide flared roof of hundreds of tile boxes that the critiques
read as "a lace doily", "a fractal gear or snowflake". A first octagonal draft with a roof as wide as the vendor's still
read as "a lantern, gazebo or pagoda": the roof hid the shaft, so it had no height. What draws now:

- A battered plinth, one sloped band, and an octagonal shaft (a face toward each eighth) 6 m tall, one line round it
  halfway; a door inset at its foot, slit windows 0.44 m wide on the other faces (narrower vanished at game size), two
  staggered tiers of them.
- A corbel flaring out at its top to a thick parapet ring (the balcony as one ring), the chamber standing inside it.
- A roof only a little wider than the shaft, so the shaft shows below it from every heading: an octagonal cone flat
  underneath, so its eaves are one line, and a thin spire to the kind's 10 m. The second blind critique read bands
  drawn as ledges (a string course, an eave board) as stripes, so each band is one line.
- Dropped: the tiles, balcony rails, timber framing and beams.

Long edges are halved along lines that never draw, and it shatters into 40 pieces (22 for the vendor's). Numpy only, no
vendor input. Bake: `just champions-tool structure bake tower`; review: `just champions-tool structure review all`.
"""

from __future__ import annotations

import math

import numpy as np

from champions_tools.structure import Builder, Kind, Mesh, face, loft, polygon, ring

SIDES = 8
PHASE = 22.5  # a face, not a corner, toward -z (face 1)
BASE_R, BASE_Y = 1.75, 0.6  # the battered plinth, one sloped band: its foot's corner radius and its top
SHAFT_R, SHAFT_Y = 1.45, 6.0  # the shaft's corner radius and top
COURSE_Y = 3.2  # a course line round the shaft, where its two tiers of windows meet
CORBEL_R, CORBEL_H = 1.8, 0.35  # the shaft flares out under the parapet
PARAPET_H, PARAPET_T = 0.5, 0.18
ROOM_R, ROOM_Y = 1.12, 7.35  # the chamber standing inside the parapet
EAVE_R, EAVE_Y = 1.62, 7.3  # the roof's eaves
SPIRE_R, SPIRE_Y = 0.24, 9.05  # the cone's top, where the spire starts
PIECES = 40  # the shatter's chunks


def octagon(r: float, y: float) -> list[np.ndarray]:
    return ring(polygon(0, 0, r, SIDES, PHASE), y)


def faces(b: Builder, r: float, y0: float, y1: float, openings: dict[int, list[tuple[float, float, float, float]]]) -> None:
    """An upright octagonal band of flat faces with their openings (in metres up from y0)."""
    corners = polygon(0, 0, r, SIDES, PHASE)
    for i in range(SIDES):
        right = np.array([corners[i][0], y0, corners[i][1]])
        left = np.array([corners[(i + 1) % SIDES][0], y0, corners[(i + 1) % SIDES][1]])
        u = right - left
        width = float(np.linalg.norm(u))
        face(b, left, u / width, (0, 1, 0), width, y1 - y0, openings.get(i, []), depth=0.14)


def build(kind: Kind) -> Mesh:
    b = Builder()
    loft(b, [octagon(BASE_R, 0), octagon(SHAFT_R, BASE_Y)], top=False)
    w = 2 * SHAFT_R * math.sin(math.pi / SIDES)
    door = (w / 2 - 0.42, 0, w / 2 + 0.42, 1.6)
    low, high = (w / 2 - 0.22, 0.8, w / 2 + 0.22, 2.1), (w / 2 - 0.22, 0.75, w / 2 + 0.22, 2.05)
    faces(b, SHAFT_R, BASE_Y, COURSE_Y, {1: [door], 3: [low], 5: [low], 7: [low]})
    faces(b, SHAFT_R, COURSE_Y, SHAFT_Y, {0: [high], 2: [high], 4: [high], 6: [high]})
    course = [b.vert(p) for p in octagon(SHAFT_R, COURSE_Y)]  # one line round the shaft where its tiers meet
    for i in range(SIDES):
        b.line(course[i], course[(i + 1) % SIDES])
    # the corbelled parapet: the shaft flares out, a thick wall round the top
    inner = CORBEL_R - PARAPET_T
    loft(b, [octagon(SHAFT_R, SHAFT_Y), octagon(CORBEL_R, SHAFT_Y + CORBEL_H), octagon(CORBEL_R, SHAFT_Y + CORBEL_H + PARAPET_H),
             octagon(inner, SHAFT_Y + CORBEL_H + PARAPET_H), octagon(inner, SHAFT_Y + CORBEL_H + 0.1),
             octagon(ROOM_R, SHAFT_Y + CORBEL_H + 0.1)], top=False)
    # the chamber, then the roof: an eave ring, a cone and the spire
    loft(b, [octagon(ROOM_R, SHAFT_Y + CORBEL_H + 0.1), octagon(ROOM_R, EAVE_Y), octagon(EAVE_R, EAVE_Y),
             octagon(SPIRE_R, SPIRE_Y), octagon(SPIRE_R * 0.6, SPIRE_Y)], top=False)
    rim = [b.vert(p) for p in octagon(SPIRE_R * 0.6, SPIRE_Y)]
    tip = b.vert((0, kind.height_m, 0))
    for i in range(SIDES):
        mid = (b.points[rim[i]] + b.points[rim[(i + 1) % SIDES]]) / 2
        b.poly([rim[i], tip, rim[(i + 1) % SIDES]], facing=mid - np.array([0, mid[1], 0]))
    return b.mesh(max_edge=1.2, pieces=PIECES)
