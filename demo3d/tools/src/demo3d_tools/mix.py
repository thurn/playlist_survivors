"""`demo3d mix`: the video capture's offline mix (choreography.md, Time and Synchronization).

Renders the song plus every SoundEvent in the timeline at its song time, then muxes the result
with a fixed-time video capture. The events are the same aggregated list the live demo plays
(the player's `--dump-sounds`), so the mix matches the demo.

    demo3d mix --events sounds.json --video captures/demo.mp4 --out captures/demo-mixed.mp4
               [--song MP3] [--start S] [--seconds N] [--sounds-dir demo3d/Assets/Vendor]

The song comes from the machine's roots.json unless --song names it. Audio is decoded with
ffmpeg to 48 kHz stereo float. Effects are mixed at their event gain under the music, and a soft
limiter keeps dense beats from clipping. Nothing here is committed: the inputs are the NCS song and
vendor sounds, and the output goes to the gitignored captures/.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

RATE = 48000
EFFECTS_LEVEL = 0.45   # effects under the music
MUSIC_LEVEL = 0.8
CEILING = 0.95         # about -0.4 dBFS


def decode(path: Path, start: float = 0.0, seconds: float | None = None):
    """A file's audio as float32 (frames, 2) at 48 kHz."""
    import numpy as np

    cmd = ["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error"]
    if start > 0:
        cmd += ["-ss", f"{start:.6f}"]
    cmd += ["-i", str(path)]
    if seconds is not None:
        cmd += ["-t", f"{seconds:.6f}"]
    cmd += ["-f", "f32le", "-ac", "2", "-ar", str(RATE), "-"]
    raw = subprocess.run(cmd, check=True, capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def find_clips(names: set[str], sounds_dir: Path) -> dict[str, Path]:
    found: dict[str, Path] = {}
    for p in sounds_dir.rglob("*"):
        if p.suffix.lower() in (".wav", ".mp3", ".ogg") and p.stem in names:
            found.setdefault(p.stem, p)
    return found


def soft_limit(x):
    """Unity below 0.8, a smooth knee above it, never past the ceiling."""
    import numpy as np

    knee = 0.8
    y = x.copy()
    over = np.abs(x) > knee
    y[over] = np.sign(x[over]) * (knee + (CEILING - knee) * np.tanh((np.abs(x[over]) - knee) / (CEILING - knee)))
    return y


def render(events: list[dict], song, start: float, clips: dict[str, object]):
    """The music plus every event from `start`, at the events' song times."""

    out = song * MUSIC_LEVEL
    n = len(out)
    for e in events:
        clip = clips.get(e["clip"])
        if clip is None:
            continue
        at = round((e["time"] - start) * RATE)
        if at >= n or at + len(clip) <= 0:
            continue
        a0, c0 = max(0, at), max(0, -at)
        length = min(n - a0, len(clip) - c0)
        out[a0:a0 + length] += clip[c0:c0 + length] * (EFFECTS_LEVEL * float(e["gain"]))
    return soft_limit(out)


def write_wav(path: Path, samples) -> None:
    import numpy as np

    pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2")
    import wave

    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def video_seconds(path: str) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", path],
                         check=True, capture_output=True, text=True).stdout
    return float(out.strip())


def main(argv: list[str]) -> int:
    from . import paths

    p = argparse.ArgumentParser(prog="demo3d mix")
    p.add_argument("--events", required=True, help="the player's --dump-sounds file")
    p.add_argument("--video", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--song", help="the Action MP3 (default: the machine's roots.json)")
    p.add_argument("--start", type=float, default=0.0, help="the video's first frame's song time")
    p.add_argument("--seconds", type=float, help="the video's length (default: all of it)")
    p.add_argument("--sounds-dir", default="demo3d/Assets/Vendor")
    a = p.parse_args(argv)

    events = json.loads(Path(a.events).read_text(encoding="utf-8"))["sounds"]
    song_path = Path(a.song or paths.load_roots()["song"])
    names = {e["clip"] for e in events if e["clip"]}
    files = find_clips(names, Path(a.sounds_dir))
    missing = sorted(names - set(files))
    if missing:
        print(f"mix: no file for {len(missing)} clips: {', '.join(missing[:5])}", file=sys.stderr)
    clips = {name: decode(path) for name, path in files.items()}
    seconds = a.seconds if a.seconds is not None else video_seconds(a.video)
    song = decode(song_path, a.start, seconds)
    mixed = render(events, song, a.start, clips)
    with tempfile.TemporaryDirectory() as tmp:
        wav = Path(tmp) / "mix.wav"
        write_wav(wav, mixed)
        subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                        "-i", a.video, "-i", str(wav), "-map", "0:v", "-map", "1:a",
                        "-c:v", "copy", "-c:a", "aac", "-b:a", "256k", "-shortest",
                        "-metadata", "comment=ps-render offline mix", a.out], check=True)
    used = sum(1 for e in events if e["clip"] in clips and a.start <= e["time"] <= a.start + len(song) / RATE)
    print(f"mix: {used} sound events over {len(song) / RATE:.1f} s of the song -> {a.out}")
    return 0
