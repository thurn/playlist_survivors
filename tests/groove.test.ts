/**
 * Press classes and the groove meter (play mode): "Timing and the Groove" in
 * docs/design/gameplay-redesign.md, as pure functions and through the
 * simulation on the synthetic song (theme A at 185 beats per minute).
 */
import { describe, expect, it } from "vitest";
import { grooveAt } from "../src/plan/evaluate.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder, type PressTrace } from "../src/sim/recorder.ts";
import { classifyPress, freshPressMemo, grooveAfterChange, tierFor } from "../src/sim/timing.ts";
import { play, tap } from "./fixtures/play-sim.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const { ctx, sch, section, run } = play(syntheticSongMap());
const hero = ctx.lib.hero;

describe("press classes", () => {
  it("class the design's six presses at 185 beats per minute, with their groove changes", () => {
    // Beats at 10.000 and 10.325; the off-beat between them at 10.1625.
    const grid = [9.675, 10.0, 10.325, 10.65];
    const memo = freshPressMemo();
    const press = (s: number, partner = false) => classifyPress(hero, grid, s, partner, memo);
    const got = [press(9.992), press(10.02, true), press(10.07), press(10.12), press(10.15), press(10.4)];
    expect(got.map((j) => [j.class, j.rating])).toEqual([
      ["counted", "perfect"],
      ["partner", undefined],
      ["double", undefined],
      ["offbeat", "perfect"],
      ["stray", undefined],
      ["counted", "great"],
    ]);
    expect(got.map((j) => j.change)).toEqual([0.08, 0, 0, 0.03, -0.1, 0.02]);
  });

  it("rates counted presses Perfect, Great, Good and loose, and lets only a Great or better one have an off-beat", () => {
    const grid = [0, 0.325, 0.65, 0.975];
    const rate = (s: number) => classifyPress(hero, grid, s, false, freshPressMemo());
    expect([0.04, 0.08, 0.13, 0.15].map((s) => rate(0.325 + s).rating)).toEqual(["perfect", "great", "good", "loose"]);
    // A Good counted press (120 ms early): a press near its off-beat is stray, not an off-beat.
    const memo = freshPressMemo();
    classifyPress(hero, grid, 0.325 - 0.12, false, memo);
    expect(classifyPress(hero, grid, 0.4875, false, memo).class).toBe("stray");
  });
});

describe("tiers", () => {
  it("rise at 0.30, 0.60 and 0.85, and drop only 0.05 below a threshold", () => {
    expect(tierFor(hero, 0, 0.29)).toBe(0);
    expect(tierFor(hero, 0, 0.3)).toBe(1);
    expect(tierFor(hero, 0, 0.9)).toBe(3);
    expect(tierFor(hero, 2, 0.57)).toBe(2);
    expect(tierFor(hero, 2, 0.549)).toBe(1);
    expect(tierFor(hero, 3, 0.81)).toBe(3);
    expect(tierFor(hero, 3, 0.79)).toBe(2);
    // Hovering about 0.6, the tier does not flicker.
    let t = 0;
    const seen: number[] = [];
    for (const g of [0.62, 0.58, 0.61, 0.57, 0.6, 0.56, 0.59]) seen.push((t = tierFor(hero, t, g)));
    expect(new Set(seen)).toEqual(new Set([2]));
  });

  it("climb from 0 with a Perfect press on every beat: tier 1 after 4 beats, 2 after 8 and 3 after 11", () => {
    let g = 0;
    let t = 0;
    const after: number[] = [];
    for (let k = 1; k <= 12; k++) {
      g = grooveAfterChange(g, hero.meter.counted.perfect);
      t = tierFor(hero, t, g);
      after.push(t);
    }
    expect(after.indexOf(1) + 1).toBe(4);
    expect(after.indexOf(2) + 1).toBe(8);
    expect(after.indexOf(3) + 1).toBe(11);
  });
});

