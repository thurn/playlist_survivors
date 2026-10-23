/**
 * Self-firing weapons sized by the tier (play mode): "Attacks" in
 * docs/design/gameplay-redesign.md, as content lookups and through the
 * simulation on the synthetic song.
 */
import { describe, expect, it } from "vitest";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { chainAt, orbitAt, swingArcAt, tierDamage, trailRadiusAt } from "../src/sim/tiers.ts";
import { evolvedOrbit, loadoutAt, passiveBonus } from "../src/sim/upgrades.ts";
import { Sim, worldFor } from "../src/sim/simulate.ts";
import { prepare } from "../src/planner/plan.ts";
import { plannerInput } from "../scripts/lib/plan-node.ts";
import { play, tap } from "./fixtures/play-sim.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const { ctx, sch, section, run } = play(syntheticSongMap());
const hero = ctx.lib.hero;
const weapon = (id: string) => ctx.lib.weapons.get(id)!;

describe("what a tier changes", () => {
  it("sizes the sword, the blades, the lightning and the trail, and the damage, at tiers 0 to 3", () => {
    expect([0, 1, 2, 3].map((t) => swingArcAt(hero, t))).toEqual([
      { radius: 100, degrees: 150 },
      { radius: 130, degrees: 180 },
      { radius: 160, degrees: 240 },
      { radius: 190, degrees: 360 },
    ]);
    expect([0, 1, 2, 3].map((t) => orbitAt(weapon("orbiting-blades"), t))).toEqual([
      { count: 3, radius: 72 },
      { count: 3, radius: 90 },
      { count: 4, radius: 105 },
      { count: 5, radius: 120 },
    ]);
    expect([0, 1, 2, 3].map((t) => chainAt(weapon("chain-lightning"), t))).toEqual([
      { jumps: 3, range: 110 },
      { jumps: 4, range: 125 },
      { jumps: 5, range: 140 },
      { jumps: 6, range: 160 },
    ]);
    expect([0, 1, 2, 3].map((t) => trailRadiusAt(weapon("fire-trail"), t))).toEqual([20, 26, 32, 38]);
    expect([0, 1, 2, 3].map((t) => tierDamage(hero, t))).toEqual([1, 1.1, 1.2, 1.3]);
  });
});

describe("the self-firing sword", () => {
  const sec = section("theme-a");
  const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 0.5 && g < sec.s0 + 9);

  it("swings on every grid time by itself", () => {
    const rec = new BatchRecorder();
    run(beats.slice(0, 4).flatMap((b) => tap(b)), [beats[12] + 0.01], rec);
    const struck = new Set(rec.swings.map((sw) => Math.round(sw.s * 1e4)));
    for (const b of beats.slice(0, 13)) expect(struck.has(Math.round(b * 1e4)), `a swing on ${b}`).toBe(true);
  });

  it("is sized by the tier when its wind-up starts, whatever the tier does before it strikes", () => {
    // From the groove at 0, the 6th Perfect press on the beat lifts it to tier 1, at that beat:
    // after its swing's wind-up started (about 190 ms before), so that swing stays tier 0.
    const rec = new BatchRecorder();
    const keys: KeyEvent[] = beats.slice(0, 8).flatMap((b) => tap(b));
    run(keys, [beats[9]], rec);
    const swingAt = (s: number) => rec.swings.find((sw) => Math.abs(sw.s - s) < 1e-6)!;
    const hit = (s: number) => rec.hits[swingAt(s).hit];
    expect(rec.tierS[rec.tierV.indexOf(1)]).toBeCloseTo(beats[5], 9);
    expect(swingAt(beats[5])).toMatchObject({ tier: 0, reach: { radius: 100, degrees: 150 } });
    expect(swingAt(beats[6])).toMatchObject({ tier: 1, reach: { radius: 130, degrees: 180 } });
    // Its hit's arc and damage follow the swing's tier.
    expect(hit(beats[5]).shape).toMatchObject({ kind: "arc", radius: 100 });
    expect(hit(beats[6]).shape).toMatchObject({ kind: "arc", radius: 130 });
    expect(hit(beats[5]).damage).toBeCloseTo(hero.swings.light.damage * 1.0, 9);
    expect(hit(beats[6]).damage).toBeCloseTo(hero.swings.light.damage * 1.1, 9);
  });
});

