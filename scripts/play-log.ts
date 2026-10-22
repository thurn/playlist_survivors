/**
 * Summarize play mode's timing log: `npm run log:play` reads the newest
 * logs/play/*.json (the dev server saves them as you play), or a file given.
 *
 *   npm run log:play                      # newest log, every press
 *   npm run log:play -- path/to/log.json  # a given log (or one downloaded with F8)
 *   npm run log:play -- --from 30 --to 45 # presses in a song-time range
 *   npm run log:play -- --no-table        # the summary only
 *   npm run log:play -- --no-rerate       # without re-rating under the current rules
 *
 * It re-rates the log's presses under the current groove rules (the log's level as it is now, with
 * grid times from the song map) and prints the classes and time at each tier beside the log's own.
 */
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { prepare } from "../src/planner/plan.ts";
import { summarize, type PlayLogFile, type RerateRules } from "./lib/play-log.ts";
import { plannerInput } from "./lib/plan-node.ts";

const args = process.argv.slice(2);
const opt = (name: string) => {
  const i = args.indexOf(name);
  if (i < 0) return undefined;
  const v = Number(args[i + 1]);
  args.splice(i, 2);
  return v;
};
const from = opt("--from");
const to = opt("--to");
const table = !args.includes("--no-table");
const rerating = !args.includes("--no-rerate");
const file =
  args.find((a) => !a.startsWith("--")) ??
  (() => {
    const dir = "logs/play";
    let files: string[] = [];
    try {
      files = readdirSync(dir).filter((x) => x.endsWith(".json"));
    } catch {
      // No logs yet.
    }
    if (files.length === 0) {
      console.error(`No logs in ${dir}/: play in the dev server (npm run dev, ?mode=play) and pause or finish to save one.`);
      process.exit(1);
    }
    return files.map((x) => join(dir, x)).sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs)[0];
  })();

/** The current rules for re-rating: the log's level as it is now (play mode's by default), on its song map. */
function currentRules(log: PlayLogFile): RerateRules {
  const own = `levels/${log.level ?? ""}.yaml`;
  const { ctx, sch } = prepare(plannerInput({ level: log.level && existsSync(own) ? own : "levels/orpheus-galop.play.yaml" }));
  return {
    hero: ctx.lib.hero,
    grid: sch.gridTimes,
    beats: ctx.song.beats,
    sections: ctx.sections.map((x) => ({ label: x.label, s0: x.s0, s1: x.s1, movement: x.level.movement, beatsPerBar: ctx.idx.beatsPerBar[x.song.bars[0]] ?? 2 })),
  };
}

const log = JSON.parse(readFileSync(file, "utf8")) as PlayLogFile;
console.log(`${file}\n`);
console.log(summarize(log, { from, to, table, rerate: rerating ? currentRules(log) : undefined }));