describe("the groove in play", () => {
  const sec = section("theme-a");
  const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 0.2);
  const bar = 2 * (beats[1] - beats[0]);

  it("fills with Perfect presses on the beat, steps the tier up within about ten beats, and drains a bar after the last raising press", () => {
    const rec = new BatchRecorder();
    const presses = beats.slice(0, 14);
    const last = presses[presses.length - 1];
    run(presses.flatMap((b) => tap(b)), [last + 11 * bar], rec);
    const groove = (s: number) => grooveAt({ s: rec.grooveS, v: rec.grooveV }, s);
    const tierAt = (s: number) => rec.tierV[rec.tierS.findLastIndex((x) => x <= s + 1e-9)];
    expect(tierAt(presses[3] + 0.01)).toBe(1);
    expect(tierAt(presses[7] + 0.01)).toBe(2);
    expect(tierAt(presses[10] + 0.01)).toBe(3);
    expect(groove(last + 0.01)).toBe(1);
    // Full for a bar, then an eighth per bar: half after four more, empty after eight.
    expect(groove(last + bar + 0.01)).toBe(1);
    expect(groove(last + 5 * bar + 0.01)).toBeCloseTo(0.5, 6);
    expect(groove(last + 9 * bar + 0.01)).toBeCloseTo(0, 6);
    // Each tier change sounds its chime on the next grid time.
    for (const c of rec.tierChimes) expect(Math.min(...Array.from(sch.gridTimes, (g) => Math.abs(g - c.s)))).toBeLessThan(1e-9);
    expect(rec.tierChimes.filter((c) => c.up).length).toBe(3);
  });

  it("falls with mashing: stray presses take a chunk off", () => {
    const rec = new BatchRecorder();
    const presses = beats.slice(0, 10);
    // On each beat a counted press, an off-beat at +120 ms, and two strays just after it.
    const mash = beats.slice(10, 14).flatMap((b) => [b, b + 0.12, b + 0.13, b + 0.14]);
    const keys: KeyEvent[] = [...presses, ...mash].flatMap((s) => tap(s));
    const r = run(keys, [beats[15]], rec);
    const strays = r.inputs.filter((i) => i.class === "stray");
    expect(strays.length).toBe(8);
    const g = (s: number) => grooveAt({ s: rec.grooveS, v: rec.grooveV }, s);
    expect(g(beats[14])).toBeLessThan(g(beats[9] + 0.01));
  });

  it("stomps with Space on the beat raise it like any press", () => {
    const rec = new BatchRecorder();
    const r = run(beats.slice(0, 6).flatMap((b) => tap(b, "space")), [beats[7]], rec);
    expect(r.inputs.every((i) => i.class === "counted" && i.stomp)).toBe(true);
    expect(grooveAt({ s: rec.grooveS, v: rec.grooveV }, beats[5] + 0.01)).toBeCloseTo(0.48, 6);
  });

  it("traces an off-beat press against its nearest grid time, with its offset from the off-beat apart", () => {
    const traces: PressTrace[] = [];
    const rec = new BatchRecorder();
    Object.assign(rec, { press: (t: PressTrace) => traces.push(t) });
    const half = (beats[0] + beats[1]) / 2;
    run([...tap(beats[0]), ...tap(half + 0.03)], [beats[2]], rec);
    const off = traces.find((t) => t.kind === "press" && t.class === "offbeat")!;
    expect(off.offbeatDelta).toBeCloseTo(0.03, 9);
    expect(off.gridS).toBeCloseTo(beats[1], 9);
    expect(off.delta).toBeCloseTo(half + 0.03 - beats[1], 9);
    expect(off.tier).toBe(0);
  });

  it("is neither rated nor changed in a free section", () => {
    const rec = new BatchRecorder();
    const r = run([3, 4, 5, 6].flatMap((s) => tap(s)), [8], rec);
    expect(r.inputs.every((i) => i.class === undefined && i.rating === undefined)).toBe(true);
    expect(rec.grooveS).toEqual([0]);
  });
});
