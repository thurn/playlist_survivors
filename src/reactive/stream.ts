/**
 * Reactive mode's belief stream: what a causal analysis knows of a track over
 * time, written by `ps-analysis stream` (tools/analysis/src/ps_analysis/stream.py).
 * Every entry becomes known at its `knownAt` (a tick at its own time), and the
 * game may read an entry only from then on; `check:reactive` proves it. Fields
 * are keyed by presence: a stage adds its fields, and the game reads the ones
 * its stage needs. See docs/design/reactive-audio.md, "The Belief Stream".
 */
import { z } from "zod";

const beat = z.object({ t: z.number(), knownAt: z.number(), period: z.number().positive(), evidence: z.number() });
/** Stage 4 adds each tick's loudness level and, from 10 s on, its key (such as "D major"). */
const tick = z.object({ t: z.number(), evidence: z.number(), energy: z.number(), level: z.enum(["quiet", "medium", "loud"]).optional(), key: z.string().optional() });
const onset = z.object({ t: z.number(), knownAt: z.number(), strength: z.number(), strong: z.boolean() });
const section = z.object({ label: z.string(), s0: z.number(), s1: z.number(), feel: z.enum(["pulsed", "free"]).optional() });

export const ReferenceSchema = z.object({
  source: z.enum(["songmap", "exact", "offline"]),
  beats: z.array(z.number()),
  sections: z.array(section).optional(),
  accents: z.array(z.object({ t: z.number(), label: z.string() })).optional(),
});

export const StreamFileSchema = z.object({
  version: z.literal(1),
  audio: z.object({ file: z.string(), duration: z.number().positive(), sampleRate: z.number().int().positive() }),
  params: z.looseObject({ gate: z.number(), tickS: z.number().positive(), toolVersion: z.number().int() }),
  beats: z.array(beat).min(2),
  ticks: z.array(tick),
  onsets: z.array(onset),
  /** Stage 2: every second, the offline decode of the last 20 s and the causal tracker's believed beats, each over the last 8 s. */
  hindsight: z.array(z.object({ knownAt: z.number(), offline: z.array(z.number()), online: z.array(z.number()) })).optional(),
  /**
   * Stage 4: changes (novelty peaks, known 6 s late), loudness jumps of 6 dB or more (known at the
   * end of their second) and candidate endings (known 1.5 s into the quiet).
   */
  structure: z
    .object({
      changes: z.array(z.object({ t: z.number(), knownAt: z.number(), strength: z.number() })),
      jumps: z.array(z.object({ t: z.number(), knownAt: z.number(), db: z.number() })),
      ends: z.array(z.object({ t: z.number(), knownAt: z.number() })),
    })
    .optional(),
  /**
   * Stage 5: accent forecasts, strong onsets repeating on a 2-, 4- or 8-beat cycle of committed
   * beats, forecast on the next cycle's beat; `confidence` is the share of the last 4 cycles with
   * the hit.
   */
  accents: z.object({ forecast: z.array(z.object({ t: z.number(), knownAt: z.number(), cycle: z.number().int().positive(), confidence: z.number() })) }).optional(),
  reference: ReferenceSchema.optional(),
});

export type StreamBeat = z.infer<typeof beat>;
export type StreamTick = z.infer<typeof tick>;
export type StreamOnset = z.infer<typeof onset>;
export type Reference = z.infer<typeof ReferenceSchema>;
export type StreamStructure = NonNullable<z.infer<typeof StreamFileSchema>["structure"]>;
export type TickLevel = NonNullable<StreamTick["level"]>;
export type StreamForecast = NonNullable<z.infer<typeof StreamFileSchema>["accents"]>["forecast"][number];
export type StreamFile = z.infer<typeof StreamFileSchema>;

/** The stream the simulation sees: the file without its reference (which only players, reports and checks read). */
export type ReactiveStream = Omit<StreamFile, "reference">;

export function parseStream(json: unknown): StreamFile {
  return StreamFileSchema.parse(json);
}

/** The stream without its reference, and the reference. */
export function splitStream(file: StreamFile): { stream: ReactiveStream; reference?: Reference } {
  const { reference, ...stream } = file;
  return { stream, reference };
}

/** Index of the latest tick at or before song time s (-1 before the first). */
export function tickIndexAt(stream: ReactiveStream, s: number): number {
  const ticks = stream.ticks;
  let lo = 0;
  let hi = ticks.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (ticks[mid].t <= s + 1e-9) lo = mid + 1;
    else hi = mid;
  }
  return lo - 1;
}

/** How long the beat must have been clear before presses are judged again (hv-s4pt.12). */
export const JUDGE_AFTER_S = 1;

export interface Judging {
  /** Per tick: is the beat judged from it to the next (clear at it and at every tick of the JUDGE_AFTER_S before)? */
  judged: Uint8Array;
  /** Per judged tick: when its run of judged ticks began. */
  since: Float64Array;
  /** Per tick: when the latest run of judging that followed a stretch of `LOCK_GAP_S` or more without it began (NaN before any). */
  lockOn: Float64Array;
  /** Per unjudged tick: when its stretch without judging began. */
  unjudgedSince: Float64Array;
}

/** A stretch without judging this long or longer makes the next run of judging a lock-on. */
export const LOCK_GAP_S = 4;

const judgingMemo = new WeakMap<ReactiveStream, Judging>();

/**
 * When the beat is judged: judging stops at the first tick below the gate and resumes once the beat
 * has been clear for JUDGE_AFTER_S, so a wobble around the gate does not flip presses between judged
 * and unjudged. Each tick's answer reads only ticks at or before it.
 */
