"""The Bandaged King (ps-8z4.20), Boss Rush boss 7, act 2: the vendor's mummy rebuilt whole as a few simple faceted
volumes, every edge it designs drawn, around what names it: a tall gaunt figure wound in slanting bands of bandage,
crowned with a pharaoh's headcloth (the nemes) and false beard, a broad royal collar over its shoulders, and the two
long bandage whips it lashes with.

The trailer's drawing of the vendor mesh (10,964 triangles) reads as a gaunt zombie traced in short dashes: its
sculpted ribs, sinews and knuckles break the body into open loops that never close or go dark, the face is a scribble,
and nothing on it says king. Its whips, a flat ribbon on eight whip bones the clips coil into the hand at rest, draw as
one long open line. So nothing of the vendor mesh is kept; every part is new, skinned to the vendor bones, and every
edge that draws is chosen here (a polygon's outline, never its triangles' diagonals, and only some of those):

- The trunk: one eight-sided loft from the hips through a narrow waist to a deep chest and square shoulders, wound in
  three broad bands: the rings drawn round it slant one way and the next, and only its two sides run along it.
- The limbs: each one four-sided prism through all its joints (shoulder, elbow, wrist; hip, knee, ankle), every long
  line drawn so each wrap meets them, and one slanting ring round each segment, the next slanting the other way:
  bandage wound round it. Six-sided limbs with two of their lines drawn left the wraps as unconnected dashes in the
  crowd; two wraps a segment were a scribble. Mitten hands and wedge feet, broad enough to anchor the gait.
- The head: a narrow face proud of the hood, a ridge down the nose, two slanted eye slits; the nemes over it, a ridged
  roof from the brow band (across the forehead and temples only: round the back too, nested in the hem from above, it
  read as a donut) flaring behind the ears to a broad hem, with two lappets falling in front of the shoulders and a
  cobra rearing at the brow; a false beard on the jaw bone. A flat-topped nemes drew as a box helmet, and at 1.2 times
  it hid the body from the 60-degree camera; this one is the vendor's size.
- The collar: a shallow cone of a plate round the neck, deeper in front; its rim and one band inside draw.
- The whips: each a flat strap through the vendor's eight whip bones, rigid on each, narrowing to a short point, every
  other ring drawn so it reads as a closed ribbon of bandage panels (a thin strap read as a stray scratch). They vanish
  into the hand while the clips coil the whip bones there and lash out with the attacks.

The trunk, legs and collar lean their normals a little (0.4) up, which keeps their long facets dark in the rim; the
head and arms keep their own (the clips raise the arms and tip the head). The mesh flags its edges, so it is drawn as
designed at every distance rather than decimated: 674 triangles at every LOD.

Source: Heroic Fantasy Creatures Full Pack Vol 1's Mummy.prefab (sha256
aa452be5f8ed513402a7cd1b5bd5f319c44484f82cae22894d46b27a41a17ef8) and SK_Mummy.FBX
(6468762111a06fc11258b047bcf56f7350f23230e01eb2ea23fbaf3956320ea1), as manifest.json lists them. Only its rig is used,
and its rest pose and skin weights to place and skin the shapes: each shape takes the weights of the vendor's nearest
vertices over its own bones (never across limbs or sides); the face and nemes are rigid on the head bone (the lappets
bend with the neck), the beard on the jaw bone, each whip ring on its own whip bone. No Blender: numpy only. Bake: `just champions-tool monster bake
bandagedKing`; review: `just champions-tool monster review bandagedKing` (and `--look trailer`).
"""

from __future__ import annotations

from itertools import pairwise

import numpy as np

from champions_tools.monster import Mesh, Source

X, Y, Z = np.eye(3)


def unit(v) -> np.ndarray:
    v = np.asarray(v, np.float64)
    return v / np.linalg.norm(v)


def ring(centre, u, v, radii, n: int, phase: float = 0.0) -> np.ndarray:
    """n points round `centre` in the plane of unit vectors u and v, radii (ru, rv), the first at u turned by phase."""
    t = phase + 2 * np.pi * np.arange(n) / n
    return np.asarray(centre, np.float64) + np.outer(np.cos(t) * radii[0], u) + np.outer(np.sin(t) * radii[1], v)


