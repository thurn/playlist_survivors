import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import type { PlanInput } from "../src/plan/types.ts";
import { gradeFor } from "../src/plan/grade.ts";
import { endCardGrade, endCardLines } from "../src/runtime/hud-lines.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const songMap = syntheticSongMap();
const PLAY = "levels/orpheus-galop.play.yaml";
const lib = plannerInput({ songMap, level: PLAY }).content;

describe("accent telegraphs", () => {
  const plan = runPlanner({ songMap, level: PLAY });
  const ev = new Evaluator(plan, lib);
  const accent = plan.cleaveAccents[0];
  const i = Array.from(plan.grid).findIndex((g) => Math.abs(g - accent.s) < 1e-6);
  const before = plan.grid[i - 1];

  it("shows the accents that will cleave in advance, with a gold ring on the accent's own tick", () => {
    const hud = ev.evaluate(accent.s - 1.5).hud;
    const at = (t: number) => hud.ticks.find((k) => Math.abs(accent.s - 1.5 + k.dt - t) < 0.01);
    expect(at(accent.s)?.cleave).toBe(true);
    // (A press on the chord launches its shockwave: the ring is on the accent, not the beat before.)
    expect(at(accent.s)?.pressHere).toBe(true);
    expect(at(before)?.pressHere).toBeFalsy();
    expect(hud.ticks.filter((k) => k.cleave).length).toBeGreaterThanOrEqual(1);
    expect(hud.ticks.filter((k) => !k.cleave && !k.pressHere).length).toBeGreaterThan(2);
  });

  it("makes the heart glow over the beat before the accent", () => {
    expect(ev.evaluate(before - 0.3).hud.heartGlow ?? 0).toBe(0);
    expect(ev.evaluate((before + accent.s) / 2).hud.heartGlow).toBeGreaterThan(0.3);
    expect(ev.evaluate(accent.s - 0.01).hud.heartGlow).toBeGreaterThan(0.9);
  });
});

describe("the key display", () => {
  it("greys a double tap, and counts only the counted press's rating", () => {
    const plan = runPlanner({ songMap, level: PLAY });
    // A press, then a mashed one for the same grid time.
    const s = plan.grid[200];
    plan.inputs = [
      { s, w: s, kind: "press", key: "up", steps: true, rating: "perfect", class: "counted" },
      { s: s + 0.01, w: s, kind: "release", key: "up", steps: true },
      { s: s + 0.02, w: s, kind: "press", key: "up", steps: true, extra: true, class: "double" },
    ];
    const keys = new Evaluator(plan, lib).evaluate(s + 0.03).hud;
    expect(keys.keys.up.rating).toBe("extra");
    expect(keys.ratings.perfect).toBe(1);
  });
});

