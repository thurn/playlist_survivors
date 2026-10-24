/**
 * Press classes and the groove meter (play mode): "Timing and the Groove" in
 * docs/design/gameplay-redesign.md, as pure functions and through the
 * simulation on the synthetic song (theme A at 185 beats per minute).
 */
import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { grooveAt } from "../src/plan/evaluate.ts";
import { checkPlan, summarizeChecks } from "../src/planner/checks.ts";
import { checkCurve, checkGrooveAverages, checkPlay } from "../src/planner/play-checks.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder, type PressTrace } from "../src/sim/recorder.ts";
import { heroDef } from "../src/content/schema.ts";
import { classifyPress, driftOf, freshPressMemo, grooveAfterChange, tierFor } from "../src/sim/timing.ts";
import { play, tap } from "./fixtures/play-sim.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const { ctx, sch, section, run } = play(syntheticSongMap());
const hero = ctx.lib.hero;
const m = hero.meter;

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
    expect(got.map((j) => j.change)).toEqual([m.counted.perfect, 0, m.doubleTap, m.offbeat.perfect, m.stray, m.counted.great]);
  });

  it("rates counted presses Perfect, Great, Good and loose, and lets a Good or better one have an off-beat", () => {
    const grid = [0, 0.325, 0.65, 0.975];
    const rate = (s: number) => classifyPress(hero, grid, s, false, freshPressMemo());
    expect([0.04, 0.08, 0.13, 0.15].map((s) => rate(0.325 + s).rating)).toEqual(["perfect", "great", "good", "loose"]);
    // A Good counted press (120 ms early): a press near its off-beat is an off-beat, not stray.
    const good = freshPressMemo();
    classifyPress(hero, grid, 0.325 - 0.12, false, good);
    expect(classifyPress(hero, grid, 0.4875, false, good).class).toBe("offbeat");
    // A loose one (150 ms early): the press near its off-beat is stray.
    const loose = freshPressMemo();
    classifyPress(hero, grid, 0.325 - 0.15, false, loose);
    expect(classifyPress(hero, grid, 0.4875 - 0.03, false, loose).class).toBe("stray");
  });

  it("costs a Great off-beat less than a Good counted press, so eighth notes stay near the beat-only groove", () => {
    expect(m.offbeat.perfect).toBe(0);
    expect(m.offbeat.great).toBeLessThan(0);
    expect(m.offbeat.great).toBeGreaterThan(m.counted.good);
  });
});

