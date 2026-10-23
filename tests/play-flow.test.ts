import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { Rng } from "../src/core/rng.ts";
import { liveSession } from "../src/live/session.ts";
import { gradeFor } from "../src/plan/grade.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { replay } from "../src/runtime/play.ts";
import { bestGradeKey, calibrationGroup, calibrationOffset, loadBestGrade, saveBestGrade } from "../src/runtime/title.ts";
import { gameKeyOf, inputOffsetFor, INPUT_OFFSET_KEY, KeyboardInput, loadInputOffset, saveInputOffset, STOMP_EXTRA_KEY } from "../src/live/input.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
const props = propsFromAtlas();

describe("watch my run", () => {
  it("replays a mid-song start with a restarted section exactly as it was played", () => {
    const startS = 70;
    const session = liveSession(input, { props, startS });
    // Presses on and off the beat, arriving up to 16 ms late.
    const r = new Rng(3, "keys");
    const keys: KeyEvent[] = [];
    const beats = session.recording.plan.songBeats;
    const dirs = ["right", "down", "left", "up"] as const;
    for (let i = 0; i < beats.length; i++) {
      if (beats[i] < startS + 0.4 || beats[i] > startS + 40) continue;
      const key = dirs[i % 4];
      const s = beats[i] + (r.next() - 0.5) * 0.08;
      keys.push({ s, kind: "press", key }, { s: s + 0.07, kind: "release", key });
    }
    let next = 0;
    let restarted = false;
    for (let now = startS + 0.0037; now < startS + 45; now += 1 / 60) {
      const due: KeyEvent[] = [];
      while (next < keys.length && keys[next].s + r.next() * 0.016 <= now) due.push(keys[next++]);
      session.runner.frame(now, due);
      // Halfway through the theme, the player restarts the section and plays it again.
      if (!restarted && now > startS + 20) {
        restarted = true;
        const s0 = session.runner.restartSection()!;
        expect(s0).toBeLessThan(now);
        next = keys.findIndex((k) => k.s >= s0);
        for (let t = s0; t < now; t += 1 / 60) {
          const again: KeyEvent[] = [];
          while (next < keys.length && keys[next].s <= t) again.push(keys[next++]);
          session.runner.frame(t, again);
        }
      }
    }
    session.runner.finish();
    expect(restarted).toBe(true);
    const again = replay(input, props, startS, session.runner.applied);
    expect(again.hashParts()).toEqual(session.recording.hashParts());
  }, 120_000);
});

describe("calibration", () => {
  const clicks = Array.from({ length: 8 }, (_, k) => 10 + k * 0.5);

  it("is the median tap error after the first two taps", () => {
    const taps = clicks.map((c, k) => c + [0.2, -0.1, 0.03, 0.025, 0.04, 0.035, 0.02, 0.03][k]);
    expect(calibrationOffset(taps, clicks)).toBeCloseTo(30, 6);
  });

  it("needs taps that aim at the clicks", () => {
    expect(calibrationOffset([10, 10.5, 10.75, 11.25], clicks)).toBeUndefined();
  });

  it("is for the kind of key most taps came from", () => {
    expect(calibrationGroup(["stomp", "stomp", "direction"])).toBe("stomp");
    expect(calibrationGroup(["stomp", "direction"])).toBe("direction");
    expect(calibrationGroup([])).toBe("direction");
  });

  /** Run `body` with a fake localStorage. */
  const withStorage = (body: (store: Map<string, string>) => void) => {
    const store = new Map<string, string>();
    const fake = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v), removeItem: (k: string) => void store.delete(k), clear: () => store.clear(), key: () => null, length: 0 };
    const was = (globalThis as { localStorage?: unknown }).localStorage;
    (globalThis as { localStorage?: unknown }).localStorage = fake;
    try {
      body(store);
    } finally {
      (globalThis as { localStorage?: unknown }).localStorage = was;
    }
  };

  it("keeps the stomp key's offset apart, the directions' standing in until it has its own", () =>
    withStorage((store) => {
      expect(loadInputOffset("stomp")).toBe(0);
      saveInputOffset(-56);
      expect([inputOffsetFor("left"), inputOffsetFor("space")]).toEqual([-56, -56]);
      saveInputOffset(-120.4, "stomp");
      expect([inputOffsetFor("up"), inputOffsetFor("space")]).toEqual([-56, -120]);
      // Saved as its difference from the directions': recalibrating the arrows (new headphones) moves both.
      expect([store.get(INPUT_OFFSET_KEY), store.get(STOMP_EXTRA_KEY)]).toEqual(["-56", "-64"]);
      saveInputOffset(94);
      expect([inputOffsetFor("up"), inputOffsetFor("space")]).toEqual([94, 30]);
    }));
});

