import numpy as np
import pytest
import soundfile as sf

from ps_analysis.crop import crop

SR = 8000


@pytest.fixture
def source(tmp_path):
    """10 s whose sample value is its own time in seconds, so cuts are visible."""
    t = np.arange(10 * SR, dtype=np.float32) / SR
    path = tmp_path / "source.wav"
    sf.write(str(path), np.stack([t / 10, t / 10], axis=1), SR, subtype="FLOAT")
    return path


def read(path):
    y, _ = sf.read(str(path), dtype="float32")
    return y * 10  # back to seconds (within 16-bit precision)


def test_single_range(source, tmp_path) -> None:
    info = crop(source, tmp_path / "a.flac", 2.0, 5.0, fade_in_ms=0, fade_out_ms=0)
    assert (info["cropStart"], info["cropEnd"], info["duration"]) == (2.0, 5.0, 3.0)
    assert "segments" not in info
    y = read(tmp_path / "a.flac")
    assert y[0] == pytest.approx(2.0, abs=1e-3)
    assert y[-1] == pytest.approx(5.0, abs=1e-3)


def test_segments_join_with_a_centred_crossfade(source, tmp_path) -> None:
    info = crop(
        source,
        tmp_path / "b.flac",
        segments=[(1.0, 3.0), (6.0, 7.0)],
        crossfade_ms=100,
        fade_in_ms=0,
        fade_out_ms=0,
    )
    assert info["duration"] == pytest.approx(3.0)
    assert (info["cropStart"], info["cropEnd"]) == (1.0, 7.0)
    assert info["segments"] == [[1.0, 3.0], [6.0, 7.0]]
    y = read(tmp_path / "b.flac")
    assert len(y) == 3 * SR
    # Before the crossfade the first segment plays; after it, the second.
    assert y[int(1.9 * SR)] == pytest.approx(2.9, abs=1e-3)
    assert y[int(2.1 * SR)] == pytest.approx(6.1, abs=1e-3)
    # Mid-join both sources play at equal power.
    mid = y[2 * SR]
    assert mid == pytest.approx((3.0 + 6.0) * np.cos(np.pi / 4), abs=0.05)


def test_segments_must_not_overlap(source, tmp_path) -> None:
    with pytest.raises(ValueError, match="before the previous one ends"):
        crop(source, tmp_path / "c.flac", segments=[(1.0, 4.0), (3.0, 5.0)])
