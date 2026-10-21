/**
 * Checks on the recording of a simulated playthrough. Every failure names the
 * time and entity involved. They run when the recording is built (in the
 * planning worker), in `npm run check:plan` and in the test suite. The
 * checks that need re-simulation (causality, determinism) are in
 * sim-checks.ts.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { shapeContains, wrapAngle } from "../core/geometry.ts";
import { lerp } from "../core/shaping.ts";
import { Warp } from "../core/warp.ts";
import { RouteLookup } from "../mapgen/mapgen.ts";
import type { EnemyRecord, Plan } from "../plan/types.ts";
import { enemyPos, heroSample } from "../plan/sample.ts";
import { aim, catches, type AimTarget } from "../sim/aim.ts";
import { HITSTOP_MAX, HITSTOP_MIN, POP_CAP, spriteOffscreen } from "../sim/simulate.ts";
import { viewRect } from "./camera.ts";
import { VOICE_BUDGET } from "./emit.ts";

export interface CheckResult {
  name: string;
  failures: string[];
  /** Number of items checked. */
  checked: number;
}

const MAX_REPORTED = 8;
/** Recorded enemy positions are 30 Hz samples of fast, surging motion: allow this much interpolation error. */
const SAMPLE_SLACK = 10;
/** The hero track is sampled at 60 Hz and a step moves fast: interpolating mid-step is off by a few pixels. */
const HERO_SLACK = 8;
/** Pace window, measured along the route. */
export const PACE_AHEAD = 60;
export const PACE_BEHIND = 140;

function beatIndexFromBeats(beats: ArrayLike<number>, s: number): number {
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

/** Distance (song seconds) from s to the nearest time in a sorted list. */
function distToNearest(times: ArrayLike<number>, s: number): number {
  let lo = 0;
  let hi = times.length - 1;
  if (hi < 0) return Infinity;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (times[mid] <= s) lo = mid;
    else hi = mid;
  }
  return Math.min(Math.abs(times[lo] - s), Math.abs(times[hi] - s));
}

export function targetAt(plan: Plan, s: number): number {
  for (const sec of plan.sections) {
    if (s >= sec.s0 && s < sec.s1) return lerp(sec.population[0], sec.population[1], (s - sec.s0) / Math.max(1e-6, sec.s1 - sec.s0));
  }
  const last = plan.sections[plan.sections.length - 1];
  return last.population[1];
}

/** Pace point route distance at song time s. */
export function planPaceAt(plan: Plan, s: number): number {
  const p = plan.pace.d;
  const f = Math.max(0, s * plan.pace.rate);
  const k = Math.min(p.length - 2, Math.floor(f));
  return p[k] + (p[k + 1] - p[k]) * Math.min(1, f - k);
}

