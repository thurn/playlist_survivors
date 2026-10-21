/**
 * The song schedule: everything the game may know in advance because it owns
 * the song. Timing grids, accents, the base time scale, the route pace,
 * level-ups and the finale are resolved here before the simulation starts.
 * Nothing in it depends on a press.
 */
import { smoothstep, lerp } from "../core/shaping.ts";
import { barOfBeatIndex, beatIndexAt, beatIndexOf, timeOfBeatIndex } from "../core/songmap.ts";
import { beatDurationAt, sectionIndexAt, subdivisionBeats, type Ctx } from "../planner/context.ts";

export interface GridSlot {
  s: number;
  /** Fractional beat index. */
  bi: number;
  bar: number;
  /** Position in its bar, in grid steps. */
  slot: number;
  section: number;
  /** Strength of an accent within 40 ms (0 if none), and its label. */
  accent: number;
  accentLabel?: string;
  /**
   * grid: a step section's timing grid (presses arm swings for these);
   * auto: a free section's automatic swing, every two beats;
   * anchor: a strong song-map anchor in a free section, where the sword cleaves.
   */
  kind: "grid" | "auto" | "anchor";
}

export interface Schedule {
  /** Each section's timing grid. */
  grids: GridSlot[][];
  /** Every section's slots in order: swings strike on these (the step grids, automatic swings and anchors). */
  slots: GridSlot[];
  strikeTimes: Float64Array;
  /** The step sections' timing grids joined end to end: presses are rated against these and arm them. */
  gridSlots: GridSlot[];
  gridTimes: Float64Array;
  /** Base world rate (section time scales, timeScale events), as constant steps from song time 0. */
  baseSteps: { s: number; rate: number }[];
  /** Route distance of the pace point, sampled every 1/PACE_RATE song seconds. */
  pace: Float64Array;
  levelUps: { id: string; s: number }[];
  blast?: { s: number; weapon: string };
}

export const PACE_RATE = 120;
/** Section paces blend with this time constant (world seconds). */
const PACE_EASE = 0.3;

/** Free movement: the sword swings by itself this often (beats). */
export const AUTO_SWING_BEATS = 2;
/** Free movement: anchors at least this strong get a cleave. */
export const ANCHOR_CLEAVE = 0.5;

/** A free section's strike times: an automatic swing every two beats, and cleaves on strong anchors. */
function freeGrid(ctx: Ctx, i: number): GridSlot[] {
  const sec = ctx.sections[i];
  const { idx, song } = ctx;
  const out: GridSlot[] = [];
  const anchors = (sec.song.anchors ?? []).filter((a) => a.strength >= ANCHOR_CLEAVE && a.time >= sec.s0 && a.time < sec.s1);
  for (const a of anchors) {
    const bi = beatIndexAt(song, a.time);
    out.push({ s: a.time, bi, bar: barOfBeatIndex(idx, bi), slot: 0, section: i, accent: a.strength, kind: "anchor" });
  }
  for (let bi = sec.b0 + AUTO_SWING_BEATS; bi < sec.b1 - 0.5; bi += AUTO_SWING_BEATS) {
    // An anchor's cleave replaces the automatic swing near it.
    if (anchors.some((a) => Math.abs(beatIndexAt(song, a.time) - bi) < 1)) continue;
    out.push({ s: timeOfBeatIndex(song, bi), bi, bar: barOfBeatIndex(idx, bi), slot: 0, section: i, accent: 0, kind: "auto" });
  }
  return out.sort((a, b) => a.s - b.s);
}

/** Grid times of one section on its timing grid, with accents matched within 40 ms. */
function sectionGrid(ctx: Ctx, i: number): GridSlot[] {
  const sec = ctx.sections[i];
  if (sec.level.movement === "free") return freeGrid(ctx, i);
  const { idx, song } = ctx;
  const out: GridSlot[] = [];
  for (let bar = sec.song.bars[0]; bar <= sec.song.bars[1]; bar++) {
    const bpb = idx.beatsPerBar[bar] ?? 2;
    const step = subdivisionBeats(sec.level.timingGrid, bpb);
    const n = Math.max(1, Math.round(bpb / step));
    const b0 = beatIndexOf(idx, { bar, beat: 1 });
    for (let k = 0; k < n; k++) {
      const bi = b0 + k * step;
      if (bi > song.beats.length - 1) continue;
      out.push({ s: timeOfBeatIndex(song, bi), bi, bar, slot: k, section: i, accent: 0, kind: "grid" });
    }
  }
  for (const a of song.accents) {
    let best: GridSlot | undefined;
    let bestD = 0.04;
    for (const sl of out) {
      const d = Math.abs(sl.s - a.time);
      if (d <= bestD) {
        best = sl;
        bestD = d;
      }
    }
    if (best && a.strength > best.accent) {
      best.accent = a.strength;
      best.accentLabel = a.label;
    }
  }
  return out;
}

