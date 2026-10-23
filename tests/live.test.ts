import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { Rng } from "../src/core/rng.ts";
import { songTimeAtPerformance } from "../src/live/input.ts";
import { LiveRecording } from "../src/live/recording.ts";
import { liveSession } from "../src/live/session.ts";
import { LiveSoundScheduler, soundKey, type SoundOutput } from "../src/live/sounds.ts";
import { Evaluator } from "../src/plan/evaluate.ts";
import type { SoundEvent } from "../src/plan/types.ts";
import { prepare, virtualPlayer } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { Sim, worldFor } from "../src/sim/simulate.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const LEVEL = "levels/orpheus-galop.play.yaml";
const songMap = syntheticSongMap();
const input = plannerInput({ songMap, level: LEVEL });
const props = propsFromAtlas();
const { ctx, sch, map } = prepare(input);

/** A batch run (no rollback) of the given key events, into a fresh live recording, up to song time `until`. */
function batch(keys: readonly KeyEvent[], until = Infinity): { recording: LiveRecording; sim: Sim } {
  const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", props);
  const sim = new Sim(ctx, sch, worldFor(), recording);
  for (const k of keys) sim.key(k);
  if (Number.isFinite(until)) sim.advanceTo(until);
  else sim.runToEnd();
  return { recording, sim };
}

// Realistic presses: the moderate player's, from a headless run. Then extra presses that land inside
// hit-stops (a press ends a hit-stop early), mashed presses, and stomps: Space, and opposite keys.
const bot = (() => {
  const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", props);
  const sim = new Sim(ctx, sch, worldFor(), recording, { player: virtualPlayer(ctx, sch, "moderate") });
  sim.runToEnd();
  return recording;
})();
const keys: KeyEvent[] = bot.plan.inputs.map((i) => ({ s: i.s, kind: i.kind, key: i.key }));
{
  const stops = bot.plan.warp.filter((b, i, all) => b.rate === 0 && i + 1 < all.length).slice(0, 12);
  for (const b of stops) keys.push({ s: b.s + 0.02, kind: "press", key: "up" }, { s: b.s + 0.07, kind: "release", key: "up" });
  const r = new Rng(7, "mash");
  for (let k = 0; k < 30; k++) {
    const s = 60 + r.next() * 60;
    keys.push({ s, kind: "press", key: "left" }, { s: s + 0.03, kind: "release", key: "left" });
  }
  for (let k = 0; k < 12; k++) {
    const s = 50 + r.next() * 70;
    keys.push({ s, kind: "press", key: "space" }, { s: s + 0.05, kind: "release", key: "space" });
    const t = 50 + r.next() * 70;
    keys.push({ s: t, kind: "press", key: "down" }, { s: t + 0.02, kind: "press", key: "up" }, { s: t + 0.06, kind: "release", key: "down" }, { s: t + 0.07, kind: "release", key: "up" });
  }
  keys.sort((a, b) => a.s - b.s);
}

/**
 * Play live on a scripted frame clock: each key event arrives at the first frame at least its
 * delivery delay (up to 16 ms) after it happens.
 */
function playLive(opts: { until?: number; onFrame?: (now: number, frame: number, session: ReturnType<typeof liveSession>) => void } = {}) {
  const session = liveSession(input, { props });
  const r = new Rng(11, "delivery");
  const arrivals = keys.map((k) => ({ k, at: k.s + r.next() * 0.016 }));
  let next = 0;
  let frame = 0;
  // (Frame times fall between the simulation's fixed steps, as a real display's do.)
  for (let now = 0.0037; now < (opts.until ?? ctx.duration + 1.2); now += 1 / 60) {
    const due: KeyEvent[] = [];
    while (next < arrivals.length && arrivals[next].at <= now) due.push(arrivals[next++].k);
    session.runner.frame(now, due);
    opts.onFrame?.(now, frame++, session);
  }
  return session;
}

