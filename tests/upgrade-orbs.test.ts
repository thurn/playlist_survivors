/**
 * Upgrade orbs (play mode's upgradeOrbs): "Level-ups and upgrade orbs" in
 * docs/design/gameplay-redesign.md, on the real song, following the design's trace from the
 * galop opening's level-up downbeat at 54.07: the moderate player plays up to a moment, then
 * scripted keys take over.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { firstAtOrAfter, NEVER_LATE } from "../src/sim/timing.ts";
import { LiveRecording } from "../src/live/recording.ts";
import { enemyPos, heroSample } from "../src/plan/sample.ts";
import { prepare, virtualPlayer } from "../src/planner/plan.ts";
import type { Key } from "../src/sim/movement.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { LEVEL_PULSE, ORB_RISE, ORB_SPREAD, ORB_TOUCH, Sim, worldFor } from "../src/sim/simulate.ts";
import { LOW_HEALTH, preferredOffer } from "../src/sim/upgrades.ts";

const input = plannerInput({ level: "levels/orpheus-galop.play.yaml" });
const props = propsFromAtlas();
const { ctx, sch, map } = prepare(input, props);
const lib = ctx.lib;
const LEVEL_UP = sch.levelUps[0].s;

// The moderate player's keys, from a headless run.
const botKeys: KeyEvent[] = (() => {
  const rec = new BatchRecorder();
  new Sim(ctx, sch, worldFor(ctx, sch, map), ctx.level.rules, rec, { player: virtualPlayer(ctx, sch, "moderate") }).advanceTo(LEVEL_UP + 10);
  return rec.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key }));
})();

/**
 * A live recording of the moderate player's keys up to song time `until`, then none (the sim is
 * left there for scripted keys), or none only until `resume`, when its keys go on.
 */
function takeOver(until: number, resume = Infinity, schedule = sch, c = ctx) {
  const recording = new LiveRecording(c, schedule, map, c.lib, "player", props);
  const sim = new Sim(c, schedule, worldFor(c, schedule, map), c.level.rules, recording);
  for (const k of botKeys) if (k.s < until || k.s >= resume) sim.key(k);
  // (A key the player still held is let go.)
  const held = new Set<string>();
  for (const k of botKeys) {
    if (k.s >= until) continue;
    if (k.kind === "press") held.add(k.key);
    else held.delete(k.key);
  }
  for (const key of held) sim.key({ s: until, kind: "release", key: key as Key });
  return { recording, sim };
}

/** The keys (one, or a diagonal's two) whose direction is nearest to (dx, dy). */
function keysToward(dx: number, dy: number): Key[] {
  const k = Math.round((Math.atan2(dy, dx) / (2 * Math.PI)) * 8);
  return [["right"], ["right", "down"], ["down"], ["down", "left"], ["left"], ["left", "up"], ["up"], ["up", "right"]][((k % 8) + 8) % 8] as Key[];
}

const beat = (s: number) => ctx.song.beats.findIndex((b) => b > s - 1e-9);
const nextGrid = (s: number) => sch.gridTimes[firstAtOrAfter(sch.gridTimes, s + NEVER_LATE - 1e-9)];

