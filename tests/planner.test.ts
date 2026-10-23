import { describe, expect, it } from "vitest";
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { checkCausality } from "../src/planner/sim-checks.ts";
import { Warp } from "../src/core/warp.ts";
import { heroSample } from "../src/plan/sample.ts";
import { plannerInput, propsFromAtlas, runPlanner, runPlannerWithSim } from "../scripts/lib/plan-node.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

// One skilled and one casual playthrough of the synthetic song, shared by every test, under the
// old step rules in the forest corridor (the legacy demo level, until hv-zoon removes that code).
// (check:seeds covers the demo, under today's rules, on more seeds of the real song.)
const LEVEL = "tests/fixtures/legacy-demo.level.yaml";
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

  it("stands still between steps", () => {
    const warp = new Warp(plan.warp);
    const sec = plan.sections.find((s) => s.label === "theme-a")!;
    let checked = 0;
    for (let i = 0; i + 1 < plan.songBeats.length && checked < 20; i++) {
      const b0 = plan.songBeats[i];
      const b1 = plan.songBeats[i + 1];
      if (b0 < sec.s0 + 2 || b1 > sec.s1) continue;
      // (A cleave's hit-stop freezes the step for a moment: skip those beats.)
      if (warp.hitStops().some(([h0]) => h0 > b0 - 0.05 && h0 < b0 + 0.3)) continue;
      // Inside one beat gap, after the step has landed and before the next press.
      const a = heroSample(plan, warp.toWorld(b0 + 0.2));
      const b = heroSample(plan, warp.toWorld(b0 + 0.27));
      expect(Math.hypot(a.x - b.x, a.y - b.y), `beat at ${b0.toFixed(2)}`).toBeLessThan(1e-6);
      checked++;
    }
    expect(checked).toBe(20);
  });

  it("strikes one beat after the press that armed it, with the groove bonus", () => {
    const armed = plan.swings.filter((s) => s.press !== undefined);
    expect(armed.length).toBeGreaterThan(100);
    for (const sw of armed) {
      const p = plan.inputs[sw.press!];
      expect(sw.s - p.s).toBeGreaterThanOrEqual(0.09 - 1e-9);
      expect(sw.s - p.s).toBeLessThan(0.75);
    }
    const late = plan.hits.filter((h) => h.source === "move" && h.s > 60);
    expect(Math.min(...late.map((h) => h.damage))).toBeGreaterThan(1.1);
  });

  it("walks in free movement, swinging by itself every two beats and cleaving on strong anchors", () => {
    const prelude = plan.sections.find((x) => x.label === "prelude")!;
    expect(prelude.movement).toBe("free");
    const warp = new Warp(plan.warp);
    let walking = 0;
    for (let w = 1; w < warp.toWorld(prelude.s1); w += 0.1) if (heroSample(plan, w).anim === "walk") walking++;
    expect(walking).toBeGreaterThan(20);
    const swings = plan.swings.filter((x) => x.s < prelude.s1);
    expect(swings.length).toBeGreaterThan(10);
    expect(swings.every((x) => x.press === undefined)).toBe(true);
    expect(swings.some((x) => x.kind === "heavy")).toBe(true);
    // Free-movement presses are not rated.
    const presses = plan.inputs.filter((i) => i.kind === "press" && i.s < prelude.s1);
    expect(presses.length).toBeGreaterThan(0);
    expect(presses.every((i) => !i.steps && i.rating === undefined)).toBe(true);
  });

  it("cleaves on accents, meets every threat with a near miss, and kills the boss on its chord", () => {
    const heavy = plan.swings.filter((x) => x.kind === "heavy" && x.press !== undefined);
    expect(heavy.length).toBeGreaterThan(3);
    for (const h of heavy) expect(h.accent).toBeGreaterThanOrEqual(0.7);
    // Each cleave's shockwave lands band by band on sixteenth notes.
    const waves = plan.hits.filter((h) => h.source === "wave");
    expect(waves.length).toBeGreaterThan(heavy.length * 4);
    expect(plan.threats.length).toBeGreaterThan(8);
    expect(plan.threats.filter((t) => t.outcome === "near-miss").length / plan.threats.length).toBeGreaterThanOrEqual(0.9);
    expect(plan.nearMisses.length).toBeGreaterThan(0);
    expect(plan.boss?.deathS).toBeDefined();
  });

  it("levels up from earned experience at (or after) the scheduled moments", () => {
    const scheduled = input.level.events.filter((e) => e.levelUp).length;
    expect(plan.weapons.length).toBe(scheduled);
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

