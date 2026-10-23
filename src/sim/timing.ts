/**
 * Timing and the groove meter: each press in step movement is rated against the section's timing
 * grid and classed (see "Timing and the Groove" in docs/design/gameplay-redesign.md). The groove
 * sets the tier, and the tier the sword's reach and every attack's damage.
 */
import type { HeroDef } from "../content/schema.ts";
import { clamp } from "../core/shaping.ts";
import type { InputKey } from "./movement.ts";

export type Rating = "perfect" | "great" | "good";

export interface PressRating {
  /** Press time minus the nearest grid time, in song seconds. */
  delta: number;
  /** The rating's name, or undefined for presses outside the Good window. */
  rating?: Rating;
}

/** Rate a press `delta` song seconds from the nearest grid time: Perfect, Great, Good, or none beyond the Good window. */
export function ratePress(hero: HeroDef, delta: number): PressRating {
  const t = hero.timing;
  const ms = Math.abs(delta) * 1000;
  if (ms <= t.perfectMs) return { delta, rating: "perfect" };
  if (ms <= t.greatMs) return { delta, rating: "great" };
  if (ms <= t.goodMs) return { delta, rating: "good" };
  return { delta };
}

/** Index of the grid time nearest s in a sorted list (binary search). */
export function nearestIndex(times: ArrayLike<number>, s: number): number {
  let lo = 0;
  let hi = times.length - 1;
  if (hi < 0) return -1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (times[mid] <= s) lo = mid;
    else hi = mid;
  }
  return Math.abs(times[hi] - s) < Math.abs(times[lo] - s) ? hi : lo;
}

/** Rate a press at song time s against a sorted grid. */
export function ratePressOnGrid(hero: HeroDef, s: number, grid: ArrayLike<number>): PressRating & { gridIndex: number } {
  const i = nearestIndex(grid, s);
  return { ...ratePress(hero, s - grid[i]), gridIndex: i };
}

// ------------------------------------------------------- grid lookups

