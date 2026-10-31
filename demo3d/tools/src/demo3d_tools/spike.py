"""`game spike NAME --file F`: the sync spike's measured run (docs/design/unity-game.md, Build Order 1).

It runs the game's player windowless (batch mode, `--headless`: the output silenced after the
in-process capture) on a File source, with a click on every committed beat, and keeps Unity's
final mix and the run's clicks, audio buffers and frames in the gitignored
captures/game/spike/NAME/. Then it measures, from that mix alone:

- **music to click**: where the music is when each click sounds. The music is found in the mix by
  cross-correlating it with the source decoded as the plugin decodes it (afconvert, at the output
  rate), window by window, so a stall or a drift would show as a moving lag; the clicks are what
  is left once the aligned music is subtracted. A click's offset is its onset's song time (from
  the aligned music) less its beat's song time: positive is late.
- **beat to onset**: how far each beat lies from the music's nearest onset (a spectral-flux peak),
  for context; that is the analyzer's placement, not the sync.
- **frame to heard**: each frame's frameSong against the song heard at its light, with the audio
  thread's buffer times (a centred linear fit) as the DSP clock's truth.

A run whose player logs a managed exception fails, whatever it measured. The numbers go to
game/evidence/spike/NAME.json (never audio), or game/evidence/DIR/NAME.json with
`--evidence DIR`. The source is an audio file or an M3U playlist (`--repeat N` plays the file N
times as one, with the playlist's 2 s gaps).

`--sounds` measures the game's own sounds instead of the beat clicks: the player runs the game's
frame loop with `--sound-probe` (every sound the sim's events schedule plays a click from its first
sample) and `--sound-out`, so "music to click" is where the music is when each scheduled sound
starts against its song time. Give it events with `--player-args "--events FEED"`.
"""

from __future__ import annotations

import argparse
import json
import math
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import numpy as np

from . import paths, setup

APP_EXE = "PlaylistSurvivors.app/Contents/MacOS/Playlist Survivors"
WINDOW_S = 4.0       # music alignment windows
STEP_S = 10.0
SEARCH = 4096        # lag search either side of the anchor's lag (frames)
CLICK_THRESHOLD = 0.2
CLICK_REFRACTORY_S = 0.05
# A scheduled click this close to another merges with it in the mix: measured only when they coincide.
CLICK_COINCIDENT_S = 0.001
CLICK_APART_S = 0.06
# Fewer clicks apart than this measure too little of the mix to vouch for its timing (30 s of the
# slice has about 130).
MIN_CLICKS_APART = 20
ANALYZER_RATE = 44_100
FRAME_TOLERANCE_MS = 1.0  # frame to heard's median


def add_parser(sub: argparse._SubParsersAction) -> None:
    sp = sub.add_parser("spike", help="the sync spike: a headless File run, measured from Unity's own mix")
    sp.add_argument("name", help="captures/game/spike/NAME and game/evidence/spike/NAME.json")
    sp.add_argument("--file", required=True, help="the audio file (paths from the repository root)")
    sp.add_argument("--repeat", type=int, default=1, help="play the file N times, as a playlist")
    sp.add_argument("--seconds", type=float, default=180)
    sp.add_argument("--backend", choices=["il2cpp", "mono"], default="il2cpp")
    sp.add_argument("--build", action="store_true", help="build the player first (a no-op if it is current)")
    sp.add_argument("--player-args", default="", help="extra player flags, e.g. '--dsp-buffer 512'")
    sp.add_argument("--analyse-only", action="store_true", help="re-measure the kept capture")
    sp.add_argument("--no-evidence", action="store_true", help="print the numbers only (slow CI)")
    sp.add_argument("--evidence", default="spike", help="the evidence folder under game/evidence (default spike)")
    sp.add_argument("--sounds", action="store_true",
                    help="measure the game loop's scheduled sounds (the sound probe) instead of the beat clicks")
    sp.set_defaults(func=cmd_spike)


