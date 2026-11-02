"""A vendor model as Unity exported it for its recipe (EdgeCutSource.cs): one mesh in root space at the model's
height (metres, y up, facing +z), each vertex skinned to 4 entries (bones, or rigid attachments) of its rig, every
triangle tagged with the renderer it came from, and the same vertices posed by the recipe's `pose` clip where it names
one. Recipes pick parts of it with `Selection`s."""

from __future__ import annotations

import fnmatch
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from . import arrays


def _matches(name: str, patterns: list[str] | tuple[str, ...]) -> bool:
    return any(fnmatch.fnmatchcase(name, p) for p in patterns)


@dataclass(frozen=True)
class Selection:
    """A set of the source's triangles (a mask over them)."""

    mask: np.ndarray

    def __or__(self, other: Selection) -> Selection:
        return Selection(self.mask | other.mask)

    def __and__(self, other: Selection) -> Selection:
        return Selection(self.mask & other.mask)

    def __sub__(self, other: Selection) -> Selection:
        return Selection(self.mask & ~other.mask)

    def __len__(self) -> int:
        return int(self.mask.sum())

    @property
    def indices(self) -> np.ndarray:
        return np.flatnonzero(self.mask)


class Source:
    def __init__(self, header: Path):
        meta, a = arrays.read(header)
        self.model: str = meta["model"]
        self.height: float = float(meta["heightM"])
        self.entries: list[str] = [e["name"] for e in meta["entries"]]
        self.entry_paths: list[str] = [e["path"] for e in meta["entries"]]
        self.renderers: list[str] = [r["name"] for r in meta["renderers"]]
        self.positions = a["positions"].astype(np.float64)
        # Where the model's pose leaves each vertex (a T-posed rig standing as in the game), else the bind pose.
        self.posed = a["posed"].astype(np.float64) if "posed" in a else self.positions
        self.normals = a["normals"].astype(np.float64)
        self.bones = a["bones"].astype(np.int64)
        self.weights = a["weights"].astype(np.float64)
        self.triangles = a["triangles"].astype(np.int64)
        self.triangle_renderer = a["triangle_renderer"].astype(np.int64)
        self.binds = a["binds"].astype(np.float64)
        self._dominant = None

    @property
    def triangle_count(self) -> int:
        return len(self.triangles)

    def all(self) -> Selection:
        return Selection(np.ones(self.triangle_count, bool))

    def none(self) -> Selection:
        return Selection(np.zeros(self.triangle_count, bool))

    def renderers_named(self, *patterns: str) -> Selection:
        """The triangles of every renderer whose name matches one of the glob patterns."""
        hits = np.array([_matches(n, patterns) for n in self.renderers], bool)
        if not hits.any():
            raise ValueError(f"{self.model}: no renderer matches {patterns}")
        return Selection(hits[self.triangle_renderer])

    def entry(self, name: str) -> int:
        """The first entry with this exact name."""
        try:
            return self.entries.index(name)
        except ValueError:
            raise ValueError(f"{self.model}: no entry (bone) named {name!r}") from None

    def dominant(self) -> np.ndarray:
        """Each triangle's dominant entry: the one with the most skin weight over its three corners."""
        if self._dominant is None:
            n = len(self.entries)
            score = np.zeros((self.triangle_count, n))
            rows = np.repeat(np.arange(self.triangle_count), 12)
            corner = self.triangles.reshape(-1)
            cols = self.bones[corner].reshape(-1)
            vals = self.weights[corner].reshape(-1)
            np.add.at(score, (rows, cols), vals)
            self._dominant = score.argmax(1)
        return self._dominant

    def bones_named(self, *patterns: str) -> Selection:
        """The triangles whose dominant entry's name matches one of the glob patterns."""
        hits = np.array([_matches(n, patterns) for n in self.entries], bool)
        if not hits.any():
            raise ValueError(f"{self.model}: no entry (bone) matches {patterns}")
        return Selection(hits[self.dominant()])

    def vertices_of(self, sel: Selection) -> np.ndarray:
        return np.unique(self.triangles[sel.mask])

    def dominant_entry(self, sel: Selection) -> int:
        """The entry holding the most skin weight over the selection's vertices (a rigid primitive's bone)."""
        v = self.vertices_of(sel)
        score = np.zeros(len(self.entries))
        np.add.at(score, self.bones[v].reshape(-1), self.weights[v].reshape(-1))
        return int(score.argmax())
