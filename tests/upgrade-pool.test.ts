/**
 * The starting loadout and the upgrade pool (play mode's upgradePool): "The starting loadout" and
 * "The upgrade pool" in docs/design/gameplay-redesign.md, as pure lookups and through the
 * simulation on the synthetic song (on a seed whose first offer is the item under test).
 */
import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas, runPlanner } from "../scripts/lib/plan-node.ts";
import { recordingContext } from "../src/live/log.ts";
import { liveSession } from "../src/live/session.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import { prepare, virtualPlayer } from "../src/planner/plan.ts";
import { replay } from "../src/runtime/play.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { Sim, worldFor } from "../src/sim/simulate.ts";
import { chainAt, orbitAt, tierDamage } from "../src/sim/tiers.ts";
import { armouredBite, evolvedOrbit, loadoutAt, offersFor, OFFERS, passiveBonus, startingLoadout, takeItem, weaponUpgrades, type Loadout, type UpgradeItem } from "../src/sim/upgrades.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
const props = propsFromAtlas();
const { ctx, sch, map } = prepare(input, props);
const lib = ctx.lib;
const pool = ctx.level.upgrades;
const blades = lib.weapons.get("orbiting-blades")!;
const lightning = lib.weapons.get("chain-lightning")!;
const key = (i: UpgradeItem) => `${i.kind}:${i.id}`;

/** The first seed whose first `items.length` level-ups each offer that item first (taking it). */
function seedTaking(...items: string[]): number {
  for (let seed = 1; seed < 50_000; seed++) {
    const l = startingLoadout(pool.loadout);
    if (items.every((it, i) => {
      const first = offersFor(seed, i, lib, pool.offer, l)[0];
      takeItem(l, first);
      return key(first) === it;
    })) return seed;
  }
  throw new Error(`no seed takes ${items.join(", ")}`);
}

/** The moderate player's run on a seed, up to song time `until`. */
function runSeed(seed: number, until: number) {
  const c = { ...ctx, seed };
  const rec = new BatchRecorder();
  const sim = new Sim(c, sch, worldFor(c, sch, map), c.level.rules, rec, { player: virtualPlayer(c, sch, "moderate") });
  sim.advanceTo(until);
  return { rec, sim };
}

const levelUp = (k: number) => sch.levelUps[k].s;
const tierAt = (rec: BatchRecorder, s: number) => rec.tierV[rec.tierS.findLastIndex((x) => x <= s)];

describe("the starting loadout", () => {
  it("is the sword, orbiting blades and chain lightning, firing from the start without a level-up; the fire trail is an upgrade", () => {
    expect(pool).toEqual({ loadout: ["orbiting-blades", "chain-lightning"], offer: ["fire-trail"] });
    const { rec } = runSeed(ctx.seed, levelUp(0) - 0.1);
    expect(rec.weaponsGranted).toEqual([
      { id: "orbiting-blades", s: 0 },
      { id: "chain-lightning", s: 0 },
    ]);
    expect(rec.upgrades).toEqual([]);
    const fired = (id: string) => rec.weaponFires.filter((f) => f.weapon === id && f.s < 10).length;
    expect(fired("orbiting-blades")).toBeGreaterThan(5);
    expect(fired("chain-lightning")).toBeGreaterThan(1);
  });
});

