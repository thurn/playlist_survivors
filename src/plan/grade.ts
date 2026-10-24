/**
 * The letter grade: from the average groove over the step sections.
 * The thresholds are tuned to the groove meter (press classes) so the design's
 * players earn their grades: on the checked seeds the perfect, skilled and
 * eighths players average 0.94–0.98 (an S), the `moderate` player 0.90–0.93
 * (an A), the casual player (downbeats only, 60 ms deviation) 0.63–0.83 (a B),
 * and the holder and mashers 0.03–0.16 (a C).
 *
 * Reactive mode grades the meter as shown, beatless stretches filling it (docs/design/reactive-
 * audio.md, stage 7), so its honest players sit near 1: on the Galop over two seeds
 * reactive-moderate and reactive-skilled average 0.991–0.993 (an A; an S takes a near-flawless
 * 0.995), reactive-human 0.90–0.96 (an A), and the holder and mashers 0.39–0.63 (a C).
 */

export type Grade = "S" | "A" | "B" | "C";

export const GRADE_THRESHOLDS: { grade: Grade; min: number }[] = [
  { grade: "S", min: 0.94 },
  { grade: "A", min: 0.85 },
  { grade: "B", min: 0.5 },
];

export const REACTIVE_GRADE_THRESHOLDS: { grade: Grade; min: number }[] = [
  { grade: "S", min: 0.995 },
  { grade: "A", min: 0.85 },
  { grade: "B", min: 0.7 },
];

/** The grade of an average groove, by play mode's thresholds or (`reactive`) reactive mode's. */
export function gradeFor(averageGroove: number, reactive = false): Grade {
  return (reactive ? REACTIVE_GRADE_THRESHOLDS : GRADE_THRESHOLDS).find((t) => averageGroove >= t.min)?.grade ?? "C";
}

/** Better grades sort first. */
export function gradeRank(g: Grade): number {
  return ["S", "A", "B", "C"].indexOf(g);
}
