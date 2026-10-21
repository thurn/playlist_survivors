/**
 * Stage 6: assign kills, planning backwards. Every enemy is created from its
 * death: a hit, a time and a position. Spawn time and place are then worked
 * out back from there.
 */
import type { EnemyDef, MoveDef, WeaponDef } from "../content/schema.ts";
import { type Shape, TAU, dist, sampleInShape, shapeArea } from "../core/geometry.ts";
import { Rng, hash32 } from "../core/rng.ts";
import { clamp, lerp } from "../core/shaping.ts";
import { energyAt, beatIndexAt, timeOfBeatIndex } from "../core/songmap.ts";
import { routeFrame } from "../mapgen/mapgen.ts";
import type { EnemyRecord, Hit } from "../plan/types.ts";
import { PlanError, sectionIndexAt, subdivisionBeats, weightFactor, type Ctx, type HeroAction } from "./context.ts";
import { progressAt } from "./stage3-warp.ts";
import { bossHome, heroAt, worldBeatIndex, worldTimeOfBeatIndex } from "./stage4-hero.ts";
import { viewRect } from "./stage5-camera.ts";

export const HURTBOX_RADIUS = 10;
export const HURTBOX_LIFT = 20;
const SPAWN_MARGIN = 16;
/** World-seconds before the finale during which the ring's enemies fill the population. */
const FINALE_FILL = 12;
/** The spawn schedule aims this much above the target population. */
const SPAWN_AIM = 1.08;
/** Never schedule more living enemies than this (the hard cap is 520). */
const POP_CAP = 500;
/** Free sections lose more enemies to big area hits, so they aim higher. */
const SPAWN_AIM_FREE = 1.12;

/** Planned waypoint for the crowd simulation. */
export interface Waypoint {
  w: number;
  x: number;
  y: number;
  hit: number;
  lethal: boolean;
  /** An appointment (stand here at w) rather than a fate. */
  appointment?: boolean;
  /** Hold the position until this world time. */
  holdUntil?: number;
  /** Hero position at w (filled before the crowd simulation). */
  hx?: number;
  hy?: number;
}

export interface Agent {
  e: EnemyRecord;
  def: EnemyDef;
  waypoints: Waypoint[];
  spawnX: number;
  spawnY: number;
  /** Kinematic (formation) positions until kinUntil. */
  kinematic?: (w: number) => { x: number; y: number };
  kinUntil: number;
  /** Is this agent's spawn exempt from the off-screen rule? */
  exempt: boolean;
  /** Has the crowd ever simulated this agent (false for pure formation members)? */
  simulated: boolean;
  /** Multiplier on the final-approach window, raised after a miss. */
  approachBoost?: number;
}

export interface KillPlan {
  hits: Hit[];
  agents: Agent[];
  /** Per-hit list of agent ids with a fate on it. */
  fatesByHit: number[][];
  /** Chain lightning bolt paths. */
  chainPaths: { w: number; x: number; y: number }[][];
  /** Enemies removed by the crowd simulation. */
  dropped?: number;
}

const BASE_KILLS = { light: 2, medium: 4, heavy: 7 } as const;

export function targetPopulation(ctx: Ctx, s: number): number {
  const sec = ctx.sections[sectionIndexAt(ctx, s)];
  const u = clamp((s - sec.s0) / Math.max(1e-6, sec.s1 - sec.s0), 0, 1);
  return lerp(sec.level.population[0], sec.level.population[1], u);
}

/** Sprite half extents (x, up, down) used for off-screen checks. */
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

/** The closest point just outside the view (for this sprite) to a target point. */
function spawnPoint(view: [number, number, number, number], def: EnemyDef, tx: number, ty: number, r: Rng): { x: number; y: number } {
  const ext = spriteExtents(def);
  const m = SPAWN_MARGIN + 12;
  const left = view[0] - m - ext.hx;
  const right = view[2] + m + ext.hx;
  const top = view[1] - m - ext.down;
  const bottom = view[3] + m + ext.up;
  const jitter = r.range(-70, 70);
  const cands = [
    { x: left, y: clamp(ty + jitter, top, bottom), d: 0 },
    { x: right, y: clamp(ty + jitter, top, bottom), d: 0 },
    { x: clamp(tx + jitter, left, right), y: top, d: 0 },
    { x: clamp(tx + jitter, left, right), y: bottom, d: 0 },
  ];
  for (const c of cands) c.d = Math.hypot(c.x - tx, c.y - ty);
  cands.sort((a, b) => a.d - b.d);
  // Mostly the nearest side; sometimes the second nearest, so crowds come from several sides.
  const pick = r.next() < 0.8 || cands[1].d > cands[0].d * 1.6 ? cands[0] : cands[1];
  // Push a little further out, as if emerging from the forest.
  const out = r.range(0, 40);
  if (pick === cands[0] || pick === cands[1]) {
    if (pick.x === left) pick.x -= out;
    else if (pick.x === right) pick.x += out;
    else if (pick.y === top) pick.y -= out;
    else pick.y += out;
  }
  return { x: pick.x, y: pick.y };
}

/** Effective average speed for planning: the rhythm multiplier averages about 0.73. */
function plannedSpeed(def: EnemyDef): number {
  return def.speed * 0.72;
}

interface KillSpec {
  hit: number;
  x: number;
  y: number;
  type: string;
}

