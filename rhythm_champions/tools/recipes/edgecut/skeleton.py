"""The skeleton's edge cut, the reference cut (ps-6y2.8): Magic Pig's Skeleton Warrior LP (9,310 triangles, 120
entries) redrawn as about seventy lines over a body of convex parts.

- Body: one hull per part, its corners budgeted by how much of the outline the part makes at game scale (the
  chest and head most, fingers nothing: a hand is one small hull). The shield is a flat disc.
- Lines: the outline-making creases of the armour and bones (lines.choose), chains shorter than 6% of the height
  dropped; the helmet's kept, the torso's capped, the hands' and feet's given little (the ribs lie under the
  breastplate and show none); the shield an empty circle; the sword two strokes (pommel to tip, the guard).
  Fewer lines than the 120 allowed: at game scale each line's light spreads over a pixel or two of the dark body
  round it, and the black share needs that body.
- Shards: a piece per part, the chest, the pelvis and the shield split in two.
"""

from itertools import pairwise

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source

FBX = "d6688b4afa37b4b15b476037d756982c"
MODEL = Model(
    id="skeleton",
    prefab="6e3f218b742294893ad834f61c027945",
    heightM=1.8,
    clips=(
        Clip("idle", FBX, "Idle", loop=True),
        Clip("walk", FBX, "Walk", loop=True, strideM=1.224),
        Clip("attack", FBX, "Attack 1", impact=0.75, end=1.5),
        Clip("hit", FBX, "Got Hit", impact=0.067, end=1.6),
    ),
)

# Parts by their triangles' dominant bones; limbs split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": ("Base HumanHead", "Base HumanJaw", "Character1_Head*", "Base HumanNeck1"),
    "chest": ("Base HumanRibcage", "Base HumanSpine3", "Character1_Spine*", "Base HumanNeck", "*Collarbone",
              "Character1_LeftShoulder1", "Character1_RightShoulder1", "Character1_LeftShoulder4"),
    "ribs": ("Character1_LeftShoulder3", "Character1_RightShoulder3"),
    "spine": ("Base HumanSpine1", "Base HumanSpine2"),
    "pelvis": ("Base HumanPelvis", "Character1_*hip_armor"),
}
SIDED = {
    "pauldron": ("Character1_LeftShoulder_armor*", "Character1_LeftShoulder2", "Character1_RightShoulder2"),
    "upperarm": ("*Upperarm1",),
    "forearm": ("*Forearm1",),
    "hand": ("*Palm", "*Finger*", "*Thumb*"),
    "thigh": ("*Thigh1",),
    "calf": ("*Calf",),
    "foot": ("*Foot", "joint*"),
}
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"head": 18, "chest": 22, "ribs": 12, "spine": 8, "pelvis": 16, "pauldron": 12, "upperarm": 8,
           "forearm": 10, "hand": 8, "thigh": 8, "calf": 10, "foot": 10}
SHIELD = ("Shield_Main_LP", "Shield_Straps_LP")
SWORD = ("Sword_LP",)
ROLE = "fodder"
LINE_BUDGET = 58  # feature lines; the circle and the strokes come on top
CAPS = {"head": 6, "chest": 9, "pelvis": 8, "hand_l": 4, "hand_r": 4, "foot_l": 5, "foot_r": 5, "spine": 4}


def principal_axes(points: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """The centre and the axes (rows, longest first) of a point cloud."""
    centre = points.mean(0)
    _, _, axes = np.linalg.svd(points - centre, full_matrices=False)
    return centre, axes


def cut(src: Source) -> Cut:
    c = Cut(src)
    shield, sword = src.renderers_named(*SHIELD), src.renderers_named(*SWORD)
    body = src.all() - shield - sword
    centroid_x = src.positions[src.triangles].mean(1)[:, 0]
    left = Selection(centroid_x < 0)
    parts = {}
    for name, bones in CENTRE.items():
        parts[name] = body & src.bones_named(*bones)
    for name, bones in SIDED.items():
        sel = body & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    claimed = src.none()
    for name, sel in parts.items():
        if len(sel & claimed):
            raise ValueError(f"skeleton: part {name} overlaps another")
        claimed = claimed | sel
    if len(body - claimed):
        raise ValueError(f"skeleton: {len(body - claimed)} body triangles in no part")

    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[name.removesuffix("_l").removesuffix("_r")])

    # The shield: an empty circle round its face over a dark disc, on the shield's bone.
    pts = src.positions[src.vertices_of(src.renderers_named("Shield_Main_LP"))]
    centre, axes = principal_axes(pts)
    radius = 0.5 * np.mean([np.ptp(pts @ axes[0]), np.ptp(pts @ axes[1])])
    entry = src.dominant_entry(shield)
    c.disc(centre, axes[2], 0.96 * radius, entry, "shield")
    c.circle(centre, axes[2], radius, entry, "shield", sides=12)

    # The sword: the blade from pommel to tip and the guard across it at its widest, on the sword's bone.
    pts = src.positions[src.vertices_of(sword)]
    centre, axes = principal_axes(pts)
    along, across = (pts - centre) @ axes[0], (pts - centre) @ axes[1]
    hand = src.positions[src.vertices_of(parts["hand_r"])].mean(0)
    if (hand - centre) @ axes[0] > 0:  # the tip is the end away from the hand
        along, axes = -along, -axes
    tip, pommel = along.max(), along.min()
    bins = np.linspace(pommel, tip, 24)
    width = np.array([np.ptp(across[(along >= a) & (along < b)]) if ((along >= a) & (along < b)).sum() > 1 else 0
                      for a, b in pairwise(bins)])
    g = int(width.argmax())
    guard_at, guard_w = 0.5 * (bins[g] + bins[g + 1]), width[g]

    def at(s, w):
        return centre + s * axes[0] + w * axes[1]

    entry = src.dominant_entry(sword)
    c.stroke([at(pommel, 0), at(tip, 0)], entry, "sword")
    c.stroke([at(guard_at, -0.5 * guard_w), at(guard_at, 0.5 * guard_w)], entry, "sword", importance=0.5)

    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=CAPS, min_length=0.06)
    c.break_into({"chest": 2, "pelvis": 2, "shield": 2})
    return c