export function judging(stream: ReactiveStream): Judging {
  const memo = judgingMemo.get(stream);
  if (memo) return memo;
  const ticks = stream.ticks;
  const gate = stream.params.gate;
  const judged = new Uint8Array(ticks.length);
  const since = new Float64Array(ticks.length).fill(NaN);
  const lockOn = new Float64Array(ticks.length).fill(NaN);
  const unjudgedSince = new Float64Array(ticks.length).fill(NaN);
  let clearFrom = NaN;
  let lastJudged = 0;
  let lock = NaN;
  for (let j = 0; j < ticks.length; j++) {
    if (ticks[j].evidence < gate) {
      clearFrom = NaN;
      continue;
    }
    if (Number.isNaN(clearFrom)) clearFrom = ticks[j].t;
    if (ticks[j].t - clearFrom >= JUDGE_AFTER_S - 1e-9) {
      judged[j] = 1;
      const run = j > 0 && judged[j - 1] === 1;
      since[j] = run ? since[j - 1] : ticks[j].t;
      if (!run && ticks[j].t - lastJudged >= LOCK_GAP_S - 1e-9) lock = ticks[j].t;
    }
    if (judged[j]) lastJudged = j + 1 < ticks.length ? ticks[j + 1].t : ticks[j].t;
    lockOn[j] = lock;
  }
  for (let j = 0; j < ticks.length; j++) if (!judged[j]) unjudgedSince[j] = j > 0 && !judged[j - 1] ? unjudgedSince[j - 1] : j > 0 ? ticks[j].t : 0;
  const out = { judged, since, lockOn, unjudgedSince };
  judgingMemo.set(stream, out);
  return out;
}

/** Is the beat judged at song time s (see judging)? */
export function judgedAt(stream: ReactiveStream, s: number): boolean {
  const i = tickIndexAt(stream, s);
  return i >= 0 && judging(stream).judged[i] === 1;
}

/** When the run of judging that song time s falls in began (NaN when not judged). */
export function judgedSinceAt(stream: ReactiveStream, s: number): number {
  const i = tickIndexAt(stream, s);
  return i >= 0 ? judging(stream).since[i] : NaN;
}

/** When the latest lock-on (a run of judging after LOCK_GAP_S or more without) began, as of song time s (NaN before any). */
export function lockOnAt(stream: ReactiveStream, s: number): number {
  const i = tickIndexAt(stream, s);
  return i >= 0 ? judging(stream).lockOn[i] : NaN;
}

/** When the stretch without judging that song time s falls in began (NaN when judged; 0 before the first tick). */
export function unjudgedSinceAt(stream: ReactiveStream, s: number): number {
  const i = tickIndexAt(stream, s);
  return i >= 0 ? judging(stream).unjudgedSince[i] : 0;
}

/** The evidence at song time s: the latest tick's at or before it (0 before the first). */
export function evidenceAt(stream: ReactiveStream, s: number): number {
  const i = tickIndexAt(stream, s);
  return i < 0 ? 0 : stream.ticks[i].evidence;
}

/** The energy at song time s: the latest tick's at or before it (0 before the first). */
export function energyAt(stream: ReactiveStream, s: number): number {
  const i = tickIndexAt(stream, s);
  return i < 0 ? 0 : stream.ticks[i].energy;
}

/** Index of the latest committed beat at or before song time s (-1 before the first). */
export function beatIndexAtOrBefore(stream: ReactiveStream, s: number): number {
  const beats = stream.beats;
  let lo = 0;
  let hi = beats.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (beats[mid].t <= s + 1e-6) lo = mid + 1;
    else hi = mid;
  }
  return lo - 1;
}

/**
 * The first committed beat at or after song time t. "Among the beats known at k, or the first to be
 * committed if none is" comes to the same: beats are committed in time order, so the first beat at
 * or after t is either known at k or the next to be committed.
 */
export function firstBeatAtOrAfter(stream: ReactiveStream, t: number): StreamBeat | undefined {
  const i = beatIndexAtOrBefore(stream, t - 2e-6) + 1;
  return stream.beats[i];
}

/** Seconds of notice a downbeat effect needs after its trigger is known. */
export const DOWNBEAT_NOTICE = 0.35;

/**
 * The effective downbeat after an event known at song time k: the first committed beat on a bar's
 * first beat (2-beat bars counted from the first committed beat) at least DOWNBEAT_NOTICE after k,
 * among the beats known at k, or, if none is known yet, the first such beat to be committed. Beats
 * are committed in time order, so both come to the first such beat. Its index, or -1 past the last.
 */
export function effectiveDownbeat(stream: ReactiveStream, k: number, beatsPerBar = 2): number {
  let i = beatIndexAtOrBefore(stream, k + DOWNBEAT_NOTICE - 2e-6) + 1;
  if (i % beatsPerBar !== 0) i += beatsPerBar - (i % beatsPerBar);
  return i < stream.beats.length ? i : -1;
}

/** The latest hindsight entry known at song time s (undefined before the first, or without hindsight). */
export function hindsightAt(stream: ReactiveStream, s: number): NonNullable<ReactiveStream["hindsight"]>[number] | undefined {
  const h = stream.hindsight;
  if (!h) return undefined;
  let lo = 0;
  let hi = h.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (h[mid].knownAt <= s + 1e-9) lo = mid + 1;
    else hi = mid;
  }
  return lo > 0 ? h[lo - 1] : undefined;
}