describe("a level-up's downbeat", () => {
  // (A schedule whose level-ups need 1 experience: standing still, the hero still levels up on time.)
  const easy = { ...sch, levelUps: sch.levelUps.map((l) => ({ ...l, xp: 1 })) };

  it("pushes enemies within 160 px out to 160 px and raises three orbs 120 px away: ahead, and 120° to each side", () => {
    // (A crowd to push, at theme A's repeat, 1:20.2: the hero has no blades or lightning here and
    // stands still for the last 16 beats before the level-up, whose bar needs 1 experience; the
    // enemies are 20 times as tough, so some survive the sword's strike on the same downbeat.)
    const bare = {
      ...ctx,
      level: { ...ctx.level, upgrades: { ...ctx.level.upgrades, loadout: [] } },
      lib: { ...lib, enemies: new Map([...lib.enemies].map(([k, e]) => [k, { ...e, health: e.health * 20 }])) },
    };
    const L = sch.levelUps[1].s;
    const { recording, sim } = takeOver(ctx.song.beats[beat(L) - 16], L, easy, bare);
    sim.advanceTo(L + 0.5);
    recording.refresh();
    const plan = recording.plan;
    const u = plan.upgrades!.find((x) => x.s === L)!;
    expect(u.s).toBe(L);
    // The orbs. (Three orbs 120° apart around the hero: their middle is where it stood; its sample
    // there is interpolated, a few px off.)
    const hp = { x: u.orbs!.reduce((a, o) => a + o.x, 0) / 3, y: u.orbs!.reduce((a, o) => a + o.y, 0) / 3 };
    const hs = heroSample(plan, u.w!);
    expect(Math.hypot(hs.x - hp.x, hs.y - hp.y)).toBeLessThan(4);
    const ang = u.orbs!.map((o) => {
      expect(Math.hypot(o.x - hp.x, o.y - hp.y)).toBeCloseTo(ORB_RISE, 6);
      return Math.atan2(o.y - hp.y, o.x - hp.x);
    });
    const turn = (a: number, b: number) => Math.atan2(Math.sin(b - a), Math.cos(b - a));
    expect(turn(ang[0], ang[1])).toBeCloseTo(-ORB_SPREAD, 6);
    expect(turn(ang[0], ang[2])).toBeCloseTo(ORB_SPREAD, 6);
    // The pulse: every enemy within 160 px is pushed out to 160 px over the knockback's 0.12 s (less
    // what it walks back meanwhile), even one the sword strikes on the same downbeat. (Not one
    // killed meanwhile.)
    const w1 = u.w! + 0.15;
    const p = { x: 0, y: 0 };
    let pushed = 0;
    let struck = 0;
    for (const e of plan.enemies) {
      if (e.spawnW > u.w! || e.deathW <= w1) continue;
      const d0 = Math.hypot(enemyPos(plan, e, u.w!, p).x - hp.x, p.y - hp.y);
      if (d0 >= LEVEL_PULSE - 1) continue;
      const d1 = Math.hypot(enemyPos(plan, e, w1, p).x - hp.x, p.y - hp.y);
      pushed++;
      if (e.fates.some((f) => f.w >= u.w! - 1e-9 && f.w <= w1)) struck++;
      const back = lib.enemies.get(e.type)!.speed * 1.5 * (w1 - u.w!);
      expect(d1, `#${e.id} ${e.type} from ${d0.toFixed(0)} px`).toBeGreaterThan(LEVEL_PULSE - back - 12);
    }
    expect(pushed).toBeGreaterThan(3);
    expect(struck).toBeGreaterThan(0);
  });

  it("heals 20", () => {
    // The hero stands still for the last 16 beats before the level-up without blades or
    // lightning, so it is bitten (more than 20 short of its 100: the heal isn't cut short by a full bar).
    const unarmed = { ...ctx, level: { ...ctx.level, upgrades: { ...ctx.level.upgrades, loadout: [] } } };
    const { recording, sim } = takeOver(ctx.song.beats[beat(LEVEL_UP) - 16], LEVEL_UP, easy, unarmed);
    sim.advanceTo(LEVEL_UP + 0.2);
    recording.refresh();
    const plan = recording.plan;
    expect(plan.upgrades![0].s).toBe(LEVEL_UP);
    const at = Array.from(plan.health.s).findIndex((s) => s === LEVEL_UP);
    const before = plan.health.v[at - 1];
    expect(before * lib.hero.health.max).toBeLessThan(lib.hero.health.max - lib.hero.health.levelUpHeal);
    expect(plan.health.v[at] * lib.hero.health.max - before * lib.hero.health.max).toBeCloseTo(lib.hero.health.levelUpHeal, 3);
    expect(plan.heals!.find((h) => h.s === LEVEL_UP)).toMatchObject({ source: "level-up" });
  });
});

