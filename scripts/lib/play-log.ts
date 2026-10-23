/**
 * Reading play mode's timing log (src/live/log.ts): one row per gameplay key
 * press from the DOM event to what the simulation did, and a summary of
 * timing offsets, outcomes, press classes and time at each tier per section,
 * the hero's speed over each second, the audio clock, frames and sounds.
 * Re-rating (`rerate`) classes a log's presses again under the current groove
 * rules, with grid times from the song map, so logs recorded under older
 * rules serve as references while the groove is tuned.
 */
import type { HeroDef } from "../../src/content/schema.ts";
import type { ApplyLog, ClockProbe, DropLog, GemLog, HealthLog, HeroPath, KeyLog, SoundLog, SoundMove, UpgradeLog } from "../../src/live/log.ts";
import type { PressTrace } from "../../src/sim/recorder.ts";
import { classifyPress, drainPerBeat, freshPressMemo, grooveAfterChange, tierFor } from "../../src/sim/timing.ts";

export interface PlayLogFile {
  id: string;
  savedAt: string;
  userAgent?: string;
  level?: string;
  params?: { avOffsetMs: number; startS: number; mode?: string };
  inputOffsetMs?: number;
  audio?: { sampleRate: number; baseLatency: number; outputLatency: number; state: string; live?: Record<string, number> };
  hero?: {
    timing: { perfectMs: number; greatMs: number; goodMs: number };
    step: { durationMs: number; diagonalWindowMs: number };
    /** (Older logs also give the armed swing's minLeadMs.) */
    swings: { windupMs: number };
    /** Logs from play mode with strides. */
    stride?: { durationMs: number; maxPeriodS: number; freePeriodS: number };
    walk?: { speed: number };
    /** Logs from play mode with press classes: the groove meter's values it was played under. */
    meter?: HeroDef["meter"];
  };
  /** The level's play-mode rule switches. */
  rules?: Record<string, boolean>;
  /** When the player took over from the virtual player (song time). */
  playerFrom?: number;
  sections?: { label: string; s0: number; s1: number; movement: string }[];
  beats?: number[];
  grid?: { s: number[]; accent: number[] };
  /** The recording's settled presses (the virtual player's before the hand-over too). */
  inputs?: { s: number; kind: string; key?: string; class?: string; rating?: string; offbeat?: string }[];
  /** Logs with press classes: the groove (0–1) and the tier after each change. */
  groove?: { s: number[]; v: number[] };
  tier?: { s: number[]; v: number[] };
  /** The hero's path every few hundredths of song time, for its speed. */
  heroPath?: HeroPath;
  /** Grid sounds: the sounds moved to a later slot, known too late for their own. */
  soundMoves?: SoundMove[];
  /** Gems on the field: pickups per sixteenth, vacuums, and the experience dropped and collected. */
  gems?: GemLog;
  /** The upgrade pool: each level-up's offers and the one taken. */
  upgrades?: UpgradeLog[];
  /** The small health pool: hurts and heals. */
  health?: HealthLog;
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
   * or the simulation's outcome (stride, stomp, rock, extra, bend; older logs have others).
   */
  outcome: string;
  /** Press classes: the class (partner, offbeat, counted, double, stray), the groove before and after, and the tier after. */
  class?: string;
  grooveBefore?: number;
  grooveAfter?: number;
  tier?: number;
  /** Re-rated under the current rules (rerate): the class and rating there, when the summary re-rates. */
  rerated?: { class: string; rating?: string; tier: number };
  /** Strides: the stride's length, the budget left after it, and the ground an interrupted stride gave back (px). */
  stride?: number;
  budget?: number;
  returned?: number;
  section?: string;
  gridDeltaMs?: number;
  rating?: string;
  /** Extra presses: ms since the press that took the slot, and the off-beat rating and offset. */
  sinceStepMs?: number;
  offbeat?: string;
  offbeatDeltaMs?: number;
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
        row.stride = t.stride;
        row.budget = t.budget;
        row.class = t.class;
        row.grooveBefore = t.grooveBefore;
        row.grooveAfter = t.grooveAfter;
        row.tier = t.tier;
        // (An off-beat's rating is its off-beat rating.)
        if (t.class === "offbeat") row.rating = t.offbeat;
        row.returned = t.returned;
        if (t.outcome === "extra" && t.stepS !== undefined) row.sinceStepMs = (t.s - t.stepS) * 1000;
        if (t.outcome === "extra") {
          row.offbeat = t.offbeat;
          if (t.offbeatDelta !== undefined) row.offbeatDeltaMs = t.offbeatDelta * 1000;
        }
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
  /** Re-rate the presses under these rules (the current game's), beside what the log recorded. */
  rerate?: RerateRules;
}

