/** The reactive feel metrics' arithmetic (scripts/lib/feel-metrics.ts) on synthetic inputs. */
import { describe, expect, it } from "vitest";
import {
  anyLevelF,
  beatDriven,
  beatF,
  beatMatches,
  beatPulse,
  changeTimes,
  committedLevel,
  levelGrids,
  lockIn,
  longestRunShare,
  onSlots,
  peaksOnTargets,
  reactsToBeat,
  spanMeans,
  spearman,
  strikeDamage,
  structureMatch,
} from "../scripts/lib/feel-metrics.ts";

const grid = Array.from({ length: 100 }, (_, i) => 1 + 0.5 * i);

describe("beat metrics", () => {
  it("scores committed beats against the grid, one to one", () => {
    expect(
      beatF(
        grid.map((t) => t + 0.02),
        grid,
      ),
    ).toBe(1);
    expect(
      beatF(
        grid.map((t) => t + 0.05),
        grid,
      ),
    ).toBe(0);
    // Every beat twice over (double time): precision halves, recall stays.
    const doubled = grid.flatMap((t) => [t, t + 0.25]);
    expect(beatF(doubled, grid)).toBeCloseTo((2 * 0.5 * 1) / 1.5, 2);
    // Two committed beats near one grid point match it once.
    expect(beatMatches([1, 1.01], [1, 1.5])).toEqual([true, false]);
  });

  it("finds the best metrical level", () => {
    const doubled = grid.flatMap((t) => [t, t + 0.25]);
    expect(anyLevelF(doubled, grid)).toBeCloseTo(1, 6);
    const offPhaseHalf = grid.filter((_, i) => i % 2 === 1);
    expect(beatF(offPhaseHalf, grid)).toBeLessThan(0.7);
    expect(anyLevelF(offPhaseHalf, grid)).toBeCloseTo(1, 6);
    const levels = levelGrids(grid);
    expect(levels[1].length).toBe(2 * grid.length - 1);
    expect(levels[3][0]).toBe(grid[1]);
  });

  it("measures the longest correct run and the lock-in", () => {
    const beats = grid;
    const hits = beats.map((_, i) => i >= 10 && i !== 50);
    expect(lockIn(beats, hits)).toBe(beats[17]);
    expect(
      lockIn(
        beats,
        beats.map(() => false),
      ),
    ).toBeUndefined();
    expect(longestRunShare(beats, hits, 50)).toBeCloseTo((beats[99] - beats[51]) / 50, 6);
  });

  it("puts sounds on the reference's sixteenths", () => {
    expect(onSlots([1, 1.125, 1.25, 1.3], grid, 4, 0.02)).toBe(0.75);
    expect(onSlots([], grid)).toBe(0);
  });
});

describe("visual metrics", () => {
  const rate = 240;
  const times = Array.from({ length: rate * 40 }, (_, i) => 1 + i / rate);
  it("tells a series that follows the beat from noise", () => {
    const follows = beatPulse(times, grid).map((p) => (Number.isNaN(p) ? 0 : p));
    let seed = 7;
    const noise = times.map(() => ((seed = (seed * 16807) % 2147483647) / 2147483647) * 2 - 1);
    expect(reactsToBeat(follows, times, grid, () => true).reacts).toBe(true);
    expect(reactsToBeat(noise, times, grid, () => true).reacts).toBe(false);
    // A squash landing a quarter beat late still reacts by its periodicity.
    const late = beatPulse(
      times.map((t) => t - 0.125),
      grid,
    ).map((p) => (Number.isNaN(p) ? 0 : p));
    expect(reactsToBeat(late, times, grid, () => true).fold).toBeGreaterThan(1);
  });

  it("finds peaks on the beats within a frame", () => {
    const pulse = beatPulse(times, grid).map((p) => (Number.isNaN(p) ? 0 : p));
    // (A peak just after each beat: the pulse is 1 at the beat itself.)
    expect(peaksOnTargets(pulse, times, grid).share).toBeGreaterThan(0.95);
    const shifted = beatPulse(
      times.map((t) => t - 0.1),
      grid,
    ).map((p) => (Number.isNaN(p) ? 0 : p));
    expect(peaksOnTargets(shifted, times, grid).share).toBeLessThan(0.05);
  });

  it("matches visible changes to section starts", () => {
    expect(structureMatch([10, 30, 60], [12, 33, 45, 80])).toEqual({ followed: 2, of: 3, stray: 2 });
    expect(changeTimes([0, 1, 2, 3, 4, 5], [0, 0, 1, 1, 2, 2])).toEqual([2, 4]);
    expect(changeTimes([0, 0.5, 1, 1.5], [0, 1, 2, 3], 1e-6, 1)).toEqual([0.5, 1.5]);
  });

  it("averages over spans and ranks loudness against intensity", () => {
    expect(spanMeans([0, 1, 2, 3, 4], [1, 1, 3, 3, 9], [0, 2, 4, 6])).toEqual([1, 3, 9]);
    expect(spearman([1, 2, 3, 4], [10, 20, 30, 40])).toBeCloseTo(1, 9);
    expect(spearman([1, 2, 3, 4], [4, 3, 2, 1])).toBeCloseTo(-1, 9);
    expect(spearman([1, 2, 3, 4], [1, 1, 1, 1])).toBe(0);
  });
});

describe("the reward and the level", () => {
  it("counts the player's own strikes, capped at each enemy's remaining health", () => {
    const hits = [{ source: "move" }, { source: "ring" }, { source: "weapon" }, { source: "wave" }];
    const enemies = [
      {
        type: "goblin",
        fates: [
          { hit: 0, damage: 2 },
          { hit: 2, damage: 5 },
        ],
      },
      { type: "goblin", fates: [{ hit: 1, damage: 1000 }] },
      {
        type: "orc",
        fates: [
          { hit: 3, damage: 1 },
          { hit: 0, damage: 1 },
        ],
      },
    ];
    const health = (t: string) => (t === "goblin" ? 3 : 10);
    // 2 + min(5, 1) from the first goblin; the ring's 1000 not at all; 1 + 1 from the orc.
    expect(strikeDamage(enemies, hits, health, () => true)).toBe(5);
    // Only the hits kept (judged ones, say).
    expect(strikeDamage(enemies, hits, health, (h) => h !== 0)).toBe(2);
  });

  it("takes the level committed beats run at from their period", () => {
    const offBeat = grid.map((t) => t + 0.25);
    expect(committedLevel(offBeat, grid)).toBe(0);
    expect(beatMatches(offBeat, levelGrids(grid)[committedLevel(offBeat, grid)]).every((h) => !h)).toBe(true);
    expect(
      committedLevel(
        grid.flatMap((t) => [t, t + 0.25]),
        grid,
      ),
    ).toBe(1);
    expect(
      committedLevel(
        grid.filter((_, i) => i % 2 === 1),
        grid,
      ),
    ).toBe(3);
    expect(committedLevel(levelGrids(grid)[4], grid)).toBe(4);
  });

  it("finds the beat-driven stretches by onset regularity", () => {
    const g = Array.from({ length: 40 }, (_, i) => 0.5 * i);
    // Onsets on the beats for the first half, then scattered off every slot.
    const onsets = [...g.slice(0, 20).flatMap((t) => [t, t + 0.25]), ...Array.from({ length: 30 }, (_, k) => 10.06 + 0.31 * k)];
    const driven = beatDriven(g, onsets);
    expect(driven.slice(0, 14).every(Boolean)).toBe(true);
    expect(driven.slice(26).some(Boolean)).toBe(false);
  });
});