describe("walking into an orb", () => {
  it("within 24 px takes it, applying on the next grid time with its sound, and the others sink (the design's trace)", () => {
    // 54.07: the orbs rise; 54.30 (the next beat): the player presses toward the one it wants and
    // steers there on each beat until it reaches it.
    const { recording, sim } = takeOver(LEVEL_UP - 0.1);
    sim.advanceTo(LEVEL_UP + 0.01);
    const u = recording.plan.upgrades![0];
    const want = preferredOffer(u.offers);
    const orb = u.orbs![want];
    let held: Key[] = [];
    for (let b = beat(LEVEL_UP + 0.01); u.sTaken === undefined && b < beat(LEVEL_UP) + 8; b++) {
      const s = ctx.song.beats[b];
      sim.advanceTo(s - 0.001);
      const hp = sim.heroPosition();
      const keys = keysToward(orb.x - hp.x, orb.y - hp.y);
      for (const k of held) sim.key({ s, kind: "release", key: k });
      keys.forEach((k, i) => sim.key({ s: s + 0.005 * i, kind: "press", key: k }));
      held = keys;
    }
    sim.advanceTo(LEVEL_UP + 6);
    recording.refresh();
    const plan = recording.plan;
    expect(u.taken).toBe(want);
    expect(u.walked).toBe(true);
    expect(u.sTaken! - LEVEL_UP).toBeLessThan(2 * 2 * 0.342 + 0.01);
    // Taken on the fixed step the hero came within 24 px: never before it.
    const hp = heroSample(plan, u.wTaken!);
    expect(Math.hypot(hp.x - orb.x, hp.y - orb.y)).toBeLessThanOrEqual(ORB_TOUCH + 6);
    for (let w = u.w!; w < u.wTaken! - 0.02; w += 0.01) {
      const h = heroSample(plan, w);
      expect(Math.hypot(h.x - orb.x, h.y - orb.y), `at w=${w.toFixed(2)}`).toBeGreaterThan(ORB_TOUCH - 6);
    }
    // It applies on the first grid time at least 60 ms later, with the orb's sound there.
    expect(u.sApply).toBeCloseTo(nextGrid(u.sTaken!), 9);
    const orbSounds = plan.events.sounds.filter((x) => x.key === "orb");
    expect(orbSounds.map((x) => x.s)).toEqual([u.sApply]);
    expect(orbSounds[0].movedFrom).toBeUndefined();
    // The upgrade itself: the blades evolve on that grid time (if that is the orb), a passive or weapon otherwise.
    const item = u.offers[want];
    if (item.kind === "evolve" && item.id === "orbiting-blades") {
      const count = (s0: number, s1: number) => {
        const firings = new Map<number, number>();
        for (const h of plan.hits) if (h.what === "orbiting-blades" && h.s >= s0 && h.s < s1) firings.set(h.s, (firings.get(h.s) ?? 0) + 1);
        return Math.max(...firings.values());
      };
      expect(count(u.sApply!, u.sApply! + 1)).toBeGreaterThan(count(LEVEL_UP, u.sApply!));
    }
  });

  it("is left to two bars: then the orb that rose ahead is taken", () => {
    const { recording, sim } = takeOver(LEVEL_UP - 0.1);
    sim.advanceTo(LEVEL_UP + 6);
    recording.refresh();
    const u = recording.plan.upgrades![0];
    const bar = ctx.idx.barStart.findIndex((b) => Math.abs(ctx.song.beats[b] - LEVEL_UP) < 1e-6);
    const deadline = ctx.song.beats[ctx.idx.barStart[bar + 2]];
    expect(u.walked).toBe(false);
    expect(u.taken).toBe(0);
    expect(u.sTaken).toBeCloseTo(deadline, 9);
    expect(u.sApply).toBeCloseTo(nextGrid(deadline), 9);
  });
});

describe("level-ups close together", () => {
  it("never offer what was taken but not yet applied: each weapon upgrade is taken once", () => {
    // (Level-ups on consecutive downbeats from the galop opening, each needing 1 experience: each
    // comes while the one before is still waiting for its orbs, or for its grid time.)
    const bar0 = ctx.idx.barStart.findIndex((b) => Math.abs(ctx.song.beats[b] - LEVEL_UP) < 1e-6);
    const levelUps = [0, 1, 2, 3, 4, 5].map((k) => ({ id: "offer", s: ctx.song.beats[ctx.idx.barStart[bar0 + k]], xp: 1 }));
    const bunched = { ...sch, levelUps };
    const { recording, sim } = takeOver(LEVEL_UP + 20, Infinity, bunched);
    sim.advanceTo(LEVEL_UP + 20);
    recording.refresh();
    const ups = recording.plan.upgrades!;
    // (Standing still, a bar can wait a downbeat for its one gem.)
    expect(ups.length).toBeGreaterThanOrEqual(4);
    const taken = ups.map((u) => `${u.offers[u.taken].kind}:${u.offers[u.taken].id}`);
    const weapons = taken.filter((t) => !t.startsWith("passive:"));
    expect(new Set(weapons).size).toBe(weapons.length);
    // Every later level-up offers none of the weapon upgrades taken before it.
    ups.forEach((u, i) => {
      for (const o of u.offers) if (o.kind !== "passive") expect(taken.slice(0, i), `level-up ${i}`).not.toContain(`${o.kind}:${o.id}`);
    });
    // (Some were taken while the next was due: the one ahead, at the next level-up's moment.)
    expect(ups.some((u, i) => i + 1 < ups.length && u.sTaken === ups[i + 1].s && !u.walked)).toBe(true);
  });
});

describe("the virtual players' choice", () => {
  it("follows a fixed priority, with max health first below half health", () => {
    const o = (...ids: string[]) => ids.map((x) => ({ kind: x.split(":")[0] as "weapon" | "evolve" | "passive", id: x.split(":")[1] }));
    expect(preferredOffer(o("weapon:fire-trail", "evolve:orbiting-blades", "passive:might"))).toBe(1);
    expect(preferredOffer(o("passive:magnet", "passive:might", "evolve:chain-lightning"))).toBe(2);
    expect(preferredOffer(o("passive:armour", "passive:max-health", "weapon:fire-trail"))).toBe(2);
    expect(preferredOffer(o("passive:armour", "passive:max-health", "weapon:fire-trail"), LOW_HEALTH - 0.01)).toBe(1);
    expect(preferredOffer(o("passive:armour", "passive:magnet", "passive:max-health"))).toBe(1);
  });
});
