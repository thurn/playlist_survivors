"""The shipped reactive tracks, and the commands that write them (ported from the TS game's
``scripts/reactive-*.ts`` when it was removed, D6).

The regression tracks (classical and synthetic, docs/design/reactive-audio.md) and the reactive
feel corpus from ``corpus/manifest.yaml`` (docs/design/reactive-feel.md, "The Corpus");
``public/reactive/index.json`` built from both; and three commands, each running this tool's own
commands in parallel:

- ``corpus-add``: for each manifest track missing its audio, encodes the source as Ogg Vorbis;
  fits each just encoded or missing its reference; streams each just fitted or missing its stream;
  rewrites the index. A second run changes nothing.
- ``streams``: regenerates every shipped track's stream (run it whenever the stream code changes,
  and bump its TOOL_VERSION), and the index.
- ``fit-corpus``: fits the corpus's reference grids (the regression tracks' fits go to
  ``captures/corpus/fits/`` for comparison only) and prints the table; held-out tracks are counted,
  never shown (docs/design/reactive-feel.md, "Held-out Protocol").
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import tempfile
import time
import zipfile
from collections import Counter
from collections.abc import Callable, Iterable
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
REACTIVE_DIR = ROOT / "public" / "reactive"
MANIFEST = ROOT / "corpus" / "manifest.yaml"
# Where the fits' click overlays go (gitignored), to check a fit by ear.
CLICK_DIR = ROOT / "captures" / "corpus"

SPLITS = ("iteration", "held-out")
LICENCES = ("CC0-1.0", "CC-BY-3.0", "CC-BY-4.0")
CORPUS_ID = re.compile(r"^(notjam|cc)-[a-z0-9][a-z0-9-]{0,56}$")


@dataclass(frozen=True)
class RegressionTrack:
    id: str
    title: str
    artist: str
    group: str
    # Listed in the index (false for check-only tracks).
    listed: bool = True
    # Stream parameter overrides (stream --params), by the tool's field names.
    params: dict[str, float] = field(default_factory=dict)

    @property
    def audio(self) -> str:
        """Its audio, relative to public/reactive/."""
        if self.group == "synthetic":
            return f"{self.id}/{self.id}.ogg"
        return f"../audio/{self.id}.flac"


REGRESSION_TRACKS = (
    RegressionTrack(
        "orpheus-galop", "Orpheus in the Underworld: Galop", "Offenbach", "classical"
    ),
    RegressionTrack(
        "william-tell-finale", "William Tell Overture: Finale", "Rossini", "classical"
    ),
    RegressionTrack(
        "mountain-king", "In the Hall of the Mountain King", "Grieg", "classical"
    ),
    RegressionTrack(
        "bumblebee", "Flight of the Bumblebee", "Rimsky-Korsakov", "classical"
    ),
    RegressionTrack(
        "beethoven-fifth", "Symphony No. 5: Allegro con brio", "Beethoven", "classical"
    ),
    RegressionTrack("house-128", "House at 128", "Synthetic", "synthetic"),
    RegressionTrack("dnb-174", "Drum and Bass at 174", "Synthetic", "synthetic"),
    RegressionTrack("hiphop-92-swing", "Swung Hip-Hop at 92", "Synthetic", "synthetic"),
    RegressionTrack(
        "accelerando-100-140", "Accelerando, 100 to 140", "Synthetic", "synthetic"
    ),
    RegressionTrack("pop-arrangement", "Pop Arrangement", "Synthetic", "synthetic"),
    # The check's short track: 20 s statistics windows (docs/design/reactive-audio.md, "Cuts"), and
    # strong onsets from the 95th percentile, since its accents (every 4 s) outnumber the top 2%.
    RegressionTrack(
        "causality-mix",
        "Causality Mix",
        "Synthetic",
        "synthetic",
        listed=False,
        params={
            "change_stats_s": 20,
            "change_threshold_s": 20,
            "end_window_s": 20,
            "strong_percentile": 95,
        },
    ),
)


@dataclass(frozen=True)
class CorpusTrack:
    id: str
    # The source file under corpus-add's --source directory; "archive.zip:path" names a file
    # inside a zip.
    source: str
    title: str
    artist: str
    licence: str
    split: str
    credit: str | None = None
    # Near-duplicates share a family, and a family has a single split.
    family: str | None = None

    @property
    def group(self) -> str:
        return "notjam" if self.id.startswith("notjam-") else "cc"

    @property
    def audio(self) -> str:
        """Its audio, relative to public/reactive/."""
        return f"{self.id}/{self.id}.ogg"

    @property
    def reference(self) -> Path:
        """Its fitted reference (fit-corpus)."""
        return REACTIVE_DIR / self.id / "reference.json"

    def reference_args(self) -> list[str]:
        """The stream command's reference: its fitted one, else madmom's offline decode."""
        if self.reference.exists():
            return ["--reference", str(self.reference)]
        return ["--offline-reference"]


class ManifestError(ValueError):
    pass


def read_manifest(path: Path = MANIFEST) -> list[CorpusTrack]:
    """The manifest's tracks, checked: ids, licences, splits, credits and one split a family."""
    import yaml

    data = yaml.safe_load(path.read_text())
    tracks: list[CorpusTrack] = []
    for i, t in enumerate((data or {}).get("tracks") or []):
        where = t.get("id", f"track {i}") if isinstance(t, dict) else f"track {i}"
        if not isinstance(t, dict):
            raise ManifestError(f"{where}: not a mapping")
        for key in ("id", "source", "title", "artist", "licence", "split"):
            if not isinstance(t.get(key), str) or not t[key]:
                raise ManifestError(f"{where}: needs {key}")
        if not CORPUS_ID.match(t["id"]):
            raise ManifestError(f"{where}: an id is notjam-... or cc-..., lowercase")
        if t["licence"] not in LICENCES:
            raise ManifestError(
                f"{where}: licence {t['licence']} is not one of {LICENCES}"
            )
        if t["split"] not in SPLITS:
            raise ManifestError(f"{where}: split {t['split']} is not one of {SPLITS}")
        if t["licence"] != "CC0-1.0" and not t.get("credit"):
            raise ManifestError(f"{where}: a {t['licence']} track needs a credit")
        tracks.append(
            CorpusTrack(
                id=t["id"],
                source=t["source"],
                title=t["title"],
                artist=t["artist"],
                licence=t["licence"],
                split=t["split"],
                credit=t.get("credit"),
                family=t.get("family"),
            )
        )
    splits: dict[str, str] = {}
    for t in tracks:
        family = t.family or t.id
        if splits.setdefault(family, t.split) != t.split:
            raise ManifestError(f"{t.id}: family {family} spans both splits")
    return tracks


