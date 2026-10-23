/**
 * Play mode's `?debug=moves` overlay: movement and the groove by the numbers,
 * for Manual QA (docs/design/gameplay-redesign.md). It shows the hero's speed
 * over the last second, the stride budget, the groove and tier, and the last
 * eight presses with their class and offset. It reads the plan (a live
 * recording's, as drawn) and never changes the simulation.
 */
import { grooveAt } from "../plan/evaluate.ts";
import { heroSample } from "../plan/sample.ts";
import { partnerPress, type Plan } from "../plan/types.ts";
import { heldAt, heldSpans, strideLimitAt, stridePeriodAt } from "../planner/checks.ts";
import { stompCancelsUntil } from "../sim/strides.ts";
import { Warp } from "../core/warp.ts";

export interface MovesPress {
  s: number;
  key: string;
  /** The press class (absent for presses outside step movement), and a counted press's rating ("loose" beyond Good). */
  cls?: string;
  rating?: string;
  /** Offset in ms: from the off-beat for an off-beat press, else from the nearest grid time. */
  deltaMs?: number;
}

export interface MovesReadout {
  s: number;
  /** The distance the hero covered over about the last second of world time (a whole number of stride periods), per second. */
  speed: number;
  /**
   * The stride budget (px): the ground built up since the latest stride started, at the walk
   * speed, up to one stride, less a free-section walk's spending. (Estimated from the plan: it
   * leaves out ground an interrupted stride gave back.)
   */
  budget?: number;
  groove: number;
  tier?: number;
  /** The last eight presses, oldest first. */
  presses: MovesPress[];
}

/** Index of the time nearest s in a sorted list. */
function nearest(times: ArrayLike<number>, s: number): number {
  let lo = 0;
  let hi = times.length - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (times[mid] <= s) lo = mid;
    else hi = mid;
  }
  return Math.abs(times[hi] - s) < Math.abs(times[lo] - s) ? hi : lo;
}

/** The union of intervals, sorted and merged. */
function union(spans: [number, number][]): [number, number][] {
  const out: [number, number][] = [];
  for (const [a, b] of [...spans].sort((x, y) => x[0] - y[0])) {
    const last = out[out.length - 1];
    if (last && a <= last[1]) last[1] = Math.max(last[1], b);
    else if (b > a) out.push([a, b]);
  }
  return out;
}

/** Intervals clipped to [lo, hi]. */
const clip = (spans: [number, number][], lo: number, hi: number): [number, number][] =>
  spans.map(([a, b]): [number, number] => [Math.max(a, lo), Math.min(b, hi)]).filter(([a, b]) => b > a);

/** The length of a union of intervals, less where it overlaps another union. */
function measure(spans: [number, number][], minus: [number, number][]): number {
  let total = 0;
  for (const [a, b] of spans) {
    total += b - a;
    for (const [c, d] of minus) total -= Math.max(0, Math.min(b, d) - Math.max(a, c));
  }
  return total;
}

