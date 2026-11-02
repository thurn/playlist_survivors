"""The Fat Ogre's edge cut (ps-6y2.12): Heroic Fantasy's Fat Ogre (17,211 body triangles, 55 bones, and its
morning star, 1,686) redrawn as big dark shapes and a few strong strokes. The mid-boss stands 5 m: about 315 px
tall at game scale, so its lines are a small share of it.

- Body: one hull per part, the belly and chest the largest; the eyes go, the fingers join the hand.
- Lines: the outline-making creases of the belly, chest, head and arms (lines.choose), chains shorter than 5% of the
  height dropped. Its skin is smooth, so an edge is on the silhouette from fewer headings than an armoured cut's
  (salience from 0.12, not 0.3), and lines deeper than 6 cm inside a hull, which the body hides anyway, are dropped
  before they spend the count. The club is its name: the shaft one stroke from the pommel through the head, the
  banded head a dark hull with two rings round it.
- Clips: the pack has no stomp and no throw. The smash is Attack3 (overhead, the club meeting the ground in front at
  0.53 s); the stomp's ring is Attack1, the club swept across the front from behind the right shoulder (crossing the
  front at 0.47 s); the boulder throw is Attack2's upward swing (fastest at 0.5 s); the charge is the run; the
  entrance roar is RoarTaunt. Bosses take no hit reaction (design.md, Bosses: no stagger).
- Shards: 40 pieces, the most a cut has (plan.md, Rendering > Edge cuts): the belly, chest and pelvis split most.
  The shards' throw grows with the type's height, so the Ogre's fly furthest of any monster's.
"""

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source

MODEL = Model(
    id="fatOgre",
    prefab="e040fed9364558541bb6583b064025c2",
    heightM=5.0,
    clips=(
        Clip("idle", "6b5e201a6a4432d4180d70e7d9d9d93e", "Idle", loop=True),
        Clip("walk", "950067766357002429f0616dae6b2207", "Walk", loop=True, strideM=4.1),
        Clip("charge", "b723a2a23ae2f9948b91122d94804a0f", "Run", loop=True, strideM=4.1),
        Clip("smash", "fb257973bbfc68149a4f05170713e05e", "Attack3", impact=0.53, end=1.167),
        Clip("stomp", "c6c60bc28d145e341afa22a0cc166250", "Attack1", impact=0.47, end=1.167),
        Clip("throw", "ce6e8484c21fc4b4b86347ffad099cb9", "Attack2", impact=0.5, end=1.167),
        Clip("roar", "ccf447dcedefe0845af78115db3dcaa4", "RoarTaunt", end=2.667),
    ),
    reviewPoses=("idle", "walk", "charge", "smash", "stomp", "throw"),
)

# Parts by their triangles' dominant bones; limbs split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": ("FAT_OGRE_ Head", "FAT_OGRE_ Neck"),
    "ponytail": ("FAT_OGRE_ Ponytail1",),
    "chest": ("FAT_OGRE_ Spine1", "FAT_OGRE_ ? Clavicle"),
    "belly": ("FAT_OGRE_ Spine", "FAT_OGRE_SAGGING_BELLY"),
    "pelvis": ("FAT_OGRE_ Pelvis",),
}
SIDED = {
    "upperarm": ("FAT_OGRE_ ? UpperArm",),
    "forearm": ("FAT_OGRE_ ? Forearm",),
    "hand": ("FAT_OGRE_ ? Hand", "FAT_OGRE_ ? Finger*"),
    "thigh": ("FAT_OGRE_ ? Thigh",),
    "calf": ("FAT_OGRE_ ? Calf",),
    "foot": ("FAT_OGRE_ ? Foot", "FAT_OGRE_ ? Toe0"),
}
EYES = ("FAT_OGRE_EYE_*",)
CLUB = ("MORGENSTERN_TRUE",)
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"head": 24, "ponytail": 10, "chest": 32, "belly": 40, "pelvis": 24, "upperarm": 16, "forearm": 16,
           "hand": 16, "thigh": 16, "calf": 14, "foot": 14, "club": 18}
ROLE = "boss"
LINE_BUDGET = 240  # feature lines; the club's strokes come on top
CAPS = {"head": 24, "chest": 40, "belly": 44, "pelvis": 16, "upperarm_l": 16, "upperarm_r": 16, "forearm_l": 14,
        "forearm_r": 14, "hand_l": 6, "hand_r": 6, "thigh_l": 8, "thigh_r": 8, "calf_l": 8, "calf_r": 8}  # others none
HEAD_SHARE = 0.27  # the club's head: this share of its length at the far end
SPLITS = {"belly": 5, "chest": 4, "pelvis": 3, "head": 3, "club": 2, **{f"{p}_{s}": 2 for p in (
    "upperarm", "forearm", "hand", "thigh", "calf") for s in "lr"}}


def principal_axes(points: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """The centre and the axes (rows, longest first) of a point cloud."""
    centre = points.mean(0)
    _, _, axes = np.linalg.svd(points - centre, full_matrices=False)
    return centre, axes


def cut(src: Source) -> Cut:
    c = Cut(src)
    eyes, club = src.bones_named(*EYES), src.renderers_named(*CLUB)
    body = src.all() - eyes - club
    centroid_x = src.positions[src.triangles].mean(1)[:, 0]
    left = Selection(centroid_x < 0)
    parts = {name: body & src.bones_named(*bones) for name, bones in CENTRE.items()}
    for name, bones in SIDED.items():
        sel = body & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    claimed = src.none()
    for name, sel in parts.items():
        if len(sel & claimed):
            raise ValueError(f"fatOgre: part {name} overlaps another")
        claimed = claimed | sel
    if len(body - claimed):
        raise ValueError(f"fatOgre: {len(body - claimed)} body triangles in no part")

    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[name.removesuffix("_l").removesuffix("_r")])

    # The club: split where the head starts along its long axis; the shaft's own line from the pommel (the end
    # behind the hand) through the head, two rings round the head and a dark hull over it, all on the club's bone.
    pts = src.positions[src.vertices_of(club)]
    middle = pts.mean(0)
    axis = principal_axes(pts)[1][0]
    hand = src.positions[src.vertices_of(parts["hand_r"])].mean(0)
    if (hand - middle) @ axis > 0:
        axis = -axis
    along = (pts - middle) @ axis
    head_from = along.max() - HEAD_SHARE * np.ptp(along)
    shaft_centre, shaft_axes = principal_axes(pts[along < head_from])
    shaft = shaft_axes[0] if shaft_axes[0] @ axis > 0 else -shaft_axes[0]
    at = (pts - shaft_centre) @ shaft
    head = along >= head_from
    radial = np.linalg.norm(pts - shaft_centre - np.outer(at, shaft), axis=1)
    h0, h1 = at[head].min(), at[head].max()
    ring = np.median(radial[head])
    entry = src.dominant_entry(club)
    tri_at = (src.positions[src.triangles].mean(1) - shaft_centre) @ shaft
    c.hull(club & Selection(tri_at >= h0), "club", points=CORNERS["club"])
    c.stroke([shaft_centre + at.min() * shaft, shaft_centre + h1 * shaft], entry, "club")
    for k in (0.25, 0.75):
        c.circle(shaft_centre + (h0 + k * (h1 - h0)) * shaft, shaft, ring, entry, "club", sides=10, importance=0.7)

    caps = {name: CAPS.get(name, 0) for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.05, min_salience=0.12,
                    max_turn_deg=60, tolerance=0.02, hidden_depth=0.012)
    c.break_into(SPLITS)
    return c
