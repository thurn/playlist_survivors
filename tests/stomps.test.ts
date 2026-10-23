/**
 * Stomps (play mode): "Standing still: stomps" in docs/design/gameplay-redesign.md, through the
 * simulation on the synthetic song and on a copy of it with a slow climax.
 */
import { describe, expect, it } from "vitest";
import { heldSpans } from "../src/planner/checks.ts";
import { cleaves } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { BatchRecorder } from "../src/sim/recorder.ts";
import { strideSteps, stompCancelsUntil } from "../src/sim/strides.ts";
import { play, slowSection, tap } from "./fixtures/play-sim.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const space = (s: number): KeyEvent[] => tap(s, "space");
const eps = 0.001;

describe("a stomp's cancelled held strides", () => {
  it("are the next grid step's: one held stride, or two where the stride period is halved", () => {
    // Steps 0 | 0.3, 0.65 (a 0.7 s grid step, halved) | 1.0, 1.3845 | 1.769, ...
    const st = strideSteps([0, 0.3, 1.0, 1.769], 0.4);
    expect(stompCancelsUntil(st, 0.1)).toBeCloseTo(0.3, 9);
    expect(stompCancelsUntil(st, 0.29)).toBeCloseTo(0.3, 9);
    expect(stompCancelsUntil(st, 0.3)).toBeCloseTo(0.65, 9);
    expect(stompCancelsUntil(st, 0.7)).toBeCloseTo(1.3845, 9);
    expect(stompCancelsUntil(st, 0.99)).toBeCloseTo(1.3845, 9);
  });
});

describe("the movement checks' held keys", () => {
  it("are directions only: holding Space holds no direction", () => {
    const at = (s: number, kind: "press" | "release", key: "space" | "right") => ({ s, w: s, kind, key, steps: true });
    expect(heldSpans([at(1, "press", "space"), at(2, "press", "right"), at(3, "release", "right"), at(9, "release", "space")])).toEqual([[2, 3]]);
  });
});

