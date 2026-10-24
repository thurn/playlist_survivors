/**
 * The reactive feel rounds' arithmetic (docs/design/reactive-feel.md, "Raters", "Rounds" and
 * "Held-out Protocol"): raters' scores, batches, a round's aggregate (means, contradictions with the
 * paired metrics, calibration on the anchor, rater noise, the issue list), a milestone's numbers-only
 * export, and the overfitting rule. Pure functions; scripts/reactive-feel.ts reads and writes the files.
 */
import { z } from "zod";
import { hash32 } from "../../src/core/rng.ts";
import type { TrackMetrics } from "./feel.ts";

export const DIMENSIONS = ["M1", "M2", "M3", "M4", "A1", "A2", "A3", "A4", "A5", "A6"] as const;
export type Dimension = (typeof DIMENSIONS)[number];
/** The anchor's dimensions that must score above 3, or its rater's batch is re-rated. */
export const CALIBRATION: Dimension[] = ["M1", "M2", "A2", "A4"];
export const ANCHOR = "anchor-orpheus-galop";

const score = z.number().int().min(1).max(5);
const IssueSchema = z.object({ dim: z.enum(DIMENSIONS), t: z.number(), evidence: z.string().min(1), issue: z.string().min(1), area: z.string().min(1) });
const ScoresSchema = z.object({
  rater: z.string().min(1),
  tracks: z.record(z.string(), z.object({ scores: z.object(Object.fromEntries(DIMENSIONS.map((d) => [d, score])) as Record<Dimension, typeof score>), issues: z.array(IssueSchema).max(3) })),
});
export type Scores = z.infer<typeof ScoresSchema>;
export type Issue = z.infer<typeof IssueSchema>;

/** A rater's scores.json, checked: all ten dimensions per track, scores 1 to 5, at most 3 issues. */
export function parseScores(json: unknown): Scores {
  return ScoresSchema.parse(json);
}

/**
 * The round's batches: about `size` tracks each, in a fixed mixed order (by a hash of the id, so
 * NotJam and CC tracks share batches for comparing identities), the anchor in the first.
 */
export function batches(ids: readonly string[], size = 5, anchor: string | null = ANCHOR): string[][] {
  const order = [...ids].filter((id) => id !== anchor).sort((a, b) => (hash32(0xfee1, a) >>> 0) - (hash32(0xfee1, b) >>> 0) || a.localeCompare(b));
  const all = anchor ? [anchor, ...order] : order;
  const n = Math.max(1, Math.round(all.length / size));
  const out: string[][] = Array.from({ length: n }, () => []);
  all.forEach((id, i) => out[Math.floor((i * n) / all.length)].push(id));
  return out;
}

/** Each dimension's paired metric for the contradiction check, higher better (null where none applies or it is n/a). */
export function pairedMetric(dim: Dimension, m: TrackMetrics): number | null {
  switch (dim) {
    case "M1":
      return m.beat.strictF;
    case "M2":
      return m.reward.damageRatio;
    case "M3":
      return m.fairness.judgedBeatDriven;
    case "M4":
      // (Pacing is several numbers, none of them a score: no contradiction check.)
      return null;
    case "A1":
      return m.coverage.n;
    case "A2":
      return m.visualSync.beatTier;
    case "A3":
      return null;
    case "A4":
      return m.structure.followed;
    case "A5":
      return m.hierarchy.spearman;
    case "A6":
      return -m.breakdowns.percussiveSounds;
  }
}

/** One rater's batch: its name, whether it is a second rating, and its scores. */
export interface RatedBatch {
  name: string;
  second: boolean;
  scores: Scores;
}

export interface Contradiction {
  id: string;
  dim: Dimension;
  score: number;
  metric: number;
  quartile: "bottom" | "top";
}

