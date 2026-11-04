"""`champions kit bake KIT` (boss-rush.md, Environments): the Boss Rush act kits' recipes, run into meshes the arena
presenter draws.

A kit is one act's environment: its prop kinds (Content/arenas.json's `props` whose `kit` names it), the placeholder
kinds it dresses, the floor treatment scattered over an arena's floor and the backdrop silhouette standing round its far
walls (arenas.json's `kits`). Its recipe, tools/recipes/kit/KIT.py, designs every piece from scratch with the structure
recipes' `Builder` and shapes (numpy only, no vendor input: the Synty packs are references by eye), and has

    PIECES = {"pieceId": build, ...}   # each build() -> Mesh, its docstring saying what it draws and why

A piece is in its own metres, y up from the ground, its middle at the origin, lying along x as a box prop's `sizeM` does
(the sim's y is the mesh's z). A prop kind's piece is its `id`'s and stands exactly its `heightM` tall. Every edge of a
design draws, as the structures' do (structure.py). A bake writes each piece to the ignored
Assets/Baked/Kits/Resources/Kits/KIT/PIECE.bytes, skipped while the recipe, this module, structure.py and arenas.json
haven't changed; `champions build` runs it before the player's build. Without the files the arenas draw their
placeholder solids and no floor or backdrop.
"""

from __future__ import annotations

import json
import shutil
import struct
import sys
import time
from pathlib import Path

import numpy as np

from . import paths
from .monster import digest
from .structure import Mesh, StructureError, load_module

CONTENT = "Content/arenas.json"


class KitError(Exception):
    pass


def recipes_dir(wt: paths.Worktree) -> Path:
    return wt.project / "tools" / "recipes" / "kit"


def baked_dir(wt: paths.Worktree, kit_id: str) -> Path:
    return wt.project / "Assets" / "Baked" / "Kits" / "Resources" / "Kits" / kit_id


def content(wt: paths.Worktree) -> dict:
    return json.loads((wt.project / CONTENT).read_text(encoding="utf-8"))


def kits(wt: paths.Worktree) -> list[str]:
    return [k["id"] for k in content(wt).get("kits", [])]


def needed(data: dict, kit_id: str) -> dict[str, float | None]:
    """The pieces arenas.json asks of a kit: each prop kind's (with its height) and the floor's and backdrop's."""
    kit = next((k for k in data.get("kits", []) if k["id"] == kit_id), None)
    if kit is None:
        raise KitError(f"no kit {kit_id!r} in {CONTENT}'s kits")
    out: dict[str, float | None] = {p["id"]: float(p["heightM"]) for p in data["props"] if p.get("kit") == kit_id}
    for piece in kit.get("floor", {}).get("pieces", []) + [p for r in kit.get("backdrop", []) for p in r["pieces"]]:
        out.setdefault(piece, None)
    return out


def write(mesh: Mesh, path: Path) -> None:
    """A piece as KitMeshes.cs reads it: vertex and triangle counts (int32), positions (float32 xyz), triangles (int32),
    then each triangle's three drawn-edge flags (uint8, the edge opposite each corner), little-endian."""
    with path.open("wb") as f:
        f.write(struct.pack("<ii", len(mesh.positions), len(mesh.triangles)))
        f.write(mesh.positions.astype("<f4").tobytes())
        f.write(mesh.triangles.astype("<i4").tobytes())
        f.write((mesh.edges != 0).astype(np.uint8).tobytes())


