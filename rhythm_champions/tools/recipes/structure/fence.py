"""The fence (ps-6y2.43): Synty's rail fence rebuilt with real thickness, every edge of it drawn.

The vendor fence's rails are hairlines that double into "a smeared stroke" and nearly vanish at game scale (ps-6y2.30's
critiques). What draws now: three square posts with flat tops (the kind's 1.1 m; pyramid caps drew as an X in a box),
and two rails spaced well apart, each a square beam 0.16 m a side against the posts' front (a thin plank's two long edges
nearly touched, so each rail drew as a doubled line), running from the first post to the last (rail ends past the
posts floated as stubs). Numpy only, no vendor input. Bake:
`just champions-tool structure bake fence`; review: `just champions-tool structure review all`.
"""

from __future__ import annotations

from champions_tools.structure import Builder, Kind, Mesh, box

POST = 0.22  # a post's side
RAILS = [0.38, 0.86]  # each rail's middle height
RAIL = 0.16  # a rail's side: a square beam
DEPTH = 0.3  # the fence's depth, the sim footprint's


def build(kind: Kind) -> Mesh:
    b = Builder()
    sx, _ = kind.size_m
    end = sx / 2 - POST / 2
    front = -DEPTH / 2  # the rails' front; the posts stand behind it, the whole as deep as the footprint
    for x in (-end, 0.0, end):
        box(b, x, DEPTH / 2 - POST / 2, POST, POST, 0, kind.height_m)
    for y in RAILS:
        box(b, 0, front + RAIL / 2, 2 * end + POST, RAIL, y - RAIL / 2, y + RAIL / 2, bottom=True)
    return b.mesh(max_edge=1.2)