/** The readout at song time s. */
export function movesReadout(plan: Plan, s: number): MovesReadout {
  const warp = new Warp(plan.warp);
  const w = warp.toWorld(s);
  const t = plan.hero;
  const presses = plan.inputs.filter((i) => i.kind === "press" && i.s <= s + 1e-9);
  const movementAt = (at: number) => (plan.sections.findLast((x) => at >= x.s0 - 1e-9) ?? plan.sections[0]).movement;

  // Speed: the path walked over a whole number of stride periods, about a second, up to now. A
  // steady walk (held, or tapped on the grid) then covers whole strides whatever the phase.
  const period = stridePeriodAt(plan, s);
  const from = Math.max(0, w - Math.max(1, Math.round(1 / period)) * period);
  let covered = 0;
  let prev = heroSample(plan, from);
  for (let k = Math.floor(from * t.rate) + 1; k / t.rate < w && k < t.x.length; k++) {
    covered += Math.hypot(t.x[k] - prev.x, t.y[k] - prev.y);
    prev = { ...prev, x: t.x[k], y: t.y[k] };
  }
  const last = heroSample(plan, w);
  covered += Math.hypot(last.x - prev.x, last.y - prev.y);
  const speed = w - from > 1e-6 ? covered / (w - from) : 0;

  // The budget: the ground built up since the latest stride started (a press that strides, or a
  // step of the stride period while a key is held, unless a stomp cancelled it), up to one stride.
  // A walk in a free section spends the refill as it comes.
  let budget: number | undefined;
  const st = plan.strides;
  if (st) {
    const spans = heldSpans(plan.inputs.filter((i) => i.s <= s + 1e-9));
    const steps = { s: st.steps, period: st.periods, per: st.per ?? new Uint8Array(st.steps.length).fill(1) };
    const cancels = presses.filter((p) => p.stomp && movementAt(p.s) === "steps").map((p) => [p.s, stompCancelsUntil(steps, p.s)]);
    let startS = -Infinity;
    for (let k = presses.length - 1; k >= 0; k--) {
      if (partnerPress(presses[k]) || presses[k].key === "space") continue;
      startS = presses[k].s;
      break;
    }
    // (Only a step in the last second can be the latest: a stride period later the budget is full.)
    for (const step of st.steps) {
      if (step > s + 1e-9) break;
      if (step > Math.max(startS, s - 1) && heldAt(spans, step) && !cancels.some(([a, b]) => a <= step + 1e-9 && step <= b + 1e-9)) startS = step;
    }
    const startW = Number.isFinite(startS) ? warp.toWorld(startS) : 0;
    // Free sections: from the stride's landing, the walk spends the refill while a key is held, but
    // not while a stride or a stomp's stamp is in progress (the walk waits for it to land).
    let walked = 0;
    if (movementAt(s) === "free") {
      const held = union(spans.map(([a, b]): [number, number] => [warp.toWorld(a), Number.isFinite(b) ? warp.toWorld(b) : w]));
      const paused = union(presses.filter((p) => !partnerPress(p)).map((p): [number, number] => [p.w, p.w + st.durationS]));
      walked = measure(clip(held, startW + st.durationS, w), paused);
    }
    budget = Math.min(strideLimitAt(plan, s), st.speed * Math.max(0, w - startW - walked));
  }

  const grid = plan.grid;
  const recent: MovesPress[] = presses.slice(-8).map((i) => {
    const p: MovesPress = { s: i.s, key: i.key };
    if (i.class) p.cls = i.class;
    if (i.class === "counted") p.rating = i.rating ?? "loose";
    if (i.class === "offbeat") {
      const k = nearest(grid, i.s);
      const a = grid[k] <= i.s ? k : k - 1;
      if (a >= 0 && a + 1 < grid.length) p.deltaMs = (i.s - (grid[a] + grid[a + 1]) / 2) * 1000;
      p.rating = i.offbeat;
    } else if (i.class && grid.length > 0) p.deltaMs = (i.delta ?? i.s - grid[nearest(grid, i.s)]) * 1000;
    return p;
  });

  let k = 0;
  while (k + 1 < plan.tier.s.length && plan.tier.s[k + 1] <= s + 1e-9) k++;
  return { s, speed, ...(budget !== undefined ? { budget } : {}), groove: grooveAt(plan.groove, s), tier: plan.tier.v[k], presses: recent };
}

/** The readout as lines of text. */
export function movesLines(r: MovesReadout): string[] {
  const lines = [
    `MOVES  s=${r.s.toFixed(2)}`,
    `speed   ${r.speed.toFixed(0)} px/s (last 1 s)`,
    r.budget !== undefined ? `budget  ${r.budget.toFixed(1)} px` : "budget  -",
    `groove  ${r.groove.toFixed(2)}${r.tier !== undefined ? `   tier ${r.tier}` : ""}`,
    "presses:",
  ];
  for (const p of [...r.presses].reverse()) {
    const d = p.deltaMs !== undefined ? `${p.deltaMs >= 0 ? "+" : ""}${p.deltaMs.toFixed(0)} ms${p.cls === "offbeat" ? " off" : ""}` : "";
    lines.push(` ${p.s.toFixed(2)} ${p.key.padEnd(5)} ${(p.cls ?? "-").padEnd(7)} ${(p.rating ?? "").padEnd(7)} ${d}`);
  }
  return lines;
}

/** The overlay: a small text panel in the top-right corner, refreshed each frame drawn. */
export class DebugMovesPanel {
  private readonly el: HTMLPreElement;
  private text = "";

  constructor(parent: HTMLElement = document.body) {
    this.el = document.createElement("pre");
    this.el.dataset.debug = "moves";
    this.el.style.cssText =
      "position:fixed;top:8px;right:8px;margin:0;padding:8px 10px;background:#05040acc;color:#d8e0ff;font:12px/1.35 ui-monospace,Menlo,monospace;z-index:20;pointer-events:none;white-space:pre;border:1px solid #ffe08055;border-radius:4px;";
    parent.appendChild(this.el);
  }

  update(r: MovesReadout): void {
    const text = movesLines(r).join("\n");
    if (text !== this.text) this.el.textContent = this.text = text;
  }

  destroy(): void {
    this.el.remove();
  }
}