export function stage6(ctx: Ctx): KillPlan {
  const warp = ctx.warp!;
  const hero = ctx.hero!;
  const cam = ctx.camera!;
  const map = ctx.map!;
  const wb = ctx.worldBeats!;
  const route = { step: map.routeStep, x: map.routeX, y: map.routeY };
  const hits: Hit[] = [];
  const lib = ctx.lib;

  const intensityOf = (sec: number, energy: number, weight: Hit["weight"], accent: number) =>
    clamp(ctx.sections[sec].level.intensity * (0.35 + 0.65 * energy) * weightFactor(weight) * (accent > 0 ? accent : 1), 0, 1);

  const addHit = (h: Omit<Hit, "id" | "intensity">): number => {
    const id = hits.length;
    hits.push({ ...h, id, intensity: intensityOf(h.section, h.energy, h.weight, h.accent) });
    return id;
  };

  const corridorOk = (x: number, y: number, w: number, pad = 18) => {
    const f = routeFrame(route, progressAt(ctx, w));
    void f;
    // Lateral distance from the route at the nearest point: approximate using the route near progress.
    const lat = lateralOf(x, y, w);
    return Math.abs(lat) < map.corridorHalfWidth - pad;
  };
  function lateralOf(x: number, y: number, w: number): number {
    // Search route samples around the hero's progress for the nearest point.
    const d0 = progressAt(ctx, w);
    let best = Infinity;
    let bestLat = 0;
    for (let dd = d0 - 900; dd <= d0 + 900; dd += 24) {
      const f = routeFrame(route, Math.max(0, dd));
      const dx = x - f.x;
      const dy = y - f.y;
      const d2 = dx * dx + dy * dy;
      if (d2 < best) {
        best = d2;
        bestLat = dx * -f.ty + dy * f.tx;
      }
    }
    return bestLat;
  }

  // ------------------------------------------------------------ hero hits
  const moveShape = (a: HeroAction, m: MoveDef): Shape | undefined => {
    const hs = m.hitShape;
    if (!hs) return undefined;
    switch (hs.kind) {
      case "arc":
        return { kind: "arc", x: a.x, y: a.y, radius: hs.radius, dir: a.dir, halfAngle: (hs.degrees * Math.PI) / 360 };
      case "circle": {
        const off = hs.offset ?? 0;
        return { kind: "circle", x: a.x + Math.cos(a.dir) * off, y: a.y + Math.sin(a.dir) * off, radius: hs.radius };
      }
      case "line":
        return { kind: "capsule", x0: a.x, y0: a.y, x1: a.x + Math.cos(a.dir) * hs.length, y1: a.y + Math.sin(a.dir) * hs.length, radius: hs.width / 2 };
      case "capsule": {
        const len = clamp(dist(a.x0, a.y0, a.x, a.y), hs.length[0], hs.length[1]);
        return { kind: "capsule", x0: a.x - Math.cos(a.dir) * len, y0: a.y - Math.sin(a.dir) * len, x1: a.x + Math.cos(a.dir) * 16, y1: a.y + Math.sin(a.dir) * 16, radius: hs.radius };
      }
      default:
        return undefined;
    }
  };

  for (const a of ctx.actions) {
    if (a.kind === "dodge" || a.kind === "sweep" || a.kind === "blast") continue;
    const shape = moveShape(a, a.move);
    if (!shape) continue;
    a.shape = shape;
    a.hit = addHit({ s: a.s, w: a.w, source: "move", what: a.move.id, weight: a.move.weight, primary: true, shape, dir: a.dir, energy: a.energy, accent: a.accent, section: a.section });
  }

  // ------------------------------------------------------- formation layouts
  const formationPos: { x: number; y: number; w0: number; w1: number }[] = [];
  const memberPositions = new Map<number, { x: number; y: number }[]>();
  for (const f of ctx.formations) {
    const def = lib.formations.get(f.def)!;
    if (def.release.at !== "finalAccentOfPhrase") continue;
    const sweep = ctx.actions.find((a) => a.kind === "sweep" && a.formation === f.id);
    if (!sweep) throw new PlanError(`formation ${f.def} has no sweep action`);
    const pts: { x: number; y: number }[] = [];
    const bi0 = beatIndexAt(ctx.song, f.sRelease);
    const stagger = subdivisionBeats(def.release.stagger, 2);
    for (let k = 0; k < def.count; k++) {
      const s = timeOfBeatIndex(ctx.song, bi0 + k * stagger);
      const w = warp.toWorld(s);
      const p = heroAt(hero, w);
      pts.push({ x: p.x, y: p.y });
      const hid = addHit({ s, w, source: "sweep", what: sweep.move.id, weight: sweep.move.weight, primary: false, shape: { kind: "circle", x: p.x, y: p.y, radius: 22 }, dir: sweep.dir, energy: energyAt(ctx.song, s), accent: k === 0 ? sweep.accent : 0, section: f.section });
      if (k === 0) sweep.hit = hid;
    }
    memberPositions.set(f.id, pts);
    for (const p of pts) formationPos.push({ ...p, w0: warp.toWorld(f.sStart), w1: warp.toWorld(f.sRelease) + 1 });
  }

  // ------------------------------------------------------------ weapon hits
  const chainPaths: { w: number; x: number; y: number }[][] = [];
  const nearFormation = (x: number, y: number, w: number, r: number) => formationPos.some((p) => w >= p.w0 && w <= p.w1 && dist(p.x, p.y, x, y) < r);
  for (const fi of ctx.firings) {
    const wd = lib.weapons.get(fi.weapon)!;
    const h = heroAt(hero, fi.w);
    const sec = fi.section;
    if (wd.kind === "orbit" && wd.orbit) {
      const bpb = ctx.idx.beatsPerBar[ctx.sections[sec].song.bars[0]] ?? 2;
      const th0 = (TAU * worldBeatIndex(wb, fi.w)) / (wd.orbit.periodBars * bpb);
      for (let k = 0; k < wd.orbit.count; k++) {
        const th = th0 + (k * TAU) / wd.orbit.count;
        const x = h.x + Math.cos(th) * wd.orbit.radius;
        const y = h.y + Math.sin(th) * wd.orbit.radius * 0.8;
        addHit({ s: fi.s, w: fi.w, source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x, y, radius: wd.hitShape!.radius }, dir: th + Math.PI / 2, energy: fi.energy, accent: 0, section: sec });
      }
    } else if (wd.kind === "chain" && wd.chain) {
      const r = ctx.rng("chain", fi.bi);
      const jumps = Math.round(lerp(wd.chain.jumps[0], wd.chain.jumps[1], fi.energy));
      const path: { w: number; x: number; y: number }[] = [{ w: fi.w, x: h.x, y: h.y - 24 }];
      let px = h.x;
      let py = h.y;
      let ang = r.range(-Math.PI, Math.PI);
      for (let j = 0; j < jumps; j++) {
        const s = timeOfBeatIndex(ctx.song, fi.bi + j * subdivisionBeats(wd.chain.step, 2));
        const w = warp.toWorld(s);
        let x = px;
        let y = py;
        for (let tries = 0; tries < 12; tries++) {
          const d = r.range(j === 0 ? 60 : 50, wd.chain.range);
          const a = ang + r.range(-1.1, 1.1);
          x = px + Math.cos(a) * d;
          y = py + Math.sin(a) * d * 0.85;
          const hh = heroAt(hero, w);
          if (corridorOk(x, y, w, 40) && dist(x, y, hh.x, hh.y) > 40 && dist(x, y, hh.x, hh.y) < 330 && !nearFormation(x, y, w, 50)) {
            ang = a;
            break;
          }
        }
        px = x;
        py = y;
        path.push({ w, x, y: y - 16 });
        addHit({ s, w, source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x, y, radius: wd.hitShape!.radius }, dir: ang, energy: fi.energy, accent: 0, section: sectionIndexAt(ctx, s) });
      }
      chainPaths.push(path);
    } else if (wd.kind === "trail" && wd.trail) {
      const bd = Math.max(0.05, worldTimeOfBeatIndex(wb, fi.bi) - worldTimeOfBeatIndex(wb, fi.bi - 1));
      const wFrom = fi.w - wd.trail.beats * bd;
      let lastX = Infinity;
      let lastY = Infinity;
      for (let w = fi.w - 0.12; w >= wFrom; w -= 1 / 60) {
        const p = heroAt(hero, w);
        if (dist(p.x, p.y, lastX, lastY) < wd.trail.spacing) continue;
        lastX = p.x;
        lastY = p.y;
        addHit({ s: fi.s, w: fi.w, source: "weapon", what: wd.id, weight: wd.weight, primary: false, shape: { kind: "circle", x: p.x, y: p.y, radius: wd.hitShape!.radius }, dir: 0, energy: fi.energy, accent: 0, section: sec });
      }
    }
  }

  // --------------------------------------------------------- finale ring
  let ringHits: number[] = [];
  let blastWeapon: WeaponDef | undefined;
  if (ctx.blastS !== undefined && ctx.blastWeapon) {
    blastWeapon = lib.weapons.get(ctx.blastWeapon)!;
    const b = blastWeapon.blast!;
    const bi0 = beatIndexAt(ctx.song, ctx.blastS);
    const step = subdivisionBeats(b.step, 2);
    const bands = Math.max(1, Math.round(b.beats / step));
    const w0 = warp.toWorld(ctx.blastS);
    const h = heroAt(hero, w0);
    for (let k = 0; k < bands; k++) {
      const s = timeOfBeatIndex(ctx.song, bi0 + k * step);
      const w = warp.toWorld(s);
      ringHits.push(
        addHit({ s, w, source: "ring", what: blastWeapon.id, weight: "heavy", primary: false, shape: { kind: "ring", x: h.x, y: h.y, inner: (b.radius * k) / bands, outer: (b.radius * (k + 1)) / bands }, dir: 0, energy: 1, accent: 0, section: sectionIndexAt(ctx, s) }),
      );
    }
  }

  // Nothing hits after the finale blast's last ring.
  if (ringHits.length > 0) {
    const endW = Math.max(...ringHits.map((i) => hits[i].w));
    for (const h of hits) if (h.source === "weapon" && h.w > endW) h.w = Number.NaN;
  }
  // Sort hits by world time (stable) and remap action/formation references.
  const order = hits.map((_, i) => i).filter((i) => !Number.isNaN(hits[i].w)).sort((a, b) => hits[a].w - hits[b].w || hits[a].s - hits[b].s || a - b);
  const remap = new Map<number, number>();
  order.forEach((old, i) => remap.set(old, i));
  const sorted = order.map((old, i) => ({ ...hits[old], id: i }));
  hits.length = 0;
  hits.push(...sorted);
  for (const a of ctx.actions) if (a.hit !== undefined) a.hit = remap.get(a.hit);
  ringHits = ringHits.map((h) => remap.get(h)!);
  const ringSet = new Set(ringHits);
  const sweepHitsByFormation = new Map<number, number[]>();
  for (const f of ctx.formations) {
    const def = lib.formations.get(f.def)!;
    if (def.release.at !== "finalAccentOfPhrase") continue;
    const ids = hits.filter((h) => h.source === "sweep" && h.section === f.section && h.w >= warp.toWorld(f.sRelease) - 1e-6).slice(0, def.count).map((h) => h.id);
    sweepHitsByFormation.set(f.id, ids);
  }

  // --------------------------------------------------------- kill counts
  const rosterAt = (s: number) => ctx.sections[sectionIndexAt(ctx, s)].level.roster;
  const meanRadius = (s: number) => {
    const roster = rosterAt(s);
    let tw = 0;
    let tr = 0;
    for (const [t, wgt] of Object.entries(roster)) {
      tw += wgt;
      tr += wgt * lib.enemies.get(t)!.radius;
    }
    return tw > 0 ? tr / tw : 12;
  };
  const caps = hits.map((h) => (h.source === "ring" || h.source === "sweep" ? 0 : Math.max(1, Math.floor((0.6 * shapeArea(h.shape)) / (Math.PI * meanRadius(h.s) ** 2)))));
  // While the horde is still gathering (small targets), hero swings may hit nobody.
  const minKills = hits.map((h) => (h.source === "move" && targetPopulation(ctx, h.s) >= 30 ? 1 : 0));
  const kills = hits.map((h, i) => {
    if (h.source === "ring" || h.source === "sweep") return 0;
    const target = targetPopulation(ctx, h.s);
    if (h.source === "weapon") {
      const wd = lib.weapons.get(h.what)!;
      if (wd.kind === "chain") return 1;
      const r = new Rng(ctx.seed, "wk", i);
      return Math.min(caps[i], r.roundStochastic(lerp(wd.killsPerFiring[0], wd.killsPerFiring[1], h.energy)));
    }
    const k = Math.round(BASE_KILLS[h.weight] * (target / 250) * (0.5 + h.energy));
    return clamp(k, minKills[i], caps[i]);
  });

  // ---------------------------------------------- fixed (special) enemies
  const fixedAgents: Agent[] = [];
  const reserved: { hit: number; x: number; y: number }[] = [];
  let nextId = 0;
  const newRecord = (type: string, spawnW: number, deathW: number): EnemyRecord => ({ id: nextId++, type, spawnW, deathW, sampleOffset: 0, sampleCount: 0, sampleW0: 0, fates: [] });

  // Kick lines and other release-by-move formations.
  for (const f of ctx.formations) {
    const def = lib.formations.get(f.def)!;
    const edef = lib.enemies.get(def.enemy)!;
    const w0 = warp.toWorld(f.sStart);
    const wRel = warp.toWorld(f.sRelease);
    if (def.release.at === "finalAccentOfPhrase") {
      const pts = memberPositions.get(f.id)!;
      const hitIds = sweepHitsByFormation.get(f.id)!;
      const wEnterEnd = worldTimeOfBeatIndex(wb, beatIndexAt(ctx.song, f.sStart) + def.entranceBeats);
      const view0 = viewRect(cam, w0);
      pts.forEach((p, k) => {
        const hit = hits[hitIds[k]];
        const startX = view0[2] + 60 + 40 + k * 6;
        const sx = Math.max(startX, p.x + 40);
        const rec = newRecord(def.enemy, w0, hit.w);
        rec.formation = f.id;
        rec.fates.push({ w: hit.w, x: p.x, y: p.y, hit: hit.id, lethal: true });
        if (def.dance) rec.dance = { w0: wEnterEnd, w1: hit.w, anim: def.dance.animation, alternate: def.dance.alternateLegs, phase: 0 };
        const steps = def.entranceBeats;
        const bStart = beatIndexAt(ctx.song, f.sStart);
        const kin = (w: number) => {
          if (w >= wEnterEnd) return { x: p.x, y: p.y };
          const wbi = worldBeatIndex(wb, w) - bStart;
          const u = clamp(wbi / steps, 0, 1);
          // March in on the beat: each step eases quickly, then holds.
          const kk = Math.floor(u * steps);
          const fr = u * steps - kk;
          const e = (kk + Math.min(1, fr / 0.45)) / steps;
          const ee = 1 - (1 - Math.min(1, e)) ** 2;
          return { x: lerp(sx, p.x, ee), y: p.y + Math.sin(Math.PI * Math.min(1, fr / 0.45)) * -3 };
        };
        const ag: Agent = { e: rec, def: edef, waypoints: [{ w: hit.w, x: p.x, y: p.y, hit: hit.id, lethal: true }], spawnX: sx, spawnY: p.y, kinematic: kin, kinUntil: hit.w, exempt: true, simulated: false };
        f.members.push(rec.id);
        fixedAgents.push(ag);
      });
    } else {
      // Break-off formations: a V-wedge flies in, then members join the crowd.
      const view0 = viewRect(cam, w0);
      const hRel = heroAt(hero, wRel);
      const fr = routeFrame(route, progressAt(ctx, wRel));
      const endX = hRel.x + fr.tx * 120;
      const endY = hRel.y + fr.ty * 120 - 70;
      const startX = endX + 40;
      const startY = view0[1] - 120;
      const dirX = endX - startX;
      const dirY = endY - startY;
      const dl = Math.hypot(dirX, dirY) || 1;
      const ux = dirX / dl;
      const uy = dirY / dl;
      const bStart = beatIndexAt(ctx.song, f.sStart);
      const totalBeats = beatIndexAt(ctx.song, f.sRelease) - bStart;
      const stepB = subdivisionBeats(def.step, 2);
      for (let k = 0; k < def.count; k++) {
        const arm = k === 0 ? 0 : Math.ceil(k / 2);
        const side = k === 0 ? 0 : k % 2 === 0 ? 1 : -1;
        const ox = -ux * arm * def.spacing + -uy * side * arm * def.spacing;
        const oy = -uy * arm * def.spacing + ux * side * arm * def.spacing;
        const kin = (w: number) => {
          const wbi = worldBeatIndex(wb, w) - bStart;
          const steps = totalBeats / stepB;
          const u = clamp(wbi / totalBeats, 0, 1);
          const kk = Math.floor(u * steps);
          const frc = u * steps - kk;
          const e = Math.min(1, (kk + Math.min(1, frc / 0.6)) / steps);
          const ee = 1 - (1 - e) ** 2;
          return { x: lerp(startX, endX, ee) + ox, y: lerp(startY, endY, ee) + oy };
        };
        const rec = newRecord(def.enemy, w0, wRel + 2);
        rec.formation = f.id;
        const p = kin(wRel);
        fixedAgents.push({ e: rec, def: edef, waypoints: [], spawnX: p.x, spawnY: p.y, kinematic: kin, kinUntil: wRel, exempt: true, simulated: true });
        f.members.push(rec.id);
      }
    }
  }

  // Boss.
  let bossAgent: Agent | undefined;
  if (ctx.boss) {
    const b = ctx.boss;
    const bdef = lib.enemies.get(b.type)!;
    const wLand = warp.toWorld(b.sEnter);
    const home = bossHome(ctx, wLand);
    const rec = newRecord(b.type, wLand - 0.55, warp.toWorld(b.sDeath));
    rec.entrance = "land";
    rec.attacks = [];
    const wps: Waypoint[] = [{ w: wLand, x: home.x, y: home.y, hit: -1, lethal: false, appointment: true }];
    const bossActions = ctx.actions.filter((a) => a.bossHit && a.hit !== undefined);
    for (const th of ctx.threats.filter((t) => t.kind === "slam")) {
      const dodge = ctx.actions[th.dodgeAction!];
      const wMiss = warp.toWorld(th.sMiss);
      const pre = { x: dodge.x0, y: dodge.y0 };
      const hm = bossHome(ctx, wMiss);
      const ux = hm.x - pre.x;
      const uy = hm.y - pre.y;
      const ul = Math.hypot(ux, uy) || 1;
      const bx = pre.x + (ux / ul) * 50;
      const by = pre.y + (uy / ul) * 50;
      wps.push({ w: warp.toWorld(th.sRelease), x: bx, y: by, hit: -1, lethal: false, appointment: true, holdUntil: wMiss + 0.1 });
      wps.push({ w: wMiss, x: bx, y: by, hit: -1, lethal: false, appointment: true });
      rec.attacks.push({ w: wMiss, kind: "slam", dir: Math.atan2(pre.y - by, pre.x - bx) });
    }
    for (const a of bossActions) {
      const h = hits[a.hit!];
      const reach = h.shape.kind === "arc" ? h.shape.radius * 0.6 : h.shape.kind === "circle" ? Math.min(60, h.shape.radius * 0.5) : 40;
      const x = a.x + Math.cos(a.dir) * reach;
      const y = a.y + Math.sin(a.dir) * reach;
      const lethal = a.kind === "finisher";
      wps.push({ w: h.w, x, y, hit: h.id, lethal });
      rec.fates.push({ w: h.w, x, y, hit: h.id, lethal });
      reserved.push({ hit: h.id, x, y });
    }
    wps.sort((p, q) => p.w - q.w);
    // Drop appointments that collide with fates closer than half a beat.
    const clean = wps.filter((p, i) => !p.appointment || !wps.some((q, j) => j !== i && !q.appointment && Math.abs(q.w - p.w) < 0.3));
    bossAgent = { e: rec, def: bdef, waypoints: clean, spawnX: home.x, spawnY: home.y, kinUntil: -Infinity, exempt: true, simulated: true };
    b.enemy = rec.id;
    fixedAgents.push(bossAgent);
  }

  // Archers: an appointment at the firing spot, then a lethal fate on a later hero hit.
  const archerFates: { agent: Agent; after: number }[] = [];
  for (const th of ctx.threats.filter((t) => t.kind === "arrow")) {
    const n = th.archers ?? 1;
    const wRel = warp.toWorld(th.sRelease);
    const hRel = heroAt(hero, wRel);
    const tang = (() => {
      const f = routeFrame(route, progressAt(ctx, wRel));
      return Math.atan2(f.ty, f.tx);
    })();
    for (let j = 0; j < n; j++) {
      const bearing = tang + th.bearing + (j - (n - 1) / 2) * 0.4;
      const x = hRel.x + Math.cos(bearing) * 190;
      const y = hRel.y + Math.sin(bearing) * 150;
      const rec = newRecord("skeleton-archer", 0, 0);
      rec.attacks = [{ w: wRel, kind: "shoot", dir: bearing + Math.PI }];
      const ag: Agent = { e: rec, def: lib.enemies.get("skeleton-archer")!, waypoints: [{ w: wRel, x, y, hit: -1, lethal: false, appointment: true, holdUntil: wRel + 0.35 }], spawnX: 0, spawnY: 0, kinUntil: -Infinity, exempt: false, simulated: true };
      fixedAgents.push(ag);
      archerFates.push({ agent: ag, after: wRel + 2.6 });
    }
  }

  // Lethal fates for break-off formation members and archers: claim kills on later hits.
  const claimKill = (ag: Agent, wFrom: number, wTo: number, fromX: number, fromY: number, originW: number): boolean => {
    const speed = plannedSpeed(ag.def);
    for (const h of hits) {
      if (h.w < wFrom || h.w > wTo || h.source === "ring" || h.source === "sweep") continue;
      const isChain = h.source === "weapon" && lib.weapons.get(h.what)!.kind === "chain";
      if (isChain && kills[h.id] === 0) continue;
      const r = new Rng(ctx.seed, "claim", ag.e.id, h.id);
      for (let tries = 0; tries < (isChain ? 1 : 6); tries++) {
        const p = isChain ? { x: (h.shape as { x: number }).x, y: (h.shape as { y: number }).y } : sampleInShape(h.shape, () => r.next(), ag.def.radius * 0.5);
        if (dist(p.x, p.y, fromX, fromY) > speed * (h.w - originW)) continue;
        if (reserved.some((q) => q.hit === h.id && dist(q.x, q.y, p.x, p.y) < 2 * ag.def.radius)) continue;
        if (!corridorOk(p.x, p.y, h.w)) continue;
        reserved.push({ hit: h.id, x: p.x, y: p.y });
        ag.waypoints.push({ w: h.w, x: p.x, y: p.y, hit: h.id, lethal: true });
        ag.e.fates.push({ w: h.w, x: p.x, y: p.y, hit: h.id, lethal: true });
        ag.e.deathW = h.w;
        if (kills[h.id] > minKills[h.id] || isChain) kills[h.id]--;
        return true;
      }
    }
    return false;
  };
  for (const ag of fixedAgents) {
    if (ag.e.formation === undefined || ag.waypoints.length > 0) continue;
    const ok = claimKill(ag, ag.kinUntil + 0.7, ag.kinUntil + 8, ag.spawnX, ag.spawnY, ag.kinUntil);
    if (!ok) throw new PlanError(`formation member ${ag.e.id} (${ag.e.type}) found no hit to die on after w=${ag.kinUntil.toFixed(2)}`);
  }
  for (const af of archerFates) {
    const ap = af.agent.waypoints[0];
    const ok = claimKill(af.agent, af.after, af.after + 12, ap.x, ap.y, ap.holdUntil ?? ap.w);
    if (!ok) throw new PlanError(`archer ${af.agent.e.id} found no hit to die on after w=${af.after.toFixed(2)}`);
  }

  return { hits, ...assignCrowd(ctx, hits, kills, caps, minKills, fixedAgents, reserved, ringHits, ringSet, blastWeapon, nextId, chainPaths, formationPos) };
}

