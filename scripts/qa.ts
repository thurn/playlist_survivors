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
import { listenInPage, type ListenResult } from "./lib/listen.ts";

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
  plan: {
    duration: number;
    songBeats: number[];
    sections: { label: string; s0: number }[];
    slams: { w1: number }[];
    nearMisses: { s: number; kind: string }[];
    threats: { s: number; kind: string; outcome: string }[];
    swings: { s: number; kind: string }[];
    hits: { s: number; source: string }[];
    inputs: { s: number; kind: string; rating?: string; steps: boolean; bend?: boolean }[];
    grid: ArrayLike<number>;
    warp: { s: number; w: number; rate: number }[];
    hero: { rate: number; x: ArrayLike<number>; y: ArrayLike<number> };
    formations: { def: string; sRelease: number }[];
    boss?: { chordS: number; deathS?: number };
    profile: string;
    stats: { averageGroove: number; presses: number; ratings: { perfect: number; great: number; good: number; none: number } };
    hud: { kills: ArrayLike<number> };
  };
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

let listen: ListenResult | undefined;

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
    record("Planning time", planned.source === "planner" && planned.ms < 3000, `fresh profile, so no cached plan: simulated in the worker, with its checks, in ${planned.ms.toFixed(0)} ms (budget 3000 ms)`);
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
    record("?seed=1859 re-plans and passes every plan check", failed.length === 0 && ms < 3000, failed.length ? `failing: ${failed.join(", ")}` : `all recording checks pass in the browser; simulated in ${ms.toFixed(0)} ms`);
    await ctx.close();
  }

  // ------------------------------------------- step movement and the reward spectrum
  // Each profile's playthrough, simulated in the browser: stillness between steps, every
  // impact on the grid, and how the groove and kills differ between players.
  {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const page = await ctx.newPage();
    const runs: Record<string, { bonus: number; kills: number; presses: number; ratings: Record<string, number>; offGrid: number; swings: number; failed: string[] }> = {};
    for (const player of ["skilled", "casual", "perfect"]) {
      await page.goto(`${base}/?test&player=${player}`);
      await waitReady(page);
      runs[player] = await page.evaluate(() => {
        const d = (window as unknown as { __demo: Hook }).__demo;
        const p = d.plan;
        const grid = Array.from(p.grid);
        const offGrid = p.swings.filter((sw) => Math.min(...grid.map((g) => Math.abs(g - sw.s))) > 0.001).length;
        return {
          bonus: p.stats.averageGroove * 0.2,
          kills: p.hud.kills.length,
          presses: p.stats.presses,
          ratings: p.stats.ratings,
          offGrid,
          swings: p.swings.length,
          failed: d.checks!().filter((c) => c.failures.length > 0).map((c) => c.name),
        };
      });
      if (player === "skilled") {
        // The hero stands still between steps: two moments inside one beat gap of theme A.
        const warp = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.plan.warp);
        const rate = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.plan.hero.rate);
        const toWorld = (t: number) => {
          let b = warp[0];
          for (const x of warp) if (x.s <= t) b = x;
          return b.w + (t - b.s) * b.rate;
        };
        const at = async (t: number) => {
          const f = toWorld(t) * rate;
          const k = Math.floor(f);
          const xy = await page.evaluate((i) => {
            const h = (window as unknown as { __demo: Hook }).__demo.plan.hero;
            return [h.x[i], h.y[i], h.x[i + 1], h.y[i + 1]];
          }, k);
          const u = f - k;
          return { x: xy[0] + (xy[2] - xy[0]) * u, y: xy[1] + (xy[3] - xy[1]) * u };
        };
        const a = await at(75.2);
        const b = await at(75.3);
        const still = Math.hypot(a.x - b.x, a.y - b.y);
        record("Theme A: the hero stands still between steps", still < 0.01, `the hero moves ${still.toFixed(3)} px between 75.20 s and 75.30 s (inside one beat gap)`);
      }
    }
    for (const [player, r] of Object.entries(runs)) {
      record(`?player=${player}: every check passes and every impact is on the grid`, r.failed.length === 0 && r.offGrid === 0, r.failed.length ? `failing: ${r.failed.join(", ")}` : `${r.swings} swings, ${r.offGrid} off the grid; ${r.presses} presses (perfect ${r.ratings.perfect}, great ${r.ratings.great}, good ${r.ratings.good}, unrated ${r.ratings.none}); ${r.kills} kills; average groove +${(r.bonus * 100).toFixed(0)}%`);
    }
    record(
      "Reward spectrum: casual below skilled, perfect at the top",
      runs.casual.bonus < runs.skilled.bonus - 0.03 && runs.casual.kills < runs.skilled.kills && runs.perfect.ratings.great + runs.perfect.ratings.good + runs.perfect.ratings.none === 0 && runs.perfect.bonus > 0.19,
      `average groove: casual +${(runs.casual.bonus * 100).toFixed(0)}%, skilled +${(runs.skilled.bonus * 100).toFixed(0)}%, perfect +${(runs.perfect.bonus * 100).toFixed(0)}% (every press Perfect); kills: casual ${runs.casual.kills}, skilled ${runs.skilled.kills}`,
    );
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
      const tab = (await browser.send("Target.createTarget", { url: "about:blank", background: false, newWindow: false })) as { targetId: string };
      await browser.send("Target.activateTarget", { targetId: tab.targetId });
      const winOf = async (targetId: string) => ((await browser.send("Browser.getWindowForTarget", { targetId })) as { windowId?: number }).windowId;
      const windows = [await winOf(pageId), await winOf(tab.targetId)];
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
        `page ${hidden ? "was hidden for 10 s" : `was NOT hidden (demo in window ${windows[0]}, new tab in window ${windows[1]})`} (${stalled} frames drawn while hidden); ${after.scheduled - before.scheduled} sounds kept playing on the audio clock meanwhile; on return song time had moved ${moved.toFixed(3)} s over ${gap.toFixed(3)} s, the picture is ${lead.toFixed(0)} ms from the audio, and ${after.startedLate} sounds started late`,
      );
      return true;
    });
    if (!ran) record("Hidden tab, real tab switch in the climax (Google Chrome)", false, "Google Chrome is not installed, so the real tab switch was not run");
  }

  // ------------------------------------------------------- listening pass
  // The whole level's mix rendered offline through the live mixing graph, then measured.
  {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const page = await ctx.newPage();
    await page.goto(`${base}/?test`);
    await waitReady(page);
    await page.waitForFunction(() => (window as unknown as { __demo: { renderMix?: unknown } }).__demo.renderMix !== undefined);
    // tsx names functions with a __name helper that does not exist in the page.
    const l = await page.evaluate<ListenResult>(`(() => { const __name = (f) => f; return (${listenInPage.toString()})(); })()`);
    listen = l;
    record("Mix: no clipping", l.clipped === 0 && l.peakDb < 0, `peak ${l.peakDb.toFixed(2)} dBFS, ${l.clipped} clipped samples over the whole level`);
    const below = l.musicPeakDb - l.sfxP95Db;
    record("Mix: effects about 6 dB below the music's peak", below >= 4.5 && below <= 7.5, `music peak ${l.musicPeakDb.toFixed(1)} dB (400 ms RMS); effects 95th percentile ${l.sfxP95Db.toFixed(1)} dB: ${below.toFixed(1)} dB below`);
    const loud = l.sections.filter((x) => x.sfxDb > x.musicDb);
    record("Mix: music stays in front in every section", loud.length === 0, loud.length ? `effects louder than the music in ${loud.map((x) => x.label).join(", ")}` : l.sections.map((x) => `${x.label} ${(x.musicDb - x.sfxDb).toFixed(1)} dB`).join(", "));
    const flat = l.sections.filter((x) => x.mixSpreadDb < x.musicSpreadDb - 1);
    record(
      "Mix: no wall of noise",
      flat.length === 0,
      `the mix keeps the music's rise and fall between hits (p10–p90 of 50 ms levels, music → mix): ${l.sections.map((x) => `${x.label} ${x.musicSpreadDb.toFixed(1)} → ${x.mixSpreadDb.toFixed(1)}`).join(", ")}`,
    );
    record(
      "Mix: effects heard on time",
      l.lateness.over10ms === 0,
      `${l.lateness.events} sound events: perceived onset (decoded file, 5 ms envelope at half peak) minus scheduled time is at most ${Math.abs(l.lateness.maxMs).toFixed(1)} ms; ${l.lateness.over10ms} are off by more than 10 ms`,
    );
    await ctx.close();
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
      return {
        sections: p.sections,
        misses: p.nearMisses,
        heavy: p.swings.filter((a) => a.kind === "heavy" && a.s > 54).map((a) => a.s),
        blast: p.hits.find((h) => h.source === "ring")?.s,
        release: p.formations.find((f) => f.def === "skeleton-kick-line")?.sRelease,
        bossDeath: p.boss?.deathS,
      };
    });
    const moments: [string, number][] = [];
    for (const s of plan.sections) moments.push([`section-${s.label}`, s.s0 + 0.6]);
    // The first assembled check: two moments inside one beat gap of theme A.
    moments.push(["theme-a-75.20", 75.2], ["theme-a-75.30", 75.3]);
    if (plan.release !== undefined) moments.push(["kick-line-release", plan.release + 0.3]);
    if (plan.bossDeath !== undefined) moments.push(["boss-death", plan.bossDeath + 0.05]);
    const arrow = plan.misses.find((m) => m.kind === "arrow");
    const slam = plan.misses.find((m) => m.kind === "slam");
    const cleave = plan.heavy[0];
    const blast = plan.blast !== undefined ? { s: plan.blast } : undefined;
    if (arrow) moments.push(["archer-near-miss", arrow.s + 0.02]);
    if (slam) moments.push(["boss-slam-dodged", slam.s + 0.02]);
    if (cleave !== undefined) moments.push(["first-cleave", cleave + 0.05]);
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
    // The reward spectrum: the casual player at the same moment of theme A.
    await page.goto(`${base}/?test&player=casual`);
    await waitReady(page);
    await page.evaluate(() => document.querySelectorAll("body > div:not(#app)").forEach((d) => d.remove()));
    await page.evaluate((s) => (window as unknown as { __demo: Hook }).__demo.renderAt(s), 75.2);
    await page.locator("canvas").screenshot({ path: join(OUT, "casual-theme-a-75.20.png") });
    record("Review captures", true, `${moments.length + 1} frames written to captures/qa/ for the human review`);
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
  ...(listen
    ? [
        "## Mix by section",
        "",
        "Levels are RMS of the offline render; spread is the p10–p90 range of 50 ms levels.",
        "",
        "| Section | Music dB | Effects dB | Mix dB | Music spread dB | Mix spread dB |",
        "|---|---|---|---|---|---|",
        ...listen.sections.map((x) => `| ${x.label} | ${x.musicDb.toFixed(1)} | ${x.sfxDb.toFixed(1)} | ${x.mixDb.toFixed(1)} | ${x.musicSpreadDb.toFixed(1)} | ${x.mixSpreadDb.toFixed(1)} |`),
        "",
      ]
    : []),
].join("\n");
writeFileSync(join(OUT, "report.md"), md);
writeFileSync(join(OUT, "report.json"), JSON.stringify(results, null, 1));
console.log(ok ? "QA passed" : "QA FAILED");
process.exit(ok ? 0 : 1);
