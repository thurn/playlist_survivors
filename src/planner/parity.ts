/**
 * The parity report (docs/design/reactive-audio.md, "The Parity Report"): how a reactive run of a
 * track compares with the authored run, row by row, measured against the track's reference (the
 * Galop's song map). A measuring tool, not a gate: each stage's bead records its rows.
 */
import type { ContentLibrary } from "../content/loader.ts";
import { grooveAt } from "../plan/evaluate.ts";
import { gradeFor } from "../plan/grade.ts";
import type { Plan } from "../plan/types.ts";
import { detectedSections, SECTION_RULE, type SectionRule } from "../reactive/sections.ts";
import type { ReactiveStream, Reference } from "../reactive/stream.ts";
import { Warp } from "../core/warp.ts";
import { classifyPress, freshPressMemo, keyGroup } from "../sim/timing.ts";
import { soundRows } from "./checks.ts";

export interface ParityRow {
  row: string;
  authored: string;
  reactive: string;
  gap?: string;
  stage: string;
}

const pct = (x: number) => `${(100 * x).toFixed(1)}%`;

/** The reference's pulsed song-time ranges (the whole track without sections). */
function pulsed(ref: Reference, duration: number): [number, number][] {
  const secs = ref.sections ?? [{ label: "all", s0: 0, s1: duration }];
  return secs.filter((s) => s.feel !== "free").map((s) => [s.s0, s.s1]);
}

const inRanges = (t: number, ranges: [number, number][]) => ranges.some(([a, b]) => t >= a && t < b);

function nearestDist(times: readonly number[], t: number): number {
  let lo = 0;
  let hi = times.length - 1;
  while (hi - lo > 1) {
    const m = (lo + hi) >> 1;
    if (times[m] <= t) lo = m;
    else hi = m;
  }
  return Math.min(Math.abs(times[lo] - t), Math.abs(times[hi] - t));
}

/** Song time of fractional reference beat index i (linear between beats). */
function timeAt(beats: readonly number[], i: number): number {
  const k = Math.max(0, Math.min(beats.length - 2, Math.floor(i)));
  return beats[k] + (i - k) * (beats[k + 1] - beats[k]);
}

function indexAt(beats: readonly number[], t: number): number {
  let lo = 0;
  let hi = beats.length - 1;
  while (hi - lo > 1) {
    const m = (lo + hi) >> 1;
    if (beats[m] <= t) lo = m;
    else hi = m;
  }
  return lo + (t - beats[lo]) / (beats[hi] - beats[lo]);
}

/** Share of a plan's strikes in the pulsed sections within 20 ms of a reference beat. */
export function strikesOnBeat(plan: Plan, ref: Reference): number {
  const ranges = pulsed(ref, plan.duration);
  const sw = plan.swings.filter((x) => inRanges(x.s, ranges));
  return sw.length ? sw.filter((x) => nearestDist(ref.beats, x.s) <= 0.02).length / sw.length : 0;
}

/** Share of a plan's gameplay sounds (the table's rows) in the pulsed sections within 20 ms of the reference slot of their subdivision. */
export function soundsOnSlot(plan: Plan, ref: Reference, lib: ContentLibrary): number {
  const rows = soundRows(lib);
  const ranges = pulsed(ref, plan.duration);
  const beats = ref.beats;
  let n = 0;
  let ok = 0;
  for (const snd of plan.events.sounds) {
    const row = snd.key !== undefined ? rows.get(snd.key) : undefined;
    if (!row || !inRanges(snd.s, ranges)) continue;
    n++;
    const per = row === "kill" || row === "sixteenth" ? 4 : row === "eighth" ? 2 : 1;
    const slot = timeAt(beats, Math.round(indexAt(beats, snd.s) * per) / per);
    if (Math.abs(slot - snd.s) <= 0.02) ok++;
  }
  return n ? ok / n : 0;
}

/**
 * Share of a reactive run's presses in the pulsed sections rated as the authored grid would rate
 * them: its own class and rating (settled, with hindsight grids; unjudged, off-beat, Perfect ...) against the counted rating of
 * the same press time at the nearest reference beat.
 */
