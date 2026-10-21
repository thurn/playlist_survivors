import { describe, expect, it } from "vitest";
import { loadRealContent } from "../scripts/lib/content-check.ts";
import { TAU, wrapAngle } from "../src/core/geometry.ts";
import { aim, type AimTarget } from "../src/sim/aim.ts";
import { bendStep, startStep, stepEnd, stepPose, walkFrame, walkTarget, walkVelocity, type Clamp } from "../src/sim/movement.ts";
import { armedStrike, damageMultiplier, grooveAfterPress, grooveDecay, ratePress, ratePressOnGrid, windupStart } from "../src/sim/timing.ts";

const hero = loadRealContent().hero;

describe("hero content", () => {
  it("loads the hero settings", () => {
    expect(hero.step.length).toBe(44);
    expect(hero.swings.heavy.hitStopMs).toBe(110);
    expect(hero.swings.light.animations).toEqual(["slash", "backslash", "halfslash"]);
  });
});

describe("step movement", () => {
  it("moves one quick, firm step and then stands still", () => {
    const st = startStep(hero, 10, 100, 200, "right");
    expect(stepPose(hero, st, 10).x).toBe(100);
    // Cubic ease-out: most of the distance is covered early.
    const early = stepPose(hero, st, 10.03);
    expect(early.x - 100).toBeGreaterThan(0.5 * 44);
    expect(early.z).toBeGreaterThan(0);
    const end = stepPose(hero, st, 10.11);
    expect(end.x).toBeCloseTo(144, 6);
    expect(end.y).toBe(200);
    expect(end.z).toBeCloseTo(0, 6);
    // Long after, still exactly at the landing spot.
    expect(stepPose(hero, st, 12).x).toBeCloseTo(144, 6);
  });

  it("hops 5 px at its peak", () => {
    const st = startStep(hero, 0, 0, 0, "up");
    expect(stepPose(hero, st, 0.055).z).toBeCloseTo(5, 6);
    expect(stepPose(hero, st, 0.11).y).toBeCloseTo(-44, 6);
  });

  it("bends toward the diagonal for a perpendicular key in the window, landing 44 px away", () => {
    const st = startStep(hero, 0, 0, 0, "right");
    const bent = bendStep(hero, st, 0.012, "up")!;
    expect(bent).toBeDefined();
    const end = stepEnd(bent);
    expect(Math.hypot(end.x, end.y)).toBeCloseTo(44, 6);
    expect(end.x).toBeCloseTo(44 / Math.SQRT2, 6);
    expect(end.y).toBeCloseTo(-44 / Math.SQRT2, 6);
    // The path is continuous at the bend and smooth after it (no kink: small turns between samples).
    const at = (w: number) => stepPose(hero, bent, w);
    const before = stepPose(hero, st, 0.012);
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
  });

  it("does not bend for a late, parallel or second extra key", () => {
    const st = startStep(hero, 0, 0, 0, "right");
    expect(bendStep(hero, st, 0.05, "up")).toBeUndefined();
    expect(bendStep(hero, st, 0.01, "left")).toBeUndefined();
    const bent = bendStep(hero, st, 0.01, "down")!;
    expect(bendStep(hero, bent, 0.02, "up")).toBeUndefined();
  });

  it("shortens a step that would leave the corridor", () => {
    // A wall at y = -20.
    const walls: Clamp = (_x, y, _dx, dy, len) => (dy < 0 ? Math.min(len, Math.max(0, (y + 20) / -dy)) : len);
    const st = startStep(hero, 0, 0, 0, "up", walls);
    expect(stepEnd(st).y).toBeCloseTo(-20, 6);
    expect(stepPose(hero, st, 0.05).y).toBeGreaterThanOrEqual(-20);
  });
});

