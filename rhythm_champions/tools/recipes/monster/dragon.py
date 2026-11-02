"""The dragon (ps-6y2.40): the boss rebuilt whole as a few simple faceted shapes on the vendor's bones.

The trailer's drawing of the vendor mesh reads beautifully gliding (long clean strokes for the wings and body), but on
the ground, where the duel keeps it, it isn't dark inside: wherever a wing turns edge-on to the camera (every folded
pose, the tail sweep, the landing) its curved membrane glows as a solid red band in the fresnel rim, the torso between
the wings is a knot of crossing lines, the head a nose-on box full of teeth, and the toes and claws scribble. What
names the dragon (the bat wings spoked by long fingers, the long neck and tail, the horned head) is in it, so every
part is new, skinned to the vendor bones, and every edge that draws is chosen here (each polygon's edges, never its
triangles' diagonals, and only some of those):

- The spine: one loft from the back of the head down the neck, over a broad deep chest and out along the tail, drawn
  by its ridge and its shoulders and quiet below; chevrons across its back, two up the neck, one over the shoulders
  and one over the hips, two down the tail (on every segment it read as a centipede, on every other one as a ladder,
  and with none on the trunk its ridge was an arrow's shaft); a flat arrowhead spade at the tail's tip (a bare point
  read as a spear).
- The head, nearly half again the vendor's skull: a long, low faceted wedge from broad cheeks and brow ridges to the
  nose, drawn round only at the brow and the nose (with parallel sides, or round every section, it read as a box with
  a pyramid of a snout); a jaw of its own whose back corners jut past the cheeks; two thick horns low on the back of
  the skull, swept back along it into the head's outline (up from the brows or out to the sides they stood off as
  daggers, or a fox's ears); slanted eyes under the brows.
- The wings: thin plates between the vendor's wing bones, the arm and first finger the leading edge, two more
  fingers spoking from the wrist (the vendor's second and fourth: with all four the folded wings drew as stacks of
  finger blades under a sawtooth of tips), a trailing edge bowed in broad scallops between their tips and back to the
  flank, the root on the body's shoulder line so the wing grows out of the body. Every landmark rides its own bone
  and each trailing-edge point the two fingers either side of it, so the edge stays taut as the wing folds (skinned
  from the vendor's membrane, those points rode one finger and zigzagged like lightning bolts). Two narrow bevels
  close each plate's outline, so an outline edge seen face-on has a lit face either side of its line, as a finger
  does, and draws as wide (on a rim square to the plate it drew at half the width and faded). A thick spar along the
  arm; no claw at the wrist (it drew a short bright bar).
- Shading normals for the membranes: by the bone each point rides, the direction in its rest frame that the vendor's
  clips turn up, toward the game's camera, in the most frames (measured from the baked bone texture). The rim then
  leaves a membrane seen nearly edge-on dark, where its facets' own normals lit it whole: the folded wings on the
  ground no longer glow. Every other shape leans its facets' normals up (the big ork's trick): unleant, the horns'
  bases and the arm spar lit as small solid chips.
- Legs lofted through the vendor's joints, thick, the hind ones with a muscled thigh, drawn only along them, each into
  a low wedge of a foot narrowing to a blunt point (separate toes and claws broke into scratches at game size, and
  thin legs with tall feet read as stakes).

The mesh flags its edges, so it is drawn as designed at every distance rather than decimated: 790 triangles at every
LOD, where the vendor's are 5,444, 2,815 and 1,408.

Source: Magic Pig Games' Dragon_LP.prefab (sha256 bb4269fa952bd422abdf9498f24a40ab958c52f0d78728ff64b52d7977264f52) and
Dragon_LP.fbx (daee8e51e18919ed78eb93f4a3ea8bbaa13e52837f07679d9541794ae64b7f32), as manifest.json lists them. Only its
rig is used, with its joints (each bone's pivot on its parent, solved from the baked clips) to place the shapes; the
tail rides the tail part's own bone entries. No Blender: numpy only. Bake: `just champions-tool monster bake dragon`;
review: `just champions-tool monster review dragon` (and `--look trailer`).
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

Skin = dict  # {bone index or name: weight}
UP = (0.0, 1.0, 0.0)
LEAN = 0.8


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


class Builder:
    """The new mesh: shapes of convex polygons, each polygon's edges drawn (not its triangles' diagonals) unless soft,
    each point skinned by a {bone: weight}, a bone by its index or by its name."""

    def __init__(self, source: Source):
        self.source = source
        self.positions: list[np.ndarray] = []
        self.triangles: list[np.ndarray] = []
        self.edges: list[np.ndarray] = []
        self.bones: list[np.ndarray] = []
        self.weights: list[np.ndarray] = []
        self.leans: list[np.ndarray] = []
        self.own: list[np.ndarray] = []
        self.count = 0

    def index(self, bone) -> int:
        return bone if isinstance(bone, (int, np.integer)) else self.source.bone(bone)

    def add(self, points, faces, skins, soft=(), lean=(0, 0, 0), drawn_only=None, normals=None) -> None:
        """points (n, 3); faces, each (indices, a point inside the shape behind it): a convex polygon wound outward
        from that point; skins, one per point; soft, pairs of points whose edge doesn't draw (or drawn_only, the only
        pairs that do); lean, a vector its shading normals lean along (see mesh), or normals, one per point, its
        shading normals in place of its facets'."""
        pts = np.asarray(points, np.float64)
        soft = {frozenset(e) for e in soft}
        only = None if drawn_only is None else {frozenset(e) for e in drawn_only}
        lean = np.asarray(lean, np.float64)
        own = 1.0 if normals is None else 0.0
        given = None if normals is None else np.asarray(normals, np.float64)
        tris, flags, leans = [], [], []
        for f, inside in faces:
            f = list(f)
            q = pts[f]
            normal = np.cross(q[1:-1] - q[0], q[2:] - q[0]).sum(axis=0)
            if normal @ (q.mean(axis=0) - np.asarray(inside, np.float64)) < 0:
                f.reverse()
                normal = -normal
            # A face turned against the lean (a plate's underside) keeps its own normal: leant, it drew solid when its
            # shard turned over.
            facing = normal @ lean >= -0.5 * np.linalg.norm(normal) * np.linalg.norm(lean)
            m = len(f)
            for k in range(1, m - 1):
                tri = [f[0], f[k], f[k + 1]]
                tris.append(tri)
                leans.append(given[tri] if given is not None else np.tile(LEAN * lean if facing else 0 * lean, (3, 1)))
                drawn = [True, k + 1 == m - 1, k == 1]  # the edges opposite corners 0, k and k + 1
                pairs = [(f[k], f[k + 1]), (f[k + 1], f[0]), (f[0], f[k])]
                flags.append([d and frozenset(p) not in soft and (only is None or frozenset(p) in only)
                              for d, p in zip(drawn, pairs)])
        bones = np.zeros((len(pts), 4), np.int32)
        weights = np.zeros((len(pts), 4), np.float32)
        for i, s in enumerate(skins):
            for k, (name, w) in enumerate(s.items()):
                bones[i, k], weights[i, k] = self.index(name), w
        self.positions.append(pts)
        self.triangles.append(np.asarray(tris, np.int64) + self.count)
        self.edges.append(np.asarray(flags, np.uint8))
        self.bones.append(bones)
        self.weights.append(weights / weights.sum(axis=1, keepdims=True))
        self.leans.append(np.asarray(leans, np.float64).reshape(-1, 3, 3))
        self.own.append(np.full(len(tris), own))
        self.count += len(pts)

    def loft(self, rings, skins, soft_rings=(), soft_sides=(), caps=(True, True), lean=(0, 0, 0), arcs=None) -> None:
        """A shape through rings of points (each (k, 3), all the same k, or a single point, an apex), wound the same
        way round; skins, one per ring (or one per point, a list of lists); the edges round the rings in soft_rings,
        and those along the shape from the ring corners in soft_sides, don't draw; arcs, {ring: edges}, draws only
        those edges of a ring (edge j from corner j to j + 1)."""
        rings = [np.atleast_2d(np.asarray(r, np.float64)) for r in rings]
        k = max(len(r) for r in rings)
        pts, first = [], []
        for r in rings:
            first.append(len(pts))
            pts.extend(r)
        centres = [r.mean(axis=0) for r in rings]
        faces, soft = [], []
        for i in range(len(rings) - 1):
            a, b = first[i], first[i + 1]
            na, nb = len(rings[i]), len(rings[i + 1])
            inside = (centres[i] + centres[i + 1]) / 2
            for j in range(k):
                if na == 1:
                    faces.append(([a, b + j, b + (j + 1) % k], inside))
                elif nb == 1:
                    faces.append(([a + j, a + (j + 1) % k, b], inside))
                else:
                    faces.append(([a + j, a + (j + 1) % k, b + (j + 1) % k, b + j], inside))
        for i in soft_rings:
            if len(rings[i]) > 1:
                soft += [(first[i] + j, first[i] + (j + 1) % k) for j in range(k)]
        for i, keep in (arcs or {}).items():
            soft += [(first[i] + j, first[i] + (j + 1) % k) for j in range(k) if j not in keep]
        for j in soft_sides:
            for i in range(len(rings) - 1):
                a = first[i] + (j if len(rings[i]) > 1 else 0)
                b = first[i + 1] + (j if len(rings[i + 1]) > 1 else 0)
                soft.append((a, b))
        for (i, other), cap in zip(((0, 1), (len(rings) - 1, len(rings) - 2)), caps):
            if cap and len(rings[i]) > 1:
                faces.append((list(range(first[i], first[i] + k)), centres[other]))
        skin = []
        for i, r in enumerate(rings):
            s = skins[i]
            skin += list(s) if isinstance(s, list) else [s] * len(r)
        self.add(pts, faces, skin, soft, lean)

    def plate(self, points, triangles, lines, skins, thickness: float, bevel: float, normals=None) -> None:
        """A thin plate over a sheet of triangles (indices into points, wound to face up its top): a top and a bottom
        layer, each drawing `lines` (pairs of points), and round its outline two narrow bevels meeting at a ridge
        `bevel` out from it. An outline edge seen face-on then has a lit face on both sides of its line, as a finger
        between two panels has, and draws as wide: on a rim square to the plate it drew at half the width, and the
        wings' outlines faded beside their fingers. normals, if given, one per point, are its shading normals in place
        of its facets'."""
        mid = np.asarray(points, np.float64)
        n = len(mid)
        tris = [list(t) for t in triangles]
        # Each point's normal on the sheet: the mean of its triangles'.
        up = np.zeros((n, 3))
        for t in tris:
            up[t] += np.cross(mid[t[1]] - mid[t[0]], mid[t[2]] - mid[t[0]])
        up /= np.linalg.norm(up, axis=1, keepdims=True)
        uses: dict[frozenset, int] = {}
        for t in tris:
            for k in range(3):
                e = frozenset((t[k], t[(k + 1) % 3]))
                uses[e] = uses.get(e, 0) + 1
        outline = [(t[k], t[(k + 1) % 3], t) for t in tris for k in range(3)
                   if uses[frozenset((t[k], t[(k + 1) % 3]))] == 1]
        # The ridge: each outline point moved out in the sheet's plane, square to its outline edges.
        out: dict[int, np.ndarray] = {}
        for a, c, t in outline:
            o = np.cross(mid[c] - mid[a], up[a] + up[c])
            if o @ (mid[a] - mid[t].mean(axis=0)) < 0:
                o = -o
            for v in (a, c):
                out[v] = out.get(v, np.zeros(3)) + o / np.linalg.norm(o)
        ridge = {v: 2 * n + i for i, v in enumerate(out)}
        pts = np.vstack([mid + up * thickness / 2, mid - up * thickness / 2,
                         [mid[v] + bevel * o / np.linalg.norm(o) for v, o in out.items()]])
        faces = []
        for t in tris:
            faces.append((t, mid[t].mean(axis=0) - up[t].mean(axis=0)))
            faces.append(([v + n for v in t], mid[t].mean(axis=0) + up[t].mean(axis=0)))
        for a, c, t in outline:
            inside = mid[t].mean(axis=0)
            faces.append(([a, c, ridge[c], ridge[a]], inside + up[a] * thickness))
            faces.append(([a + n, c + n, ridge[c], ridge[a]], inside - up[a] * thickness))
        drawn = [(a, b) for a, b in lines] + [(a + n, b + n) for a, b in lines]
        skin = list(skins) * 2 + [skins[v] for v in out]
        if normals is not None:
            normals = np.asarray(normals, np.float64)
            normals = normals / np.linalg.norm(normals, axis=1, keepdims=True)
            normals = np.vstack([normals, normals, [normals[v] for v in out]])
        self.add(pts, faces, skin, drawn_only=drawn, normals=normals)

    def mesh(self) -> Mesh:
        """Every triangle its own corners, their normal its facet's leaning along its shape's lean, or the shape's own
        normals where it gave them."""
        positions = np.vstack(self.positions)
        triangles = np.vstack(self.triangles)
        corners = triangles.reshape(-1)
        p = positions[triangles]
        normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        normal = (normal * np.concatenate(self.own)[:, None])[:, None, :] + np.vstack(self.leans)
        normal = normal.reshape(-1, 3) / np.linalg.norm(normal.reshape(-1, 3), axis=1, keepdims=True)
        return Mesh(positions[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    np.vstack(self.bones)[corners], np.vstack(self.weights)[corners],
                    normal.astype(np.float32), edges=np.vstack(self.edges))


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre, np.float64) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


