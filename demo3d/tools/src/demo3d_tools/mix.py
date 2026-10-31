"""`demo3d mix`: the video capture's offline mix (choreography.md, Time and Synchronization).

Renders the song plus every SoundEvent in the timeline at its song time, then muxes the result
with a fixed-time video capture. The events are the same aggregated list the live demo plays
(the player's `--dump-sounds`), so the mix matches the demo.

    demo3d mix --events sounds.json --video captures/demo.mp4 --out captures/demo-mixed.mp4
               [--song MP3] [--start S] [--seconds N] [--sounds-dir demo3d/Assets/Vendor]

The song comes from the machine's roots.json unless --song names it. Audio is decoded with
ffmpeg to 48 kHz stereo float. Effects are mixed at their event gain (and pitch, a playback rate,
when an event has one: the crystal shatter's) under the music, and a soft limiter keeps dense beats
from clipping; the summary line gives the peak before it and the share of samples it touched. Nothing here is committed: the inputs are the NCS song and
vendor sounds, and the output goes to the gitignored captures/.

A trailer's dump (trailer.md, "Capture") also carries its fades, applied as the live player does:
an event with `fadeAt` fades out linearly over `cutFadeS` from that cut, and from `fadeOut.at` the
music and every sound fade together over `fadeOut.seconds`. Its `start` is the video's first
frame's song time, the default for --start.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from dataclasses import dataclass
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


def repitch(clip, pitch: float):
    """A clip played at `pitch` times its rate (shorter and higher above 1), by linear interpolation."""
    import numpy as np

    if abs(pitch - 1.0) < 1e-4:
        return clip
    n = max(1, round(len(clip) / pitch))
    src = np.arange(n) * pitch
    idx = np.arange(len(clip))
    return np.stack([np.interp(src, idx, clip[:, c]) for c in range(clip.shape[1])], axis=1).astype(np.float32)


def fade(times, at: float, seconds: float):
    """A linear fade's gain at song `times`: 1 until `at`, then down to 0 over `seconds`."""
    import numpy as np

    if seconds <= 0:
        return (times <= at).astype(np.float32)
    return np.clip(1 - (times - at) / seconds, 0, 1).astype(np.float32)


def render(events: list[dict], song, start: float, clips: dict[str, object], limit: bool = True,
           cut_fade_s: float = 0.0, fade_out: dict | None = None):
    """The music plus every event from `start`, at the events' song times (unlimited if not `limit`),
    with a trailer's cut fades (an event's `fadeAt`, over `cut_fade_s`) and end fade (`fade_out`)."""
    import numpy as np

    out = song * MUSIC_LEVEL
    n = len(out)
    pitched: dict[tuple[str, float], object] = {}
    for e in events:
        clip = clips.get(e["clip"])
        if clip is None:
            continue
        pitch = round(float(e.get("pitch", 1.0)), 4)
        if pitch != 1.0:
            key = (e["clip"], pitch)
            if key not in pitched:
                pitched[key] = repitch(clip, pitch)
            clip = pitched[key]
        at = round((e["time"] - start) * RATE)
        if at >= n or at + len(clip) <= 0:
            continue
        a0, c0 = max(0, at), max(0, -at)
        length = min(n - a0, len(clip) - c0)
        part = clip[c0:c0 + length] * (EFFECTS_LEVEL * float(e["gain"]))
        if "fadeAt" in e:
            part *= fade(start + (a0 + np.arange(length)) / RATE, float(e["fadeAt"]), cut_fade_s)[:, None]
        out[a0:a0 + length] += part
    if fade_out:
        out *= fade(start + np.arange(n) / RATE, float(fade_out["at"]), float(fade_out["seconds"]))[:, None]
    return soft_limit(out) if limit else out


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


@dataclass
class Mix:
    """A finished mix: the dump, the clips found for it, the song window and the mix before and after the limiter."""
    dump: dict
    clips: dict
    start: float
    song: object
    raw: object
    mixed: object


def mix_video(events: Path, video: Path, out: Path, song: Path | None = None, start: float | None = None,
              seconds: float | None = None, sounds_dir: Path = Path("demo3d/Assets/Vendor")) -> Mix:
    """Mixes the song window under a `--dump-sounds` file's events and muxes it into `video` as `out`."""
    from . import paths

    dump = json.loads(events.read_text(encoding="utf-8"))
    sounds = dump["sounds"]
    start = start if start is not None else float(dump.get("start", 0.0))
    song_path = song or Path(paths.load_roots()["song"])
    names = {e["clip"] for e in sounds if e["clip"]}
    files = find_clips(names, sounds_dir)
    missing = sorted(names - set(files))
    if missing:
        print(f"mix: no file for {len(missing)} clips: {', '.join(missing[:5])}", file=sys.stderr)
    clips = {name: decode(path) for name, path in files.items()}
    seconds = seconds if seconds is not None else video_seconds(str(video))
    window = decode(song_path, start, seconds)
    raw = render(sounds, window, start, clips, limit=False, cut_fade_s=float(dump.get("cutFadeS", 0.0)),
                 fade_out=dump.get("fadeOut"))
    mixed = soft_limit(raw)
    with tempfile.TemporaryDirectory() as tmp:
        wav = Path(tmp) / "mix.wav"
        write_wav(wav, mixed)
        subprocess.run(["ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                        "-i", str(video), "-i", str(wav), "-map", "0:v", "-map", "1:a",
                        "-c:v", "copy", "-c:a", "aac", "-b:a", "256k", "-shortest",
                        "-metadata", "comment=ps-render offline mix", str(out)], check=True)
    import numpy as np

    used = sum(1 for e in sounds if e["clip"] in clips and start <= e["time"] <= start + len(window) / RATE)
    peak = float(np.abs(raw).max()) if len(raw) else 0.0
    limited = float((np.abs(raw) > 0.8).mean()) if len(raw) else 0.0
    print(f"mix: {used} sound events over {len(window) / RATE:.1f} s of the song -> {out}; "
          f"peak {peak:.2f} before the limiter, {limited:.2%} of samples over its knee, out peak {float(np.abs(mixed).max()):.3f}")
    return Mix(dump, clips, start, window, raw, mixed)


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser(prog="demo3d mix")
    p.add_argument("--events", required=True, help="the player's --dump-sounds file")
    p.add_argument("--video", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--song", help="the Action MP3 (default: the machine's roots.json)")
    p.add_argument("--start", type=float, help="the video's first frame's song time (default: the dump's start, else 0)")
    p.add_argument("--seconds", type=float, help="the video's length (default: all of it)")
    p.add_argument("--sounds-dir", default="demo3d/Assets/Vendor")
    a = p.parse_args(argv)
    mix_video(Path(a.events), Path(a.video), Path(a.out), Path(a.song) if a.song else None, a.start, a.seconds,
              Path(a.sounds_dir))
    return 0