UP = (0.0, 1.0, 0.0)  # a lean up the trunk and the legs
LEAN = 0.4  # how far the shading normals lean (Builder.mesh)


class Builder:
    """Closed shapes of convex polygons, each wound outward; every polygon's outline draws unless the edge is hushed.
    Each shape is skinned rigidly to one bone, or by transfer from the vendor vertices near it over a set of bones."""

    def __init__(self, source: Source):
        self.source = source
        self.points: list[np.ndarray] = []
        self.skins: list[object] = []
        self.polys: list[list[int]] = []
        self.leans: list[np.ndarray] = []
        self.quiet: set[tuple[int, int]] = set()

    def shape(self, points, faces, skin, inside=None, lean=(0, 0, 0)) -> list[int]:
        """A closed shape: points (n, 3), faces (convex polygons of point indices) wound outward from `inside` (the
        shape's centroid, or one point per face); `skin` a bone name or a tuple of bone names to transfer over; `lean`
        a vector its shading normals lean along (see mesh)."""
        pts = np.asarray(points, np.float64)
        first = len(self.points)
        for p in pts:
            self.points.append(p)
            self.skins.append(skin)
        centre = pts.mean(axis=0)
        for i, f in enumerate(faces):
            ref = centre if inside is None else np.asarray(inside[i] if np.ndim(inside) == 2 else inside)
            self.face([first + k for k in f], ref, lean)
        return list(range(first, first + len(pts)))

    def face(self, poly, inside, lean=(0, 0, 0)) -> None:
        """A convex polygon of existing points, wound outward from the point `inside`."""
        corners = np.array([self.points[k] for k in poly])
        normal = np.cross(corners, np.roll(corners, -1, axis=0)).sum(axis=0)  # Newell's, as wound
        if normal @ (corners.mean(axis=0) - np.asarray(inside, np.float64)) < 0:
            poly = list(reversed(poly))
        self.polys.append(list(poly))
        self.leans.append(np.asarray(lean, np.float64))

    def loft(self, rings, skin, soft_rings=(), soft_sides=(), lean=(0, 0, 0)) -> list[list[int]]:
        """A shape lofted through rings of equal count (a ring may be one point, an apex), capped at both ends; each
        side face is wound outward from the axis between its two rings' centres. The edges round the rings in
        `soft_rings` and those along the shape from the ring corners in `soft_sides` don't draw. Returns each ring's
        point indices."""
        rings = [np.atleast_2d(np.asarray(r, np.float64)) for r in rings]
        n = max(len(r) for r in rings)
        pts, offsets = [], []
        for r in rings:
            offsets.append(len(pts))
            pts.extend(r)
        faces, inside = [], []
        for i in range(len(rings) - 1):
            a, b = offsets[i], offsets[i + 1]
            na, nb = len(rings[i]), len(rings[i + 1])
            mid = (rings[i].mean(axis=0) + rings[i + 1].mean(axis=0)) / 2
            for k in range(n):
                if na == 1:
                    faces.append([a, b + k, b + (k + 1) % nb])
                elif nb == 1:
                    faces.append([a + k, a + (k + 1) % na, b])
                else:
                    faces.append([a + k, a + (k + 1) % na, b + (k + 1) % nb, b + k])
                inside.append(mid)
        axis = rings[-1].mean(axis=0) - rings[0].mean(axis=0)
        if len(rings[0]) > 2:
            faces.append(list(range(offsets[0], offsets[0] + len(rings[0]))))
            inside.append(rings[0].mean(axis=0) + axis * 0.5)
        if len(rings[-1]) > 2:
            faces.append(list(range(offsets[-1], offsets[-1] + len(rings[-1]))))
            inside.append(rings[-1].mean(axis=0) - axis * 0.5)
        ids = self.shape(pts, faces, skin, np.array(inside), lean)
        out = [ids[o:o + len(r)] for o, r in zip(offsets, rings)]
        for i in soft_rings:
            for k in range(len(out[i])):
                self.hush(out[i][k], out[i][(k + 1) % len(out[i])])
        for k in soft_sides:
            for r0, r1 in pairwise(out):
                if len(r0) > 1 and len(r1) > 1:
                    self.hush(r0[k], r1[k])
        return out

    def hush(self, a: int, b: int) -> None:
        self.quiet.add((min(a, b), max(a, b)))

    def mesh(self) -> Mesh:
        """Each polygon as a fan whose triangles draw only the polygon's outline; every triangle its own corners,
        their normal its facet's leaning along its shape's lean (a face turned against the lean keeps its own)."""
        def key(a: int, b: int) -> tuple[int, int]:
            return min(a, b), max(a, b)

        pts = np.array(self.points)
        tris, flags, leans = [], [], []
        for poly, lean in zip(self.polys, self.leans):
            n = len(poly)
            outline = {key(poly[i], poly[(i + 1) % n]) for i in range(n)} - self.quiet
            for i in range(1, n - 1):
                t = (poly[0], poly[i], poly[i + 1])
                tris.append(t)
                flags.append([int(key(t[1], t[2]) in outline), int(key(t[2], t[0]) in outline),
                              int(key(t[0], t[1]) in outline)])
                leans.append(lean)
        tris, leans = np.array(tris), np.array(leans)
        p = pts[tris]
        normal = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        length = np.linalg.norm(normal, axis=1, keepdims=True)
        normal = np.where(length > 1e-12, normal / np.maximum(length, 1e-12), Y)
        facing = np.einsum("ij,ij->i", normal, leans) >= -0.5 * np.linalg.norm(leans, axis=1)
        normal += LEAN * leans * facing[:, None]
        normal /= np.linalg.norm(normal, axis=1, keepdims=True)
        bones, weights = self.skin()
        corners = tris.reshape(-1)
        return Mesh(pts[corners].astype(np.float32), np.arange(len(corners), dtype=np.int32).reshape(-1, 3),
                    bones[corners], weights[corners], np.repeat(normal, 3, axis=0).astype(np.float32),
                    None, np.array(flags, np.uint8))

    def skin(self) -> tuple[np.ndarray, np.ndarray]:
        """Each point's four bones: one bone with weight 1, or the weights of the vendor vertices nearest it, blended
        by inverse distance and kept to the shape's bones."""
        s = self.source
        vp = s.positions.astype(np.float64)
        vb, vw = s.bones, s.weights.astype(np.float64)
        bones = np.zeros((len(self.points), 4), np.int32)
        weights = np.zeros((len(self.points), 4), np.float32)
        for i, (p, skin) in enumerate(zip(self.points, self.skins)):
            if isinstance(skin, str):
                bones[i, 0], weights[i, 0] = s.bone(skin), 1
                continue
            allowed = np.zeros(len(s.bone_names), bool)
            allowed[[s.bone(n) for n in skin]] = True
            near = allowed[vb][np.arange(len(vb)), np.argmax(vw, axis=1)]  # vendor vertices led by an allowed bone
            d = np.linalg.norm(vp[near] - p, axis=1)
            dense = np.zeros(len(s.bone_names))
            for v in np.argsort(d)[:8]:
                np.add.at(dense, vb[near][v], vw[near][v] / (d[v] + 0.01))
            dense[~allowed] = 0
            top = np.argsort(-dense)[:4]
            bones[i], weights[i] = top, dense[top] / dense[top].sum()
        return bones, weights