describe("offers", () => {
  const loadouts: [string, Loadout][] = [];
  {
    const l = startingLoadout(pool.loadout);
    loadouts.push(["fresh", structuredClone(l)]);
    takeItem(l, { kind: "weapon", id: "fire-trail" });
    loadouts.push(["with the trail", structuredClone(l)]);
    takeItem(l, { kind: "evolve", id: "orbiting-blades" });
    takeItem(l, { kind: "passive", id: "might" });
    loadouts.push(["one weapon upgrade left", structuredClone(l)]);
    takeItem(l, { kind: "evolve", id: "chain-lightning" });
    loadouts.push(["every weapon upgrade taken", structuredClone(l)]);
  }

  it("are three different items, at least one a weapon upgrade while any remain", () => {
    for (const [name, l] of loadouts) {
      const upgrades = weaponUpgrades(lib, pool.offer, l).map(key);
      for (let seed = 1; seed <= 200; seed++) {
        for (let index = 0; index < 3; index++) {
          const offers = offersFor(seed, index, lib, pool.offer, l).map(key);
          expect(offers.length, name).toBe(OFFERS);
          expect(new Set(offers).size, `${name}: ${offers}`).toBe(OFFERS);
          if (upgrades.length > 0) expect(offers.some((o) => upgrades.includes(o)), `${name}: ${offers}`).toBe(true);
          else expect(offers.every((o) => o.startsWith("passive:")), `${name}: ${offers}`).toBe(true);
          // Nothing owned is offered again (passives can be).
          for (const o of offers) expect(o.startsWith("passive:") || upgrades.includes(o), `${name}: ${o}`).toBe(true);
        }
      }
    }
    expect(weaponUpgrades(lib, pool.offer, loadouts[0][1]).map(key)).toEqual(["weapon:fire-trail", "evolve:orbiting-blades", "evolve:chain-lightning"]);
  });

  it("are the same for the same seed and level-up index, and vary with either", () => {
    const l = loadouts[0][1];
    const seen = new Set<string>();
    for (let seed = 1; seed <= 50; seed++) {
      for (let index = 0; index < 3; index++) {
        const a = offersFor(seed, index, lib, pool.offer, l);
        expect(offersFor(seed, index, lib, pool.offer, structuredClone(l))).toEqual(a);
        seen.add(a.map(key).join(","));
      }
    }
    expect(seen.size).toBeGreaterThan(30);
    // Each item can come first (the one a level-up takes until upgrade orbs).
    const firsts = new Set<string>();
    for (let seed = 1; seed <= 400; seed++) firsts.add(key(offersFor(seed, 0, lib, pool.offer, l)[0]));
    expect([...firsts].sort()).toEqual(["evolve:chain-lightning", "evolve:orbiting-blades", "passive:armour", "passive:magnet", "passive:max-health", "passive:might", "weapon:fire-trail"]);
  });

  it("are recorded with the one taken, its first, at each level-up's moment", () => {
    const { rec } = runSeed(ctx.seed, levelUp(1) + 0.1);
    expect(rec.upgrades.map((u) => u.s)).toEqual([levelUp(0), levelUp(1)]);
    let l = startingLoadout(pool.loadout);
    rec.upgrades.forEach((u, i) => {
      expect(u.taken).toBe(0);
      expect(u.offers).toEqual(offersFor(ctx.seed, i, lib, pool.offer, l));
      l = loadoutAt(pool.loadout, rec.upgrades, u.s);
    });
  });
});

