"""Clean solids for structures' recipes (`Cut.solid`): the corners of a box, a gabled roof, a prism and a pyramid,
in a model's own metres (x right, y up, z its depth). A recipe measures them off the vendor mesh, so a building keeps
its shape and loses its clutter (design.md, The Look > Principles: removing detail is the main tool)."""

from __future__ import annotations

import numpy as np


def box(x0: float, x1: float, y0: float, y1: float, z0: float, z1: float) -> np.ndarray:
    return np.array([[x, y, z] for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)], np.float64)


def gable(x0: float, x1: float, z0: float, z1: float, eaves: float, ridge: float, along: str = "z",
          ridge_at: float | None = None) -> np.ndarray:
    """A gabled roof over the rectangle [x0, x1] x [z0, z1]: its eaves at `eaves`, its ridge at `ridge` running along
    `along` ("x" or "z") at `ridge_at` across it (the middle by default)."""
    eave = [[x0, eaves, z0], [x0, eaves, z1], [x1, eaves, z0], [x1, eaves, z1]]
    if along == "z":
        at = 0.5 * (x0 + x1) if ridge_at is None else ridge_at
        top = [[at, ridge, z0], [at, ridge, z1]]
    else:
        at = 0.5 * (z0 + z1) if ridge_at is None else ridge_at
        top = [[x0, ridge, at], [x1, ridge, at]]
    return np.array(eave + top, np.float64)


def ring(cx: float, cz: float, radius: float, y: float, sides: int, turn: float = 0.0) -> np.ndarray:
    """`sides` corners round (cx, cz) at height y, the first `turn` radians from +x."""
    a = turn + np.linspace(0, 2 * np.pi, sides, endpoint=False)
    return np.stack([cx + radius * np.cos(a), np.full(sides, y), cz + radius * np.sin(a)], 1)


def prism(cx: float, cz: float, radius: float, y0: float, y1: float, sides: int = 8, turn: float = 0.0) -> np.ndarray:
    return np.vstack([ring(cx, cz, radius, y0, sides, turn), ring(cx, cz, radius, y1, sides, turn)])


def pyramid(cx: float, cz: float, radius: float, y0: float, apex: float, sides: int = 4, turn: float = 0.0) -> np.ndarray:
    return np.vstack([ring(cx, cz, radius, y0, sides, turn), [[cx, apex, cz]]])
