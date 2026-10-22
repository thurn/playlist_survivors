/**
 * evaluatePlan: computes one frame from the plan at song time s. A pure
 * function of song time: nothing carries between frames. Effects are closed
 * form in their age, seed and recipe.
 */
import type { ContentLibrary } from "../content/loader.ts";
import type { EffectLayer, EffectRecipe } from "../content/schema.ts";
import { rand01, noise1 } from "../core/rng.ts";
import { beatHit, beatSwell, clamp, frac, lerp, smoothstep } from "../core/shaping.ts";
import { Warp } from "../core/warp.ts";
import { latestWave, waveRadius } from "../mapgen/openfield.ts";
import { countUpTo, enemyPos, heroSample } from "./sample.ts";
import { StaticIndex, type PlanIndex } from "./view.ts";
import { partnerPress, type EffectEvent, type EnemyRecord, type Plan, type PlanInput, type PlanSection, type UiEvent } from "./types.ts";
import { orbitAt } from "../sim/tiers.ts";

export const DEATH_FALL = 0.5;
const FLASH_TIME = 0.05;
const KNOCK_TIME = 0.22;
const GRAVITY_SCALE = 1;
const DECAY_PER_S = 1.6;
const MAX_SHAKE = 9;
export const MAX_PARTICLES = 6000;
export const MAX_DECALS = 400;

/** The beat track shows beats this far ahead (seconds), and presses this long after them. */
const TRACK_AHEAD = 2.2;
const PRESS_SHOW = 0.6;
/** A released key's light fades over this long. */
const KEY_FADE = 0.25;
/** A tier-up flashes the groove meter for this long (song seconds). */
const TIER_FLASH = 0.6;

/** The groove meter at song time s (its last value at or before s). */
export function grooveAt(g: { s: ArrayLike<number>; v: ArrayLike<number> }, s: number): number {
  let lo = 0;
  let hi = g.s.length - 1;
  if (hi < 0 || s < g.s[0]) return 0;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (g.s[mid] <= s) lo = mid;
    else hi = mid - 1;
  }
  return g.v[lo];
}

/** The hero's health (fraction of its maximum) at song time s. */
export function healthAt(h: { s: ArrayLike<number>; v: ArrayLike<number> }, s: number): number {
  return h.s.length > 0 && s >= h.s[0] ? grooveAt(h, s) : 1;
}

/** A hurt flash fades over this long (song seconds). */
const HURT_FADE = 0.3;

/** How hurt the hero looks at song time s (0–1): it flashes on each drop in health and fades. */
export function hurtAt(h: { s: ArrayLike<number>; v: ArrayLike<number> }, s: number): number {
  return healthFlash(h, s, -1);
}

/** How brightly the health bar glows from a heal at song time s (0–1). */
export function healAt(h: { s: ArrayLike<number>; v: ArrayLike<number> }, s: number): number {
  return healthFlash(h, s, 1);
}

/** A flash on the latest change of health in direction `dir` (-1 a drop, 1 a rise), fading over HURT_FADE. */
function healthFlash(h: { s: ArrayLike<number>; v: ArrayLike<number> }, s: number, dir: number): number {
  let lo = 0;
  let hi = h.s.length - 1;
  if (hi < 1 || s < h.s[0]) return 0;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (h.s[mid] <= s) lo = mid;
    else hi = mid - 1;
  }
  for (let i = lo; i > 0 && s - h.s[i] < HURT_FADE; i--) if ((h.v[i] - h.v[i - 1]) * dir > 0) return 1 - (s - h.s[i]) / HURT_FADE;
  return 0;
}

/** World pixels per full walk cycle for a regular-sized enemy (two strides). */
const ENEMY_STRIDE = 56;
/** An enemy this close to the hero (plus its radius), standing still, is in melee. */
const MELEE_REACH = 20;

/** Fraction of a beat the can-can kick's wind-up (stand, knee) takes before the kick lands on the beat. */
const KICK_WINDUP = 0.36;

export interface EnemySprite {
  id: number;
  type: string;
  x: number;
  y: number;
  /** Height above the ground (hover, falls). */
  z: number;
  anim: string;
  facing: number;
  frame: number;
  flash: boolean;
  alpha: number;
  scaleX: number;
  scaleY: number;
  shadow: number;
}

export interface Particle {
  tex: string;
  x: number;
  y: number;
  rot: number;
  scale: number;
  alpha: number;
  tint: number;
  additive: boolean;
}

export interface FxSprite {
  /** "proc:<name>" or "fx:<anim>". */
  art: string;
  frame: number;
  x: number;
  y: number;
  rot: number;
  scaleX: number;
  scaleY: number;
  alpha: number;
  tint: number;
  additive: boolean;
}

export interface Ring {
  x: number;
  y: number;
  radius: number;
  width: number;
  alpha: number;
  tint: number;
  additive: boolean;
}

export interface Light {
  x: number;
  y: number;
  radius: number;
  tint: number;
  strength: number;
}

export interface FloatText {
  text: string;
  x: number;
  y: number;
  alpha: number;
  tint: number;
  size: number;
}

export interface Shockwave {
  x: number;
  y: number;
  radius: number;
  strength: number;
}

export type KeyName = "up" | "down" | "left" | "right";

/** A key on the key display: how lit (0–1), the press's rating or style, and whether it is outlined (an off-beat press). */
export interface KeyLight {
  lit: number;
  rating?: string;
  outline?: boolean;
}

/**
 * How the key display shows a press (not a partner): a counted press in its rating's colour
 * (loose beyond Good), an off-beat press outlined in its rating's, a double tap grey ("extra"),
 * a stray press red ("stumble"). Presses without a class (the demo, older plans) as before.
 */
export function pressStyle(i: PlanInput): { rating?: string; outline?: boolean } {
  switch (i.class) {
    case "counted":
      return { rating: i.rating ?? "loose" };
    case "offbeat":
      return { rating: i.offbeat, outline: true };
    case "double":
      return { rating: "extra" };
    case "stray":
      return { rating: "stumble" };
  }
  if (i.extra) return { rating: i.stumble ? "stumble" : (i.offbeat ?? "extra") };
  return { rating: i.rating };
}