def cmd_spike(a: argparse.Namespace) -> int:
    if sys.platform != "darwin":
        print("spike: the game's player runs on the Mac", file=sys.stderr)
        return 2
    wt = paths.worktree(name="game")
    captures = wt.root / "captures" / "game" / "spike" / a.name
    source = (wt.root / a.file).resolve()
    if a.repeat > 1:
        source = captures / "source.m3u"
    if not a.analyse_only:
        exe = setup.player_dir(wt, "mac", a.backend) / APP_EXE
        if a.build or not exe.exists():
            from . import cli

            code = cli.cmd_build(argparse.Namespace(
                project="game", platform="mac", backend=a.backend, out=None, development=False,
                scene=None, cold=False, force=False))
            if code != 0:
                return code
        shutil.rmtree(captures, ignore_errors=True)
        captures.mkdir(parents=True)
        if a.repeat > 1:
            source.write_text("".join(f"{(wt.root / a.file).resolve()}\n" for _ in range(a.repeat)))
        out = ["--sound-out", str(captures), "--sound-probe"] if a.sounds else ["--spike-out", str(captures)]
        cmd = [str(exe), "-batchmode", "-logFile", str(captures / "player.log"), "--file", str(source),
               "--headless", "--seconds", f"{a.seconds:g}", *out, *shlex.split(a.player_args)]
        started = time.monotonic()
        code = subprocess.run(cmd, check=False, cwd=wt.root, timeout=a.seconds + 300,
                              stdout=subprocess.DEVNULL).returncode
        print(f"spike: the player exited {code} after {time.monotonic() - started:.1f} s "
              f"(log {captures / 'player.log'})")
        thrown = exceptions(captures / "player.log")
        for line in thrown[:5]:
            print(f"spike: the player threw: {line}", file=sys.stderr)
        if code != 0 or thrown or not (captures / "spike.json").exists():
            return 1
    report = json.loads((captures / "spike.json").read_text())
    mix = read_wav(captures / "mix.wav")
    summary = measure(report, mix, decode(source, report["outputRate"]))
    summary["file"] = a.file
    summary["repeat"] = a.repeat
    summary["backend"] = a.backend
    summary["player_args"] = a.player_args
    summary["sounds"] = a.sounds
    print(table(summary))
    if a.no_evidence:
        return 0 if summary["ok"] else 1
    evidence = wt.project / "evidence" / a.evidence / f"{a.name}.json"
    evidence.parent.mkdir(parents=True, exist_ok=True)
    evidence.write_text(json.dumps(summary, indent=2) + "\n")
    print(f"spike: -> {evidence.relative_to(wt.root)}")
    return 0 if summary["ok"] else 1


def exceptions(log: Path) -> list[str]:
    """The managed exceptions a player's log reports (Unity logs each as `SomeException: message`)."""
    try:
        text = log.read_text(errors="replace")
    except OSError:
        return []
    return [line.strip() for line in text.splitlines() if re.match(r"\s*[\w.]*Exception: ", line)]


def read_wav(path: Path) -> np.ndarray:
    """A float32 WAV's samples as (frames, channels)."""
    data = path.read_bytes()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path}: not a WAV")
    at, channels = 12, 1
    while at + 8 <= len(data):
        tag, n = data[at:at + 4], int.from_bytes(data[at + 4:at + 8], "little")
        body = data[at + 8:at + 8 + n]
        if tag == b"fmt ":
            fmt, channels, bits = (int.from_bytes(body[0:2], "little"), int.from_bytes(body[2:4], "little"),
                                   int.from_bytes(body[14:16], "little"))
            if fmt == 0xFFFE:  # WAVE_FORMAT_EXTENSIBLE: the sub-format GUID starts with the code
                fmt = int.from_bytes(body[24:26], "little")
            if fmt != 3 or bits != 32:
                raise ValueError(f"{path}: not 32-bit float")
        elif tag == b"data":
            return np.frombuffer(body, dtype="<f4").reshape(-1, channels)
        at += 8 + n + (n & 1)
    raise ValueError(f"{path}: no data chunk")


def tracks(source: Path) -> list[tuple[Path, float]]:
    """A source's tracks and the silence before each, as native's `stream::playlist` reads them."""
    if source.suffix.lower() not in (".m3u", ".m3u8"):
        return [(source, 0.0)]
    out: list[tuple[Path, float]] = []
    gap = None
    for line in source.read_text().lstrip("\ufeff").splitlines():
        line = line.strip()
        if line.startswith("#PS-GAP:"):
            gap = float(line[len("#PS-GAP:"):])
        elif line and not line.startswith("#"):
            out.append((source.parent / line, gap if gap is not None else (0.0 if not out else 2.0)))
            gap = None
    return out


