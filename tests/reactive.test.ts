import { describe, expect, it } from "vitest";
import { loadContent, loadLevel } from "../src/content/loader.ts";
import type { Director } from "../src/content/schema.ts";
import { checkReactiveCausality, checkReactiveSounds, checkSettleTiming, perturbStream, smokeTest } from "../src/planner/reactive-checks.ts";
import { formatParity, parityRows, pressesRatedSame, sectionDetection, strikesOnBeat, timeAtTier } from "../src/planner/parity.ts";
import { detectedSections } from "../src/reactive/sections.ts";
import { reactiveFinale, reactiveSetPieces } from "../src/reactive/setpieces.ts";
import { GATE_EXEMPT_SOUNDS, gatedSound, soundFade } from "../src/planner/emit.ts";
import { planWithSim, playerFor, prepare } from "../src/planner/plan.ts";
import { directorPopulation, keyAt, levelBlend, paletteLevelAt, paletteSwitches, sectionRoster, zoneWavePlan } from "../src/reactive/director.ts";
import { gradedAverageGroove, REACTIVE_TITLE } from "../src/reactive/grade.ts";
import { reactivePlayerFor } from "../src/reactive/players.ts";
import { reactiveSongMap } from "../src/reactive/songmap.ts";
import { effectiveDownbeat, evidenceAt, firstBeatAtOrAfter, judgedAt, judging, lockOnAt, parseStream, splitStream, type StreamFile, type StreamStructure, type TickLevel, unjudgedSinceAt } from "../src/reactive/stream.ts";
import { Warp } from "../src/core/warp.ts";
import { BEATLESS } from "../src/sim/simulate.ts";
import type { KeyEvent, Observation, Player } from "../src/sim/player.ts";
import { Evaluator, grooveAt, pulsed, settledStyle } from "../src/plan/evaluate.ts";
import { readContentFiles } from "../scripts/lib/files.ts";
import { inputForStream, reactiveInput, readStreamFile } from "../scripts/lib/reactive-node.ts";