def build(wt: paths.Worktree, kit_id: str) -> dict[str, Mesh]:
    """Runs the kit's recipe: every piece arenas.json asks of it, checked."""
    path = recipes_dir(wt) / f"{kit_id}.py"
    if not path.exists():
        raise KitError(f"{kit_id}: no recipe at {path.relative_to(wt.project)}")
    module = load_module(path, f"kit_recipe_{kit_id}")
    pieces = getattr(module, "PIECES", None)
    if not isinstance(pieces, dict):
        raise KitError(f"{path.name} needs PIECES = {{piece id: build() -> Mesh}}")
    want = needed(content(wt), kit_id)
    missing = sorted(set(want) - set(pieces))
    if missing:
        raise KitError(f"{kit_id}: {CONTENT} asks for pieces the recipe doesn't build: {', '.join(missing)}")
    out = {}
    for piece, fn in pieces.items():
        mesh = fn()
        if not isinstance(mesh, Mesh):
            raise KitError(f"{kit_id}.{piece} returned {type(mesh).__name__}, not a Mesh")
        check(kit_id, piece, mesh, want.get(piece))
        out[piece] = mesh
    return out


def check(kit_id: str, piece: str, mesh: Mesh, height: float | None) -> None:
    n = len(mesh.positions)
    problems = []
    if mesh.positions.shape != (n, 3) or n == 0 or not np.isfinite(mesh.positions).all():
        problems.append(f"positions {mesh.positions.shape}")
    elif len(mesh.triangles) == 0 or mesh.triangles.min() < 0 or mesh.triangles.max() >= n:
        problems.append("triangles empty or past the corners")
    elif mesh.edges.shape != mesh.triangles.shape:
        problems.append("one edge flag per triangle corner")
    elif mesh.positions[:, 1].min() < -0.01:
        problems.append(f"it reaches {-mesh.positions[:, 1].min():.2f} m under the ground")
    elif height is not None and abs(float(mesh.positions[:, 1].max()) - height) > 0.01 * height:
        problems.append(f"it stands {mesh.positions[:, 1].max():.2f} m tall, not its prop kind's {height} m")
    if problems:
        raise KitError(f"{kit_id}.{piece}: " + "; ".join(problems))


def prepare(wt: paths.Worktree, kit_id: str, force: bool = False) -> list[str]:
    """Runs the kit's recipe when its inputs changed (always with `force`). What it did."""
    out = baked_dir(wt, kit_id)
    recipe = recipes_dir(wt) / f"{kit_id}.py"
    here = Path(__file__).resolve()
    key = digest(wt, recipe, wt.project / CONTENT, here.read_text(encoding="utf-8"), here.with_name("structure.py").read_text(encoding="utf-8"))
    marker = out.parent.parent.parent / f"{kit_id}.key"  # outside Resources, so the player never carries it
    if not force and marker.exists() and marker.read_text(encoding="utf-8").strip() == key and out.exists():
        return []
    started = time.monotonic()
    pieces = build(wt, kit_id)
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    for piece, mesh in pieces.items():
        write(mesh, out / f"{piece}.bytes")
    marker.write_text(key + "\n", encoding="utf-8")
    tris = sum(len(m.triangles) for m in pieces.values())
    return [f"built {len(pieces)} pieces, {tris} triangles, in {time.monotonic() - started:.1f} s"]


def prepare_all(wt: paths.Worktree, force: bool = False) -> None:
    """Every kit's recipe step (`champions build` runs it before the player's build); drops the meshes of kits since
    removed."""
    if not (wt.project / CONTENT).exists():
        return
    ids = kits(wt)
    for kit_id in ids:
        for step in prepare(wt, kit_id, force):
            print(f"kit: {kit_id}: {step}")
    root = wt.project / "Assets" / "Baked" / "Kits" / "Resources" / "Kits"
    if root.exists():
        for stale in root.iterdir():
            if stale.is_dir() and stale.name not in ids:
                shutil.rmtree(stale)


def cmd_kit(a) -> int:
    wt = paths.worktree()
    try:
        ids = kits(wt) if a.kit == "all" else [a.kit]
        for kit_id in ids:
            for step in prepare(wt, kit_id, a.force) or ["up to date"]:
                print(f"kit: {kit_id}: {step}")
    except (KitError, StructureError) as e:
        print(f"kit {a.action}: {e}", file=sys.stderr)
        return 2
    return 0

