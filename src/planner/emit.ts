/**
 * Emission: effects, sounds, gems, HUD series and UI events, produced from the
 * simulation's events. The sound voice budget is enforced as sounds are
 * emitted, which needs no future knowledge.
 *
 * Each kind of simulation event has its own emission method on Emitter. The
 * batch `emit()` calls them over a whole recording in the demo's fixed order;
 * play mode's live recording calls them as events happen.
 */
import { Rng, hash32 } from "../core/rng.ts";
import { clamp } from "../core/shaping.ts";
import { beatIndexAt } from "../core/songmap.ts";
import type { Warp } from "../core/warp.ts";
import type { CameraTrack, EffectEvent, EnemyRecord, Fate, GemEvent, HeroTrack, Hit, HudSeries, PlanSwing, Projectile, SlamMarker, SoundEvent, UiEvent } from "../plan/types.ts";
import { grooveAt } from "../plan/evaluate.ts";
import { heroSample } from "../plan/sample.ts";
import type { ArrowRecord, BossInfo, Landing, SimResult, SlamRecord, Threat } from "../sim/recorder.ts";
import { HURTBOX_LIFT } from "../sim/simulate.ts";
import { sectionIndexAt, type Ctx } from "./context.ts";
import { cameraAt, VIEW_W } from "./camera.ts";
import { resolveRef } from "./musical.ts";

export const VOICE_BUDGET = 4;

/** Priority order: heavy hero hits, boss, near misses, weapons, kills. */
export const PRIORITY = { hero: 0, boss: 1, nearMiss: 2, weapon: 3, kill: 4, ambient: 5 } as const;

const NOTE_NAMES: Record<string, number> = { C: 0, "C#": 1, Db: 1, D: 2, "D#": 3, Eb: 3, E: 4, F: 5, "F#": 6, Gb: 6, G: 7, "G#": 8, Ab: 8, A: 9, "A#": 10, Bb: 10, B: 11 };

/** The most a sample is started ahead of its event; the audio scheduler looks this far ahead. */
export const MAX_SOUND_LEAD = 0.25;

