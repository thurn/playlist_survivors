/// <reference lib="webworker" />
/** Runs the planner off the main thread, then the planner's own output checks. */
import { plan } from "../planner/plan.ts";
import { checkPlan } from "../planner/checks.ts";
import type { PlannerInput } from "../planner/context.ts";
import type { PropInfo } from "../mapgen/mapgen.ts";

self.onmessage = (e: MessageEvent<{ input: PlannerInput; props?: Record<string, PropInfo> }>) => {
  try {
    const p = plan(e.data.input, { props: e.data.props, now: () => performance.now() });
    // Never start playback with an invalid plan.
    const failed = checkPlan(p, e.data.input.content).filter((r) => r.failures.length > 0);
    if (failed.length > 0) {
      const lines = failed.flatMap((r) => [`${r.name}: ${r.failures.length} failures`, ...r.failures.slice(0, 3).map((f) => `  ${f}`)]);
      throw new Error(`the plan failed its checks:\n${lines.join("\n")}`);
    }
    (self as unknown as Worker).postMessage({ ok: true, plan: p });
  } catch (err) {
    (self as unknown as Worker).postMessage({ ok: false, error: err instanceof Error ? err.message : String(err) });
  }
};
