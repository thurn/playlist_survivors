import numpy as np

from ps_analysis import pcm, stream


def test_pcm_is_the_streams_samples(tmp_path):
    import soundfile as sf

    y = (np.sin(np.arange(4410) / 7.0) * 0.5).astype(np.float32)
    stereo = np.stack([y, -y / 2], axis=1)
    src = tmp_path / "a.wav"
    sf.write(src, stereo, 44100, subtype="FLOAT")
    n = pcm.write(src, tmp_path / "out" / "a.f32")
    got = np.frombuffer((tmp_path / "out" / "a.f32").read_bytes(), dtype="<f4")
    want, _ = stream.load_audio(src)
    assert n == len(want) == 4410
    assert got.tobytes() == want.astype("<f4").tobytes()
