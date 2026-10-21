/**
 * Plan check: runs the simulated playthrough in Node with all of its checks
 * (including causality, which re-simulates with presses cut) and prints a
 * summary. It also runs the casual player and reports its kills and groove
 * next to the main run's. With --determinism it also plans twice and
 * compares hashes, and generates the map twice and compares.
 *
 *   npm run check:plan [-- --seed=1859] [-- --player=casual] [-- --determinism]
 */
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { checkCausality } from "../src/planner/sim-checks.ts";
import type { SkillProfile } from "../src/sim/player.ts";
import type { Plan } from "../src/plan/types.ts";
import { runPlanner, plannerInput, propsFromAtlas } from "./lib/plan-node.ts";

const args = process.argv.slice(2);
const seedArg = args.find((a) => a.startsWith("--seed="));
const seed = seedArg ? Number(seedArg.split("=")[1]) : undefined;
const input = plannerInput({ seed });
const profile = (args.find((a) => a.startsWith("--player="))?.split("=")[1] ?? input.level.player) as SkillProfile;

const kills = (p: Plan) => p.hud.kills.length;
const bonus = (p: Plan) => `+${(p.stats.averageGroove * input.content.hero.groove.maxBonus * 100).toFixed(0)}%`;

/** Plan one profile, run every check, and print the summary; returns whether it passed. */
function run(prof: SkillProfile): { plan: Plan; ok: boolean } {
  const t0 = performance.now();
  const plan = runPlanner({ seed, profile: prof });
  const ms = performance.now() - t0;
  const results = checkPlan(plan, input.content);
  results.push(checkCausality(input, { props: propsFromAtlas(), profile: prof }));
  const { ok, text } = summarizeChecks(results);
  const st = plan.stats;
  console.log(`plan ${planHash(plan)} (seed ${plan.seed}, ${plan.profile} player) in ${ms.toFixed(0)} ms (simulation ${st.simMs.toFixed(0)} ms)`);
  console.log(`  peak population      ${st.peakPopulation}`);
  console.log(`  enemies              ${st.enemies}`);
  console.log(`  kills per section    ${Object.entries(st.killsPerSection).map(([k, v]) => `${k} ${v}`).join(", ")}`);
  console.log(`  presses              ${st.presses} (perfect ${st.ratings.perfect}, great ${st.ratings.great}, good ${st.ratings.good}, unrated ${st.ratings.none})`);
  console.log(`  swings               ${st.swings}`);
  console.log(`  average groove       ${st.averageGroove.toFixed(2)} (${bonus(plan)} damage)`);
  console.log(`  total hit-stop time  ${(st.totalHitStop * 1000).toFixed(0)} ms`);
  console.log(`  threats              ${plan.threats.length} (${plan.nearMisses.length} near misses)`);
  console.log("checks:");
  console.log(text);
  return { plan, ok };
}

const main = run(profile);
let failed = !main.ok;
if (profile !== "casual") {
  const casual = run("casual");
  failed ||= !casual.ok;
  console.log(`reward spectrum: ${profile} ${kills(main.plan)} kills at ${bonus(main.plan)}; casual ${kills(casual.plan)} kills at ${bonus(casual.plan)}`);
}
if (args.includes("--determinism")) {
  const again = runPlanner({ seed, profile });
  const a = planHash(main.plan);
  const b = planHash(again);
  console.log(`determinism: plan ${a} vs ${b} — ${a === b ? "same" : "DIFFERENT"}`);
  console.log(`determinism: map ${main.plan.map.hash} vs ${again.map.hash} — ${main.plan.map.hash === again.map.hash ? "same" : "DIFFERENT"}`);
  if (a !== b || main.plan.map.hash !== again.map.hash) failed = true;
}
process.exit(failed ? 1 : 0);
