/// <reference lib="webworker" />
/** Runs the planner off the main thread. */
import { plan } from "../planner/plan.ts";
import type { PlannerInput } from "../planner/context.ts";
import type { PropInfo } from "../mapgen/mapgen.ts";

self.onmessage = (e: MessageEvent<{ input: PlannerInput; props?: Record<string, PropInfo> }>) => {
  try {
    const p = plan(e.data.input, { props: e.data.props, now: () => performance.now() });
    (self as unknown as Worker).postMessage({ ok: true, plan: p });
  } catch (err) {
    (self as unknown as Worker).postMessage({ ok: false, error: err instanceof Error ? err.message : String(err) });
  }
};