/** Song seconds spent at each tier, over the sections with a steady beat, up to song time `until`. */
export function tierTimes(plan: Plan, until: number): number[] {
  const t = plan.tier;
  if (!t) return [];
  const out = [0, 0, 0, 0];
  for (const sec of plan.sections) {
    if (sec.movement !== "steps") continue;
    const a = sec.s0;
    const b = Math.min(sec.s1, until);
    for (let k = 0; k < t.s.length; k++) {
      const lo = Math.max(a, t.s[k]);
      const hi = Math.min(b, k + 1 < t.s.length ? t.s[k + 1] : Infinity);
      if (hi > lo) out[t.v[k]] += hi - lo;
    }
  }
  return out;
}

export interface HudState {
  kills: number;
  /** Rated presses so far. */
  ratings: { perfect: number; great: number; good: number };
  /** The groove meter (0–1) and the damage bonus it gives (0.19 is +19%). */
  groove: number;
  bonus: number;
  /**
   * The arrow keys: how lit each one is (0–1) and the style of the press that lit it: a rating
   * (perfect, great, good; loose), extra (a double tap, grey) or stumble (a stray press, red);
   * an off-beat press is outlined.
   */
  keys: Record<KeyName, KeyLight>;
  /** Play mode's stomps with Space: both feet, lit like a key. */
  stomp?: KeyLight;
  /**
   * Press classes (play mode): the tier (0–3), the groove values its marks stand at, and a flash
   * (1 at a tier-up, fading).
   */
  tier?: { n: number; marks: number[]; flash: number };
  /**
   * The beat track: upcoming beats (seconds until each) from the song, downbeats marked. Play
   * mode telegraphs the cleaves: accents that will cleave, and the beat to press for each.
   */
  ticks: { dt: number; down: boolean; cleave?: boolean; pressHere?: boolean }[];
  /** Play mode: the heart glows (0–1) over the beat before a cleave's accent. */
  heartGlow?: number;
  /** Presses on the beat track as they happen: seconds since, offset from the grid (seconds), rating. */
  presses: { age: number; delta: number; rating?: string; steps: boolean }[];
  xp: number;
  level: number;
  weapons: { id: string; flash: number }[];
  progress: number;
  elapsed: number;
  beatPhase: number;
  barPhase: number;
  /** Free movement (the beat track is a flowing ribbon). */
  free: boolean;
  /** The count-in before step movement: which of the four count marks is lit (1–4), and how far the beat track has faded in (0–1). */
  countIn?: { count: number; fade: number; of?: number };
  energy: number;
  banners: { text: string; age: number; dur: number }[];
  title?: { age: number; remaining: number };
  boss?: { name: string; health: number; age: number; remaining: number };
  /**
   * The end card. With press classes: loose and stray presses, and the song seconds spent at each
   * tier in the sections with a steady beat.
   */
  end?: { age: number; averageGroove: number; loose?: number; strays?: number; tierTime?: number[] };
  /** The hero's health (fraction of its maximum; always full without play mode's health), its hurt flash and its heal glow (0–1). */
  health: number;
  hurt: number;
  heal: number;
  /** Play mode's health: clean beats in a row (rated Great or better), and whether the streak heals extra. */
  streak?: { n: number; healing: boolean };
}

export interface Frame {
  s: number;
  w: number;
  /** World and song beat indices. */
  wbi: number;
  sbi: number;
  section: PlanSection;
  sectionIndex: number;
  energy: number;
  camera: { x: number; y: number; zoom: number; shakeX: number; shakeY: number };
  hero: { x: number; y: number; z: number; facing: number; anim: string; frame: number; afterimages: { x: number; y: number; facing: number; anim: string; frame: number; alpha: number }[]; hurt: number };
  enemies: EnemySprite[];
  particles: Particle[];
  fx: FxSprite[];
  rings: Ring[];
  decals: FxSprite[];
  lights: Light[];
  texts: FloatText[];
  shockwaves: Shockwave[];
  flash: { alpha: number; tint: number };
  aberration: number;
  blades: { x: number; y: number; rot: number }[];
  bolts: { points: { x: number; y: number }[]; alpha: number }[];
  flames: { x: number; y: number; frame: number; alpha: number }[];
  arrows: { x: number; y: number; rot: number }[];
  slams: { x: number; y: number; radius: number; progress: number }[];
  gems: { x: number; y: number; big: boolean }[];
  /** Zone blend at the camera: index into plan.map.zones and blend toward the next. */
  zone: { a: number; b: number; t: number };
  /**
   * The open field: the zone everywhere a transform wave has not reached, and the wave sweeping
   * out now (its ring's radius, and the zones it changes from and to).
   */
  open?: { base: number; wave?: { x: number; y: number; radius: number; prev: number; zone: number } };
  hud: HudState;
  metronome: number;
  stats: { alive: number; particles: number };
}

const colorCache = new Map<string, number>();
export function hex(c: string): number {
  let v = colorCache.get(c);
  if (v === undefined) {
    v = parseInt(c.slice(1), 16);
    colorCache.set(c, v);
  }
  return v;
}

function param(p: number | [number, number] | undefined, intensity: number, dflt = 1): number {
  if (p === undefined) return dflt;
  return typeof p === "number" ? p : lerp(p[0], p[1], intensity);
}

/** Cached per-plan helpers. */
export class Evaluator {
  readonly warp: Warp;
  readonly index: PlanIndex;
  private recipes: Map<string, EffectRecipe>;
  private uiSorted: UiEvent[];
  private downbeats: Set<number>;
  private maxBonus: number;

  /** `index` reads a growing recording (play mode); by default the plan is finished. */
  constructor(
    readonly plan: Plan,
    readonly lib: ContentLibrary,
    index?: PlanIndex,
  ) {
    this.warp = new Warp(plan.warp);
    this.index = index ?? new StaticIndex(plan, lib);
    this.recipes = lib.effects;
    this.uiSorted = plan.events.ui;
    this.downbeats = new Set(plan.barStart.filter((b) => Number.isFinite(b)));
    this.maxBonus = lib.hero.groove.maxBonus;
    // Play mode's telegraphs: the song owns the accents, so the HUD may show them in advance.
    if (plan.rules?.accentTelegraphs && plan.cleaveAccents) {
      this.cleaveTimes = plan.cleaveAccents.map((a) => a.s).sort((a, b) => a - b);
      // The grid time one grid step before each accent: a Great press there arms the cleave (with
      // press cleaves, the accent's own tick: a press on the chord launches its shockwave).
      if (plan.rules.pressCleaves) this.pressHereTimes = [...this.cleaveTimes];
      else for (const a of this.cleaveTimes) {
        let i = 0;
        while (i + 1 < plan.grid.length && plan.grid[i + 1] <= a + 1e-6) i++;
        if (i > 0) this.pressHereTimes.push(plan.grid[i - 1]);
      }
    }
  }