def afconvert(path: Path, rate: float, channels: int) -> np.ndarray:
    with tempfile.TemporaryDirectory() as tmp:
        wav = Path(tmp) / "decoded.wav"
        subprocess.run(["/usr/bin/afconvert", "-f", "WAVE", "-d", f"LEF32@{rate:g}", "-c", str(channels),
                        str(path), str(wav)], check=True)
        return read_wav(wav).copy()


def decode(source: Path, rate: int) -> np.ndarray:
    """The source as the plugin plays it (crates/plugin's session): each track's afconvert stereo
    decode at the output rate, placed where the analyzer's 44.1 kHz timeline puts it."""
    def frame(n: int) -> int:  # the output frame at analyzer sample n, rounded as Rust's f64::round
        return math.floor(n * rate / ANALYZER_RATE + 0.5)

    parts, total, cache = [], 0, {}
    for path, gap in tracks(source):
        if path not in cache:
            cache[path] = (len(afconvert(path, ANALYZER_RATE, 1)), afconvert(path, rate, 2))
        mono_len, stereo = cache[path]
        start = total + math.floor(gap * ANALYZER_RATE + 0.5)
        total = start + mono_len
        parts.append((frame(start), stereo[:frame(total) - frame(start)]))
    out = np.zeros((frame(total), 2), dtype=np.float32)
    for at, stereo in parts:
        out[at:at + len(stereo)] = stereo
    return out


def _stats(xs: np.ndarray) -> dict:
    if len(xs) == 0:
        return {"n": 0}
    return {"n": len(xs), "median": float(np.median(xs)), "mean": float(np.mean(xs)),
            "std": float(np.std(xs)), "p5": float(np.percentile(xs, 5)), "p95": float(np.percentile(xs, 95)),
            "min": float(np.min(xs)), "max": float(np.max(xs))}


def best_lag(mix: np.ndarray, ref: np.ndarray, start: int, length: int, guess: int, search: int) -> int | None:
    """The lag L (mix[n] = ref[n - L]) best matching mix[start:start+length], within guess ± search."""
    seg = mix[start:start + length]
    lo = start - guess - search
    if lo < 0 or lo + length + 2 * search > len(ref) or len(seg) < length:
        return None
    win = ref[lo:lo + length + 2 * search]
    n = 1 << math.ceil(math.log2(len(win) + length))
    corr = np.fft.irfft(np.fft.rfft(win, n) * np.conj(np.fft.rfft(seg, n)), n)[:2 * search + 1]
    if not np.any(seg):
        return None
    k = int(np.argmax(corr))  # ref[lo + k + i] ~ seg[i]: lag = start - lo - k
    return start - lo - k


def onset_peaks(ref: np.ndarray, rate: int, hop: int = 128) -> np.ndarray:
    """Spectral-flux onset times (s) of a mono signal: local maxima above a running median."""
    frame = 1024
    frames = (len(ref) - frame) // hop
    if frames <= 2:
        return np.zeros(0)
    idx = np.arange(frame)[None, :] + hop * np.arange(frames)[:, None]
    spec = np.abs(np.fft.rfft(ref[idx] * np.hanning(frame), axis=1))
    flux = np.maximum(0, np.diff(np.log1p(100 * spec), axis=0)).sum(axis=1)
    k = 15
    pad = np.pad(flux, k, mode="edge")
    med = np.array([np.median(pad[i:i + 2 * k + 1]) for i in range(len(flux))])
    peak = (flux > 1.5 * med + 1e-9) & (flux >= np.roll(flux, 1)) & (flux >= np.roll(flux, -1))
    return (np.nonzero(peak)[0] + 1) * hop / rate + frame / 2 / rate


