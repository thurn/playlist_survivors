"""The big ork's edge cut (ps-6y2.11): Synty's Big Ork (Fantasy Rivals, 6,112 triangles in its active skin, a humanoid
rig) with the Slayer greatsword in its right fist, redrawn as long lines and a few strokes over a body of convex parts.

- Pose: the line choice sees it in its sword idle, not its T-pose, so the outline it picks is the one the game shows.
- Body: one hull per part, corners budgeted by how much of the outline the part makes; the big ears are cut out of
  the head's hull (they would fill it out to their tips) and drawn as strokes instead.
- Lines: the long outline-making creases (lines.choose), chains shorter than 7% of the height dropped, capped per part
  (the belt, the pauldrons, the helmet and top-knot); four contour strokes along each arm and leg, which the
  chunky low-poly limbs give no long crease for; the tusks and the ears as strokes; the greatsword as its handle,
  guard and the blade's two edges meeting at the tip.
- Clips: Synty's sword idle and masculine walk; the attack is the heavy stab, its wind-up the charge's telegraph and
  its thrust (the impact) the charge itself.
- Shards: a piece per part, the chest and the pelvis split in two.
"""

from itertools import pairwise

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source, Weapon

MODEL = Model(
    id="bigOrk",
    prefab="0d42bc61d684259409d481c6f5dd5d5c",
    heightM=2.4,
    clips=(
        Clip("idle", "b94969b4c36c3c94284f72888ad9289d", "A_Idle_Base_Sword", loop=True),
        # The stride is game/'s roster's 2.15 m at its 2.9 m, scaled to 2.4 m.
        Clip("walk", "eead3eb07cb89cb4eb25b3550df5e84b", "A_Walk_F_Masc", loop=True, strideM=1.779),
        Clip("attack", "b963d0c4e093a9c4ea792b8095e51ba2", "A_Attack_HeavyStab01_Sword", impact=0.967, end=1.367),
        Clip("hit", "adc1feaf8563f6247989bffd0737b6ad", "A_Hit_F_React_Sword", impact=0.0, end=0.617),
    ),
    # game/'s roster's grip for the Slayer.
    weapons=(Weapon("404a923dea987c14498264bf7d13d458", palm=0.02, pitch=20, yaw=90),),
    pose="idle",
)
ROLE = "elite"

# Parts by their triangles' dominant bones; limbs split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": ("Head", "Neck", "Eyebrows", "Eyes"),
    "chest": ("Spine_02", "Spine_03"),
    "belly": ("Spine_01",),
    "pelvis": ("Hips",),
}
SIDED = {
    "shoulder": ("Clavicle_*",),
    "upperarm": ("Shoulder_*",),
    "forearm": ("Elbow_*",),
    "hand": ("Hand_*", "Thumb_*", "IndexFinger_*", "Finger_*"),
    "thigh": ("UpperLeg_*",),
    "calf": ("LowerLeg_*",),
    "foot": ("Ankle_*", "Ball_*", "Toes_*"),
}
LIMBS = ("upperarm", "forearm", "thigh", "calf")
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"head": 22, "chest": 24, "belly": 12, "pelvis": 22, "shoulder": 14, "upperarm": 10, "forearm": 12,
           "hand": 10, "thigh": 12, "calf": 12, "foot": 10}
WEAPON = ("SM_Wep_Slayer_01",)
EAR_X = 0.2  # the head's triangles further out to the side than this are the ears
TUSK = {"x": (0.06, 0.13), "y": (2.07, 2.19), "z": 0.28}  # a box round each tusk (either side) in the bind pose
LINE_BUDGET = 90  # feature lines; the contours and the strokes come on top
CAPS = {"head": 14, "chest": 10, "belly": 6, "pelvis": 10, "shoulder": 8, "upperarm": 4, "forearm": 4, "hand": 2,
        "thigh": 4, "calf": 4, "foot": 3}


def principal_axes(points: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """The centre and the axes (rows, longest first) of a point cloud."""
    centre = points.mean(0)
    _, _, axes = np.linalg.svd(points - centre, full_matrices=False)
    return centre, axes


def side_of(name: str) -> str:
    return name.removesuffix("_l").removesuffix("_r")


def cut(src: Source) -> Cut:
    c = Cut(src)
    weapon = src.renderers_named(*WEAPON)
    centroids = src.positions[src.triangles].mean(1)
    left = Selection(centroids[:, 0] < 0)
    ears = src.bones_named(*CENTRE["head"]) & Selection(np.abs(centroids[:, 0]) > EAR_X)
    body = src.all() - weapon - ears
    parts = {name: body & src.bones_named(*bones) for name, bones in CENTRE.items()}
    for name, bones in SIDED.items():
        sel = body & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[side_of(name)])
        if side_of(name) in LIMBS:
            c.contours(sel, name)

    head = src.entry("Head")
    for sel in (ears & left, ears - left):  # each ear from its root at the head's side out to its point
        pts = src.positions[src.vertices_of(sel)]
        out = np.abs(pts[:, 0])
        root, mid = pts[out <= out.min() + 0.02].mean(0), pts[np.abs(out - out.mean()) <= 0.02].mean(0)
        c.stroke([root, mid, pts[out.argmax()]], head, "ears")
    pts = src.positions[src.vertices_of(src.bones_named("Head"))]
    for sign in (-1, 1):  # each tusk from its root up to its tip
        x = sign * pts[:, 0]
        box = pts[(x > TUSK["x"][0]) & (x < TUSK["x"][1]) & (pts[:, 1] > TUSK["y"][0]) & (pts[:, 1] < TUSK["y"][1])
                  & (pts[:, 2] > TUSK["z"])]
        c.stroke([box[box[:, 1].argmin()], box[box[:, 1].argmax()]], head, "tusks")

    # The greatsword: the handle from pommel to guard, the guard across at its widest, and the blade's two edges from
    # the guard to the tip, on the sword's own entry.
    pts = src.positions[src.vertices_of(weapon)]
    centre, axes = principal_axes(pts)
    along, across = (pts - centre) @ axes[0], (pts - centre) @ axes[1]
    hand = src.positions[src.vertices_of(parts["hand_r"])].mean(0)
    if (hand - centre) @ axes[0] > 0:  # the tip is the end away from the hand
        along, across, axes = -along, -across, -axes
    tip, pommel = along.max(), along.min()
    bins = np.linspace(pommel, tip, 21)
    spans = [(across[k].min(), across[k].max()) if (k := (along >= a) & (along < b)).sum() > 1 else (0.0, 0.0)
             for a, b in pairwise(bins)]
    g = int(np.argmax([hi - lo for lo, hi in spans[:10]]))  # the guard, in the hilt's half
    guard_at = 0.5 * (bins[g] + bins[g + 1])
    root_at, mid_at = bins[g + 2], 0.5 * (bins[g + 2] + tip)
    root, mid = spans[g + 2], spans[int((mid_at - pommel) / (bins[1] - bins[0]))]
    entry = src.dominant_entry(weapon)

    def at(s, w):
        return centre + s * axes[0] + w * axes[1]

    c.stroke([at(pommel, 0), at(guard_at, 0)], entry, "weapon", importance=0.4)
    c.stroke([at(guard_at, spans[g][0]), at(guard_at, spans[g][1])], entry, "weapon", importance=0.6)
    c.stroke([at(root_at, root[0]), at(mid_at, mid[0]), at(tip, 0), at(mid_at, mid[1]), at(root_at, root[1])],
             entry, "weapon")

    caps = {name: CAPS[side_of(name)] for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.07, max_turn_deg=60)
    c.break_into({"chest": 2, "pelvis": 2})
    return c
