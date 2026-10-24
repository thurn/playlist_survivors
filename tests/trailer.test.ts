import { describe, expect, it } from "vitest";
import { join } from "node:path";
import { ROOT, readSongMap } from "../scripts/lib/files.ts";
import { Timeline, editAudio, readTrailerSpec, readWav, wavBytes, type TrailerSpec } from "../scripts/lib/trailer.ts";

const galop = readTrailerSpec(join(ROOT, "trailer/galop.yaml"));
const maps = { "orpheus-galop": readSongMap("orpheus-galop") };

/** A spec over two runs, a and b (a with the Galop's song map), with the given cuts. */
const twoRuns = (cuts: TrailerSpec["cuts"], over: Partial<TrailerSpec> = {}): TrailerSpec => ({
  ...galop,
  runs: { a: { query: "a", songMap: "orpheus-galop", gain: 1 }, b: { query: "b", gain: 1 } },
  cuts,
  fadeIn: 0,
  fadeOut: 0,
  audio: { sfx: 1, gain: 1 },
  ...over,
});

describe("the trailer's timeline", () => {
  it("resolves the Galop trailer to about 30 seconds", () => {
    expect(Math.abs(new Timeline(galop, maps).duration - 30)).toBeLessThan(0.5);
  });

  it("reads bars and beats from a run's song map, and seconds anywhere", () => {
    const tl = new Timeline(twoRuns([{ run: "a", from: "108", to: "108:2" }, { run: "b", from: 3, to: 4 }]), maps);
    expect(tl.cuts[0].from).toBeCloseTo(104.548, 2);
    expect(tl.cuts[0].to).toBeGreaterThan(tl.cuts[0].from);
    expect(tl.cuts[1].from).toBe(3);
  });

  it("rejects bars in a run without a song map, an unknown run and an empty cut", () => {
    expect(() => new Timeline(twoRuns([{ run: "b", from: "3", to: 4 }]), maps)).toThrow(/no song map/);
    expect(() => new Timeline(twoRuns([{ run: "c", from: 1, to: 2 }]), maps)).toThrow(/no run "c"/);
    expect(() => new Timeline(twoRuns([{ run: "a", from: 2, to: 2 }]), maps)).toThrow(/empty/);
  });

  it("cuts straight from one run to the next without a crossfade", () => {
    const tl = new Timeline(twoRuns([{ run: "a", from: 10, to: 12 }, { run: "b", from: 50, to: 52, crossfadeMs: 0 }]), maps);
    expect(tl.layersAt(1.99)).toEqual([{ run: "a", s: expect.closeTo(11.99, 6), w: 1 }]);
    expect(tl.layersAt(2.01)).toEqual([{ run: "b", s: expect.closeTo(50.01, 6), w: 1 }]);
  });

  it("dissolves across a crossfade centred on the join, each run playing on through it", () => {
    const tl = new Timeline(twoRuns([{ run: "a", from: 10, to: 12 }, { run: "b", from: 50, to: 52, crossfadeMs: 1000 }]), maps);
    expect(tl.layersAt(1.4)).toHaveLength(1);
    const [out, inn] = tl.layersAt(2.25);
    expect(out).toEqual({ run: "a", s: expect.closeTo(12.25, 6), w: 1 });
    expect(inn).toEqual({ run: "b", s: expect.closeTo(50.25, 6), w: expect.closeTo(0.75, 6) });
    expect(tl.layersAt(1.75)[1].w).toBeCloseTo(0.25, 6);
    expect(tl.layersAt(2.6)).toHaveLength(1);
  });

  it("rejects a crossfade longer than its cuts", () => {
    expect(() => new Timeline(twoRuns([{ run: "a", from: 10, to: 10.4 }, { run: "b", from: 50, to: 52, crossfadeMs: 1000 }]), maps)).toThrow(/shorter than the crossfades/);
  });
});

describe("the trailer's audio edit", () => {
  const sr = 1000;
  // Each sample's value is its source time in seconds, plus the run's offset (a: 0, b: 100).
  const ramp = (offset: number) => ({ sampleRate: sr, data: Float32Array.from({ length: 60 * sr * 2 }, (_, i) => offset + Math.floor(i / 2) / sr) });
  const runs = { a: { mix: ramp(0), music: ramp(0) }, b: { mix: ramp(100), music: ramp(100) } };

  it("plays each cut's own run and samples on either side of a join", () => {
    const tl = new Timeline(twoRuns([{ run: "a", from: 1, to: 2 }, { run: "b", from: 5, to: 6, crossfadeMs: 100 }]), maps);
    const out = editAudio(tl, runs).data;
    expect(out[2 * 500]).toBeCloseTo(1.5, 6);
    expect(out[2 * 940]).toBeCloseTo(1.94, 6);
    expect(out[2 * 1060]).toBeCloseTo(105.06, 4);
    expect(out[2 * 1999 + 1]).toBeCloseTo(105.999, 4);
  });

  it("joins cuts with an equal-power crossfade that keeps a steady tone steady", () => {
    const flat = { sampleRate: sr, data: new Float32Array(10 * sr * 2).fill(0.5) };
    const tl = new Timeline(twoRuns([{ run: "a", from: 1, to: 2 }, { run: "b", from: 5, to: 6, crossfadeMs: 100 }]), maps);
    const out = editAudio(tl, { a: { mix: flat, music: flat }, b: { mix: flat, music: flat } }).data;
    expect(out.length).toBe(2 * sr * 2);
    // Equal power: uncorrelated material keeps its loudness; a steady tone rises by at most √2.
    expect(Math.min(...out)).toBeGreaterThan(0.49);
    expect(Math.max(...out)).toBeLessThan(0.5 * Math.SQRT2 + 1e-3);
  });

  it("blends the music toward the game's mix by the effects setting, times the run's and the edit's gains", () => {
    const spec = twoRuns([{ run: "a", from: 0, to: 1 }], { audio: { sfx: 0.5, gain: 2 } });
    spec.runs.a.gain = 0.5;
    const music = { sampleRate: 100, data: new Float32Array(200).fill(0.2) };
    const mix = { sampleRate: 100, data: new Float32Array(200).fill(0.6) };
    expect(editAudio(new Timeline(spec, maps), { a: { mix, music } }).data[10]).toBeCloseTo(0.4, 6);
  });

  it("fades in and out", () => {
    const flat = { sampleRate: 100, data: new Float32Array(400).fill(1) };
    const tl = new Timeline(twoRuns([{ run: "a", from: 0, to: 1 }], { fadeIn: 0.2, fadeOut: 0.5 }), maps);
    const out = editAudio(tl, { a: { mix: flat, music: flat } }).data;
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
