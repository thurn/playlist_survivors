import { describe, expect, it } from "vitest";
import { loadContent, loadLevel } from "../src/content/loader.ts";
import type { Director } from "../src/content/schema.ts";
import { checkReactiveCausality, checkReactiveSounds, checkSettleTiming, perturbStream, smokeTest } from "../src/planner/reactive-checks.ts";
import { formatParity, parityRows, pressesRatedSame, strikesOnBeat, timeAtTier } from "../src/planner/parity.ts";
import { gatedSound } from "../src/planner/emit.ts";
import { planWithSim, playerFor, prepare } from "../src/planner/plan.ts";
import { directorPopulation, directorRoster } from "../src/reactive/director.ts";
import { judgedAverageGroove, REACTIVE_END_DELAY, REACTIVE_TITLE } from "../src/reactive/grade.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { reactiveSongMap } from "../src/reactive/songmap.ts";
import { effectiveDownbeat, evidenceAt, firstBeatAtOrAfter, parseStream, splitStream, type StreamFile } from "../src/reactive/stream.ts";
import type { KeyEvent, Observation, Player } from "../src/sim/player.ts";
import { grooveAt, settledStyle } from "../src/plan/evaluate.ts";
import { readContentFiles } from "../scripts/lib/files.ts";
import { inputForStream } from "../scripts/lib/reactive-node.ts";

/** A synthetic stream: beats every `period` from 0.5 s, each committed `lead` before it, and ticks with the given evidence. */
function synthStream(o: { until: number; period?: number; lead?: number; evidence?: (t: number) => number; hindsight?: boolean }): StreamFile {
  const period = o.period ?? 0.5;
  const lead = o.lead ?? 0.85;
  const ev = o.evidence ?? (() => 0.6);
  const beats: { t: number; knownAt: number; period: number; evidence: number }[] = [];
  for (let t = 0.5; t < o.until; t += period) {
    const knownAt = Math.max(0, Math.round((t - lead) * 100) / 100);
    beats.push({ t, knownAt, period, evidence: ev(knownAt) });
  }
  const ticks = [];
  for (let k = 1; k * 0.05 <= o.until + 1e-9; k++) ticks.push({ t: Math.round(k * 5) / 100, evidence: ev(k * 0.05), energy: 0.8 });
  return parseStream({
    version: 1,
    audio: { file: "x.ogg", duration: o.until, sampleRate: 44100 },
    params: { gate: 0.2, tickS: 0.05, toolVersion: 1 },
    beats,
    ticks,
    onsets: [],
    // Hindsight grids that decode the beats exactly: every second, the last 8 s of them.
    ...(o.hindsight
      ? {
          hindsight: Array.from({ length: Math.floor(o.until) }, (_, k) => {
            const T = k + 1;
            const keep = beats.map((b) => b.t).filter((t) => t >= T - 8 && t <= T);
            return { knownAt: T, offline: keep, online: keep };
          }),
        }
      : {}),
    reference: { source: "exact", beats: beats.map((b) => b.t), sections: [{ label: "all", s0: 0.5, s1: o.until }] },
  });
}

/** A player pressing `right` (released 60 ms later) at fixed song times. */
class KeyPlayer implements Player {
  private i = 0;
  constructor(private readonly times: number[]) {}
  nextDecision(after: number): number {
    while (this.i < this.times.length && this.times[this.i] < after - 1e-9) this.i++;
    return this.times[this.i] ?? Infinity;
  }
  decide(obs: Observation): KeyEvent[] {
    return [
      { s: obs.s, kind: "press", key: "right" },
      { s: obs.s + 0.06, kind: "release", key: "right" },
    ];
  }
}

