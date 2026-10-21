/** Stage 8: emit effects, sounds, gems, HUD series and UI events. */
import { Rng, hash32 } from "../core/rng.ts";
import { clamp } from "../core/shaping.ts";
import { beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import type { EffectEvent, GemEvent, HudSeries, Projectile, SlamMarker, SoundEvent, UiEvent, Chain } from "../plan/types.ts";
import { sectionIndexAt, type Ctx } from "./context.ts";
import type { KillPlan } from "./stage6-kills.ts";
import { HURTBOX_LIFT, HURTBOX_RADIUS } from "./stage6-kills.ts";
import { heroAt } from "./stage4-hero.ts";
import { cameraAt, VIEW_W } from "./stage5-camera.ts";
import { HITSTOP_CLEARANCE } from "./stage3-warp.ts";
import { resolveRef } from "./stage1-musical.ts";
import type { MusicalRef } from "../content/schema.ts";

export const VOICE_BUDGET = 4;

/** Priority order: heavy hero hits, boss, near misses, weapons, kills. */
const PRIORITY = { hero: 0, boss: 1, nearMiss: 2, weapon: 3, kill: 4, ambient: 5 } as const;

const NOTE_NAMES: Record<string, number> = { C: 0, "C#": 1, Db: 1, D: 2, "D#": 3, Eb: 3, E: 4, F: 5, "F#": 6, Gb: 6, G: 7, "G#": 8, Ab: 8, A: 9, "A#": 10, Bb: 10, B: 11 };

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
  chains: Chain[];
}