def frames(points, ref):
    """For each point of a polyline, its direction d and two unit vectors square to it: u toward `ref`, v = d x u."""
    points = np.asarray(points, np.float64)
    out = []
    for i in range(len(points)):
        d = unit(points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)])
        u = unit(np.asarray(ref, np.float64) - (np.asarray(ref) @ d) * d)
        out.append((d, u, np.cross(d, u)))
    return out


def limb_rings(points, radii, n: int, ref, phase: float = 0.0):
    rings = []
    for c, r, (d, u, v) in zip(np.asarray(points, np.float64), radii, frames(points, ref)):
        ru, rv = (r, r) if np.isscalar(r) else r
        rings.append(c if ru == 0 else ring(c, u, v, (ru, rv), n, phase))
    return rings


def one(bone) -> Skin:
    return {bone: 1.0}


def blend(a, b, wb: float = 0.5) -> Skin:
    return {a: 1 - wb, b: wb}


D = "English Dragon"
TAIL = list(range(99, 108))  # the tail part's own entries for Tail1..Tail9 (the body's share names, not bind poses)


# ---- the spine: one loft from the back of the head down the neck, along the back and out to the tail's tip ----

# Stations: a point on the spine's line, the half width, the height above and below it, the skin.
SPINE = [
    ((0, 2.62, 1.50), 0.17, 0.12, 0.16, one(D + "Head")),
    ((0, 2.42, 1.33), 0.18, 0.12, 0.17, one(D + "Neck5")),
    ((0, 2.16, 1.25), 0.2, 0.12, 0.18, blend(D + "Neck4", D + "Neck5", 0.3)),
    ((0, 1.86, 1.22), 0.23, 0.13, 0.22, blend(D + "Neck3", D + "Neck4", 0.3)),
    ((0, 1.60, 1.12), 0.3, 0.15, 0.34, blend(D + "Neck2", D + "Neck3", 0.3)),
    ((0, 1.45, 0.80), 0.55, 0.22, 0.56, blend(D + "Neck1", D + "Spine3", 0.5)),
    ((0, 1.40, 0.35), 0.72, 0.28, 0.6, one(D + "Spine3")),
    ((0, 1.36, -0.10), 0.64, 0.26, 0.52, blend(D + "Spine2", D + "Spine3", 0.3)),
    ((0, 1.30, -0.55), 0.54, 0.24, 0.44, one(D + "Pelvis")),
    ((0, 1.27, -0.95), 0.36, 0.2, 0.32, blend(D + "Pelvis", TAIL[0], 0.6)),
    ((0.03, 1.22, -1.47), 0.26, 0.17, 0.25, blend(TAIL[1], TAIL[2], 0.5)),
    ((0.05, 1.13, -1.86), 0.21, 0.14, 0.2, blend(TAIL[2], TAIL[3], 0.5)),
    ((0.10, 1.04, -2.25), 0.17, 0.12, 0.16, blend(TAIL[3], TAIL[4], 0.5)),
    ((0.15, 0.96, -2.63), 0.14, 0.1, 0.13, blend(TAIL[4], TAIL[5], 0.5)),
    ((0.22, 0.88, -3.01), 0.12, 0.09, 0.11, blend(TAIL[5], TAIL[6], 0.5)),
    ((0.30, 0.80, -3.39), 0.1, 0.08, 0.09, blend(TAIL[6], TAIL[7], 0.5)),
    ((0.38, 0.74, -3.76), 0.08, 0.06, 0.07, blend(TAIL[7], TAIL[8], 0.5)),
]