describe("live play", () => {
  it("sounds a stride's landing the same whichever frames the clock stopped at", () => {
    // A batch run, and the same with the clock stopped between the press and the landing.
    const keys: KeyEvent[] = [
      { s: 66.2, kind: "press", key: "right" },
      { s: 66.23, kind: "release", key: "right" },
    ];
    const plain = batch(keys, 67);
    const stopped = (() => {
      const recording = new LiveRecording(ctx, sch, map, ctx.lib, "player", props);
      const sim = new Sim(ctx, sch, worldFor(), recording);
      for (const k of keys) sim.key(k);
      sim.advanceTo(66.3095);
      sim.advanceTo(67);
      return recording;
    })();
    expect(stopped.hashParts().sounds).toBe(plain.recording.hashParts().sounds);
  });

  it("records exactly what a batch run of the same key events records", () => {
    const live = playLive();
    live.runner.finish();
    expect(live.runner.stats.rollbacks).toBeGreaterThan(100);
    expect(live.recording.plan.inputs.filter((i) => i.extra).length).toBeGreaterThan(5);
    // Stomps with Space, and opposite keys that rocked a stride back.
    expect(live.recording.plan.inputs.filter((i) => i.stomp && i.key === "space").length).toBeGreaterThan(10);
    expect(live.recording.plan.inputs.filter((i) => i.rock && i.key !== "space").length).toBeGreaterThan(5);
    expect(live.runner.stats.lateKeys).toBe(0);
    expect(live.runner.applied.length).toBe(keys.length);
    // (Fed in the order they were applied: keys at the very same instant take effect in that order.)
    expect(live.recording.hash()).toBe(batch(live.runner.applied).recording.hash());
  }, 120_000);

  it("rewinds cleanly: mid-run, the recording and the evaluator's caches equal a fresh run's", () => {
    const checks: { now: number; s: number; liveFrame: string; hash: string; applied: KeyEvent[] }[] = [];
    let ev: Evaluator | undefined;
    const session = playLive({
      until: 110,
      onFrame: (now, frame, session) => {
        if (now < 1) return;
        ev ??= new Evaluator(session.recording.plan, ctx.lib, session.recording);
        // The live evaluator draws every frame, so its caches see the runs that were rolled back.
        session.recording.refresh();
        const f = ev.evaluate(now - 0.05);
        if (frame % 397 === 0) checks.push({ now, s: session.runner.sim.s, liveFrame: JSON.stringify(f), hash: session.recording.hash(), applied: [...session.runner.applied] });
      },
    });
    expect(session.runner.stats.rollbacks).toBeGreaterThan(50);
    expect(checks.length).toBeGreaterThan(10);
    for (const c of checks) {
      // A run without rollback, fed the key events applied by then, to the same simulation time.
      const fresh = batch(c.applied, c.s);
      expect(fresh.sim.s).toBeCloseTo(c.s, 9);
      expect(fresh.recording.hash(), `recording at now=${c.now.toFixed(2)}`).toBe(c.hash);
      fresh.recording.refresh();
      const f = new Evaluator(fresh.recording.plan, ctx.lib, fresh.recording).evaluate(c.now - 0.05);
      expect(JSON.stringify(f), `frame at now=${c.now.toFixed(2)}`).toBe(c.liveFrame);
    }
  }, 120_000);
});

describe("cancelled sounds", () => {
  const ev = (sample: string, s: number, lead = 0): SoundEvent => ({ sample, s, gain: 1, pan: 0, rate: 1, priority: 4, ...(lead ? { lead } : {}) });
  class FakeOutput implements SoundOutput {
    currentTime = 0;
    started: { key: string; when: number; offset: number; stopped: boolean }[] = [];
    start(e: SoundEvent, when: number, offset: number) {
      const rec = { key: soundKey(e), when, offset, stopped: false };
      this.started.push(rec);
      return { stop: () => (rec.stopped = true) };
    }
  }

  it("stops every scheduled source the new run no longer has, and never schedules one twice", () => {
    const out = new FakeOutput();
    const sch = new LiveSoundScheduler(out, 0);
    const a = ev("kill", 1.05);
    const b = ev("kill-multi", 1.08);
    const c = ev("swing", 1.02);
    out.currentTime = 1.0;
    sch.update([c, a, b]);
    expect(out.started.map((x) => x.key)).toEqual([soundKey(c), soundKey(a), soundKey(b)]);
    // A rollback: the kill became a miss, and a new sound appeared.
    const d = ev("near-miss", 1.09);
    out.currentTime = 1.01;
    sch.update([c, b, d]);
    expect(out.started.find((x) => x.key === soundKey(a))!.stopped).toBe(true);
    expect(out.started.filter((x) => x.stopped).length).toBe(1);
    expect(out.started.map((x) => x.key)).toEqual([soundKey(c), soundKey(a), soundKey(b), soundKey(d)]);
    // Started sources are never stopped, and nothing is scheduled twice.
    out.currentTime = 1.06;
    sch.update([c, d]);
    expect(out.started.find((x) => x.key === soundKey(c))!.stopped).toBe(false);
    expect(new Set(out.started.map((x) => x.key)).size).toBe(out.started.length);
    expect(sch.stats.cancelled).toBe(2);
  });

  it("starts a slow attack partway in when it is due already, so its onset stays on time", () => {
    const out = new FakeOutput();
    const sch = new LiveSoundScheduler(out, 10);
    out.currentTime = 10 + 2.0;
    sch.update([ev("level-up", 2.1, 0.2)]);
    expect(out.started[0].when).toBeCloseTo(12.0, 9);
    expect(out.started[0].offset).toBeCloseTo(0.1, 9);
    // A sound whose onset has passed is skipped, not played late, even by 20 ms (a late key event
    // that changed it after it was due).
    sch.update([ev("kill", 1.9)]);
    sch.update([ev("kill-multi", 1.98)]);
    expect(out.started.length).toBe(1);
    expect(sch.stats.skippedLate).toBe(2);
  });
});

describe("the latency mapping", () => {
  it("maps a key event's timestamp to the song time heard then", () => {
    const clock = { contextTime: 20, performanceTime: 5000, currentTime: 20.05, outputLatency: 0.04, perfNow: 5050 };
    // Context time 20 was heard at performance time 5000 ms; song time 0 is context time 4.
    expect(songTimeAtPerformance(clock, 4, 5000, 0)).toBeCloseTo(16, 9);
    expect(songTimeAtPerformance(clock, 4, 5100, 0)).toBeCloseTo(16.1, 9);
    // ?avOffsetMs shifts it like the picture.
    expect(songTimeAtPerformance(clock, 4, 5100, -40)).toBeCloseTo(16.06, 9);
    // Without an output timestamp: the context time less the output latency.
    const bare = { currentTime: 20.05, outputLatency: 0.04, perfNow: 5050 };
    expect(songTimeAtPerformance(bare, 4, 5050, 0)).toBeCloseTo(16.01, 9);
  });
});
