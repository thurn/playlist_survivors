/**
 * The simulated playthrough: the game rules run forward in time, driven by key
 * presses from a player, and everything they produce is recorded.
 *
 * The rule that keeps it honest: the simulation may use the song (the
 * schedule) and the past and present, never a future press. Each step reads
 * only presses that have already happened, so re-running with every press
 * after a time T removed reproduces the recording exactly up to T.
 *
 * Time advances in fixed steps of 1/120 s of song time. Presses, strikes,
 * weapon hits and other events happen at their exact times between steps.
 */
import type { EnemyDef, HeroDef, WeaponDef } from "../content/schema.ts";
import { TAU, type Shape } from "../core/geometry.ts";
import { Rng } from "../core/rng.ts";
import { beatHit, beatSwell, clamp, frac, lerp, smoothstep } from "../core/shaping.ts";
import { beatIndexAt, energyAt, timeOfBeatIndex, barOfBeatIndex } from "../core/songmap.ts";
import type { WarpBreak } from "../core/warp.ts";
import { RouteLookup, routeFrame } from "../mapgen/mapgen.ts";
import type { AimSnapshot, Chain, EnemyRecord, Fate, HeroTrack, Hit, MapLayout, PlanInput, PlanSwing } from "../plan/types.ts";
import { CameraRig } from "../planner/camera.ts";
import { sectionIndexAt, subdivisionBeats, weightFactor, type Ctx } from "../planner/context.ts";
import { aim, catches, swingArc, type AimTarget } from "./aim.ts";
import { bendStep, startStep, stepEnd, stepPose, stepSeconds, walkFrame, walkTarget, type Clamp, type Key, type Step } from "./movement.ts";
import type { KeyEvent, Observation, Player } from "./player.ts";
import { nextStepAfter, paceAt, stepRate, type GridSlot, type Schedule } from "./schedule.ts";
import { armedStrike, damageMultiplier, grooveAfterPress, grooveDecay, ratePressOnGrid } from "./timing.ts";

/** Fixed simulation steps per song second. */
export const SIM_RATE = 120;
/** Hero and camera samples per world-second. */
export const HERO_RATE = 60;
/** Enemy position samples per world-second. */
export const ENEMY_RATE = 30;
/** The hero's body radius (the hurtbox) and its height above the feet. */
export const HURTBOX_RADIUS = 10;
export const HURTBOX_LIFT = 20;
/** Enemies stop this far beyond touching the hero. */
const CONTACT_GAP = 4;
/** The walkable band: the corridor less this margin. */
const CORRIDOR_MARGIN = 48;
/** At most this many spawns per beat. */
export const SPAWN_PER_BEAT = 40;
/** Share of spawns from the forward half of the view. */
const SPAWN_FORWARD = 0.6;
/** Off-screen spawn margin (the check uses 16 px; the rest covers camera drift between samples). */
export const SPAWN_MARGIN = 16;
const SPAWN_EXTRA = 12;
/** Neighbours closer than this many body widths push apart. */
const SEPARATION = 0.8;
/** Positional correction keeps centres at least this fraction of the summed radii apart. */
const MIN_GAP = 0.6;
/** Non-lethal hits knock an enemy this far away from the hero. */
const KNOCKBACK = 10;
const KNOCK_TIME = 0.12;
/** An enemy this many view widths behind the hero is recycled to a spawn point ahead. */
const RECYCLE_VIEWS = 1.5;
/** The hard population cap. */
export const POP_CAP = 520;
/** Hit-stop limits (seconds). */
export const HITSTOP_MAX = 0.15;
export const HITSTOP_MIN = 0.04;
/** The groove only decays after this many bars without a press. */
const GROOVE_GRACE_BARS = 2;

export const HERO_ANIMS = ["idle", "walk", "run", "jump", "slash", "backslash", "halfslash", "thrust", "spellcast", "slash-oversize", "hurt"];

export interface SimOptions {
  player: Player;
  /** Causality check: drop every press after this song time (the player stops; the game keeps running). */
  cutS?: number;
  /** Stop the simulation at this song time (default: the end of the song). */
  untilS?: number;
}

export interface Landing {
  s: number;
  w: number;
  x: number;
  y: number;
}

export interface SimResult {
  warp: WarpBreak[];
  hero: HeroTrack;
  camera: CameraRig;
  hits: Hit[];
  enemies: EnemyRecord[];
  enemySamples: Float32Array;
  swings: PlanSwing[];
  inputs: PlanInput[];
  groove: { s: number[]; v: number[] };
  landings: Landing[];
  weaponsGranted: { id: string; s: number }[];
  /** Song times each weapon fired. */
  weaponFires: { weapon: string; s: number }[];
  chains: Chain[];
  hitStops: [number, number][];
  blastS?: number;
  /** Song time the simulation ended. */
  endS: number;
}

/** Per-enemy simulation state. */
interface Foe {
  rec: EnemyRecord;
  def: EnemyDef;
  r: number;
  health: number;
  x: number;
  y: number;
  vx: number;
  vy: number;
  /** World time of (x, y): enemies integrate only on the fixed steps, and in between they are
   * extrapolated from the last step's state, so no event can reach back and change them. */
  lastW: number;
  alive: boolean;
  /** Record one last sample (the death spot), then retire. */
  finalSample: boolean;
  samples: number[];
  sx: number;
  sy: number;
  knockX: number;
  knockY: number;
  knockT: number;
  weight: number;
}

interface Swing {
  sArmed: number;
  sWind: number;
  sStrike: number;
  slot: GridSlot;
  heavy: boolean;
  anim: string;
  impact: number;
  frames: number;
  press?: number;
  accent: number;
  dir: number;
  aimed: boolean;
  struck: boolean;
  wStrike: number;
  /** Follow-through frames per world-second. */
  fps: number;
  record?: PlanSwing;
}

type Ev =
  | { kind: "decide" }
  | { kind: "key"; e: KeyEvent }
  | { kind: "hitstopEnd" }
  | { kind: "wind"; sw: Swing }
  | { kind: "strike"; sw: Swing }
  | { kind: "fire"; weapon: WeaponDef; bi: number }
  | { kind: "chain"; weapon: WeaponDef; chain: ChainState; jump: number }
  | { kind: "blast"; band: number }
  | { kind: "beat"; bi: number }
  | { kind: "appear"; type: string }
  | { kind: "levelUp"; id: string }
  | { kind: "mode"; steps: boolean }
  | { kind: "auto"; slot: GridSlot }
  | { kind: "idle"; slot: GridSlot; prev: number };

interface ChainState {
  points: { w: number; x: number; y: number }[];
  hit: Set<Foe>;
  jumps: number;
  energy: number;
  bi: number;
}

/** Event order at equal times. */
const ORDER: Record<Ev["kind"], number> = { hitstopEnd: 0, mode: 1, levelUp: 2, decide: 3, key: 4, idle: 5, auto: 6, wind: 7, strike: 8, fire: 9, chain: 10, blast: 11, beat: 12, appear: 13 };