def build_index(corpus: Iterable[CorpusTrack], reactive: Path = REACTIVE_DIR) -> dict:
    """The index: the listed regression tracks, then the corpus tracks ingested so far (audio and
    stream present), in manifest order."""
    tracks: list[dict] = [
        {
            "id": t.id,
            "title": t.title,
            "artist": t.artist,
            "group": t.group,
            "stream": f"{t.id}/stream.json",
            "audio": t.audio,
        }
        for t in REGRESSION_TRACKS
        if t.listed
    ]
    for t in corpus:
        if (
            not (reactive / t.audio).exists()
            or not (reactive / t.id / "stream.json").exists()
        ):
            continue
        row = {
            "id": t.id,
            "title": t.title,
            "artist": t.artist,
            "group": t.group,
            "stream": f"{t.id}/stream.json",
            "audio": t.audio,
            "split": t.split,
            "licence": t.licence,
        }
        if t.credit:
            row["credit"] = t.credit
        tracks.append(row)
    return {"version": 1, "tracks": tracks}


def index_text(index: dict) -> str:
    """The index as the TS scripts wrote it (one-space indents, UTF-8 kept)."""
    return json.dumps(index, indent=1, ensure_ascii=False) + "\n"


def write_index(corpus: Iterable[CorpusTrack]) -> dict:
    index = build_index(corpus)
    (REACTIVE_DIR / "index.json").write_text(index_text(index))
    return index


