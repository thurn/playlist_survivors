/**
 * Plan check: runs the planner in Node with all of its output checks and
 * prints a summary. With --determinism it also plans twice and compares
 * hashes, and generates the map twice and compares.
 *
 *   npm run check:plan [-- --seed=1859] [-- --determinism]
 */
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { runPlanner, plannerInput } from "./lib/plan-node.ts";

const args = process.argv.slice(2);
const seedArg = args.find((a) => a.startsWith("--seed="));
const seed = seedArg ? Number(seedArg.split("=")[1]) : undefined;
const t0 = performance.now();
const plan = runPlanner({ seed });
const ms = performance.now() - t0;
const lib = plannerInput({ seed }).content;
const results = checkPlan(plan, lib);
const { ok, text } = summarizeChecks(results);
const st = plan.stats;
console.log(`plan ${planHash(plan)} (seed ${plan.seed}) in ${ms.toFixed(0)} ms`);
console.log(`  peak population      ${st.peakPopulation}`);
console.log(`  enemies              ${st.enemies} (${st.dropped} dropped by the crowd simulation)`);
console.log(`  kills per section    ${Object.entries(st.killsPerSection).map(([k, v]) => `${k} ${v}`).join(", ")}`);
console.log(`  total hit-stop time  ${(st.totalHitStop * 1000).toFixed(0)} ms`);
console.log(`  longest stretch without a primary event  ${st.longestGapWithoutPrimary.toFixed(2)} s`);
console.log("checks:");
console.log(text);
let failed = !ok;
if (args.includes("--determinism")) {
  const again = runPlanner({ seed });
  const a = planHash(plan);
  const b = planHash(again);
  console.log(`determinism: plan ${a} vs ${b} — ${a === b ? "same" : "DIFFERENT"}`);
  console.log(`determinism: map ${plan.map.hash} vs ${again.map.hash} — ${plan.map.hash === again.map.hash ? "same" : "DIFFERENT"}`);
  if (a !== b || plan.map.hash !== again.map.hash) failed = true;
}
process.exit(failed ? 1 : 0);