/** Free movement: analytic walking from a velocity toward a target velocity at a constant acceleration. */
interface Walk {
  w0: number;
  x0: number;
  y0: number;
  vx0: number;
  vy0: number;
  tx: number;
  ty: number;
  /** Seconds to reach the target velocity. */
  t: number;
}

function walkPose(wk: Walk, atW: number): { x: number; y: number; vx: number; vy: number } {
  const dt = Math.max(0, atW - wk.w0);
  if (dt <= wk.t) {
    const u = wk.t > 0 ? dt / wk.t : 1;
    return {
      x: wk.x0 + wk.vx0 * dt + ((wk.tx - wk.vx0) * dt * u) / 2,
      y: wk.y0 + wk.vy0 * dt + ((wk.ty - wk.vy0) * dt * u) / 2,
      vx: wk.vx0 + (wk.tx - wk.vx0) * u,
      vy: wk.vy0 + (wk.ty - wk.vy0) * u,
    };
  }
  return {
    x: wk.x0 + ((wk.vx0 + wk.tx) * wk.t) / 2 + wk.tx * (dt - wk.t),
    y: wk.y0 + ((wk.vy0 + wk.ty) * wk.t) / 2 + wk.ty * (dt - wk.t),
    vx: wk.tx,
    vy: wk.ty,
  };
}

class Queue {
  private heap: { s: number; o: number; seq: number; ev: Ev }[] = [];
  private seq = 0;
  push(s: number, ev: Ev): void {
    const h = this.heap;
    h.push({ s, o: ORDER[ev.kind], seq: this.seq++, ev });
    let c = h.length - 1;
    while (c > 0) {
      const p = (c - 1) >> 1;
      if (!this.less(h[c], h[p])) break;
      [h[c], h[p]] = [h[p], h[c]];
      c = p;
    }
  }
  private less(a: { s: number; o: number; seq: number }, b: { s: number; o: number; seq: number }): boolean {
    return a.s < b.s || (a.s === b.s && (a.o < b.o || (a.o === b.o && a.seq < b.seq)));
  }
  peek(): number {
    return this.heap.length > 0 ? this.heap[0].s : Infinity;
  }
  pop(): { s: number; ev: Ev } {
    const h = this.heap;
    const top = h[0];
    const last = h.pop()!;
    if (h.length > 0) {
      h[0] = last;
      let c = 0;
      for (;;) {
        const l = 2 * c + 1;
        const r = l + 1;
        let m = c;
        if (l < h.length && this.less(h[l], h[m])) m = l;
        if (r < h.length && this.less(h[r], h[m])) m = r;
        if (m === c) break;
        [h[m], h[c]] = [h[c], h[m]];
        c = m;
      }
    }
    return top;
  }
}

/** Sprite half extents (x, up, down) used for off-screen spawns and checks. */
export function spriteExtents(def: EnemyDef): { hx: number; up: number; down: number } {
  const big = def.atlas === "slime" || def.atlas === "bat" || def.atlas === "bee" || def.atlas === "eyeball";
  const s = def.scale;
  if (big) return { hx: 16 * s, up: (32 + def.hover) * s, down: 6 * s };
  return { hx: 32 * s, up: (58 + def.hover) * s, down: 8 * s };
}

/** True if the sprite at (x, y) lies fully outside the view plus margin. */
export function spriteOffscreen(view: [number, number, number, number], def: EnemyDef, x: number, y: number, margin = SPAWN_MARGIN): boolean {
  const ext = spriteExtents(def);
  return x + ext.hx < view[0] - margin || x - ext.hx > view[2] + margin || y + ext.down < view[1] - margin || y - ext.up > view[3] + margin;
}

/** Target population at song time s: the section's population range, interpolated. */
export function targetPopulation(ctx: Ctx, s: number): number {
  const sec = ctx.sections[sectionIndexAt(ctx, s)];
  const u = clamp((s - sec.s0) / Math.max(1e-6, sec.s1 - sec.s0), 0, 1);
  return lerp(sec.level.population[0], sec.level.population[1], u);
}

export function facingIndex(dir: number): number {
  // 0 up, 1 left, 2 down, 3 right (atlas FACINGS order).
  const a = Math.atan2(Math.sin(dir), Math.cos(dir));
  if (a > -Math.PI / 4 && a <= Math.PI / 4) return 3;
  if (a > Math.PI / 4 && a <= (3 * Math.PI) / 4) return 2;
  if (a <= -Math.PI / 4 && a > (-3 * Math.PI) / 4) return 0;
  return 1;
}

/** The walkable band around the route. */
export class Corridor {
  private lookup: RouteLookup;
  readonly half: number;
  constructor(readonly map: MapLayout) {
    this.lookup = new RouteLookup({ step: map.routeStep, x: map.routeX, y: map.routeY });
    this.half = map.corridorHalfWidth - CORRIDOR_MARGIN;
  }
  /** Distance from (x, y) to the band's edge (negative outside). */
  edge(x: number, y: number): number {
    return this.half - Math.abs(this.lookup.nearest(x, y).lat);
  }
  /** Route distance and lateral offset of the nearest route point. */
  nearest(x: number, y: number): { d: number; lat: number } {
    return this.lookup.nearest(x, y);
  }
  /** Wall rule for steps: the longest straight move that stays in the band. */
  readonly clamp: Clamp = (x, y, dx, dy, len) => {
    if (this.edge(x + dx * len, y + dy * len) >= 0) return len;
    if (this.edge(x, y) < 0) return 0;
    let lo = 0;
    let hi = len;
    for (let i = 0; i < 12; i++) {
      const mid = (lo + hi) / 2;
      if (this.edge(x + dx * mid, y + dy * mid) >= 0) lo = mid;
      else hi = mid;
    }
    return lo;
  };
}

