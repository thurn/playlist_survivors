"""Act 2's kit, the Sunken Sands (ps-8z4.16; boss-rush.md, Environments): ruins in a dark desert in the dim amber key.

Designed by eye against Synty's POLYGON Ancient Egypt (its ornate lotus pillars, obelisks, limestone blocks and rubble,
braziers, canopic jars, pyramid blocks, sand dunes) and NatureBiomes AridDesert (cacti, spiked rocks, background
hills), redrawn from scratch as simple faceted volumes whose every edge draws (design.md, The aim): big closed facets,
closed silhouettes, nothing hairline. Numpy only, no vendor input. Content/arenas.json's kit says which pieces are props
(each its prop kind's footprint and height), which the floor scatters and which the backdrop stands round the far walls.
Bake: `just champions-tool kit bake sunkenSands`; stills: `just champions-tool stills --scene kit-sunkenSands`.
"""

from __future__ import annotations

import math

import numpy as np

from champions_tools.structure import Builder, Mesh, box, loft, panel, polygon, prism, rect, ring, slab

UP = np.array([0.0, 1.0, 0.0])


# ---- Shared shapes ----

def peak(b: Builder, rim: list[int], apex) -> None:
    """A ring closed up (or down) to a point: one triangle a side, each facing away from the ring's middle."""
    a = b.vert(apex)
    mid = sum(b.points[i] for i in rim) / len(rim)
    tip = np.asarray(apex, dtype=np.float64)
    for i in range(len(rim)):
        p, q = rim[i], rim[(i + 1) % len(rim)]
        out = (b.points[p] + b.points[q]) / 2 - mid
        b.poly([p, a, q], facing=out + 0.3 * (tip - mid) / max(1e-9, float(np.linalg.norm(tip - mid))))


def spike(b: Builder, foot, lean_deg: float, heading_deg: float, radius: float, length: float, sides: int = 5,
          tip: float = 0.45) -> None:
    """A faceted spike of rock: a prism along a tilted axis, closed under, into a point a `tip` share of its length."""
    lean, head = math.radians(lean_deg), math.radians(heading_deg)
    axis = np.array([math.sin(lean) * math.cos(head), math.cos(lean), math.sin(lean) * math.sin(head)])
    side = np.cross(axis, UP if abs(axis[1]) < 0.95 else np.array([1.0, 0.0, 0.0]))
    side /= np.linalg.norm(side)
    other = np.cross(side, axis)
    base = np.array([foot[0], radius * math.sin(lean) + 0.01, foot[1]])
    shoulder = base + axis * length * (1 - tip)

    def around(c, r):
        return [c + r * (math.cos(-2 * math.pi * i / sides) * side + math.sin(-2 * math.pi * i / sides) * other)
                for i in range(sides)]

    idx = loft(b, [around(base, radius), around(shoulder, radius * 0.85)], top=False, bottom=True)
    peak(b, idx[-1], base + axis * length)


def tilted(points, pivot, deg: float) -> list[np.ndarray]:
    """Points turned about the z axis through `pivot` by `deg`, counter-clockwise seen from +z (lowering -x's side)."""
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    out = []
    for p in points:
        d = np.asarray(p, dtype=np.float64) - pivot
        out.append(pivot + np.array([d[0] * c - d[1] * s, d[0] * s + d[1] * c, d[2]]))
    return out


# ---- Props: each its prop kind's footprint (arenas.json) and exactly its height ----

