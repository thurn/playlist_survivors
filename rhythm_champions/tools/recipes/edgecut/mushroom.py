"""The mushroom's edge cut (ps-6y2.11): Magic Pig's Mushroom Monster LP (1,638 triangles, 15 entries: a broad cap on a
short stem and four stubby legs) redrawn as a ring round a dark cap, the design's read for the ranged mushroom.

- Body: one hull per part; the cap's finely cornered, since from the game camera's height it is nearly the whole
  silhouette.
- Lines: the cap an empty ring round its rim (none of its own creases); the outline-making creases of the stem, body,
  legs and feet (lines.choose), which peek out under the rim as the legs step.
- Clips: the spore shot is the cap's lunge (MushAttack01): it rears back and snaps forward, and the spore leaves on
  the snap (game/'s roster's impact).
- Shards: a piece per part, the cap in eight, the body and the stem in two.
"""

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Source

FBX = "119e98e7044e846bb86de248d6b7c57d"
MODEL = Model(
    id="mushroom",
    prefab="be803e2df48344dafbabe4dce2b5b5c4",
    heightM=1.0,
    clips=(
        Clip("idle", FBX, "MushIdle", loop=True),
        Clip("walk", FBX, "MushWalk", loop=True, strideM=0.971),
        Clip("attack", FBX, "MushAttack01", impact=0.683, end=1.367),
        Clip("hit", FBX, "MushHit", impact=0.0, end=0.983),
    ),
)
ROLE = "fodder"

# Parts by their triangles' dominant bones; each leg is its upper bones, then its foot.
PARTS = {"cap": ("Bone003",), "stem": ("Bone002",), "body": ("MushroomPelvis",)}
LEGS = {"1": ("MushLeg01", "Bone014", "Bone015"), "2": ("MushLeg02", "Bone005", "Bone006"),
        "3": ("MushLeg03", "Bone008", "Bone009"), "4": ("MushLeg04", "Bone011", "Bone012")}
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"cap": 72, "stem": 12, "body": 20, "leg": 10, "foot": 14}
LINE_BUDGET = 40  # feature lines; the ring comes on top


def cut(src: Source) -> Cut:
    c = Cut(src)
    parts = {name: src.bones_named(*bones) for name, bones in PARTS.items()}
    for n, (root, upper, foot) in LEGS.items():
        parts["leg" + n] = src.bones_named(root, upper)
        parts["foot" + n] = src.bones_named(foot)
    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[name.rstrip("1234")])

    # The cap: a ring round its rim (its widest from the cap's vertical axis) over its dark hull.
    pts = src.positions[src.vertices_of(parts["cap"])]
    centre = pts.mean(0)
    radial = np.linalg.norm((pts - centre)[:, [0, 2]], axis=1)
    rim = radial >= 0.95 * radial.max()
    c.circle([centre[0], pts[rim, 1].mean(), centre[2]], [0, 1, 0], radial.max(), src.entry("Bone003"), "cap", sides=16)

    c.feature_lines(parts, LINE_BUDGET, drop=("cap",), min_length=0.06)
    c.break_into({"cap": 8, "body": 2, "stem": 2})
    return c