describe("the belief stream and the reactive song map", () => {
  const file = synthStream({ until: 10, evidence: (t) => (t < 4 ? 0 : 1) });
  const { stream } = splitStream(file);

  it("builds 2-beat bars over the committed beats, one keyless pulsed section, energy as known at each commit", () => {
    const m = reactiveSongMap(stream);
    expect(m.beats).toEqual(stream.beats.map((b) => b.t));
    expect(m.meter).toEqual([{ fromBar: 1, beatsPerBar: 2 }]);
    expect(m.sections).toEqual([{ label: "reactive", bars: [1, Math.ceil(stream.beats.length / 2)], feel: "pulsed" }]);
    expect(m.sections[0].key).toBeUndefined();
    expect(m.accents).toEqual([]);
    expect(m.energy).toHaveLength(m.beats.length);
    expect(m.energy[0]).toBe(0);
    expect(m.energy[5]).toBe(0.8);
  });

  it("reads the latest tick at or before a time, and the first beat at or after one", () => {
    expect(evidenceAt(stream, 0.01)).toBe(0);
    expect(evidenceAt(stream, 3.97)).toBe(0);
    expect(evidenceAt(stream, 4.0)).toBe(1);
    expect(firstBeatAtOrAfter(stream, 3.2)?.t).toBe(3.5);
    expect(firstBeatAtOrAfter(stream, 3.5)?.t).toBe(3.5);
    expect(firstBeatAtOrAfter(stream, 99)).toBeUndefined();
  });

  it("finds the effective downbeat: the first bar's first beat at least 0.35 s after the event is known", () => {
    // Beats at 0.5 + 0.5i: downbeats at even indices (0.5, 1.5, 2.5 ...).
    expect(effectiveDownbeat(stream, 1.0)).toBe(2);
    expect(effectiveDownbeat(stream, 1.2)).toBe(4);
    expect(effectiveDownbeat(stream, 1.6)).toBe(4);
    expect(stream.beats[effectiveDownbeat(stream, 1.6)].t).toBe(2.5);
    expect(effectiveDownbeat(stream, 99)).toBe(-1);
  });
});

describe("the perturbation", () => {
  const { stream } = splitStream(synthStream({ until: 30 }));
  const C = 12.34;
  const p = perturbStream(stream, C, "test");

  it("keeps every entry known by C", () => {
    expect(p.beats.filter((b) => b.knownAt <= C)).toEqual(stream.beats.filter((b) => b.knownAt <= C));
    expect(p.ticks.filter((k) => k.t <= C)).toEqual(stream.ticks.filter((k) => k.t <= C));
  });

  it("changes every later entry within the design's constraints, the same way each time", () => {
    const kept = stream.beats.filter((b) => b.knownAt <= C);
    const last = kept[kept.length - 1];
    const later = p.beats.filter((b) => b.knownAt > C);
    expect(later.length).not.toBe(stream.beats.length - kept.length);
    let prev = last;
    for (const b of later) {
      expect(b.t).toBeGreaterThan(Math.max(b.knownAt, C) + 0.1);
      expect(b.t).toBeGreaterThan(prev.t + 0.25 * prev.period);
      expect(b.knownAt).toBeGreaterThanOrEqual(prev.knownAt);
      prev = b;
    }
    expect(later.some((b) => b.evidence !== 0.6)).toBe(true);
    expect(p.ticks.filter((k) => k.t > C).every((k) => k.evidence !== 0.6 || k.energy !== 0.8)).toBe(true);
    expect(p.audio.duration).toBeGreaterThanOrEqual(C + 10);
    expect(p.audio.duration).toBeLessThanOrEqual(C + 60);
    expect(perturbStream(stream, C, "test")).toEqual(p);
    expect(perturbStream(stream, C, "other")).not.toEqual(p);
  });
});

describe("the director", () => {
  const d: Director = { population: { start: 40, perMinute: 120, max: 480, energyScale: [0.7, 1.3] }, roster: [{ from: 0, enemies: { slime: 1 } }, { from: 45, enemies: { goblin: 1 } }], levelUps: [], zoneEvery: 40 };
  it("grows the population with time and scales it by the energy, up to the maximum", () => {
    expect(directorPopulation(d, 0, 0)).toBeCloseTo(28);
    expect(directorPopulation(d, 60, 1)).toBeCloseTo(208);
    expect(directorPopulation(d, 600, 1)).toBe(480);
  });
  it("takes the roster with the latest start at or before the time", () => {
    expect(directorRoster(d, 44.9)).toEqual({ slime: 1 });
    expect(directorRoster(d, 45)).toEqual({ goblin: 1 });
  });
});