def bone(name: str) -> str:
    return "MUMMY_ " + name


def wrapped(joints, radii, n=6, tilts=None, ref=Z) -> list[np.ndarray]:
    """Rings of an n-sided prism through a limb's joints, each square to the mean of the segments either side, a
    corner toward `ref`, and tipped along the limb by its tilt (the fraction of its radius its far side rises): tipped
    rings drawn round a limb read as bandage wound round it."""
    joints = [np.asarray(j, np.float64) for j in joints]
    segments = [unit(c - a) for a, c in pairwise(joints)]
    tilts = tilts or [0.0] * len(joints)
    rings = []
    for i, (j, r, tilt) in enumerate(zip(joints, radii, tilts)):
        d = unit(segments[max(i - 1, 0)] + segments[min(i, len(segments) - 1)])
        u = unit(ref - (ref @ d) * d)
        v = np.cross(d, u)
        ru, rv = (r, r) if np.isscalar(r) else r
        pts = ring(j, u, v, (ru, rv), n)
        pts += np.outer(((pts - j) @ v) * tilt, d)  # tipped about u: a wrap's slant
        rings.append(pts)
    return rings


def lerp(a, b, t: float) -> np.ndarray:
    return np.asarray(a, np.float64) + (np.asarray(b, np.float64) - np.asarray(a, np.float64)) * t


