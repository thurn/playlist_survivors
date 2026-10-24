import { describe, expect, it } from "vitest";
import { propsFromAtlas, runPlanner } from "../scripts/lib/plan-node.ts";
import { reactiveInput, readStreamFile } from "../scripts/lib/reactive-node.ts";
import type { ContentLibrary } from "../src/content/loader.ts";
import { Warp } from "../src/core/warp.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import { enemyPos } from "../src/plan/sample.ts";
import type { Plan } from "../src/plan/types.ts";
import { planWithSim, prepare } from "../src/planner/plan.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { gaitBeat, gaitGround, gaitSlot, gaitStepAfter, gaitStepAtOrBefore, gaitSteps } from "../src/sim/gait.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

describe("gait times", () => {
  const beat = { every: "beat", pattern: "x", share: 0.4 } as const;
  const eighth = { every: "eighth", pattern: "x", share: 0.4 } as const;
  const elite = { every: "beat", pattern: "xx..", share: 0.4 } as const;
  const offbeat = { every: "eighth", pattern: ".x", share: 0.4 } as const;

  it("count beats or eighths from the song's first beat", () => {
    expect(gaitSlot(beat, 3.99)).toBe(3);
    expect(gaitSlot(beat, 4)).toBe(4);
    expect(gaitSlot(eighth, 3.6)).toBe(7);
    expect(gaitBeat(eighth, 7)).toBe(3.5);
  });

  it("step where the repeating pattern says, each covering the pattern's ground over its steps", () => {
    expect([0, 1, 2, 3, 4, 5, -1, -3].map((k) => gaitSteps(elite, k))).toEqual([true, true, false, false, true, true, false, true]);
    expect(gaitStepAtOrBefore(elite, 3)).toBe(1);
    expect(gaitStepAfter(elite, 1)).toBe(4);
    expect(gaitStepAtOrBefore(offbeat, 4)).toBe(3);
    expect(gaitGround(elite)).toBe(2);
    expect(gaitGround(offbeat)).toBe(2);
    expect(gaitGround(beat)).toBe(1);
  });
});

/**
 * Ground covered by every stepping enemy during its recorded steps (each from its start to its end,
 * plus a sample's slack) and between them (standing still, but for the crowd's pushes and knockback;
 * gliding, the most of it), over its first seconds and while it steps.
 */
function stepGround(plan: Plan): { stepping: number; standing: number } {
  let stepping = 0;
  let standing = 0;
  const a = { x: 0, y: 0 };
  const b = { x: 0, y: 0 };
  for (const e of plan.enemies) {
    const steps = e.steps;
    if (!steps) continue;
    let i = -1;
    for (let w = e.spawnW + 0.5; w < Math.min(e.deathW, e.spawnW + 6) - 0.002; w += 0.002) {
      while (i + 1 < steps.length && steps[i + 1].w <= w) i++;
      if (i < 0 || steps[i].dur === 0) continue;
      const d = Math.hypot(enemyPos(plan, e, w + 0.002, b).x - enemyPos(plan, e, w, a).x, b.y - a.y);
      if (w < steps[i].w + steps[i].dur + 0.02) stepping += d;
      else standing += d;
    }
  }
  return { stepping, standing };
}

/** Walk frames 0 and 4 are the cycle's footfalls; 1-3 and 5-7 swing a leg through. Of walking enemies between their steps, the share swinging. */
function swingingBetweenSteps(plan: Plan, lib: ContentLibrary, from: number, to: number): { n: number; swinging: number } {
  const ev = new Evaluator(plan, lib);
  const warp = new Warp(plan.warp);
  const byId = new Map(plan.enemies.map((e) => [e.id, e]));
  let n = 0;
  let swinging = 0;
  for (let s = from; s < to; s += 0.013) {
    const w = warp.toWorld(s);
    for (const x of ev.evaluate(s).enemies) {
      const steps = byId.get(x.id)!.steps;
      if (x.anim !== "walk" || !steps) continue;
      const g = steps.findLast((st) => st.w <= w);
      if (!g || g.dur === 0 || w < g.w + g.dur + 0.01) continue;
      n++;
      if (x.frame % 4 !== 0) swinging++;
    }
  }
  return { n, swinging };
}

describe("beat steps in reactive mode", () => {
  const run = reactiveInput(readStreamFile("orpheus-galop"));
  const ctx = prepare(run.input).ctx;
  const plan = planWithSim(run.input, { props: propsFromAtlas(), player: reactivePlayerFor(ctx, run.reference!, "reactive-moderate") }).plan;
  const lib = run.input.content;
  const warp = new Warp(plan.warp);

  it("step each type on its own pattern's committed gait times", () => {
    // A step starts on the fixed step after its gait time (a hair later in a hit-stop); an enemy
    // joining the steps (released from a set piece, or out of a beatless stretch) steps at once.
    let onTime = 0;
    let n = 0;
    const beats = plan.songBeats;
    const timeOf = (b: number) => {
      const i = Math.max(0, Math.min(beats.length - 2, Math.floor(b)));
      return beats[i] + (b - i) * (beats[i + 1] - beats[i]);
    };
    for (const e of plan.enemies) {
      const g = lib.enemies.get(e.type)!.gait;
      for (const st of e.steps ?? []) {
        if (st.dur === 0 || !g) continue;
        const s = warp.toSong(st.w);
        let lo = 0;
        while (lo + 1 < beats.length && beats[lo + 1] <= s) lo++;
        const bi = lo + (s - beats[lo]) / (beats[lo + 1] - beats[lo]);
        const k = gaitStepAtOrBefore(g, gaitSlot(g, bi));
        n++;
        if (s - timeOf(gaitBeat(g, k)) < 0.012) onTime++;
      }
    }
    expect(n).toBeGreaterThan(5000);
    expect(onTime / n).toBeGreaterThan(0.9);
  });

  it("freeze a step in a hit-stop, as the world stands still", () => {
    const ev = new Evaluator(plan, lib);
    const stops = warp.hitStops().filter(([s0, s1]) => s1 - s0 > 0.03);
    expect(stops.length).toBeGreaterThan(0);
    let checked = 0;
    for (const [s0, s1] of stops) {
      // (Positions and walk frames: the fast lane's squash and bob follow the music, which plays on.)
      const before = ev.evaluate(s0 + 0.005).enemies;
      const after = new Map(ev.evaluate(s1 - 0.005).enemies.map((x) => [x.id, x]));
      for (const x of before) {
        const y = after.get(x.id);
        if (!y || x.anim !== "walk" || !lib.enemies.get(x.type)!.gait) continue;
        checked++;
        expect([y.x, y.y, y.frame]).toEqual([x.x, x.y, x.frame]);
      }
    }
    expect(checked).toBeGreaterThan(100);
  });

  it("glide in play mode", () => {
    const play = runPlanner({ songMap: syntheticSongMap() });
    expect(play.enemies.some((e) => e.steps?.length)).toBe(false);
  });

  it("move stepping enemies in their steps and plant their feet between them", () => {
    const { stepping, standing } = stepGround(plan);
    expect(stepping).toBeGreaterThan(1000);
    expect(standing / (stepping + standing)).toBeLessThan(0.2);
    const { n, swinging } = swingingBetweenSteps(plan, lib, 60, 90);
    expect(n).toBeGreaterThan(1000);
    expect(swinging / n).toBeLessThan(0.02);
  });
});
