"""The dragon's edge cut (ps-6y2.12): Magic Pig's Dragon LP (5,444 triangles without its saddle, 108 entries)
redrawn as a big dark shape with a few strong strokes (docs/champions/reference/dragon-duel.jpg). The boss stands
3.39 m with a 12 m wingspan: about 215 px tall and 750 px from wing tip to wing tip at game scale.

- Body: one hull per part, the neck and tail in short runs of bones so they bend; each wing's membrane a hull per
  finger, so the scallops of its trailing edge stay dark between them. The saddle goes.
- Lines: the outline-making creases of the head, neck, body, tail and legs (lines.choose), chains shorter than 8% of
  the height dropped, and one stroke down the back over its spines from the neck to the tail's tip. The wings are a
  few spars: per wing the leading edge from the shoulder over the wrist to the first finger's tip and the other
  three fingers from the wrist, strokes along their bones that bend as the wings beat. The membranes' trailing edges
  stay unlit.
- Clips: the flying clips are authored on the ground, with the takeoff rising 4.43 m by itself and the landing
  falling as far, so fly and land are raised that much (liftM): takeoff, fly and land play in order at the dragon's
  ground position, the dive landing on the marked spot at 1.27 s. The breath's fire starts at 0.4 s; the tail sweep
  crosses the front at 1.15 s; the fireball leaves on Attack 2's snap after it rears, at 1.8 s; hit is Break's
  stagger (bosses take no other hit reaction).
- Shards: 40 pieces, the most a cut has: a piece per part, the head, the neck's base, chest, belly, pelvis and the
  tail's base split in two.
"""

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source

FBX = "367e6650649952543b0c5dc153649036"
FLIGHT = 4.43  # metres: the takeoff's rise, the height the fly and land clips are played at
MODEL = Model(
    id="dragon",
    prefab="a1e28cc5277b74ff0ad8566e5e54bee5",
    heightM=3.39,
    hide=("saddle_LP1",),
    clips=(
        Clip("idle", FBX, "Idle", loop=True),
        Clip("walk", FBX, "Walk", loop=True, strideM=2.13),
        Clip("breath", FBX, "Breathe Fire", impact=0.4, end=2.667),
        Clip("tail", FBX, "Tail Whip R", impact=1.15, end=2.167),
        Clip("fireball", FBX, "Attack 2", impact=1.8, end=2.5),
        Clip("takeoff", FBX, "Idle Takeoff", end=2.667),
        Clip("fly", FBX, "Fly Idle", loop=True, liftM=FLIGHT),
        Clip("land", FBX, "Idle Landing", impact=1.27, end=2.5, liftM=FLIGHT),
        Clip("hit", FBX, "Got Hit 1", impact=0.067, end=0.833),
    ),
    reviewPoses=("idle", "walk", "breath", "tail", "fireball", "fly", "land"),
)

D = "English Dragon"
# Parts by their triangles' dominant bones; sided parts split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": (f"{D}Head", f"{D}Jaw"),
    "neck_a": (f"{D}Neck1", f"{D}Neck2"),
    "neck_b": (f"{D}Neck3", f"{D}Neck4", f"{D}Neck5"),
    "chest": (f"{D}Ribcage", f"{D}Spine3", f"{D}?ArmCollarbone", f"{D}?WingCollarbone"),
    "belly": (f"{D}Spine1", f"{D}Spine2"),
    "pelvis": (f"{D}Pelvis",),
    "tail_a": (f"{D}Tail1", f"{D}Tail2"),
    "tail_b": (f"{D}Tail3", f"{D}Tail4"),
    "tail_c": (f"{D}Tail5", f"{D}Tail6"),
    "tail_d": (f"{D}Tail7", f"{D}Tail8", f"{D}Tail9"),
}
SIDED = {
    "thigh": (f"{D}?Leg1",),
    "shin": (f"{D}?Leg2",),
    "foot": (f"{D}?Leg3", f"{D}?LegPalm", "?LegToe*", "?LegPoint*"),
    "arm": (f"{D}?Arm1",),
    "forearm": (f"{D}?Arm2",),
    "hand": (f"{D}?Arm3", f"{D}?ArmPalm", "?HandToe*", "?ArmPoint*"),
    "wingarm": (f"{D}?Wing1",),
    "wingfore": (f"{D}?Wing2", f"{D}Wthumb?"),
    **{f"membrane{f}": (f"{D}?W{f}?",) for f in (1, 2, 3, 4)},
}
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"head": 18, "neck": 10, "chest": 20, "belly": 14, "pelvis": 14, "tail": 8, "thigh": 10,
           "shin": 8, "foot": 10, "arm": 8, "forearm": 8, "hand": 10, "wingarm": 8, "wingfore": 8, "membrane": 12}