/** A press rated on its own: a stride or a stomp, or a step in logs from before strides. */
const counted = (outcome: string) => outcome === "stride" || outcome === "stomp" || outcome === "step";

/** A plain-text report. */
export function summarize(log: PlayLogFile, opts: SummaryOptions = {}): string {
  const out: string[] = [];
  const say = (s = "") => out.push(s);
  const inRange = (s?: number) => s === undefined || ((opts.from === undefined || s >= opts.from) && (opts.to === undefined || s <= opts.to));
  const rows = pressRows(log).filter((r) => inRange(r.s));
  const rerated = opts.rerate ? rerate(log, opts.rerate) : undefined;
  if (rerated) {
    const byId = new Map(rerated.presses.filter((p) => p.id !== undefined).map((p) => [p.id!, p]));
    for (const r of rows) {
      const p = byId.get(r.id);
      if (p) r.rerated = { class: p.class, ...(p.rating ? { rating: p.rating } : {}), tier: p.tier };
    }
  }

  say(`Play log ${log.id} (saved ${log.savedAt})`);
  if (log.userAgent) say(`  browser: ${log.userAgent}`);
  say(`  level ${log.level ?? "?"}, start ${log.params?.startS ?? 0}s, ?avOffsetMs=${log.params?.avOffsetMs ?? 0}, calibration offset ${log.inputOffsetMs ?? 0} ms`);
  if (log.audio) say(`  audio: ${log.audio.sampleRate} Hz, baseLatency ${f(log.audio.baseLatency * 1000, 1)} ms, outputLatency ${f(log.audio.outputLatency * 1000, 1)} ms`);
  if (log.hero) say(`  windows: perfect ±${log.hero.timing.perfectMs} great ±${log.hero.timing.greatMs} good ±${log.hero.timing.goodMs} ms`);
  if (log.hero?.stride) say(`  strides: ${log.hero.walk?.speed ?? "?"} px/s, ${log.hero.stride.durationMs} ms each; the stride period is the grid step, halved above ${log.hero.stride.maxPeriodS} s (${log.hero.stride.freePeriodS} s free)`);
  say();

  // Outcomes.
  const counts = new Map<string, number>();
  for (const r of rows) counts.set(r.outcome, (counts.get(r.outcome) ?? 0) + 1);
  say(`Presses: ${rows.length}`);
  for (const [k, n] of [...counts].sort((a, b) => b[1] - a[1])) say(`  ${pad(k, 22)} ${lpad(String(n), 5)}`);
  const ratings = new Map<string, number>();
  for (const r of rows) if (counted(r.outcome) && r.rating) ratings.set(r.rating, (ratings.get(r.rating) ?? 0) + 1);
  if (ratings.size > 0) say(`  rated presses: ${[...ratings].map(([k, n]) => `${k} ${n}`).join(", ")}`);
  const offbeats = new Map<string, number>();
  for (const r of rows) if (r.outcome === "extra") offbeats.set(r.offbeat ?? "(none)", (offbeats.get(r.offbeat ?? "(none)") ?? 0) + 1);
  if (offbeats.size > 0) say(`  extra presses' off-beat ratings: ${[...offbeats].map(([k, n]) => `${k} ${n}`).join(", ")}`);
  const strides = rows.filter((r) => r.stride !== undefined && r.outcome !== "bend").map((r) => r.stride!);
  if (strides.length > 0) say(`  strides: median ${f(median(strides), 1)} px (min ${f(Math.min(...strides), 1)}, max ${f(Math.max(...strides), 1)}); ${rows.filter((r) => (r.returned ?? 0) > 0).length} interrupted the stride before`);
  say();

  // Press classes and time at each tier, per section; the speed over each second.
  for (const line of grooveReport(log, rows, rerated, opts)) say(line);
  say();

  // Timing offsets.
  const grid = rows.filter((r) => r.gridDeltaMs !== undefined).map((r) => r.gridDeltaMs!);
  const beat = rows.filter((r) => counted(r.outcome) && r.beatDeltaMs !== undefined).map((r) => r.beatDeltaMs!);
  if (grid.length > 0) {
    say(`Offset from the nearest grid time (rated presses and extra presses, + is late): median ${f(median(grid), 1)} ms, mean ${f(mean(grid), 1)}, sd ${f(sd(grid), 1)}`);
    const bins = [-250, -150, -100, -60, -30, 0, 30, 60, 100, 150, 250];
    const hist = new Array(bins.length + 1).fill(0);
    for (const v of grid) hist[bins.findIndex((b) => v < b) === -1 ? bins.length : bins.findIndex((b) => v < b)]++;
    const labels = ["<-250", ...bins.slice(1).map((b, i) => `${bins[i]}..${b}`), ">250"];
    say(`  ${labels.map((l, i) => `${l}: ${hist[i]}`).filter((_, i) => hist[i] > 0).join("  ")}`);
  }
  if (beat.length > 0) say(`Offset of rated presses from the nearest beat: median ${f(median(beat), 1)} ms, mean ${f(mean(beat), 1)}, sd ${f(sd(beat), 1)}`);
  const extra = rows.filter((r) => r.outcome === "extra" && r.sinceStepMs !== undefined).map((r) => r.sinceStepMs!);
  if (extra.length > 0) say(`Extra presses came ${f(median(extra))} ms (median) after the rated press for their grid time`);
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
  // Gems on the field: collected against dropped, pickups per sixteenth, and the vacuums.
  if (log.gems) {
    const gl = log.gems;
    const pick = gl.pickups.filter((p) => inRange(p.s));
    say(`Gems: ${gl.collected} of ${gl.dropped} experience dropped collected (${gl.dropped ? Math.round((100 * gl.collected) / gl.dropped) : 0}%), on ${pick.length} sixteenths (most on one: ${Math.max(0, ...pick.map((p) => p.n))} gems)`);
    for (const v of gl.vacuums.filter((x) => inRange(x.s))) say(`  vacuum at ${f(v.s, 2)}: ${v.sTouch !== undefined ? `touched at ${f(v.sTouch, 2)}, ${v.gems ?? 0} gems in view flew in` : "never touched"}`);
  }
  // The small health pool: hurts and heals per section, and the heals by source.
  if (log.health) {
    const secOf = (s: number) => (log.sections ?? []).findLast((x) => s >= x.s0 - 1e-9)?.label ?? "?";
    const by = new Map<string, { bites: number; damage: number; heal: number }>();
    for (const b of log.health.bites.filter((x) => inRange(x.s))) {
      const r = by.get(secOf(b.s)) ?? { bites: 0, damage: 0, heal: 0 };
      r.bites++;
      r.damage += b.damage;
      by.set(secOf(b.s), r);
    }
    for (const h of log.health.heals.filter((x) => inRange(x.s))) {
      const r = by.get(secOf(h.s)) ?? { bites: 0, damage: 0, heal: 0 };
      r.heal += h.amount;
      by.set(secOf(h.s), r);
    }
    const bites = log.health.bites.filter((x) => inRange(x.s));
    say(`Health: ${bites.length} hurts for ${bites.reduce((a, b) => a + b.damage, 0)} damage; healed ${log.health.heals.filter((x) => inRange(x.s)).reduce((a, h) => a + h.amount, 0)}`);
    // (In song order.)
    const order = (label: string) => (log.sections ?? []).findIndex((x) => x.label === label);
    for (const [label, r] of [...by].sort((a, b) => order(a[0]) - order(b[0]))) say(`  ${label}: ${r.bites} hurts, ${r.damage} damage, ${r.heal} healed`);
    const src = new Map<string, number>();
    for (const h of log.health.heals.filter((x) => inRange(x.s))) src.set(h.source, (src.get(h.source) ?? 0) + h.amount);
    if (src.size > 0) say(`  heals by source: ${[...src].map(([k, v]) => `${k} ${v}`).join(", ")}`);
  }
  // The upgrade pool: what each level-up offered and took.
  for (const u of (log.upgrades ?? []).filter((x) => inRange(x.s))) {
    const how = u.sTaken === undefined ? "" : u.walked ? `, walked into at ${f(u.sTaken, 2)}, applied at ${f(u.sApply!, 2)}` : `, the orb ahead after two bars, applied at ${f(u.sApply!, 2)}`;
    say(`Level-up at ${f(u.s, 2)}: took ${u.offers[u.taken] ?? "nothing yet"}${how} (offered ${u.offers.join(", ")})`);
  }
  // Grid sounds: each sound known less than 60 ms before its slot moved to a later one.
  const moves = (log.soundMoves ?? []).filter((m) => inRange(m.from));
  if (moves.length > 0) {
    const byKey = new Map<string, number>();
    for (const m of moves) byKey.set(m.key, (byKey.get(m.key) ?? 0) + 1);
    say(`Sounds moved to a later slot (known less than 60 ms before their own): ${moves.length} (${[...byKey].map(([k, n]) => `${k} ${n}`).join(", ")})`);
    if (opts.table !== false) for (const m of moves) say(`  ${pad(m.key, 18)} ${lpad(f(m.from, 3), 8)} → ${f(m.s, 3)} (+${f((m.s - m.from) * 1000)} ms)`);
  }
  const events = log.events.filter((e) => e.kind !== "flush");
  if (events.length > 0) say(`Events: ${events.map((e) => `${e.kind}${e.s !== undefined ? `@${f(e.s, 2)}` : ""}`).join(" ")}`);

  if (opts.table !== false) {
    say();
    say("   id      s   key    beat   Δbeat  Δgrid  outcome        class    rating   groove     tier  stride/left  notes");
    for (const r of rows) {
      const notes: string[] = [];
      if (r.sinceStepMs !== undefined) notes.push(`${f(r.sinceStepMs)} ms after the slot's rated press`);
      if ((r.returned ?? 0) > 0) notes.push(`interrupted a stride (${f(r.returned, 1)} px back)`);
      if (r.offbeatDeltaMs !== undefined) notes.push(`off-beat ${r.offbeat ?? "unrated"} (${f(r.offbeatDeltaMs)} ms)`);
      if (r.rollback) notes.push("rollback");
      if (r.late) notes.push("LATE (applied at settled time)");
      if (r.runs !== undefined && r.runs > 1) notes.push(`ran ${r.runs}×`);
      if (r.handlerLagMs > 20) notes.push(`handler +${f(r.handlerLagMs)} ms`);
      if (r.rerated && (r.rerated.class !== r.class || r.rerated.rating !== (r.class ? r.rating : undefined))) notes.push(`re-rated: ${r.rerated.class}${r.rerated.rating ? ` ${r.rerated.rating}` : ""} (tier ${r.rerated.tier})`);
      if (r.section) notes.push(r.section);
      const groove = r.grooveBefore !== undefined ? `${f(r.grooveBefore, 2)}→${f(r.grooveAfter ?? r.grooveBefore, 2)}` : "";
      const stride = r.stride !== undefined || r.budget !== undefined ? `${f(r.stride, 1)}/${f(r.budget, 1)}` : "";
      say(
        `${lpad(String(r.id), 5)} ${lpad(f(r.s, 3), 8)}  ${pad(r.key, 5)} ${lpad(r.beat !== undefined ? String(r.beat) : "", 6)} ${lpad(f(r.beatDeltaMs), 6)} ${lpad(f(r.gridDeltaMs), 6)}  ${pad(r.outcome, 14)} ${pad(r.class ?? "", 8)} ${pad(r.rating ?? "", 8)} ${pad(groove, 10)} ${lpad(r.tier !== undefined ? String(r.tier) : "", 4)}  ${lpad(stride, 11)}  ${notes.join("; ")}`,
      );
    }
  }
  return out.join("\n");
}

