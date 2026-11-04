"""Act 1's kit, the Overgrown Grid (ps-8z4.5; boss-rush.md, Environments): a wireframe forest at night in the teal key.

Designed by eye against Synty's POLYGON NatureBiomes EnchantedForest and MeadowForest (giant trees and their roots,
mushrooms, crystals, rune stones, lantern stands, logs, stumps, tree ferns, ground roots, moss lumps, pines, cliffs), redrawn from
scratch as simple faceted volumes whose every edge draws (design.md, The aim): big closed facets, closed silhouettes,
nothing hairline. Numpy only, no vendor input. Content/arenas.json's kit says which pieces are props (each its prop
kind's footprint and height), which the floor scatters and which the backdrop stands round the far walls.
Bake: `just champions-tool kit bake overgrownGrid`; stills: `just champions-tool stills --scene kit-overgrownGrid`.
"""

from __future__ import annotations

import math

import numpy as np

from champions_tools.structure import Builder, Mesh, loft, panel, polygon, prism, ring, slab

UP = np.array([0.0, 1.0, 0.0])


# ---- Shared shapes ----

def frame(axis: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Two unit vectors across a unit axis."""
    side = np.cross(axis, UP if abs(axis[1]) < 0.95 else np.array([1.0, 0.0, 0.0]))
    side /= np.linalg.norm(side)
    return side, np.cross(side, axis)


def tube_ring(centre, axis, radius: float, sides: int, phase_deg: float) -> list[np.ndarray]:
    """A regular ring across `axis` round `centre`, clockwise seen from the axis's tip."""
    u, v = frame(axis)
    out = []
    for i in range(sides):
        a = math.radians(phase_deg) - 2 * math.pi * i / sides
        out.append(np.asarray(centre, dtype=np.float64) + radius * (math.cos(a) * u + math.sin(a) * v))
    return out


def crystal(b: Builder, foot, lean_deg: float, heading_deg: float, radius: float, length: float, sides: int = 6,
            tip: float = 0.35) -> None:
    """A faceted shard: a prism along a tilted axis, closed under, into a pyramid point a `tip` share of its length."""
    lean, head = math.radians(lean_deg), math.radians(heading_deg)
    axis = np.array([math.sin(lean) * math.cos(head), math.cos(lean), math.sin(lean) * math.sin(head)])
    base = np.array([foot[0], radius * math.sin(lean) + 0.01, foot[1]])
    shoulder = base + axis * length * (1 - tip)
    rings = [tube_ring(base, axis, radius, sides, 0), tube_ring(shoulder, axis, radius, sides, 0)]
    idx = loft(b, rings, top=False, bottom=True)
    point = b.vert(base + axis * length)
    top = idx[-1]
    for i in range(sides):
        b.poly([top[i], point, top[(i + 1) % sides]], facing=b.points[top[i]] + b.points[top[(i + 1) % sides]] - 2 * shoulder)


def wedge(b: Builder, root, out_deg: float, reach: float, height: float, width: float) -> None:
    """A buttress root: an upright triangle from a trunk's foot out along the ground, made a slab `width` thick."""
    a = math.radians(out_deg)
    d = np.array([math.cos(a), 0.0, -math.sin(a)])
    side = np.cross(UP, d)
    r = np.asarray(root, dtype=np.float64)
    corners = [r + UP * height, r + d * reach, r]
    corners = [c - side * width / 2 for c in corners]
    slab(b, corners, width, down=side)


def ringed_cap(b: Builder, outer: list[int], share: float) -> None:
    """A flat cut's face with a growth ring printed on it: the outline's polygon cut into a band and an inner polygon,
    the ring drawn, the band's seams not."""
    pts = [b.points[i] for i in outer]
    mid = sum(pts) / len(pts)
    inner = [b.vert(mid + (p - mid) * share) for p in pts]
    n = len(outer)
    for i in range(n):
        j = (i + 1) % n
        b.poly([outer[i], outer[j], inner[j], inner[i]])
    b.poly(inner)
    for i in range(n):
        b.line(inner[i], inner[(i + 1) % n])


def peak(b: Builder, rim: list[int], apex) -> None:
    """A ring closed up to a point: one triangle a side."""
    a = b.vert(apex)
    mid = sum(b.points[i] for i in rim) / len(rim)
    for i in range(len(rim)):
        p, q = rim[i], rim[(i + 1) % len(rim)]
        b.poly([p, a, q], facing=(b.points[p] + b.points[q]) / 2 - mid)


def cut_trunk(b: Builder, sides: int, r0: float, r1: float, low: float, high: float, phase: float = 0.0,
              turn_deg: float = 0.0) -> list[int]:
    """A tapering trunk from the ground, its top cut on a slant from `low` (on the side `turn_deg` faces) to `high`
    across it, planar so every side stays one facet; the cut ring's indices."""
    foot = [b.vert(p) for p in ring(polygon(0, 0, r0, sides, phase), 0.0)]
    t = math.radians(turn_deg)
    d = np.array([math.cos(t), 0.0, -math.sin(t)])
    top = []
    for p in polygon(0, 0, 1.0, sides, phase):
        # Along the taper line from the foot corner toward the axis, up to the slanted plane.
        q = np.array([p[0], 0.0, p[1]])
        s = (float(np.dot(q, d)) + 1) / 2  # 0 on the low side, 1 on the high
        y = low + (high - low) * (1 - s)
        r = r0 + (r1 - r0) * y / high
        top.append(b.vert((p[0] * r, y, p[1] * r)))
    for i in range(sides):
        j = (i + 1) % sides
        b.poly([foot[i], foot[j], top[j], top[i]])
    return top


def splinter(b: Builder, at, height: float, size: float, turn_deg: float) -> None:
    """A three-sided spike of split wood standing on a cut."""
    base = [np.array([at[0] + x, at[1], at[2] + z]) for x, z in polygon(0, 0, size, 3, turn_deg)]
    apex = np.array([at[0], height, at[2]]) + 0.3 * (base[0] - np.array(at))
    for i in range(3):
        a, c = base[i], base[(i + 1) % 3]
        b.poly([a, apex, c], facing=(a + c) / 2 - np.array(at))


def frond(b: Builder, root, heading_deg: float, knee, tip, width: float, thick: float = 0.03) -> None:
    """A fern frond: two long diamonds from the root up to its knee and out down to its tip, each a thin slab."""
    h = math.radians(heading_deg)
    out = np.array([math.cos(h), 0.0, -math.sin(h)])
    side = np.cross(UP, out)
    r = np.asarray(root, dtype=np.float64)
    k = r + out * knee[0] + UP * knee[1]
    t = r + out * tip[0] + UP * tip[1]
    for a, c, w in ((r, k, width), (k, t, width * 0.8)):
        m = (a + c) / 2
        rise = c - a
        normal = np.cross(rise, side)
        if normal[1] < 0:
            normal = -normal
        corners = [a, m + side * w / 2, c, m - side * w / 2]
        if np.dot(np.cross(corners[1] - corners[0], corners[2] - corners[0]), normal) < 0:
            corners.reverse()
        slab(b, corners, thick, down=-normal)


def conifer(b: Builder, x: float, z: float, height: float, radius: float, turn: float, tiers: int = 3) -> None:
    """A synthwave pine: a short six-sided trunk under stacked six-sided cones, each narrower and turned a twelfth."""
    trunk = height * 0.18
    prism(b, polygon(x, z, radius * 0.12, 6, turn), 0.0, trunk + 0.2, top=False)
    span = height - trunk
    tier = span * 0.5
    for t in range(tiers):
        y0 = trunk + (span - tier) * t / max(1, tiers - 1)
        foot = [b.vert(p) for p in ring(polygon(x, z, radius * (1 - 0.26 * t), 6, turn + 30 * t), y0)]
        apex = b.vert((x, y0 + tier, z))
        for i in range(6):
            a, c = b.points[foot[i]], b.points[foot[(i + 1) % 6]]
            m = (a + c) / 2
            b.poly([foot[i], apex, foot[(i + 1) % 6]], facing=m - np.array([x, m[1], z]))
        b.poly(list(reversed(foot)), facing=(0, -1, 0))


# ---- Props: each its prop kind's footprint (arenas.json) and exactly its height ----

def broken_oak() -> Mesh:
    """The pillar's dressing (r 0.9, 3.2 m, unbreakable, so drawn grey): the snapped bole of one of EnchantedForest's
    giant trees. An eight-sided trunk tapering from 0.82 to 0.7 m, its top broken on a slant from 2.3 to 2.7 m with
    three spikes of split wood up to 3.2 m, growth rings on the break, and three low buttress roots (five tall ones
    read as a turret's or a spider's legs from above). A whole tree's crown would hide the fight from the high camera; the break keeps the forest's scale low."""
    b = Builder()
    top = cut_trunk(b, 8, 0.82, 0.7, 2.3, 2.7, phase=22.5, turn_deg=200)
    ringed_cap(b, top, 0.62)
    pts = [b.points[i] for i in top]
    mid = sum(pts) / len(pts)
    splinter(b, pts[1] * 0.75 + mid * 0.25, 3.2, 0.2, 10)
    splinter(b, pts[2] * 0.7 + mid * 0.3, 2.95, 0.17, 50)
    splinter(b, pts[0] * 0.7 + mid * 0.3, 2.75, 0.15, 80)
    for a, reach, h in ((20, 0.45, 0.7), (140, 0.5, 0.75), (260, 0.42, 0.6)):
        r = math.radians(a)
        wedge(b, (0.72 * math.cos(r), 0.0, -0.72 * math.sin(r)), a, reach, h, 0.24)
    return b.mesh()


def crystal_cluster() -> Mesh:
    """The crate's dressing (1.6 x 1.6 m, 1.4 m, two blows, so drawn in the key): EnchantedForest's glowing crystals as
    five six-sided shards leaning out of a low rock, the tallest upright to 1.4 m, the rest fanned at 18-38 degrees so
    each point keeps its own silhouette."""
    b = Builder()
    rock = [ring(polygon(0, 0, 0.72, 7, 8), 0.0), ring(polygon(0, 0, 0.5, 7, 8), 0.22)]
    loft(b, rock, top=True)
    crystal(b, (0.05, 0.0), 0, 0, 0.2, 1.39, tip=0.3)
    crystal(b, (-0.3, 0.12), 24, 160, 0.15, 1.0)
    crystal(b, (0.32, 0.2), 30, 20, 0.13, 0.85)
    crystal(b, (0.12, -0.34), 34, 280, 0.12, 0.7)
    crystal(b, (-0.22, -0.28), 18, 230, 0.1, 0.6)
    return b.mesh()


def lantern() -> Mesh:
    """The post's dressing (r 0.35, 1.6 m, one blow, so drawn in the key): the forest packs' lantern stands. A square post
    on a stepped foot under a pyramid finial, an arm out to a six-sided lantern with a pointed roof and a point under,
    hanging inside the footprint."""
    b = Builder()
    prism(b, polygon(0, 0, 0.2, 4, 45), 0.0, 0.1)
    prism(b, polygon(0, 0, 0.07, 4, 45), 0.1, 1.5, top=False)
    foot = [b.vert(p) for p in ring(polygon(0, 0, 0.07, 4, 45), 1.5)]
    apex = b.vert((0, 1.6, 0))
    for i in range(4):
        a, c = b.points[foot[i]], b.points[foot[(i + 1) % 4]]
        b.poly([foot[i], apex, foot[(i + 1) % 4]], facing=(a + c) / 2 - np.array([0, 1.5, 0]))
    prism(b, [(0.05, -0.03), (0.05, 0.03), (0.27, 0.03), (0.27, -0.03)], 1.36, 1.42, bottom=True)
    x = 0.24
    roof = [b.vert(p) for p in ring(polygon(x, 0, 0.13, 6), 1.22)]
    top = b.vert((x, 1.34, 0))
    for i in range(6):
        a, c = b.points[roof[i]], b.points[roof[(i + 1) % 6]]
        b.poly([roof[i], top, roof[(i + 1) % 6]], facing=(a + c) / 2 - np.array([x, 1.22, 0]))
    b.poly(list(reversed(roof)), facing=(0, -1, 0))
    body = loft(b, [ring(polygon(x, 0, 0.085, 6), 0.98), ring(polygon(x, 0, 0.1, 6), 1.2)], top=True)
    under = b.vert((x, 0.9, 0))
    for i in range(6):
        a, c = b.points[body[0][i]], b.points[body[0][(i + 1) % 6]]
        b.poly([body[0][(i + 1) % 6], under, body[0][i]], facing=(a + c) / 2 - np.array([x, 0.98, 0]))
    return b.mesh()


def fallen_log() -> Mesh:
    """The slab's dressing (4.0 x 0.8 m, 1.1 m, unbreakable, so drawn grey): MeadowForest's logs. An eight-sided trunk
    lying along x on a flat side, 0.74 m tall, its west end sawn clean with a growth ring, its east end snapped on a
    slant, and a broken branch stub rising to 1.1 m."""
    b = Builder()
    r = 0.4
    cy = r * math.cos(math.radians(22.5))
    xs = (-1.95, 1.75)
    rings = []
    for k, x in enumerate(xs):
        pts = []
        for i in range(8):
            a = math.radians(22.5 + 45 * i)
            y, z = cy - r * math.cos(a), r * math.sin(a)
            dx = 0.0 if k == 0 else 0.22 * (y - cy) / r  # the east end's slanted break
            pts.append(np.array([x + dx, y, z]))
        rings.append(pts)
    idx = [[b.vert(p) for p in rg] for rg in rings]
    for i in range(8):
        j = (i + 1) % 8
        b.poly([idx[0][i], idx[1][i], idx[1][j], idx[0][j]], facing=b.points[idx[0][i]] + b.points[idx[0][j]] - 2 * np.array([0, cy, 0]) - np.array([b.points[idx[0][i]][0] * 2, 0, 0]))
    ringed_cap(b, list(reversed(idx[0])) if np.dot(b.normal(list(reversed(idx[0]))), (-1, 0, 0)) > 0 else idx[0], 0.55)
    east = idx[1] if np.dot(b.normal(idx[1]), (1, 0, 0)) > 0 else list(reversed(idx[1]))
    b.poly(east)
    crystal(b, (0.55, 0.05), 28, 60, 0.11, (1.1 - 0.11 * math.sin(math.radians(28)) - 0.01) / math.cos(math.radians(28)), sides=6, tip=0.12)
    return b.mesh()


def giant_mushroom() -> Mesh:
    """A giant mushroom (r 0.5 stalk, 3 m, two blows, in the key): EnchantedForest's. A six-sided stalk leaning a little,
    flaring under an eight-sided cap 2.6 m across: a flat underside, a steep rim and a peaked top rising to a point, so
    the high camera sees a mushroom's cap and not a faceted dome (a flat top ring read as a gem)."""
    b = Builder()
    loft(b, [ring(polygon(0, 0, 0.36, 6), 0.0), ring(polygon(0.06, 0, 0.26, 6), 1.3),
             ring(polygon(0.12, 0, 0.3, 6), 2.15)], top=False)
    rings = loft(b, [ring(polygon(0.12, 0, 1.3, 8, 22.5), 2.1), ring(polygon(0.12, 0, 1.05, 8, 22.5), 2.55)], top=False,
                 bottom=True)
    peak(b, rings[-1], (0.12, 3.0, 0.0))
    return b.mesh()


def rune_stone() -> Mesh:
    """A rune stone (1.0 x 0.6 m, 2.4 m, unbreakable, grey): EnchantedForest's StoneRunes. A tapering monolith under a
    hipped top, a rune cut into the face toward the camera: one long slot between two short bars."""
    b = Builder()
    w0, d0, w1, d1, h, top = 1.0, 0.6, 0.78, 0.44, 2.0, 2.4
    lo = [(-w0 / 2, -d0 / 2), (-w0 / 2, d0 / 2), (w0 / 2, d0 / 2), (w0 / 2, -d0 / 2)]
    hi = [(-w1 / 2, -d1 / 2), (-w1 / 2, d1 / 2), (w1 / 2, d1 / 2), (w1 / 2, -d1 / 2)]
    L, H = ring(lo, 0.0), ring(hi, h)
    # The face toward the camera (-z): a trapezoid in its own plane, the rune cut into it.
    o = L[0]
    u = np.array([1.0, 0.0, 0.0])
    up = H[0] - L[0]
    up = up - np.dot(up, u) * u
    slant = float(np.linalg.norm(up))
    v = up / slant
    inset = (w0 - w1) / 2
    panel(b, o, u, v, [(0, 0), (inset, slant), (w0 - inset, slant), (w0, 0)],
          openings=[(0.46, 0.45, 0.54, 1.55), (0.24, 1.05, 0.38, 1.13), (0.62, 1.05, 0.76, 1.13)], depth=0.05)
    for a, c in ((0, 1), (1, 2), (2, 3)):  # the other three sides
        b.poly([L[a], H[a], H[c], L[c]], facing=(L[a] + L[c]) / 2 * np.array([1, 0, 1]))
    ridge = [np.array([-0.15, top, 0.0]), np.array([0.15, top, 0.0])]
    b.poly([H[0], ridge[0], ridge[1], H[3]], facing=(0, 0.3, -1))
    b.poly([H[1], H[2], ridge[1], ridge[0]], facing=(0, 0.3, 1))
    b.poly([H[0], H[1], ridge[0]], facing=(-1, 0.3, 0))
    b.poly([H[3], ridge[1], H[2]], facing=(1, 0.3, 0))
    return b.mesh()


def stump() -> Mesh:
    """A stump (r 0.6, 0.7 m, one blow, in the key): MeadowForest's tree trunks. Seven sides tapering, cut on a slant from
    0.5 to 0.6 m with a growth ring, one spike of split wood to 0.7 m and three roots."""
    b = Builder()
    top = cut_trunk(b, 7, 0.5, 0.44, 0.5, 0.6, turn_deg=30)
    ringed_cap(b, top, 0.55)
    pts = [b.points[i] for i in top]
    splinter(b, pts[4] * 0.7 + sum(pts) / len(pts) * 0.3, 0.7, 0.1, 20)
    for a, reach, h in ((60, 0.3, 0.35), (190, 0.32, 0.4), (300, 0.26, 0.3)):
        r = math.radians(a)
        wedge(b, (0.44 * math.cos(r), 0.0, -0.44 * math.sin(r)), a, reach, h, 0.16)
    return b.mesh()


def fern_tree() -> Mesh:
    """A tree fern (r 0.3, 3.4 m, one blow, in the key): MeadowForest's fern trees. A six-sided trunk bending as it rises
    to 2.9 m under a crown of seven fronds, each two long diamonds up to its knee at 3.4 m and down to its tip."""
    b = Builder()
    loft(b, [ring(polygon(0, 0, 0.17, 6), 0.0), ring(polygon(0.08, 0, 0.13, 6), 1.5),
             ring(polygon(0.2, 0, 0.1, 6), 2.95)], top=True)
    for k in range(7):
        frond(b, (0.2, 2.9, 0.0), 360 * k / 7 + 12, (0.85, 0.5), (1.75, -0.25), 0.34)
    return b.mesh()


# ---- The floor treatment: low, never solid, scattered over open floor ----

def strip(b: Builder, path, widths, thick: float) -> None:
    """A flat tapering strip along a ground path, its quads sharing their joints so only its outline draws."""
    pts = [np.array([x, 0.0, z]) for x, z in path]
    lefts, rights = [], []
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]
        c = pts[min(len(pts) - 1, i + 1)]
        d = (c - a) / np.linalg.norm(c - a)
        side = np.cross(UP, d)
        lefts.append(p + side * widths[i] / 2 + UP * thick)
        rights.append(p - side * widths[i] / 2 + UP * thick)
    for i in range(len(pts) - 1):
        corners = [lefts[i], lefts[i + 1], rights[i + 1], rights[i]]
        if np.dot(np.cross(corners[1] - corners[0], corners[2] - corners[0]), UP) > 0:
            corners.reverse()
        slab(b, corners, thick)