export interface RoundSummary {
  round: number;
  /** Per dimension, the mean over the iteration tracks (the anchor apart), a second rating averaged in. */
  means: Record<Dimension, number | null>;
  tracks: Record<string, { scores: Record<Dimension, number>; first: Record<Dimension, number>; second?: Record<Dimension, number>; metrics: Record<Dimension, number | null> }>;
  anchor?: { scores: Record<Dimension, number>; calibrated: boolean; low: Dimension[]; rater: string };
  contradictions: Contradiction[];
  /** Per dimension, the mean absolute difference between first and second ratings, over the tracks rated twice. */
  noise: { n: number; dims: Record<Dimension, number | null> };
  issues: (Issue & { id: string; rater: string })[];
  /** The round's metric means over the iteration set (from npm run reactive:corpus), for the Appendix and the stopping rule. */
  metricMeans?: Record<string, number | null>;
}

const mean = (xs: readonly number[]) => (xs.length ? xs.reduce((a, x) => a + x, 0) / xs.length : null);
const r3 = (x: number | null) => (x === null ? null : Math.round(x * 1000) / 1000);
const byDim = <T>(f: (d: Dimension) => T) => Object.fromEntries(DIMENSIONS.map((d) => [d, f(d)])) as Record<Dimension, T>;

/** The round's aggregate from its rated batches and each track's metrics. */
export function aggregate(
  round: number,
  rated: readonly RatedBatch[],
  metrics: Readonly<Record<string, TrackMetrics>>,
  anchor = ANCHOR,
  metricMeans?: Readonly<Record<string, unknown>>,
): RoundSummary {
  const first = new Map<string, { scores: Record<Dimension, number>; rater: string }>();
  const second = new Map<string, Record<Dimension, number>>();
  const issues: RoundSummary["issues"] = [];
  for (const b of rated)
    for (const [id, t] of Object.entries(b.scores.tracks)) {
      // (A re-rating replaces its batch's scores.json: a track rated twice the same way is a mistake.)
      if ((b.second ? second : first).has(id)) throw new Error(`${id} has two ${b.second ? "second" : "first"} ratings (the second in ${b.name}): a re-rating replaces its batch's scores.json`);
      if (b.second) second.set(id, t.scores);
      else {
        first.set(id, { scores: t.scores, rater: b.scores.rater });
        for (const issue of t.issues) issues.push({ ...issue, id, rater: b.scores.rater });
      }
    }
  const tracks: RoundSummary["tracks"] = {};
  for (const [id, f] of first) {
    const s = second.get(id);
    tracks[id] = {
      first: f.scores,
      ...(s ? { second: s } : {}),
      scores: s ? byDim((d) => (f.scores[d] + s[d]) / 2) : f.scores,
      metrics: byDim((d) => (metrics[id] ? pairedMetric(d, metrics[id]) : null)),
    };
  }
  const rated2 = Object.entries(tracks).filter(([id]) => id !== anchor);
  const means = byDim((d) => r3(mean(rated2.map(([, t]) => t.scores[d]))));
  // Contradictions: a 4 or 5 with the metric in the round's bottom quartile, a 1 or 2 with it in the top. Each
  // quartile is cut by value at the quarter of the tracks with a metric (ties at the cut all in or all out),
  // and a side is skipped where its cut equals the median (heavily tied metrics).
  const contradictions: Contradiction[] = [];
  for (const d of DIMENSIONS) {
    const ranked = rated2.filter(([, t]) => t.metrics[d] !== null).sort((x, y) => x[1].metrics[d]! - y[1].metrics[d]!);
    if (ranked.length < 4) continue;
    const vals = ranked.map(([, t]) => t.metrics[d]!);
    const q = Math.max(1, Math.floor(vals.length / 4));
    const low = vals[q - 1];
    const high = vals[vals.length - q];
    const median = vals[Math.floor((vals.length - 1) / 2)];
    for (const [id, t] of ranked) {
      const m = t.metrics[d]!;
      if (low < median && m <= low && t.first[d] >= 4) contradictions.push({ id, dim: d, score: t.first[d], metric: m, quartile: "bottom" });
      if (high > median && m >= high && t.first[d] <= 2) contradictions.push({ id, dim: d, score: t.first[d], metric: m, quartile: "top" });
    }
  }
  const twice = Object.values(tracks).filter((t) => t.second);
  const noise = { n: twice.length, dims: byDim((d) => r3(mean(twice.map((t) => Math.abs(t.first[d] - t.second![d]))))) };
  const a = first.get(anchor);
  const low = a ? calibrationLow(a.scores) : [];
  return {
    round,
    means,
    tracks,
    ...(a ? { anchor: { scores: a.scores, calibrated: low.length === 0, low, rater: a.rater } } : {}),
    contradictions,
    noise,
    issues,
    ...(metricMeans ? { metricMeans: numbersOf(metricMeans) } : {}),
  };
}

