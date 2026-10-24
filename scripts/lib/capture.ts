/**
 * Headless captures of the game at exact song times (the test hook's virtual clock), and captioned
 * contact sheets of them: npm run capture's and the feel dossiers' (scripts/reactive-dossier.ts).
 */
import { chromium, type Browser, type Page } from "@playwright/test";
import sharp from "sharp";

export type Hook = {
  ready: Promise<void>;
  renderAt(s: number): Promise<void>;
  plan: { sections: { s0: number }[]; songBeats: Record<string, number> | number[]; duration: number };
  renderMix?: (parts: { music: boolean; sfx: boolean }) => Promise<AudioBuffer>;
};

export interface GamePage {
  page: Page;
  /** Renders song time s and screenshots the canvas. */
  shoot(s: number): Promise<Buffer>;
  /** The plan's song beats (committed beats in reactive mode). */
  beats(): Promise<number[]>;
  close(): Promise<void>;
}

let browser: Browser | undefined;

/** Opens the game at `?test&<query>` and waits for its plan; the title overlay is removed. */
export async function openGame(base: string, query: string, opts: { width?: number; height?: number; seed?: string } = {}): Promise<GamePage> {
  browser ??= await chromium.launch({ args: ["--use-angle=metal", "--enable-gpu", "--ignore-gpu-blocklist", "--autoplay-policy=no-user-gesture-required"] });
  const page = await browser.newPage({ viewport: { width: opts.width ?? 1920, height: opts.height ?? 1080 } });
  page.on("pageerror", (e) => console.error("page error:", e.message));
  await page.goto(`${base}/?test${opts.seed ? `&seed=${opts.seed}` : ""}${query ? `&${query}` : ""}`);
  await page.waitForFunction(() => (window as unknown as { __demo?: Hook }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.ready);
  await page.evaluate(() => document.querySelectorAll("body > div:not(#app)").forEach((d) => d.remove()));
  return {
    page,
    shoot: async (s) => {
      await page.evaluate((t) => (window as unknown as { __demo: Hook }).__demo.renderAt(t), s);
      return page.locator("canvas").screenshot();
    },
    beats: () => page.evaluate(() => Object.values((window as unknown as { __demo: Hook }).__demo.plan.songBeats) as number[]),
    close: () => page.close(),
  };
}

export async function closeBrowser(): Promise<void> {
  await browser?.close();
  browser = undefined;
}

/** A sheet's tile size and caption band (pixels). */
export const TILE_W = 640;
export const TILE_H = 360;
export const CAPTION_H = 26;

const escape = (s: string) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

/** One captioned tile: the frame scaled down with a caption band beneath. */
export async function tile(png: Buffer, caption: string): Promise<Buffer> {
  const frame = await sharp(png).resize(TILE_W, TILE_H).toBuffer();
  const band = Buffer.from(
    `<svg width="${TILE_W}" height="${CAPTION_H}"><rect width="100%" height="100%" fill="#101014"/><text x="8" y="18" font-family="Menlo, monospace" font-size="15" fill="#e8e8f0">${escape(caption)}</text></svg>`,
  );
  return sharp({ create: { width: TILE_W, height: TILE_H + CAPTION_H, channels: 3, background: "#101014" } })
    .composite([
      { input: frame, left: 0, top: 0 },
      { input: band, left: 0, top: TILE_H },
    ])
    .png()
    .toBuffer();
}

/** Captioned tiles in rows of `cols` under a title, written to `file`. */
export async function sheet(tiles: Buffer[], cols: number, title: string, file: string): Promise<void> {
  const th = TILE_H + CAPTION_H;
  const rows = Math.ceil(tiles.length / cols);
  const head = Buffer.from(
    `<svg width="${cols * TILE_W}" height="${CAPTION_H + 8}"><rect width="100%" height="100%" fill="#000"/><text x="10" y="23" font-family="Menlo, monospace" font-size="18" fill="#fff">${escape(title)}</text></svg>`,
  );
  await sharp({ create: { width: cols * TILE_W, height: rows * th + CAPTION_H + 8, channels: 3, background: "#000" } })
    .composite([{ input: head, left: 0, top: 0 }, ...tiles.map((input, i) => ({ input, left: (i % cols) * TILE_W, top: CAPTION_H + 8 + Math.floor(i / cols) * th }))])
    .png()
    .toFile(file);
}

/** The phases a beat sheet shows of each beat. */
export const PHASES = [0, 0.25, 0.5, 0.75];

/** A beat sheet's 16 frames: each phase of the 4 beats from the first at or after t. */
export function beatSheetTimes(beats: readonly number[], t: number): { beat: number; phase: number; s: number }[] {
  const k = beats.findIndex((b) => b >= t - 1e-9);
  if (k < 0 || k + 4 >= beats.length) throw new Error(`no 4 beats after ${t} s (the song has ${beats.length})`);
  const out: { beat: number; phase: number; s: number }[] = [];
  for (let j = 0; j < 4; j++) for (const phase of PHASES) out.push({ beat: k + j, phase, s: beats[k + j] + phase * (beats[k + j + 1] - beats[k + j]) });
  return out;
}
