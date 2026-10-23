import { describe, expect, it } from "vitest";
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { checkCausality } from "../src/planner/sim-checks.ts";
import { Warp } from "../src/core/warp.ts";
import { heroSample } from "../src/plan/sample.ts";
import { plannerInput, propsFromAtlas, runPlanner, runPlannerWithSim } from "../scripts/lib/plan-node.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

// One skilled and one casual playthrough of the demo level on the synthetic song, shared by every
// test. (check:seeds covers the demo on more seeds of the real song.)
const LEVEL = "levels/orpheus-galop.yaml";
const songMap = syntheticSongMap();
const input = plannerInput({ songMap, level: LEVEL });
const { plan, sim } = runPlannerWithSim({ songMap, level: LEVEL });
const casual = runPlanner({ songMap, level: LEVEL, profile: "casual" });

describe("simulated playthrough on the synthetic song", () => {

  it("passes every check", () => {
    const { ok, text } = summarizeChecks(checkPlan(plan, input.content));
    expect(ok, text).toBe(true);
  });

  it("is causal: cutting the presses after T changes nothing before T", () => {
    const r = checkCausality(input, { props: propsFromAtlas(), full: sim });
    expect(r.failures, r.failures.join("\n")).toEqual([]);
  });

  it("ends with a finale that clears the horde", () => {
    const ring = plan.hits.filter((h) => h.source === "ring");
    expect(ring.length).toBeGreaterThan(0);
    const end = ring[ring.length - 1].w;
    expect(plan.enemies.every((e) => e.deathW <= end + 1e-6)).toBe(true);
  });

  it("walks in free movement, swinging light by itself every two beats", () => {
    const prelude = plan.sections.find((x) => x.label === "prelude")!;
    expect(prelude.movement).toBe("free");
    const warp = new Warp(plan.warp);
    let walking = 0;
    for (let w = 1; w < warp.toWorld(prelude.s1); w += 0.1) if (heroSample(plan, w).anim === "walk") walking++;
    expect(walking).toBeGreaterThan(20);
    const swings = plan.swings.filter((x) => x.s < prelude.s1);
    expect(swings.length).toBeGreaterThan(10);
    expect(swings.every((x) => x.kind === "light")).toBe(true);
    // Free-movement presses are not rated.
    const presses = plan.inputs.filter((i) => i.kind === "press" && i.s < prelude.s1);
    expect(presses.length).toBeGreaterThan(0);
    expect(presses.every((i) => !i.steps && i.rating === undefined)).toBe(true);
  });

  it("cleaves on accents, meets threats with near misses, and kills the boss on its chord", () => {
    // Each cleave a press on an accent launched: its heavy impact on a strong accent, and its shockwave band by band on sixteenth notes.
    const cleaves = plan.cleaves;
    expect(cleaves.length).toBeGreaterThan(3);
    for (const c of cleaves) expect(plan.swings.find((x) => Math.abs(x.s - c.s) < 1e-6 && x.kind === "heavy")?.accent).toBeGreaterThanOrEqual(0.7);
    const waves = plan.hits.filter((h) => h.source === "wave");
    expect(waves.length).toBeGreaterThan(cleaves.length * 4);
    expect(plan.threats.length).toBeGreaterThan(4);
    expect(plan.threats.filter((t) => t.outcome === "near-miss").length / plan.threats.length).toBeGreaterThanOrEqual(0.5);
    expect(plan.nearMisses.length).toBeGreaterThan(0);
    expect(plan.boss?.deathS).toBeDefined();
  });

  it("levels up from earned experience at (or after) the scheduled moments", () => {
    const scheduled = input.level.events.filter((e) => e.levelUp).length;
    expect(plan.upgrades.length).toBe(scheduled);
    expect(Array.from(plan.hud.xpFill).some((f) => f >= 1 - 1e-9)).toBe(true);
  });
});

describe("skill profiles", () => {
  const lib = input.content;
  const skilled = plan;

  it("lets the casual player through every check, with less reward", () => {
    const { ok, text } = summarizeChecks(checkPlan(casual, lib));
    expect(ok, text).toBe(true);
    expect(casual.profile).toBe("casual");
    expect(casual.stats.averageGroove).toBeLessThan(skilled.stats.averageGroove - 0.1);
    expect(casual.stats.presses).toBeLessThan(skilled.stats.presses);
  });

  it("strikes on the grid however early or late the casual player presses", () => {
    const grid = Array.from(casual.grid);
    const late = casual.inputs.filter((i) => i.kind === "press" && Math.abs(i.delta ?? 0) > 0.07);
    expect(late.length).toBeGreaterThan(10);
    for (const sw of casual.swings) expect(Math.min(...grid.map((g) => Math.abs(g - sw.s)))).toBeLessThan(0.001);
  });
});

