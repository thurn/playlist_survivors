import { describe, expect, it } from "vitest";
import { beatIndexAt, indexSong, timeOf, timeOfBeatIndex, type SongMap } from "../src/core/songmap.ts";
import { Warp } from "../src/core/warp.ts";
import { nearestInside, sampleInShape, shapeContains, type Shape } from "../src/core/geometry.ts";
import { Rng, hash32 } from "../src/core/rng.ts";

const map = { beats: [0.41, 1.52, 2.6, 3.71], firstDownbeatIndex: 0, meter: [{ fromBar: 1, beatsPerBar: 2 }] } as unknown as SongMap;

describe("musical time", () => {
  it("converts between song time and beat index", () => {
    expect(beatIndexAt(map, 2.0)).toBeCloseTo(1.444, 3);
    expect(timeOfBeatIndex(map, 2.5)).toBeCloseTo(3.155, 3);
    // Extrapolates from the nearest two beats.
    expect(timeOfBeatIndex(map, -1)).toBeCloseTo(0.41 - 1.11, 6);
    expect(beatIndexAt(map, 4.82)).toBeCloseTo(4, 6);
  });
  it("finds bars and beats", () => {
    const idx = indexSong(map);
    expect(idx.barCount).toBe(2);
    expect(timeOf(idx, { bar: 2, beat: 1 })).toBeCloseTo(2.6, 6);
    expect(timeOf(idx, { bar: 1, beat: 2 })).toBeCloseTo(1.52, 6);
  });
});

describe("time warp", () => {
  it("matches the design's example table", () => {
    const w = Warp.fromRates([
      { s: 0, rate: 0.5 },
      { s: 35.2, rate: 0 },
      { s: 35.31, rate: 1 },
      { s: 88.31, rate: 0 },
      { s: 88.42, rate: 1 },
    ]);
    expect(w.toWorld(35.2)).toBeCloseTo(17.6, 6);
    expect(w.toWorld(35.31)).toBeCloseTo(17.6, 6);
    expect(w.toWorld(88.31)).toBeCloseTo(70.6, 6);
    expect(w.toWorld(100)).toBeCloseTo(70.6 + 100 - 88.42, 6);
    // Inside a hit-stop toSong returns the song time the hit-stop began.
    expect(w.toSong(17.6)).toBeCloseTo(35.2, 6);
    expect(w.toSong(50)).toBeCloseTo(35.31 + (50 - 17.6), 6);
    expect(w.hitStops()).toEqual([
      [35.2, 35.31],
      [88.31, 88.42],
    ]);
  });
  it("never decreases", () => {
    const w = Warp.fromRates([{ s: 0, rate: 0.5 }, { s: 3, rate: 0 }, { s: 3.1, rate: 0.4 }]);
    let prev = -Infinity;
    for (let s = 0; s < 10; s += 0.01) {
      const x = w.toWorld(s);
      expect(x).toBeGreaterThanOrEqual(prev);
      prev = x;
    }
  });
});

describe("geometry and randomness", () => {
  it("samples points inside shapes and projects points into them", () => {
    const shapes: Shape[] = [
      { kind: "arc", x: 0, y: 0, radius: 56, dir: 1, halfAngle: Math.PI / 3 },
      { kind: "circle", x: 10, y: 5, radius: 14 },
      { kind: "capsule", x0: 0, y0: 0, x1: 100, y1: 20, radius: 22 },
      { kind: "ring", x: 0, y: 0, inner: 100, outer: 200 },
    ];
    const r = new Rng(1);
    for (const s of shapes) {
      for (let i = 0; i < 200; i++) {
        const p = sampleInShape(s, () => r.next(), 2);
        expect(shapeContains(s, p.x, p.y)).toBe(true);
        const q = nearestInside(s, r.range(-300, 300), r.range(-300, 300));
        expect(shapeContains(s, q.x, q.y, 1e-6)).toBe(true);
      }
    }
  });
  it("is deterministic", () => {
    expect(hash32(1, "a")).toBe(hash32(1, "a"));
    expect(new Rng(5, "x").next()).toBe(new Rng(5, "x").next());
    expect(new Rng(5, "x").next()).not.toBe(new Rng(6, "x").next());
  });
});
