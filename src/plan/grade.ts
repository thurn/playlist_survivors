/**
 * Play mode's letter grade: from the average groove over the step sections.
 * The thresholds are tuned so the `moderate` virtual player earns an A.
 */

export type Grade = "S" | "A" | "B" | "C";

export const GRADE_THRESHOLDS: { grade: Grade; min: number }[] = [
  { grade: "S", min: 0.85 },
  { grade: "A", min: 0.7 },
  { grade: "B", min: 0.5 },
];

export function gradeFor(averageGroove: number): Grade {
  return GRADE_THRESHOLDS.find((t) => averageGroove >= t.min)?.grade ?? "C";
}

/** Better grades sort first. */
export function gradeRank(g: Grade): number {
  return ["S", "A", "B", "C"].indexOf(g);
}