export function pressesRatedSame(plan: Plan, ref: Reference, lib: ContentLibrary): { share: number; n: number } {
  const ranges = pulsed(ref, plan.duration);
  const presses = plan.inputs.filter((i) => i.kind === "press" && i.steps && i.class !== "partner" && inRanges(i.s, ranges));
  // The authored grid's judgement of the same presses, in order (its memory and drift its own).
  const memo = freshPressMemo();
  // (With hindsight grids, a press's own rating is its settled one.)
  const settled = new Map((plan.settles ?? []).map((x) => [Math.round(x.p * 1e6), x.settled]));
  let same = 0;
  for (const p of presses) {
    const own = settled.get(Math.round(p.s * 1e6)) ?? (p.unjudged ? "unjudged" : p.class === "counted" ? (p.rating ?? "loose") : p.class === "offbeat" ? `offbeat-${p.offbeat}` : (p.class ?? "none"));
    const j = classifyPress(lib.hero, ref.beats, p.s, false, memo, keyGroup(p.key));
    const authored = j.class === "counted" ? (j.rating ?? "loose") : j.class === "offbeat" ? `offbeat-${j.rating}` : j.class;
    if (own === authored) same++;
  }
  return { share: presses.length ? same / presses.length : 0, n: presses.length };
}

/** Share of the pulsed time (up to a fall) at a tier of `min` or more. */
export function timeAtTier(plan: Plan, ref: Reference, min: number): number {
  const ranges = pulsed(ref, plan.duration);
  const end = plan.stats.deathS ?? plan.duration;
  let total = 0;
  let at = 0;
  for (let t = 0; t < end; t += 0.05) {
    if (!inRanges(t, ranges)) continue;
    total++;
    if (grooveAt(plan.tier, t) >= min) at++;
  }
  return total ? at / total : 0;
}

/** Kills, and the average number of enemies alive, per reference section. */
export function perSection(plan: Plan, ref: Reference): { label: string; kills: number; alive: number }[] {
  const secs = ref.sections ?? [{ label: "all", s0: 0, s1: plan.duration }];
  const kills = Array.from(plan.hud.kills);
  const warp = new Warp(plan.warp);
  return secs.map((s) => {
    let sum = 0;
    let n = 0;
    for (let t = s.s0; t < s.s1; t += 0.5) {
      // (Enemies live in world time, which hit-stops and dips slow.)
      const w = warp.toWorld(t);
      sum += plan.enemies.filter((e) => e.spawnW <= w && w < e.deathW).length;
      n++;
    }
    return { label: s.label, kills: kills.filter((k) => k >= s.s0 && k < s.s1).length, alive: n ? sum / n : 0 };
  });
}

/** How long after a reference section's start a detected start or a zone wave still counts as on it. */
export const SECTION_TOLERANCE_S = 8;

/** Is t within SECTION_TOLERANCE_S after one of the starts? */
const onStart = (t: number, starts: readonly number[]) => starts.some((s0) => t >= s0 - 1e-6 && t <= s0 + SECTION_TOLERANCE_S + 1e-6);

/**
 * Stage 4: the stream's detected section starts against the reference's section starts (those after
 * the first): how many starts have their own detected start within 8 s after them (matched one to
 * one, each start taking the earliest unmatched detected start in its window), how many detected
 * starts fall in free sections, and how many in pulsed sections are more than 8 s from every start
 * (before or after it: the stage 4 target's "false" starts).
 */
export function sectionDetection(stream: ReactiveStream, ref: Reference, rule: SectionRule = SECTION_RULE): { found: number; of: number; free: number; falseStarts: number; times: number[] } {
  const secs = ref.sections ?? [];
  const starts = secs.slice(1).map((x) => x.s0);
  const det = detectedSections(stream, rule).map((d) => d.s);
  const free = secs.filter((x) => x.feel === "free").map((x): [number, number] => [x.s0, x.s1]);
  const used = new Set<number>();
  let found = 0;
  for (const s0 of starts) {
    const k = det.findIndex((t, i) => !used.has(i) && onStart(t, [s0]));
    if (k < 0) continue;
    used.add(k);
    found++;
  }
  const near = (t: number) => starts.some((s0) => Math.abs(t - s0) <= SECTION_TOLERANCE_S + 1e-6);
  return {
    found,
    of: starts.length,
    free: det.filter((t) => inRanges(t, free)).length,
    falseStarts: det.filter((t) => !inRanges(t, free) && !near(t)).length,
    times: det,
  };
}

/** Stage 5: heavy swings striking within 50 ms of a reference accent (with this label, when given), and all heavy swings. */
export function heavyOnAccents(plan: Plan, ref: Reference, label?: string): { on: number; all: number } {
  const accents = (ref.accents ?? []).filter((a) => label === undefined || a.label === label).map((a) => a.t);
  const heavy = plan.swings.filter((w) => w.kind === "heavy").map((w) => w.s);
  return { on: heavy.filter((t) => accents.some((a) => Math.abs(a - t) <= 0.05 + 1e-9)).length, all: heavy.length };
}

/** Zone waves: how many, how many within 8 s after a reference section start, and how many on none. */
export function zoneWaves(plan: Plan, ref: Reference): { n: number; on: number; off: number } {
  const starts = (ref.sections ?? []).slice(1).map((x) => x.s0);
  const waves = plan.open.waves.map((w) => w.s);
  const on = waves.filter((t) => onStart(t, starts)).length;
  return { n: waves.length, on, off: waves.length - on };
}

