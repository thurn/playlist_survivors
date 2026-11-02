"""The troll's edge cut (ps-6y2.11): Synty's Troll (Fantasy Rivals, 4,034 triangles in its active skin, a humanoid rig)
with its spiked club in its right fist, redrawn as long lines and a few strokes over a body of convex parts.

- Pose: the line choice sees it in its sword idle, not its T-pose, so the outline it picks is the one the game shows.
- Body: one hull per part, corners budgeted by how much of the outline the part makes. The club has none: a hull
  over its spikes would be a fat blob hiding its own outline.
- Lines: the long outline-making creases (lines.choose), chains shorter than 7% of the height dropped, capped per part;
  four contour strokes along each arm and leg, which the chunky low-poly limbs give no long crease for; the tusks as
  strokes; the club as its handle, its head's outline and a band round its widest.
- Clips: Synty's sword idle and masculine walk; the attack is the leaping slam, its wind-up and leap the slam's
  telegraph and its landing blow (the impact) the slam.
- Shards: a piece per part, the chest and the pelvis split in two; the club's strokes join the right hand's.
"""

from itertools import pairwise

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source, Weapon

MODEL = Model(
    id="troll",
    prefab="2250a4a4fc528684c85ba955eb992ec6",
    heightM=2.6,
    clips=(
        Clip("idle", "b94969b4c36c3c94284f72888ad9289d", "A_Idle_Base_Sword", loop=True),
        # The stride is game/'s roster's 2.15 m at its 2.9 m, scaled to 2.6 m.
        Clip("walk", "eead3eb07cb89cb4eb25b3550df5e84b", "A_Walk_F_Masc", loop=True, strideM=1.928),
        Clip("attack", "72f114cf4075d3b4db5ce63cb994a704", "A_Attack_LightLeaping01_Sword", impact=1.033, end=1.567),
        # game/'s roster ended this hit at 0.1 s; the big ork's end for the same length of react is kept instead.
        Clip("hit", "202a1ac9f04ce2a419bc2bdd6b148b2a", "A_Hit_L_React_Sword", impact=0.0, end=0.617),
    ),
    # game/'s roster's grip for the club.
    weapons=(Weapon("39e0fb4b243ffb645a622d1945004ee5", palm=0.02, pitch=20, yaw=90),),
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
CORNERS = {"head": 20, "chest": 26, "belly": 12, "pelvis": 22, "shoulder": 10, "upperarm": 10, "forearm": 12,
           "hand": 10, "thigh": 12, "calf": 12, "foot": 10}
WEAPON = ("SM_Wep_Troll_01",)
TUSK = {"x": (0.06, 0.14), "y": (2.22, 2.37), "z": 0.30}  # a box round each tusk (either side) in the bind pose
LINE_BUDGET = 90  # feature lines; the contours and the strokes come on top
CAPS = {"head": 14, "chest": 12, "belly": 6, "pelvis": 10, "shoulder": 6, "upperarm": 4, "forearm": 4, "hand": 2,
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
    body = src.all() - weapon
    parts = {name: body & src.bones_named(*bones) for name, bones in CENTRE.items()}
    for name, bones in SIDED.items():
        sel = body & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[side_of(name)])
        if side_of(name) in LIMBS:
            c.contours(sel, name)

    head = src.entry("Head")
    pts = src.positions[src.vertices_of(src.bones_named("Head"))]
    for sign in (-1, 1):  # each tusk from its root up to its tip
        x = sign * pts[:, 0]
        box = pts[(x > TUSK["x"][0]) & (x < TUSK["x"][1]) & (pts[:, 1] > TUSK["y"][0]) & (pts[:, 1] < TUSK["y"][1])
                  & (pts[:, 2] > TUSK["z"])]
        c.stroke([box[box[:, 1].argmin()], box[box[:, 1].argmax()]], head, "tusks")

    # The club: the handle from its butt to the head (where the club widens past twice the handle), the head's outline
    # in the club's broad plane and a band round its widest, on the club's own entry.
    pts = src.positions[src.vertices_of(weapon)]
    centre, axes = principal_axes(pts)
    rel = pts - centre
    along, across, deep = rel @ axes[0], rel @ axes[1], rel @ axes[2]
    hand = src.positions[src.vertices_of(parts["hand_r"])].mean(0)
    if (hand - centre) @ axes[0] > 0:  # the head is the end away from the hand
        along, across, axes = -along, -across, -axes
    butt, top = along.min(), along.max()
    bins = np.linspace(butt, top, 21)
    width = np.array([np.ptp(across[k]) if (k := (along >= a) & (along < b)).sum() > 1 else 0.0
                      for a, b in pairwise(bins)])
    handle = np.median(width[:6])
    start = bins[next(i for i in range(len(width)) if width[i] > 2 * handle and bins[i] > 0.5 * (butt + top) - 0.5)]
    entry = src.dominant_entry(weapon)
    h = along >= start
    widest = start + (top - start) * 0.5
    span = across[h & (np.abs(along - widest) < 0.1 * (top - start))]
    thick = deep[h & (np.abs(along - widest) < 0.1 * (top - start))]
    base = across[h & (along < start + 0.1 * (top - start))]
    tip = across[h & (along > top - 0.1 * (top - start))]

    def at(s, w, d=0.0):
        return centre + s * axes[0] + w * axes[1] + d * axes[2]

    c.stroke([at(butt, 0), at(start, 0)], entry, "club", importance=0.5)
    outline = [at(start, base.min()), at(widest, span.min()), at(top, tip.min()), at(top, tip.max()),
               at(widest, span.max()), at(start, base.max())]
    c.stroke(outline + outline[:1], entry, "club")
    radius = 0.5 * (0.5 * np.ptp(span) + 0.5 * np.ptp(thick))
    c.circle(at(widest, 0.5 * (span.min() + span.max()), 0.5 * (thick.min() + thick.max())), axes[0], radius, entry,
             "club", sides=8, importance=0.6)

    caps = {name: CAPS[side_of(name)] for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.07, max_turn_deg=60)
    c.break_into({"chest": 2, "pelvis": 2})
    return c
