"""Turns TD5's result rows (rows.jsonl, ablations.jsonl) into the Markdown tables in README.md.

uv run --project demo3d/tools python demo3d/evidence/td5/table.py demo3d/evidence/td5/rows.jsonl
"""

from __future__ import annotations

import json
import statistics
import sys
from collections import defaultdict


def load(path: str) -> list[dict]:
    with open(path, encoding="utf-8") as f:
        return [json.loads(line) for line in f if line.strip()]


def breakdown(rows: list[dict]) -> None:
    runs = defaultdict(list)
    for r in rows:
        if str(r.get("tag", "")).startswith("breakdown-") and not r.get("search"):
            runs[(r["scene"], r["arm"])].append(r)
    print("| Scene | Arm | Runs | GPU p50 / p99 (ms) | Main p50 / p99 (ms) | Render p50 / p99 (ms) | On screen (alive + dying) | Shadow casters | Draw calls | Pass |")
    print("|---|---|---|---|---|---|---|---|---|---|")
    for (scene, arm), rs in sorted(runs.items()):
        def m(key: str) -> str:
            return f"{statistics.median(x[key] for x in rs):.2f}"
        on = statistics.median(x["onScreenAlive"] + x["onScreenDying"] for x in rs)
        print(f"| {scene} | {arm} | {len(rs)} | {m('gpuP50Ms')} / {m('gpuP99Ms')} | {m('mainP50Ms')} / {m('mainP99Ms')} | "
              f"{m('renderP50Ms')} / {m('renderP99Ms')} | {on:.0f} | {statistics.median(x['shadowCasters'] for x in rs):.0f} | "
              f"{rs[0]['draws']} | {sum(x['pass'] for x in rs)}/{len(rs)} |")


def searches(rows: list[dict]) -> None:
    print("\n| Scene | Arm | Max N (passes) | First failing N | Fitted candidate | Limit | On screen at max |")
    print("|---|---|---|---|---|---|---|")
    for r in rows:
        if r.get("search"):
            print(f"| {r['scene']} | {r['arm']} | {r['maxN']} | {r['firstFail']} | {r['candidate']} | {r['limit']} | {r['onScreenAtMax']} |")


def ablations(rows: list[dict]) -> None:
    print("\n| Arm | Ablation | GPU p50 / p99 (ms) |")
    print("|---|---|---|")
    for r in rows:
        if r.get("arm") and not r.get("search"):
            print(f"| {r['arm']} | {r.get('ablate') or 'none'} | {r['gpuP50Ms']:.2f} / {r['gpuP99Ms']:.2f} |")


if __name__ == "__main__":
    rows = load(sys.argv[1])
    if len(sys.argv) > 2 and sys.argv[2] == "ablations":
        ablations(rows)
    else:
        breakdown(rows)
        searches(rows)