/** What re-rating needs from the current game: the hero (its timing and meter) and the song's grid, beats and sections. */
export interface RerateRules {
  hero: Pick<HeroDef, "timing" | "meter">;
  /** Grid times (song seconds, sorted), from the song map. */
  grid: ArrayLike<number>;
  /** Beat times, for the drain. */
  beats: ArrayLike<number>;
  /** The current level's sections: pulsed (steps) or free, and their beats per bar. */
  sections: { label: string; s0: number; s1: number; movement: string; beatsPerBar: number }[];
}

/** A press re-rated under the current rules. */
export interface ReratedPress {
  id?: number;
  s: number;
  section: string;
  key: string;
  class: string;
  rating?: string;
  change: number;
  groove: number;
  tier: number;
}

export interface Rerated {
  /** From the player's first song time (the hand-over, or the start) to the end of play. */
  from: number;
  to: number;
  /** The groove re-rating starts from: the log's own at `from` when it has one, else 0. */
  groove0: number;
  presses: ReratedPress[];
  groove: { s: number[]; v: number[] };
  tier: { s: number[]; v: number[] };
}

/** The song time where the player's play starts and ends in a log: the hand-over (or start), to the last frame played or press. */
export function playSpan(log: PlayLogFile): { from: number; to: number } {
  const fr = log.frames;
  const playing = fr.phases.indexOf("playing");
  const from = log.playerFrom ?? log.params?.startS ?? 0;
  // The final attempt: frames after the last restart, and the presses that survived it.
  const restart = lastRestart(log);
  let to = restart?.s ?? -Infinity;
  for (let i = 0; i < fr.s.length; i++) if (fr.phase[i] === playing && (!restart || fr.perf[i] > restart.perf)) to = Math.max(to, fr.s[i]);
  const { kept } = finalPresses(log);
  for (const t of log.traces) if (t.id !== undefined && kept.has(t.id)) to = Math.max(to, t.s);
  return { from, to: Number.isFinite(to) ? to : from };
}

