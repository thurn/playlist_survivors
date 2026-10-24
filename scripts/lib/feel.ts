/**
 * A track's reactive feel measurements (docs/design/reactive-feel.md, "The Dossier" and "Metrics"):
 * the virtual players' runs (seed 1858, damage off unless stated), the evaluator's frame state
 * sampled over the song, and every metric, n/a where it needs a later bead (bars and downbeats: E2;
 * bands: E1; the identity family and the response budget: G1, G2).
 */
import type { ContentLibrary } from "../../src/content/loader.ts";
import type { SkillProfileName } from "../../src/content/schema.ts";
import { beatPulseOf, Evaluator, MAX_PARTICLES } from "../../src/plan/evaluate.ts";
import type { Plan } from "../../src/plan/types.ts";
import { soundRows } from "../../src/planner/checks.ts";
import { GATE_EXEMPT_SOUNDS } from "../../src/planner/emit.ts";
import { setPieceTimes } from "../../src/planner/parity.ts";
import { planWithSim, prepare } from "../../src/planner/plan.ts";
import { judgedTierShare } from "../../src/planner/reactive-checks.ts";
import { reactivePlayerFor } from "../../src/reactive/players.ts";
import { judgedAt, type ReactiveStream, type Reference } from "../../src/reactive/stream.ts";
import type { PropInfo } from "../../src/mapgen/mapgen.ts";
import {
  anyLevelF,
  beatDriven,
  beatF,
  beatMatches,
  changeTimes,
  committedLevel,
  levelGrids,
  lockIn,
  longestRunShare,
  nearest,
  onSlots,
  peaksOnTargets,
  reactsToBeat,
  spanMeans,
  spearman,
  strikeDamage,
  structureMatch,
} from "./feel-metrics.ts";
import { reactiveInput, readStreamFile, type ReactiveRun } from "./reactive-node.ts";

export const FEEL_SEED = 1858;
const NO_DAMAGE = { noHeroDamage: true } as const;
const MASHERS = ["mash2", "mash4", "mash8", "holder"] as const;

export interface TrackRuns {
  id: string;
  run: ReactiveRun;
  lib: ContentLibrary;
  /** The fitted (or shipped) reference; beat metrics skip a failed fit. */
  ref: Reference;
  /** Damage off: the honest player and the mashers. */
  plans: Record<"reactive-moderate" | (typeof MASHERS)[number], Plan>;
  /** Damage on: the honest players, for survival. */
  hurt: Record<"reactive-moderate" | "reactive-human", Plan>;
}

/** Plans a track's runs (about ten seconds a minute of music). */
export function trackRuns(id: string, props: Record<string, PropInfo> | undefined): TrackRuns {
  const file = readStreamFile(id);
  const run = reactiveInput(file, { seed: FEEL_SEED, checks: NO_DAMAGE });
  if (!run.reference) throw new Error(`${id} has no reference`);
  const ref = run.reference;
  const plan = (r: ReactiveRun, name: SkillProfileName) => planWithSim(r.input, { props, player: reactivePlayerFor(prepare(r.input).ctx, ref, name) }).plan;
  const hurtRun = reactiveInput(file, { seed: FEEL_SEED });
  return {
    id,
    run,
    lib: run.input.content,
    ref,
    plans: { "reactive-moderate": plan(run, "reactive-moderate"), mash2: plan(run, "mash2"), mash4: plan(run, "mash4"), mash8: plan(run, "mash8"), holder: plan(run, "holder") },
    hurt: { "reactive-moderate": plan(hurtRun, "reactive-moderate"), "reactive-human": plan(hurtRun, "reactive-human") },
  };
}