describe("evolutions", () => {
  it("give the blades two more blades and a 20% wider orbit at every tier", () => {
    expect([0, 1, 2, 3].map((t) => evolvedOrbit(blades, orbitAt(blades, t), true))).toEqual([
      { count: 5, radius: 72 * 1.2 },
      { count: 5, radius: 90 * 1.2 },
      { count: 6, radius: 105 * 1.2 },
      { count: 7, radius: 120 * 1.2 },
    ]);
    expect(evolvedOrbit(blades, orbitAt(blades, 2), false)).toEqual({ count: 4, radius: 105 });

    const seed = seedTaking("evolve:orbiting-blades");
    const { rec } = runSeed(seed, levelUp(0) + 10);
    const s0 = rec.upgrades[0].s;
    expect(rec.upgrades[0].offers[rec.upgrades[0].taken]).toEqual({ kind: "evolve", id: "orbiting-blades" });
    const firings = new Map<number, typeof rec.hits>();
    for (const h of rec.hits) if (h.what === "orbiting-blades" && h.s > s0 - 4) firings.set(h.s, [...(firings.get(h.s) ?? []), h]);
    let before = 0;
    let after = 0;
    for (const [s, hits] of firings) {
      const evolved = s >= s0;
      const want = evolvedOrbit(blades, orbitAt(blades, tierAt(rec, s)), evolved);
      expect(hits.length, `blades at ${s}`).toBe(want.count);
      // The blades lie on the orbit (squashed to 0.8 across): its centre is their mean.
      const cx = hits.reduce((a, h) => a + (h.shape as { x: number }).x, 0) / hits.length;
      const cy = hits.reduce((a, h) => a + (h.shape as { y: number }).y, 0) / hits.length;
      for (const h of hits) {
        const { x, y } = h.shape as { x: number; y: number };
        expect(Math.hypot(x - cx, (y - cy) / 0.8)).toBeCloseTo(want.radius, 6);
      }
      if (evolved) after++;
      else before++;
    }
    expect(before).toBeGreaterThan(4);
    expect(after).toBeGreaterThan(4);
  });

  it("make the lightning fire on every beat instead of every bar, with one more jump", () => {
    expect(lightning.fires.every).toBe("bar");
    expect(lightning.evolve).toMatchObject({ every: "beat", jumps: 1 });
    const seed = seedTaking("evolve:chain-lightning");
    // (Well into the run, where enough enemies stand in range for the extra jump.)
    const s1 = levelUp(0) + 60;
    const { rec } = runSeed(seed, s1);
    const s0 = rec.upgrades[0].s;
    const fires = rec.weaponFires.filter((f) => f.weapon === "chain-lightning").map((f) => f.s);
    // Before: on the bar's downbeat only; after: on every beat but those inside a hit-stop (weapons rest in one).
    const rests = (t: number) => rec.hitStops.some(([a, b]) => t >= a - 1e-9 && t < b - 1e-9);
    const onBeat = (b: number) => fires.some((f) => Math.abs(f - b) < 1e-6);
    const before = ctx.song.beats.filter((b) => b > s0 - 12 && b < s0 - 0.01 && !rests(b));
    const after = ctx.song.beats.filter((b) => b > s0 + 0.01 && b < s1 - 0.5 && !rests(b));
    expect(before.filter(onBeat).length).toBeLessThan(0.6 * before.length);
    for (const b of after) expect(onBeat(b), `a firing on the beat at ${b}`).toBe(true);
    // One more jump than the tier's: each firing starts a chain (its start, then a point per jump),
    // which ends early only when no enemy is in range.
    expect(rec.chains.length).toBe(fires.length);
    let most = 0;
    fires.forEach((f, i) => {
      const jumps = rec.chains[i].points.length - 1;
      const tiered = chainAt(lightning, tierAt(rec, f))!.jumps;
      const allowed = tiered + (f >= s0 ? 1 : 0);
      expect(jumps, `the chain at ${f}`).toBeLessThanOrEqual(allowed);
      if (f >= s0) most = Math.max(most, jumps - tiered);
    });
    expect(most).toBe(1);
  });
});

