/**
 * Beat steps for enemies in reactive mode, like the monsters of Crypt of the NecroDancer: an enemy
 * with a gait walks in quick steps, each starting on one of its gait's times and easing out over
 * `share` of the time to the next, and stands still in between (in play mode and the demo it glides). A step covers the enemy's speed times that time, so over
 * a beat it walks as far as it would gliding.
 *
 * Pure functions of the song's beat index. The simulation records each step it takes, and the
 * renderer plants the feet from those.
 */
import type { EnemyDef } from "../content/schema.ts";

export type Gait = NonNullable<EnemyDef["gait"]>;

/** A gait's times per beat. */
export const gaitPerBeat = (g: Gait): number => (g.every === "eighth" ? 2 : 1);

/** The index of the gait time at or before beat index b: its step, counted from the song's first beat. */
export const gaitSlot = (g: Gait, b: number): number => Math.floor(b * gaitPerBeat(g) + 1e-9);

/** The beat index of gait time k. */
export const gaitBeat = (g: Gait, k: number): number => k / gaitPerBeat(g);
