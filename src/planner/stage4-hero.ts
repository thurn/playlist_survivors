/** Stage 4: plan hero motion, baked at 60 samples per world-second. */
import { routeFrame } from "../mapgen/mapgen.ts";
import { clamp, frac, lerp, smoothstep } from "../core/shaping.ts";
import { TAU, wrapAngle } from "../core/geometry.ts";
import { sectionIndexAt, type Ctx, type HeroAction, type SectionInfo } from "./context.ts";
import { progressAt } from "./stage3-warp.ts";
import type { HeroTrack } from "../plan/types.ts";

export const HERO_RATE = 60;
const CORRIDOR_MARGIN = 48;
/** How far ahead of the route point the boss's home sits. */
export const BOSS_HOME_AHEAD = 110;

export const HERO_ANIMS = ["idle", "walk", "run", "jump", "slash", "backslash", "halfslash", "thrust", "spellcast", "slash-oversize", "hurt"];

/** World beat index at world time w (interpolated over the world beat grid). */
export function worldBeatIndex(wb: Float64Array, w: number): number {
  const n = wb.length;
  if (w <= wb[0]) return (w - wb[0]) / (wb[1] - wb[0]);
  if (w >= wb[n - 1]) return n - 1 + (w - wb[n - 1]) / (wb[n - 1] - wb[n - 2]);
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (wb[mid] <= w) lo = mid;
    else hi = mid;
  }
  const span = wb[hi] - wb[lo];
  return span <= 0 ? lo : lo + (w - wb[lo]) / span;
}

export function worldTimeOfBeatIndex(wb: Float64Array, bi: number): number {
  const n = wb.length;
  if (bi <= 0) return wb[0] + bi * (wb[1] - wb[0]);
  if (bi >= n - 1) return wb[n - 1] + (bi - (n - 1)) * (wb[n - 1] - wb[n - 2]);
  const k = Math.floor(bi);
  return wb[k] + (bi - k) * (wb[k + 1] - wb[k]);
}

export function facingOf(dir: number): number {
  // 0 up, 1 left, 2 down, 3 right (atlas FACINGS order).
  const a = wrapAngle(dir);
  if (a > -Math.PI / 4 && a <= Math.PI / 4) return 3;
  if (a > Math.PI / 4 && a <= (3 * Math.PI) / 4) return 2;
  if (a <= -Math.PI / 4 && a > (-3 * Math.PI) / 4) return 0;
  return 1;
}

function pathOffset(sec: SectionInfo, wbi: number, bpb: number): [number, number] {
  const A = sec.moveset.pathSize ?? 40;
  switch (sec.moveset.path ?? "forward") {
    case "forward":
      return [0, 0];
    case "zigzag": {
      // Triangle wave, one leg per bar.
      const u = frac(wbi / (2 * bpb));
      const tri = u < 0.5 ? 4 * u - 1 : 3 - 4 * u;
      return [0, A * tri];
    }
    case "circle": {
      const th = (TAU * wbi) / 8;
      return [A * Math.cos(th) - A, A * Math.sin(th)];
    }
    case "figure-eight": {
      const th = (TAU * wbi) / 16;
      return [A * Math.sin(th), 0.7 * A * Math.sin(2 * th)];
    }
  }
}

function easeInOut(u: number): number {
  return smoothstep(u);
}

/** Spring relaxation from 1 to 0 over about two beats. */
function relax(tau: number, beat: number): number {
  if (tau <= 0) return 1;
  const k = 2 / beat;
  return (1 + k * tau) * Math.exp(-k * tau);
}

export interface HeroMotionInfo {
  /** Directions chosen per action. */
  dirs: number[];
}

export function bossHome(ctx: Ctx, w: number): { x: number; y: number } {
  const f = routeFrame({ step: ctx.map!.routeStep, x: ctx.map!.routeX, y: ctx.map!.routeY }, progressAt(ctx, w));
  return { x: f.x + f.tx * BOSS_HOME_AHEAD, y: f.y + f.ty * BOSS_HOME_AHEAD };
}