def measure(report: dict, mix: np.ndarray, ref: np.ndarray) -> dict:
    """The spike's numbers from its report (spike.json), Unity's mix and the decoded source."""
    rate = report["outputRate"]
    mono = mix.mean(axis=1)
    refm = ref.mean(axis=1)
    start_dsp = report["captureStartDsp"]
    song_at_dsp = lambda d: report["anchorSong"] + (d - report["anchorDsp"])
    # The lag the anchor claims: mix index of song time 0.
    guess = round((report["anchorDsp"] - report["anchorSong"] - start_dsp) * rate)

    # The music in the mix, window by window.
    window, step = int(WINDOW_S * rate), int(STEP_S * rate)
    lags: list[tuple[int, int]] = []
    for s in range(max(0, guess), len(mono) - window, step):
        lag = best_lag(mono, refm, s, window, guess, SEARCH)
        if lag is not None:
            lags.append((s, lag))
    if not lags:
        return {"ok": False, "error": "the music wasn't found in the mix"}
    lag_values = np.array([lag for _, lag in lags])
    lag = int(np.median(lag_values))

    # The clicks: what is left once the aligned music is gone (its gain fitted, nominally 1).
    music = np.zeros_like(mono)
    lo, hi = max(0, lag), min(len(mono), lag + len(refm))
    music[lo:hi] = refm[lo - lag:hi - lag]
    gain = float(np.dot(mono[lo:hi], music[lo:hi]) / max(1e-12, np.dot(music[lo:hi], music[lo:hi])))
    residual = mono - gain * music
    loud = np.nonzero(np.abs(residual) > CLICK_THRESHOLD)[0]
    onsets: list[int] = []
    for i in loud:
        if not onsets or i - onsets[-1] > CLICK_REFRACTORY_S * rate:
            onsets.append(int(i))
    onset = np.array(onsets, dtype=float)

    # The scheduled clicks the capture holds whole (a run's last may be cut off as it quits), apart
    # from the rest: two clicks a few ms apart are one onset in the mix, which can't say which it is.
    # Coincident ones share their onset, so they still count.
    held = [(s, d) for s, d in zip(report["clickSong"], report["clickDsp"])
            if not math.isnan(d) and (d - start_dsp) * rate < len(mono) - 0.05 * rate]
    dsps = np.sort(np.array([d for _, d in held]))

    def apart(d: float) -> bool:
        near = np.abs(dsps - d)
        return not np.any((near > CLICK_COINCIDENT_S) & (near < CLICK_APART_S))

    scheduled = [(s, d) for s, d in held if apart(d)]
    beats = np.array([s for s, _ in scheduled])
    # Each click's onset: the residual's onset nearest where its schedule puts it.
    expect = (np.array([d for _, d in scheduled]) - start_dsp) * rate
    offsets, schedule, found_beats = [], [], []
    for b, e in zip(beats, expect):
        if len(onset) == 0:
            break
        j = int(np.argmin(np.abs(onset - e)))
        if abs(onset[j] - e) > 0.02 * rate:
            continue
        song = (onset[j] - lag) / rate  # where the music is at the click
        offsets.append((song - b) * 1000)
        schedule.append((onset[j] - e) / rate * 1000)
        found_beats.append(b)
    offsets_a = np.array(offsets)
    found = np.array(found_beats)
    drift = float(np.polyfit(found / 60, offsets_a, 1)[0]) if len(found) > 2 else float("nan")
    residual_rms = float(np.sqrt(np.mean(np.square(residual[lo:hi])))) if hi > lo else float("nan")

    # The beats against the music's own onsets (the analyzer's placement, for context).
    peaks = onset_peaks(refm, rate)
    to_onset = []
    for b in found:
        if len(peaks):
            k = int(np.argmin(np.abs(peaks - b)))
            if abs(peaks[k] - b) < 0.07:
                to_onset.append((b - peaks[k]) * 1000)

    # Frame to heard: the audio thread's buffer times are the DSP clock's truth.
    tick = np.array(report["bufferTick"])
    bdsp = np.array(report["bufferDsp"])
    frame_err = []
    for f in report["frames"]:
        light = f["tick"] + f["display"]
        near = np.abs(tick - light) < 1.0
        if near.sum() < 16:
            continue
        _, icept = np.polyfit(tick[near] - light, bdsp[near], 1)
        heard = song_at_dsp(icept) - report["outputLatency"]
        frame_err.append((f["frameSong"] - heard) * 1000)
    frame_songs = np.array([f["frameSong"] for f in report["frames"]])
    backwards = int(np.sum(np.diff(frame_songs) < 0)) if len(frame_songs) > 1 else 0

    offsets_stats = _stats(offsets_a)
    frame_stats = _stats(np.array(frame_err))
    # frameSong's median against the audio thread's clock: a lost half-buffer correction is 2.7 ms
    # at 256 frames, while loaded runs measure within 0.1 ms.
    ok = (report["stalls"] == 0 and report["gaps"] == 0 and not report["error"] and len(beats) >= MIN_CLICKS_APART
          and len(offsets_a) >= 0.98 * len(beats) and abs(offsets_stats.get("median", 99)) <= 2
          and max(abs(offsets_stats.get("p5", 99)), abs(offsets_stats.get("p95", 99))) <= 2 and backwards == 0
          and abs(frame_stats.get("median", 99)) <= FRAME_TOLERANCE_MS)
    return {
        "ok": bool(ok),
        "seconds": round(len(mono) / rate, 3),
        "output_rate": rate,
        "dsp_buffer": [report["dspBufferLength"], report["dspBuffers"]],
        "output_latency_ms": round(report["outputLatency"] * 1000, 3),
        "device_latency_ms": round(report["deviceLatency"] * 1000, 3),
        "dsp_buffering_ms": round(report["dspBuffering"] * 1000, 3),
        "stalls": report["stalls"],
        "capture_gaps": report["gaps"],
        "error": report["error"],
        "clicks_scheduled": len(held),
        "clicks_apart": len(beats),
        "clicks_dropped": int(sum(1 for d in report["clickDsp"] if math.isnan(d))),
        "clicks_found": len(offsets_a),
        "music_lag_frames": {"anchor": guess, "median": lag, "min": int(lag_values.min()),
                             "max": int(lag_values.max()), "windows": len(lags)},
        "music_gain": round(gain, 6),
        "residual_rms": residual_rms,
        "music_to_click_ms": offsets_stats,
        "drift_ms_per_min": drift,
        "click_to_schedule_ms": _stats(np.array(schedule)),
        "beat_to_onset_ms": _stats(np.array(to_onset)),
        "frame_to_heard_ms": frame_stats,
        "frame_song_backwards": backwards,
    }