/** The frame state the metrics read, one array per field, sampled at `rate` Hz. */
export interface FrameSeries {
  rate: number;
  s: number[];
  /** Enemies: mean squash (log width over height) and mean height. */
  squash: number[];
  hop: number[];
  flash: number[];
  zoom: number[];
  shake: number[];
  particles: number[];
  /** Total light strength, ripple and ring alpha. */
  light: number[];
  rings: number[];
  /** The grading's mean multiplier, the beat pulse as the renderer shows it, the hero's height. */
  grade: number[];
  pulse: number[];
  heroZ: number[];
  /** The zone the camera sees (the one it blends towards), the tier and the groove. */
  zone: number[];
  tier: number[];
  groove: number[];
  /** Reactive mode's pulse state: how much it follows the committed beats rather than the presses. */
  mix: number[];
  /** Visual intensity: flash, squash, particles and light together. */
  intensity: number[];
}

export function sampleFrames(plan: Plan, lib: ContentLibrary, rate: number): FrameSeries {
  const ev = new Evaluator(plan, lib);
  const out: FrameSeries = {
    rate,
    s: [],
    squash: [],
    hop: [],
    flash: [],
    zoom: [],
    shake: [],
    particles: [],
    light: [],
    rings: [],
    grade: [],
    pulse: [],
    heroZ: [],
    zone: [],
    tier: [],
    groove: [],
    mix: [],
    intensity: [],
  };
  const end = plan.duration;
  for (let k = 0; k / rate < end; k++) {
    const s = k / rate;
    const f = ev.evaluate(s);
    let sq = 0;
    let z = 0;
    let n = 0;
    for (const e of f.enemies) {
      if (e.anim === "hurt") continue;
      sq += Math.log(e.scaleX / e.scaleY);
      z += e.z;
      n++;
    }
    const squash = n ? sq / n : 0;
    const light = f.lights.reduce((a, l) => a + l.strength, 0);
    const look = f.look ? (f.look.mul[0] + f.look.mul[1] + f.look.mul[2]) / 3 : 1;
    out.s.push(s);
    out.squash.push(squash);
    out.hop.push(n ? z / n : 0);
    out.flash.push(f.flash.alpha);
    out.zoom.push(f.camera.zoom);
    out.shake.push(Math.hypot(f.camera.shakeX, f.camera.shakeY));
    out.particles.push(f.particles.length);
    out.light.push(light);
    out.rings.push(f.rings.reduce((a, r) => a + r.alpha, 0));
    out.grade.push(look);
    out.pulse.push(beatPulseOf(f));
    out.heroZ.push(f.hero.z);
    out.zone.push(f.zone.b);
    out.tier.push(f.hud.tier.n);
    out.groove.push(f.hud.groove);
    out.mix.push(f.reactive?.mix ?? 1);
    out.intensity.push(f.flash.alpha + Math.abs(squash) + f.particles.length / 200 + light / 5);
  }
  return out;
}

/** The element classes whose reaction coverage counts (A1): a frame series each, and zone waves. */
export const ELEMENT_CLASSES = ["enemy squash", "enemy hop", "flash", "camera punch", "camera shake", "particles", "lights", "rings", "grading", "beat pulse", "hero", "zones"] as const;

const seriesOf: Record<Exclude<(typeof ELEMENT_CLASSES)[number], "zones">, keyof FrameSeries> = {
  "enemy squash": "squash",
  "enemy hop": "hop",
  flash: "flash",
  "camera punch": "zoom",
  "camera shake": "shake",
  particles: "particles",
  lights: "light",
  rings: "rings",
  grading: "grade",
  "beat pulse": "pulse",
  hero: "heroZ",
};

/** A metric: a number, or null (n/a) with the reason in `notes`. */
export type Value = number | null;

