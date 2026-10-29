"""Cost-budgeted B: reconstructed long bones/digits, region-budgeted authored shells.

Blender 4.5.3 + meshoptimizer v0.25, independent LOD0 input. Preserves the old
B.json; writes B-matched.json and per-region settings under ignored Baked.
"""
from __future__ import annotations

import ctypes as ct
import json
import os
import sys
from pathlib import Path

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
import skeleton_lod_b as base

SCALE = float(os.environ.get("SKELETON_B_BUDGET_SCALE", "0.75"))
TOOLS = Path(os.environ.get("SKELETON_LOD_TOOLS", Path.home() / ".local/share/skeleton-lod-tools"))
LIB = ct.CDLL(str(TOOLS / "libmeshoptimizer-v0.25.dylib"))
FP, IP, BP = ct.POINTER(ct.c_float), ct.POINTER(ct.c_uint), ct.POINTER(ct.c_ubyte)
SIMPLIFY = LIB.meshopt_simplifyWithAttributes
SIMPLIFY.argtypes = [IP, IP, ct.c_size_t, FP, ct.c_size_t, ct.c_size_t, FP, ct.c_size_t,
                    FP, ct.c_size_t, BP, ct.c_size_t, ct.c_float, ct.c_uint, FP]
SIMPLIFY.restype = ct.c_size_t
P = np.ascontiguousarray(base.P, dtype=np.float32)
N = np.asarray(base.SOURCE["normals"], dtype=np.float32)
UV = np.asarray(base.SOURCE["uv"], dtype=np.float32)
ATTR = np.ascontiguousarray(np.column_stack((N, UV)))
WEIGHTS = np.array([.1, .1, .1, 1, 1], dtype=np.float32)
REGIONS = []


def allocation(name):
    explicit = {"pCube129": 240, "pCube131": 80, "lp_Helmet": 150,
                "Shield_Main_LP": 140, "Sword_LP": 70, "Shield_Straps_LP": 24,
                "pCube55": 16, "boots_lp": 100, "Bracers2_LP": 20,
                "Bracers_LP": 70, "Chest_2_lp": 30, "chest_lp": 70,
                "Crotch_LP": 30, "Hips_Inner_lp": 30, "Hips_Outer_LP": 40,
                "lp_Chestplate": 70, "Pauldrons_Lower_LP": 50,
                "Pauldrons_Upper_LP": 70, "Shinguards_lp1": 50}
    if name in explicit:
        return explicit[name]
    if name in ("pCube3", "pCube8", "pCube17", "pCube103", "pCube104"):
        return 55
    if name in ("pCube125", "pCube137"):
        return 30
    if name in ("pCube130", "pCube136"):
        return 45
    if name in ("pCube73", "pCube102"):
        return 24
    if name in ("pCube56", "pCube85"):
        return 8
    return base.choose(name)[0]


def split_surfaces(faces):
    groups = {}
    for i in np.unique(faces):
        groups.setdefault(tuple(P[i]), []).append(int(i))
    representative = {i: groups[tuple(P[i])][0] for i in np.unique(faces)}
    parent = {}
    def find(v):
        parent.setdefault(v, v)
        while parent[v] != v:
            parent[v] = parent[parent[v]]
            v = parent[v]
        return v
    for tri in faces:
        for v in tri[1:]:
            parent[find(representative[v])] = find(representative[tri[0]])
    components = {}
    for tri in faces:
        components.setdefault(find(representative[tri[0]]), []).append(tri)
    return components


