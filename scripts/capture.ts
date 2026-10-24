/**
 * Frame capture: opens the demo in headless Chrome with a test hook that
 * replaces the audio clock with a virtual one, then saves screenshots at
 * exact song times.
 *
 *   npm run capture -- --times 12.0,35.2,88.31,141.5 --out captures/
 *   npm run capture -- --sections            (the start of every section)
 *   npm run capture -- --seed 1859 --times 60
 *   npm run capture -- --query mode=play&player=moderate --times 90   (play mode's plan for a virtual player)
 *   npm run capture -- --query mode=reactive&track=orpheus-galop&player=reactive-moderate --beat-sheet 30,60
 *
 * `--beat-sheet <t>` saves one image per time instead: 16 frames, at phases 0, 0.25, 0.5 and 0.75
 * of the 4 consecutive song beats (committed beats, in reactive mode) from t, one beat a row,
 * tiled and captioned.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { beatSheetTimes, closeBrowser, openGame, sheet, tile, type Hook } from "./lib/capture.ts";
import { ROOT } from "./lib/files.ts";
import { withDevServer } from "./lib/server.ts";

const args = process.argv.slice(2);
const arg = (k: string) => {
  const i = args.indexOf(k);
  return i >= 0 ? args[i + 1] : undefined;
};
const out = resolve(ROOT, arg("--out") ?? "captures");
const seed = arg("--seed");
/** More URL parameters (such as mode=play&player=moderate). */
const query = arg("--query");
const width = Number(arg("--width") ?? 1920);
const height = Number(arg("--height") ?? 1080);
const beatSheet = arg("--beat-sheet");
mkdirSync(out, { recursive: true });

const prefix = `${query ? `${query.replace(/[^a-z0-9]+/gi, "-")}-` : ""}`;
const stamp = (t: number) => t.toFixed(2).padStart(7, "0");

await withDevServer(async (base) => {
  const game = await openGame(base, query ?? "", { width, height, seed });
  if (beatSheet !== undefined) {
    const beats = await game.beats();
    for (const t of beatSheet.split(",").map(Number)) {
      const tiles: Buffer[] = [];
      for (const f of beatSheetTimes(beats, t)) tiles.push(await tile(await game.shoot(f.s), `beat ${f.beat} +${f.phase.toFixed(2)}  s=${f.s.toFixed(3)}`));
      const file = join(out, `${prefix}beat-sheet-t-${stamp(t)}${seed ? `-seed${seed}` : ""}.png`);
      await sheet(tiles, 4, `${query ?? "demo"}${seed ? ` seed ${seed}` : ""}: beat sheet from ${t} s (rows: beats; columns: phases 0, .25, .5, .75)`, file);
      console.log(`captured ${file}`);
    }
  } else {
    let times: number[];
    if (args.includes("--sections")) {
      times = await game.page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.plan.sections.map((s) => s.s0 + 0.6));
    } else {
      times = (arg("--times") ?? "12,35.2,88.31,141.5").split(",").map(Number);
    }
    for (const t of times) {
      const file = join(out, `${prefix}t-${stamp(t)}${seed ? `-seed${seed}` : ""}.png`);
      writeFileSync(file, await game.shoot(t));
      console.log(`captured ${file}`);
    }
  }
  await closeBrowser();
});
