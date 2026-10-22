/**
 * The frozen demo: the scripted demo's plan hash for every seed and demo
 * profile, recorded before the engine was refactored for play mode. A change
 * that shifts event order, random draws or float rounding fails here. Fix the
 * change; never update the fixture.
 */
import { describe, expect, it } from "vitest";
import { planHash } from "../src/planner/plan.ts";
import { runPlanner } from "../scripts/lib/plan-node.ts";
import golden from "./fixtures/golden-plan-hashes.json" with { type: "json" };

const PROFILES = ["perfect", "skilled", "casual"] as const;

describe("golden demo hashes", () => {
  for (const [seed, hashes] of Object.entries(golden as Record<string, Record<(typeof PROFILES)[number], string>>)) {
    for (const profile of PROFILES) {
      it(`seed ${seed}, ${profile} player`, () => {
        expect(planHash(runPlanner({ seed: Number(seed), profile }))).toBe(hashes[profile]);
      }, 60_000);
    }
  }
});
