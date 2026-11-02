"""The edge-cut exchange format between Unity and the recipes (Assets/Champions/Editor/EdgeCuts/ArrayFile.cs reads
and writes the same): a JSON header beside a `.bytes` file of little-endian float32 (f4) and int32 (i4) arrays, each
listed in the header's `arrays` with its shape and byte offset. The header's other fields are the writer's own."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np

TYPES = {"f4": np.dtype("<f4"), "i4": np.dtype("<i4")}


def bytes_path(header: Path) -> Path:
    return header.with_suffix(".bytes")


def read(header: Path) -> tuple[dict, dict[str, np.ndarray]]:
    """The header's fields and its arrays by name."""
    meta = json.loads(header.read_text(encoding="utf-8"))
    raw = bytes_path(header).read_bytes()
    arrays = {}
    for a in meta["arrays"]:
        dtype = TYPES[a["type"]]
        count = int(np.prod(a["shape"])) if a["shape"] else 1
        arrays[a["name"]] = np.frombuffer(raw, dtype, count, a["offset"]).reshape(a["shape"]).copy()
    return meta, arrays


def write(header: Path, meta: dict, arrays: dict[str, np.ndarray]) -> None:
    """Writes `arrays` (float arrays as f4, integer ones as i4) and the header: `meta` with their index as `arrays`."""
    index = []
    chunks = []
    offset = 0
    for name, values in arrays.items():
        kind = "i4" if np.issubdtype(values.dtype, np.integer) else "f4"
        data = np.ascontiguousarray(values, TYPES[kind]).tobytes()
        index.append({"name": name, "type": kind, "shape": list(values.shape), "offset": offset})
        chunks.append(data)
        offset += len(data)
    header.parent.mkdir(parents=True, exist_ok=True)
    bytes_path(header).write_bytes(b"".join(chunks))
    header.write_text(json.dumps({**meta, "arrays": index}, indent=2) + "\n", encoding="utf-8")
