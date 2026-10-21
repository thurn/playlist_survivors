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
import { countUpTo, enemyPos, heroSample } from "./sample.ts";
import type { EffectEvent, EnemyRecord, Plan, PlanSection, UiEvent } from "./types.ts";

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

export interface HudState {
  kills: number;
  /** Rated presses so far. */
  ratings: { perfect: number; great: number; good: number };
  /** The groove meter (0–1) and the damage bonus it gives (0.19 is +19%). */
  groove: number;
  bonus: number;
  /** The arrow keys: how lit each one is (0–1) and the rating of the press that lit it. */
  keys: Record<KeyName, { lit: number; rating?: string }>;
  /** The beat track: upcoming beats (seconds until each) from the song, downbeats marked. */
  ticks: { dt: number; down: boolean }[];
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
  countIn?: { count: number; fade: number };
  energy: number;
  banners: { text: string; age: number; dur: number }[];
  title?: { age: number; remaining: number };
  boss?: { name: string; health: number; age: number; remaining: number };
  end?: { age: number; averageGroove: number };
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
  hero: { x: number; y: number; z: number; facing: number; anim: string; frame: number; afterimages: { x: number; y: number; facing: number; anim: string; frame: number; alpha: number }[] };
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
  private enemyById: Map<number, EnemyRecord>;
  private recipes: Map<string, EffectRecipe>;
  private uiSorted: UiEvent[];
  private downbeats: Set<number>;
  private maxBonus: number;

  constructor(
    readonly plan: Plan,
    readonly lib: ContentLibrary,
  ) {
    this.warp = new Warp(plan.warp);
    this.enemyById = new Map(plan.enemies.map((e) => [e.id, e]));
    this.recipes = lib.effects;
    this.uiSorted = plan.events.ui;
    this.downbeats = new Set(plan.barStart.filter((b) => Number.isFinite(b)));
    this.maxBonus = lib.hero.groove.maxBonus;
  }

