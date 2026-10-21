import { describe, expect, it } from "vitest";
import { Smoother } from "../src/runtime/audio.ts";

describe("audio clock smoother", () => {
  it("never runs backwards and corrects at most 2 ms per frame", () => {
    const sm = new Smoother();
    let t = 1000;
    let prev = sm.update(1.0, t);
    // A raw estimate that jitters by ±10 ms around the truth.
    for (let k = 1; k < 600; k++) {
      t += 1000 / 60;
      const truth = 1.0 + (t - 1000) / 1000;
      const raw = truth + (k % 2 === 0 ? 0.01 : -0.01);
      const v = sm.update(raw, t);
      expect(v).toBeGreaterThanOrEqual(prev);
      const predicted = prev + 1 / 60;
      expect(Math.abs(v - predicted)).toBeLessThanOrEqual(0.002 + 1e-9);
      expect(Math.abs(v - truth)).toBeLessThan(0.012);
      prev = v;
    }
  });

  it("jumps straight to the new value after a glitch or a hidden tab", () => {
    const sm = new Smoother();
    sm.update(10, 0);
    sm.update(10 + 1 / 60, 1000 / 60);
    // Ten seconds later (the tab was hidden): the estimate jumps rather than slewing.
    expect(sm.update(20.5, 10_000)).toBeCloseTo(20.5, 9);
    // A backwards glitch of more than 50 ms is also taken immediately.
    expect(sm.update(20.3, 10_000 + 1000 / 60)).toBeCloseTo(20.3, 9);
  });
});