def default_jobs() -> int:
    """Two thirds of the cores, leaving room for the desktop. (On an 18-core M-series Mac a full
    regeneration took 601 s with its 6 performance cores and 358 s with 12.)"""
    return max(1, round((os.cpu_count() or 3) * 2 / 3))


def tool(*args: str) -> str:
    """Runs this tool's own command (``ps-analysis <args>``), returning its output; raises with
    its tail on failure, so callers choose what to print (held-out tracks print nothing)."""
    script = Path(sys.executable).with_name("ps-analysis")
    cmd = (
        [str(script)] if script.exists() else [sys.executable, "-m", "ps_analysis.cli"]
    )
    r = subprocess.run(
        [*cmd, *args], capture_output=True, text=True, cwd=ROOT, check=False
    )
    if r.returncode != 0:
        tail = "\n".join((r.stderr + r.stdout).strip().splitlines()[-15:])
        raise RuntimeError(f"ps-analysis {args[0]} failed ({r.returncode})\n{tail}")
    return r.stdout


def run_jobs[T](items: list[T], jobs: int, job: Callable[[T], None]) -> list[str]:
    """Runs ``job`` over the items, at most ``jobs`` at once; returns every failure's message."""
    failures: list[str] = []

    def guarded(item: T) -> None:
        try:
            job(item)
        except Exception as e:  # noqa: BLE001 (every track's failure is reported, not just the first)
            failures.append(str(e))

    with ThreadPoolExecutor(max_workers=max(1, min(jobs, len(items) or 1))) as pool:
        list(pool.map(guarded, items))
    return failures


def fit_corpus_track(t: CorpusTrack) -> dict:
    """Fits a corpus track's reference and writes its click overlay; a held-out track's summary is
    only whether it fitted."""
    quiet = ["--quiet"] if t.split == "held-out" else []
    out = tool(
        "fit-grid",
        str(REACTIVE_DIR / t.audio),
        "--out",
        str(t.reference),
        "--click",
        str(CLICK_DIR / f"{t.id}-click.ogg"),
        *quiet,
    )
    return json.loads(out)


def source_file(t: CorpusTrack, source: Path, scratch: Path) -> Path:
    """A track's source audio, extracted into ``scratch`` when it is inside a zip."""
    zip_path, sep, member = t.source.partition(".zip:")
    if not sep:
        return source / t.source
    with zipfile.ZipFile(source / f"{zip_path}.zip") as z:
        out = scratch / Path(member).name
        out.write_bytes(z.read(member))
    return out


def unknown_ids(wanted: list[str], known: Iterable[str]) -> list[str]:
    known = set(known)
    return [i for i in wanted if i not in known]


