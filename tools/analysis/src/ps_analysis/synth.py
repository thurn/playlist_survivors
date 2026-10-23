"""`synth`: synthetic test tracks with exact reference beats and sections.

Each track is built from a beat timeline, so tempo ramps and hard cuts have
exact references. Hits get ±4 ms of jitter; the reference is the nominal grid.
Every track draws from its own seeded generator, so it renders the same audio
every time.

Tracks:

- `house-128`: steady four-on-the-floor.
- `dnb-174`: drum and bass at 174 beats per minute.
- `hiphop-92-swing`: swung, syncopated.
- `accelerando-100-140`: a steady start, a long accelerando and a steady end.
- `pop-arrangement`: a chords-only intro, verse, chorus, a pads-only breakdown,
  chorus, then a hard cut from 120 to 96 beats per minute.
- `causality-mix`: a short check-only track on which every reactive feature
  fires: steady drums with an accent every 8 beats, a quiet stretch and a loud
  entrance, a hard tempo cut, a few seconds of pads only, and a final chord
  ringing out into silence.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

import numpy as np

SR = 44100

TRACKS = (
    "house-128",
    "dnb-174",
    "hiphop-92-swing",
    "accelerando-100-140",
    "pop-arrangement",
    "causality-mix",
)

CHORDS = [
    [220, 277.2, 329.6],
    [196, 246.9, 293.7],
    [174.6, 220, 261.6],
    [196, 246.9, 293.7],
]
BASS = [55, 49, 43.65, 49]


def env(n: int, attack: float = 0.002, decay: float = 0.15) -> np.ndarray:
    t = np.arange(n) / SR
    return np.minimum(1, t / attack) * np.exp(-t / decay)


@dataclass
class Synth:
    """One track's mix buffer and its seeded generator."""

    dur: float
    seed: int
    buf: np.ndarray = field(init=False)
    rng: np.random.Generator = field(init=False)

    def __post_init__(self) -> None:
        self.buf = np.zeros(int((self.dur + 2) * SR))
        self.rng = np.random.default_rng(self.seed)

    def add(self, t: float, s: np.ndarray, jitter: float = 0.004) -> None:
        t = t + (self.rng.uniform(-jitter, jitter) if jitter else 0.0)
        i = round(t * SR)
        if i < 0:
            return
        j = min(len(self.buf), i + len(s))
        self.buf[i:j] += s[: j - i]

    def noise(self, n: int) -> np.ndarray:
        return self.rng.standard_normal(n)

    def kick(self) -> np.ndarray:
        n = int(0.35 * SR)
        t = np.arange(n) / SR
        f = 50 + 120 * np.exp(-t / 0.03)
        return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(n, 0.001, 0.12) * 0.9

    def snare(self) -> np.ndarray:
        n = int(0.25 * SR)
        t = np.arange(n) / SR
        body = np.sin(2 * np.pi * 190 * t) * env(n, 0.001, 0.05) * 0.4
        return body + self.noise(n) * env(n, 0.001, 0.08) * 0.35

    def clap(self) -> np.ndarray:
        n = int(0.2 * SR)
        noise = self.noise(n) * env(n, 0.001, 0.06) * 0.35
        return noise - np.convolve(noise, np.ones(8) / 8, mode="same")

    def hat(self, open_: bool = False) -> np.ndarray:
        n = int((0.2 if open_ else 0.06) * SR)
        noise = self.noise(n)
        hp = noise - np.convolve(noise, np.ones(4) / 4, mode="same")
        return hp * env(n, 0.0005, 0.08 if open_ else 0.015) * 0.18

    def crash(self, decay: float = 0.6) -> np.ndarray:
        n = int(4 * decay * SR)
        noise = self.noise(n)
        hp = noise - np.convolve(noise, np.ones(3) / 3, mode="same")
        return hp * env(n, 0.0005, decay) * 0.5

    def pad(self, freqs: list[float], dur: float, amp: float = 0.05) -> np.ndarray:
        n = int(dur * SR)
        t = np.arange(n) / SR
        a = np.minimum(1, t / 0.4) * np.minimum(1, (dur - t) / 0.4).clip(0, 1)
        return (
            sum(
                np.sin(2 * np.pi * f * t + self.rng.uniform(0, 6))
                + 0.3 * np.sin(2 * np.pi * 2.003 * f * t)
                for f in freqs
            )
            * a
            * amp
        )


def tone(
    freq: float,
    dur: float,
    amp: float = 0.15,
    decay: float = 0.4,
    harmonics: tuple[float, ...] = (1, 0.5, 0.25),
) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    s = sum(h * np.sin(2 * np.pi * freq * (k + 1) * t) for k, h in enumerate(harmonics))
    return s * env(n, 0.005, decay) * amp


