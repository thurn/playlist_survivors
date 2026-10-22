/**
 * Reading play mode's timing log (src/live/log.ts): one row per gameplay key
 * press from the DOM event to what the simulation did, and a summary of
 * timing offsets, outcomes, the audio clock, frames and sounds.
 */
import type { ApplyLog, ClockProbe, DropLog, KeyLog, SoundLog } from "../../src/live/log.ts";
import type { PressTrace } from "../../src/sim/recorder.ts";

export interface PlayLogFile {
  id: string;
  savedAt: string;
  userAgent?: string;
  level?: string;
  params?: { avOffsetMs: number; startS: number; mode?: string };
  inputOffsetMs?: number;
  audio?: { sampleRate: number; baseLatency: number; outputLatency: number; state: string; live?: Record<string, number> };
  hero?: { timing: { perfectMs: number; greatMs: number; goodMs: number }; step: { durationMs: number; diagonalWindowMs: number }; swings: { minLeadMs: number; windupMs: number } };
  sections?: { label: string; s0: number; s1: number; movement: string }[];
  beats?: number[];
  grid?: { s: number[]; accent: number[] };
  keys: KeyLog[];
  applies: ApplyLog[];
  drops: DropLog[];
  traces: (PressTrace & { runs: number })[];
  events: { perf: number; s?: number; kind: string; detail?: unknown }[];
  sounds: SoundLog[];
  frames: { phases: string[]; ts: number[]; perf: number[]; s: number[]; raw: number[]; phase: number[]; dur: number[]; keys: number[]; ms: number[]; simS: number[] };
}

/** One gameplay key press, followed through. */
export interface PressRow {
  id: number;
  key: string;
  /** The DOM event: performance time, and how long until the handler ran (ms). */
  timeStamp: number;
  handlerLagMs: number;
  /** Song time (after calibration), nearest beat index and offset from it (ms). */
  s?: number;
  beat?: number;
  beatDeltaMs?: number;
  /**
   * What happened: the keyboard's fate when it never reached the game (repeat, disabled, held),
   * dropped (a count-in or restart threw it away), unapplied (queued but never applied),
   * or the simulation's outcome (step, extra, bend, walk).
   */
  outcome: string;
  section?: string;
  gridDeltaMs?: number;
  rating?: string;
  strikeS?: number;
  heavy?: boolean;
  noSwing?: string;
  /** Extra presses: ms since the press that took the slot. */
  sinceStepMs?: number;
  /** Applied: how long after the press (ms, by song time) the frame applied it, and whether it rolled back. */
  applyLagMs?: number;
  late?: boolean;
  rollback?: boolean;
  /** How many times the simulation ran this press (rollbacks re-run it). */
  runs?: number;
  clock?: ClockProbe;
}

/** Index of the value nearest v in a sorted list. */
function nearest(list: readonly number[], v: number): number {
  let lo = 0;
  let hi = list.length - 1;
  if (hi < 0) return -1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (list[mid] <= v) lo = mid;
    else hi = mid;
  }
  return Math.abs(list[hi] - v) < Math.abs(list[lo] - v) ? hi : lo;
}

export function pressRows(log: PlayLogFile): PressRow[] {
  const applies = new Map(log.applies.map((a) => [a.id, a]));
  const drops = new Map(log.drops.filter((d) => d.id !== undefined).map((d) => [d.id!, d]));
  const traces = new Map(log.traces.map((t) => [t.id!, t]));
  const beats = log.beats ?? [];
  const rows: PressRow[] = [];
  for (const k of log.keys) {
    if (k.type !== "down") continue;
    const row: PressRow = { id: k.id, key: k.key, timeStamp: k.timeStamp, handlerLagMs: k.handledAt - k.timeStamp, s: k.s, outcome: k.fate, clock: k.clock };
    if (k.s !== undefined && beats.length > 0) {
      const b = nearest(beats, k.s);
      row.beat = b;
      row.beatDeltaMs = (k.s - beats[b]) * 1000;
    }
    if (k.fate === "queued") {
      const a = applies.get(k.id);
      const t = traces.get(k.id);
      const d = drops.get(k.id);
      if (d) row.outcome = `dropped (${d.phase})`;
      else if (!a) row.outcome = "unapplied";
      else {
        row.applyLagMs = (a.frameS - (k.s ?? a.appliedS)) * 1000;
        row.late = a.late;
        row.rollback = a.rollbackTo !== undefined;
      }
      if (t) {
        row.outcome = t.outcome;
        row.section = t.section;
        row.runs = t.runs;
        if (t.delta !== undefined) row.gridDeltaMs = t.delta * 1000;
        row.rating = t.rating;
        row.strikeS = t.strikeS;
        row.heavy = t.heavy;
        row.noSwing = t.noSwing;
        if (t.outcome === "extra" && t.stepS !== undefined) row.sinceStepMs = (t.s - t.stepS) * 1000;
      } else if (a) row.outcome = "untraced";
    }
    rows.push(row);
  }
  return rows;
}