describe("the drift", () => {
  // Beats every 0.3 s: a player 100 ms late on every press.
  const grid = Array.from({ length: 40 }, (_, i) => i * 0.3);
  const late = (n: number, group: "direction" | "stomp" = "direction", memo = freshPressMemo()): { memo: ReturnType<typeof freshPressMemo>; got: ReturnType<typeof classifyPress>[] } => ({ memo, got: Array.from({ length: n }, (_, i) => classifyPress(hero, grid, 0.3 * (i + 1) + 0.1, false, memo, group)) });

  it("judges a steadily late player against their own beat, after the first few presses", () => {
    const { got } = late(12);
    const d = m.drift;
    // Until there are enough presses, no drift: 100 ms late is Good.
    expect(got.slice(0, d.minPresses).map((j) => [j.rating, j.drift])).toEqual(Array.from({ length: d.minPresses }, () => ["good", 0]));
    // Then the drift (less its dead zone, capped) takes most of it off: 100 ms late is Perfect.
    for (const j of got.slice(d.minPresses)) {
      expect(j.drift).toBeCloseTo(Math.min(0.1 - d.deadMs / 1000, d.maxMs / 1000), 9);
      expect(j.rating).toBe("perfect");
    }
  });

  it("leaves a player's scatter alone: a median within the dead zone is no drift", () => {
    const memo = freshPressMemo();
    const offsets = [0.03, -0.02, 0.015, -0.03, 0.01, 0.018, -0.01, 0.019];
    offsets.forEach((o, i) => classifyPress(hero, grid, 0.3 * (i + 1) + o, false, memo));
    expect(driftOf(hero, memo, "direction")).toBe(0);
  });

  it("keeps a player falling behind a fast beat on the beat they were late for, where a slip would take the next one", () => {
    // A 0.24 s grid, the player 40 ms late and 10 ms later each beat: from 130 ms late a press is
    // nearer the next beat, which without the drift it would take (and the next press be stray).
    const fast = Array.from({ length: 40 }, (_, i) => i * 0.24);
    const lateBy = (i: number) => 0.04 + 0.01 * i;
    const judge = (memo: ReturnType<typeof freshPressMemo>, drift: boolean) =>
      Array.from({ length: 14 }, (_, i) => classifyPress(drift ? hero : { ...hero, meter: { ...m, drift: { ...m.drift, maxMs: 0 } } }, fast, 0.24 * (i + 1) + lateBy(i), false, memo));
    const without = judge(freshPressMemo(), false);
    expect(without.slice(9).some((j) => j.class !== "counted" || j.rating === "loose")).toBe(true);
    const got = judge(freshPressMemo(), true);
    for (const [i, j] of got.entries()) expect([i, j.class, j.gridIndex]).toEqual([i, "counted", i + 1]);
    expect(got.slice(9).map((j) => j.rating)).not.toContain("loose");
  });

  it("is each key group's own: the stomp key's lateness leaves the directions alone", () => {
    const { memo } = late(8, "stomp");
    expect(driftOf(hero, memo, "stomp")).toBeGreaterThan(0);
    expect(driftOf(hero, memo, "direction")).toBe(0);
    expect(classifyPress(hero, grid, 0.3 * 20 + 0.1, false, memo, "direction").rating).toBe("good");
  });

  it("can't be set to need more presses than it keeps", () => {
    expect(heroDef.shape.meter.safeParse({ ...m, drift: { ...m.drift, presses: 6, minPresses: 8 } }).success).toBe(false);
    expect(heroDef.shape.meter.safeParse(m).success).toBe(true);
  });

  it("is learned from earlier presses only, and leaves out loose ones", () => {
    const memo = freshPressMemo();
    for (let i = 1; i <= 8; i++) classifyPress(hero, grid, 0.3 * i + 0.15, false, memo);
    expect(memo.recent.direction).toEqual([]);
    expect(driftOf(hero, memo, "direction")).toBe(0);
  });
});

describe("a pulsed section's first grid time", () => {
  it("counts a press just before it, so eighth notes from there have no loose presses and no strays", () => {
    // Perfect presses on every eighth note from 12 ms before the galop vamp's first grid time: the
    // first press still lands in the prelude, a free section.
    const vamp = section("galop-vamp");
    const grid = Array.from(sch.gridTimes).filter((g) => g >= vamp.s0 - 1e-6).slice(0, 9);
    expect(grid[0] - 0.012).toBeLessThan(vamp.s0);
    const times = grid.slice(0, -1).flatMap((g, i) => [g - 0.012, (g + grid[i + 1]) / 2 - 0.012]);
    const r = run(times.flatMap((s) => tap(s)), [grid[8] + 0.05]);
    const presses = r.inputs.filter((i) => i.s < grid[8]);
    expect(presses.length).toBe(16);
    expect(presses[0]).toMatchObject({ steps: true, class: "counted", rating: "perfect" });
    expect(presses.filter((i) => i.class === "counted").map((i) => i.rating)).toEqual(Array(8).fill("perfect"));
    expect(presses.filter((i) => i.class === "offbeat").length).toBe(8);
    expect(presses.filter((i) => i.class === "stray" || (i.class === "counted" && !i.rating))).toEqual([]);
  });

  it("passes every check on the eighths player's run, whose first vamp press comes just before it", () => {
    const songMap = syntheticSongMap();
    const level = "levels/orpheus-galop.play.yaml";
    const plan = runPlanner({ songMap, level, profile: "eighths" });
    const early = plan.inputs.filter((i) => i.kind === "press" && i.steps && plan.sections.findLast((x) => i.s >= x.s0 - 1e-9)!.movement === "free");
    expect(early.map((i) => [i.class, i.rating])).toEqual([["counted", "perfect"]]);
    const lib = plannerInput({ songMap, level }).content;
    const { ok, text } = summarizeChecks([...checkPlan(plan, lib), ...checkPlay(plan, lib, { signature: false })]);
    expect(ok, text).toBe(true);
  }, 60_000);

  it("leaves a press earlier than the Good window in the free section, unclassed", () => {
    const vamp = section("galop-vamp");
    const first = Array.from(sch.gridTimes).find((g) => g >= vamp.s0 - 1e-6)!;
    const r = run(tap(first - (hero.timing.goodMs + 30) / 1000), [first + 0.05]);
    expect(r.inputs[0]).toMatchObject({ steps: false });
    expect(r.inputs[0].class).toBeUndefined();
  });
});

