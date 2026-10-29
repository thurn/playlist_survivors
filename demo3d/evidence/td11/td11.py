"""TD11's cuts: A/B captures, FLIP scores and harness costs (benchmarks.md, Fidelity A/B Checks).

From the worktree's root: `uv run --project demo3d/tools python demo3d/evidence/td11/td11.py <step> ...`.

    capture NAME [--cuts SPEC] [-- extra player args]
        12 PNG stills of S3 (demo load, 40 m, N) at fixed time over 20 s, and a 2 s fixed-time video,
        into the gitignored captures/td11/NAME/ and captures/td11/NAME.mp4, with every cut parameter
        passed explicitly and recorded with the player's "effective cuts" line in NAME/args.json.
        Holds TD7's machine lock.
    calibrate
        captures base (every cut off), base-repeat (the run-to-run floor), the invisible anchor
        (--time-offset 0.001) and the visible anchor (--pose-rate 15), then writes thresholds.json.
    cut ARM|all | cut NAME --cuts SPEC [--param key=value ...] [-- extra]
        captures an ARMS entry (or every one, or an ad hoc set) and scores it against base:
        scores/NAME.json, and an A/B crop (a small ps-render JPEG) in ab/.
    cost ARM | cost NAME --cuts SPEC [--param key=value ...] [--scene S3] [--n N] [--reps 2]
        fixed-N harness runs, every cut off (base) against the cuts on, interleaved, both with the same
        explicit parameters: costs/<scene>-NAME.json, each run with its effective-cuts line.
    frontier NAME --cuts SPEC [--opt key=value ...]
        a 40 m S3 frontier search with these cuts: costs/frontier-NAME.json.
    table
        prints the cuts table (scores against the thresholds, costs, the effective cuts).

Parameters (sceneOptions keys): lodPx "a,b", shadowLod 1|2, lightCell (m), mergePx.
The scene is S3 at demo load and a 40 m view in the arena (E2, 1x), MSAA 4x, the game camera
(DemoMain's stress path: `--scene S3 --n N --view-width 40 --load demo`), with every cut off in
the A run (`--cuts none`). N is min(2,000, the 40 m frontier) (N below).
"""

from __future__ import annotations

import argparse
import json
import shutil
import statistics
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools import flip, sweep  # noqa: E402

PROJECT = ROOT / "demo3d"
EXE = PROJECT / "Builds" / "il2cpp" / "PlaylistSurvivors3D.exe"
CAPTURES = ROOT / "captures" / "td11"
EVIDENCE = Path(__file__).resolve().parent
SCORES = EVIDENCE / "scores"
COSTS = EVIDENCE / "costs"
AB = EVIDENCE / "ab"
THRESHOLDS = EVIDENCE / "thresholds.json"

N = 600  # min(2,000, S3's 40 m frontier with every cut off, about 600): costs/pre-review/frontier-none.json and the notes
START = 8.0
STILLS = [10.0 + 1.8 * i for i in range(12)]  # 12 frames over 19.8 s, each on the 1/60 s grid
VIDEO_AT, VIDEO_S = 14.0, 2.0
FEEL: list[str] = []  # camera punch and shake stay on, alike in A, B and both anchors (73f463c keeps them off the time offset)
SCENE = ["--scene", "S3", "--view-width", "40", "--load", "demo", "--aa", "msaa4", "--arena", "e2", "--density", "1"]
WINDOW = ["-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0", "-job-worker-count", "6"]
CROP = (720, 330, 480, 270)  # x, y, w, h from the top left: the view's centre, where the hero meets the horde


def player(out: Path, args: list[str], log: str) -> None:
    cmd = [str(EXE), *WINDOW, "-logFile", str(out / log), *args]
    print("td11:", " ".join(cmd), flush=True)
    code = subprocess.run(cmd, cwd=PROJECT, timeout=1800).returncode
    if code != 0:
        raise SystemExit(f"td11: player exited {code} ({out / log})")