function lastRestart(log: PlayLogFile): { perf: number; s?: number } | undefined {
  return log.events.filter((e) => e.kind === "restartSection" || e.kind === "restartSong").sort((a, b) => a.perf - b.perf).at(-1);
}

/**
 * The presses of the log's final timeline, by trace id: a restart rewinds the recording (a section
 * restart to a snapshot at or before the section's start, a song restart to the start), so the
 * presses made before it from there on are gone from the recording, and from the groove and tier
 * it logged. Every press the recording kept is among its settled inputs (same song time and key);
 * a log without them falls back on the restart events' song times.
 */
export function finalPresses(log: PlayLogFile): { kept: Set<number>; dropped: number } {
  if (log.inputs) {
    // One input for each trace: the latest attempt's press claims it first (ids rise with key presses).
    const settled = log.inputs.filter((i) => i.kind === "press");
    const used = new Set<number>();
    const kept = new Set<number>();
    let dropped = 0;
    const presses = log.traces.filter((t) => t.kind === "press" && t.id !== undefined).sort((a, b) => b.id! - a.id!);
    for (const t of presses) {
      const k = settled.findIndex((i, n) => !used.has(n) && Math.abs(i.s - t.s) < 1e-5 && (i.key === undefined || i.key === t.key));
      if (k >= 0) {
        used.add(k);
        kept.add(t.id!);
      } else dropped++;
    }
    return { kept, dropped };
  }
  const perf = new Map(log.keys.map((k) => [k.id, k.timeStamp]));
  const restarts = log.events.filter((e) => (e.kind === "restartSection" || e.kind === "restartSong") && e.s !== undefined).sort((a, b) => a.perf - b.perf);
  const kept = new Set<number>();
  let dropped = 0;
  for (const t of log.traces) {
    if (t.kind !== "press" || t.id === undefined) continue;
    const at = perf.get(t.id) ?? -Infinity;
    // Gone when a later restart rewound to a song time at or before it.
    if (restarts.some((e) => e.perf > at && (e.kind === "restartSong" || t.s >= e.s! - 1e-9))) dropped++;
    else kept.add(t.id);
  }
  return { kept, dropped };
}

