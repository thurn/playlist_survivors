"""`demo3d trailer --out FILE.mp4 [-- player flags]`: the trailer's finished, mixed video (trailer.md, "Capture").

    demo3d trailer --out captures/trailer.mp4 -- --palette neon --monster-style crystal

1. **Render.** The windowed player plays `--trailer` at fixed time with the flags, `--capture-video` and
   `--dump-sounds`: a silent 1920x1080 60 fps H.264 video, drawn at 2x and downsampled (CaptureTool), and the
   cues the live run plays, in song seconds, with its cut and end fades.
2. **Mix and mux.** `demo3d mix` mixes the song window under the cues, with the fades, into the video.
3. **Check.** The mix's peak, each cut's fade of the sounds ringing into it, and the end fade; a failed check
   exits 1 (the video is still written).

The files beside the output, all in the gitignored captures/: FILE.silent.mp4 (the picture), FILE.sounds.json
(the cues) and FILE.player.log.

`--stand-in` runs every step but the windowed capture, for agents, who never open a player window (mac.md): a
batch-mode player dumps the same cues, and a black video of the capture's length stands in for the picture.
"""

from __future__ import annotations

import argparse
import json
import math
import shutil
import subprocess
import sys
from pathlib import Path

from . import mix, paths, setup

FPS = 60
OWNED = ("--capture-video", "--dump-sounds")  # the command sets these itself
SILENT = 1e-4  # a sample this small is silence (-80 dBFS; the mix is float, before the encoder)


def player(wt: paths.Worktree, backend: str, app: str | None) -> Path:
    if app:
        return Path(app)
    if paths.is_windows():
        return setup.player_dir(wt, "windows", backend) / "PlaylistSurvivors3D.exe"
    from .look import APP

    return setup.player_dir(wt, "mac", backend) / APP


def player_command(exe: Path, flags: list[str], files: dict[str, Path], stand_in: bool) -> list[str]:
    """The player's command line: windowed for the capture, batch mode for the stand-in's cues."""
    cmd = [str(exe)]
    if stand_in:
        cmd.append("-batchmode")
    else:
        cmd += ["-screen-fullscreen", "0", "-screen-width", "1920", "-screen-height", "1080"]
    if not paths.is_windows():
        cmd.append("-force-metal")
    cmd += ["-logFile", str(files["log"])]
    if "--trailer" not in flags:
        cmd.append("--trailer")
    cmd += ["--fixed-time", "--dump-sounds", str(files["sounds"])]
    if not stand_in:
        ffmpeg = shutil.which("ffmpeg")
        if ffmpeg is None:
            raise SystemExit("trailer: ffmpeg is not on the PATH")
        # IL2CPP's Process.Start doesn't search the PATH (mac.md), so the player gets ffmpeg's full path.
        cmd += ["--capture-video", str(files["silent"]), "--ffmpeg", ffmpeg]
    return cmd + flags


def frames(dump: dict) -> int:
    """The capture's frame count: the fixed-time frames start + k/60 up to the first at or after the end."""
    return math.ceil((dump["end"] - dump["start"]) * FPS - 1e-6) + 1


def stand_in_video(path: Path, count: int) -> None:
    subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi",
                    "-i", f"color=c=black:s=1920x1080:r={FPS}", "-frames:v", str(count), "-c:v", "libx264",
                    "-pix_fmt", "yuv420p", "-metadata", "comment=ps-render trailer stand-in", str(path)], check=True)