export function stage8(ctx: Ctx, kp: KillPlan): EmitResult {
  const warp = ctx.warp!;
  const hero = ctx.hero!;
  const cam = ctx.camera!;
  const lib = ctx.lib;
  const song = ctx.song;
  const hits = kp.hits;
  const effects: EffectEvent[] = [];
  const sounds: SoundEvent[] = [];
  const ui: UiEvent[] = [];
  const seedOf = (...k: (number | string)[]) => hash32(ctx.seed, ...k);

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
    sounds.push({ sample: id, s, gain: gain * sg * pal.gain, pan, rate, priority, duck });
  };

  // ------------------------------------------------------- hero actions
  const perfect: number[] = [];
  const combo: number[] = [];
  const markers: HudSeries["markers"] = [];
  for (const a of ctx.actions) {
    if (a.combo) combo.push(a.s);
    const size = a.move.weight === "heavy" ? 3 : a.move.weight === "medium" ? 2 : 1;
    markers.push({ s: a.s, size, kind: a.kind === "dodge" ? "dodge" : "attack" });
    if (a.kind === "dodge") {
      fx("dodge", a.s, a.x0, a.y0, a.dir, 0.7);
      sfx(a.move.sound ?? "dodge", a.s, a.x, PRIORITY.nearMiss, 0.7);
      continue;
    }
    if (a.kind === "blast") continue;
    const h = a.hit !== undefined ? hits[a.hit] : undefined;
    const intensity = h?.intensity ?? 0.6;
    if (a.kind !== "sweep") {
      fx(a.move.effect ?? `hit-${a.move.weight}`, a.s, a.x, a.y, a.dir, intensity, { hit: a.hit });
      if (a.move.extraEffect) fx(a.move.extraEffect, a.s, a.x, a.y, a.dir, intensity);
    }
    const key = a.move.sound ?? `hit-${a.move.weight}`;
    const heavy = a.move.weight === "heavy";
    sfx(key, a.s, a.x, heavy ? PRIORITY.hero : PRIORITY.weapon - 0.5, heavy ? 1 : 0.8, heavy);
    if (a.bossHit) sfx("boss-hit", a.s, a.x, PRIORITY.boss, 1);
    if (heavy && a.accent > 0) {
      perfect.push(a.s);
      ui.push({ kind: "perfect", s: a.s, x: a.x, y: a.y - 40 });
      fx("perfect-accent", a.s, a.x, a.y - 48, 0, 1);
    }
  }

  // ---------------------------------------------------------- hop landings
  for (const sec of ctx.sections) {
    const loco = sec.moveset.locomotion;
    if (!loco || loco.mode !== "step") continue;
    const bpb = ctx.idx.beatsPerBar[sec.song.bars[0]] ?? 2;
    const step = loco.every === "bar" ? bpb : 1;
    for (let bi = sec.b0 + step; bi < sec.b1; bi += step) {
      const s = timeOfBeatIndex(song, bi);
      const p = heroAt(hero, warp.toWorld(s));
      fx("dust", s, p.x, p.y, 0, 0.5);
    }
  }

  // ---------------------------------------------------------- threats
  const projectiles: Projectile[] = [];
  const slams: SlamMarker[] = [];
  for (const th of ctx.threats) {
    const wMiss = warp.toWorld(th.sMiss);
    const hm = heroAt(hero, wMiss);
    perfect.push(th.sMiss);
    ui.push({ kind: "perfect", s: th.sMiss, x: hm.x, y: hm.y - 44 });
    fx("near-miss", th.sMiss, hm.x, hm.y - HURTBOX_LIFT, 0, 1);
    sfx("near-miss", th.sMiss, hm.x, PRIORITY.nearMiss, 1);
    if (th.kind === "arrow") {
      const archers = kp.agents.filter((ag) => ag.def.attack === "archer" && ag.e.attacks?.some((at) => Math.abs(at.w - warp.toWorld(th.sRelease)) < 1e-6));
      for (const ag of archers) {
        const ap = ag.waypoints.find((w) => w.appointment)!;
        const wRel = warp.toWorld(th.sRelease);
        // Aim so the arrow passes 14 px from the hurtbox edge at the near miss.
        const hx = hm.x;
        const hy = hm.y - HURTBOX_LIFT;
        const ax = ap.x;
        const ay = ap.y - 24;
        const dx = hx - ax;
        const dy = hy - ay;
        const dl = Math.hypot(dx, dy) || 1;
        // Offset the target toward where the hero came from.
        const dodge = ctx.actions[th.dodgeAction!];
        const back = { x: dodge.x0 - dodge.x, y: dodge.y0 - dodge.y };
        const nx = -dy / dl;
        const ny = dx / dl;
        const side = Math.sign(nx * back.x + ny * back.y) || 1;
        const miss = HURTBOX_RADIUS + 14;
        const tx = hx + nx * side * miss;
        const ty = hy + ny * side * miss;
        const fl = Math.hypot(tx - ax, ty - ay);
        const v = fl / Math.max(0.05, wMiss - wRel);
        const ux = (tx - ax) / fl;
        const uy = (ty - ay) / fl;
        const extra = 260;
        projectiles.push({ kind: "arrow", w0: wRel, w1: wMiss + extra / v, x0: ax, y0: ay, x1: tx + ux * extra, y1: ty + uy * extra, wMiss });
        fx("arrow-release", th.sRelease, ax, ay, Math.atan2(uy, ux), 0.6);
        sfx("arrow", th.sRelease, ax, PRIORITY.nearMiss, 0.8);
      }
    } else {
      const boss = kp.agents.find((ag) => ag.def.attack === "slam");
      const dodge = ctx.actions[th.dodgeAction!];
      const px = dodge.x0;
      const py = dodge.y0;
      const d = Math.hypot(hm.x - px, hm.y - HURTBOX_LIFT - (py - HURTBOX_LIFT));
      const radius = clamp(d - HURTBOX_RADIUS - 14, 24, 70);
      slams.push({ w0: warp.toWorld(th.sRelease), w1: wMiss, x: px, y: py, radius });
      fx("boss-slam", th.sMiss, px, py, 0, 1, { scale: radius / 60 });
      sfx("boss-slam", th.sMiss, px, PRIORITY.boss, 1, true);
      void boss;
    }
  }

  // ------------------------------------------------- deaths and hits on enemies
  const killTimes: number[] = [];
  const killsBySlot = new Map<number, { s: number; x: number; n: number; big: boolean }>();
  const deathByEnemy = new Map<number, { s: number; x: number; y: number }>();
  for (const ag of kp.agents) {
    const e = ag.e;
    for (const f of e.fates) {
      const h = hits[f.hit];
      if (!f.lethal) {
        fx(h.source === "move" ? `hit-${h.weight}` : "blades-hit", h.s, f.x, f.y, h.dir, h.intensity * 0.6);
        continue;
      }
      killTimes.push(h.s);
      deathByEnemy.set(e.id, { s: h.s, x: f.x, y: f.y });
      const recipe = h.source === "ring" ? "blast-hit" : ag.def.death.recipe;
      fx(recipe, h.s, f.x, f.y, h.dir, Math.max(0.4, h.intensity), { debris: ag.def.death.debris });
      const slot = Math.round(beatIndexAt(song, h.s) * 4);
      const k = killsBySlot.get(slot) ?? { s: h.s, x: f.x, n: 0, big: false };
      k.n++;
      k.big ||= ag.def.size !== "regular";
      killsBySlot.set(slot, k);
    }
  }
  killTimes.sort((a, b) => a - b);
  for (const k of [...killsBySlot.values()].sort((a, b) => a.s - b.s)) {
    if (k.n === 1) sfx("kill", k.s, k.x, PRIORITY.kill, 0.6);
    else sfx("kill-multi", k.s, k.x, PRIORITY.kill, 0.5 + 0.22 * Math.log2(k.n));
  }

  // Weapon hits that kill: effects and sounds.
  const weaponFires: Record<string, number[]> = {};
  const firedSlots = new Set<string>();
  for (const h of hits) {
    if (h.source !== "weapon") continue;
    const wd = lib.weapons.get(h.what)!;
    const key = `${h.what}@${h.s.toFixed(4)}`;
    if (!firedSlots.has(key)) {
      firedSlots.add(key);
      (weaponFires[h.what] ??= []).push(h.s);
    }
    const hasKill = kp.fatesByHit[h.id].length > 0;
    if (wd.kind === "chain") {
      fx(wd.effect, h.s, (h.shape as { x: number }).x, (h.shape as { y: number }).y, h.dir, h.intensity);
      sfx(wd.sound, h.s, (h.shape as { x: number }).x, PRIORITY.weapon, 0.7);
    } else if (hasKill) {
      fx(wd.effect, h.s, (h.shape as { x: number }).x, (h.shape as { y: number }).y, h.dir, h.intensity);
      sfx(wd.sound, h.s, (h.shape as { x: number }).x, PRIORITY.weapon, 0.5);
    }
  }
  for (const f of ctx.formations) {
    for (const h of hits) if (h.source === "sweep" && h.section === f.section) fx("hit-sweep", h.s, (h.shape as { x: number }).x, (h.shape as { y: number }).y, h.dir, h.intensity);
  }

  // ------------------------------------------------------- moments and UI
  const levelUps: number[] = [];
  for (const g of ctx.weaponsGranted) {
    const p = heroAt(hero, warp.toWorld(g.s));
    levelUps.push(g.s);
    ui.push({ kind: "levelUp", s: g.s, weapon: g.id });
    fx("level-up", g.s, p.x, p.y - 16, 0, 1);
    sfx("level-up", g.s, p.x, PRIORITY.hero, 1, true);
  }
  if (ctx.boss) {
    const b = ctx.boss;
    const ag = kp.agents.find((a) => a.e.id === b.enemy)!;
    const land = ag.waypoints[0];
    fx("boss-land", b.sEnter, land.x, land.y, 0, 1);
    sfx("boss-land", b.sEnter, land.x, PRIORITY.boss, 1, true);
    ui.push({ kind: "bossBar", s: b.sEnter, until: b.sDeath, hits: ag.e.fates.map((f) => hits[f.hit].s) });
  }
  if (ctx.blastS !== undefined) {
    const p = heroAt(hero, warp.toWorld(ctx.blastS));
    fx("finale-blast", ctx.blastS, p.x, p.y - 16, 0, 1);
    sfx("blast", ctx.blastS, p.x, PRIORITY.hero, 1.2, true);
  }
  for (const a of song.accents) {
    if (a.strength >= 0.9) fx("accent-flash", a.time, 0, 0, 0, a.strength * sectionOf(a.time).level.intensity);
  }
  for (const sec of ctx.sections) if (sec.level.banner) ui.push({ kind: "banner", s: sec.s0, text: sec.level.banner, dur: 2.6 });
  for (const e of ctx.events) {
    if (e.ev.banner) ui.push({ kind: "banner", s: e.s, text: e.ev.banner, dur: 2.6 });
    if (e.ev.titleCard) {
      const until = resolveUntil(ctx, e.ev.titleCard.until);
      ui.push({ kind: "title", s: e.s, until });
    }
  }

  // ---------------------------------------------------------------- gems
  const hitStops = warp.hitStops();
  const nextDownbeat = (s: number) => {
    let bi = Math.ceil(beatIndexAt(song, s + 0.12));
    for (let guard = 0; guard < 64; guard++) {
      const t = timeOfBeatIndex(song, bi);
      const bar = barIndexFor(ctx, bi);
      const isDown = bar > 0 && ctx.idx.barStart[bar] === bi;
      const inStop = hitStops.some(([a, b]) => t >= a - 1e-6 && t < b + HITSTOP_CLEARANCE * 0);
      if (isDown && !inStop) return t;
      bi++;
    }
    return timeOfBeatIndex(song, bi);
  };
  const gemsRaw: { w0: number; x0: number; y0: number; s1: number; big: boolean; seed: number }[] = [];
  for (const ag of kp.agents) {
    const d = deathByEnemy.get(ag.e.id);
    if (!d) continue;
    const big = ag.def.size !== "regular";
    let s1 = nextDownbeat(d.s);
    if (ctx.blastS !== undefined && d.s >= ctx.blastS - 1e-6) {
      // The finale's gems arrive one beat after the blast.
      s1 = timeOfBeatIndex(song, beatIndexAt(song, ctx.blastS) + 1);
    }
    gemsRaw.push({ w0: warp.toWorld(d.s), x0: d.x, y0: d.y, s1, big, seed: seedOf("gem", ag.e.id) });
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
    const p = heroAt(hero, g.w1);
    fx("gem-pickup", g.s1, p.x, p.y - 16, 0, 0.7);
    sfx("gem", g.s1, p.x, PRIORITY.ambient, 0.4);
  }
  if (ctx.blastS !== undefined) ui.push({ kind: "end", s: finalArrival + 0.6 });

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
  const chains: Chain[] = kp.chainPaths.map((p) => ({ points: p }));
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
  return { effects, sounds: kept, gems, ui, hud, projectiles, slams, chains };
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

function resolveUntil(ctx: Ctx, r: MusicalRef): number {
  return resolveRef(ctx.idx, r);
}
