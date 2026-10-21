import { expect, test, type Page } from "@playwright/test";
import sharp from "sharp";

const BASE = "http://localhost:5199";

interface Hook {
  ready: Promise<void>;
  renderAt(s: number): Promise<void>;
  plan: { songBeats: Float64Array; barStart: number[]; duration: number };
  planHash: string;
  planSource: string;
}

async function openDemo(page: Page, query: string) {
  await page.goto(`${BASE}/?test${query}`);
  await page.waitForFunction(() => (window as unknown as { __demo?: unknown }).__demo !== undefined, undefined, { timeout: 60_000 });
  await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.ready);
  await page.evaluate(() => document.querySelectorAll("body > div:not(#app)").forEach((d) => d.remove()));
}

async function brightness(page: Page): Promise<number> {
  const png = await page.locator("canvas").screenshot();
  const { data, info } = await sharp(png).resize(160, 90).raw().toBuffer({ resolveWithObject: true });
  let sum = 0;
  for (let i = 0; i < data.length; i += info.channels) sum += (data[i] + data[i + 1] + data[i + 2]) / 3;
  return sum / (data.length / info.channels);
}

test.describe.configure({ mode: "serial" });

test("renders any song time through the capture hook", async ({ page }) => {
  await openDemo(page, "");
  for (const s of [5, 60, 120]) {
    await page.evaluate((t) => (window as unknown as { __demo: Hook }).__demo.renderAt(t), s);
    const png = await page.locator("canvas").screenshot();
    const { data } = await sharp(png).resize(64, 36).raw().toBuffer({ resolveWithObject: true });
    const values = new Set<number>();
    for (let i = 0; i < data.length; i += 3) values.add(data[i] >> 4);
    expect(values.size, `frame at ${s} s looks blank`).toBeGreaterThan(4);
  }
});

test("the metronome flash is locked to the beat", async ({ page }) => {
  await openDemo(page, "&metronome");
  const beats = await page.evaluate(() => {
    const p = (window as unknown as { __demo: Hook }).__demo.plan;
    return Array.from(p.songBeats);
  });
  // Galop beats, where the flash should fire on the beat and fade before the next.
  for (const i of [120, 200, 300]) {
    const on = beats[i];
    const off = beats[i] + 0.55 * (beats[i + 1] - beats[i]);
    await page.evaluate((t) => (window as unknown as { __demo: Hook }).__demo.renderAt(t), on);
    const bOn = await brightness(page);
    await page.evaluate((t) => (window as unknown as { __demo: Hook }).__demo.renderAt(t), off);
    const bOff = await brightness(page);
    expect(bOn, `beat ${i}: on-beat ${bOn.toFixed(1)} vs off-beat ${bOff.toFixed(1)}`).toBeGreaterThan(bOff + 25);
  }
});

test("a broken level script shows a clear error and never starts", async ({ page }) => {
  await page.goto(`${BASE}/?level=bar-range-outside-song`);
  const err = page.locator("pre");
  await expect(err).toBeVisible({ timeout: 30_000 });
  await expect(err).toContainText("bar range 1-999 is outside the song");
  await expect(err).toContainText("bar-range-outside-song.level.yaml:");
  await expect(page.getByText("Click to begin")).toBeHidden();
  await expect(page.locator("canvas")).toHaveCount(0);
});

test("a reload uses the cached plan and produces the same plan", async ({ page }) => {
  await openDemo(page, "");
  const first = await page.evaluate(() => (window as unknown as { __demo: Hook }).__demo.planHash);
  await page.reload();
  await page.waitForFunction(() => (window as unknown as { __demo?: Hook }).__demo?.planHash !== undefined, undefined, { timeout: 60_000 });
  const second = await page.evaluate(() => ({ hash: (window as unknown as { __demo: Hook }).__demo.planHash, source: (window as unknown as { __demo: Hook }).__demo.planSource }));
  expect(second.source).toBe("cache");
  expect(second.hash).toBe(first);
});