  private cleaveTimes: number[] = [];
  private pressHereTimes: number[] = [];

  /** Is there a time in a sorted list within 40 ms of t? */
  private near(list: number[], t: number): boolean {
    const i = countUpTo(list, t + 0.04);
    return i > 0 && list[i - 1] >= t - 0.04;
  }

  /** Prefix sums of walked distance per enemy, for the sample count and index version they were built from. */
  private walkedCache = new Map<number, { pre: Float32Array; count: number; version: number }>();
  /** Distance an enemy has walked by world time w (prefix sums over its samples). */
  walked(e: EnemyRecord, w: number): number {
    const cached = this.walkedCache.get(e.id);
    const sm = e.samples ?? this.plan.enemySamples;
    const base = e.samples ? 0 : e.sampleOffset;
    let pre: Float32Array;
    if (cached && cached.count === e.sampleCount && cached.version === this.index.version) pre = cached.pre;
    else {
      // A grown recording extends the sums; a rewound one rebuilds them.
      const keep = cached && cached.version === this.index.version && cached.count < e.sampleCount ? cached.count : 1;
      pre = new Float32Array(Math.max(1, e.sampleCount));
      if (keep > 1) pre.set(cached!.pre.subarray(0, keep));
      for (let i = keep; i < e.sampleCount; i++) {
        const o = (base + i) * 2;
        pre[i] = pre[i - 1] + Math.hypot(sm[o] - sm[o - 2], sm[o + 1] - sm[o - 1]);
      }
      this.walkedCache.set(e.id, { pre, count: e.sampleCount, version: this.index.version });
    }
    const f = clamp((w - e.sampleW0) * this.plan.enemySampleRate, 0, Math.max(0, e.sampleCount - 1));
    const k = Math.min(pre.length - 2, Math.floor(f));
    if (k < 0) return 0;
    return lerp(pre[k], pre[k + 1], f - k);
  }

  sectionAt(s: number): number {
    const secs = this.plan.sections;
    for (let i = secs.length - 1; i >= 0; i--) if (s >= secs[i].s0) return i;
    return 0;
  }