def table(s: dict) -> str:
    if "music_to_click_ms" not in s:
        return f"spike: {s.get('error')}"
    rows = [("music to click (ms)", s["music_to_click_ms"]), ("click to schedule (ms)", s["click_to_schedule_ms"]),
            ("beat to onset (ms)", s["beat_to_onset_ms"]), ("frame to heard (ms)", s["frame_to_heard_ms"])]
    out = ["| measure | n | median | p5 | p95 | min | max |", "|---|---|---|---|---|---|---|"]
    for name, st in rows:
        if st.get("n"):
            out.append(f"| {name} | {st['n']} | {st['median']:+.3f} | {st['p5']:+.3f} | {st['p95']:+.3f} | "
                       f"{st['min']:+.3f} | {st['max']:+.3f} |")
        else:
            out.append(f"| {name} | 0 | | | | | |")
    lag = s["music_lag_frames"]
    out.append(f"\n{s['seconds']:.0f} s at {s['output_rate']} Hz, DSP buffer {s['dsp_buffer'][0]} x {s['dsp_buffer'][1]}; "
               f"clicks {s['clicks_found']}/{s['clicks_apart']} apart found, of {s['clicks_scheduled']} ({s['clicks_dropped']} dropped); "
               f"drift {s['drift_ms_per_min']:+.4f} ms/min; music lag {lag['min']}..{lag['max']} over "
               f"{lag['windows']} windows (anchor {lag['anchor']}); gain {s['music_gain']:.4f}; "
               f"stalls {s['stalls']}, capture gaps {s['capture_gaps']}; output latency "
               f"{s['output_latency_ms']:.1f} ms (device {s['device_latency_ms']:.1f} + DSP {s['dsp_buffering_ms']:.1f}); "
               f"frameSong backwards {s['frame_song_backwards']}; {'ok' if s['ok'] else 'NOT OK'}")
    return "\n".join(out)
