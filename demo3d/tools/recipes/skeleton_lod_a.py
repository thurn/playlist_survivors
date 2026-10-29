"""TD5 topology reduction. Run from repo root with demo3d/tools' uv environment.

All exported inputs/outputs stay inside Assets/Baked. meshoptimizer v0.25 (MIT),
6daea4695c48338363b08022d2fb15deaef6ac09; use setup_skeleton_lod.sh first.
"""
from __future__ import annotations

import ctypes as ct
import hashlib
import json
import os
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "demo3d/Assets/Baked/SkeletonLod"
STREAMS = ("positions", "normals", "tangents", "uv", "boneIndices", "boneWeights")


def topology(source):
    p = np.asarray(source["positions"])
    faces = np.concatenate(source["submeshes"]).reshape(-1, 3)
    # Positional equivalence is diagnostic only: it never rewrites the source.
    groups = defaultdict(list)
    for i, pos in enumerate(p):
        groups[tuple(np.round(pos, 6))].append(i)
    canonical = {i: g[0] for g in groups.values() for i in g}
    parent = np.arange(len(p))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return int(i)

    edges = Counter()
    raw = Counter()
    for a, b, c in faces:
        for x, y in ((a, b), (b, c), (c, a)):
            raw[tuple(sorted((int(x), int(y))))] += 1
            x, y = canonical[x], canonical[y]
            edges[tuple(sorted((x, y)))] += 1
            parent[find(y)] = find(x)
    components = defaultdict(list)
    for f, tri in enumerate(faces):
        components[find(canonical[tri[0]])].append(f)
    regions = []
    for r in source["regions"]:
        ids = np.arange(r["first"], r["first"] + r["count"])
        used = np.any(np.isin(faces, ids), axis=1)
        regions.append({**r, "triangles": int(used.sum()), "min": p[ids].min(0).tolist(),
                        "max": p[ids].max(0).tolist()})
    return {
        "sourceSha256": hashlib.sha256((OUT / "source.json").read_bytes()).hexdigest(),
        "vertices": len(p), "triangles": len(faces), "materialSlots": len(source["submeshes"]),
        "uniquePositions1um": len(groups), "coincidentExtraVertices": len(p) - len(groups),
        "rawBoundaryEdges": sum(n == 1 for n in raw.values()),
        "positionWeldedBoundaryEdges": sum(n == 1 for n in edges.values()),
        "positionWeldedNonmanifoldEdges": sum(n > 2 for n in edges.values()),
        "positionWeldedComponents": sorted((len(v) for v in components.values()), reverse=True),
        "regions": regions,
        "boundaryCaveat": "1um positional diagnostic can conflate touching surfaces; reduction never welds distinct skin/material/region entries",
    }


def compact(source, submeshes):
    used = np.unique(np.concatenate(submeshes)).astype(int)
    remap = np.full(len(source["positions"]), -1, dtype=int)
    remap[used] = np.arange(len(used))
    result = {k: np.asarray(source[k])[used].tolist() for k in STREAMS}
    result["submeshes"] = [remap[s].tolist() for s in submeshes]
    return result


