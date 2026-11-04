"""The Boss Rush act kits' recipes (champions_tools.kit): each kit in arenas.json builds every piece it asks for, its
props standing their kinds' heights, and a bake writes the bytes KitMeshes.cs reads, once, or nothing when a piece is
missing or malformed."""

from __future__ import annotations

import json
import struct
from pathlib import Path

import numpy as np
import pytest

from champions_tools import kit, paths

REPO = paths.Worktree(Path(__file__).resolve().parents[3])

RECIPE = '''
"""A test kit."""
from champions_tools.structure import Builder, box

def block():
    b = Builder()
    box(b, 0, 0, 1, 1, 0, HEIGHT)
    return b.mesh()

PIECES = {"block": block, "patch": block}
'''


def project(tmp_path: Path, height: float = 2.0, pieces: str = RECIPE) -> paths.Worktree:
    wt = paths.Worktree(tmp_path)
    (wt.project / "Content").mkdir(parents=True)
    (wt.project / "tools" / "recipes" / "kit").mkdir(parents=True)
    content = {"props": [{"id": "block", "kit": "test", "sizeM": [1, 1], "heightM": 2.0, "hits": 0}],
               "kits": [{"id": "test", "floor": {"pieces": ["patch"]}, "backdrop": []}]}
    (wt.project / kit.CONTENT).write_text(json.dumps(content), encoding="utf-8")
    (wt.project / "tools" / "recipes" / "kit" / "test.py").write_text(pieces.replace("HEIGHT", str(height)), encoding="utf-8")
    return wt


def test_every_kit_in_the_content_builds_every_piece_it_asks_for() -> None:
    data = kit.content(REPO)
    assert data["kits"], "act 1's kit at least"
    for kit_id in kit.kits(REPO):
        pieces = kit.build(REPO, kit_id)
        for piece, height in kit.needed(data, kit_id).items():
            mesh = pieces[piece]
            assert mesh.positions[:, 1].min() >= -0.01, f"{piece} stands on the ground"
            if height is not None:
                assert mesh.positions[:, 1].max() == pytest.approx(height, rel=0.01), piece


def test_a_bake_writes_the_pieces_once_and_again_when_the_recipe_changes(tmp_path: Path) -> None:
    wt = project(tmp_path)
    assert kit.prepare(wt, "test") != []
    out = kit.baked_dir(wt, "test") / "block.bytes"
    data = out.read_bytes()
    n, m = struct.unpack("<ii", data[:8])
    assert (n, m) == (8, 10)  # a box open underneath: four sides and its top, two triangles each
    assert len(data) == 8 + n * 12 + m * 12 + m * 3
    edges = np.frombuffer(data[8 + n * 12 + m * 12:], dtype=np.uint8)
    assert edges.sum() == 20  # the top's and the corners' eight edges flagged by both triangles beside them, the foot's by one
    assert kit.prepare(wt, "test") == []
    (wt.project / "tools" / "recipes" / "kit" / "test.py").write_text(RECIPE.replace("HEIGHT", "2.0") + "\n# changed\n")
    assert kit.prepare(wt, "test") != []


def test_a_prop_of_the_wrong_height_or_a_missing_piece_bakes_nothing(tmp_path: Path) -> None:
    wt = project(tmp_path, height=2.5)
    with pytest.raises(kit.KitError, match="not its prop kind's 2.0 m"):
        kit.prepare(wt, "test")
    assert not kit.baked_dir(wt, "test").exists()
    wt = project(tmp_path / "b", pieces=RECIPE.replace(', "patch": block', ""))
    with pytest.raises(kit.KitError, match="patch"):
        kit.prepare(wt, "test")
