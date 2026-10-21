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
import { armedStrike, damageMultiplier, grooveAfterPress, grooveDecay, ratePressOnGrid, type Rating } from "./timing.ts";

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
export const SPAWN_PER_BEAT = 120;
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
/** A near miss: a threat passing this close to the hurtbox's edge without touching it. */
export const NEAR_MISS = 24;
/** The near-miss slow-motion dip: down to 0.4 over 40 ms, held 70 ms, back up over 40 ms. */
const DIP_RATE = 0.4;
const DIP_RAMP = 0.04;
const DIP_HOLD = 0.07;
/** Archers keep this distance from the hero, and shoot only when this close. */
const ARCHER_RANGE = 220;
const ARCHER_SHOOT_RANGE = 420;
/** Archers of a group stand this far apart, so their arrows fly nearly parallel. */
const ARCHER_GAP = 20;
/** The arrow keeps flying this far past its aim point. */
const ARROW_OVERSHOOT = 260;
/** Boss slam markers: a step out of the middle leaves the marker and earns a near miss. */
export const SLAM_RADIUS = 28;
/** The boss lands this far ahead of the hero and knocks nearby enemies back. */
const BOSS_LAND_AHEAD = 110;
const BOSS_LAND_PUSH = 120;
/** The boss-kill hit-stop. */
const BOSS_KILL_STOP = 0.15;
/** The player reacts to a threat only after it has been visible this long (song seconds). */
const REACTION = 0.15;
/** The kick line stands this far ahead of the pace point; its charge is this much faster than a walk. */
const LINE_LEAD = 170;
const CHARGE_BEATS = 2;
const CHARGE_SPEED = 4;

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
  arrows: ArrowRecord[];
  slams: SlamRecord[];
  /** Every threat (arrow or slam) and how it ended. */
  threats: { s: number; w: number; kind: "arrow" | "slam"; outcome: "near-miss" | "touch" | "miss"; x: number; y: number }[];
  boss?: { id: number; landS: number; x: number; y: number; deathS?: number; killHit?: number; health: { s: number; frac: number }[] };
  /** Gems: drop (world time, place) and arrival at the hero (song and world time). */
  gems: { w0: number; x0: number; y0: number; s1: number; w1: number; value: number; big: boolean; enemy: number }[];
  /** The XP bar after each change (fraction of the next level-up's requirement). */
  xp: { s: number; fill: number }[];
  /** Cleave shockwaves: one hit per band. */
  waves: { hits: number[] }[];
  /** Formation releases (song time) and their members. */
  formations: { def: string; sStart: number; sRelease: number; members: number[] }[];
}

export interface ArrowRecord {
  sRelease: number;
  wRelease: number;
  sArrive: number;
  wArrive: number;
  x0: number;
  y0: number;
  /** Where it was aimed: the hero's hurtbox at the release. */
  ax: number;
  ay: number;
  /** End of the drawn flight. */
  x1: number;
  y1: number;
  w1: number;
  outcome: "near-miss" | "touch" | "miss";
}