/** The value of a step series (each value holds from its time to the next) at song time s. */
function stepAt(series: { s: ArrayLike<number>; v: ArrayLike<number> }, s: number, before = 0): number {
  let v = before;
  for (let i = 0; i < series.s.length && series.s[i] <= s + 1e-9; i++) v = series.v[i];
  return v;
}

/**
 * Re-rate a log's presses under the current groove rules: every press of the player's in a pulsed
 * section is classed against the song map's grid (a diagonal's second key and a stomp's opposite
 * key, as the log recorded them, are partners), the groove changes by the current meter, drains a
 * bar after the latest press that raised it, and sets the tier. Logs recorded before press classes
 * (step movement, one rated press per grid slot) re-rate the same way.
 */
export function rerate(log: PlayLogFile, rules: RerateRules): Rerated {
  const { hero, grid, beats } = rules;
  const { from, to } = playSpan(log);
  const sectionAt = (s: number) => rules.sections.findLast((x) => s >= x.s0 - 1e-9) ?? rules.sections[0];
  // A press up to the Good window before a pulsed section's first grid time belongs to that section,
  // as the simulation has it.
  const pressSectionAt = (s: number) => {
    const i = rules.sections.findLastIndex((x) => s >= x.s0 - 1e-9);
    const sec = rules.sections[Math.max(0, i)];
    const next = rules.sections[i + 1];
    if (sec.movement === "steps" || next?.movement !== "steps") return sec;
    const first = Array.from(grid).find((g) => g >= next.s0 - 1e-6);
    return first !== undefined && first - s <= hero.timing.goodMs / 1000 + 1e-9 ? next : sec;
  };
  const { kept } = finalPresses(log);
  const presses = log.traces.filter((t) => t.kind === "press" && t.id !== undefined && kept.has(t.id) && t.s >= from - 1e-9 && pressSectionAt(t.s).movement === "steps").sort((a, b) => a.s - b.s);
  const groove0 = log.groove ? stepAt(log.groove, from) : 0;
  let groove = groove0;
  // (From the tier the log had at the hand-over: a tier drops only below its hysteresis.)
  let tier = tierFor(hero as HeroDef, log.tier ? stepAt(log.tier, from) : 0, groove);
  // The drain's bar runs from the latest press before the hand-over that raised the groove under
  // the meter the log was played under (the virtual player's: a press at a full groove still
  // counts), or else from the hand-over.
  let lastRaise = groove > 0 ? from : -Infinity;
  const played = log.hero?.meter;
  if (played && log.inputs) {
    const raised = (i: NonNullable<PlayLogFile["inputs"]>[number]) =>
      i.class === "counted" ? played.counted[(i.rating ?? "loose") as keyof typeof played.counted] > 0 : i.class === "offbeat" ? played.offbeat[(i.offbeat ?? "great") as keyof typeof played.offbeat] > 0 : false;
    const last = log.inputs.findLast((i) => i.kind === "press" && i.s <= from + 1e-9 && raised(i));
    if (last) lastRaise = last.s;
  }
  const out: Rerated = { from, to, groove0, presses: [], groove: { s: [from], v: [groove] }, tier: { s: [from], v: [tier] } };
  const set = (s: number, g: number) => {
    if (g !== groove) {
      groove = g;
      out.groove.s.push(s);
      out.groove.v.push(g);
    }
    const t = tierFor(hero as HeroDef, tier, groove);
    if (t !== tier) {
      tier = t;
      out.tier.s.push(s);
      out.tier.v.push(t);
    }
  };
  // The classes remember the virtual player's last presses before the hand-over (a press of the
  // player's soon after one of them can be a double tap or an off-beat).
  const memo = freshPressMemo();
  for (const i of log.inputs ?? []) {
    if (i.kind !== "press" || i.s >= from - 1e-9 || i.s < from - 2 || pressSectionAt(i.s).movement !== "steps") continue;
    classifyPress(hero as HeroDef, grid, i.s, i.class === "partner", memo);
  }
  let bi = 0;
  while (bi < beats.length && beats[bi] < from - 1e-9) bi++;
  for (let pi = 0; pi <= presses.length; pi++) {
    const s = pi < presses.length ? presses[pi].s : to;
    // The drain on each beat of a pulsed section up to this press.
    for (; bi + 1 < beats.length && beats[bi] < s - 1e-9 && beats[bi] <= to; bi++) {
      const sec = sectionAt(beats[bi]);
      const beatS = beats[bi + 1] - beats[bi];
      if (sec.movement === "steps" && groove > 0 && beats[bi] - lastRaise > hero.meter.drain.afterBars * sec.beatsPerBar * beatS + 1e-9) set(beats[bi], Math.max(0, groove - drainPerBeat(hero as HeroDef, sec.beatsPerBar)));
    }
    if (pi === presses.length) break;
    const p = presses[pi];
    const partner = p.class === "partner" || p.outcome === "bend" || p.outcome === "rock";
    const j = classifyPress(hero as HeroDef, grid, p.s, partner, memo);
    if (j.change > 0) lastRaise = p.s;
    set(p.s, grooveAfterChange(groove, j.change));
    out.presses.push({ id: p.id, s: p.s, section: pressSectionAt(p.s).label, key: p.key, class: j.class, ...(j.rating ? { rating: j.rating } : {}), change: j.change, groove, tier });
  }
  return out;
}