def section(c, back, u, v, w, top, bottom, lag):
    """A ring of six round c: the ridge on top (v), drawn back toward the tail (`back`) by `lag` so the ring's top
    edges make a chevron, the shoulders, the flanks below them and the keel."""
    c = np.asarray(c, np.float64)
    return np.array([c + top * v + lag * back, c + w * u + 0.3 * top * v, c + 0.8 * w * u - 0.55 * bottom * v,
                     c - bottom * v, c - 0.8 * w * u - 0.55 * bottom * v, c - w * u + 0.3 * top * v])


TAIL_UP = (0.03, 1.0, -0.08)  # the spade's shading normal: world up in the tail tip's rest frame, as its clips hold it

# The stations whose rings draw their two top edges, a chevron across the back: two up the neck, one over the shoulders
# and one over the hips, two down the tail (on every station the body read as a centipede, on every other one the neck
# and tail as ladders, and with none on the trunk its ridge was an arrow's shaft).
CHEVRONS = (2, 4, 6, 8, 11, 14)


def spine(b: Builder) -> None:
    """One loft from the back of the head to the tail, drawn by its long lines (the ridge and the shoulders) and a few
    chevrons across its back, its flanks and belly quiet; a broad, deep chest between the wings (narrower, it was a
    spindle lost between them); a spade at the tail's tip (a bare point read as a spear)."""
    pts = [s[0] for s in SPINE]
    rings = []
    for (c, w, top, bottom, _), (d, u, v) in zip(SPINE, frames(pts, (1, 0, 0))):
        if v[1] < 0:  # the dorsal side up (and back, up the neck)
            v = -v
        if d[2] > 0:  # d along the spine toward the tail
            d = -d
        rings.append(section(c, d, u, v, w, top, bottom, 0.45 * w))
    tip = np.array([0.43, 0.7, -3.98])
    rings.append(tip)
    skins = [s[4] for s in SPINE] + [one(TAIL[8])]
    soft = [i for i in range(len(rings)) if i not in CHEVRONS]
    b.loft(rings, skins, soft_rings=soft, soft_sides=(2, 3, 4), lean=UP, arcs={i: (0, 5) for i in CHEVRONS})
    # The spade: a flat arrowhead round the tip, its barbs swept back, drawn round and down its middle.
    d = unit(tip - np.asarray(SPINE[-1][0]))
    across = unit(np.cross([0, 1, 0], d))
    c = np.asarray(SPINE[-1][0], np.float64)
    spade = [c + 0.1 * d, c - 0.05 * d + 0.34 * across, c + 0.75 * d, c - 0.05 * d - 0.34 * across, c + 0.22 * d]
    tris = [[0, 1, 4], [1, 2, 4], [4, 2, 3], [0, 4, 3]]
    lines = [(0, 1), (1, 2), (2, 3), (3, 0), (4, 2)]
    tris = [t if np.cross(spade[t[1]] - spade[t[0]], spade[t[2]] - spade[t[0]])[1] > 0 else [t[0], t[2], t[1]]
            for t in tris]
    b.plate(spade, tris, lines, [one(TAIL[8])] * 5, 0.05, 0.03, normals=[TAIL_UP] * 5)


