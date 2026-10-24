/**
 * Play-mode check: runs the virtual players through play mode (the open field
 * and play-mode rules) headless, with the checks the demo shares and play
 * mode's own. Three sets of seeds: on every seed, the `moderate` player (the
 * signature moments) and the `casual` player for the reward spectrum; on the
 * extra seeds (fewer, to keep the gate quick), the perfect, skilled and eighths
 * players; on the average seeds (more, since what they're judged on is an
 * average), the human and late players (timed like real play logs), the holder
 * and the mashers at 2, 4 and 8 presses a second.
 *
 * Hard gates, on every run: the honest players (moderate, casual, perfect,
 * skilled and eighths) survive (the hero's health never runs out) and pass every
 * check; every player passes the movement checks (every movement has an input,
 * no dead presses, the speed cap and holding keeps full speed) and the impacts
 * on the grid (every gameplay sound on its subdivision's slot); the holder and
 * mashers earn a C (mashing does not pay); after the galop vamp the moderate
 * player is at tier 2 or higher for 70% of pulsed time, and the eighths
 * player's average groove is within 0.05 of the skilled player's.
 *
 * Averaged gates, over the average seeds: the sim is chaotic, so a small,
 * unrelated change reshuffles any one seed's run, and the tuning numbers are
 * judged on averages. The curve: when the holder and mashers fall, as time
 * alive (a run they survive counts as the song's end), each one's average
 * within 1:35-2:20 and the four together within the design's 1:45-2:05. The
 * groove: the human player at tier 2 or higher for 50% of pulsed time on
 * average, the late player for 40%, the holder and mashers at tier 0 for 80%.
 * (Measured over 64 seeds: a single fall time spreads from about 1:25 to past
 * the song's end, and the late player's tier-2 share from 20% to 85%; each
 * band sits about three standard deviations of an 8-seed average or more
 * from master's average: see checkCurve.)
 *
 * Prints kills, average groove, grade, time at tier 0 and at tier 2 or higher,
 * and lowest health side by side, the averages against their bands, and the
 * honest players' end health against its band (reported, not gated). Each
 * seed's honest players, and its average seeds' players, run in a process of
 * their own, in parallel.
 *
 *   npm run check:play [-- --seeds=1858,7 --extra-seeds=1858 --average-seeds=1858,7,1,2]
 */
import { execFile } from "node:child_process";
import { fileURLToPath } from "node:url";
import { checkPlan, summarizeChecks, type CheckResult } from "../src/planner/checks.ts";
import { averagedGrooveShare, checkCurve, checkGroove, checkGrooveAverages, checkPlay, checkStrides, mmss, tierShare } from "../src/planner/play-checks.ts";
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
const averageSeeds = list("average-seeds", "1858,1859,7,2024,1,2,3,4");

/** Every seed's players, the extra seeds' and the average seeds'. */
const PLAYERS: PlayerName[] = ["moderate", "casual"];
const EXTRA_PLAYERS: PlayerName[] = ["perfect", "skilled", "eighths"];
const AVERAGE_PLAYERS: PlayerName[] = ["human", "late", "holder", "mash2", "mash4", "mash8"];
/** The players the game is tuned for: the full checks, and they live to the end. */
const HONEST = new Set<PlayerName>(["moderate", "casual", "perfect", "skilled", "eighths"]);

/**
 * The curve ("The Difficulty Curve" in docs/design/gameplay-redesign.md): the honest players' end
 * health (share of the maximum). (When the holder and mashers fall is checkCurve's.)
 *
 * The honest players' end health is judged on its average over the seeds, and reported against
 * its band, not gated (hv-dtz9's decision; see "The Difficulty Curve" in the design): at tier 2
 * and up the horde hardly reaches the hero, so nearly all of their damage is the minotaur's in the
 * climax, and the heals that follow (its two chickens, the bridge's level-up) give it back. No
 * setting of the heals, the boss or the stream tried brought the averages into the bands; that
 * needs a change to the design's healing or damage.
 */
