"""The monster recipes' interface (champions_tools.monster): a recipe reads the exported vendor mesh and writes the mesh
MonsterBaker bakes for the default look; and the review's README keeps its hand-written notes."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest

from champions_tools import monster


def export(folder: Path) -> None:
    """A source as MonsterBaker.Export writes it: two triangles, a sword part of one, three bones."""
    folder.mkdir(parents=True)
    positions = np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0], [1, 1, 0]], dtype="<f4")
    positions.tofile(folder / "positions.f32")
    np.tile(np.array([0, 0, 1], dtype="<f4"), (4, 1)).tofile(folder / "normals.f32")
    np.array([[0, 1, 2], [1, 3, 2]], dtype="<i4").tofile(folder / "triangles.i32")
    np.array([[0, 1, 0, 0], [1, 0, 0, 0], [2, 0, 0, 0], [2, 0, 0, 0]], dtype="<i4").tofile(folder / "bones.i32")
    np.array([[0.5, 0.5, 0, 0], [1, 0, 0, 0], [1, 0, 0, 0], [1, 0, 0, 0]], dtype="<f4").tofile(folder / "weights.f32")
    np.array([0, 0, 1, 1], dtype=np.uint8).tofile(folder / "carried.u8")
    (folder / "source.json").write_text(json.dumps({
        "id": "skeleton", "heightM": 1.8, "vertices": 4, "triangles": 2,
        "bones": [{"name": n, "path": "Root/" + n} for n in ("Hips", "Spine", "Hand_R")],
        "parts": [{"name": "Body", "first": 0, "count": 2, "rigid": False, "carried": False},
                  {"name": "Sword", "first": 2, "count": 2, "rigid": False, "carried": True}],
    }), encoding="utf-8")


def recipe(path: Path, body: str) -> Path:
    path.write_text('"""A test recipe."""\n\nfrom champions_tools.monster import Mesh, Source\n\n\n'
                    f"def build(source: Source) -> Mesh:\n{body}\n", encoding="utf-8")
    return path


def test_a_recipe_builds_from_the_source_and_writes_what_the_baker_reads(tmp_path: Path) -> None:
    export(tmp_path / "source")
    # Keep the body's triangle only, the sword's hand bone named by the recipe.
    path = recipe(tmp_path / "skeleton.py", (
        "    mesh = source.mesh()\n"
        "    assert source.part('Sword').carried and source.bone('Hand_R') == 2\n"
        "    mesh.triangles = mesh.triangles[:1]\n"
        "    mesh.edges = mesh.triangles * 0 + 1  # a wireframe: every edge of it\n"
        "    return mesh"
    ))
    out = tmp_path / "recipe"
    monster.run_recipe(path, tmp_path / "source", out)

    assert json.loads((out / "mesh.json").read_text(encoding="utf-8")) == {"vertices": 4, "triangles": 1, "edgeAngleDeg": 24.0}
    assert np.fromfile(out / "edges.u8", dtype=np.uint8).tolist() == [1, 1, 1]
    assert np.fromfile(out / "triangles.i32", dtype="<i4").tolist() == [0, 1, 2]
    assert np.fromfile(out / "bones.i32", dtype="<i4").reshape(4, 4)[0].tolist() == [0, 1, 0, 0]
    assert np.fromfile(out / "carried.u8", dtype=np.uint8).tolist() == [0, 0, 1, 1]
    assert np.allclose(np.fromfile(out / "positions.f32", dtype="<f4").reshape(4, 3)[3], [1, 1, 0])


@pytest.mark.parametrize(("body", "problem"), [
    ("    mesh = source.mesh()\n    mesh.bones[0, 0] = 3\n    return mesh", "bones index past"),
    ("    mesh = source.mesh()\n    mesh.weights[0] = [0.2, 0, 0, 0]\n    return mesh", "weights don't sum to 1"),
    ("    mesh = source.mesh()\n    mesh.triangles[0, 0] = 9\n    return mesh", "triangles index past"),
    ("    return None", "not a Mesh"),
    ("    mesh = source.mesh()\n    mesh.edges = mesh.triangles[:1]\n    return mesh", "one flag per triangle corner"),
])
def test_a_malformed_mesh_is_refused_and_nothing_is_written(tmp_path: Path, body: str, problem: str) -> None:
    export(tmp_path / "source")
    with pytest.raises(monster.MonsterError, match=problem):
        monster.run_recipe(recipe(tmp_path / "skeleton.py", body), tmp_path / "source", tmp_path / "recipe")
    assert not (tmp_path / "recipe").exists()


def test_a_new_review_keeps_the_readmes_notes(tmp_path: Path) -> None:
    result = {"type": "skeleton", "look": "default", "source": "recipe", "camera": {"pitchDeg": 60, "distanceM": 23, "fovDeg": 40},
              "poses": ["idle", "walk", "attack"], "shatterAt": [0, 0.2], "lodTriangles": [900, 450, 220],
              "trailerLodTriangles": [9310, 4734, 2367], "shards": 30, "bones": 120, "heightM": 1.75}
    readme = tmp_path / "README.md"
    readme.write_text(monster.write_readme(result, "", "", ["default", "trailer"]) +
                      "\n## Notes\n\nThe blind critique: the helmet reads.\n", encoding="utf-8")

    again = monster.write_readme(result, "Planar dissolve, part by part.", monster._notes(readme), ["default"])

    assert again.endswith("## Notes\n\nThe blind critique: the helmet reads.\n")
    assert "tools/recipes/monster/skeleton.py" in again and "Planar dissolve, part by part." in again
    assert "`default-game.jpg`" in again and "`trailer-game.jpg`" not in again
