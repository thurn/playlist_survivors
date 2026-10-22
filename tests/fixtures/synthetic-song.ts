/**
 * A synthetic song map with the same form as the real Can-Can map: a free 4/4
 * prelude, a quiet 2/4 vamp with a steady pulse, then a 2/4 galop at about 185
 * quarter notes per minute with a faster coda.
 */
import type { SongMap, SongSection } from "../../src/core/songmap.ts";

const FORM: { label: string; bars: [number, number]; bpm: number; feel: "free" | "pulsed" }[] = [
  { label: "prelude", bars: [1, 14], bpm: 86, feel: "free" },
  { label: "galop-vamp", bars: [15, 30], bpm: 176, feel: "pulsed" },
  { label: "galop-opening", bars: [31, 53], bpm: 176, feel: "pulsed" },
  { label: "theme-a", bars: [54, 69], bpm: 185, feel: "pulsed" },
  { label: "theme-a-repeat", bars: [70, 85], bpm: 187, feel: "pulsed" },
  { label: "kick-line", bars: [86, 101], bpm: 188, feel: "pulsed" },
  { label: "bridge", bars: [102, 107], bpm: 185, feel: "pulsed" },
  { label: "climax", bars: [108, 139], bpm: 192, feel: "pulsed" },
  { label: "coda", bars: [140, 177], bpm: 238, feel: "pulsed" },
];

export function syntheticSongMap(): SongMap {
  const beats: number[] = [];
  const energy: number[] = [];
  const barTime: number[] = [];
  let t = 0.15;
  for (let bar = 1; bar <= 177; bar++) {
    const sec = FORM.find((f) => bar >= f.bars[0] && bar <= f.bars[1])!;
    const bpb = bar <= 14 ? 4 : 2;
    // The vamp (bars 15–30) is already at galop tempo, quietly.
    const bpm = bar <= 14 ? sec.bpm : bar <= 30 ? 176 : sec.bpm;
    barTime[bar] = t;
    for (let b = 0; b < bpb; b++) {
      beats.push(t);
      const prog = bar / 177;
      energy.push(sec.feel === "free" ? 0.15 + 0.08 * Math.sin(beats.length / 5) : Math.min(1, 0.5 + 0.45 * prog + (b === 0 ? 0.05 : 0)));
      t += (60 / bpm) * (bar <= 14 ? 1 + 0.03 * Math.sin(beats.length) : 1);
    }
  }
  barTime[178] = t;
  const sections: SongSection[] = FORM.map((f) => ({ label: f.label, bars: f.bars, feel: f.feel, key: f.feel === "free" ? "E major" : "D major" }));
  sections[0].anchors = [0.15, 4.1, 8.2, 12.6, 16.4, 20.5, 24.4, 28.8, 33.0, 36.0, 40.5, 44.2, 47.9, 51.6].filter((x) => x < barTime[15]).map((time, i) => ({ time, strength: 0.4 + 0.04 * i }));
  const phrases: [number, number][] = [];
  for (const f of FORM) {
    const len = f.label === "prelude" ? 4 : 8;
    for (let b = f.bars[0]; b <= f.bars[1]; b += len) phrases.push([b, Math.min(f.bars[1], b + len - 1)]);
  }
  const accents = [
    { time: barTime[31], strength: 1, label: "tutti-chord" },
    { time: barTime[33], strength: 0.9, label: "tutti-chord" },
    { time: barTime[35], strength: 0.9, label: "tutti-chord" },
    { time: barTime[62], strength: 0.8 },
    { time: barTime[78], strength: 0.8 },
    { time: barTime[93], strength: 0.85 },
    { time: barTime[101], strength: 0.9 },
    { time: barTime[108], strength: 0.85 },
    { time: barTime[116], strength: 0.9 },
    { time: barTime[124], strength: 0.75 },
    { time: barTime[132], strength: 0.85 },
    { time: barTime[150], strength: 0.9 },
    { time: barTime[170], strength: 1, label: "tutti-chord" },
    { time: barTime[173], strength: 1, label: "tutti-chord" },
    { time: barTime[175], strength: 1, label: "tutti-chord" },
    { time: barTime[176], strength: 1, label: "tutti-chord" },
  ];
  return {
    version: 1,
    song: "orpheus-galop",
    audio: { file: "orpheus-galop.flac", source: "synthetic", cropStart: 0, cropEnd: t + 2, sampleRate: 44100, duration: t + 2 },
    meter: [
      { fromBar: 1, beatsPerBar: 4 },
      { fromBar: 15, beatsPerBar: 2 },
    ],
    beats,
    firstDownbeatIndex: 0,
    sections,
    phrases,
    tempoChanges: [{ bars: [140, 177] }],
    energy,
    accents,
  };
}