export const CLASSES = ["partner", "offbeat", "counted", "double", "stray"] as const;

/** Seconds at each tier (0–3) in each pulsed section between from and to, from a tier series. */
export function tierTime(tier: { s: ArrayLike<number>; v: ArrayLike<number> }, sections: readonly { label: string; s0: number; s1: number; movement: string }[], from: number, to: number): Map<string, number[]> {
  const out = new Map<string, number[]>();
  for (const sec of sections) {
    if (sec.movement !== "steps") continue;
    const a = Math.max(sec.s0, from);
    const b = Math.min(sec.s1, to);
    if (b <= a) continue;
    const secs = [0, 0, 0, 0];
    let s = a;
    let t = stepAt(tier, a);
    for (let i = 0; i < tier.s.length; i++) {
      if (tier.s[i] <= a) continue;
      if (tier.s[i] >= b) break;
      secs[Math.min(3, t)] += tier.s[i] - s;
      s = tier.s[i];
      t = tier.v[i];
    }
    secs[Math.min(3, t)] += b - s;
    out.set(sec.label, secs);
  }
  return out;
}

/** Presses by class in each section. */
export function classesBySection(presses: readonly { section?: string; class?: string }[]): Map<string, Record<string, number>> {
  const out = new Map<string, Record<string, number>>();
  for (const p of presses) {
    if (!p.class || !p.section) continue;
    const row = out.get(p.section) ?? Object.fromEntries(CLASSES.map((c) => [c, 0]));
    row[p.class] = (row[p.class] ?? 0) + 1;
    out.set(p.section, row);
  }
  return out;
}