describe("reactive levels", () => {
  const content = loadContent(readContentFiles());
  const song = reactiveSongMap(splitStream(synthStream({ until: 10 })).stream);
  const level = (extra: string) => ({
    name: "levels/test.yaml",
    text: `song: reactive\nseed: 1\nreactive: true\nzones: [twilight-glade]\nsections:\n  - section: reactive\n    bars: all\n    pace: 50\n    presses: "x"\n    zone: twilight-glade\n    sfxPalette: crisp-steel\n${extra}`,
  });
  const director = "director:\n  population: { start: 1, perMinute: 1, max: 10, energyScale: [1, 1] }\n  roster: [{ from: 0, enemies: { slime: 1 } }]\n  zoneEvery: 10\n";

  it("loads with a director", () => {
    const l = loadLevel(level(director), content, song);
    expect(l.reactive).toBe(true);
    expect(l.sections[0].bars).toEqual([1, song.sections[0].bars[1]]);
  });
  it("rejects play-only section fields, and a director on a play level", () => {
    expect(() => loadLevel(level(`    timeScale: 0.5\n${director}`), content, song)).toThrow(/has no timeScale/);
    expect(() => loadLevel(level(`    movement: free\n${director}`), content, song)).toThrow(/has no movement/);
    expect(() => loadLevel({ name: "levels/t.yaml", text: level(director).text.replace("reactive: true\n", "") }, content, song)).toThrow(/only for a reactive level|only a reactive level/);
  });
  it("needs a director, and has no level-script events", () => {
    expect(() => loadLevel(level(""), content, song)).toThrow(/needs a director/);
    expect(() => loadLevel(level(`${director}events:\n  - { at: { bar: 2 }, banner: hi }\n`), content, song)).toThrow(/no level-script events/);
  });
});

describe("the gate in the simulation", () => {
  // Clear for 8 s, unclear to 16 s, clear again; the player presses on every beat for the first 12 s.
  const file = synthStream({ until: 30, evidence: (t) => (t >= 8 && t < 16 ? 0 : 0.8) });
  const input = inputForStream(splitStream(file).stream, { checks: { noHeroDamage: true } });
  const beats = file.beats.map((b) => b.t).filter((t) => t < 12);
  const { plan } = planWithSim(input, { player: new KeyPlayer(beats) });
  const presses = plan.inputs.filter((i) => i.kind === "press");

  it("leaves presses while the beat is unclear unjudged, and judges the rest", () => {
    for (const p of presses) {
      if (p.s >= 8 && p.s < 16) expect(p.unjudged, `press at ${p.s}`).toBe(true);
      else {
        expect(p.unjudged).toBeUndefined();
        expect(p.class).toBeDefined();
      }
    }
    expect(presses.some((p) => p.unjudged)).toBe(true);
  });

  it("stops the drain clock while the beat is unclear", () => {
    const g8 = grooveAt(plan.groove, 8.01);
    expect(g8).toBeGreaterThan(0.3);
    expect(grooveAt(plan.groove, 15.99)).toBe(g8);
    expect(grooveAt(plan.groove, 29)).toBeLessThan(g8);
    // The latest raise was at the last judged press (7.5 s): half a second of the drain's bar
    // (afterBars 1: two 0.5 s beats) had passed at 8 s, so the first drain comes on the beat at
    // least another half second of judged time after 16 s, not at 16 s.
    const lastRaise = Math.max(...presses.filter((p) => !p.unjudged).map((p) => p.s));
    const judgedBefore = 8 - lastRaise;
    const firstDrain = Array.from(plan.groove.s).find((t, i) => t > 8 && plan.groove.v[i] < g8)!;
    expect(firstDrain).toBeGreaterThan(16 + (1 - judgedBefore) - 1e-6);
    expect(firstDrain).toBeLessThan(16 + (1 - judgedBefore) + 0.5 + 1e-6);
  });

  it("queues the starting weapons' fires on every slot of their subdivision, beat by beat", () => {
    const fires = plan.hud.weaponFires["orbiting-blades"];
    expect(fires.length).toBeGreaterThan(20);
    const beatsAll = file.beats.map((b) => b.t);
    for (const t of Array.from(fires)) expect(beatsAll.some((b) => Math.abs(b - t) < 1e-6) || beatsAll.some((b, i) => i + 1 < beatsAll.length && t > b && t < beatsAll[i + 1])).toBe(true);
  });

  it("mutes percussive gameplay sounds on beats committed below the gate, but not the exempt ones", () => {
    expect(checkReactiveSounds(plan, splitStream(file).stream, input.content).failures).toEqual([]);
    const { ctx } = prepare(input);
    const unclearBeat = file.beats.find((b) => b.evidence < 0.2)!.t;
    expect(gatedSound(ctx, "hit-light", unclearBeat)).toBe(true);
    expect(gatedSound(ctx, "hurt", unclearBeat)).toBe(false);
    expect(gatedSound(ctx, "hit-light", 2.5)).toBe(false);
  });
});

