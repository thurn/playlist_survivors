/**
 * The `?debug=moves` overlay's readout, for a scripted play-mode run on the
 * synthetic song (theme A at 185 beats per minute).
 */
import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { grooveAt } from "../src/plan/evaluate.ts";
import { plan as makePlan } from "../src/planner/plan.ts";
import { readParams } from "../src/runtime/app.ts";
import { movesLines, movesReadout } from "../src/runtime/debug-moves.ts";
import type { KeyEvent, Player } from "../src/sim/player.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });

/** A play-mode plan driven by scripted key events. */
function scripted(keys: KeyEvent[]) {
  let next = 0;
  const player: Player = {
    nextDecision: (after) => (next < keys.length ? Math.max(after, keys[next].s) : Infinity),
    decide: (obs) => {
      const out: KeyEvent[] = [];
      while (next < keys.length && keys[next].s <= obs.s + 1e-9) out.push(keys[next++]);
      return out;
    },
  };
  return makePlan(input, { props: propsFromAtlas(), player });
}

describe("?debug=moves", () => {
  it("is a URL parameter of its own", () => {
    expect(readParams("?mode=play&debug=moves").debugMoves).toBe(true);
    expect(readParams("?mode=play&debug=other").debugMoves).toBe(false);
    expect(readParams("?mode=play").debugMoves).toBe(false);
  });

  const beatsOf = (plan: ReturnType<typeof scripted>) => {
    const a = plan.sections.find((s) => s.label === "theme-a")!;
    return Array.from(plan.grid).filter((g) => g > a.s0 + 0.5 && g < a.s1 - 1);
  };

  it("reads the walk speed while holding, with the budget refilling between strides", () => {
    const probe = scripted([]);
    const b = beatsOf(probe);
    const plan = scripted([{ s: b[0], kind: "press", key: "right" }, { s: b[10], kind: "release", key: "right" }]);
    const one = 140 * (b[1] - b[0]);
    const r = movesReadout(plan, b[6] + 0.01);
    // (Measured between stride starts, a steady walk reads the walk speed.)
    expect(Math.abs(r.speed - 140)).toBeLessThan(3);
    // Just after a held stride the budget is nearly empty; just before the next it is a full stride.
    expect(r.budget).toBeCloseTo(140 * 0.01, 0);
    expect(movesReadout(plan, b[7] - 0.005).budget).toBeCloseTo(140 * (b[7] - 0.005 - b[6]), 6);
    expect(movesReadout(plan, b[7] - 0.005).budget).toBeLessThanOrEqual(one);
    expect(r.groove).toBeCloseTo(grooveAt(plan.groove, b[6] + 0.01), 9);
    expect(r.presses.map((p) => [p.key, p.cls, p.rating])).toEqual([["right", "counted", "perfect"]]);
  });

  it("reads the same speed for quarter taps, eighth taps and mashing, and lists each press with its class and offset", () => {
    const probe = scripted([]);
    const b = beatsOf(probe);
    const p = (s: number, key: KeyEvent["key"] = "right"): KeyEvent[] => [
      { s, kind: "press", key },
      { s: s + 0.03, kind: "release", key },
    ];
    const half = (k: number) => (b[k] + b[k + 1]) / 2;
    const quarters = scripted(b.slice(0, 8).flatMap((s) => p(s)));
    const eighths = scripted(b.slice(0, 8).flatMap((s, k) => [...p(s), ...p(half(k) + 0.01, "up")]));
    const mash = scripted(Array.from({ length: 24 }, (_, k) => b[0] + k * 0.125).flatMap((s) => p(s)));
    // (Mashing 8 a second does not divide the window evenly: within 6%.)
    for (const plan of [quarters, eighths, mash]) expect(Math.abs(movesReadout(plan, b[6] + 0.05).speed / 140 - 1)).toBeLessThan(0.06);
    const r = movesReadout(eighths, b[6] - 0.01);
    expect(r.presses.length).toBe(8);
    const offbeat = r.presses.filter((x) => x.cls === "offbeat");
    expect(offbeat.length).toBe(4);
    for (const x of offbeat) expect(x.deltaMs).toBeCloseTo(10, 6);
    for (const x of r.presses.filter((y) => y.cls === "counted")) expect(Math.abs(x.deltaMs!)).toBeLessThan(1e-6);
    const lines = movesLines(r);
    expect(lines[1]).toMatch(/^speed +\d+ px\/s/);
    expect(lines.some((l) => / up +offbeat perfect +\+10 ms off$/.test(l))).toBe(true);
  });

  it("counts an isolated tap as soon as it lands, and falls back to nothing when the hero stands", () => {
    const b = beatsOf(scripted([]));
    const plan = scripted([{ s: b[2], kind: "press", key: "right" }, { s: b[2] + 0.03, kind: "release", key: "right" }]);
    const one = 140 * (b[1] - b[0]);
    // 150 ms after the tap: one stride over the window (3 stride periods, just under a second).
    expect(movesReadout(plan, b[2] + 0.15).speed).toBeCloseTo(one / (3 * (b[1] - b[0])), 0);
    expect(movesReadout(plan, b[2] + 1.5).speed).toBeLessThan(1);
  });

  it("knows a stomp cancels the next held stride: the budget stays full", () => {
    const b = beatsOf(scripted([]));
    const one = 140 * (b[1] - b[0]);
    const plan = scripted([
      { s: b[0], kind: "press", key: "right" },
      { s: b[3] - 0.01, kind: "press", key: "space" },
      { s: b[3] + 0.04, kind: "release", key: "space" },
      { s: b[5], kind: "release", key: "right" },
    ]);
    // Just after the beat whose held stride the stomp cancelled, the budget is a full stride.
    expect(movesReadout(plan, b[3] + 0.02).budget).toBeCloseTo(one, 0);
    expect(movesReadout(plan, b[4] + 0.02).budget).toBeCloseTo(140 * 0.02, 0);
  });

  it("knows a walk without a steady beat spends the refill", () => {
    // In the prelude, hold Right: a 49 px stride, then a walk from its landing at 140 px/s.
    const plan = scripted([{ s: 3, kind: "press", key: "right" }, { s: 6, kind: "release", key: "right" }]);
    const r = movesReadout(plan, 5);
    expect(r.budget).toBeCloseTo(140 * 0.11, 0);
    expect(Math.abs(r.speed - 140)).toBeLessThan(5);
    // A diagonal (two keys held) walks once, not twice.
    const diagonal = scripted([{ s: 3, kind: "press", key: "right" }, { s: 3.02, kind: "press", key: "up" }, { s: 6, kind: "release", key: "right" }, { s: 6, kind: "release", key: "up" }]);
    expect(movesReadout(diagonal, 5).budget).toBeCloseTo(140 * 0.11, 0);
    // A stomp while walking pauses the walk for its stamp, which refills the budget.
    const stomped = scripted([{ s: 3, kind: "press", key: "right" }, { s: 4, kind: "press", key: "space" }, { s: 4.05, kind: "release", key: "space" }, { s: 6, kind: "release", key: "right" }]);
    expect(movesReadout(stomped, 5).budget).toBeCloseTo(2 * 140 * 0.11, 0);
  });
});
