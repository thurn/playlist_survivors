/**
 * The planner's checks on its own output. Every failure names the time and
 * entity involved. They also run in the test suite and `npm run check:plan`.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { shapeBounds, shapeContains } from "../core/geometry.ts";
import { lerp } from "../core/shaping.ts";
import { Warp } from "../core/warp.ts";
import type { EnemyRecord, Plan } from "../plan/types.ts";
import { enemyPos, heroSample } from "../plan/sample.ts";
import { HITSTOP_CLEARANCE, HITSTOP_MAX, HITSTOP_MIN } from "./stage3-warp.ts";
import { HURTBOX_LIFT, HURTBOX_RADIUS, spriteOffscreen } from "./stage6-kills.ts";
import { viewRect } from "./stage5-camera.ts";
import { VOICE_BUDGET } from "./stage8-emit.ts";

export interface CheckResult {
  name: string;
  failures: string[];
  /** Number of items checked. */
  checked: number;
}

const MAX_REPORTED = 8;

function beatIndexFromBeats(beats: Float64Array, s: number): number {
  const n = beats.length;
  if (s <= beats[0]) return (s - beats[0]) / (beats[1] - beats[0]);
  if (s >= beats[n - 1]) return n - 1 + (s - beats[n - 1]) / (beats[n - 1] - beats[n - 2]);
  let lo = 0;
  let hi = n - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (beats[mid] <= s) lo = mid;
    else hi = mid;
  }
  return lo + (s - beats[lo]) / (beats[hi] - beats[lo]);
}

export function targetAt(plan: Plan, s: number): number {
  for (const sec of plan.sections) {
    if (s >= sec.s0 && s < sec.s1) return lerp(sec.population[0], sec.population[1], (s - sec.s0) / Math.max(1e-6, sec.s1 - sec.s0));
  }
  const last = plan.sections[plan.sections.length - 1];
  return last.population[1];
}