const median = (v: number[]) => {
  if (v.length === 0) return NaN;
  const s = [...v].sort((a, b) => a - b);
  return s.length % 2 ? s[s.length >> 1] : (s[s.length / 2 - 1] + s[s.length / 2]) / 2;
};
const mean = (v: number[]) => v.reduce((a, b) => a + b, 0) / v.length;
const sd = (v: number[]) => {
  const m = mean(v);
  return Math.sqrt(mean(v.map((x) => (x - m) ** 2)));
};
const f = (v: number | undefined, d = 0) => (v === undefined || Number.isNaN(v) ? "" : v.toFixed(d));
const pad = (v: string, n: number) => (v.length >= n ? v : v + " ".repeat(n - v.length));
const lpad = (v: string, n: number) => (v.length >= n ? v : " ".repeat(n - v.length) + v);

export interface SummaryOptions {
  from?: number;
  to?: number;
  /** Print the per-press table (default true). */
  table?: boolean;
}

/** A plain-text report. */
export function summarize(log: PlayLogFile, opts: SummaryOptions = {}): string {
  const out: string[] = [];
  const say = (s = "") => out.push(s);
  const inRange = (s?: number) => s === undefined || ((opts.from === undefined || s >= opts.from) && (opts.to === undefined || s <= opts.to));
  const rows = pressRows(log).filter((r) => inRange(r.s));

  say(`Play log ${log.id} (saved ${log.savedAt})`);
  if (log.userAgent) say(`  browser: ${log.userAgent}`);
  say(`  level ${log.level ?? "?"}, start ${log.params?.startS ?? 0}s, ?avOffsetMs=${log.params?.avOffsetMs ?? 0}, calibration offset ${log.inputOffsetMs ?? 0} ms`);
  if (log.audio) say(`  audio: ${log.audio.sampleRate} Hz, baseLatency ${f(log.audio.baseLatency * 1000, 1)} ms, outputLatency ${f(log.audio.outputLatency * 1000, 1)} ms`);
  if (log.hero) say(`  windows: perfect ±${log.hero.timing.perfectMs} great ±${log.hero.timing.greatMs} good ±${log.hero.timing.goodMs} ms; strike ≥${log.hero.swings.minLeadMs} ms after the press`);
  say();

  // Outcomes.
  const counts = new Map<string, number>();
  for (const r of rows) counts.set(r.outcome, (counts.get(r.outcome) ?? 0) + 1);
  say(`Presses: ${rows.length}`);
  for (const [k, n] of [...counts].sort((a, b) => b[1] - a[1])) say(`  ${pad(k, 22)} ${lpad(String(n), 5)}`);
  const ratings = new Map<string, number>();
  for (const r of rows) if (r.outcome === "step") ratings.set(r.rating ?? "(none)", (ratings.get(r.rating ?? "(none)") ?? 0) + 1);
  if (ratings.size > 0) say(`  step ratings: ${[...ratings].map(([k, n]) => `${k} ${n}`).join(", ")}`);
  const noSwing = rows.filter((r) => r.outcome === "step" && r.noSwing);
  if (noSwing.length > 0) say(`  steps that armed no swing: ${noSwing.length} (${[...new Set(noSwing.map((r) => r.noSwing))].join(", ")})`);
  say();

  // Timing offsets.
  const grid = rows.filter((r) => r.gridDeltaMs !== undefined).map((r) => r.gridDeltaMs!);
  const beat = rows.filter((r) => r.outcome === "step" && r.beatDeltaMs !== undefined).map((r) => r.beatDeltaMs!);
  if (grid.length > 0) {
    say(`Offset from the nearest grid time (rated steps and extra presses, + is late): median ${f(median(grid), 1)} ms, mean ${f(mean(grid), 1)}, sd ${f(sd(grid), 1)}`);
    const bins = [-250, -150, -100, -60, -30, 0, 30, 60, 100, 150, 250];
    const hist = new Array(bins.length + 1).fill(0);
    for (const v of grid) hist[bins.findIndex((b) => v < b) === -1 ? bins.length : bins.findIndex((b) => v < b)]++;
    const labels = ["<-250", ...bins.slice(1).map((b, i) => `${bins[i]}..${b}`), ">250"];
    say(`  ${labels.map((l, i) => `${l}: ${hist[i]}`).filter((_, i) => hist[i] > 0).join("  ")}`);
  }
  if (beat.length > 0) say(`Offset of steps from the nearest beat: median ${f(median(beat), 1)} ms, mean ${f(mean(beat), 1)}, sd ${f(sd(beat), 1)}`);
  const extra = rows.filter((r) => r.outcome === "extra" && r.sinceStepMs !== undefined).map((r) => r.sinceStepMs!);
  if (extra.length > 0) say(`Extra presses came ${f(median(extra))} ms (median) after the press that took their grid time`);
  say();

  // Input path.
  const lags = rows.map((r) => r.handlerLagMs);
  const applyLags = rows.filter((r) => r.applyLagMs !== undefined).map((r) => r.applyLagMs!);
  say(`Input path: handler ran ${f(median(lags), 1)} ms after the event (median; max ${f(Math.max(...lags, 0), 1)}); applied ${f(median(applyLags), 1)} ms of song time later (max ${f(Math.max(...applyLags, 0), 1)})`);
  say(`  rollbacks ${rows.filter((r) => r.rollback).length}, late keys (applied at the settled time) ${rows.filter((r) => r.late).length}`);
  const probes = rows.map((r) => r.clock).filter((c): c is ClockProbe => !!c);
  if (probes.length > 0) {
    const ol = probes.map((c) => c.outputLatency * 1000);
    const tsAge = probes.filter((c) => c.performanceTime !== undefined && c.performanceTime > 0).map((c) => c.perfNow - c.performanceTime!);
    const heard = probes.filter((c) => c.contextTime !== undefined && c.performanceTime! > 0).map((c) => (c.currentTime - c.contextTime! - (c.perfNow - c.performanceTime!) / 1000) * 1000);
    say(`Audio clock at presses: outputLatency ${f(Math.min(...ol), 1)}–${f(Math.max(...ol), 1)} ms; output timestamp age ${f(median(tsAge), 1)} ms (median)`);
    if (heard.length > 0) say(`  output delay by getOutputTimestamp (context time now minus the time being heard): median ${f(median(heard), 1)} ms`);
    if (probes.some((c) => c.performanceTime === undefined || c.performanceTime <= 0)) say(`  ${probes.filter((c) => !(c.performanceTime! > 0)).length} presses fell back to currentTime - outputLatency (no output timestamp)`);
  }
  say();

  // Frames.
  const fr = log.frames;
  const playing = fr.phases.indexOf("playing");
  const dts: number[] = [];
  const ds: number[] = [];
  let backwards = 0;
  for (let i = 1; i < fr.ts.length; i++) {
    if (fr.phase[i] !== playing || fr.phase[i - 1] !== playing || !inRange(fr.s[i])) continue;
    dts.push(fr.ts[i] - fr.ts[i - 1]);
    ds.push((fr.s[i] - fr.s[i - 1]) * 1000);
    if (fr.s[i] < fr.s[i - 1]) backwards++;
  }
  if (dts.length > 0) {
    const period = median(dts);
    say(`Frames (playing): ${dts.length + 1}, period ${f(period, 2)} ms; ${dts.filter((d) => d > period * 1.5).length} long frames (max ${f(Math.max(...dts), 1)} ms); song time step median ${f(median(ds), 2)} ms, max ${f(Math.max(...ds), 1)}, backwards ${backwards}`);
    const raws = fr.s.map((s, i) => (s - fr.raw[i]) * 1000).filter((_, i) => fr.phase[i] === playing && inRange(fr.s[i]));
    say(`  smoothed minus raw clock: median ${f(median(raws), 2)} ms, range ${f(Math.min(...raws), 1)}..${f(Math.max(...raws), 1)}`);
    const work = fr.ms.filter((_, i) => fr.phase[i] === playing);
    say(`  frame work: median ${f(median(work), 2)} ms, max ${f(Math.max(...work), 1)}`);
  }
  // Sounds.
  const sk = new Map<string, number>();
  for (const x of log.sounds) if (inRange(x.s)) sk.set(x.kind, (sk.get(x.kind) ?? 0) + 1);
  if (sk.size > 0) say(`Sounds: ${[...sk].map(([k, n]) => `${k} ${n}`).join(", ")}`);
  const events = log.events.filter((e) => e.kind !== "flush");
  if (events.length > 0) say(`Events: ${events.map((e) => `${e.kind}${e.s !== undefined ? `@${f(e.s, 2)}` : ""}`).join(" ")}`);

  if (opts.table !== false) {
    say();
    say("   id      s   key    beat   Δbeat  Δgrid  outcome        rating   strike  notes");
    for (const r of rows) {
      const notes: string[] = [];
      if (r.noSwing) notes.push(`no swing: ${r.noSwing}`);
      if (r.heavy) notes.push("CLEAVE");
      if (r.sinceStepMs !== undefined) notes.push(`${f(r.sinceStepMs)} ms after the slot's step`);
      if (r.rollback) notes.push("rollback");
      if (r.late) notes.push("LATE (applied at settled time)");
      if (r.runs !== undefined && r.runs > 1) notes.push(`ran ${r.runs}×`);
      if (r.handlerLagMs > 20) notes.push(`handler +${f(r.handlerLagMs)} ms`);
      if (r.section) notes.push(r.section);
      say(
        `${lpad(String(r.id), 5)} ${lpad(f(r.s, 3), 8)}  ${pad(r.key, 5)} ${lpad(r.beat !== undefined ? String(r.beat) : "", 6)} ${lpad(f(r.beatDeltaMs), 6)} ${lpad(f(r.gridDeltaMs), 6)}  ${pad(r.outcome, 14)} ${pad(r.rating ?? "", 8)} ${lpad(f(r.strikeS, 3), 7)}  ${notes.join("; ")}`,
      );
    }
  }
  return out.join("\n");
}
