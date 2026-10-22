/**
 * Play mode's letter grade: from the average groove over the step sections.
 * The thresholds are tuned so the `moderate` virtual player earns an A (its
 * average groove is 0.96–0.97 on the checked seeds; the perfect and skilled
 * players reach 0.978, the casual player 0.72–0.80).
 */

export type Grade = "S" | "A" | "B" | "C";

export const GRADE_THRESHOLDS: { grade: Grade; min: number }[] = [
  { grade: "S", min: 0.972 },
  { grade: "A", min: 0.9 },
  { grade: "B", min: 0.7 },
];

export function gradeFor(averageGroove: number): Grade {
  return GRADE_THRESHOLDS.find((t) => averageGroove >= t.min)?.grade ?? "C";
}

/** Better grades sort first. */
export function gradeRank(g: Grade): number {
  return ["S", "A", "B", "C"].indexOf(g);
}
