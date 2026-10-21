/**
 * Timing and groove: each press in step movement is rated against the
 * section's timing grid, and the groove meter tracks recent accuracy. Timing
 * raises damage a little and never punishes: there is no miss rating.
 */
import type { HeroDef } from "../content/schema.ts";
import { clamp, lerp } from "../core/shaping.ts";

export type Rating = "perfect" | "great" | "good";

export interface PressRating {
  /** Press time minus the nearest grid time, in song seconds. */
  delta: number;
  /** The rating's name, or undefined for presses outside the Good window. */
  rating?: Rating;
  /** Accuracy from the timing floor (0.1) to 1. */
  accuracy: number;
}

/** Accuracy no later than this far from the grid reaches the floor, unless half a grid step is shorter. */
const FLOOR_AT = 0.25;

/**
 * Rate a press `delta` song seconds from the nearest grid time, on a grid
 * whose step is `gridStep` song seconds.
 *
 *   |delta| ms   rating     accuracy
 *   0 - 35       Perfect    1.0
 *   35 - 70      Great      1.0 -> 0.75 (linear)
 *   70 - 120     Good       0.75 -> 0.4
 *   120 and up   (none)     0.4 -> floor, reached at 250 ms or half a grid step
 */
export function ratePress(hero: HeroDef, delta: number, gridStep: number): PressRating {
  const t = hero.timing;
  const ms = Math.abs(delta) * 1000;
  if (ms <= t.perfectMs) return { delta, rating: "perfect", accuracy: 1 };
  if (ms <= t.greatMs) return { delta, rating: "great", accuracy: lerp(1, 0.75, (ms - t.perfectMs) / (t.greatMs - t.perfectMs)) };
  if (ms <= t.goodMs) return { delta, rating: "good", accuracy: lerp(0.75, 0.4, (ms - t.greatMs) / (t.goodMs - t.greatMs)) };
  const end = Math.min(FLOOR_AT, gridStep / 2) * 1000;
  if (end <= t.goodMs) return { delta, accuracy: t.floor };
  return { delta, accuracy: lerp(0.4, t.floor, clamp((ms - t.goodMs) / (end - t.goodMs), 0, 1)) };
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
  const prev = grid[Math.max(0, i - 1)];
  const next = grid[Math.min(grid.length - 1, i + 1)];
  const step = i > 0 && i < grid.length - 1 ? Math.min(grid[i] - prev, next - grid[i]) : Math.max(grid[i] - prev, next - grid[i]);
  return { ...ratePress(hero, s - grid[i], step > 0 ? step : 0.5), gridIndex: i };
}

/** The groove after a rated press: it moves part of the way toward the press's accuracy. */
export function grooveAfterPress(hero: HeroDef, groove: number, accuracy: number): number {
  return groove + hero.groove.follow * (accuracy - groove);
}

/** The groove after `bars` bars in step movement with no presses: it halves every idleHalfLifeBars. */
export function grooveDecay(hero: HeroDef, groove: number, bars: number): number {
  return groove * Math.pow(0.5, Math.max(0, bars) / hero.groove.idleHalfLifeBars);
}

/** All hero damage is multiplied by this: 1.0 at no groove up to 1 + maxBonus. */
export function damageMultiplier(hero: HeroDef, groove: number): number {
  return 1 + hero.groove.maxBonus * clamp(groove, 0, 1);
}

// ------------------------------------------------------- when the sword strikes

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

/**
 * A press arms the sword: the swing strikes on the first grid time at least
 * minLeadMs after the press. Returns that grid index (grid.length if none).
 */
export function armedStrike(hero: HeroDef, grid: ArrayLike<number>, sPress: number): number {
  return firstAtOrAfter(grid, sPress + hero.swings.minLeadMs / 1000);
}

/**
 * Song time the swing's wind-up starts: windupMs before the strike, or at the
 * press when the press came later than that (the wind-up then plays faster).
 */
export function windupStart(hero: HeroDef, sArmed: number, sStrike: number): number {
  return Math.max(sArmed, sStrike - hero.swings.windupMs / 1000);
}