def append_source(faces, budget, name):
    # Region-local operation: no collapses between unrelated body parts. Keep
    # discontinuous UV islands and weight discontinuities at coincident seams.
    flags = np.zeros(len(P), dtype=np.uint8)
    groups = {}
    for i in np.unique(faces):
        groups.setdefault(tuple(P[i]), []).append(int(i))
    for ids in groups.values():
        if len(ids) > 1 and (np.max(np.linalg.norm(UV[ids] - UV[ids[0]], axis=1)) > .025 or
                             any(base.skin_key(i) != base.skin_key(ids[0]) for i in ids[1:])):
            flags[ids] = 2
    # Allocate inside each connected surface too. Otherwise a shallow shin plate
    # can disappear while the adjacent thicker knee consumes the region budget.
    components = split_surfaces(faces)
    all_indices = []
    errors = []
    counts = []
    for component in components.values():
        tris = np.asarray(component, dtype=np.uint32)
        q = P[tris]
        protected_face = tris[np.argmax(np.linalg.norm(np.cross(q[:, 1]-q[:, 0], q[:, 2]-q[:, 0]), axis=1))]
        for v in protected_face:
            flags[groups[tuple(P[v])]] |= 1
        target = max(4, round(budget * len(tris) / len(faces)))
        indices = tris.reshape(-1)
        result = np.empty_like(indices)
        error = ct.c_float()
        count = SIMPLIFY(result.ctypes.data_as(IP), indices.ctypes.data_as(IP), len(indices),
                         P.ctypes.data_as(FP), len(P), P.strides[0], ATTR.ctypes.data_as(FP),
                         ATTR.strides[0], WEIGHTS.ctypes.data_as(FP), 5, flags.ctypes.data_as(BP),
                         target * 3, .08, 1 << 5, ct.byref(error))
        if count == 0:
            raise RuntimeError(f"{name}: simplifier erased an anatomical surface")
        all_indices.extend(result[:count].tolist())
        errors.append(error.value)
        counts.append(count // 3)
    for i in all_indices:
        for stream in base.RESULT:
            if stream != "submeshes":
                base.RESULT[stream].append(base.SOURCE[stream][int(i)])
        base.RESULT["submeshes"][0].append(len(base.RESULT["positions"]) - 1)
    REGIONS.append({"region": name, "method": "region-budgeted original shell",
                    "targetTriangles": budget, "triangles": len(all_indices) // 3,
                    "components": len(components), "trianglesPerComponent": counts,
                    "error": max(errors)})


def append_proxy(faces, sides, name):
    triangles = base.prism(faces, sides)
    base.transfer(triangles, faces, name, len(triangles))
    REGIONS.append({"region": name, "method": f"reconstructed {sides}-sided tapered prism",
                    "triangles": len(triangles)})


def append_shield(faces):
    components = [np.asarray(c) for c in split_surfaces(faces).values()]
    body = max(range(len(components)), key=lambda i: np.max(np.ptp(P[np.unique(components[i])], axis=0)))
    # The visually dominant authored body/boss is only 76 triangles: retain it
    # exactly. Spend simplification on the six tiny decorative rivets instead.
    append_source(components[body], len(components[body]), "Shield_Main_LP/body (retained)")
    for i, component in enumerate(components):
        if i == body:
            continue
        points = P[np.unique(component)]
        center = points.mean(0)
        _, _, axes = np.linalg.svd(points-center, full_matrices=False)
        radii = np.max(np.abs((points-center) @ axes.T), axis=0)
        verts = [center + sign * axes[k] * radii[k] for k in range(3) for sign in (-1, 1)]
        triangles = [[verts[a], verts[b], verts[c]] for a in (0,1) for b in (2,3) for c in (4,5)]
        base.transfer(triangles, component, "Shield_rivet_"+str(i), 8)
        REGIONS.append({"region": "Shield_rivet_"+str(i), "method": "anatomical decorative octahedron", "triangles": 8})


def compact():
    # Share only completely compatible vertex streams. Keep real hard normals,
    # UV seams and distinct transform+bind palette identities. No positional weld.
    streams = [k for k in base.RESULT if k != "submeshes"]
    result = {k: [] for k in streams}
    result["submeshes"] = [[]]
    remap = {}
    for i in base.RESULT["submeshes"][0]:
        key = tuple(tuple(np.asarray(base.RESULT[k][i], dtype=np.float32)) for k in streams)
        if key not in remap:
            remap[key] = len(result["positions"])
            for k in streams:
                result[k].append(base.RESULT[k][i])
        result["submeshes"][0].append(remap[key])
    return result


def main():
    assert bpy.app.version[:3] == (4, 5, 3)
    assert len(base.SOURCE["submeshes"]) == 1
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    digit_groups = [[18,22,23,24], [19,25,26,27], [20,28,29,30], [21,31,32,33], [34,35,36],
                    [105,109,110,111], [106,112,113,114], [107,115,116,117], [108,118,119,120], [121,122,123],
                    [58,59,60], [62,63,61], [64,65,66], [67,68,69], [70,71,72],
                    [101,100,99], [97,96,98], [95,94,93], [92,91,90], [89,88,87]]
    grouped = {"pCube" + str(n) for group in digit_groups for n in group}
    for group in digit_groups:
        names = {"pCube" + str(n) for n in group}
        faces = np.concatenate([base.source_part(r) for r in base.SOURCE["regions"] if r["name"].split("/")[-1] in names])
        append_proxy(faces, 3, "Digit_" + str(group[0]))
    long_bones = {57,86,126,127,128,132,133,134,135,138,139,140}
    vertebrae = {42,43,44,46,*range(75,85)}
    for region in base.SOURCE["regions"]:
        name = region["name"].split("/")[-1]
        if name in grouped or name in ("Bracer_Straps_LP", "Hand_Wraps_lp", "boots_straps_LP", "boot_straps_LP_Left"):
            continue
        faces = base.source_part(region)
        if name == "Shield_Main_LP":
            append_shield(faces)
            continue
        n = int(name[5:]) if name.startswith("pCube") else -1
        if n in long_bones or n in vertebrae:
            append_proxy(faces, 5 if n in long_bones else 3, name)
            continue
        paired = name in ("boots_lp", "Bracers2_LP", "Bracers_LP", "Hips_Inner_lp", "Hips_Outer_LP",
                          "Pauldrons_Lower_LP", "Pauldrons_Upper_LP", "Shinguards_lp1")
        for side in ((-1, 1) if paired else (None,)):
            part = base.source_part(region, side) if paired else faces
            if not len(part):
                continue
            target = max(2, round(allocation(name) * SCALE / (2 if paired else 1)))
            append_source(part, target, name + (f"_{side}" if paired else ""))
    result = compact()
    triangles = len(result["submeshes"][0]) // 3
    (base.OUT / "B-matched.json").write_text(json.dumps(result, separators=(",", ":")))
    recipe = {"profile": "B matched", "source": "independent source.json LOD0; not A or low B",
              "budgetScale": SCALE, "blender": bpy.app.version_string,
              "meshoptimizer": "v0.25 6daea4695c48338363b08022d2fb15deaef6ac09 MIT",
              "triangles": triangles, "vertices": len(result["positions"]), "regions": REGIONS}
    (base.OUT / "B-matched-recipe.json").write_text(json.dumps(recipe, indent=2))
    print("B matched:", triangles, "triangles", len(result["positions"]), "vertices; scale", SCALE)


if __name__ == "__main__":
    main()