# ---- the head ----

HEAD_PIVOT = np.array([0.0, 2.6, 1.55])
HEAD_SCALE = 1.45


def big(points):
    return HEAD_PIVOT + (np.asarray(points, np.float64) - HEAD_PIVOT) * HEAD_SCALE


def head(b: Builder) -> None:
    """A long, low faceted wedge, nearly half again the vendor's skull: broad cheeks and brow ridges raised over the
    eyes with a groove between them, narrowing to the nose (with parallel sides it read as a box, a crocodile's snout);
    a jaw of its own under it, its back corners jutting past the cheeks; two thick horns low on the back of the skull,
    swept back along it into the head's outline (out to the sides, or up from the brows, they stood off as stray
    daggers or a fox's ears; spikes from the cheeks too made a thorn-burst in a crowd); slanted eyes under the
    brows."""

    def sec(z, crest, wt, yt, wm, ym, wb, yb):
        return big([(0, crest, z), (wt, yt, z), (wm, ym, z), (wb, yb, z), (-wb, yb, z), (-wm, ym, z), (-wt, yt, z)])

    skull = [sec(1.40, 2.78, 0.18, 2.8, 0.27, 2.64, 0.17, 2.48),
             sec(1.68, 2.79, 0.27, 2.87, 0.35, 2.62, 0.21, 2.45),
             sec(2.02, 2.67, 0.15, 2.67, 0.19, 2.56, 0.14, 2.46),
             sec(2.42, 2.56, 0.07, 2.54, 0.09, 2.5, 0.07, 2.46)]
    # Drawn round only at the brow and the nose, and along its brows, cheeks and jaw line (round every section it read
    # as a box with a pyramid of a snout).
    b.loft(skull, [one(D + "Head")] * 4, soft_rings=(0, 2), soft_sides=(0,), lean=UP)
    jaw = [big([(0.29, 2.47, 1.62), (0.22, 2.3, 1.68), (-0.22, 2.3, 1.68), (-0.29, 2.47, 1.62)]),
           big([(0.15, 2.45, 1.95), (0.12, 2.33, 1.97), (-0.12, 2.33, 1.97), (-0.15, 2.45, 1.95)]),
           big([(0.07, 2.44, 2.36), (0.05, 2.37, 2.34), (-0.05, 2.37, 2.34), (-0.07, 2.44, 2.36)])]
    b.loft(jaw, [one(D + "Jaw")] * 3, soft_rings=(1,), lean=UP)
    for s in (-1, 1):
        horn = big(np.array([(0.2, 2.82, 1.46), (0.27, 2.88, 1.26), (0.32, 2.91, 1.08), (0.35, 2.91, 0.92)]) * [s, 1, 1])
        b.loft(limb_rings(horn, [0.13, 0.1, 0.06, 0], 4, (0, 1, 0), np.pi / 4), [one(D + "Head")] * 4,
               soft_rings=(1, 2), lean=UP)
        eye = big(np.array([(0.19, 2.8, 1.84), (0.31, 2.71, 1.72), (0.27, 2.69, 1.81)]) * [s, 1, 1])
        b.loft([eye, eye + unit([0.8 * s, 0.5, 0.2]) * 0.015], [one(D + "Head")] * 2)