describe("passives", () => {
  it("add up when taken again, and armour never takes a bite below 1", () => {
    expect(passiveBonus(lib, {})).toEqual({ maxHealth: 0, pickup: 0, damage: 0, armour: 0 });
    expect(passiveBonus(lib, { "max-health": 2, magnet: 1, might: 3, armour: 2 })).toEqual({ maxHealth: 40, pickup: 0.3, damage: 0.1 * 3, armour: 2 });
    const l = startingLoadout(pool.loadout);
    takeItem(l, { kind: "passive", id: "might" });
    takeItem(l, { kind: "passive", id: "might" });
    expect(l.passives).toEqual({ might: 2 });
    for (const c of [2, 3, 4, 5, 6, 8]) {
      expect(armouredBite(c, 0)).toBe(c);
      expect(armouredBite(c, 1)).toBe(c - 1);
      expect(armouredBite(c, 50)).toBe(1);
    }
    expect(armouredBite(1, 3)).toBe(1);
  });

  /**
   * Whether every blades hit in [from, to) deals its tier's damage times `mult`. (The tier read when
   * it fired: one changing at the same instant may come before or after the firing.)
   */
  const bladesDeal = (rec: BatchRecorder, from: number, to: number, mult: number) => {
    const hits = rec.hits.filter((h) => h.what === "orbiting-blades" && h.s >= from && h.s < to);
    const tiers = (s: number) => [tierAt(rec, s), rec.tierV[rec.tierS.findLastIndex((x) => x < s)] ?? 0];
    const off = hits.filter((h) => !tiers(h.s).some((t) => Math.abs(h.damage - blades.damage * tierDamage(lib.hero, t) * mult) < 1e-9));
    return { n: hits.length, off: off.map((h) => `${h.s.toFixed(3)}: ${h.damage}`) };
  };

  it("might: damage 10% higher", () => {
    const { rec } = runSeed(seedTaking("passive:might"), levelUp(0) + 6);
    const s0 = rec.upgrades[0].s;
    const before = bladesDeal(rec, s0 - 6, s0, 1);
    const after = bladesDeal(rec, s0, Infinity, 1.1);
    expect(before.n).toBeGreaterThan(10);
    expect(after.n).toBeGreaterThan(10);
    expect(before.off).toEqual([]);
    expect(after.off).toEqual([]);
  });

  it("stack: might taken twice deals 20% more", () => {
    const { rec } = runSeed(seedTaking("passive:might", "passive:might"), levelUp(1) + 6);
    expect(rec.upgrades.map((u) => key(u.offers[u.taken]))).toEqual(["passive:might", "passive:might"]);
    const after = bladesDeal(rec, rec.upgrades[1].s, Infinity, 1.2);
    expect(after.n).toBeGreaterThan(10);
    expect(after.off).toEqual([]);
  });

  it("magnet: the pickup radius 30% wider", () => {
    const radius = lib.hero.pickupRadius!;
    const { rec, sim } = runSeed(seedTaking("passive:magnet"), levelUp(0) - 0.5);
    expect(sim.observe().hero.pickup).toBeCloseTo(radius[tierAt(rec, sim.s)], 9);
    sim.advanceTo(levelUp(0) + 6);
    expect(rec.upgrades[0].offers[0]).toEqual({ kind: "passive", id: "magnet" });
    expect(sim.observe().hero.pickup).toBeCloseTo(radius[tierAt(rec, sim.s)] * 1.3, 9);
  });

  /** The hero's health changes after song time `from` (in health points, by the maximum then) and the moment of each. */
  const healthSteps = (rec: BatchRecorder, max: (s: number) => number) =>
    rec.healthV.map((v, i) => ({ s: rec.healthS[i], d: (v - (rec.healthV[i - 1] ?? 1)) * max(rec.healthS[i]), v })).filter((x) => x.s > 0);

  it("max health: 20 more, healed at once", () => {
    const { rec } = runSeed(seedTaking("passive:max-health"), levelUp(0) + 40);
    const s0 = rec.upgrades[0].s;
    const base = lib.hero.health.max;
    const at = rec.healthS.findIndex((s) => s === s0);
    expect(at, "a health change at the level-up").toBeGreaterThanOrEqual(0);
    const prev = rec.healthV[at - 1] ?? 1;
    expect(rec.healthV[at]).toBeCloseTo(Math.min(base + 20, prev * base + 20) / (base + 20), 9);
    // Later bites are whole points of the larger maximum.
    const later = healthSteps(rec, (s) => (s >= s0 ? base + 20 : base)).filter((x) => x.s > s0 && x.d < 0);
    expect(later.length).toBeGreaterThan(0);
    for (const x of later) expect(Math.abs(x.d - Math.round(x.d)), `a bite at ${x.s}`).toBeLessThan(1e-6);
  });

  it("armour: every bite takes 1 less", () => {
    const { rec } = runSeed(seedTaking("passive:armour"), levelUp(0) + 60);
    const s0 = rec.upgrades[0].s;
    const contact = new Set([...lib.enemies.values()].map((e) => e.contactDamage).filter((c) => c > 0));
    const attacks = new Set([...lib.enemies.values()].map((e) => e.attackDamage).filter((c) => c > 0));
    const bites = healthSteps(rec, () => lib.hero.health.max).filter((x) => x.d < 0).map((x) => ({ s: x.s, d: Math.round(-x.d) }));
    const armoured = new Set([...contact].map((c) => Math.max(1, c - 1)));
    const before = bites.filter((b) => b.s < s0);
    const after = bites.filter((b) => b.s >= s0);
    expect(after.length).toBeGreaterThan(0);
    for (const b of before) expect(contact.has(b.d) || attacks.has(b.d), `a bite of ${b.d} at ${b.s}`).toBe(true);
    for (const b of after) expect(armoured.has(b.d) || attacks.has(b.d), `a bite of ${b.d} at ${b.s}`).toBe(true);
    // (Here the minotaur's bite of 10 becomes 9.)
    expect(after.some((b) => !contact.has(b.d))).toBe(true);
  });
});

