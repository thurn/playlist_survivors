/**
 * Automated pass over the design's Manual QA checklist: everything a machine
 * can verify in real Chrome playback, plus captures of each checklist moment
 * for the human review. Writes captures/qa/report.md and report.json.
 *
 *   npm run qa [-- --play]     (--play: only play mode's items)
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { chromium, type Page } from "@playwright/test";
import sharp from "sharp";
import { ROOT } from "./lib/files.ts";
import { runPlanner } from "./lib/plan-node.ts";
import { withDevServer } from "./lib/server.ts";
import { listenInPage, type ListenResult } from "./lib/listen.ts";

const OUT = join(ROOT, "captures/qa");
const onlyPlay = process.argv.includes("--play");
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
  checks?: () => { name: string; failures: string[]; checked?: number }[];
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
    boss?: { deathS?: number };
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
  if (!onlyPlay) {
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
  // Playwright keeps its tabs visible, so the page's main thread is stalled for 3 s
  // instead, which stops both in the same way.
  if (!onlyPlay) {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const page = await ctx.newPage();
    await page.goto(`${base}/?test`);
    await waitReady(page);
    await play(page);
    await page.waitForTimeout(1500);
    const before = await now(page);
    await page.evaluate(() => {
      const end = performance.now() + 3_000;
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
      `after a 3 s stall, song time had moved ${jumped.toFixed(2)} s over ${realGap.toFixed(2)} s and the picture is ${((after.lastS - after.audio.contextSongTime) * 1000).toFixed(0)} ms from the audio`,
    );
    const skipped = after.audio.stats.skippedLate - before.audio.stats.skippedLate;
    record("Hidden tab: no burst of late sounds", skipped > 0 && after.audio.stats.startedLate === 0, `${skipped} sound events that fell behind during the stall were skipped; ${after.audio.stats.startedLate} sounds started late`);
    await ctx.close();
  }

  if (!onlyPlay) {
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
  if (!onlyPlay) {
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

  // ---------------------------------------------- sound on the grid and the reward spectrum
  // Each profile's playthrough, simulated in the browser: every check (sound on the grid among
  // them), every impact on the grid, and how the groove and kills differ between players.
  if (!onlyPlay) {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 720 } });
    const page = await ctx.newPage();
    const runs: Record<string, { groove: number; kills: number; presses: number; ratings: Record<string, number>; offGrid: number; swings: number; failed: string[]; grid?: { failures: number; checked: number } }> = {};
    for (const player of ["skilled", "casual"]) {
      await page.goto(`${base}/?test&player=${player}`);
      await waitReady(page);
      runs[player] = await page.evaluate(() => {
        const d = (window as unknown as { __demo: Hook }).__demo;
        const p = d.plan;
        const grid = Array.from(p.grid);
        const offGrid = p.swings.filter((sw) => Math.min(...grid.map((g) => Math.abs(g - sw.s))) > 0.001).length;
        return {
          groove: p.stats.averageGroove,
          kills: p.hud.kills.length,
          presses: p.stats.presses,
          ratings: p.stats.ratings,
          offGrid,
          swings: p.swings.length,
          failed: d.checks!().filter((c) => c.failures.length > 0).map((c) => c.name),
          grid: d.checks!().filter((c) => c.name === "impacts on the grid").map((c) => ({ failures: c.failures.length, checked: c.checked ?? 0 }))[0],
        };
      });
    }
    for (const [player, r] of Object.entries(runs)) {
      // Sound on the grid: every row of the table (impacts, kills, chimes, level-up sounds...), within 1 ms.
      record(`?player=${player}: every sound on its grid slot`, r.grid !== undefined && r.grid.failures === 0 && r.grid.checked > 1000, r.grid ? `${r.grid.checked} sounds and impacts checked against the Sound on the Grid table, ${r.grid.failures} off their slot` : "no grid-sound check ran");
      record(`?player=${player}: every check passes and every impact is on the grid`, r.failed.length === 0 && r.offGrid === 0, r.failed.length ? `failing: ${r.failed.join(", ")}` : `${r.swings} swings, ${r.offGrid} off the grid; ${r.presses} presses (perfect ${r.ratings.perfect}, great ${r.ratings.great}, good ${r.ratings.good}, unrated ${r.ratings.none}); ${r.kills} kills; average groove ${(r.groove * 100).toFixed(0)}%`);
    }
    record(
      "Reward spectrum: casual below skilled",
      runs.casual.groove < runs.skilled.groove - 0.15 && runs.casual.kills < runs.skilled.kills,
      `average groove: casual ${(runs.casual.groove * 100).toFixed(0)}%, skilled ${(runs.skilled.groove * 100).toFixed(0)}%; kills: casual ${runs.casual.kills}, skilled ${runs.skilled.kills}`,
    );
    await ctx.close();
  }

  // ------------------------------------------------------- listening pass
  // The whole level's mix rendered offline through the live mixing graph, then measured.
  if (!onlyPlay) {
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

  // ------------------------------------------------------------ play mode
  // The Manual QA items of docs/design/gameplay-redesign.md (and docs/design/interactive.md's flow) a
  // machine can judge, in live play with real (synthetic) key presses: strides start at once,
  // impacts stay on the grid, the same speed however you press, mashing and holding, diagonals,
  // stomps, pausing, losing focus, and Watch my run after a level-up.
  {
    type PlayHook = {
      ready: Promise<void>;
      lastS: number;
      frames?: number;
      lastHero?: { s: number; x: number; y: number };
      stats(): { frameMs: number[] };
      play: { phase(): string; menu(): { items: string[] } | undefined; choose(label: string): void; compareReplay(): string[] };
      live: {
        recording: {
          timeWarp: { toWorld(s: number): number };
          plan: { upgrades: { s: number; offers: unknown[]; taken: number }[]; songBeats: ArrayLike<number>; grid: ArrayLike<number>; hero: { x: ArrayLike<number>; y: ArrayLike<number> }; swings: { s: number }[]; inputs: { s: number; kind: string; key: string; steps: boolean; extra?: boolean; bend?: boolean; stride?: number; stomp?: boolean; rock?: boolean; rating?: string }[]; events: { sounds: { s: number; priority: number }[] } };
        };
        key(e: { s: number; kind: "press" | "release"; key: string }): void;
      };
    };
    const ph = (page: Page) => page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.play.phase());
    const songNow = (page: Page) => page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.lastS);
    const nextFrame = (page: Page) => page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(() => r(undefined)))));
    const waitSong = async (page: Page, s: number) => {
      for (let now = await songNow(page); now < s; now = await songNow(page)) await page.waitForTimeout(Math.min(200, Math.max(2, (s - now) * 1000 - 4)));
    };
    const openPlay = async (page: Page, start: string) => {
      await page.goto(`${base}/?mode=play&start=${start}&test`);
      await waitReady(page);
      await page.getByText("Click to begin").click();
      await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play?.phase() === "playing", undefined, { timeout: 30_000 });
    };
    const ctx = await browser.newContext({ viewport: { width: 1920, height: 1080 } });
    const page = await ctx.newPage();
    const errors: string[] = [];
    page.on("pageerror", (e) => errors.push(e.message));
    await openPlay(page, "1:10");
    const beats = await page.evaluate(() => Array.from((window as unknown as { __demo: PlayHook }).__demo.live.recording.plan.songBeats));
    const t0 = (await songNow(page)) + 0.3;
    const f0 = await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.frames ?? 0);
    const p0 = performance.now();
    // A press strides at once: the first frame drawn after a key press shows the hero moving.
    let instant = 0;
    const onBeat = beats.filter((b) => b > t0).slice(0, 8);
    for (const [i, b] of onBeat.entries()) {
      await waitSong(page, b - 0.01);
      const before = await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.lastHero!);
      await page.keyboard.press(i % 2 ? "ArrowLeft" : "ArrowRight");
      await nextFrame(page);
      const after = await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.lastHero!);
      if (Math.hypot(after.x - before.x, after.y - before.y) > 2) instant++;
    }
    record("Play: a press strides at once", instant === onBeat.length, `${instant} of ${onBeat.length} presses moved the hero on the next frame drawn`);
    // Mashing: three presses for one beat are rated once; the extra two stride unrated (and greyed).
    const mash = beats.find((b) => b > onBeat[onBeat.length - 1] + 0.3)!;
    await waitSong(page, mash - 0.01);
    for (let k = 0; k < 3; k++) await page.keyboard.press("ArrowUp");
    // Diagonals: Up and Right together are one diagonal stride.
    const diag = beats.find((b) => b > mash + 0.3)!;
    await waitSong(page, diag - 0.01);
    await page.keyboard.down("ArrowUp");
    await page.keyboard.down("ArrowRight");
    await page.keyboard.up("ArrowUp");
    await page.keyboard.up("ArrowRight");
    // Holding a key presses once (its repeats do nothing) and strides on every beat.
    const holdAt = beats.findIndex((b) => b > diag + 0.3);
    const hold = beats[holdAt];
    const heldBeats = [1, 2, 3].map((k) => beats[holdAt + k]);
    await waitSong(page, hold - 0.01);
    await page.keyboard.down("ArrowDown");
    while ((await songNow(page)) < heldBeats[2] + 0.05) {
      await page.waitForTimeout(40);
      await page.keyboard.down("ArrowDown");
    }
    await page.keyboard.up("ArrowDown");
    await waitSong(page, heldBeats[2] + 0.4);
    const fps = ((await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.frames ?? 0)) - f0) / ((performance.now() - p0) / 1000);
    // (No named functions inside page.evaluate: the bundler would wrap them in a helper the page lacks.)
    const live = await page.evaluate(
      ({ t0, mash, diag, hold, heldBeats }) => {
        const d = (window as unknown as { __demo: PlayHook }).__demo;
        const rec = d.live.recording;
        const plan = rec.plan;
        // The hero's sample at or before each time (a sample at a stride's own start is drawn before
        // it moves), in world time: a cleave's hit-stop freezes the world.
        const ks = [hold - 0.05, ...heldBeats].map((s) => Math.floor(rec.timeWarp.toWorld(s) * 60 + 1e-6));
        const grid = Array.from(plan.grid);
        const ms = d.stats().frameMs.slice(-600).sort((a, b) => a - b);
        const strikes = new Set(plan.events.sounds.filter((x) => x.priority <= 0.3 + 1e-9).map((x) => Math.round(x.s * 1e4)));
        const swings = plan.swings.filter((sw) => sw.s > t0);
        const steps = plan.inputs.filter((i) => i.kind === "press" && i.steps);
        return {
          swings: swings.length,
          offGrid: swings.filter((sw) => Math.min(...grid.map((g) => Math.abs(g - sw.s))) > 0.001).length,
          silent: swings.filter((sw) => !strikes.has(Math.round(sw.s * 1e4))).length,
          mash: steps.filter((i) => i.s >= mash - 0.2 && i.s < mash + 0.2).map((p) => ({ extra: !!p.extra, stride: p.stride })),
          diag: steps.filter((i) => i.s >= diag - 0.2 && i.s < diag + 0.2).map((p) => ({ bend: !!p.bend, stride: p.stride, s: p.s })),
          hold: steps.filter((i) => i.s >= hold - 0.2 && i.s < heldBeats[2] + 0.2).length,
          // The ground covered from just before each held beat to just before the next, and the press's own stride.
          held: ks.slice(1).map((k, i) => Math.hypot(plan.hero.x[k] - plan.hero.x[ks[i]], plan.hero.y[k] - plan.hero.y[ks[i]])),
          oneStride: 140 * (heldBeats[1] - heldBeats[0]),
          p95: ms[Math.floor(0.95 * (ms.length - 1))],
        };
      },
      { t0, mash, diag, hold, heldBeats },
    );
    const diagFirst = live.diag[0];
    const diagMoved = diagFirst
      ? await page.evaluate((s) => {
          const rec = (window as unknown as { __demo: PlayHook }).__demo.live.recording;
          const [a, b] = [0, 0.13].map((dw) => Math.floor((rec.timeWarp.toWorld(s) + dw) * 60 + 1e-6));
          return { dx: rec.plan.hero.x[b] - rec.plan.hero.x[a], dy: rec.plan.hero.y[b] - rec.plan.hero.y[a] };
        }, diagFirst.s)
      : { dx: 0, dy: 0 };
    record("Play: impacts on the beat", live.swings > 8 && live.offGrid === 0 && live.silent === 0, `${live.swings} swings in live play, ${live.offGrid} off the grid, ${live.silent} without an impact sound on the strike`);
    record(
      "Play: mashing rates one press per beat, and every press strides",
      live.mash.length === 3 && live.mash.filter((x) => x.extra).length === 2 && live.mash.every((x) => x.stride !== undefined),
      `three presses for one beat: ${live.mash.map((x) => `${x.extra ? "extra" : "rated"} (${x.stride?.toFixed(1) ?? "no"} px stride)`).join(", ")}`,
    );
    const diagLen = Math.hypot(diagMoved.dx, diagMoved.dy);
    record(
      "Play: Up and Right together stride diagonally, the stride's length unchanged",
      live.diag.length === 2 && live.diag[1].bend && Math.abs(diagLen - (diagFirst?.stride ?? -1)) < 1.5 && Math.abs(Math.abs(diagMoved.dx) - Math.abs(diagMoved.dy)) < 0.25 * diagLen,
      `the second key ${live.diag[1]?.bend ? "bent the stride into a diagonal" : "did not bend the stride"}: it moved ${diagMoved.dx.toFixed(1)}, ${diagMoved.dy.toFixed(1)} (${diagLen.toFixed(1)} px; the press's stride ${diagFirst?.stride?.toFixed(1) ?? "?"} px)`,
    );
    record(
      "Play: holding a key presses once and strides on every beat",
      live.hold === 1 && live.held.every((d) => Math.abs(d / live.oneStride - 1) < 0.1),
      `${live.hold} press recorded for a key held three beats (its repeats ignored); from beat to beat the hero covered ${live.held.map((d) => d.toFixed(1)).join(", ")} px (one stride is ${live.oneStride.toFixed(1)})`,
    );
    // Stomps: Space on every beat while holding Right keeps the hero still; Left then Right 25 ms
    // later is one stomp, a rock step out and back.
    const stompAt = beats.findIndex((b) => b > heldBeats[2] + 0.6);
    const stompBeats = [1, 2, 3, 4].map((k) => beats[stompAt + k]);
    await waitSong(page, beats[stompAt] - 0.15);
    await page.keyboard.down("ArrowRight");
    for (const b of stompBeats) {
      await waitSong(page, b - 0.01);
      await page.keyboard.press("Space");
    }
    await waitSong(page, stompBeats[3] + 0.1);
    await page.keyboard.up("ArrowRight");
    const rockAt = beats[stompAt + 6] + 0.02;
    await waitSong(page, rockAt);
    await page.keyboard.down("ArrowLeft");
    await page.waitForTimeout(25);
    await page.keyboard.down("ArrowRight");
    await page.keyboard.up("ArrowLeft");
    await page.keyboard.up("ArrowRight");
    await waitSong(page, rockAt + 0.4);
    const stomps = await page.evaluate(
      ({ stompBeats, rockAt }) => {
        const rec = (window as unknown as { __demo: PlayHook }).__demo.live.recording;
        const plan = rec.plan;
        const [a, b, c, d] = [stompBeats[0] - 0.05, stompBeats[3] + 0.3, rockAt - 0.01, rockAt + 0.3].map((s) => Math.floor(rec.timeWarp.toWorld(s) * 60 + 1e-6));
        const ins = plan.inputs.filter((i) => i.kind === "press" && i.s > stompBeats[0] - 0.2 && i.s < rockAt + 0.2);
        return {
          still: Math.hypot(plan.hero.x[b] - plan.hero.x[a], plan.hero.y[b] - plan.hero.y[a]),
          spaces: ins.filter((i) => i.key === "space" && i.stomp).length,
          rocked: Math.hypot(plan.hero.x[d] - plan.hero.x[c], plan.hero.y[d] - plan.hero.y[c]),
          pair: ins.filter((i) => i.s >= rockAt - 0.05).map((i) => `${i.key}${i.rock ? " (rock)" : ""}${i.rating ? ` ${i.rating}` : ""}`),
        };
      },
      { stompBeats, rockAt },
    );
    record(
      "Play: Space on every beat while holding a direction keeps the hero still",
      stomps.spaces === 4 && stomps.still < 0.5,
      `${stomps.spaces} stomps over four beats with Right held; the hero moved ${stomps.still.toFixed(2)} px`,
    );
    record(
      "Play: Left then Right together are one stomp, a rock step out and back",
      stomps.pair.length === 2 && stomps.pair[1].startsWith("right (rock)") && stomps.rocked < 0.5,
      `presses: ${stomps.pair.join(", ")}; the hero ended ${stomps.rocked.toFixed(2)} px from where it began`,
    );
    record("Play: frame rate and frame time", fps >= 55 && live.p95 < 8, `${fps.toFixed(1)} frames per second; JavaScript frame time (simulation, rollback and drawing) p95 ${live.p95.toFixed(2)} ms`);
    // Esc pauses (song time stops) and resumes after a count-in, exactly where it stopped.
    await page.keyboard.press("Escape");
    await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play.phase() === "paused");
    const pausedAt = await songNow(page);
    await page.waitForTimeout(600);
    const still = Math.abs((await songNow(page)) - pausedAt);
    await page.keyboard.press("Enter");
    await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play.phase() === "playing", undefined, { timeout: 10_000 });
    const resumed = await songNow(page);
    record("Play: Esc pauses and resumes", still < 1e-6 && resumed >= pausedAt - 1e-6 && resumed < pausedAt + 0.2, `paused at ${pausedAt.toFixed(2)} s (song time still for 0.6 s); resumed after a one-bar count-in at ${resumed.toFixed(2)} s`);
    // Losing focus pauses.
    await page.evaluate(() => window.dispatchEvent(new Event("blur")));
    record("Play: losing focus pauses", (await ph(page)) === "paused", `after a window blur the game is ${await ph(page)}`);
    record("Play: no page errors", errors.length === 0, errors.length ? errors.join("; ") : "none during live play");
    // The first assembled check: holding Right, tapping quarter notes, tapping eighth notes and
    // mashing all move the hero at the walk speed (140 px/s). Each pattern runs three seconds; the
    // speed is read over its last two, in world time (a hit-stop freezes the world), once the first
    // press's full budget is spent.
    await openPlay(page, "1:24");
    const sb = await page.evaluate(() => Array.from((window as unknown as { __demo: PlayHook }).__demo.live.recording.plan.songBeats));
    const windows: { name: string; a: number; b: number }[] = [];
    let at = (await songNow(page)) + 0.3;
    for (const name of ["hold", "quarters", "eighths", "mash"]) {
      const end = at + 3;
      const pattern = sb.filter((b) => b > at && b < end);
      await waitSong(page, pattern[0] - 0.01);
      if (name === "hold") {
        await page.keyboard.down("ArrowRight");
        while ((await songNow(page)) < end) {
          await page.waitForTimeout(40);
          await page.keyboard.down("ArrowRight");
        }
        await page.keyboard.up("ArrowRight");
      } else if (name === "mash") {
        while ((await songNow(page)) < end) {
          await page.keyboard.press("ArrowRight");
          await page.waitForTimeout(55);
        }
      } else {
        const times = name === "quarters" ? pattern : pattern.flatMap((b, i) => (i + 1 < pattern.length ? [b, (b + pattern[i + 1]) / 2] : [b]));
        for (const t of times) {
          await waitSong(page, t - 0.01);
          await page.keyboard.press("ArrowRight");
        }
      }
      windows.push({ name, a: pattern[0] + 1, b: end });
      at = end + 0.6;
      await waitSong(page, at);
    }
    const speeds = await page.evaluate((ws) => {
      const rec = (window as unknown as { __demo: PlayHook }).__demo.live.recording;
      const plan = rec.plan;
      return ws.map(({ name, a, b }) => {
        const [ka, kb] = [a, b].map((s) => Math.floor(rec.timeWarp.toWorld(s) * 60 + 1e-6));
        return { name, speed: Math.hypot(plan.hero.x[kb] - plan.hero.x[ka], plan.hero.y[kb] - plan.hero.y[ka]) / ((kb - ka) / 60) };
      });
    }, windows);
    record(
      "Play: holding, quarter notes, eighth notes and mashing all move at the walk speed",
      speeds.length === 4 && speeds.every((x) => Math.abs(x.speed / 140 - 1) < 0.15),
      `px per world second over two seconds of each: ${speeds.map((x) => `${x.name} ${x.speed.toFixed(0)}`).join(", ")} (the walk speed is 140)`,
    );
    // Watch my run after a level-up: from 1:37 (the moderate player has played up to there), the
    // moderate player's own keys go on through the bridge's level-up at 1:40.7 to the end, each
    // injected just before its time; then the replay is compared with the live run, offers and orbs
    // included.
    const LATE = 97;
    const botKeys = runPlanner({ level: "levels/orpheus-galop.play.yaml", profile: "moderate" })
      .inputs.filter((i) => i.s > LATE + 0.2)
      .map((i) => ({ s: i.s, kind: i.kind, key: i.key }));
    await openPlay(page, "1:37");
    for (const k of botKeys) {
      if ((await ph(page)) !== "playing") break;
      await waitSong(page, k.s - 0.03);
      await page.evaluate((e) => (window as unknown as { __demo: PlayHook }).__demo.live.key(e), k);
    }
    const ended = await page
      .waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play.menu()?.items.includes("Watch my run") ?? false, undefined, { timeout: 30_000 })
      .then(() => true)
      .catch(() => false);
    if (!ended) {
      const menu = await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.play.menu()?.items ?? []);
      record("Play: Watch my run replays a run with a level-up identically", false, `the run from 1:37 never reached the end card (phase ${await ph(page)}, menu ${menu.join(", ") || "none"})`);
    } else {
      const ups = await page.evaluate((late) => (window as unknown as { __demo: PlayHook }).__demo.live.recording.plan.upgrades.filter((u) => u.s > late).map((u) => ({ s: u.s, offers: u.offers.length, taken: u.taken })), LATE);
      const differ = await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.play.compareReplay());
      await page.locator("canvas").screenshot({ path: join(OUT, "play-end-card.png") });
      record(
        "Play: Watch my run replays a run with a level-up identically",
        ups.length > 0 && ups.every((u) => u.offers > 0 && u.taken >= 0) && differ.length === 0,
        `${ups.map((u) => `level-up at ${u.s.toFixed(2)} s: ${u.offers} offers, took #${u.taken}`).join("; ") || "no level-up in the run"}; ${differ.length ? `the replay differs in: ${differ.join(", ")}` : "the replay's recording equals the live run's, part by part"}`,
      );
      // Chosen at once (the live run's music-follow curve still scheduled), the replay plays.
      const before = errors.length;
      await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.play.choose("Watch my run"));
      await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play.phase() === "replay", undefined, { timeout: 30_000 });
      const r0 = await songNow(page);
      await page.waitForTimeout(1500);
      const r1 = await songNow(page);
      const replayErrors = errors.slice(before);
      record("Play: Watch my run chosen at once plays the replay", replayErrors.length === 0 && r1 > r0 + 1, `song time ${r0.toFixed(2)} → ${r1.toFixed(2)} s over 1.5 s; ${replayErrors.length ? replayErrors.join("; ") : "no page errors"}`);
    }
    // A mid-song start: the bot played up to the climax, and the field is busy.
    await openPlay(page, "1:50");
    await page.waitForTimeout(800);
    await page.locator("canvas").screenshot({ path: join(OUT, "play-start-1-50.png") });
    const alive = await page.evaluate(() => (window as unknown as { __demo: { stats(): { alive: number } } }).__demo.stats().alive);
    record("Play: ?start=1:50 starts mid-song in a believable world", alive > 100, `${alive} monsters on screen just after the hand-over (capture: play-start-1-50.png)`);
    await ctx.close();
  }

  // -------------------------------------------- captures for the human review
  if (!onlyPlay) {
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