# ---- the wings ----

# The right wing's landmarks in the rest pose (the wings spread), from the vendor's joints: the elbow E, wrist W; the
# two knuckles and the tip of the first, second and fourth fingers (the first the leading edge); S and H, the
# membrane's root on the body's shoulder line, over the shoulder and the hip.
WING = {
    "S": (0.72, 1.49, 0.38), "E": (1.14, 1.78, 0.4), "W": (1.9, 2.13, 1.02),
    "A1": (3.33, 2.53, 0.94), "B1": (4.7, 3.0, 0.77), "T1": (6.0, 3.39, 0.03),
    "A2": (3.41, 2.58, 0.47), "B2": (4.53, 2.93, 0.0), "T2": (5.49, 3.25, -0.79),
    "A4": (2.25, 2.21, 0.16), "B4": (2.55, 2.3, -0.5), "T4": (2.87, 2.39, -1.45),
    "H": (0.54, 1.38, -0.58),
}
# Shading normals for the membrane, by the bone a point rides (right side; the left mirrors them): the direction in
# its rest frame that the clips turn up, toward the camera, in the most frames of every clip. A membrane seen nearly
# edge-on, as the folded wings are on the ground, then stays dark where its facets' own normals lit it whole in the
# fresnel rim (as the trailer's vendor wings are lit).
WING_UP = {"arm": (-0.27, 0.89, 0.37), "1": (-0.3, 0.51, 0.81), "2": (-0.41, 0.59, 0.7), "4": (-0.39, 0.79, 0.46),
           "flank": (-0.03, 0.88, 0.47)}
