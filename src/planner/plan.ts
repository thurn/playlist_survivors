/**
 * The planner: runs the simulated playthrough and records it as a plan. A pure
 * function of the song map, level script, content, seed and skill profile:
 * the same inputs always produce the same plan hash.
 *
 * 1. Resolve musical time and the song schedule.
 * 2. Generate the map (the route length comes from the pace schedule).
 * 3. Run the simulation with the virtual player.
 * 4. Record the camera from the simulated hero (the camera runs inside the simulation).
 * 5. Emit effects, sounds, gems and HUD data.
 */
import { DEFAULT_PROPS, generateMap, type LandmarkPlacement, type TiledMap, type PropInfo } from "../mapgen/mapgen.ts";
import { openFieldLayout, stampLandmark, WAVE_BANDS, type LandmarkStamp, type ZoneWave } from "../mapgen/openfield.ts";
import { barOfBeatIndex, beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import type { MapLayout, OpenFieldRecord, Plan, PlanStats } from "../plan/types.ts";
import { hash32 } from "../core/rng.ts";
import { Warp } from "../core/warp.ts";
import { beatDurationAt, makeCtx, sectionIndexAt, type Ctx, type PlannerInput } from "./context.ts";
import { resolveEvents } from "./musical.ts";
import { emit } from "./emit.ts";
import { recipeLife } from "../plan/effects-life.ts";
import { grooveAt } from "../plan/evaluate.ts";
import { buildSchedule, paceAt, type GridSlot, type Schedule } from "../sim/schedule.ts";
import { ENEMY_RATE, simulate, type SimResult } from "../sim/simulate.ts";
import { MasherPlayer, VirtualPlayer, type Player, type SkillProfile } from "../sim/player.ts";

export const PLAN_FORMAT = "2";
export const ACTIVE_BUCKET = 0.5;
/** Enemies stay drawable this long after death (the fall animation). */
export const DEATH_ANIM = 0.55;

export interface PlannerOptions {
  /** World atlas prop facts for the map generator. */
  props?: Record<string, PropInfo>;
  /** Clock for timing only (performance.now); never affects output. */
  now?: () => number;
  /** The virtual player's skill profile (default skilled). */
  profile?: SkillProfile;
  /** Mash instead of playing on the beat: presses per second (see MasherPlayer). */
  mashRate?: number;
}

export interface SimRun {
  ctx: Ctx;
  sch: Schedule;
  map: MapLayout;
  sim: SimResult;
}

/** The song schedule and the map: everything the simulation needs before it starts. */
export function prepare(input: PlannerInput, props?: Record<string, PropInfo>): { ctx: Ctx; sch: Schedule; map: MapLayout } {
  const ctx = makeCtx(input);
  resolveEvents(ctx);
  const sch = buildSchedule(ctx);
  // The open field has no route: its ground is generated in chunks as it is seen.
  if (ctx.level.world === "open") return { ctx, sch, map: openFieldLayout(ctx.level.zones) };
  // The route length comes from the pace schedule in song time, plus a margin for hit-stops.
  const zoneExt = new Map<string, { d0: number; d1: number }>();
  for (const sec of ctx.sections) {
    const d0 = paceAt(sch, sec.s0);
    const d1 = paceAt(sch, sec.s1);
    const z = zoneExt.get(sec.level.zone);
    if (z) z.d1 = d1;
    else zoneExt.set(sec.level.zone, { d0, d1 });
  }
  const zones = ctx.level.zones.filter((z) => zoneExt.has(z)).map((id) => ({ id, ...zoneExt.get(id)! }));
  const landmarks: LandmarkPlacement[] = [];
  for (const e of ctx.events) {
    if (!e.ev.landmark) continue;
    const map = input.landmarks?.[e.ev.landmark] as TiledMap | undefined;
    if (!map) continue;
    landmarks.push({ id: e.ev.landmark, d: paceAt(sch, e.s), map });
  }
  const map = generateMap({ seed: ctx.seed, routeLength: paceAt(sch, ctx.duration) + 400, zones, themes: ctx.lib.zones, landmarks, props });
  return { ctx, sch, map };
}

/** The parts of a plan that the song, the level and the map decide before play: a live recording has them too. */
export function planHeader(ctx: Ctx, sch: Schedule, map: MapLayout, profile: string) {
  return {
    version: `${PLAN_FORMAT}:${ctx.input.version ?? "dev"}`,
    seed: ctx.seed,
    song: ctx.song.song,
    duration: ctx.duration,
    songBeats: Float64Array.from(ctx.song.beats),
    energy: Float32Array.from(ctx.song.energy),
    barStart: ctx.idx.barStart,
    map,
    sections: ctx.sections.map((s) => ({
      label: s.label,
      population: s.level.population,
      s0: s.s0,
      s1: s.s1,
      zone: s.level.zone,
      feel: s.song.feel,
      movement: s.level.movement,
      intensity: s.level.intensity,
      sfxPalette: s.level.sfxPalette,
      key: s.song.key,
      banner: s.level.banner,
    })),
    accents: ctx.song.accents.map((a) => ({ s: a.time, strength: a.strength, punch: ctx.sections[sectionIndexAt(ctx, a.time)].level.camera.punchOnAccents })),
    title: ctx.level.title,
    profile,
    pace: { rate: 120, d: Float32Array.from(sch.pace) },
    grid: Float64Array.from(sch.strikeTimes),
    cleaveAccents: sch.gridSlots.filter((sl) => cleaves(ctx, sl)).map((sl) => ({ s: sl.s, strength: sl.accent })),
    levelUpsDue: sch.levelUps.map((l) => ({ id: l.id, s: l.s })),
    ...(ctx.level.rules.oneStepPerSlot || ctx.level.rules.accentTelegraphs ? { rules: ctx.level.rules } : {}),
  } satisfies Partial<Plan>;
}

/** Does a grid slot's accent make a cleave of a swing armed by a press rated Great or better? */
export function cleaves(ctx: Ctx, slot: GridSlot): boolean {
  const rule = ctx.sections[slot.section].level.heavyOnAccents;
  return slot.kind === "grid" && slot.accent > 0 && slot.accent >= rule.minStrength && (rule.label === undefined || rule.label === slot.accentLabel);
}

/** The virtual player for a profile. */
export function virtualPlayer(ctx: Ctx, sch: Schedule, profile: SkillProfile = "skilled"): VirtualPlayer {
  return new VirtualPlayer(
    ctx.lib.hero,
    ctx.seed,
    sch.slots,
    ctx.sections.map((s) => ({ steps: s.level.movement === "steps", presses: s.level.presses, s0: s.s0, s1: s.s1, pace: s.level.pace })),
    ctx.song.beats,
    profile,
    (slot) => cleaves(ctx, slot),
    { open: ctx.level.world === "open" },
  );
}

/** A player who mashes at `rate` presses per second, steering like the virtual player for `profile`. */
export function masherPlayer(ctx: Ctx, sch: Schedule, rate: number, profile: SkillProfile = "moderate"): MasherPlayer {
  const sections = ctx.sections.map((s) => ({ steps: s.level.movement === "steps", presses: s.level.presses, s0: s.s0, s1: s.s1, pace: s.level.pace }));
  return new MasherPlayer(virtualPlayer(ctx, sch, profile), sections, ctx.seed, rate);
}

/** Run the simulation only (no emission): for the causality and determinism checks. */
export function runSim(input: PlannerInput, opts: { props?: Record<string, PropInfo>; profile?: SkillProfile; player?: Player; cutS?: number; untilS?: number } = {}): SimRun {
  const { ctx, sch, map } = prepare(input, opts.props);
  const player = opts.player ?? virtualPlayer(ctx, sch, opts.profile ?? input.profile ?? input.level.player);
  const sim = simulate(ctx, sch, map, { player, cutS: opts.cutS, untilS: opts.untilS });
  return { ctx, sch, map, sim };
}

export function plan(input: PlannerInput, opts: PlannerOptions = {}): Plan {
  return planWithSim(input, opts).plan;
}

/** The plan and the simulation it records (the causality check compares simulations). */
export function planWithSim(input: PlannerInput, opts: PlannerOptions = {}): { plan: Plan; sim: SimResult } {
  const now = opts.now ?? (() => 0);
  const t0 = now();
  const profile = opts.profile ?? input.profile ?? input.level.player;
  const { ctx, sch, map } = prepare(input, opts.props);
  const tSim = now();
  const player = opts.mashRate ? masherPlayer(ctx, sch, opts.mashRate, profile) : virtualPlayer(ctx, sch, profile);
  const sim = simulate(ctx, sch, map, { player });
  const simMs = now() - tSim;
  const warp = new Warp(sim.warp);
  const wEnd = warp.toWorld(ctx.duration);
  const hero = sim.hero;
  const camera = sim.camera.track();
  const emitted = emit(ctx, sim, warp, hero, camera);

  // Active-entity index: per bucket of world time.
  const nb = Math.ceil((wEnd + 2) / ACTIVE_BUCKET) + 1;
  const enemyBuckets: number[][] = Array.from({ length: nb }, () => []);
  for (const e of sim.enemies) {
    const b0 = Math.max(0, Math.floor(e.spawnW / ACTIVE_BUCKET));
    const end = Number.isFinite(e.deathW) ? e.deathW + (e.recycled ? 0 : DEATH_ANIM) : wEnd + 2;
    const b1 = Math.min(nb - 1, Math.floor(end / ACTIVE_BUCKET));
    for (let b = b0; b <= b1; b++) enemyBuckets[b].push(e.id);
  }
  const effectBuckets: number[][] = Array.from({ length: nb }, () => []);
  emitted.effects.forEach((ev, i) => {
    const life = recipeLife(ctx.lib.effects.get(ev.recipe)!);
    const wSongEnd = warp.toWorld(ev.s + life.song);
    const end = Math.max(ev.w + life.world, wSongEnd);
    const b0 = Math.max(0, Math.floor(ev.w / ACTIVE_BUCKET));
    const b1 = Math.min(nb - 1, Math.floor(end / ACTIVE_BUCKET));
    for (let b = b0; b <= b1; b++) effectBuckets[b].push(i);
  });

  // Stats.
  const killsPerSection: Record<string, number> = {};
  for (const s of Array.from(emitted.hud.kills)) {
    const lab = ctx.sections[sectionIndexAt(ctx, s)].label;
    killsPerSection[lab] = (killsPerSection[lab] ?? 0) + 1;
  }
  let peak = 0;
  for (let b = 0; b < nb; b++) {
    const w = (b + 0.5) * ACTIVE_BUCKET;
    let n = 0;
    for (const id of enemyBuckets[b]) {
      const e = sim.enemies[id];
      if (w >= e.spawnW && w < e.deathW) n++;
    }
    peak = Math.max(peak, n);
  }
  const presses = sim.inputs.filter((i) => i.kind === "press" && !i.bend && !i.extra);
  const ratings = { perfect: 0, great: 0, good: 0, none: 0 };
  for (const p of presses) if (p.steps) ratings[p.rating ?? "none"]++;
  let gSum = 0;
  let gN = 0;
  for (const sec of ctx.sections) {
    if (sec.level.movement !== "steps") continue;
    for (let s = sec.s0; s < Math.min(sec.s1, sim.blastS ?? Infinity); s += 0.25) {
      gSum += grooveAt(sim.groove, s);
      gN++;
    }
  }
  const stats: PlanStats = {
    peakPopulation: peak,
    killsPerSection,
    totalHitStop: sim.hitStops.reduce((a, h) => a + (h[1] - h[0]), 0),
    enemies: sim.enemies.length,
    hits: sim.hits.length,
    swings: sim.swings.length,
    presses: presses.length,
    ratings,
    averageGroove: gN > 0 ? gSum / gN : 0,
    planMs: now() - t0,
    simMs,
  };

  const recorded: Plan = {
    ...planHeader(ctx, sch, map, opts.mashRate ? `masher-${opts.mashRate}` : profile),
    warp: sim.warp,
    worldBeats: Float64Array.from(ctx.song.beats, (s) => warp.toWorld(s)),
    hero,
    camera,
    hits: sim.hits,
    enemies: sim.enemies,
    enemySamples: sim.enemySamples,
    enemySampleRate: ENEMY_RATE,
    events: { effects: emitted.effects, sounds: emitted.sounds, gems: emitted.gems, ui: emitted.ui },
    projectiles: emitted.projectiles,
    slams: emitted.slams,
    chains: sim.chains,
    activeBucket: ACTIVE_BUCKET,
    activeEnemies: enemyBuckets.map((b) => Uint32Array.from(b)),
    activeEffects: effectBuckets.map((b) => Uint32Array.from(b)),
    hud: emitted.hud,
    weapons: sim.weaponsGranted,
    swings: sim.swings,
    inputs: sim.inputs,
    groove: { s: Float64Array.from(sim.groove.s), v: Float32Array.from(sim.groove.v) },
    nearMisses: sim.threats.filter((t) => t.outcome === "near-miss").map((t) => ({ s: t.s, w: t.w, kind: t.kind })),
    threats: sim.threats.map((t) => ({ s: t.s, kind: t.kind, outcome: t.outcome })),
    boss: sim.boss && sch.boss ? { id: sim.boss.id, deathS: sim.boss.deathS } : undefined,
    formations: sim.formations.map((f) => ({ def: f.def, sRelease: f.sRelease, members: f.members })),
    stats,
    ...(ctx.level.world === "open" ? { open: openFieldRecord(ctx, map, sim, opts.props) } : {}),
  };
  return { plan: recorded, sim };
}

/** The open field's transform waves and landmarks, from the simulation's records. */
export function openFieldRecord(ctx: Ctx, map: MapLayout, sim: Pick<SimResult, "zoneWaves" | "landmarks">, props?: Record<string, PropInfo>): OpenFieldRecord {
  const zoneIndex = (id: string) => Math.max(0, map.zones.findIndex((z) => z.id === id));
  const firstZone = zoneIndex(ctx.sections[0].level.zone);
  let prev = firstZone;
  const waves: ZoneWave[] = sim.zoneWaves.map((z) => {
    const sbi = beatIndexAt(ctx.song, z.s);
    const wv: ZoneWave = { s: z.s, w: z.w, x: z.x, y: z.y, sbi, prev, zone: zoneIndex(z.zone), endS: timeOfBeatIndex(ctx.song, sbi + WAVE_BANDS / 4) };
    prev = wv.zone;
    return wv;
  });
  const info = { ...DEFAULT_PROPS, ...(props ?? {}) };
  const landmarks: LandmarkStamp[] = [];
  for (const l of sim.landmarks) {
    const tm = ctx.input.landmarks?.[l.id] as TiledMap | undefined;
    if (!tm) continue;
    const st = stampLandmark(tm, l.x, l.y, info);
    // It rises over one bar.
    const sbi = beatIndexAt(ctx.song, l.s);
    const bar = Math.max(1, barOfBeatIndex(ctx.idx, sbi + 1e-6));
    const len = (ctx.idx.beatsPerBar[bar] ?? 2) * beatDurationAt(ctx, l.s);
    landmarks.push({ id: l.id, s: l.s, w: l.w, x: l.x, y: l.y, props: st.props, clearRadius: st.clearRadius, rise: len });
  }
  return { firstZone, waves, landmarks };
}

/** A stable hash of the whole plan, for determinism checks and caching. */
export function planHash(p: Plan): string {
  let h = 0x12345678;
  const mixArr = (a: ArrayLike<number>) => {
    for (let i = 0; i < a.length; i++) h = hash32(h, Math.round(a[i] * 1000));
  };
  h = hash32(h, p.version, p.seed, p.song, p.profile);
  for (const b of p.warp) h = hash32(h, Math.round(b.s * 1e6), Math.round(b.w * 1e6), Math.round(b.rate * 1e6));
  mixArr(p.hero.x);
  mixArr(p.hero.y);
  mixArr(p.hero.anim);
  mixArr(p.hero.frame);
  mixArr(p.camera.x);
  mixArr(p.camera.zoom);
  mixArr(p.enemySamples);
  for (const e of p.enemies) h = hash32(h, e.type, Math.round(e.spawnW * 1e4), Math.round((Number.isFinite(e.deathW) ? e.deathW : -1) * 1e4), e.fates.length);
  for (const i of p.inputs) h = hash32(h, i.kind, i.key, Math.round(i.s * 1e5));
  for (const sw of p.swings) h = hash32(h, Math.round(sw.s * 1e5), Math.round(sw.dir * 1e4), sw.hit);
  for (const ev of p.events.effects) h = hash32(h, ev.recipe, Math.round(ev.w * 1e4), Math.round(ev.x), ev.seed);
  for (const ev of p.events.sounds) h = hash32(h, ev.sample, Math.round(ev.s * 1e5), Math.round(ev.gain * 100));
  h = hash32(h, p.map.hash);
  return (h >>> 0).toString(16).padStart(8, "0");
}