/** The hero's speed over each second of song time: path length over the world time it took (px per world second). */
export function speedPerSecond(path: HeroPath): { s: number; speed: number; px: number }[] {
  const out: { s: number; speed: number; px: number }[] = [];
  const per = Math.max(1, Math.round(1 / path.dt));
  for (let k = 0; k + per < path.x.length; k += per) {
    let px = 0;
    for (let i = k; i < k + per; i++) px += Math.hypot(path.x[i + 1] - path.x[i], path.y[i + 1] - path.y[i]);
    const dw = path.w[k + per] - path.w[k];
    out.push({ s: path.s0 + k * path.dt, speed: dw > 1e-6 ? px / dw : 0, px });
  }
  return out;
}

/** The average of a step series over the pulsed sections between from and to. */
function averageOver(series: { s: ArrayLike<number>; v: ArrayLike<number> }, sections: readonly { s0: number; s1: number; movement: string }[], from: number, to: number): number | undefined {
  let sum = 0;
  let n = 0;
  for (const sec of sections) {
    if (sec.movement !== "steps") continue;
    for (let s = Math.max(sec.s0, from); s < Math.min(sec.s1, to); s += 0.05) {
      sum += stepAt(series, s);
      n++;
    }
  }
  return n > 0 ? sum / n : undefined;
}

