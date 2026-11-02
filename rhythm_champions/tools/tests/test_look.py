"""`champions look` on synthetic stills with ID passes: every guide value reported, where a still strays named, and
a diagnostic that exits 0 however far the stills stray, non-zero only when it can't measure."""

import argparse
import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from champions_tools import look
from champions_tools import palette as pal

W, H = 960, 540
THRESHOLDS = Path(__file__).resolve().parents[3] / "docs" / "champions" / "style-thresholds.json"
IDS = {"1": {"type": "skeleton", "role": "fodder", "heightM": 1.8},
       "7": {"type": "bigOrk", "role": "elite", "heightM": 2.4},
       "250": {"type": "hero", "role": "hero", "heightM": 1.8}}


def view_proj() -> list[float]:
    """A camera 20 m above the origin looking straight down (OpenGL clip space, row-major), 16:9, 60 degrees."""
    f = 1 / np.tan(np.radians(30))
    n, fa = 0.3, 100.0
    proj = np.array([[f / (W / H), 0, 0, 0], [0, f, 0, 0], [0, 0, (fa + n) / (n - fa), 2 * fa * n / (n - fa)],
                     [0, 0, -1, 0]])
    view = np.array([[1, 0, 0, 0], [0, 0, -1, 0], [0, 1, 0, -20], [0, 0, 0, 1]], dtype=float)  # x right, -z up
    return (proj @ view).reshape(-1).tolist()


def scene() -> tuple[np.ndarray, np.ndarray]:
    """A keyed crowd: rings of skeletons in the key's cyan round a white hero, one orange big ork; dark bodies, 2 px
    lines."""
    palette = pal.load()
    colour = {1: pal.rgb(palette["enemies"]["key"]["cyan"]), 7: pal.rgb(palette["enemies"]["counter"]["orange"]),
              250: pal.rgb(palette["hero"]["body"])}
    body = pal.rgb(palette["void"]["body"])
    rgb = np.full((H, W, 3), pal.rgb(palette["void"]["floor"]), dtype=np.uint8)
    ids = np.zeros((H, W), dtype=np.uint8)
    yy, xx = np.mgrid[0:H, 0:W]
    rng = np.random.default_rng(7)
    bodies = [(W / 2, H / 2, 16, 250), (W / 2 + 200, H / 2 - 90, 24, 7)]
    bodies += [(x, y, 13, 1) for x, y in rng.uniform((40, 40), (W - 40, H - 40), (70, 2))
               if np.hypot(x - W / 2, y - H / 2) > 60]
    for x, y, r, code in bodies:
        d = np.hypot(xx - x, yy - y)
        inside = d <= r
        lit = inside & (d > r - 2)
        rgb[inside], rgb[lit], ids[inside] = body, colour[code], code
    return rgb, ids


def write(dirpath: Path, name: str, rgb: np.ndarray, ids: np.ndarray) -> None:
    Image.fromarray(rgb).save(dirpath / f"still-{name}.png")
    Image.fromarray(np.stack([ids] * 3, axis=-1)).save(dirpath / f"id-{name}.png")
    (dirpath / f"view-{name}.json").write_text(json.dumps({"viewProj": view_proj(), "hero": [0, 0, 0], "ids": IDS}))


def crowd(tmp_path: Path, rgb: np.ndarray | None = None, ids: np.ndarray | None = None) -> Path:
    """A stills folder holding one still of the keyed crowd (or of `rgb` over `ids`)."""
    stills = tmp_path / "stills"
    stills.mkdir()
    if rgb is None:
        rgb, ids = scene()
    write(stills, "crowd-1.000", rgb, ids)
    return stills


def run(stills: Path, capsys, **args) -> tuple[int, str, Path]:
    """champions look on the folder, with --out beside it: the exit code, what it said on stderr, the evidence."""
    out = stills.parent / "evidence"
    a = argparse.Namespace(stills=str(stills), palette=None, thresholds=str(THRESHOLDS), impact=None, title="test",
                           out=str(out))
    vars(a).update(args)
    code = look.cmd_look(a)
    return code, capsys.readouterr().err, out


def test_a_keyed_crowd_keeps_every_guide_value_and_every_key_is_reported(tmp_path, capsys):
    code, _, out = run(crowd(tmp_path), capsys)
    text, metrics = (out / "metrics.md").read_text(), json.loads((out / "metrics.json").read_text())
    assert code == 0
    assert metrics["stills"]["crowd-1.000"]["strays"] == {}, text
    assert text.startswith("# Look diagnostics: test\n\nA diagnostic, not a verdict")
    for key in json.loads(THRESHOLDS.read_text())["rules"]:
        assert f"`{key}`" in text
    assert (out / "crowd-1.000.jpg").read_bytes().find(b"ps-render") > 0


def test_a_still_straying_far_from_the_guide_values_still_exits_0(tmp_path, capsys):
    rgb, ids = scene()
    rgb[ids == 0] = (60, 70, 90)  # a lit floor
    rgb[ids > 0] = 255  # flat, clipped white monsters, as bright as the hero
    code, _, out = run(crowd(tmp_path, rgb, ids), capsys)
    text = (out / "metrics.md").read_text()
    strays = json.loads((out / "metrics.json").read_text())["stills"]["crowd-1.000"]["strays"]
    assert code == 0
    assert {"floor_p50_max", "background_light_max", "white_max", "monster_white_max", "clipped_max",
            "hero_margin_min", "monster_form_min"} <= set(strays), strays
    assert "floor_p50" in strays["floor_p50_max"] and "strays from <= 8" in strays["floor_p50_max"]
    assert "(strays)" in text and "FAIL" not in text


def no_stills(stills: Path) -> dict:
    for p in stills.iterdir():
        p.unlink()
    return {}


def an_unreadable_still(stills: Path) -> dict:
    (stills / "still-crowd-1.000.png").write_bytes(b"not a png")
    return {}


def a_broken_view(stills: Path) -> dict:
    (stills / "view-crowd-1.000.json").write_text("{")
    return {}


def an_id_pass_of_another_size(stills: Path) -> dict:
    Image.new("RGB", (W // 2, H // 2)).save(stills / "id-crowd-1.000.png")
    return {}


def a_broken_palette(stills: Path) -> dict:
    palette = stills.parent / "palette.json"
    palette.write_text('{"name": ')
    return {"palette": str(palette)}


@pytest.mark.parametrize("breakage", [no_stills, an_unreadable_still, a_broken_view, an_id_pass_of_another_size,
                                      a_broken_palette])
def test_what_it_cannot_measure_exits_non_zero(tmp_path, capsys, breakage):
    stills = crowd(tmp_path)
    code, err, out = run(stills, capsys, **breakage(stills))
    assert code != 0
    assert err.startswith("look: ")
    assert not (out / "metrics.md").exists()