export function simulate(ctx: Ctx, sch: Schedule, map: MapLayout, opts: SimOptions): SimResult {
  const hero: HeroDef = ctx.lib.hero;
  const song = ctx.song;
  const lib = ctx.lib;
  const route = { step: map.routeStep, x: map.routeX, y: map.routeY };
  const corridor = new Corridor(map);
  const endS = Math.min(opts.untilS ?? Infinity, ctx.duration + 1);
  const cutS = opts.cutS ?? Infinity;
  const player = opts.player;
  const lightReach = { radius: hero.swings.light.hitShape.radius, halfAngle: (hero.swings.light.hitShape.degrees * Math.PI) / 360 };
  const heavyReach = { radius: hero.swings.heavy.hitShape.radius, halfAngle: (hero.swings.heavy.hitShape.degrees * Math.PI) / 360 };
  const anims = ctx.heroAnims;
  const animIdx = (name: string) => Math.max(0, HERO_ANIMS.indexOf(name));
  const pacePoint = (s: number) => routeFrame(route, paceAt(sch, s));
  const sectionAt = (s: number) => ctx.sections[sectionIndexAt(ctx, s)];

  // ---------------------------------------------------------------- state
  let s = 0;
  let w = 0;
  const q = new Queue();
  const breaks: WarpBreak[] = [];
  let stopStart = -1;
  let stopEnd = -1;
  const hitStops: [number, number][] = [];

  const p0 = pacePoint(0);
  let restPose = { x: p0.x, y: p0.y };
  let step: Step | undefined;
  /** Free movement: the current walk (undefined in step movement). */
  let walk: Walk | undefined;
  const held = new Set<Key>();
  let walked = 0;
  let lastSample: { x: number; y: number } | undefined;
  let landed = true;
  let facingAngle = 0;
  const landings: Landing[] = [];

  let groove = 0;
  let lastPressS = -Infinity;
  const grooveS: number[] = [0];
  const grooveV: number[] = [0];

  const inputs: PlanInput[] = [];
  const swings: Swing[] = [];
  const liveSwings: Swing[] = [];
  const takenSlots = new Set<GridSlot>();
  let lightCount = 0;
  const swingRecords: PlanSwing[] = [];
  const hits: Hit[] = [];
  const punches: { s: number; amt: number }[] = [];
  for (const a of song.accents) {
    const sec = sectionAt(a.time);
    if (sec.level.camera.punchOnAccents) punches.push({ s: a.time, amt: 0.02 + 0.02 * a.strength });
  }

  const foes: Foe[] = [];
  let alive: Foe[] = [];
  let pending = 0;
  let nextId = 0;
  const records: EnemyRecord[] = [];
  const foeSamples: Foe[] = [];

  const weaponsGranted: { id: string; s: number }[] = [];
  const weaponFires: { weapon: string; s: number }[] = [];
  const chains: Chain[] = [];
  const blastS = sch.blast?.s;
  let blasting = false;

  // Hero and camera tracks.
  const hx: number[] = [];
  const hy: number[] = [];
  const hz: number[] = [];
  const hFacing: number[] = [];
  const hAnim: number[] = [];
  const hFrame: number[] = [];
  const camera = new CameraRig();
  let nextHeroK = 0;
  let nextFoeK = 0;

  // ---------------------------------------------------------------- time
  const baseRate = (t: number) => stepRate(sch.baseSteps, t);
  const inStop = (t: number) => t >= stopStart - 1e-12 && t < stopEnd - 1e-12;
  const rateAt = (t: number) => (inStop(t) ? 0 : baseRate(t));
  const pushBreak = (rate: number) => {
    const last = breaks[breaks.length - 1];
    if (last && last.rate === rate) return;
    if (last && Math.abs(last.s - s) < 1e-12) {
      breaks[breaks.length - 1] = { s, w, rate };
      return;
    }
    breaks.push({ s, w, rate });
  };
  /** Song beat index at song time t. */
  const sbiAt = (t: number) => beatIndexAt(song, t);

  // ---------------------------------------------------------------- hero
  /**
   * The hero at world time atW: walking (free movement), or on its latest step
   * (which ends standing still at its landing spot).
   */
  const heroPose = (atW: number): { x: number; y: number; z: number; u: number; vx: number; vy: number } => {
    if (walk) return { ...walkPose(walk, atW), z: 0, u: 1 };
    if (step) return { ...stepPose(hero, step, atW), vx: 0, vy: 0 };
    return { x: restPose.x, y: restPose.y, z: 0, u: 1, vx: 0, vy: 0 };
  };

  /** Free movement: start walking toward the held keys' velocity from where the hero is now. */
  const rewalk = () => {
    const p = heroPose(w);
    const t = walkTarget(hero, held);
    const accel = hero.walk.speed / (hero.walk.accelMs / 1000);
    walk = { w0: w, x0: p.x, y0: p.y, vx0: p.vx, vy0: p.vy, tx: t.x, ty: t.y, t: Math.hypot(t.x - p.vx, t.y - p.vy) / accel };
    step = undefined;
    if (t.x !== 0 || t.y !== 0) facingAngle = Math.atan2(t.y, t.x);
  };

  /** A section boundary: entering step movement stops any walk; entering free movement walks with the held keys. */
  const switchMode = (steps: boolean) => {
    if (steps) {
      if (!walk) return;
      const p = heroPose(w);
      walk = undefined;
      step = undefined;
      restPose = { x: p.x, y: p.y };
    } else rewalk();
  };

  const startHitStop = (len: number) => {
    if (inStop(s)) return; // No stacking.
    if (len < HITSTOP_MIN) return;
    stopStart = s;
    stopEnd = s + Math.min(len, HITSTOP_MAX);
    hitStops.push([stopStart, stopEnd]);
    q.push(stopEnd, { kind: "hitstopEnd" });
  };

  // ---------------------------------------------------------------- enemies
  const weightOf = (def: EnemyDef) => (def.size === "boss" ? hero.aimWeights.boss : def.size === "elite" ? hero.aimWeights.elite : hero.aimWeights.regular);
  const spawnFoe = (type: string, x: number, y: number, health?: number): Foe => {
    const def = lib.enemies.get(type)!;
    const rec: EnemyRecord = { id: nextId++, type, health: health ?? def.health, spawnW: w, deathW: Infinity, sampleOffset: 0, sampleCount: 0, sampleW0: 0, fates: [] };
    const f: Foe = { rec, def, r: def.radius, health: rec.health, x, y, vx: 0, vy: 0, lastW: w, alive: true, finalSample: false, samples: [], sx: 0, sy: 0, knockX: 0, knockY: 0, knockT: 0, weight: weightOf(def) };
    foes.push(f);
    alive.push(f);
    records.push(rec);
    foeSamples.push(f);
    return f;
  };

  const spawnPoint = (def: EnemyDef, r: Rng, forwardOnly: boolean): { x: number; y: number } => {
    const view = camera.view();
    const cx = (view[0] + view[2]) / 2;
    const cy = (view[1] + view[3]) / 2;
    const ext = spriteExtents(def);
    const m = SPAWN_MARGIN + SPAWN_EXTRA;
    const pp = pacePoint(s);
    const fwd = Math.atan2(pp.ty, pp.tx);
    let x = cx;
    let y = cy;
    for (let tries = 0; tries < 8; tries++) {
      const forward = forwardOnly || r.next() < SPAWN_FORWARD;
      const ang = forward ? fwd + r.range(-Math.PI / 2, Math.PI / 2) : r.range(-Math.PI, Math.PI);
      const out = r.range(0, 40);
      const c = Math.cos(ang);
      const sn = Math.sin(ang);
      // The ray from the view centre leaves the view, grown by the sprite's extents.
      const left = cx - view[0] + ext.hx + m + out;
      const right = view[2] - cx + ext.hx + m + out;
      const top = cy - view[1] + ext.down + m + out;
      const bottom = view[3] - cy + ext.up + m + out;
      const tx = c > 1e-9 ? right / c : c < -1e-9 ? left / -c : Infinity;
      const ty = sn > 1e-9 ? bottom / sn : sn < -1e-9 ? top / -sn : Infinity;
      const t = Math.min(tx, ty);
      x = cx + c * t;
      y = cy + sn * t;
      if (corridor.edge(x, y) > -20) break;
    }
    return { x, y };
  };

  const kill = (f: Foe) => {
    f.alive = false;
    f.finalSample = true;
    f.rec.deathW = w;
  };

  /** An enemy's position at world time atW (at or after its last fixed step). */
  const posX = (f: Foe, atW: number) => f.x + f.vx * (atW - f.lastW);
  const posY = (f: Foe, atW: number) => f.y + f.vy * (atW - f.lastW);

  /** Deal damage to one enemy from a hit; records the fate. */
  const damage = (f: Foe, h: Hit) => {
    if (!f.alive) return;
    f.health -= h.damage;
    const lethal = f.health <= 1e-9;
    const fate: Fate = { w, x: posX(f, w), y: posY(f, w), hit: h.id, lethal, damage: h.damage };
    f.rec.fates.push(fate);
    if (lethal) kill(f);
    else {
      // A white flash and a knockback away from the hero.
      const hp = heroPose(w);
      const dx = fate.x - hp.x;
      const dy = fate.y - hp.y;
      const dl = Math.hypot(dx, dy) || 1;
      f.knockX = (dx / dl) * KNOCKBACK;
      f.knockY = (dy / dl) * KNOCKBACK;
      f.knockT = KNOCK_TIME;
    }
  };

  const intensityOf = (sec: number, energy: number, weight: Hit["weight"], accent: number) =>
    clamp(ctx.sections[sec].level.intensity * (0.35 + 0.65 * energy) * weightFactor(weight) * (accent > 0 ? accent : 1), 0, 1);

  const addHit = (h: Omit<Hit, "id" | "intensity" | "s" | "w" | "section" | "energy">, energy = energyAt(song, s)): Hit => {
    const section = sectionIndexAt(ctx, s);
    const hit: Hit = { ...h, id: hits.length, s, w, section, energy, intensity: intensityOf(section, energy, h.weight, h.accent) };
    hits.push(hit);
    return hit;
  };

  /** Everyone alive inside a shape takes the hit. */
  const applyHit = (h: Hit, filter?: (f: Foe) => boolean) => {
    for (const f of alive) {
      if (!f.alive || (filter && !filter(f))) continue;
      if (catches(h.shape, posX(f, w), posY(f, w), f.r)) damage(f, h);
    }
    alive = alive.filter((f) => f.alive);
  };

  // ---------------------------------------------------------------- swings
  /** Arm a swing for a slot (one swing per slot): the press's, or an automatic one. */
  const swingFor = (slot: GridSlot | undefined, sArmed: number, press?: number): Swing | undefined => {
    if (!slot || takenSlots.has(slot)) return undefined;
    if (blastS !== undefined && slot.s >= blastS - 1e-6) return undefined;
    takenSlots.add(slot);
    const sec = ctx.sections[slot.section];
    // Free movement cleaves on strong anchors.
    const heavy = slot.kind === "anchor";
    const anim = heavy ? hero.swings.heavy.animation : hero.swings.light.animations[lightCount++ % hero.swings.light.animations.length];
    const info = anims[anim] ?? { frames: 6, impactFrame: 3 };
    const sWind = Math.max(sArmed, slot.s - hero.swings.windupMs / 1000 / Math.max(0.05, baseRate(slot.s)));
    const sw: Swing = { sArmed, sWind, sStrike: slot.s, slot, heavy, anim, impact: info.impactFrame ?? 3, frames: info.frames, press, accent: slot.accent, dir: facingAngle, aimed: false, struck: false, wStrike: 0, fps: 16 };
    swings.push(sw);
    liveSwings.push(sw);
    q.push(sWind, { kind: "wind", sw });
    q.push(slot.s, { kind: "strike", sw });
    const amt = (heavy ? 0.035 : 0.014) * sec.level.intensity;
    punches.push({ s: slot.s, amt });
    return sw;
  };

  const aimTargets = (x: number, y: number, reach: number): AimTarget[] => {
    const out: AimTarget[] = [];
    for (const f of alive) {
      if (!f.alive) continue;
      const fx = posX(f, w);
      const fy = posY(f, w);
      if (Math.abs(fx - x) > 2 * reach + f.r || Math.abs(fy - y) > 2 * reach + f.r) continue;
      if (Math.hypot(fx - x, fy - y) > 2 * reach + f.r) continue;
      out.push({ x: fx, y: fy, r: f.r, weight: f.weight });
    }
    return out;
  };

  const doWind = (sw: Swing) => {
    const hp = heroPose(w);
    const reach = sw.heavy ? heavyReach : lightReach;
    const targets = aimTargets(hp.x, hp.y, reach.radius);
    // The snapshot is aim's whole input, so the check can re-run it.
    const snap: AimSnapshot = { x: hp.x, y: hp.y, facing: facingAngle, targets: [] };
    for (const t of targets) snap.targets.push(t.x, t.y, t.r, t.weight);
    sw.dir = aim(hp.x, hp.y, facingAngle, targets, reach);
    sw.aimed = true;
    facingAngle = sw.dir;
    sw.record = { s: sw.sStrike, w: 0, sArmed: sw.sArmed, sWind: sw.sWind, kind: sw.heavy ? "heavy" : "light", anim: sw.anim, impactFrame: sw.impact, dir: sw.dir, x: 0, y: 0, hit: -1, press: sw.press, accent: sw.accent, aim: snap };
    const windW = Math.max(1 / 240, (sw.sStrike - sw.sWind) * baseRate(sw.sStrike));
    sw.fps = sw.impact / windW;
  };

  const doStrike = (sw: Swing) => {
    if (!sw.aimed) doWind(sw);
    const hp = heroPose(w);
    const def = sw.heavy ? hero.swings.heavy : hero.swings.light;
    const reach = sw.heavy ? heavyReach : lightReach;
    const shape: Shape = swingArc(hp.x, hp.y, sw.dir, reach);
    const mult = damageMultiplier(hero, groove);
    const h = addHit({ source: "move", what: sw.heavy ? "cleave" : "slash", weight: sw.heavy ? "heavy" : "light", primary: true, shape, dir: sw.dir, accent: sw.accent, damage: def.damage * mult });
    applyHit(h);
    sw.struck = true;
    sw.wStrike = w;
    const rec = sw.record!;
    rec.w = w;
    rec.x = hp.x;
    rec.y = hp.y;
    rec.hit = h.id;
    swingRecords.push(rec);
    if (sw.heavy) startHitStop((hero.swings.heavy.hitStopMs / 1000) * (0.6 + 0.4 * sw.accent));
  };

  // ---------------------------------------------------------------- input
  const press = (e: KeyEvent) => {
    if (inStop(s)) {
      // A press ends the hit-stop early: world time resumes at the press.
      stopEnd = s;
      hitStops[hitStops.length - 1][1] = s;
    }
    const sec = sectionAt(s);
    const steps = sec.level.movement === "steps";
    const input: PlanInput = { s, w, kind: "press", key: e.key, steps };
    const idx = inputs.length;
    inputs.push(input);
    held.add(e.key);
    // Free movement: held keys walk; presses are not rated and do not arm the sword.
    if (!steps) {
      rewalk();
      return;
    }
    // A perpendicular key just after the first bends the step into a diagonal.
    const bent = step ? bendStep(hero, step, w, e.key, corridor.clamp) : undefined;
    if (bent) {
      step = bent;
      input.bend = true;
      return;
    }
    const hp = heroPose(w);
    step = startStep(hero, w, hp.x, hp.y, e.key, corridor.clamp);
    landed = false;
    facingAngle = Math.atan2(step.dy, step.dx);
    // Rate the press against the timing grid (the sections' grids join end to end), and arm the sword.
    const rated = ratePressOnGrid(hero, s, sch.gridTimes);
    input.rating = rated.rating;
    input.accuracy = rated.accuracy;
    input.delta = rated.delta;
    groove = grooveAfterPress(hero, groove, rated.accuracy);
    lastPressS = s;
    grooveS.push(s);
    grooveV.push(groove);
    input.armed = swingFor(sch.gridSlots[armedStrike(hero, sch.gridTimes, s)], s, idx) !== undefined;
  };

  const release = (e: KeyEvent) => {
    const steps = sectionAt(s).level.movement === "steps";
    inputs.push({ s, w, kind: "release", key: e.key, steps });
    held.delete(e.key);
    if (!steps) rewalk();
  };

  /** Step movement: a bar that ended with no swing in it gets one by itself on the next downbeat. */
  const idleSwing = (slot: GridSlot, prev: number) => {
    if (takenSlots.has(slot)) return;
    if (swings.some((sw) => sw.sStrike >= prev - 1e-9 && sw.sStrike < slot.s - 1e-9)) return;
    swingFor(slot, s);
  };

  // ---------------------------------------------------------------- sampling
  const sampleHero = (wk: number, sk: number) => {
    const p = heroPose(wk);
    if (lastSample) walked += Math.hypot(p.x - lastSample.x, p.y - lastSample.y);
    lastSample = { x: p.x, y: p.y };
    hx.push(p.x);
    hy.push(p.y);
    hz.push(p.z);
    // Animation: a swing (wind-up, impact, follow-through), a step's jump, or idle.
    // A swing winding up takes precedence over one following through.
    let winding: Swing | undefined;
    let following: Swing | undefined;
    for (const c of liveSwings) {
      if (c.sWind > sk + 1e-9) continue;
      if (!c.struck) {
        if (!winding || c.sStrike < winding.sStrike) winding = c;
      } else if (c.impact + (wk - c.wStrike) * c.fps < c.frames && (!following || c.sStrike > following.sStrike)) following = c;
    }
    // The sample nearest a strike always shows its impact frame, even when the next swing is winding up.
    const sw = following && wk - following.wStrike <= 0.5 / HERO_RATE + 1e-9 ? following : (winding ?? following);
    const half = 0.5 / HERO_RATE;
    if (sw) {
      let fr: number;
      if (!sw.struck) {
        const toImpactW = (sw.sStrike - sk) * Math.max(1e-6, baseRate(sk));
        if (toImpactW <= half + 1e-9) fr = sw.impact;
        else fr = clamp(Math.floor((sw.impact * (sk - sw.sWind)) / Math.max(1e-6, sw.sStrike - sw.sWind)), 0, sw.impact - 1);
      } else {
        const since = wk - sw.wStrike;
        fr = since <= half + 1e-9 ? sw.impact : clamp(sw.impact + Math.floor(since * sw.fps + 1e-6), sw.impact, sw.frames - 1);
      }
      hAnim.push(animIdx(sw.anim));
      hFrame.push(fr);
      hFacing.push(facingIndex(sw.aimed ? sw.dir : facingAngle));
    } else if (p.u < 1) {
      hAnim.push(animIdx("jump"));
      hFrame.push(clamp(Math.floor(p.u * 5), 0, 4));
      hFacing.push(facingIndex(facingAngle));
    } else if (Math.hypot(p.vx, p.vy) > 1) {
      // Walking: the cycle advances by distance, facing the way the hero walks.
      hAnim.push(animIdx("walk"));
      hFrame.push(walkFrame(hero, walked, anims.walk?.frames ?? 8));
      hFacing.push(facingIndex(Math.atan2(p.vy, p.vx)));
    } else {
      hAnim.push(animIdx("idle"));
      hFrame.push(Math.floor(frac(sbiAt(sk) / 2) * 2) % 2);
      hFacing.push(facingIndex(facingAngle));
    }
    // The camera follows the hero with the section's lead along the route.
    const sec = sectionAt(sk);
    const f = pacePoint(sk);
    const lead = sec.level.camera.lead;
    let zoom = sec.level.camera.zoom;
    if (sec.index > 0) {
      const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
      const u = smoothstep((sbiAt(sk) - sec.b0) / bpb);
      zoom = lerp(ctx.sections[sec.index - 1].level.camera.zoom, zoom, u);
    }
    for (let i = punches.length - 1; i >= 0; i--) {
      const age = sk - punches[i].s;
      if (age > 1.2 || age < -0.05) continue;
      const env = age < 0 ? smoothstep((age + 0.05) / 0.05) : Math.exp(-age / 0.22);
      zoom *= 1 + punches[i].amt * env;
    }
    camera.sample(p.x + f.tx * lead, p.y + f.ty * lead - 12, zoom);
    // Retire swings whose follow-through has ended.
    for (let i = liveSwings.length - 1; i >= 0; i--) {
      const c = liveSwings[i];
      if (c.struck && c.impact + (wk - c.wStrike) * c.fps >= c.frames) liveSwings.splice(i, 1);
    }
  };

  const recordFoes = (wk: number) => {
    for (let i = foeSamples.length - 1; i >= 0; i--) {
      const f = foeSamples[i];
      if (f.samples.length === 0) f.rec.sampleW0 = wk;
      if (f.finalSample) {
        const last = f.rec.fates[f.rec.fates.length - 1];
        if (last && last.lethal) f.samples.push(last.x, last.y);
        else f.samples.push(f.x, f.y);
        foeSamples.splice(i, 1);
        continue;
      }
      f.samples.push(posX(f, wk), posY(f, wk));
    }
  };

  // ---------------------------------------------------------------- motion
  const cell = 32;
  const G = 192;
  const head = new Int32Array(G * G);
  const stamp = new Int32Array(G * G).fill(-1);
  let nextArr = new Int32Array(1024);
  let gen = 0;

  const groupPhase = (group: string, sbi: number, barStart: number, bpb: number): number => {
    const period = group === "eighth" ? 0.5 : group === "beat" ? 1 : group === "bar" ? bpb : 2 * bpb;
    const ph = ((sbi - barStart) / period) % 1;
    return ph < 0 ? ph + 1 : ph;
  };

  /** Integrate every enemy to world time w1 (a fixed step). */
  const moveFoes = (w1: number) => {
    const hp = heroPose(w1);
    const sec = sectionAt(s);
    const free = sec.song.feel === "free";
    const sbi = sbiAt(s);
    const bar = Math.max(1, barOfBeatIndex(ctx.idx, sbi));
    const bpb = ctx.idx.beatsPerBar[Math.min(ctx.idx.barCount, bar)] ?? 2;
    const barStart = ctx.idx.barStart[Math.min(ctx.idx.barCount, bar)] ?? 0;
    const mults: Record<string, number> = {};
    for (const g of ["eighth", "beat", "bar", "two-bar"]) {
      let ph = groupPhase(g, sbi, barStart, bpb);
      if (free) {
        const period = Math.max(2, g === "eighth" ? 0.5 : g === "beat" ? 1 : g === "bar" ? bpb : 2 * bpb);
        ph = (((sbi - barStart) / period) % 1 + 1) % 1;
        mults[g] = 0.6 + 0.8 * beatSwell(ph);
      } else mults[g] = (0.6 + 0.8 * beatHit(ph)) / 0.733;
    }
    for (const f of alive) {
      // Bring everyone to this step's time first.
      const dt = w1 - f.lastW;
      f.x += f.vx * dt;
      f.y += f.vy * dt;
    }
    {
      gen++;
      const gox = Math.floor(hp.x / cell) - G / 2;
      const goy = Math.floor(hp.y / cell) - G / 2;
      if (nextArr.length < alive.length) nextArr = new Int32Array(alive.length * 2);
      for (let i = 0; i < alive.length; i++) {
        const f = alive[i];
        f.sx = 0;
        f.sy = 0;
        const ix = Math.floor(f.x / cell) - gox;
        const iy = Math.floor(f.y / cell) - goy;
        if (ix < 0 || iy < 0 || ix >= G || iy >= G) {
          nextArr[i] = -2;
          continue;
        }
        const c = iy * G + ix;
        if (stamp[c] !== gen) {
          stamp[c] = gen;
          head[c] = -1;
        }
        nextArr[i] = head[c];
        head[c] = i;
      }
      // Separation forces and positional correction, pairwise.
      for (let i = 0; i < alive.length; i++) {
        const a = alive[i];
        const ix = Math.floor(a.x / cell) - gox;
        const iy = Math.floor(a.y / cell) - goy;
        if (ix < 0 || iy < 0 || ix >= G || iy >= G) continue;
        for (let gx = ix - 1; gx <= ix + 1; gx++) {
          if (gx < 0 || gx >= G) continue;
          for (let gy = iy - 1; gy <= iy + 1; gy++) {
            if (gy < 0 || gy >= G) continue;
            const c = gy * G + gx;
            if (stamp[c] !== gen) continue;
            for (let j = head[c]; j >= 0; j = nextArr[j]) {
              if (j <= i) continue;
              const b = alive[j];
              const ox = a.x - b.x;
              const oy = a.y - b.y;
              const lim = SEPARATION * (a.r + b.r);
              const d2 = ox * ox + oy * oy;
              if (d2 >= lim * lim) continue;
              const d = Math.sqrt(d2) || 0.01;
              const ux = d > 0.01 ? ox / d : 1;
              const uy = d > 0.01 ? oy / d : 0;
              const k = (1 - d / lim) * 90;
              a.sx += ux * k;
              a.sy += uy * k;
              b.sx -= ux * k;
              b.sy -= uy * k;
              const gap = MIN_GAP * (a.r + b.r);
              if (d < gap) {
                const push = (gap - d) / 2;
                a.x += ux * push;
                a.y += uy * push;
                b.x -= ux * push;
                b.y -= uy * push;
              }
            }
          }
        }
      }
    }
    for (const f of alive) {
      const dt = w1 - f.lastW;
      f.lastW = w1;
      const k = Math.min(1, dt * 12);
      const dx = hp.x - f.x;
      const dy = hp.y - f.y;
      const d = Math.hypot(dx, dy) || 0.01;
      const contact = HURTBOX_RADIUS + f.r + CONTACT_GAP;
      const m = mults[f.def.group] ?? 1;
      const speed = f.def.speed * m * clamp((d - contact) / 16, 0, 1);
      const tvx = (dx / d) * speed + f.sx;
      const tvy = (dy / d) * speed + f.sy;
      // The new velocity carries the enemy to the next step (positions are extrapolated with it).
      f.vx += (tvx - f.vx) * k;
      f.vy += (tvy - f.vy) * k;
      if (f.knockT > 0) {
        const used = Math.min(dt, f.knockT);
        f.x += (f.knockX * used) / KNOCK_TIME;
        f.y += (f.knockY * used) / KNOCK_TIME;
        f.knockT -= used;
      }
      // The hero passes through the crowd: enemies are pushed aside.
      const minD = HURTBOX_RADIUS + f.r;
      const ex = f.x - hp.x;
      const ey = f.y - hp.y;
      const ed = Math.hypot(ex, ey);
      if (ed < minD) {
        const ux = ed > 0.01 ? ex / ed : 1;
        const uy = ed > 0.01 ? ey / ed : 0;
        f.x = hp.x + ux * minD;
        f.y = hp.y + uy * minD;
      }
    }
  };

  /**
   * Advance to song time t. World time comes from the warp breaks, and the
   * hero and enemy samples are taken at their exact times from the state so
   * far, so how events split the time between fixed steps changes nothing.
   */
  const advanceTo = (t: number, fixed: boolean) => {
    const rate = rateAt(s);
    pushBreak(rate);
    const b = breaks[breaks.length - 1];
    const w1 = b.w + (t - b.s) * b.rate;
    if (w1 > w) {
      // Step landings (the dust puff).
      if (step && !landed && step.w0 + stepSeconds(hero) <= w1 + 1e-12) {
        const wl = step.w0 + stepSeconds(hero);
        const end = stepEnd(step);
        landings.push({ s: b.s + (wl - b.w) / rate, w: wl, x: end.x, y: end.y });
        landed = true;
      }
      while (nextHeroK / HERO_RATE <= w1 + 1e-12) {
        const wk = nextHeroK / HERO_RATE;
        sampleHero(wk, b.s + (wk - b.w) / rate);
        nextHeroK++;
      }
      while (nextFoeK / ENEMY_RATE <= w1 + 1e-12) {
        recordFoes(nextFoeK / ENEMY_RATE);
        nextFoeK++;
      }
    }
    s = t;
    w = w1;
    if (fixed && w > lastFixedW) {
      moveFoes(w);
      lastFixedW = w;
    }
  };

  // ---------------------------------------------------------------- events
  const observe = (): Observation => {
    const hp = heroPose(w);
    const pp = pacePoint(s);
    return {
      s,
      hero: { x: hp.x, y: hp.y, stepping: hp.u < 1 },
      enemies: alive.filter((f) => f.alive).map((f) => ({ x: posX(f, w), y: posY(f, w), type: f.rec.type, health: f.health })),
      threats: [],
      pacePoint: { x: pp.x, y: pp.y, tx: pp.tx, ty: pp.ty },
      edgeDistance: (x, y) => corridor.edge(x, y),
    };
  };

  const scheduleDecision = (after: number) => {
    const t = player.nextDecision(after);
    if (t <= cutS && t < endS) q.push(Math.max(t, after), { kind: "decide" });
  };

  const spawnDirector = (bi: number) => {
    if (blasting) return;
    const target = targetPopulation(ctx, s);
    const living = alive.length + pending;
    const want = Math.min(SPAWN_PER_BEAT, Math.round(target) - living, POP_CAP - living);
    if (want <= 0) return;
    const sec = sectionAt(s);
    const roster = Object.entries(sec.level.roster).filter(([t]) => lib.enemies.get(t)!.behavior === "chase");
    const r = new Rng(ctx.seed, "spawn", bi);
    for (let k = 0; k < want; k++) {
      const type = r.weighted(roster.map(([t]) => t), roster.map(([, wgt]) => wgt));
      const def = lib.enemies.get(type)!;
      // Each enemy enters on its movement group's subdivision.
      let sAppear = s;
      if (def.group === "bar" || def.group === "two-bar") {
        const bar = barOfBeatIndex(ctx.idx, bi + 1e-6);
        let nb = bar + (ctx.idx.barStart[bar] < bi - 1e-6 ? 1 : 0);
        if (def.group === "two-bar" && (nb - sec.song.bars[0]) % 2 === 1) nb++;
        const start = ctx.idx.barStart[Math.min(ctx.idx.barCount, nb)];
        sAppear = start !== undefined && start < song.beats.length ? timeOfBeatIndex(song, start) : s;
      }
      pending++;
      q.push(Math.max(s, sAppear), { kind: "appear", type });
    }
  };

  const appear = (type: string) => {
    pending--;
    if (blasting) return;
    const def = lib.enemies.get(type)!;
    const r = new Rng(ctx.seed, "appear", nextId);
    const p = spawnPoint(def, r, false);
    spawnFoe(type, p.x, p.y);
  };

  const recycle = () => {
    const view = camera.view();
    const vw = view[2] - view[0];
    const hp = heroPose(w);
    const pp = pacePoint(s);
    const behind = alive.filter((f) => {
      const dx = posX(f, w) - hp.x;
      const dy = posY(f, w) - hp.y;
      return f.alive && Math.hypot(dx, dy) >= RECYCLE_VIEWS * vw && dx * pp.tx + dy * pp.ty < 0;
    });
    for (const f of behind) {
      // Not a kill: it leaves the field and re-enters ahead with its health.
      f.alive = false;
      f.finalSample = true;
      f.rec.deathW = w;
      f.rec.recycled = true;
      const r = new Rng(ctx.seed, "recycle", f.rec.id);
      const p2 = spawnPoint(f.def, r, true);
      spawnFoe(f.rec.type, p2.x, p2.y, f.health);
    }
    alive = alive.filter((f) => f.alive);
  };

  const grantWeapon = (id: string) => {
    weaponsGranted.push({ id, s });
    const wd = lib.weapons.get(id)!;
    if (wd.kind === "blast") return;
    const end = blastS ?? ctx.duration;
    const startBar = Math.max(1, barOfBeatIndex(ctx.idx, sbiAt(s)));
    for (let bar = startBar; bar <= ctx.idx.barCount; bar++) {
      const bpb = ctx.idx.beatsPerBar[bar];
      const stepB = subdivisionBeats(wd.fires.every, bpb);
      for (let off = 0; off < bpb - 1e-9; off += stepB) {
        const bi = ctx.idx.barStart[bar] + off;
        if (bi > song.beats.length - 1) continue;
        const t = timeOfBeatIndex(song, bi);
        // The weapon starts on the slot after its level-up.
        if (t <= s + 0.01 || t >= end - 0.01 || t > endS) continue;
        q.push(t, { kind: "fire", weapon: wd, bi });
      }
    }
  };

  const fire = (wd: WeaponDef, bi: number) => {
    // Weapons rest during a hit-stop: the next slot of their subdivision fires instead.
    if (inStop(s)) return;
    weaponFires.push({ weapon: wd.id, s });
    const hp = heroPose(w);
    const energy = energyAt(song, s);
    const mult = damageMultiplier(hero, groove);
    if (wd.kind === "orbit" && wd.orbit) {
      const bar = Math.max(1, barOfBeatIndex(ctx.idx, bi));
      const bpb = ctx.idx.beatsPerBar[bar] ?? 2;
      const th0 = (TAU * bi) / (wd.orbit.periodBars * bpb);
      for (let k = 0; k < wd.orbit.count; k++) {
        const th = th0 + (k * TAU) / wd.orbit.count;
        const x = hp.x + Math.cos(th) * wd.orbit.radius;
        const y = hp.y + Math.sin(th) * wd.orbit.radius * 0.8;
        const h = addHit({ source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x, y, radius: wd.hitShape!.radius }, dir: th + Math.PI / 2, accent: 0, damage: wd.damage * mult }, energy);
        applyHit(h);
      }
    } else if (wd.kind === "chain" && wd.chain) {
      const jumps = Math.round(lerp(wd.chain.jumps[0], wd.chain.jumps[1], energy));
      const chain: ChainState = { points: [{ w, x: hp.x, y: hp.y - 24 }], hit: new Set(), jumps, energy, bi };
      chains.push({ points: chain.points });
      chainJump(wd, chain, 0);
    } else if (wd.kind === "trail" && wd.trail) {
      // Circles along the hero's path over the last beats.
      const bd = (song.beats[Math.min(song.beats.length - 1, Math.floor(bi))] - song.beats[Math.max(0, Math.floor(bi) - 1)]) * baseRate(s);
      const wFrom = w - wd.trail.beats * Math.max(0.05, bd);
      let lastX = Infinity;
      let lastY = Infinity;
      for (let k = hx.length - 1; k >= 0 && k / HERO_RATE >= wFrom; k--) {
        if (k / HERO_RATE > w - 0.12) continue;
        if (Math.hypot(hx[k] - lastX, hy[k] - lastY) < wd.trail.spacing) continue;
        lastX = hx[k];
        lastY = hy[k];
        const h = addHit({ source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x: hx[k], y: hy[k], radius: wd.hitShape!.radius }, dir: 0, accent: 0, damage: wd.damage * mult }, energy);
        applyHit(h);
      }
    }
  };

  const chainJump = (wd: WeaponDef, chain: ChainState, j: number) => {
    if (inStop(s)) {
      // A jump inside a hit-stop waits for the next sixteenth after it.
      q.push(timeOfBeatIndex(song, Math.floor(sbiAt(s) * 4 + 1e-6) / 4 + 0.25), { kind: "chain", weapon: wd, chain, jump: j });
      return;
    }
    const from = chain.points[chain.points.length - 1];
    const fx = from.x;
    const fy = j === 0 ? from.y + 24 : from.y + 16;
    let best: Foe | undefined;
    let bestD = wd.chain!.range;
    for (const f of alive) {
      if (!f.alive || chain.hit.has(f)) continue;
      const d = Math.hypot(posX(f, w) - fx, posY(f, w) - fy);
      if (d < bestD) {
        bestD = d;
        best = f;
      }
    }
    if (!best) return;
    chain.hit.add(best);
    const bx = posX(best, w);
    const by = posY(best, w);
    chain.points.push({ w, x: bx, y: by - 16 });
    const h = addHit({ source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x: bx, y: by, radius: wd.hitShape!.radius }, dir: Math.atan2(by - fy, bx - fx), accent: 0, damage: wd.damage * damageMultiplier(hero, groove) }, chain.energy);
    applyHit(h);
    if (j + 1 < chain.jumps) {
      const stepB = subdivisionBeats(wd.chain!.step, 2);
      q.push(timeOfBeatIndex(song, chain.bi + (j + 1) * stepB), { kind: "chain", weapon: wd, chain, jump: j + 1 });
    }
  };

  const blastBand = (band: number) => {
    const wd = lib.weapons.get(sch.blast!.weapon)!;
    const b = wd.blast!;
    const bands = Math.max(1, Math.round(b.beats / subdivisionBeats(b.step, 2)));
    const hp = heroPose(w);
    const last = band === bands - 1;
    const inner = (b.radius * band) / bands;
    const outer = last ? 1e6 : (b.radius * (band + 1)) / bands;
    const h = addHit({ source: "ring", what: wd.id, weight: "heavy", primary: false, shape: { kind: "ring", x: hp.x, y: hp.y, inner, outer }, dir: 0, accent: 0, damage: wd.damage }, 1);
    applyHit(h);
    if (last) {
      // Nothing survives the final band, wherever it stands.
      for (const f of alive) if (f.alive) damage(f, h);
      alive = alive.filter((f) => f.alive);
    }
  };

  // Song-owned events: mode switches, automatic swings in free movement, idle-swing checks in step movement.
  const windupBefore = (t: number) => t - hero.swings.windupMs / 1000 / Math.max(0.05, baseRate(t));
  for (let i = 1; i < ctx.sections.length; i++) {
    const a = ctx.sections[i - 1].level.movement;
    const b = ctx.sections[i].level.movement;
    if (a !== b) q.push(ctx.sections[i].s0, { kind: "mode", steps: b === "steps" });
  }
  if (ctx.sections[0].level.movement === "free") walk = { w0: 0, x0: restPose.x, y0: restPose.y, vx0: 0, vy0: 0, tx: 0, ty: 0, t: 0 };
  for (const sl of sch.slots) if (sl.kind !== "grid") q.push(Math.max(0, windupBefore(sl.s)), { kind: "auto", slot: sl });
  let prevDown: GridSlot | undefined;
  for (const sl of sch.gridSlots) {
    if (sl.slot !== 0) continue;
    // The bar before must be a step bar too.
    if (prevDown && sl.bar === prevDown.bar + 1) q.push(Math.max(prevDown.s, windupBefore(sl.s)), { kind: "idle", slot: sl, prev: prevDown.s });
    prevDown = sl;
  }
  for (let bi = 0; bi < song.beats.length; bi++) if (song.beats[bi] < endS) q.push(song.beats[bi], { kind: "beat", bi });
  for (const lu of sch.levelUps) if (lu.s < endS) q.push(lu.s, { kind: "levelUp", id: lu.id });
  if (sch.blast && sch.blast.s < endS) {
    const wd = lib.weapons.get(sch.blast.weapon)!;
    const b = wd.blast!;
    const stepB = subdivisionBeats(b.step, 2);
    const bands = Math.max(1, Math.round(b.beats / stepB));
    const bi0 = sbiAt(sch.blast.s);
    for (let k = 0; k < bands; k++) q.push(timeOfBeatIndex(song, bi0 + k * stepB), { kind: "blast", band: k });
  }
  scheduleDecision(0);

  // The first camera and hero samples.
  pushBreak(rateAt(0));
  sampleHero(0, 0);
  nextHeroK = 1;
  recordFoes(0);
  nextFoeK = 1;

  let fixedK = 1;
  let lastFixedW = 0;
  let recycleK = 0;
  while (s < endS) {
    const tFixed = fixedK / SIM_RATE;
    const tRate = Math.min(nextStepAfter(sch.baseSteps, s), inStop(s) ? stopEnd : Infinity);
    const t = Math.min(q.peek(), tFixed, tRate, endS);
    advanceTo(t, t === tFixed);
    if (t === tFixed) {
      fixedK++;
      if (++recycleK % 30 === 0) recycle();
    }
    while (q.peek() <= s + 1e-12) {
      const { ev } = q.pop();
      switch (ev.kind) {
        case "hitstopEnd":
          break;
        case "decide": {
          for (const ke of player.decide(observe())) if (ke.s <= cutS + 1e-12 && ke.s < endS) q.push(Math.max(s, ke.s), { kind: "key", e: ke });
          scheduleDecision(s + 1e-6);
          break;
        }
        case "key":
          if (ev.e.kind === "press") press(ev.e);
          else release(ev.e);
          break;
        case "mode":
          switchMode(ev.steps);
          break;
        case "auto":
          swingFor(ev.slot, s);
          break;
        case "idle":
          idleSwing(ev.slot, ev.prev);
          break;
        case "wind":
          if (!ev.sw.aimed) doWind(ev.sw);
          break;
        case "strike":
          doStrike(ev.sw);
          break;
        case "fire":
          fire(ev.weapon, ev.bi);
          break;
        case "chain":
          chainJump(ev.weapon, ev.chain, ev.jump);
          break;
        case "blast":
          if (ev.band === 0) blasting = true;
          blastBand(ev.band);
          break;
        case "beat": {
          spawnDirector(ev.bi);
          // Idle decay: with no presses, the groove halves every few bars.
          const sec = sectionAt(s);
          const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
          const beatS = song.beats[Math.min(song.beats.length - 1, ev.bi + 1)] - song.beats[Math.max(0, Math.min(song.beats.length - 2, ev.bi))];
          if (s - lastPressS > GROOVE_GRACE_BARS * bpb * beatS && groove > 0) {
            groove = grooveDecay(hero, groove, 1 / bpb);
            grooveS.push(s);
            grooveV.push(groove);
          }
          break;
        }
        case "appear":
          appear(ev.type);
          break;
        case "levelUp":
          grantWeapon(ev.id);
          break;
      }
    }
  }
  // Close the tracks: a final hero sample at the end, and every enemy's last sample.
  const wEnd = w;
  while (nextFoeK / ENEMY_RATE <= wEnd + 1 / ENEMY_RATE) {
    const wk = nextFoeK / ENEMY_RATE;
    recordFoes(wk);
    nextFoeK++;
  }

  // Pack enemy samples.
  let total = 0;
  for (const f of foes) total += f.samples.length;
  const enemySamples = new Float32Array(total);
  let off = 0;
  for (const f of foes) {
    enemySamples.set(f.samples, off);
    f.rec.sampleOffset = off / 2;
    f.rec.sampleCount = f.samples.length / 2;
    off += f.samples.length;
  }

  const heroTrack: HeroTrack = {
    rate: HERO_RATE,
    w0: 0,
    x: Float32Array.from(hx),
    y: Float32Array.from(hy),
    z: Float32Array.from(hz),
    facing: Uint8Array.from(hFacing),
    anim: Uint8Array.from(hAnim),
    frame: Uint8Array.from(hFrame),
    animNames: HERO_ANIMS,
    trail: new Float32Array(hx.length),
  };
  return {
    warp: breaks,
    hero: heroTrack,
    camera,
    hits,
    enemies: records,
    enemySamples,
    swings: swingRecords,
    inputs,
    groove: { s: grooveS, v: grooveV },
    landings,
    weaponsGranted,
    weaponFires,
    chains,
    hitStops,
    blastS,
    endS: s,
  };
}