def cmd_corpus_add(ids: list[str], source: Path, jobs: int) -> int:
    corpus = read_manifest()
    unknown = unknown_ids(ids, (t.id for t in corpus))
    if unknown:
        print(f"not in the manifest: {', '.join(unknown)}", file=sys.stderr)
        return 2
    todo = [t for t in corpus if not ids or t.id in ids]
    counts = Counter()
    t0 = time.monotonic()

    def job(t: CorpusTrack) -> None:
        audio = REACTIVE_DIR / t.audio
        stream = REACTIVE_DIR / t.id / "stream.json"
        did: list[str] = []
        if not audio.exists():
            with tempfile.TemporaryDirectory(prefix="corpus-") as scratch:
                file = source_file(t, source, Path(scratch))
                if not file.exists():
                    raise RuntimeError(f"{t.id}: no source at {file}")
                tool("encode", str(file), "--out", str(audio))
            counts["encoded"] += 1
            did.append("encoded")
        # (A fresh encode gets a fresh fit and stream: both must come from the shipped Ogg file.)
        if did or not t.reference.exists():
            fit = fit_corpus_track(t)
            # (Whether a held-out track fitted is reported only as a count, by fit-corpus.)
            did.append(
                "fitted" if t.split == "held-out" else f"fitted ({fit.get('fit')})"
            )
        if did or not stream.exists():
            tool("stream", str(audio), "--out", str(stream), *t.reference_args())
            counts["streamed"] += 1
            did.append("streamed")
        print(f"{t.id}: {', '.join(did) if did else 'up to date'}", flush=True)

    failures = run_jobs(todo, jobs, job)
    for f in failures:
        print(f, file=sys.stderr)
    index = write_index(corpus)
    print(
        f"{counts['encoded']} encoded, {counts['streamed']} streamed in "
        f"{time.monotonic() - t0:.0f} s with {jobs} jobs; wrote public/reactive/index.json "
        f"({len(index['tracks'])} tracks)"
    )
    return 1 if failures else 0


def stream_length(track_id: str) -> float:
    """A track's current stream's length (s); infinite without one, so it goes first."""
    try:
        data = json.loads((REACTIVE_DIR / track_id / "stream.json").read_text())
        return float(data["audio"]["duration"])
    except (OSError, ValueError, KeyError, TypeError):
        return float("inf")


def cmd_streams(ids: list[str], jobs: int, resynth: bool) -> int:
    corpus = read_manifest()
    unknown = unknown_ids(
        ids, [t.id for t in REGRESSION_TRACKS] + [t.id for t in corpus]
    )
    if unknown:
        print(f"unknown tracks: {', '.join(unknown)}", file=sys.stderr)
        return 2

    def wanted(i: str) -> bool:
        return not ids or i in ids

    # Each job: its track, whether it is held out (printing nothing of its analysis), and its run.
    todo: list[tuple[str, bool, Callable[[], str]]] = []
    for r in REGRESSION_TRACKS:
        if not wanted(r.id):
            continue

        def run(r: RegressionTrack = r) -> str:
            audio = REACTIVE_DIR / r.audio
            out = REACTIVE_DIR / r.id / "stream.json"
            params = ["--params", json.dumps(r.params)] if r.params else []
            if r.group == "synthetic":
                reference = REACTIVE_DIR / r.id / "reference.json"
                if resynth or not audio.exists() or not reference.exists():
                    tool(
                        "synth",
                        r.id,
                        "--out",
                        str(audio),
                        "--reference",
                        str(reference),
                    )
                return tool(
                    "stream",
                    str(audio),
                    "--out",
                    str(out),
                    "--reference",
                    str(reference),
                    *params,
                )
            songmap = ROOT / "songs" / r.id / "songmap.json"
            if songmap.exists():
                return tool(
                    "stream",
                    str(audio),
                    "--out",
                    str(out),
                    "--songmap",
                    str(songmap),
                    *params,
                )
            return tool(
                "stream", str(audio), "--out", str(out), "--offline-reference", *params
            )

        todo.append((r.id, False, run))
    for t in corpus:
        if not wanted(t.id):
            continue
        audio = REACTIVE_DIR / t.audio
        if not audio.exists():
            if t.id in ids:
                print(
                    f"{t.id} is not ingested yet (ps-analysis corpus-add)",
                    file=sys.stderr,
                )
            continue

        def run_corpus(t: CorpusTrack = t, audio: Path = audio) -> str:
            out = REACTIVE_DIR / t.id / "stream.json"
            return tool("stream", str(audio), "--out", str(out), *t.reference_args())

        todo.append((t.id, t.split == "held-out", run_corpus))
    # (The longest tracks first, so the last to finish are short.)
    todo.sort(key=lambda j: -stream_length(j[0]))
    t0 = time.monotonic()

    def job(j: tuple[str, bool, Callable[[], str]]) -> None:
        s0 = time.monotonic()
        summary = j[2]()
        shown = "" if j[1] else f"\n{summary.strip()}"
        print(f"== {j[0]} ({time.monotonic() - s0:.1f} s){shown}", flush=True)

    failures = run_jobs(todo, jobs, job)
    for f in failures:
        print(f, file=sys.stderr)
    index = write_index(corpus)
    print(
        f"restreamed {len(todo)} tracks in {time.monotonic() - t0:.0f} s with {jobs} jobs; "
        f"wrote public/reactive/index.json ({len(index['tracks'])} tracks)"
    )
    return 1 if failures else 0


