"""The hero's edge cut (ps-6y2.20; design.md, The Hero): Synty's knight (Polygon Knights, one skin on a humanoid
rig) with the broadsword it holds and the round shield in its left fist, redrawn in white as long lines over a body of
convex parts.

- Pose: the line choice sees it in its sword idle, not its T-pose, so the outline it picks is the one the game shows.
- Body: one hull per part, corners budgeted by how much of the outline the part makes; the sheath at the hip is
  hidden (the sword is in the hand). The shield is a flat disc.
- Lines: the long outline-making creases (lines.choose), chains shorter than 6% of the height dropped, capped per
  part; three contour strokes along each arm, which the low-poly arms give no long crease for (the legs' scribbled
  and cost black share); the shield an
  empty circle; the blade its two edges meeting at the tip and a bright core, a stroke along its middle drawn twice
  (the lines add, so the core is twice as bright as any other line: the brightest thing on screen); the guard and
  the grip.
- Clips (the hero's rig plays them; design.md, The Hero > Animation, and Dashes): the sword idle; the legs' runs, the
  sprint forward (it strides 3.9 m a 0.6 s cycle: 6.5 m/s, near the hero's 6) and the run backward and to each side
  (for an aim the torso can't twist to); Sword Slash's LightCombo01A and LightCombo01B and HeavyFlourish01 (the spin);
  Blade Rush's HeavyStab01, Leap Slam's LightLeaping01, Shield Charge's forward parry and the plain Dash's forward
  dodge. Impacts and ends are demo3d's measurements (the right hand's peak speed); the rig's bake measures the runs'
  strides.
- Shards: a piece per part, the chest and the pelvis split in two, the shield in two.
"""

from itertools import pairwise

import numpy as np

from champions_tools.edgecut import Clip, Cut, Model, Selection, Source, Weapon

MODEL = Model(
    id="hero",
    prefab="360c73b9bf9a34d4c84bc0581d0499eb",
    heightM=1.85,
    clips=(
        Clip("idle", "b94969b4c36c3c94284f72888ad9289d", "A_Idle_Base_Sword", loop=True),
        Clip("run", "d2995d8a62be0ce42a4e73698b62b275", "A_Sprint_F_Masc", loop=True),
        Clip("back", "b28d41dc2454f694fac9ab766e54323a", "A_Run_BckStrafeB_Masc", loop=True),
        Clip("left", "d294a4226e928db4fbbd933c19ccf5a1", "A_Run_FwdStrafeL_Masc", loop=True),
        Clip("right", "4699ac7f1156bfc4dba616d9b3a05234", "A_Run_FwdStrafeR_Masc", loop=True),
        Clip("slashB", "95697c3218923dd43aa160c110bfbc79", "A_Attack_LightCombo01B_Sword", impact=0.267, end=0.45),
        Clip("slashA", "9f773338b2a96174ebe9b63870034ca3", "A_Attack_LightCombo01A_Sword", impact=0.35, end=0.717),
        Clip("spin", "b48c7e37f2264c3418122c894c6f98af", "A_Attack_HeavyFlourish_Sword", impact=1.433, end=1.65),
        Clip("stab", "b963d0c4e093a9c4ea792b8095e51ba2", "A_Attack_HeavyStab01_Sword", impact=0.883, end=0.983),
        Clip("leap", "72f114cf4075d3b4db5ce63cb994a704", "A_Attack_LightLeaping01_Sword", impact=1.1, end=1.167),
        Clip("parry", "7966ebc17e0dc2a4b8faaf62594fb9b2", "A_Parry_F_Sword", impact=0.233, end=0.4),
        Clip("dodge", "80f9aad568ebdbf4f9fd28c054c27a20", "A_Dodge_F_Sword"),
    ),
    hide=("Item_SwordSheath",),
    weapons=(Weapon("f61def225eac3e247985f6679965a655"), Weapon("a7b97d24ba2370040ba159577de7b931", hand="left")),
    pose="idle",
    reviewPoses=("idle", "run", "slashB", "slashA", "spin", "dodge"),
)
ROLE = "hero"

