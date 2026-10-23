/**
 * Fixed-speed strides (see "Movement: Fixed-Speed
 * Strides" in docs/design/gameplay-redesign.md). The hero moves at one speed
 * whatever the tempo or press rate: ground builds up in a budget as world time
 * passes, never above one stride, and each stride spends all of it. Pressing
 * more often makes strides shorter, never the hero faster.
 *
 * - A press strides at once; a held key strides on every step of the stride
 *   period (the grid times, subdivided where the grid is slow).
 * - Any other press during a stride stops it where it is: the ground it had
 *   not covered returns to the budget.
 * - Without a steady beat, holding walks at the walk speed, and the walk
 *   spends the refill as it comes.
 *
 * These are pure functions of a small state the simulation carries. Times are
 * world seconds (grid times are song seconds) and distances world pixels.
 */
import type { HeroDef } from "../content/schema.ts";
import { KEY_DIR, NO_WALLS, type Clamp, type Key, type Step } from "./movement.ts";

export interface StrideRule {
  /** The walk speed: px per world second. */
  speed: number;
  durationS: number;
  hop: number;
  maxPeriodS: number;
  freePeriodS: number;
  /** Two opposite keys this close together (s) make one stomp. */
  stompS: number;
}

export function strideRule(hero: HeroDef): StrideRule {
  return { speed: hero.walk.speed, durationS: hero.stride.durationMs / 1000, hop: hero.stride.hop, maxPeriodS: hero.stride.maxPeriodS, freePeriodS: hero.stride.freePeriodS, stompS: hero.stride.stompMs / 1000 };
}

/** The steps a held key strides on (song times), the stride period at each, and how many steps its grid step has. */
export interface StrideSteps {
  s: Float64Array;
  period: Float64Array;
  per: Uint8Array;
}

/**
 * Every grid time, plus equal subdivisions of each grid step longer than
 * maxPeriodS: the step is halved until it is not (a 78 beats-per-minute beat
 * grid strides on every eighth note). The last grid time keeps the step before it.
 */
export function strideSteps(grid: ArrayLike<number>, maxPeriodS: number): StrideSteps {
  const s: number[] = [];
  const period: number[] = [];
  const per: number[] = [];
  for (let i = 0; i < grid.length; i++) {
    const g0 = grid[i];
    const len = i + 1 < grid.length ? grid[i + 1] - g0 : i > 0 ? g0 - grid[i - 1] : maxPeriodS;
    let n = 1;
    while (len / n > maxPeriodS + 1e-9) n *= 2;
    for (let k = 0; k < n; k++) {
      s.push(g0 + (k * len) / n);
      period.push(len / n);
      per.push(n);
    }
  }
  return { s: Float64Array.from(s), period: Float64Array.from(period), per: Uint8Array.from(per) };
}

/** The stride period at song time s in a section with a steady beat: the period of the step it falls in. */
export function periodAt(steps: StrideSteps, s: number): number {
  const t = steps.s;
  if (t.length === 0) return 0;
  if (s < t[0]) return steps.period[0];
  let lo = 0;
  let hi = t.length - 1;
  while (hi > lo) {
    const mid = (lo + hi + 1) >> 1;
    if (t[mid] <= s + 1e-9) lo = mid;
    else hi = mid - 1;
  }
  return steps.period[lo];
}

/**
 * Song time of the last held step a stomp at song time s cancels: the held strides of one grid
 * step, from the first step at or after s. That is one held stride, or two where the stride period
 * at the stomp is halved.
 */
export function stompCancelsUntil(steps: StrideSteps, s: number): number {
  const t = steps.s;
  let j = 0;
  while (j < t.length && t[j] < s - 1e-9) j++;
  if (j >= t.length) return s;
  const at = t[j] <= s + 1e-9 ? j : Math.max(0, j - 1);
  return t[Math.min(t.length - 1, j + steps.per[at] - 1)];
}

/**
 * The direction of the held keys, in the order they were pressed: of two
 * opposite keys the more recent wins, and two perpendicular keys make a
 * diagonal. Undefined when no key is held.
 */
export function heldDirection(held: Iterable<Key>): { x: number; y: number } | undefined {
  let x = 0;
  let y = 0;
  for (const k of held) {
    const d = KEY_DIR[k];
    if (d.x !== 0) x = d.x;
    else y = d.y;
  }
  const l = Math.hypot(x, y);
  return l > 0 ? { x: x / l, y: y / l } : undefined;
}

/** What the simulation carries for strides. */
export interface StrideState {
  /** The ground the hero may still cover (px), as of world time `w`. */
  budget: number;
  w: number;
  /** World time from which a walk spends the refill (free sections), or Infinity. */
  walkFrom: number;
  /** A stomp cancels the held strides up to this song time. */
  stompUntil: number;
}

export const freshStrides = (): StrideState => ({ budget: 0, w: 0, walkFrom: Infinity, stompUntil: -Infinity });

/** The budget at world time w (at or after st.w): refilled at the walk speed, never above one stride. */
export function budgetAt(rule: StrideRule, st: StrideState, w: number, oneStride: number): number {
  const until = Math.min(w, Math.max(st.w, st.walkFrom));
  return Math.min(oneStride, st.budget + rule.speed * Math.max(0, until - st.w));
}

/** Bring the budget up to world time w. */
export function settle(rule: StrideRule, st: StrideState, w: number, oneStride: number): void {
  st.budget = budgetAt(rule, st, w, oneStride);
  st.w = w;
}

/** Is the stride still moving at world time w? */
export const striding = (rule: StrideRule, stride: Step | undefined, w: number): boolean => stride !== undefined && w < stride.w0 + rule.durationS - 1e-9;

/**
 * Stop a stride at world time w where it is, at (x, y): the ground it had not
 * covered goes back to the budget (still at most one stride). Returns that
 * ground: the stride's length less how far the hero is from where it started
 * (a bent stride curves, so its progress along the path would overstate it).
 */
export function interrupt(rule: StrideRule, st: StrideState, stride: Step, w: number, x: number, y: number, oneStride: number, walkBack = 0): number {
  settle(rule, st, w, oneStride);
  // (A rock step that returns past the stride's start, to its press's, pays for that walk too.)
  const back = Math.hypot(stride.ex, stride.ey) - Math.hypot(x - stride.x0, y - stride.y0) - walkBack;
  if (back <= 0) return 0;
  const before = st.budget;
  st.budget = Math.min(oneStride, st.budget + back);
  return st.budget - before;
}

/** Start a stride at world time w from (x, y) toward the unit vector dir: it spends the whole budget. */
export function startStride(rule: StrideRule, st: StrideState, w: number, x: number, y: number, dir: { x: number; y: number }, oneStride: number, opts: { key?: Key; held: boolean }, walls: Clamp = NO_WALLS): Step {
  settle(rule, st, w, oneStride);
  const len = st.budget;
  st.budget = 0;
  const len1 = walls(x, y, dir.x, dir.y, len);
  return { w0: w, x0: x, y0: y, dx: dir.x, dy: dir.y, len1, ex: dir.x * len1, ey: dir.y * len1, keys: opts.key ? [opts.key] : [], len, held: opts.held };
}