export interface SlamRecord {
  sMark: number;
  wMark: number;
  sLand: number;
  wLand: number;
  x: number;
  y: number;
  radius: number;
  outcome: "near-miss" | "touch" | "miss";
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
  /**
   * Formation members follow their slot until the release: an offset (by song time) from the
   * formation's anchor, which rides with the camera-smoothed hero.
   */
  slot?: (t: number) => { x: number; y: number };
  slotUntil: number;
  /** A charge (the kick line's release) runs faster until this song time. */
  chargeUntil: number;
  /** Archer group, and this archer's place in it (shoulder to shoulder at a shared post). */
  archerGroup?: number;
  archerSlot?: number;
  drawing?: { w0: number };
  /** The boss: its health cannot drop below 1 before this song time; it lands at landS. */
  floorUntil?: number;
  landS?: number;
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
  | { kind: "spawnTick"; bi: number }
  | { kind: "appear"; type: string }
  | { kind: "mode"; steps: boolean }
  | { kind: "auto"; slot: GridSlot }
  | { kind: "idle"; slot: GridSlot; prev: number }
  | { kind: "wave"; wave: Wave }
  | { kind: "archers"; group: number; n: number }
  | { kind: "draw"; v: number }
  | { kind: "release"; v: number }
  | { kind: "arrive"; v: number }
  | { kind: "bossSpawn" }
  | { kind: "bossLand" }
  | { kind: "slamMark"; k: number }
  | { kind: "slamLand"; k: number }
  | { kind: "formation"; f: number }
  | { kind: "formationRelease"; f: number }
  | { kind: "gem"; gem: number }
  | { kind: "levelDue"; i: number }
  | { kind: "levelUp"; id: string };

interface Wave {
  x: number;
  y: number;
  dir: number;
  band: number;
  hit: Set<Foe>;
  record: { hits: number[] };
}

interface ChainState {
  points: { w: number; x: number; y: number }[];
  hit: Set<Foe>;
  jumps: number;
  energy: number;
  bi: number;
}

/** Event order at equal times. */
const ORDER: Record<Ev["kind"], number> = {
  spawnTick: 21,
  hitstopEnd: 0,
  mode: 1,
  levelDue: 2,
  levelUp: 3,
  formation: 4,
  bossSpawn: 4,
  archers: 4,
  decide: 5,
  key: 6,
  draw: 7,
  release: 8,
  slamMark: 8,
  idle: 9,
  auto: 10,
  wind: 11,
  strike: 12,
  bossLand: 13,
  formationRelease: 14,
  wave: 15,
  fire: 16,
  chain: 17,
  blast: 18,
  arrive: 19,
  slamLand: 19,
  gem: 20,
  beat: 21,
  appear: 22,
};

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
  // A cleave aims at everything its arc and its shockwave will reach.
  const heavyAim = { radius: hero.swings.heavy.wave.to, halfAngle: heavyReach.halfAngle };
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
  /** Camera punches: song time, strength, and world time once it has happened (it freezes in a hit-stop). */
  const punches: { s: number; amt: number; w?: number }[] = [];
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
  const waves: { hits: number[] }[] = [];
  const arrows: ArrowRecord[] = [];
  const flying: { v: number; rec: ArrowRecord; ux: number; uy: number }[] = [];
  const slamRecords: SlamRecord[] = [];
  const slamMarks = new Map<number, { x: number; y: number; sMark: number; wMark: number }>();
  const threats: SimResult["threats"] = [];
  let bossFoe: Foe | undefined;
  let bossInfo: SimResult["boss"];
  const gems: SimResult["gems"] = [];
  const xpSeries: SimResult["xp"] = [{ s: 0, fill: 0 }];
  let xpNow = 0;
  let level = 0;
  let overdue = false;
  const formationRecords: SimResult["formations"] = [];

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
  // The near-miss dip, as constant steps from its start (song time).
  let dipStart = -Infinity;
  const DIP_STEPS = [0, 0.25, 0.5, 0.75, 1].map((k) => k * DIP_RAMP);
  const dipCuts = () => [...DIP_STEPS, ...DIP_STEPS.map((d) => DIP_RAMP + DIP_HOLD + d)];
  const dipFactor = (t: number) => {
    const d = t - dipStart;
    if (d < 0 || d >= 2 * DIP_RAMP + DIP_HOLD - 1e-12) return 1;
    if (d < DIP_RAMP) return 1 - (1 - DIP_RATE) * ((Math.floor((d / DIP_RAMP) * 4 + 1e-9) + 1) / 4);
    if (d < DIP_RAMP + DIP_HOLD) return DIP_RATE;
    return DIP_RATE + (1 - DIP_RATE) * ((Math.floor(((d - DIP_RAMP - DIP_HOLD) / DIP_RAMP) * 4 + 1e-9) + 1) / 4);
  };
  const nextDipCut = (t: number) => {
    for (const c of dipCuts()) if (dipStart + c > t + 1e-12) return dipStart + c;
    return Infinity;
  };
  const rateAt = (t: number) => (inStop(t) ? 0 : Math.round(baseRate(t) * dipFactor(t) * 1e6) / 1e6);
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
  const weightOf = (def: EnemyDef, formation = false) =>
    def.size === "boss" ? hero.aimWeights.boss : formation ? hero.aimWeights.formation : def.size === "elite" ? hero.aimWeights.elite : hero.aimWeights.regular;
  const spawnFoe = (type: string, x: number, y: number, health?: number): Foe => {
    const def = lib.enemies.get(type)!;
    const rec: EnemyRecord = { id: nextId++, type, health: health ?? def.health, spawnW: w, deathW: Infinity, sampleOffset: 0, sampleCount: 0, sampleW0: 0, fates: [] };
    const f: Foe = { rec, def, r: def.radius, health: rec.health, x, y, vx: 0, vy: 0, lastW: w, alive: true, finalSample: false, samples: [], sx: 0, sy: 0, knockX: 0, knockY: 0, knockT: 0, weight: weightOf(def), slotUntil: -Infinity, chargeUntil: -Infinity };
    // Its first sample is the spawn spot itself, at the last sample time before it appeared.
    f.samples.push(x, y);
    rec.sampleW0 = Math.max(0, nextFoeK - 1) / ENEMY_RATE;
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

  /** Can this enemy be hit now? (The boss is out of reach while it drops in.) */
  const hittable = (f: Foe) => f.alive && (f.landS === undefined || s >= f.landS - 1e-9);

  /** Deal damage to one enemy from a hit; records the fate. */
  const damage = (f: Foe, h: Hit) => {
    if (!hittable(f)) return;
    // The boss cannot drop below 1 health before its death chord; the first hit at or after the
    // chord kills it (if it is worn down that far).
    if (f.floorUntil !== undefined && s >= f.floorUntil - 1e-6 && f.health <= 1 + 1e-9) f.health = 0;
    else f.health -= h.damage;
    if (f.floorUntil !== undefined && s < f.floorUntil - 1e-6) f.health = Math.max(f.health, 1);
    const lethal = f.health <= 1e-9;
    const fate: Fate = { w, x: posX(f, w), y: posY(f, w), hit: h.id, lethal, damage: h.damage };
    f.rec.fates.push(fate);
    if (f === bossFoe) bossInfo!.health.push({ s, frac: Math.max(0, f.health) / f.rec.health });
    if (lethal) {
      kill(f);
      onDeath(f, fate, h);
    } else {
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

  /** Everyone alive inside a shape takes the hit. Returns who was hit. */
  const applyHit = (h: Hit, filter?: (f: Foe) => boolean): Foe[] => {
    const out: Foe[] = [];
    for (const f of alive) {
      if (!hittable(f) || (filter && !filter(f))) continue;
      if (catches(h.shape, posX(f, w), posY(f, w), f.r)) {
        damage(f, h);
        out.push(f);
      }
    }
    alive = alive.filter((f) => f.alive);
    return out;
  };

  // ---------------------------------------------------------------- swings
  /** Arm a swing for a slot (one swing per slot): the press's, or an automatic one. */
  const swingFor = (slot: GridSlot | undefined, sArmed: number, press?: number, rating?: Rating): Swing | undefined => {
    if (!slot || takenSlots.has(slot)) return undefined;
    // The finale blast ends the sword's routine (its chord is matched to the grid within 40 ms).
    if (blastS !== undefined && slot.s >= blastS - 0.04) return undefined;
    takenSlots.add(slot);
    const sec = ctx.sections[slot.section];
    // A swing striking on an accent, armed by a press rated Great or better, is a heavy cleave;
    // free movement cleaves on strong anchors.
    const rule = sec.level.heavyOnAccents;
    const onAccent = slot.accent > 0 && slot.accent >= rule.minStrength && (rule.label === undefined || rule.label === slot.accentLabel);
    const heavy = slot.kind === "anchor" || (slot.kind === "grid" && onAccent && (rating === "perfect" || rating === "great"));
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
      if (!hittable(f)) continue;
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
    const reach = sw.heavy ? heavyAim : lightReach;
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
    if (sw.heavy) {
      startHitStop((hero.swings.heavy.hitStopMs / 1000) * (0.6 + 0.4 * sw.accent));
      // The cleave's crescent shockwave travels outward along its aim, one band per sixteenth note.
      // Band k reaches 25 px further on the k-th sixteenth after the strike.
      const wave: Wave = { x: hp.x, y: hp.y, dir: sw.dir, band: 0, hit: new Set(), record: { hits: [] } };
      waves.push(wave.record);
      // (On the sixteenth-note grid, from the strike's own sixteenth or the next one.)
      const bi0 = Math.ceil(sbiAt(s) * 4 - 1e-6) / 4;
      const bands = Math.round((hero.swings.heavy.wave.to - heavyReach.radius) / hero.swings.heavy.wave.band);
      for (let k = 0; k < bands; k++) q.push(timeOfBeatIndex(song, bi0 + k * 0.25), { kind: "wave", wave: { ...wave, band: k } });
    }
  };

  const waveBand = (wave: Wave) => {
    // The wave rests during a hit-stop, like the weapons: the next band covers this one's ground.
    if (inStop(s) && s > stopStart + 1e-9) return;
    const wd = hero.swings.heavy.wave;
    const outer = heavyReach.radius + wd.band * (wave.band + 1);
    const shape: Shape = { kind: "arc", x: wave.x, y: wave.y, radius: outer, inner: heavyReach.radius, dir: wave.dir, halfAngle: heavyReach.halfAngle };
    // Everyone the front has reached and the wave has not hit yet.
    const h = addHit({ source: "wave", what: "shockwave", weight: "medium", primary: false, shape, dir: wave.dir, accent: 0, damage: wd.damage * damageMultiplier(hero, groove), group: waves.indexOf(wave.record) });
    wave.record.hits.push(h.id);
    for (const f of applyHit(h, (f) => !wave.hit.has(f))) wave.hit.add(f);
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
    input.armed = swingFor(sch.gridSlots[armedStrike(hero, sch.gridTimes, s)], s, idx, rated.rating) !== undefined;
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
      const pu = punches[i];
      if (pu.w === undefined && sk >= pu.s - 1e-9) pu.w = worldOf(pu.s);
      // Before the impact the punch leads in by song time; after it, it decays in world time.
      const age = pu.w !== undefined ? wk - pu.w : sk - pu.s;
      if (age > 1.2 || age < -0.05) continue;
      const env = age < 0 ? smoothstep((age + 0.05) / 0.05) : Math.exp(-age / 0.22);
      zoom *= 1 + pu.amt * env;
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
      // Bring everyone to this step's time first. Formation members follow their slots
      // (and the boss holds still while it drops in).
      const dt = w1 - f.lastW;
      if (f.slot && s < f.slotUntil) {
        // On the slot now, moving with the anchor and the slot's own pace until the next step.
        const a = formationAnchor();
        const p = f.slot(s);
        const rate = rateAt(s);
        const q2 = f.slot(s + 1 / SIM_RATE);
        f.vx = a.vx + (rate > 0 ? ((q2.x - p.x) * SIM_RATE) / rate : 0);
        f.vy = a.vy + (rate > 0 ? ((q2.y - p.y) * SIM_RATE) / rate : 0);
        f.x = a.x + p.x;
        f.y = a.y + p.y;
        continue;
      }
      if (f.landS !== undefined && s < f.landS) {
        f.vx = 0;
        f.vy = 0;
        continue;
      }
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
              // Formation members and the landing boss hold their places: the crowd gives way.
              const pa = pinned(a);
              const pb = pinned(b);
              if (pa && pb) continue;
              const wa = pa ? 0 : pb ? 1 : 0.5;
              const k = (1 - d / lim) * 90;
              a.sx += ux * k * 2 * wa;
              a.sy += uy * k * 2 * wa;
              b.sx -= ux * k * 2 * (1 - wa);
              b.sy -= uy * k * 2 * (1 - wa);
              const gap = MIN_GAP * (a.r + b.r);
              if (d < gap) {
                const push = gap - d;
                a.x += ux * push * wa;
                a.y += uy * push * wa;
                b.x -= ux * push * (1 - wa);
                b.y -= uy * push * (1 - wa);
              }
            }
          }
        }
      }
    }
    // Archer groups share a post: on the line from the hero toward the group, at their range.
    const posts = new Map<number, { x: number; y: number; n: number }>();
    for (const f of alive) {
      if (f.archerGroup === undefined) continue;
      const p = posts.get(f.archerGroup) ?? { x: 0, y: 0, n: 0 };
      p.x += f.x;
      p.y += f.y;
      p.n++;
      posts.set(f.archerGroup, p);
    }
    for (const f of alive) {
      const dt = w1 - f.lastW;
      f.lastW = w1;
      if (pinned(f)) continue;
      if (f.archerGroup !== undefined) {
        const p = posts.get(f.archerGroup)!;
        const cx = p.x / p.n - hp.x;
        const cy = p.y / p.n - hp.y;
        const cl = Math.hypot(cx, cy) || 1;
        const off = ((f.archerSlot ?? 0) - 0.5) * ARCHER_GAP;
        const tx = hp.x + (cx / cl) * ARCHER_RANGE - (cy / cl) * off - f.x;
        const ty = hp.y + (cy / cl) * ARCHER_RANGE + (cx / cl) * off - f.y;
        const tl = Math.hypot(tx, ty) || 1;
        const sp = f.def.speed * (mults[f.def.group] ?? 1) * clamp(tl / 20, 0, 1);
        const kk = Math.min(1, dt * 12);
        f.vx += ((tx / tl) * sp - f.vx) * kk;
        f.vy += ((ty / tl) * sp - f.vy) * kk;
        continue;
      }
      const k = Math.min(1, dt * 12);
      const dx = hp.x - f.x;
      const dy = hp.y - f.y;
      const d = Math.hypot(dx, dy) || 0.01;
      const contact = HURTBOX_RADIUS + f.r + CONTACT_GAP;
      const m = mults[f.def.group] ?? 1;
      const charge = s < f.chargeUntil ? CHARGE_SPEED : 1;
      // Archers keep their distance; everyone else closes to contact.
      const speed =
        f.def.behavior === "archer" ? f.def.speed * m * clamp((d - ARCHER_RANGE) / 30, -0.5, 1) : f.def.speed * m * charge * clamp((d - contact) / 16, 0, 1);
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
  /** Threats the player can see: each only after it has been visible for 150 ms. */
  const visibleThreats = (hp: { x: number; y: number }): Observation["threats"] => {
    const out: Observation["threats"] = [];
    // Archers drawing (the arrow will fly at the hero's position at the release)...
    for (const v of sch.volleys) {
      if (s < v.sDraw + REACTION - 1e-9 || s > v.sRelease + 1e-9) continue;
      for (const f of alive) {
        if (!f.alive || f.archerGroup !== v.group || !f.drawing) continue;
        out.push({ kind: "arrow", x: posX(f, w), y: posY(f, w) - 24, ax: hp.x, ay: hp.y - HURTBOX_LIFT, landsAt: v.sArrive });
      }
    }
    // ...arrows in flight...
    for (const a of flying) {
      if (s < sch.volleys[a.v].sDraw + REACTION - 1e-9) continue;
      out.push({ kind: "arrow", x: a.rec.x0, y: a.rec.y0, ax: a.rec.ax, ay: a.rec.ay, landsAt: a.rec.sArrive });
    }
    // ...and slam markers.
    for (const [k, m] of slamMarks) if (s >= m.sMark + REACTION - 1e-9) out.push({ kind: "slam", x: m.x, y: m.y, radius: SLAM_RADIUS + HURTBOX_RADIUS, landsAt: sch.boss!.slams[k].sLand });
    return out;
  };

  const observe = (): Observation => {
    const hp = heroPose(w);
    const pp = pacePoint(s);
    return {
      s,
      hero: { x: hp.x, y: hp.y, stepping: hp.u < 1 },
      enemies: alive.filter((f) => f.alive).map((f) => ({ x: posX(f, w), y: posY(f, w), type: f.rec.type, health: f.health })),
      threats: visibleThreats(hp),
      boss: bossFoe && bossFoe.alive && !pinned(bossFoe) ? { x: posX(bossFoe, w), y: posY(bossFoe, w) } : undefined,
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
    const r = new Rng(ctx.seed, "spawn", Math.round(bi * 2));
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
    // The ring's front: everything inside it falls (the last band reaches everyone left).
    const inner = 0;
    const outer = last ? 1e6 : (b.radius * (band + 1)) / bands;
    const h = addHit({ source: "ring", what: wd.id, weight: "heavy", primary: false, shape: { kind: "ring", x: hp.x, y: hp.y, inner, outer }, dir: 0, accent: 0, damage: wd.damage }, 1);
    applyHit(h);
    if (last) {
      // Nothing survives the final band, wherever it stands.
      for (const f of alive) if (f.alive) damage(f, h);
      alive = alive.filter((f) => f.alive);
    }
  };

  // ---------------------------------------------------------------- deaths, gems and level-ups
  /** World time of a song time at or before now, from the warp so far. */
  const worldOf = (t: number) => {
    for (let i = breaks.length - 1; i >= 0; i--) if (breaks[i].s <= t + 1e-12) return breaks[i].w + (t - breaks[i].s) * breaks[i].rate;
    return 0;
  };
  const downbeatAtOrAfter = (t: number) => {
    let bar = Math.max(1, barOfBeatIndex(ctx.idx, sbiAt(t)));
    while (bar <= ctx.idx.barCount && timeOfBeatIndex(song, ctx.idx.barStart[bar]) < t - 1e-9) bar++;
    return bar <= ctx.idx.barCount ? timeOfBeatIndex(song, ctx.idx.barStart[bar]) : t;
  };
  const levelReq = () => sch.levelUps[Math.min(level, sch.levelUps.length - 1)]?.xp ?? 1;
  const pushXp = () => xpSeries.push({ s, fill: clamp(xpNow / levelReq(), 0, 1) });

  const onDeath = (f: Foe, fate: Fate, h: Hit) => {
    // Every death drops gems, which fly to the hero on the next downbeat (a beat after the finale blast).
    const big = f.def.size !== "regular";
    const s1 = blasting && blastS !== undefined ? timeOfBeatIndex(song, sbiAt(blastS) + 1) : downbeatAtOrAfter(s + 0.12);
    gems.push({ w0: w, x0: fate.x, y0: fate.y, s1, w1: 0, value: big ? 5 : 1, big, enemy: f.rec.id });
    q.push(s1, { kind: "gem", gem: gems.length - 1 });
    if (f === bossFoe && bossInfo) {
      bossInfo.deathS = s;
      bossInfo.killHit = h.id;
      if (h.source !== "ring") startHitStop(BOSS_KILL_STOP);
    }
  };

  const grant = (id: string) => {
    if (blasting) return;
    level++;
    xpNow = 0;
    overdue = false;
    pushXp();
    grantWeapon(id);
  };

  const gemArrives = (i: number) => {
    const g = gems[i];
    g.w1 = w;
    if (level >= sch.levelUps.length) {
      xpNow += g.value;
      pushXp();
      return;
    }
    xpNow += g.value;
    pushXp();
    // A bar that fills after its scheduled moment levels up on the first downbeat after it fills.
    if (overdue && xpNow >= levelReq()) {
      overdue = false;
      q.push(downbeatAtOrAfter(s), { kind: "levelUp", id: sch.levelUps[level].id });
    }
  };

  const levelDue = (i: number) => {
    if (i !== level) return;
    // A full bar waits and glows until the scheduled moment.
    if (xpNow >= levelReq()) grant(sch.levelUps[i].id);
    else overdue = true;
  };

  // ---------------------------------------------------------------- archers
  const spawnArchers = (group: number, n: number) => {
    const def = lib.enemies.get("skeleton-archer")!;
    const r = new Rng(ctx.seed, "archers", group, nextId);
    // They come from one side together, so a single step can dodge their volley.
    const p = spawnPoint(def, r, true);
    const hp = heroPose(w);
    const dx = p.x - hp.x;
    const dy = p.y - hp.y;
    const dl = Math.hypot(dx, dy) || 1;
    const taken = new Set(alive.filter((f) => f.alive && f.archerGroup === group).map((f) => f.archerSlot));
    for (let k = 0, placed = 0; placed < n && k < 16; k++) {
      if (taken.has(k)) continue;
      const off = (k - 0.5) * ARCHER_GAP;
      const f = spawnFoe("skeleton-archer", p.x + (-dy / dl) * off, p.y + (dx / dl) * off);
      f.archerGroup = group;
      f.archerSlot = k;
      placed++;
    }
  };

  const draw = (v: number) => {
    const vol = sch.volleys[v];
    const hp = heroPose(w);
    const group = alive.filter((f) => f.alive && f.archerGroup === vol.group);
    for (const f of group) if (Math.hypot(posX(f, w) - hp.x, posY(f, w) - hp.y) <= ARCHER_SHOOT_RANGE) f.drawing = { w0: w };
    // Replacements for fallen archers walk in for the next volley.
    if (group.length < vol.archers) spawnArchers(vol.group, vol.archers - group.length);
  };

  const loose = (v: number) => {
    const vol = sch.volleys[v];
    const hp = heroPose(w);
    // Each arrow aims at the hero's position at the moment of release, not a predicted one.
    const ax = hp.x;
    const ay = hp.y - HURTBOX_LIFT;
    for (const f of alive) {
      if (!f.alive || f.archerGroup !== vol.group || !f.drawing) continue;
      const x0 = posX(f, w);
      const y0 = posY(f, w) - 24;
      const dl = Math.hypot(ax - x0, ay - y0) || 1;
      const rec: ArrowRecord = { sRelease: s, wRelease: w, sArrive: vol.sArrive, wArrive: 0, x0, y0, ax, ay, x1: ax, y1: ay, w1: 0, outcome: "miss" };
      arrows.push(rec);
      flying.push({ v, rec, ux: (ax - x0) / dl, uy: (ay - y0) / dl });
      (f.rec.attacks ??= []).push({ w0: f.drawing.w0, w, kind: "shoot", dir: Math.atan2(ay - y0, ax - x0) });
      f.drawing = undefined;
    }
  };

  /** A near miss: the slow-motion dip starts now (dips do not stack). */
  const nearMiss = () => {
    if (s < dipStart + 2 * DIP_RAMP + DIP_HOLD) return;
    dipStart = s;
  };

  const arrive = (v: number) => {
    const hp = heroPose(w);
    const hx0 = hp.x;
    const hy0 = hp.y - HURTBOX_LIFT;
    for (let i = flying.length - 1; i >= 0; i--) {
      const a = flying[i];
      if (a.v !== v) continue;
      flying.splice(i, 1);
      const rec = a.rec;
      rec.wArrive = w;
      // Closest approach of the arrow's line to the hurtbox, ahead of the archer.
      const t = (hx0 - rec.x0) * a.ux + (hy0 - rec.y0) * a.uy;
      const perp = Math.abs((hx0 - rec.x0) * a.uy - (hy0 - rec.y0) * a.ux);
      const edge = perp - HURTBOX_RADIUS;
      rec.outcome = t < 0 ? "miss" : edge <= 0 ? "touch" : edge <= NEAR_MISS ? "near-miss" : "miss";
      const len = Math.hypot(rec.ax - rec.x0, rec.ay - rec.y0);
      const speed = len / Math.max(1e-3, w - rec.wRelease);
      if (rec.outcome === "touch") {
        // It shatters on the hero, harmlessly.
        rec.x1 = rec.x0 + a.ux * t;
        rec.y1 = rec.y0 + a.uy * t;
        rec.w1 = rec.wRelease + t / speed;
      } else {
        rec.x1 = rec.ax + a.ux * ARROW_OVERSHOOT;
        rec.y1 = rec.ay + a.uy * ARROW_OVERSHOOT;
        rec.w1 = w + ARROW_OVERSHOOT / speed;
      }
      threats.push({ s, w, kind: "arrow", outcome: rec.outcome, x: hp.x, y: hp.y });
      if (rec.outcome === "near-miss") nearMiss();
    }
  };

  // ---------------------------------------------------------------- the boss
  const bossSpawn = () => {
    const b = sch.boss!;
    const hp = heroPose(w);
    const pp = pacePoint(s);
    const x = hp.x + pp.tx * BOSS_LAND_AHEAD;
    const y = hp.y + pp.ty * BOSS_LAND_AHEAD;
    const f = spawnFoe(b.type, x, y);
    f.rec.entrance = "land";
    f.rec.attacks = [];
    f.landS = b.sEnter;
    f.floorUntil = b.sDeath;
    bossFoe = f;
    bossInfo = { id: f.rec.id, landS: b.sEnter, x, y, health: [{ s, frac: 1 }] };
  };

  const bossLand = () => {
    if (!bossFoe || !bossFoe.alive) return;
    // It lands on the downbeat, knocking nearby enemies back.
    for (const f of alive) {
      if (f === bossFoe || !f.alive || pinned(f)) continue;
      const dx = posX(f, w) - bossFoe.x;
      const dy = posY(f, w) - bossFoe.y;
      const d = Math.hypot(dx, dy);
      if (d >= BOSS_LAND_PUSH || d < 1e-6) continue;
      f.knockX = (dx / d) * (BOSS_LAND_PUSH - d) * 0.5;
      f.knockY = (dy / d) * (BOSS_LAND_PUSH - d) * 0.5;
      f.knockT = KNOCK_TIME;
    }
  };

  const slamMark = (k: number) => {
    if (!bossFoe || !bossFoe.alive) return;
    // A marker appears at the hero's current position and grows for two beats.
    const hp = heroPose(w);
    slamMarks.set(k, { x: hp.x, y: hp.y, sMark: s, wMark: w });
  };

  const slamLand = (k: number) => {
    const m = slamMarks.get(k);
    if (!m || !bossFoe || !bossFoe.alive) return;
    slamMarks.delete(k);
    const hp = heroPose(w);
    // The marker appears under the hero: leaving it before the slam lands is a near miss.
    const edge = Math.hypot(hp.x - m.x, hp.y - m.y) - SLAM_RADIUS - HURTBOX_RADIUS;
    const outcome = edge <= 0 ? "touch" : "near-miss";
    slamRecords.push({ sMark: m.sMark, wMark: m.wMark, sLand: s, wLand: w, x: m.x, y: m.y, radius: SLAM_RADIUS, outcome });
    bossFoe.rec.attacks!.push({ w0: Math.max(m.wMark, w - 0.6), w, kind: "slam", dir: Math.atan2(m.y - posY(bossFoe, w), m.x - posX(bossFoe, w)) });
    threats.push({ s, w, kind: "slam", outcome, x: hp.x, y: hp.y });
    if (outcome === "near-miss") nearMiss();
  };

  // ---------------------------------------------------------------- formations
  /** Formation members on their slots and the boss dropping in hold their places. */
  const pinned = (f: Foe) => (f.slot !== undefined && s < f.slotUntil) || (f.landS !== undefined && s < f.landS);

  /** Formations ride with the camera-smoothed hero: the camera centre less its lead. */
  const formationAnchor = () => {
    const lead = sectionAt(s).level.camera.lead;
    const pp = pacePoint(s);
    return { x: camera.x - pp.tx * lead, y: camera.y - pp.ty * lead + 12, vx: camera.vx, vy: camera.vy };
  };

  const formationEnter = (fi: number) => {
    const fs = sch.formations[fi];
    const def = lib.formations.get(fs.def)!;
    const view = camera.view();
    const bStart = sbiAt(fs.sStart);
    const stepB = subdivisionBeats(def.step, 2);
    const members: number[] = [];
    formationRecords.push({ def: fs.def, sStart: fs.sStart, sRelease: fs.sRelease, members });
    for (let k = 0; k < def.count; k++) {
      let place: (t: number) => { x: number; y: number };
      let start: { x: number; y: number };
      // Places are offsets from the anchor; the entrance starts just off the camera's view.
      const a0 = formationAnchor();
      if (def.shape === "kick-line") {
        // A line of dancers ahead of the hero, along the route.
        place = (t) => {
          const p = pacePoint(t);
          const d = LINE_LEAD + def.spacing * k;
          return { x: p.tx * d, y: p.ty * d };
        };
        const end = place(fs.sStart);
        start = { x: Math.max(view[2] + 100 + k * 6 - a0.x, end.x + 40), y: end.y };
      } else {
        // A V-wedge: the leader in front, the arms trailing back.
        const arm = k === 0 ? 0 : Math.ceil(k / 2);
        const side = k === 0 ? 0 : k % 2 === 0 ? 1 : -1;
        place = (t) => {
          const p = pacePoint(t);
          const cx = p.tx * 120;
          const cy = p.ty * 120 - 70;
          return { x: cx - p.tx * arm * def.spacing - p.ty * side * arm * def.spacing, y: cy - p.ty * arm * def.spacing + p.tx * side * arm * def.spacing };
        };
        const end = place(fs.sStart);
        start = { x: end.x + 40, y: view[1] - 120 - a0.y + (end.y - place(fs.sStart).y) };
      }
      // March in on the beat (or the formation's step): each step eases quickly, then holds.
      const steps = def.entranceBeats / stepB;
      const slot = (t: number) => {
        const d = place(t);
        const u = clamp((sbiAt(t) - bStart) / def.entranceBeats, 0, 1);
        if (u >= 1) return d;
        const kk = Math.floor(u * steps);
        const fr = u * steps - kk;
        const e = 1 - (1 - Math.min(1, (kk + Math.min(1, fr / 0.45)) / steps)) ** 2;
        return { x: lerp(start.x, d.x, e), y: lerp(start.y, d.y, e) - (def.shape === "kick-line" ? Math.sin(Math.PI * Math.min(1, fr / 0.45)) * 3 : 0) };
      };
      const p = slot(s);
      const f = spawnFoe(def.enemy, a0.x + p.x, a0.y + p.y);
      f.rec.formation = fi;
      f.weight = weightOf(f.def, true);
      f.slot = slot;
      f.slotUntil = fs.sRelease;
      members.push(f.rec.id);
    }
  };

  const formationRelease = (fi: number) => {
    const fs = sch.formations[fi];
    const def = lib.formations.get(fs.def)!;
    const wEnter = worldOf(timeOfBeatIndex(song, sbiAt(fs.sStart) + def.entranceBeats));
    const beat = (song.beats[Math.min(song.beats.length - 1, Math.floor(sbiAt(s)) + 1)] ?? s) - (song.beats[Math.floor(sbiAt(s))] ?? s);
    for (const f of foes) {
      if (f.rec.formation !== fi) continue;
      if (def.dance) f.rec.dance = { w0: wEnter, w1: f.alive ? w : f.rec.deathW, anim: def.dance.animation, alternate: def.dance.alternateLegs, phase: 0 };
      if (!f.alive) continue;
      // The survivors become chasers; a charge rushes the hero first.
      f.slotUntil = s;
      if (def.release.mode === "charge") f.chargeUntil = s + CHARGE_BEATS * Math.max(0.1, beat);
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
  for (let bi = 0; bi < song.beats.length; bi++) {
    q.push(song.beats[bi], { kind: "beat", bi });
    // Free movement's beats are slow: the spawn director also looks on the off-beat eighth.
    if (bi + 1 < song.beats.length && sectionAt(song.beats[bi]).level.movement === "free") q.push(timeOfBeatIndex(song, bi + 0.5), { kind: "spawnTick", bi: bi + 0.5 });
  }
  sch.levelUps.forEach((lu, i) => q.push(lu.s, { kind: "levelDue", i }));
  sch.archerGroups.forEach((g, i) => q.push(g.s, { kind: "archers", group: i, n: g.archers }));
  sch.volleys.forEach((v, i) => {
    q.push(v.sDraw, { kind: "draw", v: i });
    q.push(v.sRelease, { kind: "release", v: i });
    q.push(v.sArrive, { kind: "arrive", v: i });
  });
  if (sch.boss) {
    // The boss drops in for half a second before it lands on its downbeat.
    q.push(Math.max(0, sch.boss.sEnter - 0.55 / Math.max(0.05, baseRate(sch.boss.sEnter))), { kind: "bossSpawn" });
    q.push(sch.boss.sEnter, { kind: "bossLand" });
    sch.boss.slams.forEach((sl, k) => {
      q.push(sl.sMark, { kind: "slamMark", k });
      q.push(sl.sLand, { kind: "slamLand", k });
    });
  }
  sch.formations.forEach((f, i) => {
    q.push(f.sStart, { kind: "formation", f: i });
    q.push(f.sRelease, { kind: "formationRelease", f: i });
  });
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
    const tRate = Math.min(nextStepAfter(sch.baseSteps, s), inStop(s) ? stopEnd : Infinity, nextDipCut(s));
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
        case "spawnTick":
          spawnDirector(ev.bi);
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
          grant(ev.id);
          break;
        case "levelDue":
          levelDue(ev.i);
          break;
        case "gem":
          gemArrives(ev.gem);
          break;
        case "wave":
          waveBand(ev.wave);
          break;
        case "archers":
          spawnArchers(ev.group, ev.n);
          break;
        case "draw":
          draw(ev.v);
          break;
        case "release":
          loose(ev.v);
          break;
        case "arrive":
          arrive(ev.v);
          break;
        case "bossSpawn":
          bossSpawn();
          break;
        case "bossLand":
          bossLand();
          break;
        case "slamMark":
          slamMark(ev.k);
          break;
        case "slamLand":
          slamLand(ev.k);
          break;
        case "formation":
          formationEnter(ev.f);
          break;
        case "formationRelease":
          formationRelease(ev.f);
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
    arrows,
    slams: slamRecords,
    threats,
    boss: bossInfo,
    gems,
    xp: xpSeries,
    waves,
    formations: formationRecords,
  };
}
