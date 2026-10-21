import { describe, expect, it } from "vitest";
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

describe("planner on the synthetic song", () => {
  const songMap = syntheticSongMap();
  const plan = runPlanner({ songMap });

  it("passes every output check", () => {
    const res = checkPlan(plan, plannerInput({ songMap }).content);
    const { ok, text } = summarizeChecks(res);
    expect(ok, text).toBe(true);
  });

  it("is deterministic", () => {
    const again = runPlanner({ songMap });
    expect(planHash(again)).toBe(planHash(plan));
    expect(again.map.hash).toBe(plan.map.hash);
  });

  it("ends with a finale that clears the horde", () => {
    const ring = plan.hits.filter((h) => h.source === "ring");
    expect(ring.length).toBeGreaterThan(0);
    const end = ring[ring.length - 1].w;
    expect(plan.enemies.every((e) => e.deathW <= end + 1e-6)).toBe(true);
    expect(plan.hud.xpFill[plan.hud.xpFill.length - 1]).toBeCloseTo(1, 5);
  });

  it("fills the XP bar exactly at each level-up", () => {
    for (const lu of plan.hud.levelUps) {
      let last = -1;
      for (let i = 0; i < plan.hud.xpTimes.length; i++) if (plan.hud.xpTimes[i] <= lu + 1e-6) last = i;
      if (last >= 0) expect(plan.hud.xpFill[last]).toBeCloseTo(1, 5);
    }
  });
});