/** Base world rate at song time s: section time scales blended over the beat before each section, plus timeScale events. */
function baseRateFn(ctx: Ctx): (s: number) => number {
  const secs = ctx.sections;
  const sectionRate = (i: number, s: number) => {
    const sec = secs[i];
    const ts = sec.level.timeScale;
    const u = (s - sec.s0) / Math.max(1e-6, sec.s1 - sec.s0);
    return lerp(ts.from, ts.to, Math.min(1, Math.max(0, u)));
  };
  const events = ctx.events.filter((e) => e.ev.timeScale !== undefined).map((e) => ({ s: e.s, rate: e.ev.timeScale! }));
  return (s: number): number => {
    const i = sectionIndexAt(ctx, s);
    let r = sectionRate(i, s);
    const next = secs[i + 1];
    if (next) {
      const bd = beatDurationAt(ctx, next.s0 - 1e-3);
      const u = (s - (next.s0 - bd)) / bd;
      if (u > 0) r = lerp(r, sectionRate(i + 1, next.s0), smoothstep(u));
    }
    for (const ev of events) {
      if (ev.s > s || ev.s < secs[i].s0) continue;
      const bd = beatDurationAt(ctx, ev.s);
      r = lerp(r, ev.rate, smoothstep((s - ev.s) / bd));
    }
    return r;
  };
}

/** The base rate as constant steps: ramps are approximated by short steps. */
function baseSteps(ctx: Ctx): { s: number; rate: number }[] {
  const secs = ctx.sections;
  const rate = baseRateFn(ctx);
  const cuts = new Set<number>([0]);
  const addSteps = (a: number, b: number, n: number) => {
    for (let k = 0; k <= n; k++) cuts.add(a + ((b - a) * k) / n);
  };
  for (let i = 0; i < secs.length; i++) {
    const sec = secs[i];
    cuts.add(sec.s0);
    if (sec.level.timeScale.from !== sec.level.timeScale.to) {
      const beats = beatIndexAt(ctx.song, sec.s1) - beatIndexAt(ctx.song, sec.s0);
      addSteps(sec.s0, sec.s1, Math.max(4, Math.ceil(beats * 4)));
    }
    const next = secs[i + 1];
    if (next) {
      const bd = beatDurationAt(ctx, next.s0 - 1e-3);
      addSteps(next.s0 - bd, next.s0, 16);
    }
  }
  for (const e of ctx.events) if (e.ev.timeScale !== undefined) addSteps(e.s, e.s + beatDurationAt(ctx, e.s), 16);
  const times = [...cuts].filter((t) => t >= 0 && t <= ctx.duration + 1).sort((a, b) => a - b);
  const out: { s: number; rate: number }[] = [];
  for (let k = 0; k < times.length; k++) {
    const a = times[k];
    const b = k + 1 < times.length ? times[k + 1] : a + 1;
    const r = Math.round(rate((a + b) / 2) * 1e6) / 1e6;
    if (out.length > 0 && out[out.length - 1].rate === r) continue;
    out.push({ s: a, rate: r });
  }
  return out;
}

/** Rate of a step function at song time s. */
export function stepRate(steps: { s: number; rate: number }[], s: number): number {
  let lo = 0;
  let hi = steps.length - 1;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (steps[mid].s <= s) lo = mid;
    else hi = mid - 1;
  }
  return steps[lo].rate;
}

/** Song time of the next step boundary after s (Infinity if none). */
export function nextStepAfter(steps: { s: number; rate: number }[], s: number): number {
  let lo = 0;
  let hi = steps.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (steps[mid].s <= s + 1e-12) lo = mid + 1;
    else hi = mid;
  }
  return lo < steps.length ? steps[lo].s : Infinity;
}

export function buildSchedule(ctx: Ctx): Schedule {
  const grids = ctx.sections.map((_, i) => sectionGrid(ctx, i));
  const slots = grids.flat().sort((a, b) => a.s - b.s);
  const steps = baseSteps(ctx);

  // The pace point: the planned route distance, from each section's pace. Song-owned,
  // so it ignores hit-stops; it follows the base time scale.
  const n = Math.ceil((ctx.duration + 2) * PACE_RATE) + 2;
  const pace = new Float64Array(n);
  const dt = 1 / PACE_RATE;
  let v = ctx.sections[0].level.pace;
  let d = 0;
  for (let k = 0; k < n; k++) {
    pace[k] = d;
    const s = k * dt;
    const rate = stepRate(steps, s);
    const target = ctx.sections[sectionIndexAt(ctx, s)].level.pace;
    v += (target - v) * Math.min(1, (dt * rate) / PACE_EASE);
    d += v * rate * dt;
  }

  const levelUps: Schedule["levelUps"] = [];
  let blast: Schedule["blast"];
  for (const e of ctx.events) {
    if (e.ev.levelUp) levelUps.push({ id: e.ev.levelUp, s: e.s });
    if (e.ev.blast) blast = { s: e.s, weapon: e.ev.blast };
  }
  levelUps.sort((a, b) => a.s - b.s);
  const gridSlots = slots.filter((x) => x.kind === "grid");
  return { grids, slots, strikeTimes: Float64Array.from(slots, (x) => x.s), gridSlots, gridTimes: Float64Array.from(gridSlots, (x) => x.s), baseSteps: steps, pace, levelUps, blast };
}

/** Route distance of the pace point at song time s. */
export function paceAt(sch: Schedule, s: number): number {
  const p = sch.pace;
  const f = Math.max(0, s * PACE_RATE);
  const k = Math.min(p.length - 2, Math.floor(f));
  return p[k] + (p[k + 1] - p[k]) * Math.min(1, f - k);
}
