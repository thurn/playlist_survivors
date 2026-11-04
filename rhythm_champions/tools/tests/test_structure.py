"""The structure recipes' interface (champions_tools.structure): a design's polygons draw their outlines and never the
diagonals that cut them into triangles, halving long edges opens no crack, and a recipe writes the mesh StructureBaker
bakes for the default look, or nothing when its mesh is malformed."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest

from champions_tools import structure
from champions_tools.structure import Builder, Kind, box, face, panel


def drawn_length(mesh: structure.Mesh) -> float:
    """The total length of the distinct edges the mesh draws."""
    seen = {}
    for t, tri in enumerate(mesh.triangles):
        for k in range(3):
            if mesh.edges[t, k]:
                a, b = mesh.positions[tri[(k + 1) % 3]], mesh.positions[tri[(k + 2) % 3]]
                key = tuple(sorted((tuple(np.round(a, 4)), tuple(np.round(b, 4)))))
                seen[key] = float(np.linalg.norm(a - b))
    return sum(seen.values())


def test_a_box_draws_its_twelve_edges_and_no_diagonal() -> None:
    b = Builder()
    box(b, 0, 0, 2, 1, 0, 1, bottom=True)
    mesh = b.mesh()
    assert len(mesh.triangles) == 12
    assert mesh.edges.sum() == 24  # each of the twelve edges flagged by the two triangles beside it
    assert drawn_length(mesh) == pytest.approx(4 * 2 + 4 * 1 + 4 * 1)


def test_polygons_in_one_plane_join_quietly_unless_a_line_is_drawn() -> None:
    b = Builder()
    left = b.poly([(0, 0, 0), (0, 0, 1), (1, 0, 1), (1, 0, 0)])
    b.poly([(1, 0, 0), (1, 0, 1), (2, 0, 1), (2, 0, 0)])
    assert drawn_length(b.mesh()) == pytest.approx(6)  # the two squares' outline, not the seam
    b.line(left[2], left[3])
    assert drawn_length(b.mesh()) == pytest.approx(7)
    b.hush(left[0], left[1])
    assert drawn_length(b.mesh()) == pytest.approx(6)


def test_an_inset_draws_its_rim_jambs_and_back() -> None:
    b = Builder()
    face(b, (0, 0, 0), (1, 0, 0), (0, 1, 0), 3, 2, [(1, 0.5, 2, 1.5)], depth=0.1)
    # the face's outline 10, the opening's rim 4 and its back 4, its four jambs' inner corners 4 x 0.1
    assert drawn_length(b.mesh()) == pytest.approx(10 + 4 + 4 + 0.4)


def test_a_triangular_panel_draws_its_outline_and_a_window_and_nothing_across_it() -> None:
    b = Builder()
    panel(b, (0, 0, 0), (1, 0, 0), (0, 1, 0), [(0, 0), (2, 4), (4, 0)], [(1.5, 1, 2.5, 2)], depth=0.1)
    assert drawn_length(b.mesh()) == pytest.approx(4 + 2 * np.hypot(2, 4) + 4 + 4 + 0.4)


def test_halving_long_edges_opens_no_crack_and_draws_the_same_lines() -> None:
    b = Builder()
    box(b, 0, 0, 3, 1, 0, 2, bottom=True)
    whole, halved = b.mesh(), b.mesh(max_edge=0.6)
    assert drawn_length(halved) == pytest.approx(drawn_length(whole))
    edges = {}
    for tri in halved.triangles:
        for k in range(3):
            key = tuple(sorted((tri[(k + 1) % 3], tri[(k + 2) % 3])))
            edges[key] = edges.get(key, 0) + 1
    assert set(edges.values()) == {2}, "every edge of the closed box between exactly two triangles"
    lengths = [np.linalg.norm(halved.positions[a] - halved.positions[b]) for a, b in edges]
    assert max(lengths) <= 0.6


def recipe(path: Path, body: str) -> Path:
    path.write_text('"""A test recipe."""\n\nfrom champions_tools.structure import Builder, Kind, Mesh, box\n\n\n'
                    f"def build(kind: Kind) -> Mesh:\n{body}\n", encoding="utf-8")
    return path


def test_a_recipe_writes_what_the_baker_reads(tmp_path: Path) -> None:
    path = recipe(tmp_path / "crate.py", "    b = Builder()\n    box(b, 0, 0, *kind.size_m, 0, kind.height_m)\n    return b.mesh()")
    out = tmp_path / "recipe"
    structure.run_recipe(path, Kind("crate", 1.5, 6, (2.0, 1.0)), out)

    assert json.loads((out / "mesh.json").read_text(encoding="utf-8")) == {"vertices": 8, "triangles": 10, "pieces": 0}
    flags = np.fromfile(out / "edges.u8", dtype=np.uint8).reshape(10, 3)
    assert flags.sum() == 2 * 8 + 4  # eight edges between two faces, the four round the open bottom once
    positions = np.fromfile(out / "positions.f32", dtype="<f4").reshape(8, 3)
    assert positions[:, 1].max() == pytest.approx(1.5)
    assert np.fromfile(out / "triangles.i32", dtype="<i4").max() == 7


@pytest.mark.parametrize(("body", "problem"), [
    ("    b = Builder()\n    box(b, 0, 0, 1, 1, 0, 2.0)\n    return b.mesh()", "stands 2.00 m tall"),
    ("    return None", "not a Mesh"),
    ("    b = Builder()\n    box(b, 0, 0, 1, 1, 0, 1.5)\n    m = b.mesh()\n    m.edges = m.edges[:1]\n    return m",
     "one flag per triangle corner"),
])
def test_a_malformed_mesh_is_refused_and_nothing_is_written(tmp_path: Path, body: str, problem: str) -> None:
    with pytest.raises(structure.StructureError, match=problem):
        structure.run_recipe(recipe(tmp_path / "crate.py", body), Kind("crate", 1.5, 6, (2.0, 1.0)), tmp_path / "recipe")
    assert not (tmp_path / "recipe").exists()


def test_a_concave_polygon_is_refused() -> None:
    b = Builder()
    b.poly([(0, 0, 0), (0, 0, 2), (0.5, 0, 0.5), (2, 0, 2), (2, 0, 0)])
    with pytest.raises(structure.StructureError, match="isn't convex"):
        b.mesh()