SCALLOP = 0.14  # how far the trailing edge bows in toward the wrist between the tips


def scallop(a, b, n, depth, toward):
    pa, pb, pt = (np.asarray(x, np.float64) for x in (a, b, toward))
    out = []
    for i in range(1, n + 1):
        t = i / (n + 1)
        p = pa * (1 - t) + pb * t
        out.append(p + depth * 4 * t * (1 - t) * (pt - p))
    return out


def wing(b: Builder, s: int) -> None:
    """A thin plate between the wing's bones: the arm and the first finger its leading edge, two more fingers spoking
    from the wrist (the vendor's second and fourth; with all four the folded wings drew as stacks of finger blades
    under a sawtooth of tips), a trailing edge bowed in broad scallops between their tips and back to the flank; its
    root on the body, along the shoulder line from the shoulder to the hip, so the wing grows out of the body (inside
    it, the folded wings stood apart beside a thin spine). Every landmark rides its own bone, each point of the
    trailing edge the two fingers either side of it, so the edge stays taut between the tips as the wing folds
    (skinned by transfer from the vendor's membrane, those points rode one finger and zigzagged across the folded wing
    like a lightning bolt). A thick spar along the arm from the shoulder to the wrist, closing the outline."""
    side = "R" if s > 0 else "L"
    bone = {n: D + side + n for n in ("Wing1", "Wing2", "W12", "W13", "W22", "W23", "W42", "W43")}
    rides = {"S": D + "Spine3", "E": bone["Wing2"], "W": bone["Wing2"], "A1": bone["W12"], "B1": bone["W13"],
             "T1": bone["W13"], "A2": bone["W22"], "B2": bone["W23"], "T2": bone["W23"], "A4": bone["W42"],
             "B4": bone["W43"], "T4": bone["W43"], "H": D + "Pelvis"}
    shade = {"S": "flank", "E": "arm", "W": "arm", "H": "flank"}  # whose normal each point takes (a finger's, else)
    names = list(rides)
    pts = [np.array(WING[n]) * [s, 1, 1] for n in names]
    skins = [one(rides[n]) for n in names]
    ups = [WING_UP[shade.get(n, n[1:])] for n in names]
    idx = {n: i for i, n in enumerate(names)}
    w = pts[idx["W"]]
    # The trailing edge: one point between the first two tips, two in the broad panel between the second and the
    # fourth finger and two back to the flank, each skinned and shaded between the ends it hangs from.
    for a, c, n, ends in (("T1", "T2", 1, ("1", "2")), ("T2", "T4", 2, ("2", "4")), ("T4", "H", 2, ("4", "flank"))):
        toward = pts[idx["E"]] if c == "H" else w
        for k, p in enumerate(scallop(pts[idx[a]], pts[idx[c]], n, SCALLOP, toward)):
            t = (k + 1) / (n + 1)
            idx[f"{a}{c}{k}"] = len(pts)
            pts.append(p)
            skins.append(blend(rides[a], rides[c], t))
            ups.append(np.multiply(WING_UP[ends[0]], 1 - t) + np.multiply(WING_UP[ends[1]], t))
    P = lambda *ns: [idx[n] for n in ns]
    tris = [
        P("W", "A1", "A2"), P("A1", "B1", "A2"), P("A2", "B1", "B2"), P("B1", "T1", "B2"), P("B2", "T1", "T1T20"),
        P("B2", "T1T20", "T2"),
        P("W", "A2", "A4"), P("A2", "B2", "B4"), P("A2", "B4", "A4"), P("B2", "T2", "T2T40"), P("B2", "T2T40", "B4"),
        P("B4", "T2T40", "T2T41"), P("B4", "T2T41", "T4"),
        P("E", "W", "A4"), P("E", "A4", "B4"), P("E", "B4", "T4"), P("E", "T4", "T4H0"), P("E", "T4H0", "T4H1"),
        P("E", "T4H1", "H"), P("S", "E", "H"),
    ]
    # wound to face up (the rest pose's +y)
    pa = np.array(pts)
    tris = [t if np.cross(pa[t[1]] - pa[t[0]], pa[t[2]] - pa[t[0]])[1] > 0 else [t[0], t[2], t[1]] for t in tris]
    trailing = ["T1", "T1T20", "T2", "T2T40", "T2T41", "T4", "T4H0", "T4H1", "H"]
    lines = [P(a, c) for a, c in (("W", "A1"), ("A1", "B1"), ("B1", "T1"), ("W", "A2"), ("A2", "B2"), ("B2", "T2"),
                                   ("W", "A4"), ("A4", "B4"), ("B4", "T4"), ("S", "E"), ("E", "W"), ("S", "H"))]
    lines += [P(a, c) for a, c in pairwise(trailing)]
    b.plate(pa, tris, lines, skins, 0.03, 0.03, normals=np.array(ups) * [s, 1, 1])
    # The arm: a spar from the shoulder joint through the elbow, ending in a point just past the wrist (on along the
    # first finger, its lines ended in mid-membrane; a claw at the wrist drew a short bright bar).
    arm = [np.array([0.6 * s, 1.57, 0.4]), pts[idx["E"]], w, w + 0.18 * unit(pts[idx["A1"]] - w)]
    arm_skin = [one(bone["Wing1"]), blend(bone["Wing1"], bone["Wing2"]), one(bone["Wing2"]), one(bone["Wing2"])]
    b.loft(limb_rings(arm, [0.18, 0.14, 0.1, 0], 4, (0, 1, 0), np.pi / 4), arm_skin, soft_rings=(1, 2), lean=UP)