/** Crowd kills, lifetimes, spawns and population balancing. Returns the full agent list. */
function assignCrowd(
  ctx: Ctx,
  hits: Hit[],
  kills: number[],
  caps: number[],
  minKills: number[],
  fixed: Agent[],
  reserved: { hit: number; x: number; y: number }[],
  ringHits: number[],
  ringSet: Set<number>,
  blastWeapon: WeaponDef | undefined,
  firstId: number,
  chainPaths: { w: number; x: number; y: number }[][],
  formationPos: { x: number; y: number; w0: number; w1: number }[],
): { agents: Agent[]; fatesByHit: number[][]; chainPaths: { w: number; x: number; y: number }[][] } {
  const warp = ctx.warp!;
  const hero = ctx.hero!;
  const cam = ctx.camera!;
  const lib = ctx.lib;

  const pickType = (s: number, r: Rng): string => {
    const roster = ctx.sections[sectionIndexAt(ctx, s)].level.roster;
    const types = Object.keys(roster).filter((t) => lib.enemies.get(t)!.size !== "boss");
    return r.weighted(types, types.map((t) => roster[t]));
  };

  // Kill spots already taken, by time bucket and cell: enemies blending into nearby spots
  // at the same moment would overlap.
  let taken = new Map<number, { x: number; y: number; w: number; r: number }[]>();
  const takenKey = (x: number, y: number, w: number) => (Math.floor(w / 0.8) * 4096 + Math.floor(x / 48) + 2048) * 4096 + Math.floor(y / 48) + 2048;
  const spotFree = (x: number, y: number, w: number, r: number) => {
    for (let dt = -1; dt <= 1; dt++)
      for (let gx = -1; gx <= 1; gx++)
        for (let gy = -1; gy <= 1; gy++) {
          const l = taken.get(takenKey(x + gx * 48, y + gy * 48, w + dt * 0.8));
          if (!l) continue;
          for (const q of l) if (Math.abs(q.w - w) < 0.8 && dist(q.x, q.y, x, y) < r + q.r + 6) return false;
        }
    return true;
  };
  const takeSpot = (x: number, y: number, w: number, r: number) => {
    const k = takenKey(x, y, w);
    let l = taken.get(k);
    if (!l) taken.set(k, (l = []));
    l.push({ x, y, w, r });
  };
  for (const q of reserved) takeSpot(q.x, q.y, hits[q.hit].w, 14);

  /** Kill positions for one hit: Poisson-disk points inside the shape. */
  const placeKills = (h: Hit, count: number): KillSpec[] => {
    const out: KillSpec[] = [];
    const r = new Rng(ctx.seed, "place", h.id);
    const hh = heroAt(hero, h.w);
    for (let j = 0; j < count; j++) {
      const type = h.source === "weapon" && lib.weapons.get(h.what)!.kind === "chain" ? pickType(h.s, new Rng(ctx.seed, "type", h.id, j)) : pickType(h.s, new Rng(ctx.seed, "type", h.id, j));
      const def = lib.enemies.get(type)!;
      let placed = false;
      for (let tries = 0; tries < 14 && !placed; tries++) {
        const p =
          h.source === "weapon" && lib.weapons.get(h.what)!.kind === "chain"
            ? { x: (h.shape as { x: number }).x, y: (h.shape as { y: number }).y }
            : sampleInShape(h.shape, () => r.next(), def.radius * 0.4);
        if (dist(p.x, p.y, hh.x, hh.y) < 16) continue;
        if (out.some((q) => dist(q.x, q.y, p.x, p.y) < 2 * def.radius)) continue;
        if (reserved.some((q) => q.hit === h.id && dist(q.x, q.y, p.x, p.y) < 2 * def.radius)) continue;
        if (formationPos.some((f) => h.w >= f.w0 && h.w <= f.w1 && dist(f.x, f.y, p.x, p.y) < 2 * def.radius + 4)) continue;
        if (!spotFree(p.x, p.y, h.w, def.radius)) continue;
        takeSpot(p.x, p.y, h.w, def.radius);
        out.push({ hit: h.id, x: p.x, y: p.y, type });
        placed = true;
      }
    }
    return out;
  };

  const wEnd = warp.toWorld(ctx.duration);
  const targetW = (w: number) => targetPopulation(ctx, warp.toSong(w));
  const freeAt = (w: number) => ctx.sections[sectionIndexAt(ctx, warp.toSong(w))].song.feel === "free";

  const regularHits = hits.filter((h) => !ringSet.has(h.id) && h.source !== "sweep");
  let specs: KillSpec[] = [];
  let lifetimes: number[] = [];
  let spawns: { w: number; x: number; y: number }[] = [];

  // Balance population: iterate kill counts until the curve is within ±15%.
  const bucket = 0.5;
  const nb = Math.ceil(wEnd / bucket) + 1;
  const ringW = ringHits.length ? hits[ringHits[0]].w : Infinity;
  // Archers and other fixed non-exempt agents need spawns.
  for (const ag of fixed) {
    if (ag.exempt) continue;
    const first = ag.waypoints[0];
    const r = new Rng(ctx.seed, "fixedspawn", ag.e.id);
    let L = 6;
    let sp = { x: 0, y: 0 };
    for (; L < 40; L *= 1.3) {
      const w = Math.max(0, first.w - L);
      sp = spawnPoint(viewRect(cam, w), ag.def, first.x, first.y, r);
      if (dist(sp.x, sp.y, first.x, first.y) / (first.w - w) <= plannedSpeed(ag.def)) break;
    }
    ag.e.spawnW = Math.max(0, first.w - L);
    ag.spawnX = sp.x;
    ag.spawnY = sp.y;
  }

  const fixedAlive = new Float64Array(nb);
  for (const a of fixed) {
    const s0 = Math.max(0, Math.floor(a.e.spawnW / bucket));
    const s1 = Math.min(nb - 1, Math.floor(a.e.deathW / bucket));
    for (let b = s0; b <= s1; b++) fixedAlive[b]++;
  }
  // Finale ring: exactly the target population's worth of enemies, spread across the bands
  // in view. They are scheduled with everyone else, so they walk in as the regulars thin out.
  const ringSpecs: KillSpec[] = [];
  if (ringHits.length > 0 && blastWeapon) {
    const blastW = hits[ringHits[0]].w;
    const nRing = Math.round(targetW(blastW - 1e-3));
    const ringShape = hits[ringHits[0]].shape as Extract<Shape, { kind: "ring" }>;
    const cx = ringShape.x;
    const cy = ringShape.y;
    const view = viewRect(cam, blastW);
    const outer = (hits[ringHits[ringHits.length - 1]].shape as Extract<Shape, { kind: "ring" }>).outer;
    const r = new Rng(ctx.seed, "ring");
    for (let j = 0; j < nRing; j++) {
      const type = pickType(hits[ringHits[0]].s, r);
      const def = lib.enemies.get(type)!;
      let x = cx;
      let y = cy;
      for (let tries = 0; tries < 40; tries++) {
        x = lerp(view[0] + 20, view[2] - 20, r.next());
        y = lerp(view[1] + 40, view[3] - 10, r.next());
        const d = dist(x, y, cx, cy);
        if (d < 40 || d >= outer) continue;
        if (ringSpecs.some((q) => dist(q.x, q.y, x, y) < 2 * def.radius + 2) && tries < 30) continue;
        break;
      }
      const d = dist(x, y, cx, cy);
      const band =
        ringHits.find((hid) => {
          const sh = hits[hid].shape as Extract<Shape, { kind: "ring" }>;
          return d >= sh.inner && d < sh.outer;
        }) ?? ringHits[ringHits.length - 1];
      ringSpecs.push({ hit: band, x, y, type });
    }
  }

  // Before the finale, the horde gathers: regular hits kill as few as allowed.
  for (const h of regularHits) if (h.w >= ringW - FINALE_FILL) kills[h.id] = minKills[h.id];

  let pop: Float64Array<ArrayBuffer> = new Float64Array(nb);
  let bestBad = Infinity;
  let best: { specs: KillSpec[]; lifetimes: number[]; spawns: { w: number; x: number; y: number }[]; pop: Float64Array<ArrayBuffer> } | undefined;
  const baseTaken = taken;
  for (let round = 0; round < 10; round++) {
    taken = new Map([...baseTaken].map(([k, v]) => [k, [...v]]));
    for (const k of ringSpecs) takeSpot(k.x, k.y, hits[k.hit].w, 12);
    specs = [];
    for (const h of regularHits) specs.push(...placeKills(h, kills[h.id]));
    specs.push(...ringSpecs);
    // Feasible lifetime range per kill: long enough to walk in from off-screen.
    const deathTimes = specs.map((k) => hits[k.hit].w);
    const feasible: KillSpec[] = [];
    const lmin: number[] = [];
    const lmax: number[] = [];
    const dws: number[] = [];
    specs.forEach((k, i) => {
      const dw = deathTimes[i];
      const def = lib.enemies.get(k.type)!;
      const r = new Rng(ctx.seed, "lmin", k.hit, i);
      let L = def.lifetime[0];
      let ok = false;
      for (let tries = 0; tries < 9; tries++) {
        const w = Math.max(0, dw - L);
        const p = spawnPoint(viewRect(cam, w), def, k.x, k.y, r);
        if (dist(p.x, p.y, k.x, k.y) / Math.max(0.1, dw - w) <= plannedSpeed(def)) {
          ok = true;
          L = dw - w;
          break;
        }
        if (w === 0) break;
        L = Math.min(def.lifetime[1], L * 1.3);
      }
      // A kill nobody can walk to in time (e.g. right after the song starts) is dropped.
      if (!ok) return;
      feasible.push(k);
      lmin.push(L);
      lmax.push(Math.max(L, def.lifetime[1]));
      dws.push(dw);
    });
    // Desired cumulative spawns: S*(t) = target(t) - fixed(t) + deaths(t).
    const order = dws.map((_, i) => i).sort((a, b) => dws[a] - dws[b]);
    const nS = order.length;
    const slots: number[] = [];
    {
      let di = 0;
      let best = 0;
      for (let b = 0; b < nb && slots.length < nS; b++) {
        const t = b * bucket;
        while (di < nS && dws[order[di]] <= t) di++;
        // Aim a little high: the crowd simulation loses a few enemies to unplanned hits.
        const want = Math.min(nS, Math.max(best, Math.round(Math.min(targetW(t + bucket / 2) * (freeAt(t) ? SPAWN_AIM_FREE : SPAWN_AIM), POP_CAP) - fixedAlive[b] + di)));
        best = want;
        while (slots.length < want) slots.push(t);
      }
      while (slots.length < nS) slots.push(wEnd);
    }
    // Earliest-deadline-first: each spawn slot goes to the waiting enemy that must spawn soonest.
    const release = order.map((i) => Math.max(0, dws[i] - lmax[i]));
    const deadline = (i: number) => Math.max(0, dws[i] - lmin[i]);
    const byRelease = order.map((_, k) => k).sort((a, b) => release[a] - release[b]);
    const spawnW = new Float64Array(specs.length).fill(-1);
    const heap: number[] = [];
    const hpush = (i: number) => {
      heap.push(i);
      let c = heap.length - 1;
      while (c > 0) {
        const pnt = (c - 1) >> 1;
        if (deadline(heap[pnt]) <= deadline(heap[c])) break;
        [heap[pnt], heap[c]] = [heap[c], heap[pnt]];
        c = pnt;
      }
    };
    const hpop = (): number => {
      const top = heap[0];
      const last = heap.pop()!;
      if (heap.length > 0) {
        heap[0] = last;
        let c = 0;
        for (;;) {
          const l = 2 * c + 1;
          const rr = l + 1;
          let m = c;
          if (l < heap.length && deadline(heap[l]) < deadline(heap[m])) m = l;
          if (rr < heap.length && deadline(heap[rr]) < deadline(heap[m])) m = rr;
          if (m === c) break;
          [heap[m], heap[c]] = [heap[c], heap[m]];
          c = m;
        }
      }
      return top;
    };
    let ri = 0;
    for (const t of slots) {
      while (ri < byRelease.length && release[byRelease[ri]] <= t) hpush(order[byRelease[ri++]]);
      // Anyone whose deadline has passed spawns at its deadline.
      while (heap.length > 0 && deadline(heap[0]) < t) {
        const i = hpop();
        spawnW[i] = deadline(i);
      }
      if (heap.length > 0) spawnW[hpop()] = t;
    }
    while (heap.length > 0) {
      const i = hpop();
      spawnW[i] = deadline(i);
    }
    for (const i of order) if (spawnW[i] < 0) spawnW[i] = deadline(i);
    lifetimes = [];
    spawns = [];
    feasible.forEach((k, i) => {
      const w = Math.min(spawnW[i], deadline(i));
      const r = new Rng(ctx.seed, "spawn", k.hit, i);
      const p = spawnPoint(viewRect(cam, w), lib.enemies.get(k.type)!, k.x, k.y, r);
      lifetimes.push(dws[i] - w);
      spawns.push({ w, ...p });
    });
    specs = feasible;
    pop = new Float64Array(nb);
    pop.set(fixedAlive);
    specs.forEach((k, i) => {
      const s0 = Math.max(0, Math.floor(spawns[i].w / bucket));
      const s1 = Math.min(nb - 1, Math.floor(hits[k.hit].w / bucket));
      for (let b = s0; b <= s1; b++) pop[b]++;
    });
    // Compare per bar window, then scale each hit's kills by the population error over
    // the span its enemies were alive (a window's population comes from later kills).
    let bad = 0;
    const ratio = new Float64Array(nb).fill(1);
    for (const [w0, w1] of barWindows(ctx)) {
      if (w0 >= ringW - 0.01) continue;
      // Near the finale, the ring's enemies fill any shortfall, so only excess counts.
      const nearEnd = w0 >= ringW - FINALE_FILL;
      const b0 = Math.floor(w0 / bucket);
      const b1 = Math.max(b0, Math.floor(w1 / bucket) - 1);
      let p = 0;
      let t = 0;
      for (let b = b0; b <= b1; b++) {
        p += pop[b];
        t += targetW(b * bucket + bucket / 2);
      }
      const m = b1 - b0 + 1;
      p /= m;
      t /= m;
      const tol = Math.max(0.15 * t, 4);
      if (nearEnd ? p > t + tol * 0.75 : Math.abs(p - t) > tol * 0.75) {
        bad++;
      }
      const r = nearEnd && p < t ? 1 : clamp((t + 2) / (p + 2), 0.25, 4);
      for (let b = b0; b <= b1; b++) ratio[b] = r;
    }
    if (bad < bestBad) {
      bestBad = bad;
      best = { specs, lifetimes, spawns, pop };
    }
    if (bad <= Math.max(2, barWindows(ctx).length * 0.02) || round === 9) break;
    const lifeByHit = new Map<number, number>();
    specs.forEach((k, i) => lifeByHit.set(k.hit, Math.max(lifeByHit.get(k.hit) ?? 0, lifetimes[i])));
    for (const h of regularHits) {
      if (h.w >= ringW - 1e-6) continue;
      const L = lifeByHit.get(h.id) ?? 6;
      const b0 = Math.max(0, Math.floor((h.w - L) / bucket));
      const b1 = Math.min(nb - 1, Math.floor(h.w / bucket));
      let acc = 0;
      for (let b = b0; b <= b1; b++) acc += Math.log(ratio[b]);
      const f = Math.exp(acc / Math.max(1, b1 - b0 + 1));
      if (h.w >= ringW - FINALE_FILL && f > 1) continue;
      const r = new Rng(ctx.seed, "bal", h.id, round);
      const base = Math.max(kills[h.id], f > 1 ? 0.6 : 0);
      kills[h.id] = clamp(r.roundStochastic(base * Math.pow(f, 0.85)), minKills[h.id], caps[h.id]);
    }
    if (bad === 0) break;
  }

  if (best) ({ specs, lifetimes, spawns, pop } = best);
  const agents: Agent[] = [...fixed];
  let id = firstId;
  specs.forEach((k, i) => {
    const def = lib.enemies.get(k.type)!;
    const h = hits[k.hit];
    const rec: EnemyRecord = { id: id++, type: k.type, spawnW: spawns[i].w, deathW: h.w, sampleOffset: 0, sampleCount: 0, sampleW0: 0, fates: [] };
    const waypoints: Waypoint[] = [];
    // Elites take several hits: earlier hero hits within reach.
    if (def.size === "elite" && def.hits && def.hits > 1) {
      const need = def.hits - 1;
      const speed = plannedSpeed(def);
      let tx = k.x;
      let ty = k.y;
      let tw = h.w;
      for (const eh of [...hits].reverse()) {
        if (waypoints.length >= need) break;
        if (eh.source !== "move" || eh.w >= tw - 0.45 || eh.w < rec.spawnW + 1 || eh.w < h.w - 6) continue;
        const r = new Rng(ctx.seed, "elite", rec.id, eh.id);
        const p = sampleInShape(eh.shape, () => r.next(), def.radius * 0.5);
        if (dist(p.x, p.y, tx, ty) > speed * (tw - eh.w)) continue;
        if (reserved.some((q) => q.hit === eh.id && dist(q.x, q.y, p.x, p.y) < 2 * def.radius)) continue;
        if (!spotFree(p.x, p.y, eh.w, def.radius)) continue;
        takeSpot(p.x, p.y, eh.w, def.radius);
        reserved.push({ hit: eh.id, x: p.x, y: p.y });
        waypoints.unshift({ w: eh.w, x: p.x, y: p.y, hit: eh.id, lethal: false });
        rec.fates.unshift({ w: eh.w, x: p.x, y: p.y, hit: eh.id, lethal: false });
        tx = p.x;
        ty = p.y;
        tw = eh.w;
      }
    }
    waypoints.push({ w: h.w, x: k.x, y: k.y, hit: h.id, lethal: true });
    rec.fates.push({ w: h.w, x: k.x, y: k.y, hit: h.id, lethal: true });
    agents.push({ e: rec, def, waypoints, spawnX: spawns[i].x, spawnY: spawns[i].y, kinUntil: -Infinity, exempt: false, simulated: true });
  });



  // Every agent's first leg must be walkable: move spawns earlier where needed.
  for (const ag of agents) {
    if (ag.exempt || ag.waypoints.length === 0) continue;
    const first = ag.waypoints[0];
    const speed = plannedSpeed(ag.def) * 0.9;
    let w = ag.e.spawnW;
    let sx = ag.spawnX;
    let sy = ag.spawnY;
    const r = new Rng(ctx.seed, "leg0", ag.e.id);
    for (let tries = 0; tries < 8 && w > 0 && dist(sx, sy, first.x, first.y) / Math.max(0.1, first.w - w) > speed; tries++) {
      w = Math.max(0, first.w - (first.w - w) * 1.35);
      const p = spawnPoint(viewRect(cam, w), ag.def, first.x, first.y, r);
      sx = p.x;
      sy = p.y;
    }
    ag.e.spawnW = w;
    ag.spawnX = sx;
    ag.spawnY = sy;
  }

  agents.sort((a, b) => a.e.id - b.e.id);
  const fatesByHit: number[][] = hits.map(() => []);
  for (const a of agents) for (const wp of a.waypoints) if (wp.hit >= 0) fatesByHit[wp.hit].push(a.e.id);
  void hash32;
  return { agents, fatesByHit, chainPaths };
}


/** World-time windows of one bar each, across the song. */
export function barWindows(ctx: Ctx): [number, number][] {
  const out: [number, number][] = [];
  const wb = ctx.worldBeats!;
  for (let bar = 1; bar <= ctx.idx.barCount; bar++) {
    const b0 = ctx.idx.barStart[bar];
    const b1 = bar + 1 <= ctx.idx.barCount ? ctx.idx.barStart[bar + 1] : Math.min(wb.length - 1, b0 + ctx.idx.beatsPerBar[bar]);
    if (b1 > wb.length - 1) break;
    out.push([wb[b0], wb[b1]]);
  }
  return out;
}

export { HURTBOX_LIFT as HURTBOX_HEIGHT };
