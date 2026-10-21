/**
 * Plan check: runs the simulated playthrough in Node with all of its checks
 * (including causality, which re-simulates with presses cut) and prints a
 * summary. With --determinism it also plans twice and compares hashes, and
 * generates the map twice and compares.
 *
 *   npm run check:plan [-- --seed=1859] [-- --player=casual] [-- --determinism]
 */
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { checkCausality } from "../src/planner/sim-checks.ts";
import type { SkillProfile } from "../src/sim/player.ts";
import { runPlanner, plannerInput, propsFromAtlas } from "./lib/plan-node.ts";

const args = process.argv.slice(2);
const seedArg = args.find((a) => a.startsWith("--seed="));
const seed = seedArg ? Number(seedArg.split("=")[1]) : undefined;
const profile = (args.find((a) => a.startsWith("--player="))?.split("=")[1] ?? "skilled") as SkillProfile;
const t0 = performance.now();
const plan = runPlanner({ seed, profile });
const ms = performance.now() - t0;
const input = plannerInput({ seed });
const results = checkPlan(plan, input.content);
results.push(checkCausality(input, { props: propsFromAtlas(), profile }));
const { ok, text } = summarizeChecks(results);
const st = plan.stats;
console.log(`plan ${planHash(plan)} (seed ${plan.seed}, ${plan.profile} player) in ${ms.toFixed(0)} ms (simulation ${st.simMs.toFixed(0)} ms)`);
console.log(`  peak population      ${st.peakPopulation}`);
console.log(`  enemies              ${st.enemies}`);
console.log(`  kills per section    ${Object.entries(st.killsPerSection).map(([k, v]) => `${k} ${v}`).join(", ")}`);
console.log(`  presses              ${st.presses} (perfect ${st.ratings.perfect}, great ${st.ratings.great}, good ${st.ratings.good}, unrated ${st.ratings.none})`);
console.log(`  swings               ${st.swings}`);
console.log(`  average groove       ${st.averageGroove.toFixed(2)} (+${(st.averageGroove * 20).toFixed(0)}% damage)`);
console.log(`  total hit-stop time  ${(st.totalHitStop * 1000).toFixed(0)} ms`);
console.log("checks:");
console.log(text);
let failed = !ok;
if (args.includes("--determinism")) {
  const again = runPlanner({ seed, profile });
  const a = planHash(plan);
  const b = planHash(again);
  console.log(`determinism: plan ${a} vs ${b} — ${a === b ? "same" : "DIFFERENT"}`);
  console.log(`determinism: map ${plan.map.hash} vs ${again.map.hash} — ${plan.map.hash === again.map.hash ? "same" : "DIFFERENT"}`);
  if (a !== b || plan.map.hash !== again.map.hash) failed = true;
}
process.exit(failed ? 1 : 0);
