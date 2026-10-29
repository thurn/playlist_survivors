"""TD12's frontier tables from sweep folders.

    uv run --project demo3d/tools python demo3d/evidence/td12/summarize.py <dir>... [--out table.md]

Each <dir> holds a sweep's rows.jsonl and frontier.jsonl (demo3d sweep --out). For every frontier
point it prints the found N (the requested on-screen count the search settled on), the on-screen
count the deciding run measured (alive + dying, frustum test per monster), the limiting resource,
the deciding run's GPU / main / render p99, the compute estimate (GPU p99 x 2.5 x 1.2,
performance.md), the fill count (visible ground at one monster per m2), the sim's tick p99 at that
N, the effect and overdraw counters, and the noise. Then the envelope by benchmarks.md's rule, at
the demo load with MSAA 4x, from the first folder given (the full sweep: the other folders hold
other lines, modes or crowds).

It also gives each point's count at the alternative GPU line TD7 found (RTX 4060 Laptop Steel Nomad
medians of about 2,240 to 2,270 rather than 2,025: ratio 2.22x, GPU line 4.69 ms), *fitted* from
the point's own runs (a least-squares line of GPU p99 against N over every run at the point,
probes included), not searched: an inference, labelled as one.
"""

from __future__ import annotations

import json
import statistics
import sys
from pathlib import Path

GPU_LINE, CPU_LINE, ALT_GPU_LINE = 4.2, 10.4, 4.69


def read_jsonl(p: Path) -> list[dict]:
    if not p.exists():
        return []
    return [json.loads(x) for x in p.read_text(encoding="utf-8").splitlines() if x.strip()]


def fit_crossing(rows: list[dict], key: str, line: float) -> float | None:
    pts = [(r["n"], r[key]) for r in rows if r.get(key) is not None and r["n"] > 0]
    if len(pts) < 3:
        return None
    xs, ys = zip(*pts)
    mx, my = statistics.fmean(xs), statistics.fmean(ys)
    sxx = sum((x - mx) ** 2 for x in xs)
    if sxx == 0:
        return None
    b = sum((x - mx) * (y - my) for x, y in pts) / sxx
    a = my - b * mx
    if b <= 0:
        return None
    return (line - a) / b


def fmt(v, d=2):
    if v is None:
        return "–"
    if isinstance(v, float):
        return f"{v:.{d}f}"
    if isinstance(v, int):
        return f"{v:,}"
    return str(v)


def main(argv: list[str]) -> int:
    out = None
    if "--out" in argv:
        i = argv.index("--out")
        out = Path(argv[i + 1])
        argv = argv[:i] + argv[i + 2:]
    lines: list[str] = []
    w = lines.append
    demo_points = []
    for d in argv:
        folder = Path(d)
        rows = read_jsonl(folder / "rows.jsonl")
        frontier = read_jsonl(folder / "frontier.jsonl")
        if not frontier:
            continue
        w(f"## {folder.name}")
        w("")
        w("| scene | load | aa | view | found N | on screen (alive + dying) | limit | GPU p99 | main p99 | render p99 "
          "| compute est. | fill | fills | sim tick p99 | particles | lights (max) | drop p99 | transp./px (beat peak) "
          "| full runs | rests on noisy | at 4.69 ms (fitted) |")
        w("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
        for p in frontier:
            at = [r for r in rows if r["sweep"] == p["sweep"] and r["viewWidthM"] == p["viewWidthM"]
                  and r["load"] == p["load"] and r["aa"] == p["aa"]]
            deciding = [r for r in at if r["n"] == p["n"] and r["kind"] != "probe"]
            # The row the search used: the non-noisy attempt, else the median attempt (matched on GPU p99).
            row = next((r for r in deciding if r.get("gpuP99Ms") == p.get("gpuP99Ms")), deciding[-1] if deciding else None)
            fill = round(p["visibleGroundM2"])
            on = None
            if row and row.get("onScreenAlive") is not None:
                on = row["onScreenAlive"] + (row.get("onScreenDying") or 0)
            alt = fit_crossing(at, "gpuP99Ms", ALT_GPU_LINE)
            cpu = min([c for c in (fit_crossing(at, "mainP99Ms", CPU_LINE), fit_crossing(at, "renderP99Ms", CPU_LINE)) if c], default=None)
            alt_n = None if alt is None else int(min(alt, cpu) if cpu else alt)
            alt_n = min(alt_n, p["ceiling"]) if alt_n is not None else None
            n = p["n"]
            label = "below range" if n is None else f"{n:,}" + (" (capacity)" if p.get("capacityBound") else "")
            w(f"| {p['scene']} | {p['load']} | {p['aa']} | {p['viewWidthM']:g} m | {label} | {fmt(on)} | {p['limit']} | "
              f"{fmt(p.get('gpuP99Ms'))} | {fmt(p.get('mainP99Ms'))} | {fmt(p.get('renderP99Ms'))} | "
              f"{fmt(p.get('computeEstimateMs'))} | {fill:,} | {'yes' if on is not None and on >= fill else 'no'} | "
              f"{fmt(p.get('simTickP99Ms'))} | {fmt(row.get('particles') if row else None)} | "
              f"{fmt(row.get('lightsUsed') if row else None)} | {fmt(row.get('lightDropShareP99') if row else None, 3)} | "
              f"{fmt(row.get('transparentPerPixel') if row else None)} ({fmt(row.get('transparentBeatPeak') if row else None)}) | "
              f"{p['fullRuns']} | {'yes' if p['restsOnNoisy'] else 'no'} | {fmt(alt_n)} |")
            if d == argv[0] and p["scene"] == "S3" and p["load"] == "demo" and p["aa"] == "msaa4":
                demo_points.append(p)
        w("")
    if demo_points:
        best = None
        for p in sorted(demo_points, key=lambda p: p["viewWidthM"]):
            if p["n"] is not None and p["n"] >= round(p["visibleGroundM2"]):
                best = p
        rule = "widest width that fills its screen"
        if best is None:
            best = next((p for p in demo_points if p["viewWidthM"] == 30), None)
            rule = "no width fills its screen, so 30 m"
        if best is not None and best["n"] is not None:
            w(f"**Envelope** ({rule}): {best['viewWidthM']:g} m, {best['n'] // 100 * 100:,} on screen "
              f"(frontier {best['n']:,}).")
            w("")
    text = "\n".join(lines) + "\n"
    if out:
        out.write_text(text, encoding="utf-8")
    sys.stdout.write(text)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