describe("stomps at 185 beats per minute (theme A)", () => {
  const { sch, section, run } = play(syntheticSongMap());
  const sec = section("theme-a");
  const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 0.5 && g < sec.s0 + 6);
  const one = 140 * (beats[1] - beats[0]);

  it("stamp in place with Space: the hero does not move, and the stomp is a rated press", () => {
    const r = run(space(beats[1] - 0.01), [beats[1] - 0.05, beats[1] + 0.03, beats[1] + 0.3]);
    expect(r.at[1]).toMatchObject({ x: r.at[0].x, y: r.at[0].y });
    expect(r.at[2]).toMatchObject({ x: r.at[0].x, y: r.at[0].y });
    expect(r.inputs).toHaveLength(1);
    expect(r.inputs[0]).toMatchObject({ key: "space", stomp: true, rating: "perfect" });
    expect(r.inputs[0].rock).toBeUndefined();
    expect(r.inputs[0].stride).toBeUndefined();
  });

  it("never moves a hero that holds Right and stomps with Space on every beat, a little early or late", () => {
    // Right pressed and held between beats strides once; from then on, Space on every beat.
    const keys: KeyEvent[] = [{ s: beats[0] + 0.1, kind: "press", key: "right" }];
    const offsets = [0, -0.015, 0.02, -0.03, 0.01, 0, 0.025, -0.01];
    for (let k = 1; k <= offsets.length; k++) keys.push(...space(beats[k] + offsets[k - 1]));
    const probes = beats.slice(1, offsets.length + 2).map((b) => b - 0.05);
    const r = run(keys, [beats[0] + 0.05, ...probes]);
    // The press strides a full stride; then nothing moves the hero.
    expect(r.at[1].x - r.at[0].x).toBeCloseTo(one, 6);
    for (const p of r.at.slice(2)) expect(Math.abs(p.x - r.at[1].x)).toBeLessThan(1e-6);
    expect(r.inputs.filter((i) => i.key === "space").every((i) => i.stomp)).toBe(true);
  });

  it("returns a rock step to where the press began, even when a held beat took over in between", () => {
    // Left held from 20 ms before a beat: the beat's held stride takes over from the press's. Right 5 ms
    // after the beat still makes the pair one stomp, and the hero comes back to where Left began.
    const b = beats[2];
    const keys: KeyEvent[] = [
      { s: b - 0.02, kind: "press", key: "left" },
      { s: b + 0.005, kind: "press", key: "right" },
      { s: b + 0.06, kind: "release", key: "left" },
      { s: b + 0.07, kind: "release", key: "right" },
    ];
    const r = run(keys, [b - 0.02 - eps, b + 0.3]);
    expect(Math.hypot(r.at[1].x - r.at[0].x, r.at[1].y - r.at[0].y)).toBeLessThan(1e-6);
    expect(r.inputs[1]).toMatchObject({ key: "right", stomp: true, rock: true });
  });

  it("lets a direction pressed soon after a stomp stride at once, never as a diagonal partner", () => {
    // Space, then Up 20 ms later; and Left, Right (a rock step), then Up 20 ms later.
    const b = beats[2];
    const afterStamp = run([...space(b), ...tap(b + 0.02, "up")], [b - eps, b + 0.3]);
    expect(afterStamp.inputs[1]).toMatchObject({ key: "up", stride: expect.any(Number) });
    expect(afterStamp.inputs[1].bend).toBeUndefined();
    expect(afterStamp.at[0].y - afterStamp.at[1].y).toBeGreaterThan(10);
    const afterRock = run([...tap(b, "left"), ...tap(b + 0.02, "right"), ...tap(b + 0.04, "up")], [b - eps, b + 0.3]);
    expect(afterRock.inputs[2].bend).toBeUndefined();
    expect(afterRock.inputs[2].stride).toBeGreaterThan(0);
  });

  it("stamps again, with a fresh hop, for Space pressed again while the first stamp still hops", () => {
    const b = beats[2];
    const r = run([{ s: b, kind: "press", key: "space" }, { s: b + 0.01, kind: "release", key: "space" }, ...space(b + 0.03)], [b + 0.2]);
    expect(r.inputs.map((i) => [i.stomp, !!i.rock])).toEqual([
      [true, false],
      [true, false],
    ]);
  });

  it("makes one stomp of Left then Right 25 ms later, timed by Left, with a rock step out and back", () => {
    const at = beats[2] + 0.02;
    const keys: KeyEvent[] = [
      { s: at, kind: "press", key: "left" },
      { s: at + 0.025, kind: "press", key: "right" },
      { s: at + 0.06, kind: "release", key: "left" },
      { s: at + 0.07, kind: "release", key: "right" },
    ];
    const r = run(keys, [at - eps, at + 0.04, at + 0.2, beats[3] + 0.2]);
    const [start, out, back, later] = r.at;
    expect(start.x - out.x).toBeGreaterThan(10);
    expect(Math.abs(back.x - start.x)).toBeLessThan(1e-6);
    expect(Math.abs(later.x - start.x)).toBeLessThan(1e-6);
    expect(r.inputs.map((i) => [i.key, !!i.stomp, !!i.rock, i.rating])).toEqual([
      ["left", false, false, "perfect"],
      ["right", true, true, undefined],
    ]);
  });

  it("stomps three times in the closing-ring example, with no net movement", () => {
    // The design's example, on this song's beats: Space 10 ms early, Left then Right 25 ms apart and
    // 60 ms late, Space on the chord 5 ms early. (Beats b0, b1, b2 stand for 61.616, 61.958, 62.300.)
    const [b0, b1, b2] = beats.slice(3, 6);
    const keys: KeyEvent[] = [
      ...space(b0 - 0.01),
      { s: b1 + 0.06, kind: "press", key: "left" },
      { s: b1 + 0.085, kind: "press", key: "right" },
      { s: b1 + 0.12, kind: "release", key: "left" },
      { s: b1 + 0.13, kind: "release", key: "right" },
      ...space(b2 - 0.005),
    ];
    const r = run(keys, [b0 - 0.05, b2 + 0.2]);
    expect(Math.hypot(r.at[1].x - r.at[0].x, r.at[1].y - r.at[0].y)).toBeLessThan(1e-6);
    // Three stomps: Space, the opposite pair (one stomp, rated by Left), and Space.
    const own = r.inputs.filter((i) => !(i.rock && i.key !== "space"));
    expect(own.map((i) => [i.key, i.rating])).toEqual([
      ["space", "perfect"],
      ["left", "great"],
      ["space", "perfect"],
    ]);
    expect(r.inputs.filter((i) => i.stomp).length).toBe(3);
  });

  it("rocks a stride still moving back to where it began, and stamps a ring of dust without a sound", () => {
    const rec = new BatchRecorder();
    const r = run([...tap(beats[2]), ...space(beats[2] + 0.04)], [beats[2] - eps, beats[2] + 0.3], rec);
    expect(Math.abs(r.at[1].x - r.at[0].x)).toBeLessThan(1e-6);
    expect(r.inputs[1]).toMatchObject({ key: "space", stomp: true, rock: true });
    // Its only landing is the stomp's ring of dust (no stride landing, so no rustle after it).
    expect(rec.landings.filter((l) => l.s > beats[2] - eps).map((l) => [l.s, !!l.stomp])).toEqual([[beats[2] + 0.04, true]]);
    // A direction pressed after the stomp still strides at once, with the ground given back.
    const next = run([...tap(beats[2]), ...space(beats[2] + 0.04), ...tap(beats[2] + 0.2, "up")], [beats[2] + 0.4]);
    expect(next.inputs[2].stride).toBeGreaterThan(one / 2);
  });
});

