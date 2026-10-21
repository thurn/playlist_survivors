/**
 * Stage 7: simulate the crowd. A fixed-step simulation (60 steps per
 * world-second) moves every enemy from its spawn point to its fates and
 * appointments. Output: positions at 30 samples per world-second.
 */
import { nearestInside, pushOutDir, shapeBounds, shapeContains } from "../core/geometry.ts";
import { Rng } from "../core/rng.ts";
import { beatHit, beatSwell, smoothstep } from "../core/shaping.ts";
import { barOfBeatIndex } from "../core/songmap.ts";
import type { EnemyRecord, Hit } from "../plan/types.ts";
import { PlanError, sectionIndexAt, type Ctx } from "./context.ts";
import type { Agent, KillPlan } from "./stage6-kills.ts";
import { heroAt, worldBeatIndex } from "./stage4-hero.ts";
import { viewRect } from "./stage5-camera.ts";

export const SIM_RATE = 60;
export const SAMPLE_RATE = 30;
const BLEND = 0.25;
/** World-seconds before a fate during which personal space and soft keep-out no longer apply. */
const APPROACH = 1.2;
/** Radius of the ring the horde swarms on around the hero while waiting. */
const HOLD_RING = 92;
/** How far ahead (world-seconds) the horde looks for big hero hits to back away from. */
const HOLD_LOOKAHEAD = 1.0;
/** Free (slow) sections give the horde longer warning. */
const HOLD_LOOKAHEAD_FREE = 2.0;
/** Minimum centre spacing kept by positional correction, as a fraction of the summed radii. */
const MIN_GAP = 0.6;
/** The spacing check's limit (0.5 body widths) plus a little slack. */
const HARD_GAP = 0.56;
/** Enemies this close to a hit's shape when it fires count as caught (robust to interpolation). */
const CONVERT_MARGIN = 3;
/** Keep-out applies to hits firing within this many world-seconds. */
const KEEPOUT_WINDOW = 0.4;
const PERSONAL_SPACE = 40;
const MISS_LIMIT = 24;
/** Simulation runs: the first, plus re-runs with 30% more lifetime for enemies that missed. */
const MAX_RUNS = 2;
const DEBUG = typeof process !== "undefined" && !!process.env?.PS_DEBUG;
export const missLog: string[] = [];

export interface CrowdResult {
  enemies: EnemyRecord[];
  samples: Float32Array;
  misses: number;
  /** Enemies removed because they could not reach their targets. */
  dropped?: number;
}

interface State {
  ag: Agent;
  r: number;
  x: number;
  y: number;
  vx: number;
  vy: number;
  wp: number;
  blendW: number;
  bx: number;
  by: number;
  active: boolean;
  done: boolean;
  fateHits: Set<number>;
  samples: number[];
  firstSample: number;
  /** Stepping in for its own fate this step. */
  approach: boolean;
  /** Holding an appointment this step. */
  holding: boolean;
  /** Cached separation push. */
  sx: number;
  sy: number;
  missed: boolean;
}

const NONE: never[] = [];
const len2 = (x: number, y: number): number => Math.sqrt(x * x + y * y);
const GROUP_STEPS = { eighth: 0.5, beat: 1, bar: -1, "two-bar": -2 } as const;

