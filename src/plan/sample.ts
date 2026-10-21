/** Sampling helpers shared by evaluation and the planner's output checks. */
import type { EnemyRecord, Plan } from "./types.ts";

/** Enemy position at world time w: Catmull-Rom over the 30 Hz samples. */
export function enemyPos(plan: Plan, e: EnemyRecord, w: number, out: { x: number; y: number }): { x: number; y: number } {
  const s = plan.enemySamples;
  const n = e.sampleCount;
  if (n === 0) {
    out.x = e.fates[0]?.x ?? 0;
    out.y = e.fates[0]?.y ?? 0;
    return out;
  }
  const f = (w - e.sampleW0) * plan.enemySampleRate;
  if (f <= 0) {
    out.x = s[e.sampleOffset * 2];
    out.y = s[e.sampleOffset * 2 + 1];
    return out;
  }
  if (f >= n - 1) {
    const k = (e.sampleOffset + n - 1) * 2;
    out.x = s[k];
    out.y = s[k + 1];
    return out;
  }
  const i = Math.floor(f);
  const t = f - i;
  const i0 = Math.max(0, i - 1);
  const i2 = Math.min(n - 1, i + 1);
  const i3 = Math.min(n - 1, i + 2);
  const b = e.sampleOffset * 2;
  out.x = catmull(s[b + i0 * 2], s[b + i * 2], s[b + i2 * 2], s[b + i3 * 2], t);
  out.y = catmull(s[b + i0 * 2 + 1], s[b + i * 2 + 1], s[b + i2 * 2 + 1], s[b + i3 * 2 + 1], t);
  return out;
}

function catmull(p0: number, p1: number, p2: number, p3: number, t: number): number {
  const t2 = t * t;
  const t3 = t2 * t;
  return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
}

/** Ids of enemies drawable at world time w (alive or in their death fall). */
export function enemiesNear(plan: Plan, w: number): Uint32Array {
  const b = Math.max(0, Math.min(plan.activeEnemies.length - 1, Math.floor(w / plan.activeBucket)));
  return plan.activeEnemies[b];
}

/** Count of sorted values ≤ x. */
export function countUpTo(arr: ArrayLike<number>, x: number): number {
  let lo = 0;
  let hi = arr.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (arr[mid] <= x) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

export function heroSample(plan: Plan, w: number) {
  const t = plan.hero;
  const f = Math.max(0, (w - t.w0) * t.rate);
  const k = Math.min(t.x.length - 2, Math.floor(f));
  const u = Math.min(1, f - k);
  const kn = u < 0.5 ? k : k + 1;
  return {
    x: t.x[k] + (t.x[k + 1] - t.x[k]) * u,
    y: t.y[k] + (t.y[k + 1] - t.y[k]) * u,
    z: t.z[k] + (t.z[k + 1] - t.z[k]) * u,
    facing: t.facing[kn],
    anim: t.animNames[t.anim[kn]],
    frame: t.frame[kn],
    trail: t.trail[kn],
  };
}