# Every run passes every cut parameter explicitly, so a capture or cost never depends on Cuts.cs's
# defaults; each records the cuts and parameters the player reports in its log ("effective cuts [...]").
PARAMS = {"lodPx": "60,30", "shadowLod": 2, "lightCell": 3.0, "mergePx": 8.0}

# The cuts tried: name -> (cut spec, parameter overrides).
ARMS = {
    "lod-60-30": ("lod", {"lodPx": "60,30"}),
    "lod-90-45": ("lod", {"lodPx": "90,45"}),
    "lod-120-60": ("lod", {"lodPx": "120,60"}),
    "shadow-proxy-lod2": ("shadow-proxy", {"shadowLod": 2}),
    "shadow-proxy-lod1": ("shadow-proxy", {"shadowLod": 1}),
    "clips30": ("clips30", {}),
    "light-cell-3m": ("light-cell", {"lightCell": 3.0}),
    "light-cell-2m": ("light-cell", {"lightCell": 2.0}),
    "merge-8px": ("merge", {"mergePx": 8.0}),
    "drop-glow": ("drop-glow", {}),
}


def params_of(overrides: dict | None) -> dict:
    return {**PARAMS, **(overrides or {})}


def param_args(params: dict) -> list[str]:
    return ["--lod-px", str(params["lodPx"]), "--shadow-lod", str(params["shadowLod"]),
            "--light-cell", f"{params['lightCell']:g}", "--merge-px", f"{params['mergePx']:g}"]


def effective(log: Path) -> str:
    """The player's own record of the cuts and parameters in force (DemoMain or TimelineScene)."""
    for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
        if "effective cuts [" in line:
            return line[line.index("cuts ["):].split(" (--cuts")[0].strip()
    raise SystemExit(f"td11: no 'effective cuts' line in {log}")


def capture(name: str, cuts: str, extra: list[str], n: int = N, video: bool = True, params: dict | None = None) -> Path:
    out = CAPTURES / name
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    params = params_of(params)
    common = [*SCENE, "--n", str(n), "--cuts", cuts, *param_args(params), *FEEL, *extra]
    with sweep.machine_lock(f"TD11 A/B capture {name}"):
        player(out, [*common, "--start-at", f"{START:g}", "--capture-stills", str(out), "--stills-format", "png",
                     "--stills-at", ",".join(f"{t:.1f}" for t in STILLS)], "stills.log")
        if video:
            player(out, [*common, "--start-at", f"{VIDEO_AT:g}", "--seconds", f"{VIDEO_S:g}",
                         "--capture-video", str(CAPTURES / f"{name}.mp4")], "video.log")
    got = sorted(out.glob("still-*.png"))
    if len(got) != len(STILLS):
        raise SystemExit(f"td11: {name}: {len(got)} stills, expected {len(STILLS)}")
    record = {"cuts": cuts, "params": params, "n": n, "feel": FEEL, "extra": extra,
              "effective": effective(out / "stills.log"), "args": common}
    (out / "args.json").write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return out


def tagged_jpeg(img, path: Path, note: str, quality: int = 88) -> None:
    img.save(path, "JPEG", quality=quality, comment=f"ps-render TD11 {note}".encode())


def ab_crops(name: str, frames: list[dict]) -> None:
    """The frame with the largest mean FLIP: A and B centre crops at 100%, side by side, as one small tagged JPEG."""
    from PIL import Image

    AB.mkdir(parents=True, exist_ok=True)
    worst = max(frames, key=lambda f: f["mean"])["frame"]
    x, y, w, h = CROP
    a = Image.open(CAPTURES / "base" / worst).convert("RGB").crop((x, y, x + w, y + h))
    b = Image.open(CAPTURES / name / worst).convert("RGB").crop((x, y, x + w, y + h))
    pair = Image.new("RGB", (2 * w + 8, h), (255, 255, 255))
    pair.paste(a, (0, 0))
    pair.paste(b, (w + 8, 0))
    tagged_jpeg(pair, AB / f"{name}-{Path(worst).stem}.jpg", f"A/B {name} {worst}: A (every cut off) left, B right, 100%")


