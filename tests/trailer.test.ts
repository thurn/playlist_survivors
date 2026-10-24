import { describe, expect, it } from "vitest";
import { join } from "node:path";
import { ROOT, readSongMap } from "../scripts/lib/files.ts";
import { Timeline, editAudio, readTrailerSpec, readWav, wavBytes, type TrailerSpec } from "../scripts/lib/trailer.ts";

const galop = readTrailerSpec(join(ROOT, "trailer/galop.yaml"));
const map = readSongMap("orpheus-galop");

describe("the trailer's timeline", () => {
  it("resolves the Galop trailer to about 30 seconds", () => {
    expect(Math.abs(new Timeline(galop, map).duration - 30)).toBeLessThan(0.5);
  });

  it("maps output time across a cut to the next range of the song", () => {
    const tl = new Timeline(galop, map);
    const [a, b] = tl.cuts;
    const join = a.to - a.from;
    expect(tl.songAt(0)).toBeCloseTo(a.from, 6);
    expect(tl.songAt(join - 0.01)).toBeCloseTo(a.to - 0.01, 6);
    expect(tl.songAt(join + 0.01)).toBeCloseTo(b.from + 0.01, 6);
  });

  it("reads bars and beats from the song map", () => {
    const tl = new Timeline(galop, map);
    expect(tl.song("108")).toBeCloseTo(104.548, 2);
    expect(tl.song("108:2")).toBeGreaterThan(tl.song("108"));
    expect(tl.song(12.5)).toBe(12.5);
  });

  it("rejects an empty cut", () => {
    expect(() => new Timeline({ ...galop, cuts: [{ from: "70", to: "70" }] }, map)).toThrow(/empty/);
  });
});

describe("the trailer's audio edit", () => {
  const sr = 1000;
  const twoCuts: TrailerSpec = { ...galop, cuts: [{ from: 1, to: 2 }, { from: 5, to: 6 }], crossfadeMs: 100, fadeIn: 0, fadeOut: 0, audio: { sfx: 1, gain: 1 } };

  it("plays each cut's own samples on either side of a join", () => {
    const tl = new Timeline(twoCuts, map);
    // Each sample's value is its source time in seconds.
    const ramp = { sampleRate: sr, data: Float32Array.from({ length: 10 * sr * 2 }, (_, i) => Math.floor(i / 2) / sr) };
    const out = editAudio(tl, ramp, ramp).data;
    expect(out[2 * 500]).toBeCloseTo(1.5, 6);
    expect(out[2 * 940]).toBeCloseTo(1.94, 6);
    expect(out[2 * 1060]).toBeCloseTo(5.06, 6);
    expect(out[2 * 1999 + 1]).toBeCloseTo(5.999, 6);
  });

  it("joins cuts with an equal-power crossfade that keeps a steady tone steady", () => {
    const tl = new Timeline(twoCuts, map);
    const flat = { sampleRate: sr, data: new Float32Array(10 * sr * 2).fill(0.5) };
    const out = editAudio(tl, flat, flat).data;
    expect(out.length).toBe(2 * sr * 2);
    // Equal power: uncorrelated material keeps its loudness; a steady tone rises by at most √2.
    expect(Math.min(...out)).toBeGreaterThan(0.49);
    expect(Math.max(...out)).toBeLessThan(0.5 * Math.SQRT2 + 1e-3);
  });

  it("blends the music toward the game's mix by the effects setting", () => {
    const tl = new Timeline({ ...galop, cuts: [{ from: 0, to: 1 }], fadeIn: 0, fadeOut: 0, audio: { sfx: 0.5, gain: 2 } }, map);
    const music = { sampleRate: 100, data: new Float32Array(200).fill(0.2) };
    const mix = { sampleRate: 100, data: new Float32Array(200).fill(0.6) };
    expect(editAudio(tl, mix, music).data[10]).toBeCloseTo(0.8, 6);
  });

  it("fades in and out", () => {
    const tl = new Timeline({ ...galop, cuts: [{ from: 0, to: 1 }], fadeIn: 0.2, fadeOut: 0.5, audio: { sfx: 1, gain: 1 } }, map);
    const flat = { sampleRate: 100, data: new Float32Array(400).fill(1) };
    const out = editAudio(tl, flat, flat).data;
    expect(out[0]).toBe(0);
    expect(out[2 * 10]).toBeCloseTo(0.5, 6);
    expect(out[2 * 30]).toBe(1);
    expect(out[2 * 75]).toBeCloseTo(0.5, 6);
  });

  it("round-trips a float WAV", () => {
    const a = { sampleRate: 44100, data: Float32Array.from([0, 0.25, -0.5, 1]) };
    expect(readWav(wavBytes(a))).toEqual(a);
  });
});