TRUNK = tuple(bone(n) for n in ("Pelvis", "Spine", "Spine1", "Neck", "L Clavicle", "R Clavicle"))


def section(y, zc, rx, front, back, tilt=0.0):
    """An eight-sided ring of the trunk at height y, x by rx, forward to zc + front and back to zc - back; tipped
    across the body by `tilt` (its right side higher), a band of bandage wound round it."""
    t = np.radians([0, 45, 90, 135, 180, 225, 270, 315])
    c, s = np.cos(t), np.sin(t)
    z = np.where(s > 0, front * s, back * s)
    pts = np.stack([rx * c, np.full(len(t), y), zc + z], axis=1)
    pts[:, 1] += tilt * pts[:, 0]
    return pts


def torso(b: Builder) -> None:
    """A lean trunk from the hips through a narrow waist to a deep chest and square shoulders, wound in three broad
    slanting bands of bandage: the rings drawn round it slant one way and the next, and nothing runs along it but the
    two sides."""
    rings = [section(1.86, 0.04, 0.34, 0.2, 0.2),
             section(2.12, 0.05, 0.33, 0.2, 0.2, 0.22),
             section(2.42, 0.07, 0.36, 0.24, 0.2, -0.22),
             section(2.72, 0.07, 0.46, 0.28, 0.24, 0.18),
             section(2.98, 0.0, 0.5, 0.22, 0.24),
             section(3.1, -0.04, 0.2, 0.15, 0.15)]
    b.loft(rings, TRUNK, soft_rings=(4,), soft_sides=(1, 2, 3, 5, 6, 7), lean=UP)


HEAD = bone("Head")
HEAD_MID = np.array([0.0, 3.34, 0.06])
HEAD_SCALE = 1.05


def at(points) -> np.ndarray:
    return HEAD_MID + (np.asarray(points, np.float64) - HEAD_MID) * HEAD_SCALE


def head(b: Builder) -> None:
    """A wrapped skull's face under the nemes, proud of the hood so it shows from above: a narrow faceted wedge from
    the brow to the chin, the eyes two slanted slits; the nemes over it; a long false beard on the chin."""
    # the face: a loft from the chin up to the brow, its front a ridge down the nose
    def fsec(y, w, front, back):
        return at([(w, y, back), (w * 0.8, y, front - 0.05), (0, y, front), (-w * 0.8, y, front - 0.05), (-w, y, back)])
    face = [fsec(3.18, 0.1, 0.24, 0.02), fsec(3.32, 0.17, 0.31, 0.0), fsec(3.48, 0.17, 0.3, 0.0)]
    b.loft(face, HEAD, soft_rings=(1,))
    for s in (-1, 1):  # an eye: a thin slanted slit across each half of the face
        e = at([(s * 0.035, 3.41, 0.3), (s * 0.145, 3.455, 0.262), (s * 0.13, 3.395, 0.266)])
        n = unit(np.cross(e[1] - e[0], e[2] - e[0]))
        if n[2] < 0:
            n = -n
        b.loft([e - n * 0.01, e + n * 0.012], HEAD, soft_rings=(0,))
    nemes(b)
    beard(b)