def score(name: str) -> dict:
    result = flip.score_dirs(CAPTURES / "base", CAPTURES / name)
    if THRESHOLDS.exists():
        lines = json.loads(THRESHOLDS.read_text(encoding="utf-8"))
        result["pass"] = flip.verdict(result, lines)
        result["lines"] = {"mean": lines["mean"], "p99": lines["p99"]}
    result["a"], result["b"] = "captures/td11/base", f"captures/td11/{name}"
    result["args"] = json.loads((CAPTURES / name / "args.json").read_text(encoding="utf-8"))
    SCORES.mkdir(parents=True, exist_ok=True)
    (SCORES / f"{name}.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"td11: {name}: mean {result['mean']:.5f}, p99 {result['p99']:.4f}" +
          (f" -> {'PASS' if result['pass'] else 'FAIL'}" if "pass" in result else ""), flush=True)
    return result


def cmd_calibrate(a) -> None:
    if not a.score_only:
        capture("base", "none", [])
        capture("base-repeat", "none", [])
        capture("invisible", "none", ["--time-offset", "0.001"])
        capture("visible", "none", ["--pose-rate", "15"])
    floor = score("base-repeat")
    inv = flip.score_dirs(CAPTURES / "base", CAPTURES / "invisible")
    vis = flip.score_dirs(CAPTURES / "base", CAPTURES / "visible")
    lines = flip.thresholds(inv, vis)
    lines.update({
        "scene": "S3", "n": N, "viewWidthM": 40, "load": "demo", "aa": "msaa4", "arena": "e2", "density": 1,
        "stillsAt": STILLS, "startAt": START, "resolution": [1920, 1080],
        "floor": {"mean": floor["mean"], "p99": floor["p99"], "note": "base against a second base run"},
        "invisibleFrames": inv["frames"], "visibleFrames": vis["frames"],
        "rule": "a cut passes when its mean and p99 are both at or below invisible + (visible - invisible) / 3",
    })
    THRESHOLDS.write_text(json.dumps(lines, indent=2) + "\n", encoding="utf-8")
    print(f"td11: invisible mean {inv['mean']:.5f} p99 {inv['p99']:.4f}; visible mean {vis['mean']:.5f} p99 {vis['p99']:.4f}")
    print(f"td11: pass at mean <= {lines['mean']:.5f} and p99 <= {lines['p99']:.4f} -> {THRESHOLDS}")
    for anchor, s in (("invisible", inv), ("visible", vis)):
        ab_crops(anchor, s["frames"])


def cmd_cut(a) -> None:
    names = list(ARMS) if a.name == "all" else [a.name]
    for name in names:
        if not a.score_only:
            if a.cuts:
                capture(name, a.cuts, a.extra, params=opts(a.param))
            elif name in ARMS:
                cuts, over = ARMS[name]
                capture(name, cuts, a.extra, params={**over, **opts(a.param)})
            else:
                raise SystemExit(f"td11: {name} is not in ARMS; pass --cuts")
        r = score(name)
        ab_crops(name, r["frames"])


def spec_for(scene: str, n: int, cuts: str, search: bool = False, options: dict | None = None) -> dict:
    body = {
        "_comment": f"TD11's cut cost: {scene} at demo load, 40 m, in the arena (E2, 1x), cuts {cuts}.",
        "scene": scene, "seconds": 20, "warmup": 5, "rewarm": 3,
        "viewWidthM": [40], "load": ["demo"], "aa": ["msaa4"], "jobWorkers": 6,
        "quiet": {"maxWaitS": 30, "repeatWaitS": 10, "repeats": 0},
        "sceneOptions": {"crowd": "bone", "arena": "e2", "density": 1, "cuts": cuts, **(options or {})},
    }
    if not search:
        body["n"] = {"fixed": [n]}
    return body


