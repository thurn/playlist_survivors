/**
 * Automated pass over the design's Manual QA checklist: everything a machine
 * can verify in real Chrome playback, plus captures of each checklist moment
 * for the human review. Writes captures/qa/report.md and report.json.
 *
 *   npm run qa
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { chromium, type Page } from "@playwright/test";
import sharp from "sharp";
import { ROOT } from "./lib/files.ts";
import { withDevServer } from "./lib/server.ts";
import { withChrome } from "./lib/chrome.ts";

const OUT = join(ROOT, "captures/qa");
mkdirSync(OUT, { recursive: true });

interface Result {
  item: string;
  ok: boolean;
  detail: string;
}
const results: Result[] = [];
const record = (item: string, ok: boolean, detail: string) => {
  results.push({ item, ok, detail });
  console.log(`${ok ? "ok  " : "FAIL"} ${item}: ${detail}`);
};

interface AudioState {
  state: string;
  contextSongTime: number;
  stats: { scheduled: number; skippedLate: number; startedLate: number; clicks: number; ducks: number; ticks: number };
}
interface Hook {
  ready: Promise<void>;
  lastS?: number;
  frames?: number;
  planMs?: number;
  planSource?: string;
  audio?: () => AudioState;
  renderAt(s: number): Promise<void>;
  checks?: () => { name: string; failures: string[] }[];
  stats?: () => { alive: number; particles: number };
  plan: { duration: number; songBeats: number[]; sections: { label: string; s0: number }[]; slams: { w1: number }[]; nearMisses: { s: number; kind: string }[]; actions: { s: number; kind: string }[] };
}
const hook = (page: Page) => page.evaluate(() => (window as unknown as { __demo: Hook }).__demo);
void hook;

async function waitReady(page: Page) {
  await page.waitForFunction(() => (window as unknown as { __demo?: unknown }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.ready);
}

async function play(page: Page) {
  await page.getByText("Click to begin").click();
  await page.waitForFunction(() => (window as unknown as { __demo: Hook }).__demo.audio !== undefined, undefined, { timeout: 10_000 });
}

const now = (page: Page) =>
  page.evaluate(() => {
    const d = (window as unknown as { __demo: Hook }).__demo;
    const a = d.audio!();
    return { lastS: d.lastS ?? 0, frames: d.frames ?? 0, audio: a, perf: performance.now() };
  });

await withDevServer(async (base) => {
  const browser = await chromium.launch({ args: ["--use-angle=metal", "--enable-gpu", "--autoplay-policy=no-user-gesture-required"] });

  // ---------------------------------------------------------------- playback
  {
    const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await ctx.newPage();
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(e.message));
    await page.goto(`${base}/?test`);
    await waitReady(page);
    const planned = await page.evaluate(() => {
      const d = (window as unknown as { __demo: Hook }).__demo;
      return { ms: d.planMs ?? NaN, source: d.planSource ?? "?" };
    });
    record("Planning time", planned.source === "planner" && planned.ms < 2000, `fresh profile, so no cached plan: planned in the worker, with its output checks, in ${planned.ms.toFixed(0)} ms (budget 2000 ms)`);
    record("Start: title screen", await page.getByText("Click to begin").isVisible(), "the title screen shows “Click to begin” once loading finishes");
    await play(page);
    await page.waitForTimeout(2000);
    const a = await now(page);
    await page.waitForTimeout(4000);
    const b = await now(page);
    const rate = (b.lastS - a.lastS) / ((b.perf - a.perf) / 1000);
    record("Audio clock", a.audio.state === "running" && Math.abs(rate - 1) < 0.02, `AudioContext ${b.audio.state}; song time advanced ${rate.toFixed(3)}× real time over 4 s`);
    const lead = b.lastS - b.audio.contextSongTime;
    record("Picture vs audio", Math.abs(lead) < 0.12, `frame song time − audio-thread song time = ${(lead * 1000).toFixed(0)} ms (output latency plus one frame)`);
    const fps = (b.frames - a.frames) / ((b.perf - a.perf) / 1000);
    record("Frame rate", fps >= 55, `${fps.toFixed(1)} frames per second during the prelude`);

    record("No page errors so far", errors.length === 0, errors.length ? errors.join("; ") : "none during playback");
    await ctx.close();
  }

  // ------------------------------------------------------------- hidden tab
  // A hidden tab stops animation frames and timers while the audio thread plays on.
  // Playwright keeps its tabs visible, so the page's main thread is stalled for 10 s
  // instead, which stops both in the same way.
  {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const page = await ctx.newPage();
    await page.goto(`${base}/?test`);
    await waitReady(page);
    await play(page);
    await page.waitForTimeout(1500);
    const before = await now(page);
    await page.evaluate(() => {
      const end = performance.now() + 10_000;
      while (performance.now() < end) {
        // stall: no frames, no timers
      }
    });
    await page.waitForTimeout(400);
    const after = await now(page);
    const jumped = after.lastS - before.lastS;
    const realGap = (after.perf - before.perf) / 1000;
    record(
      "Hidden tab: picture jumps to the right moment",
      Math.abs(after.lastS - after.audio.contextSongTime) < 0.12 && Math.abs(jumped - realGap) < 0.3,
      `after a 10 s stall, song time had moved ${jumped.toFixed(2)} s over ${realGap.toFixed(2)} s and the picture is ${((after.lastS - after.audio.contextSongTime) * 1000).toFixed(0)} ms from the audio`,
    );
    const skipped = after.audio.stats.skippedLate - before.audio.stats.skippedLate;
    record("Hidden tab: no burst of late sounds", skipped > 0 && after.audio.stats.startedLate === 0, `${skipped} sound events that fell behind during the stall were skipped; ${after.audio.stats.startedLate} sounds started late`);
    await ctx.close();
  }

  {
    const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await ctx.newPage();
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(e.message));
    await page.goto(`${base}/?test`);
    await waitReady(page);
    await play(page);
    await page.waitForTimeout(1500);
    // Window resize mid-playback.
    await page.setViewportSize({ width: 1280, height: 1024 });
    await page.waitForTimeout(500);
    const png = await page.screenshot();
    const { data, info } = await sharp(png).raw().toBuffer({ resolveWithObject: true });
    const px = (x: number, y: number) => data[(y * info.width + x) * info.channels] + data[(y * info.width + x) * info.channels + 1] + data[(y * info.width + x) * info.channels + 2];
    const barTop = px(640, 20);
    const content = px(640, 512);
    const canvas = await page.evaluate(() => ({ w: document.querySelector("canvas")!.width, h: document.querySelector("canvas")!.height }));
    const r1 = await now(page);
    await page.waitForTimeout(1000);
    const r2 = await now(page);
    writeFileSync(join(OUT, "resize-1280x1024.png"), png);
    record("Window resize", canvas.w === 1280 && canvas.h === 1024 && barTop < 30 && content > 30 && r2.lastS > r1.lastS + 0.9, `canvas ${canvas.w}×${canvas.h}; 16:9 view letterboxed (bar brightness ${barTop}); playback kept running (+${(r2.lastS - r1.lastS).toFixed(2)} s)`);
    record("No page errors", errors.length === 0, errors.length ? errors.join("; ") : "none during playback, freeze, resume and resize");
    await ctx.close();
  }

  // --------------------------------------------------------------- metronome
  {
    const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await ctx.newPage();
    await page.goto(`${base}/?test&metronome`);
    await waitReady(page);
    await play(page);
    await page.waitForTimeout(3000);
    const m = await now(page);
    record("Metronome click track", m.audio.stats.clicks > 0, `${m.audio.stats.clicks} clicks scheduled in the first 3 s (the beat flash itself is checked by the e2e test)`);
    await ctx.close();
  }

  // ------------------------------------------------- another seed, plan checks
  {
    const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await ctx.newPage();
    await page.goto(`${base}/?test&seed=1859`);
    await waitReady(page);
    const failed = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.checks!().filter((c) => c.failures.length > 0).map((c) => c.name));
    const ms = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.planMs ?? NaN);
    record("?seed=1859 re-plans and passes every plan check", failed.length === 0 && ms < 2000, failed.length ? `failing: ${failed.join(", ")}` : `all planner output checks pass in the browser; planned in ${ms.toFixed(0)} ms`);
    await ctx.close();
  }

  // ------------------------------------- real hidden tab, in the climax, in Chrome
  // Playwright's Chromium never really hides a tab, so this drives the installed
  // Google Chrome directly: play into the climax, open a new foreground tab for
  // 10 s, then switch back.
  {
    const ran = await withChrome(`${base}/?test`, async ({ page, pageId, browser }) => {
      const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
      for (let i = 0; i < 300 && !(await page.eval<boolean>("!!(window.__demo && window.__demo.planHash)")); i++) await sleep(200);
      await page.eval("window.__demo.ready");
      const pt = await page.eval<{ x: number; y: number }>(
        `(() => { const el = [...document.querySelectorAll('*')].find((e) => e.children.length === 0 && /Click to begin/.test(e.textContent)); const r = el.getBoundingClientRect(); return { x: r.x + r.width / 2, y: r.y + r.height / 2 }; })()`,
      );
      await page.click(pt.x, pt.y);
      for (let i = 0; i < 50 && !(await page.eval<boolean>("!!window.__demo.audio")); i++) await sleep(100);
      const climax = await page.eval<number>(`window.__demo.plan.sections.find((s) => s.label === "climax").s0`);
      while ((await page.eval<number>("window.__demo.lastS ?? 0")) < climax + 1) await sleep(250);
      type Snap = { vis: string; s: number; frames: number; t: number; ctxS: number; scheduled: number; startedLate: number };
      const snap = () =>
        page.eval<Snap>(
          `(() => { const d = window.__demo, a = d.audio(); return { vis: document.visibilityState, s: d.lastS, frames: d.frames, t: performance.now(), ctxS: a.contextSongTime, scheduled: a.stats.scheduled, startedLate: a.stats.startedLate }; })()`,
        );
      const f0 = await snap();
      await sleep(4000);
      const f1 = await snap();
      const fps = (f1.frames - f0.frames) / ((f1.t - f0.t) / 1000);
      record("Climax frame rate (Google Chrome)", fps >= 55, `${fps.toFixed(1)} frames per second from ${f0.s.toFixed(1)} s to ${f1.s.toFixed(1)} s (the display's refresh rate is the ceiling)`);
      await page.eval("window.__vis = []; document.addEventListener('visibilitychange', () => window.__vis.push(document.visibilityState))");
      const before = await snap();
      const tab = (await browser.send("Target.createTarget", { url: "about:blank", background: false })) as { targetId: string };
      const during: Snap[] = [];
      for (let i = 0; i < 5; i++) {
        await sleep(2000);
        during.push(await snap());
      }
      await browser.send("Target.closeTarget", { targetId: tab.targetId });
      await browser.send("Target.activateTarget", { targetId: pageId });
      await sleep(800);
      const after = await snap();
      const vis = await page.eval<string[]>("window.__vis");
      const hidden = during.every((d) => d.vis === "hidden") && vis[0] === "hidden" && after.vis === "visible";
      const stalled = during[during.length - 1].frames - during[0].frames;
      const moved = after.s - before.s;
      const gap = (after.t - before.t) / 1000;
      const lead = (after.s - after.ctxS) * 1000;
      record(
        "Hidden tab, real tab switch in the climax (Google Chrome)",
        hidden && stalled <= 2 && Math.abs(moved - gap) < 0.1 && Math.abs(lead) < 120 && after.startedLate === 0,
        `page ${hidden ? "was hidden for 10 s" : "was NOT hidden"} (${stalled} frames drawn while hidden); ${after.scheduled - before.scheduled} sounds kept playing on the audio clock meanwhile; on return song time had moved ${moved.toFixed(3)} s over ${gap.toFixed(3)} s, the picture is ${lead.toFixed(0)} ms from the audio, and ${after.startedLate} sounds started late`,
      );
      return true;
    });
    if (!ran) record("Hidden tab, real tab switch in the climax (Google Chrome)", false, "Google Chrome is not installed, so the real tab switch was not run");
  }

  // -------------------------------------------- captures for the human review
  {
    const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await ctx.newPage();
    await page.goto(`${base}/?test`);
    await waitReady(page);
    await page.evaluate(() => document.querySelectorAll("body > div:not(#app)").forEach((d) => d.remove()));
    const plan = await page.evaluate(() => {
      const p = (window as unknown as { __demo: Hook }).__demo.plan;
      return { sections: p.sections, misses: p.nearMisses, actions: p.actions.filter((a) => a.kind !== "attack").map((a) => ({ s: a.s, kind: a.kind })) };
    });
    const moments: [string, number][] = [];
    for (const s of plan.sections) moments.push([`section-${s.label}`, s.s0 + 0.6]);
    const arrow = plan.misses.find((m) => m.kind === "arrow");
    const slam = plan.misses.find((m) => m.kind === "slam");
    const sweep = plan.actions.find((a) => a.kind === "sweep");
    const finisher = plan.actions.find((a) => a.kind === "finisher");
    const blast = plan.actions.find((a) => a.kind === "blast");
    if (arrow) moments.push(["archer-near-miss", arrow.s + 0.02]);
    if (slam) moments.push(["boss-slam-dodged", slam.s + 0.02]);
    if (sweep) moments.push(["kick-line-sweep", sweep.s + 0.35]);
    if (finisher) moments.push(["boss-death", finisher.s + 0.05]);
    if (blast) moments.push(["finale-blast", blast.s + 0.3]);
    moments.push(["end-card", 147]);
    for (const [name, t] of moments) {
      await page.evaluate((s) => (window as unknown as { __demo: Hook }).__demo.renderAt(s), t);
      await page.locator("canvas").screenshot({ path: join(OUT, `${name}.png`) });
    }
    // The finale blast clears every monster within a beat of the final chord.
    if (blast) {
      const counts: string[] = [];
      let cleared = false;
      const beats = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.plan.songBeats);
      const next = beats.find((b) => b > blast.s + 1e-3) ?? blast.s + 0.5;
      for (const t of [blast.s - 0.02, next, next + 0.5]) {
        await page.evaluate((s) => (window as unknown as { __demo: Hook }).__demo.renderAt(s), t);
        const alive = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.stats!().alive);
        counts.push(`${alive} at ${t.toFixed(2)} s`);
        if (t === next) cleared = alive === 0;
      }
      await page.locator("canvas").screenshot({ path: join(OUT, "finale-after.png") });
      record("Finale clears the field within a beat", cleared, `living monsters: ${counts.join(", ")} (final chord at ${blast.s.toFixed(2)} s)`);
    }
    record("Review captures", true, `${moments.length} frames written to captures/qa/ for the human review`);
    await ctx.close();
  }
  await browser.close();
});

const ok = results.every((r) => r.ok);
const md = [
  "# Automated QA run",
  "",
  `Run on ${new Date().toISOString()} in Chromium at 1920×1080.`,
  "",
  "| Check | Result | Detail |",
  "|---|---|---|",
  ...results.map((r) => `| ${r.item} | ${r.ok ? "ok" : "**FAIL**"} | ${r.detail} |`),
  "",
].join("\n");
writeFileSync(join(OUT, "report.md"), md);
writeFileSync(join(OUT, "report.json"), JSON.stringify(results, null, 1));
console.log(ok ? "QA passed" : "QA FAILED");
process.exit(ok ? 0 : 1);
