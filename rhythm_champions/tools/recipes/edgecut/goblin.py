"""The goblin's edge cut (ps-6y2.10): Magic Pig's Goblins LP (12,457 triangles in its prefab's active renderers, 83
entries) redrawn as a few dozen lines over a body of convex parts.

- Body: one hull per part, corners budgeted by how much of the outline the part makes at game scale; the big ears
  their own parts. The helmet, eyes, teeth and rings lie inside their part's hull. The shield is a flat disc.
- Lines: the outline-making creases (lines.choose), chains shorter than 6% of the height dropped; the head's and
  ears' kept, the torso's capped; the shield an empty circle; the sword two strokes (pommel to tip, the guard).
- Shards: a piece per part, the chest, the pelvis and the shield split in two.
"""

from itertools import pairwise

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source

MODEL = Model(
    id="goblin",
    prefab="2d5e83b858ed8424291e890a090902db",
    heightM=1.2,
    clips=(
        Clip("idle", "907a74ad4197d4f268e118a17c5aa22e", "Idle", loop=True),
        # The stride is game/'s roster's 1.015 m at the model's native 1.5 m, scaled to 1.2 m.
        Clip("walk", "da8c9ca50ca4e4ab5b4f5b27efb389d0", "Walk", loop=True, strideM=0.812),
        Clip("attack", "737c1f0a1861943258251a79e1729de0", "Attack1", impact=1.017, end=1.7),
        Clip("hit", "5399d5151847d4e3681776932651afe6", "GotHit", impact=0.0, end=1.617),
    ),
)

# Parts by their triangles' dominant bones; limbs split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": ("head", "neck", "jaw_jnt", "brow_*", "*_Lips", "nose_*", "tangue_jnt", "eye_*_blink"),
    "chest": ("sp3", "sp2", "shoulder_*_jnt"),
    "belly": ("sp1",),
    "pelvis": ("pelvis", "Skirt_*"),
}
SIDED = {
    "pauldron": ("paudron_*_jnt",),
    "upperarm": ("upperArm_*_jnt",),
    "forearm": ("elbow_*_jnt",),
    "hand": ("hand_*_jnt", "F*_jnt*"),
    "thigh": ("thig_*",),
    "calf": ("knee_*",),
    "foot": ("foot_*", "ball_*"),
}
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"head": 20, "chest": 18, "belly": 10, "pelvis": 16, "pauldron": 10, "upperarm": 8, "forearm": 10,
           "hand": 10, "thigh": 8, "calf": 10, "foot": 10}
EARS = ("ear_*",)
SHIELD = ("Shield1",)
SWORD = ("Sword1",)
ROLE = "fodder"
LINE_BUDGET = 14  # feature lines; the ears, the circle and the strokes come on top
CAPS = {"head": 4, "chest": 2, "pauldron_l": 2, "pauldron_r": 2, "forearm_l": 2, "forearm_r": 2}  # others none


def principal_axes(points: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """The centre and the axes (rows, longest first) of a point cloud."""
    centre = points.mean(0)
    _, _, axes = np.linalg.svd(points - centre, full_matrices=False)
    return centre, axes


def spine(points: np.ndarray, base: np.ndarray, steps: int) -> list[np.ndarray]:
    """An ear's spine in `steps` segments: from its root (the mean of the points in the tenth nearest `base`, where
    it leaves the head) through the means of the points at even distances between to its tip (the farthest point)."""
    d = np.linalg.norm(points - base, axis=1)
    lo, span = d.min(), np.ptp(d)
    out = [points[d <= lo + 0.1 * span].mean(0)]
    for k in range(1, steps):
        out.append(points[np.abs(d - (lo + k / steps * span)) <= 0.5 * span / steps].mean(0))
    out.append(points[d.argmax()])
    return out


def cut(src: Source) -> Cut:
    c = Cut(src)
    shield, sword, ears = src.renderers_named(*SHIELD), src.renderers_named(*SWORD), src.bones_named(*EARS)
    body = src.all() - shield - sword - ears
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
            raise ValueError(f"goblin: part {name} overlaps another")
        claimed = claimed | sel
    if len(body - claimed):
        raise ValueError(f"goblin: {len(body - claimed)} body triangles in no part")

    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[name.removesuffix("_l").removesuffix("_r")])

    # The ears: no body, a stroke along each from the head to its point, on the ear's root bone.
    head_centre = src.positions[src.vertices_of(parts["head"])].mean(0)
    for side, sel in (("L", ears & left), ("R", ears - left)):
        pts = src.positions[src.vertices_of(sel)]
        base = pts[np.linalg.norm(pts - head_centre, axis=1).argmin()]
        c.stroke(spine(pts, base, 2), src.entry(f"ear_{side}_jnt1"), "ears")

    # The shield: an empty circle round its face over a dark disc, on the shield's bone.
    pts = src.positions[src.vertices_of(shield)]
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

    caps = {name: CAPS.get(name, 0) for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.06)
    c.break_into({"chest": 2, "pelvis": 2, "shield": 2})
    return c
