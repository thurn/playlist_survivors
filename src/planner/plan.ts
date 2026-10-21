/**
 * The planner: a pure function from song map, level script and content to a
 * complete plan. Deterministic: the same inputs and seed always produce the
 * same plan hash.
 */
import { generateMap, type LandmarkPlacement, type TiledMap, type PropInfo } from "../mapgen/mapgen.ts";
import type { Plan, PlanStats } from "../plan/types.ts";
import { hash32 } from "../core/rng.ts";
import { makeCtx, sectionIndexAt, type PlannerInput } from "./context.ts";
import { stage1 } from "./stage1-musical.ts";
import { stage2 } from "./stage2-actions.ts";
import { stage3, progressAt, planHitStops } from "./stage3-warp.ts";
import { stage4 } from "./stage4-hero.ts";
import { stage5 } from "./stage5-camera.ts";
import { stage6 } from "./stage6-kills.ts";
import { simulateCrowd, SAMPLE_RATE } from "./stage7-crowd.ts";
import { stage8 } from "./stage8-emit.ts";
import { recipeLife } from "../plan/effects-life.ts";

export const PLAN_FORMAT = "1";
export const ACTIVE_BUCKET = 0.5;
/** Enemies stay drawable this long after death (the fall animation). */
export const DEATH_ANIM = 0.55;

export interface PlannerOptions {
  /** World atlas prop facts for the map generator. */
  props?: Record<string, PropInfo>;
  /** Clock for timing only (performance.now); never affects output. */
  now?: () => number;
}