/** Index of the first grid time at or after s (grid.length if none). */
export function firstAtOrAfter(grid: ArrayLike<number>, s: number): number {
  let lo = 0;
  let hi = grid.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (grid[mid] < s - 1e-9) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

// ------------------------------------------------- press classes and the groove meter
// See "Timing and the Groove" in docs/design/gameplay-redesign.md. Pure functions of the presses
// so far, so a recorded log can be re-rated.

/** A counted press's rating: beyond the Good window it is loose. */
export type CountedRating = Rating | "loose";

/**
 * What a press is, judged against the grid. A partner belongs to the press before it (a
 * diagonal's second key, a stomp's opposite key); an off-beat press is near the half-way point
 * after a grid time whose counted press was Good or better; a counted press is the first for its
 * nearest grid time; a double tap comes soon after the counted press; anything else is stray.
 */
export type PressClass = "partner" | "offbeat" | "counted" | "double" | "stray";

export interface PressJudgement {
  class: PressClass;
  /** Counted presses: the rating (loose beyond Good); off-beat presses: Perfect or Great. */
  rating?: CountedRating;
  /** Offset (song seconds, after the drift) from the nearest grid time, or for an off-beat press from the off-beat. */
  delta: number;
  /** The grid time the press counts for (counted, double tap and stray presses) or follows (off-beat). */
  gridIndex: number;
  /** The groove change it makes. */
  change: number;
  /** The drift taken off the press's time before it was judged (song seconds, + for a late player). */
  drift: number;
}

/** The keys that drift together: the directions, and the stomp key (Space or Z), which a thumb or a long key may press later. */
export type KeyGroup = "direction" | "stomp";
export const keyGroup = (key: InputKey): KeyGroup => (key === "space" ? "stomp" : "direction");

/**
 * What the classes remember of earlier presses: each grid time's counted press (at its own time,
 * before the drift), the off-beats taken, and each key group's latest counted offsets (song
 * seconds, oldest first: the drift).
 */
export interface PressMemo {
  counted: Map<number, { s: number; rating: CountedRating }>;
  offbeats: Set<number>;
  recent: Record<KeyGroup, number[]>;
}

export const freshPressMemo = (): PressMemo => ({ counted: new Map(), offbeats: new Set(), recent: { direction: [], stomp: [] } });

/**
 * The drift: how late a key group's presses have been landing, so a player who runs steadily late
 * (a slow key, or a song that speeds up) is judged against their own beat. The median of the
 * group's latest counted offsets, once there are enough of them, less a dead zone (a median within
 * it is a few presses' scatter, not lateness), within ±maxMs; 0 before. Only earlier presses feed
 * it, so a recording re-rates the same.
 */
export function driftOf(hero: HeroDef, memo: PressMemo, group: KeyGroup): number {
  const d = hero.meter.drift;
  const r = memo.recent[group];
  if (r.length < Math.max(1, d.minPresses)) return 0;
  const sorted = [...r].sort((a, b) => a - b);
  const m = sorted.length >> 1;
  const median = sorted.length % 2 ? sorted[m] : (sorted[m - 1] + sorted[m]) / 2;
  const beyond = Math.sign(median) * Math.max(0, Math.abs(median) - d.deadMs / 1000);
  return clamp(beyond, -d.maxMs / 1000, d.maxMs / 1000);
}

/** A counted press's rating from its offset to the grid, with the hero's timing windows. */
export function countedRating(hero: HeroDef, delta: number): CountedRating {
  const ms = Math.abs(delta) * 1000;
  const t = hero.timing;
  return ms <= t.perfectMs ? "perfect" : ms <= t.greatMs ? "great" : ms <= t.goodMs ? "good" : "loose";
}

/**
 * Class a press at song time s (after the calibration offset) against a sorted grid, in the
 * design's order, and remember what it takes. `partner`: the press bends or rocks the one before.
 * The press is judged at s less its key group's drift. `keyOf` maps a grid index to the key the
 * memory keeps it by (the index itself, unless the grid changes from press to press: reactive mode's
 * settled judgment keeps beats by identity).
 */
export function classifyPress(hero: HeroDef, grid: ArrayLike<number>, s: number, partner: boolean, memo: PressMemo, group: KeyGroup = "direction", keyOf: (gi: number) => number = (gi) => gi): PressJudgement {
  const m = hero.meter;
  const drift = driftOf(hero, memo, group);
  const sj = s - drift;
  const gi = nearestIndex(grid, sj);
  if (partner) return { class: "partner", delta: sj - grid[gi], gridIndex: gi, change: 0, drift };
  // The off-beat nearest the press: half-way between the grid time at or before it and the next.
  let i = gi;
  if (grid[i] > sj && i > 0) i--;
  const near = (k: number) => (k >= 0 && k + 1 < grid.length ? Math.abs(sj - (grid[k] + grid[k + 1]) / 2) : Infinity);
  if (near(i - 1) < near(i)) i--;
  if (i >= 0 && i + 1 < grid.length) {
    const off = sj - (grid[i] + grid[i + 1]) / 2;
    const counted = memo.counted.get(keyOf(i));
    if (Math.abs(off) * 1000 <= hero.timing.greatMs && !memo.offbeats.has(keyOf(i)) && counted && counted.rating !== "loose") {
      memo.offbeats.add(keyOf(i));
      const rating = Math.abs(off) * 1000 <= hero.timing.perfectMs ? "perfect" : "great";
      return { class: "offbeat", rating, delta: off, gridIndex: i, change: m.offbeat[rating], drift };
    }
  }
  const delta = sj - grid[gi];
  const counted = memo.counted.get(keyOf(gi));
  if (!counted) {
    const rating = countedRating(hero, delta);
    memo.counted.set(keyOf(gi), { s, rating });
    // A loose press is too far off to say how late the player runs (it may be a slip to the next beat).
    if (rating !== "loose") {
      const r = memo.recent[group];
      r.push(s - grid[gi]);
      if (r.length > m.drift.presses) r.shift();
    }
    return { class: "counted", rating, delta, gridIndex: gi, change: m.counted[rating], drift };
  }
  if (s - counted.s <= m.doubleTapMs / 1000 + 1e-9) return { class: "double", delta, gridIndex: gi, change: m.doubleTap, drift };
  return { class: "stray", delta, gridIndex: gi, change: m.stray, drift };
}

/**
 * Never late: a sound plays exactly on its slot only when it is known at least
 * this long before it; one known later moves to a later slot of its subdivision.
 */
export const NEVER_LATE = 0.06;

/** The groove after a change: on its 0-to-1 scale. */
export const grooveAfterChange = (groove: number, change: number): number => clamp(groove + change, 0, 1);

/** The drain over one beat of a bar of `beatsPerBar` beats: full to empty in drain.emptyInBars bars. */
export const drainPerBeat = (hero: HeroDef, beatsPerBar: number): number => 1 / (hero.meter.drain.emptyInBars * Math.max(1, beatsPerBar));

/**
 * The tier for a groove, from the tier before: it rises when the groove reaches a threshold, and
 * drops only when the groove falls `hysteresis` below one.
 */
export function tierFor(hero: HeroDef, tier: number, groove: number): number {
  const th = hero.meter.tiers;
  let t = tier;
  while (t < th.length && groove >= th[t] - 1e-9) t++;
  while (t > 0 && groove < th[t - 1] - hero.meter.hysteresis - 1e-9) t--;
  return t;
}
