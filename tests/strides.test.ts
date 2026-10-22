/**
 * Fixed-speed strides (play mode): the numbers in "Movement: Fixed-Speed
 * Strides" of docs/design/gameplay-redesign.md, through the simulation on the
 * synthetic song (theme A at 185 beats per minute) and on a copy of it with a
 * slow climax.
 */
import { describe, expect, it } from "vitest";
import { cleaves } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { heldDirection, periodAt, strideSteps } from "../src/sim/strides.ts";
import { play, slowSection, tap, travel } from "./fixtures/play-sim.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

describe("stride steps", () => {
  it("are the grid times, with each step longer than the longest period halved until it is not", () => {
    const st = strideSteps([0, 0.3, 1.0, 1.769], 0.4);
    // 0.3 stays; 0.7 halves to 0.35; 0.769 halves to 0.3845; the last keeps the step before it.
    expect(Array.from(st.s).map((x) => +x.toFixed(6))).toEqual([0, 0.3, 0.65, 1.0, 1.3845, 1.769, 2.1535]);
    expect(Array.from(st.period).map((x) => +x.toFixed(6))).toEqual([0.3, 0.35, 0.35, 0.3845, 0.3845, 0.769 / 2, 0.769 / 2]);
    expect(periodAt(st, 0.5)).toBeCloseTo(0.35, 9);
    expect(periodAt(st, -1)).toBeCloseTo(0.3, 9);
  });

  it("take the direction of the held keys: of two opposite keys the more recent wins", () => {
    expect(heldDirection(["left", "right"])).toEqual({ x: 1, y: 0 });
    expect(heldDirection(["right", "left"])).toEqual({ x: -1, y: 0 });
    const d = heldDirection(["up", "right"])!;
    expect(d.x).toBeCloseTo(Math.SQRT1_2, 9);
    expect(d.y).toBeCloseTo(-Math.SQRT1_2, 9);
    expect(heldDirection([])).toBeUndefined();
  });
});

describe("strides at 185 beats per minute (theme A)", () => {
  const { sch, section, run } = play(syntheticSongMap());
  const sec = section("theme-a");
  // Theme A's beats, from its second bar (well clear of the accent on its ninth).
  const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 0.5 && g < sec.s0 + 6);
  const p = beats[1] - beats[0];
  const one = 140 * p;
  const B = beats[0];
  const eps = 0.001;

  it("has a 325 ms stride period and a 45 px stride", () => {
    expect(p).toBeCloseTo(60 / 185, 9);
    expect(one).toBeCloseTo(45.4, 1);
  });

  it("covers 140 px/s holding, tapping quarter notes, tapping eighth notes and mashing 8 a second", () => {
    // Holding: a full stride at the press, then one on every beat.
    const hold = travel(run([{ s: B, kind: "press", key: "right" }], [B - eps, ...beats.slice(1, 10).map((b) => b - eps)]).at);
    for (const d of hold.steps) expect(d).toBeCloseTo(one, 6);
    expect(hold.speed).toBeCloseTo(140, 0);

    const quarterKeys = beats.slice(0, 10).flatMap((b) => tap(b));
    const quarter = run(quarterKeys, beats.slice(1, 10).map((b) => b - eps));
    for (const d of travel(quarter.at).steps) expect(d).toBeCloseTo(one, 6);
    expect(travel(quarter.at).speed).toBeCloseTo(140, 0);
    expect(quarter.inputs.slice(1).every((i) => Math.abs(i.stride! - one) < 1e-6)).toBe(true);

    const eighths = beats.slice(0, 10).flatMap((b) => [b, b + p / 2]);
    const eighth = run(eighths.flatMap((s) => tap(s)), eighths.slice(2, 18).map((s) => s - eps));
    for (const d of travel(eighth.at).steps) expect(d).toBeCloseTo(one / 2, 6);
    expect(one / 2).toBeCloseTo(23, 0);
    expect(travel(eighth.at).speed).toBeCloseTo(140, 0);

    const mashes = Array.from({ length: 24 }, (_, k) => B + k * 0.125);
    const mash = run(mashes.flatMap((s) => tap(s)), mashes.slice(4, 24).map((s) => s - eps));
    // (A beat that falls while a mashed key is still down strides too, so single presses vary a little.)
    const mashed = travel(mash.at).steps;
    expect(mashed.reduce((a, b) => a + b, 0) / mashed.length).toBeCloseTo(17.5, 1);
    for (const d of mashed) expect(Math.abs(d - 17.5)).toBeLessThan(5);
    expect(travel(mash.at).speed).toBeCloseTo(140, 0);
  });

  it("strides on every held beat, even one that comes while the press's stride is still moving", () => {
    // Press and hold 100 ms before a beat: the beat's held stride takes over from the press's (the
    // ground it had not covered, plus 100 ms of walking), so nothing is lost by the next beat.
    const press = beats[1] - 0.1;
    const r = run([{ s: press, kind: "press", key: "right" }], [press - eps, beats[1] + 0.05, beats[2] - eps, beats[3] - eps]);
    const [a, onBeat, b2, b3] = r.at;
    expect(onBeat.x - a.x).toBeGreaterThan(one);
    expect(b2.x - a.x).toBeCloseTo(one + 140 * 0.1, 6);
    expect(b3.x - b2.x).toBeCloseTo(one, 6);
  });

  it("returns an interrupted stride's uncovered ground to the budget", () => {
    // Right on the beat, then Down 50 ms later (past the diagonal window): the first stride stops
    // where it is, and the second has its uncovered ground plus 50 ms of walking.
    const r = run([...tap(B), ...tap(B + 0.05, "down")], [B + 1]);
    const back = one * (1 - 0.05 / 0.11) ** 3;
    expect(r.inputs.map((i) => i.stride)).toEqual([expect.closeTo(one, 6), expect.closeTo(140 * 0.05 + back, 6)]);
  });

  it("never beats the walk speed by more than one stride, however bends and interruptions mix", () => {
    // Right, Up 10 ms later (a bend), every 50 ms: each press interrupts the bent stride before it.
    const keys: KeyEvent[] = [];
    const t0 = sec.s0 + 0.5;
    for (let t = t0; t < t0 + 8; t += 0.05) {
      keys.push({ s: t, kind: "press", key: "right" }, { s: t + 0.01, kind: "press", key: "up" }, { s: t + 0.03, kind: "release", key: "right" }, { s: t + 0.03, kind: "release", key: "up" });
    }
    const probes = Array.from({ length: 170 }, (_, k) => t0 - 0.001 + k * 0.05);
    const at = run(keys, probes).at;
    let worst = -Infinity;
    for (let i = 0; i < at.length; i++) for (let j = i + 1; j < at.length; j++) worst = Math.max(worst, Math.hypot(at[j].x - at[i].x, at[j].y - at[i].y) - (140 * (at[j].w - at[i].w) + one + 1));
    expect(worst).toBeLessThanOrEqual(0);
  });

  it("bends a stride toward the diagonal for a perpendicular key within 40 ms, keeping its length", () => {
    const r = run([{ s: B, kind: "press", key: "right" }, { s: B + 0.02, kind: "press", key: "up" }, { s: B + 0.05, kind: "release", key: "right" }, { s: B + 0.05, kind: "release", key: "up" }], [B - eps, B + 0.2]);
    const [a, b] = r.at;
    expect(Math.hypot(b.x - a.x, b.y - a.y)).toBeCloseTo(one, 6);
    expect(b.x - a.x).toBeCloseTo(one / Math.SQRT2, 6);
    expect(r.inputs[1].bend).toBe(true);
  });
});