/** The press-class and tier table per section, recorded and re-rated, and the hero's speed per second. */
function grooveReport(log: PlayLogFile, rows: readonly PressRow[], rerated: Rerated | undefined, opts: SummaryOptions): string[] {
  const out: string[] = [];
  const { from: spanFrom, to: spanTo } = rerated ?? playSpan(log);
  const from = Math.max(spanFrom, opts.from ?? -Infinity);
  const to = Math.min(spanTo, opts.to ?? Infinity);
  const sections = log.sections ?? opts.rerate?.sections ?? [];
  const table = (title: string, classes: Map<string, Record<string, number>>, tiers: Map<string, number[]> | undefined) => {
    out.push(title);
    out.push(`  ${pad("section", 16)} ${CLASSES.map((c) => lpad(c, 8)).join("")}   ${tiers ? ["tier 0", "1", "2", "3"].map((t) => lpad(t, 7)).join("") + "  (seconds)" : ""}`);
    for (const sec of sections) {
      const c = classes.get(sec.label);
      const t = tiers?.get(sec.label);
      if (!c && !t) continue;
      out.push(`  ${pad(sec.label, 16)} ${CLASSES.map((k) => lpad(String(c?.[k] ?? 0), 8)).join("")}   ${t ? t.map((x) => lpad(f(x, 1), 7)).join("") : ""}`);
    }
  };
  const { kept, dropped } = finalPresses(log);
  const mine = rows.filter((r) => r.s !== undefined && r.s >= from - 1e-9 && r.s <= to + 1e-9 && kept.has(r.id));
  const recordedClasses = classesBySection(mine);
  // The recorded tiers: the log's series, or else the tier after each press (drains between presses unseen).
  const tierSeries = log.tier ?? (mine.some((r) => r.tier !== undefined) ? { s: mine.filter((r) => r.tier !== undefined).map((r) => r.s!), v: mine.filter((r) => r.tier !== undefined).map((r) => r.tier!) } : undefined);
  if (recordedClasses.size > 0 || tierSeries) {
    table(`Press classes and time at each tier, per section (recorded${log.tier ? "" : tierSeries ? "; tiers from the presses alone" : ""}):`, recordedClasses, tierSeries ? tierTime(tierSeries, sections, from, to) : undefined);
    if (log.groove) out.push(`  average groove over pulsed time: ${f(averageOver(log.groove, sections, from, to), 2)}`);
  } else out.push("Press classes: none recorded (a log from before press classes; re-rate it with the current rules)");
  if (dropped > 0) out.push(`  (${dropped} presses that a later restart rewound are left out, here and in re-rating)`);
  if (rerated && opts.rerate) {
    const m = opts.rerate.hero.meter;
    const t = opts.rerate.hero.timing;
    out.push("");
    const presses = rerated.presses.filter((p) => p.s >= from - 1e-9 && p.s <= to + 1e-9);
    table(
      `Re-rated under the current rules (Perfect ±${t.perfectMs}, Great ±${t.greatMs}, Good ±${t.goodMs} ms; counted ${m.counted.perfect}/${m.counted.great}/${m.counted.good}/${m.counted.loose}, off-beat ${m.offbeat.perfect}/${m.offbeat.great}, stray ${m.stray}; tiers at ${m.tiers.join("/")}), from groove ${f(rerated.groove0, 2)} at ${f(rerated.from, 1)} s:`,
      classesBySection(presses),
      tierTime(rerated.tier, sections, from, to),
    );
    const avg = averageOver(rerated.groove, sections, from, to);
    if (avg !== undefined) out.push(`  average groove over pulsed time: ${f(avg, 2)}`);
    const changed = new Map<string, number>();
    for (const r of mine) if (r.rerated && r.class && r.rerated.class !== r.class) changed.set(`${r.class}→${r.rerated.class}`, (changed.get(`${r.class}→${r.rerated.class}`) ?? 0) + 1);
    if (changed.size > 0) out.push(`  classes changed: ${[...changed].map(([k, n]) => `${k} ${n}`).join(", ")}`);
  }
  // The hero's speed over each second.
  if (log.heroPath) {
    const walk = log.hero?.walk?.speed;
    const secs = speedPerSecond(log.heroPath).filter((x) => x.s >= from - 1e-9 && x.s + 1 <= to + 1e-9);
    out.push("");
    out.push(`Speed (px per world second, over each second of song time${walk ? `; the walk speed is ${walk}` : ""}):`);
    for (const sec of sections) {
      const v = secs.filter((x) => x.s >= sec.s0 - 1e-9 && x.s < sec.s1).map((x) => x.speed);
      if (v.length === 0) continue;
      out.push(`  ${pad(sec.label, 16)} median ${lpad(f(median(v)), 4)}, max ${lpad(f(Math.max(...v)), 4)}${walk ? `; ${v.filter((x) => x > walk * 1.05).length} s above it, ${v.filter((x) => x < 1).length} s standing` : ""}`);
    }
    if (opts.table !== false && secs.length > 0) {
      for (let k = 0; k < secs.length; k += 10) out.push(`  ${secs.slice(k, k + 10).map((x) => `${lpad(f(x.s), 4)}s ${lpad(f(x.speed), 3)}`).join("  ")}`);
    }
  } else out.push("", "Speed: no hero path in this log (logs from before it carry none)");
  return out;
}