export function plan(input: PlannerInput, opts: PlannerOptions = {}): Plan {
  const now = opts.now ?? (() => 0);
  const t0 = now();
  const ctx = makeCtx(input);
  stage1(ctx);
  stage2(ctx);
  stage3(ctx);

  // Map generation: needs the route schedule.
  const warp = ctx.warp!;
  const zoneExt = new Map<string, { d0: number; d1: number }>();
  for (const sec of ctx.sections) {
    const d0 = progressAt(ctx, warp.toWorld(sec.s0));
    const d1 = progressAt(ctx, warp.toWorld(sec.s1));
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
    landmarks.push({ id: e.ev.landmark, d: progressAt(ctx, warp.toWorld(e.s)), map });
  }
  const wEnd = warp.toWorld(ctx.duration);
  ctx.map = generateMap({ seed: ctx.seed, routeLength: progressAt(ctx, wEnd) + 400, zones, themes: ctx.lib.zones, landmarks, props: opts.props });

  stage4(ctx);
  stage5(ctx);
  const kp = stage6(ctx);
  const crowd = simulateCrowd(ctx, kp);
  const emitted = stage8(ctx, kp);

  // Active-entity index: per bucket of world time.
  const nb = Math.ceil((wEnd + 2) / ACTIVE_BUCKET) + 1;
  const enemyBuckets: number[][] = Array.from({ length: nb }, () => []);
  for (const e of crowd.enemies) {
    const b0 = Math.max(0, Math.floor(e.spawnW / ACTIVE_BUCKET));
    const b1 = Math.min(nb - 1, Math.floor((e.deathW + DEATH_ANIM) / ACTIVE_BUCKET));
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
  for (const s of emitted.hud.kills) {
    const lab = ctx.sections[sectionIndexAt(ctx, s)].label;
    killsPerSection[lab] = (killsPerSection[lab] ?? 0) + 1;
  }
  let peak = 0;
  for (const b of enemyBuckets) peak = Math.max(peak, b.length);
  const stops = planHitStops(ctx);
  const prim = [...ctx.actions.map((a) => a.s), ...ctx.threats.map((t) => t.sMiss)].sort((a, b) => a - b);
  let gap = 0;
  const galopStart = ctx.sections.find((s) => s.song.feel === "pulsed")?.s0 ?? 0;
  for (let i = 1; i < prim.length; i++) if (prim[i - 1] >= galopStart) gap = Math.max(gap, prim[i] - prim[i - 1]);
  const stats: PlanStats = {
    peakPopulation: peak,
    killsPerSection,
    totalHitStop: stops.reduce((a, h) => a + (h.s1 - h.s0), 0),
    longestGapWithoutPrimary: gap,
    enemies: crowd.enemies.length,
    hits: kp.hits.length,
    planMs: now() - t0,
    dropped: crowd.dropped ?? 0,
  };

  const plan: Plan = {
    version: `${PLAN_FORMAT}:${input.version ?? "dev"}`,
    seed: ctx.seed,
    song: ctx.song.song,
    duration: ctx.duration,
    warp: warp.breaks,
    worldBeats: ctx.worldBeats!,
    songBeats: Float64Array.from(ctx.song.beats),
    energy: Float32Array.from(ctx.song.energy),
    barStart: ctx.idx.barStart,
    hero: ctx.hero!,
    camera: ctx.camera!,
    hits: kp.hits,
    enemies: crowd.enemies,
    enemySamples: crowd.samples,
    enemySampleRate: SAMPLE_RATE,
    events: { effects: emitted.effects, sounds: emitted.sounds, gems: emitted.gems, ui: emitted.ui },
    projectiles: emitted.projectiles,
    slams: emitted.slams,
    chains: emitted.chains,
    activeBucket: ACTIVE_BUCKET,
    activeEnemies: enemyBuckets.map((b) => Uint32Array.from(b)),
    activeEffects: effectBuckets.map((b) => Uint32Array.from(b)),
    hud: emitted.hud,
    map: ctx.map,
    sections: ctx.sections.map((s) => ({
      label: s.label,
      population: s.level.population,
      s0: s.s0,
      s1: s.s1,
      zone: s.level.zone,
      feel: s.song.feel,
      intensity: s.level.intensity,
      sfxPalette: s.level.sfxPalette,
      key: s.song.key,
      banner: s.level.banner,
    })),
    weapons: ctx.weaponsGranted,
    accents: ctx.song.accents.map((a) => ({ s: a.time, strength: a.strength, punch: ctx.sections[sectionIndexAt(ctx, a.time)].level.camera.punchOnAccents })),
    title: ctx.level.title,
    actions: ctx.actions.map((a) => ({
      s: a.s,
      w: a.w,
      kind: a.kind,
      move: a.move.id,
      weight: a.move.weight,
      anim: a.move.animation.name,
      impactFrame: a.move.impactFrame ?? ctx.heroAnims[a.move.animation.name]?.impactFrame ?? 3,
      hit: a.hit,
      x: a.x,
      y: a.y,
      dir: a.dir,
    })),
    nearMisses: ctx.threats.map((t) => ({ s: t.sMiss, w: warp.toWorld(t.sMiss), kind: t.kind })),
    stats,
  };
  return plan;
}

/** A stable hash of the whole plan, for determinism checks and caching. */
export function planHash(p: Plan): string {
  let h = 0x12345678;
  const mixArr = (a: ArrayLike<number>) => {
    for (let i = 0; i < a.length; i++) h = hash32(h, Math.round(a[i] * 1000));
  };
  h = hash32(h, p.version, p.seed, p.song);
  for (const b of p.warp) h = hash32(h, Math.round(b.s * 1e6), Math.round(b.w * 1e6), Math.round(b.rate * 1e6));
  mixArr(p.hero.x);
  mixArr(p.hero.y);
  mixArr(p.hero.anim);
  mixArr(p.camera.x);
  mixArr(p.camera.zoom);
  mixArr(p.enemySamples);
  for (const e of p.enemies) h = hash32(h, e.type, Math.round(e.spawnW * 1e4), Math.round(e.deathW * 1e4), e.fates.length);
  for (const ev of p.events.effects) h = hash32(h, ev.recipe, Math.round(ev.w * 1e4), Math.round(ev.x), ev.seed);
  for (const ev of p.events.sounds) h = hash32(h, ev.sample, Math.round(ev.s * 1e5), Math.round(ev.gain * 100));
  h = hash32(h, p.map.hash);
  return (h >>> 0).toString(16).padStart(8, "0");
}