export interface TrackMetrics {
  id: string;
  duration: number;
  fit: string;
  beat: { strictF: Value; anyLevelF: Value; level: Value; longestRun: Value; lockInS: Value };
  bars: { meterCorrect: Value; downbeatsWithin45: Value };
  soundSync: Value;
  reward: { honestTier2: Value; mash4Tier0: Value; mash8Tier0: Value; damageRatio: Value; strikeSizeByTier: (number | null)[] };
  mashing: { beatsPlaying: string[]; groove: Record<string, number>; tier2: Record<string, number> };
  fairness: { judgedBeatDriven: Value; perfectOrGreat: Value };
  pacing: { setPieces: number; levelUps: number; zoneWaves: number; firstSetPiece: Value; firstLevelUp: Value; firstZoneWave: Value; falseFinales: number; survives: Record<string, boolean> };
  coverage: { n: Value; of: number; classes: Record<string, { reacts: boolean; corr: number; fold: number; changes?: number }> };
  visualSync: { beatTier: Value; barTier: Value };
  identity: { family: string | null; meanGrade: number; zones: number[] };
  structure: { followed: Value; stray: number };
  hierarchy: { spearman: Value; capShare: number };
  breakdowns: { unjudgedShare: number; percussiveSounds: number; pressFollowing: Value; intensityDrop: Value; grooveDrains: number };
  notes: string[];
}

const share = (xs: readonly boolean[]) => (xs.length ? xs.filter(Boolean).length / xs.length : null);