ROLE = "boss"
LINE_BUDGET = 200  # feature lines; the spars come on top
CAPS = {"head": 24, "neck_a": 12, "neck_b": 12, "chest": 20, "belly": 16, "pelvis": 12, "tail_a": 10, "tail_b": 10,
        "tail_c": 10, "tail_d": 12, **{f"{p}_{s}": n for p, n in (("thigh", 8), ("shin", 6), ("foot", 6), ("arm", 4),
                                                                  ("forearm", 4), ("hand", 4)) for s in "lr"}}
SPLITS = {"head": 2, "neck_a": 2, "chest": 2, "belly": 2, "pelvis": 2, "tail_a": 2}


def joint(src: Source, name: str) -> np.ndarray:
    """A bone's origin at the reference pose (its bind's inverse)."""
    return np.linalg.inv(src.binds[src.entry(name)])[:3, 3]


def tip(src: Source, name: str, came: np.ndarray) -> np.ndarray:
    """A finger's tip: of the bone's vertices ahead of its origin (along the way the finger came), the furthest."""
    origin = joint(src, name)
    pts = src.positions[src.vertices_of(src.bones_named(name))]
    d = pts - origin
    length = np.linalg.norm(d, axis=1)
    ahead = d @ came / np.maximum(length, 1e-9) > 0.9
    return pts[ahead][length[ahead].argmax()] if ahead.any() else origin + came * length.max()


def spar(c: Cut, src: Source, bones: list[str], part: str, importance: float = 1.0) -> None:
    """A stroke through the bones' origins to the last one's tip, each point on its bone."""
    points = [joint(src, b) for b in bones]
    came = points[-1] - points[-2]
    points.append(tip(src, bones[-1], came / np.linalg.norm(came)))
    c.stroke(points, [src.entry(b) for b in bones] + [src.entry(bones[-1])], part, importance)


def ridge(c: Cut, src: Source, bones: list[str], part: str) -> None:
    """A stroke along the back: through each bone's highest vertex near its middle line (its spines' tips), to the
    last bone's tip, each point on its bone."""
    points = []
    for b in bones:
        pts = src.positions[src.vertices_of(src.bones_named(b))]
        middle = np.abs(pts[:, 0] - joint(src, b)[0]) < 0.04 * src.height
        points.append(pts[middle][pts[middle][:, 1].argmax()] if middle.any() else joint(src, b))
    came = joint(src, bones[-1]) - joint(src, bones[-2])
    points.append(tip(src, bones[-1], came / np.linalg.norm(came)))
    c.stroke(points, [src.entry(b) for b in bones] + [src.entry(bones[-1])], part)


def cut(src: Source) -> Cut:
    c = Cut(src)
    body = src.all()
    centroid_x = src.positions[src.triangles].mean(1)[:, 0]
    left = Selection(centroid_x < 0)
    parts = {name: body & src.bones_named(*bones) for name, bones in CENTRE.items()}
    for name, bones in SIDED.items():
        sel = body & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    claimed = src.none()
    for name, sel in parts.items():
        if len(sel & claimed):
            raise ValueError(f"dragon: part {name} overlaps another")
        claimed = claimed | sel
    if len(body - claimed):
        raise ValueError(f"dragon: {len(body - claimed)} body triangles in no part")

    for name, sel in parts.items():
        # The corners by kind: neck_a's are the neck's, membrane2_l's the membrane's.
        kind = name.removesuffix("_l").removesuffix("_r").split("_")[0].rstrip("1234")
        c.hull(sel, name, points=CORNERS[kind])

    # The wings: the leading edge from the shoulder to the first finger's tip, then the other three fingers.
    for side in ("L", "R"):
        s = side.lower()
        spar(c, src, [f"{D}{side}Wing1", f"{D}{side}Wing2"] + [f"{D}{side}W1{k}" for k in (1, 2, 3)], f"membrane1_{s}")
        for f in (2, 3, 4):
            spar(c, src, [f"{D}{side}W{f}{k}" for k in (1, 2, 3)], f"membrane{f}_{s}", importance=0.8)

    # The back: one stroke from the head down the neck, over the back and along the tail to its tip.
    ridge(c, src, [f"{D}Neck{k}" for k in (5, 4, 3, 2, 1)] + [f"{D}Spine{k}" for k in (3, 2, 1)] + [f"{D}Pelvis"]
          + [f"{D}Tail{k}" for k in range(1, 10)], "tail_b")

    caps = {name: CAPS.get(name, 0) for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.08, min_salience=0.15, tolerance=0.02)
    c.break_into(SPLITS)
    return c
