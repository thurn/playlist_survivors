import { describe, expect, it } from "vitest";
import { propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { reactiveInput, readStreamFile } from "../scripts/lib/reactive-node.ts";
import { Warp } from "../src/core/warp.ts";
import { enemyPos } from "../src/plan/sample.ts";
import { planWithSim, prepare } from "../src/planner/plan.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { gaitBeat, gaitSlot } from "../src/sim/gait.ts";

describe("gait times", () => {
  it("count beats or eighths from the song's first beat", () => {
    expect(gaitSlot({ every: "beat", share: 0.4 }, 3.99)).toBe(3);
    expect(gaitSlot({ every: "beat", share: 0.4 }, 4)).toBe(4);
    expect(gaitSlot({ every: "eighth", share: 0.4 }, 3.6)).toBe(7);
    expect(gaitBeat({ every: "eighth", share: 0.4 }, 7)).toBe(3.5);
  });
});

describe("beat steps in reactive mode", () => {
  const run = reactiveInput(readStreamFile("orpheus-galop"));
  const ctx = prepare(run.input).ctx;
  const plan = planWithSim(run.input, { props: propsFromAtlas(), player: reactivePlayerFor(ctx, run.reference!, "reactive-moderate") }).plan;
  const lib = run.input.content;
  const warp = new Warp(plan.warp);
  const beats = plan.songBeats;
  const beatAt = (s: number) => {
    let lo = 0;
    while (lo + 2 < beats.length && beats[lo + 1] <= s) lo++;
    return lo + (s - beats[lo]) / (beats[lo + 1] - beats[lo]);
  };

  it("move a stepping enemy in quick steps from each committed beat, standing still in between", () => {
    // Ground covered by every stepping enemy in the first half of each beat (its step, which takes
    // 0.4 of it) and in the second (standing still, but for the crowd's pushes and the stretches
    // where the beat is unclear and it glides; gliding throughout, nearly half).
    let stepping = 0;
    let standing = 0;
    const a = { x: 0, y: 0 };
    const b = { x: 0, y: 0 };
    for (const e of plan.enemies) {
      if (!lib.enemies.get(e.type)!.gait) continue;
      for (let w = e.spawnW + 0.5; w < Math.min(e.deathW, e.spawnW + 6) - 0.002; w += 0.002) {
        const s = warp.toSong(w);
        if (s < beats[0] || s >= beats[beats.length - 1]) continue;
        const d = Math.hypot(enemyPos(plan, e, w + 0.002, b).x - enemyPos(plan, e, w, a).x, b.y - a.y);
        if (beatAt(s) % 1 < 0.5) stepping += d;
        else standing += d;
      }
    }
    expect(stepping).toBeGreaterThan(1000);
    expect(standing / (stepping + standing)).toBeLessThan(0.25);
  });
});
