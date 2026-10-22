import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { pressRows, summarize, type PlayLogFile } from "../scripts/lib/play-log.ts";
import { PlayLog } from "../src/live/log.ts";
import { liveSession } from "../src/live/session.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
const props = propsFromAtlas();

describe("play mode's timing log", () => {
  it("follows each key from the keyboard through the runner to what the simulation did", () => {
    const probe = liveSession(input, { props });
    const sec = probe.ctx.sections.find((x) => x.level.movement === "steps" && x.s0 > 5)!;
    const grid = Array.from(probe.recording.sch.gridTimes).filter((g) => g > sec.s0 + 1 && g < sec.s1 - 1);
    const startS = grid[0] - 0.5;
    const session = liveSession(input, { props, startS });
    const log = new PlayLog(new Date(0));
    session.recording.pressLog = (t) => log.press(t);
    session.runner.log = log;

    // A step on a grid time, a second press for the same grid time (an unrated extra step), then a step on the next.
    const [g0, g1] = grid;
    const keys: KeyEvent[] = [
      { s: g0 + 0.01, kind: "press", key: "right" },
      { s: g0 + 0.05, kind: "release", key: "right" },
      { s: g0 + 0.06, kind: "press", key: "up" },
      { s: g0 + 0.08, kind: "release", key: "up" },
      { s: g1 - 0.02, kind: "press", key: "left" },
    ];
    for (const k of keys) {
      k.id = log.newKeyId();
      log.key({ id: k.id, type: k.kind === "press" ? "down" : "up", key: k.key, code: "", timeStamp: k.s * 1000, handledAt: k.s * 1000 + 2, repeat: false, fate: "queued", s: k.s });
    }
    // Each key reaches the game one frame late, so the simulation (running ahead) rolls back for it.
    let next = 0;
    for (let now = startS; now < g1 + 0.5; now += 1 / 60) {
      const due: KeyEvent[] = [];
      while (next < keys.length && keys[next].s + 1 / 60 <= now) due.push(keys[next++]);
      session.runner.frame(now, due);
    }

    const traces = keys.map((k) => log.traces.get(k.id!));
    expect(traces.map((t) => t?.outcome)).toEqual(["step", "release", "extra", "release", "step"]);
    expect(traces[0]!.gridS).toBeCloseTo(g0, 9);
    expect(traces[0]!.delta).toBeCloseTo(0.01, 9);
    expect(traces[0]!.strikeS).toBeGreaterThan(g0);
    expect(traces[2]!.stepS).toBeCloseTo(g0 + 0.01, 9);
    expect(traces[4]!.delta).toBeCloseTo(-0.02, 9);
    expect(log.applies.length).toBe(keys.length);
    expect(log.applies.some((a) => a.rollbackTo !== undefined)).toBe(true);

    // The report, from the saved form.
    const file = JSON.parse(JSON.stringify({ ...log.toJSON(), beats: Array.from(session.recording.plan.songBeats) })) as PlayLogFile;
    const rows = pressRows(file);
    expect(rows.map((r) => r.outcome)).toEqual(["step", "extra", "step"]);
    expect(rows[1].sinceStepMs).toBeCloseTo(50, 6);
    const text = summarize(file);
    expect(text).toContain("Presses: 3");
    expect(text).toMatch(/extra\s+1/);
  });

  it("reports keys the keyboard never handed to the game", () => {
    const log = new PlayLog(new Date(0));
    log.key({ id: log.newKeyId(), type: "down", key: "up", code: "ArrowUp", timeStamp: 10, handledAt: 11, repeat: false, fate: "held" });
    log.key({ id: log.newKeyId(), type: "down", key: "up", code: "ArrowUp", timeStamp: 20, handledAt: 21, repeat: false, fate: "queued", s: 1 });
    log.drop({ id: 2, s: 1, phase: "countIn" });
    const rows = pressRows(JSON.parse(JSON.stringify(log)) as PlayLogFile);
    expect(rows.map((r) => r.outcome)).toEqual(["held", "dropped (countIn)"]);
  });
});