  beatIndex(beats: Float64Array, t: number): number {
    const n = beats.length;
    if (t <= beats[0]) return (t - beats[0]) / (beats[1] - beats[0]);
    if (t >= beats[n - 1]) return n - 1 + (t - beats[n - 1]) / (beats[n - 1] - beats[n - 2]);
    let lo = 0;
    let hi = n - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (beats[mid] <= t) lo = mid;
      else hi = mid;
    }
    const span = beats[hi] - beats[lo];
    return span <= 0 ? lo : lo + (t - beats[lo]) / span;
  }

  /** Bar index (1-based) and bar start beat for a beat index. */
  barAt(bi: number): { bar: number; start: number; len: number } {
    const bs = this.plan.barStart;
    let lo = 1;
    let hi = bs.length - 1;
    if (bi < bs[1]) return { bar: 0, start: bs[1] - 2, len: 2 };
    while (lo < hi) {
      const mid = (lo + hi + 1) >> 1;
      if (bs[mid] <= bi) lo = mid;
      else hi = mid - 1;
    }
    const next = bs[lo + 1] ?? bs[lo] + (bs[lo] - (bs[lo - 1] ?? bs[lo] - 2));
    return { bar: lo, start: bs[lo], len: next - bs[lo] };
  }

  evaluate(s: number, opts: { metronome?: boolean } = {}): Frame {
    const plan = this.plan;
    const warp = this.warp;
    const w = warp.toWorld(s);
    const wbi = this.index.worldBeatIndex(w);
    const sbi = this.beatIndex(plan.songBeats, s);
    const si = this.sectionAt(s);
    const section = plan.sections[si];
    const free = section.movement === "free";
    const bar = this.barAt(Math.max(0, sbi));
    const beatPhase = frac(sbi);
    const barPhase = clamp((sbi - bar.start) / bar.len, 0, 0.9999);
    const energy = this.energyAt(sbi);

    // Camera.
    const cam = this.cameraAt(w);
    // Screenshake: trauma summed over recent impacts, in song time.
    let trauma = 0;
    const effects = plan.events.effects;
    const bucket = this.index.effectsAt(w);

    const particles: Particle[] = [];
    const fx: FxSprite[] = [];
    const rings: Ring[] = [];
    const decals: FxSprite[] = [];
    const lights: Light[] = [];
    const texts: FloatText[] = [];
    const shockwaves: Shockwave[] = [];
    let flashA = 0;
    let flashTint = 0xffffff;
    let aberration = 0;

    // Decals live long; gather them from recent effect events directly.
    this.collectDecals(s, w, decals);

    for (let k = 0; k < bucket.length; k++) {
      const ev = effects[bucket[k]];
      if (ev.w > w + 1e-9 && ev.s > s) continue;
      const recipe = this.recipes.get(ev.recipe);
      if (!recipe) continue;
      for (let li = 0; li < recipe.layers.length; li++) {
        const L = recipe.layers[li];
        if (L.kind === "decal") continue;
        const clock = "clock" in L ? L.clock : "song";
        const age = clock === "world" ? w - ev.w : s - ev.s;
        if (age < 0) continue;
        this.layer(L, ev, age, li, particles, fx, rings, lights, texts, shockwaves, (t) => (trauma += t), (a, c) => {
          if (a > flashA) {
            flashA = a;
            flashTint = c;
          }
        }, (a) => (aberration = Math.max(aberration, a)));
      }
    }
    trauma = Math.min(1, trauma);
    const shakeAmt = MAX_SHAKE * trauma * trauma;
    const shakeX = noise1(11, s * 30) * shakeAmt;
    const shakeY = noise1(23, s * 30) * shakeAmt;

    // Hero.
    const h = heroSample(plan, w);
    const after: Frame["hero"]["afterimages"] = [];
    if (h.trail > 0.05) {
      for (let k = 1; k <= 4; k++) {
        const hp = heroSample(plan, w - k * 0.035);
        after.push({ x: hp.x, y: hp.y - hp.z, facing: hp.facing, anim: hp.anim, frame: hp.frame, alpha: (0.5 - k * 0.1) * h.trail });
      }
    }

    // Enemies.
    const enemies: EnemySprite[] = [];
    const ebucket = this.index.enemiesAt(w);
    const tmp = { x: 0, y: 0 };
    const tmp2 = { x: 0, y: 0 };
    let alive = 0;
    for (let k = 0; k < ebucket.length; k++) {
      const e = this.index.enemy(ebucket[k]);
      if (w < e.spawnW || w > e.deathW + DEATH_FALL || (e.recycled && w >= e.deathW)) continue;
      const def = this.lib.enemies.get(e.type)!;
      enemyPos(plan, e, Math.min(w, e.deathW), tmp);
      let x = tmp.x;
      let y = tmp.y;
      let z = def.hover;
      let anim = "walk";
      let frame: number;
      let alpha = 1;
      let flash = false;
      let sx = 1;
      let sy = 1;
      // Facing from motion, or toward the hero when nearly still.
      enemyPos(plan, e, Math.min(w, e.deathW) + 0.1, tmp2);
      let vx = tmp2.x - tmp.x;
      let vy = tmp2.y - tmp.y;
      const moving = vx * vx + vy * vy >= 0.5;
      if (!moving) {
        vx = h.x - x;
        vy = h.y - y;
      }
      let facing = facingOf(vx, vy);
      const dying = w >= e.deathW;
      if (!dying) alive++;
      // Walk cycles advance by distance, like the hero's: the feet never slide.
      const stride = ENEMY_STRIDE * def.scale * (def.radius / 12);
      frame = Math.floor(frac(this.walked(e, Math.min(w, e.deathW)) / stride + (e.id % 7) * 0.13) * 8);
      // At the hero, melee enemies attack on their movement group's subdivision (it never hurts).
      const contact = Math.hypot(h.x - x, h.y - y) <= MELEE_REACH + def.radius;
      if (contact && !moving && !dying && def.behavior === "chase") {
        const flyer = def.atlas === "slime" || def.atlas === "bat" || def.atlas === "bee" || def.atlas === "eyeball";
        const n = flyer ? 5 : 6;
        const impact = flyer ? 2 : 4;
        const period = def.group === "eighth" ? 0.5 : def.group === "beat" ? 1 : def.group === "bar" ? bar.len : 2 * bar.len;
        anim = flyer ? "attack" : "slash";
        frame = Math.floor(frac(wbi / period + impact / n) * n);
      }
      if (def.hop) {
        const ph = frac(wbi);
        const hopH = Math.sin(Math.PI * clamp((ph - 0.55) / 0.45, 0, 1));
        z += 5 * hopH;
        const land = beatHit(ph);
        sx = 1 + 0.25 * land * (1 - hopH);
        sy = 1 - 0.2 * land * (1 - hopH) + 0.12 * hopH;
      }
      if (def.hover > 0) z += 3 * Math.sin((wbi * Math.PI * 2) / Math.max(0.5, def.walkCycleBeats));
      // Entrances: the boss lands from above.
      if (e.entrance === "land") {
        const landW = e.spawnW + 0.55;
        if (w < landW) {
          const u = (w - e.spawnW) / 0.55;
          z = (1 - u * u) * 260;
          alpha = Math.min(1, u * 3);
        }
      }
      // Attacks: archers draw and loose; the boss winds up its slam.
      if (e.attacks) {
        for (const at of e.attacks) {
          const lead = Math.max(0.05, at.w - at.w0);
          if (w >= at.w0 && w < at.w + 0.25) {
            anim = at.kind === "slam" ? "slash" : "shoot";
            const n = at.kind === "slam" ? 6 : 13;
            const impact = at.kind === "slam" ? 4 : 9;
            frame = clamp(Math.floor(impact + ((w - at.w) / lead) * impact), 0, n - 1);
            facing = facingOf(Math.cos(at.dir), Math.sin(at.dir));
          }
        }
      }
      // Formation dance.
      if (e.dance && w >= e.dance.w0 && w < e.dance.w1) {
        // The kick lands on the beat: the knee comes up in the last part of the beat
        // before, the leg is out on the beat and held, then comes down for the next.
        anim = "kick";
        const u = wbi + KICK_WINDUP;
        if (e.dance.alternate && Math.floor(u) % 2 === 1) anim = "kick-alt";
        const ph = frac(u);
        frame = ph < KICK_WINDUP / 2 ? 0 : ph < KICK_WINDUP ? 1 : 2;
        facing = 2;
      }
      // Hits flash white; the killing blow also knocks the body back (the simulation
      // already moved survivors).
      for (const f of e.fates) {
        const age = w - f.w;
        if (age < 0) continue;
        if (age < FLASH_TIME) flash = true;
        if (age < KNOCK_TIME && f.lethal) {
          const kx = f.x - h.x;
          const ky = f.y - h.y;
          const kl = Math.hypot(kx, ky) || 1;
          const amt = (1 - age / KNOCK_TIME) ** 2 * 10;
          x += (kx / kl) * amt;
          y += (ky / kl) * amt;
          sx *= 1 + 0.15 * (1 - age / KNOCK_TIME);
          sy *= 1 - 0.12 * (1 - age / KNOCK_TIME);
        }
      }
      if (dying) {
        const age = w - e.deathW;
        anim = "hurt";
        frame = clamp(Math.floor((age / DEATH_FALL) * 6), 0, 5);
        alpha = 1 - smoothstep((age - DEATH_FALL * 0.5) / (DEATH_FALL * 0.5));
        z = def.hover * Math.max(0, 1 - age * 4);
      }
      if (anim === "walk" && def.atlas === "slime") frame = Math.floor(frac(wbi) * 4);
      else if (anim === "walk" && def.walkCycleBeats > 0 && (def.atlas === "bat" || def.atlas === "bee" || def.atlas === "eyeball")) frame = Math.floor(frac(wbi / def.walkCycleBeats) * 4);
      enemies.push({ id: e.id, type: e.type, x, y, z, anim, facing, frame, flash, alpha, scaleX: sx * def.scale, scaleY: sy * def.scale, shadow: def.scale });
    }

    // Weapons.
    const blades: Frame["blades"] = [];
    const flames: Frame["flames"] = [];
    const end = this.index.blastS() ?? plan.duration;
    for (const g of plan.weapons) {
      if (s < g.s || s >= end) continue;
      const wd = this.lib.weapons.get(g.id)!;
      if (wd.kind === "orbit" && wd.orbit) {
        const bpb = bar.len;
        const th0 = (Math.PI * 2 * wbi) / (wd.orbit.periodBars * bpb);
        // Self-firing weapons: the blades and their orbit at the tier now.
        const orbit = plan.rules?.selfFiring && plan.tier ? orbitAt(wd, this.tierAt(s)) : wd.orbit;
        for (let k = 0; k < orbit.count; k++) {
          const th = th0 + (k * Math.PI * 2) / orbit.count;
          blades.push({ x: h.x + Math.cos(th) * orbit.radius, y: h.y + Math.sin(th) * orbit.radius * 0.8, rot: th * 3 });
        }
      } else if (wd.kind === "trail") {
        for (let k = 0; k < 10; k++) {
          const t = w - 0.06 - k * 0.07;
          const hp = heroSample(plan, t);
          flames.push({ x: hp.x + (rand01(k, Math.floor(t * 14)) - 0.5) * 8, y: hp.y, frame: Math.floor((w * 12 + k * 3) % 64), alpha: 1 - k / 10 });
        }
      }
    }
    const bolts: Frame["bolts"] = [];
    for (const c of plan.chains) {
      const first = c.points[1]?.w ?? c.points[0].w;
      const last = c.points[c.points.length - 1].w;
      if (w < c.points[0].w - 0.001 || w > last + 0.18) continue;
      void first;
      const pts: { x: number; y: number }[] = [];
      for (const p of c.points) if (p.w <= w + 1e-6) pts.push({ x: p.x, y: p.y });
      if (pts.length >= 2) bolts.push({ points: pts, alpha: w > last ? 1 - (w - last) / 0.18 : 1 });
    }
    const arrows: Frame["arrows"] = [];
    for (const pr of plan.projectiles) {
      if (w < pr.w0 || w > pr.w1) continue;
      const u = (w - pr.w0) / (pr.w1 - pr.w0);
      arrows.push({ x: lerp(pr.x0, pr.x1, u), y: lerp(pr.y0, pr.y1, u), rot: Math.atan2(pr.y1 - pr.y0, pr.x1 - pr.x0) });
    }
    const slams: Frame["slams"] = [];
    for (const sl of plan.slams) {
      if (w < sl.w0 || w > sl.w1 + 0.05) continue;
      slams.push({ x: sl.x, y: sl.y, radius: sl.radius, progress: clamp((w - sl.w0) / (sl.w1 - sl.w0), 0, 1) });
    }
    const gems: Frame["gems"] = [];
    for (const g of this.index.gemsAt(w)) {
      const u = (w - g.w0) / Math.max(0.05, g.w1 - g.w0);
      const r0 = rand01(g.seed, 1);
      const r1 = rand01(g.seed, 2);
      // Burst out, hang, then fly to the hero in an arc.
      const ox = (r0 - 0.5) * 30;
      const oy = (r1 - 0.5) * 20 - 6;
      const pop = smoothstep(Math.min(1, (w - g.w0) / 0.25));
      const fly = smoothstep(clamp((u - 0.55) / 0.45, 0, 1));
      const hp = heroSample(plan, w);
      const bx = g.x0 + ox * pop;
      const by = g.y0 + oy * pop - 4 * Math.sin(Math.PI * pop);
      gems.push({ x: lerp(bx, hp.x, fly), y: lerp(by, hp.y - 16, fly) - 30 * Math.sin(Math.PI * fly), big: g.big });
    }

    // Zone blend at the camera.
    const open = plan.open ? this.openZone(s, sbi, w) : undefined;
    const zone = open ? open.zone : this.zoneAt(w);

    const hud = this.hud(s, sbi, beatPhase, barPhase, free, energy);
    let metronome = 0;
    if (opts.metronome) metronome = Math.exp(-10 * beatPhase * (this.beatLenAt(sbi) || 0.5));

    if (particles.length > MAX_PARTICLES) particles.length = MAX_PARTICLES;
    return {
      s,
      w,
      wbi,
      sbi,
      section,
      sectionIndex: si,
      energy,
      camera: { x: cam.x, y: cam.y, zoom: cam.zoom, shakeX, shakeY },
      hero: { x: h.x, y: h.y, z: h.z, facing: h.facing, anim: h.anim, frame: h.frame, afterimages: after, hurt: hurtAt(plan.health, s) },
      enemies,
      particles,
      fx,
      rings,
      decals,
      lights,
      texts,
      shockwaves,
      flash: { alpha: flashA, tint: flashTint },
      aberration,
      blades,
      bolts,
      flames,
      arrows,
      slams,
      gems,
      zone,
      ...(open ? { open: { base: open.base, wave: open.wave } } : {}),
      hud,
      metronome,
      stats: { alive, particles: particles.length },
    };
  }

  beatLenAt(sbi: number): number {
    const b = this.plan.songBeats;
    const k = clamp(Math.floor(sbi), 0, b.length - 2);
    return b[k + 1] - b[k];
  }

  energyAt(sbi: number): number {
    const e = this.plan.energy;
    if (sbi <= 0) return e[0];
    if (sbi >= e.length - 1) return e[e.length - 1];
    const k = Math.floor(sbi);
    return lerp(e[k], e[k + 1], sbi - k);
  }

  cameraAt(w: number) {
    const c = this.plan.camera;
    const f = Math.max(0, (w - c.w0) * c.rate);
    const k = Math.min(c.x.length - 2, Math.floor(f));
    const u = Math.min(1, f - k);
    return { x: lerp(c.x[k], c.x[k + 1], u), y: lerp(c.y[k], c.y[k + 1], u), zoom: lerp(c.zoom[k], c.zoom[k + 1], u) };
  }

  private collectDecals(s: number, w: number, out: FxSprite[]) {
    const plan = this.plan;
    // Binary search the newest decal at or before w, then walk back.
    const idx = this.index.decals();
    let lo = 0;
    let hi = idx.length;
    while (lo < hi) {
      const mid = (lo + hi) >> 1;
      if (plan.events.effects[idx[mid]].w <= w) lo = mid + 1;
      else hi = mid;
    }
    for (let k = lo - 1; k >= 0 && out.length < MAX_DECALS; k--) {
      const ev = plan.events.effects[idx[k]];
      const L = this.recipes.get(ev.recipe)!.layers.find((l) => l.kind === "decal") as Extract<EffectLayer, { kind: "decal" }>;
      const age = w - ev.w;
      if (age > L.life) break;
      const tint = L.tint === "debris" ? hex(ev.debris?.[0] ?? "#802020") : L.tint ? hex(L.tint) : 0xffffff;
      const sc = param(L.size, ev.intensity) * (ev.scale ?? 1);
      out.push({
        art: L.art,
        frame: 0,
        x: ev.x,
        y: ev.y,
        rot: rand01(ev.seed, 99) * Math.PI * 2 * (L.art === "proc:scorch" ? 0 : 1),
        scaleX: sc,
        scaleY: sc,
        alpha: Math.min(1, age * 20) * (1 - smoothstep((age - L.life * 0.6) / (L.life * 0.4))) * 0.85,
        tint,
        additive: false,
      });
    }
    void s;
  }

  /** The open field's zones: the newest transform wave decides, and the camera blends as its ring passes. */
  private openZone(s: number, sbi: number, w: number): { zone: Frame["zone"]; base: number; wave?: NonNullable<Frame["open"]>["wave"] } {
    const op = this.plan.open!;
    const wv = latestWave(op.waves, s);
    if (!wv) return { zone: { a: op.firstZone, b: op.firstZone, t: 0 }, base: op.firstZone };
    if (s >= wv.endS) return { zone: { a: wv.zone, b: wv.zone, t: 0 }, base: wv.zone };
    const radius = waveRadius(wv, sbi);
    const cam = this.cameraAt(w);
    const d = Math.hypot(cam.x - wv.x, cam.y - wv.y);
    return { zone: { a: wv.prev, b: wv.zone, t: smoothstep((radius - d + 150) / 300) }, base: wv.prev, wave: { x: wv.x, y: wv.y, radius, prev: wv.prev, zone: wv.zone } };
  }

  private zoneAt(w: number): Frame["zone"] {
    const plan = this.plan;
    const zones = plan.map.zones;
    // Route progress at the camera: nearest route sample to the camera centre.
    const cam = this.cameraAt(w);
    const r = plan.map;
    let best = 0;
    let bestD = Infinity;
    const n = r.routeX.length;
    const step = Math.max(1, Math.floor(n / 400));
    for (let i = 0; i < n; i += step) {
      const d = (r.routeX[i] - cam.x) ** 2 + (r.routeY[i] - cam.y) ** 2;
      if (d < bestD) {
        bestD = d;
        best = i;
      }
    }
    for (let i = Math.max(0, best - step); i < Math.min(n, best + step); i++) {
      const d = (r.routeX[i] - cam.x) ** 2 + (r.routeY[i] - cam.y) ** 2;
      if (d < bestD) {
        bestD = d;
        best = i;
      }
    }
    const d = best * r.routeStep;
    let a = zones.length - 1;
    for (let i = 0; i < zones.length; i++)
      if (d < zones[i].d1) {
        a = i;
        break;
      }
    const blend = 256;
    if (a + 1 < zones.length && d > zones[a].d1 - blend / 2) return { a, b: a + 1, t: smoothstep((d - (zones[a].d1 - blend / 2)) / blend) };
    return { a, b: a, t: 0 };
  }

  private layer(
    L: EffectLayer,
    ev: EffectEvent,
    age: number,
    li: number,
    particles: Particle[],
    fx: FxSprite[],
    rings: Ring[],
    lights: Light[],
    texts: FloatText[],
    shockwaves: Shockwave[],
    addTrauma: (t: number) => void,
    setFlash: (a: number, c: number) => void,
    setAberration: (a: number) => void,
  ): void {
    const I = ev.intensity;
    switch (L.kind) {
      case "shake": {
        const t = param(L.trauma, I) - DECAY_PER_S * age;
        if (t > 0) addTrauma(t);
        return;
      }
      case "flash": {
        if (age > L.life) return;
        setFlash(param(L.opacity, I) * (1 - age / L.life), hex(L.color));
        return;
      }
      case "aberration": {
        if (age > L.life) return;
        if (I < 0.8 && ev.recipe !== "finale-blast") return;
        setAberration(param(L.strength, I) * (1 - age / L.life));
        return;
      }
      case "light": {
        if (age > L.life) return;
        const u = age / L.life;
        lights.push({ x: ev.x, y: ev.y, radius: param(L.radius, I) * (1 + 0.3 * u), tint: hex(L.color), strength: param(L.strength, I) * (1 - u) ** 1.5 });
        return;
      }
      case "distortion": {
        if (age > L.life) return;
        const u = age / L.life;
        shockwaves.push({ x: ev.x, y: ev.y, radius: param(L.radius, I) * u, strength: param(L.strength, I) * (1 - u) });
        return;
      }
      case "ring": {
        if (age > L.life) return;
        const u = age / L.life;
        const e = 1 - (1 - u) ** 3;
        rings.push({ x: ev.x, y: ev.y, radius: param(L.radius, I) * (ev.scale ?? 1) * e, width: param(L.width, I) * (1 - u * 0.6), alpha: 1 - u * u, tint: hex(L.color), additive: L.additive });
        return;
      }
      case "text": {
        if (age > L.life) return;
        const u = age / L.life;
        texts.push({ text: L.text, x: ev.x, y: ev.y - 18 * smoothstep(Math.min(1, u * 3)), alpha: 1 - smoothstep((u - 0.6) / 0.4), tint: hex(L.color), size: L.size });
        return;
      }
      case "sprite": {
        if (age > L.life) return;
        const u = age / L.life;
        const scale = param(L.scale, I) * (ev.scale ?? 1);
        const tint = L.tint ? hex(L.tint) : 0xffffff;
        let art = L.art;
        let rot = 0;
        let sxx = scale;
        let syy = scale;
        let x = ev.x;
        let y = ev.y;
        if (L.align === "hitShape" && ev.hit !== undefined) {
          const hit = this.plan.hits[ev.hit];
          const sh = hit.shape;
          if (sh.kind === "arc" && sh.halfAngle >= Math.PI - 1e-6) {
            // A full spin, or a cleave's shockwave at tier 3: a whole ring.
            art = "proc:ring";
            sxx = syy = (sh.radius / 62) * scale * (0.85 + 0.25 * u);
            y -= 14;
          } else if (sh.kind === "arc") {
            const deg = (sh.halfAngle * 360) / Math.PI;
            art = deg < 135 ? "proc:arc120" : deg < 180 ? "proc:arc150" : "proc:arc200";
            rot = sh.dir;
            const base = art === "proc:arc120" ? 62 : art === "proc:arc150" ? 78 : 98;
            sxx = syy = (sh.radius / base) * scale * (0.85 + 0.25 * u);
            y -= 14;
          } else if (sh.kind === "capsule") {
            art = "proc:streak";
            rot = Math.atan2(sh.y1 - sh.y0, sh.x1 - sh.x0);
            x = (sh.x0 + sh.x1) / 2;
            y = (sh.y0 + sh.y1) / 2 - 14;
            sxx = Math.hypot(sh.x1 - sh.x0, sh.y1 - sh.y0) / 64;
            syy = (sh.radius * 2) / 16;
          } else if (sh.kind === "circle") {
            art = "proc:ring";
            sxx = syy = (sh.radius / 62) * (0.8 + 0.3 * u);
          }
        } else if (L.align === "up") {
          y -= 10 + 40 * u;
        }
        let frame = 0;
        if (art.startsWith("fx:")) frame = Math.floor(age * 12);
        fx.push({ art, frame, x, y, rot, scaleX: sxx, scaleY: syy, alpha: 1 - u * u, tint, additive: L.additive });
        return;
      }
      case "burst": {
        if (age > L.life) return;
        const count = Math.round(param(L.count, I));
        const u = age / L.life;
        const art = L.art;
        const sizeBase = param(L.size, I);
        const spread = (L.spread * Math.PI) / 180;
        for (let i = 0; i < count; i++) {
          const r1 = rand01(ev.seed, i, li * 7 + 1);
          const r2 = rand01(ev.seed, i, li * 7 + 2);
          const r3 = rand01(ev.seed, i, li * 7 + 3);
          const ang = (L.spread >= 360 ? r1 * Math.PI * 2 : ev.dir + (r1 - 0.5) * spread) + 0;
          const sp = param(L.speed, I) * (0.45 + 1.1 * r2);
          // Closed-form motion with drag and gravity.
          const dist = L.drag > 0 ? (sp * (1 - Math.exp(-L.drag * age))) / L.drag : sp * age;
          let x = ev.x + Math.cos(ang) * dist;
          let y = ev.y - 12 + Math.sin(ang) * dist * 0.75 + 0.5 * L.gravity * GRAVITY_SCALE * age * age;
          // Debris settles on the ground.
          if (L.gravity > 0) y = Math.min(y, ev.y + 4 + r3 * 8);
          let tex = art.replace("proc:", "");
          if (tex === "gib") tex = `fx-gib-${1 + Math.floor(r3 * 6)}`;
          else if (tex === "bone") tex = `fx-bone-${1 + Math.floor(r3 * 4)}`;
          const tint = L.tint === "debris" ? hex(ev.debris?.[Math.floor(r3 * (ev.debris?.length ?? 1))] ?? "#ffffff") : L.tint ? hex(L.tint) : 0xffffff;
          if (art === "proc:ember" || art === "proc:dust") x += Math.sin(age * 3 + r3 * 6) * 4;
          particles.push({ tex, x, y, rot: tex.startsWith("fx-") ? r3 * 6 + age * 8 * (1 - u) : 0, scale: sizeBase * (0.7 + 0.6 * r3) * (art === "proc:dust" ? 1 + u : 1), alpha: (1 - u) ** (art === "proc:spark" ? 1.5 : 1), tint, additive: L.additive && !tex.startsWith("fx-") });
        }
        return;
      }
      default:
        return;
    }
  }

  /** The tier at song time s (play mode's press classes; 0 without them). */
  private tierAt(s: number): number {
    const t = this.plan.tier;
    if (!t) return 0;
    const k = countUpTo(t.s, s) - 1;
    return t.v[Math.max(0, k)];
  }

  private hud(s: number, sbi: number, beatPhase: number, barPhase: number, free: boolean, energy: number): HudState {
    const plan = this.plan;
    const hs = plan.hud;
    const kills = countUpTo(hs.kills, s);
    const xi = countUpTo(hs.xpTimes, s);
    const xp = xi > 0 ? hs.xpFill[xi - 1] : 0;
    const level = 1 + countUpTo(Float64Array.from(hs.levelUps), s);
    // Presses: the key display, the ratings so far, and the beat track.
    const ratings = { perfect: 0, great: 0, good: 0 };
    const keys: HudState["keys"] = { up: { lit: 0 }, down: { lit: 0 }, left: { lit: 0 }, right: { lit: 0 } };
    const presses: HudState["presses"] = [];
    // (The feet, "space", light for Space and for the opposite key of a two-key stomp, which releases them.)
    const lastPress: Partial<Record<KeyName | "space", { s: number; rating?: string; outline?: boolean; released?: number; via?: string }>> = {};
    let rating: string | undefined;
    let outline: boolean | undefined;
    // The heal streak, as the simulation counts it: a rated press of Great or better extends it; a lesser one or a stumble ends it.
    let streak = 0;
    for (const i of plan.inputs) {
      if (i.s > s) break;
      const partner = partnerPress(i);
      if (i.kind === "press" && i.steps && !partner) {
        if (i.stumble || i.class === "stray") streak = 0;
        else if (!i.extra) streak = i.rating === "perfect" || i.rating === "great" ? streak + 1 : 0;
      }
      if (i.kind === "release") {
        const lp = lastPress[i.key];
        // (Feet lit by a two-key stomp go out with its second key, not with Space.)
        if (lp && lp.released === undefined && (lp.via === undefined || lp.via === i.key)) lp.released = i.s;
        const feet = lastPress.space;
        if (feet?.via === i.key && feet.released === undefined) feet.released = i.s;
        continue;
      }
      // Each press lights its key in its style (a Space stomp lights the feet). A diagonal's second
      // key (or a rock step's) shows the style of the press it bent (or rocked). Only rated,
      // counted presses count.
      if (!partner) ({ rating, outline } = pressStyle(i));
      lastPress[i.key] = { s: i.s, rating, outline };
      // Two opposite keys together are one stomp: both feet too, in the first key's style.
      if (i.stomp && i.key !== "space") lastPress.space = { s: i.s, rating, outline, via: i.key };
      if (partner || i.extra) continue;
      if (i.rating) ratings[i.rating]++;
      if (s - i.s < PRESS_SHOW) presses.push({ age: s - i.s, delta: i.delta ?? 0, rating: i.rating, steps: i.steps });
    }
    const light = (lp: { rating?: string; outline?: boolean; released?: number }): KeyLight => ({
      lit: lp.released === undefined ? 1 : clamp(1 - (s - lp.released) / KEY_FADE, 0, 1),
      rating: lp.rating,
      ...(lp.outline ? { outline: true } : {}),
    });
    for (const k of Object.keys(keys) as KeyName[]) {
      const lp = lastPress[k];
      if (lp) keys[k] = light(lp);
    }
    const stomp = lastPress.space ? light(lastPress.space) : undefined;
    const ticks: HudState["ticks"] = [];
    const bs = plan.songBeats;
    for (let bi = Math.max(0, Math.floor(sbi)); bi < bs.length && bs[bi] - s <= TRACK_AHEAD; bi++) {
      if (bs[bi] < s - 0.12) continue;
      const tick: HudState["ticks"][number] = { dt: bs[bi] - s, down: this.downbeats.has(bi) };
      if (this.cleaveTimes.length) {
        if (this.near(this.cleaveTimes, bs[bi])) tick.cleave = true;
        if (this.near(this.pressHereTimes, bs[bi])) tick.pressHere = true;
      }
      ticks.push(tick);
    }
    // The heart glows over the beat before a cleave's accent, and fades just after it.
    let heartGlow: number | undefined;
    if (this.cleaveTimes.length) {
      const i = countUpTo(this.cleaveTimes, s - 0.12);
      const c = this.cleaveTimes[i];
      if (c !== undefined) {
        const beat = this.beatLenAt(this.beatIndex(plan.songBeats, c) - 0.5);
        heartGlow = s <= c ? clamp(1 - (c - s) / beat, 0, 1) : clamp(1 - (s - c) / 0.12, 0, 1);
      }
    }
    const groove = grooveAt(plan.groove, s);
    const weapons: HudState["weapons"] = [];
    for (const g of plan.weapons) {
      if (g.s > s) continue;
      const fires = hs.weaponFires[g.id];
      let flash = 0;
      if (fires) {
        const i = countUpTo(fires, s);
        if (i > 0) flash = Math.exp(-(s - fires[i - 1]) * 12);
      }
      weapons.push({ id: g.id, flash });
    }
    const banners: HudState["banners"] = [];
    let title: HudState["title"];
    let boss: HudState["boss"];
    let end: HudState["end"];
    for (const u of this.uiSorted) {
      if (u.s > s) break;
      if (u.kind === "banner" && s - u.s < u.dur) banners.push({ text: u.text, age: s - u.s, dur: u.dur });
      if (u.kind === "title" && s < u.until + 1.5) title = { age: s - u.s, remaining: u.until - s };
      if (u.kind === "bossBar" && s < u.until + 1) boss = { name: u.name, health: u.health.findLast(([t]) => t <= s)?.[1] ?? 1, age: s - u.s, remaining: u.until - s };
      if (u.kind === "end") {
        end = { age: s - u.s, averageGroove: plan.stats.averageGroove };
        if (plan.tier) {
          const own = plan.inputs.filter((i) => i.kind === "press" && i.steps);
          Object.assign(end, { loose: own.filter((i) => i.class === "counted" && !i.rating).length, strays: own.filter((i) => i.class === "stray").length, tierTime: tierTimes(plan, this.index.blastS() ?? plan.duration) });
        }
      }
    }
    const progress = clamp(s / plan.duration, 0, 1);
    // The count-in: four count marks pulse on the last four beats before step movement.
    let countIn: HudState["countIn"];
    const si = this.sectionAt(s);
    const next = plan.sections[si + 1];
    if (free && next && next.movement === "steps") {
      const left = this.beatIndex(plan.songBeats, next.s0) - sbi;
      if (left > 0 && left <= 4) countIn = { count: clamp(5 - Math.ceil(left - 1e-6), 1, 4), fade: clamp(1 - left / 4, 0, 1) };
    }
    const health = healthAt(plan.health, s);
    const hurt = hurtAt(plan.health, s);
    const heal = healAt(plan.health, s);
    const streakHud = plan.rules?.health ? { n: streak, healing: streak >= this.lib.hero.health.heal.streakFrom } : undefined;
    let tier: HudState["tier"];
    if (plan.tier) {
      const k = countUpTo(plan.tier.s, s) - 1;
      const n = plan.tier.v[Math.max(0, k)];
      const up = k > 0 && plan.tier.v[k] > plan.tier.v[k - 1];
      tier = { n, marks: this.lib.hero.meter.tiers, flash: up ? clamp(1 - (s - plan.tier.s[k]) / TIER_FLASH, 0, 1) : 0 };
    }
    return { kills, ratings, groove, bonus: this.maxBonus * groove, keys, stomp, tier, ticks, heartGlow, presses, xp, level, weapons, progress, elapsed: s, beatPhase, barPhase, free, countIn, energy, banners, title, boss, end, health, hurt, heal, streak: streakHud };
  }
}

export function facingOf(vx: number, vy: number): number {
  if (Math.abs(vx) > Math.abs(vy)) return vx > 0 ? 3 : 1;
  return vy > 0 ? 2 : 0;
}

export { beatSwell, beatHit };
