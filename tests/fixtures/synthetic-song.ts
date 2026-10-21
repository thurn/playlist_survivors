/**
 * A synthetic song map with the same section roles as the real Can-Can map:
 * a free 4/4 prelude, then a 2/4 galop at 185 quarter notes per minute.
 */
import type { SongMap, SongSection } from "../../src/core/songmap.ts";

export function syntheticSongMap(): SongMap {
  const beats: number[] = [];
  const energy: number[] = [];
  let t = 0.5;
  // Prelude: 8 bars of 4/4 at a wavering ~66 bpm.
  for (let i = 0; i < 32; i++) {
    beats.push(t);
    energy.push(0.15 + 0.1 * Math.sin(i / 5));
    t += 60 / 66 + 0.04 * Math.sin(i);
  }
  const galopBars: [string, number][] = [
    ["galop-opening", 8],
    ["theme-a", 16],
    ["theme-a-repeat", 16],
    ["kick-line", 16],
    ["bridge", 16],
    ["climax", 32],
    ["coda", 16],
  ];
  const beat = 60 / 185;
  const sections: SongSection[] = [
    {
      label: "prelude",
      bars: [1, 8],
      feel: "free",
      key: "F major",
      anchors: [0.5, 4.2, 8.1, 12.0, 15.6, 19.9, 24.1, 28.0].map((time, i) => ({ time, strength: 0.4 + 0.08 * i })),
    },
  ];
  let bar = 9;
  for (const [label, n] of galopBars) {
    sections.push({ label, bars: [bar, bar + n - 1], feel: "pulsed", key: "D major" });
    for (let i = 0; i < n * 2; i++) {
      beats.push(t);
      const prog = (beats.length - 32) / (galopBars.reduce((a, b) => a + b[1], 0) * 2);
      energy.push(Math.min(1, 0.55 + 0.45 * prog + (i % 2 === 0 ? 0.05 : 0)));
      t += beat;
    }
    bar += n;
  }
  const phrases: [number, number][] = [];
  for (const s of sections) for (let b = s.bars[0]; b <= s.bars[1]; b += s.label === "prelude" ? 4 : 8) phrases.push([b, Math.min(s.bars[1], b + (s.label === "prelude" ? 3 : 7))]);
  const timeOfBar = (b: number) => (b <= 8 ? beats[(b - 1) * 4] : beats[32 + (b - 9) * 2]);
  const accents = [
    { time: timeOfBar(9), strength: 1, label: "tutti-chord" },
    { time: timeOfBar(11), strength: 0.9, label: "tutti-chord" },
    { time: timeOfBar(13), strength: 0.9, label: "tutti-chord" },
    { time: timeOfBar(25), strength: 0.8 },
    { time: timeOfBar(41), strength: 0.8 },
    { time: timeOfBar(56), strength: 0.85 },
    { time: timeOfBar(64), strength: 0.9 },
    { time: timeOfBar(73), strength: 0.8 },
    { time: timeOfBar(89), strength: 0.9 },
    { time: timeOfBar(97), strength: 0.75 },
    { time: timeOfBar(105), strength: 0.85 },
    { time: timeOfBar(117), strength: 0.9 },
    { time: timeOfBar(121), strength: 1, label: "tutti-chord" },
    { time: timeOfBar(125), strength: 1, label: "tutti-chord" },
    { time: timeOfBar(128), strength: 1, label: "tutti-chord" },
  ];
  return {
    version: 1,
    song: "orpheus-galop",
    audio: { file: "orpheus-galop.flac", source: "synthetic", cropStart: 0, cropEnd: t + 2, sampleRate: 44100, duration: t + 2 },
    meter: [
      { fromBar: 1, beatsPerBar: 4 },
      { fromBar: 9, beatsPerBar: 2 },
    ],
    beats,
    firstDownbeatIndex: 0,
    sections,
    phrases,
    tempoChanges: [],
    energy,
    accents,
  };
}
