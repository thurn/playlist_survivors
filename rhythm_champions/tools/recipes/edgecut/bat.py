"""The bat's edge cut (ps-6y2.9): Magic Pig's Bats LP (2,570 body triangles, 74 entries) redrawn as its wings' bones
over a dark body of convex parts. A 0.3 m flyer: about 19 px tall at game scale and 66 px from wing tip to wing tip
when spread, so it is drawn with very few lines.

- Body: one hull per part, the wing membrane a hull per finger bone so the scallops of its trailing edge stay dark
  between the fingers; the eyes, teeth and inner ears (small separate meshes inside the head) go.
- Lines: no feature lines (the membrane's thin edges come out as scribble at this size). Each wing is one stroke
  along its bones, bending at the joints as the wing flaps: the leading edge from the shoulder over the wrist to the
  long finger's tip; the ears are a stroke each. The second finger and the trailing edge stay unlit (a stroke on the
  second finger took the black share at game scale from 72% to 64%): the dark membrane hides the floor's grid
  behind it.
- Shards: a piece per part.
"""

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source

FBX = "eec0bfdad45f34788ba93083b71f06dc"
MODEL = Model(
    id="bat",
    prefab="4f9257d4aec394d59be5f34d00fa97e8",
    heightM=0.3,
    hoverM=1.3,
    clips=(
        Clip("idle", FBX, "Fly Idle", loop=True),
        Clip("walk", FBX, "Fly Forward", loop=True, strideM=4.4),
        Clip("attack", FBX, "Attack 1", impact=1.1, end=2.0),
        Clip("hit", FBX, "Got Hit", impact=0.067, end=2.0),
    ),
)

# Parts by their triangles' dominant bones; sided parts split by side (the model faces +z, so its left is -x).
CENTRE = {
    "body": ("BatPelvis", "BatRibcage", "BatNeck"),
    "head": ("BatHead", "BatJaw"),
    "tail": ("BatTail*",),
}
SIDED = {
    "ear": ("BatEar*",),
    "arm": ("Bat?ArmCollarbone", "Bat?Arm1"),
    "forearm": ("Bat?Arm2",),
    "thumb": ("Bat?ArmThumb*",),
    "flap": ("BatFlap*",),
    "leg": ("Bat*Thigh*", "Bat*Calf", "Bat*Ankle", "Bat*Toe*", "BatHook*"),
    **{f"finger{f}_{k}": (f"Bat?ArmWing{f}_{k}",) for f in (1, 2) for k in (1, 2, 3, 4)},
}
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"body": 16, "head": 14, "tail": 8, "ear": 6, "arm": 8, "forearm": 8, "thumb": 6, "flap": 10, "leg": 10,
           "finger": 8}
ROLE = "fodder"


def joint(src: Source, name: str) -> np.ndarray:
    """A bone's origin at the reference pose (its bind's inverse)."""
    return np.linalg.inv(src.binds[src.entry(name)])[:3, 3]


def tip(src: Source, name: str) -> np.ndarray:
    """The vertex of the bone's triangles furthest from its origin: a finger's or an ear's tip."""
    pts = src.positions[src.vertices_of(src.bones_named(name))]
    return pts[np.linalg.norm(pts - joint(src, name), axis=1).argmax()]


def chain(c: Cut, src: Source, bones: list[str], part: str) -> None:
    """A stroke through the bones' origins to the last one's tip, each point on its bone."""
    points = [joint(src, b) for b in bones] + [tip(src, bones[-1])]
    c.stroke(points, [src.entry(b) for b in bones] + [src.entry(bones[-1])], part)


def cut(src: Source) -> Cut:
    c = Cut(src)
    skin = src.renderers_named("LP_body")
    centroid_x = src.positions[src.triangles].mean(1)[:, 0]
    left = Selection(centroid_x < 0)
    parts = {name: skin & src.bones_named(*bones) for name, bones in CENTRE.items()}
    for name, bones in SIDED.items():
        sel = skin & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    claimed = src.none()
    for name, sel in parts.items():
        if len(sel & claimed):
            raise ValueError(f"bat: part {name} overlaps another")
        claimed = claimed | sel
    if len(skin - claimed):
        raise ValueError(f"bat: {len(skin - claimed)} body triangles in no part")

    for name, sel in parts.items():
        kind = name.removesuffix("_l").removesuffix("_r")
        c.hull(sel, name, points=CORNERS["finger" if kind.startswith("finger") else kind])

    for side in ("L", "R"):
        s = side.lower()
        palm = f"Bat{side}ArmPalm"
        chain(c, src, [f"Bat{side}Arm1", palm] + [f"Bat{side}ArmWing1_{k}" for k in (2, 3, 4)], f"wing_{s}")
        chain(c, src, [f"BatEar{side}1", f"BatEar{side}2"], f"ear_{s}")
    c.break_into()
    return c
