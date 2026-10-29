"""Anatomical replacement, independent of A. Blender 4.5.3 background Python.

Source topology is retained/reduced only for selected complex regions (skull,
open rib cages, pelvic openings, equipment). Long bones are new tapered prisms.
Transfer is restricted to each source renderer and, for paired armour, its side.
Every face takes a source UV triplet without interpolating colour-ID coordinates.
"""
from __future__ import annotations

import json
import math
from collections import defaultdict
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "demo3d/Assets/Baked/SkeletonLod"
SOURCE = json.loads((OUT / "source.json").read_text())
P = np.array(SOURCE["positions"])
BI = np.array(SOURCE["boneIndices"], int)
BW = np.array(SOURCE["boneWeights"])
FACES = np.concatenate(SOURCE["submeshes"]).reshape(-1, 3)
RESULT = {k: [] for k in ("positions", "normals", "tangents", "uv", "boneIndices", "boneWeights")}
RESULT["submeshes"] = [[] for _ in SOURCE["submeshes"]]
BUDGET = []


def skin_key(i):
    return tuple((int(b), round(float(w), 6)) for b, w in zip(BI[i], BW[i]) if w > 1e-6)


def source_part(region, side=None):
    ids = np.arange(region["first"], region["first"] + region["count"])
    faces = FACES[np.all(np.isin(FACES, ids), axis=1)]
    if side is not None:
        faces = faces[np.mean(P[faces, 0], axis=1) * side > 0]
    return faces


