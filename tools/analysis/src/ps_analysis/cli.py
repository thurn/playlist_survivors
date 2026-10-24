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
- `stream AUDIO --out stream.json [--songmap SONGMAP | --reference REF.json |
  --offline-reference] [--write-audio FILE.flac] [--params JSON]`: the belief stream for
  reactive mode
- `synth NAME --out FILE.ogg [--reference FILE.json]`: a synthetic test track
- `encode AUDIO --out FILE.ogg [--compression 0.6]`: a track for the corpus, as
  Ogg Vorbis (the default is libvorbis quality 4)
- `fit-grid AUDIO --out reference.json [--click FILE.ogg|.wav] [--quiet]`: a
  corpus track's fitted reference (beats, meter, downbeats, approximate sections,
  accents), and a click overlay to check it by ear
- `export-models [--out native/models]`: madmom's models, librosa's matrices and golden
  data for the native analyzer
- `delayed-payoff [STREAM.json ...] [--index index.json] [--markdown OUT.md] [--json OUT.json]`:
  Delayed listening's commit rule replayed over committed streams' hindsight entries,
  against Direct listening (every `public/reactive/*/stream.json` by default)

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


def cmd_stream(a: argparse.Namespace) -> int:
    from . import stream as st

    reference = None
    if a.songmap:
        reference = st.songmap_reference(a.songmap)
    elif a.reference:
        reference = json.loads(Path(a.reference).read_text())
    params = st.Params(**json.loads(a.params)) if a.params else st.Params()
    job = st.StreamJob(
        Path(a.audio),
        Path(a.out),
        reference,
        a.offline_reference,
        Path(a.write_audio) if a.write_audio else None,
        params,
    )
    _emit(st.run(job))
    return 0


def write_vorbis(out: Path, y: Any, sr: int, compression: float) -> None:
    """Writes samples (frames, or frames x channels) as Ogg Vorbis.

    The file appears only once complete (written beside it, then renamed), so an
    interrupted encode never leaves a short file that looks finished.
    """
    import soundfile as sf

    channels = 1 if y.ndim == 1 else y.shape[1]
    out.parent.mkdir(parents=True, exist_ok=True)
    part = out.with_name(out.name + ".part")
    # libsndfile's Vorbis encoder crashes on long single writes: write in blocks.
    with sf.SoundFile(
        part,
        "w",
        sr,
        channels,
        format="OGG",
        subtype="VORBIS",
        compression_level=compression,
    ) as f:
        for i in range(0, len(y), sr):
            f.write(y[i : i + sr])
    part.replace(out)


def cmd_encode(a: argparse.Namespace) -> int:
    import soundfile as sf

    y, sr = sf.read(a.audio, dtype="float32", always_2d=False)
    out = Path(a.out)
    write_vorbis(out, y, sr, a.compression)
    _emit({"out": str(out), "seconds": round(len(y) / sr, 3), "sampleRate": sr})
    return 0


def cmd_fit_grid(a: argparse.Namespace) -> int:
    import soundfile as sf

    from . import fitgrid as fg
    from . import stream as st
    from .click import render_click_at

    y, _ = st.load_audio(a.audio)
    ref = fg.fit_reference(y)
    out = Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(ref, indent=1) + "\n")
    if a.click:
        beats = ref["beats"]
        index = {round(t, 6): i for i, t in enumerate(beats)}
        downs = {
            index[round(t, 6)] for t in ref.get("downbeats", []) if round(t, 6) in index
        }
        starts = {
            index[round(s["s0"], 6)]
            for s in ref.get("sections", [])
            if round(s["s0"], 6) in index
        }
        # (Over the file as it is, at its own rate and channels.)
        full, sr = sf.read(a.audio, dtype="float32", always_2d=False)
        click = render_click_at(full, sr, beats, downs, starts)
        path = Path(a.click)
        if path.suffix == ".ogg":
            write_vorbis(path, click, sr, 0.6)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            sf.write(path, click, sr)
    # (A held-out track's numbers are not for study: --quiet prints only whether it fitted.)
    _emit({"fit": ref["fit"]} if a.quiet else fg.summary(ref))
    return 0


def cmd_synth(a: argparse.Namespace) -> int:
    import soundfile as sf

    from . import synth

    y, reference = synth.track(a.name)
    out = Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.suffix == ".ogg":
        write_vorbis(out, y, synth.SR, a.compression)
    else:
        sf.write(out, y, synth.SR)
    if a.reference:
        Path(a.reference).write_text(json.dumps(reference, indent=1) + "\n")
    _emit(
        {
            "out": str(out),
            "duration": round(len(y) / synth.SR, 3),
            "beats": len(reference["beats"]),
        }
    )
    return 0