def nemes(b: Builder) -> None:
    """The nemes: a ridged roof over the skull, its brow band straight across the forehead, flaring down behind the
    ears to a broad hem over the shoulders, with two lappets falling in front of the shoulders either side of the
    beard: from the 60-degree camera, a pointed hood rather than a helmet. The ridge, the front edges, the brow band and
    the hem draw; a cobra rears at the brow."""
    # rings from the top down: right front, right temple, right back, back middle, left back, left temple, left front
    # (a seven-sided hood, its front plane behind the face)
    def hsec(y, w, front, back):
        return at([(w, y, front), (w, y, back * 0.4), (w * 0.7, y, back), (0, y, back - 0.03),
                   (-w * 0.7, y, back), (-w, y, back * 0.4), (-w, y, front)])
    ridge = hsec(3.72, 0.015, 0.14, -0.12)
    brow = hsec(3.52, 0.2, 0.2, -0.19)
    temple = hsec(3.32, 0.24, 0.08, -0.24)
    low = hsec(3.04, 0.38, 0.0, -0.24)
    ids = b.loft([ridge, brow, temple, low], HEAD, soft_rings=(2,), soft_sides=(1, 2, 4, 5))
    for k in range(1, 5):  # the brow band runs across the forehead and temples only: round the back as well, it
        b.hush(ids[1][k], ids[1][k + 1])  # nested inside the hem from above, a donut
    # the lappets: a broad flat band from each temple down over the front of the shoulder, bending with the neck
    lappet = (HEAD, bone("Neck"), bone("Spine1"))
    for s in (-1, 1):
        m = np.array([s, 1, 1])
        rings = [at([(0.17, 3.3, 0.06), (0.26, 3.3, 0.06), (0.26, 3.3, 0.0), (0.17, 3.3, 0.0)]) * m,
                 at([(0.18, 2.88, 0.15), (0.34, 2.88, 0.15), (0.34, 2.88, 0.08), (0.18, 2.88, 0.08)]) * m]
        b.loft(rings, lappet)
    # the uraeus: a cobra's hooded head rearing from the brow band
    root = at([(0.0, 3.56, 0.22)])[0]
    hood = [root + (0.05, 0.0, -0.02), root + (-0.05, 0.0, -0.02), root + (0.0, 0.0, -0.06)]
    b.shape(np.vstack([hood, root + (0.0, 0.17, 0.03)]), [[0, 1, 2], [0, 1, 3], [1, 2, 3], [2, 0, 3]], HEAD)


def beard(b: Builder) -> None:
    """The false beard: a long narrow four-sided prism from the chin, down and a little forward, to a squared end,
    on the jaw bone so it drops when the mouth opens."""
    jb = bone("Queue de cheval 1")
    top = at([(0.05, 3.17, 0.2), (-0.05, 3.17, 0.2), (-0.05, 3.15, 0.1), (0.05, 3.15, 0.1)])
    end = at([(0.04, 2.92, 0.24), (-0.04, 2.92, 0.24), (-0.04, 2.92, 0.16), (0.04, 2.92, 0.16)])
    b.loft([top, end], jb)


def collar(b: Builder) -> None:
    """The broad collar (an usekh): a shallow cone of a plate round the neck over the shoulders and chest, deeper in
    front; its outer rim and one band inside it draw, its underside doesn't."""
    n = 12
    t = 2 * np.pi * np.arange(n) / n
    c, s = np.cos(t), np.sin(t)

    def rim(rx, front, back, y, drop_front):
        z = np.where(c > 0, front * c, back * c)  # c along z: forward
        return np.stack([rx * s, y - drop_front * c.clip(0), 0.0 + z], axis=1)
    inner = rim(0.2, 0.17, 0.15, 3.08, 0.0)
    mid = rim(0.38, 0.3, 0.24, 3.0, 0.08)
    outer = rim(0.56, 0.42, 0.3, 2.9, 0.18)
    under = outer - (0, 0.05, 0)
    bones = (bone("Spine1"), bone("Neck"), bone("L Clavicle"), bone("R Clavicle"))
    b.loft([inner, mid, outer, under, inner - (0, 0.08, 0)], bones, soft_rings=(0, 3, 4), soft_sides=tuple(range(n)),
           lean=UP)


ARM = [(0.3, 2.94, -0.02), (0.87, 2.6, 0.01), (1.35, 2.3, 0.02)]  # shoulder, elbow, wrist (rest, the right arm)


def arms(b: Builder) -> None:
    """Long arms, each one four-sided prism from the shoulder through the elbow to the wrist, every long line drawn,
    wound by a slanting wrap round each segment, slanting opposite ways; a mitten of a hand."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        m = np.array([s, 1, 1])
        sh, el, wr = (np.array(j) * m for j in ARM)
        joints = [sh, lerp(sh, el, 0.5), el, lerp(el, wr, 0.5), wr]
        rings = wrapped(joints, [0.16, 0.14, 0.13, 0.13, 0.1], n=4, tilts=[0, 1.0 * s, 0, -1.0 * s, 0], ref=Y)
        bones = tuple(bone(f"{side} {n}") for n in ("Clavicle", "UpperArm", "Forearm", "Hand"))
        b.loft(rings, bones, soft_rings=(0, 2))
        hand(b, s, side, wr)


def hand(b: Builder, s: int, side: str, wrist) -> None:
    """A mitten: a flat block from the wrist to the knuckles narrowing to the fingers' ends."""
    hb = bone(f"{side} Hand")
    fingers = tuple(bone(f"{side} Finger{f}") for f in ("1", "2", "3", "4", "11", "21", "31", "41", "12", "22", "32",
                                                        "42", "0", "01", "02"))
    d = unit(np.array([1.0, -0.75, 0.05]) * np.array([s, 1, 1]))
    across = unit(np.cross(d, Y))
    up = unit(np.cross(across, d))

    def box(c, w, t):
        return [c + across * w + up * t, c - across * w + up * t, c - across * w - up * t, c + across * w - up * t]
    k = wrist + d * 0.18
    tip = wrist + d * 0.38
    b.loft([box(wrist, 0.09, 0.07), box(k, 0.13, 0.065), box(tip, 0.1, 0.03)], (hb, *fingers), soft_rings=(1,))