def main():
    source = json.loads((OUT / "source.json").read_text())
    report = topology(source)
    (OUT / "baseline.json").write_text(json.dumps(report, indent=2))
    print(json.dumps({k: v for k, v in report.items() if k != "regions"}, indent=2))
    tool_root = Path(os.environ.get("SKELETON_LOD_TOOLS", Path.home() / ".local/share/skeleton-lod-tools"))
    lib = ct.CDLL(str(tool_root / "libmeshoptimizer-v0.25.dylib"))
    simplify = lib.meshopt_simplifyWithAttributes
    fp, ip, bp = ct.POINTER(ct.c_float), ct.POINTER(ct.c_uint), ct.POINTER(ct.c_ubyte)
    simplify.argtypes = [ip, ip, ct.c_size_t, fp, ct.c_size_t, ct.c_size_t, fp,
                         ct.c_size_t, fp, ct.c_size_t, bp, ct.c_size_t,
                         ct.c_float, ct.c_uint, fp]
    simplify.restype = ct.c_size_t
    p = np.asarray(source["positions"], dtype=np.float32)
    n = np.asarray(source["normals"], dtype=np.float32)
    uv = np.asarray(source["uv"], dtype=np.float32)
    bi = np.asarray(source["boneIndices"], dtype=np.int32)
    bw = np.asarray(source["boneWeights"], dtype=np.float32)
    # Skin attributes are continuous weights keyed by discrete palette entry. Never feed
    # palette indices as numerical attributes. The 27 most-used entries fit the API's 32.
    influence = np.zeros((len(p), len(source["palette"])), dtype=np.float32)
    for v in range(len(p)):
        for k in range(4):
            influence[v, bi[v, k]] += bw[v, k]
    ranked = np.argsort(-influence.sum(0), kind="stable")[:27]
    attr = np.ascontiguousarray(np.column_stack((n, uv, influence[:, ranked])), dtype=np.float32)
    # Selective protection: incompatible weights at coincident seams and material junctions.
    # No global border lock. No tiny components are removed in this experiment.
    flags = np.zeros(len(p), dtype=np.uint8)
    coincident = defaultdict(list)
    for v, q in enumerate(p):
        coincident[tuple(q)].append(v)
    for ids in coincident.values():
        if len(ids) > 1 and np.max(np.abs(influence[ids] - influence[ids[0]])) > 0.1:
            flags[ids] = 2  # meshopt_SimplifyVertex_Protect
    for ids in coincident.values():
        if len(ids) > 1 and np.max(np.linalg.norm(uv[ids] - uv[ids[0]], axis=1)) > 0.025:
            flags[ids] = 2  # protect atlas islands; permissive geometry-only seams stay collapsible
    slots = [set(np.asarray(s).tolist()) for s in source["submeshes"]]
    for v in range(len(p)):
        if sum(v in s for s in slots) > 1:
            flags[v] = 2
    # Three noncollinear landmarks per *connected surface*, not per renderer:
    # one renderer can contain many independent ribs/vertebrae. Protecting only
    # one point of each would still let that entire thin surface collapse away.
    faces = np.concatenate(source["submeshes"]).reshape(-1, 3)
    anatomy_components = []
    for region in source["regions"]:
        mask = np.all((faces >= region["first"]) & (faces < region["first"] + region["count"]), axis=1)
        tris = faces[mask]
        parent = {}
        def find_vertex(v, parent=parent):
            parent.setdefault(v, v)
            while parent[v] != v:
                parent[v] = parent[parent[v]]
                v = parent[v]
            return v
        for tri in tris:
            representatives = [coincident[tuple(p[i])][0] for i in tri]
            for v in representatives[1:]:
                parent[find_vertex(v)] = find_vertex(representatives[0])
        groups = defaultdict(list)
        for i in np.unique(tris):
            groups[find_vertex(coincident[tuple(p[i])][0])].append(int(i))
        for group in groups.values():
            verts = np.array(group)
            a = int(verts[0])
            b = int(verts[np.argmax(np.linalg.norm(p[verts] - p[a], axis=1))])
            c = int(verts[np.argmax(np.linalg.norm(np.cross(p[verts] - p[a], p[b] - p[a]), axis=1))])
            component_tris = tris[np.all(np.isin(tris, verts), axis=1)]
            if len(component_tris):
                q = p[component_tris]
                best = component_tris[np.argmax(np.linalg.norm(np.cross(q[:,1]-q[:,0], q[:,2]-q[:,0]), axis=1))]
                for landmark in best:
                    flags[coincident[tuple(p[landmark])]] |= 1
            anatomy_components.append(group)
            for landmark in (a, b, c):
                flags[coincident[tuple(p[landmark])]] |= 1
    records = []
    winner = None
    for preserve in ("deformation", "balanced", "geometry"):
        normal_w, uv_w, skin_w = {"deformation": (0.1, 1.0, 0.8),
                                  "balanced": (0.03, 0.5, 0.3),
                                  "geometry": (0.01, 0.25, 0.1)}[preserve]
        weights = np.array([normal_w] * 3 + [uv_w] * 2 + [skin_w] * len(ranked), dtype=np.float32)
        for tolerance in (0.01, 0.03, 0.08, 0.16):
            subs, errors = [], []
            total = sum(len(s) for s in source["submeshes"])
            for s in source["submeshes"]:
                indices = np.asarray(s, dtype=np.uint32)
                result = np.empty_like(indices)
                error = ct.c_float()
                target = max(3, int(1200 * 3 * len(s) / total) // 3 * 3)
                size = simplify(result.ctypes.data_as(ip), indices.ctypes.data_as(ip), len(indices),
                                p.ctypes.data_as(fp), len(p), p.strides[0],
                                attr.ctypes.data_as(fp), attr.strides[0], weights.ctypes.data_as(fp),
                                attr.shape[1], flags.ctypes.data_as(bp), target, tolerance,
                                1 << 5, ct.byref(error))
                subs.append(result[:size].copy())
                errors.append(error.value)
            used_positions = {tuple(p[i]) for sub in subs for i in sub}
            missing_landmarks = sum(tuple(p[i]) not in used_positions for i in np.flatnonzero(flags & 1))
            missing_components = sum(not any(tuple(p[i]) in used_positions for i in group) for group in anatomy_components)
            candidate = compact(source, subs)
            triangles = sum(len(s) for s in subs) // 3
            record = {"preservation": preserve, "tolerance": tolerance, "triangles": triangles,
                          "vertices": len(candidate["positions"]), "resultError": errors,
                          "attributeWeights": [normal_w, uv_w, skin_w], "protectedVertices": int((flags != 0).sum()), "missingLockedVertices": missing_landmarks, "missingComponents": missing_components}
            records.append(record)
            name = f"A-{preserve}-{tolerance}.json"
            (OUT / name).write_text(json.dumps(candidate, separators=(",", ":")))
            # Prefer the strongest deformation preservation within the initial comparison band.
            score = (missing_components,
                     ("deformation", "balanced", "geometry").index(preserve),
                     0 if tolerance <= 0.03 else 1, abs(triangles - 1200), tolerance)
            if winner is None or score < winner[0]:
                winner = (score, candidate, record, name)
    (OUT / "A.json").write_text(json.dumps(winner[1], separators=(",", ":")))
    (OUT / "A-recipe.json").write_text(json.dumps({"meshoptimizer": "v0.25 6daea4695c48338363b08022d2fb15deaef6ac09 MIT",
        "options": ["meshopt_SimplifyPermissive"], "welding": "no explicit weld; compatible coincident seam handling by permissive simplifier",
        "removedComponents": [], "anatomyLandmarks": "three noncollinear positions locked per connected anatomical surface", "skinAttributesPaletteEntries": ranked.tolist(),
        "sweep": records, "selected": winner[2], "selectedFile": winner[3]}, indent=2))
    print("A selected:", winner[2])


if __name__ == "__main__":
    main()