/** Each numeric value rounded to 0.001, anything else null. */
function numbersOf(o: Readonly<Record<string, unknown>>): Record<string, number | null> {
  return Object.fromEntries(Object.entries(o).map(([k, v]) => [k, typeof v === "number" && Number.isFinite(v) ? Math.round(v * 1000) / 1000 : null]));
}

/**
 * A milestone's export: only numbers (the held-out track count, the dimension means, the metric means,
 * and the anchor's scores, which are not held out). No ids, issues or prose. (Milestones take no second
 * rating; milestone 0 doesn't either, so the gap's baseline is built the same way.)
 */
export interface MilestoneNumbers {
  milestone: number;
  round: number;
  tracks: number;
  dimensions: Record<Dimension, number | null>;
  metrics: Record<string, number | null>;
  anchor: Record<Dimension, number> | null;
}

export function milestoneNumbers(milestone: number, round: number, rated: readonly RatedBatch[], metricMeans: Readonly<Record<string, unknown>>, anchor = ANCHOR): MilestoneNumbers {
  const firsts = rated.filter((b) => !b.second).flatMap((b) => Object.entries(b.scores.tracks));
  const scores = firsts.filter(([id]) => id !== anchor).map(([, t]) => t.scores);
  const a = firsts.find(([id]) => id === anchor)?.[1].scores;
  return { milestone, round, tracks: scores.length, dimensions: byDim((d) => r3(mean(scores.map((s) => s[d])))), metrics: numbersOf(metricMeans), anchor: a ? byDim((d) => a[d]) : null };
}

/** The anchor's calibration: the dimensions where it scored 3 or less (empty when calibrated). */
export const calibrationLow = (scores: Record<Dimension, number>): Dimension[] => CALIBRATION.filter((d) => scores[d] <= 3);

/** The overfitting rule: per dimension, the iteration-minus-held-out gap's growth since milestone 0. */
export function overfitting(
  iterNow: Record<Dimension, number | null>,
  heldNow: Record<Dimension, number | null>,
  iterBase: Record<Dimension, number | null>,
  heldBase: Record<Dimension, number | null>,
): { growth: Record<Dimension, number | null>; meanGrowth: number | null; trips: boolean; reasons: string[] } {
  const growth = byDim((d) => {
    const vals = [iterNow[d], heldNow[d], iterBase[d], heldBase[d]];
    if (vals.some((v) => v === null)) return null;
    const [a, b, c, e] = vals as number[];
    return r3(a - b - (c - e));
  });
  const known = DIMENSIONS.map((d) => growth[d]).filter((x): x is number => x !== null);
  const meanGrowth = r3(mean(known));
  const reasons = [
    ...DIMENSIONS.filter((d) => (growth[d] ?? -Infinity) >= 0.75 - 1e-9).map((d) => `${d}'s gap grew by ${growth[d]}`),
    ...(meanGrowth !== null && meanGrowth >= 0.4 - 1e-9 ? [`the mean gap grew by ${meanGrowth}`] : []),
  ];
  return { growth, meanGrowth, trips: reasons.length > 0, reasons };
}