const END: Partial<Record<PlayerName, [number, number]>> = {
  perfect: [0.9, 1],
  skilled: [0.8, 1],
  eighths: [0.8, 1],
  moderate: [0.4, 0.7],
  casual: [0.1, 0.4],
};
/** The hero's health at the end (share of its maximum), or undefined if it fell. */
const endHealth = (plan: Plan) => (plan.stats.deathS === undefined ? (plan.health.v.length ? plan.health.v[plan.health.v.length - 1] : 1) : undefined);
const isMasher = (p: string) => /^mash\d+$/.test(p);

/**
 * A run's table row. alive: when the hero fell, or the song's end if it survived (song seconds);
 * end: its end health (0 if it fell); share: its share of pulsed time passing its averaged groove rule.
 */
type Row = { seed: number; player: string; kills: number; groove: number; grade: string; tier0: number; tier2: number; health: string; alive: number; fell: boolean; end: number; share?: number };
type SeedResult = { ok: boolean; text: string; rows: Row[] };
/** Kills while the hero lived, and its lowest health (or when it fell). */
const killsAlive = (plan: Plan) => Array.from(plan.hud.kills).filter((s) => plan.stats.deathS === undefined || s <= plan.stats.deathS).length;
const healthNote = (plan: Plan) => (plan.stats.deathS === undefined ? `${Math.round(plan.stats.minHealth * 100)}%` : `fell ${plan.stats.deathS.toFixed(0)}s`);
/** The table's note on the end: the honest players' end health against its band, or when the hero fell. */
function endNote(player: PlayerName, plan: Plan): string {
  const end = endHealth(plan);
  const got = end !== undefined ? `end ${Math.round(end * 100)}%` : `fell ${mmss(plan.stats.deathS!)}`;
  const band = END[player];
  if (!band) return got;
  const ok = end !== undefined && end >= band[0] - 1e-9 && end <= band[1] + 1e-9;
  return `${got} (${Math.round(band[0] * 100)}-${Math.round(band[1] * 100)}%${ok ? "" : ", off target"})`;
}

/** A seed's honest players (every seed's and the extra seeds'), or its average seeds' players. */
type Group = "honest" | "average";
const playersOf = (seed: number, group: Group): PlayerName[] =>
  group === "average" ? (averageSeeds.includes(seed) ? AVERAGE_PLAYERS : []) : [...(seeds.includes(seed) ? PLAYERS : []), ...(extraSeeds.includes(seed) ? EXTRA_PLAYERS : [])];

/** One seed's group of players and their checks: what it prints, its table rows, and whether every check passed. */
function runSeed(seed: number, group: Group): SeedResult {
  const lib = plannerInput({ level: LEVEL }).content;
  const players = playersOf(seed, group);
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
    // Mashing (or only holding keys) earns a C. (Its fewer kills than playing on the beat are the
    // curve's now: it falls in the climax.)
    if (isMasher(player) || player === "holder") {
      const grade = gradeFor(plan.stats.averageGroove);
      results.push({ name: `${player}: mashing does not pay`, failures: grade === "C" ? [] : [`seed ${seed}: ${player} earned ${grade} (groove ${plan.stats.averageGroove.toFixed(3)})`], checked: 1 });
    }
    const { ok, text } = summarizeChecks(results);
    failed ||= !ok;
    const st = plan.stats;
    out.push(`play ${planHash(plan)} (seed ${seed}, ${player} player) in ${ms.toFixed(0)} ms: ${plan.hud.kills.length} kills, presses ${st.presses} (perfect ${st.ratings.perfect}, great ${st.ratings.great}, good ${st.ratings.good}, unrated ${st.ratings.none}), groove ${st.averageGroove.toFixed(2)}, threats ${plan.threats.length} (${plan.nearMisses.length} near misses)`);
    out.push(text);
    const songEnd = plan.sections[plan.sections.length - 1].s1;
    rows.push({
      seed,
      player,
      kills: killsAlive(plan),
      groove: st.averageGroove,
      grade: gradeFor(st.averageGroove),
      tier0: tierShare(plan, (t) => t === 0),
      tier2: tierShare(plan, (t) => t >= 2),
      health: `${healthNote(plan).padEnd(9)} ${endNote(player, plan)}`,
      alive: st.deathS ?? songEnd,
      fell: st.deathS !== undefined,
      end: endHealth(plan) ?? 0,
      share: averagedGrooveShare(player, plan),
    });
  }
  const { ok, text } = summarizeChecks(checkGroove(runs));
  failed ||= !ok;
  out.push(text);
  return { ok: !failed, text: out.join("\n"), rows };
}