def lotus_column() -> Mesh:
    """The pillar's dressing (r 0.9, 3.2 m, unbreakable, so drawn grey): Ancient Egypt's ornate pillars. A square
    plinth, an eight-sided shaft tapering from 0.5 to 0.42 m with two bands near its foot and two under the capital,
    a papyrus capital flaring to 0.66 m and a square abacus on top. Standing whole, it reads as a temple's column from
    the high camera; its abacus is the square a pillar's footprint is."""
    b = Builder()
    box(b, 0, 0, 1.24, 1.24, 0.0, 0.28)
    shaft = [ring(polygon(0, 0, r, 8, 22.5), y) for r, y in
             ((0.5, 0.28), (0.5, 0.42), (0.47, 0.5), (0.47, 0.62), (0.44, 0.7), (0.42, 2.05), (0.45, 2.13), (0.45, 2.24),
              (0.42, 2.32))]
    loft(b, shaft, top=False)
    loft(b, [ring(polygon(0, 0, 0.42, 8, 22.5), 2.32), ring(polygon(0, 0, 0.66, 8, 22.5), 2.88)], top=True)
    box(b, 0, 0, 1.0, 1.0, 2.88, 3.2)
    return b.mesh()


def block_stack() -> Mesh:
    """The crate's dressing (1.6 x 1.6 m, 1.4 m, two blows, so drawn in the key): Ancient Egypt's limestone stacks.
    Three dressed blocks: two side by side on the sand, one turned a little across them on top, so the stack keeps
    three clear outlines from above."""
    b = Builder()
    box(b, -0.36, 0.02, 0.7, 1.4, 0.0, 0.7, yaw_deg=-4)
    box(b, 0.4, -0.06, 0.68, 1.3, 0.0, 0.66, yaw_deg=6)
    box(b, 0.02, 0.05, 1.3, 0.62, 0.7, 1.4, yaw_deg=14)
    return b.mesh()


def brazier() -> Mesh:
    """The post's dressing (r 0.35, 1.6 m, one blow, so drawn in the key): Ancient Egypt's braziers. A square foot, a
    slim post, a six-sided bowl flaring to 0.33 m and three tongues of flame rising out of it to 1.6 m."""
    b = Builder()
    prism(b, polygon(0, 0, 0.26, 4, 45), 0.0, 0.12)
    loft(b, [ring(polygon(0, 0, 0.09, 4, 45), 0.12), ring(polygon(0, 0, 0.06, 4, 45), 1.08)], top=False)
    loft(b, [ring(polygon(0, 0, 0.12, 6), 1.08), ring(polygon(0, 0, 0.33, 6), 1.3)], top=True, bottom=True)
    for (x, z), h, r, turn in (((0.0, 0.0), 1.6, 0.13, 0), ((0.14, 0.08), 1.5, 0.09, 40), ((-0.12, 0.1), 1.46, 0.08, 15)):
        foot = [b.vert(p) for p in ring(polygon(x, z, r, 3, turn), 1.3)]
        peak(b, foot, (x * 1.2, h, z * 1.2))
    return b.mesh()


def fallen_obelisk() -> Mesh:
    """The slab's dressing (4.0 x 0.8 m, 1.1 m, unbreakable, so drawn grey): a toppled obelisk (Ancient Egypt's). Its
    shaft lies along x, 0.7 m deep and narrowing from 0.76 to 0.58 m, its pyramidion at the east end resting on the
    sand and its broken foot propped on a block so its top rises to 1.1 m; one long cartouche cut along its upper
    face (three evenly spaced ones read as a carriage's windows to the blind critique)."""
    x0, x1, tip, h, w0, w1, height = -1.9, 1.45, 1.98, 0.7, 0.76, 0.58, 1.1
    pivot = np.array([x1, 0.0, 0.0])  # the shaft's head rests on the sand at its lower edge

    def section(x, w, lift):
        return tilted([np.array([x, y, z]) for y, z in ((0, -w / 2), (0, w / 2), (h, w / 2), (h, -w / 2))], pivot, -lift)

    lo, hi = 0.0, 30.0  # the lift that raises the foot's upper edge to the kind's height
    for _ in range(60):
        lift = (lo + hi) / 2
        lo, hi = (lift, hi) if section(x0, w0, lift)[2][1] < height else (lo, lift)
    foot, head = section(x0, w0, lift), section(x1, w1, lift)
    apex = tilted([np.array([tip, h / 2, 0.0])], pivot, -lift)[0]
    b = Builder()
    fi, hi_ = [b.vert(p) for p in foot], [b.vert(p) for p in head]
    b.poly([fi[0], fi[1], hi_[1], hi_[0]], facing=(0, -1, 0))  # the underside
    b.poly([fi[1], fi[2], hi_[2], hi_[1]], facing=(0, 0, 1))
    b.poly([fi[3], fi[0], hi_[0], hi_[3]], facing=(0, 0, -1))
    b.poly(fi, facing=(-1, 0, 0))
    peak(b, hi_, apex)
    # The upper face with its cartouche: u along the shaft, v across it, facing up.
    u = head[3] - foot[3]
    length = float(np.linalg.norm(u))
    inset = (w0 - w1) / 2
    panel(b, foot[3], u / length, np.array([0.0, 0.0, 1.0]), [(0, 0), (0, w0), (length, w0 - inset), (length, inset)],
          openings=[(0.4, 0.27, 2.9, 0.48)], depth=0.05)
    # The block it fell across, under the raised foot.
    box(b, x0 + 0.42, 0.0, 0.62, 0.92, 0.0, foot[0][1] + 0.02, yaw_deg=8)
    return b.mesh()


