/**
 * Beat steps for enemies in reactive mode, like the monsters of Crypt of the NecroDancer: an enemy
 * with a gait walks in quick steps and stands still in between (in play mode and the demo it
 * glides). Its gait's times fall on every eighth note or beat, counted from the song's first beat,
 * and its pattern says which of them it steps on, repeating: "x" steps on every one, "x." on every
 * other, ".x" on the ones between. Each step starts on its time and eases out over `share` of the
 * time to the next gait time, and every step covers the same ground (its speed times the pattern's
 * time over its steps), so over a pattern the enemy walks as far as it would gliding.
 *
 * Pure functions of the song's beat index. The simulation records each step it takes, and the
 * renderer plants the feet from those.
 */
import type { EnemyDef } from "../content/schema.ts";

export type Gait = NonNullable<EnemyDef["gait"]>;

/** A gait's times per beat. */
export const gaitPerBeat = (g: Gait): number => (g.every === "eighth" ? 2 : 1);

/** The index of the gait time at or before beat index b. */
export const gaitSlot = (g: Gait, b: number): number => Math.floor(b * gaitPerBeat(g) + 1e-9);

/** The beat index of gait time k. */
export const gaitBeat = (g: Gait, k: number): number => k / gaitPerBeat(g);

const place = (g: Gait, k: number): number => ((k % g.pattern.length) + g.pattern.length) % g.pattern.length;

/** Does the enemy step on gait time k? */
export const gaitSteps = (g: Gait, k: number): boolean => g.pattern[place(g, k)] === "x";

/** Steps in one pass of the pattern. */
const stepsPer = (g: Gait): number => g.pattern.split("x").length - 1;

/** The gait time of the enemy's latest step at or before gait time k. */
export function gaitStepAtOrBefore(g: Gait, k: number): number {
  let j = k;
  while (!gaitSteps(g, j)) j--;
  return j;
}

/** The gait time of the enemy's first step after gait time k. */
export function gaitStepAfter(g: Gait, k: number): number {
  let j = k + 1;
  while (!gaitSteps(g, j)) j++;
  return j;
}

/** Gait times of ground each step covers: the pattern's length over its steps. */
export const gaitGround = (g: Gait): number => g.pattern.length / stepsPer(g);