export function checkPlan(plan: Plan, lib: ContentLibrary): CheckResult[] {
  const warp = new Warp(plan.warp);
  const hero = lib.hero;
  const results: CheckResult[] = [];
  const add = (name: string, failures: string[], checked: number) => results.push({ name, failures, checked });
  const fmt = (s: number) => `s=${s.toFixed(3)}`;
  const tmp = { x: 0, y: 0 };
  const byId = new Map(plan.enemies.map((e) => [e.id, e]));
  const fatesOnHit = plan.hits.map(() => new Set<number>());
  for (const e of plan.enemies) for (const f of e.fates) fatesOnHit[f.hit]?.add(e.id);
  const bucketAt = (w: number) => plan.activeEnemies[Math.max(0, Math.min(plan.activeEnemies.length - 1, Math.floor(w / plan.activeBucket)))];
  const blastW = plan.hits.find((h) => h.source === "ring")?.w ?? Infinity;

  // Impacts on the grid.
  {
    const f: string[] = [];
    let n = 0;
    for (const sw of plan.swings) {
      n++;
      if (distToNearest(plan.grid, sw.s) > 0.001) f.push(`${sw.kind} swing at ${fmt(sw.s)} is off the grid`);
      if (Math.abs(warp.toSong(sw.w) - sw.s) > 0.001) f.push(`${sw.kind} swing for ${fmt(sw.s)} lands at song time ${warp.toSong(sw.w).toFixed(4)}`);
      // The impact frame shows on the 60 Hz sample nearest the strike.
      const k = Math.round(sw.w * plan.hero.rate);
      if (plan.hero.animNames[plan.hero.anim[k]] !== sw.anim || plan.hero.frame[k] !== sw.impactFrame) {
        f.push(`${sw.kind} swing at ${fmt(sw.s)} shows ${plan.hero.animNames[plan.hero.anim[k]]} frame ${plan.hero.frame[k]}, expected ${sw.anim} frame ${sw.impactFrame}`);
      }
    }
    // Each strike's impact sound plays exactly at the strike.
    const impactSounds = plan.events.sounds.filter((snd) => snd.priority <= 0.3 + 1e-9).map((snd) => snd.s);
    for (const sw of plan.swings) {
      n++;
      if (distToNearest(impactSounds, sw.s) > 1e-6) f.push(`${sw.kind} swing at ${fmt(sw.s)} has no impact sound on the strike`);
    }
    // Weapon hits and shockwave bands land on sixteenth notes.
    for (const h of plan.hits) {
      if (h.source !== "weapon" && h.source !== "wave") continue;
      n++;
      const bi = beatIndexFromBeats(plan.songBeats, h.s) * 4;
      if (Math.abs(bi - Math.round(bi)) > 0.004) f.push(`${h.what} hit at ${fmt(h.s)} is off its subdivision`);
    }
    add("impacts on the grid", f, n);
  }

  // Every hero movement has an input: in step movement, a press in the 110 ms before; in free
  // movement, a held key (the hero stops within 60 ms of the last key going up). No step exceeds 44 px.
  {
    const f: string[] = [];
    const t = plan.hero;
    const presses = plan.inputs.filter((i) => i.kind === "press" && i.steps && !i.bend);
    const pressW = presses.map((p) => p.w);
    const stepS = hero.step.durationMs / 1000;
    const stopS = hero.walk.accelMs / 1000;
    // World-time intervals in which at least one key is held during free movement.
    const heldSpans: [number, number][] = [];
    {
      const down = new Map<string, number>();
      let open = 0;
      let since = 0;
      for (const i of plan.inputs) {
        if (i.kind === "press") {
          if (!down.has(i.key)) {
            if (open++ === 0) since = i.w;
            down.set(i.key, i.w);
          }
        } else if (down.delete(i.key) && --open === 0) heldSpans.push([since, i.w]);
      }
      if (open > 0) heldSpans.push([since, Infinity]);
    }
    const heldNear = (w0: number, w1: number) => heldSpans.some(([a, b]) => a <= w1 + 1e-9 && b + stopS >= w0 - 1e-9);
    const movementAt = (w: number) => {
      const s = warp.toSong(w);
      return (plan.sections.findLast((x) => s >= x.s0 - 1e-9) ?? plan.sections[0]).movement;
    };
    let n = 0;
    let pi = 0;
    for (let k = 0; k + 1 < t.x.length; k++) {
      const d = Math.hypot(t.x[k + 1] - t.x[k], t.y[k + 1] - t.y[k]);
      if (d < 0.01) continue;
      n++;
      const w0 = k / t.rate;
      const w1 = (k + 1) / t.rate;
      while (pi + 1 < pressW.length && pressW[pi + 1] <= w1 + 1e-9) pi++;
      const pw = pressW[pi];
      const pressed = pw !== undefined && pw <= w1 + 1e-9 && w0 <= pw + stepS + 1e-6;
      // A walk stops on the boundary into step movement; a step may still be landing after one into free movement.
      if (pressed || (movementAt(w1) === "free" || movementAt(w0) === "free" ? heldNear(w0, w1) : false)) continue;
      f.push(`the hero moves ${d.toFixed(1)} px at w=${w0.toFixed(3)} with no press in the 110 ms before and no key held`);
    }
    for (let i = 0; i < presses.length; i++) {
      const p = presses[i];
      const end = Math.min(p.w + stepS, presses[i + 1]?.w ?? Infinity);
      const a = heroSample(plan, p.w);
      const b = heroSample(plan, end);
      const d = Math.hypot(b.x - a.x, b.y - a.y);
      if (d > hero.step.length + 0.75) f.push(`the step at ${fmt(p.s)} covers ${d.toFixed(1)} px (max ${hero.step.length})`);
    }
    add("every hero movement has an input", f, n + presses.length);
  }

  // No skating: while walking, the walk cycle advances within 10% of the rate the distance implies.
  {
    const f: string[] = [];
    const t = plan.hero;
    const walkI = t.animNames.indexOf("walk");
    const frames = 8;
    let n = 0;
    let k = 0;
    while (k < t.x.length) {
      if (t.anim[k] !== walkI) {
        k++;
        continue;
      }
      // One run of walking samples.
      let dist = 0;
      let adv = 0;
      let j = k;
      while (j + 1 < t.x.length && t.anim[j + 1] === walkI) {
        dist += Math.hypot(t.x[j + 1] - t.x[j], t.y[j + 1] - t.y[j]);
        adv += (t.frame[j + 1] - t.frame[j] + frames) % frames;
        j++;
      }
      if (j - k >= 30) {
        n++;
        const expected = (dist / hero.walk.strideCycle) * frames;
        if (Math.abs(adv - expected) > Math.max(1, 0.1 * expected)) f.push(`the walk from w=${(k / t.rate).toFixed(2)} covers ${dist.toFixed(0)} px but its cycle advances ${adv} frames (expected ${expected.toFixed(1)})`);
      }
      k = j + 1;
    }
    add("no skating", f, n);
  }

  // Auto-aim is the rule.
  {
    const f: string[] = [];
    let n = 0;
    for (const sw of plan.swings) {
      n++;
      const a = sw.aim;
      const targets: AimTarget[] = [];
      for (let i = 0; i < a.targets.length; i += 4) targets.push({ x: a.targets[i], y: a.targets[i + 1], r: a.targets[i + 2], weight: a.targets[i + 3] });
      // A cleave aims with its shockwave's reach.
      const shape = sw.kind === "heavy" ? hero.swings.heavy.hitShape : hero.swings.light.hitShape;
      const radius = sw.kind === "heavy" ? hero.swings.heavy.wave.to : shape.radius;
      const dir = aim(a.x, a.y, a.facing, targets, { radius, halfAngle: (shape.degrees * Math.PI) / 360 });
      if (Math.abs(wrapAngle(dir - sw.dir)) > 1e-6) f.push(`swing at ${fmt(sw.s)} aimed at ${sw.dir.toFixed(3)} rad; auto-aim gives ${dir.toFixed(3)}`);
      // The snapshot is the recorded state: the hero where the track has it, the targets where enemies were.
      const ww = warp.toWorld(sw.sWind);
      const hp = heroSample(plan, ww);
      if (Math.hypot(hp.x - a.x, hp.y - a.y) > HERO_SLACK) f.push(`swing at ${fmt(sw.s)}: the aim snapshot's hero is ${Math.hypot(hp.x - a.x, hp.y - a.y).toFixed(1)} px from the recorded hero`);
      const near: { x: number; y: number }[] = [];
      for (const id of bucketAt(ww)) {
        const e = byId.get(id)!;
        if (ww < e.spawnW || ww >= e.deathW) continue;
        enemyPos(plan, e, ww, tmp);
        near.push({ x: tmp.x, y: tmp.y });
      }
      for (const t of targets) if (!near.some((q) => Math.hypot(q.x - t.x, q.y - t.y) <= SAMPLE_SLACK)) f.push(`swing at ${fmt(sw.s)}: aim target at (${t.x.toFixed(0)}, ${t.y.toFixed(0)}) matches no recorded enemy`);
    }
    add("auto-aim is the rule", f, n);
  }

  // Damage adds up.
  {
    const f: string[] = [];
    let n = 0;
    // A cleave's shockwave hits each enemy at most once.
    const groupHits = new Map<number, Set<number>>();
    for (const h of plan.hits) if (h.group !== undefined) (groupHits.get(h.group) ?? groupHits.set(h.group, new Set()).get(h.group)!).add(h.id);
    for (const h of plan.hits) {
      if (h.source === "ring") continue;
      for (const id of bucketAt(h.w)) {
        const e = byId.get(id)!;
        if (h.w < e.spawnW || h.w > e.deathW - 1e-9) continue;
        if (fatesOnHit[h.id].has(id)) continue;
        if (h.group !== undefined && e.fates.some((f) => groupHits.get(h.group!)!.has(f.hit))) continue;
        // Spawned this very instant, after the hit resolved; or the boss still dropping in.
        if (Math.abs(h.w - e.spawnW) < 1e-9 || (e.entrance === "land" && h.w < e.spawnW + 0.55)) continue;
        enemyPos(plan, e, h.w, tmp);
        const def = lib.enemies.get(e.type)!;
        n++;
        if (shapeContains(h.shape, tmp.x, tmp.y, 0.5 * def.radius - SAMPLE_SLACK)) f.push(`#${e.id} ${e.type} is inside ${h.what}'s ${h.shape.kind} at ${fmt(h.s)} but took no damage`);
      }
    }
    for (const e of plan.enemies) {
      const def = lib.enemies.get(e.type)!;
      let total = 0;
      for (const fate of e.fates) {
        n++;
        const h = plan.hits[fate.hit];
        total += fate.damage;
        if (Math.abs(fate.damage - h.damage) > 1e-9) f.push(`#${e.id} ${e.type} took ${fate.damage.toFixed(2)} from a ${h.damage.toFixed(2)} hit at ${fmt(h.s)}`);
        if (!catches(h.shape, fate.x, fate.y, def.radius)) f.push(`#${e.id} ${e.type} was hit at ${fmt(h.s)} outside ${h.what}'s ${h.shape.kind}`);
        enemyPos(plan, e, fate.w, tmp);
        if (Math.hypot(tmp.x - fate.x, tmp.y - fate.y) > SAMPLE_SLACK) f.push(`#${e.id} ${e.type} is recorded ${Math.hypot(tmp.x - fate.x, tmp.y - fate.y).toFixed(1)} px from where ${h.what} hit it at ${fmt(h.s)}`);
        if (fate.lethal && total < e.health - 1e-9) f.push(`#${e.id} ${e.type} dies at ${fmt(h.s)} after ${total.toFixed(2)} damage (health ${e.health})`);
        // (The boss cannot drop below 1 health before its death chord.)
        if (!fate.lethal && total >= e.health - 1e-9 && def.size !== "boss") f.push(`#${e.id} ${e.type} survives ${total.toFixed(2)} damage at ${fmt(h.s)} (health ${e.health})`);
      }
    }
    add("damage adds up", f, n);
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

  // Population.
  {
    const f: string[] = [];
    let n = 0;
    const alive = (w: number) => {
      let c = 0;
      for (const id of bucketAt(w)) {
        const e = byId.get(id)!;
        if (w >= e.spawnW && w < e.deathW) c++;
      }
      return c;
    };
    const wb = plan.worldBeats;
    const sectionStartBar = plan.sections.map((sec) => {
      const bi = beatIndexFromBeats(plan.songBeats, sec.s0 + 1e-6);
      let bar = 1;
      while (bar + 1 < plan.barStart.length && plan.barStart[bar + 1] <= Math.floor(bi + 1e-6)) bar++;
      return bar;
    });
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
        if (p > POP_CAP) f.push(`population ${p} exceeds ${POP_CAP} at w=${w.toFixed(1)}`);
        pSum += p;
        tSum += targetAt(plan, warp.toSong(w));
        k++;
      }
      if (k === 0) continue;
      // From the fourth bar of each section.
      const si = sectionStartBar.findLastIndex((b) => b <= bar);
      if (si >= 0 && bar < sectionStartBar[si] + 3) continue;
      n++;
      const p = pSum / k;
      const t = tSum / k;
      const tol = Math.max(0.25 * t, 6);
      if (Math.abs(p - t) > tol) f.push(`bar ${bar}: population ${p.toFixed(0)} vs target ${t.toFixed(0)} (±${tol.toFixed(0)})`);
    }
    add("population", f, n);
  }

  // Spacing: at most 1% of sampled nearby pairs closer than 0.5 body widths.
  {
    const f: string[] = [];
    let pairs = 0;
    let close = 0;
    const examples: string[] = [];
    const wEnd = warp.toWorld(plan.duration);
    for (let w = 0; w < wEnd; w += 0.1) {
      const pts: { e: EnemyRecord; x: number; y: number; r: number }[] = [];
      for (const id of bucketAt(w)) {
        const e = byId.get(id)!;
        // Formation members and dying enemies are exempt.
        if (w < e.spawnW || w >= e.deathW - 0.05 || e.formation !== undefined) continue;
        enemyPos(plan, e, w, tmp);
        pts.push({ e, x: tmp.x, y: tmp.y, r: lib.enemies.get(e.type)!.radius });
      }
      const grid = new Map<number, number[]>();
      pts.forEach((p, i) => {
        const k = Math.floor(p.x / 48) * 100003 + Math.floor(p.y / 48);
        let l = grid.get(k);
        if (!l) grid.set(k, (l = []));
        l.push(i);
      });
      for (let i = 0; i < pts.length; i++) {
        const p = pts[i];
        const cx = Math.floor(p.x / 48);
        const cy = Math.floor(p.y / 48);
        for (let gx = cx - 1; gx <= cx + 1; gx++)
          for (let gy = cy - 1; gy <= cy + 1; gy++)
            for (const j of grid.get(gx * 100003 + gy) ?? []) {
              if (j <= i) continue;
              const q = pts[j];
              const d = Math.hypot(p.x - q.x, p.y - q.y);
              if (d >= 2 * (p.r + q.r)) continue;
              pairs++;
              if (d < (p.r + q.r) / 2) {
                close++;
                if (examples.length < MAX_REPORTED) examples.push(`#${p.e.id} ${p.e.type} and #${q.e.id} ${q.e.type} are ${d.toFixed(1)} px apart at w=${w.toFixed(1)}`);
              }
            }
      }
    }
    if (close > 0.01 * pairs) f.push(`${close} of ${pairs} nearby pairs (${((100 * close) / Math.max(1, pairs)).toFixed(2)}%) are closer than half a body width`, ...examples);
    add("spacing", f, pairs);
  }

  // Clean ending.
  {
    const f: string[] = [];
    const ring = plan.hits.filter((h) => h.source === "ring");
    const end = ring.length ? ring[ring.length - 1].w : Infinity;
    for (const e of plan.enemies) if (!(e.deathW <= end + 1e-6)) f.push(`#${e.id} ${e.type} is still alive after the finale`);
    if (ring.length === 0) f.push("the recording has no finale blast");
    add("clean ending", f, plan.enemies.length);
  }

  // Pace: the hero keeps up with the route (skilled and perfect players).
  if (plan.profile !== "casual") {
    const f: string[] = [];
    let n = 0;
    const lookup = new RouteLookup({ step: plan.map.routeStep, x: plan.map.routeX, y: plan.map.routeY });
    const wEnd = Math.min(warp.toWorld(plan.duration), blastW);
    let worst = { ahead: 0, behind: 0, wa: 0, wb: 0 };
    for (let w = 0; w < wEnd; w += 0.1) {
      const h = heroSample(plan, w);
      const d = lookup.nearest(h.x, h.y).d - planPaceAt(plan, warp.toSong(w));
      n++;
      if (d > worst.ahead) worst = { ...worst, ahead: d, wa: w };
      if (-d > worst.behind) worst = { ...worst, behind: -d, wb: w };
    }
    if (worst.ahead > PACE_AHEAD) f.push(`the hero runs ${worst.ahead.toFixed(0)} px ahead of the pace point at w=${worst.wa.toFixed(1)} (max ${PACE_AHEAD})`);
    if (worst.behind > PACE_BEHIND) f.push(`the hero falls ${worst.behind.toFixed(0)} px behind the pace point at w=${worst.wb.toFixed(1)} (max ${PACE_BEHIND})`);
    add("pace", f, n);
  }

  // Signature moments (skilled and perfect players).
  if (plan.profile !== "casual") {
    const f: string[] = [];
    let n = 0;
    const beatAt = (t: number) => {
      const bi = Math.max(0, Math.min(plan.songBeats.length - 2, Math.floor(beatIndexFromBeats(plan.songBeats, t))));
      return plan.songBeats[bi + 1] - plan.songBeats[bi];
    };
    if (plan.boss) {
      n++;
      const { chordS, deathS } = plan.boss;
      if (deathS === undefined) f.push(`the boss never dies (death chord at ${fmt(chordS)})`);
      else if (deathS < chordS - 0.001 || deathS > chordS + beatAt(chordS) + 0.001) f.push(`the boss dies at ${fmt(deathS)}, not within a beat after its death chord at ${fmt(chordS)}`);
    }
    if (plan.threats.length > 0) {
      n++;
      const near = plan.threats.filter((t) => t.outcome === "near-miss").length;
      if (near < 0.9 * plan.threats.length) f.push(`only ${near} of ${plan.threats.length} threats end as near misses (90% needed): ${plan.threats.filter((t) => t.outcome !== "near-miss").map((t) => `${t.kind} at ${fmt(t.s)} ${t.outcome}`).join(", ")}`);
    }
    for (const fm of plan.formations) {
      if (lib.formations.get(fm.def)?.shape !== "kick-line") continue;
      const limit = fm.sRelease + 2 * beatAt(fm.sRelease) + 0.001;
      for (const id of fm.members) {
        n++;
        const e = byId.get(id)!;
        const last = e.fates[e.fates.length - 1];
        const ds = last?.lethal ? plan.hits[last.hit].s : Infinity;
        if (ds > limit) f.push(`kick-line skeleton #${id} dies at ${Number.isFinite(ds) ? fmt(ds) : "never"}, more than two beats after the release at ${fmt(fm.sRelease)}`);
      }
    }
    add("signature moments", f, n);
  }

  // Hit-stop limits.
  {
    const f: string[] = [];
    const stops = warp.hitStops();
    for (const [s0, s1] of stops) {
      const len = s1 - s0;
      if (len > HITSTOP_MAX + 1e-6) f.push(`hit-stop at ${fmt(s0)} lasts ${(len * 1000).toFixed(0)} ms (max 150)`);
      // A press may end a hit-stop early; otherwise it lasts at least the minimum.
      const ended = plan.inputs.some((i) => i.kind === "press" && Math.abs(i.s - s1) < 1e-9);
      if (len < HITSTOP_MIN - 1e-6 && !ended) f.push(`hit-stop at ${fmt(s0)} lasts ${(len * 1000).toFixed(0)} ms (min 40)`);
    }
    for (const h of plan.hits) {
      if (h.primary) continue;
      if (stops.some(([a, b]) => h.s > a + 1e-6 && h.s < b - 1e-6)) f.push(`${h.what} hit at ${fmt(h.s)} fires inside a hit-stop`);
    }
    add("hit-stops", f, stops.length);
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
