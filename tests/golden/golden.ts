/**
 * The frozen demo: the scripted demo's plan hash for every seed and demo
 * profile, recorded before the engine was refactored for play mode. A change
 * that shifts event order, random draws or float rounding fails here. Fix the
 * change; never update the fixture. (One test file per seed, so they run in
 * parallel.)
 */
import { describe, expect, it } from "vitest";
import { planHash } from "../../src/planner/plan.ts";
import { runPlanner } from "../../scripts/lib/plan-node.ts";
import golden from "../fixtures/golden-plan-hashes.json" with { type: "json" };

const PROFILES = ["perfect", "skilled", "casual"] as const;

export function goldenSeed(seed: number): void {
  const hashes = (golden as Record<string, Record<(typeof PROFILES)[number], string>>)[seed];
  describe(`golden demo hashes, seed ${seed}`, () => {
    for (const profile of PROFILES) {
      it(`${profile} player`, () => {
        expect(planHash(runPlanner({ seed, profile }))).toBe(hashes[profile]);
      }, 60_000);
    }
  });
}