export function noteToMidi(note: string): number | undefined {
  const m = /^([A-G][#b]?)(-?\d)$/.exec(note.trim());
  if (!m) return undefined;
  return 12 * (Number(m[2]) + 1) + NOTE_NAMES[m[1]];
}

/** Pitch classes of a key such as "D major" or "A minor". */
export function keyScale(key: string | undefined): number[] | undefined {
  if (!key) return undefined;
  const m = /^([A-G][#b]?)\s+(major|minor)$/i.exec(key.trim());
  if (!m) return undefined;
  const root = NOTE_NAMES[m[1]];
  const steps = m[2].toLowerCase() === "major" ? [0, 2, 4, 5, 7, 9, 11] : [0, 2, 3, 5, 7, 8, 10];
  return steps.map((x) => (root + x) % 12);
}

export interface EmitResult {
  effects: EffectEvent[];
  sounds: SoundEvent[];
  gems: GemEvent[];
  ui: UiEvent[];
  hud: HudSeries;
  projectiles: Projectile[];
  slams: SlamMarker[];
}

/** Where emitted events go: plain arrays for the batch, a rewindable recording in play mode. */
export interface EmitSink {
  effect(e: EffectEvent): void;
  sound(snd: SoundEvent): void;
  ui(u: UiEvent): void;
  /** Effects emitted so far (the demo's effect seeds count them). */
  readonly effectCount: number;
  readonly soundCount: number;
}

/** A kill, for grouping kill sounds per sixteenth note. */
export interface Kill {
  s: number;
  x: number;
  big: boolean;
  /** The sixteenth-note slot it is grouped in. */
  slot: number;
}

export class Emitter {
  /**
   * @param stableSeeds Seed effects and sound picks from what they are (recipe, time, place)
   *   instead of how many came before: play mode emits in event order, and replays must match.
   */
  constructor(
    readonly ctx: Ctx,
    readonly warp: Warp,
    private readonly cam: () => CameraTrack,
    readonly sink: EmitSink,
    readonly stableSeeds = false,
  ) {}

  private seedOf(...k: (number | string)[]): number {
    return hash32(this.ctx.seed, ...k);
  }

  sectionOf(s: number) {
    return this.ctx.sections[sectionIndexAt(this.ctx, s)];
  }

  fx(recipe: string, s: number, x: number, y: number, dir: number, intensity: number, extra: Partial<EffectEvent> = {}): void {
    if (!this.ctx.lib.effects.has(recipe)) return;
    const seed = this.stableSeeds ? this.seedOf("fx", recipe, Math.round(s * 1e4), Math.round(x * 4), Math.round(y * 4), extra.hit ?? -1) : this.seedOf("fx", recipe, this.sink.effectCount);
    this.sink.effect({ recipe, s, w: this.warp.toWorld(s), x, y, dir, intensity, seed, ...extra });
  }

  sfx(key: string, s: number, x: number, priority: number, gain = 1, duck = false): void {
    const ctx = this.ctx;
    const sec = this.sectionOf(s);
    const pal = ctx.lib.soundPalettes.get(sec.level.sfxPalette);
    if (!pal) return;
    const list = pal.sounds[key];
    if (!list) return;
    const r = new Rng(ctx.seed, "snd", key, Math.round(s * 1000), this.stableSeeds ? Math.round(x * 4) + 7919 * Math.round(priority * 10) : this.sink.soundCount);
    const pick = r.pick(list);
    const id = typeof pick === "string" ? pick : pick.id;
    const root = typeof pick === "string" ? undefined : pick.rootNote;
    const sg = typeof pick === "string" ? 1 : (pick.gain ?? 1);
    let rate = Math.pow(2, r.range(-1, 1) / 12);
    const scale = keyScale(sec.song.key);
    if (pal.tuned && root && scale) {
      const rm = noteToMidi(root);
      if (rm !== undefined) {
        const options: number[] = [];
        for (let n = rm - 5; n <= rm + 7; n++) if (scale.includes(((n % 12) + 12) % 12)) options.push(n);
        const target = options.length ? r.pick(options) : rm;
        rate = Math.pow(2, (target - rm) / 12);
      }
    }
    const c = cameraAt(this.cam(), this.warp.toWorld(s));
    const pan = clamp((x - c.x) / (VIEW_W / 2 / c.zoom), -0.8, 0.8);
    // Start slow-attack samples early so they are heard on time.
    const lead = Math.min(MAX_SOUND_LEAD, (ctx.soundOnsets[id] ?? 0) / rate);
    this.sink.sound({ sample: id, s, gain: gain * sg * pal.gain, pan, rate, priority, duck, ...(lead > 0.001 ? { lead } : {}) });
  }

  /** A swing's strike. Returns whether it earned "PERFECT" (a Perfect press striking on an accent). */
  swing(sw: PlanSwing, h: Hit, groove: number, pressRating: string | undefined): boolean {
    const heavy = sw.kind === "heavy";
    // The sword's trail brightens with the groove.
    this.fx(`hit-${sw.kind}`, sw.s, sw.x, sw.y, sw.dir, Math.min(1, h.intensity * (0.55 + 0.45 * groove) + 0.25 * groove), { hit: sw.hit });
    this.sfx("swing", sw.s - 0.04, sw.x, PRIORITY.hero + 0.5, 0.7);
    this.sfx(`hit-${sw.kind}`, sw.s, sw.x, heavy ? PRIORITY.hero : PRIORITY.hero + 0.3, heavy ? 1.1 : 0.95, heavy);
    if (sw.accent > 0 && pressRating === "perfect") {
      this.sink.ui({ kind: "perfect", s: sw.s, x: sw.x, y: sw.y - 40 });
      this.fx("perfect-accent", sw.s, sw.x, sw.y - 48, 0, 1);
      return true;
    }
    return false;
  }

  landing(l: Landing): void {
    this.fx("dust", l.s, l.x, l.y, 0, 0.5, { w: l.w });
    this.sfx("step", l.s, l.x, PRIORITY.ambient, 0.25);
  }

  /** A hit on an enemy: a flash, or its death. Returns the kill for the kill sounds. */
  fate(e: EnemyRecord, f: Fate, h: Hit): Kill | undefined {
    const def = this.ctx.lib.enemies.get(e.type)!;
    if (!f.lethal) {
      this.fx(h.source === "move" ? `hit-${h.weight}` : "blades-hit", h.s, f.x, f.y, h.dir, h.intensity * 0.6);
      return undefined;
    }
    const recipe = h.source === "ring" ? "blast-hit" : def.death.recipe;
    this.fx(recipe, h.s, f.x, f.y, h.dir, Math.max(0.4, h.intensity), { debris: def.death.debris });
    // Kill sounds group per sixteenth note; in the slow, quiet free movement, per beat.
    const free = this.sectionOf(h.s).level.movement === "free";
    const slot = free ? Math.round(beatIndexAt(this.ctx.song, h.s)) * 4 : Math.round(beatIndexAt(this.ctx.song, h.s) * 4);
    return { s: h.s, x: f.x, big: def.size !== "regular", slot };
  }

  /** The kill sound for one slot's kills. */
  killSound(k: { s: number; x: number; n: number }): void {
    // The slow, quiet free movement keeps to the short single-kill sound: a ringing multi-kill would fill its silences.
    if (k.n === 1 || this.sectionOf(k.s).level.movement === "free") this.sfx("kill", k.s, k.x, PRIORITY.kill, 0.6);
    else this.sfx("kill-multi", k.s, k.x, PRIORITY.kill, 0.5 + 0.22 * Math.log2(k.n));
  }

  /** A weapon hit: effects and sounds where it connects (`caught`: it hit an enemy). */
  weaponHit(h: Hit, caught: boolean): void {
    const wd = this.ctx.lib.weapons.get(h.what)!;
    const at = h.shape as { x: number; y: number };
    if (wd.kind === "chain") {
      this.fx(wd.effect, h.s, at.x, at.y, h.dir, h.intensity);
      this.sfx(wd.sound, h.s, at.x, PRIORITY.weapon, 0.7);
    } else if (caught) {
      this.fx(wd.effect, h.s, at.x, at.y, h.dir, h.intensity);
      this.sfx(wd.sound, h.s, at.x, PRIORITY.weapon, 0.5);
    }
  }

  levelUp(g: { id: string; s: number }, p: { x: number; y: number }): void {
    this.sink.ui({ kind: "levelUp", s: g.s, weapon: g.id });
    this.fx("level-up", g.s, p.x, p.y - 16, 0, 1);
    this.sfx("level-up", g.s, p.x, PRIORITY.hero, 1, true);
  }

  blast(s: number, p: { x: number; y: number }): void {
    this.fx("finale-blast", s, p.x, p.y - 16, 0, 1);
    this.sfx("blast", s, p.x, PRIORITY.hero, 1.2, true);
  }

  accent(a: { time: number; strength: number }): void {
    if (a.strength >= 0.9) this.fx("accent-flash", a.time, 0, 0, 0, a.strength * this.sectionOf(a.time).level.intensity);
  }

  /** Banners and the title card: song-owned, known before play starts. */
  scriptUi(): void {
    const ctx = this.ctx;
    for (const sec of ctx.sections) if (sec.level.banner) this.sink.ui({ kind: "banner", s: sec.s0, text: sec.level.banner, dur: 2.6 });
    for (const e of ctx.events) {
      if (e.ev.banner) this.sink.ui({ kind: "banner", s: e.s, text: e.ev.banner, dur: 2.6 });
      if (e.ev.titleCard) this.sink.ui({ kind: "title", s: e.s, until: resolveRef(ctx.idx, e.ev.titleCard.until) });
    }
  }

  /** A gem as the evaluator draws it. */
  gem(g: { w0: number; x0: number; y0: number; s1: number; w1: number; value: number; big: boolean; enemy: number }): GemEvent {
    return { w0: g.w0, x0: g.x0, y0: g.y0, s1: g.s1, w1: g.w1, value: g.value, big: g.big, seed: this.seedOf("gem", g.enemy) };
  }

  /** The first gem to arrive at a time: the pickup sparkle and chime. */
  gemArrival(s1: number, p: { x: number; y: number }): void {
    this.fx("gem-pickup", s1, p.x, p.y - 16, 0, 0.7);
    this.sfx("gem", s1, p.x, PRIORITY.ambient, 0.4);
  }

  /** An arrow's release (and its shatter, once it has touched the hero). */
  arrowRelease(a: ArrowRecord): void {
    this.fx("arrow-release", a.sRelease, a.x0, a.y0, Math.atan2(a.ay - a.y0, a.ax - a.x0), 0.6);
    this.sfx("arrow", a.sRelease, a.x0, PRIORITY.nearMiss, 0.8);
  }

  arrowShatter(a: ArrowRecord): void {
    // An arrow that touches the hero shatters in a spark: no damage, no other penalty.
    if (a.outcome === "touch") this.fx("arrow-shatter", a.sArrive, a.x1, a.y1, 0, 0.8);
  }

  slam(sl: SlamRecord): void {
    this.fx("boss-slam", sl.sLand, sl.x, sl.y, 0, 1, { scale: sl.radius / 60 });
    this.sfx("boss-slam", sl.sLand, sl.x, PRIORITY.boss, 1, true);
  }

  /** The near-miss juice: a whoosh, a ring and a "PERFECT" callout (the slow-motion dip is in the warp). */
  nearMiss(t: Threat): void {
    this.sink.ui({ kind: "perfect", s: t.s, x: t.x, y: t.y - 44 });
    this.fx("near-miss", t.s, t.x, t.y - HURTBOX_LIFT, 0, 1);
    this.sfx("near-miss", t.s, t.x, PRIORITY.nearMiss, 1);
  }

  bossLand(b: Pick<BossInfo, "landS" | "x" | "y">): void {
    this.fx("boss-land", b.landS, b.x, b.y, 0, 1);
    this.sfx("boss-land", b.landS, b.x, PRIORITY.boss, 1, true);
  }

  /** The boss's health bar. */
  bossBar(b: BossInfo, until: number): UiEvent {
    const bossEvent = this.ctx.events.find((e) => e.ev.boss);
    const name = bossEvent?.ev.banner ?? bossEvent?.ev.boss?.type.toUpperCase() ?? "BOSS";
    return { kind: "bossBar", s: b.landS, until, health: b.health.map((h) => [h.s, h.frac] as [number, number]), name };
  }

  /** The boss-kill effect on whatever swing kills it. */
  bossKill(h: Hit): void {
    const at = h.shape.kind === "arc" || h.shape.kind === "circle" || h.shape.kind === "ring" ? h.shape : { x: 0, y: 0 };
    this.fx("hit-heavy", h.s, at.x, at.y, h.dir, 1, { hit: h.source === "move" ? h.id : undefined });
    this.fx("hit-burst", h.s, at.x, at.y, h.dir, 1);
    this.sfx("finisher", h.s, at.x, PRIORITY.hero, 1.2, true);
  }

  /** A cleave shockwave band: a crescent on its sixteenth note. */
  waveBand(h: Hit): void {
    const sh = h.shape as Extract<typeof h.shape, { kind: "arc" }>;
    this.fx("shockwave", h.s, sh.x, sh.y, h.dir, 0.8, { hit: h.id });
  }
}

/** The sixteenth-note slot a sound belongs to, for the voice budget. */
export function soundSlot(ctx: Ctx, s: number): number {
  return Math.floor(beatIndexAt(ctx.song, s) * 4 + 0.5);
}

/** The voice budget for one slot's sounds: the most important few. */
export function budgetSlot(l: SoundEvent[]): SoundEvent[] {
  l.sort((a, b) => a.priority - b.priority || b.gain - a.gain);
  return l.slice(0, VOICE_BUDGET);
}

/** Plain arrays, for the batch. */
class ArraySink implements EmitSink {
  readonly effects: EffectEvent[] = [];
  readonly sounds: SoundEvent[] = [];
  readonly uis: UiEvent[] = [];
  effect(e: EffectEvent): void {
    this.effects.push(e);
  }
  sound(snd: SoundEvent): void {
    this.sounds.push(snd);
  }
  ui(u: UiEvent): void {
    this.uis.push(u);
  }
  get effectCount(): number {
    return this.effects.length;
  }
  get soundCount(): number {
    return this.sounds.length;
  }
}

export function emit(ctx: Ctx, sim: SimResult, warp: Warp, hero: HeroTrack, cam: CameraTrack): EmitResult {
  const song = ctx.song;
  const hits = sim.hits;
  const sink = new ArraySink();
  const em = new Emitter(ctx, warp, () => cam, sink);
  const heroAt = (w: number) => heroSample({ hero } as never, w);

  // ------------------------------------------------------------ swings
  const perfect: number[] = [];
  const levelUps: number[] = [];
  for (const sw of sim.swings) {
    const g = grooveAt({ s: sim.groove.s, v: sim.groove.v }, sw.s);
    const pressRating = sw.press !== undefined ? sim.inputs[sw.press].rating : undefined;
    if (em.swing(sw, hits[sw.hit], g, pressRating)) perfect.push(sw.s);
  }

  // ---------------------------------------------------------- step landings
  for (const l of sim.landings) em.landing(l);

  // ------------------------------------------------- deaths and hits on enemies
  const killTimes: number[] = [];
  const killsBySlot = new Map<number, { s: number; x: number; n: number; big: boolean }>();
  for (const e of sim.enemies) {
    for (const f of e.fates) {
      const kill = em.fate(e, f, hits[f.hit]);
      if (!kill) continue;
      killTimes.push(kill.s);
      const k = killsBySlot.get(kill.slot) ?? { s: kill.s, x: kill.x, n: 0, big: false };
      k.n++;
      k.big ||= kill.big;
      killsBySlot.set(kill.slot, k);
    }
  }
  killTimes.sort((a, b) => a - b);
  for (const k of [...killsBySlot.values()].sort((a, b) => a.s - b.s)) em.killSound(k);

  // Weapon hits: effects and sounds where they connect.
  const weaponFires: Record<string, number[]> = {};
  for (const f of sim.weaponFires) (weaponFires[f.weapon] ??= []).push(f.s);
  const caught = new Set<number>();
  for (const e of sim.enemies) for (const f of e.fates) caught.add(f.hit);
  for (const h of hits) if (h.source === "weapon") em.weaponHit(h, caught.has(h.id));

  // ------------------------------------------------------- moments and UI
  for (const g of sim.weaponsGranted) {
    levelUps.push(g.s);
    em.levelUp(g, heroAt(warp.toWorld(g.s)));
  }
  if (sim.blastS !== undefined) em.blast(sim.blastS, heroAt(warp.toWorld(sim.blastS)));
  for (const a of song.accents) em.accent(a);
  em.scriptUi();

  // ---------------------------------------------------------------- gems
  // Gems fly to the hero; the XP bar fills from the ones collected (the simulation levels up).
  const gems: GemEvent[] = sim.gems.map((g) => em.gem(g));
  const arrivals = new Set<number>();
  for (const g of gems) {
    if (arrivals.has(g.s1) || g.w1 === 0) continue;
    arrivals.add(g.s1);
    em.gemArrival(g.s1, heroAt(g.w1));
  }
  const finalArrival = gems.length ? Math.max(...gems.map((g) => g.s1)) : ctx.duration;
  if (sim.blastS !== undefined) sink.ui({ kind: "end", s: finalArrival + 1.6 });
  const xpTimes = sim.xp.map((x) => x.s);
  const xpFill = sim.xp.map((x) => x.fill);

  // ------------------------------------------------------------- threats
  const projectiles: Projectile[] = [];
  for (const a of sim.arrows) {
    if (a.w1 === 0) continue;
    projectiles.push({ kind: "arrow", w0: a.wRelease, w1: a.w1, x0: a.x0, y0: a.y0, x1: a.x1, y1: a.y1, wMiss: a.wArrive });
    em.arrowRelease(a);
    em.arrowShatter(a);
  }
  const slams: SlamMarker[] = [];
  for (const sl of sim.slams) {
    slams.push({ w0: sl.wMark, w1: sl.wLand, x: sl.x, y: sl.y, radius: sl.radius });
    em.slam(sl);
  }
  for (const t of sim.threats) {
    if (t.outcome !== "near-miss") continue;
    perfect.push(t.s);
    em.nearMiss(t);
  }

  // ---------------------------------------------------------------- the boss
  if (sim.boss) {
    const b = sim.boss;
    em.bossLand(b);
    sink.ui(em.bossBar(b, b.deathS ?? sim.blastS ?? ctx.duration));
    if (b.deathS !== undefined && b.killHit !== undefined) em.bossKill(hits[b.killHit]);
  }
  // Cleave shockwaves: a crescent per band, on its sixteenth note.
  for (const wv of sim.waves) for (const id of wv.hits) em.waveBand(hits[id]);

  // --------------------------------------------------- voice budget
  const bySlot = new Map<number, SoundEvent[]>();
  for (const snd of sink.sounds) {
    const slot = soundSlot(ctx, snd.s);
    let l = bySlot.get(slot);
    if (!l) bySlot.set(slot, (l = []));
    l.push(snd);
  }
  const kept: SoundEvent[] = [];
  for (const l of bySlot.values()) kept.push(...budgetSlot(l));
  kept.sort((a, b) => a.s - b.s || a.priority - b.priority);

  const effects = sink.effects;
  const ui = sink.uis;
  effects.sort((a, b) => a.w - b.w || a.s - b.s);
  ui.sort((a, b) => a.s - b.s);
  const hud: HudSeries = {
    kills: Float64Array.from(killTimes),
    perfect: Float64Array.from(perfect.sort((a, b) => a - b)),
    xpTimes: Float64Array.from(xpTimes),
    xpFill: Float32Array.from(xpFill),
    levelUps,
    weaponFires: Object.fromEntries(Object.entries(weaponFires).map(([k, v]) => [k, Float64Array.from(v.sort((a, b) => a - b))])),
  };
  return { effects, sounds: kept, gems, ui, hud, projectiles, slams };
}
