/**
 * Reactive mode's grade (docs/design/reactive-audio.md, "The reactive level"): the run ends when
 * the audio does, and its end card grades the average groove over judged time, the song time after
 * the first 16 s at which presses are judged (the beat clear, for a second at least).
 */
import { grooveAt } from "../plan/evaluate.ts";
import { judgedAt, type ReactiveStream } from "./stream.ts";

/** Judged time starts this far into the song. */
export const JUDGED_FROM_S = 16;
/** The end card comes up this long after the audio ends. */
export const REACTIVE_END_DELAY = 0.5;
/** The track's title card, from its index entry. */
export const REACTIVE_TITLE = { s: 0.5, until: 5 };

/** The average groove over judged time in [from, to), sampled every 0.25 s (0 with none). */
export function judgedAverageGroove(groove: { s: ArrayLike<number>; v: ArrayLike<number> }, stream: ReactiveStream, from: number, to: number): number {
  let sum = 0;
  let n = 0;
  for (let t = Math.max(JUDGED_FROM_S, from); t < to; t += 0.25) {
    if (!judgedAt(stream, t)) continue;
    sum += grooveAt(groove, t);
    n++;
  }
  return n > 0 ? sum / n : 0;
}