  private walkedCache = new Map<number, Float32Array>();
  /** Distance an enemy has walked by world time w (prefix sums over its samples). */
  walked(e: EnemyRecord, w: number): number {
    let pre = this.walkedCache.get(e.id);
    const sm = this.plan.enemySamples;
    if (!pre) {
      pre = new Float32Array(Math.max(1, e.sampleCount));
      for (let i = 1; i < e.sampleCount; i++) {
        const o = (e.sampleOffset + i) * 2;
        pre[i] = pre[i - 1] + Math.hypot(sm[o] - sm[o - 2], sm[o + 1] - sm[o - 1]);
      }
      this.walkedCache.set(e.id, pre);
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
    const wbi = this.beatIndex(plan.worldBeats, w);
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
    const bucket = plan.activeEffects[clamp(Math.floor(w / plan.activeBucket), 0, plan.activeEffects.length - 1)];

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
    const ebucket = plan.activeEnemies[clamp(Math.floor(w / plan.activeBucket), 0, plan.activeEnemies.length - 1)];
    const tmp = { x: 0, y: 0 };
    const tmp2 = { x: 0, y: 0 };
    let alive = 0;
    for (let k = 0; k < ebucket.length; k++) {
      const e = this.enemyById.get(ebucket[k])!;
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
    const end = plan.hits.find((x) => x.source === "ring")?.s ?? plan.duration;
    for (const g of plan.weapons) {
      if (s < g.s || s >= end) continue;
      const wd = this.lib.weapons.get(g.id)!;
      if (wd.kind === "orbit" && wd.orbit) {
        const bpb = bar.len;
        const th0 = (Math.PI * 2 * wbi) / (wd.orbit.periodBars * bpb);
        for (let k = 0; k < wd.orbit.count; k++) {
          const th = th0 + (k * Math.PI * 2) / wd.orbit.count;
          blades.push({ x: h.x + Math.cos(th) * wd.orbit.radius, y: h.y + Math.sin(th) * wd.orbit.radius * 0.8, rot: th * 3 });
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
    for (const g of this.gemsNear(w)) {
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
    const zone = this.zoneAt(w);

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
      hero: { x: h.x, y: h.y, z: h.z, facing: h.facing, anim: h.anim, frame: h.frame, afterimages: after },
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

  private gemCache?: { w0: number; idx: number[] }[];
  private gemsNear(w: number) {
    const plan = this.plan;
    if (!this.gemCache) {
      const nb = plan.activeEnemies.length;
      this.gemCache = Array.from({ length: nb }, (_, i) => ({ w0: i * plan.activeBucket, idx: [] as number[] }));
      plan.events.gems.forEach((g, i) => {
        const b0 = clamp(Math.floor(g.w0 / plan.activeBucket), 0, nb - 1);
        const b1 = clamp(Math.floor(g.w1 / plan.activeBucket), 0, nb - 1);
        for (let b = b0; b <= b1; b++) this.gemCache![b].idx.push(i);
      });
    }
    const b = clamp(Math.floor(w / plan.activeBucket), 0, this.gemCache.length - 1);
    const out = [];
    for (const i of this.gemCache[b].idx) {
      const g = plan.events.gems[i];
      if (w >= g.w0 && w <= g.w1) out.push(g);
    }
    return out;
  }

  private decalIdx?: number[];
  private collectDecals(s: number, w: number, out: FxSprite[]) {
    const plan = this.plan;
    if (!this.decalIdx) {
      this.decalIdx = [];
      plan.events.effects.forEach((ev, i) => {
        const r = this.recipes.get(ev.recipe);
        if (r?.layers.some((l) => l.kind === "decal")) this.decalIdx!.push(i);
      });
    }
    // Binary search the newest decal at or before w, then walk back.
    const idx = this.decalIdx;
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
          if (sh.kind === "arc") {
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
    const lastPress: Partial<Record<KeyName, { s: number; rating?: string; released?: number }>> = {};
    let rating: string | undefined;
    for (const i of plan.inputs) {
      if (i.s > s) break;
      if (i.kind === "release") {
        const lp = lastPress[i.key];
        if (lp && lp.released === undefined) lp.released = i.s;
        continue;
      }
      // A diagonal's second key shows the rating of the press it bent.
      if (!i.bend) rating = i.rating;
      lastPress[i.key] = { s: i.s, rating: i.bend ? rating : i.rating };
      if (i.bend) continue;
      if (i.rating) ratings[i.rating]++;
      if (s - i.s < PRESS_SHOW) presses.push({ age: s - i.s, delta: i.delta ?? 0, rating: i.rating, steps: i.steps });
    }
    for (const k of Object.keys(keys) as KeyName[]) {
      const lp = lastPress[k];
      if (!lp) continue;
      const lit = lp.released === undefined ? 1 : clamp(1 - (s - lp.released) / KEY_FADE, 0, 1);
      keys[k] = { lit, rating: lp.rating };
    }
    const ticks: HudState["ticks"] = [];
    const bs = plan.songBeats;
    for (let bi = Math.max(0, Math.floor(sbi)); bi < bs.length && bs[bi] - s <= TRACK_AHEAD; bi++) {
      if (bs[bi] < s - 0.12) continue;
      ticks.push({ dt: bs[bi] - s, down: this.downbeats.has(bi) });
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
      if (u.kind === "end") end = { age: s - u.s, averageGroove: plan.stats.averageGroove };
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
    return { kills, ratings, groove, bonus: this.maxBonus * groove, keys, ticks, presses, xp, level, weapons, progress, elapsed: s, beatPhase, barPhase, free, countIn, energy, banners, title, boss, end };
  }
}

export function facingOf(vx: number, vy: number): number {
  if (Math.abs(vx) > Math.abs(vy)) return vx > 0 ? 3 : 1;
  return vy > 0 ? 2 : 0;
}

export { beatSwell, beatHit };