def beat_times(
    segments: list[tuple[int, float, float]], start: float = 0.5
) -> tuple[np.ndarray, float]:
    """Beat times for segments of (beats, bpm at the first, bpm at the last).

    Returns the beats and the time one beat after the last.
    """
    out = [start]
    for n, b0, b1 in segments:
        for i in range(n):
            bpm = b0 + (b1 - b0) * (i / max(1, n - 1))
            out.append(out[-1] + 60 / bpm)
    return np.array(out[:-1]), out[-1]


def at(beats: np.ndarray, x: float) -> float:
    """Song time of fractional beat index x (linear between beats)."""
    k = min(max(int(np.floor(x)), 0), len(beats) - 2)
    return float(beats[k] + (x - k) * (beats[k + 1] - beats[k]))


def drums(
    m: Synth, beats: np.ndarray, b0: int, b1: int, style: str, gain: float = 1.0
) -> None:
    """A drum pattern over beat indices [b0, b1)."""
    for b in range(b0, b1):
        pos = b % 4
        if style == "house":
            m.add(at(beats, b), m.kick() * gain)
            m.add(at(beats, b + 0.5), m.hat(open_=True) * gain)
            if pos in (1, 3):
                m.add(at(beats, b), m.clap() * gain)
            for q in (0.25, 0.75):
                m.add(at(beats, b + q), m.hat() * gain)
        elif style == "hiphop":
            swing = 0.58
            if pos == 0:
                m.add(at(beats, b), m.kick() * gain)
            if pos == 1:
                m.add(at(beats, b + swing), m.kick() * gain)
            if pos == 2:
                m.add(at(beats, b + 0.25), m.kick() * gain)
                m.add(at(beats, b + swing), m.kick() * gain)
            if pos in (1, 3):
                m.add(at(beats, b), m.snare() * gain)
            m.add(at(beats, b), m.hat() * gain)
            m.add(at(beats, b + swing), m.hat() * gain)
        elif style == "dnb":
            if pos == 0:
                m.add(at(beats, b), m.kick() * gain)
            if pos == 2:
                m.add(at(beats, b + 0.5), m.kick() * gain)
            if pos in (1, 3):
                m.add(at(beats, b), m.snare() * gain)
            for q in (0, 0.5):
                m.add(at(beats, b + q), m.hat() * gain)
            if pos == 3:
                m.add(at(beats, b + 0.75), m.snare() * 0.4 * gain)
        elif style == "pop":
            if pos in (0, 2):
                m.add(at(beats, b), m.kick() * gain)
            if pos == 2:
                m.add(at(beats, b + 0.5), m.kick() * 0.7 * gain)
            if pos in (1, 3):
                m.add(at(beats, b), m.snare() * gain)
            for q in (0, 0.5):
                m.add(at(beats, b + q), m.hat() * gain)
        else:
            raise ValueError(f"unknown drum style {style}")


def harmony(
    m: Synth,
    beats: np.ndarray,
    b0: int,
    b1: int,
    bass: bool = True,
    chords: bool = True,
    pads: bool = False,
    gain: float = 1.0,
) -> None:
    for b in range(b0, b1):
        bar = b // 4
        ch = CHORDS[bar % 4]
        if bass:
            m.add(
                at(beats, b + 0.5),
                tone(BASS[bar % 4], 0.25, 0.25 * gain, 0.12, (1, 0.6, 0.3)),
            )
        if chords:
            for f in ch:
                m.add(at(beats, b), tone(f * 2, 0.3, 0.05 * gain, 0.15))
        if pads and b % 4 == 0:
            d = at(beats, b + 4) - at(beats, b)
            m.add(at(beats, b), m.pad(ch, d + 0.3, 0.05 * gain), jitter=0)


def accents(
    m: Synth, beats: np.ndarray, b0: int, b1: int, every: int = 8, gain: float = 1.0
) -> None:
    """A tutti hit (crash and a loud chord stab) every `every` beats from b0."""
    for b in range(b0, b1, every):
        t = at(beats, b)
        m.add(t, m.crash() * gain)
        for f in CHORDS[0]:
            m.add(t, tone(f, 0.6, 0.18 * gain, 0.3), jitter=0)


Section = tuple[str, int, int]


def _house(m_beats: int, seed: int) -> tuple[Synth, np.ndarray, list[Section]]:
    beats, end = beat_times([(m_beats, 128, 128)])
    m = Synth(end, seed)
    drums(m, beats, 0, m_beats, "house")
    harmony(m, beats, 0, m_beats)
    return m, beats, [("all", 0, m_beats)]