export function simulateCrowd(ctx: Ctx, kp: KillPlan): CrowdResult {
  let result!: CrowdResult;
  const missedOf = (r: CrowdResult) => new Set(r.enemies.filter((e) => (e as EnemyRecord & { _missed?: boolean })._missed).map((e) => e.id));
  for (let attempt = 0; attempt < MAX_RUNS; attempt++) {
    result = runOnce(ctx, kp);
    if (DEBUG) console.error(`crowd run ${attempt}: misses ${result.misses}`);
    if (result.misses === 0) {
      repairSpacing(kp, result);
      return { ...result, dropped: 0 };
    }
    if (attempt === MAX_RUNS - 1) break;
    // Give each missing enemy 30% more lifetime and re-run the whole simulation.
    const missed = missedOf(result);
    for (const ag of kp.agents) {
      if (!missed.has(ag.e.id) || ag.exempt) continue;
      // Step in earlier next time.
      ag.approachBoost = (ag.approachBoost ?? 1) * 1.8;
      const first = ag.waypoints[0];
      const L = (first.w - ag.e.spawnW) * 1.3;
      const w = Math.max(0, first.w - L);
      ag.e.spawnW = w;
      const view = viewRect(ctx.camera!, w);
      const r = new Rng(ctx.seed, "respawn", ag.e.id, attempt);
      const sp = respawn(view, ag, first.x, first.y, r);
      ag.spawnX = sp.x;
      ag.spawnY = sp.y;
    }
  }
  // Enemies that still cannot make it are removed; their hits simply kill fewer.
  const missed = missedOf(result);
  if (missed.size > Math.max(10, kp.agents.length * 0.02)) {
    const bad = result.enemies.filter((e) => missed.has(e.id));
    throw new PlanError(
      `${bad.length} enemies could not reach their targets: ` +
        bad
          .slice(0, 5)
          .map((e) => `#${e.id} ${e.type} (spawn w=${e.spawnW.toFixed(2)}, death w=${e.deathW.toFixed(2)})`)
          .join(", "),
    );
  }
  kp.agents = kp.agents.filter((a) => !missed.has(a.e.id));
  for (const list of kp.fatesByHit) for (let i = list.length - 1; i >= 0; i--) if (missed.has(list[i])) list.splice(i, 1);
  const keep = result.enemies.filter((e) => !missed.has(e.id));
  let total = 0;
  for (const e of keep) total += e.sampleCount * 2;
  const samples = new Float32Array(total);
  let off = 0;
  for (const e of keep) {
    samples.set(result.samples.subarray(e.sampleOffset * 2, (e.sampleOffset + e.sampleCount) * 2), off);
    e.sampleOffset = off / 2;
    off += e.sampleCount * 2;
  }
  const out = { enemies: keep, samples, misses: 0, dropped: missed.size };
  repairSpacing(kp, out);
  return out;
}

/**
 * Nudge apart any residual overlaps in the recorded samples (two enemies blending along
 * crossing paths, say). Samples next to a fate and formation members are left alone.
 */
