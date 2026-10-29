"""TD14's tables from sweep rows: `uv run --project demo3d/tools python demo3d/evidence/td14/summarize.py <dir>...`.

Each <dir> holds a sweep's rows.jsonl (demo3d sweep --out). Rows are grouped by configuration: the
scene, the arena's rung switches (from the row's environment counters: rung, density, GPU Resident
Drawer, merged, texture cap, prepass), view width, AA and N, and kept apart by round (the folder)
and by the run's kind (a fixed run or a frontier search's probe; the scene milestone's review: they
had been merged into one median). A noisy run is repeated up to twice by
the harness; each group prints the median of its attempts' p99s (as TD9's tables and TD7's search
do), the attempts' spread, and the last attempt's counters (one counter frame each).
"""

from __future__ import annotations

import json
import statistics
import sys
from pathlib import Path


def config(r: dict) -> tuple:
    e = r.get("environment") or {}
    return (
        r["scene"], e.get("rung"), e.get("density"), e.get("grd", "off"), bool(e.get("merged", False)),
        e.get("texCap", 0), e.get("prepass", "in"), r["viewWidthM"], r["aa"], r["n"], e.get("without", ""),
        r.get("_round", ""), r.get("kind", ""),
    )


def label(k: tuple) -> str:
    scene, rung, density, grd, merged, tex, prepass, _view, _aa, _n, without, _round, _kind = k
    parts = [str(rung)]
    if grd and grd != "off":
        parts.append("grd:" + grd)
    if merged:
        parts.append("merged")
    if tex:
        parts.append(f"tex:{tex}")
    if prepass == "out":
        parts.append("prepass:out")
    if without:
        parts.append("without:" + without)
    return " ".join(parts)


def load(dirs: list[str]) -> dict[tuple, list[dict]]:
    groups: dict[tuple, list[dict]] = {}
    for d in dirs:
        p = Path(d) / "rows.jsonl"
        if not p.exists():
            continue
        for line in p.read_text(encoding="utf-8").splitlines():
            if line.strip():
                r = json.loads(line)
                r["_round"] = Path(d).name
                groups.setdefault(config(r), []).append(r)
    return groups


def med(rows: list[dict], key: str) -> float | None:
    v = [r[key] for r in rows if r.get(key) is not None]
    return statistics.median(v) if v else None


def f(v, digits=2) -> str:
    return "" if v is None else f"{v:.{digits}f}" if isinstance(v, float) else str(v)


def table(groups: dict[tuple, list[dict]]) -> list[str]:
    out = [
        "| round | kind | scene | config | density | view | n | GPU p99 | main p99 | render p99 | attempts (GPU p99) | GRD | kit visible | casters (Unity) | draws | batches | set-pass | gfx MB | noisy |",
        "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for k in sorted(groups, key=lambda k: (k[11], k[12], k[0], str(k[1]), label(k), k[2] or 0, k[7], k[9])):
        rows = groups[k]
        last = rows[-1]
        e = last.get("environment") or {}
        out.append("| " + " | ".join([
            k[11], k[12], k[0], label(k), f"{k[2]:g}x" if k[2] else "", f(k[7], 0), str(k[9]),
            f(med(rows, "gpuP99Ms")), f(med(rows, "mainP99Ms")), f(med(rows, "renderP99Ms")),
            " / ".join(f(r.get("gpuP99Ms")) for r in rows),
            {True: "on", False: "off"}.get(e.get("grdActive"), ""),
            f(e.get("kitVisible")), f(e.get("unityShadowCasters")), f(e.get("drawCalls")), f(e.get("batches")),
            f(e.get("setPassCalls")), f(e.get("gfxUsedMB")),
            f"{sum(1 for r in rows if r.get('noisy'))}/{len(rows)}",
        ]) + " |")
    return out


def main() -> None:
    print("\n".join(table(load(sys.argv[1:]))))


if __name__ == "__main__":
    main()