describe("the picture", () => {
  it("draws evolved blades, widens the pickup ring with the magnet, and shows evolutions and passive counts on the HUD", () => {
    const seed = seedTaking("evolve:orbiting-blades", "passive:magnet");
    const plan = runPlanner({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml", seed, profile: "moderate" });
    const [u0, u1] = plan.upgrades!;
    expect([u0, u1].map((u) => key(u.offers[u.taken]))).toEqual(["evolve:orbiting-blades", "passive:magnet"]);
    const ev = new Evaluator(plan, lib);
    const tier = (s: number) => plan.tier!.v[Array.from(plan.tier!.s).findLastIndex((t) => t <= s)];
    const at = (s: number) => ({ f: ev.evaluate(s), t: tier(s) });
    const before = at(u0.s - 1);
    expect(before.f.blades.length).toBe(orbitAt(blades, before.t).count);
    expect(before.f.hud.weapons).toEqual([expect.objectContaining({ id: "orbiting-blades", evolved: false }), expect.objectContaining({ id: "chain-lightning", evolved: false })]);
    expect(before.f.hud.passives).toEqual([]);
    expect(before.f.pickup!.radius).toBeCloseTo(lib.hero.pickupRadius![before.t], 9);
    const evolved = at(u0.s + 1);
    expect(evolved.f.blades.length).toBe(evolvedOrbit(blades, orbitAt(blades, evolved.t), true).count);
    expect(evolved.f.hud.weapons.map((w) => w.evolved)).toEqual([true, false]);
    const magnet = at(u1.s + 1);
    expect(magnet.f.pickup!.radius).toBeCloseTo(lib.hero.pickupRadius![magnet.t] * 1.3, 9);
    expect(magnet.f.hud.passives).toEqual([{ id: "magnet", count: 1 }]);
  });
});

describe("watch my run", () => {
  it("replays a run across level-ups exactly, offers and all, and the live run records what a batch run does", () => {
    const { rec } = runSeed(ctx.seed, levelUp(1) + 3);
    const keys: KeyEvent[] = rec.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key }));
    const startS = levelUp(0) - 6;
    const session = liveSession(input, { props, startS });
    let next = keys.findIndex((k) => k.s >= startS);
    for (let now = startS + 0.0037; now < levelUp(1) + 2; now += 1 / 60) {
      const due: KeyEvent[] = [];
      while (next < keys.length && keys[next].s <= now) due.push(keys[next++]);
      session.runner.frame(now, due);
    }
    session.runner.finish();
    const live = session.recording.plan.upgrades!;
    expect(live.length).toBeGreaterThanOrEqual(2);
    expect(live).toEqual(rec.upgrades.filter((u) => u.s <= levelUp(1) + 2));
    const again = replay(input, props, startS, session.runner.applied);
    expect(again.hashParts()).toEqual(session.recording.hashParts());
    expect(again.plan.upgrades).toEqual(live);
    // The play log carries each level-up's offers and the one taken.
    const r = session.recording;
    expect(recordingContext(r.plan, r.timeWarp, r.s).upgrades).toEqual(live.map((u) => ({ s: Math.round(u.s * 1e6) / 1e6, offers: u.offers.map(key), taken: 0 })));
  }, 120_000);
});