def obelisk() -> Mesh:
    """An obelisk standing (0.9 x 0.9 m, 3.8 m, unbreakable, grey): Ancient Egypt's. A stepped base, a square shaft
    tapering from 0.56 to 0.4 m under a pyramidion, a column of three glyph slots cut into the face toward the camera."""
    b = Builder()
    box(b, 0, 0, 0.9, 0.9, 0.0, 0.16)
    box(b, 0, 0, 0.74, 0.74, 0.16, 0.3)
    w0, w1, y0, y1, top = 0.56, 0.4, 0.3, 3.4, 3.8
    L = ring([(-w0 / 2, -w0 / 2), (-w0 / 2, w0 / 2), (w0 / 2, w0 / 2), (w0 / 2, -w0 / 2)], y0)
    H = ring([(-w1 / 2, -w1 / 2), (-w1 / 2, w1 / 2), (w1 / 2, w1 / 2), (w1 / 2, -w1 / 2)], y1)
    o = L[0]
    u = np.array([1.0, 0.0, 0.0])
    up = H[0] - L[0]
    up = up - np.dot(up, u) * u
    slant = float(np.linalg.norm(up))
    v = up / slant
    inset = (w0 - w1) / 2
    panel(b, o, u, v, [(0, 0), (inset, slant), (w0 - inset, slant), (w0, 0)],
          openings=[(0.19, 1.95, 0.37, 2.35), (0.2, 1.35, 0.36, 1.7), (0.21, 0.75, 0.35, 1.1)], depth=0.04)
    for a, c in ((0, 1), (1, 2), (2, 3)):
        b.poly([L[a], H[a], H[c], L[c]], facing=(L[a] + L[c]) / 2 * np.array([1, 0, 1]))
    peak(b, [b.vert(p) for p in H], (0.0, top, 0.0))
    return b.mesh()


def cactus() -> Mesh:
    """A saguaro (r 0.35, 2.6 m, one blow, in the key): AridDesert's cacti. A six-sided trunk rounded to a point at
    2.6 m and two arms, each out level and then up, one high and one low, so it reads as a cactus from any side."""
    b = Builder()
    trunk = loft(b, [ring(polygon(0, 0, 0.2, 6), 0.0), ring(polygon(0, 0, 0.18, 6), 2.25)], top=False)
    peak(b, trunk[-1], (0.0, 2.6, 0.0))
    for side, y, out, up in ((1, 0.9, 0.5, 1.75), (-1, 1.35, 0.42, 2.05)):
        # The elbow: a six-sided prism lying along x from inside the trunk out to the arm's foot.
        x0, x1, r = side * 0.1, side * out, 0.12
        rings = []
        for x in (x0, x1):
            rings.append([np.array([x, y + r * math.cos(a), r * math.sin(a)])
                          for a in (2 * math.pi * k / 6 * side for k in range(6))])
        loft(b, rings, top=True, bottom=True)
        arm = loft(b, [ring(polygon(x1, 0, 0.12, 6), y + r * 0.6), ring(polygon(x1, 0, 0.11, 6), up - 0.2)], top=False)
        peak(b, arm[-1], (x1, up, 0.0))
    return b.mesh()