function repairSpacing(kp: KillPlan, r: CrowdResult): void {
  const defs = new Map(kp.agents.map((a) => [a.e.id, a.def]));
  const es = r.enemies.filter((e) => e.formation === undefined && defs.has(e.id));
  const radius = Float32Array.from(es, (e) => defs.get(e.id)!.radius);
  const k0 = Int32Array.from(es, (e) => Math.round(e.sampleW0 * SAMPLE_RATE));
  let maxK = 0;
  for (let i = 0; i < es.length; i++) maxK = Math.max(maxK, k0[i] + es[i].sampleCount);
  // Counting sort of (enemy, frame) pairs by frame.
  const count = new Int32Array(maxK + 1);
  for (let i = 0; i < es.length; i++) for (let k = 0; k < es[i].sampleCount; k++) count[k0[i] + k]++;
  const start = new Int32Array(maxK + 2);
  for (let k = 0; k <= maxK; k++) start[k + 1] = start[k] + count[k];
  const fill = start.slice();
  const members = new Int32Array(start[maxK + 1]);
  for (let i = 0; i < es.length; i++) for (let k = 0; k < es[i].sampleCount; k++) members[fill[k0[i] + k]++] = i;
  const s = r.samples;
  const H = 4096;
  const head = new Int32Array(H);
  const stamp = new Int32Array(H).fill(-1);
  const nextArr = new Int32Array(members.length);
  const hashOf = (cx: number, cy: number) => (((cx * 73856093) ^ (cy * 19349663)) >>> 0) % H;
  const locked = (e: EnemyRecord, k: number) => {
    const w = k / SAMPLE_RATE;
    if (w >= e.deathW - 0.5 / SAMPLE_RATE) return true;
    for (const f of e.fates) if (Math.abs(f.w - w) < 0.75 / SAMPLE_RATE) return true;
    return false;
  };
  let gen = 0;
  for (let k = 0; k <= maxK; k++) {
    const a0 = start[k];
    const a1 = start[k + 1];
    if (a1 - a0 < 2) continue;
    gen++;
    for (let m = a0; m < a1; m++) {
      const i = members[m];
      const o = (es[i].sampleOffset + k - k0[i]) * 2;
      const h = hashOf(Math.floor(s[o] / 32), Math.floor(s[o + 1] / 32));
      if (stamp[h] !== gen) {
        stamp[h] = gen;
        head[h] = -1;
      }
      nextArr[m] = head[h];
      head[h] = m;
    }
    for (let iter = 0; iter < 2; iter++)
    for (let m = a0; m < a1; m++) {
      const i = members[m];
      const oa = (es[i].sampleOffset + k - k0[i]) * 2;
      const cx = Math.floor(s[oa] / 32);
      const cy = Math.floor(s[oa + 1] / 32);
      for (let gx = cx - 1; gx <= cx + 1; gx++)
        for (let gy = cy - 1; gy <= cy + 1; gy++) {
          const h = hashOf(gx, gy);
          if (stamp[h] !== gen) continue;
          for (let n = head[h]; n >= 0; n = nextArr[n]) {
            if (n <= m) continue;
            const j = members[n];
            const ob = (es[j].sampleOffset + k - k0[j]) * 2;
            const goal = HARD_GAP * (radius[i] + radius[j]);
            const dx = s[oa] - s[ob];
            const dy = s[oa + 1] - s[ob + 1];
            const d2 = dx * dx + dy * dy;
            if (d2 >= goal * goal) continue;
            const la = locked(es[i], k);
            const lb = locked(es[j], k);
            if (la && lb) continue;
            const d = Math.sqrt(d2) || 0.01;
            const ux = d > 0.01 ? dx / d : 1;
            const uy = d > 0.01 ? dy / d : 0;
            const push = goal - d;
            const wa = la ? 0 : lb ? 1 : 0.5;
            s[oa] += ux * push * wa;
            s[oa + 1] += uy * push * wa;
            s[ob] -= ux * push * (1 - wa);
            s[ob + 1] -= uy * push * (1 - wa);
          }
        }
    }
  }
}

function respawn(view: [number, number, number, number], ag: Agent, tx: number, ty: number, r: Rng): { x: number; y: number } {
  const m = 16 + 40 + (ag.def.scale > 1 ? 64 : 0);
  const cands = [
    { x: view[0] - m, y: Math.min(view[3] + m, Math.max(view[1] - m, ty + r.range(-60, 60))) },
    { x: view[2] + m, y: Math.min(view[3] + m, Math.max(view[1] - m, ty + r.range(-60, 60))) },
    { x: Math.min(view[2] + m, Math.max(view[0] - m, tx + r.range(-60, 60))), y: view[1] - m },
    { x: Math.min(view[2] + m, Math.max(view[0] - m, tx + r.range(-60, 60))), y: view[3] + m + 40 },
  ];
  cands.sort((a, b) => len2(a.x - tx, a.y - ty) - len2(b.x - tx, b.y - ty));
  return cands[0];
}

