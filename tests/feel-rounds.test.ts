/** The reactive feel rounds (scripts/lib/feel-rounds.ts) on fixture scores. */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { ROOT } from "../scripts/lib/files.ts";
import type { TrackMetrics } from "../scripts/lib/feel.ts";
import { aggregate, ANCHOR, batches, DIMENSIONS, milestoneNumbers, overfitting, parseScores, type Dimension, type RatedBatch } from "../scripts/lib/feel-rounds.ts";

const all = (v: number) => Object.fromEntries(DIMENSIONS.map((d) => [d, v])) as Record<Dimension, number>;
const with_ = (v: number, over: Partial<Record<Dimension, number>>) => ({ ...all(v), ...over });

/** A track's metrics with the paired ones set (the rest never read by the rounds). */
function metrics(id: string, beatF: number, coverage = 6): TrackMetrics {
  return {
    id,
    beat: { strictF: beatF },
    reward: { damageRatio: 1.2 },
    fairness: { judgedBeatDriven: 0.9 },
    structure: { followed: 0.7 },
    coverage: { n: coverage },
    visualSync: { beatTier: 0.4 },
    hierarchy: { spearman: 0.1 },
    breakdowns: { percussiveSounds: 0 },
  } as unknown as TrackMetrics;
}

const issue = (dim: Dimension, text: string) => ({ dim, t: 12.5, evidence: "honest-vs-masher.png, row 2", issue: text, area: "weapon scale" });

function batch(name: string, tracks: Record<string, { scores: Record<Dimension, number>; issues?: ReturnType<typeof issue>[] }>, second = false): RatedBatch {
  return { name, second, scores: parseScores({ rater: name, tracks: Object.fromEntries(Object.entries(tracks).map(([id, t]) => [id, { scores: t.scores, issues: t.issues ?? [] }])) }) };
}

describe("raters' scores", () => {
  it("need all ten dimensions, whole scores from 1 to 5 and at most three issues", () => {
    expect(() => parseScores({ rater: "b", tracks: { a: { scores: all(3), issues: [] } } })).not.toThrow();
    expect(() => parseScores({ rater: "b", tracks: { a: { scores: { M1: 3 }, issues: [] } } })).toThrow();
    expect(() => parseScores({ rater: "b", tracks: { a: { scores: with_(3, { A2: 6 }), issues: [] } } })).toThrow();
    expect(() => parseScores({ rater: "b", tracks: { a: { scores: with_(3, { A2: 2.5 }), issues: [] } } })).toThrow();
    expect(() => parseScores({ rater: "b", tracks: { a: { scores: all(3), issues: [1, 2, 3, 4].map(() => issue("M1", "x")) } } })).toThrow();
  });

  it("are batched about five to a batch, the anchor in the first, the same each time", () => {
    const ids = Array.from({ length: 29 }, (_, i) => `track-${i}`);
    const b = batches(ids);
    expect(b.length).toBe(6);
    expect(b[0][0]).toBe(ANCHOR);
    expect(b.flat().sort()).toEqual([...ids, ANCHOR].sort());
    expect(Math.max(...b.map((x) => x.length)) - Math.min(...b.map((x) => x.length))).toBeLessThanOrEqual(1);
    expect(batches(ids)).toEqual(b);
    // (Without an anchor, as given.)
    expect(batches(ids, 5, null).flat().sort()).toEqual([...ids].sort());
  });
});

