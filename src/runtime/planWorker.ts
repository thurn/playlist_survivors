/// <reference lib="webworker" />
/** Runs the planner off the main thread, then the planner's own output checks. */
import { plan, prepare } from "../planner/plan.ts";
import { reactivePlayerFor } from "../reactive/players.ts";
import type { Reference } from "../reactive/stream.ts";
import { checkReactivePlan } from "../reactive/plan-checks.ts";
import { checkPlan } from "../planner/checks.ts";
import type { PlannerInput } from "../planner/context.ts";
import type { PropInfo } from "../mapgen/mapgen.ts";

self.onmessage = (e: MessageEvent<{ input: PlannerInput; props?: Record<string, PropInfo>; reference?: Reference }>) => {
  try {
    // Reactive mode's players press on the track's reference beats.
    const { input, reference } = e.data;
    const player = input.reactive && reference ? reactivePlayerFor(prepare(input).ctx, reference, input.profile ?? input.level.player) : undefined;
    const p = plan(input, { props: e.data.props, now: () => performance.now(), ...(player ? { player } : {}) });
    // Never start playback with an invalid plan.
    // (A reactive plan is checked without the three play-mode rules it breaks by design.)
    const failed = (input.reactive ? checkReactivePlan(p, input.content) : checkPlan(p, input.content)).filter((r) => r.failures.length > 0);
    if (failed.length > 0) {
      const lines = failed.flatMap((r) => [`${r.name}: ${r.failures.length} failures`, ...r.failures.slice(0, 3).map((f) => `  ${f}`)]);
      throw new Error(`the plan failed its checks:\n${lines.join("\n")}`);
    }
    (self as unknown as Worker).postMessage({ ok: true, plan: p });
  } catch (err) {
    (self as unknown as Worker).postMessage({ ok: false, error: err instanceof Error ? err.message : String(err) });
  }
};
