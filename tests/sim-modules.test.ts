import { describe, expect, it } from "vitest";
import { loadRealContent } from "../scripts/lib/content-check.ts";
import { TAU, wrapAngle } from "../src/core/geometry.ts";
import { aim, type AimTarget } from "../src/sim/aim.ts";
import { stepSpan } from "../src/sim/schedule.ts";
import { bendStep, KEY_DIR, movePose, stepEnd, walkFrame, type Key, type Step } from "../src/sim/movement.ts";
import { ratePress, ratePressOnGrid } from "../src/sim/timing.ts";

const hero = loadRealContent().hero;

describe("hero content", () => {
  it("loads the hero settings", () => {
    expect(hero.step.length).toBe(44);
    expect(hero.swings.heavy.hitStopMs).toBe(110);
    expect(hero.swings.light.animations).toEqual(["slash", "backslash", "halfslash"]);
  });
});

describe("bends", () => {
  /** A 44 px stride toward `key`, pressed at world time 0 from the origin. */
  const stride = (key: Key): Step => {
    const d = KEY_DIR[key];
    return { w0: 0, x0: 0, y0: 0, dx: d.x, dy: d.y, len1: 44, ex: d.x * 44, ey: d.y * 44, keys: [key], len: 44 };
  };
  const durationS = hero.stride.durationMs / 1000;

  it("bend a stride toward the diagonal for a perpendicular key in the window, landing 44 px away", () => {
    const st = stride("right");
    const bent = bendStep(hero, st, 0.012, "up", undefined, durationS)!;
    expect(bent).toBeDefined();
    const end = stepEnd(bent);
    expect(Math.hypot(end.x, end.y)).toBeCloseTo(44, 6);
    expect(end.x).toBeCloseTo(44 / Math.SQRT2, 6);
    expect(end.y).toBeCloseTo(-44 / Math.SQRT2, 6);
    // The path is continuous at the bend and smooth after it (no kink: small turns between samples).
    const at = (w: number) => movePose(bent, w, durationS, hero.stride.hop);
    const before = movePose(st, 0.012, durationS, hero.stride.hop);
    expect(at(0.012).x).toBeCloseTo(before.x, 6);
    let prevAng: number | undefined;
    for (let w = 0.013; w < 0.105; w += 0.002) {
      const a = at(w);
      const b = at(w + 0.002);
      const ang = Math.atan2(b.y - a.y, b.x - a.x);
      if (prevAng !== undefined) expect(Math.abs(wrapAngle(ang - prevAng))).toBeLessThan(0.35);
      prevAng = ang;
    }
    expect(at(0.2).x).toBeCloseTo(end.x, 6);
    // It hops at its peak.
    expect(at(durationS / 2).z).toBeCloseTo(hero.stride.hop, 6);
  });

  it("leave a stride alone for a late, parallel or second extra key", () => {
    const st = stride("right");
    expect(bendStep(hero, st, 0.05, "up", undefined, durationS)).toBeUndefined();
    expect(bendStep(hero, st, 0.01, "left", undefined, durationS)).toBeUndefined();
    const bent = bendStep(hero, st, 0.01, "down", undefined, durationS)!;
    expect(bendStep(hero, bent, 0.02, "up", undefined, durationS)).toBeUndefined();
  });
});

describe("walking", () => {
  it("advances the walk cycle by distance", () => {
    expect(walkFrame(hero, 0, 8)).toBe(0);
    expect(walkFrame(hero, 32, 8)).toBe(4);
    expect(walkFrame(hero, 64 + 8, 8)).toBe(1);
  });
});

describe("timing", () => {
  it("rates presses by their distance from the grid", () => {
    expect(ratePress(hero, 0.02)).toEqual({ delta: 0.02, rating: "perfect" });
    expect(ratePress(hero, -0.06).rating).toBe("great");
    expect(ratePress(hero, 0.12).rating).toBe("good");
    expect(ratePress(hero, 0.15).rating).toBeUndefined();
  });

  it("rates against the nearest grid time", () => {
    const grid = [1, 1.3, 1.6, 1.9];
    const r = ratePressOnGrid(hero, 1.592, grid);
    expect(r.gridIndex).toBe(2);
    expect(r.delta).toBeCloseTo(-0.008, 9);
    expect(r.rating).toBe("perfect");
  });
});

describe("auto-aim", () => {
  const reach = { radius: 100, halfAngle: (150 * Math.PI) / 360 };
  const enemy = (x: number, y: number, weight = 1): AimTarget => ({ x, y, r: 12, weight });

  it("swings toward the crowd", () => {
    const targets = [enemy(-60, 10), enemy(-70, -20), enemy(-50, 40), enemy(60, 0)];
    const dir = aim(0, 0, 0, targets, reach);
    expect(Math.cos(dir)).toBeLessThan(-0.7);
  });

  it("weights the boss and elites", () => {
    // Two regulars below, the boss above: the swing must catch the boss.
    const targets = [enemy(0, 60), enemy(10, 70), enemy(0, -60, 5)];
    const dir = aim(0, 0, 0, targets, reach);
    expect(Math.abs(wrapAngle(dir + Math.PI / 2))).toBeLessThanOrEqual(reach.halfAngle);
  });

  it("breaks ties toward the nearest enemy", () => {
    const dir = aim(0, 0, 0, [enemy(0, 90)], reach);
    // Several directions catch it; the one nearest straight down wins.
    expect(dir).toBeCloseTo(TAU / 4, 6);
  });

  it("aims at the nearest enemy within twice the reach when nobody is in reach, else keeps facing", () => {
    expect(aim(0, 0, 1, [enemy(120, 120)], reach)).toBeCloseTo(Math.PI / 4, 6);
    expect(aim(0, 0, 1, [enemy(300, 0)], reach)).toBe(1);
    expect(aim(0, 0, 1, [], reach)).toBe(1);
  });
});

describe("rate steps", () => {
  const steps = [
    { s: 0, rate: 1 },
    { s: 2, rate: 0.5 },
    { s: 3, rate: 0.25 },
  ];

  it("measures world time across the steps between two song times", () => {
    expect(stepSpan(steps, 0, 1)).toBeCloseTo(1, 9);
    expect(stepSpan(steps, 1.5, 2.5)).toBeCloseTo(0.5 + 0.25, 9);
    expect(stepSpan(steps, 1, 5)).toBeCloseTo(1 + 0.5 + 0.5, 9);
    expect(stepSpan(steps, 4, 4)).toBe(0);
  });
});
