/**
 * Play-mode check: runs the `moderate` virtual player through play mode (the
 * open field and play-mode rules) headless on four seeds, with the checks the
 * demo shares and play mode's own, then the `casual` player for the reward
 * spectrum, then mashers (presses at 4 and 8 per second, ignoring the beat),
 * which must earn a C and fewer kills than the moderate player. Prints kills,
 * average groove and grade side by side.
 *
 *   npm run check:play [-- --seeds=1858,7]
 */
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { checkPlay } from "../src/planner/play-checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { gradeFor } from "../src/plan/grade.ts";
import type { Plan } from "../src/plan/types.ts";
import type { SkillProfile } from "../src/sim/player.ts";
import { plannerInput, runPlanner } from "./lib/plan-node.ts";

const LEVEL = "levels/orpheus-galop.play.yaml";
const args = process.argv.slice(2);
const seeds = (args.find((a) => a.startsWith("--seeds="))?.split("=")[1] ?? "1858,1859,7,2024").split(",").map(Number);
const lib = plannerInput({ level: LEVEL }).content;

const MASH_RATES = [4, 8];
const report: { seed: number; profile: string; kills: number; groove: number; grade: string }[] = [];
let failed = false;
for (const seed of seeds) {
  for (const profile of ["moderate", "casual"] as SkillProfile[]) {
    const t0 = performance.now();
    const plan: Plan = runPlanner({ level: LEVEL, seed, profile });
    const ms = performance.now() - t0;
    const results = [...checkPlan(plan, lib), ...checkPlay(plan, lib, { signature: profile === "moderate" })];
    const { ok, text } = summarizeChecks(results);
    failed ||= !ok;
    const st = plan.stats;
    console.log(`play ${planHash(plan)} (seed ${seed}, ${profile} player) in ${ms.toFixed(0)} ms: ${plan.hud.kills.length} kills, presses ${st.presses} (perfect ${st.ratings.perfect}, great ${st.ratings.great}, good ${st.ratings.good}, unrated ${st.ratings.none}), groove ${st.averageGroove.toFixed(2)}, threats ${plan.threats.length} (${plan.nearMisses.length} near misses)`);
    console.log(text);
    report.push({ seed, profile, kills: plan.hud.kills.length, groove: st.averageGroove, grade: gradeFor(st.averageGroove) });
  }
  // Mashing must not pay: a C, and fewer kills than playing on the beat.
  const moderate = report.find((r) => r.seed === seed && r.profile === "moderate")!;
  for (const rate of MASH_RATES) {
    const plan: Plan = runPlanner({ level: LEVEL, seed, mashRate: rate });
    const st = plan.stats;
    const r = { seed, profile: `mash ${rate}/s`, kills: plan.hud.kills.length, groove: st.averageGroove, grade: gradeFor(st.averageGroove) };
    report.push(r);
    const f: string[] = [];
    if (r.grade !== "C") f.push(`seed ${seed}: mashing ${rate}/s earned ${r.grade} (groove ${r.groove.toFixed(3)})`);
    if (r.kills >= moderate.kills) f.push(`seed ${seed}: mashing ${rate}/s killed ${r.kills}, the moderate player ${moderate.kills}`);
    const { ok, text } = summarizeChecks([{ name: `mashing ${rate}/s does not pay`, failures: f, checked: 2 }]);
    failed ||= !ok;
    console.log(text);
  }
}
console.log("\nseed   player     kills  groove  grade");
for (const r of report) console.log(`${String(r.seed).padEnd(6)} ${r.profile.padEnd(10)} ${String(r.kills).padStart(5)}  ${r.groove.toFixed(2).padStart(6)}  ${r.grade}`);
process.exit(failed ? 1 : 0);