def rock_spire() -> Mesh:
    """Spiked rocks (r 0.8, 2.6 m, unbreakable, grey): AridDesert's Rocks_Spikes. Three five-sided spikes of stone
    leaning out of a low boulder, the tallest to 2.6 m, the others fanned so each point keeps its own silhouette."""
    b = Builder()
    loft(b, [ring(polygon(0, 0, 0.78, 7, 10), 0.0), ring(polygon(0.04, 0, 0.55, 7, 30), 0.34)], top=True)
    spike(b, (0.05, 0.05), 6, 90, 0.3, 2.58, tip=0.5)
    spike(b, (-0.32, -0.1), 26, 200, 0.22, 1.7)
    spike(b, (0.34, -0.22), 30, 330, 0.18, 1.3)
    return b.mesh()


def frond(b: Builder, root, heading_deg: float, knee, tip, width: float, thick: float = 0.03) -> None:
    """A palm frond: two long diamonds from the root up to its knee and out down to its tip, each a thin slab."""
    h = math.radians(heading_deg)
    out = np.array([math.cos(h), 0.0, -math.sin(h)])
    side = np.cross(UP, out)
    r = np.asarray(root, dtype=np.float64)
    k = r + out * knee[0] + UP * knee[1]
    t = r + out * tip[0] + UP * tip[1]
    for a, c, w in ((r, k, width), (k, t, width * 0.75)):
        m = (a + c) / 2
        normal = np.cross(c - a, side)
        if normal[1] < 0:
            normal = -normal
        flat(b, [a, m + side * w / 2, c, m - side * w / 2], thick, down=-normal)


def date_palm() -> Mesh:
    """A date palm (r 0.3, 3.4 m, one blow, in the key): Ancient Egypt's and the oases' palms. A six-sided trunk leaning
    as it rises, ringed by three bands of bark, under a crown of eight long drooping fronds, the highest knee at 3.4 m.
    (A canopic jar here first was a small egg at game scale, easy to take for a pickup.)"""
    b = Builder()
    pts = [(0.0, 0.0, 0.2), (0.06, 0.75, 0.17), (0.14, 1.5, 0.15), (0.24, 2.25, 0.14), (0.36, 3.0, 0.12)]
    rings = []
    for k, (x, y, r) in enumerate(pts):
        rings.append(ring(polygon(x, 0, r, 6), y))
        if 0 < k < len(pts) - 1:  # a bark band: the trunk steps out and back
            rings.append(ring(polygon(x, 0, r * 1.18, 6), y + 0.06))
            rings.append(ring(polygon(x, 0, r, 6), y + 0.12))
    loft(b, rings, top=True)
    crown = (0.36, 3.0, 0.0)
    for k in range(8):
        lift = 0.4 if k % 2 == 0 else 0.25
        frond(b, crown, 360 * k / 8 + 10, (0.8, lift), (1.6, -0.55 + lift / 2), 0.32)
    return b.mesh()


# ---- The floor treatment: low, never solid, scattered over open floor ----

def flat(b: Builder, corners, thick: float, down=(0, -1, 0)) -> None:
    """A planar polygon made a slab `thick` deep along `down`, wound so its face looks away from `down`."""
    c = [np.asarray(p, dtype=np.float64) for p in corners]
    if np.dot(np.cross(c[1] - c[0], c[2] - c[0]), down) < 0:
        c.reverse()
    slab(b, c, thick, down=down)


