import { describe, expect, it } from "vitest";
import { loadContent, loadLevel } from "../src/content/loader.ts";
import type { Director } from "../src/content/schema.ts";
import { checkReactiveCausality, checkReactiveSounds, checkSettleTiming, perturbStream, smokeTest } from "../src/planner/reactive-checks.ts";
import { formatParity, parityRows, pressesRatedSame, sectionDetection, strikesOnBeat, timeAtTier } from "../src/planner/parity.ts";
import { detectedSections } from "../src/reactive/sections.ts";
import { GATE_EXEMPT_SOUNDS, gatedSound, soundFade } from "../src/planner/emit.ts";
import { planWithSim, playerFor, prepare } from "../src/planner/plan.ts";
import { directorPopulation, directorRoster } from "../src/reactive/director.ts";
import { judgedAverageGroove, REACTIVE_END_DELAY, REACTIVE_TITLE } from "../src/reactive/grade.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { reactiveSongMap } from "../src/reactive/songmap.ts";
import { effectiveDownbeat, evidenceAt, firstBeatAtOrAfter, parseStream, splitStream, type StreamFile } from "../src/reactive/stream.ts";
import type { KeyEvent, Observation, Player } from "../src/sim/player.ts";
import { Evaluator, grooveAt, pulsed, settledStyle } from "../src/plan/evaluate.ts";
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

  it("changes the structure and the ticks' levels and keys after C", () => {
    const ticks = stream.ticks.map((k) => ({ ...k, level: "medium" as const, ...(k.t >= 10 ? { key: "G major" } : {}) }));
    const ev = (t: number) => ({ t, knownAt: t + 1 });
    const structure = {
      changes: [3, 9, 14, 18, 22, 26].map((t) => ({ t, knownAt: t + 6, strength: 1 })),
      jumps: [5, 13, 17, 21, 25].map((t) => ({ ...ev(t), db: 7 })),
      ends: [11, 15, 19, 23].map(ev),
    };
    const q = perturbStream({ ...stream, ticks, structure }, C, "test");
    for (const kind of ["changes", "jumps", "ends"] as const) {
      const before = structure[kind].filter((e) => e.knownAt <= C);
      const got = q.structure![kind];
      expect(got.filter((e) => e.knownAt <= C)).toEqual(before);
      const later = got.filter((e) => e.knownAt > C);
      expect(later).not.toEqual(structure[kind].filter((e) => e.knownAt > C));
      expect(later.length).toBeGreaterThan(0);
      for (const [i, e] of later.entries()) {
        expect(e.t).toBeLessThanOrEqual(e.knownAt);
        if (i > 0) expect(e.knownAt).toBeGreaterThan(later[i - 1].knownAt);
      }
    }
    expect(q.structure!.changes.filter((e) => e.knownAt > C).some((e) => e.strength !== 1)).toBe(true);
    expect(q.structure!.jumps.filter((e) => e.knownAt > C).some((e) => e.db !== 7)).toBe(true);
    const after = q.ticks.filter((k) => k.t > C);
    expect(after.some((k) => k.level !== "medium")).toBe(true);
    expect(after.some((k) => k.key !== "G major")).toBe(true);
    expect(q.ticks.filter((k) => k.t <= C)).toEqual(ticks.filter((k) => k.t <= C));
    // With no entry left after C, one is added.
    const lone = perturbStream({ ...stream, structure: { changes: [{ t: 3, knownAt: 9, strength: 1 }], jumps: [], ends: [] } }, C, "test");
    expect(lone.structure!.changes.filter((e) => e.knownAt > C).length).toBeGreaterThan(0);
  });
});