describe("the blades", () => {
  it("circle in their tier's number, and hit for their tier's damage", () => {
    // The blades are in the starting loadout (evolved, if a level-up takes their evolution); from the
    // galop opening, Perfect presses climb to tier 3.
    const sec = section("galop-opening");
    const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 0.5 && g < sec.s0 + 9);
    const rec = new BatchRecorder();
    run(beats.slice(0, 14).flatMap((b) => tap(b)), [beats[18]], rec);
    const tierAt = (s: number) => rec.tierV[rec.tierS.findLastIndex((x) => x <= s)];
    const firings = new Map<number, typeof rec.hits>();
    for (const h of rec.hits) if (h.what === "orbiting-blades") firings.set(h.s, [...(firings.get(h.s) ?? []), h]);
    expect(firings.size).toBeGreaterThan(10);
    const seen = new Set<number>();
    for (const [s, hits] of firings) {
      const t = tierAt(s);
      seen.add(t);
      // (With what level-ups took by then: evolved blades, and the might passive's damage.)
      const l = loadoutAt([], rec.upgrades, s);
      expect(hits.length, `blades firing at ${s} (tier ${t})`).toBe(evolvedOrbit(weapon("orbiting-blades"), orbitAt(weapon("orbiting-blades"), t), l.evolved.includes("orbiting-blades")).count);
      const might = 1 + passiveBonus(ctx.lib, l.passives).damage;
      for (const h of hits) expect(h.damage).toBeCloseTo(weapon("orbiting-blades").damage * tierDamage(hero, t) * might, 9);
    }
    expect(seen.has(0) && seen.has(3)).toBe(true);
  });
});

describe("the sword without a steady beat", () => {
  it("swings on every beat of the song map, a strong anchor's cleave taking the beat within half a beat of it", () => {
    const rec = new BatchRecorder();
    const prelude = section("prelude");
    run([], [prelude.s1 - 0.01], rec);
    const beats = ctx.song.beats.filter((b) => b > prelude.s0 + 0.5 && b < prelude.s1 - 0.5);
    const near = (b: number) => rec.swings.some((sw) => Math.abs(sw.s - b) < 1e-4);
    const covered = beats.filter((b, k) => near(b) || rec.swings.some((sw) => sw.kind === "heavy" && Math.abs(sw.s - b) < 0.5 * ((beats[k + 1] ?? b + 1) - b)));
    expect(covered.length).toBe(beats.length);
  });
});

describe("the virtual player's sense of its stride", () => {
  it("counts the ground a press would take back from a stride still moving", () => {
    const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
    const { ctx: c, sch: sc } = prepare(input);
    const sec = c.sections.find((s) => s.label === "theme-a")!;
    const b = Array.from(sc.gridTimes).filter((g) => g > sec.s0 + 0.5);
    const rec = new BatchRecorder();
    const sim = new Sim(c, sc, worldFor(), rec);
    sim.advanceTo(b[0] - 0.01);
    sim.key({ s: b[0], kind: "press", key: "right" });
    // 40 ms into the held stride on the next beat, a press would take its uncovered ground back.
    sim.advanceTo(b[1] + 0.04);
    const seen = sim.observe().hero.stride!;
    sim.key({ s: sim.s, kind: "press", key: "up" });
    sim.advanceTo(b[1] + 0.2);
    const up = rec.inputs.find((i) => i.kind === "press" && i.key === "up")!;
    expect(seen).toBeGreaterThan(10);
    expect(seen).toBeCloseTo(up.stride!, 6);
  });
});