# ---- the legs ----

def leg(b: Builder, path, radii, skins, toes, palm) -> None:
    """A thick leg lofted through its joints in five-sided rings, drawn only along it (a line round its top floated as
    an open box beside the body from above), into a low wedge of a foot that narrows to a blunt point at the toes
    (separate claws and thin legs broke into scratches at game size, and a tall foot read as a diamond)."""
    b.loft(limb_rings(path, radii, 5, (1, 0, 0)), skins, soft_rings=range(len(path)), caps=(False, False), lean=UP)
    ankle = np.asarray(path[-1], np.float64)
    toes = np.asarray(toes, np.float64)
    mid = toes.mean(axis=0)
    fwd = unit((mid - ankle) * [1, 0, 1])
    across = unit(np.cross([0, 1, 0], fwd))
    width = radii[-1] * 1.3
    ground = 0.02
    heel = [ankle + across * width + [0, 0.03, 0], ankle - across * width + [0, 0.03, 0],
            ankle - across * width * 0.9 + [0, ground - ankle[1], 0], ankle + across * width * 0.9 + [0, ground - ankle[1], 0]]
    knuckles = [mid + across * width * 1.5 + [0, 0.02, 0], mid - across * width * 1.5 + [0, 0.02, 0],
                mid - across * width * 1.4 + [0, ground - mid[1], 0], mid + across * width * 1.4 + [0, ground - mid[1], 0]]
    point = mid + fwd * 0.22 + [0, ground + 0.01 - mid[1], 0]
    toe = {t: 1 / 3 for t in toes_bones(palm)}
    b.loft([heel, knuckles, point], [one(palm), toe, toe], soft_rings=(1,), lean=UP)