describe("tiers", () => {
  it("rise at their thresholds (0.45, 0.60 and 0.85), and drop only 0.05 below one", () => {
    const [t1, t2, t3] = m.tiers;
    expect([m.tiers, m.hysteresis]).toEqual([[0.45, 0.6, 0.85], 0.05]);
    expect(tierFor(hero, 0, t1 - 0.01)).toBe(0);
    expect(tierFor(hero, 0, t1)).toBe(1);
    expect(tierFor(hero, 0, 0.9)).toBe(3);
    expect(tierFor(hero, 2, t2 - 0.03)).toBe(2);
    expect(tierFor(hero, 2, t2 - 0.051)).toBe(1);
    expect(tierFor(hero, 3, t3 - 0.04)).toBe(3);
    expect(tierFor(hero, 3, t3 - 0.06)).toBe(2);
    // Hovering about 0.6, the tier does not flicker.
    let t = 0;
    const seen: number[] = [];
    for (const g of [0.62, 0.58, 0.61, 0.57, 0.6, 0.56, 0.59]) seen.push((t = tierFor(hero, t, g)));
    expect(new Set(seen)).toEqual(new Set([2]));
  });

  it("climb from 0 with a Perfect press on every beat: tier 1 after 6 beats, 2 after 8 and 3 after 11", () => {
    let g = 0;
    let t = 0;
    const after: number[] = [];
    for (let k = 1; k <= 12; k++) {
      g = grooveAfterChange(g, hero.meter.counted.perfect);
      t = tierFor(hero, t, g);
      after.push(t);
    }
    expect(after.indexOf(1) + 1).toBe(6);
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
    expect(tierAt(presses[5] + 0.01)).toBe(1);
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

describe("the checks judged on averages", () => {
  it("judges the human, late, holder and masher players on their average share over the seeds, not one seed's", () => {
    const failing = (shares: Record<string, number[]>) =>
      checkGrooveAverages(new Map(Object.entries(shares)))
        .filter((r) => r.failures.length > 0)
        .map((r) => r.name);
    // One poor seed among good ones passes; a poor average fails, whatever the best seed.
    expect(failing({ human: [0.3, 0.7, 0.8], late: [0.2, 0.6, 0.7], mash4: [0.7, 0.95, 1] })).toEqual([]);
    expect(failing({ human: [0.9, 0.3, 0.2], late: [0.45, 0.3, 0.3], holder: [0.9, 0.6, 0.7], moderate: [0] })).toEqual([
      "groove: the human player is at tier 2 or higher for at least 50% of pulsed time, on average",
      "groove: the late player is at tier 2 or higher for at least 40% of pulsed time, on average",
      "groove: the holder player is at tier 0 for at least 80% of pulsed time, on average",
    ]);
  });

  it("judges when the holder and mashers fall on their average time alive, a survived run counting as the song's end", () => {
    const run = (player: string, alive: number[]) => alive.map((a, seed) => ({ player, seed, alive: a === 0 ? 144.6 : a, fell: a !== 0 }));
    const failing = (runs: ReturnType<typeof run>) =>
      checkCurve(runs)
        .filter((r) => r.failures.length > 0)
        .map((r) => r.name.replace(/ falls? between .*/, ""));
    // Master's spread: one early fall and one survival among the 8-a-second masher's runs pass.
    const master = [...run("holder", [111, 109, 119, 120]), ...run("mash2", [95, 100, 113, 110]), ...run("mash4", [114, 115, 91, 120]), ...run("mash8", [113, 0, 127, 122])];
    expect(failing(master)).toEqual([]);
    // An immortal 8-a-second masher fails its own band, though the four's average stays within 1:45-2:05.
    expect(failing([...master.filter((r) => r.player !== "mash8"), ...run("mash8", [0, 0, 0, 0])])).toEqual(["the curve: the mash8 player"]);
    // Everyone a little sturdier: each within its own band, the four together past 2:05.
    expect(failing(master.map((r) => ({ ...r, alive: Math.min(144.6, r.alive + 14) })))).toEqual(["the curve: the holder and mashers"]);
    // No runs: the curve is never passed unchecked.
    expect(failing([])).toEqual(["the curve: the holder and mashers"]);
  });
});
