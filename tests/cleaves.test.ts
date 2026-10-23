/**
 * Accents and cleaves ("Accents and cleaves" in
 * docs/design/gameplay-redesign.md), on the real song: an eligible accent's
 * swing is heavy for everyone, and a press on the chord launches its
 * shockwave from the eighth after it.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { checkPlan } from "../src/planner/checks.ts";
import { cleaves } from "../src/planner/plan.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { play, tap } from "./fixtures/play-sim.ts";

const { ctx, sch, section, run } = play(plannerInput({ level: "levels/orpheus-galop.play.yaml" }).songMap);
const hero = ctx.lib.hero;

/** The latest run's hit shapes (arcs), by hit id. */
let rec = (_id: number): { radius: number; inner?: number } => ({ radius: 0 });

/** One run with `keys`, to a second after the accent: the swing on it, its shockwave's bands, the cleave and the hit-stops. */
function cleaveAt(accent: number, keys: ReturnType<typeof tap>) {
  const r = new BatchRecorder();
  run(keys, [accent + 1], r);
  rec = (id) => r.hits[id].shape as { radius: number; inner?: number };
  return pick(r, accent);
}

function pick(rec: BatchRecorder, accent: number) {
  const swing = rec.swings.find((sw) => Math.abs(sw.s - accent) < 1e-6)!;
  const wave = rec.waves.find((w) => w.s !== undefined && Math.abs(w.s - accent) < 1e-6);
  const bands = wave ? wave.hits.map((h) => rec.hits[h].s) : [];
  const stops = rec.hitStops.filter(([s0]) => Math.abs(s0 - accent) < 0.01);
  return { swing, wave, bands, stops, hit: rec.hits[swing.hit] };
}