export function checkPlan(plan: Plan, lib: ContentLibrary): CheckResult[] {
  const warp = new Warp(plan.warp);
  const results: CheckResult[] = [];
  const add = (name: string, failures: string[], checked: number) => results.push({ name, failures, checked });
  const fmt = (s: number) => `s=${s.toFixed(3)}`;
  const tmp = { x: 0, y: 0 };
  const byId = new Map(plan.enemies.map((e) => [e.id, e]));
  const fatesOnHit = plan.hits.map(() => new Set<number>());
  for (const e of plan.enemies) for (const f of e.fates) fatesOnHit[f.hit]?.add(e.id);

  // Impacts on time.
  {
    const f: string[] = [];
    let n = 0;
    for (const a of plan.actions) {
      n++;
      const back = warp.toSong(a.w);
      if (Math.abs(back - a.s) > 0.001) f.push(`${a.move} at ${fmt(a.s)} lands at song time ${back.toFixed(4)}`);
      if (a.kind === "attack" || a.kind === "counter" || a.kind === "finisher") {
        // The impact frame must show on one of the two 60 Hz samples around the impact.
        const k = Math.round(a.w * plan.hero.rate);
        const shows = [k].some((i) => plan.hero.animNames[plan.hero.anim[i]] === a.anim && plan.hero.frame[i] === a.impactFrame);
        const h = heroSample(plan, a.w + 1 / 100);
        if (!shows) f.push(`${a.move} at ${fmt(a.s)} shows ${h.anim} frame ${h.frame}, expected ${a.anim} frame ${a.impactFrame}`);
      }
    }
    for (const m of plan.nearMisses) {
      n++;
      if (Math.abs(warp.toSong(m.w) - m.s) > 0.001) f.push(`${m.kind} near miss at ${fmt(m.s)} is off its slot`);
    }
    add("impacts on time", f, n);
  }

  // Deaths covered.
  {
    const f: string[] = [];
    let n = 0;
    for (const e of plan.enemies) {
      for (const fate of e.fates) {
        if (!fate.lethal) continue;
        n++;
        const h = plan.hits[fate.hit];
        if (!shapeContains(h.shape, fate.x, fate.y, 1)) f.push(`#${e.id} ${e.type} dies at ${fmt(h.s)} outside ${h.what}'s ${h.shape.kind}`);
        enemyPos(plan, e, fate.w, tmp);
        if (Math.hypot(tmp.x - fate.x, tmp.y - fate.y) > 1.5) f.push(`#${e.id} ${e.type} is ${Math.hypot(tmp.x - fate.x, tmp.y - fate.y).toFixed(1)} px from its death spot at ${fmt(h.s)}`);
      }
    }
    add("deaths covered", f, n);
  }

  // Off-screen spawns.
  {
    const f: string[] = [];
    let n = 0;
    for (const e of plan.enemies) {
      if (e.formation !== undefined || e.entrance) continue;
      n++;
      const def = lib.enemies.get(e.type)!;
      enemyPos(plan, e, e.spawnW, tmp);
      const view = viewRect(plan.camera, e.spawnW);
      if (!spriteOffscreen(view, def, tmp.x, tmp.y)) f.push(`#${e.id} ${e.type} spawns on screen at w=${e.spawnW.toFixed(2)} (${tmp.x.toFixed(0)}, ${tmp.y.toFixed(0)})`);
    }
    add("off-screen spawns", f, n);
  }

  // No pass-through.
  {
    const f: string[] = [];
    let n = 0;
    for (const h of plan.hits) {
      if (h.source === "ring") continue;
      n++;
      const b = shapeBounds(h.shape);
      const bucket = plan.activeEnemies[Math.max(0, Math.min(plan.activeEnemies.length - 1, Math.floor(h.w / plan.activeBucket)))];
      for (const id of bucket) {
        const e = byId.get(id)!;
        if (h.w < e.spawnW || h.w > e.deathW - 1e-6) continue;
        if (fatesOnHit[h.id].has(id)) continue;
        enemyPos(plan, e, h.w, tmp);
        if (tmp.x < b[0] - 2 || tmp.x > b[2] + 2 || tmp.y < b[1] - 2 || tmp.y > b[3] + 2) continue;
        if (shapeContains(h.shape, tmp.x, tmp.y, -3)) f.push(`#${e.id} ${e.type} is inside ${h.what}'s ${h.shape.kind} at ${fmt(h.s)} without a fate on it`);
      }
    }
    add("no pass-through", f, n);
  }

  // Spacing.
  {
    const f: string[] = [];
    let n = 0;
    const wEnd = warp.toWorld(plan.duration);
    for (let w = 0; w < wEnd; w += 0.1) {
      const bucket = plan.activeEnemies[Math.max(0, Math.min(plan.activeEnemies.length - 1, Math.floor(w / plan.activeBucket)))];
      const pts: { e: EnemyRecord; x: number; y: number; r: number }[] = [];
      for (const id of bucket) {
        const e = byId.get(id)!;
        // Formation members and enemies in their death animation (from the moment the
        // killing blow lands, within a frame or so) are exempt.
        if (w < e.spawnW || w >= e.deathW - 0.05 || e.formation !== undefined) continue;
        enemyPos(plan, e, w, tmp);
        pts.push({ e, x: tmp.x, y: tmp.y, r: lib.enemies.get(e.type)!.radius });
      }
      const grid = new Map<number, number[]>();
      pts.forEach((p, i) => {
        const k = Math.floor(p.x / 32) * 100003 + Math.floor(p.y / 32);
        let l = grid.get(k);
        if (!l) grid.set(k, (l = []));
        l.push(i);
      });
      for (let i = 0; i < pts.length; i++) {
        const p = pts[i];
        n++;
        const cx = Math.floor(p.x / 32);
        const cy = Math.floor(p.y / 32);
        for (let gx = cx - 1; gx <= cx + 1; gx++)
          for (let gy = cy - 1; gy <= cy + 1; gy++)
            for (const j of grid.get(gx * 100003 + gy) ?? []) {
              if (j <= i) continue;
              const q = pts[j];
              const lim = (p.r + q.r) / 2;
              const d = Math.hypot(p.x - q.x, p.y - q.y);
              if (d < lim) f.push(`#${p.e.id} ${p.e.type} and #${q.e.id} ${q.e.type} are ${d.toFixed(1)} px apart at w=${w.toFixed(1)}`);
            }
      }
    }
    add("spacing", f, n);
  }

  // Near misses stay near.
  {
    const f: string[] = [];
    let n = 0;
    for (const pr of plan.projectiles) {
      n++;
      let minD = Infinity;
      let missD = Infinity;
      for (let w = pr.w0; w <= pr.w1; w += 1 / 240) {
        const u = (w - pr.w0) / (pr.w1 - pr.w0);
        const ax = lerp(pr.x0, pr.x1, u);
        const ay = lerp(pr.y0, pr.y1, u);
        const h = heroSample(plan, w);
        const d = Math.hypot(ax - h.x, ay - (h.y - HURTBOX_LIFT)) - HURTBOX_RADIUS;
        minD = Math.min(minD, d);
        if (Math.abs(w - pr.wMiss) < 1 / 200) missD = Math.min(missD, d);
      }
      if (minD <= 0) f.push(`arrow released at w=${pr.w0.toFixed(2)} touches the hurtbox`);
      if (missD > 24) f.push(`arrow at w=${pr.wMiss.toFixed(2)} misses by ${missD.toFixed(1)} px (more than 24)`);
    }
    for (const sl of plan.slams) {
      n++;
      const h = heroSample(plan, sl.w1);
      const d = Math.hypot(sl.x - h.x, sl.y - h.y) - sl.radius - HURTBOX_RADIUS;
      if (d <= 0) f.push(`slam at w=${sl.w1.toFixed(2)} hits the hero`);
      if (d > 24) f.push(`slam at w=${sl.w1.toFixed(2)} misses by ${d.toFixed(1)} px (more than 24)`);
    }
    add("near misses stay near", f, n);
  }

  // Population.
  {
    const f: string[] = [];
    let n = 0;
    const blastW = plan.hits.find((h) => h.source === "ring")?.w ?? Infinity;
    const alive = (w: number) => {
      let c = 0;
      const bucket = plan.activeEnemies[Math.max(0, Math.min(plan.activeEnemies.length - 1, Math.floor(w / plan.activeBucket)))];
      for (const id of bucket) {
        const e = byId.get(id)!;
        if (w >= e.spawnW && w < e.deathW) c++;
      }
      return c;
    };
    const wb = plan.worldBeats;
    for (let bar = 1; bar + 1 < plan.barStart.length; bar++) {
      const b0 = plan.barStart[bar];
      const b1 = plan.barStart[bar + 1];
      if (b1 === undefined || b1 >= wb.length) break;
      const w0 = wb[b0];
      const w1 = wb[b1];
      if (w1 > blastW - 0.01) break;
      let pSum = 0;
      let tSum = 0;
      let k = 0;
      for (let w = w0; w < w1; w += 0.1) {
        const p = alive(w);
        if (p > 520) f.push(`population ${p} exceeds 520 at w=${w.toFixed(1)}`);
        pSum += p;
        tSum += targetAt(plan, warp.toSong(w));
        k++;
      }
      if (k === 0) continue;
      n++;
      const p = pSum / k;
      const t = tSum / k;
      const tol = Math.max(0.15 * t, 4);
      if (Math.abs(p - t) > tol) f.push(`bar ${bar}: population ${p.toFixed(0)} vs target ${t.toFixed(0)} (±${tol.toFixed(0)})`);
    }
    add("population", f, n);
  }

  // Clean ending.
  {
    const f: string[] = [];
    const ring = plan.hits.filter((h) => h.source === "ring");
    const end = ring.length ? ring[ring.length - 1].w : Infinity;
    for (const e of plan.enemies) if (e.deathW > end + 1e-6) f.push(`#${e.id} ${e.type} is still alive after the finale (dies at w=${e.deathW.toFixed(2)})`);
    if (ring.length === 0) f.push("the plan has no finale blast");
    add("clean ending", f, plan.enemies.length);
  }

  // Hit-stop clearance.
  {
    const f: string[] = [];
    const prim = [...plan.actions.map((a) => a.s), ...plan.nearMisses.map((m) => m.s)].sort((a, b) => a - b);
    const stops = warp.hitStops();
    for (const [s0, s1] of stops) {
      const len = s1 - s0;
      if (len > HITSTOP_MAX + 1e-6) f.push(`hit-stop at ${fmt(s0)} lasts ${(len * 1000).toFixed(0)} ms (max 150)`);
      if (len < HITSTOP_MIN - 1e-6) f.push(`hit-stop at ${fmt(s0)} lasts ${(len * 1000).toFixed(0)} ms (min 40)`);
      const next = prim.find((t) => t > s0 + 1e-6);
      if (next !== undefined && next - s1 < HITSTOP_CLEARANCE - 1e-6) f.push(`hit-stop at ${fmt(s0)} ends ${((next - s1) * 1000).toFixed(0)} ms before the next primary event`);
    }
    add("hit-stop clearance", f, stops.length);
  }

  // Sound budget.
  {
    const f: string[] = [];
    const slots = new Map<number, number>();
    for (const snd of plan.events.sounds) {
      const k = Math.floor(beatIndexFromBeats(plan.songBeats, snd.s) * 4 + 0.5);
      slots.set(k, (slots.get(k) ?? 0) + 1);
    }
    for (const [k, c] of slots) if (c > VOICE_BUDGET) f.push(`sixteenth slot ${k} starts ${c} voices (budget ${VOICE_BUDGET})`);
    add("sound budget", f, slots.size);
  }

  return results;
}

export function summarizeChecks(results: CheckResult[]): { ok: boolean; text: string } {
  const lines: string[] = [];
  let ok = true;
  for (const r of results) {
    if (r.failures.length === 0) lines.push(`  ok    ${r.name} (${r.checked} checked)`);
    else {
      ok = false;
      lines.push(`  FAIL  ${r.name}: ${r.failures.length} of ${r.checked}`);
      for (const m of r.failures.slice(0, MAX_REPORTED)) lines.push(`        ${m}`);
      if (r.failures.length > MAX_REPORTED) lines.push(`        … and ${r.failures.length - MAX_REPORTED} more`);
    }
  }
  return { ok, text: lines.join("\n") };
}