def cmd_export_models(a: argparse.Namespace) -> int:
    from . import models

    root = models_root()
    out = Path(a.out) if a.out else root / "native" / "models"
    _emit(models.export(root, out))
    return 0


def models_root() -> Path:
    """The repository root: the first parent of the working directory with public/reactive."""
    for d in [Path.cwd(), *Path.cwd().parents]:
        if (d / "public" / "reactive").is_dir():
            return d
    raise SystemExit("cannot find the repository root above the working directory")


def cmd_delayed_payoff(a: argparse.Namespace) -> int:
    from . import delayed as dl

    if a.streams:
        paths, index = [Path(x) for x in a.streams], Path(a.index) if a.index else None
    else:
        paths, index = dl.default_streams()
        index = Path(a.index) if a.index else index
    result = dl.run(paths, index)
    if a.json:
        Path(a.json).write_text(json.dumps(result, indent=1) + "\n")
    if a.markdown:
        Path(a.markdown).write_text(dl.markdown(result))
    _emit(
        {
            "tracks": len(result["tracks"]),
            "skipped": result["skipped"],
            "aggregate": result["aggregate"],
            "proposal": result["proposal"],
        }
    )
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

    c = sub.add_parser("stream", help="write a reactive belief stream")
    c.add_argument("audio")
    c.add_argument("--out", required=True)
    ref = c.add_mutually_exclusive_group()
    ref.add_argument(
        "--songmap", help="reference beats, sections and accents from a song map"
    )
    ref.add_argument(
        "--reference",
        help="a reference JSON, such as the exact one `synth --reference` writes",
    )
    ref.add_argument(
        "--offline-reference",
        action="store_true",
        help="reference beats from madmom's offline whole-song decode",
    )
    c.add_argument(
        "--write-audio",
        metavar="FILE.flac",
        help="write the analysed samples (44.1 kHz mono) here and play them instead",
    )
    c.add_argument(
        "--params",
        metavar="JSON",
        help="parameter overrides by field name, such as '{\"change_stats_s\": 20}'",
    )
    c.set_defaults(func=cmd_stream)

    c = sub.add_parser("synth", help="render a synthetic test track")
    c.add_argument("name")
    c.add_argument("--out", required=True, help=".ogg (Vorbis) or .wav")
    c.add_argument("--reference", help="also write the exact reference JSON here")
    c.add_argument(
        "--compression",
        type=float,
        default=0.6,
        help="Vorbis compression level, 0 (best quality) to 1 (smallest; default 0.6)",
    )
    c.set_defaults(func=cmd_synth)

    c = sub.add_parser("encode", help="encode a corpus track as Ogg Vorbis")
    c.add_argument("audio")
    c.add_argument("--out", required=True, help="the .ogg to write")
    c.add_argument(
        "--compression",
        type=float,
        default=0.6,
        help="Vorbis compression level, 0 (best quality) to 1 (smallest; default 0.6, quality 4)",
    )
    c.set_defaults(func=cmd_encode)

    c = sub.add_parser("fit-grid", help="fit a corpus track's reference grid")
    c.add_argument("audio")
    c.add_argument("--out", required=True, help="the reference JSON to write")
    c.add_argument("--click", help="also write a click overlay (.ogg or .wav)")
    c.add_argument("--quiet", action="store_true", help="print only whether it fitted")
    c.set_defaults(func=cmd_fit_grid)

    c = sub.add_parser(
        "export-models", help="export models and golden data for native/"
    )
    c.add_argument("--out", help="the directory to write (default: native/models)")
    c.set_defaults(func=cmd_export_models)

    c = sub.add_parser(
        "delayed-payoff",
        help="measure Delayed listening's payoff from committed streams",
    )
    c.add_argument(
        "streams", nargs="*", help="stream.json files (default: public/reactive)"
    )
    c.add_argument("--index", help="index.json naming each track's category")
    c.add_argument("--markdown", help="write the appendix tables here")
    c.add_argument("--json", help="write the full result here")
    c.set_defaults(func=cmd_delayed_payoff)
    return p


# Commands over the whole corpus or every model: run below Tollgate's checks (batch.py).
BATCH_COMMANDS = {"export-models", "delayed-payoff"}


def main(argv: list[str] | None = None) -> None:
    args = build_parser().parse_args(argv)
    if argv is None and args.command in BATCH_COMMANDS:
        from .batch import run_as_batch

        run_as_batch()
    sys.exit(args.func(args))
