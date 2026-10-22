/**
 * Benchmark: plays the whole song on the virtual clock at real-time speed in
 * headed Chrome, recording JavaScript frame time with performance marks and
 * GPU time with EXT_disjoint_timer_query_webgl2 where available. Fails if a
 * budget is exceeded at the 95th percentile or any frame exceeds 33 ms.
 *
 * With --play it benchmarks live play instead: theme A to the climax with a
 * press on every beat, each arriving a little late, so the frame time includes
 * the live simulation, its snapshots and the rollbacks.
 *
 *   npm run bench [-- --headless] [-- --play]
 */
import { chromium } from "@playwright/test";
import { withDevServer } from "./lib/server.ts";

const JS_BUDGET_MS = 8;
const GPU_BUDGET_MS = 10;
const MAX_FRAME_MS = 33;
const headless = process.argv.includes("--headless");
const play = process.argv.includes("--play");

interface Bench {
  frames: number;
  jsMs: number[];
  gpuMs: number[];
  intervalMs: number[];
}

const pct = (xs: number[], p: number) => {
  if (xs.length === 0) return NaN;
  const s = [...xs].sort((a, b) => a - b);
  return s[Math.min(s.length - 1, Math.floor(p * s.length))];
};

const result = await withDevServer(async (base) => {
  const browser = await chromium.launch({ headless, args: ["--use-angle=metal", "--enable-gpu", "--ignore-gpu-blocklist", "--disable-renderer-backgrounding", "--disable-background-timer-throttling"] });
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  page.on("pageerror", (e) => console.error("page error:", e.message));
  await page.goto(`${base}/?bench${play ? "&mode=play" : ""}`);
  await page.waitForFunction(() => (window as unknown as { __demo?: unknown }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: { ready: Promise<void> } }).__demo.ready);
  await page.waitForFunction(() => (window as unknown as { __demo: { bench?: Promise<unknown> } }).__demo.bench !== undefined);
  const r = (await page.evaluate(() => (window as unknown as { __demo: { bench: Promise<Bench> } }).__demo.bench, undefined)) as Bench;
  if (play) {
    const st = await page.evaluate(() => (window as unknown as { __demo: { live: { runner: { stats: unknown } } } }).__demo.live.runner.stats);
    console.log(`live runner: ${JSON.stringify(st)}`);
  }
  await browser.close();
  return r;
});

// Skip the first second of frames (shader compilation and texture uploads).
const warm = Math.min(result.jsMs.length - 1, 60);
const js = result.jsMs.slice(warm);
const gpu = result.gpuMs.slice(warm);
const intervals = result.intervalMs.slice(warm);
const jsP95 = pct(js, 0.95);
const gpuP95 = pct(gpu, 0.95);
const worst = Math.max(...js, ...gpu);
console.log(`frames ${result.frames}`);
console.log(`JavaScript frame time: median ${pct(js, 0.5).toFixed(2)} ms, p95 ${jsP95.toFixed(2)} ms, max ${Math.max(...js).toFixed(2)} ms (budget ${JS_BUDGET_MS} ms at p95)`);
if (gpu.length > 0) console.log(`GPU frame time: median ${pct(gpu, 0.5).toFixed(2)} ms, p95 ${gpuP95.toFixed(2)} ms, max ${Math.max(...gpu).toFixed(2)} ms (budget ${GPU_BUDGET_MS} ms at p95)`);
else console.log("GPU frame time: EXT_disjoint_timer_query_webgl2 unavailable; skipped");
console.log(`frame interval: median ${pct(intervals, 0.5).toFixed(2)} ms, p95 ${pct(intervals, 0.95).toFixed(2)} ms, max ${Math.max(...intervals).toFixed(2)} ms`);
let failed = false;
if (jsP95 > JS_BUDGET_MS) {
  console.error(`FAIL: JavaScript p95 ${jsP95.toFixed(2)} ms exceeds ${JS_BUDGET_MS} ms`);
  failed = true;
}
if (gpu.length > 0 && gpuP95 > GPU_BUDGET_MS) {
  console.error(`FAIL: GPU p95 ${gpuP95.toFixed(2)} ms exceeds ${GPU_BUDGET_MS} ms`);
  failed = true;
}
if (worst > MAX_FRAME_MS) {
  console.error(`FAIL: a frame took ${worst.toFixed(1)} ms (limit ${MAX_FRAME_MS} ms)`);
  failed = true;
}
console.log(failed ? "benchmark FAILED" : "benchmark passed");
process.exit(failed ? 1 : 0);
