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
import { partnerPress, type EnemyRecord, type Plan } from "../plan/types.ts";
import { enemyPos, heroSample } from "../plan/sample.ts";
import { aim, catches, type AimTarget } from "../sim/aim.ts";
import { HITSTOP_MAX, HITSTOP_MIN, KNOCKBACK, POP_CAP, spriteOffscreen, STREAM_SHARE } from "../sim/simulate.ts";
import { viewRect } from "./camera.ts";
import { NEVER_LATE, VOICE_BUDGET, weaponSubdivision, type Subdivision } from "./emit.ts";

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

/** The song time of a fractional beat index (the inverse of beatIndexFromBeats, extrapolating at the ends). */
function timeOfBeatFromBeats(beats: ArrayLike<number>, i: number): number {
  const n = beats.length;
  if (i <= 0) return beats[0] + i * (beats[1] - beats[0]);
  if (i >= n - 1) return beats[n - 1] + (i - (n - 1)) * (beats[n - 1] - beats[n - 2]);
  const k = Math.floor(i);
  return beats[k] + (i - k) * (beats[k + 1] - beats[k]);
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

/** Song-time spans in which a direction key is held: from each press to its key's release (Space, a stomp, moves nothing). */
export function heldSpans(inputs: Plan["inputs"]): [number, number][] {
  const out: [number, number][] = [];
  const down = new Map<string, number>();
  for (const i of inputs) {
    if (i.key === "space") continue;
    if (i.kind === "press") {
      if (!down.has(i.key)) down.set(i.key, i.s);
    } else if (down.has(i.key)) {
      out.push([down.get(i.key)!, i.s]);
      down.delete(i.key);
    }
  }
  for (const s0 of down.values()) out.push([s0, Infinity]);
  return out.sort((a, b) => a[0] - b[0]);
}

/** Is a key held at song time s (a press at s counts, a release at s does not)? */
export const heldAt = (spans: [number, number][], s: number): boolean => spans.some(([a, b]) => a <= s + 1e-9 && b > s + 1e-9);

/**
 * One stride at song time s, at most: the walk speed times the stride period of the step s falls in
 * or of the step just ended (a held stride's limit), whichever is longer.
 */
export function strideLimitAt(plan: Plan, s: number): number {
  return plan.strides.speed * Math.max(stridePeriodAt(plan, s), stridePeriodAt(plan, s - 1e-6));
}

/** Reactive mode: has the plan switched to free movement at song time s (while the beat is unclear)? */
export function modeFreeAt(plan: Pick<Plan, "modes">, s: number): boolean {
  return plan.modes?.findLast((m) => m.s <= s + 1e-9)?.free ?? false;
}

/** The stride period at song time s: the step it falls in, or the free period without a steady beat. */
export function stridePeriodAt(plan: Plan, s: number): number {
  const st = plan.strides;
  const sec = plan.sections.findLast((x) => s >= x.s0 - 1e-9) ?? plan.sections[0];
  if (sec.movement === "free" || st.steps.length === 0 || modeFreeAt(plan, s)) return st.freePeriodS;
  let k = 0;
  while (k + 1 < st.steps.length && st.steps[k + 1] <= s + 1e-9) k++;
  return st.periods[k];
}

/** The "every hero movement has an input" check under fixed-speed strides. */
function checkStrideInputs(plan: Plan, warp: Warp, stopS: number): CheckResult {
  const f: string[] = [];
  const fmt = (s: number) => `s=${s.toFixed(3)}`;
  const st = plan.strides;
  const t = plan.hero;
  const spans = heldSpans(plan.inputs);
  const movementAt = (s: number) => (modeFreeAt(plan, s) ? "free" : (plan.sections.findLast((x) => s >= x.s0 - 1e-9) ?? plan.sections[0]).movement);
  // Every stride start: each press, and each step of the stride period (in step movement) while a key is held.
  // (A held step at the very moment a press strides adds nothing: the press's stride stands. A stomp
  // with Space starts no movement: a rock step back belongs to the stride it rocks.)
  const all: { s: number; w: number; held: boolean }[] = [];
  for (const i of plan.inputs) if (i.kind === "press" && !partnerPress(i) && i.key !== "space") all.push({ s: i.s, w: i.w, held: false });
  for (const s of st.steps) if (movementAt(s) === "steps" && heldAt(spans, s)) all.push({ s, w: warp.toWorld(s), held: true });
  all.sort((a, b) => a.w - b.w || Number(a.held) - Number(b.held) || a.s - b.s);
  const starts: typeof all = [];
  for (const x of all) if (!x.held || starts.length === 0 || Math.abs(x.w - starts[starts.length - 1].w) >= 1e-9) starts.push(x);
  const startW = starts.map((x) => x.w);
  const heldW = spans.map(([a, b]): [number, number] => [warp.toWorld(a), Number.isFinite(b) ? warp.toWorld(b) : Infinity]);
  const walkingNear = (w0: number, w1: number) => heldW.some(([a, b]) => a <= w1 + 1e-9 && b + stopS >= w0 - 1e-9);
  let n = 0;
  let si = -1;
  for (let k = 0; k + 1 < t.x.length; k++) {
    const d = Math.hypot(t.x[k + 1] - t.x[k], t.y[k + 1] - t.y[k]);
    if (d < 0.01) continue;
    n++;
    const w0 = k / t.rate;
    const w1 = (k + 1) / t.rate;
    while (si + 1 < startW.length && startW[si + 1] <= w1 + 1e-9) si++;
    const sw = startW[si];
    if (sw !== undefined && w0 <= sw + st.durationS + 1e-6) continue;
    // Free movement walks while a key is held (stopping within accelMs of the last release).
    const s0 = warp.toSong(w0);
    const s1 = warp.toSong(w1);
    if ((movementAt(s0) === "free" || movementAt(s1) === "free") && walkingNear(w0, w1)) continue;
    f.push(`the hero moves ${d.toFixed(1)} px at w=${w0.toFixed(3)} with no stride started in the ${(st.durationS * 1000).toFixed(0)} ms before (no press, no held step) and no key held to walk`);
  }
  // No stride covers more than one stride: from its start to its landing, or to the next stride's start.
  for (let i = 0; i < starts.length; i++) {
    const p = starts[i];
    const next = starts[i + 1]?.w ?? Infinity;
    const end = Math.min(Math.floor((p.w + st.durationS) * t.rate + 1e-9) / t.rate, Math.floor(next * t.rate - 1e-9) / t.rate);
    if (end <= p.w) continue;
    const a = heroSample(plan, p.w);
    const b = heroSample(plan, end);
    const d = Math.hypot(b.x - a.x, b.y - a.y);
    const one = strideLimitAt(plan, p.s);
    // (The start is interpolated between the hero's samples: when a stride was already moving there,
    // as a held stride that a press takes over a few milliseconds in, it may be off by up to the
    // ground covered between those samples.)
    const k = Math.floor(p.w * t.rate + 1e-9);
    const slack = k + 1 < t.x.length ? Math.hypot(t.x[k + 1] - t.x[k], t.y[k + 1] - t.y[k]) : 0;
    if (d > one + 2 + slack) f.push(`the ${p.held ? "held stride" : "stride"} at ${fmt(p.s)} covers ${d.toFixed(1)} px (one stride is ${one.toFixed(1)})`);
  }
  return { name: "every hero movement has an input", failures: f, checked: n + starts.length };
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
    // Press cleaves: the heavy impact's sound is on the accent, and the shockwave's first band on the
    // eighth after it, or on the first sixteenth at least NEVER_LATE after a late press; the bands
    // then follow on sixteenths (checked with the weapon hits above).
    {
      const waveHits = plan.hits.filter((h) => h.source === "wave").map((h) => h.s);
      // Every eligible accent's swing is heavy, whatever was pressed, with its sound on the accent.
      const heavySounds = plan.events.sounds.filter((snd) => snd.key === "hit-heavy").map((snd) => snd.s);
      const heavySwings = plan.swings.filter((sw) => sw.kind === "heavy").map((sw) => sw.s);
      const blastS = plan.hits.find((h) => h.source === "ring")?.s ?? Infinity;
      for (const a of plan.cleaveAccents) {
        if (a.s >= blastS - 0.04) continue;
        n++;
        if (distToNearest(heavySwings, a.s) > 1e-6) f.push(`eligible accent at ${fmt(a.s)} has no heavy swing`);
        else if (distToNearest(heavySounds, a.s) > 1e-6) f.push(`eligible accent at ${fmt(a.s)} has no heavy impact sound on it`);
      }
      for (const c of plan.cleaves) {
        n++;
        const bi = beatIndexFromBeats(plan.songBeats, c.s);
        let k = bi + 0.5;
        while (timeOfBeatFromBeats(plan.songBeats, k) - c.press < NEVER_LATE - 1e-6) k += 0.25;
        const want = timeOfBeatFromBeats(plan.songBeats, k);
        if (Math.abs(c.band0 - want) > 0.001) f.push(`cleave on ${fmt(c.s)} (press ${fmt(c.press)}): first band at ${fmt(c.band0)}, expected ${fmt(want)}`);
        // (A band rests in a hit-stop, while world time stands still: the next one covers its ground.)
        const frozen = warp.toWorld(c.band0) - warp.toWorld(c.band0 - 0.005) < 1e-6;
        if (!frozen && distToNearest(waveHits, c.band0) > 0.001) f.push(`cleave on ${fmt(c.s)}: no shockwave band at ${fmt(c.band0)}`);
      }
    }
    // Grid sounds: every gameplay sound of the table in "Sound on the Grid" plays on a slot of its
    // subdivision, within 1 ms (the timing grid's slots are beats in a free section).
    {
      const rows = soundRows(lib);
      const downbeats = plan.barStart.filter((b) => Number.isFinite(b)).map((b) => plan.songBeats[b]);
      const freeAt = (t: number) => plan.sections.findLast((x) => t >= x.s0 - 1e-9)?.movement === "free";
      const onSlot = (every: Subdivision, t: number): boolean => {
        if (every === "grid" && !freeAt(t) && t <= plan.grid[plan.grid.length - 1] + 0.001) return distToNearest(plan.grid, t) <= 0.001;
        if (every === "bar") return distToNearest(downbeats, t) <= 0.001;
        const per = every === "sixteenth" ? 4 : every === "eighth" ? 2 : 1;
        // The nearest slot, back in song time (beats extrapolate past the song map's ends, as in emission).
        return Math.abs(t - timeOfBeatFromBeats(plan.songBeats, Math.round(beatIndexFromBeats(plan.songBeats, t) * per) / per)) <= 0.001;
      };
      for (const snd of plan.events.sounds) {
        const row = snd.key !== undefined ? rows.get(snd.key) : undefined;
        if (!row) continue;
        n++;
        const every = row === "kill" ? (freeAt(snd.s) ? "beat" : "sixteenth") : row;
        if (!onSlot(every, snd.s)) f.push(`${snd.key} sound at ${fmt(snd.s)} is off its ${every === "grid" ? "grid time" : every}`);
        if (snd.movedFrom !== undefined && !(snd.movedFrom < snd.s)) f.push(`${snd.key} sound at ${fmt(snd.s)} moved from ${fmt(snd.movedFrom)}, not to a later slot`);
      }
    }
    add("impacts on the grid", f, n);
  }

  // Every hero movement has an input: a stride starts only at a press or at a step of the stride
  // period while a key is held, and none covers more than one stride.
  results.push(checkStrideInputs(plan, warp, hero.walk.accelMs / 1000));

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
      // A cleave aims with its shockwave's reach; a swing sized by its tier (self-firing weapons) with the reach it recorded.
      const shape = sw.reach ?? (sw.kind === "heavy" ? hero.swings.heavy.hitShape : hero.swings.light.hitShape);
      const radius = sw.reach ? sw.reach.radius : sw.kind === "heavy" ? hero.swings.heavy.wave.to : shape.radius;
      const dir = aim(a.x, a.y, a.facing, targets, { radius, halfAngle: (shape.degrees * Math.PI) / 360 });
      if (Math.abs(wrapAngle(dir - sw.dir)) > 1e-6) f.push(`swing at ${fmt(sw.s)} aimed at ${sw.dir.toFixed(3)} rad; auto-aim gives ${dir.toFixed(3)}`);
      // The snapshot is the recorded state: the hero where the track has it, the targets where enemies were.
      const ww = warp.toWorld(sw.sWind);
      const hp = heroSample(plan, ww);
      if (Math.hypot(hp.x - a.x, hp.y - a.y) > HERO_SLACK) f.push(`swing at ${fmt(sw.s)}: the aim snapshot's hero is ${Math.hypot(hp.x - a.x, hp.y - a.y).toFixed(1)} px from the recorded hero`);
      const near: { x: number; y: number }[] = [];
      for (const id of bucketAt(ww)) {
        const e = byId.get(id)!;
        // (In a hit-stop world time stands still: an enemy dying at the wind-up's world time was still
        // alive at it when its death came later in song time, or at the same instant, after the
        // wind-up looked: a weapon firing on the wind-up's own slot.)
        const diesLater = Math.abs(ww - e.deathW) <= 1e-9 && (plan.hits[e.fates[e.fates.length - 1]?.hit]?.s ?? -Infinity) >= sw.sWind - 1e-9;
        if (ww < e.spawnW || (ww >= e.deathW && !diesLater)) continue;
        enemyPos(plan, e, ww, tmp);
        near.push({ x: tmp.x, y: tmp.y });
        // (One spawned within a sample of the wind-up, in a hit-stop's frozen world time, is recorded
        // from the next sample on, after it has moved on: its first sample stands for it too.)
        if (e.spawnW > ww - 1 / plan.enemySampleRate) {
          enemyPos(plan, e, e.spawnW + 1 / plan.enemySampleRate, tmp);
          near.push({ x: tmp.x, y: tmp.y });
        }
      }
      // (A wind-up in a hit-stop's frozen world time: enemies knocked back in it are recorded at one
      // of the places they passed, up to a knockback apart.)
      const frozen = ww - warp.toWorld(sw.sWind - 0.02) < 1e-6;
      const slack = frozen ? SAMPLE_SLACK + KNOCKBACK : SAMPLE_SLACK;
      for (const t of targets) if (!near.some((q) => Math.hypot(q.x - t.x, q.y - t.y) <= slack)) f.push(`swing at ${fmt(sw.s)}: aim target at (${t.x.toFixed(0)}, ${t.y.toFixed(0)}) matches no recorded enemy`);
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
        if (!fate.lethal && total >= e.health - 1e-9) f.push(`#${e.id} ${e.type} survives ${total.toFixed(2)} damage at ${fmt(h.s)} (health ${e.health})`);
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
    // The steady stream (enemies outside any formation, ring or stampede) is held to STREAM_SHARE of
    // the curve, and set pieces add to it: the cap is on everyone.
    const alive = (w: number, streamOnly = false) => {
      let c = 0;
      for (const id of bucketAt(w)) {
        const e = byId.get(id)!;
        if (w >= e.spawnW && w < e.deathW && !(streamOnly && e.formation !== undefined)) c++;
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
    // Press cleaves: a cleave's shockwave clears a crowd on purpose (a full ring at tier 3), and the
    // stream takes a bar to refill: the bar of its accent and the next one aren't held to the target.
    const barOf = (s: number) => {
      const bi = Math.floor(beatIndexFromBeats(plan.songBeats, s) + 1e-6);
      let bar = 1;
      while (bar + 1 < plan.barStart.length && plan.barStart[bar + 1] <= bi) bar++;
      return bar;
    };
    const cleaveBars = new Set(plan.cleaves.flatMap((c) => [barOf(c.s), barOf(c.s) + 1]));
    for (let bar = 1; bar + 1 < plan.barStart.length; bar++) {
      const b0 = plan.barStart[bar];
      const b1 = plan.barStart[bar + 1];
      if (b1 === undefined || b1 >= wb.length) break;
      const w0 = wb[b0];
      const w1 = wb[b1];
      if (w1 > blastW - 0.01) break;
      if (cleaveBars.has(bar)) continue;
      let pSum = 0;
      let tSum = 0;
      let k = 0;
      for (let w = w0; w < w1; w += 0.1) {
        const p = alive(w);
        if (p > POP_CAP) f.push(`population ${p} exceeds ${POP_CAP} at w=${w.toFixed(1)}`);
        pSum += alive(w, true);
        tSum += targetAt(plan, warp.toSong(w)) * STREAM_SHARE;
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
      if (Math.abs(p - t) > tol) f.push(`bar ${bar}: the stream's population ${p.toFixed(0)} vs target ${t.toFixed(0)} (±${tol.toFixed(0)})`);
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

/**
 * The rows of "Sound on the Grid" that exist so far, by emission key: the subdivision each keeps to
 * (kills: sixteenths, or beats in a free section; the timing grid: beats in a free section and past its end). The sword's impact is checked against its strike
 * above; its whoosh is the impact's lead-in, and strides' rustle is exempt. Gem chimes (gems on the
 * field: chime) and the vacuum's pickup play on sixteenths; orbs, chickens and hurt arrive with the
 * beads that add those rows.
 */
export function soundRows(lib: ContentLibrary): Map<string, Subdivision | "kill"> {
  const rows = new Map<string, Subdivision | "kill">([
    ["kill", "kill"],
    ["kill-multi", "kill"],
    ["tier-up", "grid"],
    ["tier-down", "grid"],
    ["orb", "grid"],
    ["hurt", "sixteenth"],
    ["chicken", "sixteenth"],
    ["level-up", "bar"],
    ["near-miss", "beat"],
    ["gem", "sixteenth"],
    ["chime", "sixteenth"],
    ["vacuum", "sixteenth"],
  ]);
  for (const wd of lib.weapons.values()) if (wd.kind !== "blast") rows.set(wd.sound, weaponSubdivision(wd));
  return rows;
}
