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
 *
 * The simulation is a steppable `Sim`: the demo runs it to the end at load
 * time (`simulate`), play mode advances it a frame at a time and clones it for
 * rollback. Everything it produces goes to a Recorder; its own state is small.
 */
import type { EnemyDef, HeroDef, WeaponDef } from "../content/schema.ts";
import { TAU, type Shape } from "../core/geometry.ts";
import { Rng } from "../core/rng.ts";
import { beatHit, beatSwell, clamp, frac, lerp, smoothstep } from "../core/shaping.ts";
import { beatIndexAt, energyAt, timeOfBeatIndex, barOfBeatIndex } from "../core/songmap.ts";
import type { WarpBreak } from "../core/warp.ts";
import type { AimSnapshot, Fate, Hit, PlanInput, PlanSwing } from "../plan/types.ts";
import { CameraRig } from "../planner/camera.ts";
import { sectionIndexAt, subdivisionBeats, weightFactor, type Ctx } from "../planner/context.ts";
import { aim, catches, swingArc, type AimTarget } from "./aim.ts";
import { deepClone, registerShared } from "./clone.ts";
import { bendStep, movePose, opposite, stepEnd, walkFrame, type InputKey, type Key, type Step } from "./movement.ts";
import type { KeyEvent, Observation, Player } from "./player.ts";
import { BatchRecorder, type ArrowRecord, type PressTrace, type Recorder, type SimResult } from "./recorder.ts";
import { nextStepAfter, stepRate, stepSpan, type GridSlot, type Schedule } from "./schedule.ts";
import { budgetAt, freshStrides, heldDirection, interrupt, periodAt, settle, startStride, stompCancelsUntil, strideRule, striding, type StrideState } from "./strides.ts";
import { chainAt, heavyArcAt, orbitAt, swingArcAt, tierDamage, trailRadiusAt, waveReachAt } from "./tiers.ts";
import { armouredBite, evolvedOrbit, offersFor, passiveBonus, startingLoadout, takeItem, type Loadout, type UpgradeItem } from "./upgrades.ts";
import { classifyPress, NEVER_LATE, drainPerBeat, firstAtOrAfter, freshPressMemo, grooveAfterChange, type PressJudgement, type PressMemo, nearestIndex, ratePressOnGrid, tierFor } from "./timing.ts";
import { OpenWorld, type World } from "./world.ts";

export type { ArrowRecord, Landing, SimResult, SlamRecord } from "./recorder.ts";

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
/** An enemy this much farther than its stopping point still bites. */
const BITE_SLACK = 2;
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
export const KNOCKBACK = 10;
const KNOCK_TIME = 0.12;
/** An enemy this many view widths behind the hero is recycled to a spawn point ahead. */
const RECYCLE_VIEWS = 1.5;
/** The hard population cap. */
export const POP_CAP = 520;
/** The horde's set pieces: the steady stream aims for this share of the population curve. */
export const STREAM_SHARE = 0.6;
/** A closing ring reaches this far from its centre on its accent (a tier-0 swing's reach); a second row rides this much further out. */
export const RING_CLOSE = 100;
const RING_ROW = 32;
/** A stampede runs at this times its enemy's speed, in rows this far apart. */
export const STAMPEDE_SPEED = 2.5;
const STAMPEDE_ROW = 48;
/**
 * Upgrade orbs: a level-up's pulse pushes enemies within LEVEL_PULSE of the hero out to it (px) and
 * heals LEVEL_HEAL; its orbs rise ORB_RISE from the hero, one ahead and one ORB_SPREAD to each side;
 * walking within ORB_TOUCH takes one, and after ORB_BARS bars the one ahead is taken.
 */
export const LEVEL_PULSE = 160;
export const LEVEL_HEAL = 20;
export const ORB_RISE = 120;
export const ORB_SPREAD = (2 * Math.PI) / 3;
export const ORB_TOUCH = 24;
export const ORB_BARS = 2;
/**
 * The small health pool: an orc elite drops a chicken with probability CHICKEN_CHANCE and the boss
 * CHICKEN_BOSS of them (CHICKEN_SPREAD px apart); touched within CHICKEN_TOUCH, one heals
 * CHICKEN_HEAL on the first sixteenth at least NEVER_LATE away.
 */
export const CHICKEN_CHANCE = 0.5;
export const CHICKEN_BOSS = 2;
export const CHICKEN_SPREAD = 28;
export const CHICKEN_TOUCH = 24;
export const CHICKEN_HEAL = 30;
/** Upgrade orbs: a taken orb applies on a grid time at most this far away (s), else on the next beat. */
const ORB_GRID_WAIT = 2;
/** Gems on the field: the virtual player sees gems this far beyond the pickup radius (px). */
const GEM_SEEN = 300;
/** Gems on the field: at most this many lie on the field. */
export const GEM_CAP = 300;
/** A vacuum drops this far ahead of the hero, is touched within this distance, and its gems land over one bar. */
export const VACUUM_AHEAD = 150;
export const VACUUM_TOUCH = 24;
/** The player sees a vacuum within this distance (px) of the hero. */
const VACUUM_SEEN = 400;
/** Hit-stop limits (seconds). */
export const HITSTOP_MAX = 0.15;
export const HITSTOP_MIN = 0.04;
/** A near miss: a threat passing this close to the hurtbox's edge without touching it. */
export const NEAR_MISS = 24;
/** The near-miss slow-motion dip: down to 0.4 over 40 ms, held 70 ms, back up over 40 ms. */
const DIP_RATE = 0.4;
/** A fully calm section's curl: enemies head this far (radians) off the line to the hero, so they spiral in. */
const CALM_CURL = Math.PI / 3;
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
/**
 * Self-firing weapons: a heavy swing aims this much beyond its shockwave's reach, at what will charge
 * into it (the open field's kick line is a file along the heading, 170-480 px ahead).
 */
const CLEAVE_AIM_AHEAD = 160;
/** The kick line stands this far ahead of the pace point; its charge is this much faster than a walk. */
const LINE_LEAD = 170;
const CHARGE_BEATS = 2;
const CHARGE_SPEED = 4;
/** The open field: the heading is the hero's displacement over this many beats, once it is at least this long (px). */
const HEADING_BEATS = 2;
const HEADING_MIN = 6;
/** An intermediate clock stop keeps at least this far (song seconds) from the loop's own stops. */
const CLOCK_EPS = 1e-9;
/** Hero samples kept for the fire trail (world seconds); the trail reaches back two beats. */
const HERO_HISTORY = 10;

export const HERO_ANIMS = ["idle", "walk", "run", "jump", "slash", "backslash", "halfslash", "thrust", "spellcast", "slash-oversize", "hurt"];

export interface SimOptions {
  player: Player;
  /** Causality check: drop every press after this song time (the player stops; the game keeps running). */
  cutS?: number;
  /** Stop the simulation at this song time (default: the end of the song). */
  untilS?: number;
}

/** Per-enemy simulation state. */
interface Foe {
  id: number;
  type: string;
  def: EnemyDef;
  r: number;
  health: number;
  /** Health at spawn. */
  health0: number;
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
  deathW: number;
  lastFate?: Fate;
  formation?: number;
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
  /** A closing ring's member (set pieces): its place in the world by song time, until slotUntil. */
  at?: (t: number) => { x: number; y: number };
  /** A stampede's runner (set pieces): its fixed velocity straight across the view (px per world second). */
  run?: { vx: number; vy: number };
  /** A charge (the kick line's release) runs faster until this song time. */
  chargeUntil: number;
  /** Archer group, and this archer's place in it (shoulder to shoulder at a shared post). */
  archerGroup?: number;
  archerSlot?: number;
  drawing?: { w0: number };
  /** The boss lands at landS (out of reach while it drops in). */
  landS?: number;
}