describe("stomps on a slow grid", () => {
  // The climax at 78 beats per minute: held strides fall on every eighth note, and a stomp cancels two.
  const { sch, section, run } = play(slowSection(syntheticSongMap(), "climax", 78));
  const sec = section("climax");
  const beats = Array.from(sch.gridTimes).filter((g) => g > sec.s0 + 1 && g < sec.s1 - 1);

  it("never moves a hero that holds Right and stomps with Space on every beat", () => {
    const keys: KeyEvent[] = [{ s: beats[0] + 0.2, kind: "press", key: "right" }];
    for (let k = 1; k <= 5; k++) keys.push(...space(beats[k] - 0.01));
    const probes = beats.slice(1, 7).flatMap((b, i) => [b - 0.05, (b + (beats[i + 2] ?? b + 0.77)) / 2 + 0.05]);
    const r = run(keys, [beats[0] + 0.5, ...probes.slice(0, 11)]);
    for (const p of r.at.slice(1)) expect(Math.abs(p.x - r.at[0].x)).toBeLessThan(1e-6);
    // Without the stomps, the same hold strides on every eighth note.
    const held = run([{ s: beats[0] + 0.2, kind: "press", key: "right" }], [beats[0] + 0.5, beats[3] - 0.05]);
    expect(held.at[1].x - held.at[0].x).toBeGreaterThan(0.8 * 140 * (beats[3] - beats[0] - 0.55));
  });
});

describe("the stomp window", () => {
  const { ctx, sch, run } = play(syntheticSongMap());
  // A cleave's hit-stop: a Perfect counted press just before an accent the level cleaves brings one at the impact.
  const i = sch.gridSlots.findIndex((g) => g.s > 60 && cleaves(ctx, g));
  const accent = sch.gridSlots[i].s;
  const before = sch.gridSlots[i - 1].s;

  it("counts song time: opposite keys 60 ms apart are no stomp, though a hit-stop froze the world between them", () => {
    const rec = new BatchRecorder();
    const keys: KeyEvent[] = [...tap(before), { s: accent - 0.03, kind: "press", key: "left" }, { s: accent + 0.03, kind: "press", key: "right" }];
    const r = run(keys, [accent + 0.3], rec);
    expect(rec.hitStops.some(([s0]) => Math.abs(s0 - accent) < 0.02)).toBe(true);
    const right = r.inputs.find((k) => k.key === "right" && k.s > accent)!;
    expect(right.rock).toBeUndefined();
    expect(right.stride).toBeGreaterThan(0);
  });
});

describe("a stomp just before a pulsed section", () => {
  const { sch, section, run } = play(syntheticSongMap());

  it("counts at its first grid time and cancels the held stride there, as mid-section", () => {
    // Right held through the end of the prelude (walking), Space 20 ms before the galop vamp's first grid time.
    const vamp = section("galop-vamp");
    const g = Array.from(sch.gridTimes).filter((t) => t >= vamp.s0 - 1e-6);
    const keys: KeyEvent[] = [{ s: g[0] - 1, kind: "press", key: "right" }, ...space(g[0] - 0.02)];
    const r = run(keys, [g[0] - 0.01, g[0] + 0.2, g[1] + 0.2]);
    expect(r.inputs.find((i) => i.key === "space")).toMatchObject({ steps: true, class: "counted", rating: "perfect", stomp: true });
    // The walk stopped at the stomp, the held stride on the first grid time is cancelled, and the next one strides.
    expect(Math.hypot(r.at[1].x - r.at[0].x, r.at[1].y - r.at[0].y)).toBeLessThan(eps);
    expect(r.at[2].x - r.at[1].x).toBeGreaterThan(20);
  });
});