def legs(b: Builder) -> None:
    """Long legs, each one four-sided prism from the hip through the knee to the ankle, every long line drawn,
    wound by a slanting wrap round each segment, slanting opposite ways; a wedge of a foot."""
    for s in (-1, 1):
        side = "L" if s < 0 else "R"
        m = np.array([s, 1, 1])
        hip, knee, ankle = (np.array(j) * m for j in [(0.2, 1.9, 0.04), (0.32, 1.04, 0.08), (0.44, 0.22, -0.04)])
        joints = [hip, lerp(hip, knee, 0.5), knee, lerp(knee, ankle, 0.5), ankle]
        rings = wrapped(joints, [0.2, 0.18, 0.14, 0.13, 0.1], n=4, tilts=[0, -0.9 * s, 0, 0.9 * s, 0],
                        ref=unit((1, 0, 1)))
        bones = tuple(bone(f"{side} {n}") for n in ("Thigh", "Calf", "Foot")) + (bone("Pelvis"),)
        b.loft(rings, bones, soft_rings=(0, 2), lean=UP)
        fb = (bone(f"{side} Foot"), bone(f"{side} Toe0"))
        heel, toe = np.array([0.44, 0.17, -0.1]) * m, np.array([0.48, 0.05, 0.36]) * m

        def box(c, w, top, bottom):
            return [c + (w, top, 0), c + (-w, top, 0), c + (-w, bottom, 0), c + (w, bottom, 0)]
        b.loft([box(heel, 0.12, 0.08, -0.16), box(toe, 0.12, 0.03, -0.05)], fb, lean=UP)


def whips(b: Builder) -> None:
    """The bandage whips: from each hand a long flat strap through the vendor's eight whip bones, rigid on each,
    narrowing to a point; only its four long lines draw, so it reads as a strap of bandage face on and one line edge
    on. At rest the clips coil every whip bone into the hand, so the whip vanishes there and lashes out with the
    attacks."""
    s_ = b.source
    lead = s_.bones[np.arange(len(s_.positions)), np.argmax(s_.weights, axis=1)]
    for side in ("L", "R"):
        names = [n for n in s_.bone_names if n.startswith(f"MUMMY_BANDAGE_ARM_{side}_") and not n.endswith("nub")]
        names.sort(key=lambda n: int("".join(ch for ch in n.rsplit("_", 1)[1] if ch.isdigit())))
        pts = [s_.positions[lead == s_.bone(n)].astype(np.float64).mean(axis=0) for n in names]
        width = np.linspace(0.15, 0.08, len(pts))
        rings = []
        for i, (p, w) in enumerate(zip(pts, width)):
            d = unit(pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)])
            u = unit(Z - (Z @ d) * d)
            rings.append(ring(p, u, np.cross(d, u), (w, 0.035), 4))
        tip = pts[-1] + unit(pts[-1] - pts[-2]) * 0.3
        ids = b.loft(rings + [tip], names[0], soft_rings=tuple(range(1, len(rings), 2)), soft_sides=(1, 3))
        for i, n in enumerate(names):  # each ring rigid on its own whip bone
            for k in ids[i]:
                b.skins[k] = n
        b.skins[ids[-1][0]] = names[-1]


def build(source: Source) -> Mesh:
    b = Builder(source)
    torso(b)
    head(b)
    collar(b)
    arms(b)
    legs(b)
    whips(b)
    return b.mesh()
