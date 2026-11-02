"""`champions look` on synthetic stills with ID passes: every threshold key reported, breaks named."""

import json
from pathlib import Path

import numpy as np
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


def scene(fill: dict[int, bool] | None = None) -> tuple[np.ndarray, np.ndarray]:
    """A keyed crowd: rings of skeletons in the key's cyan round a white hero, one orange big ork; dark bodies, 2 px
    lines. `fill` lights a code's whole body instead (a flat, lit monster)."""
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
        lit = inside if (fill or {}).get(code) else inside & (d > r - 2)
        rgb[inside], rgb[lit], ids[inside] = body, colour[code], code
    return rgb, ids


def write(dirpath: Path, name: str, rgb: np.ndarray, ids: np.ndarray) -> None:
    Image.fromarray(rgb).save(dirpath / f"still-{name}.png")
    Image.fromarray(np.stack([ids] * 3, axis=-1)).save(dirpath / f"id-{name}.png")
    (dirpath / f"view-{name}.json").write_text(json.dumps({"viewProj": view_proj(), "hero": [0, 0, 0], "ids": IDS}))


def run(tmp_path: Path, capsys) -> tuple[int, str, dict]:
    out = tmp_path / "evidence"

    class Args:
        stills, palette, thresholds, impact, title = str(tmp_path / "stills"), None, str(THRESHOLDS), None, "test"

    Args.out = str(out)
    code = look.cmd_look(Args)
    capsys.readouterr()
    return code, (out / "metrics.md").read_text(), json.loads((out / "metrics.json").read_text())


def test_a_keyed_crowd_passes_and_every_threshold_key_is_reported(tmp_path, capsys):
    (tmp_path / "stills").mkdir()
    write(tmp_path / "stills", "crowd-1.000", *scene())
    code, text, metrics = run(tmp_path, capsys)
    row = metrics["stills"]["crowd-1.000"]
    assert row["broken"] == {}, text
    assert code == 0
    for key in json.loads(THRESHOLDS.read_text())["rules"]:
        assert f"`{key}`" in text
    assert set(row["metrics"]["type_black_share"]) == {"skeleton", "bigOrk", "hero"}
    assert row["metrics"]["type_black_share"]["skeleton"] > 0.65
    assert (tmp_path / "evidence" / "crowd-1.000.jpg").read_bytes().find(b"ps-render") > 0


def test_a_flat_lit_type_breaks_its_black_share(tmp_path, capsys):
    (tmp_path / "stills").mkdir()
    rgb, ids = scene(fill={7: True})
    write(tmp_path / "stills", "flat-1.000", rgb, ids)
    code, text, metrics = run(tmp_path, capsys)
    broken = metrics["stills"]["flat-1.000"]["broken"]
    assert code == 1
    assert "type_black_share_min.bigOrk" in broken and "type_black_share_min.skeleton" not in broken
    assert "**FAIL**" in text


def test_a_lit_floor_breaks_the_floor_rules(tmp_path, capsys):
    (tmp_path / "stills").mkdir()
    rgb, ids = scene()
    rgb[ids == 0] = (60, 70, 90)
    write(tmp_path / "stills", "lit-1.000", rgb, ids)
    _, _, metrics = run(tmp_path, capsys)
    assert {"floor_p50_max", "background_light_max"} <= set(metrics["stills"]["lit-1.000"]["broken"])