def strip(b: Builder, path, widths, thick: float) -> None:
    """A flat tapering strip along a ground path, `thick` proud of the grid: its top quads share their joints in one
    plane and its rim walls run only along its two sides and ends, so only its outline draws (slabs per segment drew
    every joint)."""
    pts = [np.array([x, 0.0, z]) for x, z in path]
    lefts, rights = [], []
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]
        c = pts[min(len(pts) - 1, i + 1)]
        d = (c - a) / np.linalg.norm(c - a)
        side = np.cross(UP, d)
        lefts.append(p + side * widths[i] / 2)
        rights.append(p - side * widths[i] / 2)
    top = [(l + UP * thick, r + UP * thick) for l, r in zip(lefts, rights, strict=True)]
    for i in range(len(pts) - 1):
        b.poly([top[i][0], top[i + 1][0], top[i + 1][1], top[i][1]], facing=UP)
        for k, sign in ((0, 1), (1, -1)):
            foot = (lefts, rights)[k]
            out = (foot[i] + foot[i + 1]) / 2 - (pts[i] + pts[i + 1]) / 2
            b.poly([foot[i], foot[i + 1], top[i + 1][k], top[i][k]], facing=out)
    for i, d in ((0, pts[0] - pts[1]), (len(pts) - 1, pts[-1] - pts[-2])):
        b.poly([lefts[i], rights[i], top[i][1], top[i][0]], facing=d)


def sand_ripples() -> Mesh:
    """Wind ripples in the sand (Ancient Egypt's and AridDesert's dunes): two long shallow arcs 5 m across, side by
    side, each a flat crescent strip thick in the middle and pointed at both ends, 4 cm proud of the grid. Three short
    arcs read as a slash's motion streaks to the blind critique; two long ones lie on the ground."""
    b = Builder()
    for k, (dz, span) in enumerate(((-0.45, 4.6), (0.45, 5.2))):
        xs = np.linspace(-span / 2, span / 2, 9)
        path = [(x + 0.3 * k, dz + 0.3 * (1 - (2 * x / span) ** 2)) for x in xs]
        widths = [0.05, 0.16, 0.26, 0.32, 0.34, 0.32, 0.26, 0.16, 0.05]
        strip(b, path, widths, 0.04)
    return b.mesh()


def sand_drift() -> Mesh:
    """A drift of sand blown over the floor (the packs' sand ground patches): one broad flat patch 4.6 m long and 2 m
    wide, 3 cm proud of the grid, its outline ten irregular sides, nothing inside. Small paving stones in its place
    read as cards or pickups to the blind critique, and a hooked crescent as a claw mark; a large flat patch reads as
    ground, as act 1's moss does."""
    b = Builder()
    radii = [2.3, 1.9, 1.5, 1.7, 2.1, 2.2, 1.8, 1.4, 1.6, 2.0]
    corners = []
    for i, r in enumerate(radii):
        a = 2 * math.pi * i / len(radii)
        corners.append((r * math.cos(a), 0.03, -r * 0.48 * math.sin(a)))
    # Its top fanned from its middle into triangles whose seams lie flat in one plane, so only the outline draws; a rim
    # down to the grid.
    mid = b.vert((0.0, 0.03, 0.0))
    top = [b.vert(c) for c in corners]
    foot = [b.vert((c[0], 0.0, c[2])) for c in corners]
    n = len(top)
    for i in range(n):
        j = (i + 1) % n
        b.poly([mid, top[j], top[i]], facing=UP)
        b.poly([foot[i], top[i], top[j], foot[j]], facing=np.array(corners[i]) + np.array(corners[j]))
    return b.mesh()


# ---- The backdrop: the synthwave horizon, standing outside the far walls ----

