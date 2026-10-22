/**
 * Sound on the Grid (play mode's gridSounds; docs/design/gameplay-redesign.md): every gameplay
 * sound plays on a slot of its subdivision, and one known less than 60 ms before its slot moves to
 * a later slot instead of starting late, the same in live play and its replay.
 */
import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas, runPlanner } from "../scripts/lib/plan-node.ts";
import { summarize, type PlayLogFile } from "../scripts/lib/play-log.ts";
import type { Warp } from "../src/core/warp.ts";
import { PlayLog, recordingContext } from "../src/live/log.ts";
import { liveSession } from "../src/live/session.ts";
import { LiveSoundScheduler } from "../src/live/sounds.ts";
import type { CameraTrack, SoundEvent } from "../src/plan/types.ts";
import { checkPlan, soundRows } from "../src/planner/checks.ts";
import { Emitter, NEVER_LATE, type EmitSink } from "../src/planner/emit.ts";
import { plan, prepare } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
const props = propsFromAtlas();
const { ctx, sch } = prepare(input, props);
const section = (label: string) => ctx.sections.find((x) => x.label === label)!;

describe("never late", () => {
  const sink: EmitSink = { effect() {}, sound() {}, ui() {}, effectCount: 0, soundCount: 0 };
  const em = new Emitter(ctx, {} as Warp, () => ({}) as CameraTrack, sink, true, { grid: sch.gridTimes, lastKeyAt: () => -Infinity });
  const theme = section("theme-a");
  const beats = ctx.song.beats.filter((b) => b > theme.s0 + 1);
  const [b0, b1] = beats;
  const len = b1 - b0;

  it("moves a sound known 40 ms before its slot to the next slot of its subdivision, and keeps one known 60 ms before", () => {
    const g = Array.from(sch.gridTimes);
    const gi = g.findIndex((t) => t >= b0 - 1e-9);
    const bar = ctx.idx.barStart.map((b) => ctx.song.beats[b]).filter((t) => t >= b0 - 1e-9);
    const cases: [Parameters<Emitter["place"]>[0], number, number][] = [
      ["sixteenth", b0, b0 + len / 4],
      ["eighth", b0, b0 + len / 2],
      ["beat", b0, b1],
      ["grid", g[gi], g[gi + 1]],
      ["bar", bar[0], bar[1]],
    ];
    for (const [every, slot, next] of cases) {
      expect(em.place(every, slot, slot - 0.04), every).toEqual({ s: next, movedFrom: slot });
      expect(em.place(every, slot, slot - NEVER_LATE), every).toEqual({ s: slot });
      // A sound fixed in advance (no known time) and one caused between slots: the slot at or after it.
      expect(em.place(every, slot - 0.001).s, every).toBeCloseTo(slot, 9);
    }
  });

  it("keeps to the song map's beats in a free section", () => {
    const prelude = section("prelude");
    const pb = ctx.song.beats.filter((b) => b > prelude.s0 + 2 && b < prelude.s1);
    expect(em.place("grid", pb[0] - 0.1).s).toBeCloseTo(pb[0], 9);
    expect(em.place("grid", pb[0], pb[0] - 0.01)).toEqual({ s: pb[1], movedFrom: pb[0] });
  });
});

