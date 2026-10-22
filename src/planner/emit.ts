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
import { beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import type { Warp } from "../core/warp.ts";
import type { CameraTrack, EffectEvent, EnemyRecord, Fate, GemEvent, HeroTrack, Hit, HudSeries, PlanSwing, Projectile, SlamMarker, SoundEvent, UiEvent } from "../plan/types.ts";
import { grooveAt } from "../plan/evaluate.ts";
import { heroSample } from "../plan/sample.ts";
import type { ArrowRecord, BossInfo, Landing, LandmarkRecord, SimResult, SlamRecord, Threat, ZoneWaveRecord } from "../sim/recorder.ts";
import type { TiledMap } from "../mapgen/mapgen.ts";
import { stampLandmark } from "../mapgen/openfield.ts";
import { HURTBOX_LIFT } from "../sim/simulate.ts";
import { firstAtOrAfter } from "../sim/timing.ts";
import { sectionIndexAt, type Ctx } from "./context.ts";
import { cameraAt, VIEW_W } from "./camera.ts";
import { resolveRef } from "./musical.ts";

export const VOICE_BUDGET = 4;

/** Priority order: heavy hero hits, boss, near misses, weapons, kills. */
export const PRIORITY = { hero: 0, boss: 1, nearMiss: 2, weapon: 3, kill: 4, ambient: 5 } as const;

const NOTE_NAMES: Record<string, number> = { C: 0, "C#": 1, Db: 1, D: 2, "D#": 3, Eb: 3, E: 4, F: 5, "F#": 6, Gb: 6, G: 7, "G#": 8, Ab: 8, A: 9, "A#": 10, Bb: 10, B: 11 };

/** The most a sample is started ahead of its event; the audio scheduler looks this far ahead. */
export const MAX_SOUND_LEAD = 0.25;

/**
 * Never late (rules.gridSounds): a sound plays exactly on its slot only when it is known at least
 * this long before it; one known later moves to a later slot of its subdivision.
 */
export const NEVER_LATE = 0.06;

/** The grid a sound keeps to: the timing grid, or a subdivision of the song map's beats (also in free sections). */
export type Subdivision = "grid" | "sixteenth" | "eighth" | "beat" | "bar";

/**
 * What the emitter needs to keep sounds on the grid (rules.gridSounds): the timing grid, and the
 * song time of the latest key event at or before a song time (a press or release there can still
 * change what a sound after it is, so the sound is known only from then).
 */
export interface SoundTiming {
  grid: ArrayLike<number>;
  lastKeyAt(s: number): number;
}

/** How one sound keeps to the grid: its subdivision, and when it was known (undefined: fixed in advance). */
export interface SoundPlacement {
  every?: Subdivision;
  known?: number;
  /** Already placed (a kill group): the slot it moved from. */
  movedFrom?: number;
  /** A pan and a seed position of its own (a sound fixed in advance must not change with the hero). */
  pan?: number;
  seedX?: number;
}

/** The latest of sorted key-event times at or before s (-Infinity when none). */
export function lastAtOrBefore(times: ArrayLike<{ s: number }>, s: number): number {
  let lo = 0;
  let hi = times.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (times[mid].s <= s + 1e-9) lo = mid + 1;
    else hi = mid;
  }
  return lo > 0 ? times[lo - 1].s : -Infinity;
}

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
  /** Grid sounds: the slot its sound plays on, and the one it would have played on when it was known too late for it. */
  soundS?: number;
  movedFrom?: number;
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
    /** Grid sounds (rules.gridSounds): the timing grid and the key events. */
    readonly timing?: SoundTiming,
  ) {}

  /** Are sounds kept on the grid (play mode's gridSounds)? */
  get onGrid(): boolean {
    return this.timing !== undefined && this.ctx.level.rules.gridSounds;
  }

  /** The first slot of a subdivision at or after song time t (the timing grid's are beats in a free section and past its end). */
  slotAt(every: Subdivision, t: number): number {
    const song = this.ctx.song;
    if (every === "grid") {
      const g = this.timing!.grid;
      const i = firstAtOrAfter(g, t - 1e-6);
      if (this.sectionOf(t).level.movement !== "free" && i < g.length) return g[i];
      every = "beat";
    }
    const bi = beatIndexAt(song, t);
    if (every === "bar") {
      const starts = this.ctx.idx.barStart;
      for (let b = 1; b < starts.length; b++) if (starts[b] >= bi - 1e-6) return timeOfBeatIndex(song, starts[b]);
      return t;
    }
    const per = every === "sixteenth" ? 4 : every === "eighth" ? 2 : 1;
    return timeOfBeatIndex(song, Math.ceil(bi * per - 1e-6) / per);
  }

  /**
   * Where a sound caused at song time s plays: the first slot of its subdivision at or after s,
   * unless it was known less than NEVER_LATE before that slot; then the first slot at least
   * NEVER_LATE after it was known (and the slot it moved from).
   */
  place(every: Subdivision, s: number, known?: number): { s: number; movedFrom?: number } {
    const slot = this.slotAt(every, s);
    if (known === undefined || slot - known >= NEVER_LATE - 1e-9) return { s: slot };
    return { s: this.slotAt(every, known + NEVER_LATE), movedFrom: slot };
  }

  /** When a sound caused at song time s is known: from the latest key event at or before it. */
  knownAt(s: number): number {
    return this.timing!.lastKeyAt(s);
  }

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

  /** A sound effect. `panW`: pan it by the camera sample at or before this world time (not between samples). */
  sfx(key: string, s: number, x: number, priority: number, gain = 1, duck = false, rateScale = 1, panW?: number, at: SoundPlacement = {}): void {
    const ctx = this.ctx;
    // Grid sounds: on its subdivision's slot, never late.
    let movedFrom = at.movedFrom;
    if (this.onGrid && at.every) {
      const p = this.place(at.every, s, at.known);
      s = p.s;
      movedFrom ??= p.movedFrom;
    }
    if (this.onGrid && at.seedX !== undefined) x = at.seedX;
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
    rate *= rateScale;
    const cam = this.cam();
    const k = panW === undefined ? -1 : Math.max(0, Math.min(cam.x.length - 1, Math.floor((panW - cam.w0) * cam.rate + 1e-9)));
    const c = k < 0 ? cameraAt(cam, this.warp.toWorld(s)) : { x: cam.x[k], zoom: cam.zoom[k] };
    const pan = this.onGrid && at.pan !== undefined ? at.pan : clamp((x - c.x) / (VIEW_W / 2 / c.zoom), -0.8, 0.8);
    // Start slow-attack samples early so they are heard on time.
    const lead = Math.min(MAX_SOUND_LEAD, (ctx.soundOnsets[id] ?? 0) / rate);
    this.sink.sound({ sample: id, s, gain: gain * sg * pal.gain, pan, rate, priority, duck, ...(lead > 0.001 ? { lead } : {}), ...(this.onGrid ? { key, ...(movedFrom !== undefined ? { movedFrom } : {}) } : {}) });
  }

  /**
   * A tier change: a rising chime for a tier-up, a soft falling one for a tier-down, on the grid
   * time s, centred (panned by the camera sample at or before the change, world time w). With grid
   * sounds it is known at the change (song time `at`): one less than 60 ms before s waits a grid time.
   */
  tierChime(s: number, up: boolean, w: number, at?: number): void {
    const cam = this.cam();
    const k = Math.max(0, Math.min(cam.x.length - 1, Math.floor((w - cam.w0) * cam.rate + 1e-9)));
    this.sfx(up ? "tier-up" : "tier-down", s, cam.x[k], PRIORITY.hero + 0.6, up ? 0.8 : 0.5, false, 1, w, { every: "grid", known: at });
  }

  /** A swing's strike. Returns whether it earned "PERFECT" (a Perfect press striking on an accent). */
  swing(sw: PlanSwing, h: Hit, groove: number, pressRating: string | undefined): boolean {
    const heavy = sw.kind === "heavy";
    // The sword's trail brightens with the groove.
    this.fx(`hit-${sw.kind}`, sw.s, sw.x, sw.y, sw.dir, Math.min(1, h.intensity * (0.55 + 0.45 * groove) + 0.25 * groove), { hit: sw.hit });
    // (With grid sounds both are fixed at the wind-up, where the hero was then: a press after it can't
    // change them. The whoosh before the impact is its lead-in, exempt from the grid.)
    const fixed = this.onGrid && sw.aim;
    const x = fixed ? sw.aim.x : sw.x;
    const panW = fixed ? this.warp.toWorld(sw.sWind) : undefined;
    this.sfx("swing", sw.s - 0.04, x, PRIORITY.hero + 0.5, 0.7, false, 1, panW);
    // (Its strike is its slot: a grid time, or a cleave's accent.)
    this.sfx(`hit-${sw.kind}`, sw.s, x, heavy ? PRIORITY.hero : PRIORITY.hero + 0.3, heavy ? 1.1 : 0.95, heavy, 1, panW);
    if (sw.accent > 0 && pressRating === "perfect") {
      this.sink.ui({ kind: "perfect", s: sw.s, x: sw.x, y: sw.y - 40 });
      this.fx("perfect-accent", sw.s, sw.x, sw.y - 48, 0, 1);
      return true;
    }
    return false;
  }

  landing(l: Landing): void {
    // A stomp stamps a ring of dust, with no sound: a press can't be relied on to land on the grid.
    if (l.stomp) {
      this.fx("stomp", l.s, l.x, l.y, 0, 1, { w: l.w });
      return;
    }
    this.fx("dust", l.s, l.x, l.y, 0, 0.5, { w: l.w });
    // (Panned by the camera sample before the landing: in live play the one after may not exist yet.)
    this.sfx("step", l.s, l.x, PRIORITY.ambient, 0.25, false, 1, l.w);
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
    if (this.onGrid) {
      // On the first slot at or after the kill, or a later one when the kill was known too late for it.
      const p = this.place(free ? "beat" : "sixteenth", h.s, this.knownAt(h.s));
      const bi = beatIndexAt(this.ctx.song, p.s);
      return { s: h.s, soundS: p.s, x: f.x, big: def.size !== "regular", slot: free ? Math.round(bi) * 4 : Math.round(bi * 4), ...(p.movedFrom !== undefined ? { movedFrom: p.movedFrom } : {}) };
    }
    const slot = free ? Math.round(beatIndexAt(this.ctx.song, h.s)) * 4 : Math.round(beatIndexAt(this.ctx.song, h.s) * 4);
    return { s: h.s, x: f.x, big: def.size !== "regular", slot };
  }

  /** The kill sound for one slot's kills (with grid sounds already on its slot: `movedFrom` when a kill in it was known too late for its own). */
  killSound(k: { s: number; x: number; n: number; movedFrom?: number }): void {
    const at: SoundPlacement = k.movedFrom !== undefined ? { movedFrom: k.movedFrom } : {};
    // The slow, quiet free movement keeps to the short single-kill sound: a ringing multi-kill would fill its silences.
    if (k.n === 1 || this.sectionOf(k.s).level.movement === "free") this.sfx("kill", k.s, k.x, PRIORITY.kill, 0.6, false, 1, undefined, at);
    else this.sfx("kill-multi", k.s, k.x, PRIORITY.kill, 0.5 + 0.22 * Math.log2(k.n), false, 1, undefined, at);
  }

  /** A weapon hit: effects and sounds where it connects (`caught`: it hit an enemy). */
  weaponHit(h: Hit, caught: boolean): void {
    const wd = this.ctx.lib.weapons.get(h.what)!;
    const at = h.shape as { x: number; y: number };
    // Grid sounds: on the weapon's subdivision (a chain's jump step), known from the latest key event before the hit.
    const grid: SoundPlacement = this.onGrid ? { every: weaponSubdivision(wd), known: this.knownAt(h.s) } : {};
    if (wd.kind === "chain") {
      this.fx(wd.effect, h.s, at.x, at.y, h.dir, h.intensity);
      this.sfx(wd.sound, h.s, at.x, PRIORITY.weapon, 0.7, false, 1, undefined, grid);
    } else if (caught) {
      this.fx(wd.effect, h.s, at.x, at.y, h.dir, h.intensity);
      this.sfx(wd.sound, h.s, at.x, PRIORITY.weapon, 0.5, false, 1, undefined, grid);
    }
  }

  levelUp(g: { id: string; s: number }, p: { x: number; y: number }): void {
    this.sink.ui({ kind: "levelUp", s: g.s, weapon: g.id });
    this.fx("level-up", g.s, p.x, p.y - 16, 0, 1);
    // (With grid sounds it is centred, so that where the hero is can't change it: it is known in advance, on its downbeat.)
    this.sfx("level-up", g.s, p.x, PRIORITY.hero, 1, true, 1, undefined, { every: "bar", pan: 0, seedX: 0 });
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
    // (With grid sounds on the next sixteenth, known from the latest key event before the arrival.)
    this.sfx("gem", s1, p.x, PRIORITY.ambient, 0.4, false, 1, undefined, this.onGrid ? { every: "sixteenth", known: this.knownAt(s1) } : {});
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
    this.sfx("near-miss", t.s, t.x, PRIORITY.nearMiss, 1, false, 1, undefined, this.onGrid ? { every: "beat", known: this.knownAt(t.s) } : {});
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

  /** A transform wave: a low sweep on the downbeat, under the music. */
  zoneWave(z: ZoneWaveRecord): void {
    this.sfx("zone-sweep", z.s, z.x, PRIORITY.ambient, 0.45, false, 0.5);
  }

  /** A landmark rises from the ground: dust at each of its props on the downbeat. */
  landmark(l: LandmarkRecord): void {
    const tm = this.ctx.input.landmarks?.[l.id] as TiledMap | undefined;
    if (!tm) return;
    for (const p of stampLandmark(tm, l.x, l.y).props) this.fx("dust", l.s, p.x, p.y, 0, 1);
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

export function emit(ctx: Ctx, sim: SimResult, warp: Warp, hero: HeroTrack, cam: CameraTrack, grid?: ArrayLike<number>): EmitResult {
  const song = ctx.song;
  const hits = sim.hits;
  const sink = new ArraySink();
  // (Grid sounds: the timing grid, and the key events, sorted by time.)
  const keys = sim.inputs;
  // (Grid sounds seed sound picks as live play does, from what they are: the batch and the live recording pick the same.)
  const em = new Emitter(ctx, warp, () => cam, sink, ctx.level.rules.gridSounds, grid ? { grid, lastKeyAt: (s) => lastAtOrBefore(keys, s) } : undefined);
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
  // Tier changes (play mode's press classes).
  for (const c of sim.tierChimes) em.tierChime(c.s, c.up, c.w, c.at);

  // ------------------------------------------------- deaths and hits on enemies
  const killTimes: number[] = [];
  const killsBySlot = new Map<number, { s: number; x: number; n: number; big: boolean; movedFrom?: number }>();
  for (const e of sim.enemies) {
    for (const f of e.fates) {
      const kill = em.fate(e, f, hits[f.hit]);
      if (!kill) continue;
      killTimes.push(kill.s);
      const k = killsBySlot.get(kill.slot) ?? { s: kill.soundS ?? kill.s, x: kill.x, n: 0, big: false };
      k.n++;
      k.big ||= kill.big;
      if (kill.movedFrom !== undefined) k.movedFrom = Math.min(k.movedFrom ?? Infinity, kill.movedFrom);
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
  // The open field's transform waves and landmarks.
  for (const z of sim.zoneWaves) em.zoneWave(z);
  for (const l of sim.landmarks) em.landmark(l);

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

/** The subdivision a weapon's sound keeps to: its firing cadence, or for a chain its jump step. */
export function weaponSubdivision(wd: { kind: string; fires?: { every: string }; chain?: { step: string } }): Subdivision {
  const every = wd.kind === "chain" && wd.chain ? wd.chain.step : (wd.fires?.every ?? "sixteenth");
  return every === "eighth" || every === "beat" || every === "bar" || every === "sixteenth" ? every : "sixteenth";
}