def reduced(faces, budget, label):
    # Weld only within this anatomical renderer and identical influences. Blender
    # does not need UV/shading splits for decimation; transfer restores face seams.
    verts, tris, remap = [], [], {}
    for tri in faces:
        face = []
        for i in tri:
            key = (*np.round(P[i], 6), skin_key(i))
            if key not in remap:
                remap[key] = len(verts)
                verts.append(P[i].tolist())
            face.append(remap[key])
        if len(set(face)) == 3:
            tris.append(face)
    mesh = bpy.data.meshes.new(label)
    mesh.from_pydata(verts, [], tris)
    mesh.update()
    obj = bpy.data.objects.new(label, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if len(tris) > budget:
        mod = obj.modifiers.new("Region reduction", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = max(0.01, budget / len(tris))
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    out = [[list(obj.data.vertices[i].co) for i in poly.vertices] for poly in obj.data.polygons]
    obj.select_set(False)
    return out


def prism(faces, sides=5):
    # Principal axis of this long-bone renderer, capped tapered two-ring prism.
    pts = P[np.unique(faces)]
    center = pts.mean(0)
    _, _, axes = np.linalg.svd(pts - center, full_matrices=False)
    axis, x, y = axes
    if np.dot(np.cross(x, y), axis) < 0:
        y = -y
    along = (pts - center) @ axis
    lo, hi = along.min(), along.max()
    rings = []
    for t in (lo, hi):
        near = pts[np.abs(along - t) < (hi - lo) * 0.35]
        offsets = near - (center + axis * t)
        rx = max(0.006, float(np.max(np.abs(offsets @ x))) * 0.88)
        ry = max(0.006, float(np.max(np.abs(offsets @ y))) * 0.88)
        rings.append([center + axis * t + x * rx * math.cos(2 * math.pi * i / sides)
                      + y * ry * math.sin(2 * math.pi * i / sides) for i in range(sides)])
    triangles = []
    for i in range(sides):
        j = (i + 1) % sides
        triangles.extend([[rings[0][i], rings[0][j], rings[1][j]],
                          [rings[0][i], rings[1][j], rings[1][i]]])
    for i in range(1, sides - 1):
        triangles.extend([[rings[0][0], rings[0][i + 1], rings[0][i]],
                          [rings[1][0], rings[1][i], rings[1][i + 1]]])
    return triangles


def box(faces):
    pts = P[np.unique(faces)]
    center = pts.mean(0)
    _, _, axes = np.linalg.svd(pts - center, full_matrices=False)
    coords = (pts - center) @ axes.T
    lo, hi = coords.min(0), coords.max(0)
    vertices = [center + np.array([hi[k] if i & (1 << k) else lo[k] for k in range(3)]) @ axes for i in range(8)]
    triangles = []
    for quad in ((0,1,3,2), (4,6,7,5), (0,4,5,1), (2,3,7,6), (0,2,6,4), (1,5,7,3)):
        for face in ((quad[0],quad[1],quad[2]), (quad[0],quad[2],quad[3])):
            tri = [vertices[i] for i in face]
            normal = np.cross(tri[1]-tri[0], tri[2]-tri[0])
            if np.dot(normal, np.mean(tri,axis=0)-center) < 0:
                tri[1],tri[2] = tri[2],tri[1]
            triangles.append(tri)
    return triangles


def transfer(triangles, source_faces, label, budget):
    # BVH contains exactly this anatomical region. Opposite limbs, fingers and
    # equipment are never candidates for a nearest-surface lookup.
    ids = np.unique(source_faces)
    remap = {int(v): i for i, v in enumerate(ids)}
    local = [[remap[int(v)] for v in f] for f in source_faces]
    tree = BVHTree.FromPolygons([Vector(P[i]) for i in ids], local, all_triangles=True)
    max_dist = 0
    for tri in triangles:
        tri = [Vector(v) for v in tri]
        normal = (tri[1] - tri[0]).cross(tri[2] - tri[0]).normalized()
        if normal.length < 0.5:
            continue
        centroid = sum(tri, Vector()) / 3
        _, near_normal, face_id, _ = tree.find_nearest(centroid)
        if normal.dot(near_normal) < 0:
            tri[1], tri[2] = tri[2], tri[1]
            normal = -normal
        # Whole face appearance uses the nearest source face's discrete palette
        # coordinates (three corners); it never averages neighbouring UV colours.
        appearance_face = source_faces[face_id]
        for corner, pos in enumerate(tri):
            nearest, _, source_id, dist = tree.find_nearest(pos)
            max_dist = max(max_dist, dist)
            source_tri = source_faces[source_id]
            a, b, c = [Vector(P[i]) for i in source_tri]
            bary = barycentric_transform(nearest, a, b, c,
                                         Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
            bary = np.maximum(np.array(bary), 0)
            bary /= max(bary.sum(), 1e-12)
            influences = defaultdict(float)
            for v, factor in zip(source_tri, bary):
                for entry, weight in zip(BI[v], BW[v]):
                    influences[int(entry)] += float(factor * weight)
            top = sorted(((i, w) for i, w in influences.items() if w > 1e-8), key=lambda x: (-x[1], x[0]))[:4]
            total = sum(w for i, w in top)
            indices = [i for i, w in top] + [0] * (4 - len(top))
            weights = [w / total for i, w in top] + [0] * (4 - len(top))
            RESULT["positions"].append(list(pos))
            RESULT["normals"].append(list(normal))
            tangent = normal.cross(Vector((0, 1, 0)))
            if tangent.length < 1e-5:
                tangent = normal.cross(Vector((1, 0, 0)))
            tangent.normalize()
            RESULT["tangents"].append([*tangent, 1])
            RESULT["uv"].append(SOURCE["uv"][int(appearance_face[corner])])
            RESULT["boneIndices"].append(indices)
            RESULT["boneWeights"].append(weights)
            RESULT["submeshes"][0].append(len(RESULT["positions"]) - 1)
    BUDGET.append({"region": label, "budget": budget, "triangles": len(triangles),
                       "maxTransferDistanceM": max_dist})


def choose(name):
    # Explicit per-region allocation; sum recorded after geometry generation.
    if name == "pCube129": return 130, "reduce"  # skull: eye/nose openings
    if name == "pCube131": return 50, "reduce"   # jaw, independently animated
    if name == "lp_Helmet": return 80, "reduce"
    if name == "Shield_Main_LP": return 54, "reduce"
    if name == "Sword_LP": return 40, "reduce"
    if name == "Shield_Straps_LP": return 12, "reduce"
    if name in ("pCube3", "pCube8", "pCube17", "pCube103", "pCube104"): return 30, "reduce"
    if name in ("pCube125", "pCube137"): return 18, "reduce"  # clavicles
    if name in ("pCube130", "pCube136"): return 30, "reduce"  # pelvis, keep gaps
    if name == "pCube55": return 12, "reduce"
    if name.startswith("polySurface"): return 2, "reduce"  # teeth keep their rig entries
    if name.startswith("pCube"):
        n = int(name[5:])
        if n in (126, 127, 128, 132, 133, 134, 135, 138, 139, 140, 57, 86): return 16, "prism"
        if 75 <= n <= 84 or n in (42, 43, 44, 46): return 4, "prism3"
        if n in (73, 102): return 12, "reduce"
        if n in (56, 85): return 6, "reduce"
        if 18 <= n <= 37 or 105 <= n <= 124: return 4, "prism3"
        if 58 <= n <= 72 or 87 <= n <= 101: return 4, "prism3"
    armour = {"boots_lp": 44, "boots_straps_LP": 4, "boot_straps_LP_Left": 4,
              "Bracer_Straps_LP": 12, "Bracers2_LP": 8, "Bracers_LP": 20,
              "Chest_2_lp": 12, "chest_lp": 20, "Crotch_LP": 8,
              "Hand_Wraps_lp": 8, "Hips_Inner_lp": 12, "Hips_Outer_LP": 16,
              "lp_Chestplate": 18, "Pauldrons_Lower_LP": 16, "Pauldrons_Upper_LP": 20,
              "Shinguards_lp1": 16}
    return armour.get(name, 4), "reduce"


def main():
    assert bpy.app.version[:3] == (4, 5, 3), bpy.app.version_string
    assert len(SOURCE["submeshes"]) == 1, "recipe needs an explicit material allocation for >1 slot"
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    # Replace three detailed segments per digit with one independently skinned
    # tapered tetrahedron; the matching source digit alone supplies its weights.
    digit_groups = [[18,22,23,24], [19,25,26,27], [20,28,29,30], [21,31,32,33], [34,35,36],
                    [105,109,110,111], [106,112,113,114], [107,115,116,117], [108,118,119,120], [121,122,123],
                    [58,59,60], [62,63,61], [64,65,66], [67,68,69], [70,71,72],
                    [101,100,99], [97,96,98], [95,94,93], [92,91,90], [89,88,87]]
    grouped = {"pCube" + str(n) for g in digit_groups for n in g}
    regions = []
    for group in digit_groups:
        parts = [r for r in SOURCE["regions"] if r["name"].split("/")[-1] in {"pCube" + str(n) for n in group}]
        regions.append(({"name": "Digit_" + str(group[0])}, np.concatenate([source_part(r) for r in parts]), 4, "prism3"))
    for r in SOURCE["regions"]:
        name = r["name"].split("/")[-1]
        if name in grouped or name in ("Bracer_Straps_LP", "Hand_Wraps_lp", "boots_straps_LP", "boot_straps_LP_Left"):
            continue  # small straps consolidated into the bracer/boot silhouette
        budget, method = choose(name)
        regions.append((r, source_part(r), budget, method))
    for r, faces, budget, method in regions:
        name = r["name"].split("/")[-1]
        # Paired armour is split by side before any reduction/transfer.
        paired = P[np.unique(faces), 0].min() < -0.03 and P[np.unique(faces), 0].max() > 0.03 and name in (
            "boots_lp", "Bracer_Straps_LP", "Bracers2_LP", "Bracers_LP", "Hand_Wraps_lp",
            "Hips_Inner_lp", "Hips_Outer_LP", "Pauldrons_Lower_LP", "Pauldrons_Upper_LP", "Shinguards_lp1")
        for side in ((-1, 1) if paired else (None,)):
            part = source_part(r, side) if paired else faces
            if not len(part): continue
            count = max(2, budget // 2) if paired else budget
            label = name + (f"_{side}" if paired else "")
            if method.startswith("prism"):
                tris = prism(part, 3 if method == "prism3" else 5)
                if count == 4:
                    # Finger/toe segment: a tetrahedron fits between its end rings,
                    # no whole-hand fusion or cross-finger skin transfer.
                    pts = P[np.unique(part)]
                    center = pts.mean(0)
                    _, _, axes = np.linalg.svd(pts - center, full_matrices=False)
                    coords = (pts - center) @ axes.T
                    lo, hi = coords[:, 0].min(), coords[:, 0].max()
                    rad = max(0.002, np.max(np.abs(coords[:, 1:])))
                    a = center + axes[0] * lo
                    b = center + axes[0] * hi + axes[1] * rad
                    c = center + axes[0] * hi - axes[1] * rad / 2 + axes[2] * rad
                    d = center + axes[0] * hi - axes[1] * rad / 2 - axes[2] * rad
                    tris = [[a, b, c], [a, d, b], [a, c, d], [b, d, c]]
                transfer(tris, part, label, count)
            else:
                armour_box = not name.startswith(("pCube", "polySurface", "Digit_")) and name not in ("Sword_LP", "Shield_Main_LP", "Shield_Straps_LP", "lp_Helmet")
                triangles = box(part) if armour_box else reduced(part, count, label)
                transfer(triangles, part, label, 12 if armour_box else count)
    (OUT / "B.json").write_text(json.dumps(RESULT, separators=(",", ":")))
    triangles = sum(len(s) for s in RESULT["submeshes"]) // 3
    (OUT / "B-recipe.json").write_text(json.dumps({"blender": bpy.app.version_string,
        "buildHash": bpy.app.build_hash.decode(), "license": "GPL-3.0-or-later",
        "source": "source.json (LOD0, independent of A)", "triangles": triangles,
        "vertices": len(RESULT["positions"]), "budget": BUDGET}, indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "B-regions.blend"))
    print("B:", triangles, "triangles", len(RESULT["positions"]), "vertices")


if __name__ == "__main__":
    main()