describe("a tier chime known 40 ms before its grid time", () => {
  // From the groove at 0, five Perfect presses on theme A's beats and a sixth 40 ms early (still
  // Perfect) lift the tier to 1 at that press: its chime, due on the next grid time 40 ms later,
  // waits for the grid time after.
  const theme = section("theme-a");
  const g = Array.from(sch.gridTimes).filter((t) => t > theme.s0 + 1).slice(0, 8);
  const keys: KeyEvent[] = g.slice(0, 6).flatMap((t, k) => {
    const s = k === 5 ? t - 0.04 : t + 0.005;
    return [
      { s, kind: "press" as const, key: "right" as const },
      { s: s + 0.05, kind: "release" as const, key: "right" as const },
    ];
  });
  const end = g[7] + 0.5;
  /** Live play: frames every 1/60 s around the presses, each key arriving a frame late (the simulation rolls back for it). */
  const live = () => {
    const session = liveSession(input, { props });
    for (let now = 0; now < g[0] - 0.5; now += 0.25) session.runner.frame(now);
    let next = 0;
    for (let now = g[0] - 0.5; now < end; now += 1 / 60) {
      const due: KeyEvent[] = [];
      while (next < keys.length && keys[next].s + 1 / 60 <= now) due.push({ ...keys[next++] });
      session.runner.frame(now, due);
    }
    return session;
  };
  /** Its replay: the same keys known in advance. */
  const replay = () => {
    const session = liveSession(input, { props, keys });
    for (let now = 0; now < end; now += 0.25) session.runner.frame(now);
    session.runner.frame(end);
    return session;
  };

  it("moves to the grid time after, the same in live play and its replay, and the play log records the move", () => {
    const a = live();
    const b = replay();
    const chimes = (s: ReturnType<typeof live>) => s.recording.plan.events.sounds.filter((x) => x.key === "tier-up" && x.s > g[0]);
    const chime = chimes(a);
    expect(chime.length).toBe(1);
    expect(chime[0].s).toBeCloseTo(g[6], 9);
    expect(chime[0].movedFrom).toBeCloseTo(g[5], 9);
    expect(chimes(b)).toEqual(chime);
    // Every sound up to the end is the same in both.
    const upTo = (s: ReturnType<typeof live>) => s.recording.plan.events.sounds.filter((x) => x.s < end - 0.3).map((x) => `${x.key}@${x.s.toFixed(6)}@${x.movedFrom?.toFixed(6) ?? ""}@${x.gain.toFixed(4)}`);
    expect(upTo(a)).toEqual(upTo(b));

    // The play log's context records it, and log:play reports it.
    const rec = a.recording;
    rec.refresh();
    const context = recordingContext(rec.plan, rec.timeWarp, rec.s);
    expect(context.soundMoves).toContainEqual(expect.objectContaining({ key: "tier-up", from: expect.closeTo(g[5], 5), s: expect.closeTo(g[6], 5) }));
    const file = JSON.parse(JSON.stringify({ ...new PlayLog(new Date(0)).toJSON(), ...context })) as PlayLogFile;
    expect(summarize(file)).toMatch(/Sounds moved to a later slot \(known less than 60 ms before their own\): \d+ \(.*tier-up 1/);
  });

  it("stays on its grid time when the press lands on the beat", () => {
    const onTime = keys.map((k, i) => (i >= 10 ? { ...k, s: k.s + 0.045 } : k));
    const session = liveSession(input, { props, keys: onTime });
    for (let now = 0; now < end; now += 0.25) session.runner.frame(now);
    const chime = session.recording.plan.events.sounds.filter((x) => x.key === "tier-up" && x.s > g[0]);
    expect(chime.length).toBe(1);
    expect(chime[0].s).toBeCloseTo(g[6], 9);
    expect(chime[0].movedFrom).toBeUndefined();
  });
});

describe("the impacts-on-the-grid check", () => {
  it("covers every row of the table: a sound 5 ms off its slot fails it", () => {
    const p = runPlanner({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
    const lib = input.content;
    const result = () => checkPlan(p, lib).find((c) => c.name === "impacts on the grid")!;
    expect(result().failures).toEqual([]);
    const keys = new Set(p.events.sounds.map((x) => x.key));
    for (const k of ["kill-multi", "tier-up", "level-up", "weapon-blades", "weapon-lightning", "chime"]) expect(keys.has(k), k).toBe(true);
    for (const key of ["kill-multi", "tier-up", "level-up", "weapon-lightning", "chime"]) {
      const snd = p.events.sounds.find((x) => x.key === key)!;
      snd.s += 0.005;
      expect(result().failures.some((f) => f.startsWith(`${key} sound at`)), key).toBe(true);
      snd.s -= 0.005;
    }
  });
});

describe("live play hears every table sound on time", () => {
  /**
   * Live play at 60 frames a second through the live sound scheduler, from `startS`, with a press
   * near each grid time `g` (5, 20 or 40 ms early) arriving two frames late: one 5 ms before a strike
   * reaches the game after it, and changes sounds already due (a kill it moves out of a group
   * changes the group). Returns the sources started, and how late each lost its onset.
   */
  const hear = (startS: number, g: number[]) => {
    const session = liveSession(input, { props, startS });
    const keys: KeyEvent[] = g.flatMap((t, k) => [
      { s: t - [0.005, 0.02, 0.04][k % 3], kind: "press" as const, key: k % 2 ? ("left" as const) : ("right" as const) },
      { s: t - [0.005, 0.02, 0.04][k % 3] + 0.05, kind: "release" as const, key: k % 2 ? ("left" as const) : ("right" as const) },
    ]);
    // (A source started `offset` into its sample has lost its onset when that is past the sample's lead.)
    const out = { currentTime: 0, starts: [] as { key?: string; s: number; late: number }[], start(ev: SoundEvent, _when: number, offset: number) { this.starts.push({ key: ev.key, s: ev.s, late: offset - (ev.lead ?? 0) }); return { stop() {} }; } };
    const scheduler = new LiveSoundScheduler(out, 0);
    let next = 0;
    for (let now = startS; now < g[g.length - 1] + 1; now += 1 / 60) {
      const due: KeyEvent[] = [];
      while (next < keys.length && keys[next].s + 2 / 60 <= now) due.push({ ...keys[next++] });
      session.runner.frame(now, due);
      out.currentTime = now;
      scheduler.update(session.recording.plan.events.sounds);
    }
    const rows = soundRows(input.content);
    return { table: out.starts.filter((x) => x.key !== undefined && rows.has(x.key) && x.s > startS + 0.3), starts: out.starts, scheduler };
  };

  it("never starts one after its onset in a free section (its kills grouped per beat)", () => {
    const vamp = section("galop-vamp");
    const { table, starts } = hear(vamp.s0 - 6, Array.from(sch.gridTimes).filter((t) => t > vamp.s0 + 0.5).slice(0, 6));
    expect(table.some((x) => x.key!.startsWith("kill") && x.s < vamp.s0)).toBe(true);
    expect(starts.filter((x) => x.late > 0.001)).toEqual([]);
  });

  it("never starts one after its onset when a late key event changes it after it was due", () => {
    const theme = section("theme-a");
    const { table, starts, scheduler } = hear(theme.s0 + 2, Array.from(sch.gridTimes).filter((t) => t > theme.s0 + 3).slice(0, 24));
    expect(table.length).toBeGreaterThan(30);
    // None started late (nor any other sound: a slow attack started partway in still has its onset);
    // a sound a late key event changed after it was due was skipped instead.
    expect(starts.filter((x) => x.late > 0.001)).toEqual([]);
    expect(scheduler.stats.skippedLate).toBeGreaterThan(0);
  });
});

describe("the batch planner and the live recording", () => {
  it("place the same sounds on the same slots for the same key events", () => {
    const theme = section("theme-a");
    const g = Array.from(sch.gridTimes).filter((t) => t > theme.s0 + 1).slice(0, 12);
    const keys: KeyEvent[] = g.flatMap((t, k) => [
      { s: t - 0.03, kind: "press" as const, key: k % 2 ? ("up" as const) : ("right" as const) },
      { s: t + 0.05, kind: "release" as const, key: k % 2 ? ("up" as const) : ("right" as const) },
    ]);
    // The batch: a player that makes exactly these key events.
    let i = 0;
    const player = {
      nextDecision: (after: number) => {
        while (i < keys.length && keys[i].s < after - 1e-9) i++;
        return i < keys.length ? keys[i].s : Infinity;
      },
      decide: (obs: { s: number }) => keys.filter((k) => Math.abs(k.s - obs.s) < 1e-9),
    };
    const batch = plan(input, { props, player });
    const live = liveSession(input, { props, keys });
    for (let now = 0; now < g[11] + 2; now += 0.25) live.runner.frame(now);
    const until = g[11] + 1;
    const of = (sounds: readonly { s: number; sample: string; gain: number; pan: number; key?: string; movedFrom?: number }[]) =>
      // (Not the pan or the sample picked: the batch reads the hero and the camera from its finished
      // tracks, the live recording from the latest samples, a pixel or so apart.)
      sounds.filter((x) => x.s > theme.s0 && x.s < until).map((x) => `${x.key}@${x.s.toFixed(5)}@${x.movedFrom?.toFixed(5) ?? ""}`);
    expect(of(live.recording.plan.events.sounds)).toEqual(of(batch.events.sounds));
  });
});

describe("the sword's impact", () => {
  it("is the same sound whether or not a key is pressed between its wind-up and its strike", () => {
    const theme = section("theme-a");
    const g = Array.from(sch.gridTimes).filter((t) => t > theme.s0 + 1);
    const strike = g[4];
    const run = (keys: KeyEvent[]) => {
      const session = liveSession(input, { props, keys });
      for (let now = 0; now < strike + 1; now += 0.25) session.runner.frame(now);
      return session.recording.plan.events.sounds.filter((x) => Math.abs(x.s - strike) < 1e-6 && (x.key === "hit-light" || x.key === "hit-heavy"));
    };
    const quiet = run([]);
    const pressed = run([
      { s: strike - 0.03, kind: "press", key: "up" },
      { s: strike + 0.05, kind: "release", key: "up" },
    ]);
    expect(quiet.length).toBe(1);
    expect(pressed).toEqual(quiet);
  });
});
