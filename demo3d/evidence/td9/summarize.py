"""TD9's tables from sweep rows: `uv run --project demo3d/tools python demo3d/evidence/td9/summarize.py <dir>...`.

Each <dir> holds a sweep's rows.jsonl (demo3d sweep --out). Prints one markdown row per run:
scene, rung and density (from the row's environment counters), view width, AA, N, GPU p50 and
p99, main and render p99, the environment counters, and the noise verdict. Only the last attempt
of a repeated run is kept (the harness repeats a noisy run up to twice).
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


def rows(d: Path) -> list[dict]:
    out: dict[tuple, dict] = {}
    for line in (d / "rows.jsonl").read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        r = json.loads(line)
        env = r.get("environment") or {}
        key = (r.get("sweep"), r["scene"], env.get("rung"), env.get("density"), r["viewWidthM"], r["aa"], r["n"])
        out[key] = r  # the last attempt wins
    return list(out.values())


def f(v, digits=2):
    return "" if v is None else f"{v:.{digits}f}" if isinstance(v, float) else str(v)


def main() -> None:
    print("| scene | rung | density | view | aa | n | GPU p50 | GPU p99 | main p99 | render p99 | kit visible | kit casters | draws | batches | set-pass | tris | noisy |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for arg in sys.argv[1:]:
        for r in sorted(rows(Path(arg)), key=lambda r: (r["scene"], str((r.get("environment") or {}).get("rung")),
                                                       (r.get("environment") or {}).get("density") or 0, r["viewWidthM"], r["aa"], r["n"])):
            e = r.get("environment") or {}
            tris = e.get("triangles")
            print("| " + " | ".join([
                r["scene"], str(e.get("rung", "")), f(e.get("density")), f(r["viewWidthM"], 0), r["aa"], str(r["n"]),
                f(r.get("gpuP50Ms")), f(r.get("gpuP99Ms")), f(r.get("mainP99Ms")), f(r.get("renderP99Ms")),
                f(e.get("kitVisible")), f(e.get("kitShadowCasters")), f(e.get("drawCalls")), f(e.get("batches")),
                f(e.get("setPassCalls")), f"{tris / 1e6:.2f} M" if tris else "",
                ("yes: " + "; ".join(r.get("noiseReasons") or [])[:60]) if r.get("noisy") else "no",
            ]) + " |")


if __name__ == "__main__":
    main()