function runOnce(ctx: Ctx, kp: KillPlan): CrowdResult {
  const warp = ctx.warp!;
  const hero = ctx.hero!;
  for (const ag of kp.agents)
    for (const wp of ag.waypoints) {
      const h = heroAt(hero, wp.w);
      wp.hx = h.x;
      wp.hy = h.y;
    }
  const wb = ctx.worldBeats!;
  const hits = kp.hits;
  const dt = 1 / SIM_RATE;
  const wEnd = warp.toWorld(ctx.duration) + 0.5;
  const steps = Math.ceil(wEnd * SIM_RATE);

  const states: State[] = kp.agents.map((ag) => ({
    ag,
    r: ag.def.radius,
    x: ag.spawnX,
    y: ag.spawnY,
    vx: 0,
    vy: 0,
    wp: 0,
    blendW: -1,
    bx: 0,
    by: 0,
    active: false,
    done: false,
    fateHits: new Set(ag.waypoints.filter((w) => w.hit >= 0).map((w) => w.hit)),
    samples: [],
    firstSample: -1,
    sx: 0,
    sy: 0,
    approach: false,
    holding: false,
    missed: false,
  }));
  const bySpawn = [...states].sort((a, b) => a.ag.e.spawnW - b.ag.e.spawnW || a.ag.e.id - b.ag.e.id);
  let nextSpawn = 0;
  const active: State[] = [];
  let hitPtr = 0;
  let firedPtr = 0;
  /** An enemy caught inside a hit without a fate on it takes that hit: regular enemies die there. */
  const convertToFate = (st: State, h: Hit) => {
    const ag = st.ag;
    const e = ag.e;
    // Caught at the edge: step just inside the shape so the death is covered.
    if (!shapeContains(h.shape, st.x, st.y, -0.5)) {
      const p = nearestInside(h.shape, st.x, st.y, 1);
      st.x = p.x;
      st.y = p.y;
    }
    st.fateHits.add(h.id);
    kp.fatesByHit[h.id].push(e.id);
    // Enemies on their way to an appointment (archers, the boss) survive with a flinch.
    const appointmentAhead = ag.waypoints.slice(st.wp).some((wp) => wp.appointment);
    if (ag.def.size === "regular" && !(ag.kinematic && !ag.simulated) && !appointmentAhead) {
      // Die here, now: drop the remaining waypoints and fates.
      const old = ag.waypoints.slice(st.wp);
      for (const o of old) if (o.hit >= 0) {
        const list = kp.fatesByHit[o.hit];
        const k = list.indexOf(e.id);
        if (k >= 0) list.splice(k, 1);
        st.fateHits.delete(o.hit);
      }
      ag.waypoints.splice(st.wp, ag.waypoints.length - st.wp, { w: h.w, x: st.x, y: st.y, hit: h.id, lethal: true });
      e.fates = e.fates.filter((f) => f.w < h.w - 1e-9);
      e.fates.push({ w: h.w, x: st.x, y: st.y, hit: h.id, lethal: true });
      e.deathW = h.w;
      if (ag.kinematic) ag.kinUntil = Math.min(ag.kinUntil, h.w);
      st.done = true;
    } else {
      const f = { w: h.w, x: st.x, y: st.y, hit: h.id, lethal: false };
      e.fates.push(f);
      e.fates.sort((a, b) => a.w - b.w);
    }
  };

  const cell = 32;
  // A flat grid of linked lists, centred on the hero each step.
  const G = 160;
  const head = new Int32Array(G * G);
  const stamp = new Int32Array(G * G).fill(-1);
  let next = new Int32Array(1024);
  let curStep = -1;
  const pairs: number[] = [];
  let gox = 0;
  let goy = 0;
  const cellOf = (gx: number, gy: number) => {
    const ix = gx - gox;
    const iy = gy - goy;
    if (ix < 0 || iy < 0 || ix >= G || iy >= G) return -1;
    const c = iy * G + ix;
    return stamp[c] === curStep ? c : -1;
  };

  for (let step = 0; step <= steps; step++) {
    const w = step * dt;
    // Spawn.
    while (nextSpawn < bySpawn.length && bySpawn[nextSpawn].ag.e.spawnW <= w + 1e-9) {
      const st = bySpawn[nextSpawn++];
      st.active = true;
      if (st.ag.kinematic && w <= st.ag.kinUntil) {
        const p = st.ag.kinematic(Math.max(w, st.ag.e.spawnW));
        st.x = p.x;
        st.y = p.y;
      }
      active.push(st);
    }

    // Rhythm multipliers per group.
    const wbi = worldBeatIndex(wb, w);
    const s = warp.toSong(w);
    const sec = ctx.sections[sectionIndexAt(ctx, s)];
    const free = sec.song.feel === "free";
    const bar = Math.max(1, barOfBeatIndex(ctx.idx, wbi));
    const bpb = ctx.idx.beatsPerBar[Math.min(ctx.idx.barCount, bar)] ?? 2;
    const barStart = ctx.idx.barStart[Math.min(ctx.idx.barCount, bar)] ?? 0;
    const mult: Record<string, number> = {};
    for (const [g, st] of Object.entries(GROUP_STEPS)) {
      let period = st > 0 ? st : -st * bpb;
      let phase: number;
      if (free) {
        period = Math.max(2, period);
        phase = ((wbi - barStart) / period) % 1;
        mult[g] = (0.6 + 0.8 * beatSwell(phase < 0 ? phase + 1 : phase)) / 1.0;
      } else {
        phase = ((wbi - barStart) / period) % 1;
        mult[g] = (0.6 + 0.8 * beatHit(phase < 0 ? phase + 1 : phase)) / 0.733;
      }
    }

    // Spatial hash.
    curStep = step;
    const hp0 = heroAt(hero, w);
    gox = Math.floor(hp0.x / cell) - G / 2;
    goy = Math.floor(hp0.y / cell) - G / 2;
    if (next.length < active.length) next = new Int32Array(active.length * 2);
    for (let i = 0; i < active.length; i++) {
      const st = active[i];
      const ix = Math.floor(st.x / cell) - gox;
      const iy = Math.floor(st.y / cell) - goy;
      if (ix < 0 || iy < 0 || ix >= G || iy >= G) continue;
      const c = iy * G + ix;
      if (stamp[c] !== step) {
        stamp[c] = step;
        head[c] = -1;
      }
      next[i] = head[c];
      head[c] = i;
    }

    // Hits firing within the next beat.
    while (hitPtr < hits.length && hits[hitPtr].w < w) hitPtr++;
    const beatLen = Math.max(0.1, wb[Math.min(wb.length - 1, Math.floor(wbi) + 1)] - wb[Math.max(0, Math.floor(wbi))]);
    const upcoming: { h: Hit; b: [number, number, number, number]; big: boolean }[] = [];
    const keepout: typeof upcoming = [];
    let ub0 = Infinity;
    let ub1 = Infinity;
    let ub2 = -Infinity;
    let ub3 = -Infinity;
    for (let i = hitPtr; i < hits.length && hits[i].w <= w + Math.max(beatLen, free ? HOLD_LOOKAHEAD_FREE : HOLD_LOOKAHEAD); i++) {
      if (hits[i].source === "ring") continue;
      const b = shapeBounds(hits[i].shape);
      const big = b[2] - b[0] > 180;
      upcoming.push({ h: hits[i], b, big });
      if (hits[i].w - w <= KEEPOUT_WINDOW) {
        keepout.push(upcoming[upcoming.length - 1]);
        ub0 = Math.min(ub0, b[0]);
        ub1 = Math.min(ub1, b[1]);
        ub2 = Math.max(ub2, b[2]);
        ub3 = Math.max(ub3, b[3]);
      }
    }
    // Hits that fired since the last step: anyone caught inside without a fate on it is hit now.
    while (firedPtr < hits.length && hits[firedPtr].w <= w) {
      const h = hits[firedPtr++];
      if (h.source === "ring") continue;
      const b = shapeBounds(h.shape);
      const cx0 = Math.floor(b[0] / cell);
      const cx1 = Math.floor(b[2] / cell);
      const cy0 = Math.floor(b[1] / cell);
      const cy1 = Math.floor(b[3] / cell);
      for (let gx = cx0; gx <= cx1; gx++) {
        for (let gy = cy0; gy <= cy1; gy++) {
          const c = cellOf(gx, gy);
          if (c < 0) continue;
          for (let j = head[c]; j >= 0; j = next[j]) {
            const st = active[j];
            if (st.done || st.fateHits.has(h.id) || !shapeContains(h.shape, st.x, st.y, CONVERT_MARGIN)) continue;
            // Already dead by the time this hit lands (both fall in this step)?
            const cur = st.ag.waypoints[st.wp];
            if (cur && cur.lethal && cur.w <= h.w + 1e-9) continue;
            convertToFate(st, h);
          }
        }
      }
    }
    const hp = heroAt(hero, w);
    // The horde backs off beyond the reach of the hero's next big hits.
    let holdReach = 0;
    for (const u of upcoming) {
      if (u.h.source !== "move" || u.h.w - w > (free ? HOLD_LOOKAHEAD_FREE : HOLD_LOOKAHEAD)) continue;
      const b = u.b;
      const reach = Math.max(Math.abs(b[0] - hp.x), Math.abs(b[2] - hp.x), Math.abs(b[1] - hp.y), Math.abs(b[3] - hp.y));
      holdReach = Math.max(holdReach, reach);
    }
    const hpn = heroAt(hero, w + 0.1);
    const heroVx = (hpn.x - hp.x) * 10;
    const heroVy = (hpn.y - hp.y) * 10;

    for (let i = active.length - 1; i >= 0; i--) {
      const st = active[i];
      const ag = st.ag;
      // Kinematic formation phase.
      if (ag.kinematic && w <= ag.kinUntil) {
        const p = ag.kinematic(w);
        st.vx = (p.x - st.x) * SIM_RATE;
        st.vy = (p.y - st.y) * SIM_RATE;
        st.x = p.x;
        st.y = p.y;
        if (ag.waypoints.length > 0 && ag.waypoints[0].w <= w + 1e-9 && ag.waypoints[0].lethal && !ag.simulated) {
          st.done = true;
        }
        continue;
      }
      if (st.done) continue;
      let wp = ag.waypoints[st.wp];
      while (wp && w >= wp.w - 1e-9 && !(wp.holdUntil !== undefined && w < wp.holdUntil)) {
        st.x = wp.x;
        st.y = wp.y;
        st.vx = 0;
        st.vy = 0;
        if (wp.lethal) {
          st.done = true;
          break;
        }
        st.wp++;
        st.blendW = -1;
        wp = ag.waypoints[st.wp];
      }
      if (st.done || !wp) {
        st.done = true;
        continue;
      }
      st.holding = w >= wp.w - 1e-9;
      if (st.holding) {
        // Holding an appointment.
        st.x = wp.x;
        st.y = wp.y;
        continue;
      }
      const remaining = wp.w - w;
      if (remaining <= BLEND) {
        if (st.blendW < 0) {
          st.blendW = w;
          st.bx = st.x;
          st.by = st.y;
          if (len2(st.x - wp.x, st.y - wp.y) > MISS_LIMIT && !ag.exempt) {
            st.missed = true;
            if (DEBUG) missLog.push(`#${ag.e.id} ${ag.e.type} leg ${st.wp}/${ag.waypoints.length} d=${Math.round(len2(st.x - wp.x, st.y - wp.y))} rem=${(wp.w - ag.e.spawnW).toFixed(1)}`);
          }
        }
        const u = smoothstep((w - st.blendW) / Math.max(1e-6, wp.w - st.blendW));
        const nx = st.bx + (wp.x - st.bx) * u;
        const ny = st.by + (wp.y - st.by) * u;
        st.vx = (nx - st.x) * SIM_RATE;
        st.vy = (ny - st.y) * SIM_RATE;
        st.x = nx;
        st.y = ny;
        continue;
      }
      // Arrival. Until the final approach, swarm on a ring around the hero in the
      // direction of the eventual kill; then step in.
      const m = mult[ag.def.group] ?? 1;
      const approaching = !wp.appointment && remaining < APPROACH * (ag.approachBoost ?? 1);
      st.approach = approaching;
      const vmax = ag.def.speed * m * (approaching ? 1.6 : 1);
      const direct = len2(wp.x - st.x, wp.y - st.y);
      const budget = ag.def.speed * 0.6 * Math.max(0, remaining - APPROACH * (ag.approachBoost ?? 1));
      let dvx: number;
      let dvy: number;
      if (!wp.appointment && !approaching && direct < budget * 0.7 && wp.hx !== undefined) {
        const rx = wp.x - wp.hx;
        const ry = wp.y - wp.hy!;
        const rl = len2(rx, ry) || 1;
        const hold = Math.max(HOLD_RING + st.r, holdReach + st.r + 6, rl);
        const qx = hp.x + (rx / rl) * hold;
        const qy = hp.y + (ry / rl) * hold;
        const gx = (qx - st.x) * 1.6 + heroVx;
        const gy = (qy - st.y) * 1.6 + heroVy;
        const gl = len2(gx, gy);
        const cap = vmax;
        const sc = gl > cap ? cap / gl : 1;
        dvx = gx * sc * (0.5 + 0.5 * m);
        dvy = gy * sc * (0.5 + 0.5 * m);
      } else {
        const tx = wp.x - st.x;
        const ty = wp.y - st.y;
        const td = len2(tx, ty);
        const want = td / Math.max(0.05, remaining - BLEND * 0.8);
        const sp = Math.min(vmax, want * m);
        dvx = td > 1e-6 ? (tx / td) * sp : 0;
        dvy = td > 1e-6 ? (ty / td) * sp : 0;
      }
      // Separation (recomputed at 30 Hz, held in between).
      if ((step & 1) === 0) {
      let sepx = 0;
      let sepy = 0;
      const cx = Math.floor(st.x / cell);
      const cy = Math.floor(st.y / cell);
      for (let gx = cx - 1; gx <= cx + 1; gx++) {
        for (let gy = cy - 1; gy <= cy + 1; gy++) {
          const c = cellOf(gx, gy);
          if (c < 0) continue;
          for (let j = head[c]; j >= 0; j = next[j]) {
            const o = active[j];
            if (o === st || o.done) continue;
            const ox = st.x - o.x;
            const oy = st.y - o.y;
            const lim = 0.8 * (st.r + o.r);
            const d2 = ox * ox + oy * oy;
            if (d2 >= lim * lim) continue;
            const d = Math.sqrt(d2) || 0.01;
            const f = (1 - d / lim) * (approaching ? 25 : 90);
            sepx += (ox / d) * f;
            sepy += (oy / d) * f;
          }
        }
      }
      st.sx = sepx;
      st.sy = sepy;
      }
      dvx += st.sx;
      dvy += st.sy;
      // Personal space around the hero, except in the beat before one of its own fates.
      const hx = st.x - hp.x;
      const hy = st.y - hp.y;
      const hd = len2(hx, hy);
      const ownFateSoon = !wp.appointment && remaining < Math.max(beatLen, APPROACH);
      if (hd < PERSONAL_SPACE + st.r * 0.5 && !ownFateSoon && hd > 1e-3) {
        const f = (PERSONAL_SPACE + st.r * 0.5 - hd) * 8;
        dvx += (hx / hd) * f;
        dvy += (hy / hd) * f;
      }
      // Hit keep-out.
      const nearHits = st.x > ub0 - st.r - 10 && st.x < ub2 + st.r + 10 && st.y > ub1 - st.r - 10 && st.y < ub3 + st.r + 10;
      for (const u of nearHits ? keepout : NONE) {
        if (st.fateHits.has(u.h.id)) continue;
        if (u.h.w - w > KEEPOUT_WINDOW) continue;
        // While stepping in, nothing is dodged; anyone caught is converted at hit time.
        if (approaching) continue;
        const b = u.b;
        const mg = st.r + 10;
        if (st.x < b[0] - mg || st.x > b[2] + mg || st.y < b[1] - mg || st.y > b[3] + mg) continue;
        if (!shapeContains(u.h.shape, st.x, st.y, mg)) continue;
        const dir = pushOutDir(u.h.shape, st.x, st.y);
        const urgency = 1 - Math.max(0, u.h.w - w) / KEEPOUT_WINDOW;
        const f = 140 + 260 * urgency;
        dvx += dir.x * f;
        dvy += dir.y * f;
      }
      const k = Math.min(1, dt * 12);
      st.vx += (dvx - st.vx) * k;
      st.vy += (dvy - st.vy) * k;
      st.x += st.vx * dt;
      st.y += st.vy * dt;
    }

    // Positional correction: push apart overlapping pairs (blending enemies hold their line).
    // One neighbour scan collects the overlapping pairs; a few more passes re-solve just those.
    pairs.length = 0;
    for (let i = 0; i < active.length; i++) {
      const st = active[i];
      if (st.done || (st.ag.kinematic && w <= st.ag.kinUntil)) continue;
      const cx = Math.floor(st.x / cell);
      const cy = Math.floor(st.y / cell);
      for (let gx = cx - 1; gx <= cx + 1; gx++) {
        for (let gy = cy - 1; gy <= cy + 1; gy++) {
          const c = cellOf(gx, gy);
          if (c < 0) continue;
          for (let j = head[c]; j >= 0; j = next[j]) {
            if (j <= i) continue;
            const o = active[j];
            if (o.done || (o.ag.kinematic && w <= o.ag.kinUntil)) continue;
            const lim = MIN_GAP * (st.r + o.r) + 2;
            const ox = st.x - o.x;
            const oy = st.y - o.y;
            if (ox * ox + oy * oy < lim * lim) pairs.push(i, j);
          }
        }
      }
    }
    for (let iter = 0; iter < 4; iter++) {
      for (let p = 0; p < pairs.length; p += 2) {
        const st = active[pairs[p]];
        const o = active[pairs[p + 1]];
        const lim = MIN_GAP * (st.r + o.r);
        const ox = st.x - o.x;
        const oy = st.y - o.y;
        const d2 = ox * ox + oy * oy;
        if (d2 >= lim * lim) continue;
        const d = Math.sqrt(d2) || 0.01;
        const ux = d > 0.01 ? ox / d : 1;
        const uy = d > 0.01 ? oy / d : 0;
        // Idle enemies keep a comfortable gap; anyone stepping in only keeps the hard
        // minimum, and mostly makes the others yield. Blending enemies never move.
        const ma = st.blendW >= 0 || st.holding ? 0 : st.approach ? 0.15 : 1;
        const mb = o.blendW >= 0 || o.holding ? 0 : o.approach ? 0.15 : 1;
        if (ma + mb === 0) continue;
        const stepping = ma < 1 || mb < 1;
        const goal = stepping ? HARD_GAP * (st.r + o.r) : lim;
        if (d >= goal) continue;
        const push = goal - d;
        const wa = ma / (ma + mb);
        const wb2 = 1 - wa;
        st.x += ux * push * wa;
        st.y += uy * push * wa;
        o.x -= ux * push * wb2;
        o.y -= uy * push * wb2;
      }
    }

    // Record samples at 30 Hz.
    if (step % (SIM_RATE / SAMPLE_RATE) === 0) {
      const si = step / (SIM_RATE / SAMPLE_RATE);
      for (const st of active) {
        if (st.firstSample < 0) st.firstSample = si;
        st.samples.push(st.x, st.y);
      }
      for (let i = active.length - 1; i >= 0; i--) {
        if (active[i].done) {
          // One extra sample at the death position, then retire.
          active.splice(i, 1);
        }
      }
    }
  }

  let total = 0;
  for (const st of states) total += st.samples.length;
  const samples = new Float32Array(total);
  let off = 0;
  let misses = 0;
  const enemies: EnemyRecord[] = [];
  for (const st of states) {
    samples.set(st.samples, off);
    const e = st.ag.e;
    e.sampleOffset = off / 2;
    e.sampleCount = st.samples.length / 2;
    // The first stored sample is at world time firstSample / SAMPLE_RATE.
    e.sampleW0 = st.firstSample / SAMPLE_RATE;
    off += st.samples.length;
    if (st.missed) {
      misses++;
      (e as EnemyRecord & { _missed?: boolean })._missed = true;
    } else delete (e as EnemyRecord & { _missed?: boolean })._missed;
    enemies.push(e);
  }
  return { enemies, samples, misses };
}
