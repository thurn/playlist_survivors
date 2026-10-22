import { expect, test, type Page } from "@playwright/test";
import { devPort } from "../../scripts/lib/port.ts";

const BASE = `http://localhost:${devPort()}`;

interface PlayHook {
  ready: Promise<void>;
  lastS: number;
  play: { phase(): string; menu(): { items: string[]; selected: number } | undefined };
  live: {
    runner: { applied: { s: number; kind: string; key: string }[] };
    recording: {
      refresh(): void;
      timeWarp: { toWorld(s: number): number };
      plan: { songBeats: Float64Array; hero: { x: Float32Array; y: Float32Array }; inputs: { s: number; kind: string; extra?: boolean; bend?: boolean; steps: boolean; rating?: string }[]; swings: { s: number; press?: number }[] };
    };
  };
}

const hook = (page: Page) => page.evaluate.bind(page);

async function openPlay(page: Page, query: string) {
  await page.goto(`${BASE}/?mode=play${query}&test`);
  await page.waitForFunction(() => (window as unknown as { __demo?: unknown }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: PlayHook }).__demo.ready);
  await page.getByText("Click to begin").click();
  await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play?.phase() === "playing", undefined, { timeout: 30_000 });
}

/** Song time now, as the page draws it. */
const songNow = (page: Page) => hook(page)(() => (window as unknown as { __demo: PlayHook }).__demo.lastS);

/** Wait until the page's song time reaches s (in small real-time steps). */
async function waitSong(page: Page, s: number) {
  for (;;) {
    const now = await songNow(page);
    if (now >= s) return;
    await page.waitForTimeout(Math.min(200, Math.max(2, (s - now) * 1000 - 4)));
  }
}

test.describe.configure({ mode: "serial" });

test("play mode: a step per press, swings on the beat, mashing rated once, Esc pauses and resumes", async ({ page }) => {
  await openPlay(page, "&start=1:10");
  const beats = await hook(page)(() => Array.from((window as unknown as { __demo: PlayHook }).__demo.live.recording.plan.songBeats));
  const from = (await songNow(page)) + 0.3;
  const onBeat = beats.filter((b) => b > from).slice(0, 8);
  // Arrow presses on the beat for four bars (2/4), alternating right and left.
  for (const [i, b] of onBeat.entries()) {
    await waitSong(page, b - 0.01);
    await page.keyboard.press(i % 2 ? "ArrowLeft" : "ArrowRight");
  }
  // Then a mashed beat: three presses within a few milliseconds.
  const mash = beats.find((b) => b > onBeat[onBeat.length - 1] + 0.2)!;
  await waitSong(page, mash - 0.01);
  for (let k = 0; k < 3; k++) await page.keyboard.press("ArrowUp");
  await waitSong(page, mash + 0.8);

  const r = await hook(page)((t0: number) => {
    const live = (window as unknown as { __demo: PlayHook }).__demo.live;
    const rec = live.recording;
    rec.refresh();
    const plan = rec.plan;
    // (In world time: a cleave's hit-stop freezes the world, steps included.)
    const heroAt = (s: number, dw = 0) => {
      const k = Math.round((rec.timeWarp.toWorld(s) + dw) * 60);
      return { x: plan.hero.x[k], y: plan.hero.y[k] };
    };
    const presses = plan.inputs.filter((i) => i.kind === "press" && i.s >= t0 && i.steps);
    // Each rated press moved the hero one step (44 px). (Measured where the step before had
    // landed: under load, presses can bunch up, and a step then starts from a moving hero.)
    const stepped = presses.filter((i) => !i.extra && !i.bend);
    const steps = stepped.filter((p, i) => i === 0 || p.s - stepped[i - 1].s >= 0.15).map((p) => {
      const a = heroAt(p.s, -0.005);
      const b = heroAt(p.s, 0.13);
      return Math.hypot(b.x - a.x, b.y - a.y);
    });
    return { stepped: stepped.length, steps, extra: presses.filter((i) => i.extra).length, armed: plan.swings.filter((sw) => sw.s >= t0 && sw.press !== undefined).length };
  }, from);
  expect(r.stepped).toBe(9);
  expect(r.steps.length).toBeGreaterThanOrEqual(6);
  for (const d of r.steps) expect(d).toBeCloseTo(44, 0);
  expect(r.extra).toBe(2);
  expect(r.armed).toBeGreaterThanOrEqual(8);

  // Esc pauses: song time stops.
  await page.keyboard.press("Escape");
  await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play.phase() === "paused");
  const pausedAt = await songNow(page);
  await page.waitForTimeout(500);
  expect(await songNow(page)).toBeCloseTo(pausedAt, 6);
  expect((await hook(page)(() => (window as unknown as { __demo: PlayHook }).__demo.play.menu()))?.items[0]).toBe("Resume");
  // Enter resumes after a one-bar count-in; the game picks up exactly where it stopped.
  await page.keyboard.press("Enter");
  await page.waitForFunction(() => (window as unknown as { __demo: PlayHook }).__demo.play.phase() === "playing", undefined, { timeout: 10_000 });
  const resumed = await songNow(page);
  expect(resumed).toBeGreaterThanOrEqual(pausedAt - 1e-6);
  expect(resumed).toBeLessThan(pausedAt + 0.2);
});
