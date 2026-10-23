import { describe, expect, it } from "vitest";
import { Rng } from "../src/core/rng.ts";
import { candidates, chooseReading, READING, score, Settler, SETTLE_DELAY, type HindsightEntry, type ReadingName } from "../src/reactive/settle.ts";
import { loadContent } from "../src/content/loader.ts";
import { readContentFiles } from "../scripts/lib/files.ts";

const hero = loadContent(readContentFiles()).hero;

/** reactive-moderate's presses on beats every 0.5 s: play mode's moderate profile (30 ms spread, clamped at 100 ms, 85% of beats). */
function moderatePresses(beats: number[], seed = 7): number[] {
  const r = new Rng(seed, "presses");
  const out: number[] = [];
  for (const b of beats) {
    if (r.next() >= 0.85) continue;
    const u = Math.max(1e-12, r.next());
    const n = Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * r.next());
    out.push(b + Math.max(-0.1, Math.min(0.1, n * 0.03)));
  }
  return out.sort((a, b) => a - b);
}

/** Hindsight entries every second whose two grids are both `grid`, as in the design's examples. */
function entries(grid: number[], until: number): HindsightEntry[] {
  const out: HindsightEntry[] = [];
  for (let t = 1; t <= until; t++) {
    const keep = grid.filter((b) => b >= t - 8 && b <= t);
    out.push({ knownAt: t, offline: keep, online: keep });
  }
  return out;
}

/** Settle every press as the simulation does, 2 s after it. */
function settleAll(presses: number[], es: HindsightEntry[]) {
  const settler = new Settler();
  const results = presses.map((p) => {
    const entry = es.findLast((e) => e.knownAt <= p + SETTLE_DELAY + 1e-9)!;
    const window = presses.filter((q) => q !== p && q >= entry.knownAt - 8 && q <= entry.knownAt);
    return settler.settle(hero, { s: p, partner: false, group: "direction", unclear: false }, entry, window);
  });
  const readings = new Map<ReadingName, number>();
  for (const r of results.slice(12)) readings.set(r.reading.chosen.reading, (readings.get(r.reading.chosen.reading) ?? 0) + 1);
  const good = results.filter((r) => r.judgement?.class === "counted" && (r.judgement.rating === "perfect" || r.judgement.rating === "great")).length / results.length;
  const top = [...readings].sort((a, b) => b[1] - a[1])[0][0];
  return { top, good, results };
}

const beats = Array.from({ length: 120 }, (_, i) => 1 + 0.5 * i);
const presses = moderatePresses(beats);
const until = 62;

describe("the reading", () => {
  it("chooses double against a hindsight grid at half tempo, and settles 85% of presses Perfect or Great", () => {
    const { top, good } = settleAll(presses, entries(beats.filter((_, i) => i % 2 === 0), until));
    expect(top).toBe("double");
    expect(good).toBeGreaterThanOrEqual(0.85);
  });

  it("chooses the half reading that holds the player's beats against a grid at double tempo", () => {
    const double = beats.flatMap((b) => [b, b + 0.25]);
    const { top, good, results } = settleAll(presses, entries(double, until));
    expect(top === "half A" || top === "half B").toBe(true);
    // The chosen half holds the beats: its grid times are the player's.
    const last = results[results.length - 1].reading.chosen.beats;
    for (const b of last.slice(-4)) expect(beats.some((x) => Math.abs(x - b) < 1e-9)).toBe(true);
    expect(good).toBeGreaterThanOrEqual(0.85);
  });

  it("chooses off-beat against a grid shifted by half a beat", () => {
    const { top, good } = settleAll(presses, entries(beats.map((b) => b + 0.25), until));
    expect(top).toBe("off-beat");
    expect(good).toBeGreaterThanOrEqual(0.85);
  });

  it("keeps the grid as tracked for a player on it, and the incumbent on noise", () => {
    expect(settleAll(presses, entries(beats, until)).top).toBe("as tracked");
    // Random presses at 4 a second: no candidate beats the incumbent significantly.
    const r = new Rng(3, "mash");
    const mash: number[] = [];
    for (let t = 1; t < 60; t += 0.25 * r.range(0.7, 1.3)) mash.push(t);
    const { results } = settleAll(mash, entries(beats, until));
    const kept = results.filter((x) => x.reading.chosen.source === "offline" && x.reading.chosen.reading === "as tracked").length / results.length;
    expect(kept).toBeGreaterThanOrEqual(0.9);
  });
});

describe("the reading's arithmetic", () => {
  const entry: HindsightEntry = { knownAt: 10, offline: [2, 3, 4, 5, 6, 7, 8, 9], online: [2.5, 3.5] };
  it("offers ten candidates", () => {
    expect(candidates(entry).map((c) => `${c.source} ${c.reading}`)).toEqual(["offline as tracked", "offline off-beat", "offline double", "offline half A", "offline half B", "online as tracked", "online off-beat", "online double", "online half A", "online half B"]);
  });
  it("scores hits less chance, and keeps the incumbent with fewer than 8 presses", () => {
    const s = score(candidates(entry)[0], [2.01, 3.0, 4.5]);
    expect(s.hits).toBeCloseTo(2 / 3);
    expect(s.chance).toBeCloseTo(2 * READING.hitS * 1);
    expect(chooseReading(entry, [2.5, 3.5, 4.5], undefined).chosen.reading).toBe("as tracked");
  });
  it("finds the incumbent by its beats, not its label", () => {
    // The previous grid was the off-beats: the incumbent is the candidate holding them.
    const r = chooseReading(entry, [], [5.5, 6.5, 7.5]);
    expect(r.chosen.reading).toBe("off-beat");
    expect(r.switched).toBe(false);
  });
});

describe("beat identity", () => {
  it("blocks a second counted press for a beat after the reading changes", () => {
    const settler = new Settler();
    const asTracked: HindsightEntry = { knownAt: 12, offline: [6, 6.5, 7, 7.5, 8, 8.5, 9, 9.5, 10], online: [] };
    const first = settler.settle(hero, { s: 10.0, partner: false, group: "direction", unclear: false }, asTracked, []);
    expect(first.judgement?.class).toBe("counted");
    // A later grid holding 10.004 s at another index: the same beat, so a press at it is not counted
    // again (a memory keyed by grid index would count it).
    const later: HindsightEntry = { knownAt: 12.3, offline: [8.504, 9.004, 9.504, 10.004], online: [] };
    const again = settler.settle(hero, { s: 10.05, partner: false, group: "direction", unclear: false }, later, []);
    expect(again.reading.chosen.beats.indexOf(10.004)).not.toBe(8);
    expect(again.judgement?.class).not.toBe("counted");
  });

  it("settles unjudged without a beat near the press, or when the beat was unclear at it", () => {
    const settler = new Settler();
    const e: HindsightEntry = { knownAt: 12, offline: [2, 2.5, 3], online: [] };
    expect(settler.settle(hero, { s: 10, partner: false, group: "direction", unclear: false }, e, []).judgement).toBeUndefined();
    expect(settler.settle(hero, { s: 2.5, partner: false, group: "direction", unclear: true }, e, []).judgement).toBeUndefined();
  });
});