def fit_row(track_id: str, split: str, s: dict) -> str:
    """A row of the fit table."""

    def ms(r: object) -> str:
        return f"{r['median']:.1f}/{r['p90']:.1f}" if isinstance(r, dict) else ""

    meter = (
        "" if s.get("meter") is None else f"{s['meter']} ({s.get('meterConfidence')})"
    )
    within = "" if s.get("within45") is None else f"{100 * float(s['within45']):.0f}%"
    bpm = "" if s.get("bpm") is None else str(s["bpm"])
    decode = f"  ({s['decode']})" if s.get("decode") else ""
    return (
        f"{track_id:<42} {split:<11} {s.get('fit')!s:<10} {bpm:<7} {s.get('level') or '':<6} "
        f"{ms(s.get('residualMs')):<25} {within:<12} {meter:<11} {s.get('sections') or ''}{decode}"
    )


def cmd_fit_corpus(ids: list[str], jobs: int) -> int:
    corpus = read_manifest()
    regression = [r for r in REGRESSION_TRACKS if r.listed]
    unknown = unknown_ids(ids, [t.id for t in corpus] + [r.id for r in regression])
    if unknown:
        print(f"unknown tracks: {', '.join(unknown)}", file=sys.stderr)
        return 2
    fits = CLICK_DIR / "fits"
    fits.mkdir(parents=True, exist_ok=True)
    rows: list[tuple[str, str, dict]] = []
    todo: list[Callable[[], None]] = []
    for t in corpus:
        if not ids or t.id in ids:
            todo.append(lambda t=t: rows.append((t.id, t.split, fit_corpus_track(t))))
    for r in regression:
        if not ids or r.id in ids:

            def fit(r: RegressionTrack = r) -> None:
                out = tool(
                    "fit-grid",
                    str(REACTIVE_DIR / r.audio),
                    "--out",
                    str(fits / f"{r.id}.json"),
                    "--click",
                    str(CLICK_DIR / f"{r.id}-click.ogg"),
                )
                rows.append((r.id, "regression", json.loads(out)))

            todo.append(fit)
    t0 = time.monotonic()
    failures = run_jobs(todo, jobs, lambda job: job())
    for f in failures:
        print(f, file=sys.stderr)
    order = {"iteration": 0, "regression": 1, "held-out": 2}
    rows.sort(key=lambda r: (order[r[1]], r[0]))
    print(
        "track                                      split       fit        bpm     level  "
        "residual ms (median/p90)  within 45 ms  meter       sections"
    )
    for track_id, split, s in rows:
        if split != "held-out":
            print(fit_row(track_id, split, s))
    held = [s for _, split, s in rows if split == "held-out"]
    if held:
        counts = Counter(str(s.get("fit")) for s in held)
        shown = ", ".join(f"{n} {k}" for k, n in counts.items())
        print(f"held-out: {len(held)} fitted, {shown} (counts only)")
    print(
        f"fitted {len(rows)} tracks in {time.monotonic() - t0:.0f} s with {jobs} jobs; click "
        "overlays in captures/corpus/. Restream with ps-analysis streams."
    )
    return 1 if failures else 0
