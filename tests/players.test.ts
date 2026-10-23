/**
 * The virtual players the difficulty curve is judged with ("Virtual Players" in
 * docs/design/gameplay-redesign.md): `?player=` names each, and each plans and
 * plays back in play mode on the synthetic song.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import type { Plan } from "../src/plan/types.ts";
import { readParams } from "../src/runtime/app.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

/** The demo as it was before the redesign (the old step rules), until hv-zoon removes that code. */
const LEGACY = "tests/fixtures/legacy-demo.level.yaml";

const songMap = syntheticSongMap();
const PLAY = "levels/orpheus-galop.play.yaml";
const lib = plannerInput({ songMap, level: PLAY }).content;
const NEW = ["holder", "eighths", "mash2", "mash4", "mash8"] as const;

/** Presses in step sections (stomps, diagonals' second keys and all). */
const stepPresses = (plan: Plan) => plan.inputs.filter((i) => i.kind === "press" && i.steps);
const stepSeconds = (plan: Plan) => plan.sections.filter((s) => s.movement === "steps").reduce((a, s) => a + s.s1 - s.s0, 0);

describe("?player=", () => {
  it("names the holder, the eighths player and the mashers, and nothing else", () => {
    for (const p of NEW) expect(readParams(`?mode=play&player=${p}`).player).toBe(p);
    expect(readParams("?player=mash3").player).toBeUndefined();
    expect(readParams("?player=moderate").player).toBe("moderate");
  });
});

describe("each new player in play mode", () => {
  const plans = new Map(NEW.map((p) => [p, runPlanner({ songMap, level: PLAY, profile: p })]));

  it("plans under its own name and plays back", () => {
    for (const [p, plan] of plans) {
      expect(plan.profile).toBe(p);
      const ev = new Evaluator(plan, lib);
      for (const s of [30, 60, 120, plan.duration - 1]) expect(ev.evaluate(s).hud).toBeDefined();
    }
  });

  it("the holder holds: in step movement at most one change a bar, never a stomp", () => {
    const plan = plans.get("holder")!;
    const presses = stepPresses(plan);
    expect(presses.length).toBeGreaterThan(20);
    expect(presses.some((i) => i.key === "space")).toBe(false);
    // A change is one moment (a diagonal's second key follows within 10 ms).
    const moments = presses.filter((i, k) => k === 0 || i.s - presses[k - 1].s > 0.01);
    const bars = Array.from(plan.barStart, (b) => plan.songBeats[b]).filter((s) => s !== undefined);
    const perBar = new Map<number, number>();
    for (const m of moments) {
      const bar = bars.findLastIndex((s) => s <= m.s + 1e-9);
      perBar.set(bar, (perBar.get(bar) ?? 0) + 1);
    }
    expect(Math.max(...perBar.values())).toBe(1);
    // It never taps: a key stays down until a later change lets it go.
    const holds = presses.map((p) => (plan.inputs.find((i) => i.kind === "release" && i.key === p.key && i.s > p.s)?.s ?? plan.duration) - p.s).sort((a, b) => a - b);
    expect(holds[Math.floor(holds.length / 2)]).toBeGreaterThan(0.5);
  });

  it("the mashers press at their rate, ignoring the beat", () => {
    for (const rate of [2, 4, 8] as const) {
      const plan = plans.get(`mash${rate}` as const)!;
      const perS = stepPresses(plan).length / stepSeconds(plan);
      expect(perS, `mash${rate}`).toBeGreaterThan(rate * 0.85);
      expect(perS, `mash${rate}`).toBeLessThan(rate * 1.15);
    }
  });

  it("the eighths player presses every grid time and every off-beat, nearly all Perfect or Great", () => {
    const plan = plans.get("eighths")!;
    const judged = stepPresses(plan).filter((i) => i.class === "counted" || i.class === "offbeat");
    const counted = judged.filter((i) => i.class === "counted").length;
    const offbeats = judged.filter((i) => i.class === "offbeat").length;
    expect(offbeats).toBeGreaterThan(counted * 0.9);
    expect(counted).toBeGreaterThan(plan.grid.length * 0.6);
    // (A pulsed section's first press can land just before it starts, in the free section: until an
    // on-beat press is Great again, its off-beats are counted presses rated loose.)
    expect(judged.filter((i) => (i.class === "counted" ? i.rating : i.offbeat) === undefined).length).toBeLessThan(judged.length * 0.02);
    // It keeps pressing off-beats where one pulsed section runs into the next.
    const pulsed = plan.sections.filter((x) => x.movement === "steps");
    const presses = stepPresses(plan);
    let joins = 0;
    for (let k = 0; k + 1 < pulsed.length; k++) {
      if (Math.abs(pulsed[k + 1].s0 - pulsed[k].s1) > 1e-6) continue;
      const before = Array.from(plan.grid).findLast((g) => g < pulsed[k].s1 - 1e-6)!;
      const after = Array.from(plan.grid).find((g) => g >= pulsed[k].s1 - 1e-6)!;
      const off = (before + after) / 2;
      joins++;
      expect(presses.some((i) => Math.abs(i.s - off) < 0.045), `an off-beat at ${off.toFixed(3)}`).toBe(true);
    }
    expect(joins).toBeGreaterThan(3);
  });
});

describe("the casual player under the groove", () => {
  it("presses downbeats with 60 ms timing in play mode, and keeps the old rules' 120 ms", () => {
    const spread = (plan: Plan) => {
      const d = plan.inputs.filter((i) => i.kind === "press" && i.steps && i.delta !== undefined && (i.class === undefined || i.class === "counted")).map((i) => i.delta!);
      const mean = d.reduce((a, x) => a + x, 0) / d.length;
      return Math.sqrt(d.reduce((a, x) => a + (x - mean) ** 2, 0) / d.length);
    };
    const play = spread(runPlanner({ songMap, level: PLAY, profile: "casual" }));
    const demo = spread(runPlanner({ songMap, level: LEGACY, profile: "casual" }));
    expect(play).toBeGreaterThan(0.04);
    expect(play).toBeLessThan(0.075);
    expect(demo).toBeGreaterThan(play + 0.015);
  });
});
