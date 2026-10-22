"""Command-line entry point: `ps-analysis <command> ...`.

Commands (each prints a JSON summary on stdout):

- `crop SOURCE (--start S --end E | --segment S:E ...) --out FILE.flac
  [--info crop.json]`
- `analyze AUDIO --song ID --crop-info crop.json --source URL --out draft.json
  --analysis analysis.json`
- `report SONGMAP --out DIR [--analysis analysis.json] [--seconds 8]`
- `edit SONGMAP EDITS.json --out OUT.json [--analysis analysis.json]`
- `check SONGMAP` (exit status 1 on failure; human-readable lines on stderr)
- `click SONGMAP --out click.wav`

Commands that need the excerpt find it as `public/audio/<audio.file>` in a
parent directory of the song map or of the working directory, unless
`--audio` is given.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from . import songmap as sm


def _emit(obj: Any) -> None:
    print(json.dumps(obj, indent=1))


def resolve_audio(map_path: str | Path, m: sm.SongMap, override: str | None) -> Path:
    if override:
        return Path(override)
    name = m["audio"]["file"]
    starts = [Path(map_path).resolve().parent, Path.cwd().resolve()]
    for start in starts:
        for d in [start, *start.parents]:
            p = d / "public" / "audio" / name
            if p.exists():
                return p
    raise SystemExit(
        f"cannot find public/audio/{name} above {map_path} or the working directory; use --audio"
    )


def _load_trackers(path: str | None) -> dict[str, list[float]]:
    if not path:
        return {}
    with open(path) as f:
        return json.load(f)["trackers"]


def _segment(text: str) -> tuple[float, float]:
    start, end = text.split(":")
    return float(start), float(end)


def cmd_crop(a: argparse.Namespace) -> int:
    from .crop import crop, sha256

    segments = [_segment(s) for s in a.segment] if a.segment else None
    if (segments is None) == (a.start is None or a.end is None):
        print("crop: give --start and --end, or one or more --segment", file=sys.stderr)
        return 2
    info = crop(
        a.source,
        a.out,
        a.start,
        a.end,
        mono=not a.stereo,
        segments=segments,
        crossfade_ms=a.crossfade_ms,
        fade_in_ms=a.fade_in_ms,
    )
    info["source"] = a.source_url or str(a.source)
    info["sourceSha256"] = sha256(a.source)
    if a.info:
        Path(a.info).write_text(json.dumps(info, indent=1) + "\n")
    _emit(info)
    return 0


def cmd_analyze(a: argparse.Namespace) -> int:
    from . import analyze as an
    from . import audio as au

    y, sr = au.load_mono(a.audio)
    crop_info = json.loads(Path(a.crop_info).read_text()) if a.crop_info else {}
    audio_info = {
        "file": Path(a.audio).name,
        "source": a.source or crop_info.get("source", ""),
        "cropStart": crop_info.get("cropStart", 0.0),
        "cropEnd": crop_info.get("cropEnd", len(y) / sr),
        "sampleRate": sr,
        "duration": round(len(y) / sr, 6),
    }
    if "segments" in crop_info:
        audio_info["segments"] = crop_info["segments"]
    onsets = au.onset_features(y, sr)
    trackers = an.run_trackers(y, sr, (a.fast_min, a.fast_max))
    cands = au.accent_candidates(y, sr, onsets)
    m = an.draft_map(y, sr, trackers, onsets, cands, audio_info, a.song)
    sm.save(m, a.out)
    analysis = {"trackers": trackers, "accentCandidates": cands}
    Path(a.analysis).parent.mkdir(parents=True, exist_ok=True)
    Path(a.analysis).write_text(json.dumps(analysis))
    _emit(
        {
            "draft": a.out,
            "analysis": a.analysis,
            "beats": len(m["beats"]),
            "trackers": {
                k: len(v) for k, v in trackers.items() if not k.startswith("_")
            },
            "accentCandidates": len(cands),
        }
    )
    return 0


def cmd_report(a: argparse.Namespace) -> int:
    from . import audio as au
    from .report import write_report

    m = sm.load(a.songmap)
    y, sr = au.load_mono(resolve_audio(a.songmap, m, a.audio))
    onsets = au.onset_features(y, sr)
    trackers = _load_trackers(a.analysis)
    rep = write_report(
        m,
        y,
        sr,
        onsets,
        trackers,
        Path(a.out),
        a.seconds,
        a.per_image,
        a.t_from,
        a.t_to,
        not a.no_images,
    )
    _emit(
        {
            "out": a.out,
            "ok": rep["check"]["ok"],
            "errors": len(rep["check"]["errors"]),
            "disagreements": rep["disagreements"],
            "strips": len(rep["strips"]),
        }
    )
    return 0


def cmd_edit(a: argparse.Namespace) -> int:
    from . import audio as au
    from .edit import Context, EditError, apply_edits

    m = sm.load(a.songmap)
    edits = json.loads(Path(a.edits).read_text())
    y, sr = au.load_mono(resolve_audio(a.songmap, m, a.audio))
    onsets = au.onset_features(y, sr)
    ctx = Context(onsets=onsets, trackers=_load_trackers(a.analysis))
    try:
        out, beats_changed = apply_edits(m, edits, ctx)
    except EditError as err:
        print(f"edit failed: {err}", file=sys.stderr)
        _emit({"ok": False, "error": str(err)})
        return 1
    if beats_changed or len(out["energy"]) != len(out["beats"]):
        out["energy"] = au.beat_energy(y, sr, out["beats"], onsets)
    sm.save(out, a.out)
    idx = sm.index_song(out)
    _emit(
        {
            "ok": True,
            "out": a.out,
            "edits": len(edits),
            "beats": len(out["beats"]),
            "bars": idx.bar_count,
            "sections": [[s["label"], s["bars"]] for s in out["sections"]],
        }
    )
    return 0


def cmd_check(a: argparse.Namespace) -> int:
    from . import audio as au
    from . import check as ck

    m = sm.load(a.songmap)
    path = resolve_audio(a.songmap, m, a.audio)
    y, sr = au.load_mono(path)
    duration = len(y) / sr
    onsets = au.onset_features(y, sr)
    res = ck.check_map(m, onsets.peak_times, duration)
    audio = m.get("audio", {})
    if audio.get("sampleRate") not in (None, sr):
        res.errors.append(
            f"audio.sampleRate {audio.get('sampleRate')} but the file is {sr} Hz"
        )
    if audio.get("duration") is not None and abs(audio["duration"] - duration) > 0.001:
        res.errors.append(
            f"audio.duration {audio['duration']} but the file lasts {duration:.6f} s"
        )
    if a.format in ("json", "both"):
        _emit(res.to_json())
    if a.format in ("text", "both"):
        sys.stdout.flush()
        print(ck.format_text(res), file=sys.stderr)
    return 0 if res.ok else 1


def cmd_click(a: argparse.Namespace) -> int:
    import soundfile as sf

    from . import audio as au
    from .click import render_click

    m = sm.load(a.songmap)
    y, sr = au.load_mono(resolve_audio(a.songmap, m, a.audio))
    out = render_click(m, y, sr)
    Path(a.out).parent.mkdir(parents=True, exist_ok=True)
    sf.write(a.out, out, sr)
    _emit({"out": a.out, "beats": len(m["beats"]), "duration": round(len(out) / sr, 3)})
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="ps-analysis", description=__doc__.split("\n")[0])
    sub = p.add_subparsers(dest="command", required=True)

    c = sub.add_parser("crop", help="cut the excerpt and write FLAC")
    c.add_argument("source")
    c.add_argument("--start", type=float)
    c.add_argument("--end", type=float)
    c.add_argument(
        "--segment",
        action="append",
        metavar="S:E",
        help="a source range to keep; repeat to join several, skipping what lies between",
    )
    c.add_argument(
        "--fade-in-ms",
        type=float,
        default=5.0,
        help="fade-in at the start of the excerpt (default 5)",
    )
    c.add_argument(
        "--crossfade-ms",
        type=float,
        default=30.0,
        help="crossfade at each join (default 30)",
    )
    c.add_argument("--out", required=True)
    c.add_argument(
        "--stereo", action="store_true", help="keep all channels (default: mix to mono)"
    )
    c.add_argument("--source-url", help="URL to record as the source")
    c.add_argument("--info", help="also write the crop info JSON here")
    c.set_defaults(func=cmd_crop)

    c = sub.add_parser("analyze", help="run beat trackers and write a draft song map")
    c.add_argument("audio")
    c.add_argument("--song", required=True)
    c.add_argument("--crop-info")
    c.add_argument("--source")
    c.add_argument("--out", required=True)
    c.add_argument("--analysis", required=True, help="where to write tracker output")
    c.add_argument("--fast-min", type=float, default=150)
    c.add_argument("--fast-max", type=float, default=240)
    c.set_defaults(func=cmd_analyze)

    def common(c: argparse.ArgumentParser) -> None:
        c.add_argument("songmap")
        c.add_argument(
            "--audio", help="excerpt audio (default: public/audio/<audio.file>)"
        )

    c = sub.add_parser("report", help="write diagnostics for a song map")
    common(c)
    c.add_argument("--out", required=True)
    c.add_argument("--analysis")
    c.add_argument("--seconds", type=float, default=8.0, help="strip length")
    c.add_argument("--per-image", type=int, default=1, help="strips per PNG")
    c.add_argument("--from", dest="t_from", type=float)
    c.add_argument("--to", dest="t_to", type=float)
    c.add_argument("--no-images", action="store_true")
    c.set_defaults(func=cmd_report)

    c = sub.add_parser("edit", help="apply an edit list")
    common(c)
    c.add_argument("edits")
    c.add_argument("--out", required=True)
    c.add_argument("--analysis")
    c.set_defaults(func=cmd_edit)

    c = sub.add_parser("check", help="validate a song map; exit 1 on failure")
    common(c)
    c.add_argument("--format", choices=["json", "text", "both"], default="both")
    c.set_defaults(func=cmd_check)

    c = sub.add_parser("click", help="render a click track")
    common(c)
    c.add_argument("--out", required=True)
    c.set_defaults(func=cmd_click)
    return p


def main(argv: list[str] | None = None) -> None:
    args = build_parser().parse_args(argv)
    sys.exit(args.func(args))