describe("detected sections (stage 4)", () => {
  // Clear from 4 s on, the first 4 s below the gate.
  const { stream } = splitStream(synthStream({ until: 60, evidence: (t) => (t < 4 ? 0 : 0.8) }));
  const at = (structure: NonNullable<typeof stream.structure>) => detectedSections({ ...stream, structure });

  it("start on a change or a jump of 6 dB or more, known with the beat clear, at least 8 s apart", () => {
    const d = at({
      changes: [
        { t: 0.5, knownAt: 3, strength: 1 }, // below the gate
        { t: 4, knownAt: 10, strength: 1 },
        { t: 9, knownAt: 15, strength: 1 }, // 5 s after the last start
        { t: 20, knownAt: 26, strength: 1 },
      ],
      jumps: [
        { t: 16, knownAt: 17, db: -5 }, // too small
        { t: 18, knownAt: 19, db: -8 },
        { t: 40, knownAt: 41, db: 6 },
      ],
      ends: [],
    });
    expect(d.map((x) => [x.source, x.knownAt])).toEqual([
      ["change", 10],
      ["jump", 19],
      ["jump", 41],
    ]);
  });

  it("take effect on the effective downbeat after the event is known", () => {
    const d = at({ changes: [{ t: 4, knownAt: 10.2, strength: 1 }], jumps: [], ends: [] });
    expect(d[0].beat).toBe(effectiveDownbeat(stream, 10.2));
    expect(d[0].s).toBe(stream.beats[d[0].beat].t);
    expect(d[0].s).toBeGreaterThanOrEqual(10.2 + 0.35);
    expect(d[0].beat % 2).toBe(0);
  });

  it("are none without structure", () => {
    expect(detectedSections(stream)).toEqual([]);
  });

  it("count against the reference's section starts", () => {
    const structure = {
      changes: [{ t: 9, knownAt: 15, strength: 1 }],
      jumps: [
        { t: 2, knownAt: 3, db: 9 },
        { t: 39, knownAt: 40, db: 9 },
      ],
      ends: [],
    };
    const ref = {
      source: "exact" as const,
      beats: [],
      sections: [
        { label: "intro", s0: 0.5, s1: 10, feel: "free" as const },
        { label: "a", s0: 10, s1: 30, feel: "pulsed" as const },
        { label: "b", s0: 30, s1: 60, feel: "pulsed" as const },
      ],
    };
    // (The jump at 3 s is below the gate; the change takes effect at 15.5 s, within 8 s of a's start;
    // the jump at 40 s is on no start.)
    const d = sectionDetection({ ...stream, structure }, ref);
    expect([d.found, d.of, d.free, d.falseStarts]).toEqual([1, 2, 0, 1]);
    // One detected start counts for one section start only, however close two starts are; one just
    // before a start (within 8 s of it) is not false.
    const close = { ...ref, sections: [ref.sections[0], { label: "a", s0: 13, s1: 15, feel: "pulsed" as const }, { label: "b", s0: 15, s1: 60, feel: "pulsed" as const }] };
    const once = sectionDetection({ ...stream, structure: { changes: [{ t: 9, knownAt: 15, strength: 1 }], jumps: [], ends: [] } }, close);
    expect([once.found, once.falseStarts]).toEqual([1, 0]);
    const early = sectionDetection({ ...stream, structure: { changes: [{ t: 9, knownAt: 25, strength: 1 }], jumps: [], ends: [] } }, { ...ref, sections: [ref.sections[0], { label: "a", s0: 30, s1: 60, feel: "pulsed" as const }] });
    expect([early.found, early.falseStarts]).toEqual([0, 0]);
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
    // Through the unclear stretch the hero moves freely (stage 3), which freezes the groove; the first
    // drain comes once strides are back (2 s into the clear beat), after the drain's grace bar (1 s) again.
    const back = plan.modes?.find((m) => !m.free)?.s ?? Infinity;
    expect(back).toBeGreaterThanOrEqual(18 - 1e-6);
    const firstDrain = Array.from(plan.groove.s).find((t, i) => t > 8 && plan.groove.v[i] < g8)!;
    expect(firstDrain).toBeGreaterThan(back + 1 + 1e-6);
    expect(firstDrain).toBeLessThan(back + 2.5);
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

describe("the world follows the music and the player (stage 3)", () => {
  // Clear for 8 s, unclear to 16 s, clear again; presses on every beat for the first 12 s.
  const file = synthStream({ until: 30, evidence: (t) => (t >= 8 && t < 16 ? 0 : t >= 16 && t < 17 ? 0.3 : 0.8) });
  const input = inputForStream(splitStream(file).stream, { checks: { noHeroDamage: true } });
  const presses = file.beats.map((b) => b.t + 0.01).filter((t) => t < 12);
  const { plan } = planWithSim(input, { player: new KeyPlayer(presses) });
  const ev = new Evaluator(plan, input.content);

  it("fades the beat's pulse by the evidence, and follows the presses while the beat is unclear", () => {
    const clear = ev.evaluate(5.2).reactive!;
    expect(clear.mix).toBe(1);
    expect(clear.amp).toBe(1);
    const p = presses.find((t) => t > 11)!;
    const unclear = ev.evaluate(p + 0.05).reactive!;
    expect(unclear.mix).toBe(0);
    expect(unclear.amp).toBe(0);
    expect(unclear.press).toBeCloseTo(Math.exp(-0.05 / 0.25), 6);
    expect(pulsed(unclear, 1)).toBeCloseTo(unclear.press, 9);
    // Evidence 0.3 (between the gate and twice it): half the amplitude.
    expect(ev.evaluate(16.6).reactive!.amp).toBeCloseTo(0.5, 6);
  });

  it("sends a ripple from each press, and shows nothing of a later one", () => {
    const p = presses[4];
    expect(ev.evaluate(p + 0.1).rings.length).toBeGreaterThan(0);
    expect(ev.evaluate(p - 0.005).rings.some((r) => r.radius < 14.5)).toBe(false);
  });

  it("walks freely after 2 s of an unclear beat, on a committed downbeat, and strides again after 2 s clear", () => {
    const modes = plan.modes!;
    expect(modes.map((m) => m.free)).toEqual([true, false]);
    expect(modes[0].s).toBeGreaterThanOrEqual(10 - 1e-6);
    expect(modes[1].s).toBeGreaterThanOrEqual(18 - 1e-6);
    const beatIdx = (s: number) => file.beats.findIndex((b) => Math.abs(b.t - s) < 1e-9);
    for (const m of modes) expect(beatIdx(m.s) % 2).toBe(0);
    expect(ev.evaluate(12.5).hud.free).toBe(true);
  });

  it("settles presses in free movement unjudged, so the readings still see them when strides resume", () => {
    // With hindsight: one run with the stretch unclear, one clear throughout; presses on every beat.
    const run = (ev: (t: number) => number) => {
      const f = synthStream({ until: 30, hindsight: true, evidence: ev });
      const times = f.beats.map((b) => b.t + 0.01).filter((t) => t < 28);
      return { times, plan: planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan };
    };
    const unclear = run((t) => (t >= 8 && t < 16 ? 0 : 0.8));
    const clear = run(() => 0.8);
    const modes = unclear.plan.modes!;
    expect(modes.map((m) => m.free)).toEqual([true, false]);
    const settles = unclear.plan.settles!;
    expect(settles.length).toBe(unclear.times.length);
    const inFree = settles.filter((x) => x.p >= modes[0].s && x.p < modes[1].s);
    expect(inFree.length).toBeGreaterThan(10);
    for (const x of inFree) {
      expect(x.settled).toBe("unjudged");
      expect(x.change).toBe(0);
    }
    // Once the hindsight window has passed the unclear stretch, the readings and labels match the clear run.
    const after = (p: { settles?: { p: number; reading: string; settled: string }[] }) => p.settles!.filter((x) => x.p > modes[1].s + 8).map((x) => `${x.reading} ${x.settled}`);
    expect(after(unclear.plan)).toEqual(after(clear.plan));
  });

  it("does not switch to free movement past the stream's last tick", () => {
    // Clear to 10 s, then no ticks at all: neither side of the gate.
    const f = synthStream({ until: 20, evidence: () => 0.8 });
    const cut = parseStream({ ...f, ticks: f.ticks.filter((t) => t.t <= 10) } as unknown as StreamFile);
    const { plan } = planWithSim(inputForStream(splitStream(cut).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer([]) });
    expect(plan.modes ?? []).toEqual([]);
  });

  it("draws the pulse, the flash, the punch, the ripples and the light from nothing after s", () => {
    // A plan whose stream effects and inputs after C are changed shows the same frames up to C.
    const C = 9.3;
    const fx = plan.reactiveFx!;
    const flashes = Float64Array.from([2.1, 5.4, 9.2, 9.4, 12]);
    const base = new Evaluator({ ...plan, reactiveFx: { ...fx, flashes } }, input.content);
    const changed = new Evaluator(
      {
        ...plan,
        reactiveFx: { ...fx, evidence: fx.evidence.map((e, i) => (fx.tickT[i] > C ? 1 - e : e)), energy: fx.energy.map((e, i) => (fx.tickT[i] > C ? 0 : e)), flashes: Float64Array.from([2.1, 5.4, 9.2, 9.31, 9.35]) },
        inputs: [...plan.inputs.filter((i) => i.s <= C), { s: C + 0.001, w: C + 0.001, kind: "press", key: "left", steps: true, rating: "perfect" }],
      },
      input.content,
    );
    const pick = (e: Evaluator, s: number) => {
      const f = e.evaluate(s);
      return JSON.stringify({ reactive: f.reactive, rings: f.rings, lights: f.lights, flash: f.flash, camera: f.camera });
    };
    for (let s = 0.5; s <= C + 1e-9; s += 0.05) expect(pick(changed, s)).toBe(pick(base, s));
    expect(pick(changed, C + 0.04)).not.toBe(pick(base, C + 0.04));
  });

  it("scales the emitted sounds' gain by the fade", () => {
    // The same run at evidence 0.8 and at 0.3 (half the fade): only the percussive sounds' gain changes.
    const run = (e: number) => {
      const f = synthStream({ until: 20, evidence: () => e });
      const times = f.beats.map((b) => b.t + 0.01).filter((t) => t < 18);
      return planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan.events.sounds;
    };
    const full = run(0.8);
    const half = run(0.3);
    expect(half.length).toBe(full.length);
    let faded = 0;
    for (let k = 0; k < full.length; k++) {
      expect(half[k].s).toBe(full[k].s);
      const percussive = full[k].key !== undefined && !GATE_EXEMPT_SOUNDS.has(full[k].key!);
      expect(half[k].gain / full[k].gain, `${full[k].key} at ${full[k].s}`).toBeCloseTo(percussive ? 0.5 : 1, 9);
      if (percussive) faded++;
    }
    expect(faded).toBeGreaterThan(10);
  });

  it("caps accent flashes at one per 0.5 s, each from when its onset is known", () => {
    const f = synthStream({ until: 10 });
    const onsets = [1, 1.2, 1.6, 1.7, 3].map((t) => ({ t: t - 0.1, knownAt: t, strong: true, strength: 1 }));
    const s2 = parseStream({ ...f, onsets } as unknown as StreamFile);
    const { plan: p } = planWithSim(inputForStream(splitStream(s2).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer([]) });
    expect(Array.from(p.reactiveFx!.flashes)).toEqual([1, 1.6, 3]);
  });

  it("fades percussive sounds from full at twice the gate to silent at the gate", () => {
    const { ctx } = prepare(input);
    const at = (e: number) => file.beats.find((b) => Math.abs(b.evidence - e) < 1e-9)!.t;
    expect(soundFade(ctx, "hit-light", at(0.8))).toBe(1);
    expect(soundFade(ctx, "hit-light", at(0))).toBe(0);
    expect(soundFade(ctx, "level-up", at(0))).toBe(1);
  });
});