def _render(name: str) -> tuple[Synth, np.ndarray, list[Section]]:
    if name == "house-128":
        return _house(128, 1)
    if name == "hiphop-92-swing":
        beats, end = beat_times([(92, 92, 92)])
        m = Synth(end, 2)
        drums(m, beats, 0, 92, "hiphop")
        harmony(m, beats, 0, 92, chords=False, pads=True)
        return m, beats, [("all", 0, 92)]
    if name == "dnb-174":
        beats, end = beat_times([(174, 174, 174)])
        m = Synth(end, 3)
        drums(m, beats, 0, 174, "dnb")
        harmony(m, beats, 0, 174, chords=False, pads=True)
        return m, beats, [("all", 0, 174)]
    if name == "accelerando-100-140":
        beats, end = beat_times([(16, 100, 100), (112, 100, 140), (16, 140, 140)])
        m = Synth(end, 4)
        drums(m, beats, 0, 144, "pop")
        harmony(m, beats, 0, 144)
        return (
            m,
            beats,
            [("steady-100", 0, 16), ("ramp", 16, 128), ("steady-140", 128, 144)],
        )
    if name == "pop-arrangement":
        beats, end = beat_times([(32 + 64 + 64 + 32 + 64, 120, 120), (64, 96, 96)])
        m = Synth(end, 5)
        harmony(m, beats, 0, 32, bass=False)
        drums(m, beats, 32, 96, "pop")
        harmony(m, beats, 32, 96, chords=False)
        drums(m, beats, 96, 160, "house")
        harmony(m, beats, 96, 160)
        harmony(m, beats, 160, 192, bass=False, chords=False, pads=True)
        drums(m, beats, 192, 256, "house")
        harmony(m, beats, 192, 256)
        drums(m, beats, 256, 320, "pop")
        harmony(m, beats, 256, 320, chords=False, pads=True)
        return (
            m,
            beats,
            [
                ("intro-chords-only", 0, 32),
                ("verse", 32, 96),
                ("chorus", 96, 160),
                ("breakdown-pads-only", 160, 192),
                ("chorus-2", 192, 256),
                ("cut-to-96", 256, 320),
            ],
        )
    if name == "causality-mix":
        # 120 BPM: steady 32 beats, quiet 8, a loud entrance 16; then 150 BPM:
        # drums 16, pads only 8, drums 4 and the final chord on the next beat.
        beats, end = beat_times([(32 + 8 + 16, 120, 120), (16 + 8 + 4 + 1, 150, 150)])
        m = Synth(end + 4.0, 6)
        drums(m, beats, 0, 32, "house")
        harmony(m, beats, 0, 32)
        accents(m, beats, 0, 32)
        harmony(m, beats, 32, 40, bass=False, gain=0.08)
        drums(m, beats, 40, 56, "house", gain=1.3)
        harmony(m, beats, 40, 56, gain=1.3)
        accents(m, beats, 40, 56, gain=1.4)
        drums(m, beats, 56, 72, "dnb")
        harmony(m, beats, 56, 72, chords=False)
        accents(m, beats, 56, 72)
        harmony(m, beats, 72, 80, bass=False, chords=False, pads=True)
        drums(m, beats, 80, 84, "house")
        harmony(m, beats, 80, 84)
        last = float(beats[84])
        m.add(last, m.crash(1.2), jitter=0)
        m.add(last, m.kick(), jitter=0)
        for f in CHORDS[0]:
            m.add(last, tone(f, 4.0, 0.2, 1.0), jitter=0)
        return (
            m,
            beats,
            [
                ("steady-120", 0, 32),
                ("quiet", 32, 40),
                ("entrance", 40, 56),
                ("cut-to-150", 56, 72),
                ("pads-only", 72, 80),
                ("drums", 80, 84),
                ("final-chord", 84, 85),
            ],
        )
    raise ValueError(f"unknown synthetic track {name}; known: {', '.join(TRACKS)}")


def section_bounds(beats: np.ndarray, sections: list[Section]) -> list[dict[str, Any]]:
    """Sections as {label, s0, s1} in seconds; a section ending past the beats ends one beat later."""
    out = []
    for label, a, b in sections:
        s1 = beats[b] if b < len(beats) else beats[-1] + (beats[-1] - beats[-2])
        out.append({"label": label, "s0": float(beats[a]), "s1": float(s1)})
    return out


def track(name: str) -> tuple[np.ndarray, dict[str, Any]]:
    """The track's mono audio at SR (peak 0.8) and its exact reference."""
    m, beats, sections = _render(name)
    y = m.buf / np.max(np.abs(m.buf)) * 0.8
    reference = {
        "source": "exact",
        "beats": [float(b) for b in beats],
        "sections": section_bounds(beats, sections),
    }
    return y.astype(np.float32), reference


def reference(name: str) -> dict[str, Any]:
    """The exact reference alone (rendering is cheap enough to reuse `track`)."""
    return track(name)[1]