def pyramid(b: Builder, x: float, z: float, half: float, height: float, turn: float, courses: int) -> None:
    """A pyramid: four triangular faces, each cut into bands whose seams lie in its plane, with `courses` of them
    drawn as level lines so it reads as stepped stone and not as a tent."""
    base = ring(rect(x, z, 2 * half, 2 * half, turn), 0.0)
    apex = np.array([x, height, z])
    levels = [k / (courses + 1) for k in range(courses + 1)]
    for i in range(4):
        a, c = base[i], base[(i + 1) % 4]
        rows = [(a + (apex - a) * t, c + (apex - c) * t) for t in levels]
        out = (a + c) / 2 - np.array([x, 0.0, z])
        for k in range(len(rows)):
            lo = rows[k]
            if k + 1 < len(rows):
                hi = rows[k + 1]
                b.poly([lo[0], hi[0], hi[1], lo[1]], facing=out + UP)
            else:
                b.poly([lo[0], apex, lo[1]], facing=out + UP)
        for k in range(1, len(rows)):
            b.line(b.vert(rows[k][0]), b.vert(rows[k][1]))


def pyramids() -> Mesh:
    """The near horizon, 18 m along x: a great pyramid 6.2 m tall beside a smaller one 3.6 m tall and a broken stump of
    a third, each face cut by one or two drawn courses; dark closed facets behind the far wall. (At 9 m the great pyramid
    filled the frame's top corners and pulled the eye from the fight.)"""
    b = Builder()
    pyramid(b, -1.8, 0.8, 4.2, 6.2, 8, 2)
    pyramid(b, 4.6, -0.6, 2.4, 3.6, -10, 1)
    # The ruin: a pyramid's lower third, flat-topped.
    base = ring(rect(-7.6, -0.4, 3.0, 3.0, 20), 0.0)
    mid = np.array([-7.6, 0.0, -0.4])
    top = [mid + (p - mid) * 0.55 + UP * 1.2 for p in base]
    loft(b, [base, top], top=True)
    return b.mesh()


def dunes() -> Mesh:
    """Dunes behind the pyramids, 26 m along x and up to 4.5 m: a ridge of nine crests, each slope one big triangle
    from crest to foot, the slopes toward the arena gentler than those behind, so the range reads as soft sand in a
    few large facets and its ridge as one continuous line."""
    b = Builder()
    xs = np.linspace(-13, 13, 9)
    ridge = [2.2, 3.4, 2.6, 4.5, 3.0, 3.8, 2.4, 3.2, 2.0]
    jog = [0.0, 0.6, -0.4, 0.3, -0.6, 0.4, -0.2, 0.5, 0.0]
    crests = [np.array([x, h, j]) for x, h, j in zip(xs, ridge, jog, strict=True)]
    for sign, reach in ((-1, 5.5), (1, 3.0)):
        out = np.array([0.0, 0.3, sign])
        foot = [np.array([(xs[i] + xs[i + 1]) / 2, 0.0, sign * reach]) for i in range(len(xs) - 1)]
        for i in range(len(xs) - 1):
            b.poly([crests[i], crests[i + 1], foot[i]], facing=out)
            if i + 1 < len(foot):
                b.poly([crests[i + 1], foot[i + 1], foot[i]], facing=out)
        b.poly([crests[0], foot[0], np.array([xs[0], 0.0, sign * reach])], facing=out)
        b.poly([crests[-1], np.array([xs[-1], 0.0, sign * reach]), foot[-1]], facing=out)
    for end, d in ((0, -1.0), (len(xs) - 1, 1.0)):
        b.poly([crests[end], np.array([xs[end], 0.0, -5.5]), np.array([xs[end], 0.0, 3.0])], facing=(d, 0, 0))
    return b.mesh()


PIECES = {
    "lotusColumn": lotus_column,
    "blockStack": block_stack,
    "brazier": brazier,
    "fallenObelisk": fallen_obelisk,
    "obelisk": obelisk,
    "cactus": cactus,
    "rockSpire": rock_spire,
    "datePalm": date_palm,
    "sandRipples": sand_ripples,
    "sandDrift": sand_drift,
    "pyramids": pyramids,
    "dunes": dunes,
}