describe("the causality check", () => {
  const file = synthStream({ until: 14 });
  const { stream } = splitStream(file);
  const keys = file.beats.map((b) => b.t + 0.01);
  const run = (leak: boolean) =>
    checkReactiveCausality({
      track: "synthetic",
      stream,
      inputFor: (s) => inputForStream(s, { level: "levels/reactive-check.yaml", checks: { noHeroDamage: true, leakFutureEvidence: leak }, knownLength: stream.audio.duration }),
      players: [{ name: "keys", player: () => new KeyPlayer(keys) }],
      cuts: [4.03, 7.51, 10.2],
      stopAtFirst: leak,
    }).failures.filter((f) => f.includes("cut at"));

  it("passes the reactive simulation", () => {
    expect(run(false)).toEqual([]);
  });
  it("fails a simulation whose spawn count reads the evidence a second ahead (the negative control)", () => {
    expect(run(true).length).toBeGreaterThan(0);
  });
});

describe("reactive players", () => {
  const file = synthStream({ until: 20 });
  const input = inputForStream(splitStream(file).stream, { checks: { noHeroDamage: true } });
  const { ctx, sch } = prepare(input);
  const ref = file.reference!;
  const pressesOf = (name: "reactive-moderate" | "reactive-half" | "reactive-offbeat") =>
    planWithSim(input, { player: reactivePlayerFor(ctx, ref, name) })
      .plan.inputs.filter((i) => i.kind === "press" && i.key !== "space")
      .map((i) => i.s);
  const near = (times: number[], targets: number[]) => times.filter((t) => targets.some((x) => Math.abs(x - t) <= 0.1)).length / times.length;

  it("press on the reference beats, every other one, or the off-beats", () => {
    const beats = ref.beats;
    const mids = beats.slice(1).map((b, i) => (b + beats[i]) / 2);
    const moderate = pressesOf("reactive-moderate");
    const half = pressesOf("reactive-half");
    const off = pressesOf("reactive-offbeat");
    expect(near(moderate, beats)).toBeGreaterThan(0.8);
    expect(near(off, mids)).toBeGreaterThan(0.8);
    expect(half.length).toBeLessThan(0.7 * moderate.length);
  });

  it("are the only players of a reactive level", () => {
    expect(() => playerFor(ctx, sch, "moderate")).toThrow(/reactivePlayerFor/);
  });
});


describe("the director's schedule", () => {
  const file = synthStream({ until: 40, evidence: () => 0.8 });
  const input = inputForStream(splitStream(file).stream, { level: "levels/reactive-check.yaml", checks: { noHeroDamage: true } });
  const beats = file.beats.map((b) => b.t);
  const first = (t: number) => beats.find((b) => b >= t - 1e-9)!;
  // A player who stomps on every beat: the hero stands still, so it never walks into an orb.
  const stomper: Player = {
    nextDecision: (after) => first(after + 0.01) + 0.01,
    decide: (obs) => [
      { s: obs.s, kind: "press", key: "space" },
      { s: obs.s + 0.06, kind: "release", key: "space" },
    ],
  };
  const { plan } = planWithSim(input, { player: stomper });

  it("puts level-ups and zone waves on the first committed beat at or after their times", () => {
    expect(plan.levelUpsDue.map((l) => l.s)).toEqual([5, 10, 15].map(first));
    expect(plan.open.waves.map((w) => w.s)).toEqual([8, 16, 24, 32].map(first));
  });

  it("takes the orb ahead two bars of committed beats after the level-up's bar, counted as they arrive", () => {
    const u = plan.upgrades.find((x) => x.sTaken !== undefined);
    expect(u).toBeDefined();
    const i = beats.findIndex((b) => b >= u!.s - 1e-9);
    const bar0 = i - (i % 2);
    expect(u!.walked).toBe(false);
    expect(u!.sTaken).toBeCloseTo(beats[bar0 + 4], 6);
  });
});