def opts(pairs: list[str]) -> dict:
    """sceneOptions from key=value pairs (lodPx=120,60, lightCell=2, shadowLod=1, mergePx=8)."""
    kv = dict(o.split("=", 1) for o in pairs or [])
    return {k: (float(v) if k in ("lightCell", "mergePx") else int(v) if k == "shadowLod" else v) for k, v in kv.items()}


def run_spec(tag: str, body: dict) -> list[dict]:
    out = CAPTURES / "sweeps" / tag
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    spec = out / "spec.json"
    spec.write_text(json.dumps(body, indent=2) + "\n", encoding="utf-8")
    cmd = ["uv", "run", "--project", str(PROJECT / "tools"), "demo3d", "sweep", str(spec), "--out", str(out)]
    print("td11:", " ".join(cmd), flush=True)
    code = subprocess.run(cmd, cwd=ROOT).returncode
    rows = [json.loads(line) for line in (out / "rows.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()] \
        if (out / "rows.jsonl").exists() else []
    if code != 0 and not rows:
        raise SystemExit(f"td11: sweep {tag} exited {code} with no rows")
    return rows


KEEP = ("n", "gpuP50Ms", "gpuP99Ms", "mainP99Ms", "renderP99Ms", "onScreenAlive", "onScreenDying", "particles",
        "lightsUsed", "lightDropShareP99", "transparentPerPixel", "noisy", "noiseReasons", "pass", "kind", "attempt", "time")


def last_full(rows: list[dict]) -> dict:
    full = [r for r in rows if r.get("kind") in (None, "full", "fixed")] or rows
    r = full[-1]
    return {k: r.get(k) for k in KEEP}


def cmd_cost(a) -> None:
    cuts, over = (a.cuts, {}) if a.cuts else ARMS[a.name]
    params = params_of({**over, **opts(a.param)})
    arms = {"base": "none", a.name: cuts}
    runs = {"base": [], a.name: []}
    for rep in range(a.reps):
        for arm in (("base", a.name) if rep % 2 == 0 else (a.name, "base")):
            tag = f"{a.scene.lower()}-{arm}-{a.name}-{rep}"
            rows = run_spec(tag, spec_for(a.scene, a.n, arms[arm], options=params))
            row = last_full(rows)
            row["cuts"], row["params"] = arms[arm], params
            row["effective"] = effective(CAPTURES / "sweeps" / tag / "player.log")
            runs[arm].append(row)

    def med(arm, key):
        v = [r[key] for r in runs[arm] if r.get(key) is not None]
        return statistics.median(v) if v else None
    summary = {k: {"base": med("base", k), "cut": med(a.name, k)} for k in ("gpuP50Ms", "gpuP99Ms", "mainP99Ms", "renderP99Ms")}
    for k, v in summary.items():
        if v["base"] is not None and v["cut"] is not None:
            v["saved"] = v["base"] - v["cut"]
    result = {"cut": a.name, "cuts": cuts, "params": params, "scene": a.scene, "n": a.n, "reps": a.reps,
              "summary": summary, "runs": runs}
    COSTS.mkdir(parents=True, exist_ok=True)
    (COSTS / f"{a.scene.lower()}-{a.name}.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summary, indent=2))


def cmd_frontier(a) -> None:
    tag = f"frontier-{a.name}"
    out = CAPTURES / "sweeps" / tag
    rows = [json.loads(x) for x in (out / "rows.jsonl").read_text(encoding="utf-8").splitlines() if x.strip()]         if a.reuse else run_spec(tag, spec_for("S3", 0, a.cuts, search=True, options=params_of(opts(a.opt))))
    keep = [{k: r.get(k) for k in KEEP} for r in rows]
    fl = CAPTURES / "sweeps" / tag / "frontier.jsonl"
    frontier = [json.loads(x) for x in fl.read_text(encoding="utf-8").splitlines() if x.strip()] if fl.exists() else []
    result = {"cut": a.name, "cuts": a.cuts, "scene": "S3", "viewWidthM": 40, "load": "demo",
              "frontierN": frontier[-1].get("n") if frontier else None, "frontier": frontier, "rows": keep}
    COSTS.mkdir(parents=True, exist_ok=True)
    (COSTS / f"frontier-{a.name}.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"td11: frontier {a.name}: {result['frontierN']}")


def cmd_table(a) -> None:
    lines = json.loads(THRESHOLDS.read_text(encoding="utf-8"))
    print(f"thresholds: mean <= {lines['mean']:.5f}, p99 <= {lines['p99']:.4f} (invisible {lines['invisible']['mean']:.5f} / "
          f"{lines['invisible']['p99']:.4f}; visible {lines['visible']['mean']:.5f} / {lines['visible']['p99']:.4f}; "
          f"floor {lines['floor']['mean']:.5f} / {lines['floor']['p99']:.4f})")
    print("| run | effective cuts | mean FLIP | p99 FLIP | A/B | S3 GPU p50 / p99 saved, ms | S1 GPU p50 / p99 saved, ms |")
    print("|---|---|---|---|---|---|---|")
    for p in sorted(SCORES.glob("*.json")):
        s = json.loads(p.read_text(encoding="utf-8"))

        def saved(scene):
            c = COSTS / f"{scene}-{p.stem}.json"
            if not c.exists():
                return ""
            m = json.loads(c.read_text(encoding="utf-8"))["summary"]
            return f"{m['gpuP50Ms'].get('saved', 0):+.2f} / {m['gpuP99Ms'].get('saved', 0):+.2f}"
        eff = (s.get("args") or {}).get("effective", "").split(" (--cuts")[0]
        verdict = "floor" if p.stem == "base-repeat" else "pass" if s.get("pass") else "fail"
        print(f"| {p.stem} | {eff} | {s['mean']:.5f} | {s['p99']:.4f} | {verdict} | {saved('s3')} | {saved('s1')} |")


def main() -> int:
    p = argparse.ArgumentParser(prog="td11.py")
    sub = p.add_subparsers(dest="step", required=True)
    c = sub.add_parser("capture"); c.add_argument("name"); c.add_argument("--cuts", default="none"); c.add_argument("extra", nargs="*")
    c.add_argument("--n", type=int, default=N); c.add_argument("--no-video", action="store_true")
    k = sub.add_parser("calibrate"); k.add_argument("--score-only", action="store_true")
    u = sub.add_parser("cut", help="an ARMS name, 'all', or any name with --cuts")
    u.add_argument("name"); u.add_argument("--cuts"); u.add_argument("extra", nargs="*")
    u.add_argument("--param", action="append", default=[], help="a cut parameter, key=value (lodPx, shadowLod, lightCell, mergePx)")
    u.add_argument("--score-only", action="store_true")
    o = sub.add_parser("cost", help="an ARMS name, or any name with --cuts")
    o.add_argument("name"); o.add_argument("--cuts")
    o.add_argument("--param", action="append", default=[], help="a cut parameter, key=value")
    o.add_argument("--scene", default="S3"); o.add_argument("--n", type=int, default=N); o.add_argument("--reps", type=int, default=2)
    f = sub.add_parser("frontier"); f.add_argument("name"); f.add_argument("--cuts", required=True)
    f.add_argument("--reuse", action="store_true", help="summarize the existing sweep folder")
    f.add_argument("--opt", action="append", default=[], help="sceneOptions key=value")
    sub.add_parser("table")
    a = p.parse_args()
    if a.step == "capture":
        capture(a.name, a.cuts, a.extra, a.n, not a.no_video)
    elif a.step == "calibrate":
        cmd_calibrate(a)
    elif a.step == "cut":
        cmd_cut(a)
    elif a.step == "cost":
        cmd_cost(a)
    elif a.step == "frontier":
        cmd_frontier(a)
    else:
        cmd_table(a)
    return 0


if __name__ == "__main__":
    sys.exit(main())
