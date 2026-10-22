/**
 * Summarize play mode's timing log: `npm run log:play` reads the newest
 * logs/play/*.json (the dev server saves them as you play), or a file given.
 *
 *   npm run log:play                      # newest log, every press
 *   npm run log:play -- path/to/log.json  # a given log (or one downloaded with F8)
 *   npm run log:play -- --from 30 --to 45 # presses in a song-time range
 *   npm run log:play -- --no-table        # the summary only
 */
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { summarize, type PlayLogFile } from "./lib/play-log.ts";

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

const log = JSON.parse(readFileSync(file, "utf8")) as PlayLogFile;
console.log(`${file}\n`);
console.log(summarize(log, { from, to, table }));
