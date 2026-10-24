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
import { mkdirSync } from "node:fs";
import { join, resolve } from "node:path";
import { chromium, type Page } from "@playwright/test";
import sharp from "sharp";
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

type Hook = { ready: Promise<void>; renderAt(s: number): Promise<void>; plan: { sections: { s0: number }[]; songBeats: Record<string, number> | number[] } };
const prefix = `${query ? `${query.replace(/[^a-z0-9]+/gi, "-")}-` : ""}`;
const stamp = (t: number) => t.toFixed(2).padStart(7, "0");

/** A beat sheet's tile size and caption band (pixels). */
const TILE_W = 640;
const TILE_H = 360;
const CAPTION_H = 26;
const PHASES = [0, 0.25, 0.5, 0.75];

/** The 16 song times of a beat sheet from t: each phase of the 4 song beats from the first at or after t. */
async function sheetTimes(page: Page, t: number): Promise<{ beat: number; phase: number; s: number }[]> {
  const beats = await page.evaluate(() => Object.values((window as unknown as { __demo: Hook }).__demo.plan.songBeats) as number[]);
  const k = beats.findIndex((b) => b >= t - 1e-9);
  if (k < 0 || k + 4 >= beats.length) throw new Error(`no 4 song beats after ${t} s (the song has ${beats.length})`);
  const out: { beat: number; phase: number; s: number }[] = [];
  for (let j = 0; j < 4; j++) for (const phase of PHASES) out.push({ beat: k + j, phase, s: beats[k + j] + phase * (beats[k + j + 1] - beats[k + j]) });
  return out;
}

const escape = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

/** One captioned tile: the frame scaled down with a caption band beneath. */
async function tile(png: Buffer, caption: string): Promise<Buffer> {
  const frame = await sharp(png).resize(TILE_W, TILE_H).toBuffer();
  const band = Buffer.from(`<svg width="${TILE_W}" height="${CAPTION_H}"><rect width="100%" height="100%" fill="#101014"/><text x="8" y="18" font-family="Menlo, monospace" font-size="15" fill="#e8e8f0">${escape(caption)}</text></svg>`);
  return sharp({ create: { width: TILE_W, height: TILE_H + CAPTION_H, channels: 3, background: "#101014" } })
    .composite([
      { input: frame, left: 0, top: 0 },
      { input: band, left: 0, top: TILE_H },
    ])
    .png()
    .toBuffer();
}

await withDevServer(async (base) => {
  const browser = await chromium.launch({ args: ["--use-angle=metal", "--enable-gpu", "--ignore-gpu-blocklist"] });
  const page = await browser.newPage({ viewport: { width, height } });
  page.on("pageerror", (e) => console.error("page error:", e.message));
  await page.goto(`${base}/?test${seed ? `&seed=${seed}` : ""}${query ? `&${query}` : ""}`);
  await page.waitForFunction(() => (window as unknown as { __demo?: Hook }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.ready);
  // Hide the title overlay so captures show the game.
  await page.evaluate(() => document.querySelectorAll("body > div:not(#app)").forEach((d) => d.remove()));
  const shoot = async (s: number) => {
    await page.evaluate((t) => (window as unknown as { __demo: Hook }).__demo.renderAt(t), s);
    return page.locator("canvas").screenshot();
  };
  if (beatSheet !== undefined) {
    for (const t of beatSheet.split(",").map(Number)) {
      const tiles: Buffer[] = [];
      for (const f of await sheetTimes(page, t)) tiles.push(await tile(await shoot(f.s), `beat ${f.beat} +${f.phase.toFixed(2)}  s=${f.s.toFixed(3)}`));
      const title = Buffer.from(`<svg width="${4 * TILE_W}" height="${CAPTION_H + 8}"><rect width="100%" height="100%" fill="#000"/><text x="10" y="23" font-family="Menlo, monospace" font-size="18" fill="#fff">${escape(`${query ?? "demo"}${seed ? ` seed ${seed}` : ""}: beat sheet from ${t} s (rows: beats; columns: phases 0, .25, .5, .75)`)}</text></svg>`);
      const th = TILE_H + CAPTION_H;
      const file = join(out, `${prefix}beat-sheet-t-${stamp(t)}${seed ? `-seed${seed}` : ""}.png`);
      await sharp({ create: { width: 4 * TILE_W, height: 4 * th + CAPTION_H + 8, channels: 3, background: "#000" } })
        .composite([{ input: title, left: 0, top: 0 }, ...tiles.map((input, i) => ({ input, left: (i % 4) * TILE_W, top: CAPTION_H + 8 + Math.floor(i / 4) * th }))])
        .png()
        .toFile(file);
      console.log(`captured ${file}`);
    }
  } else {
    let times: number[];
    if (args.includes("--sections")) {
      times = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.plan.sections.map((s) => s.s0 + 0.6));
    } else {
      times = (arg("--times") ?? "12,35.2,88.31,141.5").split(",").map(Number);
    }
    for (const t of times) {
      const file = join(out, `${prefix}t-${stamp(t)}${seed ? `-seed${seed}` : ""}.png`);
      await page.evaluate((s) => (window as unknown as { __demo: Hook }).__demo.renderAt(s), t);
      await page.locator("canvas").screenshot({ path: file });
      console.log(`captured ${file}`);
    }
  }
  await browser.close();
});
