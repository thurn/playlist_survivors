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
const tick = z.object({ t: z.number(), evidence: z.number(), energy: z.number() });
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
  reference: ReferenceSchema.optional(),
});

export type StreamBeat = z.infer<typeof beat>;
export type StreamTick = z.infer<typeof tick>;
export type StreamOnset = z.infer<typeof onset>;
export type Reference = z.infer<typeof ReferenceSchema>;
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