def toes_bones(palm: str) -> list[str]:
    side = palm[len(D)]
    kind = "HandToe" if "Arm" in palm else "LegToe"
    return [f"{side}{kind}{k}" for k in (1, 2, 3)]


def front_leg(b: Builder, s: int) -> None:
    side = "R" if s > 0 else "L"
    path = np.array([(0.3, 1.5, 0.7), (0.36, 1.05, 0.76), (0.36, 0.67, 0.5), (0.4, 0.12, 0.84)]) * [s, 1, 1]
    skins = [one(D + f"{side}Arm1"), blend(D + f"{side}Arm1", D + f"{side}Arm2"),
             blend(D + f"{side}Arm2", D + f"{side}Arm3"), blend(D + f"{side}Arm3", D + f"{side}ArmPalm")]
    toes = np.array([(0.26, 0.07, 1.07), (0.4, 0.08, 1.08), (0.53, 0.1, 1.08)]) * [s, 1, 1]
    leg(b, path, [0.25, 0.21, 0.17, 0.13], skins, toes, D + f"{side}ArmPalm")


def hind_leg(b: Builder, s: int) -> None:
    side = "R" if s > 0 else "L"
    # A muscled thigh bulging forward of the hip-knee line (straight, the legs read as pegs), the shin back to the
    # heel, the foot forward.
    path = np.array([(0.34, 1.3, -0.45), (0.38, 1.0, -0.36), (0.34, 0.7, -0.47), (0.3, 0.49, -0.95),
                     (0.28, 0.2, -0.85)]) * [s, 1, 1]
    skins = [one(D + f"{side}Leg1"), one(D + f"{side}Leg1"), blend(D + f"{side}Leg1", D + f"{side}Leg2"),
             blend(D + f"{side}Leg2", D + f"{side}Leg3"), blend(D + f"{side}Leg3", D + f"{side}LegPalm")]
    toes = np.array([(0.21, 0.1, -0.39), (0.32, 0.12, -0.36), (0.44, 0.09, -0.41)]) * [s, 1, 1]
    leg(b, path, [0.3, 0.34, 0.21, 0.16, 0.13], skins, toes, D + f"{side}LegPalm")


def build(source: Source) -> Mesh:
    b = Builder(source)
    spine(b)
    head(b)
    for s in (1, -1):
        wing(b, s)
        front_leg(b, s)
        hind_leg(b, s)
    return b.mesh()
