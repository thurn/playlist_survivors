/**
 * The display over the hero's head: where it lands on screen, what its wings
 * show, and whether its gem keeps the beat or breathes in free movement.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, runPlanner } from "../scripts/lib/plan-node.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import { HEAD_HEIGHT, OVERHEAD_GAP, OVERHEAD_HALF_W, overheadState } from "../src/runtime/overhead.ts";
import { VIEW_H, VIEW_W, worldToScreen } from "../src/runtime/view.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const songMap = syntheticSongMap();
const PLAY = "levels/orpheus-galop.play.yaml";
const lib = plannerInput({ songMap, level: PLAY }).content;
const plan = runPlanner({ songMap, level: PLAY });
const ev = new Evaluator(plan, lib);
/** A 1920×1080 screen: two screen pixels per HUD unit. */
const out = { x: 0, y: 0, w: 1920, h: 1080 };
const camera = { x: 1000, y: 500, zoom: 1, shakeX: 0, shakeY: 0 };

describe("the world view's projection", () => {
  it("puts the camera's centre at the screen's centre, and scales with the zoom", () => {
    expect(worldToScreen(camera, out, 1000, 500)).toMatchObject({ x: 960, y: 540, scale: 2 });
    expect(worldToScreen(camera, out, 1000 + VIEW_W / 2, 500 - VIEW_H / 2)).toMatchObject({ x: 1920, y: 0 });
    const zoomed = worldToScreen({ ...camera, zoom: 1.5 }, out, 1010, 500);
    expect(zoomed.scale).toBeCloseTo(3);
    expect(zoomed.x).toBeCloseTo(990);
  });

  it("follows the camera's shake, and the letterbox's offset", () => {
    expect(worldToScreen({ ...camera, shakeX: 4 }, out, 1000, 500).x).toBeCloseTo(952);
    expect(worldToScreen(camera, { ...out, x: 100 }, 1000, 500).x).toBeCloseTo(1060);
  });
});

describe("the overhead display", () => {
  const pulsed = plan.sections.find((s) => s.feel === "pulsed")!;
  const free = plan.sections.find((s) => s.feel === "free" && s.s1 - s.s0 > 8)!;
  const hero = (x: number, y: number, z = 0) => ({ x, y, z });

  it("floats over the hero's head, and stays inside the view near its edges", () => {
    const f = ev.evaluate(pulsed.s0 + 2);
    const o = overheadState({ s: f.s, camera, hero: hero(1000, 500) }, f.hud, out)!;
    expect(o.x).toBeCloseTo(960);
    expect(o.y).toBeCloseTo(540 - HEAD_HEIGHT * 2 - OVERHEAD_GAP * 2);
    // (A jump lifts it with the hero.)
    expect(overheadState({ s: f.s, camera, hero: hero(1000, 500, 10) }, f.hud, out)!.y).toBeCloseTo(o.y - 20);
    const edge = overheadState({ s: f.s, camera, hero: hero(1000 - VIEW_W / 2, 500) }, f.hud, out)!;
    expect(edge.x).toBeCloseTo(OVERHEAD_HALF_W * 2);
  });

  it("shows life and groove, warns when life is low, and is gone on the end card", () => {
    const f = ev.evaluate(pulsed.s0 + 2);
    const o = overheadState({ s: f.s, camera, hero: hero(1000, 500) }, { ...f.hud, health: 0.8, groove: 0.6 }, out)!;
    expect(o).toMatchObject({ health: 0.8, groove: 0.6, danger: 0, tier: f.hud.tier.n });
    expect(overheadState({ s: f.s, camera, hero: hero(1000, 500) }, { ...f.hud, health: 0.15 }, out)!.danger).toBeCloseTo(0.5);
    expect(overheadState({ s: f.s, camera, hero: hero(1000, 500) }, { ...f.hud, health: 1.4, groove: -1 }, out)).toMatchObject({ health: 1, groove: 0 });
    const end = ev.evaluate(plan.duration + 1);
    expect(end.hud.end).toBeDefined();
    expect(overheadState({ s: end.s, camera, hero: hero(1000, 500) }, end.hud, out)).toBeUndefined();
  });

  it("strikes each beat with a steady beat, and breathes with the bar in free movement", () => {
    const beat = plan.songBeats.find((b) => b > pulsed.s0 + 2)!;
    const at = (s: number) => overheadState({ s, camera, hero: hero(1000, 500) }, ev.evaluate(s).hud, out)!;
    const on = at(beat + 0.005);
    expect(on.step).toBe(1);
    expect(on.hit).toBeGreaterThan(0.9);
    // (Just before the next beat the ring has nearly closed and the gem has settled.)
    const next = plan.songBeats.find((b) => b > beat + 0.01)!;
    const before = at(next - 0.02);
    expect(before.beatPhase).toBeGreaterThan(0.9);
    expect(before.hit).toBeLessThan(0.05);

    const loose = at((free.s0 + free.s1) / 2);
    expect(loose.step).toBeLessThan(1);
    expect(loose.breathe).toBeGreaterThanOrEqual(0);
    expect(loose.breathe).toBeLessThanOrEqual(1);
    expect(loose.spin).toBeGreaterThanOrEqual(0);
    expect(loose.spin).toBeLessThan(1);
  });

  it("telegraphs the beat to press for a cleave and the downbeats on the closing ring", () => {
    const accent = plan.cleaveAccents[0];
    const prev = plan.songBeats.filter((b) => b < accent.s - 0.05).at(-1)!;
    const f = ev.evaluate(prev + 0.05);
    expect(overheadState({ s: f.s, camera, hero: hero(1000, 500) }, f.hud, out)!.next).toBe("press");
    const ticks = [{ dt: -0.05, down: true }, { dt: 0.3, down: true }];
    expect(overheadState({ s: f.s, camera, hero: hero(1000, 500) }, { ...f.hud, ticks }, out)!.next).toBe("down");
    expect(overheadState({ s: f.s, camera, hero: hero(1000, 500) }, { ...f.hud, ticks: [{ dt: 0.3, down: false }] }, out)!.next).toBe("beat");
  });
});