/** Every metric for a track, from its runs and its honest run's frames. */
export function trackMetrics(r: TrackRuns, frames: FrameSeries): TrackMetrics {
  const notes: string[] = [];
  const stream: ReactiveStream = r.run.stream;
  const honest = r.plans["reactive-moderate"];
  const duration = stream.audio.duration;
  const fitted = r.ref.fit !== "failed";
  const grid = fitted ? r.ref.beats : [];
  if (!fitted) notes.push("the fit failed: beat, sound-sync, fairness, coverage, visual-sync and hierarchy metrics are n/a (the raw decode is not ground truth)");
  const committed = stream.beats.map((b) => b.t);
  // Beat (M1): strict at the fitted level, and the best F at any level; the run and the lock-in at the
  // level the committed beats run at (so an off-beat lock at the grid's period counts as wrong).
  let beat: TrackMetrics["beat"] = { strictF: null, anyLevelF: null, level: null, longestRun: null, lockInS: null };
  if (fitted && grid.length >= 2) {
    const levels = levelGrids(grid);
    const best = committedLevel(committed, grid);
    const hits = beatMatches(committed, levels[best]);
    beat = {
      strictF: beatF(committed, grid),
      anyLevelF: anyLevelF(committed, grid),
      level: [1, 2, 0.5, 0.5, 2 / 3][best],
      longestRun: longestRunShare(committed, hits, duration),
      lockInS: lockIn(committed, hits) ?? null,
    };
  }
  notes.push("bars and downbeats need the stream's causal meter (E2): n/a");
  // Sound sync (M1): percussive gameplay sounds on a 16th of the reference grid.
  const rows = soundRows(r.lib);
  const percussive = honest.events.sounds.filter((x) => x.key !== undefined && rows.has(x.key)).map((x) => x.s);
  const soundSync = fitted ? onSlots(percussive, grid, 4, 0.02) : null;
  // Reward (M2) over judged time, damage off: the player's own strikes' damage, capped at what each enemy
  // had left, and the strike size by tier.
  const health = (type: string) => r.lib.enemies.get(type)?.health ?? Infinity;
  const dealt = (p: Plan) => strikeDamage(p.enemies, p.hits, health, (h) => judgedAt(stream, p.hits[h].s));
  const tierOfHit = new Map(honest.swings.filter((w) => w.tier !== undefined && judgedAt(stream, w.s)).map((w) => [w.hit, w.tier!] as const));
  const sizes: number[][] = [[], [], [], []];
  for (const [hit, tier] of tierOfHit) {
    const sh = honest.hits[hit]?.shape;
    if (!sh) continue;
    sizes[Math.min(3, tier)].push(sh.kind === "capsule" ? Math.hypot(sh.x1 - sh.x0, sh.y1 - sh.y0) : sh.kind === "ring" ? sh.outer : sh.radius);
  }
  const reward: TrackMetrics["reward"] = {
    honestTier2: judgedTierShare(honest, stream, (t) => t >= 2),
    mash4Tier0: judgedTierShare(r.plans.mash4, stream, (t) => t === 0),
    mash8Tier0: judgedTierShare(r.plans.mash8, stream, (t) => t === 0),
    damageRatio: dealt(r.plans.mash4) > 0 ? dealt(honest) / dealt(r.plans.mash4) : null,
    strikeSizeByTier: sizes.map((l) => (l.length ? l.reduce((a, x) => a + x, 0) / l.length : null)),
  };
  // Mashing never beats playing (hv-s4pt.13's gate, measured here).
  const groove: Record<string, number> = {};
  const tier2: Record<string, number> = {};
  for (const [name, p] of Object.entries(r.plans)) {
    groove[name] = p.stats.averageGroove;
    tier2[name] = judgedTierShare(p, stream, (t) => t >= 2);
  }
  const beatsPlaying = MASHERS.filter((m) => groove[m] > groove["reactive-moderate"] + 1e-9 || tier2[m] > tier2["reactive-moderate"] + 1e-9);
  // Fairness (M3): of the clearly beat-driven stretches (by the onsets' regularity on the fitted grid),
  // the share judged; and the honest presses settled Perfect or Great.
  const settled = (honest.settles ?? []).filter((x) => x.settled !== "unjudged" && x.settled !== "partner");
  const driven = fitted
    ? beatDriven(
        grid,
        stream.onsets.map((o) => o.t),
      )
    : [];
  const drivenJudged = grid.slice(0, -1).flatMap((t, i) => (driven[i] ? [judgedAt(stream, (t + grid[i + 1]) / 2)] : []));
  const fairness = { judgedBeatDriven: fitted ? share(drivenJudged) : null, perfectOrGreat: share(settled.map((x) => x.settled === "perfect" || x.settled === "great")) };
  // Pacing (M4).
  const pieces = setPieceTimes(honest);
  const { "the finale": finale, ...setPieces } = pieces;
  const pieceStarts = Object.values(setPieces).flatMap((ts) => ts.slice(0, 1));
  const first = (ts: number[]) => (ts.length ? Math.min(...ts) / duration : null);
  const pacing: TrackMetrics["pacing"] = {
    setPieces: pieceStarts.length,
    levelUps: honest.upgrades.length,
    zoneWaves: honest.open.waves.length,
    firstSetPiece: first(pieceStarts),
    firstLevelUp: first(honest.upgrades.map((u) => u.s)),
    firstZoneWave: first(honest.open.waves.map((w) => w.s)),
    falseFinales: finale.filter((t) => t < duration - 10).length,
    survives: Object.fromEntries(Object.entries(r.hurt).map(([n, p]) => [n, p.stats.deathS === undefined])),
  };
  // Coverage (A1): during clear stretches, does each class react to the reference beat? (n/a without a fit.)
  const beats = grid;
  const clear = frames.s.map((s) => judgedAt(stream, s));
  const classes: TrackMetrics["coverage"]["classes"] = {};
  for (const c of ELEMENT_CLASSES) {
    if (c === "zones") {
      const changes = changeTimes(frames.s, frames.zone).length;
      classes[c] = { reacts: changes > 0, corr: 0, fold: 0, changes };
      continue;
    }
    const x = fitted ? reactsToBeat(frames[seriesOf[c]] as number[], frames.s, beats, (i) => clear[i]) : { reacts: false, corr: 0, fold: 0 };
    classes[c] = { reacts: x.reacts, corr: round(x.corr), fold: round(x.fold) };
  }
  // Visual sync (A2): the beat-tier outputs' peaks on the reference beats, within a frame.
  const beatTier = ["squash", "pulse", "light"] as const;
  const on = beatTier.map((k) => peaksOnTargets(frames[k], frames.s, beats));
  const nPeaks = on.reduce((a, x) => a + x.n, 0);
  const visualSync = { beatTier: fitted && nPeaks ? on.reduce((a, x) => a + x.share * x.n, 0) / nPeaks : null, barTier: null };
  // Identity (A3): the family is G2's; the grading and the zones seen stand for it until then.
  const zones = frames.zone.filter((z, i) => i === 0 || z !== frames.zone[i - 1]);
  const identity = { family: null, meanGrade: round(frames.grade.reduce((a, x) => a + x, 0) / (frames.grade.length || 1), 4), zones };
  notes.push("identity families arrive with G2: the zones seen and the mean grading stand for the identity");
  // Structure (A4): a visible change (zone wave, set piece or grading change) after each reference section start.
  const starts = (r.ref.sections ?? []).slice(1).map((s) => s.s0);
  const changes = [...honest.open.waves.map((w) => w.s), ...pieceStarts, ...changeTimes(frames.s, frames.grade, 1e-4, 2)];
  const sm = structureMatch(starts, changes);
  // Hierarchy (A5): per bar, the music's loudness against the visual intensity.
  const bars = r.ref.downbeats?.length && r.ref.meterConfidence === "high" ? r.ref.downbeats : beats.filter((_, i) => i % 4 === 0);
  const energyT = stream.ticks.map((k) => k.t);
  const loud = spanMeans(
    energyT,
    stream.ticks.map((k) => k.energy),
    bars,
  );
  const vis = spanMeans(frames.s, frames.intensity, bars);
  const ok = loud.map((v, i) => !Number.isNaN(v) && !Number.isNaN(vis[i]));
  const hierarchy = {
    spearman:
      fitted && ok.filter(Boolean).length >= 4
        ? spearman(
            loud.filter((_, i) => ok[i]),
            vis.filter((_, i) => ok[i]),
          )
        : null,
    capShare: frames.particles.filter((n) => n >= MAX_PARTICLES).length / (frames.particles.length || 1),
  };
  notes.push("the response budget's cap is G1's: capShare counts frames at the particle cap");
  // Breakdowns (A6): in stretches without judging.
  const unjudged = clear.map((c) => !c);
  const gate = stream.params.gate;
  const sb = stream.beats;
  const owner = (t: number) => {
    let lo = -1;
    let hi = sb.length;
    while (hi - lo > 1) {
      const m = (lo + hi) >> 1;
      if (sb[m].t <= t + 1e-6) lo = m;
      else hi = m;
    }
    return lo;
  };
  // (As the sound gate counts them: the finale's sounds, from its first band on, are exempt.)
  const finaleS = Math.min(Infinity, ...honest.hits.filter((h) => h.source === "ring").map((h) => h.s));
  const percussiveBelow = honest.events.sounds.filter(
    (x) => x.key !== undefined && rows.has(x.key) && !GATE_EXEMPT_SOUNDS.has(x.key) && x.s < finaleS - 1e-9 && (owner(x.s) < 0 || sb[owner(x.s)].evidence < gate),
  ).length;
  const mean = (xs: number[], keep: boolean[]) => {
    const kept = xs.filter((_, i) => keep[i]);
    return kept.length ? kept.reduce((a, x) => a + x, 0) / kept.length : null;
  };
  const uMean = mean(frames.intensity, unjudged);
  const jMean = mean(frames.intensity, clear);
  let drains = 0;
  for (let i = 1; i < frames.s.length; i++) if (unjudged[i] && unjudged[i - 1] && frames.groove[i] < frames.groove[i - 1] - 0.005) drains++;
  const breakdowns = {
    unjudgedShare: unjudged.filter(Boolean).length / (unjudged.length || 1),
    percussiveSounds: percussiveBelow,
    pressFollowing: mean(
      frames.mix.map((m) => 1 - m),
      unjudged,
    ),
    intensityDrop: uMean !== null && jMean ? uMean / jMean : null,
    grooveDrains: drains,
  };
  return {
    id: r.id,
    duration,
    fit: r.ref.fit ?? r.ref.source,
    beat: roundAll(beat),
    bars: { meterCorrect: null, downbeatsWithin45: null },
    soundSync: soundSync === null ? null : round(soundSync),
    reward: { ...roundAll({ ...reward, strikeSizeByTier: undefined }), strikeSizeByTier: reward.strikeSizeByTier.map((x) => (x === null ? null : round(x, 1))) } as TrackMetrics["reward"],
    mashing: { beatsPlaying, groove: roundAll(groove), tier2: roundAll(tier2) },
    fairness: roundAll(fairness),
    pacing: { ...pacing, firstSetPiece: rn(pacing.firstSetPiece), firstLevelUp: rn(pacing.firstLevelUp), firstZoneWave: rn(pacing.firstZoneWave) },
    coverage: { n: fitted ? Object.values(classes).filter((c) => c.reacts).length : null, of: ELEMENT_CLASSES.length, classes },
    visualSync: { beatTier: rn(visualSync.beatTier), barTier: null },
    identity,
    structure: { followed: sm.of ? round(sm.followed / sm.of) : null, stray: sm.stray },
    hierarchy: { spearman: rn(hierarchy.spearman), capShare: round(hierarchy.capShare) },
    breakdowns: { ...breakdowns, unjudgedShare: round(breakdowns.unjudgedShare), pressFollowing: rn(breakdowns.pressFollowing), intensityDrop: rn(breakdowns.intensityDrop) },
    notes,
  };
}

