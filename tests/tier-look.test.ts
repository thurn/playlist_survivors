/**
 * The tier is visible ("What a tier changes" in docs/design/gameplay-redesign.md): play mode's
 * frames carry the tier for the weapons' colours and the hero's aura, a tier-3 swing is a spin, and
 * the sword's arcs take the colour of the tier the swing was sized by. The demo's frames are as today.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { Warp } from "../src/core/warp.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import { heroSample } from "../src/plan/sample.ts";
import { StaticIndex } from "../src/plan/view.ts";
import { SWORD_TINTS, tierLook, AURA_ALPHA } from "../src/plan/tier-look.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

/** The demo as it was before the redesign (the old step rules), until hv-zoon removes that code. */
const LEGACY = "tests/fixtures/legacy-demo.level.yaml";

const songMap = syntheticSongMap();
const PLAY = "levels/orpheus-galop.play.yaml";
const lib = plannerInput({ songMap, level: PLAY }).content;
const plan = runPlanner({ songMap, level: PLAY, profile: "moderate" });
const ev = new Evaluator(plan, lib);

describe("the tier on screen", () => {
  it("is in every play-mode frame, and the aura shows from tier 1", () => {
    const t3 = plan.swings.find((sw) => sw.tier === 3)!;
    expect(ev.evaluate(t3.s).tier).toBeGreaterThanOrEqual(1);
    expect(ev.evaluate(1).tier).toBe(0);
    expect(tierLook(AURA_ALPHA, 0)).toBe(0);
    expect([1, 2, 3].every((t) => tierLook(AURA_ALPHA, t) > tierLook(AURA_ALPHA, t - 1))).toBe(true);
  });

  it("turns a tier-3 swing into a spin: the hero faces all four ways through its wind-up and impact", () => {
    const sw = plan.swings.find((x) => x.tier === 3 && x.kind === "light")!;
    const facings = new Set<number>();
    for (let k = 0; k < 8; k++) facings.add(ev.evaluate(sw.sWind + ((sw.s + 0.12 - sw.sWind) * (k + 0.5)) / 8).hero.facing);
    expect(facings.size).toBe(4);
    // A tier-0 swing doesn't spin.
    const low = plan.swings.find((x) => x.tier === 0 && x.kind === "light" && x.s > 5)!;
    const lowFacings = new Set<number>();
    for (let k = 0; k < 8; k++) lowFacings.add(ev.evaluate(low.sWind + ((low.s + 0.12 - low.sWind) * (k + 0.5)) / 8).hero.facing);
    expect(lowFacings.size).toBeLessThan(4);
  });

  it("draws the sword's arcs in the colour of the swing's tier", () => {
    for (const tier of [0, 3]) {
      const sw = plan.swings.find((x) => x.tier === tier && x.kind === "light" && x.s > 5)!;
      const arcs = ev.evaluate(sw.s + 0.02).fx.filter((f) => f.art.startsWith("proc:arc") || f.art === "proc:ring");
      expect(arcs.length, `tier ${tier}`).toBeGreaterThan(0);
      expect(arcs.some((f) => f.tint === SWORD_TINTS[tier]), `tier ${tier}`).toBe(true);
    }
  });

  it("recolours a swing that a live rewind replaced, though the swing count is the same", () => {
    // A live recording's index bumps its version on every rewind; here a swing's tier changes under
    // the same count, as a rewind and re-run can leave it.
    const p = runPlanner({ songMap, level: PLAY, profile: "moderate" });
    let version = 0;
    const base = new StaticIndex(p, lib);
    const index = new Proxy(base, {
      get: (t, k) => {
        if (k === "version") return version;
        const v = Reflect.get(t, k, t);
        return typeof v === "function" ? v.bind(t) : v;
      },
    });
    const live = new Evaluator(p, lib, index);
    const sw = p.swings.find((x) => x.tier === 3 && x.kind === "light")!;
    const tintAt = () => live.evaluate(sw.s + 0.02).fx.filter((f) => f.art.startsWith("proc:arc") || f.art === "proc:ring").map((f) => f.tint);
    expect(tintAt()).toContain(SWORD_TINTS[3]);
    sw.tier = 1;
    version = 1;
    expect(tintAt()).toContain(SWORD_TINTS[1]);
    expect(tintAt()).not.toContain(SWORD_TINTS[3]);
  });

  it("leaves the old rules' frames as they were (the legacy demo level)", () => {
    const demo = runPlanner({ songMap, level: LEGACY });
    const dev = new Evaluator(demo, lib);
    const sw = demo.swings[40];
    const f = dev.evaluate(sw.s + 0.02);
    expect(f.tier).toBeUndefined();
    // The hero faces as its recorded track has it, swings included.
    const warp = new Warp(demo.warp);
    for (let k = 0; k < 6; k++) {
      const t = sw.sWind + k * 0.03;
      expect(dev.evaluate(t).hero.facing).toBe(heroSample(demo, warp.toWorld(t)).facing);
    }
  });
});
