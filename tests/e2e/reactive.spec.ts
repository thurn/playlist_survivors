import { expect, test, type Page } from "@playwright/test";
import { devPort } from "../../scripts/lib/port.ts";

const BASE = `http://localhost:${devPort()}`;

interface ReactiveHook {
  ready: Promise<void>;
  lastS: number;
  play: { phase(): string; compareReplay(): string[] };
  live: {
    recording: {
      refresh(): void;
      plan: {
        songBeats: Float64Array;
        title: { piece: string };
        inputs: { s: number; kind: string; class?: string; unjudged?: boolean; steps: boolean }[];
        swings: { s: number }[];
        events: { sounds: { s: number; key?: string }[] };
      };
    };
  };
}

const songNow = (page: Page) => page.evaluate(() => (window as unknown as { __demo: ReactiveHook }).__demo.lastS);

async function waitSong(page: Page, s: number) {
  for (;;) {
    const now = await songNow(page);
    if (now >= s) return;
    await page.waitForTimeout(Math.min(200, Math.max(2, (s - now) * 1000 - 4)));
  }
}

test("reactive mode plays the Galop from its stream: strikes and sounds on committed beats, judged presses, an exact replay", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  // From the galop's opening, where the beat is clear (the prelude's presses are unjudged).
  await page.goto(`${BASE}/?mode=reactive&track=orpheus-galop&start=55&test`);
  await page.waitForFunction(() => (window as unknown as { __demo?: unknown }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: ReactiveHook }).__demo.ready);
  await page.getByText("Click to begin").click();
  await page.waitForFunction(() => (window as unknown as { __demo: ReactiveHook }).__demo.play?.phase() === "playing", undefined, { timeout: 30_000 });

  const beats = await page.evaluate(() => Array.from((window as unknown as { __demo: ReactiveHook }).__demo.live.recording.plan.songBeats));
  const from = (await songNow(page)) + 0.3;
  const onBeat = beats.filter((b) => b > from).slice(0, 8);
  for (const [i, b] of onBeat.entries()) {
    await waitSong(page, b - 0.01);
    await page.keyboard.press(i % 2 ? "ArrowLeft" : "ArrowRight");
  }
  await waitSong(page, onBeat[onBeat.length - 1] + 0.8);

  const r = await page.evaluate((t0: number) => {
    const d = (window as unknown as { __demo: ReactiveHook }).__demo;
    const rec = d.live.recording;
    rec.refresh();
    const plan = rec.plan;
    const presses = plan.inputs.filter((i) => i.kind === "press" && i.s >= t0);
    const swings = plan.swings.filter((sw) => sw.s >= t0);
    return {
      title: plan.title.piece,
      presses: presses.length,
      judged: presses.filter((i) => i.class !== undefined && !i.unjudged).length,
      swings: swings.length,
      onBeats: swings.filter((sw) => Array.from(plan.songBeats).some((b) => Math.abs(b - sw.s) < 1e-4)).length,
      sounds: plan.events.sounds.filter((x) => x.s >= t0).length,
      replay: d.play.compareReplay(),
    };
  }, from);
  expect(errors).toEqual([]);
  expect(r.title).toBe("Orpheus in the Underworld: Galop");
  expect(r.presses).toBe(8);
  expect(r.judged).toBeGreaterThan(0);
  // The sword strikes on the committed beats, which are the reactive song map's beats.
  expect(r.swings).toBeGreaterThan(4);
  expect(r.onBeats).toBe(r.swings);
  expect(r.sounds).toBeGreaterThan(0);
  // Watch my run replays the recorded keys through the engine: the same recording.
  expect(r.replay).toEqual([]);
});

test("Watch the demo in reactive mode plans the track with reactive-skilled and renders it", async ({ page }) => {
  const errors: string[] = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto(`${BASE}/?mode=reactive&track=orpheus-galop&player=reactive-skilled&test`);
  await page.waitForFunction(() => (window as unknown as { __demo?: unknown }).__demo !== undefined, undefined, { timeout: 120_000 });
  await page.evaluate(() => (window as unknown as { __demo: { ready: Promise<void> } }).__demo.ready);
  const r = await page.evaluate(async () => {
    const d = (window as unknown as { __demo: { plan: { profile: string; stats: { averageGroove: number; deathS?: number }; reactiveFx?: unknown }; renderAt(s: number): Promise<void> } }).__demo;
    await d.renderAt(60);
    return { profile: d.plan.profile, reactive: d.plan.reactiveFx !== undefined, groove: d.plan.stats.averageGroove, fell: d.plan.stats.deathS !== undefined };
  });
  expect(errors).toEqual([]);
  expect(r).toMatchObject({ profile: "reactive-skilled", reactive: true, fell: false });
  expect(r.groove).toBeGreaterThan(0.9);
});
