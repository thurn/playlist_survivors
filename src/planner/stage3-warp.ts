/** Stage 3: build the time warp from the chosen actions, then the route schedule. */
import { energyAt, beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import { smoothstep, lerp } from "../core/shaping.ts";
import { Warp } from "../core/warp.ts";
import { beatDurationAt, sectionIndexAt, type Ctx } from "./context.ts";

export const HITSTOP_CLEARANCE = 0.05;
export const HITSTOP_MAX = 0.15;
export const HITSTOP_MIN = 0.04;
const DIP_RATE = 0.4;
const DIP_RAMP = 0.04;
const DIP_HOLD = 0.07;

/** Song times of every primary event: hero actions and near misses. */
export function primaryTimes(ctx: Ctx): number[] {
  const t = ctx.actions.map((a) => a.s);
  for (const th of ctx.threats) t.push(th.sMiss);
  return t.sort((a, b) => a - b);
}

export interface HitStop {
  s0: number;
  s1: number;
  action: number;
}

export function planHitStops(ctx: Ctx): HitStop[] {
  const prim = primaryTimes(ctx);
  const out: HitStop[] = [];
  for (const a of ctx.actions) {
    const m = a.move;
    if (m.weight !== "heavy" || !m.hitStopMs || a.kind === "blast") continue;
    const accentFactor = 0.6 + 0.4 * a.accent;
    const wanted = (m.hitStopMs / 1000) * accentFactor;
    const next = prim.find((t) => t > a.s + 1e-6);
    const gap = next === undefined ? Infinity : next - a.s;
    const len = Math.min(wanted, gap - HITSTOP_CLEARANCE, HITSTOP_MAX);
    if (len < HITSTOP_MIN) continue;
    out.push({ s0: a.s, s1: a.s + len, action: a.id });
  }
  return out;
}

export function stage3(ctx: Ctx): void {
  const { song } = ctx;
  const secs = ctx.sections;
  const hitStops = planHitStops(ctx);

  const sectionRate = (i: number, s: number) => {
    const sec = secs[i];
    const ts = sec.level.timeScale;
    const u = (s - sec.s0) / Math.max(1e-6, sec.s1 - sec.s0);
    return lerp(ts.from, ts.to, Math.min(1, Math.max(0, u)));
  };
  const events = [...ctx.timeScaleEvents].sort((a, b) => a.s - b.s);
  /** Base rate: section time scales, blended into the next section over the beat before it, plus timeScale events. */
  const baseRate = (s: number): number => {
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
      const u = (s - ev.s) / bd;
      r = lerp(r, ev.rate, smoothstep(u));
    }
    return r;
  };
  const dips = ctx.threats.map((t) => t.sMiss);
  const dipRate = (s: number): number => {
    let r = 1;
    for (const m of dips) {
      const d = Math.abs(s - m);
      if (d < DIP_HOLD / 2) r = Math.min(r, DIP_RATE);
      else if (d < DIP_HOLD / 2 + DIP_RAMP) r = Math.min(r, lerp(DIP_RATE, 1, (d - DIP_HOLD / 2) / DIP_RAMP));
    }
    return r;
  };

  // Breakpoint candidates.
  const cuts = new Set<number>([0]);
  const addSteps = (a: number, b: number, n: number) => {
    for (let k = 0; k <= n; k++) cuts.add(a + ((b - a) * k) / n);
  };
  for (let i = 0; i < secs.length; i++) {
    const sec = secs[i];
    cuts.add(sec.s0);
    if (sec.level.timeScale.from !== sec.level.timeScale.to) {
      const beats = beatIndexAt(song, sec.s1) - beatIndexAt(song, sec.s0);
      addSteps(sec.s0, sec.s1, Math.max(4, Math.ceil(beats * 4)));
    }
    const next = secs[i + 1];
    if (next) {
      const bd = beatDurationAt(ctx, next.s0 - 1e-3);
      addSteps(next.s0 - bd, next.s0, 16);
    }
  }
  for (const ev of events) addSteps(ev.s, ev.s + beatDurationAt(ctx, ev.s), 16);
  for (const m of dips) {
    addSteps(m - DIP_HOLD / 2 - DIP_RAMP, m - DIP_HOLD / 2, 4);
    addSteps(m + DIP_HOLD / 2, m + DIP_HOLD / 2 + DIP_RAMP, 4);
  }
  for (const h of hitStops) {
    cuts.add(h.s0);
    cuts.add(h.s1);
  }
  const times = [...cuts].filter((t) => t >= 0 && t <= ctx.duration + 1).sort((a, b) => a - b);
  const steps: { s: number; rate: number }[] = [];
  for (let k = 0; k < times.length; k++) {
    const a = times[k];
    const b = k + 1 < times.length ? times[k + 1] : a + 1;
    const mid = (a + b) / 2;
    const inStop = hitStops.some((h) => mid >= h.s0 && mid < h.s1);
    const rate = inStop ? 0 : Math.min(baseRate(mid), dipRate(mid));
    steps.push({ s: a, rate: Math.round(rate * 1e6) / 1e6 });
  }
  const warp = Warp.fromRates(steps);
  ctx.warp = warp;

  // Secondary events inside a hit-stop move to the next slot of their subdivision.
  const taken = new Set(ctx.firings.map((f) => `${f.weapon}@${f.bi.toFixed(4)}`));
  const moved: typeof ctx.firings = [];
  for (const f of ctx.firings) {
    const hs = hitStops.find((h) => f.s >= h.s0 - 1e-9 && f.s < h.s1);
    if (!hs) {
      moved.push(f);
      continue;
    }
    let bi = f.bi;
    while (timeOfBeatIndex(song, bi) < hs.s1) bi += f.step;
    const key = `${f.weapon}@${bi.toFixed(4)}`;
    if (taken.has(key)) continue;
    taken.add(key);
    const s = timeOfBeatIndex(song, bi);
    moved.push({ ...f, bi, s, energy: energyAt(song, s) });
  }
  ctx.firings = moved.sort((a, b) => a.s - b.s || a.weapon.localeCompare(b.weapon));

  for (const a of ctx.actions) a.w = warp.toWorld(a.s);
  for (const f of ctx.firings) f.w = warp.toWorld(f.s);
  ctx.worldBeats = Float64Array.from(song.beats, (s) => warp.toWorld(s));

  // Route schedule: progress = ∫ travelSpeed dw, sampled at routeRate Hz of world time.
  const wEnd = warp.toWorld(ctx.duration) + 1;
  const n = Math.ceil(wEnd * ctx.routeRate) + 2;
  const prog = new Float64Array(n);
  const dt = 1 / ctx.routeRate;
  let v = 0;
  let p = 0;
  for (let k = 0; k < n; k++) {
    prog[k] = p;
    const w = k * dt;
    const s = warp.toSong(w);
    const sec = secs[sectionIndexAt(ctx, s)];
    let target = sec.moveset.travelSpeed ?? 40;
    if (sec.moveset.locomotion?.mode === "glide") target *= 0.5 + energyAt(song, s);
    // Ease between section speeds (time constant 0.3 world-seconds).
    v += (target - v) * Math.min(1, dt / 0.3);
    p += v * dt;
  }
  ctx.routeProgress = prog;
}

/** Route progress at world time w. */
export function progressAt(ctx: Ctx, w: number): number {
  const prog = ctx.routeProgress!;
  const f = Math.max(0, w * ctx.routeRate);
  const k = Math.min(prog.length - 2, Math.floor(f));
  return prog[k] + (prog[k + 1] - prog[k]) * (f - k);
}
