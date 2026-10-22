/**
 * Frame capture: opens the demo in headless Chrome with a test hook that
 * replaces the audio clock with a virtual one, then saves screenshots at
 * exact song times.
 *
 *   npm run capture -- --times 12.0,35.2,88.31,141.5 --out captures/
 *   npm run capture -- --sections            (the start of every section)
 *   npm run capture -- --seed 1859 --times 60
 *   npm run capture -- --query mode=play&player=moderate --times 90   (play mode's plan for a virtual player)
 */
import { mkdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { chromium } from "@playwright/test";
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
mkdirSync(out, { recursive: true });

await withDevServer(async (base) => {
  const browser = await chromium.launch({ args: ["--use-angle=metal", "--enable-gpu", "--ignore-gpu-blocklist"] });
  const page = await browser.newPage({ viewport: { width, height } });
  page.on("pageerror", (e) => console.error("page error:", e.message));
  await page.goto(`${base}/?test${seed ? `&seed=${seed}` : ""}${query ? `&${query}` : ""}`);
  await page.waitForFunction(() => (window as unknown as { __demo?: { ready: Promise<void> } }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: { ready: Promise<void> } }).__demo.ready);
  let times: number[];
  if (args.includes("--sections")) {
    times = await page.evaluate(() => (window as unknown as { __demo: { plan: { sections: { s0: number }[] } } }).__demo.plan.sections.map((s) => s.s0 + 0.6));
  } else {
    times = (arg("--times") ?? "12,35.2,88.31,141.5").split(",").map(Number);
  }
  // Hide the title overlay so captures show the game.
  await page.evaluate(() => document.querySelectorAll("body > div:not(#app)").forEach((d) => d.remove()));
  for (const t of times) {
    await page.evaluate((s) => (window as unknown as { __demo: { renderAt(s: number): Promise<void> } }).__demo.renderAt(s), t);
    const file = join(out, `${query ? `${query.replace(/[^a-z0-9]+/gi, "-")}-` : ""}t-${t.toFixed(2).padStart(7, "0")}${seed ? `-seed${seed}` : ""}.png`);
    await page.locator("canvas").screenshot({ path: file });
    console.log(`captured ${file}`);
  }
  await browser.close();
});
