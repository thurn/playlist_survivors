"""The wall (ps-6y2.43): Synty's dry-stone wall rebuilt as a crenellated stone wall, every edge of it drawn.

Drawn at its creases, the vendor's dozens of small stones turn into "a strip of gravel" and "scribble even up close"
(ps-6y2.30's critiques). A first draft of four big chamfered blocks read clean but as "a row of crates or boxcars", and
its small capstones as vents. What draws now:

- One long body 0.88 m tall whose faces lean in toward the top (0.6 m deep at the foot, 0.48 at the top), each long face
  printed with two courses of big stones, their joints regular and staggered.
- Six merlons standing on it with gaps between them, flush with its faces, to the kind's 1.2 m: a wall at a glance, from
  every heading, the tower's stones (set in from the faces they read as Lego studs).

Its lines are printed with `Builder.line` where the stones meet in one plane. Numpy only, no vendor input. Bake:
`just champions-tool structure bake wall`; review: `just champions-tool structure review all`.
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.structure import Builder, Kind, Mesh, box

BODY_H, FOOT_D, TOP_D = 0.88, 0.6, 0.48  # the battered body: its height, its depth at the foot and at the top
COURSE = 0.44  # the two courses' joint
JOINTS = ([-1.4, 0.0, 1.4], [-2.1, -0.7, 0.7, 2.1])  # the lower and the upper course's joints: regular, staggered
MERLONS, MERLON_W = 6, 0.52  # merlons, as deep as the wall's top


def body(b: Builder, half: float) -> None:
    """A long block whose faces lean in toward the top, each long face printed with two courses of staggered stones."""
    def at(x: float, y: float, side: int) -> np.ndarray:
        d = FOOT_D + (TOP_D - FOOT_D) * y / BODY_H
        return np.array([x, y, side * d / 2])

    for side in (-1, 1):
        rows = [(0.0, COURSE, JOINTS[0]), (COURSE, BODY_H, JOINTS[1])]
        for y0, y1, joints in rows:
            xs = [-half, *joints, half]
            for xa, xb in pairwise(xs):
                b.poly([at(xa, y0, side), at(xa, y1, side), at(xb, y1, side), at(xb, y0, side)], facing=(0, 0.2, side))
            for x in joints:
                b.line(b.vert(at(x, y0, side)), b.vert(at(x, y1, side)))
        # the course line draws by itself: the two courses' cells meet off each other's corners
    for end in (-1, 1):
        x = end * half
        b.poly([at(x, 0, -1), at(x, BODY_H, -1), at(x, BODY_H, 1), at(x, 0, 1)], facing=(end, 0, 0))
    b.poly([at(-half, BODY_H, -1), at(-half, BODY_H, 1), at(half, BODY_H, 1), at(half, BODY_H, -1)], facing=(0, 1, 0))


def build(kind: Kind) -> Mesh:
    b = Builder()
    sx, _ = kind.size_m
    half = sx / 2
    body(b, half)
    gap = (sx - MERLONS * MERLON_W) / MERLONS
    for i in range(MERLONS):
        x = -half + gap / 2 + MERLON_W / 2 + i * (MERLON_W + gap)
        box(b, x, 0, MERLON_W, TOP_D, BODY_H, kind.height_m)
    return b.mesh(max_edge=1.2)
