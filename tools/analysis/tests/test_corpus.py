"""The corpus commands' pieces (ported from the TS scripts, D6): the manifest, the index, sources."""

import zipfile

import pytest

from ps_analysis import corpus
from ps_analysis.cli import build_parser


def test_the_index_rebuilt_from_the_manifest_is_the_shipped_one():
    # (The TS scripts wrote it; the port must write the same bytes.)
    shipped = (corpus.REACTIVE_DIR / "index.json").read_text()
    assert corpus.index_text(corpus.build_index(corpus.read_manifest())) == shipped


def test_the_shipped_manifest_reads(tmp_path):
    tracks = corpus.read_manifest()
    assert tracks and all(t.split in corpus.SPLITS for t in tracks)
    assert {t.group for t in tracks} <= {"notjam", "cc"}


@pytest.mark.parametrize(
    ("entry", "error"),
    [
        ("id: Bad-Id", "lowercase"),
        ("licence: GPL", "licence"),
        ("split: training", "split"),
        ("licence: CC-BY-4.0", "credit"),
    ],
)
def test_a_bad_manifest_entry_is_named(tmp_path, entry, error):
    base = {
        "id": "cc-some-track",
        "source": "a.wav",
        "title": "A",
        "artist": "B",
        "licence": "CC0-1.0",
        "split": "iteration",
    }
    key, value = entry.split(": ")
    base[key] = value
    lines = "\n".join(f"    {k}: {v}" for k, v in base.items())
    path = tmp_path / "manifest.yaml"
    path.write_text("tracks:\n  -\n" + lines + "\n")
    with pytest.raises(corpus.ManifestError, match=error):
        corpus.read_manifest(path)


def test_a_family_keeps_to_one_split(tmp_path):
    def track(i, split):
        return (
            f"  - {{id: cc-t{i}, source: a.wav, title: A, artist: B, licence: CC0-1.0, "
            f"split: {split}, family: f}}\n"
        )

    path = tmp_path / "manifest.yaml"
    path.write_text("tracks:\n" + track(1, "iteration") + track(2, "held-out"))
    with pytest.raises(corpus.ManifestError, match="family"):
        corpus.read_manifest(path)


def test_a_source_inside_a_zip_is_extracted(tmp_path):
    with zipfile.ZipFile(tmp_path / "pack.zip", "w") as z:
        z.writestr("inner/dir/song.wav", b"RIFF")
    t = corpus.CorpusTrack(
        id="cc-x",
        source="pack.zip:inner/dir/song.wav",
        title="X",
        artist="Y",
        licence="CC0-1.0",
        split="iteration",
    )
    scratch = tmp_path / "scratch"
    scratch.mkdir()
    out = corpus.source_file(t, tmp_path, scratch)
    assert out == scratch / "song.wav" and out.read_bytes() == b"RIFF"
    plain = corpus.CorpusTrack(**{**t.__dict__, "source": "loose.wav"})
    assert corpus.source_file(plain, tmp_path, scratch) == tmp_path / "loose.wav"


def test_an_unknown_track_is_refused(capsys):
    assert corpus.cmd_streams(["no-such-track"], 1, False) == 2
    assert "no-such-track" in capsys.readouterr().err


def test_the_commands_parse():
    p = build_parser()
    a = p.parse_args(["corpus-add", "cc-a", "--source", "/tmp", "--jobs", "2"])
    assert a.ids == ["cc-a"] and a.jobs == 2
    assert p.parse_args(["streams", "--resynth"]).resynth
    with pytest.raises(SystemExit):
        p.parse_args(["fit-corpus", "--jobs", "0"])


def test_a_fit_row_lines_up():
    row = corpus.fit_row(
        "cc-x",
        "iteration",
        {
            "fit": "ok",
            "bpm": 120,
            "level": "high",
            "residualMs": {"median": 3.21, "p90": 9.87},
            "within45": 0.93,
            "meter": 4,
            "meterConfidence": 0.8,
            "sections": 5,
        },
    )
    assert row.startswith("cc-x" + " " * 39 + "iteration")
    assert "3.2/9.9" in row and "93%" in row and "4 (0.8)" in row
