"""Edge cuts (champions_tools.edgecut) on a synthetic model: a torso box and a flat shield box, each on its own bone.
What a recipe builds from it, what the baker receives, and which of `champions edgecut`'s steps run again."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest

from champions_tools import paths
from champions_tools.edgecut import Clip, Cut, Model, Source, Weapon, arrays, lines, run

BOX = np.array([[x, y, z] for x in (0, 1) for y in (0, 1) for z in (0, 1)], float)
# Outward-facing triangles of the unit box above (two per face).
BOX_TRIS = np.array([[0, 1, 3], [0, 3, 2], [4, 6, 7], [4, 7, 5], [0, 4, 5], [0, 5, 1],
                     [2, 3, 7], [2, 7, 6], [0, 2, 6], [0, 6, 4], [1, 5, 7], [1, 7, 3]])


def write_source(path: Path, posed: np.ndarray | None = None) -> Path:
    """A torso box (0.4 x 1 x 0.3 m, on bone 0) and a shield box (0.5 x 0.5 x 0.02 m, on bone 1), with its vertices
    posed as `posed` if given."""
    torso = BOX * [0.4, 1.0, 0.3] + [-0.2, 0.5, -0.15]
    shield = BOX * [0.5, 0.5, 0.02] + [-0.8, 0.8, 0.2]
    positions = np.vstack([torso, shield])
    bones = np.zeros((16, 4), np.int64)
    bones[8:, 0] = 1
    weights = np.zeros((16, 4))
    weights[:, 0] = 1
    triangles = np.vstack([BOX_TRIS, BOX_TRIS + 8])
    meta = {"version": 1, "model": "boxes", "heightM": 1.8,
            "entries": [{"name": "Torso", "path": "Root/Torso"}, {"name": "ShieldBone", "path": "Root/ShieldBone"}],
            "renderers": [{"name": "Body"}, {"name": "Shield"}]}
    arrays.write(path, meta, {
        "positions": positions, **({"posed": posed} if posed is not None else {}), "normals": np.zeros_like(positions),
        "bones": bones, "weights": weights, "triangles": triangles, "triangle_renderer": np.repeat([0, 1], 12),
        "triangle_submesh": np.zeros(24, np.int64), "binds": np.tile(np.eye(4), (2, 1, 1)),
    })
    return path


@pytest.fixture
def src(tmp_path: Path) -> Source:
    return Source(write_source(tmp_path / "source.json"))


def edge_uses(tris: np.ndarray) -> dict[tuple[int, int], int]:
    uses: dict[tuple[int, int], int] = {}
    for t in tris:
        for a, b in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])):
            key = (int(min(a, b)), int(max(a, b)))
            uses[key] = uses.get(key, 0) + 1
    return uses


def test_arrays_round_trip_with_the_header(tmp_path: Path) -> None:
    header = tmp_path / "x.json"
    arrays.write(header, {"model": "m"}, {"f": np.arange(6, dtype=float).reshape(2, 3), "i": np.array([[1, -2]])})
    meta, a = arrays.read(header)
    assert meta["model"] == "m"
    assert a["f"].dtype == np.float32 and a["f"].tolist() == [[0, 1, 2], [3, 4, 5]]
    assert a["i"].dtype == np.int32 and a["i"].tolist() == [[1, -2]]


def test_a_hull_is_a_closed_body_inside_its_part_keeping_the_skin(src: Source) -> None:
    c = Cut(src)
    c.hull(src.renderers_named("Body"), "torso", points=8)
    tris = np.vstack(c.body_triangles)
    assert set(edge_uses(tris).values()) == {2}, "every edge between exactly two triangles: closed"
    pos = np.vstack(c.body_positions)
    assert (pos.min(0) > [-0.2, 0.5, -0.15]).all() and (pos.max(0) < [0.2, 1.5, 0.15]).all(), "pulled inside the part"
    assert (np.vstack(c.body_bones)[:, 0] == 0).all() and c.triangle_count == 12


def test_feature_lines_follow_the_creases_within_the_count(src: Source) -> None:
    c = Cut(src)
    parts = {"torso": src.renderers_named("Body"), "shield": src.renderers_named("Shield")}
    c.hull(parts["torso"], "torso", points=8)
    picked = c.feature_lines(parts, count=40, min_length=0.01)
    assert 0 < c.segment_count <= 40
    assert {ch.part for ch in picked} == {"torso", "shield"}
    corners = np.vstack([BOX * [0.4, 1.0, 0.3] + [-0.2, 0.5, -0.15], BOX * [0.5, 0.5, 0.02] + [-0.8, 0.8, 0.2]])
    for p in c.line_positions:
        assert np.min(np.linalg.norm(corners - p, axis=1)) < 1e-6, "lines run between the boxes' corners"
    assert all(0.15 <= ch.importance <= 1 for ch in picked)

    fewer = Cut(src)
    fewer.feature_lines(parts, count=40, drop=("shield",), caps={"torso": 3}, min_length=0.01)
    assert set(fewer.segment_part) == {"torso"} and fewer.segment_count <= 3


def test_the_line_choice_sees_the_source_in_its_pose(src: Source, tmp_path: Path) -> None:
    assert src.posed is src.positions, "without a pose, the bind pose"
    lifted = src.positions + [0, 3, 0]
    posed = Source(write_source(tmp_path / "posed.json", posed=lifted))
    face_part = np.zeros(posed.triangle_count, np.int64)
    proxy = lines.weld(posed, posed.renderers_named("Body"), face_part, 1e-4, smooth_iterations=0)
    assert np.allclose(proxy.raw, posed.positions[proxy.source_vertex]), "lines keep the bind pose, for their skin"
    assert np.allclose(proxy.smooth, lifted[proxy.source_vertex]), "silhouettes are judged on the posed shape"


def test_contours_run_along_a_part_at_its_surface_rigid_on_its_bone(src: Source) -> None:
    c = Cut(src)
    c.contours(src.renderers_named("Body"), "torso")
    assert c.segment_count == 4 and set(c.segment_part) == {"torso"}
    ends = np.array(c.line_positions)
    assert np.allclose(ends[0::2, [0, 2]], ends[1::2, [0, 2]]), "each runs along the box's long (y) axis"
    assert np.allclose(np.sort(ends[:, 1]), [0.5] * 4 + [1.5] * 4)
    # Four turns apart round the axis, each as far out as the box reaches that way (its support along the diagonal).
    offsets = {(round(x, 6), round(z, 6)) for x, _, z in ends}
    assert offsets == {(x, z) for x in (-0.175, 0.175) for z in (-0.175, 0.175)}
    assert all(b[0] == 0 and w[0] == 1 for b, w in zip(c.line_bones, c.line_weights, strict=True))


def test_a_circle_and_strokes_are_rigid_on_their_bones(src: Source) -> None:
    c = Cut(src)
    entry = src.dominant_entry(src.renderers_named("Shield"))
    c.circle([0, 1, 0], [0, 0, 1], 0.3, entry, "shield", sides=12)
    c.stroke([[0, 0, 0], [0, 1, 0], [0, 2, 0]], 0, "sword")
    c.stroke([[0, 0, 0], [0, 1, 0], [-0.8, 1, 0.2]], [0, 0, entry], "arm")
    assert entry == src.entry("ShieldBone")
    assert c.segment_count == 16 and c.segments[11] == (11, 0), "the ring closes"
    assert np.allclose([np.linalg.norm(p - [0, 1, 0]) for p in c.line_positions[:12]], 0.3)
    assert all(b[0] == entry and w[0] == 1 for b, w in zip(c.line_bones[:12], c.line_weights[:12], strict=True))
    assert [b[0] for b in c.line_bones[15:]] == [0, 0, entry], "a chain's points each keep their own bone"
    assert c.segments[14:] == [(15, 16), (16, 17)]
    with pytest.raises(ValueError, match="2 entries"):
        c.stroke([[0, 0, 0], [0, 1, 0], [0, 2, 0]], [0, 1], "arm")


def test_pieces_cover_every_triangle_and_segment_within_their_part(src: Source) -> None:
    c = Cut(src)
    c.hull(src.renderers_named("Body"), "torso", points=8)
    c.disc([-0.55, 1.05, 0.21], [0, 0, 1], 0.25, 1, "shield")
    c.circle([-0.55, 1.05, 0.21], [0, 0, 1], 0.25, 1, "shield", sides=8)
    c.break_into({"torso": 3})
    assert len(c.pieces) == 4
    assert set(c.piece_of_triangle[:12]) == {0, 1, 2} and set(c.piece_of_triangle[12:]) == {3}
    assert set(c.piece_of_segment) == {3}
    assert c.pieces[3]["entry"] == 1 and all(p["size"] > 0 for p in c.pieces)


def test_the_written_cut_splits_vertices_shared_by_two_pieces(src: Source, tmp_path: Path) -> None:
    c = Cut(src)
    c.hull(src.renderers_named("Body"), "torso", points=8)
    c.break_into({"torso": 2})
    c.write(tmp_path / "cut.json")
    meta, a = arrays.read(tmp_path / "cut.json")
    assert meta["triangles"] == 12 and meta["pieces"] == 2
    tri_piece = a["body_piece"][a["body_triangles"]]
    assert (tri_piece == tri_piece[:, :1]).all(), "a triangle's corners belong to its piece"
    assert len(a["body_positions"]) > 8, "the corners on the cut between the pieces were split"


def test_simplify_keeps_only_the_corners() -> None:
    pts = np.array([[0, 0, 0], [1, 0.001, 0], [2, 0, 0], [2, 1, 0], [2, 2, 0]], float)
    assert lines.simplify(pts, [0, 1, 2, 3, 4], 0.01) == [0, 2, 4]


def test_judge_measures_game_scale_and_the_role_s_limits() -> None:
    review = {"segments": 130, "triangles": 322, "cells": [
        {"size": "game", "blackShare": 0.7}, {"size": "game", "blackShare": 0.62}, {"size": "far", "blackShare": 0.1}]}
    verdicts = run.judge(review, run.THRESHOLDS["fodder"])
    assert [ok for _, _, ok in verdicts] == [True, False, True]
    assert verdicts[0][1].startswith("66.0%")


RECIPE = '''
from champions_tools.edgecut import Clip, Cut, Model, Source

MODEL = Model(id="boxes", prefab="0" * 32, heightM=1.8, clips=(Clip("idle", "1" * 32, "Idle", loop=True),))
ROLE = "fodder"


def cut(src: Source) -> Cut:
    c = Cut(src)
    c.hull(src.renderers_named("Body"), "torso", points={points})
    return c
'''


def test_the_model_names_its_weapons_and_pose_as_unity_reads_them() -> None:
    model = Model(id="ork", prefab="0" * 32, heightM=2.4, clips=(Clip("idle", "1" * 32, "Idle", loop=True),),
                  weapons=(Weapon("2" * 32, palm=0.02, pitch=20, yaw=90),), pose="idle")
    d = json.loads(json.dumps(model.json()))
    assert d["weapons"] == [{"prefab": "2" * 32, "hand": "right", "palm": 0.02, "pitch": 20, "yaw": 90, "roll": 0}]
    assert d["pose"] == "idle" and d["clips"][0]["role"] == "idle"


def test_a_bake_runs_only_the_steps_whose_inputs_changed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    wt = paths.Worktree(tmp_path / "wt")
    recipe = run.recipes_dir(wt) / "boxes.py"
    recipe.parent.mkdir(parents=True)
    recipe.write_text(RECIPE.format(points=8))
    (wt.project / "tools" / "src" / "champions_tools" / "edgecut").mkdir(parents=True)
    (wt.project / "manifest.json").write_text("{}\n")
    calls = []

    def unity(_wt, task, *args):
        calls.append(task)
        out = run.baked_dir(wt, "boxes")
        if task == "Export":
            write_source(out / "source.json")
        else:
            (out / "boxes.asset").write_text("baked")

    monkeypatch.setattr(run, "_unity", unity)
    monkeypatch.setattr(run.setup, "save_baked", lambda _wt: None)
    first = run.bake(wt, "boxes")
    assert calls == ["Export", "Bake"] and first["triangles"] == 12
    assert json.loads((run.baked_dir(wt, "boxes") / "model.json").read_text())["prefab"] == "0" * 32
    assert run.bake(wt, "boxes")["steps"] == ["nothing changed"] and calls == ["Export", "Bake"]
    recipe.write_text(RECIPE.format(points=6))
    again = run.bake(wt, "boxes")
    assert calls == ["Export", "Bake", "Bake"], "a recipe change cuts and bakes again, but needs no export"
    assert again["triangles"] == 8


def test_an_unknown_recipe_names_the_known_ones(tmp_path: Path) -> None:
    wt = paths.Worktree(tmp_path / "wt")
    run.recipes_dir(wt).mkdir(parents=True)
    (run.recipes_dir(wt) / "slime.py").write_text("")
    with pytest.raises(run.EdgeCutError, match="recipes: slime"):
        run.load_recipe(wt, "bat")
