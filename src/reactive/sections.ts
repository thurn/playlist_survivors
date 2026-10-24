/**
 * Reactive mode's detected sections (stage 4; docs/design/reactive-audio.md, "Detected sections"):
 * a new section starts on a change, or on a loudness jump of 6 dB or more, whose evidence at its
 * knownAt is at or above the gate, provided the previous start's knownAt was at least 8 s earlier.
 * It takes effect on the effective downbeat after the event's knownAt. Whether a start happens
 * depends only on stream entries known by its knownAt; its downbeat (`s`, `beat`) may be a beat not
 * yet committed then (the first to be committed after it), so the simulation may act on a start at
 * its downbeat, but must not read `s` before that beat is known. A start past the last committed
 * beat is dropped.
 */
import { effectiveDownbeat, evidenceAt, type ReactiveStream } from "./stream.ts";

export interface DetectedSection {
  /** The effective downbeat it takes effect on (a committed beat's time), and that beat's index. */
  s: number;
  beat: number;
  /** When it became known (its event's knownAt), the event's kind and time. */
  knownAt: number;
  source: "change" | "jump";
  eventT: number;
}

export interface SectionRule {
  /** The least loudness jump (dB, either way) that starts a section. */
  jumpDb: number;
  /** The least time between starts' knownAt. */
  minGapS: number;
}

export const SECTION_RULE: SectionRule = { jumpDb: 6, minGapS: 8 };

/** Every detected section start in the stream, in order (none without structure). */
export function detectedSections(stream: ReactiveStream, rule: SectionRule = SECTION_RULE): DetectedSection[] {
  const st = stream.structure;
  if (!st) return [];
  const events = [
    ...st.changes.map((c) => ({ knownAt: c.knownAt, t: c.t, source: "change" as const })),
    ...st.jumps.filter((j) => Math.abs(j.db) >= rule.jumpDb - 1e-9).map((j) => ({ knownAt: j.knownAt, t: j.t, source: "jump" as const })),
  ].sort((a, b) => a.knownAt - b.knownAt || (a.source === b.source ? 0 : a.source === "change" ? -1 : 1));
  const out: DetectedSection[] = [];
  const gate = stream.params.gate;
  for (const e of events) {
    if (evidenceAt(stream, e.knownAt) < gate) continue;
    const prev = out[out.length - 1];
    if (prev && e.knownAt - prev.knownAt < rule.minGapS - 1e-9) continue;
    const beat = effectiveDownbeat(stream, e.knownAt);
    if (beat < 0) continue;
    out.push({ s: stream.beats[beat].t, beat, knownAt: e.knownAt, source: e.source, eventT: e.t });
  }
  return out;
}
