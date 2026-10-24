/**
 * The rubric's anchor (docs/design/reactive-feel.md, "The Dossier"): the authored Galop in play mode,
 * planned with play mode's own profiles (moderate, the mashers, the holder), as `?mode=play&player=`
 * plans them, so its sheets show the runs its metrics measure. The Galop's reactive stream plays the
 * same audio, so its lanes and judging stand in where a metric needs a stream; beat metrics are n/a
 * (the song map is play mode's grid). The runs keep the reactive names (moderate under
 * reactive-moderate, human under reactive-human).
 */
import type { SkillProfileName } from "../../src/content/schema.ts";
import type { PlannerInput } from "../../src/planner/context.ts";
import { planWithSim } from "../../src/planner/plan.ts";
import type { PropInfo } from "../../src/mapgen/mapgen.ts";
import { FEEL_SEED, trackMetrics, type FrameSeries, type TrackMetrics, type TrackRuns } from "./feel.ts";
import { plannerInput } from "./plan-node.ts";
import { reactiveInput, readStreamFile } from "./reactive-node.ts";

const AUTHORED = "levels/orpheus-galop.play.yaml";

export function anchorRuns(props: Record<string, PropInfo> | undefined): { runs: TrackRuns; metrics: (frames: FrameSeries) => TrackMetrics } {
  const run = reactiveInput(readStreamFile("orpheus-galop"), { seed: FEEL_SEED, checks: { noHeroDamage: true } });
  const ref = run.reference!;
  const off: PlannerInput = { ...plannerInput({ level: AUTHORED, seed: FEEL_SEED }), checks: { noHeroDamage: true } };
  const on = plannerInput({ level: AUTHORED, seed: FEEL_SEED });
  const plan = (input: PlannerInput, name: SkillProfileName) => planWithSim({ ...input, profile: name }, { props }).plan;
  const runs: TrackRuns = {
    id: "anchor-orpheus-galop",
    run,
    lib: off.content,
    ref,
    plans: { "reactive-moderate": plan(off, "moderate"), mash2: plan(off, "mash2"), mash4: plan(off, "mash4"), mash8: plan(off, "mash8"), holder: plan(off, "holder") },
    hurt: { "reactive-moderate": plan(on, "moderate"), "reactive-human": plan(on, "human") },
  };
  return {
    runs,
    metrics: (frames) => {
      const m = trackMetrics(runs, frames);
      return {
        ...m,
        beat: { strictF: null, anyLevelF: null, level: null, longestRun: null, lockInS: null },
        notes: ["the anchor is play mode: the song map is its grid, so beat metrics are n/a; judged time is the Galop's reactive stream's (an approximation)", ...m.notes],
      };
    },
  };
}
