/**
 * Play mode's plan checks as they apply to a reactive plan. Three of play mode's rules a reactive
 * plan breaks by design: impacts are muted while the beat is unclear, the director sets the
 * population, and there is no finale blast yet. check:reactive is reactive mode's own gate.
 */
import type { ContentLibrary } from "../content/loader.ts";
import type { Plan } from "../plan/types.ts";
import { checkPlan, type CheckResult } from "../planner/checks.ts";

export const REACTIVE_PLAN_EXEMPT = new Set(["impacts on the grid", "population", "clean ending"]);

/** checkPlan, without the rules a reactive plan breaks by design. */
export function checkReactivePlan(plan: Plan, lib: ContentLibrary): CheckResult[] {
  return checkPlan(plan, lib).filter((r) => !REACTIVE_PLAN_EXEMPT.has(r.name));
}
