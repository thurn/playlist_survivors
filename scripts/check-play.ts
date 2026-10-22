/**
 * Play-mode check: runs the virtual players through play mode (the open field
 * and play-mode rules) headless, with the checks the demo shares and play
 * mode's own. On every seed: the `moderate` player (the signature moments), the
 * `casual` player for the reward spectrum, and mashers at 4 and 8 presses a
 * second, which must earn a C and fewer kills than the moderate player. On the
 * extra seeds (fewer, to keep the gate quick): the skilled and eighths players,
 * the holder and the masher at 2 a second. The moderate, casual, skilled and
 * eighths players must survive (the hero's health never runs out). Every
 * player passes the movement checks (every movement has an input, no dead
 * presses, the speed cap and holding keeps full speed) and the impacts on the
 * grid (every gameplay sound on its subdivision's slot). The groove tells them
 * apart: after the galop vamp the moderate player is at tier 2 or higher for
 * 70% of pulsed time, the holder and mashers at tier 0 for 80% of it, and the
 * eighths player's average groove is within 0.05 of the skilled player's.
 * Prints kills, average groove, grade, time at tier 0 and at tier 2 or higher,
 * and lowest health side by side. Each seed runs in its own process, in parallel.
 *
 *   npm run check:play [-- --seeds=1858,7 --extra-seeds=1858]
 */
import { execFile } from "node:child_process";
import { fileURLToPath } from "node:url";
import { checkPlan, summarizeChecks, type CheckResult } from "../src/planner/checks.ts";
import { checkGroove, checkPlay, checkStrides, tierShare } from "../src/planner/play-checks.ts";
import { planHash } from "../src/planner/plan.ts";
import { gradeFor } from "../src/plan/grade.ts";
import type { Plan } from "../src/plan/types.ts";
import type { PlayerName } from "../src/sim/player.ts";
import { plannerInput, runPlanner } from "./lib/plan-node.ts";

const LEVEL = "levels/orpheus-galop.play.yaml";
const args = process.argv.slice(2);
const list = (name: string, fallback: string) => (args.find((a) => a.startsWith(`--${name}=`))?.split("=")[1] ?? fallback).split(",").filter(Boolean).map(Number);
const seeds = list("seeds", "1858,1859,7,2024");
const extraSeeds = list("extra-seeds", "1858,7");

/** Every seed's players, and the extra seeds' (the mashers' "does not pay" compares with the moderate player). */
const PLAYERS: PlayerName[] = ["moderate", "casual", "mash4", "mash8"];
const EXTRA_PLAYERS: PlayerName[] = ["skilled", "eighths", "holder", "mash2"];
/** The players the game is tuned for: the full checks, and they live to the end. */
const HONEST = new Set<PlayerName>(["moderate", "casual", "skilled", "eighths"]);
const isMasher = (p: string) => /^mash\d+$/.test(p);

type Row = { seed: number; player: string; kills: number; groove: number; grade: string; tier0: number; tier2: number; health: string };
/** Kills while the hero lived, and its lowest health (or when it fell). */
const killsAlive = (plan: Plan) => Array.from(plan.hud.kills).filter((s) => plan.stats.deathS === undefined || s <= plan.stats.deathS).length;
const healthNote = (plan: Plan) => (plan.stats.deathS === undefined ? `${Math.round(plan.stats.minHealth * 100)}%` : `fell ${plan.stats.deathS.toFixed(0)}s`);

