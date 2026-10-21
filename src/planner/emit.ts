/**
 * Emission: effects, sounds, gems, HUD series and UI events, produced from the
 * simulation's events. The sound voice budget is enforced as sounds are
 * emitted, which needs no future knowledge.
 */
import { Rng, hash32 } from "../core/rng.ts";
import { clamp } from "../core/shaping.ts";
import { beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import type { Warp } from "../core/warp.ts";
import type { CameraTrack, EffectEvent, GemEvent, HeroTrack, HudSeries, Projectile, SlamMarker, SoundEvent, UiEvent } from "../plan/types.ts";
import { heroSample } from "../plan/sample.ts";
import type { SimResult } from "../sim/simulate.ts";
import { sectionIndexAt, type Ctx } from "./context.ts";
import { cameraAt, VIEW_W } from "./camera.ts";
import { resolveRef } from "./musical.ts";

export const VOICE_BUDGET = 4;

/** Priority order: heavy hero hits, boss, near misses, weapons, kills. */
const PRIORITY = { hero: 0, boss: 1, nearMiss: 2, weapon: 3, kill: 4, ambient: 5 } as const;

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

export function emit(ctx: Ctx, sim: SimResult, warp: Warp, hero: HeroTrack, cam: CameraTrack): EmitResult {
  const lib = ctx.lib;
  const song = ctx.song;
  const hits = sim.hits;
  const effects: EffectEvent[] = [];
  const sounds: SoundEvent[] = [];
  const ui: UiEvent[] = [];
  const seedOf = (...k: (number | string)[]) => hash32(ctx.seed, ...k);
  const heroAt = (w: number) => heroSample({ hero } as never, w);

  const fx = (recipe: string, s: number, x: number, y: number, dir: number, intensity: number, extra: Partial<EffectEvent> = {}) => {
    if (!lib.effects.has(recipe)) return;
    effects.push({ recipe, s, w: warp.toWorld(s), x, y, dir, intensity, seed: seedOf("fx", recipe, effects.length), ...extra });
  };
  const sectionOf = (s: number) => ctx.sections[sectionIndexAt(ctx, s)];

  // ---------------------------------------------------------------- sounds
  const sfx = (key: string, s: number, x: number, priority: number, gain = 1, duck = false) => {
    const sec = sectionOf(s);
    const pal = lib.soundPalettes.get(sec.level.sfxPalette);
    if (!pal) return;
    const list = pal.sounds[key];
    if (!list) return;
    const r = new Rng(ctx.seed, "snd", key, Math.round(s * 1000), sounds.length);
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
    const c = cameraAt(cam, warp.toWorld(s));
    const pan = clamp((x - c.x) / (VIEW_W / 2 / c.zoom), -0.8, 0.8);
    // Start slow-attack samples early so they are heard on time.
    const lead = Math.min(MAX_SOUND_LEAD, (ctx.soundOnsets[id] ?? 0) / rate);
    sounds.push({ sample: id, s, gain: gain * sg * pal.gain, pan, rate, priority, duck, ...(lead > 0.001 ? { lead } : {}) });
  };

  // ------------------------------------------------------------ swings
  const perfect: number[] = [];
  const markers: HudSeries["markers"] = [];
  for (const sw of sim.swings) {
    const h = hits[sw.hit];
    const heavy = sw.kind === "heavy";
    markers.push({ s: sw.s, size: heavy ? 3 : 1, kind: "attack" });
    fx(`hit-${sw.kind}`, sw.s, sw.x, sw.y, sw.dir, h.intensity, { hit: sw.hit });
    sfx("swing", sw.s - 0.04, sw.x, PRIORITY.hero + 0.5, 0.7);
    sfx(`hit-${sw.kind}`, sw.s, sw.x, heavy ? PRIORITY.hero : PRIORITY.hero + 0.3, heavy ? 1.1 : 0.95, heavy);
    if (heavy && sw.accent > 0) {
      perfect.push(sw.s);
      ui.push({ kind: "perfect", s: sw.s, x: sw.x, y: sw.y - 40 });
      fx("perfect-accent", sw.s, sw.x, sw.y - 48, 0, 1);
    }
  }
  const combo = sim.inputs.filter((i) => i.kind === "press" && !i.bend).map((i) => i.s);

  // ---------------------------------------------------------- step landings
  for (const l of sim.landings) {
    fx("dust", l.s, l.x, l.y, 0, 0.5, { w: l.w });
    sfx("step", l.s, l.x, PRIORITY.ambient, 0.25);
  }

  // ------------------------------------------------- deaths and hits on enemies
  const killTimes: number[] = [];
  const killsBySlot = new Map<number, { s: number; x: number; n: number; big: boolean }>();
  const deathByEnemy = new Map<number, { s: number; x: number; y: number }>();
  for (const e of sim.enemies) {
    const def = lib.enemies.get(e.type)!;
    for (const f of e.fates) {
      const h = hits[f.hit];
      if (!f.lethal) {
        fx(h.source === "move" ? `hit-${h.weight}` : "blades-hit", h.s, f.x, f.y, h.dir, h.intensity * 0.6);
        continue;
      }
      killTimes.push(h.s);
      deathByEnemy.set(e.id, { s: h.s, x: f.x, y: f.y });
      const recipe = h.source === "ring" ? "blast-hit" : def.death.recipe;
      fx(recipe, h.s, f.x, f.y, h.dir, Math.max(0.4, h.intensity), { debris: def.death.debris });
      const slot = Math.round(beatIndexAt(song, h.s) * 4);
      const k = killsBySlot.get(slot) ?? { s: h.s, x: f.x, n: 0, big: false };
      k.n++;
      k.big ||= def.size !== "regular";
      killsBySlot.set(slot, k);
    }
  }
  killTimes.sort((a, b) => a - b);
  for (const k of [...killsBySlot.values()].sort((a, b) => a.s - b.s)) {
    if (k.n === 1) sfx("kill", k.s, k.x, PRIORITY.kill, 0.6);
    else sfx("kill-multi", k.s, k.x, PRIORITY.kill, 0.5 + 0.22 * Math.log2(k.n));
  }

  // Weapon hits: effects and sounds where they connect.
  const weaponFires: Record<string, number[]> = {};
  for (const f of sim.weaponFires) (weaponFires[f.weapon] ??= []).push(f.s);
  const caught = new Set<number>();
  for (const e of sim.enemies) for (const f of e.fates) caught.add(f.hit);
  for (const h of hits) {
    if (h.source !== "weapon") continue;
    const wd = lib.weapons.get(h.what)!;
    const at = h.shape as { x: number; y: number };
    if (wd.kind === "chain") {
      fx(wd.effect, h.s, at.x, at.y, h.dir, h.intensity);
      sfx(wd.sound, h.s, at.x, PRIORITY.weapon, 0.7);
    } else if (caught.has(h.id)) {
      fx(wd.effect, h.s, at.x, at.y, h.dir, h.intensity);
      sfx(wd.sound, h.s, at.x, PRIORITY.weapon, 0.5);
    }
  }

  // ------------------------------------------------------- moments and UI
  const levelUps: number[] = [];
  for (const g of sim.weaponsGranted) {
    const p = heroAt(warp.toWorld(g.s));
    levelUps.push(g.s);
    ui.push({ kind: "levelUp", s: g.s, weapon: g.id });
    fx("level-up", g.s, p.x, p.y - 16, 0, 1);
    sfx("level-up", g.s, p.x, PRIORITY.hero, 1, true);
  }
  if (sim.blastS !== undefined) {
    const p = heroAt(warp.toWorld(sim.blastS));
    fx("finale-blast", sim.blastS, p.x, p.y - 16, 0, 1);
    sfx("blast", sim.blastS, p.x, PRIORITY.hero, 1.2, true);
  }
  for (const a of song.accents) {
    if (a.strength >= 0.9) fx("accent-flash", a.time, 0, 0, 0, a.strength * sectionOf(a.time).level.intensity);
  }
  for (const sec of ctx.sections) if (sec.level.banner) ui.push({ kind: "banner", s: sec.s0, text: sec.level.banner, dur: 2.6 });
  for (const e of ctx.events) {
    if (e.ev.banner) ui.push({ kind: "banner", s: e.s, text: e.ev.banner, dur: 2.6 });
    if (e.ev.titleCard) ui.push({ kind: "title", s: e.s, until: resolveRef(ctx.idx, e.ev.titleCard.until) });
  }

  // ---------------------------------------------------------------- gems
  const hitStops = sim.hitStops;
  const nextDownbeat = (s: number) => {
    let bi = Math.ceil(beatIndexAt(song, s + 0.12));
    for (let guard = 0; guard < 64; guard++) {
      const t = timeOfBeatIndex(song, bi);
      const bar = barIndexFor(ctx, bi);
      const isDown = bar > 0 && ctx.idx.barStart[bar] === bi;
      // A downbeat where a hit-stop starts is fine: the gems land on the impact itself.
      const inStop = hitStops.some(([a, b]) => t > a + 1e-6 && t < b);
      if (isDown && !inStop) return t;
      bi++;
    }
    return timeOfBeatIndex(song, bi);
  };
  const gemsRaw: { w0: number; x0: number; y0: number; s1: number; big: boolean; seed: number }[] = [];
  for (const e of sim.enemies) {
    const d = deathByEnemy.get(e.id);
    if (!d) continue;
    const big = lib.enemies.get(e.type)!.size !== "regular";
    let s1 = nextDownbeat(d.s);
    if (sim.blastS !== undefined && d.s >= sim.blastS - 1e-6) {
      // The finale's gems arrive one beat after the blast.
      s1 = timeOfBeatIndex(song, beatIndexAt(song, sim.blastS) + 1);
    }
    gemsRaw.push({ w0: warp.toWorld(d.s), x0: d.x, y0: d.y, s1, big, seed: seedOf("gem", e.id) });
  }
  gemsRaw.sort((a, b) => a.s1 - b.s1 || a.w0 - b.w0);
  // XP segments end at each level-up and at the finale gems' arrival.
  const segEnds = [...levelUps];
  const finalArrival = gemsRaw.length ? gemsRaw[gemsRaw.length - 1].s1 : ctx.duration;
  segEnds.push(finalArrival);
  const segOf = (s: number) => {
    for (let i = 0; i < segEnds.length; i++) if (s <= segEnds[i] + 1e-6) return i;
    return segEnds.length - 1;
  };
  const segTotals = segEnds.map(() => 0);
  for (const g of gemsRaw) segTotals[segOf(g.s1)] += g.big ? 5 : 1;
  const gems: GemEvent[] = [];
  const xpTimes: number[] = [];
  const xpFill: number[] = [];
  const segAcc = segEnds.map(() => 0);
  for (const g of gemsRaw) {
    const seg = segOf(g.s1);
    const v = (g.big ? 5 : 1) / Math.max(1, segTotals[seg]);
    segAcc[seg] += v;
    gems.push({ w0: g.w0, x0: g.x0, y0: g.y0, s1: g.s1, w1: warp.toWorld(g.s1), value: v, big: g.big, seed: g.seed });
    xpTimes.push(g.s1);
    xpFill.push(Math.min(1, segAcc[seg]));
  }
  // Gem pickup sparkles, one per arrival slot.
  const arrivals = new Set<number>();
  for (const g of gems) {
    if (arrivals.has(g.s1)) continue;
    arrivals.add(g.s1);
    const p = heroAt(g.w1);
    fx("gem-pickup", g.s1, p.x, p.y - 16, 0, 0.7);
    sfx("gem", g.s1, p.x, PRIORITY.ambient, 0.4);
  }
  if (sim.blastS !== undefined) ui.push({ kind: "end", s: finalArrival + 1.6 });

  // --------------------------------------------------- voice budget
  const bySlot = new Map<number, SoundEvent[]>();
  for (const snd of sounds) {
    const slot = Math.floor(beatIndexAt(song, snd.s) * 4 + 0.5);
    let l = bySlot.get(slot);
    if (!l) bySlot.set(slot, (l = []));
    l.push(snd);
  }
  const kept: SoundEvent[] = [];
  for (const l of bySlot.values()) {
    l.sort((a, b) => a.priority - b.priority || b.gain - a.gain);
    kept.push(...l.slice(0, VOICE_BUDGET));
  }
  kept.sort((a, b) => a.s - b.s || a.priority - b.priority);

  effects.sort((a, b) => a.w - b.w || a.s - b.s);
  ui.sort((a, b) => a.s - b.s);
  const hud: HudSeries = {
    kills: Float64Array.from(killTimes),
    combo: Float64Array.from(combo.sort((a, b) => a - b)),
    perfect: Float64Array.from(perfect.sort((a, b) => a - b)),
    xpTimes: Float64Array.from(xpTimes),
    xpFill: Float32Array.from(xpFill),
    markers: markers.sort((a, b) => a.s - b.s),
    levelUps,
    weaponFires: Object.fromEntries(Object.entries(weaponFires).map(([k, v]) => [k, Float64Array.from(v.sort((a, b) => a - b))])),
  };
  const projectiles: Projectile[] = [];
  const slams: SlamMarker[] = [];
  return { effects, sounds: kept, gems, ui, hud, projectiles, slams };
}

function barIndexFor(ctx: Ctx, bi: number): number {
  let lo = 1;
  let hi = ctx.idx.barCount;
  if (bi < ctx.idx.barStart[1]) return 0;
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (ctx.idx.barStart[mid] <= bi) lo = mid;
    else hi = mid - 1;
  }
  return lo;
}
