"""The demon's edge cut (ps-6y2.10): Magic Pig's Demons LP (5,992 triangles in its prefab's active renderers, 59
entries) redrawn as a few dozen lines over a body of convex parts.

- Body: one hull per part, corners budgeted by how much of the outline the part makes at game scale (the chest,
  head and long arms most). The armour lies inside its part's hull.
- Lines: the outline-making creases of the long arms and a few of the chest, pelvis and calves (lines.choose),
  chains shorter than 6% of the height dropped. The demon's name is its horns and claws: the two tall pairs of
  horns on the crown are strokes along their spines (the small face horns go), and the fingers have no body, only a
  stroke from the palm to each of the three long claws (the thumb goes).
- Shards: a piece per part, the chest in three, the belly, the pelvis and the forearms in two.
"""

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source

FBX = "bebd353bcd71a47dabd72141665125c1"
MODEL = Model(
    id="demon",
    prefab="97be6194801464926b2c78020bf4eb32",
    heightM=1.9,
    clips=(
        Clip("idle", FBX, "idle", loop=True),
        Clip("walk", FBX, "walk", loop=True, strideM=1.477),
        Clip("attack", FBX, "attack1", impact=0.35, end=1.233),
        Clip("hit", FBX, "hit reaction", impact=0.067, end=1.25),
    ),
)

# Parts by their triangles' dominant bones; limbs split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": ("Head", "Jaw", "Neck"),
    "chest": ("Spine2", "Spine1", "LeftShoulder", "RightShoulder"),
    "belly": ("Spine",),
    "pelvis": ("Hips",),
}
SIDED = {
    "upperarm": ("LeftArm", "RightArm"),
    "forearm": ("*ForeArm",),
    "hand": ("*Hand",),
    "thigh": ("*UpLeg",),
    "calf": ("LeftLeg", "RightLeg"),
    "foot": ("*Foot", "*ToeBase", "*FootClaw*"),
}
CORNERS = {"head": 18, "chest": 22, "belly": 12, "pelvis": 16, "upperarm": 12, "forearm": 14, "hand": 12,
           "thigh": 8, "calf": 10, "foot": 10}
HORNS = ("AHorns_*",)
FINGERS = ("*HandThumb*", "*HandIndex*", "*HandMiddle*", "*HandRing*")
DRAWN_HORNS = ("AHorns_A1", "AHorns_A3")  # the two tall pairs on the crown; the small face horns go
CLAWS = ("Index", "Middle", "Ring")
ROLE = "fodder"
LINE_BUDGET = 32  # feature lines; the horns and claws come on top
CAPS = {"head": 4, "chest": 4, "pelvis": 2, "upperarm_l": 5, "upperarm_r": 5, "forearm_l": 5, "forearm_r": 5,
        "calf_l": 2, "calf_r": 2}  # others none


def spine(points: np.ndarray, base: np.ndarray, steps: int) -> list[np.ndarray]:
    """A horn's or a claw's spine in `steps` segments: from its root (the mean of the points in the tenth nearest `base`, where
    it leaves the head or the palm) through the means of the points at even distances between to its tip (the farthest point)."""
    d = np.linalg.norm(points - base, axis=1)
    lo, span = d.min(), np.ptp(d)
    out = [points[d <= lo + 0.1 * span].mean(0)]
    for k in range(1, steps):
        out.append(points[np.abs(d - (lo + k / steps * span)) <= 0.5 * span / steps].mean(0))
    out.append(points[d.argmax()])
    return out


def cut(src: Source) -> Cut:
    c = Cut(src)
    horns, fingers = src.renderers_named(*HORNS), src.bones_named(*FINGERS)
    body = src.all() - horns - fingers
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
            raise ValueError(f"demon: part {name} overlaps another")
        claimed = claimed | sel
    if len(body - claimed):
        raise ValueError(f"demon: {len(body - claimed)} body triangles in no part")

    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[name.removesuffix("_l").removesuffix("_r")])

    # The horns: each renderer holds a pair, one each side; a stroke along each horn's spine, on the head.
    head_centre = src.positions[src.vertices_of(parts["head"])].mean(0)
    entry = src.dominant_entry(horns)
    for name in DRAWN_HORNS:
        sel = src.renderers_named(name)
        for side in (sel & left, sel - left):
            pts = src.positions[src.vertices_of(side)]
            base = pts[np.linalg.norm(pts - head_centre, axis=1).argmin()]
            c.stroke(spine(pts, base, 2), entry, "horns")

    # The claws: no body, a stroke along each long finger from the palm, on its middle bone (the thumb goes).
    for side, prefix in (("l", "Left"), ("r", "Right")):
        palm = src.positions[src.vertices_of(parts["hand_" + side])].mean(0)
        for finger in CLAWS:
            pts = src.positions[src.vertices_of(fingers & src.bones_named(f"{prefix}Hand{finger}*"))]
            base = pts[np.linalg.norm(pts - palm, axis=1).argmin()]
            c.stroke(spine(pts, base, 1), src.entry(f"{prefix}Hand{finger}2"), "claws_" + side, importance=0.7)

    caps = {name: CAPS.get(name, 0) for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.06)
    c.break_into({"chest": 3, "belly": 2, "pelvis": 2, "forearm_l": 2, "forearm_r": 2})
    return c