/** One seed's players and checks: what it prints, its table rows, and whether every check passed. */
function runSeed(seed: number): { ok: boolean; text: string; rows: Row[] } {
  const lib = plannerInput({ level: LEVEL }).content;
  // (A seed with only the extra players still runs the moderate player: the mashers compare with it.)
  const players = [...(seeds.includes(seed) ? PLAYERS : ["moderate" as const]), ...(extraSeeds.includes(seed) ? EXTRA_PLAYERS : [])];
  const runs = new Map<string, Plan>();
  const out: string[] = [];
  const rows: Row[] = [];
  let failed = false;
  for (const player of players) {
    const t0 = performance.now();
    const plan: Plan = runPlanner({ level: LEVEL, seed, profile: player });
    const ms = performance.now() - t0;
    runs.set(player, plan);
    let results: CheckResult[];
    if (HONEST.has(player)) {
      // The players the game is tuned for live to the end.
      const survives = { name: "the hero survives", failures: plan.stats.deathS === undefined ? [] : [`the hero's health ran out at s=${plan.stats.deathS.toFixed(3)}`], checked: 1 };
      results = [...checkPlan(plan, lib), ...checkPlay(plan, lib, { signature: player === "moderate" }), survives];
    } else {
      results = [...checkPlan(plan, lib).filter((c) => c.name === "every hero movement has an input" || c.name === "impacts on the grid"), ...checkStrides(plan)];
    }
    results = results.map((c) => ({ ...c, name: `${player}: ${c.name}` }));
    // Mashing must not pay: a C, and fewer kills (while the hero lives) than playing on the beat.
    if (isMasher(player)) {
      const moderate = runs.get("moderate")!;
      const f: string[] = [];
      const grade = gradeFor(plan.stats.averageGroove);
      if (grade !== "C") f.push(`seed ${seed}: ${player} earned ${grade} (groove ${plan.stats.averageGroove.toFixed(3)})`);
      if (killsAlive(plan) >= moderate.hud.kills.length) f.push(`seed ${seed}: ${player} killed ${killsAlive(plan)}, the moderate player ${moderate.hud.kills.length}`);
      results.push({ name: `${player}: mashing does not pay`, failures: f, checked: 2 });
    }
    const { ok, text } = summarizeChecks(results);
    failed ||= !ok;
    const st = plan.stats;
    out.push(`play ${planHash(plan)} (seed ${seed}, ${player} player) in ${ms.toFixed(0)} ms: ${plan.hud.kills.length} kills, presses ${st.presses} (perfect ${st.ratings.perfect}, great ${st.ratings.great}, good ${st.ratings.good}, unrated ${st.ratings.none}), groove ${st.averageGroove.toFixed(2)}, threats ${plan.threats.length} (${plan.nearMisses.length} near misses)`);
    out.push(text);
    rows.push({ seed, player, kills: killsAlive(plan), groove: st.averageGroove, grade: gradeFor(st.averageGroove), tier0: tierShare(plan, (t) => t === 0), tier2: tierShare(plan, (t) => t >= 2), health: healthNote(plan) });
  }
  const { ok, text } = summarizeChecks(checkGroove(runs));
  failed ||= !ok;
  out.push(text);
  return { ok: !failed, text: out.join("\n"), rows };
}

const ONE = "--one-seed=";
const one = args.find((a) => a.startsWith(ONE));
if (one) {
  // A child process: one seed, its result as JSON on stdout.
  process.stdout.write(JSON.stringify(runSeed(Number(one.slice(ONE.length)))));
  process.exit(0);
}
const t00 = performance.now();
const all = [...new Set([...seeds, ...extraSeeds])];
const self = fileURLToPath(import.meta.url);
const results = await Promise.all(
  all.map(
    (seed) =>
      new Promise<{ ok: boolean; text: string; rows: Row[] }>((resolve, reject) =>
        execFile(process.execPath, [...process.execArgv, self, ...args, `${ONE}${seed}`], { maxBuffer: 64 << 20 }, (err, stdout, stderr) => {
          if (err) reject(new Error(`seed ${seed}: ${err.message}\n${stderr}`));
          else resolve(JSON.parse(stdout));
        }),
      ),
  ),
);
let failed = false;
for (const r of results) {
  console.log(r.text);
  failed ||= !r.ok;
}
console.log(`\n(${((performance.now() - t00) / 1000).toFixed(1)} s; tier columns: share of pulsed time after the galop vamp)`);
console.log("seed   player     kills  groove  grade  tier 0  tier 2+  health (lowest)");
const pct = (x: number) => `${(x * 100).toFixed(0)}%`.padStart(6);
for (const r of results.flatMap((x) => x.rows)) console.log(`${String(r.seed).padEnd(6)} ${r.player.padEnd(10)} ${String(r.kills).padStart(5)}  ${r.groove.toFixed(2).padStart(6)}  ${r.grade.padEnd(5)}  ${pct(r.tier0)}  ${pct(r.tier2).padStart(7)}  ${r.health}`);
process.exit(failed ? 1 : 0);