def roots() -> Mesh:
    """Roots crawling over the floor (EnchantedForest's ground roots): a long root curving across 4 m, two branches off
    it, each a flat strip tapering to a point, 6 cm proud of the grid."""
    b = Builder()
    strip(b, [(-2.0, -0.2), (-1.0, 0.15), (0.0, 0.1), (1.0, 0.45), (2.0, 0.3)], [0.34, 0.28, 0.22, 0.14, 0.05], 0.06)
    strip(b, [(-0.4, 0.0), (0.1, -0.6), (0.4, -1.3)], [0.16, 0.1, 0.04], 0.05)
    strip(b, [(0.6, 0.3), (1.0, 0.9), (1.2, 1.5)], [0.12, 0.08, 0.03], 0.05)
    return b.mesh()


def moss() -> Mesh:
    """A moss patch (EnchantedForest's moss and ground cover): one broad flat patch 3.4 m across and 3 cm proud of the
    grid, its outline nine irregular sides, nothing inside. Lumps and mounds of moss read as gems or XP shards to both
    blind critiques; a large flat patch reads as ground."""
    b = Builder()
    radii = [1.7, 1.35, 1.55, 1.1, 1.45, 1.6, 1.2, 1.5, 1.3]
    corners = []
    for i, r in enumerate(radii):
        a = 2 * math.pi * i / len(radii)
        corners.append((r * math.cos(a), 0.03, -r * 0.8 * math.sin(a)))
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

