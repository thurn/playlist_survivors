import { describe, expect, it } from "vitest";
import { plannerInput, propsFromAtlas } from "../scripts/lib/plan-node.ts";
import { classesBySection, pressRows, rerate, speedPerSecond, summarize, tierTime, type PlayLogFile, type RerateRules } from "../scripts/lib/play-log.ts";
import { PlayLog, recordingContext } from "../src/live/log.ts";
import { grooveAt } from "../src/plan/evaluate.ts";
import { liveSession } from "../src/live/session.ts";
import { prepare } from "../src/planner/plan.ts";
import type { KeyEvent } from "../src/sim/player.ts";
import { tierFor } from "../src/sim/timing.ts";
import { syntheticSongMap } from "./fixtures/synthetic-song.ts";

const input = plannerInput({ songMap: syntheticSongMap(), level: "levels/orpheus-galop.play.yaml" });
const props = propsFromAtlas();

/** The current rules on the synthetic song, as log:play builds them from the song map. */
const { ctx, sch } = prepare(input);
const rules: RerateRules = {
  hero: ctx.lib.hero,
  grid: sch.gridTimes,
  beats: ctx.song.beats,
  sections: ctx.sections.map((x) => ({ label: x.label, s0: x.s0, s1: x.s1, movement: x.level.movement, beatsPerBar: ctx.idx.beatsPerBar[x.song.bars[0]] ?? 2 })),
};

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

    // A stride on a grid time, a second press for the same grid time (an unrated extra stride, which
    // interrupts the first), then a stride on the next.
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
    expect(traces.map((t) => t?.outcome)).toEqual(["stride", "release", "extra", "release", "stride"]);
    // Strides: standing still a while, the first is a full stride (140 px/s times the grid step). The
    // extra press 50 ms later stops it, takes back the ground it had not covered, and adds 50 ms of
    // walking; the next press has the ground since then. Each spends the whole budget.
    const one = 140 * (g1 - g0);
    expect(traces[0]!.stride).toBeCloseTo(one, 6);
    expect(traces[0]!.returned).toBe(0);
    expect(traces[2]!.returned).toBeCloseTo(one * (1 - 0.05 / 0.11) ** 3, 6);
    expect(traces[2]!.stride).toBeCloseTo(140 * 0.05 + traces[2]!.returned!, 6);
    expect(traces[4]!.stride).toBeCloseTo(140 * (g1 - 0.02 - (g0 + 0.06)), 6);
    for (const k of [0, 2, 4]) expect(traces[k]!.budget).toBe(0);
    expect(traces[0]!.gridS).toBeCloseTo(g0, 9);
    expect(traces[0]!.delta).toBeCloseTo(0.01, 9);
    expect(traces[2]!.stepS).toBeCloseTo(g0 + 0.01, 9);
    expect(traces[4]!.delta).toBeCloseTo(-0.02, 9);
    expect(log.applies.length).toBe(keys.length);
    expect(log.applies.some((a) => a.rollbackTo !== undefined)).toBe(true);

    // The report, from the saved form (with what play.ts adds: sections, the groove and tier series, the hero's path).
    const rec = session.recording;
    rec.refresh();
    const context = { beats: Array.from(rec.plan.songBeats), sections: rules.sections, playerFrom: rec.playerFrom, hero: { timing: ctx.lib.hero.timing, step: ctx.lib.hero.step, swings: ctx.lib.hero.swings, walk: { speed: ctx.lib.hero.walk.speed }, meter: ctx.lib.hero.meter }, inputs: rec.plan.inputs, ...recordingContext(rec.plan, rec.timeWarp, rec.s) };
    const file = JSON.parse(JSON.stringify({ ...log.toJSON(), ...context })) as PlayLogFile;
    const rows = pressRows(file);
    expect(rows.map((r) => r.outcome)).toEqual(["stride", "extra", "stride"]);
    expect(rows[0].stride).toBeCloseTo(one, 6);
    expect(rows[1].sinceStepMs).toBeCloseTo(50, 6);
    // Per press: the class, rating and delta, the stride and the budget left, the groove before and after, the tier.
    expect(rows.map((r) => [r.class, r.rating])).toEqual([
      ["counted", "perfect"],
      ["double", undefined],
      ["counted", "perfect"],
    ]);
    expect(rows[0].budget).toBe(0);
    expect(rows[0].grooveAfter! - rows[0].grooveBefore!).toBeCloseTo(ctx.lib.hero.meter.counted.perfect, 6);
    expect(rows[1].grooveAfter).toBeCloseTo(rows[0].grooveAfter!, 6);
    // (The tier after each press: from the groove the virtual player left at the hand-over, the tier it set.)
    expect(rows.map((r) => r.tier)).toEqual(rows.map((r) => tierFor(ctx.lib.hero, rows[0].tier!, r.grooveAfter!)));
    const text = summarize(file, { rerate: rules });
    expect(text).toContain("Presses: 3");
    expect(text).toMatch(/extra\s+1/);
    expect(text).toContain("1 interrupted the stride before");
    // The table: class, rating, groove before→after and tier.
    expect(text).toMatch(/stride\s+counted\s+perfect\s+\d\.\d\d→\d\.\d\d\s+\d/);
    // Presses by class and time at each tier per section, recorded and re-rated: under the rules it
    // was played under, re-rating changes nothing.
    expect(text).toContain("Press classes and time at each tier, per section (recorded):");
    expect(text).toContain("Re-rated under the current rules");
    const re = rerate(file, rules);
    expect(re.presses.map((p) => p.class)).toEqual(rows.map((r) => r.class));
    expect(re.presses.at(-1)!.groove).toBeCloseTo(rows.at(-1)!.grooveAfter!, 6);
    // (The drain's bar runs from the virtual player's last raising press before the hand-over, as in play.)
    const end = g1 + 6;
    const recorded = tierTime(file.tier!, rules.sections, re.from, end);
    for (const [label, secs] of tierTime(re.tier, rules.sections, re.from, end)) secs.forEach((x, k) => expect(x, `${label} tier ${k}`).toBeCloseTo(recorded.get(label)![k], 6));
    for (let s = re.from; s < re.to; s += 0.1) expect(grooveAt(re.groove, s), `groove at ${s}`).toBeCloseTo(grooveAt(file.groove!, s), 4);
    expect(text).not.toContain("classes changed");
    // The speed over each second, from the hero's path.
    expect(file.heroPath!.x.length).toBeGreaterThan(10);
    expect(text).toMatch(/Speed \(px per world second/);
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

describe("the log's summaries", () => {
  it("adds up presses by class and time at each tier in each pulsed section", () => {
    const sections = [
      { label: "a", s0: 0, s1: 10, movement: "steps" },
      { label: "free", s0: 10, s1: 12, movement: "free" },
      { label: "b", s0: 12, s1: 20, movement: "steps" },
    ];
    const tiers = tierTime({ s: [0, 4, 6, 15], v: [0, 1, 2, 3] }, sections, 2, 18);
    expect(tiers.get("a")).toEqual([2, 2, 4, 0]);
    expect(tiers.get("b")).toEqual([0, 0, 3, 3]);
    expect(tiers.has("free")).toBe(false);
    const c = classesBySection([
      { section: "a", class: "counted" },
      { section: "a", class: "stray" },
      { section: "a", class: "counted" },
      { section: "b", class: "offbeat" },
      { section: "b" },
    ]);
    expect(c.get("a")).toMatchObject({ counted: 2, stray: 1, offbeat: 0 });
    expect(c.get("b")).toMatchObject({ offbeat: 1, counted: 0 });
  });

  it("measures the hero's speed over each second in world time, through a slow motion", () => {
    // 140 px per world second along x; from song second 2 the world runs at half speed.
    const dt = 0.05;
    const w: number[] = [];
    const x: number[] = [];
    for (let k = 0; k <= 80; k++) {
      const s = k * dt;
      const wk = s <= 2 ? s : 2 + (s - 2) / 2;
      w.push(wk);
      x.push(140 * wk);
    }
    const speeds = speedPerSecond({ dt, s0: 0, w, x, y: x.map(() => 0) });
    expect(speeds.map((v) => v.s)).toEqual([0, 1, 2, 3]);
    for (const v of speeds) expect(v.speed).toBeCloseTo(140, 6);
    expect(speeds[3].px).toBeCloseTo(70, 6);
  });
});

describe("re-rating a log recorded before press classes", () => {
  // Today's (pre-class) rules: one rated press per grid slot. Its presses' outcomes: step, extra, bend.
  const theme = rules.sections.find((x) => x.label === "theme-a")!;
  const g = Array.from(sch.gridTimes).filter((t) => t > theme.s0 + 1).slice(0, 12);
  let id = 1;
  const press = (s: number, key: string, outcome: string) => ({ id: id++, s, w: s, kind: "press" as const, key, section: "theme-a", movement: "steps" as const, outcome: outcome as "step" | "extra" | "bend", runs: 1 });
  const traces = [
    ...g.slice(0, 12).map((t) => press(t + 0.01, "right", "step")),
    press(g[0] + 0.02, "up", "bend"), // a diagonal's second key: a partner
    press(g[1] + 0.06, "right", "extra"), // 50 ms after the counted press: a double tap
    press(g[2] + (g[3] - g[2]) / 2 - 0.04, "right", "extra"), // the off-beat, after a Perfect press
    press(g[2] + (g[3] - g[2]) / 2 - 0.012, "right", "extra"), // the off-beat is taken: a stray
  ].sort((a, b) => a.s - b.s);
  const end = g[11] + 4 * 2 * (g[1] - g[0]);
  const old = {
    id: "old",
    savedAt: "2026-10-21T00:00:00Z",
    level: "orpheus-galop.play",
    params: { avOffsetMs: 0, startS: theme.s0 },
    keys: traces.map((t) => ({ id: t.id, type: "down" as const, key: t.key, code: "", timeStamp: t.s * 1000, handledAt: t.s * 1000, repeat: false, fate: "queued" as const, s: t.s })),
    applies: [],
    drops: [],
    traces,
    events: [],
    sounds: [],
    frames: { phases: ["countIn", "playing", "paused", "replay"], ts: [0, 1], perf: [0, 1], s: [theme.s0, end], raw: [theme.s0, end], phase: [1, 1], dur: [8, 8], keys: [0, 0], ms: [1, 1], simS: [theme.s0, end] },
    sections: rules.sections,
  } as unknown as PlayLogFile;

  it("classes its presses with the grid from the song map, and fills and drains the groove under the current meter", () => {
    const re = rerate(old, rules);
    const byId = new Map(re.presses.map((p) => [p.id, p]));
    expect(traces.map((t) => byId.get(t.id)!.class)).toEqual(traces.map((t) => (t.outcome === "bend" ? "partner" : t.outcome === "step" ? "counted" : t.s - g[1] < 0.1 && t.s > g[1] ? "double" : t.s < g[2] + (g[3] - g[2]) / 2 - 0.02 ? "offbeat" : "stray")));
    expect(re.presses.filter((p) => p.class === "counted").every((p) => p.rating === "perfect")).toBe(true);
    // The groove steps by the meter's values from 0, the tier follows it, and with no presses after
    // the last it drains a bar later.
    const m = rules.hero.meter;
    let groove = 0;
    for (const p of re.presses) {
      groove = Math.min(1, Math.max(0, groove + p.change));
      expect(p.groove).toBeCloseTo(groove, 9);
      expect(p.change).toBe(p.class === "counted" ? m.counted.perfect : p.class === "offbeat" ? m.offbeat.perfect : p.class === "stray" ? m.stray : 0);
    }
    expect(re.tier.v.at(-1)).toBeLessThan(Math.max(...re.tier.v));
    expect(re.groove.v.at(-1)).toBeLessThan(groove);
    // The summary prints the re-rated classes and tiers, and says the log recorded none.
    const text = summarize(old, { rerate: rules, table: false });
    expect(text).toContain("Press classes: none recorded");
    expect(text).toMatch(/theme-a\s+1\s+1\s+12\s+1\s+1\s+[\d.]+/);
    expect(text).toContain("Speed: no hero path in this log");
  });
});

describe("re-rating where a run is interrupted", () => {
  const theme = rules.sections.find((x) => x.label === "theme-a")!;
  const g = Array.from(sch.gridTimes).filter((t) => t > theme.s0 + 1).slice(0, 6);
  /** A log of presses: [song time, key, outcome, the key's performance time (ms)]. */
  const logOf = (presses: [number, string, string, number][], extra: Partial<PlayLogFile> = {}): PlayLogFile => {
    const traces = presses.map(([s, key, outcome], k) => ({ id: k + 1, s, w: s, kind: "press" as const, key, section: "theme-a", movement: "steps" as const, outcome: outcome as "stride", runs: 1 }));
    return {
      id: "t",
      savedAt: "",
      params: { avOffsetMs: 0, startS: theme.s0 },
      keys: presses.map(([s, key, , perf], k) => ({ id: k + 1, type: "down" as const, key, code: "", timeStamp: perf, handledAt: perf, repeat: false, fate: "queued" as const, s })),
      applies: [],
      drops: [],
      traces,
      events: [],
      sounds: [],
      frames: { phases: ["countIn", "playing", "paused", "replay"], ts: [], perf: [], s: [], raw: [], phase: [], dur: [], keys: [], ms: [], simS: [] },
      sections: rules.sections,
      ...extra,
    } as PlayLogFile;
  };

  it("counts a press just before a pulsed section's first grid time, as the simulation does", () => {
    const vamp = rules.sections.find((x) => x.label === "galop-vamp")!;
    const v = Array.from(sch.gridTimes).filter((t) => t >= vamp.s0 - 1e-6).slice(0, 2);
    const log = logOf(
      [
        [v[0] - 0.012, "right", "stride", 1000],
        [v[1] - 0.012, "right", "stride", 1300],
      ],
      { params: { avOffsetMs: 0, startS: vamp.s0 - 2 } },
    );
    const re = rerate(log, rules);
    expect(re.presses.map((p) => [p.id, p.class, p.rating, p.section])).toEqual([
      [1, "counted", "perfect", "galop-vamp"],
      [2, "counted", "perfect", "galop-vamp"],
    ]);
  });

  it("re-rates only the final timeline after a restart", () => {
    // The same press on g[0] before and after a song restart: both counted, not a double tap.
    const log = logOf(
      [
        [g[0] + 0.01, "right", "stride", 1000],
        [g[1] + 0.01, "right", "stride", 1300],
        [g[0] + 0.012, "right", "stride", 9000],
      ],
      { events: [{ perf: 5000, s: theme.s0, kind: "restartSong" }] },
    );
    const re = rerate(log, rules);
    expect(re.presses.map((p) => [p.id, p.class])).toEqual([[3, "counted"]]);
    expect(summarize(log, { rerate: rules, table: false })).toContain("(2 presses that a later restart rewound are left out");
    // A section restart rewinds only from its song time: the press before it stays.
    const section = logOf(
      [
        [g[0] + 0.01, "right", "stride", 1000],
        [g[2] + 0.01, "right", "stride", 1600],
        [g[2] + 0.02, "right", "stride", 9000],
      ],
      { events: [{ perf: 5000, s: g[1] + 0.1, kind: "restartSection" }] },
    );
    expect(rerate(section, rules).presses.map((p) => [p.id, p.class])).toEqual([
      [1, "counted"],
      [3, "counted"],
    ]);
  });

  it("keeps the presses the recording settled, whatever song time a restart reports", () => {
    // A section restart reported at g[1] rewound to a snapshot just before it: the press at
    // g[1] - 0.01 is gone, as the recording's settled inputs show.
    const log = logOf(
      [
        [g[0] + 0.01, "right", "stride", 1000],
        [g[1] - 0.01, "right", "stride", 1300],
        [g[1] + 0.01, "left", "stride", 9000],
      ],
      { events: [{ perf: 5000, s: g[1], kind: "restartSection" }], inputs: [g[0] + 0.01, g[1] + 0.01].map((t, k) => ({ s: t, kind: "press", key: k ? "left" : "right" })) },
    );
    expect(rerate(log, rules).presses.map((p) => p.id)).toEqual([1, 3]);
    // The same press (song time and key) before and after a song restart: the recording settled one,
    // the later attempt's.
    const twice = logOf(
      [
        [g[0] + 0.01, "right", "stride", 1000],
        [g[0] + 0.01, "right", "stride", 9000],
      ],
      { events: [{ perf: 5000, s: theme.s0, kind: "restartSong" }], inputs: [{ s: g[0] + 0.01, kind: "press", key: "right" }] },
    );
    expect(rerate(twice, rules).presses.map((p) => [p.id, p.class])).toEqual([[2, "counted"]]);
  });

  it("ends the report with the final attempt, not a longer one abandoned by a restart", () => {
    const frames = (rows: [number, number][]) => ({ phases: ["countIn", "playing", "paused", "replay"], ts: rows.map(([p]) => p), perf: rows.map(([p]) => p), s: rows.map(([, t]) => t), raw: rows.map(([, t]) => t), phase: rows.map(() => 1), dur: rows.map(() => 8), keys: rows.map(() => 0), ms: rows.map(() => 1), simS: rows.map(([, t]) => t) });
    // Played to g[5] + 3, restarted the song, then played only to g[2].
    const log = logOf([[g[0] + 0.01, "right", "stride", 8000]], {
      events: [{ perf: 5000, s: theme.s0, kind: "restartSong" }],
      frames: frames([
        [1000, g[0]],
        [4000, g[5] + 3],
        [6000, theme.s0],
        [9000, g[2]],
      ]),
    });
    expect(rerate(log, rules).to).toBeCloseTo(g[2], 9);
  });

  it("remembers the virtual player's presses before the hand-over, and its tier", () => {
    const from = g[0] + 0.015;
    // The virtual player's counted press at g[0]; the player's press 30 ms later is a double tap.
    const log = logOf([[g[0] + 0.03, "right", "stride", 1000]], {
      playerFrom: from,
      inputs: [
        { s: g[0], kind: "press", key: "up", class: "counted", rating: "perfect" },
        { s: g[0] + 0.03, kind: "press", key: "right", class: "double" },
      ],
      // At the hand-over the groove (0.415) is below tier 1's threshold (0.45) but within its hysteresis.
      groove: { s: [0, g[0]], v: [0, 0.415] },
      tier: { s: [0, g[0] - 1], v: [0, 1] },
    });
    const re = rerate(log, rules);
    expect(re.presses.map((p) => p.class)).toEqual(["double"]);
    expect(re.tier.v[0]).toBe(1);
  });

  it("shows a stomp's budget left in the table, though a stomp has no stride", () => {
    const log = logOf([[g[0], "space", "stomp", 1000]]);
    (log.traces[0] as { budget?: number }).budget = 47.7;
    expect(summarize(log, { table: true })).toMatch(/\/47\.7/);
  });
});
