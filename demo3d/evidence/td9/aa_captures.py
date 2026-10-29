"""TD9's anti-aliasing capture pairs: fixed-time stills, enlarged crops and 2 s videos, MSAA 4x against TAA.

`uv run --project demo3d/tools python demo3d/evidence/td9/aa_captures.py` from the worktree's root.
It holds TD7's machine lock (so no sweep runs beside it), then runs the IL2CPP player windowed once
per mode and moment: S3 at demo load, 1,200 monsters at 40 m (monster edges), and the song's drop-1
at 50 s (the hero's combos, bolts and hit flashes: the bright, fast things that TAA smears and
that stand in for TD8's spark trails until they land). Full stills and videos go to the gitignored
captures/td9/; the enlarged crops are copied to demo3d/evidence/td9/aa/.
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "demo3d" / "tools" / "src"))
from demo3d_tools import sweep  # noqa: E402

EXE = ROOT / "demo3d" / "Builds" / "il2cpp" / "PlaylistSurvivors3D.exe"
CAPTURES = ROOT / "captures" / "td9"
EVIDENCE = ROOT / "demo3d" / "evidence" / "td9" / "aa"
CROP = "800,420,320,180,3"  # the centre of the view, where the hero meets the horde, enlarged 3x

MOMENTS = {
    "s3": ["--scene", "S3", "--n", "1200", "--view-width", "40", "--load", "demo", "--start-at", "12", "--seconds", "2",
           "--stills-at", "12.5,13.5"],
    "song": ["--scene", "song", "--start-at", "50", "--seconds", "2", "--stills-at", "50.5,51.5"],
}


def main() -> int:
    CAPTURES.mkdir(parents=True, exist_ok=True)
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    with sweep.machine_lock("TD9 AA captures"):
        for moment, args in MOMENTS.items():
            for aa in ("msaa4", "taa"):
                name = f"{moment}-{aa}"
                out = CAPTURES / name
                if out.exists():
                    shutil.rmtree(out)
                out.mkdir()
                cmd = [str(EXE), "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0",
                       "-logFile", str(out / "player.log"), "--aa", aa, *args,
                       "--capture-stills", str(out), "--capture-crop", CROP,
                       "--capture-video", str(CAPTURES / f"{name}.mp4")]
                print("capture:", " ".join(cmd), flush=True)
                code = subprocess.run(cmd, cwd=ROOT / "demo3d", timeout=900).returncode
                print(f"capture: {name} exited {code}", flush=True)
                if code != 0:
                    return code
                for crop in sorted(out.glob("still-*-crop.jpg")):
                    shutil.copy2(crop, EVIDENCE / f"{name}-{crop.name.replace('still-', '')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