def check(m: mix.Mix) -> list[str]:
    """Prints the mix's checks (trailer.md, "Capture") and returns its failures: clipping, cues with no clip,
    sound left after a cut's fade, and an end fade that isn't one."""
    import numpy as np

    problems: list[str] = []
    dump, sounds, start = m.dump, m.dump["sounds"], m.start
    n = len(m.mixed)

    def idx(t: float) -> int:
        """The sample at song time t."""
        return min(n, max(0, round((t - start) * mix.RATE)))

    peak_out, peak_raw = float(np.abs(m.mixed).max()), float(np.abs(m.raw).max())
    print(f"trailer check: peak {peak_raw:.2f} before the limiter, {peak_out:.3f} after (ceiling {mix.CEILING})")
    if peak_out > mix.CEILING + 1e-6:
        problems.append(f"the mix peaks at {peak_out:.3f}, over the limiter's ceiling {mix.CEILING}")

    names = {e["clip"] for e in sounds if e["clip"]}
    missing = sorted(names - set(m.clips))
    print(f"trailer check: {len(sounds)} cues, {len(names)} clips, {len(missing)} without a file")
    if missing:
        problems.append(f"no file for {len(missing)} clips the live run plays: {', '.join(missing[:5])}")

    cut_fade = float(dump.get("cutFadeS", 0.0))
    for cut in sorted({e["fadeAt"] for e in sounds if "fadeAt" in e}):
        # What the cues fading from this cut add to the mix: what rings into the next shot, then nothing.
        others = mix.render([e for e in sounds if e.get("fadeAt") != cut], m.song, start, m.clips, limit=False,
                            cut_fade_s=cut_fade, fade_out=dump.get("fadeOut"))
        ringing = m.raw - others
        fading = float(np.abs(ringing[idx(cut):idx(cut + cut_fade)]).max(initial=0.0))
        left = float(np.abs(ringing[idx(cut + cut_fade) + 1:]).max(initial=0.0))
        print(f"trailer check: cut at trailer {cut - dump['trailerStart']:.3f} s: "
              f"ringing {fading:.3f} at the cut, {left:.1e} after {cut_fade * 1000:.0f} ms")
        if left > SILENT:
            problems.append(f"sound from before the cut at song {cut:.3f} s is still playing {cut_fade * 1000:.0f} ms after it")

    fade_out = dump.get("fadeOut")
    if fade_out and idx(fade_out["at"] + fade_out["seconds"]) < n:
        at, secs = fade_out["at"], fade_out["seconds"]
        quarters = [float(np.sqrt(np.mean(m.mixed[idx(at + q * secs / 4):idx(at + (q + 1) * secs / 4)] ** 2)))
                    for q in range(4)]
        after = float(np.abs(m.mixed[idx(at + secs) + 1:]).max(initial=0.0))
        print(f"trailer check: end fade from song {at:.3f} s over {secs} s: RMS by quarter "
              f"{', '.join(f'{q:.3f}' for q in quarters)}; {after:.1e} after it")
        if not quarters[3] < 0.5 * quarters[0]:
            problems.append(f"the end fade's last quarter (RMS {quarters[3]:.3f}) isn't under half its first ({quarters[0]:.3f})")
        if after > SILENT:
            problems.append(f"sound after the end fade (peak {after:.1e})")
    else:
        print("trailer check: the run ends before the end fade does")
    return problems


def main(argv: list[str]) -> int:
    ours, flags = (argv[:argv.index("--")], argv[argv.index("--") + 1:]) if "--" in argv else (argv, [])
    p = argparse.ArgumentParser(prog="demo3d trailer", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out", required=True, help="the mixed video (under captures/)")
    p.add_argument("--stand-in", action="store_true",
                   help="windowless: the cues from a batch-mode player, a black video for the picture")
    p.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    p.add_argument("--app", help="another player's executable")
    p.add_argument("--song", help="the Action MP3 (default: the machine's roots.json)")
    a = p.parse_args(ours)
    for flag in OWNED:
        if flag in flags:
            p.error(f"{flag} is the command's own; leave it out of the player flags")

    wt = paths.worktree()
    exe = player(wt, a.backend, a.app)
    if not exe.exists():
        print(f"trailer: no player at {exe} (run `demo3d build`)", file=sys.stderr)
        return 2
    out = Path(a.out).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    stem = out.with_suffix("")
    files = {"silent": Path(f"{stem}.silent.mp4"), "sounds": Path(f"{stem}.sounds.json"), "log": Path(f"{stem}.player.log")}
    for f in files.values():
        f.unlink(missing_ok=True)

    cmd = player_command(exe, flags, files, a.stand_in)
    print(f"trailer: {'dumping the cues (batch mode)' if a.stand_in else 'capturing (windowed)'}: {' '.join(cmd)}", flush=True)
    code = subprocess.run(cmd, check=False, cwd=wt.project, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                          timeout=3600).returncode
    if code != 0 or not files["sounds"].exists() or not (a.stand_in or files["silent"].exists()):
        print(f"trailer: the player exited {code} without its files (see {files['log']})", file=sys.stderr)
        return 1
    dump = json.loads(files["sounds"].read_text(encoding="utf-8"))
    if "fadeOut" not in dump:
        print(f"trailer: {files['sounds']} has no trailer fades: an older player? (rebuild with `demo3d build`)",
              file=sys.stderr)
        return 1
    if a.stand_in:
        stand_in_video(files["silent"], frames(dump))
    print(f"trailer: {frames(dump)} frames from song {dump['start']:.3f} s to {dump['end']:.3f} s", flush=True)

    m = mix.mix_video(files["sounds"], files["silent"], out, Path(a.song) if a.song else None,
                      sounds_dir=wt.project / "Assets" / "Vendor")
    problems = check(m)
    for problem in problems:
        print(f"trailer: FAILED: {problem}", file=sys.stderr)
    print(f"trailer: {out}{' (a stand-in: black picture)' if a.stand_in else ''}")
    return 1 if problems else 0
