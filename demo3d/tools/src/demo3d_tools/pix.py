"""`demo3d pix`: a PIX GPU capture at one point and its per-pass GPU summary (benchmarks.md).

1. The player holds the point (`--hold`), warms up, then calls PIX's programmatic capture API
   (WinPixGpuCapturer.dll's CaptureNextFrame, which pix3.h's PIXGpuCaptureNextFrames wraps) for
   `--frames` frames. pixtool launches it, which loads the capturer:

       pixtool launch <exe> --command-line="<player args> --hold n:..,viewWidthM:..,load:..,aa:..
           --pix-capture <dir>\\capture.wpix --pix-frames 3" programmatic-capture

   pixtool's --command-line must be quoted on the raw command line and can't contain '=' (it
   splits the option at the last '='), hence the colons in --hold.
2. PIX replays the capture to time each event, and its event list goes to CSV:

       pixtool open-capture <dir>\\capture.wpix save-event-list <dir>\\events.csv
           --counters="*Duration*"

   This replay needs Windows' Developer Mode on the analysis machine (an administrator setting:
   E_PIX_FEATURE_REQUIRES_DEVELOPER_MODE otherwise).
3. Only the per-pass summary is kept (passes.md and passes.json); the capture and the event list
   stay in the gitignored folder.
"""

from __future__ import annotations

import csv
import json
import re
import subprocess
from collections import defaultdict
from pathlib import Path

from . import paths, sweep

PIXTOOL = Path("C:/Program Files/Microsoft PIX/2603.25/pixtool.exe")


def capture_command(exe: Path, spec: Path, out: Path, point: str, frames: int) -> str:
    """The raw Windows command line for step 1."""
    if "=" in point:
        point = point.replace("=", ":")
    player = " ".join(sweep.player_args(spec, out, (1920, 1080), 6, [])) + \
        f" --hold {point} --pix-capture {out / 'capture.wpix'} --pix-frames {frames}"
    return f'"{PIXTOOL}" launch "{exe}" --command-line="{player}" programmatic-capture'


def event_list_command(out: Path) -> str:
    return (f'"{PIXTOOL}" open-capture "{out / "capture.wpix"}" save-event-list "{out / "events.csv"}" '
            f'--counters="*Duration*"')


def summarize(events_csv: Path, frames: int) -> list[dict]:
    """
    Per-pass GPU time from PIX's event list: the duration of every top-level marker region (the
    shallowest depth present), summed per name and divided by the captured frames. Durations in
    ns or µs columns are converted to ms.
    """
    with open(events_csv, newline="", encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    if not rows:
        return []
    cols = rows[0].keys()
    dur_col = next((c for c in cols if re.search(r"TOP to EOP Duration", c, re.IGNORECASE)), None) \
        or next((c for c in cols if re.search(r"duration", c, re.IGNORECASE)), None)
    if dur_col is None:
        raise ValueError(f"events.csv has no duration column ({', '.join(cols)})")
    scale = 1e-6 if "ns" in dur_col else 1e-3 if ("us" in dur_col or "µs" in dur_col) else 1.0
    depth_col = next((c for c in cols if re.search(r"depth|level", c, re.IGNORECASE)), None)
    if depth_col:
        depths = [int(r[depth_col]) for r in rows if (r.get(depth_col) or "").strip().lstrip("-").isdigit()]
        top = min(depths) if depths else 0
        rows = [r for r in rows if (r.get(depth_col) or "").strip() == str(top)]
    totals: dict[str, float] = defaultdict(float)
    for r in rows:
        try:
            totals[r.get("Name", "?")] += float((r.get(dur_col) or "0").replace(",", "")) * scale
        except ValueError:
            continue
    out = [{"pass": k, "msPerFrame": round(v / max(1, frames), 4)} for k, v in totals.items() if v > 0]
    return sorted(out, key=lambda p: -p["msPerFrame"])


def cmd_pix(a) -> int:
    wt = paths.worktree()
    out = Path(a.out).resolve()
    out.mkdir(parents=True, exist_ok=True)
    exe = Path(a.player).resolve() if a.player else sweep.ensure_player(wt, a.backend, False)
    cmd = capture_command(exe, Path(a.spec).resolve(), out, a.point, a.frames)
    print(f"pix: {cmd}", flush=True)
    code = subprocess.run(cmd, check=False).returncode  # a string: passed verbatim to CreateProcess
    capture = out / "capture.wpix"
    if not capture.exists():
        print(f"pix: no capture (pixtool exited {code}; see {out / 'bench.log'})")
        return 1
    print(f"pix: captured {capture} ({capture.stat().st_size / 2**20:.0f} MiB)")
    cmd = event_list_command(out)
    print(f"pix: {cmd}", flush=True)
    res = subprocess.run(cmd, check=False, capture_output=True, text=True)
    print(res.stdout[-2000:])
    events = out / "events.csv"
    if not events.exists():
        if "DEVELOPER_MODE" in res.stdout + res.stderr:
            print("pix: PIX's replay needs Developer Mode (an administrator setting); the capture is kept "
                  "for the operator to summarize")
        return 2
    passes = summarize(events, a.frames)
    (out / "passes.json").write_text(json.dumps(passes, indent=2) + "\n", encoding="utf-8")
    lines = ["| pass | GPU ms per frame |", "|---|---|"] + [f"| {p['pass']} | {p['msPerFrame']:.3f} |" for p in passes]
    (out / "passes.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    return 0
