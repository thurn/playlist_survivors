import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { prepare, virtualPlayer } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import type { Recorder } from "../src/sim/recorder.ts";
import { DEMO_RULES } from "../src/sim/rules.ts";
import { Sim, simulate } from "../src/sim/simulate.ts";
import { CorridorWorld } from "../src/sim/world.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

/** A recorder that logs a hash of every event (its name and arguments); it has no clock. */
function logRecorder(log: number[]): Recorder {
  const record = (name: string | symbol) => (...args: unknown[]) => {
    const text = JSON.stringify([name, args]);
    let h = 0x811c9dc5;
    for (let i = 0; i < text.length; i++) h = Math.imul(h ^ text.charCodeAt(i), 0x01000193);
    log.push(h >>> 0);
  };
  return new Proxy({} as Recorder, { get: (_t, name) => (name === "clock" ? undefined : record(name)) });
}

/** The index of the first difference between two logs, or -1. */
function firstDifference(a: number[], b: number[]): number {
  for (let i = 0; i < Math.max(a.length, b.length); i++) if (a[i] !== b[i]) return i;
  return -1;
}

describe("the steppable engine", () => {
  // (The old demo's rules in the corridor, the legacy demo level, which DEMO_RULES and
  // CorridorWorld stand for until hv-zoon removes them.)
  const input = plannerInput({ songMap: syntheticSongMap(), level: "tests/fixtures/legacy-demo.level.yaml" });
  const { ctx, sch, map } = prepare(input, propsFromAtlas());
  const world = new CorridorWorld(map, sch);
  const batch = simulate(ctx, sch, map, { player: virtualPlayer(ctx, sch) });
  const keys: KeyEvent[] = batch.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key }));

  /** The whole run's log, with the player's presses fed in as key events. */
  const scriptedLog = (advance: (sim: Sim) => void) => {
    const log: number[] = [];
    const sim = new Sim(ctx, sch, world, DEMO_RULES, logRecorder(log));
    for (const k of keys) sim.key(k);
    advance(sim);
    return log;
  };
  const whole = scriptedLog((sim) => sim.runToEnd());

  it("records the same run from the player's key events as from the player", () => {
    const log: number[] = [];
    const sim = new Sim(ctx, sch, world, DEMO_RULES, logRecorder(log), { player: virtualPlayer(ctx, sch) });
    sim.runToEnd();
    expect(firstDifference(log, whole)).toBe(-1);
  });

  it("records the same run however it is advanced", () => {
    const framed = scriptedLog((sim) => {
      for (let t = 0; !sim.done; t += 1 / 60) sim.advanceTo(t);
      sim.runToEnd();
    });
    expect(firstDifference(framed, whole)).toBe(-1);
  });

  it("applies keys pushed just in time like keys known in advance", () => {
    const log: number[] = [];
    const sim = new Sim(ctx, sch, world, DEMO_RULES, logRecorder(log));
    let next = 0;
    for (let t = 0; !sim.done; t += 1 / 60) {
      // Every key whose time falls in this frame arrives at the frame's start.
      while (next < keys.length && keys[next].s < t + 1 / 60) sim.key(keys[next++]);
      sim.advanceTo(t + 1 / 60);
    }
    sim.runToEnd();
    expect(firstDifference(log, whole)).toBe(-1);
  });

  it("clones a snapshot that runs on exactly like the original and shares nothing with it", () => {
    const head: number[] = [];
    const a = new Sim(ctx, sch, world, DEMO_RULES, logRecorder(head));
    for (const k of keys) a.key(k);
    const cuts = [40, 95, 130];
    const clones: { tail: number[]; sim: Sim; at: number }[] = [];
    for (const t of cuts) {
      a.advanceTo(t);
      const tail: number[] = [];
      clones.push({ tail, sim: a.clone(logRecorder(tail)), at: head.length });
    }
    a.runToEnd();
    // Run the clones last, after the original has moved on: the original's run cannot have changed them.
    for (const c of clones) {
      c.sim.runToEnd();
      expect(firstDifference(c.tail, head.slice(c.at))).toBe(-1);
    }
    expect(firstDifference(head, whole)).toBe(-1);
  });
});