# Parts by their triangles' dominant bones; limbs split by side (the model faces +z, so its left is -x).
CENTRE = {
    "head": ("Head", "Neck", "Eyebrows", "Eyes"),
    "chest": ("Spine_02", "Spine_03"),
    "belly": ("Spine_01",),
    "pelvis": ("Hips", "Root"),
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
LIMBS = ("upperarm", "forearm")
# Hull corners per part (a hull of n corners has 2n - 4 triangles).
CORNERS = {"head": 22, "chest": 24, "belly": 12, "pelvis": 20, "shoulder": 12, "upperarm": 10, "forearm": 10,
           "hand": 8, "thigh": 12, "calf": 12, "foot": 10}
SWORD = ("SM_Wep_Broadsword_01",)
SHIELD = ("SM_Wep_Shield_01",)
LINE_BUDGET = 48  # feature lines; the contours, the circle and the strokes come on top
CAPS = {"head": 12, "chest": 10, "belly": 6, "pelvis": 8, "shoulder": 6, "upperarm": 3, "forearm": 3, "hand": 2,
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
    sword, shield = src.renderers_named(*SWORD), src.renderers_named(*SHIELD)
    centroids = src.positions[src.triangles].mean(1)
    left = Selection(centroids[:, 0] < 0)
    body = src.all() - sword - shield
    parts = {name: body & src.bones_named(*bones) for name, bones in CENTRE.items()}
    for name, bones in SIDED.items():
        sel = body & src.bones_named(*bones)
        parts[name + "_l"], parts[name + "_r"] = sel & left, sel - left
    claimed = src.none()
    for name, sel in parts.items():
        if len(sel & claimed):
            raise ValueError(f"hero: part {name} overlaps another")
        claimed = claimed | sel
    if len(body - claimed):
        raise ValueError(f"hero: {len(body - claimed)} body triangles in no part")
    for name, sel in parts.items():
        c.hull(sel, name, points=CORNERS[side_of(name)])
        if side_of(name) in LIMBS:
            c.contours(sel, name, sides=3)

    # The shield: an empty circle round its face over a dark disc, on its own entry (it rides the left fist).
    pts = src.positions[src.vertices_of(shield)]
    centre, axes = principal_axes(pts)
    radius = 0.5 * np.mean([np.ptp(pts @ axes[0]), np.ptp(pts @ axes[1])])
    entry = src.dominant_entry(shield)
    c.disc(centre, axes[2], 0.96 * radius, entry, "shield")
    c.circle(centre, axes[2], radius, entry, "shield", sides=16)

    # The sword: the grip from pommel to guard, the guard across at its widest, the blade's two edges from the guard
    # to the tip and its core down the middle, twice.
    pts = src.positions[src.vertices_of(sword)]
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
    entry = src.dominant_entry(sword)

    def at(s, w):
        return centre + s * axes[0] + w * axes[1]

    c.stroke([at(pommel, 0), at(guard_at, 0)], entry, "sword", importance=0.4)
    c.stroke([at(guard_at, spans[g][0]), at(guard_at, spans[g][1])], entry, "sword", importance=0.6)
    c.stroke([at(root_at, root[0]), at(mid_at, mid[0]), at(tip, 0), at(mid_at, mid[1]), at(root_at, root[1])],
             entry, "sword")
    core = [at(guard_at, 0.5 * (spans[g][0] + spans[g][1])), at(tip, 0)]
    for _ in range(2):
        c.stroke(core, entry, "blade")

    caps = {name: CAPS[side_of(name)] for name in parts}
    c.feature_lines(parts, LINE_BUDGET, keep=("head",), caps=caps, min_length=0.08, max_turn_deg=60)
    c.break_into({"chest": 2, "pelvis": 2, "shield": 2})
    return c
