"""The slime's edge cut (ps-6y2.9): Magic Pig's Slime LP (1,428 triangles: the jelly with a brain, veins and an eye
inside, 8 entries) redrawn as its outline and its eye over one dark blob.

- Body: one hull over the jelly, so the blob stays closed as it stretches and drags itself along; the brain and the
  veins inside it go. The hull is pulled in further than the default, under the rim. The eye is a flat disc on the
  eye's bone.
- Lines: the outline-making creases of the jelly (lines.choose), which at the game camera's pitch are its rim on
  the ground, simplified finely enough that its two chains overlap at the front without a gap; the eye an empty
  circle facing out of the blob.
- Clips: the vendor's stab and cannonball attacks are blend shapes (a spike, a ball) that the crowd's bones can't
  play, so the attack is the spit (bones only); the walk is a crawl, the hop on each beat the sim's step.
- Shards: the blob in twenty pieces, the eye one more.
"""

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Source

FBX = "7fec430887d724867a2483827db438a9"
MODEL = Model(
    id="slime",
    prefab="fe5eb242a632a47529d5bfc2a09bccda",
    heightM=0.9,
    clips=(
        Clip("idle", FBX, "Idle Cycle", loop=True),
        Clip("walk", FBX, "Walk Cycle", loop=True, strideM=0.83),
        Clip("attack", FBX, "Spit Attack", impact=1.29, end=1.6),
        Clip("hit", FBX, "Been Hit", impact=0.083, end=1.05),
    ),
)
ROLE = "fodder"
CORNERS = 160  # a hull of n corners has 2n - 4 triangles
INSET = 0.02  # the hull pulled in under the rim, which a bulge just above it would otherwise cover
LINE_BUDGET = 40  # feature lines; the eye's circle comes on top


def cut(src: Source) -> Cut:
    c = Cut(src)
    jelly = src.renderers_named("Slime:Slime")
    c.hull(jelly, "body", points=CORNERS, inset=INSET)

    # The eye: an empty circle over a dark disc, facing out of the blob from its middle, on the eye's bone.
    eye = src.renderers_named("Slime:Eye")
    pts = src.positions[src.vertices_of(eye)]
    centre = pts.mean(0)
    normal = centre - src.positions[src.vertices_of(jelly)].mean(0)
    radius = 0.5 * np.ptp(pts[:, 0])
    entry = src.dominant_entry(eye)
    c.disc(centre, normal, radius, entry, "eye")
    c.circle(centre, normal, radius, entry, "eye", sides=10)

    c.feature_lines({"body": jelly}, LINE_BUDGET, min_length=0.2, tolerance=0.006)
    c.break_into({"body": 20})
    return c