describe("the parity report and the smoke test", () => {
  const file = synthStream({ until: 20 });
  const { stream } = splitStream(file);
  const input = inputForStream(stream, { checks: { noHeroDamage: true } });
  const ref = file.reference!;
  const { plan } = planWithSim(input, { player: new KeyPlayer(ref.beats.map((b) => b + 0.01)) });

  it("measures strikes, ratings and tiers against the reference", () => {
    // The synthetic stream's committed beats are its reference beats.
    expect(strikesOnBeat(plan, ref)).toBe(1);
    const same = pressesRatedSame(plan, ref, input.content);
    expect(same.n).toBeGreaterThan(20);
    expect(same.share).toBeGreaterThan(0.9);
    expect(timeAtTier(plan, ref, 0)).toBe(1);
    const rows = parityRows(undefined, plan, ref, input.content);
    expect(rows[0].authored).toBe("—");
    expect(formatParity(rows)).toContain("Strikes within 20 ms");
  });

  it("plays 10 s through the live runner with judged presses, and fails when nothing can be judged", () => {
    const bot = (ctx: Parameters<typeof reactivePlayerFor>[0]) => reactivePlayerFor(ctx, ref, "reactive-moderate");
    expect(smokeTest(input, undefined, bot, 5).failures).toEqual([]);
    const unclear = splitStream(synthStream({ until: 20, evidence: () => 0 })).stream;
    expect(smokeTest(inputForStream(unclear), undefined, bot, 5).failures.some((f) => f.startsWith("no judged presses"))).toBe(true);
  });
});

describe("the reactive run's end", () => {
  // Clear only from 20 s to 30 s: judged time starts at 16 s and skips the unclear stretch.
  const file = synthStream({ until: 40, evidence: (t) => (t >= 20 && t < 30 ? 0.8 : 0) });
  const { stream } = splitStream(file);

  it("grades the groove over judged time only", () => {
    const groove = { s: [0, 25, 35], v: [0.2, 0.8, 1] };
    // From 20 to 25 s at 0.2, from 25 to 30 s at 0.8: 0.5.
    expect(judgedAverageGroove(groove, stream, 0, 40)).toBeCloseTo(0.5, 2);
    expect(judgedAverageGroove(groove, stream, 26, 40)).toBeCloseTo(0.8, 6);
    expect(judgedAverageGroove(groove, stream, 0, 19)).toBe(0);
  });

  it("shows the track's title card and the end card when the audio ends", () => {
    const input = inputForStream(stream, { checks: { noHeroDamage: true } });
    const { plan } = planWithSim(input, { player: new KeyPlayer([]) });
    const ui = plan.events.ui.filter((u) => u.kind === "title" || u.kind === "end").map((u) => [u.kind, u.s]);
    expect(ui).toEqual([
      ["title", REACTIVE_TITLE.s],
      ["end", 40 + REACTIVE_END_DELAY],
    ]);
  });
});


describe("the settled groove (stage 2)", () => {
  const file = synthStream({ until: 30, hindsight: true, evidence: () => 0.8 });
  const { stream } = splitStream(file);
  const input = inputForStream(stream, { checks: { noHeroDamage: true } });
  // Every beat for the first 12 s, then nothing.
  const presses = file.beats.map((b) => b.t + 0.01).filter((t) => t < 12);
  const { plan } = planWithSim(input, { player: new KeyPlayer(presses) });

  it("judges each press provisionally at once and settles it 2 s later", () => {
    const settles = plan.settles ?? [];
    expect(settles.length).toBe(presses.length);
    for (const x of settles) expect(x.s - x.p).toBeCloseTo(2, 9);
    expect(settles.filter((x) => x.settled === "perfect").length).toBeGreaterThan(0.8 * settles.length);
    // The provisional judgment shows on the press (its class), and changes nothing.
    const first = plan.inputs.find((i) => i.kind === "press")!;
    expect(first.class).toBe("counted");
  });

  it("changes the groove only on settles and on drains 2 s after each beat", () => {
    expect(checkSettleTiming(plan, stream).failures).toEqual([]);
    const firstChange = Array.from(plan.groove.s).find((_, i) => i > 0 && plan.groove.v[i] !== plan.groove.v[i - 1])!;
    expect(firstChange).toBeGreaterThanOrEqual(presses[0] + 2 - 1e-9);
    // The last raise was the press before 12 s: the drain starts a bar (1 s) of judged time after it, 2 s later.
    const last = presses[presses.length - 1];
    const peak = grooveAt(plan.groove, last + 2 + 0.01);
    expect(grooveAt(plan.groove, last + 2 + 0.9)).toBe(peak);
    expect(grooveAt(plan.groove, last + 2 + 1.6)).toBeLessThan(peak);
  });

  it("shows a settled rating that corrects the provisional one in its colour", () => {
    expect(settledStyle("offbeat-great")).toBe("great");
    expect(settledStyle("unjudged")).toBe("extra");
    expect(settledStyle("stray")).toBe("stray");
  });
});