def treeline() -> Mesh:
    """A stretch of the forest's edge, 10 m along x: five pines from 6 to 11 m tall, staggered over 3 m of depth and
    spaced so each keeps its own outline, a dark skyline of closed facets behind the far wall. Only pines: from the high
    camera a cone stays a tree, while a round crown seen from above read as a wheel or a gem to the blind critique."""
    b = Builder()
    conifer(b, -4.0, 0.6, 9.0, 1.4, 0)
    conifer(b, -2.0, -0.8, 6.5, 1.15, 10)
    conifer(b, 0.1, 0.9, 11.0, 1.6, 20)
    conifer(b, 2.2, -0.6, 7.5, 1.25, 5)
    conifer(b, 4.2, 0.7, 8.5, 1.35, 30)
    return b.mesh()


def hills() -> Mesh:
    """Low mountains behind the trees, 22 m along x and up to 7 m: a ridge of nine peaks, each slope one big triangle
    from peak to foot and back, so the range reads as a few large facets and its ridge as one continuous line (two rows
    of small triangles read as a truss)."""
    b = Builder()
    xs = np.linspace(-11, 11, 9)
    ridge = [3.5, 5.8, 4.2, 7.0, 5.0, 6.2, 3.8, 5.4, 3.2]
    jog = [0.0, 0.4, -0.3, 0.2, -0.5, 0.3, -0.2, 0.5, 0.0]
    peaks = [np.array([x, h, j]) for x, h, j in zip(xs, ridge, jog, strict=True)]
    for sign in (-1, 1):
        out = np.array([0.0, 0.3, sign])
        foot = [np.array([(xs[i] + xs[i + 1]) / 2, 0.0, sign * 3.5]) for i in range(len(xs) - 1)]
        for i in range(len(xs) - 1):
            b.poly([peaks[i], peaks[i + 1], foot[i]], facing=out)
            if i + 1 < len(foot):
                b.poly([peaks[i + 1], foot[i + 1], foot[i]], facing=out)
        b.poly([peaks[0], foot[0], np.array([xs[0], 0.0, sign * 3.5])], facing=out)
        b.poly([peaks[-1], np.array([xs[-1], 0.0, sign * 3.5]), foot[-1]], facing=out)
    for end, d in ((0, -1.0), (len(xs) - 1, 1.0)):
        b.poly([peaks[end], np.array([xs[end], 0.0, -3.5]), np.array([xs[end], 0.0, 3.5])], facing=(d, 0, 0))
    return b.mesh()


PIECES = {
    "brokenOak": broken_oak,
    "crystalCluster": crystal_cluster,
    "lantern": lantern,
    "fallenLog": fallen_log,
    "giantMushroom": giant_mushroom,
    "runeStone": rune_stone,
    "stump": stump,
    "fernTree": fern_tree,
    "roots": roots,
    "moss": moss,
    "treeline": treeline,
    "hills": hills,
}
