import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const songMap = syntheticSongMap();
const PLAY = "levels/orpheus-galop.play.yaml";
const lib = plannerInput({ songMap, level: PLAY }).content;

describe("accent telegraphs", () => {
  const plan = runPlanner({ songMap, level: PLAY });
  const ev = new Evaluator(plan, lib);
  const accent = plan.cleaveAccents![0];
  const i = Array.from(plan.grid).findIndex((g) => Math.abs(g - accent.s) < 1e-6);
  const before = plan.grid[i - 1];

  it("shows the accents that will cleave, and the beat to press for each, in advance", () => {
    const hud = ev.evaluate(accent.s - 1.5).hud;
    const at = (t: number) => hud.ticks.find((k) => Math.abs(accent.s - 1.5 + k.dt - t) < 0.01);
    expect(at(accent.s)?.cleave).toBe(true);
    expect(at(before)?.pressHere).toBe(true);
    expect(hud.ticks.filter((k) => k.cleave).length).toBeGreaterThanOrEqual(1);
    expect(hud.ticks.filter((k) => !k.cleave && !k.pressHere).length).toBeGreaterThan(2);
  });

  it("makes the heart glow over the beat before the accent", () => {
    expect(ev.evaluate(before - 0.3).hud.heartGlow ?? 0).toBe(0);
    expect(ev.evaluate((before + accent.s) / 2).hud.heartGlow).toBeGreaterThan(0.3);
    expect(ev.evaluate(accent.s - 0.01).hud.heartGlow).toBeGreaterThan(0.9);
  });

  it("keeps the demo's HUD unchanged", () => {
    const demo = runPlanner({ songMap });
    const hud = new Evaluator(demo, lib).evaluate(accent.s - 1.5).hud;
    expect(hud.heartGlow).toBeUndefined();
    expect(hud.ticks.every((k) => !k.cleave && !k.pressHere)).toBe(true);
  });
});

describe("the key display", () => {
  it("greys an ignored press", () => {
    const plan = runPlanner({ songMap, level: PLAY });
    // A press, then a mashed one for the same grid time.
    const s = plan.grid[200];
    plan.inputs = [
      { s, w: s, kind: "press", key: "up", steps: true, rating: "perfect" },
      { s: s + 0.01, w: s, kind: "release", key: "up", steps: true },
      { s: s + 0.02, w: s, kind: "press", key: "up", steps: true, ignored: true },
    ];
    const keys = new Evaluator(plan, lib).evaluate(s + 0.03).hud;
    expect(keys.keys.up.rating).toBe("ignored");
    expect(keys.ratings.perfect).toBe(1);
  });
});
