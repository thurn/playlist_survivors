/**
 * Replays a reactive play log's presses through the current simulation, as the live runner plays
 * them, and compares the groove with the recorded run's: how the current rules would have judged
 * that run.
 *
 *   npm run reactive:replay -- [<log.json> ...] [--windows 43-54,89-95,131-137]
 *
 * With no log, the newest in the main checkout's logs/play/. Only reactive runs (level
 * `reactive.<track>`) replay.
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve } from "node:path";
import { grooveAt } from "../src/plan/evaluate.ts";
import { liveRun } from "../src/planner/reactive-checks.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { ROOT } from "./lib/files.ts";
import { propsFromAtlas } from "./lib/plan-node.ts";
import { reactiveInput, readStreamFile } from "./lib/reactive-node.ts";

const args = process.argv.slice(2);
const opt = (k: string) => {
  const i = args.indexOf(k);
  return i >= 0 ? args.splice(i, 2)[1] : undefined;
};
const windows = (opt("--windows") ?? "").split(",").filter(Boolean).map((w) => w.split("-").map(Number) as [number, number]);
/** The main checkout's logs (a worktree has none). */
const logDir = ROOT.includes("/.worktrees/") ? resolve(ROOT, "../../logs/play") : join(ROOT, "logs/play");
const newest = () => {
  const files = readdirSync(logDir).filter((f) => f.endsWith(".json"));
  return join(logDir, files.sort((a, b) => statSync(join(logDir, b)).mtimeMs - statSync(join(logDir, a)).mtimeMs)[0]);
};
const logs = args.length ? args : [newest()];
const props = propsFromAtlas();

interface Log {
  id: string;
  level: string;
  seed: number;
  inputs: { s: number; kind: "press" | "release"; key: string }[];
  groove: { s: number[]; v: number[] };
  settles?: { p: number; provisional: string; settled: string }[];
}

const GOOD = new Set(["perfect", "great", "offbeat-perfect", "offbeat-great"]);
const BAD = new Set(["stray", "loose", "good"]);

for (const path of logs) {
  const log = JSON.parse(readFileSync(path, "utf8")) as Log;
  if (!log.level.startsWith("reactive.")) {
    console.log(`${path}: not a reactive run (${log.level})`);
    continue;
  }
  const track = log.level.slice("reactive.".length);
  const run = reactiveInput(readStreamFile(track), { seed: log.seed });
  const keys: KeyEvent[] = log.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key as KeyEvent["key"] }));
  const plan = liveRun(run.input, props, { keys }).recording.plan;
  const end = Math.max(...log.groove.s, 0) + 2;
  const avg = (g: { s: ArrayLike<number>; v: ArrayLike<number> }, a: number, b: number) => {
    let sum = 0;
    let n = 0;
    for (let t = a; t < b; t += 0.25, n++) sum += grooveAt(g, t);
    return n ? sum / n : 0;
  };
  const demoted = (st: { provisional: string; settled: string }[] | undefined) => (st ?? []).filter((x) => GOOD.has(x.provisional) && BAD.has(x.settled)).length;
  console.log(`\n${log.id} (${track}, seed ${log.seed}, ${log.inputs.filter((i) => i.kind === "press").length} presses)`);
  console.log(`  average groove: recorded ${avg(log.groove, 0, end).toFixed(2)}, replayed ${avg(plan.groove, 0, end).toFixed(2)}`);
  console.log(`  presses rated well at first but settled stray, loose or good: recorded ${demoted(log.settles)}, replayed ${demoted(plan.settles)}`);
  const step = 4;
  const row = (g: { s: ArrayLike<number>; v: ArrayLike<number> }) => Array.from({ length: Math.ceil(end / step) }, (_, k) => String(Math.round(grooveAt(g, k * step) * 100)).padStart(3)).join(" ");
  console.log(`  groove every ${step} s from 0:`);
  console.log(`    recorded ${row(log.groove)}`);
  console.log(`    replayed ${row(plan.groove)}`);
  for (const [a, b] of windows) {
    const lo = (g: { s: ArrayLike<number>; v: ArrayLike<number> }) => {
      let m = Infinity;
      for (let t = a; t < b; t += 0.1) m = Math.min(m, grooveAt(g, t));
      return m;
    };
    console.log(`  ${a}-${b} s: recorded average ${avg(log.groove, a, b).toFixed(2)} (lowest ${lo(log.groove).toFixed(2)}), replayed ${avg(plan.groove, a, b).toFixed(2)} (lowest ${lo(plan.groove).toFixed(2)})`);
  }
}