describe("free movement", () => {
  it("walks at full speed within 60 ms and in eight directions", () => {
    const t = walkTarget(hero, ["up", "right"]);
    expect(Math.hypot(t.x, t.y)).toBeCloseTo(80, 6);
    expect(t.x).toBeCloseTo(80 / Math.SQRT2, 6);
    let v = { x: 0, y: 0 };
    for (let i = 0; i < 6; i++) v = walkVelocity(hero, v.x, v.y, t.x, t.y, 0.01);
    expect(Math.hypot(v.x, v.y)).toBeCloseTo(80, 6);
    expect(walkTarget(hero, ["left", "right"])).toEqual({ x: 0, y: 0 });
    let s = { x: 80, y: 0 };
    for (let i = 0; i < 6; i++) s = walkVelocity(hero, s.x, s.y, 0, 0, 0.01);
    expect(Math.hypot(s.x, s.y)).toBeCloseTo(0, 6);
  });

  it("advances the walk cycle by distance", () => {
    expect(walkFrame(hero, 0, 8)).toBe(0);
    expect(walkFrame(hero, 32, 8)).toBe(4);
    expect(walkFrame(hero, 64 + 8, 8)).toBe(1);
  });
});

describe("timing", () => {
  it("rates presses by their distance from the grid", () => {
    expect(ratePress(hero, 0.02, 0.32)).toMatchObject({ rating: "perfect", accuracy: 1 });
    expect(ratePress(hero, -0.0525, 0.32).rating).toBe("great");
    expect(ratePress(hero, -0.0525, 0.32).accuracy).toBeCloseTo(0.875, 6);
    expect(ratePress(hero, 0.095, 0.32).accuracy).toBeCloseTo(0.575, 6);
    expect(ratePress(hero, 0.095, 0.32).rating).toBe("good");
    const late = ratePress(hero, 0.14, 0.32);
    expect(late.rating).toBeUndefined();
    // 0.4 at 120 ms, floor at half a grid step (160 ms).
    expect(late.accuracy).toBeCloseTo(0.25, 6);
    expect(ratePress(hero, 0.2, 2).accuracy).toBeCloseTo(0.4 - 0.3 * (80 / 130), 6);
    expect(ratePress(hero, 0.3, 2).accuracy).toBeCloseTo(0.1, 6);
  });

  it("rates against the nearest grid time", () => {
    const grid = [1, 1.3, 1.6, 1.9];
    const r = ratePressOnGrid(hero, 1.592, grid);
    expect(r.gridIndex).toBe(2);
    expect(r.delta).toBeCloseTo(-0.008, 9);
    expect(r.rating).toBe("perfect");
  });

  it("moves the groove 30% toward each press's accuracy and halves it every 8 idle bars", () => {
    expect(grooveAfterPress(hero, 0.5, 1)).toBeCloseTo(0.65, 9);
    expect(grooveAfterPress(hero, 0.9, 0.4)).toBeCloseTo(0.75, 9);
    expect(grooveDecay(hero, 0.8, 8)).toBeCloseTo(0.4, 9);
    expect(grooveDecay(hero, 0.8, 4)).toBeCloseTo(0.8 / Math.SQRT2, 9);
  });

  it("raises damage by up to 20%", () => {
    expect(damageMultiplier(hero, 0)).toBe(1);
    expect(damageMultiplier(hero, 0.95)).toBeCloseTo(1.19, 9);
    expect(damageMultiplier(hero, 1)).toBeCloseTo(1.2, 9);
  });

  it("arms the swing for the first grid time at least 90 ms after the press", () => {
    const grid = [88.01, 88.334, 88.658];
    // An on-time press strikes on the next beat.
    expect(armedStrike(hero, grid, 88.002)).toBe(1);
    // A press 250 ms late still strikes on the grid.
    expect(armedStrike(hero, grid, 88.26)).toBe(2);
    expect(armedStrike(hero, grid, 88.7)).toBe(3);
    expect(windupStart(hero, 88.002, 88.334)).toBeCloseTo(88.144, 9);
    expect(windupStart(hero, 88.2, 88.334)).toBe(88.2);
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