describe("a press on the chord", () => {
  it("follows the design's galop-opening trace: a Great press 50 ms late launches the wave on the 'and'", () => {
    // The design's trace has a tutti chord at 56.780; in the song map that chord is 41 ms off its
    // grid time, so the galop opening's first eligible one stands in: about the same tempo (an eighth
    // of 178 ms there) and the same offsets.
    const accent = sch.gridSlots.find((g) => g.s > section("galop-opening").s0 + 1 && cleaves(ctx, g))!.s;
    const bi = ctx.song.beats.findIndex((b) => b > accent - 1e-6);
    const eighth = (ctx.song.beats[bi + 1] - ctx.song.beats[bi]) / 2;
    expect(eighth).toBeCloseTo(0.171, 1);
    const { swing, wave, bands, stops, hit } = cleaveAt(accent, tap(accent + 0.05));
    // The heavy wind-up for every player (190 ms before), the heavy impact on the chord.
    expect(swing.kind).toBe("heavy");
    expect(swing.sWind).toBeCloseTo(accent - 0.19, 2);
    expect(hit.what).toBe("cleave");
    expect(hit.damage).toBeCloseTo(hero.swings.heavy.damage * hero.meter.damage[swing.tier ?? 0], 9);
    // The press arrives after the impact: a cleave, no hit-stop.
    expect(wave).toMatchObject({ press: expect.closeTo(accent + 0.05, 6) });
    expect(stops).toEqual([]);
    expect(bands[0]).toBeCloseTo(accent + eighth, 6);
    expect(bands[1]).toBeCloseTo(accent + 1.5 * eighth, 6);
    expect(wave!.band0).toBeCloseTo(bands[0], 9);
    // Bands of 25 px from the tier's heavy arc, one per sixteenth; the last reaches the tier's reach exactly.
    const tier = swing.tier ?? 0;
    const shapes = wave!.hits.map((h) => rec(h));
    expect(shapes[0].inner).toBe(hero.swings.heavy.tiers![tier].radius);
    expect(shapes[0].radius).toBe(hero.swings.heavy.tiers![tier].radius + 25);
    expect(shapes.at(-1)!.radius).toBe(hero.swings.heavy.wave.tiers![tier]);
    expect(shapes.at(-2)!.radius).toBeLessThan(hero.swings.heavy.wave.tiers![tier]);
  });

  it("in the coda, too late for the 'and' (yet Great), starts the wave one sixteenth later", () => {
    // The coda's eighth is short enough (here 145 ms) that a Great press can come less than 60 ms
    // before it: one 4 ms past that.
    const coda = section("coda");
    const accent = sch.gridSlots.find((g) => g.s > coda.s0 + 2 && cleaves(ctx, g))!.s;
    const bi = ctx.song.beats.findIndex((b) => b > accent - 1e-6);
    const beat = ctx.song.beats[bi + 1] - ctx.song.beats[bi];
    const tooLate = beat / 2 - 0.06 + 0.004;
    expect(tooLate).toBeLessThan(hero.timing.greatMs / 1000);
    const onTime = cleaveAt(accent, tap(accent + 0.05));
    const late = cleaveAt(accent, tap(accent + tooLate));
    expect(onTime.bands[0]).toBeCloseTo(accent + beat / 2, 3);
    expect(late.bands[0]).toBeCloseTo(accent + (3 * beat) / 4, 3);
    expect(late.bands[0]! - late.wave!.press!).toBeGreaterThanOrEqual(0.06 - 1e-6);
  });

  it("brings a hit-stop when it comes before the impact, and none when it comes after", () => {
    const accent = sch.gridSlots.find((g) => g.s > 60 && cleaves(ctx, g))!;
    const early = cleaveAt(accent.s, tap(accent.s - 0.03));
    const late = cleaveAt(accent.s, tap(accent.s + 0.03));
    expect(early.wave).toBeDefined();
    expect(late.wave).toBeDefined();
    expect(early.stops.length).toBe(1);
    expect(early.stops[0][1] - early.stops[0][0]).toBeCloseTo(Math.min(0.15, 0.11 * (0.6 + 0.4 * accent.accent)), 3);
    expect(late.stops).toEqual([]);
  });

  it("rated only Good gets the heavy impact without a wave, as does no press at all", () => {
    const accent = sch.gridSlots.find((g) => g.s > 60 && cleaves(ctx, g))!.s;
    const good = cleaveAt(accent, tap(accent + 0.12));
    expect(good.swing.kind).toBe("heavy");
    expect(good.wave).toBeUndefined();
    const none = cleaveAt(accent, []);
    expect(none.swing.kind).toBe("heavy");
    expect(none.wave).toBeUndefined();
  });

  it("a Perfect stomp on the chord counts like any press", () => {
    const accent = sch.gridSlots.find((g) => g.s > 60 && cleaves(ctx, g))!.s;
    expect(cleaveAt(accent, tap(accent + 0.01, "space")).wave).toBeDefined();
  });
});

describe("a free section's strong anchor", () => {
  it("gets no cleave: free movement swings only light, with no shockwave", () => {
    const prelude = section("prelude");
    expect(ctx.sections.find((s) => s.label === "prelude")!.song.anchors!.some((a) => a.strength >= 0.5)).toBe(true);
    const r = new BatchRecorder();
    run([], [prelude.s1 - 0.01], r);
    const swings = r.swings.filter((sw) => sw.s > prelude.s0 && sw.s < prelude.s1);
    expect(swings.length).toBeGreaterThan(10);
    expect(swings.every((sw) => sw.kind === "light")).toBe(true);
    expect(r.waves).toEqual([]);
  });
});

describe("the impacts-on-the-grid check", () => {
  it("fails an eligible accent whose heavy impact sound is missing", () => {
    const p = runPlanner({ level: "levels/orpheus-galop.play.yaml" });
    const lib = plannerInput({ level: "levels/orpheus-galop.play.yaml" }).content;
    const result = () => checkPlan(p, lib).find((c) => c.name === "impacts on the grid")!;
    expect(result().failures).toEqual([]);
    for (const snd of p.events.sounds) if (snd.key === "hit-heavy") snd.key = "hit-light";
    expect(result().failures.some((f) => /has no heavy impact sound on it/.test(f))).toBe(true);
  });
});