describe("a round's aggregate", () => {
  const ids = ["a", "b", "c", "d", "e"];
  const m = Object.fromEntries([...ids.map((id, i) => [id, metrics(id, [0.1, 0.5, 0.6, 0.7, 0.95][i])]), [ANCHOR, metrics(ANCHOR, 0.99)]]);
  const first = batch("batch-1", { [ANCHOR]: { scores: with_(5, { A2: 3 }) }, a: { scores: with_(3, { M1: 5 }), issues: [issue("M2", "strikes look alike")] }, b: { scores: all(3) } });
  const second = batch("batch-2", { c: { scores: all(4) }, d: { scores: all(2) }, e: { scores: with_(2, { M1: 1 }) } });
  const again = batch("second-1", { a: { scores: with_(3, { M1: 3, A1: 5 }) } }, true);

  it("means each dimension over the iteration tracks, a second rating averaged in", () => {
    const s = aggregate(0, [first, second], m);
    expect(s.means.M2).toBeCloseTo((3 + 3 + 4 + 2 + 2) / 5, 6);
    const t = aggregate(0, [first, second, again], m);
    expect(t.tracks.a.scores.M1).toBe(4);
    expect(t.means.M1).toBeCloseTo((4 + 3 + 4 + 2 + 1) / 5, 6);
    expect(t.noise).toEqual({ n: 1, dims: { ...Object.fromEntries(DIMENSIONS.map((d) => [d, 0])), M1: 2, A1: 2 } });
  });

  it("flags scores that contradict their paired metric's quartile", () => {
    const s = aggregate(0, [first, second], m);
    // a: 5 on beat lock with the round's lowest beat F-measure; e: 1 with the highest.
    expect(s.contradictions.filter((c) => c.dim === "M1")).toEqual([
      { id: "a", dim: "M1", score: 5, metric: 0.1, quartile: "bottom" },
      { id: "e", dim: "M1", score: 1, metric: 0.95, quartile: "top" },
    ]);
    // (A dimension whose metric is the same everywhere has no quartiles to contradict.)
    expect(s.contradictions.some((c) => c.dim === "M3")).toBe(false);
  });

  it("cuts the quartiles by value, so tied metrics are flagged all or none", () => {
    // Coverage 8 on four tracks, 3 on one: the top quartile's cut equals the median, so no top side;
    // the bottom quartile is the 3 alone.
    const ties = Object.fromEntries(ids.map((id, i) => [id, metrics(id, 0.5, i === 0 ? 3 : 8)]));
    const s = aggregate(0, [batch("batch-1", Object.fromEntries(ids.map((id, i) => [id, { scores: with_(3, { A1: i === 0 ? 5 : 1 }) }])))], ties);
    expect(s.contradictions.filter((c) => c.dim === "A1")).toEqual([{ id: "a", dim: "A1", score: 5, metric: 3, quartile: "bottom" }]);
  });

  it("refuses a track rated twice the same way", () => {
    expect(() => aggregate(0, [first, batch("batch-9", { a: { scores: all(3) } })], m)).toThrow(/two first ratings/);
  });

  it("calibrates on the anchor: 3 or less on M1, M2, A2 or A4 re-rates its rater's batch", () => {
    const s = aggregate(0, [first, second], m);
    expect(s.anchor).toEqual({ scores: with_(5, { A2: 3 }), calibrated: false, low: ["A2"], rater: "batch-1" });
    expect(s.means.A2).toBeCloseTo(2.8, 6);
    expect(Object.keys(s.tracks)).toContain(ANCHOR);
    const ok = aggregate(0, [batch("batch-1", { [ANCHOR]: { scores: all(5) } })], m);
    expect(ok.anchor?.calibrated).toBe(true);
  });

  it("keeps every first rating's issues, with their track and rater", () => {
    expect(aggregate(0, [first, second, again], m).issues).toEqual([{ ...issue("M2", "strikes look alike"), id: "a", rater: "batch-1" }]);
  });
});

describe("a milestone's export", () => {
  it("holds numbers only: no track ids, issues or prose; the anchor apart", () => {
    const rated = [
      batch("batch-1", { [ANCHOR]: { scores: all(5) }, "held-1": { scores: all(4), issues: [issue("A3", "PROSE that must not leak")] }, "held-2": { scores: with_(2, { M1: 4 }) } }),
      batch("second-1", { "held-1": { scores: all(1) } }, true),
    ];
    const out = milestoneNumbers(0, 0, rated, { strictF: 0.81234, note: "words", coverage: 7 });
    expect(out).toEqual({ milestone: 0, round: 0, tracks: 2, dimensions: with_(3, { M1: 4 }), metrics: { strictF: 0.812, note: null, coverage: 7 }, anchor: all(5) });
    const json = JSON.stringify(out);
    for (const word of ["held-1", "held-2", "PROSE", "words", "batch", "evidence"]) expect(json).not.toContain(word);
    const leaves = (v: unknown): unknown[] => (v && typeof v === "object" ? Object.values(v).flatMap(leaves) : [v]);
    expect(leaves(out).every((x) => x === null || typeof x === "number")).toBe(true);
  });
});

describe("the overfitting rule", () => {
  const base = all(3);
  it("trips when a dimension's gap grows by 0.75 or the mean gap by 0.4", () => {
    expect(overfitting(all(3.5), all(3.5), base, base).trips).toBe(false);
    const one = overfitting(with_(3, { A3: 4 }), with_(3, { A3: 3.2 }), base, base);
    expect(one.trips).toBe(true);
    expect(one.reasons).toEqual(["A3's gap grew by 0.8"]);
    const spread = overfitting(all(3.5), all(3.05), base, base);
    expect(spread.meanGrowth).toBeCloseTo(0.45, 6);
    expect(spread.reasons).toEqual(["the mean gap grew by 0.45"]);
    // (A dimension n/a on either side is left out.)
    expect(overfitting({ ...all(3), A3: null }, all(3), base, base).growth.A3).toBeNull();
  });
});

describe("the rater brief", () => {
  it("ends with the reply line a milestone brief replaces (so a held-out rater's reply names no track or score)", () => {
    const brief = readFileSync(join(ROOT, "docs/feel/rater-brief.md"), "utf8");
    expect(brief).toContain("Then reply with one line per track: its id and its ten scores.");
    expect(readFileSync(join(ROOT, "scripts/reactive-feel.ts"), "utf8")).toContain('"Then reply with one line per track: its id and its ten scores."');
  });
});