const round = (x: number, d = 3) => Math.round(x * 10 ** d) / 10 ** d;
const rn = (x: number | null) => (x === null ? null : round(x));
function roundAll<T extends object>(o: T): T {
  return Object.fromEntries(Object.entries(o).map(([k, v]) => [k, typeof v === "number" ? round(v) : v])) as T;
}

/** The distance from t to the nearest reference beat (exported for the dossier's captions). */
export const fromBeat = (t: number, beats: readonly number[]) => nearest(beats, t);

const fmt = (v: unknown): string =>
  v === null || v === undefined
    ? "n/a"
    : typeof v === "number"
      ? String(v)
      : Array.isArray(v)
        ? `[${v.map(fmt).join(", ")}]`
        : typeof v === "object"
          ? Object.entries(v as object)
              .map(([k, x]) => `${k} ${fmt(x)}`)
              .join(", ")
          : String(v);

/** metrics.md: one line per metric. */
export function metricsMarkdown(m: TrackMetrics): string {
  const lines = [`# ${m.id}`, "", `Duration ${m.duration.toFixed(1)} s; reference fit: ${m.fit}. Seed ${FEEL_SEED}, damage off unless stated.`, ""];
  const section = (title: string, o: object) => {
    lines.push(`## ${title}`, "");
    for (const [k, v] of Object.entries(o)) lines.push(`- ${k}: ${fmt(v)}`);
    lines.push("");
  };
  section("Beat (M1)", m.beat);
  section("Bars (M1, A2)", m.bars);
  section("Sound sync (M1)", { percussiveOn16ths: m.soundSync });
  section("Reward (M2), judged time", m.reward);
  section("Mashing never beats playing", m.mashing);
  section("Fairness (M3)", m.fairness);
  section("Pacing (M4)", m.pacing);
  lines.push(`## Coverage (A1): ${m.coverage.n ?? "n/a"} of ${m.coverage.of}`, "");
  for (const [k, c] of Object.entries(m.coverage.classes)) lines.push(`- ${k}: ${c.reacts ? "reacts" : "no"} (${c.changes !== undefined ? `${c.changes} changes` : `corr ${c.corr}, fold ${c.fold}`})`);
  lines.push("");
  section("Visual sync (A2)", m.visualSync);
  section("Identity (A3)", m.identity);
  section("Structure (A4)", m.structure);
  section("Hierarchy (A5)", m.hierarchy);
  section("Breakdowns (A6)", m.breakdowns);
  lines.push("## Notes", "", ...m.notes.map((n) => `- ${n}`), "");
  return lines.join("\n");
}