describe("press classes on the HUD", () => {
  const plan = runPlanner({ songMap, level: PLAY });

  it("shows the tier beside the groove meter, its marks, and a flash on each tier-up", () => {
    const ev = new Evaluator(plan, lib);
    const t = plan.tier;
    // (A tier-up with no other change in the second after it.)
    const up = Array.from(t.v).findIndex((v, k) => k > 0 && v > t.v[k - 1] && !(t.s[k + 1] < t.s[k] + 1.1));
    expect(up).toBeGreaterThan(0);
    const at = t.s[up];
    const hud = ev.evaluate(at + 0.01).hud;
    expect(hud.tier).toMatchObject({ n: t.v[up], marks: lib.hero.meter.tiers });
    expect(hud.tier.flash).toBeGreaterThan(0.9);
    expect(ev.evaluate(at + 1).hud.tier.flash).toBe(0);
    expect(ev.evaluate(1).hud.tier).toMatchObject({ n: 0, flash: 0 });
  });

  it("styles each press class on the key display, and shows stomps as both feet", () => {
    const s = plan.grid[200];
    const p = (dt: number, key: PlanInput["key"], extra: Partial<PlanInput>): PlanInput[] => [
      { s: s + dt, w: s + dt, kind: "press", key, steps: true, ...extra },
    ];
    const styleOf = (inputs: PlanInput[], key: "up" | "down" | "left" | "right" = "up") => {
      const hud = new Evaluator({ ...plan, inputs }, lib).evaluate(s + 0.5).hud;
      return { key: hud.keys[key], hud };
    };
    expect(styleOf(p(0, "up", { class: "counted", rating: "perfect" })).key).toMatchObject({ lit: 1, rating: "perfect" });
    expect(styleOf(p(0, "up", { class: "counted", rating: "good" })).key.rating).toBe("good");
    expect(styleOf(p(0, "up", { class: "counted" })).key.rating).toBe("loose");
    expect(styleOf(p(0, "up", { class: "offbeat", offbeat: "perfect", extra: true })).key).toMatchObject({ rating: "perfect", outline: true });
    expect(styleOf(p(0, "up", { class: "double", extra: true })).key.rating).toBe("extra");
    expect(styleOf(p(0, "up", { class: "stray", extra: true })).key.rating).toBe("stray");
    // A diagonal's partner shows the style of the press it bent; neither extra press counts.
    const bend = styleOf([...p(0, "up", { class: "counted", rating: "great" }), ...p(0.01, "right", { class: "partner", bend: true })], "right");
    expect(bend.key.rating).toBe("great");
    expect(bend.hud.ratings).toEqual({ perfect: 0, great: 1, good: 0 });
    // A stomp with Space lights both feet in its rating's colour, and no arrow.
    const stomp = styleOf(p(0, "space", { class: "counted", rating: "perfect", stomp: true }));
    expect(stomp.hud.stomp).toMatchObject({ lit: 1, rating: "perfect" });
    expect(stomp.key.lit).toBe(0);
    expect(stomp.hud.ratings.perfect).toBe(1);
    // Two opposite keys together are one stomp: both feet light in the first key's style, and go out with the second key.
    const rock = [...p(0, "up", { class: "counted", rating: "great" }), ...p(0.02, "down", { class: "partner", stomp: true, rock: true })];
    expect(styleOf(rock).hud.stomp).toMatchObject({ lit: 1, rating: "great" });
    const released: PlanInput[] = [...rock, { s: s + 0.05, w: s + 0.05, kind: "release", key: "down", steps: true }];
    expect(styleOf(released).hud.stomp!.lit).toBe(0);
    // Space held across a two-key stomp: letting go of Space leaves the feet to the second key.
    const overlap: PlanInput[] = [...p(-0.2, "space", { class: "counted", rating: "perfect", stomp: true }), ...rock, { s: s + 0.05, w: s + 0.05, kind: "release", key: "space", steps: true }];
    expect(styleOf(overlap).hud.stomp).toMatchObject({ lit: 1, rating: "great" });
  });

  it("ends with kills, the rating counts, loose and stray presses, the average groove, the time at each tier and the gems collected", () => {
    const ev = new Evaluator(plan, lib);
    const endS = plan.events.ui.find((u) => u.kind === "end")!.s;
    const hud = ev.evaluate(endS + 0.5).hud;
    const end = hud.end!;
    const own = plan.inputs.filter((i) => i.kind === "press" && i.steps);
    expect(end.strays).toBe(own.filter((i) => i.class === "stray").length);
    expect(end.loose).toBe(own.filter((i) => i.class === "counted" && !i.rating).length);
    const pulsed = plan.sections.filter((x) => x.movement === "steps").reduce((a, x) => a + Math.min(x.s1, endS) - x.s0, 0);
    expect(end.tierTime.reduce((a, b) => a + b, 0)).toBeLessThanOrEqual(pulsed + 1e-6);
    expect(end.tierTime[3]).toBeGreaterThan(10);
    const lines = endCardLines(hud, end).map((l) => l.text);
    expect(lines).toEqual([
      `KILLS ${hud.kills}`,
      `PERFECT ${hud.ratings.perfect}`,
      `GREAT ${hud.ratings.great}`,
      `GOOD ${hud.ratings.good}`,
      `LOOSE ${end.loose}   STRAY ${end.strays}`,
      `AVERAGE GROOVE ${Math.round(end.averageGroove * 100)}%`,
      `TIME AT TIER  ${end.tierTime.map((t, i) => `${i}: ${Math.round(t)}S`).join("  ")}`,
      `GEMS ${end.gems.collected} OF ${end.gems.dropped}`,
    ]);
    // Gems on the field: the experience collected, of all that dropped.
    expect(end.gems.dropped).toBe(plan.events.gems.reduce((a, g) => a + g.value, 0));
    expect(end.gems.collected).toBeLessThan(end.gems.dropped);
    expect(end.gems.collected).toBeGreaterThan(0);
    // A recorded run (the demo, or ?player=) shows the grade of its average groove; play mode its own.
    expect(endCardGrade(end, undefined).grade).toBe(gradeFor(end.averageGroove));
    expect(endCardGrade(end, { grade: "S", newBest: true }).grade).toBe("S");
  });
});