export function stage4(ctx: Ctx): void {
  const map = ctx.map!;
  const warp = ctx.warp!;
  const wb = ctx.worldBeats!;
  const route = { step: map.routeStep, x: map.routeX, y: map.routeY };
  const wEnd = warp.toWorld(ctx.duration) + 1;
  const n = Math.ceil(wEnd * HERO_RATE) + 1;
  const secAtW = (w: number) => ctx.sections[sectionIndexAt(ctx, warp.toSong(w))];
  const bpbAtBi = (bi: number) => {
    const s = ctx.song.beats[Math.max(0, Math.min(ctx.song.beats.length - 1, Math.floor(bi)))];
    const sec = ctx.sections[sectionIndexAt(ctx, s)];
    return ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
  };
  const beatW = (w: number) => {
    const bi = worldBeatIndex(wb, w);
    const k = Math.max(0, Math.min(wb.length - 2, Math.floor(bi)));
    return Math.max(0.05, wb[k + 1] - wb[k]);
  };

  // --- Base route position, re-timed into hops and lunges.
  const baseD = (w: number): { d: number; z: number; lunge: number } => {
    const sec = secAtW(w);
    const loco = sec.moveset.locomotion ?? { mode: "glide" as const };
    if (loco.mode === "glide") return { d: progressAt(ctx, w), z: 0, lunge: 0 };
    const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
    const stepBeats = loco.every === "bar" ? bpb : loco.every === "eighth" ? 0.5 : 1;
    const wbi = worldBeatIndex(wb, w);
    const rel = (wbi - sec.b0) / stepBeats;
    const k = Math.floor(rel);
    const wA = worldTimeOfBeatIndex(wb, sec.b0 + k * stepBeats);
    const wB = worldTimeOfBeatIndex(wb, sec.b0 + (k + 1) * stepBeats);
    const pA = progressAt(ctx, wA);
    const pB = progressAt(ctx, wB);
    const u = wB > wA ? (w - wA) / (wB - wA) : 1;
    if (loco.mode === "step") {
      const h0 = 0.5;
      const v = u < h0 ? 0 : (u - h0) / (1 - h0);
      const hopH = stepBeats >= bpb ? 10 : 6;
      return { d: lerp(pA, pB, easeInOut(v)), z: v > 0 ? hopH * Math.sin(Math.PI * v) : 0, lunge: 0 };
    }
    // Dash: a quick lunge just before the beat, then settle.
    const d0 = 0.72;
    const v = u < d0 ? 0 : (u - d0) / (1 - d0);
    const e = 1 - Math.pow(1 - v, 3);
    return { d: lerp(pA, pB, e), z: 0, lunge: v > 0 && v < 1 ? 1 : 0 };
  };

  // --- Path shape around the route, blended over a bar at section changes.
  const pathAt = (w: number): [number, number] => {
    const wbi = worldBeatIndex(wb, w);
    const s = warp.toSong(w);
    const i = sectionIndexAt(ctx, s);
    const sec = ctx.sections[i];
    const bpb = bpbAtBi(wbi);
    const cur = pathOffset(sec, wbi, bpb);
    if (i === 0) return cur;
    const u = smoothstep((wbi - sec.b0) / Math.max(1, bpb));
    if (u >= 1) return cur;
    const prev = pathOffset(ctx.sections[i - 1], wbi, bpb);
    return [lerp(prev[0], cur[0], u), lerp(prev[1], cur[1], u)];
  };

  // --- Directions for every action.
  const dirCycle = new Map<number, number>();
  const tangentAt = (w: number) => {
    const f = routeFrame(route, progressAt(ctx, w));
    return Math.atan2(f.ty, f.tx);
  };
  const DIR_OFFSETS = { left: -Math.PI / 2, right: Math.PI / 2, ahead: 0, behind: Math.PI } as const;
  const rough = (w: number) => {
    const b = baseD(w);
    const f = routeFrame(route, b.d);
    const [al, lat] = pathAt(w);
    return { x: f.x + f.tx * al - f.ty * lat, y: f.y + f.ty * al + f.tx * lat, f };
  };
  for (const a of ctx.actions) {
    const th = tangentAt(a.w);
    const r = ctx.rng("dir", a.id);
    if (a.kind === "dodge") {
      const threat = ctx.threats[a.threat!];
      const pos = rough(a.w);
      let incoming: number;
      if (threat.kind === "arrow") incoming = th + threat.bearing;
      else {
        const home = bossHome(ctx, a.w);
        incoming = Math.atan2(pos.y - home.y, pos.x - home.x) + Math.PI;
      }
      // Sideways relative to the bearing, toward the route centre line.
      const lat = (pos.x - pos.f.x) * -pos.f.ty + (pos.y - pos.f.y) * pos.f.tx;
      const c1 = incoming + Math.PI / 2;
      const latDir = Math.cos(c1) * -pos.f.ty + Math.sin(c1) * pos.f.tx;
      a.dir = latDir * lat > 0 ? incoming - Math.PI / 2 : c1;
      continue;
    }
    if (a.bossHit) {
      const pos = rough(a.w);
      const home = bossHome(ctx, a.w);
      a.dir = Math.atan2(home.y - pos.y, home.x - pos.x);
      continue;
    }
    if (a.kind === "sweep") {
      a.dir = th;
      continue;
    }
    const sec = ctx.sections[a.section];
    const cyc = sec.moveset.directions ?? ["ahead", "left", "right", "ahead"];
    const k = dirCycle.get(sec.index) ?? 0;
    dirCycle.set(sec.index, k + 1);
    const jitter = a.move.id === "dash-strike" ? r.range(-0.45, 0.45) : r.range(-0.35, 0.35);
    a.dir = th + DIR_OFFSETS[cyc[k % cyc.length]] + jitter;
  }

  // --- Move travel offsets.
  interface Push {
    a: HeroAction;
    wStart: number;
    wImpact: number;
    wHold: number;
    dx: number;
    dy: number;
    beat: number;
    /** Piecewise-linear sweep profile (world time, distance). */
    profile?: [number, number][];
  }
  const pushes: Push[] = [];
  const animStartOf = (a: HeroAction) => {
    const info = ctx.heroAnims[a.move.animation.name];
    const impact = a.move.impactFrame ?? info?.impactFrame ?? 3;
    return a.w - impact / a.move.animation.fps;
  };
  for (const a of ctx.actions) {
    let travel = a.move.travel;
    if (a.kind === "dodge" && ctx.threats[a.threat!].kind === "slam") travel = 80;
    if (a.kind === "sweep") {
      const f = ctx.formations[a.formation!];
      const def = ctx.lib.formations.get(f.def)!;
      const prof: [number, number][] = [[a.w - 0.1, 0]];
      const bi0 = a.bi;
      const stagger = def.release.at === "finalAccentOfPhrase" ? 0.25 : 0.25;
      for (let k = 0; k < def.count; k++) {
        const s = ctx.song.beats.length > 0 ? timeAtBeat(ctx, bi0 + k * stagger) : a.s;
        prof.push([warp.toWorld(s), 24 + k * def.spacing]);
      }
      const last = prof[prof.length - 1];
      pushes.push({ a, wStart: a.w - 0.1, wImpact: last[0], wHold: last[0], dx: Math.cos(a.dir), dy: Math.sin(a.dir), beat: beatW(a.w), profile: prof });
      continue;
    }
    if (travel <= 0) continue;
    const wStart = Math.min(a.w - 0.04, animStartOf(a));
    const wHold = a.holdUntilS !== undefined ? warp.toWorld(a.holdUntilS) : a.w;
    pushes.push({ a, wStart, wImpact: a.w, wHold, dx: Math.cos(a.dir) * travel, dy: Math.sin(a.dir) * travel, beat: beatW(a.w) });
  }
  pushes.sort((p, q) => p.wStart - q.wStart);

  const pushAt = (w: number, out: { x: number; y: number; trail: number }) => {
    out.x = 0;
    out.y = 0;
    out.trail = 0;
    for (const p of pushes) {
      if (p.wStart > w) break;
      let amt: number;
      if (p.profile) {
        let dist = 0;
        const pr = p.profile;
        if (w >= pr[pr.length - 1][0]) dist = pr[pr.length - 1][1] * relax(w - pr[pr.length - 1][0], p.beat);
        else {
          for (let k = 0; k + 1 < pr.length; k++) {
            if (w >= pr[k][0] && w < pr[k + 1][0]) {
              dist = lerp(pr[k][1], pr[k + 1][1], (w - pr[k][0]) / Math.max(1e-6, pr[k + 1][0] - pr[k][0]));
              break;
            }
          }
          out.trail = Math.max(out.trail, 1);
        }
        out.x += p.dx * dist;
        out.y += p.dy * dist;
        continue;
      }
      if (w < p.wImpact) {
        amt = easeInOut((w - p.wStart) / Math.max(1e-6, p.wImpact - p.wStart));
        if (p.a.move.id === "dash-strike" || p.a.kind === "dodge") out.trail = Math.max(out.trail, 1);
      } else if (w < p.wHold) amt = 1;
      else amt = relax(w - p.wHold, p.beat);
      if (amt < 1e-3 && w > p.wHold) continue;
      out.x += p.dx * amt;
      out.y += p.dy * amt;
    }
  };

  // --- Bake samples.
  const x = new Float32Array(n);
  const y = new Float32Array(n);
  const z = new Float32Array(n);
  const facing = new Uint8Array(n);
  const anim = new Uint8Array(n);
  const frame = new Uint8Array(n);
  const trail = new Float32Array(n);
  const push = { x: 0, y: 0, trail: 0 };
  const maxLat = map.corridorHalfWidth - CORRIDOR_MARGIN;
  for (let k = 0; k < n; k++) {
    const w = k / HERO_RATE;
    const b = baseD(w);
    const f = routeFrame(route, b.d);
    const [al, lat] = pathAt(w);
    pushAt(w, push);
    // Offset in world space, then clamp its lateral part to the corridor.
    let ox = f.tx * al - f.ty * lat + push.x;
    let oy = f.ty * al + f.tx * lat + push.y;
    const olat = ox * -f.ty + oy * f.tx;
    if (Math.abs(olat) > maxLat) {
      const fix = olat - Math.sign(olat) * maxLat;
      ox -= -f.ty * fix;
      oy -= f.tx * fix;
    }
    x[k] = f.x + ox;
    y[k] = f.y + oy;
    z[k] = b.z;
    trail[k] = Math.max(push.trail, b.lunge);
  }

  // --- Animation and facing.
  const animIdx = (name: string) => Math.max(0, HERO_ANIMS.indexOf(name));
  // Each move's animation starts early so its impact frame lands on the slot. When that
  // wind-up would cut into the previous move's impact, the wind-up plays faster instead.
  let prevImpact = -Infinity;
  const windows = ctx.actions.map((a) => {
    const info = ctx.heroAnims[a.move.animation.name] ?? { frames: 6 };
    const impact = a.move.impactFrame ?? info.impactFrame ?? 3;
    let fps = a.move.animation.fps;
    let start = a.w - impact / fps;
    const earliest = prevImpact + 1 / 40;
    if (start < earliest && impact > 0) {
      start = Math.min(a.w - 1 / 120, earliest);
      // A snap: the wind-up plays as fast as needed (the impact frame still lands on the slot).
      fps = impact / Math.max(1 / 240, a.w - start);
      start = a.w - impact / fps;
    }
    prevImpact = Math.max(prevImpact, a.w);
    return { a, start, end: start + info.frames / fps, frames: info.frames, fps };
  });
  let wi = 0;
  let lastFacing = 3;
  for (let k = 0; k < n; k++) {
    const w = k / HERO_RATE;
    while (wi < windows.length && windows[wi].end <= w) wi++;
    // Before an impact, show the action whose impact comes next; after it, follow through
    // until the next action's animation starts.
    let win: (typeof windows)[number] | undefined;
    let pending: (typeof windows)[number] | undefined;
    for (let j = wi; j < windows.length && windows[j].start <= w; j++) {
      if (w >= windows[j].end) continue;
      if (windows[j].a.w >= w) {
        if (!pending || windows[j].a.w < pending.a.w) pending = windows[j];
      } else win = windows[j];
    }
    if (pending) win = pending;
    if (win) {
      const imp = win.a.move.impactFrame ?? ctx.heroAnims[win.a.move.animation.name]?.impactFrame ?? 3;
      // The sample nearest the impact always shows the impact frame, however fast the wind-up.
      const fr = Math.abs(w - win.a.w) <= 0.5 / HERO_RATE ? imp : clamp(Math.floor((w - win.start) * win.fps + 1e-6), 0, win.frames - 1);
      anim[k] = animIdx(win.a.move.animation.name);
      frame[k] = fr;
      let fc = facingOf(win.a.dir);
      if (win.a.move.animation.cycleFacings) fc = (fc + fr) % 4;
      facing[k] = fc;
      lastFacing = facingOf(win.a.dir);
      continue;
    }
    const k0 = Math.max(0, k - 2);
    const k1 = Math.min(n - 1, k + 2);
    const vx = (x[k1] - x[k0]) * (HERO_RATE / (k1 - k0 || 1));
    const vy = (y[k1] - y[k0]) * (HERO_RATE / (k1 - k0 || 1));
    const speed = Math.hypot(vx, vy);
    const wbi = worldBeatIndex(wb, w);
    if (z[k] > 0.5) {
      anim[k] = animIdx("jump");
      const sec = secAtW(w);
      const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
      const stepBeats = sec.moveset.locomotion?.every === "bar" ? bpb : 1;
      const u = frac((wbi - sec.b0) / stepBeats);
      frame[k] = clamp(Math.floor(((u - 0.5) / 0.5) * 5), 0, 4);
    } else if (speed > 140) {
      anim[k] = animIdx("run");
      frame[k] = Math.floor(frac(wbi / 2) * 8) % 8;
    } else if (speed > 8) {
      anim[k] = animIdx("walk");
      // One stride per beat: the 8 walk frames (after the standing frame) span two beats.
      frame[k] = 1 + (Math.floor(frac(wbi / 2) * 8) % 8);
    } else {
      anim[k] = animIdx("idle");
      frame[k] = Math.floor(frac(wbi / 2) * 2) % 2;
    }
    if (speed > 20) lastFacing = facingOf(Math.atan2(vy, vx));
    facing[k] = lastFacing;
  }

  const track: HeroTrack = { rate: HERO_RATE, w0: 0, x, y, z, facing, anim, frame, animNames: HERO_ANIMS, trail };
  ctx.hero = track;

  // Record impact and start positions on actions.
  for (const a of ctx.actions) {
    const p = heroAt(track, a.w);
    a.x = p.x;
    a.y = p.y;
    const p0 = heroAt(track, Math.min(a.w - 0.02, animStartOf(a)));
    a.x0 = p0.x;
    a.y0 = p0.y;
  }
}

function timeAtBeat(ctx: Ctx, bi: number): number {
  const b = ctx.song.beats;
  const k = Math.max(0, Math.min(b.length - 2, Math.floor(bi)));
  return b[k] + (bi - k) * (b[k + 1] - b[k]);
}

export function heroAt(t: HeroTrack, w: number): { x: number; y: number; z: number } {
  const f = Math.max(0, (w - t.w0) * t.rate);
  const k = Math.min(t.x.length - 2, Math.floor(f));
  const u = Math.min(1, f - k);
  return { x: lerp(t.x[k], t.x[k + 1], u), y: lerp(t.y[k], t.y[k + 1], u), z: lerp(t.z[k], t.z[k + 1], u) };
}