/** A synthetic stream: beats every `period` from 0.5 s, each committed `lead` before it, and ticks with the given evidence. */
function synthStream(o: { until: number; period?: number; lead?: number; evidence?: (t: number) => number; hindsight?: boolean; level?: (t: number) => TickLevel; key?: (t: number) => string | undefined; structure?: StreamStructure; onsets?: StreamFile["onsets"]; forecast?: { t: number; knownAt: number; cycle: number; confidence: number }[] }): StreamFile {
  const period = o.period ?? 0.5;
  const lead = o.lead ?? 0.85;
  const ev = o.evidence ?? (() => 0.6);
  const beats: { t: number; knownAt: number; period: number; evidence: number }[] = [];
  for (let t = 0.5; t < o.until; t += period) {
    const knownAt = Math.max(0, Math.round((t - lead) * 100) / 100);
    beats.push({ t, knownAt, period, evidence: ev(knownAt) });
  }
  const ticks = [];
  for (let k = 1; k * 0.05 <= o.until + 1e-9; k++) {
    const t = Math.round(k * 5) / 100;
    const key = o.key?.(t);
    ticks.push({ t, evidence: ev(k * 0.05), energy: 0.8, ...(o.level ? { level: o.level(t) } : {}), ...(key ? { key } : {}) });
  }
  return parseStream({
    version: 1,
    audio: { file: "x.ogg", duration: o.until, sampleRate: 44100 },
    params: { gate: 0.2, tickS: 0.05, toolVersion: 1 },
    beats,
    ticks,
    onsets: o.onsets ?? [],
    ...(o.forecast ? { accents: { forecast: o.forecast } } : {}),
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
    ...(o.structure ? { structure: o.structure } : {}),
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
    // Accent forecasts: the same rules.
    const forecast = [6, 10, 14, 18, 22].map((t) => ({ t, knownAt: t - 1, cycle: 8, confidence: 0.75 }));
    const fq = perturbStream({ ...stream, accents: { forecast } }, C, "test").accents!.forecast;
    expect(fq.filter((e) => e.knownAt <= C)).toEqual(forecast.filter((e) => e.knownAt <= C));
    expect(fq.filter((e) => e.knownAt > C)).not.toEqual(forecast.filter((e) => e.knownAt > C));
    expect(fq.filter((e) => e.knownAt > C).some((e) => e.confidence !== 0.75)).toBe(true);
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
  const d = { population: { start: 40, perMinute: 120, max: 480 }, sections: [{ slime: 1 }, { goblin: 1 }] } as unknown as Director;
  const { stream } = splitStream(synthStream({ until: 30, level: (t) => (t < 10 ? "quiet" : t < 20 ? "loud" : "medium"), key: (t) => (t >= 10 ? (t < 20 ? "D major" : "G major") : undefined) }));

  it("grows the population with time and scales it, up to the maximum", () => {
    expect(directorPopulation(d, 0, 0.7)).toBeCloseTo(28);
    expect(directorPopulation(d, 60, 1.3)).toBeCloseTo(208);
    expect(directorPopulation(d, 600, 1)).toBe(480);
  });

  it("steps the roster with each detected section, holding the last", () => {
    expect(sectionRoster(d, 0)).toEqual({ slime: 1 });
    expect(sectionRoster(d, 1)).toEqual({ goblin: 1 });
    expect(sectionRoster(d, 5)).toEqual({ goblin: 1 });
  });

  it("blends a level's value over the last window of ticks, from nothing later", () => {
    const v = (l: TickLevel) => ({ quiet: 0, medium: 1, loud: 2 })[l];
    expect(levelBlend(stream, 9.9, 2, v)).toBeCloseTo(0, 9);
    expect(levelBlend(stream, 11, 2, v)).toBeCloseTo(1, 1);
    expect(levelBlend(stream, 12.5, 2, v)).toBeCloseTo(2, 9);
    // Before any tick: medium.
    expect(levelBlend(stream, 0.01, 2, v)).toBe(1);
  });

  it("reads the key of the latest tick that has one", () => {
    expect(keyAt(stream, 9.9)).toBeUndefined();
    expect(keyAt(stream, 15)).toBe("D major");
    expect(keyAt(stream, 25)).toBe("G major");
  });

  it("switches the palette on the effective downbeat after a level holds for 2 s", () => {
    const sw = paletteSwitches(stream, 2);
    expect(sw.map((x) => x.level)).toEqual(["quiet", "loud", "medium"]);
    for (const [x, held] of [[sw[0], 2.05], [sw[1], 12], [sw[2], 22]] as const) expect(x.s).toBe(stream.beats[effectiveDownbeat(stream, held)].t);
    expect(paletteLevelAt(stream, 2, sw[1].s - 0.01)).toBe("quiet");
    expect(paletteLevelAt(stream, 2, sw[1].s)).toBe("loud");
  });

  it("runs a zone wave on each section start and one after the fallback time without one", () => {
    const starts = [10.5, 30.5].map((s) => ({ s, beat: 0, knownAt: s - 0.5, source: "change" as const, eventT: s - 6 }));
    expect(zoneWavePlan(stream, starts, 12, 29.9)).toEqual([{ s: 10.5, section: 1 }, { s: 22.5 }]);
    expect(zoneWavePlan(stream, starts, 12, 100)).toEqual([{ s: 10.5, section: 1 }, { s: 22.5 }, { s: 30.5, section: 2 }]);
    // A start on the fallback's own beat is the wave.
    expect(zoneWavePlan(stream, [{ ...starts[0], s: 12 }], 12, 20)).toEqual([{ s: 12, section: 1 }]);
  });
});

describe("reactive levels", () => {
  const content = loadContent(readContentFiles());
  const song = reactiveSongMap(splitStream(synthStream({ until: 10 })).stream);
  const level = (extra: string) => ({
    name: "levels/test.yaml",
    text: `song: reactive\nseed: 1\nreactive: true\nzones: [twilight-glade]\nsections:\n  - section: reactive\n    bars: all\n    pace: 50\n    presses: "x"\n    zone: twilight-glade\n    sfxPalette: crisp-steel\n${extra}`,
  });
  const director =
    "director:\n  population: { start: 1, perMinute: 1, max: 10, levelScale: { quiet: 1, medium: 1, loud: 1 } }\n  sections: [{ slime: 1 }]\n" +
    "  camera: { quiet: { zoom: 1.2, lead: 50 }, medium: { zoom: 1.2, lead: 50 }, loud: { zoom: 1.2, lead: 50 } }\n  palettes: { quiet: crisp-steel, medium: crisp-steel, loud: crisp-steel }\n  calm: { quiet: 0, medium: 0, loud: 0 }\n";

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

  it("leaves presses unjudged while the beat is unclear and for a second after, and judges the rest", () => {
    const { stream } = splitStream(file);
    for (const p of presses) {
      if (!judgedAt(stream, p.s)) expect(p.unjudged, `press at ${p.s}`).toBe(true);
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
    // Judging resumes a second into the clear beat (17 s); the first drain waits the grace bar (1 s) after it.
    const firstDrain = Array.from(plan.groove.s).find((t, i) => t > 8 && plan.groove.v[i] < g8)!;
    expect(firstDrain).toBeGreaterThan(17 + 1 + 1e-6);
    expect(firstDrain).toBeLessThan(17 + 2.5);
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
  const pressesOf = (name: "reactive-moderate" | "reactive-half" | "reactive-offbeat" | "reactive-skilled") =>
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

  it("include the reactive demo's skilled player, every beat and tighter than moderate", () => {
    const beats = ref.beats;
    const spread = (times: number[]) => times.reduce((a, t) => a + Math.min(...beats.map((b) => Math.abs(b - t))), 0) / times.length;
    const skilled = pressesOf("reactive-skilled");
    expect(near(skilled, beats)).toBeGreaterThan(0.9);
    expect(spread(skilled)).toBeLessThan(spread(pressesOf("reactive-moderate")));
  });

  it("are the only players of a reactive level", () => {
    expect(() => playerFor(ctx, sch, "moderate")).toThrow(/reactivePlayerFor/);
  });
});


describe("the director's schedule (stage 4)", () => {
  // Detected sections from changes known at 14 s and 30 s (the check level: at least 4 s apart, a
  // fallback wave after 12 s without one, level-ups at most 4 s after their experience); quiet to
  // 20 s, then loud.
  const structure = { changes: [{ t: 8, knownAt: 14, strength: 1 }, { t: 24, knownAt: 30, strength: 1 }], jumps: [], ends: [] };
  const file = synthStream({ until: 60, evidence: () => 0.8, structure, level: (t) => (t < 20 ? "quiet" : "loud") });
  const { stream } = splitStream(file);
  const input = inputForStream(stream, { level: "levels/reactive-check.yaml", checks: { noHeroDamage: true } });
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
  const warp = new Warp(plan.warp);
  const starts = [14, 30].map((k) => beats[effectiveDownbeat(stream, k)]);

  it("sweeps a zone wave on each detected section start, and one after 12 s without one", () => {
    const waves = plan.open.waves;
    expect(waves.filter((w) => w.section !== undefined).map((w) => [w.s, w.section])).toEqual([
      [starts[0], 1],
      [starts[1], 2],
    ]);
    expect(waves.map((w) => w.s)).toEqual(zoneWavePlan(stream, detectedSections(stream, { jumpDb: 6, minGapS: 4 }), 12, plan.duration).map((w) => w.s));
    expect(waves.some((w) => w.section === undefined)).toBe(true);
    // Each start shows a short banner with its zone's name.
    expect(plan.events.ui.filter((u) => u.kind === "banner").map((b) => b.s)).toEqual(starts);
  });

  it("steps the roster on each start", () => {
    // (The horde's own enemies, not the set pieces' formations, rings and archers.)
    const inSetPiece = new Set(plan.formations.flatMap((f) => f.members));
    const types = (a: number, b: number) => new Set(plan.enemies.filter((e) => !inSetPiece.has(e.id) && e.type !== "skeleton-archer" && warp.toSong(e.spawnW) >= a && warp.toSong(e.spawnW) < b).map((e) => e.type));
    for (const t of types(0, starts[0])) expect(["slime", "bat"]).toContain(t);
    for (const t of types(starts[0] + 0.5, starts[1])) expect(["goblin", "slime", "bat"]).toContain(t);
    expect(types(starts[0] + 0.5, starts[1]).has("goblin")).toBe(true);
    for (const t of types(starts[1] + 0.5, 60)) expect(["goblin", "orc", "skeleton", "orc-elite"]).toContain(t);
  });

  it("lets a level-up fall due at the next section start once its experience is reached, or 4 s later", () => {
    // A player walking back and forth on every beat, collecting gems.
    const walker: Player = {
      nextDecision: (after) => first(after + 0.01) + 0.01,
      decide: (obs) => {
        const key = Math.floor(obs.s / 4) % 2 ? "left" : "right";
        return [
          { s: obs.s, kind: "press", key },
          { s: obs.s + 0.06, kind: "release", key },
        ];
      },
    };
    const walked = planWithSim(input, { player: walker }).plan;
    expect(walked.levelUpsDue).toEqual([]);
    const ups = walked.upgrades.map((u) => u.s);
    // Both ways happen: at a section start, and after the wait with none.
    expect(ups.some((u) => starts.includes(u))).toBe(true);
    expect(ups.some((u) => !starts.includes(u))).toBe(true);
    const fill = Array.from(walked.hud.xpFill);
    const times = Array.from(walked.hud.xpTimes);
    let from = 0;
    for (const u of walked.upgrades) {
      const k = times.findIndex((t, i) => t >= from - 1e-9 && fill[i] >= 1 - 1e-9);
      const full = times[k];
      const start = starts.find((x) => x >= full - 1e-9);
      const due = start !== undefined && start <= full + 4 + 1e-9 ? start : beats.find((b, i) => i % 2 === 0 && b >= full + 4 - 1e-9)!;
      expect(u.s, `level-up after the bar filled at ${full}`).toBeCloseTo(due, 6);
      from = u.s;
    }
  });

  it("zooms in while quiet and out while loud, blending over 2 s", () => {
    // (Punches only zoom in, and the stomps' punches never quite die away: the least zoom over a
    // stretch is the camera's own, give or take a little.)
    const zoomOver = (a: number, b: number) => Math.min(...Array.from(plan.camera.zoom).slice(Math.round(warp.toWorld(a) * plan.hero.rate), Math.round(warp.toWorld(b) * plan.hero.rate)));
    expect(zoomOver(6, 9.5)).toBeGreaterThanOrEqual(1.3 - 1e-6);
    expect(zoomOver(6, 9.5)).toBeLessThan(1.31);
    expect(zoomOver(25, 28)).toBeGreaterThanOrEqual(1.05 - 1e-6);
    expect(zoomOver(25, 28)).toBeLessThan(1.06);
    expect(zoomOver(20.9, 21.1)).toBeGreaterThan(1.06);
    expect(zoomOver(20.9, 21.1)).toBeLessThan(1.29);
  });

  it("plays the quiet palette while quiet and the loud one while loud", () => {
    const lib = input.content.soundPalettes;
    const only = (pal: string) => new Set(Object.values(lib.get(pal)!.sounds).flat().map((x) => (typeof x === "string" ? x : x.id)));
    const switchS = paletteSwitches(stream, 2).find((x) => x.level === "loud")!.s;
    const soft = only("soft-chimes");
    const heavy = only("heavy-infernal");
    const quietSounds = plan.events.sounds.filter((x) => x.s > 3 && x.s < 20 && x.key);
    const loudSounds = plan.events.sounds.filter((x) => x.s >= switchS && x.key);
    expect(quietSounds.length).toBeGreaterThan(5);
    expect(loudSounds.length).toBeGreaterThan(5);
    for (const x of quietSounds) expect(soft.has(x.sample), x.sample).toBe(true);
    for (const x of loudSounds) expect(heavy.has(x.sample), x.sample).toBe(true);
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

describe("the loudness level and the key in the simulation (stage 4)", () => {
  const run = (o: { level?: (t: number) => TickLevel; key?: (t: number) => string | undefined }) => {
    const f = synthStream({ until: 40, evidence: () => 0.8, ...o });
    const times = f.beats.map((b) => b.t + 0.01);
    return planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan;
  };

  it("scales the population by the level", () => {
    const quiet = run({ level: () => "quiet" });
    const loud = run({ level: () => "loud" });
    expect(loud.enemies.length).toBeGreaterThan(1.5 * quiet.enemies.length);
  });

  it("pitches gem chimes in the key detected when each is caused", () => {
    const c = run({ key: (t) => (t >= 10 ? "C major" : undefined) });
    const fs = run({ key: (t) => (t >= 10 ? "F# major" : undefined) });
    const chimes = (p: typeof c) => p.events.sounds.filter((x) => x.key === "chime");
    expect(chimes(c).length).toBeGreaterThan(5);
    expect(chimes(c).map((x) => x.s)).toEqual(chimes(fs).map((x) => x.s));
    // Before 10 s, no key: the same; after it, the keys pitch them differently.
    expect(chimes(c).filter((x) => x.s < 10).map((x) => x.rate)).toEqual(chimes(fs).filter((x) => x.s < 10).map((x) => x.rate));
    const later = chimes(c).filter((x) => x.s > 11);
    expect(later.some((x, i) => x.rate !== chimes(fs).filter((y) => y.s > 11)[i].rate)).toBe(true);
  });
});

describe("level-ups on the shipped tracks (stage 4)", () => {
  // The check level's compressed director, where a gem can arrive between the wait's end and the
  // level-up's downbeat: each level-up happens once, at most a bar after its wait.
  for (const [track, seed] of [["orpheus-galop", 7], ["william-tell-finale", 1858], ["william-tell-finale", 7]] as const) {
    it(`grants each level-up once, by its wait at the latest (${track}, seed ${seed})`, () => {
      const run = reactiveInput(readStreamFile(track), { seed, level: "levels/reactive-check.yaml", checks: { noHeroDamage: true } });
      const { plan } = planWithSim(run.input, { player: reactivePlayerFor(prepare(run.input).ctx, run.reference!, "reactive-moderate") });
      const d = run.input.level.director!;
      expect(plan.upgrades.length).toBeLessThanOrEqual(d.levelUps.length);
      const fill = Array.from(plan.hud.xpFill);
      const times = Array.from(plan.hud.xpTimes);
      let from = 0;
      for (const u of plan.upgrades) {
        const full = times.find((t, i) => t >= from - 1e-9 && fill[i] >= 1 - 1e-9)!;
        // (A bar of committed beats is at most about 2 s.)
        expect(u.s, `level-up after the bar filled at ${full}`).toBeLessThanOrEqual(full + d.levelUpWaitS + 2);
        from = u.s;
      }
    });
  }
});

describe("heavy swings and cleaves on accents (stage 5)", () => {
  // Beats every 0.5 s, clear; the player presses right 10 ms after every beat (Perfect).
  const base = { until: 30, evidence: () => 0.8 };
  const plan5 = (o: { onsets?: StreamFile["onsets"]; forecast?: { t: number; knownAt: number; cycle: number; confidence: number }[] }, presses?: number[]) => {
    const f = synthStream({ ...base, ...o });
    const times = presses ?? f.beats.map((b) => b.t + 0.01).filter((t) => t > 2);
    return { file: f, plan: planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan };
  };
  const strong = (t: number) => ({ t, knownAt: Math.round((t + 0.02) * 100) / 100, strength: 100, strong: true });

  it("swings heavy on a beat with a forecast accent of confidence 0.6 or more known by the wind-up", () => {
    const forecast = [
      { t: 10.5, knownAt: 9.8, cycle: 8, confidence: 0.75 },
      { t: 14.5, knownAt: 13.8, cycle: 8, confidence: 0.5 },
      // (Known after the wind-up: too late to swing heavy.)
      { t: 18.5, knownAt: 18.45, cycle: 8, confidence: 1 },
    ];
    const { plan } = plan5({ forecast });
    const heavy = plan.swings.filter((w) => w.kind === "heavy");
    expect(heavy.map((w) => w.s)).toEqual([10.5]);
    expect(heavy[0].accent).toBe(0.75);
    // Its pending cleave: the Perfect press launches it.
    expect(plan.cleaves.map((c) => c.s)).toEqual([10.5]);
  });

  it("cleaves on a strong onset that was not forecast, launched by a Great press in its window", () => {
    const { plan } = plan5({ onsets: [strong(10.5), strong(14.5)] });
    expect(plan.swings.some((w) => w.kind === "heavy")).toBe(false);
    expect(plan.cleaves.map((c) => c.s)).toEqual([10.5, 14.5]);
    for (const c of plan.cleaves) {
      // The press (10 ms after the onset) came before the onset was known: launched when it was.
      expect(c.press).toBeCloseTo(c.s + 0.01, 6);
      const launch = c.s + 0.02;
      // The bands start on the first eighth-note slot at least 0.06 s after the launch.
      expect(c.band0).toBeGreaterThanOrEqual(launch + 0.06 - 1e-6);
      expect(Math.abs((c.band0 - 0.5) / 0.25 - Math.round((c.band0 - 0.5) / 0.25))).toBeLessThan(1e-6);
      expect(c.band0 - (launch + 0.06)).toBeLessThan(0.25 + 1e-6);
    }
  });

  it("launches an onset's cleave with a press after it is known, but not a Good one", () => {
    const later = plan5({ onsets: [strong(10.5)] }, [10.58]);
    expect(later.plan.cleaves.map((c) => [c.s, c.press])).toEqual([[10.5, 10.58]]);
    // 130 ms late: Good, and outside the Great window.
    expect(plan5({ onsets: [strong(10.5)] }, [10.63]).plan.cleaves).toEqual([]);
  });

  it("starts a forecast cleave's bands after the strike when the press came first", () => {
    const { plan } = plan5({ forecast: [{ t: 10.5, knownAt: 9.8, cycle: 8, confidence: 1 }] }, [10.43]);
    expect(plan.cleaves.map((c) => [c.s, c.press, c.launch])).toEqual([[10.5, 10.43, 10.5]]);
    expect(plan.cleaves[0].band0).toBeCloseTo(10.75, 6);
  });

  it("launches one cleave a press", () => {
    const fc = [{ t: 10.5, knownAt: 9.8, cycle: 8, confidence: 1 }];
    // The press launches the forecast cleave at the strike; an onset 70 ms later finds it spent.
    const a = plan5({ forecast: fc, onsets: [strong(10.57)] }, [10.49]).plan;
    expect(a.cleaves.map((c) => [c.s, c.press])).toEqual([[10.5, 10.49]]);
    // The press launches an onset's cleave; the strike after it finds it spent.
    const b = plan5({ forecast: fc, onsets: [strong(10.42)] }, [10.47]).plan;
    expect(b.cleaves.map((c) => [c.s, c.press])).toEqual([[10.42, 10.47]]);
  });

  it("needs the Great press within 90 ms of the onset", () => {
    // A Perfect press on the beat, 130 ms after the onset.
    expect(plan5({ onsets: [strong(10.38)] }, [10.51]).plan.cleaves).toEqual([]);
  });

  it("swings light on a forecast accent while the beat is unclear", () => {
    const f = synthStream({ until: 30, evidence: (t) => (t >= 6 && t < 14 ? 0 : 0.8), forecast: [{ t: 10.5, knownAt: 9.8, cycle: 8, confidence: 1 }] });
    const { plan } = planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(f.beats.map((b) => b.t + 0.01)) });
    expect(plan.swings.some((w) => w.kind === "heavy")).toBe(false);
  });

  it("leaves an onset its cleave when the forecast near it came too late to swing heavy", () => {
    const { plan } = plan5({ forecast: [{ t: 18.5, knownAt: 18.45, cycle: 8, confidence: 1 }], onsets: [strong(18.51)] }, [18.51]);
    expect(plan.swings.some((w) => w.kind === "heavy")).toBe(false);
    expect(plan.cleaves.map((c) => [c.s, c.onset])).toEqual([[18.51, true]]);
  });

  it("leaves a forecast accent's onset to its heavy swing", () => {
    const { plan } = plan5({ onsets: [strong(10.51)], forecast: [{ t: 10.5, knownAt: 9.8, cycle: 8, confidence: 1 }] });
    expect(plan.cleaves.length).toBe(1);
    expect(plan.swings.filter((w) => w.kind === "heavy").map((w) => w.s)).toEqual([10.5]);
  });
});

describe("set pieces (stage 6)", () => {
  const checkLevel = inputForStream(splitStream(synthStream({ until: 10 })).stream, { level: "levels/reactive-check.yaml" }).level.director!;
  const sp0 = checkLevel.setPieces!;
  const d = { ...checkLevel, setPieces: { ...sp0, gapS: 6, ring: { ...sp0.ring, afterS: 3, maxBeats: 12 }, stampede: { ...sp0.stampede, quietS: 8 } } } as Director;
  const beats = (b: { t: number }) => b.t;
  const fb = (id: string) => (id === "bee-wedge" ? { entranceBeats: 4, releaseBeats: 6 } : { entranceBeats: 8 });
  const loud = (from: number) => (t: number) => (t >= from ? "loud" : "quiet") as TickLevel;

  it("fires the finale on the first candidate ending known in the audio's last 10 s, or at its end, once", () => {
    const st = (ends: { t: number; knownAt: number }[]) => splitStream(synthStream({ until: 60, structure: { changes: [], jumps: [], ends } })).stream;
    expect(reactiveFinale(st([{ t: 20, knownAt: 21.5 }, { t: 52, knownAt: 53.5 }, { t: 55, knownAt: 56.5 }]), d, 60)).toMatchObject({ s: 53.5, endS: 56.5 });
    expect(reactiveFinale(st([{ t: 20, knownAt: 21.5 }]), d, 60)).toMatchObject({ s: 60, endS: 63 });
  });

  it("starts each on the effective downbeat after its trigger, 6 s apart, the boss first", () => {
    // Loud from 20 s: a ring and (after 45 s) the boss on loud section starts; a wedge on the medium one.
    const changes = [10, 30, 34, 50].map((k) => ({ t: k - 6, knownAt: k, strength: 1 }));
    const stream = splitStream(synthStream({ until: 70, level: loud(20), structure: { changes, jumps: [], ends: [] } })).stream;
    const sp = reactiveSetPieces(stream, d, 70, Infinity, fb);
    const at = (k: number) => stream.beats[effectiveDownbeat(stream, k)].t;
    // (30 s: a ring; 34 s is within 6 s of it, dropped; 50 s: the boss, which outranks the ring.)
    expect(sp.starts.filter((x) => x.kind !== "volleys")).toEqual([
      { kind: "wedge", s: at(10) },
      { kind: "ring", s: at(30) },
      { kind: "boss", s: at(50) },
    ]);
    expect(sp.boss!.sEnter).toBeGreaterThanOrEqual(50 + 0.6 - 1e-9);
    for (const sl of sp.boss!.slams) expect(sl.sLand - sl.sMark).toBeCloseTo(0.5, 6);
    // Volleys from the loud stretch's start, blocking none of the others.
    expect(sp.starts[1]).toEqual({ kind: "volleys", s: at(20) });
    expect(sp.volleys.length).toBeGreaterThan(3);
  });

  it("closes a ring from the first accent known 3 s in, on a downbeat known by then", () => {
    const changes = [{ t: 4, knownAt: 30, strength: 1 }];
    const onsets = [{ t: 31, knownAt: 31.01, strength: 9, strong: true }, { t: 34, knownAt: 34.01, strength: 9, strong: true }];
    const stream = splitStream(synthStream({ until: 60, level: loud(20), onsets, structure: { changes, jumps: [], ends: [] } })).stream;
    const r = reactiveSetPieces(stream, d, 60, Infinity, fb).rings[0];
    expect(r.kClose).toBe(34.01);
    const known = stream.beats.filter((b) => b.knownAt <= 34.01).map(beats);
    expect(known).toContain(r.sClose);
    expect(r.sClose).toBeGreaterThanOrEqual(34.01 + 0.35 - 1e-9);
  });

  it("starts a stampede on a rising jump after a stretch that was not loud, a 5% blip of loud aside", () => {
    const jumps = [{ t: 30, knownAt: 31, db: 8 }];
    const blip = (t: number) => (t >= 25 && t < 25.3 ? "loud" : "quiet") as TickLevel;
    const mk = (level: (t: number) => TickLevel) => reactiveSetPieces(splitStream(synthStream({ until: 60, level, structure: { changes: [], jumps, ends: [] } })).stream, d, 60, Infinity, fb).stampedes;
    expect(mk(blip).length).toBe(1);
    expect(mk((t) => (t >= 24 && t < 26 ? "loud" : "quiet")).length).toBe(0);
  });

  it("drops the boss if the finale comes before it would drop in", () => {
    const changes = [{ t: 44, knownAt: 50, strength: 1 }];
    const st = (endKnown: number) => splitStream(synthStream({ until: 60, level: loud(20), structure: { changes, jumps: [], ends: [{ t: endKnown - 1.5, knownAt: endKnown }] } })).stream;
    expect(reactiveSetPieces(st(58), d, 60, Infinity, fb).boss).toBeDefined();
    expect(reactiveSetPieces(st(50.1), d, 60, Infinity, fb).boss).toBeUndefined();
  });

  it("decides each set piece from the stream known by its trigger (the perturbation)", () => {
    const run = reactiveInput(readStreamFile("causality-mix"), { level: "levels/reactive-check.yaml" });
    const dd = run.input.level.director!;
    const len = run.stream.audio.duration;
    const full = reactiveSetPieces(run.stream, dd, len, Infinity, fb);
    const upTo = (sp: typeof full, C: number) => ({
      starts: sp.starts.filter((x) => x.s < C),
      rings: sp.rings.filter((r) => r.s < C).map((r) => ({ ...r, kClose: r.kClose < C ? r.kClose : undefined, sClose: r.kClose < C ? r.sClose : undefined })),
      // (A volley is decided at its draw; its arrival is read at its release, its beat committed by then.)
      volleys: sp.volleys.filter((v) => v.sDraw < C).map((v) => ({ ...v, sArrive: v.sRelease < C ? v.sArrive : undefined })),
      formations: sp.formations.filter((f) => f.sStart < C).map((f) => ({ ...f, sRelease: f.sRelease < C ? f.sRelease : undefined })),
      boss: sp.boss && sp.boss.sEnter - 0.55 < C ? { ...sp.boss, slams: sp.boss.slams.filter((x) => x.sMark < C) } : undefined,
      finale: sp.finale && sp.finale.s < C ? sp.finale : undefined,
    });
    expect(full.starts.length).toBeGreaterThan(8);
    for (let C = 5; C < len; C += 1.7) {
      const cut = reactiveSetPieces(perturbStream(run.stream, C, "test"), dd, len, Infinity, fb);
      expect(upTo(cut, C), `at ${C.toFixed(1)}`).toEqual(upTo(full, C));
    }
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

  it("grades the groove as the meter shows it, from 16 s on", () => {
    const groove = { s: [0, 25, 35], v: [0.2, 0.8, 1] };
    expect(gradedAverageGroove(groove, 0, 40)).toBeCloseTo((9 * 0.2 + 10 * 0.8 + 5) / 24, 2);
    expect(gradedAverageGroove(groove, 26, 40)).toBeCloseTo((9 * 0.8 + 5) / 14, 2);
    expect(gradedAverageGroove(groove, 0, 15)).toBe(0);
  });

  it("shows the track's title card, and the end card once the finale's epilogue has played out", () => {
    const input = inputForStream(stream, { checks: { noHeroDamage: true } });
    const { plan } = planWithSim(input, { player: new KeyPlayer([]) });
    const ui = plan.events.ui.filter((u) => u.kind === "title" || u.kind === "end").map((u) => [u.kind, u.s]);
    // (No candidate ending: the finale comes at the audio's end, the epilogue 3 s after it.)
    expect(ui).toEqual([
      ["title", REACTIVE_TITLE.s],
      ["end", 40 + 3],
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
    // The provisional judgment shows on the press (its class), and changes nothing (judging starts a second in).
    const first = plan.inputs.find((i) => i.kind === "press" && i.s >= 1.5)!;
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

describe("beatless credit and the lock-on grace (hv-s4pt.13)", () => {
  // Clear but for 8 to 16 s; the player presses on every beat to 6 s, then stops.
  const run = (ev: (t: number) => number, grace?: Partial<typeof BEATLESS>, extra: number[] = []) => {
    const saved = { ...BEATLESS };
    Object.assign(BEATLESS, grace);
    try {
      const f = synthStream({ until: 40, hindsight: true, evidence: ev });
      const { stream } = splitStream(f);
      const times = [...f.beats.map((b) => b.t + 0.01).filter((t) => t < 6), ...extra];
      return { stream, plan: planWithSim(inputForStream(stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan };
    } finally {
      Object.assign(BEATLESS, saved);
    }
  };
  const gap = (t: number) => (t >= 8 && t < 16 ? 0 : 0.8);

  it("marks lock-ons after 4 s or more without judging, and when each stretch without judging began", () => {
    const { stream } = run(gap);
    // Judging starts at 1 s (not a lock-on: 1 s without), stops at 8 s, and locks on again at 17 s.
    expect(lockOnAt(stream, 5)).toBeNaN();
    expect(lockOnAt(stream, 17.5)).toBeCloseTo(17, 6);
    expect(lockOnAt(stream, 39)).toBeCloseTo(17, 6);
    expect(unjudgedSinceAt(stream, 12)).toBeCloseTo(8, 6);
    expect(unjudgedSinceAt(stream, 0.5)).toBe(0);
    expect(unjudgedSinceAt(stream, 18)).toBeNaN();
    // A flicker shorter than 4 s keeps the lock-on it interrupts.
    const flicker = run((t) => (gap(t) === 0 || (t >= 25 && t < 26) ? 0 : 0.8)).stream;
    expect(lockOnAt(flicker, 30)).toBeCloseTo(17, 6);
  });

  it("raises the groove a Perfect's worth for each beat 2 s or more into a stretch without judging", () => {
    const { plan } = run(gap);
    const low = grooveAt(plan.groove, 10.4);
    expect(low).toBeLessThan(0.9);
    // Beats from 10 s earn (each 2 s later): a Perfect's worth (0.08) each, to the full meter.
    expect(grooveAt(plan.groove, 12.4)).toBeCloseTo(Math.min(1, low + BEATLESS.perBeat), 6);
    expect(grooveAt(plan.groove, 18.6)).toBe(1);
    // A stretch shorter than 2 s earns nothing.
    const flicker = run((t) => (t >= 8 && t < 8.8 ? 0 : 0.8)).plan;
    for (let t = 8; t < 14; t += 0.1) expect(grooveAt(flicker.groove, t + 0.1)).toBeLessThanOrEqual(grooveAt(flicker.groove, t) + 1e-9);
  });

  it("waives the drain in the grace only for a steady player still pressing", () => {
    const { plan, stream } = run(gap);
    // Steady (to 6 s) but no longer pressing: locked on at 17 s, the drain waits its grace bar (1 s) and
    // comes 2 s after its beat.
    expect(grooveAt(plan.groove, 19.9)).toBe(1);
    expect(grooveAt(plan.groove, 21)).toBeLessThan(1);
    expect(checkSettleTiming(plan, stream).failures).toEqual([]);
  });

  it("forgives part of an unsteady player's penalties in the grace", () => {
    // Off the beat (a quarter beat late) from 17.5 to 21 s, in the grace.
    const off = Array.from({ length: 7 }, (_, k) => 17.5 + 0.125 + 0.5 * k);
    const after = (u: number) => grooveAt(run(gap, { unsteadyGrace: u }, off).plan.groove, 23.5);
    const [none, half, full] = [after(0), after(0.5), after(1)];
    // (Unsteady, the player drains as usual; only the presses' penalties shrink.)
    expect(half).toBeGreaterThan(full + 0.01);
    expect(none).toBeGreaterThan(half + 0.01);
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

  it("strides through an unclear stretch: there is no free movement", () => {
    expect(ev.evaluate(12.5).hud.free).toBe(false);
    const unclear = plan.inputs.filter((i) => i.kind === "press" && i.s >= 9 && i.s < 12);
    expect(unclear.length).toBeGreaterThan(3);
    for (const i of unclear) {
      expect(i.steps).toBe(true);
      expect(i.unjudged).toBe(true);
      expect(i.stride).toBeGreaterThan(0);
    }
  });

  it("judges again only after the beat has been clear for a second, whatever the wobble", () => {
    // Clear, then a wobble around the gate (0.25 / 0.15 every 0.3 s), then clear.
    const f = synthStream({ until: 20, evidence: (t) => (t >= 6 - 1e-9 && t < 10 ? (Math.floor((t - 6 + 1e-9) / 0.3) % 2 ? 0.25 : 0.15) : 0.8) });
    const j = judging(splitStream(f).stream);
    const at = (t: number) => j.judged[Math.round(t / 0.05) - 1] === 1;
    expect(at(5.9)).toBe(true);
    for (let t = 6.05; t < 10; t += 0.05) expect(at(t), `at ${t.toFixed(2)}`).toBe(false);
    // (The wobble's last 0.1 s is clear: judged a second after 9.9 s.)
    expect(at(10.8)).toBe(false);
    expect(at(10.95)).toBe(true);
  });

  it("chooses readings from judged presses only, restarting from as tracked after an unclear stretch", () => {
    const run = (ev: (t: number) => number) => {
      const f = synthStream({ until: 30, hindsight: true, evidence: ev });
      const times = f.beats.map((b) => b.t + 0.01).filter((t) => t < 28);
      return planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan;
    };
    const unclear = run((t) => (t >= 8 && t < 16 ? 0 : 0.8));
    const clear = run(() => 0.8);
    // Back from the stretch (judged from 17 s), it starts from offline "as tracked".
    const back = unclear.settles!.filter((x) => x.p >= 17 - 1e-9);
    expect(back[0].reading).toBe("offline as tracked");
    // Even from a reading the presses chose before the stretch: grids at half tempo, the player on
    // every beat, read "double" before it; after it, "as tracked" again until 12 presses choose.
    const half = (() => {
      const f = synthStream({ until: 40, hindsight: true, evidence: (t) => (t >= 12 && t < 18 ? 0 : 0.8) });
      const h = f.hindsight!.map((e) => ({ ...e, offline: e.offline.filter((_, i) => i % 2 === 0), online: e.online.filter((_, i) => i % 2 === 0) }));
      const times = f.beats.map((b) => b.t + 0.005).filter((t) => t > 1.5 && t < 38);
      return planWithSim(inputForStream(splitStream({ ...f, hindsight: h }).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan.settles!;
    })();
    expect(half.filter((x) => x.p > 8 && x.p < 12).every((x) => x.reading === "offline double")).toBe(true);
    const resumed = half.filter((x) => x.p >= 19 - 1e-9);
    expect(resumed[0].reading).toBe("offline as tracked");
    expect(resumed.some((x) => x.reading === "offline double")).toBe(true);
    // Once the window holds only presses after it, the readings and labels match the clear run.
    const after = (p: typeof clear) => p.settles!.filter((x) => x.p > 17 + 8).map((x) => `${x.reading} ${x.settled}`);
    expect(after(unclear)).toEqual(after(clear));
  });

  it("never lets a settle lower a steady player's provisional judgment, and scales penalties by the confidence", () => {
    // Hindsight grids a quarter of a beat late: settled against them, on-beat presses would rate worse.
    const late = (o: { evidence: number }) => {
      const f = synthStream({ until: 24, hindsight: true, evidence: () => o.evidence });
      const h = f.hindsight!.map((e) => ({ ...e, offline: e.offline.map((b) => b + 0.12), online: e.online.map((b) => b + 0.12) }));
      const times = f.beats.map((b) => b.t + 0.005).filter((t) => t < 22);
      return planWithSim(inputForStream(splitStream({ ...f, hindsight: h }).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan;
    };
    const p = late({ evidence: 0.8 });
    const judgedSettles = p.settles!.filter((x) => x.provisional !== "unjudged");
    expect(judgedSettles.some((x) => x.kept !== undefined)).toBe(true);
    for (const x of judgedSettles) if (x.kept) expect(x.settled).toBe(x.provisional);
    // (Once the player is steady: four judged presses in a row provisionally Perfect or Great.)
    expect(judgedSettles.slice(3).every((x) => x.change >= 0)).toBe(true);
    // Presses a third of a beat off: penalties, scaled to none at the gate and in full from 0.55.
    const off = (e: number) => {
      const f = synthStream({ until: 24, hindsight: true, evidence: () => e });
      const times = f.beats.map((b) => b.t + 0.16).filter((t) => t < 22);
      return planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) }).plan.settles!.filter((x) => x.change < 0);
    };
    const sure = off(0.8);
    const unsure = off(0.3);
    expect(sure.length).toBeGreaterThan(5);
    const mean = (l: { change: number }[]) => l.reduce((a, x) => a + x.change, 0) / l.length;
    expect(mean(unsure) / mean(sure)).toBeCloseTo((0.3 - 0.2) / (0.55 - 0.2), 1);
  });

  it("keeps the provisional judgment only for a steady run of four", () => {
    // Every other press a third of a beat off: never four good ones in a row, so nothing is kept.
    const f = synthStream({ until: 24, hindsight: true, evidence: () => 0.8 });
    const h = f.hindsight!.map((e) => ({ ...e, offline: e.offline.map((b) => b + 0.12), online: e.online.map((b) => b + 0.12) }));
    const times = f.beats.map((b, i) => b.t + (i % 2 ? 0.16 : 0.005)).filter((t) => t > 1.5 && t < 22);
    const { plan: p } = planWithSim(inputForStream(splitStream({ ...f, hindsight: h }).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) });
    expect(p.settles!.filter((x) => x.kept !== undefined)).toEqual([]);
    expect(p.settles!.some((x) => x.change < 0)).toBe(true);
  });

  it("scales the drain by the confidence, and an unsteady player's raises too", () => {
    // A bar of presses, then none: the drain at evidence 0.3 takes (0.3 - 0.2) / (0.55 - 0.2) of each step.
    const drained = (e: number) => {
      const f = synthStream({ until: 24, evidence: () => e });
      const times = f.beats.map((b) => b.t + 0.005).filter((t) => t > 1.5 && t < 8);
      const { plan: p } = planWithSim(inputForStream(splitStream(f).stream, { checks: { noHeroDamage: true } }), { player: new KeyPlayer(times) });
      return grooveAt(p.groove, 12) - grooveAt(p.groove, 16);
    };
    expect(drained(0.8)).toBeGreaterThan(0);
    expect(drained(0.3) / drained(0.8)).toBeCloseTo((0.3 - 0.2) / (0.55 - 0.2), 1);
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
      // (From the finale, at the audio's end, its sounds are exempt.)
      const percussive = full[k].key !== undefined && !GATE_EXEMPT_SOUNDS.has(full[k].key!) && full[k].s < 20 - 1e-9;
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