describe("strides in theme A's repeat", () => {
  const { sch, section, run } = play(syntheticSongMap());
  const sec = section("theme-a-repeat");
  const [b0, b1, b2, b3] = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 0.5);
  const one = 140 * (b1 - b0);

  it("strides at once on a press between beats, then snaps to the beat while the key is held", () => {
    // The design's example: beats at 80.240, 80.562 and 80.884; a press and hold at 80.400 strides
    // 45 px at once, 23 px on the next beat (the ground since the press), then 45 px.
    const press = b0 + 0.16;
    const r = run([{ s: press, kind: "press", key: "right" }], [press - 0.001, b1 - 0.001, b2 - 0.001, b3 - 0.001]);
    const [first, onBeat, next] = travel(r.at).steps;
    expect(first).toBeCloseTo(one, 6);
    expect(onBeat).toBeCloseTo(140 * (b1 - press), 6);
    expect(next).toBeCloseTo(one, 6);
    expect(Math.abs(first - 45)).toBeLessThan(1);
    expect(Math.abs(onBeat - 23)).toBeLessThan(1);
  });
});

describe("strides on a slow grid", () => {
  // The climax at 78 beats per minute: beats 769 ms apart, so the stride period halves to an eighth note.
  const { sch, section, run } = play(slowSection(syntheticSongMap(), "climax", 78));
  const sec = section("climax");
  const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 1 && g < sec.s1 - 1);
  const p = (beats[1] - beats[0]) / 2;

  it("halves the stride period, so holding strides 54 px on every eighth note at 140 px/s", () => {
    expect(beats[1] - beats[0]).toBeCloseTo(60 / 78, 6);
    const B = beats[0];
    const eighths = Array.from({ length: 9 }, (_, k) => B + k * p);
    const hold = travel(run([{ s: B, kind: "press", key: "right" }], eighths.map((s) => s - 0.001)).at);
    for (const d of hold.steps) {
      expect(d).toBeCloseTo(140 * p, 6);
      expect(Math.abs(d - 54)).toBeLessThan(1);
    }
    expect(Math.abs(hold.speed / 140 - 1)).toBeLessThan(0.05);
  });
});

describe("strides without a steady beat", () => {
  const { run } = play(syntheticSongMap());

  it("moves one 49 px stride on a tap from standing still, and walks at 140 px/s while a key is held", () => {
    const t = run(tap(3), [2.99, 3.5]);
    expect(travel(t.at).steps[0]).toBeCloseTo(49, 6);
    const held = run([{ s: 3, kind: "press", key: "right" }], [3.5, 4.5]);
    expect(travel(held.at).speed).toBeCloseTo(140, 0);
  });
});

describe("a press during a hit-stop", () => {
  const { ctx, sch, run } = play(syntheticSongMap());
  // A cleave: a Perfect press one grid step before an accent the level turns into a cleave.
  const i = sch.gridSlots.findIndex((g) => g.s > 60 && cleaves(ctx, g));
  const accent = sch.gridSlots[i].s;
  const before = sch.gridSlots[i - 1].s;

  it("ends the hit-stop and strides at once", () => {
    const rec = new BatchRecorder();
    const press = accent + 0.03;
    const r = run([...tap(before), ...tap(press, "down")], [press, press + 0.02], rec);
    const stop = rec.hitStops.find(([s0]) => Math.abs(s0 - accent) < 0.02);
    expect(stop).toBeDefined();
    expect(stop![1]).toBeCloseTo(press, 9);
    // World time runs again from the press, and the stride has taken the hero down 20 ms later.
    expect(r.at[1].w - r.at[0].w).toBeCloseTo(0.02, 6);
    expect(r.at[1].y - r.at[0].y).toBeGreaterThan(5);
    expect(r.inputs[1].stride).toBeGreaterThan(0);
  });
});