/** A heavy impact on an accent, as its shockwave would launch from it. */
interface PendingCleave {
  gi: number;
  s: number;
  x: number;
  y: number;
  dir: number;
  tier: number;
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
  /** The tier read when the wind-up started (it sizes the swing and its damage). */
  tier?: number;
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
  | { kind: "held" }
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
  | { kind: "ring"; k: number }
  | { kind: "stampede"; k: number }
  | { kind: "formationRelease"; f: number }
  | { kind: "gem"; gem: number; value: number }
  | { kind: "vacuumDrop" }
  | { kind: "levelDue"; i: number }
  | { kind: "levelUp"; id: string }
  | { kind: "orbDefault"; u: number }
  | { kind: "orbApply"; item: UpgradeItem }
  | { kind: "chickenHeal" }
  | { kind: "zoneWave"; zone: string }
  | { kind: "landmark"; id: string };

interface Wave {
  x: number;
  y: number;
  dir: number;
  band: number;
  hit: Set<Foe>;
  /** Index of the wave's record. */
  group: number;
  /** Its cleave's damage multiplier (the tier's). */
  mult: number;
  /** The heavy impact's arc at its tier, where the bands start, their half-angle, and how far the last reaches. */
  inner: number;
  halfAngle: number;
  to: number;
}

interface ChainState {
  /** The chain's latest point. */
  last: { w: number; x: number; y: number };
  /** Index of the chain's record. */
  index: number;
  hit: Set<Foe>;
  jumps: number;
  energy: number;
  bi: number;
  /** Each jump's reach (when the weapon has tiers) and damage multiplier, set by the tier when it fired. */
  range?: number;
  mult: number;
}

/** Event order at equal times. */
const ORDER: Record<Ev["kind"], number> = {
  spawnTick: 21,
  hitstopEnd: 0,
  mode: 1,
  levelDue: 2,
  // (A level-up still waiting for its orbs at a new one's moment takes its orb ahead then (see grant);
  // an upgrade applies before the weapons fire on its grid time.)
  orbDefault: 2.5,
  levelUp: 3,
  orbApply: 3.5,
  chickenHeal: 20,
  formation: 4,
  ring: 4,
  stampede: 4,
  bossSpawn: 4,
  archers: 4,
  decide: 5,
  key: 6,
  // A held stride comes after a key at the same time: a press there has already stridden.
  held: 6.5,
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
  vacuumDrop: 4,
  beat: 21,
  appear: 22,
  zoneWave: 4,
  landmark: 4,
};

/** Events that point at mutable state; the rest are frozen, so snapshots share them. */
const MUTABLE_EVENTS = new Set<Ev["kind"]>(["wind", "strike", "wave", "chain"]);

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

type Entry = { s: number; o: number; seq: number; ev: Ev };

class Queue {
  private heap: Entry[] = [];
  private seq = 0;
  push(s: number, ev: Ev): void {
    const h = this.heap;
    const entry: Entry = { s, o: ORDER[ev.kind], seq: this.seq++, ev };
    if (!MUTABLE_EVENTS.has(ev.kind)) Object.freeze(entry);
    h.push(entry);
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
  /** Drop every event that matches (order among the rest is kept: it rests on time, kind and push order). */
  remove(drop: (ev: Ev) => boolean): void {
    const kept = this.heap.filter((e) => !drop(e.ev));
    this.heap = [];
    for (const e of kept.sort((a, b) => (this.less(a, b) ? -1 : 1))) this.heap.push(e);
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

/** What a simulation is made of that never changes: shared by every clone. */
interface Env {
  ctx: Ctx;
  sch: Schedule;
  world: World;
  endS: number;
  cutS: number;
  shared: WeakSet<object>;
}

/** Where a simulation sends its events, and who decides its presses (the batch run's virtual player). */
interface Io {
  recorder: Recorder;
  player?: Player;
}

type Punch = { s: number; amt: number; w?: number };

/** Everything a simulation carries from one moment to the next. A snapshot is a deep copy of it. */
/** An element of one of the state's lists. */
type StateOf<K extends "field" | "vacuums"> = SimState[K][number];

interface SimState {
  s: number;
  w: number;
  /** Song time of the loop's latest own stop: the rate is read there (live frames stop in between). */
  rateS: number;
  q: Queue;
  breaks: WarpBreak[];
  stopStart: number;
  stopEnd: number;
  restPose: { x: number; y: number };
  /** The latest step (or stride), which ends standing still at its landing spot. */
  step?: Step;
  /** Free movement: the current walk (undefined in step movement). With strides, it may start when the stride in progress lands. */
  walk?: Walk;
  /** Strides: the stride budget. */
  strides: StrideState;
  held: Set<Key>;
  walked: number;
  lastSample?: { x: number; y: number };
  landed: boolean;
  facingAngle: number;
  groove: number;
  lastPressS: number;
  /** Press classes: what earlier presses took, the tier, and the latest press that raised the groove. */
  memo: PressMemo;
  tier: number;
  lastRaiseS: number;
  /**
   * Press cleaves: an eligible accent's heavy impact that struck before its counted press, waiting
   * for that press (Great or better launches its shockwave): the accent's grid index and time, where
   * the hero stood and aimed, and the tier.
   */
  pendingCleave?: PendingCleave;
  /** Swings that may still matter (the idle-swing rule looks back one bar). */
  swings: Swing[];
  liveSwings: Swing[];
  takenSlots: Set<GridSlot>;
  lightCount: number;
  nInputs: number;
  nHits: number;
  nWaves: number;
  nChains: number;
  nArrows: number;
  nGems: number;
  /** Gems on the field: those lying where they dropped, by gem record; the vacuums still to touch; the stampedes in play (formation index) and whether their vacuum has dropped. */
  field: { gem: number; x: number; y: number; value: number; big: boolean }[];
  vacuums: { i: number; x: number; y: number }[];
  nVacuums: number;
  /** The upgrade pool: what the hero has beside the sword, and its passives' bonuses added up. */
  loadout: Loadout;
  bonus: { maxHealth: number; pickup: number; damage: number; armour: number };
  /** Upgrade orbs waiting to be walked into: the upgrade record, its offers, where each orb is, and when the one ahead is taken; and items taken but not yet applied. */
  orbs?: { u: number; offers: UpgradeItem[]; pts: { x: number; y: number }[]; deadline: number };
  applying: UpgradeItem[];
  /** The small health pool: world time the hero's invulnerability lasts until, and the chickens lying on the field. */
  invulnUntil: number;
  chickens: { i: number; x: number; y: number }[];
  nChickens: number;
  stampedes: { fi: number; done: boolean }[];
  /** Camera punches: song time, strength, and world time once it has happened (it freezes in a hit-stop). */
  punches: Punch[];
  alive: Foe[];
  pending: number;
  nextId: number;
  /** Enemies still recording samples. */
  foeSamples: Foe[];
  formationMembers: Foe[][];
  /** Set pieces: each closing ring, its centre, when it spawned and closes, and the angles of its gaps. */
  rings: { x: number; y: number; s0: number; s1: number; r0: number; gaps: number[] }[];
  blasting: boolean;
  flying: { v: number; index: number; x0: number; y0: number; ax: number; ay: number; wRelease: number; ux: number; uy: number; damage: number }[];
  /** The hero's health, in the hero's health units. */
  health: number;
  slamMarks: Map<number, { x: number; y: number; sMark: number; wMark: number }>;
  bossFoe?: Foe;
  xpNow: number;
  level: number;
  overdue: boolean;
  /** The open field: the hero's heading (a unit vector), and a formation's direction while it is in play. */
  heading: { x: number; y: number };
  formationDir?: { x: number; y: number; until: number };
  /** Recent hero samples (for the fire trail): sample index heroBase onward. */
  heroX: number[];
  heroY: number[];
  heroBase: number;
  camera: CameraRig;
  nextHeroK: number;
  nextFoeK: number;
  /** The near-miss dip, as constant steps from its start (song time). */
  dipStart: number;
  head: Int32Array;
  stamp: Int32Array;
  nextArr: Int32Array;
  gen: number;
  fixedK: number;
  lastFixedW: number;
  recycleK: number;
}

const CELL = 32;
const G = 192;

function freshState(env: Env): SimState {
  const song = env.ctx.song;
  const punches: Punch[] = [];
  for (const a of song.accents) {
    const sec = env.ctx.sections[sectionIndexAt(env.ctx, a.time)];
    if (sec.level.camera.punchOnAccents) punches.push({ s: a.time, amt: 0.02 + 0.02 * a.strength });
  }
  const p0 = env.world.pacePoint(0);
  return {
    s: 0,
    w: 0,
    rateS: 0,
    q: new Queue(),
    breaks: [],
    stopStart: -1,
    stopEnd: -1,
    restPose: { x: p0.x, y: p0.y },
    held: new Set(),
    strides: freshStrides(),
    walked: 0,
    landed: true,
    facingAngle: 0,
    groove: 0,
    lastPressS: -Infinity,
    memo: freshPressMemo(),
    tier: 0,
    lastRaiseS: -Infinity,
    swings: [],
    liveSwings: [],
    takenSlots: new Set(),
    lightCount: 0,
    nInputs: 0,
    nHits: 0,
    nWaves: 0,
    nChains: 0,
    nArrows: 0,
    nGems: 0,
    field: [],
    vacuums: [],
    nVacuums: 0,
    loadout: startingLoadout(env.ctx.level.upgrades.loadout),
    bonus: { maxHealth: 0, pickup: 0, damage: 0, armour: 0 },
    applying: [],
    invulnUntil: -Infinity,
    chickens: [],
    nChickens: 0,
    stampedes: [],
    punches,
    alive: [],
    pending: 0,
    nextId: 0,
    foeSamples: [],
    formationMembers: env.sch.formations.map(() => []),
    rings: [],
    blasting: false,
    flying: [],
    health: env.ctx.lib.hero.health.max,
    slamMarks: new Map(),
    xpNow: 0,
    level: 0,
    overdue: false,
    heading: { x: 1, y: 0 },
    heroX: [],
    heroY: [],
    heroBase: 0,
    camera: new CameraRig(),
    nextHeroK: 0,
    nextFoeK: 0,
    dipStart: -Infinity,
    head: new Int32Array(G * G),
    stamp: new Int32Array(G * G).fill(-1),
    nextArr: new Int32Array(1024),
    gen: 0,
    fixedK: 1,
    lastFixedW: 0,
    recycleK: 0,
  };
}

/** The game rules over one state: every closure reads and writes `st`. */
function engine(env: Env, st: SimState, io: Io) {
  const { ctx, sch, world, endS, cutS } = env;
  const hero: HeroDef = ctx.lib.hero;
  const song = ctx.song;
  const lib = ctx.lib;
  const anims = ctx.heroAnims;
  const animIdx = (name: string) => Math.max(0, HERO_ANIMS.indexOf(name));
  /** The way forward now: the hero's heading. */
  const forward = (): { x: number; y: number; tx: number; ty: number } => {
    const hp = heroPose(st.w);
    return { x: hp.x, y: hp.y, tx: st.heading.x, ty: st.heading.y };
  };
  /** The camera lead's direction at song time s: the heading (a formation's direction while it is in play). */
  const leadDir = (s: number): { tx: number; ty: number } => {
    const fd = st.formationDir;
    if (fd && s < fd.until) return { tx: fd.x, ty: fd.y };
    return { tx: st.heading.x, ty: st.heading.y };
  };
  const sectionAt = (s: number) => ctx.sections[sectionIndexAt(ctx, s)];
  /** Each section's first grid time (pulsed sections only). */
  const firstGrid = new Map<number, number>();
  for (const g of sch.gridSlots) if (!firstGrid.has(g.section)) firstGrid.set(g.section, g.s);
  /**
   * The section a press belongs to: its own, or, up to the Good window before a pulsed section's
   * first grid time, that section (the press is that grid time's counted press, as mid-section).
   */
  const pressSectionAt = (s: number) => {
    const i = sectionIndexAt(ctx, s);
    const next = ctx.sections[i + 1];
    if (ctx.sections[i].level.movement === "steps" || next?.level.movement !== "steps") return ctx.sections[i];
    const g = firstGrid.get(i + 1);
    return g !== undefined && g - s <= hero.timing.goodMs / 1000 + 1e-9 ? next : ctx.sections[i];
  };
  const blastS = sch.blast?.s;
  const rec = () => io.recorder;

  // ---------------------------------------------------------------- time
  const baseRate = (t: number) => stepRate(sch.baseSteps, t);
  const inStop = (t: number) => t >= st.stopStart - 1e-12 && t < st.stopEnd - 1e-12;
  const DIP_STEPS = [0, 0.25, 0.5, 0.75, 1].map((k) => k * DIP_RAMP);
  const dipCuts = () => [...DIP_STEPS, ...DIP_STEPS.map((d) => DIP_RAMP + DIP_HOLD + d)];
  const dipFactor = (t: number) => {
    const d = t - st.dipStart;
    if (d < 0 || d >= 2 * DIP_RAMP + DIP_HOLD - 1e-12) return 1;
    if (d < DIP_RAMP) return 1 - (1 - DIP_RATE) * ((Math.floor((d / DIP_RAMP) * 4 + 1e-9) + 1) / 4);
    if (d < DIP_RAMP + DIP_HOLD) return DIP_RATE;
    return DIP_RATE + (1 - DIP_RATE) * ((Math.floor(((d - DIP_RAMP - DIP_HOLD) / DIP_RAMP) * 4 + 1e-9) + 1) / 4);
  };
  const nextDipCut = (t: number) => {
    for (const c of dipCuts()) if (st.dipStart + c > t + 1e-12) return st.dipStart + c;
    return Infinity;
  };
  const rateAt = (t: number) => (inStop(t) ? 0 : Math.round(baseRate(t) * dipFactor(t) * 1e6) / 1e6);
  const pushBreak = (rate: number) => {
    const breaks = st.breaks;
    const last = breaks[breaks.length - 1];
    if (last && last.rate === rate) return;
    const b = Object.freeze({ s: st.s, w: st.w, rate });
    if (last && Math.abs(last.s - st.s) < 1e-12) {
      breaks[breaks.length - 1] = b;
      rec().warp(b, true);
      return;
    }
    breaks.push(b);
    rec().warp(b, false);
  };
  /** Song beat index at song time t. */
  const sbiAt = (t: number) => beatIndexAt(song, t);

  // ---------------------------------------------------------------- hero
  /**
   * The hero at world time atW: walking (free movement), or on its latest step
   * (which ends standing still at its landing spot).
   */
  const heroPose = (atW: number): { x: number; y: number; z: number; u: number; vx: number; vy: number } => {
    // A walk waits for the stride in progress to land.
    if (st.walk && (!st.step || atW >= st.walk.w0 - 1e-12)) return { ...walkPose(st.walk, atW), z: 0, u: 1 };
    if (st.step) return { ...movePose(st.step, atW, rule.durationS, rule.hop), vx: 0, vy: 0 };
    return { x: st.restPose.x, y: st.restPose.y, z: 0, u: 1, vx: 0, vy: 0 };
  };

  // ---------------------------------------------------------------- strides
  const rule = strideRule(hero);
  /** How long a stride takes. */
  const stepDur = rule.durationS;
  /** One stride at song time s: the walk speed times the stride period. */
  const oneStride = (s: number) => rule.speed * (sectionAt(s).level.movement === "free" ? rule.freePeriodS : periodAt(sch.strideSteps, s));
  /**
   * A held stride's limit: one stride of the step just ended, the time it built up over. (On an
   * uneven grid, a step shorter than the one before would otherwise lose ground while holding.)
   */
  const heldStrideLimit = () => oneStride(st.s - 1e-6);
  /**
   * Strides: how far a press would stride now: the budget, plus the ground a stride still moving
   * would give back (as an interruption does), at most one stride.
   */
  const strideNow = (hp: { x: number; y: number }) => {
    const one = oneStride(st.s);
    const sp = st.step;
    const back = sp && striding(rule, sp, st.w) ? Math.max(0, Math.hypot(sp.ex, sp.ey) - Math.hypot(hp.x - sp.x0, hp.y - sp.y0)) : 0;
    return Math.min(one, budgetAt(rule, st.strides, st.w, one) + back);
  };
  /** A press's key goes down: held keys keep the order they were pressed in (the more recent of two opposite keys wins). */
  const hold = (key: InputKey) => {
    if (key === "space") return;
    st.held.delete(key);
    st.held.add(key);
  };
  /** Would this key bend the step (or press stride) in progress into a diagonal? */
  const bendOf = (key: InputKey) => (key !== "space" && st.step ? bendStep(hero, st.step, st.w, key, world.clamp, stepDur) : undefined);
  /**
   * Strides: would this key, opposite to the press whose stride is moving and within the stomp
   * window of it, make a stomp of the two (rocking that stride back)?
   */
  const rocks = (key: InputKey): boolean => {
    const sp = st.step;
    if (key === "space" || !sp || sp.held || sp.rock || sp.bendE !== undefined || sp.keys.length === 0 || !striding(rule, sp, st.w)) return false;
    return st.s - (sp.press?.s ?? sp.s0 ?? -Infinity) <= rule.stompS + 1e-9 && opposite(sp.keys[0], key);
  };
  /** A press that belongs to the one before it: a diagonal's second key, or a stomp's opposite key. */
  const partnerOf = (key: InputKey) => bendOf(key) !== undefined || rocks(key);
  /** Strides, free movement: is the hero walking now (not waiting for a stride to land)? */
  const walking = () => st.walk !== undefined && (!st.step || st.w >= st.walk.w0 - 1e-12);
  /** Strides, free movement: the walk ends where the hero is now (a walk waiting for a landing is dropped). */
  const stopWalk = () => {
    if (!st.walk) return;
    settle(rule, st.strides, st.w, oneStride(st.s));
    st.strides.walkFrom = Infinity;
    if (walking()) {
      const p = heroPose(st.w);
      st.step = undefined;
      st.restPose = { x: p.x, y: p.y };
    }
    st.walk = undefined;
  };
  /**
   * Strides, free movement: walk with the held keys at the walk speed, from the landing of the stride
   * in progress, or from now (slowing to a stop when no key is held). The walk spends the refill.
   */
  const walkOn = () => {
    const was = walking() ? heroPose(st.w) : undefined;
    stopWalk();
    const dir = heldDirection(st.held);
    const tx = dir ? dir.x * rule.speed : 0;
    const ty = dir ? dir.y * rule.speed : 0;
    const accel = hero.walk.speed / (hero.walk.accelMs / 1000);
    if (st.step && striding(rule, st.step, st.w)) {
      if (!dir) return;
      const land = st.step.w0 + rule.durationS;
      const end = stepEnd(st.step);
      st.walk = { w0: land, x0: end.x, y0: end.y, vx0: 0, vy0: 0, tx, ty, t: rule.speed / accel };
      st.strides.walkFrom = land;
      return;
    }
    if (!dir && !was) return;
    const p = was ?? heroPose(st.w);
    st.walk = { w0: st.w, x0: p.x, y0: p.y, vx0: p.vx, vy0: p.vy, tx, ty, t: Math.hypot(tx - p.vx, ty - p.vy) / accel };
    st.step = undefined;
    if (dir) {
      st.strides.walkFrom = st.w;
      st.facingAngle = Math.atan2(dir.y, dir.x);
    }
  };
  /**
   * Strides: stop the stride in progress where it is (its uncovered ground goes back to the budget)
   * and set off toward the held keys with the whole budget. `key` is the press that strides
   * (undefined for a held stride).
   */
  const strideOff = (key: Key | undefined): { stride: number; returned: number } => {
    const one = key === undefined ? heldStrideLimit() : oneStride(st.s);
    const hp = heroPose(st.w);
    const prev = st.step && striding(rule, st.step, st.w) ? st.step : undefined;
    const returned = prev ? interrupt(rule, st.strides, prev, st.w, hp.x, hp.y, one) : 0;
    const dir = heldDirection(st.held) ?? { x: Math.cos(st.facingAngle), y: Math.sin(st.facingAngle) };
    // A held stride taking over a press's stride within the diagonal window carries on that press:
    // a perpendicular key still bends it.
    const press = key === undefined && prev && !prev.held && !prev.rock && prev.keys.length > 0 && prev.bendE === undefined ? (prev.press ?? { w: prev.w0, s: prev.s0 ?? st.s, x: prev.x0, y: prev.y0 }) : undefined;
    const carried = press !== undefined && (st.w - press.w <= hero.step.diagonalWindowMs / 1000 + 1e-9 || st.s - press.s <= rule.stompS + 1e-9);
    st.step = startStride(rule, st.strides, st.w, hp.x, hp.y, dir, one, { key, held: key === undefined && !carried }, world.clamp);
    st.step.s0 = st.s;
    if (carried) Object.assign(st.step, { press, keys: prev!.keys });
    st.landed = false;
    st.facingAngle = Math.atan2(dir.y, dir.x);
    return { stride: st.step.len ?? 0, returned };
  };
  /**
   * Strides: a stomp plants the hero. A stride that started within the stomp window rocks back to
   * where it began (the ground it had not covered goes back to the budget); an older one stops where
   * it is. Then, unless rocking, both feet stamp: a hop in place. Either way a ring of dust, no
   * sound, and in step movement the held strides of one grid step are cancelled.
   */
  const stompHere = (input: PlanInput): Pick<PressTrace, "budget" | "returned"> => {
    input.stomp = true;
    const hp = heroPose(st.w);
    let returned = 0;
    const moving = st.step && striding(rule, st.step, st.w) ? st.step : undefined;
    // Only a stride (held, or a press's) rocks; a stamp still hopping stamps again.
    const rocking = moving && (moving.held || moving.keys.length > 0) && !moving.rock && st.s - (moving.press?.s ?? moving.s0 ?? -Infinity) <= rule.stompS + 1e-9;
    // A rock step returns to where the press's stride began (a held stride that took over from it carries it).
    const home = moving?.press ?? (moving ? { x: moving.x0, y: moving.y0 } : undefined);
    if (moving) returned = interrupt(rule, st.strides, moving, st.w, hp.x, hp.y, oneStride(st.s), rocking ? Math.hypot(moving.x0 - home!.x, moving.y0 - home!.y) : 0);
    if (rocking) {
      st.step = { ...moving, ex: 0, ey: 0, rock: { w: st.w, x: hp.x, y: hp.y, tx: home!.x, ty: home!.y } };
      // It lands where it began: the ring of dust is its landing, with no rustle after.
      st.landed = true;
      input.rock = true;
    } else {
      st.step = { w0: st.w, s0: st.s, x0: hp.x, y0: hp.y, dx: Math.cos(st.facingAngle), dy: Math.sin(st.facingAngle), len1: 0, ex: 0, ey: 0, keys: [], len: 0, held: false };
      // A stamp lands where it began: no landing puff or rustle after it.
      st.landed = true;
    }
    rec().landing({ s: st.s, w: st.w, x: hp.x, y: hp.y, stomp: true });
    // (A stomp just before a pulsed section's first grid time, which counts there, cancels its held stride too.)
    if (pressSectionAt(st.s).level.movement === "steps") st.strides.stompUntil = stompCancelsUntil(sch.strideSteps, st.s);
    return { budget: budgetAt(rule, st.strides, st.w, oneStride(st.s)), returned };
  };
  /**
   * Strides: a step of the stride period. A held key strides, taking over from a stride still moving
   * as a press would (so no ground is lost), except one a press started at this very moment. A stomp
   * cancels the held strides of one grid step.
   */
  const heldStride = () => {
    if (st.held.size === 0 || sectionAt(st.s).level.movement !== "steps" || st.s <= st.strides.stompUntil + 1e-9) return;
    if (st.step && !st.step.held && Math.abs(st.w - st.step.w0) < 1e-9) return;
    strideOff(undefined);
  };

  /** A section boundary: into step movement, a walk in progress stops and held strides take over on the grid; into free movement, the hero walks with the held keys. */
  const switchMode = (steps: boolean) => {
    if (steps) stopWalk();
    else walkOn();
  };

  const startHitStop = (len: number) => {
    if (inStop(st.s)) return; // No stacking.
    // The finale blast's bands never wait for a hit-stop.
    if (blastS !== undefined && st.s >= blastS - 1e-6) return;
    // A swing already armed strikes on time: the stop ends a sample before its strike.
    let end = st.s + Math.min(len, HITSTOP_MAX);
    for (const sw of st.swings) if (!sw.struck && sw.sStrike > st.s) end = Math.min(end, sw.sStrike - 1 / HERO_RATE);
    if (end - st.s < HITSTOP_MIN) return;
    st.stopStart = st.s;
    st.stopEnd = end;
    rec().hitStop(st.stopStart, st.stopEnd);
    st.q.push(st.stopEnd, { kind: "hitstopEnd" });
  };

  // ---------------------------------------------------------------- enemies
  const weightOf = (def: EnemyDef, formation = false) =>
    def.size === "boss" ? hero.aimWeights.boss : formation ? hero.aimWeights.formation : def.size === "elite" ? hero.aimWeights.elite : hero.aimWeights.regular;
  const spawnFoe = (type: string, x: number, y: number, health?: number, extra?: { entrance?: "land"; attacks?: []; formation?: number }): Foe => {
    const def = lib.enemies.get(type)!;
    const id = st.nextId++;
    const h0 = health ?? def.health;
    const f: Foe = {
      id,
      type,
      def,
      r: def.radius,
      health: h0,
      health0: h0,
      x,
      y,
      vx: 0,
      vy: 0,
      lastW: st.w,
      alive: true,
      finalSample: false,
      deathW: Infinity,
      sx: 0,
      sy: 0,
      knockX: 0,
      knockY: 0,
      knockT: 0,
      weight: weightOf(def),
      slotUntil: -Infinity,
      chargeUntil: -Infinity,
    };
    if (extra?.formation !== undefined) f.formation = extra.formation;
    // Its first sample is the spawn spot itself, at the last sample time before it appeared.
    rec().spawn({ id, type, health: h0, spawnW: st.w, deathW: Infinity, sampleOffset: 0, sampleCount: 0, sampleW0: Math.max(0, st.nextFoeK - 1) / ENEMY_RATE, fates: [], ...extra });
    rec().sample(id, x, y);
    st.alive.push(f);
    st.foeSamples.push(f);
    return f;
  };

  const spawnPoint = (def: EnemyDef, r: Rng, forwardOnly: boolean): { x: number; y: number } => {
    const view = st.camera.view();
    const cx = (view[0] + view[2]) / 2;
    const cy = (view[1] + view[3]) / 2;
    const ext = spriteExtents(def);
    const m = SPAWN_MARGIN + SPAWN_EXTRA;
    const pp = forward();
    const fwd = Math.atan2(pp.ty, pp.tx);
    const ahead = forwardOnly || r.next() < SPAWN_FORWARD;
    const ang = ahead ? fwd + r.range(-Math.PI / 2, Math.PI / 2) : r.range(-Math.PI, Math.PI);
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
    return { x: cx + c * t, y: cy + sn * t };
  };

  const kill = (f: Foe) => {
    f.alive = false;
    f.finalSample = true;
    f.deathW = st.w;
    rec().death(f.id, st.w, false);
  };

  /** An enemy's position at world time atW (at or after its last fixed step). */
  const posX = (f: Foe, atW: number) => f.x + f.vx * (atW - f.lastW);
  const posY = (f: Foe, atW: number) => f.y + f.vy * (atW - f.lastW);

  /** Can this enemy be hit now? (The boss is out of reach while it drops in.) */
  const hittable = (f: Foe) => f.alive && (f.landS === undefined || st.s >= f.landS - 1e-9);

  /** Deal damage to one enemy from a hit; records the fate. */
  const damage = (f: Foe, h: Hit) => {
    if (!hittable(f)) return;
    f.health -= h.damage;
    const lethal = f.health <= 1e-9;
    const fate: Fate = { w: st.w, x: posX(f, st.w), y: posY(f, st.w), hit: h.id, lethal, damage: h.damage };
    f.lastFate = fate;
    rec().fate(f.id, fate);
    if (f === st.bossFoe) rec().bossHealth(st.s, Math.max(0, f.health) / f.health0);
    if (lethal) {
      kill(f);
      onDeath(f, fate, h);
    } else if (!(f.knockT > 0 && (Math.hypot(f.knockX, f.knockY) * f.knockT) / KNOCK_TIME > KNOCKBACK)) {
      // A white flash and a knockback away from the hero. (Upgrade orbs: not cutting short a level-up
      // pulse's longer push still under way.)
      const hp = heroPose(st.w);
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

  const addHit = (h: Omit<Hit, "id" | "intensity" | "s" | "w" | "section" | "energy">, energy = energyAt(song, st.s)): Hit => {
    const section = sectionIndexAt(ctx, st.s);
    // The might passive: the hero's hits deal more (the finale blast kills anything anyway).
    const damage = st.bonus.damage > 0 && h.source !== "ring" ? h.damage * (1 + st.bonus.damage) : h.damage;
    const hit: Hit = { ...h, damage, id: st.nHits++, s: st.s, w: st.w, section, energy, intensity: intensityOf(section, energy, h.weight, h.accent) };
    rec().hit(hit);
    return hit;
  };

  /** Everyone alive inside a shape takes the hit. Returns who was hit. */
  const applyHit = (h: Hit, filter?: (f: Foe) => boolean): Foe[] => {
    const out: Foe[] = [];
    for (const f of st.alive) {
      if (!hittable(f) || (filter && !filter(f))) continue;
      if (catches(h.shape, posX(f, st.w), posY(f, st.w), f.r)) {
        damage(f, h);
        out.push(f);
      }
    }
    st.alive = st.alive.filter((f) => f.alive);
    return out;
  };

  // ---------------------------------------------------------------- swings
  /** Arm a swing for a slot (one swing per slot): the press's, or an automatic one. */
  const swingFor = (slot: GridSlot | undefined, sArmed: number): Swing | undefined => {
    if (!slot || st.takenSlots.has(slot)) return undefined;
    // The finale blast ends the sword's routine (its chord is matched to the grid within 40 ms).
    if (blastS !== undefined && slot.s >= blastS - 0.04) return undefined;
    st.takenSlots.add(slot);
    const sec = ctx.sections[slot.section];
    // A swing striking on an eligible accent is a heavy cleave (everyone's); free movement only swings light.
    const rule = sec.level.heavyOnAccents;
    const onAccent = slot.accent > 0 && slot.accent >= rule.minStrength && (rule.label === undefined || rule.label === slot.accentLabel);
    const heavy = slot.kind === "grid" && onAccent;
    const anim = heavy ? hero.swings.heavy.animation : hero.swings.light.animations[st.lightCount++ % hero.swings.light.animations.length];
    const info = anims[anim] ?? { frames: 6, impactFrame: 3 };
    const sWind = Math.max(sArmed, slot.s - hero.swings.windupMs / 1000 / Math.max(0.05, baseRate(slot.s)));
    const sw: Swing = { sArmed, sWind, sStrike: slot.s, slot, heavy, anim, impact: info.impactFrame ?? 3, frames: info.frames, accent: slot.accent, dir: st.facingAngle, aimed: false, struck: false, wStrike: 0, fps: 16 };
    // Only swings from the last few seconds still matter (the hit-stop and the animation look back).
    if (st.swings.length > 64) st.swings = st.swings.filter((x) => x.sStrike >= st.s - 8);
    st.swings.push(sw);
    st.liveSwings.push(sw);
    st.q.push(sWind, { kind: "wind", sw });
    st.q.push(slot.s, { kind: "strike", sw });
    const amt = (heavy ? 0.035 : 0.014) * sec.level.intensity;
    st.punches.push({ s: slot.s, amt });
    return sw;
  };

  const aimTargets = (x: number, y: number, reach: number): AimTarget[] => {
    const out: AimTarget[] = [];
    for (const f of st.alive) {
      if (!hittable(f)) continue;
      const fx = posX(f, st.w);
      const fy = posY(f, st.w);
      if (Math.abs(fx - x) > 2 * reach + f.r || Math.abs(fy - y) > 2 * reach + f.r) continue;
      if (Math.hypot(fx - x, fy - y) > 2 * reach + f.r) continue;
      out.push({ x: fx, y: fy, r: f.r, weight: f.weight });
    }
    return out;
  };

  /** A light swing's arc at a tier. */
  const tierReach = (tier: number) => {
    const arc = swingArcAt(hero, tier);
    return { radius: arc.radius, halfAngle: (arc.degrees * Math.PI) / 360 };
  };

  /** The heavy impact's reach at a tier (press cleaves): its arc, a full spin at 360°. */
  const heavyTierReach = (tier: number) => {
    const arc = heavyArcAt(hero, tier);
    return { radius: arc.radius, halfAngle: (arc.degrees * Math.PI) / 360 };
  };
  /** Where an accent's heavy swing aims (press cleaves): out to its shockwave's reach at the tier and on along a charging line. */
  const accentAimAt = (tier: number) => ({ radius: waveReachAt(hero, tier) + CLEAVE_AIM_AHEAD, halfAngle: heavyTierReach(tier).halfAngle });

  /**
   * Press cleaves: launch an accent's shockwave, known at song time `known` (its press, or the strike
   * when the press came first). Its first band is on the eighth note after the accent, or when known
   * less than NEVER_LATE before that, on the first sixteenth at least that long after; then one band
   * per sixteenth, 25 px further each, out to the tier's reach.
   */
  const launchCleave = (c: PendingCleave, known: number, press: number) => {
    const wd = hero.swings.heavy.wave;
    const reach = heavyTierReach(c.tier);
    let bi0 = sbiAt(c.s) + 0.5;
    while (timeOfBeatIndex(song, bi0) - known < NEVER_LATE - 1e-9) bi0 += 0.25;
    // (Enough bands to reach it: the last one stops at the reach.)
    const to = waveReachAt(hero, c.tier);
    const bands = Math.ceil((to - reach.radius) / wd.band - 1e-9);
    const wave: Wave = { x: c.x, y: c.y, dir: c.dir, band: 0, hit: new Set(), group: st.nWaves++, mult: tierDamage(hero, c.tier), inner: reach.radius, halfAngle: reach.halfAngle, to };
    rec().wave({ s: c.s, press, band0: timeOfBeatIndex(song, bi0) });
    for (let k = 0; k < bands; k++) st.q.push(timeOfBeatIndex(song, bi0 + k * 0.25), { kind: "wave", wave: { ...wave, band: k } });
  };

  const doWind = (sw: Swing) => {
    const hp = heroPose(st.w);
    // The tier when the wind-up starts sizes the swing, whatever comes after.
    sw.tier = st.tier;
    const reach = sw.heavy ? accentAimAt(sw.tier) : tierReach(sw.tier);
    const targets = aimTargets(hp.x, hp.y, reach.radius);
    // The snapshot is aim's whole input, so the check can re-run it.
    const snap: AimSnapshot = { x: hp.x, y: hp.y, facing: st.facingAngle, targets: [] };
    for (const t of targets) snap.targets.push(t.x, t.y, t.r, t.weight);
    sw.dir = aim(hp.x, hp.y, st.facingAngle, targets, reach);
    sw.aimed = true;
    st.facingAngle = sw.dir;
    sw.record = { s: sw.sStrike, w: 0, sArmed: sw.sArmed, sWind: sw.sWind, kind: sw.heavy ? "heavy" : "light", anim: sw.anim, impactFrame: sw.impact, dir: sw.dir, x: 0, y: 0, hit: -1, accent: sw.accent, aim: snap };
    sw.record.tier = sw.tier;
    sw.record.reach = sw.heavy ? { radius: reach.radius, degrees: (reach.halfAngle * 360) / Math.PI } : swingArcAt(hero, sw.tier);
    const windW = Math.max(1 / 240, (sw.sStrike - sw.sWind) * baseRate(sw.sStrike));
    sw.fps = sw.impact / windW;
  };

  const doStrike = (sw: Swing) => {
    if (!sw.aimed) doWind(sw);
    const hp = heroPose(st.w);
    const def = sw.heavy ? hero.swings.heavy : hero.swings.light;
    const tier = sw.tier!;
    const reach = sw.heavy ? heavyTierReach(tier) : tierReach(tier);
    const shape: Shape = swingArc(hp.x, hp.y, sw.dir, reach);
    const mult = tierDamage(hero, tier);
    const h = addHit({ source: "move", what: sw.heavy ? "cleave" : "slash", weight: sw.heavy ? "heavy" : "light", primary: true, shape, dir: sw.dir, accent: sw.accent, damage: def.damage * mult });
    applyHit(h);
    sw.struck = true;
    sw.wStrike = st.w;
    const r = sw.record!;
    r.w = st.w;
    r.x = hp.x;
    r.y = hp.y;
    r.hit = h.id;
    rec().swing(r);
    sw.record = undefined;
    if (sw.heavy) {
      // Press cleaves: the heavy impact is everyone's; the accent's counted press of Great or better
      // launches its shockwave, now when it came first (with a hit-stop), or when it comes.
      const gi = nearestIndex(sch.gridTimes, sw.sStrike);
      const c: PendingCleave = { gi, s: sw.sStrike, x: hp.x, y: hp.y, dir: sw.dir, tier };
      const counted = st.memo.counted.get(gi);
      if (counted && counted.s <= st.s + 1e-9) {
        if (counted.rating === "perfect" || counted.rating === "great") {
          startHitStop(Math.min(0.15, (hero.swings.heavy.hitStopMs / 1000) * (0.6 + 0.4 * sw.accent)));
          launchCleave(c, counted.s, counted.s);
        }
        st.pendingCleave = undefined;
      } else st.pendingCleave = c;
    }
  };

  const waveBand = (wave: Wave) => {
    // The wave rests during a hit-stop, like the weapons: the next band covers this one's ground.
    if (inStop(st.s) && st.s > st.stopStart + 1e-9) return;
    const wd = hero.swings.heavy.wave;
    const inner = wave.inner;
    const outer = Math.min(inner + wd.band * (wave.band + 1), wave.to);
    const shape: Shape = { kind: "arc", x: wave.x, y: wave.y, radius: outer, inner, dir: wave.dir, halfAngle: wave.halfAngle };
    // Everyone the front has reached and the wave has not hit yet.
    const h = addHit({ source: "wave", what: "shockwave", weight: "medium", primary: false, shape, dir: wave.dir, accent: 0, damage: wd.damage * wave.mult, group: wave.group });
    rec().waveHit(wave.group, h.id);
    for (const f of applyHit(h, (f) => !wave.hit.has(f))) wave.hit.add(f);
  };

  // ---------------------------------------------------------------- input
  /** A stride's record on its press: its length, the budget left, and the ground an interrupted stride gave back. */
  const strideTrace = (r: { stride: number; returned: number }, input: PlanInput, bend = false): Pick<PressTrace, "stride" | "budget" | "returned"> => {
    if (!bend) input.stride = r.stride;
    return { stride: r.stride, budget: budgetAt(rule, st.strides, st.w, oneStride(st.s)), returned: r.returned };
  };
  /**
   * Press classes: the groove after a press's change (a press that adds to it starts the drain's
   * bar again), and the tier it sets.
   */
  const meterChange = (change: number) => {
    const before = st.groove;
    st.groove = grooveAfterChange(st.groove, change);
    if (change > 0) st.lastRaiseS = st.s;
    if (st.groove !== before) rec().groove(st.s, st.groove);
    setTier();
  };
  /** The tier for the groove now; a change sounds its chime on the next grid time. */
  const setTier = () => {
    const t = tierFor(hero, st.tier, st.groove);
    if (t === st.tier) return;
    st.tier = t;
    rec().tier(st.s, st.w, t, sch.gridTimes[firstAtOrAfter(sch.gridTimes, st.s + 1e-6)] ?? st.s);
  };
  /** A press's class in its trace, with the tier after it. */
  // (Its grid fields are the nearest grid time's, as for any press; an off-beat's own offset is offbeatDelta.)
  const judgedTrace = (j: PressJudgement | undefined): Partial<PressTrace> => {
    if (!j) return {};
    const gi = nearestIndex(sch.gridTimes, st.s);
    const near = { gridIndex: gi, gridS: sch.gridTimes[gi], delta: st.s - sch.gridTimes[gi] };
    if (j.class === "offbeat") return { class: j.class, tier: st.tier, ...near, offbeatDelta: j.delta, offbeat: j.rating };
    return { class: j.class, tier: st.tier, ...(j.class !== "partner" ? near : {}), ...(j.rating ? { rating: j.rating } : {}) };
  };

  /** Strides: what a press does to the hero: a stride toward the held keys, or a stomp for Space. */
  const pressMove = (key: InputKey, input: PlanInput): Partial<PressTrace> => (key === "space" ? stompHere(input) : strideTrace(strideOff(key), input));

  const press = (e: KeyEvent) => {
    const sec = pressSectionAt(st.s);
    const steps = sec.level.movement === "steps";
    // Play mode's timing log (a no-op elsewhere): what the press did, and why.
    const trace: PressTrace | undefined = rec().press ? { id: e.id, s: st.s, w: st.w, kind: "press", key: e.key, section: sec.label, movement: steps ? "steps" : "free", outcome: "stride", grooveBefore: st.groove } : undefined;
    // Press classes: every press in step movement is classed against the grid, before it moves the
    // hero (a partner bends or rocks the stride in progress).
    const judged = steps ? classifyPress(hero, sch.gridTimes, st.s, partnerOf(e.key), st.memo) : undefined;
    // Press cleaves: the counted press of an accent whose heavy impact has struck: Great or better
    // launches its shockwave now (a late press brings no hit-stop).
    const pc = st.pendingCleave;
    if (pc && judged?.class === "counted" && judged.gridIndex === pc.gi) {
      if (judged.rating === "perfect" || judged.rating === "great") launchCleave(pc, st.s, st.s);
      st.pendingCleave = undefined;
    }
    if (inStop(st.s)) {
      // A press ends the hit-stop early: world time resumes at the press.
      st.stopEnd = st.s;
      rec().hitStopCut(st.s);
      if (trace) trace.cutHitStop = true;
    }
    const input: PlanInput = { s: st.s, w: st.w, kind: "press", key: e.key, steps };
    st.nInputs++;
    hold(e.key);
    // Free movement: presses are not rated. A press strides first (or bends the stride in
    // progress), and the walk with the held keys starts when it lands.
    if (!steps) {
      const bent = bendOf(e.key);
      const rocked = !bent && rocks(e.key);
      if (bent) {
        st.step = bent;
        input.bend = true;
      } else stopWalk();
      const moved = bent ? strideTrace({ stride: bent.len ?? 0, returned: 0 }, input, true) : rocked ? stompHere(input) : pressMove(e.key, input);
      walkOn();
      rec().input(input);
      if (trace) rec().press!({ ...trace, ...moved, outcome: bent ? "bend" : rocked ? "rock" : e.key === "space" ? "stomp" : "stride" });
      return;
    }
    // (A press just before a pulsed section's first grid time, still in the free section: the walk
    // stops here, as it would on the boundary, and the press strides as in step movement.)
    if (sectionAt(st.s).level.movement !== "steps") stopWalk();
    // A perpendicular key just after the first bends the stride into a diagonal.
    const bent = bendOf(e.key);
    if (bent) {
      st.step = bent;
      input.bend = true;
      const moved = strideTrace({ stride: bent.len ?? 0, returned: 0 }, input, true);
      if (judged) input.class = judged.class;
      rec().input(input);
      if (trace) rec().press!({ ...trace, ...moved, ...judgedTrace(judged), outcome: "bend" });
      return;
    }
    // An opposite key just after the first makes the two one stomp: the first one's stride rocks back.
    if (rocks(e.key)) {
      const moved = stompHere(input);
      if (judged) input.class = judged.class;
      rec().input(input);
      if (trace) rec().press!({ ...trace, ...moved, ...judgedTrace(judged), outcome: "rock" });
      return;
    }
    if (trace && st.step && !st.landed) trace.midStep = true;
    const moved = pressMove(e.key, input);
    if (trace) trace.outcome = e.key === "space" ? "stomp" : "stride";
    // Press classes: the class sets the groove change; only a counted press is rated.
    if (judged && judged.class !== "counted") {
      input.class = judged.class;
      input.extra = true;
      if (judged.class === "offbeat") input.offbeat = judged.rating as "perfect" | "great";
      meterChange(judged.change);
      rec().input(input);
      if (trace) rec().press!({ ...trace, ...moved, ...judgedTrace(judged), outcome: "extra", stepS: st.lastPressS, grooveAfter: st.groove });
      return;
    }
    // Rate the press against the timing grid (the sections' grids join end to end). (Presses arm
    // nothing: the sword swings on every grid time by itself.)
    const rated = ratePressOnGrid(hero, st.s, sch.gridTimes);
    input.rating = rated.rating;
    input.delta = rated.delta;
    st.lastPressS = st.s;
    if (judged) {
      input.class = judged.class;
      meterChange(judged.change);
    }
    const slot = sch.gridSlots[rated.gridIndex];
    rec().input(input);
    if (trace) {
      Object.assign(trace, moved, { gridIndex: rated.gridIndex, gridS: sch.gridTimes[rated.gridIndex], delta: rated.delta, rating: rated.rating, grooveAfter: st.groove, slotS: slot?.s, accent: slot?.accent }, judgedTrace(judged));
      rec().press!(trace);
    }
  };

  const release = (e: KeyEvent) => {
    const sec = sectionAt(st.s);
    const steps = sec.level.movement === "steps";
    st.nInputs++;
    rec().input({ s: st.s, w: st.w, kind: "release", key: e.key, steps });
    rec().press?.({ id: e.id, s: st.s, w: st.w, kind: "release", key: e.key, section: sec.label, movement: steps ? "steps" : "free", outcome: "release" });
    if (e.key !== "space") st.held.delete(e.key);
    // A stride in progress finishes; in free movement the walk follows the keys still held.
    if (!steps) walkOn();
  };

  // ---------------------------------------------------------------- sampling
  const sampleHero = (wk: number, sk: number) => {
    const p = heroPose(wk);
    if (st.lastSample) st.walked += Math.hypot(p.x - st.lastSample.x, p.y - st.lastSample.y);
    st.lastSample = { x: p.x, y: p.y };
    st.heroX.push(p.x);
    st.heroY.push(p.y);
    if (st.heroX.length > 2 * HERO_HISTORY * HERO_RATE) {
      const drop = st.heroX.length - HERO_HISTORY * HERO_RATE;
      st.heroX.splice(0, drop);
      st.heroY.splice(0, drop);
      st.heroBase += drop;
    }
    // The heading: the hero's displacement over the last two beats (kept while it stands still).
    {
      const bi = Math.max(0, Math.min(song.beats.length - 2, Math.floor(sbiAt(sk))));
      const span = HEADING_BEATS * (song.beats[bi + 1] - song.beats[bi]) * Math.max(0.05, baseRate(sk));
      const last = st.heroX.length - 1;
      const back = Math.max(0, last - Math.round(span * HERO_RATE));
      const dx = st.heroX[last] - st.heroX[back];
      const dy = st.heroY[last] - st.heroY[back];
      const d = Math.hypot(dx, dy);
      if (d >= HEADING_MIN) st.heading = { x: dx / d, y: dy / d };
    }
    // Animation: a swing (wind-up, impact, follow-through), a step's jump, or idle.
    // A swing winding up takes precedence over one following through.
    let winding: Swing | undefined;
    let following: Swing | undefined;
    for (const c of st.liveSwings) {
      if (c.sWind > sk + 1e-9) continue;
      if (!c.struck) {
        if (!winding || c.sStrike < winding.sStrike) winding = c;
      } else if (c.impact + (wk - c.wStrike) * c.fps < c.frames && (!following || c.sStrike > following.sStrike)) following = c;
    }
    // The sample nearest a strike always shows its impact frame, even when the next swing is winding up.
    const sw = following && wk - following.wStrike <= 0.5 / HERO_RATE + 1e-9 ? following : (winding ?? following);
    const half = 0.5 / HERO_RATE;
    let anim: number;
    let frame: number;
    let facing: number;
    if (sw) {
      let fr: number;
      if (!sw.struck) {
        // Across the rate steps to the strike: a ramp (such as a timeScale event's) changes the rate on the way.
        const toImpactW = Math.max(1e-6 * (sw.sStrike - sk), stepSpan(sch.baseSteps, sk, sw.sStrike));
        if (toImpactW <= half + 1e-9) fr = sw.impact;
        else fr = clamp(Math.floor((sw.impact * (sk - sw.sWind)) / Math.max(1e-6, sw.sStrike - sw.sWind)), 0, sw.impact - 1);
      } else {
        const since = wk - sw.wStrike;
        fr = since <= half + 1e-9 ? sw.impact : clamp(sw.impact + Math.floor(since * sw.fps + 1e-6), sw.impact, sw.frames - 1);
      }
      anim = animIdx(sw.anim);
      frame = fr;
      facing = facingIndex(sw.aimed ? sw.dir : st.facingAngle);
    } else if (p.u < 1) {
      anim = animIdx("jump");
      frame = clamp(Math.floor(p.u * 5), 0, 4);
      facing = facingIndex(st.facingAngle);
    } else if (Math.hypot(p.vx, p.vy) > 1) {
      // Walking: the cycle advances by distance, facing the way the hero walks.
      anim = animIdx("walk");
      frame = walkFrame(hero, st.walked, anims.walk?.frames ?? 8);
      facing = facingIndex(Math.atan2(p.vy, p.vx));
    } else {
      anim = animIdx("idle");
      frame = Math.floor(frac(sbiAt(sk) / 2) * 2) % 2;
      facing = facingIndex(st.facingAngle);
    }
    rec().hero(p.x, p.y, p.z, facing, anim, frame);
    // The camera follows the hero with the section's lead along the route.
    const sec = sectionAt(sk);
    const f = leadDir(sk);
    const lead = sec.level.camera.lead;
    let zoom = sec.level.camera.zoom;
    if (sec.index > 0) {
      const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
      const u = smoothstep((sbiAt(sk) - sec.b0) / bpb);
      zoom = lerp(ctx.sections[sec.index - 1].level.camera.zoom, zoom, u);
    }
    const punches = st.punches;
    for (let i = punches.length - 1; i >= 0; i--) {
      let pu = punches[i];
      if (pu.w === undefined && sk >= pu.s - 1e-9) punches[i] = pu = Object.freeze({ s: pu.s, amt: pu.amt, w: worldOf(pu.s) });
      // Before the impact the punch leads in by song time; after it, it decays in world time.
      const age = pu.w !== undefined ? wk - pu.w : sk - pu.s;
      if (age > 1.2 || age < -0.05) continue;
      const env = age < 0 ? smoothstep((age + 0.05) / 0.05) : Math.exp(-age / 0.22);
      zoom *= 1 + pu.amt * env;
    }
    st.camera.sample(p.x + f.tx * lead, p.y + f.ty * lead - 12, zoom);
    rec().camera(st.camera.x, st.camera.y, zoom);
    // Retire swings whose follow-through has ended.
    for (let i = st.liveSwings.length - 1; i >= 0; i--) {
      const c = st.liveSwings[i];
      if (c.struck && c.impact + (wk - c.wStrike) * c.fps >= c.frames) st.liveSwings.splice(i, 1);
    }
  };

  const recordFoes = (wk: number) => {
    const list = st.foeSamples;
    for (let i = list.length - 1; i >= 0; i--) {
      const f = list[i];
      if (f.finalSample) {
        const last = f.lastFate;
        if (last && last.lethal) rec().sample(f.id, last.x, last.y);
        else rec().sample(f.id, f.x, f.y);
        list.splice(i, 1);
        continue;
      }
      rec().sample(f.id, posX(f, wk), posY(f, wk));
    }
  };

  // ---------------------------------------------------------------- motion
  const groupPhase = (group: string, sbi: number, barStart: number, bpb: number): number => {
    const period = group === "eighth" ? 0.5 : group === "beat" ? 1 : group === "bar" ? bpb : 2 * bpb;
    const ph = ((sbi - barStart) / period) % 1;
    return ph < 0 ? ph + 1 : ph;
  };

  /** Integrate every enemy to world time w1 (a fixed step). */
  const moveFoes = (w1: number) => {
    const hp = heroPose(w1);
    const s = st.s;
    const sec = sectionAt(s);
    const free = sec.song.feel === "free";
    // A calm section slows the enemies (not the hero) and curls their approach into a lazy spiral.
    const calmSpeed = 1 - sec.level.calm;
    const curlCos = Math.cos(sec.level.calm * CALM_CURL);
    const curlSin = Math.sin(sec.level.calm * CALM_CURL);
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
    const alive = st.alive;
    const health = st.health > 0;
    for (const f of alive) {
      // Bring everyone to this step's time first. Formation members follow their slots
      // (and the boss holds still while it drops in).
      const dt = w1 - f.lastW;
      if (f.at && s < f.slotUntil) {
        // A closing ring's member: on its circle, closing in song time.
        const p = f.at(s);
        const rate = rateAt(s);
        const q2 = f.at(s + 1 / SIM_RATE);
        f.vx = rate > 0 ? ((q2.x - p.x) * SIM_RATE) / rate : 0;
        f.vy = rate > 0 ? ((q2.y - p.y) * SIM_RATE) / rate : 0;
        f.x = p.x;
        f.y = p.y;
        continue;
      }
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
      st.gen++;
      const gen = st.gen;
      const head = st.head;
      const stamp = st.stamp;
      const gox = Math.floor(hp.x / CELL) - G / 2;
      const goy = Math.floor(hp.y / CELL) - G / 2;
      if (st.nextArr.length < alive.length) st.nextArr = new Int32Array(alive.length * 2);
      const nextArr = st.nextArr;
      for (let i = 0; i < alive.length; i++) {
        const f = alive[i];
        f.sx = 0;
        f.sy = 0;
        const ix = Math.floor(f.x / CELL) - gox;
        const iy = Math.floor(f.y / CELL) - goy;
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
        const ix = Math.floor(a.x / CELL) - gox;
        const iy = Math.floor(a.y / CELL) - goy;
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
              // Formation members, the landing boss and a stampede's runners hold their places or
              // course: the crowd gives way.
              const pa = pinned(a) || a.run !== undefined;
              const pb = pinned(b) || b.run !== undefined;
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
      // Set pieces keep their course (a ring's circle, a stampede's run), whatever the hero does, and
      // bite whoever they run into.
      if ((f.at && s < f.slotUntil) || f.run) {
        if (f.run) {
          f.vx = f.run.vx;
          f.vy = f.run.vy;
        }
        const ed = Math.hypot(f.x - hp.x, f.y - hp.y);
        if (health && f.def.contactDamage > 0 && ed <= HURTBOX_RADIUS + f.r + CONTACT_GAP + BITE_SLACK && st.w >= st.invulnUntil - 1e-9) {
          hurt(armouredBite(f.def.contactDamage, st.bonus.armour), "bite", f.type);
        }
        continue;
      }
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
        const sp = f.def.speed * calmSpeed * (mults[f.def.group] ?? 1) * clamp(tl / 20, 0, 1);
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
      const m = (mults[f.def.group] ?? 1) * calmSpeed;
      const charge = s < f.chargeUntil ? CHARGE_SPEED : 1;
      // Archers keep their distance; everyone else closes to contact.
      const speed =
        f.def.behavior === "archer" ? f.def.speed * m * clamp((d - ARCHER_RANGE) / 30, -0.5, 1) : f.def.speed * m * charge * clamp((d - contact) / 16, 0, 1);
      // The curl turns half the enemies clockwise and half counterclockwise.
      const cs = f.id % 2 === 0 ? curlSin : -curlSin;
      const tvx = ((dx * curlCos - dy * cs) / d) * speed + f.sx;
      const tvy = ((dy * curlCos + dx * cs) / d) * speed + f.sy;
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
      // An enemy at contact bites whenever the hero isn't invulnerable.
      if (health && f.def.contactDamage > 0 && Math.max(ed, minD) <= minD + CONTACT_GAP + BITE_SLACK && st.w >= st.invulnUntil - 1e-9) {
        hurt(armouredBite(f.def.contactDamage, st.bonus.armour), "bite", f.type);
      }
    }
  };

  /**
   * Move the clock to song time t. World time comes from the warp breaks, and the
   * hero and enemy samples are taken at their exact times from the state so
   * far, so how events split the time between fixed steps changes nothing.
   */
  const moveClock = (t: number, fixed: boolean, between = false) => {
    // The rate of the interval from the latest stop of the loop's own: a clock stop in between
    // (a live frame) must not move a rate change, or the recording would depend on the frames.
    const rate = rateAt(st.rateS);
    pushBreak(rate);
    const b = st.breaks[st.breaks.length - 1];
    const w1 = b.w + (t - b.s) * b.rate;
    if (w1 > st.w) {
      while (st.nextHeroK / HERO_RATE <= w1 + 1e-12) {
        const wk = st.nextHeroK / HERO_RATE;
        sampleHero(wk, b.s + (wk - b.w) / rate);
        st.nextHeroK++;
      }
      // Step landings (the dust puff), after the samples up to them: a live recording sounds them
      // with the camera sample before, whatever frames the clock stopped at on the way.
      if (st.step && !st.landed && st.step.w0 + stepDur <= w1 + 1e-12) {
        const wl = st.step.w0 + stepDur;
        const end = stepEnd(st.step);
        rec().landing({ s: b.s + (wl - b.w) / rate, w: wl, x: end.x, y: end.y });
        st.landed = true;
      }
      while (st.nextFoeK / ENEMY_RATE <= w1 + 1e-12) {
        recordFoes(st.nextFoeK / ENEMY_RATE);
        st.nextFoeK++;
      }
    }
    st.s = t;
    st.w = w1;
    if (!between) st.rateS = t;
    if (fixed && st.w > st.lastFixedW) {
      moveFoes(st.w);
      collectGems();
      touchOrbs();
      touchChickens();
      st.lastFixedW = st.w;
    }
    rec().clock?.(st.s, st.w, between);
  };

  // ---------------------------------------------------------------- events
  /** Threats the player can see: each only after it has been visible for 150 ms. */
  const visibleThreats = (hp: { x: number; y: number }): Observation["threats"] => {
    const out: Observation["threats"] = [];
    const s = st.s;
    // Archers drawing (the arrow will fly at the hero's position at the release)...
    for (const v of sch.volleys) {
      if (s < v.sDraw + REACTION - 1e-9 || s > v.sRelease + 1e-9) continue;
      for (const f of st.alive) {
        if (!f.alive || f.archerGroup !== v.group || !f.drawing) continue;
        out.push({ kind: "arrow", x: posX(f, st.w), y: posY(f, st.w) - 24, ax: hp.x, ay: hp.y - HURTBOX_LIFT, landsAt: v.sArrive, seenAt: v.sDraw });
      }
    }
    // ...arrows in flight...
    for (const a of st.flying) {
      if (s < sch.volleys[a.v].sDraw + REACTION - 1e-9) continue;
      out.push({ kind: "arrow", x: a.x0, y: a.y0, ax: a.ax, ay: a.ay, landsAt: sch.volleys[a.v].sArrive, seenAt: sch.volleys[a.v].sDraw });
    }
    // ...and slam markers.
    for (const [k, m] of st.slamMarks) if (s >= m.sMark + REACTION - 1e-9) out.push({ kind: "slam", x: m.x, y: m.y, radius: SLAM_RADIUS + HURTBOX_RADIUS, landsAt: sch.boss!.slams[k].sLand, seenAt: m.sMark });
    return out;
  };

  /** The middle of the formation members still in their slots, if any. */
  const dancingFormation = (): { x: number; y: number } | undefined => {
    let x = 0;
    let y = 0;
    let n = 0;
    for (const f of st.alive) {
      if (!f.alive || !f.slot || st.s >= f.slotUntil) continue;
      x += posX(f, st.w);
      y += posY(f, st.w);
      n++;
    }
    return n > 0 ? { x: x / n, y: y / n } : undefined;
  };

  /** Set pieces as the player sees them: the latest ring still closing, and stampede runners within 300 px. */
  const setPieceView = (hp: { x: number; y: number }): Pick<Observation, "ring" | "runners"> => {
    const out: Pick<Observation, "ring" | "runners"> = {};
    const ring = st.rings.findLast((r) => st.s >= r.s0 && st.s < r.s1);
    if (ring) out.ring = { x: ring.x, y: ring.y, r: lerp(ring.r0, RING_CLOSE, (st.s - ring.s0) / (ring.s1 - ring.s0)), gaps: ring.gaps };
    const runners = st.alive.filter((f) => f.alive && f.run && Math.hypot(posX(f, st.w) - hp.x, posY(f, st.w) - hp.y) < 300);
    if (runners.length > 0) out.runners = runners.map((f) => ({ x: posX(f, st.w), y: posY(f, st.w), vx: f.run!.vx, vy: f.run!.vy }));
    return out;
  };

  /** Gems on the field as the player sees them: those within GEM_SEEN of the pickup radius, and the nearest vacuum. */
  const gemView = (hp: { x: number; y: number }): Pick<Observation, "gems" | "vacuum"> => {
    const reach = pickupRadius() + GEM_SEEN;
    const gems = st.field.filter((g) => Math.hypot(g.x - hp.x, g.y - hp.y) <= reach).map((g) => ({ x: g.x, y: g.y, value: g.value }));
    let vacuum: { x: number; y: number } | undefined;
    // (A vacuum the player sees: within VACUUM_SEEN of the hero; one left far behind no longer draws it.)
    for (const v of st.vacuums) if (Math.hypot(v.x - hp.x, v.y - hp.y) <= VACUUM_SEEN && (!vacuum || Math.hypot(v.x - hp.x, v.y - hp.y) < Math.hypot(vacuum.x - hp.x, vacuum.y - hp.y))) vacuum = { x: v.x, y: v.y };
    return { ...(gems.length ? { gems } : {}), ...(vacuum ? { vacuum } : {}) };
  };

  const observe = (): Observation => {
    const hp = heroPose(st.w);
    const pp = forward();
    return {
      s: st.s,
      w: st.w,
      hero: { x: hp.x, y: hp.y, stepping: hp.u < 1, stride: strideNow(hp), tier: st.tier, pickup: pickupRadius(), health: st.health / healthMax() },
      enemies: st.alive.filter((f) => f.alive).map((f) => ({ x: posX(f, st.w), y: posY(f, st.w), type: f.type, health: f.health })),
      threats: visibleThreats(hp),
      boss: st.bossFoe && st.bossFoe.alive && !pinned(st.bossFoe) ? { x: posX(st.bossFoe, st.w), y: posY(st.bossFoe, st.w) } : undefined,
      formation: dancingFormation(),
      ...setPieceView(hp),
      ...gemView(hp),
      ...(st.orbs ? { orbs: st.orbs.pts.map((p, i) => ({ x: p.x, y: p.y, item: st.orbs!.offers[i] })) } : {}),
      ...(st.chickens.length ? { chickens: st.chickens.filter((c) => Math.hypot(c.x - hp.x, c.y - hp.y) <= VACUUM_SEEN).map((c) => ({ x: c.x, y: c.y })) } : {}),
      ...(st.level < sch.levelUps.length && !st.overdue && st.xpNow >= levelReq() && sch.levelUps[st.level].s >= st.s ? { levelUpAt: sch.levelUps[st.level].s } : {}),
      pacePoint: { x: pp.x, y: pp.y, tx: pp.tx, ty: pp.ty },
    };
  };

  const scheduleDecision = (after: number) => {
    if (!io.player) return;
    const t = io.player.nextDecision(after);
    if (t <= cutS && t < endS) st.q.push(Math.max(t, after), { kind: "decide" });
  };

  const spawnDirector = (bi: number) => {
    if (st.blasting) return;
    const s = st.s;
    // (With set pieces the stream is thinner, and set pieces add to it: it counts only its own.)
    const target = targetPopulation(ctx, s) * STREAM_SHARE;
    const all = st.alive.length + st.pending;
    const living = st.alive.filter((f) => f.formation === undefined).length + st.pending;
    const want = Math.min(SPAWN_PER_BEAT, Math.round(target) - living, POP_CAP - all);
    if (want <= 0) return;
    const sec = sectionAt(s);
    const roster = Object.entries(sec.level.roster).filter(([t]) => lib.enemies.get(t)!.behavior === "chase");
    const r = new Rng(ctx.seed, "spawn", Math.round(bi * 4));
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
      st.pending++;
      st.q.push(Math.max(s, sAppear), { kind: "appear", type });
    }
  };

  const appear = (type: string) => {
    st.pending--;
    // (Never past the cap: a set piece may have taken the room since it was queued.)
    if (st.blasting || st.alive.length >= POP_CAP) return;
    const def = lib.enemies.get(type)!;
    const r = new Rng(ctx.seed, "appear", st.nextId);
    const p = spawnPoint(def, r, false);
    spawnFoe(type, p.x, p.y);
  };

  const recycle = () => {
    const view = st.camera.view();
    const vw = view[2] - view[0];
    const hp = heroPose(st.w);
    // Too far away from the hero, in any direction.
    const behind = st.alive.filter((f) => {
      const dx = posX(f, st.w) - hp.x;
      const dy = posY(f, st.w) - hp.y;
      // A stampede's runner, once it has left the view on the side it runs to.
      if (f.alive && f.run) {
        const x = posX(f, st.w);
        const y = posY(f, st.w);
        return (f.run.vx > 0 && x > view[2] + STAMPEDE_ROW) || (f.run.vx < 0 && x < view[0] - STAMPEDE_ROW) || (f.run.vy > 0 && y > view[3] + STAMPEDE_ROW) || (f.run.vy < 0 && y < view[1] - STAMPEDE_ROW);
      }
      return f.alive && Math.hypot(dx, dy) >= RECYCLE_VIEWS * vw;
    });
    for (const f of behind) {
      // Not a kill: it leaves the field and re-enters ahead with its health.
      f.alive = false;
      f.finalSample = true;
      f.deathW = st.w;
      rec().death(f.id, st.w, true);
      const r = new Rng(ctx.seed, "recycle", f.id);
      const p2 = spawnPoint(f.def, r, true);
      spawnFoe(f.type, p2.x, p2.y, f.health);
    }
    st.alive = st.alive.filter((f) => f.alive);
    // A stampede whose runners have all left the view (or died): its vacuum on the next downbeat.
    for (const sp of st.stampedes) {
      if (sp.done || st.alive.some((f) => f.formation === sp.fi && f.run)) continue;
      sp.done = true;
      st.q.push(downbeatAtOrAfter(st.s + 1e-6), { kind: "vacuumDrop" });
    }
  };

  const grantWeapon = (id: string) => {
    rec().weapon(id, st.s);
    const wd = lib.weapons.get(id)!;
    if (wd.kind === "blast") return;
    scheduleFires(wd, wd.fires.every);
  };

  /**
   * A weapon fires on every slot of a subdivision after now, up to the finale blast; an evolution's
   * faster subdivision skips the slots the weapon's own already fires on.
   */
  const scheduleFires = (wd: WeaponDef, every: WeaponDef["fires"]["every"], skip?: WeaponDef["fires"]["every"]) => {
    const end = blastS ?? ctx.duration;
    const startBar = Math.max(1, barOfBeatIndex(ctx.idx, sbiAt(st.s)));
    for (let bar = startBar; bar <= ctx.idx.barCount; bar++) {
      const bpb = ctx.idx.beatsPerBar[bar];
      const stepB = subdivisionBeats(every, bpb);
      const skipB = skip ? subdivisionBeats(skip, bpb) : undefined;
      for (let off = 0; off < bpb - 1e-9; off += stepB) {
        if (skipB !== undefined && Math.abs(off / skipB - Math.round(off / skipB)) < 1e-6) continue;
        const bi = ctx.idx.barStart[bar] + off;
        if (bi > song.beats.length - 1) continue;
        const t = timeOfBeatIndex(song, bi);
        // The weapon starts on the slot after its level-up.
        if (t <= st.s + 0.01 || t >= end - 0.01 || t > endS) continue;
        st.q.push(t, { kind: "fire", weapon: wd, bi });
      }
    }
  };

  const fire = (wd: WeaponDef, bi: number) => {
    // Weapons rest during a hit-stop: the next slot of their subdivision fires instead.
    if (inStop(st.s)) return;
    rec().fire(wd.id, st.s);
    const hp = heroPose(st.w);
    const energy = energyAt(song, st.s);
    // The tier when it fires sets its area and damage.
    const mult = tierDamage(hero, st.tier);
    if (wd.kind === "orbit" && wd.orbit) {
      const bar = Math.max(1, barOfBeatIndex(ctx.idx, bi));
      const bpb = ctx.idx.beatsPerBar[bar] ?? 2;
      const th0 = (TAU * bi) / (wd.orbit.periodBars * bpb);
      const orbit = evolvedOrbit(wd, orbitAt(wd, st.tier), st.loadout.evolved.includes(wd.id));
      for (let k = 0; k < orbit.count; k++) {
        const th = th0 + (k * TAU) / orbit.count;
        const x = hp.x + Math.cos(th) * orbit.radius;
        const y = hp.y + Math.sin(th) * orbit.radius * 0.8;
        const h = addHit({ source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x, y, radius: wd.hitShape!.radius }, dir: th + Math.PI / 2, accent: 0, damage: wd.damage * mult }, energy);
        applyHit(h);
      }
    } else if (wd.kind === "chain" && wd.chain) {
      const tiered = chainAt(wd, st.tier);
      // An evolved chain jumps further.
      const more = st.loadout.evolved.includes(wd.id) ? (wd.evolve?.jumps ?? 0) : 0;
      const jumps = (tiered ? tiered.jumps : Math.round(lerp(wd.chain.jumps[0], wd.chain.jumps[1], energy))) + more;
      const first = { w: st.w, x: hp.x, y: hp.y - 24 };
      const chain: ChainState = { last: first, index: st.nChains++, hit: new Set(), jumps, energy, bi, ...(tiered ? { range: tiered.range } : {}), mult };
      rec().chain(first);
      chainJump(wd, chain, 0);
    } else if (wd.kind === "trail" && wd.trail) {
      // Circles along the hero's path over the last beats.
      const bd = (song.beats[Math.min(song.beats.length - 1, Math.floor(bi))] - song.beats[Math.max(0, Math.floor(bi) - 1)]) * baseRate(st.s);
      const wFrom = st.w - wd.trail.beats * Math.max(0.05, bd);
      const hx = st.heroX;
      const hy = st.heroY;
      const base = st.heroBase;
      if (base > 0 && base / HERO_RATE >= wFrom) throw new Error(`the fire trail reaches back past the kept hero samples (w=${st.w.toFixed(2)})`);
      let lastX = Infinity;
      let lastY = Infinity;
      for (let k = base + hx.length - 1; k >= base && k / HERO_RATE >= wFrom; k--) {
        if (k / HERO_RATE > st.w - 0.12) continue;
        const x = hx[k - base];
        const y = hy[k - base];
        if (Math.hypot(x - lastX, y - lastY) < wd.trail.spacing) continue;
        lastX = x;
        lastY = y;
        const h = addHit({ source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x, y, radius: trailRadiusAt(wd, st.tier) }, dir: 0, accent: 0, damage: wd.damage * mult }, energy);
        applyHit(h);
      }
    }
  };

  const chainJump = (wd: WeaponDef, chain: ChainState, j: number) => {
    if (inStop(st.s)) {
      // A jump inside a hit-stop waits for the next sixteenth after it.
      st.q.push(timeOfBeatIndex(song, Math.floor(sbiAt(st.s) * 4 + 1e-6) / 4 + 0.25), { kind: "chain", weapon: wd, chain, jump: j });
      return;
    }
    const from = chain.last;
    const fx = from.x;
    const fy = j === 0 ? from.y + 24 : from.y + 16;
    let best: Foe | undefined;
    let bestD = chain.range ?? wd.chain!.range;
    for (const f of st.alive) {
      if (!f.alive || chain.hit.has(f)) continue;
      const d = Math.hypot(posX(f, st.w) - fx, posY(f, st.w) - fy);
      if (d < bestD) {
        bestD = d;
        best = f;
      }
    }
    if (!best) return;
    chain.hit.add(best);
    const bx = posX(best, st.w);
    const by = posY(best, st.w);
    const p = { w: st.w, x: bx, y: by - 16 };
    chain.last = p;
    rec().chainPoint(chain.index, p);
    const h = addHit({ source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x: bx, y: by, radius: wd.hitShape!.radius }, dir: Math.atan2(by - fy, bx - fx), accent: 0, damage: wd.damage * chain.mult }, chain.energy);
    applyHit(h);
    if (j + 1 < chain.jumps) {
      const stepB = subdivisionBeats(wd.chain!.step, 2);
      st.q.push(timeOfBeatIndex(song, chain.bi + (j + 1) * stepB), { kind: "chain", weapon: wd, chain, jump: j + 1 });
    }
  };

  const blastBand = (band: number) => {
    const wd = lib.weapons.get(sch.blast!.weapon)!;
    const b = wd.blast!;
    const bands = Math.max(1, Math.round(b.beats / subdivisionBeats(b.step, 2)));
    const hp = heroPose(st.w);
    const last = band === bands - 1;
    // The ring's front: everything inside it falls (the last band reaches everyone left).
    const inner = 0;
    const outer = last ? 1e6 : (b.radius * (band + 1)) / bands;
    const h = addHit({ source: "ring", what: wd.id, weight: "heavy", primary: false, shape: { kind: "ring", x: hp.x, y: hp.y, inner, outer }, dir: 0, accent: 0, damage: wd.damage }, 1);
    applyHit(h);
    if (last) {
      // Nothing survives the final band, wherever it stands.
      for (const f of st.alive) if (f.alive) damage(f, h);
      st.alive = st.alive.filter((f) => f.alive);
    }
  };

  // ---------------------------------------------------------------- deaths, gems and level-ups
  /** World time of a song time at or before now, from the warp so far. */
  const worldOf = (t: number) => {
    const breaks = st.breaks;
    for (let i = breaks.length - 1; i >= 0; i--) if (breaks[i].s <= t + 1e-12) return breaks[i].w + (t - breaks[i].s) * breaks[i].rate;
    return 0;
  };
  const downbeatAtOrAfter = (t: number) => {
    let bar = Math.max(1, barOfBeatIndex(ctx.idx, sbiAt(t)));
    while (bar <= ctx.idx.barCount && timeOfBeatIndex(song, ctx.idx.barStart[bar]) < t - 1e-9) bar++;
    return bar <= ctx.idx.barCount ? timeOfBeatIndex(song, ctx.idx.barStart[bar]) : t;
  };
  const levelReq = () => sch.levelUps[Math.min(st.level, sch.levelUps.length - 1)]?.xp ?? 1;
  const pushXp = () => rec().xp(st.s, clamp(st.xpNow / levelReq(), 0, 1));

  const onDeath = (f: Foe, fate: Fate, h: Hit) => {
    // Every death drops gems, which fly to the hero on the next downbeat (a beat after the finale blast).
    const big = f.def.size !== "regular";
    const value = big ? 5 : 1;
    if (!st.blasting) dropOnField(f, fate, value, big);
    else {
      const s1 = st.blasting && blastS !== undefined ? timeOfBeatIndex(song, sbiAt(blastS) + 1) : downbeatAtOrAfter(st.s + 0.12);
      rec().gem({ w0: st.w, x0: fate.x, y0: fate.y, s1, w1: 0, value, big, enemy: f.id });
      st.q.push(s1, { kind: "gem", gem: st.nGems++, value });
    }
    // The small health pool: an orc elite drops a chicken half the time (seeded by the enemy), the boss two.
    if (!st.blasting) {
      const n = f.def.size === "boss" ? CHICKEN_BOSS : f.def.size === "elite" && new Rng(ctx.seed, "chicken", f.id).next() < CHICKEN_CHANCE ? 1 : 0;
      for (let k = 0; k < n; k++) {
        const x = fate.x + (n > 1 ? (k - (n - 1) / 2) * CHICKEN_SPREAD : 0);
        rec().chicken({ s: st.s, w: st.w, x, y: fate.y });
        st.chickens.push({ i: st.nChickens++, x, y: fate.y });
      }
    }
    if (f === st.bossFoe) {
      rec().bossDeath(st.s, h.id);
      // Slams it had marked never land.
      st.slamMarks.clear();
      if (h.source !== "ring") startHitStop(BOSS_KILL_STOP);
    }
  };

  // ------------------------------------------------------- gems on the field
  /** Gems on the field: the pickup radius at the tier now. */
  const pickupRadius = () => {
    const r = hero.pickupRadius ?? [40];
    // The magnet passive widens it.
    return r[Math.max(0, Math.min(r.length - 1, st.tier))] * (1 + st.bonus.pickup);
  };
  /** The first sixteenth note at or after song time t. */
  const sixteenthAtOrAfter = (t: number) => timeOfBeatIndex(song, Math.ceil(sbiAt(t) * 4 - 1e-6) / 4);

  /** A death's gem waits where it dropped; past the cap, its value goes to the nearest gem, which grows. */
  const dropOnField = (f: Foe, fate: Fate, value: number, big: boolean) => {
    if (st.field.length >= GEM_CAP) {
      let near = st.field[0];
      for (const g of st.field) if (Math.hypot(g.x - fate.x, g.y - fate.y) < Math.hypot(near.x - fate.x, near.y - fate.y)) near = g;
      near.value += value;
      near.big = near.big || big || near.value >= 5;
      rec().gemGrow(near.gem, near.value, near.big);
      return;
    }
    rec().gem({ w0: st.w, x0: fate.x, y0: fate.y, s1: Infinity, w1: Infinity, value, big, enemy: f.id });
    st.field.push({ gem: st.nGems++, x: fate.x, y: fate.y, value, big });
  };

  /** A gem takes off for the hero now, collected (its experience counting) at song time s1. */
  const takeOff = (g: StateOf<"field">, s1: number) => {
    rec().gemFly(g.gem, st.w, s1);
    st.q.push(s1, { kind: "gem", gem: g.gem, value: g.value });
  };

  /**
   * On every fixed step: gems inside the pickup radius take off, collected on the first sixteenth at
   * least NEVER_LATE away; a vacuum the hero touches sends every gem in view, landing over one bar.
   */
  const collectGems = () => {
    if (st.blasting) return;
    const hp = heroPose(st.w);
    const r = pickupRadius();
    if (st.field.some((g) => Math.hypot(g.x - hp.x, g.y - hp.y) <= r)) {
      const s1 = sixteenthAtOrAfter(st.s + NEVER_LATE);
      st.field = st.field.filter((g) => {
        if (Math.hypot(g.x - hp.x, g.y - hp.y) > r) return true;
        takeOff(g, s1);
        return false;
      });
    }
    const v = st.vacuums.find((x) => Math.hypot(x.x - hp.x, x.y - hp.y) <= VACUUM_TOUCH);
    if (v) touchVacuum(v, hp);
  };

  const touchVacuum = (v: StateOf<"vacuums">, hp: { x: number; y: number }) => {
    st.vacuums = st.vacuums.filter((x) => x !== v);
    const view = st.camera.view();
    const inView = st.field.filter((g) => g.x >= view[0] && g.x <= view[2] && g.y >= view[1] && g.y <= view[3]).sort((a, b) => Math.hypot(a.x - hp.x, a.y - hp.y) - Math.hypot(b.x - hp.x, b.y - hp.y));
    rec().vacuumTouch(v.i, st.s, st.w, inView.length);
    if (inView.length === 0) return;
    // Over one bar of sixteenths from the first at least NEVER_LATE away, nearest first.
    const bi0 = sbiAt(sixteenthAtOrAfter(st.s + NEVER_LATE));
    const bpb = ctx.idx.beatsPerBar[Math.max(1, barOfBeatIndex(ctx.idx, sbiAt(st.s)))] ?? 2;
    const n = 4 * bpb;
    const taken = new Set(inView);
    inView.forEach((g, k) => takeOff(g, timeOfBeatIndex(song, bi0 + Math.floor((k * n) / inView.length) / 4)));
    st.field = st.field.filter((g) => !taken.has(g));
  };

  /** A vacuum drops on the first downbeat after a set piece ends, VACUUM_AHEAD along the hero's heading. */
  const vacuumDrop = () => {
    const hp = heroPose(st.w);
    const x = hp.x + st.heading.x * VACUUM_AHEAD;
    const y = hp.y + st.heading.y * VACUUM_AHEAD;
    const i = st.nVacuums++;
    rec().vacuum({ s: st.s, w: st.w, x, y });
    st.vacuums.push({ i, x, y });
  };

  const grant = () => {
    if (st.blasting) return;
    const index = st.level;
    st.level++;
    st.xpNow = 0;
    st.overdue = false;
    pushXp();
    // The upgrade pool: three offers from the run's seed and the level-up's index, as orbs. A choice
    // still waiting takes its orb ahead first, and what has been taken but not yet applied counts
    // as owned. The downbeat heals, offers or none.
    if (st.orbs) takeOrb(0, false);
    const owned = structuredClone(st.loadout);
    for (const item of st.applying) takeItem(owned, item);
    const offers = offersFor(ctx.seed, index, lib, ctx.level.upgrades.offer, owned);
    heal(hero.health.levelUpHeal, "level-up");
    if (offers.length > 0) riseOrbs(index, offers);
    else rec().upgrade({ s: st.s, offers, taken: -1 });
  };

  /**
   * Upgrade orbs, on the level-up's downbeat: the pulse pushes nearby enemies out, the hero heals,
   * and the offers rise as orbs around the hero (the first ahead along its heading, then to the left
   * and right).
   */
  const riseOrbs = (u: number, offers: UpgradeItem[]) => {
    const hp = heroPose(st.w);
    for (const f of st.alive) {
      if (!f.alive || f === st.bossFoe || pinned(f)) continue;
      const dx = posX(f, st.w) - hp.x;
      const dy = posY(f, st.w) - hp.y;
      const d = Math.hypot(dx, dy);
      if (d >= LEVEL_PULSE || d < 1e-6) continue;
      f.knockX = (dx / d) * (LEVEL_PULSE - d);
      f.knockY = (dy / d) * (LEVEL_PULSE - d);
      f.knockT = KNOCK_TIME;
    }
    const a0 = Math.atan2(st.heading.y, st.heading.x);
    const pts = offers.map((_, i) => {
      const a = a0 + [0, -ORB_SPREAD, ORB_SPREAD][i];
      return { x: hp.x + Math.cos(a) * ORB_RISE, y: hp.y + Math.sin(a) * ORB_RISE };
    });
    const bar = Math.max(1, barOfBeatIndex(ctx.idx, sbiAt(st.s)));
    const last = bar + ORB_BARS;
    const deadline = last <= ctx.idx.barCount ? timeOfBeatIndex(song, ctx.idx.barStart[last]) : ctx.duration;
    rec().upgrade({ s: st.s, offers, taken: -1, w: st.w, orbs: pts.map((p) => ({ ...p })) });
    st.orbs = { u, offers, pts, deadline };
    st.q.push(deadline, { kind: "orbDefault", u });
  };

  /** On every fixed step: a chicken within CHICKEN_TOUCH of the hero is eaten, healing on the first sixteenth at least NEVER_LATE away. */
  const touchChickens = () => {
    if (st.chickens.length === 0) return;
    const hp = heroPose(st.w);
    st.chickens = st.chickens.filter((c) => {
      if (Math.hypot(c.x - hp.x, c.y - hp.y) > CHICKEN_TOUCH) return true;
      const sHeal = sixteenthAtOrAfter(st.s + NEVER_LATE);
      rec().chickenTaken(c.i, st.s, st.w, sHeal);
      st.q.push(sHeal, { kind: "chickenHeal" });
      return false;
    });
  };

  /** On every fixed step: an orb within ORB_TOUCH of the hero is taken (the nearest, if two are). */
  const touchOrbs = () => {
    const o = st.orbs;
    if (!o) return;
    const hp = heroPose(st.w);
    let k = -1;
    o.pts.forEach((p, i) => {
      const d = Math.hypot(p.x - hp.x, p.y - hp.y);
      if (d <= ORB_TOUCH && (k < 0 || d < Math.hypot(o.pts[k].x - hp.x, o.pts[k].y - hp.y))) k = i;
    });
    if (k >= 0) takeOrb(k, true);
  };

  /** An orb is taken (walked into, or the one ahead at the deadline): it applies on the first grid time at least NEVER_LATE away, with its sound there. */
  const takeOrb = (k: number, walked: boolean) => {
    const o = st.orbs!;
    st.orbs = undefined;
    // (Without a grid time within ORB_GRID_WAIT, as before a free section or past the last one, it
    // applies on the next beat at least NEVER_LATE away, or at once at the song's end.)
    const g = sch.gridTimes[firstAtOrAfter(sch.gridTimes, st.s + NEVER_LATE - 1e-9)];
    const b = song.beats[firstAtOrAfter(song.beats, st.s + NEVER_LATE - 1e-9)];
    const sApply = g !== undefined && g - st.s <= ORB_GRID_WAIT ? g : (b ?? st.s);
    rec().upgradeTaken(o.u, k, st.s, st.w, sApply, walked);
    st.applying.push(o.offers[k]);
    st.q.push(sApply, { kind: "orbApply", item: o.offers[k] });
  };

  /** The hero takes an item: a new weapon starts firing, an evolution applies from now, a passive adds its bonus. */
  const takeUpgrade = (item: UpgradeItem) => {
    takeItem(st.loadout, item);
    st.bonus = passiveBonus(lib, st.loadout.passives);
    if (item.kind === "weapon") grantWeapon(item.id);
    else if (item.kind === "evolve") {
      const wd = lib.weapons.get(item.id)!;
      if (wd.evolve?.every) scheduleFires(wd, wd.evolve.every, wd.fires.every);
    } else {
      // Max health also heals that much at once.
      const p = lib.passives.get(item.id)!;
      if (p.maxHealth > 0) heal(p.maxHealth, "max-health");
    }
  };

  const gemArrives = (i: number, value: number) => {
    rec().gemArrive(i, st.w);
    if (st.level >= sch.levelUps.length) {
      st.xpNow += value;
      pushXp();
      return;
    }
    st.xpNow += value;
    pushXp();
    // A bar that fills after its scheduled moment levels up on the first downbeat after it fills.
    if (st.overdue && st.xpNow >= levelReq()) {
      st.overdue = false;
      st.q.push(downbeatAtOrAfter(st.s), { kind: "levelUp", id: sch.levelUps[st.level].id });
    }
  };

  const levelDue = (i: number) => {
    if (i !== st.level) return;
    // A full bar waits and glows until the scheduled moment.
    if (st.xpNow >= levelReq()) grant();
    else st.overdue = true;
  };

  // ---------------------------------------------------------------- archers
  const spawnArchers = (group: number, n: number) => {
    const def = lib.enemies.get("skeleton-archer")!;
    const r = new Rng(ctx.seed, "archers", group, st.nextId);
    // They come from one side together, so a single step can dodge their volley.
    const p = spawnPoint(def, r, true);
    const hp = heroPose(st.w);
    const dx = p.x - hp.x;
    const dy = p.y - hp.y;
    const dl = Math.hypot(dx, dy) || 1;
    const taken = new Set(st.alive.filter((f) => f.alive && f.archerGroup === group).map((f) => f.archerSlot));
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
    const hp = heroPose(st.w);
    const group = st.alive.filter((f) => f.alive && f.archerGroup === vol.group);
    for (const f of group) {
      if (Math.hypot(posX(f, st.w) - hp.x, posY(f, st.w) - hp.y) > ARCHER_SHOOT_RANGE) continue;
      f.drawing = { w0: st.w };
      rec().drawStart?.(f.id, st.w, vol.sRelease, Math.atan2(hp.y - HURTBOX_LIFT - posY(f, st.w) + 24, hp.x - posX(f, st.w)));
    }
    // Replacements for fallen archers walk in for the next volley.
    if (group.length < vol.archers) spawnArchers(vol.group, vol.archers - group.length);
  };

  const loose = (v: number) => {
    const vol = sch.volleys[v];
    const hp = heroPose(st.w);
    // Each arrow aims at the hero's position at the moment of release, not a predicted one.
    const ax = hp.x;
    const ay = hp.y - HURTBOX_LIFT;
    for (const f of st.alive) {
      if (!f.alive || f.archerGroup !== vol.group || !f.drawing) continue;
      const x0 = posX(f, st.w);
      const y0 = posY(f, st.w) - 24;
      const dl = Math.hypot(ax - x0, ay - y0) || 1;
      const r: ArrowRecord = { sRelease: st.s, wRelease: st.w, sArrive: vol.sArrive, wArrive: 0, x0, y0, ax, ay, x1: ax, y1: ay, w1: 0, outcome: "miss" };
      rec().arrow(r);
      st.flying.push({ v, index: st.nArrows++, x0, y0, ax, ay, wRelease: st.w, ux: (ax - x0) / dl, uy: (ay - y0) / dl, damage: f.def.attackDamage });
      rec().attack(f.id, { w0: f.drawing.w0, w: st.w, kind: "shoot", dir: Math.atan2(ay - y0, ax - x0) });
      f.drawing = undefined;
    }
  };

  /** The maximum health, with the max-health passives. */
  const healthMax = () => hero.health.max + st.bonus.maxHealth;

  /**
   * The hero takes damage; at none, the run is over and it takes no more. Nothing hurts it while it
   * is invulnerable, and each hurt makes it so.
   */
  const hurt = (amount: number, source: "bite" | "arrow" | "slam", enemy: string) => {
    if (amount <= 0 || st.health <= 0 || st.w < st.invulnUntil - 1e-9) return;
    st.invulnUntil = st.w + hero.health.invulnerableMs / 1000;
    st.health = Math.max(0, st.health - amount);
    rec().health(st.s, st.health / healthMax());
    rec().bite({ s: st.s, w: st.w, source, enemy, damage: amount, until: st.invulnUntil });
  };

  /** The hero heals (never past its maximum), from a source the play log names. */
  const heal = (amount: number, source: "chicken" | "level-up" | "max-health") => {
    if (st.health <= 0 || st.health >= healthMax()) return;
    const before = st.health;
    st.health = Math.min(healthMax(), st.health + amount);
    rec().health(st.s, st.health / healthMax());
    rec().heal({ s: st.s, amount: st.health - before, source });
  };

  /** A near miss: the slow-motion dip starts now (dips do not stack). */
  const nearMiss = () => {
    if (st.s < st.dipStart + 2 * DIP_RAMP + DIP_HOLD) return;
    st.dipStart = st.s;
  };

  const arrive = (v: number) => {
    const hp = heroPose(st.w);
    const hx0 = hp.x;
    const hy0 = hp.y - HURTBOX_LIFT;
    for (let i = st.flying.length - 1; i >= 0; i--) {
      const a = st.flying[i];
      if (a.v !== v) continue;
      st.flying.splice(i, 1);
      const wArrive = st.w;
      // Closest approach of the arrow's line to the hurtbox, ahead of the archer.
      const t = (hx0 - a.x0) * a.ux + (hy0 - a.y0) * a.uy;
      const perp = Math.abs((hx0 - a.x0) * a.uy - (hy0 - a.y0) * a.ux);
      const edge = perp - HURTBOX_RADIUS;
      const outcome = t < 0 ? "miss" : edge <= 0 ? "touch" : edge <= NEAR_MISS ? "near-miss" : "miss";
      const len = Math.hypot(a.ax - a.x0, a.ay - a.y0);
      const speed = len / Math.max(1e-3, st.w - a.wRelease);
      if (outcome === "touch") {
        // It shatters on the hero (and hurts, when the hero has health).
        hurt(a.damage, "arrow", "skeleton-archer");
        rec().arrowEnd(a.index, { wArrive, outcome, x1: a.x0 + a.ux * t, y1: a.y0 + a.uy * t, w1: a.wRelease + t / speed });
      } else {
        rec().arrowEnd(a.index, { wArrive, outcome, x1: a.ax + a.ux * ARROW_OVERSHOOT, y1: a.ay + a.uy * ARROW_OVERSHOOT, w1: st.w + ARROW_OVERSHOOT / speed });
      }
      rec().threat({ s: st.s, w: st.w, kind: "arrow", outcome, x: hp.x, y: hp.y });
      if (outcome === "near-miss") nearMiss();
    }
  };

  // ---------------------------------------------------------------- the boss
  const bossSpawn = () => {
    const b = sch.boss!;
    const hp = heroPose(st.w);
    const pp = forward();
    const x = hp.x + pp.tx * BOSS_LAND_AHEAD;
    const y = hp.y + pp.ty * BOSS_LAND_AHEAD;
    // (With set pieces, room under the cap first: they can fill it.)
    makeRoom(1);
    const f = spawnFoe(b.type, x, y, undefined, { entrance: "land", attacks: [] });
    f.landS = b.sEnter;
    st.bossFoe = f;
    rec().boss({ id: f.id, landS: b.sEnter, x, y, health: [{ s: st.s, frac: 1 }] });
  };

  const bossLand = () => {
    const boss = st.bossFoe;
    if (!boss || !boss.alive) return;
    // It lands on the downbeat, knocking nearby enemies back.
    for (const f of st.alive) {
      if (f === boss || !f.alive || pinned(f)) continue;
      const dx = posX(f, st.w) - boss.x;
      const dy = posY(f, st.w) - boss.y;
      const d = Math.hypot(dx, dy);
      if (d >= BOSS_LAND_PUSH || d < 1e-6) continue;
      f.knockX = (dx / d) * (BOSS_LAND_PUSH - d) * 0.5;
      f.knockY = (dy / d) * (BOSS_LAND_PUSH - d) * 0.5;
      f.knockT = KNOCK_TIME;
    }
  };

  const slamMark = (k: number) => {
    if (!st.bossFoe || !st.bossFoe.alive) return;
    // A marker appears at the hero's current position and grows for two beats.
    const hp = heroPose(st.w);
    st.slamMarks.set(k, { x: hp.x, y: hp.y, sMark: st.s, wMark: st.w });
    const boss = st.bossFoe;
    rec().slamMark?.(k, { x: hp.x, y: hp.y, sMark: st.s, wMark: st.w, sLand: sch.boss!.slams[k].sLand, boss: boss.id, dir: Math.atan2(hp.y - posY(boss, st.w), hp.x - posX(boss, st.w)) });
  };

  const slamLand = (k: number) => {
    const m = st.slamMarks.get(k);
    const boss = st.bossFoe;
    if (!m || !boss || !boss.alive) return;
    st.slamMarks.delete(k);
    const hp = heroPose(st.w);
    // The marker appears under the hero: leaving it before the slam lands is a near miss.
    const edge = Math.hypot(hp.x - m.x, hp.y - m.y) - SLAM_RADIUS - HURTBOX_RADIUS;
    const outcome = edge <= 0 ? "touch" : "near-miss";
    rec().slam({ sMark: m.sMark, wMark: m.wMark, sLand: st.s, wLand: st.w, x: m.x, y: m.y, radius: SLAM_RADIUS, outcome });
    rec().attack(boss.id, { w0: Math.max(m.wMark, st.w - 0.6), w: st.w, kind: "slam", dir: Math.atan2(m.y - posY(boss, st.w), m.x - posX(boss, st.w)) });
    rec().threat({ s: st.s, w: st.w, kind: "slam", outcome, x: hp.x, y: hp.y });
    if (outcome === "touch") hurt(boss.def.attackDamage, "slam", boss.type);
    if (outcome === "near-miss") nearMiss();
  };

  // ---------------------------------------------------------------- formations
  /** Formation members on their slots and the boss dropping in hold their places. */
  const pinned = (f: Foe) => ((f.slot !== undefined || f.at !== undefined) && st.s < f.slotUntil) || (f.landS !== undefined && st.s < f.landS);

  /** Formations ride with the camera-smoothed hero: the camera centre less its lead. */
  const formationAnchor = () => {
    const lead = sectionAt(st.s).level.camera.lead;
    const pp = leadDir(st.s);
    const cam = st.camera;
    return { x: cam.x - pp.tx * lead, y: cam.y - pp.ty * lead + 12, vx: cam.vx, vy: cam.vy };
  };

  const formationEnter = (fi: number) => {
    const fs = sch.formations[fi];
    const def = lib.formations.get(fs.def)!;
    const view = st.camera.view();
    const bStart = sbiAt(fs.sStart);
    const stepB = subdivisionBeats(def.step, 2);
    const members: number[] = [];
    // A formation keeps the heading it entered on, and the camera leads that way while it is in play.
    const dir = { x: 0, y: 0, tx: st.heading.x, ty: st.heading.y };
    st.formationDir = { x: dir.tx, y: dir.ty, until: fs.sRelease };
    const frame = () => dir;
    // (With set pieces, as many as fit under the cap, after the stream makes room.)
    const count = makeRoom(def.count);
    for (let k = 0; k < count; k++) {
      let place: (t: number) => { x: number; y: number };
      let start: { x: number; y: number };
      // Places are offsets from the anchor; the entrance starts just off the camera's view.
      const a0 = formationAnchor();
      if (def.shape === "kick-line") {
        // A line of dancers ahead of the hero, along its heading.
        place = () => {
          const p = frame();
          const d = LINE_LEAD + def.spacing * k;
          return { x: p.tx * d, y: p.ty * d };
        };
        const end = place(fs.sStart);
        if (dir) {
          // From just off the screen, on the side it stands.
          const ex = dir.tx > 1e-6 ? (view[2] - a0.x) / dir.tx : dir.tx < -1e-6 ? (view[0] - a0.x) / dir.tx : Infinity;
          const ey = dir.ty > 1e-6 ? (view[3] - a0.y) / dir.ty : dir.ty < -1e-6 ? (view[1] - a0.y) / dir.ty : Infinity;
          const out = Math.max(Math.min(ex, ey) + 100 + k * 6, LINE_LEAD + def.spacing * k + 40);
          start = { x: dir.tx * out, y: dir.ty * out };
        } else start = { x: Math.max(view[2] + 100 + k * 6 - a0.x, end.x + 40), y: end.y };
      } else {
        // A V-wedge: the leader in front, the arms trailing back.
        const arm = k === 0 ? 0 : Math.ceil(k / 2);
        const side = k === 0 ? 0 : k % 2 === 0 ? 1 : -1;
        place = () => {
          const p = frame();
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
      const p = slot(st.s);
      const f = spawnFoe(def.enemy, a0.x + p.x, a0.y + p.y, undefined, { formation: fi });
      f.weight = weightOf(f.def, true);
      f.slot = slot;
      f.slotUntil = fs.sRelease;
      if (def.dance) rec().danceStart?.(f.id, timeOfBeatIndex(song, bStart + def.entranceBeats), def.dance.animation, def.dance.alternateLegs);
      members.push(f.id);
      st.formationMembers[fi].push(f);
    }
    rec().formation({ def: fs.def, sStart: fs.sStart, sRelease: fs.sRelease, members });
  };

  /**
   * Room for a set piece of n under the cap: the stream's enemies furthest from the hero leave the
   * field (as recycling does, without coming back). Returns how many of the n fit.
   */
  const makeRoom = (n: number): number => {
    const excess = st.alive.length + n - POP_CAP;
    if (excess > 0) {
      const hp = heroPose(st.w);
      const far = st.alive.filter((f) => f.alive && f.formation === undefined && f.def.size !== "boss").sort((a, b) => Math.hypot(posX(b, st.w) - hp.x, posY(b, st.w) - hp.y) - Math.hypot(posX(a, st.w) - hp.x, posY(a, st.w) - hp.y));
      for (const f of far.slice(0, excess)) {
        f.alive = false;
        f.finalSample = true;
        f.deathW = st.w;
        rec().death(f.id, st.w, true);
      }
      st.alive = st.alive.filter((f) => f.alive);
    }
    return Math.max(0, Math.min(n, POP_CAP - st.alive.length));
  };

  /**
   * A closing ring (set pieces): its members evenly spaced on a circle just outside the view, around
   * where the hero stands, with gaps of three missing places (two rows past 40 members). It closes at a
   * steady speed to RING_CLOSE from its centre on its accent; then its members chase as usual.
   */
  const ringEnter = (k: number) => {
    const ring = { ...sch.rings[k] };
    ring.count = makeRoom(ring.count);
    if (ring.count === 0) return;
    const def = lib.enemies.get(ring.enemy)!;
    const hp = heroPose(st.w);
    const view = st.camera.view();
    const far = Math.max(...[view[0], view[2]].flatMap((x) => [view[1], view[3]].map((y) => Math.hypot(x - hp.x, y - hp.y))));
    const r0 = far + def.radius + SPAWN_MARGIN;
    const r = new Rng(ctx.seed, "ring", k);
    const a0 = r.range(0, Math.PI * 2);
    const rows = ring.count > 40 ? 2 : 1;
    const fi = sch.formations.length + k;
    const members: number[] = [];
    const s0 = st.s;
    const s1 = ring.sClose;
    const gapAngles: number[] = [];
    for (let row = 0; row < rows; row++) {
      const n = row === 0 ? Math.ceil(ring.count / rows) : Math.floor(ring.count / rows);
      const places = n + 3 * ring.gaps;
      const gap = new Set<number>();
      for (let g = 0; g < ring.gaps; g++) {
        const first = Math.floor((g * places) / ring.gaps);
        for (let j = 0; j < 3; j++) gap.add(first + j);
        if (row === 0) gapAngles.push(a0 + ((first + 1) * Math.PI * 2) / places);
      }
      const from = r0 + row * RING_ROW;
      const to = RING_CLOSE + row * RING_ROW;
      for (let j = 0; j < places; j++) {
        if (gap.has(j)) continue;
        const th = a0 + (j * Math.PI * 2) / places;
        const c = Math.cos(th);
        const sn = Math.sin(th);
        const at = (t: number) => {
          const rad = lerp(from, to, clamp((t - s0) / (s1 - s0), 0, 1));
          return { x: hp.x + c * rad, y: hp.y + sn * rad };
        };
        const p = at(s0);
        const f = spawnFoe(ring.enemy, p.x, p.y, undefined, { formation: fi });
        f.weight = weightOf(def, true);
        f.at = at;
        f.slotUntil = s1;
        members.push(f.id);
      }
    }
    st.rings.push({ x: hp.x, y: hp.y, s0, s1, r0, gaps: gapAngles });
    // Its vacuum on the first downbeat after its accent.
    st.q.push(downbeatAtOrAfter(s1 + 1e-6), { kind: "vacuumDrop" });
    rec().formation({ def: "ring", sStart: s0, sRelease: s1, members });
  };

  /**
   * A stampede (set pieces): its enemies enter from one side of the view at STAMPEDE_SPEED times their
   * speed and run straight across, ignoring the hero, in rows with a gap of three missing places at
   * the same height in each. Those that leave the far side are recycled.
   */
  const stampedeEnter = (k: number) => {
    const sp = { ...sch.stampedes[k] };
    sp.count = makeRoom(sp.count);
    if (sp.count === 0) return;
    const def = lib.enemies.get(sp.enemy)!;
    const view = st.camera.view();
    const r = new Rng(ctx.seed, "stampede", k);
    const dir = r.next() < 0.5 ? 1 : -1;
    const speed = def.speed * STAMPEDE_SPEED;
    const fi = sch.formations.length + sch.rings.length + k;
    const members: number[] = [];
    const perRow = Math.ceil(sp.count / sp.rows);
    const places = perRow + 3;
    const top = view[1] - def.radius;
    const span = view[3] - view[1] + 2 * def.radius;
    const gapAt = Math.floor(r.range(0.2, 0.7) * places);
    let left = sp.count;
    for (let row = 0; row < sp.rows && left > 0; row++) {
      const x = dir > 0 ? view[0] - SPAWN_MARGIN - def.radius - row * STAMPEDE_ROW : view[2] + SPAWN_MARGIN + def.radius + row * STAMPEDE_ROW;
      for (let j = 0; j < places && left > 0; j++) {
        if (j >= gapAt && j < gapAt + 3) continue;
        const y = top + (span * (j + 0.5)) / places;
        const f = spawnFoe(sp.enemy, x, y, undefined, { formation: fi });
        f.run = { vx: dir * speed, vy: 0 };
        f.vx = dir * speed;
        members.push(f.id);
        left--;
      }
    }
    rec().formation({ def: "stampede", sStart: st.s, sRelease: st.s, members });
    st.stampedes.push({ fi, done: false });
  };

  const formationRelease = (fi: number) => {
    const fs = sch.formations[fi];
    const def = lib.formations.get(fs.def)!;
    const s = st.s;
    const wEnter = worldOf(timeOfBeatIndex(song, sbiAt(fs.sStart) + def.entranceBeats));
    const beat = (song.beats[Math.min(song.beats.length - 1, Math.floor(sbiAt(s)) + 1)] ?? s) - (song.beats[Math.floor(sbiAt(s))] ?? s);
    for (const f of st.formationMembers[fi]) {
      if (def.dance) rec().dance(f.id, { w0: wEnter, w1: f.alive ? st.w : f.deathW, anim: def.dance.animation, alternate: def.dance.alternateLegs, phase: 0 });
      if (!f.alive) continue;
      // The survivors become chasers; a charge rushes the hero first.
      f.slotUntil = s;
      if (def.release.mode === "charge") f.chargeUntil = s + CHARGE_BEATS * Math.max(0.1, beat);
    }
    st.formationMembers[fi] = [];
  };

  // ---------------------------------------------------------------- the loop
  const windupBefore = (t: number) => t - hero.swings.windupMs / 1000 / Math.max(0.05, baseRate(t));

  /** Song-owned events and the first samples: a fresh simulation's song time 0. */
  const init = () => {
    const q = st.q;
    // Mode switches, automatic swings in free movement, idle-swing checks in step movement.
    for (let i = 1; i < ctx.sections.length; i++) {
      const a = ctx.sections[i - 1].level.movement;
      const b = ctx.sections[i].level.movement;
      if (a !== b) q.push(ctx.sections[i].s0, { kind: "mode", steps: b === "steps" });
    }
    if (ctx.sections[0].level.movement === "free") st.walk = { w0: 0, x0: st.restPose.x, y0: st.restPose.y, vx0: 0, vy0: 0, tx: 0, ty: 0, t: 0 };
    // The sword swings by itself on every slot, the grid's too, and presses arm nothing.
    for (const sl of sch.slots) q.push(Math.max(0, windupBefore(sl.s)), { kind: "auto", slot: sl });
    // A held key strides on every step of the stride period.
    for (const t of sch.strideSteps.s) q.push(t, { kind: "held" });
    for (let bi = 0; bi < song.beats.length; bi++) {
      q.push(song.beats[bi], { kind: "beat", bi });
      // Free movement's beats are slow: the spawn director also looks on every sixteenth note.
      if (bi + 1 < song.beats.length && sectionAt(song.beats[bi]).level.movement === "free") for (const f of [0.25, 0.5, 0.75]) q.push(timeOfBeatIndex(song, bi + f), { kind: "spawnTick", bi: bi + f });
    }
    sch.levelUps.forEach((lu, i) => q.push(lu.s, { kind: "levelDue", i }));
    // The upgrade pool: the starting loadout fires from the start, without a level-up.
    for (const id of st.loadout.weapons) grantWeapon(id);
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
    // Gems on the field: a vacuum on the first downbeat after the kick line's release.
    for (const f of sch.formations) if (lib.formations.get(f.def)?.shape === "kick-line") q.push(downbeatAtOrAfter(f.sRelease + 1e-6), { kind: "vacuumDrop" });
    sch.rings.forEach((r, k) => q.push(r.s, { kind: "ring", k }));
    sch.stampedes.forEach((r, k) => q.push(r.s, { kind: "stampede", k }));
    if (sch.blast && sch.blast.s < endS) {
      const wd = lib.weapons.get(sch.blast.weapon)!;
      const b = wd.blast!;
      const stepB = subdivisionBeats(b.step, 2);
      const bands = Math.max(1, Math.round(b.beats / stepB));
      const bi0 = sbiAt(sch.blast.s);
      for (let k = 0; k < bands; k++) q.push(timeOfBeatIndex(song, bi0 + k * stepB), { kind: "blast", band: k });
    }
    {
      // A transform wave on the first downbeat of each section whose zone differs from the previous one's.
      for (let i = 1; i < ctx.sections.length; i++) {
        const z = ctx.sections[i].level.zone;
        if (z !== ctx.sections[i - 1].level.zone) q.push(ctx.sections[i].s0, { kind: "zoneWave", zone: z });
      }
      // Landmarks rise centred on the hero.
      for (const e of ctx.events) if (e.ev.landmark) q.push(e.s, { kind: "landmark", id: e.ev.landmark });
    }
    scheduleDecision(0);

    // The first camera and hero samples.
    pushBreak(rateAt(0));
    sampleHero(0, 0);
    st.nextHeroK = 1;
    recordFoes(0);
    st.nextFoeK = 1;
  };

  /** Song time of the loop's next stop: the next event, fixed step or rate change. */
  const nextStop = () => {
    const tFixed = st.fixedK / SIM_RATE;
    const tRate = Math.min(nextStepAfter(sch.baseSteps, st.s), inStop(st.s) ? st.stopEnd : Infinity, nextDipCut(st.s));
    return Math.min(st.q.peek(), tFixed, tRate, endS);
  };

  /** One turn of the loop: move to the next stop, then run every event due there. */
  const step = () => {
    const tFixed = st.fixedK / SIM_RATE;
    const t = nextStop();
    moveClock(t, t === tFixed);
    if (t === tFixed) {
      st.fixedK++;
      if (++st.recycleK % 30 === 0) recycle();
    }
    const q = st.q;
    while (q.peek() <= st.s + 1e-12) {
      const { ev } = q.pop();
      switch (ev.kind) {
        case "hitstopEnd":
          break;
        case "decide": {
          if (!io.player) break;
          for (const ke of io.player.decide(observe())) if (ke.s <= cutS + 1e-12 && ke.s < endS) q.push(Math.max(st.s, ke.s), { kind: "key", e: ke });
          scheduleDecision(st.s + 1e-6);
          break;
        }
        case "key":
          if (ev.e.kind === "press") press(ev.e);
          else release(ev.e);
          break;
        case "mode":
          switchMode(ev.steps);
          break;
        case "held":
          heldStride();
          break;
        case "auto":
          swingFor(ev.slot, st.s);
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
          if (ev.band === 0) st.blasting = true;
          blastBand(ev.band);
          break;
        case "spawnTick":
          spawnDirector(ev.bi);
          break;
        case "beat": {
          spawnDirector(ev.bi);
          // The groove drains a bar after the latest press that raised it (free sections freeze it).
          const sec = sectionAt(st.s);
          const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
          const beatS = song.beats[Math.min(song.beats.length - 1, ev.bi + 1)] - song.beats[Math.max(0, Math.min(song.beats.length - 2, ev.bi))];
          if (sec.level.movement === "steps" && st.groove > 0 && st.s - st.lastRaiseS > hero.meter.drain.afterBars * bpb * beatS + 1e-9) {
            st.groove = Math.max(0, st.groove - drainPerBeat(hero, bpb));
            rec().groove(st.s, st.groove);
            setTier();
          }
          break;
        }
        case "appear":
          appear(ev.type);
          break;
        case "levelUp":
          grant();
          break;
        case "orbDefault":
          if (st.orbs?.u === ev.u) takeOrb(0, false);
          break;
        case "chickenHeal":
          heal(CHICKEN_HEAL, "chicken");
          break;
        case "orbApply":
          st.applying.splice(st.applying.findIndex((x) => x.kind === ev.item.kind && x.id === ev.item.id), 1);
          takeUpgrade(ev.item);
          break;
        case "levelDue":
          levelDue(ev.i);
          break;
        case "vacuumDrop":
          vacuumDrop();
          break;
        case "gem":
          gemArrives(ev.gem, ev.value);
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
        case "ring":
          ringEnter(ev.k);
          break;
        case "stampede":
          stampedeEnter(ev.k);
          break;
        case "zoneWave": {
          const hp = heroPose(st.w);
          rec().zoneWave({ s: st.s, w: st.w, x: hp.x, y: hp.y, zone: ev.zone });
          break;
        }
        case "landmark": {
          const hp = heroPose(st.w);
          rec().landmark({ id: ev.id, s: st.s, w: st.w, x: hp.x, y: hp.y });
          break;
        }
      }
    }
  };

  /** Close the tracks: every enemy's last sample. */
  const finish = () => {
    const wEnd = st.w;
    while (st.nextFoeK / ENEMY_RATE <= wEnd + 1 / ENEMY_RATE) {
      recordFoes(st.nextFoeK / ENEMY_RATE);
      st.nextFoeK++;
    }
  };

  const pushKey = (e: KeyEvent) => {
    if (e.s < st.s - 1e-12) throw new Error(`key event at s=${e.s} is before the simulation's time ${st.s}`);
    if (e.s <= cutS + 1e-12 && e.s < endS) st.q.push(e.s, { kind: "key", e });
  };

  /** The player stops: its key events still waiting are dropped. */
  const dropKeys = () => st.q.remove((ev) => ev.kind === "key" || ev.kind === "decide");

  return { init, step, nextStop, moveClock, finish, observe, pushKey, heroPose, dropKeys };
}

/**
 * A steppable simulation. `advanceTo` runs the game rules up to a song time;
 * `key` applies a key event at its exact song time; `clone` is a snapshot for
 * rollback. Advancing in many small calls gives exactly the recording one run
 * to the end gives.
 */
export class Sim {
  private readonly env: Env;
  private readonly st: SimState;
  private readonly io: Io;
  private readonly eng: ReturnType<typeof engine>;
  private finished = false;

  constructor(ctx: Ctx, sch: Schedule, world: World, recorder: Recorder, opts: { player?: Player; cutS?: number; untilS?: number } = {}, from?: { env: Env; st: SimState }) {
    if (from) {
      this.env = from.env;
      this.st = from.st;
    } else {
      const shared = new WeakSet<object>();
      registerShared(ctx, shared);
      registerShared(sch, shared);
      shared.add(world);
      this.env = { ctx, sch, world, endS: Math.min(opts.untilS ?? Infinity, ctx.duration + 1), cutS: opts.cutS ?? Infinity, shared };
      this.st = freshState(this.env);
    }
    this.io = { recorder, player: opts.player };
    this.eng = engine(this.env, this.st, this.io);
    if (!from) this.eng.init();
  }

  /** Song time now. */
  get s(): number {
    return this.st.s;
  }

  /** World time now. */
  get w(): number {
    return this.st.w;
  }

  /** Song time the simulation ends. */
  get endS(): number {
    return this.env.endS;
  }

  get done(): boolean {
    return this.st.s >= this.env.endS;
  }

  get recorder(): Recorder {
    return this.io.recorder;
  }

  set recorder(r: Recorder) {
    this.io.recorder = r;
  }

  /** The keys held down now. */
  heldKeys(): Key[] {
    return [...this.st.held];
  }

  /**
   * Stop the virtual player (handing over to the keyboard): its key events still waiting are
   * dropped, and from now on only `key` presses keys. Release its held keys with `key`.
   */
  detachPlayer(): void {
    this.io.player = undefined;
    this.eng.dropKeys();
  }

  /**
   * Run the game rules up to song time t: every event before t happens, and
   * the clock (and the hero and enemy samples) moves to t. Events at t itself
   * wait for the next call, so a key at t still takes its place among them.
   * (Within a nanosecond of one of its own stops, the clock stays just short of t.)
   */
  advanceTo(t: number): void {
    const { st, env, eng } = this;
    if (t >= env.endS) {
      this.runToEnd();
      return;
    }
    while (st.s < env.endS && eng.nextStop() < t - CLOCK_EPS) eng.step();
    // The clock stops between the loop's own stops only well clear of them: rate changes are
    // resolved with tolerances near 1e-11 s, and a stop just before one would see the new rate.
    if (t - st.s > CLOCK_EPS && eng.nextStop() - t > CLOCK_EPS) eng.moveClock(t, false, true);
  }

  /** Run to the end of the song. */
  runToEnd(): void {
    const { st, env, eng } = this;
    while (st.s < env.endS) eng.step();
    if (!this.finished) {
      this.finished = true;
      eng.finish();
    }
  }

  /** Apply a key event at its exact song time (at or after the simulation's time). */
  key(e: KeyEvent): void {
    this.eng.pushKey(e);
  }

  observe(): Observation {
    return this.eng.observe();
  }

  /** The hero's position now. */
  heroPosition(): { x: number; y: number } {
    const p = this.eng.heroPose(this.st.w);
    return { x: p.x, y: p.y };
  }

  /** A deep copy for rollback: it shares nothing mutable with this one, and records into `recorder`. */
  clone(recorder: Recorder): Sim {
    const st = deepClone(this.st, this.env.shared);
    const c = new Sim(this.env.ctx, this.env.sch, this.env.world, recorder, {}, { env: this.env, st });
    c.finished = this.finished;
    return c;
  }
}

/** The level's world: the open field. */
export function worldFor(): World {
  return new OpenWorld();
}

export function simulate(ctx: Ctx, sch: Schedule, opts: SimOptions): SimResult {
  const rec = new BatchRecorder();
  const world = worldFor();
  const sim = new Sim(ctx, sch, world, rec, opts);
  sim.runToEnd();
  return rec.result({ endS: sim.s, blastS: sch.blast?.s, animNames: HERO_ANIMS, heroRate: HERO_RATE });
}