describe("the keyboard", () => {
  /** A keyboard on a fake window: key events at song time s (ms of performance time are s × 1000). */
  const keyboard = (offset: (k: string) => number = () => 0) => {
    const on: Record<string, (e: unknown) => void> = {};
    const kb = new KeyboardInput((ms) => ms / 1000, offset);
    kb.attach({ addEventListener: (type: string, fn: (e: unknown) => void) => (on[type] = fn) } as unknown as Window);
    const send = (type: "keydown" | "keyup", code: string, key: string, s: number) => on[type]({ code, key, timeStamp: s * 1000, repeat: false, preventDefault: () => {} });
    return { kb, send };
  };

  it("takes each key's own calibration offset", () => {
    const { kb, send } = keyboard((k) => (k === "space" ? 120 : 50));
    send("keydown", "ArrowLeft", "ArrowLeft", 10);
    send("keydown", "KeyZ", "z", 11);
    expect(kb.drain().map((e) => [e.key, Math.round(e.s * 1000)])).toEqual([
      ["left", 9950],
      ["space", 10880],
    ]);
  });

  it("lets Z go down while Space is held, and each stomp key come up on its own", () => {
    const { kb, send } = keyboard();
    send("keydown", "Space", " ", 1);
    send("keydown", "KeyZ", "z", 1.3);
    send("keyup", "KeyZ", "z", 1.35);
    send("keydown", "KeyZ", "z", 1.6);
    send("keyup", "Space", " ", 1.7);
    send("keyup", "KeyZ", "z", 1.8);
    expect(kb.drain().map((e) => `${e.kind}@${e.s}`)).toEqual(["press@1", "press@1.3", "release@1.35", "press@1.6", "release@1.7", "release@1.8"]);
  });
});

describe("the stomp key", () => {
  it("is Z (by position) or Space", () => {
    expect([{ key: "z", code: "KeyZ" }, { key: "w", code: "KeyZ" }, { key: " ", code: "Space" }].map(gameKeyOf)).toEqual(["space", "space", "space"]);
    expect(gameKeyOf({ key: "x", code: "KeyX" })).toBeUndefined();
  });
});

describe("the grade", () => {
  it("comes from the average groove", () => {
    expect([0.96, 0.94, 0.93, 0.85, 0.8, 0.75, 0.5, 0.49, 0.1].map(gradeFor)).toEqual(["S", "S", "A", "A", "B", "B", "B", "C", "C"]);
  });

  it("is saved under a new key: a best grade from before the redesign is ignored, and the calibration offset kept", () => {
    const store = new Map<string, string>();
    const fake = { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v), removeItem: (k: string) => void store.delete(k), clear: () => store.clear(), key: () => null, length: 0 };
    const was = (globalThis as { localStorage?: unknown }).localStorage;
    (globalThis as { localStorage?: unknown }).localStorage = fake;
    try {
      store.set("ps.best.orpheus-galop.play", "S");
      store.set(INPUT_OFFSET_KEY, "42");
      expect(loadBestGrade("orpheus-galop.play")).toBeUndefined();
      saveBestGrade("orpheus-galop.play", "B");
      expect(loadBestGrade("orpheus-galop.play")).toBe("B");
      expect(store.get(bestGradeKey("orpheus-galop.play"))).toBe("B");
      expect(store.get("ps.best.orpheus-galop.play")).toBe("S");
      expect(store.get(INPUT_OFFSET_KEY)).toBe("42");
    } finally {
      (globalThis as { localStorage?: unknown }).localStorage = was;
    }
  });
});
