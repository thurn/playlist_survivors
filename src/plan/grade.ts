/**
 * Play mode's letter grade: from the average groove over the step sections.
 * The thresholds are tuned to the groove meter (press classes): on the checked
 * seeds the perfect and skilled players average 0.94–0.95, the `moderate`
 * player 0.89–0.92 (an A), and mashers 0.11–0.70 (a C). The casual player
 * (downbeats only, 120 ms deviation) averages 0.08–0.13, a C until its profile
 * and the groove values are retuned (the design wants a B).
 */

export type Grade = "S" | "A" | "B" | "C";

export const GRADE_THRESHOLDS: { grade: Grade; min: number }[] = [
  { grade: "S", min: 0.94 },
  { grade: "A", min: 0.85 },
  { grade: "B", min: 0.75 },
];

export function gradeFor(averageGroove: number): Grade {
  return GRADE_THRESHOLDS.find((t) => averageGroove >= t.min)?.grade ?? "C";
}

/** Better grades sort first. */
export function gradeRank(g: Grade): number {
  return ["S", "A", "B", "C"].indexOf(g);
}
