/**
 * Plan check: runs the simulated playthrough in Node with all of its checks
 * and prints a summary. The first seed also gets the causality check
 * (re-simulating with the presses after a cut removed must change nothing
 * before it); extra seeds get the recording checks only.
 *
 *   npm run check:plan                      the level's seed, plus the casual player
 *   tsx scripts/check-plan.ts --seeds=1859,7 --player=perfect --casual --determinism
 *
 * --determinism plans the first seed twice and compares the hashes (and the maps').
 */
import { checkPlan, summarizeChecks, type CheckResult } from "../src/planner/checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { checkCausality } from "../src/planner/sim-checks.ts";
import type { SkillProfile } from "../src/sim/player.ts";
import type { Plan } from "../src/plan/types.ts";
import { runPlanner, runPlannerWithSim, plannerInput, propsFromAtlas } from "./lib/plan-node.ts";

const args = process.argv.slice(2);
const arg = (k: string) => args.find((a) => a.startsWith(`--${k}=`))?.split("=")[1];
const base = plannerInput();
const seeds = (arg("seeds") ?? arg("seed") ?? String(base.level.seed)).split(",").map(Number);
const profile = (arg("player") ?? base.level.player) as SkillProfile;
const bonus = (p: Plan) => `+${(p.stats.averageGroove * base.content.hero.groove.maxBonus * 100).toFixed(0)}%`;

let failed = false;
/** Plan one seed and profile, run its checks, and print one summary. */
function run(seed: number, prof: SkillProfile, full: boolean): Plan {
  const t0 = performance.now();
  const input = plannerInput({ seed });
  const { plan, sim } = runPlannerWithSim({ seed, profile: prof });
  const results: CheckResult[] = checkPlan(plan, input.content);
  if (full) results.push(checkCausality(input, { props: propsFromAtlas(), profile: prof, full: sim }));
  const { ok, text } = summarizeChecks(results);
  failed ||= !ok;
  const st = plan.stats;
  console.log(
    `plan ${planHash(plan)} (seed ${seed}, ${prof}) in ${(performance.now() - t0).toFixed(0)} ms: ` +
      `${st.enemies} enemies, peak ${st.peakPopulation}, ${plan.hud.kills.length} kills, ${st.presses} presses ` +
      `(${st.ratings.perfect} perfect), groove ${bonus(plan)}, ${plan.nearMisses.length}/${plan.threats.length} near misses`,
  );
  console.log(ok ? `  all ${results.length} checks pass` : text);
  return plan;
}

const main = run(seeds[0], profile, true);
if (args.includes("--casual") && profile !== "casual") {
  const casual = run(seeds[0], "casual", false);
  console.log(`reward spectrum: ${profile} ${main.hud.kills.length} kills at ${bonus(main)}; casual ${casual.hud.kills.length} kills at ${bonus(casual)}`);
}
for (const seed of seeds.slice(1)) run(seed, profile, false);
if (args.includes("--determinism")) {
  const again = runPlanner({ seed: seeds[0], profile });
  const same = planHash(again) === planHash(main) && again.map.hash === main.map.hash;
  console.log(`determinism: plan ${planHash(main)} vs ${planHash(again)}, map ${main.map.hash} vs ${again.map.hash}: ${same ? "same" : "DIFFERENT"}`);
  failed ||= !same;
}
process.exit(failed ? 1 : 0);