const ONE = "--one-seed=";
const one = args.find((a) => a.startsWith(ONE));
if (one) {
  // A child process: one seed's group, its result as JSON on stdout.
  const [seed, group] = one.slice(ONE.length).split(":");
  process.stdout.write(JSON.stringify(runSeed(Number(seed), group as Group)));
  process.exit(0);
}
const t00 = performance.now();
const jobs = [...new Set([...seeds, ...extraSeeds, ...averageSeeds])].flatMap((seed) => (["honest", "average"] as const).filter((g) => playersOf(seed, g).length > 0).map((group) => `${seed}:${group}`));
const self = fileURLToPath(import.meta.url);
const results = await Promise.all(
  jobs.map(
    (job) =>
      new Promise<SeedResult>((resolve, reject) =>
        execFile(process.execPath, [...process.execArgv, self, ...args, `${ONE}${job}`], { maxBuffer: 64 << 20 }, (err, stdout, stderr) => {
          if (err) reject(new Error(`seed ${job}: ${err.message}\n${stderr}`));
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
const rows = results.flatMap((x) => x.rows);

// The averaged gates: the curve, and the groove of the players judged on averages.
const shares = new Map<string, number[]>();
for (const r of rows) if (r.share !== undefined) shares.set(r.player, [...(shares.get(r.player) ?? []), r.share]);
const averaged = summarizeChecks([...checkCurve(rows), ...checkGrooveAverages(shares)]);
failed ||= !averaged.ok;
console.log(`\naveraged over the seeds each player runs on\n${averaged.text}`);

console.log(`\n(${((performance.now() - t00) / 1000).toFixed(1)} s; tier columns: share of pulsed time after the galop vamp)`);
console.log("seed   player     kills  groove  grade  tier 0  tier 2+  lowest    end (target)");
const mean = (xs: readonly number[]) => xs.reduce((a, x) => a + x, 0) / xs.length;
const within = (x: number, [lo, hi]: [number, number]) => x >= lo - 1e-9 && x <= hi + 1e-9;
const pct = (x: number) => `${(x * 100).toFixed(0)}%`.padStart(6);
for (const r of rows) console.log(`${String(r.seed).padEnd(6)} ${r.player.padEnd(10)} ${String(r.kills).padStart(5)}  ${r.groove.toFixed(2).padStart(6)}  ${r.grade.padEnd(5)}  ${pct(r.tier0)}  ${pct(r.tier2).padStart(7)}  ${r.health}`);
console.log("\naverages over the seeds");
for (const player of [...PLAYERS, ...EXTRA_PLAYERS, ...AVERAGE_PLAYERS]) {
  const mine = rows.filter((r) => r.player === player);
  if (mine.length === 0) continue;
  const fell = mine.filter((r) => r.fell).length;
  const band = END[player];
  // The honest players' end health is judged on its average (a fall counts as 0), and reported
  // against its band rather than gated: see END.
  const end = band ? `end health ${pct(mean(mine.map((r) => r.end)))} (${Math.round(band[0] * 100)}-${Math.round(band[1] * 100)}%${within(mean(mine.map((r) => r.end)), band) ? "" : ", off target"}; reported, not checked)` : "";
  console.log(`${player.padEnd(10)} ${String(mine.length).padStart(2)} seeds  tier 0 ${pct(mean(mine.map((r) => r.tier0)))}  tier 2+ ${pct(mean(mine.map((r) => r.tier2)))}  fell on ${fell}, time alive ${mmss(mean(mine.map((r) => r.alive)))}  ${end}`);
}
process.exit(failed ? 1 : 0);