/** The report's rows (the stage 4 rows from the stream's structure, when given). */
export function parityRows(authored: Plan | undefined, reactive: Plan, ref: Reference, lib: ContentLibrary, stream?: ReactiveStream, rule?: SectionRule): ParityRow[] {
  const both = (f: (p: Plan) => number, fmt: (x: number) => string, stage: string, row: string): ParityRow => {
    const r = f(reactive);
    const a = authored ? f(authored) : undefined;
    return { row, authored: a === undefined ? "—" : fmt(a), reactive: fmt(r), ...(a !== undefined ? { gap: `${r - a >= 0 ? "+" : ""}${fmt(r - a)}` } : {}), stage };
  };
  const rows: ParityRow[] = [
    both((p) => strikesOnBeat(p, ref), pct, "1", "Strikes within 20 ms of a reference beat (pulsed sections)"),
    both((p) => soundsOnSlot(p, ref, lib), pct, "1", "Gameplay sounds within 20 ms of their reference slot (pulsed sections)"),
  ];
  const same = pressesRatedSame(reactive, ref, lib);
  rows.push({ row: `Presses rated the same as the authored grid would (${same.n} presses)`, authored: "—", reactive: pct(same.share), stage: "1, 2" });
  rows.push(both((p) => timeAtTier(p, ref, 2), pct, "1, 2", "Time at tier 2 or higher (pulsed sections)"));
  const grade = (p: Plan) => `${p.stats.averageGroove.toFixed(2)} (${gradeFor(p.stats.averageGroove)})`;
  rows.push({ row: "Average groove and grade", authored: authored ? grade(authored) : "—", reactive: grade(reactive), gap: authored ? (reactive.stats.averageGroove - authored.stats.averageGroove).toFixed(2) : undefined, stage: "1, 2" });
  const ka = authored ? perSection(authored, ref) : undefined;
  const kr = perSection(reactive, ref);
  rows.push({ row: "Kills", authored: authored ? String(authored.hud.kills.length) : "—", reactive: String(reactive.hud.kills.length), gap: authored ? String(reactive.hud.kills.length - authored.hud.kills.length) : undefined, stage: "1" });
  kr.forEach((r, i) => {
    const a = ka?.[i];
    rows.push({ row: `  ${r.label}: kills, enemies alive`, authored: a ? `${a.kills}, ${a.alive.toFixed(0)}` : "—", reactive: `${r.kills}, ${r.alive.toFixed(0)}`, stage: "1" });
  });
  if (ref.sections && ref.sections.length > 1) {
    const wa = authored ? zoneWaves(authored, ref) : undefined;
    const wr = zoneWaves(reactive, ref);
    rows.push({ row: "Zone waves (within 8 s after a reference section start)", authored: wa ? `${wa.n} (${wa.on})` : "—", reactive: `${wr.n} (${wr.on})`, stage: "4" });
    if (stream?.structure) {
      const d = sectionDetection(stream, ref, rule);
      rows.push({ row: "Detected section starts within 8 s after a reference section start", authored: "—", reactive: `${d.found} of ${d.of}`, stage: "4" });
      rows.push({ row: "Detected section starts in free sections", authored: "—", reactive: String(d.free), stage: "4" });
      rows.push({ row: "Detected section starts in pulsed sections more than 8 s from every start", authored: "—", reactive: String(d.falseStarts), stage: "4" });
    }
    rows.push({ row: "False zone waves (on no reference section start)", authored: wa ? String(wa.off) : "—", reactive: String(wr.off), stage: "4" });
  }
  if (ref.accents?.length) {
    const ha = authored ? heavyOnAccents(authored, ref) : undefined;
    const hr = heavyOnAccents(reactive, ref);
    rows.push({ row: "Heavy swings within 50 ms of a reference accent (of all heavy swings)", authored: ha ? `${ha.on} (${ha.all})` : "—", reactive: `${hr.on} (${hr.all})`, stage: "5" });
  }
  rows.push({ row: "Cleaves launched", authored: authored ? String(authored.cleaves.length) : "—", reactive: String(reactive.cleaves.length), stage: "5" });
  return rows;
}

/** The rows as a text table. */
export function formatParity(rows: ParityRow[]): string {
  const w = [Math.max(...rows.map((r) => r.row.length)), 14, 14, 10];
  const line = (c: string[]) => c.map((x, i) => (i === 0 ? x.padEnd(w[0]) : x.padStart(w[i]))).join("  ");
  return [line(["Row", "Authored", "Reactive", "Gap"]), ...rows.map((r) => line([r.row, r.authored, r.reactive, r.gap ?? ""]))].join("\n");
}
